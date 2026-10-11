# ASSETS agent — progress log

Owner folder: `art/` only. All Blender work is headless Blender **5.1.2** (`/Applications/Blender.app`).
Raw downloads live in `art/raw/` (gitignored). Pipeline scripts: `art/pipeline/`.

## 2026-10-05 — Milestone 1: character solution chosen + built (in progress)

### Decision: MPFB 2 (MakeHuman, CC0) bodies + Quaternius UAL animations on one UE-mannequin-named skeleton
Researched on live pages today:
- **Quaternius Universal Base Characters** (quaternius.com/packs/universalbasecharacters.html, itch.io
  `quaternius/universal-base-characters`): exists, CC0, ~13k tris, humanoid rig. **Free "Standard" tier =
  only 2 bodies in "Superhero" proportions (bodybuilder male) + 5 hairstyles + beard + 2 eyebrows**; the
  Regular/Teen bodies need the paid Source tier ($19.99) -> body rejected for the Sims look, but its 6
  opaque stylised **hair meshes are used** (fitted onto our heads).
- **Quaternius Universal Animation Library 1 + 2** (CC0, Standard tier free, 43 clips each in the free FBX,
  30 fps, same UE-mannequin bone names as UBC). Used for 47 clips.
- **MPFB 2.0.17** (extensions.blender.org/add-ons/mpfb, released 2026-07-22, Blender >= 4.2) works headless in
  Blender 5.1.2 (verified: human creation, proxies, clothes fitting, game_engine rig). Asset packs from
  static.makehumancommunity.org/assets/assetpacks.html — every pack used is the `_cc0.zip` and every asset
  in it is listed as CC0 (MakeHuman licence page: assets CC0; MPFB code GPL — the code is not shipped).
- Kenney/KayKit characters not needed (chibi/blocky, wrong look).

Why MPFB: real human proportions (Sims-like), 94 CC0 garments/35 hairs/skins, clothes are fitted and
skinned automatically, low-poly body proxies (~3.1k tris), and its `game_engine` rig uses the same
Unreal-mannequin names as Quaternius UAL -> clips map 1:1.

### Isolated Blender environment
`art/pipeline/blender.sh <script.py> -- args` runs Blender with `BLENDER_USER_RESOURCES=art/raw/blender_user`
(a copy of MPFB 2.0.17 + the CC0 asset packs). The user's own Blender config is not touched.

### Character pipeline (run in this order)
```
art/pipeline/blender.sh art/pipeline/char_build.py  -- male|female   # MPFB human + all garments -> CTL skeleton, T-pose
art/pipeline/blender.sh art/pipeline/char_custom.py -- male|female   # our procedural CT pieces (vest, coat, hard hat...)
art/pipeline/blender.sh art/pipeline/char_hair.py   -- male|female   # fit Quaternius hairs onto the head
art/pipeline/blender.sh art/pipeline/char_export.py -- male|female   # textures, LODs, region-split body, FBX, manifest
art/pipeline/blender.sh art/pipeline/char_npc.py                     # 1-mesh/1-atlas NPC presets
art/pipeline/blender.sh art/pipeline/anim_retarget.py -- male        # UAL clips -> CTL skeleton FBX
art/pipeline/blender.sh art/pipeline/char_preview.py -- male art/previews/characters_male_lineup.jpg
```
(`char_custom.py` expects a fresh `char_<sex>_master.blend`; copy `char_<sex>_master_base.blend` first if re-running.)

### Milestone 1 result (2026-10-05) — characters: DONE (first pass)
- `art/processed/characters/{male,female}/` — body FBX (24 regions x LOD0/1/2 + eyes; LOD0 2.5k tris) +
  45/46 modular part FBX (each LOD0/1/2, skinned to the same 30-bone skeleton) + texture tiles + `manifest.json`
  (slot, variants, tris, which body regions a part hides, hair_mode for hats, emissive variants).
- 6 skin tones, 5 eye colours, 8 hair colours (tint variants), 13 male / 12 female preset outfits (`ctl_config.OUTFITS`).
- `art/processed/characters/npc/` — 22 ready NPCs (Phase-1 nine + 13 more), each ONE skinned mesh per LOD +
  ONE 1024 atlas; LOD0 4.9–7.2k tris, LOD1 3.3–4.6k, LOD2 1.8–2.3k.
- `art/processed/animations/` — 92 clips (48 retargeted Quaternius UAL1/UAL2 + 44 hand-keyed by script), 30 fps,
  in place, `clips.json` index. Each clip FBX carries a 1-triangle `_bind_proxy` mesh so importers get the
  T-pose bind pose (without it Blender takes frame 1 as rest). Unity: Rig = Humanoid, Copy From Other Avatar.
- Previews: `art/previews/characters_male_lineup.jpg`, `characters_female_lineup.jpg`,
  `characters_npc_phase1.jpg`, `characters_npc_more.jpg`.
- Known issues: slight skin poke-through at tee armpits in arms-down poses on modular avatars (NPC bakes pull
  the covered skin in); female sleuth coat reads a bit like a robe; tee coverage only hides `belly`.

### 2026-10-05 (resumed after stall) — resource rule + Unity request
- The run stalled because the Mac was overloaded (load average ~430 while the Unity editor builds). From now on
  every Blender job goes through `art/pipeline/run.sh <script> -- args`: one job at a time (lock file),
  `timeout 1800`, full log in `art/raw/logs/<script>_<time>.log`, summary lines echoed.
