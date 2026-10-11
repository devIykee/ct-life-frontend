READY TO COMMIT: client/Assets/Scripts/Features/WorldLife/ docs/progress/world-life.md docs/progress/requests.md docs/fe-contract-proposals/npc-world-life-20261008.md docs/fe-contract-proposals/npc_lines.world-life-20261008.json — World Life lane: CT NPC presentation for all 28 spec archetypes (backend-accurate state interpretation, lines with slot filling, bubbles/babble/props), city events as world moments (Town Crier JUST IN, crowd reactions, siren/carpet yank/exploit alarm/sniper swarm/furnace flash, Market Mood hook), live billboards/Pixel Wall/Timeline screen, 106 EditMode test cases

(Unity generates the `.meta` files on first import; commit them with this. Nothing is live until the integration lead
applies `docs/progress/requests.md` → "2026-10-08 world-life -> integration lead", hooks 1–3.)

# World Life lane — progress log

Owner: world-life agent. Scope: `client/Assets/Scripts/Features/WorldLife/` (assembly `CTLife.Features.WorldLife`,
namespace `CTLife.WorldLife`), this log, my section of `requests.md`, two files in `docs/fe-contract-proposals/`.
No core file, other lane, backend or git state was touched. No Unity editor, no adb.

## Status (2026-10-08)

Built and compiling in `compile_check.py` player / `--editor` / `--release` (0 errors, 0 warnings in this assembly).
Pure logic covered by **106 passing NUnit cases** run out of the editor. **Not run in the Unity editor, on the
Seeker, or against a live backend.** All Unity glue (UI Toolkit render-texture panels, MaterialPropertyBlocks, NavMesh
warps, primitives, babble) is compile-verified only. UI is functional, not styled.

## What is built

| Area | What | Files |
| --- | --- | --- |
| NPC state playback | `NpcBeatInterpreter` turns the replicated stream into stable "runs" (backend rewrites `startedAt` every tick and drops `animId`/`lineId` after one tick); unknown states → idle; offstage handling (Perp Degen "goes to the Rekt Clinic" → leave + hidden, respawn when back); spot resolution for brain names, venue ids and `"x,z"` (`SpotResolver`) | `Logic/NpcBeatInterpreter.cs`, `Logic/SpotResolver.cs`, `Runtime/NpcPresenter.cs` |
| Lines | `NpcLineBook`: backend line ids bound to `npc_lines.json` by alias; FE proposal sets for the 21 archetypes the backend lacks (32–33 lines each); `{slot}` filling from `lineParams` with formatting (`*Cents` → $, `*Bps` → %, `$TICKER`), sanitised (no rich text, control chars, braces), never shows a raw `{slot}`; deterministic pick per `npcId|lineId|run start` so all clients agree | `Logic/NpcLineBook.cs`, `Logic/WireText.cs`, `Resources/WorldLife/npc_presentation.json` |
| Bubbles, babble, gestures | Every fresh line in any state (not only `talk`): bubble (3.5–6.5 s), audio-lane `BabblePlayer` bank per archetype (fallback `Babble.Speak`), archetype talk gesture | `Runtime/NpcPresenter.cs` |
| Looks/props | `archetype_looks.json`: 30 archetypes (29 spec ids incl. Farmer/Sybil split + ORE Miner), signature line, look note, props (placeholder primitives or `Resources/WorldLife/Props/<id>`), body mode (Sniper Bot = drone), talk/idle emotes, behaviours, `castKeys` for the cast lane (data-only swap together with NpcCastCatalog) | `Logic/ArchetypeBook.cs`, `Runtime/PropDresser.cs` |
| Per-archetype behaviours | Reply Guy faces his follow target on each line; Giveaway Guy drain gag on `toast_drained` (laugh, line, coin burst); Town Crier shouts; Perp Degen liquidation → siren; Sybil clones (≤ 10, ≤ 90 s) cleared by the Community Mod; Chain Tribalists face each other; AI Agent / Sleuth / Thread Boy / Wallet Tracker / Alpha Caller / Engagement Farmer / Doomposter / Fake-Rich / Town Crier post their lines to the Timeline screen; Doomposter and Normie waves are presented exactly as the server drives them | `Logic/Behaviours.cs`, `Runtime/MomentStage.cs`, `Runtime/WorldLifeHub.cs` |
| City events → world moments | `EventMoments.Plan`: all 6 contract events; Town Crier "JUST IN" shout filled from event params (client-staged because the backend never notifies the crier brain; a server crier line of the same category cancels it); deterministic crowd reactions on idle/act/follow NPCs only (≤ 3–5, staggered); venue effects: Perp Pit siren, rug "carpet yank", Audit Row exploit alarm, Launchpad sniper drone swarm (10 s), Refinery furnace flash; only events ≤ 30 s old by the server clock are staged; reconnect replays deduped | `Logic/Moments.cs`, `Runtime/MomentStage.cs` |
| Hooks for other lanes | `WorldMomentSignal` (Bus) + `IWorldMomentAudio` (positional loops); `MarketMoodSignal` + `IMarketMoodTarget` (venue/lighting tint, red/green bands with hysteresis at the −2% Doomposter line) | `Runtime/WorldLifeHooks.cs` |
| Live surfaces | Screens found by material `Screen_<id>`; Timeline screen (events + NPC posts + `GET /feed?scope=global`), billboards/kiosks (`GET /ads/live` creative with a "Sponsored" chip, else rotating parody house ads with the baked art kept in rotation), Pixel Wall (5×4 tiles, live tiles marked AD); ≤ 6 surfaces × ≤ 512 px RT, panels render only on change; creatives ≤ 1.5 MB, PNG/JPEG magic check, ≤ 1024 px, 16 MB LRU budget, on-screen textures pinned | `Logic/Surfaces.cs`, `Runtime/LiveSurfaces.cs`, `Runtime/AdImageCache.cs`, `Runtime/WorldLifeApi.cs`, `Resources/WorldLife/house_ads.json` |

