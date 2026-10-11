READY FOR COORDINATOR VALIDATION: `client/Assets/Scripts/Features/{Ventures,Competition}` changes listed below, plus this log. Unity compilation and EditMode execution are owned by the coordinator. Do not describe this lane as fully backend-ready.

# Ventures, Competition and personal-character connectivity — 2026-10-12

Source inspected: frontend `dev`; initial sibling backend checkout based on `eefa667`, including its existing local likeness changes; then the fetched backend `origin/main` at `93dfabf` without changing the dirty checkout. The inspected Ventures/Competition route/service files have no diff between those commits. No backend file, hosted service, real wallet, payment or generation was changed by this lane. The coordinator reports Iyke deployed likeness commits `d362d0a` and `93dfabf` with generation/payment flags off; authenticated hosted readiness and delivery are not established by this lane.

## Changes in the frontend

- Ad uploads now send raw PNG/JPEG bytes, matching the handler in `server/api/routes/push-meta-ads.ts`; the old multipart body was incompatible. The transport enforces the server's 1 MiB maximum, refuses other MIME types, retains the idempotency key and auth refresh behavior, and disables redirects. **The backend still returns 415 because its ad route has no raw-image content-type parser. This change alone does not make uploads operational.**
- Ad upload decoding accepts the implemented `creativeId` response and retains its `imageKey`, `mimeType`, `sizeBytes` for the next request. The booking body echoes this server metadata. Booking result decoding accepts `{booking:{bookingId,...},creative:...}` without manufacturing a payment intent. Existing UI reports the missing payment setup; it does not show a paid result.
- Ad payment gateway forwards the Money lane's new `SubmitIntent` call, so a future real booking intent uses the same signature reporting/recovery flow as other intents. A submission acknowledgement does not imply payment confirmation.
- Project detail/create responses use `cashCents`; the client formerly rendered zero treasury. Current detail wraps members in `hires` and dates in `hiredAt`; those now populate membership and payroll presentation. An omitted name shows the project's category instead of a blank heading; the server still needs to return the persisted name. Explicit richer server fields retain precedence.
- Tribe change now uses implemented `POST /me/tribe {tribeId}` instead of nonexistent `PATCH /characters/me/tribe`, including the retained request key fingerprint.
- Cabal lists now send the actual `hostId`/`guestId` filter with the caller ID. `role=host|guest` was ignored by the server. A used invite without an explicit status is decoded as accepted, not pending.
- Cabal invite creation and Beef creation now require explicit capabilities from a fresh network `/config` response before any mutation. The current backend does not advertise them, so the existing screen reports unavailable instead of debiting a stake or claiming a targeted invite was sent after the server discarded its recipient/topic. Browsing remains available. When the server opts in, the response must preserve the selected guest and Beef topic/stake; a mismatch is an uncertain write, preserves the idempotency key, and does not call the success UI.
- LLM refusal metadata accepts the backend's `refusalReason` field as well as the proposal's `refusalReasonId`.

No visual redesign, package addition, fabricated server state or API-success-as-feature-completion claim.

## Actual local HTTP evidence

External evidence directory: `/Users/mac/Documents/codes/projects/ct-life-evidence/backend-connectivity-20261012/ventures/`.

**Current-source rerun:** `../ventures-current/receipt.json` records 62 requests against the coordinator's isolated backend `93dfabf` at `http://127.0.0.1:4011/v1`. Its new disposable accounts reproduce the successful project/farm/tribe readbacks and the same missing/disabled routes below. It also confirms `competitionCapabilities` is absent, raw ad upload still returns415, and likeness availability explicitly remains disabled. The direct HTTP audit intentionally exercised Beef to demonstrate the server's ignored fields; the new client guard will not submit that request on this contract. Old evidence is retained separately.

