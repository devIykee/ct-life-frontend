READY FOR COORDINATOR UNITY VALIDATION: `client/Assets/Scripts/Features/{Social,CityLife,WorldLife}` changed files — connect free-call RTC/end and make server-read failures truthful.

# Backend connection audit: Social, City Life, World Life · 2026-10-12

Scope: current frontend and backend **`origin/main` at `93dfabf`** (backend read-only), re-audited after the coordinator fetched upstream. Preserve accepted UI. No hosted writes, paid-provider requests, wallet signatures or charges. Disposable developer accounts only on coordinator-owned localhost services. The first smoke targeted an older `eefa667` checkout on port 4010; current findings below use the isolated `93dfabf` server on port 4011.

## Implemented

- Free-call media grants use backend `video` room kind; paid-call media grants use `paid_call`. Previous `call` kind cannot find backend participant rows and returns `403 FORBIDDEN`.
- The outgoing free-call host now sends `POST /video-calls/{id}/end` on hangup/failure/timeout. If the user hangs up while creation is pending, the eventual server call is also ended. Uncertain ends retain their idempotency key and retry while the signed-in social hub remains alive. Media stops immediately; failed server acknowledgement is displayed honestly. Callees do not invoke this host-only operation.
- Each in-flight call media join now owns a cancellation source. Hangup cancels pending token fetch; a successor call can start immediately. Ownership checks after token fetch and nearby-voice disconnect keep an old callback from clearing or disposing a newer call. Removed stale embedded-grant reuse.
- Space unmute still requires a fresh server grant. Removed a legacy fallback that treated a missing RTC endpoint as publish permission.
- Server `VALIDATION_FAILED {reason: blocked}` displays a blocked-message error instead of a generic invalid-input message.
- Week preserves failed Meta/events reads as errors with retry. It no longer converts a failed events request into “Quiet timeline”, nor missing Meta endpoint into “not announced”. Unknown/disconnected Market Mood is omitted from the share recap instead of fabricated as 0%. Re-reading the current joined city handles a first server mood of exactly zero that emits no change event.
- Homes remain browsable and the current home can renew. Noncurrent Move/Downsize buttons are disabled with an unavailable explanation, and the mutation method independently rejects a different target before POST. The existing renewal endpoint can debit Cash without moving, so profile readback alone was not a sufficient guard. A renewal response is still followed by authoritative profile refresh.
- City Life needs actions use the coordinator’s shared `NeedsCodec` for fractional server values.
- The world Timeline no longer claims “live” unconditionally while displaying retained events/local NPC presentation. World surface polling distinguishes a missing entity from a missing route; an entity 404 no longer disables the surface for the entire session.

## Observed current localhost wire checks

Current receipt: `/Users/mac/Documents/codes/projects/ct-life-evidence/backend-connectivity-20261012/social-current/receipt.json`, `backendCommit: 93dfabf`, port 4011. Reproducible bounded probe: sibling `probe.py`. Tokens remain in memory and are not written to receipts. Both disposable accounts log out at the end. All temporary Spaces/free calls were ended.

56 checks: **44 passed, 12 failed contract/authorization checks**. These include multiple checks of one Spaces defect and one alternate promotion route that the existing client already handles. A passing token response establishes grant issuance, not actual microphone/video transport. No Unity/device execution is claimed by this lane.

| Surface | Observed |
| --- | --- |
| Profile, needs | Auth, `/me`, eat action, and hunger readback passed. |
| Homes catalog | `/homes` returns five authoritative rows. Moving/visiting remains blocked below. |
| Daily clue / Weekly Meta | Daily clue GET passed. Meta returned a valid entity 404 in the fresh database, not a missing route; the older populated database returned 200. Daily answer not submitted; backend supports `{attempt}`, client intentionally sends both contract `answer` and backend `attempt`. |
| Shop / inventory | GETs passed. Purchases/equipment not performed by this lane. |
| Season pass | `/season-pass/me` correctly returned entity 404 “no season pass” for a new user; this is not a missing route. Purchase/fulfilment remains unverified. |
| DMs | Send, history, sender and recipient inbox readback passed. Blocking hides the peer from inbox and rejects reciprocal send with `400 reason: blocked`. Upstream `03b650d` added the inbox; it matches the existing client parser. |
| Businesses | `/businesses` returned `{owned:[]}`. Upstream `c93a47e` implements catalog/list/buy/collect/sell with fields matching the client. This lane did not buy or accrue a business; coordinator/economy-lane validation owns those mutations. |
| Timeline | Create/readback, follow/following feed readback, and unfollow passed. |
| Spaces | Create, hand, promote via existing path, readback, demote, end and ended readback passed. Contract promotion path 404s; existing client fallback works. Host/speaker grants are false, a listener can request true and receive it, ended Spaces still mint tokens, and room IDs are double-prefixed. Details below. |
| Free video calls | Start passed. Old `call` grant returned 403; both `video` grants passed. Host end returned 204; reconnect grant after end returned 403. Actual LiveKit audio/video session remains untested. |
| Ads | `/ads/live` read passed with no proof of paid creative display. URL gap below. |

