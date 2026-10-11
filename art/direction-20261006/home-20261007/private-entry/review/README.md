# Private Home native review — frozen, not run by the staging agent

Import only the two exact files in `import-manifest.json`, after verifying the existing FrontendReviewController baseline hash and the already-imported base Home patch. The new helper is compiled only for Unity Editor and excluded by CTLIFE_PROD. Root owns the native slot.

Use the existing saved-Bootstrap launcher shape (`-openfile` alone for project resolution; do not add `-projectPath`, which previously opened an unsaved default scene). Existing execute method: `CTLife.EditorTools.FrontendReview.Launch`. Flags:

```
-ctlife-review 1 -ctlife-reviewLandscape 1 -ctlife-reviewPrivateHomeChecks 1
-ctlife-reviewCapture /tmp/ct-home-private-entry-native-20261007 -ctlife-reviewExit 1
```

Expected result: `home-private-entry-review.json` with `pass=true`, `cleanupComplete=true`, `servicesRestored=true`, `originalVenueReloaded=true`, errors/mutations/private joins/global leave/global disconnect all zero; exactly observed pending owner-cancel and pending native-destroy cases; captured Home entry/return phone pages and actual arrival/center walk. A process exit alone is not acceptance. Requesting a 60 FPS cap is not measuring actual FPS or phone performance.

The helper uses real Flow.EnterGame (with Start disabled to avoid a second wallet/bootstrap flow), actual Home NavigationSubmit callbacks, real VenueSession/Loader, selected B2 prefab, additive scenes, maps, native navigation and local avatar movement. `/me` returns an isolated complete profile; public realtime joins deliberately fail and are counted, explicitly exercising the existing offline fallback. Home must never call a public join. It creates no voice owner. No transport mutation, persisted identity, wallet, microphone or connected service is used.

Unsupported/null home IDs are checked before replacing the public visit. A displayed old selection is changed before button submission; its callback must recheck. A detached old View's actual Visit method is separately invoked to establish stale-view rejection; that is recorded as a direct callback-body probe, not physical input. The actual current button paths use Unity UI Toolkit NavigationSubmit. Normal Android touch and connected public return still need device/service checks.

Cleanup destroys only the test Flow, awaits its current scene exit, restores captured services in reverse order, reloads the original isolated review venue, restores frame/vsync/background settings and writes a receipt. The hook restores the original review HUD in finally. No production scene/prefab/controller/material/catalog save occurs in this fixture.

Base compiler evidence is in the parent directory. The fixture exact Editor App compilation uses current candidate World/UI private binaries, and strips all inherited output flags before one `/tmp` output. `compile-receipt.json` describes compile scope; it is not a runtime or furniture acceptance receipt.
