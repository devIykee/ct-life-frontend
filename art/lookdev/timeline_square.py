"""CT Life lookdev: Timeline Square (downtown plaza of CT City), Sims-4-style target look.

Run (from repo root):
  /Applications/Blender.app/Contents/MacOS/Blender -b -P art/lookdev/timeline_square.py -- [--preview] [--shot hero|phone|both] [--no-save]
Outputs: art/lookdev/renders/timeline_square_{hero,phone}_raw.png (+ json with screen positions),
         art/lookdev/timeline_square.blend
Then run post.py to grade + composite the HUD.
"""
import json
import math
import os
import random
import sys

import bpy
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lookdev_common as L  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = "--preview" in ARGS
SHOT = ARGS[ARGS.index("--shot") + 1] if "--shot" in ARGS else "both"
OUTDIR = os.path.join(L.HERE, "renders")
os.makedirs(OUTDIR, exist_ok=True)
T = lambda f: os.path.join(L.TEX, f)  # noqa: E731

L.reset()
rnd = random.Random(42)
Z_ROAD = -0.15

# =========================================================================== materials
M = {}
M["pave"] = L.mat_pbr("plaza_pave", "herringbone_pavement", scale=0.45, tint=(1.9, 1.72, 1.52))
M["border"] = L.mat_pbr("plaza_border", "brick_pavement_02", scale=0.5, tint=(0.95, 0.8, 0.72))
M["side"] = L.mat_pbr("sidewalk", "concrete_pavement", scale=0.35, tint=(1.0, 0.97, 0.93))
M["asphalt"] = L.mat_pbr("asphalt", "clean_asphalt", scale=0.25, tint=(0.62, 0.62, 0.66))
M["grass"] = L.mat_pbr("lawn", "leafy_grass", scale=0.5, tint=(0.58, 1.0, 0.42), bump=0.6)
M["curb"] = L.mat_color("curb_stone", (0.78, 0.74, 0.68), rough=0.7)
M["paint"] = L.mat_color("road_paint", (0.95, 0.95, 0.92), rough=0.6)
M["yellow"] = L.mat_color("road_yellow", (0.95, 0.75, 0.15), rough=0.6)
M["metal_dark"] = L.mat_color("metal_dark", (0.06, 0.065, 0.075), rough=0.35, metal=0.8)
M["metal_white"] = L.mat_color("metal_white", (0.9, 0.9, 0.92), rough=0.3, metal=0.0, coat=0.5)
M["frame"] = L.mat_color("bb_frame", (0.12, 0.12, 0.14), rough=0.4, metal=0.6)
M["stucco_cream"] = L.mat_pbr("stucco_cream", "plastered_wall_04", scale=0.4, tint=(1.0, 0.92, 0.78))
M["stucco_peach"] = L.mat_pbr("stucco_peach", "plastered_wall_04", scale=0.4, tint=(1.0, 0.75, 0.62))
M["stucco_mint"] = L.mat_pbr("stucco_mint", "plastered_wall_04", scale=0.4, tint=(0.75, 0.92, 0.82))
M["red_glow"] = L.mat_color("siren_red", (1, 0.1, 0.1), rough=0.3, emit=(1, 0.1, 0.08), emit_strength=8)
M["gold"] = L.mat_color("gold", (1.0, 0.75, 0.3), rough=0.25, metal=1.0)
M["bulb"] = L.mat_color("lamp_bulb", (1, 0.9, 0.7), emit=(1.0, 0.78, 0.45), emit_strength=12)
M["water"] = L.mat_color("water", (0.1, 0.35, 0.45), rough=0.05, spec=0.8)

# =========================================================================== ground
L.ground("base_ground", -900, -400, 900, 900, -0.2, L.mat_color("far_ground", (0.55, 0.48, 0.40), rough=0.9))
L.ground("road", -400, -24, 400, -15, Z_ROAD, M["asphalt"])
L.ground("plaza", -24, -15, 24, 19, 0.0, M["pave"], thick=0.2)
L.ground("sidewalk_far", -400, -31, 400, -24, 0.0, M["side"], thick=0.2)
L.ground("sidewalk_left", -400, -15, -24, 19, 0.0, M["side"], thick=0.2)
L.ground("sidewalk_right", 24, -15, 400, 19, 0.0, M["side"], thick=0.2)
L.ground("street_back", -400, 19, 400, 27, Z_ROAD, M["asphalt"])
L.ground("sidewalk_back", -400, 27, 400, 31, 0.0, M["side"], thick=0.2)
# border band around plaza
for (x0, y0, x1, y1) in [(-24, -15, 24, -13.8), (-24, 17.8, 24, 19), (-24, -15, -22.8, 19), (22.8, -15, 24, 19)]:
    L.ground("plaza_border", x0, y0, x1, y1, 0.005, M["border"])
# curbs
for y in (-15, -24, 19, 27):
    L.box("curb", (800, 0.25, 0.3), (0, y, -0.05), M["curb"])
# lane markings (dashed centre line) + crosswalk
for x in range(-120, 121, 6):
    L.box("lane_dash", (3.0, 0.18, 0.02), (x, -19.5, Z_ROAD + 0.01), M["paint"])
    L.box("lane_dash", (3.0, 0.18, 0.02), (x, 23, Z_ROAD + 0.01), M["paint"])
