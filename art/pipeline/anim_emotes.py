"""Hand-keyed CT Life emotes / actions (made by us = CC0), layered on retargeted UAL base clips.

Usage: art/pipeline/blender.sh art/pipeline/anim_emotes.py -- [clip,clip,...]
Needs art/raw/work/anims_male.blend from anim_retarget.py. Writes art/processed/animations/<clip>.fbx
and updates clips.json. Poses use ctl_anim's language: D() direction, R() delta, L() local, P() pelvis, IK().
"""
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(__file__))
import ctl_config as C
from ctl_anim import Rig, build_action, export_clip, solve_pose, spec_at, floor_lift, D, R, L, P, LR, IK, mirror, both, FPS
from mathutils import Matrix

ONLY = sys.argv[sys.argv.index('--') + 1].split(',') if '--' in sys.argv and len(sys.argv) > sys.argv.index('--') + 1 else None
OUT = os.path.join(C.PROCESSED, 'animations')

# ------------------------------------------------------------------ reusable poses (left side; mirror for right)
ARM_DOWN = {'upperarm_l': D(0.12, 0.02, -1), 'lowerarm_l': D(0.08, -0.12, -1), 'hand_l': D(0.05, -0.1, -1)}
FIST = {'index_01_l': L(85, 0, 0), 'thumb_01_l': L(40, 0, 0)}
FLAT = {'index_01_l': L(0, 0, 0)}
HEAD_DOWN = {'Head': R(18, 0, 0)}
HEAD_UP = {'Head': R(-16, 0, 0)}


def merge(*ds):
    out = {}
    for d in ds:
        out.update(d)
    return out


def r(d):
    return mirror(d)


def osc(t0, t1, n, a, b):
    """alternate between poses a and b n times from t0 to t1"""
    ks = []
    for i in range(n + 1):
        ks.append((t0 + (t1 - t0) * i / n, a if i % 2 == 0 else b))
    return ks


E = {}

# --- social emotes (1.5-3 s) ---------------------------------------------------------------------------
wave_up = r({'upperarm_l': D(0.55, -0.1, 0.83), 'lowerarm_l': D(0.12, -0.25, 0.96), 'hand_l': D(0.3, -0.2, 0.93), 'index_01_l': D(0.3, -0.2, 0.93)})
wave_a = merge(wave_up, r({'lowerarm_l': D(0.45, -0.25, 0.86), 'hand_l': D(0.5, -0.2, 0.84)}))
wave_b = merge(wave_up, r({'lowerarm_l': D(-0.15, -0.25, 0.96), 'hand_l': D(-0.2, -0.2, 0.96)}))
E['emote_wave'] = dict(base='loco_idle', dur=2.2, keys=[(0.35, wave_up)] + osc(0.45, 1.75, 5, wave_a, wave_b) + [(1.9, wave_up)])

point = r({'upperarm_l': D(0.12, -0.99, 0.1), 'lowerarm_l': D(0.05, -1, 0.08), 'hand_l': D(0.03, -1, 0.05), 'index_01_l': D(0, -1, 0.0),
           'thumb_01_l': L(30, 0, 0)})
E['emote_point'] = dict(base='loco_idle', dur=1.8, keys=[(0.35, merge(point, {'spine_03': R(0, 0, 8)})), (1.4, merge(point, {'spine_03': R(0, 0, 8), 'Head': R(-4, 0, 6)}))])

clap_open = both({'upperarm_l': IK('spine_03', (0.16, -0.36, 0.18), (1, 0.2, -1)), 'hand_l': D(-0.5, -0.8, 0.3), 'index_01_l': D(-0.5, -0.8, 0.3)})
clap_shut = both({'upperarm_l': IK('spine_03', (0.035, -0.36, 0.2), (1, 0.2, -1)), 'hand_l': D(-0.75, -0.6, 0.25), 'index_01_l': D(-0.75, -0.6, 0.25)})
E['emote_clap'] = dict(base='loco_idle', dur=2.2, keys=[(0.3, clap_open)] + osc(0.4, 1.85, 8, clap_shut, clap_open))

thumbs = merge(r({'upperarm_l': D(0.35, -0.55, -0.76), 'lowerarm_l': D(0.05, -0.85, 0.52), 'hand_l': D(0.0, -0.9, 0.4),
                  'index_01_l': L(95, 0, 0), 'thumb_01_l': D(0.0, -0.2, 0.98)}), {'Head': R(-5, 0, 0)})
E['emote_thumbs_up'] = dict(base='loco_idle', dur=1.8, keys=[(0.35, thumbs), (1.4, merge(thumbs, {'spine_03': R(-4, 0, 0)}))])

