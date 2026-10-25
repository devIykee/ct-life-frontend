"""Rekt Burger's restaurant (2026-10-25, backend PR #16): a walk-in room built here (no Sketchfab room), lit and baked like
casino_room.py / world_v3_interior.py's rooms, plus an unbaked 'props' mesh for the lit signs.

Usage: art/pipeline/run.sh art/pipeline/rekt_room.py [-- --samples N] [--preview out.png]
In:  art/raw/polyhaven/tex_* (painted plaster, brown planks: CC0), art/source/rektburger/out/png (terrazzo floor, mint
     subway tiles, the signs atlas rb_signs)
Out: art/processed/world_v3/v3_interiors/rekt_burger.fbx + textures/rekt_burger_baked.png (one material, Interior Baked),
     rekt_burger_props.fbx (the signs on their atlas x baked vertex light, vertex alpha = glow),
     manifest entry 'rekt_burger' (kind interior_room, colliders, seats, spots, props)

Room (Blender: x right seen from the door, y into the room, z up; origin = the entrance's floor centre):
  10.4 x 12.2 m, 3.5 m ceiling, a 0.3 m front wall with the 1.1 x 2.2 m entrance and storefront windows (daylight panes).
  Dining (front): cream terrazzo, cream walls over a charcoal wainscot with an orange stripe, two orange-vinyl booths on the
  left, four two-seat tables on the right, the REKT BURGER logo wall on the right wall, light panels and booth pendants.
  The counter across the room (charcoal front, orange band, mint kick light, quartz top) with the order screen, a pass shelf
  under heat lamps, and two menu boards hanging above it ("Rug Pull Burger", "Liquidation Fries" ... our own jokes).
  Kitchen (back, staff only): darker tile, mint subway tiles up the back wall, the line: flat-top grill and fryers under a
  steel hood, a prep fridge with ingredient pans, a drink fountain, a tall fridge, the KITCHEN header.
  Our own parody brand: no real chain's name, logo, colours or likeness.
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
PNG = os.path.join(REPO, 'art', 'source', 'rektburger', 'out', 'png')
PH = os.path.join(REPO, 'art', 'raw', 'polyhaven')
RID = 'rekt_burger'
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []

XW, YD, ZH = 5.2, 12.2, 3.5          # half width, depth, ceiling
FRONT_T = 0.3
DOOR_W, DOOR_H = 1.1, 2.2
CY0, CY1 = 6.0, 6.8                  # the counter: customer face, staff face
CX0, CX1 = -3.2, 3.6                 # the counter's run (a gate closes x CX1..XW)
LIGHT_SCALE = 0.5


def log(*a):
    print('[rekt_room]', *a, flush=True)


MATS = {}


def mat_img(name, img_path, tile, tint=(1, 1, 1), rough=0.6, emit=None, metal=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes['Principled BSDF']
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal
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
    mat_img('terrazzo', os.path.join(PNG, 'rb_floor.png'), 2.0, rough=0.35)
    mat_img('kfloor', os.path.join(PNG, 'rb_floor.png'), 1.2, tint=(0.42, 0.40, 0.38), rough=0.6)
    mat_img('tile', os.path.join(PNG, 'rb_tile.png'), 1.0, rough=0.2)
    mat_img('plaster', os.path.join(PH, 'tex_painted_plaster_wall', 'painted_plaster_wall_diff_1k.png'), 2.0, tint=(1.0, 0.95, 0.86), rough=0.85)
    mat_img('wood', os.path.join(PH, 'tex_planks_brown_10', 'planks_brown_10_diff_1k.png'), 1.6, tint=(0.85, 0.72, 0.6), rough=0.5)
    mat_img('charcoal', None, 1, tint=(0.035, 0.038, 0.045), rough=0.55)
    mat_img('orange', None, 1, tint=(0.92, 0.2, 0.03), rough=0.45)
    mat_img('vinyl', None, 1, tint=(0.85, 0.18, 0.03), rough=0.35)
    mat_img('vinyl_dark', None, 1, tint=(0.04, 0.04, 0.045), rough=0.4)
    mat_img('quartz', None, 1, tint=(0.88, 0.86, 0.83), rough=0.25)
    mat_img('steel', None, 1, tint=(0.62, 0.63, 0.65), rough=0.3, metal=0.7)
    mat_img('steel_dark', None, 1, tint=(0.16, 0.16, 0.17), rough=0.35, metal=0.5)
    mat_img('ceiling', None, 1, tint=(0.82, 0.81, 0.79), rough=0.9)
    mat_img('black', None, 1, tint=(0.02, 0.02, 0.022), rough=0.4)
    mat_img('oil', None, 1, tint=(0.35, 0.22, 0.05), rough=0.08)
    mat_img('pan_red', None, 1, tint=(0.75, 0.12, 0.06), rough=0.4)
    mat_img('pan_green', None, 1, tint=(0.25, 0.65, 0.2), rough=0.5)
    mat_img('pan_yellow', None, 1, tint=(0.95, 0.72, 0.15), rough=0.4)
    mat_img('pan_white', None, 1, tint=(0.9, 0.86, 0.95), rough=0.4)
    mat_img('panel', None, 1, tint=(0, 0, 0), emit=(WI.kelvin_rgb(4200), 3.0))
    mat_img('kpanel', None, 1, tint=(0, 0, 0), emit=(WI.kelvin_rgb(5200), 3.2))
    mat_img('daylight', None, 1, tint=(0, 0, 0), emit=((0.78, 0.86, 1.0), 2.2))
    mat_img('mint_glow', None, 1, tint=(0, 0, 0), emit=((0.22, 0.88, 0.64), 4.0))
    mat_img('orange_glow', None, 1, tint=(0, 0, 0), emit=((1.0, 0.42, 0.17), 4.0))
    mat_img('heat', None, 1, tint=(0, 0, 0), emit=((1.0, 0.38, 0.12), 6.0))
    mat_img('lamp', None, 1, tint=(0, 0, 0), emit=(WI.kelvin_rgb(2700), 6.0))



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
    """Floor, walls (dining: cream over a charcoal wainscot with an orange stripe; kitchen: mint tiles), the front wall
    with the entrance and the daylit storefront panes, the ceiling with light panels."""
    x0, x1 = -XW, XW
    B.quad([(x0, FRONT_T, 0), (x1, FRONT_T, 0), (x1, CY1, 0), (x0, CY1, 0)], 'terrazzo')
    B.quad([(x0, CY1, 0), (x1, CY1, 0), (x1, YD, 0), (x0, YD, 0)], 'kfloor')

    def wall_y(y, xa, xb, za, zb, mat, facing):
        pts = [(xa, y, za), (xb, y, za), (xb, y, zb), (xa, y, zb)]
        B.quad(pts if facing < 0 else pts[::-1], mat)

    def wall_x(x, ya, yb, za, zb, mat, facing):
        pts = [(x, ya, za), (x, yb, za), (x, yb, zb), (x, ya, zb)]
        B.quad(pts if facing > 0 else pts[::-1], mat)

    dx = DOOR_W / 2
    din = [(0.0, 1.05, 'charcoal'), (1.05, 1.13, 'orange'), (1.13, ZH, 'plaster')]
    kit = [(0.0, 2.4, 'tile'), (2.4, ZH, 'plaster')]
    # the storefront: panes either side of the door (z 0.45 .. 3.0), the front wall round them
    panes = [(-4.6, -0.95), (0.95, 4.6)]
    for za, zb, m in din:
        cuts = sorted(set([x0, x1, -dx, dx] + [p for pr in panes for p in pr]))
        for xa, xb in zip(cuts[:-1], cuts[1:]):
            mid = (xa + xb) / 2
            if -dx < mid < dx:                     # the doorway: open from the floor, wall above the lintel
                lo, hi = 0.0, DOOR_H
            elif any(a < mid < b for a, b in panes):
                lo, hi = 0.45, 3.0
            else:
                lo, hi = None, None
            if lo is None:
                wall_y(FRONT_T, xa, xb, za, zb, m, +1)
            else:
                if za < lo:
                    wall_y(FRONT_T, xa, xb, za, min(zb, lo), m, +1)
                if zb > hi:
                    wall_y(FRONT_T, xa, xb, max(za, hi), zb, m, +1)
        wall_x(x0, FRONT_T, CY1, za, zb, m, +1)
        wall_x(x1, FRONT_T, CY1, za, zb, m, -1)
    for za, zb, m in kit:
        wall_y(YD, x0, x1, za, zb, m, -1)
        wall_x(x0, CY1, YD, za, zb, m, +1)
        wall_x(x1, CY1, YD, za, zb, m, -1)
    # the panes: daylight behind slim black mullions (the street is not drawn from inside), sills
    for a, b in panes:
        B.quad([(a, FRONT_T - 0.05, 0.45), (b, FRONT_T - 0.05, 0.45), (b, FRONT_T - 0.05, 3.0), (a, FRONT_T - 0.05, 3.0)][::-1], 'daylight')
        for xa, xb in ((a, a + 0.06), (b - 0.06, b)):
            B.box(xa, xb, FRONT_T - 0.06, FRONT_T + 0.02, 0.45, 3.0, 'black')
        n = int(round((b - a) / 1.25))
        for k in range(1, n):
            xm = a + (b - a) * k / n
            B.box(xm - 0.03, xm + 0.03, FRONT_T - 0.06, FRONT_T + 0.02, 0.45, 3.0, 'black')
        B.box(a, b, FRONT_T - 0.06, FRONT_T + 0.02, 2.55, 2.6, 'black')
        B.box(a - 0.04, b + 0.04, FRONT_T - 0.06, FRONT_T + 0.14, 0.41, 0.45, 'quartz')
        B.box(a - 0.04, b + 0.04, FRONT_T - 0.06, FRONT_T + 0.04, 2.98, 3.04, 'black')
        for side in ((a, FRONT_T - 0.05, FRONT_T), (b, FRONT_T - 0.05, FRONT_T)):
            pass
    # door reveal and an outside face round the opening (seen from the street through the leaf)
    B.quad([(-dx, 0, 0), (-dx, FRONT_T, 0), (-dx, FRONT_T, DOOR_H), (-dx, 0, DOOR_H)], 'black')
    B.quad([(dx, FRONT_T, 0), (dx, 0, 0), (dx, 0, DOOR_H), (dx, FRONT_T, DOOR_H)], 'black')
    B.quad([(-dx, 0, DOOR_H), (-dx, FRONT_T, DOOR_H), (dx, FRONT_T, DOOR_H), (dx, 0, DOOR_H)], 'black')
    B.quad([(-dx, 0, 0), (dx, 0, 0), (dx, FRONT_T, 0), (-dx, FRONT_T, 0)], 'terrazzo')
    B.quad([(-dx, 0, DOOR_H), (-dx, 0, 2.6), (dx, 0, 2.6), (dx, 0, DOOR_H)][::-1], 'charcoal')
    B.box(-dx - 0.1, -dx, FRONT_T, FRONT_T + 0.04, 0, DOOR_H + 0.1, 'orange', skip=('-y',))
    B.box(dx, dx + 0.1, FRONT_T, FRONT_T + 0.04, 0, DOOR_H + 0.1, 'orange', skip=('-y',))
    B.box(-dx - 0.1, dx + 0.1, FRONT_T, FRONT_T + 0.04, DOOR_H, DOOR_H + 0.1, 'orange', skip=('-y',))
    # ceiling (faces down) and light panels (just below it)
    B.quad([(x0, FRONT_T, ZH), (x1, FRONT_T, ZH), (x1, YD, ZH), (x0, YD, ZH)][::-1], 'ceiling')
    for (cx, cy) in ((-2.6, 1.8), (0.0, 1.8), (2.6, 1.8), (-2.6, 4.4), (0.0, 4.4), (2.6, 4.4)):
        B.box(cx - 0.62, cx + 0.62, cy - 0.62, cy + 0.62, ZH - 0.04, ZH, 'steel_dark', skip=('+z',))
        B.quad([(cx - 0.58, cy - 0.58, ZH - 0.045), (cx - 0.58, cy + 0.58, ZH - 0.045), (cx + 0.58, cy + 0.58, ZH - 0.045), (cx + 0.58, cy - 0.58, ZH - 0.045)], 'panel')
    for cx in (-2.4, 2.0):
        B.box(cx - 1.25, cx + 1.25, 8.6, 9.2, ZH - 0.04, ZH, 'steel_dark', skip=('+z',))
        B.quad([(cx - 1.2, 8.65, ZH - 0.045), (cx - 1.2, 9.15, ZH - 0.045), (cx + 1.2, 9.15, ZH - 0.045), (cx + 1.2, 8.65, ZH - 0.045)], 'kpanel')
    # an orange cove line round the dining room's ceiling edge, skirting
    B.box(x0 + 0.02, x0 + 0.06, FRONT_T, CY1, ZH - 0.18, ZH - 0.14, 'orange_glow')
    B.box(x1 - 0.06, x1 - 0.02, FRONT_T, CY1, ZH - 0.18, ZH - 0.14, 'orange_glow')


def counter(B):
    """The counter: charcoal front with an orange band and a mint kick light, a quartz top, the staff way in on the right,
    the pass shelf under heat lamps, the order screen's stand; the menu boards' frames hanging above."""
    B.box(CX0, CX1, CY0 + 0.05, CY1, 0.0, 1.02, 'charcoal')
    B.box(CX0, CX1, CY0 + 0.03, CY0 + 0.05, 0.62, 0.74, 'orange')
    B.box(CX0, CX1, CY0 + 0.0, CY0 + 0.05, 0.04, 0.08, 'mint_glow')
    B.box(CX0 - 0.04, CX1 + 0.04, CY0 - 0.06, CY1 + 0.04, 1.02, 1.08, 'quartz')
    # the end panel to the left wall (the kitchen side is closed there); on the right the staff way in stays open (the
    # clock-in loop walks the player through it to the stations), an orange-capped post marking it
    B.box(-XW, CX0, CY0 + 0.05, CY1, 0.0, 1.08, 'charcoal')
    B.box(XW - 0.12, XW, CY0 + 0.05, CY1, 0.0, 1.0, 'steel')
    B.box(XW - 0.14, XW, CY0 + 0.03, CY1 + 0.02, 1.0, 1.06, 'orange')
    # pass shelf over the counter's middle, a heat-lamp bar above it (warm glow onto the shelf)
    for x in (-1.7, 1.1):
        B.box(x - 0.025, x + 0.025, CY0 + 0.3, CY0 + 0.35, 1.08, 1.95, 'steel')
    B.box(-1.75, 1.15, CY0 + 0.15, CY0 + 0.55, 1.42, 1.46, 'steel')
    B.box(-1.75, 1.15, CY0 + 0.2, CY0 + 0.5, 1.9, 1.98, 'steel_dark')
    B.box(-1.65, 1.05, CY0 + 0.25, CY0 + 0.45, 1.885, 1.9, 'heat')
    # trays waiting on the pass (charcoal with a mint liner)
    for x in (-1.2, 0.1):
        B.box(x - 0.24, x + 0.24, CY0 + 0.18, CY0 + 0.52, 1.46, 1.48, 'vinyl_dark')
        B.box(x - 0.2, x + 0.2, CY0 + 0.21, CY0 + 0.49, 1.48, 1.485, 'pan_green')
    # order screen stand (the screen is a props quad)
    B.box(-2.75, -2.55, CY0 + 0.3, CY0 + 0.4, 1.08, 1.32, 'steel_dark')
    B.box(-3.05, -2.25, CY0 + 0.24, CY0 + 0.3, 1.3, 1.9, 'black')
    # menu boards' frames and their hangers (faces are props quads)
    for a, b in ((-3.95, -0.65), (-0.35, 2.95)):
        B.box(a - 0.05, b + 0.05, CY1 + 0.08, CY1 + 0.14, 2.2, 3.1, 'black')
        for x in (a + 0.3, b - 0.3):
            B.box(x - 0.01, x + 0.01, CY1 + 0.1, CY1 + 0.12, 3.1, ZH, 'steel')


def kitchen(B):
    """The line along the back wall: grill and fryers under a hood, the prep fridge with pans, the drink fountain, the
    tall fridge."""
    yb0, yb1 = YD - 0.85, YD
    # flat-top grill
    B.box(-5.0, -3.3, yb0, yb1, 0.0, 0.86, 'steel')
    B.box(-5.0, -3.3, yb0, yb1, 0.86, 0.92, 'black')
    B.box(-5.0, -3.3, yb0 - 0.04, yb0, 0.78, 0.86, 'steel_dark')
    for k in range(4):
        x = -4.85 + k * 0.42
        B.cyl(x, yb0 - 0.03, 0.62, 0.66, 0.035, 'black', n=8)
    # patties on the grill
    for x, y in ((-4.6, yb0 + 0.3), (-4.2, yb0 + 0.45), (-3.8, yb0 + 0.28)):
        B.cyl(x, y, 0.92, 0.94, 0.07, 'oil', n=10)
    # fryers: two oil wells with baskets
    B.box(-3.2, -1.9, yb0, yb1, 0.0, 0.92, 'steel')
    for x in (-2.85, -2.25):
        B.box(x - 0.24, x + 0.24, yb0 + 0.12, yb1 - 0.12, 0.88, 0.9, 'oil')
        B.box(x - 0.2, x + 0.2, yb0 + 0.18, yb1 - 0.22, 0.9, 1.0, 'steel_dark')
        B.box(x - 0.02, x + 0.02, yb0 - 0.05, yb0 + 0.2, 0.98, 1.02, 'black')
    # the hood over grill and fryers, a lamp strip underneath
    B.box(-5.1, -1.8, YD - 1.25, YD, 2.35, 3.1, 'steel')
    B.box(-5.0, -1.9, YD - 1.15, YD - 1.05, 2.33, 2.35, 'kpanel')
    B.box(-5.1, -1.8, YD - 0.2, YD, 3.1, ZH, 'steel')
    # prep fridge with ingredient pans
    B.box(-1.7, 0.9, yb0, yb1, 0.0, 0.9, 'steel')
    B.box(-1.7, 0.9, yb0 - 0.02, yb0, 0.1, 0.8, 'steel_dark')
    pans = ['pan_red', 'pan_green', 'pan_yellow', 'pan_white', 'pan_green', 'pan_red']
    for k, m in enumerate(pans):
        x = -1.55 + k * 0.41
        B.box(x, x + 0.36, yb0 + 0.12, yb0 + 0.5, 0.86, 0.92, 'steel_dark')
        B.box(x + 0.02, x + 0.34, yb0 + 0.14, yb0 + 0.48, 0.92, 0.93, m)
    B.box(-1.7, 0.9, yb0 + 0.55, yb1, 0.9, 1.25, 'steel')
    # drink fountain on a cabinet
    B.box(1.1, 2.5, yb0, yb1, 0.0, 0.92, 'charcoal')
    B.box(1.2, 2.4, yb0 + 0.15, yb1, 0.92, 1.75, 'black')
    B.box(1.25, 2.35, yb0 + 0.13, yb0 + 0.15, 1.2, 1.7, 'orange')
    for k in range(4):
        x = 1.38 + k * 0.28
        B.box(x - 0.04, x + 0.04, yb0 + 0.06, yb0 + 0.16, 1.12, 1.2, 'steel')
    B.box(1.2, 2.4, yb0 + 0.02, yb0 + 0.3, 0.92, 0.95, 'steel_dark')
    # tall fridge
    B.box(2.9, 4.6, YD - 0.85, YD, 0.0, 2.25, 'steel')
    B.box(3.74, 3.76, YD - 0.87, YD - 0.85, 0.05, 2.2, 'black')
    for x in (3.6, 3.9):
        B.box(x - 0.02, x + 0.02, YD - 0.92, YD - 0.87, 0.8, 1.6, 'steel_dark')
    # the KITCHEN header's backing on the back wall
    B.box(1.1, 2.5, YD - 0.05, YD, 2.45, 3.0, 'black')


def dining(B):
    """Two booths on the left (orange vinyl, charcoal backs, quartz tables on steel pedestals) with pendants; four two-seat
    tables on the right with chairs; the logo wall's frame on the right wall."""
    seats = []
    for k, c in enumerate((1.75, 4.15)):
        for sy, face in ((c - 0.8, 0), (c + 0.8, 180)):
            y0, y1 = (sy - 0.28, sy + 0.22) if face == 0 else (sy - 0.22, sy + 0.28)
            B.box(-XW, -3.55, y0, y1, 0.0, 0.42, 'vinyl_dark')
            B.box(-XW, -3.55, y0, y1, 0.42, 0.5, 'vinyl')
            by0, by1 = (sy - 0.36, sy - 0.22) if face == 0 else (sy + 0.22, sy + 0.36)
            B.box(-XW, -3.55, by0, by1, 0.0, 1.15, 'vinyl_dark')
            B.box(-XW, -3.55, by0 + (0.0 if face == 0 else -0.04), by1 + (0.04 if face == 0 else 0.0), 0.5, 1.05, 'vinyl')
            seats.append(dict(id=f'booth_{k}_{face}', kind='seat', at=[-4.35, sy, 0.5], yaw=face, approach=[-3.1, sy]))
        B.box(-XW + 0.05, -3.75, c - 0.42, c + 0.42, 0.72, 0.76, 'quartz')
        B.cyl(-4.45, c, 0.0, 0.72, 0.05, 'steel', n=10, caps=False)
        B.cyl(-4.45, c, 0.0, 0.03, 0.26, 'steel', n=14)
        # pendant
        B.cyl(-4.45, c, 2.3, ZH, 0.008, 'black', n=6, caps=False)
        B.cyl(-4.45, c, 2.15, 2.32, 0.2, 'orange', n=16)
        B.quad([(-4.45 + 0.19 * math.cos(2 * math.pi * i / 4 + math.pi / 4), c + 0.19 * math.sin(2 * math.pi * i / 4 + math.pi / 4), 2.149) for i in range(3, -1, -1)], 'lamp')
    for (tx, ty) in ((0.9, 2.1), (3.2, 2.1), (0.9, 4.4), (3.2, 4.4)):
        B.box(tx - 0.36, tx + 0.36, ty - 0.36, ty + 0.36, 0.72, 0.76, 'wood')
        B.cyl(tx, ty, 0.0, 0.72, 0.04, 'black', n=8, caps=False)
        B.box(tx - 0.22, tx + 0.22, ty - 0.22, ty + 0.22, 0.0, 0.02, 'black')
        for sy in (-1, 1):
            cy = ty + sy * 0.62
            B.box(tx - 0.21, tx + 0.21, cy - 0.2, cy + 0.2, 0.44, 0.48, 'orange')
            for lx in (-0.18, 0.18):
                for ly in (-0.17, 0.17):
                    B.box(tx + lx - 0.015, tx + lx + 0.015, cy + ly - 0.015, cy + ly + 0.015, 0.0, 0.44, 'black')
            by = cy + sy * 0.18
            B.box(tx - 0.21, tx + 0.21, by - 0.02, by + 0.02, 0.48, 0.86, 'black')
    # logo wall frame (right wall, faces -x)
    B.box(XW - 0.06, XW, 1.45, 5.25, 1.35, 2.95, 'charcoal', skip=('+x',))
    return seats


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



