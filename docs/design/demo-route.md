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
- `islandShot=<shot|id>` (W4) is the one-tap set-up for filming a shot. It stands the player on the shot's spot and holds the camera on the shot's authored pose (position, aim and FOV) until the player walks off. Then Feel's camera rig takes over from the same heading. `islandShot=all` (or a list) holds each shot for 8 s, for screenshots and frame rate.
- `islandWaypoints=<shot>|all` (W3) teleports only, with the normal follow camera.
- Frame-rate bisection, dev builds only: `islandSkip=tree,grass,shadows,post` and `islandRenderScale=0.7` (pins Feel's cool-device render scale).

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

## Shot set-ups (`islandShot`, `islandWaypoints`)

The camera sits `dist` m behind the player on the line to the subject, `side` m to the right and `height` m above the feet, aimed at the subject. Source: `tools/world/islandgen/waypoints.py`.

| Shot | Id | Player (x, z) | Hero subject (x, y, z) | Camera dist / side / height, FOV |
|---|---|---|---|---|
| 1 | `timeline_plaza` | (0, 6) | (-5, 8, 49) | 5.6 / 1.5 / 2.4 m, 56 deg |
| 2 | `consensus_boulevard` | (82, 86) | (10, 22, 8) | 4.6 / 1.4 / 1.8 m, 56 deg |
| 3 | `ocean_drive` | (-136, -225) | (60, 6, -225) | 4.4 / 1.2 / 2.1 m, 54 deg |
| 4 | `marina` | (618, -412) | (505, 10, -300) | 4.6 / 1.3 / 2.4 m, 52 deg |
| 5 | `marina_helipad` | (652, -250) | (600, 0, -330) | 6.0 / -1.6 / 3.6 m, 58 deg |
| 6 | `mtndao_lodge` | (-232, 426) | (-300, 60, 396) | 7.5 / 1.8 / 3.0 m, 62 deg |
| 7 | `builder_hills` | (360, 358) | (392, 42, 372) | 7.0 / 1.6 / 1.3 m, 64 deg |
| 8 | `breakpoint` | (-276, 278) | (-262, 7, 315) | 5.0 / 1.6 / 1.9 m, 56 deg |

## Notes for the shoot

- The light is fixed at golden hour, sun at 30° elevation from the south-west. The best direction for long shots is towards the north-east, where the range has its sun-side rim and the haze turns blue.
- On foot, keep the camera at the default follow distance; inside venues the shell walls fade.
- Frame-rate targets: about 60 fps on foot and at least 30 fps driving and flying. W3 Seeker pass (2026-10-18, thermal state 2, which caps the target at 30): 30.1 fps at all eight waypoints. W4 pass (2026-10-18, `islandShot=all`, render scale pinned at 0.7, Feel's cool-device setting): 29.5–30.2 fps at every shot. The first launch after a fresh install runs at 12–15 fps for about a minute while the shaders and pipelines compile, so film from the second launch on. Check `CTLIFE [fps]` and `CTLIFE [vehicles] demo` lines in logcat.
- Avoid the far west cliffs in shot 5 if the frame rate drops. They are the forest's densest view.
