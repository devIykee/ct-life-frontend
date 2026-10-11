"""CT Life lookdev: Terminal Café interior (Sims-style cutaway), golden hour + neon.

Run (from repo root):
  /Applications/Blender.app/Contents/MacOS/Blender -b -P art/lookdev/terminal_cafe.py -- [--preview] [--no-save]
Output: art/lookdev/renders/terminal_cafe_raw.png, art/lookdev/terminal_cafe.blend  (then post.py cafe)
"""
import math
import os
import random
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lookdev_common as L  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = "--preview" in ARGS
OUTDIR = os.path.join(L.HERE, "renders")
os.makedirs(OUTDIR, exist_ok=True)
T = lambda f: os.path.join(L.TEX, f)  # noqa: E731
DCT = os.path.join(L.SRC, "quaternius_downtown_city_megakit", "Textures")

L.reset()
rnd = random.Random(11)
R90 = math.pi / 2
W0, W1, D0, D1, HGT = -8.0, 8.0, -6.0, 6.0, 4.2

# ------------------------------------------------------------------ materials
M = {
    "floor": L.mat_pbr("cafe_floor", "wood_floor", scale=0.5, tint=(1.05, 0.92, 0.82)),
    "brick": L.mat_tex_box("cafe_brick", os.path.join(DCT, "T_RedBrick_BaseColor.png"), os.path.join(DCT, "T_RedBrick_Normal.png"),
                           scale=0.33, tint=(1.0, 0.85, 0.78), rough=0.85),
    "plaster": L.mat_pbr("cafe_plaster", "plastered_wall_04", scale=0.4, tint=(0.30, 0.50, 0.52)),
    "plaster_light": L.mat_pbr("cafe_plaster_light", "plastered_wall_04", scale=0.4, tint=(0.95, 0.9, 0.82)),
    "black": L.mat_color("cafe_black", (0.02, 0.022, 0.026), rough=0.35, metal=0.5),
    "frame": L.mat_color("win_frame", (0.04, 0.04, 0.045), rough=0.4, metal=0.7),
    "marble": L.mat_color("counter_top", (0.92, 0.9, 0.86), rough=0.18, coat=0.4),
    "wood_dark": L.mat_color("counter_wood", (0.18, 0.09, 0.05), rough=0.5),
    "chrome": L.mat_color("chrome", (0.85, 0.85, 0.88), rough=0.12, metal=1.0),
    "mug": L.mat_color("mug", (0.95, 0.95, 0.93), rough=0.25),
    "mug_green": L.mat_color("mug_green", (0.15, 0.7, 0.4), rough=0.25),
    "key": L.mat_color("keyboard", (0.08, 0.08, 0.1), rough=0.5),
    "rug": L.mat_color("rug", (0.55, 0.22, 0.16), rough=0.95),
    "bulb": L.mat_color("bulb", (1, 0.9, 0.7), emit=(1.0, 0.75, 0.42), emit_strength=25),
    "outside": L.mat_pbr("outside_pave", "concrete_pavement", scale=0.35, tint=(1.0, 0.95, 0.9)),
    "grass": L.mat_pbr("outside_grass", "leafy_grass", scale=0.5, tint=(0.58, 1.0, 0.42)),
    "ai_face": L.mat_screen("ai_face", T("cafe_chart_crab.png"), strength=4.0),
}

# ------------------------------------------------------------------ shell
L.ground("outside", -60, -60, 60, 60, -0.02, M["outside"])
L.ground("lawn_out", -16, -10, -9, 10, 0.0, M["grass"])
L.ground("floor", W0, D0, W1, D1, 0.0, M["floor"], thick=0.1)
L.ground("rug", 3.6, -4.6, 7.4, -0.6, 0.012, M["rug"])
# back wall (brick) and left wall (teal plaster) with windows
L.box("wall_back", (W1 - W0 + 0.6, 0.3, HGT), (0, D1 + 0.15, HGT / 2), M["brick"])
WIN = [(-4.6, -1.6), (0.4, 3.4)]  # window spans along Y on the left wall
segs, y = [], D0 - 0.3
for (a, b) in WIN:
    segs.append((y, a)); y = b
segs.append((y, D1 + 0.3))
for (a, b) in segs:
    L.box("wall_left", (0.3, b - a, HGT), (W0 - 0.15, (a + b) / 2, HGT / 2), M["plaster"])
