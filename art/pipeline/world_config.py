"""CT Island v2 world kit (World lane, 2026-10-17): which CC0 source model becomes which island asset.

Run with the env pipeline, in its own output set (manifest, palette and shared textures under art/processed/world/):
  CTL_ENV_CONFIG=world_config CTL_ENV_SET=world CTL_RAW=<checkout>/art/raw art/pipeline/run.sh art/pipeline/env_process.py
Same item format as env_config.py (src 'pack::Name', dir under art/processed/, fit or scale, tags).

Scales: Kenney city kits x8.5 (a storey is about 0.4 units, so 3.4 m), Kenney car kit x1.76 (a sedan is 4.5 m),
Quaternius textured buildings x2.7 (as the venues use them), KayKit forest x1.15 (pines up to 12 m).
Tags drive placement in tools/world/islandgen/dress.py. 'tint': facade faces (the texels the Quaternius colourways
recolour, art/processed/world/_shared/qtb_tintmask.png) become greys in the palette's bottom half, which the island toon
shader multiplies by each building's own facade colour.
"""
ITEMS = []

KC = 'kenney/city-kit-commercial::'
KS = 'kenney/city-kit-suburban::'
KI = 'kenney/city-kit-industrial::'
KR = 'kenney/city-kit-roads::'
KCAR = 'kenney/car-kit::'
KN = 'kenney/nature-kit::'
KYC = 'kaykit/city_builder_bits::'
KF = 'kaykit/kaykit_forest::'
TB = 'quaternius/ultimate_textured_buildings/Textured Models/Finished Textured Buildings::'
TBTEX = 'quaternius/ultimate_textured_buildings/Textured Models/Textures/'
K = 8.5


def add(id, src, dir, fit=None, scale=None, **kw):
    d = dict(id=id, src=src, dir=dir, **kw)
    if fit:
        d['fit'] = fit
    if scale:
        d['scale'] = scale
    ITEMS.append(d)


B = 'world/buildings'
# Quaternius Ultimate Textured Buildings: painted facades, ten colourways in Unity (one texture per colourway)
QTB = ['1Story', '1Story_GableRoof', '1Story_RoundRoof', '1Story_Sign', '2Story', '2Story_2', '2Story_Balcony', '2Story_Center',
       '2Story_Columns', '2Story_Double', '2Story_GableRoof', '2Story_RoundRoof', '2Story_Sidehouse', '2Story_Sign', '2Story_Slim',
       '2Story_Stairs', '2Story_Wide', '2Story_Wide_2Doors', '3Story_Balcony', '3Story_Slim', '3Story_Small', '4Story',
       '4Story_Center', '4Story_Wide_2Doors', '4Story_Wide_2Doors_Roof', '6Story_Stack']
for n in QTB:
    add('qtb_' + n.lower(), TB + n, B, scale=2.7, tex={'Texture': TBTEX + 'Texture_Light.png'}, tex_max=1024,
        tint_mask='world/_shared/qtb_tintmask.png', tags=['building', 'kit:qtb', 'tint'])
# Kenney commercial: shops, offices and the glass towers
for k in 'abcdefghijklmn':
    add(f'kc_building_{k}', KC + f'building-{k}', B, scale=K, tags=['building', 'kit:kc'])
for k in 'abcde':
    add(f'kc_tower_{k}', KC + f'building-skyscraper-{k}', B, scale=K, tags=['building', 'tower', 'kit:kc'])
for n in ('detail-awning', 'detail-awning-wide', 'detail-overhang', 'detail-overhang-wide', 'detail-parasol-a', 'detail-parasol-b'):
    add('kc_' + n.replace('-', '_'), KC + n, 'world/props', scale=K, tags=['prop'])
# Kenney suburban houses, fences and planters
for k in 'abcdefghijklmnopqrstu':
    add(f'ks_house_{k}', KS + f'building-type-{k}', B, scale=K, tags=['building', 'house', 'kit:ks'])
for n in ('fence', 'fence-1x2', 'fence-1x3', 'fence-2x2', 'fence-low', 'planter'):
    add('ks_' + n.replace('-', '_'), KS + n, 'world/props', scale=K, tags=['prop'])
# Kenney industrial: sheds, warehouses, tanks, chimneys, containers, the water tower and windmills
for k in 'abcdefghijklmnopqrst':
    add(f'ki_building_{k}', KI + f'building-{k}', B, scale=K, tags=['building', 'industrial', 'kit:ki'])
for n in ('chimney-basic', 'chimney-large', 'chimney-medium', 'chimney-small', 'detail-tank', 'detail-tank-large',
          'shipping-container-a', 'shipping-container-b', 'shipping-container-c', 'water-tower', 'windmill', 'windmill-low',
          'solar-panel-landscape-group'):
    add('ki_' + n.replace('-', '_'), KI + n, 'world/props', scale=K, tags=['prop', 'industrial'])

