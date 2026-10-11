# CT research / incident presentation · 6 October 2026

## Latest staged work: Perps terminal

- Native update: root integrated the original bundle and ran the review. Initial run and v2 both failed the strict
  text geometry gate on `empty`; unavailable screenshot passed. v2 captured the failure and identified the
  right-aligned `GAME CASH` eyebrow at a near-wrap intrinsic width. v3 stages a stylesheet-only full-column-width
  correction, with the gate unchanged: `art/gameplay-candidates/perps-native-v3-20261006`, manifest
  `456ab96e09401962fb0cb559c5fd076523c12d05a9e89878932754dc4a2bb732`. Native rerun remains required.

- Bundle: `art/gameplay-candidates/perps-20261006`; remains outside Assets. Manifest SHA-256:
  `4c8bdae74b513b482dfe14369559e8f4671057425a2c410ffcd5637be84109c1`.
- Net/UI/App compile passes against actual Unity 6000.3.25f1 assemblies for Android-development and Editor.
  Executable parser/contract/precision/retained-intent/cancellation tests pass; no live request or order was sent.
- Narrow Api, RestClient, PhoneCatalog and guarded native-controller hook patches pass `git apply --check`.
  Native review helper is ready for root's queue: `reviewScreen=perps`, six labelled fixture states plus real
  side/margin/leverage/review/back UI callbacks, 45-second bound, screenshot/JSON receipts, explicit no-write mode.
- Material backend activation gap recorded in `backend-dev-feedback.md` E.26–30: in-flight reservation can fall
  through idempotency middleware; no conclusive ambiguous-open lookup. Normal catalog candidate is read/prepare-only.
  Full open/close source exists behind a reviewed integration switch. Single-attempt transport and retained
  intent do not substitute for verified backend economic semantics.
- Native visual/input, actual UnityWebRequest and backend recovery acceptance are **not yet passed**. Next queued
  task after this freeze: isolated existing-local-LiveKit lifecycle test design, without microphone capture,
  existing-room interference or server/package changes.

## Existing-local-LiveKit native harness staged

- `art/gameplay-candidates/spaces-native-review-20261006`, frozen manifest
  `2e14f49b5ae6957f3e635b3e98c7c44b00257bb681a1221c64059d167adf6746`.
- Actual installed Unity6000.3.25f1 + LiveKit2.1.0 Net/App Editor compilation passed; no native launch or connection
  attempted by this agent. Spaces candidate itself remains unchanged/frozen.
- Harness uses one approved unique local review room, fresh UUID identities and review-prefixed display names.
  Requires real nonzero synthetic PCM through candidate AudioStream, roster removal/resubscribe, disposal and two
  actual native late-connect completions followed by disconnection. It never calls microphone/permission APIs.
- External token creation script reads local credentials from environment and creates0600 files under `/tmp`;
  no key/JWT is stored in source or written to logs. Native test has a90-second bound and restores runtime
  runInBackground after all cleanup paths. Sanitized receipts distinguish failures and remaining acceptance limits.
- Root owns integration/native queue. Native pass, Android and actual microphone acceptance remain outstanding.

## Completed research

- Read handoff, workspace brief, build spec and relevant contract sections; inspected actual M4 city screenshot.
- Opened primary sources for Pumpville competition, X Spaces, X developer policy, Roblox retention guidance, LiveKit media subscriptions, Solana submission/publisher policy, CLOCK IN and likeness/endorsement constraints.
- Delivered [research and direction](../research/ct-social-game-direction-20261006.md). Recommendations are labelled proposals/inferences; no virality claim or backend completion claim.
- Verified publisher policy via direct HTTPS after web extraction failed.
- Current X-post sampling did not complete. Chrome reached search/loading state; after interruption, browser reconnection failed twice with `Debugger unattached`. No post/sentiment metric is attributed to that attempt. No posts, messages, account changes or financial actions performed.

## Incident presentation ownership

Coordinator approved these files only:

- `client/Assets/Scripts/UI/Overlays/IncidentPresentation.cs` (new)
- `client/Assets/Scripts/UI/Text/IncidentStrings.cs` (new)
- `client/Assets/Resources/UI/incidents.uss` (new)
- Narrow routing change in `client/Assets/Scripts/UI/Overlays/ToastLayer.cs`
- New matching metadata and a contained development-only verification helper if needed.

The actual imported `contract/realtime/messages.ts` enum and `contract/ids/events.json` contain six incident IDs. Existing `ToastLayer` also handles four uncontracted IDs. The replacement uses the six confirmed IDs only and leaves money, world truth and personal liquidation handling with their existing owners.

## Implemented

- Replaced the old event-to-toast switch with `IncidentPresentation`, automatically constructed by the existing ToastLayer entry points. No coordinator-owned HUD/root files were edited.
- Recognizes only the six imported event IDs. Shows one non-modal eight-second card, queues at most four, retains twelve recent receipts and deduplicates the last 64 payloads. Queued headlines expire after 30 seconds. Without a synced server clock, incoming messages enter receipts but do not masquerade as fresh headlines.
- Rendering uses existing palette, fonts and icons, disabled rich text, bounded display fields and explicit Game Cash context. The empty overlay is non-pickable and full-screen menus/phone/modals suppress it. Session changes clear retained incidents.
- No ORE number is displayed: the optional event `amountOre` key/denomination is not defined by the imported contract. The prose requires token amounts in base units; labelling the old presenter’s raw number as whole ORE was not defensible.
- Read-only local backend inspection found `server/realtime/rooms/city.ts` has a TODO EventPublisher and sinusoidal mood, with no incident broadcast implementation in that file. This is a client presentation implementation, not proof of a live server event engine.

## Verification

