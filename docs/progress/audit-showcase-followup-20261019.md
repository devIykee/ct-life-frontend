# Offline showcase placement follow-up — 19 October 2026

> Chronological lane receipt. For the latest installed build, scoped device outcomes and final cleanup, see [the current handoff](../handoff/CONTINUE-HERE-20261019.md). Earlier test counts and pending statements below describe their own checkpoints.

Status: the full native Unity suite passed **2018/2018**, zero failures/skips (`../ct-life-evidence/audit-20261019/editmode-followup.xml`, 2026-10-19 04:43 UTC), including all 13 showcase cases and the fallback ownership regression. Android build and device acceptance are coordinator-owned and remain separate from this test result. Compact navigation reference: [manual route card](audit-manual-route-20261019.md).

Scope: showcase placement and focused tests; the follow-up fallback hierarchy correction is in `IslandWorld.cs`, `IslandGroundFallback.cs` and `IslandSafetyTests.cs`. No marker, public parking, terrain geometry, art, flight physics, camera, UI or backend changes from this lane. The existing public parking export remains current.

## Observations and source evidence before implementation

- `showcase-lodge.png` shows a car on the lodge's interior wood floor. `showcase-lodge-entrance.png` shows a sports car on the porch. These are direct observations from the prior Seeker run.
- No helicopter was observed during the Timeline drone scan. There was no successful/failed placement log, so the exact runtime rejection cannot be established from that run.
- All serialized `VehicleSpawn` markers were inspected: the lodge has **zero within90m of arrival**. Timeline's26 nearby bays have mask15 (`Cars|Moped`), excluding Roadstar16. Those craft necessarily depend on the fallback branch when their showcase spawn succeeds.
- The old fallback casts with `CarPhysics.GroundMask`, which includes `Interior17`. `IslandWorld.Prepare` moves venue layer0 floors/props onto17. A flat floor can satisfy the ray, normal, height and corner checks; the occupancy box starts15cm above it. There was no venue-footprint exclusion. This establishes a concrete acceptance path for the observed interior/porch placement; the exact per-car selected candidate was not logged.
- The old bay branch checked only `Vehicle20` overlap. It did not validate static obstacles, support, water, loading, or `VehicleFlight22`. The old helipad branch spawned without validation.
- The three authored pads are approximately `(-226,69.21,428)`, `(640,-0.66,-262)`, `(-300,7.3,214)`. None is within260m of Timeline. The nearest lodge pad is available. Timeline therefore relies on the14–70m fallback search, including its knee-height linecast from the player **inside** the courtyard.
- `VehicleSystem.Start` calls the old spawner after20frames, once. `IslandWorld.Ensure` already loads cells within160m around the initial venue; therefore a streaming race is **not established as the cause** of the observed Timeline absence. Explicit readiness is still the correct contract for the expanded exterior search and a separately selected pad.
- Authored frames: Timeline origin(0,0,0), yaw354°, local frame(-16.5,-17.4,33,34.4), door(1.296,0,-12.332). Lodge origin(-259.587,69.211,412.183), yaw267.904°, frame(-13,-15,26,26), door(-249.594,69.211,412.549). The frame contains the porch; moving only beyond the interaction door would remain inside it.

## Primary research opened before implementation

- [Unity6000.3 Physics.Raycast](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Physics.Raycast.html): layer masks select tested colliders, explicit trigger handling is supported, and a ray whose origin is inside a collider does not detect that collider. Placement probes now start above roofs and below the authored flight ceiling, then reject non-ground/non-road support.
- [Unity6000.3 Physics.CheckBox](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Physics.CheckBox.html): extents are half-size, orientation rotates the query, and a true result means an overlapping collider. Every placement branch uses the same body-clearance query with world, vehicle, flight-vehicle and character layers included.
- [Unity6000.3 Physics.SyncTransforms](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Physics.SyncTransforms.html): transform changes can need an explicit flush before immediate physics queries. Sync occurs once per craft after cell/previous-craft creation, not inside the search loop.
- [Unity6000.3 Physics.RaycastAll](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Physics.RaycastAll.html): hit ordering is undefined. The change keeps a closest-hit ray instead of selecting an arbitrary surface from a multi-hit result.

No dependencies were introduced. Existing HUD/interaction standards remain in use; there is no new player-facing UI.

## Implementation decisions

