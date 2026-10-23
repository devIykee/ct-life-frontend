"""World fix-ups (2026-10-23): real architecture for four venue shells that were flat toy boxes (IslandV2Shells' procedural
walls on the facade shader). Built here from clean modular pieces (walls, piers, glass, mullions, transoms, fins, slabs,
canopies, posts, rafters, roof planes) and written as plain mesh data for the island builder:

  conference_hall   CHECKPOINT 26 convention centre (Breakpoint Park): a glass-and-steel hall, concrete piers, a deep
                    cantilevered canopy on slender columns over the entrance plaza, a white fascia, ribbon windows
  perp_pit          the Perp Pit (The Trenches): a two-storey glass trading floor, dark steel frames and deep fins, a
                    floor-edge band, a crown with a green ticker strip, a projecting steel entrance portal
  hackathon_house   the Basecamp Lodge (MtnDAO): a timber A-frame hall with a glazed gable on heavy timber beams,
                    two low board-clad wings with shed roofs, natural slate roofs, a stone plinth and chimney
  prediction_pit    the boardwalk beach hut: a pine-board pavilion on a raised deck, a deep reed-thatch hip roof on
                    timber posts, wide shuttered serving windows, a roof-edge signboard

Every venue keeps the shell's footprint (the walls stand on the IslandV2Shells footprint: x0..x1, z0 (the front, with the
door) .. z1, in the venue's local frame: x right seen from the street, y up, z into the building), its door opening (the
front's centre, the same width and height as the shell's) and its height for the fader. Parts are split by side (front,
back, left, right, roof) so VenueShell can dissolve the camera-side walls and the roof while the player is inside.

Materials are named slots the builder maps to CT Life/World/Kit PBR materials with CC0 Poly Haven scans
(art/pipeline/world_venue_textures.py) or flat PBR colours (glass, steel, aluminium, accent). UVs are planar per face in
metres (the material's tiling sets the scan's size).

Output: art/processed/world_v3/v3_venues/<venue>.json  {"parts": {side: {"v": [...], "n": [...], "uv": [...],
"sub": {material: [triangle indices]}}}, "sign": {...}} in Unity's left-handed frame (y up).
Run: art/pipeline/blender.sh art/pipeline/world_venue_shells.py [-- venue ...] [--sheet <png>]
"""
import json, math, os, sys
import bpy, bmesh
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, '..', 'processed', 'world_v3', 'v3_venues'))
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
MATS = ['glass', 'steel', 'alu', 'concrete', 'accent', 'timber_dark', 'timber_light', 'wood_post', 'slate', 'thatch', 'stone', 'inner', 'paving']


def U2B(p):
    """Unity local (x, y up, z depth) -> Blender (x, y = depth, z = up)."""
    return Vector((p[0], p[2], p[1]))


