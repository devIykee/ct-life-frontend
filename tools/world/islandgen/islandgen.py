"""CT Island v2 generator: authored layout -> heightfield, ground maps, road network, placements, plan image.

Run (repo root):
  uv run --no-project --with pillow==12.3.0 --with scipy==1.18.1 --with numpy==2.4.2 \
      python -I tools/world/islandgen/islandgen.py [--plan-only]
Outputs: tools/world/islandgen/out/{height.r16, terrain.json, holes.png, control.png, macro.png, roads.json, plan.json}
         docs/design/ct-island-v2-plan.png
The Unity side (CTLife.EditorTools.IslandV2.IslandV2Builder) reads out/ and builds the meshes and prefabs.
"""
import json
import math
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import numpy as np
from PIL import Image
from scipy import ndimage

import geom as G
import layout as L
import network as NW
import terrain as T

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(HERE, 'out')
AREAS = os.path.join(REPO, 'client', 'Assets', 'Resources', 'UI', 'Map', 'areas.json')
H_MIN, H_MAX = L.SEA - 34.0, 200.0


def log(*a):
    print('[islandgen]', *a, flush=True)


def load_areas():
    doc = json.load(open(AREAS))
    out = {}
    for a in doc['areas']:
        w = a.get('walkable') or [-12, -10, 12, 10]
        out[a['id']] = dict(min=a['min'], size=a['size'], walk=w, title=a.get('title'))
    return out


def side_normal(t, side):
    rn = np.array([t[1], -t[0]])
    ln = -rn
    key = {'north': lambda n: n[1], 'south': lambda n: -n[1], 'east': lambda n: n[0], 'west': lambda n: -n[0]}[side]
    return (rn, 'r') if key(rn) >= key(ln) else (ln, 'l')


def place(net, entry, areas, nat):
    a = areas.get(entry['id'])
    if a is None:
        return None
    mn, sz, walk = a['min'], a['size'], a['walk']
    pl = entry['place']
    if pl[0] == 'end':
        _, route, node, gap = pl
        poly = net.route_poly[route]
        P = poly[-1]
        t = poly[-1] - poly[-6]
        t = t / np.linalg.norm(t)
        seg = next(s for s in net.segs if s.route['id'] == route and s.to == node)
        x, z = P + t * (gap + (-mn[1]))
        yaw = math.degrees(math.atan2(t[0], t[1])) % 360.0
        y = float(seg.y[-1])
        road = dict(segment=seg.id, route=route, point=[float(P[0]), y, float(P[1])])
    elif pl[0] == 'at':
        x, z, yaw = pl[1], pl[2], pl[3]
        y = pl[4] if len(pl) > 4 and pl[4] is not None else float(T.sample(nat, np.array([x]), np.array([z]))[0])
        road = None
        gap = 4.0
    else:
        _, route, near, side, gap = pl
        if isinstance(near, str) and near.startswith('end-'):
            poly = net.route_poly[route]
            sl = G.arc_lengths(poly)
            back = sl[-1] - float(near[4:])
            near = (float(np.interp(back, sl, poly[:, 0])), float(np.interp(back, sl, poly[:, 1])))
        P, t, best = net.nearest_on_route(route, near)
        if best is None:
            raise RuntimeError(f'{entry["id"]}: no segment on route {route!r} (routes: {sorted(net.route_poly)})')
        seg, j = best
        n, which = side_normal(t, side)
        hw = seg.out_r if which == 'r' else seg.out_l
        dist = hw + gap + (-mn[1])
        x, z = P + n * dist
        yaw = math.degrees(math.atan2(n[0], n[1])) % 360.0
        y = float(seg.y[j]) + (0.15 if seg.prof['kerb'] else 0.0)
        road = dict(segment=seg.id, route=route, point=[float(P[0]), float(seg.y[j]), float(P[1])])
    door_local = (0.0, float(walk[1]))
    dx, dz = G.local_to_world(x, z, yaw, *door_local)
    fx0, fz0, fx1, fz1 = mn[0] - 5.0, mn[1] - gap, mn[0] + sz[0] + 5.0, mn[1] + sz[1] + 5.0   # diorama trim keeps frame + 4 m
    ccx, ccz = G.local_to_world(x, z, yaw, (fx0 + fx1) / 2, (fz0 + fz1) / 2)
    pad = dict(cx=ccx, cz=ccz, w=fx1 - fx0, d=fz1 - fz0, yaw=yaw, y=y)
    frame_c = G.local_to_world(x, z, yaw, mn[0] + sz[0] / 2, mn[1] + sz[1] / 2)
    shell = L.SHELLS.get(entry['id']) if hasattr(L, 'SHELLS') else None
    return dict(id=entry['id'], district=entry['district'], shell=shell, origin=[round(float(x), 3), round(y, 3), round(float(z), 3)],
                yaw=round(float(yaw), 3), door=[round(dx, 3), round(y, 3), round(dz, 3)], frame_min=mn, frame_size=sz,
                walk=walk, center=[round(frame_c[0], 3), round(frame_c[1], 3)], road=road, pad=pad, title=a['title'])


