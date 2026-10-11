"""Cast v3 registry: Quaternius modular families (each character = a distinct base with its own default look,
all parts swappable inside the family), UBC athletic bases (rebuilt with the neckline/brow fixes), CC-BY masked
heroes (material pass), CT hero combos. Pure data; no bpy."""

# ---------------------------------------------------------------- modular families
# Region rules per family: material -> region for the head mesh. Body/legs/feet: Skin -> skin, dominant other
# material -> top/bottom/shoes, second -> accent (top only, if >= 15 % of non-skin faces), rest -> none.
FAMILIES = {
    'umm': {
        'label': 'Quaternius Ultimate Modular Men (CC0)', 'gender': 'm', 'dir': 'UMM_DIR',
        'head_regions': {'Skin': 'skin', 'Skin_Darker': 'skin', 'Hair': 'hair', 'Hair_White': 'hair', 'Eyebrows': 'brows',
                         'Eye': 'eyes', 'Moustache': 'hair', 'Earrings': 'accent'},
        'characters': [
            # (source stem, base id, label, archetypes, head/body overrides, default look)
            ('Casual', 'umm_hoodie', 'Hoodie anon', ['anon', 'builder', 'rug_dev'], {},
             {'skin': 'skin_02', 'hair': 'dark_brown', 'top': 'black', 'bottom': 'charcoal', 'shoes': 'white'}),
            ('Casual2', 'umm_tee', 'Tee degen', ['degen', 'trencher', 'laser_eyes_maxi'], {},
             {'skin': 'skin_08', 'hair': 'black', 'top': 'orange', 'bottom': 'denim', 'shoes': 'white'}),
            ('Suit', 'umm_suit', 'Suit whale', ['whale', 'cabal_insider', 'vc'], {'body': {'White': 'none', 'Tie': 'accent'}},
             {'skin': 'skin_05', 'hair': 'black', 'top': 'navy', 'bottom': 'navy', 'shoes': 'black', 'accent': 'gold'}),
            ('Punk', 'umm_punk', 'Punk caller', ['kol_caller', 'degen'], {'head': {'Red': 'hair', 'Red_Dark': 'hair'}},
             {'skin': 'skin_01', 'hair': 'green', 'top': 'black', 'bottom': 'denim', 'shoes': 'black'}),
            ('Swat', 'umm_swat', 'Tactical bot', ['sniper_bot', 'cabal_insider'], {'head': {'Swat': 'hat', 'Swat_Black': 'hat', 'Visor': 'none'}},
             {'skin': 'skin_07', 'top': 'black', 'bottom': 'charcoal', 'shoes': 'black', 'hat': 'black', 'accent': 'charcoal'}),
            ('Spacesuit', 'umm_astro', 'Moon mission', ['mooner', 'sniper_bot'],
             {'head': {'Grey': 'none', 'SciFi_Light_Accent': 'hat', 'SciFi_Light': 'none'}},
             {'skin': 'skin_04'}),
            ('Worker', 'umm_worker', 'Hard-hat builder', ['builder'], {'head': {'Worker_Yellow': 'hat'}, 'body': {'Worker_Yellow': 'none'}},
             {'skin': 'skin_09', 'top': 'khaki', 'bottom': 'charcoal', 'accent': 'orange', 'hat': 'yellow', 'hair': 'black'}),
            ('Adventurer', 'umm_hiker', 'Backpack farmer', ['airdrop_farmer', 'trencher'], {},
             {'skin': 'skin_06', 'hair': 'brown', 'top': 'olive', 'bottom': 'khaki'}),
            ('Farmer', 'umm_farmer', 'Literal farmer', ['airdrop_farmer'],
             {'head': {'Beige': 'hat', 'Red': 'accent'}, 'body': {'LightBlue': 'bottom', 'Beige': 'none'}},
             {'skin': 'skin_10', 'top': 'white', 'bottom': 'olive', 'hat': 'khaki', 'hair': 'black'}),
            ('Beach', 'umm_beach', 'Made-it beach', ['retired_whale', 'degen'], {'head': {'Earrings': 'accent'}},
             {'skin': 'skin_03', 'hair': 'blonde', 'top': 'white', 'bottom': 'teal'}),
        ],
        'excluded': {'King': 'medieval crown/armour; not a current-CT look'},
        'extras': {'Backpack': {'from': 'Adventurer', 'socket': 'back', 'id': 'backpack'}},
    },
    'umw': {
        'label': 'Quaternius Ultimate Modular Women (CC0)', 'gender': 'f', 'dir': 'UMW_DIR',
        'head_regions': {'Skin': 'skin', 'Hair_Blond': 'hair', 'Hair_Brown': 'hair', 'Hair_Black': 'hair', 'Brown': 'eyes'},
        'characters': [
            ('Casual', 'umw_tee', 'Tee degen', ['degen', 'trencher', 'laser_eyes_maxi'], {},
             {'skin': 'skin_09', 'hair': 'black', 'top': 'white', 'bottom': 'sol_purple', 'shoes': 'charcoal'}),
            ('Suit', 'umw_suit', 'Suit fund manager', ['whale', 'vc', 'cabal_insider'], {'body': {'White': 'none'}},
             {'skin': 'skin_02', 'hair': 'platinum', 'top': 'black', 'bottom': 'black', 'shoes': 'black'}),
            ('Formal', 'umw_dress', 'Gala KOL', ['kol_caller', 'whale'], {'head': {'Red': 'hair'}, 'legs': {'LimeGreen': 'top'}},
             {'skin': 'skin_06', 'hair': 'auburn', 'top': 'sol_green', 'shoes': 'black'}),
            ('Punk', 'umw_punk', 'Punk caller', ['kol_caller', 'degen'], {'head': {'Pink': 'hair', 'Black': 'accent'}},
             {'skin': 'skin_04', 'hair': 'purple', 'top': 'pink', 'bottom': 'black', 'shoes': 'black'}),
            ('Soldier', 'umw_tactical', 'Tactical sniper', ['sniper_bot'], {},
             {'skin': 'skin_07', 'hair': 'dark_brown', 'top': 'black', 'bottom': 'olive', 'shoes': 'black'}),
            ('SciFi', 'umw_cyber', 'Cyber dev', ['builder', 'ai_agent'], {'head': {'Blue': 'hat', 'Black': 'none'}},
             {'skin': 'skin_05', 'hair': 'black', 'top': 'black', 'accent': 'teal', 'hat': 'teal'}),
            ('Worker', 'umw_worker', 'Hard-hat builder', ['builder'], {'head': {'DarkBrown': 'hair', 'Worker_Yellow': 'hat'},
                                                                       'body': {'Worker_Yellow': 'none'}},
             {'skin': 'skin_10', 'hair': 'black', 'top': 'white', 'accent': 'orange', 'hat': 'yellow'}),
            ('Adventurer', 'umw_hiker', 'Backpack farmer', ['airdrop_farmer', 'trencher'], {},
             {'skin': 'skin_01', 'hair': 'ginger', 'top': 'olive', 'bottom': 'khaki'}),
            ('Medieval', 'umw_hood', 'Hooded cabal', ['cabal_insider', 'anon'],
             {'head': {'White': 'hat', 'DarkBrown': 'hair', 'Black': 'none'}},
             {'skin': 'skin_08', 'hair': 'black', 'top': 'charcoal', 'accent': 'sol_purple', 'hat': 'black'}),
        ],
        'excluded': {'Witch': 'witch hat/robe; fantasy, not a current-CT look'},
        'extras': {},
    },
}

