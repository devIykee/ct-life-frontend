#!/usr/bin/env python3
"""Build the CMU mocap clips for CT Life: window selection evidence -> BVH -> FBX (Blender) -> re-import check +
contact sheet -> art/processed/animations/mocap/mocap.json.

  python3 tools/anim/build_mocap_clips.py                 # build every clip in CLIPS + per-subject rest FBX
  python3 tools/anim/build_mocap_clips.py --only trip     # one clip
  python3 tools/anim/build_mocap_clips.py --preview 91 91_56 1 500 --align start   # sheet of any window (scratch)

Sources: art/raw/animations/cmu_mocap/<subject>/ (copied from the main checkout; gitignored).
Windows are AMC frame numbers (120 Hz, inclusive). Output is every 4th source frame (30 fps).
"""
import argparse
import json
import math
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import cmu_asf_amc as C  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
RAW = os.path.join(REPO, 'art', 'raw', 'animations', 'cmu_mocap')
OUT = os.path.join(REPO, 'art', 'processed', 'animations', 'mocap')
EVIDENCE = '/Users/mac/Documents/codes/projects/ct-life-evidence/character-feel/round2/mocap'
BLENDER = os.environ.get('BLENDER', '/Applications/Blender.app/Contents/MacOS/Blender')
STEP = 4
FPS = C.SRC_FPS / STEP
SIMPLIFY = 0.05  # FBX bake_anim_simplify_factor: drops constant curves; measured 0.001 mm max re-import error

LICENCE = {
    'source': 'CMU Graphics Lab Motion Capture Database, http://mocap.cs.cmu.edu',
    'licence': 'The motion capture data may be copied, modified, or redistributed without permission. '
               '(mocap.cs.cmu.edu/faqs.php, read 2026-10-08; copy in art/raw/animations/cmu_mocap/'
               'LICENSE_CMU_faq_2026-10-08.txt)',
    'acknowledgement': 'The data used in this project was obtained from mocap.cs.cmu.edu. '
                       'The database was created with funding from NSF EIA-0196217.',
}


def take_paths(subject, take):
    return os.path.join(RAW, subject, subject + '.asf'), os.path.join(RAW, subject, take + '.amc')


_cache = {}


def load(subject, take):
    key = (subject, take)
    if key not in _cache:
        asf, amc = take_paths(subject, take)
        sk = C.Skeleton.load(asf)
        frames = C.load_amc(amc)
        _cache[key] = (sk, frames, C.metrics(sk, frames))
    return _cache[key]


def at(m, frame):
    return m[frame - m[0]['frame']]


def rng(m, a, b):
    return m[a - m[0]['frame']:b - m[0]['frame'] + 1]


def r2(x, n=3):
    return round(float(x), n)


# --------------------------------------------------------------------------------------------- evidence helpers
def summary(m, a, b):
    s, e = at(m, a), at(m, b)
    seg = rng(m, a, b)
    return {
        'net_root_translation_m': r2(math.hypot(e['root_x'] - s['root_x'], e['root_z'] - s['root_z'])),
        'net_root_translation_xz_m': [r2(e['root_x'] - s['root_x']), r2(e['root_z'] - s['root_z'])],
        'yaw_change_deg': r2(e['heading'] - s['heading'], 1),
        'hips_height_start_m': r2(s['root_y']), 'hips_height_end_m': r2(e['root_y']),
        'hips_height_min_m': r2(min(r['root_y'] for r in seg)),
        'speed_start_mps': r2(s['speed_h'], 2), 'speed_end_mps': r2(e['speed_h'], 2),
        'speed_max_mps': r2(max(r['speed_h'] for r in seg), 2),
        'torso_pitch_start_deg': r2(s['torso_pitch'], 1), 'torso_pitch_end_deg': r2(e['torso_pitch'], 1),
        'torso_pitch_max_deg': r2(max(r['torso_pitch'] for r in seg), 1),
        'chest_forward_y_start': r2(s['chest_fwd_y'], 2), 'chest_forward_y_end': r2(e['chest_fwd_y'], 2),
        'feet_ankle_height_max_m': r2(max(max(r['lankle_y'], r['rankle_y']) for r in seg)),
    }


def first(m, a, b, pred):
    for r in rng(m, a, b):
        if pred(r):
            return r['frame']
    return None


def last(m, a, b, pred):
    for r in reversed(rng(m, a, b)):
        if pred(r):
            return r['frame']
    return None


def heel_strikes(m, a, b, side):
    """frames where the ankle speed drops below 0.35 m/s after being above 1.0 m/s (foot plants)."""
    key = 'lfoot_spd' if side == 'l' else 'rfoot_spd'
    out, swinging = [], False
    for r in rng(m, a, b):
        if r[key] > 1.0:
            swinging = True
        elif swinging and r[key] < 0.35:
            out.append(r['frame'])
            swinging = False
    return out


