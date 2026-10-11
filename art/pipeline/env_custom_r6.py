"""CT Life round-6 venue furniture made by us (CC0): pieces no CC0 kit covers well, modelled properly
(lathe profiles, bevels, creased subdivision for soft goods) instead of the venue builder's box/cylinder primitives.

  booth_bench        café booth: walnut base and frame, two seat cushions, three channel-tufted back cushions
  bench_cushioned    studio terrace bench: slatted base, long seat cushion, three back cushions
  bean_bag           soft bean bag with a seat hollow and raised back (single colour, recoloured per placement)
  floor_cushion      plump square floor cushion with a tufted button (single colour)
  pouf_round         ribbed round knitted pouf (single colour)
  throw_pillow       square throw pillow (single colour)
  espresso_machine   two-group espresso machine: enamel body, chrome top/rails, group heads, portafilters, steam wands,
                     gauges, drip tray, cups on the warmer
  coffee_grinder     burr grinder: base, body, bean hopper and lid
  pastry_case        counter-top pastry display: walnut base, back panel, two shelves, chrome frame (open front so the
                     pastries the venue places inside stay visible; the builder has no transparent route)
  fountain_tiered    Timeline Square fountain: moulded basin ring, pedestal, column with rings, scalloped lower bowl,
                     upper bowl, pine-cone finial; KEEP_water surfaces
  pendant_dome       enamel dome pendant shade with white inside and a brass cap
  sack               tied burlap sack
  lantern_floor      wooden floor lantern with a glowing glass core (KEEP_neon -> glow route)
  table_oval         low oval walnut coffee table (studio)

Usage: art/pipeline/blender.sh art/pipeline/env_custom_r6.py [-- name,name]
Writes art/raw/ctlife_custom/<name>.glb; env_config.py ('round 6' section) processes them like any source.
"""
import bpy, bmesh, os, sys, math
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(__file__))
import ctl_config as C

OUT = os.path.join(C.RAW, 'ctlife_custom')
os.makedirs(OUT, exist_ok=True)
ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
ONLY = set(ARGS[0].split(',')) if ARGS else None
TAU = math.tau


def srgb(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


MATS = {}


def mat(name, hexcol, rough=0.7, emit=0.0):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*srgb(hexcol), 1)
    b.inputs['Roughness'].default_value = rough
    if emit:
        b.inputs['Emission Color'].default_value = (*srgb(hexcol), 1)
        b.inputs['Emission Strength'].default_value = emit
    MATS[name] = m
    return m


