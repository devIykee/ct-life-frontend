# ORE / SKR client integration audit

Date: 5 October 2026. Contract: `1.0.0-rc1`.

This is a read-only audit of repository artifacts and existing client code. No financial feature, contract, generated model or wallet implementation was changed. No payment, wallet approval, chain transaction or live backend request was performed. Current on-chain deployments, mint decimals and backend availability were not independently re-verified in this pass; values below are identified as contract/config values.

## Result

- The contract and generated C# models already cover Cash top-up intents, status polling, the ORE board, ORE position and four staking transaction builders.
- `Api.cs` has no wrappers for those five endpoints. ORE and Top up are still `ComingApp` registrations.
- Read-only board/position UI can be implemented against the existing models. Money-moving UI needs the cluster/session and confirmation decisions below before it can be represented as working.
- Preserve the original product scope: ORE is featured in the Refinery, board, staking and payments; SKR is a first-class payment currency. The board does not accept mining wagers. This scope comes from `docs/spec/build-spec.md:316`, `:326`, `:327`, `:328` and `:242`.

## Exact endpoint and model map

Paths are relative to a server base already ending in `/v1` (`contract/openapi.yaml:25`). All five endpoints inherit bearer authentication (`contract/openapi.yaml:60`). Both POST endpoints require `Idempotency-Key`; the contract specifies UUIDv4, 24-hour replay, serialized concurrent requests and rejection when one key is reused with a different body (`contract/openapi.yaml:2598`).

| Endpoint | Request | Response | Source |
| --- | --- | --- | --- |
| `POST /payments/intents` | `kind: topup/cosmetic/ad/season_pass`, `sku`, `currency: USDC/SOL/SKR/ORE` | `PaymentIntent` | `contract/openapi.yaml:2050` |
| `GET /payments/intents/{id}` | Intent UUID in path | `status: pending/confirmed/expired/failed`; nullable `signature`; HTTP 410 `PAYMENT_EXPIRED` | `contract/openapi.yaml:2126` |
| `GET /ore/board` | None | Round ID, UTC end time, end slot, exactly 25 squares, motherlode, city stake and USD price | `contract/openapi.yaml:2325`, `:3130` |
| `GET /ore/me` | None | `balance`, `staked`, `stOre`, `minerTier` | `contract/openapi.yaml:2346`, `:3149` |
| `POST /ore/stake/tx` | `action: deposit/withdraw/claim/compound`; base-unit `amount` required for deposit/withdraw | Only `{tx}`, a base64 v0 transaction | `contract/openapi.yaml:2364` |

Existing generated types are under `client/Assets/Generated/Rest/src/CTLife.Rest/Model/`:

- `PostPaymentsIntentsRequest.cs:38`: kind enum; `:75`: currency enum; `:119`: constructor `(kind, sku, currency)`.
- `PaymentIntent.cs:91`: constructor; `:136`: `Guid IntentId`; string recipient, mint, amount, reference, memo, transaction and Solana Pay URL; `:161`: `long CashCents`; `:167`: `int BonusBps`; `:197`: `DateTime ExpiresAt`.
- `GetPaymentsIntentsId200Response.cs:38`: status enum; `:81`: constructor `(status, signature)`.
- `OreBoard.cs:84`: `int RoundId`; `:90`: `DateTime EndsAt`; `:96`: `int EndsAtSlot`; `:102`: `List<OreBoardSquaresInner>`; motherlode, city stake and price remain strings. `OreBoardSquaresInner.cs:57` stores `DeployedLamports` as a string.
- `OreMe.cs:38`: tier enum (`none`, `prospector`, `miner`, `refiner`); `:83`: constructor with string balance/staked/stORE fields.
- `PostOreStakeTxRequest.cs:38`: action enum; `:81`: constructor `(action, amount)`.
- `PostOreStakeTx200Response.cs:58`: only `string Tx`.

Use the generated types. The existing transport already serializes typed request bodies, provides bearer headers and preserves one idempotency key during transport retries (`client/Assets/Scripts/Net/Rest/RestClient.cs:44`). A UI-level retry/resume must also retain the same operation key when repeating the same intent creation.

## Verified existing client boundaries

