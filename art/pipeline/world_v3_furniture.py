"""Duo Shop furniture (2026-10-23): CC0 Poly Haven models for the shop catalogue (docs/fe-contract-proposals/shop_items.json).

  python3 -I art/pipeline/world_v3_furniture.py fetch     downloads every source into art/raw/polyhaven/<id>/ (ph_fetch.ph_model)
  art/pipeline/blender.sh art/pipeline/world_v3_process.py -- <ph3 ids, comma list>
      processes them as world_v3_config 'ph3_*' entries (family v3_furniture): decimated, one baked atlas per piece
  Unity: CT Life/Shop/Build Furniture Prefabs (ShopFurnitureBuilder) copies the FBX + textures and writes
      Resources/World/Furniture/<modelKey>.prefab, the key the catalogue's modelKey names.

Licence: https://polyhaven.com/license "Our assets are all licensed as CC0" (checked 2026-10-23). Picked for the clean
semi-real look of the walk-in apartment; branded pieces (Television_01 carries a maker's name) are left out.
"""
import os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ph_fetch  # noqa: E402

RAW_PH = os.path.join(HERE, '..', 'raw', 'polyhaven')
SOURCES = ['sofa_02', 'Sofa_01', 'mid_century_lounge_chair', 'modern_arm_chair_01', 'Ottoman_01', 'coffee_table_round_01',
           'modern_coffee_table_01', 'side_table_01', 'metal_office_desk', 'wooden_display_shelves_01', 'steel_frame_shelves_03',
           'modern_wooden_cabinet', 'potted_plant_04', 'ceramic_vase_02', 'industrial_pipe_lamp', 'desk_lamp_arm_01', 'boombox',
           'long_life_food']

if __name__ == '__main__' and sys.argv[1:2] == ['fetch']:
    root = os.path.abspath(RAW_PH)
    for m in SOURCES:
        ph_fetch.ph_model(m, root)
    print('[fetch] done', flush=True)