for i in range(9):
    L.box("zebra", (0.7, 8.2, 0.02), (-2.0 + i * 1.1 - 4.4, -19.5, Z_ROAD + 0.012), M["paint"])

# lawns (raised planters) with stone curbs
LAWNS = [(-21.5, -9.5, -12.5, 4.5), (12.5, -9.5, 21.5, 4.5), (-21.5, 9.0, -14.0, 16.5), (14.0, 9.0, 21.5, 16.5)]
for (x0, y0, x1, y1) in LAWNS:
    L.ground("lawn", x0, y0, x1, y1, 0.32, M["grass"], thick=0.32)
    for (a, b, c, d) in [(x0 - .25, y0 - .25, x1 + .25, y0), (x0 - .25, y1, x1 + .25, y1 + .25), (x0 - .25, y0, x0, y1), (x1, y0, x1 + .25, y1)]:
        L.box("planter_curb", (c - a, d - b, 0.45), ((a + c) / 2, (b + d) / 2, 0.225), M["curb"], bevel=0.04)

# central round fountain-ish inlay (water feature)
L.cylinder("fountain_rim", 3.3, 0.45, (0, -4.5, 0), M["curb"], seg=48)
L.cylinder("fountain_water", 3.0, 0.47, (0, -4.5, 0.0), M["water"], seg=48)
L.cylinder("fountain_col", 0.5, 1.4, (0, -4.5, 0), M["curb"], seg=24)
L.cylinder("fountain_bowl", 0.6, 0.3, (0, -4.5, 1.25), M["curb"], seg=32, r_top=1.5)
L.cylinder("fountain_bowl_water", 1.38, 0.02, (0, -4.5, 1.53), M["water"], seg=32)
L.cylinder("fountain_col2", 0.22, 0.8, (0, -4.5, 1.5), M["curb"], seg=16)
L.cylinder("fountain_bowl2", 0.25, 0.18, (0, -4.5, 2.25), M["curb"], seg=24, r_top=0.7)
JET = L.mat_color("water_jet", (0.8, 0.92, 1.0), rough=0.1, emit=(0.75, 0.9, 1.0), emit_strength=1.2, alpha=0.6)
L.cylinder("fountain_jet", 0.07, 1.1, (0, -4.5, 2.4), JET, seg=12, r_top=0.03)


# =========================================================================== buildings
def fix_bld(objs):
    L.fix_vertex_color_mix(objs, tints={"Trim_Dark": (0.22, 0.22, 0.25), "Trim_Green": (0.25, 0.5, 0.36)})
    for o in objs:
        if o.type != "MESH":
            continue
        for m in o.data.materials:
            if m and m.name.startswith("MI_Glass") and m.use_nodes:
                b = m.node_tree.nodes.get("Principled BSDF")
                for lk in list(b.inputs["Base Color"].links) + list(b.inputs["Alpha"].links):
                    m.node_tree.links.remove(lk)
                b.inputs["Base Color"].default_value = (0.05, 0.09, 0.11, 1)
                b.inputs["Roughness"].default_value = 0.04
                b.inputs["Alpha"].default_value = 1.0
                b.inputs["Metallic"].default_value = 0.2
            if m and m.name.startswith("MI_FakeInterior") and m.use_nodes:
                b = m.node_tree.nodes.get("Principled BSDF")
                tex = next((x for x in m.node_tree.nodes if x.type == "TEX_IMAGE"), None)
                if tex and "dark" not in tex.image.name:
                    m.node_tree.links.new(tex.outputs["Color"], b.inputs["Emission Color"])
                    b.inputs["Emission Strength"].default_value = 0.6


BLD = {k: L.load(k, os.path.join(L.QDC, f"{k}.gltf"), fix_bld) for k in ("Building_Large_2", "Building_Medium_2_001", "Building_Small_1")}
R90 = math.pi / 2
# back row (faces -Y, toward plaza) behind the back street
L.inst(BLD["Building_Small_1"], (-21, 33, 0), scale=(1, 1, 0.75))
L.inst(BLD["Building_Medium_2_001"], (1, 32, 0), scale=(1, 1, 0.55))
L.inst(BLD["Building_Small_1"], (19.5, 33, 0), scale=(1, 1, 0.8))
L.inst(BLD["Building_Medium_2_001"], (37, 32, 0), scale=(1, 1, 0.6))
L.inst(BLD["Building_Small_1"], (-38, 33, 0), scale=(1, 1, 0.7))
# right row (faces -X)
L.inst(BLD["Building_Small_1"], (29, 8, 0), rot=-R90)
L.inst(BLD["Building_Large_2"], (29, -8.5, 0), rot=-R90, scale=(1, 1, 0.82))
# left row (faces +X)
L.inst(BLD["Building_Medium_2_001"], (-29, 2, 0), rot=R90)
L.inst(BLD["Building_Small_1"], (-29, 18, 0), rot=R90)
L.inst(BLD["Building_Large_2"], (-29, -18, 0), rot=R90)
# mid-distance city fill
for i, (x, y, k, r) in enumerate([(-60, 60, "Building_Large_2", 0), (-25, 62, "Building_Medium_2_001", 0), (8, 60, "Building_Large_2", 0),
                                   (40, 64, "Building_Small_1", 0), (70, 58, "Building_Medium_2_001", 0), (-90, 40, "Building_Small_1", R90),
                                   (95, 40, "Building_Large_2", -R90), (-75, 90, "Building_Medium_2_001", 0), (30, 95, "Building_Large_2", 0),
                                   (-10, 100, "Building_Small_1", 0), (65, 92, "Building_Medium_2_001", 0)]):
    L.inst(BLD[k], (x, y, 0), rot=r, scale=(1, 1, 0.45 + 0.1 * (i % 3)))

