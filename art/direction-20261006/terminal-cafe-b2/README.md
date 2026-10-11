# Terminal Café B2 — café identity and arrival pockets

[Inference] Native Café B has a coherent source family but still reads as a computer classroom: oversized joined tables, empty shelving, blank displays and muddy exterior paving. B2 is a source-validated art candidate for native review, not accepted production art.

Builder: `TerminalCafeCandidateB2.cs` (outside Assets). SHA256: `00bc797c62a74d9e21844c7237130f50da9c64b7753f493882a8b60150e2130a`.

- Stage: `CTLife.EditorTools.TerminalCafeCandidateB2.BuildCandidate`
- Bake: `CTLife.EditorTools.TerminalCafeCandidateB2.BakeCandidate`
- Output: `Assets/Art/Candidates/TerminalCafe20261006B2/TerminalCafeB2.prefab`
- Authored camera: distance 10.8 m, pitch 30°, yaw −12°, FOV 45°, feet viewport Y 0.30. Do not apply the Timeline camera override when reviewing this room.

The coordinator alone stages the helper when Unity is idle, then runs stage and bake in separate native processes. Maintain external pre-startup and post-exit protected-file hashes as before. The helper snapshots existing model dependencies/metas, canonical Timeline dependencies, current snapshot/manifest, venue registry, packages manifest/lock and ProjectSettings. Candidate materials, meshes and copied textures stay inside the new output folder; only narrow asset saves are used. The original Café A and B outputs remain intact. No registry promotion occurs in this helper.

## Composition

All 17 current anchors and the 24 × 18 m walkable boundary remain exact. The current parsed VenueDef still determines district. No backend, billing, market, voice or multiplayer state changes are made.

- Four compact 1.269 m diameter, 0.75 m high sourced wood tables replace the giant communal desks. Each is 1.20 m north of its immutable social anchor; existing chairs stay at those four anchors. All social and worktop bridge pieces are removed.
- The eight existing terminal anchors keep individual adult wood desks and office chairs. Four modest plant dividers replace the bridge strips. Separate narrow petrol carpet fields leave timber floor visible between rows.
- Two 2.2 m adult blue sofas form near-entry reading pockets at x ±5.6, z −7, with 0.888 m coffee tables and existing floor lamps. The inner coffee-table edges leave a 7.35 m central opening. These are decorative sofas, not additional claimed multiplayer seats.
- A 3.2 m wide, 2.9 m high sage counter return at z −1.34 gives the coffee counter a backdrop. A single menu is mounted on its camera-facing side; the NPC/counter anchor remains clear. The counter apron is a restrained stone-colored candidate surface, not the mottled concrete texture.
- The rear blank market board and both pink café signs are removed. One original cream/evergreen mural reads “NO ALPHA. JUST COFFEE.” Its complete framed surface projects inside the arrival view. The eight monitors use a quiet coffee screensaver, not invented market prices or live metrics.
- Six stocked, sourced book shelves occupy perimeter joinery, with two small stocked shelves on the backbar. These replace empty white bookcases. Low walnut wall panels and source plaster/window modules give the interior a consistent envelope.
- Exterior cutaway ground uses the same existing `plaza_hexpaving_2m` model family as Timeline. Existing planters and blossom trees continue the window/street context.

Existing environment sources are unchanged: Quaternius, Kenney and Kaykit assets already recorded in `art/LICENSES.md` and the environment manifest. No new model/package download. `create_identity.py` authors the three original decorative graphics from text and simple shapes; `artwork-provenance.json` records their hashes. No copied source artwork or font files are distributed. There are no prices, advertising claims, fabricated occupancy or fake trading values.

## Evidence and remaining gates

`source-gate.json`: isolated C# compile exit 0 against installed Unity assemblies; known CS1701 Newtonsoft/netstandard harness warning. An initial attempt encountered absent cached Editor assemblies (CS0006); the isolated RSP was corrected to reference the existing native ScriptAssemblies. No project/cache mutation was used to resolve that harness issue. All 26 literal Add model IDs resolve. The conditional window model already exists and is inherited unchanged from native-tested B.

`source-bounds.json`: CPU-only Blender LOD0 bounds for 14 relevant source models. `audit_composition.py` reproduces `composition-audit.json`, including all 17 anchor-to-solid clearances; minimum remains 0.511 m at the existing terminal desks. Sofa, table, lamp, new plant and shelf/backbar locations are included. Source checks do not establish native NavMesh reachability or seated animation contact.

At 2670 × 1200, the existing camera projects a 1.8 m actor proxy to 226 px high. The main mural's complete frame projects to x 0.517–0.652, y 0.843–0.990 (~362 × 177 px bounding rectangle); the counter menu to x 0.197–0.259, y 0.745–0.902 (~163 × 189 px). Six message sample rays per panel clear the audited furniture/monitor AABBs. This is a limited geometric check, not proof of visual readability or freedom from every prop/light/character occlusion. Menu body text is expected to be read nearer the counter; the heading carries the arrival identity.

Native stage/bake/render have **not** run for B2. The receipt's six baked lights, renderer count and LOD0 source triangle estimate must be checked after stage. Required review: all 17 complete navigation paths; entry/center/back-row images; mural/menu and shelf orientation; unobstructed central route; sofa/table placement; exact typing and social seat animation contact; warm lighting without excessive yellow; clean hex threshold; travel out/back; actual Android frame/memory behavior. No performance acceptance is claimed.
