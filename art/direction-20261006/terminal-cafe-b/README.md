# Terminal Café B — room composition revision

[Inference] A new candidate for native review, not accepted art. The first Café native capture showed a distant avatar, large unstructured floor, unrelated furniture styles and a gray exterior void. This revision addresses that composition while retaining the exact 24 × 18 m room contract and all 17 current spot IDs/positions/rotations.

Builder: `TerminalCafeCandidateB.cs`, outside Assets. SHA256: `53369d0eee1277af4f7178de8bd02a6bbdc667506207ef570bd416628c2af764`.

- Stage: `CTLife.EditorTools.TerminalCafeCandidateB.BuildCandidate`
- Bake: `CTLife.EditorTools.TerminalCafeCandidateB.BakeCandidate`
- Output: `Assets/Art/Candidates/TerminalCafe20261006B/TerminalCafeB.prefab`

Root alone imports/runs this helper when Unity is idle. Use separate batch stage/bake processes and the established external pre-startup/post-exit byte checks. Internal protection includes all existing manifest model dependencies and their metas, canonical Timeline dependencies, snapshot, manifest, live venue registry, package manifest/lock and ProjectSettings. Materials/meshes/textures are saved only in the isolated candidate folder; no registry promotion or original Café overwrite occurs.

## Authored changes

The palette comes from the existing Home family: restrained wooden desks/chairs, sage kitchen cabinets, a blue sofa, warm plaster/window walls and existing wood flooring. The orange restaurant tables/display, black sci-fi desks, turquoise fridge and pink-sofa/yellow-rug combination are removed from this candidate. Source provenance remains in the existing environment manifest and `art/LICENSES.md`; no package or model download was added.

The eight immutable terminal positions now belong to four paired work counters. Existing wooden desks supply the primary furniture, joined by narrow timber worktop bridges; a 3.5 m center opening leads to the Pulse standing anchor. The four immutable social seats face two long communal tables, using existing home table/chair geometry plus timber bridges. Table fronts are 0.52 m north of the social foot anchors. Their walnut material is a candidate-only material assigned to those meshes, not a shared texture/atlas edit. Seating contact still requires native animation review.

Nine existing sage cabinet modules and a shallow stone worktop form a continuous coffee bar beside the unchanged counter/NPC anchor. The bread display is replaced by scale-appropriate pastries and the existing coffee machine. Source window/plaster walls, low front cutaway, full rear/right work-zone walls, skirting and timber reveals establish the room envelope. Repeated carpet fields organize the terminal wings. Three primary warm practical locations, the reading lamp and cool window fill use five baked lights; the venue's runtime directional/ambient values are separately authored.

The exterior ground extends under the threshold and window garden, so the close cutaway looks grounded. Only the unchanged contract room floor is navigable. The existing static `IdlePanel.png` from the venue-expansion proposal replaces invented chart prices on workstation/room display art; it remains static artwork. No new trading, host, occupancy or voice behavior is introduced.

## Camera and checks

The new prefab default is distance 10.8 m, pitch 30°, yaw −12°, FOV 45°, feet viewport Y 0.30. Review Café B using this authored camera first; do not inadvertently apply the Timeline close-camera override. The camera presents entry/social space at adult scale instead of fitting every item in the room at once. Review again at the center and terminal rows.

`source-gate.json`: standalone C# compile exited 0 against installed Unity 6000.3.25f1 assemblies, with the existing CS1701 Newtonsoft/netstandard harness warning. All literal asset IDs resolve to existing imported model files. Native stage/bake/render/device checks have not run for B.

`source-bounds.json`: CPU-only Blender measurements for ten primary furniture models. `audit_composition.py` reproduces `composition-audit.json`: all 17 fixed anchors remain more than 0.50 m from the authored solid bounds (minimum 0.511 m at terminal desks). This is a geometric clearance check, not a native NavMesh pass. The close camera projects a 1.8 m adult proxy to ~226 px high at 2670 × 1200. Near communal table reference points project to (0.397, 0.664) and (0.704, 0.607); the counter coffee machine to (0.148, 0.627). Actual mesh scale, final pixels, lighting and readability remain native acceptance questions.

The helper derives `districtId` from the same parsed `VenueDef` used by the map. With the current imported catalog this follows the FE fallback to `trenches`; an explicit future catalog value takes precedence. This resolves candidate/map consistency without inventing a backend district assignment or changing the backend contract.

Native acceptance: all 17 staged paths, avatar scale at entry, eight exact typing poses, four exact social seats, knees/hands/table edges, passage around the communal ends and coffee counter, Pulse anchor access, absence of gray foreground void, interior lighting/color balance, full room travel out/back, and device frame/memory measurements. The source-level work does not satisfy these gates by itself.
