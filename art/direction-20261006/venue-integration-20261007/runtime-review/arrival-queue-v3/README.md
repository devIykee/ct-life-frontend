# Arrival queue v3 — staged, native pending

The retained-arrival v2 run failed its newer-target control: two accepted native requests in one frame were followed by 18 seconds with no path, no movement and no seat claim. This candidate coalesces valid local movement intents before native submission; it does not claim to diagnose the earlier intermittent audience arrival failure.

`MoveTo` retains mandatory complete-path preflight and the synchronous, independently validated already-at-goal branch. Other accepted intents pause the old path without resetting it, replace the owned target fields, and return local acceptance. Actual `Update` consumes the latest queued destination once after existing seated/agent/exit guards and exit handoff. It performs a fresh complete-path check before one native reset/submission. Cancellation, direct-input takeover, pause/disable and validated arrival clear queued ownership. Native submission failure is not retried automatically. Existing path-pending and independently validated arrival requirements remain.

Only LocalPlayerController and the Editor-only saved-flow fixture are replacement sources. The already imported FrontendReview hook is unchanged. No UI visibility workaround, remote controller, seat-exit motion, persistent profile, catalog or geometry changes are included.

## Review requirements

The first actual Café screenshot and HUD-child witness now precede the diagnostic controls. Prior UI state was attached/Flex/full bounds; the rendering cause remains unestablished. This review must produce the actual visible HUD rather than force-show it.

The original 18-second approach deadline and all 16 saved-room seat cases remain. The diagnostic checks early rejection retains the request, actual arrival claims once, cancellation reaches neither native submission nor claim, and same-frame terminal_2 → terminal_3 submits exactly once from Update, moves over 1 metre, reaches terminal_3, and never claims terminal_2. No assertion requires synchronous native submission. The current-position branch must still claim immediately once. Each submitted request and per-frame endpoint/path/pending state is recorded.

The fixture uses real Flow/Map/Home callbacks and saved profiles, with explicit counted offline public joins and injected seat acknowledgements. It does not certify a live server, phone performance, all-seat physical exits or a cure for the unresolved intermittent failure.

## Root-owned native invocation

Use the established clean saved Bootstrap `-openfile` launcher, without a second project argument, then:

```
-executeMethod CTLife.EditorTools.FrontendReview.Launch
-ctlife-review 1
-ctlife-reviewLandscape 1
-ctlife-reviewCloseCamera 1
-ctlife-reviewSavedVenueChecks 1
-ctlife-reviewSavedVenueManifest /Users/mac/Documents/codes/projects/ct-life/art/direction-20261006/venue-integration-20261007/runtime-review/arrival-queue-v3/review-inputs.completed-v3.json
-ctlife-reviewCapture /tmp/ct-saved-venue-flow-arrival-queue-v3-native-20261007
-ctlife-reviewExit 1
```

Three private source checks passed: World, App with native reference dependencies and App with matching actual dependency outputs, both App checks using the private candidate World. All inherited compiler output flags were removed; outputs are solely in `/tmp`. This is compilation evidence, not native acceptance.

Preserve both previous failed runtime/harness receipts. The accepted two-property city-material normalization is now the protected baseline; the earlier whole-tree audit remains non-clean with that reviewed exception.
