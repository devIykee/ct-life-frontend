"""Cast v2 registry: bases (distinct characters), example CT hero combos."""
SK = '/Users/mac/Documents/codes/projects/ct-life/art/raw/sketchfab'
BASES = {
    'ubc_m': {'adapter': 'ubc', 'label': 'Athletic base (M)', 'gender': 'm', 'licence': 'CC0 1.0 (bundled License_Standard.txt)',
              'source': 'Quaternius Universal Base Characters [Standard] Superhero_Male', 'archetypes': ['customisation blank'], 'hair_library': True,
              'look': {'hair': 'dark_brown', 'top': 'charcoal', 'bottom': 'denim', 'shoes': 'white'}},
    'ubc_f': {'adapter': 'ubc', 'label': 'Athletic base (F)', 'gender': 'f', 'licence': 'CC0 1.0 (bundled License_Standard.txt)',
              'source': 'Quaternius Universal Base Characters [Standard] Superhero_Female', 'archetypes': ['customisation blank'], 'hair_library': True,
              'look': {'hair': 'black', 'top': 'white', 'bottom': 'black', 'shoes': 'white'}},
}

HEROES = {
    'hero_tactical': {'adapter': 'hero', 'label': 'Tactical red visor', 'gender': 'm', 'hero_id': 'h26',
                      'file': f'{SK}/h26_tactical_red_visor/game_ready_low_poly_character_tactical.glb',
                      'licence': 'CC BY 4.0', 'credit': '"Game Ready low poly character tactical" by DanlyVostok (https://sketchfab.com/3d-models/game-ready-low-poly-character-tactical-342501b0c84843a3a418319c31ea26fa), CC BY 4.0; modified by CT Life',
                      'source': 'Sketchfab CC-BY (row 26)', 'archetypes': ['sniper_bot', 'anon', 'cabal_insider'], 'target_height': 1.80,
                      'snap': [{'mesh': 'Head', 'mode': 'head', 'bone': 'mixamorig:Head_06', 'below_head': 0.03, 'with': ['Material.001']}],
                      'drop_meshes': ['gloves'], 'add_hands': True,
                      'region_rules': [('mat', r'jumpsuit', 'suit'), ('mat', r'boots', 'shoes'), ('mat', r'shoulderpads|plate|^material$', 'accent'),
                                       ('mat', r'gloves|head|headphones|material\.001', 'none')]},
    'hero_gynoid': {'adapter': 'hero', 'label': 'Gynoid 2000', 'gender': 'f', 'hero_id': 'h16',
                    'file': f'{SK}/h16_gynoid_2000/source/Gynoid 2000.fbx',
                    'licence': 'CC BY 4.0', 'credit': '"Gynoid 2000 Series" by Loves_Art (https://sketchfab.com/3d-models/gynoid-2000-series-56993f528d7041f9b734c0221f5dffe0), CC BY 4.0; modified by CT Life',
                    'source': 'Sketchfab CC-BY (row 16)', 'archetypes': ['ai_agent', 'sniper_bot'], 'target_height': 1.72,
                    'region_rules': [('mat', r'^white', 'suit'), ('mat', r'^black', 'accent'), ('mat', r'glass', 'none')]},
    'hero_assassin': {'adapter': 'hero', 'label': 'Gas-mask runner', 'gender': 'f', 'hero_id': 'h01',
                      'file': f'{SK}/h01_cyberpunk_assassin_girl/source/infiltrator.fbx', 'drop_meshes': ['Wapon'],
                      'licence': 'CC BY 4.0', 'credit': '"Cyberpunk Assassin Girl Free Rigged" by Ilya.Anchouz.Danilov (https://sketchfab.com/3d-models/cyberpunk-assassin-girl-free-rigged-b7201c11c0ba494ca4ef158884ce4a05), CC BY 4.0; modified by CT Life',
                      'source': 'Sketchfab CC-BY (row 1)', 'archetypes': ['anon', 'sniper_bot', 'rug_dev'], 'target_height': 1.72,
                      'region_rules': [('zabove', '0.84', 'none'), ('zbelow', '0.07', 'shoes')], 'region_default': 'suit'},
    'hero_hazmat': {'adapter': 'hero', 'label': 'Hazmat operator', 'gender': 'm', 'hero_id': 'h06',
                    'file': f'{SK}/h06_elite_hazmat_operator/scene.gltf',
                    'licence': 'CC BY 4.0', 'credit': '"Elite Hazmat Operator" by SKP_GAMES (https://sketchfab.com/3d-models/elite-hazmat-operator-4d722398711c474cbbc393b117d4e668), CC BY 4.0; modified by CT Life (auto-rigged)',
                    'source': 'Sketchfab CC-BY (row 6)', 'archetypes': ['anon', 'cabal_insider', 'sniper_bot'], 'target_height': 1.80,
                    'region_rules': [('zabove', '0.83', 'none'), ('zbelow', '0.09', 'shoes')], 'region_default': 'suit'},
    'hero_balaclava': {'adapter': 'hero', 'label': 'Balaclava streetwear', 'gender': 'm', 'hero_id': 'h13',
                       'file': f'{SK}/h13_cyberpunk_character/cyberpunk_character.glb',
                       'licence': 'CC BY 4.0', 'credit': '"CyberPunk Character" by AlekseiPresnaykov (https://sketchfab.com/3d-models/cyberpunk-character-bd161bb708a941f0b436c2e95eafc755), CC BY 4.0; modified by CT Life (auto-rigged, decimated)',
                       'source': 'Sketchfab CC-BY (row 13)', 'archetypes': ['cabal_insider', 'rug_dev', 'perp_degen_npc'], 'target_height': 1.80,
                       'region_rules': [('mat', r'mask', 'none'), ('mat', r'eye|cornea|^material$', 'eyes'), ('mat', r'head', 'skin'),
                                        ('mat', r'chest', 'top'), ('zbelow', '0.07', 'shoes'), ('mat', r'legs', 'bottom')]},
}
BASES.update(HEROES)