- Checked art/processed: all character exports, 22 NPCs and 92 clips had completed; env kits done so far:
  timeline_square, terminal_cafe, props, nature (cars re-scaled).
- Unity request done: added `act_host_mic`, `act_listen_nod`, `emote_moon_jump`, `react_sad` and
  `art/processed/animations/clip_map.json` (all 46 contract anim ids mapped). 96 clips total.

### 2026-10-05 — Milestones 2+3: environment kits DONE (all Phase 1–2 venues + home + city map)
Sources (all CC0, verified on live pages by a research sub-agent; inventory `art/raw/env_inventory.json`):
Kenney (City Kit Commercial/Suburban/Roads/Industrial, Factory, Furniture, Food, Mini Market, Car),
Quaternius (Ultimate Textured Buildings, Stylized Nature MegaKit, Ultimate Stylized Nature, Ultimate Nature,
Ultimate Home Interior, Ultimate Furniture, Sci-Fi Essentials, Cyberpunk, Modular Streets, Downtown City streets),
KayKit (Restaurant, Furniture, City Builder, Resource Bits), Poly Haven 1k textures. Rejected: poly.pizza
(aggregator, uploader-declared licence), Downtown City buildings (18–45k tris, 12 materials each).
Our own procedural pieces (`env_custom.py`, CC0): blank billboards x3, Pixel Wall, ad kiosk, sandwich board,
trading terminal, 16:9 wall screen, triple monitor, trading desk, server rack, neon frames, hospital bed, IV
stand, vitals monitor, privacy curtain, tombstone, stage, podium, mic stand, speaker, studio softbox light,
ON AIR sign, podcast table, ORE furnace (glow), 5x5 ORE board frame, stake terminal, ORE crystal pile,
fountain, park bench, bollard, crypto ATM, news ticker, and a 2 m interior kit (wood/tile/marble/concrete/
metal floors, plaster/brick walls with window/door variants, Poly Haven textures).

Pipeline: `env_custom.py` -> `env_process.py` (config `env_config.py`): import, metres, origin bottom-centre,
flat colours (and Kenney/Quaternius 32px colour maps) baked into ONE shared city palette
(`environment/_shared/ctl_palette.png`, material `M_ctl_palette`), real textures re-saved <=512 px (1024 for
buildings) with one material per texture (`M_<texture>`), Kenney factory/industrial lavender recoloured to
steel (`*_steel.png`), pink blossom = tinted Quaternius maple leaves, LOD1/LOD2 by decimation, FBX with Apply
Transform (static meshes only). `env_preview.py` = kit sheets, `env_diorama.py` = venue mock-ups.
Output: 265 assets; LOD0 medians 120–1400 tris (largest: 4-storey shop 12.6k -> LOD1 6.3k, LOD2 2.5k).

Previews (JPEG): art/previews/characters_{male,female}_lineup.jpg, characters_npc_phase1.jpg,
characters_npc_more.jpg, anims_locomotion_sit_work.jpg, anims_emotes_liquidation.jpg,
venue_timeline_square.jpg, venue_terminal_cafe.jpg, kit_{timeline_square,terminal_cafe,perp_pit,rekt_clinic,
spaces_studio,ore_refinery,home,city_map,props,nature}.jpg

Animation fix: all clips now keep the body above the floor (whole-pose floor clamp using bone flesh radii;
our bodies are thicker than the UAL mannequin, so Death01/LayToIdle sank before). Re-exported all 96 clips.

### READY FOR UNITY IMPORT (Phase 1–2)
Import these folders (keep the relative layout; manifests use paths relative to art/processed):
- `art/processed/characters/male/`, `art/processed/characters/female/` — `body/ctl_<sex>_body.fbx` (avatar
  source; T-pose, 30 bones) + `parts/*.fbx` + `textures/*.png` + `manifest.json`.
- `art/processed/characters/npc/` — 22 single-mesh NPC FBX + 1024 atlas PNG each + `manifest.json`.
- `art/processed/animations/` — 96 clip FBX + `clips.json` + `clip_map.json` (contract anim id -> file).
- `art/processed/environment/` (all venue folders + `_shared/` palette and textures + `manifest.json`, which
  also covers props and nature), `art/processed/props/`, `art/processed/nature/`.
Settings: Scale Factor 1 with Convert Units (exported FBX_SCALE_ALL, metres); tick Bake Axis Conversion
[Unverified for Unity 6.3: option name from earlier Unity versions]; characters Rig = Humanoid, body FBX
creates the avatar, parts + clips = Copy From Other Avatar (male clips also drive the female avatar through
Humanoid retargeting); clip FBX include a 1-triangle `_bind_proxy` mesh (ignore/strip). Environment: Rig None,
Generate Lightmap UVs on (one UV set exported). `ctl_palette.png`: Point filter, mipmaps off (16 px swatches).
Materials named `KEEP_*` are content surfaces with 0–1 UVs: ad_slot (game paints ads/Pixel Wall/tombstone
names), screen (terminals, ORE board, ATM), neon, furnace_glow / ore_glow (drive emission from city-wide staked
ORE), glass, water, softbox, on_air. Materials with `ctl_alpha_clip` (leaves, grass, flowers) need Alpha
Clipping. Character parts: one texture tile per part/variant, pack per avatar at runtime into one atlas (NPCs
are pre-packed); `emissive_variants` in the manifests list tiles that should also drive emission.
