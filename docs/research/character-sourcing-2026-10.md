# Character sourcing: distinct base characters for CT Life (2026-10-09)

**Status label: mixed.** Licence, face count, animation count, author and downloadable flag for Sketchfab rows come from the Sketchfab public search API (fetched 2026-10-09). Everything in the "ethnicity/gender read", "style", "archetype" columns is `[Inference]` from looking at thumbnails only. No model file was downloaded or opened, so every rig claim is `[Unverified]` until opened in Unity/Blender. The per-model `/v3/models/<uid>` endpoint returned HTTP 429 (rate-limited) for the whole session, so licence *slugs* are derived from the licence label (`CC Attribution` = `by`, `CC Attribution-ShareAlike` = `by-sa`) and are marked `slug_verified=false` in the JSON `[Inference]`.

Contact sheets: `/Users/mac/Documents/codes/projects/ct-life-evidence/character-sourcing/candidates-sheet-1.png` (1-30) and `candidates-sheet-2.png` (31-56). Thumbnails: `.../character-sourcing/thumbs/NN_<uid8>.jpg`. Twin data: `character-sourcing-2026-10.json`.

## Key findings

1. **There are effectively zero CC0 rigged humanoids on Sketchfab.** Every CC0 + downloadable hit I found (about 36 across 37 queries) was a museum scan (stelae, figurines, statues, skulls). All usable Sketchfab characters are **CC-BY** (attribution required, commercial use allowed). We need a credits screen and an attribution file. CC-BY-SA hits exist (Boxer, anime male) but are excluded from the table: share-alike: avoid unless needed.
2. **The truly CC0 distinct-human sources are Quaternius packs** (rows 54-56): Ultimate Modular Men (11 men), Ultimate Modular Women (10 women), Universal Base Characters (6, already on disk as the free "Standard" tier, licence file says CC0). They are low-poly or stylised-semi-real, so they set the *style floor*; Sketchfab CC-BY models are the "cool" layer.
3. **Style mismatch is the main risk.** The pool mixes realistic, anime, PS2-retro and low-poly. Plan on a unifying pass (shared toon/lit shader, palette-limited textures, same eye/face treatment) or pick one or two style lanes (see architecture section).
4. **Many "cool" candidates have no rig metadata** (animationCount 0, no rig tags). They are T-pose or game-ready meshes. Mixamo auto-rig usually handles a clean humanoid T-pose, but that is `[Unverified]` per model.
5. Hooded/masked/faceless characters (rows 2, 6, 7, 8, 9, 22, 26) are the cheapest way to cover "anon" and sidestep face and ethnicity matching; skin tint only affects hands.

## Candidate table (56 rows; ranked roughly by CT fit then quality)

Faces are the Sketchfab faceCount (triangles). "Rig" = what the metadata says. Licence for all Sketchfab rows: CC Attribution (CC-BY) unless stated.

