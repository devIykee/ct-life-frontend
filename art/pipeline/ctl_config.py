"""CT Life character pipeline configuration (shared by all char_* scripts).

Everything here is data: which CC0 assets are used, how they are named in the game,
which slot they occupy, and the stylisation targets that push MakeHuman/MPFB towards a
Sims-4-like stylised realism (slightly larger head and eyes, smaller nose).
"""
import os

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
ART = os.path.join(REPO, 'art')
RAW = os.environ.get('CTL_RAW') or os.path.join(ART, 'raw')   # CTL_RAW: raw packs from another checkout (worktrees)
WORK = os.path.join(RAW, 'work')
PROCESSED = os.path.join(ART, 'processed')
PREVIEWS = os.path.join(ART, 'previews')
MPFB_DATA = os.path.join(RAW, 'blender_user', 'extensions', '.user', 'user_default', 'mpfb', 'data')
MPFB_SYS = os.path.join(RAW, 'blender_user', 'extensions', 'user_default', 'mpfb', 'data')
QUAT = os.path.join(RAW, 'quaternius')
UBC = os.path.join(QUAT, 'ubc', 'Universal Base Characters[Standard]')

# ---------------------------------------------------------------- skeleton
# One humanoid skeleton for every CT Life avatar. Names follow the Unreal-mannequin
# convention that the Quaternius Universal Animation Library uses, so clips map 1:1.
SKELETON = [
    # name,          parent
    ('root',         None),
    ('pelvis',       'root'),
    ('spine_01',     'pelvis'),
    ('spine_02',     'spine_01'),
    ('spine_03',     'spine_02'),
    ('neck_01',      'spine_03'),
    ('Head',         'neck_01'),
    ('jaw',          'Head'),
    ('eye_l',        'Head'),
    ('eye_r',        'Head'),
    ('clavicle_l',   'spine_03'),
    ('upperarm_l',   'clavicle_l'),
    ('lowerarm_l',   'upperarm_l'),
    ('hand_l',       'lowerarm_l'),
    ('thumb_01_l',   'hand_l'),
    ('index_01_l',   'hand_l'),   # mitten: drives all four fingers
    ('clavicle_r',   'spine_03'),
    ('upperarm_r',   'clavicle_r'),
    ('lowerarm_r',   'upperarm_r'),
    ('hand_r',       'lowerarm_r'),
    ('thumb_01_r',   'hand_r'),
    ('index_01_r',   'hand_r'),
    ('thigh_l',      'pelvis'),
    ('calf_l',       'thigh_l'),
    ('foot_l',       'calf_l'),
    ('ball_l',       'foot_l'),
    ('thigh_r',      'pelvis'),
    ('calf_r',       'thigh_r'),
    ('foot_r',       'calf_r'),
    ('ball_r',       'foot_r'),
]
BONE_NAMES = [b for b, _ in SKELETON]
# MPFB game_engine bones merged into the mitten bones
FINGER_MERGE = {}
for s in 'lr':
    for f in ('index', 'middle', 'ring', 'pinky'):
        for i in (1, 2, 3):
            FINGER_MERGE[f'{f}_0{i}_{s}'] = f'index_01_{s}'
    for i in (2, 3):
        FINGER_MERGE[f'thumb_0{i}_{s}'] = f'thumb_01_{s}'
    FINGER_MERGE[f'index_01_{s}'] = f'index_01_{s}'
MPFB_RENAME = {'Root': 'root', 'head': 'Head'}

