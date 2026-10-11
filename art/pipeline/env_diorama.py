"""Venue mock-up renders ("does the kit read as the place?"): lays out processed kit FBX files + baked NPCs
(posed with exported clips) and renders a high 3/4 tilt view like the game camera, golden-hour light.

Usage: art/pipeline/blender.sh art/pipeline/env_diorama.py -- <venue> <out.png>
venues: timeline_square | terminal_cafe | perp_pit | rekt_clinic | spaces_studio | ore_refinery | home
These layouts are previews only (the Unity agent builds the real scenes).
"""
import bpy, sys, os, json, math, re, random
from mathutils import Vector, Matrix, Euler
sys.path.insert(0, os.path.dirname(__file__))
import ctl_config as C
import ctl_render as R
from ctl_assemble import import_fbx, material

args = sys.argv[sys.argv.index('--') + 1:]
VENUE, OUTP = args[0], args[1]
MAN = json.load(open(os.path.join(C.PROCESSED, 'environment', 'manifest.json')))
NPC = json.load(open(os.path.join(C.PROCESSED, 'characters', 'npc', 'manifest.json')))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = 30
_cache = {}


def place(iid, x, y, rot=0.0, z=0.0, s=1.0):
    """instance a processed asset (LOD0) at (x, y) metres, rot degrees about Z."""
    if iid not in _cache:
        objs = import_fbx(os.path.join(C.PROCESSED, MAN[iid]['fbx']))
        keep = []
        for o in objs:
            if o.type != 'MESH':
                continue
            nm = re.sub(r'\.\d+$', '', o.name)
            if re.search(r'_LOD[12]$', nm):
                bpy.data.objects.remove(o, do_unlink=True)
                continue
            for m in o.data.materials:
                if not m or not m.use_nodes:
                    continue
                b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
                tn = next((n for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image), None)
                if tn and 'palette' in tn.image.name.lower():
                    tn.interpolation = 'Closest'
                if b and m.name.startswith('KEEP_'):
                    look = {'screen': ((0.03, 0.08, 0.06), (0.15, 0.95, 0.55), 1.2), 'ad_slot': ((0.98, 0.55, 0.15), (1, 0.6, 0.2), 0.4),
                            'glass': ((0.6, 0.8, 0.9), (0, 0, 0), 0.0), 'water': ((0.3, 0.65, 0.85), (0.2, 0.5, 0.8), 0.2),
                            'neon': ((0.2, 0.95, 0.8), (0.2, 1.0, 0.85), 3.0), 'on_air': ((1, 0.15, 0.15), (1, 0.15, 0.15), 3.0),
                            'glow': ((1, 0.45, 0.1), (1, 0.45, 0.1), 5.0), 'softbox': ((1, 0.97, 0.9), (1, 0.97, 0.9), 2.0)}
                    key = next((k for k in look if k in m.name), None)
                    if key:
                        base, em, st = look[key]
                        for l in list(b.inputs['Base Color'].links):
                            m.node_tree.links.remove(l)
                        for l in list(b.inputs['Emission Color'].links):
                            m.node_tree.links.remove(l)
                        b.inputs['Base Color'].default_value = (*base, 1)
                        b.inputs['Emission Color'].default_value = (*em, 1)
                        b.inputs['Emission Strength'].default_value = st
                if b and tn and m.get('ctl_alpha_clip') is not None or (tn and b and any(k in tn.image.name.lower() for k in ('leaves', 'grass', 'flower', 'petal'))):
                    try:
                        m.node_tree.links.new(tn.outputs['Alpha'], b.inputs['Alpha'])
                        m.surface_render_method = 'DITHERED'
                    except Exception:
                        pass
            keep.append(o)
        for o in keep:
            o.hide_render = True
            o.hide_viewport = True
        _cache[iid] = keep
    out = []
    for src in _cache[iid]:
        o = src.copy()
        bpy.context.scene.collection.objects.link(o)
        o.hide_render = False
        o.hide_viewport = False
        o.matrix_world = Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(rot), 4, 'Z') @ Matrix.Diagonal((s, s, s, 1)) @ src.matrix_world
        out.append(o)
    return out


_clip = {}


