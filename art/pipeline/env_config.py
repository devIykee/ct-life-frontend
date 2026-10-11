"""Environment kit: which CC0 source model becomes which CT Life asset.

src  'pack_dir::FileName' (searched under art/raw/<pack_dir>, prefers .glb/.gltf, then .fbx/.obj)
fit  ('z'|'x'|'y'|'max', metres) -> uniform scale so that dimension matches; or scale=<float>
dir  output folder under art/processed/ (environment/<venue>, props, nature)
tex  {material-name substring: texture path under art/raw} overrides (e.g. pink blossom leaves)
"""
KQ = 'quaternius'
DT = 'quaternius/downtown_city_megakit::'
TB = 'quaternius/ultimate_textured_buildings/Textured Models/Finished Textured Buildings::'
TBTEX = 'quaternius/ultimate_textured_buildings/Textured Models/Textures/'
SN = 'quaternius/stylized_nature_megakit::'
USN = 'quaternius/ultimate_stylized_nature::'
UN = 'quaternius/ultimate_nature::'
HI = 'quaternius/ultimate_home_interior::'
UF = 'quaternius/ultimate_furniture::'
SF = 'quaternius/scifi_essentials_kit::'
CP = 'quaternius/cyberpunk_game_kit::'
MS = 'quaternius/modular_streets::'
KC = 'kenney/city-kit-commercial::'
KS = 'kenney/city-kit-suburban::'
KR = 'kenney/city-kit-roads::'
KI = 'kenney/city-kit-industrial::'
KF = 'kenney/factory-kit::'
KFU = 'kenney/furniture-kit::'
KFO = 'kenney/food-kit::'
KMM = 'kenney/mini-market::'
KCAR = 'kenney/car-kit::'
KYC = 'kaykit/city_builder_bits::'
KYR = 'kaykit/restaurant_bits::'
KYF = 'kaykit/furniture_bits::'
KYRES = 'kaykit/resource_bits::'
CU = 'ctlife_custom::'

ITEMS = []


def add(id, src, dir, fit=None, scale=None, fit3=None, **kw):
    d = dict(id=id, src=src, dir=dir, **kw)
    if fit:
        d['fit'] = fit
    if scale:
        d['scale'] = scale
    if fit3:
        d['fit3'] = fit3
    ITEMS.append(d)


# ================================================================== nature (shared)
N = 'nature'
for i in (1, 2, 4):
    add(f'tree_common_{i}', SN + f'CommonTree_{i}', N, fit=('z', 7.0 + i * 0.4))
add('tree_pine_1', SN + 'Pine_1', N, fit=('z', 8.0))
add('tree_pine_3', SN + 'Pine_3', N, fit=('z', 7.0))
for i in (1, 2, 3, 4):
    add(f'tree_blossom_{i}', USN + f'MapleTree_{i}', N, fit=('z', 5.5 + i * 0.3),
        tex={'MapleTree_Leaves': 'ctlife_custom/BlossomTree_Leaves.png'}, tags=['pink_blossom'])
add('tree_maple_autumn', USN + 'MapleTree_5', N, fit=('z', 6.0))
add('tree_birch_1', USN + 'BirchTree_1', N, fit=('z', 7.0))
add('palm_1', UN + 'PalmTree_1', N, fit=('z', 6.5))
add('palm_2', UN + 'PalmTree_3', N, fit=('z', 7.5))
add('bush_round', SN + 'Bush_Common', N, fit=('z', 1.1))
add('bush_flowers', SN + 'Bush_Common_Flowers', N, fit=('z', 1.1))
add('bush_large_flowers', USN + 'Bush_Large_Flowers', N, fit=('z', 1.5))
add('bush_small', USN + 'Bush_Small', N, fit=('z', 0.6))
add('flowers_pink_group', SN + 'Flower_3_Group', N, fit=('z', 0.45))
add('flowers_yellow_group', SN + 'Flower_4_Group', N, fit=('z', 0.45))
add('flower_clump_1', USN + 'Flower_1_Clump', N, fit=('z', 0.4))
add('flower_clump_2', USN + 'Flower_2_Clump', N, fit=('z', 0.4))
add('flower_clump_3', USN + 'Flower_3_Clump', N, fit=('z', 0.4))
add('grass_tuft', SN + 'Grass_Common_Short', N, fit=('z', 0.35))
add('grass_tall', SN + 'Grass_Common_Tall', N, fit=('z', 0.7))
add('plant_big_leaf', SN + 'Plant_1_Big', N, fit=('z', 0.9))
add('fern', SN + 'Fern_1', N, fit=('z', 0.6))
add('petals_scatter_1', USN + 'Petals_1', N, fit=('max', 1.5), tags=['pink_blossom'])
add('petals_scatter_2', USN + 'Petals_2', N, fit=('max', 1.5), tags=['pink_blossom'])
add('rock_path_round', SN + 'RockPath_Round_Wide', N, fit=('max', 2.0))

