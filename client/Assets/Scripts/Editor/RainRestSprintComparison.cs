// Preserve the current Sprint FK gait before correcting measured foot penetration.
// Root coordinator only. Generated clips are unreferenced candidates; no global SaveAssets.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    public static class RainRestSprintComparison
    {
        private const int Keys = 480, Dense = 480;
        private const string Stage = "Assets/Art/characters/authored/MotionReview20261006/RainRestSprintCompareV1/";
        private const string SourcePath = "Assets/Art/animations/loco_run.fbx";
        private static readonly string[] PatchNames = { "Foot.L", "Toes.L", "Foot.R", "Toes.R" };
        private static Dictionary<string, int[][]> patchIndices;
        private static readonly HumanBodyBones[] Tracked = {
            HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes };

        private sealed class Frame
        {
            public float phase, leftSole, rightSole, nativeNormalizedTime, animatorSpeed, stateSpeed, speedMultiplier;
            public float[] tierLeft, tierRight;
            public string[] tierNames;
            public Vector3[][] patchMeans;
            public Quaternion bodyRotation;
            public float humanScale;
            public Vector3[] joints;
            public Quaternion[] rotations;
            public float[] muscleValues;
        }
        private sealed class Skin
        {
            public SkinnedMeshRenderer renderer;
            public Mesh baked;
            public int[] left, right;
            public int[][] patches;
            public readonly List<Vector3> vertices = new List<Vector3>();
        }
        private sealed class Rig : IDisposable
        {
            public GameObject root;
            public Animator animator;
            public AnimatorController controller;
            public AnimatorStateMachine machine;
            public AnimatorState state;
            public HumanPoseHandler human;
            public readonly List<Skin> skins = new List<Skin>();
            public Rig(GameObject prefab, Scene scene, string stateName, AnimationClip clip, float speed, float reference, bool footIk)
            {
                try
                {
                root = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                animator = root.GetComponentsInChildren<Animator>(true).Single(); animator.enabled = false;
                if (animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid) throw new InvalidDataException("Own Avatar is invalid.");
                Require(Mathf.Abs(speed / reference - 1f) < .000001f, "Contact comparison must use the proven matrix 1x time path.");
                controller = new AnimatorController { name = "UNSAVED_SPRINT_DIRECT_LEG_V1" };
                machine = new AnimatorStateMachine(); state = machine.AddState(stateName); state.motion = clip;
                state.speed = 1f; state.speedParameterActive = false; state.iKOnFeet = footIk;
                machine.defaultState = state;
                controller.layers = new[] { new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1f, stateMachine = machine } };
                animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false; animator.speed = 1f; animator.enabled = true; animator.Rebind();
                human = new HumanPoseHandler(animator.avatar, animator.transform);
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var weights = renderer.sharedMesh.boneWeights;
                    var names = renderer.bones.Select(b => b.name).ToArray();
                    int[] Select(string suffix) => Enumerable.Range(0, weights.Length).Where(i =>
                    {
                        var w = weights[i];
                        float W(int bone, float value) => value > 0 && (names[bone] == "Foot." + suffix || names[bone] == "Toes." + suffix) ? value : 0f;
                        return W(w.boneIndex0, w.weight0) + W(w.boneIndex1, w.weight1) + W(w.boneIndex2, w.weight2) + W(w.boneIndex3, w.weight3) > .5f;
                    }).ToArray();
                    Require(patchIndices.TryGetValue(renderer.name, out var patches), "Missing fixed material patches for " + renderer.name);
                    Require(patches.All(p => p.Length > 0 && p.All(i => i >= 0 && i < renderer.sharedMesh.vertexCount)), "Fixed patch vertex bounds differ.");
                    var skin = new Skin { renderer = renderer, baked = new Mesh(), left = Select("L"), right = Select("R"), patches = patches };
                    skins.Add(skin); Require(skin.left.Length > 0 && skin.right.Length > 0, "Missing footwear weights.");
                    skin.vertices.Capacity = renderer.sharedMesh.vertexCount;
                }
                Require(skins.Count > 0, "No skinned geometry.");
                }
                catch { Dispose(); throw; }
            }
            public void Warm(int steps)
            {
                animator.Play(state.name, 0, 0); animator.Update(0);
                float dt = ((AnimationClip)state.motion).length / steps;
                for (int i = 0; i < 2 * steps; i++) animator.Update(dt);
                Require(Mathf.Abs(animator.GetCurrentAnimatorStateInfo(0).normalizedTime - 2f) < .001f, "Native warm-up did not advance exactly two loops.");
            }
            public void Step(int steps)
            {
                float before = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                animator.Update(((AnimationClip)state.motion).length / steps);
                float after = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                Require(Mathf.Abs(after - before - 1f / steps) < .0001f, "Native state time failed to advance one requested sample.");
            }
            public void Pose(float phase)
            {
                Warm(Keys);
                for (int i = 0; i < Mathf.RoundToInt(phase * Keys); i++) Step(Keys);
            }
            public Frame Read(float phase, bool muscles = false)
            {
                var info = animator.GetCurrentAnimatorStateInfo(0);
                Require(Mathf.Abs(info.normalizedTime - (2f + phase)) < .001f, "Sample label differs from actual native state time.");
                Require(Mathf.Abs(info.speed * info.speedMultiplier * animator.speed - 1f) < .00001f, "Contact sampler native rate differs from 1x.");
                Require(root.transform.position.sqrMagnitude < 1e-10f, "Disabled root motion still moved the root.");
                var frame = new Frame { phase = phase, nativeNormalizedTime = info.normalizedTime, animatorSpeed = animator.speed,
                    stateSpeed = info.speed, speedMultiplier = info.speedMultiplier, leftSole = float.PositiveInfinity, rightSole = float.PositiveInfinity,
                    tierLeft = new float[skins.Count], tierRight = new float[skins.Count],
                    tierNames = skins.Select(s => s.renderer.name).ToArray(), patchMeans = new Vector3[skins.Count][],
                    joints = Tracked.Select(b => root.transform.InverseTransformPoint(animator.GetBoneTransform(b).position)).ToArray(),
                    rotations = Tracked.Select(b => Quaternion.Inverse(root.transform.rotation) * animator.GetBoneTransform(b).rotation).ToArray() };
                for (int tier = 0; tier < skins.Count; tier++)
                {
                    var skin = skins[tier]; skin.renderer.BakeMesh(skin.baked, false); skin.baked.GetVertices(skin.vertices);
                    var matrix = root.transform.worldToLocalMatrix * skin.renderer.localToWorldMatrix;
                    frame.tierLeft[tier] = skin.left.Min(i => matrix.MultiplyPoint3x4(skin.vertices[i]).y);
                    frame.tierRight[tier] = skin.right.Min(i => matrix.MultiplyPoint3x4(skin.vertices[i]).y);
                    frame.patchMeans[tier] = skin.patches.Select(p => p.Aggregate(Vector3.zero, (sum, i) => sum + matrix.MultiplyPoint3x4(skin.vertices[i])) / p.Length).ToArray();
                    frame.leftSole = Mathf.Min(frame.leftSole, frame.tierLeft[tier]); frame.rightSole = Mathf.Min(frame.rightSole, frame.tierRight[tier]);
                }
                if (muscles)
                {
                    var pose = new HumanPose(); human.GetHumanPose(ref pose);
                    frame.bodyRotation = pose.bodyRotation; frame.humanScale = animator.humanScale; frame.muscleValues=(float[])pose.muscles.Clone();
                }
                Require(frame.joints.All(v => Finite(v.sqrMagnitude)) && Finite(frame.leftSole) && Finite(frame.rightSole), "Nonfinite pose.");
                return frame;
            }
            public void Dispose()
            {
                human?.Dispose();
                foreach (var skin in skins) if (skin.baked != null) Object.DestroyImmediate(skin.baked);
                if (root != null) Object.DestroyImmediate(root);
                if (controller != null) Object.DestroyImmediate(controller);
                if (state != null) Object.DestroyImmediate(state);
                if (machine != null) Object.DestroyImmediate(machine);
            }
        }

        private const string RestPath="Assets/Art/characters/authored/MotionReview20261006/SourceLibraryRest/UAL_SourceSprint.fbx";
        public static void Capture()
        {
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/character-candidates/motion-review-20261006/native-rain-rest-sprint-compare-v1"));Directory.CreateDirectory(output);
            var roots=new[]{"Assets/Resources/Characters","Assets/Art/animations","Assets/Art/characters/authored"};
            Dictionary<string,string> Inventory()=>roots.SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).Where(p=>!p.Replace('\\','/').StartsWith(Stage,StringComparison.Ordinal)&&p!=Stage.TrimEnd('/')+".meta").ToDictionary(p=>p,Hash);
            var before=Inventory();var receipt=new JObject{["state"]="capturing-not-accepted",["captured"]=false,["purpose"]="Exact licensed rest-Avatar Sprint versus integrated Sprint on current Rain world tiers. Same1x/.667s/world4.5, no pose/rate correction or production assignment. Dense floor/hinge feasibility is separate from continuous visual feel.",["variants"]=new JArray()};bool done=false;
            try
            {
                Require(Hash(SourcePath)=="8c4f53caa05a528ba7554378617efa32b318d5ecd11cb2b7c0872fafe9ad4a48"&&Hash(SourcePath+".meta")=="669360664ad82c37615b75bb32ce8852e34794b1c6ce74b379333ed66ea89613","Production source/importer changed.");
                Require(Hash(RestPath)=="aa7fd9448fb1694823ecad4d4d1c168c74b90be8dd4d30ca3064d4adf94f9161"&&Hash(RestPath+".meta")=="706b5a0005d2f1c5a6911e87bca4e5054fec98179a593dbdbc25399f9519d701","Frozen licensed rest source/importer changed.");
                var restImporter=(ModelImporter)AssetImporter.GetAtPath(RestPath);string avatar=AssetDatabase.GetAssetPath(restImporter.sourceAvatar);
                Require(avatar=="Assets/Art/characters/authored/MotionReview20261006/SourceLibraryRest/UAL_SourceRest.fbx","Rest source no longer uses measured unanimated Avatar.");
                Require(Hash(avatar)=="c7982fc0f414181a6ae65c28676ee6c62802bd635ea85b743d634273526d1de7","Source rest Avatar file changed.");
                string staged=Stage+"UAL_SourceSprint_Original0.fbx";
                if(!AssetDatabase.IsValidFolder(Stage.TrimEnd('/')))AssetDatabase.CreateFolder("Assets/Art/characters/authored/MotionReview20261006", "RainRestSprintCompareV1");
                if(!File.Exists(staged))Require(AssetDatabase.CopyAsset(RestPath,staged),"Could not copy source/importer into isolated comparison directory.");
                Require(Hash(staged)==Hash(RestPath),"Isolated source bytes differ.");
                var copy=(ModelImporter)AssetImporter.GetAtPath(staged);Require(AssetDatabase.GetAssetPath(copy.sourceAvatar)==avatar,"Copied importer lost exact rest Avatar.");
                var settings=copy.clipAnimations;Require(settings.Length==1,"Expected one source take.");
                Require(settings[0].keepOriginalOrientation&&settings[0].keepOriginalPositionY&&!settings[0].heightFromFeet&&settings[0].heightOffset==0,"Copied source root settings changed.");
                if(settings[0].rotationOffset!=0){settings[0].rotationOffset=0;copy.clipAnimations=settings;copy.SaveAndReimport();}
                receipt["isolated_source"]=staged;receipt["source_sha256"]=Hash(staged);receipt["source_avatar"]=avatar;
                var registry=AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");var entry=registry.looks.Single(e=>e.key.Split('|').Contains("head_rain"));
                var matrix=JObject.Parse(File.ReadAllText(Path.Combine(output,"../native-rest-contact-matrix/rain-original-y-footik.json")));patchIndices=new Dictionary<string,int[][]>();
                foreach(var skin in matrix["rest_surfaces"])patchIndices.Add((string)skin["skin"],PatchNames.Select(n=>skin["patches"].Single(p=>(string)p["name"]==n)["fixed_vertex_indices"].Values<int>().ToArray()).ToArray());
                var scene=EditorSceneManager.NewPreviewScene();
                try
                {
                    foreach(var variant in new[]{new[]{"production",SourcePath},new[]{"rest-source-original0",staged}})
                    {
                        var clip=AssetDatabase.LoadAllAssetsAtPath(variant[1]).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal));Require(Mathf.Abs(clip.length-2f/3f)<.00001f,"Source cadence differs from frozen2/3second Sprint.");
                        string stem=Path.Combine(output,"rain-"+variant[0]);var frames=Sample(entry.prefab,scene,"loco_run",clip,4.5f,4.5f,false,stem);
                        WriteFrames(stem+".json",frames,clip.length);var row=new JObject{["variant"]=variant[0],["clip_path"]=variant[1],["clip_seconds"]=clip.length,["foot_ik"]=false,["source_rate"]=1,["world_speed_mps"]=4.5};((JArray)receipt["variants"]).Add(row);Analyze(frames,clip.length,row);
                    }
                }
                finally{EditorSceneManager.ClosePreviewScene(scene);}
                done=true;
            }
            finally
            {
                var after=Inventory();var drift=before.Where(x=>!after.TryGetValue(x.Key,out string v)||v!=x.Value).Select(x=>x.Key).Concat(after.Keys.Where(k=>!before.ContainsKey(k))).ToArray();
                receipt["protected_drift"]=new JArray(drift);receipt["protected_hashes"]=JObject.FromObject(before);receipt["captured"]=done&&drift.Length==0;receipt["state"]=done&&drift.Length==0?"captured-not-accepted":"failed";
                File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString());Require(drift.Length==0,"Protected files changed.");
            }
        }
        private static void Analyze(Frame[] frames,float length,JObject row)
        {
            Require(frames.Length==Dense+1&&Mathf.Abs(frames[Dense].nativeNormalizedTime-frames[0].nativeNormalizedTime-1)<.001f,"Source failed full advancing native loop.");
            Require(frames[0].tierNames.Length==3,"Expected all current Rain world skins.");row["world_tiers"]=new JArray(frames[0].tierNames);row["native_samples"]=frames.Length;
            row["minimum_sole_m"]=frames.Min(f=>Mathf.Min(f.leftSole,f.rightSole));row["both_feet_above3cm_phases"]=frames.Take(Dense).Count(f=>Mathf.Min(f.leftSole,f.rightSole)>.03f);
            row["mean_forward_head_hip_angle_degrees"]=frames.Take(Dense).Average(f=>Mathf.Atan2(f.joints[2].z-f.joints[0].z,f.joints[2].y-f.joints[0].y)*Mathf.Rad2Deg);
            var sides=new JArray();row["feet"]=sides;
            foreach(int side in new[]{0,1})
            {
                int h=side==0?5:9;float maxAngle=0,maxLift=0,minCapped=1,bodyShift=0;var planeLocals=new List<Vector3>();
                foreach(var f in frames)
                {
                    var hip=f.joints[h];var knee=f.joints[h+1];var ankle=f.joints[h+2];float u=Vector3.Distance(hip,knee),l=Vector3.Distance(knee,ankle),angle=Vector3.Angle(hip-knee,ankle-knee),sole=side==0?f.leftSole:f.rightSole,lift=Mathf.Max(0,.0005f-sole);
                    float NewAngle(float y){float d=Vector3.Distance(hip,ankle+Vector3.up*y);return Mathf.Acos(Mathf.Clamp((u*u+l*l-d*d)/(2*u*l),-1,1))*Mathf.Rad2Deg;}
                    maxAngle=Mathf.Max(maxAngle,Mathf.Abs(angle-NewAngle(lift)));maxLift=Mathf.Max(maxLift,lift);float lo=0,hi=.1f;
                    for(int n=0;n<30;n++){float m=(lo+hi)*.5f;if(angle-NewAngle(m)>15)hi=m;else lo=m;}
                    minCapped=Mathf.Min(minCapped,sole+Mathf.Min(lift,lo));bodyShift=Mathf.Max(bodyShift,lift-lo);
                    if(angle>70&&angle<150)planeLocals.Add((Quaternion.Inverse(f.rotations[h])*Vector3.Cross(knee-hip,ankle-knee)).normalized);
                }
                Require(planeLocals.Count>=8,"Insufficient bent-knee phases for a measured anatomical hinge axis.");Vector3 mean=planeLocals.Aggregate(Vector3.zero,(a,b)=>a+b).normalized;
                float spread=planeLocals.Max(p=>Vector3.Angle(mean,p));
                sides.Add(new JObject{["side"]=side==0?"left":"right",["underfloor_phases"]=frames.Take(Dense).Count(f=>(side==0?f.leftSole:f.rightSole)<=0),["ankle_z_excursion_m"]=frames.Max(f=>f.joints[h+2].z)-frames.Min(f=>f.joints[h+2].z),["maximum_floor_lift_m"]=maxLift,["analytic_knee_change_degrees_for_floor_target"]=maxAngle,["existing15degree_gate_feasible_for_scalar_target"]=maxAngle<=15,["predicted_minimum_sole_if_capped15_m"]=minCapped,["minimum_constant_upward_body_shift_to15_m"]=Mathf.Max(0,bodyShift),["measured_thigh_local_hinge_normal"]=new JArray(mean.x,mean.y,mean.z),["hinge_local_max_spread_degrees"]=spread,["hinge_sample_count"]=planeLocals.Count});
            }
            row["scope"]="Actual untouched native source geometry + analytic fixed-length target feasibility. No corrective solve/bake/source selection accepted; continuous review/cadence and real skin response required.";
        }
        private static Frame[] Sample(GameObject prefab, Scene scene, string state, AnimationClip clip, float speed, float reference, bool ik, string proofStem = null)
        {
            using var rig = new Rig(prefab, scene, state, clip, speed, reference, ik);
            var frames = new Frame[Dense + 1]; rig.Warm(Dense);
            for (int i = 0; i <= Dense; i++)
            {
                float phase = i / (float)Dense; frames[i] = rig.Read(phase);
                // Same eight equal phases for both variants, all authored tiers. Also retained on a numeric failure.
                if (proofStem != null && i < Dense && i % (Dense / 8) == 0)
                    for (int tier = 0; tier < rig.skins.Count; tier++)
                        WriteObj(proofStem + "-tier" + tier + "-phase" + i.ToString("D3") + ".obj.gz", rig.root.transform, rig.skins[tier]);
                if (i < Dense) rig.Step(Dense);
            }
            return frames;
        }
        private static void WriteFrames(string path, Frame[] frames, float period) => File.WriteAllText(path, JsonConvert.SerializeObject(new
        {
            clip_seconds = period, source_rate = 1, joints = Tracked.Select(b => b.ToString()).ToArray(), coordinates = "root-local metres; min sole across all authored world tiers",
            frames = frames.Select(f => new { f.phase, f.nativeNormalizedTime, f.animatorSpeed, f.stateSpeed, f.speedMultiplier,
                f.leftSole, f.rightSole, f.tierLeft, f.tierRight,
                surfaces = f.tierNames.Select((name, tier) => new { skin = name, left_min_y = f.tierLeft[tier], right_min_y = f.tierRight[tier],
                    patches = PatchNames.Select((patch, index) => new { patch, mean = f.patchMeans[tier][index] })
                        .ToDictionary(p => p.patch, p => new { mean = new[] { p.mean.x, p.mean.y, p.mean.z } }) }),
                joints = f.joints.Select(v => new[] { v.x, v.y, v.z }).ToArray(),
                rotations = f.rotations.Select(q => new[] { q.x, q.y, q.z, q.w }).ToArray(),
                bodyRotation = new[] { f.bodyRotation.x, f.bodyRotation.y, f.bodyRotation.z, f.bodyRotation.w }, f.humanScale, f.muscleValues })
        }, Formatting.Indented));
        private static void WriteObj(string path, Transform root, Skin skin)
        {
            var matrix = root.worldToLocalMatrix * skin.renderer.localToWorldMatrix;
            using var file = File.Create(path);
            using var zip = new GZipStream(file, System.IO.Compression.CompressionLevel.Fastest);
            using var writer = new StreamWriter(zip);
            string N(float x) => x.ToString("R", CultureInfo.InvariantCulture);
            foreach (var raw in skin.vertices)
            {
                Vector3 v = matrix.MultiplyPoint3x4(raw); writer.WriteLine($"v {N(v.x)} {N(v.y)} {N(v.z)}");
            }
            foreach (var uv in skin.renderer.sharedMesh.uv) writer.WriteLine($"vt {N(uv.x)} {N(uv.y)}");
            foreach (var raw in skin.baked.normals)
            {
                Vector3 n = matrix.MultiplyVector(raw).normalized; writer.WriteLine($"vn {N(n.x)} {N(n.y)} {N(n.z)}");
            }
            int[] triangles = skin.renderer.sharedMesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i] + 1, b = triangles[i + 1] + 1, c = triangles[i + 2] + 1;
                writer.WriteLine($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}");
            }
        }
        private static string Hash(string path) { using var hash = SHA256.Create(); using var file = File.OpenRead(path); return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant(); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Contact candidate: " + message); }
    }
}
