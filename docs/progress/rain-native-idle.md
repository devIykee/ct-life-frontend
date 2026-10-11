# Rain native idle — 2026-10-06

The new Rain-specific idle passed Blender source, fresh-FBX and native Unity validation. Build L2 completed, but installation and Seeker visual acceptance remain pending because the device disconnected before installation. The source issue was the shared Snow idle placing Rain's fingers into her jeans in the [build-L creator capture](screens/quality-rain-20261006l/touch/rain-creator-body.png).

## Authored result and actual proof

[Animation bundle](../../art/character-candidates/rain-adult-production/animations/README.md), [front](../../art/character-candidates/rain-adult-production/animations/renders/front.png), [hand side](../../art/character-candidates/rain-adult-production/animations/renders/hand_side.png), [hand clearance](../../art/character-candidates/rain-adult-production/animations/renders/left_hand.png), [feet](../../art/character-candidates/rain-adult-production/animations/renders/feet.png).

These are actual Eevee renders of the authored action, inspected for visible finger/jeans separation and sole contact. They use embedded source materials; the separate artist-owned scarf map correction has its own proof. The action retains Rain's 55 canonical bones/rest matrices and geometry, keys all 30 digit joints, and adds restrained breathing over a closed six-second loop. No blink/gaze/face motion or improved walk/gesture quality is claimed.

| Source measurement | Result |
|---|---:|
| Frames / rate | 1–181 / 30 fps |
| Minimum hand-to-jeans lateral gap, all 181 frames | Left 18.55 mm; right 17.78 mm |
| Foot-joint drift | 0 |
| Source sole offset | −4.83e−9 m |
| Loop vertex delta | 0 |
| Fresh FBX canonical rest-matrix delta | 0 |
| Fresh FBX thirteen-phase maximum skin-matrix delta | 4.68e−6 |
| Fresh FBX validation | 14/14 checks pass |
| FBX size | 3,732,028 bytes |

Frozen source SHA256: `ad5dd2f98ae96ff899a7ba47b318fba75a4605dd1f9fa661bed8dbe7abc02005`. The included canonical skin reference mesh supplies the bind cluster; the game references the AnimationClip. Source creator/LOD1/LOD2 FBX hashes stayed unchanged. The first 74.5° arm-lowering candidate failed the measurable gap check; the final 72° pose passed. This was geometry-driven clearance adjustment.

## Unity handoff

Execute synchronously with normal batchmode and `-quit`:

```text
-executeMethod CTLife.EditorTools.RainIdleImport.ImportIdle
```

The importer reads `art/character-candidates/rain-adult-production/animations/RainAdult_RelaxedIdle.fbx` and its passing hash-matched report. It generates:

- `Assets/Art/characters/authored/RainIdle/RainAdult_RelaxedIdle.fbx` — isolated from ContentImport and the canonical Rain import folder.
- `Assets/Resources/Characters/RainController.overrideController` — one override for the unique shared `loco_idle` clip.
- Only `controllerOverride` on the exact Rain look entry in `CharacterRegistry.asset`.

Rain's own valid Humanoid Avatar is required; generic source inspection precedes Humanoid conversion. Import settings use metre/file scale, baked axes, six-second loop/in-place and Body Orientation. Gates check explicit finite rotation keys for all 30 source digit joints, converted finger muscles, and actual standing/grounded skinning over thirteen phases on both creator and every world LOD. The skin test requires more than 1 cm conservative lateral hand/thigh clearance, sole offset under 3 cm and foot drift under 2 cm. Invalid results throw and restore the prior Rain clip, override and registry. Shared controller bytes and every other registry field are checked for preservation.

Root's `CharacterRegistry.Entry.controllerOverride` and `AvatarFactory` selection route allow only the exact Rain entry to use this controller. No shared controller/prefab rewrite is performed here. A future ContentImport shared-controller rebuild deletes/recreates the controller asset; the existing Rain override must then be explicitly rebased or recreated and revalidated. Merely rerunning this importer will reject a different/missing existing base controller, by design.

Pinned Unity 6000.3.25f1 local XML/API references verified `AnimatorOverrideController.GetOverrides/ApplyOverrides`, constructor, `NewPreviewScene` and `SkinnedMeshRenderer.BakeMesh`. Current World plus isolated new importer compiled with the pinned Roslyn/reference assemblies: zero errors; two known out-of-editor warnings (response-file `noconfig`, Newtonsoft netstandard2.0→2.1 reference unification). This is a compile check, not an asset import.

