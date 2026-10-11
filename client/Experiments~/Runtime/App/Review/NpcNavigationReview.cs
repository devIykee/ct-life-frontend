#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.World;
using UnityEngine;
using UnityEngine.AI;

namespace CTLife.App.Review
{
    /// <summary>Real NavMesh/state-transition checks with an offline authored avatar; no server writes.</summary>
    internal static class NpcNavigationReview
    {
        public static async Task Run()
        {
            var session = VenueSession.Current;
            var venue = session.Venue;
            if (!venue.TryGetSpot("center", out var center, out _) ||
                !venue.TryGetSpot("entrance", out var entrance, out _))
                throw new InvalidOperationException("NPC review requires entrance and center anchors.");
            var view = AvatarFactory.Create("NPC navigation review", Services.Get<PlayerProfile>().LookArray(),
                false, parent: session.transform);
            view.isNpc = true;
            // The fixture checks actual Animator state, including when the camera faces away.
            if (view.Animator != null) view.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var targetA = new GameObject("Review target A");
            var targetB = new GameObject("Review target B");
            targetA.transform.position = center + Vector3.forward * 2f;
            targetB.transform.position = entrance + Vector3.forward * 2f;
            bool lateAvailable = false;
            var npc = view.gameObject.AddComponent<NpcController>();
            try
            {
                npc.Init("review-npc", State("idle"), venue, id =>
                    id == "a" || (id == "late" && lateAvailable) ? targetA.transform :
                    id == "b" ? targetB.transform : null);
                await Awaitable.WaitForSecondsAsync(0.3f);
                var agent = npc.GetComponent<NavMeshAgent>();
                Log.Info("review-npc", "Initial check: " + npc.ReviewSnapshot());
                Require(agent.isOnNavMesh && agent.isStopped, "NPC did not start idle on navigation. " + npc.ReviewSnapshot());
                RequireAnimation(view, "loco_idle", npc);

                var start = npc.transform.position;
                npc.Apply(State("follow", "a"));
                await Awaitable.WaitForSecondsAsync(1f);
                float moved = Vector3.Distance(start, npc.transform.position);
                Require(moved > 0.45f && !agent.isStopped, "Idle → follow did not resume movement.");
                Log.Info("review-npc", $"PASS idle → follow; moved={moved:F3}m.");

                npc.Apply(State("talk"));
                await Awaitable.WaitForSecondsAsync(0.1f);
                var stopped = npc.transform.position;
                await Awaitable.WaitForSecondsAsync(0.4f);
                float drift = Vector3.Distance(stopped, npc.transform.position);
                Require(agent.isStopped && !agent.hasPath && drift < 0.03f, "Talk retained a stale path or drifted.");
                RequireAnimation(view, "loco_idle", npc);
                var follow = State("follow", "a");
                npc.Apply(follow);
                await Awaitable.WaitForSecondsAsync(0.7f);
                Require(Vector3.Distance(stopped, npc.transform.position) > 0.25f, "Talk → follow stayed stopped.");
                Log.Info("review-npc", $"PASS follow → talk → follow; stopped drift={drift:F4}m.");

                // The target can change without changing state/start time; that must not be deduplicated.
                npc.Apply(new NpcState { state = "follow", spotId = follow.spotId, startedAt = follow.startedAt, targetUserId = "b" });
                await Awaitable.WaitForSecondsAsync(0.3f);
                Require(Vector3.Distance(agent.destination, entrance) < 0.3f, "Follow ignored a target-only update.");
                Log.Info("review-npc", "PASS target-only follow update.");

                npc.Apply(State("follow", "late"));
                await Awaitable.WaitForSecondsAsync(0.3f);
                Require(agent.isStopped, "Missing followed player retained a stale path.");
                start = npc.transform.position;
                lateAvailable = true;
                await Awaitable.WaitForSecondsAsync(0.8f);
                Require(Vector3.Distance(start, npc.transform.position) > 0.25f, "Late replicated target was never resolved.");
                Log.Info("review-npc", "PASS late target resolution.");

                float settleDeadline = Time.realtimeSinceStartup + 20f;
                while (!agent.isStopped)
                {
                    Require(Time.realtimeSinceStartup < settleDeadline, "Follow never settled behind stationary target. " + npc.ReviewSnapshot());
                    await Awaitable.NextFrameAsync();
                }
                await Awaitable.WaitForSecondsAsync(0.3f);
                Require(Vector3.Distance(npc.transform.position, center) < 0.7f,
                    "Follow stopped away from its target. " + npc.ReviewSnapshot());
                RequireAnimation(view, "loco_idle", npc);
                start = npc.transform.position;
                targetA.transform.position = entrance + Vector3.forward * 2f;
                await Awaitable.WaitForSecondsAsync(0.6f);
                Require(Vector3.Distance(start, npc.transform.position) > 0.25f && !agent.isStopped,
                    "Settled follow did not resume when the same target moved. " + npc.ReviewSnapshot());
                Log.Info("review-npc", "PASS follow settles idle behind target and resumes when that target moves.");

                // Cancel a leave before arrival, then enter an act at the current anchor.
                npc.Apply(State("leave"));
                npc.Apply(State("idle"));
                npc.Apply(new NpcState { state = "act", spotId = "center", animId = "act_sit", startedAt = ServerClock.NowMs });
                float deadline = Time.realtimeSinceStartup + 14f;
                while (agent.hasPath || agent.pathPending)
                {
                    Require(Time.realtimeSinceStartup < deadline, "Act arrival timed out.");
                    await Awaitable.NextFrameAsync();
                }
                await Awaitable.WaitForSecondsAsync(0.25f);
                Require(npc.gameObject.activeSelf, "Interrupted leave hid the NPC on a later arrival. " + npc.ReviewSnapshot());
                Require(Vector3.Distance(npc.transform.position, center) < 0.35f, "NPC did not reach the act anchor. " + npc.ReviewSnapshot());
                Require(agent.isStopped, "Act arrival left navigation moving.");
                RequireAnimation(view, "act_sit", npc);
                Require(Physics.Raycast(npc.transform.position + Vector3.up * 0.35f, Vector3.down,
                    out var floor, 0.85f, venue.groundMask.value, QueryTriggerInteraction.Ignore), "No collider below NPC.");
                float gap = Mathf.Abs(npc.transform.position.y - floor.point.y);
                Require(gap < 0.025f, "NPC root floats above paving.");
                Log.Info("review-npc", $"PASS cancelled leave → act arrival; rootGap={gap:F4}m. Root only, not animated foot-contact proof.");
                // Talk/react must settle to idle instead of resurrecting a stopped walk/act loop.
                var talk = State("talk");
                foreach (var line in Services.Get<ConfigStore>().Registries.NpcLines)
                {
                    if (string.IsNullOrEmpty(line.Value.text)) continue;
                    talk.lineId = line.Key;
                    break;
                }
                Require(!string.IsNullOrEmpty(talk.lineId), "NPC review needs one configured dialogue line.");
                npc.Apply(talk);
                await Awaitable.WaitForSecondsAsync(Duration("emote_point") + 0.35f);
                RequireAnimation(view, "loco_idle", npc);
                npc.Apply(new NpcState { state = "react", animId = "react_liquidated", startedAt = ServerClock.NowMs });
                await Awaitable.WaitForSecondsAsync(Duration("react_liquidated") + 0.35f);
                RequireAnimation(view, "loco_idle", npc);
                Log.Info("review-npc", "PASS missing-line talk and completed talk/react gestures settle to idle Animator state.");

                npc.Apply(new NpcState { state = "act", spotId = "review_missing_spot", animId = "act_sit", startedAt = ServerClock.NowMs });
                await Awaitable.WaitForSecondsAsync(0.35f);
                Require(agent.isStopped && !agent.hasPath && npc.gameObject.activeSelf,
                    "Missing act spot did not stay stopped and present. " + npc.ReviewSnapshot());
                RequireAnimation(view, "loco_idle", npc);
                Log.Info("review-npc", "PASS missing act spot remains idle; expected unknown-spot warning above.");

                Require(venue.TryGetSpot("exit", out var exit, out _), "Venue has no exit anchor.");
                Require(NavMesh.SamplePosition(exit, out var sampledExit, 0.75f, agent.areaMask),
                    "Exit has no reachable navigation projection.");
                float exitProjection = Vector3.Distance(sampledExit.position, exit);
                Require(exitProjection <= 0.751f, "Exit navigation projection exceeds the spot sampling bound.");
                npc.Apply(State("leave"));
                deadline = Time.realtimeSinceStartup + 35f;
                while (npc.gameObject.activeSelf)
                {
                    Require(Time.realtimeSinceStartup < deadline, "Normal leave did not arrive. " + npc.ReviewSnapshot());
                    await Awaitable.NextFrameAsync();
                }
                Require(npc.ReviewHasLeaveReceipt, "NPC disappeared without a leave-arrival receipt.");
                float rootArrival = HorizontalDistance(npc.ReviewLeaveRoot, sampledExit.position);
                float navArrival = HorizontalDistance(npc.ReviewLeaveNavigation, sampledExit.position);
                float rawDistance = Vector3.Distance(npc.ReviewLeaveRoot, exit);
                Require(Vector3.Distance(npc.ReviewLeaveTarget, sampledExit.position) < 0.01f &&
                    rootArrival <= 0.25f && navArrival <= 0.25f,
                    $"Leave hid before its independently sampled exit. rootError={rootArrival:F4}; " +
                    $"navError={navArrival:F4}; rawDistance={rawDistance:F4}; " + npc.ReviewLeaveSnapshot);
                Require(Physics.Raycast(npc.ReviewLeaveRoot + Vector3.up * 0.35f, Vector3.down,
                    out var exitFloor, 0.85f, venue.groundMask.value, QueryTriggerInteraction.Ignore) &&
                    Mathf.Abs(npc.ReviewLeaveRoot.y - exitFloor.point.y) < 0.025f,
                    "Leave root was not grounded at the sampled exit. " + npc.ReviewLeaveSnapshot);
                Log.Info("review-npc", $"PASS normal leave at sampled exit; raw={exit:F3}; sampled={sampledExit.position:F3}; " +
                    $"projection={exitProjection:F4}m; rootArrival={rootArrival:F4}m; navArrival={navArrival:F4}m; " +
                    $"rawDistance={rawDistance:F4}m. Root ground gap <0.025m.");
                await CheckDisconnectedNavigation();
            }
            finally
            {
                UnityEngine.Object.Destroy(view.gameObject);
                UnityEngine.Object.Destroy(targetA);
                UnityEngine.Object.Destroy(targetB);
            }
        }

