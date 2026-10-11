"""Road network for CT Island v2: routes -> nodes and segments, elevation profiles, junction shapes, lanes.

Segment samples are the TRIMMED centreline (they stop where the junction surface begins). Lanes are offsets of
that centreline: forward lanes (from -> to) on the right, back lanes (to -> from) on the left, lane 0 innermost.
Lane index = 0..F-1 forward, F..F+B-1 back. One-way segments centre their lanes on the centreline.
"""
import math
from collections import defaultdict

import numpy as np
from scipy import ndimage
from scipy.spatial import cKDTree

import geom as G
import layout as L

SPACING = 2.0


class Seg:
    def __init__(self, sid, route, pts, frm, to):
        self.id, self.route, self.frm, self.to = sid, route, frm, to
        self.name, self.kind = route['name'], route['kind']
        self.prof = dict(L.PROFILES[self.kind])
        self.F, self.B = route['lanes']
        self.one_way = self.B == 0
        self.pts = pts                      # (n, 2) untrimmed dense centreline
        self.y = None
        self.bridge = self.tunnel = False
        self.trim = [0.0, 0.0]              # metres cut at the from / to ends
        self.ctrl = []
        self.underground = None             # per-sample bool (tunnels)
        self.guard = None                   # per-sample bit flags (1 right, 2 left)
        self.speed = self.prof['speed']
        self.recompute()

    def recompute(self):
        p = self.prof
        if self.one_way:
            half = self.F * L.LANE / 2.0
            self.cw_r = self.cw_l = half + p['parking'] + p['shoulder']
        else:
            self.cw_r = p['median'] / 2.0 + self.F * L.LANE + p['parking'] + p['shoulder']
            self.cw_l = p['median'] / 2.0 + self.B * L.LANE + p['parking'] + p['shoulder']
        self.sidewalk = p['sidewalk']

    @property
    def out_r(self):
        return self.cw_r + self.sidewalk

    @property
    def out_l(self):
        return self.cw_l + self.sidewalk

    def lane_offset(self, lane):
        """Signed offset to the RIGHT of the from->to centreline, and travel sign (+1 forward, -1 back)."""
        p = self.prof
        if self.one_way:
            return (lane + 0.5) * L.LANE - self.F * L.LANE / 2.0, 1
        if lane < self.F:
            return p['median'] / 2.0 + (lane + 0.5) * L.LANE, 1
        j = lane - self.F
        return -(p['median'] / 2.0 + (j + 0.5) * L.LANE), -1


def downtown_routes():
    """Grid streets from layout.DT_*: rows 1..6 (row 0 is Ocean Drive, its own route), columns from Ocean Drive north,
    and the diagonal boulevard pieces (they pass grid nodes only, so every crossing is a real junction)."""
    routes = []
    nx, nz = len(L.DT_U), len(L.DT_V)

    def runs_of(cells, removed):
        runs, run = [], [cells[0]]
        for k in range(len(cells) - 1):
            if removed(k):
                runs.append(run)
                run = [cells[k + 1]]
            else:
                run.append(cells[k + 1])
        runs.append(run)
        return [q for q in runs if len(q) >= 2]

    for r in range(1, nz):
        runs = runs_of(list(range(nx)), lambda c: ('h', c, r) in L.DT_REMOVED)
        for k, run in enumerate(runs):
            avenue = r == L.DT_AVENUE_ROW
            rid = 'genesis' if avenue else f'dt_h{r}' + ('' if len(runs) == 1 else 'abc'[k])
            ow = L.DT_ONE_WAY.get(('h', r))
            names = [L.dt_node(c, r) for c in run]
            routes.append(dict(id=rid, name=L.DT_ROW_NAMES[r], kind='avenue' if avenue else 'street',
                               lanes=(2, 2) if avenue else ((2, 0) if ow else (1, 1)), pts=names[::-1] if ow == -1 else names))
    for c in range(nx):
        runs = runs_of(list(range(nz)), lambda r: ('v', c, r) in L.DT_REMOVED)
        for k, run in enumerate(runs):
            avenue = c == L.DT_AVENUE_COL
            rid = 'timeline_ave' if avenue else f'dt_v{c}' + ('' if len(runs) == 1 else 'abc'[k])
            routes.append(dict(id=rid, name=L.DT_COL_NAMES[c], kind='avenue' if avenue else 'street',
                               lanes=(2, 2) if avenue else (1, 1), pts=[L.dt_node(c, r) for r in run]))
    for rid, names in L.DT_DIAGONAL:
        routes.append(dict(id=rid, name='Consensus Boulevard', kind='avenue', lanes=(2, 2), pts=list(names)))
    return routes


