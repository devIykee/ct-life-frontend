# Next cast source research — 2026-10-05

**[Unverified — visual fit after adaptation, Unity retargeting and Seeker behavior remain untested.]** Source availability and the measurements below were checked directly. This was research only: no client assets/code, Unity, device operations, accounts, purchases, viewer extraction or generative image tools.

## Recommendation

The strongest **technically usable, account-free source found is Microsoft Rocketbox, starting with `Male_Adult_12`**. Its official preview shows a darker skin tone, short dark hair, a denim jacket, white shirt, jeans, individually shaped fingers and a modeled face. The downloaded FBX contains 6,722 triangles and 30 finger joints. This is a source for a deliberate art comparison, **not approval to ship it beside Snow/Rain**: my assessment of the actual preview is that its photographic textures look dated and substantially more realistic than the current cast. I did not find a verified drop-in source meeting both the visual direction and all access conditions.

Use the [official pinned character folder](https://github.com/microsoft/Microsoft-Rocketbox/tree/0943055db6ec570bcef9f2c8b41c9e5467c808f9/Assets/Avatars/Adults/Male_Adult_12). The [publisher README](https://github.com/microsoft/Microsoft-Rocketbox) explicitly applies MIT to the avatar library; the [actual license](https://github.com/microsoft/Microsoft-Rocketbox/blob/0943055db6ec570bcef9f2c8b41c9e5467c808f9/LICENSE.md) permits modification and commercial distribution while retaining the notice. The repository is archived; this is a static art source, not a maintained Unity dependency. Do not install its old importer wholesale.

## Three distinct adults actually inspected

These are publisher renders, **not in-game evidence**. All three FBXs and previews were downloaded anonymously from Microsoft's pinned public repository into session scratch. Geometry/material counts below come from their binary FBX data, not marketing descriptions.

| Candidate and actual preview | Visible distinction | FBX bytes | Triangles / control vertices | Material definitions |
|---|---|---:|---:|---:|
| [Male_Adult_12](https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/0943055db6ec570bcef9f2c8b41c9e5467c808f9/Assets/Avatars/Adults/Male_Adult_12/Male_Adult_12.png) — first trial | Darker skin, close-cut hair, denim outfit | 487,776 | 6,722 / 3,383 | 2 |
| [Male_Adult_03](https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/0943055db6ec570bcef9f2c8b41c9e5467c808f9/Assets/Avatars/Adults/Male_Adult_03/Male_Adult_03.png) | Older appearance, gray hair, checked blazer, burgundy shirt | 513,728 | 7,200 / 3,749 | 3 |
| [Female_Adult_13](https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/0943055db6ec570bcef9f2c8b41c9e5467c808f9/Assets/Avatars/Adults/Female_Adult_13/Female_Adult_13.png) | Short blond hair, gray waistcoat, flared jeans | 605,168 | 8,708 / 4,734 | 3 |

The age descriptors above are visual assessments, not documented ages. The folder names classify them as adults. This set adds age, skin, hairstyle and outfit variation; it does **not** yet provide a strong stockier-body candidate. `Male_Adult_20` was also inspected (7,120 triangles, brown hoodie); its build does not clearly satisfy that missing role.

## Measured budget and access for the strongest candidate

- [Base FBX](https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/0943055db6ec570bcef9f2c8b41c9e5467c808f9/Assets/Avatars/Adults/Male_Adult_12/Export/Male_Adult_12.fbx): 487,776 bytes, binary FBX 7400, **one mesh**, 6,722 triangular polygons. SHA-256 `b44fa6011b934abd69954fd6c5025c1e2011e05c7892acb6bbc2e9cf70a84014`.
- 80 limb-node definitions and 80 skin clusters, including both eyes, jaw, facial bones and **30 finger joints**. Nonzero skin influences per vertex: 1,871 vertices have one, 972 have two, 472 have three, 68 have four. No vertex exceeds four.
- Two original materials (`m007_body`, `m007_head`). Six TGA maps: body/head color, normal and specular. Every texture's public GET returned HTTP206 without authentication; each header confirms **2048×2048, 24-bit**, each source file 12,582,956 bytes.
- Exact base FBX + six source textures: **75,985,512 bytes (72.47 MiB)**. Full character folder including alternate facial FBX and preview: **78,264,394 bytes (74.64 MiB)**. These are source-download sizes, not APK or GPU memory predictions.
- No authored LOD set is present in this inspected base FBX: it contains only one geometry. The library README mentions other detail levels generally; that does not prove this export contains them.
- File metadata records `UnitScaleFactor=1.0`; raw geometry extends approximately 180.7 source units along its longest body axis. The source's geometric axis and global axis metadata need normal import evaluation. Do not copy Snow's rig scale/rest transforms or assign Snow's Avatar to it.

**Proposed mobile adaptation budget, not achieved output:** preserve this existing 6,722-triangle hero mesh first; combine the two material regions into one atlas while reserving enough face texels, retain articulated fingers, and use at most three 2048² maps. ASTC 6×6 with mipmaps would be approximately 7.2 MiB for those three maps; actual format/import and quality need verification. Do not lower facial geometry simply to produce a smaller number. LODs, own Humanoid mapping, standing/walking hands and feet, and a side-by-side Snow/Rain render remain required before rollout. No forecast of 62-character performance is supported by this research.

## Attractive sources rejected or held

- [BELAZ — An Elderly Man](https://sketchfab.com/3d-models/an-elderly-man-60cca77799e249e39ef53e3112baa4a7): the official public metadata reports 14,303 triangles, 7,461 vertices, rigged FBX and CC-BY4.0. Its actual thumbnail shows a bald, white-bearded man with glasses, gray sweater and brown trousers. **Blocked:** the ordinary Sketchfab download endpoint returns HTTP401 without authentication. Public `isDownloadable=true` is not evidence that we can obtain the package account-free. No viewer geometry extracted.
- [Phil](https://studio.blender.org/characters/phil/v1/): CC-BY; actual preview is stockier with a detailed face and fingers, but very exaggerated proportions. Official public download GET succeeds without an account (15,081,086-byte `.blend`). The publisher states Blender3.3–3.6 compatibility. Not recommended as an adult Sims-style shortcut.
- [Rex](https://studio.blender.org/characters/rex/v1/): CC-BY; public download GET succeeds (88,742,805-byte `.blend`). Preview shows a strongly caricatured long face and muscular cricket outfit. The same old Blender compatibility warning applies.
- [Jay](https://studio.blender.org/characters/jay/v1/): darker skin and distinct hair/outfit, but the actual preview's exaggerated face, enormous shoes and narrow torso are a poor match. No package was acquired.
- [Einar](https://studio.blender.org/characters/einar/v1/): CC-BY, detailed older face and hands; the preview shows bulky scavenged clothing and a robotic arm. Adapting that genre and a film rig would be a separate art project. Package access/size was not verified with GET.
- [Vincent](https://studio.blender.org/characters/vincent/v2/): attractive softer face, but another slim light-skinned adult-like design, a BlenRig dependency, and no download link on the retrieved v2 page. Not the strongest answer to the missing cast roles.

Blender Studio's web-tool fetch returned402 and HEAD download requests returned403, while ordinary anonymous page GET and public download GET succeeded for Phil/Rex. Report the successful GET evidence; do not mislabel the HEAD responses as an account requirement. The downloads were requested through the exact publisher links, with no cookies/authentication. Temporary signed redirects are deliberately not recorded here.

## Reproducibility and limits

Pinned Rocketbox tree: `0943055db6ec570bcef9f2c8b41c9e5467c808f9`. The official repository's tree API supplied each source file byte count; binary FBX inspection counted terminated polygon indices, mesh control vertices, material definitions, limb nodes and skin-cluster weights. These are pre-Unity values: UV seams/import processing can change renderer vertex counts.

Scratch evidence lives under `/var/folders/zm/v0w94xb95p11_5df6rxm0js80000gp/T/ct-cast-research/`: publisher HTML/previews, `rocketbox-tree.json`, `rocketbox-measured.json`, `rocketbox-rig-audit.json`, `rocketbox-textures-access.json` and four small FBXs. Nothing was imported. If a source is selected for production, copy its complete notice into `art/LICENSES.md` and the shipped credits before importing; no production-use row is claimed by this research.
