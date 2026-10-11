# Native Snow idle import — 2026-10-05

## J import result

Root executed the importer successfully. [The Unity log](../../client/Logs/quality-idle-20261005j.log)
records 30/30 source digit joints, 560 source bindings, 40 converted finger-muscle curves,
and a 6.000-second Humanoid clip. Both Snow prefab numeric checks passed; 45 other controller
states were preserved. Final source SHA-256:
`f0684daf3901de9c1871c10bd09734e7feff98261e6ab069506567117625034a`.
The final FBX includes a canonical skin-bind reference mesh. The source roundtrip report
passed after the armature-only candidate described below was replaced. Only the imported
clip is used by the runtime controller. Device visual approval remains separate.

## Implementation and original handoff

`client/Assets/Scripts/Editor/AuthoredIdleImport.cs` is a targeted Editor importer for the
artist's separate `art/character-candidates/unity-snow/animations/SnowAdult_RelaxedIdle.fbx`.
It copies that file into `Assets/Art/characters/authored/SnowIdle/`, outside the character
model importer's `Snow/` folder and within the existing generic importer's authored-content
exclusion. It changes only `loco_idle.motion` in the existing shared AvatarController.
It does not regenerate the controller, character meshes, maps, LODs, registry or world.

The source roundtrip report must have `all_pass: true` and the exact source FBX SHA-256.
At the first source inspection, the artist's armature-only export failed canonical rest and
sampled skinning equivalence. The artist is correcting that export; the importer rejects
that failed report before touching Unity assets. No native import or device acceptance has
been claimed here.

Import checks:

- First import as Generic with compression disabled and check rotation curves on every one
  of the 30 explicitly named finger joints. Then convert the same asset to Humanoid with
  `CopyFromOther`, referencing **Snow's own valid CreateFromThisModel Avatar**.
- Apply metre scale, file units and baked axis conversion. Preserve the FBX take/frame
  range; rename the resulting clip `SnowAdult_RelaxedIdle`. Enable six-second loop/loop
  pose, lock root rotation/height/XZ, and use Body Orientation.
- Require a non-legacy `humanMotion` clip, finite Humanoid curves with actual motion, and
  a converted muscle binding for each of the 30 digit joints. Match binding names against
  the installed Unity `HumanTrait.MuscleFromBone` / `MuscleName` results and log the actual
  names; no guessed finger-curl threshold is used.
- Snapshot every controller state's saved identity and motion. Require the controller GUID
  and every non-idle state's motion to remain unchanged. Call the existing
  `CharacterValidation.ValidateSnowAsset` on both creator and world prefabs.
- On failure, restore the previous imported idle FBX/meta and saved controller bytes. This
  also covers reruns where the previous idle already references this same FBX. Unsaved
  controller edits are rejected before starting, rather than overwritten.

Root-owned Unity invocation (the method is synchronous, so `-quit` is appropriate):

```sh
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit \
  -projectPath /Users/mac/Documents/codes/projects/ct-life/client \
  -executeMethod CTLife.EditorTools.AuthoredIdleImport.ImportIdle \
  -logFile /Users/mac/Documents/codes/projects/ct-life/client/Logs/snow-native-idle-import.log
```

Run after `ContentImport.SyncAll`: that existing full-content method recreates the shared
controller from the original animation registry and would otherwise replace this idle.
The new helper does not alter that existing importer.

Source verification: checked the installed Unity 6000.3.25f1 Editor/Animation module API
documentation and compiled all nine current Editor C# files using its Roslyn compiler and
the existing project assembly references. Result: **9 files, compiler exit 0**. Scratch
output: `/var/folders/zm/v0w94xb95p11_5df6rxm0js80000gp/T/ct-native-idle-compile-ktqz2xjt`.
This verifies C# API compatibility, not Unity's native importer or retargeting. Root must
run the method and inspect the imported hand pose, stance, loop seam, and movement
transitions on device. Existing numeric validation logs contain a historical statement
about legacy CTL clips lacking finger expressions; the new idle's explicit binding log
supersedes that statement for this one clip only.
