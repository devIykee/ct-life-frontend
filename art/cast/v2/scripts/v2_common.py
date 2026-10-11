"""CT cast v2 shared definitions: standard skeleton (CTS), Humanoid mapping, sockets, tint regions,
budgets, paths. Runs inside Blender (bpy optional for the constants)."""
import json, math
from pathlib import Path

REPO = Path('/Users/mac/Documents/codes/projects/ct-life')
V2 = REPO / 'art/cast/v2'
EXPORT = V2 / 'export'
PREVIEWS = V2 / 'previews'
EVID = Path('/Users/mac/Documents/codes/projects/ct-life-evidence/cast/v2')
RAW_Q = REPO / 'art/raw/quaternius'
UBC = RAW_Q / 'ubc/Universal Base Characters[Standard]'
UBC_GLTF = UBC / 'Base Characters/Godot - UE'
UBC_HAIR = UBC / 'Hairstyles/Rigged to Head Bone/glTF (Godot -Unreal)'
UMM = RAW_Q / 'ultimate_modular_men'
UMW = RAW_Q / 'ultimate_modular_women'
HEROES_IN = EVID / 'heroes_in'          # slot B: CC-BY heroes handed over by the coordinator

# ---------------------------------------------------------------- CTS = Quaternius Universal skeleton
# Same hierarchy/names as Universal Base Characters + Universal Animation Library (the source of our
# shared clips). Leaf bones are kept in the rig but are non-deforming.
CTS_PARENT = {
    'root': None, 'pelvis': 'root', 'spine_01': 'pelvis', 'spine_02': 'spine_01', 'spine_03': 'spine_02',
    'neck_01': 'spine_03', 'Head': 'neck_01',
}
for s in ('l', 'r'):
    CTS_PARENT.update({f'clavicle_{s}': 'spine_03', f'upperarm_{s}': f'clavicle_{s}', f'lowerarm_{s}': f'upperarm_{s}',
                       f'hand_{s}': f'lowerarm_{s}', f'thigh_{s}': 'pelvis', f'calf_{s}': f'thigh_{s}',
                       f'foot_{s}': f'calf_{s}', f'ball_{s}': f'foot_{s}', f'ball_leaf_{s}': f'ball_{s}'})
    for f in ('thumb', 'index', 'middle', 'ring', 'pinky'):
        prev = f'hand_{s}'
        for i in (1, 2, 3):
            CTS_PARENT[f'{f}_0{i}_{s}'] = prev
            prev = f'{f}_0{i}_{s}'
        CTS_PARENT[f'{f}_04_leaf_{s}'] = prev
CTS_BONES = list(CTS_PARENT)
CTS_DEFORM = [b for b in CTS_BONES if 'leaf' not in b and b != 'root']

# Unity HumanBodyBones -> CTS (for the Avatar "Configure" mapping; also usable by name in code)
HUMANOID = {
    'Hips': 'pelvis', 'Spine': 'spine_01', 'Chest': 'spine_02', 'UpperChest': 'spine_03', 'Neck': 'neck_01', 'Head': 'Head',
    'LeftShoulder': 'clavicle_l', 'LeftUpperArm': 'upperarm_l', 'LeftLowerArm': 'lowerarm_l', 'LeftHand': 'hand_l',
    'RightShoulder': 'clavicle_r', 'RightUpperArm': 'upperarm_r', 'RightLowerArm': 'lowerarm_r', 'RightHand': 'hand_r',
    'LeftUpperLeg': 'thigh_l', 'LeftLowerLeg': 'calf_l', 'LeftFoot': 'foot_l', 'LeftToes': 'ball_l',
    'RightUpperLeg': 'thigh_r', 'RightLowerLeg': 'calf_r', 'RightFoot': 'foot_r', 'RightToes': 'ball_r',
}
for side, s in (('Left', 'l'), ('Right', 'r')):
    for unity, q in (('Thumb', 'thumb'), ('Index', 'index'), ('Middle', 'middle'), ('Ring', 'ring'), ('Little', 'pinky')):
        for part, i in (('Proximal', 1), ('Intermediate', 2), ('Distal', 3)):
            HUMANOID[f'{side}{unity}{part}'] = f'{q}_0{i}_{s}'