keys = list(BLD)
for gx in range(-130, 171, 26):
    for gy in range(48, 141, 24):
        if rnd.random() < 0.25:
            continue
        L.inst(BLD[keys[rnd.randint(0, 2)]], (gx + rnd.uniform(-3, 3), gy + rnd.uniform(-3, 3), 0), rot=rnd.choice([0, R90, -R90, math.pi]),
               scale=(1, 1, rnd.uniform(0.3, 0.55)))

# =========================================================================== Dubai glass towers (procedural)
GLASS = [L.mat_glass_tower("tower_glass_teal", base=(0.04, 0.20, 0.26)),
         L.mat_glass_tower("tower_glass_blue", base=(0.05, 0.11, 0.28), frame=(0.75, 0.76, 0.8)),
         L.mat_glass_tower("tower_glass_gold", base=(0.32, 0.20, 0.08), frame=(0.85, 0.78, 0.62)),
         L.mat_glass_tower("tower_glass_silver", base=(0.16, 0.20, 0.25), frame=(0.85, 0.86, 0.9))]


def tower(x, y, w, d, h, mat, twist=0.0, taper=1.0, spire=0.0, steps=0):
    if steps:
        z = 0
        for s in range(steps):
            f = 1 - s * 0.18
            hh = h * (0.45 if s == 0 else 0.55 / (steps - 1))
            L.box("tower", (w * f, d * f, hh), (x, y, z + hh / 2), mat)
            z += hh
    else:
        o = L.box("tower", (w, d, h), (x, y, h / 2), mat)
        if twist or taper != 1.0:
            # subdivide along Z so the deform is smooth
            me = o.data
            import bmesh
            bm = bmesh.new(); bm.from_mesh(me)
            bmesh.ops.subdivide_edges(bm, edges=[e for e in bm.edges if abs(e.verts[0].co.z - e.verts[1].co.z) > 1], cuts=24)
            bm.to_mesh(me); bm.free()
            for v in me.vertices:
                t = (v.co.z + h / 2) / h
                a = twist * t
                s = 1 + (taper - 1) * t
                x0, y0 = v.co.x * s, v.co.y * s
                v.co.x, v.co.y = x0 * math.cos(a) - y0 * math.sin(a), x0 * math.sin(a) + y0 * math.cos(a)
        z = h
    if spire:
        L.cylinder("spire", w * 0.06, spire, (x, y, z), M["metal_white"], seg=12, r_top=0.05)


TOWERS = [(-120, 170, 30, 30, 230, 0, 0, 1.0, 40, 3), (-60, 200, 28, 28, 400, 1, 0, 1.0, 110, 4), (5, 185, 36, 30, 280, 2, 1.4, 0.8, 0, 0),
          (65, 195, 30, 30, 330, 3, 0, 0.7, 40, 0), (130, 175, 32, 28, 240, 0, 0, 1.0, 0, 3), (-25, 150, 24, 24, 200, 3, 0.9, 1.0, 0, 0),
          (35, 145, 26, 22, 180, 1, 0, 1.0, 25, 0), (95, 150, 24, 24, 210, 2, 0, 0.85, 0, 0), (-90, 145, 28, 28, 190, 1, 0, 1.0, 0, 2),
          (190, 210, 32, 32, 290, 3, 1.0, 0.9, 0, 0), (-180, 200, 30, 30, 230, 2, 0, 1.0, 30, 0), (-150, 140, 22, 22, 150, 0, 0.6, 1.0, 0, 0)]
for (x, y, w, d, h, mi, tw, tp, sp, st) in TOWERS:
    tower(x, y, w, d, h, GLASS[mi], twist=tw, taper=tp, spire=sp, steps=st)

# =========================================================================== Timeline screen
SCREEN_Y = 15.5
L.box("screen_frame", (19.2, 0.9, 11.2), (0, SCREEN_Y + 0.45, 3.4 + 5.6), M["frame"], bevel=0.12)
L.plane("timeline_screen", 18.4, 10.35, (0, SCREEN_Y - 0.02, 3.4 + 5.6), mat=L.mat_screen("timeline_screen", T("timeline_screen.png"), strength=4.0))
for x in (-6.5, 6.5):
    L.box("screen_leg", (0.8, 0.8, 4.0), (x, SCREEN_Y + 0.6, 2.0), M["frame"], bevel=0.05)
L.box("screen_base", (16, 1.6, 0.5), (0, SCREEN_Y + 0.6, 0.25), M["curb"], bevel=0.05)
L.box("screen_cap", (19.6, 1.3, 0.4), (0, SCREEN_Y + 0.45, 14.7), M["frame"], bevel=0.05)
# "CT CITY" letters on the cap
L.plane("screen_cap_sign", 6.0, 0.9, (0, SCREEN_Y - 0.25, 15.4), mat=L.mat_screen("cap_sign", T("neon_wagmi.png"), strength=6.0, base=0.0))