| # | Name | Author | Licence | Faces | Rig / anim | Style (Inference) | Archetype fit (Inference) | Read (Inference) | Notes | URL |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Cyberpunk Assassin Girl Free Rigged | Ilya.Anchouz.Danilov | CC-BY | 18025 | rigged + animated (animationCount=1) | cyberpunk, techwear, gas-mask/visor, jacket | sniper, anon | F; mixed/ambiguous (helmet) | Orange techwear + visor mask; strongest anon/sniper silhouette. 18k faces. | https://sketchfab.com/3d-models/none-b7201c11c0ba494ca4ef158884ce4a05 |
| 2 | Tvman | AspAscon3D | CC-BY | 32350 | rigged + animated (animationCount=1) | TV-head, neon, suit, cyber | anon, KOL, bot | M-presenting; faceless | Monitor-head = instant CT anon; face is a screen so no face-art needed. 32k, decimate. | https://sketchfab.com/3d-models/none-3a4b3f20412a446d9e163927eeeeb293 |
| 3 | Cyber Girl | Rotmill | CC-BY | 26152 | rigged + animated (animationCount=1) | anime, cyberpunk, sci-fi bodysuit | trader, sniper | F; anime, white hair | Pair image shows two poses of one model. Anime-adjacent. 26k. | https://sketchfab.com/3d-models/none-46640796f0dd4fbab5eba5262d542e25 |
| 4 | Cool Girl | Rotmill | CC-BY | 8565 | rigged + animated (animationCount=1) | anime, noir, trench coat, long coat | KOL, normie | F; East-Asian-coded anime | Long trench coat; low 8.5k faces. Check clothing-region split. | https://sketchfab.com/3d-models/none-779942ef230c4c05ae7e658a2937c11d |
| 5 | Character Male (Sci-fi) | LokitoBlu | CC-BY | 17075 | [Unverified] no rig/animation metadata; T-pose/game-ready mesh, plan on Mixamo auto-rig | streetwear, yellow puffer, sneakers, cyber | degen, trader | M; light skin, young | Best streetwear read in the pool. Mesh-only (no rig tags): [Unverified] needs Mixamo auto-rig. | https://sketchfab.com/3d-models/none-0010a35d806a470b8b01b3496b7dc631 |
| 6 | Elite Hazmat Operator | SKP_GAMES | CC-BY | 32256 | [Unverified] no rig/animation metadata; T-pose/game-ready mesh, plan on Mixamo auto-rig | techwear, hazmat/tactical, full-face mask | anon, sniper bot, cabal | M-presenting; helmeted | Faceless so any ethnicity via skin tint of hands only. 32k. | https://sketchfab.com/3d-models/none-4d722398711c474cbbc393b117d4e668 |
| 7 | Shadow Operative (Full Body) | achrefrhm9 | CC-BY | 30120 | rigged per title/tags [Unverified until opened] | hood, cape, exosuit, stealth | anon, cabal | M-presenting; hooded | Hooded cape silhouette. [Unverified] rig. 30k. | https://sketchfab.com/3d-models/none-becd2d1dd8924311b95cdacbb81d8a61 |
| 8 | Martian Space Pirate | AndrewDW | CC-BY | 35831 | [Unverified] no rig/animation metadata; T-pose/game-ready mesh, plan on Mixamo auto-rig | hood, mask, desert-punk robes, goggles | anon, degen | F/M ambiguous; masked | Hooded masked wanderer. 35.8k, heavy. [Unverified] rig. | https://sketchfab.com/3d-models/none-c692387df5644fba87a8994e7dd04e5e |
| 9 | Assassin-male | milakpro | CC-BY | 50000 | rigged + animated (animationCount=2) | hood, mask, cloak, ninja-adjacent | anon | M; masked | Hooded anon; 50k faces is at the limit - decimate hard. HEAVY (>40k): decimate hard. | https://sketchfab.com/3d-models/none-1ab4305b96674fb2b8a2b22e313e8f68 |
| 10 | Cyberpunk girl Low-poly 3D model | Q.SARDOR | CC-BY | 39027 | rigged + animated (animationCount=1) | cyberpunk, mixamo, jacket, implants | trader, KOL | F; light-medium skin, red cyber marks | Tagged mixamo. 39k, animated 1. | https://sketchfab.com/3d-models/none-aeec41f7d33647d1bb1d3079db51b5cc |
| 11 | Cyberpunk female hacker | achrefrhm9 | CC-BY | 18312 | rigged per title/tags [Unverified until opened] | hacker, hood, cyber, techwear | builder, anon, sniper | F; hooded | Cyber hacker with hood up. [Unverified] rig. | https://sketchfab.com/3d-models/none-c1496b5f902f45b8ae4cb0b08c9607ae |
| 12 | Cyberpunk/Scifi Female Protagonist | yronthal | CC-BY | 9975 | [Unverified] no rig/animation metadata; T-pose/game-ready mesh, plan on Mixamo auto-rig | cyberpunk, sci-fi, orange/blue suit | sniper, trader | F; South-Asian/Latina-coded | Low 10k faces; protagonist-style. | https://sketchfab.com/3d-models/none-01da2468f177498daa9d24f31455c64c |
| 13 | CyberPunk Character | AlekseiPresnaykov | CC-BY | 51704 | [Unverified] no rig/animation metadata; T-pose/game-ready mesh, plan on Mixamo auto-rig | cyberpunk, balaclava, jacket, tactical | cabal, degen | M; masked, light skin | 51.7k: needs aggressive decimation. [Unverified] rig. HEAVY (>40k): decimate hard. | https://sketchfab.com/3d-models/none-bd161bb708a941f0b436c2e95eafc755 |
| 14 | FREE Rigged Cyberg Character | Astronaut | CC-BY | 4807 | rigged + animated (animationCount=2) | cyborg, low poly, stylised, orange | bot, degen | M-presenting; cyborg | 4.8k - ideal crowd/bot, 2 anims. | https://sketchfab.com/3d-models/none-48e6566ac52b446096c3bbcc2feb32be |
| 15 | Sci-Fi Cyborg Character | HypJo | CC-BY | 10338 | rigged per title/tags [Unverified until opened] | cyborg, armour suit, game-ready | sniper bot | M-presenting; cyborg | T-pose biped, 10k. [Unverified] rig. | https://sketchfab.com/3d-models/none-91f2c3da0a0648b7b371b4d9ff584e9f |
| 16 | Gynoid 2000 Series | Loves_Art | CC-BY | 24148 | rigged + animated (animationCount=1) | gynoid, white/blue, sleek | sniper bot | F-presenting; robot | Sibling of #17; 24k, animated. | https://sketchfab.com/3d-models/none-56993f528d7041f9b734c0221f5dffe0 |
| 17 | Gynoid 1000 Series | Loves_Art | CC-BY | 41276 | rigged + animated (animationCount=1) | gynoid, chrome/blue | sniper bot | F-presenting; robot | 41k heavy; use #16 first. HEAVY (>40k): decimate hard. | https://sketchfab.com/3d-models/none-69fe1348d12c4338b2d32b7f3f3393d3 |
| 18 | Male Hooded Character - IDLE [MESHY] | Ethan Cox | CC-BY | 10000 | rigged + animated (animationCount=1) | hoodie, urban, cargo, AI-generated (Meshy) | builder, anon | M; hooded olive | Closest to a plain CT hoodie anon. Provenance: Meshy AI-generated - confirm Meshy terms permit CC-BY relicense before use. | https://sketchfab.com/3d-models/none-324bb454193f41e9b46e286a06a5a5f9 |
| 19 | Cool Man | ardhanaputra | CC-BY | 16737 | rigged + animated (animationCount=6) | long coat/parka, jacket, glasses | KOL, builder | M; light skin, glasses | 6 animations, 16.7k. | https://sketchfab.com/3d-models/none-ad14b71697dd4ea7836c1f06c75e5f72 |
| 20 | Man In Coat - Human Rigged Model | Saitam | CC-BY | 31323 | rigged + animated (animationCount=1) | long coat, trench, realistic | whale, maxi | M; light skin | 31k realistic; T-pose. | https://sketchfab.com/3d-models/none-cd71038373d5427099a7970f1a1af2df |
| 21 | Hicks | T-Art | CC-BY | 7850 | rigged + animated (animationCount=1) | streetwear, graphic sweater, hacker | builder, normie | M; light skin, sunglasses | 7.8k, animated. Tagged hacker. Check sweater graphics for third-party logos. | https://sketchfab.com/3d-models/none-d875303643534c3f887e6fffbc23dfc0 |
| 22 | Ninja character rigged 3d model | Saad Asif Attari | CC-BY | 24780 | rigged + animated (animationCount=1) | ninja, full mask, dark suit | anon | M-presenting; masked | Fully masked. 24.8k. | https://sketchfab.com/3d-models/none-c7da1dc8294947ad82fc984765594572 |
| 23 | biker girl #2 | BELAZ | CC-BY | 15244 | rigged per title/tags [Unverified until opened] | biker helmet, leather jacket | degen, anon | F; helmeted biker | BELAZ set (many similar girls); 15k. | https://sketchfab.com/3d-models/none-7cb50fa99eed4dedb0200b58aefb4f97 |
| 24 | Street Gangster / Realistic 3D Character Model | PropShop™ | CC-BY | 17088 | [Unverified] no rig/animation metadata; T-pose/game-ready mesh, plan on Mixamo auto-rig | street, bandana mask, tattoos, realistic | degen, cabal | M; muscular, face-covering bandana, tattoos | Realistic style clashes with stylised cast; [Unverified] rig. | https://sketchfab.com/3d-models/none-ff784828aa0541a6a225711cd6230398 |
| 25 | Game Ready Low Poly Tactical Character | DanlyVostok | CC-BY | 23822 | rigged + animated (animationCount=1) | anime, tactical vest, toon | sniper, trader | F; East-Asian anime | Anime-adjacent; 23.8k, animated. | https://sketchfab.com/3d-models/none-68213c7d97a04b1b963599180210f5bd |
| 26 | Game Ready low poly character tactical | DanlyVostok | CC-BY | 22868 | rigged + animated (animationCount=1) | tactical, red visor mask, vest | sniper, cabal | M; masked tactical | Pair with #25 (same author). | https://sketchfab.com/3d-models/none-342501b0c84843a3a418319c31ea26fa |
| 27 | Game character, Girl. Rigged, textured, animated | gbarzu | CC-BY | 23300 | rigged + animated (animationCount=3) | mixamo, bodysuit, sci-fi | trader | F; light skin | Rigged + 3 anims; 23k. | https://sketchfab.com/3d-models/none-e304748a4d704299bb1c37bb6f8cf40e |
| 28 | Upgraded Camerawoman Rigged Ik MIXAMO | Guzinho0 | CC-BY | 47297 | rigged per title/tags [Unverified until opened] | mixamo, neon cyber armour | KOL/streamer | F; neon cyber | 47k - heavy; the glow is texture-driven (emissive). HEAVY (>40k): decimate hard. | https://sketchfab.com/3d-models/none-9365e4560c9748ebaf2bc2864ede45aa |
| 29 | Middle Age Man / Rigged & Animated / FREE | Animator05 | CC-BY | 38286 | rigged + animated (animationCount=1) | bomber jacket, jeans, realistic | whale, maxi | M; East-Asian-coded, middle age | 38k. Use for an older normie/whale. | https://sketchfab.com/3d-models/none-3e660352084843d3bb36a371a7a6427b |
| 30 | indian character Named Sanjay T Pose with Rigged | Mrxxx | CC-BY | 22804 | rigged per title/tags [Unverified until opened] | jacket, sunglasses, T-pose, realistic | builder, trader | M; South-Asian | 22.8k. [Unverified] rig. | https://sketchfab.com/3d-models/none-f7d4c862e6ab4627bf20f50f151114b7 |
| 31 | Indian-man-with-blue-clothes | Himanshuvaid | CC-BY | 35976 | rigged + animated (animationCount=1) | kurta (blue), traditional, realistic | maxi, normie | M; South-Asian | 36k, animated. | https://sketchfab.com/3d-models/none-7cdbd4f668524e42982acb09cba57152 |
| 32 | Indian women / women / girl / Indian women / | DC Chaudhary | CC-BY | 35600 | rigged + animated (animationCount=1) | saree/kurta, stylised | normie, KOL | F; South-Asian | 35.6k; head reads cartoonish - check close-up. | https://sketchfab.com/3d-models/none-a12493879ccb4c549d772a1f194d53fd |
| 33 | Parameswara Chronicles: Shaikh Abdul Aziz | Dr Helmi Norman | CC-BY | 16566 | rigged + animated (animationCount=1) | keffiyeh, merchant, traditional | whale | M; Arab/Middle-Eastern | 16.5k, animated. Same author as other Malaysian-themed models. | https://sketchfab.com/3d-models/none-ca665011d8a044edaa19d8c678436e7c |
| 34 | A rigged, animated black African male character | Maggadog | CC-BY | 47898 | rigged + animated (animationCount=1) | street, tank top, realistic | trader, degen | M; Black/African | 47.9k, animated; heavy. HEAVY (>40k): decimate hard. | https://sketchfab.com/3d-models/none-6507aba4e704464086e3ff528e3635e2 |
| 35 | Thandi (T-Pose) GHf05 | greyHouse34 | CC-BY | 19196 | rigged per title/tags [Unverified until opened] | casual, T-pose, game asset | KOL, builder | F; Black/African | 19k. | https://sketchfab.com/3d-models/none-c82b0c6e28724efdbb5621b5351d84b7 |
| 36 | MPFB Woman3l_Animations_3_3 | Maggadog | CC-BY | 34932 | rigged + animated (animationCount=1) | MPFB (MakeHuman) base, casual | normie | F; Black/African, hijab/head-wrap | Output of MakeHuman MPFB; 35k. | https://sketchfab.com/3d-models/none-9ba9fd66bcb8406aa9bffe2588e5660b |
| 37 | Ps2 style Senegalese Casamance male and female | Humans of the World | CC-BY | 7225 | rigged + animated (animationCount=4) | PS2 retro low-poly, 4 anims each | normie, crowd | M+F pair; Black/African (Senegalese) | IMAGE SHOWS 2 CHARACTERS (M and F) in one model page. 7.2k. | https://sketchfab.com/3d-models/none-cd34fcf7d3e94368ac1364c97841af2e |
| 38 | Free ps2 style isthmid amerindian characters | Humans of the World | CC-BY | 8109 | rigged + animated (animationCount=4) | PS2 retro low-poly | normie, crowd | M+F pair; Latin-American/Amerindian | TWO characters. 8.1k. | https://sketchfab.com/3d-models/none-cdb4d27bd4154712968eb3f0961bb375 |
| 39 | Asian Girl PS1/PS2 Character / McPato | McPato | CC-BY | 15532 | rigged per title/tags [Unverified until opened] | PS1/PS2 retro, scarf | degen, normie | F; East-Asian | Thumbnail is a head close-up; full body is retro low-poly, 15.5k. | https://sketchfab.com/3d-models/none-dae6f2d7e60e4a37be25a36ed082e16c |
| 40 | Stylized low poly woman wearing a hoodie | Oscar Goldsworthy | CC-BY | 3753 | rigged per title/tags [Unverified until opened] | hoodie, stylised low poly | builder, normie | F; light skin | 3.7k - crowd/hero hoodie, no anims. | https://sketchfab.com/3d-models/none-cd1b96207b40465b9c460b5c480afedb |
| 41 | Female Character With Casual Clothing | gamedevme | CC-BY | 11934 | rigged per title/tags [Unverified until opened] | cargo pants, crop top, modern | normie, degen | F; East-Asian-coded | 11.9k; pair with #46? (same author as casual male). | https://sketchfab.com/3d-models/none-622a56e7f30446b694f59245d3b01bcc |
| 42 | Female Character Lowpoly model | kolani3d | CC-BY | 16986 | rigged per title/tags [Unverified until opened] | punk hair, jeans, boots, stylised | degen | F; light, green hair | 17k. | https://sketchfab.com/3d-models/none-7b9bdb5245ea4049b2954b8451549adb |
| 43 | Stylized Female Game Character Apocalyptic | JHauge | CC-BY | 15725 | rigged per title/tags [Unverified until opened] | scavenger, utility belt, stylised | degen, sniper | F; post-apoc, light-medium skin | 15.7k. | https://sketchfab.com/3d-models/none-bf4a852dfe8d4e30b21c10a6942f61f7 |
| 44 | Casual NPC Woman / Free Low-Poly 3D Model | Stan | CC-BY | 6498 | rigged + animated (animationCount=1) | casual NPC, mixamo-ready, low poly | normie | F; East-Asian-coded | 6.5k, tagged mixamo/UE-ready, animated. | https://sketchfab.com/3d-models/none-48902da4e3f8494b80e349a3cb819385 |
| 45 | Business Man - Low Polygon game character | manoeldarochadeoliveira | CC-BY | 4574 | rigged + animated (animationCount=25) | business suit, low poly | VC, whale | M; light skin | 4.6k, 25 animations (great test rig). | https://sketchfab.com/3d-models/none-b6f6740f883b4749abac47af0045a9dd |
| 46 | Carla Rigged 001 - Rigged 3D Business Women | Renderpeople | CC-BY | 19988 | rigged per title/tags [Unverified until opened] | business wear, Renderpeople (realistic) | VC, KOL | F; Black/mixed | 20k, rigged. Realistic style. | https://sketchfab.com/3d-models/none-acf520f450d14dd799f98a6fede3edf5 |
| 47 | Anime Boy | kazukiarts | CC-BY | 12694 | [Unverified] no rig/animation metadata; T-pose/game-ready mesh, plan on Mixamo auto-rig | anime, dark jacket, original character | trader | M; East-Asian anime | 12.7k; T-pose. | https://sketchfab.com/3d-models/none-cf5de6e8d866489488c138571d15f8d1 |
| 48 | Anime Model, Beuatiful Boy, Rigged! | dequeijospizza | CC-BY | 33408 | rigged per title/tags [Unverified until opened] | anime, hoodie, rigged | KOL, degen | M; anime, red hoodie | 33k; red hoodie = CT-streetwear read. | https://sketchfab.com/3d-models/none-b1beef2688a1411ba88909f9089a1747 |
| 49 | Modular Humanoid Characters / Male (Free Demo) | joaobaltieri | CC-BY | 3876 | rigged per title/tags [Unverified until opened] | modular, toon, mobile-ready | normie, crowd | M; East-Asian-coded | Mobile-ready 3.9k; designed for modular swaps. | https://sketchfab.com/3d-models/none-dd4cb5e8d0e74cd9b05c44b0f738d2d9 |
| 50 | Modular Humanoid Characters / Female (Free Demo) | joaobaltieri | CC-BY | 3660 | rigged per title/tags [Unverified until opened] | modular, toon, mobile-ready | normie, crowd | F; East-Asian-coded | 3.7k; sibling of #49. | https://sketchfab.com/3d-models/none-2e8403c4e687412d983a08df8599aaa9 |
| 51 | JUMPSUIT Low Poly | DanlyVostok | CC-BY | 26870 | [Unverified] no rig/animation metadata; T-pose/game-ready mesh, plan on Mixamo auto-rig | jumpsuit, helmet, sponsor logos? | sniper, degen | M; helmeted, racing jumpsuit | [Unverified] rig. Sponsor decals look like real-brand logos: avoid unless retextured. | https://sketchfab.com/3d-models/none-ddbd830d325b40ceb3829149de146d63 |
| 52 | Cyborg Oni/鬼械姫 | Kai Xiang | CC-BY | 34042 | rigged + animated (animationCount=2) | cyborg, dark, spear-limbs (horned oni) | sniper bot, cabal | F-presenting; cyborg | 34k, 2 anims. 'Oni' = Japanese myth, not IP. | https://sketchfab.com/3d-models/none-30c027e40cb14c7a9f98ba6c047acc1d |
| 53 | LowPoly Anime Character CyberStyle | DanlyVostok | CC-BY | 57927 | rigged + animated (animationCount=2) | anime, cyberstyle, overalls/jacket | trader, KOL | F; East-Asian anime | 57.9k: at limit, decimate heavily; 2 anims. HEAVY (>40k): decimate hard. | https://sketchfab.com/3d-models/none-9c284506f1e247348acd0de4151974d6 |
| 54 | Quaternius Ultimate Modular Men | Quaternius | CC0 | 11 models | rigged, humanoid-compatible per pack page | low poly, hardhat, suit, SWAT-style operator, punk mohawk, helmeted astronaut, cowboy, king, horse-head | VC, builder, cabal, anon, normie | Mixed M (11) | PACK. 11 distinct men, 4 swappable parts each, 24 anims. CC0 (Quaternius). Not on disk yet (only UBC + UAL are). | https://quaternius.com/packs/ultimatemodularcharacters.html |
| 55 | Quaternius Ultimate Modular Women | Quaternius | CC0 | 10 models | rigged, humanoid-compatible per pack page | low poly, wizard, hooded rogue, hardhat, punk, suit, dress | KOL, builder, normie, degen | Mixed F (10) | PACK. 10 women, humanoid-rig version included, CC0 (Quaternius). Not on disk yet. | https://quaternius.com/packs/ultimatemodularwomen.html |
| 56 | Quaternius Universal Base Characters (Standard) | Quaternius | CC0 | 6 models | rigged, humanoid-compatible per pack page | stylised semi-real base bodies, 20 hairstyles | any | Mixed M+F (6): superhero, regular, teen proportions | PACK. Already on disk as 'Standard' free tier (CC0). 'Teen' proportions variants must be excluded (adult-only rule). Source version is paid (Patreon/itch). | https://quaternius.com/packs/universalbasecharacters.html |

