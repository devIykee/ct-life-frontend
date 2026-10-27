#!/usr/bin/env python3
"""Build the locomotion v3 clip set (real mocap) for the CT Life characters.

  python3 tools/anim/loco_v3/build_loco_v3.py prep                 # CMU ASF/AMC -> 120 Hz BVH (full takes)
  python3 tools/anim/loco_v3/build_loco_v3.py build [--targets snow,rain,hazmat] [--only id,id]
  python3 tools/anim/loco_v3/build_loco_v3.py manifest             # art/processed/animations/mocap_v3.json

Sources (licences in art/LICENSES.md):
  * CMU Graphics Lab Motion Capture Database (art/raw/animations/cmu_mocap/<subject>/, ASF/AMC 120 Hz)
  * 100STYLE (Mason, Starke, Komura 2022; CC BY 4.0; art/raw/animations/100style/100STYLE/<Style>/<Style>_<XX>.bvh,
    Xsens, 60 Hz, centimetres)
Outputs: client/Assets/Art/animations/mocap_v3/<target>/<clip>.fbx (target rig, 30 fps, armature + 1-tri bind proxy)
         art/processed/animations/mocap_v3/<target>_report.json
"""
import argparse
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
sys.path.insert(0, os.path.join(REPO, 'tools', 'anim'))
RAW = os.path.join(REPO, 'art', 'raw', 'animations')
BVH = os.path.join(RAW, 'loco_v3_bvh')
S100 = os.path.join(RAW, '100style', '100STYLE')
OUT = os.environ.get('LOCO_V3_OUT') or os.path.join(REPO, 'client', 'Assets', 'Art', 'animations', 'mocap_v3')
PROC = os.environ.get('LOCO_V3_PROC') or os.path.join(REPO, 'art', 'processed', 'animations', 'mocap_v3')
BLENDER = os.environ.get('BLENDER', '/Applications/Blender.app/Contents/MacOS/Blender')
ART = os.path.join(REPO, 'client', 'Assets', 'Art')

TARGETS = {
    'snow': {'fbx': os.path.join(ART, 'characters/authored/Snow/SnowAdult_Creator.fbx'), 'kind': 'h55'},
    'rain': {'fbx': os.path.join(ART, 'characters/authored/Rain/RainAdult_Creator.fbx'), 'kind': 'h55'},
    'hazmat': {'fbx': os.path.join(ART, 'characters/distinct/hero_hazmat/hero_hazmat_Hero.fbx'), 'kind': 'cts'},
}

CMU_TAKES = ['137_28', '137_41', '16_22', '16_32', '16_34', '16_57', '09_07', '143_01', '143_03', '69_13', '69_17', '69_18']


def cmu(take):
    return os.path.join(BVH, 'cmu_%s.bvh' % take)


def s100(style, kind):
    return os.path.join(S100, style, '%s_%s.bvh' % (style, kind))


# id -> spec.  replaces = the old clip / state it stands in for.  window in seconds of the source take.
# knee_offset_deg: CMU (ASF) knees never straighten (see retarget_v3_blender.py); Xsens (100STYLE) knees do.
# hand_follow: share of the hand orientation taken from the forearm (CMU wrists are short, noisy segments).
S100_ID_CUT = {'Neutral': (655, 1555), 'OnPhoneRight': (606, 1622), 'HandsInPockets': (525, 1669)}  # Frame_Cuts.csv (60 Hz)
CMU_DEF = dict(src='cmu', hand_follow=0.75)
S100_DEF = dict(src='s100', unit=0.01, hand_follow=0.3, knee_offset_deg=0.0, search_dt=1.0 / 30.0)


def c(**k):
    base = dict(S100_DEF if k.get('src') == 's100' else CMU_DEF)
    base.update(k)
    return base


