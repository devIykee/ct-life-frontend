# Money and Careers backend integration — 2026-10-12

READY TO COMMIT: `client/Assets/Scripts/Features/Money/`, `client/Assets/Scripts/Features/Careers/Net/CareersApi.cs`, `client/Assets/Scripts/Features/Careers/Tests/CareersApiContractTests.cs` (+ `.meta`), this log — connect payment signature reporting, persisted prediction history, and correct alpha-call wire prices. Root Unity verification remains required before commit.

Scope: frontend integration, initially exercised against the sibling backend `ct-life-backend` checkout at **`eefa667` plus its existing likeness changes**. A later fetch revealed current upstream **`origin/main` = `93dfabf`**; its source was reaudited with `git diff HEAD..origin/main` and `git show origin/main:<path>` without checking out or overwriting the dirty backend. Do not treat the older local runtime evidence as evidence against current upstream. Backend inspected read-only. No hosted writes, real wallet signing, purchases, provider spend, Unity, ADB or Git mutations by this agent. Accepted presentation and real local minigame simulation preserved.

## Implemented

- **Top-up submission:** `TopUpFlow` now calls the existing `POST /payments/intents/{id}/submitted` after the wallet supplies a signature. The report key and acknowledgement survive process restart. An offline report retries the same signature/key on Check again; it never re-signs a transfer. Reporting is not credited as payment confirmation: GET status or the authenticated city-room confirmation remains authoritative. Cancellation returns to recoverable Pending, and a realtime confirmation arriving before wallet/report completion cannot reopen the finished payment. Sources: `Money/Logic/TopUpFlow.cs`, `Money/Net/MoneyApi.cs`.
- **Alpha calls:** entry, target and stop micros are now decimal JSON strings as required by the running route's Zod schema. Previously the UI always sent JSON numbers and the server rejected them. Sources: `Money/Logic/OrderTickets.cs:180`; backend `server/api/routes/alpha.ts:31`.
- **Prediction history:** Prediction Pit now reads all persisted personal bets from `GET /predictions`, including resolved statuses. Removed session-only receipts and the nonexistent `/predictions/{id}/bets` request path. The screen states that new predictions are unavailable. It does not turn existing bets into fabricated market questions or hide the contract mismatch behind an empty-market message. Sources: `Money/UI/PredictionPitApp.cs`, `Money/Logic/MoneyModels.cs:332`, `Money/Net/MoneyApi.cs:66`.
- **Jobs/gigs decoding:** a malformed response or a response containing only malformed rows is surfaced as an error, rather than successful empty availability. An actual empty array remains valid. Source: `Careers/Net/CareersApi.cs:39,64`.
- Ventures' `AdIntentGateway` and fixture gateway were updated by its owning agent to implement the new submission method.
- **Recovery follow-up:** removed `Resume()`'s 24-hour age discard for wallet-started intents. Signed and uncertain transfers remain recoverable until a server terminal response, even after their displayed expiry. `Reset()` cannot erase Signing/Confirming/Pending evidence; the Bank Pending screen keeps Check again and no longer offers the destructive New top-up shortcut. Regression tests resume 30-day-old signed and unknown-result intents, retain the reporting key and prove no wallet re-signing.

## Current upstream correction — `93dfabf`

The initial local server was behind Iyke's work. The following earlier gaps are **implemented upstream**, not remaining requests:

- Career XP/levels persist in `career_progress`; apply preserves existing progress, completion updates XP and promotion in the same transaction as pay, and subsequent shifts use the resulting level. Sources: `server/application/careers.ts`, `server/application/shifts.ts`, migration `1700000073000_career_progress.sql` and route tests in `server/api/progression.test.ts`.
- ORE reads decode the real Board, Round and Treasury accounts, cache a bounded last-good board, and return 503 outside local/mock when unavailable. With the reader enabled, wallet ORE and stORE balances are summed from mainnet SPL accounts. Sources: `server/application/refinery.ts`, `server/shared/solana/ore-board.ts`, related tests. This replaces the old program-account/zero-board stub; it is not a remaining blanket ORE-reader request.
- Market simulation now starts from the workers process, launches house coins, picks up player coins, ticks prices and announces deaths. Sources: `server/workers/market-sim.ts`, `server/workers/main.ts`, `server/application/market-engine.ts`. Running/deployed behavior still requires latest-instance validation.
- Top-up Solana Pay URL amounts were corrected from base units to user units. The **confirmed-status-before-expiry issue is still present** in `git show origin/main:server/application/topup.ts` at `getPaymentIntent`: it tests the timestamp before returning the stored status.