class Model:
    def __init__(self, name):
        self.name = name
        self.objs = []

    # ---------------------------------------------------------------- low level
    def _obj(self, bm, m):
        me = bpy.data.meshes.new(self.name + '_p')
        bm.to_mesh(me)
        bm.free()
        me.materials.append(m)
        o = bpy.data.objects.new(self.name + '_p', me)
        bpy.context.scene.collection.objects.link(o)
        self.objs.append(o)
        return o

    def _subsurf(self, o, levels):
        mod = o.modifiers.new('S', 'SUBSURF')
        mod.levels = mod.render_levels = levels
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
        o.modifiers.clear()
        o.data = me
        return o

    # ---------------------------------------------------------------- shapes
    def box(self, size, loc, m, bevel=0.0, seg=2, rot_z=0.0):
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=size, verts=bm.verts)
        if bevel:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, 0.45 * min(size)), segments=seg, affect='EDGES',
                            profile=0.5, clamp_overlap=True)
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(rot_z, 3, 'Z'), verts=bm.verts)
        bmesh.ops.translate(bm, vec=loc, verts=bm.verts)
        return self._obj(bm, m)

    def soft(self, size, loc, m, crease=0.55, levels=2, rot=None, tuft=0.0, puff=0.0):
        """Cushion: creased cube, subdivided; optional tuft (centre of the top pulled in) and puff (sides bulge)."""
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        cl = bm.edges.layers.float.get('crease_edge') or bm.edges.layers.float.new('crease_edge')
        for e in bm.edges:
            e[cl] = crease
        bmesh.ops.scale(bm, vec=size, verts=bm.verts)
        o = self._obj(bm, m)
        self._subsurf(o, levels)
        bm = bmesh.new()
        bm.from_mesh(o.data)
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        for v in bm.verts:
            x, y, z = v.co
            if puff:
                k = 1 + puff * (1 - (z / hz) ** 2)
                v.co.x, v.co.y = x * k, y * k
            if tuft and z > 0:
                d = math.sqrt((x / hx) ** 2 + (y / hy) ** 2)
                v.co.z -= tuft * max(0.0, 1 - d / 0.45) ** 2 * hz
        if rot:
            bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=rot, verts=bm.verts)
        bmesh.ops.translate(bm, vec=loc, verts=bm.verts)
        bm.to_mesh(o.data)
        bm.free()
        return o

    def lathe(self, prof, m, seg=32, loc=(0, 0, 0), radial=None, sx=1.0, sy=1.0):
        """Closed surface of revolution: prof = [(r, z), ...] from the axis back to the axis (or a closed loop)."""
        bm = bmesh.new()
        vs = [bm.verts.new((max(r, 0.0), 0.0, z)) for r, z in prof]
        es = [bm.edges.new((vs[i], vs[i + 1])) for i in range(len(vs) - 1)]
        if prof[0][0] > 1e-6 and prof[-1][0] > 1e-6:
            es.append(bm.edges.new((vs[-1], vs[0])))
        bmesh.ops.spin(bm, geom=vs + es, cent=(0, 0, 0), axis=(0, 0, 1), angle=TAU, steps=seg, use_merge=True)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        if radial:
            for v in bm.verts:
                r = math.hypot(v.co.x, v.co.y)
                if r > 1e-6:
                    k = radial(math.atan2(v.co.y, v.co.x), v.co.z, r)
                    v.co.x *= k
                    v.co.y *= k
        for v in bm.verts:
            v.co.x *= sx
            v.co.y *= sy
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bmesh.ops.translate(bm, vec=loc, verts=bm.verts)
        return self._obj(bm, m)

    def cyl(self, r, h, loc, m, seg=16, r2=None, axis='Z', rot=None):
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h)
        R = {'Z': Matrix.Identity(3), 'X': Matrix.Rotation(math.pi / 2, 3, 'Y'), 'Y': Matrix.Rotation(math.pi / 2, 3, 'X')}[axis]
        if rot is not None:
            R = rot @ R
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=R, verts=bm.verts)
        bmesh.ops.translate(bm, vec=loc, verts=bm.verts)
        return self._obj(bm, m)

    def tube(self, pts, r, m, seg=8):
        """Pipe through points (straight segments with ball joints)."""
        for a, b in zip(pts, pts[1:]):
            a, b = Vector(a), Vector(b)
            d = b - a
            q = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
            self.cyl(r, d.length, (a + b) / 2, m, seg=seg, rot=q)
        for p in pts[1:-1]:
            self.sphere(r, p, m, seg=seg, ring=max(4, seg // 2))

    def sphere(self, r, loc, m, seg=16, ring=8, scale=(1, 1, 1)):
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=ring, radius=r)
        bmesh.ops.scale(bm, vec=scale, verts=bm.verts)
        bmesh.ops.translate(bm, vec=loc, verts=bm.verts)
        return self._obj(bm, m)

    # ---------------------------------------------------------------- export
    def save(self, smooth_all=False):
        bpy.ops.object.select_all(action='DESELECT')
        for o in self.objs:
            o.select_set(True)
            for p in o.data.polygons:
                p.use_smooth = smooth_all
        bpy.context.view_layer.objects.active = self.objs[0]
        if len(self.objs) > 1:
            bpy.ops.object.join()
        o = bpy.context.view_layer.objects.active
        o.name = o.data.name = self.name
        path = os.path.join(OUT, self.name + '.glb')
        bpy.ops.export_scene.gltf(filepath=path, use_selection=True, export_format='GLB', export_apply=True)
        tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
        bpy.data.objects.remove(o, do_unlink=True)
        print('[env_custom_r6]', self.name, tris, 'tris', path, flush=True)


def want(name):
    return ONLY is None or name in ONLY


bpy.ops.wm.read_factory_settings(use_empty=True)

WALNUT = mat('r6_walnut', '6B4429')
OAK = mat('r6_oak', 'B07A46')
GREEN_UPH = mat('r6_upholstery', '35634A', 0.9)      # recolour rule target: h 0.38..0.44
BRASS = mat('r6_brass', 'B8913F', 0.35)
CHROME = mat('r6_chrome', 'CFD6DC', 0.25)
BLACK = mat('r6_black', '1B1C20', 0.5)
CHARCOAL = mat('r6_charcoal', '33363C', 0.6)
WHITE = mat('r6_white', 'F2EEE6', 0.5)
CREAM = mat('r6_cream', 'EFE3C8', 0.8)
ENAMEL = mat('r6_enamel_green', '2E6B4A', 0.35)
BEANS = mat('r6_beans', '4A2C1A', 0.8)
STONE = mat('r6_stone', 'E2D3B2', 0.9)               # limestone
STONE_D = mat('r6_stone_shade', 'C9B892', 0.9)
WATER = mat('KEEP_water', '5DB7E8', 0.05, emit=0.2)
SOFT = mat('r6_soft', 'C25560', 0.95)                # single-colour soft goods (recoloured per placement)
BURLAP = mat('r6_burlap', 'C9B48A', 0.95)
TWINE = mat('r6_twine', '8C6A43', 0.9)
GLOWCORE = mat('KEEP_neon_lantern', 'FFB03B', 0.3, emit=3.0)
IRON = mat('r6_iron', '2B2D31', 0.5)

# ------------------------------------------------------------------ café booth (2.4 m)
if want('booth_bench'):
    b = Model('booth_bench')
    L, D = 2.4, 0.62
    b.box((L, D - 0.06, 0.36), (0, 0.02, 0.18), WALNUT, bevel=0.012)                 # plinth
    b.box((L + 0.04, 0.02, 0.06), (0, -D / 2 + 0.02, 0.33), OAK, bevel=0.006)          # nosing
    for i, x in enumerate((-L / 4, L / 4)):                                             # two seat cushions
        b.soft((L / 2 - 0.03, D - 0.12, 0.14), (x, -0.03, 0.43), GREEN_UPH, crease=0.6, puff=0.04)
    b.box((L, 0.12, 0.78), (0, D / 2 - 0.04, 0.75), WALNUT, bevel=0.012)               # back panel
    b.box((L + 0.06, 0.2, 0.05), (0, D / 2 - 0.06, 1.165), OAK, bevel=0.01)             # top cap
    for i in range(4):                                                                  # channel-tufted back cushions
        w = L / 4 - 0.02
        b.soft((w, 0.11, 0.5), (-L / 2 + (i + 0.5) * L / 4, D / 2 - 0.15, 0.8), GREEN_UPH, crease=0.5, puff=0.03,
               rot=Matrix.Rotation(math.radians(-8), 3, 'X'))
    for x in (-L / 2 - 0.03, L / 2 + 0.03):                                             # end panels
        b.box((0.06, D + 0.02, 0.72), (x, 0.0, 0.36), WALNUT, bevel=0.01)
    b.save()

# ------------------------------------------------------------------ studio terrace bench (3.0 m)
if want('bench_cushioned'):
    b = Model('bench_cushioned')
    L = 3.0
    b.box((L, 0.62, 0.06), (0, 0, 0.36), OAK, bevel=0.008)
    for i in range(9):
        b.box((0.07, 0.56, 0.33), (-L / 2 + 0.1 + i * (L - 0.2) / 8, 0, 0.165), WALNUT, bevel=0.006)
    b.box((L - 0.1, 0.56, 0.02), (0, 0, 0.02), WALNUT)
    b.soft((L - 0.06, 0.58, 0.13), (0, -0.01, 0.45), GREEN_UPH, crease=0.6, puff=0.03, levels=1)
    for i in range(3):
        b.soft((0.9, 0.16, 0.42), (-1.0 + i * 1.0, 0.26, 0.69), GREEN_UPH, crease=0.45, puff=0.04, tuft=0.15, levels=1,
               rot=Matrix.Rotation(math.radians(-12), 3, 'X'))
    b.save()

# ------------------------------------------------------------------ soft goods (single colour)
if want('bean_bag'):
    # Big bean bag (concept): a squat sack whose front sinks into a seat hollow (seat ~0.58 m) and whose back rises into
    # a soft backrest (~1.0 m), so the seat measure reads a seat with a back behind it. Hollow centre: 0.10 m in front.
    b = Model('bean_bag')
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=14, radius=1.0)
    for v in bm.verts:
        x, y, z = v.co
        X, Y = x * 0.62, y * 0.58
        Z = 0.27 + (z * 0.45 if z > 0 else z * 0.27)
        up = max(0.0, z)
        back = max(0.0, min(1.0, (y + 0.1) / 0.9))
        Z += 0.34 * back ** 1.5 * up ** 0.6
        Y += 0.06 * back * up                                   # the backrest leans back a little
        d = math.hypot(X, Y + 0.1)
        Z -= 0.15 * max(0.0, 1 - d / 0.34) ** 1.4 * up         # seat hollow
        sag = 1 + 0.08 * (1 - abs(z)) ** 2                      # sack sides bulge at the middle
        v.co = Vector((X * sag, Y * sag, max(0.0, Z)))
    b._obj(bm, SOFT)
    b.save(smooth_all=True)

