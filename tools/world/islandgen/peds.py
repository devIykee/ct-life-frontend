"""Pedestrian path data for CT Island (World lane, W4): where people can walk, for the Feel lane's walking pedestrians.

Writes client/Assets/Resources/World/Island/pedestrian_paths.json (run after islandgen.py and dress.py):
  sidewalks  polylines down the middle of every sidewalk, trimmed to the junction cuts: {id, segment, side 'left'|'right',
             width m, points [[x, y, z], ...]} (points every ~4 m, in the segment's travel direction)
  corners    the sidewalk round each junction corner (the kerb fillet offset to the sidewalk middle): {id, junction, width,
             points}; their ends meet the sidewalks' ends (within ~1.5 m)
  (y None on an area: sample the ground there, e.g. a downward raycast on the Ground / Road layers)
  crossings  zebra crossings at signal and stop junctions (the ones the builder paints): {id, junction, segment, signal
             bool, width, a [x, y, z], b [x, y, z]}; a and b lie on the two sidewalks' middles
  areas      open walkable ground: the Timeline plaza, parks, the boardwalk and the hero plazas: {id, kind, y, poly [[x, z]...]}
Units: island metres, +x east, +z north, y up (sidewalk surface + 0.15 already applied). Junction cuts and fillets come from
out/roads.json, the same data RoadNetwork is built from.
"""
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import numpy as np

import geom as G
import layout as L

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(REPO, 'client', 'Assets', 'Resources', 'World', 'Island', 'pedestrian_paths.json')


def r3(p):
    return [round(float(p[0]), 2), round(float(p[1]), 2), round(float(p[2]), 2)]


def resample(pts, step=4.0):
    pts = np.asarray(pts, float)
    if len(pts) < 2:
        return pts
    d = np.r_[0, np.cumsum(np.linalg.norm(np.diff(pts[:, [0, 2]], axis=0), axis=1))]
    if d[-1] < 0.5:
        return pts[[0, -1]]
    t = np.linspace(0, d[-1], max(2, int(d[-1] / step) + 1))
    return np.stack([np.interp(t, d, pts[:, k]) for k in range(3)], axis=1)