Failure paths: `/ads/live` or `/feed` 404/405/501 → stop asking this session, house content / events only; other
errors → backoff (ads 60 s → 10 min, feed 30 s → 4 min); creative download failure → not retried for 10 min; unknown
archetype/spot/line/state ids → fallback + one warning each ("Contract gap"); seam missing → one warning, legacy NPCs.

## Verify

```sh
python3 client/Tools~/compile_check.py                     # player: CTLife.Features.WorldLife ok, 0 warnings
python3 client/Tools~/compile_check.py --editor            # + CTLife.Features.WorldLife.Tests ok
python3 client/Tools~/compile_check.py --release           # CTLIFE_PROD pass ok
client/Assets/Scripts/Features/WorldLife/Tests~/run_worldlife_tests.sh   # → PASSED 106  FAILED 0
```
In the editor: Test Runner → EditMode → `CTLife.Features.WorldLife.Tests`. Last run 2026-10-08 00:40: all clean.
Hook verification: the exact seam diff (requests.md hook 2) plus hooks 1 and 3 were applied to a scratch copy and the
whole client (App included) compiled.

Tests (106 cases, `Tests/*.cs`): line formatting/slots/sanitising; line book incl. every backend brain line id and
determinism; line content rules (30–60 lines, no "jeet", balanced slots); spot resolution; NPC state interpretation
against backend tick sequences (Giveaway cycle, Reply Guy, Perp Degen liquidation → Rekt Clinic → respawn, Doomposter
perpetual leave, unknown state/spot); behaviour cues; event → moment mapping for all 6 events × venues, staleness,
crowd bounds, mood, keys; crier queue; mood bands; timeline feed; live ads parsing/URL safety/slots/rotation/backoff/
magic bytes; texture budget; coverage of archetypes/anims/sfx/house ads against `contract/`.

## Research and sources (content is CT October 2026, parody names only)