def modular_bases():
    out = {}
    for fam, F in FAMILIES.items():
        for stem, bid, label, arch, ovr, look in F['characters']:
            out[bid] = {'family': fam, 'stem': stem, 'label': label, 'gender': F['gender'], 'archetypes': arch,
                        'overrides': ovr, 'look': look, 'licence': 'CC0 1.0',
                        'source': f"{F['label'].split(' (')[0]}: {stem}"}
    return out

MODULAR = modular_bases()

# ---------------------------------------------------------------- UBC (rebuilt in v3 with fixes)
UBC = {
    'ubc_m': {'label': 'Athletic base (M)', 'gender': 'm', 'archetypes': ['customisation blank', 'degen'],
              'look': {'skin': 'skin_06', 'hair': 'dark_brown', 'top': 'charcoal', 'bottom': 'denim', 'shoes': 'white'}},
    'ubc_f': {'label': 'Athletic base (F)', 'gender': 'f', 'archetypes': ['customisation blank', 'kol_caller'],
              'look': {'skin': 'skin_03', 'hair': 'black', 'top': 'white', 'bottom': 'black', 'shoes': 'white'}},
}

# ---------------------------------------------------------------- CC-BY heroes: v2 geometry + v3 material pass
HEROES = {
    'hero_assassin': {'label': 'Gas-mask runner', 'gender': 'f', 'archetypes': ['anon', 'sniper_bot'], 'rare': True},
    'hero_gynoid': {'label': 'Gynoid 2000', 'gender': 'f', 'archetypes': ['ai_agent', 'sniper_bot'], 'rare': True},
    'hero_hazmat': {'label': 'Hazmat operator', 'gender': 'm', 'archetypes': ['anon', 'cabal_insider'], 'rare': True},
    'hero_balaclava': {'label': 'Balaclava streetwear', 'gender': 'm', 'archetypes': ['cabal_insider', 'rug_dev'], 'rare': True},
}
HERO_EXCLUDED = {'hero_tactical': 'v2 export still has rigid mitten placeholder hands and a misplaced Head-weighted '
                 'source island; the authored-glove candidate is unaccepted (uneven finger folds, questionable distal '
                 'finger references). Excluded from the v3 cast until a hand fix is accepted.'}

