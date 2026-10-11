READY TO COMMIT: client/Assets/Scripts/Features/Money/ client/Assets/Resources/UI/Money/ docs/progress/phase2-client.md docs/progress/requests.md — Phase 2 money client: Bank top-ups (payment intents + MWA + polling), Pulse trading/Launchpad/positions/tape/smart money/alpha calls, Perp Pit + Rekt Clinic, Prediction Pit, ORE Refinery, REKT card, 111 EditMode test cases

(Unity generates the `.meta` files on first import; commit them with this. The apps are not reachable until the
integration lead applies `docs/progress/requests.md` → "2026-10-07 phase2-client -> integration lead".)

# Phase 2 client (Money and markets) — progress log

Owner: phase2-client agent. Scope: `client/Assets/Scripts/Features/Money/` (assembly `CTLife.Features.Money`,
C# namespace `CTLife.Economy`), `client/Assets/Resources/UI/Money/`, this log, requests in `docs/progress/requests.md`.
Nothing outside those paths was edited. The Unity editor and adb were not used.

## Status (2026-10-07)

Built, compiling (player, editor and release passes of `client/Tools~/compile_check.py`: 0 errors, 0 warnings in
`CTLife.Features.Money`), and the logic is covered by **111 passing NUnit cases** (run out of the editor, see
"Verify"). **Not yet run in the Unity editor, on the Seeker, or against a live backend** — the integration hooks are
pending, and several backend endpoints do not match the contract (list below). UI is functional, not styled.

## What is built

| Feature (spec #) | Phone app (id) | Endpoints / realtime |
| --- | --- | --- |
| #13 Cash top-ups USDC/SOL/SKR/ORE | `MoneyBankApp` (`bank`, Phase 1; top-up section from Phase 2) and `MoneyBankApp(topUpTile: true)` (`topup`) | `POST /payments/intents`, `GET /payments/intents/{id}`, MWA `sign_and_send` via `ITransactionSigner`, city `payment_confirmed` |
| #14 Terminal Café trading | `PulseApp` (`pulse`) tabs Market / Positions / Tape + coin page | market room `coins` (+ REST fallback `GET /market/coins?stage=`), `GET /market/coins/{id}`, `POST /market/trades`, `GET /market/positions`, market `tape` |
| #15 Launchpad Tower board | `PulseApp` tab Launchpad | `new` + `final` coins, graduation threshold and launch interval from config `market` |
| #16 Perp Pit + liquidation | `PerpPitApp` (`perps`) tabs Trade / Open / History | `POST /perps`, `GET /perps?status=open`, `GET /perps`, `POST /perps/{id}/close`, city `liquidated`; `MoneyRektCard` overlay |
| #17 Rekt Clinic tombstones | `PerpPitApp` tab Rekt Clinic | `GET /rekt/tombstones?limit=50` |
| #18 ORE Refinery client | `OreRefineryApp` (`ore`) | `GET /ore/board` (2 s), `GET /ore/me` (15 s), `POST /ore/stake/tx` + MWA |
| #19 Prediction Pit | `PredictionPitApp` (`predict`) | `GET /predictions?status=open`, `POST /predictions/{id}/bets` |
| #20 Alpha Calls + Trust Score | `PulseApp` tab Alpha calls + "post a call" on the coin page | `GET /alpha-calls[?userId=]`, `POST /alpha-calls` |
| #21 Smart Money | `PulseApp` tab Smart money ("Copy" opens the coin page with the side preset; "Trust score" shows that trader's calls) | `GET /smart-money` |

18 of the contract's Phase 2 client operations are wrapped (`Net/MoneyApi.cs`, list in `MoneyEndpoints.Covered`).
Not called on purpose: the backend-only `POST /market/launchpad*`, `/payments/intents/{id}/submitted`,
`/ore/stake/submitted`, `/me/smart-money` (not in the contract).

### How it behaves
- **Server-authoritative.** Every amount shown comes from a response: intent `cashCents`/`bonusBps`/`amount`,
  trade fills, `unrealizedCents`, perp `entryMicros`/`liqMicros`/`realizedCents`, `payment_confirmed.cashCents`.
  Cash balance is re-read with `GET /me` (coalesced, ≤ 1 per 2 s) after any write or confirmation; it is never
  adjusted locally. Display-only ratios of server values: position return % (unrealized / cost basis) and the
  Launchpad progress bar (mcap / config `graduateMcapCents`). Leverage cap is UI pre-validation from config
  `skills` (the server enforces it and its `LEVERAGE_NOT_ALLOWED` details are shown).
- **Idempotency.** `RetainedWrite` binds one UUIDv4 key to one exact request body. Unknown outcome (network loss,
  timeout, 5xx, unreadable reply) → the same request retries with the same key (server replays). Definitive answer →
  the key is retired, so an identical next order is a new order. A changed body always gets a new key.
- **Top-up state machine** (`Logic/TopUpFlow.cs`): Choose → Creating → Review → Signing → Confirming (poll every 1 s
  for 30 s) → Confirmed | Pending | Expired | Failed | Error. `payment_confirmed` closes it from any stage. Declined in
  wallet → back to Review (same intent). Wallet error after a possible broadcast → polls, and never re-signs that
  intent. Intents expiring within 5 s are not signed. An intent without `tx` is not signable (Solana Pay link shown
  for copy). A 404 on the status route → Pending, relying on `payment_confirmed`. The in-flight intent (ids and
  display values only) is persisted per env+user in PlayerPrefs once the wallet opens, so a Pending payment survives
  the app being killed during MWA; it resumes and re-polls when the Bank opens.
- **Devnet / mainnet.** `TopUpCatalog.Availability`: mock env (no cluster) → payments unavailable; devnet → USDC/SOL
  test payments; SKR unavailable (no verified devnet mint); ORE always mainnet. Any mainnet payment needs an explicit
  "I understand this spends real X" toggle before "Pay with wallet" enables. ORE staking: permanent mainnet warning,
  an acknowledgement bound to the exact typed amount (editing the amount withdraws it), "Prepare transaction", then a
  second separate "Sign in wallet" tap; built transactions are discarded after 60 s `[Inference: blockhash lifetime]`.
  I never triggered any transaction (no device, no editor).
- **Live data.** Market room `coins` when joined and non-empty (snapshot of `State.coins` + add/change/remove
  events), otherwise REST polling every 4 s with backoff to 30 s (`PollGate`). Positions refresh every 5 s and on
  each market tick while visible ("live PnL" = the server's `unrealizedCents`). Perps open 5 s, history 20 s,
  tombstones 30 s, smart money 15 s, alpha calls 30 s, predictions 15 s, ORE board 2 s, ORE me 15 s. Stale data is
  kept on screen with its age; errors show inline with the server's code mapped to copy (`Logic/MoneyErrors.cs`).
- **Degraded flags** (`GET /config degraded`): `payments` composite, `markets` composite (orders disabled),
  `smart_money`, `ore_reader` are honoured.
- **Defensive parsing** (`Logic/Wire.cs`, `Logic/MoneyModels.cs`): accepts the contract shape and the backend's
  variants (string bigints, wrapped lists, epoch-ms times); malformed rows are counted and reported, never crash.
  Server ids that go into URL paths are restricted to `[A-Za-z0-9_-]`.

## Files

- `Logic/` (pure, no UnityEngine calls; what the tests cover): `Wire`, `TokenUnits` (BigInteger base-unit
  format/parse/compare), `MoneyFormat`, `MoneyModels` (+ `MoneyParse`), `TopUpCatalog`, `TopUpFlow`, `OrderTickets`
  (`RetainedWrite`, `AmountInput`, `Tickets`), `MoneyErrors`, `MarketLogic` (`LeverageCap`, `PnlCardData`,
  `RektCardModel`, `OreView`, `PollGate`).
- `Net/`: `MoneyApi` (HTTP wrapper on `IRestClient`), `MarketFeed` (room/REST coins, ticker cache, tape),
  `MoneyHub` (shared per phone: API, feed, top-up flow, `payment_confirmed` toast + profile refresh).
- `UI/`: `MoneyUi` (builders, `MoneyView` lifecycle base, `MoneyApp` base, `Sparkline`, `ChipTabs`), `BankApp`,
  `PulseApp`, `PerpPitApp`, `PredictionPitApp`, `OreRefineryApp`, `MoneyRektCard` (+ `MoneyApps.Create()`),
  `MoneyStrings` (all copy).
- `Tests/` (EditMode, asmdef `CTLife.Features.Money.Tests`): `TopUpFlowTests` (17), `ParseTests`/`WireTests` (13),
  `TicketAndLogicTests` (32 incl. TestCases), `TokenUnitsAndFormatTests` (49 incl. TestCases) → 111 executed cases.
- `Tests~/` (ignored by Unity and compile_check): out-of-editor test harness.
- `client/Assets/Resources/UI/Money/money.uss`: layout-only rules on the existing theme.

## Verify

```sh
python3 client/Tools~/compile_check.py                 # player pass: CTLife.Features.Money ok, 0 warnings
python3 client/Tools~/compile_check.py --editor        # editor pass: ok
python3 client/Tools~/compile_check.py --release       # CTLIFE_PROD pass: ok
client/Assets/Scripts/Features/Money/Tests~/run_money_tests.sh   # → PASSED 111  FAILED 0
#   (add --head-deps if someone's uncommitted work breaks Core/Net/UI/World)
```
In the editor (reference runner): Window → General → Test Runner → EditMode → `CTLife.Features.Money.Tests`.
Last run 2026-10-07: all three compile passes clean; harness `PASSED 111 FAILED 0` against the working tree.

## Requests made (docs/progress/requests.md, 2026-10-07 phase2-client -> integration lead)

1. Add `CTLife.Features.Money` to `CTLife.App.asmdef`.
2. `PhoneCatalog`: register `MoneyBankApp`, `PulseApp`, `PerpPitApp`, `PredictionPitApp`, `OreRefineryApp`,
   `MoneyBankApp(topUpTile: true)` in place of `BankApp`/`PerpsApp`/the four `ComingApp`s.
3. Raise `PhoneShell.CurrentPhase` to 2 in `FlowController.BuildHudAndPhone`.
4. Replace `RektCard` with `MoneyRektCard(ui.ModalLayer, id => _phone?.Open(id))`.
5. `ConfigStore.Normalize`: also merge `economy`, `skills` (and `lottery`, `needs`) from a live `/config`.
6. Add the test asmdef to `compile_check.py`.
Plus: rejoin city/market (in progress on the lead's side), venue-terminal → app mapping, and forwarding the backend
mismatches below to `backend-dev-feedback.md`. Namespace warning for other agents: `CTLife.Features.*` shadows
`CTLife.Core.Features`, `CTLife.Money` shadows `CTLife.Core.Money` (CS0234 in every referencing assembly).

## Backend vs contract (ct-life-backend 9f3e5ef, read-only)

Blocking for Phase 2 end-to-end:
1. `POST /payments/intents` returns no `tx` (contract: required), no `cashCents`, `bonusBps`, `currency`. `amount`
   is derived from the pack's Cash (`cashCents/100`) and printed as a decimal string, not base units (Whale →
   `"0.007000"` instead of `"10000000"`); SOL/SKR/ORE get no live quote. (`server/application/topup.ts`)
2. `GET /payments/intents/{id}` does not exist (only the non-contract `POST …/{id}/submitted`).
3. The city room never sends `payment_confirmed` or `liquidated`; the market room state is `{ts, placeholder}` (no
   `coins`) and never sends `tape`. (`server/realtime/rooms/city.ts`, `market.ts`, `realtime/schemas.ts`)
4. `POST /ore/stake/tx` returns `{txBase64,…}` (contract `{tx}`), and the bytes are `serializeMessage()` of a
   legacy transaction without fee payer or blockhash, not a serialized v0 transaction; MWA cannot sign it.
5. Predictions are a different API: `POST /v1/predictions {outcomeId, stakeCents, horizon}` and `GET /predictions`
   returns the player's bets, not `PredictionMarket[]`; there is no market list, so the Pit shows none.
6. `POST /alpha-calls` requires `entryMicros/targetMicros/stopMicros` as digit strings; the contract (and this
   client) send integers → `VALIDATION_FAILED`.
7. `GET /ore/board` and `/ore/me` are fixtures/zeros (5 numeric squares, epoch-ms `endsAt`, numeric `priceUsd`).
Shape differences the client tolerates: bigint fields as strings everywhere; `{positions|perps|tombstones|trades}`
wrappers instead of bare arrays; candles `{at, priceMicros}` vs OHLCV; `riskHint` object vs string; holders without
`userId/pct`; trade receipt `{cashDeltaCents, realizedCents, newUnits}` without `feeCents`/`cashCents` (and no fee
charged despite `market.json feesBps`); tombstone `lostCents` vs `lossCents`; smart money `cashCents` is a signed
ledger delta; `POST /perps` answers 201, allows leverage 25 (contract max 20) and returns `FORBIDDEN
insufficient_margin` instead of `INSUFFICIENT_CASH`; `LEVERAGE_NOT_ALLOWED.details` has `allowed` (contract `max`).
Config: `topUpCapCentsPerDay = 1,000,000` is compared with credited Cash cents, i.e. $10,000 Cash per user per day,
so the Cabal pack (1,600,000) can never be bought — the spec says "100 USDC per wallet per day".

## Gaps vs the spec / not done

- 3D venue side (Terminal Café terminals, Refinery giant board and furnace, tombstone props, hard hat on avatars,
  liquidation animations beyond `react_liquidated`) is World scope; terminal → app mapping is in the request.
- Share cards: PnL and REKT "share" copy a text summary to the clipboard; no image export.
- Prediction "my bets" is session-local and resolution/payout is not shown (no contract endpoint).
- Solana Pay fallback is a copyable link, no QR.
- Cosmetic / ad / season-pass intents (`kind` ≠ `topup`) are Phase 4/5; `TopUpFlow` only sends `topup`.
- `[Unverified]` `MwaWallet.SignAndSendBase64` (existing) on a real Seeker; the full top-up and stake flows are
  unverified end to end until the backend items 1–4 land.

## Next

- After the lead wires the hooks: run in the editor against the local docker backend (dev auth), check every app's
  loading/empty/error states, then on the Seeker against staging for a 1 devnet-USDC top-up (integration checklist).
- PlayMode/UI tests for the views once the app is reachable in the real flow.
