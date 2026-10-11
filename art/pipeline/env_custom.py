"""CT Life procedural environment pieces (made by us = CC0) for things no CC0 pack covers well:
blank billboards + Pixel Wall, trading terminals and screens, hospital bed / IV stand / vitals monitor,
stage / podium / mic stand / speakers / studio light / ON AIR sign, server rack, neon frames, ORE furnace
and live-board frame, tombstones, plaza fountain, park bench, and a textured interior build kit
(floors/walls on a 2 m grid using CC0 Poly Haven / ambientCG textures).

Usage: art/pipeline/blender.sh art/pipeline/env_custom.py
Writes art/raw/ctlife_custom/<name>.glb (then env_process.py normalises/exports them like any source).
Materials named KEEP_* are content surfaces (ads, screens, neon, glow) with their own 0-1 UVs, kept out of the
shared palette so the game can paint/drive them. Every other material is a flat colour -> city palette.
"""
import bpy, bmesh, os, sys, math
from mathutils import Vector, Matrix

sys.path.insert(0, os.path.dirname(__file__))
import ctl_config as C

OUT = os.path.join(C.RAW, 'ctlife_custom')
os.makedirs(OUT, exist_ok=True)
PH = os.path.join(C.RAW, 'polyhaven', 'textures')
ACG = os.path.join(C.RAW, 'ambientcg')