# ------------------------------------------------------------------ signs (props on the atlas)
def signs():
    """Sign faces on the rb_signs atlas (2048 x 1024; v up)."""
    B = Builder()
    m = 'rb_signs'

    def U(px, py):
        return (px / 2048.0, 1.0 - py / 1024.0)

    def face(pts, box):
        x0, y0, x1, y1 = box
        B.quad(pts, m, uv=[U(x0, y1), U(x1, y1), U(x1, y0), U(x0, y0)])
    # menu boards (face -y, towards the customers); seen from the room, left = -x
    yb = CY1 + 0.075
    face([(-3.95, yb, 2.2), (-0.65, yb, 2.2), (-0.65, yb, 3.1), (-3.95, yb, 3.1)], (0, 0, 1024, 512))
    face([(-0.35, yb, 2.2), (2.95, yb, 2.2), (2.95, yb, 3.1), (-0.35, yb, 3.1)], (1024, 0, 2048, 512))
    # order screen (faces -y)
    ys = CY0 + 0.235
    face([(-3.0, ys, 1.34), (-2.3, ys, 1.34), (-2.3, ys, 1.86), (-3.0, ys, 1.86)], (1024, 512, 1536, 896))
    # logo wall (right wall, faces -x: seen from the room, left = +y)
    xw = XW - 0.065
    face([(xw, 5.15, 1.45), (xw, 1.55, 1.45), (xw, 1.55, 2.85), (xw, 5.15, 2.85)], (0, 512, 1024, 896))
    # KITCHEN header on the back wall (faces -y)
    yk = YD - 0.055
    face([(1.15, yk, 2.5), (2.45, yk, 2.5), (2.45, yk, 2.95), (1.15, yk, 2.95)], (1536, 512, 2048, 704))
    # the hours plaque right of the entrance (faces +y: seen from the room, left = +x)
    yp = FRONT_T + 0.02
    face([(1.2 + 1.0, yp, 1.35), (1.2, yp, 1.35), (1.2, yp, 1.72), (1.2 + 1.0, yp, 1.72)][::-1][::-1], (1536, 704, 2048, 896))
    MATS[m] = atlas_material('M_rb_signs', os.path.join(PNG, 'rb_signs.png'))
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
    for i, (cx, cy) in enumerate(((-2.6, 1.8), (0.0, 1.8), (2.6, 1.8), (-2.6, 4.4), (0.0, 4.4), (2.6, 4.4))):
        area(f'panel{i}', (cx, cy, ZH - 0.06), 1.15, 190, 4200)
    for i, cx in enumerate((-2.4, 2.0)):
        area(f'kpanel{i}', (cx, 8.9, ZH - 0.06), 2.4, 300, 5200, sy=0.5)
    for i, c in enumerate((1.75, 4.15)):
        area(f'pend{i}', (-4.45, c, 2.12), 0.35, 60, 2700)
    area('hood', (-3.45, YD - 1.1, 2.3), 3.0, 180, 5000, sy=0.3)
    area('heat', (-0.3, CY0 + 0.35, 1.86), 2.6, 90, 0, sy=0.2, col=(1.0, 0.45, 0.2))
    # daylight through the storefront: soft and cool, from the front wall into the room
    for i, (a, b) in enumerate(((-4.6, -0.95), (0.95, 4.6))):
        area(f'day{i}', ((a + b) / 2, FRONT_T + 0.05, 1.7), b - a, 220, 0, rot=(math.radians(-90), 0, 0), sy=2.4, col=(0.82, 0.9, 1.0))
    wd = bpy.data.worlds.new('w'); sc.world = wd; wd.use_nodes = True
    wd.node_tree.nodes['Background'].inputs[0].default_value = (0.8, 0.82, 0.85, 1)
    wd.node_tree.nodes['Background'].inputs[1].default_value = 0.05


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
    col = np.empty(len(me.loops) * 4, np.float32); ca.data.foreach_get('color', col); col = col.reshape(-1, 4)
    col[:, :3] = np.clip(col[:, :3] * LIGHT_SCALE, 0, 1)
    col[:, 3] = 1.0          # every sign face is lit (screens and backlit boards)
    ca.data.foreach_set('color', col.ravel())