for (a, b) in WIN:
    L.box("wall_left_sill", (0.3, b - a, 0.9), (W0 - 0.15, (a + b) / 2, 0.45), M["plaster"])
    L.box("wall_left_head", (0.3, b - a, 0.8), (W0 - 0.15, (a + b) / 2, HGT - 0.4), M["plaster"])
    for yy in (a + 0.03, (a + b) / 2, b - 0.03):  # mullions
        L.box("mullion", (0.08, 0.06, HGT - 1.7), (W0 - 0.1, yy, 0.9 + (HGT - 1.7) / 2), M["frame"])
    L.box("transom", (0.08, b - a, 0.06), (W0 - 0.1, (a + b) / 2, 2.6), M["frame"])
# cut-away stubs (Sims "walls down") + invisible full walls/ceiling that still cast shadows
L.box("stub_front", (W1 - W0 + 0.6, 0.3, 0.55), (0, D0 - 0.15, 0.275), M["plaster_light"])
L.box("stub_right", (0.3, D1 - D0, 0.55), (W1 + 0.15, 0, 0.275), M["plaster_light"])
for (nm, size, loc) in [("shadow_front", (W1 - W0 + 0.6, 0.3, HGT), (0, D0 - 0.15, HGT / 2)),
                        ("shadow_right", (0.3, D1 - D0, HGT), (W1 + 0.15, 0, HGT / 2)),
                        ("shadow_ceiling", (W1 - W0 + 0.6, D1 - D0 + 0.6, 0.2), (0, 0, HGT + 0.1))]:
    o = L.box(nm, size, loc, M["plaster_light"])
    o.visible_camera = False
    o.visible_glossy = False

# ------------------------------------------------------------------ back-wall screens & neon
L.box("pulse_frame", (5.3, 0.14, 3.3), (-1.6, D1 - 0.07, 2.25), M["black"], bevel=0.03)
L.plane("pulse_screen", 5.1, 3.1875, (-1.6, D1 - 0.15, 2.25), mat=L.mat_screen("pulse", T("cafe_pulse.png"), strength=3.2))
for (x, tex) in [(-6.3, "cafe_chart_pump.png")]:
    L.box("wall_tv_frame", (2.1, 0.1, 1.36), (x, D1 - 0.05, 2.6), M["black"], bevel=0.02)
    L.plane("wall_tv", 2.0, 1.25, (x, D1 - 0.11, 2.6), mat=L.mat_screen("wall_" + tex, T(tex), strength=3.0))
L.plane("neon_cafe", 4.6, 1.15, (4.9, D1 - 0.04, 3.35), mat=L.mat_neon("neon_cafe", T("neon_terminal_cafe.png"), strength=10))
L.plane("neon_gm", 1.2, 0.9, (-6.3, D1 - 0.04, 1.4), mat=L.mat_neon("neon_gm", T("neon_gm.png"), strength=10))
L.plane("neon_wagmi", 1.9, 0.71, (W0 + 0.02, -0.6, 2.35), (0, 0, R90), mat=L.mat_neon("neon_wagmi", T("neon_wagmi.png"), strength=10))
L.area_light("neon_cafe_light", (4.9, D1 - 0.6, 3.3), (math.radians(90), 0, 0), 4.0, 120, (1.0, 0.25, 0.75), size_y=0.8)
L.area_light("neon_wagmi_light", (W0 + 0.6, -0.6, 2.35), (0, math.radians(-90), 0), 1.8, 60, (0.2, 0.9, 1.0), size_y=0.7)
L.area_light("pulse_light", (-1.6, D1 - 0.8, 2.25), (math.radians(90), 0, 0), 4.5, 90, (0.45, 0.6, 1.0), size_y=2.8)

# ------------------------------------------------------------------ coffee bar
L.box("counter", (5.6, 0.9, 1.0), (4.8, 4.75, 0.5), M["wood_dark"], bevel=0.02)
L.box("counter_top", (5.8, 1.05, 0.06), (4.8, 4.75, 1.03), M["marble"], bevel=0.015)
L.plane("neon_nfa", 3.6, 0.56, (4.8, 4.28, 0.55), mat=L.mat_neon("neon_nfa", T("neon_nfa.png"), strength=7))
L.box("espresso", (0.8, 0.5, 0.5), (6.6, 4.95, 1.31), M["chrome"], bevel=0.04)
L.box("espresso_top", (0.82, 0.52, 0.06), (6.6, 4.95, 1.59), M["black"])
L.box("grinder", (0.25, 0.25, 0.55), (5.9, 5.0, 1.33), M["black"], bevel=0.03)
L.box("shelf_back", (5.6, 0.3, 0.05), (4.8, D1 - 0.2, 2.0), M["wood_dark"])
for i in range(14):
    L.cylinder("cup", 0.05, 0.11, (2.4 + i * 0.36, D1 - 0.2, 2.03), M["mug"] if i % 3 else M["mug_green"], seg=12)