# ---------------------------------------------------------------- the eight CT hero combos (4 F / 4 M)
COMBOS = [
    {'id': 'ct_anon', 'label': 'Anon', 'base': 'umm_hoodie', 'skin': 'skin_04', 'accessories': ['pixel_face_mask'],
     'tints': {'top': 'black', 'bottom': 'black', 'shoes': 'white', 'hair': 'black', 'accent': 'sol_green'}},
    {'id': 'ct_whale', 'label': 'Whale', 'base': 'umw_suit', 'skin': 'skin_09', 'accessories': ['chain', 'shoulder_drone'],
     'tints': {'top': 'white', 'bottom': 'white', 'shoes': 'gold', 'hair': 'black', 'accent': 'gold'}},
    {'id': 'ct_degen', 'label': 'Degen', 'base': 'umw_punk', 'skin': 'skin_02', 'accessories': ['headphones'],
     'tints': {'hair': 'green', 'top': 'sol_purple', 'bottom': 'black', 'shoes': 'white', 'accent': 'sol_green'}},
    {'id': 'ct_kol', 'label': 'KOL / caller', 'base': 'umm_punk', 'skin': 'skin_10', 'accessories': ['headphones', 'chain'],
     'tints': {'hair': 'platinum', 'top': 'red', 'bottom': 'black', 'shoes': 'white', 'accent': 'gold'}},
    {'id': 'ct_builder', 'label': 'Builder', 'base': 'umw_cyber', 'skin': 'skin_06', 'accessories': ['visor', 'shoulder_drone'],
     'tints': {'hair': 'black', 'top': 'charcoal', 'accent': 'sol_green', 'hat': 'sol_green'}},
    {'id': 'ct_sniper_bot', 'label': 'Sniper bot', 'base': 'umm_swat', 'skin': 'skin_05', 'accessories': ['visor'],
     'tints': {'top': 'charcoal', 'bottom': 'black', 'hat': 'black', 'accent': 'red'}},
    {'id': 'ct_laser_maxi', 'label': 'Laser-eyes maxi', 'base': 'umm_tee', 'skin': 'skin_08', 'accessories': ['laser_eyes', 'chain'],
     'tints': {'top': 'orange', 'bottom': 'black', 'shoes': 'orange', 'hair': 'black', 'accent': 'gold'}},
    {'id': 'ct_cabal', 'label': 'Cabal', 'base': 'umw_hood', 'skin': 'skin_03', 'accessories': ['visor', 'chain'],
     'tints': {'hat': 'black', 'top': 'black', 'accent': 'sol_purple', 'hair': 'black'}},
]

ARCHETYPES = ['anon', 'whale', 'degen', 'kol_caller', 'builder', 'sniper_bot', 'laser_eyes_maxi', 'cabal_insider',
              'trencher', 'airdrop_farmer', 'rug_dev', 'vc', 'ai_agent', 'mooner', 'retired_whale']