facepalm = merge(r({'upperarm_l': IK('Head', (0.03, -0.16, 0.0), (1, 0.4, -0.6)), 'hand_l': D(-0.35, -0.2, 0.92), 'index_01_l': D(-0.35, -0.1, 0.94)}),
                 {'Head': R(22, 0, 0), 'neck_01': R(8, 0, 0), 'spine_03': R(6, 0, 0)})
facepalm_s = merge(facepalm, {'Head': R(22, 0, 9)})
facepalm_s2 = merge(facepalm, {'Head': R(22, 0, -9)})
E['emote_facepalm'] = dict(base='loco_idle', dur=2.4, keys=[(0.45, facepalm)] + osc(0.8, 1.8, 4, facepalm_s, facepalm_s2) + [(2.0, facepalm)])

shrug = merge(both({'clavicle_l': R(0, 14, 0), 'upperarm_l': D(0.45, -0.3, -0.84), 'lowerarm_l': D(0.55, -0.83, 0.05),
                    'hand_l': D(0.6, -0.8, 0.0, -60), 'index_01_l': D(0.6, -0.8, 0.0)}), {'Head': R(0, 12, 0)})
E['emote_shrug'] = dict(base='loco_idle', dur=1.7, keys=[(0.4, shrug), (1.15, shrug)])

laugh_a = merge(both({'upperarm_l': IK('spine_01', (0.12, -0.17, 0.0), (1, 0.3, -0.5)), 'hand_l': D(-0.6, -0.5, -0.3)}), {'spine_03': R(-10, 0, 0), 'Head': R(-22, 0, 0)})
laugh_b = merge(laugh_a, {'spine_03': R(10, 0, 0), 'spine_02': R(6, 0, 0), 'Head': R(-6, 0, 0)})
E['emote_laugh'] = dict(base='loco_idle', dur=2.4, keys=[(0.3, laugh_a)] + osc(0.45, 2.0, 5, laugh_b, laugh_a))

bow = merge({'spine_01': R(14, 0, 0), 'spine_02': R(14, 0, 0), 'spine_03': R(14, 0, 0), 'Head': R(10, 0, 0)}, both(ARM_DOWN))
E['emote_bow'] = dict(base='loco_idle', dur=2.0, keys=[(0.55, bow), (1.2, bow)])

salute = r({'upperarm_l': D(0.92, -0.38, 0.05), 'lowerarm_l': D(-0.55, -0.45, 0.7), 'hand_l': D(-0.75, -0.3, 0.6), 'index_01_l': D(-0.75, -0.3, 0.6)})
E['emote_salute'] = dict(base='loco_idle', dur=1.8, keys=[(0.3, salute), (1.3, salute)])

heart = both({'upperarm_l': IK('spine_03', (0.075, -0.38, 0.2), (1, 0.1, -1)), 'hand_l': D(-0.55, -0.55, 0.62), 'index_01_l': D(-0.7, -0.4, 0.6),
              'thumb_01_l': D(-0.6, -0.5, -0.6)})
E['emote_heart_hands'] = dict(base='loco_idle', dur=2.2, keys=[(0.45, heart), (1.0, merge(heart, {'Head': R(0, 8, 0)})), (1.6, merge(heart, {'Head': R(0, -6, 0)}))])

pump_up = merge(r(merge({'upperarm_l': D(0.4, -0.15, 0.9), 'lowerarm_l': D(0.15, -0.1, 0.98), 'hand_l': D(0.1, -0.1, 0.99)}, FIST)), {'Head': R(-10, 0, 0)})
pump_dn = merge(r(merge({'upperarm_l': D(0.55, -0.25, 0.45), 'lowerarm_l': D(-0.1, -0.35, 0.93), 'hand_l': D(0.0, -0.3, 0.95)}, FIST)), {'Head': R(-4, 0, 0)})
E['emote_wagmi_fist_pump'] = dict(base='loco_idle', dur=2.0, keys=[(0.3, pump_dn)] + osc(0.45, 1.65, 4, pump_up, pump_dn))

nod_a = {'Head': R(14, 0, 0)}
nod_b = {'Head': R(-4, 0, 0)}
E['talk_listen_nod'] = dict(base='loco_idle', dur=2.0, loop=True, keys=osc(0.0, 2.0, 4, nod_b, nod_a))

