READY TO COMMIT: art/cast/export/looks/{rain_breeze,npc_auditor,npc_chain_tribalist_b,npc_clip_farmer,npc_community_mod,npc_dubai_relocator,npc_engagement_farmer,npc_hackathon_grinder,npc_hiring_bait_farmer,npc_odds_crier,npc_prediction_shark,npc_privacy_maxi,npc_seeker_farmer,npc_side_event_hopper,npc_spaces_host,npc_tip_coin_pilgrim,npc_tradfi_suit,npc_wallet_tracker,player_f_anon,player_f_blazer,player_f_builder,player_f_coat,player_f_farmer,player_f_kol,player_f_lagos_ankara,player_f_lagos_kaftan,player_f_perp,player_f_shark,player_f_trencher,npc_sleuth,player_m_coat,player_m_lagos_kaftan}/ art/cast/export/parts/f/ art/cast/export/parts/m/{top_coat*,top_kaftan*,bottom_skirt*,bottom_wrap*,parts.json} art/cast/export/manifest.json art/cast/export/validation.json art/cast/previews/cast_lineup_f.jpg art/cast/previews/cast_lineup_m.jpg art/cast/scripts/{cast_common,cast_extract,cast_wardrobe,cast_validate}.py art/LICENSES.md docs/progress/cast.md docs/progress/requests.md — cast weight fix: Rain family rebuilt from the weight-corrected creator (no lower-body Head weights) + dress/coat/skirt hems weighted to the thighs (if the earlier fix-pass READY line was not committed yet, commit all of art/cast/export/ art/cast/previews/ art/cast/scripts/ instead)

# CAST lane — progress log

Owner: CAST agent (Blender 5.1.2 headless only; no Unity, adb or git). Intermediates (blends, maps,
logs, Poly Haven downloads): `/Users/mac/Documents/codes/projects/ct-life-evidence/cast/` (not in the repo,
so no `.gitignore` change is needed). Export size: 190 MB in 1,133 files, all under 10 MB (plain git; LFS blocked).

## Status (2026-10-08): stable, awaiting the user's look approval

**Budgets (fresh-FBX validation, `art/cast/export/validation.json`: 389 files, 0 failures, incl. a new check: no Head/Jaw/Eye weight below 1.1 m).**
Every look: one mesh per tier, one material, one atlas (1024² hero looks, 512² NPC looks), ≤4 weights
(≤2 at LOD2), both 55-bone skeletons identical to the shipped Snow/Rain creator FBX (max rest-matrix
delta 0.0). Worst offenders: hero 11,988 (`player_f_shark`), LOD0 5,988 / LOD1 2,836 / LOD2 1,093
(`npc_auditor`), all under 12k / 6k / 3k / 1.2k (unchanged by the fix pass; highest changed look: `player_f_kol` hero 11,987, LOD0 5,890).

| Identity look | Original | Hero | LOD0 | LOD1 | LOD2 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Snow / Everyday | 43,478 | 10,794 | 4,996 | 2,386 | 936 |
| Rain / Breeze | 76,974 | 11,396 | 5,297 | 2,540 | 999 |

**Cast.** 75 looks in `art/cast/export/manifest.json`: 2 identity, 51 NPC, 22 player combinations
(11 per family, incl. Lagos senator/kaftan + fila, Ankara + gele, Ankara kaftan). All 28 contract archetype
ids are bound; 22 further ids (Laser-Eyes Maxi, Chain Tribalist pair, 19 dossier archetypes) are labelled
`proposed id (not in contract)`. Wardrobe: 55 parts per family (tops, vest, bottoms, boots, hair, beards,
hats, face/neck/back/hand accessories incl. every requested CT signature item) exported as runtime parts
(`art/cast/export/parts/{m,f}/parts.json`, 70 + 75 FBX chunks with all tiers).