The remaining checklist below was narrowed against `93dfabf`. No frontend fixes in this lane were invalidated: the signature reporting route still exists, alpha still requires string prices, and predictions still return personal bets without a market catalog.

## Current upstream runtime verification — isolated `93dfabf`, localhost:4011

Root prepared `/Users/mac/Documents/codes/projects/ct-life-backend-audit` with isolated database/migrations and Redis DB15. The corrected protocol fixtures were copied to **`ct-life-evidence/backend-connectivity-20261012/money-current/`**; no historical receipts were overwritten. This is current local backend evidence, not hosted production or Seeker gameplay proof.

- `local-smoke.mjs`: **27 HTTP calls, 18 checks passed, zero failures**. Career apply, early-settlement rejection, timed shift completion, duplicate-key replay, persisted balance/progression readbacks, gig pay/replay and the ten read routes all passed.
- Progression is now verified working: start **level1/XP0**, completion **pay4000 / XP100 / promoted=true**, subsequent `/careers` **level2/XP0** because the first promotion threshold is100. Fictional Cash **300000 → 304000**, still304000 after replay. Gig pay adds425 exactly once.
- `market-smoke.mjs`: **20 HTTP calls, nine checks passed, one blocked sale**. The isolated market initially had no coins, so the fixture created one via the real launchpad endpoint; this is not claimed as worker-generated activity. Buy/debit/replay/position, corrected alpha create/history and perp open/read/close passed.
- The current server still rejected selling the bought999900 units: `SLIPPAGE_EXCEEDED`, expected1000micros, execution0, tolerance5000bps. A successful sell is still not established. The fixture did not loosen tolerance.
- Current local ORE fallback has **25 squares, round0**. Reader-disabled local data is a development fixture, not proof of live mainnet reads. Mainnet board/balance implementation is established by source inspection, with runtime mainnet/deployment verification still outstanding in this lane.

Receipts: `money-current/receipt-current-4011.json`, `money-current/market-receipt-current-4011.json`.

```sh
CTLIFE_LOCAL_PORT=4011 node /Users/mac/Documents/codes/projects/ct-life-evidence/backend-connectivity-20261012/money-current/local-smoke.mjs
CTLIFE_LOCAL_PORT=4011 node /Users/mac/Documents/codes/projects/ct-life-evidence/backend-connectivity-20261012/money-current/market-smoke.mjs
```

## Historical local verification — `eefa667` (not current upstream)

Evidence directory (outside Git): `/Users/mac/Documents/codes/projects/ct-life-evidence/backend-connectivity-20261012/money/`.

`local-smoke.mjs` is a bounded protocol integration fixture using a disposable `/auth/dev` account on **localhost:4010 only**. JWTs and generated wallet secret material stay in memory and are not emitted. It simulates a score after the server's real shift deadline; it does not claim that the human minigame was played.

- **27 HTTP calls; 17 assertions passed.** Real character creation; career apply; early shift rejection; timed completion; identical-key replay; gigs; ten Money/Careers reads.
- Fictional Cash: **300,000 → 304,000 cents** after server-confirmed shift pay of 4,000. Completion replay left it at 304,000. Gig pay added 425 once across an identical-key replay.
- The completion returned **100 XP**, but a subsequent `/careers` still returned **level 1 / XP 0**. This was a verified gap in **eefa667 only**, subsequently found fixed in upstream 93dfabf. It is no longer an outstanding progression request.
- `/market/coins`, positions, perps, prediction history, alpha calls, smart money, tombstones, gigs, ORE board and ORE position all returned HTTP 200. That establishes route reachability and response shape, **not production completeness**. Market returned two stored coins. ORE returned `dev-fixture`, five squares and zero balances.
- The first run's two failed assertions were fixture mistakes: an old Rain outfit no longer passed palette validation, and successful gigs return HTTP **201**. Corrected to a valid Snow look and 2xx handling, then reran the entire fixture. Preserved `receipt-first-run.json`; final evidence is `receipt.json`.

`market-smoke.mjs`: **18 HTTP calls; nine checks passed, one blocked sale**.

- Buy persisted a position and debited 1,000 fictional Cash cents once across same-key replay.
- Perp open, open-position readback and close succeeded.
- An alpha call with numeric prices returned HTTP 400 with `invalid_type`, expected string for all three prices. The corrected string-price body returned HTTP 200 and appeared in `/alpha-calls` history.
- Selling the bought position was **rejected** with `SLIPPAGE_EXCEEDED`: expected `1000000` micros, execution `51`, tolerance `5000` bps. This is **not a passed sell flow**. The script did not loosen slippage or fake success. Receipt: `market-receipt.json`.

