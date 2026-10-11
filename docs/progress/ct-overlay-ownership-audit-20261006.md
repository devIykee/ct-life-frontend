# Overlay ownership audit — 2026-10-06

Status: staged outside Assets; full actual UI/App source compilation passes in Editor, Android development and production modes. Native lifecycle acceptance is pending. Root retains integration, Unity and device ownership.

## Reproducible source paths

| Path | Existing behavior | Bounded candidate |
|---|---|---|
| FlowController.Start | Creates five overlays and discards every handle. OnDestroy cannot release them. | Retain a list of IDisposable handles; dispose them through existing guarded cleanup. |
| ToastLayer | Owns Bus/updater state but drops its IncidentPresentation handle. | Retain incident; release both overlays and each owned toast. |
| IncidentPresentation.Dispose | Already releases Bus, Session and updater; leaves `city-incidents` mounted. | Remove the owned root and reject callbacks after disposal. Feed/order/TTL unchanged. |
| WorldLabels / BusyOverlay | Retain UiRoot updaters indefinitely; WorldLabels consults the current singleton during each tick. | Capture the original UiRoot and unregister there; remove owned tags, bubbles and busy root. |
| RektCard | Retains liquidation subscription; shown modal cards have no lifetime owner. | Track and remove owned cards, retain existing stacking behavior and reject stale buttons. |
| ProfileCard.ShowPlayer | GET writes captured labels after close/replacement; no cancellation. | Cancel only that read, validate sheet and Session identity after awaiting, dispose its own CTS in finally. |
| ProfileCard.Block / ShowReport | A late response calls global Close, so an older action can close a newly opened sheet. Repeated buttons can dispatch again. | Per-sheet pending gate; exact originating-sheet guard before UI effects. Started requests retain original uncancelled lifetime and payloads. |
| FrontendReviewController / SpikeController | Also discard overlay handles. Spike's anonymous panel updater cannot be removed. | Retain/dispose handles; capture original UI owner. Review guards post-profile-await creation; Spike owns its panel/updater and guards late label writes. |

The source-backed stale-response reproduction is: open player A, start Block, open player B before the request resolves, then complete A's request. The baseline's unconditional Close removes B. The staged native fixture drives this sequence through actual UI callbacks with a local controlled transport; it has not run yet. The analogous Report replacement path and non-cooperative late GET response are also included.

## Evidence and integration boundary

The [candidate README](../../art/gameplay-candidates/overlay-lifetime-20261006/README.md) records exact integration paths and native acceptance limits. `INTEGRATIONS.json` freezes nine original live hashes. The nine patches are narrow; full staged owner files are compile inputs and must not overwrite later root hook merges. One new App review helper is compile-guarded out of production.

All three complete UI/App source compiles and current-baseline patch checks pass. The first helper compile failed on ambiguous Position (REST model versus UI Toolkit), then passed after an explicit namespace qualification. Independent ProfileCard review found no blocking stale-completion flaw. Neither compilation nor that review is native runtime acceptance.

The proposed native probe counts actual Bus, Session and UiRoot delegates, verifies unrelated views survive, uses actual detach/click events, tests a temporary replacement UiRoot, and destroys three disabled-before-Start controller GameObjects with their owned handles. Api/Config/Session are isolated and restored; there is no HTTP, wallet, persisted credential, backend mutation or economic write. Its expected two Block and one Report operations exist solely inside the controlled in-memory transport.

[Unity 6.3 panel events](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-Panel-Events.html) distinguish the affected target/hierarchy, which is why child detaches must not cancel a whole owner. [IDisposable](https://learn.microsoft.com/en-us/dotnet/api/system.idisposable?view=net-9.0) provides the explicit release contract used here. Request semantics are verified against actual imported Api source, not inferred server behavior.

## Deliberately unchanged / remaining risk

- Started Block and Report calls are not cancelled or automatically replayed by this patch. Existing transport retry behavior is unchanged. Opening a fresh sheet can issue a fresh action; pending suppression is per sheet, not per target.
- Unexpected late errors retain existing logs, even when their obsolete sheet cannot display UI. Global error suppression is not part of this change.
- Native whole-layer detach, geometry/event ordering and original-service restoration require the queued native fixture. Normal Start auth/profile/room flow and physical Android lifecycle are not covered by its disabled-Start owner tests.
- Existing uncancellable room/world/RTC work can still complete after a controller dies. Shared Session/Profile service generation races and broader review/Spike resource cancellation remain outside this overlay patch.
- No relationship to the earlier review harness's intentional phone exit or indefinite thermal stability is claimed.

## Bounded native acceptance received

Root integrated the nine patches and ran the guarded native fixture after restarting with Unity's new Input System selected. The [preserved receipt](screens/overlay-ownership-native-20261006/overlay-ownership-review.json) has `pass:true`: seven local reads, two local Blocks, one local Report, three actual controller OnDestroy cycles, zero unexpected requests/errors, services restored and original UiRoot restored. Exact Bus/session/updater cleanup, child/root detach, stale completion guards, incident root removal and unrelated-view preservation passed.

This supersedes the candidate-only status above for those tested boundaries. Start remained disabled on the three temporary owner controllers: normal auth/room startup, physical touch, actual network cancellation and Android process lifetime are not certified. Root is separately refining ProfileCard layout; that later visual change is not included in this earlier lifetime receipt.