**Contact sheets:** `art/cast/previews/cast_lineup_m.jpg`, `cast_lineup_f.jpg` (full body + head, every
look), `cast_lod_comparison.jpg` (original creator mesh vs hero/LOD0/LOD1/LOD2 for Snow and Rain).

**Integration:** Unity import steps are in `docs/progress/requests.md` (2026-10-08 cast -> integration lead).

## Weight fix (character-feel lane report, 2026-10-08)
- **Cause of the Head weights on Rain-family shoes/ankles/hips:** the cast pipeline extracted from the
  historical `rain-adult-production/RainAdult_Unity.blend`, whose creator mesh still carries the old
  stale `DEF-COR-*` groups mapped to Head (the defect documented in
  `art/character-candidates/rain-weight-correction/README.md`). Every garment/legs/skirt part derived
  from it inherited them. **Fix at the source:** `cast_common.FAMILIES['f']` now reads
  `rain-weight-correction/RainAdult_WeightsCorrected.blend` (same geometry/UVs/rig, corrected weights);
  `cast_extract.py` asserts 0 source vertices below 0.62×height with Head/Jaw/Eye weight; `cast_validate.py`
  fails any exported mesh with Head/Jaw/Eye weight below 1.1 m. The whole Rain family (29 looks, all f
  parts) was rebuilt; Snow-family meshes were already clean (same check).