# --- tribe emotes ------------------------------------------------------------------------------------------
slam_up = merge(both({'upperarm_l': D(0.25, -0.2, 0.95), 'lowerarm_l': D(0.05, -0.1, 0.99), 'hand_l': D(0, 0, 1)}), {'spine_03': R(-10, 0, 0), 'Head': R(-12, 0, 0)}, both(FIST))
slam_dn = merge(both({'upperarm_l': D(0.2, -0.95, -0.25), 'lowerarm_l': D(0.05, -0.6, -0.8), 'hand_l': D(0.0, -0.4, -0.9)}),
                {'spine_01': R(10, 0, 0), 'spine_02': R(12, 0, 0), 'spine_03': R(12, 0, 0), 'pelvis': P(0, 0.02, -0.12)}, both(FIST),
                both({'thigh_l': R(-25, 0, 0), 'calf_l': R(40, 0, 0)}))
E['emote_send_it_slam'] = dict(base='loco_idle', dur=1.8, keys=[(0.45, slam_up), (0.62, slam_up), (0.78, slam_dn), (1.25, slam_dn)])

thump_out = merge(r(merge({'upperarm_l': D(0.5, -0.4, -0.77), 'lowerarm_l': D(0.2, -0.9, 0.4)}, FIST)), {'spine_03': R(-6, 0, 0)})
thump_in = merge(r(merge({'upperarm_l': IK('spine_03', (0.08, -0.18, 0.15), (1, 0, -1)), 'hand_l': D(-0.6, 0.0, 0.2)}, FIST)), {'spine_03': R(4, 0, 0), 'Head': R(-12, 0, 0)})
E['emote_chest_thump'] = dict(base='loco_idle', dur=1.8, keys=[(0.3, thump_out)] + osc(0.45, 1.45, 4, thump_in, thump_out))

flip_a = r({'upperarm_l': D(0.35, -0.55, -0.76), 'lowerarm_l': D(0.05, -0.95, 0.2), 'hand_l': D(0.0, -0.95, 0.25, -90), 'thumb_01_l': L(0, 0, 0)})
flip_b = merge(r({'upperarm_l': D(0.35, -0.6, -0.7), 'lowerarm_l': D(0.05, -0.75, 0.66), 'hand_l': D(0.0, -0.5, 0.86, -90), 'thumb_01_l': L(-40, 0, 0)}), {'Head': R(-25, 0, 0)})
E['emote_coin_flip'] = dict(base='loco_idle', dur=2.0, keys=[(0.4, flip_a), (0.55, flip_b), (0.8, merge(flip_a, {'Head': R(-30, 0, 0)})), (1.25, merge(flip_a, {'Head': R(10, 0, 0)})), (1.6, flip_a)])

ring_pose = merge(r({'upperarm_l': D(0.3, -0.75, 0.6), 'lowerarm_l': D(0.05, -0.9, 0.43), 'hand_l': D(0.05, -0.9, 0.43)}),
                  {'upperarm_l': IK('pelvis', (0.2, -0.05, 0.08), (0.6, 0.8, 0)), 'hand_l': D(-0.4, 0.2, -0.9)},
                  {'Head': R(-6, 12, -10), 'spine_03': R(0, -4, 0), 'pelvis': P(0.02, 0, 0)})
E['emote_ring_light_pose'] = dict(base='loco_idle', dur=2.4, loop=True, keys=[(0.0, ring_pose), (1.2, merge(ring_pose, {'Head': R(-4, 6, -14)})), (2.4, ring_pose)])

hood_a = both({'upperarm_l': IK('Head', (0.12, 0.04, 0.02), (1, 0.3, 0)), 'hand_l': D(0, 0.4, 0.9)})
hood_b = both({'upperarm_l': IK('Head', (0.1, -0.02, 0.17), (1, 0.2, 0.3)), 'hand_l': D(-0.3, -0.6, 0.6)})
E['emote_hood_up'] = dict(base='loco_idle', dur=1.8, keys=[(0.35, hood_a), (0.85, hood_b), (1.25, merge(hood_b, {'Head': R(10, 0, 0)}))])

water = merge(r({'upperarm_l': D(0.3, -0.75, -0.6), 'lowerarm_l': D(0.1, -0.95, -0.2), 'hand_l': D(0.1, -0.9, -0.4)}), {'spine_03': R(8, 0, 0), 'Head': R(12, 0, 0)})
water2 = merge(water, r({'hand_l': D(0.1, -0.6, -0.8)}))
E['emote_watering_can'] = dict(base='loco_idle', dur=2.4, keys=[(0.4, water)] + osc(0.6, 2.0, 4, water2, water))

E['emote_hood_pull'] = None  # placeholder removed below

# --- despair ---------------------------------------------------------------------------------------------
cry = merge(both({'upperarm_l': IK('Head', (0.05, -0.15, 0.02), (1, 0.2, -1)), 'hand_l': D(-0.3, -0.3, 0.9), 'index_01_l': D(-0.3, -0.2, 0.93)}),
            {'Head': R(24, 0, 0), 'neck_01': R(8, 0, 0), 'spine_03': R(10, 0, 0), 'spine_02': R(5, 0, 0)})