# ---------------------------------------------------------------- bodies
STYLE_TARGETS = (
    [{"target": f"head-scale-{a}-incr", "value": 0.8} for a in ('vert', 'horiz', 'depth')]
    + [{"target": f"{s}-eye-scale-incr", "value": 1.0} for s in 'lr']
    + [{"target": t, "value": 0.5} for t in ('nose-scale-horiz-decr', 'nose-scale-vert-decr', 'nose-scale-depth-decr')]
    + [{"target": "mouth-scale-horiz-decr", "value": 0.25}]
    # face pass (Sims 4 look): open friendly eyes, fuller lips, slight smile, softer jaw and cheeks
    + [{"target": f"{s}-eye-height2-incr", "value": 0.35} for s in 'lr']
    + [{"target": f"{s}-eye-bag-decr", "value": 0.7} for s in 'lr']
    + [{"target": f"{s}-eye-corner1-up", "value": 0.2} for s in 'lr']
    + [{"target": t, "value": v} for t, v in (('mouth-upperlip-volume-incr', 0.6), ('mouth-lowerlip-volume-incr', 0.65),
                                               ('mouth-upperlip-height-incr', 0.25), ('mouth-lowerlip-height-incr', 0.2),
                                               ('mouth-angles-up', 0.55), ('mouth-cupidsbow-incr', 0.3),
                                               ('chin-prominent-decr', 0.35), ('chin-width-decr', 0.15),
                                               ('nose-point-width-decr', 0.3))]
    + [{"target": f"{s}-cheek-volume-incr", "value": 0.35} for s in 'lr']
    + [{"target": f"{s}-cheek-bones-decr", "value": 0.3} for s in 'lr']
)
BODIES = {
    'male': dict(
        phenotype={"gender": 1.0, "age": 0.45, "muscle": 0.55, "weight": 0.45, "proportions": 1.0, "height": 0.55,
                   "cupsize": 0.5, "firmness": 0.5, "race": {"african": 0.34, "asian": 0.33, "caucasian": 0.33}},
        proxy="male1591/male1591.proxy", eyebrows="eyebrow001/eyebrow001.mhclo",
        eyelashes="eyelashes02/eyelashes02.mhclo",
        skin="young_caucasian_male/young_caucasian_male.mhmat",
        extra_targets=[]),
    'female': dict(
        phenotype={"gender": 0.0, "age": 0.45, "muscle": 0.5, "weight": 0.45, "proportions": 1.0, "height": 0.5,
                   "cupsize": 0.55, "firmness": 0.65, "race": {"african": 0.34, "asian": 0.33, "caucasian": 0.33}},
        proxy="female1605/female1605.proxy", eyebrows="eyebrow010/eyebrow010.mhclo",
        eyelashes="eyelashes01/eyelashes01.mhclo",
        skin="young_caucasian_female/young_caucasian_female.mhmat",
        extra_targets=[]),
}

# Six skin tones (sRGB hex). Warm, saturated, Sims-like.
SKIN_TONES = {
    'tone1': 'F0C6A6',
    'tone2': 'E3AD87',
    'tone3': 'CB9068',
    'tone4': 'A86C46',
    'tone5': '7C482D',
    'tone6': '52301F',
}
EYE_COLORS = {'brown': 'brown', 'darkbrown': 'brownlight', 'blue': 'blue', 'green': 'green', 'grey': 'lightblue', 'hazel': 'brown'}  # stylised repaint in ctl_face