def venue_road_clashes(net, venues):
    """Venues whose frame overlaps a road's carriageway or sidewalk (their own street's edge excluded by the gap)."""
    out = {}
    for v in venues:
        mn, sz = v['frame_min'], v['frame_size']
        us = np.linspace(mn[0], mn[0] + sz[0], 14)
        ws = np.linspace(mn[1], mn[1] + sz[1], 14)
        pts = np.array([G.local_to_world(v['origin'][0], v['origin'][2], v['yaw'], u, w) for u in us for w in ws])
        worst = 0.0
        for s in net.segs:
            d = np.min(np.linalg.norm(pts[:, None, :] - s.pts[None, ::2, :], axis=2), axis=1)
            over = max(s.out_r, s.out_l) - d.min()
            if over > 0.3:
                worst = max(worst, over)
                out[v['id']] = (s.route['id'], round(float(over), 1))
    return out


def quays(coast, ctypes, h, width=7.5):
    """Quay edges (coast type quay): wall line 0.8 m seaward of the coast, the paved strip back to `width` m inland.
    Each run: points [x, z], top (paving height per point, from the flats behind it), and the inner line."""
    out = []
    n = len(coast)
    q = ctypes == 3
    if not q.any():
        return out
    start = int(np.argmin(q)) if not q.all() else 0           # begin outside a run so runs never wrap
    order = np.roll(np.arange(n), -start)
    run = []

    def flush():
        if len(run) < 4:
            return
        P = coast[run]
        P = G.resample(P, 2.0)
        tan = G.tangents(P)
        sea = G.right_normals(tan)                              # right of the counter-clockwise outline = the sea
        wall = P + sea * 0.8
        inner = P - sea * width
        top = ndimage.uniform_filter1d(T.sample(h, (P - sea * 2.5)[:, 0], (P - sea * 2.5)[:, 1]), 5, mode='nearest') + 0.06
        inner_y = T.sample(h, inner[:, 0], inner[:, 1]) + 0.06
        dry = top > L.SEA + 0.6                                   # no quay across the river mouth
        k = 0
        while k < len(P):
            if not dry[k]:
                k += 1
                continue
            j = k
            while j < len(P) and dry[j]:
                j += 1
            if j - k >= 4:
                sl = slice(k, j)
                out.append(dict(wall=[[round(float(x), 2), round(float(z), 2)] for x, z in wall[sl]],
                                edge=[[round(float(x), 2), round(float(z), 2)] for x, z in P[sl]],
                                inner=[[round(float(x), 2), round(float(z), 2)] for x, z in inner[sl]],
                                top=[round(float(y), 3) for y in top[sl]], innerTop=[round(float(y), 3) for y in inner_y[sl]],
                                bottom=L.SEA - 3.0))
            k = j
    for i in order:
        if q[i]:
            run.append(i)
        else:
            flush()
            run = []
    flush()
    return out


