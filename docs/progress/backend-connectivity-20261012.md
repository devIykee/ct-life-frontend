# CT Life — backend connection acceptance, 12 October 2026

**Not complete.** This audit repairs confirmed frontend mismatches and records the remaining server work. A successful HTTP response, a rendered phone screen, or passing client tests does not certify a feature's full lifecycle.

## Revisions and evidence

- Frontend starting revision: `787f2479`, branch `dev`.
- The existing local backend on port 4010 was `eefa667` plus the uncommitted likeness work. Its files and database were preserved.
- Fetched current upstream **`93dfabf`**, including Iyke's newer career progression, inbox, business catalog, market worker and likeness deployment commits. Tested it in a separate worktree, **new database and separate Redis database**, on ports **4011/2568**. Current receipts are explicitly separated from the older local receipts.
- Evidence root outside Git: `/Users/mac/Documents/codes/projects/ct-life-evidence/backend-connectivity-20261012/`.
- Railway API and realtime health/readiness responded HTTP 200. This is an availability check, **not authenticated hosted acceptance**. No hosted data was changed, real wallet was charged or generation credit was spent.
- Iyke's Telegram deployment update reports likeness commits `d362d0a` + `93dfabf` deployed, migrations renumbered 075–079 and feature flags off. Current upstream source confirms separate private-bucket credentials. Logged-in hosted generation/payment/delivery acceptance remains outstanding.

## Connected, repaired, and still missing

All paths below are relative to `/v1`. Source, request shapes, exact server requirements and reproduction receipts are in the three lane reports linked below.

| Area | Established connection | Remaining acceptance |
|---|---|---|
| Identity / onboarding | Real auth, profile, lottery and character REST; server-stored choices. | Hosted SIWS and second-device/reinstall verification are separate from local developer auth. |
| City / venues / NPCs | Colyseus city, market and venue channels; other players, NPC state, movement, chat and emotes. | Public travel no longer silently substitutes an offline room after failed admission. Private-home multiplayer still needs a complete server contract. |
| Profile / needs | `/me`, `/needs/actions`; fractional server values now decode correctly into the legacy whole-percent display. | Server action cost/cooldown/venue checks and elapsed-decay handling still need completion. |
| Jobs / shifts / gigs | Current server pays fictional Cash once under replay; career level 1→2 promotion persists. | Held-job identity/skill gates, durable active shifts across server restart, actual gig duration and cooldowns. |
| Spot market / perps / alpha | Buy, positions, duplicate protection, perp open/read/close, and alpha create/history verified on current server. Alpha price fields corrected to digit strings. | Spot sale round trip is rejected by server pricing/slippage. Worker-driven liquidations/settlement need full runtime acceptance. |
| Predictions | Persisted personal history now reads the actual `/predictions` shape. | The market catalog, accepted outcomes and authoritative resolver are missing; new bets must not use invented questions/odds. |
| Bank / Cash top-up | Signed transaction reporting now calls `/payments/intents/{id}/submitted` and retains signature/request key across retries. | Chain reconciliation, missed-webhook recovery and confirmed-versus-expired status precedence remain server blockers. No financial completion was tested. |
| Wallet / ORE | Wallet display intentionally reads Solana RPC. Current upstream includes real ORE board and ORE/stORE account readers. | Mainnet wallet/board runtime proof; stake operation ID/submission/confirmation; remaining stake/price/aggregate placeholders. Local ORE fixture is not mainnet evidence. |
| Timeline / DMs | Posts, follow feed, DM send/readback, latest inbox index and block exclusion verified. | Cross-device follows/blocks readback requires explicit authoritative endpoints. |
| Spaces | Create, hand raise, role updates, list/readback and end persist. | **RTC permission and room-identity defects remain.** Correct server role-derived grants and actual two-device audio acceptance are required. Client never upgrades itself to speaker when the token route is absent. |
| Free calls | Corrected `video` RTC kind; grant issuance, server hangup and refusal after end verified. Per-call cancellation protects a subsequent call from stale callbacks. | Actual two-device media/ringing and interrupted-network acceptance. A token response alone is not proof of media. |
| Paid calls | Client escrow flow exists. | Server creation/settings/price/escrow contract differs materially. Do not substitute caller-selected pricing for creator/server pricing. |
| Homes | Server catalog, owned-home local art and current-home renewal UI. | Move is not renewal: non-renewal writes are now blocked before debit. Server-authorized visits, movement, visitor settings and gallery remain incomplete. |
| Shop / businesses | Shop/inventory reads; current upstream business catalog + owned collection aligns with client, with current readback verified. | Purchase/equip and full business buy/accrue/collect/sell/restart acceptance still needed. |
| Week / boards / league | Clue, Meta, leaderboards and tribe endpoints. Correct tribe POST route now persists. | Missing active-events, district-standing/season-result paths. Week now displays unavailable/retry instead of invented quiet/zero mood. Hosted phase policy also needs an explicit server value. |
| Projects / VC | Create, same-key replay, list/detail persistence; corrected treasury/member response fields. | Name/ticker in detail, treasury funding, consent-based hiring offers, quoted launch/coin creation, HQ and VC lifecycle. |
| Ads | Client sends the documented raw image body and retains returned upload metadata. | Current raw image upload415; missing parser, owner-bound metadata, verified payment/booking lifecycle, availability/detail/cancel/refunds and world image delivery. |
| Beef / Cabal | Existing lists, gates and status reads are wired. | Targeted challenges/invites are currently ignored by the server. Client now blocks these writes unless the server explicitly opts into the documented complete semantics; invalid receipts cannot show success. See the exact capability contract in the Competition report. |
| Farm / Sybil / X / NPC chat | Farm claim→replay→history and tribe readback verified; quota and existing status routes connected. | Sybil room/season endpoints, X OAuth configuration, chat history and service-backed response acceptance remain separate gaps. |
| Personal character | Availability/job/delivery/checkout API families implemented and deployment reported with flags off. | Provider agreement, private storage/runtime/reference verification, owned paid delivery in the app and multiplayer avatar replication remain outstanding. |