cry_b = merge(cry, {'spine_03': R(14, 0, 0), 'Head': R(26, 0, 3)})
E['emote_cry'] = dict(base='loco_idle', dur=2.0, loop=True, keys=osc(0.0, 2.0, 8, cry, cry_b))

E['act_sit_head_in_hands'] = dict(base='act_sit', dur=2.4, loop=True, keys=osc(0.0, 2.4, 6,
    merge(cry, {'spine_02': R(14, 0, 0), 'spine_03': R(16, 0, 0)}), merge(cry_b, {'spine_02': R(14, 0, 0), 'spine_03': R(20, 0, 0)})))

shock = merge(both({'upperarm_l': IK('Head', (0.09, -0.12, -0.03), (1, 0.3, -1)), 'hand_l': D(-0.2, -0.3, 0.93)}),
              {'Head': R(-16, 0, 0), 'spine_03': R(-10, 0, 0), 'pelvis': P(0, 0.05, 0)})
E['react_shock_gasp'] = dict(base='loco_idle', dur=1.6, keys=[(0.18, shock), (1.0, shock)])

stomp_up = merge(both(merge({'upperarm_l': D(0.35, 0.1, -0.93), 'lowerarm_l': D(0.2, -0.5, -0.84)}, FIST)),
                 {'thigh_r': D(-0.12, -0.7, -0.7), 'calf_r': D(-0.12, 0.1, -1), 'spine_03': R(-6, 0, 0), 'Head': R(-6, 0, 0)})
stomp_dn = merge(both(merge({'upperarm_l': D(0.35, 0.15, -0.92), 'lowerarm_l': D(0.25, -0.35, -0.9)}, FIST)),
                 {'spine_03': R(12, 0, 0), 'Head': R(14, 0, 0), 'pelvis': P(0, 0, -0.04)})
E['react_rage_stomp'] = dict(base='loco_idle', dur=1.8, keys=[(0.3, stomp_up), (0.5, stomp_dn), (0.8, mirror(stomp_up)), (1.0, mirror(stomp_dn)), (1.35, stomp_dn)])

kneel = merge({'pelvis': P(0, 0.05, -0.42), 'thigh_l': D(0.05, -0.75, -0.66), 'calf_l': D(0.02, 0.05, -1), 'foot_l': D(0, 0.6, -0.8),
               'thigh_r': D(-0.05, -0.1, -1), 'calf_r': D(-0.02, 0.98, -0.2), 'foot_r': D(0, 0.95, -0.3)},
              both(ARM_DOWN), {'spine_03': R(16, 0, 0), 'Head': R(20, 0, 0)})
E['react_kneel_despair'] = dict(base='loco_idle', dur=2.2, keys=[(0.7, kneel), (1.6, merge(kneel, {'Head': R(28, 0, 0)}))])

# --- seated terminal work -----------------------------------------------------------------------------------
type_a = merge(both({'upperarm_l': IK('spine_03', (0.17, -0.38, -0.06), (1, 0.5, -0.4)), 'hand_l': D(0.05, -1, -0.15), 'index_01_l': D(0.0, -1, -0.35)}),
               {'Head': R(12, 0, 0), 'spine_03': R(6, 0, 0)})
type_b = merge(type_a, both({'upperarm_l': IK('spine_03', (0.17, -0.38, -0.04), (1, 0.5, -0.4))}), {'hand_r': D(-0.05, -1, -0.25)})
type_c = merge(type_a, {'hand_l': D(0.05, -1, -0.25)})
E['act_type'] = dict(base='act_sit', dur=1.2, loop=True, keys=[(0.0, type_a), (0.3, type_b), (0.6, type_a), (0.9, type_c), (1.2, type_a)])
ft_a = merge(type_a, {'spine_03': R(14, 0, 0), 'spine_02': R(6, 0, 0), 'Head': R(18, 0, 0)})
ft_b = merge(ft_a, both({'upperarm_l': IK('spine_03', (0.2, -0.4, -0.02), (1, 0.5, -0.4))}), {'Head': R(14, 0, 4)})
ft_c = merge(ft_a, both({'upperarm_l': IK('spine_03', (0.14, -0.36, -0.07), (1, 0.5, -0.4))}), {'Head': R(20, 0, -4)})
E['act_frantic_type'] = dict(base='act_sit', dur=0.8, loop=True, keys=[(0.0, ft_a), (0.13, ft_b), (0.27, ft_c), (0.4, ft_a), (0.53, ft_c), (0.67, ft_b), (0.8, ft_a)])
lean = merge(both({'upperarm_l': D(0.6, 0.35, 0.72), 'lowerarm_l': D(-0.75, 0.45, 0.48), 'hand_l': D(-0.8, 0.5, 0.2)}),
             {'spine_02': R(-10, 0, 0), 'spine_03': R(-10, 0, 0), 'Head': R(-8, 0, 0)})