        private static NpcState State(string state, string target = "") =>
            new NpcState { state = state, spotId = "entrance", targetUserId = target, startedAt = ServerClock.NowMs };

        private static float Duration(string anim) => (AnimationLibrary.Def(anim)?.durationMs ?? 2000) / 1000f;

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            var delta = a - b;
            delta.y = 0f;
            return delta.magnitude;
        }

        private static void RequireAnimation(AvatarView view, string state, NpcController npc)
        {
            Require(view.Animator != null && view.Animator.runtimeAnimatorController != null,
                "NPC animation review requires an authored Animator.");
            var actual = view.Animator.GetCurrentAnimatorStateInfo(0);
            Require(actual.shortNameHash == Animator.StringToHash(state) && !view.Animator.IsInTransition(0),
                $"Expected settled {state}, actual hash={actual.shortNameHash}; animatorEnabled={view.Animator.enabled}; " + npc.ReviewSnapshot());
        }

        private static async Task CheckDisconnectedNavigation()
        {
            // Two tiny isolated islands, far from the loaded venue. Only this fixture's data
            // instance is removed in finally; the live venue navigation is never rebuilt.
            var origin = new Vector3(1000f, 0f, 1000f);
            var sources = new List<NavMeshBuildSource>();
            foreach (float x in new[] { 0f, 12f })
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(origin + new Vector3(x, -0.1f, 0f), Quaternion.identity, Vector3.one),
                    size = new Vector3(6f, 0.2f, 6f), area = 0 });
            NavMeshData data = null;
            NavMeshDataInstance instance = default;
            GameObject root = null;
            AvatarView view = null;
            try
            {
                data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                    new Bounds(origin + Vector3.right * 6f, new Vector3(22f, 6f, 10f)), Vector3.zero, Quaternion.identity);
                Require(data != null, "Could not build disconnected-navigation fixture.");
                instance = NavMesh.AddNavMeshData(data);
                Require(instance.valid, "Could not register disconnected-navigation fixture.");
                root = new GameObject("Review isolated NPC navigation");
                root.transform.position = origin;
                var venue = root.AddComponent<VenueSceneRoot>();
                venue.venueId = "review_isolated";
                venue.groundMask = 0;
                AddAnchor(root.transform, "entrance", Vector3.zero);
                AddAnchor(root.transform, "near", Vector3.right * 2f);
                AddAnchor(root.transform, "center", Vector3.right * 12f);
                AddAnchor(root.transform, "exit", Vector3.right * 12f);
                AddAnchor(root.transform, "off_mesh", Vector3.forward * 12f);
                venue.Init(null);
                view = AvatarFactory.Create("Disconnected navigation review", Services.Get<PlayerProfile>().LookArray(), false, parent: root.transform);
                view.isNpc = true;
                var npc = view.gameObject.AddComponent<NpcController>();
                npc.Init("review-isolated", State("idle"), venue, _ => null);
                await Awaitable.WaitForSecondsAsync(0.3f);
                var agent = view.GetComponent<NavMeshAgent>();
                Require(agent.isOnNavMesh && agent.isStopped, "Isolated NPC failed initial binding. " + npc.ReviewSnapshot());
                Require(NavMesh.SamplePosition(origin + Vector3.right * 12f, out var other, 0.75f, agent.areaMask),
                    "Second fixture island has no navigation.");
                var path = new NavMeshPath();
                bool found = NavMesh.CalculatePath(agent.nextPosition, other.position, agent.areaMask, path);
                Require(!found || path.status != NavMeshPathStatus.PathComplete,
                    "Fixture islands unexpectedly connect; cannot exercise path failure.");
                var start = npc.transform.position;
                foreach (string state in new[] { "act", "leave" })
                {
                    npc.Apply(new NpcState { state = state, spotId = "center", animId = "act_sit", startedAt = ServerClock.NowMs });
                    await Awaitable.WaitForSecondsAsync(0.3f);
                    Require(npc.gameObject.activeSelf && agent.isStopped && !agent.hasPath &&
                        Vector3.Distance(start, npc.transform.position) < 0.03f,
                        "Unreachable " + state + " moved or hid. " + npc.ReviewSnapshot());
                }
                Log.Info("review-npc", "PASS disconnected target rejected for act/leave; observed path=" + path.status + ".");
                npc.Apply(new NpcState { state = "act", spotId = "off_mesh", animId = "act_sit", startedAt = ServerClock.NowMs });
                await Awaitable.WaitForSecondsAsync(0.3f);
                Require(agent.isStopped && !agent.hasPath && npc.gameObject.activeSelf, "Off-mesh target was accepted. " + npc.ReviewSnapshot());
                npc.Apply(new NpcState { state = "goto", spotId = "near", startedAt = ServerClock.NowMs });
                await Awaitable.WaitForSecondsAsync(0.2f);
                Require(agent.hasPath && !agent.isStopped, "Valid isolated path never started. " + npc.ReviewSnapshot());
                agent.ResetPath(); // Simulate an active path becoming unavailable before arrival.
                await Awaitable.WaitForSecondsAsync(0.2f);
                Require(agent.isStopped && !agent.hasPath && npc.gameObject.activeSelf,
                    "Lost path did not settle stopped. " + npc.ReviewSnapshot());
                Log.Info("review-npc", "PASS off-mesh target and externally lost active path remain stopped; expected path warnings above.");

                start = npc.transform.position;
                agent.enabled = false;
                npc.Apply(new NpcState { state = "goto", spotId = "near", startedAt = ServerClock.NowMs });
                npc.Apply(State("idle")); // The newest state must win while binding is unavailable.
                agent.enabled = true;
                await Awaitable.WaitForSecondsAsync(0.3f);
                Require(agent.isOnNavMesh && agent.isStopped && !agent.hasPath &&
                    npc.State.state == "idle" && Vector3.Distance(start, npc.transform.position) < 0.03f,
                    "Deferred state did not apply the latest idle after rebind. " + npc.ReviewSnapshot());
                Log.Info("review-npc", "PASS latest pending state wins after agent rebind; " + npc.ReviewSnapshot());
            }
            finally
            {
                if (view != null) view.gameObject.SetActive(false);
                if (instance.valid) NavMesh.RemoveNavMeshData(instance);
                if (root != null) UnityEngine.Object.Destroy(root);
                if (data != null) UnityEngine.Object.Destroy(data);
            }
        }

        private static void AddAnchor(Transform parent, string id, Vector3 position)
        {
            var go = new GameObject("Review " + id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.AddComponent<SpotAnchor>().spotId = id;
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
#endif
