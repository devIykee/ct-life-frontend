# Home B — bounded domestic dressing

Status: staged source, compiled against the installed Editor references. **Not imported, built, baked or visually accepted.** Root owns those steps. Home A and its native screenshots remain intact.

Inspected baseline: `docs/progress/screens/home-basement-art-a-native-20261007/city.png` (root archive; original capture `/tmp/ct-home-basement-art-a-native-20261007/city.png`). The room has a readable bed/work/living/kitchen layout, but its large wood center, empty bookcase, blank TV and plain counter row do not communicate a lived-in home.

## Exact bounded change

- Clone the frozen baked A prefab. Keep all six `home` anchors, four standing approaches, original rendered surface positions, all colliders, navigation asset, camera, five existing lights, bed/sofa/chair/desk transforms and source contact geometry.
- Reassign the eight existing floor top triangles in X −2…2 / Z −3…1 to a quiet woven finish: exactly 16 m², Y 0. No raised rug, duplicate plane, floor partition or collider change.
- Three source book/frame clusters on alternating measured bookcase shelf levels; one desk lamp and original paper notebook; coffee machine, two mugs and a small plant on existing counters.
- Two small source frames with original prints, original television/laptop images, and a fridge note. The source screen mesh stays intact; each small display overlay follows a measured coplanar native face with a 0.6 mm depth offset. No live market/account data or interactive claim is implied.
- Separate new decorative hierarchy, zero new colliders, no furniture, actor or navigation translation. No registry, backend, Home lifecycle, action or motion-profile changes.

The native existing A mesh read in `native-source-plane-preflight.json` confirms the TV's 0.88712 m² rectangle and laptop's 0.071158 m² tilted plane. Bookcase shelf pitch is about 0.26852 m; the three 0.45-scale clusters are about 0.221 m tall and use alternating shelves. This is source feasibility, **not B import/render acceptance**. A direct read of the existing native floor asset found eight selected triangles / 16.0 m². The builder repeats those checks against the instantiated room and rejects any unexpected layout.

## Assets and budget

Added expected source LOD0 triangles: 2,370, including twelve original art quad triangles. Hard native ceiling: **3,000 added triangles, 24 added MeshRenderers, six new pre-bake materials**. Three new materials are authored: screen Unlit, paper Simple Lit, woven Simple Lit. Existing A source materials and palette texture remain read-only. New small props keep only their source LOD0; no duplicated simultaneous source LODs.

Two original textures: `HomeDetailAtlas.png` 2048×1024 and `HomeWeave.png` 512×512, Android ASTC 8×8, mipmapped. Compressed base levels total about 576 KiB before mipmaps and platform bookkeeping. Fresh lightmap cost is measured by the bake, not included in that texture estimate. Renderer/material counts after the lighting builder may differ from pre-bake counts and must be reported from native evidence.

Source models are already present, licensed and inspected in this project. Current primary license pages opened 2026-10-07:

- [KayKit Furniture Bits](https://kaylousberg.itch.io/furniture-bits): CC0; decorated shelf and picture frames.
- [Kenney Furniture Kit](https://kenney.nl/assets/furniture-kit): CC0; desk lamp, coffee machine, plant.
- [Kenney Food Kit](https://kenney.nl/assets/food-kit): CC0; mugs.

`source-manifest.json` records exact processed FBX provenance, texture dependencies and all current Home A asset bytes. `artwork-provenance.json` records original layouts/drawings and the existing licensed Manrope font. No new package or download.

## Root-owned import and native sequence

Import only the frozen C# helper to `client/Assets/Scripts/Editor/ParentsBasementCandidateB.cs`. The builder reads its pinned source manifest and original PNGs from this external folder; do not copy them into the shared environment source tree. `native-import-manifest.json` records exact hashes and the absent destination requirement.

1. Idle native `-executeMethod CTLife.EditorTools.ParentsBasementCandidateB.BuildCandidate`. New isolated output: `Assets/Art/Candidates/ParentsBasement20261007B/ParentsBasementB.prefab`. Do not reuse/erase a failed output. Required receipt: `staged_unbaked_private_home_not_published`, six complete approach paths, held-geometry receipt, within budget and zero added colliders.
2. Fresh graphics bake: `-executeMethod CTLife.EditorTools.ParentsBasementCandidateB.BakeCandidate`. It requires the exact staged prefab hash, repeats held geometry/navigation/camera/lights checks after bake and checks added surface identity. Required receipt: `baked_private_home_art_not_registered_or_contact_accepted`, complete unique lightmap bindings. Inspect the post-Editor transaction independently because import callbacks can run after the helper returns.
3. Existing landscape review from saved Bootstrap using **`-openfile` alone**, without simultaneous `-projectPath`:

   `-executeMethod CTLife.EditorTools.FrontendReview.Launch -ctlife-review 1 -ctlife-venue home -ctlife-reviewVenuePrefab Assets/Art/Candidates/ParentsBasement20261007B/ParentsBasementB.prefab -ctlife-reviewLandscape 1 -ctlife-reviewSequence city -ctlife-reviewCapture /tmp/ct-home-basement-art-b-native-20261007 -ctlife-reviewExit 1`

   Do not pass close-camera flags: preserve A's 8.8 m / pitch30 / yaw−12 / FOV42 / target0.30 room camera.

## Actual view acceptance

Compare the same arrival view to A: central floor should feel intentional without a dark dirty band; original TV image should be upright, inset and readable; added book clusters must fit between actual shelves; prints must not float, disappear into the north wall or be hidden by the standing lamp; small counter props should read at adult scale. Check close TV/laptop/desk/counter views if the arrival view cannot resolve plane orientation or overlap. Confirm no added art is inside the immutable standing approach or obvious body envelope. Reject visible occlusion/UV/lighting errors before selecting B.

The source geometry guard is not furniture contact approval. Home bed, sofa and office chair still have no accepted Home action/exit presentation, and this dressing does not establish sleeping, needs effects, local Home entry/return, multiplayer, connected claims, performance or production acceptance. Stop after the requested visible pass; no wider room expansion is included.

Protection captures both pre-existing hashes and the path set across physical Assets / ProjectSettings / Packages, excluding only this new candidate. It saves only owned candidate assets and never restores bytes, clears unrelated dirty assets or calls global SaveAssets. Later post-Editor hash checks remain required. Existing unsaved scenes are preserved by an early guard.
