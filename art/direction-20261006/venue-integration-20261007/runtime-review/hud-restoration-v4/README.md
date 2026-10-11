# Phone HUD restoration v4 — staged, native pending

The actual V3 first Café capture failed before movement controls ran. At frame 4 the shared HUD layer was attached with absent inline display (`Null`), resolved `Flex` and full bounds. Phone open wrote explicit `None`. Close restored `Null`, but resolved display remained `None` through frame 19; every HUD child had zero bounds. Toasts still rendered. The layer has no matching USS `display: none` rule and PhoneShell is its only runtime display writer. This is observed phone restoration failure, not a screenshot or camera-only problem. Preserve the V3 failure.

The five-line PhoneShell correction snapshots resolved display only when no inline value exists. It restores the state this phone actually hid. Explicit prior `None` or `Flex` values are preserved; closing does not unconditionally show the HUD. No CSS, movement, profile, art or catalog source changes are included. The current LocalPlayer queue remains exact SHA `43a77d278475b2af00c1d03484f36e31c9aa3878a9b174a3e510f62b9281c932` and remains natively untested until the full review passes.

Unity's [StyleKeyword.Null documentation](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.StyleKeyword.Null.html) describes removal of an inline setting. Its [6000.3 reference source](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Modules/UIElements/Core/Style/InlineStyleAccess.cs#L741) removes that value before `RemoveInlineStyle` tries to retrieve the matching computed style; the [display setter](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Modules/UIElements/Core/Style/Generated/InlineStyleAccess.cs#L399) updates layout only on success. [Inference] A missing style-cache entry could explain the observed Null/None split. No cache state or initiating invalidation was instrumented; this is not a confirmed engine-level root cause.

## Native review

The revised saved-flow helper retains the first actual Café/HUD capture before diagnostic controls, all 16 seat cases, 22 visible-HUD captures, original 18-second approach bounds and queue submission controls. It adds real PhoneShell visible close, reopen/close and dispose, plus deliberately hidden prior-state close/dispose controls. Five controls must pass; their temporary UI state is restored in finally. These negative controls are not a forced-visibility workaround in the normal capture/travel path. First normal phone capture also records that absent inline display was preserved as effective Flex.

Use the established root-owned saved Bootstrap `-openfile` launcher and existing FrontendReview flags, changing only:

```
-ctlife-reviewSavedVenueManifest /Users/mac/Documents/codes/projects/ct-life/art/direction-20261006/venue-integration-20261007/runtime-review/hud-restoration-v4/review-inputs.completed-v4.json
-ctlife-reviewCapture /tmp/ct-saved-venue-flow-arrival-queue-hud-v4-native-20261007
```

No live server, remote, physical-device or full pose acceptance is claimed. Public joins remain explicit counted failures with the existing offline fallback; seat acknowledgements are explicitly injected. Whole-tree protection remains root-owned and begins from the documented current material normalization.