def still_from(m, a, b, speed=0.1, foot=0.15, hold=60):
    """first frame from which root speed < speed and both ankle speeds < foot for >= hold frames."""
    run = 0
    for r in rng(m, a, b):
        if r['speed_h'] < speed and r['lfoot_spd'] < foot and r['rfoot_spd'] < foot:
            run += 1
            if run >= hold:
                return r['frame'] - hold + 1
        else:
            run = 0
    return None


def trace(m, a, b, every=12):
    """compact per-0.1 s trace over the window (source frames)."""
    seg = rng(m, a, b)[::every]
    return {'frame': [r['frame'] for r in seg],
            'hips_y': [r2(r['root_y'], 2) for r in seg],
            'speed': [r2(r['speed_h'], 2) for r in seg],
            'yaw': [r2(r['heading'] - seg[0]['heading'], 0) for r in seg],
            'torso_pitch': [r2(r['torso_pitch'], 0) for r in seg],
            'chest_fwd_y': [r2(r['chest_fwd_y'], 2) for r in seg],
            'min_ankle_y': [r2(min(r['lankle_y'], r['rankle_y']), 2) for r in seg]}


def onset_limbs(m, a, b, hi=0.15, lo=0.1, hold=30):
    rows = rng(m, a, b)
    for i, r in enumerate(rows):
        if r['limb_spd'] > hi and all(x['limb_spd'] > lo for x in rows[i:i + hold]):
            return r['frame']
    return None


def sel_getup(m, face):
    f0, f1 = m[0]['frame'], m[-1]['frame']
    onset = onset_limbs(m, f0, f1)
    start = max(f0, onset - 30)                       # keep 0.25 s of lying still before the push
    peak = max(rng(m, onset, f1), key=lambda r: r['head_y'])['frame']  # upright peak (then the subject drops
    end = peak                                        # into a knees-bent ready stance, see evidence)
    s0, e0 = at(m, start), at(m, end)
    ok = (s0['chest_fwd_y'] < -0.8) if face == 'down' else (s0['chest_fwd_y'] > 0.8)
    after = rng(m, end, f1)
    ev = {
        'rule': 'start = motion onset (mean limb speed rel. to pelvis > 0.15 m/s, sustained > 0.1 for 0.25 s) '
                '- 0.25 s; end = frame of maximum head height after onset (most upright point).',
        'onset_frame': onset, 'onset_limb_speed_mps': r2(at(m, onset)['limb_spd'], 2),
        'start_lying': {'hips_height_m': r2(s0['root_y']), 'head_height_m': r2(s0['head_y']),
                        'chest_forward_y': r2(s0['chest_fwd_y'], 2),
                        'meaning': 'chest_forward_y = world-up component of the thorax forward axis: '
                                   '-1 = chest facing the floor (prone), +1 = facing the sky (supine)'},
        'face_check_passed': ok,
        'end_upright': {'head_height_m': r2(e0['head_y']), 'hips_height_m': r2(e0['root_y']),
                        'torso_pitch_deg': r2(e0['torso_pitch'], 1), 'root_speed_mps': r2(e0['speed_h'], 2)},
        'after_window_tail': {'frames': [end, f1], 'torso_pitch_deg_range': [r2(min(r['torso_pitch'] for r in after), 1),
                                                                             r2(max(r['torso_pitch'] for r in after), 1)],
                              'hips_height_m_last': r2(after[-1]['root_y']),
                              'note': 'take continues into a knees-bent, arms-forward ready stance; not included'},
        'rise_duration_s': r2((end - onset) / C.SRC_FPS, 2),
        'root_path_length_m': r2(sum(math.hypot(b['root_x'] - a['root_x'], b['root_z'] - a['root_z'])
                                     for a, b in zip(rng(m, start, end)[:-1], rng(m, start, end)[1:]))),
    }
    return start, end, ev