# =========================================================================== billboards
def billboard(name, tex, w, h, loc, rot_z, emit=1.2, legs=0.0, frame=True, lamp=True):
    rot = (0, 0, rot_z)
    L.plane(name, w, h, loc, rot, mat=L.mat_print(name, T(tex), rough=0.55, emit=emit))
    fx, fy = math.sin(rot_z), -math.cos(rot_z)  # facing vector (local -Y)
    back = Vector(loc) - Vector((fx, fy, 0)) * 0.18
    if frame:
        L.box(name + "_frame", (w + 0.4, 0.3, h + 0.4), tuple(back), M["frame"], rot=rot_z, bevel=0.04)
    if legs:
        for s in (-0.3, 0.3):
            off = Vector((math.cos(rot_z), math.sin(rot_z), 0)) * (s * w)
            L.box(name + "_leg", (0.35, 0.35, legs), (back.x + off.x, back.y + off.y, loc[2] - h / 2 - legs / 2), M["frame"], rot=rot_z)
    if lamp:
        for s in (-0.33, 0.0, 0.33):
            off = Vector((math.cos(rot_z), math.sin(rot_z), 0)) * (s * w)
            p = Vector(loc) + off + Vector((fx, fy, 0)) * 0.6 + Vector((0, 0, h / 2 + 0.25))
            L.box(name + "_lamp", (0.5, 0.25, 0.12), tuple(p), M["frame"], rot=rot_z)


# rooftop $FROGE on the left Medium building (roof ~25 m), angled toward camera
billboard("bb_froge", "bb_froge.png", 12, 6, (-30.0, 25.0, 19.0), 0.2, emit=1.4, legs=16.0)
# RUG WEEK on the right Large building facade (faces -X)
billboard("bb_rugweek", "bb_rugweek.png", 8, 4.0, (28.4, -3.6, 10.5), -R90, emit=0.9, frame=True, lamp=False)
# WAGMI REALTY rooftop on back-right Small building (17 m)
billboard("bb_wagmi", "bb_wagmi_realty.png", 12, 6, (19.5, 37.0, 18.5), 0.0, emit=1.0, legs=1.5)
# gm square on the back-left Large building side wall
billboard("bb_gm", "bb_gm.png", 6.0, 6.0, (-21.0, 37.0, 16.5), 0.0, emit=1.2, legs=1.0)
# ORE REFINERY -> road sign at the front-right on posts
billboard("bb_ore", "bb_ore_refinery.png", 7.5, 2.5, (19.5, -12.6, 4.8), 0.22, emit=1.2, legs=3.5)
# Pixel Wall (ad tiles) on the right Small building facade
L.plane("pixel_wall", 6, 6, (28.35, 8.5, 9.5), (0, 0, -R90), mat=L.mat_screen("pixel_wall", T("pixel_wall.png"), strength=1.6, base=0.3))

# storefront signs / awnings at ground level
def storefront(sign_tex, loc, rot_z, w=6.0, awning="awning_red.png", neon=None):
    L.plane("sign", w, w / 6, loc, (0, 0, rot_z), mat=L.mat_print(sign_tex, T(sign_tex), emit=0.8))
    fx, fy = math.sin(rot_z), -math.cos(rot_z)
    a = L.box("awning", (w + 0.6, 1.6, 0.08), (loc[0] + fx * 0.9, loc[1] + fy * 0.9, loc[2] - 0.9), L.mat_print(awning, T(awning), rough=0.8), rot=(math.radians(18) * (1 if fy < 0 else 1), 0, rot_z))
    if neon:
        L.plane("neon", w * 0.7, w * 0.7 / 4, (loc[0] + fx * 0.05, loc[1] + fy * 0.05, loc[2] - 2.2), (0, 0, rot_z), mat=L.mat_screen(neon, T(neon), strength=8.0, base=0.0))


storefront("sign_terminal_cafe.png", (28.3, 6.0, 4.6), -R90, w=7.0, awning="awning_blue.png")
storefront("sign_gm_coffee.png", (-28.4, 2.0, 4.6), R90, w=6.0, awning="awning_green.png")
storefront("sign_rekt_clinic.png", (-21.0, 31.6, 4.4), 0.0, w=6.0, awning="awning_red.png")
storefront("sign_cope_bakery.png", (1.0, 31.5, 4.4), 0.0, w=7.0, awning="awning_red.png")

# =========================================================================== nature
def fix_tree_pink(objs):
    for o in objs:
        if o.type != "MESH":
            continue
        for m in o.data.materials:
            if m and "Leaves" in m.name and m.use_nodes and not m.get("pinked"):
                nt = m.node_tree
                b = nt.nodes["Principled BSDF"]
                tex = next(x for x in nt.nodes if x.type == "TEX_IMAGE")
                bw = nt.nodes.new("ShaderNodeRGBToBW")
                nt.links.new(tex.outputs["Color"], bw.inputs["Color"])
                ramp = nt.nodes.new("ShaderNodeValToRGB")
                ramp.color_ramp.elements[0].position = 0.05
                ramp.color_ramp.elements[0].color = (0.93, 0.42, 0.62, 1)
                ramp.color_ramp.elements[1].position = 0.6
                ramp.color_ramp.elements[1].color = (1.0, 0.80, 0.89, 1)
                nt.links.new(bw.outputs["Val"], ramp.inputs["Fac"])
                nt.links.new(ramp.outputs["Color"], b.inputs["Base Color"])
                b.inputs["Subsurface Weight"].default_value = 0.0
                m["pinked"] = 1