# ================================================================== props (shared street furniture)
P = 'props'
add('park_bench', CU + 'park_bench', P)
add('bench_city', KYC + 'bench', P, fit=('x', 1.6))
add('street_lamp_single', MS + 'Streetlight_Single', P, fit=('z', 4.4))
add('street_lamp_double', MS + 'Streetlight_Double', P, fit=('z', 4.4))
add('street_lamp_kaykit', KYC + 'streetlight', P, fit=('z', 4.2))
add('street_lamp_neon', CP + 'Light_Street_1', P, fit=('z', 4.0))
add('planter_round', DT + 'Prop_Planter_Single', P)
add('planter_sidewalk', DT + 'Sidewalk_Planter', P)
add('ac_unit', DT + 'Prop_ACUnit', P)
add('manhole_cover', DT + 'Prop_ManholeCover', P)
add('bollard', CU + 'bollard', P)
add('trash_can', KFU + 'trashcan', P, fit=('z', 0.9))
add('dumpster', KR + 'dumpster', P, fit=('z', 1.4))
add('parasol', KC + 'detail-parasol-a', P, fit=('z', 2.6))
add('awning', KC + 'detail-awning', P, fit=('x', 3.0))
add('awning_wide', KC + 'detail-awning-wide', P, fit=('x', 5.0))
add('construction_cone', KR + 'construction-cone', P, fit=('z', 0.7))
add('billboard_large', CU + 'billboard_large', P, tags=['ad_slot'])
add('billboard_wall', CU + 'billboard_wall', P, tags=['ad_slot'])
add('billboard_rooftop', CU + 'billboard_rooftop', P, tags=['ad_slot'])
add('ad_kiosk', CU + 'ad_kiosk', P, tags=['ad_slot'])
add('sandwich_board', CU + 'sandwich_board', P, tags=['ad_slot'])
add('crypto_atm', CU + 'crypto_atm', P, tags=['screen'])
add('news_ticker', CU + 'news_ticker', P, tags=['screen'])
add('neon_frame_wide', CU + 'neon_frame_wide', P, tags=['neon'])
add('neon_frame_square', CU + 'neon_frame_square', P, tags=['neon'])
add('sign_casino', 'quaternius/ultimate_textured_buildings::CasinoSign', P, scale=2.7,
    tex={'Texture': TBTEX + 'Texture_Casino.png'})
add('potted_plant', KFU + 'pottedPlant', P, fit=('z', 1.0))
add('plant_small', KFU + 'plantSmall1', P, fit=('z', 0.35))
add('houseplant_tall', HI + 'Houseplant_3', P, fit=('z', 1.5))
add('houseplant_bush', HI + 'Houseplant_7', P, fit=('z', 0.9))
add('car_sedan', KCAR + 'sedan', P, fit=('max', 4.5))
add('car_suv', KCAR + 'suv', P, fit=('max', 4.7))
add('car_taxi', KCAR + 'taxi', P, fit=('max', 4.5))

