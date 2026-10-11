# Timeline street B — landscape composition revision

[Inference] A second visual hypothesis, not final art approval. It responds to the actual native A captures, which showed readable destinations but repetitive squat kiosks, conspicuous orange chairs and too much pale paving.

`CityDirectionCandidateB.cs` is deliberately **outside Assets** until the coordinator's native queue is idle. It compiles independently against installed Unity references and the project's existing compiled assemblies. `source-gate.json` records its hash and the standalone compiler's single CS1701 reference-compatibility warning. This is not a native import/build pass.

When ready, the coordinator copies this source to `client/Assets/Scripts/Editor/CityDirectionCandidateB.cs`. Methods:

- `CTLife.EditorTools.CityDirectionCandidateB.BuildCandidate`
- `CTLife.EditorTools.CityDirectionCandidateB.BakeCandidate`

Output: `Assets/Art/Candidates/CityDirection20261006B/TimelineStreetB.prefab`. Version A is retained.

Changes from A:

- West side becomes a linked, two-building coffee/bakery frontage; the east side becomes a taller three-storey balcony building. These use different complete models from the existing CC0 [Quaternius Buildings pack](https://quaternius.com/packs/ultimatetexturedbuildings.html), with uniform source scaling rather than stretching one kiosk.
- Café chairs/table use the thinner existing meshes from the CC0 [Ultimate House Interior pack](https://quaternius.com/packs/ultimatehomeinterior.html). Warm wood tints/roughness apply to **candidate material copies assigned only to these meshes**. The shared source atlas is unchanged.
- The arrival promenade narrows to 3.3 m. Blossom gardens move toward the foreground to create a frame around arrival rather than a distant row. Their nav exclusions move with the actual solids and retain all existing spot gates.
- Warmer directional sun and cooler sky fill give the bake a deliberate warm/cool balance. This must be judged in the native image; source color values are not proof of visual quality.
- Existing camera values are retained for controlled comparison using the coordinator's landscape review override. No synthetic people or fake live activity are added.

Use the [protected native procedure](../README.md), including exact external bytes captured before Unity launch and checked after exit. The inherited receipt/protection system preserves the canonical prefab and its dependencies, config/registry and project settings. It does not register B as the published scene.