E['act_lean_back'] = dict(base='act_sit', dur=3.0, loop=True, keys=[(0.0, lean), (1.5, merge(lean, {'Head': R(-12, 0, 8)})), (3.0, lean)])
slam_raise = merge(both(merge({'upperarm_l': IK('spine_03', (0.2, -0.3, 0.25), (1, 0.3, -0.5)), 'hand_l': D(0, -0.8, 0.6)}, FIST)), {'spine_03': R(-6, 0, 0)})
slam_hit = merge(both(merge({'upperarm_l': IK('spine_03', (0.18, -0.42, -0.1), (1, 0.4, -0.4)), 'hand_l': D(0, -0.9, -0.4)}, FIST)), {'spine_03': R(14, 0, 0), 'Head': R(16, 0, 0)})
E['act_desk_slam'] = dict(base='act_sit', dur=1.6, keys=[(0.35, slam_raise), (0.5, slam_raise), (0.62, slam_hit), (1.1, slam_hit)])

# --- phone (upper body; mask in Unity) -------------------------------------------------------------------
ph_hold = merge(r({'upperarm_l': IK('spine_03', (0.1, -0.33, 0.12), (1, 0.4, -1)), 'hand_l': D(-0.3, -0.75, 0.6, 80), 'index_01_l': D(-0.3, -0.75, 0.6)}),
                {'Head': R(22, 0, 0), 'neck_01': R(6, 0, 0)})
ph_hip = merge(r({'upperarm_l': D(0.2, 0.1, -0.97), 'lowerarm_l': D(0.15, -0.3, -0.94), 'hand_l': D(0.1, -0.2, -0.97)}))
E['act_phone_pull_out'] = dict(base='loco_idle', dur=1.0, keys=[(0.15, ph_hip), (0.75, ph_hold)])
E['act_phone_hold'] = dict(base='loco_idle', dur=2.4, loop=True, keys=[(0.0, ph_hold), (1.2, merge(ph_hold, {'Head': R(24, 0, 4)})), (2.4, ph_hold)])
scr_a = merge(ph_hold, r({'thumb_01_l': L(-10, 0, 0)}))
scr_b = merge(ph_hold, r({'thumb_01_l': L(25, 0, 0)}))
E['act_phone_scroll'] = dict(base='loco_idle', dur=1.6, loop=True, keys=osc(0.0, 1.6, 4, scr_a, scr_b))
ph_photo = merge(r({'upperarm_l': IK('Head', (0.06, -0.42, 0.02), (1, 0, -1)), 'hand_l': D(0.05, -0.15, 0.98, 90), 'index_01_l': D(0.05, -0.15, 0.98)}),
                 {'Head': R(-4, 0, 0)})
E['act_phone_photo'] = dict(base='loco_idle', dur=1.8, keys=[(0.4, ph_photo), (0.9, merge(ph_photo, {'Head': R(-2, 0, 3)})), (1.3, ph_photo)])
E['act_phone_put_away'] = dict(base='loco_idle', dur=1.0, keys=[(0.0, ph_hold), (0.7, ph_hip)], ramp_in=0.0)

# --- talk ---------------------------------------------------------------------------------------------------
g1a = merge(both({'upperarm_l': D(0.35, -0.35, -0.87), 'lowerarm_l': D(0.35, -0.9, 0.2), 'hand_l': D(0.4, -0.85, 0.2, -50)}))
g1b = merge(both({'upperarm_l': D(0.3, -0.4, -0.86), 'lowerarm_l': D(0.15, -0.9, 0.35), 'hand_l': D(0.2, -0.9, 0.35, -70)}), {'Head': R(-4, 0, 5)})
E['talk_gesture_1'] = dict(base='loco_idle', dur=2.4, loop=True, keys=[(0.0, g1a), (0.6, g1b), (1.2, g1a), (1.8, merge(g1b, {'Head': R(-4, 0, -5)})), (2.4, g1a)])
g2a = merge(r({'upperarm_l': D(0.35, -0.4, -0.85), 'lowerarm_l': D(0.1, -0.95, 0.3), 'hand_l': D(0.1, -0.95, 0.3, -40)}), ARM_DOWN)
g2b = merge(r({'upperarm_l': D(0.4, -0.5, -0.77), 'lowerarm_l': D(0.4, -0.85, 0.35), 'hand_l': D(0.5, -0.8, 0.3, -70)}), ARM_DOWN, {'Head': R(0, 0, 8)})
E['talk_gesture_2'] = dict(base='loco_idle', dur=2.0, loop=True, keys=[(0.0, g2a), (0.5, g2b), (1.0, g2a), (1.5, g2b), (2.0, g2a)])
mic = merge(r(merge({'upperarm_l': IK('Head', (0.03, -0.17, -0.07), (1, 0.4, -1)), 'hand_l': D(-0.2, -0.5, 0.84)}, FIST)),
            {'upperarm_l': D(0.35, -0.4, -0.85), 'lowerarm_l': D(0.35, -0.9, 0.2), 'hand_l': D(0.4, -0.85, 0.2, -50)})
