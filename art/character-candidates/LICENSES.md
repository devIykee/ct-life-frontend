# Candidate asset license ledger

Downloaded/verified 2026-10-05. **Source and derivative ledger:** the compiled Snow asset has been delivered for Unity integration; other sources remain candidates. This ledger is prepared for merging into `art/LICENSES.md` by the parent agent, which owns that file. The content here does not select a production asset.

| Asset / local files | Source URL | License / author | Date downloaded | Use |
| --- | --- | --- | --- | --- |
| Snow v4 archive, `raw/snow/snow.zip`, extracted `raw/snow/Snow/**` | https://studio.blender.org/characters/snow/v4/ ; download https://studio.blender.org/download-source/files/ba/0f/ba0fe6d810333b1d73c1c359e4dc03cb-6.zip | **CC-BY 4.0**, Blender Foundation / Blender Studio, publisher Demeter Dzadik. License link on page: https://creativecommons.org/licenses/by/4.0/ | 2026-10-05 | Source for the adult Snow compiled Unity derivative below; raw source untouched. Attribution: **Snow Rig (CC) Blender Foundation \| studio.blender.org**. |
| Rain v3 archive, `raw/rain/rain.zip`, extracted `raw/rain/Rain v3.3/**` | https://studio.blender.org/characters/rain/v3/ ; download https://studio.blender.org/download-source/files/ee/a7/eea73e55dba1cea31c09848df6a794b2-4.zip | **CC-BY 4.0**, Blender Foundation / Blender Studio, publisher Demeter Dzadik. License link on page: https://creativecommons.org/licenses/by/4.0/ | 2026-10-05 | Internal art/rig evaluation; derivative local renders and diagnostic export only. Attribution: **Rain Rig (CC) Blender Foundation \| studio.blender.org**. No Unity use. |
| Snow official preview, `thumbs/snow_official.jpg` (source file is PNG) | https://studio.blender.org/files/public/thumbnail/ba/0f/ba0fe6d810333b1d73c1c359e4dc03cb-6.png | Blender Foundation; accompanies the CC-BY character page above. Preserve source credit. | 2026-10-05 | Internal source-art reference; not game/store art. |
| Rain official preview, `thumbs/rain_official.jpg` | https://studio.blender.org/files/public/thumbnail/ee/a7/eea73e55dba1cea31c09848df6a794b2-4.jpg | Blender Foundation; accompanies the CC-BY character page above. Preserve source credit. | 2026-10-05 | Internal source-art reference; not game/store art. |
| MetaPerson male public sample, `raw/metaperson-evaluation-only/male.zip`, extracted `male/model.glb` | https://avatarsdk.com/avatars/ ; download https://drive.google.com/uc?export=download&id=1-xTFz5EhGGd2EkB5OYHXV-OFaNvRP-i5 | **Proprietary evaluation sample, itSeez3D. Not CC0/CC-BY.** Supplied publicly on the provider's official sample-model page. Terms: https://avatarsdk.com/eula/ . No standalone permissive license included. | 2026-10-05 | Internal technical/visual evaluation and comparison only. **Do not put model in game, distribute as an asset, sell, or reuse its garments on another body.** Commercial licensing remains unresolved. |
| MetaPerson female public sample, `raw/metaperson-evaluation-only/female.zip`, extracted `female/model.glb` | https://avatarsdk.com/avatars/ ; download https://drive.google.com/uc?export=download&id=1aOu0Yzr93_cj_IEXhqrFnfcrs1x_sE9b | **Proprietary evaluation sample, itSeez3D. Not CC0/CC-BY.** Same official source and terms as male sample. | 2026-10-05 | Internal technical/visual evaluation and comparison only. Same non-production restrictions as male sample. |
| Universal Base Characters Standard, existing local glTFs under `art/raw/quaternius/ubc/` | https://quaternius.itch.io/universal-base-characters | **CC0 1.0**, Quaternius. https://creativecommons.org/publicdomain/zero/1.0/ | Pre-existing download, reverified 2026-10-05 | Read-only source used in candidate renders. No new download or changes to original pack. |

