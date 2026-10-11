# Proposed character interaction messages — UNAPPROVED

2026-10-08, character-feel lane. Frontend proposal for devIykee. This does not change the imported contract, backend or
bundled config. Everything below already works on the frontend with a **local-only fallback**; these additions only
let other players see more of it.

## What already works on the current contract (no change needed)

- Seats, benches, sofas, stage chairs, terminals and the host's mic spot are claimed with the existing
  `sit {spotId}` (registry wire id) and released with `stand {}`. Other clients present the sit-down / stand-up from
  `PlayerState.spotId` / `anim` (same presentation as the local player; NPCs use it for `act` at a seat).
- The host stage spot (`kind: stage`, `anim: loco_idle`) is presented as `act_host_mic` on every client by a shared FE
  rule (`OccupiedSpotAnimation`), so no server change is needed for the mic pose.
- A knock-down of the local player is shown to the room with the existing `emote {emoteId: "react_liquidated"}`
  (a registry anim). [Unverified] whether the server accepts `react_*` ids in `emote`; if it rejects them, the fall
  stays local (the client already handles `rejected`).

## Proposed additions

### 1. `interact` (client → server, venue room) for non-seat interactions

For things that are not exclusive seats: leaning on a counter, using a door/vending machine, reading a board.

```ts
export const InteractMessage = z.object({
  targetId: z.string().min(1),          // registry spot id (wire form), e.g. "terminal_cafe.counter"
  action: z.enum(['lean', 'use', 'leave']),
});
```

Server: validate the spot is in the room's venue and within ~3 m of the player's last `move`; set two new
`PlayerState` fields (below) and clear them on the next `move` beyond 0.5 m, `sit`, or leave. Not exclusive (no
`SPOT_TAKEN`). Rate limit as `emote` (1/s).

```ts
// PlayerState additions (additive)
@type('string') interactId = '';   // targetId while engaged, '' otherwise
@type('string') interactAction = '';
```

Fallback today: the client engages locally (`InteractionMachine` LocalOnly); others see the player standing at the
counter without the lean.

### 2. Reaction ids for physical moments (FE-owned `ids/anims.json`, additions only)

```json
["react_stumble", "react_fall_forward", "react_fall_back", "react_get_up", "react_land_hard"]
```

Sent through the existing `emote` message (or a dedicated `react {animId, dirY}` if the server prefers to keep emotes
cosmetic-only). `dirY` (radians) would let other clients play the fall in the right direction. The client already has
the clips (`react_stagger`, `react_rug_pull_fall`, `act_get_up`, `react_roll`); the ragdoll itself stays local.

### 3. Seated pose variants (optional, later)

`PlayerState.anim` is server-set to `act_sit | act_type`. Allowing the client to request one of
`act_sit | act_sit_talk | act_lean_back | act_phone_hold` while seated (e.g. `sit {spotId, pose}`) would let players
change how they sit and have everyone see it. Until then the CTA offers no seated pose changes (a local-only pose would
show something to the player that no one else sees).

## Test coverage on the frontend

`client/Assets/Tests/EditMode/InteractionTests.cs`: payload JSON for `sit` / `stand` matches `messages.ts`
(`{"spotId":"terminal_cafe.table_1"}`, `{}`), local-only kinds send nothing, registry validation, and the state
machine's server-confirm / reject / timeout / rejoin paths.
