using System.Collections.Generic;
using System.Linq;
using CTLife.EditorTools;
using CTLife.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>
    /// Seat fits (SeatFitCatalog / SeatPlacement), the fitted seated blends, the CMU mocap one-shots and the cast import
    /// guard. Pure rules use hand-made seats; the coverage tests read the shipped catalog, controllers and venue prefabs.
    /// </summary>
    public class SeatFitTests
    {
        private static readonly string[] Venues = { "spaces_studio", "terminal_cafe", "timeline_square" };

        private static SeatFitCatalog Catalog() =>
            AssetDatabase.LoadAssetAtPath<SeatFitCatalog>(SeatFitBuilder.CatalogPath) ?? throw new AssertionException("SeatFits.asset missing");

        private static SeatFitCatalog.PoseMetric Pose(float hipY = 0.48f, float hipZ = -0.3f, float backZ = -0.38f, float soleZ = 0.15f) =>
            new SeatFitCatalog.PoseMetric
            {
                hipContact = new Vector3(0f, hipY, hipZ), backContact = new Vector3(0f, hipY + 0.2f, backZ),
                leftFoot = new Vector3(-0.1f, hipY - 0.45f, soleZ), rightFoot = new Vector3(0.1f, hipY - 0.45f, soleZ),
                calfBack = new Vector3(0f, hipY - 0.3f, 0.05f),
            };

        // ------------------------------------------------------------------ rules

        [TestCase(SeatKind.Chair, "act_sit", "act_sit")]
        [TestCase(SeatKind.Bench, "act_sit", "act_sit")]
        [TestCase(SeatKind.Pouf, "act_sit", "act_sit")]
        [TestCase(SeatKind.Lounge, "act_sit", "act_sit_lounge")]
        [TestCase(SeatKind.BeanBag, "act_sit", "act_sit_beanbag")]
        [TestCase(SeatKind.Stool, "act_sit", "act_sit_stool")]
        [TestCase(SeatKind.Office, "act_type", "act_type")]
        [TestCase(SeatKind.Lounge, "act_frantic_type", "act_frantic_type")]
        [TestCase(SeatKind.Chair, "loco_idle", "act_sit")]
        public void PoseFor_MapsSeatKindsAndKeepsTyping(SeatKind kind, string server, string expected) =>
            Assert.AreEqual(expected, SeatFitCatalog.PoseFor(kind, server));

        [Test]
        public void SoftSeatsSinkHardSeatsDoNot()
        {
            Assert.Greater(SeatFitCatalog.Sink(SeatKind.BeanBag), SeatFitCatalog.Sink(SeatKind.Lounge));
            Assert.Greater(SeatFitCatalog.Sink(SeatKind.Lounge), SeatFitCatalog.Sink(SeatKind.Pouf));
            Assert.LessOrEqual(SeatFitCatalog.Sink(SeatKind.Chair), 0.01f);
            Assert.LessOrEqual(SeatFitCatalog.Sink(SeatKind.Stool), 0.01f);
        }

        [Test]
        public void SeatHeight_StoolMeasuresToItsFootrest_ScaleNormalises()
        {
            var stool = new SeatFitCatalog.Seat { kind = SeatKind.Stool, seatTop = 0.81f, floor = 0f, footrest = 0.25f };
            Assert.AreEqual(0.81f - SeatFitCatalog.Sink(SeatKind.Stool) - 0.25f, SeatFitCatalog.SeatHeight(stool, 1f), 1e-4f);
            var chair = new SeatFitCatalog.Seat { kind = SeatKind.Chair, seatTop = 0.45f, floor = 0.01f };
            Assert.AreEqual((0.45f - SeatFitCatalog.Sink(SeatKind.Chair) - 0.01f) / 0.9f, SeatFitCatalog.SeatHeight(chair, 0.9f), 1e-4f);
        }

        [Test]
        public void ModelOffset_HipsRestOnTheSeatAndBackOffTheBackrest()
        {
            var seat = new SeatFitCatalog.Seat { kind = SeatKind.Chair, seatTop = 0.45f, backDistance = 0.4f, rearEdge = -0.42f, frontEdge = 0.05f };
            var pose = Pose();
            var off = SeatFitCatalog.ModelOffset(seat, pose, 1f);
            Assert.AreEqual(seat.seatTop - SeatFitCatalog.Sink(seat.kind), off.y + pose.hipContact.y, 1e-4f, "hip contact on the seat top");
            Assert.AreEqual(-seat.backDistance + 0.015f, off.z + pose.backContact.z, 1e-4f, "lumbar 1.5 cm off the backrest");
            // Scaled looks: the scaled contact lands on the same surface.
            var small = SeatFitCatalog.ModelOffset(seat, pose, 0.88f);
            Assert.AreEqual(seat.seatTop - SeatFitCatalog.Sink(seat.kind), small.y + pose.hipContact.y * 0.88f, 1e-4f);
        }

        [Test]
        public void ModelOffset_DeepUpholsteryKeepsTheCalvesOutOfItsFront()
        {
            // A 0.76 m deep pouf: centred hips would put the calves through its front face.
            var pouf = new SeatFitCatalog.Seat { kind = SeatKind.Pouf, seatTop = 0.43f, hipTarget = new Vector3(0f, 0.43f, -0.02f), rearEdge = -0.38f, frontEdge = 0.38f };
            var pose = Pose(hipZ: -0.3f);
            var off = SeatFitCatalog.ModelOffset(pouf, pose, 1f);
            Assert.GreaterOrEqual(off.z + pose.calfBack.z, pouf.frontEdge + 0.019f, "calves in front of the front edge");
            Assert.LessOrEqual(off.z + pose.hipContact.z, pouf.frontEdge - 0.099f, "hips still on the seat");
            // A chair with legs lets the calves under the seat: no forward push.
            var chair = new SeatFitCatalog.Seat { kind = SeatKind.Chair, seatTop = 0.45f, hipTarget = new Vector3(0f, 0.45f, -0.1f), rearEdge = -0.4f, frontEdge = 0.05f };
            Assert.AreEqual(chair.hipTarget.z, SeatFitCatalog.ModelOffset(chair, pose, 1f).z + pose.hipContact.z, 1e-4f);
        }

        [Test]
        public void ModelOffset_StoolPutsTheSolesOnTheBar()
        {
            var stool = new SeatFitCatalog.Seat { kind = SeatKind.Stool, seatTop = 0.81f, rearEdge = -0.18f, frontEdge = 0.18f, footrest = 0.25f, footrestForward = 0.09f };
            var pose = Pose(hipZ: -0.27f, soleZ: -0.16f);
            var off = SeatFitCatalog.ModelOffset(stool, pose, 1f);
            Assert.AreEqual(stool.footrestForward, off.z + pose.leftFoot.z, 1e-4f);
            Assert.That(off.z + pose.hipContact.z, Is.InRange(stool.rearEdge, stool.frontEdge), "hips on the seat");
        }

        [Test]
        public void TryGetPose_InterpolatesLikeTheBlendTree()
        {
            var c = ScriptableObject.CreateInstance<SeatFitCatalog>();
            try
            {
                c.poses = new List<SeatFitCatalog.PoseMetric>
                {
                    new SeatFitCatalog.PoseMetric { state = "act_sit", family = "m", height = 0.4f, hipContact = new Vector3(0, 0.4f, 0) },
                    new SeatFitCatalog.PoseMetric { state = "act_sit", family = "m", height = 0.6f, hipContact = new Vector3(0, 0.6f, 0) },
                    new SeatFitCatalog.PoseMetric { state = "act_sit", family = "f", height = 0.4f, hipContact = new Vector3(0, 0.1f, 0) },
                };
                Assert.IsTrue(c.TryGetPose("act_sit", "m", 0.45f, out var m));
                Assert.AreEqual(0.45f, m.hipContact.y, 1e-4f);
                Assert.IsTrue(c.TryGetPose("act_sit", "m", 0.9f, out var high));
                Assert.AreEqual(0.6f, high.hipContact.y, 1e-4f, "clamped to the highest child");
                Assert.IsTrue(c.TryGetPose("act_sit", "f", 0.5f, out var f));
                Assert.AreEqual(0.1f, f.hipContact.y, 1e-4f, "families do not mix");
                Assert.IsFalse(c.TryGetPose("act_sit_lounge", "m", 0.4f, out _));
            }
            finally { Object.DestroyImmediate(c); }
        }

        // ------------------------------------------------------------------ shipped data

        [Test]
        public void EverySeatInTheThreeVenuesIsMeasuredAndHasPosesForBothFamilies()
        {
            var catalog = Catalog();
            var missing = new List<string>();
            int seats = 0;
            foreach (var venue in Venues)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/World/Venues/{venue}/{venue}.prefab");
                Assert.IsNotNull(prefab, venue);
                foreach (var a in prefab.GetComponentsInChildren<SpotAnchor>(true).Where(SeatFitBuilder.IsSeat))
                {
                    seats++;
                    if (!catalog.TryGet(venue, a.spotId, out var seat)) { missing.Add($"{venue}/{a.spotId}: not measured"); continue; }
                    string state = SeatFitCatalog.PoseFor(seat.kind, a.anim);
                    foreach (var family in new[] { "m", "f" })
                        if (!catalog.TryGetPose(state, family, SeatFitCatalog.SeatHeight(seat, 1f), out _))
                            missing.Add($"{venue}/{a.spotId}: no {family} metric for {state}");
                }
            }
            Assert.Greater(seats, 40);
            Assert.IsEmpty(missing, string.Join("\n", missing));
        }

        [Test]
        public void SeatedStatesExistAndTheFFamilyPlaysItsOwnFits()
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterFeelController.ControllerPath);
            var rain = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CastImport.RainControllerPath);
            var states = ctrl.layers[0].stateMachine.states.Select(s => s.state).ToDictionary(s => s.name);
            foreach (var name in new[] { "act_sit", "act_sit_lounge", "act_sit_beanbag", "act_sit_stool" })
            {
                Assert.IsTrue(states.TryGetValue(name, out var st), name);
                var tree = st.motion as BlendTree;
                Assert.IsNotNull(tree, name + " is a seat-height blend");
                Assert.AreEqual(SeatPoseBuilder.SeatHeightParam, tree.blendParameter);
                Assert.IsFalse(st.iKOnFeet, name + ": edited muscle clips carry stale IK goals");
                foreach (var child in tree.children)
                {
                    var clip = (AnimationClip)child.motion;
                    StringAssert.Contains("_m_", clip.name);
                    var f = rain[clip];
                    Assert.IsNotNull(f, clip.name);
                    StringAssert.Contains("_f_", f.name, "Rain override for " + clip.name);
                }
            }
            var catalog = Catalog();
            CollectionAssert.Contains(catalog.femaleControllers, rain, "f family recognised by its controller");
        }

        [Test]
        public void TransitionClipsAreOneShots()
        {
            foreach (var file in new[] { "act_sit_down.fbx", "act_stand_up.fbx", "act_get_up.fbx" })
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath("Assets/Art/animations/" + file);
                Assert.IsNotNull(mi, file);
                Assert.IsTrue(mi.clipAnimations.Length > 0 && mi.clipAnimations.All(c => !c.loopTime), file + " must not loop (it wraps to its first frame under the cross-fade)");
            }
        }

        [Test]
        public void MocapOneShotsImportWithTheirOwnAvatarsAndAreWired()
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterFeelController.ControllerPath);
            var states = ctrl.layers[0].stateMachine.states.Select(s => s.state).ToDictionary(s => s.name);
            var tuning = AssetDatabase.LoadAssetAtPath<LocomotionTuning>(CharacterFeelController.TuningPath);
            foreach (var (file, state) in MocapImport.Clips)
            {
                var path = MocapImport.PathOf(file);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(model, path);
                var avatar = model.GetComponent<Animator>()?.avatar;
                Assert.IsTrue(avatar != null && avatar.isValid && avatar.isHuman, path + " Humanoid avatar");
                var clip = MocapImport.Clip(path);
                Assert.IsNotNull(clip, path);
                Assert.IsTrue(clip.isHumanMotion, path);
                Assert.IsFalse(clip.isLooping, path);
                Assert.IsTrue(states.TryGetValue(state, out var st) && st.motion == clip, state + " plays " + file);
                Assert.Greater(tuning.LengthOf(state, -1f), 0.5f, state + " length");
            }
            Assert.IsTrue(tuning.TryPose("act_get_up_front", out var getUp), "face-down get-up start pose (Knockdown aligns the root to it)");
            Assert.Less(getUp.hipsStart.y, 0.3f, "starts lying");
            Assert.Greater(getUp.hipsEnd.y, 0.8f, "ends standing");
        }

        [Test]
        public void CastFFamilyShoesCarryNoHeadWeight()
        {
            // CastWeightRepair (import): the uncorrected Rain source sent stale hip/ankle groups to Head.
            foreach (var look in new[] { "player_f_builder", "player_f_kol", "rain_breeze" })
            {
                var path = $"Assets/Art/characters/cast/looks/{look}/{look}.fbx";
                var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                try
                {
                    AnimatorUtility.DeoptimizeTransformHierarchy(go);
                    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        int head = System.Array.FindIndex(smr.bones, b => b != null && b.name == "Head");
                        Assert.GreaterOrEqual(head, 0, smr.name);
                        var baked = new Mesh();
                        smr.BakeMesh(baked, true);
                        var v = baked.vertices; var w = smr.sharedMesh.boneWeights; var m = smr.transform.localToWorldMatrix;
                        int bad = 0;
                        for (int i = 0; i < v.Length; i++)
                        {
                            if ((m.MultiplyPoint3x4(v[i]) - go.transform.position).y > 0.6f) continue;
                            var b = w[i];
                            if ((b.boneIndex0 == head && b.weight0 > 0.01f) || (b.boneIndex1 == head && b.weight1 > 0.01f) ||
                                (b.boneIndex2 == head && b.weight2 > 0.01f) || (b.boneIndex3 == head && b.weight3 > 0.01f)) bad++;
                        }
                        Object.DestroyImmediate(baked);
                        Assert.AreEqual(0, bad, $"{path}#{smr.name}: vertices below 0.6 m weighted to Head");
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }
        }
    }
}
