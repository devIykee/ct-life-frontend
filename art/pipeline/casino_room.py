"""The Rug Room casino floor (R3, 2026-10-24): a walk-in room built here (no Sketchfab room), lit and baked like
world_v3_interior.py's rooms, plus an unbaked 'props' mesh for the glowing parts.

Usage: art/pipeline/run.sh art/pipeline/casino_room.py [-- --samples N] [--preview out.png]
In:  art/processed/world_v3/v3_casino (the retextured slot pair, poker / blackjack tables, roulette wheel),
     art/processed/world_v3/v3_furniture (Poly Haven sofa, lounge chairs, coffee table, plants),
     art/raw/polyhaven/tex_* (rosewood veneer, velour velvet, marble 01: CC0), art/source/casino/out/png (carpet, signs)
Out: art/processed/world_v3/v3_interiors/rug_room.fbx + textures/rug_room_baked.png (the room: one material, Interior Baked),
     rug_room_props.fbx (slot machines + signs: their own atlases x baked vertex light, vertex alpha = glow),
     manifest entry 'rug_room' (kind interior_room, colliders, seats, spots, props)

Room (Blender: x right seen from the door, y into the room, z up; origin = the entrance's floor centre):
  12.8 x 16 m, 4.6 m ceiling; a 0.35 m front wall with the 1.1 x 2.2 m entrance; a marble foyer, then deco carpet;
  rosewood wainscot under plum velvet with rosewood pilasters and brass rails; a coffered ceiling with warm coves and a
  pink neon line round the top of the walls.
  Left wall: a bank of six slot machines (three retextured pairs) under a SLOTS sign      -> spot 'slots'
  Right wall: the Moon or Rug screen over a console                                         -> spot 'crash'
  Centre: the coinflip table (the blackjack table) under a COINFLIP sign and a pendant      -> spot 'coinflip'
  Back: roulette and poker tables (decor) under pendants, the neon wordmark board, a bar with backlit shelves (no cashier,
  no cash-out anything) and a lounge corner. The disclaimer plaque "Play money. No cash value. 18+" by the entrance.
"""
import bpy, bmesh, sys, os, json, math, time
sys.path.insert(0, os.path.dirname(__file__))
import numpy as np
from mathutils import Vector, Matrix
import world_v3_lib as W
import world_v3_interior as WI

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
PROC = os.path.join(REPO, 'art', 'processed', 'world_v3')
OUT = os.path.join(PROC, 'v3_interiors')
PNG = os.path.join(REPO, 'art', 'source', 'casino', 'out', 'png')
PH = os.path.join(REPO, 'art', 'raw', 'polyhaven')
RID = 'rug_room'
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []

XW, YD, ZH = 6.4, 16.0, 4.6          # half width, depth, ceiling
FRONT_T = 0.35                       # front wall thickness (the building's glazed bays sit inside it)
DOOR_W, DOOR_H = 1.1, 2.2
SCONCES = (2.2, 5.2, 8.2, 11.2, 14.2)   # pilaster y (sconces on both side walls)
LIGHT_SCALE = 0.5                    # props: vertex light stored x0.5 (the shader doubles it)


def log(*a):
    print('[rug_room]', *a, flush=True)


# ------------------------------------------------------------------ materials
MATS = {}


def mat_img(name, img_path, tile, tint=(1, 1, 1), rough=0.6, emit=None):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes['Principled BSDF']
    b.inputs['Roughness'].default_value = rough
    if img_path:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(img_path)
        uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = 'UVMap'
        mp = nt.nodes.new('ShaderNodeMapping'); mp.inputs['Scale'].default_value = (1 / tile, 1 / tile, 1)
        nt.links.new(uvn.outputs['UV'], mp.inputs['Vector']); nt.links.new(mp.outputs['Vector'], t.inputs['Vector'])
        if tint != (1, 1, 1):
            mx = nt.nodes.new('ShaderNodeVectorMath'); mx.operation = 'MULTIPLY'
            nt.links.new(t.outputs['Color'], mx.inputs[0]); mx.inputs[1].default_value = tint
            nt.links.new(mx.outputs[0], b.inputs['Base Color'])
        else:
            nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    else:
        b.inputs['Base Color'].default_value = tint + (1,)
    if emit:
        b.inputs['Emission Color'].default_value = emit[0] + (1,)
        b.inputs['Emission Strength'].default_value = emit[1]
    MATS[name] = m
    return m