# ---------------------------------------------------------------- MPFB (MakeHuman) CC0 parts
# key -> (mpfb asset path, slot, covers, game id, tile size)
# slot: top | bottom | outfit (top+bottom) | shoes | hat | glasses | hair
MPFB_PARTS = {
    'male': {
        'tee':          ('elvs_crude_t-shirt_male/elvs_crude_t-shirt_male.mhclo', 'top', 512),
        'polo':         ('namuhekam_male_polo_shirt/namuhekam_male_polo_shirt.mhclo', 'top', 512),
        'sweater':      ('toigo_fisherman_sweater/toigo_fisherman_sweater.mhclo', 'top', 512),
        'casual_denim': ('male_casualsuit01/male_casualsuit01.mhclo', 'outfit', 512),
        'casual_long':  ('male_casualsuit02/male_casualsuit02.mhclo', 'outfit', 512),
        'casual_stripe':('male_casualsuit03/male_casualsuit03.mhclo', 'outfit', 512),
        'casual_jacket':('male_casualsuit05/male_casualsuit05.mhclo', 'outfit', 512),
        'casual_white': ('male_casualsuit06/male_casualsuit06.mhclo', 'outfit', 512),
        'overalls':     ('male_worksuit01/male_worksuit01.mhclo', 'outfit', 512),
        'suit':         ('toigo_male_suit_tie_and_jacket/toigo_male_suit_tie_and_jacket.mhclo', 'outfit', 512),
        'cargo':        ('cortu_cargo_pants/cortu_cargo_pants.mhclo', 'bottom', 512),
        'shorts':       ('cortu_jeans_shorts/cortu_jeans_shorts.mhclo', 'bottom', 512),
        'slacks':       ('toigo_wool_pants/toigo_wool_pants.mhclo', 'bottom', 512),
        'dress_shoes':  ('shoes01/shoes01.mhclo', 'shoes', 256),
        'sneakers_grey':('shoes02/shoes02.mhclo', 'shoes', 256),
        'boots_black':  ('shoes03/shoes03.mhclo', 'shoes', 256),
        'sneakers_white':('shoes05/shoes05.mhclo', 'shoes', 256),
        'sneakers_blue':('shoes06/shoes06.mhclo', 'shoes', 256),
        'fedora':       ('fedora01/fedora01.mhclo', 'hat', 256),
        'newsboy_cap':  ('jujube_newsboy_cap/jujube_newsboy_cap.mhclo', 'hat', 256),
        'round_shades': ('toigo_round_glasses_leopard/toigo_round_glasses_leopard.mhclo', 'glasses', 256),
        'nerd_glasses': ('spamrakuen_tbm_glasses_frames_01/spamrakuen_tbm_glasses_frames_01.mhclo', 'glasses', 256),
    },
    'female': {
        'tee':          ('joepal_crude_t-shirt_female/joepal_crude_t-shirt_female.mhclo', 'top', 512),
        'camisole':     ('toigo_camisole_top/toigo_camisole_top.mhclo', 'top', 512),
        'tank':         ('toigo_keyhole_tank_top/toigo_keyhole_tank_top.mhclo', 'top', 512),
        'sweater':      ('toigo_fisherman_sweater/toigo_fisherman_sweater.mhclo', 'top', 512),
        'casual_jeans': ('female_casualsuit01/female_casualsuit01.mhclo', 'outfit', 512),
        'casual_shorts':('female_casualsuit02/female_casualsuit02.mhclo', 'outfit', 512),
        'office':       ('female_elegantsuit01/female_elegantsuit01.mhclo', 'outfit', 512),
        'sport':        ('female_sportsuit01/female_sportsuit01.mhclo', 'outfit', 512),
        'suit':         ('toigo_female_suit/toigo_female_suit.mhclo', 'outfit', 512),
        'cargo':        ('cortu_cargo_pants/cortu_cargo_pants.mhclo', 'bottom', 512),
        'shorts':       ('cortu_jeans_shorts/cortu_jeans_shorts.mhclo', 'bottom', 512),
        'slacks':       ('toigo_wool_pants/toigo_wool_pants.mhclo', 'bottom', 512),
        'loafers':      ('shoes04/shoes04.mhclo', 'shoes', 256),
        'ankle_boots':  ('toigo_ankle_boots_female/toigo_ankle_boots_female.mhclo', 'shoes', 256),
        'sneakers_white':('shoes05/shoes05.mhclo', 'shoes', 256),
        'sneakers_blue':('shoes06/shoes06.mhclo', 'shoes', 256),
        'boots_black':  ('shoes03/shoes03.mhclo', 'shoes', 256),
        'fedora':       ('fedora01/fedora01.mhclo', 'hat', 256),
        'newsboy_cap':  ('jujube_newsboy_cap/jujube_newsboy_cap.mhclo', 'hat', 256),
        'round_shades': ('toigo_round_glasses_leopard/toigo_round_glasses_leopard.mhclo', 'glasses', 256),
        'nerd_glasses': ('spamrakuen_tbm_glasses_frames_01/spamrakuen_tbm_glasses_frames_01.mhclo', 'glasses', 256),
    },
}
MPFB_HAIRS = {
    'male':   {'short_crop': 'short02/short02.mhclo', 'afro': 'afro01/afro01.mhclo'},
    'female': {'bob': 'bob02/bob02.mhclo', 'ponytail': 'ponytail01/ponytail01.mhclo', 'afro': 'afro01/afro01.mhclo'},
}
# Quaternius UBC stylised (opaque) hair, fitted onto the MPFB heads
QUAT_HAIRS = {
    'male':   {'buzzed': 'Hair_Buzzed', 'parted': 'Hair_SimpleParted', 'beard': 'Hair_Beard'},
    'female': {'buzzed': 'Hair_BuzzedFemale', 'long': 'Hair_Long', 'buns': 'Hair_Buns', 'parted': 'Hair_SimpleParted'},
}