- `receipt.json`: final 59-request smoke against `http://127.0.0.1:4010/v1`, using two newly generated disposable dev accounts. Tokens remained in process memory and were not logged. Safe fictional Cash was transferred between these two test accounts to cover the project seed; no real money was used.
- Project create returned 201, identical idempotency replay returned the same project, and `GET /projects?scope=mine` persisted its submitted name. `GET /projects/{id}` returned treasury 50000 cents in `cashCents` and an actual `hires` list, but omitted the name.
- Farm claim returned 200, identical replay returned the same claim, history contained it, and the farm readback reported `claimedToday=true`, amount 500 cents.
- Tribe change through the old path returned 404; the implemented path returned 200 and `/me.character.tribeId` persisted `builder`.
- Beef creation persisted a 100-cent fake stake and appeared in the caller's list. The requested opponent and topic were absent. Match detail and cancellation returned 404. Invited-player decline returned 403 (`not the challenger`); challenger decline succeeded and refunded through the server's existing flow.
- `upload-route-receipt.json`: raw PNG `/uploads` returned **415 VALIDATION_FAILED**; `/x/link/start` returned **503 X_LINK_DISABLED**; ad booking detail returned **404 NOT_FOUND**. No ad booking was submitted.
- Local likeness availability returned HTTP 200 but `enabled=false`, `reason=provider_not_configured`, `photoUploadEnabled=false`, `checkoutAvailable=false`, with server price `3.00 USDC`. HTTP 200 here is an explicit unavailable response, not successful generation.
- The initial smoke hit legitimate project seed insufficiency on an Exit Liquidity account. The final smoke added a Cash tip between disposable accounts and exercised persistence successfully. No existing player balances were edited.

## Feature matrix

“Verified” below means the stated local API operation/readback, not a full Unity/Seeker or hosted lifecycle.

| Feature / actual client surface | Wire/source result | Remaining work |
| --- | --- | --- |
| Builder: mine/hiring/all lists, creation, detail | Real database-backed GETs; create/replay/persistence locally verified. Treasury/hires parsing corrected. | Server detail/create omit saved name/ticker. `scope=hiring` currently returns all projects because no open-role records exist. |
| Builder: treasury funding | Actual UI calls `POST /projects/{id}/fund`; local 404. | Server route + ledgered treasury mutation. |
| Builder: offers / accept / decline / fire | Read offers route exists. Creating offers local 404; accept/decline/member removal absent in source. | Full consent-based hiring lifecycle; `/hire` is immediate employment/salary and must not replace offers silently. |
| Builder: launch / HQ / VC offers | Quote/HQ/VC reads local 404. Existing launch handler only flips status to `launching`. | Quote-authorized launch, actual coin/runtime linkage, project room admission, VC decision lifecycle. |
| VC Row recent deals | `/vc-row/deals` 200 and database-backed query. | No reachable VC offer acceptance producer in inspected routes; an empty list does not establish the mechanic works. |
| Ad Desk: slots / bookings | Local 200 reads of real tables. | Booking detail/availability absent; raw upload blocked415; payment/review/placement/expiry/refund lifecycle incomplete. |
| League: leagues / my membership / tribes / districts | Local 200, compatible decoding and database-backed services. Tribe mutation persistence verified after path correction. | District standings and season result routes local404; scheduled rollover/rewards still need end-to-end evidence. |
| Beef: list / open / Elo | Local reads work; direct HTTP open persists but drops terms. Client creation now blocks before POST until explicit capability. | Detail404, invitation/topic ignored, client guest decline incompatible with server challenger-refund semantics; votes/cancel absent; no automatic match settlement demonstrated. |
| Farm: status / claim / history / Sybil state | Local claim/replay/history verified; compatible status reads. | Sybil Barn list404; bail absent, but current server correctly reports `bailable=false`, so no active bail button is implied. |
| Cabal: membership / venue access / own invite lists | Local200, list filters corrected. Client targeted creation blocks before POST until explicit capability. | Create ignores requested guest and returns a bearer code; no revoke; target delivery/acceptance must be implemented. |
| NPC AI chat | Client POST/quota paths exist. Local quota503 LLM_DISABLED. | Configure real provider; production/staging explicitly reject missing key. Missing reply ID prevents reporting individual replies. |
| X link | Status GET200; start503 disabled. | Configure OAuth; browser callback auth model and unlink route need completion. |
| Personal character | Client/server route families are implemented; price from server. Coordinator reports latest deployment with flags off. Current local availability explicitly disabled. | Verify hosted configuration, approved provider end-user permission, private assets/references, worker and actual paid delivery. Deployment alone does not enable generation. |