# ================================================================== Timeline Square (plaza + streets + buildings)
TS = 'environment/timeline_square'
add('plaza_paving_2m', CU + 'plaza_paving_2m', TS)
add('plaza_hexpaving_2m', CU + 'plaza_hexpaving_2m', TS)
add('plaza_fountain', CU + 'plaza_fountain', TS)
add('pixel_wall', CU + 'pixel_wall', TS, tags=['ad_slot'])
add('sidewalk_straight_3m', DT + 'Sidewalk_Straight_3m', TS)
add('sidewalk_corner_3m', DT + 'Sidewalk_Corner_Round_3m', TS)
add('sidewalk_nocurb_3m', DT + 'Sidewalk_NoCurb_3m', TS)
add('street_2lane', DT + 'Street_2Lane', TS)
add('street_4lane', DT + 'Street_4Lane', TS)
add('street_4way', DT + 'Street_4WayIntersection', TS)
add('street_t', DT + 'Street_TIntersection', TS)
add('street_curve_2lane', DT + 'Street_Curve_2Lane_Curb', TS)
add('crosswalk_decal', DT + 'Decal_Crosswalk', TS)
add('floor_concrete_4m', DT + 'Floor_4x4', TS)
for name, tex, h in (('2Story_Sign', 'Texture_Light', 7.4), ('1Story_Sign', 'Texture_Yellow', 4.6),
                     ('3Story_Balcony', 'Texture_Blue', 10.5), ('4Story_Wide_2Doors_Roof', 'Texture_Red', 14.0),
                     ('6Story_Stack', 'Texture_Grey', 19.0), ('2Story_Columns', 'Texture_Green', 7.4),
                     ('2Story_Wide', 'Texture_Light2', 7.4), ('3Story_Slim', 'Texture_DarkPurple', 10.5),
                     ('4Story_Center', 'Texture_DarkBlue', 14.0), ('2Story_Balcony', 'Texture_Yellow', 7.4)):
    add('shop_' + name.lower(), TB + name, TS, scale=2.7, tex={'Texture': TBTEX + tex + '.png'},
        tags=['building', 'recolour:' + ','.join(['Texture_Light', 'Texture_Yellow', 'Texture_Blue', 'Texture_Red', 'Texture_Green', 'Texture_Grey'])])
for k in 'abcde':
    add(f'tower_glass_{k}', KC + f'building-skyscraper-{k}', TS, fit=('z', 30.0 + 'abcde'.index(k) * 4))
for k in 'abcdefgh':
    add(f'building_low_{k}', KC + f'building-{k}', 'environment/city_map', fit=('max', 12.0))

# ================================================================== Terminal Café
TC = 'environment/terminal_cafe'
add('trading_terminal', CU + 'trading_terminal', TC, tags=['screen'])
add('wall_screen_large', CU + 'wall_screen_large', TC, tags=['screen'])
add('cafe_table_round', KYR + 'table_round_A', TC, fit3=(1.0, 1.0, 0.76))
add('cafe_table_round_set', KYR + 'table_round_A_decorated', TC, scale_like='cafe_table_round')
add('cafe_table_small', KYR + 'table_round_A_small', TC, fit3=(0.7, 0.7, 0.76))
add('cafe_chair_a', KYR + 'chair_A', TC, fit=('z', 0.9))
add('cafe_chair_b', KYR + 'chair_B', TC, fit=('z', 0.9))
add('cafe_stool', KYR + 'chair_stool', TC, fit3=(0.42, 0.42, 0.72))
add('bar_counter', KYR + 'kitchencounter_straight_A', TC, fit3=(1.0, 0.7, 0.95))
add('bar_counter_corner', KYR + 'kitchencounter_outercorner', TC, scale_like='bar_counter')
add('cafe_fridge', KYR + 'fridge_A_decorated', TC, fit3=(0.8, 0.75, 1.9))
add('cafe_shelf', KYR + 'shelf_papertowel_decorated', TC, fit=('x', 1.0))
add('menu_board', KYR + 'menu', TC, fit=('z', 0.3))
add('coffee_machine', KFU + 'kitchenCoffeeMachine', TC, fit=('z', 0.4))
add('kitchen_bar', KFU + 'kitchenBar', TC, fit=('z', 1.05))
add('kitchen_bar_end', KFU + 'kitchenBarEnd', TC, fit=('z', 1.05))
add('bar_stool', KFU + 'stoolBar', TC, fit=('z', 0.75))
add('laptop', KFU + 'laptop', TC, fit=('x', 0.34))
add('desk_lamp', KFU + 'lampSquareTable', TC, fit=('z', 0.5))
add('cup_coffee', KFO + 'cup-coffee', TC, fit=('z', 0.1))
add('mug', KFO + 'mug', TC, fit=('z', 0.1))
add('croissant', KFO + 'croissant', TC, fit=('x', 0.14))
add('donut', KFO + 'donut-sprinkles', TC, fit=('x', 0.1))
add('cake', KFO + 'cake', TC, fit=('x', 0.25))
add('cash_register', KMM + 'cash-register', TC, fit=('z', 0.35))
add('display_bread', KMM + 'display-bread', TC, fit=('z', 1.2))
add('floor_wood_2m', CU + 'floor_wood_2m', TC)
add('floor_tiles_2m', CU + 'floor_tiles_2m', TC)
for kind in ('solid', 'window', 'door'):
    add(f'wall_brick_{kind}_2m', CU + f'wall_brick_{kind}_2m', TC)
    add(f'wall_plaster_{kind}_2m', CU + f'wall_plaster_{kind}_2m', 'environment/home')

