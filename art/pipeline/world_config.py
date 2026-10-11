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