## Backend handoff: concrete requirements for Iyke

These are source/live blockers, not requests to hide or remove the features. Contract baseline: `docs/fe-contract-proposals/phase4-6/openapi-additions.yaml` and the current client request models. Preserve explicit unavailable responses until the lifecycle exists.

### Explicit Competition capability contract (new frontend requirement)

This is a proposed server extension, **not an existing or agreed server capability**. Add the following top-level field to the actual `GET /v1/config` response only as each lifecycle below is implemented and verified:

```json
{"competitionCapabilities":{"version":1,"targetedCabalInvites":true,"beefChallenges":true}}
```

The fields must be JSON booleans and `version` must be the integer1. Omitted, false, malformed or other-version values remain unavailable. `targetedCabalInvites` certifies recipient-specific creation/delivery/authorization/revocation; `beefChallenges` certifies preserved topic/optional recipient, valid phase actions and settlement. They can be enabled independently. Never enable either merely because POST returns201. The client reads the live payload directly, not the bundled/merged config fallback, and still validates the returned guest/topic/stake. Acceptance: absent flags produce no POST; enabled flags with honest receipts permit success; dropped/mismatched fields never produce a sent toast and retain the same request key for reconciliation.

### 1. Ads: upload and paid lifecycle

Files/functions: `server/api/main.ts` content parsers; `server/api/routes/push-meta-ads.ts:registerPushMetaAdRoutes`; `server/application/ads.ts:uploadCreative`, `bookAd`, `approveBooking`, `listLiveAds`.

- Register an encapsulated PNG/JPEG `Buffer` parser for `POST /v1/uploads` with a 1048576-byte cap. The likeness parser is in another plugin scope and does not cover ads. Current request: `Content-Type:image/png`, authenticated, retained `Idempotency-Key`, raw image bytes; current result415. Accept a valid image with201; reject >1MiB and invalid formats; return stable replay; bind upload ownership, MIME, size and object key on the server.
- Current upload returns `{creativeId,bookingId:'',imageKey,mimeType,sizeBytes,uploadedAt}`. Current booking requires `{slotId,termId,uploadId,imageKey,mimeType,sizeBytes,link}`. Client now preserves these fields. Prefer deriving metadata from the owned upload record rather than trusting echoed metadata.
- `bookAd` currently inserts `pending_review` with no payment intent; `approveBooking` can make it live without a verified payment. Implement `{bookingId,intent}` or `{booking,intent}` with an owned server-priced ad intent; pending review/live only after correct payment. Keep signature verification, slot reservation, term scheduling, admin moderation, expiry and refund idempotent.
- Add `GET /ads/slots/{slotId}/availability`, `GET /ads/bookings/{id}`, `POST /ads/bookings/{id}/cancel` and expected status pushes. `GET /ads/live` must provide a usable image URL (currently only a storage `imageKey`) or an authenticated documented image fetch route that the world billboard client can use.
- Acceptance: two accounts cannot reuse each other's uploads/bookings, concurrent slot reservation has one winner, a signed-but-unverified payment never goes live, correct devnet receipt advances the same booking exactly once, approved art renders in the world, term expiry removes it, rejection exposes an actual refund receipt. No paid test was performed by this agent.

### 2. Projects: summary and lifecycle

Files/functions: `server/api/routes/projects.ts:registerProjectRoutes`; `server/application/projects.ts:rowToProject`, `foundProject`, `getProject`, `hirePlayer`, `launchProject`, `listProjects`.

