# Normal phone reliability audit — 2026-10-06

Scope: PhoneShell, Jobs, Bank, Home and Profile; normal source paths, with review-only transport identified separately. Read-only audit; no Unity/ADB/network writes. Source findings are not claims that an Android process crashed.

## Ranked findings

| Priority | Evidence and reproduction | Impact / proposed next step |
|---|---|---|
| P1 — reproduced | `UI/Phone/Apps/CoreApps.cs:137` indexes `cfg["actions"]["eat"]`. Both `contract/config/needs.json` and `Resources/Contract/config_snapshot.json` contain an array. Running that expression with the installed Newtonsoft DLL and the actual bundled snapshot throws `ArgumentException: Accessed JArray values with invalid key value: "eat". Int32 array index expected.` Receipt `/tmp/ct-phone-audit-repro-20261006/receipt.txt`. | Normal Home cannot build with this configuration. `PhoneShell.cs:184–192` catches it and shows a generic error; this is not an unhandled process crash. Staged correction reads/validates the actual array and preserves configured IDs/prices. |
| P1 — source-backed latent issue behind the opening failure | Home's existing callback has no busy guard/token/identity check (`CoreApps.cs:138–153`); `PhoneApp.Closed` is empty. If the config failure is bypassed, two presses start two requests; completing their snapshots in reverse order applies the older one last. Closing or reauthenticating leaves the same captured `PlayerProfile` accessible. `PlayerProfile.cs:35–53` has no owner check and bootstrap registers it once (`GameBootstrap.cs:48`). | [Inference] A late response can replace newer local needs/cash or update a different local signed-in profile, given that interleaving. This does not establish duplicate financial charges: current four actions cost zero, and backend `application/needs.ts:73–84` returns unchanged cash. Staged one-flight, cancellation and ownership guards address this local path; no shared transport changes. |
| P2 — source-backed liveness gap | Jobs `Load` invokes `Api.Careers()` without a token/deadline (`JobsApp.cs:37–71`). `Closed` invalidates only list/version. `RestClient.cs:149–156` retries SERVER_BUSY / applicable 503 indefinitely until cancellation. Reproduce condition: respond SERVER_BUSY repeatedly, close/reopen Jobs. | The old GET remains alive and another GET starts on reopen; the loading state has no deadline. If a finite failure occurs, only an error label appears, with no inline Retry. Proposal: own/cancel/bound only catalog reads and add Retry; do not change shift settlement behavior. Not executed against a live server. |
| P2 — source-backed retained subscription | PhoneShell subscribes to `DegradedChangedSignal` in its constructor (`PhoneShell.cs:46–50`) and never disposes `_subs`. Normal `FlowController.BuildHudAndPhone` creates a fresh shell on each EnterGame; SignOut only closes the old shell (`FlowController.cs:225–229,313–324`). Review teardown also only closes. `Bus` retains delegates strongly (`Core/Services/Bus.cs:12–20`). | Every new shell retains its callback and view/app graph. Closed shells skip the callback's UI body, but remain referenced. Proposal: separate idempotent PhoneShell.Dispose plus normal/review ownership call sites and subscription-count checks. No quantified device memory/crash claim. |
| P3 — recovery/display limits | Bank and Profile construct labels from current PlayerProfile once (`CoreApps.cs:53–61,177–193` in baseline). They contain no requests, loading states or ProfileChanged subscriptions. | There is no async exception path inside these Open methods. If the shared profile changes while displayed, labels remain stale until reopening. Reopen is current recovery; a narrow subscription can be considered later. No invented backend failure. |
| P3 — sign-out dependency to track separately | Settings invokes the Action that starts `_ = SignOut()`. `FlowController.cs:317–319` awaits venue exit, realtime disconnect and auth sign-out before replacing the UI. Auth logout has no caller deadline (`AuthService.cs:126–141`), and uses the same busy-retry transport. | [Inference] Repeated SERVER_BUSY on logout can delay sign-out indefinitely; a fault in a prior awaited dependency can fault the fire-and-forget Task without a player recovery view. This was not reproduced; do not label it an Android crash. Needs a separate auth/flow cancellation design, not a Home patch. |

## What is already handled

- Jobs success/error callbacks check both `_loadVersion` and list identity; ordinary late catalog responses cannot replace a reopened list. Missing cancellation is a resource/liveness issue, not a stale-view-write finding.
- Apply and paid shifts hold `_busy`, cancel on session identity change and bound operations through ShiftReliability. Completion retries only explicit `SHIFT_NOT_FINISHED`; uncertain settlement is not blindly submitted again. Preserve these protections.
- Home's old awaited request was caught locally. `async void` alone is not evidence of a crash. The shell's synchronous catch cannot catch later async-void failures, but this audit did not demonstrate an uncaught ordinary API failure in these requested screens.
- ReviewTransport deliberately rejects unsupported calls. Jobs.Load catches its unsupported Careers request and AppUi.Err logs an exception/error. The generic error and red console in offline review are handled fixture behavior, not proof of production transport or Android process failure. The separate guarded practice hub supplies actual local games without fabricated Careers/payout responses.

## Staged Home correction