if want('floor_cushion'):
    b = Model('floor_cushion')
    b.soft((0.66, 0.66, 0.2), (0, 0, 0.1), SOFT, crease=0.35, puff=0.06, tuft=0.35)
    b.sphere(0.022, (0, 0, 0.165), SOFT, seg=8, ring=4)
    b.save(smooth_all=True)

if want('throw_pillow'):
    b = Model('throw_pillow')
    b.soft((0.44, 0.12, 0.42), (0, 0, 0.21), SOFT, crease=0.75, puff=0.05, levels=2)
    bm = bmesh.new(); bm.from_mesh(b.objs[-1].data)
    for v in bm.verts:                      # pinch the corners in a little, plump the middle (a sewn square pillow)
        x, y, z = v.co.x / 0.22, v.co.y / 0.06, (v.co.z - 0.21) / 0.21
        v.co.y *= 1.0 + 0.55 * (1 - min(1.0, x * x)) * (1 - min(1.0, z * z))
    bm.to_mesh(b.objs[-1].data); bm.free()
    b.save(smooth_all=True)

if want('pouf_round'):
    b = Model('pouf_round')
    prof = [(0.0, 0.0), (0.26, 0.0), (0.3, 0.02), (0.325, 0.08), (0.335, 0.2), (0.325, 0.32), (0.3, 0.38), (0.26, 0.405),
            (0.12, 0.415), (0.0, 0.405)]
    b.lathe(prof, SOFT, seg=28, radial=lambda th, z, r: 1 + (0.025 * math.cos(14 * th) if 0.03 < z < 0.39 else 0.0))
    b.save(smooth_all=True)

