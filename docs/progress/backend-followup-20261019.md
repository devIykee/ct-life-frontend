# Backend/network follow-up — 19 October 2026

> Chronological lane receipt. For the latest installed build, scoped device outcomes and final cleanup, see [the current handoff](../handoff/CONTINUE-HERE-20261019.md). Earlier test counts and pending statements below describe their own checkpoints.

Current result: the prior foreground venue and driving 2 s/45 s trials established driver release, fresh joining and later claims under the published backend semantics. A new entry-focused 2 s trial at **17:06:19 UTC** cut four sockets 33 ms after the observer first saw an SUV claim; the driver cleared, street presence returned, and the parked pose stayed unchanged until a second claim at **17:11:31 UTC**. Independent video review supports interruption during the on-foot approach, before visible door opening or seating. Later vehicle motion and a normal exit/walk were correlated with the coordinator's actions. This is not retained-driver reconnection. The direct observer completed at **17:26:14 UTC**; isolated backend PID 22726 and proxy PID 26668 were command-matched and stopped at **17:28:38 UTC**. Original services on 4010/2567 remain on PIDs 72652/72656. The latest entry-trial section below supersedes earlier running/stopped status notes; historical receipts remain intact.

The first device pass did not establish a disconnect: removing an adb reverse listener left active streams alive. This follow-up uses a loopback TCP proxy that destroys existing downstream and upstream sockets and rejects new connections for a chosen interval. The isolated backend and independent observer bypassed that proxy and stayed running during each injected outage. Backend source and original services were not changed.

## Verified reconnection semantics

