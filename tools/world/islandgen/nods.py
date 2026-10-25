"""Community nods on CT Island (R4, backend #13): places the 80 painted props of contract/ids/nods.json for real.

Server rule behind it: POST /nods/{id}/collect checks the player's STREET position (ctl:pos, written by the street and
vehicles rooms only) against the registry x/z within radiusM, so every nod stands OUTDOORS, on land, inside its
district's polygon (contract/ids/districts.json), where a player walks: at the outer edge of a sidewalk (beside a shop
front, a stall, a doorway), on the open ground of a plaza, park or the boardwalk near its edge, or in a nook round a
venue, landmark building or hero statue. Never on a road, inside a building or walk-in room, in the water, against a
tree trunk or a fence.

Each nod keeps its district; its community-eggs zone (docs/research/community-eggs-2026-10.json: cafe, harbour, park,
market street, tech hub ...) pulls it toward the matching place, and a hero statue of its own (the Black Bull for
ANSEM, the chef bull for Marinade, the ghost, the penguin cart) pulls it to the statue. Within a district the nods keep
at least 20 m apart (16 m where a district is tight) and spread toward 35 m. Deterministic: no randomness.

Usage: python3 tools/world/islandgen/nods.py            (after islandgen.py, dress.py and peds.py)
Writes: contract/ids/nods.json (x, z; every other field kept), client/Assets/Resources/World/Island/nods.json
        ({nodId, setId, x, y, z, yaw}: y the ground, yaw facing the nearest walkway), tools/world/islandgen/out/nods_report.json
"""
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import numpy as np

import geom as G

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(HERE, 'out')
REGISTRY = os.path.join(REPO, 'contract', 'ids', 'nods.json')
DISTRICTS = os.path.join(REPO, 'contract', 'ids', 'districts.json')
EGGS = os.path.join(REPO, 'docs', 'research', 'community-eggs-2026-10.json')
PEDS = os.path.join(REPO, 'client', 'Assets', 'Resources', 'World', 'Island', 'pedestrian_paths.json')
CLIENT = os.path.join(REPO, 'client', 'Assets', 'Resources', 'World', 'Island', 'nods.json')

MIN_GAP, TIGHT_GAP, SPREAD = 20.0, 16.0, 35.0
HEROES = {'ansem': 'black_bull', 'marinade': 'chef_bull', 'phantom': 'purple_ghost', 'pengu': 'penguin_cart'}
# zone -> (venue / landmark ids it gathers round, candidate kinds it likes)
ZONES = {
    'cafe': (['terminal_cafe'], ()),
    'harbour': (['prediction_pit', 'rug_room'], ('boardwalk',)),
    'park': ([], ('park',)),
    'market street': ([], ('sidewalk',)),
    'sky / market street': ([], ('sidewalk', 'plaza')),
    'sky': ([], ('plaza',)),
    'tech hub': (['audit_row', 'vc_row'], ()),
    'stage/Spaces': (['conference_hall', 'ratio_arena'], ('park',)),
    'trenches': (['perp_pit', 'launchpad_tower', 'rekt_clinic', 'helius_depot', 'rekt_burger'], ()),
    'Lagos corner': ([], ('sidewalk',)),
    'mine': (['sybil_barn', 'airdrop_fields', 'checker_kiosk'], ()),
}
# no collider to keep clear of: ground cover, crops, flat street furniture
SOFT = ('pv_grass', 'pv_flowers', 'pv_weeds', 'pv_fern', 'pv_crop', 'pr_moss', 'kn_crop', 'v3_manhole', 'v3_storm_drain')
CLEAR = {'pt_': 1.4, 'lp_': 1.4, 'nc_tree': 1.4, 'palm': 1.2, 'yd_chainlink_fence': 2.0, 'pv_hedge': 1.4, 'pv_bush': 1.1}


def log(*a):
    print('[nods]', *a, flush=True)