Candidate: `art/gameplay-candidates/home-reliability-20261006/` (outside Assets). Removes only HomeApp's old class from CoreApps through a narrow patch and adds HomeApp/HomeActions/HomeStrings under the same UI namespace. No packages/backend/API/transport changes.

- Requires an actions array; rejects missing/duplicate/blank IDs, absent required actions and missing/non-integer/negative/overflow prices. No made-up free-price fallback. Invalid config shows a clear unavailable state with no actionable buttons.
- One request survives as the gate until completion even if its cancelled view closes/reopens. Closing, root detach and identity change cancel the caller token and release the session subscription. Child detach does not cancel the whole page.
- A 30-second request cancellation bound applies to normal RestClient, which accepts that token and aborts the UnityWebRequest. A late non-cooperative response still cannot apply after close/reauth. No new retries are introduced; shared transport's existing per-request idempotency/retry behavior remains unchanged.
- Confirmed server values alone update the profile. Lost/cancelled/malformed/5xx receipts say the action was not confirmed and needs may have changed. Controls recover for a deliberate new action; the UI does not resubmit automatically. This is not an audit of backend idempotency or concurrent server snapshot versioning.

Verification: exact installed UI assembly refs; full UI source compiles with Android-development, Editor and production defines. 79 controlled managed checks use actual bundled config and actual staged HomeApp methods with UI/transport doubles: original exception; malformed config; repeat taps; confirmed response; child/root detach; closed/reauth late completion; reopen; network ambiguity; cancellation; synchronous request failure. Native layout/physical input and real transport deadline remain pending. No economic calls were made.

## Current official references opened