Reproduce (no credentials in command lines):

```sh
node /Users/mac/Documents/codes/projects/ct-life-evidence/backend-connectivity-20261012/money/local-smoke.mjs
node /Users/mac/Documents/codes/projects/ct-life-evidence/backend-connectivity-20261012/money/market-smoke.mjs
python3 /Users/mac/Documents/codes/projects/ct-life-evidence/backend-connectivity-20261012/money/compile-owned.py
```

The owned Money/Careers assemblies compile with Roslyn against already-built Unity dependency assemblies. Original pure tests: **Money 121 passed, Careers 119 passed, zero failures** (`roslyn-results.txt`). After the recovery follow-up: **Money 124 passed, Careers 119 passed, zero failures** (`recovery-roslyn-results.txt`). This supplemental runner excludes `MarketReadStateTests` (native Editor panels); it does not replace root's Unity runner. Root reported **1793/1793 Unity tests passed before the recovery follow-up**; the three additional recovery cases still need its focused Unity rerun.

The older `Tests~/run_money_tests.sh` failed before reaching Money: its shared `compile_check.py` lacks current GLTFast dependency wiring for `LikenessAvatarLoader.cs` (`CS0246`, eight errors). No source dependency was removed to hide that harness failure. `compile-owned.py` instead uses current Unity-built dependency DLLs while recompiling all owned feature sources and pure tests.

Root Unity filters: `CTLife.Economy.Tests;CTLife.Careers.Tests`. Device gameplay and hosted production validation were not run by this agent.

## Feature matrix — updated against upstream `93dfabf` source

Paths in this table are under `client/Assets/Scripts/Features/` unless marked backend.

| Feature | Actual connection and ownership | Status / remaining boundary |
|---|---|---|
| Coin list, detail, positions | `MoneyApi.Coins/Coin/Positions` → GET `/market/coins`, `/market/coins/{id}`, `/market/positions`; `MarketFeed` consumes market room and falls back to real REST | Current isolated 93dfabf reads and post-buy position verified. Fallback is server data, not client-generated prices. Backend `server/realtime/rooms/market.ts` now publishes stored coin snapshots; old client comment saying it was an empty stub was corrected. Worker freshness not established by this pass. |
| Spot buys/sells | `PulseApp` → `MoneyApi.Trade` POST `/market/trades`; retained request key; `/me` refresh after receipt | Buy and replay passed on current isolated 93dfabf. Sell blocked by the observed price/slippage mismatch; see acceptance below. |
| Perps | `PerpPitApp` → GET/POST `/perps`, POST `/perps/{id}/close`; server margin, cap, receipts | Open/read/close verified on current isolated 93dfabf using fictional Cash. Liquidation worker and realtime delivery not exercised here. |
| Alpha calls | `PulseApp` → GET/POST `/alpha-calls`; server trust score | Numeric/string mismatch corrected and actual create/history verified. Resolver progression not exercised. |
| Smart money | `PulseApp` → GET `/smart-money`; only opted-in server trades | Read verified. Opt-in lives outside this owned feature surface; no synthetic leaderboard/trade rows added. |
| Tombstones | `PerpPitApp` → GET `/rekt/tombstones`; realtime liquidation listener | Read verified. Actual liquidation-to-tombstone end to end remains untested. |
| Prediction history | `PredictionView` → GET `/predictions`; persisted rows and statuses | Wired in this change. Local empty-history read verified; populated/resolved payloads tested with fixtures. |
| New predictions | Backend POST `/predictions` accepts outcomeId/stake/horizon; FE's old contract market catalog and nested-bet route do not exist | **Backend incomplete.** No user-writable market catalog or genuine outcome resolution. New bets explicitly unavailable in client. |
| Cash top-up | `BankApp`/`TopUpFlow` → intent create, signature submitted, status poll; wallet signs; server credits | Missing report connection repaired with restart/retry tests. No payment performed. USDC builder exists; SOL/SKR/ORE rejected by backend due missing price feed. Webhook/reconciliation readiness not established. |
| ORE board/position | `OreRefineryApp` → `/ore/board`, `/ore/me` | Upstream now decodes real board accounts and enabled-reader wallet ORE/stORE balances. Remaining: staked amount, city stake aggregate and price are still zero placeholders; actual live-reader deployment not verified by the old local fixture. |
| ORE stake | `OreRefineryApp` → POST `/ore/stake/tx` then wallet signer | Unsigned builder route exists. No signing attempted. Submitted endpoint requires an intentId which builder does not create/return. Actual confirmation/reconciliation missing. |
| Jobs list/apply | `CareersApi` GET `/careers`, POST `/careers/{id}/apply`; config joins server row values | Current isolated apply and promotion readback verified. Upstream persists per-career XP/level and apply records but still has no held/current-career field; skill lookup still returns10. Client confirmed-apply preference remains local, not cross-device job authority. |
| Paid shifts | `ShiftRunner` → POST `/shifts`, assigned game IDs, exact score results → POST `/shifts/{id}/complete`; `/me` refresh | Current isolated pay/replay and level1→2 promotion verified. Upstream persists XP/promotions atomically with pay; minigame interaction intentionally runs locally. Active assignments still live in a process map and do not survive server restart/another instance. |
| Career practice | `MiniGameRunner` local; explicitly practice, no paid settlement | Correct local simulation; no backend connection required for unpaid practice. |
| Gigs | `CareersApi` GET `/gigs`, POST `/gigs/{id}/run`; server ledger receipt and profile refresh | Current isolated ledger + replay verified. Backend instantly pays default performance inputs and returns future endedAt; no actual elapsed enforcement/cooldown. This is connected, but incomplete game behavior. |

