"""CT Island v2 dressing (World lane): buildings on street lots, street furniture, trees, bushes, rocks and crops.

Reads islandgen's outputs (out/roads.json, plan.json, height.r16, forest.png, ground.png) and the processed kit
manifests (art/processed/world, art/processed/environment); writes out/dress.json, which the Unity builder
(CTLife.EditorTools.IslandV2.IslandV2Dress) turns into per-cell GPU-instance data, colliders and markers.

Run (repo root), after islandgen.py:
  uv run --no-project --with pillow==12.3.0 --with scipy==1.18.1 --with numpy==2.4.2 python -I tools/world/islandgen/dress.py

Variety rule (v2 art direction): on a street side no model repeats within four lots; setbacks, gaps and heights jitter;
facade colours (Quaternius kit) never repeat next door; downtown heights follow a skyline with a tall core round Timeline
Square that tapers to mid-rise and low edges; trees vary species, size and rotation. Everything is seeded, so a rebuild
gives the same island.
"""
import json
import math
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

import geom as G
import layout as L

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(HERE, 'out')
WORLD_MAN = os.path.join(REPO, 'art', 'processed', 'world', 'manifest.json')
ENV_MAN = os.path.join(REPO, 'art', 'processed', 'environment', 'manifest.json')
RNG = np.random.default_rng(20261017)

# Which way each kit's facade (door side) faces in model space: yaw offset added so the facade looks at the street.
FRONT = {'qtb': 0.0, 'kc': 0.0, 'ks': 0.0, 'ki': 0.0, 'env': 0.0}

FACADES = [(0.93, 0.87, 0.74), (0.92, 0.62, 0.52), (0.80, 0.46, 0.34), (0.62, 0.82, 0.70), (0.58, 0.74, 0.88),
           (0.72, 0.66, 0.86), (0.96, 0.84, 0.52), (0.86, 0.76, 0.60), (0.66, 0.74, 0.58), (0.90, 0.66, 0.72),
           (0.52, 0.58, 0.66), (0.95, 0.95, 0.93), (0.68, 0.36, 0.28), (0.36, 0.62, 0.64), (0.84, 0.64, 0.32),
           (0.98, 0.76, 0.62), (0.46, 0.52, 0.72)]


def log(*a):
    print('[dress]', *a, flush=True)


# ------------------------------------------------------------------ inputs

class Ground:
    """The carved heightfield islandgen wrote (bilinear samples, slopes)."""

    def __init__(self):
        t = json.load(open(os.path.join(OUT, 'terrain.json')))
        self.n, self.cell, self.min = t['samples'], t['cell'], t['min']
        raw = np.fromfile(os.path.join(OUT, 'height.r16'), dtype='<u2').reshape(self.n, self.n).astype(np.float64)
        self.h = t['hmin'] + raw / 65535.0 * (t['hmax'] - t['hmin'])
        gz, gx = np.gradient(self.h, self.cell)
        self.slope = np.hypot(gx, gz)
        self.forest = self._png('forest.png')[..., 0] if os.path.exists(os.path.join(OUT, 'forest.png')) else np.zeros_like(self.h)
        g = self._png('ground.png')
        self.rock, self.dirt, self.talus = g[..., 0], g[..., 1], g[..., 2]

    def _png(self, name):
        a = np.asarray(Image.open(os.path.join(OUT, name)), dtype=np.float64) / 255.0
        a = a[::-1]                                       # PNG row 0 = north; grids are row 0 = south
        return a if a.ndim == 3 else a[..., None]

    def _fxz(self, x, z):
        return (np.asarray(x, float) - self.min[0]) / self.cell, (np.asarray(z, float) - self.min[1]) / self.cell

    def sample(self, field, x, z):
        fx, fz = self._fxz(x, z)
        return ndimage.map_coordinates(field, [np.atleast_1d(fz), np.atleast_1d(fx)], order=1, mode='nearest')

    def y(self, x, z):
        return self.sample(self.h, x, z)


class Occupancy:
    """1 m raster of what is taken: roads, junctions, venues, water, buildings, props (no overlaps, no trees on roads)."""
    X0, Z0, W, H = -960.0, -800.0, 1920, 1560
    FREE, ROAD, VENUE, WATER, BUILDING, PARK, FIELD, PROP, TREE = 0, 1, 2, 3, 4, 5, 6, 7, 8

    def __init__(self):
        self.img = Image.new('L', (self.W, self.H), 0)
        self.draw = ImageDraw.Draw(self.img)
        self.grid = None

    def px(self, x, z):
        return (x - self.X0, self.H - 1 - (z - self.Z0))

    def poly(self, pts, code):
        self.draw.polygon([self.px(x, z) for x, z in pts], fill=code)
        self.grid = None

    def line(self, pts, width, code):
        self.draw.line([self.px(x, z) for x, z in pts], fill=code, width=max(1, int(round(width))), joint='curve')
        self.grid = None

    def disc(self, x, z, r, code):
        cx, cy = self.px(x, z)
        self.draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=code)
        self.grid = None

    def arr(self):
        if self.grid is None:
            self.grid = np.asarray(self.img).copy()
        return self.grid

    def codes(self, xs, zs):
        g = self.arr()
        ix = np.clip(np.round(np.asarray(xs) - self.X0).astype(int), 0, self.W - 1)
        iy = np.clip(np.round(self.H - 1 - (np.asarray(zs) - self.Z0)).astype(int), 0, self.H - 1)
        return g[iy, ix]

    def rect_free(self, cx, cz, w, d, yaw, margin=0.6, allow=(0,)):
        """True when every metre of the rotated rectangle (plus margin) is in an allowed class."""
        a = math.radians(yaw)
        ca, sa = math.cos(a), math.sin(a)
        us = np.arange(-w / 2 - margin, w / 2 + margin + 0.01, 1.0)
        vs = np.arange(-d / 2 - margin, d / 2 + margin + 0.01, 1.0)
        U, V = np.meshgrid(us, vs)
        x = cx + U * ca + V * sa
        z = cz - U * sa + V * ca
        c = self.codes(x.ravel(), z.ravel())
        return bool(np.isin(c, allow).all())

    def rect(self, cx, cz, w, d, yaw, code, margin=0.0):
        self.poly(G.oriented_rect(cx, cz, w + 2 * margin, d + 2 * margin, yaw), code)


# ------------------------------------------------------------------ kit

class Kit:
    def __init__(self):
        self.items = {}
        for path, kitname in ((WORLD_MAN, None), (ENV_MAN, 'env')):
            if not os.path.exists(path):
                continue
            for k, v in json.load(open(path)).items():
                kit = kitname or next((t[4:] for t in v.get('tags', []) if t.startswith('kit:')), k.split('_')[0])
                sx, sy_, sz = v['size_m']                     # Blender x (width), y (depth), z (height)
                self.items[k] = dict(id=k, w=sx, d=sy_, h=sz, kit=kit, tags=set(v.get('tags', [])), tris=v['tris'][0])

    def get(self, k):
        return self.items[k]

    def ids(self, prefix=None, tag=None, hmin=0.0, hmax=1e9):
        return [k for k, v in self.items.items() if (prefix is None or k.startswith(prefix)) and (tag is None or tag in v['tags'])
                and hmin <= v['h'] <= hmax]


# ------------------------------------------------------------------ the dressing

