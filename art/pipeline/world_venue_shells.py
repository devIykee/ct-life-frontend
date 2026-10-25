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


# district fix-ups 2: the far LOD leaves out line-like members (frames, mullions, bars, sills, trims, string courses: any box
# whose second-smallest side is under 0.3 m, any beam under 0.3 m): at distance they are sub-pixel triangles that cost the
# mobile GPU (quad overdraw) without being seen
LOD_MODE = False


def mat_index(mat):
    """Material slots: a base name, optionally tinted as 'base:rrggbb' (the builder multiplies the base material by it)."""
    if mat not in MATS:
        MATS.append(mat)
    return MATS.index(mat)


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
        f.material_index = mat_index(mat)
        return f

    def box(self, x0, x1, y0, y1, z0, z1, mat, skip=()):
        """An axis-aligned box in the venue frame (x, y up, z depth); skip: faces left out ('-x', '+x', '-y', '+y', '-z', '+z')."""
        x0, x1 = min(x0, x1), max(x0, x1); y0, y1 = min(y0, y1), max(y0, y1); z0, z1 = min(z0, z1), max(z0, z1)
        if x1 - x0 < 1e-4 or y1 - y0 < 1e-4 or z1 - z0 < 1e-4:
            return
        if LOD_MODE and sorted((x1 - x0, y1 - y0, z1 - z0))[1] < 0.3:
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
        if LOD_MODE and max(w, h) < 0.3:
            return
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
    if v.get('walkin'):            # a walk-in landmark (The Rug Room): the streamed room's entrance, 1.2 x 2.3 m
        return x0, x1, z0, z1, h, v['walkin'][0], v['walkin'][1]
    if v.get('nodoor'):            # the landmark buildings: no door, no interior
        return x0, x1, z0, z1, h, 0.0, 0.0
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


# ---------------------------------------------------------------------------------------------------- district fix-ups 2
# The remaining venue shells, from a few parametric archetypes (masonry with punched windows, glass towers, a barn, an
# industrial shed, an open-air arena), each configured to its venue's identity (docs/progress/venues.md).
SIDES = ('front', 'back', 'left', 'right')


def side_frame(side, x0, x1, z0, z1):
    """(axis, plane, outward, a0, a1) of a side: front z = z0 facing -z, back z = z1, left x = x0, right x = x1."""
    return {'front': ('z', z0, -1, x0, x1), 'back': ('z', z1, 1, x0, x1), 'left': ('x', x0, -1, z0, z1), 'right': ('x', x1, 1, z0, z1)}[side]


def abox(P, axis, plane, out, a_lo, a_hi, y_lo, y_hi, d0, d1, mat):
    """A box on a side in its own terms: along a, up y, depth d measured outward from the wall plane."""
    if axis == 'z':
        P.box(a_lo, a_hi, y_lo, y_hi, plane + out * d0, plane + out * d1, mat)
    else:
        P.box(plane + out * d0, plane + out * d1, y_lo, y_hi, a_lo, a_hi, mat)


def wall_openings(P, axis, plane, out, a0, a1, y0, y1, t, holes, mat):
    """A wall t thick (inwards) with rectangular holes [(a_lo, a_hi, y_lo, y_hi)], as boxes round the holes."""
    cuts = sorted(set([a0, a1] + [min(max(h[0], a0), a1) for h in holes] + [min(max(h[1], a0), a1) for h in holes]))
    for ca, cb in zip(cuts[:-1], cuts[1:]):
        if cb - ca < 1e-3:
            continue
        mid = (ca + cb) / 2
        y = y0
        for lo, hi in sorted((h[2], h[3]) for h in holes if h[0] <= mid <= h[1]):
            if lo > y:
                abox(P, axis, plane, out, ca, cb, y, lo, -t, 0.0, mat)
            y = max(y, hi)
        if y < y1:
            abox(P, axis, plane, out, ca, cb, y, y1, -t, 0.0, mat)


def window(P, axis, plane, out, a_lo, a_hi, y_lo, y_hi, frame, glass='glass', reveal=0.25, mull=0, bars=False, sill='concrete'):
    """Glass set back `reveal` in an opening, a frame round it, optional mullions and bars, a projecting sill."""
    abox(P, axis, plane, out, a_lo, a_hi, y_lo, y_hi, -reveal - 0.04, -reveal, glass)
    fw = 0.07
    for a, b, c, d in ((a_lo, a_lo + fw, y_lo, y_hi), (a_hi - fw, a_hi, y_lo, y_hi), (a_lo, a_hi, y_lo, y_lo + fw), (a_lo, a_hi, y_hi - fw, y_hi)):
        abox(P, axis, plane, out, a, b, c, d, -reveal - 0.02, -reveal + 0.06, frame)
    for k in range(1, mull + 1):
        a = a_lo + (a_hi - a_lo) * k / (mull + 1)
        abox(P, axis, plane, out, a - fw / 2, a + fw / 2, y_lo, y_hi, -reveal - 0.02, -reveal + 0.06, frame)
    if bars:
        n = max(2, int((a_hi - a_lo) / 0.16))
        for k in range(1, n):
            a = a_lo + (a_hi - a_lo) * k / n
            abox(P, axis, plane, out, a - 0.02, a + 0.02, y_lo, y_hi, -0.12, -0.08, 'steel')
    if sill:
        abox(P, axis, plane, out, a_lo - 0.08, a_hi + 0.08, y_lo - 0.1, y_lo, -0.05, 0.08, sill)


def venue_parts():
    return {k: Part(k) for k in SIDES + ('roof',)}