def lineup_entries():
    """Default look of every base, then the CT hero combos (filled once the modular bases exist)."""
    out = []
    tones = ['skin_02', 'skin_07', 'skin_04', 'skin_09', 'skin_01', 'skin_05', 'skin_10', 'skin_03', 'skin_06', 'skin_08']
    from v2_common import EXPORT
    built = [bid for bid in BASES if (EXPORT / 'bases' / bid / f'{bid}.json').exists()]
    for i, bid in enumerate(built):
        b = BASES[bid]
        out.append({'base': bid, 'label': b['label'], 'skin': tones[i % len(tones)], 'tints': b.get('tints', {})})
    out += [dict(c) for c in COMBOS if c['base'] in built]
    return out

COMBOS = [
    {'base': 'ubc_m', 'label': 'Test: laser-eyes', 'skin': 'skin_08', 'accessories': ['laser_eyes', 'chain'], 'sub': 'UBC TEST COMBO'},
    {'base': 'ubc_f', 'label': 'Test: TV head', 'skin': 'skin_03', 'accessories': ['tv_head', 'small_wings'], 'sub': 'UBC TEST COMBO'},
]

TEST_ENTRIES = [
    {'base': 'ubc_m', 'label': 'm default', 'skin': 'skin_03'},
    {'base': 'ubc_m', 'label': 'm buzzed beard', 'skin': 'skin_09', 'hair': 'Hair_Buzzed', 'facial': ['Hair_Beard'], 'tints': {'top': 'orange', 'hair': 'black'}},
    {'base': 'ubc_m', 'label': 'm long xfit', 'skin': 'skin_06', 'hair': 'Hair_Long', 'tints': {'hair': 'blonde'}},
    {'base': 'ubc_m', 'label': 'm buns xfit', 'skin': 'skin_01', 'hair': 'Hair_Buns', 'tints': {'hair': 'pink'}},
    {'base': 'ubc_f', 'label': 'f default', 'skin': 'skin_10'},
    {'base': 'ubc_f', 'label': 'f buns', 'skin': 'skin_02', 'hair': 'Hair_Buns', 'tints': {'hair': 'ginger', 'top': 'sol_purple'}},
    {'base': 'ubc_f', 'label': 'f buzzed', 'skin': 'skin_07', 'hair': 'Hair_BuzzedFemale', 'tints': {'hair': 'platinum'}},
    {'base': 'ubc_f', 'label': 'f parted xfit', 'skin': 'skin_05', 'hair': 'Hair_SimpleParted', 'tints': {'hair': 'brown'}},
]