# ================================================================== Perp Pit (trading floor)
PP = 'environment/perp_pit'
add('trading_desk', CU + 'trading_desk', PP)
add('monitor_triple', CU + 'monitor_triple', PP, tags=['screen'])
add('desk_l_scifi', SF + 'Prop_Desk_L', PP, fit=('z', 0.76))
add('desk_medium_scifi', SF + 'Prop_Desk_Medium', PP, fit=('z', 0.76))
add('office_chair_scifi', SF + 'Prop_Chair', PP, fit=('z', 1.0))
add('office_chair', UF + 'OfficeChair', PP, fit=('z', 1.05))
add('server_rack', CU + 'server_rack', PP, tags=['neon'])
add('computer_tower_large', CP + 'Computer_Large', PP, fit=('z', 2.0))
add('computer_terminal', CP + 'Computer', PP, fit=('z', 1.4))
add('locker', SF + 'Prop_Locker', PP, fit=('z', 1.9))
add('floor_metal_2m', CU + 'floor_metal_2m', PP)
add('floor_concrete_2m', CU + 'floor_concrete_2m', PP)

# ================================================================== Rekt Clinic
RC = 'environment/rekt_clinic'
add('hospital_bed', CU + 'hospital_bed', RC)
add('iv_stand', CU + 'iv_stand', RC)
add('vitals_monitor', CU + 'vitals_monitor', RC, tags=['screen'])
add('privacy_curtain', CU + 'privacy_curtain', RC)
add('tombstone', CU + 'tombstone', RC, tags=['ad_slot'])
add('health_pack', SF + 'Prop_HealthPack', RC, fit=('max', 0.45))
add('syringe', SF + 'Prop_Syringe', RC, fit=('max', 0.2))
add('waiting_chair', HI + 'Chair_2', RC, fit=('z', 0.9))
add('waiting_couch', HI + 'Couch_Small1', RC, fit=('x', 1.6))
add('clinic_cabinet', HI + 'Drawer_2', RC, fit=('x', 1.4))
add('clinic_sink', HI + 'Bathroom_Sink', RC, fit=('z', 0.9))
add('floor_marble_2m', CU + 'floor_marble_2m', RC)

# ================================================================== Spaces Studio
SS = 'environment/spaces_studio'
add('stage_platform', CU + 'stage_platform', SS)
add('podium', CU + 'podium', SS, tags=['ad_slot'])
add('mic_stand', CU + 'mic_stand', SS)
add('studio_speaker', CU + 'studio_speaker', SS)
add('studio_light', CU + 'studio_light', SS)
add('on_air_sign', CU + 'on_air_sign', SS, tags=['neon'])
add('podcast_table', CU + 'podcast_table', SS)
add('lounge_sofa', KFU + 'loungeSofa', SS, fit=('x', 2.1))
add('lounge_chair', KFU + 'loungeChair', SS, fit=('z', 0.85))
add('floor_lamp', HI + 'Light_Floor2', SS, fit=('z', 1.7))
add('light_stand', HI + 'Light_Stand1', SS, fit=('z', 1.9))
add('speaker_small', KFU + 'speakerSmall', SS, fit=('z', 0.35))

