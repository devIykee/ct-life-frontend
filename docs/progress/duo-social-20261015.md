# Duo social interiors — 2026-10-15

INTEGRATED AND VALIDATED: `client/Assets/Scripts/Features/Social/UI/{SocialUi,SocialControlIcon,MessagesApp,TimelineApp,CallsApp,SpacesStudioApp}.cs`, new `client/Assets/Resources/UI/phone-social.uss` and corresponding new metas — refine Duo social app interiors using the supplied references. Coordinator compile and device verification are recorded in [integration results](duo-interiors-integration-20261015.md).

## Applied references

- **Message composer:** one white rounded surface, wrapped text input, quiet divider, contextual count or error, one distinct blue arrow + Send/Post action. Messages stay text-only; no attachment/video affordances were invented.
- **Status tags:** muted pastel backgrounds with readable state labels and appropriate existing Phosphor icons for voice, roles and paid-call progress. Statuses derive from the current controllers/session; settlement labels still use server values.
- **Task cards / profile rows:** stronger title/body/meta hierarchy, name-derived initial avatars, rounded white contact/feed/Space surfaces, compact post action footer and genuine unread counts.
- **Compact audio toolbars:** grouped microphone, camera, speaker and leave controls with icons and text. Mic availability, publish grants, connection checks and existing disabled behavior remain intact. Speaking outlines follow the existing LiveKit speaking flag; no decorative waveform was introduced.
- **Bright simple palette:** social colours alias the shared `--ct-duo-*` tokens in `phone-ui.uss`. Spaces has a violet accent; Messages/Timeline use blue. The launcher and device hardware are untouched.

## Implementation / safety boundaries

- Every stylesheet rule is scoped beneath `.ct-phone .ct-social`; the coordinator loads `UI/phone-social` after shared phone styles.
- Minimum social button/chip hit target remains 96 logical pixels. Post actions keep visible labels in a single grouped row. Long message/post text wraps.
- Existing controller calls, auth, block/report, payment confirmation, send/retry/discard, retained drafts, render throttling and lifecycle disposal are unchanged.
- Original lightweight line geometry implements send and speaker controls absent from the bundled icons. Other symbols use existing licensed Phosphor assets. No dependency or downloaded asset added.

## Research

- Apple's current [Text fields guidance](https://developer.apple.com/design/human-interface-guidelines/text-fields), read via its linked Markdown source on 2026-10-15: persistent input identification, context-appropriate fields, wrapping and useful end controls. This is design guidance, not copied Apple artwork.
- Unity 6.3 [USS common properties](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-USS-SupportedProperties.html), opened 2026-10-15: use native flex layout and supported dimensions/borders/colours rather than browser-only effects.
- The supplied references are interpreted by the feature's actual purpose; no screenshots are incorporated into the app.

## Verification

- `git diff --check -- client/Assets/Scripts/Features/Social/UI client/Assets/Resources/UI/phone-social.uss` passed.
- Manually reviewed the complete source diff: no controller/net/logic edits; existing real state conditions and callback targets are preserved.
- Unity compilation, tests and device render review are coordinator-owned and **not performed by this agent**. Suggested regression suite: `CTLife.SocialFeatures.Tests.SocialViewFocusTests` for persistent composers, focus and press-safe rendering.

## Native review follow-up

- Reviewed the coordinator's eleven native social captures. Messages, empty/error states and the compact media controls were legible. Timeline's scope/format chips, the Journal tag, Calls' short action labels and the All Spaces back control revealed unwanted label wrapping.
- Updated only the scoped social layout: four equal format columns, reserved widths for the scope/back buttons, and nonshrinking one-line short action/tag labels. Player names and message bodies still wrap.
- `DuoSocialCapture` now records each short label's measured single-line width and actual available width, checks it remains inside its container, and includes a negative check against the former collapsed tab width.
- Reviewed all eleven `native-final` social screenshots: the corrected Timeline tabs/chips/Journal tag, Calls labels and All Spaces control render on one line. Long aliases, message bodies, connection controls and empty/error states remain readable. The receipt recorded 67 visible short labels fitting and one false failure for the intentionally hidden inbox Send control; zero REST writes and restoration of the previous REST service were recorded.
- The harness now excludes only `display:none` ancestry or resolved `visibility:hidden`, records those exclusions separately, and explicitly verifies that the inbox's footer hides Send while the thread shows it. Visible zero-width controls still fail. The final coordinator recapture passed, including 67 visible social short-label fit checks.