- The endpoint wrapper class is `client/Assets/Scripts/Net/Rest/Api.cs:14`. None of the five methods above exists there yet.
- ORE and Top up are registered as phase-2 placeholders in `client/Assets/Scripts/App/PhoneCatalog.cs:22` and `:23`.
- `PhoneShell.CurrentPhase` defaults to 1 (`client/Assets/Scripts/UI/Phone/PhoneShell.cs:41`); the App code does not currently assign it. Availability depends on phase and degraded feature flags (`:110`, `:136`). Advancing phase globally would also make the still-placeholder Pulse/Perps/Predict entries look available. Proposed integration: distinguish implemented apps from placeholders before unlocking these two surfaces.
- `Features.Payments` combines Helius, price-feed and ledger-freeze availability; ORE currently uses `Features.OreReader` (`client/Assets/Scripts/Core/Data/Features.cs:33`; `client/Assets/Scripts/App/PhoneCatalog.cs:22`). A disabled payment gate must be rechecked at the action, including while an app is already open.
- The city channel already publishes `PaymentConfirmedMessage` on the bus (`client/Assets/Scripts/Net/Realtime/Channels.cs:25`). The payload is `intentId` plus `long cashCents` (`client/Assets/Generated/Support/RealtimeMessages.cs:30`).
- `PlayerProfile.Refresh()` loads authoritative `/me` and publishes `ProfileChangedSignal` (`client/Assets/Scripts/Net/Auth/PlayerProfile.cs:35`). There is no payment-event refresh subscriber in the current client. Proposed integration: refresh after confirmation; do not add a delta to cached Cash and risk counting a poll and event twice.
- `.skr` identity is already available as server data and displayed in the profile (`client/Assets/Scripts/UI/Phone/Apps/CoreApps.cs:193`). SKR support does not require a separate balance or naming endpoint for this scope.

## Proposed top-up state machine

The polling interval and pending behavior are specified in `docs/spec/contract.md:285`–`:289`. The remaining UI transitions below are implementation recommendations, not additional backend promises.

1. **Loading catalogue** → fetch/read config packs and currencies; show loading, unavailable or selectable packs.
2. **Choose pack/currency** → show supplied pack labels/base Cash and eligible currencies. ORE has the featured treatment; SKR is an explicit peer choice. Config bonuses are ORE 1500 bps and SKR 1000 bps (`contract/config/economy.json:12`). Do not calculate the final credited Cash from these numbers.
3. **Creating quote** → send `POST /payments/intents` only for the chosen pack/currency. Hold the operation's idempotency key and disable duplicate requests.
4. **Review quote** → show the exact server-returned token amount, currency, Cash credit, bonus and expiry. `cashCents` and `bonusBps` must be shown from the intent, not recomputed (`docs/spec/contract.md:285`). Distinguish real-wallet payment from game Cash. Missing/invalid required top-up values are an unavailable quote, not a zero-value purchase.
5. **Awaiting wallet** → an explicit user action invokes the signer with the server transaction. Wallet decline returns to review if the quote remains valid. An expired quote needs a fresh intent. Do not auto-sign a replacement quote.
6. **Checking payment** → after submission, poll the existing intent every 1 second for up to 30 seconds. A returned wallet signature is submission evidence; the payment status is the credit confirmation.
7. **Confirmed** → match the intent ID from polling or `payment_confirmed`, deduplicate completion, refresh `/me`, then show the resulting receipt/credit once.
8. **Payment pending** → after the 30-second window, retain the intent ID and submitted signature. Keep the operation recoverable across closing/reopening the phone and an app restart. Recheck the existing intent before offering another payment when submission outcome is unknown.
9. **Expired / failed / unavailable** → show specific recoverable states. `PAYMENT_EXPIRED`, `TOPUP_CAP_REACHED`, no connection, unavailable quote service, no wallet session and wallet decline are different outcomes. Never convert a transport timeout into a success or immediately resend another transfer.

Backend ownership remains: quote, transfer construction, mint/recipient/reference/memo checks, chain verification, ledger credit and replay protection. Those checks are specified in `docs/spec/contract.md:421`. The client displays the quote and asks the wallet to sign; it does not build a replacement transfer or credit Cash.

## Proposed ORE surface and state machine

The required product surface is the Refinery terminal plus a read-only live board (`docs/spec/build-spec.md:326`–`:330`). A phone ORE app can expose the same state; it is not the whole 3D venue feature.