`renders/`, `comparisons/` and `export-check/` contain derivatives of the corresponding sources above. Preview changes and limitations are documented in `README.md`. Source archive hashes and sizes are in `download-manifest.json`.

The pre-existing VIPE VRM, search thumbnails, and earlier research files were not downloaded by this pass. They remain unselected; this ledger does not certify their licenses or authorize game use.

CC-BY 4.0 permits sharing/adaptation including commercial use, with attribution, a license link, and indication of changes. See the [official deed](https://creativecommons.org/licenses/by/4.0/). The local models/render changes should carry the source credit and a changes note when distributed. Attribution does not imply endorsement.

## Single adult Snow derivative

`snow-adult-proof/` contains a derivative of Snow under **CC-BY 4.0**: `snow_adult_candidate.blend`, `snow_adult_lod0_candidate.fbx`, diagnostic renders and comparison sheets. Credit: **Snow Rig (CC) Blender Foundation | studio.blender.org**. Source: https://studio.blender.org/characters/snow/v4/ . Licence: https://creativecommons.org/licenses/by/4.0/ . Changes made 2026-10-05: adult head/leg/shoulder and orbital proportions; 27-bone experimental skeleton with remapped/normalized weights; simplified geometry, duplicate/loose geometry cleanup, two test morphs, static poses and studio renders. Candidate-only; no Unity installation or final art approval. Full limitations: `snow-adult-proof/README.md`.

## Targeted Snow hand-quality derivative

`snow-hand-quality/` is the next single-Snow CC-BY 4.0 derivative: source hand surfaces retained, 55-bone skeleton with 30 weighted digit joints, city/creator FBXs, phone-grip and open-wave diagnostics. Same required **Snow Rig (CC) Blender Foundation | studio.blender.org** credit and https://creativecommons.org/licenses/by/4.0/ licence. The plain phone proxy is original local diagnostic geometry, not a downloaded asset, and is excluded from avatar FBXs. Changes and limitations are recorded in `snow-hand-quality/README.md`. No Unity use or cast rollout.

## Compiled Snow Unity derivative

`unity-snow/exports/SnowAdult_Creator.fbx`, its three baked2048 source-texture maps, compiled blend and renders are Snow derivatives under **CC-BY4.0**. Required credit: **Snow Rig (CC) Blender Foundation | studio.blender.org**. Source https://studio.blender.org/characters/snow/v4/ ; licence https://creativecommons.org/licenses/by/4.0/ . Changes2026-10-05: adult proportions,55bone rig/weights, ground alignment, one skinned mesh, unique atlas UVs, base-colour/tangent-normal/metallic-smoothness bakes. Delivered to coordinating agent for actual Unity import at `Assets/Art/characters/authored/Snow`; this agent did not edit Unity. Source-quality near geometry retained; in-game validation belongs to root.

The compiled derivative now also includes `SnowAdult_LOD1.fbx`, `SnowAdult_LOD2.fbx`, `SnowAdult_LODs.blend` and comparison renders under the same CC-BY 4.0 source credit. Additional changes: per-part world reduction, protected hand/face/opening geometry, source normal transfer, unchanged atlas/skeleton and reviewed 21,005 / 12,135-triangle tiers. No new third-party download was required.

## Distinct adult Rain derivative and Sketchfab tab evidence

`rain-adult-production/` contains the ongoing CC-BY4.0 adult Rain derivative: her own55-bone rig with30 digit joints, source face/hands/clothing retained, adult head/leg proportions, downward ponytail rest shape, collar fit, target-only atlas UVs,2048 source-texture PBR maps and separate world LODs. Required credit: **Rain Rig (CC) Blender Foundation | studio.blender.org**. Source https://studio.blender.org/characters/rain/v3/ ; licence https://creativecommons.org/licenses/by/4.0/ . Changes dated2026-10-05. The original archive/source is unchanged. This is a distinct authored face/body/outfit, not a Snow recolour. Frozen-bundle status and limitations are recorded in the Rain directory; do not infer Unity acceptance from this credit entry.

`user-tabs-audit/thumbs/` contains6 exact official Sketchfab publisher thumbnails, credited individually in `user-tabs-audit/README.md` and `download-manifest.json`, for internal visual research only. Four models carry Standard metadata and two CC-BY4.0; model licence does not establish third-party franchise/imagery clearance. No geometry package from those six tabs was acquired, no thumbnail is game/store art, and none was passed into image generation. Model/API source URLs, retrieval date and SHA256 are retained.


## Distinct male source comparison — 2026-10-06

Actual official source files, downloaded anonymously for geometry/material/rig comparison. No Unity use or game selection is claimed by this row. Frozen Snow/Rain files are unchanged.

| Source / local file | Official source | Licence / attribution | Download | Use |
|---|---|---|---|---|
| Phil / `raw/phil/phil.blend` | https://studio.blender.org/characters/phil/v1/ ; https://studio.blender.org/download-source/files/c1/c1a19179f6136a2f8c7b802d1201ea3b/c1a19179f6136a2f8c7b802d1201ea3b.blend | CC BY4.0, Blender Foundation / Blender Studio. https://creativecommons.org/licenses/by/4.0/ | 2026-10-06 | Actual source comparison; SHA256 `b9671b4add7a7e52000c5dba3ddfa322fb0c0c85abf70ea3ad5fcf6499947e59` |
| Rex / `raw/rex/rex.blend` | https://studio.blender.org/characters/rex/v1/ ; https://studio.blender.org/download-source/files/cb/cb8d453b64a548d03bcee7671f83e599/cb8d453b64a548d03bcee7671f83e599.blend | CC BY4.0, Blender Foundation / Blender Studio. https://creativecommons.org/licenses/by/4.0/ | 2026-10-06 | Actual source comparison; SHA256 `7e0a774da5f309cde4eb9ba313890b554b9fd8d284d2b668a28f72b1380e5335` |
| Vincent / `raw/vincent/vincent.blend` | https://studio.blender.org/characters/vincent/v1/ ; https://studio.blender.org/download-source/files/07/07fc1f2630bf448a96ba4d086d160fc7/07fc1f2630bf448a96ba4d086d160fc7.blend | CC BY4.0, Blender Foundation / Blender Studio. https://creativecommons.org/licenses/by/4.0/ | 2026-10-06 | Actual source comparison; SHA256 `4b616d0f39c1e9b7547218d481c2b3486562005b35594ab8488735d47b2b79c0` |

Complete byte counts, hashes and live anonymous GET evidence: `male-source-audit/download-manifest.json`; source-page license links and snapshots: `male-source-audit/page-manifest.json` and `pages/`. Phil/Rex publisher: Demeter Dzadik. Vincent v1 lists Andy Goralczyk as publisher. Source credit: Vincent (CC) Blender Foundation | studio.blender.org; retain the source-page link, licence link, and a changes note with a derivative.


## Vincent Weekender derivative and offline tools — 2026-10-06

`vincent-adult-production/` is the adapted Vincent candidate: adult proportions, own55-bone rig, source-derived weights, five evaluated facial morphs, opaque one-material2048 PBR atlas, cloth micro-bump attenuation, creator and two visually reviewed reduction tiers. Raw source unchanged. Required credit: **Vincent (CC) Blender Foundation | studio.blender.org**, [source](https://studio.blender.org/characters/vincent/v1/), [CC BY4.0](https://creativecommons.org/licenses/by/4.0/). Record the adaptation when distributing. The complete-look UI proposal is **Weekender**; native acceptance and backend availability remain separate gates.

Offline build tools, not game/runtime dependencies: **xatlas-python0.0.11**, MIT, [primary source](https://github.com/mworchel/xatlas-python) with xatlas by Jonathan Young, [primary source](https://github.com/jpcy/xatlas); **meshoptimizer1.3**, MIT, Arseny Kapoulkine, [release/source](https://github.com/zeux/meshoptimizer/releases/tag/v1.3). Verified download hashes and tool scope are in `male-source-audit/{xatlas-tool-provenance,meshoptimizer-tool-provenance}.json`. Tool archives/libraries live in `/tmp`; no third-party tool code is copied into the runtime or this repository.