Rows 37 and 38 are single Sketchfab pages that each show a male and a female model (PS2 retro style). Rows 3 and 4 thumbnails show one model in two views. Row 54-56 are packs, not single models; their licence is stated on the Quaternius pack pages (CC0) and, for 56, in the on-disk `License_Standard.txt`.

### Balance of the set (Inference from thumbnails)
Of the 53 Sketchfab rows: about 26 female-presenting, 25 male-presenting (including masked/robot ones, which are counted by apparent body), 2 male+female pair pages; plus 3 packs. Ethnicity coverage: East-Asian/anime (3, 4, 25, 26, 39, 41, 44, 47, 48, 50, 53), South-Asian (12?, 30, 31, 32), Black/African (34, 35, 36, 37, 46), Latin-American/Amerindian (38), Arab/Middle-Eastern (33), light-skinned (many), plus the faceless set. The weakest cells are **Latino/Latina, Southeast Asian, and Black women in a cyber/streetwear style**. Plan to fill those with the Quaternius packs plus skin-tone tint, or the optional paid list.

## Top 15 for CT fit

| Rank | # | Name | Licence | Why |
|---|---|---|---|---|
| 1 | 1 | FREE Cyberpunk assassin girl (gas-mask) | CC-BY | Visor + techwear, 18k, animated |
| 2 | 2 | Tvman | CC-BY | TV-head anon; faceless, animated |
| 3 | 5 | Character Male (Sci-fi) | CC-BY | Yellow puffer + sneakers, best streetwear read (needs rig) |
| 4 | 4 | Cool Girl (Rotmill) | CC-BY | Long trench coat, 8.5k, animated |
| 5 | 3 | Cyber Girl (Rotmill) | CC-BY | Anime-adjacent cyber, animated |
| 6 | 6 | Elite Hazmat Operator | CC-BY | Fully masked anon/sniper |
| 7 | 7 | Shadow Operative | CC-BY | Hooded cape anon (needs rig) |
| 8 | 8 | Martian Space Pirate | CC-BY | Hooded + masked wanderer (35.8k, needs rig) |
| 9 | 11 | Cyberpunk female hacker | CC-BY | Hooded hacker (needs rig) |
| 10 | 19 | Cool Man | CC-BY | Coat + glasses, 6 anims, 16.7k |
| 11 | 25 | Tactical anime girl (DanlyVostok) | CC-BY | Anime-adjacent sniper, animated |
| 12 | 53 | LowPoly Anime Character CyberStyle | CC-BY | Anime cyberstyle; 57.9k so heavy decimation |
| 13 | 34 | African male (Maggadog) | CC-BY | Fills Black male cell; 47.9k, animated |
| 14 | 35 | Thandi (African woman) | CC-BY | Fills Black female cell; 19k |
| 15 | 54 | Quaternius Ultimate Modular Men (pack of 11) | CC0 | Only fully CC0 route; crowd + SWAT/punk/hardhat/suit |

