// Isolated deterministic native controller/render review. No registry or production assignment.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Reflection;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    public static class SnowRunPlaybackReview
    {
        private const string Stage = "Assets/Art/characters/authored/MotionReview20261006/SnowPlaybackV1/";
        private const string Shared = "Assets/Resources/Characters/AvatarController.controller";
        private const string Candidate = "Assets/Art/characters/authored/MotionReview20261006/RestJogContactGoalsV3/Snow_RestJogContact_Goals_Candidate.anim";
        private const string CandidateSha = "6497e285a7a686dfce9ef2ff7fbfc14e1fe095fbdae05b278a4a02122ebe056c";
        private const float Speed = 4.5f, Reference = 5.84536284f;
        private const int Fps = 30, Frames = 120, Width = 1068, Height = 480;
        private static readonly float[] ContactPhases = { 11f / 240f, 130f / 240f };
        private sealed class Actor
        {
            public GameObject root;
            public Animator animator;
            public AnimationClip clip;
            public SkinnedMeshRenderer skin;
            public Mesh baked;
            public int[] left, right;
            public int[][] patches;
            public readonly List<Vector3> vertices = new List<Vector3>();
            public float rate, previousTime;
            public string name;
            public readonly RuntimeFoley currentFoley = new RuntimeFoley();
        }
        // Invoke the actual runtime implementation without adding an Editor assembly dependency.
        private sealed class RuntimeFoley
        {
            private readonly object cadence;
            private readonly MethodInfo advance;
            public RuntimeFoley()
            {
                var type = Type.GetType("CTLife.Audio.FootstepCadence, CTLife.Audio", true);
                cadence = Activator.CreateInstance(type);
                advance = type.GetMethod("Advance", new[] { typeof(float), typeof(float), typeof(bool), typeof(bool) });
                Require(advance != null && advance.ReturnType == typeof(bool), "Runtime Foley API changed.");
            }
            public bool Advance(float metres, float seconds, bool moving, bool running) =>
                (bool)advance.Invoke(cadence, new object[] { metres, seconds, moving, running });
        }
        public static void Capture()
        {
            Require(!Application.isPlaying, "Run from a dedicated graphics-enabled Editor, outside Play mode.");
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string output = Path.Combine(repo, "art/character-candidates/motion-review-20261006/native-snow-playback-v1");
            Directory.CreateDirectory(output);
            var roots = new[] { "Assets/Resources/Characters", "Assets/Art/animations", "Assets/Art/characters/authored" };
            var before = Snapshot(roots);
            var receipt = new JObject { ["state"] = "capturing", ["world_speed_mps"] = Speed, ["candidate_reference_mps"] = Reference,
                ["fps"] = Fps, ["frames"] = Frames, ["width"] = Width, ["height"] = Height, ["contact_phases"] = new JArray(ContactPhases),
                ["scope"] = "Native complete Snow controller, continuous Animator.Update, exact4.5m/s translated roots, fixed world-LOD1; Cafe camera projection and side view over measured grid. Deterministic render proof, not interactive NavMesh/Seeker/performance acceptance." };
            Scene scene = default; var actors = new List<Actor>(); var owned = new List<Object>();
            try
            {
                Require(Hash(Candidate) == CandidateSha, "Accepted Snow clip changed.");
                var evidence = JObject.Parse(File.ReadAllText(Path.Combine(output, "../native-rest-jog-goals-v3/candidate-receipt.json")));
                Require(evidence["cases"].Any(c => (string)c["character"] == "snow" && (bool)c["all_pass"]), "Snow numeric gate did not pass.");
                var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
                var entry = registry.looks.Single(e => e.key.Split('|').Contains("head_snow"));
                var original = entry.controllerOverride as AnimatorOverrideController;
                var shared = AssetDatabase.LoadAssetAtPath<AnimatorController>(Shared);
                Require(original != null && original.runtimeAnimatorController == shared, "Unexpected Snow controller lineage.");
                Require(!Directory.Exists(Stage), "Use a fresh stage path; existing candidate controller must not be overwritten.");
                Directory.CreateDirectory(Stage); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                string controllerPath = Stage + "SnowRunReview.controller";
                Require(AssetDatabase.CopyAsset(Shared, controllerPath), "Could not copy the complete existing controller.");
                var copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                var states = States(copy).ToArray(); var run = states.Single(s => s.name == "loco_run");
                var fingerprints = states.Where(s => s != run).ToDictionary(s => s.name, Fingerprint);
                var newClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Candidate);
                run.motion = newClip; run.iKOnFeet = true; run.speed = 1f / Reference;
                run.speedParameter = "speed"; run.speedParameterActive = true;
                EditorUtility.SetDirty(run); EditorUtility.SetDirty(copy); AssetDatabase.SaveAssetIfDirty(copy);
                Require(states.Where(s => s != run).All(s => Fingerprint(s) == fingerprints[s.name]), "Unrelated copied state changed.");
                var prior = new List<KeyValuePair<AnimationClip, AnimationClip>>(); original.GetOverrides(prior);
                var candidate = new AnimatorOverrideController(copy) { name = "Snow Run Playback Candidate" };
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(); candidate.GetOverrides(pairs);
                candidate.ApplyOverrides(pairs.Select(p => new KeyValuePair<AnimationClip, AnimationClip>(p.Key,
                    prior.FirstOrDefault(old => old.Key == p.Key).Value)).ToList());
                string overridePath = Stage + "SnowRunReview.overrideController"; AssetDatabase.CreateAsset(candidate, overridePath);
                Require(candidate[newClip] == null || candidate[newClip] == newClip, "Candidate run received an unrelated override.");
                receipt["candidate_controller"] = overridePath; receipt["candidate_clip_sha256"] = CandidateSha;
                receipt["preserved_explicit_overrides"] = new JArray(prior.Where(p => p.Value != null && p.Value != p.Key).Select(p => AssetDatabase.GetAssetPath(p.Value)));
                var raw = States(shared).Single(s => s.name == "loco_run").motion as AnimationClip;
                var baselineClip = original[raw] != null ? original[raw] : raw;
                scene = EditorSceneManager.NewPreviewScene();
                var matrix = JObject.Parse(File.ReadAllText(Path.Combine(output, "../native-rest-contact-matrix/snow-original-y-footik.json")));
                var patchMeta = matrix["rest_surfaces"].Single(s => (string)s["skin"] == "Snow_LOD1");
                actors.Add(CreateActor(entry.prefab, scene, original, baselineClip, "integrated-sprint", 28, 1f, patchMeta));
                actors.Add(CreateActor(entry.prefab, scene, candidate, newClip, "candidate-jog", 29, Speed / Reference, patchMeta));
                var ground = new Material(Shader.Find("Universal Render Pipeline/Lit")); ground.color = new Color(.32f,.35f,.34f); owned.Add(ground);
                var marking = new Material(Shader.Find("Universal Render Pipeline/Unlit")); marking.color = new Color(.76f,.77f,.68f); owned.Add(marking);
                Cube(scene, "ground", new Vector3(0,-.03f,10), new Vector3(20,.06f,70), ground);
                for (int i = -12; i <= 38; i++) Cube(scene, "metre grid", new Vector3(0,.002f,i), new Vector3(16,.003f,.012f), marking);
                for (int i = -8; i <= 8; i++) Cube(scene, "metre grid", new Vector3(i,.002f,12), new Vector3(.012f,.003f,52), marking);
                var sun = new GameObject("Review sun"); SceneManager.MoveGameObjectToScene(sun, scene);
                var light = sun.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.8f; light.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(45,-35,0);
                var fill = new GameObject("Review fill"); SceneManager.MoveGameObjectToScene(fill, scene);
                var fillLight = fill.AddComponent<Light>(); fillLight.type = LightType.Directional; fillLight.intensity = .7f; fill.transform.rotation = Quaternion.Euler(25,145,0);
                var cameraGo = new GameObject("Review camera"); SceneManager.MoveGameObjectToScene(cameraGo, scene);
                var camera = cameraGo.AddComponent<Camera>(); camera.scene = scene; camera.enabled = false; camera.aspect = Width / (float)Height; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.27f,.31f,.34f);
                var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32); target.Create(); owned.Add(target);
                var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false); owned.Add(pixels);
                var request = new RenderPipeline.StandardRequest { destination = target };
                var venue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Candidates/TerminalCafe20261006B2/TerminalCafeB2.prefab").GetComponent<VenueSceneRoot>();
                receipt["camera"] = new JObject { ["pitch"] = venue.cameraPitch, ["yaw"] = venue.cameraYaw, ["distance"] = venue.cameraDistance,
                    ["field_of_view"] = venue.cameraFieldOfView, ["target_viewport_y"] = venue.cameraTargetViewportY, ["focus_height"] = venue.cameraFocusHeight };
                var samples = new JArray(); var cues = new JArray();
                foreach (var actor in actors)
                {
                    actor.animator.Play("loco_run", 0, 0); actor.animator.Update(0); actor.animator.SetFloat("speed", Speed);
                    for (int i = 0; i < 240; i++) actor.animator.Update(actor.clip.length / actor.rate / 120f);
                    actor.previousTime = actor.animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                    Require(Mathf.Abs(actor.previousTime - 2f) < .002f, "Controller warm-up did not advance two loops.");
                    Directory.CreateDirectory(Path.Combine(output, actor.name + "-game")); Directory.CreateDirectory(Path.Combine(output, actor.name + "-side"));
                }
                for (int frame = 0; frame < Frames; frame++) foreach (var actor in actors)
                {
                    float time = frame / (float)Fps;
                    actor.root.transform.position = new Vector3(0, 0, Speed * time);
                    var info = actor.animator.GetCurrentAnimatorStateInfo(0);
                    float rate = info.speed * info.speedMultiplier * actor.animator.speed;
                    Require(Mathf.Abs(rate - actor.rate) < .00001f && Mathf.Abs(info.normalizedTime - 2f - time * actor.rate / actor.clip.length) < .002f, "Actual controller playback differs from requested time/rate.");
                    bool cue = actor.name == "integrated-sprint" ? frame > 0 && actor.currentFoley.Advance(Speed / Fps, 1f / Fps, true, true) :
                        ContactPhases.Any(p => Mathf.FloorToInt(info.normalizedTime - p) > Mathf.FloorToInt(actor.previousTime - p));
                    if (cue) cues.Add(new JObject { ["actor"] = actor.name, ["frame"] = frame, ["seconds"] = time, ["native_time"] = info.normalizedTime });
                    actor.skin.BakeMesh(actor.baked); actor.baked.GetVertices(actor.vertices);
                    var transform = actor.root.transform.worldToLocalMatrix * actor.skin.localToWorldMatrix;
                    Vector3 P(int i) => transform.MultiplyPoint3x4(actor.vertices[i]);
                    samples.Add(new JObject { ["actor"] = actor.name, ["frame"] = frame, ["seconds"] = time, ["native_time"] = info.normalizedTime,
                        ["rate"] = rate, ["world_z"] = actor.root.transform.position.z, ["sole_left"] = actor.left.Min(i => P(i).y), ["sole_right"] = actor.right.Min(i => P(i).y),
                        ["patches_root_xyz"] = new JArray(actor.patches.Select(p => { Vector3 v = p.Aggregate(Vector3.zero,(sum,i)=>sum+P(i))/p.Length; return new JArray(v.x,v.y,v.z); })) });
                    camera.cullingMask = 1 | (1 << actor.root.layer);
                    foreach (string view in new[] { "game", "side" })
                    {
                        Vector3 focus = actor.root.transform.position;
                        if (view == "game")
                        {
                            camera.orthographic = false; camera.fieldOfView = venue.cameraFieldOfView;
                            float ahead = CameraRig.LookAheadForViewport(venue.cameraDistance, venue.cameraPitch, venue.cameraFieldOfView, venue.cameraTargetViewportY, venue.cameraFocusHeight);
                            focus += Quaternion.Euler(0,venue.cameraYaw,0) * Vector3.forward * ahead + Vector3.up * venue.cameraFocusHeight;
                            Quaternion rotation = Quaternion.Euler(venue.cameraPitch,venue.cameraYaw,0);
                            camera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * venue.cameraDistance, rotation);
                        }
                        else { camera.orthographic = true; camera.orthographicSize = 1.25f; focus += Vector3.up * .95f; camera.transform.SetPositionAndRotation(focus + Vector3.right * 6, Quaternion.LookRotation(Vector3.left,Vector3.up)); }
                        Require(RenderPipeline.SupportsRenderRequest(camera, request), "Active render pipeline does not support StandardRequest; no image claim.");
                        RenderPipeline.SubmitRenderRequest(camera, request);
                        var priorTarget = RenderTexture.active;
                        try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,Width,Height),0,0,false); pixels.Apply(false,false); File.WriteAllBytes(Path.Combine(output,actor.name+"-"+view,frame.ToString("D4")+".png"),pixels.EncodeToPNG()); }
                        finally { RenderTexture.active = priorTarget; }
                    }
                    actor.previousTime = info.normalizedTime; actor.animator.Update(1f / Fps);
                }
                File.WriteAllText(Path.Combine(output,"samples.json"), samples.ToString()); File.WriteAllText(Path.Combine(output,"footstep-cues.json"), cues.ToString());
                receipt["state"] = "rendered-not-accepted";
            }
            catch(Exception e) { receipt["state"] = "failed"; receipt["failure"] = e.ToString(); throw; }
            finally
            {
                foreach(var actor in actors) if(actor.baked != null) Object.DestroyImmediate(actor.baked);
                if(scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                foreach(var obj in owned) if(obj != null) Object.DestroyImmediate(obj);
                var after = Snapshot(roots); var drift = before.Where(p=>!after.TryGetValue(p.Key,out var hash)||hash!=p.Value).Select(p=>p.Key).Concat(after.Keys.Where(p=>!before.ContainsKey(p))).ToArray();
                receipt["protected_drift"] = new JArray(drift); File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString());
                Require(drift.Length==0,"Protected character/controller/source files changed.");
            }
        }
        private static Actor CreateActor(GameObject prefab,Scene scene,RuntimeAnimatorController controller,AnimationClip clip,string name,int layer,float rate,JToken patches)
        {
            var root=Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(root,scene); root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            foreach(var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=layer;
            foreach(var group in root.GetComponentsInChildren<LODGroup>(true)) group.enabled=false;
            var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>(true); var skin=skins.Single(s=>s.name=="Snow_LOD1");
            foreach(var s in skins) { s.enabled=s==skin; s.updateWhenOffscreen=true; }
            var animator=root.GetComponentsInChildren<Animator>(true).Single(); animator.enabled=false; animator.runtimeAnimatorController=controller;
            animator.applyRootMotion=false; animator.speed=1; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; animator.enabled=true; animator.Rebind();
            var weights=skin.sharedMesh.boneWeights; var bones=skin.bones.Select(b=>b.name).ToArray();
            int[] Select(string suffix)=>Enumerable.Range(0,weights.Length).Where(i=> { var w=weights[i]; float W(int b,float v)=>v>0&&(bones[b]=="Foot."+suffix||bones[b]=="Toes."+suffix)?v:0; return W(w.boneIndex0,w.weight0)+W(w.boneIndex1,w.weight1)+W(w.boneIndex2,w.weight2)+W(w.boneIndex3,w.weight3)>.5f; }).ToArray();
            return new Actor { root=root,animator=animator,clip=clip,name=name,rate=rate,skin=skin,baked=new Mesh(),left=Select("L"),right=Select("R"),
                patches=patches["patches"].Select(p=>p["fixed_vertex_indices"].Values<int>().ToArray()).ToArray() };
        }
        private static void Cube(Scene scene,string name,Vector3 at,Vector3 size,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; SceneManager.MoveGameObjectToScene(go,scene); go.transform.SetPositionAndRotation(at,Quaternion.identity); go.transform.localScale=size;
            Object.DestroyImmediate(go.GetComponent<Collider>()); go.GetComponent<Renderer>().sharedMaterial=material;
        }
        private static IEnumerable<AnimatorState> States(AnimatorController controller)=>controller.layers.SelectMany(l=>States(l.stateMachine));
        private static IEnumerable<AnimatorState> States(AnimatorStateMachine machine) { foreach(var s in machine.states) yield return s.state; foreach(var c in machine.stateMachines) foreach(var s in States(c.stateMachine)) yield return s; }
        private static string Fingerprint(AnimatorState s)=>EditorJsonUtility.ToJson(s);
        private static Dictionary<string,string> Snapshot(string[] roots)=>roots.SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).Where(p=>!p.Replace('\\','/').StartsWith(Stage,StringComparison.Ordinal)&&p!=Stage.TrimEnd('/')+".meta").ToDictionary(p=>p,Hash);
        private static string Hash(string path) { using var h=SHA256.Create(); using var f=File.OpenRead(path); return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant(); }
        private static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException("Snow playback review: "+message); }
    }
}
