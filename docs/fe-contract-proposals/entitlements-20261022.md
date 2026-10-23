# Proposal: player entitlements (frontend, 2026-10-22)

The hovercar (vehicle spec `roadstar`, the v3 flying car) is sold as a scarce right-to-drive: a Metaplex Core NFT
bought with real money only (user decision, 2026-10-22). Game Cash can never unlock it. The 3D model stays the current
lionsharp hovercar (CC BY-SA 4.0, credited in game). The client gates the HUD's hovercar button and the summon on an
entitlement, but the backend has no endpoint yet that says what a player owns.

## Request

`GET /v1/me/entitlements` (authenticated) returns the player's owned item ids:

```json
{ "items": ["vehicle.hovercar"], "updatedAt": "2026-10-22T05:00:00Z" }
```

- Item ids are stable strings. The first one is `vehicle.hovercar`. Later drops (other premium vehicles, cosmetics) use
  the same list.
- The backend grants `vehicle.hovercar` only after it has verified that the player's linked wallet owns a hovercar
  pass NFT (Metaplex Core, in the hovercar collection), and revokes it when the NFT leaves the wallet. A Cash purchase
  must never grant it.
- After a Duo Shop purchase settles, the list includes the item. A realtime nudge such as a `entitlements_changed`
  message on the player's room would let the client refresh without polling. It's optional; the client can re-fetch
  after a purchase receipt.
- Ownership is checked server-side too. The vehicle lease for `roadstar` should be refused for a player without
  `vehicle.hovercar`.

## Until then

- `CTLife.Core.IEntitlements` (`client/Assets/Scripts/Core/Services/Entitlements.cs`) is registered with a local
  stand-in, `LocalEntitlements`, that owns nothing. Without the hovercar, the HUD button shows as locked: it is dimmed and
  labelled "Buy in Duo Shop". Tapping it shows "The hovercar is a limited right-to-drive pass. Buy it in the Duo Shop."
  and nothing is summoned.
- Development builds can grant it for testing with the launch extra `-e grantHovercar 1`. The offline showcase
  (`showcaseFleet=1` on the offline island) still unlocks every vehicle, as before.
- When the endpoint exists, a client service fetches it after sign-in and calls `LocalEntitlements.Set(items)`, or
  replaces the stand-in. The HUD and `VehicleSystem` already listen to `IEntitlements.Changed`.