Also strongly consider 56 (Universal Base Characters, already on disk, CC0) as the customisation baseline, and 55 (Modular Women).

## Rejected and why

- **NonCommercial / NoDerivatives / Standard / Editorial** licences: excluded from the table (several attractive cyberpunk models, e.g. "Full-body Cyberpunk Male/Female" series, are CC-BY-NC-SA).
- **Recognisable IP or likeness** (examples seen in search): Spartan Armour (Halo), Junkrat, TMNT, Miles Morales, Superman/Invisible Woman, Squall (FF8), Squid Game guard, Lesley (MLBB), "Wong", Ironman, real-person likenesses ("Savage 21", "Glad Valakas", "Yash KGF", "Myself as Meta-Human").
- **Minors / school-age / sexualised** models and chibi proportions (many anime results, Kenney Mini Characters, Quaternius Animated Character Pack): excluded under the adult-proportions rule.
- **Real-brand logos visible in thumbnails** (rows 51 sponsor decals; row 21 sweater graphic): flagged in notes, retexture before use.
- **CC-BY-SA** (e.g. Boxer 3D, Rigged Anime Male Character-1): share-alike: avoid unless needed.

## Other sources checked

| Source | Result | Use |
|---|---|---|
| Quaternius (CC0) | Ultimate Modular Men (11), Ultimate Modular Women (10), Universal Base Characters (6; Source version paid), Zombie Apocalypse Kit (4 playable), Toon Shooter Kit, Cyberpunk Game Kit (1 character + enemies), Animated Men/Women (4+4), Ultimate Animated Character Pack (50+, small chibi-ish; exclude for hero use) | Rows 54-56. On disk already: `quaternius/ubc`, `quaternius/ual`, `ual2`, `cyberpunk_game_kit`, `scifi_essentials_kit`. The two modular packs and the Animated Characters are NOT on disk. |
| KayKit (kaylousberg.itch.io) | Free character packs are fantasy (Adventurers, Skeletons). Mystery Monthly Series 4/5/6 are paid ($19.99 each, 14-15 low-poly rigged characters each; contents not verified) | Not CT-themed. On disk: bits packs + character animations only. |
| Kenney (CC0) | Animated Characters Protagonists/Retro/Survivors (8 files each; protagonists include a cyborg, a skater, a criminal), Mini Characters, Blocky Characters 2.0 | All chibi/blocky proportions; crowd filler at best. On disk: kits only, no characters. |
| Poly Pizza (CC0/CC-BY) | 437 search hits across 40 queries; only 40 flagged Animated, ~85 percent are Quaternius (same packs). Non-Quaternius animated: J-Toastie (Hyper Casual Character, Generic Female, Retail Worker, Rigged Astronaut), Zsky Stylized Character, KolosStudios Soldier, prathm Character_Man | Redundant with Quaternius; nothing added that beats the table. On disk: props only. |
| OpenGameArt CC0 | **Not searched** (time). `[Unverified]` whether it adds anything beyond the above. | Open item |

