# NPC navigation review — 2026-10-06

## Status

**M4 passed the complete native navigation fixture on Seeker.** M/M2/M3 failures remain recorded below; M4 establishes the corrected exit measurement and disconnected/rebind checks for this fixture, not general online NPC acceptance. No Unity Editor or ADB commands were run by this agent. Root owns the next build and Seeker run. Website and Unity UI files stayed frozen during this work.

Build M's real device failure remains evidence, not a passed check:

```text
10-06 08:50:32.697 E/Unity: CTLIFE [review] InvalidOperationException: NPC did not start idle on navigation.
```

Source: `docs/progress/screens/quality-review-20261006m/device.log:675`. The former assertion combined `isOnNavMesh && isStopped` without reporting either value. No preceding navigation warning was visible in that log. **The log does not establish which condition failed.**

[Inference] The old initialization added its agent at the prefab's origin, attempted a same-frame Warp, and consumed the initial state key even if `Stop()` skipped because the agent was not yet bound. A later binding could therefore leave the idle agent unstopped. This is a source-level failure route, not a device-proven diagnosis of M.

### M2 native result and subsequent correction

M2 **failed** the initial assertion too. Its new snapshot reported `ready=True`, `pendingState=False`, and `stopped=False` immediately at the end of Init and again after 0.3 seconds. Deferred binding is therefore **not the explanation for that M2 result**. Source: `docs/progress/screens/quality-review-20261006m2/device.log`.

M2 also exposed a new lifecycle error in this agent's change:

```text
UnityException: InitializeNavMeshPath is not allowed to be called from a MonoBehaviour constructor (or instance field initializer), call it in Awake or Start instead. Called from MonoBehaviour 'NpcController'
```

The native `NavMeshPath` allocation is now in `Init`, rather than a field initializer. The Roslyn check could not detect that engine lifecycle restriction.

The NPC stop order is now `ResetPath → velocity=0 → isStopped=true`, matching the existing device-checked local-player cancellation order. Previously it set `isStopped` first. Because M2 was already unstopped at the end of Init, before any Update, the later grounding refresh does not explain this immediate result. The next development run logs first-stop readbacks after `isStopped=true`, after `ResetPath`, after `velocity=0`, and after the final stop assignment; these distinguish the native setter responsible. The revised ordering was pending native confirmation at M2.

### M3 native result and exit measurement correction

`quality-review-20261006m3/device.log` records `afterSet=True; afterReset=False; afterVelocity=False; final=True`: **ResetPath cleared the stopped flag on this engine/device**. Init and the strict 0.3-second check both reported ready/stopped true. The native path-constructor exception did not recur. The fixture passed idle/follow, talk/follow, target-only and late-target updates, follow settle/resume, cancelled leave/act, talk/react idle recovery, and missing-act rejection. Measured act root gap was 0.0000m.

M3 then failed `Leave hid the NPC before the exit.` No final positions were recorded, so that log alone cannot prove where it hid. Source evidence: the authored exit is `(0,0,-15)`, exactly on the first walkable rectangle's boundary; the NavMesh bake uses radius 0.5m (`ProjectSettings/NavMeshAreas.asset`). Production and the editor venue validator project raw anchors onto navigation within 0.75m. The old fixture instead required a root less than 0.4m from the raw boundary anchor in 3D, without considering that projection. [Inference] Boundary erosion explains the disagreement; the exact runtime displacement awaits the next receipt.

The updated check independently samples the exit before movement and bounds that projection to 0.75m. Immediately before hiding, the controller records root/nav/target/raw positions while native navigation getters remain valid. The fixture requires both root and navigation horizontal arrival error ≤0.25m from its independently sampled endpoint, the controller target equal to that endpoint, and root-to-floor gap <0.025m. It logs raw distance too. Production leave behavior is unchanged; this is a corrected coordinate-space assertion and diagnostic, not a wider raw-distance tolerance. Disconnected-path checks have **not yet run on device**, because M3 stopped at leave.

## Corrections

Files: `client/Assets/Scripts/World/Npcs/NpcController.cs` and `client/Assets/Scripts/App/Review/NpcNavigationReview.cs`.

- Position the actor at a sampled spawn before adding the agent. If binding is unavailable, retain the latest replicated state and apply it once ready. Clear deduplication for deferred entries. Respect externally disabled agents; binding retries never enable them.
- Log guarded Init/deferred/check snapshots: active/enabled/ready, pending state, root/nav positions, stopped/path/pending/path-status, velocity, base offset and travel flag. Unbound snapshots do not read navigation-only properties.
- Keep the strict initial on-navigation **and** stopped assertion after 0.3 seconds; append the diagnostic snapshot instead of relaxing the check.
- Talk/react set their base to idle before a gesture. Previously `SetSpeed(0)` did not change the clip state, and `AvatarView` resumed the old walk/act base after the gesture.
- An emote-only update plays its overlay without restarting navigation. The old full-key dispatch compared total elapsed time to remaining travel distance and could warp a moving actor on such an update. Standalone backend emote updates have not been observed; this is a permitted schema sequence.
- Static journeys validate `PathComplete` before accepting a destination, including catch-up warps. Their estimated travel time uses path corners, rather than straight-line distance through obstacles.
- Track the intended endpoint. Missing/partial/invalid/lost paths settle idle; a path ending away from its intended endpoint cannot trigger an action or leave. Failed act locations do not play their action.
- Follow retains target-only updates and late target resolution, resumes stopped agents, and stops failed asynchronous paths. The next follow sample can retry when the target becomes reachable.
- Leave ownership resets on every state entry; arriving at a later act cannot carry a cancelled leave. Successful warp/arrival refreshes root grounding.
- Initial idle fidgets are scheduled 6–12 seconds ahead, avoiding an immediate random emote during initial idle verification.

