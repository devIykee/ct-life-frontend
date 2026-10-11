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

## Status

- [x] Part A.1 backend up, tests run, commands documented
- [x] Part A.2 contract diff + re-sync, PR #1 status
- [ ] Part B (waiting for "CHECKPOINT PUSHED" in `docs/progress/steward.md`)
