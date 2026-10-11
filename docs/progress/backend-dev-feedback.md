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