- [Newtonsoft querying LINQ to JSON](https://www.newtonsoft.com/json/help/html/QueryingLINQtoJSON.htm): object property vs array element access and LINQ queries. Actual exception additionally reproduced against installed DLL.
- [Unity 6.3 panel events](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-Panel-Events.html): panel attach/detach lifecycle; the candidate filters the event target to its root.
- [Microsoft C# async return types](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-return-types): async-void callers cannot await/catch its eventual failure; event-handler faults require local handling.
- [CancellationTokenSource.CancelAfter](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokensource.cancelafter?view=net-9.0): scheduled cancellation is cooperative, not proof a server operation did not happen.

## Follow-up integration and acceptance status

- Root imported Home correction after manifest verification. Compiler/79 managed checks passed; native v1 failed because the **review fixture** omitted required generated Me fields. A separate v2 copied complete isolated review fields; actual Generated-DLL constructor execution passed. V2 then failed the strict native Home action-size gate before any request. Screenshot is visibly readable, but exact bounds were absent, so v3 adds geometry diagnostics without relaxing the gate. Home native acceptance is **not yet passed**.
- Root imported the separate PhoneShell IDisposable and normal/review owner call-site patches. Staged `phone-lifetime-20261006` executed 1,104 actual-shell/actual-Bus managed checks over 100 cycles, including repeat/throwing teardown; removing unsubscription fails a negative control. Native helper includes three real-shell cycle checks, still pending completion after the Home layout gate.
- Separate `jobs-catalog-lifetime-20261006` is staged only: owned/bounded catalog read and explicit Retry. All paid/economic code from Apply through MiniGameHost is byte-identical to baseline. Full UI compiles three modes; 21 catalog-method managed assertions plus owner-cancellation negative control pass. Native behavior/deadline pending; do not count this as shipped.
- The two Thread CS4014 warnings were deliberately unawaited Task-returning factories expected to reject synchronously, not async layout checks. Root integrated explicit discards/comments preserving those assertions. No previous layout receipt is invalidated by that clarification; expanded pool's separate full50second regression remains required.

### Native Home geometry diagnosis

V3 recorded actual bounds: authored minimum124; world/layout/resolved height123.222961; observed scale1.11180556px/unit. The rendered target is137px versus ideal137.864px, a0.864px deficit. Root requested a bounded physical-pixel gate rather than a production layout change. Staged v4 retains authored min-height>=124 and requires both rendered dimensions>=floor(124×observed scale), with0.001px arithmetic epsilon. It accepts observed137px and rejects136.9px and authored minimum123.99. Text-clipping gates remain byte-identical. App compilation and pure boundary checks passed; the full native lifecycle/30second timeout rerun remains pending at this update.

### Native Home v4 accepted within its stated boundary

Read `/tmp/ct-home-native-v4-20261006/home-review.json` after root's run: `pass:true`, five controlled local requests, zero runtime errors, actual client timeout30.600044375s, original Services restored. Checks include actual bundled config/four controls, duplicate callback, child/root detach, close/reopen, late non-cooperative response, changed identity, malformed config/recovery and three real-shell dispose/reopen cycles with Bus counts. Four native captures were written. Timeout and unavailable captures were visually inspected: readable controls, uncertainty text, and no clickable actions for missing config.

This accepts the tested native Editor UI/callback/lifetime behavior. Real HTTP abort, backend settlement, physical touch and Android background/process lifecycle remain outside this fixture. Earlier v1/v2/v3 failures remain recorded above rather than replaced with an uninterrupted-pass claim.

### Jobs integration status correction

Root subsequently verified and imported the separate Jobs catalog candidate before the Android v2 build. The earlier final/staged-only status is superseded: source is now integrated. Its three-mode compile,21 managed assertions and cancellation negative control passed; the dedicated native Jobs catalog timing/recovery fixture remains pending. Paid shift/economic code remains byte-identical to its baseline.

### Native Thread feedback and Flow ownership follow-up

Root integrated the Flow controller ownership correction and separate guarded helper. Receipt `/tmp/ct-flow-ownership-native-20261006/flow-ownership-review.json` independently read: three native Awake→Destroy cycles, longest cancellation0.202463709s, zero transport attempts/runtime errors and restored services. Scope is actual destruction token, scheduled poll cancellation and owned private Bus probe; normal Start/online/wallet/overlays/Android lifecycle remain excluded. Full audit in `ct-flow-ownership-audit-20261006.md`.

The Thread publication feedback correction was integrated and the full native50second review passed: `/tmp/ct-thread-feedback-native-20261006/thread-review.json`, elapsed50.018590417s,40/50 with the intentional wrong order. Live helper source verifies both feedback labels wholly inside the composer viewport after actual Publish callbacks; previous helpers checked text boxes but not this viewport intersection. Independently viewed `feedback-order-right.png`: the explanation title and body are visible above the Next draft button. This accepts the measured native Editor scenario; the original Seeker failure still needs the pending Android follow-up with this build. No external posting or economic calls were used.

Root has integrated the Jobs native helper and started its separate catalog timing review. At this update only loading/empty captures existed; no completed Jobs native receipt was available, so no Jobs native pass is claimed.

### Jobs native v1 failed on probe log accounting

The completed receipt appeared after the prior progress note: `/tmp/ct-jobs-catalog-native-20261006/jobs-catalog-review.json` has `pass:false`, phase finite-error, expectedFixtureErrors2, unexpectedRuntimeErrors0, mutationAttempts0, servicesRestoredtrue, actual timeout30.442712292s. Earlier loading/empty, child/root detach, close/reopen, identity cancellation, late-response drop and timeout/Retry checks passed. Finite-error recovery after that assertion was not completed.

The helper incorrectly expected one log event. `Core/Util/Log.cs:17–20` deliberately calls Debug.LogError then Debug.LogException; bounded log lines848/901 match those two emissions for the same marker. This is not evidence of a duplicate Careers request. Root requested a narrow fixture-only correction: require exactly one expected marker Error and one Exception, retaining all other error gates. Staged v2 full App3mode compile passed; native rerun pending. Production logging remains unchanged. Red development-console output for a handled recoverable API failure is a separate UX concern, deferred.

### Jobs native v2 accepted within its stated boundary

Independently read and archived the [v2 receipt](screens/jobs-catalog-native-v2-20261006/jobs-catalog-review.json) and [five watermarked captures](screens/jobs-catalog-native-v2-20261006/). The actual registered JobsApp/PhoneShell passed loading/empty states, child versus root detach, close/reopen, identity change, late non-cooperative response rejection, an actual 30.022867417-second catalog timeout, explicit Retry and finite-error recovery. There were eight controlled local reads, zero mutation attempts and zero unexpected runtime errors; original services were restored.

The deliberate API failure produced exactly one Error summary and one Exception, matching the existing logger. The v1 assertion failure remains documented above; this is a corrected test expectation, not a duplicate-request correction or a phone-process crash. Production logging was not changed.

Visually inspected the timeout and recovered-catalog captures: timeout copy and Try again are readable; recovery displays the watermarked review career and its Apply control. Only Retry callbacks were invoked. Apply/Work, HTTP, wallet, persisted-session and payout operations were not performed. Native text and control bounds passed. This certifies the tested local native Editor catalog recovery/ownership behavior; real backend availability, actual network abort, physical phone input and Android lifecycle remain separate acceptance. No full app log was copied into the evidence directory.

### Overlay ownership candidate — source checks only

The [overlay ownership audit](ct-overlay-ownership-audit-20261006.md) identifies retained Start overlay subscriptions/updaters and an exact ProfileCard stale-close path. Nine narrow staged owner/runtime patches plus a guarded native fixture are outside Assets. Full actual UI/App compilation passes in Editor, Android development and production modes. Native whole-layer detach, stale response and actual controller destruction acceptance remains pending; this is not yet a shipped/runtime-accepted correction. Existing request payloads and started-mutation lifetimes are retained, with no new transport retry behavior.

### Overlay lifetime native receipt accepted

The [native overlay receipt](screens/overlay-ownership-native-20261006/overlay-ownership-review.json) now passes its defined lifecycle boundary: seven local reads, two local Block completions, one local Report completion, three real controller destruction cycles, exact subscription/updater cleanup, no unexpected requests/errors and original services/UI root restored. No HTTP or actual moderation/economic operation occurred. Full normal startup remained outside its disabled-Start owner checks; the [separate room ownership audit](ct-startup-room-ownership-audit-20261006.md) records those outstanding paths and a narrow, not-yet-integrated Realtime candidate.