mic_b = merge(mic, {'lowerarm_l': D(0.15, -0.9, 0.38), 'Head': R(0, 0, 8)})
E['talk_host_with_mic'] = dict(base='loco_idle', dur=2.4, loop=True, keys=[(0.0, mic), (0.8, mic_b), (1.6, mic), (2.4, mic)])

# --- jumps / liquidation extras --------------------------------------------------------------------------
crouch = merge({'pelvis': P(0, 0.03, -0.16)}, both({'thigh_l': D(0.12, -0.55, -0.83), 'calf_l': D(0.1, 0.45, -0.9)}), both(ARM_DOWN), {'spine_03': R(10, 0, 0)})
air = merge({'pelvis': P(0, 0, 0.42)}, both({'upperarm_l': D(0.55, -0.15, 0.82), 'lowerarm_l': D(0.4, -0.1, 0.91), 'hand_l': D(0.4, -0.1, 0.9)}),
            both({'thigh_l': D(0.15, -0.6, -0.78), 'calf_l': D(0.15, 0.5, -0.85)}), both(FIST), {'Head': R(-16, 0, 0), 'spine_03': R(-8, 0, 0)})
E['react_moon_jump'] = dict(base='loco_idle', dur=1.6, keys=[(0.25, crouch), (0.5, air), (0.8, merge(air, {'pelvis': P(0, 0, 0.36)})), (1.05, crouch), (1.35, crouch)])
rug1 = merge({'pelvis': P(0, 0.12, -0.25)}, both({'thigh_l': D(0.1, -0.8, -0.2), 'calf_l': D(0.1, -0.7, -0.6)}),
             both({'upperarm_l': D(0.6, 0.1, 0.8), 'lowerarm_l': D(0.5, -0.2, 0.84)}), {'spine_03': R(-25, 0, 0), 'spine_02': R(-15, 0, 0), 'Head': R(-10, 0, 0)})
rug2 = merge({'pelvis': LR(0, 0.38, -0.78, -78, 0, 0)}, both({'thigh_l': D(0.15, -0.95, 0.25), 'calf_l': D(0.12, -0.95, -0.1)}),
             both({'upperarm_l': D(0.95, 0.25, 0.1), 'lowerarm_l': D(0.9, 0.3, 0.2)}), {'Head': R(12, 0, 0)})
E['react_rug_pull_fall'] = dict(base='loco_idle', dur=1.6, keys=[(0.15, rug1), (0.55, rug2), (1.6, rug2)], ramp_out=0.0)
pop0 = merge({'pelvis': P(0, 0, -0.3)}, both({'thigh_l': D(0.15, -0.6, -0.78), 'calf_l': D(0.12, 0.6, -0.8)}), both(ARM_DOWN), {'spine_03': R(20, 0, 0), 'Head': R(18, 0, 0)})
pop1 = merge({'pelvis': P(0, 0, 0.12)}, both({'upperarm_l': D(0.85, -0.2, 0.5), 'lowerarm_l': D(0.9, -0.2, 0.4), 'hand_l': D(0.9, -0.2, 0.4)}), {'Head': R(-12, 0, 0)})
E['react_ghost_respawn_pop'] = dict(base='loco_idle', dur=1.2, keys=[(0.0, pop0), (0.35, pop1), (0.75, merge(pop1, {'pelvis': P(0, 0, 0.0)}))], ramp_in=0.0)

# --- idle fidgets ---------------------------------------------------------------------------------------------
look = [(0.0, {'Head': R(0, 0, 0)}), (0.8, {'Head': R(0, 0, 35), 'neck_01': R(0, 0, 10)}), (1.6, {'Head': R(0, 0, 35), 'neck_01': R(0, 0, 10)}),
        (2.5, {'Head': R(0, 0, -35), 'neck_01': R(0, 0, -10)}), (3.3, {'Head': R(0, 0, -35), 'neck_01': R(0, 0, -10)}), (4.0, {'Head': R(0, 0, 0)})]
