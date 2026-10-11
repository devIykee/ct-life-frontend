# Duo interior and toast integration — 15 October 2026

## Result and scope

User requested app-wide toast refinement and design work inside Solana Duo, explicitly excluding its home/launcher. The eight supplied images were studied by component purpose; see `docs/research/duo-interiors-reference-map-20261015.md`. No screenshot was copied into the application.

Three parallel lanes handled toast behavior, economy/career/city interiors and social interiors. Coordinator supplied shared stylesheet integration, Profile/Settings, native capture integration, build and device validation. Existing controller callbacks, wallet/payment gates, backend endpoints and persistence remain in place. No dependency was added.

Toasts have white rounded surfaces, original semantic symbols, optional supporting copy, dismiss controls and a brief entrance that honors reduced motion. Existing three-visible FIFO queue and unscaled timers remain. Only dismiss controls capture input.

Internal pages now share white cards, restrained semantic status tags, Inter hierarchy, grouped controls and consistent input/action surfaces. Specific layouts cover Bank, Jobs, needs, projects, ads, businesses, League, Profile, Settings, Messages, Timeline, Calls and Spaces. Profile and need indicators use actual values. Social composers expose only supported actions.

## Validation

- Unity 6000.3.25f1, isolated `ct-life-build/client` clone.
- Full EditMode suite: **1,800 passed, 0 failed, 0 skipped**. Initial run had 37 contract-fixture failures with `Set CTLIFE_CONTRACT_DIR (looked for contract, ../contract)`; rerun supplied the source contract directory and passed.
- Initial social capture compilation reported `CS0234` because the editor assembly did not reference `CTLife.Features.Social`. Added the explicit editor-only assembly reference; subsequent compile and capture succeeded.
- First visual inspection found short social labels wrapping despite container bounds passing. Corrected local social sizing and strengthened the capture harness with measured text width, containment and a negative control for the former collapsed width. A later false failure on the deliberately hidden inbox Send control was corrected by checking ancestor display state; visible zero-width labels still fail.
- Corrected Settings' double-surface Credits & licenses row after native inspection.
- Device inspection additionally found clipped long Ad Desk filters and Builder project-type chips. Their shared constructors now restore text-based minimum width inside the phone body, bound to the row width, with wrapped-height measurement for unusually long labels. This leaves the global button builder unchanged. Added a focused native fixture using the actual four feature chip constructors, including narrow-and-restore geometry checks. An initial compile reported `CS1503: cannot convert from UnityEngine.UIElements.StyleFloat to float`; corrected the minimum-style reads to `.value` after checking the Unity 6.3 API.
- Final native capture: **50 screenshots, pass, zero failures** (`native-release-pass`). Toast tone/queue/dismiss/expiry/dispose checks, 67 visible social short-label fit checks and 23 feature chip measurement records passed. Long aliases, large Cash/wallet balances, empty lists and connection states were reviewed visually. The final source also passed all **1,800 EditMode tests** (`editmode-release-pass.xml`).
- Social populated/connecting states use isolated GET fixtures through shipping private views and PhoneShell; **zero writes, zero media grant requests**, previous REST service restored. These prove presentation, not live payment, message or voice delivery.
- Final Android development APK: **227.0 MiB, 146 seconds, 0 errors, 25 warnings** (`android-build-final.log`). Local API configuration, existing Seeker package.
- APK installed on Seeker successfully using `adb install -r`; current private preferences backed up and retained. Final APK installed successfully; device review is recorded below.

Evidence stays outside Git: `../ct-life-evidence/duo-interiors-20261015/` contains EditMode results, `native-release-pass/receipt.json`, native images, Android build logs and device evidence. Local backend health endpoints returned 200; readiness returned 503 (dependency readiness issue), so do not infer production backend health from this UI pass.

## Seeker review

Inspected actual local-backend pages: Settings top/audio/credits, Bank, Jobs, Home needs, Messages, Timeline, Spaces, Calls, Profile, Ad Desk, Builder (including creation form), League. Scrolling and credits expansion worked; no outbound message, booking or payment was made. The device review caught the Ads/Builder chip width issue described above and redundant Messages/Calls headings, which were corrected and recaptured natively. The app log captured after this review had no fatal/managed-exception markers.

Messages showed its existing local-inbox fallback because the local server inbox was unavailable. This remains a backend integration follow-up; populated native social examples are clearly marked fixtures. The phone was backgrounded during subsequent builds. The final APK was installed and Messages, Calls, Ad Desk, Builder creation form and Bank were inspected again on the Seeker. The final screenshots confirm readable filter labels, balanced tabs and the simplified empty-state headings. Final app log: 79 lines, zero fatal/managed-exception markers. Thermal status was 1 at the last active review; the app was left on Bank for the user. Toast variants and lifecycle were verified by the native editor harness; no synthetic notices were injected into the device session.

No iOS build, paid transaction, real outbound message, voice call or production deployment was performed for this presentation change. The user’s separately edited lobby image metadata, Android plugin metadata and submission kit remain outside this change.
