# `ids/spots.json` — home furniture and frame spots (FE-owned registry)

[Proposal] Additive. FE owns `spots.json`; these ids go upstream by PR once the home scene authors matching anchors.
Today the `home` venue has `entrance`, `exit`, `center`, `bed` (`act_sit`), `desk` (`act_type`), `sofa` (`act_sit`).

## Furniture (tappable → needs action)

`WorldInput` only raises `SpotTapped` for anchors with an `anim`, so each new spot names an existing id from `anims.json`.

| id | kind | anim | Needs action (client map in `Logic/Homes.cs` `HomeFurniture`) |
| --- | --- | --- | --- |
| `home.bed` (exists) | bed | `act_sit` | `sleep` |
| `home.sofa` (exists) | seat | `act_sit` | `sleep` (nap), `party` (club-only today) |
| `home.desk` (exists) | terminal | `act_type` | `eat` ("instant ramen at the desk") |
| `home.fridge` | stand | `emote_gm_coffee` | `eat` |
| `home.shower` | stand | `loco_idle` | `shower` |
| `home.plant` | stand | `emote_watering_can` | `touch_grass` |
| `home.speaker` | stand | `act_dance_1` | `party` |

## Frames (NFT gallery wall)

`home.wall_1` … `home.wall_12`, `kind: "frame"`, no `anim` (not claimable). `rotY` faces **into the room**; the client
hangs the frame 1.6 m above the spot and 5 cm off the wall. Positions below fit the 12 × 10 m greybox `home` walkable
rectangle (`x0 -6, z0 -5, x1 6, z1 5`); the authored basement prefab must place its own anchors and the exporter
overwrites these numbers.

```json
{ "id": "home.wall_1",  "kind": "frame", "x": -4.5, "z": 4.9, "rotY": 3.142 },
{ "id": "home.wall_2",  "kind": "frame", "x": -1.5, "z": 4.9, "rotY": 3.142 },
{ "id": "home.wall_3",  "kind": "frame", "x":  1.5, "z": 4.9, "rotY": 3.142 },
{ "id": "home.wall_4",  "kind": "frame", "x":  4.5, "z": 4.9, "rotY": 3.142 },
{ "id": "home.wall_5",  "kind": "frame", "x":  5.9, "z": 3.0, "rotY": -1.571 },
{ "id": "home.wall_6",  "kind": "frame", "x":  5.9, "z": 1.0, "rotY": -1.571 },
{ "id": "home.wall_7",  "kind": "frame", "x":  5.9, "z": -1.0, "rotY": -1.571 },
{ "id": "home.wall_8",  "kind": "frame", "x":  5.9, "z": -3.0, "rotY": -1.571 },
{ "id": "home.wall_9",  "kind": "frame", "x": -5.9, "z": 3.0, "rotY": 1.571 },
{ "id": "home.wall_10", "kind": "frame", "x": -5.9, "z": 1.0, "rotY": 1.571 },
{ "id": "home.wall_11", "kind": "frame", "x": -5.9, "z": -1.0, "rotY": 1.571 },
{ "id": "home.wall_12", "kind": "frame", "x": -5.9, "z": -3.0, "rotY": 1.571 }
```

How many frames a home shows: `min(authored wall spots, Home.frameSlots)`; until `frameSlots` exists the client uses
`ids/homes.json rooms` (basement 4, hacker house 6, studio 3, penthouse 8, villa 12) [Inference: one frame per room is a
presentation choice].
