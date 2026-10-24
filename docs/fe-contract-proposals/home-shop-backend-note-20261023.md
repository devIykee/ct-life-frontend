# FE contract files for backend PRs #7 (homes, flat doors) and #8 (Duo Shop), 2026-10-23

Replace the placeholder copies in `ct-life-backend/contract/ids/` with these two files. Both validate against the PR's own
schemas (`home_units.schema.json`, `shop_items.schema.json`).

- `home_units.json`: the six real walk-in doors on the `lg_midrise_flats` buildings in the Residences. Each `unitId` is
  `flat:lgmf_<x>_<z>:0`, where `lgmf_n439_p68` is the building's footprint centre in whole island metres (x -439, z +68),
  and doorIndex 0 is its one street door. The ids come from `client/Assets/Resources/World/Island/interior_doors.json`,
  which `tools/world/islandgen/dress.py` writes. All six are `tier: flat`, `roomType: apartment_modern`, 18000 cents a
  week and capacity 2.
  - On an existing DB, the placeholder `flat:midrise_0N:0` rows stay in `home_units` (the upsert never deletes). Any
    still unleased should be deleted once, or a lease can land on a door that is not in the world.
- `shop_items.json`: 18 items. There are 12 furniture, 4 decor, 1 appliance and 1 food. There is no clothing (the
  cosmetics shop sells looks).
  - Each `modelKey` is the client prefab `Resources/World/Furniture/<modelKey>`. They are CC0 Poly Haven models,
    credited in `art/LICENSES.md`.
  - `footprint` is the model's measured bounding box in metres. `w` runs along the model's x and `d` along its z. Place
    mode checks overlap against it.

Tested end to end against `feat/duo-shop-delivery`. It ran locally from an overlay directory with these two files
swapped in; the repo was not edited.

## Two small asks (not blocking)

1. Add `homeId` to `GET /units/{unitId}/access` when `canEnter` is true (owner or guest). A guest who walks up to a door
   can enter, but cannot join the `home` room or read the placements without it. The client reads `homeId` when it is
   present and does not need a release.
   - Until then, the client learns a guest's `homeId` from `POST /homes/{ownerId}/visit`.
2. `GET /v1/config` → `flags: { wireV3Kinds: true }` once PR #6 is deployed. The client then sends the v3 vehicle kinds
   (sedan, taxi, police, pickup, van, bike) with no release. Without the flag it keeps sending `compact` / `moped`.