CLIPS = [
    # ---- idles (in place, seamless loops found by pose+velocity matching, residual spread over the loop)
    c(id='loco_idle', bvh=s100('Neutral', 'ID'), src='s100', mode='loop', window=[11.0, 18.6], period=5.5,
      period_tol=0.35, replaces='loco_idle (UAL1 Idle_Loop; Rain: RainAdult_RelaxedIdle)',
      note='100STYLE Neutral_ID: relaxed standing, arms hanging naturally, breathing / small weight shifts'),
    c(id='idle_hands_hips', bvh=cmu('137_28'), mode='loop', window=[18.2, 23.0], period=4.0, period_tol=0.2,
      search_dt=1.0 / 60.0, knee_offset_deg=10, arm_fix=False, replaces='(new) idle variation (idle_fidget_* are UAL keyframe)',
      note='CMU 137_28 "Normal Wait": hands on hips, looks around over the left shoulder, weight shifts'),
    c(id='idle_heavy', bvh=cmu('137_41'), mode='loop', window=[19.2, 23.8], period=4.0, period_tol=0.2,
      search_dt=1.0 / 60.0, knee_offset_deg=10, replaces='(new) idle for bulky looks',
      note='CMU 137_41 "Strong Man Wait": wide arms, slow weight shift'),
    c(id='idle_phone_call', bvh=s100('OnPhoneRight', 'ID'), src='s100', mode='loop', window=[10.2, 26.9], period=6.0,
      period_tol=0.35, replaces='(new) phone idle (act_phone_call is UAL keyframe)',
      note='100STYLE OnPhoneRight_ID: phone held to the right ear (a call, not a screen check)'),
    # ---- locomotion loops (in place, start at the LEFT foot plant: walk / jog / sprint blend in phase)
    # step_widen_m / arm_swing_gain (motion pass 2026-10-26): wider feet (a measured adult step width is ~18 cm; the
    # takes land 11-15 cm apart, which reads as knees closing on these bodies) and a fuller arm swing
    c(id='loco_walk', bvh=cmu('16_22'), mode='loop', window=[0.1, 2.5], period=0.96, knee_offset_deg=12,
      step_widen_m=0.05, arm_swing_gain=1.25, arm_max_deg=16,
      replaces='loco_walk (UAL1 Walk_Loop; Snow: SnowWalkContact.anim)', note='CMU 16_22 "walk"'),
    c(id='loco_jog', bvh=cmu('09_07'), mode='loop', window=[0.05, 1.1], period=0.75, knee_offset_deg=8,
      step_widen_m=0.06, arm_swing_gain=1.2, arm_max_deg=16,
      replaces='loco_jog (UAL1 Jog_Fwd_Loop = the run band)', note='CMU 09_07 "run"'),
    c(id='loco_sprint', bvh=cmu('143_01'), mode='loop', window=[0.0, 0.82], period=0.62, knee_offset_deg=6,
      step_widen_m=0.065, arm_swing_gain=1.0,
      replaces='loco_run (UAL1 Sprint_Loop)', note='CMU 143_01 "Run"'),
    # ---- starts / stops (translation kept in the hips; start at the origin facing -Y)
    c(id='loco_walk_start', bvh=cmu('16_32'), mode='oneshot', window=[0.70, 2.60], knee_offset_deg=12,
      replaces='(new) idle -> walk', note='CMU 16_32 "walk": standing start, ends on the left plant (= walk phase 0)'),
    c(id='loco_walk_stop', bvh=cmu('16_34'), mode='oneshot', window=[0.52, 2.86], knee_offset_deg=12,
      replaces='(new) walk -> idle', note='CMU 16_34 "slow walk, stop": starts on the left plant (= walk phase 0)'),
    c(id='loco_run_start', bvh=cmu('143_03'), mode='oneshot', window=[0.62, 1.62], knee_offset_deg=8,
      replaces='(new) idle -> jog / sprint', note='CMU 143_03 "Start to Run": ends at ~4.3 m/s on the left plant'),
    c(id='loco_jog_stop', bvh=cmu('16_57'), mode='oneshot', window=[0.09, 2.15], knee_offset_deg=8,
      replaces='loco_run_stop (CMU 104_09 JogStop)', note='CMU 16_57 "run/jog, sudden stop"'),
    # ---- turns (yaw carried by the hips: Unity root-motion rotation)
    c(id='turn_90_l', bvh=cmu('69_17'), mode='turn', window=[0.70, 2.30], face_frames=4, knee_offset_deg=10,
      replaces='(new)', note='CMU 69_17 "turn in place"'),
    c(id='turn_90_r', bvh=cmu('69_18'), mode='turn', window=[4.00, 5.35], face_frames=4, knee_offset_deg=10,
      replaces='(new)', note='CMU 69_18 "turn in place (opposite direction)"'),
    c(id='turn_180_walk', bvh=cmu('69_13'), mode='turn', window=[3.45, 5.55], face_frames=4, knee_offset_deg=10,
      replaces='(new) walking U-turn', note='CMU 69_13 "walk, turn in place": slow walk -> 180 deg pivot -> walk'),
]


