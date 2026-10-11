READY TO COMMIT (round 7) — one commit. Use `git add -A` on these paths. The `client/Assets/Art/Candidates/` re-saves are Unity import churn and are discarded. The worktree is fast-forwarded to dev f3f4fce.
READY TO COMMIT (round 7: compact, dense Terminal Café): tools/venues/propose_spots.py tools/venues/make_venue_signs.py docs/fe-contract-proposals/ids/spots.json art/pipeline/env_config.py art/pipeline/env_custom_r6.py art/processed/environment/manifest.json art/processed/environment/_shared art/processed/environment/terminal_cafe client/Assets/Art/environment/manifest.json client/Assets/Art/environment/_shared client/Assets/Art/environment/terminal_cafe client/Assets/Scripts/Editor/VenueBuild/Venues_Cafe.cs client/Assets/Scripts/Editor/VenueBuild/VenueCatalog.cs client/Assets/Scripts/Editor/VenueBuild/VenueCloseups.cs client/Assets/Scripts/Editor/VenueBuild/VenueVerify.cs client/Assets/Scripts/Editor/VenueBuild/VenueBuildPipeline.cs client/Assets/World/Venues/terminal_cafe client/Assets/World/Venues/spaces_studio client/Assets/World/Venues/_Shared/Materials/M_hexagonal_concrete_paving_diff_1k.mat client/Assets/World/Venues/_Shared/Materials/M_hexagonal_concrete_paving_diff_1k.mat.meta client/Assets/Resources/Characters/SeatFits.asset client/Assets/Resources/Characters/AvatarController.controller client/Assets/Resources/Characters/RainController.overrideController client/Assets/Art/animations/seated docs/progress/venues.md docs/progress/requests.md art/LICENSES.md — feat(venues): Terminal Café v3: 17 x 12 m compact room (spots.json fe.8), density and warmth pass (rugs per zone, hanging planters, shelf clutter, full pastry case, framed prints, table items, sidewalk), bare-floor frame metric, seat fits re-run

## 2026-10-08 round 7: compact, dense Terminal Café; Spaces Studio checked

Evidence (`ct-life-evidence/venues/round7/`):
- `terminal_cafe-before-after-concept.png`: round 6, then round 7, at the entrance and centre spots, then the concept.
- `terminal_cafe-closeups-sheet.png`: bar, pastry case, espresso, communal, booths, round tables, lounge, armchairs,
  trading.
- Single shots `*-after*.png`, `*-spot-*.png`.
- The Spaces Studio check shots (`spaces_studio-*-check.png`).
- Seat builder reports and `venue-physicality-audit-after.json`.
- Logs `venues/logs/140`–`152`.

**New frame metric** (`VenueVerify`, every build): the share of the gameplay frame that is bare floor.
- How it works: rays through a 96 × 44 grid hit the render meshes. A hit on the shell floor, facing up, below 3 cm
  counts as bare floor; rugs, furniture and decor count as room content.
- Logged as `CTLIFE [venues] <venue> entrance|center: bare floor N% of the frame`, and stored in the verify receipt
  (`bareFloorOfFrame`).

