"""Body-type and face morphs for cast looks. Pure vertex functions of (rest position, skin weights), so
every layer (skin, garments, rigid accessories) moves consistently and the skeleton stays unchanged.
"""
import numpy as np
from cast_common import weights_matrix, mesh_arrays, set_co

# per-bone radial inflation factors (fraction of distance from bone axis)
BODY = {
    'average': {},
    'heavy':  {'Hips': .16, 'Spine': .22, 'Chest': .12, 'Neck': .10, 'UpperArm': .13, 'Forearm': .06,
               'Thigh': .13, 'Calf': .06, 'Shoulder': .08, '_belly': .045, '_jowl': .25},
    'stocky': {'Hips': .10, 'Spine': .12, 'Chest': .12, 'Neck': .12, 'UpperArm': .16, 'Forearm': .09,
               'Thigh': .10, 'Calf': .06, 'Shoulder': .12, '_belly': .02, '_wide': .05, '_jowl': .12},
    'slim':   {'Hips': -.06, 'Spine': -.09, 'Chest': -.06, 'Neck': -.04, 'UpperArm': -.08, 'Forearm': -.05,
               'Thigh': -.08, 'Calf': -.05, 'Shoulder': -.03},
    'broad':  {'Chest': .10, 'Spine': .03, 'UpperArm': .14, 'Forearm': .07, 'Shoulder': .12, 'Neck': .08,
               'Thigh': .05, '_wide': .07},
    'curvy':  {'Hips': .15, 'Thigh': .12, 'Spine': .02, 'Chest': .05, 'UpperArm': .04, '_hip': .03},
}

def _seg_closest(p, h, t):
    ab = t - h
    L2 = max(float(ab @ ab), 1e-9)
    s = np.clip(((p - h) @ ab) / L2, 0, 1)
    return h + s[:, None] * ab

def _smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)

def body_disp(co, W, bones, meta, body):
    prm = BODY.get(body or 'average', {})
    disp = np.zeros_like(co)
    if not prm:
        return disp
    heads = meta['bone_heads']; tails = meta['bone_tails']
    for j, name in enumerate(bones):
        base = name.split('.')[0]
        k = prm.get(base)
        if not k:
            continue
        w = W[:, j]
        sel = w > 1e-4
        if not sel.any():
            continue
        h = np.array(heads[name], dtype=np.float32); t = np.array(tails[name], dtype=np.float32)
        c = _seg_closest(co[sel], h, t)
        r = co[sel] - c
        if base in ('Hips', 'Spine', 'Chest', 'Neck'):
            r[:, 2] = 0  # torso: inflate horizontally only
        disp[sel] += (w[sel] * k)[:, None] * r
    hz = heads['Hips'][2]; cz = heads['Chest'][2]
    if prm.get('_belly'):
        sp = sum(W[:, j] for j, n in enumerate(bones) if n in ('Spine', 'Hips'))
        front = np.clip(-(co[:, 1] - heads['Spine'][1]) / 0.12, 0, 1)
        band = np.exp(-((co[:, 2] - (hz + (cz - hz) * 0.45)) / 0.13) ** 2)
        disp[:, 1] -= prm['_belly'] * sp * front * band
    if prm.get('_wide'):
        ch = sum(W[:, j] for j, n in enumerate(bones) if n.startswith(('Chest', 'Shoulder', 'UpperArm', 'Forearm', 'Hand', 'Thumb', 'Index', 'Middle', 'Ring', 'Pinky')))
        disp[:, 0] += prm['_wide'] * np.sign(co[:, 0]) * np.clip(ch, 0, 1) * _smooth(0.0, 0.18, np.abs(co[:, 0]))
    if prm.get('_hip'):
        hp = sum(W[:, j] for j, n in enumerate(bones) if n.startswith(('Hips', 'Thigh')))
        band = np.exp(-((co[:, 2] - hz) / 0.12) ** 2)
        disp[:, 0] += prm['_hip'] * np.sign(co[:, 0]) * hp * band
    return disp

FACE = {  # name -> amplitude multipliers used by face_disp
    'jaw_wide': 1, 'jaw_narrow': -1, 'chin_long': 1, 'cheeks_full': 1, 'nose_big': 1, 'brow_low': 1, 'face_round': 1,
}

def face_disp(co, W, bones, meta, face, body):
    """face: dict of {name: amount in [-1, 1]}. Only head-weighted vertices below the brow move."""
    disp = np.zeros_like(co)
    face = dict(face or {})
    jowl = BODY.get(body or 'average', {}).get('_jowl', 0)
    if jowl:
        face['cheeks_full'] = face.get('cheeks_full', 0) + jowl * 2
        face['jaw_wide'] = face.get('jaw_wide', 0) + jowl
    if not face:
        return disp
    heads = meta['bone_heads']
    hi = bones.index('Head')
    wh = W[:, hi] + W[:, bones.index('Jaw')] if 'Jaw' in bones else W[:, hi]
    eyez = (heads['Eye.L'][2] + heads['Eye.R'][2]) / 2
    eyey = (heads['Eye.L'][1] + heads['Eye.R'][1]) / 2
    chinz = meta.get('chin_z', eyez - 0.10)
    x, y, z = co[:, 0], co[:, 1], co[:, 2]
    lower = _smooth(eyez - 0.005, eyez - 0.06, z) * _smooth(chinz - 0.05, chinz + 0.005, z) * wh
    if face.get('jaw_wide') or face.get('jaw_narrow'):
        a = face.get('jaw_wide', 0) - face.get('jaw_narrow', 0)
        disp[:, 0] += 0.10 * a * x * lower
    if face.get('cheeks_full'):
        a = face['cheeks_full']
        cheek = np.exp(-((z - (eyez - 0.045)) / 0.03) ** 2) * _smooth(0.015, 0.05, np.abs(x)) * wh
        disp[:, 0] += 0.012 * a * np.sign(x) * cheek
        disp[:, 1] -= 0.004 * a * cheek
    if face.get('chin_long'):
        a = face['chin_long']
        m = np.clip((eyez - 0.075 - z) / max(eyez - 0.075 - chinz, 1e-3), 0, 1) * _smooth(chinz - 0.06, chinz - 0.02, z)
        m = m * wh * (y < eyey + 0.02)
        disp[:, 2] -= 0.012 * a * m
    if face.get('nose_big'):
        a = face['nose_big']
        front = co[(wh > 0.5)][:, 1].min() if (wh > 0.5).any() else eyey - 0.05
        tip = np.array([0, front, eyez - 0.04])
        dn = np.linalg.norm(co - tip, axis=1)
        m = np.clip(1 - dn / 0.03, 0, 1) * wh
        disp += (0.18 * a * m)[:, None] * (co - tip)
    if face.get('brow_low'):
        a = face['brow_low']
        m = np.exp(-((z - (eyez + 0.025)) / 0.012) ** 2) * (y < eyey) * wh
        disp[:, 2] -= 0.004 * a * m
        disp[:, 1] -= 0.003 * a * m
    if face.get('face_round'):
        a = face['face_round']
        disp[:, 0] += 0.05 * a * x * lower
        disp[:, 2] += 0.004 * a * lower
    return disp

def displacement(o, look, meta, bones):
    co = mesh_arrays(o)
    W = weights_matrix(o, bones)
    return co, body_disp(co, W, bones, meta, look.get('body')) + face_disp(co, W, bones, meta, look.get('face'), look.get('body'))

def apply(o, look, meta, bones):
    if not look.get('body') and not look.get('face'):
        return
    co, d = displacement(o, look, meta, bones)
    if np.abs(d).max() > 0:
        set_co(o, co + d)