# ---------------------------------------------------------------- colour variants per part
# variant -> None (original texture) | ('recolor', hex, detail_k) | ('tint_hair', hex)
# Procedural parts take their colour from the 'proc' table instead (baked per variant).
VARIANTS = {
    'tee':          {'white': None, 'black': ('recolor', '24242A', 0.6), 'blurple': ('recolor', '5865F2', 0.6),
                     'rekt': ('rekt', 'E9A0A0', 0.7), 'startup': ('logo', '1F2A44', 0.6),
                     'green': ('recolor', '2FA36B', 0.6), 'orange': ('recolor', 'F28C28', 0.6), 'pink': ('recolor', 'F2A7C3', 0.6)},
    'polo':         {'grey': None, 'skyblue': ('recolor', '9CC9F0', 0.6), 'linen': ('recolor', 'F2ECE0', 0.45),
                     'navy': ('recolor', '25365C', 0.6), 'pink': ('recolor', 'F4B6C8', 0.6)},
    'sweater':      {'red': None, 'cream': ('recolor', 'EDE3CF', 0.5), 'forest': ('recolor', '2F5D46', 0.6)},
    'hoodie':       {'black': ('recolor', '232328', 0.25), 'grey': ('recolor', '9A9CA3', 0.25), 'orange': ('recolor', 'F07C2B', 0.25),
                     'pink': ('recolor', 'E86FA6', 0.25), 'green': ('recolor', '3FAE6A', 0.25), 'purple': ('recolor', '7B5CD6', 0.25)},
    'slacks':       {'charcoal': None, 'khaki': ('recolor', 'C8B48E', 0.6), 'navy': ('recolor', '2B3858', 0.6),
                     'scrubs': ('recolor', '3FA7A0', 0.5), 'cream': ('recolor', 'E9E1CF', 0.5)},
    'shorts':       {'denim': None, 'beige': ('recolor', 'D9C7A3', 0.6), 'black': ('recolor', '2A2A2E', 0.6)},
    'cargo':        {'olive': None, 'khaki': ('recolor', 'BFA77A', 0.6), 'black': ('recolor', '28282C', 0.6)},
    'camisole':     {'original': None, 'white': ('recolor', 'F4F1EA', 0.5), 'black': ('recolor', '232327', 0.5)},
    'tank':         {'original': None, 'white': ('recolor', 'F4F1EA', 0.5), 'mint': ('recolor', '9FE3C8', 0.5)},
}
HAIR_COLORS = {'black': '1E1A18', 'brown': '5A3A24', 'chestnut': '7A4A2A', 'blonde': 'D9B26A', 'auburn': '9A3E22',
               'grey': 'A8A6A2', 'pink': 'F08CB8', 'blue': '4A7FE0'}

# procedural (made-by-us) parts: variant -> base colour hex (other details keep their colours)
PROC_VARIANTS = {
    'fleece_vest': {'navy': '23324A', 'grey': '8A8F98', 'black': '1F1F24', 'olive': '59603F'},
    'trench_coat': {'tan': 'B8925E', 'charcoal': '3A3A40'},
    'hood':        {'black': '232328', 'grey': '9A9CA3', 'orange': 'F07C2B', 'pink': 'E86FA6', 'green': '3FAE6A', 'purple': '7B5CD6'},
    'hard_hat':    {'yellow': 'F5B21B', 'orange': 'F07C1B', 'white': 'EDEDED'},
    'headset':     {'black': '1E1E24', 'white': 'E8E8EC'},
    'ring_light':  {'default': '1C1C1C'},
    'gold_chain':  {'gold': 'E8B931', 'silver': 'C9CDD2'},
    'siren_hat':   {'default': '2A2A33'},
    'baseball_cap': {'navy': '1F2A44', 'black': '1A1A1E', 'red': 'C8312B', 'white': 'EDEDEA', 'green': '2F8F5B'},
    'shades':      {'black': '101014', 'laser': 'FF2020'},
    'ore_backpack': {'default': '3A3F4A'},
    'straw_hat':   {'default': 'E2C27A'},
    'lanyard':     {'red': 'D9342B', 'blue': '2B6CD6'},
    'fox_mask':    {'default': 'F6F2EA'},
    'phone':       {'black': '1A1A1F', 'white': 'EDEDEA', 'gold': 'D8B45A'},
    'megaphone':   {'default': 'F2F2EE'},
    'binoculars':  {'default': '2B2E33'},
}
# parts/variants whose texture should also drive emission in Unity (None = every variant)
EMISSIVE = {('ring_light', None), ('shades', 'laser'), ('ore_backpack', None), ('phone', None)}
# NPC presets are crowd characters: their LOD0 part budgets are scaled down (player avatars keep the full LOD0)
NPC_LOD_SCALE = (0.75, 0.9, 1.0)
NPC_LOD0_CAP = 5900          # crowd budget: whole NPC LOD0 <= ~6k tris
# LOD triangle budgets per slot: (LOD0, LOD1, LOD2). LOD0 keeps the source if it is already lighter.
LOD_BUDGET = {
    'body': (0.8, 0.5, 0.28),           # ratios for the body (LOD0: only hands/feet are reduced)
    'top': (1400, 800, 320), 'bottom': (1000, 600, 240), 'outfit': (2400, 1300, 520),
    'outer': (1500, 800, 320), 'top_addon': (400, 220, 100), 'shoes': (500, 280, 110),
    'hair': (1200, 650, 260), 'facial_hair': (600, 320, 130), 'hat': (500, 280, 110),
    'glasses': (300, 160, 70), 'acc': (350, 200, 80), 'held': (400, 220, 100), 'neck': (260, 150, 60),
    'back': (500, 260, 110), 'face': (400, 200, 90),
}