def setup_materials():
    mat_img('carpet', os.path.join(PNG, 'carpet.png'), 2.0, rough=0.95)
    mat_img('marble', os.path.join(PH, 'tex_marble_01', 'marble_01_diff_2k.jpg'), 2.4, tint=(0.95, 0.93, 0.9), rough=0.25)
    mat_img('wood', os.path.join(PH, 'tex_rosewood_veneer1', 'rosewood_veneer1_diff_2k.jpg'), 1.4, tint=(0.75, 0.6, 0.55), rough=0.35)
    mat_img('velvet', os.path.join(PH, 'tex_velour_velvet', 'velour_velvet_diff_2k.jpg'), 1.2, tint=(0.12, 0.025, 0.08), rough=0.9)
    mat_img('felt', None, 1, tint=(0.05, 0.28, 0.17), rough=0.95)
    mat_img('brass', None, 1, tint=(0.72, 0.53, 0.24), rough=0.3)
    mat_img('ceiling', None, 1, tint=(0.05, 0.03, 0.05), rough=0.9)
    mat_img('gold_leaf', None, 1, tint=(0.10, 0.05, 0.06), rough=0.4)
    mat_img('dark', None, 1, tint=(0.03, 0.025, 0.03), rough=0.5)
    mat_img('leather', None, 1, tint=(0.09, 0.03, 0.04), rough=0.5)
    mat_img('cove', None, 1, tint=(0, 0, 0), emit=(WI.kelvin_rgb(2500), 1.4))
    mat_img('neon_pink', None, 1, tint=(0, 0, 0), emit=((1.0, 0.18, 0.55), 8.0))
    mat_img('neon_gold', None, 1, tint=(0, 0, 0), emit=((1.0, 0.72, 0.3), 10.0))
    mat_img('lamp', None, 1, tint=(0, 0, 0), emit=(WI.kelvin_rgb(2700), 6.0))
    mat_img('shelf_glow', None, 1, tint=(0, 0, 0), emit=((1.0, 0.5, 0.18), 0.3))


# ------------------------------------------------------------------ geometry (one bmesh for the room shell and built pieces)
class Builder:
    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new('UVMap')
        self.names = []

    def mi(self, name):
        if name not in self.names:
            self.names.append(name)
        return self.names.index(name)

    def quad(self, pts, mat, uv=None):
        vs = [self.bm.verts.new(p) for p in pts]
        f = self.bm.faces.new(vs)
        f.material_index = self.mi(mat)
        f.normal_update()
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for k, l in enumerate(f.loops):
            if uv is not None:
                l[self.uv].uv = uv[k]
            else:
                c = l.vert.co
                l[self.uv].uv = (c.y, c.z) if ax == 0 else ((c.x, c.z) if ax == 1 else (c.x, c.y))
        return f

    def box(self, x0, x1, y0, y1, z0, z1, mat, skip=()):
        x0, x1 = min(x0, x1), max(x0, x1); y0, y1 = min(y0, y1), max(y0, y1); z0, z1 = min(z0, z1), max(z0, z1)
        F = {
            '-x': [(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)],
            '+x': [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)],
            '-y': [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)],
            '+y': [(x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0)],
            '-z': [(x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0)],
            '+z': [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)],
        }
        for k, pts in F.items():
            if k not in skip:
                self.quad(pts, mat)

    def cyl(self, x, y, z0, z1, r, mat, n=12, caps=True):
        ring0 = [(x + r * math.cos(2 * math.pi * i / n), y + r * math.sin(2 * math.pi * i / n)) for i in range(n)]
        for i in range(n):
            a, b = ring0[i], ring0[(i + 1) % n]
            self.quad([(a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1)], mat)
        if caps:
            f = self.bm.faces.new([self.bm.verts.new((p[0], p[1], z1)) for p in ring0]); f.material_index = self.mi(mat)
            for l in f.loops:
                l[self.uv].uv = (l.vert.co.x, l.vert.co.y)

    def to_object(self, name):
        me = bpy.data.meshes.new(name)
        bmesh.ops.recalc_face_normals(self.bm, faces=[])     # keep the authored winding
        self.bm.to_mesh(me); self.bm.free()
        for n in self.names:
            me.materials.append(MATS[n])
        o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
        return o