class Part:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()

    def _face(self, vs, mat, away=None):
        """A face from venue-frame points; away: a venue-frame point the face must face away from (else the order given
        is taken as counter-clockwise seen from outside in Blender's frame)."""
        bv = [U2B(v) for v in vs]
        if away is not None:
            c = sum(bv, Vector()) / len(bv)
            n = (bv[1] - bv[0]).cross(bv[2] - bv[0])
            if n.length < 1e-9 and len(bv) > 3:
                n = (bv[2] - bv[0]).cross(bv[3] - bv[0])
            if n.dot(c - U2B(away)) < 0:
                bv = bv[::-1]
        f = self.bm.faces.new([self.bm.verts.new(v) for v in bv])
        f.material_index = MATS.index(mat)
        return f

    def box(self, x0, x1, y0, y1, z0, z1, mat, skip=()):
        """An axis-aligned box in the venue frame (x, y up, z depth); skip: faces left out ('-x', '+x', '-y', '+y', '-z', '+z')."""
        x0, x1 = min(x0, x1), max(x0, x1); y0, y1 = min(y0, y1), max(y0, y1); z0, z1 = min(z0, z1), max(z0, z1)
        if x1 - x0 < 1e-4 or y1 - y0 < 1e-4 or z1 - z0 < 1e-4:
            return
        c = ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)
        faces = {
            '-z': [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)],
            '+z': [(x1, y0, z1), (x0, y0, z1), (x0, y1, z1), (x1, y1, z1)],
            '-x': [(x0, y0, z1), (x0, y0, z0), (x0, y1, z0), (x0, y1, z1)],
            '+x': [(x1, y0, z0), (x1, y0, z1), (x1, y1, z1), (x1, y1, z0)],
            '-y': [(x0, y0, z1), (x1, y0, z1), (x1, y0, z0), (x0, y0, z0)],
            '+y': [(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)],
        }
        for k, vs in faces.items():
            if k not in skip:
                self._face(vs, mat, away=c)

    def prism(self, pts, depth, mat, push=(0, -1, 0)):
        """A slab: the planar polygon pts (venue frame) is one face, extruded depth m along its normal on the push side."""
        a = [Vector(p) for p in pts]
        n = Vector((0, 0, 0))
        for i in range(1, len(a) - 1):
            n += (a[i] - a[0]).cross(a[i + 1] - a[0])
        n.normalize()
        if n.dot(Vector(push)) < 0:
            n = -n
        b = [p + n * depth for p in a]
        c = tuple(sum(a + b, Vector()) / (2 * len(a)))
        self._face([tuple(p) for p in a], mat, away=c)
        self._face([tuple(p) for p in b], mat, away=c)
        m = len(a)
        for i in range(m):
            j = (i + 1) % m
            self._face([tuple(a[i]), tuple(a[j]), tuple(b[j]), tuple(b[i])], mat, away=c)

    def beam(self, p, q, w, h, mat, up=(0, 1, 0)):
        """A square-section member from p to q (venue frame), w wide and h deep."""
        p, q = Vector(p), Vector(q)
        d = (q - p).normalized()
        u = Vector(up)
        if abs(d.dot(u)) > 0.95:
            u = Vector((0, 0, 1))
        s_ = d.cross(u).normalized() * (w / 2)
        t = s_.cross(d).normalized() * (h / 2)
        c4 = [p - s_ - t, p + s_ - t, p + s_ + t, p - s_ + t]
        e = [v + (q - p) for v in c4]
        c = tuple((p + q) / 2)
        for qd in ((c4[0], c4[1], e[1], e[0]), (c4[1], c4[2], e[2], e[1]), (c4[2], c4[3], e[3], e[2]), (c4[3], c4[0], e[0], e[3]),
                   (c4[3], c4[2], c4[1], c4[0]), (e[0], e[1], e[2], e[3])):
            self._face([tuple(v) for v in qd], mat, away=c)

    def box_door(self, x0, x1, y0, y1, z0, z1, mat, dw):
        """box() with the door gap (|x| < dw / 2) left open."""
        self.box(x0, -dw / 2, y0, y1, z0, z1, mat)
        self.box(dw / 2, x1, y0, y1, z0, z1, mat)


def export_part(part):
    bm = part.bm
    bmesh.ops.triangulate(bm, faces=bm.faces)
    V, N, UV, sub = [], [], [], {}
    for f in bm.faces:
        f.normal_update()
        n = f.normal.copy()
        if n.length < 0.5:
            continue
        up = Vector((0, 0, 1))
        t = up.cross(n)
        if t.length < 0.3:
            t = Vector((1, 0, 0))
        t.normalize()
        b = n.cross(t).normalized()
        if b.z < 0 or (abs(b.z) < 1e-6 and b.y < 0):
            b = -b
        base = len(V)
        for lv in f.verts:
            co = lv.co
            V.append((round(co.x, 4), round(co.z, 4), round(co.y, 4)))            # Blender (x, depth, up) -> Unity (x, up, depth)
            N.append((round(n.x, 4), round(n.z, 4), round(n.y, 4)))
            UV.append((round(co.dot(t), 4), round(co.dot(b), 4)))
        mat = MATS[f.material_index]
        # the axis swap mirrors the frame: reverse the winding so Unity's (clockwise) front faces are the outsides
        sub.setdefault(mat, []).extend([base, base + 2, base + 1])
    return dict(v=V, n=N, uv=UV, sub=sub)


def mullion_wall(P, x0, x1, y0, y1, z, pitch, mat_glass='glass', mat_frame='steel', depth=0.22, fw=0.08, door=None,
                 transoms=(), pane_inset=0.12, frame_out=0.04):
    """A curtain wall in the plane z (facing -z): glass panes inset pane_inset behind the frame face, vertical mullions
    every pitch m and horizontal transoms; door = (dx0, dx1, dy1): an opening at the bottom, framed."""
    n = max(1, int(round((x1 - x0) / pitch)))
    xs = [x0 + (x1 - x0) * i / n for i in range(n + 1)]
    gz0, gz1 = z + pane_inset, z + pane_inset + 0.04
    # glass (split round the door)
    if door:
        dx0, dx1, dy1 = door
        P.box(x0, dx0, y0, y1, gz0, gz1, mat_glass)
        P.box(dx1, x1, y0, y1, gz0, gz1, mat_glass)
        P.box(dx0, dx1, dy1, y1, gz0, gz1, mat_glass)
    else:
        P.box(x0, x1, y0, y1, gz0, gz1, mat_glass)
    fz0, fz1 = z - frame_out, z + depth
    for x in xs:
        if door and door[0] - fw < x < door[1] + fw:
            P.box(x - fw / 2, x + fw / 2, door[2], y1, fz0, fz1, mat_frame)
            continue
        P.box(x - fw / 2, x + fw / 2, y0, y1, fz0, fz1, mat_frame)
    for y in (y0,) + tuple(transoms) + (y1,):
        if door and y < door[2] + 0.01:
            P.box(x0, door[0], y - fw * 0.75, y + fw * 0.75, fz0, fz1, mat_frame)
            P.box(door[1], x1, y - fw * 0.75, y + fw * 0.75, fz0, fz1, mat_frame)
        else:
            P.box(x0, x1, y - fw * 0.75, y + fw * 0.75, fz0, fz1, mat_frame)
    if door:
        dx0, dx1, dy1 = door
        for x in (dx0, dx1):
            P.box(x - 0.09, x + 0.09, y0 - 0.6, dy1, fz0 - 0.02, fz1, mat_frame)
        P.box(dx0 - 0.09, dx1 + 0.09, dy1 - 0.09, dy1 + 0.12, fz0 - 0.02, fz1, mat_frame)