The authoritative behavior remains in `docs/spec/build-spec.md`, “NPCs and AI,” and `docs/spec/contract.md`, NPC schema: server supplies states; client navigates/animates; FOLLOW stays two metres behind a player; LEAVE hides at the exit. No contract or backend changes were made.

## Verification fixture

`reviewNpcChecks=1` now uses actual server-epoch-shaped timestamps (`ServerClock.NowMs`), not literal values 1–9. It runs with an authored review avatar and real Unity navigation, without a backend connection or writes.

Checks encoded, **not yet reported as device passes**:

- Strict initial binding/stopped state, with detailed snapshot.
- Idle → follow movement; follow → missing-line talk → follow with stopped drift and idle Animator checks.
- Target-only follow change with unchanged start time; late target resolution.
- Follow settling behind a stationary target, then resuming when the same target moves.
- Cancelled leave → act at center, actual settled `act_sit` Animator state, and root-to-paving gap.
- Configured talk and reaction completing into actual idle Animator state; missing act spot remaining idle.
- Normal leave hiding only near the exit.
- A temporary two-island NavMesh, placed far from the venue: prove islands are disconnected before checking rejected act/leave destinations; off-mesh target rejection; deliberate loss of an active path; latest pending state after an agent disable/re-enable cycle.

The isolated fixture creates and removes only its own runtime navigation instance and GameObjects in `finally`; it does not rebuild or remove the loaded venue's navigation. Its deliberate missing/invalid path cases produce expected warnings. It reports the engine's observed disconnected-path status; a single run is not evidence of both Partial and Invalid variants. Animation checks use the main authored actor; the distant navigation fixture asserts navigation/presence, not rendered animation.

Allow approximately 60–90 seconds for NPC checks, in addition to normal movement/venue checks and captures. Root grounding is **not** animated sole/contact, foot-sliding, general crowd, or online-server verification. The offline fixture does not yet reproduce a synced-clock standalone emote update.

## API evidence

- Unity 6.3 documents that [`SetDestination`](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMeshAgent.SetDestination.html) reports request acceptance and may finish path calculation later.
- [`NavMeshPathStatus`](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMeshPathStatus.html) distinguishes reaching a destination from a partial or invalid path.
- [`NavMesh.CalculatePath`](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMesh.CalculatePath.html) and [`Warp`](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMeshAgent.Warp.html) were checked against official Unity 6.3 docs; installed Unity 6000.3.25f1 references are used by the compiler check.
- [`SetPath`](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMeshAgent.SetPath.html) assigns the already validated path and reports whether assignment succeeded.
- Runtime NavMesh build-source/data APIs follow the existing `GreyboxVenueBuilder` and `VenueContentBuilder` code; no package added.

## Build checks

- First full `python3 client/Tools~/compile_check.py --editor`: all CT Life assemblies compiled against pinned Unity references. This is a Roslyn source/API check, not Unity import, IL2CPP or device proof.
- Final `python3 client/Tools~/compile_check.py --editor` after fixture completion: all CT Life assemblies compiled; scoped `git diff --check` passed.

## Long-alias review (read-only)

Build L2 demonstrated the 20-W alias pushing Settings offscreen. That supersedes the earlier unconfirmed static assessment. Current root-owned USS constrains the player card with `flex-basis: 0`, `min-width: 0`, shrink enabled; the inner text container also has `min-width: 0`; the alias is single-line ellipsis; Settings has a fixed 132-unit minimum and cannot shrink. No new color or type family was introduced by these constraints. This is source evidence; the resulting stress capture is root's device acceptance check.

## M4 native receipt — 2026-10-06

Source: [actual device log](screens/quality-review-20261006m4/device.log), 10:01:51–10:02:22. Root ran Unity/device operations; this agent read the resulting log.

- Initial ready/stopped true; idle→follow moved 1.444m; talk stopped drift 0.0000m; target-only/late-target, follow settle/resume, cancelled leave→act, configured talk/react idle recovery, and missing-act rejection all passed. Act root gap measured 0.0000m.
- Exit raw `(0,0,-15)`, sampled `(0,0.083,-14.333)`, root before hide `(-0.149,0,-14.333)`. Projection **0.6719m**, root/nav horizontal arrival error **0.1492m**, raw distance **0.6832m**, floor gap <0.025m. This directly confirms the M3 raw-anchor assertion was testing the wrong endpoint; the NPC reached the independently sampled walkable exit. Production leave behavior was not loosened.
- The isolated islands produced **PathPartial**; unreachable act/leave stayed present. Off-mesh and deliberately lost active paths settled stopped. Latest pending idle won after disable/re-enable, with ready/stopped true and no path.
- The pre-hide snapshot can report the previous native velocity within the same frame despite final stopped=true; this receipt checks position/arrival and ground contact. It is not a claim about zeroed animation-frame velocity or animated feet.

No native claim is made for every possible PathInvalid cause, live backend timing, crowd avoidance, final mixed-cast frame rate or all retargeted actions. The controlled fixtures now pass; those broader acceptance tasks remain separate.