def boardwalk(net, h, width=6.0):
    """The crescent bay boardwalk: a timber deck on posts along Ocean Drive's seaward sidewalk, with steps down to the
    beach where the beach paths start. Returns runs of {left, right (seaward edge), y, steps}."""
    out = []
    for s in net.segs:
        if s.route['id'] != 'ocean_drive':
            continue
        pts, ys, ug = net.trimmed(s)
        if len(pts) < 4:
            continue
        tan = G.tangents(pts)
        rn = G.right_normals(tan)
        mid = len(pts) // 2
        probe_r = float(T.sample(h, np.array([pts[mid, 0] + rn[mid, 0] * 16]), np.array([pts[mid, 1] + rn[mid, 1] * 16]))[0])
        probe_l = float(T.sample(h, np.array([pts[mid, 0] - rn[mid, 0] * 16]), np.array([pts[mid, 1] - rn[mid, 1] * 16]))[0])
        side = 1.0 if probe_r < probe_l else -1.0
        w0 = (s.out_r if side > 0 else s.out_l)
        a = pts + rn * side * w0
        b = pts + rn * side * (w0 + width)
        y = ys + 0.15
        under = T.sample(h, b[:, 0], b[:, 1])
        out.append(dict(segment=s.id, inner=a, outer=b, y=y, side=side))
    if not out:
        return []
    # one continuous deck along the bay: the runs joined across the junctions (Ocean Drive's segments go west to east)
    out.sort(key=lambda r: float(r['inner'][0][0]))
    A = np.vstack([r['inner'] for r in out]); B = np.vstack([r['outer'] for r in out]); Y = np.concatenate([r['y'] for r in out])
    keep = np.ones(len(A), bool)
    for k in range(1, len(A)):
        if np.dot(A[k] - A[k - 1], A[min(k + 1, len(A) - 1)] - A[k]) < 0:
            keep[k] = False                                       # tiny back-steps where runs meet
    A, B, Y = A[keep], B[keep], Y[keep]
    sl = G.arc_lengths(A)
    t = np.arange(0.0, sl[-1], 2.0)
    A = np.stack([np.interp(t, sl, A[:, 0]), np.interp(t, sl, A[:, 1])], 1)
    B2 = np.stack([np.interp(t, sl, B[:, 0]), np.interp(t, sl, B[:, 1])], 1)
    Y = ndimage.uniform_filter1d(np.interp(t, sl, Y), 3, mode='nearest')
    ground = T.sample(h, B2[:, 0], B2[:, 1])
    steps = []
    for name, pts, w in L.TRACKS:
        if not name.startswith('beach_path'):
            continue
        q = np.array(pts[0], float)
        k = int(np.argmin(np.linalg.norm(B2 - q, axis=1)))
        if np.linalg.norm(B2[k] - q) < 30:
            steps.append(k)
    return [dict(inner=[[round(float(x), 2), round(float(z), 2)] for x, z in A],
                 outer=[[round(float(x), 2), round(float(z), 2)] for x, z in B2],
                 y=[round(float(v), 3) for v in Y], ground=[round(float(v), 3) for v in ground], steps=steps)]


def tunnel_holes(net, h):
    """Cells to drop from the terrain mesh at tunnel portals: just inside each portal, over the road corridor."""
    holes = np.zeros(h.shape, bool)
    for s in net.segs:
        if s.underground is None or not s.underground.any():
            continue
        ug = s.underground
        edges = [k for k in range(1, len(ug)) if ug[k] != ug[k - 1]]
        sl = G.arc_lengths(s.pts)
        for k in edges:
            inside = range(k, min(len(ug), k + 5)) if ug[k] else range(max(0, k - 5), k)
            for j in inside:
                x, z = s.pts[j]
                hw = max(s.cw_r, s.cw_l) + 1.0
                ix0, ix1 = np.searchsorted(T.AXIS, x - hw - 2), np.searchsorted(T.AXIS, x + hw + 2)
                iz0, iz1 = np.searchsorted(T.AXIS, z - hw - 2), np.searchsorted(T.AXIS, z + hw + 2)
                d = np.hypot(T.X[iz0:iz1, ix0:ix1] - x, T.Z[iz0:iz1, ix0:ix1] - z)
                holes[iz0:iz1, ix0:ix1] |= d < hw
    return holes


def write_png(path, arr, mode):
    img = Image.fromarray(np.clip(arr * 255.0 + 0.5, 0, 255).astype(np.uint8), mode)
    img.transpose(Image.Transpose.FLIP_TOP_BOTTOM).save(path, optimize=True)   # PNG row 0 = north (top)