| Venue | Entrance view | Centre view |
| --- | --- | --- |
| Terminal Café, round 6 (coordinator's estimate) | ≈60% | — |
| Terminal Café, round 7 | **25%** | **16%** |
| Spaces Studio, unchanged | 29% | 25% |

- Most of the café's remaining 25% at the entrance is the new sidewalk at the bottom of the frame.
- Spaces Studio is under the 50% threshold from the default camera (bean bags, poufs, rugs, risers and the stage fill
  it), so it was not tightened. It was rebuilt once for the metric; its content is unchanged from round 6.

**Terminal Café v3** (`Venues_Cafe.cs` rewritten for the compact layout):
- **Footprint:** 24.6 × 18 m → **17 × 12 m** (walkable x −8.5..8.5, z −6..6; `spots.json` fe.8).
- **Zones, all within about 2 m of each other:**
  - Bar along the back-left (5 m counter, full back counter).
  - Communal laptop table in the centre, two round tables right of it.
  - Booths under the back windows.
  - Trading wall (four desks) on the brick.
  - Sofa lounge and window seat by the street windows; armchairs front-right.
- **Density and warmth:**
  - Rugs: a rug under every zone (communal, each round table, each booth, the armchairs, the lounge), a runner along
    the trading desks and one in front of the bar stools.
  - Hanging greens: six macramé planters (ours) hang from the ceiling line with pothos trailing, plus pothos from the
    window heads and shelves.
  - Shelves: cluttered with jars of beans, coffee bags and cup stacks (ours), books, mugs and plants. There is also a
    tall open bookcase by the door.
  - Pastry case: full, with croissants and muffins below, doughnuts, cupcakes and a cake above, and cookies on top.
  - Prints: four framed prints on the brick (two new ones: a botanical and a coffee-cup line drawing; letter-free).
  - Small items: a cup, a mug and a pastry on a plate on every table; books and a desk lamp on the communal table.
  - Sun: stronger and warmer, with a lower fill, so the window-mullion patches on the floor read.
  - Outside: the front is now a sidewalk with hex paving, a kerb and street, lush planter boxes against the low front
    wall, two sidewalk bistro sets and lamp posts.
- **Budgets:** gameplay max **21** draws (round 6: 26); tris 99.8k (84.0k); LOD0 101.1k.
- **APK:** café LZ4 8.54 → **9.27 MiB (+0.73**, within the +1 MiB allowance). All 25 venues: 34.2 → 35.0 MiB.
  - [Inference] The release estimate goes from ≈145 to ≈146 MiB.
- **Spots:** 17/17 reachable; physicality audit 0 walk-through regions.
- **Seats:** `SeatFitBuilder` → `SeatPoseBuilder` (Build + Measure) re-run. Heights are unchanged, apart from
  `terminal_5..8`, now 0.449 m (was 0.455). Moves are listed in `requests.md`.
- **Fixes on the way:**
  - The reachability gate now prints the ASCII NavMesh map for approach failures too.
  - Small obstacles block the desks' near corners, which the seat carve leaves uncarved round the anchor.

**Life (NPC patrons):** not venue data. Venue NPCs come only from the backend room
(`server/realtime/rooms/venue.ts` `npcArchetypesFor`):
- The café gets one Wallet Tracker that stands at `terminal_4`.
- Spaces Studio gets none.
- B10 can drop café joins offline.

Request filed in `requests.md` ("patrons in Terminal Café and Spaces Studio"):
- Backend B17/B10: a barista at `counter`, typers at `terminal_1..4`, a trader at `terminal_5..8`, the studio host,
  guests and audience.
- World-life lane: aliases and looks.

## 2026-10-08 round 6: real modelled props (Terminal Café, Spaces Studio, Timeline Square)

Evidence (`ct-life-evidence/venues/round6/`):
- Before/after sheets per venue: `*-before-after-concept.png` (round 5, then round 6, at the entrance and centre spots,
  then the concept).
- Furniture close-ups: `*-closeups-sheet.png`, plus single `*-close-*.png` shots from free cameras (`-ctCloseups`).
- `r6-models-sheet.png`: every new processed model in its palette colourway, with triangles and size.
- `candidates/*.png`: the ~280 CC0 candidates, processed and rendered before choosing (seating, tables, lamps, plants,
  coffee, tech and soft goods, square, flowers).
- Seat builder reports (`seat-measure.json`, `seat-pose-build.json`, `seat-pose-metrics.json`) and the physicality
  audit (`venue-physicality-audit-after.json`).
- Logs `venues/logs/120`–`139`.

### Prop audit: what was primitive, what is a real model now

**Terminal Café**

| Prop | Round 5 | Round 6 |
| --- | --- | --- |
| Communal-table chairs (`terminal_1..4`, two end chairs), booth chairs | KayKit `chair_A` (blocky) | Quaternius spindle chair (UHI `Chair_3`): oak, green cushion |
| Round-table chairs (`table_1..4`, four extra chairs) | KayKit `chair_B` | Quaternius bentwood bistro chair (Ultimate Furniture `Chair`): walnut, green cushion |
| Bar stools ×4 | Kenney `stoolBar` + primitive cushion | Quaternius bentwood stool (`Stool`) with cushion |
| Booths ×2 | primitives (boxes) | **ours** (`booth_bench`): walnut base and frame, two seat cushions, four channel-tufted back cushions |
| Booth tables ×2 | primitives | Quaternius trestle table (`Table2`) |
| Communal table | primitives (5.2 m slab) | Two Quaternius plank farmhouse tables (`Table`) pushed together |
| Round tables ×2 | KayKit `table_round_A` | Quaternius pedestal table (UHI `Table_RoundSmall2`) |
| Coffee table, side table | primitive cylinders | Quaternius round tables (UHI `Table_RoundSmall`, lighter LOD0) |
| Sofa | KayKit `couch_pillows` | Quaternius mid-century sofa (UHI `Couch_Medium1`), green velvet, walnut legs |
| Armchairs ×2 | KayKit `armchair_pillows` | Quaternius armchair (UHI `Couch_Small1`), green |
| Cushions on the sofa, armchairs, booths, window seat | primitive boxes and blobs | **ours** (`throw_pillow`), recoloured per placement |
| Pouf | primitive cylinders | **ours** (`pouf_round`): ribbed knit |
| Espresso machine | primitives | **ours** (`espresso_machine`): enamel body, chrome top and rails, group heads, portafilters, steam wands, gauges, drip tray, cups |
| Grinders ×2 | primitives | **ours** (`coffee_grinder`): bean hopper, lid, spout |
| Pastry case | primitives + a printed sign | **ours** (`pastry_case`): open front with a chrome frame, holding Kenney croissants, doughnuts and a cake |
| Cups, mugs, to-go cups | Kenney models + primitive stacks | Kenney models only (`mug_lite`, `cup_coffee`) |
| Laptops ×4 | primitives + a sign | Kenney `laptop`, with a candle-chart sign on the leaning display |
| Monitors ×8, keyboards ×4 | primitives + a sign | Kenney `computerScreen` with a chart sign, Kenney `computerKeyboard` |
| Desks ×4, office chairs ×4 | Quaternius models | same models, lighter LOD0 |
| Pendants ×9 | primitive cones | **ours** (`pendant_dome`): enamel dome, white inside, brass cap; plus the glowing bulb |
| Desk lamp, floor lamp | primitives; KayKit `lamp_standing` | Quaternius banker's desk lamp (UHI `Light_Desk`); floor lamp kept |
| Floor plants ×17 | Quaternius houseplants (black pots) + primitive pots | Quaternius houseplants recoloured with terracotta or cream pots (`cafe_plant_*`) |
| Hanging pothos | primitive blobs | **ours** (`pothos_vine`): a stem with two-tone leaves |
| Books | primitive boxes | Kenney `books` |

Still primitive in the café, on purpose:
- Room joinery: the bar counter and back counter, which now have a rounded nosing, foot-rail brackets and cabinet
  doors; shelves, the window seat and window frames; the planter boxes.
- Printed decor: signs, rugs and the neon.

**Spaces Studio**

| Prop | Round 5 | Round 6 |
| --- | --- | --- |
| Stage armchairs (`stage_4..7`) | KayKit `armchair_pillows`, tan | Quaternius armchair (UHI `Couch_Small1`), cream with walnut legs, plus our throw pillow |
| Pod stools (`stage_1..3, 8..10`) | Kenney `stoolBar` + primitive cushion | **ours** (`bar_stool_ring`): slim legs, brass foot ring, rose / petrol / purple cushion |
| Bean bags (`seat_1..10` + one decor) | primitive blobs | **ours** (`bean_bag`): seat hollow, soft back |
| Riser poufs (`seat_11..20`) | primitive boxes | **ours** (`pouf_round`) |
| Floor cushions | primitive boxes | **ours** (`floor_cushion`): tufted |
| Stage coffee table, lounge and backstage tables | primitive cylinders and boxes | **ours** (`table_oval`): oval walnut |
| Side tables, drinks | primitive cylinders | Quaternius round table (lighter LOD0), Kenney mugs |
| Green-room sofa (`greenroom_1/2`), backstage couch | Kenney `loungeSofa`; KayKit `couch_pillows` | Quaternius mid-century sofas, teal and plum |
| Terrace benches ×4 | primitives | **ours** (`bench_cushioned`): slatted base, seat cushion, three back cushions, in purple and teal |
| Terrace lanterns ×8 | primitives | **ours** (`lantern_floor`): wooden frame, glowing core |
| Headphones on the shelf, wall and lounge table | primitive blobs | **ours** (`headphones`) |
| Fridge | primitive box | Quaternius fridge (UHI `Kitchen_Fridge`) |
| Laptop on the host desk | Kenney `laptop` | same, café colourway |
| Planter fill | Quaternius houseplants | lighter variants of the same plants |

Still primitive in the studio, on purpose:
- Studio build: the host desk, stage, risers, truss and fixtures.
- Kit built from parts: mic stands with phone holders, the broadcast camera, coat rail, bar cart, credenza, the
  headphone shelf body and the record player.

**Timeline Square**

| Prop | Round 5 | Round 6 |
| --- | --- | --- |
| Fountain | primitives (cylinders, arcs) | **ours** (`fountain_tiered`, lathe-profiled): moulded basin ring, pedestal, ringed column, scalloped lower bowl, upper bowl, pine-cone finial, water surfaces. The water streams stay builder geometry. |
| Lamp posts ×4 | primitives | Quaternius cast-iron streetlight (Modular Streets `Streetlight_Single`), warm glass, lighter LOD0 |
| Café terrace tables ×3, chairs ×9 | KayKit table and chairs, recoloured | Quaternius pedestal tables (black) and bentwood chairs (black, cream cushion) |
| Market stall (the dog stall) | primitives | Kenney Fantasy Town `stall-red`, canopy recoloured to the stall's orange, produce on the counter. The egg pennant, balloon and sunflowers are kept. |
| Produce crates ×12 | primitive boxes + spheres | KayKit restaurant crates (carrots, tomatoes, lettuce, onions, potatoes), flattened to the palette, lighter LOD0 |
| Sacks | primitive blobs | **ours** (`sack`): tied burlap |
| Flower pots ×~30 | primitive cylinders | Kenney `pot_small`, terracotta |
| Flowers in every bed, trough, pot and basket | primitive blobs + spheres | Quaternius flowering bushes, flower clumps and tulips (alpha-cut foliage), all in one atlas draw |
| Citrus trees ×4 | primitive blobs on a pipe | Quaternius tree (`MapleTree_4`) with its greyscale leaves recoloured green by us, plus the oranges |
| Benches ×6 | our `park_bench` model | unchanged (already a model) |

Still primitive in the square, on purpose:
- Buildings and their facade details: windows, awnings, vines, wisteria.
- Stone planters and troughs, the south wall, railings and gate pillars.
- The ORE plot, string lights and bunting.
- The eggs (our own drawings, built from parts).
- Barrels and the bicycle: no CC0 model on disk fits.

### How
- **Asset pipeline (`art/pipeline`):**
  - `env_config.py` has a new "Round 6" section listing every new model.
  - `env_process.py` gains:
    - rule-based recolouring: HSV ranges map to palette colours, so one source ships as several colourways on the
      shared palette material (no new draws);
    - `force_palette` for KayKit and Kenney gradient maps (alpha-cut foliage keeps its texture) and a per-item colour
      `quant`;
    - `smooth: <degrees>` (smooth shading with hard edges above the angle);
    - `decimate` (a lighter LOD0 for props placed by the dozen); LODs as before.
  - `env_custom_r6.py` builds our own models. Every chosen model was rendered first (`candidates/`).
- **Fetched today:** Kenney Fantasy Town Kit 2.0. Its CC0 was checked on the page and in its `License.txt` on
  2026-10-08; it is unzipped in this worktree at `art/raw/kenney-fantasy-town-kit/`, which git ignores.
- **Licences re-checked live today:** Quaternius Ultimate Furniture, Ultimate House Interior and Ultimate Stylized
  Nature pages say CC0; the Kenney Furniture Kit page says CC0.
- **Poly Pizza:** not used, in line with the `LICENSES.md` note on it. I first tried its CC0 monitor and headphones,
  then replaced them with Kenney `computerScreen` and our own headphones.
- **Venue builder (`VenueKit`):**
  - `FoliageAtlas` (opt-in): alpha-cut foliage textures from sourced models are packed into one 1024 atlas per venue
    and merged into one renderer, instead of one draw per material per 10 m cell. A submesh whose UVs leave 0..1 keeps
    its own material, and no shared material asset is written for foliage routed to the atlas.
  - `VenueCloseups` with the `-ctCloseups` flag renders the furniture close-ups.
- **Feel-lane requests folded in:**
  - fe.7 anchor nudges: `terminal_1..4` 0.10 m towards the table, `terminal_5..8` 0.25 m towards the desks. The desks,
    laptops and keyboards stay put.
  - bench_4: the ORE plot fence moved off the bench back.
  - `SeatFitBuilder` and `SeatPoseBuilder` (Build + Measure) re-run on the new prefabs. The changed seats are listed in
    `requests.md`.
- **Seat kinds kept:** the bean bags got a real seat hollow and back, and the pod stools slim legs and a low foot
  ring, so `SeatFitBuilder` still classifies them as BeanBag and Stool.

### Budgets (editor estimates; venue only)

| Venue | Gameplay max draws | Gameplay tris | LOD0 tris | APK LZ4 (single venue) |
| --- | --- | --- | --- | --- |
| Terminal Café | 31 → **26** | 67.5k → **84.0k** | 68.3k → 84.2k | 7.22 → 8.54 MiB |
| Spaces Studio | 23 → **26** | 89.7k → **111.3k** | 104.0k → 121.4k | 8.25 → 9.22 MiB |
| Timeline Square | 14 → **24** | 103.4k → **129.7k** | 103.6k → 136.5k | 5.36 → 7.40 MiB |

- **Draws with the crowd:** Timeline 24 + 85–106 crowd ≈ 109–130, under the 150 budget.
- **Where Timeline's +10 draws come from:**
  - +1 for the foliage atlas;
  - the Kenney food-kit material, for mugs and croissants on the terrace;
  - more palette cells, now that props sit across the whole square.
- **Triangles:** about +20–25% per venue, with lighter LOD0 variants on the props placed most often.
  [Unverified] I have not measured frame time on the device; the 60 fps headroom needs a device check.
- **APK** (all 25 venues, Android, one bundle): LZ4 30.3 → **34.2 MiB** (+3.9), raw 68.3 → 75.0 MiB.
  - [Inference] With the reported ~141 MiB APK, the release would be about 145 MiB, under 150.
  - Receipt: `receipts/venue-size-r6.json`.
- **Physicality audit:** 0 walk-through regions in all three venues. The "collider, NavMesh not carved" regions
  (café 14, studio 29, Timeline 6) sit at seat anchors, where carving is clipped by design.
- **Spots:** all reachable (café 17, studio 36, Timeline 15).

## 2026-10-08 round 5: Terminal Café v2, Spaces Studio truss, Timeline Square lushness

Evidence (`ct-life-evidence/venues/round5/`):
- `terminal_cafe-before-after-concept.png`: the round 3/4 café, then v2 at the entrance and centre spots, then the concept.
- `terminal_cafe-spots-eggs.png`: game camera at `counter`, `terminal_5` and `table_1`.
- `studio-on-mic-before-after.png`: the device shot, then the editor capture at `stage_host`.
- `timeline_square-before-after-concept.png`.
- Single shots `*-after*.png` and `venue-physicality-audit-after.json`.
- Logs `venues/logs/110`–`116`.

**Terminal Café v2** (new builder `Venues_Cafe.cs`; this replaces the repack of the authored prefab):
- **Layout, west to east:**
  - West: big open street windows with the sun streaming in, a window seat with a mustard cushion, and the sage sofa
    lounge with rug, pouf and floor lamp.
  - North-west: the wooden bar with a green-tile front, pastry case, stools and till. Behind it, the back counter with
    a green espresso machine, grinders and cup stacks, open shelves, the chalkboard menu (doodles only), and pendants.
  - Centre: the long communal work table with laptops (`terminal_1..4`) and two round tables on rugs (`table_1..4`).
  - North-east: green booths under the back windows, with pots on the sill and pothos.
  - East: exposed brick, with the sunrise-coffee neon (a cup with rays, no letters) above four twin-monitor trading
    desks with candle charts (`terminal_5..8`), shelves and two prints.
  - South-east: armchairs on a rug.
  - Throughout: plants (potted big-leaf plants, front planters) and hanging pothos.
- **Town outside.** Painted town panels (backdrop: not lightmapped, no shadows) stand just across the street on the
  west, north and east sides, with trees and lamps. Through the windows, the game camera sees shopfronts with blue
  awnings, not sand.
- **Eggs** (`VenueEggs.cs`, our own drawings, shared with Timeline Square):
  - The JUPCAT astronaut cat on the window-seat cushion.
  - The pink-beanie dog under the communal table.
  - The three-lens phone on the bar.
  - The purple ghost plush on the middle shelf.
  - The O-mouth cat by the till.
- **Seats (`spots.json` fe.6).** Every terminal and table seat and the counter moved onto the new furniture, with the
  same ids. `entrance`, `exit`, `center` and `pulse_screen` are unchanged.
  - `CafeSeatMotionProfile` is still bound from the authored café, and its 12 exit routes are regenerated.
  - The old → new table for the character-feel lane is in `requests.md` ("Terminal Café v2 moved every seat").
- **Budgets:** gameplay max 31 draws / 67.5k tris; the whole venue at LOD0 is 33 / 68.3k. Round 4 was 25 / 68k.
  Lighting: 2 lightmaps (327k texels) and 160 probes. Spots 17/17 reachable.
- **Physicality audit:** 0 walk-through regions. Ten "collider, NavMesh not carved" regions sit at seat anchors,
  where carving is clipped by design.

**Spaces Studio truss** (the `requests.md` item from device round 20261008c):
- The overhead truss ring is removed.
- Fixtures hang on two truss towers at the stage wings (x ±6.7, z 2.1, 5 m) and on an upstage bar (z 9.2, y 5.6) with
  four back lights.
- Nothing crosses the camera line for `stage_host` or `stage_1..6`, so no occluder fading is needed.
  [Inference] The editor capture uses the venue camera settings, not the device follow camera; a device check
  at `stage_host` would confirm it.
- **Budgets:** 23 draws / 89.7k tris (unchanged). Spots 36/36 reachable.

**Timeline Square lushness** (small budget):
- Vines and wisteria garlands on the facades.
- Hanging baskets on the lamp posts, on wall brackets and along the awning edges.
- Fuller flower clumps (10 blooms each, one trailing).
- A produce table with sacks by the market stall.
- **Budgets:** 14 draws (unchanged), 103.4k tris (was 86.8k). [Inference] With the 85–106-draw crowd that is
  about 120 draws, under the 150 budget. Spots 15/15 reachable.

**APK** (`VenueMeshCompression.MeasureVenues`, Android, all 25 venues in one bundle):

| | Round 4 | Round 5 |
| --- | --- | --- |
| Raw | 73.7 MiB | 68.3 MiB |
| LZ4 | 31.4 MiB | 30.3 MiB (−1.1 MiB) |

The café's unused repack atlases and meshes are deleted. Per-venue LZ4 changes: Timeline +0.49 MB, café +0.07 MB,
studio unchanged. Receipt: `ct-life-evidence/venues/receipts/venue-size-r5.json`.

## 2026-10-08 round 4: Timeline Square v3, Spaces Studio polish, solid props, APK

Evidence (`ct-life-evidence/venues/round4/`): `timeline_square-before-after-concept.png` (v2 vs v3 from the default game
camera at the entrance and the centre spot, then the concept), `timeline_square-eggs-views.png` (game camera at four
named spots), `spaces_studio-before-after-concept.png`, single shots `*-after-*.png`, and
`venue-physicality-audit-after.json` (the character-feel lane's VenueFeelAudit, re-run on all 26 venues/homes).

**Timeline Square v3** (layout build, default camera 11.5 m / 31°, the 14.5 m camera is reverted):
- **Compact square.** The plaza is 23 × 22 m (v2 was 41 × 31), plus the street to the town hall steps. Building fronts
  close three sides: the café terrace and bakery on the left, the toyshop, parcels shop and market stall on the right.
  The fountain sits in the middle of the frame from both the entrance and the centre spot.
- **Dense dressing:**
  - Fountain and square: flower troughs and pots on the fountain deck, a ring of planters round it, stone paving rings
    and a bordered path from the gate.
  - Street furniture: lantern posts and wall lanterns, citrus trees, three strings of lights across the square.
  - Café and market: café umbrella table, crates of fruit, barrels.
  - Buildings: vines and wisteria, window boxes.
  - Approach: steps up to the town hall; a stone wall with railings and a gate on the camera side.
- **Eggs:** all 15, 1.5–2× their v2 size via the new `VB.Scaled` block, still props.
- **Live surfaces.** The game camera's frame top meets the north fronts at about 3 m, so the screens moved to the ground
  floors: `bb_01` / `bb_02` (ad slots `billboard_01/02`) on the left north house, the Timeline screen on the right one.
  The Pixel Wall is on the west corner house. All four are LiveSurfaces `Screen_*` materials.
- **Skyline** (Colosseum-style hall, clock tower, hills, hill town) is visible in the overview and when zoomed out.
  [Inference] With the player at 30% from the bottom and a 45° FOV, no default-camera framing reaches above the
  north fronts, so the skyline cannot be in the default view.
- **Budgets:** 14 draws max, 86.8k tris (v2: 10 / 87k); 2 lightmaps (1.31 M texels); 200 light probes.
  Spots 15/15 reachable.
- **Spots (`spots.json` fe.5):**
  - Moved into the compact square: crier_1/2/3, entrance, exit, pixel_wall and billboard_view.
  - Walkable rectangles shrunk to the square.
  - Unchanged: bench positions and facing, center and square_center.

**Spaces Studio polish** (camera yaw −12° → +6°):
- **Terrace and city.** The glazing moved in to x 8.6, so the walk-out terrace sits inside the room's footprint and the
  default view. It has cushioned benches, lanterns, planters, palms and zig-zag string lights.
- **Bunting.** The multi-flag bunting runs along the terrace, flags turned to the camera.
- **City.** The studio now sits on a rooftop: city blocks stand below the parapet and towers rise beyond. Backdrop
  models merge into 40 m cells (`BackdropCellSize`), which keeps draws at 23.
- **On-air lamp** moved to 3.3 m beside the LED wall, which is lowered so it is fully in frame.
- **Riser LEDs:** warm strips on the top edges, the back edge and the ends of the risers.
- **Spots:** stage_5/6 are 0.3 m further apart (fe.5) so their side-step approaches stay reachable with carved chairs.
- **Budgets:** 23 draws, 89.8k tris (round 3: 23 / 69k); spots 36/36.

**Solid props, carved NavMesh (character-feel request, docs/progress/requests.md):**
- **Colliders.** Every prop gets a solid collider. Seat tap targets no longer count as cover, so benches and sofas get
  full furniture colliders. [Inference] Taps still resolve the seat, because WorldInput falls back to the nearest
  claimable spot within 0.9 m.
- **Carving.** Every prop is carved from the NavMesh, seat furniture included. The carve is clipped so each spot anchor
  and approach point keeps 0.6 m of walkable NavMesh (carve the furniture, never the anchor or its approach). Clipped
  pieces under 0.35 m are dropped, because the 0.17 m voxels would not carve them.
- **Geometry scan in the build gate** (`VenueCollision.GeometryRegions`), same criteria as VenueFeelAudit, computed
  analytically:
  - It scans the merged render meshes on a 0.2 m grid at body height (0.25–1.9 m) where the NavMesh is under them.
  - Anything walk-through or uncarved gets a collider and a carve during the build, then the build fails if any remains.
  - Areas within 0.75 m of anchors and approaches are skipped.
- **Applied to all 25 venues:**
  - Timeline, Studio and Café were rebuilt.
  - The other 22 venues were patched by `VenueCollisionPass` (no re-bake).
  - The reachability error now prints an ASCII NavMesh map round the failing spot. It showed two things:
    - Carving whole seat chairs closed the VC Row back seats and the studio's centre approaches in. That is why
      anchors are kept clear as well as approaches.
    - A café bookshelf (round-3 dressing) closed the barista's way behind the counter once the menu partition was
      carved. The bookshelf is removed.
  - Every spot and every approach point in all 25 venues is reachable (receipts `receipts/<id>-collision.json`).
- **Audit result:**
  - Timeline Square: 23 walk-through regions on dev → 0.
  - All venues: 1 walk-through region left (2 samples on the hacker-house bunk rail, beside the bed anchor).
  - 142 "collider, NavMesh not carved" regions remain, all within 0.8 m of a seat or terminal anchor. They are the
    furniture deliberately left walkable at the anchor so NPCs and the approach / align / sit path reach it.

**APK.** `VenueMeshCompression.MeasureVenues -buildTarget Android`: all 25 venue prefabs and their dependencies in one
Android asset bundle (LZ4 / raw):

| State | Raw | LZ4 | Timeline (LZ4) | Studio (LZ4) | Café (LZ4) |
| --- | --- | --- | --- | --- | --- |
| dev before round 3 (dedfe79d) | 118.5 MiB | 45.1 MiB | 11.92 MiB | 5.09 MiB | 8.26 MiB |
| round 3 merged | 73.3 MiB | 31.3 MiB | 5.11 MiB | 7.95 MiB | 7.16 MiB |
| round 4 (this) | 73.7 MiB | 31.4 MiB | 4.90 MiB | 8.26 MiB | 7.16 MiB |

- Round 4 adds +0.1 MiB LZ4.
- Since the 148.4 MiB APK, venue data is down 13.7 MiB LZ4. [Inference] With no other lane's additions the release
  would be about 135 MiB. Unity's Android build compresses player data with LZ4 (project setting not checked) and the
  APK zip adds a little, so this is not an APK build.
- How it was measured: the old venue folders were checked out for the measurement, then the round-4 folder was restored
  from a tar backup. The checksums and `git status` were verified identical afterwards.
- The worktree's Library is now on the Android target.

## 2026-10-08 round 3, task 3: look-dev, Timeline Square v2 and Terminal Café

**Evidence** (`ct-life-evidence/venues/round3/`):
- `timeline_square-before-after-concept.png`: before, after (entrance and centre spots, game camera), the in-game concept
  and the title art.
- `timeline_square-eggs-views.png`: five game-camera views at named spots, showing the eggs.
- `terminal_cafe-before-after.png`.
- Single shots `*-after-game*.png` and `*-after-overview.png`.

**Look-dev, shared by the three venues:**
- **Grade:** a global Volume per venue with Neutral tonemapping, colour adjustments, white balance, threshold bloom
  (emissive lights only, high-quality filtering off) and a light vignette. `VenueLook` turns post-processing on for the
  gameplay camera; `VenueLook.PostProcessingEnabled` is the Settings switch.
- **Probes:** baked light probes for characters (Timeline 510, café 160, studio 180) and a baked reflection cubemap.
- **Lightmaps:** baked AO is on (it already was).
- **Sky:** the clear colour matches each painted sky.
- **Renderer:** the URP renderer needed its `postProcessData` set (it was empty).

**Timeline Square v2 (layout build; the old repack source is unused):**
- **Square:** painted seamless cobbles; a three-tier fountain on a round raised deck with flower troughs; six park
  benches with flower pots; lantern posts; citrus trees in stone planters.
- **West:** blue café with a striped awning and terrace, the Pixel Wall house, a bakery with a red awning and a
  balcony, a vine-covered corner house.
- **North:** two rows carrying billboards `bb_01` / `bb_02`, a street with steps up to the town hall and its Timeline
  screen, the clock tower.
- **East:** toyshop, parcels shop, market building.
- **South:** a stone wall with iron railings and flower troughs.
- **Background:** a hill town, cypresses, the Colosseum-style hall, a golden-hour sky with sea and hills.
- **Light:** golden-hour sun from the south-west and warm lanterns. The camera is 14.5 m / 33° (was the 11.5 / 31
  default) so the plaza and its facades read.
- **Live surfaces** (WorldLife `LiveSurfaces`, `Screen_*` materials with their own idle textures via the new
  `VB.LiveScreen`): `timeline_screen`, `pixel_wall` (gets the `pixel_tile_*` slots) and `bb_01` / `bb_02` (get
  `billboard_01/02`). Only the Timeline screen existed before.
- **15 Tier A eggs**, our own drawings with no logos or letters, placed as in the title art:
  - **Café:** Jupiter-style astronaut cat napping on the café sill; WIF-style tan dog in a pink knit hat under a café
    table; Seeker-style three-lens phone and coffee on that table.
  - **West side:** Phantom-style purple ghost (scarf, round cheeks) peeking from a lamp post; Popcat-style O-mouth cat
    with a doughnut on a string in the bakery window.
  - **South-west corner:** ORE 5×5 copper tiles with pickaxe and wheelbarrow, behind an iron fence.
  - **East side:** Claynosaurz-style clay dino in the toyshop window; Mad Lads-style red burst poster; Helius-style
    orange orb lamp over parcels.
  - **Square, east and front:** BONK-style yellow/orange stall with a red "!" pennant and a mouthless dog balloon;
    Pudgy-style igloo shaved-ice cart with a blue penguin plush and a wizard-hat penguin; pump.fun-style capsule
    machine (green/white capsules).
  - **Sky and background:** Superteam-style bunting (two strings: purple/yellow pennants and country flags); the
    Solana gradient as a sky wash only; the Colosseum-style arched hall with a burgundy banner.
- **Spots (`spots.json` fe.4):**
  - `center` → (0, −5.6) and `square_center` → (0, −7.0), off the fountain.
  - The six benches now face the fountain. rc1 rotY values were 30–150° off, inconsistently.
  - Everything else is unchanged.

**Terminal Café look-dev (still a repack of the authored prefab):**
- **Why it stays a repack:** the character-feel lane fitted the typing and social sit motions to this furniture
  (`CafeSeatMotionProfile`).
- **New `VenueSpec.dress` hook:** adds dressing to the source instance before the merge, atlas and bake.
- **Light:** the desk lamps are lit, glass table lamps sit on the social tables, and brass wall sconces carry baked
  warm lights.
- **Dressing:** a deep-green wainscot with an oak rail; plants, lounge bookshelves, framed pictures, cake, doughnuts
  and mugs on the counter, and a back-bar shelf.
- **Removed:** the two floating chandeliers (their baked sun shadows read as floor stains); their lights stay.
- **Gate:** parity against the authored source is OK. Anchors, approaches, the profile and the source colliders are
  unchanged (`receipts/terminal_cafe-parity.json`).

**Budgets (batchmode estimates, Android quality level, LOD bias 0.7):**

| Venue | Gameplay draws (max) | Gameplay tris | Whole venue | Lightmaps |
| --- | --- | --- | --- | --- |
| Timeline Square | 10 | 87k | 13 draws / 87k | 2 × 1024² |
| Spaces Studio | 23 | 69k | 40 draws / 94k | 2 × 512² |
| Terminal Café | 25 | 68k | 25 draws / 80k | 2 × 512² |

All spots are reachable and the collision gate passes everywhere.

**APK** [Inference, not an APK build]: source folder size changed as follows.
- **Timeline:** 54.5 → 24.4 MB. The repack's four atlases are gone (they were unreferenced and are deleted).
- **Café:** 26.5 → 18.6 MB.
- **Studio:** 14.1 → 19.1 MB.
- **Net:** about −33 MB of source assets. No APK was built in this lane.

**Not matched yet:**
- The painted concepts' surface detail and their people. In-game, the crowd supplies the people.
- **Café:** no in-game concept exists, so it was graded and dressed toward the key-art warmth only. The authored
  shell (beige walls, open top) is unchanged.

## 2026-10-08 round 3, task 2: Spaces Studio v4 (to `ingame-spaces-concept-v1.png`)

**Before/after/concept:** `ct-life-evidence/venues/round3/spaces_studio-before-after-concept.png` (before = dev build,
after = batchmode game camera at the entrance and at the centre spot, baked + graded; then the concept). Single shots:
`round3/spaces_studio-after-game.png`, `-after-game-center.png`, `-after-overview.png`.

**Why it read as a café:** 20 seats in a 5 × 4 grid and 10 stage stools in a line. The layout now follows the concept:
- **Stage:** a round two-step stage (honey/walnut wood, oak nosings, amber LED strips under both nosings with soft
  baked spill), a cream woven rug, four tan armchairs in a talk-show semicircle with coloured cushions and boom mic
  stands, a low round table, and the host desk (sun lamp, mixer, laptop, plant, boom mic).
- **North wall:** the teal LED waveform (live hook `Hook_SpacesWaveform`), fluted purple/teal acoustic panels, walnut
  slats, spot cans, speakers on tripods, the red on-air lamp (live hook `Hook_OnAirLamp`, idle dim), a truss with six
  warm spots.
- **Audience:** bean bags on two floor arcs and square poufs on two curved wooden risers with amber LED nosing, rugs,
  drink tables, floor cushions.
- **West:** green-room lounge (teal sofa with pillows, record player, headphone shelf, slatted partition, rug, lamp)
  beside the backstage block: rainbow neon arch over the door (Colosseum-style arch), a lanyard frame (three big
  conference lanyards) and a lanyard poster wall (Breakpoint-style). Backstage room behind the door (couch, coat
  rail, vanity mirror, fridge, plants).
- **East:** open glazing onto the terrace (cushioned benches, lanterns, string lights, planters, palms) with one
  multi-country flag bunting (Superteam-style), the evening city and a painted sky with a mountain range.
- **Branded nods (our own drawings, no logos or letters):** Solflare-style sun lamp on the host desk, Seeker-style
  three-lens phones on the pod mic stands and the host desk.
- **Added:** a sponsor screen `Screen_bb_studio` on the backstage block. It is a LiveSurfaces billboard, so the
  contract's studio ad slot `billboard_03` now has a surface; it shows the stage feed when idle.

**Spots (`spots.json` fe.4, positions/rotY only; ids, kinds and anims unchanged):**
- `seat_1..20` sit on four arcs (r 8.2 / 10.2 / 12.2 / 14.2 m) round the stage centre (0, 6.2), facing it.
- `stage_4..7` form a shallow talk-show arc facing the room (rc1 x kept, turned 8–15° inwards, z 5.2–5.75);
  `stage_1..3` / `8..10` are pods hugging the stage (r 5.65 m).
- **Fitted seat motion kept:** the dev studio carried the character-feel lane's `CafeSeatMotionProfile`, so v4 binds it
  again. `VenueSeatMotion` copies the animator bindings from the authored source and regenerates the 30 exit routes
  from the new approaches. Each approach is a 0.95 m side step, right of `seat_*` and left of `stage_*`, matching the
  fitted exit motions; the build fails if a route would be rejected at runtime.
- [Inference] The fitted social sit pose was made for chair-height seats. Bean bags (seat about 0.35 m) and bar stools
  (0.75 m) may need a refit by the feel lane.
- `greenroom_1/2` are new (lounge sofa).
- `stage_host`, `entrance`, `exit` and `center` are unchanged.
- **Heights:** stage seats sit at y 0.22, `seat_11..15` at 0.12, `seat_16..20` at 0.24.
- **Merge note:** dev's fe.3 (builder_hills) is kept. My worktree was fast-forwarded to dev 981f134f before
  writing fe.4.

**Builder additions (shared, used again for Timeline Square):**
- `VB.Arc` and `VB.WalkableArc`: curved blocks and walkable decks.
- `Sign(..., lit: true)` / `FloorSign(..., lit: true)`: printed decor that is lit and lightmapped instead of unlit.
- `VB.LiveScreen`: a `Screen_<id>` material with its own idle texture, for WorldLife LiveSurfaces.
- `BoxRot`, `CylinderRot`, `Triangle`, and smoother `Blob` options.
- Entrance and centre gameplay captures at the concepts' 1870×841.
- Palette: appended `rose`, `dustpink`, `tan`, `honey`, `sage`. Existing swatches are unchanged.

**Numbers** (final build, log `logs/70-studio-timeline.log`):
- **Gameplay views:** 23 draws max, 68.8k tris (entrance), 19 draws / 53k (centre).
- **Whole venue:** 40 draws / 94k tris at LOD0.
- **Bake:** 2 lightmaps (512²), 180 light probes, baked reflection cubemap.
- **Gates:** 36/36 spots reachable. Collision gate: 86 props, 35 seats, 51 obstacles.

**Not matched yet:**
- The concept's painterly surface detail (fabric folds, wood grain).
- The camera-facing city view: the gameplay camera looks north, so the terrace is at the right edge of the frame.

## 2026-10-08 round 3: APK, venue mesh compression

`VenueMeshCompression.Run` sets Unity mesh compression to **Low** on every venue mesh asset (764 meshes in 25 venues).
`VenueBake` applies the same level to new UV2 copies.
- **What it does:** the vertex data is quantised on disk and decompressed on load
  ([Unity manual](https://docs.unity3d.com/6000.3/Documentation/Manual/types-of-mesh-data-compression.html)).
- **Why Low:** Low keeps UV precision high for lightmap UV2 and atlas UVs. Medium would cut UVs to about 10 bits.

**Measured** with `VenueMeshCompression.Measure` (a throwaway asset bundle of the meshes, StandaloneOSX target).
These are not APK numbers:

| Mesh set | Raw | LZ4 |
| --- | --- | --- |
| 3 repacked venues | 33.0 → 15.9 MiB | 15.1 → 9.6 MiB |
| All 25 venues | 71.6 → 34.5 MiB | 31.3 → 19.9 MiB |

The 33.0 MiB raw figure matches the lead's 32.9 MB for the repacked venues.

**Visual check:** the Timeline Square and Perp Pit previews are pixel-identical to the uncompressed ones; 2 of 518k
pixels differ by more than 24/255. No re-bake was needed.

## 2026-10-08 round 3, task 1: prop collision (all 25 venues)

**Problem.** Characters walked through chairs, tables, benches and plants. Movement uses a `NavMeshAgent`, so props had
to be both physical colliders and holes in the NavMesh; most props were only render geometry.

**What changed.** `VenueCollision.cs` defines the rules and the gate:
- **Which objects are props:** every floor-standing model whose footprint touches the walkable area, and registered
  procedural pieces (figures, stanchions, hay bales, rocket).
- **Colliders:** each prop gets a `BoxCollider` fitted to its oriented bounds:
  - Trees and poles (parasols, lamps, mic/IV/light stands): trunk or pole only.
  - Overhead and table-top items are skipped.
  - Props are collided up to 2.2 m.
- **NavMesh:**
  - Non-seat props also become not-walkable NavMesh volumes. Obstacles now extend 0.5 m below the floor so they
    always carve.
  - Seat furniture (a claimable spot inside its footprint) gets a collider only, so seat anchors and approach points
    are unchanged and still reachable.
- **Build gate:** the build fails if any prop has no collider (under 60% footprint coverage) or a non-seat prop is
  still on the NavMesh. The gate runs in full builds and repacks.

**Applying it to existing prefabs.** `VenueCollisionPass.Run` patches the baked prefabs without touching meshes or
lightmaps:
- **Layout venues:** the venue script reruns in a `DryRun` mode to recover its props. The prefab's Colliders group
  and NavMesh are then regenerated.
- **Repacked venues** (Timeline Square, Terminal Café, Spaces Studio): props come from the source prefab's manifest
  model instances. A "Prop colliders" group is added, and the NavMesh is rebuilt from:
  - the walkable rectangles,
  - the existing solid colliders,
  - prop obstacles.

**Results.**
- **Gate:** all 25 venues pass, and every spot stays reachable. Receipts are `receipts/<id>-collision.json`.
- **Prop counts:** Timeline 79 props (73 obstacles), Café 50, Studio 46, Sybil Barn 43, Rekt Clinic 33, the rest 6–20.
- **Parity:** the repacked venues still match their sources (anchors, approaches, `CafeSeatMotionProfile`, live
  surfaces).
- **Bugs found and fixed along the way:**
  - Obstacle boxes whose bottom sat exactly on the floor did not carve.
  - Parasol canopies blocked Yacht Club's `gangway`.

Run: `Unity -batchmode -nographics -quit -projectPath client -executeMethod CTLife.EditorTools.VenueBuild.VenueCollisionPass.Run [-ctVenues ids]`.

## 2026-10-08 draw-call pass: Timeline Square, Terminal Café, Spaces Studio

Request: `docs/progress/requests.md` (integration lead, 2026-10-08). Done in the worktree on dev @ 60c5fcb; not committed.

**How.** New `VenueRepack.cs` (selected by `VenueSpec.repackSource`) rebuilds each venue from its shipped prefab instead
of re-authoring it, so the tuned layout and gameplay data stay byte-identical:
- Kept: anchors, approaches, colliders, NavMesh (copied), camera settings, `VenueContentLighting` and
  `CafeSeatMotionProfile` (exit routes and controller bindings).
- Re-emitted geometry:
  - Merged per material into LOD-grouped cells (source LOD0/LOD1).
  - Non-tiling textures (palettes, colour maps, signs, desktop screens) go into per-venue atlases with tints baked in.
  - Bounded tiled textures (bark, paving, plaster) are baked out as repeated tiles into a fourth atlas.
  - Live `Screen_*` surfaces (WorldLife `house_ads.json` rules) stay one renderer per owner under `Hooks/`, with the
    exact material name.
- Rebaked with `VenueBake` (GPU, ≤ 1024² maps) and verified with `VenueVerify`. Registered with
  `VenueRepack.Register`, which updates the registry, syncs the catalog and checks that `VenueContent.Find` resolves
  to the new prefab.

**Results.** Editor estimates from the gameplay camera, Android LOD bias 0.7, landscape. Before = shipped prefab,
after = repacked and baked.

| venue | entrance draws | centre draws | far draws | entrance tris | centre tris | far tris | materials | lightmaps |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| timeline_square | 181 → **28** | 103 → 24 | 45 → 17 | 120,754 → 113,491 | 88,776 → 122,393 | 54,628 → 80,361 | 46 → 5 | 5 → 2 (≤ 1024²) |
| terminal_cafe | 194 → **25** | 147 → 17 | 63 → 9 | 49,404 → 62,155 | 53,794 → 61,497 | 27,216 → 55,249 | 34 → 4 | 2 (≤ 1024²) |
| spaces_studio | 116 → **17** | 72 → 12 | 37 → 9 | 24,853 → 23,240 | 16,020 → 21,196 | 9,545 → 17,038 | 21 → 4 | 2 (≤ 1024²) |

**Trade-off.** Draws fall 85–87%, but the cells cull less precisely than the old per-model renderers, so some views
draw more triangles:
- Timeline centre rises to 122k.
- Café is up to 62k.
- The worst view per venue rises about 1% (Timeline, 122k against the old 121k) and about 15% (Café, 62k against
  54k); Studio's worst view falls.
- Cell size and LOD thresholds are per-venue knobs in `VenueCatalog.cs`. Smaller cells trade draws back for
  triangles if the Seeker shows vertex cost.

**Verified in batchmode:**
- Spots: all fe.2 spots reachable — Timeline 15, Café 17, Studio 34. Café terminal and table seats are reached via
  their approach points, as in the shipped NavMesh.
- Parity against the source (`VenueRepack.CheckParity`, receipts `*-parity.json`), all equal:
  - live surfaces (Timeline: `timeline:timeline_screen`)
  - anchor poses and approaches
  - `CafeSeatMotionProfile` JSON
  - all colliders (30 / 60 / 48)
  - camera settings
  - NavMesh data
- No lights are left in the prefabs.

**Not changed:**
- No CT-now set dressing was added to these three tuned layouts; their existing parody signage is kept.
- The old source prefabs under `Art/Generated/Venues` and `Art/Candidates/PlayableVenues20261007` are no longer
  registered. They must stay, because they are the repack input.
- I re-ran no other venue.

**Evidence** (outside the repo):
- `ct-life-evidence/venues/<id>.png` (one preview each)
- `receipts/<id>-{before,build,verify,parity}.json`
- logs `20`–`39`

Commands:
- `…Unity -batchmode -quit -projectPath client -executeMethod CTLife.EditorTools.VenueBuild.VenueBuildPipeline.All -ctVenues timeline_square,terminal_cafe,spaces_studio`
  (needs graphics), then `VenueRepack.Register` and `VenueRepack.CheckParity` with the same `-ctVenues`.

# Venue builder lane — progress log

Branch `feat/venues` (worktree `/Users/mac/Documents/codes/projects/ct-life-venues`, from `dev` 7fb99fb).
Owner: venue-builder agent. Started 2026-10-07, updated 2026-10-08.

## Status

All 18 remaining public venues and the 4 remaining home types are built as playable dioramas by a scripted,
reproducible pipeline, baked (GPU lightmapper), and verified in batchmode. Every contract spot is on the NavMesh and
reachable from the entrance. The integration lead still has to register them (see "Integration").

[Unverified] Nothing here has run on the Seeker yet. The draw-call and triangle figures are editor estimates
(see "Budgets"). Device profiling with avatars and the 50-player stress test remains the acceptance gate.

## What was built

| Venue (map name) | id / spot set | District | CT identity (set dressing) | Live hooks |
| --- | --- | --- | --- | --- |
| Perp Pit | `perp_pit` | trenches | Perp-DEX trading floor. 8 terminals face a leverage board and a 24h-casualties board. Liquidation witness circle, CROSS/ISOLATED margin lounges, lever-o-meter, funding poster, ticker ("perps on BTC · OIL · GOLD · US10Y (lol)"). | LeverageBoard, LiquidationBoard |
| Rekt Clinic | `rekt_clinic` | trenches | 6 recovery beds with vitals monitors and curtains, triage desk, WAGMI/NGMI eye test, patient chart ("prescription: touch grass"), waiting room, memorial wall, tombstone garden (RIP 100x, DEV SOLD…). | — |
| Launchpad Tower | `launchpad_tower` | trenches | Pulse-ish launch board (FRESH MINTS / FINAL LAP / GRADUATED), next-launch clock, launch desk with a big red button inside the NPC swarm ring, bonding-curve floor line, rocket, creator-fee jar, stream booth, skyline windows. | LaunchBoard, NextLaunchClock |
| Prediction Pit | `prediction_pit` | trenches | ODDSBOARD (market / % chance / YES ¢ / NO ¢ / $ vol), 6 booths, YES/NO floor halves, category banner (politics · AI · celebs · macro…), the Oracle, "game Cash only, never withdrawable" rules. | OddsBoard |
| Ratio Arena | `ratio_arena` | timeline | Flush ring: red corner (the OP) vs blue corner (the reply guy), ratio meter, bleacher benches with a static crowd, "L + RATIO" and community-note banners, spotlights. | RatioMeter |
| Compliance Jail | `ct_jail` | timeline | 4 barred cells labelled by offence (unregistered offering, wash trading, rug pull, forgot to KYC) with bunks, "Know Your Degen" KYC desk, mugshot height wall, guard post. Bail is in game Cash. | — |
| Discord Heights | `discord_heights` | timeline | Channel doors (#general #alpha-maybe #gm #wen-token #support-ticket #memes), #announcements board, verify-to-enter kiosk, mod desk with ban hammer, "admins never DM first", hiring-bait drainer poster. | Announcements |
| Conference Center | `conference_hall` | builder_hills | COIN2050 ("feels like a board meeting"): main-stage LED wall and podium, 12 seats, expo booths (Startup Village, IV drip bar), lanyard registration, 2-minute pitch timer, "side events down 40%", After2050 rooftop sign. | MainStage, PitchTimer |
| Hackathon House | `hackathon_house` | builder_hills | Builder Station: 16 m table with 8 desks, submissions countdown ("deadline extended (again)"), whiteboard plan, couch with beanbags and pizza, bunk and sleeping bags, energy-drink fridge, demo podium. | SubmissionCountdown |
| Audit Row | `audit_row` | builder_hills | 4 audit booths behind glass, EXPLOIT ALARM board and red beacon, REKT-O-BOARD, bug-bounty corkboard, "funds are probably fine", AUDITED (by a guy) / UNAUDITED stamps, Hermit Crew wanted poster, giant magnifier. | ExploitAlarm, ExploitBeacon (tint) |
| VC Row | `vc_row` | builder_hills | Glass pitch room (6 seats) with a pitch-deck screen, WE ARE SO EARLY neon, term sheet, thesis "tokenize everything · the suits are here", portfolio joke, partner desk, library lounge, gold "cabal chat" phone. | PitchDeck |
| ORE Refinery | `ore_refinery` | airdrop_farm | Giant 5×5 ORE board, round timer and motherlode panels, furnace bay, conveyors of glowing ore, 4 stake/unstake/claim/compound terminals, miner post with hard hats and badge tiers, "mining happens on ore.com, not in this app". | OreBoard5x5, OreRoundTimer, OreMotherlode, FurnaceGlow (tint) |
| Points Fields | `airdrop_fields` | airdrop_farm | Open-air points farm: beds of glowing "points sprouts", SEASON ∞ leaderboard billboard, airdrop crates (one still parachuting in), Sybil scarecrow, season mile posts, claim-window-closed sign, barn on the horizon. | PointsLeaderboard |
| Sybil Barn | `sybil_barn` | airdrop_farm | Three clone pens of identical wallet farmers (WALLETS 0001–∞), phone-farm rack, wallets_final_v7.csv desk, WANTED: SYBIL SQUAD, hay-bale seats, lanterns. | — |
| Checker Kiosk | `checker_kiosk` (new set) | airdrop_farm | Eligibility plaza: 3 checker kiosks under a canopy, ELIGIBILITY board, queue stanchions ("est. wait: forever"), NOT ELIGIBLE (lol) board, tier poster, cope bench, confetti. | EligibilityBoard |
| Cabal Lounge | `cabal_lounge` | dubai_marina | Velvet booths under CABAL CHAT ("7 members · 0 leaks"), gold neon, guest list (dress code: matching PFPs), glowing bottle bar with an insider menu, "100% NOT BUNDLED" neon. | CabalChat |
| Yacht Club | `yacht_club` (new set) | dubai_marina | Teak dock with 6 loungers and a 4-stool bar, moored yacht NUMBER GO UP with gangway, EXIT LIQUIDITY in the marina, departures board, palms, parasols, orange supercar, tower canyon. | MarinaDepartures |
| Penthouse Row | `penthouse_row` (new set) | dubai_marina | Rooftop with three penthouse fronts (PH-1..3, rent $1,500/week game Cash), concierge, infinity pool with 4 loungers, fire-bowl sofa lounge, glass balustrade, dusk skyline. | — |
| Hacker House (home) | `home` / `hacker_house` | — | Bunk bed, triple-monitor desk plus a second workstation, server rack, SHIP IT whiteboard, GM/WAGMI neon, pizza, beanbag, sleeping bag. | — |
| Studio (home) | `home` / `studio` | — | Single bed, desk, small sofa with chart TV, kitchenette, daylight windows. | — |
| Penthouse (home) | `home` / `penthouse` | — | King bed, L-couch, chandelier, bar cart, night-skyline windows, "I bought the top" frame. | — |
| Marina Villa (home) | `home` / `marina_villa` | — | King bed, L-couch, fireplace, open glass back onto pool terrace, palms, yacht and marina towers. | — |

Map-name to id mapping: Conference Center → `conference_hall`, Compliance Jail → `ct_jail` and Points Fields →
`airdrop_fields` are existing spot-set ids. No spot set existed for Checker Kiosk, Yacht Club or Penthouse Row, so new
ids were appended. [Inference] The map names came from the coordinator's list. The spec's map is embedded content,
not text, so I could not verify these names against it. I did not repurpose the district ids `dubai_marina`,
`airdrop_farm` or `builder_hills`. The 2026-10-06 coverage audit warns against silently renaming district ids into
venue ids.

## Pipeline (reproducible)

Code: `client/Assets/Scripts/Editor/VenueBuild/` (all new files, `CTLife.Editor` assembly). Assets:
`client/Assets/World/Venues/<id>/` plus `_Shared/` (palette texture and shared pre-bake materials).

- `VenueCatalog.cs` lists the 22 venues, with camera framing, lighting look and identity for each.
- The per-district layouts are `Venues_Trenches.cs`, `Venues_Timeline.cs`, `Venues_BuilderHills.cs`,
  `Venues_AirdropFarm.cs`, `Venues_Marina.cs` and `Venues_Homes.cs`.
- `VenueKit.cs` (the builder context), `VenueParts.cs` and `VenueProps.cs` provide the shared building blocks:
  - The diorama shell has a low front wall on the camera side, with the doorway on the exit spot.
  - Manifest models are merged per material into one LOD0/LOD1 `LODGroup` per 10 m cell.
  - Procedural trim is drawn from one 256² swatch atlas (one lit draw and one unlit "glow" draw for the whole venue).
  - Signage comes from one atlas per venue.
  - Live hooks are separate `Hook_*` renderers.
  - NavMesh is built from the walkable rectangles plus not-walkable modifier boxes for solid props. Seats are never
    covered.
  - There are ground and wall colliders, and tap colliders parented to claimable `SpotAnchor`s.
  - Optional `approach` points sit beside furniture.
- `VenueBake.cs` is a mobile bake adapted from `VenueLightingBuilder`:
  - ProgressiveGPU at 8 texels/m, 1024 maps, no reflection probe.
  - `Backdrop` geometry (far ground, water, skyline) stays Simple Lit, so it does not consume lightmap texels.
  - Baked-only lights are removed from the prefab.
  - The bake scene, LightingData and unreferenced pre-bake meshes are deleted.
- `VenueVerify.cs` runs in a clean scene:
  - Checks that lightmaps bind.
  - Re-validates every anchor, and that each spot is on the NavMesh and reachable from the entrance.
  - Estimates gameplay-camera draws and triangles at three positions (entrance, centre, far spot), using the
    venue's own framing, landscape 2670×1200 and the Android quality level's LOD bias (0.7).
  - Captures a 960×540 overview PNG plus an entrance gameplay frame.
- `VenueIntegration.cs` holds the integration helpers. None of them ran on this branch.
- `tools/venues/make_venue_signs.py` writes the original parody signage atlases. They use Lilita One and Manrope
  (OFL, already bundled with the client) and need Pillow (`art/lookdev/.venv`).
- `tools/venues/propose_spots.py` makes the strictly additive spot proposal. It has a guard that existing ids,
  positions and walkable rectangles are unchanged.

Commands (worktree root; Unity 6000.3.25f1):
```sh
art/lookdev/.venv/bin/python tools/venues/make_venue_signs.py        # signage atlases (only when sign text changes)
python3 tools/venues/propose_spots.py                                # additive spot proposal (idempotent)
U=/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity
# Build + GPU bake + verify. Graphics required, so NO -nographics. Optional -ctVenues perp_pit,ore_refinery
$U -batchmode -quit -projectPath "$PWD/client" -executeMethod CTLife.EditorTools.VenueBuild.VenueBuildPipeline.All -logFile /tmp/venues.log
# Build only (works with -nographics):  ...VenueBuildPipeline.Build     Verify only: ...VenueVerify.Verify
```
Receipts and previews go to `/Users/mac/Documents/codes/projects/ct-life-evidence/venues/`, outside the repo:
`<id>.png`, `captures/<id>-entrance.png`, `receipts/<id>-{build,verify}.json` and `logs/`.

Deviation from the brief: the coordinator's template passes `-nographics`. The lightmap bake (ProgressiveGPU, the
only lightmapper Unity 6.3 supports on Apple silicon, per the existing `VenueLightingBuilder` guard) and the render
captures need a graphics device. Build/Bake/Verify therefore ran in batchmode **without** `-nographics`, always
against the worktree project only.

## Budgets (editor estimates, 2026-10-08 final run)

The figures below come from `VenueVerify`, three gameplay-camera positions per venue (entrance, centre, far spot).
Draws count one per visible renderer submesh. Venue prefabs are instantiated at runtime, so no static batching
applies. The cell LOD is selected at the Android LOD bias of 0.7. "Whole" is every LOD0 renderer regardless of the
camera.

Exclusions: the figures do not count avatars, NPCs, UI or the realtime sun's per-pixel cost. They are not a GPU
profile: `UnityStats` returns 0 for manual `SubmitRenderRequest` renders, so the estimate is computed instead.
The spec budget is under 150 draws and 100–150k visible triangles per frame on the Seeker.

| id | spots / reachable | max nav offset m | gameplay draws (max) | gameplay tris (max) | whole draws / tris | lightmaps | sourced instances | live hooks |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `perp_pit` | 13 / 13 | 0.67 | 19 | 26,285 | 19 / 26,787 | 2 (512k texels) | 292 | 2 |
| `rekt_clinic` | 11 / 11 | 0.67 | 15 | 17,952 | 19 / 20,344 | 1 (256k texels) | 171 | 0 |
| `launchpad_tower` | 15 / 15 | 0.67 | 17 | 17,984 | 17 / 19,987 | 2 (512k texels) | 194 | 2 |
| `prediction_pit` | 10 / 10 | 0.67 | 11 | 16,958 | 12 / 17,576 | 2 (512k texels) | 193 | 1 |
| `ratio_arena` | 17 / 17 | 0.67 | 6 | 4,700 | 8 / 5,320 | 2 (512k texels) | 179 | 1 |
| `ct_jail` | 13 / 13 | 0.67 | 14 | 10,050 | 14 / 11,098 | 2 (512k texels) | 184 | 0 |
| `discord_heights` | 11 / 11 | 0.67 | 12 | 8,644 | 13 / 9,400 | 2 (512k texels) | 186 | 1 |
| `conference_hall` | 22 / 22 | 0.67 | 13 | 9,016 | 14 / 9,859 | 2 (512k texels) | 190 | 2 |
| `hackathon_house` | 17 / 17 | 0.67 | 14 | 29,368 | 14 / 29,574 | 2 (320k texels) | 203 | 1 |
| `audit_row` | 11 / 11 | 0.67 | 13 | 16,396 | 15 / 17,855 | 2 (512k texels) | 191 | 2 |
| `vc_row` | 14 / 14 | 0.67 | 12 | 22,222 | 12 / 24,752 | 2 (512k texels) | 188 | 1 |
| `ore_refinery` | 10 / 10 | 0.67 | 25 | 16,336 | 27 / 17,328 | 2 (512k texels) | 299 | 4 |
| `airdrop_fields` | 9 / 9 | 0.67 | 27 | 8,352 | 34 / 10,698 | 2 (128k texels) | 33 | 1 |
| `sybil_barn` | 9 / 9 | 0.67 | 7 | 9,284 | 7 / 10,604 | 2 (512k texels) | 172 | 0 |
| `checker_kiosk` | 9 / 9 | 0.67 | 24 | 9,181 | 27 / 10,140 | 1 (256k texels) | 134 | 1 |
| `cabal_lounge` | 13 / 13 | 0.67 | 11 | 10,646 | 12 / 11,507 | 2 (320k texels) | 186 | 1 |
| `yacht_club` | 15 / 15 | 0.67 | 25 | 16,968 | 39 / 37,912 | 1 (256k texels) | 234 | 1 |
| `penthouse_row` | 13 / 13 | 0.67 | 18 | 13,804 | 32 / 32,258 | 1 (256k texels) | 170 | 0 |
| `home_hacker_house` | 6 / 6 | 0.67 | 11 | 7,098 | 11 / 8,888 | 2 (128k texels) | 66 | 0 |
| `home_studio` | 6 / 6 | 0.67 | 8 | 7,400 | 8 / 7,606 | 2 (128k texels) | 69 | 0 |
| `home_penthouse` | 6 / 6 | 0.67 | 7 | 9,661 | 7 / 9,867 | 3 (144k texels) | 64 | 0 |
| `home_marina_villa` | 6 / 6 | 0.67 | 9 | 13,039 | 14 / 21,061 | 3 (144k texels) | 72 | 0 |

How to read the table:
- **Nav offset.** The only offset above 0 is the `exit` spot. Every venue puts it on the walkable boundary (as in
  rc1), and NavMesh erosion moves it 0.5–0.67 m inside. That matches the shipped venues.
- **Lightmaps.** These are 1024² or smaller, EXR source; they compress on import.
- **Signage.** Each venue has one 2048-wide atlas, with heights from 640 to 2048 rounded to a multiple of 128, and
  ASTC 6×6 on Android.
- **Repository size.** `client/Assets/World` is about 137 MB in total. Mesh `.asset` YAML (force text) and
  lightmap EXRs are the bulk. The largest single file is about 2.8 MB.

## Live hooks (for integration code)

Every hook is a `Hook_*` GameObject under `Hooks/` in the prefab, with its own renderer and an unlit material. The
boards ship an idle/standby state only ("—" placeholders). They never show invented prices, PnL, odds or counts.

| Hook | What the integration does |
| --- | --- |
| `Hook_LeverageBoard`, `Hook_LiquidationBoard` (perp_pit) | Swap `_BaseMap` for a RenderTexture or UI Toolkit panel texture. The material uses `_BaseMap_ST` to select the atlas region; reset scale to (1,1) and offset to (0,0) when swapping. |
| `Hook_LaunchBoard`, `Hook_NextLaunchClock` (launchpad_tower) | Same, fed by the fictional coin launches (about every 5 min). |
| `Hook_OddsBoard` (prediction_pit) | Same, fed by prediction markets. |
| `Hook_RatioMeter` (ratio_arena) | Same. |
| `Hook_Announcements` (discord_heights) | Same. |
| `Hook_MainStage`, `Hook_PitchTimer` (conference_hall) | Same. |
| `Hook_SubmissionCountdown` (hackathon_house) | Same. |
| `Hook_ExploitAlarm` (audit_row) | Same. |
| `Hook_PitchDeck` (vc_row) | Same. |
| `Hook_PointsLeaderboard` (airdrop_fields) | Same. |
| `Hook_EligibilityBoard` (checker_kiosk) | Same. |
| `Hook_CabalChat` (cabal_lounge) | Same. |
| `Hook_MarinaDepartures` (yacht_club) | Same. |
| `Hook_OreRoundTimer`, `Hook_OreMotherlode` (ore_refinery) | Same, from the cached ORE round and treasury. |
| `Hook_OreBoard5x5` (ore_refinery) | Write a 5×5 `Texture2D` (point filter, one pixel per square, row 0 = bottom) to `_BaseMap`, for example from `readOreBoard().squares`. |
| `Hook_FurnaceGlow` (ore_refinery), `Hook_ExploitBeacon` (audit_row) | Modulate `_BaseColor`. Furnace heat = city-wide staked ORE (spec); the beacon pulses on an exploit alert. |

## Integration (for the lead; not done on this branch)

The `VenueContentRegistry` asset and its runtime catalog (`VenueContent*`) are mid-refactor in the main checkout and
untracked there. I therefore did not touch the registry on this branch.

1. **Register the venues.** After merging, run `CT Life → Venues → Integration → Register built venues in content
   registry` (`VenueIntegration.RegisterBuiltVenues`). It adds the 18 venue prefabs (by `venueId`) and the 4 home
   prefabs (by `homeId`) to `Assets/Venues/VenueContentRegistry.asset`, falling back to the Resources path. It then
   calls `VenueContentCatalog.Sync` if that exists. Prefab paths and GUIDs are in `docs/progress/venues-manifest.json`.
2. **Enable the homes.** `VenueContentRegistry.FindHome` and `VenueContent.HasPlayableHome` currently only allow
   `parents_basement`. The gate must be widened to `hacker_house`, `studio`, `penthouse` and `marina_villa`. Their
   prefabs use `venueId = "home"` and the shared `home` spot set, as the loader expects.
3. **Get backend rooms for the new venues.** Backend `venues.json` (BE-owned) has rooms for only 10 of these 18:
   - perp_pit, rekt_clinic, ore_refinery, launchpad_tower, prediction_pit, ratio_arena, audit_row, discord_heights,
     vc_row and cabal_lounge.
   - Still missing: `conference_hall`, `ct_jail`, `hackathon_house`, `airdrop_fields`, `sybil_barn`,
     `checker_kiosk`, `yacht_club` and `penthouse_row`. They need backend venue entries (and a `districtId`) before
     they can be joined or listed on the map.
   - `FallbackDistricts` in `Registries.cs` has no entries for checker_kiosk (airdrop_farm), yacht_club or
     penthouse_row (dubai_marina).
   - Request for devIykee: add these rooms and adopt spot proposal `1.0.0-rc1+fe.2`.
4. **Sync the spots.** `docs/fe-contract-proposals/ids/spots.json` is now `fe.2`, with 103 appended spots and 3 new
   sets, all additive. The client snapshot `Resources/Contract/config_snapshot.json` is still `fe.1`. Scene anchors
   already carry the new spots: `VenueSceneRoot.TryGetSpot` prefers anchors. Server-side `sit` on new seats needs the
   backend to adopt the ids.
5. **Optional:** `Mark venue prefabs addressable` creates an Addressables group "Venues" with addresses
   `venue/<id>`, for Phase 3+ streaming.
6. **Don't merge editor side effects.** Opening this worktree made Unity re-serialise unrelated materials under
   `Art/Candidates` and `Art/characters`. Those changes are not committed and should not be merged.

## CT-now dossier proposals (docs/research/ct-now-2026-10.md, origin/dev 14da875)

Built into the existing venues:
- Perp Pit: BTC / OIL / GOLD / US10Y (joke) ticker.
- Prediction Pit: politics, AI, celebs and macro categories.
- Launchpad Tower: stream booth, Holder-Rewards fountain and creator-fee jar.
- Cabal Lounge: matching-PFP dress code.
- Discord Heights: hiring-bait drainer poster and "admins never DM first". Signage uses the parody name
  "Discordia Heights" and a non-brand purple.
- Conference Center:
  - COIN2050 skin with "feels like a board meeting" and "side events down 40%".
  - Booth mascot photo op, lanyard registration, After2050 rooftop sign, "next stop: Checkpoint London".
- Hackathon House: a deadline that keeps getting extended.
- Audit Row: The Hermit Crew wanted poster.
- Yacht Club: crypto gov-fee kiosk (the "Dubai Marina" add).
- ORE Refinery: 5×5 board (as specced).
- Parody naming pass: "Builder Station" became "Buildooor Station", "Startup Village" became "Startup Hamlet" and
  "Mindshare Arena" became "Yapper Arena". ORE, Solana, Seeker and SKR stay real; signage only names ORE (and
  ore.com in the "mining is not in this app" notice).

Deferred:
- **New venues** Rooftop Bar (After2050), Bank Tower and the NG P2P Market. They are not in the 21-venue list, have
  no spot sets and no backend rooms. The pipeline can add each one in about an hour once the user accepts them and
  the ids are agreed.
- **Skins and runtime systems:** the Checkpoint London skin of the Conference Center, the Nomad Beach skin, Odds
  Crier and ORE Split/Solo NPC talk, and macro/hack events.
- **Not this lane:** Timeline Square, Terminal Café, Spaces Studio and Phone Shop adds belong to existing or other
  venues.

## Research sources (fetched 2026-10-08 by this lane; dates as shown on the pages)

The web research ran in a sub-agent using WebFetch only; no browser tools. Its WebSearch quota was exhausted.
- Axiom Pulse docs (date not shown): https://docs.axiom.trade/axiom/finding-tokens/pulse.md
  - Supports: New Pairs / Final Stretch / Migrated columns, token-card fields, quick buy.
  - Used for: PULSE-ISH with FRESH MINTS / FINAL LAP / GRADUATED.
- GMGN icon definitions (images dated Jul–Aug 2024): https://docs.gmgn.ai/index/featured-icon-definition.md
  - Supports: sniper, bundle and dev labels.
- Pump.fun on Wikipedia (edited 2026-10-06): https://en.wikipedia.org/wiki/Pump.fun
  - Supports: bonding-curve graduation and stream launches.
  - Used for: bonding-curve floor line and stream booth.
- Hyperliquid docs on liquidations, margining and funding (date not shown):
  - https://hyperliquid.gitbook.io/hyperliquid-docs/trading/liquidations
  - https://hyperliquid.gitbook.io/hyperliquid-docs/trading/margining
  - https://hyperliquid.gitbook.io/hyperliquid-docs/trading/funding
  - Supports: 1x to 40x leverage, cross/isolated margin, hourly funding, liquidation price from mark price.
  - Used for: lever-o-meter, CROSS / ISOLATED lounges, "funding / hr", "mark price, not last price".
- Coinglass liquidation data and heatmap (date not shown): https://www.coinglass.com/LiquidationData and
  https://www.coinglass.com/pro/futures/LiquidationHeatMap
  - Supports: 1H/4H/12H/24H tabs and the "biggest single liquidation" line.
  - Used for: the 24H CASUALTIES board and the LIQ HEATMAP poster (labelled "decor, not data").
- Polymarket (fetched Oct 2026; docs date not shown): https://polymarket.com/ and
  https://docs.polymarket.com/polymarket-101
  - Supports: Trending/Breaking/New tabs, YES/NO priced in cents, % chance, volume.
  - Used for: the ODDSBOARD.
- Kalshi docs (date not shown): https://docs.kalshi.com/getting_started/terms
- Colosseum Builder Station (2026-09-17): https://blog.colosseum.com/builder-station-sbpfv3-solana-resilience/
  - Supports: desks, monitors, snacks, 24/7 access.
- Colosseum demo day (2026-09-24): https://blog.colosseum.com/cohort-v-demo-day-solana-microscope-alpenglow-migration/
  - Supports: 2-minute pitches.
- ETHGlobal event pages (2025): https://ethglobal.com/events/newyork2025
- Hackathon on Wikipedia (edited 2026-09-25): https://en.wikipedia.org/wiki/Hackathon
  - Supports: pizza, energy drinks, sleeping bags.
- TOKEN2049 Singapore and Dubai pages (2026/2027 editions): https://token2049.com/singapore/ and
  https://token2049.com/dubai/
  - Supports: startup zone, rooftop afterparty, IV drips and cold plunge.
  - Used in parody form.
- Solana Breakpoint guide (listed 2026-09-18): https://solana.com/news/breakpoint-guide-part1
- Dubai Marina on Wikipedia (edited 2026-09-20): https://en.wikipedia.org/wiki/Dubai_Marina
  - Supports: tower canyon and yacht club.
- Axiom points docs: https://docs.axiom.trade/getting-started/point-system.md
- Kaito: https://www.kaito.ai/
  - Supports: leaderboards and mindshare.
- Airdrop on Wikipedia (edited 2026-09-23): https://en.wikipedia.org/wiki/Airdrop_(cryptocurrency)
- Rekt leaderboard: https://rekt.news/leaderboard/
  - Supports: ranked losses and audit tags.
  - Used for: the REKT-O-BOARD.
- Immunefi bug bounties ("last updated Oct 7th 2026"): https://immunefi.com/bug-bounty/
  - Supports: max bounty, PoC required, safe harbor, KYC.
- Collab.Land docs: https://docs.collab.land/help-docs/intro
  - Supports: the verify-to-enter flow.
- a16z State of Crypto 2025 (2025-10-22): https://a16zcrypto.com/posts/article/state-of-crypto-report-2025/
- Know your customer on Wikipedia: https://en.wikipedia.org/wiki/Know_your_customer
  - Supports: KYC fields.
  - Used for: "Know Your Degen".
- In-repo sources: `docs/research/ct-x-observations-2026-10-04.md` and the CT-now dossier
  `docs/research/ct-now-2026-10.md` (+ `.json`) on origin/dev 14da875.

[Unverified] I could not find a primary source for the following, so they are parody conventions only:
- PnL share-card styling
- 50x leverage
- "You are not eligible" wording
- channel names such as #alpha
- ON AIR or ring-light studio specifics

## Limitations and next steps

- [Unverified] **No device run.** Nothing here has been checked for Seeker fps, thermal behaviour or memory, and
  the 50-player + 12-NPC stress test has not run. The budgets above are editor estimates only.
- **Seat contact is functional, not polished.** Seats use furniture placed on the anchor's facing, with tap
  colliders and approach points. Sitting contact with real rigs (chair heights, bed edges) has not been reviewed.
  Some contract facings come from the generic rc1 sets (for example, all home spots face +z).
- **Live boards need feeds.** Hooks show idle art until integration code drives them, and no feature binding is
  implemented here.
- **Some venues are backend-gated.** Venues without backend rooms load art but cannot be joined (see "Integration"
  step 3).

## Commits (branch feat/venues, not pushed)

- `ea26ae0` feat(venues): scripted venue diorama builder, GPU bake and batchmode verify
- `f454984` docs(venues): additive venue spot proposals and signage provenance
- `97435a4` feat(venues): 18 venue dioramas and 4 home types, baked and verified
- docs(venues): venue builder progress log (this file) — tip of feat/venues
