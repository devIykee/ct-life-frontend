# Public parking export — 19 October 2026

This proposal supplies the frontend-owned bays still empty in backend `7d2f069`. It has **283 public parked vehicles**: 99 compact, 51 sports, 133 SUV. No price, rental, owned vehicle, premium entitlement or showcase fleet is introduced.

`parking.json` uses the exact `ParkingBay` shape consumed by `server/realtime/movement.ts` and `VehiclesRoom.seedBays`: `id`, `kind`, `x`, `y`, `z`, `yaw`. Coordinates are island metres, yaw is radians, and Y includes the same +0.05 m offset used when the client swaps a decorative car for its drivable equivalent. The IDs and fallback model selection match `ParkedCars` and `IslandProtocol`; they use serialized float32 coordinates rather than actor positions after physics moves them.

The source is every active `ParkedCar` marker in the final working-tree cell prefabs after the coordinator's world bake. Empty `VehicleSpawn` slots are for the explicit offline showcase and are excluded. The exporter refuses nonidentity ancestor transforms, inactive markers and duplicate IDs. `provenance.json` records each marker/file/line, the frontend base commit, and SHA-256 hashes of the actual working-tree inputs; the commit alone does not include these uncommitted repairs.

Validation against the actual backend geometry/helpers found all 283 on island land with unique IDs and valid shapes. **36 bays are outside every current street district plus its 30 m border.** Their IDs are listed in `validation.json`. Seeding them does not resolve the countryside presence gap: entering there still needs fresh shared presence. Serialized pose validation is not a new driving/collision certification.

To reproduce from the frontend root:

```sh
python3 tools/export_parking.py
```

Backend integration is to copy this proposed `parking.json` to `contract/ids/parking.json`, review the countryside gap, then restart/recreate the vehicles room so `seedBays` loads it. Server-owned bays are outside the client-registration cap and use the existing return-to-bay behavior. This handoff **has not** patched, deployed, committed, pushed or seeded the backend; the published and isolated runtime registry remains empty.
