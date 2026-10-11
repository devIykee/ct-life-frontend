# Native orientation result — keep original B2 chair yaw

Correction: the earlier Blender-to-Unity sign inference was incorrect for these native imported meshes. The proposed B2Seats yaw 0° amendment is rejected. Its failed output and stage log are preserved. Original B2's 180° chairs have their backs behind the +Z-facing seated actor roots.

`CafeFurnitureOrientationReview.Capture` ran in one coordinator-authorized native Unity batch process, exit 0. It recorded **all four** yaw/model combinations before making a decision, and saved no scene or asset. The process has exited. Log: `/tmp/ct-cafe-native-orientation-20261006.log`. Receipt: `native-orientation.json`.

| Native imported furniture | Yaw | High-back mean world Z | High-back range Z |
|---|---:|---:|---:|
| Office chair | 0° | +0.3011 m | +0.1798 to +0.3758 m |
| Office chair | 180° | −0.3011 m | −0.3758 to −0.1798 m |
| Wooden chair, scale 1.08 | 0° | +0.2030 m | +0.1818 to +0.2269 m |
| Wooden chair, scale 1.08 | 180° | −0.2030 m | −0.2269 to −0.1818 m |

All 3,299 externally hashed protected source/environment, generated, original B2, failed B2Seats, Resources, ProjectSettings and package files remained unchanged. External receipt: `/tmp/ct-cafe-native-orientation-protection-20261006/after.json`. The only new live source was the narrowly authorized `Assets/Scripts/Editor/CafeFurnitureOrientationReview.cs` and its imported meta.

The earlier symmetric furniture AABB dimensions remain useful for source-level footprint screening; the sign mapping cannot be used to infer facing. Native imported geometry decides facing. This orientation finding does not accept the actual seated pose.

## Next exact probe: original B2

Use `CafeSeatPoseReviewB2.cs` (outside Assets), SHA256 `4b25245aef21f6c01b28659f94956766b3554ba9a969a14017a30d601af5387e`. It compiles exit 0 with the known CS1701 harness warning. This is the same diagnostic sampler, retargeted to the **unchanged** `Assets/Art/Candidates/TerminalCafe20261006B2/TerminalCafeB2.prefab`.

- Graphics method: `CTLife.EditorTools.CafeSeatPoseReviewB2.Capture`.
- Explicit geometry-only method: `CTLife.EditorTools.CafeSeatPoseReviewB2.CaptureGeometry`.
- Output: this folder's `native-b2-pose-review/`.
- Exact cases remain Snow/Rain × `terminal_cafe.table_1` with `act_sit` and `terminal_cafe.terminal_1` with `act_type`.
- Both exact source chairs remain yaw **180°**, unlike the historical B2Seats case table in the first README.

The existing B2 bake is available. No replacement prefab or rebake is needed to obtain this pose evidence. Review butt/seat contact, backrest placement, thighs/desktop, feet/floor and loop stability before deciding if any furniture or motion adjustment is necessary. The new pose probe has not run at this handoff; read the README's stated diagnostic limitations and connected-seat/runtime grounding gates.