Coordinator instruction 2026-10-08: lines/behaviours/events from research. Inputs:
- `docs/research/ct-x-observations-2026-10-04.md` (giveaway template, paid reply-guy farms, "GUARANTEED 100X cabal
  launch", fake PnL flexes, wallet-tracker "$44 → $509k", JUST IN bots, exposé threads, stream-dev rug, points/sybil).
- `docs/research/ct-now-2026-10.md` + `.json` **v0.1 partial** (00:17): parody names adopted (Pomp.fun, Bitgot,
  Hyperliquor, Bloombag Terminal, Robbinghood, Pollymarket, COIN2050 Singapura, Checkpoint London, $DESKTOP); formats
  adopted (retired-off-one-trade, +$40k → liquidated, KOL wallet DB, holder rewards, CTO, quote-your-username airdrop,
  shill-me-your-gem, AI-bot tutorial drainer, MEV "yoink", TradFi suits/tokenized equities). **Re-check v1** when it
  lands (archetype table and drama formats were `[pending]`); update `npc_presentation.json` feLines/eventLines only.
- My WebFetch reads (WebSearch was unavailable: session search budget exhausted; no Chrome tools used), all fetched
  2026-10-08:
  - rekt.news — exploits: Term Labs 26 Aug ~$8.5M; Mantra 31 Aug $3.6M; TAC 1 Sep ~$7.5M; Kiichain 2 Sep ~$9.7M; Nesa
    11 Sep ~$50M; Tectonic 14 Sep $120.4M ("pumped TONIC ~300x and borrowed"); Liquid Network 16 Sep $320M; Nomic
    18 Sep ~$3.15M; Nostra 23 Sep $3.5M (rigged pool, oracle 8,306x); SingularityNET 25 Sep $2.29M; Bitget 30 Sep
    $387.5M hot/warm wallets; Duelbits 6 Oct ~$6.07M. → Auditor/exploit lines ("pumped a token 300x and borrowed",
    "oracle priced it at 8,000x", "hot wallet drained, withdrawals paused"). Note: the dossier has "Bitget reportedly
    hacked >$170M, 24 Sep [Unverified]"; rekt.news lists 30 Sep, $387.5M. In-game copy names no figure.
  - decrypt.co/news (7 Oct): BTC below $83K on an oil shock, ~$969M liquidated in 24 h ($644.47M longs).
  - theblock.co/latest (6–7 Oct): BTC below $84K, $487M long liquidations; political-family memecoin market-maker
    fight; Jito JTX mobile app; Orca + Loopscale.
  - coindesk.com/markets (6–7 Oct): "Liquidations jump to $547 million as oil rally hits crypto market"; BTC rejected at
    $87K; 60+ US stocks headed onchain; US scraps $10k private-wallet reporting rule.
  - cointelegraph.com/tags/solana: record 263K tokens issued in one day, mostly on the biggest launchpad (10 Sep); a
    rival launchpad out-earned it for a day, $1.76M vs $1.1M (7 Sep); Solana tx size limit tripled (15 Sep); an L1
    moving its token to Solana (21 Sep).
  - decrypt.co/degen-alley: launchpad token rally headlines 28 Sep–2 Oct.
  - protos.com (5 Oct): an on-chain investigator posed as a launderer to infiltrate a hacking group → Sleuth line
    "I went undercover as a launderer. They hired me." (no name).
- Rules applied: no real people or brands in copy; no "jeet"; numbers in event shouts come only from event params;
  numeric flavour lines are parody of formats, not claims about the game state.

## Backend vs contract (ct-life-backend main 9f3e5ef, read-only) — please forward to backend-dev-feedback.md

1. `venue.ts tickNpcs` writes `startedAt = now` to every NPC every second; it is not a state start time.
2. Brain fall-through sends `animId: loco_idle`, `lineId: ''` on later ticks of `act`/`react`/`goto`/`talk`.
3. Line ids (`crier_*`, `giveaway_pitch`, `reply_gm`, `perp_*`, `rekt_stimmy`, `wallet_turnaround`, `doom_bear`,
   `spaces_gm`, `ore_*`, `vc_*`, `builder_*`) do not exist in `npc_lines.json` (unnamed arrays); `lineParams` is never set.
4. Spot ids outside `spots.json`: `perp_terminal`, `cafe_terminal`, `venue_exit`, `builder_hq_1/2`, venue ids as spots,
   `"x0,z0"` strings from walkable rects; emote `emote_moon` is not in `anims.json`.
5. `VenueCtx.moodBps = 0`, `recentEvents = []`, `notify()` unused → Town Crier never reacts, Doomposter is always
   `leave`, Perp Degen ignores real liquidations; `richestPlayers` sorts by `netWorthCents: 0`.
6. City room never broadcasts `event`; `mood` is a synthetic ±200 bps sine (never crosses −200).
7. Perp Degen's Rekt Clinic respawn is a `spotId` inside the Perp Pit room, not a spawn in the `rekt_clinic` venue.
8. NPCs spawn only in 5 venues; brains `vc_npc`/`builder_npc` are not registry ids; 19 contract archetypes have no
   brain; Spaces Host is spawned in Timeline Square but targets `spaces_studio`.
9. `npc_tap` on Giveaway Guy `broadcast`s `toast {to, textId: toast_drained}` to the whole room (every client shows
   "you got drained"; contract ToastMessage has no `to`) and debits nothing yet.
10. `GET /v1/ads/live` returns `{ads: [{slotId, bookingId, link, imageKey, mimeType}]}` (S3 key, no `imageUrl`, no
    `sponsored`); contract is a slot map with `imageUrl`. Creatives cannot be shown until a URL is served.
11. `GET /v1/feed` returns `{posts: [...]}` (contract `FeedPost[]`; client accepts both). `GET /events/active` is not
    implemented (not needed by this lane).
12. `archetypes.json`: `spaces_host` twice; `normie.venueId = market_room` (not a venue); `laser_eyes_maxi`,
    `chain_tribalist` missing. Event `params` keys are undocumented; `evt_ore_motherlode` amount unit undefined.
Proposals for all of these: `docs/fe-contract-proposals/npc-world-life-20261008.md`.

## Gaps / not done

- Nothing verified in the editor or on device. `[Inference]` A UI Toolkit panel whose GameObject is disabled keeps its
  last pixels in the RenderTexture (`clearColor = false`); if the editor shows black screens after 3 frames, set
  `RenderFrames` to keep panels enabled (cost: a few draw calls per surface per frame).
- `[Unverified]` IL2CPP keeps the four seam fields for the reflection binder (they are read by NpcController itself, so
  stripping should keep them); switch `NpcHookBinder` to direct assignment after the seam lands if preferred.
- Billboard tap → link (needs a confirm dialog in the UI lane); share cards; Timeline phone app (UI/Phase 3 lanes).
- Town Crier 3-spot patrol, normie waves, Sybil split/Mod clear, Chain Tribalist debates, AI Agent trades need server
  NPCs/ids (§5 of the proposal); client staging is ready and data-driven.
- Positional loops use one-shot `AudioManager.PlayAt` until the audio lane implements `IWorldMomentAudio`.
- Mood tint is a hook only; no venue implements `IMarketMoodTarget` yet.

## Next

- After hooks 1–3: run against the local docker backend; check NPC runs in Timeline Square / Perp Pit / Rekt Clinic,
  the crier shout on a test `event`, and the Timeline screen + billboards in Timeline Square.
- Update lines/events from `ct-now-2026-10.md` v1 when it lands.