def npc(name, x, y, rot=0.0, clip='loco_idle', frame=10, z=0.0):
    base = os.path.join(C.PROCESSED, 'characters', 'npc')
    objs = import_fbx(os.path.join(base, NPC[name]['fbx']))
    arm = next(o for o in objs if o.type == 'ARMATURE')
    for o in objs:
        if o.type == 'MESH':
            nm = re.sub(r'\.\d+$', '', o.name)
            if not nm.endswith('_LOD0'):
                bpy.data.objects.remove(o, do_unlink=True)
                continue
            o.data.materials.clear()
            o.data.materials.append(material(os.path.join(base, NPC[name]['atlas']), 'skin'))
    if clip not in _clip:
        cobjs = import_fbx(os.path.join(C.PROCESSED, 'animations', clip + '.fbx'))
        a = next(o for o in cobjs if o.type == 'ARMATURE').animation_data.action
        for o in cobjs:
            bpy.data.objects.remove(o, do_unlink=True)
        _clip[clip] = a
    # bake one pose: evaluate the clip at `frame` and freeze it as pose-bone transforms
    arm.animation_data_create()
    arm.animation_data.action = _clip[clip]
    arm.animation_data.action_slot = _clip[clip].slots[0]
    bpy.context.scene.frame_set(frame)
    mats = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = mats[pb.name]
    arm.matrix_world = Matrix.Translation((x, y, z)) @ Matrix.Rotation(math.radians(rot), 4, 'Z') @ arm.matrix_world
    return arm


def grid(iid, x0, y0, nx, ny, step=2.0):
    for i in range(nx):
        for j in range(ny):
            place(iid, x0 + i * step + step / 2, y0 + j * step + step / 2)


def finish(cam_loc, cam_target, lens=35, res=(2400, 1500), sun=(48, 0, -38), sun_e=2.4, world=0.55, ground=None):
    R.setup_scene(res=res, samples=96, bg_top=(0.98, 0.86, 0.72), bg_bottom=(0.62, 0.76, 0.93), strength=world)
    if ground:
        R.add_ground(size=400, color=ground, z=-0.02)
    R.add_sun(rot_deg=sun, energy=sun_e, color=(1.0, 0.86, 0.68), angle_deg=4)
    R.add_area((cam_loc[0] * 0.5, cam_loc[1] * 0.5, 25), cam_target, energy=8000, size=30, color=(0.75, 0.85, 1.0))
    R.add_camera(cam_loc, cam_target, lens=lens)
    sc = bpy.context.scene
    try:
        sc.eevee.use_bloom = True
    except Exception:
        pass
    R.render(OUTP)


random.seed(3)
if VENUE == 'timeline_square':
    # 24 x 20 m plaza of paving, streets around, shops behind, towers on the skyline
    grid('plaza_paving_2m', -12, -10, 12, 10)
    for x in range(-12, 12, 2):
        place('plaza_hexpaving_2m', x + 1, -11)
    place('plaza_fountain', 0, 0)
    place('pixel_wall', 0, 9.3)
    for x in (-15, -9, 9, 15):
        pass
    # shops along the back (north, +Y) and the left side
    xs = -18
    for iid in ('shop_2story_sign', 'shop_3story_balcony', 'shop_2story_columns', 'shop_4story_center', 'shop_2story_wide', 'shop_3story_slim', 'shop_2story_balcony'):
        w = MAN[iid]['size_m'][0]
        place(iid, xs + w / 2, 14.5, rot=0)
        xs += w + 0.4
    ys = -8
    for iid in ('shop_1story_sign', 'shop_2story_sign', 'shop_3story_balcony'):
        d = MAN[iid]['size_m'][0]
        place(iid, -17.5, ys + d / 2, rot=90)
        ys += d + 0.6
    for i, iid in enumerate(('tower_glass_a', 'tower_glass_c', 'tower_glass_e', 'tower_glass_b')):
        place(iid, -16 + i * 16, 40)
    # streets: one along the front, one along the right
    place('street_4lane', 0, -16.5, rot=90)
    place('street_4lane', -18, -16.5, rot=90)
    place('street_4lane', 18, -16.5, rot=90)
    for y in (-3, 3, 9):
        place('street_2lane', 19, y + 3)
    # sidewalk strip between plaza and street
    for x in range(-12, 12, 3):
        place('sidewalk_straight_3m', x + 1.5, -12.5, rot=90)
    # pink blossom ring + flowers
    for i, (x, y) in enumerate(((-9, -6), (9, -6), (-9, 5), (9, 5), (-5, 7.5), (5, 7.5))):
        place(f'tree_blossom_{1 + i % 4}', x, y, rot=random.uniform(0, 360))
        place('flowers_pink_group', x + 1.2, y - 0.8)
        place('flower_clump_2', x - 1.0, y - 1.1)
        place('bush_large_flowers', x + 0.4, y + 1.4, rot=random.uniform(0, 360))
    for (x, y) in ((-3.8, -3.2), (3.8, -3.2), (-3.8, 3.2), (3.8, 3.2)):
        place('park_bench', x, y, rot=math.degrees(math.atan2(-y, -x)) + 90)
    for (x, y) in ((-11, -11), (11, -11), (-11, 8.5), (11, 8.5), (0, -11)):
        place('street_lamp_double', x, y)
    place('billboard_large', 13.5, 10.5, rot=20)
    place('ad_kiosk', -6.5, -9.5, rot=10)
    place('crypto_atm', 7.5, -9.2)
    place('news_ticker', -10.5, 11.0, z=8.0)
    for (x, y) in ((-2, -10.2), (2, -10.2)):
        place('bollard', x, y)
    place('car_taxi', 6, -15.0, rot=90)
    place('car_sedan', -9, -18.2, rot=-90)
    # NPCs
    npc('town_crier', 0, -3.6, rot=0, clip='talk_host_with_mic', frame=12)
    npc('giveaway_guy', -2.2, -6.0, rot=-20, clip='emote_wave', frame=30)
    npc('reply_guy', 2.6, -5.5, rot=25, clip='act_phone_scroll', frame=8)
    npc('doomposter', 6.5, -2.0, rot=60, clip='emote_cry', frame=10)
    npc('wallet_tracker', -6.5, 1.0, rot=-70, clip='loco_idle', frame=20)
    npc('perp_degen', 4.6, 2.6, rot=140, clip='emote_wagmi_fist_pump', frame=28)
    npc('normie_tourist', -1.5, 5.5, rot=180, clip='act_phone_photo', frame=30)
    npc('engagement_farmer', 1.6, 5.0, rot=200, clip='emote_ring_light_pose', frame=20)
    npc('rekt_guy', -3.8, -3.2, rot=150, clip='act_sit_head_in_hands', frame=10, z=0.02)
    npc('vc', 8.0, -7.0, rot=-30, clip='loco_walk', frame=10)
    finish((17, -25, 17), (0, 1.5, 1.0), lens=38, ground=(0.55, 0.62, 0.45))

