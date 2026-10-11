# Package Manager warning audit — 6 October 2026

The user reported the Package Manager prompt alongside Unity and Android closing. These
findings classify the visible package messages; they do not diagnose the latest phone exit.

## Unity IAP

Native Package Manager showed `com.unity.purchasing` 4.15.1 as deprecated, with the message
that IAP 4 is unsupported as of 8 June 2026. The source/serialized-reference audit found
no purchasing API, assembly, link or IAP component references in the client. The unused
direct manifest dependency was removed. Native Unity resolved the lockfile: the IAP entry
was removed and the remaining services-core dependency depth changed from 1 to 2, with no
package version upgrades. Runtime acceptance is still pending. No payment flow or imported
contract was changed. See `ct-iap-dependency-20261006.md` for the bounded dependency audit.

## LiveKit

- Installed version: `io.livekit.livekit-sdk` 2.1.0, OpenUPM.
- Exact Package Manager metadata: `errors: []`, signature status `Unsigned`, reason
  `.signature file could not be located in the package`, code
  `ERR_LEGACY_SIGNATURE_FILE_NOT_FOUND`.
- The publisher's [release page](https://github.com/livekit/client-sdk-unity/releases/tag/v2.1.0)
  lists 2.1.0, commit `356bf5dfe079fd981cd514271b32d6cfe5d5516f`. The
  [official installation instructions](https://github.com/livekit/client-sdk-unity#installation)
  explicitly offer OpenUPM and Git installation.
- Compared the installed ARM64 Android `.so`, Android WebRTC `.jar`, and ARM64 macOS
  `.dylib` against the exact release's Git LFS SHA-256 identifiers and byte sizes. All
  three matched. Receipt: `ct-livekit-native-integrity-20261006.json` beside this report.
- This verifies those three files' identity, not the entire package or every runtime
  behavior. No signature was fabricated and no signature checks were disabled.
- LiveKit has native initialization at startup, so it remains within the device diagnostic
  scope if a future crash trace names it. The missing signature message itself is not a
  native crash trace. Keep version 2.1.0 pending actual failure evidence.

## Separate native review results

- V5 restarted during the Input System prompt; no valid Play result. Afterwards
  `activeInputHandler` was 2 (Both), previously 0. The origin of that change is uncertain
  because the Unity window was being interacted with concurrently; it was preserved.
- V6 exited 1 before Play with `TimeoutException: Review startup stopped after 120 seconds:
  asset imports or the Unity Search index are not ready. The existing scene has not been
  replaced.` The full-index readiness gate induced a large cold index pass and was too
  strict. It has been narrowed to public database initialization before Play. Native
  acceptance is pending; a bounded automation exit is not reported as an Editor crash.
- Unity's ADB failure printed inherited environment values into the V5 log. Those values
  were redacted from that generated file. Subsequent Editor launches use a minimal process
  environment without unrelated service credentials.

The phone is still absent from ADB. Package-specific Android exit history and fresh logs
must be collected before attributing the user's current closure to these package messages.
