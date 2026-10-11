# Solana Duo interiors — 2026-10-15

Implementation integrated and validated by the coordinator; see [integration results](duo-interiors-integration-20261015.md). No Unity/ADB/Git commands run in this lane.

## Scope and reference mapping

The user explicitly excluded the phone launcher/home screen. This change is inside `.ct-phone-body`; the approved Duo hardware, wallpaper, icons, header, dock and search are unchanged.

- **Status-tag reference:** Bank debt and payment results, career availability, project lifecycle, ad booking/review state and rent status. Colour reinforces explicit text and an outline shape; it does not invent a status.
- **White task-card reference:** Careers put status above role, level and actual pay, with the action footer separated by a fine line. Project and ad cards separate their state, identity, supporting data and action.
- **Metric-card reference:** Home needs use actual projected values and their existing real progress fills; League shows server rank and net worth. No decorative charts, fabricated trajectories or new targets.
- **Segmented-control reference:** Existing Careers, money and League tabs, plus Builder and Ad Desk view switches, use a compact connected pill group with a plain selected segment.
- **Form/modal reference:** Existing interior text fields and buttons now share quiet inset input surfaces, clear focus and plain primary/secondary controls. Payment acknowledgement, quote, amount, signature and pending/unknown paths remain in place.
- **Profile menu, composer and toolbar references:** Covered by coordinator / social lanes; this lane supplies the common tokens and presentation components.

## Changed

- Added `UI/Phone/PhonePresentation.cs`: presentation-only status, metric, state panel, footer and segment helpers; all labels use `El.Text` with rich text off. No provider/network/state inference.
- Added `.ct-phone-body` interior tokens and scoped component rules in `Resources/UI/phone-ui.uss`. Pure white cards, quiet neutral canvas, one blue action colour, semantic pastel status colours, consistent Inter typography and restrained edge depth. No unsupported browser box-shadow, backdrop-filter or line-height.
- `Money/UI/BankApp.cs`: one readable Cash hero with debt status; compact horizontal top-up packs; confirmed/pending/failed/expired states become explicit panels. Existing flow calls, disabled conditions and all mainnet/wallet checks retained.
- `Careers/UI/CareersApp.cs`: career cards use meaningful status tags, clearer role/pay hierarchy and a separated action footer. Existing apply/work/details/practice and gig lifecycle unchanged.
- `Ventures/UI/BuilderApp.cs`: lifecycle tags, clear project name/kind, treasury metric, member count and separated entry action. The old combined project sentence is split using one additional localized string `project.card.people` in `Logic/VStrings.cs`.
- `Ventures/UI/AdDeskApp.cs`: view segments, readable slot title/identifier, actual availability tag, booking-state tags and separated booking action. No booking, upload, payment or cancellation logic changed.
- `CityLife/UI/NeedsPanel.cs`: five needs become meaningful metric tiles using the same data projection, amount labels, fill geometry and actions. This panel is instantiated only in `CityHomeApp` (source search confirmed).
- `CityLife/UI/CityHomeApp.cs`: rent state gets a semantic tag, with existing debt and timing details retained.
- `CityLife/UI/CityBusinessApp.cs`: owned businesses receive a state tag and separated collect/sell actions.
- `Competition/UI/LeagueApp.cs`: rank and net worth now have distinct metric tiles; unchanged backend values and board actions.

## Research / verification

Opened current Unity 6.3 LTS sources before implementation:
- https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-USS-SupportedProperties.html
- https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-USS-Properties-Reference.html

Apple HIG status, segmented-controls and progress-indicators pages were opened, but the web tool returned only their JS shell/title; no specific Apple text is claimed as evidence. The role mapping above is based on the user's actual supplied images and the existing feature source.

Source review confirms `.ct-phone-body` and launcher `_home` are sibling elements in `PhoneShell`, so the new scoped rules do not reach the launcher. Changes add no package, asset licence requirement, generated financial state or persistence.

Coordinator subsequently completed Unity compile/USS import, 1,800 passing EditMode tests, 50 passing native captures and actual Seeker review of Bank / Jobs / Home needs / Builder / Ads / League. See the integration note for fixture boundaries and backend limitations.

## Native visual review (coordinator capture)

Reviewed six actual native captures from `ct-life-evidence/duo-interiors-20261015/native/`: Bank, Jobs, Home needs, large Wallet holdings, Profile, Settings. The large Cash number and token holdings fit; long profile alias wraps; Jobs status/pay/actions have visible separation; real needs and severity colours render coherently. Lower Home/Wallet content continues below their scroll viewport, without intersecting the hardware/home indicator.

No own-file visual correction was warranted by these captures. Reported one cross-lane Settings inconsistency to coordinator: Credits & licenses uses a nested outlined button/card and larger text than the other menu rows; CoreApps owner to adjust. Native fixtures are layout evidence, not live backend verification or Seeker evidence.

## Device-discovered chip regression

Coordinator's actual Seeker Ads and Builder form captures exposed stacked letters in content-sized chips. The earlier native views did not exercise these actual controls. Root cause: `El.Btn` owns decorative child elements, which remove Unity's intrinsic text measurement; a nonzero `min-width` leaves a nonempty content box, so the existing emergency zero-width fallback does not run.

Bounded correction: `PhonePresentation.SizeToText(Button)` measures the resolved font plus padding/border, clamps the minimum to parent content width minus margins, and measures height when a label exceeds that width. It only runs under `.ct-phone-body`, skips hidden ancestry, listens to parent resize, removes that listener on detach, and uses epsilon guards for style updates. VUi, MoneyUi (+ ChipTabs), CityUi and CompUi call it for their text chips. Global El, launcher and overlays are unchanged.

Added editor-only `DuoInteriorChipCapture`: actual feature chip constructors in the actual PhoneShell, with Ads filter/term labels, Builder type choices and segments, Money/CityLife/Competition controls, and a long term at narrowed/restored widths. It checks actual font measurement against content bounds and wrapped height, chip containment and equal short-segment widths. It performs no network or persistence writes. Final native execution passed, including 23 chip measurement records; the final Seeker review confirmed Ads and Builder labels fit.

Current Unity 6.3 APIs checked:
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.TextElement.MeasureTextSize.html
- https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.VisualElement.MeasureMode.html
