// Explicit source typing-loop closure; contact and furniture fit remain separate.
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
    public static class TypingLoopClosureCandidate
    {
        private const int Keys = 120, Dense = 240;
        private const string Stage = "Assets/Art/characters/authored/MotionReview20261006/TypingLoopClosureV1/";
        private const string SourcePath = "Assets/Art/animations/act_type.fbx";
        private const string Action = "act_type";
        private static readonly string[] PatchNames = { "Foot.L", "Toes.L", "Foot.R", "Toes.R" };
        private static Dictionary<string, int[][]> patchIndices;
        private static readonly HumanBodyBones[] Tracked = {
            HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm };

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
                controller = new AnimatorController { name = "UNSAVED_SEATED_GOALS_V1" };
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

        public static void BuildCandidates()
        {
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../ct-life-evidence/art/character-candidates/motion-review-20261006/native-typing-loop-closure-v1"));Directory.CreateDirectory(output);
            var roots=new[]{"Assets/Resources/Characters","Assets/Art/animations","Assets/Art/characters/authored"};
            Dictionary<string,string> Snapshot()=>roots.SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).Where(p=>!p.Replace('\\','/').StartsWith(Stage,StringComparison.Ordinal)&&p!=Stage.TrimEnd('/')+".meta").ToDictionary(p=>p,Hash);
            var before=Snapshot();var receipt=new JObject{["state"]="sampling",["all_pass"]=false,["cases"]=new JArray(),
                ["scope"]="Close the original1.2s typing whole-body curve seam only. Preserve source exactly away from first/last10% of loop. No foot-contact/hand/seat acceptance or production assignment."};
            bool pass=true;AnimationClip candidate=null;
            try
            {
                Require(Hash(SourcePath)=="f69c0328ab536eb34f465c3f8e0261f08d5a853a8bd6c7a970e6b4760f0f0902"&&Hash(SourcePath+".meta")=="f10b3a8b6d8066c1b5a73dda1c177669bd52d5fa57e7d810de92cc7b76249127","Original typing source/importer changed.");
                var source=AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal));
                candidate=CloseLoop(source,receipt);receipt["source_clip_length"]=source.length;
                var registry=AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");patchIndices=new Dictionary<string,int[][]>();
                foreach(string character in new[]{"snow","rain"})
                {
                    var matrix=JObject.Parse(File.ReadAllText(Path.Combine(output,"../native-rest-contact-matrix/"+character+"-original-y-footik.json")));
                    foreach(var skin in matrix["rest_surfaces"])patchIndices.Add((string)skin["skin"],PatchNames.Select(name=>skin["patches"].Single(q=>(string)q["name"]==name)["fixed_vertex_indices"].Values<int>().ToArray()).ToArray());
                    var item=new JObject{["character"]=character,["all_pass"]=false};((JArray)receipt["cases"]).Add(item);
                    try{Review(character,registry.looks.Single(e=>e.key.Split('|').Contains("head_"+character)).prefab,source,candidate,output,item);item["all_pass"]=true;}
                    catch(Exception e){pass=false;item["failure"]=e.ToString();Debug.LogError("CTLIFE [typing-loop] "+character+": "+e);}
                }
                if(pass)
                {
                    Directory.CreateDirectory(Stage);string path=Stage+"TypingClosedSourceV1.anim";SaveClip(candidate,path);candidate=null;
                    receipt["candidate_asset"]=path;receipt["candidate_sha256"]=Hash(path);
                }
            }
            catch(Exception e){pass=false;receipt["failure"]=e.ToString();throw;}
            finally
            {
                if(candidate!=null)Object.DestroyImmediate(candidate);var after=Snapshot();var drift=before.Where(p=>!after.TryGetValue(p.Key,out string v)||v!=p.Value).Select(p=>p.Key).Concat(after.Keys.Where(p=>!before.ContainsKey(p))).ToArray();
                receipt["protected_drift"]=new JArray(drift);receipt["all_pass"]=pass&&drift.Length==0;receipt["state"]=pass&&drift.Length==0?"native-loop-pass-contact-pending":"failed";
                File.WriteAllText(Path.Combine(output,"candidate-receipt.json"),receipt.ToString());Require(drift.Length==0,"Protected source/candidate assets changed.");
            }
            Require(pass,"Closed typing source failed; original/closed full-cycle data retained.");
        }
        private static AnimationClip CloseLoop(AnimationClip source,JObject receipt)
        {
            var clip=Object.Instantiate(source);clip.name="TypingClosedSourceV1";
            try
            {
                float length=source.length,window=length*.1f;var changed=new JArray();float awayError=0;
                foreach(var binding in AnimationUtility.GetCurveBindings(source))
                {
                    Require(binding.type==typeof(Animator),"Expected Humanoid source curves only; explicit review required for other bindings.");
                    var original=AnimationUtility.GetEditorCurve(source,binding);var keys=original.keys;
                    Require(keys.Length>=2&&keys.All(k=>k.weightedMode==WeightedMode.None&&Finite(k.inTangent)&&Finite(k.outTangent)),"Expected finite unweighted source curves.");
                    Require(Mathf.Abs(keys[0].time)<.000001f&&Mathf.Abs(keys[keys.Length-1].time-length)<.000001f,"Unexpected source curve domain.");
                    if(Mathf.Abs(original.Evaluate(0)-original.Evaluate(length))<1e-7f&&Mathf.Abs(keys[0].outTangent-keys[keys.Length-1].inTangent)<1e-6f)continue;
                    float ta=length-window,tb=length+window,va=original.Evaluate(ta),vb=original.Evaluate(window),ma=Slope(keys,ta,true),mb=Slope(keys,window,false),h=tb-ta;
                    float Bridge(float time)
                    {
                        float t=(time-ta)/h;
                        return (2*t*t*t-3*t*t+1)*va+(t*t*t-2*t*t+t)*h*ma+(-2*t*t*t+3*t*t)*vb+(t*t*t-t*t)*h*mb;
                    }
                    float Derivative(float time)
                    {
                        float t=(time-ta)/h;
                        return ((6*t*t-6*t)*va+(-6*t*t+6*t)*vb)/h+(3*t*t-4*t+1)*ma+(3*t*t-2*t)*mb;
                    }
                    float center=Bridge(length),tangent=Derivative(length);
                    var revised=new List<Keyframe>{new Keyframe(0,center,tangent,tangent),new Keyframe(window,vb,mb,mb)};
                    revised.AddRange(keys.Where(k=>k.time>window+.000001f&&k.time<ta-.000001f));
                    revised.Add(new Keyframe(ta,va,ma,ma));revised.Add(new Keyframe(length,center,tangent,tangent));
                    var closed=new AnimationCurve(revised.ToArray()){preWrapMode=original.preWrapMode,postWrapMode=original.postWrapMode};
                    for(int i=0;i<=1000;i++){float t=Mathf.Lerp(window,ta,i/1000f);awayError=Mathf.Max(awayError,Mathf.Abs(closed.Evaluate(t)-original.Evaluate(t)));}
                    AnimationUtility.SetEditorCurve(clip,binding,closed);changed.Add(new JObject{["property"]=binding.propertyName,["old_value_seam"]=original.Evaluate(length)-original.Evaluate(0),["old_derivative_seam"]=keys[keys.Length-1].inTangent-keys[0].outTangent});
                }
                receipt["closed_curves"]=changed;receipt["preserved_phase_interval"]=new JArray(.1f,.9f);receipt["maximum_curve_error_outside_seam"]=awayError;
                Require(changed.Count>0&&awayError<.000005f,"Closure missing or altered source curve outside seam.");
                Require(clip.humanMotion&&clip.isLooping&&Mathf.Abs(clip.length-source.length)<.000001f,"Closure changed Humanoid loop/period.");return clip;
            }
            catch{Object.DestroyImmediate(clip);throw;}
        }
        private static void Review(string character,GameObject prefab,AnimationClip source,AnimationClip closed,string output,JObject receipt)
        {
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var original=Sample(prefab,scene,source,false,Path.Combine(output,character+"-original-noik"),out float originalSurface);
                var final=Sample(prefab,scene,closed,false,Path.Combine(output,character+"-closed-noik"),out float closedSurface);
                var ik=Sample(prefab,scene,closed,true,Path.Combine(output,character+"-closed-footik"),out float ikSurface);
                WriteFrames(Path.Combine(output,character+"-original-noik.json"),original,source.length);
                WriteFrames(Path.Combine(output,character+"-closed-noik.json"),final,closed.length);WriteFrames(Path.Combine(output,character+"-closed-footik.json"),ik,closed.length);
                float away=0,maximum=0,kneeDelta=0;var perJoint=new JArray();
                for(int j=0;j<Tracked.Length;j++)
                {
                    float delta=0;
                    for(int i=0;i<=Dense;i++)
                    {
                        float d=Vector3.Distance(original[i].joints[j],final[i].joints[j]);delta=Mathf.Max(delta,d);maximum=Mathf.Max(maximum,d);
                        if(i>=Dense/10&&i<=Dense*9/10)away=Mathf.Max(away,d);
                    }
                    float oldSeam=Vector3.Distance(original[0].joints[j],original[Dense].joints[j]);
                    perJoint.Add(new JObject{["joint"]=Tracked[j].ToString(),["old_seam_m"]=oldSeam,["new_seam_m"]=Vector3.Distance(final[0].joints[j],final[Dense].joints[j]),["maximum_delta_m"]=delta});
                    Require(delta<((j<5||j>=13)?.01f:.015f),"Whole-body closure displacement exceeds observed source seam neighborhood.");
                }
                float Angle(Frame f,int hip)=>Vector3.Angle(f.joints[hip]-f.joints[hip+1],f.joints[hip+2]-f.joints[hip+1]);
                for(int i=0;i<=Dense;i++)foreach(int hip in new[]{5,9})kneeDelta=Mathf.Max(kneeDelta,Mathf.Abs(Angle(original[i],hip)-Angle(final[i],hip)));
                float jointSeam=Enumerable.Range(0,Tracked.Length).Max(j=>Vector3.Distance(final[0].joints[j],final[Dense].joints[j]));
                float dt=closed.length/Dense;
                float Velocity(Frame[] fs)=>Enumerable.Range(0,Tracked.Length).Max(j=>Vector3.Distance((fs[1].joints[j]-fs[0].joints[j])/dt,(fs[Dense].joints[j]-fs[Dense-1].joints[j])/dt));
                float oldVelocity=Velocity(original),newVelocity=Velocity(final);
                receipt["joint_comparison"]=perJoint;receipt["away_from_seam_max_joint_error_m"]=away;receipt["maximum_joint_change_m"]=maximum;
                receipt["old_all_skin_loop_seam_m"]=originalSurface;receipt["closed_all_skin_loop_seam_m"]=closedSurface;receipt["closed_footik_all_skin_loop_seam_m"]=ikSurface;
                receipt["old_loop_velocity_mismatch_mps"]=oldVelocity;receipt["closed_loop_velocity_mismatch_mps"]=newVelocity;receipt["maximum_knee_angle_change_degrees"]=kneeDelta;
                Require(away<=.0001f,"Closing seam changed original body intent away from first/last10%.");
                Require(jointSeam<=.0002f&&closedSurface<=.0002f&&ikSurface<=.0002f,"Whole-body/hand/skin loop remains discontinuous.");
                Require(newVelocity<=.05f&&kneeDelta<=5f,"Loop repair introduced excessive velocity/knee change.");
                receipt["contact_status"]="Not accepted: this isolated source still has original retargeted foot penetration, pending a new grounded goal solve against the closed source.";
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        private static Frame[] Sample(GameObject prefab,Scene scene,AnimationClip clip,bool ik,string stem,out float surfaceSeam)
        {
            using var rig=new Rig(prefab,scene,Action,clip,4.5f,4.5f,ik);rig.Warm(Dense);var fs=new Frame[Dense+1];Vector3[][] start=null;surfaceSeam=0;
            Vector3[][] Vertices()=>rig.skins.Select(s=>s.vertices.Select((rig.root.transform.worldToLocalMatrix*s.renderer.localToWorldMatrix).MultiplyPoint3x4).ToArray()).ToArray();
            for(int i=0;i<=Dense;i++)
            {
                fs[i]=rig.Read(i/(float)Dense,true);
                if(i==0)start=Vertices();if(i==Dense){var end=Vertices();for(int tier=0;tier<start.Length;tier++)for(int j=0;j<start[tier].Length;j++)surfaceSeam=Mathf.Max(surfaceSeam,Vector3.Distance(start[tier][j],end[tier][j]));}
                if(i<Dense&&i%(Dense/8)==0)for(int tier=0;tier<rig.skins.Count;tier++)WriteObj(stem+"-tier"+tier+"-phase"+i.ToString("D3")+".obj.gz",rig.root.transform,rig.skins[tier]);
                if(i<Dense)rig.Step(Dense);
            }
            return fs;
        }
        private static float Slope(Keyframe[] keys,float time,bool incoming)
        {
            foreach(var k in keys)if(Mathf.Abs(k.time-time)<.000001f)return incoming?k.inTangent:k.outTangent;
            int i=0;while(i+1<keys.Length&&keys[i+1].time<time)i++;Require(i+1<keys.Length,"Derivative outside source domain.");
            var a=keys[i];var b=keys[i+1];float h=b.time-a.time,t=(time-a.time)/h;
            return ((6*t*t-6*t)*a.value+(-6*t*t+6*t)*b.value)/h+(3*t*t-4*t+1)*a.outTangent+(3*t*t-2*t)*b.inTangent;
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
                bodyRotation = new[] { f.bodyRotation.x, f.bodyRotation.y, f.bodyRotation.z, f.bodyRotation.w }, f.humanScale })
        }, Formatting.Indented));
        private static void SaveClip(AnimationClip clip, string path)
        {
            var previous = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (previous == null) AssetDatabase.CreateAsset(clip, path);
            else { EditorUtility.CopySerialized(clip, previous); EditorUtility.SetDirty(previous); AssetDatabase.SaveAssetIfDirty(previous); Object.DestroyImmediate(clip); }
        }
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
