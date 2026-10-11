# Vincent idle: native surface review

The first shared-idle gate rejected a −17.8 mm lateral projection gap. Actual front and profile meshes show the hands in front of the trousers. That axis overlap was not proof of an intersection.

Native evidence: `native-shared/`, captured by the coordinator with `VincentIdleProbe.CaptureShared`. All 13 phases, including the repeated loop endpoint, are exported for Creator and all three world renderers (52 meshes). The diagnostic does not change the Avatar, clip, controller, registry or prefabs. Neutral facial weights are explicit.

`analyze_triangle_clearance.py` checks the minimum distance between finite triangles of the selected hand and hip/thigh surfaces. It includes triangles crossing the >0.5 bone-weight selection boundary, vertex/face and edge/edge closest features, and edge-through-face crossings. A BVH sphere bound prunes triangle pairs that cannot be closer. Three analytic geometry fixtures pass before the actual meshes are inspected.

The complete `native-shared-triangle-clearance.json` passes the proposed **2 mm engineering surface margin** across all 52 meshes. Minimum distance is **2.919555 mm**, Creator/world high tier, right hand at phase 0.25. LOD1 minimum is 4.349384 mm; LOD2 minimum is 3.755117 mm. This is separate from the original 10 mm lateral heuristic. The selected regions are open surfaces, so the result is not a whole-body containment test, transition test, or a guarantee for unsampled phases. The native importer repeats the triangle-distance calculation rather than accepting an external report as authorization.

Reviewed renders in `renders/native-shared-{front,hands,side}.png` are actual Unity-baked phase-zero meshes under diagnostic lighting. They are not device screenshots. The initial five-phase BVH/vertex-distance report is retained as `native-shared-surface-analysis.json`; the later triangle result supersedes it for distance evidence.

## Optional authored idle, not assigned

A separate six-second Vincent idle was authored while investigating the lateral failure. It uses Vincent's own canonical 55-bone rest and mesh, not another character's proportions. The measured source-pose grid selected 45 degrees of arm lowering and an 8-degree forearm bend; all 30 digit joints have mild keyed movement. The source loop has 22.24–25.07 mm lateral hand clearance, zero foot-joint drift, grounded soles and zero loop surface difference. These are source measurements, not native acceptance.

`idle-export-validation.json` passes 14 fresh-FBX checks: canonical rest-matrix delta exactly 0; maximum sampled skinning-matrix delta 1.32e−6; a six-second closed loop and all 30 keyed digits. File `VincentAdult_RelaxedIdle.fbx` SHA256 is `83a4d79da89b9f0488f2adb3fcb0eb8449441fc80f340314f4a2bba85d1bbf36`. It includes the unchanged canonical skin reference for bind preservation. The production source exports remain unchanged. All facial shape defaults are zero.

`renders/source-candidate-{front,hands,side}.png` show that authored candidate in Blender. It is **not imported, registered or selected**. The optional `VincentIdleImport.cs` stages an unreferenced own-Avatar override and preserves the shared controller; it is held because the actual shared-idle surfaces currently pass the more relevant 3D gate. There is no need to force an extra idle solely to satisfy the lateral projection test.