if want('bar_stool_ring'):
    # Studio pod stool: four slim splayed legs, a brass foot ring at 0.26 m, an apron ring, an upholstered round seat
    # (top 0.78 m). Slim legs and a low ring so the seat measure reads a perching stool with a footrest.
    b = Model('bar_stool_ring')
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        top = Vector((math.cos(a) * 0.13, math.sin(a) * 0.13, 0.7))
        bot = Vector((math.cos(a) * 0.19, math.sin(a) * 0.19, 0.0))
        b.tube([bot, top], 0.018, WALNUT, seg=6)
    ring = [(0.18, 0.25), (0.2, 0.25), (0.2, 0.27), (0.18, 0.27)]
    b.lathe(ring, BRASS, seg=24)
    b.lathe([(0.13, 0.62), (0.15, 0.62), (0.15, 0.645), (0.13, 0.645)], WALNUT, seg=20)
    b.cyl(0.19, 0.04, (0, 0, 0.72), WALNUT, seg=20)
    b.lathe([(0.0, 0.74), (0.19, 0.74), (0.205, 0.755), (0.2, 0.775), (0.17, 0.79), (0.0, 0.795)], GREEN_UPH, seg=20)
    b.save()

if want('headphones'):
    # Studio headphones: padded band over two ear cups (single colour charcoal with orange cushions).
    b = Model('headphones')
    pts = [(math.cos(t) * 0.085, 0.0, 0.09 + math.sin(t) * 0.09) for t in [math.pi * k / 6 for k in range(7)]]
    b.tube(pts, 0.012, CHARCOAL, seg=5)
    for sx in (-1, 1):
        b.cyl(0.045, 0.03, (sx * 0.09, 0, 0.06), CHARCOAL, seg=10, axis='X')
        b.cyl(0.04, 0.012, (sx * 0.07, 0, 0.06), mat('r6_cushion_orange', 'E8743B', 0.9), seg=10, axis='X')
    b.save()

if want('jar_beans'):
    # Glass storage jar (pale glass flattened to the palette) full of coffee beans, cork lid.
    b = Model('jar_beans')
    GLASS = mat('r6_glass', 'CFE3E6', 0.1)
    b.lathe([(0.0, 0.0), (0.07, 0.0), (0.075, 0.01), (0.075, 0.17), (0.06, 0.19), (0.0, 0.19)], GLASS, seg=14)
    b.lathe([(0.0, 0.012), (0.068, 0.012), (0.068, 0.13), (0.0, 0.14)], BEANS, seg=14)
    b.lathe([(0.0, 0.19), (0.058, 0.19), (0.058, 0.23), (0.0, 0.23)], mat('r6_cork', 'B9925F', 0.9), seg=12)
    b.save()