def sel_trip(m):
    f0, f1 = m[0]['frame'], m[-1]['frame']
    p0 = sorted(r['torso_pitch'] for r in rng(m, f0, f0 + 24))[12]
    onset = first(m, f0, f1, lambda r: r['torso_pitch'] > p0 + 10)
    start = max(f0, onset - 36)                       # 0.3 s lead-in
    peak = max(rng(m, onset, onset + 150), key=lambda r: r['torso_pitch'])
    s0 = at(m, start)
    end = first(m, peak['frame'], f1, lambda r: r['torso_pitch'] <= peak['torso_pitch'] - 20
                and r['root_y'] >= s0['root_y'] and abs(r['heading'] - s0['heading']) <= 20)
    upright = first(m, peak['frame'], f1, lambda r: r['torso_pitch'] < 30)
    walk_before = rng(m, start, onset)
    ev = {
        'rule': 'onset = first frame torso pitch > standing pitch + 10 deg; start = onset - 0.3 s; end = first frame '
                'after the pitch peak with pitch <= peak - 20 deg, hips back to start height and |yaw| <= 20 deg.',
        'standing_torso_pitch_deg': r2(p0, 1), 'onset_frame': onset,
        'peak_pitch': {'frame': peak['frame'], 'torso_pitch_deg': r2(peak['torso_pitch'], 1),
                       'speed_mps': r2(peak['speed_h'], 2), 'chest_forward_y': r2(peak['chest_fwd_y'], 2)},
        'lead_in_speed_mps_range': [r2(min(r['speed_h'] for r in walk_before), 2),
                                    r2(max(r['speed_h'] for r in walk_before), 2)],
        'end_state': {'torso_pitch_deg': r2(at(m, end)['torso_pitch'], 1), 'speed_mps': r2(at(m, end)['speed_h'], 2),
                      'hips_height_m': r2(at(m, end)['root_y'])},
        'rejected_longer_end': {'frame': upright, 'torso_pitch_deg': r2(at(m, upright)['torso_pitch'], 1),
                                'yaw_change_deg': r2(at(m, upright)['heading'] - s0['heading'], 1),
                                'why': 'first frame with torso pitch < 30 deg comes only after the subject has spun '
                                       'round; unusable as a trip-and-continue'},
        'caveat': 'NOT a trip during steady walking: the take starts standing (speed %.2f m/s at frame %d), takes one '
                  'step and lurches forward into a near-horizontal stagger (acted "StumbleWalk"). There is no steady '
                  'walk before it and no return to an upright walk inside 2.5 s.' % (at(m, f0)['speed_h'], f0),
    }
    return start, end, ev


def sel_jog_stop(m):
    f0, f1 = m[0]['frame'], m[-1]['frame']
    jog = sorted(r['speed_h'] for r in rng(m, f0, f0 + 90))[45]
    decel = None
    for r in rng(m, f0, f1):
        if r['speed_h'] < 0.8 * jog and all(x['speed_h'] < 0.9 * jog for x in rng(m, r['frame'], f1)):
            decel = r['frame']
            break
    strikes = sorted(heel_strikes(m, f0, f1, 'l') + heel_strikes(m, f0, f1, 'r'))
    before = [f for f in strikes if f < decel]
    start = before[-2] if len(before) >= 2 else f0   # two jog steps before the deceleration
    still = still_from(m, decel, f1, speed=0.15, foot=0.2, hold=24)
    end = f1
    ev = {
        'rule': 'jog speed = median of first 0.75 s; decel = first frame below 0.8x jog speed that never recovers '
                'above 0.9x; start = 2nd foot plant before decel; end = last frame of the take.',
        'jog_speed_mps': r2(jog, 2), 'decel_frame': decel, 'foot_plants': strikes,
        'still_from_frame': still, 'standing_tail_s': r2((f1 - still + 1) / C.SRC_FPS, 2) if still else 0,
        'end_speed_mps': r2(at(m, end)['speed_h'], 2),
        'caveat': 'Take ends %.2f s after the subject is still; the requested ~0.5 s standing tail is not in the '
                  'source.' % ((f1 - still + 1) / C.SRC_FPS if still else 0),
    }
    return start, end, ev


def sel_hop_turn(m):
    f0, f1 = m[0]['frame'], m[-1]['frame']
    y0 = sorted(r['root_y'] for r in rng(m, f0, f0 + 120))[60]
    onset = first(m, f0, f1, lambda r: r['root_y'] < y0 - 0.03 or abs(r['yaw_rate']) > 60)
    start = max(f0, onset - 30)
    air = [r for r in rng(m, onset, f1) if min(r['lankle_y'], r['rankle_y']) > 0.15]
    land = air[-1]['frame']
    settle = None
    rows = rng(m, land, f1)
    for i, r in enumerate(rows):
        win = rows[i:i + 30]
        if len(win) == 30 and all(x['speed_h'] < 0.12 and abs(x['yaw_rate']) < 25 and x['limb_spd'] < 0.2
                                  for x in win):
            settle = r['frame']
            break
    end = min(f1, settle + 30) if settle else f1
    s0, e0 = at(m, start), at(m, end)
    ev = {
        'rule': 'onset = first frame hips drop 3 cm below standing or |yaw rate| > 60 deg/s; start = onset - 0.25 s; '
                'settle = first frame after landing with speed < 0.12 m/s, |yaw rate| < 25 deg/s, limb speed '
                '< 0.2 m/s for 0.25 s; end = settle + 0.25 s.',
        'standing_hips_m': r2(y0), 'onset_frame': onset, 'crouch_min_hips_m': r2(min(r['root_y'] for r in rng(m, onset, land))),
        'airborne_frames': [air[0]['frame'], land], 'airborne_s': r2(len(air) / C.SRC_FPS, 2),
        'apex_hips_m': r2(max(r['root_y'] for r in air)), 'max_yaw_rate_dps': r2(max(abs(r['yaw_rate']) for r in air), 0),
        'settle_frame': settle,
        'caveat': 'This is a HOP turn: both feet leave the ground (ankles > 0.15 m) and the body spins in the air; '
                  'it is not a stepping pivot.',
    }
    return start, end, ev