def main():
    samples = int(ARGS[ARGS.index('--samples') + 1]) if '--samples' in ARGS else 256
    preview = ARGS[ARGS.index('--preview') + 1] if '--preview' in ARGS else None
    W.reset()
    setup_materials()
    B = Builder()
    shell(B)
    counter(B)
    kitchen(B)
    seats = dining(B)
    room = B.to_object('room')
    room.data.uv_layers[0].name = 'UVMap'
    sg = signs()
    props = W.join_parts([sg], 'props')
    log('room tris', W.tris(room), 'props tris', W.tris(props), 'materials', [m.name for m in room.data.materials])
    add_lights()
    if preview:
        sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 48
        sc.render.resolution_x, sc.render.resolution_y = 1280, 720; sc.view_settings.view_transform = 'AgX'
        for tag, loc, look in (('a', (2.8, 0.9, 1.7), (-1.0, 10.0, 1.2)), ('b', (-3.0, 5.0, 1.6), (2.0, 11.5, 1.3))):
            cam = bpy.data.objects.new('cam' + tag, bpy.data.cameras.new('cam' + tag)); sc.collection.objects.link(cam)
            cam.data.lens = 16; cam.location = loc
            cam.rotation_euler = (Vector(look) - cam.location).to_track_quat('-Z', 'Y').to_euler()
            sc.camera = cam
            sc.render.filepath = preview.replace('.png', f'_{tag}.png')
            bpy.ops.render.render(write_still=True)
        log('preview', preview)
        return
    tmp = room.copy(); tmp.data = room.data.copy(); bpy.context.scene.collection.objects.link(tmp)
    boxes = WI.colliders(tmp)
    bpy.data.objects.remove(tmp)
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
    WI.prepare_materials(room, alb, 'UVMap')
    room.data.uv_layers['bake'].active_render = True
    room.data.uv_layers.active = room.data.uv_layers['bake']
    bpy.ops.object.select_all(action='DESELECT'); room.select_set(True); bpy.context.view_layer.objects.active = room
    WI.set_target(room, alb); WI.bake('DIFFUSE', alb, 1, 16, {'COLOR'})
    WI.set_target(room, lit); WI.bake('DIFFUSE', lit, samples, 6, {'DIRECT', 'INDIRECT'})
    WI.set_target(room, emi); WI.bake('EMIT', emi, 1, 6)
    A = WI.img_np(alb)

    def zoom(x, n):
        h, w = x.shape[:2]
        ys = np.clip((np.arange(n) + 0.5) * h / n - 0.5, 0, h - 1); xs = np.clip((np.arange(n) + 0.5) * w / n - 0.5, 0, w - 1)
        y0 = np.floor(ys).astype(int); y1 = np.minimum(y0 + 1, h - 1); fy = (ys - y0)[:, None, None]
        r = x[y0] * (1 - fy) + x[y1] * fy
        x0 = np.floor(xs).astype(int); x1 = np.minimum(x0 + 1, w - 1); fx = (xs - x0)[None, :, None]
        return r[:, x0] * (1 - fx) + r[:, x1] * fx
    Lu = zoom(WI.img_np(lit)[..., :3], ap)
    Eu = zoom(WI.img_np(emi)[..., :3], ap)
    exposure = 1.0
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
    bake_vertex_light(props, max(64, samples // 2))
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
    props.name = props.data.name = RID + '_props'
    bpy.ops.object.select_all(action='DESELECT'); props.select_set(True); bpy.context.view_layer.objects.active = props
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, RID + '_props.fbx'), use_selection=True, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', use_triangles=True, colors_type='LINEAR',
                             path_mode='RELATIVE', embed_textures=False, bake_anim=False)
    vs = np.array([v.co[:] for v in room.data.vertices])
    spots = [dict(id='counter', game='shift', at=[-0.3, CY0 - 0.75, 0.0], yaw=0, range=2.3)]
    entry = {RID: dict(fbx=f'world_v3/v3_interiors/{RID}.fbx', kind='interior_room', tris=[W.tris(room)],
                       size_m=[round(float(x), 3) for x in (vs.max(0) - vs.min(0))], bounds=[vs.min(0).round(3).tolist(), vs.max(0).round(3).tolist()],
                       materials=[mat.name], textures=[f'{RID}_baked.png'],
                       door=dict(at=[0, 0, 0], into='+y', width=DOOR_W, height=DOOR_H),
                       colliders=boxes, seats=seats, spots=spots,
                       props=dict(fbx=f'world_v3/v3_interiors/{RID}_props.fbx', light_scale=LIGHT_SCALE,
                                  materials={'M_rb_signs': 'world_v3/v3_interiors/textures/rb_signs.png'}),
                       tags=['interior', 'kit:v3', 'enterable', 'restaurant'],
                       source='built in art/pipeline/rekt_room.py (original geometry and art); Poly Haven CC0 textures',
                       licence='CC0 (Poly Haven textures) + original', credit='Rekt Burger restaurant, CT Life (original parody brand)')}
    import shutil
    shutil.copy(os.path.join(PNG, 'rb_signs.png'), os.path.join(OUT, 'textures', 'rb_signs.png'))
    W.write_manifest(os.path.join(OUT, 'manifest.json'), entry)
    log('done', entry[RID]['tris'], entry[RID]['size_m'], 'colliders', len(boxes), 'props tris', W.tris(props))


main()
