# App-wide toast refinement — 2026-10-15

Implemented and integrated. Coordinator validation passed; see [integration results](duo-interiors-integration-20261015.md).

## Reference interpretation

The user's first image separates feedback into a semantic symbol, a short clear title, optional supporting copy and a quiet dismiss control. CT Life now uses the white-surface version, with a restrained edge and two low-opacity depth planes. Colour is concentrated in original vector status symbols, so a long error is readable without a dark red slab. Event messages retain a separate purple symbol; they are not presented as errors or successes.

The supplied transaction ID was reference content, not real app state. No transaction IDs, receipts or status claims were invented. Existing title strings remain unchanged. `ToastSignal.Text(title, kind, detail)` supports a real optional second line; old call sites still compile. `ToastKind.Warning` is appended without changing existing numeric enum values.

## Implementation

- `ToastNotice.cs`: plain-text labels, original Painter2D symbols, non-pickable decoration, real named dismiss Button, 180ms restrained entrance honoring the existing reduced-motion setting, cancelled on detachment.
- `ToastLayer.cs`: same three-visible FIFO stack, unscaled 3.5s / 5s timers, subscription teardown and incident ownership. Dismiss removes that item and advances the waiting queue. Empty notices are ignored.
- `Resources/UI/toast.uss`: dedicated styles, bundled licensed Inter font. Loaded after the global and landscape styles in `UiRoot`.
- `ToastNoticeTests.cs`: optional/literal text, real dismiss interaction, long-copy layout and reduced-motion behavior. No debug screen or fixture messages enter normal user UI.
- `Editor/ToastCapture.cs`: isolated real ToastLayer capture of all five tones, three-visible queue, middle-dismiss advancing the fourth notice, timed expiry, empty input and disposal/unsubscription. Every fixture is visibly labelled REVIEW and the entire harness lives in the Editor-only assembly.

## Documentation checked

- [Unity 6.3 USS common properties](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-USS-SupportedProperties.html): flexbox, wrapping, border radius and text properties.
- [Unity 6.3 Painter2D](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.Painter2D.html): original status geometry through `generateVisualContent`.
- [Unity 6.3 transitions](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-Transitions.html): motion support; reused the project's existing reduced-motion-aware Tween instead of adding another animation system.

No new dependencies or downloaded artwork. This lane did not run Unity, ADB, build or Git mutation commands. Coordinator subsequently passed all 1,800 EditMode tests and 50 native captures, including toast tones and lifecycle, and installed the Android build on Seeker. Toast visual and lifecycle evidence comes from the isolated native harness; no synthetic notices were injected into the device session.