CLIPS = [
    {'id': 'getup_face_down', 'subject': '140', 'take': '140_01', 'alt_take': '140_02', 'align': 'end',
     'cmu_title': 'Get Up Face Down', 'select': lambda m: sel_getup(m, 'down'), 'ortho': 2.8},
    {'id': 'getup_face_up', 'subject': '140', 'take': '140_08', 'align': 'end',
     'cmu_title': 'Get Up From Ground Laying on Back', 'select': lambda m: sel_getup(m, 'up'), 'ortho': 2.8},
    {'id': 'trip', 'subject': '104', 'take': '104_13', 'align': 'start', 'cmu_title': 'StumbleWalk',
     'select': sel_trip},
    {'id': 'jog_stop', 'subject': '104', 'take': '104_09', 'align': 'start', 'cmu_title': 'JogStop',
     'select': sel_jog_stop},
    {'id': 'turn_180', 'subject': '91', 'take': '91_54', 'align': 'start', 'cmu_title': '180',
     'select': sel_hop_turn},
]


# --------------------------------------------------------------------------------------------- pipeline
def blender(script, *argv):
    cmd = [BLENDER, '-b', '--factory-startup', '-noaudio', '--python', os.path.join(HERE, script), '--'] + list(argv)
    p = subprocess.run(cmd, capture_output=True, text=True)
    out = p.stdout + p.stderr
    if p.returncode != 0 or 'Traceback' in out:
        sys.stderr.write(out[-4000:])
        raise SystemExit('blender failed: %s' % script)
    return out