- **Dress / long-coat hems** (`top_coat`, `top_kaftan`, `bottom_skirt`, `bottom_wrap`, both families):
  new `dress_weights()` blends Hips → Thigh.L/R with depth below the hip (soft 50/50 split at the centre
  line, ~20% Hips left at the hem), plus Calf below the knee for the ankle-length wrapper. Measured at
  LOD0 between 0.45-0.70 m: max Hips weight 0.20-0.28 (wrapper 0.56), 0 pelvis-only vertices, mean leg
  weight 0.82-0.96 (`ct-life-evidence/cast/hemcheck.json`). Re-exported: all 29 Rain looks +
  npc_sleuth, player_m_coat, player_m_lagos_kaftan; parts f/* and m/{top_coat,top_kaftan,bottom_skirt,bottom_wrap}.
- Triangle counts unchanged for the Rain looks (same geometry); m coat/kaftan looks now: npc_sleuth LOD0
  5,906, player_m_coat hero 11,987 / LOD0 5,867, player_m_lagos_kaftan hero 10,656 / LOD0 5,154 (all within caps).
- The feel lane's `CastWeightRepair` should now be a no-op on this export (no lower-body Head weights,
  hems already thigh-weighted). Not verified in Unity by this lane.

## Fix pass (coordinator review, 2026-10-08) — 15 looks rebuilt, nothing else touched
- **Afro (both families):** rebuilt as the scalp offset by a thickness field: 0 at a forehead/temple/
  nape hairline, rounded thicker crown, 110 clump bumps + coil relief, coil-pattern normal/AO texture
  in the hair slot; scalp shell carries the hairline. Reads as textured hair from the 3/4 camera; the
  silhouette is closer to a high-top than a round afro and a small ridge remains at the front hairline.
- **Gele:** pleated wrap rising to the crown (clears the skull), two accordion-pleated fan crests,
  knot + two tails at the back, Ankara-style print on all of it (original motif).
- **Skirts/wrapper:** 40-segment profile with flare, irregular drape folds, hem wave and a 2 cm
  turned-back hem (thickness); pushed outside the trousers.
- **Female hood (anon):** fabric hood with roomy crown, centre seam, oval opening framing the face with a
  rolled rim, folds on back/sides, drape onto the shoulders; inner layer 6 mm inside (no lining poke-through).
- **Rain hems (blazer/suit/coat/kaftan):** solidify hem-rim faces removed before the hem extension,
  consistent outward winding (no back-face holes), hem pushed 12-14 mm outside the jeans, and a
  `hip` band on trousers hidden under hem-extended tops (`<bottom>_hip` chunk for runtime). The red
  coat's dark band in the sheet is its intended belt.

| Look | Hero before→after | LOD0 | LOD1 | LOD2 |
| --- | --- | --- | --- | --- |
| npc_engagement_farmer | — | 5,428→5,599 | 2,540→2,626 | 989→1,015 |
| npc_hackathon_grinder | — | 5,084→5,134 | 2,353→2,384 | 900→910 |
| npc_spaces_host | — | 5,440→5,611 | 2,509→2,595 | 949→975 |
| player_f_lagos_kaftan | 11,071→11,040 | 5,134→5,074 | 2,365→2,335 | 897→875 |
| player_f_perp | 11,231→11,432 | 5,382→5,432 | 2,518→2,549 | 988→998 |
| player_f_lagos_ankara | 10,156→11,247 | 5,187→5,326 | 2,450→2,489 | 963→970 |
| player_f_anon | 10,704→10,706 | 4,965→4,966 | 2,306→2,307 | 884→887 |
| player_f_coat | 11,575→11,342 | 5,333→5,221 | 2,456→2,393 | 935→905 |
| player_f_blazer | 11,469→11,237 | 5,392→5,281 | 2,514→2,455 | 966→934 |
| npc_tradfi_suit | — | 5,296→5,185 | 2,434→2,375 | 936→904 |
| npc_hiring_bait_farmer | — | 5,492→5,381 | 2,564→2,505 | 986→954 |
| player_f_kol | 11,633→11,987 | 5,769→5,890 | 2,690→2,745 | 1,057→1,073 |
| npc_cto_lead | — | 5,036→5,136 | 2,325→2,405 | 863→924 |
| npc_altseason_prophet | — | 5,050→5,150 | 2,370→2,450 | 901→962 |
| player_m_builder | 11,088→11,205 | 5,310→5,410 | 2,469→2,549 | 935→996 |

Partial validation (59 changed FBX) passed and is merged into `validation.json`; the manifest was
regenerated; both lineup sheets re-rendered. Close-up check (evidence only):
`ct-life-evidence/cast/fixpass_closeups.png`.

## Honest quality notes
- Faces/identity unchanged at hero and LOD0; LOD1 is soft; LOD2 (≤1.2k for the whole avatar) has smeared
  eyes and blocky ears — distance-only. Fingers survive at hero/LOD0 only.
- Only two faces exist (Snow, Rain). Variety comes from skin tone, hair, beards, glasses/hats, mild
  face/body morphs and outfits; at close range the cast still reads as two head shapes.
- Garment shells are offsets of the authored tee/tank/jeans; folds are authored on original regions and
  procedural (noise + AO) on new regions (sleeves, hems, hoods). Shirts/jackets/coats are texture-painted
  (plackets, lapels, ties), not modelled. Coat skirts follow the thighs (they will stretch when running).
- Props are simple procedural shapes except 7 Poly Haven CC0 models; crystal ball, gimbal, second phone
  and multi-lanyards from the dossier are not modelled.
- Unverified: Unity import, Humanoid retarget on these meshes, ASTC appearance, device frame time.
  Renders are Blender Eevee previews, not in-game evidence.

## Reproduce
`art/cast/scripts/build_all.sh` (extract → wardrobe → LODs → looks per family; ~45 min) then
`art/cast/scripts/rebuild_from_lods.sh` (LODs → looks → parts → validation → manifest → sheets).
Single stages: `art/cast/scripts/run.sh <script.py> <args>`.

## Research notes (sources verified 2026-10-08)
WebSearch budget for the session was exhausted when this lane started, so only direct page fetches
(WebFetch) and the Poly Haven public API were used. Findings that shaped the wardrobe:

| Finding | Source | Used for |
| --- | --- | --- |
| Pink Wojak: pink-tinted Wojak for crypto losses; first documented on /biz/ 15 June 2017. | https://knowyourmeme.com/memes/pink-wojak | Rekt Guy: pink-flushed skin, torn pink shirt |
| Pump.fun livestreams launched 2024, suspended 26 Nov 2024, relaunched 11 Apr 2025 with moderation. | https://en.wikipedia.org/wiki/Pump.fun | Streamer / Engagement Farmer ring light, Rug Dev burner phone + hoodie |
| Fleece vests became the finance/VC status uniform; Patagonia restricted co-branded vests (2019) and stopped adding corporate logos (2021). | https://en.wikipedia.org/wiki/Patagonia,_Inc. | VC fleece vest over button-down (parody logo only) |
| Agbada = flowing robe + inner shirt + shokoto trousers, often with fila / abeti-aja cap; aso-oke, damask, lace; embroidered neckline. | https://en.wikipedia.org/wiki/Agbada | Nigerian CT looks: kaftan/agbada-style tunic, fila cap, neckline embroidery |
| African wax prints (Ankara): bold geometric/organic motifs; specific manufacturer designs (Vlisco etc.) are protected. | https://en.wikipedia.org/wiki/African_wax_prints | Original procedural Ankara-style prints (no copied manufacturer designs) |
| Gele: voluminous Yoruba head tie, worn casually and formally, fabric matched to the outfit. | https://en.wikipedia.org/wiki/Gele_(head_tie) | Female Lagos CT look with gele |
| Solana Breakpoint 2026: 15–17 Nov 2026, Olympia London. Page has no attire/merch info. | https://solana.com/breakpoint | Conference lanyards/badges as a CT signal (no Solana logo used) |
| Existing CT X notes (2026-10-04): giveaway template, reply-guy farms, callers, wallet-tracker posts, "JUST IN" bots, Nigerian engagement prompts. | `docs/research/ct-x-observations-2026-10-04.md` | Text prints/props: GM, LFG, WAGMI, 100X, JUST IN, SER |

`[Unverified]` claims about current (2026) CT conference fashion beyond the sources above: no
first-hand 2026 photo source was available in this session.

**Dossier folded in (2026-10-08):** `docs/research/ct-now-2026-10.md/.json` (research lane). Used its
`archetypes` (28 spec NPCs updated + 21 new) and `fashion` sections. Its fashion rows are mostly marked
`inference` by that lane, so they are treated as direction, not fact. Concrete changes: Town Crier
press badge; Giveaway Guy fresh cap; Alpha Caller hoodie + anime mask + shades; Rug Dev = livestream dev
(hoodie, burner, ring-light/webcam rig); Perp Degen compression tee + headset; Laser-Eyes Maxi suit with
orange tie; Auditor hi-vis vest + lanyard; VC fleece over oxford + lanyard; Spaces Host hoodie +
headphones; Normie new phone; founder lanyard; trencher headset; NG flex = senator/kaftan + white
sneakers + car key; Ankara shirts; .skr name tees; 19 of the 21 new archetypes as extra NPC looks
(livestream dev merged into Rug Dev; crystal ball / gimbal / second phone not modelled). Parody names
from the dossier on prints: COIN2050, CHECKPOINT, POMP, WOTCHA.

Parody rule: no real logos, brands, NFT PFPs or real people's likeness. All prints are original text
or abstract marks.

## Log
- 2026-10-08: read brief/spec/QA/handoff/ledgers; inspected compiled Snow (43,478 tris) and Rain (76,974;
  11.5k tris of mouth interior removed). Neither base has skin under clothes, so garments derive from the
  authored garment/skin surfaces. Built extract → wardrobe → tiered LODs → looks → parts → validation.
- 2026-10-08: weight fix — Rain family rebuilt from the weight-corrected creator; dress/coat hems thigh-weighted; validator checks lower-body Head weights (389/389 pass).
- 2026-10-08: folded in the CT-now dossier; fixed Rain inner tank shell, hair far-tier hulls, crotch leg
  tubes, inward-wound hems/boot shafts/hats (would be back-face culled in Unity), jeans poke-through
  (waist chunk hidden under tops + hip push-out). Final validation 375/375 FBX pass.