def shell(B):
    """Walls (inner faces + the door's jambs), floor, coffered ceiling, wainscot, pilasters, rails, coves, neon."""
    x0, x1 = -XW, XW
    # floor: marble foyer, carpet beyond (faces up)
    B.quad([(x0, FRONT_T, 0), (x1, FRONT_T, 0), (x1, 3.0, 0), (x0, 3.0, 0)], 'marble')
    B.quad([(x0, 3.0, 0), (x1, 3.0, 0), (x1, YD, 0), (x0, YD, 0)], 'carpet')
    B.box(-2.0, 2.0, 2.98, 3.02, 0.0, 0.012, 'brass', skip=('-z',))
    # front wall (inner face at y = FRONT_T) with the entrance, jambs and head
    def wall_y(y, xa, xb, za, zb, mat, facing):
        pts = [(xa, y, za), (xb, y, za), (xb, y, zb), (xa, y, zb)]
        B.quad(pts if facing < 0 else pts[::-1], mat)
    def wall_x(x, ya, yb, za, zb, mat, facing):
        pts = [(x, ya, za), (x, yb, za), (x, yb, zb), (x, ya, zb)]
        B.quad(pts if facing > 0 else pts[::-1], mat)
    dx = DOOR_W / 2
    bands = [(0.0, 1.1, 'wood'), (1.1, 1.16, 'brass'), (1.16, 3.9, 'velvet'), (3.9, 4.0, 'brass'), (4.0, ZH, 'ceiling')]
    for za, zb, m in bands:
        # front (faces +y): split round the door
        for xa, xb in ((x0, -dx), (dx, x1)):
            wall_y(FRONT_T, xa, xb, za, zb, m, +1)
        if za >= DOOR_H:
            wall_y(FRONT_T, -dx, dx, za, zb, m, +1)
        elif zb > DOOR_H:
            wall_y(FRONT_T, -dx, dx, DOOR_H, zb, m, +1)
        wall_y(YD, x0, x1, za, zb, m, -1)                 # back (faces -y)
        wall_x(x0, FRONT_T, YD, za, zb, m, +1)            # left (faces +x)
        wall_x(x1, FRONT_T, YD, za, zb, m, -1)            # right (faces -x)
    # the entrance: jambs, head, the front wall's outer face round the opening (seen from the street through the leaf)
    B.quad([(-dx, 0, 0), (-dx, FRONT_T, 0), (-dx, FRONT_T, DOOR_H), (-dx, 0, DOOR_H)], 'wood')
    B.quad([(dx, FRONT_T, 0), (dx, 0, 0), (dx, 0, DOOR_H), (dx, FRONT_T, DOOR_H)], 'wood')
    B.quad([(-dx, 0, DOOR_H), (-dx, FRONT_T, DOOR_H), (dx, FRONT_T, DOOR_H), (dx, 0, DOOR_H)], 'wood')
    B.quad([(-dx, 0, 0), (dx, 0, 0), (dx, FRONT_T, 0), (-dx, FRONT_T, 0)], 'marble')
    for xa, xb in ((-1.4, -dx), (dx, 1.4)):
        B.quad([(xa, 0, 0), (xa, 0, 2.6), (xb, 0, 2.6), (xb, 0, 0)][::-1], 'dark')
    B.quad([(-dx, 0, DOOR_H), (-dx, 0, 2.6), (dx, 0, 2.6), (dx, 0, DOOR_H)][::-1], 'dark')
    # brass door surround inside
    B.box(-dx - 0.12, -dx, FRONT_T, FRONT_T + 0.04, 0, DOOR_H + 0.12, 'brass', skip=('-y',))
    B.box(dx, dx + 0.12, FRONT_T, FRONT_T + 0.04, 0, DOOR_H + 0.12, 'brass', skip=('-y',))
    B.box(-dx - 0.12, dx + 0.12, FRONT_T, FRONT_T + 0.04, DOOR_H, DOOR_H + 0.12, 'brass', skip=('-y',))
    # sconces on the pilasters (frosted emissive shades on brass plates; their point lights are in add_lights)
    for y in SCONCES:
        for x, sx in ((x0, 1), (x1, -1)):
            if sx < 0 and 5.0 < y < 9.5:
                continue                      # the Moon or Rug screen hangs there
            B.box(x, x + sx * 0.1, y - 0.07, y + 0.07, 2.35, 2.85, 'brass', skip=('-x' if sx > 0 else '+x',))
            B.box(x + sx * 0.1, x + sx * 0.24, y - 0.09, y + 0.09, 2.5, 2.78, 'lamp')
    # pilasters (rosewood, brass caps) along the side walls and the back
    for y in np.arange(2.2, YD - 1.0, 3.0):
        for x, sx in ((x0, 1), (x1, -1)):
            if sx < 0 and 5.5 < y < 9.3:
                continue                      # the Moon or Rug screen and its header
            B.box(x, x + sx * 0.08, y - 0.18, y + 0.18, 0, 3.9, 'wood', skip=('-x' if sx > 0 else '+x',))
            B.box(x, x + sx * 0.11, y - 0.22, y + 0.22, 3.75, 3.9, 'brass', skip=('-x' if sx > 0 else '+x',))
    # ceiling: a dark lid at ZH with three coffers (recessed 0.35 m) with warm coves; pendant fixtures hang from it
    cof = [(-2.6, 2.6, 4.0, 7.6), (-2.6, 2.6, 8.4, 12.0), (-2.6, 2.6, 12.8, 15.4)]
    # lid split round the coffers (faces down)
    ys = [FRONT_T] + sum([[c[2], c[3]] for c in cof], []) + [YD]
    for i in range(len(ys) - 1):
        ya, yb = ys[i], ys[i + 1]
        inside = any(abs(ya - c[2]) < 1e-6 for c in cof)
        if inside:
            B.quad([(x0, ya, ZH), (-2.6, ya, ZH), (-2.6, yb, ZH), (x0, yb, ZH)][::-1], 'ceiling')
            B.quad([(2.6, ya, ZH), (x1, ya, ZH), (x1, yb, ZH), (2.6, yb, ZH)][::-1], 'ceiling')
        else:
            B.quad([(x0, ya, ZH), (x1, ya, ZH), (x1, yb, ZH), (x0, yb, ZH)][::-1], 'ceiling')
    for cx0, cx1, cy0, cy1 in cof:
        top = ZH + 0.35
        B.quad([(cx0, cy0, top), (cx1, cy0, top), (cx1, cy1, top), (cx0, cy1, top)][::-1], 'gold_leaf')
        for (a, b) in (((cx0, cy0), (cx1, cy0)), ((cx1, cy0), (cx1, cy1)), ((cx1, cy1), (cx0, cy1)), ((cx0, cy1), (cx0, cy0))):
            B.quad([(a[0], a[1], ZH), (b[0], b[1], ZH), (b[0], b[1], top), (a[0], a[1], top)][::-1], 'ceiling')
        # cove: an emissive strip just inside each coffer's lip, hidden from below by a brass fascia
        B.box(cx0 + 0.05, cx1 - 0.05, cy0 + 0.05, cy0 + 0.12, ZH + 0.02, ZH + 0.08, 'cove')
        B.box(cx0 + 0.05, cx1 - 0.05, cy1 - 0.12, cy1 - 0.05, ZH + 0.02, ZH + 0.08, 'cove')
        B.box(cx0 + 0.05, cx0 + 0.12, cy0 + 0.05, cy1 - 0.05, ZH + 0.02, ZH + 0.08, 'cove')
        B.box(cx1 - 0.12, cx1 - 0.05, cy0 + 0.05, cy1 - 0.05, ZH + 0.02, ZH + 0.08, 'cove')
        # the brass fascia round the coffer's lip: a frame (four bars), not a slab across the opening
        for bx0_, bx1_, by0_, by1_ in ((cx0 - 0.08, cx1 + 0.08, cy0 - 0.08, cy0 + 0.04), (cx0 - 0.08, cx1 + 0.08, cy1 - 0.04, cy1 + 0.08),
                                       (cx0 - 0.08, cx0 + 0.04, cy0 + 0.04, cy1 - 0.04), (cx1 - 0.04, cx1 + 0.08, cy0 + 0.04, cy1 - 0.04)):
            B.box(bx0_, bx1_, by0_, by1_, ZH - 0.12, ZH, 'brass', skip=('+z',))
    # pink neon line round the top of the walls, a gold one along the rail of the back wall
    B.box(x0 + 0.02, x0 + 0.06, FRONT_T, YD, 4.22, 4.27, 'neon_pink')
    B.box(x1 - 0.06, x1 - 0.02, FRONT_T, YD, 4.22, 4.27, 'neon_pink')
    B.box(x0, x1, YD - 0.06, YD - 0.02, 4.22, 4.27, 'neon_pink')
    B.box(x0, x1, FRONT_T + 0.02, FRONT_T + 0.06, 4.22, 4.27, 'neon_pink')