Installed versions: vendored Unity SDK **0.18.8**, JavaScript SDK **0.18.5**, server `colyseus` **0.18.9**, resolved `@colyseus/core` **0.18.18**, WebSocket transport **0.18.4**. The official documentation opened for this research is labeled v0.18. Local installed source controls when a documentation example differs (for example, this Unity SDK's `OnDrop` delegate takes only a close code).

Colyseus requires a server reconnection reservation for the old session to resume. Automatic SDK retry and fresh authenticated join are different paths. A reserved session can keep its room/listeners; a fresh room needs its listeners attached again. See [official reconnection documentation](https://docs.colyseus.io/room/reconnection) and [lifecycle documentation](https://docs.colyseus.io/room/lifecycle).

The installed Unity `Room.cs` retries close codes 1005, 1006, 1001 and 4010 after its five-second minimum uptime, with up to 15 attempts and exponential delays capped at five seconds. `OnLeave(4003)` is terminal; `OnReconnect` denotes success. These defaults also appear in [the v0.18 client connection reference](https://docs.colyseus.io/sdk/connection). `Connection.Reconnect` appends a reconnection token and logs the full URL, so raw SDK/device logs must remain private or have tokens redacted before sharing.

**Current CT Life street, vehicles and venue rooms do not call `allowReconnection`.** They implement `onLeave` only. Installed core `Room.cjs:_onLeave` selects `onDrop || onLeave` for an abnormal close; absent a reservation, it completes departure immediately. `VehiclesRoom.onLeave` releases the driver and publishes the existing exit grant. There is no current retained-lease grace period to expire. The grant lasts 15 seconds and shared-position freshness lasts 30 seconds; neither is a reconnect reservation.

Consequently, the truthful device scenarios are **short outage with immediate lease loss and fresh rejoin**, and **45-second outage beyond the exit grant/shared-position window**. Same-session vehicle resume and delayed lease expiration require an explicit upstream backend policy. They cannot be certified by changing the client alone, and this pass does not add such a policy.

## Harness and local validation

- `tools/integration/realtime-fault-proxy.mjs`: loopback **2570 → 2569**, control **2571**. No payload/path/header inspection or persistence; receipts store only timestamps, socket-pair numbers and counts. `/cut` destroys both endpoints of every existing proxy pair and resets new accepted sockets until its deadline. The target listener stays up.
- `tools/integration/realtime-fault-probe.mjs`: an authenticated proxied driver and a separate direct observer, with SDK console output suppressed. It waits six seconds before cutting so the installed SDK's default minimum uptime is met, verifies ownership/movement, severs sockets, observes release, verifies direct movement acknowledgment, and joins fresh rooms after recovery to reclaim/release the car.
- `tools/integration/island-observer.mjs`: now records pseudonymous peer/vehicle presence transitions and driver changes, plus observer drop/reconnect/leave/error codes. Its optional --venue target also records venue departure/return and session-change counts without saving session IDs. Atomic output replacement avoids partially read receipts. No wallet, bearer token, session ID or vehicle ID is written.

Both local scenarios passed **9 checks each**, without backend implementation changes:

| Outage | Existing socket pairs cut | New connection attempts refused | Driver release observed after cut | Result |
|---|---:|---:|---:|---|
| 2 seconds | 2 | 6 | 67 ms | Both SDK rooms dropped; server returned terminal 4003; fresh joins reclaimed/released car. |
| 45 seconds | 2 | 24 | 112 ms | Same immediate release; direct observer stayed up; fresh joins worked after outage. |

Redacted evidence is in sibling `ct-life-evidence/audit-20261019/realtime-fault-{proxy,short,long}.json`. The elapsed release values are observation timing from these runs, not server timing guarantees. The injected SDK error/drop events in those receipts are expected faults, not clean-run errors.

The isolated backend was restarted from the same ignored audit `.env` (audit database, Redis database 8, API4012/WS2569); runtime PID **2989**, subsequently stopped during the cleanup recorded below. The original services on4010/2567 are preserved. Proxy and backend were kept running for device QA and were stopped after the phone became unavailable. Device launch uses `CTLIFE_API_PORT=4012 CTLIFE_REALTIME_PORT=2570`; the observer connects directly to2569. Inject only when the coordinator confirms the phone is driving. Control is `POST http://127.0.0.1:2571/cut` with `{"durationMs":2000}` or `{"durationMs":45000}`; `GET /status` reports socket counts, and `POST /resume` clears the block early.

## Unity lifecycle finding and repair

Read-only tracing found that terminal4003 already clears `RoomChannel.Room`, and the street/vehicle bridge Update loops join fresh rooms when `IsJoined` becomes false. VenueSession did not inspect room health: remaining inside the same zone produces no streaming handoff, so a terminally lost venue could retain a dead channel until the player crossed zones or explicitly traveled.

With coordinator authorization, this pass adds a visit-owned same-zone rejoin path. It waits for terminal loss rather than competing with SDK retries, allows only one attempt at a time, backs failed attempts off from2 to32 seconds, cancels an obsolete target when the player changes zones or travels, and drains the captured late join before another attempt starts. Accepted replacements rebind local movement/interactions, avatars/NPCs, session identity and the venue-changed signal. Commit-time checks reject results after vehicle entry or recovery begins, even before the next Update can cancel them. A failed or cancelled handoff retires its captured candidate before adoption; it does not choose cleanup from the shared current room. An explicit travel waits for the retiring reconnect attempt and keeps its own pause ownership.

Explicit channel retirement now disables SDK reconnect, caps subsequent retries at zero, clears queued actions and cancels the current dropped transport; obsolete room callbacks cannot report a successor as dropped/reconnected. **SDK limitation:** an already-scheduled retry delay does not recheck the enabled flag before calling Connect. Such delayed attempts can still run, although their next retry cycle is disabled. A captured-room OnReconnect fence now sends a leave if an already-scheduled attempt succeeds after retirement, without touching the successor. This is not claimed to cancel every already-scheduled socket attempt; fully cancellable SDK retry timers remain an upstream SDK concern.

Focused tests cover terminal4003 through the actual SDK connection-event path, dropped-room retirement, stale terminal callbacks, retry serialization/backoff, changed-zone late-result cleanup and visit cancellation, and a late SDK reconnect sending LEAVE_ROOM only to its retired handle. The final frozen-snapshot editor compile harness passed across runtime/editor assemblies and all53 EditMode test files, with six existing warnings (unused probe fields, obsolete editor raycast collider IDs and an unused venue-nav variable). The initial development-player compile passed with zero warnings. The coordinator’s full native Unity suite then passed **2018/2018 tests**, including all eight new reconnect tests, with zero failures or skips. This was independently checked against editmode-followup.xml; venue-reconnect-test-receipt.json extracts the eight outcomes. The controlled foreground venue rejoin and post-return walking are verified below; the two vehicle outage scenarios remain pending.

## Geometry handoff

The [street coverage proposal](../fe-contract-proposals/street-coverage-20261019/README.md) maps ORE and all36 server-boundary parking gaps, plus two additional bays outside the client's exact polygons. It proposes a reviewed extension of the existing mountain polygon using its current array schema, not a new room or backend field. The actual backend helper validation covers all38 affected bays and ORE. No geometry has been applied to runtime contracts, authored world data or backend rooms.

## Controlled device venue trial

The direct observer joined `timeline_square`, `street:timeline` and `vehicles` through2569, while the phone used the2570 proxy. The completed first window is `island-observer-faults-20261019.json` (04:59:41–05:29:42UTC).

- A preliminary adb-related disruption at05:02:29 was not an injected scenario. It is recorded separately in `device-fault-preliminary-disruption.json`.
- The first real2-second cut at05:06:12 destroyed five socket pairs while the Android notification shade had paused the app. Venue and street returned just after foregrounding at05:07:05. This is a separate background/foreground trial, `device-fault-venue-short-background.json`, not the clean foreground result.
- The controlled **foreground on-foot same-zone** cut at05:08:46.161 destroyed five existing socket pairs and refused15 retry connections during the two-second block. Observer departure occurred05:08:46.264; the same venue returned with a **new session** at05:08:49.690 (about3.53s after cut), and street presence returned05:08:49.889 (about3.73s). Subsequent ordinary movement appeared in both rooms with matching island positions, no extra departure, and zero observer errors/rejections. `device-fault-venue-short.json` preserves baseline, proxy events, fresh-session proof and post-return movement.

The phone’s vehicle2-second and45-second scenarios remain **unverified**; no vehicle cut was injected. The coordinator reported an empty adb device list at10:46UTC. The separate observer window ran10:28:58–10:48:54UTC in `island-observer-faults-20261019-vehicles.json`, observed zero peers/vehicles, and stopped gracefully. There is no observer coverage between the completed first window and this continuation; these receipts must not be represented as continuous monitoring.


## Earlier cleanup after the first device disconnect (10:49UTC)

At10:49UTC, command-line and listening-port matches identified the owned observer PID91616, proxy PID8784 and isolated backend PID2989 before SIGTERM. All three exited gracefully; backend wrapper PIDs2983/2972 also exited. API4012, WS2569 and proxy/control2570/2571 are closed. The original services remain running: PID72652 on4010 and PID72656 on2567. `backend-followup-cleanup.json` records these checks. The proxy recorded four total cuts: the two local probe scenarios, the paused-device venue trial and the foreground venue trial; **zero device vehicle cuts**.

Phone preference restoration and helper cleanup could not be verified because the phone was disconnected. No completion claim is made for those device cleanup actions.

## Resumed Seeker validation

After the phone reconnected, the coordinator requested the remaining vehicle trials with the final plaza APK (SHA-256 `2da427ec7087ae713e3c246168d46533820ee0a5a5c2a7cb744f02c796b59443`). The exact isolated backend at7d2f069a was restarted as PID63565 on4012/2569; the new proxy is PID64866 on2570/2571 and writes `realtime-fault-proxy-resumed.json`. Original services4010/2567 remain on PIDs72652/72656. After the online-launch cue, the direct observer started11:54:08UTC for30 minutes in `final-plaza-vehicle-observer.json`, joining Timeline venue, Timeline street and vehicles through2569. It observed the phone in both player rooms; no resumed fault has been injected yet.

The previous empty vehicle map is explained by the current registration contract: backend `parking.bays` is empty, and the client sends `vehicle_enter` only when attempting to enter a nearby public car. Proximity alone swaps decorative geometry for a local drivable stand-in (9–22m); it does not register the car with the server. Once registered, independent clients receive it within250m, with removal beyond300m and a1Hz interest refresh. The first observer window saw no registered vehicle; the later empty window saw no phone peers. The observer cannot determine whether the phone made a rejected entry attempt. These are not evidence of a vehicle-state decode failure. The283-bay export remains a proposal; it has not been seeded into this backend.

The final-plaza observer completed11:54:08–12:24:08UTC with one phone peer, zero registered vehicles and zero observer errors/rejections. Both player-room departures at12:14:26.480UTC followed the coordinator’s intentional app stop for the on-foot camera repair; they were not injected faults. The resumed proxy still records zero cuts. BackendPID63565 and proxyPID64866 remain available while the coordinator builds the camera-repaired APK. A new observer window is held for the next online cue using the fresh local test profile; vehicle2s/45s scenarios remain pending.

## Camera-repaired APK: public-car short outage

APK SHA-256 `0ffb83f3f87d9a05e246399385961d15cc7277b8bbb7181f8ed0f9f76f710a15` reached the public parking row through ordinary on-foot movement. `controls-final-vehicle-observer.json` recorded an SUV claim at12:41:50.841UTC. A separate, non-injected disruption closed all four live downstream sockets at12:50:40.960–.966; the SUV driver cleared12:50:41.104 and street presence returned12:50:42.711. The cause of those downstream closes is unverified. Backend source has no occupied-driver duration expiry: its ten-minute cleanup skips vehicles with a driver and applies to unoccupied client registrations.

The next claimed car was a **sports car**, not the SUV, at12:54:38.586. The coordinator held ordinary gas while injecting the controlled2-second cut at12:55:25.609. Four existing sockets were destroyed and12 retry connections refused. The direct observer saw the sports driver released193ms after the cut and the phone’s street presence return4.212s after it. Root control/proxy receipt: `vehicle-short-cut-controls.json`; derived observer summary: `vehicle-short-observer-summary.json`. The observer stayed error/rejection-free. This establishes driver release and fresh street return; it does not establish retained-session or retained-driver grace. The old observer’s pose-change counts can include idle floating-point jitter, so they are not presented as a quantified pre-cut driving speed.

After that completed short trial, the first observer was stopped and a new30-minute window began12:56:35UTC in `controls-long-vehicle-observer.json`. It adds maximum speed, moving-sample count and position bounds to distinguish driving from idle jitter. Both existing cars were unoccupied at the initial snapshot. The45-second trial proceeded after a new acknowledged claim; its completed results follow.


## Completed vehicle outage evidence

Both controlled vehicle trials used the sports car, distinct from the earlier SUV. All times here are host UTC. The2s cut at12:55:25.609 destroyed four existing device connections and refused12 connection attempts; driver release was observed193ms later and street return4.212s later. The sports car was claimed again at12:57:44.106 before the long trial.

The45s cut at12:58:48.322 destroyed four connections and refused72 attempts. Immediately before release, the independent observer saw3.533m/s and about1.045m displacement from the claimed parked pose. Driver release was observed269ms after the cut. Street presence returned12:59:34.808,46.486s after the cut (1.486s after the block ended). The parked car retained exactly the same observedXZ through the outage and remained unowned until a new claim at13:00:19.425. The coordinator also confirmed recovery in the phone UI. Observer errors/rejections remained zero. These measurements are sampled observation latencies, not guaranteed timing.

`vehicle-fault-final-summary.json` links the raw proxy/touch receipts and the short/long observer summaries. This proves released authority, fresh joining and new claims under the backend’s existing semantics. It does **not** prove same-session/retained-driver resume or delayed lease expiry: these rooms still have no server reconnect reservation. No additional>0.5m/s samples were observed after the final reclaim, so post-reclaim driving motion is not measured. The final driver release followed all four downstream sockets closing13:01:06.497–.530 during the coordinator’s intentional cooling stop; a separately completed normal exit is not established. This stop is not another injected outage.

The old SUV disappeared13:00:41.996, about10min after its earlier release, matching the existing unoccupied-registration expiry. This is separate from the cuts and does not imply an occupied-driver timeout. The client’s previously documented registered-bay expiry/visibility limitation remains.

The long observer PID95573 was command-matched and stopped gracefully at13:02:48.100. Backend63565 and proxy64866 remain available for possible further online checks; original4010/2567 services were not altered.


## Final owned-service cleanup (15:38:12UTC)

The coordinator matched each owned command before sending SIGTERM: proxy PID64866 (`realtime-fault-proxy.mjs`) and isolated backend PID63565 (`ctlife-audit-runtime-20261019.mts`). The receipt verifies that neither command still matched afterward and that API4012, WS2569 and proxy/control2570/2571 had no listeners. Original API4010 PID72652 and WS2567 PID72656 were preserved. Evidence: sibling `ct-life-evidence/audit-20261019/followup-services-cleanup-final.json`, UTC15:38:12.627138. This final cleanup supersedes the running-service notes above.

The coordinator separately reported the app stopped at15:27UTC, the three original private/preference files restored byte-for-byte at15:35UTC, all28 task videos hash- and ffprobe-verified before removing their phone copies, and the task helper removed. Root owns those device receipts and the coordinator/handoff documents; this lane performed no adb or Unity operations. The earlier disconnected-phone cleanup limitation no longer describes the final coordinator status. This does not turn unfinished route or rendering investigations into passes.

## Entry-focused restart, cut, re-entry and cleanup (16:51–17:28 UTC)

On the coordinator's new cue, the same isolated backend revision `7d2f069a3f432347dee7b9fa33d5431676dfb3c0` started at 16:51:35 UTC as PID 22726 on API 4012/realtime 2569. Proxy PID 26668 listens on 2570/control 2571. HTTP `/healthz` and `/v1/config` return 200; the isolated PostgreSQL database responds with 68 public tables, and Redis database 8 responds to PING. The FE/backend `contract/realtime/states.ts` and `messages.ts` files match byte-for-byte. `/readyz` was deliberately not called because it also requests external Helius health. Original services were preserved. Receipt: `entry-authority-services-start.json`.

The coordinator reported installing APK SHA-256 `4ef24fa133acfe9626c2f3cac01596fd624069091f3ed65f983cbb2f3854d07d`. Direct observer PID 51912 started at 16:56:14.625 UTC for 30 minutes, joining Timeline venue, Timeline street and vehicles through 2569. It saw the phone as `peer1`. The first 120 s next-claim watcher expired without a cut; the renewed watcher fired once. The helper is private QA tooling and is not shipped in the app. It polls the observer's atomic 200 ms snapshots every 20 ms, refuses an already-owned vehicle or overlapping fault, and records only aliases, counts and timestamps.

| Observation | UTC |
| --- | --- |
| SUV claim first observed | 17:06:19.332 |
| Proxy cut, four existing socket pairs destroyed | 17:06:19.365 |
| Driver release observed | 17:06:19.532 |
| Street departure observed | 17:06:19.533 |
| Street presence returned | 17:06:22.946 |
| Same SUV claimed again | 17:11:31.339 |
| Driver released after coordinator's normal Get out action | 17:21:31.845 |
| Observer completed its 30-minute window | 17:26:14.881 |

The proxy refused 12 new connections during its 2 s block. Claim-to-cut observation delay was 33 ms, cut-to-release 167 ms, and cut-to-street-return 3.581 s. These are sampled timings, not guarantees. The SUV's observed parked position was `(6.055894, -0.243875, -32.940834)`; X/Z stayed exactly unchanged, with zero speed and no observed movement, from the first claim through the outage and until the second claim. The second claim initially produced only small idle drift. Subsequent movement ultimately reached an observed maximum **6.005 m/s**, with **35 samples above 0.5 m/s**, X range 1.157–8.664 and Z range -36.363–-31.891. Ownership remained stable until the normal exit. Observer errors and rejections remained zero through completion; those counters describe the observer's own rooms, not every message received by the phone.

The independent device-video reviewer inspected the cut transition at quarter-second sampling and selected dense full-resolution frames. The player walks toward the visibly closed SUV door, stops, and returns to an upright on-foot state with Drive offered. This supports **approach-phase cancellation**; it does not prove a door-open, seating or final-door-close device interruption. The device log clock was approximately 0.606 s behind the host cut in this interval, so raw timestamps must not be mixed without calibration. Later re-entry falls outside that particular recording and is supported separately by the observer and coordinator's UI/action evidence.

The coordinator confirmed visible Get out followed by a 1.8 s forward walk, completed by 17:21:49 UTC. The observer saw driver release at 17:21:31.845 with final parked pose `(2.820914, -0.246160, -35.572884)` and speed zero. There was no proxy socket event around this release, and the phone stayed present in the street. Its recorded street position changed from `(4.938776, -31.514574)` before exit to `(-4.801344, -41.066742)` afterward: 13.642 m including dismount and walking. **The person's exact position at the release instant was not retained, so initial exit-grant distance is unmeasured.** The later 9.396 m car/person separation includes walking and must not be treated as an exit-grant violation or validation.

A separate street absence/return at 17:05:43.573–17:05:44.176 UTC preceded the injected cut while the proxy cut count was still zero. Its cause is not established by these receipts and is not part of the controlled fault result. Evidence: `entry-authority-claim-cut.json` (timeout), `entry-authority-claim-cut-attempt2.json`, `entry-authority-vehicle-observer.json`, `realtime-fault-proxy-entry-authority.json`, and derived `entry-authority-fault-summary.json`. No additional cut is armed.

At the coordinator's completion cue, observer PID 51912 had already completed its 30-minute window and exited. Exact command matches identified proxy PID 26668 and isolated backend PID 22726 before SIGTERM. Both exited, and API 4012/realtime 2569/proxy-control 2570/2571 were closed by 17:28:38.359723 UTC. Original API 4010 PID 72652 and realtime 2567 PID 72656 remained running. `entry-authority-services-cleanup.json` records the checks. This intentional service cleanup and the coordinator's subsequent app stop are not injected faults. Device cleanup remains owned by the coordinator; this lane performed no adb or Unity action.
