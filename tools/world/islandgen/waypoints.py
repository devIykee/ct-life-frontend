"""Demo-route waypoints (World lane, W3; docs/design/demo-route.md): one standing spot per shot, snapped to a sidewalk
(or the verge of a road without one) or kept on its helipad, facing what the shot films. Written to
client/Assets/Resources/World/Island/demo_route.json for the dev-only `islandWaypoints` launch extra (IslandWaypoints).
Run after islandgen.py (needs out/roads.json and out/plan.json).
"""
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import numpy as np

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
OUT = os.path.join(REPO, 'client', 'Assets', 'Resources', 'World', 'Island', 'demo_route.json')

# (shot, id, title, near (x, z), look at (x, z), snap): snap 'sidewalk' moves to the nearest sidewalk or verge, 'pad'
# keeps the exact point (a helipad or plaza).
SHOTS = [
    (1, 'timeline_plaza', 'Timeline Square plaza', (0.0, 6.0), (-5.0, 49.0), 'pad'),
    (2, 'consensus_boulevard', 'Consensus Boulevard roundabout', (100.0, 92.0), (115.7, 104.7), 'sidewalk'),
    (3, 'ocean_drive', 'Ocean Drive', (-180.0, -220.0), (200.0, -220.0), 'sidewalk'),
    (4, 'marina_promenade', 'Marina promenade', (372.0, -284.0), (600.0, -300.0), 'sidewalk'),
    (5, 'marina_helipad', 'Marina helipad', (640.0, -262.0), (-180.0, 515.0), 'pad'),
    (6, 'lodge_helipad', 'MtnDAO lodge helipad', (-226.0, 428.0), (-180.0, 515.0), 'pad'),
    (7, 'builder_hills', 'Builder Hills', (380.0, 352.0), (392.0, 372.0), 'sidewalk'),
    (8, 'breakpoint_helipad', 'Breakpoint Park helipad', (-300.0, 214.0), (-217.0, 295.0), 'pad'),
]


def snap(roads, x, z):
    """The nearest sidewalk centre (or 1.5 m beyond the road edge) to (x, z), with its height."""
    best = None
    for s in roads['segments']:
        if s['kind'] == 'tunnel' or s.get('underground'):
            continue
        p = np.array([[q[0], q[2]] for q in s['samples']])
        y = np.array([q[1] for q in s['samples']])
        if len(p) < 2:
            continue
        tan = np.gradient(p, axis=0)
        tan /= np.maximum(np.linalg.norm(tan, axis=1, keepdims=True), 1e-9)
        rn = np.stack([tan[:, 1], -tan[:, 0]], axis=1)             # right of travel (+x east, +z north)
        for side, w in ((1.0, s['widthRight']), (-1.0, s['widthLeft'])):
            off = w + (s['sidewalk'] * 0.5 if s['sidewalk'] > 0 else 1.5)
            q = p + rn * side * off
            d = np.hypot(q[:, 0] - x, q[:, 1] - z)
            k = int(np.argmin(d))
            if best is None or d[k] < best[0]:
                best = (float(d[k]), float(q[k, 0]), float(y[k]) + (0.15 if s['sidewalk'] > 0 else 0.0), float(q[k, 1]))
    return best[1], best[2], best[3]


def main():
    roads = json.load(open(os.path.join(HERE, 'out', 'roads.json')))
    plan = json.load(open(os.path.join(HERE, 'out', 'plan.json')))
    pads = {(round(h[0]), round(h[2])): h[1] for h in plan['helipads']}
    wps = []
    for shot, wid, title, (x, z), (lx, lz), mode in SHOTS:
        if mode == 'sidewalk':
            x, y, z = snap(roads, x, z)
        else:
            y = pads.get((round(x), round(z)))                   # None: the runtime samples the ground
        yaw = math.degrees(math.atan2(lx - x, lz - z)) % 360.0    # clockwise from north
        wps.append(dict(shot=shot, id=wid, title=title, x=round(x, 2), y=None if y is None else round(y + 0.3, 2), z=round(z, 2),
                        yaw=round(yaw, 1)))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    json.dump(dict(version=1, source='tools/world/islandgen/waypoints.py', waypoints=wps), open(OUT, 'w'), indent=1)
    print(f'[waypoints] {len(wps)} -> {OUT}')


if __name__ == '__main__':
    main()