def run_blender(script, *a):
    cmd = [BLENDER, '-b', '--factory-startup', '-P', os.path.join(HERE, script), '--'] + list(a)
    p = subprocess.run(cmd, capture_output=True, text=True)
    out = p.stdout + p.stderr
    if p.returncode != 0 or 'Traceback' in out:
        print(out[-4000:])
        raise SystemExit('blender failed: %s' % script)
    return out


def prep():
    import cmu_asf_amc as C
    os.makedirs(BVH, exist_ok=True)
    for t in CMU_TAKES:
        s = t.split('_')[0]
        amc = os.path.join(RAW, 'cmu_mocap', s, t + '.amc')
        out = cmu(t)
        if os.path.exists(out):
            continue
        r = C.convert(os.path.join(RAW, 'cmu_mocap', s, s + '.asf'), amc, out, step=1, check=False)
        print('bvh', t, r['frames_out'])


def build(targets, only, clips=None, out_root=OUT):
    os.makedirs(PROC, exist_ok=True)
    clips = clips or CLIPS
    for tname in targets:
        t = TARGETS[tname]
        specs = [dict(c) for c in clips if not only or c['id'] in only]
        for c in specs:
            c.setdefault('arm_clear', 0.004 if tname != 'hazmat' else 0.0)
        job = {'target': {'name': tname, 'fbx': t['fbx'], 'kind': t['kind']},
               'out_dir': os.path.join(out_root, tname), 'report': os.path.join(PROC, '%s_report.json' % tname),
               'clips': specs}
        jp = os.path.join(PROC, '_job_%s.json' % tname)
        json.dump(job, open(jp, 'w'), indent=1)
        out = run_blender('retarget_v3_blender.py', '--job', jp)
        os.remove(jp)
        print(tname, 'ok', out.count('[loco_v3] {'), 'clips')


LICENCES = {
    'cmu': {'source': 'CMU Graphics Lab Motion Capture Database, http://mocap.cs.cmu.edu',
            'licence': 'Free use. FAQ (http://mocap.cs.cmu.edu/faqs.php): "The motion capture data may be copied, '
                       'modified, or redistributed without permission." Requested acknowledgement: "The data used in '
                       'this project was obtained from mocap.cs.cmu.edu. The database was created with funding from '
                       'NSF EIA-0196217."'},
    's100': {'source': '100STYLE dataset, Ian Mason, Sebastian Starke, Taku Komura (2022), '
                       'https://www.ianxmason.com/100style/ , https://zenodo.org/records/8127870 (BVH zip; members '
                       'fetched by HTTP range from the authors\' Google Drive mirror of the same 100STYLE.zip)',
             'licence': 'CC BY 4.0: "This work is licensed under a Creative Commons Attribution 4.0 International '
                        'License." (ianxmason.com/100style, read 2026-10-22; Zenodo record licence field: Creative '
                        'Commons Attribution 4.0 International). Credit: "100STYLE dataset by Ian Mason, Sebastian '
                        'Starke and Taku Komura, CC BY 4.0; retargeted and edited by CT Life."'},
}