## Optional paid list (Sketchfab Standard licence; price from API `price` in cents; none are downloadable without purchase)

Standard-licence terms for shipping in an app bundle are `[Unverified]`; read https://sketchfab.com/licenses before buying. None of these were visually reviewed.

| Name | Author | Price | Faces | Anim | Notes | URL |
|---|---|---|---|---|---|---|
| [Game-Ready][Rigged] Stylized Alkaline | Cyril Margalin | $5.00 | 41,424 | 3 | Cyberpunk girl, rigged, cheapest | https://sketchfab.com/3d-models/none-02f4a8281fb348bf8892b17091111b62 |
| Cyberpunk - The Street Girl C | Slayerazazel | $7.50 | 30,082 | 0 | Streetwear-cyber | https://sketchfab.com/3d-models/none-6bd2e18d31f74fcb9df154b86f913363 |
| Sci-Fi Female Suit | amazingStanley | $14.99 | 25,134 | 4 | Rigged + animated | https://sketchfab.com/3d-models/none-76ac890b64ec40b085df6282743e5c03 |
| Cyperpunk girl | deluxor | $19.99 | 23,162 | 0 | Club/punk fashion | https://sketchfab.com/3d-models/none-58122312af96442687da1fbe62f6d1fe |
| Sci-Fi Pilot Suit [with head, fully rigged] | Isi.Izzy | $30.00 | 43,590 | 0 | Rigged per title | https://sketchfab.com/3d-models/none-9c131a2a4a544cab949f899a3dfa2caf |
| Secret Agent - Venus | ridho.mnf | $35.00 | 48,988 | 0 | Spy/agent | https://sketchfab.com/3d-models/none-3bcf38c6d6654f7a9bcfc64f07580b7a |
| Cybernetist Game Ready Rigged Character | Rumpelstiltskin | $69.90 | 11,710 | 0 | Low faces, rigged per title | https://sketchfab.com/3d-models/none-e172f7e6634644a199e0e85c8ff741e0 |

