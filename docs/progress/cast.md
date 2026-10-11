READY TO COMMIT: art/cast/scripts/ art/cast/export/ art/cast/previews/ art/LICENSES.md docs/progress/cast.md docs/progress/requests.md — CT cast: Snow/Rain performance tiers, modular wardrobe, 75 looks (28/28 contract archetypes), manifest + contact sheets

# CAST lane — progress log

Owner: CAST agent (Blender 5.1.2 headless only; no Unity, adb or git). Intermediates (blends, maps,
logs, Poly Haven downloads): `/Users/mac/Documents/codes/projects/ct-life-evidence/cast/` (not in the repo,
so no `.gitignore` change is needed). Export size: 182 MB in 1,119 files, all under 10 MB (plain git; LFS blocked).

## Status (2026-10-08): stable, awaiting the user's look approval

**Budgets (fresh-FBX validation, `art/cast/export/validation.json`: 375 files, 0 failures).**
Every look: one mesh per tier, one material, one atlas (1024² hero looks, 512² NPC looks), ≤4 weights
(≤2 at LOD2), both 55-bone skeletons identical to the shipped Snow/Rain creator FBX (max rest-matrix
delta 0.0). Worst offenders: hero 11,988 (`player_f_shark`), LOD0 5,988 / LOD1 2,836 / LOD2 1,093
(`npc_auditor`), all under 12k / 6k / 3k / 1.2k.

| Identity look | Original | Hero | LOD0 | LOD1 | LOD2 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Snow / Everyday | 43,478 | 10,794 | 4,996 | 2,386 | 936 |
| Rain / Breeze | 76,974 | 11,396 | 5,297 | 2,540 | 999 |

**Cast.** 75 looks in `art/cast/export/manifest.json`: 2 identity, 51 NPC, 22 player combinations
(11 per family, incl. Lagos senator/kaftan + fila, Ankara + gele, Ankara kaftan). All 28 contract archetype
ids are bound; 22 further ids (Laser-Eyes Maxi, Chain Tribalist pair, 19 dossier archetypes) are labelled
`proposed id (not in contract)`. Wardrobe: 55 parts per family (tops, vest, bottoms, boots, hair, beards,
hats, face/neck/back/hand accessories incl. every requested CT signature item) exported as runtime parts
(`art/cast/export/parts/{m,f}/parts.json`, 68 + 70 FBX chunks with all tiers).

**Contact sheets:** `art/cast/previews/cast_lineup_m.jpg`, `cast_lineup_f.jpg` (full body + head, every
look), `cast_lod_comparison.jpg` (original creator mesh vs hero/LOD0/LOD1/LOD2 for Snow and Rain).

**Integration:** Unity import steps are in `docs/progress/requests.md` (2026-10-08 cast -> integration lead).

## Honest quality notes
- Faces/identity unchanged at hero and LOD0; LOD1 is soft; LOD2 (≤1.2k for the whole avatar) has smeared
  eyes and blocky ears — distance-only. Fingers survive at hero/LOD0 only.
- Only two faces exist (Snow, Rain). Variety comes from skin tone, hair, beards, glasses/hats, mild
  face/body morphs and outfits; at close range the cast still reads as two head shapes.
- Garment shells are offsets of the authored tee/tank/jeans; folds are authored on original regions and
  procedural (noise + AO) on new regions (sleeves, hems, hoods). Shirts/jackets/coats are texture-painted
  (plackets, lapels, ties), not modelled. Coat skirts follow the thighs (they will stretch when running).
- Two small jeans poke-through specks remain at the hem of Rain-family blazers/suits (Blender close-up).
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
- 2026-10-08: folded in the CT-now dossier; fixed Rain inner tank shell, hair far-tier hulls, crotch leg
  tubes, inward-wound hems/boot shafts/hats (would be back-face culled in Unity), jeans poke-through
  (waist chunk hidden under tops + hip push-out). Final validation 375/375 FBX pass.