if want('coffee_bag'):
    # Stand-up coffee bag with a folded top and a cream label band (no letters).
    b = Model('coffee_bag')
    b.soft((0.14, 0.08, 0.2), (0, 0, 0.1), mat('r6_kraft', 'A0784C', 0.9), crease=0.85, puff=0.04, levels=1)
    b.box((0.142, 0.06, 0.03), (0, 0, 0.215), mat('r6_kraft', 'A0784C', 0.9))
    b.box((0.143, 0.082, 0.06), (0, 0, 0.1), CREAM)
    b.save()

if want('cup_stack'):
    # Three white cups on saucers, stacked (espresso station / shelf clutter).
    b = Model('cup_stack')
    for k in range(3):
        z = k * 0.075
        b.cyl(0.07, 0.01, (0, 0, z + 0.005), WHITE, seg=14)
        b.lathe([(0.0, 0.01), (0.035, 0.01), (0.047, 0.03), (0.05, 0.07), (0.045, 0.072), (0.0, 0.072)], WHITE, seg=12, loc=(0, 0, z))
    b.save()

if want('hanging_planter'):
    # Macrame hanging planter: cream pot in a rope cradle (the venue adds the cord to the ceiling line and the vine).
    b = Model('hanging_planter')
    ROPE = mat('r6_rope', 'D9C7A3', 0.95)
    b.lathe([(0.0, 0.0), (0.09, 0.0), (0.13, 0.05), (0.15, 0.17), (0.13, 0.19), (0.12, 0.18), (0.0, 0.17)], CREAM, seg=16)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        b.tube([(math.cos(a) * 0.1, math.sin(a) * 0.1, 0.0), (math.cos(a) * 0.16, math.sin(a) * 0.16, 0.18), (0, 0, 0.62)], 0.008, ROPE, seg=4)
    b.lathe([(0.0, 0.17), (0.12, 0.17), (0.0, 0.2)], mat('r6_soil', '5C4630', 0.9), seg=12)
    LEAF = mat('r6_leaf', '4F8A3A', 0.8)
    for k in range(6):
        a = k * math.tau / 6
        b.sphere(0.07, (math.cos(a) * 0.09, math.sin(a) * 0.09, 0.23), LEAF, seg=6, ring=4, scale=(1.3, 1.3, 0.6))
    b.save()

if want('art_frame_easel'):
    pass