def main():
    t0 = time.time()
    os.makedirs(OUT, exist_ok=True)
    areas = load_areas()
    coast, ctypes, ccliff = T.coastline()
    land = T.land_mask(coast)
    inland = ndimage.distance_transform_edt(land) * L.CELL
    seaward = ndimage.distance_transform_edt(~land) * L.CELL
    log(f'coast {len(coast)} samples, land {land.mean() * 4 * L.DOMAIN ** 2 / 1e6:.2f} km2 ({time.time() - t0:.1f}s)')
    emaps = {}
    nat, coastw = T.natural_heights(land, inland, seaward, coast, ctypes, ccliff, stats=emaps)
    log(f'relief y {nat[land].min():.1f}..{nat[land].max():.1f} ({time.time() - t0:.1f}s)')
    T.fit_water_levels(nat)
    log(f'river source y {L.RIVER[0][2]}, lakes {[l["level"] for l in L.LAKES]}')
    river = T.river_samples()
    net = NW.Network()
    net.build_routes(nat, T.sample)
    net.make_roundabouts()
    net.solve_heights(nat, T.sample, river)
    net.build_junctions()
    nxt = net.lane_graph()
    steep = [(s.id, s.route['id'], round(getattr(s, 'max_grade', 0), 3)) for s in net.segs
             if getattr(s, 'max_grade', 0) > L.MAX_GRADE[s.kind] + 0.01]
    log(f'roads: {len(net.segs)} segments, {len(net.junctions)} junctions, {sum(len(v) for v in nxt.values())} lane links; '
        f'unused nodes {net.unused_nodes}; grade over limit {steep[:8]}')
    venues = [v for v in (place(net, e, areas, nat) for e in L.VENUES) if v]
    homes = [v for v in (place(net, e, areas, nat) for e in L.HOMES) if v]
    clashes = venue_road_clashes(net, venues + homes)
    log(f'venue frames on roads: {clashes or "none"}')
    h, river = T.carve_river(nat, land)
    h, walls, road_core = T.carve_roads(h, net)
    heli = resolve_helipads(venues, h)
    for p in heli:                                   # a pad next to a venue sits at the venue's level
        near = min(venues, key=lambda v: math.hypot(v['origin'][0] - p[0], v['origin'][2] - p[2]))
        if math.hypot(near['origin'][0] - p[0], near['origin'][2] - p[2]) < 70:
            p[1] = near['origin'][1]
    heli_pads = [dict(cx=p[0], cz=p[2], w=24.0, d=24.0, yaw=0.0, y=float(p[1]) + 0.12) for p in heli]
    lm_pads = []
    for lb in getattr(L, 'LANDMARK_BUILDINGS', []):
        if lb['h'] <= 0:
            continue
        (cx, cz), (w, d) = lb['at'], lb['size']
        pts = np.array([[cx + u, cz + v] for u in (-w / 2, 0, w / 2) for v in (-d / 2, 0, d / 2)])
        y = (lb['fix_y'] if lb.get('fix_y') is not None else float(np.median(T.sample(h, pts[:, 0], pts[:, 1])))) + 0.12
        lb['y'] = round(y - 0.12, 3)
        lm_pads.append(dict(cx=cx, cz=cz, w=w + 6.0, d=d + 6.0, yaw=lb['yaw'], y=y))
    h = T.carve_pads(h, heli_pads + lm_pads + [v['pad'] for v in venues + homes])      # venue pads last and exact ...
    h = np.where(np.isfinite(road_core), road_core - 0.12, h)                   # ... roads stay on their profile
    log(f'retaining walls: {len(walls)} runs, {sum(len(w["points"]) for w in walls) * 2} m')
    holes = tunnel_holes(net, h)
    gz, gx = np.gradient(h, L.CELL)
    slope = np.hypot(gx, gz)
    excl = np.zeros(h.shape)
    for d in L.FLATS:
        excl = np.maximum(excl, T.poly_mask(d[1], 20.0))
    forest = T.forest_mask(h, land, slope, excl)
    ctl, macro, slope = T.ground_maps(h, land, coastw, inland, river, forest, emaps)
    log(f'carved; terrain y {h.min():.1f}..{h.max():.1f}; holes {int(holes.sum())} cells ({time.time() - t0:.1f}s)')

    if '--plan-only' not in sys.argv:
        q = np.clip((h - H_MIN) / (H_MAX - H_MIN), 0, 1)
        (q * 65535 + 0.5).astype('<u2').tofile(os.path.join(OUT, 'height.r16'))
        json.dump(dict(version=2, samples=T.N, cell=L.CELL, min=[-L.DOMAIN, -L.DOMAIN], size=2 * L.DOMAIN, hmin=H_MIN, hmax=H_MAX,
                       sea=L.SEA, flightCeiling=L.FLIGHT_CEILING, layout='row-major, row 0 = south (z = -domain), col 0 = west',
                       generated=time.strftime('%Y-%m-%d')), open(os.path.join(OUT, 'terrain.json'), 'w'), indent=1)
        write_png(os.path.join(OUT, 'holes.png'), holes.astype(float), 'L')
        write_png(os.path.join(OUT, 'control.png'), T.resize(ctl, 1024), 'RGBA')
        write_png(os.path.join(OUT, 'macro.png'), T.resize(macro, 1024), 'RGBA')
        write_png(os.path.join(OUT, 'forest.png'), forest, 'L')                              # dress.py scatters trees here
        write_png(os.path.join(OUT, 'ground.png'), np.stack([ctl[..., 1], ctl[..., 3], np.clip(emaps.get('talus', np.zeros_like(h)) * 2, 0, 1)], -1), 'RGB')
        write_roads(net, os.path.join(OUT, 'roads.json'), walls)
        write_plan(net, venues, homes, coast, river, os.path.join(OUT, 'plan.json'), h,
                   extra=dict(quays=quays(coast, ctypes, h), boardwalk=boardwalk(net, h),
                              landmarkBuildings=[dict(lb, y=lb.get('y', float(T.sample(h, np.array([lb['at'][0]]), np.array([lb['at'][1]]))[0])))
                                                 for lb in getattr(L, 'LANDMARK_BUILDINGS', [])]))
        import textures
        tex_dir = os.path.join(REPO, 'client', 'Assets', 'World', 'Island', 'Textures')
        textures.write_all(h, river, net, macro, tex_dir, ctl=ctl)
        write_png(os.path.join(tex_dir, 'terrain_control.png'), T.resize(ctl, 1024), 'RGBA')
        write_png(os.path.join(tex_dir, 'terrain_macro.png'), T.resize(macro, 1024), 'RGBA')
        log(f'textures written ({time.time() - t0:.1f}s)')
    import render
    render.plan_image(h, land, net, venues, homes, river, forest, os.path.join(REPO, 'docs', 'design', 'ct-island-v2-plan.png'),
                      landmarks=resolve_landmarks(venues), helipads=resolve_helipads(venues, h))
    log(f'done ({time.time() - t0:.1f}s)')


