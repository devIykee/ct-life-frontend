// Staged fallback after rest-Jog matrix: per-foot stance goals, no body/root shift.
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
    public static class SprintGoalResponseReview
    {
        private const int Keys = 120, Dense = 240;
        private const string Stage = "Assets/Art/characters/authored/MotionReview20261006/SprintGoalResponseReviewNoAssets/";
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
                controller = new AnimatorController { name = "UNSAVED_SPRINT_GOALS_V1" };
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
            public Frame Read(float phase, bool muscles = false, float expectedCycle = 2f)
            {
                var info = animator.GetCurrentAnimatorStateInfo(0);
                Require(Mathf.Abs(info.normalizedTime - (expectedCycle + phase)) < .001f, "Sample label differs from actual native state time.");
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
                    frame.bodyRotation = pose.bodyRotation; frame.humanScale = animator.humanScale;
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

        public static void Capture()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/character-candidates/motion-review-20261006/native-sprint-goal-response"));
            Directory.CreateDirectory(output);
            var roots = new[] { "Assets/Resources/Characters", "Assets/Art/animations", "Assets/Art/characters/authored" };
            var hashes = roots.SelectMany(p => Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                .Where(p => !p.Replace('\\', '/').StartsWith(Stage, StringComparison.Ordinal) && p != Stage.TrimEnd('/') + ".meta").ToDictionary(p => p, Hash);
            var receipt = new JObject { ["state"] = "sampling-source-diagnostic", ["all_pass"] = false, ["purpose"] = "Isolated Humanoid foot translation/quaternion curve response. Exact constant linear curve operations, no asset writes. Tests the units/body frame before reconstructing original FK foot goals.", ["cases"] = new JArray() };
            bool passed = true;
            try
            {
                Require(Hash(SourcePath) == "8c4f53caa05a528ba7554378617efa32b318d5ecd11cb2b7c0872fafe9ad4a48", "Production Sprint source changed.");
                Require(Hash(SourcePath + ".meta") == "669360664ad82c37615b75bb32ce8852e34794b1c6ce74b379333ed66ea89613", "Production Sprint importer changed.");
                var importer = (ModelImporter)AssetImporter.GetAtPath(SourcePath);
                var settings = importer.clipAnimations.Single();
                Require(settings.keepOriginalOrientation && settings.rotationOffset == 180 && settings.keepOriginalPositionY &&
                    !settings.heightFromFeet && settings.lockRootHeightY && settings.heightOffset == 0,
                    "Expected frozen Original-Y, Original180 orientation and zero height offset.");
                receipt["source_avatar"] = AssetDatabase.GetAssetPath(importer.sourceAvatar);
                var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
                patchIndices = new Dictionary<string, int[][]>();
                foreach (string character in new[] { "snow", "rain" })
                {
                    string matrixPath = Path.Combine(output, "../native-rest-contact-matrix/" + character + "-original-y-footik.json");
                    var matrix = JObject.Parse(File.ReadAllText(matrixPath));
                    foreach (var skin in matrix["rest_surfaces"])
                        patchIndices.Add((string)skin["skin"], PatchNames.Select(name => skin["patches"].Single(p => (string)p["name"] == name)["fixed_vertex_indices"].Values<int>().ToArray()).ToArray());
                    var result = new JObject { ["character"] = character, ["all_pass"] = false };
                    ((JArray)receipt["cases"]).Add(result);
                    try
                    {
                        var entry = registry.looks.Single(e => e.key.Split('|').Contains("head_" + character));
                        Build(character, entry.prefab, output, result);
                        result["all_pass"] = true;
                    }
                    catch (Exception error) { passed = false; result["failure"] = error.ToString(); Debug.LogError("CTLIFE [sprint-goals] " + character + ": " + error); }
                }
            }
            catch (Exception error) { passed = false; receipt["failure"] = error.ToString(); throw; }
            finally
            {
                var after = roots.SelectMany(p => Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                    .Where(p => !p.Replace('\\', '/').StartsWith(Stage, StringComparison.Ordinal) && p != Stage.TrimEnd('/') + ".meta").ToDictionary(p => p, Hash);
                var drift = hashes.Where(p => !after.TryGetValue(p.Key, out string value) || value != p.Value).Select(p => p.Key)
                    .Concat(after.Keys.Where(p => !hashes.ContainsKey(p))).ToArray();
                receipt["protected_drift"] = new JArray(drift); receipt["protected_hashes"] = JObject.FromObject(hashes);
                receipt["all_pass"] = passed && drift.Length == 0;
                receipt["state"] = passed && drift.Length == 0 ? "native-goal-response-pass-no-candidate" : "failed";
                File.WriteAllText(Path.Combine(output, "candidate-receipt.json"), receipt.ToString());
                Require(drift.Length == 0, "Protected production/candidate files changed; inspect receipt.");
            }
            Require(passed, "At least one isolated candidate failed. No production motion/controller/registry was assigned; inspect candidate-receipt.json.");
            Debug.Log("CTLIFE [sprint-goals] Native goal response captured; no motion candidate or contact/cadence acceptance.");
        }

        private static void Build(string character, GameObject prefab, string output, JObject receipt)
        {
            var source=AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal));
            var scene=EditorSceneManager.NewPreviewScene();var rows=new JArray();receipt["responses"]=rows;bool pass=true;
            try
            {
                foreach(float phase in new[]{.1f,.6f})
                using(var rig=new Rig(prefab,scene,"loco_run",source,4.5f,4.5f,true))
                {
                    rig.Pose(phase);var original=rig.Read(phase,true);
                    for(int side=0;side<2;side++)for(int axis=0;axis<3;axis++)foreach(float sign in new[]{-1f,1f})foreach(bool rotation in new[]{false,true})
                    {
                        int foot=side==0?7:11;Vector3 unit=axis==0?Vector3.right:axis==1?Vector3.up:Vector3.forward;
                        var worldRotation=Quaternion.AngleAxis(sign,unit);var worldTranslation=unit*(sign*.001f);
                        var localRotation=Quaternion.Inverse(original.bodyRotation)*worldRotation*original.bodyRotation;
                        var localTranslation=Quaternion.Inverse(original.bodyRotation)*worldTranslation/original.humanScale;
                        var probe=Object.Instantiate(source);probe.name="UNSAVED_SPRINT_GOAL_RESPONSE";
                        try
                        {
                            if(rotation)RotateGoal(probe,source,side,localRotation);else TranslateGoal(probe,source,side,localTranslation);
                            using var target=new Rig(prefab,scene,"loco_run",probe,4.5f,4.5f,true);target.Pose(phase);var actual=target.Read(phase);
                            Vector3 displacement=actual.joints[foot]-original.joints[foot];
                            float positionError=Vector3.Distance(displacement,rotation?Vector3.zero:worldTranslation);
                            float rotationError=Quaternion.Angle(actual.rotations[foot],rotation?worldRotation*original.rotations[foot]:original.rotations[foot]);
                            float body=Enumerable.Range(0,5).Max(j=>Vector3.Distance(original.joints[j],actual.joints[j]));
                            bool valid=body<=.00025f&&rotationError<=.1f&&(rotation||positionError<=.00025f);
                            pass&=valid;
                            rows.Add(new JObject{["kind"]=rotation?"rotation":"translation",["side"]=side==0?"left":"right",["axis"]="xyz"[axis].ToString(),["sign"]=sign,
                                ["phase"]=phase,["rotation_request_degrees"]=rotation?sign:0,["translation_request_m"]=rotation?0:sign*.001f,
                                ["ankle_displacement_xyz_m"]=new JArray(displacement.x,displacement.y,displacement.z),["position_response_error_m"]=positionError,
                                ["rotation_response_error_degrees"]=rotationError,["upper_body_delta_m"]=body,["pass"]=valid});
                        }
                        finally { Object.DestroyImmediate(probe); }
                    }
                }
                File.WriteAllText(Path.Combine(output,character+"-responses.json"),rows.ToString());
                Require(pass,"Native T/Q response differs from requested world-space change; do not reconstruct goals from assumed semantics.");
                receipt["scope"]="Signed1mm translations and1degree rotations at two native phases, both feet/allaxes; no goal candidate or production changes. Rotation-induced ankle displacement is measured separately, not assumed zero.";
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        private static EditorCurveBinding Binding(AnimationClip clip,string name)
        {
            var matches=AnimationUtility.GetCurveBindings(clip).Where(b=>b.type==typeof(Animator)&&b.propertyName==name).ToArray();
            Require(matches.Length==1,"Missing unique Humanoid goal binding "+name);return matches[0];
        }
        private static void TranslateGoal(AnimationClip target,AnimationClip source,int side,Vector3 delta)
        {
            string prefix=side==0?"LeftFootT.":"RightFootT.";
            for(int axis=0;axis<3;axis++)
            {
                var binding=Binding(source,prefix+"xyz"[axis]);var curve=AnimationUtility.GetEditorCurve(source,binding);var keys=curve.keys;
                for(int i=0;i<keys.Length;i++)keys[i].value+=delta[axis];curve.keys=keys;AnimationUtility.SetEditorCurve(target,binding,curve);
            }
        }
        private static void RotateGoal(AnimationClip target,AnimationClip source,int side,Quaternion delta)
        {
            string prefix=side==0?"LeftFootQ.":"RightFootQ.";
            var bindings="xyzw".Select(c=>Binding(source,prefix+c)).ToArray();var curves=bindings.Select(b=>AnimationUtility.GetEditorCurve(source,b)).ToArray();
            Require(curves.SelectMany(c=>c.keys).All(k=>k.weightedMode==WeightedMode.None&&Finite(k.inTangent)&&Finite(k.outTangent)),"Expected unweighted finite imported quaternion goal curves.");
            var times=curves.SelectMany(c=>c.keys.Select(k=>k.time)).Distinct().OrderBy(t=>t).ToArray();
            // Left multiplication by a constant unit quaternion is a linear combination of four source cubics.
            // Retain union knots and both derivatives, preserving native curve timing exactly.
            float[][] weights={new[]{delta.w,-delta.z,delta.y,delta.x},new[]{delta.z,delta.w,-delta.x,delta.y},new[]{-delta.y,delta.x,delta.w,delta.z},new[]{-delta.x,-delta.y,-delta.z,delta.w}};
            for(int axis=0;axis<4;axis++)
            {
                var keys=times.Select(t=>new Keyframe(t,Enumerable.Range(0,4).Sum(c=>weights[axis][c]*curves[c].Evaluate(t)),
                    Enumerable.Range(0,4).Sum(c=>weights[axis][c]*Slope(curves[c].keys,t,true)),Enumerable.Range(0,4).Sum(c=>weights[axis][c]*Slope(curves[c].keys,t,false)))).ToArray();
                var curve=new AnimationCurve(keys){preWrapMode=curves[axis].preWrapMode,postWrapMode=curves[axis].postWrapMode};AnimationUtility.SetEditorCurve(target,bindings[axis],curve);
            }
        }
        private static float Slope(Keyframe[] keys,float time,bool incoming)
        {
            foreach(var k in keys)if(Mathf.Abs(k.time-time)<.000001f)return incoming?k.inTangent:k.outTangent;
            int i=0;while(i+1<keys.Length&&keys[i+1].time<time)i++;
            Require(i+1<keys.Length,"Derivative outside source domain.");var a=keys[i];var b=keys[i+1];float h=b.time-a.time,t=(time-a.time)/h;
            return ((6*t*t-6*t)*a.value+(-6*t*t+6*t)*b.value)/h+(3*t*t-4*t+1)*a.outTangent+(3*t*t-2*t)*b.inTangent;
        }

        private static string Hash(string path) { using var hash = SHA256.Create(); using var file = File.OpenRead(path); return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant(); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Contact candidate: " + message); }
    }
}
