# Seat approach and authoritative pose proposal

[Inference] Recommendation: separate a reachable walking approach from the immutable seated anchor. Only a server-confirmed occupied spot enters seated mode. Seated mode suspends the NavMesh agent and movement publication; standing restores a validated walking point after the authoritative spot clears. This lets furniture have real navigation exclusions while preserving every wire ID and anchor.

The original audit below did not change runtime/backend source. A later root-authorized runtime candidate is now staged outside Assets; see the implementation update at the end. No backend source was changed.

## Verified current flow and defaults

| Step | Existing source behavior |
|---|---|
| Tap | `WorldInput.Tap` raycasts colliders, then finds an animated `SpotAnchor` within 0.9 m of the hit. This is a client tap-selection radius, not a server claim radius. |
| Approach | `VenueSession.OnSpotTapped` calls `LocalPlayerController.GoSit`; that resolves the exact anchor and calls `MoveTo(anchor, spotId)`. `MoveTo` samples NavMesh within 1.5 m and checks the walkable rectangle. It does not choose a deliberate furniture-side approach. |
| Arrival | After path remaining distance falls below stopping distance 0.05 m + 0.02 m, `Update` sends `Sit(Venue.WireSpotId(...))`. |
| Confirmation | `VenueSession.OnPlayerChanged` invokes `OnServerSeat` only for a nonempty changed spot ID. Empty local spot IDs and action changes on the same spot are not reconciled. |
| Visual seating | `OnServerSeat` cancels movement, calls `NavMeshAgent.Warp(anchor)` without checking its result, applies the anchor yaw and starts the server action. The avatar remains controlled by the agent. |
| Standing | `StandIfSeated` clears local state immediately, sends `stand`, and changes to `loco_idle`. It is called by `MoveTo` and direct-input entry, both after an `AgentReady` guard. Those guards will block a naive implementation that simply disables the agent while seated. |
| Movement | Runtime agent radius 0.3 m, height 1.75 m, stopping distance 0.05 m. Venue builders currently use default build settings with radius 0.5 m / height 2 m. `Update` publishes transform changes at the channel's 10 Hz throttle even when `_seatedSpot` is set. |
| Grounding | `RefreshGrounding` requires an active agent, samples NavMesh within 0.75 m, raycasts down and changes `agent.baseOffset`. It cannot be reused unchanged for a deliberately off-NavMesh seated anchor. |
| Other clients | `RemotePlayer.Apply` already renders an occupied spot at its exact anchor independently of network x/z, then plays the server `act_*` animation. This supports the proposed local separation. |

Relevant files: `client/Assets/Scripts/World/Input/WorldInput.cs`; `World/Players/LocalPlayerController.cs` (GoSit/Stand/OnServerSeat/Update); `World/Venue/VenueSession.cs` (OnPlayerChanged/OnRejected/OnSpotTapped); `World/Venue/VenueSceneRoot.cs` (TryGetSpot/WireSpotId); `World/Players/RemotePlayer.cs`; `Net/Realtime/Channels.cs`; `ProjectSettings/NavMeshAreas.asset`.

## Contract and backend findings

The imported `docs/spec/contract.md:383–384` says `sit` requires an existing free spot. `contract/realtime/messages.ts:25–29` defines `{spotId}` and empty `stand`. Neither specifies a claim-distance/radius rule. Do not invent a server radius from the client 0.9 m tap threshold.

Read-only inspection of the sibling backend at commit `a5eafda4be78f3b5d127cbd6105304de83336832`, `server/realtime/rooms/venue.ts:236–275`, shows:

- `handleSit` validates existence and occupancy, sets `spotId` and animation, and has **no distance check**. It does not move player x/z or the velocity-validation lastX/lastZ values.
- `handleStand` clears the occupied spot and sets `loco_idle`; coordinates stay at their last walking location.
- `handleMove` validates speed ≤6 m/s and walkable rectangles, updates coordinates, and **clears an occupied spot**.
- This checkout still chooses `kind === 'stage' ? 'act_type' : 'act_sit'`, ignoring `spot.anim`. This is already recorded in `docs/progress/backend-dev-feedback.md` item 13. A work-seat pose rendered offline with `act_type` is not evidence that the connected backend supplies that action correctly.

This is source-checkout evidence, not verification of deployed-server behavior. No backend files, imported contract or positions were changed.

[Inference] Current client seating can publish its visual anchor warp as a normal move; the inspected server would then release the seat. Since local empty-spot updates are ignored, the local avatar can remain visually seated while server ownership has cleared. The implementation must explicitly suppress seated movement publication and reconcile clear/rejection paths.

## Bounded client change

