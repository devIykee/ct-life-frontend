# art/lookdev — licences

Everything used to make the lookdev renders. **No CC-BY, no Synty, no Unity Asset Store, no Mixamo,
no logins.** All third-party 3D/texture/HDRI assets are **CC0 1.0**; fonts are **SIL OFL 1.1**;
all text, logos, screens and billboards are original work made by `make_textures.py`.

Licences were checked on the live pages on **2026-10-05** (the download date for every row below).
Raw files live in `art/lookdev/src/` (gitignored). `python3 art/lookdev/fetch_assets.py` re-downloads them.

## Quaternius (CC0 1.0): author Quaternius (quaternius.com)

Licence evidence: each pack page shows the CC0 badge and says "Free to use in personal, educational and
commercial projects"; the itch.io pages say "Asset license: Creative Commons Zero v1.0 Universal". The
bundled `License.txt` / `License_Standard.txt` files say "CC0 1.0 Universal (CC0 1.0) Public Domain Dedication".
Only the **free** tiers were downloaded, through public "download" links with no login.

| Asset | Source URL | Files used | Used in |
| --- | --- | --- | --- |
| Downtown City MegaKit [Standard] | https://quaternius.com/packs/downtowncitymegakit.html → https://quaternius.itch.io/downtown-city-megakit | `Building_Large_2`, `Building_Medium_2_001`, `Building_Small_1` (glTF Godot export) + `T_RedBrick_*` textures | Square: all brick buildings. Café: neighbour buildings + exposed-brick wall texture |
| Stylized Nature MegaKit [Standard] | https://quaternius.com/packs/stylizednaturemegakit.html → https://quaternius.itch.io/stylized-nature-megakit | `TwistedTree_1/3/4` (leaves recoloured pink = blossom trees), `CommonTree_1/2/4/5`, `Bush_Common(_Flowers)`, `Flower_3/4_Group` | Square: all trees, bushes, flowers. Café: trees and bushes outside |
| Background Posed Humans | https://quaternius.com/packs/backgroundposedhumans.html (Google Drive folder linked there) | All 19 male/female pose FBXs + 8 hairstyle FBXs | Every person in all three images (recoloured per person; hair placed by script) |
| Cars Pack | https://quaternius.com/packs/cars.html (Google Drive folder linked there) | `Taxi`, `NormalCar1`, `SUV`, `SportsCar` (FBX) | Square: road traffic. (`Cop`, `NormalCar2`, `SportsCar2` downloaded, not used) |

## Poly Haven (CC0 1.0): https://polyhaven.com/license

"You can use our assets for any purpose, including commercial work. You do not need to give credit."
All files were downloaded from `https://api.polyhaven.com/files/<id>` at 1k (models/textures) or 4k/2k (HDRIs).

| Asset (id) | Author(s) | Used in |
| --- | --- | --- |
| HDRI `kiara_7_late-afternoon` (4k) | Greg Zaal | Sky + ambient light in both scenes (sun disc clamped out; a Blender sun lamp provides the key light) |
| Texture `herringbone_pavement` | Charlotte Baglioni | Square: plaza paving (tinted warm sand) |
| Texture `brick_pavement_02` | Charlotte Baglioni | Square: plaza border band |
| Texture `concrete_pavement` | Charlotte Baglioni | Square sidewalks; café exterior |
| Texture `clean_asphalt` | Dimitrios Savva | Square: roads |
| Texture `leafy_grass` | Charlotte Baglioni | Lawns in both scenes |
| Texture `plastered_wall_04` | Rob Tuytel | Café walls (tinted teal / cream) |
| Texture `wood_floor` | Dimitrios Savva | Café floor |
| Model `street_lamp_01` | Josh Dean | Square street lamps |
| Model `painted_wooden_bench` | Kirill Sannikov | Square benches; café exterior bench |
| Model `metal_trash_can` | GurJas Studios | Square |
| Model `fire_hydrant` | Gonçalo Felício | Square |
| Model `planter_box_01` | James Ray Cock | Square |
| Model `CoffeeCart_01` | Joe Seabuhr | Square |
| Model `outdoor_table_chair_set_01` | James Ray Cock | Square (café terrace) |
| Model `Megaphone_01` | Ethan Simon-Law | Square: Town Crier's megaphone |
| Model `metal_office_desk` | Ulan Cabanilla | Café trading desks |
| Model `modern_arm_chair_01` | Vibrant Nordic | Café desk chairs |
| Model `bar_chair_round_01` | Dairon Sanchez | Café bar stools |
| Model `classic_laptop` | Arrangemonk | Café desk |
| Model `Sofa_01` | Kirill Sannikov | Café lounge |
| Model `modern_coffee_table_01` | Amin | Café lounge |
| Model `potted_plant_01`, `potted_plant_02` | Rico Cilliers | Café plants |
| Downloaded, **not used** in the final renders: HDRI `aft_lounge`; models `jacaranda_tree`, `island_tree_01`, `island_tree_02` (Rob Tuytel, Rico Cilliers), `tree_small_02`, `shrub_01/02/04` (Rico Cilliers), `hanging_industrial_lamp` (Kuutti Siitonen), `modern_ceiling_lamp_01` (James Ray Cock), `Shelf_01` (Gabriel Radić) | — | Not used |

## Fonts (SIL Open Font License 1.1), from https://github.com/google/fonts (`ofl/` directory)

| Font | File | Used in |
| --- | --- | --- |
| Inter | `ofl/inter/Inter[opsz,wght].ttf` | Screen UI text, HUD, billboards |
| Space Grotesk | `ofl/spacegrotesk/SpaceGrotesk[wght].ttf` | Neon signs, storefront signs |
| JetBrains Mono | `ofl/jetbrainsmono/JetBrainsMono[wght].ttf` | Terminal / chart screens |
| Bebas Neue | `ofl/bebasneue/BebasNeue-Regular.ttf` | Billboard headlines |

The fonts are only rasterised into PNGs here. If they ship in the game, include the OFL text
(`src/fonts/inter_OFL.txt` holds Inter's copy; the other families use the same OFL 1.1 text in their `ofl/<family>/OFL.txt`).

## Original work (this repo)

`tex/*.png` (Timeline feed, $FROGE / RUG WEEK / gm / WAGMI REALTY / ORE REFINERY billboards, candlestick
charts, Pulse board, neon signs, Pixel Wall, awnings), the procedural Dubai-style glass towers, the HUD
in `timeline-square-phone.png`, and all scene layout are original, made by the scripts in this folder.
Names are parody only ($FROGE, $HOPIUM, $COPE, @just_in_bot…). No real brands, logos, people or handles.