def fix_tree_green(objs):
    for o in objs:
        if o.type != "MESH":
            continue
        for m in o.data.materials:
            if m and "Leaves" in m.name and m.use_nodes and not m.get("greened"):
                nt = m.node_tree
                b = nt.nodes["Principled BSDF"]
                src = b.inputs["Base Color"].links[0].from_socket
                hs = nt.nodes.new("ShaderNodeHueSaturation")
                hs.inputs["Saturation"].default_value = 1.15
                hs.inputs["Value"].default_value = 1.05
                nt.links.new(src, hs.inputs["Color"])
                nt.links.new(hs.outputs["Color"], b.inputs["Base Color"])
                m["greened"] = 1


PINK = [L.load(f"TwistedTree_{i}_pink", os.path.join(L.QN, f"TwistedTree_{i}.gltf"), fix_tree_pink) for i in (1, 3, 4)]
GREEN = [L.load(f"CommonTree_{i}", os.path.join(L.QN, f"CommonTree_{i}.gltf"), fix_tree_green) for i in (1, 2, 4, 5)]
BUSH = L.load("Bush_Common_Flowers", os.path.join(L.QN, "Bush_Common_Flowers.gltf"))
BUSH2 = L.load("Bush_Common", os.path.join(L.QN, "Bush_Common.gltf"), fix_tree_green)
FLOWERS = [L.load(f"Flower_{i}_Group", os.path.join(L.QN, f"Flower_{i}_Group.gltf")) for i in (3, 4)]

# blossom trees in lawns
for (x, y, k, r, s) in [(-18.5, -5.5, 0, 0.4, 0.5), (-15.0, 1.5, 1, 2.2, 0.45), (17.5, -6.0, 2, 3.6, 0.5), (15.0, 1.5, 0, 5.0, 0.42),
                        (-18, 13, 1, 1.0, 0.42), (18, 13, 2, 4.0, 0.42)]:
    L.inst(PINK[k], (x, y, 0.32), rot=r, scale=s)
# small blossom trees in round planters at the plaza front
for (x, y, k, r) in [(-4.4, -13.0, 2, 0.8)]:
    L.cylinder("tree_planter", 1.5, 0.5, (x, y, 0), M["curb"], seg=32)
    L.cylinder("tree_planter_soil", 1.35, 0.52, (x, y, 0), M["grass"], seg=32)
    L.inst(PINK[k], (x, y, 0.5), rot=r, scale=0.3)
    for i in range(4):
        a_ = i * 1.57 + 0.4
        L.inst(BUSH, (x + math.cos(a_) * 0.9, y + math.sin(a_) * 0.9, 0.5), rot=a_, scale=0.55)
# green street trees on the sidewalks
for i, x in enumerate([-34, -12, 10, 34, -56, 56]):
    L.inst(GREEN[i % 4], (x, -12.8 if abs(x) > 30 else -13.4, 0), rot=rnd.uniform(0, 6.28), scale=1.15 if abs(x) < 30 else 1.3)
for i, x in enumerate([-38, -16, 16, 38]):
    L.inst(GREEN[(i + 1) % 4], (x, 29.0, 0), rot=rnd.uniform(0, 6.28), scale=1.1)
for (x, y) in [(-26, -8), (-26, 10), (26, -12), (26, 16)]:
    L.inst(GREEN[rnd.randint(0, 3)], (x, y, 0), rot=rnd.uniform(0, 6.28), scale=1.1)
# lush big tree accent
# bushes and flowers along lawn edges
for (x0, y0, x1, y1) in LAWNS:
    for i in range(10):
        bx = rnd.uniform(x0 + 0.8, x1 - 0.8)
        by = rnd.choice([y0 + 0.7, y1 - 0.7]) if i % 2 else rnd.uniform(y0 + 0.8, y1 - 0.8)
        L.inst(BUSH if i % 3 else BUSH2, (bx, by, 0.32), rot=rnd.uniform(0, 6.28), scale=rnd.uniform(0.7, 1.0))
    for i in range(8):
        L.inst(FLOWERS[i % 2], (rnd.uniform(x0 + 0.5, x1 - 0.5), rnd.uniform(y0 + 0.5, y1 - 0.5), 0.32), rot=rnd.uniform(0, 6.28), scale=rnd.uniform(0.5, 0.8))

# =========================================================================== street furniture
LAMP = L.load("street_lamp_01", os.path.join(L.PH, "street_lamp_01", "street_lamp_01_1k.gltf"))
BENCH = L.load("painted_wooden_bench", os.path.join(L.PH, "painted_wooden_bench", "painted_wooden_bench_1k.gltf"))
TRASH = L.load("metal_trash_can", os.path.join(L.PH, "metal_trash_can", "metal_trash_can_1k.gltf"))
HYDRANT = L.load("fire_hydrant", os.path.join(L.PH, "fire_hydrant", "fire_hydrant_1k.gltf"))
PLANTER = L.load("planter_box_01", os.path.join(L.PH, "planter_box_01", "planter_box_01_1k.gltf"))
CART = L.load("CoffeeCart_01", os.path.join(L.PH, "CoffeeCart_01", "CoffeeCart_01_1k.gltf"))
TABLESET = L.load("outdoor_table_chair_set_01", os.path.join(L.PH, "outdoor_table_chair_set_01", "outdoor_table_chair_set_01_1k.gltf"))
MEGA = L.load("Megaphone_01", os.path.join(L.PH, "Megaphone_01", "Megaphone_01_1k.gltf"))