# ---------------------------------------------------------------------------------------------------- the venues
def frame(v):
    """The shell footprint and door, exactly as IslandV2Shells computes them."""
    mn, sz, walk = v['frame_min'], v['frame_size'], v['walk']
    x0, x1 = mn[0] - 0.3, mn[0] + sz[0] + 0.3
    z0, z1 = walk[1] - 0.7, mn[1] + sz[1] + 0.3
    h = v['shell']['h']
    dw = min(5.2, (x1 - x0) * 0.4)
    dh = min(4.4, h - 1.2)
    return x0, x1, z0, z1, h, dw, dh


def conference_hall(v):
    x0, x1, z0, z1, h, dw, dh = frame(v)
    F, B, L, R, T = (Part(n) for n in ('front', 'back', 'left', 'right', 'roof'))
    base = -0.7
    pier = 3.4
    gx0, gx1 = x0 + pier, x1 - pier
    top = h - 1.6                     # glass top; the fascia above
    # front: concrete piers at the ends, the curtain wall between, a white fascia band that becomes the canopy
    F.box(x0, gx0, base, h, z0, z0 + 0.7, 'concrete')
    F.box(gx1, x1, base, h, z0, z0 + 0.7, 'concrete')
    F.box_door(gx0, gx1, base, 0.45, z0 + 0.1, z0 + 0.5, 'concrete', dw)
    mullion_wall(F, gx0, gx1, 0.45, top, z0, 1.6, door=(-dw / 2, dw / 2, dh), transoms=(dh, dh + (top - dh) / 2), depth=0.3, fw=0.1)
    cy0, cy1 = top, top + 0.75
    F.box(x0 - 0.4, x1 + 0.4, cy0, h + 0.9, z0 - 0.15, z0 + 0.7, 'alu')            # fascia
    F.box(-11.0, 11.0, cy0, cy1, z0 - 4.6, z0 - 0.15, 'alu')                         # canopy slab
    F.box(-10.8, 10.8, cy0 - 0.03, cy0, z0 - 4.4, z0 - 0.15, 'inner')                # soffit
    for cx in (-9.6, -4.4, 4.4, 9.6):
        F.box(cx - 0.14, cx + 0.14, base, cy0, z0 - 4.1, z0 - 3.82, 'steel')
    # sides: concrete panels with recessed joints, a ribbon window high up, an aluminium coping
    for P, x, sgn in ((L, x0, 1), (R, x1, -1)):
        xin = x + 0.5 * sgn
        y = base
        for yt in (3.5, 7.0, 10.5, h):
            P.box(x, xin, y + 0.03, yt - 0.03, z0 + 0.7, z1, 'concrete')
            y = yt
        P.box(x + 0.06 * sgn, xin, base, h, z0 + 0.7, z1, 'steel')                       # joint backing
        for k in range(6):
            za = z0 + 1.6 + k * 2.6
            P.box(x - 0.05 * sgn, x + 0.1 * sgn, 7.9, 10.0, za, za + 2.45, 'glass')
            P.box(x - 0.1 * sgn, x + 0.1 * sgn, 7.8, 10.1, za - 0.12, za, 'steel')
        P.box(x - 0.1 * sgn, x + 0.1 * sgn, 7.8, 7.92, z0 + 1.48, z0 + 1.6 + 6 * 2.6, 'steel')
        P.box(x - 0.1 * sgn, x + 0.1 * sgn, 9.98, 10.1, z0 + 1.48, z0 + 1.6 + 6 * 2.6, 'steel')
        P.box(x - 0.08 * sgn, x + 0.6 * sgn, h, h + 0.9, z0 + 0.7, z1 + 0.05, 'alu')
    # back: concrete panels, a service door
    y = base
    for yt in (3.5, 7.0, 10.5, h):
        B.box(x0 + 0.5, x1 - 0.5, y + 0.03, yt - 0.03, z1 - 0.5, z1, 'concrete')
        y = yt
    B.box(x0 + 0.5, x1 - 0.5, base, h, z1 - 0.5, z1 - 0.44, 'steel')
    B.box(-1.2, 1.2, 0.0, 2.6, z1 - 0.02, z1 + 0.06, 'steel')
    B.box(x0 - 0.05, x1 + 0.05, h, h + 0.9, z1 - 0.6, z1 + 0.05, 'alu')
    # the entrance plaza: stone paving over the apron, a granite kerb
    F.box(x0 - 1.0, x1 + 1.0, -0.6, 0.06, v['frame_min'][1] + 0.3, z0, 'paving')
    F.box(x0 - 1.2, x1 + 1.2, -0.6, 0.12, v['frame_min'][1], v['frame_min'][1] + 0.3, 'concrete')
    # roof: the slab and rooftop plant
    T.box(x0 + 0.5, x1 - 0.5, h - 0.4, h + 0.2, z0 + 0.7, z1 - 0.5, 'concrete')
    T.box(-6.0, -1.0, h + 0.2, h + 1.8, 2.0, 6.5, 'steel')
    T.box(2.0, 6.5, h + 0.2, h + 1.4, -2.0, 4.0, 'alu')
    return dict(parts=[F, B, L, R, T], sign=None)


