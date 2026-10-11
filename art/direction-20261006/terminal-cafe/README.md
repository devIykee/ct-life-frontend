# Terminal Café — isolated second venue

[Inference] This authored interior is a candidate requiring native visual, seated-animation and device review. It is not a published venue or completed market integration.

Builder: `client/Assets/Scripts/Editor/TerminalCafeCandidate.cs`.

Output: `Assets/Art/Candidates/TerminalCafe20261006/TerminalCafe.prefab`.

- Stage: `CTLife.EditorTools.TerminalCafeCandidate.BuildCandidate`.
- Bake: `CTLife.EditorTools.TerminalCafeCandidate.BakeCandidate`.
- Request **`terminal_cafe`** as the review venue when passing that prefab to the coordinator's candidate override.

The room uses the exact 24×18 m walkable rectangle in the current FE config snapshot. Eight existing `act_type` terminal anchors each receive a desk, chair, triple monitor, laptop, coffee cup and desk lamp. Four existing social `act_sit` anchors receive chairs and furnished round tables. The counter/barista anchor retains an access lane behind a furnished coffee counter.

The back brick wall holds a large Pulse display and café identity sign. The left wall contains open windows; panes are removed from copied meshes only so the outside garden is visible. Front/right walls are low cutaways. Wooden floor, tiled coffee apron, and wall meshes are combined by material before baking. Three visible pendant fixtures use editor-baked point lights, not mixed/realtime lights. The baked-light API was checked in the [Unity 6.3 reference](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Light-lightmapBakeType.html).

`source-assets.json` records 31 referenced asset IDs, their existing source provenance and triangle/texture metadata. All are already present in the imported environment manifest and covered by the existing art licence records; no additional model download occurred. The original `art/lookdev/terminal_cafe.blend` and render were inspected as composition references and remain unchanged.

The builder validates 8 terminal anchors, 4 social anchors, unchanged positioned coordinates/rotations and complete paths from entry to every positioned spot. It rejects a changed room rectangle rather than silently adapting server geometry. It writes no venue registry entry, backend file or contract change. Dynamic market data and server-connected seating are existing client/server concerns and are not inferred from decorative monitors or correctly positioned chairs.

Use the same coordinator-only external byte baseline, candidate-only save, separate bake, and post-Unity-exit verification procedure as [the Timeline candidate](../README.md). Its backup receipts are under `/tmp/ctlife-terminal-cafe-<phase>-<id>/transaction.json`. Inspect both the prefab and Stage/Bake receipts before native review. The internal baseline begins after Unity startup.

Required native checks: confirm each chair faces its desk and supports the authored seated pose; inspect hands/desktop intersections; verify room entry/exit and all 17 positioned spots; inspect through-window scenery and front cutaway; check lightmaps after travel out/back. No APK or thermal claim is made from the source manifest estimates.

Source status at handoff: generated and asset references checked. A shared compile attempt stopped in concurrent `FrontendReviewController.cs` at a missing `EventMessage` namespace, which root subsequently reported corrected. Root owns the next authoritative compile/native run to avoid simultaneous writers to the compile cache.
