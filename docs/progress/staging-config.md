READY TO COMMIT: client/Assets/Scripts/Core/Env/EnvConfig.cs, client/Assets/Resources/Env/EnvConfig_Staging.asset, client/Assets/Tests/EditMode/NetPolicyTests.cs, docs/progress/staging-config.md — align staging with confirmed Railway deployment and SIWS identity

# Railway staging configuration — 2026-10-10

- App-wide MWA/SIWS identity is `https://ctlife.fun/` (trailing slash required); domain `ctlife.fun`.
- Staging fallback defaults AND the existing serialized staging asset now use REST `https://ctlife-api-production.up.railway.app/v1`, realtime `wss://ctlife-realtime-production.up.railway.app`, devnet, disabled dev auth, blank identity override, blank LiveKit URL override and blank dev token. Railway service names include `production`; that does not change this environment's devnet cluster.
- Production endpoint placeholders, environment selection (`Local`), local/mock settings, and live phase are unchanged. No secrets, signing, network mutations, build-clone operations, APK build/install, or commits were performed in this lane.

## Build/asset inspection

`BuildScript.Build` loads `Assets/Resources/Env/EnvSelection.asset`, calls `Find(env)`, and validates `PlaceholderProblem()` before building; it temporarily changes `selection.active`, then restores it. The new parametrized EditMode test checks both `CreateDefault(Staging)` and this exact build-selected asset, including its identity with `Resources.Load`. Both now pass the same endpoint guard.

`ProjectSetup.CreateEnvAssets` only creates missing assets; it does not refresh existing assets from code defaults. Therefore changing only `EnvConfig.cs` would leave existing builds pointed at `.invalid`. Both paths were updated. `sync_build_clone.sh` copies all Assets including Resources/Env; the coordinator must sync before building with `-ctlifeEnv staging`. This lane only inspected that script and did not access the build clone.

## Executed verification (strict test first)

Evidence: `/Users/mac/Documents/codes/projects/ct-life-evidence/hermes-20261010/exhaustive/staging-config/`

- `identity-red.xml` / `.log`: 1 failing test, expected `https://ctlife.fun/`, actual `https://ctlife.invalid/`, before changing the identity constant.
- `identity-green.xml` / `.log`: all 8 existing environment/phase tests passed after the constant changed.
- `staging-red.xml` / `staging-red-retry.log`: 12 tests, 10 passed / 2 failed. Both fallback and build-selected asset failed on the old `.invalid` REST URL before the staging change.
- `all-green.xml` / `.log`: complete EditMode run **1574 passed, 0 failed, 0 skipped**, including all 12 environment/phase tests. Other lane tests were arriving concurrently, so suite totals differ between runs.
- Initial `python3 client/Tools~/compile_check.py --editor` passed (`compile-red.log`). Final Android player compile passed, but the editor compile was blocked by concurrently arriving `UI/Dev/QaUiSnapshotData.cs` references to `QaUiNode` / `QaUiAudio` (`compile-green.log`). Those files belong to the other lane and were not changed here. Actual Unity full-suite verification above passed.
- Scoped `git diff --check` passed. Whole-tree check found unrelated trailing whitespace in another lane's `docs/progress/e2e.md`; left untouched.

Test command (omit `-testFilter` for the full suite; no `-quit` or `-nographics`):

```sh
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath /Users/mac/Documents/codes/projects/ct-life-feel/client \
  -buildTarget Android -runTests -testPlatform EditMode \
  -testFilter CTLife.Tests.EnvAndPhaseTests \
  -testResults <evidence>/results.xml -logFile <evidence>/unity.log
```

## Caveats / remaining ownership

- An initial staging test attempt used unsupported NUnit `Assert.Multiple` (`staging-red.log` compiler error). Replaced it with compatible assertions and reran to obtain the real failing tests before production edits.
- Unity logged an existing `VenueVerify.cs` unused-variable warning; this is not a warning-free project run.
- The backend's LIVEKIT_* placeholders remain a backend blocker. Blank client overrides/tokens intentionally do not conceal it.
- `Scripts/Solana/MwaWallet.cs` still has an obsolete inline comment saying the domain is not bought; it is outside this lane's allowed code scope. Its actual constructor uses the corrected constant.
- Device/wallet/RTC E2E remains the coordinator's responsibility; these are configuration and EditMode results, not proof of deployed end-to-end gameplay.
