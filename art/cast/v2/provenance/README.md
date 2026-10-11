# Existing cast source provenance, checked 9 October 2026

This record concerns the existing seven exported bases. No assets were downloaded or regenerated in this continuation. Original FBX, PNG, per-base JSON and Blender files were preserved. It does not establish Unity import, animation, resemblance or player approval.

## Universal Base Characters Standard

The local pack contains `art/raw/quaternius/ubc/Universal Base Characters[Standard]/License_Standard.txt`, which explicitly labels this Standard distribution CC0 1.0. The two current first-party product pages also identify this pack as CC0:

- [Quaternius Universal Base Characters](https://quaternius.com/packs/universalbasecharacters.html), licence and features sections.
- [Official itch listing](https://quaternius.itch.io/universal-base-characters), licence metadata and commercial-use feature; the page reports its last update as 16 September 2026.

The separate [general Quaternius licence page](https://quaternius.com/license.html) currently presents QAL v1.0, dated 28 August 2026. It allows modification and incorporation in commercial products and collaborator access, restricts standalone asset redistribution, and addresses earlier releases in section7. This is a real discrepancy with the product-specific pages, not a reason to silently rewrite the bundled CC0 statement. We retain the pack-specific CC0 provenance and flag the general QAL terms. Internal game incorporation is within both published permissions. No standalone redistribution licence is inferred for this cast handoff. Any future asset-pack publication would need a separate licence decision.

Actual affected local sources: the male/female Superhero base glTF files and the existing UBC hairstyle files used by `v2_adapters.py`. The unused Ultimate Modular Men/Women packs are not evidence for these exports and are not included as available bases.

## Five authored hero sources

Direct model page requests returned403 in the browsing tool. The public first-party Sketchfab model API returned each model's current author, CC Attribution licence linked to CC-BY4.0, and downloadable=true. Exact endpoint, response hash, source-file hash and supplied attribution are retained in `sketchfab-verification-20261009.json`. This verifies the platform's current licence declaration, not an independent guarantee that an uploader owns all depicted rights.

| Export | Author | Primary source |
| --- | --- | --- |
| hero_tactical | DanlyVostok | [Game Ready low poly character tactical](https://sketchfab.com/3d-models/game-ready-low-poly-character-tactical-342501b0c84843a3a418319c31ea26fa) |
| hero_gynoid | Loves_Art | [Gynoid 2000 Series](https://sketchfab.com/3d-models/gynoid-2000-series-56993f528d7041f9b734c0221f5dffe0) |
| hero_assassin | Ilya.Anchouz.Danilov | [Cyberpunk Assassin Girl Free Rigged](https://sketchfab.com/3d-models/cyberpunk-assassin-girl-free-rigged-b7201c11c0ba494ca4ef158884ce4a05) |
| hero_hazmat | SKP_GAMES | [Elite Hazmat Operator](https://sketchfab.com/3d-models/elite-hazmat-operator-4d722398711c474cbbc393b117d4e668) |
| hero_balaclava | AlekseiPresnaykov | [CyberPunk Character](https://sketchfab.com/3d-models/cyberpunk-character-bd161bb708a941f0b436c2e95eafc755) |

Keep each source title, author, source URL, [CC-BY4.0 link](https://creativecommons.org/licenses/by/4.0/) and modification notice in shipped credits. CT Life modifications include retargeting or automatic rigging, topology reduction, atlas generation, region masks, modular separation and fitted accessories. Existing weapons/gloves removed by the original adapter remain removed. No celebrity endorsement or franchise identity is asserted.

## Preservation and validation

Pre-continuation scripts and metadata are preserved under `ct-life-evidence/claude-continuation-20261009/cast-reconcile/before`. `original-assets.json` pins all154 files under export/bases. The refreshed validation receipt imports all119 FBXs and reports zero input drift. It fails8 file checks; it is deliberately not rewritten as an overall pass. See the handoff for the split between metadata differences, an unreferenced older file and the tactical head-weight investigation.