- The spawner itself requires the explicit development showcase flag and offline island mode; online/ordinary play cannot enter this path.
- Derive an exterior staging point from the authored door, rotation and full venue frame. This removes the courtyard/interior from the parking search and its visibility origin without hardcoding a new Timeline coordinate.
- Wait for the exterior search cells and selected pad's ground. Yield between craft, avoid respawning a craft of the same spec already present, and synchronize physics before querying.
- Apply one physical validator to bays, helipads and fallback candidates: all nine footprint samples loaded, dry, flat and supported; ground/road support only; no overlap with the rotated authored venue frames; no body collision or placement on the low-resolution fallback mesh. Building roofs and interior floors are not support.
- A fallback approach is a line through the exterior world, including vehicle-only blockers. It no longer depends on incidental proximity to a separate NavMesh surface.
- Log `CTLIFE [vehicles] showcase <kind>: parked at (x,y,z)` or a concrete no-placement/load outcome. A missing craft is reviewable instead of silent.

## Validation plan and limits

Focused physics regressions cover the old empty-interior acceptance path, static and vehicle occupancy, vehicle-only doorway blockers, unloaded footprint corners, fallback-only collision, narrow support, roofs above the old6m ray origin, and rotated porch/frame exclusion. Authored-world fixtures use the shipped Timeline/lodge collider shapes, marker poses and full-resolution terrain and require all six showcase craft to find exterior supported space.

Coordinator owns Unity and the Seeker. Compile/test results and exact fixture positions will be appended after execution. The fixtures validate physics placement without running game services or rigidbody simulation; they do not substitute for hand-playing the two showcase launches on the device. Vehicle usability, route performance, hill landing, camera and ordinary public parking remain separate acceptance items.

First native targeted run (`flight-showcase-before.xml`): source compiled; showcase 11/13 passed. Lodge found all six craft outside the venue. Two fixture errors were corrected without changing the production safety rules: Timeline checked the scooter with the car's larger reserved footprint before reaching the helicopter assertion; the unloaded-corner test changed the cell list after its rect cache was initialized. The revised test uses the scooter's reserved dimensions and initializes immutable cell metadata before the first query.

Native rerun (`../ct-life-evidence/audit-20261019/flight-showcase-after.xml`) passed all 13 showcase cases, 22/22 including camera cases. These are fixture results, not device spawn telemetry:

| Craft | Timeline XYZ | Lodge XYZ |
| --- | --- | --- |
| Compact | (2.81, -0.23, -26.75) | (-235.10, 68.88, 413.08) |
| Sports | (7.45, -0.12, -23.66) | (-240.16, 69.14, 406.93) |
| Roadstar | (-2.37, -0.13, -24.69) | (-236.40, 69.24, 422.81) |
| SUV | (10.36, -0.07, -18.44) | (-242.25, 69.14, 423.80) |
| Scooter | (-3.62, -0.07, -19.26) | (-240.60, 69.14, 418.84) |
| Helicopter | (-13.85, -0.09, -24.70) | (-225.15, 69.56, 427.27) |

Later plaza-grounding repair: the Timeline exterior pavers now have matching collision. The 11:01 UTC native fixture rerun retained all six Timeline X/Z coordinates above, with Y **0.31 m** for compact, sports, Roadstar, SUV and helicopter, and **0.29 m** for scooter. Lodge positions are unchanged. All 13 showcase cases passed. These supersede the earlier table's Timeline Y values; see [plaza evidence and limits](audit-plaza-grounding-20261019.md). The full run also had graphics-device UI failures, so its showcase pass is not a full-suite pass.

## Fallback hierarchy correction

Source review found that `IslandWorld.Load` attached `IslandGroundFallback` directly to the island root. Every streamed road/terrain collider and venue descended from that root, so the existing ancestor checks in flight-floor, below-world recovery and vehicle recovery logic classified ordinary support as coarse fallback. [Unity 6000.3 GetComponentInParent](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Component.GetComponentInParent.html) confirms that the lookup traverses ancestors.

`IslandGroundFallback.Create` now creates a dedicated child owner containing only the four coarse meshes; streamed cells and venues remain siblings. Showcase support uses the same ownership classification rather than a mesh-name prefix. The new native regression uses the runtime creation helper and actual ray hits on road, tunnel and coarse floors, requiring only the coarse hit to have the fallback ancestor. The existing fallback-only placement regression now uses the same helper. Both passed in the full 2018-test native run.

## Manual route research for coordinator

Read-only calculations use the shipped `heightfield.bytes` (byte-identical to generator `height.r16`), `plan.json`, `roads.json`, current cell colliders and `Vehicles/heli.prefab`. Coordinates below are inspection candidates, not completed device tests.

