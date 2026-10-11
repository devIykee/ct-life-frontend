# CT Life Unity client — independent code and architecture QA

**Date:** 2026-10-07 · **Scope:** `client/Assets/Scripts` (8 hand-written assemblies), `client/Assets/Generated`, `client/Tools~`, against `contract/` (rc1) and `docs/spec/contract.md` · **Mode:** read-only. Nothing was modified except this file. The Unity editor and adb were not used.

**How to read this.** Statements without a label come from reading the code or from commands run for this review (listed in the appendix). `[Inference]` marks a conclusion drawn from code reading that was not executed. `[Unverified]` marks something I could not check. Earlier agent reports were used only to locate code; none of their claims are repeated here as fact.

---

## 0. Verdict in one paragraph

The networking skeleton is sound and is contract-driven: the REST models and Colyseus state classes are generated from `contract/` and **regenerate byte-for-byte identical** today. Auth (SIWS via Solana.Unity-SDK MWA), the venue room (move, sit, stand, emote, say, talking, npc_tap, welcome, rejected, said) and LiveKit voice are real, not stubbed. **However, none of it has ever run against a server.** I found no log or evidence of a successful REST call or room join. All device evidence comes from `review=1` builds, which replace the HTTP transport with a fixture that only answers `GET /me`. Several first-contact bugs will surface during integration, and the most serious ones block it outright: a new user cannot finish character creation against rc1, cold start has no retry path, realtime joins send an expired JWT after about 14 minutes, there is no way to choose the staging environment at build time, and the Perps and Spaces apps cannot be opened in the real flow. Payments, ORE and SKR are missing. The codebase is also badly unbalanced: **about 66% of the hand-written C# (≈54.5k of 82.4k lines) is one-off candidate, review or probe tooling**, and the out-of-editor compile check is currently red for the editor assembly.

| Area | Status |
| --- | --- |
| REST models generated from `contract/openapi.yaml` | **Real** (regenerated, identical after the patch step) |
| REST transport (headers, Idempotency-Key, refresh, 429/503, errors) | **Real** (bugs below) |
| REST endpoint coverage | **Partial**: about 36 of 83 client-facing operations are wrapped, and several wrapped ones have no caller |
| Auth: SIWS via MWA (challenge → `LoginWalletAdapter(SignInPayload)` → verify) | **Real, never run end to end**. The identity URI is a placeholder (`ctlife.invalid`) |
| Auth: refresh, resume, logout, MWA deauthorize | **Real**. Stale-token handling has gaps |
| Colyseus state schemas from `contract/realtime/states.ts` | **Real** (regenerated, identical) |
| Realtime message payloads (`messages.ts` is Zod, not codegen) | **Real, hand-mirrored**. All 18 message types are present |
| Venue room: join, state sync, client→server messages | **Real, never joined a server** |
| City room: event, liquidated, kicked, toast | **Real** consumers |
| City room: payment_confirmed, dm, call_invite | **Missing** consumers (the events fire into nothing) |
| Market room: coins and tape | **Stub**: joined but no consumer |
| Server clock from `welcome.serverNow` | **Real** |
| LiveKit proximity voice (`/rtc/token` → connect → mic permission → spatial) | **Real**, opt-in from the HUD mic button |
| LiveKit Spaces (stage, hand, promote, audio) | **Real code, unreachable** in the real flow (Phase-3 gate) |
| Careers and shifts (apply → `/shifts` → mini-games → `/complete`) | **Real** for 3 contract mini-games. The other 6 games run only in a dev fixture |
| Perps (open, close, positions, receipts) | **Real code, unreachable** in the real flow (Phase-2 gate) |
| Spot trading, positions, predictions, alpha calls, tombstones UI | **Missing** (`Api.Trade` and `Api.Tombstones` exist with no caller) |
| Payment intents (USDC/SOL/SKR top-up), MWA `sign_and_send` | **Missing**. `ITransactionSigner` is registered, never called |
| ORE board, stake and miner tier | **Missing** (no endpoint wrappers, the phone tile is "Coming") |
| Homes, shop, inventory, feed, DMs, calls, ads, leagues | **Missing** (the phone tiles are "Coming") |
| Private Home (parents_basement) | **Partial**: a local scene chosen by `Me.homeId`, which the contract allows until Phase 4 |
| NPCs | **Real** presentation follower of server `NpcState`; no client brain (correct split) |
| Game state in the tested builds | **Fixture**: `ReviewTransport` (hard-coded `Me`) and `PracticeJobsApp` (no API) |
| Release/prod build configuration | **Missing**: `CTLIFE_PROD` is never defined, there is no env selection in the build, and the staging realtime URL is `.invalid` |
| Automated tests | **Missing** as NUnit. One out-of-editor check (thermal) exists and passes |

---

## 1. Architecture

