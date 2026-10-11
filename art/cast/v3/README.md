# CT Life cast v3 (2026-10-10)

Status: art export validated in Blender (fresh FBX import). Not accepted in Unity, not seen on the device. Not production-ready.

## What v3 is
- **25 distinct bases, 12 F / 13 M**, all on the CTS skeleton (Unity-Humanoid-compatible names and hierarchy, `export/cts_humanoid_map.json`):
  - `umm_*` (10) and `umw_*` (9): Quaternius Ultimate Modular Men / Women (CC0). Each pack is one **family**: one skeleton bind pose,
    one 2048 atlas, one material (`umm_Mat`, `umw_Mat`). Every head / top / bottom / feet part swaps inside its family
    (`export/families/<fam>/parts/*.fbx`, gap rules in `<fam>.json` `compatibility`).
  - `ubc_m`, `ubc_f`: Quaternius Universal Base Characters (CC0), rebuilt from the v2 builder with the neckline, seam and brow fixes
    (`scripts/v3_ubc.py` monkey-patches `art/cast/v2/scripts`, which stay untouched).
  - `hero_assassin`, `hero_gynoid`, `hero_hazmat`, `hero_balaclava`: CC-BY 4.0 Sketchfab heroes ("rare masked looks"). v3 changes
    only their textures (`export/bases/hero_*/`); geometry/FBX stay the v2 files, sha256-pinned in each `hero_*.json`.
    `hero_tactical` is excluded (rigid placeholder hands).
- One toon material per character: Albedo + TintMask0 (R skin, G hair, B top, A bottom) + TintMask1 (R shoes, G accent, B brows, A hat).
  Tint formula, tint resolution and palettes are in `export/manifest_v3.json`.
- Starter accessories (pixel-face mask, visor, helmet, laser eyes, chain, headphones, small wings, shoulder drone) fitted per base,
  bound to the base material. TV head dropped.

## Files
- `export/manifest_v3.json`: bases, families, palettes, combos, presets (50/50 NPC roster), inventory with sha256, validation summary.
- `export/runtime_part_map_v3.json`: per base, what to instantiate (complete default, slots, options, accessories, sockets).
- `export/customizer_schema_v3.json`: proposed customiser data format (base, skin, parts, hair, tints, accessories) + rules.
- `export/validation_v3.json`: fresh-import receipt (`scripts/v3_validate.py`).
- `previews/`: EEVEE previews of the intended shader (not Unity output).
- `provenance/licence-verification-20261010.json`: live licence checks (quaternius.com, itch.io, Sketchfab API, Drive folder ids).

## Rebuild (Blender 5.1, headless, one process at a time)
```
cd art/cast/v3/scripts
./run.sh v3_family.py <log> umm      # ~7 min each
./run.sh v3_family.py <log> umw
./run.sh v3_ubc.py <log> ubc_m ubc_f
uv run -q v3_heroes.py               # hero texture pass (no Blender)
python3 v3_relook.py                 # spec looks/archetypes + per-part default tints (no Blender)
./run.sh v3_validate.py <log> --output <evidence dir>
python3 v3_manifest.py --validation <evidence dir>/validation_v3.json
python3 test_v3_manifest.py
python3 v3_jobs.py lineup combos custom closeups && ./run.sh v3_render.py <log> <job.json> && uv run -q v3_sheets.py lineup ...
```
Intermediates (blends, renders, logs) go to `../ct-life-evidence/cast-hermes-20261010/`.