# ---------------------------------------------------------------- sockets (resolved via Humanoid bones)
SOCKETS = {
    'head_top':   {'bone': 'Head', 'humanoid': 'Head', 'use': 'hats, helmets, TV head'},
    'face':       {'bone': 'Head', 'humanoid': 'Head', 'use': 'masks, visors, glasses, laser glow'},
    'neck':       {'bone': 'spine_03', 'humanoid': 'UpperChest', 'use': 'chains, headphones worn on the neck'},
    'back':       {'bone': 'spine_03', 'humanoid': 'UpperChest', 'use': 'wings, backpacks'},
    'shoulder_l': {'bone': 'clavicle_l', 'humanoid': 'LeftShoulder', 'use': 'drones, pets'},
    'shoulder_r': {'bone': 'clavicle_r', 'humanoid': 'RightShoulder', 'use': 'drones, pets'},
    'hand_l':     {'bone': 'hand_l', 'humanoid': 'LeftHand', 'use': 'hand props'},
    'hand_r':     {'bone': 'hand_r', 'humanoid': 'RightHand', 'use': 'hand props'},
}

# ---------------------------------------------------------------- tint regions (two RGBA masks)
# mask0: R skin, G hair, B top, A bottom; mask1: R shoes, G accent, B (reserved), A (reserved)
TINT_REGIONS = ['skin', 'hair', 'top', 'bottom', 'shoes', 'accent']
REGION_ID = {'none': 0, 'skin': 1, 'hair': 2, 'top': 3, 'bottom': 4, 'shoes': 5, 'accent': 6, 'eyes': 7}
MASK_CHANNEL = {'skin': (0, 0), 'hair': (0, 1), 'top': (0, 2), 'bottom': (0, 3), 'shoes': (1, 0), 'accent': (1, 1)}

STARTER_ACCESSORIES = ['pixel_face_mask', 'tv_head', 'visor', 'helmet', 'laser_eyes', 'chain', 'headphones', 'small_wings', 'shoulder_drone']

# ---------------------------------------------------------------- tint palettes (sRGB hex)
HAIR_COLORS = {'black': '#1b1512', 'dark_brown': '#3a2519', 'brown': '#5e3b24', 'auburn': '#7a3420', 'ginger': '#b05a2a',
               'blonde': '#c9a463', 'platinum': '#e3dccb', 'grey': '#8d8a86', 'pink': '#e05aa8', 'blue': '#3a6fd8',
               'green': '#36a36a', 'purple': '#7a4dd1'}
CLOTH_COLORS = {'black': '#1c1d22', 'charcoal': '#3a3d44', 'white': '#f2f2ee', 'navy': '#1f2c4a', 'denim': '#3b5a8a',
                'khaki': '#b9a27a', 'olive': '#4b5320', 'red': '#c8102e', 'orange': '#ff7a1a', 'gold': '#e0a72e',
                'green': '#2e8b57', 'teal': '#1aa6a6', 'purple': '#6b3fd1', 'pink': '#ff4fa3', 'sol_purple': '#8a4dff',
                'sol_green': '#2ee6a6'}

TIERS = ['hero', 'lod0', 'lod1', 'lod2']
CAPS = {'hero': 12000, 'lod0': 6000, 'lod1': 3000, 'lod2': 1200}
MAX_INFL = {'hero': 4, 'lod0': 4, 'lod1': 4, 'lod2': 2}

def palette():
    return json.loads((EXPORT / 'skin_palette.json').read_text())

def log(*a):
    print('[v2]', *a, flush=True)
