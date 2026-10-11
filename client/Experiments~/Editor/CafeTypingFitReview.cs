// Native read-only pose evidence. Coordinator imports/runs only when Unity is idle.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    public static class CafeTypingFitReview
    {
        private const string VenuePath = "Assets/Art/Candidates/TerminalCafe20261006B3Social/TerminalCafeB3Social.prefab";
        private const string RegistryPath = "Assets/Resources/Characters/CharacterRegistry.asset";
        private const int Samples = 240;
        private static string _surfaceOutput;
        private static string Evidence => Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/direction-20261006/terminal-cafe-b3-fit"));
        private static readonly Dictionary<string,string> TargetHashes = new Dictionary<string,string> {
            { "adult_depth_48cm-office-target.obj.gz", "c9090cabcd8e3f77a27cfa512f638b764b352cc041b063d9f0b6700cb0310157" },
            { "shared-typing-desk-target.obj.gz", "2164c82b921f1870ead8b117b3d5baa61981d8fa5e2c1acf32c7389afbc8ee9f" },
            { "keyboard/shared-typing-keyboard-target.obj.gz", "9b7c02535527e6e7e5e1e327715fea950d383c9954ba27797002f81857920eaf" } };
        public static void Capture() => Run(true);
        public static void CaptureGeometry() => Run(false);

        private static void Run(bool render)
        {
            Need(!EditorApplication.isPlayingOrWillChangePlaymode && !Lightmapping.isRunning, "Use idle Edit mode.");
            var venue = AssetDatabase.LoadAssetAtPath<GameObject>(VenuePath);
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>(RegistryPath);
            Need(venue != null && registry != null, "Stage B3Social and keep the real current character registry available.");
            string output = Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/direction-20261006/terminal-cafe-b3-fit/native-typing-fit-review-v3"));
            Directory.CreateDirectory(output);
            _surfaceOutput=Path.Combine(output,"furniture-surfaces");
            string bakeReceiptPath = Path.GetFullPath(Path.Combine(Application.dataPath,"../../../ct-life-evidence/art/character-candidates/motion-review-20261006/native-typing-persistent-replay-v1/receipt.json"));
            Need(File.Exists(bakeReceiptPath), "Persistent combined typing bake receipt is required; transient hand capture is insufficient.");
            var bakeReceipt = JObject.Parse(File.ReadAllText(bakeReceiptPath));
            Need((string)bakeReceipt["review_revision"] == "fresh-process-persistent-replay-v3" &&
                (bool?)bakeReceipt["all_pass"] == true && bakeReceipt["protected_drift"] is JArray drift && drift.Count == 0,
                "Combined typing bake must pass fresh-process-persistent-replay-v3 with empty protected drift.");
            string authorReceiptPath = Path.GetFullPath(Path.Combine(Application.dataPath,"../../../ct-life-evidence/art/character-candidates/motion-review-20261006/native-typing-offline-bake-v1/receipt.json"));
            const string expectedAuthorHash = "de022c185d2aa84f1f0b133fffda62b5576dd9b791df706be53b8ba2d2b785aa";
            Need((int?)bakeReceipt["process_id"] > 0 &&
                DateTimeOffset.TryParse((string)bakeReceipt["process_started_utc"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var replayStarted) &&
                DateTimeOffset.TryParse((string)bakeReceipt["author_receipt_written_utc"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var authorWritten) &&
                replayStarted > authorWritten && (string)bakeReceipt["author_receipt_sha256"] == expectedAuthorHash &&
                File.Exists(authorReceiptPath) && Hash(authorReceiptPath) == expectedAuthorHash,
                "Fresh-process replay must identify a process started after the exact frozen author receipt.");
            var expectedClipHashes = new Dictionary<string,string> {
                { "snow", "6cc4de1ff564ac7b4acd83a6849c406e11db8cb44858de5a68e214080c6576cc" },
                { "rain", "8b86b3b985f0e138d1f07bc027e4a6a3433b21ccf99fbcfdaed992fb0de98ad1" } };
            var clipPaths = new[] { "snow", "rain" }.Select(character => {
                var entry = ((JArray)bakeReceipt["cases"]).OfType<JObject>().Single(c => (string)c["character"] == character);
                Need((bool?)entry["all_pass"] == true && (bool?)entry["persisted_replay_verified"] == true &&
                    (string)entry["replay_source"] == "saved-asset-loaded-in-distinct-fresh-process" &&
                    (bool?)entry["playback_foot_ik"] == false && (bool?)entry["playback_hand_ik_pass"] == false &&
                    (int?)entry["playback_ik_callback_count"] == 0,
                    "Combined typing replay must verify the saved clip in its distinct fresh process without any runtime foot/hand IK callback: " + character);
                string path = (string)entry["candidate_asset"];
                Need(!string.IsNullOrEmpty(path) && File.Exists(path) && (string)entry["candidate_sha256"] == expectedClipHashes[character] && Hash(path) == expectedClipHashes[character],
                    "Combined typing clip no longer matches its accepted replay receipt: " + character);
                return path;
            }).ToArray();
            foreach(var target in TargetHashes) Need(File.Exists(Path.Combine(Evidence,target.Key)) && Hash(Path.Combine(Evidence,target.Key)) == target.Value,
                "Frozen physical target changed: " + target.Key);
            foreach (var path in clipPaths) Need(AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null, "Missing accepted persistent combined typing clip " + path);
            var dependencies = AssetDatabase.GetDependencies(new[] { VenuePath, RegistryPath }.Concat(clipPaths).ToArray(),true).Where(File.Exists).ToArray();
            var frozen = dependencies.Concat(dependencies.Where(p=>File.Exists(p+".meta")).Select(p=>p+".meta")).Concat(TargetHashes.Keys.Select(p=>Path.Combine(Evidence,p))).Concat(new[]{bakeReceiptPath,authorReceiptPath}).Distinct().ToDictionary(p=>p,Hash);
            CafeKeyboardAppearance.RequireSources();
            foreach(string path in CafeKeyboardAppearance.SourcePaths)frozen[path]=Hash(path);
            var receipt = new JObject { ["status"]="running",["review_revision"]="original-keyboard-materials-cpu-pose-snapshots-v3",["keyboard_texture_sha256"]=CafeKeyboardAppearance.TextureSha256,["venue"]=VenuePath,["render_requested"]=render,
                ["scope"]="Exact immutable terminal_1 roots; actual current world prefabs with accepted persistent typing clip in transient controller, FootIK and hand IK pass OFF; actual sourced chair/desk transformed to frozen shared targets plus exact frozen keyboard mesh; two warm loops and 241 native samples; no source asset edits.",
                ["limitations"]="Candidate-controller contact evidence only. The persistent clip must already pass its independent FootIK-off/hand-pass-off replay gate. Furniture transforms and the keyboard preview are temporary; no production controller/prefab is changed. Transitions, complete venue navigation, multiplayer and phone remain unverified. Surface witnesses do not replace visual and complete contact review. Prior v2 PNGs repeated despite advancing native geometry and establish static contact views only. This v3 renders CPU BakeMesh snapshots verified against each measured native pose, with original materials; isolated landscape distance views do not establish final full-room composition." };
            try
            {
                var cases = new JArray(); receipt["cases"]=cases;
                foreach(string character in new[]{"snow","rain"})
                {
                    var entry=registry.looks.Single(e=>e.key.Split('|').Contains("head_"+character));
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPaths[character == "snow" ? 0 : 1]);
                    Need(entry.prefab != null && clip != null, "World look or body-fit clip missing: " + character);
                    var controller = new AnimatorController { name = "UNSAVED_TYPING_FURNITURE_CONTACT_" + character };
                    var machine = new AnimatorStateMachine();
                    var state = machine.AddState("act_type"); state.motion = clip; state.speed = 1f;
                    state.speedParameterActive = false; state.iKOnFeet = false; machine.defaultState = state;
                    controller.layers = new[] { new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1f, stateMachine = machine, iKPass = false } };
                    try { cases.Add(CaptureCase(venue,entry.prefab,controller,character,true,output,render)); }
                    finally { Object.DestroyImmediate(state); Object.DestroyImmediate(machine); Object.DestroyImmediate(controller); }
                }
                receipt["status"]="captured_not_combined_furniture_accepted";
            }
            catch(Exception error) { receipt["status"]="failed";receipt["error"]=error.ToString();throw; }
            finally
            {
                var changed=frozen.Where(p=>!File.Exists(p.Key)||Hash(p.Key)!=p.Value).Select(p=>p.Key).ToArray();
                receipt["protected_drift"]=new JArray(changed);receipt["protected_sha256"]=JObject.FromObject(frozen);
                File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString());
                Need(changed.Length==0,"Read-only pose review found protected asset drift: "+string.Join(",",changed));
            }
        }

        private static JObject CaptureCase(GameObject venue,GameObject prefab,RuntimeAnimatorController controller,string character,bool work,string output,bool render)
        {
            string spotId=work?"terminal_1":"table_1";
            string action=work?"act_type":"act_sit";
            string caseId=character+(work?"-work":"-social");
            var anchor=venue.GetComponentsInChildren<SpotAnchor>(true).Single(a=>a.spotId==spotId);
            Vector3 foot=anchor.transform.position;
            Transform Furniture(string id) => venue.GetComponentsInChildren<Transform>(true)
                .Where(t=>t.name=="Direction_"+id).OrderBy(t=>(t.position-foot).sqrMagnitude).First();
            var sourceChair=Furniture(work?"office_chair":"home_chair_1");
            var sourceTable=Furniture(work?"home_desk":"home_table_roundsmall");
            var scene=EditorSceneManager.NewPreviewScene();
            var temporary=new List<Object>();
            try
            {
                GameObject Copy(GameObject source)
                {
                    var copy=Object.Instantiate(source);SceneManager.MoveGameObjectToScene(copy,scene);temporary.Add(copy);return copy;
                }
                var chair=Copy(sourceChair.gameObject);var table=Copy(sourceTable.gameObject);
                chair.transform.SetPositionAndRotation(foot+new Vector3(0,0,-.31f),Quaternion.Euler(0,180,0));
                chair.transform.localScale=new Vector3(1,1,.8f);
                table.transform.SetPositionAndRotation(foot+new Vector3(0,0,.5487464f),Quaternion.identity);
                table.transform.localScale=new Vector3(1,.74f/.76f,1);
                foreach(var obj in new[]{chair,table})foreach(var group in obj.GetComponentsInChildren<LODGroup>(true))group.ForceLOD(0);
                var frozenChair=ReadTarget("adult_depth_48cm-office-target.obj.gz");
                var frozenDesk=ReadTarget("shared-typing-desk-target.obj.gz");
                var curvedSupport=SupportTriangles(frozenChair);
                CompareFurniture(chair,foot,frozenChair,"shared office chair");
                CompareFurniture(table,foot,frozenDesk,"shared desk");
                var keyboard=CreateKeyboard(scene,foot,temporary);
                CompareFurniture(keyboard,foot,ReadTarget("keyboard/shared-typing-keyboard-target.obj.gz"),"shared keyboard");
                var root=Copy(prefab);root.transform.SetPositionAndRotation(foot,anchor.transform.rotation);
                var animator=root.GetComponentsInChildren<Animator>(true).Single();
                Need(!root.GetComponentsInChildren<MonoBehaviour>(true).Any(c=>c!=null && c.GetType().Name=="SeatedTypingHandTargets"),
                    "Combined persistent clip preview must not contain the transient hand IK driver.");
                animator.enabled=false;animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.speed=1;animator.enabled=true;animator.Rebind();
                foreach(var group in root.GetComponentsInChildren<LODGroup>(true))group.ForceLOD(0);
                Need(animator.HasState(0,Animator.StringToHash(action)),"Effective controller lacks "+action);
                animator.Play(action,0,0);animator.Update(0);
                var clips=animator.GetCurrentAnimatorClipInfo(0);
                Need(clips.Length==1 && clips[0].clip!=null,"Expected one effective seated clip.");
                var clip=clips[0].clip;var initial=animator.GetCurrentAnimatorStateInfo(0);
                Need(initial.loop && clip.isLooping,"Current seated state/clip does not loop.");
                Need(Mathf.Abs(initial.speed-1)<.0001f && Mathf.Abs(initial.speedMultiplier-1)<.0001f,"Review requires the current 1x seated state.");
                float period=initial.length,dt=period/Samples;
                Need(period>.01f && float.IsFinite(period),"Invalid native seated period.");
                for(int i=0;i<2*Samples;i++)animator.Update(dt);
                float start=animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                Need(Mathf.Abs(start-2)<.005f,"Native seated warm-up did not advance two cycles.");
                var furniture=Geometry(chair).Concat(Geometry(table)).Concat(Geometry(keyboard)).ToArray();
                // The office seat is curved: record ray-to-triangle support, never substitute a flat .451m plane.
                
                var frames=new JArray();var visuals=new JArray();Vector3[] previousVisible=null;JObject previousVisual=null;
                Camera camera=null;
                if(render)camera=PrepareCamera(scene,foot,temporary);
                for(int sample=0;sample<=Samples;sample++)
                {
                    var info=animator.GetCurrentAnimatorStateInfo(0);
                    Need(info.IsName(action) && Mathf.Abs(info.normalizedTime-start-sample/(float)Samples)<.005f,"Native seated phase froze or changed state.");
                    Need((root.transform.position-foot).sqrMagnitude<1e-8f,"Unexpected avatar root translation.");
                    var skins=new JArray();var measured=new Dictionary<SkinnedMeshRenderer,Vector3[]>();
                    foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var baked=new Mesh();
                        try
                        {
                            skin.BakeMesh(baked);
                            var vertices=baked.vertices.Select(skin.transform.TransformPoint).ToArray();
                            measured[skin]=vertices;
                            Need(vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),"Nonfinite posed skin.");
                            int[] Region(params HumanBodyBones[] wanted)
                            {
                                var bones=wanted.Select(animator.GetBoneTransform).Where(b=>b!=null).ToHashSet();
                                var weights=skin.sharedMesh.boneWeights;
                                return Enumerable.Range(0,weights.Length).Where(i=> {
                                    var w=weights[i];float Weight(int b,float value)=>value>0&&bones.Contains(skin.bones[b])?value:0;
                                    return Weight(w.boneIndex0,w.weight0)+Weight(w.boneIndex1,w.weight1)+Weight(w.boneIndex2,w.weight2)+Weight(w.boneIndex3,w.weight3)>=.5f;
                                }).ToArray();
                            }
                            var feet=Region(HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightFoot,HumanBodyBones.RightToes);
                            var hips=Region(HumanBodyBones.Hips,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg);
                            var nearSeat=hips.Where(i=>Mathf.Abs(vertices[i].x-foot.x)<=.145f && vertices[i].z>=foot.z-.42f && vertices[i].z<=foot.z-.15f).ToArray();
                            Need(feet.Length > 0 && feet.Min(i=>vertices[i].y-foot.y) >= -.002f,
                                "Candidate typing feet penetrate the floor beyond the declared tolerance.");
                            var torso=Region(HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest);
                            skins.Add(new JObject { ["skin"]=skin.name,["vertices"]=vertices.Length,
                                ["foot_surface_min_y_m"]=feet.Length>0?(JToken)feet.Min(i=>vertices[i].y):JValue.CreateNull(),
                                ["butt_thigh_near_seat_min_y_m"]=nearSeat.Length>0?(JToken)nearSeat.Min(i=>vertices[i].y):JValue.CreateNull(),
                                ["butt_thigh_curved_support_min_gap_m"]=CurvedSupportGap(vertices,nearSeat,foot,curvedSupport),
                                ["torso_back_min_world_z_m"]=torso.Length>0?(JToken)torso.Min(i=>vertices[i].z):JValue.CreateNull() });
                            if(sample%30==0 && sample<Samples)
                                WriteObj(Path.Combine(output,caseId+"-"+skin.name+"-"+sample.ToString("00")+".obj.gz"),new[]{(vertices,baked.triangles)}.Concat(furniture));
                        }
                        finally { Object.DestroyImmediate(baked); }
                    }
                    frames.Add(new JObject { ["phase"]=sample/(float)Samples,["native_time"]=info.normalizedTime,["skins"]=skins,
                        ["hips_world"]=V(animator.GetBoneTransform(HumanBodyBones.Hips).position),["root_world"]=V(root.transform.position) });
                    if(render && sample%80==0 && sample<Samples)
                    {
                        using var snapshot=new PoseSnapshot(root,measured);
                        var visual=new JObject { ["phase"]=sample/(float)Samples,["native_time"]=info.normalizedTime,
                            ["render_source"]="CPU BakeMesh snapshot of the same measured native pose, original materials/property blocks",
                            ["gameplay_view_scope"]="Isolated furniture only; final venue camera values distance10.8/pitch30/yaw-12/FOV45/feetY.30/focusHeight1 via actual CameraRig look-ahead; full room still pending",
                            ["skin"]=snapshot.SkinName,["snapshot_vertex_error_m"]=snapshot.Error,["measured_vertices_sha256"]=VertexHash(snapshot.Vertices) };
                        foreach(string view in new[]{"front","side","keyboard","gameplay"})
                        {
                            string path=Path.Combine(output,caseId+"-"+sample.ToString("00")+"-"+view+".png");Render(camera,foot,view,path);
                            visual[view+"_file"]=Path.GetFileName(path);visual[view+"_sha256"]=Hash(path);
                        }
                        if(previousVisible!=null)
                        {
                            float delta=snapshot.Vertices.Select((p,i)=>Vector3.Distance(p,previousVisible[i])).Max();
                            bool changed=new[]{"front","side","keyboard"}.Any(view=>(string)visual[view+"_sha256"]!=(string)previousVisual[view+"_sha256"]);
                            visual["max_native_vertex_delta_from_previous_m"]=delta;visual["contact_image_changed"]=changed;
                            Need(delta<.005f||changed,"Visible native mesh moved at least5mm but all contact PNGs were identical.");
                        }
                        previousVisible=snapshot.Vertices;previousVisual=visual;visuals.Add(visual);
                    }
                    if(sample<Samples)animator.Update(dt);
                }
                var result=new JObject { ["case"]=caseId,["spot"]=spotId,["animation"]=action,["prefab"]=AssetDatabase.GetAssetPath(prefab),
                    ["effective_controller"]="transient candidate act_type, FootIK OFF and layer hand IK pass OFF; no hand driver; not production controller",["effective_clip"]=AssetDatabase.GetAssetPath(clip),["effective_clip_name"]=clip.name,
                    ["root_world"]=V(foot),["root_yaw_degrees"]=anchor.transform.eulerAngles.y,["chair_root_world"]=V(chair.transform.position),
                    ["chair_yaw_degrees"]=chair.transform.eulerAngles.y,["chair_support"]="frozen actual curved seat triangles",["keyboard_sha256"]=TargetHashes["keyboard/shared-typing-keyboard-target.obj.gz"],["period_seconds"]=period,["frames"]=frames,["visuals"]=visuals,
                    ["acceptance"]="candidate_contact_captured_requires_visual_triangle_review_and_real_controller_integration" };
                File.WriteAllText(Path.Combine(output,caseId+".json"),result.ToString());
                return new JObject { ["case"]=caseId,["file"]=caseId+".json",["samples"]=frames.Count,["images"]=render?12:0,["snapshot_images_verified"]=render,["visuals"]=visuals };
            }
            finally
            {
                foreach(var obj in temporary.AsEnumerable().Reverse())if(obj!=null)Object.DestroyImmediate(obj);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static IEnumerable<(Vector3[] vertices,int[] triangles)> Geometry(GameObject go)
        {
            foreach(var filter in go.GetComponentsInChildren<MeshFilter>(true).Where(f=>!f.name.Contains("_LOD")||f.name.Contains("_LOD0")))
                yield return (filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray(),filter.sharedMesh.triangles);
        }
        private static (Vector3[] vertices,int[] triangles) ReadTarget(string relative)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            using var file=File.OpenRead(Path.Combine(Evidence,relative));using var zip=new GZipStream(file,CompressionMode.Decompress);using var reader=new StreamReader(zip);
            while(!reader.EndOfStream)
            {
                var tokens=(reader.ReadLine()??"").Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
                if(tokens.Length==4 && tokens[0]=="v")vertices.Add(new Vector3(float.Parse(tokens[1],CultureInfo.InvariantCulture),float.Parse(tokens[2],CultureInfo.InvariantCulture),float.Parse(tokens[3],CultureInfo.InvariantCulture)));
                else if(tokens.Length==4 && tokens[0]=="f")for(int i=1;i<=3;i++)triangles.Add(int.Parse(tokens[i],CultureInfo.InvariantCulture)-1);
            }
            Need(vertices.Count>0 && triangles.Count>0,"Empty frozen mesh "+relative);
            return(vertices.ToArray(),triangles.ToArray());
        }
        private static void CompareFurniture(GameObject item,Vector3 foot,(Vector3[] vertices,int[] triangles) target,string label)
        {
            CafeFurnitureGeometry.RequireExactSurface(item,foot,target,label,_surfaceOutput);
        }
        private static GameObject CreateKeyboard(Scene scene,Vector3 foot,List<Object> temporary)
        {
            var mesh=CafeKeyboardAppearance.CreateMesh();temporary.Add(mesh);
            var texture=CafeKeyboardAppearance.CreatePreviewTexture();temporary.Add(texture);
            var materials=CafeKeyboardAppearance.CreateMaterials(texture);temporary.AddRange(materials);
            var go=new GameObject("UNSAVED exact shared keyboard target");SceneManager.MoveGameObjectToScene(go,scene);temporary.Add(go);go.transform.position=foot;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=materials;return go;
        }
        private static Vector3[][] SupportTriangles((Vector3[] vertices,int[] triangles) chair)
        {
            var result=new List<Vector3[]>();var v=chair.vertices;var t=chair.triangles;
            for(int i=0;i<t.Length;i+=3)
            {
                var a=v[t[i]];var b=v[t[i+1]];var c=v[t[i+2]];var normal=Vector3.Cross(b-a,c-a);float mean=(a.y+b.y+c.y)/3;
                if(normal.magnitude>=1e-8f && Mathf.Abs(normal.normalized.y)>.90f && mean>.40f && mean<.49f)result.Add(new[]{a,b,c});
            }
            Need(result.Count>0,"Frozen office chair has no selected support triangles.");return result.ToArray();
        }
        private static JToken CurvedSupportGap(Vector3[] vertices,int[] region,Vector3 foot,Vector3[][] support)
        {
            float gap=float.PositiveInfinity;
            foreach(int index in region)
            {
                var p=vertices[index]-foot;float highest=float.NegativeInfinity;
                foreach(var triangle in support)
                {
                    var a=triangle[0];var b=triangle[1];var c=triangle[2];
                    float den=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(den)<1e-10f)continue;
                    float u=((b.z-c.z)*(p.x-c.x)+(c.x-b.x)*(p.z-c.z))/den;
                    float w=((c.z-a.z)*(p.x-c.x)+(a.x-c.x)*(p.z-c.z))/den;float q=1-u-w;
                    if(u<0||w<0||q<0)continue;float y=u*a.y+w*b.y+q*c.y;if(y>.40f&&y<.49f)highest=Mathf.Max(highest,y);
                }
                if(!float.IsNegativeInfinity(highest))gap=Mathf.Min(gap,p.y-highest);
            }
            return float.IsPositiveInfinity(gap)?JValue.CreateNull():(JToken)gap;
        }
        private static Camera PrepareCamera(Scene scene,Vector3 foot,List<Object> temporary)
        {
            GameObject InScene(string name) { var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);temporary.Add(go);return go; }
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);temporary.Add(floor);
            floor.transform.position=foot+new Vector3(0,-.035f,.3f);floor.transform.localScale=new Vector3(5,.07f,5);
            var material=new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));temporary.Add(material);material.SetColor("_BaseColor",new Color(.48f,.46f,.41f));floor.GetComponent<Renderer>().sharedMaterial=material;
            var key=InScene("Read-only pose key").AddComponent<Light>();key.type=LightType.Directional;key.intensity=1.5f;key.color=new Color(1,.93f,.84f);key.transform.rotation=Quaternion.Euler(35,-35,0);
            var fill=InScene("Read-only pose fill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.6f;fill.transform.rotation=Quaternion.Euler(30,140,0);
            var camera=InScene("Read-only pose camera").AddComponent<Camera>();camera.scene=scene;camera.enabled=false;
            camera.orthographic=true;camera.orthographicSize=1.35f;camera.aspect=1;camera.nearClipPlane=.01f;camera.farClipPlane=20;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.20f,.23f,.24f);
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            return camera;
        }
        private static void Render(Camera camera,Vector3 foot,string view,string path)
        {
            Vector3 target=foot+new Vector3(0,.75f,.35f);
            bool landscape=view=="gameplay";int width=landscape?1600:1024,height=landscape?900:1024;
            camera.aspect=width/(float)height;camera.orthographic=!landscape;camera.fieldOfView=45;
            camera.orthographicSize=view=="keyboard"?.42f:1.35f;
            if(view=="keyboard")target=foot+new Vector3(0,.75f,.2f);
            if(landscape)
            {
                float ahead=CameraRig.LookAheadForViewport(10.8f,30,45,.30f,1);
                target=foot+Quaternion.Euler(0,-12,0)*Vector3.forward*ahead+Vector3.up;
                camera.transform.rotation=Quaternion.Euler(30,-12,0);camera.transform.position=target-camera.transform.forward*10.8f;
            }
            else { camera.transform.position=target+(view=="front"?new Vector3(0,.1f,4):view=="side"?new Vector3(4,.1f,0):new Vector3(1.2f,1.8f,1.6f));camera.transform.LookAt(target); }
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);rt.Create();
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try
            {
                var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};
                Need(RenderPipeline.SupportsRenderRequest(camera,request),"URP render requests unavailable; run with graphics, or use CaptureGeometry explicitly.");
                RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=rt;
                image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally { RenderTexture.active=previous;Object.DestroyImmediate(image);rt.Release();Object.DestroyImmediate(rt); }
        }
        private sealed class PoseSnapshot : IDisposable
        {
            private readonly SkinnedMeshRenderer[] sources;private readonly bool[] sourceEnabled;
            private readonly LODGroup[] groups;private readonly bool[] groupEnabled;
            private GameObject go;private Mesh mesh;
            public readonly Vector3[] Vertices;public readonly string SkinName;public readonly float Error;
            public PoseSnapshot(GameObject root,Dictionary<SkinnedMeshRenderer,Vector3[]> measured)
            {
                sources=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);sourceEnabled=sources.Select(r=>r.enabled).ToArray();
                groups=root.GetComponentsInChildren<LODGroup>(true);groupEnabled=groups.Select(g=>g.enabled).ToArray();
                try
                {
                    var visible=groups.SelectMany(g=>g.GetLODs().Take(1)).SelectMany(l=>l.renderers).OfType<SkinnedMeshRenderer>().Distinct().ToArray();
                    Need(visible.Length==1&&measured.ContainsKey(visible[0]),"Expected one measured authored LOD0 skin for the beauty snapshot.");
                    var skin=visible[0];Vertices=measured[skin];SkinName=skin.name;
                    mesh=new Mesh { name="Native measured typing pose snapshot" };skin.BakeMesh(mesh);
                    go=new GameObject("Current CPU pose only");go.transform.SetParent(skin.transform,false);go.layer=skin.gameObject.layer;
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=skin.sharedMaterials;
                    renderer.shadowCastingMode=skin.shadowCastingMode;renderer.receiveShadows=skin.receiveShadows;renderer.renderingLayerMask=skin.renderingLayerMask;
                    var block=new MaterialPropertyBlock();skin.GetPropertyBlock(block);renderer.SetPropertyBlock(block);
                    for(int i=0;i<renderer.sharedMaterials.Length;i++){block.Clear();skin.GetPropertyBlock(block,i);if(!block.isEmpty)renderer.SetPropertyBlock(block,i);}
                    var local=mesh.vertices;Need(local.Length==Vertices.Length,"Snapshot topology differs from measured native skin.");
                    float error=0;for(int i=0;i<local.Length;i++)error=Mathf.Max(error,Vector3.Distance(go.transform.TransformPoint(local[i]),Vertices[i]));
                    Error=error;Need(error<=.00001f,"Rendered CPU snapshot differs from measured pose by more than10 micrometres.");
                    foreach(var group in groups)group.enabled=false;foreach(var source in sources)source.enabled=false;
                    Need(go.activeInHierarchy&&renderer.enabled&&sources.All(r=>!r.enabled),"Original GPU skin remained visible in snapshot render.");
                }
                catch { Dispose();throw; }
            }
            public void Dispose()
            {
                if(go!=null)Object.DestroyImmediate(go);if(mesh!=null)Object.DestroyImmediate(mesh);
                for(int i=0;i<groups.Length;i++)groups[i].enabled=groupEnabled[i];for(int i=0;i<sources.Length;i++)sources[i].enabled=sourceEnabled[i];
            }
        }
        private static string VertexHash(Vector3[] vertices)
        {
            using var memory=new MemoryStream();using(var writer=new BinaryWriter(memory,System.Text.Encoding.UTF8,true))foreach(var v in vertices){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);}
            using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(memory.ToArray())).Replace("-","").ToLowerInvariant();
        }
        private static void WriteObj(string path,IEnumerable<(Vector3[] vertices,int[] triangles)> pieces)
        {
            using var file=File.Create(path);using var gzip=new GZipStream(file,System.IO.Compression.CompressionLevel.Optimal);using var writer=new StreamWriter(gzip);
            int offset=1;foreach(var (v,t) in pieces)
            {
                foreach(var p in v)writer.WriteLine(string.Format(CultureInfo.InvariantCulture,"v {0:R} {1:R} {2:R}",p.x,p.y,p.z));
                for(int i=0;i<t.Length;i+=3)writer.WriteLine($"f {t[i]+offset} {t[i+1]+offset} {t[i+2]+offset}");offset+=v.Length;
            }
        }
        private static JArray V(Vector3 value)=>new JArray(value.x,value.y,value.z);
        private static void Need(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
        private static string Hash(string path)
        {
            using var sha=SHA256.Create();using var stream=File.OpenRead(path);return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
    }
}