### 1.1 Assembly layout and dependency direction

```
CTLife.Core      (no refs)                         ← services locator, Bus, Session, Env, ConfigStore, ServerClock
CTLife.Generated (ColyseusSDK)                     ← REST models (openapi-generator 7.25.0) + schema states + hand-mirrored messages
CTLife.Net       (Core, Generated, Colyseus, LiveKit)        ← RestClient, Api, AuthService, RealtimeService, VoiceService
CTLife.Audio     (Core)
CTLife.Solana    (Core, Solana.Unity-SDK, UniTask)           ← MwaWallet only (137 LOC)
CTLife.World     (Core, Net, Generated, Audio, Colyseus, Addressables, URP)
CTLife.UI        (Core, Net, Generated, World, Audio, Colyseus, URP)
CTLife.App       (everything)                                ← GameBootstrap, FlowController, Review fixtures
CTLife.Editor    (Core, World, UI, App, Addressables(.Editor), URP)
```

The direction is acyclic and broadly correct: Core sits at the bottom and App is the composition root. The Solana SDK is isolated behind `IWalletSignIn` and `ITransactionSigner` in Core (`Core/Wallet/WalletContracts.cs`), which is good.

Problems:
- **World depends directly on Net.** `VenueSession` (`World/Venue/VenueSession.cs`, 489 LOC) does scene loading, room join, avatar spawning, chat, rejection handling, and voice token fetch and connect. It is a god object, and it is also a static singleton (`VenueSession.Current`) that UI reaches into 14 times.
- **The UI asmdef references `ColyseusSDK` but no UI file uses it.** Dead reference.
- **Service locator everywhere.** There are 72 `Services.Get<>()` calls and 26 `UiRoot.Instance` calls in runtime code. Singletons include `UiRoot`, `CameraRig`, `AudioManager`, `PerfManager`, `Runner`, `CharacterRegistry`, `NpcCastCatalog`, `VenueSession.Current` and `GameBootstrap.Booted`, plus static `Bus`, `Services`, `Env`, `LaunchArgs`, `ServerClock` and `ControlSettings`. The minigame classes (`LiveCues`, `ClipCut`, `RoleLock`, `ReceiptMatch`, `RunwayPlan`, `RaidRouter`, `ThreadWorkshop`) are static classes holding a static `_active` element.
- **Testability is poor.** Because of the static locator, every "review" swaps globals by mutating `Services` (e.g. `App/Review/VenueLifetimeReview.cs:74`, `App/Review/FlowOwnershipReview.cs:52`, `SavedVenueFlowReview.cs:37`). There is no NUnit or Unity Test Framework assembly, even though `com.unity.test-framework` is in the manifest.
- **The lifecycle plumbing is heavy.** About 600+ LOC of cancellation and ownership machinery (`App/FlowLifetime.cs`, `Net/Realtime/RoomJoinSlot.cs`, `VenueSession.EnterCore/CleanupOwned`, `World/Venue/VenueWait.cs`) guards against races in flows that have never talked to a server. It is careful work, but it is disproportionate to the feature surface and makes the code hard to change.

### 1.2 Bootstrapping flow

`Scenes/Bootstrap.unity` → `GameBootstrap.Awake` (`App/GameBootstrap.cs:23-59`) registers the following, then creates `PerfManager`, `AudioManager`, `UiRoot` and `AvatarLod`:

- Session, ConfigStore (loads the cache or the bundled `Resources/Contract/config_snapshot.json`)
- RestClient (or `ReviewTransport` when `review=1` in a dev build)
- Api, AuthService, PlayerProfile, RealtimeService, VoiceService, MwaWallet

`Start` then adds one of three controllers: `FlowController` (the real flow), `FrontendReviewController` (dev fixture), or `SpikeController` (dev).

The real flow (`App/FlowController.cs:103-279`):

1. Splash, then `GET /config`.
2. `TryResume` with the stored refresh token, otherwise the wallet sign-in screen.
3. `GET /me`, then the character creator if there is no character, then the lottery if not rolled.
4. Join the city and market rooms.
5. Build the HUD and phone, show the lobby, travel to `timeline_square` (venue join, offline fallback).

`Scenes/Spike.unity` is also enabled in Build Settings, so it ships in release builds (`ProjectSettings/EditorBuildSettings.asset`).

### 1.3 Size

