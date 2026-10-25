"""contract/ids/districts.json from the island plan, for the backend's street and vehicles rooms (2026-10-24, R3).

Same output as the backend's tools/scripts/import-districts.ts (island = coast, islet, causeway; districts; venues), plus
the land the coast outline misses: landmark pads built out over the water (`fix_y` in layout.LANDMARK_BUILDINGS, e.g. The
Rug Room on the arcade pier) and venues on a deck over the water (the Prediction Pit), each as its pad rectangle grown by MARGIN so it overlaps the beach. Without them the street
room answers moves on the pier with OUT_OF_BOUNDS and other players cannot see you in the casino.
The backend importer needs the same addition (plan.extraLand); until then the backend can copy this file as is.

Run (repo root): python3 -I tools/world/islandgen/export_districts.py
"""
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import layout as L  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
PLAN = os.path.join(HERE, 'out', 'plan.json')
DEST = os.path.join(REPO, 'contract', 'ids', 'districts.json')
MARGIN = 1.5


def rnd(n):
    return math.floor(n * 100 + 0.5) / 100   # JS Math.round, as the backend importer


def circle(c, r, n=32):
    return [[rnd(c[0] + r * math.cos(2 * math.pi * i / n)), rnd(c[1] + r * math.sin(2 * math.pi * i / n))] for i in range(n)]


def strip(path, hw):
    left, right = [], []
    for i, p in enumerate(path):
        a, b = path[max(0, i - 1)], path[min(len(path) - 1, i + 1)]
        dx, dz = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(dx, dz) or 1
        nx, nz = -dz / ln * hw, dx / ln * hw
        left.append([rnd(p[0] + nx), rnd(p[1] + nz)])
        right.append([rnd(p[0] - nx), rnd(p[1] - nz)])
    return left + right[::-1]


def pad_rect(lb):
    """The pad's footprint (size w x d, yaw clockwise from north) grown by MARGIN, as [x, z] corners."""
    (cx, cz), (w, d) = lb['at'], lb['size']
    a = math.radians(lb.get('yaw', 0.0))
    hw, hd = w / 2 + MARGIN, d / 2 + MARGIN
    out = []
    for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        x, z = sx * hw, sz * hd
        out.append([rnd(cx + x * math.cos(a) + z * math.sin(a)), rnd(cz - x * math.sin(a) + z * math.cos(a))])
    return out


def inside(pt, poly):
    """Even-odd point in polygon ([x, z] points)."""
    x, z = pt
    c = False
    for i in range(len(poly)):
        x1, z1 = poly[i]
        x2, z2 = poly[(i + 1) % len(poly)]
        if (z1 > z) != (z2 > z) and x < (x2 - x1) * (z - z1) / (z2 - z1) + x1:
            c = not c
    return c


def _num(o):
    if isinstance(o, float) and o.is_integer():
        return int(o)
    if isinstance(o, dict):
        return {k: _num(v) for k, v in o.items()}
    if isinstance(o, list):
        return [_num(v) for v in o]
    return o


def dumps(obj):
    """JSON as the repo's prettier writes it: 2-space indent, arrays of plain numbers on one line, whole numbers bare."""
    import re
    text = json.dumps(_num(obj), indent=2, ensure_ascii=False)
    return re.sub(r'\[\s*(-?[\d.]+(?:,\s*-?[\d.]+)*)\s*\]', lambda m: '[' + ', '.join(x.strip() for x in m.group(1).split(',')) + ']', text)


def main():
    plan = json.load(open(PLAN))
    island = [[[rnd(x), rnd(z)] for x, _, z in plan['coast']]]
    if plan.get('islet'):
        island.append(circle(plan['islet']['center'], plan['islet']['radius']))
    if plan.get('causeway'):
        island.append(strip(plan['causeway']['path'], plan['causeway']['half_width']))
    extra = [pad_rect(lb) for lb in L.LANDMARK_BUILDINGS if lb.get('fix_y') is not None]
    # venues standing on a deck over the water (an explicit deck height in layout.VENUES, e.g. the Prediction Pit on the
    # arcade pier): their pad, grown the same way, when its centre is off the coast outline
    for v in plan.get('venues', []):
        pad = v.get('pad')
        if pad and not inside([pad['cx'], pad['cz']], island[0]):
            extra.append(pad_rect(dict(at=(pad['cx'], pad['cz']), size=(pad['w'], pad['d']), yaw=pad.get('yaw', 0.0))))
    island += extra
    venues = {v['id']: dict(district=v['district'], origin=[rnd(c) for c in v['origin']], yaw=rnd(v['yaw']),
                            door=[rnd(v['door'][0]), rnd(v['door'][2])]) for v in plan.get('venues', [])}
    old = json.load(open(DEST)) if os.path.exists(DEST) else {}
    out = dict(_owner='FE', _provisional=False, version=old.get('version', '1.0.0'),
               _note=('CT Island v2 bounds for the street and vehicles rooms, generated from the frontend island plan '
                      '(tools/world/islandgen/out/plan.json, ct-life feat/v3-studio-world 2026-10-24) by '
                      'tools/world/islandgen/export_districts.py (the backend importer\'s format plus pads over the water). '
                      'Island metres, +X east, +Z north, origin at the downtown plaza. `island` is a list of land polygons '
                      '(coast, islet, causeway, then pier pads such as The Rug Room); `districts` maps each district id to its '
                      'polygon. `venues` places each venue\'s local frame on the island: origin [x, y, z], yaw in degrees '
                      'clockwise from north, door [x, z]. Points are [x, z].'),
               units='island metres', island=island,
               districts={d['id']: [[x, z] for x, z in d['poly']] for d in plan['districts']}, venues=venues)
    open(DEST, 'w').write(dumps(out) + '\n')
    print('districts.json:', len(island), 'land polygons (', len(extra), 'pier pads ),', len(out['districts']), 'districts,',
          len(venues), 'venues')


if __name__ == '__main__':
    main()