def perp_pit(v):
    x0, x1, z0, z1, h, dw, dh = frame(v)
    F, B, L, R, T = (Part(n) for n in ('front', 'back', 'left', 'right', 'roof'))
    base = -0.7
    floor2 = 5.0
    crown = h - 1.3
    F.box_door(x0, x1, base, 0.5, z0, z0 + 0.5, 'concrete', dw)                                     # plinth
    # front: two storeys of glass between the corner posts, the floor-edge band, the crown with its ticker strip
    mullion_wall(F, x0 + 0.5, x1 - 0.5, 0.5, crown, z0, 1.5, door=(-dw / 2, dw / 2, dh), transoms=(floor2 + 2.2,), depth=0.25, fw=0.09)
    F.box_door(x0 + 0.5, x1 - 0.5, floor2 - 0.3, floor2 + 0.3, z0 - 0.12, z0 + 0.3, 'steel', 0.0)     # slab edge
    F.box(x0 - 0.05, x1 + 0.05, crown, h + 1.0, z0 - 0.2, z0 + 0.6, 'steel')                       # crown
    F.box(x0 + 0.6, x1 - 0.6, crown + 0.85, crown + 1.25, z0 - 0.25, z0 - 0.2, 'accent')           # ticker strip
    for x in (x0, x1 - 0.5):
        F.box(x, x + 0.5, base, h + 1.0, z0 - 0.2, z0 + 0.5, 'steel')                              # corner posts
    n = int((x1 - x0) // 3.0)
    for i in range(1, n):                                                                           # deep fins
        x = x0 + i * (x1 - x0) / n
        if abs(x) < dw / 2 + 1.6:
            continue
        F.box(x - 0.07, x + 0.07, 0.5, crown, z0 - 0.75, z0 - 0.04, 'steel')
    # the entrance portal: a steel frame pushed 1.4 m out round the door
    px0, px1, py1 = -dw / 2 - 0.5, dw / 2 + 0.5, dh + 0.7
    F.box(px0, px0 + 0.4, base, py1, z0 - 1.5, z0 - 0.05, 'steel')
    F.box(px1 - 0.4, px1, base, py1, z0 - 1.5, z0 - 0.05, 'steel')
    F.box(px0, px1, py1 - 0.45, py1, z0 - 1.5, z0 - 0.05, 'steel')
    F.box(px0 + 0.4, px1 - 0.4, py1 - 0.48, py1 - 0.45, z0 - 1.45, z0 - 0.1, 'accent')
    # sides: glass the full depth behind mullions and fins, a plinth, the slab band and the crown carried round
    for P, x, sgn in ((L, x0, 1), (R, x1, -1)):
        P.box(x, x + 0.5 * sgn, base, 0.5, z0 + 0.5, z1, 'concrete')
        P.box(x + 0.12 * sgn, x + 0.16 * sgn, 0.5, crown, z0 + 0.5, z1 - 0.5, 'glass')
        P.box(x + 0.4 * sgn, x + 0.5 * sgn, 0.5, crown, z0 + 0.5, z1 - 0.5, 'inner')
        m = int((z1 - z0 - 1.0) // 1.5)
        for i in range(m + 1):
            z = z0 + 0.5 + i * (z1 - z0 - 1.0) / m
            P.box(x - 0.04 * sgn, x + 0.3 * sgn, 0.5, crown, z - 0.045, z + 0.045, 'steel')
            if i % 2 == 0 and 0 < i < m:
                P.box(x - 0.6 * sgn, x, 0.5, crown, z - 0.06, z + 0.06, 'steel')
        P.box(x - 0.12 * sgn, x + 0.3 * sgn, floor2 - 0.3, floor2 + 0.3, z0 + 0.5, z1, 'steel')
        P.box(x - 0.2 * sgn, x + 0.6 * sgn, crown, h + 1.0, z0 + 0.5, z1, 'steel')
    # back: dark metal panels with the ticker carried round
    B.box(x0, x1, base, h + 1.0, z1 - 0.5, z1, 'steel')
    B.box(x0 + 1, x1 - 1, crown + 0.85, crown + 1.25, z1, z1 + 0.04, 'accent')
    T.box(x0 + 0.5, x1 - 0.5, h - 0.4, h + 0.2, z0 + 0.6, z1 - 0.5, 'concrete')
    T.box(-8.0, -3.0, h + 0.2, h + 1.6, 1.0, 6.0, 'alu')
    T.box(3.0, 9.0, h + 0.2, h + 1.2, -3.0, 2.0, 'alu')
    return dict(parts=[F, B, L, R, T], sign=None)


def hackathon_house(v):
    """Basecamp Lodge: a timber A-frame hall (ridge front to back, glazed gable to the street) between two low wings."""
    x0, x1, z0, z1, h, dw, dh = frame(v)
    F, B, L, R, T = (Part(n) for n in ('front', 'back', 'left', 'right', 'roof'))
    base = -0.8
    hw = 7.2                       # the A-frame's half width at its foot
    foot = 0.9                     # the foot line (top of the stone plinth)
    tp = math.tan(math.radians(58))
    ridge = foot + hw * tp
    ov = 1.4                       # roof overhang past the gables
    wing_eave, wing_top = 3.4, 5.4
    sy = lambda x: foot + (hw - abs(x)) * tp
    # stone plinth (the door left open)
    F.box_door(x0 - 0.1, x1 + 0.1, base, foot, z0 - 0.1, z0 + 0.6, 'stone', dw)
    B.box(x0 - 0.1, x1 + 0.1, base, foot, z1 - 0.6, z1 + 0.1, 'stone')
    L.box(x0 - 0.1, x0 + 0.6, base, foot, z0 + 0.6, z1 - 0.6, 'stone')
    R.box(x1 - 0.6, x1 + 0.1, base, foot, z0 + 0.6, z1 - 0.6, 'stone')
    # wings: board walls on the plinth, windows, shed roofs falling outwards
    for P, xa, xb, sgn in ((L, x0, -hw, -1), (R, hw, x1, 1)):
        outer = xa if sgn < 0 else xb
        F.box(xa, xb, foot, wing_eave, z0, z0 + 0.35, 'timber_dark')
        B.box(xa, xb, foot, wing_eave, z1 - 0.35, z1, 'timber_dark')
        P.box(outer, outer - 0.35 * sgn, foot, wing_eave, z0, z1, 'timber_dark')
        for cx in ((xa + xb) / 2,):
            F.box(cx - 1.3, cx + 1.3, 1.5, 3.0, z0 + 0.14, z0 + 0.18, 'glass')
            for a, b, c, d in ((cx - 1.45, cx - 1.3, 1.35, 3.15), (cx + 1.3, cx + 1.45, 1.35, 3.15), (cx - 1.45, cx + 1.45, 1.35, 1.5),
                               (cx - 1.45, cx + 1.45, 3.0, 3.15), (cx - 0.05, cx + 0.05, 1.5, 3.0)):
                F.box(a, b, c, d, z0 - 0.08, z0 + 0.16, 'wood_post')
        # side windows
        for zc in (z0 + 5.0, z1 - 5.0):
            P.box(outer + 0.04 * sgn, outer - 0.05 * sgn, 1.5, 3.0, zc - 1.2, zc + 1.2, 'glass')
            P.box(outer + 0.1 * sgn, outer - 0.02 * sgn, 1.35, 1.5, zc - 1.35, zc + 1.35, 'wood_post')
            P.box(outer + 0.1 * sgn, outer - 0.02 * sgn, 3.0, 3.15, zc - 1.35, zc + 1.35, 'wood_post')
        ox = outer + 0.8 * sgn
        inner = (hw - 0.4) * sgn
        T.prism([(ox, wing_eave - 0.1, z0 - 0.6), (inner, wing_top, z0 - 0.6), (inner, wing_top, z1 + 0.6), (ox, wing_eave - 0.1, z1 + 0.6)], 0.28, 'slate')
        T.box(ox, ox + 0.14 * sgn, wing_eave - 0.55, wing_eave - 0.05, z0 - 0.6, z1 + 0.6, 'wood_post')
        # the wing's gable-end infill under its shed roof (front and back): a board triangle
        for z, Q, d in ((z0, F, 0.35), (z1 - 0.35, B, 0.35)):
            Q.prism([(xb if sgn < 0 else xa, wing_eave, z), (outer, wing_eave, z), (xb if sgn < 0 else xa, wing_top - 0.1, z)], d, 'timber_dark', push=(0, 0, 1))
    # the A-frame roof: two slate planes from the foot line to the ridge, past both gables
    for sgn in (-1, 1):
        T.prism([(sgn * (hw + 0.5), foot - 0.3, z0 - ov), (0.0, ridge + 0.2, z0 - ov), (0.0, ridge + 0.2, z1 + ov), (sgn * (hw + 0.5), foot - 0.3, z1 + ov)], 0.4, 'slate')
        for z in (z0 - ov + 0.1, z1 + ov - 0.1):
            T.beam((sgn * (hw + 0.55), foot - 0.35, z), (0.0, ridge + 0.3, z), 0.25, 0.55, 'wood_post')
    T.box(-0.28, 0.28, ridge + 0.2, ridge + 0.55, z0 - ov, z1 + ov, 'wood_post')                  # ridge cap
    # the glazed front gable: heavy timber A-frame members, posts, two cross beams, glass panes following the slope
    gz = z0 + 0.1
    for sgn in (-1, 1):
        F.beam((sgn * hw, foot, gz), (0.0, ridge, gz), 0.45, 0.5, 'wood_post')
    for y in (dh + 0.4, dh + 4.0):
        half = (ridge - y) / tp
        F.box(-half, half, y - 0.16, y + 0.16, gz - 0.24, gz + 0.24, 'wood_post')
    outer_n = 3
    xs = [-hw + i * (hw - dw / 2) / outer_n for i in range(outer_n)] + [-dw / 2, -dw / 4, 0.0, dw / 4, dw / 2] + \
         [dw / 2 + (i + 1) * (hw - dw / 2) / outer_n for i in range(outer_n)]
    for a, b in zip(xs[:-1], xs[1:]):
        lo = dh + 0.4 if (a >= -dw / 2 - 1e-6 and b <= dw / 2 + 1e-6) else foot
        ya, yb = sy(a) - 0.25, sy(b) - 0.25
        if min(ya, yb) - lo > 0.2:
            F.prism([(a + 0.08, lo, gz + 0.1), (b - 0.08, lo, gz + 0.1), (b - 0.08, yb, gz + 0.1), (a + 0.08, ya, gz + 0.1)], 0.04, 'glass', push=(0, 0, 1))
    for x in xs[1:-1]:
        lo = dh + 0.4 if -dw / 2 - 1e-6 <= x <= dw / 2 + 1e-6 else foot
        if abs(x) in (dw / 2,):
            lo = 0.0
        F.box(x - 0.09, x + 0.09, lo, sy(x) - 0.1, gz - 0.12, gz + 0.22, 'wood_post')
    F.box_door(-hw, hw, foot - 0.15, foot + 0.1, gz - 0.15, gz + 0.25, 'wood_post', dw)               # sill beam
    F.box(-dw / 2 - 0.2, dw / 2 + 0.2, dh, dh + 0.4, gz - 0.25, gz + 0.3, 'wood_post')               # door head
    # back gable: boards, a stone chimney up past the ridge
    B.prism([(-hw, foot, z1 - 0.3), (hw, foot, z1 - 0.3), (0.0, ridge, z1 - 0.3)], 0.3, 'timber_dark', push=(0, 0, 1))
    B.box(2.2, 4.0, base, ridge - 3.0, z1 - 0.3, z1 + 1.0, 'stone')
    T.box(2.3, 3.9, ridge - 3.0, ridge + 1.6, z1 - 0.3, z1 + 1.0, 'stone')
    return dict(parts=[F, B, L, R, T], sign=dict(y=dh + 0.75, z=z0 - 0.35, w=6.4))


def prediction_pit(v):
    """The boardwalk beach hut: a pine pavilion on a low deck, a reed-thatch hip roof on posts, wide serving windows."""
    x0, x1, z0, z1, h, dw, dh = frame(v)
    F, B, L, R, T = (Part(n) for n in ('front', 'back', 'left', 'right', 'roof'))
    base = -0.9
    deck = 0.1
    wall = 5.0
    ov = 2.6
    eave = wall - 0.2
    tp = math.tan(math.radians(34))
    # the deck (planks on a timber edge) out round the hut; the front deck is the apron
    F.box(x0 - ov - 0.2, x1 + ov + 0.2, base, deck, z0 - ov - 0.2, z0, 'wood_post')
    B.box(x0 - ov - 0.2, x1 + ov + 0.2, base, deck, z1, z1 + ov + 0.2, 'wood_post')
    L.box(x0 - ov - 0.2, x0, base, deck, z0, z1, 'wood_post')
    R.box(x1, x1 + ov + 0.2, base, deck, z0, z1, 'wood_post')
    # walls: pine boards, the front open in two wide serving windows either side of the door
    wx = dw / 2 + 4.2
    F.box_door(x0, x1, deck, 1.3, z0, z0 + 0.3, 'timber_light', dw)
    F.box(x0, x1, dh, wall, z0, z0 + 0.3, 'timber_light')
    for xa, xb in ((x0, -wx - 3.2), (-wx + 3.2, -dw / 2), (dw / 2, wx - 3.2), (wx + 3.2, x1)):
        F.box(xa, xb, 1.3, dh, z0, z0 + 0.3, 'timber_light')
    for xa, xb in ((-wx - 3.2, -wx + 3.2), (wx - 3.2, wx + 3.2)):
        F.box(xa, xb, 3.8, dh, z0, z0 + 0.3, 'timber_light')
    B.box(x0, x1, deck, wall, z1 - 0.3, z1, 'timber_light')
    L.box(x0, x0 + 0.3, deck, wall, z0, z1, 'timber_light')
    R.box(x1 - 0.3, x1, deck, wall, z0, z1, 'timber_light')
    for cx in (-wx, wx):
        F.box(cx - 3.0, cx + 3.0, 1.5, 3.6, z0 + 0.2, z0 + 0.24, 'glass')
        F.box(cx - 3.3, cx + 3.3, 1.3, 1.45, z0 - 0.55, z0 + 0.3, 'wood_post')          # counter
        for a, b, c, d in ((cx - 3.2, cx - 3.0, 1.3, 3.8), (cx + 3.0, cx + 3.2, 1.3, 3.8), (cx - 3.2, cx + 3.2, 3.6, 3.8)):
            F.box(a, b, c, d, z0 - 0.1, z0 + 0.3, 'wood_post')
        F.prism([(cx - 3.2, 3.85, z0 - 0.05), (cx + 3.2, 3.85, z0 - 0.05), (cx + 3.2, 4.45, z0 - 1.6), (cx - 3.2, 4.45, z0 - 1.6)], 0.08, 'timber_light')
    for x in (-dw / 2, dw / 2):
        F.box(x - 0.15, x + 0.15, deck, dh + 0.15, z0 - 0.12, z0 + 0.35, 'wood_post')
    F.box(-dw / 2 - 0.15, dw / 2 + 0.15, dh, dh + 0.3, z0 - 0.12, z0 + 0.35, 'wood_post')
    # posts at the deck edge carry the eave beams
    ex0, ex1, ez0, ez1 = x0 - ov, x1 + ov, z0 - ov, z1 + ov
    for P, pts in ((F, [(x, ez0 + 0.3) for x in (ex0 + 0.3, -9.0, -dw / 2 - 1.4, dw / 2 + 1.4, 9.0, ex1 - 0.3)]),
                   (B, [(x, ez1 - 0.3) for x in (ex0 + 0.3, -6.0, 0.0, 6.0, ex1 - 0.3)]),
                   (L, [(ex0 + 0.3, z) for z in (-3.0, 4.0)]), (R, [(ex1 - 0.3, z) for z in (-3.0, 4.0)])):
        for x, z in pts:
            P.box(x - 0.16, x + 0.16, deck, eave, z - 0.16, z + 0.16, 'wood_post')
    F.box(ex0, ex1, eave, eave + 0.35, ez0 + 0.1, ez0 + 0.45, 'wood_post')
    B.box(ex0, ex1, eave, eave + 0.35, ez1 - 0.45, ez1 - 0.1, 'wood_post')
    L.box(ex0 + 0.1, ex0 + 0.45, eave, eave + 0.35, ez0, ez1, 'wood_post')
    R.box(ex1 - 0.45, ex1 - 0.1, eave, eave + 0.35, ez0, ez1, 'wood_post')
    # wall-top to eave: rafters' soffit as a board ceiling under the overhang
    F.box(ex0, ex1, eave + 0.35, eave + 0.4, ez0, z0, 'timber_light')
    B.box(ex0, ex1, eave + 0.35, eave + 0.4, z1, ez1, 'timber_light')
    L.box(ex0, x0, eave + 0.35, eave + 0.4, z0, z1, 'timber_light')
    R.box(x1, ex1, eave + 0.35, eave + 0.4, z0, z1, 'timber_light')
    F.box(x0, x1, wall, eave + 0.36, z0, z0 + 0.3, 'timber_light')
    # the thatch hip roof: ridge along x
    hd = (ez1 - ez0) / 2
    e = eave + 0.3
    ry = e + hd * tp
    zc = (ez0 + ez1) / 2
    rx0, rx1 = ex0 + hd, ex1 - hd
    th = 0.6
    for poly in ([(ex0, e, ez0), (ex1, e, ez0), (rx1, ry, zc), (rx0, ry, zc)],
                 [(ex1, e, ez1), (ex0, e, ez1), (rx0, ry, zc), (rx1, ry, zc)],
                 [(ex0, e, ez1), (ex0, e, ez0), (rx0, ry, zc)],
                 [(ex1, e, ez0), (ex1, e, ez1), (rx1, ry, zc)]):
        T.prism([(p[0], p[1] + 0.35, p[2]) for p in poly], th, 'thatch')
    T.box(rx0 - 0.4, rx1 + 0.4, ry + 0.2, ry + 0.75, zc - 0.4, zc + 0.4, 'wood_post')     # ridge roll
    return dict(parts=[F, B, L, R, T], sign=dict(y=e + 0.6, z=ez0 - 0.3, w=8.0))


BUILDERS = {'conference_hall': conference_hall, 'perp_pit': perp_pit, 'hackathon_house': hackathon_house, 'prediction_pit': prediction_pit}


def main():
    plan = json.load(open(os.path.abspath(os.path.join(HERE, '..', '..', 'tools', 'world', 'islandgen', 'out', 'plan.json'))))
    venues = {v['id']: v for v in plan['venues']}
    ids = [a for a in ARGS if a in BUILDERS] or list(BUILDERS)
    os.makedirs(OUT, exist_ok=True)
    for vid in ids:
        res = BUILDERS[vid](venues[vid])
        parts = {}
        tris = 0
        for p in res['parts']:
            d = export_part(p)
            if d['v']:
                parts[p.name] = d
                tris += sum(len(t) for t in d['sub'].values()) // 3
        x0, x1, z0, z1, h, dw, dh = frame(venues[vid])
        out = dict(venue=vid, footprint=[x0, x1, z0, z1], height=h, door=[dw, dh], tris=tris, parts=parts, sign=res['sign'],
                   note='art/pipeline/world_venue_shells.py (world fix-ups 2026-10-23)')
        json.dump(out, open(os.path.join(OUT, vid + '.json'), 'w'), separators=(',', ':'))
        print('[venues]', vid, 'tris', tris, {k: sum(len(t) for t in d['sub'].values()) // 3 for k, d in parts.items()}, flush=True)
    if '--sheet' in ARGS:
        sheet(ids, ARGS[ARGS.index('--sheet') + 1])


# ---------------------------------------------------------------------------------------------------- lookdev sheet
COLS = {'glass': (0.05, 0.08, 0.11, 0.95), 'steel': (0.06, 0.065, 0.07, 0.5), 'alu': (0.78, 0.79, 0.8, 0.6), 'concrete': (0.42, 0.40, 0.37, 0.2),
        'accent': (0.1, 0.8, 0.45, 0.5), 'timber_dark': (0.30, 0.22, 0.15, 0.1), 'timber_light': (0.62, 0.47, 0.30, 0.2),
        'wood_post': (0.33, 0.24, 0.16, 0.1), 'slate': (0.26, 0.26, 0.27, 0.2), 'thatch': (0.55, 0.45, 0.30, 0.05), 'stone': (0.45, 0.40, 0.33, 0.2),
        'inner': (0.9, 0.86, 0.8, 0.2), 'paving': (0.6, 0.58, 0.55, 0.2)}


def sheet(ids, path):
    """Each venue from its street side three-quarter view (EEVEE, sun and sky; flat colours per material)."""
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    sc = bpy.context.scene
    mats = {}
    for k, c in COLS.items():
        m = bpy.data.materials.new(k); m.use_nodes = True
        b = m.node_tree.nodes['Principled BSDF']
        b.inputs['Base Color'].default_value = c[:3] + (1,)
        b.inputs['Roughness'].default_value = 1 - c[3]
        mats[k] = m
    x = 0.0
    for vid in ids:
        d = json.load(open(os.path.join(OUT, vid + '.json')))
        for pn, p in d['parts'].items():
            me = bpy.data.meshes.new(vid + pn)
            verts = [(vv[0], vv[2], vv[1]) for vv in p['v']]
            faces, fm = [], []
            for mi, (mk, idx) in enumerate(p['sub'].items()):
                for i in range(0, len(idx), 3):
                    faces.append((idx[i], idx[i + 2], idx[i + 1])); fm.append(mi)
            me.from_pydata(verts, [], faces)
            for mk in p['sub']:
                me.materials.append(mats[mk])
            for f, mi in zip(me.polygons, fm):
                f.material_index = mi
            o = bpy.data.objects.new(vid + pn, me)
            o.location.x = x
            sc.collection.objects.link(o)
        x += 50.0
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam)
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = x
    cam.location = (x / 2 - 25 - 30, -70, 32); cam.rotation_euler = (math.radians(70), 0, math.radians(-25))
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(-40)); sc.collection.objects.link(sun)
    sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
    sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.55, 0.65, 0.8, 1)
    sc.world.node_tree.nodes['Background'].inputs[1].default_value = 0.9
    sc.camera = cam; sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x = 2400; sc.render.resolution_y = 700
    sc.view_settings.view_transform = 'Standard'
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print('[venues] sheet', path)


main()
