# Choices before origin — 2026-10-11

The user requested character choices before the lottery, an automatic tarot draw and reveal, centered cards, less introductory copy, and use of the card references previously supplied to Hermes. They retained the bottom-right action placement.

## Implemented flow

Sign-in → character → tribe → two traits → dream → alias → review/age confirmation → automatic draw → single landscape origin card → Continue → Home. Optional character customization remains within the first selection. The draw and result need no extra tap. The result stays available to read until Continue.

- Removed the origin introduction and its three competing text lines. Centered the two/three-card deck.
- Kept the existing painted art and wide card direction; flattened the nested stats panel into aligned cash, home and debt columns. The card includes the alias and selected tribe perk.
- Kept the shared bottom-right action location and seven-segment progress, with the lottery last.
- Removed the guessed origin from the pre-draw review card. An origin receipt is never reconstructed from current balances or `/me`'s default home.
- Retained per-account/environment lottery receipt and request keys, flushed before writes. Character edits preserve the roll; an unchanged retry keeps its request key; lost creation responses reconcile through `/me`. Consent is not persisted across launches.
- Existing saved outcomes remain displayable if current Seeker eligibility differs. A committed roll with no recoverable receipt receives neutral copy rather than an invented result.
- Closing the lottery cancels its scheduled UI work. Explicit development automation remains available; older capture fixtures opt out of automatic start.

## Recovered references

Hermes session `20261009_225051_144de5`, user messages 495/708 and follow-up 2527; implementation commit 49937420. All seven original attachments still exist. Primary usable card references are Storm Breakers (`image_da4d7d.png`), Thunder Thursday (`image_11f110.png`), and illustrated reward cards (`image_b51c7a.png`). The accepted direction is landscape; these references inform hierarchy, illustration and compact benefits rather than importing another game's artwork.

Recovery report and original paths: `../ct-life-evidence/onboarding-reorder-20261011/hermes-card-context.md`. This is targeted recovery of the card discussion, not a claim to have reread all Hermes messages.

## Contract boundary

The backend still requires the lottery write before character creation. Choices are collected locally first; write order remains `POST /lottery/roll` → `POST /characters`. No backend files changed.

Known backend discrepancy: configured/returned birth homes (`studio`, `hacker_house`) differ from character creation's mapping (`skyrise`, `penthouse`). The birth card uses the exact roll receipt. Current Home uses the actual character profile. Missing old receipts cannot be reconstructed across devices after the backend's 24-hour idempotency cache expires. See `onboarding-contract-audit-20261011.md`.

## Verification

- 63 targeted EditMode tests passed, 0 failed/skipped (`editmode-final.xml`). The first run only discovered 47 because the new fixture's .meta GUID had an extra character; corrected it and confirmed all 63 ran.
- 5 actual-controller PlayMode tests passed, 0 failed/skipped (`playmode-results.xml`): creator-first order, automatic roll/result-before-create, lost response reconciliation, validation correction without reroll, unchanged-request retry, and returning-character skip. These use real screens/controller/profile/API with controlled transport and reduced motion, stopping before world construction.
- 2 City Life regression tests passed (`rent-regression.xml`): birth debt does not issue a Sunday Unlock warning; the first character observation establishes a baseline; later debt increases still notify. This regression was discovered in the actual Exit result walkthrough and corrected in `CityLifeHub.CheckRent`.
- Seeker manual walkthrough reached Home with a newly created Insider character and retained its $3,000. Cold launch skipped onboarding for that created character. A second account reached the Exit result ($300, basement, $720 debt) with a 19-character alias and the long Anon perk; no card text clipped. Labels were aligned after the first phone capture.
- Restart of the unfinished Exit account retained all selections, asked for age confirmation again, then displayed its same card in 0.84 seconds without another shuffle. Device snapshots/video are in the evidence directory.
- Final Android build succeeded in 71 seconds, 0 errors and 25 existing warnings; installed with `adb install -r`. Artifact: `../ct-life-build/client/Builds/ctlife-onboarding-review-20261011.apk` (258,434,015 bytes), SHA-256 `8f38c55c0953ac931f1698d426f25f194f3eeb7e900cb9e14bd8545f38173ab8`.
- On the final APK, an automated fresh-account phone walkthrough observed the Exit card and reached Home. UI snapshots from launch through Home contained no Sunday Unlock warning (`notification-device-check.json`, `11-birth-debt-no-warning.png`). The previous capture attempt began after the reveal, so it was repeated with monitoring started immediately after launch.
- Final review returns to the second local account’s retained Exit card. The staging wallet/session was not cleared.
- Seeker eligibility was not granted by the local dev-auth session, so the actual phone walkthrough exercised the two-card deck; three-card eligibility is not claimed as device-verified.

Evidence directory: `../ct-life-evidence/onboarding-reorder-20261011/`. No iOS build was attempted; this review targets the Seeker Android app.