elif VENUE == 'terminal_cafe':
    grid('floor_wood_2m', -6, -4, 6, 4)
    for x in range(-6, 6, 2):
        place('wall_brick_window_2m' if x in (-4, 2) else 'wall_brick_solid_2m', x + 1, 4.05)
    for y in range(-4, 4, 2):
        place('wall_brick_door_2m' if y == -2 else 'wall_brick_solid_2m', -6.05, y + 1, rot=90)
    place('wall_screen_large', 0, 3.9, z=1.0, rot=180)
    for i, x in enumerate((-4.5, -3.5, -2.5)):
        place('bar_counter', x, 2.4, rot=180)
    place('bar_counter_corner', -5.5, 2.4, rot=180)
    place('coffee_machine', -4.4, 2.35, z=0.95, rot=180)
    place('cash_register', -2.6, 2.35, z=0.95, rot=180)
    place('cake', -3.4, 2.35, z=0.95)
    for (x, y) in ((-1.5, -1.0), (1.5, -1.0), (-1.5, -3.0)):
        place('cafe_table_small', x, y)
        place('cafe_chair_a', x - 0.7, y, rot=90)
        place('cafe_chair_b', x + 0.7, y, rot=-90)
        place('cup_coffee', x + 0.12, y + 0.05, z=0.76)
        place('laptop', x - 0.15, y - 0.05, z=0.76, rot=170)
    for i, x in enumerate((2.4, 3.6, 4.8)):
        place('trading_terminal', x, 2.6, rot=180)
    place('kitchen_bar', 4.5, -2.5, rot=90)
    place('bar_stool', 4.0, -2.2, rot=90)
    place('potted_plant', 5.4, 3.3)
    place('houseplant_tall', -5.4, -3.4)
    place('sandwich_board', -7.5, -2.0, rot=-90)
    npc('wallet_tracker', 2.4, 1.9, rot=180, clip='act_phone_hold', frame=5)
    npc('perp_degen', 3.6, 1.9, rot=180, clip='loco_idle', frame=10)
    npc('community_mod', 4.8, 1.9, rot=180, clip='emote_thumbs_up', frame=30)
    npc('vc', -1.9, -1.0, rot=-90, clip='act_sit', frame=10, z=0.0)
    npc('auditor', 1.9, -1.0, rot=90, clip='act_sit_talk', frame=20)
    npc('giveaway_guy', -3.5, 1.5, rot=180, clip='emote_gm_coffee', frame=20)
    npc('doxxed_founder', 0.5, -2.8, rot=30, clip='talk_gesture_1', frame=20)
    finish((11, -14, 12), (-0.5, 0.5, 1.0), lens=35, ground=(0.75, 0.72, 0.66))
else:
    raise SystemExit('unknown venue ' + VENUE)
