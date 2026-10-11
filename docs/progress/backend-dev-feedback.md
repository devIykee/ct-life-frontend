# Feedback on CT Life REST API 1.0.0-rc1 (from the Unity client)

For: deviykee (github.com/devIykee/ct-life-backend). Prepared by the Unity client agent on 2026-10-05; the user forwards it.
Spec used: `contract/backend-dev/openapi.json` (OpenAPI 3.1.0, "CT Life REST API" 1.0.0-rc1, 90 operations,
46 schemas, extracted from the Redoc page). Realtime (Colyseus) schemas were not part of it.

**How the client consumes it:** openapi-generator-cli **7.25.0** (Maven Central), generator `csharp`, models only:
`--global-property models,supportingFiles=false --additional-properties=packageName=CTLife.Rest,targetFramework=netstandard2.1,nullableReferenceTypes=false,library=httpclient,validatable=false`
(`client/Tools~/codegen_rest.sh`). Generation itself succeeds (107 model files; only warning: "OpenAPI 3.1 support is still
in beta"). The models are then compiled with Unity 6000.3.25f1's compiler. The client builds today because a local patch
script works around items 1 and 2. Per your rule ("any codegen failure is a contract bug"), here they are.

## Codegen failures / blocking

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

## Schema problems that make the client fragile (non-blocking)

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

## Notes, no action needed
- `servers` include `/v1`; the client now uses `http://localhost:4010/v1`, `https://staging-api.ctlife.dev/v1` and
  `https://api.ctlife.dev/v1` as REST bases.
- `POST /auth/dev` takes `{wallet}` only; the client no longer sends `isSeeker`.
- `AuthChallenge.issuedAt`/`expirationTime` are `format: date-time`. The client parses the challenge raw and signs the
  exact strings you send, so formatting is never altered before SIWS verification.
- Realtime schemas (`contract/realtime/*.ts`, Colyseus 0.18) aren't in this file. The client still uses a local stand-in
  until it can run `schema-codegen` on your `contract-v1.0.0-rc1`.
