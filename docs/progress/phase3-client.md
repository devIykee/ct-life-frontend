READY TO COMMIT: client/Assets/Scripts/Features/Social/ docs/progress/phase3-client.md docs/progress/requests.md — Phase 3 social client: Spaces Studio (list/create/end, listen, request to speak, host add/move speakers, space_update + polling, speaking, leave/rejoin, reconnect), free video calls (1:1/group ≤ 8, call_invite ring/accept/decline/timeout, LiveKit video, mic/camera/silence, Android permissions), paid calls (creator settings, fan create → MWA sign on devnet → submitted → ringing → live → end → settled/refunded, creator accept/decline in the 120 s window), Messages (threads, requests, paging, send, realtime dm, block/report, scam hints), Timeline (global/following, ≤ 280, CT post formats, follow), tips (Cash + on-chain record), 151 EditMode test cases

(Unity generates the `.meta` files on first import; commit them with this. The apps are not reachable until the
integration lead applies `docs/progress/requests.md` → "2026-10-08 phase3-client -> integration lead".)

# Phase 3 client (Voice and calls) — progress log

Owner: phase3-client agent. Scope: `client/Assets/Scripts/Features/Social/` (assembly `CTLife.Features.Social`, C# namespace
`CTLife.SocialFeatures`, including `Resources/UI/Social/social.uss`), this log, and one entry in `docs/progress/requests.md`.
Nothing else was edited. The Unity editor and adb were not used. No git commands that change state were run.

## Status (2026-10-08)

Built and compiling in all three `client/Tools~/compile_check.py` passes (player, `--editor` incl. the test assembly,
`--release`): all CT Life assemblies compile, 0 warnings in `CTLife.Features.Social`. Logic covered by **151 passing NUnit
cases** run out of the editor (see "Verify"). **Not yet run in the Unity editor, on the Seeker, or against a live backend**:
the hooks are pending, and the backend checkout is missing the endpoints that carry media (`POST /rtc/token`) and the
realtime pushes (`space_update`, `dm`, `call_invite`); details below. UI is functional and unstyled beyond the theme.

## What is built

