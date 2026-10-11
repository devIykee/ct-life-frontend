USER ACTION NEEDED: fingerprint sign-in ready. CT Life (local dev build, 03:05) is open on the Seeker sign-in screen: tap "Sign in with Solana", choose your wallet, approve with your fingerprint. A passive watcher records the result and then runs the device feature pass; nothing approves on your behalf.

READY TO COMMIT: (1) venue+audio wiring as listed before: client/Assets/Scripts/{Core/Data/Registries.cs,Core/Services/Signals.cs,Editor/ContentImport.cs,Editor/IntegrationBatch.cs,Net/Voice/{VoiceService,SpacesAudio,VoiceVolume}.cs,UI/Overlays/IncidentPresentation.cs,UI/Phone/Apps/CoreApps.cs,UI/Screens/{CharacterCreatorScreen,LotteryScreen}.cs,UI/Text/Strings.cs,World/Avatars/CafeSeatExitPresentation.cs,World/Npcs/NpcController.cs,World/Players/{LocalPlayerController,RemotePlayer}.cs,World/Venue/{VenueContent,VenueContentRegistry,VenueSession}.cs} (+ .meta of new files), client/Assets/Resources/Contract/config_snapshot.json, client/Assets/Resources/World/{VenueContentIndex.asset,VenueContent/*}, client/Assets/Venues/VenueContentRegistry.asset; (2) feature pass: client/Assets/Scripts/App/{Dev/FeatureProbe.cs,FlowController.cs}, client/Assets/Scripts/Core/Env/LaunchArgs.cs, client/Assets/Tests/Integration/{IntegrationRun.cs,CTLife.Tests.Integration.asmdef}, client/Tools~/compile_check.py, tools/integration/{run_editor_integration.sh,sync_build_clone.sh}; docs/progress/{integration,backend-dev-feedback}.md — "integration: venues + audio wired, device/editor feature probe, prioritized backend feedback". Checks: compile_check dev/release/editor 0 errors; lane Tests~ all pass; Unity EditMode 792/792; editor integration 13/13 (+ feature table).

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
| Sign-in: real SIWS via MWA | n/a (no MWA in editor) | reaches Seed Vault prompt after nonce fix; **waiting for the user's fingerprint** |
| Onboarding: lottery → creator (Snow/Rain look) | pass | pass |
| City + market rooms, rejoin on drop | pass | pass (market dropped 01:01, rejoined in 0.1 s) |
| Timeline Square presence: see/be seen move, emote, chat | pass | pass (bot heard Seeker chat, saw wave + walk) |
| Server NPCs visible | pass (5) | pass (5) |
| Shift pays Cash | pass (30000→34000) | not rerun on device |
| Block / report / unblock | pass | not rerun on device |
| Token refresh, then room rejoin | pass | – |
| Venue rooms other than Timeline Square | blocked #39 (client refuses the wrong room, enters offline) | same |
| All 21 built venues + own home load | pass (21/21, ~0.1-0.2 s each in editor) | pending device pass |
| Money: top-up intent | blocked #52 (no tx) | pending |
| Money: spot buy | blocked #76 (always SLIPPAGE_EXCEEDED) | pending |
| Money: perp open + close (1x, Trading lv 1) | pass | pending |
| Money: ORE stake tx | blocked #55 (500); never signed | pending |
| City life: needs (eat) | pass | pending |
| City life: shop purchase | pass | pending |
| City life: homes list / rent / visit | blocked #40, #75 | pending |
| Careers: list + gig pays | pass | pending |
| Social: DM send + thread | pass | pending |
| Social: feed post | pass | pending |
| Social: Space create / audio | create pass; audio blocked #40 | pending |
| Social: call ring | blocked #54/#68 | pending |
| World life: city events (crier) | blocked #64 | pending |
| World life: live ads | pass (no bookings) | pending |
| Rich List | pass (core Api) | pass |
| Home from phone → private home | pass | pass (Parent's Basement) |
| Chat with soft keyboard, emote labels | – | pass (panel docks above keyboard; labels readable) |
| Proximity voice (local LiveKit, dev token; no /rtc/token #40) | – | mic track published (audio/red), seen by LiveKit |
| EditMode tests (Unity) | 792/792 | – |

APK (local dev, all lanes + 18 venues + 4 homes): 183.8 MiB (budget 150; was 159.5 before the venue merge).
Audio payload measured 20.9 MB `.resource` (audio lane estimate 20.5-21.3; was 36.7 before the import policy).
Perf (city + 2 venues, frame time / draw calls): pending the device pass after sign-in.
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

## Status

- [x] Part A.1 backend up, tests run, commands documented
- [x] Part A.2 contract diff + re-sync, PR #1 status
- [x] B.3 restructure + compile harness
- [x] B.4 integration blockers (see above; spawn shadow not yet checked on device)
- [x] B.5 editor run against local backend
- [ ] B.5 Seeker: dev APK env=local, adb reverse, real SIWS (in progress)
- [x] B.6 EditMode tests + editor integration script
