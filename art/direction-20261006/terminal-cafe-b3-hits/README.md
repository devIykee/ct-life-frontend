# Café B3: physical hit geometry and explicit seat selection

Candidate sources only. No file in `client/Assets` was changed by this staging work. Original B3 Social remains the comparison artifact. Workstation fitting, baked lighting and venue registration remain separate gates.

## Concrete defect and bounded change

The previous builder used the same 2.4 m tall obstacle bounds for both navigation exclusion and runtime BoxColliders. A camera ray could therefore strike empty space above a chair/table. WorldInput then compared that hit to a floor-level seat anchor using a 0.9 m three-dimensional radius. Even a real high chair back can exceed that radius.

`TerminalCafeCandidateB3Hits` retains the original navigation bounds but gives sourced solid props their actual static LOD0 MeshCollider. Authored joinery uses its actual box dimensions. Each of the 12 chair/table pairs is parented, with world transforms preserved, under its existing immutable SpotAnchor. No new synced IDs or altered anchor positions/rotations are introduced.

`WorldInput.cs` adds an explicit SpotAnchor-ancestry lookup for the first non-trigger solid hit before the existing proximity fallback. Avatar priority, ray layers, trigger handling, legacy venue fallback and transport behavior remain unchanged. The tag resolves selection; LocalPlayerController still owns its complete-path/arrival/claim flow.

## Import and native procedure

Coordinator owns the Unity slot and live imports. Verify `baseline-sha256.json` against current files before replacing WorldInput; if the source differs, review/rebase the eight-line patch instead of overwriting another change.

- Copy `WorldInput.cs` to `client/Assets/Scripts/World/Input/WorldInput.cs`.
- Copy `TerminalCafeCandidateB3Hits.cs` and `CafeSeatHitReview.cs` to `client/Assets/Scripts/Editor/`.
- In a scoped Editor process run `CTLife.EditorTools.TerminalCafeCandidateB3Hits.BuildCandidate`. This writes only the new `Assets/Art/Candidates/TerminalCafe20261006B3Hits` output, narrow saves and a protection receipt. Review its 17 exact anchors, 12 authored approaches and complete navigation paths. Recheck protected hashes after Editor exit; package virtual paths resolve through the installed PackageCache.
- Launch a saved scene using the coordinator's `-openfile` Bootstrap procedure, then `-executeMethod CTLife.EditorTools.CafeSeatHitReview.Launch -ctlife-cafeHitOutput /tmp/ct-cafe-seat-hit-native-20261006 -ctlife-cafeHitExit 1`. Do not use `-quit`: the asynchronous fixture exits after Play mode and scene restoration. It refuses unsaved scenes and waits for import/Search initialization. Timeout is 180 seconds.
- Require `receipt.json` success, `protected-drift.json` empty, no Error/Exception/Assert logs, then review all ray records. It selects 24 actual source-triangle points (a high back and tabletop for each seat) from the candidate's actual 16:9 CameraRig at that seat's standing approach. Every ray calls real WorldInput.Tap and must select exactly its immutable spot. High-back hits must exceed the legacy 0.9 m radius. It then replays identical geometry points/cameras on original B3 Social and requires at least one missed back to reproduce the prior defect.
- Bake is unnecessary for the physics-only fixture. Later, only after final furniture/body acceptance, the isolated method is `CTLife.EditorTools.TerminalCafeCandidateB3Hits.BakeCandidate`. Do not register this intermediate furniture version.

## Source validation

`compile-receipt.json`: World Editor and Android player plus both Editor helpers compiled with the actual direct project assembly references. Editor compilation has the existing Newtonsoft/netstandard CS1701 compatibility warning. This is source compilation, not native execution or Android validation.

The fixture reads non-readable imported meshes with the installed public Editor snapshot API, without changing importer Read/Write settings. [Unity 6.3 MeshUtility.AcquireReadOnlyMeshData](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/MeshUtility.AcquireReadOnlyMeshData.html), [MeshData.GetVertices](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh.MeshData.GetVertices.html), and [MeshData.GetIndices](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh.MeshData.GetIndices.html) document the used APIs.

## Acceptance boundaries

No native fixture execution, touch dispatch, walking through this actual room, server claim/occupancy, runtime seated animation, lightmap bake, registry promotion or phone measurement is claimed by this source freeze. Actual static MeshCollider cooking and Android startup/frame cost must be measured; these mesh colliders do not replace navigation obstacles or imply rigid-body seating contact. The fixture intentionally contains no direct ClaimSeat shortcut and no synthetic server confirmation. Its baseline control uses the improved WorldInput runtime too, isolating whether the old furniture lacks truthful geometry/explicit ancestry.

Social chair/body contact evidence is retained in `../terminal-cafe-b3-fit/native-social-fit-review/`; original work desks/chairs remain B2 geometry here pending the combined typing fit. The modest social-table composition change is also deliberately separate from this hit-selection revision.