def r3(v):
    return [round(float(c), 3) for c in v]


def write_roads(net, path, walls=None):
    nodes = []
    for nid, p in net.nodes.items():
        if nid not in net.node_y:
            continue
        nodes.append(dict(id=nid, p=r3([p[0], net.node_y[nid], p[1]]), kind=net.node_kind.get(nid, 'unused')))
    segs = []
    for s in net.segs:
        pts, ys, ug = net.trimmed(s)
        kind = 'bridge' if s.bridge else 'tunnel' if s.tunnel else s.kind
        segs.append(dict(
            id=s.id, name=s.name, route=s.route['id'], from_=s.frm, to=s.to, kind=kind, base=s.kind,
            lanesForward=s.F, lanesBack=s.B, laneWidth=L.LANE, speedLimit=round(s.speed, 2), oneWay=s.one_way,
            controlPoints=[r3([c[0], float(np.interp(0, [0], [0])) if False else 0.0, c[1]]) for c in s.ctrl],
            samples=[r3([x, y, z]) for (x, z), y in zip(pts, ys)],
            underground=[bool(u) for u in ug] if ug is not None else None,
            widthRight=round(s.cw_r, 3), widthLeft=round(s.cw_l, 3), sidewalk=s.sidewalk, median=s.prof['median'],
            parking=s.prof['parking'], shoulder=s.prof['shoulder'], kerb=s.prof['kerb'], markings=s.prof['markings'],
            surface=s.prof['surface'], ring=getattr(s, 'ring', None),
        ))
    # control point heights from the profile at each control point
    for sd, s in zip(segs, net.segs):
        sl = G.arc_lengths(s.pts)
        out = []
        for c in s.ctrl:
            j = int(np.argmin(np.linalg.norm(s.pts - np.array(c), axis=1)))
            out.append(r3([c[0], s.y[j], c[1]]))
        sd['controlPoints'] = out
    lanes = [dict(segment=k[0], lane=k[1], next=[dict(segment=a, lane=b) for a, b in v]) for k, v in sorted(net.lanes_next.items())]
    rings = {}
    for j in net.junctions:
        if j['ring']:
            rings.setdefault(j['ring'], []).append(j['node'])
    inters = []
    for j in net.junctions:
        if j['ring']:
            continue
        inters.append(dict(id=j['id'], nodeIds=[j['node']], kind=j['kind'], center=r3(j['center']), polygon=[r3(p) for p in j['polygon']],
                           approaches=j['approaches'], fillets=j['fillets']))
    for c, ns in rings.items():
        js = [j for j in net.junctions if j['ring'] == c]
        cx = float(np.mean([j['center'][0] for j in js])); cz = float(np.mean([j['center'][2] for j in js]))
        inters.append(dict(id=f'rb_{c}', nodeIds=ns, kind='roundabout', center=r3([cx, js[0]['center'][1], cz]),
                           radius=L.ROUNDABOUTS[c], parts=[dict(node=j['node'], polygon=[r3(p) for p in j['polygon']],
                                                                   approaches=j['approaches'], fillets=j['fillets']) for j in js]))
    doc = dict(version=1, seaLevel=L.SEA, laneWidth=L.LANE, driving='right', laneIndexing='0..F-1 forward (from->to, right of centreline, 0 innermost); F..F+B-1 back (0 innermost)',
               nodes=nodes, segments=segs, intersections=inters, lanes=lanes, walls=walls or [])
    txt = json.dumps(doc, separators=(',', ':')).replace('"from_"', '"from"')
    open(path, 'w').write(txt)