def furniture(B):
    """Built pieces: the crash console, the roulette table, the bar, stools, pendants, the sign backings."""
    # crash console under the screen on the right wall
    B.box(XW - 1.2, XW, 5.9, 8.9, 0.0, 0.95, 'wood', skip=('+x',))
    B.box(XW - 1.25, XW, 5.85, 8.95, 0.95, 1.0, 'brass', skip=('+x',))
    B.box(XW - 1.25, XW - 1.2, 5.9, 8.9, 0.1, 0.13, 'neon_gold')
    # screen bezel (the screen itself is a props quad)
    B.box(XW - 0.08, XW, 5.7, 9.1, 1.0, 3.0, 'dark', skip=('+x',))
    B.box(XW - 0.1, XW, 5.65, 9.15, 3.0, 3.06, 'brass', skip=('+x',))
    # roulette table: a rosewood body with a green felt top and a brass rim; the wheel sits on its right end
    rx, ry = -2.7, 12.2
    B.box(rx - 1.3, rx + 1.3, ry - 0.62, ry + 0.62, 0.0, 0.70, 'wood')
    B.box(rx - 1.36, rx + 1.36, ry - 0.68, ry + 0.68, 0.70, 0.78, 'leather')
    B.box(rx - 1.2, rx + 1.2, ry - 0.52, ry + 0.52, 0.78, 0.785, 'felt', skip=('-z',))
    B.box(rx - 1.0, rx + 0.4, ry - 0.38, ry + 0.38, 0.785, 0.788, 'brass', skip=('-z',))
    B.box(rx - 0.97, rx + 0.37, ry - 0.35, ry + 0.35, 0.788, 0.79, 'felt', skip=('-z',))
    # the bar along the back wall's right half: rosewood front, marble top, backlit shelves, a brass foot rail
    bx0, bx1 = 2.2, XW - 0.2
    B.box(bx0, bx1, YD - 1.8, YD - 1.15, 0.0, 1.05, 'wood')
    B.box(bx0 - 0.05, bx1, YD - 1.9, YD - 1.1, 1.05, 1.1, 'marble')
    B.box(bx0, bx1, YD - 1.85, YD - 1.8, 0.12, 0.16, 'neon_gold')
    for k, z in enumerate((1.3, 1.85, 2.4)):
        B.box(bx0 + 0.2, bx1 - 0.2, YD - 0.3, YD, z, z + 0.04, 'wood', skip=('+y',))
    B.box(bx0 + 0.2, bx1 - 0.2, YD - 0.04, YD - 0.01, 1.2, 2.9, 'shelf_glow')
    for i, x in enumerate(np.arange(bx0 + 0.45, bx1 - 0.3, 0.32)):       # bottles: dark glass silhouettes on the shelves
        for z in (1.34, 1.89):
            hgt = 0.26 + 0.06 * ((i * 7 + int(z * 10)) % 3)
            B.box(x - 0.04, x + 0.04, YD - 0.2, YD - 0.12, z, z + hgt, 'dark')
    for x in (3.0, 4.2, 5.4):                                              # stools
        B.cyl(x, YD - 2.35, 0.0, 0.72, 0.05, 'brass', n=8, caps=False)
        B.cyl(x, YD - 2.35, 0.72, 0.8, 0.22, 'leather', n=12)
        B.cyl(x, YD - 2.35, 0.0, 0.03, 0.22, 'brass', n=12)
    # pendants over the three tables: drum shades with an emissive underside, on brass rods
    for (px, py) in ((0.0, 9.0), (-2.7, 12.2), (2.9, 12.4)):
        B.box(px - 0.75, px + 0.75, py - 0.3, py + 0.3, 2.65, 2.9, 'dark', skip=('-z',))
        B.quad([(px - 0.72, py - 0.27, 2.66), (px - 0.72, py + 0.27, 2.66), (px + 0.72, py + 0.27, 2.66), (px + 0.72, py - 0.27, 2.66)], 'lamp')
        B.box(px - 0.76, px + 0.76, py - 0.31, py + 0.31, 2.62, 2.66, 'brass', skip=('+z',))
        for sx in (-0.5, 0.5):
            B.box(px + sx - 0.012, px + sx + 0.012, py - 0.012, py + 0.012, 2.9, ZH, 'brass')
    # COINFLIP sign hangs on rods over the coinflip table (its face is a props quad); SLOTS sign backing on the left wall
    B.box(-1.05, 1.05, 9.0, 9.06, 3.25, 4.31, 'dark')
    for sx in (-0.9, 0.9):
        B.box(sx - 0.012, sx + 0.012, 9.02, 9.04, 4.31, ZH, 'brass')
    B.box(-XW, -XW + 0.06, 6.2, 8.4, 2.85, 3.95, 'dark', skip=('-x',))
    # the wordmark board's frame on the back wall
    B.box(-2.15, 2.15, YD - 0.1, YD, 2.25, 3.95, 'dark', skip=('+y',))
    B.box(-2.2, 2.2, YD - 0.12, YD, 2.2, 2.25, 'brass', skip=('+y',))
    B.box(-2.2, 2.2, YD - 0.12, YD, 3.95, 4.0, 'brass', skip=('+y',))


