READY TO COMMIT (job 2: dev error overlay): client/Assets/Scripts/Solana/MwaWallet.cs, client/Assets/Scripts/App/GameBootstrap.cs, client/Assets/Scripts/App/FlowController.cs (dev-only logerror extra), client/Assets/Scripts/Core/Env/LaunchArgs.cs, client/Assets/Tests/EditMode/MwaWalletTests.cs + .meta (NEW), client/Assets/Tests/EditMode/CTLife.Tests.EditMode.asmdef (+CTLife.Solana), client/Tools~/compile_check.py (EditMode test refs read from the asmdef), docs/progress/integration.md (v6 sign-in notes + section "First-time flow capture, and the dev error overlay"). NOT included: client/Assets/Scripts/UI/Screens/SignInScreen.cs (bleed fix, unverified on device; see the section). Commit message: "mwa: no SDK focus reconnect while SIWS is in flight (hide the cached key during sign-in; test pins the SDK key); dev builds keep the developer console closed on errors (logcat unchanged); dev logerror extra". Checks: compile_check dev/release/editor compile; EditMode 1363/1363; on the Seeker: logerror → logcat only, no overlay; real Seed Vault SIWS on this build → no OperationInFlightException.

# Integration lead log

Agent: integration lead (only agent that runs the Unity editor and adb on the Seeker
SM02G40619141343). Brief: `docs/AGENTS-BRIEF.md` "Current priorities (2026-10-07)".
Backend: devIykee's `ct-life-backend`, cloned read-only beside this repo.

## Local backend: start / stop (exact commands)

One-time setup (done 2026-10-07 on this Mac; moved to main @ 7243073 on 2026-10-08):

```sh
cd ../ct-life-backend
git fetch origin && git checkout --detach origin/main     # main @ 7243073; history was rewritten upstream
pnpm install --frozen-lockfile                             # Node 24.5 locally; engines >=22
cp .env.example .env                                       # gitignored; then set (values are local throwaways):
#   DATABASE_URL=postgres://ctlife:ctlife@localhost:5434/ctlife   REDIS_URL=redis://localhost:6381/0
#   JWT_PRIVATE_KEY=~/.ctlife-local/jwt-private.pem                 ENABLE_DEV_AUTH=true
#   TREASURY_PUBKEY_DEVNET / SETTLE_AUTHORITY_KEYPAIR_DEVNET / ADMIN_KEYPAIR_DEVNET -> ~/.ctlife-local/*-devnet.json
#   HELIUS_API_KEY=local-dev-no-helius  HELIUS_WEBHOOK_SECRET / LIVEKIT_WEBHOOK_SECRET = random hex
#   (7243073 keys) SIWS_DOMAIN=ctlife.invalid  SIWS_URI=https://ctlife.invalid/  ADMIN_WALLETS=   (empty)
#   SOLANA_RPC_URL=https://api.devnet.solana.com  TEST_DATABASE_URL=postgres://ctlife:ctlife@localhost:5434/ctlife_test
#   TEST_SCHEMA=test   (ctlife_test created with `createdb` in the ctlife-postgres container)
```

