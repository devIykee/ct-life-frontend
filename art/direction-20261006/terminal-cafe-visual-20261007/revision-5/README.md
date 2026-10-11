# Café Art5: rear monitor support

Frozen outside Assets; native import/build/bake/graphics remain pending. This is the bounded repair for the visible black crossbar across the two wing screens in accepted Art3 floor/light views. Art3 remains intact.

The actual serialized Art3 monitor has 156 vertices / 78 triangles. Three existing stand boxes account for 36 triangles: base, upright and crossbar. The candidate recognizes their exact eight corner positions and requires 12 triangles per box. It does not rely on source index order. A mere bounds-containment test also selected overlapping end faces; the CPU source check caught this before freeze, and exact corner selection isolates all three parts correctly. See the unchanged geometry analysis in `../revision-4/cpu-monitor-support-feasibility.json`.

The base moves +0.05 m in native monitor-local Z; upright and crossbar move +0.10 m. The source screen/bezel triangles, materials, UVs, normals, tangents and original triangle topology remain intact. Three original small mounting brackets reconnect the rear of the screens to the shifted bar, using the existing stand material and its palette UV. They add 36 triangles per station / 288 for eight stations. They have no collider. CPU plane checks put the nearest bracket corner 15.37 mm behind a wing screen and 25.49 mm behind the center screen. The shifted base remains inside the fixed desk with 9.24 mm rear margin. Native stage rechecks the selection and plane distances rather than accepting this CPU record as native proof.

All non-monitor rendered triangles, every collider, fitted chairs/desks/keyboards, 17 anchors and approach transforms, navigation asset, room camera, lights and existing desktop artwork remain held. The source must have zero monitor colliders before mutation. Each repaired monitor's exact surface digest is pinned before the candidate bake and rechecked afterward. Materials/texture assets are reused; no global model, source pipeline, package or canonical prefab is overwritten. Stage records actual renderer counts and additional triangles. No motion profile or registry is attached.

## Root-owned native procedure

Verify `native-import-manifest.json` and import only the two new classes to absent Editor paths. Idle batch stage method: `CTLife.EditorTools.TerminalCafeVisualCandidate5.BuildCandidate`; bake method `.BakeCandidate`. Both preserve earlier outputs and use byte protection with physical package resolution and candidate-only saves. Rehash each transaction's protected files after Editor exit.

Graphics review method: `CTLife.EditorTools.CafeArtRoomReview5.Launch`. Use saved Bootstrap via `-openfile` alone, then `-ctlife-cafeArtOutput /tmp/ct-cafe-art5-room-native-20261007 -ctlife-cafeArtExit 1`. No asynchronous `-quit`, no `-nographics`. The fixture keeps Art3's three furniture tap/walk probes and seven walking legs. It adds a rear monitor detail to the matching front detail and actual gameplay-camera views; the detail cameras are labeled separately. It uses the already reviewed one-owner cleanup/finally boundary and refuses a reused output directory.

Art acceptance requires native front screens unobstructed, original artwork still correctly mapped/readable, rear brackets attached cleanly, base resting within the desktop, coherent existing finish/lighting and no new silhouette distraction in gameplay views. This is not seated exit, connected authority, crowd, phone or venue/product acceptance. No new pose tuning is requested.

## Preserved Art4 failure and narrow correction

Art4 correctly found all three 12-triangle stand boxes but rejected the screen-plane count. The saved material is `Desktop01_0_1`, while its classifier expected the earlier transient `OriginalDesktop` prefix. Art5 instead loads each exact Art3 `DesktopNN.png`, verifies its SHA against that station’s saved stage receipt, and compares the actual `_BaseMap` texture reference. It requires exactly six source screen triangles on three planes, records every material/submesh identity before any aggregate failure, and retains all original physical gates. No triangle or plane-count expectation was weakened.

The Art4 protection transaction was independently rehashed after native exit: 4,982 physical files, zero missing or changed. See `../revision-4/independent-failed-stage-audit.json`. Art4 remains failed and intact; this revision has only compiled against the installed Editor assembly references and has not run natively.