Rendering, authored art/models, camera controls, local animation, phone layout and unpaid minigame practice correctly remain client-side. Cash, ownership, prices, eligibility, paid outcomes and progression must remain server-authoritative. Bundled presentation registries are not authorization to grant items or charge money.

## Ready-to-implement server handoff

1. [Money and Careers](backend-economy-careers-20261012.md): current versus historical proof, pricing, payment recovery, progression and ORE requirements.
2. [Social, City Life and World Life](backend-social-world-20261012.md): Spaces grants, homes, needs, paid calls and world delivery.
3. [Ventures and Competition](backend-ventures-competition-20261012.md): exact upload, project, invitation, competition and deployment contracts.

Priority is to resolve Spaces authorization, spot pricing, real-payment reconciliation and paid booking semantics before making those paths broadly available. Then complete the remaining home/social/project/competition lifecycles and rerun the same tests on the deployed revision.

## Validation

- Current local Money/Careers: **27 requests / 18 assertions passed**, including persisted promotion and no duplicate pay.
- Current local market: **20 requests / 9 checks passed / 1 blocked sell**. The failure was retained; slippage was not widened to hide it.
- Current local Social/City: **56 checks / 44 passed / 12 contract or authorization failures**, documented individually.
- Current local Ventures/Competition: **62 requests** with project, farm and tribe persistence/replay checks; missing/incompatible operations retained as failures, not blanket passes.
- Combined final Unity EditMode: **1,796 passed, zero failed/skipped**, including delayed-call cancellation and old/uncertain payment recovery. The initial pass exposed a stale Cabal probe call and then missing contract-fixture path in the isolated build clone; both were corrected before the final run. No failing test was removed.
- Integration reporting was corrected: blocked feature rows now keep the overall verdict incomplete even when core networking passes. Device reporting uses the same rule.
- Current Unity play-mode against `93dfabf`: **13/13 core checks passed** (developer sign-in, lottery/character, city/market/venue, visible server NPCs, mutual movement/chat/emotes, paid fictional shift, block/report/unblock, token rotation and isolated room rejoin). **42/48 feature/UI probes passed, 3 blocked, 2 unavailable, 1 disabled**; the corrected overall verdict is incomplete/exit 1. “App opened” probes do not certify a paid lifecycle. Receipt: `current-editor/editor-20261012-053759/receipt.json`.
- Remaining play-mode rows: home rent/visit404, no city event observed during the short run (workers were not started in this isolated stack), Beef creation explicitly unavailable, NPC LLM503 disabled, VC offers unavailable. These were not reclassified as successes.
- Pending financial recovery no longer expires merely because a day elapsed. Signed/uncertain records survive 30-day test cases; Reset/New top-up cannot discard a transfer that may already have broadcast. The original transaction is never re-signed to recover its status.
- Final Android development build succeeded: 227.0 MiB, zero errors, 25 warnings. Installed successfully on the Seeker. APK SHA-256: `704cac99139e3ecd35462d23950e7b6b40151d4a622c0875572bdc83779410ea` (binary retained outside Git).
- Seeker read-only UI checks used the **preserved older local backend on 4010/2567**, not the isolated current revision or Railway. The existing `joel` character and Cash balance were retained. Confirmed lobby load, Week's missing-event error/retry, persisted prediction-history view with new bets unavailable, and disabled non-renewal home moves. PNG/JSON evidence: `seeker-lobby`, `seeker-week-unavailable`, `seeker-predictions`, `seeker-home-move-blocked`.
- The app remained alive through those checks; its process-filtered Android error log was empty. Thermal status rose from 0 to 2 during this bounded check; the app was returned to the lobby and backgrounded. This is not a sustained performance or crash-free certification.

## Deployment boundary

The Seeker's development build and the hosted services are separate environments. Current hosted `/config` has no `livePhase`; the staging client defaults to phase 1. Raising that default on the client would not complete missing backend features. Publish a deliberate server phase/capability policy only when its feature acceptance passes.

The preserved old local service's readiness reports a Helius authorization failure; the hosted service's readiness passed. Credentials were not printed, copied into the repository or sent in the handoff.