lamp_pts = [(-22, -14), (-8, -14), (8, -14), (22, -14), (-22, 17.5), (22, 17.5), (-9.5, 6), (9.5, 6), (-40, -14), (40, -14)]
for (x, y) in lamp_pts:
    L.inst(LAMP, (x, y, 0), rot=0, scale=1.25)
    L.sphere("lamp_glow", 0.17, (x, y, 4.55), M["bulb"])
BENCHES = [(-12.0, -2.5, R90), (-12.0, 1.5, R90), (12.0, -2.5, -R90), (12.0, 1.5, -R90), (-4.5, 9.5, math.pi), (4.5, 9.5, math.pi),
           (-3.8, -9.5, 0.0), (3.8, -9.5, 0.0)]
for (x, y, r) in BENCHES:
    L.inst(BENCH, (x, y, 0), rot=r, scale=1.35)  # pew-style bench; seat faces -Y at rot 0
for (x, y) in [(-11.8, -6.5), (11.8, -6.5), (-23, 17.0), (23, -13.5)]:
    L.inst(TRASH, (x, y, 0), rot=rnd.uniform(0, 6.28), scale=1.1)
L.inst(HYDRANT, (-14.0, -14.3, 0), rot=0.4, scale=1.1)
L.inst(CART, (-7.5, 12.8, 0), rot=math.radians(150), scale=1.3)
for (x, y) in [(-26.5, -2.5), (-26.5, 6.5)]:
    L.inst(TABLESET, (x, y, 0), rot=rnd.uniform(0, 6.28), scale=1.1)
for (x, y) in [(-7.0, 17.5), (7.0, 17.5)]:
    L.inst(PLANTER, (x, y, 0), rot=0, scale=1.6)

# =========================================================================== trading kiosk
KX, KY, KR = 8.0, -8.0, math.radians(-20)
kiosk = bpy.data.objects.new("kiosk", None); L.link(kiosk); kiosk.location = (KX, KY, 0); kiosk.rotation_euler = (0, 0, KR)
def kid(o):
    o.parent = kiosk
    return o
kid(L.box("kiosk_body", (3.0, 1.6, 1.15), (0, 0, 0.575), M["metal_white"], bevel=0.08))
kid(L.box("kiosk_trim", (3.05, 1.65, 0.12), (0, 0, 1.15), L.mat_color("kiosk_green", (0.1, 0.75, 0.42), rough=0.3), bevel=0.03))
kid(L.box("kiosk_screen_back", (2.7, 0.16, 1.55), (0, 0.25, 2.05), M["frame"], rot=(math.radians(-12), 0, 0), bevel=0.03))
kid(L.plane("kiosk_screen", 2.5, 1.5625 * 0.9, (0, 0.15, 2.07), (math.radians(-12), 0, 0), mat=L.mat_screen("kiosk_chart", T("kiosk_chart.png"), strength=3.5)))
for x in (-1.35, 1.35):
    kid(L.box("kiosk_pole", (0.12, 0.12, 2.0), (x, 0.65, 2.15), M["metal_white"]))
kid(L.box("kiosk_canopy", (3.6, 2.2, 0.16), (0, 0.1, 3.2), L.mat_print("awning_green_k", T("awning_green.png"), rough=0.8), bevel=0.04))
kid(L.plane("kiosk_sign", 3.0, 0.5, (0, -1.02, 3.2), mat=L.mat_screen("kiosk_sign", T("neon_nfa.png"), strength=5.0, base=0.0)))

# =========================================================================== podium + Town Crier
PX, PY = -6.5, 1.0
L.cylinder("podium_low", 2.2, 0.3, (PX, PY, 0), M["curb"], seg=48)
L.cylinder("podium_top", 1.6, 0.35, (PX, PY, 0.3), L.mat_color("podium_red", (0.75, 0.12, 0.16), rough=0.45), seg=48)
L.cylinder("podium_trim", 1.62, 0.06, (PX, PY, 0.62), M["gold"], seg=48)
lect = L.box("lectern", (1.0, 0.6, 1.1), (PX + 0.2, PY - 0.9, 0.65 + 0.55), M["frame"], bevel=0.04, rot=math.radians(-15))
L.plane("lectern_banner", 0.95, 0.6, (PX + 0.2 + 0.08, PY - 0.9 - 0.31, 1.25), (0, 0, math.radians(-15)), mat=L.mat_print("podium_justin", T("podium_justin.png"), emit=0.5))
crier = L.person("M", "Standing_Waving", (PX, PY + 0.1, 0.65), math.radians(-15), rnd, shirt=(0.98, 0.78, 0.2), pants=(0.12, 0.12, 0.14), name="TownCrier")
bpy.context.view_layer.update()  # matrix_world is stale until the depsgraph updates
# siren hat (red emissive dome) on his head + gold sash
hx, hy, top = L._head_of(next(o for o in L._cache["pose_M_Standing_Waving"].objects if o.type == "MESH"))
# raised hand is the highest skin; head top for this pose measured ~3.7 units
crier_head = crier.matrix_world @ Vector((0.0, 0.0, 3.62))
L.sphere("siren_hat", 0.16, (crier_head.x, crier_head.y, crier_head.z), M["red_glow"], hemi=True)
L.cylinder("siren_base", 0.19, 0.05, (crier_head.x, crier_head.y, crier_head.z - 0.02), M["metal_white"], seg=24)
siren_light = bpy.data.lights.new("siren_light", "POINT"); siren_light.energy = 60; siren_light.color = (1, 0.15, 0.1); siren_light.shadow_soft_size = 0.2
so = bpy.data.objects.new("siren_light", siren_light); L.link(so); so.location = (crier_head.x, crier_head.y, crier_head.z + 0.3)
hand = crier.matrix_world @ Vector((-0.39, 0.23, 4.3))
L.inst(MEGA, (hand.x, hand.y - 0.05, hand.z - 0.15), rot=(math.radians(80), 0, math.radians(-15)), scale=1.6)

