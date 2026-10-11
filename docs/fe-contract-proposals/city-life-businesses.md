# City Life: businesses and bad days (build spec #30)

[Proposal] Additive. The contract has the catalog (`config/businesses.json`) but no endpoint. The client ships a
catalog view plus a gateway (`IBusinessGateway`) that calls the routes below and turns read-only when the server answers
404 without a contract error body (UNAVAILABLE). No money or roll happens client-side.

## 1. Catalog (contract today) and additive config fields

`businesses.json` rows stay as they are (`id, name, costCents, incomeRangeCents[2], badDayChanceBps, badDayMultiplier`).
Add, all optional:

| Field | Meaning | Client use |
| --- | --- | --- |
| `payoutPeriodHours` | How often income accrues (e.g. 24) | "per payout" copy, next payout time |
| `sellBackBps` | Sell-back price as bps of `costCents` (e.g. 5000) | "Sells back for $X" |
| `maxOwned` | Per-player cap | Disable Buy at the cap |
| `badDays` | `[{id, weight}]` reasons rolled on a bad day | Copy per reason (FE writes words) |

The client derives display-only numbers and labels them "estimate": bad-day pay = `floor(min × badDayMultiplier)`
(config README definition), expected payout `(1 − p)·mean + p·badDay` [Inference: assumes uniform income in the range],
break-even payouts = cost / expected.

## 2. Endpoints

```
GET  /businesses                      → { owned: Business[] }
POST /businesses        {kindId}      → Business                 errors: INSUFFICIENT_CASH, FORBIDDEN {reason: "max_owned"}
POST /businesses/{id}/collect         → { collectedCents, cashCents, days: BusinessDay[] }
POST /businesses/{id}/sell            → { refundCents, cashCents }

Business    { businessId, kindId, boughtAt, pendingCents, nextPayoutAt, sellBackCents, lastDay: BusinessDay }
BusinessDay { kind: normal | bad_day | great_day, payCents, eventId?, at }
```
Every POST takes `Idempotency-Key`; replays return the first response (contract section 1). Income accrues server-side
per period (rolled with the bad-day table); `collect` moves accrued Cash to the ledger. The city room may push
`event {eventId: "evt_business_bad_day", params: {businessId, reasonId}}` so the owner sees a bad day as it happens
(new `ids/events.json` id).

## 3. CT-native bad days (FE writes the words; ids for `badDays`)

Grounded in observed CT events (research files below); parody names only.

| Business | Reason id | Copy |
| --- | --- | --- |
| KOLs Agency | `kol_exposed` | A sleuth thread says your top KOL faked his PnL screenshots. Clients paused. |
| KOLs Agency | `kol_wallets_leaked` | Your clients' wallets showed up in a public KOL wallet database. |
| Audit Firm | `audit_exploited` | A protocol you audited got exploited on Audit Row. A bot called Yoinker front-ran the hacker; your reputation didn't make it. |
| Memecoin Lab | `dev_faked_death` | Your dev faked his own death on stream and rugged. |
| Memecoin Lab | `launch_bundled` | Sleuth thread: your launch was "fully bundled". |
| Memecoin Lab | `holder_rewards_change` | Pomp.fun changed its rewards overnight. Your lab's launch plan is cooked. |
| Lounge | `flash_crash` | Oil shock: hundreds of millions in longs gone in ten minutes. Nobody's ordering bottles. |
| any | `exchange_hack` | Bitgot froze withdrawals after a "maintenance event". Your treasury is stuck. |

## 4. Backend checkout vs this proposal

`ct-life-backend` @ 9f3e5ef has `POST /businesses {kind}`, `GET /businesses/{id}`, `POST /businesses/{id}/run` with kinds
`lemonade_stand | laundromat | cafe | shop | arcade` and a hard-coded bad-day table, while the contract config lists
`kols_agency | audit_firm | memecoin_lab | lounge`. Proposal: adopt the contract kinds (CT-native) and the routes above;
`run` becomes server-side accrual + `collect`.

## Sources

- `contract/config/businesses.json`, `contract/config/README.md` (badDayMultiplier definition), build spec feature #30.
- `ct-life-backend` @ 9f3e5ef `server/api/routes/businesses.ts`, `server/application/businesses.ts` (read-only).
- `docs/research/ct-x-observations-2026-10-04.md` (faked PnLs, bundled launches, fake-death rug 3 Oct, flash crash 22 Aug)
  and `docs/research/ct-now-2026-10.md` v0.1 (KOL wallet DB 3 Oct, MEV "Yoink" 15 Sep, oil-shock liquidations 6–7 Oct,
  launchpad Holder Rewards 12 Sep, exchange hack report 24 Sep [Unverified there]).