# ------------------------------------------------------------------ imported props
def import_fbx(path, at, yaw_deg, name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == 'MESH']
    keep = [o for o in meshes if not o.name.endswith('_LOD1') and not o.name.endswith('_LOD1.001')]
    for o in new:
        if o not in keep:
            bpy.data.objects.remove(o)
    out = []
    for o in keep:
        o.parent = None
        bpy.context.view_layer.update()
        o.data = o.data.copy()
        o.data.transform(o.matrix_world); o.matrix_world = Matrix.Identity(4)
        o.data.transform(Matrix.Translation(Vector(at)) @ Matrix.Rotation(math.radians(yaw_deg), 4, 'Z'))
        o.name = name
        out.append(o)
    return out


def atlas_material(name, tex, blend=False):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes['Principled BSDF']
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(tex)
    uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = 'UVMap'
    nt.links.new(uvn.outputs['UV'], t.inputs['Vector'])
    nt.links.new(t.outputs['Color'], b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = 0.5
    if blend:
        nt.links.new(t.outputs['Alpha'], b.inputs['Alpha'])
    return m


def kit_prop(fam, pid, at, yaw):
    path = os.path.join(PROC, fam, pid + '.fbx')
    objs = import_fbx(path, at, yaw, pid)
    tex = os.path.join(PROC, fam, 'textures', pid.rsplit('_', 0)[0] + '_BaseColor.png')
    man = json.load(open(os.path.join(PROC, fam, 'manifest.json')))
    tname = man[pid]['textures'][0]
    tex = os.path.join(PROC, fam, 'textures', tname)
    for o in objs:
        for sl in o.material_slots:
            sl.material = atlas_material('M_' + pid, tex)
        if not o.data.uv_layers:
            o.data.uv_layers.new(name='UVMap')
        o.data.uv_layers[0].name = 'UVMap'
    return objs


def place_props():
    parts = []
    # coinflip table (the blackjack table, players' arc towards the door), poker table, roulette wheel on its table
    parts += kit_prop('v3_casino', 'cs_blackjack_table', (0.0, 9.2, 0.0), 0)
    parts += kit_prop('v3_casino', 'cs_poker_table', (2.9, 12.4, 0.0), 0)
    parts += kit_prop('v3_casino', 'cs_roulette_table', (-2.7 + 0.85, 12.2, 0.79), 0)
    # lounge corner (back left): sofa against the left wall, two lounge chairs, a round coffee table; plants
    # the sofa against the back wall facing the room (fronts face -y as processed), two lounge chairs facing it
    parts += kit_prop('v3_furniture', 'fu_sofa_chesterfield', (-4.2, YD - 0.55, 0.0), 0)
    parts += kit_prop('v3_furniture', 'fu_lounge_chair', (-5.3, YD - 2.3, 0.0), 160)
    parts += kit_prop('v3_furniture', 'fu_lounge_chair', (-3.1, YD - 2.3, 0.0), 200)
    parts += kit_prop('v3_furniture', 'fu_coffee_round', (-4.2, YD - 1.55, 0.0), 0)
    for p in ((-5.9, 0.9), (5.9, 0.9), (-5.9, 11.0), (5.9, 10.4)):
        parts += kit_prop('v3_furniture', 'fu_plant_aloe', (p[0], p[1], 0.0), 0)
    return parts


def slot_bank():
    """Three retextured slot pairs along the left wall, facing +x (their fronts face -y as processed)."""
    objs = []
    for y in (5.2, 7.3, 9.4):
        objs += import_fbx(os.path.join(PROC, 'v3_casino', 'cs_slot_machine.fbx'), (-XW + 0.5, y, 0.0), 90, 'slot')
    tex = os.path.join(PROC, 'v3_casino', 'textures', 'cs_slot_machine_BaseColor.png')
    for o in objs:
        for sl in o.material_slots:
            sl.material = atlas_material('M_rr_slots', tex, blend=False)
        o.data.uv_layers[0].name = 'UVMap'
    return objs


def signs():
    """Sign faces on the signs atlas (art/source/casino rr_signs, 2048 x 1024; v up)."""
    B = Builder()
    m = 'rr_signs'
    def U(px, py):
        return (px / 2048.0, 1.0 - py / 1024.0)
    def face(pts, box):
        x0, y0, x1, y1 = box
        B.quad(pts, m, uv=[U(x0, y1), U(x1, y1), U(x1, y0), U(x0, y0)])
    # the wordmark board on the back wall (faces -y): seen from the room, left = -x
    face([(-2.1, YD - 0.11, 2.3), (2.1, YD - 0.11, 2.3), (2.1, YD - 0.11, 3.9), (-2.1, YD - 0.11, 3.9)], (0, 0, 1024, 384))
    # the Moon or Rug screen on the right wall (faces -x), header above it
    face([(XW - 0.09, 9.0, 1.08), (XW - 0.09, 5.8, 1.08), (XW - 0.09, 5.8, 2.92), (XW - 0.09, 9.0, 2.92)], (1024, 0, 2048, 576))
    face([(XW - 0.02, 9.0, 3.15), (XW - 0.02, 5.8, 3.15), (XW - 0.02, 5.8, 3.95), (XW - 0.02, 9.0, 3.95)], (1024, 576, 2048, 832))
    # the disclaimer plaque right of the entrance (faces +y)
    face([(2.6, FRONT_T + 0.02, 1.3), (1.4, FRONT_T + 0.02, 1.3), (1.4, FRONT_T + 0.02, 1.6), (2.6, FRONT_T + 0.02, 1.6)], (0, 384, 1024, 640))
    # SLOTS over the slot bank (faces +x), COINFLIP both faces of the hanging sign
    face([(-XW + 0.07, 6.3, 2.9), (-XW + 0.07, 8.3, 2.9), (-XW + 0.07, 8.3, 3.9), (-XW + 0.07, 6.3, 3.9)], (0, 640, 512, 896))
    face([(-1.0, 8.99, 3.3), (1.0, 8.99, 3.3), (1.0, 8.99, 4.26), (-1.0, 8.99, 4.26)], (512, 640, 1024, 896))
    face([(1.0, 9.07, 3.3), (-1.0, 9.07, 3.3), (-1.0, 9.07, 4.26), (1.0, 9.07, 4.26)], (512, 640, 1024, 896))
    MATS[m] = atlas_material('M_rr_signs', os.path.join(PNG, 'rr_signs.png'))
    return B.to_object('signs')


# ------------------------------------------------------------------ lights
def add_lights():
    sc = bpy.context.scene
    def area(name, loc, size, watts, k, rot=(0, 0, 0), sy=None, col=None):
        L = bpy.data.lights.new(name, 'AREA'); L.size = size; L.energy = watts
        if sy:
            L.shape = 'RECTANGLE'; L.size_y = sy
        L.color = col or WI.kelvin_rgb(k)
        o = bpy.data.objects.new(name, L); sc.collection.objects.link(o); o.location = loc; o.rotation_euler = rot
        o.visible_camera = False
    # warm pools from the pendants over the tables
    for i, (px, py) in enumerate(((0.0, 9.0), (-2.7, 12.2), (2.9, 12.4))):
        area(f'pend{i}', (px, py, 2.6), 1.3, 480, 2500, sy=0.5)
    # coffers: soft downlight
    # wall sconces: warm scallops up the velvet
    for i, y in enumerate(SCONCES):
        for x, sx in ((-XW, 1), (XW, -1)):
            if sx < 0 and 5.0 < y < 9.5:
                continue
            L = bpy.data.lights.new(f'sconce{i}{sx}', 'POINT'); L.energy = 70; L.shadow_soft_size = 0.06; L.color = WI.kelvin_rgb(2400)
            o = bpy.data.objects.new(L.name, L); sc.collection.objects.link(o); o.location = (x + sx * 0.22, y, 2.62); o.visible_camera = False
    # the foyer
    area('foyer', (0.0, 1.8, ZH - 0.05), 2.0, 170, 2900, sy=1.6)
    # the slot bank's own glow and the screen's spill
    area('slots', (-4.4, 7.3, 2.2), 1.2, 160, 0, rot=(0, math.radians(-90), 0), sy=5.0, col=(1.0, 0.45, 0.75))
    area('screen', (XW - 0.5, 7.4, 2.0), 1.6, 150, 0, rot=(0, math.radians(90), 0), sy=3.0, col=(0.45, 0.75, 1.0))
    # the bar
    area('bar', (4.2, YD - 1.5, 2.9), 1.0, 160, 2600, sy=4.0)
    area('lounge', (-4.2, YD - 1.6, 2.6), 1.6, 140, 2500, sy=1.2)
    # sky: none (a casino has no daylight); a dim warm world for the bounce floor
    wd = bpy.data.worlds.new('w'); sc.world = wd; wd.use_nodes = True
    wd.node_tree.nodes['Background'].inputs[0].default_value = (0.25, 0.18, 0.15, 1)
    wd.node_tree.nodes['Background'].inputs[1].default_value = 0.02


# ------------------------------------------------------------------ props: vertex light
def bake_vertex_light(props, samples):
    sc = bpy.context.scene
    me = props.data
    ca = me.color_attributes.new('Col', 'BYTE_COLOR', 'CORNER')
    me.color_attributes.active_color = ca
    me.attributes.render_color_index = me.color_attributes.find('Col')
    bpy.ops.object.select_all(action='DESELECT'); props.select_set(True); bpy.context.view_layer.objects.active = props
    sc.render.bake.target = 'VERTEX_COLORS'
    sc.cycles.samples = samples
    t0 = time.time()
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'DIRECT', 'INDIRECT'}, target='VERTEX_COLORS')
    log('vertex light baked', f'{time.time() - t0:.0f}s')
    sc.render.bake.target = 'IMAGE_TEXTURES'
    # scale, and the glow mask in alpha: the signs and the slot atlas's screen panels and reel strips glow
    uv = me.uv_layers['UVMap'].data
    col = np.empty(len(me.loops) * 4, np.float32); ca.data.foreach_get('color', col); col = col.reshape(-1, 4)
    sign_mat = [i for i, m in enumerate(me.materials) if m.name.startswith('M_rr_signs')]
    for p in me.polygons:
        us = [uv[li].uv for li in p.loop_indices]
        u = sum(q[0] for q in us) / len(us); v = sum(q[1] for q in us) / len(us)
        if p.material_index in sign_mat:
            glow = 1.0
        else:   # slot atlas (v up): top signs/reel glass/belly glass (v > 0.5, except the dark parts strip u > 0.5, v > 0.75)
            glow = 1.0 if ((v > 0.5 and not (u > 0.5 and v > 0.75)) or (v < 0.25 and 0.5 < u < 0.95)) else 0.0
        for li in p.loop_indices:
            col[li, :3] = np.clip(col[li, :3] * LIGHT_SCALE, 0, 1)
            col[li, 3] = glow
    ca.data.foreach_set('color', col.ravel())