# ================================================================== ORE Refinery
ORE = 'environment/ore_refinery'
add('ore_furnace', CU + 'ore_furnace', ORE, tags=['glow'])
add('ore_board_frame', CU + 'ore_board_frame', ORE, tags=['screen', 'neon'])
add('stake_terminal', CU + 'stake_terminal', ORE, tags=['screen'])
add('ore_crystal_pile', CU + 'ore_crystal_pile', ORE, tags=['glow'])
for name, fitv in (('conveyor', ('x', 2.0)), ('conveyor-long', ('x', 4.0)), ('conveyor-corner', ('x', 2.0)),
                   ('conveyor-stripe-sides', ('x', 2.0)), ('hopper-round', ('z', 2.6)), ('hopper-high-square', ('z', 3.6)),
                   ('machine', ('x', 2.0)), ('machine-window', ('x', 2.0)), ('machine-fortified', ('x', 2.0)),
                   ('crane', ('z', 5.0)), ('catwalk-straight', ('x', 2.0)), ('catwalk-stairs', ('z', 2.2)),
                   ('pipe-glass-large-long', ('max', 2.0)), ('cog-a', ('max', 1.2)), ('box-large', ('max', 1.4)),
                   ('lever-double', ('z', 1.2)), ('floor-large', ('x', 4.0))):
    add('factory_' + name.replace('-', '_'), KF + name, ORE, fit=fitv, recolor='steel')
for name, fitv in (('detail-tank-large', ('z', 6.0)), ('detail-tank', ('z', 4.0)), ('chimney-large', ('z', 14.0)),
                   ('water-tower', ('z', 10.0)), ('shipping-container-a', ('x', 6.0))):
    add('ind_' + name.replace('-', '_'), KI + name, ORE, fit=fitv, recolor='steel')
for name, fitv in (('Gold_Bars_Stack_Large', ('max', 0.9)), ('Gold_Nuggets', ('max', 0.5)), ('Stone_Chunks_Large', ('max', 1.0)),
                   ('Iron_Bars_Stack_Medium', ('max', 0.8)), ('Pallet_Wood', ('x', 1.2)), ('Fuel_A_Barrels', ('max', 1.4))):
    add('res_' + name.lower(), KYRES + name, ORE, fit=fitv)
add('pipes_industrial', CP + 'Pipe_1', ORE, fit=('max', 3.0))
add('ac_stacked', CP + 'AC_Stacked', ORE, fit=('z', 2.0))

# ================================================================== Player home
HM = 'environment/home'
for name, fitv in (('Couch_Medium1', ('x', 2.2)), ('Couch_L', ('x', 2.6)), ('Couch_Small2', ('x', 1.5)),
                   ('Bed_King', ('x', 2.1)), ('Bed_Single', ('y', 2.1)), ('Bed_Bunk', ('z', 1.8)),
                   ('Kitchen_Fridge', ('z', 1.9)), ('Kitchen_Oven', ('z', 0.95)), ('Kitchen_Sink', ('z', 0.95)),
                   ('Kitchen_2Drawers', ('z', 0.95)), ('Kitchen_Cabinet1', ('z', 0.95)),
                   ('Bathroom_Toilet', ('z', 0.8)), ('Bathroom_Bathtub', ('x', 1.7)), ('Bathroom_Shower1', ('z', 2.1)),
                   ('Bathroom_Sink', ('z', 0.9)), ('Bookshelf', ('z', 1.9)), ('Drawer_1', ('z', 0.9)),
                   ('Table_RoundSmall', ('z', 0.75)), ('Fireplace', ('z', 1.3)), ('Carpet_1', ('x', 2.4)),
                   ('Carpet_Round', ('x', 2.0)), ('Light_Floor1', ('z', 1.7)), ('Light_Chandelier', ('max', 0.9)),
                   ('Houseplant_1', ('z', 1.1)), ('Houseplant_5', ('z', 0.6)),
                   ('Curtains_Double', ('z', 2.4)), ('Chair_1', ('z', 0.9)), ('Door_1', ('z', 2.1))):
    add('home_' + name.lower(), HI + name, HM, fit=fitv)