def downtown_nodes():
    return {L.dt_node(c, r): L.dt_pos(c, r) for c in range(len(L.DT_U)) for r in range(len(L.DT_V))}


def clean_polyline(p, min_step=0.5):
    """Drop near-duplicate and backtracking points (left by snapping nodes onto the dense spline), then resample at
    SPACING with both end points exact."""
    if len(p) < 3:
        return p
    keep = [p[0]]
    for q in p[1:-1]:
        if np.linalg.norm(q - keep[-1]) >= min_step:
            keep.append(q)
    if np.linalg.norm(p[-1] - keep[-1]) < min_step and len(keep) > 1:
        keep.pop()
    keep.append(p[-1])
    q = np.array(keep)
    # backtracking: a point whose step reverses the overall heading near the ends
    changed = True
    while changed and len(q) > 3:
        changed = False
        for i in (1, len(q) - 2):
            a, b, c = q[i - 1], q[i], q[i + 1]
            if np.dot(b - a, c - b) < 0:
                q = np.delete(q, i, axis=0)
                changed = True
                break
    return G.resample(q, SPACING)


class Network:
    def __init__(self):
        self.nodes = {k: np.array(v, dtype=np.float64) for k, v in L.NODES.items()}
        self.nodes.update({k: np.array(v, dtype=np.float64) for k, v in downtown_nodes().items()})
        self.node_y = {}
        self.node_kind = {}
        self.segs = []
        self.routes = {}
        self.route_poly = {}
        self.junctions = []
        self.lanes_next = {}
        self.ring_nodes = {}

    # ------------------------------------------------------------------ topology
    def build_routes(self, nat=None, sample=None):
        all_routes = list(L.ROUTES) + downtown_routes()
        used = set()
        for r in all_routes:
            self.routes[r['id']] = r
            if 'switchback' in r:
                r['pts'] = self._switchback(r, nat, sample)
            items = list(r['pts'])
            # open ends that are plain points become dead-end nodes
            for k, tag in ((0, 'a'), (len(items) - 1, 'b')):
                if not isinstance(items[k], str) and not r.get('closed'):
                    nid = f'{r["id"]}_{tag}'
                    self.nodes[nid] = np.array(items[k], dtype=np.float64)
                    items[k] = nid
            r['pts'] = items
            if r.get('closed'):
                items = items + [items[0]]
            ctrl, names = [], []
            for it in items:
                if isinstance(it, str):
                    ctrl.append(self.nodes[it])
                    names.append(it)
                    used.add(it)
                else:
                    ctrl.append(np.array(it, dtype=np.float64))
                    names.append(None)
            ctrl = np.array(ctrl)
            dense = G.catmull_rom(ctrl, spacing=SPACING)
            self.route_poly[r['id']] = dense
            tree = cKDTree(dense)
            # node positions along the dense polyline (monotonic search so loops resolve correctly)
            idx = []
            lo = 0
            for k, nm in enumerate(names):
                if nm is None:
                    continue
                d, cand = tree.query(ctrl[k], k=min(8, len(dense)))
                cand = sorted(int(c) for c, dd in zip(np.atleast_1d(cand), np.atleast_1d(d)) if dd < 3.0 and c >= lo)
                j = cand[0] if cand else lo
                if k == len(names) - 1:
                    j = len(dense) - 1
                if k == 0:
                    j = 0
                dense[j] = ctrl[k]
                idx.append((j, nm))
                lo = j + 1
            ctrl_lists = []
            for a in range(len(idx) - 1):
                (j0, n0), (j1, n1) = idx[a], idx[a + 1]
                pts = clean_polyline(dense[j0:j1 + 1].copy())
                s = Seg(f'{r["id"]}:{len(self.segs)}', r, pts, n0, n1)
                s.ctrl = [tuple(map(float, self.nodes[n0]))] + [tuple(map(float, c)) for c, nm in zip(ctrl, names)
                          if nm is None and self._between(c, dense, j0, j1, tree)] + [tuple(map(float, self.nodes[n1]))]
                s.bridge = (n0, n1) in set(map(tuple, r.get('bridges', []))) or (n1, n0) in set(map(tuple, r.get('bridges', [])))
                s.tunnel = (n0, n1) in set(map(tuple, r.get('tunnels', []))) or (n1, n0) in set(map(tuple, r.get('tunnels', [])))
                self.segs.append(s)
        for i, s in enumerate(self.segs):
            s.id = f's{i:03d}'
        unused = set(self.nodes) - used
        self.unused_nodes = sorted(u for u in unused if not (u.startswith('D') and u[1:].isdigit()))
        for u in unused:
            del self.nodes[u]

    def _switchback(self, r, nat, sample):
        """Contour-following hairpin road: each leg follows the terrain contour at the road's own (rising) height,
        legs at least `gap` metres apart, semicircular hairpins at the corridor ends. Creates the end node."""
        sb = r['switchback']
        x, z = map(float, self.nodes[sb['start']])
        y = float(sample(nat, np.array([x]), np.array([z]))[0])
        g, R, step, gap = sb['grade'], sb['radius'], 5.0, 2.0 * sb['radius'] - 2.0
        direction = 1.0 if sb['first'] == 'east' else -1.0
        pts = [(x, z)]
        prev_leg = None
        for leg in range(sb['legs']):
            xt = sb['east'] if direction > 0 else sb['west']
            if leg == sb['legs'] - 1 and sb.get('end_x') is not None:
                xt = sb['end_x']
            this_leg = []
            while (xt - x) * direction > step * 0.5:
                x2 = x + step * direction
                y += g * step
                cand = np.arange(z - 6.0, z + 26.0, 0.5)
                hv = sample(nat, np.full(len(cand), x2), cand)
                j = int(np.argmin(np.abs(hv - y)))
                z2 = float(np.clip(cand[j], z - 2.2, z + 2.2))
                if prev_leg is not None:
                    order = np.argsort(prev_leg[:, 0])
                    floor = float(np.interp(x2, prev_leg[order, 0], prev_leg[order, 1])) + gap
                    z2 = max(z2, floor)
                x, z = x2, z2
                pts.append((x, z))
                this_leg.append((x, z))
            prev_leg = np.array(this_leg) if this_leg else prev_leg
            if leg == sb['legs'] - 1:
                break
            cz = z + R
            for k in range(1, 9):
                a = -math.pi / 2 + math.pi * k / 8
                pts.append((x + direction * R * math.cos(a), cz + R * math.sin(a)))
            y += g * 0.6 * math.pi * R
            x, z = pts[-1]
            direction = -direction
        arr = np.array(pts)
        # gentle smoothing of the legs (hairpins keep their shape)
        sm = arr.copy()
        sm[:, 1] = ndimage.uniform_filter1d(arr[:, 1], 5, mode='nearest')
        sm[0], sm[-1] = arr[0], arr[-1]
        self.nodes[sb['end']] = sm[-1].copy()
        self.designed = getattr(self, 'designed', {})
        self.designed[r['id']] = g
        return [sb['start']] + [tuple(p) for p in sm[1:-1]] + [sb['end']]

    @staticmethod
    def _between(c, dense, j0, j1, tree):
        d, j = tree.query(c)
        return d < 4.0 and j0 < j < j1

    def incident(self):
        inc = defaultdict(list)
        for s in self.segs:
            inc[s.frm].append((s, 'from'))
            inc[s.to].append((s, 'to'))
        return inc

    # ------------------------------------------------------------------ roundabouts
    def make_roundabouts(self):
        """Replace each roundabout node by ring nodes on a circle and one-way arcs between them (counter-clockwise)."""
        for nid, R in L.ROUNDABOUTS.items():
            centre = self.nodes[nid]
            inc = self.incident()[nid]
            entries = []
            for s, end in inc:
                pts = s.pts if end == 'from' else s.pts[::-1]
                d = np.linalg.norm(pts - centre, axis=1)
                k = int(np.argmax(d >= R)) if np.any(d >= R) else len(pts) - 1
                ang = math.atan2(pts[k, 1] - centre[1], pts[k, 0] - centre[0])
                ring_id = f'{nid}_r{len(entries)}'
                ring_pt = centre + R * np.array([math.cos(ang), math.sin(ang)])
                self.nodes[ring_id] = ring_pt
                newpts = np.vstack([ring_pt[None, :], pts[k:]])
                newpts = G.resample(newpts, SPACING)
                if end == 'from':
                    s.pts, s.frm = newpts, ring_id
                else:
                    s.pts, s.to = newpts[::-1], ring_id
                entries.append((ang, ring_id))
            entries.sort()
            self.ring_nodes[nid] = [e[1] for e in entries]
            ring_route = dict(id=f'{nid}_ring', name='Roundabout', kind='street', lanes=(2 if R >= 19 else 1, 0))
            self.routes[ring_route['id']] = ring_route
            n = len(entries)
            for a in range(n):
                a0, n0 = entries[a]
                a1, n1 = entries[(a + 1) % n]
                if a1 <= a0:
                    a1 += 2 * math.pi
                m = max(4, int((a1 - a0) * R / 1.5))
                t = np.linspace(a0, a1, m)
                arc = centre + R * np.stack([np.cos(t), np.sin(t)], axis=1)
                seg = Seg(f'ring{len(self.segs)}', ring_route, G.resample(arc, SPACING), n0, n1)
                seg.prof.update(sidewalk=0.0, parking=0.0, shoulder=0.6, markings='none', speed=8.3)
                seg.speed = 8.3
                seg.recompute()
                seg.ctrl = [tuple(map(float, x)) for x in arc[:: max(1, m // 4)]] + [tuple(map(float, arc[-1]))]
                seg.ring = nid
                self.segs.append(seg)
            del self.nodes[nid]
        for i, s in enumerate(self.segs):
            s.id = f's{i:03d}'

    # ------------------------------------------------------------------ elevation
    def solve_heights(self, nat, sample, river):
        line, level, hw, depth = river
        rtree = cKDTree(line)
        inc = self.incident()
        for nid, p in self.nodes.items():
            self.node_y[nid] = float(sample(nat, np.array([p[0]]), np.array([p[1]]))[0])
        # Roundabout ring nodes share the centre height.
        for centre, ring in self.ring_nodes.items():
            c = np.mean([self.nodes[r] for r in ring], axis=0)
            y = float(sample(nat, np.array([c[0]]), np.array([c[1]]))[0])
            for r in ring:
                self.node_y[r] = y
        # Bridge ends clear the water underneath (harbour: tall masts are not modelled; 6 m deck clearance).
        for s in self.segs:
            if not s.bridge:
                continue
            mid = s.pts[len(s.pts) // 2]
            d, j = rtree.query(mid)
            water = float(level[j]) if d < hw[j] + 30 else L.SEA
            clear = 6.0 if water <= L.SEA + 0.1 else 3.6
            for n in (s.frm, s.to):
                self.node_y[n] = max(self.node_y[n], water + clear)
        for rid, g in getattr(self, 'designed', {}).items():
            segs = [s for s in self.segs if s.route['id'] == rid]
            for s in segs:
                length = float(G.arc_lengths(s.pts)[-1])
                self.node_y[s.to] = self.node_y[s.frm] + g * length
        self._relax_nodes()
        for s in self.segs:
            self._profile(s, nat, sample)
        return self

    def _relax_nodes(self, iters=600):
        """Make every segment's end heights reachable within its grade limit (joint, weighted adjustment).

        Downtown grid nodes are heavy (the grid stays level); tunnel, ring and free nodes move first; bridge ends keep
        their clearance as a lower bound.
        """
        lb = {}
        for s in self.segs:
            if s.bridge:
                lb[s.frm] = self.node_y[s.frm]
                lb[s.to] = self.node_y[s.to]
        def weight(n):
            if n.startswith('D') and n[1:].isdigit():
                return 8.0
            if n in lb:
                return 3.0
            return 1.0
        ring_of = {r: c for c, rs in self.ring_nodes.items() for r in rs}
        for it in range(iters):
            worst = 0.0
            for s in self.segs:
                length = float(G.arc_lengths(s.pts)[-1])
                lim = L.MAX_GRADE[s.kind] * 0.94 * length
                a, b = s.frm, s.to
                d = self.node_y[b] - self.node_y[a]
                if abs(d) <= lim:
                    continue
                ex = abs(d) - lim
                worst = max(worst, ex)
                wa, wb = weight(a), weight(b)
                sgn = 1.0 if d > 0 else -1.0
                self.node_y[a] += sgn * ex * wb / (wa + wb) * 0.7
                self.node_y[b] -= sgn * ex * wa / (wa + wb) * 0.7
            for n, v in lb.items():
                self.node_y[n] = max(self.node_y[n], v)
            for c, rs in self.ring_nodes.items():
                m = float(np.mean([self.node_y[r] for r in rs]))
                for r in rs:
                    self.node_y[r] = m
            if worst < 0.05:
                break
        self.relax_iterations = it + 1

    def _profile(self, s, nat, sample):
        pts = s.pts
        n = len(pts)
        sl = G.arc_lengths(pts)
        total = sl[-1]
        y0, y1 = self.node_y[s.frm], self.node_y[s.to]
        base = y0 + (y1 - y0) * (sl / max(total, 1e-6))
        if s.bridge:
            arch = (1.6 if total > 80 else 0.6) * np.sin(np.pi * sl / max(total, 1e-6))
            s.y = base + arch
            return
        if s.tunnel:
            s.y = base.copy()
            raw = sample(nat, pts[:, 0], pts[:, 1])
            s.underground = (raw - s.y) > 8.5
            return
        if s.route['id'] in getattr(self, 'designed', {}):
            s.y = base.copy()
            s.max_grade = float(np.max(np.abs(np.diff(s.y)) / np.maximum(np.diff(sl), 1e-6)))
            return
        raw = sample(nat, pts[:, 0], pts[:, 1])
        dev = raw - base
        sigma = L.SMOOTH[s.kind] / SPACING
        dev_s = ndimage.gaussian_filter1d(dev, sigma, mode='nearest') if n > 3 else dev
        taper = min(30.0, total / 3.0)
        w = G.smoothstep(0.0, taper, sl) * G.smoothstep(0.0, taper, total - sl)
        y = base + dev_s * w
        g = L.MAX_GRADE[s.kind]
        ds = np.diff(sl)
        for _ in range(6):
            y[0], y[-1] = y0, y1
            for k in range(1, n):
                y[k] = min(max(y[k], y[k - 1] - g * ds[k - 1]), y[k - 1] + g * ds[k - 1])
            y[-1] = y1
            for k in range(n - 2, -1, -1):
                y[k] = min(max(y[k], y[k + 1] - g * ds[k]), y[k + 1] + g * ds[k])
        y[0], y[-1] = y0, y1
        s.y = y
        grades = np.abs(np.diff(y)) / np.maximum(ds, 1e-6)
        s.max_grade = float(grades.max()) if len(grades) else 0.0

    # ------------------------------------------------------------------ junctions
    def build_junctions(self):
        inc = self.incident()
        self.junctions = []
        for nid, ends in inc.items():
            deg = len(ends)
            p = self.nodes[nid]
            appr = []
            for s, end in ends:
                pts = s.pts if end == 'from' else s.pts[::-1]
                ys = s.y if end == 'from' else s.y[::-1]
                k = min(len(pts) - 1, 4)
                d = pts[k] - pts[0]
                d = d / max(np.linalg.norm(d), 1e-9)
                # edges relative to the outward direction
                if end == 'from':
                    r_w, l_w = s.cw_r, s.cw_l
                    r_sw, l_sw = s.sidewalk, s.sidewalk
                else:
                    r_w, l_w = s.cw_l, s.cw_r
                    r_sw, l_sw = s.sidewalk, s.sidewalk
                appr.append(dict(seg=s, end=end, dir=d, rw=r_w, lw=l_w, rsw=r_sw, lsw=l_sw, pts=pts, ys=ys,
                                 ang=math.atan2(d[1], d[0])))
            if deg == 2:
                a, b = appr
                straight = np.dot(a['dir'], -b['dir'])
                same = abs(a['rw'] - b['lw']) < 0.3 and abs(a['lw'] - b['rw']) < 0.3 and a['seg'].sidewalk == b['seg'].sidewalk
                if straight > math.cos(math.radians(28)) and same:
                    self.node_kind[nid] = 'continuation'
                    continue
            if deg == 1:
                self.node_kind[nid] = 'end'
                continue
            appr.sort(key=lambda a: a['ang'])
            n = len(appr)
            radius = lambda a: {'highway': 11.0, 'avenue': 7.5, 'mountain': 6.0}.get(a['seg'].kind, 5.0)
            corners = []
            for i in range(n):
                a, b = appr[i], appr[(i + 1) % n]
                rn_a = G.right_normals(a['dir'][None])[0]
                ln_b = -G.right_normals(b['dir'][None])[0]
                pa = p + (-rn_a) * a['lw']            # a's left edge (counter-clockwise side)
                pb = p + (-ln_b) * b['rw']            # b's right edge (clockwise side)
                hit = G.seg_intersect(pa, a['dir'], pb, b['dir']) if n > 1 else None
                corners.append(None if hit is None or hit[0] < -2 or hit[1] < -2 else (pa + a['dir'] * hit[0], hit[0], hit[1]))
            setback = []
            for i in range(n):
                a = appr[i]
                need = []
                c_prev, c_next = corners[(i - 1) % n], corners[i]
                if c_prev is not None:
                    need.append(c_prev[2])
                if c_next is not None:
                    need.append(c_next[1])
                sb = (max(need) if need else max(a['rw'], a['lw'])) + radius(a)
                sb = float(np.clip(sb, max(a['rw'], a['lw']) * 0.6, 42.0))
                setback.append(sb)
            # Polygon: for each approach its right then left trimmed edge, fillets between neighbours.
            poly = []
            fillet = []
            approaches = []
            for i in range(n):
                a = appr[i]
                sb = setback[i]
                sl = G.arc_lengths(a['pts'])
                cut = np.array([np.interp(sb, sl, a['pts'][:, 0]), np.interp(sb, sl, a['pts'][:, 1])])
                ycut = float(np.interp(sb, sl, a['ys']))
                k = int(np.searchsorted(sl, sb))
                k = min(max(k, 1), len(sl) - 1)
                d = a['pts'][k] - a['pts'][k - 1]
                d = d / max(np.linalg.norm(d), 1e-9)
                rn = G.right_normals(d[None])[0]
                R_pt = cut + rn * a['rw']
                L_pt = cut - rn * a['lw']
                a['cut'], a['ycut'], a['R'], a['L'], a['cutdir'] = cut, ycut, R_pt, L_pt, d
                # record trim on the segment
                if a['end'] == 'from':
                    a['seg'].trim[0] = sb
                else:
                    a['seg'].trim[1] = sb
                approaches.append(dict(segment=a['seg'].id, end=a['end'], cut=[float(cut[0]), ycut, float(cut[1])],
                                       dir=[float(d[0]), float(d[1])], right=[float(R_pt[0]), ycut, float(R_pt[1])],
                                       left=[float(L_pt[0]), ycut, float(L_pt[1])], sidewalk=float(a['seg'].sidewalk)))
            for i in range(n):
                a, b = appr[i], appr[(i + 1) % n]
                poly.append((a['R'], a['ycut']))
                poly.append((a['L'], a['ycut']))
                c = corners[i]
                ctl = c[0] if c is not None else (a['L'] + b['R']) / 2.0
                curve = G.bezier2(a['L'], ctl, b['R'], 7)[1:-1] if c is not None else np.zeros((0, 2))
                ys = np.linspace(a['ycut'], b['ycut'], len(curve) + 2)[1:-1]
                for q, yq in zip(curve, ys):
                    poly.append((q, float(yq)))
                fillet.append(dict(a=i, b=(i + 1) % n, points=[[float(q[0]), float(yq), float(q[1])] for q, yq in
                                   zip(np.vstack([a['L'][None], curve, b['R'][None]]), np.linspace(a['ycut'], b['ycut'], len(curve) + 2))],
                                   sidewalk=float(min(a['seg'].sidewalk, b['seg'].sidewalk)) if a['seg'].sidewalk and b['seg'].sidewalk else 0.0,
                                   straight=c is None))
            ring = next((c for c, r in self.ring_nodes.items() if nid in r), None)
            kind = 'roundabout' if ring else ('signal' if nid in L.SIGNALS else ('merge' if deg == 2 else 'stop'))
            self.node_kind[nid] = kind
            self.junctions.append(dict(id=f'j_{nid}', node=nid, kind=kind, ring=ring, center=[float(p[0]), self.node_y[nid], float(p[1])],
                                       polygon=[[float(q[0]), float(yq), float(q[1])] for q, yq in poly],
                                       approaches=approaches, fillets=fillet))
        # group roundabouts as one intersection each (contract: intersections {id, nodeIds, kind})
        return self

    def trimmed(self, s):
        """Trimmed centreline (x, z) and heights for a segment, resampled every SPACING metres by arc length."""
        sl = G.arc_lengths(s.pts)
        a, b = s.trim[0], sl[-1] - s.trim[1]
        if b - a < 1.0:
            mid = (a + b) / 2
            a, b = mid - 0.5, mid + 0.5
        n2 = max(2, int(round((b - a) / SPACING)) + 1)
        t = np.linspace(a, b, n2)
        xs, zs = np.interp(t, sl, s.pts[:, 0]), np.interp(t, sl, s.pts[:, 1])
        ys = np.interp(t, sl, s.y)
        ug = (np.interp(t, sl, s.underground.astype(float)) > 0.5) if s.underground is not None else None
        return np.stack([xs, zs], axis=1), ys, ug

    # ------------------------------------------------------------------ lanes
    def lane_graph(self):
        """next[(seg, lane)] = [(seg, lane), ...] by turn direction rules."""
        inc = self.incident()
        nxt = {}
        out_at = defaultdict(list)   # node -> lanes starting there: (seg, lane, dir_vec)
        in_at = defaultdict(list)    # node -> lanes ending there
        for s in self.segs:
            pts, _, _ = self.trimmed(s)
            d_start = pts[min(3, len(pts) - 1)] - pts[0]
            d_end = pts[-1] - pts[max(0, len(pts) - 4)]
            for lane in range(s.F + s.B):
                off, sign = s.lane_offset(lane)
                if sign > 0:
                    out_at[s.frm].append((s, lane, d_start / max(np.linalg.norm(d_start), 1e-9)))
                    in_at[s.to].append((s, lane, d_end / max(np.linalg.norm(d_end), 1e-9)))
                else:
                    out_at[s.to].append((s, lane, -d_end / max(np.linalg.norm(d_end), 1e-9)))
                    in_at[s.frm].append((s, lane, -d_start / max(np.linalg.norm(d_start), 1e-9)))
        for node, ins in in_at.items():
            outs = out_at.get(node, [])
            for (s, lane, dv) in ins:
                cands = [(t, tl, tv) for (t, tl, tv) in outs if t is not s]
                if not cands:
                    cands = [(t, tl, tv) for (t, tl, tv) in outs]          # dead end: U-turn
                in_count = s.F if s.lane_offset(lane)[1] > 0 else s.B
                in_rank = lane if lane < s.F else lane - s.F               # 0 = leftmost (innermost) in travel direction
                res = []
                by_seg = defaultdict(list)
                for (t, tl, tv) in cands:
                    by_seg[t.id].append((t, tl, tv))
                for tid, lanes in by_seg.items():
                    t, _, tv = lanes[0]
                    cross = dv[0] * tv[1] - dv[1] * tv[0]                 # > 0: turning left (counter-clockwise)
                    dot = float(np.dot(dv, tv))
                    turn = 'straight' if dot > 0.7 else ('left' if cross > 0 else 'right')
                    out_count = len(lanes)
                    ranks = sorted(lanes, key=lambda q: q[1] if q[1] < q[0].F else q[1] - q[0].F)
                    if in_count <= 1:
                        allowed = True
                    elif turn == 'right':
                        allowed = in_rank == in_count - 1
                    elif turn == 'left':
                        allowed = in_rank == 0
                    else:
                        allowed = True
                    if not allowed:
                        continue
                    if turn == 'right':
                        pick = ranks[-1]
                    elif turn == 'left':
                        pick = ranks[0]
                    else:
                        pick = ranks[min(in_rank, out_count - 1)]
                    res.append((pick[0].id, int(pick[1])))
                if not res and cands:
                    t, tl, _ = cands[0]
                    res.append((t.id, int(tl)))
                nxt[(s.id, lane)] = res
        self.lanes_next = nxt
        return nxt

    def lane_points(self, s, lane):
        pts, ys, _ = self.trimmed(s)
        off, sign = s.lane_offset(lane)
        tan = G.tangents(pts)
        rn = G.right_normals(tan)
        q = pts + rn * off
        out = np.stack([q[:, 0], ys + 0.0, q[:, 1]], axis=1)
        return out if sign > 0 else out[::-1]

    # ------------------------------------------------------------------ helpers
    def carve_points(self):
        """All untrimmed samples with half-widths for terrain carving (bridges and tunnel interiors excluded)."""
        xs, zs, ys, hws, carve = [], [], [], [], []
        for s in self.segs:
            if s.y is None:
                continue
            c = np.ones(len(s.pts), bool)
            if s.bridge:
                c[:] = False
                c[:3] = c[-3:] = True
            if s.underground is not None:
                c &= ~s.underground
            xs.append(s.pts[:, 0]); zs.append(s.pts[:, 1]); ys.append(s.y)
            hws.append(np.full(len(s.pts), max(s.out_r, s.out_l))); carve.append(c)
        return dict(x=np.concatenate(xs), z=np.concatenate(zs), y=np.concatenate(ys), hw=np.concatenate(hws), carve=np.concatenate(carve))

    def nearest_on_route(self, route_id, near):
        poly = self.route_poly[route_id]
        d = np.linalg.norm(poly - np.asarray(near, float), axis=1)
        k = int(np.argmin(d))
        k0, k1 = max(0, k - 2), min(len(poly) - 1, k + 2)
        t = poly[k1] - poly[k0]
        t = t / max(np.linalg.norm(t), 1e-9)
        # the segment that covers it (for its width and height)
        best, bd = None, 1e9
        for s in self.segs:
            if s.route['id'] != route_id or s.y is None:
                continue
            dd = np.linalg.norm(s.pts - poly[k], axis=1)
            j = int(np.argmin(dd))
            if dd[j] < bd:
                bd, best = dd[j], (s, j)
        return poly[k], t, best