class Island:
    def __init__(self):
        self.plan = json.load(open(os.path.join(OUT, 'plan.json')))
        self.dress = json.load(open(os.path.join(OUT, 'dress.json')))
        self.roads = json.load(open(os.path.join(OUT, 'roads.json')))
        self.peds = json.load(open(PEDS))
        dist = json.load(open(DISTRICTS))
        self.polys = {k: np.array(v, float) for k, v in dist['districts'].items()}
        self.land = [np.array(p, float) for p in dist['island']]
        t = json.load(open(os.path.join(OUT, 'terrain.json')))
        self.t = t
        hf = os.path.join(OUT, 'height_dressed.r16')
        if not os.path.exists(hf):
            hf = os.path.join(OUT, 'height.r16')
        q = np.fromfile(hf, dtype='<u2').reshape(t['samples'], t['samples']) / 65535.0
        self.h = t['hmin'] + q * (t['hmax'] - t['hmin'])
        self._footprints()
        self._solids()
        self._walkways()
        self._roads()

    # ------------------------------------------------------------------ ground
    def y(self, x, z):
        fx = (x - self.t['min'][0]) / self.t['cell']; fz = (z - self.t['min'][1]) / self.t['cell']
        i, j = int(math.floor(fx)), int(math.floor(fz)); a, b = fx - i, fz - j
        h = self.h
        return float(h[j, i] * (1 - a) * (1 - b) + h[j, i + 1] * a * (1 - b) + h[j + 1, i] * (1 - a) * b + h[j + 1, i + 1] * a * b)

    def district_of(self, x, z):
        for k, p in self.polys.items():
            if G.point_in_poly(x, z, p):
                return k
        return None

    def on_land(self, x, z):
        if not any(G.point_in_poly(x, z, p) for p in self.land):
            return False
        sea = self.t['sea']
        return all(self.y(x + dx, z + dz) > sea + 0.6 for dx, dz in ((0, 0), (1.5, 0), (-1.5, 0), (0, 1.5), (0, -1.5)))

    def flat(self, x, z):
        hs = [self.y(x + dx, z + dz) for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1), (0, 0))]
        return max(hs) - min(hs) < 0.45

    # ------------------------------------------------------------------ what is solid
    def _footprints(self):
        """Buildings as oriented rectangles (cx, cz, w, d, yaw, margin): dress boxes, venue frames, landmark buildings."""
        fp = []
        for b in self.dress['buildingBoxes']:
            fp.append((b[0], b[2], b[3], b[5], b[6], 0.9))
        self.venues = {}
        for v in self.plan['venues'] + self.plan.get('homes', []):
            mn, sz = v['frame_min'], v['frame_size']
            cx, cz = G.local_to_world(v['origin'][0], v['origin'][2], v['yaw'], mn[0] + sz[0] / 2, mn[1] + sz[1] / 2)
            r = (cx, cz, sz[0] + 0.6, sz[1] + 0.6, v['yaw'], 1.2)
            fp.append(r)
            self.venues[v['id']] = r
        for lb in self.plan.get('landmarkBuildings', []):
            r = (lb['at'][0], lb['at'][1], lb['size'][0], lb['size'][1], lb['yaw'], 1.2)
            fp.append(r)
            self.venues[lb['id']] = r
        self.footprints = fp
        self.heroes = {h['id']: (h['p'][0], h['p'][2], max((h.get('plinth') or [2.4, 2.4])[:2]) / 2 + 0.9) for h in self.dress['heroes']}
        # a coarse grid of footprints for quick lookups
        self.fgrid = {}
        for i, (cx, cz, w, d, yaw, m) in enumerate(fp):
            r = math.hypot(w, d) / 2 + m
            for gx in range(int((cx - r) // 16), int((cx + r) // 16) + 1):
                for gz in range(int((cz - r) // 16), int((cz + r) // 16) + 1):
                    self.fgrid.setdefault((gx, gz), []).append(i)

    def in_building(self, x, z):
        for i in self.fgrid.get((int(x // 16), int(z // 16)), []):
            cx, cz, w, d, yaw, m = self.footprints[i]
            a = math.radians(yaw)
            dx, dz = x - cx, z - cz
            u = dx * math.cos(a) - dz * math.sin(a)
            v = dx * math.sin(a) + dz * math.cos(a)
            if abs(u) <= w / 2 + m and abs(v) <= d / 2 + m:
                return True
        for hx, hz, r in self.heroes.values():
            if math.hypot(x - hx, z - hz) < r:
                return True
        return False

    def _solids(self):
        pts, rad = [], []
        for inst in self.dress['instances']:
            p = inst['proto']
            if p.startswith(SOFT) or inst['n'] == 0:
                continue
            r = next((v for k, v in CLEAR.items() if p.startswith(k)), 0.9)
            d = inst['d']
            for k in range(0, len(d), 10):
                pts.append((d[k], d[k + 2])); rad.append(r)
        self.solid = np.array(pts, float)
        self.solid_r = np.array(rad, float)
        self.sgrid = {}
        for i, (x, z) in enumerate(pts):
            self.sgrid.setdefault((int(x // 8), int(z // 8)), []).append(i)

    def blocked(self, x, z):
        gx, gz = int(x // 8), int(z // 8)
        for ix in (gx - 1, gx, gx + 1):
            for iz in (gz - 1, gz, gz + 1):
                for i in self.sgrid.get((ix, iz), []):
                    if math.hypot(self.solid[i, 0] - x, self.solid[i, 1] - z) < self.solid_r[i]:
                        return True
        return False

    # ------------------------------------------------------------------ where people walk, where cars drive
    def _walkways(self):
        pts = []
        for s in self.peds['sidewalks'] + self.peds['corners']:
            pts.extend((p[0], p[2]) for p in s['points'])
        self.walk = np.array(pts, float)
        self.areas = [(a['id'], a['kind'], np.array(a['poly'], float)) for a in self.peds['areas']]
        # the boardwalk is a raised deck railed on both sides, reached by its steps: nods stand on its middle line
        self.deck = []
        for bw in self.plan.get('boardwalk', []):
            A, B, Y = np.array(bw['inner'], float), np.array(bw['outer'], float), np.array(bw['y'], float)
            M = (A + B) / 2
            steps = bw.get('steps') or [0]
            for k in range(1, len(M) - 1):
                t = M[k + 1] - M[k - 1]; t /= max(np.linalg.norm(t), 1e-6)
                s_ = min(steps, key=lambda q: abs(q - k))
                towards = t if s_ > k else -t                      # face along the deck toward the nearest steps
                self.deck.append((float(M[k, 0]), float(M[k, 1]), float(Y[k]), math.degrees(math.atan2(towards[0], towards[1])) % 360,
                                  float(np.linalg.norm(A[k] - B[k]))))
        self.deck_at = {(round(x, 2), round(z, 2)): (y, yaw) for x, z, y, yaw, _ in self.deck}

    def _roads(self):
        segs = []
        for s in self.roads['segments']:
            S = np.array([[p[0], p[2]] for p in s['samples']], float)
            half = max(float(s['widthLeft']), float(s['widthRight'])) + float(s.get('shoulder') or 0) + float(s.get('parking') or 0)
            segs.append((s['id'], S, half))
        self.segs = segs
        self.seg_by_id = {sid: (S, half) for sid, S, half in segs}

    def on_road(self, x, z, pad=0.4):
        q = np.array([x, z])
        for sid, S, half in self.segs:
            if abs(S[:, 0].mean() - x) > 400 or abs(S[:, 1].mean() - z) > 400:
                continue
            if np.min(np.linalg.norm(S - q, axis=1)) < half + pad:
                return True
        return False

    def nearest_walk(self, x, z):
        d = np.linalg.norm(self.walk - np.array([x, z]), axis=1)
        i = int(np.argmin(d))
        return self.walk[i], float(d[i])


# ---------------------------------------------------------------------- candidates
def candidates(I):
    """(x, z, kind, near) points where a nod could stand; near = venue / hero ids within reach (for zone pulls)."""
    out = []
    # (a) the outer edge of every sidewalk: beside the shop fronts, away from the kerb
    for s in I.peds['sidewalks']:
        seg = I.seg_by_id.get(s['segment'])
        P = np.array([[p[0], p[2]] for p in s['points']], float)
        if seg is None or len(P) < 2:
            continue
        S, _ = seg
        for k in range(0, len(P), 2):
            p = P[k]
            c = S[int(np.argmin(np.linalg.norm(S - p, axis=1)))]
            n = p - c
            if np.linalg.norm(n) < 0.5:
                continue
            n /= np.linalg.norm(n)
            q = p + n * (s['width'] / 2 + 0.75)
            out.append((float(q[0]), float(q[1]), 'sidewalk'))
    # (b) open ground near the edges of plazas, parks and the boardwalk
    for x, z, y, yaw, width in I.deck[::2]:
        if width >= 4.5:
            out.append((round(x, 2), round(z, 2), 'boardwalk'))
    for aid, kind, poly in I.areas:
        if kind == 'boardwalk':
            continue
        x0, z0 = poly.min(0); x1, z1 = poly.max(0)
        for x in np.arange(x0 + 1.5, x1, 3.0):
            for z in np.arange(z0 + 1.5, z1, 3.0):
                if not G.point_in_poly(x, z, poly):
                    continue
                e = min(seg_dist(x, z, poly[i], poly[(i + 1) % len(poly)]) for i in range(len(poly)))
                if 1.5 <= e <= 7.0:
                    out.append((float(x), float(z), 'park' if kind == 'park' else 'plaza'))
    # (c) nooks round venues, landmark buildings and hero statues
    for vid, (cx, cz, w, d, yaw, m) in I.venues.items():
        for u, v in ring(w / 2 + m + 1.2, d / 2 + m + 1.2, 3.0):
            x, z = G.local_to_world(cx, cz, yaw, u, v)
            out.append((float(x), float(z), 'nook'))
    for hid, (hx, hz, r) in I.heroes.items():
        for k in range(12):
            a = 2 * math.pi * k / 12
            out.append((hx + math.cos(a) * (r + 1.8), hz + math.sin(a) * (r + 1.8), 'hero'))
    return out


def ring(hw, hd, step):
    pts = []
    for x in np.arange(-hw, hw + 1e-6, step):
        pts += [(x, -hd), (x, hd)]
    for z in np.arange(-hd + step, hd - 1e-6, step):
        pts += [(-hw, z), (hw, z)]
    return pts


def seg_dist(x, z, a, b):
    p = np.array([x, z]); ab = b - a
    t = np.clip(np.dot(p - a, ab) / max(np.dot(ab, ab), 1e-9), 0, 1)
    return float(np.linalg.norm(p - (a + t * ab)))


def usable(I, cands):
    keep, why = [], {}
    for x, z, kind in cands:
        d = I.district_of(x, z)
        r = None
        if d is None:
            r = 'no district'
        elif not I.on_land(x, z):
            r = 'water'
        elif I.on_road(x, z):
            r = 'road'
        elif I.in_building(x, z):
            r = 'building'
        elif I.blocked(x, z):
            r = 'prop'
        elif not I.flat(x, z):
            r = 'slope'
        else:
            _, wd = I.nearest_walk(x, z)
            in_area = any(G.point_in_poly(x, z, poly) for _, _, poly in I.areas)
            if wd > 6.0 and not in_area:
                r = 'off the walkways'
        if r:
            why[r] = why.get(r, 0) + 1
            continue
        keep.append((x, z, kind, d))
    log(f'candidates {len(cands)}, usable {len(keep)}; dropped {why}')
    return keep


# ---------------------------------------------------------------------- placing
def affinity(I, c, nod, zone):
    """How well a candidate suits a nod: its own hero statue, its zone's venues and kinds, any landmark nearby."""
    x, z, kind, _ = c
    s = 0.0
    hero = HEROES.get(nod['nodId'])
    if hero and hero in I.heroes:
        hx, hz, r = I.heroes[hero]
        dd = math.hypot(x - hx, z - hz)
        s += 6.0 if dd < r + 4.0 else (3.0 * max(0.0, 1 - dd / 40.0))
    places, kinds = ZONES.get(zone, ([], ()))
    if places:
        best = min((math.hypot(x - I.venues[p][0], z - I.venues[p][1]) - math.hypot(I.venues[p][2], I.venues[p][3]) / 2
                    for p in places if p in I.venues), default=None)
        if best is not None:
            s += 2.5 * max(0.0, 1 - max(best, 0.0) / 45.0)
    if kind in kinds:
        s += 1.5
    if kind in ('nook', 'hero'):
        s += 0.6
    return s


def interest(c):
    return {'hero': 1.0, 'nook': 0.8, 'park': 0.6, 'boardwalk': 0.6, 'plaza': 0.5, 'sidewalk': 0.3}.get(c[2], 0.0)


def sites(pool, count, taken, seed=()):
    """count spots from pool, as far apart as the district allows (from 35 m down to the tight gap): farthest-point
    sampling with a small pull toward nooks and open ground (a plain sweep when that packs too loosely), 12 m clear of the
    nods already placed in neighbouring districts."""
    pool = [c for c in pool if all(math.hypot(c[0] - q[0], c[1] - q[1]) >= 12.0 for q in taken)]
    ordered = sorted(pool, key=lambda c: (-interest(c), c[0], c[1]))
    for gap in np.arange(SPREAD, TIGHT_GAP - 1e-6, -1.0):
        sel = list(seed)
        while len(sel) < count:
            best, bs = None, -1e9
            for c in ordered:
                md = min([math.hypot(c[0] - q[0], c[1] - q[1]) for q in sel] or [SPREAD * 2])
                if md < gap:
                    continue
                sc = min(md, SPREAD * 2) + 4.0 * interest(c)
                if sc > bs + 1e-9:
                    best, bs = c, sc
            if best is None:
                break
            sel.append(best)
        if len(sel) < count:
            sel = list(seed)
            for c in sorted(pool, key=lambda c: (c[0], c[1])):
                if all(math.hypot(c[0] - q[0], c[1] - q[1]) >= gap for q in sel):
                    sel.append(c)
            sel = sel[:count]
        if len(sel) == count:
            return sel, float(gap)
    raise SystemExit(f'not enough room for {count} nods at {TIGHT_GAP} m')


def facing(I, c):
    """The ground height and the way the disc faces: on the boardwalk along the deck toward its nearest steps; in a park or
    plaza inward, away from its nearest edge; elsewhere toward the nearest walkway."""
    x, z, kind = c[0], c[1], c[2]
    if kind == 'boardwalk' and (round(x, 2), round(z, 2)) in I.deck_at:
        return I.deck_at[(round(x, 2), round(z, 2))]
    y = I.y(x, z)
    if kind in ('park', 'plaza'):
        for _, akind, poly in I.areas:
            if akind != 'boardwalk' and G.point_in_poly(x, z, poly):
                e = min(range(len(poly)), key=lambda i: seg_dist(x, z, poly[i], poly[(i + 1) % len(poly)]))
                a, b = poly[e], poly[(e + 1) % len(poly)]
                t = (b - a) / max(np.linalg.norm(b - a), 1e-6)
                n = np.array([-t[1], t[0]])
                if np.dot(n, poly.mean(0) - np.array([x, z])) < 0:
                    n = -n
                return y, math.degrees(math.atan2(n[0], n[1])) % 360
    w, _ = I.nearest_walk(x, z)
    return y, math.degrees(math.atan2(w[0] - x, w[1] - z)) % 360


def place(I, nods, eggs, usable_c):
    zone = {e['id']: e['zone'] for e in eggs}
    placed, taken = {}, []
    by_d = {}
    for c in usable_c:
        by_d.setdefault(c[3], []).append(c)
    groups = {}
    for n in nods:
        groups.setdefault(n['district'], []).append(n)
    for d in sorted(groups, key=lambda k: -len(groups[k])):       # the crowded districts choose first
        ns = sorted(groups[d], key=lambda n: n['nodId'])
        # a nod with a statue of its own in this district stands by it (the nearest usable spot round the plinth)
        seed = []
        for n in ns:
            hero = HEROES.get(n['nodId'])
            if hero in I.heroes:
                hx, hz, r = I.heroes[hero]
                near = sorted((math.hypot(c[0] - hx, c[1] - hz), c) for c in by_d.get(d, []) if math.hypot(c[0] - hx, c[1] - hz) < r + 6.0)
                if near:
                    seed.append(near[0][1])
        spots, gap = sites(by_d.get(d, []), len(ns), taken, seed)
        log(f'{d}: {len(ns)} nods, gap {gap:.0f} m')
        # assign: the strongest nod-spot pairs first (a nod's own statue, its zone's venues and kinds)
        pairs = sorted(((affinity(I, c, n, zone.get(n['nodId'], '')), n['nodId'], k) for n in ns for k, c in enumerate(spots)),
                       key=lambda t: (-t[0], t[1], t[2]))
        used_n, used_s = set(), set()
        for sc, nid, k in pairs:
            if nid in used_n or k in used_s:
                continue
            used_n.add(nid); used_s.add(k)
            c = spots[k]
            y, yaw = facing(I, c)
            placed[nid] = dict(x=round(c[0], 2), z=round(c[1], 2), y=round(y, 2), yaw=round(yaw, 1),
                               kind=c[2], district=c[3], score=round(sc, 2))
        taken += spots
    return placed


def main():
    I = Island()
    reg = json.load(open(REGISTRY))
    eggs = json.load(open(EGGS))['entries']
    use = usable(I, candidates(I))
    placed = place(I, reg['nods'], eggs, use)
    # spacing report
    ids = list(placed)
    gaps = {}
    for a in ids:
        pa = placed[a]
        gaps[a] = round(min(math.hypot(pa['x'] - placed[b]['x'], pa['z'] - placed[b]['z']) for b in ids if b != a), 1)
    for n in reg['nods']:
        p = placed[n['nodId']]
        assert p['district'] == n['district'], n['nodId']
        n['x'], n['z'] = p['x'], p['z']
    reg['_provisional'] = False
    reg['version'] = '1.0.0'
    reg['_note'] = ("Community nods (the 80 painted props from docs/research/community-eggs-2026-10 and the island plan). nodId is the "
                    "community-eggs id (lowercase, stable, never renamed); x and z are the placed props' positions in island metres (+X "
                    "east, +Z north, origin at the downtown plaza, the same frame as ctl:pos and districts.json), written by the frontend's "
                    "tools/world/islandgen/nods.py: every nod stands outdoors on land inside its district, where a player walks (a "
                    "sidewalk's outer edge, a plaza, park or boardwalk, a nook by a venue or statue), at least 16 m from the next. radiusM "
                    "is the collect radius (default 15). setId groups nods into sets; filling a set grants its off-chain cosmetic once. "
                    "Cosmetics here are rewards only: never sold, never tradable, never NFTs. A nod dropped from the file keeps its past "
                    "finds but no longer counts toward a set.")
    with open(REGISTRY, 'w') as f:
        json.dump(reg, f, indent=2)
        f.write('\n')
    client = dict(version=1, source='tools/world/islandgen/nods.py', units='island metres, +x east, +z north, y up; yaw degrees (0 = +z)',
                  nods=[dict(nodId=n['nodId'], setId=n['setId'], x=placed[n['nodId']]['x'], y=placed[n['nodId']]['y'],
                             z=placed[n['nodId']]['z'], yaw=placed[n['nodId']]['yaw']) for n in reg['nods']])
    with open(CLIENT, 'w') as f:
        json.dump(client, f, indent=1)
    from collections import Counter
    rep = dict(per_district=Counter(p['district'] for p in placed.values()), per_kind=Counter(p['kind'] for p in placed.values()),
               min_gap_m=min(gaps.values()), median_gap_m=sorted(gaps.values())[len(gaps) // 2], placed=placed, nearest_gap=gaps)
    json.dump(rep, open(os.path.join(OUT, 'nods_report.json'), 'w'), indent=1)
    log(f"placed {len(placed)}: {dict(rep['per_district'])}; kinds {dict(rep['per_kind'])}; gap min {rep['min_gap_m']} m, median {rep['median_gap_m']} m")


if __name__ == '__main__':
    main()