Historical receipt: `../social/receipt.json` records **48 checks, 37 passed** on `eefa667` / port 4010. Its DM-inbox and businesses failures are superseded by the current source and runtime checks above; they are not current backend requirements. No backend files were changed by this lane.

## Minimum backend completion requirements

These affect visible shipping phone/world features, not merely unused helper APIs.

1. **Spaces voice** (`server/api/routes/rtc.ts`, `application/livekit.ts`, `application/spaces.ts`): derive `canPublish` from authenticated host/speaker membership and live room status. Both the host and a promoted speaker currently receive `canPublish:false`. The current smoke also verified that a listener sending `canPublish:true` receives true, and an ended Space still gets a new token. Normalize room IDs: `spaceId` already contains `space:`; current minter and room-service helper produce `space:space:<uuid>` while DB voice room is `space:<uuid>`. Acceptance: host/speaker true, listener false even if client requests true, demoted participant false, ended Space denied; actual audio connects and listener counts match webhook persistence.
2. **Cross-device follows/blocks**: DM inbox is implemented and verified. Follows/blocks still need authenticated read endpoints for cross-device restoration; current backend exposes writes only. Frontend device-retained knowledge cannot establish complete server state.
3. **Homes move/renew**: `POST /homes/{kind}/rent` currently only pays an existing owned home row; it never updates character `home_id`. `POST /homes {kind}` instead creates an additional rental, also without moving the character. Implement an atomic move/renew contract: debit once, set current home, terminate superseded lease, return authoritative receipt, verify via `/me`; do not charge multiple old homes weekly. Client disables and rejects noncurrent targets before POST until a reviewed move contract is implemented.
4. **Home visits/settings/gallery**: visit currently returns `{visitId,homeId,visitorUserId,visitedAt}`, without `roomId`; the client cannot enter a granted interior. Return a server-authorized home-room ticket after owner visitor policy/block checks, and enforce the same policy on room join. Implement GET/PUT `/homes/me/settings` `{visitors:open|followers|closed}`; GET `/nfts/me?limit=100`; GET `/homes/{ownerId}/gallery`; PUT `/homes/me/gallery` with ownership checks. These currently return explicit missing-route 404s. The existing home room by itself does not establish authorization or movement support (coordinator audit).
5. **Paid calls/creator settings**: implement agreed `PUT /creator {enabled,ratePerMinUsdc,maxMinutes}` and authenticated readback; paid creation takes `{creatorId,minutes}`, derives creator rate server-side and returns `{callId,sessionPda,tx}` for the escrow flow. Current `POST /paid-calls` expects payer-supplied `{payeeUserId,pricePerMinuteCents,maxMinutes}`, returns a different Cash/livekit model, and its end body requires `{secondsUsed}`. Do not use a frontend field rename as a substitute for approved server-priced escrow. Current PUT404 / start400 were observed, with no funds spent.
6. **Week events**: implement authenticated GET `/events/active` returning `{events:[{eventId,at,params}]}` or agreed server-state snapshot. Current404 is now shown as unavailable rather than quiet.
7. **Ad delivery**: `/ads/live` currently returns `imageKey` only (`application/ads.ts:LiveAd`), not a fetchable `imageUrl`. Return an HTTPS creative URL with bounded expiry/refresh support; retain Sponsored label. Current frontend correctly rejects unresolved private storage keys and shows clearly house content. Empty-list 200 does not prove paid placement delivery.
8. **Needs server rules**: `NeedsService.apply` increments stored values and resets `last_action_at`; it does not materialize elapsed decay for every need before the update, charge configured `costCents`, enforce `cooldownSec`, or REST venue restrictions. Client connection exists; backend authority for these rules remains incomplete. Never rely on client gates to enforce the economy.

