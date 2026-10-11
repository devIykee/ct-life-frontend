# CT Island demo route (60–90 s film)

The route used to film the demo video on the Seeker. It runs in the offline demo mode, so no backend is needed. It shows the island the way the user asked: real distances, good roads, parked cars and traffic, venues people go into, and the community landmarks. The total is about 80 s of footage, split into eight shots that are cut together.

## Launch

These are development builds only. Lock the device first (the `~/.ctlife-local/seeker.lockdir` protocol). Then launch:

```
adb shell am start -n <package>/<activity> --es islandDemo 1 --es venue timeline_square
```

- `islandDemo=1` sets everything up for an offline shoot:
  - the bundled config is used
  - a fixed demo character stands in for the player (zero cash; nothing is written)
  - no rooms are joined
  - each venue gets its own NPCs locally
- `islandTour=a,b,c` fast-travels between doors, for frame-rate passes.
- `vehicleDemo=sports,heli` drives and flies the fleet by itself. Use it for driving and flying clips and their fps.
- `islandWaypoints=<shot>` (W3) teleports the player to that shot's standing spot and leaves them there, so each shot can be started from its exact place, for example `-e islandWaypoints 4`. `islandWaypoints=all` (or a list such as `2,5,6`) visits every spot for 10 s each, which works as a streaming and frame-rate pass. The spots come from `tools/world/islandgen/waypoints.py`, which writes `client/Assets/Resources/World/Island/demo_route.json`; road spots are snapped to the nearest sidewalk.

## Shots

| # | Time | Where (island metres) | What the camera sees | How |
|---|---|---|---|---|
| 1 | 0–8 s | Timeline Square plaza (0, 0) to the Timeline screens (-5, 49) | Walk out of the plaza under the bunting. The live billboards are across Genesis Avenue, with the tower core behind. | on foot |
| 2 | 8–20 s | Genesis Avenue, then Consensus Boulevard (D34 → D45 roundabout) | A sports car pulls out of a bay and takes the diagonal boulevard round the roundabout. There are parked cars, lamps and traffic lights on the way. | `vehicleDemo=sports`, or drive by hand |
| 3 | 20–30 s | Ocean Drive (-180 → 200, z ≈ -220) | The palm boulevard along the bay, with the boardwalk, beach, buoys, boats at anchor and a liner offshore. | drive |
| 4 | 30–40 s | Marina promenade (HM1 roundabout → 600, -300) | The quays with moored boats, the marina towers and the Cabal Lounge and Penthouse Row fronts. | drive |
| 5 | 40–52 s | Helipad at the marina (640, -262) → over the bay → MtnDAO | Helicopter take-off and a climb over downtown. The skyline tapers from the core to the edges, and the mountain range rises ahead. | `vehicleDemo=heli`, or fly by hand |
| 6 | 52–62 s | MtnDAO summit (-180, 515) and the lodge shoulder (-255, 410) | Fly past the snowy north face, the switchback road and the basecamp lodge with its helipad. | fly |
| 7 | 62–72 s | Builder Hills (300–450, 250–370), then Airdrop Farm (520–750, -150–220) | Descend over the Superteam builder HQ and the hills homes to the field patchwork, power lines and Sybil Barn. | fly |
| 8 | 72–82 s | Breakpoint Park convention centre (-217, 295) | Land at the park helipad (-300, 214), walk up to the convention centre door, and the walls dissolve as the player steps inside. | on foot |

## Waypoints (`islandWaypoints`)

| Shot | Id | Spot (x, z) | Facing (deg from north) |
|---|---|---|---|
| 1 | `timeline_plaza` | (0, 6) | 353 |
| 2 | `consensus_boulevard` | (96, 95) | 65 |
| 3 | `ocean_drive` | (-177, -214) | 91 |
| 4 | `marina_promenade` | (374, -283) | 94 |
| 5 | `marina_helipad` | (640, -262) | 314 |
| 6 | `lodge_helipad` | (-226, 428) | 28 |
| 7 | `builder_hills` | (380, 359) | 42 |
| 8 | `breakpoint_helipad` | (-300, 214) | 46 |

## Notes for the shoot

- The light is fixed at golden hour, sun at 30° elevation from the south-west. The best direction for long shots is towards the north-east, where the range has its sun-side rim and the haze turns blue.
- On foot, keep the camera at the default follow distance; inside venues the shell walls fade.
- Frame-rate targets: about 60 fps on foot and at least 30 fps driving and flying. W3 Seeker pass (2026-10-18, thermal state 2, which caps the target at 30): 30.1 fps at all eight waypoints. Check `CTLIFE [fps]` and `CTLIFE [vehicles] demo` lines in logcat.
- Avoid the far west cliffs in shot 5 if the frame rate drops. They are the forest's densest view.
