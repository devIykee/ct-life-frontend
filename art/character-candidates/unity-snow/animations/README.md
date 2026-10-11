# Snow — one relaxed idle

One separate six-second idle authored for the canonical adult Snow rig. It replaces the legacy clip's broad stance and C-shaped hands when the coordinating Unity agent imports it successfully. Model, atlas and LOD exports remain byte-identical.

- [Animation FBX](SnowAdult_RelaxedIdle.fbx), [editable source](SnowAdult_RelaxedIdle.blend), [54-bone Humanoid mapping](unity-humanoid-map.json).
- [Actual pose proof](idle-pose-proof.jpg), [five loop samples](idle-loop-samples.jpg), [six-second Blender preview](SnowAdult_RelaxedIdle_SourceLoop.mp4).
- [Source measurements](idle-manifest.json), [fresh-FBX verification](idle-export-validation.json).

The source action is `SnowAdult_RelaxedIdle`. The exported FBX take imports in Blender as `SnowAdult_Rig|Scene`: **frames 1–181, 30 fps, exactly 6 seconds**. The final FBX SHA256 is `f0684daf3901de9c1871c10bd09734e7feff98261e6ab069506567117625034a` (2,302,380 bytes).

All 55 bones have explicit sampled transform keys; all thirty independent digit joints have actual rotation variation. The fingers have a gentle progressive curl rather than the legacy mitten/claw shape. Arms hang near the torso with neutral wrists. Source ankle width is **0.172976 m**, compared with hip-joint width **0.170425 m**. Feet stay fixed while small spine/chest/neck rotations carry a quiet breathing cycle. No facial, blink, gaze, mouth or lip-sync animation is claimed.

## Import reference and the rejected export attempt

Import as Humanoid with **Snow's own corrected Avatar**, not the old CTL body Avatar. The coordinating importer verifies all thirty raw digit rotation bindings and the resulting Humanoid finger-muscle curves before replacing only the `loco_idle` motion. Other locomotion/emote clips remain separate.

The FBX deliberately includes the **unchanged canonical skinned mesh as a bind reference**. An armature-only FBX exported while posed made its first idle pose the importer's rest pose. A non-exported canonical T-pose preroll reduced that ambiguity but still differed from the canonical skinned FBX reference. Retaining the real skin cluster gives **exactly identical canonical rest matrices** after fresh import. The source's frame-zero reference is excluded from the exported scene range 1–181. Use only the imported AnimationClip at runtime; do not add this reference renderer to the player.

## Verified scope

All thirteen export checks pass. The fresh FBX retains the same 55 names, parents and canonical rest matrices (maximum rest matrix delta **0**). Thirteen sampled animation skinning matrices match the authored source within **2.51e-6**. The first and final pose matrices match exactly. All thirty digit rotation tracks exist and vary through the loop. Across every one of the 181 source frames, foot-joint drift is **0** and the evaluated sole remains at approximately **3.7e-9 m**, numerical zero.

Actual source renders were inspected from front, side, three-quarter/back, both hand views and feet. The reviewed hands are open with soft bends and remain clear of clothing. These Blender proofs are not device captures. Unity's finger-muscle interpretation, final framing and actual device motion still require the coordinating agent's check. Successful Avatar mapping alone is not animation acceptance.

Reproduce with `scripts/build_snow_relaxed_idle.py`, `scripts/review_snow_relaxed_idle.py`, and `scripts/export_validate_snow_idle.py` through the existing Blender 5.1.2 background command with embedded scripts disabled. They write only this separate animation directory. The exporter records hashes of all six canonical import files and verifies they remain unchanged.

**Snow Rig (CC) Blender Foundation | studio.blender.org** — [source](https://studio.blender.org/characters/snow/v4/), [CC-BY 4.0](https://creativecommons.org/licenses/by/4.0/). Adult mesh/rig adaptation and this idle pose/curve authoring: CT Life. Attribution does not imply endorsement.
