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
art/pipeline/blender.sh art/pipeline/char_preview.py -- male art/previews/characters_male_lineup.png
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
- Previews: `art/previews/characters_male_lineup.png`, `characters_female_lineup.png`,
  `characters_npc_phase1.png`, `characters_npc_more.png`.
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
