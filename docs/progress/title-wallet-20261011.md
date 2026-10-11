# Title wallet action — 2026-10-11

User decision: remove **Use another wallet** from the title/connect-wallet screen. Wallet switching belongs in Home → Settings → Sign out.

- Removed the secondary title button, callback, switching state, unused localization entry and styles.
- New players retain **Sign in with Solana**; returning players retain **PLAY**.
- Existing Home settings route and sign-out sequence remain: leave venue, disconnect realtime, revoke auth/deauthorize wallet, clear the session, show sign-in.

Validation:

- Unity editor scripts compiled successfully; the refresh MCP call timed out waiting for readiness after compilation.
- `git diff --check` passed. No obsolete title-switch identifiers remain in `client/Assets`.
- Staging development Android build succeeded: 127 seconds, 0 errors, 25 warnings; 226.94 MiB APK. Existing compiler/shader warnings remain.
- Installed with `adb install -r` on the Seeker. Viewed the returning-player title: **PLAY** is the only action; the secondary wallet link is absent. Evidence: `title-returning-no-switch.png`.
- Settings sign-out wiring was traced in source; full wallet logout was not repeated for this UI-only removal. No new tests or full regression suite run.

APK: `../ct-life-build/client/Builds/ctlife-title-20261011.apk`; SHA256 `bde9e37bfff627cba97aab33a406a61573c80a59f5aad3842a403b4e26172521`.

Evidence stays outside the repository at `../ct-life-evidence/onboarding-open-20261011/`.