def main():
    roads = json.load(open(os.path.join(HERE, 'out', 'roads.json')))
    plan = json.load(open(os.path.join(HERE, 'out', 'plan.json')))
    dress = json.load(open(os.path.join(HERE, 'out', 'dress.json')))
    cuts = {}                                            # (segment, 'from'|'to') -> cut point
    for j in roads['intersections']:
        for part in (j.get('parts') or [j]):
            for a in part['approaches']:
                cuts[(a['segment'], a['end'])] = np.array(a['cut'])
    sidewalks = []
    for s in roads['segments']:
        if s['sidewalk'] <= 0 or s['kind'] == 'tunnel':
            continue
        P = np.array(s['samples'], float)
        xz = P[:, [0, 2]]
        d = np.r_[0, np.cumsum(np.linalg.norm(np.diff(xz, axis=0), axis=1))]
        lo, hi = 0.0, d[-1]
        for end in ('from', 'to'):
            c = cuts.get((s['id'], end))
            if c is None:
                continue
            k = int(np.argmin(np.linalg.norm(xz - c[[0, 2]], axis=1)))
            if end == 'from':
                lo = max(lo, d[k])
            else:
                hi = min(hi, d[k])
        if hi - lo < 2.0:
            continue
        t = np.linspace(lo, hi, max(2, int((hi - lo) / 4.0) + 1))
        Q = np.stack([np.interp(t, d, P[:, k]) for k in range(3)], axis=1)
        tan = np.gradient(Q[:, [0, 2]], axis=0)
        tan /= np.maximum(np.linalg.norm(tan, axis=1, keepdims=True), 1e-9)
        rn = np.stack([tan[:, 1], -tan[:, 0]], axis=1)
        for side, w in (('right', s['widthRight']), ('left', -s['widthLeft'])):
            off = w + (s['sidewalk'] * 0.5 if side == 'right' else -s['sidewalk'] * 0.5)
            pts = np.stack([Q[:, 0] + rn[:, 0] * off, Q[:, 1] + 0.15, Q[:, 2] + rn[:, 1] * off], axis=1)
            sidewalks.append(dict(id=f'{s["id"]}_{side[0].upper()}', segment=s['id'], side=side, width=round(float(s['sidewalk']), 2),
                                  points=[r3(p) for p in pts]))
    corners, crossings = [], []
    segs = {s['id']: s for s in roads['segments']}
    for j in roads['intersections']:
        centre = np.array(j['center'])
        for pi, part in enumerate(j.get('parts') or [j]):
            for fi, f in enumerate(part.get('fillets') or []):
                sw = f.get('sidewalk', 0.0)
                if sw <= 0 or len(f['points']) < 2:
                    continue
                F = np.array(f['points'], float)
                out = []
                for k in range(len(F)):
                    v = F[k, [0, 2]] - centre[[0, 2]]
                    v /= max(np.linalg.norm(v), 1e-9)
                    out.append([F[k, 0] + v[0] * sw * 0.5, F[k, 1] + 0.15, F[k, 2] + v[1] * sw * 0.5])
                corners.append(dict(id=f'{j["id"]}_c{pi}_{fi}', junction=j['id'], width=round(float(sw), 2), points=[r3(p) for p in resample(out, 2.0)]))
            if j['kind'] not in ('signal', 'stop'):
                continue
            for ai, a in enumerate(part['approaches']):
                s = segs.get(a['segment'])
                if s is None or s['sidewalk'] <= 0 or s['kind'] == 'bridge':
                    continue
                cut = np.array(a['cut'], float)
                dx, dz = a['dir']
                rg = np.array([dz, 0.0, -dx])
                dvec = np.array([dx, 0.0, dz])
                uR = float(np.dot(np.array(a['right']) - cut, rg)); uL = float(np.dot(np.array(a['left']) - cut, rg))
                mid = cut + dvec * 2.8
                pa = mid + rg * (uL - s['sidewalk'] * 0.5); pb = mid + rg * (uR + s['sidewalk'] * 0.5)
                pa[1] = pb[1] = cut[1] + 0.15
                crossings.append(dict(id=f'{j["id"]}_x{pi}_{ai}', junction=j['id'], segment=s['id'], signal=j['kind'] == 'signal',
                                      width=3.2, a=r3(pa), b=r3(pb)))
    areas = []
    for pz in plan.get('plazas') or []:
        poly = pz.get('poly') or pz.get('polygon')
        if poly:
            areas.append(dict(id=pz.get('id', 'plaza'), kind='plaza', y=None,
                              poly=[[round(float(q[0]), 2), round(float(q[-1]), 2)] for q in poly]))
    for name, poly in L.PARKS:
        areas.append(dict(id=name, kind='park', y=None, poly=[[round(float(x), 2), round(float(z), 2)] for x, z in poly]))
    for k, bw in enumerate(plan.get('boardwalk') or []):
        if bw.get('inner') and bw.get('outer'):
            ring = list(bw['inner']) + list(reversed(bw['outer']))
            areas.append(dict(id=f'boardwalk_{k}', kind='boardwalk', y=None, poly=[[round(float(q[0]), 2), round(float(q[-1]), 2)] for q in ring]))
    for k, hp in enumerate(dress.get('heroPlazas') or []):
        x, y, z, rr = hp[0], hp[1], hp[2], hp[3]
        areas.append(dict(id=f'hero_plaza_{k}', kind='plaza', y=round(y + 0.06, 2),
                          poly=[[round(x + math.cos(a) * rr, 2), round(z + math.sin(a) * rr, 2)] for a in np.linspace(0, math.tau, 17)[:-1]]))
    doc = dict(version=1, source='tools/world/islandgen/peds.py', units='island metres, +x east, +z north, y up',
               sidewalks=sidewalks, corners=corners, crossings=crossings, areas=areas)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    json.dump(doc, open(OUT, 'w'), separators=(',', ':'))
    total = sum(sum(math.dist(a, b) for a, b in zip(s['points'], s['points'][1:])) for s in sidewalks)
    print(f'[peds] {len(sidewalks)} sidewalks ({total / 1000:.1f} km), {len(corners)} corners, {len(crossings)} crossings, '
          f'{len(areas)} areas -> {OUT} ({os.path.getsize(OUT) / 1e3:.0f} kB)')


if __name__ == '__main__':
    main()