def manifest(targets):
    out = {'generated_by': 'tools/anim/loco_v3/build_loco_v3.py (retarget_v3_blender.py, Blender 5.1.2)',
           'date': '2026-10-22',
           'conventions': {
               'fps': 30, 'rig': 'each target\'s own skeleton (snow/rain: 55-bone Root/Hips/... h55 rig; hazmat: CTS '
                                 'mannequin names); armature + 1-tri _bind_proxy; rest (T/A pose) keyed one frame '
                                 'before the take so FBX node defaults = rest',
               'facing': 'character faces -Y in Blender = +Z in Unity (FBX -Z forward / Y up export)',
               'loops': 'in place (mean travel velocity removed; planted feet move backward at native_speed_mps); '
                        'last frame == first frame (Unity Loop Time, no Loop Pose needed); locomotion loops start at '
                        'the LEFT foot plant so walk/jog/sprint are phase-aligned for a 1D blend tree',
               'oneshots': 'translation kept in the hips (start at origin facing forward); turns keep yaw in the hips',
               'metres_per_cycle': 'body travel over one loop at 1x = native_speed_mps x duration_s; '
                                   'playback rate for a speed v = v / native_speed_mps'},
           'licences': LICENCES, 'clips': []}
    spec = {c['id']: c for c in CLIPS}
    reps = {t: {r['id']: r for r in json.load(open(os.path.join(PROC, '%s_report.json' % t)))} for t in targets}
    for c in CLIPS:
        e = {'id': c['id'], 'mode': c['mode'], 'replaces': c.get('replaces'), 'source': c['note'],
             'source_kind': c['src'], 'licence': LICENCES[c['src']]['licence'][:60] + '...',
             'source_file': os.path.relpath(c['bvh'], REPO), 'per_target': {}}
        for t in targets:
            r = reps[t].get(c['id'])
            if not r:
                continue
            k = {'fbx': os.path.relpath(r['fbx'], REPO), 'frames': r['frames'], 'duration_s': r['duration_s'],
                 'source_window_s': r['source_window_s'], 'source_frames': r['source_frames'],
                 'source_fps': round(r['source_fps'], 3), 'time_warp': r['time_warp']}
            if c['mode'] == 'loop':
                k['loop_range_frames'] = [0, r['frames'] - 1]
                k['metres_per_cycle'] = r.get('metres_per_cycle')
                k['native_speed_mps'] = r.get('native_speed_mps')
                k['stance_foot_backward_speed_mps'] = r.get('stance_foot_backward_speed_mps')
                k['closure_residual'] = r.get('closure_residual')
            else:
                k['hips_displacement_m_xy'] = r.get('hips_displacement_m')
                k['yaw_change_deg'] = r.get('yaw_change_deg')
                k['hips_speed_mps_per_frame'] = r.get('hips_speed_mps_per_frame')
            for key in ('arm_body_intersections', 'arm_abduction_added_deg_max', 'gaze_lift_deg',
                        'knee_offset_hips_raise_m', 'hips_raised_m', 'straightened', 'ik_goal_clamped_frames',
                        'scale_leg_ratio', 'phase_shift_frames'):
                if r.get(key) is not None:
                    k[key] = r[key]
            e['per_target'][t] = k
        out['clips'].append(e)
    path = os.path.join(REPO, 'art', 'processed', 'animations', 'mocap_v3.json')
    json.dump(out, open(path, 'w'), indent=1)
    print('manifest', path, len(out['clips']))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('cmd', choices=['prep', 'build', 'manifest'])
    ap.add_argument('--targets', default='snow,rain,hazmat')
    ap.add_argument('--only', default='')
    a = ap.parse_args()
    if a.cmd == 'prep':
        prep()
    elif a.cmd == 'build':
        build(a.targets.split(','), [x for x in a.only.split(',') if x])
    elif a.cmd == 'manifest':
        manifest(a.targets.split(','))


if __name__ == '__main__':
    main()