for name, fitv in (('armchair_pillows', ('z', 0.9)), ('couch_pillows', ('x', 2.0)), ('bed_double_A', ('x', 1.7)),
                   ('lamp_standing', ('z', 1.6)), ('table_medium', ('z', 0.76)), ('shelf_B_large_decorated', ('x', 1.2)),
                   ('rug_rectangle_stripes_A', ('x', 2.4)), ('cactus_medium_A', ('z', 0.5)), ('pictureframe_large_A', ('max', 0.9))):
    add('home_' + name.lower(), KYF + name, HM, fit=fitv)
add('home_desk', UF + 'Desk', HM, fit=('z', 0.76))
add('home_tv_modern', KFU + 'televisionModern', HM, fit=('x', 1.3), tags=['screen'])
add('home_floor_wood_2m', CU + 'floor_wood_2m', HM)

# ================================================================== City map (low detail travel scene)
CM = 'environment/city_map'
for name in ('road-straight', 'road-crossroad', 'road-bend', 'road-intersection', 'road-end', 'road-roundabout'):
    add('map_' + name.replace('-', '_'), KR + name, CM, fit=('x', 8.0))
add('map_tile_low', KR + 'tile-low', CM, fit=('x', 8.0))
add('map_tree_small', KS + 'tree-small', CM, fit=('z', 4.0))
add('map_tree_large', KS + 'tree-large', CM, fit=('z', 6.0))
for k in 'abcdefgh':
    add(f'map_house_{k}', KS + f'building-type-{k}', CM, fit=('max', 10.0))
for k in 'abcd':
    add(f'map_tower_{k}', KC + f'low-detail-building-{k}', CM, fit=('max', 14.0))

# ================================================================== Round 6: real modelled venue furniture (café, studio, square)
# Replaces the venue builder's box/cylinder furniture. Colourways come from rule-based recolouring in env_process
# (first matching HSV range -> palette colour), so every variant stays on the shared palette material (no new draws).
FT = 'kenney-fantasy-town-kit::'
TL = 'environment/timeline_square'


def rc(*rules):
    return [dict(r) for r in rules]


WOOD_GREEN = rc({'v': (0.0, 0.4), 'to': 'a06a3c'}, {'v': (0.4, 1.0), 'to': '3b6b4b'})          # UHI chairs: wood / cushion
BENT = lambda cushion, wood='5a3a24': rc({'s': (0.3, 1.0), 'to': wood}, {'s': (0.0, 0.3), 'to': cushion})  # UF chair/stool
UPH = lambda fabric, legs='5a3a24': rc({'s': (0.2, 1.0), 'to': fabric}, {'s': (0.0, 0.2), 'to': legs})    # UHI couches
PLANKS = rc({'v': (0.0, 0.43), 'to': '7f5530'}, {'v': (0.43, 1.0), 'to': '94643a'})
POT = lambda pot, leaf='4f8a3a': rc({'h': (0.15, 0.35), 's': (0.4, 1.0), 'to': leaf}, {'s': (0.0, 0.12), 'to': pot})
CUSHION = lambda col: rc({'h': (0.36, 0.46), 's': (0.3, 1.0), 'to': col})                     # custom upholstery 35634a