- **Loading**: load board and personal position independently. A failed board request need not erase an already fetched position.
- **Live board**: exactly 25 display-only squares, round identifier, server end-time countdown, motherlode, city stake and server price. Refresh no faster than the contract's 2-second cache interval (`docs/spec/contract.md:295`). No deposit-to-square control, simulated mining result or invented winning square.
- **Stale / unavailable**: keep the last valid values explicitly marked as stale, or show unavailable before the first valid response. Do not generate fresh totals/timers. Do not pad the invalid two-square example into a supposedly live board.
- **Position**: display server balance, staked amount and miner tier. Tier and hard-hat eligibility follow authoritative profile/realtime state, not a client threshold calculation.
- **Stake / unstake**: enter a positive decimal amount with validated token precision, review the real ORE action, then ask the backend for its transaction. Unit/denomination questions below must be resolved before accepting withdrawal amounts.
- **Claim / compound**: omit amount in the existing request shape. The response has no claimable reward quote; do not invent one or promise a yield/APR.
- **Awaiting wallet**: explicit approval of the server transaction, on the correct network and signed-in wallet.
- **Submitted**: preserve the returned signature and show pending verification. A wallet submission response must not be labelled “staked,” “claimed” or “compounded.”
- **Verified result**: after an agreed chain-confirmation route succeeds, refresh `/ore/me` and `/me`; world tier/hard-hat updates follow backend state. A position change alone is not a receipt identifying this transaction.
- **Declined / expired transaction / submission unknown / failed**: retain enough state to distinguish retrying a request from sending a second transaction. No automatic signing retries.

Stop screen-specific polling on close/background; restore a fresh read on return. Pending money operations need a longer-lived coordinator than a disposable phone page. This is a proposed client lifecycle choice, not a new endpoint.

## Blockers and missing decisions before wallet actions

| Item | Verified evidence | Required next decision |
| --- | --- | --- |
| Per-operation network | Staging config uses devnet (`client/Assets/Scripts/Core/Env/EnvConfig.cs:75`). `MwaWallet.EnsureWeb3()` binds the SDK once to that environment cluster (`client/Assets/Scripts/Solana/MwaWallet.cs:58`). `ITransactionSigner` accepts only transaction and cancellation token (`client/Assets/Scripts/Core/Wallet/WalletContracts.cs:56`). ORE payments/staking must use mainnet even on staging (`docs/spec/contract.md:53`). | Add an explicit, verified cluster/session route for the operation. Do not pass mainnet ORE transactions into the existing staging devnet session. Payment/stake responses currently contain no cluster field. |
| Wallet session and account | Signing requires an existing `SolanaWalletAdapter` (`client/Assets/Scripts/Solana/MwaWallet.cs:121`). A JWT refresh or review/dev profile does not create that adapter. | Define wallet reconnect and signed-in-account match behavior without silently replacing the user's identity. |
| v0 transaction round-trip | The signer deserializes `VersionedTransaction` then passes it through a `Transaction[]` SDK API. Its own comment marks round-trip support unverified (`client/Assets/Scripts/Solana/MwaWallet.cs:113`). | Verify serialization/submission with the installed SDK and actual wallet before claiming transaction support. |
| Stake confirmation | The stake builder response contains only `tx` (`contract/openapi.yaml:2394`; generated `PostOreStakeTx200Response.cs:58`). There is no stake status/receipt endpoint or expiry/last-valid-block-height field. | Agree on explicit signature verification and expiry handling: client RPC with a known network, or an additive backend receipt/status contract. Until then, only “submitted/pending” is substantiated. |
| stORE display and withdrawal units | `OreMe.stOre` is a string (`contract/openapi.yaml:3155`); `economy.json` defines ORE decimals but no stORE decimals. Stake `amount` only says “Base units” (`contract/openapi.yaml:2381`). | Confirm stORE precision and whether withdrawal amount means ORE principal or stORE units. Do not infer they share decimals/denomination. |
| SKR environment | Config has `devnetMint: null` and explicitly limits SKR top-up to production until verified (`contract/config/economy.json:24`). | Keep SKR selectable in eligible production configuration; give staging a clear unavailable state. Do not silently substitute a token. |
| Reward amounts and fees | ORE position contains only balance/staked/stORE/tier. Stake response only contains transaction. | Omit claimable reward/APR/estimated fee promises unless a verified source is added. The wallet remains the transaction review surface. |