def main():
    samples = int(ARGS[ARGS.index('--samples') + 1]) if '--samples' in ARGS else 256
    preview = ARGS[ARGS.index('--preview') + 1] if '--preview' in ARGS else None
    W.reset()
    setup_materials()
    B = Builder()
    shell(B)
    furniture(B)
    built = B.to_object('built')
    parts = [built] + place_props()
    room = W.join_parts(parts, 'room')
    room.data.uv_layers[0].name = 'UVMap'
    while len(room.data.uv_layers) > 1:
        room.data.uv_layers.remove(room.data.uv_layers[1])
    slots = slot_bank()
    sg = signs()
    props = W.join_parts(slots + [sg], 'props')
    log('room tris', W.tris(room), 'props tris', W.tris(props), 'materials', [m.name for m in room.data.materials])
    add_lights()
    if preview:
        cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); bpy.context.scene.collection.objects.link(cam)
        cam.data.lens = 16; cam.location = (2.5, 0.8, 1.7)
        cam.rotation_euler = (Vector((-1.2, 10.0, 1.3)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc = bpy.context.scene; sc.camera = cam; sc.render.engine = 'CYCLES'; sc.cycles.samples = 64
        sc.render.resolution_x, sc.render.resolution_y = 1280, 720; sc.view_settings.view_transform = 'AgX'
        sc.render.filepath = preview
        bpy.ops.render.render(write_still=True)
        log('preview', preview)
        return
    # colliders: the room's geometry and the slot bank's boxes (the props mesh is not in the room)
    tmp = room.copy(); tmp.data = room.data.copy(); bpy.context.scene.collection.objects.link(tmp)
    tmp2 = props.copy(); tmp2.data = props.data.copy(); bpy.context.scene.collection.objects.link(tmp2)
    both = W.join_parts([tmp, tmp2], 'colliders_src')
    boxes = WI.colliders(both)
    bpy.data.objects.remove(both)
    # bake the room (world_v3_interior's helpers)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        prefs.compute_device_type = 'METAL'; prefs.get_devices()
        for dv in prefs.devices:
            dv.use = True
        sc.cycles.device = 'GPU'
    except Exception as e:
        log('cycles GPU unavailable', e)
    W.clean(room, merge=0.0005)
    WI.bake_uvs(room)
    ap, lp = 4096, 2048
    alb = bpy.data.images.new('alb', ap, ap, alpha=True, float_buffer=True)
    lit = bpy.data.images.new('lit', lp, lp, alpha=False, float_buffer=True)
    emi = bpy.data.images.new('emi', lp, lp, alpha=False, float_buffer=True)
    info = WI.prepare_materials(room, alb, 'UVMap')
    room.data.uv_layers['bake'].active_render = True
    room.data.uv_layers.active = room.data.uv_layers['bake']
    bpy.ops.object.select_all(action='DESELECT'); room.select_set(True); bpy.context.view_layer.objects.active = room
    WI.set_target(room, alb); WI.bake('DIFFUSE', alb, 1, 16, {'COLOR'})
    WI.set_target(room, lit); WI.bake('DIFFUSE', lit, samples, 6, {'DIRECT', 'INDIRECT'})
    WI.set_target(room, emi); WI.bake('EMIT', emi, 1, 6)
    A = WI.img_np(alb)
    def zoom(x, n):
        # separable linear upsampling (Blender's Python has no scipy)
        h, w = x.shape[:2]
        ys = np.clip((np.arange(n) + 0.5) * h / n - 0.5, 0, h - 1); xs = np.clip((np.arange(n) + 0.5) * w / n - 0.5, 0, w - 1)
        y0 = np.floor(ys).astype(int); y1 = np.minimum(y0 + 1, h - 1); fy = (ys - y0)[:, None, None]
        r = x[y0] * (1 - fy) + x[y1] * fy
        x0 = np.floor(xs).astype(int); x1 = np.minimum(x0 + 1, w - 1); fx = (xs - x0)[None, :, None]
        return r[:, x0] * (1 - fx) + r[:, x1] * fx
    Lu = zoom(WI.img_np(lit)[..., :3], ap)
    Eu = zoom(WI.img_np(emi)[..., :3], ap)
    exposure = 1.2
    rgb = (A[..., :3] * Lu + Eu) * exposure
    out = np.concatenate([rgb, np.ones(rgb.shape[:2] + (1,), np.float32)], axis=2)
    stats = np.percentile(rgb[A[..., 3] > 0.5].mean(1), [50, 90, 99]).round(3).tolist()
    log('baked radiance percentiles 50/90/99', stats)
    os.makedirs(os.path.join(OUT, 'textures'), exist_ok=True)
    img = bpy.data.images.new(RID + '_baked', ap, ap, alpha=True, float_buffer=True)
    img.pixels.foreach_set(np.clip(out, 0, 64).astype(np.float32).ravel())
    p = os.path.join(OUT, 'textures', f'{RID}_baked.png')
    img.filepath_raw = p; img.file_format = 'PNG'; sc.render.image_settings.color_depth = '8'
    img.save_render(p)
    # props: vertex light (with the room in the scene for occlusion and bounce)
    bake_vertex_light(props, max(64, samples // 2))
    # export the room: bake UVs only, one material
    me = room.data
    keep = me.uv_layers['bake']
    for l in [l for l in me.uv_layers if l.name != 'bake']:
        me.uv_layers.remove(l)
    keep.name = 'UVMap'
    me.materials.clear()
    mat = bpy.data.materials.new(f'M_{RID}_baked'); me.materials.append(mat)
    room.name = room.data.name = RID
    for o in [o for o in bpy.data.objects if o.type == 'LIGHT']:
        bpy.data.objects.remove(o)
    W.export_fbx([room], os.path.join(OUT, RID + '.fbx'))
    # export the props with their vertex colours (linear, x0.5 light; alpha = glow)
    props.name = props.data.name = RID + '_props'
    bpy.ops.object.select_all(action='DESELECT'); props.select_set(True); bpy.context.view_layer.objects.active = props
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, RID + '_props.fbx'), use_selection=True, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', use_triangles=True, colors_type='LINEAR',
                             path_mode='RELATIVE', embed_textures=False, bake_anim=False)
    vs = np.array([v.co[:] for v in room.data.vertices])
    # spots (Blender room coordinates; yaw: 0 = +y, 90 = +x, the way the player faces while using it)
    spots = [dict(id='slots', game='slots', at=[-4.6, 7.3, 0.0], yaw=270, range=2.6),
             dict(id='crash', game='crash', at=[XW - 2.0, 7.4, 0.0], yaw=90, range=2.4),
             dict(id='coinflip', game='coinflip', at=[0.0, 7.9, 0.0], yaw=0, range=2.4)]
    seats = [dict(id='sofa_1', kind='sofa', at=[-4.65, YD - 0.6, 0.45], yaw=180, approach=[-4.65, YD - 1.3]),
             dict(id='sofa_2', kind='sofa', at=[-3.75, YD - 0.6, 0.45], yaw=180, approach=[-3.75, YD - 1.3])]
    entry = {RID: dict(fbx=f'world_v3/v3_interiors/{RID}.fbx', kind='interior_room', tris=[W.tris(room)],
                       size_m=[round(float(x), 3) for x in (vs.max(0) - vs.min(0))], bounds=[vs.min(0).round(3).tolist(), vs.max(0).round(3).tolist()],
                       materials=[mat.name], textures=[f'{RID}_baked.png'],
                       door=dict(at=[0, 0, 0], into='+y', width=DOOR_W, height=DOOR_H),
                       colliders=boxes, seats=seats, spots=spots,
                       props=dict(fbx=f'world_v3/v3_interiors/{RID}_props.fbx', light_scale=LIGHT_SCALE,
                                  materials={'M_rr_slots': 'world_v3/v3_casino/textures/cs_slot_machine_BaseColor.png',
                                             'M_rr_signs': 'world_v3/v3_interiors/textures/rr_signs.png'}),
                       tags=['interior', 'kit:v3', 'enterable', 'casino'],
                       source='built in art/pipeline/casino_room.py (original); props: Sketchfab CC BY (slot pair by nermin, roulette by Robilben, '
                              'poker table by thethieme, blackjack by ravijangid.rv), Poly Haven CC0 furniture and textures',
                       licence='CC BY 4.0 parts (see v3_casino credits) + CC0', credit='The Rug Room casino floor, CT Life (see the v3_casino and v3_furniture credits)')}
    import shutil
    shutil.copy(os.path.join(PNG, 'rr_signs.png'), os.path.join(OUT, 'textures', 'rr_signs.png'))
    W.write_manifest(os.path.join(OUT, 'manifest.json'), entry)
    log('done', entry[RID]['tris'], entry[RID]['size_m'], 'colliders', len(boxes), 'props tris', W.tris(props))


main()