1. **Resolve a real approach.** Preserve `SpotAnchor` and `WireSpotId`. Add optional frontend-only approach points beside a chair, or derive a small deterministic set in the anchor's yaw frame: left/right around 0.85–1.10 m, then a rear fallback around 1.0 m. These are proposed local art distances, not a contract claim radius. For each: small NavMesh sample radius, walkable-rectangle check, complete path from the current agent and adequate capsule/obstacle clearance. Choose the shortest valid path. If none exists, report unavailable instead of walking through the chair. Prefer explicit authored side points for closely packed stage chairs.
2. **Track approach / awaiting-confirmation / seated / awaiting-stand separately.** Store the chosen validated approach and last walking/wire position. On arrival, send one `sit` with the unchanged qualified wire ID. Keep the actor standing at the approach until the matching server `spotId` arrives. A rejected request clears pending state and leaves the actor there. New input, pause or travel cancels the local intent; a late seat acknowledgement must be released/reconciled rather than reviving stale movement.
3. **Render exact authoritative seating.** On a confirmed spot, cancel path/input, stop velocity/footsteps, disable NavMesh transform ownership, and set the whole avatar transform to the exact authored anchor/yaw. Use the server animation; do not substitute local work-seat animation to hide a backend disagreement. Suppress all `move` publication while seated. Keep the last walking/wire position separate from the visible seated transform. No model, source clip, anchor or arbitrary root-height offset is part of this navigation change.
4. **Handle the full authoritative state.** Reconcile occupied→empty spot transitions, a changed spot, an updated action on the same spot, rejected sit, correction, reconnect and teardown. Normalize local versus qualified IDs for comparisons; `TryGetSpot` already accepts both. Do not call `Stand()` again when merely processing the server's empty-spot acknowledgement.
5. **Stand before the AgentReady guard.** Direct input and path requests first request `stand` from seated mode. After the server clears the spot, restore the agent at the saved reachable approach (or a newly validated nearby point), restore navigation grounding and locomotion, reset motion/network/footstep baselines, then honor the queued movement intent. If no safe restoration exists, stay in an explicit unavailable state rather than warping through furniture. Do not transmit the seated visual anchor as an intermediate walking position. The inspected backend's x/z remain at the approach, making restoration compatible with its speed validator.
6. **Then give chairs and desks real exclusions.** Candidate art may add chair/desk navigation volumes once the seated anchor no longer has to be part of the standing NavMesh. Native validation must prove a complete path to an approach, an exact confirmed seated anchor, and a safe stand point. Merely sampling an anchor within 0.75 m and reporting `PathComplete` is insufficient. Preserve the actual furniture seat/back contact review as a separate motion/art gate.

One implementation caution: `Update`, `SetMoveInput` and `MoveTo` currently return early when the agent is not ready. Explicit seated/stand state handling must occur before those guards. Otherwise disabling the agent would make a seated player unable to stand via movement input. `SnapTo`/rejection handling likewise needs the current authoritative occupied/empty state; it must not blindly reactivate navigation in an occupied seat.

## Narrow acceptance cases

- An occupied chair/desk has a real exclusion: direct walking cannot pass through it; an approach remains reachable.
- Sit sends one qualified ID from the approach; the visible actor stays standing until acknowledgement.
- Confirmation renders exact anchor XYZ/yaw even when that point is excluded from walking NavMesh; no seated `move` packets are sent.
- A second claimant receives `SPOT_TAKEN` and remains at its approach; no invented occupancy or local forced seat.
- Empty server spot state, direct stand/move, rejected sit, pause/travel and late acknowledgement leave no stranded disabled agent or stale pending seat.
- Stand resumes at the validated walking position without a speed-rejected visual snap packet. Remote observers release the same seat and return to locomotion.
- Connected work seats receive the correct server action; until the inspected backend action mismatch is resolved/verified, only offline posture sampling can be claimed for `act_type`.

Native B2 evidence currently rejects seat contact even before this navigation integration: both avatars' foot surfaces are below the floor and chair backs intersect posed bodies. Motion's read-only seated FootIK matrix and the furniture fit sweep are investigating those independently. Neither this proposal nor a nav-path pass accepts the existing pose.


## Implementation candidate update

The staged patch is at `art/gameplay-candidates/seat-approach-20261006/`. Four runtime copies and a native Play-mode fixture compile against installed Unity; native execution is pending root ownership. This supersedes the original proposal's wait-for-empty-ack recommendation: local stand now restores the saved validated standing pose immediately and ignores released-seat echoes, because sit/stand may coalesce to unchanged empty schema state. The optional authored approach falls back to existing anchors; no automatic cardinal approach generator was added. Exact immutable seat positions/wire IDs and all backend files remain unchanged.

See the candidate README for baseline hashes/import steps, the null-channel native fixture's precise limits, and unverified connected-message/reconnect cases. Compile success does not accept native seating, live occupancy, or furniture/body contact.

## Native component acceptance

Root's Unity v6 review exited 0:31 checks passed with zero protected drift across 2,539 hashed files. Retained receipts: `art/gameplay-candidates/seat-approach-20261006/revision-5/native-acceptance-v6/`. The accepted runtime requests SetDestination after a complete-path preflight, waits for actual completed navigation, and rechecks complete path/end distance before a single claim. Native controls rejected SetPath in both same and next frame after Warp/re-enable, while SetDestination accepted requests; no blanket sleep was used.

Live component code is revision 5. Isolated null-channel component acceptance is not connected-server, physical-furniture or phone acceptance. Frontend backend-feedback item 31 records the uncorrelated delayed-rejection gap for a newer already-dispatched claim.