E['idle_fidget_lookaround'] = dict(base='loco_idle', dur=4.0, keys=look)
stretch = merge(both({'upperarm_l': D(0.25, 0.05, 0.97), 'lowerarm_l': D(-0.2, 0.05, 0.98), 'hand_l': D(-0.4, 0.05, 0.9)}), {'spine_03': R(-10, 0, 0), 'Head': R(-14, 0, 0)})
E['idle_fidget_stretch'] = dict(base='loco_idle', dur=3.0, keys=[(0.8, stretch), (1.6, merge(stretch, {'spine_03': R(-8, 6, 0)})), (2.2, stretch)])

# --- extra dances (8 beats at 120 BPM = 4 s, loop) ------------------------------------------------------------
def dance(beats):
    ks = []
    for i, p in enumerate(beats * 2):
        ks.append((i * 0.5, p))
    ks.append((len(beats) * 2 * 0.5, beats[0]))
    return ks

d2a = merge({'pelvis': P(0.06, 0, -0.05)}, both({'upperarm_l': D(0.5, -0.3, -0.8), 'lowerarm_l': D(0.3, -0.9, 0.3)}), both(FIST), {'spine_03': R(0, 8, 0), 'Head': R(6, 8, 0)})
d2b = merge(mirror(d2a), {'pelvis': P(-0.06, 0, -0.05)})
E['emote_dance_2'] = dict(base='loco_idle', dur=4.0, loop=True, keys=dance([d2a, d2b, d2a, d2b]))
d3a = merge(r({'upperarm_l': D(0.5, -0.2, 0.84), 'lowerarm_l': D(0.45, -0.2, 0.87), 'hand_l': D(0.45, -0.2, 0.87), 'index_01_l': D(0.45, -0.2, 0.87)}),
            {'upperarm_l': IK('pelvis', (0.2, -0.05, 0.06), (0.6, 0.8, 0))}, {'pelvis': P(0.04, 0, 0)})
d3b = merge(r({'upperarm_l': D(0.35, -0.5, -0.79), 'lowerarm_l': D(-0.5, -0.6, -0.6), 'hand_l': D(-0.5, -0.6, -0.6)}),
            {'upperarm_l': IK('pelvis', (0.2, -0.05, 0.06), (0.6, 0.8, 0))}, {'pelvis': P(-0.02, 0, -0.04)})
E['emote_dance_disco'] = dict(base='loco_idle', dur=4.0, loop=True, keys=dance([d3a, d3b, d3a, d3b]))
d4a = merge({'pelvis': P(0, 0, -0.08)}, both({'upperarm_l': D(0.7, -0.1, 0.7), 'lowerarm_l': D(0.0, -0.1, 1.0), 'hand_l': D(-0.2, -0.1, 0.98)}), {'Head': R(10, 0, 0)})
d4b = merge({'pelvis': P(0, 0, 0.0)}, both({'upperarm_l': D(0.7, -0.1, 0.72), 'lowerarm_l': D(0.05, -0.1, 0.99), 'hand_l': D(0.0, -0.1, 1.0)}), {'Head': R(-12, 0, 0)})
E['emote_dance_raise_roof'] = dict(base='loco_idle', dur=4.0, loop=True, keys=dance([d4a, d4b, d4a, d4b]))
d5a = merge({'pelvis': P(0.05, 0, -0.03), 'spine_01': R(0, 0, 12), 'spine_03': R(0, 0, 10)}, both({'upperarm_l': D(0.45, 0.2, -0.87), 'lowerarm_l': D(0.4, -0.6, -0.7)}), both(FIST))
d5b = merge({'pelvis': P(-0.05, 0, -0.03), 'spine_01': R(0, 0, -12), 'spine_03': R(0, 0, -10)}, both({'upperarm_l': D(0.45, 0.2, -0.87), 'lowerarm_l': D(0.4, -0.6, -0.7)}), both(FIST))
E['emote_dance_shuffle'] = dict(base='loco_idle', dur=4.0, loop=True, keys=dance([d5a, d5b, d5a, d5b]))

# --- Unity request 2026-10-05: exact ids for the contract ---------------------------------------------
E['act_host_mic'] = dict(base='loco_idle', dur=2.4, loop=True, keys=[(0.0, mic), (0.6, merge(mic, {'Head': R(-4, 0, 6)})), (1.2, mic_b),
                                                                       (1.8, merge(mic, {'Head': R(-2, 0, -6)})), (2.4, mic)])
