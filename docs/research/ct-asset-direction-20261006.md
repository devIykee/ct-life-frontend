# CT Life asset direction — 6 October 2026

[Inference] This report combines directly inspected source metadata and images with explicitly identified art-direction judgments. It is not user approval or proof of native/device quality.

## The immediate problem

The supplied M4 [native city screenshot](../progress/screens/quality-review-20261006m4/quality-review-20261006m4/city.png) shows the player entering a large, mostly empty paved area. Shopfronts, meaningful social activity and most city context are outside that frame. The [existing Blender lookdev](../../art/lookdev/renders/timeline_square_hero_raw.png) already has a denser city, large Timeline event screen and overlapping activity clusters; it is not the scene shown on the phone.

[Inference] The highest-value change is authored street composition around where players actually arrive, paired with a closer camera and reduced HUD obstruction. More downloaded models alone will not create that composition. A screenshot with a beautiful city somewhere beyond the camera is still a failed arrival experience.

## Six supplied Sketchfab models — checked live

The public model pages returned HTTP 403 through the web tool. Sketchfab's official public model API returned the records below on 6 October 2026. Frozen JSON and separately labelled visual-reference thumbnails are in [source-evidence](../../art/direction-20261006/source-evidence/). No model files were downloaded or extracted from viewers.