def resolve_landmarks(venues):
    by = {v['id']: v for v in venues}
    out = []
    for lm in L.LANDMARKS:
        lm = dict(lm)
        if lm.get('venue'):
            v = by[lm['venue']]
            lm['at'] = (v['center'][0], v['center'][1])
        out.append(lm)
    return out


def resolve_helipads(venues, h):
    by = {v['id']: v for v in venues}
    out = []
    for p in L.HELIPADS:
        if p[0] == 'venue':
            v = by[p[1]]
            mn, sz = v['frame_min'], v['frame_size']
            x, z = G.local_to_world(v['origin'][0], v['origin'][2], v['yaw'], mn[0] + sz[0] + p[2] * 0.5, mn[1] + sz[1] * 0.5)
            out.append([round(x, 2), round(v['origin'][1], 2), round(z, 2)])
        else:
            y = float(T.sample(h, np.array([p[0]]), np.array([p[1]]))[0])
            out.append([p[0], round(y, 2), p[1]])
    return out


def write_plan(net, venues, homes, coast, river, path, h, extra=None):
    line, level, hw, depth = river
    doc = dict(
        version=2, sea=L.SEA, flightCeiling=L.FLIGHT_CEILING,
        coast=[r3([x, 0, z]) for x, z in coast[::2]],
        districts=[dict(id=d['id'], name=d['name'], label=d['label'], poly=d['poly']) for d in L.DISTRICTS],
        venues=venues, homes=homes,
        landmarks=resolve_landmarks(venues), helipads=resolve_helipads(venues, h), fields=L.FIELDS, parks=L.PARKS,
        river=dict(points=[r3([x, l, z]) for (x, z), l in zip(line[::2], level[::2])], halfWidth=[round(float(v), 2) for v in hw[::2]]),
        lakes=L.LAKES, islet=L.ISLET, causeway=L.CAUSEWAY,
        plazas=[dict(id=L.DT_PLAZA[0], polygon=[[round(float(x), 3), round(float(z), 3)] for x, z in L.dt_block_poly(*L.DT_PLAZA[1])])],
    )
    doc.update(extra or {})
    json.dump(doc, open(path, 'w'), indent=1)


if __name__ == '__main__':
    main()
