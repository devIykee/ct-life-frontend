# City Life: homes, rent, visits, needs, Sunday Unlock (Phase 4)

[Proposal] Additive changes only (CHANGELOG rule: new optional fields, new endpoints, new enum/error/kick ids).
Written by the city-life lane on 2026-10-08 against `contract/` (rc1 + Unreleased, SOURCE.md commit `9f3e5ef`) and a
read-only look at `ct-life-backend` @ `9f3e5ef`. The client already reads every field below when present and falls back
to contract-only behaviour when absent (`client/Assets/Scripts/Features/CityLife/`).

## 1. What the contract already gives, and what is missing

| Need (build spec #31, #32, #8) | rc1 contract | Gap |
| --- | --- | --- |
| List the five homes with rent | `GET /homes` → `Home[] {id, name, rentCents, nextDueAt}` | No lease state: overdue / evicted / missed weeks |
| Move or renew | `POST /homes/{homeId}/rent` → `{homeId, rentCents, nextDueAt}` | Fine |
| Eviction | — | No rule anywhere (config, contract, spec). The client never invents one |
| Visit | `POST /homes/{ownerId}/visit` → `{roomId}`; `home` room (12 clients, `HomeState`) | Visitor permissions; which interior to load |
| Needs between reads | `Me.needs` ints | No reading time; client uses the response time |
| Needs warnings | — (HUD hard-codes 25, QA §3) | Thresholds belong in config |
| Weekly recap / share card | — | No ledger summary of the week |

## 2. Proposed additions

### 2a. Lease state on `Home` (GET /homes rows)

```yaml
Home:
  properties:
    status:      { type: string, enum: [active, overdue, evicted] }   # optional
    missedWeeks: { type: integer, minimum: 0 }                          # optional, consecutive missed Sunday Unlocks
    rooms:       { type: integer }                                      # optional, mirrors ids/homes.json
    frameSlots:  { type: integer, minimum: 0 }                          # optional, NFT frames this home can show (see city-life-nft-gallery.md)
```

### 2b. Rent policy in `economy.json`

```json
"rentPolicy": { "graceWeeks": 2, "evictToHomeId": "parents_basement", "dueSoonHours": 48 }
```
Numbers are placeholders for the backend owner to set. With them the client shows "Eviction risk — 1 week left";
without them it shows "Overdue" only. Eviction itself is a server action at Sunday Unlock (move the lease to
`evictToHomeId`, keep debt).

### 2c. Visitor permissions

```
GET /homes/me/settings  → { visitors: "open" | "followers" | "closed" }
PUT /homes/me/settings  { visitors }  (Idempotency-Key) → same shape
```
`POST /homes/{ownerId}/visit` answers `403 FORBIDDEN {details: {reason: "visits_closed" | "followers_only"}}` when the
owner's setting refuses the caller, and `BLOCKED` when either side blocked the other. Switching to `closed` while
visitors are inside sends them `kicked {reasonId: "home_closed"}` on the home room (new id in `kick_reasons.json`).
Default for new players: `followers` [Inference: least-surprise privacy default; product call].

### 2d. Visit reply

```yaml
# POST /homes/{ownerId}/visit 200
{ roomId: "home:0192…", homeId: "studio" }   # homeId optional: which interior the visitor loads
```
`ownerId == me` returns the owner's own room, so the owner is present for visitors (contract is silent on how the owner
joins). The room is created on first visit and closes when empty.

### 2e. Needs reading time and warnings

- `Me.needsAt` (optional, ISO-8601): when the server computed the decayed values. The client projects
  `value − decayPerHour × hours` from it (today: from the response time).
- `needs.json`: `"warnings": {"low": 25, "critical": 10}` and optional
  `"consequences": [{"need": "energy", "below": 10, "textId": "needs.consequence.energy"}]` so any gameplay effect of
  low needs is stated by the server, not guessed by the client.
- Optional product call: `party` at home as a house party when the home room has ≥ N visitors
  (`"homePartyMinGuests": 3`). Today `party` is club-only and the client greys it out at home.