# ---------------------------------------------------------------- CT outfits (preset looks)
# outfit -> dict(parts={part: variant}, skin, hair=(part, colour), eyes)
OUTFITS = {
    'male': {
        'anon_hoodie':  dict(parts={'hoodie': 'black', 'hood': 'black', 'cargo': 'black', 'sneakers_white': 'default'},
                             hair=('hair_buzzed', 'black'), skin='tone3', label='Anon / Trencher hoodie'),
        'vc_fleece':    dict(parts={'polo': 'skyblue', 'fleece_vest': 'navy', 'slacks': 'khaki', 'dress_shoes': 'default'},
                             hair=('hair_parted', 'brown'), skin='tone1', label='VC fleece vest'),
        'ore_miner':    dict(parts={'overalls': 'default', 'hard_hat': 'yellow', 'boots_black': 'default', 'hair_beard': 'auburn'},
                             hair=None, skin='tone4', label='ORE miner (hard hat)'),
        'discord_mod':  dict(parts={'tee': 'blurple', 'cargo': 'olive', 'sneakers_blue': 'default', 'headset': 'black'},
                             hair=('hair_short_crop', 'black'), skin='tone2', label='Community mod (headset)'),
        'dubai_linen':  dict(parts={'polo': 'linen', 'slacks': 'cream', 'dress_shoes': 'default', 'round_shades': 'default', 'gold_chain': 'gold'},
                             hair=('hair_parted', 'black'), skin='tone3', label='Dubai linen'),
        'sleuth':       dict(parts={'sweater': 'cream', 'trench_coat': 'tan', 'fedora': 'default', 'slacks': 'charcoal', 'dress_shoes': 'default', 'hair_beard': 'brown'},
                             hair=None, skin='tone2', label='Sleuth (detective coat)'),
        'streamer':     dict(parts={'hoodie': 'pink', 'hood': 'pink', 'cargo': 'black', 'sneakers_white': 'default', 'headset': 'white', 'ring_light': 'default'},
                             hair=('hair_afro', 'black'), skin='tone5', label='Streamer (ring light)'),
        'founder_suit': dict(parts={'suit': 'default', 'dress_shoes': 'default'},
                             hair=('hair_parted', 'blonde'), skin='tone1', label='Founder suit'),
        'degen_tee':    dict(parts={'casual_white': 'default', 'sneakers_grey': 'default', 'gold_chain': 'gold'},
                             hair=('hair_short_crop', 'brown'), skin='tone6', label='Degen tee + chain'),
        'builder':      dict(parts={'casual_long': 'default', 'nerd_glasses': 'default', 'sneakers_blue': 'default'},
                             hair=('hair_afro', 'black'), skin='tone5', label='Builder'),
        'town_crier':   dict(parts={'casual_jacket': 'default', 'siren_hat': 'default', 'boots_black': 'default'},
                             hair=None, skin='tone3', label='Town Crier (siren hat)'),
        'clinic_scrubs':dict(parts={'tee': 'green', 'slacks': 'scrubs', 'sneakers_white': 'default'},
                             hair=('hair_buzzed', 'brown'), skin='tone4', label='Rekt Clinic scrubs'),
    },
    'female': {
        'anon_hoodie':  dict(parts={'hoodie': 'black', 'hood': 'black', 'cargo': 'black', 'sneakers_white': 'default'},
                             hair=('hair_bob', 'black'), skin='tone2', label='Anon hoodie'),
        'vc_fleece':    dict(parts={'tee': 'white', 'fleece_vest': 'grey', 'slacks': 'navy', 'loafers': 'default'},
                             hair=('hair_long', 'blonde'), skin='tone1', label='VC fleece vest'),
        'ore_miner':    dict(parts={'tee': 'orange', 'cargo': 'khaki', 'hard_hat': 'yellow', 'boots_black': 'default'},
                             hair=('hair_buzzed', 'brown'), skin='tone5', label='ORE miner (hard hat)'),
        'discord_mod':  dict(parts={'tee': 'blurple', 'shorts': 'denim', 'sneakers_blue': 'default', 'headset': 'black'},
                             hair=('hair_ponytail', 'auburn'), skin='tone3', label='Community mod (headset)'),
        'dubai_linen':  dict(parts={'camisole': 'white', 'slacks': 'cream', 'loafers': 'default', 'round_shades': 'default', 'gold_chain': 'gold'},
                             hair=('hair_long', 'chestnut'), skin='tone4', label='Dubai linen'),
        'sleuth':       dict(parts={'sweater': 'cream', 'trench_coat': 'charcoal', 'fedora': 'default', 'slacks': 'charcoal', 'ankle_boots': 'default'},
                             hair=('hair_bob', 'black'), skin='tone2', label='Sleuth (detective coat)'),
        'streamer':     dict(parts={'sport': 'default', 'headset': 'white', 'ring_light': 'default', 'sneakers_white': 'default'},
                             hair=('hair_buns', 'pink'), skin='tone3', label='Streamer (ring light)'),
        'office':       dict(parts={'office': 'default', 'loafers': 'default'},
                             hair=('hair_bob', 'brown'), skin='tone6', label='Office / founder'),
        'casual_jeans': dict(parts={'casual_jeans': 'default', 'sneakers_white': 'default'},
                             hair=('hair_afro', 'black'), skin='tone5', label='Casual tee + jeans'),
        'pink_suit':    dict(parts={'suit': 'default', 'loafers': 'default'},
                             hair=('hair_parted', 'blonde'), skin='tone1', label='Power suit'),
        'gym_degen':    dict(parts={'sport': 'default', 'sneakers_blue': 'default'},
                             hair=('hair_ponytail', 'black'), skin='tone4', label='Gym / perp degen'),
        'clinic_scrubs':dict(parts={'tee': 'green', 'slacks': 'scrubs', 'sneakers_white': 'default'},
                             hair=('hair_bob', 'auburn'), skin='tone3', label='Rekt Clinic scrubs'),
    },
}

