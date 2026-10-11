# Studio F2: explicit prefab-parent classification

Frozen outside Assets, native unrun. F's failure and partial output are preserved. Its process failed before navigation or prefab save with `A non-chair rendered or collided surface changed.` The post-Editor protection audit covered 4,790 physical files; none changed or disappeared.

[Unverified] The suspected cause is classification, not an observed furniture mutation. F called `GetComponentInParent<SpotAnchor>()` on persistent-prefab chair transforms and active-clone chair transforms without including inactive ancestors. Unity 6.3 documents the default as false and supports the explicit bool overload. F2 uses `GetComponentInParent<SpotAnchor>(true)` to give both objects the same intended chair exclusion. [Current Unity API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Component.GetComponentInParent.html).

F2 must establish the cause natively **before changing any furniture**. `PreMutationClassification.json` records each of the 30 original and cloned lounge assemblies, activeSelf/activeInHierarchy, default versus explicit parent identity, and exact corrected render/collider differences. It requires all of these together: 30 explicit chairs in the persistent prefab and unchanged active clone; the suspected default classification discrepancy (0 persistent / 30 active); and no corrected non-chair surface difference. Otherwise it stops before mutation with the diagnostic intact. That result is not claimed in advance.

The copied physical chair/material/navigation implementation remains F's reviewed implementation. Exact non-chair world triangle and collider equality, source/copy chair topology and 10µm identity, 0.450 m support, unchanged 34 anchors/31 approaches/camera, complete paths and byte protection are retained. No geometric threshold was relaxed. `NonChairEquality.json` now records exact added/missing surfaces with multiplicities and per-mesh path/asset/transform witnesses before any equality exception. Failure evidence stays in the separate F2 folder.

The two new review classes differ from frozen F only in unique class names and the F2 asset/output paths. Research independently reviewed the F population helper's new controller/source/evidence boundary and found no concrete mismatch; this was not native, art or contact acceptance.

## Root-owned native order

Import the three new files in `native-import-manifest.json` only after their hashes and absent destinations match. Do not replace F. Stage with `CTLife.EditorTools.SpacesStudioSharedChairCandidate2.BuildCandidate`, then bake with `.BakeCandidate` if stage passes. These are normal idle batch methods.

Actual graphics room review: `CTLife.EditorTools.SpacesStudioSharedChairRoomReview2.Launch`, saved Bootstrap via `-openfile` alone, `-ctlife-studioOutput /tmp/ct-studio-shared-chair-f2-room-native-20261007 -ctlife-studioExit 1`. No asynchronous `-quit` or `-nographics`.

Occupied graphics review: `CTLife.EditorTools.SpacesStudioSharedChairPopulationReview2.Launch`, same saved Bootstrap entry, `-ctlife-studioPrefabHash <exact passing F2 baked prefab SHA> -ctlife-studioBakeHash <exact F2 BakeReceipt SHA> -ctlife-studioPopulationOutput /tmp/ct-studio-shared-chair-f2-population-native-20261007 -ctlife-studioPopulationExit 1`.

All visual/contact/runtime limits from `../revision-f/README.md` still apply. The 12-person cast is explicitly synthetic; eleven seats use preview-only exact saved fitted Café social states and the host stays canonical standing with a separate gesture audition. No Studio profile, backend, voice or registry mutation. Root retains the native slot.
