READY TO COMMIT: client/Assets/Scripts/UI/Phone/PhoneShell.cs; client/Assets/Resources/UI/phone-ui.uss; client/Assets/Scripts/UI/Phone/Apps/WalletApp.cs (+ meta); client/Assets/Scripts/UI/Phone/Apps/WalletStrings.cs (+ meta); client/Assets/Scripts/App/PhoneCatalog.cs; client/Assets/Tests/EditMode/WalletAppTests.cs (+ meta); docs/research/solana-duo-phone-20261012.md; this log — Give Solana Duo a hinged device shell and a real read-only Wallet app.

## 12 October 2026

Research: inspected Microsoft Surface Duo single-screen image, Google Pixel Fold hinge/product photograph, Nintendo NookPhone official poster and contained recipe UI. References/evidence and limitations are in the research note.

Code: asymmetric narrow device rim, hardware hinge, white glass, actual clock, 3-column native app launcher, functional home indicator, existing reduced-motion-aware entrance, unified app chrome. Preserved existing catalog IDs, feature/phase gates, app lifecycles and HUD restoration.

Wallet: registered `wallet`, displaying SOL/USDC/SKR/ORE via shared WalletBalanceService. Full token precision, distinct unknown/loading/confirmed-zero states, explicit stale labels, cluster name and read-only refresh. It unsubscribes when closed without disposing shared state.

Tests: all four WalletAppTests passed in the coordinator’s final 31/31 EditMode run. The phone lane inspected final-editmode.xml. No Unity, ADB, Git, downloaded shipping asset or dependency action taken by this lane.

Native Unity compilation, tests and review capture are verified below. Android build and Seeker installation completed; actual touch checks are recorded in `docs/progress/lobby-solana-duo-20261012.md`. Native review does not establish Android behavior or real-wallet RPC availability.

Coordinator compile found CS0246 for the unsupported NUnit NonParallelizable attribute in the new fixture. Removed it; package manifest is com.unity.test-framework 1.6.0; the installed Custom NUnit package reports 2.0.5 (its documentation says it is based on NUnit 3.5). Coordinator’s later native compilation and final tests passed after the correction.

## Runtime screenshot review, pass 3

Inspected coordinator captures in ct-life-evidence/lobby-duo-20261012/lobby-pass3: phone launcher, Wallet ready/large, Map, Bank, Jobs. Removed launcher greeting/subtitle/Your apps heading, leaving the three-column grid directly below Solana Duo. Later-phase heading remains when needed.

Reduced wallet summary to network/address and meaningful loading/error states. Kept full token precision; large captured balances already fit. In phone context only, removed repeated same-as-navbar app titles and Map marketing introduction, retained real place count. Reduced Bank hero spacing. Jobs Details action now receives usable width and active career tab styling survives shared chrome. The final native recapture verifies these refinements; no Unity action performed by this lane.

## Final native verification

Inspected ct-life-evidence/lobby-duo-20261012/lobby-final/receipt.json: pass=true, 28 screenshots, no failures, runtimeErrors=0, servicesRestored=true, restWrites=0. Its explicit scope is the actual DashboardScreen, AvatarPreview, PhoneShell and PhoneCatalog apps using isolated profile and read-only in-memory REST/RPC fixtures; this is not live backend or wallet data. Captured routes include launcher, Map, Jobs, Bank, Profile, Home and Settings, plus Wallet ready/large/unavailable/disconnected cases and phone closed.

Visually inspected final launcher (nine complete first-view app tiles), Jobs (selected Careers tab and unbroken Details label), and Wallet large (full precision fits). final-editmode.xml reports 31 passed, 0 failed, including all four WalletAppTests. Subsequent Android/device acceptance is recorded in the coordinator log.

## Device corrections

Actual Seeker review found Profile wrapping at its last glyph. Launcher labels now take full tile width and use centered single-line text with bounded ellipsis for unusually long names. The new device capture confirms Profile fits. Lower-grid inspection found nine catalog routes using the fallback dots icon; PhoneShell now maps those routes to existing bundled assets. All 29 catalog routes resolve to a concrete icon. The coordinator rebuilt and performed final device checks; see the lobby progress log.