def standing_ankle_height(subject, takes):
    """median lower-ankle height over still, upright frames of the subject's takes (robust floor reference)."""
    vals = []
    for t in takes:
        _, _, m = load(subject, t)
        for r in m:
            if r['speed_h'] < 0.05 and r['lfoot_spd'] < 0.1 and r['rfoot_spd'] < 0.1 and r['torso_pitch'] < 15:
                vals.append(min(r['lankle_y'], r['rankle_y']))
    vals.sort()
    return vals[len(vals) // 2] if vals else 0.0


def write_rest(subject, takes, out_dir):
    asf = os.path.join(RAW, subject, subject + '.asf')
    sk = C.Skeleton.load(asf)
    ank = standing_ankle_height(subject, takes)
    root_y = C.rest_root_height(sk, ank)
    path = os.path.join(out_dir, 'cmu_%s_rest.bvh' % subject)
    res = C.convert_rest(asf, path, root_y)
    res.update({'standing_ankle_height_m': r2(ank), 'rest_root_height_m': r2(root_y)})
    return path, res


def make_ref(bvh, rest_bvh, labels, title, facing_blender, ortho=2.6, cam_z=0.9):
    names, frames = C.bvh_positions(bvh)
    _, rest = C.bvh_positions(rest_bvh)
    joints, _, _ = C.read_bvh(bvh)
    parents = {j['name']: (joints[j['parent']]['name'] if j['parent'] is not None else None) for j in joints}
    end_len = {j['name']: C.vlen(j['end']) for j in joints if j['end'] is not None}
    fd, path = tempfile.mkstemp(suffix='_ref.json')
    with os.fdopen(fd, 'w') as f:
        json.dump({'names': names, 'frames': frames, 'rest': rest[0], 'parents': parents, 'end_len': end_len,
                   'labels': labels, 'title': title, 'fps': FPS, 'facing_blender': facing_blender,
                   'ortho_scale': ortho, 'cam_z': cam_z}, f)
    return path


def facing_for(sk, frames, idx, align):
    if align != 'none':
        return [0.0, -1.0, 0.0]  # aligned clips face BVH +Z (= Blender -Y) at the alignment frame
    h = math.radians(C.heading_deg(C.fk_asf(sk, frames[idx[0]][1])))
    return [math.sin(h), -math.cos(h), 0.0]


def build_window(subject, take, start, end, align, bvh_out, fbx_out, png_out, title, rest_bvh, take_name,
                 cells=8, views='side,34', ortho=2.6):
    asf, amc = take_paths(subject, take)
    res = C.convert(asf, amc, bvh_out, start, end, STEP, align)
    if res['selfcheck_max_err_m'] >= 0.001:
        raise SystemExit('BVH self-check failed for %s: %r' % (take_name, res))
    sk, frames, _ = load(subject, take)
    idx = C.select_frames(frames, start, end, STEP)
    labels = ['src %d  %.2fs' % (frames[i][0], k / FPS) for k, i in enumerate(idx)]
    out = blender('bvh_to_fbx_blender.py', '--bvh', bvh_out, '--out', fbx_out, '--take', take_name,
                  '--rest-bvh', rest_bvh, '--armature', 'cmu_' + subject, '--simplify', str(SIMPLIFY))
    assert 'FBX_OK' in out, out[-2000:]
    ref = make_ref(bvh_out, rest_bvh, labels, title, facing_for(sk, frames, idx, align), ortho)
    fd, rep = tempfile.mkstemp(suffix='_report.json')
    os.close(fd)
    blender('fbx_check_render_blender.py', '--fbx', fbx_out, '--ref', ref, '--out', png_out, '--report', rep,
            '--cells', str(cells), '--views', views)
    report = json.load(open(rep))
    os.remove(ref)
    os.remove(rep)
    return res, report


def preview(subject, take, start, end, align, cells):
    sc = tempfile.mkdtemp(prefix='mocap_preview_')
    _, _, m = load(subject, take)
    start = start or m[0]['frame']
    end = end or m[-1]['frame']
    rest, _ = write_rest(subject, [take], sc)
    name = '%s_%d_%d' % (take, start, end)
    png = os.path.join(sc, name + '.png')
    res, rep = build_window(subject, take, start, end, align, os.path.join(sc, name + '.bvh'),
                            os.path.join(sc, name + '.fbx'), png, 'preview %s %d-%d align=%s' % (take, start, end, align),
                            rest, name, cells=cells)
    print(json.dumps({'bvh': res, 'fbx_check': rep}, indent=1))
    print('PREVIEW', png)


def scan(raw_dir):
    """Search every take under raw_dir for (a) grounded stepping turns of ~90/180 deg and (b) walk -> stand stops."""
    import glob
    out = {'turns': [], 'walk_stops': []}
    for asf in sorted(glob.glob(os.path.join(raw_dir, '*', '*.asf'))):
        sk = C.Skeleton.load(asf)
        for amc in sorted(glob.glob(os.path.join(os.path.dirname(asf), '*.amc'))):
            m = C.metrics(sk, C.load_amc(amc))
            n = len(m)
            stand_y = sorted(r['root_y'] for r in m)[int(0.9 * n)]
            best = {}
            for i in range(0, n, 6):
                if m[i]['speed_h'] > 0.25:
                    continue
                for L in range(96, 481, 6):
                    j = i + L
                    if j >= n:
                        break
                    if m[j]['speed_h'] > 0.25:
                        continue
                    seg = m[i:j + 1]
                    if max(min(r['lankle_y'], r['rankle_y']) for r in seg) > 0.14:
                        continue  # both feet off the ground = hop
                    if max(r['root_y'] for r in seg) > stand_y + 0.05 or max(r['torso_pitch'] for r in seg) > 35:
                        continue
                    dh = m[j]['heading'] - m[i]['heading']
                    net = math.hypot(m[j]['root_x'] - m[i]['root_x'], m[j]['root_z'] - m[i]['root_z'])
                    for tgt in (90, 180):
                        if abs(abs(dh) - tgt) < 20 and net < 0.8:
                            sc = abs(abs(dh) - tgt) + 40 * net + L / 120.0
                            if tgt not in best or sc < best[tgt][0]:
                                best[tgt] = (sc, {'take': os.path.basename(amc)[:-4], 'target_deg': tgt,
                                                  'frames': [m[i]['frame'], m[j]['frame']], 'yaw_deg': r2(dh, 1),
                                                  'net_m': r2(net), 'dur_s': r2(L / 120, 2)})
            out['turns'] += [v[1] for v in best.values()]
            i = 0
            while i < n:
                if m[i]['speed_h'] < 0.1 and m[i]['lfoot_spd'] < 0.2 and m[i]['rfoot_spd'] < 0.2:
                    j = i
                    while j < n and m[j]['speed_h'] < 0.1:
                        j += 1
                    walk = [r for r in m[max(0, i - 300):i] if 0.8 <= r['speed_h'] <= 1.8 and r['torso_pitch'] < 20
                            and min(r['lankle_y'], r['rankle_y']) < 0.14]
                    if j - i >= 48 and len(walk) >= 60:
                        out['walk_stops'].append({'take': os.path.basename(amc)[:-4], 'still_from': m[i]['frame'],
                                                  'still_frames': j - i, 'walk_frames_before': len(walk)})
                    i = j + 1
                else:
                    i += 1
    return out


SCAN_RECORD = {  # result of `--scan` over the main checkout's art/raw/animations/cmu_mocap (all 9 subjects), 2026-10-08
    'subjects_scanned': ['13', '77', '90', '91', '104', '139', '140', '143'],
    'turn_rule': 'window 0.8-4 s, still (< 0.25 m/s) at both ends, never both ankles > 0.14 m (no hop), hips never '
                 '> standing + 5 cm, torso pitch <= 35 deg, |yaw| within 20 deg of 90/180, net translation < 0.8 m',
    'turns_found': [
        {'take': '13_26', 'target_deg': 90, 'frames': [2599, 2887], 'yaw_deg': 89.5, 'net_m': 0.09, 'dur_s': 2.4},
        {'take': '13_27', 'target_deg': 90, 'frames': [1393, 1543], 'yaw_deg': -90.0, 'net_m': 0.13, 'dur_s': 1.25},
        {'take': '13_26', 'target_deg': 180, 'frames': [973, 1453], 'yaw_deg': -169.3, 'net_m': 0.111, 'dur_s': 4.0},
        {'take': '13_27', 'target_deg': 180, 'frames': [3535, 3883], 'yaw_deg': -180.7, 'net_m': 0.058, 'dur_s': 2.9}],
    'turns_verdict': 'all four are inside CMU 13_26/13_27 "direct traffic, wave, point": rendered 13_27 1381-1561 and '
                     '13_26 2587-2899 show arms raised in traffic gestures through the turn and a mid-turn pause; not '
                     'used as idle turns.',
    'walk_stop_rule': '>= 0.4 s still (< 0.1 m/s, ankles < 0.2 m/s) preceded within 2.5 s by >= 0.5 s of walking '
                      '(0.8-1.8 m/s, torso pitch < 20 deg, one foot down)',
    'walk_stops_found': [],
    'walk_stop_relaxed': 'with >= 0.15 s still and < 0.15 m/s the only hit is 104_09 (still from frame 308, the jog '
                         'stop)',
}


def not_produced():
    _, _, m56 = load('91', '91_56')
    s, e, ev = sel_hop_turn(m56)
    sm = summary(m56, s, e)
    ends = {}
    for t in ('104_13', '104_14'):
        _, _, m = load('104', t)
        go = first(m, m[0]['frame'], m[-1]['frame'], lambda r: r['speed_h'] > 0.5)
        moving = rng(m, go, m[-1]['frame'])
        ends[t] = {'last_frame': m[-1]['frame'], 'speed_last_mps': r2(m[-1]['speed_h'], 2),
                   'moving_from_frame': go,
                   'min_speed_after_moving_mps': r2(min(r['speed_h'] for r in moving), 2),
                   'torso_pitch_after_moving_deg': [r2(min(r['torso_pitch'] for r in moving), 1),
                                                    r2(max(r['torso_pitch'] for r in moving), 1)]}
    return {
        'turn_90': {
            'requested_source': '91_56 (CMU title "90")',
            'measured': {'window': [s, e], 'yaw_change_deg': sm['yaw_change_deg'],
                         'net_root_translation_m': sm['net_root_translation_m'], 'airborne_s': ev['airborne_s'],
                         'apex_hips_m': ev['apex_hips_m']},
            'why_not': 'Data and render agree: 91_56 is a crouch-jump that spins ~%.0f deg to the LEFT in the air and '
                       'lands facing %.0f deg to the right of the start. Shipping it as a 90 deg turn would show a '
                       '3/4 spin the wrong way. 91_58 ("270") is the same motion (+279 deg). No clean grounded '
                       '90 deg pivot exists in the downloaded takes (see scan).'
                       % (sm['yaw_change_deg'], 360 - sm['yaw_change_deg']),
            'scan': SCAN_RECORD,
        },
        'walk_stop': {
            'why_not': '104_14 "HappyStartWalk" starts standing, then never slows below the min speed below once '
                       'moving (acted lurching/skipping with a strong forward lean, see torso pitch range) and ends '
                       'moving; 104_13 likewise; the scan found no walk -> stand in any downloaded take. jog_stop '
                       '(104_09) was produced instead, as instructed.',
            'take_ends': ends,
        },
    }


def build(only=None):
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(EVIDENCE, exist_ok=True)
    subjects = {}
    for c in CLIPS:
        subjects.setdefault(c['subject'], []).extend([c['take']] + ([c['alt_take']] if c.get('alt_take') else []))
    rest = {}
    for sub, takes in subjects.items():
        path, res = write_rest(sub, takes, OUT)
        fbx = path[:-4] + '.fbx'
        name = 'cmu_%s_rest' % sub
        out = blender('bvh_to_fbx_blender.py', '--bvh', path, '--out', fbx, '--take', name,
                      '--armature', 'cmu_' + sub, '--simplify', str(SIMPLIFY))
        assert 'FBX_OK' in out
        ref = make_ref(path, path, ['ASF rest, root y %.2f m' % res['rest_root_height_m']], name, [0.0, -1.0, 0.0])
        png = os.path.join(EVIDENCE, name + '.png')
        fd, rep = tempfile.mkstemp(suffix='_report.json')
        os.close(fd)
        blender('fbx_check_render_blender.py', '--fbx', fbx, '--ref', ref, '--out', png, '--report', rep,
                '--cells', '1', '--views', 'front,side,34', '--cell-w', '380', '--cell-h', '420')
        r = json.load(open(rep))
        os.remove(ref)
        os.remove(rep)
        rest[sub] = {'bvh': rel(path), 'fbx': rel(fbx), 'fbx_bytes': r['fbx_bytes'], 'sheet': png,
                     'take': name, 'armature_object': 'cmu_' + sub, 'bones': r['bones'],
                     'bvh_selfcheck_max_err_mm': r2(res['selfcheck_max_err_m'] * 1000, 4),
                     'fbx_reimport_rest_err_mm': r2(r['rest_pose_max_err_m'] * 1000, 4),
                     'standing_ankle_height_m': res['standing_ankle_height_m'],
                     'rest_root_height_m': res['rest_root_height_m']}
        print('rest', sub, rest[sub])

    clips = []
    for c in CLIPS:
        if only and c['id'] not in only:
            continue
        cands = []
        for t in [c['take']] + ([c['alt_take']] if c.get('alt_take') else []):
            _, _, m = load(c['subject'], t)
            st, en, ev = c['select'](m)
            cands.append((t, st, en, ev, summary(m, st, en), m))
        choice_note = None
        if len(cands) > 1:
            def score(cd):  # prefer the take that ends upright and still (clean hand-off to idle)
                return cd[4]['torso_pitch_end_deg'] + 50 * cd[4]['speed_end_mps']
            cands.sort(key=score)
            choice_note = {'rule': 'pick the take whose end is most upright and still: '
                                   'score = end torso pitch (deg) + 50 x end speed (m/s), lower wins',
                           'scores': {cd[0]: r2(score(cd), 1) for cd in cands},
                           'candidates': {cd[0]: {'window': [cd[1], cd[2]], 'duration_s': r2((cd[2] - cd[1]) / 120, 2),
                                                  'evidence': cd[3], 'summary': cd[4]} for cd in cands}}
        take, st, en, ev, sm, m = cands[0]
        sub = c['subject']
        bvh = os.path.join(OUT, c['id'] + '.bvh')
        fbx = os.path.join(OUT, c['id'] + '.fbx')
        png = os.path.join(EVIDENCE, c['id'] + '.png')
        title = '%s  <-  CMU %s "%s" frames %d-%d (120 Hz) -> %d frames @30fps, aligned at %s' % (
            c['id'], take, c['cmu_title'], st, en, len(range(st, en + 1, STEP)), c['align'])
        res, rep = build_window(sub, take, st, en, c['align'], bvh, fbx, png, title,
                                os.path.join(OUT, 'cmu_%s_rest.bvh' % sub), c['id'], ortho=c.get('ortho', 2.6))
        nout = res['frames_out']
        entry = {
            'id': c['id'],
            'source_take': {'subject': sub, 'take': take, 'cmu_title': c['cmu_title'],
                            'asf': rel(take_paths(sub, take)[0]), 'amc': rel(take_paths(sub, take)[1])},
            'source_frame_window_120hz': [st, en],
            'output': {'frames': nout, 'fps': FPS, 'duration_s': r2((nout - 1) / FPS, 3),
                       'src_frames_used': 'every %dth AMC frame from %d to %d' % (STEP, res['src_first'], res['src_last'])},
            'files': {'bvh': rel(bvh), 'fbx': rel(fbx), 'fbx_bytes': rep['fbx_bytes'], 'contact_sheet': png},
            'net_root_translation_m': sm['net_root_translation_m'],
            'yaw_change_deg': sm['yaw_change_deg'],
            'hips_height_start_m': sm['hips_height_start_m'], 'hips_height_end_m': sm['hips_height_end_m'],
            'alignment': dict(res['align'], mode=c['align'],
                              meaning='clip rigidly moved so the root is at x=z=0 on the first frame and the pelvis '
                                      'faces +Z on the %s frame' % ('first' if c['align'] == 'start' else 'last')),
            'summary': sm,
            'evidence': ev,
            'trace_every_0.1s': trace(m, st, en),
            'checks': {'bvh_fk_vs_asf_max_err_mm': r2(res['selfcheck_max_err_m'] * 1000, 4),
                       'fbx_reimport_vs_bvh_fk_max_err_mm': r2(rep['frame_pos_max_err_m'] * 1000, 4),
                       'fbx_reimport_rest_pose_err_mm': r2(rep['rest_pose_max_err_m'] * 1000, 4),
                       'fbx_actions': rep['actions'], 'fbx_frame_range': rep['action_frame_range'],
                       'fbx_missing_bones': rep['missing_bones'], 'fbx_extra_bones': rep['extra_bones']},
        }
        if 'turn' in c['id']:
            entry['turn_direction'] = 'left' if sm['yaw_change_deg'] > 0 else 'right'
            entry['turn_direction_basis'] = ('sign of the pelvis heading change about +Y in the right-handed Y-up '
                                             'source frame (the CMU character\'s left side is +X when it faces +Z), '
                                             'positive = counter-clockwise seen from above = turning to its left')
        if choice_note:
            entry['take_choice'] = choice_note
        clips.append(entry)
        print('clip', c['id'], take, st, en, entry['checks'])

    doc = {
        'generated_by': 'tools/anim/build_mocap_clips.py (uses tools/anim/cmu_asf_amc.py, bvh_to_fbx_blender.py, '
                        'fbx_check_render_blender.py; Blender %s)' % blender_version(),
        'licence': LICENCE,
        'conventions': {
            'units': 'metres (ASF units x (1/0.45) x 2.54/100 = %.6f, CMU FAQ)' % C.UNITS_TO_M,
            'axes': 'BVH and FBX: right-handed, Y up, rest pose faces +Z, character left = +X. Blender import/export '
                    'use axis_forward=-Z, axis_up=Y both ways, so FBX coordinates equal BVH coordinates.',
            'fps': '30 (every 4th frame of the 120 Hz capture, no filtering)',
            'skeleton': 'CMU ASF bone names, 31 bones, one bone per ASF bone; bone i starts at the end of its parent '
                        '(root children lhipjoint/rhipjoint/lowerback start at the pelvis centre). Rest orientation '
                        'of every BVH joint is identity (world aligned); local rotation = C*M*C^-1.',
            'root_motion': 'kept (real displacement); each clip rigidly aligned, see alignment.',
            'rest_pose': 'BOTH: (1) separate cmu_<subject>_rest.fbx per subject (one frame, ASF rest pose with the '
                         'root lifted so the ankles sit at the measured standing ankle height); (2) every clip FBX '
                         'also has the ASF rest pose as its static node transforms (keyed at Blender frame -1, '
                         'outside the exported take, scene parked there during export), while the take itself holds '
                         'only motion frames 0..N-1. Verified by re-importing each FBX in Blender (rest error column).',
            'fbx_export': 'armature only, add_leaf_bones=False, primary/secondary bone axis Y/X (defaults), '
                          'apply_unit_scale=True, apply_scale_options=FBX_SCALE_ALL, bake_anim_use_all_actions=False, '
                          'one take named after the clip id, bake step 1, simplify %.2f' % SIMPLIFY,
            'rest_pose_shape': 'ASF rest = arms horizontal (T), legs splayed ~20 deg outwards (femur direction '
                               '(+-0.342, -0.940, 0)), feet forward. [Unverified] Unity may want Enforce T-Pose in the '
                               'avatar configure step because of the splayed legs.',
            'unity_notes_unverified': '[Unverified: not opened in Unity] Rig=Humanoid; avatar from '
                                      'cmu_<subject>_rest.fbx (Copy From Other Avatar) for the clips of that subject. '
                                      'Likely mapping: Hips=root, Upper/Lower Leg=l/rfemur, l/rtibia, Foot=l/rfoot, '
                                      'Toes=l/rtoes, Spine=upperback (lowerback starts at the hips position), '
                                      'Chest=thorax, Neck=lowerneck, Head=head, Shoulder=l/rclavicle, Upper/Lower '
                                      'Arm=l/rhumerus, l/rradius, Hand=l/rwrist.',
        },
        'rest_files': rest,
        'clips': clips,
        'not_produced': not_produced(),
    }
    if only:
        old = json.load(open(os.path.join(OUT, 'mocap.json')))
        keep = [cl for cl in old['clips'] if cl['id'] not in only]
        doc['clips'] = sorted(keep + clips, key=lambda cl: [c['id'] for c in CLIPS].index(cl['id']))
    with open(os.path.join(OUT, 'mocap.json'), 'w') as f:
        json.dump(doc, f, indent=1)
    print('wrote', os.path.join(OUT, 'mocap.json'))


def rel(p):
    return os.path.relpath(p, REPO)


def blender_version():
    p = subprocess.run([BLENDER, '-b', '--factory-startup', '--version'], capture_output=True, text=True)
    return p.stdout.split('\n')[0].replace('Blender ', '').strip()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--only', nargs='*')
    ap.add_argument('--preview', nargs=4, metavar=('SUBJECT', 'TAKE', 'START', 'END'))
    ap.add_argument('--align', default='start', choices=('none', 'start', 'end'))
    ap.add_argument('--cells', type=int, default=8)
    ap.add_argument('--scan', metavar='RAW_DIR', help='scan every take under RAW_DIR for stepping turns / walk stops')
    a = ap.parse_args()
    if a.scan:
        print(json.dumps(scan(a.scan), indent=1))
        return
    if a.preview:
        s, t, st, en = a.preview
        preview(s, t, int(st), int(en), a.align, a.cells)
        return
    build(a.only)


if __name__ == '__main__':
    main()
