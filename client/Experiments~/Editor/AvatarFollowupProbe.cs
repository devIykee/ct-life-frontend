using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CTLife.World;
using CTLife.World.Vehicles;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>One-off, read-only imported-pose/production traffic seating diagnostic. No asset assignments or saves.</summary>
    public static class AvatarFollowupProbe
    {
        private static readonly string[] DemoLook = { "skin_04", "head_snow", "hair_villa_blonde", "top_hoodie_black", "bottom_jeans_indigo", "shoes_sneakers_white", "acc_aviators" };
        private static readonly MethodInfo Seat = typeof(TrafficDrivers).GetMethod("Seat", BindingFlags.Instance | BindingFlags.NonPublic);
        private static JObject V(Vector3 p) => new JObject { ["x"] = p.x, ["y"] = p.y, ["z"] = p.z };

        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run this isolated probe outside Play mode.");
            string output = Environment.GetEnvironmentVariable("CTLIFE_AVATAR_PROBE_OUT") ??
                Path.GetFullPath(Path.Combine(Application.dataPath, "../../../ct-life-evidence/audit-20261019/avatar-followup-before.json"));
            var report = new JObject { ["unity"] = Application.unityVersion, ["utc"] = DateTime.UtcNow.ToString("O"), ["productionAssetsModified"] = false };
            var actors = new JArray(); report["actors"] = actors;
            foreach (string archetype in new[] { "demo", "alpha_caller", "auditor" })
            {
                AvatarView view = null;
                try
                {
                    view = NewView(archetype);
                    var a = view.Animator;
                    var o = new JObject { ["archetype"] = archetype, ["model"] = a.gameObject.name,
                        ["controller"] = AssetDatabase.GetAssetPath(a.runtimeAnimatorController), ["humanScale"] = a.humanScale,
                        ["modelLocalPosition"] = V(a.transform.localPosition), ["leftFootBottom"] = a.leftFeetBottomHeight,
                        ["rightFootBottom"] = a.rightFeetBottomHeight };
                    actors.Add(o);
                    a.enabled = true; a.cullingMode = AnimatorCullingMode.AlwaysAnimate; a.Rebind(); a.Update(0f);
                    o["optimizedBoneAvailability"] = Bones(a);
                    if (!RagdollRig.TryAcquire()) throw new InvalidOperationException("Diagnostic ragdoll budget unavailable.");
                    var rig = RagdollRig.Build(a);
                    if (rig == null) { RagdollRig.ReleaseUnbuilt(); throw new InvalidOperationException("Diagnostic ragdoll did not build."); }
                    rig.Teardown();
                    o["boneAvailabilityAfterRagdoll"] = Bones(a);
                    o["optimizedAfterRagdoll"] = !a.hasTransformHierarchy;
                    // Diagnostic only: imported casts hide transforms. Preserve that fact above; expose a temporary
                    // instance to measure the native retargeted orientation/ankles without changing the import asset.
                    AnimatorUtility.DeoptimizeTransformHierarchy(a.gameObject);
                    a.Rebind(); a.Update(0f);
                    o["poseMeasurementUsesDeoptimizedInstance"] = true;
                    var poses = new JArray(); o["poses"] = poses;
                    foreach (string state in new[] { "loco_idle", "loco_run_stop", "act_drive" })
                    {
                        foreach (float phase in new[] { 0f, .25f, .5f, .75f, .99f })
                        {
                            a.Play(state, 0, phase); a.Update(0f);
                            var pose = Measure(view); pose["requestedState"] = state; pose["phase"] = phase; poses.Add(pose);
                        }
                    }
                }
                finally { if (view != null) Object.DestroyImmediate(view.gameObject); }
            }
            var traffic = new JArray(); report["traffic"] = traffic;
            foreach (string carId in new[] { "compact", "suv", "sports" })
                foreach (string archetype in new[] { "alpha_caller", "auditor" })
                    foreach (bool culledReuse in new[] { false, true }) traffic.Add(TrafficCase(carId, archetype, culledReuse));
            report["timelineFloor"] = Ground();
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, report.ToString());
            Debug.Log("CTLIFE avatar followup probe: " + output);
        }

        private static JArray Ground()
        {
            var result = new JArray();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/World/Venues/timeline_square/timeline_square.prefab");
            var go = Object.Instantiate(prefab);
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Physics.SyncTransforms();
                foreach (var p in new[] { new Vector3(0f, 0f, -10f), new Vector3(0f, 0f, -7f), new Vector3(1f, 0f, -9f) })
                {
                    float collision = float.NegativeInfinity, surface = float.NegativeInfinity;
                    string collisionName = null, surfaceName = null;
                    foreach (var c in go.GetComponentsInChildren<Collider>())
                    {
                        if (!c.enabled || c.isTrigger || !c.Raycast(new Ray(p + Vector3.up * 2f, Vector3.down), out var hit, 4f) ||
                            hit.normal.y < .7f || hit.point.y > .5f || hit.point.y <= collision) continue;
                        collision = hit.point.y; collisionName = c.name;
                    }
                    foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
                    {
                        var mesh = filter.sharedMesh; var renderer = filter.GetComponent<MeshRenderer>();
                        if (mesh == null || renderer == null || !renderer.enabled) continue;
                        var vertices = mesh.vertices; var triangles = mesh.triangles;
                        for (int i = 0; i < triangles.Length; i += 3)
                        {
                            var a = filter.transform.TransformPoint(vertices[triangles[i]]);
                            var b = filter.transform.TransformPoint(vertices[triangles[i + 1]]);
                            var c = filter.transform.TransformPoint(vertices[triangles[i + 2]]);
                            var normal = Vector3.Cross(b - a, c - a).normalized;
                            if (Mathf.Abs(normal.y) < .7f) continue;
                            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                            if (Mathf.Abs(d) < 1e-8f) continue;
                            float u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / d;
                            float v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / d;
                            if (u < -.0001f || v < -.0001f || u + v > 1.0001f) continue;
                            float y = u * a.y + v * b.y + (1f - u - v) * c.y;
                            if (y > .5f || y < -2f || y <= surface) continue;
                            surface = y; surfaceName = filter.name;
                        }
                    }
                    result.Add(new JObject { ["point"] = V(p), ["collisionY"] = collision, ["collisionName"] = collisionName,
                        ["renderedSurfaceY"] = surface, ["surfaceName"] = surfaceName, ["renderMinusCollision"] = surface - collision });
                }
            }
            finally { Object.DestroyImmediate(go); }
            return result;
        }

        private static AvatarView NewView(string archetype)
        {
            var view = AvatarFactory.Create("avatar-followup-" + archetype, archetype == "demo" ? DemoLook : null,
                archetype != "demo", archetype == "demo" ? null : archetype);
            if (view.Animator == null || !view.Animator.isHuman) throw new InvalidOperationException("Expected a valid Humanoid: " + archetype);
            view.transform.position = new Vector3(10000f, 0f, 10000f);
            foreach (var skin in view.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
            return view;
        }

        private static JObject TrafficCase(string id, string archetype, bool culledReuse)
        {
            GameObject carObject = null; AvatarView view = null;
            try
            {
                var prefab = Resources.Load<GameObject>("Vehicles/" + id);
                carObject = Object.Instantiate(prefab); carObject.transform.position = new Vector3(10000f, 0f, 10000f);
                var vehicle = carObject.GetComponent<Vehicle>(); vehicle.Bind();
                var car = carObject.AddComponent<TrafficCar>(); car.Bind();
                if (vehicle.Seat == null) throw new InvalidOperationException("No driver anchor: " + id);
                view = NewView(archetype); view.ambient = true;
                var a = view.Animator;
                a.enabled = true; a.cullingMode = AnimatorCullingMode.AlwaysAnimate; a.Rebind(); a.Update(0f);
                a.Play(culledReuse ? "act_drive" : "loco_idle", 0, 0f); a.Update(0f);
                if (culledReuse) { view.SetBase("act_drive"); view.ApplyLod(2, true, 1f); }
                else a.cullingMode = AnimatorCullingMode.CullCompletely;
                view.gameObject.SetActive(false);
                var o = new JObject { ["car"] = id, ["archetype"] = archetype, ["culledReuse"] = culledReuse,
                    ["seatInCar"] = V(car.transform.InverseTransformPoint(vehicle.Seat.position)),
                    ["bodyColliderTopInCar"] = car.GetComponent<BoxCollider>().center.y + car.GetComponent<BoxCollider>().size.y * .5f,
                    ["before"] = Measure(view) };
                // Execute the production Seat method before any diagnostic deoptimization.
                var drivers = new TrafficDrivers(null);
                Seat.Invoke(drivers, new object[] { car, view });
                o["afterSeat"] = Measure(view, vehicle.Seat);
                // Simulate the next visible LOD assignment, then advance real Humanoid evaluation without moving the actor.
                view.ApplyLod(0, false, 1f); a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var frames = new JArray(); o["afterVisibleTicks"] = frames;
                for (int i = 0; i < 5; i++) { a.Update(.1f); frames.Add(Measure(view, vehicle.Seat)); }
                // Same actor placement, actual bones exposed only for diagnosis: did the guessed optimized fit miss?
                AnimatorUtility.DeoptimizeTransformHierarchy(a.gameObject); a.Rebind();
                a.Play("act_drive", 0, .5f); a.Update(0f);
                o["deoptimizedDriveAtSameRoot"] = Measure(view, vehicle.Seat);
                Seat.Invoke(drivers, new object[] { car, view });
                o["seatWithAvailableHips"] = Measure(view, vehicle.Seat);
                return o;
            }
            finally
            {
                if (view != null) Object.DestroyImmediate(view.gameObject);
                if (carObject != null) Object.DestroyImmediate(carObject);
            }
        }

        private static JObject Measure(AvatarView view, Transform seat = null)
        {
            var a = view.Animator;
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            var head = a.GetBoneTransform(HumanBodyBones.Head);
            var ls = a.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var rs = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var lf = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rf = a.GetBoneTransform(HumanBodyBones.RightFoot);
            var across = rs != null && ls != null ? rs.position - ls.position : Vector3.zero; across.y = 0f;
            var forward = Vector3.Cross(across.normalized, Vector3.up);
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            var baked = new Mesh();
            try
            {
                foreach (var skin in view.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin.sharedMesh == null) continue;
                    skin.BakeMesh(baked);
                    foreach (var v in baked.vertices)
                    {
                        float y = skin.transform.TransformPoint(v).y - view.transform.position.y;
                        minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                    }
                }
            }
            finally { Object.DestroyImmediate(baked); }
            var info = a.GetCurrentAnimatorStateInfo(0);
            var o = new JObject { ["enabled"] = a.enabled, ["culled"] = view.Culled, ["cullingMode"] = a.cullingMode.ToString(),
                ["stateHash"] = info.shortNameHash, ["transition"] = a.IsInTransition(0), ["normalizedTime"] = info.normalizedTime,
                ["rootY"] = view.transform.position.y, ["actorYaw"] = view.transform.eulerAngles.y,
                ["boneAvailability"] = Bones(a),
                ["shoulderForwardDotActor"] = rs != null && ls != null ? (JToken)new JValue(Vector3.Dot(forward, view.transform.forward)) : JValue.CreateNull(),
                ["hipsLocal"] = hips != null ? (JToken)V(view.transform.InverseTransformPoint(hips.position)) : JValue.CreateNull(),
                ["headYAboveRoot"] = head != null ? (JToken)new JValue(head.position.y - view.transform.position.y) : JValue.CreateNull(),
                ["skinMinYAboveRoot"] = minY, ["skinMaxYAboveRoot"] = maxY,
                ["leftAnkleYAboveRoot"] = lf != null ? (JToken)new JValue(lf.position.y - view.transform.position.y) : JValue.CreateNull(),
                ["rightAnkleYAboveRoot"] = rf != null ? (JToken)new JValue(rf.position.y - view.transform.position.y) : JValue.CreateNull() };
            if (seat != null)
            {
                o["hipsMinusSeat"] = hips != null ? (JToken)V(hips.position - seat.position) : JValue.CreateNull();
                o["headYAboveCar"] = head != null ? (JToken)new JValue(head.position.y - seat.root.position.y) : JValue.CreateNull();
                o["skinTopAboveCar"] = maxY + view.transform.position.y - seat.root.position.y;
            }
            return o;
        }

        private static JObject Bones(Animator animator)
        {
            var result = new JObject();
            foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftUpperArm,
                HumanBodyBones.RightUpperArm, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
            {
                var transform = animator.GetBoneTransform(bone);
                result[bone.ToString()] = transform != null ? (JToken)new JValue(transform.name) : JValue.CreateNull();
            }
            return result;
        }
    }
}
