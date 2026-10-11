READY TO COMMIT: client/Assets/Scripts/Editor/VenueBuild/VenueRepack.cs client/Assets/Scripts/Editor/VenueBuild/VenueRepack.cs.meta client/Assets/Scripts/Editor/VenueBuild/VenueBake.cs client/Assets/Scripts/Editor/VenueBuild/VenueBuildPipeline.cs client/Assets/Scripts/Editor/VenueBuild/VenueCatalog.cs client/Assets/Scripts/Editor/VenueBuild/VenueMeshAcc.cs client/Assets/Scripts/Editor/VenueBuild/VenueSpec.cs client/Assets/Scripts/Editor/VenueBuild/VenueVerify.cs client/Assets/World/Venues/timeline_square client/Assets/World/Venues/timeline_square.meta client/Assets/World/Venues/terminal_cafe client/Assets/World/Venues/terminal_cafe.meta client/Assets/World/Venues/spaces_studio client/Assets/World/Venues/spaces_studio.meta client/Assets/Venues/VenueContentRegistry.asset client/Assets/Resources/World/VenueContent/venue_timeline_square.asset client/Assets/Resources/World/VenueContent/venue_terminal_cafe.asset client/Assets/Resources/World/VenueContent/venue_spaces_studio.asset docs/progress/venues.md — perf(venues): repack Timeline Square, Terminal Café and Spaces Studio through the venue pipeline (181/194/116 → 28/25/17 draws)

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