def masonry(v, wall, frame, floors, win_w, spacing, win_h, sill_h=0.9, plinth='stone', plinth_h=0.8, cornice='concrete', reveal=0.28,
            bars=False, mull=1, glass='glass', side_windows=True, front_skip=None, parapet=0.9):
    """A rendered / stone building: walls 0.4 thick with punched windows (deep reveals, frames, sills) on every floor, a
    plinth, a cornice and parapet; the door left open on the front."""
    x0, x1, z0, z1, h, dw, dh = frame_(v)
    Pt = venue_parts()
    t = 0.4
    base = -0.7
    for side in SIDES:
        P = Pt[side]
        axis, plane, out, a0, a1 = side_frame(side, x0, x1, z0, z1)
        if axis == 'x':
            a0, a1 = z0 + t, z1 - t
        holes, wins = [], []
        if side == 'front':
            holes.append((-dw / 2, dw / 2, base, dh))
        if side == 'front' or side_windows:
            y = plinth_h
            for fh in floors:
                n = max(1, int((a1 - a0 - 2.0) // spacing))
                c0 = (a0 + a1) / 2 - (n - 1) * spacing / 2
                for i in range(n):
                    a = c0 + i * spacing
                    lo, hi = y + sill_h, min(y + sill_h + win_h, h - 0.6)
                    if side == 'front' and y < dh and abs(a) < dw / 2 + win_w / 2 + 0.6:
                        continue
                    if side == 'front' and front_skip and front_skip(a, lo, hi):
                        continue
                    if hi - lo > 0.4:
                        holes.append((a - win_w / 2, a + win_w / 2, lo, hi)); wins.append((a - win_w / 2, a + win_w / 2, lo, hi))
                y += fh
        wall_openings(P, axis, plane, out, a0, a1, base, h, t, holes, wall)
        for a_lo, a_hi, lo, hi in wins:
            window(P, axis, plane, out, a_lo, a_hi, lo, hi, frame, glass=glass, reveal=reveal, mull=mull, bars=bars)
        # plinth band and cornice
        pa0, pa1 = (a0, a1) if axis == 'z' else (z0, z1)
        if side == 'front':
            abox(P, axis, plane, out, pa0 - 0.1, -dw / 2, base, plinth_h, -0.05, 0.12, plinth)
            abox(P, axis, plane, out, dw / 2, pa1 + 0.1, base, plinth_h, -0.05, 0.12, plinth)
        else:
            abox(P, axis, plane, out, pa0 - 0.1, pa1 + 0.1, base, plinth_h, -0.05, 0.12, plinth)
        abox(P, axis, plane, out, pa0 - 0.25, pa1 + 0.25, h - 0.35, h, -0.05, 0.35, cornice)
        abox(P, axis, plane, out, pa0 - 0.1, pa1 + 0.1, h, h + parapet, -0.3, 0.1, wall)
        abox(P, axis, plane, out, pa0 - 0.18, pa1 + 0.18, h + parapet, h + parapet + 0.12, -0.38, 0.18, cornice)
        # floor-line string courses
        y = plinth_h
        for fh in floors[:-1]:
            y += fh
            abox(P, axis, plane, out, pa0, pa1, y - 0.12, y + 0.08, -0.05, 0.1, cornice)
    # door surround
    F = Pt['front']
    if dw > 0:
        F.box(-dw / 2 - 0.35, -dw / 2, base, dh + 0.35, z0 - 0.15, z0 + t, frame)
        F.box(dw / 2, dw / 2 + 0.35, base, dh + 0.35, z0 - 0.15, z0 + t, frame)
        F.box(-dw / 2 - 0.35, dw / 2 + 0.35, dh, dh + 0.35, z0 - 0.15, z0 + t, frame)
    Pt['roof'].box(x0 + t, x1 - t, h - 0.4, h + 0.15, z0 + t, z1 - t, 'concrete')
    return Pt


def frame_(v):
    return frame(v)


def roof_plant(T, h, boxes):
    for (a, b, c, d, hh, m) in boxes:
        T.box(a, b, h + 0.15, h + 0.15 + hh, c, d, m)


def ct_jail(v):
    """Compliance Jail: a stern civic block in pale stone, narrow barred windows, a columned portico, a heavy cornice."""
    x0, x1, z0, z1, h, dw, dh = frame(v)
    Pt = masonry(v, 'plaster:d6d2c8', 'steel', [4.2, 3.8], 1.0, 2.6, 2.4, sill_h=1.4, plinth='stone', plinth_h=1.0, cornice='concrete:e6e2da',
                 bars=True, mull=0)
    F = Pt['front']
    # portico: four stone columns carrying an entablature over the steps
    for cx in (-6.0, -2.9, 2.9, 6.0):
        F.box(cx - 0.4, cx + 0.4, 0.3, 6.0, z0 - 3.4, z0 - 2.6, 'concrete:e6e2da')
        F.box(cx - 0.55, cx + 0.55, 0.15, 0.4, z0 - 3.55, z0 - 2.45, 'stone')
    F.box(-7.0, 7.0, 6.0, 7.4, z0 - 3.8, z0, 'concrete:e6e2da')
    F.box(-7.2, 7.2, 7.4, 7.6, z0 - 4.0, z0, 'concrete:e6e2da')
    F.box(-7.5, 7.5, -0.6, 0.15, z0 - 4.4, z0, 'stone')            # steps
    F.box(-7.0, 7.0, -0.6, 0.3, z0 - 3.9, z0, 'stone')
    roof_plant(Pt['roof'], h, [(-5, -1, 2, 6, 1.4, 'steel'), (3, 6, -3, 1, 1.0, 'alu')])
    return dict(parts=list(Pt.values()), sign=dict(y=6.1, z=z0 - 3.86, w=8.0))


def rekt_clinic(v):
    """Rekt Clinic: a white rendered clinic, green-framed ribbon-ish windows, an entrance canopy, a red cross on the parapet."""
    x0, x1, z0, z1, h, dw, dh = frame(v)
    Pt = masonry(v, 'plaster:eef3ff', 'steel:3f8f66', [4.4, 3.6], 2.6, 3.2, 1.9, sill_h=1.1, plinth='concrete', plinth_h=0.6,
                 cornice='alu', mull=1, parapet=0.7)
    F = Pt['front']
    F.box(-6.5, 6.5, dh + 0.3, dh + 0.65, z0 - 3.2, z0, 'alu')          # canopy
    for cx in (-6.0, 6.0):
        F.box(cx - 0.12, cx + 0.12, -0.5, dh + 0.3, z0 - 3.0, z0 - 2.76, 'steel')
    # the red cross on a white panel over the parapet, off-centre
    c = 9.0
    F.box(c - 1.2, c + 1.2, h - 0.2, h + 2.2, z0 - 0.25, z0 - 0.05, 'alu')
    F.box(c - 0.9, c + 0.9, h + 0.7, h + 1.3, z0 - 0.32, z0 - 0.25, 'accent:d83a3a')
    F.box(c - 0.3, c + 0.3, h + 0.1, h + 1.9, z0 - 0.32, z0 - 0.25, 'accent:d83a3a')
    roof_plant(Pt['roof'], h, [(-6, -2, 1, 5, 1.2, 'alu')])
    return dict(parts=list(Pt.values()), sign=None)


def audit_row(v):
    """Audit Row: a sober office block, a deep-set window grid in cream render, blue frames, a slim entrance canopy."""
    x0, x1, z0, z1, h, dw, dh = frame(v)
    Pt = masonry(v, 'plaster:ebe4d2', 'steel:2f4f7f', [4.6, 3.4], 1.6, 2.4, 2.2, sill_h=1.0, plinth='stone', plinth_h=0.7,
                 cornice='concrete', mull=0, reveal=0.32)
    Pt['front'].box(-4.0, 4.0, dh + 0.35, dh + 0.6, z0 - 2.2, z0, 'steel:2f4f7f')
    roof_plant(Pt['roof'], h, [(-7, -3, 0, 4, 1.3, 'alu'), (4, 7, -4, 0, 0.9, 'steel')])
    return dict(parts=list(Pt.values()), sign=None)


def spaces_studio(v):
    """Spaces Studio: a terracotta-rendered broadcast studio, a wide glazed studio window upstairs, a dish and a mast on top."""
    x0, x1, z0, z1, h, dw, dh = frame(v)
    big = lambda a, lo, hi: lo > 5.0 and abs(a) < 9.0
    Pt = masonry(v, 'plaster:d98c6a', 'steel', [5.0, 5.2], 1.6, 2.8, 2.4, sill_h=1.0, plinth='concrete', plinth_h=0.6,
                 cornice='concrete:efe8de', mull=0, front_skip=big)
    F = Pt['front']
    # the studio window: one long glazed band with slim mullions, framed proud of the wall
    F.box(-9.0, 9.0, 6.0, 9.4, z0 - 0.02, z0 + 0.02, 'glass')
    for k in range(0, 13):
        x = -9.0 + k * 1.5
        F.box(x - 0.05, x + 0.05, 6.0, 9.4, z0 - 0.12, z0 + 0.02, 'steel')
    F.box(-9.3, 9.3, 5.7, 6.0, z0 - 0.5, z0 + 0.02, 'concrete:efe8de')
    F.box(-9.3, 9.3, 9.4, 9.7, z0 - 0.5, z0 + 0.02, 'concrete:efe8de')
    F.box(-9.3, -9.0, 6.0, 9.4, z0 - 0.5, z0 + 0.02, 'concrete:efe8de')
    F.box(9.0, 9.3, 6.0, 9.4, z0 - 0.5, z0 + 0.02, 'concrete:efe8de')
    T = Pt['roof']
    # satellite dish (a tilted octagonal dish on a post) and a lattice-ish mast
    cx, cz = 8.0, 4.0
    T.box(cx - 0.15, cx + 0.15, h + 0.15, h + 1.6, cz - 0.15, cz + 0.15, 'steel')
    ring = [(cx + 1.4 * math.cos(a), h + 2.4 + 1.4 * math.sin(a) * 0.9, cz - 0.5 + 1.4 * math.sin(a) * 0.4) for a in [k * math.pi / 4 for k in range(8)]]
    T.prism(ring, 0.12, 'alu', push=(0, 0, 1))
    T.box(-9.2, -8.8, h + 0.15, h + 7.0, 5.0, 5.4, 'steel')
    T.box(-9.6, -8.4, h + 6.8, h + 7.1, 4.8, 5.6, 'accent:e4572e')
    return dict(parts=list(Pt.values()), sign=None)


def terminal_cafe(v):
    """Terminal Cafe: a warm two-storey corner cafe, a full shopfront of glass downstairs, a green awning, a timber terrace."""
    x0, x1, z0, z1, h, dw, dh = frame(v)
    shop = lambda a, lo, hi: lo < 4.0
    Pt = masonry(v, 'plaster:efe0c2', 'steel:2e3b33', [4.2, 3.6], 1.4, 2.6, 2.0, sill_h=0.9, plinth='stone', plinth_h=0.5,
                 cornice='concrete:f6efe2', mull=1, front_skip=shop)
    F = Pt['front']
    # the shopfront: tall panes between slim piers either side of the door, a fascia over them
    for xa, xb in ((x0 + 1.2, -dw / 2 - 0.5), (dw / 2 + 0.5, x1 - 1.2)):
        F.box(xa, xb, 0.5, 3.6, z0 + 0.15, z0 + 0.19, 'glass')
        n = max(1, int((xb - xa) / 2.2))
        for k in range(n + 1):
            x = xa + (xb - xa) * k / n
            F.box(x - 0.06, x + 0.06, 0.5, 3.6, z0 - 0.05, z0 + 0.2, 'steel:2e3b33')
        F.box(xa, xb, 0.42, 0.52, z0 - 0.05, z0 + 0.2, 'steel:2e3b33')
        F.box(xa, xb, 2.85, 2.93, z0 - 0.05, z0 + 0.2, 'steel:2e3b33')
    # cut the wall where the shopfront is (the masonry wall stays only as piers): a lighter way, a dark lining inside
    F.box(x0 + 1.2, x1 - 1.2, 3.6, 4.1, z0 - 0.08, z0 + 0.1, 'steel:2e3b33')        # fascia
    # awning: green canvas sloping out over the terrace, with a scalloped valance of alternating strips
    F.prism([(x0 + 0.8, 4.0, z0 - 0.05), (x1 - 0.8, 4.0, z0 - 0.05), (x1 - 0.8, 3.1, z0 - 2.6), (x0 + 0.8, 3.1, z0 - 2.6)], 0.05, 'accent:2f7a5a')
    for k in range(int((x1 - x0 - 1.6) / 0.9)):
        xa = x0 + 0.8 + k * 0.9
        F.box(xa, xa + 0.9, 2.85, 3.12, z0 - 2.64, z0 - 2.6, 'accent:2f7a5a' if k % 2 == 0 else 'alu')
    # terrace deck in front, edged by planters
    F.box(x0 + 0.5, x1 - 0.5, -0.6, 0.12, z0 - 3.4, z0 - 0.05, 'wood_post')
    for xa in (x0 + 0.6, x1 - 1.6):
        F.box(xa, xa + 1.0, 0.12, 0.8, z0 - 3.3, z0 - 2.4, 'concrete')
    roof_plant(Pt['roof'], h, [(-6, -3, 0, 3, 1.0, 'alu')])
    return dict(parts=list(Pt.values()), sign=dict(y=4.25, z=z0 - 0.15, w=7.0))


def glass_tower(v, fh=3.6, spandrel='steel', mframe='steel', pier='alu', fins=None, fin_every=3.0, crown='steel', mast=None,
                canopy='alu', glass='glass'):
    """A curtain-wall building: glass all round between corner piers, spandrel bands at each floor, mullions every 1.5 m,
    optional fins (colour), a crown band, a canopy over the door, rooftop plant and an optional mast."""
    x0, x1, z0, z1, h, dw, dh = frame(v)
    Pt = venue_parts()
    base = -0.7
    nfl = max(1, int(round((h - 0.6) / fh)))
    fh = (h - 0.6) / nfl
    for side in SIDES:
        P = Pt[side]
        axis, plane, out, a0, a1 = side_frame(side, x0, x1, z0, z1)
        if axis == 'x':
            a0, a1 = z0 + 0.6, z1 - 0.6
        door = side == 'front'
        # plinth
        if door:
            abox(P, axis, plane, out, a0, -dw / 2, base, 0.6, -0.5, 0.0, 'concrete')
            abox(P, axis, plane, out, dw / 2, a1, base, 0.6, -0.5, 0.0, 'concrete')
        else:
            abox(P, axis, plane, out, a0, a1, base, 0.6, -0.5, 0.0, 'concrete')
        # glass (round the door)
        if door:
            abox(P, axis, plane, out, a0, -dw / 2, 0.6, h, -0.2, -0.16, glass)
            abox(P, axis, plane, out, dw / 2, a1, 0.6, h, -0.2, -0.16, glass)
            abox(P, axis, plane, out, -dw / 2, dw / 2, dh, h, -0.2, -0.16, glass)
        else:
            abox(P, axis, plane, out, a0, a1, 0.6, h, -0.2, -0.16, glass)
        # spandrels at the floors (an opaque band where each slab meets the glass)
        for k in range(1, nfl):
            y = 0.6 + k * fh
            if door and y < dh + 0.3:
                abox(P, axis, plane, out, a0, -dw / 2 - 0.2, y - 0.45, y + 0.35, -0.16, 0.04, spandrel)
                abox(P, axis, plane, out, dw / 2 + 0.2, a1, y - 0.45, y + 0.35, -0.16, 0.04, spandrel)
            else:
                abox(P, axis, plane, out, a0, a1, y - 0.45, y + 0.35, -0.16, 0.04, spandrel)
        # mullions
        n = max(1, int(round((a1 - a0) / 1.5)))
        for i in range(n + 1):
            a = a0 + (a1 - a0) * i / n
            if door and abs(a) < dw / 2 + 0.05:
                abox(P, axis, plane, out, a - 0.05, a + 0.05, dh, h, -0.2, 0.06, mframe)
                continue
            abox(P, axis, plane, out, a - 0.05, a + 0.05, 0.6, h, -0.2, 0.06, mframe)
        if fins:
            m = max(1, int(round((a1 - a0) / fin_every)))
            for i in range(1, m):
                a = a0 + (a1 - a0) * i / m
                if door and abs(a) < dw / 2 + 1.2:
                    continue
                abox(P, axis, plane, out, a - 0.08, a + 0.08, 0.6, h + 0.6, 0.0, 0.7, fins)
        # crown
        abox(P, axis, plane, out, a0 - 0.6, a1 + 0.6, h - 0.1, h + 1.1, -0.2, 0.25, crown)
    # corner piers
    for cx, cz in ((x0, z0), (x1, z0), (x0, z1), (x1, z1)):
        side = 'front' if cz == z0 else 'back'
        Pt[side].box(cx - 0.35, cx + 0.35, base, h + 1.1, cz - 0.35, cz + 0.35, pier)
    F = Pt['front']
    if dw > 0:
        F.box(-dw / 2 - 1.5, dw / 2 + 1.5, dh + 0.35, dh + 0.7, z0 - 3.0, z0, canopy)
        F.box(-dw / 2 - 0.12, -dw / 2 + 0.06, base, dh + 0.35, z0 - 0.25, z0 + 0.05, mframe)
        F.box(dw / 2 - 0.06, dw / 2 + 0.12, base, dh + 0.35, z0 - 0.25, z0 + 0.05, mframe)
    T = Pt['roof']
    T.box(x0 + 0.5, x1 - 0.5, h - 0.4, h + 0.15, z0 + 0.5, z1 - 0.5, 'concrete')
    k = min(1.0, (x1 - x0) / 26.0)
    roof_plant(T, h, [(-6 * k, -1 * k, 1 * k, 6 * k, 1.8, 'steel'), (2 * k, 6 * k, -4 * k, 0, 1.2, 'alu')])
    if mast:
        T.box(4.0, 4.6, h + 0.15, h + 9.0, 4.0, 4.6, mast)
        T.box(3.4, 5.2, h + 8.6, h + 9.1, 3.4, 5.2, mast)
    return Pt


def launchpad_tower(v):
    """Launchpad Tower: a tall glass tower striped with orange launch fins, a white crown and a launch mast on the roof."""
    Pt = glass_tower(v, fh=3.6, spandrel='steel:2b2f36', fins='accent:ef6a2a', fin_every=3.4, crown='alu', mast='accent:ef6a2a', pier='alu')
    return dict(parts=list(Pt.values()), sign=None)


def discord_heights(v):
    """Discord Heights: a glass block with blurple spandrel bands and a white crown."""
    Pt = glass_tower(v, fh=3.4, spandrel='accent:5865f2', crown='alu', pier='steel:2b2f36', glass='glass')
    return dict(parts=list(Pt.values()), sign=None)


def vc_row(v):
    """VC Row: a glass office with warm timber fins and a dark crown (the money in the timber)."""
    Pt = glass_tower(v, fh=3.8, spandrel='steel:30343a', fins='wood_post', fin_every=1.5, crown='steel:30343a', pier='steel:30343a')
    return dict(parts=list(Pt.values()), sign=None)


def cabal_lounge(v):
    """Cabal Lounge: smoked dark glass in gold frames, gold fins, a gold-edged canopy - discreet, expensive."""
    Pt = glass_tower(v, fh=4.6, spandrel='steel:1a1a1f', mframe='alu:c9a24a', fins='alu:c9a24a', fin_every=2.2, crown='steel:1a1a1f',
                     pier='steel:1a1a1f', canopy='alu:c9a24a', glass='glass:5c5a66')
    return dict(parts=list(Pt.values()), sign=None)


def sybil_barn(v):
    """Sybil Barn: a red board barn (ridge front to back), white trim, an open sliding door on its track, a hay-loft door,
    a corrugated metal roof."""
    x0, x1, z0, z1, h, dw, dh = frame(v)
    Pt = venue_parts()
    base = -0.7
    eave = 5.0
    tp = math.tan(math.radians(32))
    hw = (x1 - x0) / 2
    ridge = eave + hw * tp
    red, trim = 'timber_light:e05a48', 'alu:f2efe8'
    sy = lambda x: eave + (hw - abs(x)) * tp
    F, B, L, R, T = (Pt[k] for k in SIDES + ('roof',))
    # long walls
    L.box(x0, x0 + 0.3, base, eave, z0 + 0.3, z1 - 0.3, red)
    R.box(x1 - 0.3, x1, base, eave, z0 + 0.3, z1 - 0.3, red)
    for P, z, d in ((F, z0, 1), (B, z1, -1)):
        za, zb = (z, z + 0.3) if d > 0 else (z - 0.3, z)
        if P is F:
            P.box(x0, -dw / 2, base, eave, za, zb, red); P.box(dw / 2, x1, base, eave, za, zb, red)
            P.box(-dw / 2, dw / 2, dh, eave, za, zb, red)
        else:
            P.box(x0, x1, base, eave, za, zb, red)
        P.prism([(x0, eave, z), (x1, eave, z), (0.0, ridge, z)], 0.3, red, push=(0, 0, d))
        # white trim: corner boards and the gable edge
        for x in (x0, x1 - 0.25):
            P.box(x - 0.05, x + 0.3, base, eave, z - 0.08 * d, z + 0.05 * d, trim)
        for sgn in (-1, 1):
            P.beam((sgn * hw, eave, z - 0.1 * d), (0.0, ridge, z - 0.1 * d), 0.25, 0.12, trim)
    # the sliding door: two leaves pushed aside on a track, white X-braced frames
    for sgn in (-1, 1):
        xa, xb = (dw / 2 + 0.1, dw / 2 + 0.1 + dw / 2) if sgn > 0 else (-dw / 2 - 0.1 - dw / 2, -dw / 2 - 0.1)
        F.box(xa, xb, 0.0, dh, z0 - 0.22, z0 - 0.08, red)
        for a, b, c, d in ((xa, xa + 0.18, 0.0, dh), (xb - 0.18, xb, 0.0, dh), (xa, xb, 0.0, 0.18), (xa, xb, dh - 0.18, dh)):
            F.box(a, b, c, d, z0 - 0.28, z0 - 0.2, trim)
        F.beam((xa + 0.1, 0.1, z0 - 0.26), (xb - 0.1, dh - 0.1, z0 - 0.26), 0.16, 0.06, trim)
        F.beam((xa + 0.1, dh - 0.1, z0 - 0.26), (xb - 0.1, 0.1, z0 - 0.26), 0.16, 0.06, trim)
    F.box(-dw - 0.4, dw + 0.4, dh + 0.15, dh + 0.35, z0 - 0.35, z0, 'steel')       # the track
    # hay-loft door high in the gable
    F.box(-1.4, 1.4, eave + 1.2, eave + 4.0, z0 - 0.1, z0 + 0.05, red)
    for a, b, c, d in ((-1.55, -1.4, eave + 1.05, eave + 4.15), (1.4, 1.55, eave + 1.05, eave + 4.15), (-1.55, 1.55, eave + 1.05, eave + 1.2),
                       (-1.55, 1.55, eave + 4.0, eave + 4.15)):
        F.box(a, b, c, d, z0 - 0.16, z0 + 0.02, trim)
    F.box(-0.15, 0.15, eave + 4.3, ridge - 0.4, z0 - 0.9, z0 - 0.6, 'steel')         # hay hood
    # side windows
    for P, x, out in ((L, x0, -1), (R, x1, 1)):
        for zc in (z0 + 5.0, (z0 + z1) / 2, z1 - 5.0):
            P.box(x + out * 0.02, x - out * 0.05, 2.0, 3.2, zc - 0.7, zc + 0.7, 'glass')
            for a, b in ((zc - 0.85, zc - 0.7), (zc + 0.7, zc + 0.85)):
                P.box(x + out * 0.08, x - out * 0.02, 1.85, 3.35, a, b, trim)
            P.box(x + out * 0.08, x - out * 0.02, 1.85, 2.0, zc - 0.85, zc + 0.85, trim)
            P.box(x + out * 0.08, x - out * 0.02, 3.2, 3.35, zc - 0.85, zc + 0.85, trim)
    # roof: corrugated metal planes with an overhang, a ridge cap
    ov = 0.8
    for sgn in (-1, 1):
        T.prism([(sgn * (hw + ov), eave - ov * tp, z0 - ov), (0.0, ridge + 0.1, z0 - ov), (0.0, ridge + 0.1, z1 + ov), (sgn * (hw + ov), eave - ov * tp, z1 + ov)],
                0.15, 'corrugated:8d9196')
    T.box(-0.3, 0.3, ridge + 0.05, ridge + 0.3, z0 - ov, z1 + ov, 'steel')
    return dict(parts=list(Pt.values()), sign=dict(y=dh + 0.5, z=z0 - 0.4, w=7.0))


def ore_refinery(v, clad='corrugated:c8743e', plant=True, hazard='accent:f2c230'):
    """ORE Refinery: a steel-framed industrial plant in rust-orange corrugated cladding on a concrete plinth, a clerestory
    band, a roller-door portal with hazard stripes, a smoke stack and an ore hopper on legs."""
    x0, x1, z0, z1, h, dw, dh = frame(v)
    Pt = venue_parts()
    base = -0.7
    frame_ = 'steel:2a2d31'
    F, B, L, R, T = (Pt[k] for k in SIDES + ('roof',))
    clere0, clere1 = h - 2.6, h - 1.0
    for side in SIDES:
        P = Pt[side]
        axis, plane, out, a0, a1 = side_frame(side, x0, x1, z0, z1)
        if axis == 'x':
            a0, a1 = z0 + 0.3, z1 - 0.3
        holes = [(-dw / 2 - 0.6, dw / 2 + 0.6, base, dh + 0.6)] if side == 'front' and dw > 0 else []
        holes.append((a0 + 1.0, a1 - 1.0, clere0, clere1))
        wall_openings(P, axis, plane, out, a0, a1, 1.4, h, 0.3, holes, clad)
        if side == 'front':
            abox(P, axis, plane, out, a0, -dw / 2 - 0.6, base, 1.4, -0.3, 0.1, 'concrete')
            abox(P, axis, plane, out, dw / 2 + 0.6, a1, base, 1.4, -0.3, 0.1, 'concrete')
        else:
            abox(P, axis, plane, out, a0, a1, base, 1.4, -0.3, 0.1, 'concrete')
        # clerestory glazing and the expressed steel frame every ~6 m
        abox(P, axis, plane, out, a0 + 1.0, a1 - 1.0, clere0, clere1, -0.2, -0.16, 'glass')
        n = max(1, int(round((a1 - a0) / 6.0)))
        for i in range(n + 1):
            a = a0 + (a1 - a0) * i / n
            if side == 'front' and abs(a) < dw / 2 + 0.8:
                continue
            abox(P, axis, plane, out, a - 0.2, a + 0.2, base, h + 0.3, 0.0, 0.35, frame_)
        m = max(1, int(round((a1 - a0 - 2.0) / 1.2)))
        for i in range(m + 1):
            a = a0 + 1.0 + (a1 - a0 - 2.0) * i / m
            abox(P, axis, plane, out, a - 0.04, a + 0.04, clere0, clere1, -0.2, 0.0, frame_)
        abox(P, axis, plane, out, a0 - 0.3, a1 + 0.3, h, h + 0.6, -0.3, 0.2, frame_)
    # the roller-door portal: steel jambs and head with yellow/black hazard bands
    if dw <= 0:
        dw = dh = 0.0
    F.box(-dw / 2 - 0.6, -dw / 2, base, dh + 0.6, z0 - 0.4, z0 + 0.3, frame_)
    F.box(dw / 2, dw / 2 + 0.6, base, dh + 0.6, z0 - 0.4, z0 + 0.3, frame_)
    F.box(-dw / 2 - 0.6, dw / 2 + 0.6, dh, dh + 0.6, z0 - 0.4, z0 + 0.3, frame_)
    for k in range(int((dw + 1.2) / 0.6) if dw > 0 else 0):
        xa = -dw / 2 - 0.6 + k * 0.6
        F.box(xa, xa + 0.6, dh + 0.05, dh + 0.5, z0 - 0.44, z0 - 0.4, hazard if k % 2 == 0 else 'steel:151515')
    if dw > 0:
        F.box(-dw / 2, dw / 2, dh - 0.9, dh, z0 + 0.0, z0 + 0.25, 'corrugated:9a9ea3')      # roller drum housing
    # roof: low-pitched corrugated planes, ridge along x
    hd = (z1 - z0) / 2
    zc = (z0 + z1) / 2
    rise = hd * math.tan(math.radians(8))
    for sgn in (-1, 1):
        T.prism([(x0 - 0.5, h + 0.6, zc + sgn * (hd + 0.5)), (x1 + 0.5, h + 0.6, zc + sgn * (hd + 0.5)), (x1 + 0.5, h + 0.6 + rise, zc), (x0 - 0.5, h + 0.6 + rise, zc)],
                0.15, 'corrugated:8d9196')
    if not plant:
        return dict(parts=list(Pt.values()), sign=None)
    # smoke stack (octagonal) and an ore hopper on legs, on the right flank
    sx, sz = x1 + 2.6, z1 - 4.0
    ring = [(sx + 1.1 * math.cos(a), 0.0, sz + 1.1 * math.sin(a)) for a in [k * math.pi / 4 for k in range(8)]]
    top = [(p[0], h + 12.0, p[2]) for p in ring]
    for i in range(8):
        j = (i + 1) % 8
        R._face([ring[j], ring[i], top[i], top[j]], 'steel:4a4d52', away=(sx, h * 0.5 + 6.0, sz))
    for y in (h + 3.0, h + 11.0):
        R.box(sx - 1.3, sx + 1.3, y, y + 0.5, sz - 1.3, sz + 1.3, 'accent:f2c230' if y > h + 5 else 'steel:2a2d31')
    hx, hz = x1 + 3.2, z0 + 6.0
    for dx in (-1.6, 1.6):
        for dz in (-1.6, 1.6):
            R.box(hx + dx - 0.15, hx + dx + 0.15, base, 6.5, hz + dz - 0.15, hz + dz + 0.15, frame_)
    R.box(hx - 2.0, hx + 2.0, 6.5, 9.5, hz - 2.0, hz + 2.0, 'corrugated:c8743e')
    R.prism([(hx - 2.0, 6.5, hz - 2.0), (hx + 2.0, 6.5, hz - 2.0), (hx + 0.4, 4.6, hz - 0.4), (hx - 0.4, 4.6, hz - 0.4)], 0.1, 'steel:4a4d52')
    R.beam((hx - 1.0, 9.0, hz + 1.0), (x1 - 0.2, h - 1.5, hz + 4.0), 1.0, 0.8, 'steel:2a2d31')          # conveyor into the plant
    return dict(parts=list(Pt.values()), sign=None)


def ratio_arena(v):
    """Ratio Arena: an open-air fight arena - a concrete ring wall with tall ribs, red and blue banners between them, a
    steel ring truss at the top and floodlight masts at the corners; no roof."""
    x0, x1, z0, z1, h, dw, dh = frame(v)
    Pt = venue_parts()
    base = -0.7
    for side in SIDES:
        P = Pt[side]
        axis, plane, out, a0, a1 = side_frame(side, x0, x1, z0, z1)
        if axis == 'x':
            a0, a1 = z0 + 0.5, z1 - 0.5
        holes = [(-dw / 2, dw / 2, base, dh)] if side == 'front' else []
        wall_openings(P, axis, plane, out, a0, a1, base, h, 0.5, holes, 'plaster:e2ded6')
        n = max(2, int(round((a1 - a0) / 2.6)))
        for i in range(n + 1):
            a = a0 + (a1 - a0) * i / n
            if side == 'front' and abs(a) < dw / 2 + 0.4:
                continue
            abox(P, axis, plane, out, a - 0.3, a + 0.3, base, h + 1.2, 0.0, 1.0, 'concrete:ffffff')
            if i < n:
                b = a0 + (a1 - a0) * (i + 1) / n
                col = 'accent:d63a4a' if (i + (0 if side in ('front', 'right') else 1)) % 2 == 0 else 'accent:3a62d6'
                if not (side == 'front' and abs((a + b) / 2) < dw / 2 + 1.0):
                    abox(P, axis, plane, out, a + 0.45, b - 0.45, h * 0.45, h - 1.2, 0.05, 0.12, col)
        abox(P, axis, plane, out, a0 - 0.6, a1 + 0.6, h, h + 0.8, -0.6, 1.1, 'steel:2b2f36')        # ring truss
        abox(P, axis, plane, out, a0 - 0.6, a1 + 0.6, -0.7, 0.6, 0.0, 1.2, 'stone')                 # plinth
    for cx, cz, side in ((x0, z0, 'front'), (x1, z0, 'front'), (x0, z1, 'back'), (x1, z1, 'back')):
        P = Pt[side]
        P.box(cx - 0.25, cx + 0.25, base, h + 9.0, cz - 0.25, cz + 0.25, 'steel:2b2f36')
        P.box(cx - 1.2, cx + 1.2, h + 8.4, h + 9.6, cz - 0.3, cz + 0.3, 'alu')
    return dict(parts=list(Pt.values()), sign=None)


def superteam_hq(v):
    """Builder HQ: a slim glass tower, violet spandrels and gold fins, a white crown (the bunting hangs from it)."""
    Pt = glass_tower(v, fh=3.6, spandrel='accent:5a3fd0', fins='alu:e8a21a', fin_every=2.6, crown='alu', pier='steel:2b2f36')
    return dict(parts=list(Pt.values()), sign=None)


def jupiter_observatory(v):
    """The observatory: a white rendered drum-base under the dome, blue-framed windows, a stone plinth."""
    Pt = masonry(v, 'plaster:f6f6f2', 'steel:4a8fc0', [6.2], 1.1, 2.6, 2.4, sill_h=1.4, plinth='stone', plinth_h=0.8,
                 cornice='concrete:eeeae2', mull=0, parapet=0.4)
    return dict(parts=list(Pt.values()), sign=None)


def seeker_shop(v):
    """The phone shop: a single-storey glass box in dark frames with a violet crown band."""
    Pt = glass_tower(v, fh=5.4, spandrel='steel:2a2a30', mframe='steel:2a2a30', crown='accent:8f5ae6', pier='steel:2a2a30')
    return dict(parts=list(Pt.values()), sign=None)


def helius_depot(v):
    """The depot: a dark corrugated shed on a concrete plinth, a clerestory band, red-banded steel frame."""
    return ore_refinery(v, clad='corrugated:4a4c52', plant=False, hazard='accent:e8402a')


# ---------------------------------------------------------------------------------------------------- The Rug Room (R3)
def rug_room(v):
    """The Rug Room (2026-10-24): a Miami Art-Deco casino pavilion at the boardwalk's arcade pier. Cream stucco over a black
    granite plinth, a stepped central tower with gold fins and the neon name board, a marquee canopy on slim gold columns
    over a plum runner, dark glazing in gold frames either side of the walk-in door (1.2 x 2.3 m, the streamed room's
    entrance), blind plum panels down the sides, gold speed lines under a stepped parapet. Neon tubes (pink and gold) are
    exported as quads in 'neon' and drawn by the sign's emissive material."""
    x0, x1, z0, z1, h, dw, dh = frame(v)
    Pt = venue_parts()
    F, B, L, R, T = Pt['front'], Pt['back'], Pt['left'], Pt['right'], Pt['roof']
    t, base = 0.4, -0.7
    cream, granite, gold, plum, glass = 'plaster:f3eadb', 'stone:3a3a40', 'alu:c9a24e', 'accent:2a0d34', 'glass'
    neon = []

    def tube(p, q, col, w=0.09, out=-0.06):
        """A neon tube from p to q (x, y) on the plane z = out (venue frame); horizontal or vertical."""
        (ax, ay, az), (bx, by, bz) = p, q
        if abs(ay - by) < 1e-3:
            neon.append(dict(c=col, q=[[ax, ay - w / 2, az + out], [ax, ay + w / 2, az + out], [bx, by + w / 2, bz + out], [bx, by - w / 2, bz + out]]))
        else:
            neon.append(dict(c=col, q=[[ax - w / 2, ay, az + out], [ax - w / 2, by, az + out], [ax + w / 2, by, az + out], [ax + w / 2, ay, az + out]]))

    # ---- walls: the front with the door and two glazed bays, sides and back blind (a casino keeps its daylight out)
    bay = (0.75, 2.6, 0.35, 3.2)
    holes = [(-dw / 2, dw / 2, base, dh), (-bay[1], -bay[0], bay[2], bay[3]), (bay[0], bay[1], bay[2], bay[3])]
    win = [(-6.9, -5.9), (-5.0, -4.0), (4.0, 5.0), (5.9, 6.9)]
    holes += [(a, b, 1.3, 6.6) for a, b in win]
    wall_openings(F, 'z', z0, -1, x0, x1, base, h, t, holes, cream)
    wall_openings(B, 'z', z1, 1, x0, x1, base, h, t, [], cream)
    wall_openings(L, 'x', x0, -1, z0 + t, z1 - t, base, h, t, [], cream)
    wall_openings(R, 'x', x1, 1, z0 + t, z1 - t, base, h, t, [], cream)
    # glazing: dark glass in gold frames (the entrance bays carry a gold transom grid)
    for a, b in ((-bay[1], -bay[0]), (bay[0], bay[1])):
        window(F, 'z', z0, -1, a, b, bay[2], bay[3], gold, glass=glass, reveal=0.2, mull=1, sill=None)
        F.box(a, b, 2.35, 2.43, z0 + 0.12, z0 + 0.2, gold)
    for a, b in win:
        window(F, 'z', z0, -1, a, b, 1.3, 6.6, gold, glass=glass, reveal=0.22, mull=0, sill=granite)
        for y in (2.9, 4.5):
            F.box(a, b, y, y + 0.07, z0 + 0.14, z0 + 0.22, gold)
    # the door's gold surround
    F.box(-dw / 2 - 0.16, -dw / 2, base, dh + 0.16, z0 - 0.1, z0 + t, gold)
    F.box(dw / 2, dw / 2 + 0.16, base, dh + 0.16, z0 - 0.1, z0 + t, gold)
    F.box(-dw / 2 - 0.16, dw / 2 + 0.16, dh, dh + 0.16, z0 - 0.1, z0 + t, gold)
    # ---- the granite plinth (left open at the door) and the corner pilasters
    for P, side in ((F, 'front'), (B, 'back'), (L, 'left'), (R, 'right')):
        axis, plane, out, a0, a1 = side_frame(side, x0, x1, z0, z1)
        if side == 'front':
            for lo, hi in ((a0 - 0.15, -bay[1]), (-bay[0], -dw / 2 - 0.16), (dw / 2 + 0.16, bay[0]), (bay[1], a1 + 0.15)):
                abox(P, axis, plane, out, lo, hi, base, 0.35 if abs(lo) < 3 and abs(hi) < 3 else 0.9, -0.05, 0.14, granite)
        else:
            abox(P, axis, plane, out, a0 - 0.15, a1 + 0.15, base, 0.9, -0.05, 0.14, granite)
        # speed lines: three gold bands under the parapet
        for y in (7.25, 7.6, 7.95):
            abox(P, axis, plane, out, a0 + 0.6, a1 - 0.6, y, y + 0.1, -0.02, 0.09, gold)
        # cornice, parapet, coping
        abox(P, axis, plane, out, a0 - 0.2, a1 + 0.2, h - 0.3, h, -0.05, 0.3, cream)
        abox(P, axis, plane, out, a0 - 0.05, a1 + 0.05, h, h + 0.7, -0.3, 0.05, cream)
        abox(P, axis, plane, out, a0 - 0.12, a1 + 0.12, h + 0.7, h + 0.82, -0.36, 0.12, gold)
    for x in (x0, x1 - 0.7):
        F.box(x, x + 0.7, base, h + 1.3, z0 - 0.22, z0 + 0.4, cream)
        F.box(x - 0.04, x + 0.74, h + 1.3, h + 1.42, z0 - 0.26, z0 + 0.44, gold)
        for y in (h + 0.25, h + 0.55):
            F.box(x - 0.02, x + 0.72, y, y + 0.08, z0 - 0.26, z0 + 0.4, gold)
    # blind plum panels in gold frames down the sides and the back
    for P, side in ((L, 'left'), (R, 'right'), (B, 'back')):
        axis, plane, out, a0, a1 = side_frame(side, x0, x1, z0, z1)
        n = 5 if axis == 'x' else 4
        span = (a1 - a0 - 2.4) / n
        for k in range(n):
            lo = a0 + 1.2 + k * span + 0.35; hi = lo + span - 0.7
            abox(P, axis, plane, out, lo, hi, 1.4, 6.6, 0.0, 0.05, plum)
            for a, b, c, d in ((lo - 0.08, lo, 1.32, 6.68), (hi, hi + 0.08, 1.32, 6.68), (lo - 0.08, hi + 0.08, 1.32, 1.4), (lo - 0.08, hi + 0.08, 6.6, 6.68)):
                abox(P, axis, plane, out, a, b, c, d, 0.0, 0.08, gold)
    # ---- the stepped central tower over the entrance (projects 0.5 m), gold fins, the name board
    tz = z0 - 0.5
    for x in (-3.3, 2.9):
        F.box(x, x + 0.4, base, 11.0, tz, z0 + 0.4, cream)                         # piers framing the entrance bay
    F.box(-3.3, 3.3, 3.45, 11.0, tz, tz + 0.5, cream)                                 # the face over the canopy (board here)
    F.box(-2.5, 2.5, 11.0, 12.4, tz + 0.4, z0 + 2.5, cream)
    F.box(-1.6, 1.6, 12.4, 13.6, tz + 0.8, z0 + 2.0, cream)
    for y, w in ((11.0, 3.3), (12.4, 2.5), (13.6, 1.6)):
        F.box(-w - 0.06, w + 0.06, y - 0.1, y + 0.02, tz - 0.06 + (0.0 if y == 11.0 else (0.4 if y == 12.4 else 0.8)), z0 + 0.6, gold)
    for k in range(5):
        x = -1.6 + k * 0.8
        F.box(x - 0.09, x + 0.09, 7.0, 12.2, tz - 0.32, tz, gold)                     # fins
    F.box(-1.9, 1.9, 6.85, 7.0, tz - 0.36, tz, gold)
    T.box(-0.08, 0.08, 13.6, 15.4, tz + 1.3, tz + 1.46, gold)                         # spire
    # ---- the marquee canopy on two gold columns, a plum fascia with gold rims, a soffit, the runner
    cz0, cz1, cy0, cy1 = z0 - 3.4, tz, 3.45, 3.95
    F.box(-3.7, 3.7, cy0, cy1, cz0, cz1, plum)
    F.box(-3.76, 3.76, cy1, cy1 + 0.07, cz0 - 0.06, cz1, gold)
    F.box(-3.76, 3.76, cy0 - 0.05, cy0, cz0 - 0.06, cz1, gold)
    F.box(-3.6, 3.6, cy0 - 0.02, cy0, cz0 + 0.1, cz1 - 0.1, 'inner')
    for x in (-3.35, 3.35):
        F.box(x - 0.11, x + 0.11, 0.0, cy0, cz0 + 0.2, cz0 + 0.42, gold)
        F.box(x - 0.2, x + 0.2, 0.0, 0.12, cz0 + 0.11, cz0 + 0.51, granite)
    F.box(-0.95, 0.95, 0.0, 0.025, cz0 + 0.1, z0, 'accent:5c0f30')
    # ---- the roof: slab, plant boxes
    T.box(x0 + t, x1 - t, h - 0.4, h + 0.15, z0 + t, z1 - t, 'concrete')
    roof_plant(T, h, [(-5.5, -3.0, 4.0, 6.5, 1.4, 'steel'), (2.5, 5.5, 3.0, 5.0, 1.1, 'alu'), (-1.0, 1.0, 7.0, 8.5, 0.9, 'steel')])
    # ---- neon: pink round the canopy fascia and the tower's steps, gold up the piers and the pilasters
    pk, gd = 'pink', 'gold'
    tube((-3.7, cy0 + 0.25, cz0), (3.7, cy0 + 0.25, cz0), pk, out=-0.08)
    tube((-3.7, cy1 - 0.08, cz0), (3.7, cy1 - 0.08, cz0), pk, w=0.05, out=-0.08)
    for x in (-3.1, 3.1):
        tube((x, 3.95, tz), (x, 10.9, tz), gd)
    for y, w, dz in ((11.08, 3.3, 0.0), (12.48, 2.5, 0.4), (13.68, 1.6, 0.8)):
        tube((-w, y, tz + dz), (w, y, tz + dz), pk, w=0.07, out=-0.12)
    for x in (x0 + 0.35, x1 - 0.35):
        tube((x, 1.0, z0 - 0.22), (x, h + 1.25, z0 - 0.22), gd, w=0.07)
    return dict(parts=[F, B, L, R, T], sign=dict(y=4.6, z=tz - 0.06, w=6.0), neon=neon)


LANDMARKS = {'superteam_hq': superteam_hq, 'jupiter_observatory': jupiter_observatory, 'seeker_shop': seeker_shop, 'helius_depot': helius_depot,
             'rug_room': rug_room}


BUILDERS = {'conference_hall': conference_hall, 'perp_pit': perp_pit, 'hackathon_house': hackathon_house, 'prediction_pit': prediction_pit,
            'ct_jail': ct_jail, 'rekt_clinic': rekt_clinic, 'audit_row': audit_row, 'spaces_studio': spaces_studio, 'terminal_cafe': terminal_cafe,
            'launchpad_tower': launchpad_tower, 'discord_heights': discord_heights, 'vc_row': vc_row, 'cabal_lounge': cabal_lounge,
            'sybil_barn': sybil_barn, 'ore_refinery': ore_refinery, 'ratio_arena': ratio_arena}


def main():
    plan = json.load(open(os.path.abspath(os.path.join(HERE, '..', '..', 'tools', 'world', 'islandgen', 'out', 'plan.json'))))
    venues = {v['id']: v for v in plan['venues']}
    for lb in plan.get('landmarkBuildings', []):
        if lb['id'] in LANDMARKS:
            sx, sz = lb['size']
            venues[lb['id']] = dict(id=lb['id'], frame_min=[-sx / 2, -sz / 2], frame_size=[sx, sz], walk=[-sx / 2 + 1, -sz / 2 + 0.7, sx / 2 - 1, sz / 2 - 1],
                                    shell=dict(h=lb['h']), nodoor=True, walkin=lb.get('walkin'))
    BUILDERS.update(LANDMARKS)
    ids = [a for a in ARGS if a in BUILDERS] or list(BUILDERS)
    os.makedirs(OUT, exist_ok=True)
    global LOD_MODE
    for vid in ids:
        LOD_MODE = True
        lod1 = {}
        for p in BUILDERS[vid](venues[vid])['parts']:
            d = export_part(p)
            if d['v']:
                lod1[p.name] = d
        LOD_MODE = False
        res = BUILDERS[vid](venues[vid])
        parts = {}
        tris = 0
        for p in res['parts']:
            d = export_part(p)
            if d['v']:
                parts[p.name] = d
                tris += sum(len(t) for t in d['sub'].values()) // 3
        x0, x1, z0, z1, h, dw, dh = frame(venues[vid])
        tris1 = sum(sum(len(t) for t in d['sub'].values()) // 3 for d in lod1.values())
        out = dict(venue=vid, footprint=[x0, x1, z0, z1], height=h, door=[dw, dh], tris=tris, tris_lod1=tris1, parts=parts, lod1=lod1, sign=res['sign'],
                   neon=res.get('neon'),
                   note='art/pipeline/world_venue_shells.py (world fix-ups 2026-10-23)')
        json.dump(out, open(os.path.join(OUT, vid + '.json'), 'w'), separators=(',', ':'))
        print('[venues]', vid, 'tris', tris, 'lod1', tris1, {k: sum(len(t) for t in d['sub'].values()) // 3 for k, d in parts.items()}, flush=True)
    if '--sheet' in ARGS:
        sheet(ids, ARGS[ARGS.index('--sheet') + 1])


# ---------------------------------------------------------------------------------------------------- lookdev sheet
COLS = {'glass': (0.05, 0.08, 0.11, 0.95), 'steel': (0.06, 0.065, 0.07, 0.5), 'alu': (0.78, 0.79, 0.8, 0.6), 'concrete': (0.42, 0.40, 0.37, 0.2),
        'accent': (0.1, 0.8, 0.45, 0.5), 'timber_dark': (0.30, 0.22, 0.15, 0.1), 'timber_light': (0.62, 0.47, 0.30, 0.2),
        'wood_post': (0.33, 0.24, 0.16, 0.1), 'slate': (0.26, 0.26, 0.27, 0.2), 'thatch': (0.55, 0.45, 0.30, 0.05), 'stone': (0.45, 0.40, 0.33, 0.2),
        'inner': (0.9, 0.86, 0.8, 0.2), 'paving': (0.6, 0.58, 0.55, 0.2), 'plaster': (0.8, 0.78, 0.74, 0.2),
        'corrugated': (0.6, 0.6, 0.62, 0.4), 'siding': (0.5, 0.4, 0.3, 0.2)}


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
                if mk not in mats:
                    base, _, hx = mk.partition(':')
                    c = COLS.get(base, (0.6, 0.6, 0.6, 0.2))
                    m = bpy.data.materials.new(mk); m.use_nodes = True
                    b = m.node_tree.nodes['Principled BSDF']
                    col = tuple(int(hx[i:i + 2], 16) / 255.0 * (c[i // 2] if False else 1.0) for i in (0, 2, 4)) if hx else c[:3]
                    b.inputs['Base Color'].default_value = col + (1,)
                    b.inputs['Roughness'].default_value = 1 - c[3]
                    mats[mk] = m
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