- `python3 client/Tools~/compile_check.py --editor` exited 0 for the final owned source, including integer timestamp validation and omission of unconfirmed ORE numbers: all CT Life player/editor assemblies compiled. This standalone compiler check does not establish native import, visual acceptance or device behaviour.
- `git diff --check` passed for owned paths.
- Added `CTLife.UI.IncidentPresentationChecks.Run()` under the existing development/editor guard. Checks canonical dedupe, newest-first queue, TTL, history/burst limits, timestamp validation, unknown IDs, absent fields, unconfirmed token units, rich-text disabling and session reset. It does not publish synthetic events or make network calls.

## Next

- Coordinator reported the guarded incident checks passed in native v4 and provided `/tmp/ct-direction-paired-v4/city-incident.png` and `city-portrait.png`. Both images were inspected. The banner was readable; the history button clipped because its text did not contribute intrinsic width. With coordinator authorization, added explicit width/min-width 280 to `incidents.uss`. The next native capture must verify the label fits. Any explicit visual event fixture remains review-only.
- Coordinator owns native Unity import/render/device validation. No independent Unity or adb process will be started.

## Playable loop audit and staged candidate

- Wrote [six-phase playable-loop audit](../research/ct-playable-loop-audit-20261006.md), separating API-wired source, offline visual fixtures, later-phase placeholders and backend-source limits. Phase scope, ORE/SKR and backend ownership unchanged.
- Confirmed the strongest currently wired loop is a paid career shift. Needs actions currently cost zero, so the result is earn plus recover, not a completed spend/earn economy.
- Read-only backend findings: apply returns `{id,level}` while imported `Job` requires `careerId,level,xp`; careers report fixed level 1/XP 0; shifts are in memory, use level-1 pay, persist the Game Cash ledger and return XP without a progression write in that method. These were reported to the coordinator; no backend or contract edits.
- Root authorized a candidate outside Assets: `art/gameplay-candidates/mod-desk-20261006/`. It contains JobsApp patch/baseline, the moderator desk, shift settlement/receipt view, localizable text and a separate stylesheet. Ten original fictional cases, five Ban/five Allow,45-second registry duration, fair capped score, duplicate-click handling, server-clock settlement, single in-flight state and server-confirmed pay/XP receipt. Other career games retain the shared host. Explicit practice mode awards nothing and never calls a backend.
- Staged direction follows the user's landscape decision and root's clean Manrope/thin-border phone direction. No new dependency and no live Assets import by this agent.
- `verify_candidate.py` passed: staged UI assembly compilation, actual model checks for balanced unique deck, perfect/zero/partial scores, duplicate answers and deadline. An initial closure definite-assignment error was corrected before the passing run.
- Root received integration instructions and a development-only practice invocation. Native landscape interaction/readability and connected shift settlement remain unverified. The fixture transport rejects all mutations, so its profile cannot prove earned balance or payment.
- Reopened exact Pumpville primary sources: [Solana Mobile winners](https://solanamobile.com/blog/solana-mobile-monolith-hackathon-winners-announced) and [Pumpville docs](https://pumpville.gitbook.io/docs). These support competitor existence/description, not CT Life demand, retention, revenue or guaranteed virality.

## Stability priority

- User clarified the game closes on the phone. [Stability audit](ct-stability-audit-20261006.md) separates recorded review-requested quits from an unverified ordinary-session exit, plus the Editor audio reload exception storm, Search startup and Addressables configuration.
- Root authorized lifecycle work only in `AudioManager.cs` and `MusicDirector.cs`: reconstruct runtime lookup/subscription state on enable, reuse named native sources, retain selected music/fade state and resume paused loops. Standalone compilation and targeted whitespace checks passed. Live source frozen for root's native test; no Android-fix claim.
- Staged `art/stability-candidates/audio-reload-20261006/AudioReloadReview.cs` and metadata outside Assets with exact README run procedure. Native review uses two actual `RequestScriptReload` cycles, native IDs/counts, loaded/playing clips, district, listener, SFX bus and footsteps, followed by 12 seconds sustained play. SessionState spans reloads; no source trigger, network bootstrap or asset save.
- First full-index gate failed in root's native V6 run after many package imports. Replaced only the staged harness gate with the coordinator/world-agent verified public `SearchService.EnumerateDatabases().Any()` in idle Edit mode. Revised staged source compiled successfully against installed Unity6000.3.25f1 Editor APIs; root owns copy/import/run. Harness remains frozen outside Assets until that handoff.
- Root independently verified three installed LiveKit native payload hashes/sizes against official release Git LFS pointers. This does not convert the observed unsigned-UPM warning into a signature or establish a phone crash cause.
- Native audio V1 failed before reload: `MissingComponentException` on the new `sfx0` due to Unity-object `??` semantics. Harness correctly failed, restored the scene and exited 1. Replaced all five new component coalesces in owned audio files with explicit `== null` checks, and corrected the existing AudioClip fallback. Recompiled successfully, whitespace checks passed, and froze source for root's V2. Native reload acceptance remains pending; the earlier compiler pass was insufficient evidence.
- Native V5 subsequently passed two real reloads, runtime source/playback/listener/SFX checks and 12-second sustained play: `PASS reloads=2 runtimeErrors=0 sceneRestored=True`, root-reported exit 0. Earlier failures and the temporary runtime-only background setting used to isolate Editor focus are preserved in the stability audit; this is not an Android-crash conclusion.

## Staged moderator audit follow-up

- Current live JobsApp still matches baseline SHA-256 `f6ba9036147259a8dd85a17545a3c899e466bef5ad83d76ad07814cdbb937245`. Live El.Btn now uses native Button.text, so the suspected stale inner-label issue was rejected on current source evidence.
- Candidate-only corrections: Ban/Allow contrast including hover/focus; non-promissory stale balance/XP copy; bounded request waits with session cancellation; monotonic deadline floor when server clock is unsynced; one complete retry only after explicit SHIFT_NOT_FINISHED; assignment/receipt validation; duplicate/late-click guard for other mini-games.
- New `ShiftReliability.cs` plus existing model checks execute outside Unity. Final `verify_candidate.py` passed staged UI compilation, balanced finite scoring/deadline-in-feedback checks, clock skew timing, cooperative and non-cooperative timeout handling, session cancellation and no hidden write retries. All writes remain delegated to existing Api/RestClient endpoints. No backend/contract edit or financial request occurred.
- Frozen source/test manifest `art/gameplay-candidates/mod-desk-20261006/CANDIDATE.sha256`, SHA-256 `6d427daa47e97a2b8f81de79d2a9449d0b70a706933fa0a1d980d98a7c1230d0`; README lists all six integration destinations, guarded practice probe and connected failure probes. Native/connected moderator validation remains pending.

## Spaces frontend candidate — current backend capability first

- Read current brief, full social-layer spec, imported OpenAPI/realtime contracts, client phone/REST/voice/session code and sibling backend route registration read only. The contract supports list/create/detail/hand/promote/demote/end and RTC tokens, but the sibling API registers no Spaces or RTC routes. Its shared token minter returns epoch-second expiry whereas the response contract expects an ISO date-time; a future handler must adapt that field. No deployed endpoint was queried.
- Staged five runtime/style files and two narrow integration patches under `art/gameplay-candidates/spaces-20261006/`. Existing Phase 3/voice gating remains. The source has real-response empty/unavailable distinctions, host/listener/hand queue controls, server-confirmed end/report actions, bounded in-flight requests, retained create intent, identity-change cancellation, and explicit mic consent. New two-dimensional stage audio avoids the existing proximity service's six-nearest-avatar filtering. No backend/contract/package/live Assets edits or live network/mic operation occurred.
- Opened current official Unity 6.3 permission/pause docs and LiveKit Unity 2.1.0 release/participant management docs; verified every used delegate against installed package source and DLL. Server-side promotion/revocation remains required. No invented permission API, listener count, avatar name, endpoint or automatic speaker capture is used.
- `verify_candidate.py` passed full staged Net/UI/App compilation in Android DEVELOPMENT_BUILD and Editor branches against the actual installed LiveKit DLL. Executed transport/model checks passed for endpoint paths/bodies/idempotency, role/queue boundaries, speaker cap, invalid snapshots, owner cancellation and bounded non-cooperative waits. Initial ambiguous RoomOptions compile failure was corrected with explicit `LiveKit.RoomOptions`; no native success is claimed from compilation.
- Candidate README records exact copy paths, patch procedure, capability gaps and required native/multi-client acceptance. Connected voice, Android permission/lifecycle cleanup, role-revocation races, real listener-count accounting, pending create after process kill and Spaces-specific block enforcement remain integration acceptance items. Candidate is frozen for root's independent review; root owns integration/native queue.

## Spaces adversarial follow-up

- Root requested a second lifecycle review. Explicitly reported/unfroze the candidate before modifying it. Found source-proven late-connect leak: installed LiveKit `Room.Disconnect` is a no-op while connect is pending. Also found stream/room cleanup order risk, a stale-role escape while permission was pending, unverified MIC ON immediately after SDK's asynchronous startup, and missing view-panel-detach disposal. Concrete interleavings and SDK evidence are retained in `art/gameplay-candidates/spaces-20261006/ADVERSARIAL-REVIEW.md`.
- Candidate now drains late connection/publication completion, disconnects abandoned rooms, disposes remote streams before Room handles, uses a monotonic per-gesture publish gate, and tears down on panel detach as well as normal close/sign-out. New owned capture source/probe use only public/protected SDK processing APIs plus current Unity Microphone/audio callbacks; readiness is cancellable, no automatic resume exists, raw local samples are cleared, and the audio callback is detached before DSP/native source disposal. Package/private internals were not modified or reflected.
- Repeated installed-SDK compilation passed Android-development and Editor branches. Executed gate checks cover deadline expiry, refreshed same-role consent, invalidation followed by re-promotion, pause and demotion during publication. Existing contract/timeout/cancellation checks passed. Native late-connect, rejected-publish, microphone, allocation and repeated-lifecycle probes remain unperformed and are explicitly listed; original frozen-source success did not establish these properties.
- Seven runtime/style files now require integration rather than five. The shared compiler cache changed during review, so Editor references now come directly from the installed Unity directory. Current narrow patch baselines were refreshed after root's independent Timeline changes; no live source was replaced.

### Native review follow-ups — 2026-10-06

- Perps native v4 passed unavailable/empty, then failed active touch target `perps-openTab`; receipt lacked exact
  bounds. v5 staged at `art/gameplay-candidates/perps-native-v5-20261006`: nonshrinking136-unit standard buttons,
  unchanged124-unit gate, detailed all-button diagnostic, bounded painted-layout settling, scrolled position /
  receipt / retained-request captures. Editor UI/App compile and three patch checks pass; native pending root.
- Earnings replay recovery staged and root integrated. Arithmetic variation staged separately at
  `art/gameplay-candidates/earnings-variation-20261006`, preserving recovery copy. Actual growth/margin/EPS ratios
  vary; numerical headlines/reasons derive from report figures. Android-dev+Editor UI/App compile passes;
  1024decks/6144reports with independent arithmetic, choice uniqueness and copy in three locales pass. Original
  constant-answer model fails the diversity check as a negative control. Same six reports/60seconds/scoring /
  settlement; native varied-copy acceptance pending. Manifest d54936ee037792089a3d0b6b15e960e6ab5f45190614651139ca14832a973834.
- Local native Spaces v1 failed after listener connect with a generic bounded-predicate timeout, runtimeErrors0,
  background restored. No PCM or lifecycle pass claimed. Quiet raw log was inspected only through fixed-pattern
  counts; no sensitive data copied. `spaces-native-v2-20261006` adds named phases and sanitized publication /
  subscription / stream / tone / meter / DSP snapshots, preserving all deadlines and runtime source. Actual Net /
  App compile and patch check pass; next native run belongs to root. No live Assets edits or native launches here.

### Thread Workshop writing shift — staged 2026-10-06

`art/gameplay-candidates/thread-workshop-20261006` contains the distinct50second/50point `kols_engagement`
replacement: five original three-fragment CT microthreads, tap-to-compose, actual Undo/reorder/remove, one filing
per draft and neutral explanatory feedback. No external posting or invented engagement/payout. Existing Jobs
settlement remains responsible for server receipt. Full-landscape workspace, localized prose,136unit controls,
no-payout practice replay and bounded native helper are staged. Five new files plus narrow Jobs/controller patches.
Exact UI/App asmdef reference checks and actual installed Android-dev/Editor compilation pass.256decks/1280threads
pass independent permutation/edit/deadline/score tests. Native viewport/text/control/input/50second review remains
pending root. No live Assets, contract or backend changes, no Unity/device/network writes by this agent.

Spaces native v2 localized its failure to nonzero PCM: one subscribed track/stream,610source frames,555receive
callbacks,0nonzero, sourcevolume0, DSP advancing. v3 staged at `spaces-native-v3-20261006` tests owned-sourcegain1
only after an active clearing meter is confirmed after SDK AudioProbe in public component order. Runtime source
and deadlines remain unchanged; actual compile/patch checks pass. No claim of voice acceptance until native rerun.

### Native acceptance and Spaces UI follow-up

- Root reports Perps human-copy native v6 passed all six fixture screens, geometry and actual75.5margin/4x/Short callbacks. Earlier failures remain recorded; no live order acceptance is inferred.
- Root native Spaces media v3 passed:9560nonzero samples/8receive callbacks, roster stream unsubscribe/rebuild, repeateddispose and two actual lateConnect completions drained;0runtimeerrors/backgroundrestored. v2's owned zero-gain receive probe was the failed measurement boundary. ProductionSpacesAudio unchanged. PhysicalAndroidmic/gameSpacesroutes remain unverified.
- Thread first native failed strict metadata text measurement before playthrough; pre-cancel/detach/cancel passed and initial three fragments/slots visibly fit. Staged one-USS narrow revision in `thread-native-v2-20261006` gives Thread context/tally atomic nowrap/nonshrink; all prose and all gates unchanged. Actual UI/App Android-dev/Editor compilation and patch checks passed; native rerun pending.
- `spaces-ui-review-20261006` stages four UI/review files and two current narrow catalog/controller patches (Api.Spaces alreadylive). Conversation-first list/disclosed hosting, concise ambiguous-state copy, safe view-detach lifecycle and isolated watermarked eight-state native helper. Fixture resolves no transport/session/audio; safe real callbacks only, writes/mic disabled. InstalledUI/App compilation passes Android-development/Editor/production with exactasmdefrefs and patch checks. NativeUI/physicalinput/routeacceptance remain pending root.

### Accepted UI and expanded Thread replay content

- Root nativeThreadv2 passed the full50.033second sequence,40/50with one wrong order, realUndo/reorder/remove and strictlayout. Before import, root rejected an overbroad generatedUSS selector edit; the correctedpatch was exactly one additive atomicmetadata rule and preserved prose byte-for-byte. RootnativeSpacesUI passed alleightfixtures, touch/text bounds and actuallocalcallbacks with no API/session/audioobjectresolved. Native receipts live in `/tmp/ct-thread-native-v2-20261006` and `/tmp/ct-spaces-ui-native-20261006`; these are not server/microphone acceptance.
- `thread-pool-20261006` stages30originalfictional stories/fiveuniquepernormalround and explicit fixed-five developmentoracle. Independentagenteditorial feedback led to threeprose revisions; no humanplaytest claimed. SourcecompiledUI/App Androiddev/Editor/prod;1024decks/5120drafts, fullpool/permutation coverage, deterministicseed,256oldfixturesequence comparisons, strictscoring/deadlines and90uniqueconcise localizedfragments passed. Originalfive-only negativecontrol fails fullpoolreachability. Sixnarrowpatches apply; no liveAssets changes. Newguarded30storynativepreview cancelseachattempt/noeconomics; nativeexpandedcontentpendingrootafterphonevalidation.

### Read-only Seeker pacing audit

`ct-android-pacing-audit-20261006.md` records exact startup30Hz sample vs later60Hz DisplayDeviceInfo and independently recalculated2.074second/60.2696fps SurfaceFlinger actual-presentation sample. Full165.379steady seconds remain Update timing only. Verifiedlibgame loads before the two Swappy lookup messages; pinnedofficial2.1.2Java catches/logs the auxiliary load attempt and continues, with internalDEX native registration in JNIUtil. WrapperInit success itself only wires function pointers. No automatic harmful/harmless claim or settings/packagechange. CurrentUnity6.3recommendedGameActivity4.4.0 matchesexport/log. NoUnity/ADB/liveAssets actions by this agent.

## Normal phone audit and contained reliability candidates

- Reproduced normal Home opening failure with actual bundled `needs.actions` array and installed Newtonsoft DLL: old string indexing throws before buttons render. Root imported staged array validation + localized unavailable state, per-view cancellation, one-flight gate and identity/late-response protections after 79 managed checks and actual UI compile in three modes. No transport/economic changes.
- Root imported separate PhoneShell ownership disposal with normal/review teardown call sites after 1,104 actual-shell/Bus managed checks and a failing no-unsubscribe negative control. Close remains reusable; Dispose is idempotent and clears owner subscriptions.
- Actual native Home helper v1 FAILED on an incomplete generated Me fixture; v2 supplies every required field and has an executable Generated-DLL constructor preflight. V2 FAILED at action geometry before requests; services restored, zero runtime errors. V3 stages exact geometry diagnostics with unchanged124px panel gate. Native Home acceptance remains pending, not passed.
- Separate Jobs catalog candidate owns/cancels/bounds reads and provides a genuine retry action; paid Apply/RunShift/MiniGameHost byte-identical. Three-mode UI compile and21 managed lifecycle/recovery tests pass, including negative cancellation control. Staged outside Assets; no native/economic calls.
- Detailed ranked audit, current official docs and limitations: `docs/progress/ct-phone-reliability-audit-20261006.md`. Android pacing assessment remains `docs/progress/ct-android-pacing-audit-20261006.md`; no speculative package/settings change was made from Swappy log lines.

- Home v4 native receipt subsequently PASSED (`/tmp/ct-home-native-v4-20261006/home-review.json`): five local fixture requests, zero runtime errors, actual30.600s timeout, restored Services, three real-shell lifecycle cycles. Read receipt and visually inspected timeout/unavailable captures. This bounds acceptance to native Editor fixture UI/callback/lifetime behavior, not real server settlement or physical Android input.

- Status correction from root: Jobs catalog lifetime/Retry candidate was verified and integrated before Android v2. Earlier staged-only status is superseded; focused native Jobs catalog timing acceptance remains pending.

### Flow/Thread follow-ups frozen, native queue owned by root

- `flow-lifetime-20261006`: controller destruction-token waits, poll/update/kick ownership, direct UI cleanup, duplicate action guards and preserved explicit-logout protocol.145 actual-method/real-Bus managed assertions plus deletion mutation control; full App3mode compilation. No native acceptance yet. Overlay/service generation ownership remains separately documented in `ct-flow-ownership-audit-20261006.md`; no broader expansion authorized now.
- `thread-feedback-scroll-20261006`: actual Seeker successful-publication capture hid feedback below viewport. Geometry-aware guarded next-frame scroll plus BOTH-label viewport oracle staged/frozen and root importing for native verification. Existing UnityScrollTo already has its own deferred path; exact failure cause remains unproven. Prior probes measured text boxes, not feedback viewport visibility.
- `jobs-native-review-20261006`: actual Jobs/Phone local read-only fixture for actual30s deadline/Retry/error/close/identity/reopen, no Apply/Work and zero allowed mutations. App3mode compile passed; native pending.
- `flow-native-review-20261006`: real disabled-before-Start Flow MonoBehaviour/Awake/OnDestroy/destroy token, actual scheduled poll and private real-Bus probe;3cycles, local Api trap/restoration. App3mode compile passed; native pending. Normal Start's known overlay ownership and online service/wallet paths explicitly outside this fixture.

### Native follow-up receipts read — Flow and Thread

Root integrated Flow ownership source/helper and merged Jobs/Flow guarded hooks. Flow native receipt passed three actual Awake→Destroy cycles, zero transport attempts/runtime errors, restored services and maximum cancellation0.202463709s. Actual poll cancellation/owned real-Bus probe/native token accepted; disabled-before-Start limits exclude overlays, online wallet/session/room lifecycle, full120s timer expiry and Android process behavior.

Thread feedback native full round passed50.018590417s,40/50 with an intentional wrong order, actual edit/publish controls and new both-label viewport assertions. Independently viewed correct-feedback capture; explanation is visible. Android follow-up remains pending. Jobs native catalog timing run is active without a completed receipt at this update. No broader source changes undertaken.

- Jobs native v1 then completed **FAILED** on the helper's single-log assumption: existing Core.Log.Exception emits one Error summary plus one Exception. Actual30.4427s timeout/Retry and preceding owner checks passed; zero unexpected errors/mutations, services restored. V2 helper only counts/requires exactly1ofeach; full App3mode compile passed, native error→Retry→success acceptance pending. No production logging changes.

### Moderate thermal response proposed from actual phone evidence

The v2 app log records aggregate Android status2/AP0 while target FPS stayed60. Preserved HAL data shows SKIN40.338°C/status2 against this device's moderate40°C threshold, despite CPU/GPU status0; this is reported sensor data, not an independent surface measurement. Staged `thermal-moderate-20261006` adds an Android-player-only30FPS cap at existing severity1, retaining cool60, moderate visual quality and existing30second recovery/stricter budgets.72 new model checks +27 existing checks, negative control and actual World3mode/Editor compilation pass. No native/device/live edit. Heat reduction efficacy, actual presentation cadence and sustained play remain pending; no sole-cause or indefinite-stability claim. Full evidence/official refs in `ct-moderate-thermal-audit-20261006.md`.

### Jobs catalog native v2 receipt archived

Copied the bounded receipt and five watermarked captures to `docs/progress/screens/jobs-catalog-native-v2-20261006/`, preserving original bytes and recording hashes. Native v2 passed the actual 30.022867417-second deadline, Retry, view/identity lifetime and finite-error recovery: eight local reads, zero mutation attempts, exactly one expected Error summary plus one Exception, zero unexpected runtime errors and restored services. Timeout/recovered captures were visually inspected. Real backend/network cancellation, physical phone input and Android lifecycle remain outside this fixture. Earlier v1 failure is retained in the phone audit.

The frozen thermal stage already includes the extended native PerfManager wiring probe as its third patch, `ThermalBudgetReview.cs.patch`. Entry point `CTLife.EditorTools.ThermalBudgetReview.Check` requires an idle Editor with no PerfManager. It simulates the Android opt-in locally, checks moderate 30 FPS with unchanged render/animation/sleep, unknown-read retention, delayed recovery, stricter states and teardown restoration. Root owns import/run; no native pass or heat efficacy is claimed yet.

## Overlay ownership stage — 2026-10-06

Completed the bounded source audit and staged explicit owners for ToastLayer, WorldLabels, RektCard, ProfileCard, BusyOverlay and Toast's IncidentPresentation. Flow, FrontendReview and Spike retain their handles; review guards post-profile-await creation. Profile cancellation is read-only; started Block/Report retain original request semantics, with per-sheet duplicate/stale completion guards. Full actual UI/App three-mode compile and exact live baseline / narrow patch checks pass. Independent ProfileCard review found no blocking issue. Initial native-helper compile caught an ambiguous Position type, corrected by explicit UI Toolkit qualification.

Candidate: `art/gameplay-candidates/overlay-lifetime-20261006/`. New compile-guarded `OverlayOwnershipReview` uses actual production overlays, Bus/updater counts, detach/NavigationSubmit callbacks, controlled non-cooperative reads and local mutation completions, plus three real controller OnDestroy paths with Start disabled. Native execution, integration and phone work remain root-owned and pending. See `docs/progress/ct-overlay-ownership-audit-20261006.md` for exact remaining limits.

Also recorded root's bounded thermal API wiring pass from `docs/progress/screens/thermal-moderate-native-20261006/receipt.json`; physical cooling efficacy and post-change Seeker presentation still require measurement.

## Realtime startup ownership stage — 2026-10-06

Confirmed exact late-room publication and late-disconnect successor-clobber interleavings. Staged four narrow patches plus internal RoomJoinSlot outside Assets. Flow passes its existing destruction token to City/Market joins; slots invalidate pending attempts and capture old handles before awaits; RoomChannel rejects/drains an obsolete raw SDK result before handler registration. No backend, package, socket, account or live source change. Actual Net/World/UI/App three-mode compilation passes. Sixty-five deterministic assertions against actual candidate slot/service/channel source and two negative controls pass with SDK/Unity-timer doubles. See `docs/progress/ct-startup-room-ownership-audit-20261006.md` and candidate README for exact integration and limits.

VenueSession/loader/proximity-voice late ownership, same-Flow sign-out generation, shared service replacement and physical socket/native acceptance remain open; this stage does not claim complete startup cleanup. Independently read root's passing native overlay receipt and updated its audit with the bounded evidence.

## 2026-10-07 — venue and proximity voice candidate frozen

Staged [venue/voice lifetime candidate](../../art/gameplay-candidates/venue-voice-lifetime-20261006/README.md), five narrow existing-file patches and one new wait helper. The exact manifest is `f377d5b4209ec2588bcc480d977e64a729a0a467cf5ba70e588e32b85214f29a`. Actual assemblies compile in Editor, Android development and production; 133 controlled source assertions and eight negative controls pass. Root's review identified the failed-Leave cleanup poison; the frozen revision independently attempts owned releases, surfaces collected failure, and permits the next visit. Failed native unloading remains a real unresolved release failure, not a claimed success. Native scene, SDK and device acceptance are pending; the stage contains an explicit plan, not a run receipt. World agent was unavailable for independent review of this revision.

Next authorized work returns to Phase 1 breadth: six distinct additional local shift games, paid routing gated on agreed registry IDs. The existing imported three games do not satisfy the spec's nine games.

## 2026-10-07 — gameplay breadth and seated presentation

[Proposal] [Six additional Phase 1 games](../fe-contract-proposals/phase1-nine-games-20261007.md) now have concrete IDs, durations, caps and distinct local rules. The paid server still assigns one existing configured ID; no new paid route or registry change is claimed.

[Raid Router candidate](../../art/gameplay-candidates/raid-router-20261007/README.md) supplies the first continuous arcade game: one shield, three lanes, 24 seeded packets, three escalating waves, 45 active seconds, readable clean streak and last-moment-save feedback, pause/focus-loss handling and replay. The fourth guarded practice Jobs card and a native review helper are staged. Full actual UI/App assemblies compile in all three modes; 128 seeded model decks and independent score/deadline/pause checks pass (12,818 assertions, primarily per-packet invariants), along with three negative controls. Native visual, actual touch, OS background behavior and performance acceptance remain pending. The latest Frontend review hook was rebased after root's venue-lifetime integration.

[Occupied-spot presentation adapter](../../art/gameplay-candidates/occupied-spot-presentation-20261007/README.md) is separately frozen. Exact wire-ID confirmations with the backend's generic action may use their registered pose for presentation; explicit other actions and emote one-shots remain untouched. Actual World compile passes three modes, with 25 source assertions and one negative control. No native or online acknowledgement claim; root/motion must merge only the two small SetBase substitutions into their pending traversal source.

Venue/voice revision 2 superseded preserved v1 to propagate missing-root scene-release errors instead of swallowing them into fallback. Root reported a native VenueLifetimeReview pass at `/tmp/ct-venue-lifetime-native-20261007/venue-lifetime-review.json`: actual Studio→Timeline additive transitions, worker cancellation, held controlled join replacement/late result, real destruction and subsequent entry, zero runtime errors, restored services/resolver and reloaded original venue. This does not transfer SDK/RTC/microphone acceptance to the new proximity connection implementation.

## 2026-10-07 — Role Lock practice candidate frozen

[Role Lock](../../art/gameplay-candidates/role-lock-20261007/README.md) adds the fifth local practice game: five explicit least-privilege puzzles, nine tactile switches, reversible edits before one Lock, exact-grid scoring, 50 active seconds, pause/focus/Resume, finite result and replay. Original fictional tasks cover launch access, calls, bot cleanup, demos and support. Board/row/column order varies; no real admin API, external post, server assignment or payout is invented. Existing CT/Manrope tokens are reused; Raid Router remains root-owned.

Full actual UI/App compilation passed Editor, Android development and production with exact asmdef references. The model passed 4,320 assertions, including all 512 grids on every authored board, independent task-copy checks, deadline/pause/cancel and stale/duplicate controls. Three deliberate regressions failed their expected tests. The guarded native helper is compiled but has not run: actual fifth Jobs entry, all five boards, switch travel/lock feedback, text/viewport/touch bounds, 80/100 with one wrong panel, full 50 active seconds, and unscripted replay are still pending root execution and visual review. Physical Android delivery and human playtesting remain unverified.

Role Lock v2 supersedes the preserved, unimported v1 after root's product decision: 50 seconds is the active-time ceiling, and fifth Lock completes immediately. Receipt Match will use the same rule. Actual three-mode UI/App compilation, 4,323 model assertions and four negative controls pass. The native probe now paces five boards at about 30 active seconds and requires an immediate result; idle full-deadline and paused-time behavior are separately tested. Score/cancel/paid boundaries are unchanged. The proposal records this clarification. Native acceptance remains pending; see [v2 candidate](../../art/gameplay-candidates/role-lock-v2-20261007/README.md).

## 2026-10-07 — Receipt Match candidate frozen

[Receipt Match](../../art/gameplay-candidates/receipt-match-20261007/README.md) stages the sixth local practice game: original fictional receipt slips and statement rows, intentional amount/reference/date collisions, tap-to-pair, reversible swaps, tap-to-unpair, Clear and one File per board. Five points per correct pair, maximum 100; fifth File finishes immediately and 50 active seconds is the ceiling. No account/payment/API/config/paid route changes. Shared owners are based on root's newly integrated Role Lock v2 hashes.

Actual full UI/App compilation passed Editor, Android development and production. 12,253 model assertions include all 209 injective partial assignments for every board, exact visible-field scoring, unique solution, swap/cancel/deadline boundaries and six failing mutation controls. Initial compile issues are retained with their corrections; no native crash is inferred. The compiled native probe exercises the actual sixth entry, all five boards, real pair/swap/unpair/file controls, strict geometry and a paced ~30-second 90/100 result with one deliberate two-pair swap. Native layout, physical touch and human usability remain pending.

Root separately reported Role Lock v2 native PASS (31.0846 seconds wall, 1.02546 paused, 80/100 with one extra grant) at `screens/role-lock-v2-native-20261007`. That is bounded technical UI/lifecycle acceptance; root explicitly judged the current flat settings-like skin insufficient. A separate presentation revision is next, with no rule/model changes.

Receipt Match's first native run failed the strict overlap gate after ownership/pause checks passed. Actual selected-paper capture and source identify the global button active transform (`0 6px`) overriding the selected rule while slips have a 4-unit gap. [Fit revision 2](../../art/gameplay-candidates/receipt-match-fit2-20261007/README.md) preserves the failure evidence, scopes vertical motion/focus geometry, explicitly uses Manrope for metadata and expands native bounds diagnostics without relaxing the overlap gate. Full actual UI/App compilation passes three modes; corrected native geometry/full play acceptance is pending root. The reconciliation model is unchanged.

## 2026-10-07 — Role Lock mechanical presentation staged

Independently read the actual v2 receipt and viewed selected/error frames. The native technical pass is preserved, while its settings-like appearance is explicitly unaccepted. [Mechanical panel candidate](../../art/gameplay-candidates/role-lock-panel-20261007/README.md) stages four narrow patches: inset gripped sliders, indicator lights, case/control ledges, a real moving lock shackle and compact hierarchy. Existing palette/Manrope and all model/score/timing/owner bytes remain unchanged; decorative elements ignore picking. A singular-count copy issue is also corrected.

Full actual UI/App three-mode compilation and exact baseline/model protection checks pass. The native helper retains strict geometry and gameplay gates, adding actual light-change and seal-motion measurements/captures. New appearance, geometry, motion and physical phone acceptance are pending root. Raid's native arena frame was also inspected read-only: lanes/shield/RAID-CHAT distinction communicate the mechanic; inherited packet Lilita was flagged, and root owns its scoped typography correction.


Receipt Match fit2 native evidence has now been read directly: [receipt](screens/receipt-match-fit2-native-20261007/receipt-match-review.json) reports PASS, 31.3623 seconds wall, 1.2058 seconds paused, five filed boards, 18 correct/two deliberately wrong pairs and 90/100. Strict touch/text/viewport, selection/swap/File, pause/cancel/replay and immediate fifth completion passed. Root visually inspected selected/filed frames and accepted this bounded presentation for continued work. Physical Seeker input and human usability remain pending.

Root also reported the Role Lock mechanical native probe passed and an actual desktop replay correctly registered Listener with no grants, Host with queue/edit, and Stage mod with mute, followed by a sealed +20 result. That human replay timed out normally after one board at 20/100; root intentionally stopped Play and quit. This is one desktop board, not complete device or content acceptance. The separate native evidence is being retained by root.


## 2026-10-07 — Live Cues candidate frozen

[Live Cues](../../art/gameplay-candidates/live-cues-20261007/README.md) stages the seventh local practice game: a fictional show console with Camera/Caption/Applause pads, visible next cue, moving timing rail and an explicit 800 ms window. Twenty unique scenes per seeded show come from 30 original lines. Each cue accepts one action for 5 points on time; early/wrong/missed inputs score zero. The continuous show lasts 45 active seconds, with practice pause/cancel/shared ownership retained. No microphone, real audience, external post, API, payout or imported contract change.

Actual UI/App Editor, Android development and production compilation passed. 10,714 model assertions and six deliberate failing regressions cover finite scoring, exact timing edges, stale input, paused time and deadline. The initial captured-button compilation error is preserved and corrected. Independent editorial review clarified three scene cues. Five new files plus three narrow owner patches are frozen against current Receipt Match owner hashes. The compiled native helper must still run: actual seventh Jobs entry, all 20 previews, three real controls, strict geometry, 45 active seconds and an expected 85/100 with one early/wrong/missed cue. No native, physical phone or player-retention acceptance is claimed.


## 2026-10-07 — Runway Plan candidate frozen

[Runway Plan](../../art/gameplay-candidates/runway-plan-20261007/README.md) stages the eighth local practice game, sequentially after Live Cues. Four fictional budget boards use five cost/capacity preset strips, a three-change allowance, exact Undo and explicit budget/service constraints. Any valid allocation earns 25 points; fourth File completes immediately, with a 60-active-second ceiling otherwise. Existing practice cancellation/pause ownership is retained. No real financial data, account mutation or paid route is added.

Private-output compiler inspection and full actual UI/App three-mode compilation pass. 3,474 assertions cover all reachable allocations and concrete lifecycle/scoring boundaries; six negative controls fail as intended. The compiled native helper remains pending root: actual eighth entry, all four boards, preset changes/exhaustion/Undo, strict geometry, one invalid service plan and immediate 75/100 completion at about 28 active seconds. Root's prior assembly recovery interruption paused compilers; source drafting continued outside Assets, then compilation resumed only after root reported clean recovery. This verifier does not write Library or shared compile outputs.


Live Cues first native run failed before scoring: WAIT's measured 25.18-unit text exceeded its 19.79-unit content height. Entry/cancellation passed. Actual failure capture also shows Pause/End below the panel. [Fit2](../../art/gameplay-candidates/live-cues-fit2-20261007/README.md) preserves both, grows the rail-label row, uses the overlay's full padded height and reduces one brief margin. The helper adds panel-content containment without relaxing existing gates. Runtime/model/strings/owners remain protected; actual three-mode UI/App compilation passes, including production with DEVELOPMENT_BUILD and UNITY_EDITOR* stripped. Native fit and gameplay remain pending, and root explicitly rejected the current flat blue presentation as final art.

Compiler-scope clarification: earlier stage verifiers added CTLIFE_PROD but retained a cached DEVELOPMENT_BUILD symbol. Their `(UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD` practice/review classes were excluded, but unrelated development-only branches remained included. Those earlier 'production' labels were broader than that check established. Frozen artifacts remain preserved; new verifiers use clean production symbols, and root owns the integrated production check.


## 2026-10-07 — Clip Cut candidate frozen

[Clip Cut](../../art/gameplay-candidates/clip-cut-20261007/README.md) stages the ninth local practice game, sequentially after Runway Plan. Five original eight-frame text storyboards support large IN/OUT handles, tap-to-trim, carried crossed boundaries, Reset and an actual looping selected-range storyboard preview. One Cut per reel, 20 points for its exact concise Setup→Payoff interval; fifth Cut ends immediately or the 50-active-second ceiling expires. Solutions vary from three to five frames. No recording, acquired media, external posting, API, contract or economic changes.

Actual UI/App Editor/development/clean-production compilation passed, with explicit output-option and production-symbol checks. 791 assertions cover all 36 intervals per reel and focused ownership/scoring boundaries; seven negative controls fail as intended. The compiled native helper still needs root execution: actual ninth card, all five reels, real handle/preview/cancel controls, strict panel/text/touch bounds and 80/100 with one deliberate extra-frame cut at about 25 active seconds. Nine local implementations do not establish nine paid assignments, mobile usability or completed Phase 1.


Root imported Clip Cut and the four [count-neutral result summaries](../../art/gameplay-candidates/practice-result-copy-20261007/README.md). A subsequent pre-import verifier correctly refused changed baseline hashes; this was not a compiler failure. The separate [post-import check](../../art/gameplay-candidates/clip-cut-post-import-20261007/README.md) verifies all imported Clip bytes, compiles actual live UI/App sources with clean production symbols, and passes the original 791 assertions/seven negative controls against the live model/strings. Original frozen artifacts are unchanged. Native Clip/Runway/Live Cues follow-up remains owned by root; no phone or paid-route acceptance is transferred from this source pass.


### Native failures retained; three bounded review/fit revisions staged — 7 October

Live Cues fit2 passed the early text frames, but the deliberate early input scored a hit. The original helper performed a PNG capture before aiming just 150 ms before Opens and checked only Closes. [Timing revision 3](../../art/gameplay-candidates/live-cues-timing3-20261007/README.md) removes that capture from the early path and records before-dispatch and synchronous consumed-at witnesses, with strict early/window/marker gates. Gameplay rules and window remain unchanged. This was a failed native attempt, not a scoring defect established by the evidence.

Runway failed before scoring with zero content height for Full, and its actual capture showed footer overflow. [Fit revision 2](../../art/gameplay-candidates/runway-plan-fit2-20261007/README.md) gives each preset two intrinsic rows, puts constraint label/value on one line, uses the existing footer for disclosure, and adds strict panel-content gates. All fonts and 124-unit minimum targets are preserved. Clip failed disclosure containment by 4.50 panel units; its [fit revision 2](../../art/gameplay-candidates/clip-cut-fit2-20261007/README.md) recovers 16 units through four gap reductions only. All failed receipts/screenshots are preserved in their stage evidence folders.

All three staged revisions compile against actual UI/App references in Editor, development and clean production, with temporary private outputs only. Native reruns and physical phone readability remain pending. Root measured Seeker 1200×2670 at density480: existing 22–28 reference-unit body labels at 1.111 scale are roughly 24–31 physical pixels, which warrants a separate composition/readability pass; no font reduction was used to address these failures. Live Cues flat blue art remains unaccepted.