| User reference | Live official metadata | Decision for this production candidate |
| --- | --- | --- |
| [358 Broadway block](https://sketchfab.com/3d-models/manhattan-modular-city-block-358-broadway-st-71f1ca2b46a84c0d907b71f78c34b790) — 99.Miles | `isDownloadable=false`; Standard licence. Description explicitly identifies an image preview for a paid asset and links to Fab. The displayed API 100 faces/72 vertices describe the preview, not the building pack. [API](https://api.sketchfab.com/v3/models/71f1ca2b46a84c0d907b71f78c34b790) | Useful architecture/density reference. Not a verified free game-model source. |
| [Razor Crest](https://sketchfab.com/3d-models/the-mandalorian-razor-crest-full-interior-ee72226c953c414ca0b7ab5780fe400a) — Binkley-Spacetrucker | Downloadable; CC BY 4.0; API face count 333,245; 160,925 vertices. [API](https://api.sketchfab.com/v3/models/ee72226c953c414ca0b7ab5780fe400a) | Not selected: recognizable Mandalorian/Star Wars design and a face count already above the whole visible-scene target. [Unverified] The uploader's authority to grant franchise rights has not been established. |
| [Drone V2 Red Manga](https://sketchfab.com/3d-models/drone-v2-red-manga-fe11a0f49faf4ff182d838df53328d9f) — iggy-design | Not downloadable; Standard; 9,576 faces; 5,573 vertices. [API](https://api.sketchfab.com/v3/models/fe11a0f49faf4ff182d838df53328d9f) | Visually interesting mechanical silhouette for future sniper-bot research; not a verified free source. |
| [Printable wings](https://sketchfab.com/3d-models/wings-3d-printable-28927be01af9401bbabc6344b0f98b34) — Rumpelstiltskin | Not downloadable; Standard; 1,531,428 faces; 765,704 vertices; described as high-poly watertight print geometry. [API](https://api.sketchfab.com/v3/models/28927be01af9401bbabc6344b0f98b34) | Reference only. A wearable would require a distinct mobile mesh, skinning and material pipeline. |
| [Manhattan Bridge](https://sketchfab.com/3d-models/manhattan-bridge-brooklyn-new-york-ny-usa-70106cd239f64a31aaf1c62d41b2d373) — Brian Trepanier | Downloadable; CC BY 4.0; 1,404,978 faces; 899,348 vertices. [API](https://api.sketchfab.com/v3/models/70106cd239f64a31aaf1c62d41b2d373) | [Inference] The inspected preview has scanned city textures and irregular surrounding geometry, unsuitable for dropping directly into the stylized city. Potential distant-landmark reference, not today's runtime asset. |
| [Man in coat](https://sketchfab.com/3d-models/man-in-coat-character-human-riged-model-758a855697be47a1be0d707623e3907e) — Kasit Studio | Not downloadable; Standard; 19,372 faces; 10,095 vertices; description says humanoid rig, 4K PBR maps, Tommy Shelby inspiration. [API](https://api.sketchfab.com/v3/models/758a855697be47a1be0d707623e3907e) | A useful adult-proportion/wardrobe reference, not a verified free source or tested Unity rig. |

CC BY requires credit, the licence link and change disclosure; its deed also warns that it may not supply every permission needed. CC0 does not waive other parties' trademark/publicity/privacy rights. Those are source-licence limits, not a claim that any particular use has been legally adjudicated. [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), [CC0](https://creativecommons.org/publicdomain/zero/1.0/).

## Cohesive sources already available

The environment manifest currently has **265 entries**. These are local manifest entries, not a claim of 265 distinct production-approved game objects. The inspected kit sheet shows complete low-rise shop buildings, skyline towers, streets and nature; the weakness at entry is their arrangement and visibility.

| Role | Primary-source licence check | Use |
| --- | --- | --- |
| Walkable, adult-scale street frontage | [Quaternius Ultimate Buildings](https://quaternius.com/packs/ultimatetexturedbuildings.html), 76 models, CC0, modular buildings and palette atlases | Reuse existing 2,426-triangle one-story shop and existing signage. Two near-entry addresses supply visible street edges. |
| Foliage framing | [Quaternius Ultimate Stylized Nature](https://quaternius.com/packs/ultimatestylizednature.html), 63 models, CC0 | Move existing blossom gardens into the arrival frame; preserve foreground/middle/background spacing. |
| Café social pockets | [KayKit Restaurant Bits](https://kaylousberg.itch.io/restaurant-bits), free tier CC0, 140+ assets, single 1024² gradient atlas | Reuse imported tables/chairs, sourced Kenney laptop/coffee details and matching material families. |
| Mid-distance architectural upgrade | [Quaternius Downtown City MegaKit](https://quaternius.com/packs/downtowncitymegakit.html), CC0 Standard subset; 315 pack models advertised, seven example buildings | Revisit the existing source modular pieces for future blocks. Do not accept the previous blanket "too heavy" conclusion as an aesthetic veto: inspect a selected facade's real geometry/material cost. Paid Source shader/project features are not part of the free Standard entitlement. |
| Distant skyline | [Kenney City Kit Commercial](https://kenney.nl/assets/city-kit-commercial), 50 files, CC0, version 2.1 | Retain simple glass towers as skyline silhouette; do not treat them as hero storefront detail. |

No paid download, account, new dependency or unverified Sketchfab extraction is required for this candidate. Existing per-asset licences remain in `art/LICENSES.md`; new reference downloads are recorded only in the isolated study's source evidence and are not shipped art.

## Implemented scene study

Source: [CityDirectionCandidate.cs](../../client/Assets/Scripts/Editor/CityDirectionCandidate.cs). This is an editor builder, not an already imported or published venue.

- Copies the canonical Timeline into `Assets/Art/Candidates/CityDirection20261006/TimelineStreet.prefab`.
- Adds two sourced storefront shells near the entrance, four compact café/market seating pockets, a rear event screen and a continuous central promenade.
- Moves existing blossom clusters, lamps and display furniture to support near/mid/far composition.
- Preserves every existing spot position and camera setting, then builds and checks complete navigation paths from the entrance to every positioned spot.
- Separates stage from lighting bake. Every renderer material is copied into the candidate folder before edits or baking. The venue registry and backend contract remain unchanged.
- Records placement sources and manifest triangle estimates in the native stage receipt; these estimates are not GPU measurements.

[Inference] The resulting composition should let the arrival view explain where people meet, where the street continues and why this is CT. This remains a hypothesis until the paired native render is inspected. Static chairs are dressing, not a completed sit interaction, and static screen artwork is not a live market integration.

## The next authored venue

**Recommendation: Terminal Café next**, with the existing `art/lookdev/terminal_cafe.blend` as composition reference and the processed models as runtime inputs. The [existing render](../../art/lookdev/terminal-cafe.png) has a clear social/market identity: warm wood floor, teal window wall, red brick display wall, neon café sign, eight terminals and coffee bar. It is not a native playable venue.

The current FE config snapshot already positions eight terminals at x = −7.5, −2.5, 2.5, 7.5 and z = 3, 7; four seats at x = −6, −2, 2, 6, z = −4; entrance at (0,−8); and a 24×18 m walkable rectangle. The Blender lookdev shell is 16×12 m. **Do not import it whole and silently rescale/reposition the contract.** Reconstruct the larger shell from existing `floor_wood_2m`, brick/window modules and the processed café kit, placing workstations against the exact eight contract seats. Preserve a centered entrance lane and distinct coffee/social pockets.

Concrete route: a dedicated `TerminalCafeCandidate.cs` builder can use the same isolated material/LOD/bake approach, assign `venueId=terminal_cafe`, create positioned `SpotAnchor`s from the snapshot, and output a candidate prefab. Validate seat orientation/feet, all paths, camera visibility of the Pulse screen, native travel out/back, and finally device load/unload/performance before adding one registry entry. This is a second finished authored venue, not a new backend or a renamed Timeline room.

## Verification status

`python3 client/Tools~/compile_check.py --editor` passed with **all CT Life assemblies compile**, including the new builder. Log: `/tmp/ct-city-direction-compile.log`.

Native stage, bake, visual inspection, APK and device checks are coordinator-owned. Root subsequently reported Timeline stage/bake success; visual/device acceptance is still separate. No new gameplay implementation or whole-game completion is claimed.

After the initial research, root authorized the second venue builder: [TerminalCafeCandidate.cs](../../client/Assets/Scripts/Editor/TerminalCafeCandidate.cs). Its [candidate record](../../art/direction-20261006/terminal-cafe/README.md) describes the implemented source and outstanding native gates. It uses the exact snapshot footprint/anchors and existing processed assets rather than rescaling the smaller Blender reference room.