Skipped as Editorial (not usable): "Character Hero Hoodie (Rigged)", "Sci-fi girl - Rigged".

## Customisation architecture across distinct base models

All of this is `[Inference]` from standard Unity practice; not prototyped here.

1. **Shared animation via Unity Humanoid.** Import each base as Humanoid (Avatar generated per model, `Muscle & Settings` tweak where needed). One animation set (UAL/Mixamo retargeted) drives every character. Cost: each model needs an Avatar mapping check; non-standard rigs (robots with digitigrade legs, TV-head rigs, models with 0 animations) must be auto-rigged first. Reject models whose Avatar fails to configure.
2. **Per-model material regions for tint.** Pick one authoring contract and enforce it at import: either (a) separate submeshes/materials named `skin`, `hair`, `top`, `bottom`, `shoes`, `accessory`, or (b) one atlas plus an RGBA **region mask texture** (R=skin, G=hair, B=top, A=bottom; shoes via a second mask or submesh). A single shared URP shader multiplies the albedo by per-region tints through a `MaterialPropertyBlock` or per-instance material. Submeshes cost extra draw calls; the mask approach keeps 1 draw call per character on Seeker. The mask needs hand-painting per model (about 30 to 60 minutes each is a guess `[Speculation]`), so budget this as the real cost of each added model, not the download.
3. **Bone-attached accessories.** Anchor sockets are resolved from the Humanoid avatar (`Animator.GetBoneTransform` for Head, Neck, UpperChest, LeftShoulder, RightHand, etc.), not from bone names, so the same prefab attaches to any imported base. Store per-accessory offsets in a small socket table plus a per-model scale/offset override (head size varies a lot across anime vs realistic bases). Face accessories (masks, glasses) need a per-model face anchor offset; hooded/masked bases should flag `occludesFace` / `hidesHair` to hide conflicting accessories.
4. **Per-model outfit variants: optional.** Because every base already ships a distinct outfit, make outfit swaps accessories plus tint only. Add mesh variants (a second top/outerwear layer) only for the 5 to 8 hero bases; this keeps the cost linear.
5. **Hair.** Hair baked into these models cannot be swapped. Options: tint only; or strip hair and use the UBC hairstyles; or only allow hair swaps on bases that ship bald or hooded.
6. **Style unification.** Run all bases through the same toon/lit shader and an agreed texture budget. Keep two lanes at most (e.g. semi-real stylised plus anime-adjacent) and cull the others, otherwise the cast reads as a kitbash.