# ------------------------------------------------------------------ coffee bar equipment
if want('espresso_machine'):
    b = Model('espresso_machine')
    W, Dp = 0.78, 0.52
    for x in (-W / 2 + 0.06, W / 2 - 0.06):                                             # feet
        for y in (-Dp / 2 + 0.06, Dp / 2 - 0.06):
            b.cyl(0.025, 0.05, (x, y, 0.025), CHROME, seg=10)
    b.box((W, Dp, 0.42), (0, 0, 0.26), ENAMEL, bevel=0.035, seg=3)                       # body
    b.box((W + 0.02, Dp + 0.02, 0.035), (0, 0, 0.485), CHROME, bevel=0.01)              # top plate
    for i in range(5):                                                                  # cup warmer rail
        b.box((W - 0.06, 0.012, 0.012), (0, -Dp / 2 + 0.06 + i * 0.1, 0.51), CHROME)
    for x in (-0.24, -0.08, 0.08, 0.24):                                                # cups on the warmer
        b.lathe([(0.0, 0.0), (0.03, 0.0), (0.04, 0.012), (0.045, 0.06), (0.04, 0.063), (0.035, 0.012), (0.0, 0.012)],
                WHITE, seg=14, loc=(x, 0.06, 0.505))
    b.box((W - 0.12, 0.012, 0.16), (0, -Dp / 2 - 0.005, 0.38), CHROME, bevel=0.004)    # front panel band
    for x in (-0.12, 0.12):                                                             # pressure gauges
        b.cyl(0.035, 0.018, (x, -Dp / 2 - 0.014, 0.4), CHROME, seg=16, axis='Y')
        b.cyl(0.028, 0.006, (x, -Dp / 2 - 0.025, 0.4), WHITE, seg=16, axis='Y')
    for x in (-0.18, 0.18):                                                             # group heads + portafilters
        b.cyl(0.05, 0.06, (x, -Dp / 2 - 0.05, 0.27), CHROME, seg=16)
        b.box((0.03, 0.05, 0.03), (x, -Dp / 2 - 0.025, 0.3), CHROME)
        b.cyl(0.045, 0.035, (x, -Dp / 2 - 0.05, 0.225), CHROME, seg=16)
        b.tube([(x, -Dp / 2 - 0.09, 0.225), (x, -Dp / 2 - 0.2, 0.21)], 0.012, CHROME, seg=8)
        b.tube([(x, -Dp / 2 - 0.2, 0.21), (x, -Dp / 2 - 0.3, 0.19)], 0.016, BLACK, seg=8)
    for s in (-1, 1):                                                                   # steam wands
        x = s * (W / 2 - 0.05)
        b.cyl(0.02, 0.03, (x, -Dp / 2 - 0.02, 0.37), BLACK, seg=10, axis='Y')
        b.tube([(x, -Dp / 2 - 0.03, 0.34), (x + s * 0.02, -Dp / 2 - 0.07, 0.3), (x + s * 0.03, -Dp / 2 - 0.09, 0.12)], 0.007,
               CHROME, seg=6)
    b.box((W - 0.08, 0.17, 0.035), (0, -Dp / 2 - 0.07, 0.07), CHROME, bevel=0.006)      # drip tray
    for i in range(7):
        b.box((W - 0.12, 0.008, 0.006), (0, -Dp / 2 - 0.14 + i * 0.022, 0.09), BLACK)
    for i in range(4):                                                                  # buttons
        b.cyl(0.012, 0.012, (-0.045 + i * 0.03, -Dp / 2 - 0.012, 0.335), BLACK if i % 2 else WHITE, seg=8, axis='Y')
    b.save()

if want('coffee_grinder'):
    b = Model('coffee_grinder')
    b.box((0.2, 0.24, 0.07), (0, 0, 0.035), BLACK, bevel=0.012)
    b.box((0.17, 0.2, 0.3), (0, 0.02, 0.22), CHARCOAL, bevel=0.02, seg=2)
    b.box((0.06, 0.07, 0.07), (0, -0.1, 0.17), CHROME, bevel=0.008)                    # spout
    b.cyl(0.06, 0.03, (0, 0.02, 0.385), CHROME, seg=16)
    b.lathe([(0.0, 0.0), (0.05, 0.0), (0.075, 0.03), (0.105, 0.17), (0.11, 0.2), (0.0, 0.2)], BEANS, seg=20,
            loc=(0, 0.02, 0.4))                                                         # hopper (beans show through)
    b.lathe([(0.0, 0.0), (0.115, 0.0), (0.115, 0.02), (0.05, 0.04), (0.02, 0.06), (0.0, 0.065)], BLACK, seg=20,
            loc=(0, 0.02, 0.6))
    b.save()

if want('pastry_case'):
    b = Model('pastry_case')
    W, Dp, H = 1.5, 0.6, 0.62
    b.box((W, Dp, 0.08), (0, 0, 0.04), WALNUT, bevel=0.01)
    b.box((W, 0.03, H - 0.08), (0, Dp / 2 - 0.015, 0.08 + (H - 0.08) / 2), WALNUT)
    for z in (0.09, 0.34):
        b.box((W - 0.04, Dp - 0.06, 0.012), (0, 0.0, z), CREAM)
    for x in (-W / 2 + 0.01, W / 2 - 0.01):                                             # chrome uprights + side rails
        for y in (-Dp / 2 + 0.01, Dp / 2 - 0.02):
            b.box((0.016, 0.016, H - 0.08), (x, y, 0.08 + (H - 0.08) / 2), CHROME)
        b.box((0.016, Dp, 0.016), (x, 0, H - 0.008), CHROME)
    for y in (-Dp / 2 + 0.01, Dp / 2 - 0.02):
        b.box((W, 0.016, 0.016), (0, y, H - 0.008), CHROME)
    b.box((W, 0.016, 0.016), (0, -Dp / 2 + 0.01, 0.35), CHROME)
    b.save()

