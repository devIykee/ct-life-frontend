# CT Life backend feedback — prioritized summary (2026-10-08, main @ 9f3e5ef)

> **Update for main @ 7243073:** section G at the end lists only what is still broken or newly broken after your
> 77 commits. The summary below is the 9f3e5ef state and is kept for reference.

From the Unity client, run against your stack locally (editor + Seeker). One line each; numbers point to the detailed
entries below (reproductions included there). Everything here was reproduced live unless marked (source).

**Sign-in blocker (fix first of all)**
0. `POST /v1/auth/verify` can never accept a real wallet: `verifySignature` in `server/application/auth.ts` passes the raw
   32-byte public key to `node:crypto` `verify`, which throws `ERR_OSSL_UNSUPPORTED` (caught → `SIGNATURE_INVALID`). Seen
   live 2026-10-08 with Seed Vault on the Seeker (the wallet signed correctly). Fix: wrap the key as SPKI DER via
   `createPublicKey({ key: Buffer.concat([0x302a300506032b6570032100, raw32]), format: 'der', type: 'spki' })`; with that,
   challenge → sign → verify returns 200. Patch: `tools/backend-local/patches/0001-siws-ed25519-spki-key.patch` (FE repo).

**Security (fix first)**
1. `POST /v1/beef/:id/settle` has no admin check: any signed-in player can settle a match and name the winner (source; Phase 4-6 README #1).
2. `GET /v1/cabal/invites` returns every invite with its bearer `code`; `POST /v1/cabal/invites/:id/accept` accepts for anyone (source).
3. `POST /v1/video-calls` returns every participant's LiveKit token to the caller → caller can join as anyone (#68, verified).
4. `POST /v1/shifts/:id/complete` pays for unassigned `miniGameId`s (0/10 + invented game paid 3636¢) (careers proposal §6, verified).
5. Realtime `venue` never verifies the JWT; every player's `userId` is `unverified-eyJhbGci`; profile fields come from join options (#38).
6. `POST /v1/auth/verify` never ties the signed message to the nonce/domain; challenge `domain`/`uri` are `"ct-life"` (#43).

**Blocks Phase 1 (city & identity)**
7. Seed Vault rejects the SIWS nonce: `GET /v1/auth/challenge` nonce must be ≥8 alphanumeric (UUID hyphens fail) (#59). FE works around it.
8. `POST /v1/auth/verify` reads `signedMessage` as UTF-8; contract says base64 (#46). FE retries with text.
9. Rooms serve `server/realtime/schemas.ts`, not `contract/realtime/states.ts` → codegen'd clients can't decode (#47).
10. `venue` room has no `filterBy(['venueId'])`: every venue joins the Timeline Square room (#39).
11. Realtime can't start / never syncs as published: tsconfig excludes `contract/`, `useDefineForClassFields` (#32, #33).
12. Workers crash at startup (redis URL + BullMQ `:` queue names) → no events, rollovers, Sunday Unlock (#34).
13. `POST /v1/lottery/roll` after `POST /v1/characters` → 409 always; 2nd pre-character roll → 500; no `flags.lotteryRolled` (#41).
14. `GET /v1/me` `user` lacks `hasCharacter`; auth responses flat; refresh has no user; `apply` returns `{id, level}` (#45, #48, #49).
15. A bodiless POST is 500 (Fastify 415 mapped to INTERNAL_ERROR) (#50).
16. City room is a stub: no `event` (mood is a sine), no `toast`, `dm`, `call_invite`, `space_update`, `payment_confirmed`, `liquidated` (#54, #64).
17. NPC state: `startedAt` rewritten every tick, ids that aren't spots/lines, brains never fed mood/events (#61-#63, #67).
18. Careers: skill gates never block, level/xp fixed 1/0, `POST /v1/shifts` without applying (careers proposal §6).

**Blocks Phase 2 (money)**
19. `POST /v1/payments/intents` has no `tx`/`cashCents`/`bonusBps`, amount not in base units; no `GET /v1/payments/intents/{id}` (#52, #53).
20. `POST /v1/market/trades` buy always fails `SLIPPAGE_EXCEEDED` (execution price 40.8× list price, even at 10000 bps) (#76).
21. `POST /v1/ore/stake/tx` → 500 locally; returns `{txBase64}` built without fee payer/blockhash; board/me are fixtures (#55).
22. Predictions and alpha calls differ from the contract (`POST /predictions`, string micros) (#56, #57); top-up cap unit (#58).

**Blocks Phase 3 (voice & social)**
23. No `POST /v1/rtc/token` → no proximity voice, Space audio or call media for invitees (#40, #69).
24. Paid calls: different request/response, no tx to sign, 5% fee vs 10%, no refund job (#70); no `PUT /v1/creator` (#71).
25. `GET /v1/dms` requires `peerUserId` (no thread list); DMs/feed ignore blocks; tips body/status differ (#72, #73).

**Blocks Phase 4 (city life)**
26. No `GET /v1/homes`; `POST /v1/homes/:id/rent` takes an instance id; `POST /v1/homes/:ownerId/visit` → 404 / no `roomId`; no `home` room (#75).
27. Venue rooms missing for 8 built venues: conference_hall, ct_jail, hackathon_house, airdrop_fields, sybil_barn, checker_kiosk, yacht_club, penthouse_row; please adopt spots `1.0.0-rc1+fe.2` (venues lane).
28. Insufficient Cash answered as `VALIDATION_FAILED`/`FORBIDDEN` instead of 402 `INSUFFICIENT_CASH` (city-life #2, Phase 4-6 #6).

**Tooling / hygiene**
29. Compose images that don't exist (livekit, minio), migration 060 runs its down section, 6 failing tests, 3 typecheck errors (#35-#37).
30. 61 of 135 registered routes have no rc1 operation although M8 says "no contract changes" (Phase 4-6 README #7).

---

# Feedback on CT Life REST API 1.0.0-rc1 (from the Unity client)

For: deviykee (github.com/devIykee/ct-life-backend). Prepared by the Unity client agent on 2026-10-05; the user forwards it.
Contract used: **tag `contract-v1.0.0-rc1` (commit 160d59e)**: `contract/openapi.yaml` (90 operations, 46 schemas; same
content as the Redoc export), `contract/realtime/{states,messages,index}.ts`, `contract/ids/*`, `contract/config/*`.
Sections A–B were first found on the Redoc export and re-verified on `openapi.yaml`; section C covers the rest of the
repo contract.

**How the client consumes it:** openapi-generator-cli **7.25.0** (Maven Central), generator `csharp`, models only:
`--global-property models,supportingFiles=false --additional-properties=packageName=CTLife.Rest,targetFramework=netstandard2.1,nullableReferenceTypes=false,library=httpclient,validatable=false`
(`client/Tools~/codegen_rest.sh`). Generation itself succeeds (107 model files; only warning: "OpenAPI 3.1 support is still
in beta"). The models are then compiled with Unity 6000.3.25f1's compiler. The client builds today because a local patch
script works around items 1 and 2. Per your rule ("any codegen failure is a contract bug"), here they are.

## A. REST codegen failures / blocking

1. **`type: boolean` + `const: true` generates invalid C#.** `CreateCharacterRequest.ageConfirmed` becomes
   `public enum AgeConfirmedEnum { True = true }`, which does not compile: C# enum values must be integral. The
   generated property would also serialize as the *string* `"true"`.
   **Ask:** declare it as plain `type: boolean` and keep "must be true, else VALIDATION_FAILED" in the description
   (the server already enforces it).

2. **Money fields are 32-bit in generated code.** These are `type: integer` with no `format: int64`, so they become C#
   `int`. Any value above 2,147,483,647 cents ($21,474,836.47) makes JSON deserialization throw. A rich player's `GET /me`
   would fail outright, as would rich-list `BoardEntry.value`. Your description says "All money is `bigint` cents".
   Affected named schemas: `Me.cashCents`, `Me.debtCents`, `Gig.payCents`, `PublicProfile.netWorthCents`, `Coin.mcapCents`,
   `Coin.vol5mCents`, `Trade.feeCents`, `Trade.cashCents`, `Position.costBasisCents`, `Position.unrealizedCents`,
   `Perp.marginCents`, `Perp.realizedCents`, `Bet.stakeCents`, `SmartMoneyTrade.cashCents`, `BoardEntry.value`,
   `Tombstone.lossCents`, `PaymentIntent.cashCents`, `ShopItem.priceCents`, plus the inline `*Cents` fields (lottery
   roll, needs action, shift complete, tips, homes rent, gigs run, daily clue, trades/perps/bets requests).
   **Ask:** add `format: int64` to every `*Cents` field and to `BoardEntry.value`.

## B. REST schema problems that make the client fragile (non-blocking)

3. **24 operations use inline (anonymous) schemas**, so the generator invents names that change whenever a path or method
   changes. A path rename then breaks the client build even though the payload is identical. Examples:
   `PostLotteryRoll200Response` (POST /lottery/roll), `GetCareers200ResponseInner` (GET /careers),
   `PostShiftsShiftIdComplete200Response`, `PostRtcToken200Response`, `GetLeaderboard200Response`,
   `PostNeedsAction200Response`, `GetMarketCoins200Response`, `GetMarketCoin200Response`, `GetDailyClue200Response`.
   **Ask:** promote them to named `components/schemas` (e.g. `LotteryResult`, `CareerSummary`, `ShiftResult`,
   `RtcToken`, `Leaderboard`, `NeedsActionResult`, `CoinPage`, `CoinDetail`, `DailyClue`).

4. **`ConfigResponse` sections are untyped** (`venues`, `archetypes`, `tribes`, `careers`, `market`, `events`,
   `npc_lines`, `ads`, `ore`, `meta`: `type: object, additionalProperties: true`). Without item schemas the client can't
   generate types, so a renamed field fails silently at runtime instead of at compile time. The example venue uses
   `maxClients`, where the shared spec's `venues.json` used `maxPlayers`.
   **Ask:** item schemas for each section (at least `venues`, `archetypes`, `tribes`, `npc_lines`, `careers`).

5. **Registries the client needs aren't in `GET /config`.** The shared spec's contract section 7 says the client never
   hardcodes config. These are missing: `items` (look slots and starter items for the character creator), `traits`,
   `dreams`, `homes`, `mini_games` (maxScore, durationSec), `boards`, `toasts`, `report_reasons`, `errors`, `needs`
   (action costs and decay), `economy` (top-up packs, bonuses), `skills` (leverage caps), `lottery`.
   **Ask:** add them, or confirm the client should bundle them. Until then the client bundles a local stand-in snapshot and
   merges your `/config` over it.

6. **`GET /careers` returns only `{id, name, level, xp, active}`.** The jobs screen needs each level's title, `payCents`,
   `shiftSec` and `miniGameIds`. The client reads them from config `careers`.
   **Ask:** confirm config `careers` carries `levels[]` with those fields, or add them to `/careers`.

7. **`Me.flags` is a free-form object.** The client relies on `flags.lotteryRolled` (and `ageConfirmed`) to skip the birth
   lottery.
   **Ask:** document the known keys and their types.

8. **SERVER_BUSY is declared on 1 of 90 operations** (`POST /payments/intents` has a 503 with a `SERVER_BUSY` example; the
   `ServerError` component response with `Retry-After` is not referenced anywhere else). The client treats `SERVER_BUSY`
   plus `Retry-After` on *any* endpoint and on realtime matchmaking as "City is busy, retrying in Ns".
   **Ask:** reference `ServerError` (503) on every operation that can shed load, and say whether `Retry-After` is always
   delta-seconds (the client also accepts an HTTP-date).

9. **`degraded`: thanks, it's implemented** for `voice`, `feed`, `dms`, `smart_money`, `ore_reader`, `login`, `signup`,
   `board_refresh`, `llm`, `price_feed`/`helius` (payments) and `ledger_freeze` (anything that moves Cash).
   **Ask:** confirm `degraded` is re-read on every `/config` (the client polls with `If-None-Match` every 2 minutes) and
   whether you'd rather push changes on the `city` room.

## C. Realtime, registries and handover (contract-v1.0.0-rc1)

10. **No C# is generated for any realtime message.** `messages.ts` defines all 19 messages as Zod schemas
    (`z.object(...)`). `schema-codegen contract/realtime/*.ts --csharp` (the command in `contract/realtime/README.md`,
    @colyseus/schema 5.0.36, TypeScript 5.9.3) emits only the 7 state classes. The client hand-mirrors the messages in
    C#, which is exactly the drift codegen is meant to prevent.
    **Ask:** also export plain TS interfaces/classes for the payloads (the codegen emits those), or publish JSON
    Schema for them (`zod-to-json-schema`), so the client can generate.
11. **The server sends `rejected` codes that aren't in the `RejectedMessage` enum.** `server/realtime/rooms/venue.ts`
    sends `SPOT_NOT_FOUND`, `UNKNOWN_EMOTE`, `OUT_OF_BOUNDS`, `SPEED_EXCEEDED` and `NPC_NOT_FOUND`; the enum has none of
    them. The client tolerates unknown codes, but the contract should list them (and `errors.json` too).
12. **NPC line ids can't be resolved.** The NPC brain sends named `lineId`s (`crier_long_liquidation`, `giveaway_pitch`,
    `reply_gm`, `perp_max_long`…), but `config/npc_lines.json` maps each archetype to an array of unnamed strings.
    **Ask:** make each line `{ "id": "crier_long_liquidation", "text": "…{coin}…" }`. Until then the client shows a
    stable line from the archetype's list (hash of the id), so the text won't match the event.
13. **`spots.json` has no positions, and `sit` ignores a spot's clip.** rc1 spots are `{id, kind}` only, while the
    client needs positions to place NPCs and seats. The room sets `anim = kind == 'stage' ? 'act_type' : 'act_sit'`,
    so terminals get the wrong clip unless they're kinded `stage`. spots.json is FE-owned: the FE proposal
    (`docs/fe-contract-proposals/ids/spots.json`, sent separately) keeps every rc1 id and kind, adds `x`/`z`/`rotY` and
    `anim` per spot, plus the other venues.
    **Ask:** the room uses `spot.anim` when present.
    **2026-10-06 recheck:** backend checkout `a5eafda` now contains the added positions, `anim` fields and
    `terminal_cafe.table_1..4` entries. The remaining problem is the handler at
    `server/realtime/rooms/venue.ts:260`, which still maps every `kind: terminal` to `act_sit` despite
    that spot's `anim: act_type`. The fitted frontend typing state deliberately requires confirmed
    `act_type` at an authored terminal; it does not silently reinterpret the server's action.
    **Studio readiness recheck:** the current adjacent backend contract also contains all 34 positioned
    `spaces_studio` anchors and matches the client snapshot exactly. The same handler maps all eleven
    `kind: stage` entries to `act_type`: `stage_host` explicitly asks for `loco_idle`, and `stage_1..10`
    explicitly ask for `act_sit`. This blocks truthful host/speaker pose integration as well as Café
    terminals. The request remains honoring the existing valid `spot.anim` field, not a new endpoint
    or a frontend action rewrite. No backend files were edited.
    Preserve the existing kind fallback for entries without an explicit valid animation, and add a
    room-level check that terminal_1 returns act_type while table_1 returns act_sit. No backend files
    were edited during this read-only audit.
    Spot ids are global (`timeline_square.center`) as your `sit` handler expects; the shared spec said venue-local. The
    client follows rc1.
14. **Registries reference anim ids that `anims.json` doesn't contain** (`tribes.json`: `emote_send_it_slam`,
    `emote_max_long`, `emote_ring_light_pose`, `emote_frantic_typing`). The `emote` handler rejects ids not in
    anims.json, so those signature emotes would be refused. The FE anims proposal (46 + 4 ids) contains them all and keeps
    every rc1 id. FE sfx proposal: 27 ids (keeps the 5 rc1 ids).
15. **`items.json` can't build a full `Look`.** It has 12 items: no `skin` slot, 1 `head` (`head_skull_visor`), no
    "none" accessory, while `Look` requires all 7 slots.
    **Ask:** starter items for every slot (the art kit has `skin_tone1..6`, male/female base heads, `acc_none`), and
    say which values `skinTone` accepts. The client currently offers `skin_tone1..6`, `head_male`/`head_female` and
    `acc_none` as fallbacks [Unverified that the server accepts them].
16. **`venues.json` has no `districtId`, `phase` or `sceneKey`.** The map and music need districts. The client uses an
    FE table; `sceneKey` is FE-added per the shared spec.
17. **No city-room `toast`.** Toasts for players who aren't in a venue (rent paid, tip received) have nowhere to go;
    `toast` exists only on the venue room. The client already listens for it on `city`.
18. **The handover is out of date:**
    - `docs/HANDOVER.md` points to `contract/README.md`, which doesn't exist.
    - `pnpm contract:codegen` runs `tools/contract-check/codegen.ts`, which doesn't exist at rc1 or on main.
    - The promised `contract/realtime/generated/csharp/` isn't produced.

    The client runs the commands from `contract/realtime/README.md` and HANDOVER §1 directly
    (`client/Tools~/codegen_realtime.sh`, `client/Tools~/codegen_rest.sh`).
19. Minor: `EventMessage.eventId` is an enum of 6 events, while the shared spec also lists graduation, big win, Sunday
    Unlock and the Weekly Meta. Confirm the enum will grow (an additive minor bump).

## Notes, no action needed
- Realtime states (`states.ts`) generate cleanly; `number` fields become C# `double`, so epoch ms keep full precision.
- `servers` include `/v1`; the client now uses `http://localhost:4010/v1`, `https://staging-api.ctlife.dev/v1` and
  `https://api.ctlife.dev/v1` as REST bases.
- `POST /auth/dev` takes `{wallet}` only; the client no longer sends `isSeeker`.
- `AuthChallenge.issuedAt`/`expirationTime` are `format: date-time`. The client parses the challenge raw and signs the
  exact strings you send, so formatting is never altered before SIWS verification.


## D. ORE and SKR payment integration audit — 2026-10-05

Full endpoint/model/state audit: [ore-skr-client-audit.md](ore-skr-client-audit.md).
These are local findings for the user to review/share, not messages sent to the backend developer.

20. **Transaction cluster is ambiguous per operation.** The Unity signer currently uses the environment cluster;
    staging is devnet, while the contract's economy config requires ORE on mainnet even in staging.
    `PaymentIntent` and the stake response do not identify the transaction cluster.
    **Ask:** include explicit cluster (and trusted RPC/confirmation policy) in transaction responses or a mandatory
    per-operation config mapping. FE must reauthorize/connect the correct wallet cluster before signing. No silent
    use of the staging devnet signer for ORE. A restored API JWT also does not restore a wallet-adapter session.
21. **ORE staking has no confirmation/expiry contract.** `POST /ore/stake/tx` returns only `{tx}`; there is no last-valid
    block height/expiry, submitted-signature endpoint or status operation. Claimable rewards and stORE decimals are
    also absent, and withdraw amount units are not explicit.
    **Ask:** define amount units for deposit/withdraw, stORE decimals, quote expiry, signature submission/status,
    commitment and when `/ore/me` reflects a confirmed action. FE can display submission as pending; it cannot
    truthfully declare staking complete from a signature alone. Do not infer APR or claimable rewards.
22. **Top-up cap disagrees by 100×.** `config/economy.json` sets `topUpCapCentsPerDay=1_000_000`; its schema defines the
    unit as USDC cents (= 10,000 USDC). The product spec and error copy say 100 USDC.
    **Ask:** confirm the intended cap and correct the value/unit/copy together. FE uses the server's returned quote
    and does not hard-code either disputed number.
23. **ORE tier and board examples disagree with the spec/schema.** Config requires 1 ORE for Prospector while the spec
    says any ORE; the board example has 2 squares while the schema requires 25. Example motherlode 55,000,000,000 at
    configured 11 decimals is 0.55 ORE, while the spec example says 55.
    **Ask:** confirm tier thresholds, publish a valid 25-square example, and specify raw/display units for every ORE
    amount. FE displays the returned `minerTier`, validates board shape and uses explicit denomination.
24. **Cash credit uses confirmation, not local arithmetic.** The city `payment_confirmed` event exists and is already
    published by the client channel. FE will correlate `intentId`, then refresh `/me`; a wallet signature alone must
    not locally add Cash. Payment retry must preserve the existing intent/idempotency key until its outcome is known.
    **Ask:** confirm event delivery/replay policy and the intended reconciliation after the client restarts.

25. **Top-up quote completeness and pack labels need alignment.** `PaymentIntent` does not require `cashCents` or
    `bonusBps`, although the top-up flow needs the exact quoted credit. Economy config tags Whale as `bestValue`,
    while Cabal has a higher base Cash-per-USDC ratio.
    **Ask:** require the credit fields for top-up intents and define the calculation behind `bestValue`; correct the
    label or pack values accordingly. FE should not advertise an unverified value comparison.

## E. Perps frontend activation audit — 2026-10-06

These are read-only findings from the sibling backend checkout, not a claim about deployed behavior. No economic
request was sent, no backend source changed, and no message was sent to the backend developer.

26. **Material blocker: an in-flight idempotency reservation falls through to the economic handler.**
    `server/shared/infra/idempotency.ts:140` returns `{acquired:false, waitMs:200, bodyHashMismatch:false}` when the
    lock is held. `server/api/middleware/idempotency.ts:47–67` handles acquired, mismatch and cached replay, then
    returns without replying or throwing for that waiting decision. The route therefore has no middleware stop
    for the in-flight case. The Unity client's existing `RestClient.cs:89–100` retries keyed network failures after
    its 15-second timeout; the backend lock lasts 30 seconds (`shared/infra/idempotency.ts:38`).
    **[Inference]** A sufficiently delayed open can enter the handler twice under a timeout/retry with the same key.
    This is a source-backed race finding, not a reproduced double debit. The same middleware serves other economic
    writes; the finding is not limited to Perps.
    **Ask:** handle the waiting decision with bounded wait/recheck or an explicit non-executing response; verify
    concurrent same-key requests, process interruption after commit/before response caching, lock expiry during a
    transaction, and key/body mismatch. The Redis cache alone does not demonstrate durable at-most-once economics.
    FE staged mitigation: opt-in single-request `PostOnce`, no automatic order retry or redirects, durable retained
    request key/body, explicit ambiguous result. Existing REST consumers are unchanged. Normal Perps submissions
    remain disabled until server behavior and the recovery path are verified.
27. **An ambiguous open cannot be conclusively reconciled.** The contract has GET `/perps`, but no request-key
    lookup or idempotency key in `Perp`. Matching coin/side/leverage/margin cannot identify a particular request if
    two positions intentionally share those fields. Absence from the backend's latest 200 positions is not proof
    of rejection. Close can reconcile by its known `perpId` when that exact row becomes closed/liquidated.
    **Ask:** define a read-only receipt lookup or durable, safe replay policy, including the 24-hour boundary.
    The staged FE retains an uncertain open and pauses submissions; it does not manufacture an ID or repeat it.
28. **Perps and market payloads differ from rc1.** Actual GET `/perps` returns `{perps}` rather than `Perp[]`;
    monetary fields are decimal integer strings, POST returns 201, validator permits leverage 25 (contract 20),
    leverage error details use `allowed` rather than `max`, and insufficient margin is `FORBIDDEN` with
    `reason:insufficient_margin` rather than contract `INSUFFICIENT_CASH`. Actual coin history is sampled
    `{at,priceMicros}`, not OHLC candles; `riskHint` is an object, not a string.
    **Sources:** `server/api/routes/perps.ts:20–68`, `server/application/perp-pit.ts:44–54,97–105,120–128`,
    `server/application/terminal-cafe.ts:56–61,192–194`; imported `contract/openapi.yaml:1071–1166,2908–2917,2944–2959`.
    **Ask:** align the published schema and implementation. FE reads both explicitly observed list/money/history
    shapes, keeps exact 64-bit integer reads, limits order leverage to the contract ceiling and request cents to
    JavaScript's exact integer range. No fabricated OHLC candles, generated price history or locked quote.
29. **Settlement result is not necessarily the change in available Cash.** Close returns raw `realizedCents`,
    while its credited amount is floored at zero if margin + result is negative. Liquidation updates status/closed
    time without assigning `realized_cents`; its event also omits contract `lossCents`.
    **Sources:** `server/application/perp-pit.ts:210–252,292–315`.
    **Ask:** define the realized-loss semantics and complete liquidation receipt/event fields. FE labels the actual
    returned result, leaves missing results unknown, and refreshes `/me` for available Cash instead of deriving a
    payout. A liquidation notification only invalidates Perps state.
30. **Open's coin validation is weaker than spot's.** Perps selects `coin_id,ticker,price_micros` and uses the price
    without checking stage or rejecting zero (`server/application/perp-pit.ts:109–118`); spot explicitly rejects a
    dead coin (`server/application/terminal-cafe.ts:284`). **Ask:** reject dead/nonpositive-price markets at the
    authoritative write boundary. FE disables preparation for a returned dead or nonpositive-price market; this
    client check cannot authorize the execution price or replace backend validation.


31. **Seat rejections cannot be correlated with a particular claim.**
    Existing `sit {spotId}` / `stand {}` and generic `rejected {type, code}` do not carry a claim request ID
    or rejected spot ID. Source review of FE `VenueSession.OnRejected` / `LocalPlayerController.OnSeatRejected`
    identified this sequence: request A, cancel A, begin/request B, delayed rejection A. If B is merely walking,
    the client now preserves its undispatched approach when no `_requestedSpot` exists. If B has already been
    dispatched, the rejection cannot be distinguished from a rejection of B and may clear the newer intent.
    This is a source-backed protocol ambiguity, not a reproduced deployed-server race.
    **Ask:** provide correlation for occupied/claim/rejection outcomes (request ID or an explicit equivalent),
    and specify ordering/replay on drop/reconnect; confirm cancellation semantics when sit+stand changes are
    coalesced into an unchanged empty schema state. Avoid inferring claim success from transport send success.
    FE native acceptance: Unity review v6 exited 0 with 31 actual component/navigation assertions, including
    separate standing/seated transforms, exact off-NavMesh seating, no seated movement publication intent,
    local/server stand, stale released-seat echoes, partial-path rejection, real walking arrival and retries.
    It used a null channel and explicitly injected dispatched-request markers for race paths; no live server
    calls or occupancy convergence were tested. Receipts: `art/gameplay-candidates/seat-approach-20261006/`
    `revision-5/native-acceptance-v6/`. No backend files were changed and no message was sent to its owner.

## F. First real run of `main` @ 9f3e5ef — 2026-10-07 (integration lead)

Ran the backend locally: Postgres/Redis/LiveKit from `docker-compose.yml`, api/realtime as
`node --import tsx server/<x>/main.ts` (the Dockerfiles' own entrypoints) with `.env` from `.env.example`.
Helper scripts: frontend `tools/backend-local/{up,down}.sh`. Every item below reproduces on a clean clone.

**Startup / tooling**

32. **Realtime cannot start as published.** `tsconfig.json` excludes `contract/`, so tsx compiles
    `contract/realtime/states.ts` with TC39 decorators and `@colyseus/schema` throws
    `Cannot read properties of undefined (reading 'constructor')` at import
    (`node --import tsx server/realtime/main.ts`, same CMD as `Dockerfile.realtime`).
33. **Realtime state never syncs once it does start.** `target: ES2022` implies `useDefineForClassFields: true`,
    which replaces the `@type` accessors on `server/realtime/schemas.ts` classes. The first patch throws
    `reading 'Symbol(Symbol.metadata)'` in `encodeMapEntry` and the room stops patching (players/npcs stay empty
    on clients). **Fix:** `"useDefineForClassFields": false` and include `contract/realtime` for tsx. Our
    workaround config is `tools/backend-local/tsconfig.backend.json` (FE repo) via `TSX_TSCONFIG_PATH`.
    `venue.test.ts` never encodes a patch, so tests stay green.
34. **Workers crash at startup.** `server/workers/main.ts` `redisConnection()` does `new URL(redis.main.options.host)`
    but ioredis stores a bare host (`localhost`) → `ERR_INVALID_URL`; even with that bypassed, BullMQ 6.3.11 rejects
    the queue names (`Queue name cannot contain :` for `events:publish`, `jobs:daily-board`, ...). No worker job runs.
35. **Compose images do not exist:** `livekit/livekit:v1.13.7` (the image is `livekit/livekit-server`) and
    `minio/minio|mc:RELEASE.2026-01-01T00-00-00Z`. `env_file: .env` with `localhost` URLs also cannot reach
    `postgres`/`redis` from inside the api/realtime/workers containers.
36. **Migration 060 runs its own down section.** `db/migrations/1700000060000_webhook_events_helius_program.sql` has no
    `-- Up Migration` marker, so `pnpm db:migrate` applies up+down: `webhook_events_source_check` ends without
    `helius_program` (`select pg_get_constraintdef(...)`), and `program-webhook.test.ts` fails.
37. **Test suite: 6 of 316 fail on a clean `.env`.** 1 from item 36; 5 in `llm-npcs.test.ts` because `.env.example`
    has `LLM_MODEL=` (empty) and `config.ts` uses `?? 'claude-…'`, so the model is `''` and violates
    `llm_npc_interactions_model_check`. `pnpm typecheck`: 3 errors (`program-webhook.test.ts:88` `expected`,
    `tools/scripts/inspect-ore.ts:1-2`). `pnpm db:status` is not a node-pg-migrate command. `tools/bots/world.ts`
    ignores `BOT_COUNT`/`BOT_VENUE` (reads `--bots/--venue`) and bots leave the room after 200 ms, so the `mock`
    profile has no standing population.

**Contract vs server (blocks the client)**

38. **Realtime does not verify the JWT and every player gets the same `userId`.** `VenueRoom.verifyToken` returns
    `sub: 'unverified-' + token.slice(0, 8)`; every RS256 JWT starts `eyJhbGci`, so all players are
    `unverified-eyJhbGci`. The client cannot tell itself from others, profile cards/block/report target nobody, and
    any string is accepted as a token. `name`, `skrName`, `isSeeker`, `tribeId`, `minerTier` are copied from the
    client's join options instead of the profile. **Ask:** verify with the API's public key in `onAuth`, take `sub`,
    and fill the PlayerState from the DB. Repro: two dev users join `venue` → both `userId` equal.
39. **`venue` has no `filterBy(['venueId'])`.** `joinOrCreate('venue', {venueId:'terminal_cafe'})` joins the existing
    `timeline_square` room (same `roomId`, `state.venueId === 'timeline_square'`). Also `gameServer.define(name, Room,
    {maxClients, patchRate})` passes those as *create options*, not room settings; `filterBy: () => true` is not a
    Colyseus option.
40. **`POST /rtc/token` is not implemented** (404 "No route"), so proximity voice cannot get a token. Also missing:
    `GET /events/active`, `GET /homes`, `GET /payments/intents/{id}`, `PATCH /characters/me/look|tribe`,
    `POST /feed`, `POST /predictions/{id}/bets`, `POST /spaces/{id}/speakers`, `PUT /creator`, all 4 admin writes
    (route probe of all 90 operations).
41. **Lottery order contradicts the bots and the client.** `LotteryService.roll` treats "character exists" as
    already rolled, so a roll *after* `POST /characters` always returns 409 `ALREADY_ROLLED` and the player keeps
    $0. Your bots create the character first and count 409 as success, which hides it. A **second** roll before the
    character returns **500 `INTERNAL_ERROR`** (ledger idempotency_ref collision), also with the same
    Idempotency-Key. `Me.flags` is `{}` (no `lotteryRolled`). **Ask:** 409 for any second roll, a
    `flags.lotteryRolled` (or `lotteryOutcome`) on `/me`, and say in the contract that the roll comes first.
    FE now rolls before the creator and treats 409 as done.
42. **`POST /characters` validates nothing but shapes.** Any `tribeId`, `dreamId`, `traitIds` or look item id is
    accepted (`tribeId:"y"` → 201). `items.json` has no free `head` item at all (only `head_skull_visor`, rare,
    phase 2), so no contract-valid look exists for a new player. Returns 201; the contract says 200.
    FE proposal for starter looks: `docs/fe-contract-proposals/starter-looks-items-20261007.md`.
43. **SIWS challenge `domain`/`uri` are `"ct-life"`** (JWT issuer/audience), not a domain and URI. Wallets render
    and may check these against the dApp identity. `verifySignedMessage` accepts any non-empty message: the
    signature is never tied to the nonce, so an old signed message from the same wallet verifies against a fresh
    nonce. **Ask:** domain/uri from config (e.g. `ctlife.app` / `https://ctlife.app`), parse the SIWS text and
    compare nonce/domain/expiry.
44. **Venue details.** Every player spawns at (0,0); NPC `spotId`s include ids that are not spots (`spaces_studio`
    is a venue, `square_center`/`venue_exit` are not in `spots.json`); `toast` for `giveaway_guy` is broadcast to
    the whole room with a `to` field; `rejected` codes `SPEED_EXCEEDED`, `OUT_OF_BOUNDS`, `SPOT_NOT_FOUND`,
    `NPC_NOT_FOUND`, `UNKNOWN_EMOTE` are not in the `RejectedMessage` enum (see item 11). The city room is a stub
    (no `event`, `toast`, `payment_confirmed`, `liquidated` sends).

**Found while running the real Unity client (editor, 2026-10-07)**

45. **Auth responses are flat, refresh has no user.** Contract `AuthVerifyResponse` is `{accessToken, refreshToken,
    user:{id, wallet, isSeeker, hasCharacter}}`. `/auth/verify` and `/auth/dev` return the user fields at the top level;
    `/auth/refresh` returns only the two tokens. A contract client gets `user = null` (no user id). FE now reads both
    shapes and falls back to the JWT `sub`/`wal`/`skr` claims.
46. **`signedMessage` encoding.** contract.md §3: "Both byte fields are base64". `verifySignature` does
    `Buffer.from(signedMessage, 'utf8')`, so a base64 message never verifies (`SIGNATURE_INVALID`). FE retries once
    with the decoded UTF-8 SIWS text (same nonce; it is only consumed on success). **Ask:** base64-decode it.
47. **Live room schemas are not the contract's.** Rooms serve `server/realtime/schemas.ts`, not
    `contract/realtime/states.ts`: PlayerState has no `look` and a different field order; NpcState has `npcId`,
    `archetype` (contract `archetypeId`) and no `lineParams`; numbers are `number` (contract `float32`/`int32`);
    MarketState is `{ts, placeholder}` with no `coins`. A codegen'd C# client binds a type only if every name, index
    and type matches, so typed decoding silently fails. FE now decodes by name (`DynamicSchema`). **Ask:** have the
    rooms `import { … } from 'contract/realtime/states'`.
48. **`GET /me` `user` lacks `hasCharacter`** (required by the contract `User`). The generated client model throws,
    so a contract client can never load the profile. Repro: `curl /v1/me` → `user` has `id, wallet, isSeeker, skrName,
    isSybilJailed, sybilJailedAt`. FE derives it from `character`.
49. **`POST /careers/{id}/apply` returns `{id, level}`**; contract `Job` is `{careerId, level, xp}`.
50. **A POST without a JSON body is a 500.** Unity's bodiless POST carries a form content type; Fastify throws
    "Unsupported Media Type", which the error envelope maps to `500 INTERNAL_ERROR` (should be 415/400). Repro:
    `curl -X POST /v1/lottery/roll -H 'authorization: …' -H 'content-type: application/x-www-form-urlencoded'`.
    FE now always sends `{}`.
51. **`minClientVersion` is `1.0.0`** in `GET /config` while there is no released client; FE moved to 1.0.0 to pass
    the gate. Please confirm the versioning you intend (FE sends `X-Client-Version`).

### Phase 2 (money) — from the phase2-client lane, each re-verified against main @ 9f3e5ef on 2026-10-07

52. **Top-up intent shape.** `POST /payments/intents {"kind":"topup","sku":"ramen","currency":"USDC"}` →
    `{intentId, recipient, mint, amount:"0.000500", reference, memo, solanaPayUrl, expiresAt}`: no `tx` (required),
    no `currency`, `cashCents`, `bonusBps`; `amount` is a decimal string derived from the pack's Cash, not base units
    of the paid token (the ramen pack costs 0.0005 USDC). MWA `sign_and_send` needs `tx`.
53. **No `GET /payments/intents/{id}`** (404 "No route"); the client cannot poll a pending top-up.
54. **City room never sends `payment_confirmed` or `liquidated`; market room has no `coins` and never sends `tape`**
    (`rooms/city.ts`, `rooms/market.ts`). Clients fall back to polling.
55. **ORE stake.** `POST /ore/stake/tx {"action":"deposit","amount":"1000000000"}` → `500 INTERNAL_ERROR` on the local
    stack; per source it returns `{txBase64}` (contract `{tx}`) built from a legacy message without fee payer or
    blockhash, so MWA could not sign it anyway. `GET /ore/board` is a fixture (`roundId:"dev-fixture"`, 5 numeric
    squares, `priceUsd:0`); `GET /ore/me` all zeros.
56. **Predictions API differs.** `GET /predictions` → `{"predictions":[]}` (contract: `PredictionMarket[]`, open
    markets); bets are `POST /predictions {outcomeId, stakeCents, horizon}` instead of `POST /predictions/{id}/bets`
    (404). There is no market list, so the Pit is empty.
57. **Alpha calls want strings.** `POST /alpha-calls` with integer `entryMicros/targetMicros/stopMicros` (contract) →
    `VALIDATION_FAILED expected string`.
58. **Top-up cap unit.** `economy.json topUpCapCentsPerDay = 1000000` is compared with credited Cash cents
    (`topup.ts:138`), i.e. $10,000 Cash/day, so the Cabal pack (1,600,000 Cash cents) can never be bought; the spec
    says "100 USDC per wallet per day" (a paid-value cap).
    Other shape differences the client tolerates (no action needed unless you want the contract exact): bigint fields
    as strings, `{coins|positions|perps|tombstones|trades}` wrappers instead of arrays, candles `{at, priceMicros}`,
    trade receipt without `feeCents`, `POST /perps` 201 with leverage up to 25 and `FORBIDDEN insufficient_margin`.

**Found on the Seeker (2026-10-08, local stack over adb reverse)**

59. **SIWS nonce is not wallet-valid.** `GET /auth/challenge` issues a hyphenated UUID nonce. The Seeker's Seed Vault
    wallet rejects it inside `authorize`: `IllegalArgumentException: nonce must be at least 8 alphanumeric characters`
    (wallet logcat, 2026-10-08 00:14) and never answers, so SIWS cannot complete. `uri: "ct-life"` is also not an
    RFC 3986 URI. **Ask:** alphanumeric nonce (e.g. UUID without hyphens) and real `domain`/`uri` (item 43). FE now
    strips the hyphens for the wallet and sends the server's own nonce to `/auth/verify` (works only because the
    server does not compare the signed message with the nonce, item 43).
60. **Move velocity check is jitter-sensitive.** `handleMove` divides the distance by the gap between *arrival* times
    of accepted moves; two moves that arrive close together after normal running (≈4 m/s, client sends ≤10/s)
    trip `SPEED_EXCEEDED` (seen on the Seeker at 00:50:29). **Ask:** compare with the client's send interval or allow
    a burst budget.

### World life (from the world-life lane; items re-checked live 2026-10-08 against main @ 9f3e5ef)

61. **NpcState semantics.** `tickNpcs` rewrites `startedAt = now` every second (sampled 75124 → 76121 → 77122 ms),
    and later ticks of a state send `animId: loco_idle` (Giveaway Guy in `goto`) and `lineId: ''` (Town Crier,
    Spaces Host one tick after the line). Clients restart walks/one-shots every second and lose the line.
62. **Brain ids don't resolve.** Spot ids sent: `square_center`, `venue_exit`, and venue ids as spots (`spaces_studio`),
    plus per source `perp_terminal`, `cafe_terminal`, `builder_hq_1/2` and `"x0,z0"` rect strings — none are in
    `spots.json`. Line ids (`crier_air_thin`, `spaces_gm`, `giveaway_pitch`, …) are not ids in `npc_lines.json`
    (its lines are unnamed arrays); `lineParams` is never set; `emote_moon` is not in `anims.json`.
63. **Brains never see the world.** `VenueCtx.moodBps = 0`, `recentEvents = []`, `richestPlayers` sorts by
    `netWorthCents: 0` → Town Crier never reacts, Doomposter is always `leave`, Reply Guy follows the first joiner.
64. **City room**: never broadcasts `event`; `mood` is a synthetic sine (sampled −171, −169, −167 bps).
65. **Giveaway Guy** `npc_tap` broadcasts `toast {to, textId:"toast_drained"}` to the whole room and debits nothing.
66. **`GET /ads/live`** (per source) returns `{ads:[{slotId, bookingId, link, imageKey, mimeType}]}`: an S3 key, no
    `imageUrl`/`sponsored`; contract is a slot map with `imageUrl`. (Live list was empty, so unverified by request.)
    `GET /feed` returns `{posts:[…]}` (contract `FeedPost[]`).
67. **Registry gaps.** `archetypes.json` lists `spaces_host` twice, `normie.venueId = market_room` (not a venue),
    no `laser_eyes_maxi`/`chain_tribalist`; NPCs spawn in only 5 venues; `vc_npc`/`builder_npc` brains are not
    archetype ids; Perp Degen's Rekt Clinic "respawn" is a spot id inside the Perp Pit room.
    FE proposals: `docs/fe-contract-proposals/npc-world-life-20261008.md`.

### Phase 3 social (from the phase3-client lane; key items re-checked live 2026-10-08 against main @ 9f3e5ef)

68. **Security: `POST /video-calls` hands the caller every participant's LiveKit token.** Repro: two dev users;
    A posts `{"userIds":["<B id>"]}` → `{call, tokens:[{userId:A, token}, {userId:B, token}]}` (verified: 2 tokens).
    A can join the call as B. Contract: `{callId}`; each participant should fetch only its own token
    (`POST /rtc/token {kind:"call"}`, which does not exist — item 40). Room is `video:{callId}` (contract `call:{callId}`).
69. **No `POST /rtc/token`** (item 40) also blocks Space audio and call media for invitees; the city room sends no
    `space_update`, `dm` or `call_invite` (item 54), so calls never ring and DMs/Spaces only poll.
70. **Paid calls are a different API.** `POST /paid-calls {payeeUserId, pricePerMinuteCents, maxMinutes}` (fan sets the
    price) instead of `{creatorId, minutes}`; returns `{call, livekit}` with no `tx`/`sessionPda`, so nothing to sign;
    status never `ringing`; amounts are cent numbers (contract base-unit strings); fee 5% (`PLATFORM_FEE_BPS = 500`;
    spec/IDL 10%); `/end` needs a client-reported `{secondsUsed}`; no accept-window refund job;
    `onchain_session_id` never written; `submitOnChainSettle` does `new PublicKey(<user uuid>)`. (source review)
71. **No `PUT /creator`** (there is `POST`/`PATCH /creator {handle, bio, categories}`; no rate or max minutes).
72. **`GET /dms` requires `peerUserId`** → `VALIDATION_FAILED {"reason":"peerUserId required"}` (verified); no
    thread list. `POST /dms` ignores the receiver's block (contract 403 `BLOCKED`); `GET /feed` does not hide
    blocked authors.
73. **Tips.** `POST /tips {toUserId, cashCents}` → `VALIDATION_FAILED expected amountCents` (verified); no new balance in
    the response, insufficient Cash is `VALIDATION_FAILED` (contract 402 `INSUFFICIENT_CASH`), no daily cap.
    `POST /tips/onchain` requires `amountLamports` and credits in-game Cash (spec: wallet-to-wallet).
74. **Spaces shape.** `GET /spaces` → `{spaces:[…]}` (verified) with ids `space:{uuidv7}` (contract `format: uuid`),
    `speakerUserIds`/`handUserIds`; promotion at `POST /spaces/{id}/speakers/{userId}`; stage cap 8 incl. host
    (spec host + 10); several creates answer 201.

**Found by the feature pass (2026-10-08, editor + local stack)**

75. **Homes don't match the contract.** `GET /v1/homes` → 404 "No route". `POST /v1/homes/studio/rent` → `404 NOT_FOUND
    {resource: home}`: the route treats `:homeId` as a *home instance* id and only pays rent on an owned instance
    (contract: home *type* id, signs a lease). `POST /v1/homes/<ownerUserId>/visit` → `404 NOT_FOUND` (looks up
    `homes.home_id = ownerId`); when it succeeds it returns a visit-log row, no `roomId`; `server/realtime/main.ts`
    defines no `home` room. 3D visits and moving home cannot work end to end.
76. **Every buy fails slippage.** Fresh dev user, coin `TRUST…` listed at `priceMicros 1000000`, mcap $1M:
    `POST /v1/market/trades {coinId, side:"buy", cashCents:500, maxSlippageBps:10000}` →
    `SLIPPAGE_EXCEEDED {expected:"1000000", actual:"40833374"}` (also at 500 and 2000 bps). The execution price for a
    $5 buy is 40.8× the quoted price, so no spot trade can execute.

### Phase 5 ventures + Phase 6 competition (integration lead; checked live 2026-10-08 against main @ 9f3e5ef)

Probe: editor run `../ct-life-evidence/integration/editor-20261008-070316/receipt.json` (lanes' own API classes, dev user).
Lane logs with the full proposal-vs-main tables: `docs/progress/phase5-ventures.md`, `docs/progress/phase6-competition.md`
("Backend gaps"); rows not listed here were not probed [Unverified].

77. **`POST /projects` refuses for cash with a generic error.** `{kind:"memecoin", name}` from a dev user with $339 →
    `400 VALIDATION_FAILED {reason:"insufficient cash for project seed", have:"33925", need:"50000"}` (verified, also with
    `{kind}` only). Ask: `402 INSUFFICIENT_CASH` (contract code) so the app can say why, and expose the $500 seed in
    config/economy so the form shows it before the tap.
78. **Phase 5 routes missing** (404 "No route", verified): `GET /projects?scope=mine`, `GET /me/project-offers`,
    `GET /vc-row/deals`. Present and working: `GET /ads/slots` (4 slots), `GET /ads/bookings`.
79. **Phase 6 routes missing** (404 "No route", verified): `GET /beef` (list), `GET /airdrop/farm`, `GET /me/sybil-status`,
    `GET /venues/{id}/access`, `GET /me/cabal`, `GET /llm/quota`. Working: `GET /tribes` (7), `GET /leagues` (5),
    `GET /me/league` (no membership before rollover), `POST /beef {stakeCents, topicId, invitedUserId}` (status `open`;
    [Unverified] whether `topicId`/`invitedUserId` are stored), `GET /beef/elo`, `GET /cabal/invites`, `GET /x/link`.
    `GET /districts` returns **0 districts**, so district flags and standings stay empty.
80. **No realtime for Phase 5/6.** The server sends none of `ad_booking_update`, `ads_changed`, `vc_offer`, `hire_offer`,
    `project_event`, `district_flipped`, `season_rollover`, `league_result`, `beef_challenge`, `beef_update`, `beef_result`,
    `sybil_status`, `cabal_invite`, `x_link_update` (no sender in `server/`, verified by search), no
    `kicked {reasonId:"sybil_jailed"}`, and no venue join codes `NET_WORTH_TOO_LOW` / `INVITE_REQUIRED` / `SYBIL_JAILED`
    (only the Marina home gate checks jail/net worth). The client handles all of them when they arrive.
81. **Unknown routes answer with the contract body** `404 NOT_FOUND "No route for this path and method."`, the same code as
    a missing record. The client now tells them apart by that message text. Ask: a distinct code (e.g. `ROUTE_NOT_FOUND`)
    or `501` for proposal routes that are not built yet.

---

## G. After 7243073 — still broken or newly broken (integration lead, local run 2026-10-08)

Run: your `main` @ 7243073 locally (`tools/backend-local/up.sh`), migrations through `1700000070000_add_lottery_rolled`,
api + realtime + workers all start. Evidence: `../ct-life-evidence/backend-7243073/`. Items are added as the re-run
reaches them; anything not listed was either fixed or not re-checked yet (marked when known).

82. **SIWS domain default is `ctlife.dev`.** `.env.example` and `server/shared/config/config.ts` default `SIWS_DOMAIN` /
    `SIWS_URI` to `ctlife.dev` / `https://ctlife.dev`. We will not use `.dev`, and the real domain (a .fun or gaming TLD)
    is not bought yet. Ask: make the default a placeholder until we pick it, e.g. `ctlife.invalid` /
    `https://ctlife.invalid/`, which is the client's MWA identity today. The client keeps the identity in one line
    (`EnvConfig.AppIdentityUri`) and derives the expected SIWS domain from it; when the domain exists, that line and
    your two env vars change together. Locally both already agree: `GET /v1/auth/challenge` →
    `{domain:"ctlife.invalid", uri:"https://ctlife.invalid/", nonce:"c911559f…" (32 alphanumeric), chainId:"solana:devnet"}`.
83. **Newly broken: every `venue` join fails.** `VenueRoom.fetchPlayerProfile` (`server/realtime/rooms/venue.ts`, from
    2cb4bcd "B5") runs `SELECT c.name, c.skr_name, … FROM characters c JOIN users u …`, but `characters` has `alias`, not
    `name`, and `skr_name` lives on `users`. Repro: any client `joinOrCreate('venue', {token, venueId})` →
    `MatchMakeError: column c.name does not exist` (realtime log: `VenueRoom.onJoin` → `fetchPlayerProfile`). Expected:
    the join succeeds with the DB profile. Fix: `SELECT c.alias AS name, u.skr_name, u.is_seeker, c.tribe_id, c.look`.
    Second bug in the same function: `characters.look` is stored as the contract `Look` object
    (`{skinTone, headId, …}`), but the code only accepts an array, so every player's `look` in the room is
    `["0","0","0","0","0","0","0"]` and other players never see anyone's chosen look. Expected: the room's string[7] in
    contract order `[skinTone, headId, hairId, topId, bottomId, shoesId, accId]`. We run both fixes locally
    (`tools/backend-local/patches/0001-venue-profile.patch`, FE repo); with them, players and NPCs sync, `userId` is the
    real user id, and a spoofed `name`/`look` in the join options is ignored (B5 works). Also: `minerTier` is hardcoded
    `'none'` although `users.miner_tier` exists.
84. **Newly broken: `POST /v1/characters` rejects every look.** `CharacterService.validateInput`
    (`server/application/character.ts`, from 8377ffb "B13") reads `items.items[id]` as a map, but
    `contract/ids/items.json` is `{items: [{id, slot, …}]}`, an array, so every id is unknown. Repro: dev sign-in,
    `POST /v1/lottery/roll` (200), then `POST /v1/characters {alias, look:{skinTone:"skin_tone3", headId:"head_rain",
    hairId:"hair_rain_ponytail", topId:"top_hoodie_black", bottomId:"bottom_jeans_indigo", shoesId:"shoes_sneakers_white",
    accId:"acc_none"}, traitIds:["fast_talker","iron_will"], dreamId:"make_10k", tribeId:"trencher", ageConfirmed:true}` →
    `400 VALIDATION_FAILED {reason:"invalid_look_item", itemId:"head_rain"}`. The same happens with ids that are in
    `items.json` (`head_skull_visor`, `hair_mohawk_pink`, `acc_aviators`, …): `itemId:"head_skull_visor"`. Expected: 201. Second half: even indexed
    by id, `items.json` holds 12 shop items and none of the creator's base-look ids (`head_<look>`, `hair_<look>`,
    `top_<look>`, `bottom_<look>`, `shoes_<look>`, `acc_none`; the creator offers 24 looks), so every real creator
    submit would still fail. Ask: validate only shop items (ids present in `items.json`) and accept base-look ids, or let
    us add the base looks to `items.json` (we can send the list). Third part: `skinTone` must match `/^skin_tone[1-6]$/`,
    but the contract's `Look.skinTone` is a free string and its own example is `'tan'`: `skinTone:"tan"` and
    `"skin_tan"` (what the creator sends for its cast looks) both → `400 {reason:"invalid_skin_tone"}`. Seen on the
    Seeker: creator look `player_f_kol` → `POST /characters → 400`. Effect today: no new player can finish onboarding, and
    bots join as `Anon`. Seen through the real client too: editor run `editor-20261008-151018`, `POST /characters → 400`.
    Locally we skip ids that are not in the catalog and accept any non-empty skin tone
    (`patches/0002-character-look-validation.patch`) so the rest can be tested.
85. **FYI, no action needed: CoinState `bigint64`.** e69bf64 is right for your server (the values are `bigint`), but the
    Colyseus Unity SDK (0.18.8, the latest) cannot decode `bigint64`: it reads nothing and misaligns the rest of the patch.
    We patched our vendored SDK copy to read it as int64 (same wire format). Please tell us before moving other fields to
    `bigint64`/`biguint64`, and keep them out of rooms the Unity client joins if you can.
86. **Newly broken: `GET /v1/config` is always 500.** `server/api/routes/core.ts` (415d355 "B34") adds
    `emotes: cache.load('emotes') ?? null`, but there is no `contract/ids/emotes.json` and `ConfigCache.load` throws
    rather than returning null. Repro: `GET /v1/config` → `500 INTERNAL_ERROR` (api log: `Config file not found:
    emotes.json`). Expected: 200. The client survives on its bundled snapshot, so no server-side config change can reach
    it. Same handler: the ETag is the constant `"1.0.0-rc1"`, so once a client sends `If-None-Match` it gets 304 forever,
    even after `economy.json` etc. change; please hash the payload. Local fix: `patches/0003-config-emotes.patch`.
87. **Newly broken: `POST /v1/payments/intents` is always 500.** `server/application/topup.ts` (B19) does
    `new PublicKey(input.userId)`, but the route passes `req.user.sub`, the user UUID → `Non-base58 character`.
    `referencePubkey` is also built from a base64 string (`new PublicKey(Buffer.from(reference,'hex').toString('base64'))`),
    which is not base58 either. Repro: dev sign-in, `POST /v1/payments/intents` with the client's top-up body → 500
    (api log `Non-base58 character`). Expected: 201 with the unsigned `tx`. Look up `users.wallet` as `/ore/stake/tx` does,
    and use 32 random bytes for the reference key. Not patched locally (money path).
88. **`POST /v1/rtc/token` exists now, but rejects the contract body.** The contract request is `{kind, id}`
    (`kind: venue | space | call`); the route's zod schema requires `roomId`, so every contract client gets
    `400 VALIDATION_FAILED` (editor feature row `social.space_audio`). Expected: accept `id` (the response shape already
    matches). Second mismatch: `expiresAt` comes back as epoch seconds (`1791474257`), the contract says a date-time
    string, so the client cannot decode the reply ("Voice unavailable"). Local fix for both:
    `patches/0004-rtc-token-contract-id.patch`; with it, a Space audio token mints (editor run `editor-20261008-152406`)
    and the Seeker joins proximity voice in Terminal Café (LiveKit room `venue:<instanceId>`, active speaker).
89. **Still open from the 9f3e5ef list (re-checked live on 7243073):**
    - City room is still a stub (#16, #54, #64): `mood` is a sine, no `event`, no targeted `toast`/`dm`/`call_invite`/
      `space_update`/`payment_confirmed`/`liquidated`. `POST /video-calls` now returns only `callId` (#68 fixed), but the
      callee is never rung.
    - Homes (#75): `POST /homes/{homeId}/rent` looks up a home *instance* (`studio` → 404); `POST /homes/{ownerId}/visit`
      looks up `home_id = ownerId` (user id → 404) and there is still no `home` room. `GET /homes` works (#40 part fixed).
    - `GET /me` `user` still has no `hasCharacter` (it moved to `flags`) (#48); `/auth/verify` and `/auth/dev` answer
      flat, `/auth/refresh` has no user (#45); `POST /careers/{id}/apply` returns `{id, level}` (#49).
    - Phase 5/6: `POST /projects` still 400 `VALIDATION_FAILED` for cash (#77); still 404 "No route": `GET /projects?scope=mine`,
      `/me/project-offers`, `/vc-row/deals`, `/beef`, `/airdrop/farm`, `/me/sybil-status`, `/venues/{id}/access`, `/me/cabal`,
      `/llm/quota` (#78, #79); `GET /districts` still 0 (#79); no Phase 5/6 realtime messages (#80).
    - NPC brains are still not fed mood or events (`tickNpcs`: `moodBps: 0`, `recentEvents: []`) (#17).
    - The `market` room is a stub: `onCreate` leaves `state.coins` empty ("M3 reads from Redis" TODO), so no live
      prices reach clients although `GET /market/coins` lists coins and buys fill. (This also means the CoinState
      `bigint64` fields of item 85 never travel yet.)
90. **B17, venue patrons (still open).** `VenueRoom.npcArchetypesFor` still gives `terminal_cafe` only
    `['wallet_tracker']` (which only stands and talks at `cafe_terminal`) and `spaces_studio` nothing. Ask: seat NPCs at
    the global spot ids from `spots.json` (1.0.0-rc1+fe.8; every seat below is fitted on the client) with these states:

    | Venue | Who | Spot(s) | State / anim |
    | --- | --- | --- | --- |
    | terminal_cafe | barista | `terminal_cafe.counter`, behind the bar facing the room | stand, `loco_idle` (or a serve beat) |
    | terminal_cafe | 2–3 laptop patrons | `terminal_cafe.terminal_1..4` (communal laptop table) | `act` + `act_type` |
    | terminal_cafe | 1 trader | one of `terminal_cafe.terminal_5..8` (twin-monitor desks) | `act` + `act_type` |
    | terminal_cafe | 1–2 coffee drinkers (optional) | `terminal_cafe.table_1..4` | `act` + `act_sit` |
    | spaces_studio | host | `spaces_studio.stage_host` | stand, talk beats |
    | spaces_studio | 2 guests | `spaces_studio.stage_4..7` | `act` + `act_sit` |
    | spaces_studio | 3–5 audience | `spaces_studio.seat_1..20` (bean bags 1–10, poufs 11–20) | `act` + `act_sit` |

    Send `spotId` (the global id) and `animId` in `NpcState`; the client walks an NPC with a seat anim to the seat's
    approach point and sits it. New archetypes (barista, patron) need a line in `archetypes.json`; we add their looks.
91. **B10, venue room routing: fixed.** `define('venue').filterBy(['venueId'])` is in; two café joins share one room and
    Timeline Square gets its own (verified with bots and the editor run). The client's wrong-room guard is removed.

92. **Newly broken for existing players: the lottery comes back.** Migration `1700000070000_add_lottery_rolled` adds
    `users.lottery_rolled_at` without a backfill, and `GET /me` derives `flags.lotteryRolled` from it. Every user created
    before the migration reads `{lotteryRolled:false, hasCharacter:true}` (locally 330 of 347 users with a character).
    Rolling then fails: `POST /v1/lottery/roll` → `500 INTERNAL_ERROR` (api log: `duplicate key value violates unique
    constraint "ledger_entries_idempotency_ref_key"`), Cash unchanged. Seen on the Seeker: a real Seed Vault sign-in of
    an existing player landed on "Your origin story". Expected: a backfill in the migration
    (`UPDATE users SET lottery_rolled_at = COALESCE(<first lottery ledger entry time>, created_at) WHERE id IN (SELECT
    user_id FROM characters) OR <has a lottery ledger entry>`), and `409 ALREADY_ROLLED` instead of 500 when the ledger
    already has the roll. This matters for any database that has users before the migration runs (the hosted one, if
    any). Client side we now treat "has a character" as rolled whatever the flag says (contract order).

93. **NPCs have no starting spot.** Every venue NPC is created `idle` with `spotId: ""`
    (`server/realtime/rooms/venue.ts` ~429: `new NpcState()` with only `archetypeId` set). The client therefore has to invent where each one stands; it used the
    entrance, which is also the player's spawn, so five Timeline Square NPCs stood on every arriving player (seen on the
    Seeker in the first-time flow, 2026-10-08). Repro: join `timeline_square` in a fresh room and read `npcs` at once:
    all five `idle@-` (`../ct-life-evidence/npc-spawn/npc-states-timeline.txt`, sampled every 5 s for 30 s). Later
    targets also use ids that are not spots of that venue: Timeline's `spaces_host` → `talk@spaces_studio` (a venue id),
    and `doomposter` → `leave@venue_exit` (spots.json has `exit` and `entrance`). Expected: give each NPC a home `spotId`
    from its venue's spots.json (`npc`/`standing`/`stand` kinds) when the room creates it, and only send spot ids of the
    current venue. The client now places spot-less NPCs on their own home spots, away from the spawn, so this is not
    blocking.

Fixed and verified live on 7243073 (no action): B0 SIWS verify (local ed25519 round trip: base64 → 200, text → 401;
and a real Seed Vault sign-in on the Seeker: challenge for `ctlife.invalid`, signed as issued, `POST /auth/verify` → 200
on the first try);
B6/#43 verify checks every SIWS field against the nonce and domain (source; the round trip passes); #59 alphanumeric nonce; #46 base64 `signedMessage`;
#47 rooms serve `contract/realtime/states.ts` (venue, city, market checked field by field); #39 filterBy; #32/#33
realtime starts and syncs with your own tsconfig; #34 workers start; #41 `flags.lotteryRolled`, second roll → 409
`ALREADY_ROLLED`; #50 a bodiless POST is now 400, not 500; #76 spot buy fills; #55 `POST /ore/stake/tx` returns a v0 `tx`
(with a working devnet RPC; an off-curve wallet gives a 500 with an empty message, low priority); #68 call start returns
only `callId`; #40 `GET /homes` and `POST /rtc/token` exist (see 88).


94. **Dream progress is not in the contract (home screen, 2026-10-09).** `/me` has `character.dreamId` only; nothing says
    how far a player is towards it, and nothing defines what each dream measures (does "Make $10K" count Cash, net worth or
    lifetime earnings? what completes "Survive a rug"?). The redesigned home shows the dream as a progress card, so the
    client now reads an optional `flags.dreamProgress: {current: number, target: number, unit?: "cents"}` (flags is
    `additionalProperties: true`, so this is not a breaking change) and hides the bar until it arrives. Expected: either
    that flag, or a typed `character.dreamProgress {current, target, unit, completedAt?}` in `Me`, plus the rule per dream
    id in `dreams.json` (`metric`, `target`). Debt is fine: `Me.debtCents` and `lottery.json debtRepayment.weeklyDueCents`
    are what the home shows ("$720 to pay back", "$120 is due each week").

95. **Trait labels read as jargon to players (user review, 2026-10-09).** Players did not know what "Iron Will" or
    "Sleuth" meant. The client now shows plain names and a one-line effect from its own copy (ids unchanged, they are the
    contract):
    - `fast_talker` "Fast Talker": chat cooldown 1.5s instead of 2s.
    - `deep_pockets` "Deep Pockets": rent penalty halved.
    - `iron_will` → **"Diamond Hands"**: Sanity drains 20% slower.
    - `early_bird` "Early Bird": +10% shift pay before noon.
    - `sleuth` → **"Tipster"**: the onchain detective NPC pays you for tips.
    - `rug_veteran` "Rug Veteran": a special look on your REKT card.

    Suggestion for Iyke: align `ids/traits.json` `label` with these, and give each trait a player-facing `effectText`
    beside the machine `effect`. Then every screen (profile, other players' cards) shows the same words, not only the
    creator. The client keeps the server's own `effect` text whenever it differs from the rc1 string it was written from,
    so a changed effect is never contradicted.