| Assembly | Files | Total LOC | Dev-only (Review/Spike/probe files) | Production LOC |
| --- | ---: | ---: | ---: | ---: |
| CTLife.App | 42 | 9,913 | 9,169 | **744** |
| CTLife.UI | 65 | 9,492 | 55 | 9,437 |
| CTLife.World | 42 | 6,185 | 140 | 6,045 |
| CTLife.Net | 17 | 2,567 | 73 | 2,494 |
| CTLife.Core | 21 | 1,682 | 0 | 1,682 |
| CTLife.Audio | 6 | 1,205 | 0 | 1,205 |
| CTLife.Solana | 1 | 137 | 0 | 137 |
| **Runtime total** | 194 | **31,181** | **9,437** | **21,744** (of which a further ~616 lines are inline `#if DEVELOPMENT_BUILD` review hooks) |
| CTLife.Editor | 141 | **51,213** | 45,093 in 119 `*Candidate*/*Review*/*Probe*/*Diagnostic*/*Comparison*/*Replay*/*Fit*` files | 6,120 (build, setup, importers) |
| CTLife.Generated | 116 | 12,696 | — | generated |

- **Hand-written total: 82,394 LOC.** About **54.5k (66%) is one-off or dev-only tooling**: 45.1k editor candidates and reviews plus 9.4k runtime review fixtures. Production game code is about 21k LOC.
- **Editor scripts outnumber runtime about 1.6 : 1, and 88% of editor code is single-purpose experiments.** Many are versioned copies: `SpacesStudioCandidateC/D/E/E2`, `TerminalCafeCandidate*` ×12, `KayKitRawRunCandidate` ×5, `SharedSocialExitCandidate` ×4, `CafeArtRoomReview` ×6. There are 2,056 editor lines over 200 characters, with a maximum of 1,725 characters on one line (`Editor/SocialSeatExitRoomReview.cs`, `Editor/CafeContinuousExitRoomReview.cs`).
- Largest runtime files:

  | File | LOC | Kind |
  | --- | ---: | --- |
  | `App/Review/FrontendReviewController.cs` | 898 | fixture |
  | `App/Review/SavedVenueFlowReview.cs` | 635 | fixture |
  | `World/Players/LocalPlayerController.cs` | 603 | |
  | `App/FlowController.cs` | 567 | |
  | `Audio/AudioManager.cs` | 549 | |
  | `UI/Phone/Apps/SpacesApp.cs` | 502 | |
  | `World/Venue/VenueSession.cs` | 489 | |
  | `UI/Phone/Apps/PerpsApp.cs` | 460 | |
  | `App/Review/VoiceNativeReview.cs` | 454 | fixture |
  | `UI/Hud/MovementControls.cs` | 437 | |

### 1.4 Duplicated or parallel systems

- **Characters: three build paths plus per-character runtime patches.** `World/Avatars/AvatarFactory.cs:46-88`:
  - (1) registry prefab;
  - (2) modular `ItemCatalog` + `CharacterAssembler` + `GeneratedAvatarResources` (~330 LOC). This path is **dead**: `Resources/Characters/Base.prefab` does not exist and `Characters/Parts/` is empty;
  - (3) `PlaceholderHumanoid` + `ProceduralAnimator` fallback (~617 LOC).
  
  On top of these sit character- and venue-specific runtime corrections: `SnowRunCorrection`, `SnowTransitionContact` (per-frame CPU skinning of foot vertices), `CafeSeatExitPresentation`, `CafeExitSettleContact`, `CafeWorkExitGoals` and `CafeSeatMotionProfile`. A doc comment calls `SnowRunCorrection` a "Candidate", yet `AvatarFactory.cs:102-106` binds it to every registry avatar it matches.
- **Mini-games: two hosts.** The real `MiniGameHost.Play` (`UI/Phone/Apps/JobsApp.cs:303-308`) maps 3 IDs and falls back to a generic tap game. The dev `PracticeJobsApp` (`App/Review/PracticeJobsApp.cs`) is the only route to the other 6 games (RaidRouter, RoleLock, ReceiptMatch, LiveCues, RunwayPlan, ClipCut), whose IDs are proposals, not in `contract/ids/mini_games.json`.
- **Config and profile are fetched twice.** `Net/Rest/PerpsApi.cs:27-28` re-GETs `/config` and `/me` as raw JSON, bypassing `ConfigStore` (and its ETag) and `PlayerProfile`.
- **Phase gate in two places.** `UI/Screens/CityMapScreen.cs:16` (`const CurrentPhase = 1`) and `UI/Phone/PhoneShell.cs:45` (`CurrentPhase = 1`, never set elsewhere).
- **Movement.** A single local controller (`LocalPlayerController`) owns both direct analog and NavMesh tap-to-move. That part is fine. `App/Spike/CrowdWalker.cs` is a separate dev-only wanderer.

---

## 2. Contract integration

### 2.1 REST: generated from the contract, and used?

