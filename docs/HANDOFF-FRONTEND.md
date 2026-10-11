# Frontend handoff (for backend contributors)

`dev` is the integrated branch: every feature branch is merged into it. Start new frontend work from `dev` on a `feat/<topic>` branch and open a PR back into `dev`.

Read first: `docs/AGENTS-BRIEF.md` (project rules), `client/` (Unity 6000.3.25f1 project), `docs/progress/` (per-area status).

## Build and run

| Step | Command |
| --- | --- |
| Compile check (seconds, no Unity) | `python3 client/Tools~/compile_check.py --editor` |
| EditMode tests (~4 min, 1615 tests) | `Unity -batchmode -projectPath client -runTests -testPlatform EditMode -testResults results.xml -logFile unity.log` |
| Android APK against Railway | `Unity -batchmode -quit -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.BuildScript.BuildAndroid -ctlifeEnv staging -ctlifeDev -ctlifeOut Builds/ctlife-staging.apk -logFile build.log` |
| Launch on a device | `bash tools/integration/seeker_launch.sh -e env staging` (add `-e qaUi 1` for the dev-only UI snapshot used by device tests) |

Staging targets: REST `https://ctlife-api-production.up.railway.app/v1`, realtime `wss://ctlife-realtime-production.up.railway.app` (`client/Assets/Resources/Env/EnvConfig_Staging.asset`, `docs/progress/staging-config.md`).

## Contract notes the client relies on

- Venue gates: the client maps `NET_WORTH_TOO_LOW`, `INVITE_REQUIRED`/`NOT_A_MEMBER`, `SYBIL_JAILED`, `NOT_JAILED` (`Features/Competition/Logic/Rules.cs`).
- Venue chat: the room's `said` skips the sender, so the client shows its own bubble on send and ignores an echo of its own session (`UI/Hud/Hud.cs`).
- A `404 NOT_FOUND` "No route for this path and method" envelope is shown as *unavailable*, not *not found* (`Features/CityLife/Logic/CityErrors.cs`).
- Open backend items from the Seeker run are in devIykee/ct-life-backend issue #5 (ORE board 500, `/businesses` list route, DM inbox shape, empty market).

## Known open client issues (Seeker, Railway, 2026-10-10)

- After a network drop the venue room can leave with 4003 and is not rejoined (`Net/Realtime/RoomChannel.cs` raises `LostUnexpectedly`, but only `city`/`market` rejoin). Chat/emotes then reach no one until the player re-enters a venue.
- Once (not reproduced since), two taps on the HUD mic did nothing and reached the world layer as a tap at panel (0,0) (`UI/WorldInputLayer.cs`).
- With arrow-button movement, the "Walk" caption overlaps the down arrow.
- The server's move speed check uses arrival times, so bunched packets occasionally reject a run step (`SPEED_EXCEEDED`) and the avatar snaps back.