for i in range(5):
    L.cylinder("bar_mug", 0.05, 0.11, (2.6 + i * 0.9 + rnd.uniform(-0.1, 0.1), 4.45, 1.06), M["mug"], seg=12)
STOOL = L.load("bar_chair_round_01", os.path.join(L.PH, "bar_chair_round_01", "bar_chair_round_01_1k.gltf"))
for x in (2.9, 4.2, 5.5, 6.8):
    L.inst(STOOL, (x, 3.75, 0), rot=rnd.uniform(0, 6.28), scale=1.05)

# ------------------------------------------------------------------ trading desks
DESK = L.load("metal_office_desk", os.path.join(L.PH, "metal_office_desk", "metal_office_desk_1k.gltf"))
CHAIR = L.load("modern_arm_chair_01", os.path.join(L.PH, "modern_arm_chair_01", "modern_arm_chair_01_1k.gltf"))
LAPTOP = L.load("classic_laptop", os.path.join(L.PH, "classic_laptop", "classic_laptop_1k.gltf"))
CHARTS = ["cafe_chart_pump.png", "cafe_chart_dump.png", "cafe_chart_crab.png", "kiosk_chart.png"]


def monitor(x, y, rot_z, tex, z=0.8, w=0.66):
    h = w / 1.6
    fx, fy = math.sin(rot_z), -math.cos(rot_z)
    L.box("mon_stand", (0.06, 0.06, 0.22), (x - fx * 0.04, y - fy * 0.04, z + 0.11), M["black"], rot=rot_z)
    L.box("mon_foot", (0.25, 0.18, 0.015), (x - fx * 0.04, y - fy * 0.04, z + 0.008), M["black"], rot=rot_z)
    L.box("mon_frame", (w + 0.04, 0.035, h + 0.04), (x, y, z + 0.22 + h / 2), M["black"], rot=rot_z, bevel=0.008)
    L.plane("mon_screen", w, h, (x + fx * 0.019, y + fy * 0.019, z + 0.22 + h / 2), (0, 0, rot_z),
            mat=L.mat_screen("mon_" + tex, T(tex), strength=2.6))


def desk(x, y, face):
    """face=-1: monitors face -Y, trader sits on the -Y side facing +Y (back to camera).
    face=+1: monitors face +Y, trader sits on the +Y side facing -Y (toward camera)."""
    rot = 0.0 if face < 0 else math.pi          # desk + monitor base orientation
    L.inst(DESK, (x, y, 0), rot=rot)
    my = y - 0.25 * face                          # monitors on the far edge from the trader
    for i, dx in enumerate((-0.68, 0.0, 0.68)):
        turn = 0.0 if dx == 0 else (0.32 if dx < 0 else -0.32) * (-face)
        monitor(x + dx, my - (0.08 * face if dx else 0.0), rot + turn, CHARTS[(int(x * 3) + i) % 4])
    L.box("keyboard", (0.46, 0.15, 0.02), (x, y + 0.18 * face, 0.8), M["key"], rot=rot)
    L.cylinder("mug", 0.045, 0.1, (x + 0.6, y + 0.15 * face, 0.79), M["mug_green"] if rnd.random() < 0.5 else M["mug"], seg=12)
    cy = y + 0.85 * face
    trader_rot = math.pi if face < 0 else 0.0    # chair + person face the monitors
    L.inst(CHAIR, (x, cy, 0), rot=trader_rot)
    # posed-human origin sits ~0.35 m in front of the hips (toward the knees)
    py = cy + (0.35 if face < 0 else -0.35)
    return py, trader_rot


people = []


def P(sex, pose, x, y, rot, z=0.0, **kw):
    o = L.person(sex, pose, (x, y, z), rot, rnd, name=f"C_{len(people)}", **kw)
    people.append(o)
    return o


# back row: monitors face the camera, we look over the traders' shoulders
for i, x in enumerate((-5.2, -2.3, 0.6)):
    cy, rot = desk(x, 1.7, -1)
    sex, pose = [("M", "Sitting"), ("F", "Sitting"), ("M", "Sitting")][i]
    P(sex, pose, x, cy, rot)
