using System.Reflection;
using CTLife.World;
using CTLife.World.Vehicles;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.Tests
{
    /// <summary>Uses shipped cast rigs, clips and vehicle anchors, including the production traffic pooling path.</summary>
    public class AvatarVehiclePoseTests
    {
        private static readonly MethodInfo Seat = typeof(TrafficDrivers).GetMethod("Seat", BindingFlags.Instance | BindingFlags.NonPublic);

        private static AvatarView Create(string archetype)
        {
            var view = AvatarFactory.Create("vehicle-pose-test", null, true, archetype);
            Assert.IsNotNull(view);
            Assert.IsNotNull(view.Animator);
            Assert.IsTrue(view.Animator.isHuman);
            view.transform.position = new Vector3(1000f, 0f, 1000f);
            view.Animator.enabled = true;
            view.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            view.Animator.Rebind(); view.Animator.Update(0f);
            return view;
        }

        [TestCase("alpha_caller")]
        [TestCase("auditor")]
        public void RunStopKeepsBodyFacingAlongActorAcrossClip(string archetype)
        {
            var view = Create(archetype);
            try
            {
                var a = view.Animator;
                // Measuring the shoulders is diagnostic only; production keeps these bones optimized.
                AnimatorUtility.DeoptimizeTransformHierarchy(a.gameObject);
                a.Rebind(); a.Update(0f);
                view.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
                var position = view.transform.position;
                foreach (var state in new[] { "loco_idle", "loco_run_stop" })
                    foreach (float phase in new[] { 0f, .25f, .5f, .75f, .99f })
                    {
                        a.Play(state, 0, phase); a.Update(0f);
                        var left = a.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                        var right = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
                        Assert.IsNotNull(left); Assert.IsNotNull(right);
                        var across = right.position - left.position; across.y = 0f;
                        var forward = Vector3.Cross(across.normalized, Vector3.up);
                        Assert.Greater(Vector3.Dot(forward, view.transform.forward), .9f, state + " at " + phase);
                        Assert.Less(Vector3.Distance(position, view.transform.position), .001f, "pose does not move logical root");
                        Assert.AreEqual(37f, view.transform.eulerAngles.y, .001f, "pose does not turn logical root");
                    }
            }
            finally { Object.DestroyImmediate(view.gameObject); }
        }

        [TestCase("alpha_caller")]
        [TestCase("auditor")]
        public void OptimizedCastExposesHipsForMeasuredSeatsAfterKnockdown(string archetype)
        {
            var view = Create(archetype);
            RagdollRig rig = null;
            try
            {
                var a = view.Animator;
                Assert.IsFalse(a.hasTransformHierarchy, "the crowd retains optimized animation");
                Assert.IsNotNull(a.GetBoneTransform(HumanBodyBones.Hips), "seat measurement needs the actual pelvis");
                Assert.IsNull(a.GetBoneTransform(HumanBodyBones.LeftUpperArm), "unneeded bones remain optimized");
                Assert.IsTrue(RagdollRig.TryAcquire());
                rig = RagdollRig.Build(a);
                if (rig == null) RagdollRig.ReleaseUnbuilt();
                Assert.IsNotNull(rig);
                rig.Teardown(); rig = null;
                Assert.IsFalse(a.hasTransformHierarchy);
                Assert.IsNotNull(a.GetBoneTransform(HumanBodyBones.Hips), "fall/get-up must retain the imported seat anchor");
                Assert.IsNull(a.GetBoneTransform(HumanBodyBones.LeftUpperArm));
                a.Play("act_drive", 0, .5f); a.Update(0f);
                view.transform.rotation = Quaternion.Euler(0f, 61f, 0f);
                var hips = a.GetBoneTransform(HumanBodyBones.Hips);
                var measuredWorld = view.transform.rotation * VehicleSystem.MeasureSeatOffset(view) + view.transform.position;
                Assert.Less(Vector3.Distance(hips.position, measuredWorld), .001f, "player and traffic share the real-pose measurement");
            }
            finally { rig?.Teardown(); Object.DestroyImmediate(view.gameObject); }
        }

        [TestCase("compact", "alpha_caller", false)]
        [TestCase("compact", "alpha_caller", true)]
        [TestCase("compact", "auditor", false)]
        [TestCase("compact", "auditor", true)]
        [TestCase("suv", "alpha_caller", false)]
        [TestCase("suv", "alpha_caller", true)]
        [TestCase("suv", "auditor", false)]
        [TestCase("suv", "auditor", true)]
        [TestCase("sports", "alpha_caller", false)]
        [TestCase("sports", "alpha_caller", true)]
        [TestCase("sports", "auditor", false)]
        [TestCase("sports", "auditor", true)]
        public void TrafficSeatsActualDrivePoseBeforeVisibleTickIncludingCulledReuse(string id, string archetype, bool culledReuse)
        {
            var prefab = Resources.Load<GameObject>("Vehicles/" + id);
            Assert.IsNotNull(prefab);
            var go = Object.Instantiate(prefab);
            AvatarView view = null;
            try
            {
                go.transform.SetPositionAndRotation(new Vector3(1000f, 0f, 1000f), Quaternion.Euler(0f, 31f, 0f));
                var vehicle = go.GetComponent<Vehicle>(); vehicle.Bind();
                var car = go.AddComponent<TrafficCar>(); car.Bind();
                Assert.IsNotNull(vehicle.Seat);
                view = Create(archetype); view.ambient = true;
                var a = view.Animator;
                if (culledReuse)
                {
                    view.SetBase("act_drive"); a.Play("act_drive", 0, .5f); a.Update(0f);
                    view.ApplyLod(2, true, 1f);
                }
                else a.cullingMode = AnimatorCullingMode.CullCompletely;
                view.gameObject.SetActive(false);
                Seat.Invoke(new TrafficDrivers(null), new object[] { car, view });
                Assert.IsTrue(a.enabled); Assert.IsFalse(view.Culled);
                Assert.AreEqual(view, car.Driver);
                var hips = a.GetBoneTransform(HumanBodyBones.Hips);
                Assert.IsNotNull(hips, "the optimized imported pelvis must be exposed");
                Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).IsName("act_drive"), "already seated before its first visible frame");
                Assert.IsFalse(a.IsInTransition(0), "do not measure the standing-to-driving blend");
                Assert.Less(Vector3.Distance(hips.position, vehicle.Seat.position), .005f);
                view.ApplyLod(0, false, 1f); a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                for (int i = 0; i < 5; i++)
                {
                    a.Update(.1f);
                    Assert.IsTrue(a.GetCurrentAnimatorStateInfo(0).IsName("act_drive"));
                    Assert.Less(Vector3.Distance(hips.position, vehicle.Seat.position), .015f, "visible frame " + i);
                }
            }
            finally
            {
                if (view != null) Object.DestroyImmediate(view.gameObject);
                Object.DestroyImmediate(go);
            }
        }
    }
}
