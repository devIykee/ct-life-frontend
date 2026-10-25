# CT Life v3 backlog (coordinator's working list)

Read this on every wake-up. Pick the top unblocked item, run it, then update this file. Branch: `feat/v3-studio-world` in
`/Users/mac/Documents/codes/projects/ct-life-repair`. Started 2026-10-21. The user wants the full game built to their
taste and landed as PRs, with work continuing without prompts.

## RESUME HERE (RESUMED 2026-10-25: both phones plugged in; lanes A + B + backend agent running; hourly cron 03109268 at :17)
- 2026-10-25: localhost is fast (nc/java 0.02-0.03 s). LuLu 4.5.1 IS installed and active as a network extension, but it's not stalling now, so the warm.jar is not needed. The user sent Iyke the Firebase service-account key (done). Tip fee: explained to the user, decision still pending.
- Backend: #15 MERGED 2026-10-24 04:25Z. Breakpoint follow-ups are PR #18 (feat/breakpoint-followups, migration 093, carries the crash fix c55f22e); the #15 comment is posted. The contract was forwarded to lane B to wire (trivia retry UI, restore, attendee 'Say hi', Wear lanyard).
- Lane B DONE with PR #18 wiring (6aafa40f, feat fast-forwarded to 15f1408e; 171/171 CityLife tests; Infinix evidence in breakpoint-r2-20261024/pr18/). Lane B's NEXT job: the Infinix low-end preset. The base game is 6-15 fps on the Infinix even without the event, so profile, then an auto Low tier (target ≥24 street/plaza, ≥20 driving), the Seeker walk fps, and plaza OUT_OF_BOUNDS (districts.json land). Backend: the idempotency key stays IN_FLIGHT after a 400 (409 on retry); the fix goes to #18.
- Lane A: casino online walk-in + Infinix casino panel fps (uncommitted edits in ct-life-repair). Not pushed. 13:17 tick: lane A hung 74 min on an ffmpeg tile over a 120 MB recording, so it was stopped and resumed with a timeout rule. #18 got the idempotency fix (a failed request releases the key). Lane B is on the Low tier (holds the Infinix).
- **Lane A (casino):** device run done: the Seeker walk-in has no fall-out, 30 fps on the floor; the 2-device H2H works; pier outline commit 16695c2b. NOT pushed to either repo yet. Open issues:
  - (1) Online, the casino route teleport is corrected back downtown after ~25 s (the street-room walking rule vs the teleport); verify a real walk-in from the boardwalk; probably fine for normal play.
  - (2) The crash client overshoots the server crash (1.24× shown vs 1.13× server) until backend c55f22e lands (#15).
  - (3) The casino UI runs ~15 fps on the Infinix (full-screen backdrop cost).
  - Gradle needs `JAVA_TOOL_OPTIONS=-javaagent:<scratchpad>/warm/warm.jar` while localhost stalls (~10-14 s first connect per process; suspected LuLu).
  - ~400 MB of rr_*.mp4 files are left on the Seeker /sdcard (delete them).
  - Evidence: ct-life-evidence/casino-r3-20261024/{seeker,infinix,editor}.
- **Lane B (Breakpoint, branch lane/b-breakpoint, 7 commits 3ac65215→7d7f5891, 159/159 CityLife tests):** Seeker-verified (purple BP26 plaza, booths, nod, trivia, claim + lanyard, IRL refuse/check-in/board, countdown; plaza fps 30.2/min 30.1). Left: the Infinix run and a walk fps re-measure without screenrecord. Backend asks for #15 (the #11 part): trivia stuck after 2 wrong answers; GET /events/{slug} should return done booths/nods + the caller's IRL state; the admin PUT should accept the geofence; IRL attendees need an OPAQUE handle (not the raw user id) for messaging; rewardCents 20000 = $200 (check the intended amount); no PATCH /characters/me/look, so the lanyard can't be worn (only shown in the wardrobe). See ct-life-evidence/breakpoint-r2-20261024/PAUSED.md. NOT merged into feat/v3-studio-world yet. It was re-themed to the real BP26 look (black/purple).
- **Backend PRs for Iyke:** #15 (combined #9-#14, head c55f22e), #16 careers + Rekt Burger, #17 heat/police. A pending comment for #15 is noted below (the GitHub API was failing).
- **To push when verified:** the casino (lane A) and Breakpoint (lane B, after merging into feat/v3-studio-world) to origin PR #1 and a new iyke-fe sync PR (#5 merged).
- **Env:** Docker was force-restarted 06:40 UTC (healthy). Local PR backends: :4011 r1 (~/.ctlife-local/r1-backend), :4012 r2 (lane B), :4013 r3 (casino); the start scripts are in the scratchpad (r1-up.sh, r3-up.sh, pr-up.sh). Firebase project ct-life; client/google-services.json is local only.

## Parallel lanes (user, 2026-10-24: run in parallel, finish faster, don't crash the Mac; M2 Pro, 32 GB)
- Unity lanes: max 2-3 at once, each in its own worktree + branch with its own Library:
  - lane A = `ct-life-repair` (`feat/v3-studio-world`, the integration branch);
  - lane B = `../ct-life-lane-b` (branch `lane/b-*`).
  The coordinator merges finished lane branches into `feat/v3-studio-world`, then pushes both repos. Give lanes disjoint areas; island rebuilds only on lane A.
- Phones: Seeker `SM02G40619141343` (the look/perf reference) and Infinix X6517 `097784036T001536` (low-end, 4 GB, 720x1612, Android 12: "does it run" and UI checks only).
  Agents MUST use `tools/integration/device_lock.sh acquire <owner> seeker|infinix|any` (re-run to refresh, 45 min TTL), then `release <owner>`, `adb -s <serial>` always, and `SEEKER=<serial> tools/integration/seeker_launch.sh`.
- Migration 091 = #16 careers, 092 = #17 heat/police. Migration numbers taken (open PRs): 084 + 088 #9 (streak/quests/push + push-token unique), 085 + 090 #11 (calendar + IRL check-in), 086 #12 casino, 087 #13 season/nods, 089 #14 Rugpull Factory. 093 = Breakpoint follow-ups (trivia retry; backend PR feat/breakpoint-followups; 094 reserved for the same agent, unused so far). The next free one after 092 is 095.
- Backend lanes: one worktree per PR (`../ct-life-backend-pr-<k>`), each with its OWN test DB (`ctlife_pr_test_<k>`).

## How we work (user rules)
- **Look:** clean semi-real, studio quality. Use real ready-made models (Sketchfab, Poly Haven, ambientCG); never generated
  stand-ins or synthesized audio. Use fictional, unbranded vehicles. Vet provenance (no AI meshes, rips or NC licences).
- **One Unity agent at a time**, cost-conscious; do small jobs as coordinator. Verify on the Seeker
  (`SM02G40619141343`): screenshots for looks, screen recordings for motion (never judge motion from stills). Don't
  over-test.
- **Push every frontend change to both repos:**
  - `origin` (`Immadominion/ct-life`): PR #1 `feat/v3-studio-world` → `main`.
  - `iyke-fe` (`devIykee/ct-life-frontend`): a sync commit on top of `sync/v3-studio-world` → PR into `dev` (#3, #4, #5 merged; WE merge iyke-fe PRs into `dev` ourselves (no deploy), user OK 2026-10-24; open the next PR from the same branch after each merge). Use
    `git commit-tree HEAD^{tree} -p <remote sync sha>` and push sha:refs (zsh: `"${C}:refs/..."`).
- **Backend fixes** go as PRs to `devIykee/ct-life-backend` from `projects/ct-life-backend-pr` (its `.env` points at
  the throwaway DB `ctlife_pr_test`). Never edit `projects/ct-life-backend`.
- **Sketchfab:** my '8 per ~7 h window' was an UNVERIFIED inference and is wrong or incomplete: the user downloaded 10+ by hand on 2026-10-24 ~01:00 UTC. The likely cause was burst-rate limiting from my scripted calls ~2.5 s apart. Space scripted downloads (≥30-60 s apart) or ask the user to download by hand. Use the logged-in Chrome, `fetch('/i/models/<uid>/download')`
  plus the anchor-download JSON trick, then curl into `ct-life/art/raw/sketchfab/<uid>/<uid>.glb`. The last window
  opened 2026-10-23 21:20 UTC (7 OK, then 429 on the 8th; lilac 10312697 still to download). Next window ≈04:20 UTC 2026-10-24.
  Curl recipe: quote `--proto '=https'` (zsh expands bare `=https`). Only delete `~/Downloads/sfdl_*.json` after the .glb
  has size > 0. Chrome saves at most 7 anchor downloads per page, so do 7 per page or reload the page between.
- **Money rules:** only scarce items become Metaplex Core NFTs (hovercar pass, drops, art, later deeds); Cash never
  buys anything tradable; homes are game-only for now.

## Done (v3)
R1 retention FE (2026-10-24): gm streak tile, 3D daily chest (coins into the Cash pill, day 7/14/30 gold), Quests app with deep links and live toasts, push (firebase-messaging plugin, disabled until the user adds client/google-services.json), sign-out token rotation + an always-revoke sign-out. Verified against backend #9 locally (~/.ctlife-local/r1-backend :4011).
Perf 2 (2026-10-23): no more spawn and UI hitches (async vehicle/van/courier loads, font atlas prewarm), merged car meshes (-120 draws), merged far terrain (105→27 draws); eye-level draws 728→554, capped minimum 30.1 everywhere. Left: island instance renderer draws (225), the first-flat shader compile, the car-controls UI rebuild, fly80 GPU +1.1–1.6 ms.
Homes + Duo Shop FE (2026-10-23): flat doors gated by access, locked door with Knock, a 4403 shown-out toast, the Duo Shop app (catalogue, 3D product, bag, checkout, tracking), a courier van plus a parcel at the door, place mode, visitors seeing placements, live entitlements; WireV3Kinds via the `/v1/config` flag. Verified against PR #8 running locally (:4011).
District fix-ups 2 (2026-10-23): a forested mountain up to the tree line, real architecture for 16 venue shells, real power poles. Uncapped GPU is +1–2 ms on the drive/fly80 legs (watch it).
District fix-ups 2 (2026-10-23): the mountain forested to a tree line (~1500 conifers, alpine shrubs, banded mossy cliffs), real architecture on all 16 venue shells and 4 landmark buildings (merged outside renderer + far LOD, no MPB at rest), pole shafts under the power-line hardware, route shots aimed. Evidence `ct-life-evidence/world-fixups2-20261023/`.
District fix-ups (2026-10-23): mountain terrain without stripes (triplanar cliffs, scree, alpine, snow), closed boulders, an intact islet with a path, real venue architecture for CHECKPOINT 26, Perp Pit, Basecamp Lodge and the beach hut, the Trenches walled, 54 power poles with wires.
District fix-ups (2026-10-23): mountain on scanned tri-planar rock + scree with slope-aware weights (the sine strata stripes removed), boulders by cliff bands / ridges / cliff feet, islet shards fixed (island-tree wood) + a footpath, real architecture for CHECKPOINT 26 / Perp Pit / Basecamp Lodge / the beach hut (world_venue_shells.py), Trenches lot halls, power poles with wires, last toy road lamps. Evidence `ct-life-evidence/world-fixups-20261023/`.
Forests + districts 2 (2026-10-23): scanned Poly Haven forest trees island-wide, Builder Hills/Airdrop Farm/Boardwalk/Breakpoint/Basecamp/islet dressed, toy rocks/crops/palms/poles removed, sofa sit fixed.
World polish (2026-10-23): seated three-quarter camera, semi-real terrain and ground cover, Trenches yards and compounds.
Vehicle fleet with 4 LODs; hovercar gating; downtown, Trenches, Residences and Marina on the v3 kit; mocap locomotion;
GTA-style HUD; collisions; the shadow fix; the perf pass (30 fps cap holds); the walk-in apartment. Backend PR #6
(vehicle kinds, entitlements).

## Queue (top = next)
**PRODUCT PRIORITY (user, 2026-10-23):** a finished, usable product fast; it's a crypto game, so retention loops matter as much as looks.
Plan: ct-life-evidence/design-retention-20261023/retention-plan.md (24 items, cited). Decisions taken (coordinator, from the
plan's recommendations): a first Jupiter swap earns a cosmetic badge only; casino RTP ~95% with a daily stake cap, simulated before
shipping; Breakpoint first (15-17 Nov 2026, Olympia London); Solana Foundation outreach (stream embed, Luma tickets) is the
user's call and runs in parallel, never blocking.
**Retention top 5 (after perf-2; each = FE (us, Unity) + a backend PR to Iyke; acceptance in plan §7, verified by recording):**
R1. [DONE FE; backend #9 open] gm streak + daily chest + 3 daily quests/weekly chain + real push (FCM; needs a Firebase project the USER must create:
    google-services.json + service account; build it behind config).
R2. [backend PR #11 open; FE running on lane B. USER DECIDED 2026-10-24: an opt-in IRL check-in (one geofence check at Olympia during 15-17 Nov; store only an 'at venue' boolean + time; an 'IRL here' board; wave/DM to meet; IRL badge + gold lanyard + a one-time Cash bonus (modest, account must predate the event, mock-location rejected); no QR). Backend → PR #11; FE → lane B] Event calendar engine + Breakpoint week. FE: skin `london_2026` on `conference_hall` on `event_live` / `GET /events/current`, removed on `event_ended`; countdown from startsAt/endsAt plus serverNow; a 'Watch the official stream' button opening `streamUrl` (hidden while null; ops set it via the admin PUT); quest UI from `GET /events/{slug}` quest.steps; proof ids booths `bp26.booth_1..6` (3), nods `bp26.nod_1..8` (5), trivia `bp26.q_city|q_venue|q_month|q_days` with answer a-d (3 right); show `lanyard_breakpoint_2026` from `/inventory`. Known v1 gap: booth/nod proofs are replayable static ids (fine for Cash plus a cosmetic; venue-signed tokens later). Old note: at the Conference Center (London skin, countdown, official-stream link-out, no embed,
    3-quest chain, off-chain lanyard). DEADLINE: live before 15 Nov 2026.
R3. [FE BUILT on lane A, commits a96836de..8dde9461 NOT pushed: Art-Deco pier casino + walk-in floor, slots/crash/coinflip/H2H/fairness/limits/gates, CT symbol art (IP removed). PENDING device verification (blocked by localhost slowness 2026-10-24 ~05:40 UTC, suspected LuLu): build APK with the fall-out fix 8dde9461 + camera fix 10739460; Seeker exterior/walk-in/crash/coinflip/2-device H2H; floor fps on both phones. Backend asks: reap crash rounds on read (sent to #15); the casino pier is outside the island outline in contract/ids/districts.json (OUT_OF_BOUNDS for others): FE must re-export districts.json (tools/scripts/import-districts.ts) to include the pier. Infinix: floor 17.7 fps, casino UI 9–10 fps (slow but runs).] ART READY in art/processed/world_v3/v3_casino (cs_slot_machine, cs_roulette_table, cs_poker_table, cs_blackjack_table) BUT: the slot cabinet art includes 'Jurassic Park' (film IP) and a 'Vegas Slot' skin, so retexture the cabinet glass and screen with our own CT symbols; table scales are inconsistent (one huge, others tiny), so normalise to real sizes (slot ~1.8 m tall, tables ~0.75 m high)] FE contract: `GET /casino/state` on entry (ageConfirmed gate, selfExcludedUntil lock, today.stakeRemainingCents/lossRemainingCents, sessionReminder popup); the `disclaimer` on every screen; slots animate `outcome.reels` + multiplier; crash animates from params.startedAtMs + growthPerSecond, trusting the cashout reply (`outcome.crashed`); coinflip `{stakeCents, side}` and H2H challenges polled (410 expired); a fairness screen (seedHash before, seed after; docs/CASINO.md); a self-exclude picker 1–365 days. Not built yet: blackjack/roulette, the Marina high-roller room, the jackpot cosmetic, casino_win announcements. "The Rug Room" casino on the Boardwalk pier (Cash only, 18+, server RNG): slots, crash, coinflip first; then blackjack and
    roulette; Marina high-roller room. Vet Sketchfab/Poly Haven casino interiors.
R4. [backend PR #13 open; FE next. FE must replace the placeholder nod positions in `contract/ids/nods.json` with the real placed positions; season 1 dates/XP/rewards are placeholders. FE contract: GET /v1/season (tracks free|paid, status locked|claimable|claimed|pass_required); POST /nods/{id}/collect {proof:{nodId,x,z}} needs a server street position <30 s old and within 15 m (409 NOD_TOO_FAR/POSITION_UNKNOWN, 429); GET /v1/nods (book, no positions); /nods/leaderboard] Season pass tracks (free + paid, 10+ tiers) + the community-nods collection book (80 nods already designed).
R5. [backend PR #14 open; FE next. FE contract: /factory/config (12 art ids rf_*, fee 500c, liquidity 1k-100k, dev alloc ≤5000 bps); /factory/me (3/day, 10 min cooldown, heat 0-100 + heatLevel); POST /factory/coins (NAME_NOT_ALLOWED/TICKER_TAKEN/LAUNCH_COOLDOWN/LAUNCH_LIMIT_REACHED); Pulse CoinState.playerLaunched+templateId; /feed posts kind player|sleuth with meta.deepLink venue:launchpad_tower; trades via POST /market/trades] Coin-launch sandbox "Rugpull Factory" at Launchpad Tower (Cash only, hidden-arc engine exists) + the Sleuth exposé on the Timeline.
Later from the plan (see also 17-43 below): the Timeline as world feed, tribe war flags, Ratio Arena bets, drainer-DM mini-events, nameplates,
Jupiter swap sheet, the Seeker perks pack, the wallet gallery, Solana Pay merch, Blinks.
**Making it make sense (added 2026-10-24, recommended order after R3: 17 → 18 → 19 → 22 → 36 → 40):**
17 guided first session (origin → character → home → first shift → first trade → first quest/chest) · 18 always-visible dream ladder
(basement → studio → flat → penthouse → hovercar) · 19 [backend PR #16 open, after #15; FE next: careers screen from GET /v1/careers?view=ladders, career_promoted celebration (cosmetics reward_career_*), the Rekt Burger counter in the_trenches venue (propId rekt_burger_counter), a minigame sending per-ticket {i,startedMs,servedMs,correct}, uniform reward_rekt_burger_uniform] career ladders per archetype (+ USER IDEA: blow your port → fallback parody fast-food job 'Rekt Burger', no McDonald's IP; mostly Trenchers; a guaranteed Cash faucet with a way back out) (Builder/Trader/Caller/Farmer/Founder, ranks unlock
places/perks) · 20 clout/reputation stat · 21 nameplates/rank titles/.skr tag · 22 group chats and alpha groups in the Duo · 23 crews/DAOs with a
clubhouse, treasury and wars · 24 Spaces stage loop · 25 photo mode + share to X · 26 invite-a-fren rewards · 27 Jupiter swap at Terminal Cafe
· 28 Seeker perks lounge · 29 airdrop seasons (Cash/cosmetic points) · 30 prediction markets on real CT events (Cash) · 31 hackathon mode ·
32 Blinks · 33 day/night + weather, night-only venues · 34 animals + real ambience · 35 NPC routines + drainer-DM events · 36 Timeline as world
feed · 37 garage + car customisation · 38 wardrobe/outfits · 39 home tiers + housewarming · 40 analytics + crash reporting · 41 legal pack
(ToS, privacy, disclaimers) · 42 dApp Store submission (listing, trailer, policy re-check) · 43 backend deploy + load test.

0. **FE wiring after backend merges** (small, any Unity agent can take it on the side): flip `WireV3Kinds` on; read `GET /v1/me/entitlements` live; after #7, home flow: export `contract/ids/home_units.json` door ids `flat:<buildingInstanceId>:<doorIndex>` for the 6 lg_midrise_flats doors, check `GET /units/{unitId}/access` before opening the door leaf, stream `roomType` on visit, and join the `home` room. `GET /homes/{id}` now returns `view: owner|guest|public`; non-owners get no status, rent or unit fields (`Logic/Homes.cs` already tolerates missing status). The `home` room kicks revoked clients with code 4403: show a 'you were shown out' toast and walk them to the door.
1. **[next]** District fix-ups 3 (left from fix-ups 2): the summit cone above the tree line is still a big bare face from
   the south (the cliff/stone Sketchfab packs could dress it); uncapped perf-route GPU is +1.0 / +2.2 / +1.8 / +1.0 ms
   (eye / drive / fly80 / skyline) over pre-fixup (capped GPU within 0.3 ms) - the mountain forest seen from fly80 is the
   likely cost; the masonry venues still read as plain render boxes from afar (no balconies, awnings, rooftop variety).
2. **[DONE 2026-10-24 ~01:00 UTC: the user downloaded these by hand + more; processed: casino props, lilac (BROKEN: merged into 1 asset, needs splitting via world_v3_trees.py like the maples), dining interior, contemporary apts, American house; cliff rock 75fb6b34 downloaded, unprocessed] Sketchfab batch, NEXT WINDOW (≈04:20 UTC 2026-10-24), in this order (vetted 00:40 UTC: no AI/rip flags):**
   London bus 2e32927de49e42fe913ec01c097fb257 (robinmikart, CC-BY, 95k; fallback a2a2736856a7452bbbaf712e07ed8eb4) ·
   London phone booth b9033372814b421a8fb225990c8f2226 (stovepipe, CC-BY; fallback 759770a87c4640ebadb51c7c9e039939) → TELL LANE B the paths ·
   slot machine b853ed93538a4405949800ab501606d8 (nermin, 5k) · roulette 18a27b0e326644a0b47887d07e0d0f99 (Robilben, 87k) ·
   poker table 5c335b7eff684213bd955b05ef90a354 (thethieme) · blackjack table baf7e3cbd14140ca81ea1a79d28fb635 (ravijangid.rv) ·
   lilac bushes 10312697ec994fc99355cb94f1963a2e. Then the older queue below.
   (older)  (next window). Still to download (the 06:20 UTC batch was lost to a curl quoting bug): bedroom 4f3db3cb, bathroom 9ba7e0a0,
   cafe a945f202, pub a6534141, office 5540183d, container 772f4be3, crane 49851dc7, lilac bushes 10312697. Next: dining df3f3c9f, Contemporary Apts 0d928b4f,
   American House 798ffaea, cliff 75fb6b34, stone pack f3e0a67b, pigeon 797d27b6, seagull 73aed843, snake 83c4290c,
   dog 9abfce88, cat 0b86bde7, bus station 8c984e73, dumpster 6caa33dd, fence f2ed9f55, palm d05c8ffb.
3. **Interiors:** the ~8 reusable types (apartment done; bedroom, bathroom, cafe, shop, office, bar/club, gym) behind
   real doors; every venue gets a real interior. The m99_storefront shop interiors need rescaling.
4. **[partly done] Draw calls:** the instance renderer (225 draws) is next; the hitches are fixed. Old note: single-frame 7.5–15 fps hitches when the van, courier or Decorate thumbnails spawn (pool and prewarm them). ~740 at eye level vs ≤350. Traffic cars drawn per part (merge static parts), avatars, venue
   dioramas.
5. **Remaining districts on the v3 kit:** Builder Hills, Airdrop Farm, Boardwalk/beach, Breakpoint Park (convention
   centre), MtnDAO mountain/lodge, the islet. Timeline Square plaza and the venue landmarks are still toy-shaped.
6. **Home and rent flow (FE + backend PR):** a lease maps to a flat/door; the owner's door unlocks; visits stream the
   room; tiers (street/shelter, basement, flat, penthouse). Design: `ct-life-evidence/systems-research-20261022/ownership-housing-shops.md`.
7. **Duo Shop + scheduled doorstep delivery (FE + backend PR):** furniture catalogue, delivery windows, a courier NPC/van, a
   parcel at the door, furniture placement in the home.
8. **NFT art frames:** show your wallet's NFTs (DAS) framed at home, with report/block buttons.
9. **Hovercar sale:** the Core collection, the Duo Shop page, the `HOVERCAR_COLLECTION` env.
10. **[backend PR #17 open, after #15/#16; FE next: a Steal prompt (resend vehicle_enter steal:true on STEAL_REQUIRED, traffic ids `traffic_*`), the HeatPlate from /heat/me, police AI cruisers per pursuitId (dedupe city+street events; despawn on police_pursuit_end; POST /heat/caught on contact), jail venue ct_jail with countdown + bail, JAILED gating in the UI] Police "Sleuth heat" + carjacking:** a heat system, police AI using the police cruisers, the Steal prompt (the HUD is
    already built), an original parody sleuth character (no ZachXBT likeness).
11. **Life:** pedestrians density and variety, animals (pigeons, gulls, dog, cat, snakes), ambient audio pass (real
    recordings).
12. **Small gaps:** remote horn, haptics helper, hovercar glass and visible driver, washed-out sun-side sky, 2 pre-existing
    EditMode test failures.

## Waiting on others
- TODO when GitHub API works again: comment on backend PR #15 about c55f22e (casino crash rounds settle on read; casino gate tests timed out on the slow local DB, so rerun them on CI).
- Firebase project `ct-life` created 2026-10-24 (no Gemini, no Analytics); Android app com.ctlife.app registered; client config at client/google-services.json (gitignored; copy in ~/.ctlife-local/secrets/). Push delivery still needs the SERVER service-account key: the user generates it (credential, not handled by agents) and gives it to Iyke for Railway (`FCM_PROJECT_ID=ct-life`, `FCM_SERVICE_ACCOUNT_JSON`).
- MERGE POLICY (user 2026-10-24): we merge frontend PRs (iyke-fe dev); Iyke merges and deploys the backend. Combined backend PR #15 (#9-#14, conflicts resolved, migrations 084→090 apply in order, 1046 tests pass) is OPEN for Iyke with a deploy runbook. Iyke merges #15 (not the six). Season furniture reward picks are placeholders for the FE to choose.
- 2026-10-23 08:41 UTC: Iyke MERGED backend #7 and #8 and frontend #4, but before our #7/#8 updates (door ids, access homeId, /me fix, wireV3Kinds, catalogue), which go in a follow-up PR. Backend #9 (streak, chest, quests, push) is open, with the tip/call/visit anti-farming fix pushed. Backend #10 (the #7/#8 follow-ups) is open. USER DECISION PENDING: a small Cash tip fee (e.g. 3%, burned) would make alt-ring quest farming net-negative; without it, a 3+ alt ring can still earn ~1 social quest per counterpart per week. The next iyke-fe sync needs a NEW PR from sync/v3-studio-world (#4 merged).
- Backend #6 MERGED 2026-10-23 05:19 (vehicle kinds and entitlements). Iyke must deploy it, then the FE flips `WireV3Kinds` on (queue item 0).
- Backend #8 (Duo Shop + delivery, merge after #7) open: FE needs the real `contract/ids/shop_items.json` (with modelKey), the Duo Shop app (catalogue, window asap/30m/1h, door/locker), a courier van on `delivery_out` (unitId/locker, etaAt), a parcel on `delivery_arrived` then a refetch of `/me/items`, and place mode (`PUT /homes/{homeId}/placements`, metres and radians).
- Backend #7 (homes, flats, doors) open; the security fixes are pushed (view-scoped GET, live revocation, rent-worker locking).
- User: keep the Seeker connected with USB debugging authorized ("Always allow").