# front row: traders face the camera
for i, x in enumerate((-5.2, -2.3, 0.6)):
    cy, rot = desk(x, -2.3, +1)
    sex, pose = [("F", "Sitting"), ("M", "Sitting_Cheering"), ("F", "Sitting_Cheering")][i]
    P(sex, pose, x, cy, rot)
L.inst(LAPTOP, (-2.3 + 0.62, -2.3 - 0.05, 0.79), rot=math.pi + 0.3, scale=0.8)

# ------------------------------------------------------------------ lounge
SOFA = L.load("Sofa_01", os.path.join(L.PH, "Sofa_01", "Sofa_01_1k.gltf"))
CTABLE = L.load("modern_coffee_table_01", os.path.join(L.PH, "modern_coffee_table_01", "modern_coffee_table_01_1k.gltf"))
PLANT1 = L.load("potted_plant_01", os.path.join(L.PH, "potted_plant_01", "potted_plant_01_1k.gltf"))
PLANT2 = L.load("potted_plant_02", os.path.join(L.PH, "potted_plant_02", "potted_plant_02_1k.gltf"))
for k, c in [("sofa", SOFA), ("ctable", CTABLE)]:
    mn, mx = Vector(c["bounds"][0]), Vector(c["bounds"][1])
    print("DIM", k, tuple(round(v, 2) for v in mn), tuple(round(v, 2) for v in mx))
L.inst(SOFA, (7.0, -2.6, 0), rot=-R90)
L.inst(CTABLE, (5.3, -2.6, 0), rot=-R90)
L.cylinder("table_mug", 0.045, 0.1, (5.2, -2.4, 0.42), M["mug"], seg=12)
L.inst(PLANT1, (7.3, 5.4, 0), rot=1.0, scale=1.3)
L.inst(PLANT1, (-7.4, 5.4, 0), rot=2.0, scale=1.2)
L.inst(PLANT2, (-7.3, -5.3, 0), rot=0.3, scale=1.4)
L.inst(PLANT2, (7.4, -5.3, 0), rot=0.3, scale=1.3)

# warm ceiling lights over desks and bar (shades omitted: from this cut-away camera they cover the wall neon)
for x in (-5.2, -2.3, 0.6):
    for y in (1.7, -2.3):
        L.point_light("lamp_light", (x, y, HGT - 1.35), 45, (1.0, 0.72, 0.45), radius=0.08)
for x in (3.4, 6.2):
    L.point_light("lamp_light", (x, 4.6, HGT - 1.35), 40, (1.0, 0.72, 0.45), radius=0.08)

# ------------------------------------------------------------------ more people
P("F", "Standing", 4.9, 5.55, 0.0, shirt=(0.12, 0.13, 0.18), hair=4)  # barista
P("M", "Standing_Hips", 3.4, 3.55, math.pi + 0.2)  # ordering
P("F", "Sitting", 5.5, 3.75 + 0.35, math.pi, z=0.27)  # on a stool
P("M", "Standing_CoveringEyes", 2.6, -4.4, math.radians(40), shirt=(0.85, 0.10, 0.16))  # rekt
P("F", "LookingUp", -1.0, 4.4, math.pi + 0.1)  # watching the Pulse wall
P("M", "Walking", -6.0, -4.3, math.radians(-110))
P("F", "Sitting", 6.6, -3.2, -R90, z=0.05)
P("M", "Sitting_Cheering", 6.6, -1.9, -R90, z=0.05)
# AI Agent NPC: a trader with a terminal for a head
ai = P("M", "Standing", 2.4, 1.2, math.radians(25), shirt=(0.2, 0.22, 0.28), pants=(0.12, 0.12, 0.14))
bpy.context.view_layer.update()
hx, hy, top = L._head_of(next(o for o in L._cache["pose_M_Standing"].objects if o.type == "MESH"))
hc = ai.matrix_world @ Vector((hx, hy, top - 0.28))
L.box("ai_head", (0.42, 0.34, 0.32), tuple(hc), M["black"], rot=math.radians(25), bevel=0.03)
fx, fy = math.sin(math.radians(25)), -math.cos(math.radians(25))
L.plane("ai_face", 0.36, 0.225, (hc.x + fx * 0.175, hc.y + fy * 0.175, hc.z + 0.01), (0, 0, math.radians(25)), mat=M["ai_face"])

# outside trees by the windows (dappled light through the glass)
TREE = L.load("CommonTree_2", os.path.join(L.QN, "CommonTree_2.gltf"))
for (x, y, s) in [(-11.0, -9.5, 1.2), (-12.5, 9.5, 1.35), (-16.0, 13.0, 1.1)]:
    L.inst(TREE, (x, y, 0), rot=rnd.uniform(0, 6.28), scale=s)