Remaining root checks after the native retry: actual creator/front/side idle loop with fingers outside the jeans; world idle/sole contact; idle→walk/run→idle and gesture-return transitions. No Unity/editor/adb operation was run by this subagent.

## First Unity attempt and coordinate correction

The coordinating agent's first native import (`client/Logs/quality-rain-idle-20261006l2.log`) passed source digit and converted Humanoid gates: 30/30 digits, 40 muscle curves, six seconds. The skin-clearance gate rejected phase zero with a −0.4147 m gap; rollback restored the prior clip, override and registry.

The original clearance code assumed Blender's named left hand remained root-space +X after Unity Body Orientation normalization. That assumption was not valid as a general measurement basis. The revised validator projects vertices onto the actual RightUpperLeg→LeftUpperLeg horizontal direction. It retains the same >1 cm clearance threshold and adds a separate head→eyes navigation-forward gate (`face.z > .02`) so a backward character cannot pass by rotating the measurement basis. First-phase diagnostic logs report named hand/hip positions, face vector, lateral axis and both old-X/new-anatomical gap values. Native rerun is pending; no pose, FBX or frozen hash changed. The revised importer compiled with zero errors and the same Newtonsoft reference warning.

## Native retry passed; device result still pending

The [native retry log](../../client/Logs/quality-rain-idle-20261006l2-retry.log:564) confirms the coordinate cause: left hip X=−0.08162 m and left hand X=−0.22050 m, with anatomical left approximately −X. The face vector Z=+0.05846 m passes the independent navigation-forward check. At phase zero the old assumed-X gap was −0.41470 m while the anatomical gap was +0.02650 m. The first failure was an invalid measurement basis, not evidence of 41 cm of mesh penetration.

Native imported clip validation passed 30 source digit joints, 40 Humanoid finger-muscle curves and a six-second loop. Thirteen sampled phases on the creator skin measured minimum hand/thigh clearance **0.0243 m**; thirteen phases across all three world LOD skins measured **0.0242 m**. Both runs logged maximum sole offset and foot drift as **0.0000 m**. The importer then assigned the exact Rain registry entry's override and confirmed unchanged shared-controller bytes. These are logged numerical results; the rounding is retained as logged.

Root reports L2 built successfully: **157.2 MiB, zero errors, 29 warnings**, and the APK hash matches its receipt. Root also reports the final all-assembly `compile_check.py --editor` passed. Seeker disconnected before installation, so L2 is **built, not installed or device-reviewed** at this checkpoint.

## Narrow read-only pipeline audit

- The serialized `RainController.overrideController` contains one non-null clip override; every other clip inherits its shared motion. Only the exact Rain entry references it. Snow's override field remains null.
- `AvatarFactory.Create` chooses the entry override before the registry controller for both creator and world instances. `AvatarPreview.SetLook` then plays the existing `loco_idle` state; ordinary runtime crossfade/gesture-return code uses the same inherited state names. Canonical prefab controllers remain shared, intentionally; direct prefab instantiation outside AvatarFactory does not by itself exercise Rain's override.
- RainCharacterImport retains the existing entry instance, so its ordinary prefab/LOD refresh leaves the override field intact. A full ContentImport.SyncAll is a different path: it deletes/recreates the shared controller, requiring explicit override recovery/revalidation as described above. That future maintenance hazard is not a newly observed blocker in L2.
- `FrontendReviewController`, `ReviewTransport`, and both bootstrap review branches require `(UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD`. Android review extras additionally require DEVELOPMENT_BUILD; CTLIFE_PROD parsing returns before reading launch arguments. The sample-data label and opt-in engineering controls are entirely inside the guarded review controller. No review launch-argument route into a normal release build was found in this source review. No release binary execution was performed.

## Remaining game-feel checks, confined to this change

1. On the installed L2, inspect creator full-body/front/side over a complete six-second loop and rotate the preview: fingers must remain visibly outside jeans, soles grounded, with no snapped loop seam. Native samples support this but are not a substitute for the final rendered device result.
2. In-world, exercise idle→short analog walk→run→release→idle, turn, and wall stop with Rain. The new idle changes only one motion; shared locomotion cadence/speed and transition blending have not been newly accepted for Rain on L2.
3. Trigger a gesture and return to idle, then sit/stand: the Rain override preserves the shared gesture/seated clips. Passing native idle clearance does not certify hand contact during those other poses or their crossfades.
4. Inspect the separately corrected scarf material on that same installed build. Its import/artist proof passed separately; this idle work does not provide a device material acceptance result.

No additional code, Unity, adb or source animation changes were made during this audit.
