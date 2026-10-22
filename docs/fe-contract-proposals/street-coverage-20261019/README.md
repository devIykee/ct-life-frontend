# ORE and north approach street coverage proposal

Review-only data proposal against backend `7d2f069`. No backend implementation, runtime contract, Unity asset or authored layout was changed. No new district ID, room, field, entitlement or economy rule is introduced.

The original parking validation counted **36 of 283 bays** outside every server street polygon plus its 30 m border. The client uses exact polygons (`IslandWorld.DistrictAt`), so **38 bays** currently resolve to countryside. The two extra bays are `park_compact_-1017_3293` (29.469219 m outside Breakpoint) and `park_suv_-1074_3273` (24.117869 m outside Breakpoint). Their server bounds would accept movement, but the current client does not join that street room there.

ORE is labeled mountain, yet its door at **(133.32, 309.44)** is **70.56 m** south of the mountain polygon. The earlier live mountain-room probe returned `OUT_OF_BOUNDS` for that door. ORE venue presence and street presence are separate contracts.

## Proposed disposition

Recommend reviewing a connected southern extension of the existing **mountain** polygon: the rectangle **x -120..170, z 250..380**, attached to its current south edge. It adds 37,700 square metres and encloses all 38 affected public bays and the ORE door. These coordinates are a proposed boundary selected to give the affected bays at least 10 m of interior margin; they are not an already approved world-design decision.

`mountain-polygon.proposed.json` contains only the existing polygon field's array-of-[x,z] shape. Its vertices are:

```json
[[-720,380],[-120,380],[-120,250],[170,250],[170,380],[640,380],[640,620],[-720,620]]
```

If accepted, FE must change the authored `mountain` polygon in `tools/world/islandgen/layout.py`, regenerate the relevant plan/prefab district data, and hand Iyke the identical polygon for `contract/ids/districts.json`. BE must adopt that reviewed data and recreate affected room instances; each room caches its polygon at creation. Updating only one side leaves disagreement. Keep the existing island-land validation and 30 m movement border. The proposal covers these concrete gaps; it is not complete countryside coverage.

The public parking export remains the separate, existing **283-bay** proposal. All 38 affected bays should remain in that export, with their actual poses and stable IDs, pending coordinated geometry review. Do not silently relocate, omit or re-identify them to hide coverage errors. After geometry approval, the existing backend `parking.json` schema can seed all 283; the published registry remains empty today. Marker poses and all recorded source hashes still matched the export in this follow-up, and the World lane confirmed it made no public marker pose changes.

## Evidence and reproduction

- [Coverage map](coverage-map.svg): current district outlines, proposed mountain extension, 36 server gaps in orange, two additional client gaps in blue, ORE in red.
- [Affected parking CSV](affected-parking.csv): all 38 IDs, exact poses, nearest district/distance, excess beyond 30 m, source prefab/line/marker.
- [Input hashes and counts](coverage-summary.json).
- [Validation with the actual backend geometry helpers](backend-validation.json): 38 exact-client gaps, 36 server gaps, all affected bays on island land and inside the proposed polygon, ORE on land/inside proposal, zero of the 283 bays outside all exact polygons after the proposed change.

Run `python3 tools/integration/street_coverage_proposal.py` from the frontend root to reproduce the review artifacts. The backend-helper receipt is separate corroboration using `pointInPolygon`, `distanceToPolygon` and `onLand` from the unchanged audit checkout. This is geometry validation, not terrain, collision, driveability or server deployment certification.
