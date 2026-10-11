# Café B2Seats — failed orientation amendment, retained evidence

**Do not stage or promote B2Seats.** Its native stage failed with `Native chair high back is not behind north-facing anchor: terminal_1; mean relative z=0.3010979`. The deciding native assertion rejected the proposed yaw 0°. The failed candidate/log are retained.

Correction: the earlier Blender-to-Unity high-back direction inference used the wrong sign for the actual imported mesh. The original B2 yaw 180° may already be correct. `CafeFurnitureOrientationReview.Capture` records both native yaws for both source chairs; those results, followed by actual posed meshes, decide the next action. The rest of this document preserves the original handoff procedure as historical evidence, not current instructions to promote B2Seats.

The frozen B2 composition and bake remain intact. This new builder changes only the 12 claimed chair yaws to 0°; anchor positions/rotations, chair centers/scales, tables, materials, lighting and camera remain unchanged.

| Source | SHA256 |
|---|---|
| `TerminalCafeCandidateB2Seats.cs` | `66af656780ae32f4dd10e889a2bbe3c99e0c190d7130ef81e8ad7e6cd15faf57` |
| `CafeSeatPoseReview.cs` | `31b5578c28659629dc953f9ea18fab3d45628dc2ad9c7da932b2ed06794f930b` |

Both compile in the isolated `/tmp/ct-cafe-b2-seats-isolated/compile.rsp` harness, exit 0, known CS1701 warning. The pose helper initially had a `CompressionLevel` namespace ambiguity, resolved by qualifying `System.IO.Compression.CompressionLevel`. Current `CTLife.Editor.asmdef` already references URP/Core Runtime; no assembly definition or package change is needed.

## Coordinator procedure

1. When Unity is idle, stage both `.cs` files into the existing Editor assembly. Preserve external pre-startup/post-exit file hashes as with B2.
2. Run `CTLife.EditorTools.TerminalCafeCandidateB2Seats.BuildCandidate`. New output: `Assets/Art/Candidates/TerminalCafe20261006B2Seats/TerminalCafeB2Seats.prefab`.
3. Read `StageReceipt.json.native_imported_chair_back_checks`. Each of 12 entries must report actual imported LOD0 high-back vertices with mean relative Z less than −0.05 m. A failed inference stops stage. This is independent of the Blender coordinate mapping.
4. Run `CTLife.EditorTools.CafeSeatPoseReview.Capture` in an idle graphics-enabled native Unity process. It writes to `art/direction-20261006/terminal-cafe-b2/seating-amendment/native-pose-review/`. `CaptureGeometry` is a deliberate non-rendering alternative, not an image pass. A bake is not required for this isolated furniture/pose probe.
5. Inspect `receipt.json`, all four case JSONs, and front/side PNGs at three phases per case. The pose helper uses unsaved preview scenes, the actual current world prefab and effective registry controller, the exact anchor root/yaw, root motion off, 0.2 s crossfade, two warm loops, then 25 samples spanning a continuous full loop. Assertions check the actual state and normalized time, avoiding a frozen-pose “pass.” No source clips/controllers are replaced or edited.
6. Only after geometry/pose review, run `TerminalCafeCandidateB2Seats.BakeCandidate` and the normal gameplay camera/native movement review. The B2 art family has coordinator visual approval for device candidacy; seat correctness is a separate gate.

The probe hashes all venue/character dependencies and metas before/after. No global save, registry edit, source import change, temporary runtime controller or new anchor occurs. It creates neither a multiplayer connection nor a fake server-ownership claim.

## Exact cases

| Character | Spot | Root XYZ / yaw | Action | Chair center / yaw | Table center / height |
|---|---|---|---|---|---|
| Snow and Rain | `terminal_cafe.table_1` | (−6, 0, −4), 0° | `act_sit` | (−6, 0, −4.10), 0°, scale 1.08 | (−6, 0, −2.80), 0.75 m |
| Snow and Rain | `terminal_cafe.terminal_1` | (−7.5, 0, 3), 0° | `act_type` | (−7.5, 0, 2.90), 0° | (−7.5, 0, 3.86), 0.76 m |

`act_type` is the actual immutable work-seat action, so the work case must exercise it rather than substituting `act_sit`. Other social seats use x −2, +2, +6 at z −4. Work seats use x −7.5, −2.5, +2.5, +7.5 at z 3 and 7. All have root yaw 0°. The helper samples all world skin tiers; native PNGs force LOD0 for contact inspection. Compressed OBJ files include each posed skin and the actual chair/table triangles in the same world coordinates.

The probe derives the native upward seat plane from chair triangles and records weighted foot, torso and butt/thigh witness bounds. These are diagnostics, not a complete collision solver. A navigation margin does **not** show that a pelvis rests on a cushion, feet touch the floor, a back clears the chair, or knees/hands fit the table.

Review the entire loop for: butt/thigh support instead of hovering or sinking; backrest on the avatar's back side; no torso intersection with the chair back; stable foot/floor relation; no thigh/table underside intersections; work hands above the desktop with plausible reach; no chair/desk clipping when standing and leaving. Any observed mismatch requires a further furniture-only adjustment or motion investigation; do not move immutable anchors to hide it.

For runtime confirmation, `GoSit` walks to an anchor and sends `sit` only through a connected channel. Offline review cannot prove a server-confirmed seat merely by walking there. `OnServerSeat` then warps the NavMesh agent to the exact spot, applies its yaw, sets the action and refreshes grounding. A clearly marked local Editor fixture may call that method to inspect local behavior, but actual ownership/stand/re-entry and observer synchronization require a connected room. Do not report those network gates passed from this isolated sampler.

## Source evidence

`../chair-facing-audit.json` records source high-back vertices: the wooden chair's top 20% is at Blender Y +0.168 to +0.210 m; the office chair's at +0.180 to +0.376 m. Under the previously used Blender→Unity mapping (X, Z, −Y), that is a back at negative Unity Z before yaw. Native stage is the deciding orientation check.

Rendering uses the installed URP `SingleCameraRequest` and `RenderPipeline.SubmitRenderRequest` APIs, verified in the installed package and [Unity 6.3's render-request documentation](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/User-Render-Requests.html). Images are native Unity pose-diagnostic views with simple temporary lighting, not gameplay-camera or baked-room art evidence.

Native stage, pose capture, bake and runtime seat checks have not run for this source at handoff.