SIWS identity: one value. `EnvConfig.AppIdentityUri` in `client/Assets/Scripts/Core/Env/EnvConfig.cs` is the MWA
dApp identity, and the client expects the SIWS domain to be its authority. `up.sh` reads that line and starts the api
with `SIWS_DOMAIN`/`SIWS_URI` from it (process env beats `.env`). Today it is the placeholder `https://ctlife.invalid/`.
User decision 2026-10-08: no `.dev`; the real domain (a .fun or gaming TLD) is not bought yet. Switching later means
changing that line (and the hosted backend's two env vars).

Keys live in `~/.ctlife-local/` (outside both repos, chmod 700). Throwaway devnet keys only.

Every time:

```sh
tools/backend-local/up.sh                 # infra in Docker + migrations + local patches + api + realtime + workers
CTLIFE_WORKERS=0 tools/backend-local/up.sh  # same without workers
tools/backend-local/up.sh --bots 5        # + headless bots (note: backend bots leave after 200 ms)
tools/backend-local/down.sh               # stop node processes only (other agents share Postgres/Redis)
tools/backend-local/down.sh --containers  # also stop the containers (keeps the DB volume)
tools/backend-local/down.sh --wipe        # also drop the Postgres/Redis volumes
```

| Service | Where | Port |
| --- | --- | --- |
| REST (Fastify) | host node, `server/api/main.ts` | `http://localhost:4010/v1` (`/healthz`) |
| Realtime (Colyseus 0.18) | host node, `server/realtime/main.ts` | `ws://localhost:2567` |
| LiveKit dev | Docker `livekit/livekit-server:v1.13.7 --dev --node-ip 127.0.0.1` | 7880 ws, 7881 ICE/TCP, 7882 udp |
| Postgres 16 | Docker | 5434 |
| Redis 7 | Docker | 6381 |
| Workers | host node, `server/workers/main.ts` (start fine from 7243073) | none |
| MinIO | **not started** (image tags missing, feedback item 35; only ad uploads need it) | — |

Logs: `~/.ctlife-local/logs/{api,realtime,workers,migrate}.log`. PIDs: `~/.ctlife-local/run/`.
Why api/realtime run on the host and not in Docker: the backend uses one `LIVEKIT_URL` for server calls
and for the URL it hands clients, and its compose `env_file` URLs point at `localhost`; on the host both work.
The old `tsconfig.backend.json` override (feedback items 32-33) is gone: at 7243073 the backend's own tsconfig includes
`contract/realtime/**` with legacy decorators, and players (moves, emotes) and NPCs sync without it (observer + 3 bots,
`../ct-life-evidence/backend-7243073/bot2.json`). `tools/backend-local/patches/*.patch` are local fixes for backend bugs
that block the client; `up.sh` applies each one that applies cleanly and says so when upstream has made it obsolete.
Each is reported in `backend-dev-feedback.md` section G. Today: `0001-venue-profile.patch` (item 83: profile query
columns + look object → string[7]), `0002-character-look-validation.patch` (item 84: catalog ids and skin tones), `0003-config-emotes.patch`
(item 86), `0004-rtc-token-contract-id.patch` (item 88: body `id` and ISO `expiresAt`). Local `.env` also points `HELIUS_RPC_URL_DEVNET` at the public
devnet RPC (no Helius key locally), so `POST /ore/stake/tx` can fetch a blockhash.

Seeker over USB: `adb -s SM02G40619141343 reverse tcp:4010 tcp:4010; adb reverse tcp:2567 tcp:2567;
adb reverse tcp:7880 tcp:7880; adb reverse tcp:7881 tcp:7881` (adb reverse is TCP only; LiveKit media then
uses ICE/TCP 7881).

## 2026-10-07 — Part A results

**Backend pulled.** Local clone was on `feature/fe-registries` (our PR #1 branch, now merged). `origin/main`
had been force-rewritten (27 local vs 84 remote commits, same subjects, new hashes), so the clone is now
detached at `origin/main` = `9f3e5ef` (2026-10-06 05:59 +0100). Upstream now claims M0-M8 done (M4 partial).
PR #1 (FE registries anims/sfx/spots): **MERGED**.

**Contract re-synced.** Upstream `contract/` differs from our rc1 copy only additively: our PR #1 registries,
`realtime/states.ts` + `SpaceState`/`SpeakerState`/`VideoCallState` (M4), `idl/ct_escrow.json`, CHANGELOG.
Version string still `1.0.0-rc1`; the rc1 tag (`160d59e`) is no longer an ancestor of `main` but its contract
equals `main`'s M1 commit. Re-synced `contract/` from `9f3e5ef`, updated `contract/SOURCE.md`. C# codegen for
the new states is pending (touches `client/Assets/Generated`, Part B).

**Stack up.** Postgres/Redis/LiveKit healthy. `pnpm db:migrate`: 60/60 migrations applied; `pnpm db:verify`:
143/143 invariants OK. There is no separate seed step (config is read from `contract/config` at runtime).

**Backend test suite** (`pnpm test:unit`, vitest): **310 passed, 6 failed** of 316 (41 files). Causes are
backend-side: migration 060 runs its own down section (1 test) and `LLM_MODEL=` empty in `.env.example` (5 tests;
all 17 pass with `LLM_MODEL=stub-model`). `pnpm typecheck`: 3 errors. Details: feedback items 32-37.

**REST smoke** (dev auth, curl): `/healthz`, `/config`, `/auth/dev`, `/auth/challenge`, `/me`, `/characters`,
`/lottery/roll`, `/careers`, `/needs/actions`, `/leaderboards/net_worth` respond. Probe of all 90 contract
operations: 15 have no route, incl. **`POST /rtc/token`** and `GET /events/active` (item 40).

**Realtime smoke** (node `@colyseus/sdk` 0.18.5 from the backend's own deps): venue join + welcome, 5 server
NPCs in Timeline Square with brain states, `move` validation (`SPEED_EXCEEDED`), city room state. Blocking
findings: every player's `userId` is `unverified-eyJhbGci` (no JWT verification, item 38) and venue rooms are
not filtered by `venueId` (item 39).

**Implications for the client work (Part B):**
- Lottery must be rolled **before** character creation (server treats "has character" as rolled); 409 = done,
  and a second pre-character roll returns 500, so "rolled" is inferred from `/me` cash/debt too (item 41).
- The server accepts any look ids today; the starter-look proposal goes to `docs/fe-contract-proposals/`.
- Local players must be identified by the `welcome`/session id, not `userId`, until item 38 is fixed.
- Voice has no backend token route; a dev-only token path (local LiveKit dev keys) is the only way to test
  editor↔phone voice now.

## Feature table (2026-10-08) — local backend main @ 9f3e5ef

Editor = `tools/integration/run_editor_integration.sh` (real FlowController, env=local, dev auth, observer bot; feature
rows via `App/Dev/FeatureProbe.cs`, the lanes' own APIs). Seeker = dev APK (env=local, adb reverse). "blocked #N" =
backend gap, item N in `backend-dev-feedback.md`; no client workaround fakes server behaviour.

| Feature | Editor | Seeker |
| --- | --- | --- |
| Sign-in: dev auth | pass | pass (00:44, automation) |
| Sign-in: real SIWS via MWA (Seed Vault) | n/a (no MWA in editor) | **pass** 03:24 (user fingerprint; backend verify patched locally, see handoff) |
| Onboarding: lottery → creator (Snow/Rain look) | pass | pass |
| City + market rooms, rejoin on drop | pass | pass (market dropped 01:01, rejoined in 0.1 s) |
| Timeline Square presence: see/be seen move, emote, chat | pass | pass (bot heard Seeker chat, saw wave + walk) |
| Server NPCs visible | pass (5) | pass (5) |
| Shift pays Cash | pass (30000→34000) | not rerun on device |
| Block / report / unblock | pass | not rerun on device |
| Token refresh, then room rejoin | pass | – |
| Venue rooms other than Timeline Square | blocked #39 (client refuses the wrong room, enters offline) | same |
| All 21 built venues + own home load | pass (21/21, ~0.1-0.2 s each in editor) | pass (21/21, 0.1-0.3 s each) |
| Money: top-up intent | blocked #52 (no tx) | same as editor (SIWS user, 03:35 and stripped build 04:3x) |
| Money: spot buy | blocked #76 (always SLIPPAGE_EXCEEDED) | same as editor (SIWS user, 03:35 and stripped build 04:3x) |
| Money: perp open + close (1x, Trading lv 1) | pass | same as editor (SIWS user, 03:35 and stripped build 04:3x) |
| Money: ORE stake tx | blocked #55 (500); never signed | same as editor (SIWS user, 03:35 and stripped build 04:3x) |
| City life: needs (eat) | pass | same as editor (SIWS user, 03:35 and stripped build 04:3x) |
| City life: shop purchase | pass | same as editor (SIWS user, 03:35 and stripped build 04:3x) |
| City life: homes list / rent / visit | blocked #40, #75 | same as editor (SIWS user, 03:35 and stripped build 04:3x) |
| Careers: list + gig pays | pass | same as editor (SIWS user, 03:35 and stripped build 04:3x) |
| Social: DM send + thread | pass | same as editor (SIWS user, 03:35 and stripped build 04:3x) |
| Social: feed post | pass | same as editor (SIWS user, 03:35 and stripped build 04:3x) |
| Social: Space create / audio | create pass; audio blocked #40 | same as editor (SIWS user, 03:35 and stripped build 04:3x) |
| Social: call ring | blocked #54/#68 | same as editor (SIWS user, 03:35 and stripped build 04:3x) |
| World life: city events (crier) | blocked #64 | same as editor (SIWS user, 03:35 and stripped build 04:3x) |
| World life: live ads | pass (no bookings) | same as editor (SIWS user, 03:35 and stripped build 04:3x) |
| Rich List | pass (core Api) | pass |
| Home from phone → private home | pass | pass (Parent's Basement) |
| Chat with soft keyboard, emote labels | – | pass (panel docks above keyboard; labels readable) |
| Proximity voice (local LiveKit, dev token; no /rtc/token #40) | – | mic track published (audio/red), seen by LiveKit |
| EditMode tests (Unity) | 792/792 | – |
| Ventures: Ad Desk (slots, bookings) | pass (4 slots, 0 bookings) | pass: opens, slots and kinds listed |
| Ventures: Builder Hills projects | create blocked #77 (seed $500, $339 cash, VALIDATION_FAILED); list/offers n/a #78 | pass: opens, "Not available yet" |
| Ventures: VC Row | n/a #78 (no `GET /vc-row/deals`) | pass: opens, "Not available yet" |
| Ventures: Hire on profile card | wired (copies the player id, opens Builder); not exercised, hire offers n/a | – |
| Ventures: ads_changed → live ad refresh; spot links | wired; server sends no `ads_changed` #80; `builder_hills` spots fe.3 have no scene yet | – |
| Competition: League, tribes, leagues | pass (7 tribes, 5 leagues, no membership before rollover); 0 districts #79 | pass: "No league yet", tabs |
| Competition: Beef | open pass (stake, topic, invited peer → `open`); Elo pass; list n/a #79 | pass: opens, list "Not available yet" |
| Competition: Farm / Sybil Barn | n/a #79 (`/airdrop/farm`, `/me/sybil-status`) | pass: "Not available yet", Check again |
| Competition: Cabal, gated venues | invites pass (0); `/me/cabal`, `/venues/{id}/access` n/a #79 (gate falls through; the room decides) | pass: "Not available yet" |
| Competition: NPC Chat (AI Agent, Sleuth; profile "Talk") | quota n/a #79; app opens, input says "Not available yet" | not captured (carrier USSD dialog over the app) |
| Competition: X link (Settings section + app) | `GET /x/link` pass (not linked); app opens | not captured (adb server restarts) |
| Competition: raw room messages, sybil kick → Sybil Barn | wired (14 city types, 2 venue types); server sends none #80 | – |

APK (local dev, all lanes + 18 venues + 4 homes): 183.8 MiB (budget 150; was 159.5 before the venue merge).
Audio payload measured 20.9 MB `.resource` (audio lane estimate 20.5-21.3; was 36.7 before the import policy).
Perf on the Seeker (dev build, thermal status 2-3 = game caps at 30 fps; CPU main / GPU ms of the last frame):
| Scene | avatars | fps | draw calls | triangles | CPU main | GPU |
| --- | --- | --- | --- | --- | --- | --- |
| Timeline Square | 6 (5 NPC + me) | 30.1 | 202 | 156k | 14.6 | 13.9 |
| Perp Pit (venue lane) | 1 | 30.1 | 27 | 33k | 10.1 | 9.7 |
| Terminal Café | 1 | 30.1 | 202 | 56k | 9.9 | 10.6 |
| Timeline Square, 30-bot crowd, cast LODs (12 LOD0 / 21 LOD1) | 35 | 30.1 (thermal cap) | 256 | 238k | 16.6 | 17.7 |
| same, thermal override 0 for 45 s (restored) | 35 | **60.3** (p99 16.6 ms) | 253 | 228k | 10.8 | 13.9 |
→ A 35-avatar crowd holds 60 fps once the thermal cap allows it, but the city is 253 draw calls (budget < 150): ~200 of
them are the old Timeline Square/Terminal Café prefabs (rebuilt venues are 19-27). Asked the venue lane to rebuild them
(requests.md). Avatars add ~50 (35 skinned + blob shadows).

Cast import (2026-10-08): `CastImport.Run` (menu CT Life → Cast) copies art/cast/export looks into
Assets/Art/characters/cast, imports Humanoid with Copy From Other (m → Snow, f → Rain avatars), mesh compression Medium,
one URP/Lit material per look, prefabs with manifest scale + LODGroup (AvatarLod now forces the tier by crowd rank).
50 archetypes bound (all contract ids), Snow/Rain look keys now point at the cast identity looks (light, LOD'd),
22 player looks added to the creator (FE look ids `skin_*|head_<look>|...`, server accepts any ids today).
Remote players without a server look (#47) get a stable creator look by session hash (presentation only).
Not done: runtime wardrobe merge from the 138 part chunks (step 9 of the cast plan). Snow/Rain rig credits are already
in AssetCredits (CC BY 4.0, studio.blender.org).

APK size (local env; packed asset sizes from the build report, MiB):
| Category | before (183.8 MiB dev APK + cast, 217.1) | after |
| --- | --- | --- |
| cast textures (ASTC 6x6; only hero BaseColor at 1024, rest 512) | 94.7 (all character textures) | 46.5 |
| character meshes (mesh compression Medium) | 62.0 | 16.7 |
| venue textures (Android cap 1024: 24 sign/UI atlases were 2048) | 36.7 | 10.4 |
| venue meshes / lightmaps / audio / shaders | 63.0 / 6.8 / 21.0 / 5.4 | unchanged |
| libunity.so (dev unstripped → release) | 36.6 | 20.9 |
| classes.dex (R8 minify release) | 11.8 | 0.7 |
| libil2cpp.so (managed stripping Low → Medium, OptimizeSize) | 55.9 (dev, uncompressed) | 55.2 |
| **APK** | 217.1 dev / 183.8 dev pre-cast | **140.3 release** (216.7 dev) |
The budget applies to the release APK: 140.3 MiB < 150. Remaining big items: venue meshes 63 MiB (venue lane),
LiveKit native 18.7 MB, Solana SDK Resources textures 7.4 MB (package, not editable), Unity splash 2.7 MB.
3D home visits (City Life hook 6): not wired — the backend has no `home` room and visits return no `roomId` (#75);
the client code would be unverifiable, so it waits for the room.

## 2026-10-07/08 — Part B (coordinator: proceed on local commits)

### Restructure
- 122 editor probe/candidate/review scripts → `client/Experiments~/Editor/` (kept in `Assets/Scripts/Editor`: build,
  setup, importers/AssetPostprocessors, venue builders: 21 files). Runtime fixtures → `client/Experiments~/Runtime/`
  (`App/Review` 36 files incl. `ReviewTransport`/`FrontendReviewController`/`PracticeJobsApp`, `App/Spike`,
  `Net/Review`, `World/Review`, `PerpsReviewProbe`), `Scenes/Spike.unity` → `Experiments~/Scenes` and out of Build
  Settings. No review fixture is kept: the real flow against the local backend replaces it (`review=1` is gone).
- Evidence paths inside Experiments~ now point at `../ct-life-evidence/...`; no compiled code references the four
  symlinked evidence folders any more (the QA report's own `device/` image links still use that symlink).
- 9 unreferenced duplicate `UniversalRenderPipelineGlobalSettings N.asset` deleted (GraphicsSettings references the
  unnumbered one, guid 532bd9ed…).
- `client/Tools~/compile_check.py`: per-run output dir (parallel agents clobbered a shared one), `--editor` now
  recompiles every runtime assembly with editor defines before `CTLife.Editor` (the old 14 CS0246 were a harness
  defect), warnings on (`-warn:4`) and listed, `--warnaserror`, `--release` (CTLIFE_PROD, no DEVELOPMENT_BUILD),
  Features.Money + both EditMode test assemblies + the integration driver included.
  Result now: dev player, release player and editor passes all compile, 0 warnings.

### Integration fixes (client)
- Env/build: `BuildScript -ctlifeEnv mock|local|staging|prod` (selects EnvConfig for the build, restores the
  committed selection), prod = release + `CTLIFE_PROD` via `extraScriptingDefines`, staging/prod refuse `.invalid`
  hosts, dev auth and non-https. Local env REST fixed 3000 → 4010. MWA identity name/URI/icon now per EnvConfig.
  `livePhase` per env (local/mock 6, staging/prod 1) + server `livePhase` wins (`Core/Data/Phases.cs`).
- Auth: SIWS verify retries once with the UTF-8 SIWS text when the base64 body is rejected (backend item 46); auth
  responses accepted in contract and flat shapes, refresh identity from JWT claims (item 45); access-token refresh
  time from the JWT `exp`; `EnsureFresh` before every room join; stored wallet cleared on sign-out.
- Start-up: any failure (config/resume//me/rooms) shows "Can't reach CT Life · Try again" and resumes.
- Onboarding: lottery before creator; 409 or a reconciled /me = done (dead-end fixed); creator submits authored looks
  (server is authoritative); `/me` decoded tolerantly (`user.hasCharacter` missing on backend main → DECODE_FAILED
  blocked everyone, item 48); `/careers/{id}/apply` tolerant (item 49); bodiless POSTs send `{}` (415 → 500, item 50).
- Realtime: all rooms decoded with `DynamicSchema` and mapped by name into the generated classes (server schema ≠
  contract, item 47); one change subscription per live entity, released on remove (leak fix); city/market kept alive
  with capped backoff; venue room must match the requested venue (item 39 guard); unplaced (0,0) spawn → entrance;
  identity-safe player mapping (item 38); profile fields sent in join options. SERVER_BUSY capped (5) in REST and joins.
- Content: `VenueContentRegistry` moved out of Resources to `Assets/Venues/`; runtime uses per-venue
  `Resources/World/VenueContent/*.asset` + an id-only index (synced by `VenueContentCatalog.Sync`, also before builds).
- UI (functional only): chat docks above the keyboard + OS input line + 4-line history; emote buttons ≥30% width with
  labels; volume sliders → −/+ steppers; client version 1.0.0 (backend `minClientVersion` 1.0.0 showed "Update required").
- Phase 2 lane wired per requests.md: App references CTLife.Features.Money, PhoneCatalog uses its apps, MoneyRektCard,
  live economy/skills/lottery/needs config.

### Tests
- EditMode (`client/Assets/Tests/EditMode`, Unity Test Framework 1.6.0 = the editor's builtin package, verified in
  packages-lock + PackageCache): auth/JWT/base58, onboarding + /me decode, retry/backoff/env/phase/build defines,
  StateMapper. Run: `Unity -batchmode -projectPath client -runTests -testPlatform EditMode
  -assemblyNames "CTLife.Tests.EditMode;CTLife.Features.Money.Tests" -testResults <xml>`.
- Editor integration: `tools/integration/run_editor_integration.sh` (needs `tools/backend-local/up.sh`): real
  FlowController in play mode (batchmode), env=local, fresh dev wallet, plus `tools/integration/observer-bot.mjs`
  as the second client. Receipts in `../ct-life-evidence/integration/editor-*/`.
  **Result 2026-10-07 23:53: 12/13 PASS** — sign-in, lottery→creator, city+market, Timeline Square join, 5 server
  NPCs, sees bot move (2.0 m) and chat, bot sees our move/emote/say, shift pays Cash (30000→34000), block/report/
  unblock, token refresh + rejoin. FAIL: venue isolation (backend item 39; client guard refuses the wrong room).

## 2026-10-08: Branding (official CT mark in the app)

Scope after the user's correction: only the CT mark (`art/branding/logo/ct-mark-*.png`, c3a17bb) is approved. The
website draft is not approved. So there is no DynaPuff or Chakra Petch, no new lockup, and no other layout, colour or font changes.

**Generator.** `client/Assets/Scripts/Editor/BrandAssets.cs` produces every brand asset from the source.
- Run it from the menu (CT Life → Build → Apply brand assets) or in batch
  (`-executeMethod CTLife.EditorTools.BrandAssets.ApplyBatch`).
- `BuildScript.BuildPlayer` calls it before every build, so icons and splash always match the source.
- It rewrites only files whose bytes changed, and reimports only when importer settings changed.
- `sync_build_clone.sh` now copies `art/branding/logo` next to the build clone.
- If the source is missing, it keeps the committed outputs and logs a warning.

**Colour and icons**
- **Brand purple** sampled from `ct-mark-on-purple-1254.png`: rgb(32,4,82) = `#200452`.
- **Android icons** live in `client/Assets/Art/branding/generated/`:
  - Adaptive icon: 432 px solid purple background, transparent-mark foreground. The mark's farthest opaque pixel sits at
    0.96 × the 66 % safe-circle radius (108 dp layer, 66 dp safe zone).
  - Legacy icon: 192 px, mark on purple (radius 78 %).
  - Round icon: 192 px, the same, circle-masked (radius 82 %).
  - All 6 densities of each kind are set through `PlayerSettings.SetPlatformIcons` (adaptive = [background, foreground]).
  - The APK carries `mipmap-anydpi-v26/app_icon.xml`, `app_icon_round`, `ic_launcher_foreground/background` and
    `drawable/unity_static_splash.png`.

**Splash.** Unity's "Made with Unity" splash is **off** (`SplashScreen.show = false`, `showUnityLogo = false`,
background `#200452`). The Android static splash image is the mark centred on purple (2670×1200, scale-to-fill).
Sources:
- Unity 6.2 Manual, Splash Image Player settings,
  https://docs.unity3d.com/6000.2/Documentation/Manual/class-PlayerSettingsSplashScreen.html: "Use the Splash Image
  Player settings to customize or disable the Unity logo, add your own logos, or disable the splash screen entirely."
- 6000.3 Scripting API `PlayerSettings.SplashScreen.show`: "Set this property to false to disable the Splash Screen".
  Also `showUnityLogo`.
- Unity blog, 2024-09-12, https://unity.com/blog/unity-is-canceling-the-runtime-fee: "The Made with Unity splash
  screen will become optional for Unity Personal games made with Unity 6 when it launches later this year."
- [Unverified] The 6000.3 Manual page returned 404 at both URLs I tried, so the exact 6.3 Manual wording is
  unconfirmed. The 6.3 API and the policy above are confirmed.

**In app.** The mark replaces the old building icon at the same sizes:
- `El.Brand()` (sign-in, character creator, connection-retry, update-required). The "CT LIFE" wordmark beside it keeps
  its current `.ct-brand-wordmark` font and colour.
- Loading-screen emblem, 192 px.
- Busy overlay, 52 px.
- Mark source: `client/Assets/Resources/UI/Brand/ct-mark.png` (512 px, ASTC 4x4).

No lockup was added where none existed (lobby and phone home are unchanged).

**Launch.** `GameBootstrap.Awake` now creates `UiRoot` and shows the loading screen before perf and audio init. The
flow adopts that same screen (`SplashScreen.TakeBoot`), so the scene's empty sky is never shown. The loading screen is
the only screen that takes the brand purple, so it matches the Android splash. Its text is white and its progress bar
is an indeterminate sweep. Retry and update screens keep their colours.

**Seeker (SM02G40619141343), dev APK env=local, cold launches.** Evidence is in `../ct-life-evidence/branding/`.

| Stage | Result | Evidence |
|---|---|---|
| Launcher icon | CT mark on brand purple, inside the launcher's squircle mask, label "CT Life" | `01-launcher-icon.png`, `01-launcher-icon-crop.png` |
| Splash | Tap → purple window (≈1.1 s) → mark centred on purple (1.35–4.38 s). No Unity logo, no empty sky. [Unverified] whether that frame is drawn by Android's 12+ system splash or by Unity's static splash; both are branded. | `02-splash-static.png`, `coldstart-fresh.mp4` |
| Loading | On a local backend the static splash holds until the first UI is ready: frame-by-frame, sign-in follows the splash directly (`-e fresh 1` at 4.38 s; dev autologin at 4.55 s). With the backend unreachable, the in-app loading screen shows for 4–8 s (purple full-bleed, mark, existing "CT LIFE" font, tagline, sweeping bar, status), then the retry screen. | `03-loading.png`, `coldstart-offline.mp4` |
| Sign-in (`seeker_launch.sh -e fresh 1`) | Header: mark + "CT LIFE" in the existing font. Nothing else changed. | `04-signin.png` |
| Home / lobby (`-e autologin dev -e autocreate 1`) | Unchanged, as the correction requires (no lockup added) | `05-home-lobby.png`, `coldstart-autologin.mp4` |
| Connection retry | Mark replaces the building icon | `06-retry-offline.png` |

Notes for the coordinator:
- The retry screen's "Try again" button renders as a narrow pill with the text wrapped ("a / g"); see `06-retry-offline.png`.
  [Inference] It predates this change, because its button, classes and container styles are untouched. I did not fix it
  because of the "no other layout changes" rule. It needs a go-ahead.
- `-e fresh 1` cleared the user's real SIWS session on the Seeker. The device is now signed in as dev wallet
  `dev67417`. The next real-wallet check needs the user's fingerprint sign-in again.
- The dev APK is 213.5 MiB. The release-size budget (140.3 MiB last round) was not re-measured. The new assets add
  [Inference] roughly 0.3 MiB of icon/splash PNGs plus a ≈0.33 MiB 512 px ASTC 4x4 mark with mipmaps (not measured).
- Build logs: `../ct-life-evidence/branding/apply-brand.log` (one main-project batch run) and `build-dev.log`.
  Compile check is green in all three passes. The single editor warning is in the venue lane's `VenueVerify.cs`.

**READY TO COMMIT (branding).** These are all of this round's changes. Nothing else in the tree is mine.
- Modified:
  - `client/Assets/Resources/UI/ctlife.uss`
  - `client/Assets/Scripts/App/FlowController.cs`
  - `client/Assets/Scripts/App/GameBootstrap.cs`
  - `client/Assets/Scripts/Editor/BuildScript.cs`
  - `client/Assets/Scripts/UI/El.cs`
  - `client/Assets/Scripts/UI/Overlays/BusyOverlay.cs`
  - `client/Assets/Scripts/UI/Screens/SplashScreen.cs`
  - `client/Assets/Scripts/UI/UiRoot.cs`
  - `client/ProjectSettings/ProjectSettings.asset`
  - `tools/integration/sync_build_clone.sh`
  - `docs/progress/integration.md`
- New:
  - `client/Assets/Scripts/Editor/BrandAssets.cs` (+ `.meta`)
  - `client/Assets/Resources/UI/Brand.meta`
  - `client/Assets/Resources/UI/Brand/ct-mark.png` (+ `.meta`)
  - `client/Assets/Art/branding.meta`
  - `client/Assets/Art/branding/generated.meta`
  - `client/Assets/Art/branding/generated/` files, each with its `.meta`:
    - `icon_adaptive_background_432.png`
    - `icon_adaptive_foreground_432.png`
    - `icon_legacy_192.png`
    - `icon_round_192.png`
    - `splash_landscape_2670x1200.png`
- Not included: DynaPuff and Chakra Petch (removed per the correction; no font files or USS references remain).

## 2026-10-08: Device round on dev 456ad50 (repacked venues, retry button)

### 1. Connection-retry "Try again" button (user-approved fix)
- **Cause, confirmed in Unity 6000.3.25f1's C# source** (`Modules/UIElements/Core/VisualElementHierarchy.cs`,
  UnityCsReference tag 6000.3.25f1):
  - Adding a child to an element calls `RemoveMeasureFunction()`. It is restored only when the child count drops back to 0.
  - Every `El.Btn` adds two decorative children (`ct-button-depth`, `ct-button-glint`), so no button measures its own text.
  - A button that its parent sizes (stretch, flex-grow, explicit width) looks right. One that no parent sizes collapses
    to its padding and wraps one letter per line. Example: retry in `.ct-splash`, a centre-aligned column; the pill was
    80 ref px = 72 padding + 8 border.
- **Fix** (`client/Assets/Scripts/UI/El.cs`, `KeepTextWidth`, called from `El.Btn`):
  - It acts only when a button is laid out (border box > 0) but its content box is empty.
  - It then sets `min-width` = the single-line text width (`TextElement.MeasureTextSize`) + padding + border, capped to the parent.
  - It re-checks on geometry and text changes (`ChangeEvent<string>`).
  - Buttons a parent already sizes, and hidden ones, are untouched, so nothing else changes look.
  - Dev builds log each corrected button (`CTLIFE [ui] button "…" had no width from its parent`).
- **Other buttons with the same bug.** Code check of the text buttons' containers:
  - The Social lane's "ongoing call / space" button sits in `.ct-social-chip-bar`, also a centre-aligned column. It has
    the same collapse and the generic fix covers it.
  - Lottery, sign-in, lobby, app pages, sheets and HUD stretch or size their buttons. The HUD chat button already had a
    `min-width: 240px` workaround.
  - Device logs from this round's launches name no other corrected button. The first version had two false positives
    on hidden buttons (lobby "OPENING THE CITY…" and the incident history button); the border-box guard removed them.
- Device check: verified on the Seeker by launching with the backend unreachable. "Try again" is one line with its yellow fill, outline and depth unchanged; the log shows `min-width 249 from its text`. Evidence: `../ct-life-evidence/perf-venues-20261008/07-retry-fixed.png` (before: `../ct-life-evidence/branding/06-retry-offline.png`).

### 2. Venue perf on the Seeker
Dev APK env=local, dev account session. Median of 5 s `[fps]` windows; CPU main / GPU are frame timings of the last frame.
- "Normal" means the Seeker's own thermal state, status 2–3, where the game caps at 30.
- "Lifted" means `cmd thermalservice override-status 0`, restored with `reset` afterwards. Only windows where the game's
  target is 60 are kept. `ThermalBudget` holds the 30 cap for 30 s after cooling (`RecoverySeconds`), so a 45 s override
  yields only 2–3 windows at 60; I re-ran the lifted phase for 95 s.

| Venue | avatars | phase | windows | fps | p99 ms | draw calls | triangles | CPU main ms | GPU ms |
|---|---|---|---|---|---|---|---|---|---|
| Timeline Square, 30-bot crowd | 35 | normal | 11 | 30.1 (cap) | 33.3 | 105 | 237k | 16.3 | 18.4 |
| Timeline Square, 30-bot crowd | 35 | lifted | 17 | **60.3** (min 59.3) | 16.6 | 104 | 230k | 9.8 | 14.2 |
| Timeline Square, alone | 1 | lifted | 12 | 60.3 | 16.6 | 36 | 120k | 8.7 | 12.5 |
| Terminal Café | 2 | normal | 11 | 30.2 (cap) | 33.4 | 37 | 74k | 8.8 | 10.7 |
| Terminal Café | 2 | lifted | 12 | 60.3 | 16.6 | 35 | 74k | 8.7 | 11.6 |
| Spaces Studio | 1 | normal | 11 | 30.1 (cap) | 33.3 | 25 | 30k | 8.1 | 9.7 |
| Spaces Studio | 1 | lifted | 12 | 60.3 | 16.6 | 25 | 30k | 7.7 | 10.0 |

**Compared with last round**

| | Before | Now |
|---|---|---|
| Timeline crowd, normal | 30.1 fps, 256 draws, 238k triangles, GPU 17.7 ms | 30.1 fps, 105 draws (−59 %), 237k triangles, GPU 18.4 ms |
| Timeline crowd, lifted | 60.3 fps, 253 draws, 228k triangles, CPU 10.8 / GPU 13.9 ms | 60.3 fps (17 windows), 104 draws (−59 %), 230k triangles, CPU 9.8 / GPU 14.2 ms |
| Terminal Café | 202 draws, 56k triangles, CPU 9.9 / GPU 10.6 ms | 37 draws (−82 %), 74k triangles (+32 %), CPU 8.8 / GPU 10.7 ms |

- **Triangles vs draws saved.** The triangles do **not** cost more than the draws saved:
  - Timeline's triangle count is unchanged.
  - The café's +18k triangles show no GPU change at the cap, and 11.6 ms at 60 fps, well inside 16.6.
  - No cell-size change is needed in `VenueCatalog.cs`.
- With 35 avatars the city is 105 draws, inside the < 150 budget. Avatars are now the largest share: about 69 draws,
  and most of the 16 ms of CPU main.
- Spaces Studio had no earlier measurement.
- Evidence: `../ct-life-evidence/perf-venues-20261008/` (raw logcat per venue and phase, screenshots, crowd logs).

### 3. Release APK size
**148.4 MiB** (was 140.3; budget 150; `Builds/ctlife-local-release.apk`, env=local, built in the build clone).

Build-report diff of used assets, uncompressed:

| Change | Size |
|---|---|
| Repacked venues added | +55.8 MB |
| Old venue sources and candidates removed | about −29 MB |
| Unity splash removed (branding) | −2.7 MB |

The new venue weight breaks down as:

| Part | Size |
|---|---|
| Baked-lighting uv2 meshes (`*/Lighting/Meshes`) | 32.9 MB |
| Atlases | 21.2 MB |
| Lightmaps | 1.4 MB |

Headroom is now 1.6 MiB. [Inference] The largest lever is the uv2 lighting meshes, which are `.asset` meshes and so get
no importer mesh compression (venue lane, `VenueRepack.cs`).

The dev APK's file size is not a usable metric this round. It holds 171.9 MiB of entries in a 232.3 MiB file.
[Inference] The rest is dead space from Gradle's incremental packaging in the reused build clone.

### 4. EditMode suite
**793/793 passed** (7 assemblies, same `-assemblyNames` as last round; was 792/792). Results:
`../ct-life-evidence/perf-venues-20261008/editmode-results.xml`.

## 2026-10-08: Phase 5/6 wiring (dev 059d16f)

Both lanes' requests are wired (requests.md "2026-10-08 phase5-ventures" and "phase6-competition"):
- **App asmdef:** references `CTLife.Features.Ventures` and `CTLife.Features.Competition`.
- **PhoneCatalog:**
  - Ad Desk (`ads`, same id as the old tile), Builder (`builder`), VC Row (`vc`).
  - League (`league`), Beef, Farm, Cabal, Chat (`npcchat`), X link.
- **Raw room messages:** one forwarder, `Net/Realtime/RawRoomMessages.cs`, takes both lanes' types. The city room carries
  5 Phase 5 and 9 Phase 6 types; venue rooms carry `beef_update`/`beef_result`.
  - FlowController sends each message to `VenturesRealtime.Dispatch` first and gives the rest to `CompetitionRuntime.OnRaw`.
  - Each lane ignores types that aren't its own.
- **FlowController:**
  - `CompetitionRuntime.Bind`/`Unbind` and `CompetitionLinks.OpenApp` set and cleared with gameplay.
  - The pre-join gate in `Travel` (dubai_marina / cabal_lounge / sybil_barn); "null" means the room decides.
  - Join-rejection wording for the three gate codes, found anywhere in the join error chain. A refused join shows the
    reason and stays put instead of entering offline.
  - `kicked {sybil_jailed}` sends the player to the Sybil Barn instead of signing out.
  - `AdsChangedSignal` → `WorldLifeHub.RefreshAds()` (the WorldLife lane's two one-line methods, as requested).
  - `VenturesSpotLinks` added as an overlay.
- **ProfileCard:**
  - Players get Social + Competition (Beef, Cabal) + Ventures Hire. Hire copies the player id and opens Builder, because
    the lane has no prefill hook.
  - NPCs get "Talk" for the AI Agent and Sleuth (`ProfileCard.NpcActions`).
- **Settings:** `SettingsApp.ExtraSection` = `XLinkPanel.Build`.
- **Config:** `ConfigStore` merges `ad_rejection_reasons` and `project_events` when the server serves them.
- **Spots:** `builder_hills` set added as **fe.3**, additive, in `docs/fe-contract-proposals/ids/spots.json` and the client
  snapshot:
  - entrance/exit/center plus `builder_hills.project_board` and `builder_hills.hq_door` (stand).
  - [Proposal] Positions only: there is no Builder Hills scene yet, so the venue lane places the anchors.
- **Skipped as asked (stubs):** project HQ presenter, creative picker, city-map district flags, Ratio Arena board lines.

**Fix found by the run: "Not found." instead of "Not available yet".**
- This backend answers an unknown route with the contract body `404 NOT_FOUND "No route for this path and method."`.
  Both lanes counted only a 404 *without* that body as a missing route.
- `CompErrors` and `VErrors` now also treat that exact server message as unavailable. A real `NOT_FOUND` keeps its
  meaning. One test was added per lane; the lane runners pass 225/225 and 283/283.
- Backend ask #81.

**Editor integration.**
- `tools/integration/run_editor_integration.sh` gives 13/13 core checks; venue isolation is still blocked by #39.
- Phase 5/6 feature rows come from `FeatureProbe`: the lanes' APIs, with "n/a" meaning "not available yet".
- Every new app is opened through the real catalog and phone (`IntegrationRun.OpenApps`). All 9 open without errors, and
  the missing routes show "Not available yet" (0 "Not found.").
- Receipt: `../ct-life-evidence/integration/editor-20261008-070316/receipt.json`.

**Seeker smoke** (dev APK env=local, dev account): a new dev-only launch extra `app=<id>` opens a phone app after entering
the city (`seeker_launch.sh -e autoenter 1 -e app beef`).
- Results:
  - Ad Desk, Builder, VC Row, League, Beef, Farm and Cabal open; missing routes say "Not available yet".
  - Chat and X link weren't captured. A carrier USSD dialog (data-balance menu) appeared over the screen during the Chat
    run and is still on screen; I didn't touch it. The adb server was also restarted repeatedly; [Inference] Android
    Studio, which the user has open, is restarting it.
- Characters are still in T-pose (the feel fix isn't merged; ignored as asked).
- [Unverified] Each smoke launch logged an async exception stack before the app opened. Its message wasn't captured, and
  the editor runs log no error in these apps.
- Evidence: `../ct-life-evidence/phase56-20261008/app-*.png`.

**Backend:** items #77–#81 added to `backend-dev-feedback.md` (verified live; lanes' unprobed rows stay in their logs).

## 2026-10-08: Title screen (approved art direction)

`SignInScreen` is now a game title screen. Styles: `Resources/UI/title.uss`. Art: `Resources/UI/Title/`.
- **Background:** full-bleed `title-bg.png`, the approved native 1870×841 `title-bg-eggs-v2.png` (sha256 matches its
  manifest).
  - Cover-fit and centred, so 16:9 screens crop the sides.
  - It extends under the safe-area insets: the screen sets negative offsets equal to the safe-area layer's insets.
  - Import (`Editor/TitleArtImporter.cs`, applied on every import): no mipmaps, Android max 2048, ASTC 6x6; the button
    bodies get ASTC 4x4 with alpha.
  - Licence: `art/LICENSES.md` "Title screen".
- **Removed:** El.Brand, the eyebrow/title/body copy and the AvatarPreview portrait, plus their old `.ct-welcome*` /
  `.ct-signin` styles.
- **Button:** one big centred button, 38% of the width and 168 px tall, bottom edge at 9% (the open cobblestones under the
  group's feet).
  - Lilita One, cream `rgb(255,246,228)` with a 3 px dark-plum outline `rgb(58,20,64)` and a small shadow.
  - Press: scale 0.96 plus a darker tint over 80 ms, a haptic tick (Android `performHapticFeedback(VIRTUAL_KEY)`, which
    honours the phone's touch-feedback setting) and the tap sound.
  - Idle: a 5 px bob (2.6 s) and a shine sweep every 4.5 s.
  - Busy: "Opening your wallet…" and disabled. The existing enable rule is kept (`Features.Login` and `walletAvailable`).
- **Error line:** under the button, shown only when there is text (cream, outlined). If the wallet or login is
  unavailable, the existing messages appear there.
- **Legal line:** small, bottom centre, cream with a soft shadow.
- **Returning players.** If a valid session exists (stored, or resumed from the refresh token) the same screen shows the
  purple **PLAY** button, which continues into the game as before.
  - The small underlined "Use another wallet" link signs out (`IAuthService.SignOut`) and swaps to the sign-in button.
  - The automation extras (`autologin`, `autoenter`, `integration`, `app`) skip the PLAY gate in dev builds only, so the
    scripts and the editor integration still run unattended.
- **Ken Burns:** scale 1.00 → 1.04 over 25 s, ease in-out, ping-pong (USS transition).
  - The project has no reduced-motion setting. Motion (drift, bob, shine) is off when Android's "Remove animations" is on
    (`ANIMATOR_DURATION_SCALE` = 0, `DeviceFeel.ReducedMotion`).
- **Buttons are placeholders:** `btn-green-blank.png` / `btn-purple-blank.png` were not in `art-direction/out/` yet.
  - The current bodies are styled in USS (green/purple pill, plum outline, thicker bottom edge).
  - When the files are copied to `Resources/UI/Title/`, `ApplyButtonArt` uses them automatically, 9-sliced, with the
    label still drawn by Unity.
- **Dev-only evidence extra:** `shot=<name>` saves the app's own frame to `files/<name>.png`, so system dialogs don't
  appear in it. Pull it with `adb exec-out run-as com.ctlife.app cat files/<name>.png`.

**Release APK: 149.5 MiB** (budget 150; was 148.4). The background is 0.69 MB in the build. [Inference] The rest is the
Phase 5/6 code merged since then. **Headroom is 0.5 MiB:** the painted buttons at ASTC 4x4 may not fit. Two ASTC 4x4
bodies around 900×260 would be about 0.47 MB.

**EditMode:** **1301/1301** passed (9 assemblies: the 7 from before plus Ventures and Competition). The first run was 1300/1301: `WriteFlowTests.Success_RetiresTheKey` (Ventures lane) kept its key counter across tests because NUnit reuses the fixture instance; `Make()` now resets it. That was a test-only fix. Results: `../ct-life-evidence/title-screen/editmode-results-2.xml`.

**Seeker screenshots: blocked.**
- An "MTN - NG Message: Connection problem or invalid MMI code [OK]" carrier dialog holds window focus. Unity on Android
  waits for focus before it loads the first scene (its log shows `HasWindow = 1, HasFocus = 0`), so the app never draws
  the title.
- Not touched, as instructed. Once it is dismissed:
  - (a) Fresh: `seeker_launch.sh -e fresh 1 -e shot title-signin` → `files/title-signin.png`
  - Then `seeker_launch.sh -e autologin dev` to sign the dev account back in.
  - (b) Returning: `seeker_launch.sh -e shot title-play` → `files/title-play.png`
- Target: `../ct-life-evidence/title-screen/`.

**Buttons (2026-10-08, second pass).**
- **Art:** the painted blanks `btn-green-blank.png` / `btn-purple-blank.png` (600×130, alpha) are imported as ASTC 4x4 with
  no mipmaps (`TitleArtImporter`). They are 77.5 KB each in the build.
- **9-slice:** the top and bottom slices together cover the whole height (64 + 64 of 130) and the caps are 64 wide. The
  slice scale follows the button height, so the rounded ends stay round at any width and only the flat middle stretches.
  Simulated at 700, 862 and 1000 px wide: ends round. That is a model of the slice math, not a Unity render.
- **Placement:** 37% wide × 186 px, bottom at 7.5%. The top edge is at ~75% of the height, clearing the group's feet
  (~72%, ~73% at the Ken Burns peak); the centre is at ~84%.
- **Label:** Lilita One 66 px (PLAY 84 px), cream with a 4 px plum outline and a solid plum drop shadow, as on the painted
  references. It is lifted 14 px off the thicker bottom rim.
- **Release APK: 149.56 MiB** (≤ 150, 0.44 MiB headroom).
- **Seeker:** the dev APK with the buttons is installed. The screenshots were **not taken**: I polled `mCurrentFocus`
  every 20 s for 30 min and the "MTN - NG Message" dialog still holds focus (no touch on the phone for 53 min). Unity does
  not start without window focus. The three commands in the section above are ready to run.

**Polish (2026-10-08, third pass), Seeker captures replaced.**
- Files: `../ct-life-evidence/title-screen/title-signin-device.png` and `title-play-device.png` (adb screencap).
- **Button:** 36% × 150 units (1.1 px per unit on the Seeker), bottom at 9.1%, label 60 (PLAY 76).
  - Its top edge sits at ~926 px, below the group's shoes, which reach 905–915 px across the Ken Burns drift. The shoes go
    lower than the ~72% estimate.
- **Spacing:** "Use another wallet" sits 9 px under the button. The legal line is pinned 8 units above the bottom of the
  safe area, with ~25 px of air between the two.
- **Legal line:** 24 units, solid cream, Manrope Semibold, 1.5 px dark-plum outline and shadow.
- **Scrim:** a full-bleed scrim over the bottom 10%, a transparent → dark-plum ramp texture made at runtime (no build
  cost). Bottom rows measure ~75–95 brightness against ~140 above.
- **`shot` option:** it works. The file lands in `/sdcard/Android/data/com.ctlife.app/files/` (Unity's persistentDataPath
  on this build), not the internal `files/` I had pulled from.

## 2026-10-08: Device round on dev 9f23592 (T-pose fix, character feel, venues round 4)

Seeker, dev APK env=local, dev account. Evidence: `../ct-life-evidence/device-round-20261008b/`. The earlier pass on
b4eaa8b is archived in `prev-b4eaa8b/`.

**Two blocking bugs found on device and fixed (client core).**
1. **No thumb movement at all on device.**
   - The Social lane's always-present `ct-social-chip-bar` (a click-through container, `pickingMode = Ignore`, on the
     modal layer) made `MovementControls.HasOverlay` treat the modal layer as an open overlay. The stick was hidden and
     movement was blocked in every venue. Earlier device passes only used automation, so they never showed it.
   - Fix: only a child that takes input (`pickingMode = Position`: sheets, cards, backdrops, phone, screens) blocks
     movement. Real overlays all set Position or keep the default.
   - The stick now shows ("Walk" / "Run") and moves the player.
2. **Sit stood the player straight back up** (~1.5 s; recording `b-sit-attempt.mp4`).
   - `RequestSeat` sent `sit` but kept sending `move` updates while the claim was in flight. The venue room applies them
     in order, and its move handler stands a seated player up ("If seated, stand up", `venue.ts`).
   - Fix (`LocalPlayerController.SendSit`): flush the last position first, then send no `move` while a claim is pending.
     Stick input already cancels a claim, so moves resume as soon as the player moves.
   - The cause is confirmed by the fix's result: the seat now holds. The server log was not readable (the
     `realtime.log` file was deleted while still open).

**(a) T-pose: none.** The player, server NPCs and the 30-bot crowd stand in natural poses in Timeline Square v3, Terminal
Café and Spaces Studio v4 (`venue-*.png`). In the pre-fix build the player and NPCs were in T-pose
(`../ct-life-evidence/perf-venues-20261008/*.png`).

**(b) CTA flow.**
- Café: walking up to a table offers **Sit** (yellow pill + floor ring) → tap → walks to the approach and sits →
  **Stand up** → stands, and "Sit" is offered again.
  - Files: `b-1-cta-sit-offered.png`, `b-2-seated.png`, `b-3-stood-up.png`, `b-sit-stand.mp4`.
- **Spaces Studio "Take the mic": not verified on device.** The host spot (`spaces_studio.stage_host`, 0,8) is 17 m from
  the entrance, behind the amphitheatre tiers. The stick took the player onto the first tier only. Taps on the
  stage/desk did not focus it (a tap must land within 0.9 m of the spot). Taps on the café chairs did not focus a seat
  either, so the venue lane should check that the seat/stage anchors line up with the visible furniture in round 4. A
  dev hook to focus a spot by id would make this testable.
- Note: at spawn the café NPC "Wallet Tracker" and the Timeline "Town Crier"/"Spaces Host (alt)" name tags overlap.

**(c) Sprint into a prop: pass.** Full stick ("Run") into a café table: trip at ~2.0 s, ragdoll on the floor 2.3–3.7 s,
get-up 4.0–4.7 s, control back at ~5 s. Files: `c-1-running.png`, `c-2-knocked-down.png`, `c-3-getting-up.png`,
`c-4-back-up.png`, `c-sprint-knockdown.mp4`.

**(d) Look on device (interim; Timeline Square round 5 is in progress).** Files: `venue-timeline_square.png` (v3 with
the crowd), `venue-terminal_cafe.png`, `venue-spaces_studio.png` (v4 amphitheatre, stage, city-view terrace).

**(e) Perf.** Normal = the Seeker's own thermal state, where the game caps at 30. Lifted = `cmd thermalservice
override-status 0`, restored after; only windows at the 60 target are counted.

| Venue | avatars | phase | fps (median, min) | p99 ms | draws | triangles | CPU main ms | GPU ms |
|---|---|---|---|---|---|---|---|---|
| Timeline Square v3, 30-bot crowd | 35 | normal | 30.2 (30.2) | 33.3 | 85 | 213k | 15.0 | 22.7 |
| same | 35 | lifted | **60.3 (57.9)** | 16.6 | 106 | 204k | 11.3 | 14.4 |
| Terminal Café | 2 | normal / lifted | 30.2 / **60.3 (59.1)** | 16.6 | 54 | 80k | 10.8 | 14.0 |
| Spaces Studio v4 | 1 | normal / lifted | 30.2 / **60.1 (59.7)** | 16.6 | 50 | 96k | 8.2 | 14.1 |

- **Compared with this morning:**
  - Timeline crowd: 253 → ~85–106 draws at the same triangle count.
  - Café: 35 → 54 draws, GPU 11.6 → 14.0 ms (look-dev with post-processing on).
  - Studio: 25 → 50 draws, 30k → 96k triangles.
- All hold 60 fps with the cap lifted, with GPU at 14–15 ms of the 16.6 ms budget.
- The adb server was restarted ~5 times during the run (Android Studio is open). `adb_reverse_watchdog.sh` re-applied the
  port reverses; one Timeline lifted window was lost and re-measured.

**(f) Release APK: 141.0 MiB** (was 149.56 before venues round 3's mesh compression; round 4 is the same size).

**EditMode: 1333/1333** (9 assemblies). `editmode-results.xml`.

**Still open:**
- [Unverified] Every launch logs an async exception stack (`Task.TrySetException`) without its message.
- The venue isolation backend item #39 is still open; the café got its own room this time only because no other venue
  room was open.

## 2026-10-08: Name tags, launch "exception", tap focus (device-round-20261008c)

Evidence: `../ct-life-evidence/device-round-20261008c/`.

**3. Tap focus: the real cause was in the client, three layers deep.**
1. **Every world tap was swallowed.** `WorldInputLayer.IsBlocked` had its own copy of the overlay check that blocked the
   stick. The Social lane's always-present click-through `ct-social-chip-bar` on the modal layer counted as an open
   overlay, so `WorldInput.Tap` never ran. Seats, the stage and avatars (profile cards) could not be tapped on device.
   - Found with dev logging: the press reached `#layer-world`, but no tap followed.
   - Fix: same rule as the stick; only a child that takes input (`pickingMode = Position`) blocks.
2. **The tap → spot rule predated solid furniture.** Since venues round 4 a tap lands on the chair/sofa/table/desk
   collider, and the old rule (first hit only, ≤ 0.9 m in 3D from the floor anchor) rarely matched.
   - New `WorldInput.ResolveSpotTap`, shared with the editor audit. In order:
     - an explicit tap target (a collider under the anchor) within 1 m behind the first hit;
     - the anchor the touched object belongs to;
     - the anchor horizontally nearest the touched point (0.75 m, standing spots such as the host's mic 1.3 m);
     - the anchors a touched furniture-sized collider covers.
   - Editor audit `Editor/SpotTapAudit.cs`: each venue loaded like the game, taps aimed from the venue's own camera
     yaw/pitch/distance. From the default camera yaw:

     | Venue | Old rule | New rule |
     |---|---|---|
     | Café | 0/12 | **12/12** |
     | Spaces Studio | 19/33 | **33/33** |
     | Timeline Square | 3/6 | **6/6** |

   - **No anchor is off its furniture** (report `spot-tap-audit.md`), so there is no anchor list for the venue lane.
3. **A tapped target was dropped beyond 14 m.** The Spaces host spot is ~17 m from the entrance. `InteractionFinder.FocusRange`
   is now 24 m, enough for what the venue camera shows.

**Device:**
- **Café:** one tap on a chair from the spawn point focuses it (`tap → table_3`, `focus ok`, ring on the chair, "Sit"),
  then "Sit" walks the player ~9 m there and seats them. Files: `3-cafe-seat-tapped.png`, `3-cafe-walked-and-seated.png`.
- **Spaces:** one tap on the host desk from the entrance gives **"Take the mic"**. Tapping it walks the player around the
  tiers to the desk and holds the mic pose, with "Step away" / "Wave" / "Hype the room". Files:
  `3-studio-mic-offered.png`, `3-studio-on-the-mic.png`, `3-studio-take-the-mic.mp4`.
- New venue finding: on the mic the stage lighting truss blocks the camera (requests.md).
- Dev builds now log one line per world tap (`CTLIFE [input] tap … -> spot`) and per focus.

**2. Launch "exception": there was no exception.**
- The `Task.TrySetException` / `AsyncTaskMethodBuilder.SetException` lines were the script stack trace that Unity
  attaches to every log line in dev builds. They belonged to one ordinary warning, `CTLIFE [citylife] GET /homes failed:
  NOT_FOUND` (backend #75). The awaited request threw a 404 that CityLife caught and logged, so the async frames came
  through `TrySetException`.
- Fix (`GameBootstrap.Boot`): `Application.SetStackTraceLogType(Log/Warning, None)`; errors, asserts and exceptions keep
  their stacks.
- Device: launch log 687 → 338 lines; 0 `TrySetException` lines. The only warning left is that one-line 404, a backend
  gap. Files: `launch-unity-full.log` (before), `launch-unity-after.log`.

**1. Name tags** (`UI/Overlays/TagPlanner.cs`, pure and unit-tested; `WorldLabels` renders it).
- **Who gets a tag:**
  - never your own;
  - always: speakers, friends (people you follow, via `SocialHub.Follows`) and NPCs within 10 m in your 50° facing cone;
  - then the 6 nearest, within 16 m and on screen.
- **Collisions:** a tag pushed up one row if it collides; otherwise the lower priority is hidden.
- **The centre of the screen stays clear:** an ordinary tag there is hidden; a priority tag is pushed sideways.
- **Fade:** alpha falls from 1 at 6 m to 0.4 at 16 m (×0.9 when pushed).
- **Style:** smaller rounded pills (19 px); NPCs show just the name with a tiny gold star (`UiIcon("npc")`).
- **Tests:** 7 in `Tests/EditMode/TagPlannerTests.cs`. They cover own tag hidden, nearest 6 plus speaker, friend and faced
  NPC, range/off-screen, collision push/hide, centre clear, fade, and priority.
- **Device** (30-bot crowd): `1-tags-before-after.png`. Before: ~30 overlapping labels. After: a handful of small pills,
  nothing over your character.

**EditMode: 1340/1340.** Compile clean on all three passes.

**HUD chips (2026-10-08).**
- The presence row ("1 in the city" + "Market ±x%") had `flex-wrap: wrap`. When the two chips were wider than the venue
  title, the Market chip wrapped to a second line. The Explore/Lobby dock sits at a fixed top (176 units in landscape), so
  that second line was underneath it ("Ma… +1.9%" peeking out).
- Fix (`city-hud.uss`): the row no longer wraps and the chips don't shrink. The location block now ends at ~150 units,
  above the dock. No other styling changed.
- Seeker, all three venues: `../ct-life-evidence/device-round-20261008c/4-hud-chips-three-venues.png`.

## 2026-10-08: Device round d (venues rounds 5–7, character feel round 2, cast re-import): checkpoint

Evidence: `../ct-life-evidence/device-round-20261008d/` (`r5/` = round 5 on aa1685a; `r6-*` = round 6 on f3f4fce + the cast
re-import).

**Cast re-import (658cb8b export), and the weight patch removed.**
- `CastImport.RunBatch` copied 109 files; 75 looks, 50 archetypes, 22 creator looks, 0 problems.
- `CastWeightRepair` still "repaired" 41 meshes, every Rain-family look: 290–872 vertices each.
- Blender on the export finds **0** vertices with both Head and pelvis/leg weight in all 41 files, using the real bone
  names. The cause is that **Hips and Spine share a position** in the export rig. The patch names bones by bind-pose
  position, so it labelled Spine as Hips and stripped legitimate Head weight from spine-following vertices (long hair,
  collars).
- The patch is removed. The validator stays as a test: `SeatFitTests.CastFFamilyShoesCarryNoHeadWeight` (no Head
  weight below 0.6 m, real bone names) now covers **every** cast look, and it passes.
- EditMode **1360/1360**. Release APK with round 6 + cast: **143.6 MiB**.

**New dev launch options** (dev builds only):
- `spot=<id>` focuses a seat/terminal/mic and presses its CTA; `stand=<s>` stands up after that many seconds.
- `look=<cast look>` picks the creator look for `autocreate`.

**Device results:**

| Check | Round 5 (aa1685a) | Round 6 (f3f4fce) |
|---|---|---|
| Café look | v2 to the concept (`r5/1-cafe-v2-look.png`) | real furniture (`r6-cafe-*`) |
| Café table sit / stand | pass; fit on chair | pass; bentwood chair fit good |
| Café terminal sit | pass; hands on laptop | pass; hands at keyboard |
| Spaces bean bag | pass (sinks in) | pass |
| Spaces bar / pod stool | first tries failed, retries pass | pass |
| Spaces stage armchair | – | pass |
| Spaces green-room sofa | **server `SPOT_NOT_FOUND`** (backend spots lack `greenroom_*`) | local engage only |
| Spaces mic | pass, truss gone from the view | pass ("Step away") |
| Timeline bench | pass | – |
| Timeline crowd of 35 | **60.2 / 60.3 fps** (normal / cap lifted); 87 draws; 217k tris; CPU 11.4–11.9 ms; GPU 12.4–14.5 ms | pending |
| Release APK | 140.6 MiB | 143.6 MiB |

- **Round 5 stool failures:** "Can't get there from here" once and "The room didn't answer" once.
- **Round 6 runs were offline:** realtime was deliberately pointed at a dead port (see below), so seats engaged locally
  without a server echo.
- **adb is restarted by something outside this session every ~30 s.** The server pid changes; [Inference] it is Android
  Studio, which has been open for 1.5 h. Each restart drops the port reverses and the app's realtime connection. That
  explains the intermittent seat failures, and it makes any check that needs realtime (server seat echo, the crowd)
  unreliable until it stops.
- **Female walk:** inconclusive. At spawn the player was boxed in by bodies standing on the same entrance spot.
- **After Stand up (round 5):** the player plays an arms-up stretch while stepping. Possibly an idle variation; check
  that it is intended.
- **Pending (paused for the backend reconciliation):** café v3 (round 7, 82c380a; build ready), sprint trip vs
  knockdown, run stop, female dress walk/sit on device (`look=player_f_kol`), crowd on rounds 6/7.
- **NPC patrons:** B17/B10 go to `backend-dev-feedback.md` in the "after 7243073" section. The client-side "seat the
  NPC at its spot" presentation rule (world-life `npc_presentation.json` + `NpcBeatInterpreter`) is designed but not
  started.
- Note: I ran `git rm --cached` and then a path-limited `git reset` on `CastWeightRepair.cs` to undo it. The index is
  back to HEAD and the file is deleted in the working tree only.

## 2026-10-08: Reconcile with backend main 7243073

### Step 1: backend on 7243073 (done)

- `../ct-life-backend`: our local `auth.ts` patch discarded (B0 is upstream), detached at `origin/main` 7243073,
  `pnpm install --frozen-lockfile`, migrations through `1700000070000_add_lottery_rolled`, `.env` extended with the
  new `.env.example` keys (local section above; backup of the old `.env` in `~/.ctlife-local/`).
- Workers start and stay up, so `up.sh` starts them by default (`CTLIFE_WORKERS=0` opts out). `down.sh` no longer stops
  the shared containers unless asked (`--containers`).
- The tsconfig override is removed; players and NPCs sync without it (observer + 3 bots: 3 players moving and waving,
  5 NPCs; `../ct-life-evidence/backend-7243073/bot2.json`).
- **Newly broken, patched locally:** every `venue` join failed with `column c.name does not exist` (feedback item 83).
  `tools/backend-local/patches/0001-venue-profile.patch`, applied by `up.sh`.
- **Newly broken:** `POST /v1/characters` rejects every look, including ids in `items.json` (item 84). No new player can
  finish onboarding against this backend; bots join as `Anon`. Patched locally in step 3 so the rest could be tested.
- **SIWS identity, one value (user decision):** `EnvConfig.AppIdentityUri` (`https://ctlife.invalid/`, no `.dev`) is the
  MWA identity in every env; the four env assets leave `walletIdentityUri` empty. `EnvConfig.SiwsDomain` is its authority.
  `AuthService` warns when a challenge's `domain` differs. `up.sh` starts the api with `SIWS_DOMAIN`/`SIWS_URI` read from
  that line. Live: `GET /v1/auth/challenge` → `domain "ctlife.invalid"`, `uri "https://ctlife.invalid/"`, nonce 32
  alphanumeric. Staging/prod REST URLs were `*.ctlife.dev`; they are now `.invalid` placeholders too (not hosted, no
  domain), which the build's placeholder check already refuses for shipping. Feedback item 82 asks Iyke for the same
  placeholder default.
- Checks: compile_check dev / release / editor all compile (the one editor warning is the venue lane's
  `VenueVerify.cs:58` unused `nav`); EditMode **1361/1361** (new `NetPolicyTests.IdentityAndSiwsDomainComeFromOneValue`).

### Step 2: contract re-sync + codegen (done)

- `contract/` re-synced from 7243073 per `contract/SOURCE.md` (SOURCE.md restored and updated). Only two files changed:
  `ids/venues.json` (B27 sync with spots: +`airdrop_fields`, `club_liquidity`, `conference_hall`, `ct_jail`,
  `hackathon_house`, `home`, `sybil_barn`, `the_trenches`; −`airdrop_farm`, `builder_hills`, which are districts) and
  `realtime/states.ts` (CoinState `priceMicros`, `mcapCents`, `vol5mCents`, `launchedAt`: `int64` → `bigint64`).
  `openapi.yaml` is unchanged, so the REST codegen was not rerun (nothing to regenerate).
- `python3 client/Tools~/sync_contract.py` → `config_snapshot.json` (19 registries, 14 config files).
- `client/Tools~/codegen_realtime.sh` → only `CoinState.cs` changed. schema-codegen 5.0.36 writes C# type `undefined`
  for `bigint64`; the script now maps it to `long` (`biguint64` → `ulong`) and fails if any `undefined` remains.
- **Unity SDK gap, patched in our vendored copy:** Colyseus Unity SDK 0.18.8 (latest release, 2026-09-23; our
  `Packages/io.colyseus.sdk/.../Utils/Decode.cs` is identical to upstream HEAD) has no `bigint64` case:
  `DecodePrimitiveType` returns null and reads nothing, so every field after a coin's `priceMicros` would be misread
  in the market room (we decode every room with `DynamicSchema`, which takes the server's type names). The wire format
  is the int64 one (`@colyseus/schema` 5.0.36 `encode.ts`: two little-endian int32), so the patch reads
  `bigint64`/`biguint64` with the existing int64/uint64 decoders. `StateMapper.Long` now keeps integral values exact
  instead of going through double. Test: `StateMapperTests.Bigint64FieldsDecodeAndKeepTheStreamAligned`.
  Live market-room check: step 4.
- Checks: compile_check dev / release / editor compile (editor warning: venue lane's `VenueVerify.cs:58`);
  EditMode **1362/1362**.

### Step 3: client workarounds (verified live, then removed or kept)

Evidence: `../ct-life-evidence/backend-7243073/` (`siws-local-roundtrip.txt`, `room-*.json`, `room-state-shapes.json`,
`join-profile-from-db*.json`, `me-auth-shapes.txt`).

| Workaround | Live check on 7243073 | Result |
| --- | --- | --- |
| SIWS nonce/uri adjustment (`SiwsWalletCompat`) | challenge nonce = 32 alphanumeric, `uri` absolute (`https://ctlife.invalid/`) | **removed**; the wallet signs the server's challenge as issued. Seed Vault check: device round (fingerprint) |
| base64 → UTF-8 verify retry (`SiwsEncoding`) | local ed25519 round trip: base64 `signedMessage` → 200; UTF-8 text → 401 | **removed** (the retry could only fail now) |
| Venue wrong-room guard (`RequireRequestedVenue`, `VenueRoomCheck.Matches`) | bots: two café joins share one room, Timeline Square gets another; editor `venue_isolation` pass | **removed**; `FeatureProbe` `venues.rooms` and `IntegrationRun` `venue_isolation` now test the split |
| Profile fields in join options (`ExtraJoinOptions`, item 38) | venue join with a spoofed name/look: the room shows the DB alias and look | **removed** for venue/city/market. **Kept** for the `home` room join (no home room on the server to check) |
| Tolerant room parsing (`DynamicSchema` + `StateMapper`) | venue, city, market states match `states.ts` field for field | 9f3e5ef alias (`archetype`) **removed**. Name-based decoding **kept**: the contract allows additive schema fields, and an installed app cannot regenerate typed classes; a typed schema stops decoding on any mismatch |
| `/homes` errors | `GET /homes` 200 | nothing to remove: errors keep the registry rows (generic error handling, not a 404 special case). Rent/visit still 404 (item 89) |
| "No route" handling (`RouteMissing`, "Not available yet") | Phase 5/6 routes still 404 | **kept**: honest empty states, never fake data |
| `MeCompat` (`user.hasCharacter`), flat auth parsing, `{id, level}` apply | still the 9f3e5ef shapes (`flags.hasCharacter`, flat auth, no user on refresh) | **kept** (items 45, 48, 49 still open) |
| Creator-look fallback for remote players without a look | upstream rooms still send `["0",…]` (item 83) | **kept** until upstream maps the look |
| `livekitToken` dev launch extra (pre-minted LiveKit token) | `/rtc/token` exists but rejects the contract body upstream (item 88) | **kept** (dev builds only) until item 88 is fixed upstream |

Also: `StateMapper.Long` keeps 64-bit integers exact; `run_editor_integration.sh` now makes the dev wallet a real ed25519
key (random 32 bytes are usually off-curve, and the ORE stake builder 500s on those).

Local backend patches added in this step (reported in feedback section G): `0002-character-look-validation`,
`0003-config-emotes`, `0004-rtc-token-contract-id`; `0001-venue-profile` now also maps the stored look object.

### Step 4a: editor integration on 7243073 (+ local patches 0001-0004)

`tools/integration/run_editor_integration.sh` → **13/13 core checks pass**
(`../ct-life-evidence/backend-7243073/editor-20261008-153125/receipt.json`). Without patch 0002 the run stops at
`lottery_then_character` (`POST /characters → 400 invalid_look_item`, `editor-20261008-151018`).

Feature table, main @ 7243073 (editor). "blocked #N" = backend gap, item N in `backend-dev-feedback.md`.

| Feature | 9f3e5ef | 7243073 (editor) |
| --- | --- | --- |
| Sign-in: dev auth | pass | pass |
| Onboarding: lottery → creator | pass | **fail upstream #84** (every look rejected); pass with local patch 0002 |
| City + market rooms | pass | pass (market `coins` always empty, #89) |
| Timeline Square presence, NPCs, chat, seen by bot | pass | pass (5 NPCs; profile from the DB) |
| Shift pays Cash; block/report/unblock; token refresh + rejoin | pass | pass |
| Venue rooms other than Timeline Square | blocked #39 | **pass** (own room per venue) |
| All 21 venues + own home load offline | pass | pass (21/21) |
| Money: top-up intent | blocked #52 (no tx) | **fail #87** (500 `Non-base58 character`) |
| Money: spot buy | blocked #76 | **pass** |
| Money: perp open + close | pass | pass |
| Money: ORE stake tx | blocked #55 (500) | **pass** (v0 tx built, not signed; needs a working devnet RPC) |
| City life: needs, shop | pass | pass |
| City life: homes list | blocked #40 | **pass** |
| City life: rent / visit | blocked #75 | blocked #89/#75 (`POST /homes/studio/rent` 404, `POST /homes/{ownerId}/visit` 404) |
| Careers: list + gig | pass | pass |
| Social: DM, feed post | pass | pass |
| Social: Space audio token | blocked #40 (no route) | **fail upstream #88** (400, body field `roomId` vs contract `id`); pass with local patch 0004 |
| Social: call ring | blocked #54/#68 | blocked #54 (call starts, only `callId` returned; city room never sends `call_invite`) |
| World life: city events | blocked #64 | blocked #64 (city room still a stub) |
| World life: live ads | pass | pass (no bookings) |
| Ventures: ad slots / bookings | pass | pass (4 slots, 0 bookings) |
| Ventures: project create | blocked #77 | blocked #77 (400 `insufficient cash for project seed`) |
| Ventures: projects mine / hire offers / VC deals | n/a #78 | n/a #78 (404 No route) |
| Competition: tribes, leagues, my league, beef open, Elo, cabal invites, X link | pass | pass (0 districts, #79) |
| Competition: beef list, farm, sybil status, venue access, cabal, LLM quota | n/a #79 | n/a #79 (404 No route) |
| `GET /config` | 200 | **fail upstream #86** (always 500; client uses its bundled snapshot); 200 with local patch 0003 |

### Step 4b: Seeker round on 7243073 (+ local patches; dev APK from the build clone, 217.4 MiB)

Evidence: `../ct-life-evidence/backend-7243073/device/`.

| Check | Result |
| --- | --- |
| Fresh dev account → lottery → creator (`player_f_kol`, dress) | first try **fail upstream #84**: `POST /characters → 400` (`invalid_skin_tone`, `skin_tan`); pass after extending patch 0002 (`a-kol-cafe-log.txt`) |
| `GET /config` | 200 (patch 0003), later 304 |
| City, market, Terminal Café rooms | pass; café gets its own room, the observer bot in the same room sees the Seeker player walk (`a-kol-cafe-bot.json`) |
| Café v3 seat online (`terminal_2`, laptop table) | **pass**: walks over, sits with hands on the laptop, "Stand up" CTA, stands on cue (`b-terminal2-online.mp4`, `b-t2-strip.png`, `b-t2-zoom.png`); female dress look sits cleanly |
| Proximity voice (real `POST /rtc/token`, no dev token) | upstream **fail #88** (400 body, then undecodable `expiresAt` → "Voice unavailable: ApiException"); with patch 0004: **pass**, LiveKit room `venue:<instanceId>`, active speaker level 0.125. Mic turned off again afterwards |
| Real SIWS via Seed Vault (fingerprint) | **pass** 15:53 (user): challenge for `ctlife.invalid`, signed as issued (no nonce/uri adjustment), `POST /auth/verify` base64 → 200 on the first try (`e-siws-trail.txt`). `seeker=False` is expected locally (no Helius key, so no SGT lookup) |
| Returning player after SIWS | **fail upstream #92**: the existing player (character from 9f3e5ef days) landed on "Your origin story": the new `lottery_rolled_at` column has no backfill, so `/me` says `lotteryRolled:false` with a character, and a roll is a 500. Client fixed: a character now always means rolled (`Onboarding.LotteryRolled`, test `CharacterBeatsAStaleFalseFlag`) |
| Dev-build error overlay after the wallet returns | Solana Unity SDK logs `[MWA] ReconnectWallet failed: OperationInFlightException … 'Login' cannot start` from its own `HandleApplicationFocus` while the sign-in is still in flight; the dev build shows it on screen. Sign-in still completes. [Inference] harmless (release builds show no log overlay); not changed |

## 2026-10-08: Title screen v4 (squircle slicing, outlined bottom copy, high-res background)

Evidence: `../ct-life-evidence/title-screen/v4-play.png`, `v4-signin.png` (Seeker screencaps, dev APK from the build
clone), `v4-*-bottom.png` crops. `v3-*` are the intermediate plate version, which the user rejected.

**1. Squircle buttons (green body in; purple still the pill).** `SignInScreen.ApplyButtonArt` loads
`Resources/UI/Title/btn-<tone>-blank-sq.png` when it exists and falls back to the current pill. The squircle is 9-sliced
with side caps of `SquircleSideSlice` = 0.30 of the texture height (39 px of 130: the ~22% corner, 28.6 px, plus its
antialiasing and rim). The top and bottom slices cover the whole height, and the slice scale follows the button height,
so nothing stretches vertically and the corners keep their painted shape at any width. `TitleButtonArtTests` decodes each
delivered `-sq` PNG, measures where its outline stops curving and fails if that reaches past the cap.
- `btn-green-blank-sq.png` arrived at 16:15 (600×130, body 119 px tall, 5 px margins). Its painted corner ends 36 px in,
  inside the 39 px cap: the test passes. It is in `Resources/UI/Title/`, with the same ASTC 4x4 import as before, and
  `btn-green-blank.png` is deleted. "Sign in with Solana" uses it.
- `btn-purple-blank-sq.png` is not delivered yet, so PLAY keeps the purple pill and its test case is Ignored. When it
  lands: copy it in, delete `btn-purple-blank.png` (+ .meta), run the test, rebuild.
- The USS placeholder radius is now 33 px (22% of 150).

**2. Bottom copy readable over the art (user correction: no plate, no gradient).** The first pass used dark-plum plates;
the user asked for mobile-game text instead, so the plates are gone, and so is the old bottom gradient band (`ScrimTexture`
and `.ct-title-scrim` removed). The copy now carries itself:
- Font and fill: Lilita One (the button font), solid cream fill.
- Outline and shadow: dark-plum outline 3.5 units on the link and 3 on the legal line and errors, which is 3.9 px and 3.3 px
  on the Seeker (1.112 px per unit). A soft dark drop shadow (`0 3px 5px`, 85%) under each.
- "Use another wallet": 28 → 36 px (+30%), underlined. **Placement chosen: beside PLAY**, centred on the button body, with a transparent
  hit area of min 130 × 130 units = 144 px = 48 dp on the Seeker (480 dpi), so it never overlaps PLAY or the legal line.
  Pressed state scales to 0.94.
- Legal line: 24 → 31 px, slightly smaller than the link.
- Errors under the button use the same style.

The device captures (`v4-play.png`, `v4-signin.png`, zooms `v3-legal-zoom.png`, `v3-link-zoom.png` from the same text
build) show the outline and shadow separating the text from the light cobblestones. There is no plate, so the old plate contrast numbers no longer
apply. Cream fill against the plum outline is ~14:1.

**3. High-res background in.** `title-bg-eggs-v2-3840.png` (3840×1727) is a Photoshop "Preserve Details" upscale of the
approved v2, with no content change. It replaces `Resources/UI/Title/title-bg.png`.
- Import: ASTC 6x6, no mips, max size 4096 on Default, Standalone and Android, so it keeps its full 3840 width.
  `TitleArtImporter` now sets 4096 for the background (buttons stay at 2048), and the .meta says the same.
- Size: ASTC 6x6 is 16 bytes per 6×6 block, so 640 × 288 blocks = 2.95 MB, against 0.70 MB for the old 1870×841.
- `art/LICENSES.md` updated with the new size, sha256, upscale provenance, and the green squircle body.
- Release APK (local env, build clone, round 7 + reconcile + this): **146.2 MiB** (153,251,861 bytes), within the
  150 MiB budget. Round 7 was 144.2 MiB. The build report packs `title-bg.png` at 2.81 MiB (full 3840 ASTC 6x6). Dev APK:
  231.6 MiB.
- On the device (`v4-signin-detail.png`, a 1:1 crop of the screencap) the faces, fountain and roofs are sharp at native
  2670×1200.

Checks: compile_check dev / release compile. EditMode 1360 passed + 2 ignored of 1362 before the green body arrived.
After it: `TitleButtonArtTests` green passes (corner 36 px ≤ cap 39 px) and purple is still ignored. No code changed
after that full run.

## 2026-10-08: Title screen v5 (purple squircle, larger link, shine clipped)

Evidence: `../ct-life-evidence/title-screen/v5-signin.png`, `v5-play.png` (Seeker screencaps), `v5-*-bottom.png` crops,
`v5-signin-shine.mp4` + `v5-shine-corner-frames.png` (the sweep at the right end), `editmode-v5.xml`.

- **Purple squircle in.** `btn-purple-blank-sq.png` (16:22) is copied to `Resources/UI/Title/` with the same import as
  green (ASTC 4x4). `btn-purple-blank.png` + .meta are deleted, so both buttons are squircles and no pill art is left.
  `TitleButtonArtTests`: both pass (green corner 36 px, purple 31 px, side cap 39 px). `art/LICENSES.md` lists both bodies.
- **"Use another wallet" beside PLAY:** 36 → 41 px (+15%). Its outline is now 3 units, the same as the legal line (it was
  3.5). The 48 dp hit area is unchanged.
- **Shine fixed for the squircle.** On v5's first capture the button shine swept as a rectangle past the squircle's
  transparent corners and margins: a light wedge over the cobblestones at the right end of PLAY
  (`v5-play-shine-corner.png`). The shine now sits inside `.ct-title-shine-clip`, a child inset to the painted body
  (6/6/6/8 units, radius 30) with `overflow: hidden`. The recording shows it clipped to the rounded body at the right end.
- The dev MWA error overlay does not appear on either variant (no wallet operation runs on the title screen).

Checks: compile_check dev compiles; `TitleButtonArtTests` 2/2 pass; EditMode 1362/1362.

## 2026-10-08: Title screen v6 (button slot lower)

User tweak: the main button (and PLAY, which shares its slot, plus "Use another wallet" beside it) sits ~4% of the screen
height lower, fully on the open cobblestones. `.ct-title-actions` bottom: 9.1% → 5.4% (3.7%). At exactly 4% the gap to the
legal line dropped to 15 px at the low point of the button's idle bob. Measured on the Seeker across the bob (8 captures):
the rim-to-legal-text gap is 19-30 px (≥ 16), and the button top sits ~55 px below the group's shoes. The legal line is
unchanged (pinned at the bottom, same style). Errors now show above the button instead of under it (no room under it any
more). USS only.

Evidence: `../ct-life-evidence/title-screen/v6-signin.png`, `v6-signin_s.png`, `v6-signin-bottom.png`. The app was left
open on the sign-in screen (`-e fresh 1`, no autologin) for the user's fingerprint sign-in.

User sign-in afterwards: Seed Vault → `POST /auth/verify` 200 (557 ms), existing character, and the app went straight to
the lobby ("Your next chapter / Make your next move", Enter the City), not the lottery: the returning-player fix (df3eaee)
works on the device. City and market rooms joined. Captures `v6-after-signin-1.png` (+8 s) and `-2.png` (+20 s).
- The dev-build log overlay with the Solana SDK's `OperationInFlightException` (MWA reconnect on focus) covers the left
  half of the lobby. It is a dev-build-only overlay; sign-in still completes.
- Energy shows 0/100. The server stores 50, last action 03:06 UTC, and `/me` applies the hourly decay, so ~12 h later 0
  is the server's value, not a display bug.

## 2026-10-08: First-time flow capture, and the dev error overlay (job 2)

### First-time flow on the Seeker (dev build, brand-new dev account)

Account: dev auth with a fresh random wallet (`../ct-life-evidence/first-time-flow/wallet.txt`). The server saw a new
user with no character; alias `newbie1746`. Every screen was stepped by hand (adb taps), nothing auto-advanced. Captures
01-12 come from the first run. A network drop stopped that run at the dream step, and on resume the creator had gone back
to step 1. So 13-25 come from a relaunch with the same wallet: the lottery was already rolled, so it went straight to
the creator, and the same choices were made again. The phone was in the other landscape orientation for the second run,
so the layout sits inside the safe area on the other side.

| File | Screen |
| --- | --- |
| 01-title-signin.png | Title, "Sign in with Solana" |
| 02-origin-story-before-roll.png | "Your origin story": Insider Baby / Exit Liquidity Baby, "Discover my beginning" |
| 03-origin-story-rolling.png | Rolling ("Your story is unfolding…") |
| 04-origin-story-after-roll.png | Result: Exit Liquidity Baby, $300 Cash, $720 to pay back, "Enter CT City" |
| 05-creator-1-look.png | Creator 1 Look: "Everyday", 1 of 24 |
| 06-creator-1-look-next.png | Next look: "Breeze", 2 of 24 |
| 07-creator-1-look-face.png | Face view |
| 08-creator-2-personality-tribe.png | 2 Personality: tribes (7, each with a perk) |
| 09-creator-2-tribe-chosen.png | Builder chosen |
| 10-creator-2-traits.png | Traits (6) |
| 11-creator-2-traits-chosen.png | Fast Talker + Sleuth |
| 12-creator-2-dream.png | Dreams (8) |
| 13-creator-2-dream-chosen.png | "Build a project" chosen |
| 14-creator-3-identity.png | 3 Identity: alias, "I am 18 or older" |
| 15-creator-3-alias-keyboard.png | Alias field tapped: the keyboard covers the screen |
| 16-creator-3-alias-typed.png | Typing: no visible text anywhere |
| 17-creator-3-alias-entered.png | Keyboard closed: alias shown in the field |
| 18-creator-3-age-confirmed.png | Age box ticked |
| 19-creator-3-story-summary.png | Summary: "Breeze · Builder · Fast Talker · Sleuth · Build a project", "Start my story" |
| 20-lobby-first-arrival-1s.png, 21-lobby-first-arrival-7s.png | First arrival: lobby ("Make your next move", $300.00, Energy 50/100, Enter the City) |
| 22-first-venue-arrival-2s.png, 23-…-12s.png, 24-…-20s.png | "Enter the City": Timeline Square on arrival |
| 25-first-venue-player-walked.png | After walking forward: the player (Breeze) separates from the NPC group |

Findings for review (as seen; not changed):
1. **Lottery result layout:** after the roll, "One beginning, just for you. All rewards are game Cash." runs through the
   bottom of the Exit Liquidity card (04).
2. **Alias typing is blind:** the soft keyboard covers the whole screen, including the alias field, and there is no input
   line above it, so the player cannot see what they type until they close the keyboard (15, 16).
3. **Traits have no descriptions:** tribes show their perk, but traits are names only (10).
4. **Age checkbox placement:** the box sits mid-screen, far from its label (14).
5. **Debt not shown on the lobby:** it shows $300.00 but not the $720 to pay back that the lottery just announced (21).
6. **NPCs spawn on the player:** all five Timeline Square NPCs start at the venue spawn (logcat `npc-nav ...
   requested=(0, 0, -10.9)`, the same point as the player's spawn), so on arrival an NPC stands on the new player and
   hides them. Two NPC speech bubbles overlap (22-24). The red-jacket figure there is an NPC, not the player: once the
   player walks forward, Breeze shows correctly (25). The room sends the player's look correctly
   (`["skin_tone3","head_rain",…]`).
7. **Face view:** the face framing shows the A-pose arms jutting into the frame (07).
8. **Resume loses creator progress:** after the app went to the background and back, the creator was on step 1 with
   the model turned away, and the step 2-3 choices were not shown (`zz-resume-check.png`).

### Job 2: the dev error overlay over the lobby

Cause, in Solana.Unity-SDK @ bd0d30f (`SolanaMobileWalletAdapter`):
- The SDK's lifecycle hook calls `HandleApplicationFocus` on every focus change. When a public key and auth token are
  cached and `Account` is null, it runs a silent `ReconnectWallet` → `Login`.
- Our SIWS goes through `LoginWithSignIn`, which holds the SDK's single-flight gate but sets neither `Account` nor
  `_loginInProgress` until it finishes. The wallet hands focus back before that.
- So the reconnect hits the gate and logs `[MWA] ReconnectWallet failed: OperationInFlightException` with
  `Debug.LogError`, and the development console pops up over the game.

Fixes:
- **No reconnect while signing in.** `MwaWallet.SignIn` hides the SDK's cached public key
  (`solana_sdk.mwa.public_key`) for the duration of the sign-in. The hook returns early without it, and the sign-in path
  never reads it. A successful sign-in writes the new key; a failed one puts the old key back. No SDK change.
  `MwaWalletTests.SdkPublicKeyPrefKeyStillMatches` reads the SDK's private constant by reflection, so an SDK update that
  renames it fails the test instead of silently disabling the guard.
- **No auto-opening console.** `GameBootstrap.Boot` sets `Debug.developerConsoleEnabled = false` and
  `Debug.developerConsoleVisible = false` in development player builds. Errors still go to logcat.
- **Dev check extra.** A dev-only launch extra `logerror=1` logs one error on the title screen.

Verified on the Seeker:
- **Console stays closed:** with `logerror=1` the error is in logcat (`E Unity : CTLIFE [dev] developer console check`)
  and nothing opens on screen (`devconsole-check-after-error.png`).
- **No reconnect error:** the user signed in with Seed Vault on this build at 19:59 (real SIWS, wallet 3hqi…,
  `/auth/verify` 200). The wallet's cached MWA key and token were present, the same precondition that produced the error
  twice before. The full logcat (`job2-real-siws-logcat.txt`) has no `OperationInFlightException`, `ReconnectWallet` or
  `HandleApplicationFocus` line. The only error is the deliberate `logerror` one.
- At 19:59:54 the app's data was cleared from Android Settings (`am_clear_app_data_caller`, system uid), which was not
  from this lane. So the device holds no session now. A backup of the earlier SIWS session's PlayerPrefs is in
  `~/.ctlife-local/device-backup/` (outside the repos): restore on request, or delete.

Also in the tree, **not verified, not in the READY line:** `SignInScreen.Bleed` now measures the safe-area insets from
world rects. With the phone held the other way (ROTATION_270, camera cutout on the right) the title art stopped ~48 px
short of the right edge and the panel's light background showed (`devconsole-check-after-error.png`, right edge). It
compiles, but the device check was interrupted when the user took the phone. It needs one look on the Seeker in that
orientation.

## Status

- [x] Part A.1 backend up, tests run, commands documented
- [x] Part A.2 contract diff + re-sync, PR #1 status
- [x] B.3 restructure + compile harness
- [x] B.4 integration blockers (see above; spawn shadow not yet checked on device)
- [x] B.5 editor run against local backend
- [ ] B.5 Seeker: dev APK env=local, adb reverse, real SIWS (in progress)
- [x] B.6 EditMode tests + editor integration script