# ---------------------------------------------------------------- animation clips from the Quaternius
# Universal Animation Library 1 + 2 (Standard = free tier, CC0), retargeted to the CTL skeleton.
# clip id (contract/ids/anims.json style) -> (library, source clip, loop)
UAL1 = os.path.join(QUAT, 'ual', 'Universal Animation Library[Standard]', 'Unity', 'UAL1_Standard.fbx')
UAL2 = os.path.join(QUAT, 'ual2', 'Universal Animation Library 2[Standard]', 'Unity', 'UAL2_Standard.fbx')
UAL_CLIPS = {
    'loco_idle':            ('UAL1', 'Idle_Loop', True),
    'idle_fidget_talk':     ('UAL1', 'Idle_Talking_Loop', True),
    'idle_fidget_foldarms': ('UAL2', 'Idle_FoldArms_Loop', True),
    'idle_lean_rail':       ('UAL2', 'Idle_Rail_Loop', True),
    'loco_walk':            ('UAL1', 'Walk_Loop', True),
    'loco_walk_formal':     ('UAL1', 'Walk_Formal_Loop', True),
    'loco_jog':             ('UAL1', 'Jog_Fwd_Loop', True),
    'loco_run':             ('UAL1', 'Sprint_Loop', True),
    'loco_crouch_idle':     ('UAL1', 'Crouch_Idle_Loop', True),
    'loco_crouch_walk':     ('UAL1', 'Crouch_Fwd_Loop', True),
    'loco_shamble':         ('UAL2', 'Zombie_Walk_Fwd_Loop', True),     # rekt walk
    'idle_rekt':            ('UAL2', 'Zombie_Idle_Loop', True),
    'loco_carry_walk':      ('UAL2', 'Walk_Carry_Loop', True),
    'loco_swim':            ('UAL1', 'Swim_Fwd_Loop', True),
    'loco_swim_idle':       ('UAL1', 'Swim_Idle_Loop', True),
    'jump_start':           ('UAL1', 'Jump_Start', False),
    'jump_air':             ('UAL1', 'Jump_Loop', True),
    'jump_land':            ('UAL1', 'Jump_Land', False),
    'act_sit_down':         ('UAL1', 'Sitting_Enter', False),
    'act_sit':              ('UAL1', 'Sitting_Idle_Loop', True),
    'act_sit_talk':         ('UAL1', 'Sitting_Talking_Loop', True),
    'act_stand_up':         ('UAL1', 'Sitting_Exit', False),
    'act_phone_call':       ('UAL2', 'Idle_TalkingPhone_Loop', True),
    'act_interact':         ('UAL1', 'Interact', False),
    'act_pickup':           ('UAL1', 'PickUp_Table', False),
    'act_push':             ('UAL1', 'Push_Loop', True),
    'act_kneel_work':       ('UAL1', 'Fixing_Kneeling', True),
    'act_drive':            ('UAL1', 'Driving_Loop', True),
    'act_mine':             ('UAL2', 'TreeChopping_Loop', True),      # ORE pickaxe swing
    'act_farm_water':       ('UAL2', 'Farm_Watering', False),
    'act_farm_plant':       ('UAL2', 'Farm_PlantSeed', False),
    'act_farm_harvest':     ('UAL2', 'Farm_Harvest', False),
    'act_open_chest':       ('UAL2', 'Chest_Open', False),
    'act_climb_up':         ('UAL2', 'ClimbUp_1m', False),
    'emote_gm_coffee':      ('UAL2', 'Consume', False),
    'emote_nod':            ('UAL2', 'Yes', False),
    'emote_headshake':      ('UAL2', 'Idle_No_Loop', False),
    'emote_shout':          ('UAL2', 'Idle_Rail_Call', False),
    'emote_dance_1':        ('UAL1', 'Dance_Loop', True),
    'emote_throw':          ('UAL2', 'OverhandThrow', False),
    'emote_punch_jab':      ('UAL1', 'Punch_Jab', False),
    'emote_punch_cross':    ('UAL1', 'Punch_Cross', False),
    'react_hit_chest':      ('UAL1', 'Hit_Chest', False),
    'react_hit_head':       ('UAL1', 'Hit_Head', False),
    'react_stagger':        ('UAL2', 'Hit_Knockback', False),          # liquidation: stagger
    'react_liquidated':     ('UAL1', 'Death01', False),                # liquidation: collapse
    'act_get_up':           ('UAL2', 'LayToIdle', False),              # liquidation: get up
    'react_roll':           ('UAL1', 'Roll', False),
}

