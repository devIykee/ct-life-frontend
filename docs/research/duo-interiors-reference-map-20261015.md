# Duo interiors and notices — reference study, 15 October 2026

The eight images supplied by the user are design references, not shipping artwork. The launcher, hardware, header, dock and search stay at the approved 854beef6 checkpoint. This work changes in-app notices and the phone’s internal pages.

| Reference | Detail studied | CT Life use |
|---|---|---|
| 1. Toast variants | Shape-led semantic icon, title/detail hierarchy, small dismiss glyph, soft edge | App-wide notices; white surface, colour in icon, real optional supporting text, 96-unit dismiss hit region |
| 2. Status tags | Restrained tinted background, icon plus word, consistent spacing | Existing payment state, career availability, project/ad lifecycle, rent, call progress; no guessed state |
| 3. Account menu | Aligned symbols and labels, deliberate group separators, quiet selected surface | Settings control/audio groups and Profile identity/skills, subdued sign-out row |
| 4. Metrics and navigation | Purposeful colour, label/value hierarchy, small meaningful progress, grouped controls | Actual needs, Cash/debt, rank/net worth and Profile skills; no invented financial chart |
| 5. Feedback form | Clear input area, concise heading, one primary action, quieter cancellation | Existing phone forms and composers, retaining validation and real actions; no new feedback endpoint |
| 6. Task cards | Status above title, secondary context, distinct action footer | Careers, project cards, ad bookings and businesses |
| 7. Message composer | Contained input, supporting controls separated from content, clear send action | Messages and Timeline; no unsupported attachment/file/video button |
| 8. Compact toolbars | Grouped actions, useful spacing, one emphasized active control | Existing microphone/camera/speaker/leave actions in Calls and Spaces; no fake recorder or waveform |

The common language is pure white content on a quiet neutral canvas, Inter typography, semantic pastel tags, fine borders and restrained edge depth. Feature identity comes from content and state; new decoration does not imply functionality.

Primary sources opened/read:

- [Apple materials guidance](https://developer.apple.com/design/human-interface-guidelines/materials), read through its linked Markdown on 15 October: glass belongs chiefly to navigation/controls; content needs a clear hierarchy and sparing effects. The fictional Android phone uses existing Unity materials, not Apple native frameworks.
- [Apple notification guidance](https://developer.apple.com/design/human-interface-guidelines/notifications), read through linked Markdown: concise, useful information at a glance. These are in-app notices, not OS push notifications; no permission prompt was added.
- [Unity 6.3 USS properties](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-USS-SupportedProperties.html): native flex layout, dimensions/borders/colour; browser CSS shadow/filter effects are not assumed supported.
- [Unity 6.3 resolved minimum width](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.IResolvedStyle-minWidth.html) and [StyleFloat value](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.StyleFloat-value.html): minimum dimensions expose `StyleFloat`; the bounded text-measurement helper reads their numeric `.value`.

See the toast, interior and social progress notes for files, detailed source checks and coordinator validation. User-provided references informed the design; no third-party screenshot is included in the APK.