- Return persisted `name` and `ticker` in create/detail/launch project records. Existing DB rows store them and list reads expose them; detail's SELECT/mapper drops them. Acceptance: same exact submitted name/ticker after create, app restart and detail fetch, alongside unchanged treasury.
- Add `POST /projects/{id}/fund {cashCents}` with owner/member validation and atomic debit/credit; acceptance: caller balance and project treasury each move once on repeated idempotency key.
- Add `POST /projects/{id}/offers {userId,role,salaryCentsPerWeek}`, `POST /project-offers/{offerId}/accept|decline`, `DELETE /projects/{id}/members/{userId}`. `/hire` presently debits owner and credits the hire immediately; do not route an “offer” button to it. Acceptance: intended guest sees a pending offer, only that guest accepts, no payroll before acceptance, accepted membership persists; authorized removal stops subsequent payroll.
- Implement `GET /projects/{id}/launch-quote` and honor `POST /projects/{id}/launch {ticker,quotedCostCents}`. Current launch ignores the body and only changes the status; it does not create a tradeable coin. Acceptance: quote mismatch safely requotes; valid launch charges once, creates the actual project/coin binding and broadcast, and survives reload.
- Add `POST /projects/{id}/hq/visit` returning `{roomName,roomId,expiresAt}` with authenticated room admission; ensure an actual realtime room serves that ticket.
- Add `GET /projects/{id}/vc-offers`, `POST /vc-offers/{offerId}/accept|decline`, and feed the existing recent-deals read from real accepted deals. Acceptance: owner-only decision, one ledger transfer/equity allocation, locked stake/payout lifecycle and matching realtime messages.

### 3. Beef: invitations, detail, votes and settlement

Files/functions: `server/api/routes/ratio-arena.ts:OpenBody`, `registerRatioArenaRoutes`; `server/application/ratio-arena.ts:openMatch`, `declineMatch`, `settleMatch`, `listMatches`.

- Current `POST /beef {stakeCents,topicId,invitedUserId?}` strips topic and invited user. Persist/validate both and emit the targeted challenge. Do not turn a selected-player challenge into a public invitation.
- Add the missing `GET /beef/{id}`, `POST /beef/{id}/cancel`, `POST /beef/{id}/votes {votedUserId}`. The source file's comment lists GET detail, but there is no registration.
- Distinguish challenger cancellation from invited-player decline. Current `/decline` only allows the challenger and refunds that challenger, while the client uses decline for the invited guest.
- Implement server phase deadlines, voter eligibility, one vote per account, hidden pre-close tallies and actual scheduled settlement. Admin-only `/settle` by itself does not fulfill the game loop.
- Acceptance: A invites B; only B can accept/decline, A can cancel only while open; accepted participants cannot vote, C can vote once, tally/result resolves at server deadline, stake/payout/refund and Elo persist and replay does not double-credit.

### 4. Cabal: targeted invitations and revocation

Files/functions: `server/api/routes/venues-gating.ts:registerVenuesGatingRoutes`; `server/application/venues-gating.ts:createCabalInvite`, `acceptCabalInvite`, `rowToInvite`.

- Consume `{guestUserId}` on create; enforce the server's host membership/net-worth/quota rules; persist guest before sending notification. Current create ignores the body, inserts no guest, returns `code`, and acceptance binds whichever non-host knows the invite ID.
- Add `POST /cabal/invites/{id}/revoke`; return explicit invite statuses and stop sending bearer codes to the client.
- Acceptance: host sees invite in hostId-filtered list, intended guest sees it in guestId-filtered list before acceptance, a third account gets403 for read/redeem, revoked/expired invites cannot admit, membership persists and gated room entry respects it.

### 5. League, districts and Farm

Files: `server/api/routes/tribes.ts`, `leagues.ts`, `airdrop-farm.ts`; services `tribes.ts`, `leagues.ts`, `airdrop-farm.ts`.

- Add `GET /districts/{id}/standings` with `{districtId,dayDate,rolloverAt,standings:[{tribeId,score}],myTribeId,myContribution}` using actual contribution data.
- Add `GET /me/season-results?seasonId=` with recorded rank/outcome/rewards; verify the scheduled rollover writes the result and rewards exactly once.
- Add `GET /sybil-barn` for server-owned inmate/status data. Bail endpoint `POST /me/sybil-status/bail` is only required when returning `bailable=true`; retain false until a real gated debit/release exists.
- Acceptance: nonempty fixture contribution changes standings, rollover changes owner/membership/result once, member-specific visibility honors anonymity, jail state limits prohibited actions and persists on fresh login.

### 6. NPC chat and X linking

Files: `server/api/routes/llm-npcs.ts`, `x-link.ts`; `server/application/llm-npcs.ts`, `x-link.ts`.

