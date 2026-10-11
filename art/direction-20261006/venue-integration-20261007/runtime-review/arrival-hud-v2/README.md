# Combined arrival correction and real HUD witness

This is the next staged helper revision against the live baseline saved-flow helper `7930d6de…73ca`. It retains the exact LocalPlayer arrival amendment from `../arrival-hardening-v1` (`09e740dc…9447`), its 18-second real-arrival controls, all saved-room navigation/profile checks, normal Flow/map/Home sequence and owner cleanup. No live/native actions have been taken by this stage's author.

The baseline native run passed 16 cases/10 visits/11 callbacks/9 public join attempts, but its actual gameplay screenshots show no HUD. That old receipt did not test HUD visibility. Source tracing confirms the review hook hides only the outer review Hud.Element; normal Flow owns a separate HUD, and CloseLobby shows it. PhoneShell is the source that temporarily changes the shared HudLayer display. **The initiating cause of the missing HUD is not established by the available receipt.** Do not present a guessed layer-reset as a correction.

This revision adds read-only state witnesses at actual EnterGame, map open, callback submit, arrival and every gameplay capture: owned HUD and its ancestors, shared HudLayer and all children, inline/resolved display, visibility, opacity, world bounds, panel attachment, screen/phone children, the phone's captured prior HUD display and hidden-layer reference. It then requires the actual owned HUD, top row and social controls to be visible and have positive on-panel layout in all 22 images. It writes the failing image/witness before rejecting. It never calls Show(true) as a workaround or changes normal UI code.

Root should use the parent review launch flags with this folder's `review-inputs.completed-v2.json` and a new output path. A hidden-HUD failure is expected to be actionable because the exact owner/layer state will be preserved. It must not be called an overall visual pass just because the prior functional checks passed.

`import-manifest.json` contains two replacements only: LocalPlayer and the review helper. The already-imported FrontendReview hook remains unchanged. All three private World/App compile modes passed; no new assembly reference or inherited compiler output escaped `/tmp`.

The source-pinned rejected-immediate-request cleanup remains source-reviewed only; no fabricated NavMesh failure. The earlier intermittent Studio arrival's cause also remains unknown. The original baseline and first staged arrival revision are preserved.
