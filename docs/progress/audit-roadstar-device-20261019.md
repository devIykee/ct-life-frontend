# Roadstar controlled device trial — 19 October 2026

**Status: one scoped offline showcase trial confirms boarding, short ground acceleration/braking, flight toward Builder Hills, descent to grass, ground reverse movement, normal exit and subsequent on-foot movement.** The two recordings have a gap. The exact intended landing point, a complete route twice, Pro-mode preservation and automatic cliff safe-ground rescue remain unverified. Green/yellow sky flashes remain visible during flight.

## Artifact and recordings

The installed artifact is `ctlife-controls-lifecycle-dev.apk`, SHA256 `4ef24fa133acfe9626c2f3cac01596fd624069091f3ed65f983cbb2f3854d07d`. `controls-lifecycle-build.json` records source commit `31e48cee` and installation verification at 16:54:21 UTC. The integrated native suite passed **2,067/2,067**; this device trial adds the narrower observations below. Root owned all launches, touch commands, recordings and device collection; review was host-only and made no source changes.

Evidence is in sibling `ct-life-evidence/audit-20261019/`:

| Recording | Duration / bytes | SHA256 |
| --- | --- | --- |
| `ctlife-roadstar-controlled-20261019.mp4` | 600.133 s / 400,944,945 | `a5ecc28afdea8ad65b1fb5ef4b6e8f71023fa911a565d0896fdca939fd934202` |
| `ctlife-roadstar-transit-20261019.mp4` | 1,086.895 s / 518,338,713 | `514399400ee6107b5da3d97b9178db881e2434ccb606ba774b4803bda196bad3` |

Both files parse completely as 2670×1200 recordings. Embedded Android frame timestamps span **17:38:17.554–17:48:17.653 UTC** and **17:50:01.795–18:08:08.657 UTC**, respectively. Host capture receipts end/start around 17:48:18 and 17:50:01, leaving about 103 seconds between capture windows. The final first-leg acceleration extends beyond the first recording, and the following brake input falls in this gap. It is a known recording gap, not evidence of unexplained input. These two files do not establish uninterrupted lodge-to-arrival coverage.

## Observed sequence

| Phase | Visible evidence and scope |
| --- | --- |
| Boarding and ground basics | The first clip shows the avatar approach the red Roadstar, then the driving HUD/Get out state by about 221.5 s. A short Gas gesture moves it to about 19 km/h; Brake reduces the displayed speed to 0. No distinct ground-steering trial was established by this clip. |
| Fly and initial climb | Around 470.5 s in the first clip, Fly becomes Land and cyan wheel/hover presentation appears. The upward stick gesture visibly raises the car to roughly 32 m over the queried surface before hover. Later map close, an airborne right turn and acceleration are visible near the end. The entire acceleration/braking sequence crosses the recording gap. |
| Second flight leg | The second clip begins already airborne. After a map pause, a short right steering adjustment and Gas advance the car across visible roads/buildings in Builder Hills. Exact frame **439.5 s** shows **156 km/h**, **78 m**, and the Builder Hills label. Brake visibly reduces the speed. The HUD value is a displayed reading, not independent speed/altitude telemetry. |
| Descent and initial touchdown | After further heading/throttle adjustments, downward stick input lowers the car. Land changes to Fly while it is still above the surface. Exact frames show 18 m at 734 s, 6 m with the car tilted beside a pale building at 742 s, and 2 m at 748 s. By 752 s the wheels are down, Drift is present and the car is on grass; **756–760 s show 0 km/h** beside the building. This is a grassy, building-adjacent landing, not the intended clear-road point. Every wheel/contact collider was not instrumented. |
| Reverse onto pavement | The Brake command beginning **18:04:11.736 UTC** starts from a stopped ground state and produces reverse travel away from the buildings, across grass and onto road/pavement. The car reaches about 27 km/h, then coasts down after release. This is brake/reverse behavior; it was not a separate brake-to-zero action immediately before exit. |
| Normal exit and walking | Get out was commanded around **18:04:21.514 UTC** while the car was still slowing. By second-clip 860–861 s the ordinary on-foot HUD and Drive offer are visible beside the car. A lamp post initially obscures most of the avatar; later **869 s** shows clear on-foot movement away and **872 s** upright idle after release. No recovery veil or ground fall-through was visible in this reviewed sequence. |

These observations support usable arrival and returned control at the tested location. They do not measure every cell's readiness before entry, all collision surfaces, exact exit distance, frame-time stability, or sustained high-speed handling. The path includes a close building approach and grass touchdown; it is not a polished precision-landing acceptance.

## Position and rendering limits

The final map was opened **after** reversing, exiting and walking. The coordinator's visible-map calibration estimates the **post-walk player** at approximately **X376.0/Z237.7**, in `cell_5_4`, about **46.3 m** from the proposed X420/Z252 destination. This is an estimate from displayed map geometry, not an exact craft landing pose; no altitude was inferred. The map and Builder Hills label support arrival in the intended district, not arrival at the exact suggested point. The original calibration contains a copied helicopter/seat-offset caveat that does not apply to this already-on-foot sample.

Bright green/yellow sky bands are visible in multiple flight frames, including the exact 439.5 s transit frame. Successful travel does not resolve that rendering defect. No causal Vulkan, shader, batching or encoding diagnosis is established by this route receipt. Existing input-origin observations also remain unattributed; the recording gap in this controlled trial must not be conflated with those earlier incidents.

## Review coverage and outstanding acceptance

The second clip was sampled across its full duration every 10 seconds, with additional 2 fps review of 428–456 s, 708–744 s and 847–879 s, followed by full-resolution exact seeks through the flight, touchdown, exit and post-walk map. Long map/idle intervals were not inspected frame by frame. First-clip independent review covered boarding, the ground/Fly transition and initial-flight tail; it does not add landing or district-arrival evidence to that first clip.

The following remain separate: distinct ground steering, a precise clear-road touchdown, continuous full-route coverage, repeating the complete route twice, Pro-mode flight preservation, automatic cliff safe-ground rescue, and rendering stability. This receipt does not replace the scoped helicopter/drone/cliff observations in [flight follow-up](audit-flight-followup-20261019.md) or the pending items on [the manual route card](audit-manual-route-20261019.md).