P = 'world/props'
# street furniture (Kenney roads kit, KayKit city bits)
for n in ('traffic-light', 'traffic-light-hanging', 'light-curved', 'light-curved-double', 'light-square', 'light-square-double',
          'road-sign-stop', 'road-sign-street', 'road-sign-warning', 'sign-highway', 'sign-highway-wide', 'construction-barrier',
          'construction-cone', 'construction-light', 'dumpster'):
    add('kr_' + n.replace('-', '_'), KR + n, P, scale=K, tags=['prop', 'street'])
for n in ('electricity-pole', 'electricity-pole-single', 'electricity-pole-wide'):
    add('kr_' + n.replace('-', '_'), KR + n, P, fit=('z', 9.5), tags=['prop', 'power'])
for n in ('bench', 'firehydrant', 'trash_A', 'trash_B', 'streetlight', 'trafficlight_A', 'trafficlight_B', 'trafficlight_C', 'box_A'):
    add('kyc_' + n.lower(), KYC + n, P, fit=('z', {'bench': 0.9, 'firehydrant': 0.8, 'trash_A': 1.0, 'trash_B': 1.0,
                                                   'streetlight': 5.6, 'trafficlight_A': 4.4, 'trafficlight_B': 4.4,
                                                   'trafficlight_C': 4.4, 'box_A': 1.1}[n]), tags=['prop', 'street'])

V = 'world/vehicles'
# parked cars and service vehicles (static decor; Feel's drivable cars are separate)
for n in ('sedan', 'sedan-sports', 'hatchback-sports', 'suv', 'suv-luxury', 'taxi', 'van', 'police', 'delivery', 'delivery-flat',
          'truck', 'truck-flat', 'firetruck', 'ambulance', 'garbage-truck', 'tractor'):
    add('car_' + n.replace('-', '_'), KCAR + n, V, scale=1.76, tags=['vehicle'])

N = 'world/nature'
# KayKit Forest Nature Pack: the forests, groves, street trees, bushes, rocks and grass (one gradient texture)
for n, ks in (('Tree_1', 'ABC'), ('Tree_2', 'ABCDE'), ('Tree_3', 'ABC'), ('Tree_4', 'ABC'), ('Tree_Bare_1', 'ABC'), ('Tree_Bare_2', 'ABC')):
    for k in ks:
        add(f'kf_{n.lower()}_{k.lower()}', KF + f'{n}_{k}_Color1', N, scale=1.15, tags=['tree'], lods=(0.45, 0.14))
for n, ks in (('Bush_1', 'ABCDEFG'), ('Bush_2', 'ABCDEF'), ('Bush_3', 'ABC'), ('Bush_4', 'ABCDEF')):
    for k in ks:
        add(f'kf_{n.lower()}_{k.lower()}', KF + f'{n}_{k}_Color1', N, scale=1.6, tags=['bush'])
for n, ks in (('Rock_1', 'ABCDEFGHIJ'), ('Rock_2', 'ABCDEFGH'), ('Rock_3', 'ABCDEFGHIJ')):
    for k in ks:
        add(f'kf_{n.lower()}_{k.lower()}', KF + f'{n}_{k}_Color1', N, scale=2.4, tags=['rock'])
for n, ks in (('Grass_1', 'ABCD'), ('Grass_2', 'ABCD')):
    for k in ks:
        add(f'kf_{n.lower()}_{k.lower()}', KF + f'{n}_{k}_Color1', N, scale=1.4, tags=['grass'])
# Kenney nature kit: crops and farm fences
for n, h in (('crops_cornStageD', 2.2), ('crops_cornStageC', 1.6), ('crops_wheatStageB', 1.0), ('crops_wheatStageA', 0.7),
             ('crops_leafsStageB', 0.5), ('crop_pumpkin', 0.6), ('crop_melon', 0.45), ('crop_carrot', 0.4),
             ('crops_bambooStageB', 2.6)):
    add('kn_' + n.lower(), KN + n, N, fit=('z', h), tags=['crop'])
for n in ('fence_simple', 'fence_planks', 'fence_gate', 'fence_simpleHigh'):
    add('kn_' + n.lower(), KN + n, P, fit=('x', 2.0), tags=['prop', 'fence'])

