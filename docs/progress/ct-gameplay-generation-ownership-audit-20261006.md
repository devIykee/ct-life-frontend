# Gameplay generation and remaining world ownership — 2026-10-06

The staged correction is in [gameplay-generation-20261006](../../art/gameplay-candidates/gameplay-generation-20261006/README.md). It is not integrated or native-accepted yet.

## Source-backed defect and bounded correction

The live FlowController used its MonoBehaviour lifetime for EnterGame. A city kick can start SignOut while ConnectMarket is pending. The controller remains alive, so an OperationCanceledException from the invalidated join fails the `when (!Alive)` filter, falls into the generic “market unavailable” catch, and execution proceeds to BuildHudAndPhone. This is a same-owner logout race, distinct from destruction.

The candidate cancels a separate captured gameplay generation immediately at SignOut, guards its callbacks and waits, and retains exact accepted room leases. Lease cancellation detaches only its matching channel and revision; reuse transfers ownership. DisconnectAll waits for accepted cleanup already retired by that cancellation. A cancelled post-GET PlayerProfile response is rejected before shared assignment or Bus publication. Started economic/auth operations and their existing order remain unchanged; no replay is added.

Compilation passes against installed Unity 6000.3.25f1 and exact App/Net/World/UI references in Editor, Android development and production modes. Managed actual-source checks pass 162 Flow/Bus, 74 room/service and 7 PlayerProfile assertions. Four removed-boundary negative controls fail as intended. This is not native, backend or device acceptance. Native helpers are staged; root owns integration and execution. Accepted-lease independent review is pending.

## Main-thread boundary

[Unity's current continuation documentation](https://docs.unity3d.com/6000.3/Documentation/Manual/async-awaitable-continuations.html) says main-thread Task calls capture UnitySynchronizationContext, while calls begun on a background thread do not automatically resume on the main thread. Cancellable room joins therefore require an owning context before candidate creation; worker cancellation posts exact-channel cleanup back to that captured context. The source remains a main-thread service API. Default no-token calls keep their existing service ownership.

[Microsoft documents](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokenregistration.dispose) the callback-self-unregistration exception to Dispose waiting. The candidate tests same-thread cancellation and old queued worker cancellation after lease transfer. Native Unity-context acceptance remains pending.

## Separate known world and proximity voice races

| Current source | Concrete ordering | Required ownership boundary |
| --- | --- | --- |
| [VenueSession](../../client/Assets/Scripts/World/Venue/VenueSession.cs) `Enter` / `Begin` | Enter assigns static Current, then awaits scene load and default-token JoinVenue. Exit may unload/destroy the session before Begin resumes, allowing late avatar/handler publication. | A per-visit cancellation owner plus serialized scene transition. Reject stale continuation before spawning or publishing; pass the visit token to JoinVenue. |
| VenueSession `Exit` | It resolves global VoiceService.Disconnect and IRealtimeService.LeaveVenue after awaits. Generic late cleanup could close a newer visit's room. | Capture ownership and close only that visit. Do not use global late Exit as a cleanup shortcut. |
| [VenueLoader](../../client/Assets/Scripts/World/Venue/VenueLoader.cs) `Load` / `Unload` | Native/Addressables load continues asynchronously; active scene, lighting and CameraRig are global. Unload while load is unfinished does not cancel the native operation. | Drain the owned load before its unload, serialize scene activation/restoration and verify ownership before publication. Cancellation may delay cleanup; it does not abort a native scene operation. |
| VenueSession `ConnectVoice` | RTC token request returns after a visit is superseded, then reads shared Channel.InstanceId and writes global voice resolvers. | Capture visit/channel/service identities before await and revalidate after token completion. |
| [VoiceService](../../client/Assets/Scripts/Net/Voice/VoiceService.cs) `Connect` / `Disconnect` / `SetMic` | Connect awaits an instruction and resumes through shared `_room`; an older failure can null a newer room. Disconnect awaits microphone unpublish and then clears shared room/stream state. | A separate local-room generation and captured resource teardown. Pending microphone permission/publish must not resume into a successor; mic remains explicit-only. |

These world/voice races are not resolved by the current gameplay-generation patch. A separate staged correction is the next work item. Native/UI cancellation alone is not evidence of resource cleanup. No backend operation or live audio/room connection was used in this audit.

## Revision 2 — replacement retirement review finding

Root review found an additional accepted-room ordering gap in frozen revision 1: replacement Join cleared Current, then directly awaited the prior accepted room's Leave. DisconnectAll during that wait saw an empty slot and no tracked retirement. This could allow logout to proceed before that captured Leave settled.

[Revision 2](../../art/gameplay-candidates/gameplay-generation-v2-20261006/README.md) records both replacement and owner-cancellation cleanup in one retirement path before Leave starts. Replacement still propagates Leave failure and does not create a new candidate on that failure. Background cleanup logs failure; a captured DisconnectAll retirement drain propagates it, then removes only its own settled entries. Raw Detach callers remain responsible for the returned handle, as the service's LeaveVenue/DisconnectAll already are.

Revision 2 passes the three actual assembly compilation modes, 162 Flow/Bus + 85 room/service + 7 PlayerProfile assertions, and five removed-boundary negative controls. The new tests hold an accepted Leave open through same-identity nonreuse replacement and identity-changing ConnectCity, invoke logout/drain while Current is null, and require logout to remain pending. Leave-failure propagation is checked too. The staged native helper includes the same replacement retirement boundary with synthetic handles. Revision 1 remains preserved, but revision 2 supersedes it; native acceptance is still pending.

## Native acceptance — root-run Unity review

Root integrated revision 2 after its own source review and reran all three compilation modes, 254 managed assertions and five negative controls. The native Unity review then passed with 3 local reads, 0 mutations, 0 runtime errors, 2 actual Flow destruction cycles, 2 fake disconnects and 1 local wallet callback. It restored the original services and venue reference.

The receipt at `/tmp/ct-gameplay-generation-native-20261006/gameplay-generation-review.json` checks Unity-context lease handoff, stale posted cancellation, held replacement retirement, same-owner logout during a held market join, a fresh sign-in callback, late shared-profile response rejection and actual destruction cleanup. Root is recording the durable evidence copy. The receipt explicitly excludes SDK connections, actual wallet signing, microphone, physical touch, Android process lifecycle and VenueSession/Loader/Voice ownership. Those world/voice changes remain a separate unintegrated candidate.