# =========================================================================== crowd
def face_to(px, py, tx, ty):
    return math.atan2(tx - px, -(ty - py))


people = []
def P(sex, pose, x, y, tx=None, ty=None, rot=None, z=0.0, **kw):
    r = rot if rot is not None else face_to(x, y, tx, ty)
    hair = kw.pop("hair", None)
    if pose == "Standing_CoveringEyes" and sex == "F":
        hair = 0
    o = L.person(sex, pose, (x, y, z), r + rnd.uniform(-0.15, 0.15), rnd, hair=hair, name=f"P_{len(people)}", **kw)
    people.append(o)
    return o


SX, SY = 0, SCREEN_Y  # timeline screen
# watching the Timeline
for (sex, pose, x, y) in [("F", "LookingUp", -2.5, 9.8), ("M", "LookingUp", 1.0, 10.8), ("M", "Standing_Hips", 3.4, 11.3), ("F", "Standing", -0.8, 12.2),
                          ("M", "Standing", 5.8, 12.0), ("F", "LookingUp", -4.6, 12.5)]:
    P(sex, pose, x, y, SX + rnd.uniform(-3, 3), SY)
# listening to the Town Crier
for (sex, pose, x, y) in [("M", "Standing", -6.8, -2.6), ("F", "Standing_Hips", -4.3, -1.8), ("M", "LookingUp", -9.4, -1.0), ("F", "Standing", -3.4, 0.6),
                          ("M", "Standing_CoveringEyes", -9.5, 3.4)]:
    P(sex, pose, x, y, PX, PY)
# at the trading kiosk
P("M", "Standing", 7.6, -10.6, KX, KY)
P("F", "Standing_Hips", 10.0, -10.0, KX, KY)
P("M", "Standing_CoveringEyes", 5.4, -10.6, KX, KY - 1)  # rekt
# on benches (seated, facing away from bench back)
for (bx, by, br), (sex, pose) in zip([BENCHES[0], BENCHES[3], BENCHES[4], BENCHES[7]], [("F", "Sitting"), ("M", "Sitting_Cheering"), ("F", "Sitting"), ("M", "Sitting")]):
    P(sex, pose, bx + math.sin(br) * 0.05, by - math.cos(br) * 0.05, rot=br, z=0.12)
# walkers / joggers / gm waver
P("F", "Walking", -16.0, -12.0, rot=math.radians(-90))
P("M", "Walking", 15.5, -12.4, rot=math.radians(90))
P("M", "Running", -26.0, 3.0, rot=math.radians(180))
P("F", "Walking", 4.0, 3.0, rot=math.radians(-140))
P("M", "Walking", -1.5, -12.0, rot=math.radians(20))
P("F", "Walking", 24.5, 4.0, rot=math.radians(0))
# foreground "player" group (phone view)
P("F", "Standing_Hips", 1.2, -11.2, rot=math.radians(-160), shirt=(0.98, 0.42, 0.08), pants=(0.85, 0.82, 0.76), hair=3, hair_col=(0.55, 0.15, 0.08))
P("M", "Standing", -0.4, -12.3, 1.2, -11.2, shirt=(0.45, 0.20, 0.80))
P("F", "Standing", 2.6, -12.6, 1.2, -11.2, shirt=(0.02, 0.60, 0.62), hair=1)
P("M", "Walking", 6.2, -13.0, rot=math.radians(70))
GM = P("M", "Standing_Waving", 2.8, -1.8, rot=math.radians(-10), shirt=(0.05, 0.40, 0.90))
P("F", "Standing", 1.7, -0.6, 2.8, -1.8)
P("F", "Running", 20.0, -27.0, rot=math.radians(90))
# chatting pairs/groups around the fountain and lawns
for (sex, pose, x, y, tx, ty) in [("F", "Standing_Hips", -2.2, -8.8, -0.8, -9.6), ("M", "Standing", -0.8, -9.6, -2.2, -8.8),
                                  ("F", "Standing", 5.2, 1.0, 6.4, 2.0), ("M", "Standing_Hips", 6.4, 2.0, 5.2, 1.0),
                                  ("M", "LookingUp", 8.8, 9.6, 0, 15.5), ("F", "Standing", -8.6, 9.0, 0, 15.5),
                                  ("M", "Walking", -14.5, -11.5, 0, -11.5), ("F", "Standing_Hips", 11.0, -12.4, 9.5, -12.0)]:
    P(sex, pose, x, y, tx, ty)