# ---------------------------------------------------------------- ready-made NPCs (single mesh, single atlas)
# name -> (sex, parts {part: variant}, hair (part, colour) | None, skin, eyes)
NPCS = {
    # Phase 1 archetypes (feature #7)
    'town_crier':     ('male', {'casual_jacket': 'default', 'siren_hat': 'default', 'megaphone': 'default', 'boots_black': 'default'}, ('hair_buzzed', 'black'), 'tone3', 'brown'),
    'giveaway_guy':   ('male', {'casual_white': 'default', 'gold_chain': 'gold', 'phone': 'gold', 'sneakers_white': 'default', 'shades': 'black'}, ('hair_short_crop', 'black'), 'tone4', 'brown'),
    'reply_guy':      ('male', {'tee': 'black', 'cargo': 'olive', 'sneakers_grey': 'default', 'phone': 'black'}, ('hair_parted', 'brown'), 'tone1', 'blue'),
    'perp_degen':     ('male', {'hoodie': 'orange', 'hood': 'orange', 'cargo': 'black', 'sneakers_blue': 'default', 'headset': 'black'}, ('hair_afro', 'black'), 'tone5', 'darkbrown'),
    'rekt_guy':       ('male', {'tee': 'rekt', 'cargo': 'khaki', 'sneakers_grey': 'default'}, ('hair_buzzed', 'blonde'), 'tone1', 'blue'),
    'wallet_tracker': ('male', {'polo': 'navy', 'slacks': 'khaki', 'binoculars': 'default', 'dress_shoes': 'default', 'baseball_cap': 'green'}, ('hair_short_crop', 'brown'), 'tone2', 'green'),
    'doomposter':     ('male', {'hoodie': 'black', 'hood': 'black', 'cargo': 'black', 'boots_black': 'default'}, ('hair_buzzed', 'black'), 'tone2', 'grey'),
    'spaces_host':    ('female', {'sweater': 'forest', 'slacks': 'charcoal', 'headset': 'white', 'sneakers_white': 'default'}, ('hair_long', 'black'), 'tone6', 'darkbrown'),
    'ore_miner':      ('male', {'overalls': 'default', 'hard_hat': 'yellow', 'boots_black': 'default', 'hair_beard': 'auburn', 'ore_backpack': 'default'}, None, 'tone4', 'brown'),
    # later phases (ready early)
    'laser_eyes_maxi':('male', {'tee': 'black', 'cargo': 'black', 'shades': 'laser', 'baseball_cap': 'black', 'sneakers_white': 'default'}, ('hair_buzzed', 'black'), 'tone3', 'brown'),
    'alpha_caller':   ('male', {'hoodie': 'purple', 'hood': 'purple', 'fox_mask': 'default', 'cargo': 'black', 'sneakers_blue': 'default'}, ('hair_short_crop', 'black'), 'tone2', 'brown'),
    'sleuth':         ('male', {'sweater': 'cream', 'trench_coat': 'tan', 'fedora': 'default', 'slacks': 'charcoal', 'dress_shoes': 'default', 'hair_beard': 'brown'}, ('hair_buzzed', 'brown'), 'tone2', 'brown'),
    'auditor':        ('female', {'tee': 'white', 'lanyard': 'red', 'slacks': 'navy', 'loafers': 'default', 'nerd_glasses': 'default'}, ('hair_bob', 'brown'), 'tone3', 'brown'),
    'vc':             ('male', {'polo': 'skyblue', 'fleece_vest': 'navy', 'slacks': 'khaki', 'dress_shoes': 'default'}, ('hair_parted', 'brown'), 'tone1', 'blue'),
    'dubai_relocator':('male', {'polo': 'linen', 'slacks': 'cream', 'dress_shoes': 'default', 'round_shades': 'default', 'gold_chain': 'gold'}, ('hair_parted', 'black'), 'tone3', 'brown'),
    'community_mod':  ('female', {'tee': 'blurple', 'cargo': 'olive', 'sneakers_blue': 'default', 'headset': 'black'}, ('hair_ponytail', 'auburn'), 'tone3', 'green'),
    'airdrop_farmer': ('male', {'overalls': 'default', 'straw_hat': 'default', 'boots_black': 'default'}, ('hair_buzzed', 'blonde'), 'tone2', 'blue'),
    'doxxed_founder': ('male', {'tee': 'startup', 'slacks': 'navy', 'sneakers_white': 'default', 'nerd_glasses': 'default'}, ('hair_parted', 'blonde'), 'tone1', 'blue'),
    'normie_tourist': ('female', {'tee': 'pink', 'shorts': 'beige', 'baseball_cap': 'white', 'sneakers_white': 'default', 'phone': 'white'}, ('hair_ponytail', 'blonde'), 'tone1', 'blue'),
    'engagement_farmer': ('female', {'sport': 'default', 'ring_light': 'default', 'sneakers_white': 'default'}, ('hair_buns', 'pink'), 'tone4', 'brown'),
    'clinic_doctor':  ('female', {'tee': 'green', 'slacks': 'scrubs', 'sneakers_white': 'default', 'lanyard': 'blue'}, ('hair_bob', 'auburn'), 'tone5', 'darkbrown'),
    'fake_rich_larp': ('male', {'suit': 'default', 'round_shades': 'default', 'gold_chain': 'gold', 'dress_shoes': 'default'}, ('hair_parted', 'black'), 'tone4', 'brown'),
}
