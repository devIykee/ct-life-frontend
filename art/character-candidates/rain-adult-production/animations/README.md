# Rain native relaxed idle

A Rain-only six-second breathing idle, authored from the frozen Rain runtime skeleton and canonical mesh. Thirty independent finger joints are explicitly keyed. It does not replace Snow's shared idle, change model geometry/rest matrices, or change locomotion/gesture clips.

Source / licence: [Rain Rig (CC) Blender Foundation | studio.blender.org](https://studio.blender.org/characters/rain/v3/), [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). CT Life derivative: adult runtime model conversion described in [the character README](../README.md), plus this native arm/hand/breathing animation. No additional downloaded animation is used.

## Evidence and files

- `RainAdult_RelaxedIdle.blend`: authored action, 30 fps, frames 1–181.
- `RainAdult_RelaxedIdle.fbx`: 3,732,028 bytes; SHA256 `ad5dd2f98ae96ff899a7ba47b318fba75a4605dd1f9fa661bed8dbe7abc02005`; take `RainAdult_Rig|Scene`.
- `idle-manifest.json`: 181-phase source geometry/ground/clearance measurements and preserved source FBX hashes.
- `idle-export-validation.json`: fourteen fresh-FBX checks passed, exact canonical rest matrices, maximum thirteen-phase skin-matrix error 4.68e-6, exact closed loop.
- `renders/front.png`, `left_hand.png`, `hand_side.png`, `feet.png`, `loop_001.png`, `loop_091.png`, `loop_181.png`: actual Eevee proof. Front/hand/feet images were inspected. Materials here are the embedded source reference; they do not represent the separate corrected Unity scarf map.

The minimum conservative lateral hand-to-jeans clearance in the authored source is 17.78 mm. Foot-joint drift and loop vertex delta are zero; source soles remain at zero within 5e-9 m. These source measurements are not Unity Humanoid retargeting acceptance. The importer samples the actual imported creator and all world LOD skins before publishing the Rain entry override. Device review remains required.

The FBX intentionally includes one canonical skin reference mesh for reliable bind matrices; runtime uses its AnimationClip subasset. Canonical character exports, LODs and the shared controller are not overwritten.

## Reproduce

With Blender 5.1.2, embedded scripts disabled, run `build_rain_relaxed_idle.py`, `export_validate_rain_idle.py`, then `review_rain_relaxed_idle.py` from `../../scripts/`. Each CLI invocation uses `--python-exit-code 1`. Re-authoring invalidates the frozen SHA and requires new validation.

Unity import (root owns the editor): `-executeMethod CTLife.EditorTools.RainIdleImport.ImportIdle`. See [the handoff report](../../../../docs/progress/rain-native-idle.md) for generated paths and remaining checks.