### 2f. Weekly recap

```
GET /me/week-recap?week=2026-W41 →
{ weekIso, rentPaidCents, gasPaidCents, taxPaidCents, debtDeltaCents, cashDeltaCents, metaBonus, clueWins, rentState }
```
Until then the Week app compares the server's current Cash/debt/home with the device's own snapshot and labels it
"Since you last checked" (never as a ledger total).

### 2g. Weekly Meta shape

Keep the contract shape `WeeklyMeta {weekday, bonus}` and add optional `weekIso`, `startsAt`, `endsAt`. The backend checkout
answers `{key: "current", value: {...}, updatedAt}` with a worker value `{weekIso, winnersCount, losersCount, perkCents}`
that has no `bonus`; the client reads all three shapes but can only name the bonus from the contract shape.

Theme ideas for the rotation, grounded in the research below (BE owns `meta.json`; ids are suggestions):
`airdrop_season` (points multiplier + weekly leaderboard, like the Seeker Summer rounds), `unlock_week` (VC NPCs dump at
Sunday Unlock), `holder_rewards_week` (Launchpad Tower pays holders), `regulator_week` (fees and tax copy change).
`npc_density_double` already plays as "Conference week" (COIN2050 Singapura / Checkpoint London in parody).

### 2h. Daily clue

The client sends `{answer, attempt}` (contract name + backend name; verified the backend's zod 4.6.5 `z.object` strips the
unknown key). Proposal: the contract adopts one name (`answer`), documents "one attempt per day" and `FORBIDDEN
{reason: "already_attempted"}`, and adds optional `prizeCents` to `GET /daily-clue` (the backend sends it already).

## 3. Backend checkout vs contract (for docs/progress/backend-dev-feedback.md)

1. No `GET /homes` list; there is `GET /homes/{id}` and an extra `POST /homes {kind}`.
2. `POST /homes/{homeId}/rent` takes a home *instance* id (`home:{uuidv7}`) and only pays rent for an existing home;
   contract: home *type* id, signs a lease (move/renew). Reply is `HomeRow {homeId, kind, rentCentsPerWeek, lastRentPaidAt}`.
3. `POST /homes/{ownerId}/visit` takes a home id, returns a visit-log row `{visitId, homeId, visitorUserId, visitedAt}` with
   status 201 and no `roomId`; there is no `home` room in `server/realtime/rooms/`.
4. Rent worker runs Monday 00:00 UTC (`server/workers/rent.ts`); contract/spec: rent is due at Sunday Unlock, Sun 20:00 UTC.
5. Short balances raise `VALIDATION_FAILED {reason: "insufficient cash", have, need}` instead of `INSUFFICIENT_CASH`.
6. `POST /daily-clue/answer` body is `{attempt}` (contract `{answer}`), one attempt per day.
7. `GET /meta/current` shape (see 2g).

## Sources (opened 2026-10-07/08)

- `contract/openapi.yaml`, `contract/config/{economy,needs,meta}.json`, `contract/ids/homes.json`, `contract/realtime/states.ts`.
- `ct-life-backend` @ 9f3e5ef: `server/api/routes/homes.ts`, `server/application/homes.ts`, `server/workers/rent.ts`,
  `server/workers/sunday-unlock.ts`, `server/application/meta.ts`, `server/api/routes/daily-clue.ts`, `server/application/needs.ts`.
- Seeker Summer rounds/quests/claims: [solanamobile.com blog, 12 Aug 2026](https://solanamobile.com/blog/suns-out-your-seeker-has-been-busy-this-summer).
- Conference calendar: [TOKEN2049 Singapore 7–8 Oct 2026](https://token2049.com/singapore/), [Breakpoint London 15–17 Nov 2026](https://solana.com/breakpoint).
- CT mood and parody names: `docs/research/ct-now-2026-10.md` (v0.1) and `docs/research/ct-x-observations-2026-10-04.md`.