- **Generated: yes, verified.** I re-ran openapi-generator 7.25.0 with the exact `codegen_rest.sh` arguments into a scratch directory and applied `patch_rest_codegen.py`. The 107 model files are **identical** to `client/Assets/Generated/Rest`. The patch works around real generator issues: the `const: true` enum, and int32 `*Cents` widened to long. Those issues are already reported in `docs/progress/backend-dev-feedback.md`.
- **Used: partly.** `Api.cs` and `PerpsApi.cs`/`SpacesApi.cs` are hand-written, as the contract requires ("models only; FE writes the thin HTTP wrapper"). Twenty runtime files use generated models. Perps parses raw `JToken` through its own `PerpsWire` rather than the generated `Perp`/`Coin` models.
- **Coverage.** The spec has 90 operations; 83 are client-facing once the 5 admin and 2 webhook operations are removed. About 36 are wrapped:
  - auth (5)
  - config, me, characters, lottery, needs (5)
  - careers, apply, shifts, complete (4)
  - users, blocks ×2, reports, leaderboards (5)
  - events/active, rtc/token (2)
  - market coins list and detail, trades, tombstones (4)
  - perps open, list, close (3)
  - spaces ×8

  **No caller:** `Api.Trade`, `Api.Coins`, `Api.Tombstones`, `Api.ActiveEvents`, `Api.Unblock`.

  **Not wrapped:** payments/intents, ore/*, homes, shop, inventory, feed, follows, dms, tips, video-calls, paid-calls, creator, alpha-calls, smart-money, predictions, market/positions, gigs, daily-clue, meta/current, push/register, uploads, ads/*, characters/me/look, characters/me/tribe.
- **Transport** (`Net/Rest/RestClient.cs`):
  - Per contract: `X-Client-Version` and `X-Contract-Version` on every request, and a UUIDv4 `Idempotency-Key` on every mutation, reused across network retries.
  - Proactive and 401-triggered refresh, single-flighted in `AuthService.Refresh`.
  - 429 and `SERVER_BUSY`/503 backoff, 426/`CLIENT_OUTDATED` → `UpdateRequiredSignal`, and the `{error:{code,message,details}}` shape is mapped to `ApiException`.
  - `PostOnce` exists for economic writes: one attempt, no redirect.

### 2.2 Auth (SIWS / MWA)

The chain is wired end to end in code:

1. `AuthService.SignInWithWallet` (`Net/Auth/AuthService.cs:31-65`) GETs `/auth/challenge` raw, so the signed strings stay exact.
2. `MwaWallet.SignIn` (`Solana/MwaWallet.cs:71-106`) calls `Web3.LoginWalletAdapter(SignInPayload)`.
3. The client POSTs `/auth/verify {address, signedMessage, signature, nonce}` and gets tokens.
4. `TryResume` and `Refresh` use the refresh token stored in PlayerPrefs, keyed per environment.
5. `SignOut` calls `/auth/logout` and then MWA `DeauthorizeWallet`.
6. Dev bypass: `/auth/dev {wallet}` runs only when `Env.allowDevAuth`, and the code path exists only in dev builds.

The API compiles against the pinned SDK (bd0d30f), so the call signatures exist. **It has never been run on a device against a server.** I found no successful `/auth/verify` in the repository's logs.

Gaps:
- `MwaWallet.cs:27` uses `identityUri = "https://ctlife.invalid/"` and `iconUri = "/favicon.ico"`. `[Unverified]` whether Seed Vault or Phantom reject a SIWS domain that does not match the identity URI.
- The refresh token sits in PlayerPrefs (`Core/Session/Session.cs:40`). The code itself acknowledges it should move to the Keystore.
- Access-token expiry is assumed to be "now + 14 min" (`Session.cs:39`) rather than read from the JWT `exp` claim.

### 2.3 Colyseus realtime

- **Schemas: verified.** Re-running `schema-codegen` 5.0.36 on `contract/realtime/*.ts` gives output identical to `Assets/Generated/Realtime`. Message payloads are hand-mirrored in `Assets/Generated/Support/RealtimeMessages.cs`, because Zod is not emitted by codegen. All 7 client→server and 11 server→client types match `messages.ts` field for field.
- **Rooms:**
  - `city` and `market` use `joinOrCreate` after sign-in (`FlowController.cs:262-272`), with options `{token}`.
  - `venue` is `joinOrCreate("venue", {token, venueId})` (`Net/Realtime/RealtimeService.cs:42-47,101-109`).
  - Room names match the backend checkout (`ct-life-backend/server/realtime/main.ts:41-43`).
- **Messages:**
  - Client sends: `move` (throttled to 10/s), `sit`, `stand`, `emote` (1/s), `say` (140 chars, every 2 s), `talking` (mirrors LiveKit speaking), `npc_tap` (from `ProfileCard`). See `Net/Realtime/Channels.cs:106-143`.
  - Client handles: `welcome` → `ServerClock` and `instanceId`; `rejected` → seat rollback, move snap, toasts; `said` → bubbles; `toast`, `event` → `IncidentPresentation`; `liquidated` → `RektCard` and a Perps refresh; `kicked` → sign-out; `space_update` → Spaces refetch.
  - **Unhandled:** `payment_confirmed`, `dm`, `call_invite`, `tape`. The `market` state (`coins`) has no consumer at all.
- **State sync.** Remote players use 120 ms snapshot interpolation (`World/Players/RemotePlayer.cs`). NPCs follow `NpcState` (`World/Npcs/NpcController.cs`), and late joiners snap using `startedAt`. Local movement is client-predicted and the server corrects it via `rejected:move`.
- **Backend-side risk, for the backend developer.** The local backend checkout (commit a5eafda) defines `venue` with no `filterBy(['venueId'])` (`ct-life-backend/server/realtime/main.ts:43-46`, and no `filterBy` anywhere in `server/`). The contract requires `filterBy venueId`. `[Inference]` Under Colyseus matchmaking semantics, `joinOrCreate("venue", {venueId:"terminal_cafe"})` can then land in an existing `timeline_square` instance. The client sends `venueId` correctly. Its `VenueSession` trusts that the joined room is the requested venue and never checks `State.venueId`; a guard there would help.
- **Never exercised.** `docs/progress/unity.md:55` itself records that the Colyseus join was skipped because no server was running. No "Joined … room" log line exists anywhere in `docs/`.

### 2.4 LiveKit voice

- **Proximity voice is real.** `VenueSession.ConnectVoiceOwned` (`World/Venue/VenueSession.cs:442-477`) does `POST /rtc/token {kind:"venue", id: instanceId}`, then `VoiceService.Connect` to room `venue:{instanceId}`. The mic needs an explicit gesture and Android permission (`Net/Voice/VoiceService.cs:381-387`). Audio is spatial with an 8 m radius and at most 6 subscriptions.
- It is **not auto-connected**: `Enter(..., voice:false)` is the default, so voice starts from the HUD mic button (`UI/Hud/Hud.cs:161-197`).
- Spaces audio (`Net/Voice/SpacesAudio.cs`) is real but unreachable (§3, issue 6).

### 2.5 Payments, ORE, SKR

**Missing.** No `/payments/intents`, `/ore/*` or `/paid-calls` wrapper exists. `ITransactionSigner` is registered (`App/GameBootstrap.cs:53`) but has no caller. `MwaWallet.SignAndSendBase64` (`Solana/MwaWallet.cs:119-135`) is untested, and its own comment marks the v0 `VersionedTransaction` round-trip `[Unverified]`. The phone tiles for Top up, ORE, Pulse and Predict are `ComingApp` placeholders (`App/PhoneCatalog.cs:19-23`).

### 2.6 Where game state comes from today

- **Every build with device evidence** (v5–v7) was launched with `review=1`. In that mode:
  - `ReviewTransport` (`App/Review/ReviewTransport.cs:32-37`) returns a hard-coded `Me` ("Amara", $2,487.50, fixed needs and skills, `lotteryRolled=true`) and throws on every other call;
  - `PracticeJobsApp` plays the 9 games locally ("No career, shift, score submission or payout API is called");
  - the Perps and Spaces "presentation reviews" render explicitly simulated data with writes disabled;
  - venues enter **offline** (no room), so NPCs and other players are absent.
- **The real flow** has no local economy, market simulation or NPC brain. Cash and needs change only from server responses: `PlayerProfile.SetCash` is called only with `/needs/actions`, `/me` and the perps account response.

### 2.7 Client logic that should be server-authoritative

I found little, which is the right outcome. Residual items:

- **Leverage cap computed on the client** from config, with hard caps of 20 and 25 (`Net/Rest/PerpsApi.cs:103,162-167`). This is acceptable as UI pre-validation because the server returns `LEVERAGE_NOT_ALLOWED`, but the numbers belong in config.
- **Mini-game scores are produced and reported by the client** (`POST /shifts/{id}/complete`). This follows the contract (the server only caps at `maxScore`). `[Inference]` It is trivially farmable: any client can post `maxScore` every shift. This is a contract-level anti-cheat gap to raise with the backend developer, not a client bug.
- **Own Home is a local scene** with no `home` room. The contract allows this until Phase 4.

---

## 3. Correctness risks (ranked)

1. **New users cannot finish onboarding against rc1.** Creator submit requires every look item to be confirmed by `GET /config` (`Core/Data/ConfigStore.cs:49-56`, `UI/Screens/CharacterCreatorScreen.cs:44-45`). The only selectable looks are the Snow and Rain authored looks (`UI/Screens/CreatorLookCatalog.cs:27-48`), whose IDs are not among the 12 provisional items in `contract/ids/items.json`. So against the real backend, `CanSubmitLook` stays false and the player is stuck in the creator. This is integration-blocking, and the fix belongs in the contract (proposal already in `docs/fe-contract-proposals/`).

2. **Cold start has no recovery path.**
   - `AuthService.TryResume` rethrows network errors (`Net/Auth/AuthService.cs:90`, `when (!e.IsNetwork)`).
   - `EnterGame` does not catch a `GET /me` failure (`App/FlowController.cs:258`).
   - Both propagate to `Start`'s catch (`FlowController.cs:69-70`), which only shows a toast. The splash is already closed, so the app sits on an empty screen with no retry until it is restarted. This is likely on mobile networks.

3. **Realtime joins send an expired JWT.**
   - `RealtimeService.Options` reads `_session.AccessToken` with no staleness check or refresh (`Net/Realtime/RealtimeService.cs:42-47`).
   - The token lives 15 minutes (`Session.cs:39`). The 2-minute config refresh is unauthenticated (`Api.cs:36`), so it never refreshes the token.
   - After about 14 minutes without an authenticated REST call, every venue travel fails the join and drops to the "Realtime is offline. You're alone here" fallback (`FlowController.cs:452-456`).
   - `[Inference]` This will not show against the local backend checkout, whose `VenueRoom.onAuth` accepts any token (`ct-life-backend/server/realtime/rooms/venue.ts:106-108`). It will show on staging.

4. **No rejoin for city and market.** Join failures are logged and swallowed (`FlowController.cs:263-272`). `OnLeave` only logs (`Net/Realtime/RoomChannel.cs:64-68`). If either room is unavailable at login, or drops beyond the SDK's reconnection window, the session runs without liquidations, kicks or (future) payment confirmations until the player signs out.

5. **The production build cannot target staging or prod safely.**
   - `CTLIFE_PROD` is referenced in 62 files but **defined nowhere**: `ProjectSettings.asset` has `scriptingDefineSymbols: {}`, and `Editor/BuildScript.cs:35-89` never adds it.
   - As a result, release builds still read the `env`, `wallet`, `venue`, `livekitToken` and `livekitUrl` Android intent extras (`Core/Env/LaunchArgs.cs:34-51`) and honour `env=` (`Core/Env/Env.cs:27-32`). Any app on the device can relaunch the game pointed at a different backend. `[Inference]` This is a phishing surface for SIWS.
   - The committed `Resources/Env/EnvSelection.asset` has `active: 0` (Mock, localhost).
   - The staging and prod realtime URLs are `wss://staging-realtime.invalid` and `wss://realtime.invalid` (`Core/Env/EnvConfig.cs:74,80` and the assets).
   - There is no `-ctlifeEnv` build flag.

6. **Implemented Perps and Spaces cannot be opened in the real flow.** `PhoneShell.CurrentPhase` is 1 and never changes (`UI/Phone/PhoneShell.cs:45`). `PerpsApp.Phase => 2` and `SpacesApp.Phase => 3` (`PerpsApp.cs:20`, `SpacesApp.cs:22`). `Launch` therefore refuses them with "coming soon" (`PhoneShell.cs:210-215`). They have only been exercised through review fixtures.

7. **The lottery can dead-end.** `LotteryScreen.OnRoll` (`UI/Screens/LotteryScreen.cs:69-80`) shows any error and re-enables "Roll". If the first roll committed but its response was lost after the transport's retries, each later tap uses a new idempotency key, gets `409 ALREADY_ROLLED`, and shows "No re-rolls" forever. It should refresh `/me` and continue. The dev spike already treats `ALREADY_ROLLED` as success (`App/Spike/SpikeController.cs:162`); the product flow does not.

8. **The REST busy-retry loop is unbounded.** In `RestClient.cs:149-157`, `SERVER_BUSY` or a 503 with `Retry-After` retries forever, capped at 30 s or 120 s per wait. `float.TryParse(res.retryAfter, out var ra)` at `:163` is culture-sensitive, unlike `RetryAfterSeconds`.

9. **Subscriptions grow per removed entity.** `Track(cb.OnChange(p, …))` registered inside `OnAdd` for players, NPCs and coins (`Net/Realtime/Channels.cs:55,88,94`) is released only when the room is left. In a busy 50-player venue over a long session, the list and the callbacks for departed players accumulate. `[Inference]` Small but unbounded.

10. **All venue art is loaded through Resources.**
    - `Resources/World/VenueContentRegistry.asset` hard-references every venue prefab (Timeline, Café, Studio, Basement).
    - `[Inference]` The first `Resources.Load<VenueContentRegistry>` therefore loads all of their meshes, textures and lightmaps into memory at once.
    - That load is called from `VenueSession.CanEnterOwnHome` (`VenueSession.cs:67`), which the Home app's availability predicate evaluates repeatedly, as well as from `VenueLoader`.
    - Everything in `Resources` also ships regardless of use. `Resources/` is 142 MB, 130 MB of it audio (streamed Vorbis q0.45, so the shipped size is smaller), against a 150 MB APK budget that v7 already exceeds.

11. **Character-specific runtime corrections run per avatar.** `SnowTransitionContact` re-skins foot vertices on the CPU every frame during transitions and validates blendshapes by throwing (`World/Avatars/SnowTransitionContact.cs:85-130`). `[Inference]` With many Snow-look players in a 50-player venue, this is a CPU cost the Mali-class device cannot absorb.

12. **Editor null pitfalls.** `GetComponent<T>()?.Method()` on UnityEngine objects (`World/Venue/VenueLoader.cs:100,196-197`) bypasses Unity's null semantics. In the editor, a missing component returns a "fake null", so `?.` does not short-circuit.

13. **Minor issues:**
    - `Mathf.Abs(string.GetHashCode())` (`AvatarFactory.cs:107,137`) returns a negative value for `int.MinValue`, which would produce `skin_0-3`.
    - The config cache is not keyed by environment (`ConfigStore.cs:41`), unlike the session keys.
    - `Session.Clear` leaves the stored wallet behind.
    - `ClipCutStoryboard.Load` keeps its Resources textures loaded after the game ends.

**Hard-coded values that belong in contract or config:**
- need-low threshold `25` (`UI/Hud/Hud.cs:152,157`)
- leverage bounds `20` and `25` (`PerpsApi.cs:103,167`)
- realtime rate limits written as literals (`Channels.cs:109,122,133,136`) instead of the mirrored `RealtimeLimits`
- `CurrentPhase = 1` in two places
- movement speeds that differ between files: `DirectMovementInput.WalkSpeed = 2.2f` (`Core/Services/ControlSettings.cs:42`), `NpcController.WalkSpeed = 1.6f`, and the handoff's stated 1.75 m/s.

**Dead code:**
- the modular character path (`ItemCatalog`, `CharacterAssembler`, `GeneratedAvatarResources`)
- the unused `Api` methods listed in §2.1
- the unguarded `App/Spike/*` and `Net/Voice/SpacesCaptureProbe.cs`
- the `Spike.unity` scene in the build
- the UI→Colyseus asmdef reference

**Good practice worth keeping:**
- Every `async void` has a try/catch.
- Bus handlers are isolated with try/catch.
- `Subscriptions` disposal patterns are used consistently.
- `RoomChannel.Join` drains superseded joins.
- Pause and focus cancel movement and seat presentation.
- The microphone is never auto-resumed after a reconnect.

---

## 4. Compile health

| Pass | Result |
| --- | --- |
| `python3 client/Tools~/compile_check.py --editor --keep-going` (as instructed) | All 8 runtime CT Life assemblies and all third-party assemblies compile. **`CTLife.Editor` FAILS with 14 errors (CS0246)**. Exit code 1. |
| Same runtime sources, dev player defines, warnings enabled (scratch rsp, `-warn:4`) | 0 errors. **3 warnings**, all CS0649 in `App/Review/DeviceGaitSampler.cs:58,62`. |
| Release player defines (no `DEVELOPMENT_BUILD`), with and without `CTLIFE_PROD` | 0 errors, 0 warnings in all 8 runtime assemblies. |
| Editor defines on the runtime assemblies, then `CTLife.Editor` against them | The 14 errors disappear. **107 new errors in 41 editor files**: all are `UniversalAdditionalCameraData` or `UniversalRenderPipeline.SingleCameraRequest` missing from the harness's URP stub. |
| `python3 client/Tools~/thermal_budget_check.py` | PASS, 27 assertions (the only standalone automated check). |

Interpretation:

- **The 14 errors are a harness defect, not a C# bug.** `compile_check.py` compiles runtime assemblies only with player defines (`compile_check.py:200` uses `defines(u.editor)` per unit). Editor-only types live in a runtime assembly under `#if UNITY_EDITOR` (`World/Review/*.cs`: `CafeExitChoreographyTargets`, `SeatedTypingHandTargets`, `CafeEmergenceFootTargets`, `CafeWorkEmergenceTargets`), so they are invisible to the editor pass. Behind them, the URP stub hides 107 more errors. **The `--editor` check therefore cannot validate the editor assembly at all today.** The handoff's statement that "all player/editor assemblies passed" no longer holds for current source. `[Inference]` The real Unity editor compiles these files, since APKs were built after most of them were written. That is not proof for files changed after v7: `World/Avatars/SnowRunCorrection.cs`, `SnowTransitionContact.cs` and several `App/Review` and `Editor/KayKitRawRunCandidate*` files are newer than the v7 APK.
- **The harness hides warnings.** It always passes `-warn:0` (`compile_check.py:197`).
- **Root fix:**
  - move the editor-only MonoBehaviours out of `CTLife.World` (an editor-only asmdef, or a `Review` asmdef with `defineConstraints`);
  - have the harness compile a second runtime pass with editor defines;
  - extend the URP stub, or reference the real URP sources.
- **Tests:** there are no NUnit or Unity Test Framework tests. The repository has no `Tests` folders and no `[Test]` attributes. Verification lives in:
  - (a) 36 runtime "Review" MonoBehaviours, about 8.9k LOC, which need the editor or a device;
  - (b) about 119 editor `-executeMethod` review and candidate scripts;
  - (c) inline `#if DEVELOPMENT_BUILD` check methods inside production classes (e.g. `ShiftReliabilityChecks`, `UI/Phone/Apps/ShiftReliability.cs:44-93`);
  - (d) `Tools~/checks/ThermalBudgetChecks.cs`.
  
  Nothing runs in CI, and edit-mode tests cannot run without launching the editor because none exist.

---

## 5. What I would change first

The user wants a quality game, plus backend integration in under one hour. Ordered by how much each item unblocks:

1. **Run the real flow against the real backend before any more presentation work.** `ct-life-backend` has `docker compose --profile mock up` with api, realtime and workers, and its local contract is identical to ours except for the FE-owned `anims`, `sfx` and `spots`, which it has already merged. Launch `FlowController` (not `review=1`) in the editor with dev auth against it, then on the Seeker through `adb reverse`. Every item in §3 points 1–7 will surface in the first hour. That is where the risk is.
2. **Unblock onboarding.** Get Snow and Rain starter-item IDs into the contract (send the existing proposal), or temporarily offer one confirmed rc1 look. Treat `ALREADY_ROLLED` as success.
3. **Add a cold-start retry screen.** Wrap resume, `/me`, city and market in a "Can't reach CT Life, Retry" state. Refresh the token before every room join (`await auth.EnsureFresh()` in `RealtimeService.Options`). Rejoin city and market after a non-consented leave or a failed initial join.
4. **Make environment and prod first-class build inputs.**
   - Add a `-ctlifeEnv staging|prod` flag to `BuildScript` that writes `EnvSelection` and adds `CTLIFE_PROD` for prod.
   - Fill in the real staging realtime URL and the MWA identity URI.
   - Remove `Spike.unity` from release scenes.
   - That turns the checklist's "switch EnvConfig to staging and build" into a single command.
5. **Make the phase gate data-driven.** Drive it from config or a build flag, so Perps and Spaces can be tested in the real flow. Wire `payment_confirmed`, the market room and the tape, or stop joining `market` until something consumes it.
6. **Repair the verification toolchain.** Fix `compile_check.py` (editor-define runtime pass, warnings on, URP stub), and move `World/Review` out of the runtime assembly. Add a real `Tests/EditMode` assembly for the pure logic that already exists as hand-rolled checks: `ShiftReliability`, `RestClient.RetryAfterSeconds`, `ServerClock`, `ConfigStore.IsLookConfirmed`, `PerpsWire` parsing, `DirectMovementInput.Speed` and `ThermalBudget`. Add contract-shape tests that deserialize every `openapi.yaml` example into the generated models.
7. **Stop the tooling sprawl.** Archive the 119 candidate, review and probe editor scripts (45k LOC) and the versioned copies out of `Assets/` into `Tools~` or a separate branch. Keep only importers, builders and the build script. Delete the dead modular-character path. Then extract `VenueSession` into a scene loader, a room binding and a voice binding, and replace `VenueSession.Current` lookups in UI with an injected interface.
8. **Then quality.** Fix the performance budget (Resources registry → Addressables or per-venue loading, draw calls, per-avatar CPU corrections), and continue with the missing Phase 2 money surfaces (payments intent → MWA → poll → `payment_confirmed`, ORE board and stake) using the already-registered `ITransactionSigner`.

---

## Appendix: commands run (all read-only for the repo; outputs in the session scratchpad)

```sh
python3 client/Tools~/compile_check.py --editor --keep-going          # exit 1: CTLife.Editor 14 errors
python3 <scratch>/qa_compile.py                                       # variant passes over the cached .rsp files:
                                                                      #   A dev player -warn:4, B/B2 release, C editor defines
TMPDIR=<scratch> python3 client/Tools~/thermal_budget_check.py       # PASS 27
~/.cache/ctlife-codegen/schema-5.0.36/node_modules/.bin/schema-codegen contract/realtime/*.ts --output <scratch>/rt --csharp --namespace CTLife.Realtime
diff -r <scratch>/rt client/Assets/Generated/Realtime                 # identical
java -jar ~/.cache/ctlife-codegen/oag-7.25.0.jar generate -i contract/openapi.yaml -g csharp -o <scratch>/rest  (codegen_rest.sh args)
python3 <patched copy of patch_rest_codegen.py> ; diff -r              # identical (107 models)
diff contract/{openapi.yaml,realtime,config} ../ct-life-backend/contract/...   # identical; ids/anims,sfx,spots differ (BE merged FE proposals)
```