# neighbouring buildings behind the café + exterior dressing
def fix_bld(objs):
    L.fix_vertex_color_mix(objs, tints={"Trim_Dark": (0.22, 0.22, 0.25), "Trim_Green": (0.25, 0.5, 0.36)})
    for o in objs:
        for m in (o.data.materials if o.type == "MESH" else []):
            if m and m.name.startswith("MI_Glass") and m.use_nodes:
                bb = m.node_tree.nodes.get("Principled BSDF")
                for lk in list(bb.inputs["Base Color"].links) + list(bb.inputs["Alpha"].links):
                    m.node_tree.links.remove(lk)
                bb.inputs["Base Color"].default_value = (0.05, 0.09, 0.11, 1)
                bb.inputs["Roughness"].default_value = 0.04
                bb.inputs["Alpha"].default_value = 1.0


BS = L.load("Building_Small_1", os.path.join(L.QDC, "Building_Small_1.gltf"), fix_bld)
BM = L.load("Building_Medium_2_001", os.path.join(L.QDC, "Building_Medium_2_001.gltf"), fix_bld)
L.inst(BS, (-2.0, 9.6, 0), scale=(1.0, 1.0, 0.85))
L.inst(BM, (13.0, 9.0, 0), scale=(1.0, 1.0, 0.6))
L.inst(BM, (-17.0, 9.0, 0), scale=(1.0, 1.0, 0.5))
L.ground("street_back", -60, 6.4, 60, 9.0, 0.005, M["outside"])
BENCH = L.load("painted_wooden_bench", os.path.join(L.PH, "painted_wooden_bench", "painted_wooden_bench_1k.gltf"))
L.inst(BENCH, (-10.0, -1.0, 0), rot=-R90, scale=1.3)
BUSH = L.load("Bush_Common_Flowers", os.path.join(L.QN, "Bush_Common_Flowers.gltf"))
for (x, y) in [(-9.2, -5.5), (-9.0, 5.0), (-9.4, 0.9), (9.2, -5.0), (9.0, 4.5)]:
    L.inst(BUSH, (x, y, 0), rot=rnd.uniform(0, 6.28), scale=0.8)

# ------------------------------------------------------------------ lighting
L.world_hdri(os.path.join(L.HDRI, "kiara_7_late-afternoon_4k.hdr"), strength=0.6, rot_z=math.radians(110), clamp=3.0, tint=(1.0, 0.88, 0.76))
L.sun(energy=13.0, color=(1.0, 0.66, 0.40), elev=19, azim=-97, angle=1.2)
L.area_light("ceiling_fill", (0, 0, HGT - 0.15), (0, 0, 0), 14.0, 330, (1.0, 0.82, 0.66), size_y=10.0)
# interior haze for light shafts
vol = L.box("haze", (W1 - W0, D1 - D0, HGT), (0, 0, HGT / 2))
vm = bpy.data.materials.new("haze")
vm.use_nodes = True
nt = vm.node_tree
nt.nodes.remove(nt.nodes["Principled BSDF"])
pv = nt.nodes.new("ShaderNodeVolumePrincipled")
pv.inputs["Density"].default_value = 0.028
pv.inputs["Anisotropy"].default_value = 0.45
pv.inputs["Color"].default_value = (1.0, 0.92, 0.85, 1)
nt.links.new(pv.outputs[0], nt.nodes["Material Output"].inputs["Volume"])
vol.data.materials.append(vm)

# ------------------------------------------------------------------ camera / render
cam = L.camera((9.6, -12.0, 10.4), (-0.4, 0.6, 0.9), lens=32, shift_y=0.0)
w, h, samples = (2400, 1350, 96)
if PREVIEW:
    w, h, samples = 1200, 675, 32
L.render_settings(w, h, samples=samples, exposure=0.8, look="AgX - Punchy")
sc = bpy.context.scene
sc.eevee.use_volumetric_shadows = True
sc.eevee.volumetric_tile_size = "8"
sc.eevee.volumetric_samples = 64
sc.eevee.volumetric_end = 60
L.render(os.path.join(OUTDIR, "terminal_cafe_raw.png"))
print("RENDERED cafe")

if "--no-save" not in ARGS:
    L.save_blend(os.path.join(L.HERE, "terminal_cafe.blend"))
    print("SAVED blend")