## Backend acceptance checklist for Iyke

1. **Spot sell pricing:** inspect `server/application/terminal-cafe.ts:300–350`. Its reserves initialize quote values in cents, buy multiplies input by 10,000, sell divides output by 10,000, while execution price is compared in micros. [Inference] This unit mismatch explains the observed immediate buy/sell rejection. Acceptance: small round trips have consistent units and expected fees/slippage; arbitrary fixtures at several coin prices pass; no tolerance increase to mask an incorrect quote.
2. **Held job and real skill gates (XP persistence fixed upstream):** latest `server/application/careers.ts` persists progress and apply records, but `active` still means career offered and `getUserSkill` still returns10. Return an unambiguous held/currentCareerId if one active job remains the product rule; enforce actual server skills. Acceptance: account re-login/on a second device restores the same held job and gated careers reject insufficient skill. Do not ask Iyke to implement XP/level persistence again.
3. **Active shift persistence (XP/promotion fixed upstream):** latest `server/application/shifts.ts` still stores `_shifts` in a process Map. Acceptance: start on one instance and complete through another/restart without losing the assignment; retain the upstream atomic pay/XP update and duplicate protection; stored receipt recoverable after disconnect.
4. **Prediction catalog/resolution:** `server/api/routes/predictions.ts` exposes personal bets, not `PredictionMarket[]`; `server/application/predictions.ts:176` resolves one outcome using minute parity and all other IDs as false. Acceptance: server supplies market/question/outcomes/close-time metadata, validates outcome membership and market openness, resolves a real authoritative game event/feed, stores settlements and returns cross-session personal history. Client must not submit guessed outcomes into the current resolver.
5. **Top-up reconciliation:** `server/workers/payment-poller.ts` only calls `expirePendingIntents`; it does not inspect submitted signatures or recover missed webhooks. `server/application/topup.ts:154` supports USDC only; `getPaymentIntent` tests expiry before confirmed state. Acceptance: independently validate transfer mint/recipient/reference/amount/finality; missed webhook can reconcile; duplicate reporting/notifications credit once; a confirmed intent remains confirmed after its original expiry; supported currencies explicitly reflect available quote builders.
6. **Remaining ORE fields (board and wallet token reads implemented upstream):** latest `server/application/refinery.ts` explicitly leaves `staked`, `cityStakedOre` and `priceUsd` as zero placeholders; minerTier remains stored rather than derived by this read. Acceptance: decode the wallet stake position and expose actual aggregate/price when available or explicit unavailable fields; distinguish last-good board timestamp from fetch freshness. Validate deployed reader/mainnet configuration and actual wallet reads. Do not request a Board/Round/Treasury reader again.
7. **ORE submission contract:** `server/api/routes/ore.ts` `/stake/submitted` requires intentId; `/stake/tx` creates no tracked intent and returns no ID. Acceptance: return a durable operation ID when building, accept signature against owner/action/amount/network, read confirmation and refreshed balances; uncertain wallet results remain recoverable without duplicate signing. No client implementation can invent the missing intent.
8. **Gig timing/cooldowns:** `server/application/gigs.ts` uses `DEFAULT_INPUTS`, credits immediately, and sets endedAt 30 seconds into the future without awaiting or enforcing it. Acceptance: enforce server-owned availability/elapsed time and bounded results, return cooldown/nextAvailableAt on reads, reject early repeats independently of client buttons.

No backend files were changed by this lane. These are remaining implementation/verification requirements, not a claim that a reachable endpoint makes the full feature production-ready.
