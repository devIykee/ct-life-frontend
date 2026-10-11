# WSL session, 2026-10-11

Machine: Windows + WSL2, backend clone at `../ct-life-backend` @ `dbfefc1`.

## Done (source only; not compiled in Unity, not run on a device)
- Venue room rejoin after an unexpected leave (4003): `World/Venue/VenueSession.cs` (`OnChannelLost`, `RejoinAfterLoss`,
  `AdoptChannel`), `LocalPlayerController.Rebind`, `InteractionController.Rebind`.
- `SPEED_EXCEEDED` catch-up instead of snap-back: `LocalPlayerController.CatchUpFrom` / `SendMove` / `CatchUpStep`;
  `VenueSession.OnRejected`. Backend root cause: `backend-dev-feedback.md` item 96.
- Arrow-mode caption overlap: `MovementControls.RefreshMode` toggles `ct-move-arrow-mode`; rule in `city-hud.uss`.
- Tests: `Tests/EditMode/MovementSyncTests.cs` (catch-up stepping, speed cap margin, arrow-mode class).
- Contract re-synced to backend `dbfefc1` (additive only; see `contract/SOURCE.md`), `config_snapshot.json` rebuilt,
  REST models regenerated (9 new models, existing `.meta` GUIDs kept, new `.meta` files added).

## Blockers
- **Unity on Windows is not usable.** Hub installed `6000.6.5f1`, and the installer exited with code 2 (Hub log:
  `%APPDATA%\UnityHub\logs\info-log.json`). `Editor\Data` lacks `Managed`/`PlaybackEngines`, and no Android module is selected.
  The project pins **6000.3.25f1** (changeset `e1dba0a9aba4`). Install that version with Android Build Support
  (+ OpenJDK, Android SDK & NDK) from Hub, run as administrator.
- `client/Tools~/compile_check.py` hardcodes the macOS editor path (`/Applications/Unity/Hub/...`), so the
  pre-licence compile check cannot run here until it learns the Windows path.

## Next
1. Install Unity 6000.3.25f1 + Android modules. Then run EditMode tests:
   `Unity.exe -batchmode -projectPath client -runTests -testPlatform EditMode -testResults results.xml -logFile unity.log`.
2. Build a staging APK, then verify on the Seeker: venue rejoin after toggling airplane mode, running without snap-back,
   and the arrow-mode caption.