W = 'world/boats'
KW = 'kenney/watercraft-kit::'
# Kenney Watercraft Kit (CC0): moored boats in the marina and the harbour, sailing boats in the bay, ships offshore
for n, sc in (('boat-sail-a', 2.6), ('boat-sail-b', 2.6), ('boat-speed-a', 2.1), ('boat-speed-b', 2.1), ('boat-speed-c', 2.1),
              ('boat-speed-e', 2.1), ('boat-speed-g', 2.1), ('boat-fishing-small', 2.3), ('boat-tug-a', 2.8), ('boat-row-small', 1.4),
              ('buoy', 1.2), ('buoy-flag', 1.2), ('ship-large', 4.0), ('ship-ocean-liner', 5.0), ('ship-cargo-a', 5.5), ('boat-house-a', 2.4)):
    add('kw_' + n.replace('-', '_'), KW + n, W, scale=sc, tags=['boat'])

H = 'world/heroes'
TR = 'tripo::'
# Tripo3D text-to-model hero statues (W3; prompts and credits in art/raw/tripo/<id>/receipt.json): original designs that
# nod to community mascots (docs/research/community-eggs-2026-10.md), never their art
for n, h in (('black_bull', 2.6), ('chef_bull', 3.4), ('purple_ghost', 2.2), ('penguin_cart', 2.6), ('space_cat', 3.6)):
    add('hero_' + n, TR + n, H, fit=('z', h), tex_max=1024, tags=['hero'], lods=(0.4, 0.12))

# ------------------------------------------------------------------ W4 beauty pass (2026-10-18)
SN = 'quaternius/stylized_nature_megakit::'
UN = 'quaternius/ultimate_nature::'
USN = 'quaternius/ultimate_stylized_nature::'
PP = 'polypizza::'
# Mature stylised trees (Quaternius Stylized Nature MegaKit, CC0): textured bark and cut-out leaf cards replace the round
# KayKit crowns at eye level. Broadleaf in town and the hills, pines up the mountain, a few twisted autumn trees in parks.
for i, h in ((1, 14.0), (2, 15.5), (3, 12.5), (4, 13.5), (5, 11.0)):
    add(f'w_tree_common_{i}', SN + f'CommonTree_{i}', N, fit=('z', h), tags=['tree', 'broadleaf'], lods=(0.3, 0.08))
for i, h in ((1, 16.0), (2, 14.5), (3, 18.0), (4, 13.0), (5, 10.5)):
    add(f'w_pine_{i}', SN + f'Pine_{i}', N, fit=('z', h), tags=['tree', 'pine'], lods=(0.3, 0.08))
for i, h in ((1, 11.0), (3, 12.0)):
    add(f'w_tree_twisted_{i}', SN + f'TwistedTree_{i}', N, fit=('z', h), tags=['tree', 'autumn'], lods=(0.22, 0.06))
# eye-level ground detail: grass clumps, clover, ferns, flowers, pebbles (tiny, short draw range)
for n, h in (('Grass_Wispy_Short', 0.35), ('Grass_Wispy_Tall', 0.7), ('Grass_Common_Short', 0.3), ('Clover_1', 0.18), ('Clover_2', 0.22),
             ('Fern_1', 0.6), ('Plant_7', 0.35), ('Flower_3_Group', 0.4), ('Flower_4_Group', 0.42), ('Flower_3_Single', 0.35),
             ('Mushroom_Common', 0.2)):
    add('w_' + n.lower(), SN + n, N, fit=('z', h), tags=['ground_detail'])
for n in ('Pebble_Round_1', 'Pebble_Round_3', 'Pebble_Square_2', 'Pebble_Square_5'):
    add('w_' + n.lower(), SN + n, N, fit=('max', 0.35), tags=['ground_detail', 'pebble'])
# street life (Poly Pizza model pages checked 2026-10-18; sources and licences in art/raw/polypizza/_manifest.json)
add('pp_vendor_cart', PP + 'Street_Vendor_Cart__f_LuAcP2_Yh', P, fit=('z', 2.4), tags=['prop', 'street', 'vendor'])        # CC BY 3.0, Alan Zimmerman
add('pp_vending_machine', PP + 'Vending_Machine__0CX6wj64Swu', P, fit=('z', 1.9), tags=['prop', 'street'])                 # CC BY 3.0, Don Carson
add('pp_market_stalls', PP + 'Market_Stalls_Compact__fmHUuX9AS3', P, fit=('z', 2.6), tags=['prop', 'vendor'], decimate=0.4)  # CC0, Quaternius
add('pp_scooter', PP + 'Scooter__3vLV1QtLFP', P, fit=('z', 1.15), tags=['prop', 'street', 'two_wheeler'])                    # CC BY 3.0, MiniPoly
add('pp_moped', PP + 'Bike__DN6DCPopeH', P, fit=('z', 1.15), tags=['prop', 'street', 'two_wheeler'])                         # CC BY 3.0, MiniPoly
add('pp_bicycle', PP + 'Bicycle__19VoUuA2pcN', P, fit=('x', 1.75), tags=['prop', 'street', 'two_wheeler'])                   # CC BY 3.0, Poly by Google