| Feature (spec #) | Phone app (id) | Endpoints / realtime |
| --- | --- | --- |
| #23 Spaces Studio | `SpacesStudioApp` (`spaces`, replaces `CTLife.UI.SpacesApp`) | `GET /spaces?live=true`, `POST /spaces`, `GET /spaces/{id}`, `POST`/`DELETE /spaces/{id}/hand`, `POST /spaces/{id}/speakers` (falls back to the backend's `POST /spaces/{id}/speakers/{userId}` only when the contract route is missing), `DELETE /spaces/{id}/speakers/{userId}`, `POST /spaces/{id}/end`, `POST /rtc/token {kind: space}`, `POST /reports {targetType: space}`, city `space_update` |
| #24 Free video calls | `CallsApp` (`calls`) + `SocialOverlay` (ring sheet over the world) | `POST /video-calls {userIds ≤ 7}`, `POST /rtc/token {kind: call}`, city `call_invite {paid: false}` |
| #25 Paid video calls | `CallsApp` (fan flow, creator settings) + `SocialOverlay` (creator ring) | `PUT /creator`, `POST /paid-calls`, MWA `sign_and_send` via `ITransactionSigner`, `POST /paid-calls/{id}/submitted`, `/accept`, `/decline`, `/end`, `GET /paid-calls/{id}`, city `call_invite {paid: true}` |
| #26 DMs | `MessagesApp` (`messages`) | `GET /dms`, `GET /dms/{userId}?before=`, `POST /dms/{userId}`, `POST /blocks/{userId}`, `POST /reports`, city `dm` |
| #27 Timeline + follows | `TimelineApp` (`timeline`) | `GET /feed?scope=global|following`, `POST /feed/posts`, `POST`/`DELETE /follows/{userId}`, `GET /users/{id}` (names) |
| #28 Tips | Tip sheet in `TimelineApp` (from a post or the profile card) | `POST /tips {toUserId, cashCents}`, `POST /tips/onchain {toUserId, signature}` |
| (#22 proximity voice) | unchanged (`VoiceService`); the social controllers disconnect it while a call or Space is joined | — |

29 REST operations and 3 city messages (`SocialEndpoints.Covered` / `.Realtime`). Profile-card actions (Message, Follow,
Video call, Paid call, Tip) come from `SocialProfileActions.Build` once hook 4 lands.

### How it behaves
- **Sessions outlive the phone.** One `SocialHub` per signed-in user (held by the overlay and the apps) owns the Space,
  the call and its media, the inbox and the timeline. Closing the phone keeps a Space or call running; a chip ("Ongoing
  call" / "In a Space") reopens it. Sign-out or a user change disposes everything.
- **Server-authoritative.** Space roles come only from `GET /spaces/{id}` (role actions disabled on a stale roster);
  paid-call stages follow `PaidCall.status` (terminal never moves back); every amount shown is a server value
  (`creatorAmount`, `refundAmount`, `minutesUsed`, `cashCents`); the wallet shows what the escrow moves before signing.
- **Idempotency** (`RetainedKey`): one UUIDv4 per exact request (method + path + body). Unknown outcome (network, timeout,
  5xx, unreadable reply) → the retry reuses the key ("Try again: it won't post/create twice"); a definitive answer retires
  it. Space create, Timeline post, DM send, tips, paid create/submit/accept/decline/end, follows, blocks, reports all use it.
  `POST /rtc/token` always gets a fresh key (a replayed grant would be expired).
- **Retries / offline / tokens.** Transport retries and 401 refresh are RestClient's; every call is bounded (30 s) by
  `SpacesRequest.Run`. Connectivity is watched (`Application.internetReachability`): offline banner, polls paused, and on
  any change Space/call media retry immediately and lists refetch. LiveKit media is rejoined with a fresh grant and backoff
  (1, 2, 4, 8, 15 s…; Spaces give up after 6 tries → "Rejoin", calls after 5 → "The call dropped"). A grant whose
  `expiresAt` is within 5 s is not used.
- **Without realtime** (the backend's city room is a stub): a Space is polled every 5 s, the live list every 15 s while
  visible, an open DM thread every 8 s, the inbox every 30 s, the Timeline every 30 s, a paid call every 2 s (5 s live).
- **Media.** Spaces reuse `SpacesAudio` (stage-only subscriptions, explicit unmute, never resumed after reconnect/pause);
  blocked speakers are left out of its roster so their audio is never delivered. Calls use `VideoRoom` (new, video): mic via
  the existing `SpacesCaptureSource`, camera via the SDK's `WebCameraSource` (front camera, 640×480@24), remote `VideoStream`
  textures in the phone panel, speaking highlight, silence-room, capture stopped on pause/reconnect and never resumed.
  Permissions are requested at the moment of use; "denied" asks again next tap, "don't ask again" shows "Open Settings".
- **Paid calls are devnet-only** (`EnvConfig.cluster == Devnet`; otherwise disabled with a message). A transaction is
  signed at most once; a wallet error after a possible broadcast goes to "Checking the escrow…" (polling), never to a
  second signature; the call id is persisted so a kill inside the wallet resumes polling. Built transactions older than
  60 s are discarded `[Inference: blockhash lifetime]`. Creators see a 120 s countdown and Accept disappears at 110 s.
- **Safety.** DM requests (people you don't follow and haven't answered) are kept apart with a warning; scam hints on
  messages, posts and unknown callers; Block and Report on every conversation, post, Space and caller; text is never
  rich-rendered and links are never clickable; "Not recorded" on Spaces and calls.

## Research (CT socialising now) and what it changed

WebSearch was unavailable (session search budget exhausted); sources were fetched directly (WebFetch, curl to the
Internet Archive). Chrome tools were not used. Real names appear only here; in-game copy uses parody names.

| Source (date) | What it says | Reflected in |
| --- | --- | --- |
| X Help "How to use Spaces", help.x.com/en/using-x/spaces (live site answers 403 to fetchers; Wayback snapshot 2026-07-27) | "Up to 13 people (including the host and 2 co-hosts) can speak"; "Listeners can request permission to speak from the host by selecting the Request"; "the host will join with their mic off"; hosts "can mute any Speaker, but it is up to the individual to unmute themselves"; blocked speakers get a "Blocked" label and "You have blocked 1 person who is speaking." | Host/Speaker/Listener roles, "Request to speak"/"Withdraw request", "Speaker requests (n)", "Add as speaker"/"Move to listeners", everyone joins muted and unmutes themselves, blocked label + warning (and not subscribed), co-hosts noted as unsupported (contract proposal) |
| X Help "About Direct Messages", help.x.com/en/using-x/direct-messages (Wayback 2026-09-06) | Requests from people you don't follow are "kept separate from your other DMs until you accept them"; "they will not know you have seen the message until you have accepted"; blocked accounts cannot message you; report a message or a conversation | Messages "Requests (n)" section, Accept, Block/Report per conversation |
| X Help "How to create a thread" (Wayback 2026-09-04) | "A thread on X is a series of connected posts from one person." | Thread post format/starter; `replyToPostId` proposal |
| MetaMask Support, "MetaMask's official support channels" (support.metamask.io, retrieved 2026-10-08) | "we will never DM you offering support"; "if anyone asks for your Secret Recovery Phrase, they are trying to scam you"; no support on X/Telegram/Discord | DM safety banner ("CT Life staff never DM you first…"), impersonation/secret flags |
| FTC, "What To Know About Cryptocurrency and Scams" (consumer.ftc.gov; page dated May 2022, retrieved 2026-10-08) | "Only scammers demand payment in cryptocurrency"; "Only scammers will guarantee profits"; "free money promises are always fake" | Guarantee / send-funds / giveaway flags |
| Time.fun explore page (Wayback 2025-12-21; `time.fun` has no A record on 2026-10-08) | "Time.fun is shutting down and most in-app actions are now disabled"; creators priced per minute ("$0.09 / min"); "direct messages, group chats, and voice or video calls"; bios like "I respond to every DM, and voice call" | Per-minute USDC rate wording; `[Inference]` the tokenised creator-time market closed, so paid calls stay plain escrowed minutes (as the spec says) and creators are told "selling time, not promos" (FTC disclosure note from the social-direction research) |
| `docs/research/ct-x-observations-2026-10-04.md` and `docs/research/ct-now-2026-10.md` v0.1 (2026-10-07/08) | "JUST IN"/"BREAKING" news bots and prediction-market odds; liquidation clips "+$40,000 → liquidated"; tracker legends "turned $1K into $15M", "retired off one trade"; trader journals "Day 605"; giveaway template "Drop your wallets… Join my TG"; "like and reply hi, surprise in DMs"; "quote this post with your username" airdrop tests; Sep 2026 "build an AI crypto bot" self-drain tutorials; slang (trenches, sending, cooked, so over/so back; never "jeet"); parody names | Post kinds + badges (News, Thread, Call, PnL, Rekt, Tracker, Journal, gm, Giveaway?), composer starters with fictional `$FROGE`, scam flags (Drainer, Giveaway…), Space topic hints ("it's so over / we're so back", "COIN2050 recap") |
| Unity 6.3 manual "Android permissions in Unity" + ScriptReference `Android.PermissionCallbacks` (2026-10-08); compiler | WebCamTexture → CAMERA, Microphone → RECORD_AUDIO added by Unity; `PermissionDeniedAndDontAskAgain` is **obsolete** in 6000.3.25 ("use ShouldShowRequestPermissionRationale") | `MediaPermission` (denied vs blocked via rationale, dismissed handled) |
| LiveKit Unity SDK 2.1.0 (pinned package, read locally): README "Video", Samples~/Meet | `WebCameraSource` + `LocalVideoTrack.CreateVideoTrack` + publish with `TrackSource.SourceCamera`; `VideoStream` + `TextureReceived` + `Update()` coroutine | `VideoRoom` |

## Files

- `Logic/` (pure, no UnityEngine; what the tests cover): `SocialWire` (tolerant readers, ids, times), `SocialModels`
  (+ `SocialParse`), `SocialErrors` (`RetainedKey`, `SocialError`, `Backoff`), `UsdcUnits` (+ `CashInput`),
  `SpaceSession`, `CallSession`, `PaidCallFlow`, `DmLogic` (`DmThread`, `DmInbox`, `ScamSignals`), `FeedLogic`
  (`PostKinds`, `FeedTimeline`, `IdSet`, `TipRules`, `Base58Lite`, `Ago`).
- `Net/`: `SocialApi`, `SocialHub` (+ `SocialStore`, `NameCache`), `SpacesController`, `CallController`,
  `MessagesController` (+ `TimelineController`), `VideoRoom`, `MediaPermission`.
- `UI/`: `SocialUi` (builders, `SocialView`, `SocialApp`), `SpacesStudioApp`, `CallsApp`, `MessagesApp`, `TimelineApp`,
  `SocialOverlay` (+ `SocialProfileActions`, `SocialApps`), `SocialStrings` (all copy).
- `Resources/UI/Social/social.uss` (layout only).
- `Tests/` (EditMode, asmdef `CTLife.Features.Social.Tests`): `SpaceSessionTests` (17), `CallFlowTests` (CallSession 9,
  PaidCallFlow 21 incl. TestCases), `DmFeedTests` (DmThread 14, DmInbox 5, Feed 28), `ParseAndRulesTests` (Parse 19,
  Rules 38) → 151 cases.
- `Tests~/` (ignored by Unity and compile_check): `social_check.py`, `run_social_tests.sh`, `Runner.cs` (copied from Money).

## Verify

```sh
python3 client/Tools~/compile_check.py --keep-going            # player: all CT Life assemblies compile, 0 warnings
python3 client/Tools~/compile_check.py --editor --keep-going   # editor pass incl. CTLife.Features.Social.Tests: ok
python3 client/Tools~/compile_check.py --release --keep-going  # CTLIFE_PROD: ok
client/Assets/Scripts/Features/Social/Tests~/run_social_tests.sh   # → PASSED 151  FAILED 0  (only this lane's deps)
```
In the editor (reference runner): Test Runner → EditMode → `CTLife.Features.Social.Tests`.
Last run 2026-10-08 against the working tree: the three passes above clean; harness `PASSED 151 FAILED 0`.

## Requests made (docs/progress/requests.md, "2026-10-08 phase3-client -> integration lead")

1. `CTLife.Features.Social` in `CTLife.App.asmdef`.
2. `PhoneCatalog`: `TimelineApp`, `MessagesApp`, `SpacesStudioApp`, `CallsApp` in place of the three `ComingApp`s and `SpacesApp`.
3. `SocialOverlay` in `FlowController` overlays (ring sheet + ongoing chip; handles invites/DMs with the phone closed).
4. `ProfileCard.PlayerActions` static hook (Message, Follow, Video call, Paid call, Tip), set in `BuildHudAndPhone`.
5. `UserBlockedSignal` in Core + publish from `ProfileCard.Block` (+ one subscription line in `SocialHub`).
Plus: Phase gate ≥ 3, core `MicPermission` dismissed-dialog fix, manifest CAMERA check, HUD proximity voice during
calls, forwarding the backend list below. No compile_check change needed (it already discovers feature lanes and tests).

## Backend vs contract (ct-life-backend 9f3e5ef, read-only; please confirm and forward)

Blocking for Phase 3 end to end:
1. **No `POST /rtc/token` route** (no match in `server/api/routes/`; `server/application/livekit.ts` only mints inside
   `POST /video-calls` and `POST /paid-calls`). Nobody can get Space audio, invitees cannot join calls, and venue
   proximity voice has no token either. The client shows "This server can't issue voice tokens yet".
2. **The city room sends no `space_update`, `dm` or `call_invite`** (`server/realtime/rooms/city.ts` is a stub; `space.ts`
   only mentions it in a comment). Incoming calls never ring; DMs and Spaces fall back to polling.
3. **Paid calls are a different API.** `POST /paid-calls` takes `{payeeUserId, pricePerMinuteCents 25..5000, maxMinutes
   1..240}` (the fan sets the price, in Cash cents) instead of `{creatorId, minutes}` and returns `{call, livekit}` with no
   `tx`/`sessionPda`, so there is nothing to sign (client: "The server didn't return a transaction"). Status never reports
   `ringing` (DB `active` = funded-but-unaccepted maps to `live`; `pending_onchain` too). `creatorAmount`/`refundAmount`
   are numbers in cents (contract: base-unit strings). Fee is 5 % (`PLATFORM_FEE_BPS = 500`; spec/IDL 10 %).
   `POST /paid-calls/{id}/end` requires `{secondsUsed}` from the client (contract: no body; spec: LiveKit webhook measures
   it). No 120 s accept-window refund job; `onchain_session_id` is never written, so the on-chain settle path never runs;
   `submitOnChainSettle` does `new PublicKey(args.payerUserId)` on a user UUID. (`server/application/paid-calls.ts`,
   `server/api/routes/paid-calls.ts`)
4. **No `PUT /creator`.** The backend has `POST`/`PATCH /creator {handle, bio, categories}` and `GET /creator/:handle`;
   there is no rate or max-minutes anywhere. (`server/api/routes/creator.ts`)
5. **`GET /dms` requires `peerUserId`** (`VALIDATION_FAILED` without it), so there is no thread list; the client falls
   back to conversations known on the device. (`server/api/routes/social-graph.ts`)
6. **`POST /tips`** expects `amountCents` (contract `cashCents`), returns a `TipRow` without the sender's new balance,
   answers insufficient Cash with `VALIDATION_FAILED` (contract 402 `INSUFFICIENT_CASH`) and enforces no daily cap.
   **`POST /tips/onchain`** requires `amountLamports` (contract `{toUserId, signature}` only), so the contract body is
   rejected; it then credits the recipient's in-game Cash via Helius (spec: wallet-to-wallet, server not involved).
7. **`POST /video-calls`** returns `{call, tokens: [...]}` with **every participant's LiveKit token** to the caller (the
   caller could join as anyone) instead of `{callId}`; room `video:{callId}` (contract `call:{callId}`); no invite is pushed.
   The client uses only its own token from that list, and only when `/rtc/token` is missing.

Shape differences the client tolerates: `GET /spaces` → `{spaces: []}`; Space ids `space:{uuidv7}` (contract `format:
uuid`; the existing `SpacesApi`/`SpacesApp` parse ids as `Guid` and would fail on every Space); `hostUserId`,
`speakerUserIds` (includes the host), `handUserIds`, extra `status/startedAt/endedAt/maxSpeakers`; promotion route
`POST /spaces/{id}/speakers/{userId}` (client falls back only on the backend's route-missing envelope `NOT_FOUND {path,
method}`); `NOT_SPACE_HOST` sent as `FORBIDDEN {reason: NOT_SPACE_HOST}`; `POST /spaces`, `/dms/{id}`, `/feed/posts`,
`/video-calls`, `/paid-calls`, `/tips` answer 201; `GET /dms/{id}` → `{messages}` newest-first with `createdAt`; `GET /feed`
→ `{posts}` with `createdAt`, `alias: ""` on a new post; LiveKit `expiresAt` in epoch seconds.
Other behaviour to note: stage cap is 8 including the host (`voice_rooms.max_speakers`; spec: host + 10); `listenerCount`
only counts rows created by raising a hand `[Inference]`; `POST /dms` ignores the receiver's block (contract 403 `BLOCKED`);
`GET /feed` does not hide blocked authors; DM `before` compares `created_at < ISO` with millisecond truncation, so messages
in the same millisecond at a page edge can be skipped.

## Contract gaps / proposals (for docs/fe-contract-proposals, not drafted as files)

- A fan cannot see a creator's rate before `POST /paid-calls` (no creator fields on `PublicProfile`, no
  `GET /creator/{userId}`), so spec step 1 ("shows rate × minutes plus a 10 % fee") is impossible; the client says the
  wallet shows the amount. Proposal: `creator {enabled, ratePerMinUsdc, maxMinutes}` on `PublicProfile` and a quote
  (`depositAmount`, `feeAmount`) in the `POST /paid-calls` response. Also: `GET /creator` (own settings), a cancel for an
  unpaid call, and a fan `refund` transaction builder after expiry (spec step 7).
- No `GET` for follows or blocks (client remembers its own actions per device).
- Free calls have no decline/end/state: the caller only learns "No answer" after 45 s `[Inference: client timeout]`.
  Proposal: `POST /video-calls/{id}/decline` and a city `call_update {callId, state}`.
- `GET /dms/{userId}?before=` names a "cursor returned by a previous call" but returns a bare array; define it as the
  oldest `at` (what the client sends) or return `{messages, nextBefore}`. `GET /feed` has `cursor` but returns no cursor.
- Feed posts are flat text: proposal `replyToPostId` (threads), optional `kind` and `coinId` (calls/PnL).
- Spaces co-hosts (X allows 2) and `space_update` for listener joins.
- `economy.tipCapCentsPerDay` (the contract says the tip cap "comes from config" but rc1 has no key; the client reads that
  key when present).
- On-chain tips: `PublicProfile` has no wallet and the server builds no transfer, so the app cannot start a wallet tip; it
  records one the player already sent. Proposal: `POST /tips/onchain/tx {toUserId, currency, amount}` → unsigned `tx`.

## Not done / unverified

- `[Unverified]` everything on device: LiveKit video publish/subscribe on the Seeker (Mali), front-camera orientation in
  the UI Toolkit tile (rotation by `videoRotationAngle`, mirrored), remote frame orientation, echo with speaker + mic,
  `ShouldShowRequestPermissionRationale` behaviour, `MwaWallet.SignAndSendBase64` with a real paid-call transaction.
- Not run in the editor against the local docker backend; blocked mostly by backend items 1–3 for media and paid calls.
  DMs (except the inbox list), Timeline, follows and Space list/create/hand/promote/end should work against it today.
- No Space or call presence in the 3D world (stage seats, `act_host_mic` on the host avatar, `talking` from Spaces/call
  speaking): World scope, not started.
- No PlayMode/UI tests; views are verified by compile only.
- Group call invitee list cannot be edited mid-call (no contract endpoint).

## Next

- After the hooks: run in the editor with dev auth against the local backend (Timeline, DMs, Space list/create/roles),
  then two Seekers on staging once `/rtc/token`, `call_invite` and the paid-call API exist.
- Add the `UserBlockedSignal` subscription when hook 5 lands.
