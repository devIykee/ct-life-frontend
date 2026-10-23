"""World v3 kit: which Sketchfab download becomes which CT Life asset family (see world_v3_process.py).

uid -> dict(prefix, family, kind, mode, ...)
  family  output folder under art/processed/world_v3/
  kind    building | prop | tree | rock | animal | interior | vehicle
  mode    'cluster' (split into spatially separate assets) | 'whole' (one asset)
  yaw     optional fixed facade yaw for buildings (else: side with most detail faces -Y)
  skip    cluster indices to drop (brands, duplicates, broken bits)
  min_xy  ignore clusters whose footprint diagonal is below this (m)
Licence/credit text is filled from picks_meta (Sketchfab API) at run time.
"""

PACKS = {
    # ---------------------------------------------------------------- downtown / mid-rise (99.Miles: clean, low-poly, baked facades)
    'ec3905031a734cb88b4aa00f571c2deb': dict(prefix='m99_apt_complex', family='v3_buildings', kind='building', mode='cluster', height=34.0),
    '2305f0fe03ba44229a1f768dcb48bd8f': dict(prefix='m99_office_tower', family='v3_buildings', kind='building', mode='whole', lod0=20000, height=105.0),
    'c4a3993ef1e84e6d9d5a15c78da88aae': dict(prefix='m99_chicago_a', family='v3_buildings', kind='building', mode='whole', height=16.5, card=False,
                                             debrand=[('Image_2', (0.52, 0.415, 0.69, 0.462))], drop_mats=['Sidewalks']),   # 'FOUND' restaurant sign
    '950b3d8a998c41ecb06e6ba85da3e1f6': dict(prefix='m99_chicago_b', family='v3_buildings', kind='building', mode='cluster'),
    '8ef9555171c44ba1872a17558f589630': dict(prefix='m99_nyc_dwelling', family='v3_buildings', kind='building', mode='cluster', exclude=True),  # a whole street scene: one 130 m terrace mesh with road/sidewalk baked in; doesn't split into buildings
    '98faceefdc154d60a2b8617ca5e182e7': dict(prefix='m99_ny_building', family='v3_buildings', kind='building', mode='cluster'),
    'd96380fb001345cca9a9be121f3e43d5': dict(prefix='m99_corp_tower', family='v3_buildings', kind='building', mode='cluster', lod0=20000),
    # ---------------------------------------------------------------- residential
    # an enterable entrance (the walk-in apartment): 1.1 x 2.2 m through the facade and the backing box, at the central door
    'ace3194d157447548dbdb87d14122b18': dict(prefix='lg_midrise_flats', family='v3_residential', kind='building', mode='cluster',
                                             door=dict(width=1.1, height=2.2, through=True)),
    '0542fa5076ca4e5499e9cb0fbfeb00f7': dict(prefix='lg_astergea_flats', family='v3_residential', kind='building', mode='cluster'),
    '0d928b4f665e4803adec4e80cec70ac1': dict(prefix='lg_contemporary_apts', family='v3_residential', kind='building', mode='cluster'),
    '798ffaeadacf4e08a2665785422fb45d': dict(prefix='aa_american_house', family='v3_residential', kind='building', mode='whole'),
    # ---------------------------------------------------------------- harbour / industrial
    # the download is 247 x 108 x 43 m: a 15 m sawtooth warehouse (86 x 38 m, 3.2 m loading doors)
    'e6e105d35b3f486292c42ffa52f363c0': dict(prefix='m99_warehouse', family='v3_harbour', kind='building', mode='cluster', height=15.0, nonmetal=True),
    # cluster 25 is the pack's texture showcase wall (99 x 191 x 89 m), not a building
    'e0b0d0342be24e6c923319991a2a4d3d': dict(prefix='dz_industrial', family='v3_harbour', kind='building', mode='cluster', skip=[24], nonmetal=True),
    '772f4be391a245f699c54e6cf157c58d': dict(prefix='sh_container', family='v3_harbour', kind='prop', mode='whole', lod0=3000),
    '49851dc7a51b43bda6aea06856c26a85': dict(prefix='ch_tower_crane', family='v3_harbour', kind='prop', mode='whole', lod0=8000, lod1=2000),
    # ---------------------------------------------------------------- shops / storefronts
    # shop interiors (one 2k atlas with baked emissive), for the venue-interior pass: one 58 m 'prop' as it stands (needs
    # its real scale and a split per shop), so not processed yet
    '348da01c195d46eca29e478e98aab16c': dict(prefix='m99_storefront', family='v3_shops', kind='prop', mode='cluster', exclude=True),
    '3170993b67ee4e58b27d69d5877f71bd': dict(prefix='oz_brick_shop', family='v3_shops', kind='building', mode='whole'),
    '6ae7775d2b21417f8bb6aa3d821d294b': dict(prefix='lg_corner_shop', family='v3_shops', kind='building', mode='whole'),
    # ---------------------------------------------------------------- street furniture
    # by glTF node: the bounding-box clusters merged the hydrant into the bollard ring and the dumpster into its posts.
    # Not used: the wall ATM (mounted 1.2 m up) and the post box (a real postal service's livery).
    '32b8b82bb1e9489c993799c91967a916': dict(prefix='tj_city_prop', family='v3_props', kind='prop', mode='named', groups={
        'tj_trash_large': ['TrashCanLarge'], 'tj_trash_small': ['TrashCanSmall'], 'tj_trash_commercial': ['TrashCommercial'],
        'tj_trash_cap': ['TrashCanCap'], 'tj_crossing_sign': ['Crossing'], 'tj_post_barrier': ['PostBarrier'],
        'tj_drain': ['Drain'], 'tj_storm_drain': ['StormDrain'], 'tj_manhole': ['Manhole'], 'tj_tree_grate': ['Grate'],
        'tj_bench_metal': ['Bench'], 'tj_bench_concrete': ['ConcreteBench'], 'tj_bench_park_short': ['ParkBenchShort'],
        'tj_bench_park_long': ['ParkBenchLong'], 'tj_hydrant': ['FireHydrant'], 'tj_parking_meter': ['ParkingMeter'],
        'tj_utility_box': ['Utility'], 'tj_bus_stop': ['BusStop'], 'tj_pallet_a': ['pallet1'], 'tj_pallet_b': ['pallet2'],
        'tj_tarp_crates': ['Tarp', 'TarpCrate1', 'TarpCrate2', 'TarpCrate3', 'TarpCrate4'], 'tj_street_light': ['Light'],
        'tj_dumpster': ['Dumpster', 'LidLeft', 'LidRight']}),
    '27a6775c51e44bfda8e1ac8725eb3e9e': dict(prefix='sg_street_prop', family='v3_props', kind='prop', mode='cluster'),
    '8c984e73fa274c49a444f9991444d542': dict(prefix='ts_bus_stop', family='v3_props', kind='prop', mode='whole', lod0=4000),
    '6caa33dd3ae6463ab4b60fb15e53e2e1': dict(prefix='tj_dumpster', family='v3_props', kind='prop', mode='cluster'),
    '209e7dc032de483cae12eb5c3e7f1bcf': dict(prefix='tj_barricade', family='v3_props', kind='prop', mode='cluster'),
    'f2ed9f555ff64eae95b408409cbff602': dict(prefix='tj_fence', family='v3_props', kind='prop', mode='cluster'),
    '929f056abe02416da19d7b1aa805d235': dict(prefix='tk_sat_dish', family='v3_props', kind='prop', mode='whole'),
    '5c134526af464b5389f6c8a29fddeaff': dict(prefix='m99_construction', family='v3_props', kind='prop', mode='cluster'),
    '61c6f6f70db14bbb89ecdfa04b99bbc6': dict(prefix='m99_playground', family='v3_props', kind='prop', mode='whole', lod0=4000),
    # ---------------------------------------------------------------- vegetation
    # LOLIPOP maples ship LOD0-2 + billboard per tree (all 12 trees in one scene): pack_lods picks the medium trees at their
    # own 10-13.5 m and the large ones scaled to 12.5-14 m (street and park trees, 8-14 m)
    'b5d2833c258f4054a01ee2b4ef85adf0': dict(prefix='lp_maple', family='v3_trees', kind='tree', mode='whole', pack_lods=True,
                                             pick={'Acer_medium_1': None, 'Acer_medium_2': None, 'Acer_medium_3': None,
                                                   'Acer_large_1': 14.0, 'Acer_large_2': 13.5, 'Acer_large_3': 12.5}),
    '10312697ec994fc99355cb94f1963a2e': dict(prefix='lp_lilac', family='v3_trees', kind='tree', mode='cluster', lod0=2500),
    'd05c8ffb6ec64a7184c4b673e21f7224': dict(prefix='ns_palm', family='v3_trees', kind='tree', mode='cluster'),
    '5ff5a51e74324845a4e4905f182dfb2b': dict(prefix='m99_forest', family='v3_trees', kind='tree', mode='cluster'),
    '7e55084ba28e49adaa8c22a817863475': dict(prefix='ev_hedge', family='v3_trees', kind='tree', mode='cluster', lod0=1500),
    # ---------------------------------------------------------------- rocks / mountain
    'f3e0a67b9ca243b09119177649f21e17': dict(prefix='af_stone', family='v3_rocks', kind='rock', mode='cluster'),
    '75fb6b34dc0d46259dedc8c7cd94e7b7': dict(prefix='kw_cliff', family='v3_rocks', kind='rock', mode='whole', lod0=8000, lod1=2000),
    # ---------------------------------------------------------------- animals (rigged + animated: converted, not decimated)
    '9abfce885a834399b2c3ccaed51cd474': dict(prefix='an_dog_shiba', family='v3_animals', kind='animal', mode='whole'),
    '83c4290cd4b648fd942d4bbc2280a3f6': dict(prefix='an_snake', family='v3_animals', kind='animal', mode='whole'),
    '797d27b68af3453e865149435df6aa30': dict(prefix='an_pigeon', family='v3_animals', kind='animal', mode='whole'),
    '73aed843190a4dfda55f2b65cc0f8d63': dict(prefix='an_seagull', family='v3_animals', kind='animal', mode='whole'),
    '0b86bde7a8b64fd7a459035b339a790f': dict(prefix='an_cat_sphynx', family='v3_animals', kind='animal', mode='whole'),
    'd5a9b0df4da3493688b63ce42c8a83e2': dict(prefix='an_crow', family='v3_animals', kind='animal', mode='whole'),
    # ---------------------------------------------------------------- interiors (rooms <= 40k tris)
    '400c9069181a4342a7142433dfa3466e': dict(prefix='int_apartment_modern', family='v3_interiors', kind='interior', mode='whole'),
    'afb8cb0cbee1488caf61471ef14041e9': dict(prefix='int_living_white', family='v3_interiors', kind='interior', mode='whole'),
    '4f3db3cb57bd4bce886f7b9a13273a2f': dict(prefix='int_bedroom_minimal', family='v3_interiors', kind='interior', mode='whole'),
    '9ba7e0a094694335bd8f4656611c0676': dict(prefix='int_bathroom_modern', family='v3_interiors', kind='interior', mode='whole'),
    'df3f3c9f6233447eb8b7ee129f3bace5': dict(prefix='int_dining_kitchen', family='v3_interiors', kind='interior', mode='whole'),
    '5540183da1c7452f810f8de33734879a': dict(prefix='int_office', family='v3_interiors', kind='interior', mode='whole'),
    'a945f20219c44e94a77cf76929368470': dict(prefix='int_cafe_iso', family='v3_interiors', kind='interior', mode='whole'),
    'a6534141c33343b382dd23d519c3de73': dict(prefix='int_pub', family='v3_interiors', kind='interior', mode='whole'),
    'ae4e079225ee4982b61b048ea4c1081a': dict(prefix='int_shop_shelves', family='v3_interiors', kind='prop', mode='cluster'),
    '7f496b3002d24341a618408cb8221e8c': dict(prefix='int_wooden_house', family='v3_interiors', kind='building', mode='whole', card=False),
    # ---------------------------------------------------------------- vehicle (parked decor for lookdev; drivable cars are another lane)
    '8492aa13bc1246f6b589dca2887edb8d': dict(prefix='veh_hatchback_retro', family='v3_vehicles', kind='prop', mode='whole', lod0=12000, lod1=3000),
    # ---------------------------------------------------------------- Poly Haven models (CC0, already in art/raw/polyhaven; real-world scale)
    'ph_modular_street_seating': dict(prefix='ph_modular_street_seating', family='v3_props', kind='prop', mode='whole', path='polyhaven/models/modular_street_seating/modular_street_seating_1k.gltf', lod0=2000,
                   meta=dict(name='Modular Street Seating', display='Poly Haven', url='https://polyhaven.com/a/modular_street_seating', lic='CC0')),
    'ph_fire_hydrant': dict(prefix='ph_fire_hydrant', family='v3_props', kind='prop', mode='whole', path='polyhaven/models/fire_hydrant/fire_hydrant_1k.gltf', lod0=2000,
                   meta=dict(name='Fire Hydrant', display='Poly Haven', url='https://polyhaven.com/a/fire_hydrant', lic='CC0')),
    'ph_street_lamp_01': dict(prefix='ph_street_lamp_01', family='v3_props', kind='prop', mode='whole', path='polyhaven/models/street_lamp_01/street_lamp_01_1k.gltf', lod0=2000,
                   meta=dict(name='Street Lamp 01', display='Poly Haven', url='https://polyhaven.com/a/street_lamp_01', lic='CC0')),
    'ph_street_lamp_02': dict(prefix='ph_street_lamp_02', family='v3_props', kind='prop', mode='whole', path='polyhaven/models/street_lamp_02/street_lamp_02_1k.gltf', lod0=2000,
                   meta=dict(name='Street Lamp 02', display='Poly Haven', url='https://polyhaven.com/a/street_lamp_02', lic='CC0')),
    'ph_potted_plant_01': dict(prefix='ph_potted_plant_01', family='v3_props', kind='prop', mode='whole', path='polyhaven/models/potted_plant_01/potted_plant_01_1k.gltf', lod0=2000,
                   meta=dict(name='Potted Plant 01', display='Poly Haven', url='https://polyhaven.com/a/potted_plant_01', lic='CC0')),
    'ph_potted_plant_02': dict(prefix='ph_potted_plant_02', family='v3_props', kind='prop', mode='whole', path='polyhaven/models/potted_plant_02/potted_plant_02_1k.gltf', lod0=2000,
                   meta=dict(name='Potted Plant 02', display='Poly Haven', url='https://polyhaven.com/a/potted_plant_02', lic='CC0')),
    'ph_barrel_03': dict(prefix='ph_barrel_03', family='v3_props', kind='prop', mode='whole', path='polyhaven/models/barrel_03/barrel_03_1k.gltf', lod0=1500,
                   meta=dict(name='Barrel 03', display='Poly Haven', url='https://polyhaven.com/a/barrel_03', lic='CC0')),
    'ph_Sofa_01': dict(prefix='ph_sofa_01', family='v3_furniture', kind='prop', mode='whole', path='polyhaven/models/Sofa_01/Sofa_01_1k.gltf', lod0=4000, lod1=1000,
                   meta=dict(name='Sofa 01', display='Poly Haven', url='https://polyhaven.com/a/Sofa_01', lic='CC0')),
    'ph_sofa_02': dict(prefix='ph_sofa_02', family='v3_furniture', kind='prop', mode='whole', path='polyhaven/models/sofa_02/sofa_02_1k.gltf', lod0=4000, lod1=1000,
                   meta=dict(name='Sofa 02', display='Poly Haven', url='https://polyhaven.com/a/sofa_02', lic='CC0')),
    'ph_modern_arm_chair_01': dict(prefix='ph_modern_arm_chair_01', family='v3_furniture', kind='prop', mode='whole', path='polyhaven/models/modern_arm_chair_01/modern_arm_chair_01_1k.gltf', lod0=3000, lod1=800,
                   meta=dict(name='Modern Arm Chair 01', display='Poly Haven', url='https://polyhaven.com/a/modern_arm_chair_01', lic='CC0')),
    'ph_modern_coffee_table_01': dict(prefix='ph_modern_coffee_table_01', family='v3_furniture', kind='prop', mode='whole', path='polyhaven/models/modern_coffee_table_01/modern_coffee_table_01_1k.gltf', lod0=2000, lod1=500,
                   meta=dict(name='Modern Coffee Table 01', display='Poly Haven', url='https://polyhaven.com/a/modern_coffee_table_01', lic='CC0')),
    'ph_metal_office_desk': dict(prefix='ph_metal_office_desk', family='v3_furniture', kind='prop', mode='whole', path='polyhaven/models/metal_office_desk/metal_office_desk_1k.gltf', lod0=2000, lod1=500,
                   meta=dict(name='Metal Office Desk', display='Poly Haven', url='https://polyhaven.com/a/metal_office_desk', lic='CC0')),
}
