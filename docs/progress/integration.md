READY TO COMMIT (device round on dev 456ad50): client/Assets/Scripts/UI/El.cs, docs/progress/integration.md. Commit message: "fix(ui): buttons no parent sizes keep their text width (connection-retry Try again); device round: venues, release size, EditMode". Checks: compile_check dev/release/editor 0 errors; EditMode 793/793; Seeker retry screen verified.

# Integration lead log

Agent: integration lead (only agent that runs the Unity editor and adb on the Seeker
SM02G40619141343). Brief: `docs/AGENTS-BRIEF.md` "Current priorities (2026-10-07)".
Backend: devIykee's `ct-life-backend`, cloned read-only beside this repo.

## Local backend: start / stop (exact commands)

One-time setup (done 2026-10-07 on this Mac):

```sh
cd ../ct-life-backend
git fetch origin && git checkout --detach origin/main     # main @ 9f3e5ef; history was rewritten upstream
pnpm install --frozen-lockfile                             # Node 24.5 locally; engines >=22
cp .env.example .env                                       # gitignored; then set (values are local throwaways):
#   DATABASE_URL=postgres://ctlife:ctlife@localhost:5434/ctlife   REDIS_URL=redis://localhost:6381/0
#   JWT_PRIVATE_KEY=~/.ctlife-local/jwt-private.pem                 ENABLE_DEV_AUTH=true
#   TREASURY_PUBKEY_DEVNET / SETTLE_AUTHORITY_KEYPAIR_DEVNET / ADMIN_KEYPAIR_DEVNET -> ~/.ctlife-local/*-devnet.json
#   HELIUS_API_KEY=local-dev-no-helius  HELIUS_WEBHOOK_SECRET / LIVEKIT_WEBHOOK_SECRET = random hex
```

Keys live in `~/.ctlife-local/` (outside both repos, chmod 700). Throwaway devnet keys only.

Every time:

```sh
tools/backend-local/up.sh             # infra in Docker + migrations + api + realtime on the host
tools/backend-local/up.sh --bots 5    # + headless bots (note: backend bots leave after 200 ms)
tools/backend-local/down.sh           # stop node processes, stop containers (keeps DB volume)
tools/backend-local/down.sh --wipe    # also drop the Postgres/Redis volumes
```

| Service | Where | Port |
| --- | --- | --- |
| REST (Fastify) | host node, `server/api/main.ts` | `http://localhost:4010/v1` (`/healthz`) |
| Realtime (Colyseus 0.18) | host node, `server/realtime/main.ts` | `ws://localhost:2567` |
| LiveKit dev | Docker `livekit/livekit-server:v1.13.7 --dev --node-ip 127.0.0.1` | 7880 ws, 7881 ICE/TCP, 7882 udp |
| Postgres 16 | Docker | 5434 |
| Redis 7 | Docker | 6381 |
| Workers | **not started** (crash at startup, feedback item 34) | — |
| MinIO | **not started** (image tags missing, feedback item 35; only ad uploads need it) | — |

Logs: `~/.ctlife-local/logs/{api,realtime,migrate}.log`. PIDs: `~/.ctlife-local/run/`.
Why api/realtime run on the host and not in Docker: the backend uses one `LIVEKIT_URL` for server calls
and for the URL it hands clients, and its compose `env_file` URLs point at `localhost`; on the host both work.
`tools/backend-local/tsconfig.backend.json` (via `TSX_TSCONFIG_PATH`) works around two realtime bugs
(feedback items 32-33); without it realtime does not start, or starts and never syncs state.

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

## Status

- [x] Part A.1 backend up, tests run, commands documented
- [x] Part A.2 contract diff + re-sync, PR #1 status
- [x] B.3 restructure + compile harness
- [x] B.4 integration blockers (see above; spawn shadow not yet checked on device)
- [x] B.5 editor run against local backend
- [ ] B.5 Seeker: dev APK env=local, adb reverse, real SIWS (in progress)
- [x] B.6 EditMode tests + editor integration script