# --- Terminal Café
add('cafe_chair_spindle', HI + 'Chair_3', TC, fit=('z', 0.9), recolor=WOOD_GREEN)
add('cafe_chair_bistro', UF + 'Chair', TC, fit=('z', 0.85), recolor=BENT('3b6b4b'), decimate=0.7)
add('cafe_bar_stool', UF + 'Stool', TC, fit3=(0.46, 0.46, 0.76), recolor=BENT('3b6b4b'))
add('cafe_sofa', HI + 'Couch_Medium1', TC, fit3=(2.2, 0.95, 0.66), recolor=UPH('35634a'))
add('cafe_armchair', HI + 'Couch_Small1', TC, fit3=(0.95, 0.85, 0.66), recolor=UPH('35634a'))
add('cafe_table_farm', UF + 'Table', TC, fit3=(2.6, 1.3, 0.76), recolor=PLANKS)
add('cafe_table_booth', UF + 'Table2', TC, fit3=(1.3, 0.72, 0.76), recolor=PLANKS)
add('cafe_table_bistro', HI + 'Table_RoundSmall2', TC, fit3=(1.0, 1.0, 0.76), recolor=rc({'to': '6b4429'}))
add('cafe_coffee_table', HI + 'Table_RoundSmall', TC, fit3=(1.0, 1.0, 0.46), recolor=rc({'to': '6b4429'}), decimate=0.6)
add('cafe_side_table', HI + 'Table_RoundSmall', TC, fit3=(0.6, 0.6, 0.55), recolor=rc({'to': '6b4429'}), decimate=0.6)
add('cafe_floor_lamp', HI + 'Light_Stand1', TC, fit=('z', 1.7), recolor=rc({'v': (0.0, 0.6), 'to': 'b8913f'}, {'to': 'efe3c8'}))
add('cafe_desk_lamp', HI + 'Light_Desk', TC, fit=('z', 0.45), recolor=rc({'v': (0.0, 0.3), 'to': '2e6b4a'}, {'to': 'b8913f'}))
add('cafe_plant_monstera', HI + 'Houseplant_1', TC, fit=('z', 1.15), recolor=POT('efe3c8'))
add('cafe_plant_leafy', HI + 'Houseplant_6', TC, fit=('z', 1.35), recolor=POT('c8673f', '5a9441'))
add('cafe_plant_snake', HI + 'Houseplant_4', TC, fit=('z', 0.95), recolor=POT('c8673f', '4f7f3a'))
add('cafe_plant_fern', HI + 'Houseplant_5', TC, fit=('z', 0.62), recolor=POT('efe3c8', '5a9441'))
add('cafe_plant_bush', HI + 'Houseplant_7', TC, fit=('z', 0.95), recolor=POT('c8673f', '4f8a3a'))
add('cafe_laptop', KFU + 'laptop', TC, fit=('x', 0.34), recolor=rc({'v': (0.85, 1.0), 'to': '1b2230'}, {'to': 'b9c0c6'}))
add('cafe_keyboard', KFU + 'computerKeyboard', TC, fit=('x', 0.44), recolor=rc({'v': (0.0, 0.68), 'to': '2b2d31'}, {'to': '3a3d44'}))
add('cafe_monitor', KFU + 'computerScreen', TC, fit=('x', 0.62), recolor=rc({'v': (0.85, 1.0), 'to': '0f141c'}, {'to': '1d1f24'}))
add('booth_bench', CU + 'booth_bench', TC, smooth=40)
add('espresso_machine', CU + 'espresso_machine', TC, smooth=35)
add('coffee_grinder', CU + 'coffee_grinder', TC, smooth=35)
add('pastry_case', CU + 'pastry_case', TC)
add('pendant_dome', CU + 'pendant_dome', TC, smooth=40)
add('cafe_books', KFU + 'books', TC, fit=('x', 0.26),
    recolor=rc({'h': (0.95, 0.1), 'to': '8e2f23'}, {'h': (0.4, 0.5), 's': (0.3, 1.0), 'to': '2f6f73'}, {'v': (0.97, 1.0), 'to': 'efe3c8'}, {'to': 'c9962f'}))

# --- Spaces Studio
add('studio_armchair', HI + 'Couch_Small1', SS, fit3=(0.95, 0.85, 0.62), recolor=UPH('e3d6c0', '6b4429'))
add('studio_sofa_teal', HI + 'Couch_Medium1', SS, fit3=(2.2, 0.95, 0.66), recolor=UPH('2f6f73', '6b4429'))
add('studio_sofa_plum', HI + 'Couch_Medium1', SS, fit3=(2.0, 0.9, 0.64), recolor=UPH('4b2163', '6b4429'))
for col, hexc in (('rose', 'c25560'), ('petrol', '2f6f73'), ('purple', '4b2163')):
    add('studio_stool_' + col, CU + 'bar_stool_ring', SS, smooth=40, recolor=CUSHION(hexc))