## Concrete server patch acceptance: Spaces and homes

**Spaces.** Use a single canonical room-ID helper across token issuance, room-service calls, persisted voice rooms and webhook lookups. Accept a bare Space UUID or the existing single `space:` prefix, then produce one `space:` prefix; reject mismatched kinds. Token issuance must load the live Space, use authenticated caller identity, check relevant blocks/access, and derive publish permission from persisted host/speaker membership. Ignore or reject client-supplied permission flags. Grant microphone publishing only where the voice product needs it. A listener token stays subscribe-only; an ended/missing/inaccessible Space cannot obtain a token.

Promotion/demotion must update both persisted role and LiveKit participant permissions with observable retry on service failure, rather than swallowing failures. Revoking LiveKit `CanPublish` removes published tracks; this needs a two-participant audio test, not only a signed-grant test. [LiveKit participant permissions](https://docs.livekit.io/intro/basics/rooms-participants-tracks/participants/)

Use short initial join-token lifetimes and check role/status each time a new token is minted. LiveKit Cloud and self-hosted revocation differ: self-hosted permission updates/removal do not invalidate a previously issued token, so test the actual deployment's rejoin behavior. [LiveKit token grants and revocation](https://docs.livekit.io/frontends/reference/tokens-grants/)

Required negative cases: listener requesting publish true, unrelated/nonmember identity, demoted member, ended Space, wrong kind/prefix. Required positive cases: host and promoted speaker can publish; all clients join the same room; promotion, demotion and end reach media service; webhook listener counts update that same persisted room. The existing alternate promotion route is already handled by the client and does not need a duplicate route merely to pass the diagnostic probe.

**Homes.** Agree one atomic move operation that locks current lease and balance, validates authoritative offer/eligibility, debits once under the retained idempotency key, replaces the active lease and updates `characters.home_id`. Renewal must extend the current lease without creating another active weekly charge. Return the resulting home and balance and read them back with `/me`. Until this exists, keep the frontend's noncurrent-home guard; do not enable it because a catalog row or renewal endpoint exists. A future capability such as `homeMoveV1` is a proposal only, not a current server/client flag.

Visits must produce an authenticated, expiring room authorization after applying owner policy (open/followers/closed), block state and home existence. Home room joins must derive owner/home from that authorization and stored lease, never trust supplied `ownerId`/`homeId`. Add authorized movement/chat handlers before the client enters this room; the present room is not equivalent to the city controller. Test owner entry, allowed visitor, closed home, blocked visitor, forged owner/home options, expired authorization and reconnect after access changes. Gallery writes also require verified wallet ownership and bounded media references.

## Automated validation

Added `CTLife.SocialFeatures.Tests.SocialBackendRequestTests` (grant room kinds, idempotent end retry, precise terminal error, blocked mapping), `SocialViewFocusTests.HangingUpDuringGrantFetchDoesNotStrandTheNextCall`, `CTLife.CityLife.Tests.WeekRecapTests.DisconnectedRecapDoesNotInventZeroMarketMood`, the Week unavailable UI case in `CityRouteAvailabilityTests`, `HomeLeaseActionTests` (four current/other/missing-ID cases), and `CTLife.WorldLife.Tests.WorldLifeRouteTests`.

`bash client/Assets/Scripts/Features/Social/Tests~/run_social_tests.sh` **failed before compiling Social**, because its older shared `compile_check.py` dependency set omits GLTFast references required by existing `World/Likeness/LikenessAvatarLoader.cs`. Output: `CTLife.World FAILED (8 errors)`; `CS0246: GLTFast / GltfImport / IMaterialGenerator / ICodeLogger / IGltfReadable could not be found`. This is not a test pass. Coordinator owns canonical Unity compilation, Editor tests and Seeker verification. `git diff --check` passed for the current working diff at handoff.