- **Summit:** the nominal landmark is X−180/Z495, terrain Y158.59; the highest terrain sample is X−176/Z516, Y179.47. A 2m-grid search over X[−270,−90]/Z[430,550], above Y90, found no broad flat summit: the smallest sampled 9×11m footprint height range was 3.99m. That spawn-clearance footprint is larger than the heli's actual cabin collider (1.68×2.26×5.83m) plus tail (0.5×0.8×3.948m), with total local Z span 9.4m.
- **Authored feature:** `mtndao_cairn` at (−182,174.23,513) is stones and three 7m flagpoles, with no platform, roof or collider. `IslandV2Shells` exits before wall collision for this feature.
- **Preferred terrain inspection candidate:** (−154.5,168.99,496.5), about99m northeast of the lodge heli (+70.65m X/+69.23m Z), is a narrow crag shoulder about29m southeast of the cairn. Reconstructing the actual terrain triangle gives a19.9° center slope. With nose north, samples under the actual cabin/tail bottoms do not require raising the root above the center floor. A potential west exit (−156.5,168.50,496.5) has less than1.2m floor delta and less than35° slope. Nearest authored prop centers are11.29m and11.68m away. These measurements support trying the spot; they do not replace actual contact, capsule and stability checks. It is not a broad flat pad. Immediately east at Z496, terrain descends from Y169.14/X−154 to165.12/X−150,160.14/X−146 and150.47/X−138 (~19m over16m) for nearby fall inspection.

The later manual repeat makes the shoulder's precision limit explicit: at X−154.5, moving from Z496.5 to497.5 changes the actual collision triangle from19.88°/normalY0.94040 to32.03°/normalY0.84775, just outside flight's0.85 landing threshold; Z498.5 is59.02°. The proposed west exit is29.81°, about0.49m lower than the intended root floor. Even a successful landing at the intended point would establish one narrow landing/exit, not broadly forgiving summit terrain.

Navigation during that repeat used only saved visible full-map UI, not runtime coordinates or game callbacks. `../ct-life-evidence/audit-20261019/calibrate-visible-map.py` inverts the actual0.5-radian tilted map projection from viewport bounds and two authored landmark centers, with district-pill centers as independent checks. At14:07:46UTC, the driver marker estimated XZ(−156.69,498.55); independent checks differed0.22–0.35m. This is the seated actor, not the helicopter's Rigidbody root. [Inference] Static FBX seat coordinates, the prefab's model-orientation correction and the existing native demo `act_drive` pelvis measurement put the actor roughly0.219m left and2.528m forward of the craft root at north-facing upright hover. That estimates root(−156.48,496.02), close enough to a triangle boundary that0.3m south changes29.81° to45.36°. The imported helicopter seat transform itself was not separately native-probed in this repeat; UI rounding, pose and lean remain limits. Full evidence and samples are in `heli-repeat2-after-correction-map-calibration.json`. Physical touchdown/exit acceptance remains coordinator-owned.
- **Landing inspection candidate:** the large `kf_rock_1_j` at (−180.85,174.08,518.41), yaw97.9°, has a serialized BoxCollider roof at Y181.448 and scaled footprint10.10×11.19m. It is 101m northeast of the lodge heli fixture (+44m X/+91m Z). Aligning the actual heli body roughly east fits the proxy footprint; exit clearance and rigidbody landing have not been simulated by this source inspection. The rendered rock is irregular, so successful contact on this flat proxy may still look wrong. This is not an authored flat-terrain landing pass.
- **Adjacent descent:** north of that boulder, authored terrain at X−181 descends from Y168.36/Z524 to Y149.33/Z540 and Y110.29/Z570. This is a real steep descent near the summit for an on-foot fall/recovery inspection; walkability and camera behavior remain device checks.
- **Roadstar:** lodge fixture (−236.40,69.24,422.81) is about10.5m from Summit Road end (−240,69.21,412.9). Follow the road east to the (−90,56.10,391.6) hairpin, west to (−315,36.10,365.2), then east to the stop/T at (−120,19.31,318). Continue east on Foothill Road through (−20,20.29,340), bridge (30,17.98,332)→(66,17.98,322), (96,16.40,306), and the Builder Hills roundabout near (184,8.75,238). The east exit reaches (420,19.08,252). The connected route is approximately1.2km including junctions. Summit Road is8.6m wide with maximum sampled grade8.1%; Foothill11.4m/9%; Hills Drive7.2m/8%. These are authored centerline/width measurements, not an obstacle or traffic clearance result.
- **Streaming destination:** Builder Hills destination lies in `cell_5_4`, X[256,512]/Z[0,256], more than500m from the lodge and outside its420m initial streaming radius. The direct Roadstar flight leg from the lodge fixture is678m horizontal, offering a shorter approach to an initially unvisited cell if the winding driving route is impractical.

The coordinator will hand-play through existing UI; no route automation, new platform or terrain modification was introduced.