P("M", "Walking", -30.0, -27.5, rot=math.radians(-90))

# =========================================================================== cars
def fix_car(objs):
    for o in objs:
        if o.type == "MESH":
            for p in o.data.polygons:
                p.use_smooth = False
            for m in o.data.materials:
                if m and m.use_nodes:
                    b = m.node_tree.nodes.get("Principled BSDF")
                    if b:
                        b.inputs["Coat Weight"].default_value = 0.6
                        b.inputs["Roughness"].default_value = 0.3


CARS = {k: L.load("car_" + k, os.path.join(L.QC, f"{k}.fbx"), fix_car) for k in ("Taxi", "NormalCar1", "SUV", "SportsCar")}
for k, c in CARS.items():
    mn, mx = Vector(c["bounds"][0]), Vector(c["bounds"][1])
    print("CAR", k, tuple(round(v, 2) for v in (mx - mn)))


def car(k, x, y, heading):
    c = CARS[k]
    mn, mx = Vector(c["bounds"][0]), Vector(c["bounds"][1])
    dims = mx - mn
    length = max(dims.x, dims.y)
    s = 4.4 / length  # ~4.4 m long cars
    e = L.inst(c, (x, y, Z_ROAD - mn.z * s), rot=heading, scale=s)
    return e


CAR_LONG_AXIS_X = None  # resolved at runtime below
for (k, x, y, d) in [("Taxi", -9.0, -21.5, 1), ("NormalCar1", 13.0, -17.3, -1), ("SUV", 9.0, 25.0, 1), ("SportsCar", -13.0, 21.0, -1)]:
    c = CARS[k]
    mn, mx = Vector(c["bounds"][0]), Vector(c["bounds"][1])
    along_y = (mx - mn).y > (mx - mn).x
    heading = (-R90 if along_y else 0.0) if d > 0 else (R90 if along_y else math.pi)
    car(k, x, y, heading)

# =========================================================================== lighting / world
L.world_hdri(os.path.join(L.HDRI, "kiara_7_late-afternoon_4k.hdr"), strength=1.1, rot_z=math.radians(110), clamp=4.0, tint=(1.0, 0.88, 0.76), sat=1.1)
SUN = L.sun(energy=5.0, color=(1.0, 0.72, 0.47), elev=26, azim=-140, angle=3.0)

# =========================================================================== cameras + render
def shot(name, w, h, loc, target, lens, shift_y=0.0, samples=96, dof=None):
    cam = L.camera(loc, target, lens=lens, shift_y=shift_y, name="Cam_" + name, dof=dof)
    if PREVIEW:
        w, h, samples = w // 2, h // 2, 24
    L.render_settings(w, h, samples=samples, exposure=0.5, look="AgX - Punchy")
    out = os.path.join(OUTDIR, f"timeline_square_{name}_raw.png")
    L.render(out)
    # screen-space positions for HUD placement (normalised, origin top-left)
    sc = bpy.context.scene
    pts = {}
    for key, ob, off in [("gm_head", GM, Vector((0, 0, 3.9))), ("crier_head", crier, Vector((0, 0, 4.0)))]:
        wp = ob.matrix_world @ off
        v = world_to_camera_view(sc, cam, wp)
        pts[key] = [v.x, 1 - v.y]
    with open(out.replace(".png", ".json"), "w") as f:
        json.dump(pts, f, indent=1)
    print("RENDERED", out, pts)


if SHOT == "towertest":
    L.camera((0, -40, 20), (0, 200, 110), lens=24, name="tt")
    L.render_settings(800, 450, samples=12, exposure=0.15)
    L.render(os.path.join(OUTDIR, "wip", "towertest.png"))
if SHOT == "camtest":
    for i, (loc, tgt, lens, sh) in enumerate([((-13, -40, 31), (2, 6, 0), 30, 0.10), ((-12, -46, 36), (2, 4, 0), 26, 0.08),
                                             ((-10, -36, 27), (2, 4, 0), 32, 0.14), ((-16, -52, 38), (2, 4, 0), 30, 0.12)]):
        L.camera(loc, tgt, lens=lens, shift_y=sh, name=f"camtest{i}")
        L.render_settings(800, 450, samples=12, exposure=0.15)
        L.render(os.path.join(OUTDIR, "wip", f"camtest{i}.png"))
if SHOT in ("hero", "both"):
    shot("hero", 2400, 1350, (-11, -42, 24), (1.5, 4.0, 2.0), lens=28, shift_y=0.12,
         dof={"dist": None, "fstop": 2.8})
if SHOT in ("phone", "both"):
    shot("phone", 1200, 2670, (-1.5, -21.5, 11.0), (-0.5, 1.5, 1.0), lens=24, shift_y=0.0,
         dof={"dist": None, "fstop": 2.0})

if "--no-save" not in ARGS:
    L.save_blend(os.path.join(L.HERE, "timeline_square.blend"))
    print("SAVED blend")
