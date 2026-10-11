# Flow async ownership audit — 2026-10-06

Status: root integrated the controller-only correction from `art/gameplay-candidates/flow-lifetime-20261006` and the separately guarded native helper. The native destruction/poll/Bus boundary passed; normal-start/online/Android limits remain below. This agent made no live Assets edits; parent owns native/build/device. Read current `docs/AGENTS-BRIEF.md`; backend and full six-phase product scope unchanged.

## Confirmed source paths and controlled reproduction

| Finding | Baseline evidence | Candidate / evidence |
|---|---|---|
| Update callback retains Flow after destruction | `App/FlowController.cs` Start discards `Bus.Subscribe<UpdateRequiredSignal>` return; OnDestroy only disposes phone. Actual `Core/Services/Bus.cs` stores delegates strongly. | Owned Subscriptions disposed on teardown. Actual candidate+live Bus checks20 successful creation/destruction cycles, count1→0; removing cleanup fails negative control. |
| Kick handler retains Flow after disconnect/reentry | EnterGame attaches an anonymous City.Kicked handler and never removes it. | Stored channel/delegate; unbind on replacement, signout and destruction. Checks retained callback after teardown cannot trigger logout. |
| Sleeping poll can wake and issue another request after destruction | ConfigRefreshLoop checks `this != null` before120second await; no token or postwait check. | Cached destroy token passed to delay/config GET, check before request. Controlled timer checks cancellation while sleeping and no subsequent GET. Actual120second native wait not run. |
| Startup can create UI after destruction | Run/EnterGame await config, resume/profile and room connect with no owner guard. | Owner wait cancels promptly even for controlled noncooperative tasks; checks late config/profile/city completion does not advance auth/market/HUD. Late task faults are observed and logged by type. |
| Wallet callback/closed screen lifetime | async-void wallet callback only uses visual SetBusy; completion/finally can write after closure. | Explicit one-flight and owned screen guards; existing Auth token parameter used. Actual Flow method checks double invoke produces one request and delayed completion after teardown writes no screen. No signing payload or auth endpoints altered. |
| Fire-forgotten logout fault ownership | Settings/lobby/phone callbacks discard Tasks; no general async action error boundary. | Local guarded async callback catches owned failures; direct UI disposed on teardown. A logout already requested continues its unchanged exit/disconnect/auth sequence even after owner disappears; controlled test verifies revocation stage is not skipped. |

Compilation passed full actual App in Editor, Android development and production modes with exact assembly references.145 managed checks execute candidate methods against controlled service/UI/Unity doubles and the real Bus. At the initial source-verification stage no Unity/native/network/wallet or economic operation was run. The subsequent native result is recorded below. Compile success does not certify Android process stability.

## Material remaining boundaries

Five overlay constructors (`UI/Overlays/ToastLayer`, `WorldLabels`, `RektCard`, `ProfileCard`, `BusyOverlay`) discard their Bus/updater handles and lack Dispose. ToastLayer also discards its IncidentPresentation owner. These pre-existing objects remain retained after Flow teardown; the controller patch does not claim complete scene cleanup. Separate overlay-owned subscriptions/updaters and late ProfileCard requests should be corrected together.

`Net/Realtime/RealtimeService.ConnectCity/ConnectMarket/JoinVenue` and `World/Venue/VenueSession.Enter` expose no cancellation parameter. Their async continuations assign shared room/world state. Controller wait cancellation does not revoke those operations or certify native resource cleanup. Calling DisconnectAll from a late controller continuation could instead target a replacement owner's rooms, so that is not added here. Service-level operation generation/cancellation needs a separately reviewed change. The controlled late-city check explicitly makes no disconnect claim.

`AuthService` and `PlayerProfile` update shared state internally before returning. Existing Session ownership and any same-session concurrent snapshot ordering are outside this controller-only patch. No new live-owner timeout is applied to wallet/transport/logout. A kick/signout during a separately pending EnterGame remains a separate operation-generation race, not covered by destruction lifetime.

## Current primary references opened

- [Unity6.3 destroyCancellationToken](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/MonoBehaviour-destroyCancellationToken.html): destruction signal; cache the token before object destruction.
- [Unity6.3 asynchronous support](https://docs.unity3d.com/6000.3/Documentation/Manual/async-await-support.html): installed Unity asynchronous API context.
- [Microsoft task cancellation](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/cancel-an-async-task-or-a-list-of-tasks): cooperative tokens; cancellation is not rollback of an operation already sent.
- [Microsoft async return types](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-return-types): callers cannot await async-void event handlers; local exception boundaries are required.

## Native ownership acceptance — bounded pass

Independently read `/tmp/ct-flow-ownership-native-20261006/flow-ownership-review.json` after root integrated the frozen Flow source and merged review hooks. Native Unity ran three actual FlowController Awake→Object.Destroy cycles, with the real cached destruction token and actual scheduled ConfigRefreshLoop. The longest measured cancellation was0.202463709seconds. All three cycles restored the private real-Bus probe subscription count, rejected a late controlled continuation/fault, and tolerated repeated owner disposal. Receipt: `pass:true`, `iterations:3`, `apiAttempts:0`, `runtimeErrors:0`, `servicesRestored:true`.

The component was disabled before Start to avoid constructing the known unowned overlays. This validates the native destruction/scheduler/poll/Bus boundary; it does not validate normal Start, full120second poll elapse, actual auth/wallet, shared Session generation, realtime/world resource ownership, Android background/process lifecycle, or a general “no crashes” claim. The private probe signal avoids broadcasting production UpdateRequired notifications. Broader overlay/service edits remain on hold per root; source-backed gaps above stay open.
