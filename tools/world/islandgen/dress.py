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
# World v3 (2026-10-22): the semi-real kit (Neighbourhood City anchor 'kit:nc', 99.Miles buildings and Poly Haven props
# 'kit:v3'). It covers Timeline Downtown for now: v3_downtown() re-dresses that district after everything else.
WORLD_V3_MAN = os.path.join(REPO, 'art', 'processed', 'world_v3', 'manifest.json')
V3_SEED = 20261022
RNG = np.random.default_rng(20261017)

# Which way each kit's facade (door side) faces in model space: yaw offset added so the facade looks at the street.
FRONT = {'qtb': 0.0, 'kc': 0.0, 'ks': 0.0, 'ki': 0.0, 'env': 0.0, 'nc': 0.0, 'v3': 0.0}

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
        for path, kitname in ((WORLD_MAN, None), (ENV_MAN, 'env'), (WORLD_V3_MAN, None)):
            if not os.path.exists(path):
                continue
            for k, v in json.load(open(path)).items():
                kit = kitname or next((t[4:] for t in v.get('tags', []) if t.startswith('kit:')), k.split('_')[0])
                sx, sy_, sz = v['size_m']                     # Blender x (width), y (depth), z (height)
                self.items[k] = dict(id=k, w=sx, d=sy_, h=sz, kit=kit, tags=set(v.get('tags', [])), tris=v['tris'][0], kind=v.get('kind'))

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
        self.signposts = []            # W4.1 [x, y, z, yaw (panel faces), sign id] -> Signs/sign_post_<id>.png
        self.flag_poles = []           # W4.1 [x, y, z, yaw (cloth flies towards), r, g, b]
        self.event_dress = None        # W4.1 convention centre event props
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
        if getattr(self, 'v3_district_pools', False):
            return self.pool_v3_district(style)
        if getattr(self, 'v3_only', False):
            return self.pool_v3(style)
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

    # -------------------------------------------------------------- world v3: Timeline Downtown
    V3_NC = ['nc_building_01', 'nc_building_02', 'nc_building_03', 'nc_building_04', 'nc_building_05', 'nc_building_06']
    V3_NC_TALL = ['nc_building_01', 'nc_building_02', 'nc_building_03', 'nc_building_05']
    # subtle per-building colour shifts (albedo multipliers) so the six Neighbourhood City facades read as more buildings
    V3_TINTS = [(1.0, 1.0, 1.0), (1.06, 1.0, 0.92), (0.94, 0.97, 1.04), (1.04, 0.95, 0.9), (0.92, 0.92, 0.92), (1.08, 1.05, 1.0),
                (0.97, 1.02, 0.97), (1.02, 0.97, 1.02)]
    V3_TREES = ['nc_tree_01', 'nc_tree_02', 'nc_tree_03', 'nc_tree_04', 'nc_tree_05', 'nc_tree_06', 'nc_tree_07', 'nc_tree_08',
                'nc_tree_09', 'nc_tree_10', 'nc_tree_11', 'nc_tree_12']
    V3_SMALL_PLANTERS = ['nc_tree_13', 'nc_tree_14', 'ph_potted_plant_01', 'ph_potted_plant_02']

    def pool_v3(self, style):
        """Downtown frontage from the v3 kit. Neighbourhood City mid-rises (4-14 m, 5-8 m deep) line the streets; the
        99.Miles Chicago walk-ups (38 m frontage, shopfronts) and the 34 m apartment complex (55 m) break the rhythm and
        lift the core. No upward stretching: these facades are photo-textured and would show it."""
        K = self.kit.items
        tall = [k for k in self.V3_NC_TALL if k in K]                    # 10-14 m, three to four storeys
        mid = tall + [k for k in ('nc_building_04',) if k in K]           # + the 6.6 m two-storey
        low = [k for k in ('nc_building_06',) if k in K]                  # the 4 m shop: edge only, now and then
        # (the 99.Miles walk-ups and apartment complexes are placed first by v3_anchors: they need long, deep lots)
        if style == 'core':
            return dict(ids=tall * 3 + mid, gap=(0.0, 0.3), setback=(0.15, 0.6), scale=(1.04, 1.14), sy=(1.0, 1.0),
                        slope=1.5, alley=0.06, city=True)
        if style == 'mid':
            return dict(ids=tall * 2 + mid, gap=(0.0, 0.4), setback=(0.15, 0.8), scale=(1.0, 1.12), sy=(1.0, 1.0),
                        slope=1.6, alley=0.1, city=True)
        if style == 'edge':
            return dict(ids=tall + mid * 2 + low, gap=(0.0, 0.8), setback=(0.3, 1.2), scale=(1.0, 1.1), sy=(1.0, 1.0),
                        slope=1.8, alley=0.16, city=True)
        return None

    def v3_anchors(self):
        """The 99.Miles frontage first: apartment complexes (55 x 25 m, 34 m tall) in the core and mid ring, Chicago
        walk-ups (38 x 13 m with shopfronts) anywhere downtown, each on a street side with room for it, never two on one
        side of a segment. The Neighbourhood City mid-rises then fill the frontage between them."""
        K = self.kit.items
        caps = {'m99_apt_complex': 7, 'm99_chicago_a': 10}
        self.v3_why = {}
        made = {k: 0 for k in caps}
        segs = [s for s in self.roads['segments'] if s['kind'] not in ('bridge', 'tunnel') and s['base'] not in ('mountain', 'dirt', 'highway')]
        for i in RNG.permutation(len(segs)):
            s = segs[i]
            p, y, tan, rn = self.frame(s['samples'])
            if len(p) < 4:
                continue
            sl = G.arc_lengths(p)
            for side in (1.0, -1.0):
                edge = (s['widthRight'] if side > 0 else s['widthLeft']) + s['sidewalk']
                order = [pid for pid in ('m99_apt_complex', 'm99_chicago_a') if pid in K and made[pid] < caps[pid]]
                for pid in order:
                    it = K[pid]
                    done = False
                    for pos in RNG.permutation(np.arange(4.0, max(4.0, sl[-1] - it['w'] - 4.0), 3.0)):
                        cpos = pos + it['w'] / 2
                        kc = min(max(int(np.searchsorted(sl, cpos)), 1), len(p) - 1)
                        P, n_ = p[kc], rn[kc] * side
                        style = self.style_for(s, *P)
                        if style not in ('core', 'mid', 'edge') or (pid == 'm99_apt_complex' and style == 'edge'):
                            continue
                        setback = RNG.uniform(0.2, 0.7)
                        c = P + n_ * (edge + setback + it['d'] / 2)
                        yaw = math.degrees(math.atan2(-n_[0], -n_[1]))
                        if self._v3_add_building(pid, c[0], c[1], yaw, 1.0, margin=0.12, why=self.v3_why.setdefault(pid, {})):
                            made[pid] += 1
                            done = True
                            break
                    if done:
                        break                                   # one anchor per street side
        log(f'v3 anchors {made}; blocked by occupancy codes / slope {self.v3_why}')
        return made

    @staticmethod
    def _in_rect(x, z, box, margin):
        cx, cy, cz, w, h, d, yaw = box
        a = math.radians(yaw)
        dx, dz = np.asarray(x, float) - cx, np.asarray(z, float) - cz
        u = dx * math.cos(a) - dz * math.sin(a)
        v = dx * math.sin(a) + dz * math.cos(a)
        return (np.abs(u) <= w / 2 + margin) & (np.abs(v) <= d / 2 + margin)

    def v3_downtown(self):
        """World v3 in Timeline Downtown (after every other step, on its own RNG, so the rest of the island and the
        reviewed placement repairs stay exactly as they were): the toy-kit buildings come out with their shopfront
        dressing, wall decor and rooftop boards; the frontage and block interiors are rebuilt from the v3 kit, with the
        105 m 99.Miles office tower as the skyline landmark; trees inside new footprints go; downtown street furniture
        and trees switch to the v3 props and planter trees."""
        global RNG
        if not any(k.startswith('nc_building') for k in self.kit.items):
            log('v3 downtown: no world_v3 kit, skipped')
            return
        t0 = time.time()
        RNG = np.random.default_rng(V3_SEED)
        K = self.kit.items
        # the reviewed placement corrections first (they match the original protos), so swapped props keep them
        from placement_repairs import apply_repairs
        doc = dict(instances=[dict(proto=p, d=[c for r in rows for c in r]) for p, rows in self.inst.items()], buildingBoxes=self.boxes,
                   wallDecor=self.wall_decor, shopSigns=self.shop_signs)
        moved = apply_repairs(doc)
        for g in doc['instances']:
            dd = g['d']
            self.inst[g['proto']] = [dd[i:i + 10] for i in range(0, len(dd), 10)]
        log(f'v3 downtown: {moved} placement corrections applied first')
        dt = lambda x, z: self.district_at(x, z) == 'timeline'
        # 1. out with the old downtown buildings
        keep_b, keep_i, gone = [], [], []
        for box, pid in zip(self.boxes, self.box_ids):
            if dt(box[0], box[2]) and K[pid]['kit'] not in ('nc', 'v3'):
                gone.append(box)
            else:
                keep_b.append(box); keep_i.append(pid)
        old_ids = set(self.box_ids[i] for i in range(len(self.box_ids)) if self.boxes[i] in gone)
        for pid in old_ids:
            self.inst[pid] = [r for r in self.inst.get(pid, []) if not any(abs(r[0] - b[0]) < 0.03 and abs(r[2] - b[2]) < 0.03 for b in gone)]
        shopdress = ('kc_detail_awning', 'kc_detail_awning_wide', 'kc_detail_parasol_a', 'kc_detail_parasol_b', 'pp_vending_machine')
        removed = dict(buildings=len(gone), wall_decor=0, shop_signs=0, shopdress=0)
        for b in gone:
            n0 = len(self.wall_decor)
            self.wall_decor = [r for r in self.wall_decor if not self._in_rect(r[0], r[2], b, 0.6)]
            removed['wall_decor'] += n0 - len(self.wall_decor)
            n0 = len(self.shop_signs)
            self.shop_signs = [r for r in self.shop_signs if not self._in_rect(r[0], r[2], b, 2.5)]
            removed['shop_signs'] += n0 - len(self.shop_signs)
            for proto in shopdress:
                rows = self.inst.get(proto, [])
                if rows:
                    keep = [r for r in rows if not self._in_rect(r[0], r[2], b, 3.2)]
                    removed['shopdress'] += len(rows) - len(keep)
                    self.inst[proto] = keep
        roof_boards = [bb for bb in self.billboards if bb['kind'] == 'rooftop' and any(self._in_rect(bb['p'][0], bb['p'][2], b, 0.5) for b in gone)]
        self.billboards = [bb for bb in self.billboards if bb not in roof_boards]
        self.boxes, self.box_ids = keep_b, keep_i
        # free their ground: every BUILDING pixel inside the district
        g = self.occ.arr().copy()
        img = Image.new('L', (self.occ.W, self.occ.H), 0)
        ImageDraw.Draw(img).polygon([self.occ.px(x, z) for x, z in L.DOWNTOWN_POLY], fill=1)
        g[(np.asarray(img) == 1) & (g == Occupancy.BUILDING)] = Occupancy.FREE
        # and the shopfront dressing they had (awnings, parasols, vending machines stamped PROP round their fronts)
        fronts = Image.new('L', (self.occ.W, self.occ.H), 0)
        fd = ImageDraw.Draw(fronts)
        for b in gone:
            fd.polygon([self.occ.px(x, z) for x, z in G.oriented_rect(b[0], b[2], b[3] + 7.0, b[5] + 7.0, b[6])], fill=1)
        g[(np.asarray(fronts) == 1) & (g == Occupancy.PROP)] = Occupancy.FREE
        self.occ.img = Image.fromarray(g)
        self.occ.draw = ImageDraw.Draw(self.occ.img)
        self.occ.grid = None
        nb0 = len(self.boxes)
        # 2. the landmark first (it needs a whole block), then the frontage, then block interiors
        self.v3_landmark()
        anchors = self.v3_anchors()
        self.v3_only = True
        stats = dict(self.stats)
        self.place_buildings()
        infill = self.v3_infill()
        self.v3_only = False
        self.stats = stats
        new = list(zip(self.boxes[nb0:], self.box_ids[nb0:]))
        # colour drift for the repeated facades (never the same shift next door; not the landmark)
        last = -1
        for box, pid in new:
            if pid == 'm99_office_tower':
                continue
            rows = self.inst[pid]
            for r in rows:
                if abs(r[0] - box[0]) < 0.03 and abs(r[2] - box[2]) < 0.03:
                    ci = int(RNG.integers(len(self.V3_TINTS)))
                    if ci == last:
                        ci = (ci + 1) % len(self.V3_TINTS)
                    last = ci
                    r[6:10] = [*self.V3_TINTS[ci], 1.0]
        # 3. nothing grows through the new buildings
        cut = 0
        for proto in list(self.inst):
            it = K.get(proto)
            if it is None or 'building' in it['tags'] or proto.startswith(('nc_building', 'm99_')):
                continue
            rows = self.inst[proto]
            if not rows:
                continue
            xs = np.array([r[0] for r in rows]); zs = np.array([r[2] for r in rows])
            inside = np.zeros(len(rows), bool)
            for box, pid in new:
                near = (np.abs(xs - box[0]) < 70) & (np.abs(zs - box[2]) < 70)
                if near.any():
                    inside[near] |= self._in_rect(xs[near], zs[near], box, 0.2)
            if inside.any():
                cut += int(inside.sum())
                self.inst[proto] = [r for r, o in zip(rows, inside) if not o]
        for key in ('tree_pits', 'bus_shelters', 'signposts'):
            rows = getattr(self, key)
            setattr(self, key, [r for r in rows if not any(self._in_rect(r[0], r[2], b, 0.3) for b, _ in new)])
        # 4. rooftop boards back on tall v3 buildings near the avenues (same screen ids, so LiveSurfaces keeps them)
        tall = [(b, pid) for b, pid in new if b[4] >= 15.0 and pid != 'm99_office_tower']
        avenues = [s for s in self.roads['segments'] if s['base'] == 'avenue' and dt(*np.array(s['samples'][len(s['samples']) // 2])[[0, 2]])]
        used = set()
        rehomed = 0
        for bb in roof_boards:
            best = None
            for i, (b, pid) in enumerate(tall):
                if i in used:
                    continue
                for s in avenues:
                    P = np.array([[q[0], q[2]] for q in s['samples']])
                    dd = np.linalg.norm(P - np.array([b[0], b[2]]), axis=1)
                    j = int(np.argmin(dd))
                    if dd[j] < 45 and (best is None or dd[j] < best[0]):
                        best = (dd[j], i, P[j])
            if best is None:
                continue
            _, i, P = best
            used.add(i)
            b = tall[i][0]
            to = P - np.array([b[0], b[2]])
            face = math.degrees(math.atan2(to[0], to[1]))
            face = b[6] + round((face - b[6]) / 90.0) * 90.0
            bb = dict(bb, p=[b[0], round(b[1] + b[4] / 2, 2), b[2]], yaw=round(face % 360, 1))
            self.billboards.append(bb)
            rehomed += 1
        # 5. street furniture and trees; the toy-kit power poles and their overhead lines leave the district
        swaps = self.v3_street()
        swaps.update(self.v3_street_kit())
        swaps.update(self.v3_shop_signs(new))
        swaps['paved_m2'] = self.v3_paving()
        runs0 = len(self.power)
        gone_poles = [q for run in self.power if sum(dt(q[0], q[2]) for q in run) * 2 > len(run) for q in run]
        self.power = [run for run in self.power if not sum(dt(q[0], q[2]) for q in run) * 2 > len(run)]
        for proto in [k for k in self.inst if k.startswith('kr_electricity')]:
            self.inst[proto] = [r for r in self.inst[proto] if not any(abs(r[0] - q[0]) < 0.05 and abs(r[2] - q[2]) < 0.05 for q in gone_poles)]
        swaps['power_runs_removed'] = runs0 - len(self.power)
        self.stats['v3_downtown'] = dict(removed=removed, buildings=len(new), anchors=anchors, infill=infill, trees_cut=cut, rooftop_boards=f'{rehomed}/{len(roof_boards)}', **swaps)
        log(f'v3 downtown: {removed}; {len(new)} v3 buildings ({sum(p == "m99_office_tower" for _, p in new)} tower), '
            f'{cut} plants cut, rooftop boards {rehomed}/{len(roof_boards)}, swaps {swaps} ({time.time() - t0:.1f}s)')

    def _v3_add_building(self, pid, x, z, yaw, sc=1.0, margin=1.2, why=None, slope=1.6, allow=(0,)):
        it = self.kit.get(pid)
        w, d, hh = it['w'] * sc, it['d'] * sc, it['h'] * sc
        if not self.occ.rect_free(x, z, w, d, yaw, margin=margin, allow=allow):
            if why is not None:
                a = math.radians(yaw)
                U, V = np.meshgrid(np.arange(-w / 2, w / 2 + 0.01, 1.0), np.arange(-d / 2, d / 2 + 0.01, 1.0))
                codes = self.occ.codes((x + U * math.cos(a) + V * math.sin(a)).ravel(), (z - U * math.sin(a) + V * math.cos(a)).ravel())
                for c in set(codes.tolist()) - {0}:
                    why[c] = why.get(c, 0) + 1
            return False
        ys = self.g.y([x - w / 2, x + w / 2, x, x - w / 2, x + w / 2], [z - d / 2, z + d / 2, z, z + d / 2, z - d / 2])
        if ys.max() - ys.min() > slope * (1 + max(w, d) / 30):
            if why is not None:
                why['slope'] = why.get('slope', 0) + 1
            return False
        ybase = float(ys.min()) + 0.02
        self.add(pid, x, ybase, z, yaw, sc, 1.0, None)
        self.boxes.append([round(float(x), 2), round(ybase + hh / 2, 2), round(float(z), 2), round(w, 2), round(hh, 2), round(d, 2), round(yaw % 360, 1)])
        self.box_ids.append(pid)
        self.occ.rect(x, z, w, d, yaw, Occupancy.BUILDING, margin=0.6)
        return True

    def v3_landmark(self):
        """The 105 m 99.Miles office tower: one, on a whole block close to Timeline Square (seen down Consensus
        Boulevard and from the hovercar), its podium front on the nearest street."""
        if 'm99_office_tower' not in self.kit.items:
            return
        it = self.kit.get('m99_office_tower')
        grid_yaw = -L.DT_ROT
        poly = np.array(L.DOWNTOWN_POLY, float)
        # the block nearest the square is 53 x 50 m inside its streets: the tower at 0.9 (94.5 m) fits it
        for sc in (0.9, 0.85, 1.0):
            w, d = it['w'] * sc, it['d'] * sc
            best = None
            for u in np.arange(-240, 240, 3.0):
                for v in np.arange(-200, 180, 3.0):
                    x, z = L.dt_local_to_world(u, v)
                    r = math.hypot(x, z)
                    if r < 70 or r > 200 or not G.point_in_poly(x, z, poly):
                        continue
                    for k in range(4):
                        yaw = grid_yaw + 90.0 * k
                        if not self.occ.rect_free(x, z, w, d, yaw, margin=0.6):
                            continue
                        # its podium front (+z) towards the nearest road
                        a = math.radians(yaw)
                        f = np.array([math.sin(a), math.cos(a)])
                        ts = np.arange(d / 2 + 1.0, d / 2 + 14.0, 1.0)
                        codes = self.occ.codes(x + f[0] * ts, z + f[1] * ts)
                        if not (codes == Occupancy.ROAD).any():
                            continue
                        score = abs(r - 100.0) + float(np.argmax(codes == Occupancy.ROAD))
                        if best is None or score < best[0]:
                            best = (score, x, z, yaw)
            if best is not None:
                _, x, z, yaw = best
                if self._v3_add_building('m99_office_tower', x, z, yaw, sc, margin=0.6):
                    log(f'v3 downtown: office tower at ({x:.0f}, {z:.0f}) yaw {yaw % 360:.0f} scale {sc}')
                    return
        log('v3 downtown: no block for the office tower')

    def v3_infill(self):
        """Block interiors from the v3 kit: the Chicago walk-ups and the apartment complex where they fit, Neighbourhood
        City mid-rises in the smaller gaps, squared to the grid."""
        K = self.kit
        poly = np.array(L.DOWNTOWN_POLY, float)
        grid_yaw = -L.DT_ROT
        cands = []
        for u in np.arange(-260, 260, 7.0):
            for v in np.arange(-260, 200, 7.0):
                x, z = L.dt_local_to_world(u, v)
                if G.point_in_poly(x, z, poly):
                    cands.append((x, z))
        n = 0
        for i in RNG.permutation(len(cands)):
            x, z = cands[i]
            if self.occ.codes([x], [z])[0] != Occupancy.FREE:
                continue
            r = math.hypot(x, z)
            pool = (['m99_apt_complex'] * 2 + ['m99_chicago_a'] * 2 + self.V3_NC_TALL) if r < 125 else \
                (['m99_chicago_a'] + self.V3_NC_TALL * 2) if r < 215 else (self.V3_NC_TALL + ['nc_building_04'])
            pool = [p for p in pool if p in K.items]
            for pid in [pool[j] for j in RNG.permutation(len(pool))][:3]:
                sc = RNG.uniform(1.04, 1.14) if pid.startswith('nc_') else 1.0
                if self._v3_add_building(pid, x, z, grid_yaw + 90.0 * int(RNG.integers(4)), sc):
                    n += 1
                    break
        return n

    def v3_street(self):
        """Downtown street furniture to the v3 props: lamps to the Poly Haven street lamp (avenues) and the Neighbourhood
        City double lamp, benches to the modular seating, bins to the steel barrel, hydrants to the Poly Haven hydrant,
        round planters to potted plants and small planters, and every tree to a Neighbourhood City planter tree (its own
        planter, so the tree pits under them go)."""
        K = self.kit.items
        dt = lambda x, z: self.district_at(x, z) == 'timeline'
        lamp_road = 'ph_street_lamp_01' if 'ph_street_lamp_01' in K else None
        lamp_walk = 'nc_prop_lamp_double_01' if 'nc_prop_lamp_double_01' in K else lamp_road
        simple = {'bench_city': ('ph_modular_street_seating', 1.0), 'trash_can': ('ph_barrel_03', 1.0), 'kyc_trash_b': ('ph_barrel_03', 1.05),
                  'kyc_firehydrant': ('ph_fire_hydrant', 1.0)}
        trees = ('w_tree', 'tree_', 'kf_tree', 'pt_', 'lp_maple')
        counts = {}
        pits = [(r[0], r[2]) for r in self.tree_pits]
        drop_pits = set()
        for proto in list(self.inst):
            rows = self.inst[proto]
            if not rows:
                continue
            target = None
            if proto in ('kr_light_curved', 'street_lamp_kaykit'):
                target = 'road_lamp'
            elif proto in ('street_lamp_double', 'street_lamp_single'):
                target = 'walk_lamp'
            elif proto in simple:
                target = 'simple'
            elif proto == 'planter_round':
                target = 'planter'
            elif proto.startswith(trees):
                target = 'tree'
            if target is None:
                continue
            keep = []
            for r in rows:
                if not dt(r[0], r[2]):
                    keep.append(r)
                    continue
                x, y, z, yaw, sc, sy = r[:6]
                if target == 'road_lamp' and lamp_road:
                    self.add(lamp_road, x, y, z, yaw, 1.45, 1.0)          # 5.6 m: the Poly Haven lamp is pedestrian height
                    new = lamp_road
                elif target == 'walk_lamp' and lamp_walk:
                    self.add(lamp_walk, x, y, z, yaw, 1.25, 1.0)
                    new = lamp_walk
                elif target == 'simple' and simple[proto][0] in K:
                    self.add(simple[proto][0], x, y, z, yaw, simple[proto][1], 1.0)
                    new = simple[proto][0]
                elif target == 'planter':
                    # plaza trees stand in a planter_round of their own (scale 1.6): the planter tree replaces both
                    if sc > 1.3:
                        counts['planter_under_tree'] = counts.get('planter_under_tree', 0) + 1
                        continue
                    new = self.V3_SMALL_PLANTERS[int(RNG.integers(len(self.V3_SMALL_PLANTERS)))]
                    if new not in K:
                        keep.append(r)
                        continue
                    self.add(new, x, y, z, RNG.uniform(0, 360), RNG.uniform(1.0, 1.15) if new.startswith('ph_') else 1.0, 1.0)
                elif target == 'tree':
                    new = self.V3_TREES[int(RNG.integers(len(self.V3_TREES)))]
                    if new not in K:
                        keep.append(r)
                        continue
                    self.add(new, x, y + 0.08, z, RNG.uniform(0, 360), RNG.uniform(0.95, 1.12), 1.0)
                    for k, (px, pz) in enumerate(pits):
                        if abs(px - x) < 0.05 and abs(pz - z) < 0.05:
                            drop_pits.add(k)
                else:
                    keep.append(r)
                    continue
                counts[new] = counts.get(new, 0) + 1
            self.inst[proto] = keep
        # every downtown pit goes: the planter trees bring their own planter, and a pit whose tree a reviewed repair moved
        # (or a shot frame cleared) would be left empty
        n0 = len(self.tree_pits)
        self.tree_pits = [r for k, r in enumerate(self.tree_pits) if k not in drop_pits and not dt(r[0], r[2])]
        counts['tree_pits_removed'] = n0 - len(self.tree_pits)
        return counts

    # -------------------------------------------------------------- world v3: street level (2026-10-22)
    V3_PAVING_PNG = os.path.join(REPO, 'client', 'Assets', 'World', 'Island', 'Textures', 'paving_mask.png')

    def v3_paving(self):
        """Downtown paving: every metre of Timeline Downtown that is not park, water or field is paved (forecourts, gaps
        between buildings, block interiors) instead of grass. A 1 m mask over the district's bounds (1 = paved, soft 3 m
        edge at the district line) that the terrain shader paves with the sidewalk stone; its domain goes into
        dress.json (paving). Returns the paved area (m2)."""
        poly = np.array(L.DOWNTOWN_POLY, float)
        x0, z0 = poly.min(0) - 8.0
        x1, z1 = poly.max(0) + 8.0
        W, H = int(math.ceil(x1 - x0)), int(math.ceil(z1 - z0))
        img = Image.new('L', (W, H), 0)
        ImageDraw.Draw(img).polygon([(x - x0, H - 1 - (z - z0)) for x, z in L.DOWNTOWN_POLY], fill=255)
        inside = np.asarray(img) > 0
        xs = x0 + np.arange(W) + 0.5
        zs = z0 + (H - 1 - np.arange(H)) + 0.5
        X, Z = np.meshgrid(xs, zs)
        codes = self.occ.codes(X.ravel(), Z.ravel()).reshape(H, W)
        paved = inside & ~np.isin(codes, (Occupancy.PARK, Occupancy.WATER, Occupancy.FIELD))
        m = ndimage.gaussian_filter(paved.astype(np.float32), 1.2)
        m = np.clip((m - 0.15) / 0.7, 0, 1)
        Image.fromarray((m * 255 + 0.5).astype(np.uint8), 'L').save(self.V3_PAVING_PNG, optimize=True)
        self.paving = dict(min=[round(float(x0), 2), round(float(z0), 2)], size=[W, H], png='World/Island/Textures/paving_mask.png')
        area = int(paved.sum())
        log(f'v3 paving: {area} m2 paved downtown ({W} x {H} m mask)')
        return area

    def v3_paving_all(self):
        """The paving mask over every paved district: Timeline Downtown, The Trenches and Dubai Marina (dense frontage,
        yards and forecourts are paved, not grass lots); Residences keeps its gardens. Same 1 m mask format as v3_paving,
        one domain over their union."""
        polys = [np.array(L.DOWNTOWN_POLY, float)] + [np.array(self._district_poly(d), float) for d in ('trenches', 'dubai_marina')]
        # world fix-ups: the domain also covers the islet for its footpath
        allp = np.concatenate(polys + [np.array(self._district_poly('islet'), float)])
        x0, z0 = allp.min(0) - 8.0
        x1, z1 = allp.max(0) + 8.0
        W, H = int(math.ceil(x1 - x0)), int(math.ceil(z1 - z0))
        img = Image.new('L', (W, H), 0)
        dr = ImageDraw.Draw(img)
        for P in polys:
            dr.polygon([(x - x0, H - 1 - (z - z0)) for x, z in P], fill=255)
        inside = np.asarray(img) > 0
        xs = x0 + np.arange(W) + 0.5
        zs = z0 + (H - 1 - np.arange(H)) + 0.5
        X, Z = np.meshgrid(xs, zs)
        codes = self.occ.codes(X.ravel(), Z.ravel()).reshape(H, W)
        paved = inside & ~np.isin(codes, (Occupancy.PARK, Occupancy.WATER, Occupancy.FIELD))
        m = ndimage.gaussian_filter(paved.astype(np.float32), 1.2)
        m = np.clip((m - 0.15) / 0.7, 0, 1)
        # world polish (2026-10-23): green = The Trenches' yards (worn concrete, asphalt patches, oil and puddles in the
        # terrain shader) instead of the downtown sidewalk slab; the harbour's riverside land counts too
        yard_img = Image.new('L', (W, H), 0)
        yd = ImageDraw.Draw(yard_img)
        for P in [np.array(self._district_poly('trenches'), float)] + [np.array(q, float) for q in self.V3_SITE_EXTRA.get('trenches', [])]:
            yd.polygon([(x - x0, H - 1 - (z - z0)) for x, z in P], fill=255)
        yard = (np.asarray(yard_img) > 0).astype(np.float32)
        path_img = Image.new('L', (W, H), 0)
        ImageDraw.Draw(path_img).line([(x - x0, H - 1 - (z - z0)) for x, z in self.islet_path()], fill=255, width=3, joint='curve')
        path = np.clip((ndimage.gaussian_filter((np.asarray(path_img) > 0).astype(np.float32), 0.9) - 0.1) / 0.6, 0, 1)
        # the mask is RG on the device: the path is r (amount) with g = 0.35 (yards are g = 1), g spread 3 m past the path
        # so its filtered edge never reads as sidewalk stone
        pg = ndimage.binary_dilation(path > 0.01, iterations=3).astype(np.float32) * 0.35
        rgb = np.stack([np.maximum(m, path), np.maximum(yard * (m > 0), pg), np.zeros_like(m)], -1)
        Image.fromarray((rgb * 255 + 0.5).astype(np.uint8), 'RGB').save(self.V3_PAVING_PNG, optimize=True)
        self.paving = dict(min=[round(float(x0), 2), round(float(z0), 2)], size=[W, H], png='World/Island/Textures/paving_mask.png')
        log(f'v3 paving (downtown, trenches, marina): {int(paved.sum())} m2 ({W} x {H} m mask)')

    def v3_street_kit(self):
        """The rest of the toy street kit downtown goes semi-real (art/processed/world_v3/v3_street): traffic lights and
        stop signs, bins, the vendor carts, rows of parked two-wheelers become bike racks, bus shelters become the glass
        shelter model; then drains at the kerbs, manhole covers in the lanes and bollards at the crossings."""
        K = self.kit.items
        dt = lambda x, z: self.district_at(x, z) == 'timeline'
        # (old proto, new proto, yaw offset, scale): the v3 traffic light and stop sign face +Z (the toy ones -Z)
        swap = {'kr_traffic_light': ('v3_traffic_light', -180.0, 1.0), 'kr_road_sign_stop': ('v3_stop_sign', -180.0, 1.0),
                'trash_can': ('v3_trash_bin', 0.0, 1.0), 'kyc_trash_b': ('v3_trash_bin', 0.0, 1.0), 'ph_barrel_03': ('v3_trash_bin', 0.0, 1.0),
                'pp_vendor_cart': ('v3_vendor_cart', 0.0, 1.0)}
        counts = {}
        for old, (new, dyaw, sc) in swap.items():
            if new not in K or not self.inst.get(old):
                continue
            keep = []
            for r in self.inst[old]:
                if not dt(r[0], r[2]):
                    keep.append(r)
                    continue
                self.add(new, r[0], r[1], r[2], (r[3] + dyaw) % 360.0, sc, 1.0)
                counts[new] = counts.get(new, 0) + 1
            self.inst[old] = keep
        # parked two-wheelers: each kerbside row becomes one bike rack along the kerb
        if 'v3_bike_rack' in K:
            rows = []
            for proto in ('pp_scooter', 'pp_moped', 'pp_bicycle'):
                keep = []
                for r in self.inst.get(proto, []):
                    (rows if dt(r[0], r[2]) else keep).append(r)
                self.inst[proto] = keep
            used = [False] * len(rows)
            for i, r in enumerate(rows):
                if used[i]:
                    continue
                cl = [j for j in range(len(rows)) if not used[j] and abs(rows[j][0] - r[0]) < 4.5 and abs(rows[j][2] - r[2]) < 4.5]
                for j in cl:
                    used[j] = True
                cx = float(np.mean([rows[j][0] for j in cl])); cz = float(np.mean([rows[j][2] for j in cl]))
                if len(cl) >= 2:
                    a = math.degrees(math.atan2(rows[cl[-1]][0] - rows[cl[0]][0], rows[cl[-1]][2] - rows[cl[0]][2])) - 90.0   # rack's X along the row
                else:
                    a = r[3]
                self.add('v3_bike_rack', cx, r[1], cz, a % 360.0, 1.0, 1.0)
                counts['v3_bike_rack'] = counts.get('v3_bike_rack', 0) + 1
        # bus shelters downtown: the modelled glass shelter (open side to the road, as the built one) instead of the boxes
        if 'v3_bus_shelter' in K:
            keep = []
            for b in self.bus_shelters:
                if dt(b[0], b[2]):
                    self.add('v3_bus_shelter', b[0], b[1], b[2], b[3], 1.0, 1.0)
                    counts['v3_bus_shelter'] = counts.get('v3_bus_shelter', 0) + 1
                else:
                    keep.append(b)
            self.bus_shelters = keep
        # drains at both kerbs every ~24 m, a manhole cover in a lane every ~45 m, along downtown streets
        nd = nm = 0
        for s in self.roads['segments']:
            if s['kind'] in ('bridge', 'tunnel') or s['base'] in ('dirt', 'service', 'highway') or not s.get('kerb'):
                continue
            p, y, tan, rn = self.frame(s['samples'])
            if len(p) < 4 or not dt(*p[len(p) // 2]):
                continue
            sl = G.arc_lengths(p)
            for side in (1.0, -1.0):
                edge = s['widthRight'] if side > 0 else s['widthLeft']
                pos = RNG.uniform(6.0, 18.0)
                while pos < sl[-1] - 8.0 and 'v3_storm_drain' in K:
                    k = min(max(int(np.searchsorted(sl, pos)), 1), len(p) - 1)
                    q = p[k] + rn[k] * side * (edge - 0.32)
                    yaw = math.degrees(math.atan2(tan[k][0], tan[k][1]))
                    self.add('v3_storm_drain', q[0], float(y[k]) + 0.004, q[1], (yaw + 90.0) % 360.0, 1.0, 1.0)
                    nd += 1
                    pos += RNG.uniform(20.0, 28.0)
            if 'v3_manhole' in K:
                lw = s.get('laneWidth', 3.3)
                pos = RNG.uniform(10.0, 30.0)
                while pos < sl[-1] - 10.0:
                    k = min(max(int(np.searchsorted(sl, pos)), 1), len(p) - 1)
                    lanes = max(1, int(s['lanesForward']))
                    off = (s.get('median', 0.0) * 0.5 + lw * (RNG.integers(lanes) + 0.5)) * (1.0 if RNG.random() < 0.5 else -1.0)
                    q = p[k] + rn[k] * off
                    self.add('v3_manhole', q[0], float(y[k]) + 0.004, q[1], RNG.uniform(0, 360), 1.0, 1.0)
                    nm += 1
                    pos += RNG.uniform(38.0, 55.0)
        counts['v3_storm_drain'] = nd
        counts['v3_manhole'] = nm
        # bollards: a pair on each downtown crossing's kerb corner
        nb = 0
        if 'v3_bollard' in K:
            for j in self.roads['intersections']:
                parts = j.get('parts') or [j]
                for part in parts:
                    for a in part['approaches']:
                        seg = self.segs.get(a['segment'])
                        if seg is None or seg['sidewalk'] <= 0 or not dt(a['right'][0], a['right'][2]):
                            continue
                        dx, dz = a['dir']
                        rnv = np.array([dz, -dx])
                        base = np.array([a['right'][0], a['right'][2]]) + np.array([dx, dz]) * 1.6
                        yq = a['cut'][1] + 0.15
                        for k2 in (0.45, 1.6):
                            q = base + rnv * k2
                            if self.occ.codes([q[0]], [q[1]])[0] in (Occupancy.BUILDING,):
                                continue
                            self.add('v3_bollard', q[0], yq, q[1], 0.0, 1.0, 1.0)
                            nb += 1
        counts['v3_bollard'] = nb
        log(f'v3 street kit: {counts}')
        return counts

    def v3_shop_signs(self, new):
        """Blade shop signs (the CT parody names of the sign atlas, built by Unity from shopSigns) on the street fronts of
        the v3 buildings downtown: one in two Neighbourhood City / Chicago walk-up fronts, at the ground-floor lintel."""
        n = 0
        for box, pid in new:
            if pid not in ('m99_chicago_a',) and not pid.startswith('nc_building') or RNG.random() > 0.55:
                continue
            x, yc, z, w, h, d, yaw = box
            if w < 5.0:
                continue
            a = math.radians(yaw)
            out = np.array([math.sin(a), math.cos(a)])
            tan = np.array([math.cos(a), -math.sin(a)])
            front = np.array([x, z]) + out * (d / 2)
            side = 1.0 if RNG.random() < 0.5 else -1.0
            q = front + tan * side * (w / 2 - 0.9) + out * 0.62
            ybase = yc - h / 2
            self.shop_signs.append([round(float(q[0]), 2), round(float(ybase + 3.9), 2), round(float(q[1]), 2), round(float(yaw) % 360.0, 1),
                                    int(RNG.integers(self.N_SHOP_SIGNS))])
            n += 1
        return {'v3_shop_signs': n}

    # -------------------------------------------------------------- world v3: the districts outside downtown (2026-10-23)
    V3_DISTRICTS = ('trenches', 'residences', 'dubai_marina')
    V3_SHEDS = ['dz_industrial_01', 'dz_industrial_02', 'dz_industrial_03', 'dz_industrial_04', 'dz_industrial_05', 'dz_industrial_10',
                'dz_industrial_12', 'dz_industrial_30', 'dz_industrial_21', 'dz_industrial_17']
    V3_SMALL_SHEDS = ['dz_industrial_11', 'dz_industrial_13', 'dz_industrial_29', 'dz_industrial_15']
    # yard structures: (id, cap) - tanks, silos, chimneys, the water tower, the gantry crane, the substation, the radio mast
    V3_YARD = [('dz_industrial_23', 4), ('dz_industrial_27', 2), ('dz_industrial_28', 6), ('dz_industrial_26', 6), ('dz_industrial_22', 3),
               ('dz_industrial_24', 3), ('dz_industrial_14', 2), ('dz_industrial_18', 2), ('dz_industrial_06', 4), ('dz_industrial_08', 1),
               ('dz_industrial_07', 3)]
    V3_FLATS = ['lg_midrise_flats', 'lg_astergea_flats_01', 'lg_astergea_flats_02']
    # unassigned land next to a district where its big buildings may stand (the marina's north bank across the ring road,
    # the harbour's riverside north of The Trenches)
    V3_SITE_EXTRA = {'dubai_marina': [[(350, -252), (700, -252), (700, -205), (350, -205)]],
                     'trenches': [[(-470, -175), (-250, -175), (-250, -120), (-470, -120)]]}
    V3_MAPLES = ['lp_maple_medium_1', 'lp_maple_medium_2', 'lp_maple_medium_3', 'lp_maple_large_1', 'lp_maple_large_2', 'lp_maple_large_3']
    # walk-in interiors: building -> (door_main y in model space (Blender, front -Y), interior room id)
    V3_ENTERABLE = {'lg_midrise_flats': (-6.8, 'int_apartment_modern')}

    def _district_poly(self, did):
        return next(p for d, p in self.districts if d == did)

    def _v3_clear(self, did):
        """Toy-kit buildings out of a district with their shopfront dressing, wall decor, shop signs and rooftop boards;
        their ground goes back to FREE (as v3_downtown does for Timeline)."""
        K = self.kit.items
        inside = lambda x, z: self.district_at(x, z) == did
        keep_b, keep_i, gone = [], [], []
        for box, pid in zip(self.boxes, self.box_ids):
            (gone if inside(box[0], box[2]) and K[pid]['kit'] not in ('nc', 'v3') else keep_b).append(box)
            if not (inside(box[0], box[2]) and K[pid]['kit'] not in ('nc', 'v3')):
                keep_i.append(pid)
        old_ids = set(pid for box, pid in zip(self.boxes, self.box_ids) if box in gone)
        for pid in old_ids:
            self.inst[pid] = [r for r in self.inst.get(pid, []) if not any(abs(r[0] - b[0]) < 0.03 and abs(r[2] - b[2]) < 0.03 for b in gone)]
        shopdress = ('kc_detail_awning', 'kc_detail_awning_wide', 'kc_detail_parasol_a', 'kc_detail_parasol_b', 'pp_vending_machine')
        for b in gone:
            self.wall_decor = [r for r in self.wall_decor if not self._in_rect(r[0], r[2], b, 0.6)]
            self.shop_signs = [r for r in self.shop_signs if not self._in_rect(r[0], r[2], b, 2.5)]
            for proto in shopdress:
                if self.inst.get(proto):
                    self.inst[proto] = [r for r in self.inst[proto] if not self._in_rect(r[0], r[2], b, 3.2)]
        self.billboards = [bb for bb in self.billboards if not (bb['kind'] == 'rooftop' and any(self._in_rect(bb['p'][0], bb['p'][2], b, 0.5) for b in gone))]
        self.boxes, self.box_ids = keep_b, keep_i
        g = self.occ.arr().copy()
        img = Image.new('L', (self.occ.W, self.occ.H), 0)
        ImageDraw.Draw(img).polygon([self.occ.px(x, z) for x, z in self._district_poly(did)], fill=1)
        mask = np.asarray(img) == 1
        g[mask & (g == Occupancy.BUILDING)] = Occupancy.FREE
        fronts = Image.new('L', (self.occ.W, self.occ.H), 0)
        fd = ImageDraw.Draw(fronts)
        for b in gone:
            fd.polygon([self.occ.px(x, z) for x, z in G.oriented_rect(b[0], b[2], b[3] + 7.0, b[5] + 7.0, b[6])], fill=1)
        g[(np.asarray(fronts) == 1) & (g == Occupancy.PROP)] = Occupancy.FREE
        self.occ.img = Image.fromarray(g)
        self.occ.draw = ImageDraw.Draw(self.occ.img)
        self.occ.grid = None
        return len(gone)

    def pool_v3_district(self, style):
        """Frontage pools outside downtown (photo-textured facades: no stretching, a little uniform scale at most)."""
        K = self.kit.items
        allowed = getattr(self, 'v3_district_styles', None)
        if allowed is not None and style not in allowed:
            return None
        have = lambda ids: [k for k in ids if k in K]
        if style == 'industrial':
            return dict(ids=have(self.V3_SHEDS) * 2 + have(self.V3_SMALL_SHEDS), gap=(2.0, 8.0), setback=(1.5, 6.0), scale=(1.0, 1.0),
                        sy=(1.0, 1.0), slope=2.0, alley=0.12)
        if style == 'suburb':
            # lower density than downtown: flats with garden gaps, the Neighbourhood City mid-rises between them
            return dict(ids=have(self.V3_FLATS) * 3 + have(['nc_building_01', 'nc_building_03', 'nc_building_05', 'nc_building_04']),
                        gap=(6.0, 15.0), setback=(3.0, 6.5), scale=(1.0, 1.0), sy=(1.0, 1.0), slope=1.6, alley=0.0)
        if style == 'marina':
            return dict(ids=have(['m99_apt_complex']) * 3 + have(['nc_building_01', 'nc_building_05']), gap=(3.0, 9.0),
                        setback=(1.0, 3.0), scale=(1.0, 1.0), sy=(1.0, 1.0), slope=1.8, alley=0.0)
        # world districts 2: Builder Hills' hacker houses and campus flats on the terraces, the farm's sheds well apart, the
        # beach front's low kiosks and cafes
        if style == 'hills':
            return dict(ids=have(self.V3_FLATS) * 2 + have(['nc_building_01', 'nc_building_03', 'nc_building_05', 'nc_building_04', 'nc_building_02']) * 2
                        + have(['m99_chicago_a']), gap=(5.0, 13.0), setback=(3.0, 7.0), scale=(1.0, 1.0), sy=(1.0, 1.0), slope=2.6, alley=0.0)
        if style == 'farm':
            return dict(ids=have(self.FARM_SHEDS), gap=(30.0, 70.0), setback=(8.0, 16.0), scale=(1.0, 1.0), sy=(1.0, 1.0), slope=3.0, alley=0.0)
        if style == 'beach':
            return dict(ids=have(['nc_building_06', 'nc_building_06', 'nc_building_04']), gap=(8.0, 18.0), setback=(2.0, 5.0), scale=(1.0, 1.0),
                        sy=(1.0, 1.0), slope=2.0, alley=0.0)
        return None

    def _v3_anchor(self, did, pid, cap, scales=(1.0,), prefer=None, margin=0.3, why=None):
        """Place up to cap copies of a big model on street sides in a district (one per side); prefer(x, z) ranks spots."""
        segs = [s for s in self.roads['segments'] if s['kind'] not in ('bridge', 'tunnel') and s['base'] not in ('mountain', 'dirt')]
        it = self.kit.get(pid)
        cands = []
        for s in segs:
            p, y, tan, rn = self.frame(s['samples'])
            if len(p) < 4 or self.district_at(*p[len(p) // 2]) != did:
                continue
            sl = G.arc_lengths(p)
            for side in (1.0, -1.0):
                edge = (s['widthRight'] if side > 0 else s['widthLeft']) + s['sidewalk']
                for pos in np.arange(4.0, max(4.0, sl[-1] - 4.0), 4.0):
                    kc = min(max(int(np.searchsorted(sl, pos)), 1), len(p) - 1)
                    P, n_ = p[kc], rn[kc] * side
                    if self.district_at(*P) != did:
                        continue
                    cands.append((prefer(*P) if prefer else RNG.random(), s['id'], side, P, n_, edge))
        cands.sort(key=lambda c: c[0])
        made, sides = 0, set()
        for _, sid, side, P, n_, edge in cands:
            if made >= cap:
                break
            if (sid, side) in sides:
                continue
            for sc in scales:
                d = it['d'] * sc
                c = P + n_ * (edge + RNG.uniform(1.0, 3.0) + d / 2)
                yaw = math.degrees(math.atan2(-n_[0], -n_[1]))
                if self._v3_add_building(pid, c[0], c[1], yaw, sc, margin=margin, why=why):
                    made += 1
                    sides.add((sid, side))
                    break
        return made

    def v3_sites(self, did, order, field, dmin, dmax, spacing=6.0, margin=1.0, slope=1.6, why=None):
        """Big footprints on free ground of a district: candidates every spacing metres whose distance in field (a
        distance transform on the occupancy raster: road or water) is within [dmin, dmax]; the facade (+Z) turns to the
        nearest road / water (the field's falling gradient), squared to 5 degrees. order: [(id, count, scale), ...]."""
        polys = [np.array(self._district_poly(did), float)] + [np.array(q, float) for q in self.V3_SITE_EXTRA.get(did, [])]
        allp = np.concatenate(polys)
        x0, z0 = allp.min(0); x1, z1 = allp.max(0)
        gy, gx = np.gradient(field)
        own = lambda x, z: self.district_at(x, z) in (did, None)
        pts = [(x, z) for x in np.arange(x0, x1, spacing) for z in np.arange(z0, z1, spacing)
               if any(G.point_in_poly(x, z, P) for P in polys) and own(x, z)]
        made = {}
        for pid, count, sc in order:
            if pid not in self.kit.items:
                continue
            it = self.kit.get(pid)
            n = 0
            for i in RNG.permutation(len(pts)):
                if n >= count:
                    break
                x, z = pts[i]
                ox = int(np.clip(round(x - Occupancy.X0), 0, Occupancy.W - 1)); oy = int(np.clip(round(Occupancy.H - 1 - (z - Occupancy.Z0)), 0, Occupancy.H - 1))
                dd = field[oy, ox]
                # the footprint's front edge is dd - depth/2 from the road / water
                if not (dmin <= dd - it['d'] * sc / 2 <= dmax):
                    continue
                vx, vz = -gx[oy, ox], gy[oy, ox]                 # raster rows run north to south: flip the row gradient
                if abs(vx) + abs(vz) < 1e-6:
                    continue
                yaw = round(math.degrees(math.atan2(vx, vz)) / 5.0) * 5.0
                for dyaw in (0.0, 15.0, -15.0, 30.0, -30.0, 90.0, -90.0):
                    if self._v3_add_building(pid, x, z, yaw + dyaw, sc, margin=margin, why=why, slope=slope,
                                             allow=(Occupancy.FREE, Occupancy.PROP, Occupancy.TREE)):
                        n += 1
                        break
            made[pid] = made.get(pid, 0) + n
        return made

    def v3_yards(self, did, pool, spacing, cap, road_min=9.0, margin=1.5):
        """Behind the frontage: structures on free ground at least road_min from a road, squared to the nearest street."""
        poly = np.array(self._district_poly(did), float)
        x0, z0 = poly.min(0); x1, z1 = poly.max(0)
        road_d = ndimage.distance_transform_edt(self.occ.arr() != Occupancy.ROAD)
        caps = dict(pool)
        made = {k: 0 for k in caps}
        n = 0
        pts = [(x, z) for x in np.arange(x0, x1, spacing) for z in np.arange(z0, z1, spacing)]
        for i in RNG.permutation(len(pts)):
            if n >= cap:
                break
            x, z = pts[i]
            x += RNG.uniform(-spacing / 3, spacing / 3); z += RNG.uniform(-spacing / 3, spacing / 3)
            if not G.point_in_poly(x, z, poly) or self.occ.codes([x], [z])[0] != Occupancy.FREE:
                continue
            ox = int(np.clip(round(x - Occupancy.X0), 0, Occupancy.W - 1)); oy = int(np.clip(round(Occupancy.H - 1 - (z - Occupancy.Z0)), 0, Occupancy.H - 1))
            if road_d[oy, ox] < road_min:
                continue
            live = [k for k in caps if k in self.kit.items and made[k] < caps[k]]
            if not live:
                break
            pid = live[int(RNG.integers(len(live)))]
            # square to the nearest street: the direction to the closest road pixel
            win = road_d[max(0, oy - 40):oy + 41, max(0, ox - 40):ox + 41]
            yaw = float(RNG.integers(4)) * 90.0
            if self._v3_add_building(pid, x, z, yaw + RNG.uniform(-4, 4), 1.0, margin=margin):
                made[pid] += 1
                n += 1
        return made

    def _v3_cut_inside(self, new):
        K = self.kit.items
        cut = 0
        for proto in list(self.inst):
            it = K.get(proto)
            if it is None or 'building' in it['tags'] or proto.startswith(('nc_building', 'm99_', 'dz_', 'lg_')):
                continue
            rows = self.inst[proto]
            if not rows:
                continue
            xs = np.array([r[0] for r in rows]); zs = np.array([r[2] for r in rows])
            inside = np.zeros(len(rows), bool)
            for box, pid in new:
                near = (np.abs(xs - box[0]) < 110) & (np.abs(zs - box[2]) < 110)
                if near.any():
                    inside[near] |= self._in_rect(xs[near], zs[near], box, 0.3)
            if inside.any():
                cut += int(inside.sum())
                self.inst[proto] = [r for r, o in zip(rows, inside) if not o]
        for key in ('tree_pits', 'bus_shelters', 'signposts'):
            rows = getattr(self, key)
            setattr(self, key, [r for r in rows if not any(self._in_rect(r[0], r[2], b, 0.3) for b, _ in new)])
        return cut

    def v3_district_street(self, districts=None):
        """Street furniture and trees in the three districts to the v3 kit: TampaJoey hydrants, bins and benches, the Poly
        Haven lamp on roads, LOLIPOP maples for every broadleaf / blossom tree (palms stay on the marina coast), the v3
        traffic lights, stop signs and bus shelters; the toy power poles leave; ground clumps go where it is paved."""
        K = self.kit.items
        DS = set(districts or self.V3_DISTRICTS)
        second = districts is not None
        dist = lambda x, z: self.district_at(x, z)
        lamp_road = 'ph_street_lamp_01' if 'ph_street_lamp_01' in K else None
        walk = {'residences': ('tj_street_light', 1.3), 'trenches': ('ph_street_lamp_01', 1.45), 'dubai_marina': ('nc_prop_lamp_double_01', 1.25),
                'builder_hills': ('tj_street_light', 1.3), 'airdrop_farm': ('ph_street_lamp_01', 1.45), 'boardwalk': ('ph_street_lamp_02', 1.4),
                'breakpoint': ('nc_prop_lamp_double_01', 1.25), 'mountain': ('ph_street_lamp_01', 1.45), 'islet': ('ph_street_lamp_02', 1.4)}
        bench = {'residences': ['tj_bench_park_long', 'tj_bench_park_short'], 'trenches': ['tj_bench_concrete'], 'dubai_marina': ['tj_bench_metal', 'ph_modular_street_seating'],
                 'builder_hills': ['tj_bench_park_long', 'tj_bench_metal'], 'airdrop_farm': ['ph_wooden_bench'], 'boardwalk': ['ph_modular_street_seating', 'tj_bench_metal'],
                 'breakpoint': ['tj_bench_park_long', 'tj_bench_park_short'], 'mountain': ['ph_wooden_bench'], 'islet': ['tj_bench_park_short']}
        bins = {'residences': ['tj_trash_large', 'tj_trash_small'], 'trenches': ['tj_trash_commercial', 'tj_trash_large'], 'dubai_marina': ['tj_trash_cap', 'tj_trash_small'],
                'builder_hills': ['tj_trash_small', 'tj_trash_cap'], 'airdrop_farm': ['ph_wooden_bucket', 'tj_trash_large'], 'boardwalk': ['tj_trash_cap', 'tj_trash_small'],
                'breakpoint': ['tj_trash_small', 'tj_trash_large'], 'mountain': ['tj_trash_large'], 'islet': ['tj_trash_cap']}
        # the second pass leaves the semi-real forest alone (only toy trees become maples there)
        broad = ('w_tree_common', 'w_tree_twisted', 'kf_tree', 'w_pine') if second else \
            ('w_tree_common', 'w_tree_twisted', 'kf_tree', 'tree_blossom', 'tree_', 'w_pine', 'pt_')
        extra = [(did, np.array(q, float)) for did, qs in self.V3_SITE_EXTRA.items() for q in qs]
        counts = {}
        pits = {(round(r[0], 1), round(r[2], 1)) for r in self.tree_pits}
        new_rows = []

        def put(proto, x, y, z, yaw, sc=1.0):
            if proto in K:
                new_rows.append((proto, x, y, z, yaw, sc))
                counts[proto] = counts.get(proto, 0) + 1
                return True
            return False
        for proto in list(self.inst):
            rows = self.inst[proto]
            if not rows:
                continue
            kind = None
            if proto in ('kr_light_curved', 'street_lamp_kaykit'):
                kind = 'road_lamp'
            elif proto in ('street_lamp_double', 'street_lamp_single'):
                kind = 'walk_lamp'
            elif proto == 'bench_city':
                kind = 'bench'
            elif proto in ('trash_can', 'kyc_trash_b', 'ph_barrel_03'):
                kind = 'bin'
            elif proto == 'kyc_firehydrant':
                kind = 'hydrant'
            elif proto == 'pp_vendor_cart' or (second and proto == 'pp_market_stalls'):
                kind = 'cart'
            elif proto == 'kr_traffic_light':
                kind = 'signal'
            elif proto == 'kr_road_sign_stop':
                kind = 'stop'
            elif proto.startswith(broad) and not proto.startswith('palm'):
                kind = 'tree'
            elif proto.startswith('kr_electricity'):
                kind = 'pole'
            elif 'ground_detail' in K.get(proto, {}).get('tags', ()) and not second:
                kind = 'detail'
            if kind is None:
                continue
            keep = []
            for r in rows:
                did = dist(r[0], r[2])
                if did is None and kind == 'tree':
                    # the districts' extra building land (the marina's north bank, the harbour's riverside) gets maples too
                    did = next((d_ for d_, P in extra if G.point_in_poly(r[0], r[2], P)), None)
                if did not in DS:
                    keep.append(r)
                    continue
                x, y, z, yaw, sc, sy = r[:6]
                if kind == 'road_lamp':
                    ok = put(lamp_road, x, y, z, yaw, 1.45)
                elif kind == 'walk_lamp':
                    ok = put(walk[did][0], x, y, z, yaw, walk[did][1])
                elif kind == 'bench':
                    ok = put(bench[did][int(RNG.integers(len(bench[did])))], x, y, z, yaw + (180.0 if did != 'dubai_marina' else 0.0))
                elif kind == 'bin':
                    ok = put(bins[did][int(RNG.integers(len(bins[did])))], x, y, z, yaw)
                elif kind == 'hydrant':
                    ok = put('tj_hydrant', x, y, z, yaw)
                elif kind == 'cart':
                    ok = put('v3_vendor_cart', x, y, z, yaw)
                elif kind == 'signal':
                    ok = put('v3_traffic_light', x, y, z, (yaw - 180.0) % 360.0)
                elif kind == 'stop':
                    ok = put('v3_stop_sign', x, y, z, (yaw - 180.0) % 360.0)
                elif kind == 'tree':
                    m = self.V3_MAPLES[int(RNG.integers(len(self.V3_MAPLES)))]
                    street = (round(x, 1), round(z, 1)) in pits
                    ok = put(m, x, y + 0.02, z, RNG.uniform(0, 360), RNG.uniform(0.82, 0.95) if street else RNG.uniform(0.85, 1.1))
                elif kind == 'pole' or (kind == 'detail' and did != 'residences' and not second):
                    ok = True                                   # dropped: toy poles everywhere, grass clumps where it is paved
                    counts[kind + '_removed'] = counts.get(kind + '_removed', 0) + 1
                else:
                    ok = False
                if not ok:
                    keep.append(r)
            self.inst[proto] = keep
        for proto, x, y, z, yaw, sc in new_rows:
            self.add(proto, x, y, z, yaw, sc, 1.0)
        # the toy power runs leave the three districts with their poles
        gone_poles = [q for run in self.power if sum(dist(q[0], q[2]) in DS for q in run) * 2 > len(run) for q in run]
        self.power = [run for run in self.power if not sum(dist(q[0], q[2]) in DS for q in run) * 2 > len(run)]
        for proto in [k for k in self.inst if k.startswith('kr_electricity')]:
            self.inst[proto] = [r for r in self.inst[proto] if not any(abs(r[0] - q[0]) < 0.05 and abs(r[2] - q[2]) < 0.05 for q in gone_poles)]
        # bus shelters: the modelled glass shelter (marina, trenches) or TampaJoey's (residences)
        keep = []
        for b in self.bus_shelters:
            did = dist(b[0], b[2])
            if did in DS and put('tj_bus_stop' if did == 'residences' else 'v3_bus_shelter', b[0], b[1], b[2], b[3] + (180.0 if did == 'residences' else 0.0)):
                continue
            keep.append(b)
        self.bus_shelters = keep
        return counts

    def v3_trench_dressing(self, new):
        """Yard dressing round the industrial buildings: a dumpster beside the front, pallet stacks and tarp crates, a
        utility box now and then (all on free ground, never on a road)."""
        n = 0
        for box, pid in new:
            if not pid.startswith(('dz_', 'm99_warehouse')) or box[3] < 6.0:
                continue
            cx, cy, cz, w, h, d, yaw = box
            a = math.radians(yaw)
            fwd = np.array([math.sin(a), math.cos(a)]); right = np.array([math.cos(a), -math.sin(a)])
            ybase = cy - h / 2
            for proto, prob, along, out, rot in (('tj_dumpster', 0.7, 0.5, 1.6, 90.0), ('tj_pallet_a', 0.45, -0.42, 1.2, 0.0),
                                                 ('tj_tarp_crates', 0.3, 0.3, -1.0, 0.0), ('tj_pallet_b', 0.3, -0.2, 1.1, 30.0),
                                                 ('tj_utility_box', 0.25, -0.5, 1.8, 0.0)):
                if RNG.random() > prob or proto not in self.kit.items:
                    continue
                side = right * (w / 2 + 1.4) * np.sign(along) if out < 0 else right * w * along
                q = np.array([cx, cz]) + side + fwd * ((d / 2 + out) if out > 0 else (RNG.uniform(-d / 3, d / 3)))
                it = self.kit.get(proto)
                if not self.occ.rect_free(q[0], q[1], it['w'], it['d'], yaw + rot, margin=0.4):
                    continue
                yq = float(self.g.y([q[0]], [q[1]])[0])
                self.add(proto, q[0], yq + 0.01, q[1], yaw + rot + RNG.uniform(-6, 6), 1.0, 1.0)
                self.occ.rect(q[0], q[1], it['w'], it['d'], yaw + rot, Occupancy.PROP, margin=0.2)
                n += 1
        return n

    def _level_under(self, door, fwd, right, target, half, depth):
        """Lower every heightfield sample in the rectangle behind a door (plus one cell) to target; returns how far the
        ground rose above target there before. The edited field is written back by write_heights()."""
        g = self.g
        n, cell = g.n, g.cell
        xs = g.min[0] + np.arange(n) * cell
        zs = g.min[1] + np.arange(n) * cell
        c = door - fwd * (depth / 2)
        r = math.hypot(half, depth / 2) + 2 * cell
        ix = np.where(np.abs(xs - c[0]) <= r)[0]; iz = np.where(np.abs(zs - c[1]) <= r)[0]
        if len(ix) == 0 or len(iz) == 0:
            return 0.0
        X, Z = np.meshgrid(xs[ix], zs[iz])
        dx, dz = X - c[0], Z - c[1]
        u = dx * right[0] + dz * right[1]
        v = -(dx * fwd[0] + dz * fwd[1])                                 # into the building
        inside = (np.abs(u) <= half + cell) & (np.abs(v) <= depth / 2 + cell)
        sub = g.h[np.ix_(iz, ix)]
        rise = float(np.max(np.where(inside, sub - target, -1e9)))
        sub = np.where(inside, np.minimum(sub, target), sub)
        g.h[np.ix_(iz, ix)] = sub
        self.heights_edited = True
        return max(0.0, rise)

    def write_heights(self):
        if not getattr(self, 'heights_edited', False):
            return
        t = json.load(open(os.path.join(OUT, 'terrain.json')))
        q = np.clip((self.g.h - t['hmin']) / (t['hmax'] - t['hmin']), 0, 1)
        # a separate file (the Unity builder prefers it): dress.py always starts again from islandgen's height.r16
        (q * 65535 + 0.5).astype('<u2').tofile(os.path.join(OUT, 'height_dressed.r16'))
        log('heightfield levelled under the walk-in rooms -> height_dressed.r16')

    def v3_interior_doors(self, new):
        """Walk-in doors: every enterable v3 building gets its door (world position of door_main, facing the street) and
        the interior room the client streams in behind it; only on near-flat sites (the room's floor is level)."""
        self.interior_doors = []
        for box, pid in new:
            if pid not in self.V3_ENTERABLE:
                continue
            dy, room = self.V3_ENTERABLE[pid]
            cx, cy, cz, w, h, d, yaw = box
            a = math.radians(yaw)
            fwd = np.array([math.sin(a), math.cos(a)])                  # model +Z (Unity) = Blender -Y = the facade
            q = np.array([cx, cz]) + fwd * (-dy)
            # the room behind the door (11.5 x 10.3 m, inside the 24 x 16 m flats): the heightfield is levelled under it to
            # just below the door's base, falling off inside the footprint (never visible: the building covers it), so a
            # hillside lot no longer pushes ground up through the room's floor
            ybase = cy - h / 2
            # the street in front of the door must meet it (a hillside lot sinks the facade's downhill-placed base)
            outside = float(self.g.y([q[0] + fwd[0] * 1.8], [q[1] + fwd[1] * 1.8])[0])
            if not (-0.6 <= outside - ybase <= 0.25):
                self.stats['interior_doors_off_grade'] = self.stats.get('interior_doors_off_grade', 0) + 1
                continue
            right = np.array([math.cos(a), -math.sin(a)])
            rise = self._level_under(q, fwd, right, ybase - 0.08, half=6.4, depth=10.6)
            self.stats.setdefault('interior_door_rises', []).append(round(rise, 2))
            floor = ybase + 0.03
            self.interior_doors.append(dict(p=[round(float(q[0]), 3), round(float(ybase), 3), round(float(q[1]), 3)], yaw=round(yaw % 360.0, 2),
                                            floor=round(float(floor), 3), building=pid, box=box, room=room))
        log(f'v3 interior doors: {len(self.interior_doors)}')

    def v3_districts(self):
        """World v3 outside downtown (after v3_downtown, own RNG): The Trenches gets the 99.Miles warehouse and the
        DanielZhabotinsky sheds on its streets with tanks, chimneys, the water tower and gantry cranes in the yards;
        Residences the Lost Gecko flats with Neighbourhood City mid-rises and Chicago walk-ups at a lower density (some
        flats enterable); Dubai Marina the glass towers on the waterfront. Street furniture and trees to the v3 kit, and
        the dense two paved like downtown."""
        global RNG
        K = self.kit.items
        if not any(k.startswith('dz_industrial') for k in K):
            log('v3 districts: no harbour kit, skipped')
            return
        t0 = time.time()
        RNG = np.random.default_rng(V3_SEED + 1)
        removed = {did: self._v3_clear(did) for did in self.V3_DISTRICTS}
        if os.environ.get('CTL_DRESS_OCC'):
            pal = np.array([[60, 90, 60], [110, 110, 115], [200, 60, 200], [40, 80, 160], [230, 140, 60], [90, 170, 90], [170, 160, 80],
                            [240, 240, 240], [20, 110, 20]] + [[0, 0, 0]] * 247, np.uint8)
            Image.fromarray(pal[self.occ.arr()]).save(os.environ['CTL_DRESS_OCC'])
        nb0 = len(self.boxes)
        why = {}
        sea_d = ndimage.distance_transform_edt(self.occ.arr() != Occupancy.WATER)

        def near_water(x, z):
            ox = int(np.clip(round(x - Occupancy.X0), 0, Occupancy.W - 1)); oy = int(np.clip(round(Occupancy.H - 1 - (z - Occupancy.Z0)), 0, Occupancy.H - 1))
            return float(sea_d[oy, ox])
        road_d = ndimage.distance_transform_edt(self.occ.arr() != Occupancy.ROAD)
        anchors = {
            # the marina's glass: office towers and the apartment complex on the basin's free banks, facing the water
            'marina': self.v3_sites('dubai_marina', [('m99_office_tower', 1, 0.85), ('m99_office_tower', 1, 0.76), ('m99_office_tower', 1, 0.68),
                                                     ('m99_apt_complex', 4, 1.0)], sea_d, 6.0, 150.0, margin=1.0, slope=3.0, why=why.setdefault('marina', {})),
            # the harbour's big halls where the river banks leave room, fronts to the nearest street
            'trenches': self.v3_sites('trenches', [('m99_warehouse', 2, 1.0), ('dz_industrial_20', 1, 1.0), ('dz_industrial_19', 1, 1.0),
                                                   ('dz_industrial_09', 1, 1.0), ('dz_industrial_17', 2, 1.0), ('dz_industrial_21', 2, 1.0)],
                                      road_d, 2.0, 30.0, spacing=5.0, margin=1.0, slope=3.0, why=why.setdefault('trenches', {})),
            'm99_chicago_a@res': self._v3_anchor('residences', 'm99_chicago_a', 4, why=why),
        }
        self.v3_only = True
        self.v3_district_pools = True
        self.v3_district_styles = {'industrial', 'suburb', 'marina'}
        stats = dict(self.stats)
        self.place_buildings()
        self.v3_district_styles = None
        self.v3_only = False
        self.v3_district_pools = False
        self.stats = stats
        yards = self.v3_yards('trenches', self.V3_YARD + [(k, 3) for k in self.V3_SMALL_SHEDS], 8.0, 40, road_min=4.0, margin=1.0)
        new = list(zip(self.boxes[nb0:], self.box_ids[nb0:]))
        last = -1
        for box, pid in new:
            if pid == 'm99_office_tower':
                continue
            for r in self.inst[pid]:
                if abs(r[0] - box[0]) < 0.03 and abs(r[2] - box[2]) < 0.03:
                    ci = int(RNG.integers(len(self.V3_TINTS)))
                    if ci == last:
                        ci = (ci + 1) % len(self.V3_TINTS)
                    last = ci
                    r[6:10] = [*self.V3_TINTS[ci], 1.0]
        cut = self._v3_cut_inside(new)
        street = self.v3_district_street()
        dressing = self.v3_trench_dressing(new)
        self.v3_interior_doors(new)
        by = {}
        for box, pid in new:
            d_ = self.district_at(box[0], box[2])
            by.setdefault(d_, {}).setdefault(pid.split('_')[0], 0)
            by[d_][pid.split('_')[0]] += 1
        self.stats['v3_districts'] = dict(removed=removed, buildings=len(new), by_district=by, anchors=anchors, yards=yards, plants_cut=cut,
                                          street=street, yard_dressing=dressing, interior_doors=len(self.interior_doors))
        log(f'v3 district sites blocked by occupancy code / slope: {why}')
        log(f'v3 districts: removed {removed}; {len(new)} v3 buildings {by}; anchors {anchors}; yards {yards}; {cut} plants cut; '
            f'street {street}; {dressing} yard props ({time.time() - t0:.1f}s)')

    def v3_trenches_junction(self):
        """World districts 2: the first street-level view of The Trenches (the device route's spot by the Perp Pit lot, facing
        east along Harbour Road) read open - one kiosk and distant sheds. Frame it: sheds on both sides of the view between
        14 and 60 m, squared to the view, fronted by chain-link yard edges and pallets, so the eye meets an industrial street."""
        global RNG
        K = self.kit.items
        if 'dz_industrial_02' not in K:
            return {}
        RNG = np.random.default_rng(V3_SEED + 17)
        if os.environ.get('CTL_DRESS_OCC_J'):
            pal = np.array([[60, 90, 60], [110, 110, 115], [200, 60, 200], [40, 80, 160], [230, 140, 60], [90, 170, 90], [170, 160, 80],
                            [240, 240, 240], [20, 110, 20]] + [[0, 0, 0]] * 247, np.uint8)
            Image.fromarray(pal[self.occ.arr()]).save(os.environ['CTL_DRESS_OCC_J'])
        sx, sz, vy = -332.5, -279.0, 101.0
        a = math.radians(vy)
        f = np.array([math.sin(a), math.cos(a)]); r = np.array([math.cos(a), -math.sin(a)])
        road_d = ndimage.distance_transform_edt(self.occ.arr() != Occupancy.ROAD)

        def rd(x, z):
            ox = int(np.clip(round(x - Occupancy.X0), 0, Occupancy.W - 1)); oy = int(np.clip(round(Occupancy.H - 1 - (z - Occupancy.Z0)), 0, Occupancy.H - 1))
            return float(road_d[oy, ox])
        made = []
        # the view crosses Quay Street diagonally 16-40 m out; beyond it (44-75 m) the lots were half empty, so the street read
        # as an open junction: close the vista there with long sheds facing back across the road, then flank it
        made = []
        big = ['dz_industrial_02', 'dz_industrial_01', 'dz_industrial_12', 'dz_industrial_03', 'dz_industrial_10']
        cands = [(along, lat) for along in np.arange(46.0, 80.0, 2.0) for lat in np.arange(-24.0, 25.0, 2.0)]
        cands.sort(key=lambda c: abs(c[1]) * 1.4 + (c[0] - 46.0))
        for pid in big:
            if len(made) >= 3:
                break
            it = K[pid]
            for along, lat in cands:
                q = np.array([sx, sz]) + f * (along + it['d'] / 2) + r * lat
                if rd(*q) < it['d'] / 2 + 1.0:
                    continue
                if self._v3_add_building(pid, q[0], q[1], vy + 180.0, 1.0, margin=0.8, slope=3.5, allow=(Occupancy.FREE, Occupancy.PROP, Occupancy.TREE)):
                    made.append((self.boxes[-1], pid))
                    break
        # yard edges: a chain-link run along each new shed's street side, pallets and a dumpster at its corner
        fence = K.get('yd_chainlink_fence')
        props = 0
        for box, pid in made:
            cx, cy, cz, w, h, d, yaw = box
            ya = math.radians(yaw)
            fwd = np.array([math.sin(ya), math.cos(ya)]); right = np.array([math.cos(ya), -math.sin(ya)])
            if fence is not None:
                L_ = fence['w']
                for j in range(-int(w / L_ / 2), int(w / L_ / 2) + 1):
                    q = np.array([cx, cz]) + fwd * (d / 2 + 4.0) + right * j * L_
                    it = fence
                    if rd(*q) > 1.5 and self.occ.rect_free(q[0], q[1], it['w'], it['d'], yaw, margin=0.1, allow=(Occupancy.FREE, Occupancy.TREE)):
                        self.add('yd_chainlink_fence', q[0], float(self.g.y([q[0]], [q[1]])[0]) - 0.02, q[1], yaw, 1.0, 1.0)
                        self.occ.rect(q[0], q[1], it['w'], it['d'], yaw, Occupancy.PROP, margin=0.1)
                        props += 1
            for proto, off in (('tj_dumpster', (0.42, 1.6)), ('tj_pallet_a', (-0.4, 1.3)), ('tj_pallet_b', (-0.32, 2.2)), ('v3_tyre_stack', (0.2, 2.0))):
                if proto not in K:
                    continue
                it = K[proto]
                q = np.array([cx, cz]) + right * w * off[0] + fwd * (d / 2 + off[1])
                if self.occ.rect_free(q[0], q[1], it['w'], it['d'], yaw, margin=0.2, allow=(Occupancy.FREE, Occupancy.TREE)):
                    self.add(proto, q[0], float(self.g.y([q[0]], [q[1]])[0]) + 0.01, q[1], yaw + RNG.uniform(-8, 8), 1.0, 1.0)
                    self.occ.rect(q[0], q[1], it['w'], it['d'], yaw, Occupancy.PROP, margin=0.1)
                    props += 1
        cut = self._v3_cut_inside(made)
        out = dict(sheds=[pid for _, pid in made], yard_props=props, plants_cut=cut)
        log(f'v3 trenches junction: {out}')
        return out

    def v3_trenches_lot(self):
        """World fix-ups: the first Trenches view still read open - the lots on its right (between Quay Street and the
        water) held only small sheds and clutter, and a lone kiosk stood in the middle of the junction. The lots between
        12 and 80 m on that side are cleared and take the big halls (the 15 m warehouse dz_industrial_04 and the long
        dz_industrial_01 / 03 / 02 halls) squared to the street, fronts to it; the junction corridor is cleared of the
        small kiosks and clutter, so the eye runs down an industrial street walled by real buildings."""
        global RNG
        K = self.kit.items
        if 'dz_industrial_04' not in K:
            return {}
        RNG = np.random.default_rng(V3_SEED + 37)
        sx, sz, vy = -332.5, -279.0, 101.0
        a = math.radians(vy)
        f = np.array([math.sin(a), math.cos(a)]); r = np.array([math.cos(a), -math.sin(a)])
        rel = lambda x, z: (float((np.array([x, z]) - [sx, sz]) @ f), float((np.array([x, z]) - [sx, sz]) @ r))
        # the lot side: the land between the street the view runs down and the water (one connected piece of non-road
        # ground, seeded 40 m out and 18 m right), 10-100 m out
        g0 = self.occ.arr()
        H_, W_ = g0.shape
        xs = Occupancy.X0 + np.arange(W_); zs = Occupancy.Z0 + (H_ - 1 - np.arange(H_))
        X, Z = np.meshgrid(xs, zs)
        A = (X - sx) * f[0] + (Z - sz) * f[1]; R = (X - sx) * r[0] + (Z - sz) * r[1]
        box_ = (A >= 10) & (A <= 125) & (R >= -20) & (R <= 62)
        lab, _ = ndimage.label(box_ & ~np.isin(g0, (Occupancy.ROAD, Occupancy.WATER, Occupancy.VENUE)))
        seed = np.array([sx, sz]) + f * 40.0 + r * 18.0
        sid = lab[int(round(H_ - 1 - (seed[1] - Occupancy.Z0))), int(round(seed[0] - Occupancy.X0))]
        lotm = (lab == sid) if sid > 0 else np.zeros_like(box_)
        corr = (A >= 4) & (A <= 38) & (np.abs(R) <= 7)

        def at(m, x, z):
            ox = int(np.clip(round(x - Occupancy.X0), 0, W_ - 1)); oy = int(np.clip(round(H_ - 1 - (z - Occupancy.Z0)), 0, H_ - 1))
            return bool(m[oy, ox])
        in_lot = lambda x, z: at(lotm, x, z)
        in_corr = lambda x, z: at(corr, x, z)
        out = dict(removed=0, halls=[])
        # 1. clear the lot side and the corridor: small buildings, props and plants (street furniture by the kerb stays)
        street = ('ph_street_lamp', 'tj_street_light', 'v3_traffic_light', 'v3_stop_sign', 'tj_hydrant', 'v3_bus_shelter')
        keep_b, keep_i = [], []
        for box, pid in zip(self.boxes, self.box_ids):
            if (in_lot(box[0], box[2]) or in_corr(box[0], box[2])) and K.get(pid, {}).get('kind') != 'venue':
                self.inst[pid] = [q for q in self.inst.get(pid, []) if not (abs(q[0] - box[0]) < 0.03 and abs(q[2] - box[2]) < 0.03)]
                out['removed'] += 1
                continue
            keep_b.append(box); keep_i.append(pid)
        self.boxes, self.box_ids = keep_b, keep_i
        for proto in list(self.inst):
            if proto.startswith(street):
                continue
            rows = self.inst[proto]
            keep = [q for q in rows if not (in_lot(q[0], q[2]) or in_corr(q[0], q[2]))]
            out['removed'] += len(rows) - len(keep)
            self.inst[proto] = keep
        g = self.occ.arr().copy()
        region = lotm | corr
        g[region & np.isin(g, (Occupancy.BUILDING, Occupancy.PROP, Occupancy.TREE))] = Occupancy.FREE
        self.occ.img = Image.fromarray(g); self.occ.draw = ImageDraw.Draw(self.occ.img); self.occ.grid = None
        # 2. the halls: fronts to the nearest street, squared to it, as close to the view as they fit
        road = self.occ.arr() == Occupancy.ROAD
        dist, (iy, ix) = ndimage.distance_transform_edt(~road, return_indices=True)

        def near_road(x, z):
            ox = int(np.clip(round(x - Occupancy.X0), 0, W_ - 1)); oy = int(np.clip(round(H_ - 1 - (z - Occupancy.Z0)), 0, H_ - 1))
            return float(dist[oy, ox]), np.array([Occupancy.X0 + ix[oy, ox], Occupancy.Z0 + (H_ - 1 - iy[oy, ox])])
        halls = ['dz_industrial_04', 'dz_industrial_01', 'dz_industrial_03', 'dz_industrial_02', 'dz_industrial_12', 'dz_industrial_02', 'dz_industrial_12']
        cands = [(al, la) for al in np.arange(14.0, 112.0, 2.0) for la in np.arange(-10.0, 46.0, 2.0)]
        cands.sort(key=lambda c: c[0] + c[1] * 0.8)
        made = []
        why = {}
        for pid in halls:
            if len(made) >= 4:
                break
            it = K[pid]
            for al, la in cands:
                q = np.array([sx, sz]) + f * al + r * la
                d0, rp = near_road(*q)
                if d0 < it['d'] / 2 + 2.5 or d0 > it['d'] / 2 + 9.0:
                    continue
                to = rp - q
                yaw = math.degrees(math.atan2(to[0], to[1]))
                # square to the street: the street's direction is perpendicular to 'to'; snap to the view grid's 90 degrees
                # when within 20 degrees (the halls then line up with each other)
                for snap in (vy, vy + 90, vy + 180, vy + 270):
                    if abs((yaw - snap + 180) % 360 - 180) < 20:
                        yaw = snap
                        break
                if self._v3_add_building(pid, q[0], q[1], yaw, 1.0, margin=1.0, slope=4.0, why=why,
                                         allow=(Occupancy.FREE, Occupancy.TREE, Occupancy.PARK, Occupancy.FIELD)):
                    made.append((self.boxes[-1], pid))
                    out['halls'].append((pid, round(al), round(la), round(yaw)))
                    break
        # 3. yard edges: chain-link along each hall's street side, a dumpster and pallets at its corner
        props = 0
        for box, pid in made:
            cx, cy, cz, w, h, d, yaw = box
            ya = math.radians(yaw)
            fwd = np.array([math.sin(ya), math.cos(ya)]); right = np.array([math.cos(ya), -math.sin(ya)])
            # at the hall's ends (the street side is the forecourt)
            for proto, off in (('tj_dumpster', (1.0, 0.25)), ('tj_pallet_a', (-1.0, 0.3)), ('v3_tyre_stack', (-1.0, -0.1)), ('tj_pallet_b', (1.0, -0.2))):
                if proto not in K:
                    continue
                it = K[proto]
                q = np.array([cx, cz]) + right * off[0] * (w / 2 + 1.6) + fwd * d * off[1]
                if self.occ.rect_free(q[0], q[1], it['w'], it['d'], yaw, margin=0.2, allow=(Occupancy.FREE, Occupancy.TREE)):
                    self.add(proto, q[0], float(self.g.y([q[0]], [q[1]])[0]) + 0.01, q[1], yaw + RNG.uniform(-8, 8), 1.0, 1.0)
                    self.occ.rect(q[0], q[1], it['w'], it['d'], yaw, Occupancy.PROP, margin=0.1)
                    props += 1
        out['props'] = props
        out['fails'] = why
        if os.environ.get('CTL_DRESS_OCC_LOT'):
            pal = np.array([[60, 90, 60], [110, 110, 115], [200, 60, 200], [40, 80, 160], [230, 140, 60], [90, 170, 90], [170, 160, 80],
                            [240, 240, 240], [20, 110, 20]] + [[0, 0, 0]] * 247, np.uint8)
            Image.fromarray(pal[self.occ.arr()]).save(os.environ['CTL_DRESS_OCC_LOT'])
        out['plants_cut'] = self._v3_cut_inside(made)
        log(f'v3 trenches lot: {out}')
        return out

    # -------------------------------------------------------------- world districts 2 (2026-10-23)
    V3_DISTRICTS2 = ('builder_hills', 'airdrop_farm', 'boardwalk', 'breakpoint', 'mountain', 'islet')
    # the Namaqualand granites read orange on the grey mountain (Seeker/editor 2026-10-23): rock faces and the greyer mossy set
    # world fix-ups: the rock-face scans (pr_face_*) are open shells (a scanned cliff surface, no back): placed freely they
    # read as dark floating shards from most sides; only the closed boulders are used now (open bottoms sunk)
    PR_GRANITE = ['pr_moss_b_02', 'pr_moss_b_07', 'pr_moss_b_06', 'pr_moss_a_04', 'pr_moss_a_02']
    GRANITE_TINT = (0.80, 0.82, 0.88, 1.0)      # the Namaqualand granite's orange towards the mountain's grey
    PR_MOSS = ['pr_moss_a_01', 'pr_moss_a_02', 'pr_moss_a_03', 'pr_moss_a_04', 'pr_moss_a_05', 'pr_moss_a_06', 'pr_moss_b_01', 'pr_moss_b_02',
               'pr_moss_b_04', 'pr_moss_b_06', 'pr_moss_b_07']
    FARM_SHEDS = ['dz_industrial_29', 'dz_industrial_29', 'dz_industrial_12', 'dz_industrial_11', 'dz_industrial_13', 'dz_industrial_02']
    FARM_SILOS = [('dz_industrial_24', 0.55), ('dz_industrial_26', 0.5), ('dz_industrial_28', 0.4), ('dz_industrial_22', 0.2)]
    FARM_PROPS = ['ph_wooden_barrels', 'ph_wooden_crate', 'ph_compost_bags', 'ph_propane_tank', 'ph_wooden_bucket', 'tj_pallet_a',
                  'tj_pallet_b', 'ph_picnic_table']
    # field crop -> semi-real card rows: (cards, scale, height scale, tint or None, row spacing m, step m)
    CROP_CARDS = {'wheat': (['pv_crop_tall'], (1.2, 1.45), (1.2, 1.5), (0.98, 0.80, 0.42), 2.0, 1.3),
                  'corn': (['pv_crop_tall'], (1.5, 1.8), (2.2, 2.7), (0.70, 0.86, 0.48), 2.2, 1.4),
                  'sunflower': (['pv_crop_sunflower', 'pv_crop_tall'], (1.3, 1.6), (1.6, 2.0), None, 2.2, 1.5),
                  'cabbage': (['pv_crop_leafy'], (1.0, 1.25), (0.8, 1.0), (0.72, 0.9, 0.62), 1.8, 1.3),
                  'lavender': (['pv_crop_lavender'], (1.1, 1.35), (1.0, 1.25), None, 1.8, 1.2),
                  'pumpkin': (['pv_crop_leafy', 'pv_crop_pumpkin'], (1.0, 1.3), (0.8, 1.0), None, 2.2, 1.5)}

    def v3_districts2(self):
        """The rest of the island on the v3 look (after v3_districts): Builder Hills' hacker houses and campus flats, Airdrop
        Farm's sheds, silos and yards with crop rows of semi-real cards, the Boardwalk's beach front, Breakpoint Park, the
        MtnDAO mountain and basecamp, Palm Islet. Island-wide: scanned rocks for the toy rocks, the toy pebbles and power
        runs out, the street furniture and the last toy trees to the v3 kit."""
        global RNG
        K = self.kit.items
        if 'pr_granite_a' not in K or 'dz_industrial_29' not in K:
            log('v3 districts 2: kit missing, skipped')
            return
        t0 = time.time()
        RNG = np.random.default_rng(V3_SEED + 13)
        DS = self.V3_DISTRICTS2
        stats = {}
        stats['removed'] = {did: self._v3_clear(did) for did in DS}
        nb0 = len(self.boxes)
        keep_stats = dict(self.stats)
        self.v3_only = True
        self.v3_district_pools = True
        self.v3_district_styles = {'hills', 'farm', 'beach'}
        self.place_buildings()
        self.v3_only = self.v3_district_pools = False
        self.v3_district_styles = None
        self.stats = keep_stats
        new = list(zip(self.boxes[nb0:], self.box_ids[nb0:]))
        last = -1
        for box, pid in new:
            for r in self.inst[pid]:
                if abs(r[0] - box[0]) < 0.03 and abs(r[2] - box[2]) < 0.03 and not pid.startswith('dz_'):
                    ci = int(RNG.integers(len(self.V3_TINTS)))
                    ci = (ci + 1) % len(self.V3_TINTS) if ci == last else ci
                    last = ci
                    r[6:10] = [*self.V3_TINTS[ci], 1.0]
        stats['buildings'] = {}
        for box, pid in new:
            d_ = self.district_at(box[0], box[2])
            stats['buildings'].setdefault(str(d_), {}).setdefault(pid.split('_')[0], 0)
            stats['buildings'][str(d_)][pid.split('_')[0]] += 1
        stats['farm_yards'] = self.v3_farm_yards(new)
        stats['plants_cut'] = self._v3_cut_inside(new)
        stats['street'] = self.v3_district_street(DS)
        stats['rocks'] = self.v3_rocks()
        stats['mountain_rocks'] = self.v3_mountain_rocks()
        stats['crops'] = self.v3_crops()
        stats['trees'] = self.v3_last_toy_trees()
        # world fix-ups: the last toy road lamps and stop sign (outside the districts: the country avenues) to the v3 kit,
        # as v3_district_street does inside them
        K = self.kit.items
        swapped = 0
        for old, new, sc, dyaw in (('kr_light_curved', 'ph_street_lamp_01', 1.45, 0.0), ('kr_road_sign_stop', 'v3_stop_sign', 1.0, -180.0)):
            if new not in K:
                continue
            for r in self.inst.get(old, []):
                self.add(new, r[0], r[1], r[2], (r[3] + dyaw) % 360.0, sc, sc)
                swapped += 1
            self.inst[old] = []
        stats['toy_street_swapped'] = swapped
        stats['islet'] = self.v3_islet()
        stats['basecamp'] = self.v3_basecamp()
        # the toy power runs (orange poles, sagging wires) leave the whole island
        stats['power_runs_removed'] = len(self.power)
        self.power = []
        for proto in [k for k in self.inst if k.startswith('kr_electricity')]:
            stats['power_poles_removed'] = stats.get('power_poles_removed', 0) + len(self.inst[proto])
            self.inst[proto] = []
        stats['power_lines'] = self.v3_power_lines()
        # the device route's eye spots stand clear: no tree, rock or shrub within 9 m (the camera sits ~5 m behind the player)
        clear = 0
        for proto in list(self.inst):
            if not proto.startswith(self.TREE_PREFIXES + ('pr_', 'pv_bush', 'pv_hedge')):
                continue
            rows = self.inst[proto]
            keep = [r for r in rows if not any(math.hypot(r[0] - sx, r[2] - sz) < 9.0 for sx, sz in self.ROUTE_SPOTS)]
            clear += len(rows) - len(keep)
            self.inst[proto] = keep
        stats['route_spots_cleared'] = clear
        self.stats['v3_districts2'] = stats
        log(f'v3 districts 2: {stats} ({time.time() - t0:.1f}s)')

    def v3_farm_yards(self, new):
        """Round each farm shed: a silo or two and a tank behind it, barrels, crates, compost bags and pallets at its side."""
        n = 0
        for box, pid in new:
            if self.district_at(box[0], box[2]) != 'airdrop_farm' or not pid.startswith('dz_'):
                continue
            cx, cy, cz, w, h, d, yaw = box
            a = math.radians(yaw)
            fwd = np.array([math.sin(a), math.cos(a)]); right = np.array([math.cos(a), -math.sin(a)])
            side = 1.0 if RNG.random() < 0.5 else -1.0
            k = 0
            for proto, prob in self.FARM_SILOS:
                if RNG.random() > prob or proto not in self.kit.items:
                    continue
                it = self.kit.get(proto)
                q = np.array([cx, cz]) + right * side * (w / 2 + it['w'] / 2 + 1.5 + k * (it['w'] + 1.0)) - fwd * (d / 4)
                if self._v3_add_building(proto, q[0], q[1], yaw, 1.0, margin=0.6, slope=2.5, allow=(Occupancy.FREE, Occupancy.PROP, Occupancy.TREE)):
                    n += 1
                    k += 1
            for _ in range(int(RNG.integers(3, 7))):
                proto = self.FARM_PROPS[int(RNG.integers(len(self.FARM_PROPS)))]
                if proto not in self.kit.items:
                    continue
                it = self.kit.get(proto)
                q = np.array([cx, cz]) - right * side * (w / 2 + RNG.uniform(1.2, 4.0)) + fwd * RNG.uniform(-d / 2, d / 2)
                ry = yaw + RNG.choice([0.0, 90.0]) + RNG.uniform(-12, 12)
                if not self.occ.rect_free(q[0], q[1], it['w'], it['d'], ry, margin=0.3):
                    continue
                self.add(proto, q[0], float(self.g.y([q[0]], [q[1]])[0]) + 0.01, q[1], ry, 1.0, 1.0)
                self.occ.rect(q[0], q[1], it['w'], it['d'], ry, Occupancy.PROP, margin=0.2)
                n += 1
        return n

    def v3_rocks(self):
        """The toy flat-shaded rocks become Poly Haven scans: granite boulders and rock faces on the mountain's scree and
        bare ground, mossy rocks in the woods; sized to the old rock's footprint (and a little bigger on the mountain)."""
        K = self.kit.items
        gran = [k for k in self.PR_GRANITE if k in K]
        moss = [k for k in self.PR_MOSS if k in K]
        n = 0
        rows_out = []
        for proto in [k for k in self.inst if k.startswith('kf_rock_')]:
            it0 = K.get(proto)
            for r in self.inst[proto]:
                x, y, z, yaw, sc, sy = r[:6]
                size0 = max(it0['w'], it0['d']) * sc if it0 else 1.4 * sc
                mtn = self.district_at(x, z) == 'mountain' or y > 60
                woods = float(self.g.sample(self.g.forest, [x], [z])[0]) > 0.35
                pool = moss if (woods and not mtn and moss) else gran
                pid = pool[int(RNG.integers(len(pool)))]
                it = K[pid]
                s = np.clip(size0 * (1.6 if mtn else 1.15) / max(it['w'], it['d']), 0.6, 4.0)
                # sunk by a third of its height on the mountain (the slopes are steep: a shallow sink floats the downhill side)
                sy_ = s * RNG.uniform(0.75, 1.05)
                rows_out.append((pid, x, y - (0.33 if mtn else 0.15) * it['h'] * sy_, z, RNG.uniform(0, 360), s, sy_))
                n += 1
            self.inst[proto] = []
        for pid, x, y, z, yaw, s, sy in rows_out:
            self.add(pid, x, y, z, yaw, s, sy, self.GRANITE_TINT if pid.startswith('pr_granite') else None)
        return n

    # world fix-ups (2026-10-23): the mountain's rocks follow its form
    MTN_FACES = ['pr_moss_a_02', 'pr_moss_a_04', 'pr_moss_a_05', 'pr_moss_a_03']
    MTN_SCREE = ['pr_moss_b_01', 'pr_moss_b_03', 'pr_moss_b_04', 'pr_moss_b_05', 'pr_moss_b_06']

    def v3_mountain_rocks(self):
        """The mountain's rocks were the toy rocks' random spots, scanned: dark blobs dotted over the slopes. They go, and the
        scans are placed by the ground's form instead: rock-face scans in outcrops along the cliff bands (steep ground,
        broad side to the valley), crowning the ridgelines (convex crests high up), and scree boulders in clusters at the
        cliff feet (moderate slopes just below steep ground). Roads, the lodge, the cairn and the route's spots stay clear."""
        K = self.kit.items
        faces = [k for k in self.MTN_FACES if k in K]
        scree = [k for k in self.MTN_SCREE if k in K]
        if not faces or not scree:
            return {}
        rng = np.random.default_rng(V3_SEED + 29)
        g = self.g
        in_mtn = lambda x, z, y: self.district_at(x, z) == 'mountain' or y > 60.0
        # 1. the random scans on the mountain go
        removed = 0
        for proto in [k for k in self.inst if k.startswith('pr_')]:
            rows = self.inst[proto]
            keep = [r for r in rows if not in_mtn(r[0], r[2], r[1])]
            removed += len(rows) - len(keep)
            self.inst[proto] = keep
        # 2. the ground's form on the 2 m grid
        h = g.h
        ny = 1.0 / np.sqrt(1.0 + g.slope ** 2)
        lap = ndimage.laplace(ndimage.gaussian_filter(h, 2.0))
        cliff = ny < 0.62
        near_cliff = ndimage.binary_dilation(cliff, iterations=4) & ~cliff
        # cliff feet: below steep ground (lower than the cliff cells next to them), moderate slope
        hmax_near = ndimage.maximum_filter(np.where(cliff, h, -1e9), size=9)
        foot = near_cliff & (hmax_near > h + 3.0) & (ny > 0.62) & (ny < 0.93)
        ridge = (lap < -0.35) & (ny > 0.45) & (ny < 0.9) & (h > 70.0)
        n = g.n
        xs = g.min[0] + np.arange(n) * g.cell
        zs = g.min[1] + np.arange(n) * g.cell
        cairn = np.array([-182.0, 513.0])
        lodge = np.array([-259.6, 412.2])

        def ok(x, z, y, r):
            if y < 30.0 or not in_mtn(x, z, y):
                return False
            if np.hypot(*(np.array([x, z]) - cairn)) < 14.0 or np.hypot(*(np.array([x, z]) - lodge)) < 34.0:
                return False
            if any(math.hypot(x - sx, z - sz) < 12.0 for sx, sz in self.ROUTE_SPOTS):
                return False
            return self.occ.rect_free(x, z, r * 2, r * 2, 0.0, margin=1.5, allow=(Occupancy.FREE, Occupancy.TREE))

        def cells(mask, spacing):
            """Jittered picks from a mask, at most one per spacing x spacing block."""
            iz, ix = np.nonzero(mask)
            if len(ix) == 0:
                return []
            order = rng.permutation(len(ix))
            taken = set()
            out = []
            for i in order:
                x = xs[ix[i]] + rng.uniform(-0.9, 0.9); z = zs[iz[i]] + rng.uniform(-0.9, 0.9)
                key = (int(x // spacing), int(z // spacing))
                if key in taken:
                    continue
                taken.add(key)
                out.append((x, z, iz[i], ix[i]))
            return out

        def downhill_yaw(iz, ix):
            gz_ = h[min(iz + 1, n - 1), ix] - h[max(iz - 1, 0), ix]
            gx_ = h[iz, min(ix + 1, n - 1)] - h[iz, max(ix - 1, 0)]
            return math.degrees(math.atan2(-gx_, -gz_))

        rows = []
        stats = dict(removed=removed, cliff=0, ridge=0, scree=0)
        # cliff bands: outcrops of two or three faces stepping along the contour, broad side to the valley
        for x, z, iz, ix in cells(cliff, 22.0):
            y = float(g.y([x], [z])[0])
            pid = faces[int(rng.integers(len(faces)))]
            it = K[pid]
            s = rng.uniform(1.6, 3.2)
            if not ok(x, z, y, max(it['w'], it['d']) * s * 0.5):
                continue
            yaw = downhill_yaw(iz, ix)
            along = np.array([math.cos(math.radians(yaw)), -math.sin(math.radians(yaw))])
            for k in range(int(rng.integers(1, 4))):
                q = np.array([x, z]) + along * (k - 1) * it['w'] * s * 0.7 + rng.uniform(-1.0, 1.0, 2)
                yq = float(g.y([q[0]], [q[1]])[0])
                sk = s * rng.uniform(0.7, 1.05)
                rows.append((pid if k == 0 else faces[int(rng.integers(len(faces)))], q[0], yq - 0.45 * it['h'] * sk, q[1],
                             yaw + rng.uniform(-20, 20), sk, sk * rng.uniform(0.8, 1.05)))
                stats['cliff'] += 1
        # ridgelines: single crowns, a little smaller
        for x, z, iz, ix in cells(ridge, 28.0):
            y = float(g.y([x], [z])[0])
            pid = faces[int(rng.integers(len(faces)))]
            it = K[pid]
            s = rng.uniform(1.1, 2.0)
            if not ok(x, z, y, max(it['w'], it['d']) * s * 0.5):
                continue
            rows.append((pid, x, y - 0.45 * it['h'] * s, z, rng.uniform(0, 360), s, s * rng.uniform(0.7, 0.95)))
            stats['ridge'] += 1
        # scree: clusters of boulders at the cliff feet
        for x, z, iz, ix in cells(foot, 19.0):
            y = float(g.y([x], [z])[0])
            if not ok(x, z, y, 3.0):
                continue
            for k in range(int(rng.integers(2, 5))):
                q = np.array([x, z]) + rng.normal(0, 2.2, 2)
                pid = scree[int(rng.integers(len(scree)))]
                it = K[pid]
                sk = rng.uniform(0.55, 1.35)
                yq = float(g.y([q[0]], [q[1]])[0])
                rows.append((pid, q[0], yq - 0.3 * it['h'] * sk, q[1], rng.uniform(0, 360), sk, sk * rng.uniform(0.75, 1.0)))
                stats['scree'] += 1
        # on the steep faces a centre-sunk boulder floats on its downhill side: its base goes below the lowest ground
        # under it (8 samples round its footprint), so it reads bedded into the slope
        for pid, x, y, z, yaw, sc, sy in rows:
            it = K[pid]
            r = 0.42 * max(it['w'], it['d']) * sc
            a = np.linspace(0, 2 * math.pi, 8, endpoint=False)
            gmin = float(g.y(x + r * np.cos(a), z + r * np.sin(a)).min())
            y = min(y, gmin - 0.12 * it['h'] * sy)
            self.add(pid, x, y, z, yaw, sc, sy, self.GRANITE_TINT if pid.startswith('pr_granite') else None)
        return stats

    # world fix-ups (2026-10-23): the Poly Haven electricity poles (modular_electricity_poles, CC0) on the country roads
    POLE_FULL = ['ph_power_pole_01', 'ph_power_pole_02', 'ph_power_pole_01', 'ph_power_pole_03']
    POLE_ROUTES = ('farm_road', 'farm_lane', 'farm_track', 'hills_drive', 'foothill_road')
    POLE_SCALE = 1.35          # the scans are 6.1 m; rural distribution poles stand ~8-9 m

    def v3_power_lines(self):
        """Overhead lines along Airdrop Farm's roads and the Builder Hills / foothill roads outside the built frontage: a
        pole every 36-44 m on one verge (3.5 m off the carriageway), crossarm square to the road, three wires strung by
        the builder (dress.json power: runs of [x, y, z, yaw, scale]). Runs break at junctions, drives and buildings."""
        K = self.kit.items
        poles = [k for k in self.POLE_FULL if k in K]
        if not poles:
            return {}
        rng = np.random.default_rng(V3_SEED + 31)
        n = runs = 0
        sc = self.POLE_SCALE
        for s_ in self.roads['segments']:
            if s_['route'] not in self.POLE_ROUTES or s_['kind'] in ('bridge', 'tunnel'):
                continue
            p, y, tan, rn = self.frame(s_['samples'])
            if len(p) < 4:
                continue
            sl = G.arc_lengths(p)
            side = 1.0 if (int(s_['id'][1:]) % 2) == 0 else -1.0
            edge = (s_['widthRight'] if side > 0 else s_.get('widthLeft', s_['widthRight'])) + s_['sidewalk'] + s_.get('shoulder', 0.0) + 3.0
            run = []
            pos = 8.0
            while pos < sl[-1] - 8:
                k = min(max(int(np.searchsorted(sl, pos)), 1), len(p) - 1)
                q = p[k] + rn[k] * side * edge
                face = math.degrees(math.atan2(tan[k][0], tan[k][1]))
                d_ = self.district_at(q[0], q[1])
                built = self.occ.codes([q[0]], [q[1]])[0] in (Occupancy.BUILDING, Occupancy.VENUE, Occupancy.ROAD, Occupancy.WATER)
                ok = (not built and d_ in ('airdrop_farm', 'builder_hills', 'mountain', None, 'countryside', 'breakpoint')
                      and self.occ.rect_free(q[0], q[1], 1.6, 1.6, face, margin=0.4,
                                             allow=(Occupancy.FREE, Occupancy.FIELD, Occupancy.PARK, Occupancy.TREE, Occupancy.PROP)))
                if ok and run:
                    # a span must not cross buildings or water
                    a = np.array(run[-1][0:3:2])
                    ts = np.linspace(0, 1, 12)[1:-1]
                    cs = self.occ.codes(a[0] + (q[0] - a[0]) * ts, a[1] + (q[1] - a[1]) * ts)
                    if np.isin(cs, (Occupancy.BUILDING, Occupancy.VENUE)).any():
                        ok = False
                if ok:
                    yq = float(self.g.y([q[0]], [q[1]])[0]) - 0.1
                    pid = poles[int(rng.integers(len(poles)))]
                    self.add(pid, q[0], yq, q[1], face, sc, sc)
                    self.occ.disc(q[0], q[1], 1.2, Occupancy.PROP)
                    run.append([round(float(q[0]), 2), round(yq, 2), round(float(q[1]), 2), round(face, 1), sc])
                    n += 1
                    pos += rng.uniform(36, 44)
                else:
                    if len(run) >= 2:
                        self.power.append(run); runs += 1
                    run = []
                    pos += 6.0
            if len(run) >= 2:
                self.power.append(run); runs += 1
            elif len(run) == 1:
                pass   # a lone pole still reads as a pole
        # no tree under the wires: trees within 3 m of a span go
        cut = 0
        spans = [(np.array(r[i][0:3:2]), np.array(r[i + 1][0:3:2])) for r in self.power for i in range(len(r) - 1)]
        if spans:
            A = np.array([a for a, b in spans]); B = np.array([b for a, b in spans])
            for proto in [k for k in self.inst if k.startswith(self.TREE_PREFIXES)]:
                rows = self.inst[proto]
                keep = []
                for r in rows:
                    pt = np.array([r[0], r[2]])
                    ab = B - A
                    t = np.clip(((pt - A) * ab).sum(1) / np.maximum((ab * ab).sum(1), 1e-6), 0, 1)
                    if np.min(np.hypot(*(A + ab * t[:, None] - pt).T)) < 3.0:
                        cut += 1
                        continue
                    keep.append(r)
                self.inst[proto] = keep
        return dict(poles=n, runs=runs, trees_cut=cut)

    def v3_crops(self):
        """The toy crop sprites become rows of the semi-real cards: tall grass tinted ripe for wheat, taller and greener for
        corn, sunflowers, leafy weeds for cabbage and pumpkin, lavender's violet; rows follow each field's direction."""
        K = self.kit.items
        gone = sum(len(self.inst.get(k, [])) for k in list(self.inst) if k.startswith('kn_crop'))
        for k in [k for k in self.inst if k.startswith('kn_crop')]:
            self.inst[k] = []
        n = 0
        for poly, ang, kind in L.FIELDS:
            spec = self.CROP_CARDS.get(kind)
            if spec is None:
                continue
            cards, scr, syr, tint, row_d, step = spec
            cards = [c for c in cards if c in K]
            if not cards:
                continue
            P = np.array(poly, float)
            c0 = P.mean(axis=0)
            a = math.radians(ang)
            u = np.array([math.sin(a), math.cos(a)]); v = np.array([u[1], -u[0]])
            for row in np.arange(-70, 70, row_d):
                for t in np.arange(-70, 70, step):
                    q = c0 + u * t + v * row + RNG.uniform(-0.2, 0.2, 2)
                    if not G.point_in_poly(q[0], q[1], P):
                        continue
                    yq = float(self.g.y([q[0]], [q[1]])[0])
                    tt = None
                    if tint is not None:
                        tt = tuple(np.clip(np.array(tint) * RNG.uniform(0.93, 1.05, 3), 0, 1)) + (1.0,)
                    self.add(cards[int(RNG.integers(len(cards)))], q[0], yq - 0.04, q[1], ang + RNG.uniform(-25, 25), RNG.uniform(*scr), RNG.uniform(*syr), tt)
                    n += 1
        return {'toy_removed': gone, 'cards': n}

    def v3_last_toy_trees(self):
        """Toy trees still standing (street trees outside the v3 districts, blossom, palms) -> maples, jacarandas and the
        scanned coastal trees."""
        K = self.kit.items
        maples = [k for k in self.V3_MAPLES if k in K]
        coast = [k for k in ('pt_island_a', 'pt_island_b') if k in K]
        bloom = [k for k in ('pt_jacaranda',) if k in K]
        counts = {}
        rows_out = []
        for proto in list(self.inst):
            if proto.startswith(('w_tree', 'w_pine', 'kf_tree')):
                pool, s = maples, (0.82, 0.98)
            elif proto.startswith('tree_blossom'):
                pool, s = bloom or maples, (0.8, 0.95)
            elif proto.startswith('palm'):
                pool, s = coast or None, (0.95, 1.2)
            else:
                continue
            if not pool:
                continue
            for r in self.inst[proto]:
                pid = pool[int(RNG.integers(len(pool)))]
                rows_out.append((pid, r[0], r[1] + 0.02, r[2], RNG.uniform(0, 360), RNG.uniform(*s)))
                counts[pid] = counts.get(pid, 0) + 1
            self.inst[proto] = []
        for pid, x, y, z, yaw, sc in rows_out:
            self.add(pid, x, y, z, yaw, sc, 1.0, self.foliage_tint(pid, x, z))
        return counts

    def islet_path(self):
        """World fix-ups: a gravel footpath from the causeway's end into the grove and round the islet's crown (a loop at
        about 22 m from its centre), in the paving mask (r amount, g = 0.35) so the terrain draws it."""
        c = np.array(self.plan['islet']['center'], float)
        start = np.array(self.plan['causeway']['path'][-1], float)
        r = min(22.0, float(self.plan['islet']['radius']) * 0.5)
        # enter from the causeway, join the loop at its nearest point, go round
        a0 = math.atan2(start[1] - c[1], start[0] - c[0])
        entry = c + r * np.array([math.cos(a0), math.sin(a0)])
        pts = [tuple(start), tuple((start + entry) / 2), tuple(entry)]
        for k in range(1, 37):
            a = a0 + k * 2 * math.pi / 36 + 0.12 * math.sin(k * 0.9)
            rr = r * (1.0 + 0.12 * math.sin(k * 0.7 + 1.3))
            pts.append(tuple(c + rr * np.array([math.cos(a), math.sin(a)])))
        return [(float(x), float(z)) for x, z in pts]

    def v3_islet(self):
        """Palm Islet: a grove of the scanned coastal trees, granite on its shore, a bench and a lamp by the causeway.
        World fix-ups: a gravel footpath loops through the grove (islet_path), trees and rocks stand off it, benches by it."""
        K = self.kit.items
        poly = np.array(self._district_poly('islet'), float)
        coast = [k for k in ('pt_island_a', 'pt_island_b') if k in K]
        path = self.islet_path()
        P = np.array(path)

        def path_d(x, z):
            a, b = P[:-1], P[1:]
            ab = b - a
            t = np.clip(((np.array([x, z]) - a) * ab).sum(1) / np.maximum((ab * ab).sum(1), 1e-6), 0, 1)
            return float(np.min(np.hypot(*(a + ab * t[:, None] - np.array([x, z])).T)))
        # whatever stood on the path's line goes (earlier vegetation), and nothing new stands on it
        for proto in list(self.inst):
            if proto.startswith(self.TREE_PREFIXES + ('pr_', 'pv_bush', 'pv_hedge', 'bush', 'kf_')):
                self.inst[proto] = [r for r in self.inst[proto] if not (G.point_in_poly(r[0], r[2], poly) and path_d(r[0], r[2]) < 3.2)]
        self.occ.line(path, 3.0, Occupancy.PROP)
        n = 0
        # two benches and a picnic table beside the loop, facing out to sea
        c = np.array(self.plan['islet']['center'], float)
        for k, proto in enumerate(('ph_wooden_bench', 'ph_picnic_table', 'ph_wooden_bench')):
            if proto not in K:
                continue
            q = np.array(path[3 + 7 + k * 11])
            out = (q - c) / max(np.linalg.norm(q - c), 1e-3)
            q = q + out * 2.6
            yaw = math.degrees(math.atan2(out[0], out[1]))
            it = K[proto]
            if self.occ.rect_free(q[0], q[1], it['w'], it['d'], yaw, margin=0.3, allow=(Occupancy.FREE, Occupancy.TREE)):
                self.add(proto, q[0], float(self.g.y([q[0]], [q[1]])[0]) + 0.01, q[1], yaw, 1.0, 1.0)
                self.occ.rect(q[0], q[1], it['w'], it['d'], yaw, Occupancy.PROP, margin=0.3)
        for _ in range(400):
            q = poly.min(0) + RNG.random(2) * (poly.max(0) - poly.min(0))
            y = float(self.g.y([q[0]], [q[1]])[0])
            if y < L.SEA + 0.6 or self.occ.codes([q[0]], [q[1]])[0] != Occupancy.FREE or path_d(*q) < 3.5:
                continue
            if y < L.SEA + 1.6 and RNG.random() < 0.5:
                pid = self.PR_GRANITE[int(RNG.integers(len(self.PR_GRANITE)))]
                s = RNG.uniform(0.9, 2.0)
                self.add(pid, q[0], y - 0.35 * K[pid]['h'] * s, q[1], RNG.uniform(0, 360), s, s * 0.8,
                         self.GRANITE_TINT if pid.startswith('pr_granite') else None)
            elif coast and RNG.random() < 0.35:
                pid = coast[int(RNG.integers(len(coast)))]
                self.add(pid, q[0], y + 0.02, q[1], RNG.uniform(0, 360), RNG.uniform(0.85, 1.2), 1.0, self.foliage_tint(pid, q[0], q[1]))
                self.occ.disc(q[0], q[1], 2.5, Occupancy.TREE)
            else:
                continue
            n += 1
            if n > 70:
                break
        return n

    def v3_basecamp(self):
        """The MtnDAO basecamp lodge (Hackathon House): picnic tables, benches, barrels, crates and a woodpile of barrels and
        planters round its yard, on free ground only."""
        hv = next((v for v in getattr(L, 'VENUES', []) if v.get('id') in ('hackathon_house', 'mtndao_lodge')), None)
        c = np.array([-221.0, 479.0]) if hv is None else np.array(hv.get('root', (-221.0, 479.0))[:2], float)
        props = ['ph_picnic_table', 'ph_picnic_table', 'ph_wooden_bench', 'ph_wooden_bench', 'ph_wooden_barrels', 'ph_wooden_crate',
                 'ph_planter_box', 'ph_compost_bags', 'ph_propane_tank', 'ph_wooden_bucket']
        n = 0
        for i in range(160):
            if n >= 16:
                break
            proto = props[n % len(props)]
            if proto not in self.kit.items:
                n += 1
                continue
            it = self.kit.get(proto)
            a = RNG.uniform(0, 2 * math.pi); rr = RNG.uniform(14, 30)
            q = c + np.array([math.cos(a), math.sin(a)]) * rr
            ry = RNG.uniform(0, 360)
            ys = self.g.y([q[0] - 1, q[0] + 1, q[0]], [q[1], q[1], q[1] + 1])
            if ys.max() - ys.min() > 0.5 or not self.occ.rect_free(q[0], q[1], it['w'], it['d'], ry, margin=0.5):
                continue
            self.add(proto, q[0], float(ys.min()) + 0.01, q[1], ry, 1.0, 1.0)
            self.occ.rect(q[0], q[1], it['w'], it['d'], ry, Occupancy.PROP, margin=0.3)
            n += 1
        return n

    # -------------------------------------------------------------- world polish (2026-10-23)
    def v3_trenches_dense(self):
        """The Trenches read as one shed on an empty lot: fill the free yards with the industrial kit that fits (small
        sheds, tanks, silos, chimneys), edge the lots along the streets with chain-link fence runs and parked vans and
        pickups, and scatter yard clutter (pallets, crates, tyre stacks, drums, dumpsters, jersey barriers, cones) in
        clusters. Roads, venue pads and doorways stay clear (occupancy); everything stays off the carriageway."""
        global RNG
        K = self.kit.items
        if 'dz_industrial_11' not in K:
            return
        RNG = np.random.default_rng(V3_SEED + 7)
        polys = [np.array(self._district_poly('trenches'), float)] + [np.array(q, float) for q in self.V3_SITE_EXTRA.get('trenches', [])]
        inside = lambda x, z: any(G.point_in_poly(x, z, P) for P in polys)
        allp = np.concatenate(polys)
        x0, z0 = allp.min(0); x1, z1 = allp.max(0)
        road_d = ndimage.distance_transform_edt(self.occ.arr() != Occupancy.ROAD)

        def rd(x, z):
            ox = int(np.clip(round(x - Occupancy.X0), 0, Occupancy.W - 1)); oy = int(np.clip(round(Occupancy.H - 1 - (z - Occupancy.Z0)), 0, Occupancy.H - 1))
            return float(road_d[oy, ox])

        def gy(x, z):
            return float(self.g.y([x], [z])[0])

        fails = {}

        def put(pid, x, z, yaw, sc=1.0, margin=0.3, sink=0.0, code=Occupancy.PROP):
            if pid not in K:
                return False
            it = K[pid]
            if not self.occ.rect_free(x, z, it['w'] * sc, it['d'] * sc, yaw, margin=margin, allow=(Occupancy.FREE, Occupancy.TREE)):
                fails['occ'] = fails.get('occ', 0) + 1
                return False
            ys = self.g.y([x - it['w'] / 2, x + it['w'] / 2, x, x], [z, z, z - it['d'] / 2, z + it['d'] / 2])
            if ys.max() - ys.min() > 0.5 + max(it['w'], it['d']) * 0.08:
                fails['slope'] = fails.get('slope', 0) + 1
                return False
            self.add(pid, x, float(ys.min()) + 0.01 - sink, z, yaw, sc, 1.0)
            self.occ.rect(x, z, it['w'] * sc, it['d'] * sc, yaw, code, margin=0.15)
            return True
        nb0 = len(self.boxes)
        made = {}
        # 1. yard structures on the free ground behind the frontage (squared to the street grid of the district)
        pool = [('dz_industrial_11', 4), ('dz_industrial_13', 4), ('dz_industrial_15', 5), ('dz_industrial_29', 4), ('dz_industrial_06', 4),
                ('dz_industrial_28', 6), ('dz_industrial_26', 6), ('dz_industrial_22', 2), ('dz_industrial_24', 2), ('dz_industrial_23', 2),
                ('dz_industrial_14', 1), ('dz_industrial_08', 1)]
        caps = dict(pool)
        pts = [(x, z) for x in np.arange(x0, x1, 6.0) for z in np.arange(z0, z1, 6.0)]
        for i in RNG.permutation(len(pts)):
            x, z = pts[i]
            x += RNG.uniform(-2, 2); z += RNG.uniform(-2, 2)
            if not inside(x, z) or self.occ.codes([x], [z])[0] != Occupancy.FREE or rd(x, z) < 4.5:
                continue
            live = [k for k in caps if k in K and made.get(k, 0) < caps[k]]
            if not live:
                break
            pid = live[int(RNG.integers(len(live)))]
            yaw = 101.0 + float(RNG.integers(4)) * 90.0 + RNG.uniform(-3, 3)
            if self._v3_add_building(pid, x, z, yaw, 1.0, margin=1.0, slope=3.0, allow=(Occupancy.FREE, Occupancy.TREE)):
                made[pid] = made.get(pid, 0) + 1
        new = list(zip(self.boxes[nb0:], self.box_ids[nb0:]))
        for box, pid in new:
            for r in self.inst[pid]:
                if abs(r[0] - box[0]) < 0.03 and abs(r[2] - box[2]) < 0.03:
                    r[6:10] = [*self.V3_TINTS[int(RNG.integers(len(self.V3_TINTS)))], 1.0]
        cut = self._v3_cut_inside(new)
        # 2. lot edges along the district's streets: chain-link runs with gaps, parked vans and pickups, jersey barriers
        fence = K.get('yd_chainlink_fence')
        fences = vans = barriers = 0
        for s in self.roads['segments']:
            if s['kind'] in ('bridge', 'tunnel'):
                continue
            p, y, tan, rn = self.frame(s['samples'])
            if len(p) < 3 or not any(inside(*q) for q in p[::3]):
                continue
            sl = G.arc_lengths(p)
            for side in (1.0, -1.0):
                edge = (s['widthRight'] if side > 0 else s['widthLeft']) + s['sidewalk']
                pos = RNG.uniform(4, 12)
                while pos < sl[-1] - 6:
                    k = min(max(int(np.searchsorted(sl, pos)), 1), len(p) - 1)
                    P, t, n_ = p[k], tan[k], rn[k] * side
                    if not inside(*P):
                        pos += 8.0
                        continue
                    yaw_t = math.degrees(math.atan2(t[0], t[1]))
                    r = RNG.random()
                    if fence is not None and r < 0.5:
                        # a fence run 3-8 panels long, 3.2 m back from the kerb line
                        L_ = fence['w']
                        runs = int(RNG.integers(3, 9))
                        ok = 0
                        for back in (2.2, 3.4, 5.0, 7.0, 9.5):
                            for j in range(runs):
                                kk = min(max(int(np.searchsorted(sl, pos + j * L_)), 1), len(p) - 1)
                                q = p[kk] + rn[kk] * side * (edge + back)
                                ya = math.degrees(math.atan2(tan[kk][0], tan[kk][1])) + 90.0
                                if not inside(*q) or not put('yd_chainlink_fence', q[0], q[1], ya, margin=0.1, sink=0.02):
                                    break
                                ok += 1
                            if ok:
                                break
                        fences += ok
                        pos += max(ok, 1) * L_ + RNG.uniform(6, 16)
                    elif r < 0.78:
                        # a van or a pickup parked nose along the street on the lot's apron
                        pid = 'car_van' if RNG.random() < 0.5 else 'car_truck'
                        flip = 180.0 if RNG.random() < 0.5 else 0.0
                        for back in (2.2, 3.2, 4.5, 6.0, 8.0):
                            q = P + n_ * (edge + back)
                            if inside(*q) and put(pid, q[0], q[1], yaw_t + flip + RNG.uniform(-4, 4), margin=0.3):
                                vans += 1
                                break
                        pos += RNG.uniform(9, 18)
                    else:
                        # a short line of jersey barriers closing a yard entrance
                        cnt = 0
                        for j in range(int(RNG.integers(2, 5))):
                            q = P + n_ * (edge + 2.0) + t * j * 1.62
                            if inside(*q) and put('v3_jersey_barrier', q[0], q[1], yaw_t + 90.0 + RNG.uniform(-3, 3), margin=0.1):
                                cnt += 1
                        barriers += cnt
                        pos += RNG.uniform(8, 14)
        # vans and pickups parked in front of the sheds (beside the door, nose out)
        for box, pid in zip(self.boxes, self.box_ids):
            if not pid.startswith('dz_') or not inside(box[0], box[2]) or box[3] < 5.0 or RNG.random() > 0.6:
                continue
            cx, cy, cz, w, h, d, yaw = box
            a = math.radians(yaw)
            fwd = np.array([math.sin(a), math.cos(a)]); right = np.array([math.cos(a), -math.sin(a)])
            vid = 'car_van' if RNG.random() < 0.5 else 'car_truck'
            for side in (RNG.choice([-1.0, 1.0]),) * 1 + (0.0,):
                q = np.array([cx, cz]) + fwd * (d / 2 + 3.4) + right * side * (w / 2 - 1.8)
                if inside(*q) and rd(*q) > 1.0 and put(vid, q[0], q[1], yaw + 90.0 * RNG.choice([1.0, -1.0]) + RNG.uniform(-8, 8), margin=0.3):
                    vans += 1
                    break
        # chain-link compounds round the tanks, silos, chimneys and the substation (a gate gap on the street side)
        compounds = 0
        if fence is not None:
            L_ = fence['w']
            for box, pid in zip(self.boxes, self.box_ids):
                if pid not in ('dz_industrial_28', 'dz_industrial_23', 'dz_industrial_26', 'dz_industrial_22', 'dz_industrial_24',
                               'dz_industrial_06', 'dz_industrial_14') or not inside(box[0], box[2]):
                    continue
                cx, cy, cz, w, h, d, yaw = box
                a = math.radians(yaw)
                fwd = np.array([math.sin(a), math.cos(a)]); right = np.array([math.cos(a), -math.sin(a)])
                hw, hd = w / 2 + 2.2, d / 2 + 2.2
                c0 = np.array([cx, cz])
                # the side nearest a road gets the gate
                sides = [(fwd, right, hd, hw), (-fwd, right, hd, hw), (right, fwd, hw, hd), (-right, fwd, hw, hd)]
                gate = min(range(4), key=lambda i: rd(*(c0 + sides[i][0] * sides[i][2])))
                for i, (nrm, along, off, half) in enumerate(sides):
                    n_p = max(1, int(round(2 * half / L_)))
                    for j in range(n_p):
                        t = -half + (j + 0.5) * (2 * half / n_p)
                        if i == gate and abs(t) < 2.4:
                            continue
                        q = c0 + nrm * off + along * t
                        ya = math.degrees(math.atan2(along[0], along[1])) + 90.0
                        if put('yd_chainlink_fence', q[0], q[1], ya, sc=1.0, margin=0.05, sink=0.02):
                            compounds += 1
        fences += compounds
        # 3. yard clutter in clusters round the sheds and in the open yards
        kinds = [('tj_pallet_a', 3), ('tj_pallet_b', 2), ('v3_crate', 3), ('v3_tyre_stack', 2), ('ph_barrel_03', 3), ('tj_tarp_crates', 1),
                 ('tj_dumpster', 1), ('tj_utility_box', 1), ('nc_prop_cone_01', 1), ('nc_prop_barricade_a_01', 1), ('v3_jersey_barrier', 1)]
        bag = [k for k, w in kinds for _ in range(w) if k in K]
        clusters = props = 0
        pts = [(x, z) for x in np.arange(x0, x1, 6.5) for z in np.arange(z0, z1, 6.5)]
        for i in RNG.permutation(len(pts)):
            if clusters >= 260 or not bag:
                break
            x, z = pts[i]
            x += RNG.uniform(-3, 3); z += RNG.uniform(-3, 3)
            if not inside(x, z) or self.occ.codes([x], [z])[0] != Occupancy.FREE or not (2.0 < rd(x, z) < 40):
                continue
            base_yaw = 101.0 + float(RNG.integers(4)) * 90.0
            n_here = 0
            for _ in range(int(RNG.integers(3, 8))):
                pid = bag[int(RNG.integers(len(bag)))]
                q = (x + RNG.uniform(-3.5, 3.5), z + RNG.uniform(-3.5, 3.5))
                if rd(*q) < 2.0:
                    continue
                if put(pid, q[0], q[1], base_yaw + RNG.choice([0.0, 90.0]) + RNG.uniform(-12, 12), margin=0.15):
                    n_here += 1
            if n_here:
                clusters += 1; props += n_here
        self.stats['v3_trenches_dense'] = dict(yard_buildings=made, plants_cut=cut, fence_panels=fences, vans=vans, barriers=barriers,
                                               clutter_clusters=clusters, clutter_props=props)
        log(f'v3 trenches dense: yard {made}; {fences} fence panels, {vans} vans/pickups, {barriers} barriers, {clusters} clutter clusters '
            f'({props} props); {cut} plants cut; refused {fails}')

    # toy ground cover / bushes -> the semi-real v3_plants set (ground cover: (choices, scale range, sy range))
    V3_COVER = {
        'w_grass_wispy_short': (['pv_grass_short'], (1.1, 1.6)), 'w_grass_common_short': (['pv_grass_short'], (1.1, 1.6)),
        'w_grass_wispy_tall': (['pv_grass_tall', 'pv_grass_short'], (1.1, 1.5)), 'kf_grass': (['pv_grass_tall', 'pv_grass_short'], (1.1, 1.5)),
        'w_clover_1': (['pv_grass_short'], (1.0, 1.3)), 'w_clover_2': (['pv_grass_short', 'pv_flowers_violet'], (1.0, 1.3)),
        'w_flower': (['pv_flowers_yellow', 'pv_flowers_dandelion', 'pv_flowers_violet', 'pv_flowers_orange'], (1.1, 1.45)),
        'flowers_': (['pv_flowers_yellow', 'pv_flowers_orange', 'pv_flowers_violet'], (1.1, 1.4)),
        'w_pebble': ([], (1.0, 1.0)),                               # world districts 2: the toy pebbles go
        'w_fern': (['pv_fern'], (0.9, 1.25)), 'w_plant_7': (['pv_weeds'], (1.0, 1.35)),
        'w_mushroom_common': ([], (1.0, 1.0)),
    }
    V3_BUSH = {'bush_round': ['pv_bush_round', 'pv_bush_low'], 'bush_small': ['pv_bush_low'], 'bush_flowers': ['pv_bush_round', 'pv_bush_large'],
               'bush_large_flowers': ['pv_bush_large', 'pv_hedge'], 'kf_bush': ['pv_bush_round', 'pv_bush_low', 'pv_bush_large']}

    ROUTE_SPOTS = [(-332.5, -279.0), (-493.6, 50.7), (690.0, -335.0),
                   # world districts 2 route (InteriorRoute.Spots2)
                   (422.7, 302.2), (560.0, 92.0), (-60.0, -230.0), (-276.0, 278.0), (-222.0, 420.0), (802.0, -594.0),
                   # world fix-ups route (InteriorRoute.SpotsFx)
                   (-374.6, -248.8), (102.0, -228.0), (-200.0, 300.0), (300.0, 52.0)]

    def v3_ground_cover(self):
        """Island-wide: the toy grass clumps, clover, cartoon flowers, ferns and blob bushes become the semi-real cards and
        leaf-card shrubs (art/pipeline/world_v3_plants.py); ground cover thins out towards roads (none within 1.5 m of a
        carriageway, sparse to 4 m) and leaves paved ground alone; flowering bushes get a flower patch at their foot;
        the parks get loose wildflower drifts."""
        global RNG
        K = self.kit.items
        if 'pv_grass_short' not in K:
            log('ground cover: no v3_plants kit, skipped')
            return
        RNG = np.random.default_rng(V3_SEED + 11)
        road_d = ndimage.distance_transform_edt(self.occ.arr() != Occupancy.ROAD)
        pave = None
        if getattr(self, 'paving', None):
            pave = np.asarray(Image.open(self.V3_PAVING_PNG).convert('RGB'), np.float32)[..., 0] / 255.0
            pmin, psz = self.paving['min'], self.paving['size']

        def paved(x, z):
            if pave is None:
                return 0.0
            ix, iy = int(x - pmin[0]), int(psz[1] - 1 - (z - pmin[1]))
            return float(pave[iy, ix]) if 0 <= ix < psz[0] and 0 <= iy < psz[1] else 0.0

        def rd(x, z):
            ox = int(np.clip(round(x - Occupancy.X0), 0, Occupancy.W - 1)); oy = int(np.clip(round(Occupancy.H - 1 - (z - Occupancy.Z0)), 0, Occupancy.H - 1))
            return float(road_d[oy, ox])
        counts = {'cover_in': 0, 'cover_out': 0, 'thinned_road': 0, 'thinned_paved': 0, 'bushes': 0, 'bush_flowers': 0, 'park_drifts': 0}
        out = []
        for proto in list(self.inst):
            cover = next((v for k, v in self.V3_COVER.items() if proto.startswith(k)), None)
            bush = next((v for k, v in self.V3_BUSH.items() if proto.startswith(k)), None)
            if cover is None and bush is None:
                continue
            rows = self.inst.pop(proto)
            for r in rows:
                x, y, z = r[0], r[1], r[2]
                d, pv = rd(x, z), paved(x, z)
                if cover is not None:
                    counts['cover_in'] += 1
                    if not cover[0]:
                        continue
                    if pv > 0.25:
                        counts['thinned_paved'] += 1
                        continue
                    if d < 1.5 or (d < 4.0 and RNG.random() > (d - 1.5) / 2.5):
                        counts['thinned_road'] += 1
                        continue
                    pid = cover[0][int(RNG.integers(len(cover[0])))]
                    out.append((pid, x, y - 0.03, z, RNG.uniform(0, 360), RNG.uniform(*cover[1]), RNG.uniform(0.85, 1.2)))
                    counts['cover_out'] += 1
                else:
                    if pv > 0.5 or d < 1.2:
                        continue
                    # the device route's eye-level spots (client InteriorRoute.Spots): no shrub between the lens and the player
                    if any(math.hypot(x - sx, z - sz) < 7.0 for sx, sz in self.ROUTE_SPOTS):
                        continue
                    pid = bush[int(RNG.integers(len(bush)))]
                    out.append((pid, x, y - 0.02, z, RNG.uniform(0, 360), RNG.uniform(0.75, 1.1), RNG.uniform(0.9, 1.15)))
                    counts['bushes'] += 1
                    if 'flower' in proto:
                        for _ in range(int(RNG.integers(1, 3))):
                            a = RNG.uniform(0, 2 * math.pi); rr = RNG.uniform(1.0, 1.8)
                            q = (x + math.cos(a) * rr, z + math.sin(a) * rr)
                            if rd(*q) > 1.5 and paved(*q) < 0.25:
                                out.append((['pv_flowers_yellow', 'pv_flowers_orange', 'pv_flowers_violet'][int(RNG.integers(3))], q[0],
                                            float(self.g.y([q[0]], [q[1]])[0]) - 0.03, q[1], RNG.uniform(0, 360), RNG.uniform(0.8, 1.05), 1.0))
                                counts['bush_flowers'] += 1
        # the parks' lawns: grass tufts in loose clumps (denser where a low-frequency noise says so, none on the paths),
        # and wildflower drifts (clusters of a few cards of one kind)
        for name, poly in L.PARKS:
            P = np.array(poly, float)
            area = abs(float(np.sum(P[:, 0] * np.roll(P[:, 1], -1) - np.roll(P[:, 0], -1) * P[:, 1]))) / 2
            for _ in range(int(area / 9)):
                q = P.min(0) + RNG.random(2) * (P.max(0) - P.min(0))
                if not G.point_in_poly(q[0], q[1], P) or self.occ.codes([q[0]], [q[1]])[0] not in (Occupancy.PARK, Occupancy.FREE) or rd(*q) < 2.0:
                    continue
                if G.fbm(np.array([q[0] / 23.0 + 3.1]), np.array([q[1] / 23.0]))[0] < 0.45 + RNG.random() * 0.25:
                    continue
                kind = 'pv_grass_tall' if RNG.random() < 0.3 else 'pv_grass_short'
                out.append((kind, q[0], float(self.g.y([q[0]], [q[1]])[0]) - 0.03, q[1], RNG.uniform(0, 360), RNG.uniform(1.0, 1.5), RNG.uniform(0.85, 1.25)))
                counts['park_drifts'] += 1
            for _ in range(int(min(90, area / 400))):
                c = P.min(0) + RNG.random(2) * (P.max(0) - P.min(0))
                if not G.point_in_poly(c[0], c[1], P):
                    continue
                kind = ['pv_flowers_yellow', 'pv_flowers_dandelion', 'pv_flowers_violet', 'pv_flowers_orange', 'pv_grass_tall'][int(RNG.integers(5))]
                for _ in range(int(RNG.integers(3, 7))):
                    q = c + RNG.normal(0, 1.4, 2)
                    if self.occ.codes([q[0]], [q[1]])[0] not in (Occupancy.PARK, Occupancy.FREE) or rd(*q) < 2.5:
                        continue
                    out.append((kind, q[0], float(self.g.y([q[0]], [q[1]])[0]) - 0.03, q[1], RNG.uniform(0, 360), RNG.uniform(1.1, 1.45), RNG.uniform(0.9, 1.15)))
                    counts['park_drifts'] += 1
        for pid, x, y, z, yaw, sc, sy in out:
            self.add(pid, x, y, z, yaw, sc, sy)
        self.stats['v3_ground_cover'] = counts
        log(f'v3 ground cover: {counts}')

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

    # world districts 2 (2026-10-23): the forest on semi-real trees (art/pipeline/world_v3_trees.py: Poly Haven scans as
    # billboard-cloud trees) and the LOLIPOP maples; the toy pools above stay as the fallback when the v3 kit is absent
    PT_PINES = ['pt_fir_a', 'pt_fir_a', 'pt_fir_b', 'pt_fir_b', 'pt_fir_c', 'pt_pine_a', 'pt_pine_a']
    PT_BROAD = ['lp_maple_medium_1', 'lp_maple_medium_2', 'lp_maple_medium_3', 'lp_maple_large_1', 'lp_maple_large_2',
                'lp_maple_large_3', 'pt_broad_a', 'pt_broad_a', 'pt_island_b']
    PT_AUTUMN = ['pt_island_b', 'pt_island_a']
    PT_BLOSSOM = ['pt_jacaranda']
    TREE_PREFIXES = ('w_tree', 'w_pine', 'palm', 'tree_', 'kf_tree', 'lp_maple', 'pt_')

    def semi(self, pt_pool, toy_pool):
        live = [q for q in pt_pool if q in self.kit.items]
        return live if live else toy_pool

    def pick(self, pool):
        live = [q for q in pool if q in self.kit.items]
        return live[RNG.integers(len(live))] if live else None

    def foliage_tint(self, proto, x, z):
        """Per-tree colour drift (the kit shader multiplies foliage by it): mostly deeper greens, some olive and blue-green,
        a few autumn crowns in the groves; large-scale noise keeps neighbours related, so stands read as one wood."""
        if proto.startswith('pt_'):
            # semi-real trees: a narrow drift round neutral (their atlas carries the scan's own colour), x1.25 in the shader
            n = G.fbm(np.array([x / 90.0]), np.array([z / 90.0]))[0]
            base = np.array([0.80, 0.80, 0.78]) + (n - 0.5) * np.array([0.10, 0.06, 0.08]) + RNG.uniform(-0.025, 0.025, 3)
            return tuple(np.clip(base, 0, 1)) + (1.0,)
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
        pines, broad = self.semi(self.PT_PINES, self.PINES), self.semi(self.PT_BROAD, self.BROAD)
        autumn, blossom_pool = self.semi(self.PT_AUTUMN, self.AUTUMN), self.semi(self.PT_BLOSSOM, None)
        semi = pines is not self.PINES

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
                return self.pick(autumn), RNG.uniform(0.8, 1.0), 1.0
            return self.pick(broad), RNG.uniform(0.75, 1.1), 1.0
        n_forest = self.scatter(6.0, region, forest_p, forest_pick)

        # meadow trees: lone trees and small groves in open country (not towns, not forest)
        def meadow_p(X, Z):
            f = g.sample(g.forest, X, Z)
            y = g.y(X, Z)
            return np.where((f < 0.1) & (y > L.SEA + 3) & (y < 90), 0.05 + 0.1 * (G.fbm(X / 60.0, Z / 60.0) > 0.58), 0.0)

        def meadow_pick(x, z, y):
            if RNG.random() < 0.05:
                return self.pick(autumn), RNG.uniform(0.85, 1.05), 1.0
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
            blossom = blossom_pool or ['tree_blossom_1', 'tree_blossom_2', 'tree_blossom_3', 'tree_blossom_4']

            def park_p(X, Z, P=P):
                inside = np.array([G.point_in_poly(x, z, P) for x, z in zip(X, Z)])
                return np.where(inside, 0.55 * (G.fbm(X / 30.0 + 5, Z / 30.0) > 0.46), 0.0)

            def park_pick(x, z, y):
                r = RNG.random()
                if r < 0.4:
                    if semi:
                        return self.pick(blossom), RNG.uniform(0.8, 1.0), 1.0         # jacaranda, 10-12.5 m
                    return self.pick(blossom), RNG.uniform(1.35, 1.6), 1.0          # cherry blossom, 8-10 m
                if r < 0.5:
                    return self.pick(autumn), RNG.uniform(0.8, 0.95), 1.0
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

    # -------------------------------------------------------------- W4.1 shot dressing
    def place_shot_dressing(self):
        """The lodge yard (shot 6): two parked cars, bicycles, a summit-trail signpost and three MtnDAO flags. The convention
        centre (shot 8): a 'CHECKPOINT' entry arch (our own words, navy and coral per the community-eggs rules), banner
        poles along the approach, flags on the frontage and queue barriers by the door. Unity builds the arch, banners,
        flags, signposts and barriers from dress.json (signposts, flagPoles, eventDress)."""
        K = self.kit.items
        venues = {v['id']: v for v in self.plan['venues']}

        def frame(vid):
            v = venues[vid]
            o = np.array([v['origin'][0], v['origin'][2]]); a = math.radians(v['yaw'])
            out = np.array([-math.sin(a), -math.cos(a)])           # the door wall faces the venue's local -z
            right = np.array([math.cos(a), -math.sin(a)])
            half = v['frame_size'][1] / 2.0
            return o + out * half, out, right, v['origin'][1]

        def gy(q):
            return float(self.g.y([q[0]], [q[1]])[0])

        def face_of(d):
            return math.degrees(math.atan2(d[0], d[1])) % 360.0

        # lodge yard
        if 'hackathon_house' in venues:
            door, out, right, y0 = frame('hackathon_house')
            for k, proto in enumerate(('car_suv', 'car_sedan')):
                q = door + out * 6.5 - right * (8.0 + k * 3.0)
                if proto in K:
                    self.add(proto, q[0], gy(q), q[1], face_of(out) + 180.0, 1.0, 1.0, None)
            for k in range(3):
                q = door + out * 2.2 + right * (5.0 + k * 0.9)
                if 'pp_bicycle' in K:
                    self.add('pp_bicycle', q[0], gy(q) + 0.02, q[1], face_of(out) + 90.0 + RNG.uniform(-6, 6))
            q = door + out * 7.0 + right * 9.5
            self.signposts.append([round(q[0], 2), round(gy(q), 2), round(q[1], 2), round(face_of(out), 1), 'trail'])
            for k, col in enumerate(((0.86, 0.32, 0.20), (0.96, 0.96, 0.94), (0.20, 0.30, 0.62))):
                q = door + out * (3.5 + k * 1.6) + right * 11.5
                self.flag_poles.append([round(q[0], 2), round(gy(q), 2), round(q[1], 2), round(face_of(right), 1), *col])
        # convention centre
        if 'conference_hall' in venues:
            door, out, right, y0 = frame('conference_hall')
            clear = 3.0                                          # free ground between the door and the road
            for t in np.arange(1.0, 20.0, 0.5):
                q = door + out * t
                if self.occ.codes([q[0]], [q[1]])[0] == Occupancy.ROAD:
                    break
                clear = t
            arch_t = max(2.5, min(clear - 1.2, 7.0))
            ev = dict(arch=None, banners=[], queues=[])
            q = door + out * arch_t
            ev['arch'] = [round(q[0], 2), round(y0, 2), round(q[1], 2), round(face_of(out), 1), 7.5]
            for t in np.arange(1.5, max(2.0, clear - 0.6), 3.0):
                for sd in (-1.0, 1.0):
                    q = door + out * t + right * sd * 5.2
                    ev['banners'].append([round(q[0], 2), round(y0, 2), round(q[1], 2), round(face_of(out), 1), int(RNG.integers(3))])
            for k in range(3):                                   # a three-lane queue to the right of the door
                q = door + out * (1.4 + k * 1.2) + right * 4.0
                ev['queues'].append([round(q[0], 2), round(y0, 2), round(q[1], 2), round(face_of(right), 1), 4.2])
            for k, col in enumerate(((0.10, 0.14, 0.32), (0.98, 0.50, 0.40), (0.97, 0.97, 0.98), (0.98, 0.50, 0.40), (0.10, 0.14, 0.32))):
                q = door + out * 0.9 + right * (-11.0 + k * 5.5)
                self.flag_poles.append([round(q[0], 2), round(y0, 2), round(q[1], 2), round(face_of(out), 1), *col])
            self.event_dress = ev
            q = door + out * (arch_t + 1.0) - right * 7.5
            self.signposts.append([round(q[0], 2), round(y0, 2), round(q[1], 2), round(face_of(out), 1), 'checkpoint'])
        log(f'shot dressing: {len(self.signposts)} signposts, {len(self.flag_poles)} flags, event {"yes" if self.event_dress else "no"}')

    def clear_shot_frames(self):
        """W4.1: no tree trunk or palm fills the edge of an authored shot. Trees, palms and bushes within 12 m of a shot
        camera and inside its view (plus a 3 m margin) are removed (same pose maths as IslandWaypoints.CameraPose)."""
        import waypoints as W
        roads = self.roads
        cut = 0
        for shot, wid, title, (x, z), (sx, sy, sz), mode, cam in W.SHOTS:
            if mode == 'sidewalk':
                x, _, z = W.snap(roads, x, z)
            f = np.array([sx - x, sz - z]); f /= max(np.linalg.norm(f), 1e-6)
            r = np.array([f[1], -f[0]])
            c = np.array([x, z]) - f * cam['dist'] + r * cam['side']
            half = math.tan(math.radians(cam['fov'] * 2.225 / 2.0))        # horizontal half-FOV at the Seeker's aspect (approx.)
            for proto, rows in self.inst.items():
                if not proto.startswith(self.TREE_PREFIXES + ('bush_', 'kf_bush')):
                    continue
                keep = []
                for row in rows:
                    v = np.array([row[0], row[2]]) - c
                    along, lat = float(v @ f), float(v @ r)
                    if -1.5 < along < 12.0 and abs(lat) < max(along, 0.0) * min(half, 1.6) + 3.0:
                        cut += 1
                        continue
                    keep.append(row)
                self.inst[proto] = keep
        self.stats['shot_frame_trees_removed'] = cut
        log(f'shot frames: {cut} trees cleared')

    # -------------------------------------------------------------- output
    def write(self, path):
        protos = sorted(self.inst)
        doc = dict(version=1, generated=time.strftime('%Y-%m-%d'), stats=self.stats,
                   record='x, y, z, yaw (deg), scale xz, scale y, tint r, g, b, a',
                   instances=[dict(proto=p, n=len(self.inst[p]), d=[c for r in self.inst[p] for c in r]) for p in protos],
                   buildingBoxes=self.boxes, signals=self.signals, power=self.power, billboards=self.billboards, heroes=self.heroes,
                   shopSigns=self.shop_signs, treePits=self.tree_pits, busShelters=self.bus_shelters, wallDecor=self.wall_decor,
                   heroPlazas=self.hero_plazas, signposts=self.signposts, flagPoles=self.flag_poles, eventDress=self.event_dress,
                   paving=getattr(self, 'paving', None), interiorDoors=getattr(self, 'interior_doors', []))
        from placement_repairs import apply_repairs
        repairs = apply_repairs(doc)
        if repairs:
            log(f'replayed {repairs} reviewed placement corrections')
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
    d.place_shot_dressing()
    d.clear_shot_frames()
    d.v3_downtown()
    d.v3_districts()
    d.v3_trenches_dense()
    d.stats['v3_trenches_junction'] = d.v3_trenches_junction()
    d.stats['v3_trenches_lot'] = d.v3_trenches_lot()
    d.v3_districts2()
    d.v3_paving_all()
    d.v3_ground_cover()
    d.write_heights()
    d.write(os.path.join(OUT, 'dress.json'))
    log(f'done ({time.time() - t0:.1f}s)')


if __name__ == '__main__':
    main()
