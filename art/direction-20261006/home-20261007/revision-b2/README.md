# Home B2 — conditioned display-plane classification

Staged narrow correction only. Failed Home B, its receipt and original source remain preserved. No new art/layout/geometry scope.

B's native stage stopped at `Display plane is not a filled source rectangle: Laptop / original personal notes`. The exact native laptop mesh reproduced this failure with float32 world positions: recomputing each small triangle's normal and comparing its world-origin plane distance split one screen into three groups. The largest partial group measured 0.0637631 m² against a 0.0712866 m² projected rectangle, a 10.5539% mismatch. This was not evidence of a missing source screen or permission to relax the area check.

B2 evaluates all three triangle vertices against the existing plane's normal **relative to a vertex on that plane**. The angular tolerance, 20 µm coplanarity threshold and 3.5% rectangle-area threshold are unchanged. Exact serialized-mesh CPU reproduction joins the screen area to 0.0711582 m²; maximum residual is 0.708 µm and the existing area gate passes at 0.1801%. `laptop-plane-diagnosis.json` and its read-only script preserve this proof. Native B2 still must confirm the result. The helper now records native `DisplayPlaneClassification.json` before the area assertion, including every measured area/residual.

The failed B post-Editor audit checked all 10,706 captured existing protected files: zero changed/missing. This does not claim no later root-added paths. See `failed-b-post-exit-audit.json`.

Only import `ParentsBasementCandidateB2.cs` to a new absent `client/Assets/Scripts/Editor/ParentsBasementCandidateB2.cs`. Distinct class and output retain B. Methods:

- `CTLife.EditorTools.ParentsBasementCandidateB2.BuildCandidate`
- `CTLife.EditorTools.ParentsBasementCandidateB2.BakeCandidate`

Output: `Assets/Art/Candidates/ParentsBasement20261007B2/ParentsBasementB2.prefab`.

All original B source manifests/textures remain in `../revision-b/`, frozen and read by the helper. Original six anchors, four approaches, furniture/colliders/navigation/camera/lights, material and triangle ceilings are unchanged. The [B README](../revision-b/README.md) remains the complete source license, budget and visual review procedure; substitute only the B2 prefab path and `/tmp/ct-home-basement-art-b2-native-20261007` capture folder. Keep the same normal Home camera. No backend/registry/contact acceptance.

Private installed Editor-reference compile exits0, existing CS1701 only. Audited sole compiler output: `/tmp/ct-cafe-hit-compile/ParentsBasementCandidateB2.dll`; no Bee/refout writes. Root owns native import, fresh bake, actual view and post-Editor hash audit. No new room expansion.