def srgb(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


MATS = {}


def mat(name, hexcol='888888', rough=0.7, emit=0.0, tex=None):
    key = (name, hexcol, tex)
    if key in MATS:
        return MATS[key]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = (*srgb(hexcol), 1)
    b.inputs['Roughness'].default_value = rough
    if emit:
        b.inputs['Emission Color'].default_value = (*srgb(hexcol), 1)
        b.inputs['Emission Strength'].default_value = emit
    if tex:
        n = m.node_tree.nodes.new('ShaderNodeTexImage')
        n.image = bpy.data.images.load(tex, check_existing=True)
        m.node_tree.links.new(n.outputs['Color'], b.inputs['Base Color'])
    MATS[key] = m
    return m


def find_tex(folder, *keys):
    for root, _, files in os.walk(folder):
        for f in sorted(files):
            fl = f.lower()
            if all(k in fl for k in keys) and fl.endswith(('.jpg', '.png')):
                return os.path.join(root, f)
    return None


class Builder:
    """Accumulates primitives (each with a material) into one mesh."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.mats = []
        self.uv = self.bm.loops.layers.uv.new('UVMap')

    def _mi(self, m):
        if m not in self.mats:
            self.mats.append(m)
        return self.mats.index(m)

    def _tag(self, geom_faces, m, uv_mode='box', uv_scale=1.0):
        mi = self._mi(m)
        for f in geom_faces:
            f.material_index = mi
            n = f.normal
            for l in f.loops:
                p = l.vert.co
                if uv_mode == 'box':
                    ax = max(range(3), key=lambda i: abs(n[i]))
                    u, v = [(p.y, p.z), (p.x, p.z), (p.x, p.y)][ax]
                    l[self.uv].uv = (u * uv_scale, v * uv_scale)

    def box(self, size, loc, m, bevel=0.0, rot=None, uv_scale=1.0):
        before = set(self.bm.faces)
        mtx = Matrix.Translation(loc)
        if rot:
            mtx = mtx @ rot
        mtx = mtx @ Matrix.Diagonal((*size, 1))
        bmesh.ops.create_cube(self.bm, size=1.0, matrix=mtx)
        new = [f for f in self.bm.faces if f not in before]
        if bevel:
            edges = list({e for f in new for e in f.edges})
            bmesh.ops.bevel(self.bm, geom=edges, offset=bevel, segments=1, affect='EDGES')
            new = [f for f in self.bm.faces if f not in before]
        self.bm.normal_update()
        self._tag(new, m, uv_scale=uv_scale)
        return new

    def cyl(self, r1, r2, depth, loc, m, seg=16, axis='Z', caps=True):
        before = set(self.bm.faces)
        rot = {'Z': Matrix.Identity(4), 'X': Matrix.Rotation(math.pi / 2, 4, 'Y'), 'Y': Matrix.Rotation(math.pi / 2, 4, 'X')}[axis]
        bmesh.ops.create_cone(self.bm, cap_ends=caps, segments=seg, radius1=r1, radius2=r2, depth=depth,
                              matrix=Matrix.Translation(loc) @ rot)
        new = [f for f in self.bm.faces if f not in before]
        self.bm.normal_update()
        self._tag(new, m)
        return new

    def sphere(self, r, loc, m, seg=16, ring=8, scale=(1, 1, 1)):
        before = set(self.bm.faces)
        bmesh.ops.create_uvsphere(self.bm, u_segments=seg, v_segments=ring, radius=r,
                                  matrix=Matrix.Translation(loc) @ Matrix.Diagonal((*scale, 1)))
        new = [f for f in self.bm.faces if f not in before]
        self.bm.normal_update()
        self._tag(new, m)
        return new

    def panel(self, w, h, loc, m, facing='-Y'):
        """single quad with 0..1 UVs (content surface: ad, screen, neon)."""
        hw, hh = w / 2, h / 2
        if facing == '-Y':
            pts = [(-hw, 0, -hh), (hw, 0, -hh), (hw, 0, hh), (-hw, 0, hh)]
        elif facing == '+Y':
            pts = [(hw, 0, -hh), (-hw, 0, -hh), (-hw, 0, hh), (hw, 0, hh)]
        elif facing == '+Z':
            pts = [(-hw, -hh, 0), (hw, -hh, 0), (hw, hh, 0), (-hw, hh, 0)]
        vs = [self.bm.verts.new(Vector(loc) + Vector(p)) for p in pts]
        f = self.bm.faces.new(vs)
        f.material_index = self._mi(m)
        for l, uv in zip(f.loops, [(0, 0), (1, 0), (1, 1), (0, 1)]):
            l[self.uv].uv = uv
        self.bm.normal_update()
        return f

    def tiled_quad(self, w, d, loc, m, tile=2.0, z=0.0):
        """horizontal quad with world-scaled UVs (1 UV unit = `tile` metres)."""
        x0, y0 = loc[0] - w / 2, loc[1] - d / 2
        vs = [self.bm.verts.new((x0, y0, z)), self.bm.verts.new((x0 + w, y0, z)),
              self.bm.verts.new((x0 + w, y0 + d, z)), self.bm.verts.new((x0, y0 + d, z))]
        f = self.bm.faces.new(vs)
        f.material_index = self._mi(m)
        for l in f.loops:
            l[self.uv].uv = (l.vert.co.x / tile, l.vert.co.y / tile)
        return f

    def save(self):
        me = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(me)
        self.bm.free()
        for m in self.mats:
            me.materials.append(m)
        o = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(o)
        bpy.ops.object.select_all(action='DESELECT')
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        path = os.path.join(OUT, self.name + '.glb')
        bpy.ops.export_scene.gltf(filepath=path, use_selection=True, export_format='GLB', export_apply=True)
        bpy.data.objects.remove(o, do_unlink=True)
        print('[env_custom]', self.name, path, flush=True)


bpy.ops.wm.read_factory_settings(use_empty=True)

# palette colours (Sims-ish warm, saturated)
METAL = mat('metal_dark', '3A3D44', 0.4)
METAL_L = mat('metal_light', 'B8BEC6', 0.35)
WHITE = mat('white', 'F2F0EA', 0.6)
BLACK = mat('black', '1B1C20', 0.5)
WOOD = mat('wood', '9C6B43', 0.7)
WOOD_D = mat('wood_dark', '5E3F2A', 0.7)
RED = mat('ct_red', 'E0443A', 0.6)
GREEN = mat('ct_green', '2FBF71', 0.6)
BLUE = mat('ct_blue', '3B82F6', 0.6)
PURPLE = mat('ct_purple', '7C5CFF', 0.6)
ORANGE = mat('ore_orange', 'F28C28', 0.6)
STONE = mat('stone', 'C9C3B6', 0.9)
TEAL = mat('clinic_teal', '67C6C0', 0.6)
PINK = mat('pink', 'F2A7C3', 0.6)
AD = mat('KEEP_ad_slot', 'FFFFFF', 0.8)           # billboard content (0..1 UV)
SCREEN = mat('KEEP_screen', '0B1220', 0.3, emit=1.0)  # terminal/screen content (0..1 UV)
NEON = mat('KEEP_neon', '33F5C8', 0.3, emit=3.0)
GLOW = mat('KEEP_furnace_glow', 'FF7A1A', 0.5, emit=4.0)

# ------------------------------------------------------------------ billboards / ad slots
b = Builder('billboard_large')            # 6 x 3 m on two legs, ad face 0..1
for x in (-2.2, 2.2):
    b.box((0.25, 0.25, 4.2), (x, 0.1, 2.1), METAL, bevel=0.02)
b.box((6.4, 0.3, 3.4), (0, 0.1, 5.4), METAL, bevel=0.03)
b.panel(6.0, 3.0, (0, -0.06, 5.4), AD)
b.box((6.6, 0.6, 0.12), (0, -0.2, 3.66), METAL_L)          # catwalk
for i in range(4):
    b.cyl(0.05, 0.05, 0.25, (-2.4 + i * 1.6, -0.45, 7.2), BLACK, seg=8)   # lamps
b.save()

b = Builder('billboard_wall')             # 4 x 2 m wall-mounted panel
b.box((4.2, 0.12, 2.2), (0, 0.06, 1.1), METAL, bevel=0.02)
b.panel(4.0, 2.0, (0, -0.005, 1.1), AD)
b.save()

b = Builder('billboard_rooftop')          # 5 x 2.5 on a lattice frame
for x in (-2.0, 0, 2.0):
    b.box((0.12, 0.12, 1.4), (x, 0.4, 0.7), METAL)
    b.box((0.12, 0.9, 0.12), (x, 0.1, 0.3), METAL, rot=Matrix.Rotation(0.6, 4, 'X'))
b.box((5.2, 0.2, 2.7), (0, 0.4, 2.65), METAL, bevel=0.02)
b.panel(5.0, 2.5, (0, 0.29, 2.65), AD)
b.save()

b = Builder('ad_kiosk')                   # 2-sided street ad column (Pixel Wall tiles nearby)
b.box((1.4, 0.5, 2.6), (0, 0, 1.3), METAL_L, bevel=0.04)
b.panel(1.2, 2.0, (0, -0.255, 1.4), AD)
b.panel(1.2, 2.0, (0, 0.255, 1.4), AD, facing='+Y')
b.box((1.5, 0.6, 0.12), (0, 0, 2.66), BLACK, bevel=0.02)
b.save()

b = Builder('pixel_wall')                 # Timeline Square Pixel Wall: 8 x 4 m, face = 0..1 (game draws a 32x16 tile grid)
b.box((8.4, 0.4, 4.6), (0, 0.2, 2.6), STONE, bevel=0.05)
b.panel(8.0, 4.0, (0, -0.005, 2.6), AD)
b.box((8.6, 0.6, 0.2), (0, 0.1, 4.95), METAL)
b.box((8.6, 0.6, 0.3), (0, 0.1, 0.15), STONE)
b.save()

b = Builder('sandwich_board')             # A-frame sign, two ad faces
for s in (1, -1):
    rot = Matrix.Rotation(s * 0.18, 4, 'X')
    b.box((0.62, 0.04, 0.95), (0, s * 0.1, 0.47), WOOD, rot=rot)
b.panel(0.52, 0.8, (0, -0.19, 0.5), AD)
b.save()

# ------------------------------------------------------------------ screens / terminals
b = Builder('trading_terminal')           # standing kiosk with tilted screen (Terminal Café)
b.box((0.6, 0.45, 0.08), (0, 0, 0.04), METAL)
b.box((0.18, 0.18, 1.0), (0, 0.05, 0.55), METAL, bevel=0.02)
rot = Matrix.Rotation(-0.35, 4, 'X')
b.box((0.66, 0.08, 0.46), (0, 0.0, 1.2), BLACK, rot=rot, bevel=0.02)
f = b.panel(0.6, 0.4, (0, 0, 0), SCREEN)
for v in f.verts:
    v.co = rot @ v.co + Vector((0, -0.045, 1.2))
b.box((0.5, 0.2, 0.03), (0, -0.22, 0.98), METAL_L, rot=Matrix.Rotation(-0.25, 4, 'X'))   # keyboard ledge
b.save()

b = Builder('wall_screen_large')          # 4 x 2.25 m (16:9) "Pulse" wall display
b.box((4.15, 0.12, 2.4), (0, 0.06, 1.2), BLACK, bevel=0.02)
b.panel(4.0, 2.25, (0, -0.005, 1.2), SCREEN)
b.save()

b = Builder('monitor_triple')             # Perp Pit: three monitors on an arm
b.box((0.3, 0.22, 0.03), (0, 0.05, 0.015), METAL)
b.box((0.05, 0.05, 0.42), (0, 0.08, 0.22), METAL)
b.box((1.5, 0.05, 0.05), (0, 0.08, 0.45), METAL)
for i, (x, yaw) in enumerate(((-0.56, 0.35), (0, 0), (0.56, -0.35))):
    r = Matrix.Rotation(yaw, 4, 'Z')
    b.box((0.54, 0.035, 0.33), (x, 0.03, 0.52), BLACK, rot=r)
    f = b.panel(0.5, 0.29, (0, 0, 0), SCREEN)
    for v in f.verts:
        v.co = r @ v.co + Vector((x, 0.03 - 0.02, 0.52))
b.save()

b = Builder('trading_desk')               # 1.8 m desk with drawer, cable tray; monitors are separate
b.box((1.8, 0.8, 0.05), (0, 0, 0.74), WOOD_D, bevel=0.01)
for x in (-0.85, 0.85):
    b.box((0.06, 0.7, 0.72), (x, 0, 0.36), METAL)
b.box((0.45, 0.6, 0.5), (0.6, 0.0, 0.42), METAL_L, bevel=0.01)
b.save()

b = Builder('server_rack')                # 0.6 x 1 x 2 m rack with blinking LED strip (neon)
b.box((0.6, 1.0, 2.0), (0, 0, 1.0), BLACK, bevel=0.02)
for i in range(10):
    b.box((0.5, 0.02, 0.14), (0, -0.505, 0.2 + i * 0.18), METAL)
    b.panel(0.38, 0.02, (0, -0.517, 0.2 + i * 0.18 + 0.04), NEON)
b.save()

for name, w, h in (('neon_frame_wide', 2.4, 0.8), ('neon_frame_square', 1.0, 1.0)):
    b = Builder(name)                     # neon sign frame: game drives the glyphs via the KEEP_neon UV 0..1
    b.box((w + 0.12, 0.08, h + 0.12), (0, 0.04, h / 2 + 0.06), BLACK, bevel=0.02)
    b.panel(w, h, (0, -0.005, h / 2 + 0.06), NEON)
    b.save()

# ------------------------------------------------------------------ Rekt Clinic
b = Builder('hospital_bed')
b.box((0.95, 2.05, 0.12), (0, 0, 0.55), METAL_L, bevel=0.01)
b.box((0.9, 1.95, 0.16), (0, 0, 0.69), WHITE, bevel=0.05)              # mattress
b.box((0.7, 0.4, 0.1), (0, 0.72, 0.8), mat('pillow', 'E8F1F8'), bevel=0.04)
b.box((0.92, 1.2, 0.05), (0, -0.35, 0.78), TEAL, bevel=0.02)            # blanket
for y in (-1.0, 1.0):
    b.box((0.95, 0.05, 0.55 if y > 0 else 0.4), (0, y, 0.55 + (0.27 if y > 0 else 0.2)), METAL_L, bevel=0.01)   # head/foot boards
for x in (-0.5, 0.5):
    b.box((0.03, 1.1, 0.03), (x, 0.1, 0.92), METAL_L)                     # side rails
for x in (-0.4, 0.4):
    for y in (-0.9, 0.9):
        b.box((0.04, 0.04, 0.45), (x, y, 0.27), METAL)
        b.cyl(0.05, 0.05, 0.04, (x, y, 0.05), BLACK, seg=10, axis='X')
b.save()

b = Builder('iv_stand')
b.cyl(0.22, 0.22, 0.04, (0, 0, 0.05), METAL, seg=5)
b.cyl(0.015, 0.015, 1.9, (0, 0, 1.0), METAL_L, seg=8)
b.box((0.3, 0.02, 0.02), (0, 0, 1.92), METAL_L)
b.box((0.12, 0.05, 0.2), (0.12, 0, 1.78), mat('iv_bag', 'D9EEF7', 0.2), bevel=0.02)
b.save()

b = Builder('vitals_monitor')
b.cyl(0.25, 0.25, 0.04, (0, 0, 0.05), METAL, seg=5)
b.cyl(0.025, 0.025, 1.2, (0, 0, 0.65), METAL_L, seg=8)
b.box((0.45, 0.12, 0.35), (0, 0, 1.35), WHITE, bevel=0.02)
b.panel(0.38, 0.26, (0, -0.062, 1.36), SCREEN)
b.save()

b = Builder('privacy_curtain')
b.box((0.03, 0.03, 2.2), (-1.0, 0, 1.1), METAL_L)
b.box((0.03, 0.03, 2.2), (1.0, 0, 1.1), METAL_L)
b.box((2.05, 0.03, 0.03), (0, 0, 2.2), METAL_L)
bm_c = Builder('tmp')
for i in range(16):
    x = -0.95 + i * 0.127
    b.box((0.13, 0.05 + 0.03 * (i % 2), 1.7), (x, 0.02 * (i % 2), 1.2), mat('curtain', 'A8D8D2', 0.9))
b.save()
bm_c.bm.free()

b = Builder('tombstone')                  # REKT tombstone (rekt events)
b.box((0.7, 0.2, 0.75), (0, 0, 0.38), STONE, bevel=0.06)
b.cyl(0.35, 0.35, 0.2, (0, 0, 0.75), STONE, seg=16, axis='Y')
b.box((0.95, 0.5, 0.08), (0, 0.05, 0.04), mat('stone_dark', '8D877C', 0.9), bevel=0.02)
b.panel(0.5, 0.3, (0, -0.105, 0.55), AD)  # engraving / name plate drawn by the game
b.save()

# ------------------------------------------------------------------ Spaces Studio
b = Builder('stage_platform')             # 6 x 4 m stage, 0.6 m high, steps at the front
b.box((6.0, 4.0, 0.6), (0, 0, 0.3), WOOD_D, bevel=0.03)
b.box((6.05, 0.06, 0.08), (0, -2.0, 0.6), PURPLE)       # LED edge strip
for i, h in enumerate((0.2, 0.4)):
    b.box((1.4, 0.35, h), (2.0, -2.17 - 0.35 * (1 - i), h / 2), WOOD, bevel=0.01)
b.save()

b = Builder('podium')
b.box((0.7, 0.5, 1.05), (0, 0, 0.525), WOOD_D, bevel=0.03)
b.box((0.8, 0.6, 0.06), (0, 0, 1.1), WOOD, rot=Matrix.Rotation(-0.15, 4, 'X'), bevel=0.01)
b.panel(0.5, 0.3, (0, -0.256, 0.6), AD)   # project logo slot
b.save()

b = Builder('mic_stand')
b.cyl(0.14, 0.14, 0.03, (0, 0, 0.02), BLACK, seg=16)
b.cyl(0.012, 0.012, 1.45, (0, 0, 0.75), METAL_L, seg=8)
b.box((0.02, 0.25, 0.02), (0, -0.1, 1.48), METAL_L, rot=Matrix.Rotation(-0.4, 4, 'X'))
b.cyl(0.025, 0.02, 0.12, (0, -0.22, 1.55), BLACK, seg=10, axis='Y')
b.sphere(0.03, (0, -0.29, 1.55), METAL, seg=10, ring=6)
b.save()

b = Builder('studio_speaker')
b.box((0.45, 0.4, 0.8), (0, 0, 0.4), BLACK, bevel=0.02)
b.cyl(0.15, 0.15, 0.02, (0, -0.205, 0.32), METAL, seg=20, axis='Y')
b.cyl(0.07, 0.07, 0.02, (0, -0.205, 0.64), METAL, seg=16, axis='Y')
b.save()

b = Builder('studio_light')               # softbox light on a tripod
for a in range(3):
    ang = a * 2 * math.pi / 3
    b.box((0.02, 0.02, 1.0), (math.cos(ang) * 0.2, math.sin(ang) * 0.2, 0.45), METAL,
          rot=Matrix.Rotation(-0.45, 4, Vector((-math.sin(ang), math.cos(ang), 0))))
b.cyl(0.015, 0.015, 1.0, (0, 0, 1.3), METAL_L, seg=8)
b.box((0.55, 0.3, 0.45), (0, -0.05, 1.85), BLACK, bevel=0.02)
b.panel(0.5, 0.4, (0, -0.205, 1.85), mat('KEEP_softbox', 'FFF7E6', 0.5, emit=2.0))
b.save()

b = Builder('on_air_sign')
b.box((1.3, 0.12, 0.45), (0, 0.06, 0.25), BLACK, bevel=0.03)
b.panel(1.2, 0.35, (0, -0.005, 0.25), mat('KEEP_on_air', 'FF2D2D', 0.4, emit=3.0))
b.save()

b = Builder('podcast_table')
b.box((2.2, 0.9, 0.05), (0, 0, 0.75), WOOD, bevel=0.01)
b.cyl(0.08, 0.08, 0.72, (0, 0, 0.37), METAL, seg=12)
b.box((0.8, 0.5, 0.03), (0, 0, 0.02), METAL)
for x in (-0.7, 0, 0.7):
    b.box((0.12, 0.12, 0.02), (x, -0.25, 0.785), BLACK)
    b.cyl(0.006, 0.006, 0.3, (x, -0.25, 0.94), METAL_L, seg=6)
    b.cyl(0.025, 0.02, 0.1, (x, -0.3, 1.08), BLACK, seg=10, axis='Y')
b.save()

# ------------------------------------------------------------------ ORE Refinery
b = Builder('ore_furnace')                # 4 m furnace; KEEP_furnace_glow brightness = city-wide staked ORE
b.cyl(1.6, 1.4, 3.0, (0, 0, 1.5), mat('furnace_brick', '6E4A3A', 0.9), seg=24)
b.cyl(1.7, 1.7, 0.3, (0, 0, 0.15), METAL, seg=24)
b.cyl(1.45, 1.0, 1.2, (0, 0, 3.6), METAL, seg=24)
b.cyl(0.45, 0.45, 2.5, (0, 0, 5.4), METAL, seg=16)
for i in range(6):
    a = i * math.pi / 3
    b.box((0.18, 0.18, 3.0), (math.cos(a) * 1.55, math.sin(a) * 1.55, 1.5), METAL)
f = b.panel(1.4, 1.0, (0, -1.47, 1.2), GLOW)   # furnace mouth
b.box((1.7, 0.3, 0.15), (0, -1.5, 0.62), METAL)
b.box((1.7, 0.3, 0.15), (0, -1.5, 1.78), METAL)
b.save()

b = Builder('ore_board_frame')            # giant live 5x5 ORE board: 5 x 5 m screen on a frame
b.box((5.6, 0.4, 5.6), (0, 0.2, 3.4), METAL, bevel=0.05)
b.panel(5.0, 5.0, (0, -0.005, 3.4), SCREEN)
for x in (-2.4, 2.4):
    b.box((0.4, 0.4, 0.8), (x, 0.2, 0.4), METAL)
b.box((5.6, 0.5, 0.6), (0, 0.15, 6.5), BLACK)
b.panel(5.2, 0.5, (0, -0.105, 6.5), NEON)    # round timer / motherlode strip
b.save()

b = Builder('stake_terminal')             # Refinery terminal (stake / unstake / claim)
b.box((0.8, 0.6, 1.1), (0, 0, 0.55), ORANGE, bevel=0.04)
b.box((0.85, 0.65, 0.08), (0, 0, 1.14), METAL)
rot = Matrix.Rotation(-0.5, 4, 'X')
b.box((0.7, 0.06, 0.5), (0, -0.05, 1.45), BLACK, rot=rot, bevel=0.02)
f = b.panel(0.62, 0.42, (0, 0, 0), SCREEN)
for v in f.verts:
    v.co = rot @ v.co + Vector((0, -0.09, 1.45))
b.save()

b = Builder('ore_crystal_pile')
import random
random.seed(4)
for i in range(9):
    a = random.random() * math.tau
    r = random.random() * 0.5
    h = 0.3 + random.random() * 0.5
    b.cyl(0.12 + random.random() * 0.08, 0.0, h, (math.cos(a) * r, math.sin(a) * r, h / 2), mat('KEEP_ore_glow', 'FFB547', 0.3, emit=2.0), seg=6)
b.cyl(0.8, 0.6, 0.15, (0, 0, 0.07), mat('rock', '5C5650', 0.9), seg=10)
b.save()

# ------------------------------------------------------------------ city / plaza
b = Builder('plaza_fountain')             # Timeline Square centre piece
b.cyl(2.4, 2.4, 0.5, (0, 0, 0.25), STONE, seg=32)
b.cyl(2.1, 2.1, 0.06, (0, 0, 0.47), mat('KEEP_water', '5DB7E8', 0.05, emit=0.2), seg=32)
b.cyl(0.5, 0.35, 1.6, (0, 0, 1.2), STONE, seg=16)
b.cyl(1.0, 0.8, 0.25, (0, 0, 2.0), STONE, seg=24)
b.cyl(0.25, 0.1, 0.8, (0, 0, 2.5), mat('gold', 'E8B931', 0.3), seg=12)
b.save()

b = Builder('park_bench')
for x in (-0.75, 0.75):
    b.box((0.08, 0.55, 0.45), (x, 0, 0.225), METAL, bevel=0.01)
    b.box((0.08, 0.08, 0.45), (x, 0.22, 0.65), METAL)
for i in range(4):
    b.box((1.7, 0.11, 0.035), (0, -0.2 + i * 0.13, 0.46), WOOD, bevel=0.008)
for i in range(3):
    b.box((1.7, 0.035, 0.1), (0, 0.25, 0.6 + i * 0.13), WOOD, bevel=0.008)
b.save()

b = Builder('bollard')
b.cyl(0.11, 0.11, 0.9, (0, 0, 0.45), METAL, seg=12)
b.sphere(0.12, (0, 0, 0.9), METAL, seg=12, ring=6, scale=(1, 1, 0.6))
b.save()

b = Builder('crypto_atm')                 # "Cash top-up" ATM (CT City)
b.box((0.8, 0.7, 1.9), (0, 0, 0.95), PURPLE, bevel=0.05)
b.box((0.6, 0.15, 0.55), (0, -0.33, 1.4), BLACK, bevel=0.02)
b.panel(0.5, 0.38, (0, -0.41, 1.42), SCREEN)
b.box((0.5, 0.25, 0.08), (0, -0.4, 1.02), METAL_L)
b.panel(0.6, 0.2, (0, -0.355, 1.78), NEON)
b.save()

b = Builder('news_ticker')                # LED ticker band for building fronts (Timeline Square)
b.box((8.2, 0.25, 0.7), (0, 0.12, 0.35), BLACK, bevel=0.02)
b.panel(8.0, 0.5, (0, -0.006, 0.35), SCREEN)
b.save()

# ------------------------------------------------------------------ interior build kit (2 m grid)
wood_floor = find_tex(os.path.join(PH, 'wood_floor'), 'diff') or find_tex(os.path.join(PH, 'wood_floor'), '')
tiles = find_tex(os.path.join(PH, 'floor_tiles_06'), 'diff')
plaster = find_tex(os.path.join(PH, 'painted_plaster_wall'), 'diff')
brick = find_tex(os.path.join(PH, 'red_brick'), 'diff')
marble = find_tex(os.path.join(PH, 'marble_01'), 'diff')
paving = find_tex(os.path.join(PH, 'rectangular_paving'), 'diff')
hexpave = find_tex(os.path.join(PH, 'hexagonal_concrete_paving'), 'diff')
concrete = find_tex(os.path.join(PH, 'concrete_floor_02'), 'diff')
metal_plate = find_tex(os.path.join(PH, 'metal_plate'), 'diff')
print('[env_custom] textures', wood_floor, tiles, plaster, brick, marble, paving, hexpave, concrete, metal_plate)


def floor_piece(name, tex, tile=2.0, size=2.0, thick=0.1):
    b = Builder(name)
    m = mat('TEX_' + name, 'FFFFFF', 0.8, tex=tex)
    b.tiled_quad(size, size, (0, 0), m, tile=tile, z=0.0)
    # thin skirt so tiles read as slabs from the tilt camera
    b.box((size, size, thick), (0, 0, -thick / 2 - 0.001), mat('slab_edge', '8B867D', 0.9))
    b.save()


floor_piece('floor_wood_2m', wood_floor, tile=2.0)
floor_piece('floor_tiles_2m', tiles, tile=1.0)
floor_piece('floor_marble_2m', marble, tile=2.0)
floor_piece('floor_concrete_2m', concrete, tile=2.0)
floor_piece('floor_metal_2m', metal_plate, tile=2.0)
floor_piece('plaza_paving_2m', paving, tile=2.0)
floor_piece('plaza_hexpaving_2m', hexpave, tile=2.0)


def wall_piece(name, tex, kind='solid', h=3.0, w=2.0, t=0.15):
    b = Builder(name)
    m = mat('TEX_' + name.split('_2m')[0], 'FFFFFF', 0.85, tex=tex)
    if kind == 'solid':
        b.box((w, t, h), (0, 0, h / 2), m, uv_scale=0.5)
    elif kind == 'window':
        b.box((w, t, 0.9), (0, 0, 0.45), m, uv_scale=0.5)
        b.box((w, t, 0.6), (0, 0, h - 0.3), m, uv_scale=0.5)
        b.box((0.3, t, 1.5), (-w / 2 + 0.15, 0, 1.65), m, uv_scale=0.5)
        b.box((0.3, t, 1.5), (w / 2 - 0.15, 0, 1.65), m, uv_scale=0.5)
        b.box((1.4, 0.04, 1.5), (0, 0, 1.65), mat('KEEP_glass', 'BFE3F2', 0.05))
        b.box((1.5, t + 0.06, 0.05), (0, -0.03, 0.92), WHITE)
    elif kind == 'door':
        b.box((0.5, t, h), (-0.75, 0, h / 2), m, uv_scale=0.5)
        b.box((0.5, t, h), (0.75, 0, h / 2), m, uv_scale=0.5)
        b.box((1.0, t, 0.8), (0, 0, h - 0.4), m, uv_scale=0.5)
        b.box((1.06, t + 0.04, 0.06), (0, 0, 2.2), WHITE)
    b.box((w, t + 0.02, 0.1), (0, 0, 0.05), mat('baseboard', 'EDE8DD', 0.6))   # skirting
    b.save()


for tex, label in ((plaster, 'plaster'), (brick, 'brick')):
    for kind in ('solid', 'window', 'door'):
        wall_piece(f'wall_{label}_{kind}_2m', tex, kind)

print('[env_custom] done')