# ------------------------------------------------------------------ Timeline Square fountain (local z = deck top)
if want('fountain_tiered'):
    b = Model('fountain_tiered')
    scal = lambda n, amp: (lambda th, z, r: 1 + amp * max(0.0, math.cos(n * th)) ** 2)
    # basin ring: plinth, wall, moulded lip, inner wall down to the water line (closed loop)
    basin = [(2.92, 0.0), (2.92, 0.07), (2.85, 0.1), (2.84, 0.46), (2.9, 0.49), (2.93, 0.55), (2.9, 0.6), (2.82, 0.62),
             (2.5, 0.62), (2.44, 0.58), (2.44, 0.36), (2.6, 0.36), (2.6, 0.0)]
    b.lathe(basin, STONE, seg=56)
    b.cyl(2.45, 0.04, (0, 0, 0.38), WATER, seg=48)                                    # basin water
    # pedestal, column with a ring, lower bowl with a scalloped rim
    low = [(0.0, 0.36), (0.66, 0.36), (0.66, 0.44), (0.56, 0.5), (0.4, 0.58), (0.31, 0.66), (0.28, 0.72), (0.28, 1.08),
           (0.33, 1.12), (0.33, 1.17), (0.27, 1.21), (0.26, 1.26), (0.36, 1.3), (0.62, 1.38), (0.96, 1.48), (1.24, 1.57),
           (1.4, 1.63), (1.44, 1.7), (1.4, 1.75), (1.32, 1.74), (1.3, 1.68), (0.0, 1.68)]
    b.lathe(low, STONE, seg=48, radial=lambda th, z, r: 1 + (0.035 * max(0.0, math.cos(12 * th)) if z > 1.6 and r > 1.2 else 0.0))
    b.cyl(1.31, 0.03, (0, 0, 1.685), WATER, seg=40)                                   # lower bowl water
    # upper column and bowl
    up = [(0.0, 1.68), (0.22, 1.68), (0.2, 1.74), (0.19, 2.06), (0.24, 2.1), (0.24, 2.14), (0.18, 2.18), (0.2, 2.22),
          (0.36, 2.27), (0.58, 2.34), (0.74, 2.41), (0.79, 2.46), (0.76, 2.5), (0.68, 2.49), (0.66, 2.45), (0.0, 2.45)]
    b.lathe(up, STONE_D, seg=36, radial=lambda th, z, r: 1 + (0.04 * max(0.0, math.cos(10 * th)) if z > 2.38 and r > 0.6 else 0.0))
    b.cyl(0.67, 0.02, (0, 0, 2.455), WATER, seg=32)
    fin = [(0.0, 2.45), (0.11, 2.45), (0.11, 2.52), (0.07, 2.56), (0.08, 2.61), (0.13, 2.68), (0.14, 2.76), (0.11, 2.84),
           (0.06, 2.9), (0.0, 2.92)]
    b.lathe(fin, STONE, seg=20)
    b.save()

# ------------------------------------------------------------------ lamps, market, studio
if want('pendant_dome'):
    b = Model('pendant_dome')
    outer = [(0.0, 0.3), (0.04, 0.3), (0.05, 0.26), (0.12, 0.22), (0.2, 0.15), (0.245, 0.06), (0.26, 0.0), (0.24, 0.0),
             (0.225, 0.06), (0.18, 0.13), (0.1, 0.19), (0.0, 0.205)]
    b.lathe(outer, ENAMEL, seg=16)
    b.lathe([(0.0, 0.2), (0.1, 0.185), (0.18, 0.125), (0.222, 0.06), (0.0, 0.06)], WHITE, seg=16)
    b.lathe([(0.0, 0.26), (0.04, 0.26), (0.045, 0.31), (0.03, 0.34), (0.0, 0.345)], BRASS, seg=10)
    b.save()

if want('sack'):
    b = Model('sack')
    prof = [(0.0, 0.0), (0.18, 0.0), (0.23, 0.04), (0.25, 0.16), (0.24, 0.3), (0.19, 0.4), (0.1, 0.46), (0.07, 0.48),
            (0.075, 0.52), (0.1, 0.56), (0.11, 0.6), (0.06, 0.62), (0.0, 0.62)]
    b.lathe(prof, BURLAP, seg=18, radial=lambda th, z, r: 1 + 0.06 * math.sin(3 * th + z * 7) + (0.12 * math.cos(6 * th) if z > 0.52 else 0))
    b.lathe([(0.072, 0.47), (0.085, 0.47), (0.085, 0.5), (0.072, 0.5)], TWINE, seg=14)
    b.save(smooth_all=True)