- Chat is intentionally disabled locally. A configured production provider must return a persistent owned `replyId`; current service inserts the interaction ID but omits it from the response, so the UI cannot report that reply. Acceptance: actual provider response with quota, safe refusal, and report action resolves the stored interaction. Do not count the dev stub string as provider connectivity.
- X start is intentionally disabled locally. Add authenticated `DELETE /x/link`; resolve browser callback auth: the current callback requires the app's bearer auth, but the system-browser OAuth redirect does not supply that header. Use validated one-time state/PKCE to bind the callback owner server-side; never put the app JWT in the URL. Acceptance: app→system browser→callback→app produces a verified persisted handle for the correct account; unlink removes it; cross-account/replayed state fails.

### 7. Personal-character deployment readiness (read-only audit)

Actual client routes in `Net/Likeness/LikenessClient.cs` map to `server/api/routes/likeness.ts`: availability, quote/current/submitted, photo upload/delete, job list/create/status/cancel, reference/approve, preview, manifest/result and accept. `World/Likeness/LikenessAvatarLoader.cs` consumes the validated owned manifest/model. The coordinator reports deployed `d362d0a`/`93dfabf` with flags off. Current-source local readiness is false; no authenticated hosted readiness or completed delivery was established here.

Deployment inputs are explicitly gated in `server/application/likeness-runtime.ts` and `server/shared/config/config.ts`:

- Verify deployed migrations and both API/worker versions against `origin/main`, including the likeness scheduler in `server/workers/main.ts`. The dirty local likeness migration names differ from the merged migration sequence; use the merged deployment contract rather than reapplying local draft migrations.
- Private storage needs a dedicated private bucket and configuration confirmation; current `origin/main` additionally requires separate `LIKENESS_S3_ACCESS_KEY` / `LIKENESS_S3_SECRET_KEY` and refuses reuse of the ads access key. Local disk storage is intentionally local-only. Keep models/photos authenticated and preserve owner access when new generation is disabled.
- Supply the paid provider API key privately, tested runtime profile confirmation and actual reference manifest files for supported human model IDs. Four IDs are recognized: `head_snow`, `head_rain`, `head_ubc_m`, `head_ubc_f`.
- End-user generation/checkout requires actual prior provider permission, recorded by `TRIPO_END_USER_AGREEMENT_CONFIRMED`; do not flip this merely because API credits exist. Operator-only evaluation does not enable retail checkout.
- Server price remains300 USDC cents (3USDC). Configure the correct cluster, mint, treasury and receipt RPC. No wallet was charged and no provider task was started in this pass.
- Acceptance: owner loads readiness, reviews a 3USDC quote, a devnet receipt is reconciled without re-signing after reconnect, photo→reference approval→rig/validation→preview→accept completes, generated asset appears after fresh sign-in and in actual venue actor, a second account cannot read/claim it, and failed delivery retains the owed creation without charging again.

## Validation state

New regressions cover project treasury/hires and richer-response precedence; upload limits, implemented metadata and nested booking response; booking metadata forwarding; signature submission forwarding; corrected tribe/filter paths; used invite status; server refusal metadata; missing/malformed Competition capabilities blocking before mutation; and invalid targeted invite/Beef receipts retaining the request key with no success result.

The standalone `run_ventures_tests.sh` and `run_competition_tests.sh` were attempted. Both stopped before the owned assemblies because their stale compiler harness omits glTFast dependencies now required by `CTLife.World`:

```text
CTLife.World 67 files FAILED (8 errors)
LikenessAvatarLoader.cs: error CS0246: The type or namespace name 'GLTFast' could not be found
LikenessAvatarLoader.cs: error CS0246: The type or namespace name 'GltfImport' could not be found
LikenessAvatarLoader.cs: error CS0246: The type or namespace name 'IMaterialGenerator' could not be found
```

No standalone test pass is claimed. Coordinator: run `CTLife.Ventures.Tests` and `CTLife.Competition.Tests` in the real Unity project, then test the hosted service and Seeker separately. The successful local HTTP checks do not replace those checks.

Primary API documentation consulted for the upload transport: [Unity6.3 UploadHandlerRaw](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Networking.UploadHandlerRaw.html) and [SetRequestHeader](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Networking.UnityWebRequest.SetRequestHeader.html).