## Contract/config versus product-spec discrepancies

These are local artifact inconsistencies, not claims about current chain state. Do not edit backend-owned contract/config to resolve them unilaterally.

| Topic | Contract/config evidence | Other artifact / implication | Client behavior pending resolution |
| --- | --- | --- | --- |
| Daily top-up cap | `contract/config/economy.json:3` is `1000000`; `contract/config/economy.schema.json:7` defines the field in **USDC cents**. That equals 10,000 USDC. | Product spec says 100 USDC (`docs/spec/build-spec.md:243`); API error example also says 100 USDC (`contract/openapi.yaml:2107`). | Server enforces the cap; avoid publishing a numeric cap from conflicting data. Ask BE which value/unit is intended. |
| Prospector threshold | `contract/config/ore.json:5` sets `minOre: 1`. | Product spec says holds ORE (`docs/spec/build-spec.md:329`); contract prose says holds **any** ORE (`docs/spec/contract.md:501`). | Use returned `minerTier`; do not calculate an entitlement or display an unconfirmed threshold. |
| Board example shape | Example has two squares (`contract/openapi.yaml:2341`). | Schema requires exactly 25 (`contract/openapi.yaml:3138`). | Treat malformed data as unavailable; correct fixture needed for contract/mock testing. |
| Motherlode example units | Example `motherlodeOre: '55000000000'` (`contract/openapi.yaml:2342`); config ORE decimals are 11 (`contract/config/economy.json:31`). | This represents **0.55 ORE** in the specified base-unit convention. Product research snapshot says 55 ORE (`docs/spec/build-spec.md:309`). Those are not the same amount; a research snapshot is not a current price/board assertion. | Format actual returned base units; never copy the old 55 ORE snapshot into a live screen. Ask BE to clarify/correct the example. |
| Cash fields optional in schema | `PaymentIntent` required list excludes `cashCents`/`bonusBps` (`contract/openapi.yaml:3079`); generated fields are non-nullable numbers defaulting to zero. | Contract prose says both are set for top-ups (`docs/spec/contract.md:285`). | Require a valid top-up quote before enabling payment; do not interpret a missing Cash credit as a valid zero-Cash pack. |
| “Best value” pack | Config marks Whale (700,000 Cash cents for 10 USDC) best value (`contract/config/economy.json:8`). Cabal supplies 1,600,000 Cash cents for 20 USDC (`:9`). | Cabal has the higher base Cash-per-USDC ratio: 80,000 versus 70,000 cents. | Do not invent another badge; ask product/BE whether the configured label is intended as “popular” or whether pack economics change. |

## Suggested implementation split after the current UI capture

- **Root / Net / Core / Solana:** five typed API wrappers; operation/session/network routing; pending-operation lifecycle; event/poll deduplication; verified signer and stake-confirmation route; update phone catalogue readiness.
- **UI:** ORE board/position/status views; top-up catalogue/currency choice; exact quote review; decline/error/pending/receipt states; centralized copy and amount formatting with explicitly configured units.
- **World owner:** Refinery 3D board/terminal, furnace state, authenticated miner-tier/hard-hat presentation and venue interaction with the same data source.
- **Backend owner:** deployed endpoint behavior, correct quote/units/caps, transaction builders, on-chain verification and ledger/tier updates. Resolve the questions above in the contract rather than independent FE guesses.

## Verification needed for the later implementation

- Typed JSON round-trip for all four payment currencies, four stake actions and four intent statuses; Cash values above 32-bit range remain `long`.
- Read-only fixtures for all 25 board squares, zero balances, large base-unit strings, stale/offline requests and each miner tier.
- Quote expiry, wallet decline, double tap, timeout after submission, page close/reopen, application restart, and poll/event arriving in either order.
- A real wallet verifies the intended network/account and preserves the backend-built v0 transaction. Separate submission from chain confirmation in visible status.
- Payment confirmation refreshes authoritative Cash once. Stake confirmation refreshes position and tier; another client sees the expected hard hat.
- No execution from review/demo data, unavailable feature gates, or an unsupported currency/network configuration.

No item in this final verification list was run as part of this audit.