### Size impact per model (estimates, `[Inference]`; measure on the first imported pair)
- Hero at ~12k faces: about 6k vertices (a vertex ~32-40 bytes with skin weights) is roughly 0.2-0.3 MB of mesh, plus one 1024x1024 albedo and a 1024 mask in ASTC 6x6 at about 0.25-0.35 MB each, so roughly **0.7-1.0 MB per hero**, before compression of the bundle.
- Crowd at ~6k faces with 512 textures: roughly **0.2-0.3 MB**.
- 50 characters (15 hero + 35 crowd) is therefore about 20-25 MB, which is **far more than the 3 MiB of APK headroom** (APK is 147 of 150 MiB, your figure). Even 4 or 5 heroes would not fit.
- Therefore: ship at most the default character(s) in the APK (or none, with a tiny placeholder), and deliver the rest as **download-on-demand bundles** (e.g. Unity Addressables with a self-hosted/CDN catalog, one bundle per character plus a shared bundle for animations/shaders). Play Asset Delivery is an AAB mechanism; whether the Solana dApp Store accepts an AAB or only an APK is `[Unverified]` here, so do not assume PAD. Check the first-run UX: a download gate must not block the character picker, so thumbnails (a few KB each, shipped in-APK) show the cast and the bundle streams on selection.
- Textures dominate, not meshes: sharing one texture atlas across crowd NPCs (and reusing the mask layout) is the largest saving.

### Licensing housekeeping
- CC-BY needs visible credit: keep `credits.json` per model (title, author, URL, licence) and a credits screen; the JSON in this folder is the seed.
- Re-export transforms (decimation, retexturing) are allowed under CC-BY if credit is kept and changes are indicated `[Inference: indicate modifications]`.
- Re-verify each licence on the model page at download time (authors can change it) and save a screenshot to the evidence folder.
- Meshy/AI-generated sources (row 18) have extra terms; check before use.

## Next steps (needs your logged-in browser and approval)
1. Download the top ~15 plus packs 54-56, import as Humanoid, and record which actually pass Avatar configuration.
2. Re-run the `/v3/models/<uid>` verification (429 cleared) and flip `slug_verified` in the JSON.
3. Search OpenGameArt CC0 and fill the Latino, Southeast Asian and Black-woman gaps.
