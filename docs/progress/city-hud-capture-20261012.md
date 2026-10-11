# City HUD capture and review — 12 October 2026

Status: coordinator compilation, native stress capture and the scoped Seeker acceptance below passed. No Unity, Android or Git mutation was run by this agent.

## Completed

- Inspected official Sky, Warzone Mobile, Fortnite and PUBG control references and their available gameplay images. Findings and source links: `docs/research/city-hud-ctos-20261012.md`.
- Added Editor-only `CityHudCapture` and a `hud` dispatcher branch in `OnboardingCapture`. The harness loads the shipped Timeline Square with an actual stock Rain through `VenueSession.Enter(joinRoom:false, voice:false)`, renders its camera behind the native UI and labels fixture data on every image.
- In-memory profile and REST instances reject mutations and are restored after capture, including exceptional venue cleanup. No wallet signing, login persistence, voice connection, room join or real chat send is performed.
- Captures normal HUD, large Cash/long venue, low/expanded needs, chat draft, emotes, Sit/approach/Stand/multiple contextual actions, four screen corners, prompts near real controls, CTOS open/close and HUD hide/restore. Phone captures include both launcher pages, return to page one, the actual Wallet unavailable state and Home return. A live wallet service is prohibited in this isolated capture so opening Wallet performs no RPC.
- The receipt records actual control bounds, centre-world input picking, label fit, exact registered obstacle bounds with clearance, safe local bounds, projected world anchors and pointer alignment. A separate camera-viewport-to-visible-world-layer calculation checks the projector independently. Prompt fixtures have no action owner and establish presentation only.
- Reviewed current `Hud`, `InteractionPrompt` and `ContextPromptPlacement`: panel projection and obstacle conversion use the same parent-local coordinates; parent hiding does not trap the prompt in its pre-layout hidden state; utility actions close competing panels and release chat focus. Native geometry and scoped device behavior passed.
- Added 60 camera-pan frames around the actual actor with the same `QaUiSnapshotExporter.Capture` / `Panel.Pick` traversal used on Seeker. The prompt changes from Stand to three mic actions halfway through; every frame verifies visible action nodes. Layout-struggle warnings are counted separately from runtime errors.

## Verified native result

Authoritative evidence: `../ct-life-evidence/hud-ctos-20261012/native-stress/receipt.json`. It reports **PASS**, 24 PNG captures, 11 static prompt cases, all 60 moving-camera/QA-picking frames, no failures/fatal error, zero runtime errors, zero layout-struggle messages, four in-memory REST reads, zero writes and all original service references restored. Camera target is 2670×1200; panel layout is approximately 2401.5×1079.3. Maximum independent projection difference is 0.000315 panel units. The coordinator also visually reviewed the central actor/Stand alignment and phone wallpaper.

Latest focused coordinator test receipt: `final-tests.xml` **66/66 passed**. Earlier `phone-material.xml` also passed **7/7** in the same external evidence directory.

Earlier evidence is superseded:

- `native-pass1` stopped before screenshots because the test profile omitted required `skills`. The fixture now includes all required fields from the generated Me/User/Character/Look/Needs/Skills models and the previously passing lobby fixture.
- `native-pass2` reported pass from incomplete checks but had a 640×480 camera target against a 2670×1200 UI target. Its self-referential projector check did not detect the mismatch; **do not use that result as projection acceptance**. The harness now matches camera/UI texture dimensions and aspect, applies a measured Editor-only Y-origin mapping, and independently compares each projected anchor with the actual rendered viewport. On this batch run the display remained 640×480, requiring approximately +720 pixels of texture-space Y offset. The production projector was not changed for this capture issue.
- The 23-image `native-final` static pass did not exercise moving prompt updates during QA picking. The first device build then logged repeated recursive-layout errors. The interaction lane replaced geometry-callback/left-top repositioning with cached transform translation and measured-frame updates. The authoritative `native-stress` run and rebuilt device acceptance below exercise this correction.

## Seeker acceptance recorded by the coordinator

Rebuilt Android APK: 227 MiB, 88-second build, zero errors and 25 warnings. SHA-256: `cc3f704e8ad5e2d8b5e79dd3d1f890867aae86caf19f49a1b1892baeff6ea87a`.

Actual Seeker checks passed: bench tap → Sit → seated/Stand offered → Stand tap → Sit offered again; camera drag; joystick release; CTOS swipe to page two, Home to page one and close; HUD Map opening and X return; Lobby navigation. Evidence is the `seeker-final-*` PNG/JSON set in the external evidence directory, including `seeker-final-seated`, `seeker-final-stood`, `seeker-final-movement-release`, `seeker-final-ctos-page2` and `seeker-final-hud-restored`.

`device-final-errors.log` is empty. The coordinator's retained PID 9349 log contained 71 lines, with zero Unity/AndroidRuntime errors, fatal errors or layout-struggle messages. Thermal status was 0 at the start and 1 at completion; the app was backgrounded after returning to Lobby.

Not established by this pass: occupied-seat/rejection behavior on the device, live microphone operation, native chat send/keyboard behavior, or server-authoritative seat reservation. Do not present fixture checks or the local/device interaction cycle as evidence for those paths.

## Run and acceptance

Coordinator command, using the normal isolated review checkout and an external evidence destination:

```text
Unity -batchmode -projectPath <review-client> -buildTarget Android -screen-width 2670 -screen-height 1200 -executeMethod CTLife.EditorTools.OnboardingCapture.Launch -ctlife-captureSet hud -ctlife-captureOut /Users/mac/Documents/codes/projects/ct-life-evidence/hud-ctos-20261012/native-stress -logFile <external-log>
```

Do not pass `-nographics`; both the scene camera and UI panel must render. Existing capture modes retain their prior dispatcher behavior.

Run focused interaction/geometry, microphone-HUD and chat-focus tests. Then verify on Seeker: real seat → approach → seated → Stand, occupied/rejected seat, movement release across phone/lobby, camera dragging, native keyboard dismissal/send, CTOS navigation and microphone off when returning to lobby. Record native logs and actual screenshot evidence separately; fixture images are not live-backend evidence.

The scoped native/device results above are complete. Backend/voice and the explicitly untested interaction cases remain separate acceptance work.

Paths for the coordinator after verification: `client/Assets/Scripts/Editor/CityHudCapture.cs`, its `.meta`, `client/Assets/Scripts/Editor/OnboardingCapture.cs`, this log and the research report above. Unrelated `docs/SUBMISSION-KIT.md` is excluded.