ln_a = merge({'Head': R(14, 0, 2)}, {'spine_03': R(2, 0, 0)})
ln_b = merge({'Head': R(-3, 0, -2)}, {'spine_03': R(-1, 0, 0)})
E['act_listen_nod'] = dict(base='idle_fidget_foldarms', dur=3.0, loop=True,
                           keys=[(0.0, ln_b), (0.4, ln_a), (0.8, ln_b), (1.2, ln_a), (1.6, ln_b), (2.2, merge(ln_b, {'Head': R(-3, 0, 8)})), (3.0, ln_b)])
mj_up = merge(air, r(merge({'upperarm_l': D(0.35, -0.1, 0.93), 'lowerarm_l': D(0.2, -0.05, 0.98), 'hand_l': D(0.15, 0, 0.99)}, FIST)))
E['emote_moon_jump'] = dict(base='loco_idle', dur=2.0, keys=[(0.3, crouch), (0.55, air), (0.8, mj_up), (1.0, merge(mj_up, {'pelvis': P(0, 0, 0.3)})),
                                                             (1.25, crouch), (1.55, merge(crouch, {'pelvis': P(0, 0.02, -0.08)}))])
sad = merge(both({'clavicle_l': R(0, -8, 0), 'upperarm_l': D(0.1, 0.05, -1), 'lowerarm_l': D(0.05, -0.05, -1), 'hand_l': D(0.03, 0, -1)}),
            {'spine_01': R(6, 0, 0), 'spine_02': R(8, 0, 0), 'spine_03': R(10, 0, 0), 'neck_01': R(10, 0, 0), 'Head': R(22, 0, 0),
             'pelvis': P(0, 0.01, -0.03)})
sad_sigh = merge(sad, {'spine_03': R(4, 0, 0), 'Head': R(14, 0, 0), 'pelvis': P(0, 0.01, -0.01)})
E['react_sad'] = dict(base='loco_idle', dur=2.6, keys=[(0.5, sad), (1.1, sad_sigh), (1.6, merge(sad, {'Head': R(26, 0, 6)})), (2.1, sad)])

E = {k: v for k, v in E.items() if v}

# ------------------------------------------------------------------ build
bpy.ops.wm.open_mainfile(filepath=os.path.join(C.WORK, 'anims_male.blend'))
rig = Rig(bpy.data.objects['CTL_Rig_male'])
bpy.context.scene.render.fps = FPS
index_path = os.path.join(OUT, 'clips.json')
index = json.load(open(index_path)) if os.path.exists(index_path) else {}
cache = {}


def base_mats(base, f):
    act = bpy.data.actions[base]
    n = int(act.frame_range[1] - act.frame_range[0])
    bf = int(act.frame_range[0]) + (f % max(1, n))
    key = (base, bf)
    if key not in cache:
        cache[key] = rig.sample(act, bf)
    return cache[key]


for cid, spec in E.items():
    if ONLY and cid not in ONLY:
        continue
    n = int(round(spec['dur'] * FPS)) + 1
    loop = spec.get('loop', False)
    rin = 0.0 if loop else spec.get('ramp_in', 0.25)
    rout = 0.0 if loop else spec.get('ramp_out', 0.3)
    bones = set()
    for _, p in spec['keys']:
        bones.update(p)
    frames = []
    for f in range(n):
        t = f / FPS
        w = 1.0
        if rin > 0 and t < rin:
            w = t / rin
        if rout > 0 and t > spec['dur'] - rout:
            w = min(w, (spec['dur'] - t) / rout)
        w = w * w * (3 - 2 * w)
        specs = {b: spec_at(spec['keys'], b, t) for b in bones}
        mats = solve_pose(rig, base_mats(spec['base'], f), specs, w)
        lift = floor_lift(rig, mats)
        if lift > 0:
            for b in mats:
                if b != 'root':
                    mats[b] = Matrix.Translation((0, 0, lift)) @ mats[b]
        frames.append(rig.to_basis(mats))
    act = build_action(rig, cid, frames, loop, 'CT Life hand-keyed (procedural), base ' + spec['base'])
    path = os.path.join(OUT, f'{cid}.fbx')
    export_clip(rig.obj, act, path)
    index[cid] = {'fbx': f'{cid}.fbx', 'frames': n, 'fps': FPS, 'seconds': round((n - 1) / FPS, 2), 'loop': loop,
                  'source': 'CT Life hand-keyed in Blender by script (CC0), layered on ' + spec['base'],
                  'skeleton': 'CTL (male rig; Humanoid-retargets to female)', 'root_motion': False}
    print('[anim_emotes]', cid, n, 'frames', flush=True)
json.dump(index, open(index_path, 'w'), indent=1)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(C.WORK, 'anims_male.blend'))