add('studio_side_table', HI + 'Table_RoundSmall', SS, fit3=(0.5, 0.5, 0.52), recolor=rc({'to': '6b4429'}), decimate=0.6)
add('studio_coat_rack', KFU + 'coatRackStanding', SS, fit=('z', 1.75), recolor=rc({'to': '6b4429'}))
add('studio_headphones', CU + 'headphones', SS, smooth=35)
add('bean_bag', CU + 'bean_bag', SS, smooth=True)
add('floor_cushion', CU + 'floor_cushion', SS, smooth=True)
add('pouf_round', CU + 'pouf_round', SS, smooth=True)
add('throw_pillow', CU + 'throw_pillow', SS, smooth=True)
add('table_oval', CU + 'table_oval', SS, smooth=35)
add('lantern_floor', CU + 'lantern_floor', SS, tags=['neon'])
add('terrace_bench_purple', CU + 'bench_cushioned', SS, smooth=40, recolor=CUSHION('4b2163'))
add('terrace_bench_teal', CU + 'bench_cushioned', SS, smooth=40, recolor=CUSHION('2f6f73'))

# --- Timeline Square
add('fountain_tiered', CU + 'fountain_tiered', TL, smooth=40)
add('tl_chair_bistro', UF + 'Chair', TL, fit=('z', 0.85), recolor=BENT('efe3c8', '2b2d31'), decimate=0.7)
add('tl_table_bistro', HI + 'Table_RoundSmall2', TL, fit3=(0.8, 0.8, 0.74), recolor=rc({'to': '2b2d31'}))
add('tl_stall', FT + 'stall-red', TL, fit=('z', 2.7), force_palette=True, quant=24,
    recolor=rc({'h': (0.93, 0.03), 's': (0.4, 1.0), 'to': 'fc8e03'}))
for veg in ('carrots', 'tomatoes', 'lettuce', 'onions', 'potatoes'):
    add('tl_crate_' + veg, KYR + 'crate_' + veg, TL, fit=('x', 0.62), force_palette=True, quant=20, decimate=0.4)
add('tl_sack', CU + 'sack', TL, smooth=True)
add('tl_pot_large', 'kenney/nature-kit::pot_large', TL, fit=('x', 0.62), recolor=rc({'v': (0.0, 0.95), 'to': 'b05a36'}, {'to': 'c8673f'}))
add('tl_pot_small', 'kenney/nature-kit::pot_small', TL, fit=('x', 0.44), recolor=rc({'v': (0.0, 0.95), 'to': 'b05a36'}, {'to': 'c8673f'}))
add('tl_bush_flowers', USN + 'Bush_Flowers', TL, fit=('z', 0.42), decimate=0.5)
add('tl_bush_flowers_long', USN + 'Bush_Large_Flowers', TL, fit=('z', 0.45), decimate=0.6)
for i in (1, 2, 4, 5):
    add(f'tl_flower_{i}', USN + f'Flower_{i}_Clump', TL, fit=('z', 0.34), decimate=0.4 if i == 1 else None)
add('tl_tulips', SN + 'Flower_3_Group', TL, fit=('z', 0.4), decimate=0.3)
add('pothos_vine', CU + 'pothos_vine', TC)
# lighter LOD0 variants of models placed many times (round 6 triangle budget)
add('cafe_desk', UF + 'Desk', TC, fit=('z', 0.76), decimate=0.4)
add('cafe_office_chair', UF + 'OfficeChair', TC, fit=('z', 1.05), decimate=0.45)
add('mug_lite', KFO + 'mug', TC, fit=('z', 0.1), decimate=0.5)
add('houseplant_bush_lite', HI + 'Houseplant_7', TC, fit=('z', 0.9), decimate=0.45)
add('tl_lamp_post', 'quaternius/modular_streets::Streetlight_Single', TL, fit=('z', 3.9), decimate=0.6,
    recolor=rc({'v': (0.85, 1.0), 's': (0.0, 0.1), 'to': 'ffe9b0'}, {'h': (0.1, 0.2), 's': (0.5, 1.0), 'to': 'ffd36a'}))
add('tl_tree_citrus', USN + 'MapleTree_4', TL, fit=('z', 2.6), force_palette=True, quant=24,
    tex={'MapleTree_Leaves': 'ctlife_custom/CitrusTree_Leaves.png'})
add('houseplant_fern_lite', HI + 'Houseplant_5', TC, fit=('z', 0.6), decimate=0.5)
