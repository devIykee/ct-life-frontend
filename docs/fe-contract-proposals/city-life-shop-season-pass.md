# City Life: shop, wardrobe and season pass (build spec #33, #35)

[Proposal] Additive. The client calls the contract routes (`GET /shop`, `POST /shop/purchase`, `GET /inventory`,
`PATCH /characters/me/look`, `POST /payments/intents {kind: season_pass}`, `GET /payments/intents/{id}`) and one proposed
read (`GET /season-pass/me`).

## 1. Season pass

The contract can sell a pass (`kind: season_pass` intents) but cannot say whether the player has one, and there is no sku.

```
GET /season-pass/me → { tier: "season" | "free", startedAt, expiresAt, seasonId }   404 NOT_FOUND = no pass
config "seasonPass": { "sku": "season_1", "seasonId": "s1", "priceUsdc": 5, "endsAt": "…", "perks": ["…"] }
```
- Purchase: `POST /payments/intents {kind: "season_pass", sku: config.seasonPass.sku, currency: "USDC"}` → MWA sign →
  poll → `payment_confirmed` (same as top-ups). The client shows the amount from the intent, never from config.
- The backend checkout already serves `GET /season-pass/me` in that shape, but sells the pass with **Cash**
  (`POST /season-pass/buy`, $5 Cash). Spec: "the 5 USDC season pass" is real money (Economy, revenue lines). Proposal: drop
  the Cash buy, keep the intent route.
- Perks are server-applied (`POST /season-pass/claim` in the backend); the client lists `perks` copy only.

## 2. Shop and inventory shapes

| | Contract | Backend checkout | Proposal |
| --- | --- | --- | --- |
| `GET /shop` | `ShopItem[] {id, slot, name, priceCents}` | `{items: [{itemId (uuid), slot, itemKey, name, priceCents, onchainOnly}]}` | Contract shape; add optional `onchainOnly`, `salePriceCents` (for the `cosmetics_sale` meta) |
| `POST /shop/purchase` | `{itemId}` (registry id) → `Inventory` | `{itemId: uuid}` → one inventory row, 201 | Contract (registry ids end to end) |
| `GET /inventory` | `{items: [{itemId, slot, acquiredAt}]}` | rows add `inventoryId, itemKey, name, source` | Contract + optional `source: shop | mint | grant` |

The client tolerates both today (purchases by whichever id the server lists, wears by `itemKey`/registry id).

## 3. Cosmetic ideas (BE owns `ids/items.json`; parody only)

Grounded in what CT shows off now (research below): conference lanyards, VC fleece, ring lights, laser eyes (ironic),
PnL-card merch, marina linen.

| id | slot | Note |
| --- | --- | --- |
| `acc_lanyard_coin2050` | acc | "COIN2050 Singapura" badge (TOKEN2049 parody), Conference week drop |
| `acc_lanyard_checkpoint` | acc | "Checkpoint London" badge (Breakpoint parody), November |
| `top_fleece_vest` | top | VC Row uniform |
| `acc_ring_light` | acc | Caller/KOL tribe flex |
| `head_laser_eyes` | head | Maxi joke (fading slang = ironic) |
| `top_tee_pnl_card` | top | "+$40k → liquidated" print |
| `top_linen_marina` | top | Dubai Relocator, Marina Villa set |
| `acc_gm_mug` | acc | gm coffee emote prop |

## Sources

- `contract/openapi.yaml` (`ShopItem`, `Inventory`, `PaymentIntent`), build spec Economy + features #33/#35.
- `ct-life-backend` @ 9f3e5ef `server/api/routes/cosmetics.ts`, `server/application/cosmetics.ts`,
  `server/api/routes/season-pass.ts`, `server/application/season-pass.ts` (read-only).
- `docs/research/ct-now-2026-10.md` v0.1 (parody names COIN2050 Singapura / Checkpoint, slang status, "+$40k → liquidated"
  clip format), [TOKEN2049 Singapore](https://token2049.com/singapore/), [Breakpoint](https://solana.com/breakpoint).