if want('lantern_floor'):
    b = Model('lantern_floor')
    b.box((0.3, 0.3, 0.04), (0, 0, 0.02), IRON, bevel=0.005, seg=1)
    for x in (-0.12, 0.12):
        for y in (-0.12, 0.12):
            b.box((0.03, 0.03, 0.42), (x, y, 0.25), WALNUT)
    b.box((0.2, 0.2, 0.34), (0, 0, 0.24), GLOWCORE)
    b.cyl(0.04, 0.08, (0, 0, 0.08), CREAM, seg=10)
    b.box((0.32, 0.32, 0.04), (0, 0, 0.48), IRON, bevel=0.005, seg=1)
    b.lathe([(0.0, 0.5), (0.2, 0.5), (0.0, 0.62)], IRON, seg=4, loc=(0, 0, 0))
    b.tube([(-0.06, 0, 0.62), (-0.06, 0, 0.7), (0.06, 0, 0.7), (0.06, 0, 0.62)], 0.008, IRON, seg=6)
    b.save()

if want('table_oval'):
    b = Model('table_oval')
    b.lathe([(0.0, 0.0), (0.42, 0.0), (0.44, 0.02), (0.44, 0.3), (0.46, 0.33), (0.46, 0.37), (0.43, 0.39), (0.0, 0.39)], WALNUT,
            seg=40, sx=1.35, sy=0.8)
    b.save()

# ------------------------------------------------------------------ trailing pothos vine (hangs from its origin, 1 m)
if want('pothos_vine'):
    import random
    rnd = random.Random(11)
    b = Model('pothos_vine')
    LEAF_A = mat('r6_leaf', '4F8A3A', 0.8)
    LEAF_B = mat('r6_leaf_light', '6AA84F', 0.8)
    STEM = mat('r6_stem', '3D6B35', 0.9)
    pts = []
    for i in range(9):
        t = i / 8
        pts.append((0.05 * math.sin(t * 5.0), 0.04 * math.sin(t * 3.0 + 1.0), -t))
    b.tube(pts, 0.006, STEM, seg=4)
    bm = bmesh.new()
    def leaf(c, yaw, size, droop):
        q = Matrix.Rotation(yaw, 3, 'Z') @ Matrix.Rotation(droop, 3, 'X')
        tip = Vector((0, -size * 1.3, 0)); l = Vector((-size * 0.55, -size * 0.45, 0.02 * size)); r = Vector((size * 0.55, -size * 0.45, 0.02 * size))
        base = Vector((0, 0.0, 0)); mid = Vector((0, -size * 0.55, -0.12 * size))
        vs = [bm.verts.new(Vector(c) + q @ v) for v in (base, l, tip, r, mid)]
        bm.faces.new((vs[0], vs[1], vs[4])); bm.faces.new((vs[1], vs[2], vs[4]))
        bm.faces.new((vs[2], vs[3], vs[4])); bm.faces.new((vs[3], vs[0], vs[4]))
        vb_ = [bm.verts.new(v.co.copy()) for v in vs]                 # back side (palette material is one-sided)
        bm.faces.new((vb_[4], vb_[1], vb_[0])); bm.faces.new((vb_[4], vb_[2], vb_[1]))
        bm.faces.new((vb_[4], vb_[3], vb_[2])); bm.faces.new((vb_[4], vb_[0], vb_[3]))
    for k in range(15):
        t = min(1.0, k / 14 + rnd.uniform(-0.02, 0.02))
        x = 0.05 * math.sin(t * 5.0); y = 0.04 * math.sin(t * 3.0 + 1.0); z = -t
        leaf((x, y, z), rnd.uniform(0, math.tau), 0.06 + 0.035 * (1 - t) + rnd.uniform(-0.01, 0.01), math.radians(rnd.uniform(-60, -20)))
    for k in range(4):                                  # crown at the hook
        leaf((rnd.uniform(-0.05, 0.05), rnd.uniform(-0.05, 0.05), rnd.uniform(-0.05, 0.02)), k * 0.9, 0.08, math.radians(-30))
    me_leaves = b._obj(bm, LEAF_A)
    for i, p_ in enumerate(me_leaves.data.polygons):
        p_.material_index = 0
    # second tone on every other leaf
    me_leaves.data.materials.append(LEAF_B)
    for i, p_ in enumerate(me_leaves.data.polygons):
        p_.material_index = (i // 8) % 2
    b.save()

print('[env_custom_r6] done', flush=True)
