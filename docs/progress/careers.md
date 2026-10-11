READY TO COMMIT: client/Assets/Scripts/Features/Careers/ docs/progress/careers.md docs/progress/requests.md docs/fe-contract-proposals/careers-minigames-20261008.md docs/fe-contract-proposals/ids/mini_games.json — Careers lane: Jobs app for 14 careers × 5 levels (apply, skill gates, shifts, pay from server values), 33 new CT-native mini-games on a deterministic mini-game framework (42 total with the 9 Phase 1 games), side gigs, 114 EditMode tests

(Unity generates the `.meta` files on first import; commit them with this. The app is not reachable until the
integration lead applies `docs/progress/requests.md` → "2026-10-08 careers -> integration lead".)

# Careers and mini-games lane — progress log

Owner: careers lane agent. Scope: `client/Assets/Scripts/Features/Careers/` (assembly `CTLife.Features.Careers`,
C# namespace `CTLife.Careers`), this log, FE proposals in `docs/fe-contract-proposals/`, requests in
`docs/progress/requests.md`. No core file or other lane's folder was edited. No Unity editor, no adb, no git writes.

## Status (2026-10-08)

Built, compiling, and the logic is covered by **114 passing NUnit cases**, run outside the editor. All three
passes of the shared `client/Tools~/compile_check.py` (player, `--release`, `--editor`) are green with this
assembly and its tests included; the script discovers feature lanes on its own. Wire shapes were checked against
the integration lead's local backend with a throwaway dev wallet (see "Backend vs contract").
**Not yet run in the Unity editor or on the Seeker.** The UI code is compiled but no one has seen or touched it. UI
is functional, not styled.

## What is built

| Area | What | Where |
| --- | --- | --- |
| Mini-game framework | Deterministic seeds (FNV-1a of `shiftId|miniGameId|index` → Mulberry32; practice uses a nonce). An active-time clock that pauses on focus loss and on any frame gap over 1.5 s, with a 180 s pause budget. Session state machine: Briefing (tutorial card) → Playing ⇄ Paused → Results / Cancelled. Score always capped at the registry `maxScore`. | `Logic/DetRng.cs`, `Logic/MiniGameSession.cs`, `Logic/Scoring.cs`, `Logic/MiniGameSpec.cs` |
| 8 game engines | Sort (guess-proof formula scoring), Flag hunt, Sequence, Match, Pick, Combo (hidden total), Timing (pre-sampled windows), Balance (fixed 20 Hz simulation with cues). All pure C#. | `Logic/Engines/` |
| 33 new games | 3 per career for the 11 Phase 4 careers (list below), plus routing to the 9 existing Phase 1 games = **42**. | `Content/*.cs` |
| Host UI | Full-screen overlay: tutorial card (rules, scoring, "Based on" real task, limits), timer, score, pause; the board is hidden while paused; results card with grade and stats. Touch targets ≥ 112 px; text verdicts, never colour alone. | `UI/MiniGameRunner.cs`, `UI/EngineViews.cs`, `Resources/CTLifeCareers/careers.uss` |
| Careers progression | 14 careers × 5 levels from config `careers` joined with `GET /careers`: status (hired / hiring / skill-gated / later phase / not hiring), level title, XP, career ladder with each level's pay, shift length and XP, and pay "up to" from server values. Apply calls `POST /careers/{id}/apply`. | `Logic/Work/CareerModels.cs`, `UI/CareersApp.cs` |
| Paid shifts | `POST /shifts` → validate the assignment (registry caps win; unknown, duplicate or other-career ids are refused, nothing submitted) → play each game with its shift seed → wait for `endsAt` (server clock plus a monotonic floor) → `POST /shifts/{id}/complete` with exactly the assigned games, in order, capped → receipt and balance refresh. After an unknown outcome, "Check again" reuses the Idempotency-Key, so it can't pay twice. After `SHIFT_NOT_FINISHED`, the client waits for `details.endsAt` and sends again with a new key. | `Logic/Work/ShiftFlow.cs`, `UI/ShiftRunner.cs` |
| Practice | Every game of every career, no server calls, fresh seed each time. Release builds respect the phase gate; dev builds allow all 42 for review. | `UI/CareersApp.cs` |
| Gigs | `GET /gigs`, `POST /gigs/{id}/run` with CT City flavour for both the contract ids and the backend's ids. Cooldown display. 429 `retryAfterMs` honoured. Keys kept after an unknown outcome. Shown from Phase 4. | `Logic/Work/Gigs.cs`, `UI/CareersApp.cs` |
| Net | Thin wrapper on `IRestClient`. Always sends a JSON body. Defensive parsing of contract and backend shapes. Ids in URL paths restricted to `[A-Za-z0-9_-]`. | `Net/CareersApi.cs`, `Logic/Work/CareersWire.cs` |

### The 42 mini-games

Phase 1 (existing, in `CTLife.UI`, routed through `LegacyGames`): Discord Mod — Mod Desk, Raid Router, Role Lock ·
Junior Analyst — Earnings Desk, Receipt Match, Runway Plan · KOL Engagement — Thread Workshop, Clip Cut, Live Cues.

New (this lane). The first id per career is the one `contract/config/careers.json` already references:

| Career | Games (id · engine · real task it mirrors) |
| --- | --- |
| Copywriter | `copywriter_rewrite` Red-Pen the Shill · Flag · editing paid/launch copy · `copywriter_char_crunch` 280 Crunch · Combo · fitting a post to 255–280 chars · `copywriter_safe_to_post` Safe to Post? · Sort · final review of client copy |
| Graphic Designer | `designer_canvas` Readable or Not · Sort with live colour swatches, WCAG 4.5:1 · `designer_trait_mixer` Trait Rarity Mixer · Combo · `designer_billboard_proof` Billboard Proof · Flag · sponsored-placement rules |
| Smart Contract Auditor | `auditor_report` Severity Call · Sort (4 levels, impact-based) · `auditor_spot_the_bug` Spot the Bug · Flag (Solana exploit classes) · `auditor_rug_scanner` Rug Risk Scanner · Flag (generated token reports) |
| Yield Farmer | `yield_farm_apy` Real Yield Picker · Pick (fees vs printed incentives, vault cut, IL) · `yield_farm_lp_range` Stay in Range · Balance (the "LP army" range game; every move costs fees) · `yield_farm_sybil_check` Sybil-Safe Farming · Sort |
| Shill Runner | `shill_post` Bait or Alpha · Sort · `shill_room_match` Right Shill, Right Room · Match · `shill_reply_timing` Top Reply · Timing (pass on bait) |
| Doxx Researcher | `doxx_thread` Receipts Thread · Sequence (bundle, fake-PnL, drainer, slow-rug, paid-shill, KOL-wallet-leak, stream-rug schemes) · `doxx_alt_match` Same Anon? · Match (generated tells) · `doxx_fact_check` Source It · Sort (proof / screenshot / vibes) |
| Trend Hunter | `trend_call` Call the Rotation · Pick (growth vs acceleration) · `trend_odds` Price the Odds · Sort (prediction-market EV vs costs) · `trend_arc_order` Read the Arc · Sequence (stealth → hype → peak → aftermath) |
| KOL Manager | `kol_negotiate` Rate Card · Pick (real reach per $, budget, KOL-wallet-list dumpers) · `kol_bot_audit` Bot or Not · Sort (generated profiles) · `kol_agent_leash` Agent Leash · Sort with a stateful budget judge (AI agent policy) |
| DevRel | `devrel_ama` AMA Triage · Sort (4 routes) · `devrel_spaces` Keep the Room Alive · Balance with cues · `devrel_partner_match` Partnership Match · Match |
| Alpha Lead | `alpha_call` Curve Exit · Timing (bonding-curve exit before the dev dump; holder rewards) · `alpha_arb_spread` Arb the Spread · Timing (perp vs spot incl. OIL/GOLD, pass under fees) · `alpha_book_balance` Keep the Book Balanced · Balance |
| Rug Sleuth | `sleuth_thread` Follow the Wallets · Pick chain (trace the bulk to an exchange deposit) · `sleuth_bundle_check` Bundle Check · Flag (generated launch-slot buyers) · `sleuth_cluster_share` Insider Share · Pick (sum clusters) |

Tuning check (score-distribution diagnostic, 30 seeds per game at default caps): a perfect player reaches 100 on every
board and timing game. On the balance games a naive scripted controller scores 61–100 (it pays every fee, so a good
human beats it). An idle player gets 0 on board games, 0–20 on timing games (passing the one no-edge window is the
right call) and averages about 10–13 on balance games, with a worst seed of 35 (ranges are in `ContentTests`).

## Research (2026-10-08) — what real CT work the games mirror

### Re-check against the complete CT-now dossier (`docs/research/ct-now-2026-10.md` + `.json`, 1 Sep–7 Oct 2026)

The coordinator committed the full dossier after the first build. Each section below was re-checked and the games
were adjusted:
- **§4a careers / JSON `careers`.** The dossier proposes 14 careers. The contract's 14 career ids are BE-owned and
  differ, so each dossier job is mapped onto a contract career's games:
  - reply guy → Top Reply and Bait or Alpha (Shill Runner);
  - clipper → Clip Cut (KOL Engagement);
  - KOL/caller "survive the KOL-wallet exposé" → Rate Card dumpers (KOL Manager);
  - ghostwriter → Red-Pen the Shill (new board: sponsored "I tried it" thread), 280 Crunch and Thread Workshop;
  - community manager "run AMA / defuse FUD" → AMA Triage (new FUD card);
  - Discord mod → Mod Desk;
  - Spaces host "keep energy / promote speakers / cut trolls" → Keep the Room Alive;
  - sleuth "trace bundle graph" → Follow the Wallets and Bundle Check;
  - auditor "spot bug line" → Spot the Bug;
  - **LP farmer "keep price in range" → new `yield_farm_lp_range` Stay in Range**, which replaces the earlier Claim
    Window game;
  - market maker → Keep the Book Balanced (now with "Founder posts: 'MM should buy it all back and burn it'");
  - livestream dev → a livestream-launch variant in Curve Exit plus a "Dev stream" risk line in Rug Risk Scanner;
  - P2P merchant (gig) → the data-entry gig's CT flavour (naira-to-USDC order reconciliation);
  - PFP artist → anime-PFP traits in Trait Rarity Mixer.
  Hackathon builder, booth crew, news-desk operator and a standalone livestream-dev career have no contract career.
  They are listed as future careers/gigs in the proposal doc §7.
- **§2a slang.** Only live, new or evergreen terms are used. Fading "ser/fren/wagmi" (three tells in Same Anon?) were
  replaced with live ones ("skill issue", "we're so back", "cooked"). Grades now read "We're so back" / "Skill issue".
  A test (`NoRealBrandNamesInContent`) fails the build if fading or never-use slang ("jeet") appears in any game
  content across 25 seeds.
- **§2b post formats / §2c drama.** Hiring bait ("$1,500 a week, reply below"), the price-trigger giveaway, the
  reverse-psychology shill, the loyalty airdrop, the shill-me bait and the tracker legend are now Bait or Alpha
  cards and Top Reply windows. The liquidation clip and the JUST IN liquidation bot are Top Reply posts. The
  exposé-with-no-receipts and copy-trade-victim formats are Source It cards, and the copy-trade trap is a Receipts
  Thread scheme.
- **§3a/3b archetypes / events.** The scam trio (Hiring-Bait Farmer, Giveaway Guy, Drainer) all appear in Bait or
  Alpha. The AI Agent begging for compute and the "AI bot tutorial" self-drain are Agent Leash actions. The Hermit
  Crew "paying well for OTC help" is an AMA Triage mod card. The Odds Crier's "Mayor Toad 68%" and the "FOMO
  Committee" are Price the Odds markets.
- **§5b Nigerian/African CT.** The Shill Runner ladder now follows the airdrops → gigs → monthly-retainer path
  (Reply Guy → Gig Poster → Campaign Runner → Retainer Marketer → Head of Growth). A "Lagos builders' night"
  Partnership Match board covers naira on-ramp via P2P merchants, a retainer instead of airdrops, Hausa-language
  AMAs and Startup Village pitch practice. There is a disclosed sponsored "I tried funding from naira" thread job.
- **§6 parody names.** Player-facing text now uses TeleGrum and Discordia, never Telegram/TG/Discord, and USDC instead
  of USDT. COIN2050, Mayor Toad and the FOMO Committee come from the list. The guard test bans the real names
  (Telegram, Twitch, YouTube, Bloomberg, BlackRock, TOKEN2049, Breakpoint, Bitget, Robinhood, …). "Discord Heights"
  stays because it is the contract's own venue name (`contract/ids/venues.json`). Solana, Seeker, SKR, ORE and USDC
  stay real, as the brief allows.

### Primary sources fetched for the first build

WebSearch was unavailable in this session (budget exhausted). Sources are primary pages fetched with WebFetch on
2026-10-08, plus the repo's dated X research. Parody names only in game content; a test (`NoRealBrandNamesInContent`)
guards a list of real platform names.

| Source (fetched 2026-10-08 unless noted) | Used for |
| --- | --- |
| `docs/research/ct-now-2026-10.md` + `.json` (first used as v0.1; re-checked against the complete version above) | Launchpad holder rewards (12 Sep) → Curve Exit payoff; public KOL wallet database (3 Oct) → Rate Card dumpers, Source It, Receipts Thread; "build an AI crypto bot" self-drain tutorials (20 Sep) → Agent Leash, AMA Triage; loyalty-test "quote with your username" airdrops and "shill me your hidden gem" (6 Oct) → Bait or Alpha, Top Reply; perps on oil and gold (5 Oct) → Arb the Spread; prediction-market accounts posting news as odds → Price the Odds, Top Reply; liquidation sizes ($360M in 10 min) → Top Reply; TOKEN2049-style conference week and suits → Partnership Match; "1K → 15M" / "retired off one trade" tracker formats → Bait or Alpha, Source It; stream-dev rug (3 Oct) → Receipts Thread. Its section 4 "Jobs" was still `[pending]`. |
| `docs/research/ct-x-observations-2026-10-04.md` | Giveaway / "drop your wallet" template, paid reply-guy farms, "GUARANTEED 100X ... early CA", fake PnL flexes, bundle/cabal exposés, sybil filters and claim windows, the reply-guy growth ladder. |
| [Decrypt, 15 Jan 2026](https://decrypt.co/354736/elon-musks-x-bans-access-infofi-crypto-projects-ai-slop-backlash) | X revoked API access for pay-to-post apps over "AI slop and reply spam" → shill games reward signal, not bait. |
| [coral-xyz/sealevel-attacks](https://github.com/coral-xyz/sealevel-attacks/tree/master/programs) | Solana bug classes (signer authorization, owner/data matching, type cosplay, initialization, arbitrary CPI, bump canonicalization, closing accounts) → Spot the Bug. |
| [Immunefi severity classification v2.3](https://immunefi.com/immunefi-vulnerability-severity-classification-system-v2-3/) | Critical / High / Medium / Low by impact → Severity Call. |
| [pump.fun fees page](https://pump.fun/docs/fees) (page dated 20-21 May 2026) | 1.25% bonding-curve fee, graduation to an AMM pool, creator fee share → Curve Exit. |
| [Hyperliquid liquidations](https://hyperliquid.gitbook.io/hyperliquid-docs/trading/liquidations), [funding](https://hyperliquid.gitbook.io/hyperliquid-docs/trading/funding) | Mark vs spot premium, hourly funding, basis trades → Arb the Spread. |
| [Polymarket 101](https://docs.polymarket.com/polymarket-101) | Price = implied probability, $1 per winning share → Price the Odds. |
| [Arbitrum sybil-detection](https://github.com/ArbitrumFoundation/sybil-detection) | Common funder, sweeps, clusters of 20+, similar activity → Sybil-Safe Farming. |
| [Bubblemaps](https://bubblemaps.io/) | Wallet clusters and supply concentration → Insider Share, Bundle Check. |
| [RugCheck](https://rugcheck.xyz/) (title only; field list `[Unverified]` typical checker fields) | Mint/freeze authority, LP, holder concentration, mutable metadata, creator history → Rug Risk Scanner. |
| [Solana Agent Kit](https://github.com/sendaifun/solana-agent-kit), [x402](https://www.x402.org/) | Agents that trade/launch/pay per call with no built-in spend limits (page shows 75.41M x402 tx / 30 days) → Agent Leash. |
| [Superteam Earn](https://superteam.fun/earn/) | Bounty categories Content / Design / Dev → copywriter and designer framing. |
| [Meteora docs](https://docs.meteora.ag/) | Fee yield vs incentives in concentrated-liquidity pools → Real Yield Picker. |
| [WCAG 2.1 SC 1.4.3](https://www.w3.org/TR/WCAG21/) | Contrast ≥ 4.5:1 → Readable or Not (borderline pairs, 2.6–5.0, never generated). |
| [Discord safety: scams](https://discord.com/safety/protecting-users-from-scams-on-discord) (dated 3 Feb 2022) | Urgency, unknown links, QR scams, impersonated bots → AMA Triage, copy games. |

Not reachable: Hummingbot inventory-skew page (404), so Keep the Book Balanced rests on the general idea of
inventory risk `[Unverified against a primary source]`. X help for Spaces returned 403, so Keep the Room Alive uses
the roles described in `docs/research/ct-social-game-direction-20261006.md`. "Doxx Researcher" is the contract's career
id; its games only link fictional accounts and grade evidence. Nothing in them identifies a real person.

## Backend vs contract (ct-life-backend 9f3e5ef, read-only; probed live on the local stack 2026-10-08)

Confirmed with a throwaway dev wallet against `localhost:4010` (no tokens kept in the repo):
- `GET /careers` returns only the 3 active careers, each `level: 1, xp: 0` plus `payCents/shiftSec/miniGameId/phase`,
  all with `active: true`.
- `POST /careers/{id}/apply` returns `{id, level}`. Applying to an inactive career gives `400 VALIDATION_FAILED
  reason: career_inactive`.
- `POST /shifts` returns ISO `endsAt`. An early completion gives `400 SHIFT_NOT_FINISHED` with `details.endsAt` in
  epoch ms (the client reads both). `POST /shifts {careerId: "copywriter"}` (an inactive career) still returns a
  shift with `copywriter_rewrite`.
- `GET /gigs` returns `{gigs: [{gigId, name, description, minPayCents, maxPayCents}]}` (backend ids).
  `POST /gigs/food_delivery/run` gives 201 with `{runId, gigId, payCents, outcomeId, startedAt, endedAt}`. Re-sending
  the same key replays. A new key straight away pays again: there is no cooldown. Contract id `doot_feed` gives
  `400 unknown gigId`.
- `POST /shifts/{id}/complete` with 8/10 after `endsAt` gives `{payCents: 3200, xp: 80, promoted: false}`, i.e. 4000 ×
  0.8. The same key replays that 200. A new key afterwards gives `404 NOT_FOUND details.kind: shift_completed`;
  the client now treats that as "already paid" and refreshes the balance. Adding an unassigned
  `{miniGameId: "made_up_game", score: 100}` next to a 0/10 raised pay from $0 to $36.36: the anti-cheat gap is
  confirmed live. `GET /careers` still reports `xp: 0` after two settled shifts.

From reading the source: skill gates never block, XP isn't persisted, `promoted` is always false, and `complete`
accepts unassigned ids. Full list and proposals: `docs/fe-contract-proposals/careers-minigames-20261008.md` §4–6.
Forwarding is requested of the lead (requests item 4).

## Hooks requested (`docs/progress/requests.md`, 2026-10-08 careers -> integration lead)

Update 01:36: hooks 1 and 2 are applied in the working tree by the lead. With them, `compile_check.py --editor` and
the player pass compile `CTLife.App` cleanly against this assembly. Earlier transient failures came from four
concurrent `compile_check` runs sharing the cache, not from this lane.


1. Add `"CTLife.Features.Careers"` to `CTLife.App.asmdef`.
2. `PhoneCatalog`: `jobsOverride ?? new CareersApp()` (+ `using CTLife.Careers;`). A patched copy of `App/`
   compiled clean against the current tree.
3. Optional: a one-line `PlaySeeded(...)` entry point on each of the 9 Phase 1 games, so shifts are deterministic there too.
4. Forward the backend mismatches. 5. (Design call) world job spots → `Open("jobs")`.
No `compile_check.py` change is needed: it already picks up the assembly and tests.

## Contract proposals made (FE-owned, not assumed accepted)

- `docs/fe-contract-proposals/ids/mini_games.json`: all 42 entries in the contract shape (3 already in the registry,
  11 referenced by `careers.json` but missing from it, 28 proposed). Generated from the client catalog.
- `docs/fe-contract-proposals/careers-minigames-20261008.md`: per-level `miniGameIds`; the seed derivation (and an
  optional `Shift.seed`); a later replayable-input anti-cheat path; the meaning of `GET /careers` `active` and an
  `xpToNext` field; gigs aligned with contract `gigs.json`.

## Verify

```sh
client/Assets/Scripts/Features/Careers/Tests~/run_careers_tests.sh      # → PASSED 114 FAILED 0 (add --head-deps if others' work breaks deps)
python3 client/Tools~/compile_check.py                                  # player: CTLife.Features.Careers ok, 0 warnings
python3 client/Tools~/compile_check.py --release                        # CTLIFE_PROD: ok
python3 client/Tools~/compile_check.py --editor --keep-going            # editor + CTLife.Features.Careers.Tests: ok
```
In the editor: Test Runner → EditMode → `CTLife.Features.Careers.Tests`. Last run 2026-10-08: everything above green.

Manual checks for the lead once the hooks are in (editor, dev auth, local backend):
1. Phone → Jobs. Three Phase 1 careers show "Hiring". Apply to Discord Mod → "Your job".
2. Work a shift. The Mod Desk opens, then the wait countdown, then a receipt with the server's `payCents`, and the
   balance refreshes.
3. Details on any career → Practice each game. The tutorial card appears, pause hides the board, Alt-Tab pauses,
   results show.
4. Run with launch arg `phase=4` → the Gigs tab lists the backend gigs. "Do it" pays once; tapping again pays again
   (the backend has no cooldown).

## Gaps / not done

- Not run in the editor or on a device. Layout at the Seeker's 6.36" landscape and the actual touch feel are unverified.
- The 9 Phase 1 games still seed from the tick count until hook 3 lands.
- Promotion progress: there is no contract field for it, so only level, XP and the server's `promoted` flag are shown.
- Held-job memory under the backend's "every row active" semantics is per device (PlayerPrefs), not server state.
- No share cards for shift receipts; no 3D venue hooks (World lane).