class Dress:
    def __init__(self):
        t0 = time.time()
        self.g = Ground()
        self.kit = Kit()
        self.roads = json.load(open(os.path.join(OUT, 'roads.json')))
        self.plan = json.load(open(os.path.join(OUT, 'plan.json')))
        self.occ = Occupancy()
        self.inst = {}                 # proto id -> list of [x, y, z, yaw, sxz, sy, r, g, b, a]
        self.boxes = []                # building colliders [cx, cy, cz, w, h, d, yaw]
        self.box_ids = []              # the kit model of each box (W4 wall dressing)
        self.signals = []              # traffic light heads
        self.shop_signs = []           # W3 blade signs [x, y, z, facade yaw, atlas cell]
        self.tree_pits = []            # W4 street tree pits [x, y, z, yaw] (Unity builds the grate and soil)
        self.bus_shelters = []         # W4 [x, y, z, yaw (open side faces the road), ad poster]
        self.wall_decor = []           # W4 dressing on exposed side and back walls [x, y, z, yaw, w, h, kind, variant]
        self.hero_plazas = []          # W4 paved clearings round the statues [x, y, z, radius, statue yaw]
        self.power = []                # power line pole runs
        self.billboards = []           # live ad boards
        self.heroes = []               # W3 statues
        self.stats = {}
        self.segs = {s['id']: s for s in self.roads['segments']}
        self.districts = [(d['id'], np.array(d['poly'], float)) for d in self.plan['districts']]
        log(f'inputs: {len(self.kit.items)} kit models, {len(self.segs)} segments ({time.time() - t0:.1f}s)')

    # -------------------------------------------------------------- helpers
    def add(self, proto, x, y, z, yaw, s=1.0, sy=1.0, tint=None):
        t = tint or (0, 0, 0, 0)
        self.inst.setdefault(proto, []).append([round(float(x), 2), round(float(y), 2), round(float(z), 2), round(float(yaw) % 360.0, 1),
                                                round(float(s), 3), round(float(sy), 3)] + [round(float(c), 3) for c in t])

    def district_at(self, x, z):
        for did, poly in self.districts:
            if G.point_in_poly(x, z, poly):
                return did
        return None

    DISTRICT_CODES = {'timeline': 1, 'residences': 2, 'builder_hills': 3, 'trenches': 4, 'dubai_marina': 5, 'boardwalk': 6,
                      'breakpoint': 7, 'airdrop_farm': 8, 'mountain': 9, 'islet': 10}

    def district_raster(self):
        """District code per 4 m cell over the domain (0 = none)."""
        img = Image.new('L', (512, 512), 0)
        dr = ImageDraw.Draw(img)
        for did, poly in reversed(self.districts):
            code = self.DISTRICT_CODES.get(did, 0)
            dr.polygon([((x + 1024) / 4, (z + 1024) / 4) for x, z in poly], fill=code)
        return np.asarray(img)                              # row = z index (south first, as drawn)

    @staticmethod
    def frame(samples):
        p = np.array([[q[0], q[2]] for q in samples])
        y = np.array([q[1] for q in samples])
        tan = G.tangents(p) if len(p) > 1 else np.array([[1.0, 0.0]])
        return p, y, tan, G.right_normals(tan)

    # -------------------------------------------------------------- what is taken
    def stamp_static(self):
        o = self.occ
        # water and beaches: low ground near the sea, and the sea itself
        h = self.g.h
        n = self.g.n
        xs = self.g.min[0] + np.arange(n) * self.g.cell
        wet = (h < L.SEA + 1.4)
        img = Image.fromarray((wet[::-1] * 255).astype(np.uint8))
        # paste the 2 m water mask into the 1 m occupancy (scaled)
        x0, z1 = self.g.min[0], self.g.min[1] + (n - 1) * self.g.cell
        big = img.resize((int(n * self.g.cell), int(n * self.g.cell)), Image.Resampling.NEAREST)
        ox, oy = int(round(x0 - o.X0)), int(round((o.Z0 + o.H) - z1))
        mask = Image.new('L', (o.W, o.H), 0)
        mask.paste(big, (ox, oy))
        o.img.paste(Occupancy.WATER, mask=mask)
        o.grid = None
        # rivers and lakes with their banks
        rv = self.plan['river']
        pts = [(p[0], p[2]) for p in rv['points']]
        for (a, b), hw in zip(zip(pts[:-1], pts[1:]), rv['halfWidth'][:-1]):
            o.line([a, b], 2 * hw + 10, Occupancy.WATER)
        for lake in self.plan['lakes']:
            o.disc(lake['center'][0], lake['center'][1], lake['radius'] + 6, Occupancy.WATER)
        # parks and fields (no buildings; parks get their own trees, fields their crops)
        for name, poly in L.PARKS:
            o.poly([tuple(q) for q in poly], Occupancy.PARK)
        o.poly([tuple(q) for q in L.dt_block_poly(*L.DT_PLAZA[1])], Occupancy.PARK)     # Timeline Square plaza block stays open
        for poly, ang, kind in L.FIELDS:
            o.poly([tuple(q) for q in poly], Occupancy.FIELD)
        # roads: carriageway + sidewalks + a metre, and the junction surfaces
        for s in self.roads['segments']:
            p, y, tan, rn = self.frame(s['samples'])
            if len(p) < 2:
                continue
            wr, wl = s['widthRight'] + s['sidewalk'] - 0.5, s['widthLeft'] + s['sidewalk'] - 0.5
            under = s.get('underground') or [s['kind'] == 'tunnel'] * len(p)     # tunnel segments are open road outside the bore
            # one quad per sample step: a single long polygon self-overlaps on tight curves and PIL's fill then leaves
            # holes in the road (W4: trees and a statue were placed on the coast highway's bends)
            R_, L_ = p + rn * wr, p - rn * wl
            for i in range(len(p) - 1):
                if i + 1 < len(under) and under[i] and under[i + 1]:
                    continue
                o.poly([tuple(R_[i]), tuple(R_[i + 1]), tuple(L_[i + 1]), tuple(L_[i])], Occupancy.ROAD)
        for j in self.roads['intersections']:
            parts = j.get('parts') or [j]
            for part in parts:
                poly = [(q[0], q[2]) for q in part['polygon']]
                if len(poly) >= 3:
                    o.poly(poly, Occupancy.ROAD)
                    # a little extra round junctions (corner props and turning room)
                    c = np.mean(np.array(poly), axis=0)
                    if self.district_at(*c) != 'timeline':
                        o.poly([tuple(c + (np.array(q) - c) * 1.12) for q in poly], Occupancy.ROAD)
        # venues and homes (frame + 6 m), helipads
        for v in self.plan['venues'] + self.plan['homes']:
            mn, sz = v['frame_min'], v['frame_size']
            cx, cz = G.local_to_world(v['origin'][0], v['origin'][2], v['yaw'], mn[0] + sz[0] / 2, mn[1] + sz[1] / 2)
            o.rect(cx, cz, sz[0], sz[1], v['yaw'], Occupancy.VENUE, margin=6.0)
        for hp in self.plan['helipads']:
            o.disc(hp[0], hp[2], 15, Occupancy.VENUE)
        # landmark sites (W3 heroes stand here): kept clear of lots, props and forest
        clear = {'lighthouse': 26, 'jupiter_observatory': 36, 'superteam_hq': 30, 'seeker_shop': 12, 'helius_depot': 22, 'mtndao_summit': 16}
        for lm in L.LANDMARKS:
            r = clear.get(lm['id'])
            if r and lm.get('at'):
                o.disc(lm['at'][0], lm['at'][1], r, Occupancy.VENUE)
        # the bay boardwalk and the beach in front of it stay open (no buildings, no trees)
        for bw in self.plan.get('boardwalk', []):
            A = np.array(bw['inner'], float); B = np.array(bw['outer'], float)
            sea = B - A
            sea /= np.maximum(np.linalg.norm(sea, axis=1, keepdims=True), 1e-6)
            o.poly([tuple(q) for q in np.vstack([A, (B + sea * 45.0)[::-1]])], Occupancy.PARK)
            o.poly([tuple(q) for q in np.vstack([A, B[::-1]])], Occupancy.ROAD)
        log('occupancy stamped')

    # -------------------------------------------------------------- buildings
    def style_for(self, seg, x, z):
        d = self.district_at(x, z)
        k = seg['base']
        if seg['kind'] in ('bridge', 'tunnel') or k in ('mountain', 'dirt'):
            return None
        if d == 'timeline':
            r = math.hypot(x, z)
            return 'core' if r < 125 else ('mid' if r < 215 else 'edge')
        if d == 'trenches':
            return 'industrial'
        if d == 'dubai_marina':
            return 'marina'
        if d == 'boardwalk':
            return 'beach' if k != 'highway' else None
        if d in ('residences',):
            return 'suburb'
        if d == 'builder_hills':
            return 'hills'
        if d == 'airdrop_farm':
            return 'farm' if k != 'highway' else None
        return None

    def pool(self, style):
        K = self.kit
        qtb_low = K.ids('qtb_', hmax=8.6)
        qtb_mid = K.ids('qtb_', hmin=7.0)
        qtb_tall = K.ids('qtb_', hmin=10.0)
        kc_low = [k for k in K.ids('kc_building', hmax=11.5)]
        kc_mid = [k for k in K.ids('kc_building', hmin=10.0)]
        towers = K.ids('kc_tower')
        houses = K.ids('ks_house')
        sheds = K.ids('ki_building')
        if style == 'core':
            return dict(ids=towers * 6 + kc_mid * 2 + qtb_tall * 2 + ['qtb_6story_stack'] * 2, gap=(0.0, 0.5), setback=(0.6, 1.8),
                        scale=(1.2, 1.45), sy=(1.05, 1.6), slope=1.5, alley=0.08, city=True)
        if style == 'mid':
            return dict(ids=qtb_mid * 3 + kc_mid * 2 + towers, gap=(0.0, 0.6), setback=(0.6, 2.0), scale=(1.1, 1.3), sy=(1.0, 1.15),
                        slope=1.6, alley=0.1, city=True)
        if style == 'edge':
            return dict(ids=qtb_low * 2 + qtb_mid + kc_low * 2, gap=(0.0, 1.5), setback=(0.8, 3.0), scale=(1.05, 1.25), sy=(1.0, 1.0),
                        slope=1.8, alley=0.16, city=True)
        if style == 'beach':
            return dict(ids=qtb_low + kc_low + ['qtb_1story_roundroof', 'qtb_1story_gableroof', 'qtb_1story_sign'] * 2,
                        gap=(4.0, 12.0), setback=(2.0, 6.0), scale=(1.0, 1.2), sy=(1.0, 1.0), slope=2.0, alley=0.0)
        if style == 'industrial':
            return dict(ids=sheds * 2 + qtb_low, gap=(3.0, 9.0), setback=(2.0, 7.0), scale=(1.0, 1.15), sy=(1.0, 1.0), slope=2.2, alley=0.0)
        if style == 'marina':
            return dict(ids=towers * 3 + qtb_tall * 2 + kc_mid, gap=(2.0, 6.0), setback=(1.5, 4.0), scale=(1.2, 1.45), sy=(1.0, 1.4),
                        slope=1.8, alley=0.0)
        if style == 'suburb':
            return dict(ids=houses * 3 + ['qtb_2story_gableroof', 'qtb_2story_roundroof', 'qtb_1story_gableroof', 'qtb_2story_sidehouse'],
                        gap=(6.0, 14.0), setback=(4.5, 9.0), scale=(0.95, 1.1), sy=(1.0, 1.0), slope=4.0, alley=0.0, yawj=4.0)
        if style == 'hills':
            return dict(ids=houses * 2 + qtb_mid + ['qtb_2story_sidehouse', 'qtb_2story_wide'] * 2, gap=(7.0, 16.0),
                        setback=(4.0, 10.0), scale=(1.0, 1.15), sy=(1.0, 1.0), slope=4.0, alley=0.0, yawj=5.0)
        if style == 'farm':
            return dict(ids=houses + sheds, gap=(30.0, 70.0), setback=(8.0, 16.0), scale=(1.0, 1.1), sy=(1.0, 1.0), slope=3.0,
                        alley=0.0, yawj=6.0)
        return None

    def place_buildings(self):
        t0 = time.time()
        placed = 0
        why = {}
        for s in self.roads['segments']:
            p, y, tan, rn = self.frame(s['samples'])
            if len(p) < 4:
                continue
            sl = G.arc_lengths(p)
            mid = p[len(p) // 2]
            for side in (1.0, -1.0):
                edge = (s['widthRight'] if side > 0 else s['widthLeft']) + s['sidewalk']
                recent = []
                colours = []
                pos = 1.0 + RNG.uniform(0, 1.5)
                tries = 0
                while pos < sl[-1] - 2.0:
                    k = int(np.searchsorted(sl, pos))
                    k = min(max(k, 1), len(p) - 1)
                    style = self.style_for(s, *p[k])
                    pl = self.pool(style) if style else None
                    if pl is None or not pl['ids']:
                        why['nostyle'] = why.get('nostyle', 0) + 1
                        pos += 10.0
                        continue
                    choices = [c for c in pl['ids'] if c not in recent[-4:]] or pl['ids']
                    if tries:                                   # after a miss, try something narrower
                        cur = min(self.kit.get(c)['w'] for c in choices)
                        choices = [c for c in choices if self.kit.get(c)['w'] <= max(cur * 1.6, 7.0)] or choices
                    pid = choices[RNG.integers(len(choices))]
                    it = self.kit.get(pid)
                    sc = RNG.uniform(*pl['scale'])
                    sy = RNG.uniform(*pl['sy']) if 'tower' in pid else 1.0
                    w, d, hh = it['w'] * sc, it['d'] * sc, it['h'] * sc * sy
                    if pos + w > sl[-1] - 1.0:
                        why['end'] = why.get('end', 0) + 1
                        if tries < 3:
                            tries += 1
                            continue
                        break
                    cpos = pos + w / 2
                    kc = min(max(int(np.searchsorted(sl, cpos)), 1), len(p) - 1)
                    P, n_, t_ = p[kc], rn[kc] * side, tan[kc]
                    setback = RNG.uniform(*pl['setback'])
                    c = P + n_ * (edge + setback + d / 2)
                    # the facade faces the street: model front direction towards -n_
                    face = math.degrees(math.atan2(-n_[0], -n_[1]))
                    yaw = face + FRONT.get(it['kit'], 0.0) + RNG.uniform(-pl.get('yawj', 0.0), pl.get('yawj', 0.0))
                    ok = self.occ.rect_free(c[0], c[1], w, d, yaw, margin=0.12 if pl.get('city') else 0.4)
                    if not ok:
                        why['occupied'] = why.get('occupied', 0) + 1
                    if ok:
                        # foot: corners and centre heights
                        a = math.radians(yaw)
                        corners = [(c[0] + u * math.cos(a) + v * math.sin(a), c[1] - u * math.sin(a) + v * math.cos(a))
                                   for u in (-w / 2, 0, w / 2) for v in (-d / 2, 0, d / 2)]
                        ys = self.g.y([q[0] for q in corners], [q[1] for q in corners])
                        if ys.max() - ys.min() > pl['slope'] * (1 + max(w, d) / 20):
                            ok = False
                            why['slope'] = why.get('slope', 0) + 1
                    if not ok:
                        tries += 1
                        if tries >= 3:
                            pos += 1.5
                            tries = 0
                        continue
                    tries = 0
                    ybase = float(ys.min()) + 0.02
                    tint = None
                    if 'tint' in it['tags']:
                        cols = [q for q in range(len(FACADES)) if q not in colours[-2:]]
                        ci = cols[RNG.integers(len(cols))]
                        colours.append(ci)
                        tint = FACADES[ci] + (1.0,)
                    self.add(pid, c[0], ybase, c[1], yaw, sc, sy, tint)
                    self.boxes.append([round(float(c[0]), 2), round(ybase + hh / 2, 2), round(float(c[1]), 2), round(w, 2), round(hh, 2),
                                       round(d, 2), round(yaw % 360, 1)])
                    self.box_ids.append(pid)
                    self.occ.rect(c[0], c[1], w, d, yaw, Occupancy.BUILDING, margin=0.05)
                    self.shopfront(pid, it, c, yaw, w, d, ybase, -n_, t_, s, setback)
                    recent.append(pid)
                    placed += 1
                    gap = RNG.uniform(*pl['gap'])
                    if RNG.random() < pl['alley']:
                        gap += RNG.uniform(3.0, 6.0)         # alleys break the frontage
                    pos += w + gap
        self.stats['buildings'] = placed
        log(f'buildings: {placed} ({time.time() - t0:.1f}s); rejected {why}')

    def infill_blocks(self):
        """Downtown block interiors: second-row buildings aligned with the grid (bigger footprints, the skyline's height
        rule), so blocks read as built-up from the air; what stays open becomes courtyards."""
        t0 = time.time()
        K = self.kit
        poly = np.array(L.DOWNTOWN_POLY, float)
        grid_yaw = -L.DT_ROT
        cands = []
        for u in np.arange(-260, 260, 7.0):
            for v in np.arange(-260, 200, 7.0):
                x, z = L.dt_local_to_world(u, v)
                if G.point_in_poly(x, z, poly):
                    cands.append((x, z))
        order = RNG.permutation(len(cands))
        big = K.ids('kc_building', hmin=10.0) + K.ids('ki_building', hmax=13.0) + K.ids('qtb_', hmin=10.0)
        towers = K.ids('kc_tower')
        n = 0
        for i in order:
            x, z = cands[i]
            if self.occ.codes([x], [z])[0] != Occupancy.FREE:
                continue
            r = math.hypot(x, z)
            pool = towers * 5 + big if r < 125 else (big + towers[:3] if r < 215 else big)
            pid = pool[RNG.integers(len(pool))]
            it = K.get(pid)
            sc = RNG.uniform(1.1, 1.35)
            sy = RNG.uniform(1.1, 1.7) if ('tower' in pid and r < 125) else (RNG.uniform(1.0, 1.25) if 'tower' in pid else 1.0)
            if r >= 215 and it['h'] * sc > 16:
                continue
            w, d, hh = it['w'] * sc, it['d'] * sc, it['h'] * sc * sy
            yaw = grid_yaw + 90.0 * RNG.integers(4)
            if not self.occ.rect_free(x, z, w, d, yaw, margin=1.2):
                continue
            ys = self.g.y([x - w / 2, x + w / 2, x], [z - d / 2, z + d / 2, z])
            if ys.max() - ys.min() > 1.6:
                continue
            ybase = float(ys.min()) + 0.02
            tint = FACADES[RNG.integers(len(FACADES))] + (1.0,) if 'tint' in it['tags'] else None
            self.add(pid, x, ybase, z, yaw, sc, sy, tint)
            self.boxes.append([round(float(x), 2), round(ybase + hh / 2, 2), round(float(z), 2), round(w, 2), round(hh, 2), round(d, 2),
                               round(yaw % 360, 1)])
            self.box_ids.append(pid)
            self.occ.rect(x, z, w, d, yaw, Occupancy.BUILDING, margin=0.6)
            n += 1
        self.stats['infill_buildings'] = n
        log(f'infill: {n} block-interior buildings ({time.time() - t0:.1f}s)')

    def place_plaza(self):
        """Timeline Square's block: a paved plaza (built by Unity from plan.json) ringed with trees in planters, benches
        and double lamps, around the venue in the middle."""
        K = self.kit.items
        P = np.array(L.dt_block_poly(*L.DT_PLAZA[1]), float)
        c = P.mean(axis=0)
        inset = 7.0
        Q = c + (P - c) * (1.0 - inset / 45.0)
        n = 0
        for a, b in zip(Q, np.roll(Q, -1, axis=0)):
            L_ = float(np.linalg.norm(b - a))
            t = (b - a) / max(L_, 1e-6)
            face = math.degrees(math.atan2(*(c - (a + b) / 2)))
            for k, u in enumerate(np.arange(5.0, L_ - 4.0, 11.0)):
                q = a + t * u
                if self.occ.codes([q[0]], [q[1]])[0] == Occupancy.VENUE:
                    continue
                yq = float(self.g.y([q[0]], [q[1]])[0]) + 0.08
                if k % 3 == 1 and 'bench_city' in K:
                    self.add('bench_city', q[0], yq, q[1], face)
                elif self.pick(self.STREET):
                    tree = self.STREET[k % 3]
                    self.add(tree, q[0], yq, q[1], RNG.uniform(0, 360), RNG.uniform(0.75, 0.85), 1.0, self.foliage_tint(tree, q[0], q[1]))
                    if 'planter_round' in K:
                        self.add('planter_round', q[0], yq, q[1], 0.0, 1.6, 1.0)
                n += 1
        for q in Q:
            if 'street_lamp_double' in K:
                self.add('street_lamp_double', q[0], float(self.g.y([q[0]], [q[1]])[0]) + 0.08, q[1], 0.0)
        self.stats['plaza_props'] = n

    def place_billboards(self):
        """Roadside billboards on the coast highway and the country avenues (facing oncoming drivers), rooftop boards on
        tall downtown buildings near the avenues, and the Timeline screens landmark. The Unity builder gives each a live
        screen panel (Screen_bb_island_*), which LiveSurfaces rotates with house ads and live creatives."""
        boards = []
        idle = ['bb_01', 'bb_02', 'bb_studio']
        k = 0
        for s in self.roads['segments']:
            if not (s['base'] == 'highway' or s['route'] in ('farm_road', 'hills_drive', 'genesis_w', 'breakpoint_way', 'harbour_road')):
                continue
            if s['kind'] in ('bridge', 'tunnel'):
                continue
            p, y, tan, rn = self.frame(s['samples'])
            if len(p) < 8:
                continue
            sl = G.arc_lengths(p)
            pos = 90.0 + RNG.uniform(0, 60)
            side = 1.0 if RNG.random() < 0.5 else -1.0
            while pos < sl[-1] - 40:
                kk = min(max(int(np.searchsorted(sl, pos)), 1), len(p) - 1)
                edge = (s['widthRight'] if side > 0 else s['widthLeft']) + s['sidewalk'] + RNG.uniform(5.0, 8.0)
                q = p[kk] + rn[kk] * side * edge
                # traffic on this side drives along +tan (right side, forward lanes) or -tan (left side): face it
                travel = tan[kk] * (1.0 if side > 0 else -1.0)
                face = math.degrees(math.atan2(-travel[0], -travel[1])) + side * 18.0     # turned a little towards the road
                if self.occ.rect_free(q[0], q[1], 10.0, 3.0, face, margin=0.5, allow=(Occupancy.FREE, Occupancy.FIELD)):
                    yq = float(self.g.y([q[0]], [q[1]])[0])
                    boards.append(dict(kind='roadside', model='billboard_large', p=[round(float(q[0]), 2), round(yq, 2), round(float(q[1]), 2)],
                                       yaw=round(face % 360, 1), scale=1.35, screen=f'island_{k:02d}', idle=idle[k % 3]))
                    self.occ.rect(q[0], q[1], 10.0, 3.0, face, Occupancy.PROP)
                    k += 1
                    side = -side
                pos += RNG.uniform(200, 280)
        # rooftops: tall downtown buildings next to the avenues, the board facing the avenue
        avenues = [s for s in self.roads['segments'] if s['base'] == 'avenue' and self.district_at(*np.array(s['samples'][len(s['samples']) // 2])[[0, 2]]) == 'timeline']
        roof = 0
        order = RNG.permutation(len(self.boxes))
        for i in order:
            cx, cy, cz, w, hh, d, yaw = self.boxes[i]
            if hh < 16 or roof >= 7 or math.hypot(cx, cz) > 230:
                continue
            best = None
            for s in avenues:
                P = np.array([[q[0], q[2]] for q in s['samples']])
                dd = np.linalg.norm(P - np.array([cx, cz]), axis=1)
                j = int(np.argmin(dd))
                if dd[j] < 40 and (best is None or dd[j] < best[0]):
                    best = (dd[j], P[j])
            if best is None:
                continue
            to = best[1] - np.array([cx, cz])
            face = math.degrees(math.atan2(to[0], to[1]))
            face = yaw + round((face - yaw) / 90.0) * 90.0                     # square to the building
            boards.append(dict(kind='rooftop', model='billboard_rooftop', p=[cx, round(cy + hh / 2, 2), cz], yaw=round(face % 360, 1),
                               scale=1.7, screen=f'island_{k:02d}', idle=idle[k % 3]))
            k += 1
            roof += 1
        # the Timeline screens landmark: three big boards on the park's south edge, facing the plaza across Genesis Avenue
        lm = next((l for l in L.LANDMARKS if l['id'] == 'timeline_screens'), None)
        if lm:
            P = np.array(L.dt_block_poly(*L.DT_PARK[1]), float)
            a, b = P[0], P[1]                                                  # the park block's south edge
            t = (b - a) / np.linalg.norm(b - a)
            n = np.array([-t[1], t[0]])                                        # into the park
            face = math.degrees(math.atan2(-n[0], -n[1]))                       # looking south, at the plaza
            for j, u in enumerate((0.3, 0.5, 0.7)):
                q = a + (b - a) * u + n * 9.0
                yq = float(self.g.y([q[0]], [q[1]])[0])
                boards.append(dict(kind='landmark', model='billboard_large', p=[round(float(q[0]), 2), round(yq, 2), round(float(q[1]), 2)],
                                   yaw=round((face + (j - 1) * 12.0) % 360, 1), scale=1.6 if j == 1 else 1.35,
                                   screen=f'island_{k:02d}', idle=idle[j]))
                k += 1
        self.billboards = boards
        self.stats['billboards'] = len(boards)
        log(f'billboards: {len(boards)} ({sum(b["kind"] == "roadside" for b in boards)} roadside, {roof} rooftop)')

    def place_boats(self):
        """Moored boats along the marina and harbour quays (bow either way, parallel to the wall), sailing boats and a
        speedboat anchored in the bay, buoys off the beach, and ships far offshore (seen from the hills and the beach)."""
        K = self.kit.items
        water = L.SEA - 0.08
        n = 0
        marina = ['kw_boat_sail_a', 'kw_boat_sail_b', 'kw_boat_speed_a', 'kw_boat_speed_b', 'kw_boat_speed_c', 'kw_boat_speed_e',
                  'kw_boat_speed_g', 'kw_boat_house_a']
        harbour = ['kw_boat_fishing_small', 'kw_boat_fishing_small', 'kw_boat_tug_a', 'kw_boat_row_small', 'kw_boat_speed_c']
        for q in self.plan.get('quays', []):
            W = np.array(q['wall'], float)
            E = np.array(q['edge'], float)
            if len(W) < 6:
                continue
            pool = marina if W[:, 0].mean() > 0 else harbour
            sl = G.arc_lengths(W)
            pos = RNG.uniform(4, 10)
            recent = []
            while pos < sl[-1] - 6:
                k = min(max(int(np.searchsorted(sl, pos)), 1), len(W) - 1)
                t = W[k] - W[k - 1]
                t = t / max(np.linalg.norm(t), 1e-6)
                sea = W[k] - E[k]
                sea = sea / max(np.linalg.norm(sea), 1e-6)
                choices = [c for c in pool if c not in recent[-2:] and c in K] or [c for c in pool if c in K]
                if not choices:
                    break
                proto = choices[RNG.integers(len(choices))]
                it = K[proto]
                beam = it['w']
                c = W[k] + sea * (beam / 2 + 0.9)
                yaw = math.degrees(math.atan2(t[0], t[1])) + (180.0 if RNG.random() < 0.5 else 0.0)
                y = float(self.g.y([c[0]], [c[1]])[0])
                if y < L.SEA - 1.2:                                       # real water under it
                    self.add(proto, c[0], water, c[1], yaw + RNG.uniform(-4, 4))
                    recent.append(proto)
                    n += 1
                pos += it['d'] + RNG.uniform(1.5, 5.0)
        # the bay: a few boats at anchor and buoys off the beach
        bay = [(-120, -330, 'kw_boat_sail_a'), (40, -360, 'kw_boat_sail_b'), (150, -320, 'kw_boat_speed_e'), (-40, -300, 'kw_boat_row_small'),
               (240, -420, 'kw_boat_sail_a'), (-260, -420, 'kw_boat_fishing_small'), (520, -520, 'kw_boat_sail_b'), (700, -620, 'kw_boat_speed_g')]
        for x, z, proto in bay:
            if proto in K and float(self.g.y([x], [z])[0]) < L.SEA - 2.0:
                self.add(proto, x, water, z, RNG.uniform(0, 360))
                n += 1
        for x in np.arange(-180, 200, 38):
            z = -276 - 8 * math.cos(x / 120.0)
            if 'kw_buoy' in K and float(self.g.y([x], [z])[0]) < L.SEA - 0.8:
                self.add('kw_buoy' if int(x) % 2 else 'kw_buoy_flag', x, water + 0.05, z, RNG.uniform(0, 360))
        # ships offshore
        for x, z, yaw, proto in ((-260, -900, 98.0, 'kw_ship_ocean_liner'), (650, -880, 260.0, 'kw_ship_cargo_a'), (980, 120, 10.0, 'kw_ship_large')):
            if proto in K:
                self.add(proto, x, water - 0.2, z, yaw)
                n += 1
        self.stats['boats'] = n
        log(f'boats: {n}')

    def place_heroes(self):
        """W3 hero statues: the nearest free spot to each layout.HEROES point (spiral search), facing the nearest road,
        on a plinth the Unity builder makes from dress.json heroes."""
        K = self.kit.items
        heroes = []
        roads = np.array([[q[0], q[2]] for s in self.roads['segments'] for q in s['samples']])
        for hdef in getattr(L, 'HEROES', []):
            proto = hdef['proto']
            if proto not in K:
                continue
            it = K[proto]
            pw, pd, ph = hdef['plinth'] or (it['w'] + 0.6, it['d'] + 0.6, 0.0)
            spot = None
            for r in np.arange(0.0, 40.0, 1.5):
                for a in np.linspace(0, 2 * math.pi, max(1, int(r * 1.5)), endpoint=False):
                    x, z = hdef['near'][0] + r * math.cos(a), hdef['near'][1] + r * math.sin(a)
                    side = 2 * (max(pw, pd) / 2 + 4.6) - 1.5           # W4: the whole paved plaza stays off roads and buildings
                    if self.occ.rect_free(x, z, side, side, 0.0, margin=0.3, allow=(Occupancy.FREE, Occupancy.PARK)):
                        gy = self.g.y([x - pw / 2, x + pw / 2, x - pw / 2, x + pw / 2], [z - pd / 2, z - pd / 2, z + pd / 2, z + pd / 2])
                        if float(gy.max() - gy.min()) > 0.9:          # level ground only: a plinth, not a tower
                            continue
                        spot = (x, z)
                        break
                if spot:
                    break
            if spot is None:
                log(f'hero {hdef["id"]}: no free spot near {hdef["near"]}')
                continue
            x, z = spot
            d = np.linalg.norm(roads - np.array([x, z]), axis=1)
            q = roads[int(np.argmin(d))]
            face = math.degrees(math.atan2(q[0] - x, q[1] - z))
            ys = self.g.y([x - pw / 2, x + pw / 2, x, x - pw / 2, x + pw / 2], [z - pd / 2, z - pd / 2, z, z + pd / 2, z + pd / 2])
            # the plinth starts below the lowest corner and stands its full height above the highest, so on a slope it
            # never floats or sinks; the statue stands on its top
            y0 = float(ys.min()) - 0.6
            top = float(ys.max()) + (ph or 0.0) + 0.05
            hh = top - 0.05 - y0 if ph else 0.0
            self.add(proto, x, top if ph else float(ys.min()) + 0.05, z, face, 1.0, 1.0, tuple(hdef['tint']) + (1.0,) if hdef.get('tint') else None)
            heroes.append(dict(id=hdef['id'], proto=proto, p=[round(x, 2), round(y0, 2), round(z, 2)], yaw=round(face, 1),
                               plinth=[pw, pd, round(hh, 2)] if ph else None, egg=hdef.get('egg')))
            # a clear ring for the statue to read: no trees, benches or lamps within ~5 m of the plinth
            self.occ.rect(x, z, pw + 10.0, pd + 10.0, face, Occupancy.PROP)
            # W4: the clearing is a small paved plaza (Unity drapes the paving and its edging on the ground), with benches
            # facing the statue, flower beds and a lamp
            R = max(pw, pd) / 2 + 4.6
            self.hero_plazas.append([round(x, 2), round(float(ys.mean()), 2), round(z, 2), round(R, 2), round(face, 1)])
            K = self.kit.items
            for k, da in enumerate((-62.0, 62.0)):
                a = math.radians(face + 180.0 + da)                # benches on the far side, looking at the statue's front
                bq = (x + math.sin(a) * (R - 1.3), z + math.cos(a) * (R - 1.3))
                if 'bench_city' in K:
                    self.add('bench_city', bq[0], float(self.g.y([bq[0]], [bq[1]])[0]) + 0.04, bq[1], math.degrees(a) + 180.0)
            for k in range(6):
                a = math.radians(face + 30.0 + k * 60.0 + RNG.uniform(-8, 8))
                if abs(((k * 60.0 + 30.0) % 360.0) - 0.0) < 40.0:
                    continue                                       # keep the approach from the road open
                fq = (x + math.sin(a) * (R - 0.55), z + math.cos(a) * (R - 0.55))
                fy = float(self.g.y([fq[0]], [fq[1]])[0])
                for proto, sc in (('bush_flowers', RNG.uniform(0.8, 1.1)), ('flowers_pink_group', 1.4), ('flowers_yellow_group', 1.4)):
                    if proto in K:
                        off = RNG.uniform(-0.5, 0.5, 2)
                        self.add(proto, fq[0] + off[0], fy, fq[1] + off[1], RNG.uniform(0, 360), sc, 1.0)
            if 'street_lamp_single' in K:
                a = math.radians(face + 135.0)
                lq = (x + math.sin(a) * (R - 0.4), z + math.cos(a) * (R - 0.4))
                self.add('street_lamp_single', lq[0], float(self.g.y([lq[0]], [lq[1]])[0]), lq[1], face)
        self.heroes = heroes
        self.stats['heroes'] = len(heroes)
        log(f'heroes: {[h["id"] for h in heroes]}')

    # -------------------------------------------------------------- W4: no blank walls from public space
    DECOR_KITS = ('qtb', 'ki')       # kit buildings whose sides and backs are blank (Quaternius brick, Kenney industrial)

    def place_wall_decor(self):
        """Side and back walls that a road, park or plaza can see (open ground within ~24 m, not a party wall) get
        dressing that Unity builds from dress.json wallDecor: painted windows per floor (most walls), a mural (big blank
        walls), or climbing ivy. Kinds: 0 windows, 1 mural, 2 ivy; variant picks the atlas cell."""
        public = (Occupancy.ROAD, Occupancy.PARK, Occupancy.VENUE)
        n = {0: 0, 1: 0, 2: 0}
        for box, pid in zip(self.boxes, self.box_ids):
            it = self.kit.items.get(pid)
            if it is None or it['kit'] not in self.DECOR_KITS:
                continue
            cx, cy, cz, w, h, d, yaw = box
            a = math.radians(yaw)
            ex, ez = np.array([math.cos(a), -math.sin(a)]), np.array([math.sin(a), math.cos(a)])   # local +x, +z in world
            ybase = cy - h / 2
            for nrm, half, length in ((-ez, d / 2, w), (ex, w / 2, d), (-ex, w / 2, d)):
                c = np.array([cx, cz]) + nrm * half
                if self.occ.codes([c[0] + nrm[0] * 1.3], [c[1] + nrm[1] * 1.3])[0] == Occupancy.BUILDING:
                    continue                                   # party wall against a neighbour
                ts = np.array([3.0, 6.0, 10.0, 15.0, 20.0, 24.0])
                codes = self.occ.codes(c[0] + nrm[0] * ts, c[1] + nrm[1] * ts)
                blocked = np.argmax(codes == Occupancy.BUILDING) if (codes == Occupancy.BUILDING).any() else len(ts)
                if not np.isin(codes[:blocked], public).any() or length < 3.0 or h < 3.5:
                    continue
                r = RNG.random()
                kind = 1 if (length >= 7.0 and h >= 7.0 and r < 0.32) else (2 if r > 0.84 else 0)
                face = math.degrees(math.atan2(nrm[0], nrm[1]))
                self.wall_decor.append([round(float(c[0]), 2), round(float(ybase), 2), round(float(c[1]), 2), round(face % 360, 1),
                                        round(float(length), 2), round(float(h), 2), kind, int(RNG.integers(8 if kind == 0 else (4 if kind == 1 else 2)))])
                n[kind] += 1
        self.stats.update(wall_windows=n[0], wall_murals=n[1], wall_ivy=n[2])
        log(f'wall decor: {n[0]} window walls, {n[1]} murals, {n[2]} ivy walls')

    # -------------------------------------------------------------- shopfronts (W3 street life)
    SHOP_DISTRICTS = ('timeline', 'trenches', 'dubai_marina', 'boardwalk')
    N_SHOP_SIGNS = 32                  # cells in signs.shop_atlas

    def shopfront(self, pid, it, c, yaw, w, d, y, out, tan, seg, setback):
        """Downtown ground floors: canvas awnings over the shop windows, a blade sign with a shop name projecting from
        the facade (built by Unity from dress.json shopSigns), now and then cafe parasols on a wide sidewalk."""
        if it['kit'] not in ('qtb', 'kc') or self.district_at(*c) not in self.SHOP_DISTRICTS or seg['sidewalk'] < 1.5:
            return
        K = self.kit.items
        front = c + out * (d / 2)
        a = math.radians(yaw)
        n = self.stats.setdefault('shopfronts', 0)
        self.stats['shopfronts'] = n + 1
        r = RNG.random()
        if r < 0.6 and 'kc_detail_awning' in K:
            wide = w >= 8.6 and 'kc_detail_awning_wide' in K
            proto = 'kc_detail_awning_wide' if wide else 'kc_detail_awning'
            aw, ad = K[proto]['w'], K[proto]['d']
            count = 2 if w >= aw * 2 + 1.0 else 1
            for k in range(count):
                u = 0.0 if count == 1 else (k - 0.5) * (aw + 0.5)
                q = front + tan * u + out * (ad / 2 + 0.03)
                self.add(proto, q[0], y, q[1], yaw)
                self.occ.rect(q[0], q[1], aw, ad + 0.6, yaw, Occupancy.PROP)
            self.stats['awnings'] = self.stats.get('awnings', 0) + count
        elif r < 0.72 and seg['sidewalk'] + setback >= 3.2 and 'kc_detail_parasol_a' in K:
            for k in range(2 if w >= 7 else 1):
                u = (k - 0.5) * 3.2 if w >= 7 else 0.0
                q = front + tan * u + out * 1.9
                if self.occ.codes([q[0]], [q[1]])[0] in (Occupancy.FREE, Occupancy.PARK, Occupancy.ROAD):
                    self.add(['kc_detail_parasol_a', 'kc_detail_parasol_b'][RNG.integers(2)], q[0], y, q[1], RNG.uniform(0, 360))
                    self.occ.rect(q[0], q[1], 3.0, 3.0, yaw, Occupancy.PROP)
                    self.stats['parasols'] = self.stats.get('parasols', 0) + 1
        if r >= 0.72 and r < 0.86 and 'pp_vending_machine' in K and w >= 6.0:
            # a vending machine against the wall beside the shop window (modelled facing +z, like the building)
            q = front + tan * (w / 2 - 1.1) * (1 if RNG.random() < 0.5 else -1) + out * 0.45
            self.add('pp_vending_machine', q[0], y, q[1], yaw)
            self.occ.rect(q[0], q[1], 1.2, 1.2, yaw, Occupancy.PROP)
            self.stats['vending_machines'] = self.stats.get('vending_machines', 0) + 1
        if RNG.random() < 0.75 and w >= 4.5:
            side = 1.0 if RNG.random() < 0.5 else -1.0
            q = front + tan * side * (w / 2 - 0.7) + out * 0.62
            self.shop_signs.append([round(float(q[0]), 2), round(float(y + 4.1), 2), round(float(q[1]), 2), round(float(yaw) % 360.0, 1),
                                    int(RNG.integers(self.N_SHOP_SIGNS))])

    # -------------------------------------------------------------- street furniture
    def town(self, x, z):
        return self.district_at(x, z) in ('timeline', 'trenches', 'dubai_marina', 'boardwalk', 'residences', 'builder_hills')

    def place_street(self):
        t0 = time.time()
        K = self.kit.items
        lamp = 'kr_light_curved' if 'kr_light_curved' in K else 'street_lamp_kaykit'
        n_l = n_f = n_t = n_tr = 0
        for s in self.roads['segments']:
            if s['kind'] in ('tunnel',) or s['base'] in ('dirt', 'service'):
                continue
            p, y, tan, rn = self.frame(s['samples'])
            if len(p) < 4:
                continue
            sl = G.arc_lengths(p)
            mid = p[len(p) // 2]
            town = self.town(*mid)
            hwy = s['base'] == 'highway'
            if not town and not s['kind'] == 'bridge' and s['base'] not in ('avenue',):
                continue
            spacing = 34.0 if hwy else 28.0
            sides = (1.0, -1.0) if (s['base'] == 'avenue' or s['kind'] == 'bridge') else ((1.0,) if RNG.random() < 0.5 else (-1.0,))
            for side in sides:
                edge = (s['widthRight'] if side > 0 else s['widthLeft'])
                off = edge + (0.6 if s['sidewalk'] > 0 else 1.2)
                pos = RNG.uniform(6, spacing / 2) + (spacing / 2 if side < 0 else 0.0)
                while pos < sl[-1] - 5:
                    k = min(max(int(np.searchsorted(sl, pos)), 1), len(p) - 1)
                    q = p[k] + rn[k] * side * off
                    code = self.occ.codes([q[0]], [q[1]])[0]
                    if code in (Occupancy.FREE, Occupancy.ROAD, Occupancy.PARK):
                        face = math.degrees(math.atan2(-rn[k][0] * side, -rn[k][1] * side))   # arm over the road
                        self.add(lamp, q[0], y[k] - 0.02 + 0.15 * (s['sidewalk'] > 0), q[1], face + 180.0)
                        n_l += 1
                    pos += spacing * RNG.uniform(0.92, 1.08)
            # town sidewalks: benches, bins, hydrants, planters and street trees
            if town and s['sidewalk'] > 1.5 and s['kind'] not in ('bridge',):
                for side in (1.0, -1.0):
                    edge = (s['widthRight'] if side > 0 else s['widthLeft'])
                    pos = RNG.uniform(5, 12)
                    while pos < sl[-1] - 5:
                        k = min(max(int(np.searchsorted(sl, pos)), 1), len(p) - 1)
                        kerb = p[k] + rn[k] * side * (edge + 0.9)
                        inner = p[k] + rn[k] * side * (edge + s['sidewalk'] * 0.72)
                        face = math.degrees(math.atan2(-rn[k][0] * side, -rn[k][1] * side))
                        r = RNG.random()
                        yk = y[k] + 0.15
                        shop = self.district_at(*p[k]) in self.SHOP_DISTRICTS
                        if self.occ.codes([inner[0]], [inner[1]])[0] in (Occupancy.PROP, Occupancy.BUILDING):
                            pos += RNG.uniform(4, 7)
                            continue
                        if (s['base'] == 'avenue' and r < 0.45) or (r < 0.26 and self.district_at(*p[k]) in self.SHOP_DISTRICTS):
                            tree = self.pick(self.STREET)
                            if tree:
                                self.add(tree, kerb[0], yk - 0.1, kerb[1], RNG.uniform(0, 360), RNG.uniform(0.7, 0.85), 1.0, self.foliage_tint(tree, kerb[0], kerb[1]))
                                self.tree_pits.append([round(float(kerb[0]), 2), round(float(yk) - 0.13, 2), round(float(kerb[1]), 2), round(face % 360, 1)])
                                n_tr += 1
                        elif r < 0.56 and 'bench_city' in K:
                            self.add('bench_city', inner[0], yk, inner[1], face)
                            if 'trash_can' in K:
                                tq = inner + tan[k] * 1.6
                                self.add('trash_can', tq[0], yk, tq[1], face)
                            n_f += 2
                        elif r < 0.62 and 'kyc_firehydrant' in K:
                            self.add('kyc_firehydrant', kerb[0], yk, kerb[1], face)
                            n_f += 1
                        elif r < 0.68 and 'planter_round' in K:
                            self.add('planter_round', inner[0], yk, inner[1], RNG.uniform(0, 90))
                            n_f += 1
                        elif r < 0.80 and shop:
                            # parked two-wheelers: a row of 2-4 scooters, mopeds and bicycles at the kerb, angled in
                            kinds = [q for q in ('pp_scooter', 'pp_moped', 'pp_bicycle') if q in K]
                            if kinds:
                                cnt = int(RNG.integers(2, 5))
                                along = math.degrees(math.atan2(tan[k][0], tan[k][1]))
                                for k2 in range(cnt):
                                    bq = kerb + tan[k] * (k2 * 0.95) - rn[k] * side * 0.35
                                    kq = kinds[RNG.integers(len(kinds))]
                                    # scooters and mopeds are modelled along +z, the bicycle along +x
                                    self.add(kq, bq[0], yk, bq[1], along + 90.0 * side + (90.0 if kq == 'pp_bicycle' else 0.0) + RNG.uniform(-14, 14))
                                self.occ.rect(kerb[0] + tan[k][0] * cnt * 0.45, kerb[1] + tan[k][1] * cnt * 0.45, 1.8, cnt * 1.0 + 0.6,
                                              along, Occupancy.PROP)
                                n_f += cnt
                                self.stats['two_wheelers'] = self.stats.get('two_wheelers', 0) + cnt
                        elif r < 0.87 and shop and s['sidewalk'] >= 2.6 and 'pp_vendor_cart' in K:
                            self.add('pp_vendor_cart', inner[0], yk, inner[1], face + RNG.uniform(-8, 8))   # modelled along +x: lies along the kerb
                            self.occ.rect(inner[0], inner[1], 2.8, 2.2, face, Occupancy.PROP)
                            self.stats['vendor_carts'] = self.stats.get('vendor_carts', 0) + 1
                            n_f += 1
                        elif r < 0.93 and 'kyc_trash_b' in K:
                            self.add('kyc_trash_b', inner[0], yk, inner[1], face)
                            n_f += 1
                        pos += RNG.uniform(9, 15) if self.district_at(*p[k]) in self.SHOP_DISTRICTS else RNG.uniform(14, 22)
        # traffic lights and stop signs on every approach (right-hand corner, facing the oncoming traffic)
        for j in self.roads['intersections']:
            parts = j.get('parts') or [j]
            kind = j['kind']
            if kind not in ('signal', 'stop'):
                continue
            for part in parts:
                for a in part['approaches']:
                    seg = self.segs.get(a['segment'])
                    if seg is None or seg['base'] in ('dirt', 'service'):
                        continue
                    dx, dz = a['dir']
                    right = np.array([a['right'][0], a['right'][2]])
                    rn = np.array([dz, -dx])                       # right of the outward direction
                    q = right + np.array([dx, dz]) * 1.2 + rn * (1.0 + (seg['sidewalk'] > 0) * 0.4)
                    yq = a['cut'][1] + 0.15 * (seg['sidewalk'] > 0)
                    face = math.degrees(math.atan2(dx, dz))        # looks along the outward direction, at the traffic
                    if kind == 'signal' and 'kr_traffic_light' in K:
                        self.add('kr_traffic_light', q[0], yq, q[1], face + 180.0)
                        self.signals.append(dict(intersection=j['id'], segment=a['segment'], p=[round(float(q[0]), 2), round(float(yq), 2), round(float(q[1]), 2)],
                                                 yaw=round(face, 1)))
                        n_t += 1
                    elif kind == 'stop' and 'kr_road_sign_stop' in K and seg['base'] != 'highway':
                        self.add('kr_road_sign_stop', q[0], yq, q[1], face + 180.0)
                        n_t += 1
        self.stats.update(lamps=n_l, furniture=n_f, signals_and_signs=n_t, street_trees=n_tr)
        log(f'street: {n_l} lamps, {n_f} furniture, {n_t} signals/signs, {n_tr} street trees ({time.time() - t0:.1f}s)')

    def place_transit(self):
        """W4: bus shelters with ad panels (built by Unity from dress.json busShelters) on avenue and downtown sidewalks
        every ~170 m, alternating sides, backs to the buildings; a small market of stalls in each park."""
        K = self.kit.items
        n = 0
        for s in self.roads['segments']:
            if s['kind'] in ('tunnel', 'bridge') or s['sidewalk'] < 2.4:
                continue
            p, y, tan, rn = self.frame(s['samples'])
            if len(p) < 4:
                continue
            mid = p[len(p) // 2]
            if not (s['base'] == 'avenue' or self.district_at(*mid) in self.SHOP_DISTRICTS):
                continue
            sl = G.arc_lengths(p)
            side = 1.0 if RNG.random() < 0.5 else -1.0
            pos = RNG.uniform(30, 70)
            while pos < sl[-1] - 25:
                k = min(max(int(np.searchsorted(sl, pos)), 1), len(p) - 1)
                edge = s['widthRight'] if side > 0 else s['widthLeft']
                q = p[k] + rn[k] * side * (edge + s['sidewalk'] * 0.6)
                face = math.degrees(math.atan2(-rn[k][0] * side, -rn[k][1] * side))      # open side towards the road
                if self.occ.rect_free(q[0], q[1], 4.6, 1.8, face, margin=0.2, allow=(Occupancy.FREE, Occupancy.ROAD, Occupancy.PARK)):
                    yq = float(y[k]) + 0.15
                    self.bus_shelters.append([round(float(q[0]), 2), round(yq, 2), round(float(q[1]), 2), round(face % 360, 1), int(RNG.integers(8))])
                    self.occ.rect(q[0], q[1], 4.6, 1.8, face, Occupancy.PROP)
                    n += 1
                    side = -side
                    pos += RNG.uniform(150, 190)
                else:
                    pos += 12.0
        m = 0
        if 'pp_market_stalls' in K:
            for name, poly in L.PARKS:
                P = np.array(poly, float)
                c = P.mean(axis=0)
                for k in range(2):
                    for t in range(14):
                        q = c + RNG.uniform(-18, 18, 2)
                        if G.point_in_poly(q[0], q[1], P) and self.occ.rect_free(q[0], q[1], 9.0, 9.0, 0.0, margin=0.5, allow=(Occupancy.PARK, Occupancy.FREE)):
                            yaw = RNG.uniform(0, 360)
                            self.add('pp_market_stalls', q[0], float(self.g.y([q[0]], [q[1]])[0]) - 0.05, q[1], yaw)
                            self.occ.rect(q[0], q[1], 9.0, 9.0, yaw, Occupancy.PROP)
                            m += 1
                            break
        self.stats.update(bus_shelters=n, market_stalls=m)
        log(f'transit: {n} bus shelters, {m} market stalls')

    # -------------------------------------------------------------- promenade, power lines, billboards
    def place_promenade(self):
        """Ocean Drive: palms, lamps and benches along the seaward edge, facing the bay; on the boardwalk when there is one."""
        K = self.kit.items
        n = 0
        bw = self.plan.get('boardwalk') or []
        if bw:
            A = np.array(bw[0]['inner'], float); B = np.array(bw[0]['outer'], float); Y = np.array(bw[0]['y'], float)
            steps = set(bw[0].get('steps', []))
            for k in range(2, len(A) - 2):
                sea = B[k] - A[k]
                sea = sea / max(np.linalg.norm(sea), 1e-6)
                face = math.degrees(math.atan2(sea[0], sea[1]))
                if any(abs(k - st) < 3 for st in steps):
                    continue
                if k % 6 == 0:
                    q = A[k] + (B[k] - A[k]) * 0.3
                    palm = 'palm_1' if (k // 6) % 2 else 'palm_2'
                    if palm in K:
                        self.add(palm, q[0], Y[k], q[1], RNG.uniform(0, 360), RNG.uniform(1.4, 1.8))
                        n += 1
                elif k % 6 == 3 and 'bench_city' in K:
                    q = A[k] + (B[k] - A[k]) * 0.62
                    self.add('bench_city', q[0], Y[k], q[1], face + 180.0)
                elif k % 12 == 9 and 'street_lamp_single' in K:
                    q = A[k] + (B[k] - A[k]) * 0.9
                    self.add('street_lamp_single', q[0], Y[k], q[1], face)
            # keep the deck clear of other props
            for a, b in zip(A, B):
                self.occ.line([tuple(a), tuple(b)], 2.5, Occupancy.PROP)
            self.stats['promenade_palms'] = n
            return
        for s in self.roads['segments']:
            if s['route'] != 'ocean_drive':
                continue
            p, y, tan, rn = self.frame(s['samples'])
            sl = G.arc_lengths(p)
            # the seaward side: the side whose offset point is lower (towards the bay)
            test = self.g.y([p[len(p) // 2][0] + rn[len(p) // 2][0] * 14], [p[len(p) // 2][1] + rn[len(p) // 2][1] * 14])[0]
            side = 1.0 if test < y[len(p) // 2] else -1.0
            edge = (s['widthRight'] if side > 0 else s['widthLeft']) + s['sidewalk']
            pos = RNG.uniform(3, 8)
            while pos < sl[-1] - 3:
                k = min(max(int(np.searchsorted(sl, pos)), 1), len(p) - 1)
                q = p[k] + rn[k] * side * (edge - 1.0)
                palm = 'palm_1' if RNG.random() < 0.5 else 'palm_2'
                if palm in K:
                    self.add(palm, q[0], y[k] + 0.15, q[1], RNG.uniform(0, 360), RNG.uniform(1.4, 1.8))
                    n += 1
                pos += RNG.uniform(10, 14)
        self.stats['promenade_palms'] = n

    def place_power(self):
        """Power poles along the rural roads (farm, residences, hills) on one side, every ~38 m; Unity strings the wires."""
        K = self.kit.items
        pole = 'kr_electricity_pole' if 'kr_electricity_pole' in K else None
        if pole is None:
            return
        n = 0
        for s in self.roads['segments']:
            p, y, tan, rn = self.frame(s['samples'])
            if len(p) < 4:
                continue
            # W4: about half of downtown's side streets (not the avenues) get overhead lines too, poles at the kerb
            urban = (s['base'] == 'street' and s['sidewalk'] > 1.5 and s['kind'] not in ('bridge', 'tunnel')
                     and self.district_at(*p[len(p) // 2]) in ('timeline', 'trenches') and (int(s['id'][1:]) * 7919) % 10 < 5)
            if s['route'] not in ('farm_road', 'res_spine', 'res_crescent', 'res_link', 'res_cliff', 'farm_track', 'farm_lane', 'hills_drive') and not urban:
                continue
            sl = G.arc_lengths(p)
            side = 1.0
            edge = s['widthRight'] + (s['sidewalk'] - 0.5 if urban else s['sidewalk'] + 3.0)
            run = []
            pos = 6.0
            while pos < sl[-1] - 6:
                k = min(max(int(np.searchsorted(sl, pos)), 1), len(p) - 1)
                q = p[k] + rn[k] * side * edge
                code = self.occ.codes([q[0]], [q[1]])[0]
                if code in (Occupancy.FREE, Occupancy.FIELD, Occupancy.PARK) or (urban and code == Occupancy.ROAD):
                    yq = float(self.g.y([q[0]], [q[1]])[0]) + (0.15 if urban else 0.0)
                    face = math.degrees(math.atan2(tan[k][0], tan[k][1]))
                    self.add(pole, q[0], yq, q[1], face + 90.0)
                    run.append([round(float(q[0]), 2), round(yq, 2), round(float(q[1]), 2), round(face + 90.0, 1)])
                    n += 1
                elif len(run) >= 2:
                    self.power.append(run); run = []
                pos += RNG.uniform(34, 42)
            if len(run) >= 2:
                self.power.append(run)
        self.stats['power_poles'] = n

    # -------------------------------------------------------------- vegetation
    def scatter(self, spacing, region, accept, choose, cull_codes=(Occupancy.FREE,), margin=1.5, jitter=0.9):
        """Jittered-grid scatter over region (x0, x1, z0, z1): accept(x, z) -> probability; choose(x, z, y) -> (proto,
        scale, sy) or None. Skips taken ground (roads, buildings, water...)."""
        x0, x1, z0, z1 = region
        xs = np.arange(x0, x1, spacing)
        zs = np.arange(z0, z1, spacing)
        X, Z = np.meshgrid(xs, zs)
        X = X + RNG.uniform(-jitter, jitter, X.shape) * spacing * 0.5
        Z = Z + RNG.uniform(-jitter, jitter, Z.shape) * spacing * 0.5
        X, Z = X.ravel(), Z.ravel()
        pr = accept(X, Z)
        keep = RNG.random(len(X)) < pr
        X, Z = X[keep], Z[keep]
        if len(X) == 0:
            return 0
        codes = self.occ.codes(X, Z)
        ok = np.isin(codes, cull_codes)
        # margin: the 4 neighbours a margin away must be free too
        for dx, dz in ((margin, 0), (-margin, 0), (0, margin), (0, -margin)):
            ok &= np.isin(self.occ.codes(X + dx, Z + dz), cull_codes + (Occupancy.TREE,))
        X, Z = X[ok], Z[ok]
        Y = self.g.y(X, Z)
        n = 0
        for x, y, z in zip(X, Y, Z):
            c = choose(x, z, y)
            if c is None:
                continue
            proto, sc, sy = c
            if proto not in self.kit.items:
                continue
            self.add(proto, x, y - 0.1, z, RNG.uniform(0, 360), sc, sy, self.foliage_tint(proto, x, z))
            n += 1
        return n

    # W4 species per biome (Quaternius Stylized Nature MegaKit trees, 10-18 m mature; palms and blossom from the env set)
    BROAD = ['w_tree_common_1', 'w_tree_common_2', 'w_tree_common_3', 'w_tree_common_4', 'w_tree_common_5']
    PINES = ['w_pine_1', 'w_pine_2', 'w_pine_3', 'w_pine_4', 'w_pine_5']
    AUTUMN = ['w_tree_twisted_1', 'w_tree_twisted_3']
    STREET = ['w_tree_common_3', 'w_tree_common_4', 'w_tree_common_5']
    BLOSSOM = ['tree_blossom_1', 'tree_blossom_2', 'tree_blossom_3', 'tree_blossom_4']
    PALMS = ['palm_1', 'palm_2']
    BUSHES = ['bush_round', 'bush_flowers', 'bush_large_flowers', 'bush_small']
    COAST_DISTRICTS = ('boardwalk', 'dubai_marina', 'islet')

    def pick(self, pool):
        live = [q for q in pool if q in self.kit.items]
        return live[RNG.integers(len(live))] if live else None

    def foliage_tint(self, proto, x, z):
        """Per-tree colour drift (the kit shader multiplies foliage by it): mostly deeper greens, some olive and blue-green,
        a few autumn crowns in the groves; large-scale noise keeps neighbours related, so stands read as one wood."""
        if not (proto.startswith(('kf_tree', 'kf_bush', 'w_tree_common', 'w_pine', 'bush_'))):
            return None
        n = G.fbm(np.array([x / 70.0]), np.array([z / 70.0]))[0]
        base = np.array([0.74, 0.80, 0.66]) + (n - 0.5) * np.array([0.28, 0.18, 0.12]) + RNG.uniform(-0.05, 0.05, 3)
        if proto.startswith('kf_tree') and 'tree_4' not in proto and RNG.random() < 0.05:
            base = np.array([1.0, 0.70, 0.36]) * RNG.uniform(0.85, 1.0)          # autumn
        return tuple(np.clip(base, 0, 1)) + (1.0,)

    def place_vegetation(self):
        t0 = time.time()
        g = self.g
        region = (-900.0, 940.0, -780.0, 720.0)
        pines, broad = self.PINES, self.BROAD

        dmap = self.district_raster()
        road_d = ndimage.distance_transform_edt(self.occ.arr() != Occupancy.ROAD)      # metres from the nearest road

        def town_factor(X, Z):
            ix = np.clip(((X + 1024) / 4).astype(int), 0, dmap.shape[1] - 1)
            iz = np.clip(((Z + 1024) / 4).astype(int), 0, dmap.shape[0] - 1)
            f = np.choose(dmap[iz, ix], [1.0, 0.0, 0.3, 0.3, 0.0, 0.0, 0.15, 0.6, 1.0, 1.0, 1.0])
            ox = np.clip(np.round(X - Occupancy.X0).astype(int), 0, Occupancy.W - 1)
            oy = np.clip(np.round(Occupancy.H - 1 - (Z - Occupancy.Z0)).astype(int), 0, Occupancy.H - 1)
            rd = road_d[oy, ox]
            return f * np.clip((rd - 4.0) / 14.0, 0.25, 1.0)

        def forest_p(X, Z):
            return np.clip(g.sample(g.forest, X, Z) * 1.15 * town_factor(X, Z), 0, 0.95)

        def forest_pick(x, z, y):
            sl = float(g.sample(g.slope, [x], [z])[0])
            high = y > 55 or sl > 0.6
            if y < L.SEA + 9 and self.district_at(x, z) in self.COAST_DISTRICTS:
                return self.pick(self.PALMS), RNG.uniform(1.45, 1.85), RNG.uniform(0.95, 1.1)     # coast: palms, 9-14 m
            if RNG.random() < (0.86 if high else 0.3):
                return self.pick(pines), RNG.uniform(0.75, 1.15), RNG.uniform(0.95, 1.1)
            if RNG.random() < 0.015:
                return self.pick(self.AUTUMN), RNG.uniform(0.8, 1.0), 1.0
            return self.pick(broad), RNG.uniform(0.75, 1.1), 1.0
        n_forest = self.scatter(6.0, region, forest_p, forest_pick)

        # meadow trees: lone trees and small groves in open country (not towns, not forest)
        def meadow_p(X, Z):
            f = g.sample(g.forest, X, Z)
            y = g.y(X, Z)
            return np.where((f < 0.1) & (y > L.SEA + 3) & (y < 90), 0.05 + 0.1 * (G.fbm(X / 60.0, Z / 60.0) > 0.58), 0.0)

        def meadow_pick(x, z, y):
            if RNG.random() < 0.05:
                return self.pick(self.AUTUMN), RNG.uniform(0.85, 1.05), 1.0
            return self.pick(broad), RNG.uniform(0.85, 1.15), 1.0
        n_meadow = self.scatter(16.0, region, meadow_p, meadow_pick)

        # bushes: forest edges and rural verges
        bushes = [k for k in self.BUSHES if k in self.kit.items] or [k for k in self.kit.items if k.startswith('kf_bush_')]

        def bush_p(X, Z):
            f = g.sample(g.forest, X, Z)
            y = g.y(X, Z)
            edge = np.clip(1.0 - np.abs(f - 0.25) / 0.2, 0, 1)
            return np.where((y > L.SEA + 2) & (y < 110), edge * 0.5 + 0.04, 0.0)

        def bush_pick(x, z, y):
            return bushes[RNG.integers(len(bushes))], RNG.uniform(1.0, 1.9), RNG.uniform(0.9, 1.2)
        n_bush = self.scatter(5.0, region, bush_p, bush_pick, margin=0.8)

        # rocks: rocky and scree ground, cliff tops
        rocks = [k for k in self.kit.items if k.startswith('kf_rock_')]

        def rock_p(X, Z):
            r = g.sample(g.rock, X, Z)
            t = g.sample(g.talus, X, Z)
            return np.clip(r * 0.12 + t * 0.35, 0, 0.4)

        def rock_pick(x, z, y):
            return rocks[RNG.integers(len(rocks))], RNG.uniform(0.45, 1.25), RNG.uniform(0.6, 1.1)
        n_rock = self.scatter(9.0, region, rock_p, rock_pick, margin=0.6)

        # parks: groves and blossom trees, lawns stay open
        n_park = 0
        for name, poly in L.PARKS:
            P = np.array(poly, float)
            x0, z0 = P.min(axis=0); x1, z1 = P.max(axis=0)
            blossom = ['tree_blossom_1', 'tree_blossom_2', 'tree_blossom_3', 'tree_blossom_4']

            def park_p(X, Z, P=P):
                inside = np.array([G.point_in_poly(x, z, P) for x, z in zip(X, Z)])
                return np.where(inside, 0.55 * (G.fbm(X / 30.0 + 5, Z / 30.0) > 0.46), 0.0)

            def park_pick(x, z, y):
                r = RNG.random()
                if r < 0.4:
                    return self.pick(blossom), RNG.uniform(1.35, 1.6), 1.0          # cherry blossom, 8-10 m
                if r < 0.5:
                    return self.pick(self.AUTUMN), RNG.uniform(0.8, 0.95), 1.0
                return self.pick(broad), RNG.uniform(0.75, 1.0), 1.0
            n_park += self.scatter(9.0, (x0, x1, z0, z1), park_p, park_pick, cull_codes=(Occupancy.PARK,))
        # crops in the fields: rows along each field's direction
        n_crop = 0
        crop_of = {'wheat': 'kn_crops_wheatstageb', 'corn': 'kn_crops_cornstaged', 'sunflower': 'kn_crops_cornstagec',
                   'cabbage': 'kn_crops_leafsstageb', 'lavender': 'kn_crops_leafsstageb', 'pumpkin': 'kn_crop_pumpkin'}
        for poly, ang, kind in L.FIELDS:
            proto = crop_of.get(kind)
            if proto not in self.kit.items:
                continue
            P = np.array(poly, float)
            c0 = P.mean(axis=0)
            a = math.radians(ang)
            u = np.array([math.sin(a), math.cos(a)])
            v = np.array([u[1], -u[0]])
            for row in np.arange(-70, 70, 2.8):
                for t in np.arange(-70, 70, 2.3):
                    q = c0 + u * t + v * row + RNG.uniform(-0.25, 0.25, 2)
                    if not G.point_in_poly(q[0], q[1], P):
                        continue
                    yq = float(self.g.y([q[0]], [q[1]])[0])
                    self.add(proto, q[0], yq - 0.05, q[1], ang + RNG.uniform(-12, 12), RNG.uniform(0.85, 1.15), RNG.uniform(0.85, 1.15))
                    n_crop += 1
        n_detail = self.place_ground_detail(road_d)
        self.stats.update(forest_trees=n_forest, meadow_trees=n_meadow, bushes=n_bush, rocks=n_rock, park_trees=n_park, crops=n_crop,
                          ground_detail=n_detail)
        log(f'vegetation: {n_forest} forest, {n_meadow} meadow, {n_bush} bushes, {n_rock} rocks, {n_park} park, {n_crop} crops '
            f'({time.time() - t0:.1f}s)')

    def place_ground_detail(self, road_d):
        """W4 eye-level ground: grass clumps, clover, flowers, ferns and pebbles on the green within ~14 m of roads,
        plazas and paths, and through the parks (drawn only within ~38 m of the camera)."""
        g = self.g
        region = (-900.0, 940.0, -780.0, 720.0)

        def near_road(X, Z):
            ox = np.clip(np.round(X - Occupancy.X0).astype(int), 0, Occupancy.W - 1)
            oy = np.clip(np.round(Occupancy.H - 1 - (Z - Occupancy.Z0)).astype(int), 0, Occupancy.H - 1)
            return road_d[oy, ox]

        def detail_p(X, Z):
            d = near_road(X, Z)
            y = g.y(X, Z)
            grassy = (g.sample(g.rock, X, Z) < 0.35) & (y > L.SEA + 1.2) & (y < 120)
            return np.where(grassy, np.clip(0.85 - (d - 2.5) / 30.0, 0.0, 0.85), 0.0)

        def detail_pick(x, z, y):
            r = RNG.random()
            if r < 0.42:
                return self.pick(['w_grass_wispy_short', 'w_grass_wispy_tall', 'w_grass_common_short']), RNG.uniform(0.9, 1.6), RNG.uniform(0.8, 1.3)
            if r < 0.56:
                return self.pick(['w_clover_1', 'w_clover_2']), RNG.uniform(1.2, 2.2), 1.0
            if r < 0.74:
                return self.pick(['w_flower_3_group', 'w_flower_4_group', 'w_flower_3_single', 'flowers_pink_group', 'flowers_yellow_group']), RNG.uniform(1.0, 1.6), 1.0
            if r < 0.84:
                return self.pick(['w_pebble_round_1', 'w_pebble_round_3', 'w_pebble_square_2', 'w_pebble_square_5']), RNG.uniform(0.7, 1.6), RNG.uniform(0.6, 1.2)
            if r < 0.93:
                return self.pick(['w_fern_1', 'w_plant_7']), RNG.uniform(0.8, 1.3), 1.0
            return self.pick(['w_mushroom_common', 'w_grass_wispy_tall']), RNG.uniform(0.8, 1.2), 1.0
        n = self.scatter(2.3, region, detail_p, detail_pick, cull_codes=(Occupancy.FREE, Occupancy.PARK), margin=0.4, jitter=1.0)
        log(f'ground detail: {n}')
        return n

    # -------------------------------------------------------------- output
    def write(self, path):
        protos = sorted(self.inst)
        doc = dict(version=1, generated=time.strftime('%Y-%m-%d'), stats=self.stats,
                   record='x, y, z, yaw (deg), scale xz, scale y, tint r, g, b, a',
                   instances=[dict(proto=p, n=len(self.inst[p]), d=[c for r in self.inst[p] for c in r]) for p in protos],
                   buildingBoxes=self.boxes, signals=self.signals, power=self.power, billboards=self.billboards, heroes=self.heroes,
                   shopSigns=self.shop_signs, treePits=self.tree_pits, busShelters=self.bus_shelters, wallDecor=self.wall_decor,
                   heroPlazas=self.hero_plazas)
        txt = json.dumps(doc, separators=(',', ':'))
        open(path, 'w').write(txt)
        log(f'wrote {path} ({len(txt) / 1e6:.1f} MB, {sum(len(v) for v in self.inst.values())} instances of {len(protos)} models)')


def main():
    t0 = time.time()
    d = Dress()
    d.stamp_static()
    d.place_heroes()
    d.place_buildings()
    d.infill_blocks()
    d.place_wall_decor()
    d.place_promenade()
    d.place_plaza()
    d.place_billboards()
    d.place_boats()
    d.place_transit()
    d.place_street()
    d.place_power()
    d.place_vegetation()
    d.write(os.path.join(OUT, 'dress.json'))
    log(f'done ({time.time() - t0:.1f}s)')


if __name__ == '__main__':
    main()
