// Fresh-process saved-clip preflight before the actual room enters Play mode. No asset writes.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CTLife.EditorTools
{
    public static class RainWalkPersistentReplay
    {
        private const string Helper="Assets/Scripts/Editor/RainWalkContactCandidateV2.cs";
        private const string Saved="Assets/Art/characters/authored/MotionReview20261006/RainWalkContactV2/Rain_WalkContact_CafeCandidate.anim";
        private const string SavedHash="57bc3c9a2357b5bb036a7955a96e26acc8f9d695c6ebd6b532dbf0f36b938d4c";
        private static object Call(string name,params object[] args)
        {
            var method=typeof(RainWalkContactCandidateV2).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static);
            Need(method!=null,"Pinned native sampling method missing: "+name);
            try{return method.Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException??e;}
        }
        public static void Capture()
        {
            Need(!EditorApplication.isPlayingOrWillChangePlaymode,"Persistent preflight must run before entering Play.");
            Need(Hash(Helper)=="57210ff89579152b4e58295b5b057f1539d640c7235c811b6975dc44616c837c","Pinned source-relative authoring sampler changed.");
            string evidence=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../ct-life-evidence/art/character-candidates/motion-review-20261006"));
            string authorPath=Path.Combine(evidence,"native-rain-walk-contact-v2/receipt.json");var author=JObject.Parse(File.ReadAllText(authorPath));
            Need((bool?)author["all_pass"]==true&&!author["protected_drift"].Any()&&(string)author["revision"]=="rain-contact-source-relative-loop-v2","Authoring contact comparison must pass.");
            Need((string)author["candidate_asset"]==Saved&&(string)author["candidate_sha256"]==SavedHash&&Hash(Saved)==SavedHash,"Exact saved candidate bytes required.");
            var process=System.Diagnostics.Process.GetCurrentProcess();DateTime started=process.StartTime.ToUniversalTime(),written=File.GetLastWriteTimeUtc(authorPath);
            Need(started>written,"Process must start after completed authoring receipt; in-memory replay is insufficient.");
            string parent=Path.Combine(evidence,"native-rain-walk-persistent-replay-v1");string output=Path.Combine(parent,"process-"+process.Id);Directory.CreateDirectory(output);
            var before=Directory.GetFiles("Assets/Art/characters/authored","*",SearchOption.AllDirectories).Concat(Directory.GetFiles("Assets/Resources/Characters","*",SearchOption.AllDirectories)).Concat(Directory.GetFiles("Assets/Art/animations","*",SearchOption.AllDirectories)).ToDictionary(p=>p,Hash);
            var report=new JObject{["revision"]="rain-saved-contact-fresh-process-v1",["all_pass"]=false,["persistent_replay_verified"]=false,["process_id"]=process.Id,["process_started_utc"]=started.ToString("O"),["author_receipt_written_utc"]=written.ToString("O"),["author_receipt_sha256"]=Hash(authorPath),["saved_asset"]=Saved,["saved_sha256"]=SavedHash,["replay_source"]="fresh-process AssetDatabase disk import; original pinned241-phase native sampler; all3LOD soles",["scope"]="Saved contact-only clip correspondence. Original loop seam is retained. Actual Local/Remote exit composition, visual feel and mobile performance remain separate."};
            var originalScene=SceneManager.GetActiveScene();
            Need(originalScene.IsValid()&&originalScene.isLoaded&&!originalScene.isDirty,"Original active scene must already be clean; do not clear dirty flags.");
            var originalSetup=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i)).Select(x=>new{x.handle,x.path,x.isDirty,x.isLoaded}).ToArray();
            Scene scratch=default,scene=default;
            try
            {
                // Instantiate first belongs to the active regular scene before the pinned sampler moves it
                // to PreviewScene. Own that temporary destination so it cannot dirty the user's Bootstrap.
                scratch=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                Need(SceneManager.SetActiveScene(scratch),"Could not activate owned empty replay scene.");
                scene=EditorSceneManager.NewPreviewScene();
                var registry=AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");var entry=registry.looks.Single(e=>e.key==(string)author["look_key"]);
                Need(AssetDatabase.GetAssetPath(entry.prefab)==(string)author["world_prefab_asset"],"World body changed.");
                var own=entry.prefab.GetComponentInChildren<Animator>(true).avatar;Need(AssetDatabase.GetAssetPath(own)==(string)author["own_avatar_asset"],"Own Avatar changed.");
                string sourcePath=(string)author["source_asset"];Need(Hash(sourcePath)==(string)author["source_sha256"]&&Hash(sourcePath+".meta")== (string)author["source_importer_sha256"],"Source/importer changed.");
                var source=AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));
                var saved=AssetDatabase.LoadAssetAtPath<AnimationClip>(Saved);Need(saved!=null,"Saved AnimationClip could not load.");
                var original=Call("Sample",entry.prefab,scene,"loco_walk",source,1f,1f,true,null);
                var replay=Call("Sample",entry.prefab,scene,"loco_walk",saved,1f,1f,true,null);
                string framesPath=Path.Combine(output,"rain-saved-replay.json");Call("WriteFrames",framesPath,replay,saved.length);
                var expected=JObject.Parse(File.ReadAllText(Path.Combine(evidence,"native-rain-walk-contact-v2/rain-candidate.json")));var actual=JObject.Parse(File.ReadAllText(framesPath));
                Need(expected["frames"].Count()==241&&actual["frames"].Count()==241,"Full source cycle required.");
                float joint=0,sole=0,rotation=0;
                Vector3 V(JToken t){var a=t.Values<float>().ToArray();return new Vector3(a[0],a[1],a[2]);}
                Quaternion Q(JToken t){var a=t.Values<float>().ToArray();return new Quaternion(a[0],a[1],a[2],a[3]);}
                for(int i=0;i<241;i++)
                {
                    var a=expected["frames"][i];var b=actual["frames"][i];Need(a["surfaces"].Count()==3&&b["surfaces"].Count()==3,"All3 skins required.");
                    Need(Mathf.Abs((float)a["nativeNormalizedTime"]-(float)b["nativeNormalizedTime"])<.0001f,"Native phase correspondence changed.");
                    for(int j=0;j<a["joints"].Count();j++){joint=Mathf.Max(joint,Vector3.Distance(V(a["joints"][j]),V(b["joints"][j])));rotation=Mathf.Max(rotation,Quaternion.Angle(Q(a["rotations"][j]),Q(b["rotations"][j])));}
                    for(int tier=0;tier<3;tier++)foreach(string side in new[]{"tierLeft","tierRight"})sole=Mathf.Max(sole,Mathf.Abs((float)a[side][tier]-(float)b[side][tier]));
                }
                report["author_to_saved_joint_error_m"]=joint;report["author_to_saved_sole_error_m"]=sole;report["author_to_saved_rotation_error_degrees"]=rotation;
                Need(joint<=.0001f&&sole<=.0001f&&rotation<=.1f,"Persistent clip differs from authoring candidate.");
                var gates=new JObject();report["native_contact_gates"]=gates;Call("Validate",original,replay,saved.length,gates);
                Need(Hash(Saved)==SavedHash,"Replay mutated saved clip.");report["persistent_replay_verified"]=true;report["all_pass"]=true;
            }
            catch(Exception e){report["failure"]=e.ToString();throw;}
            finally
            {
                var cleanup=new System.Collections.Generic.List<string>();
                try{if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);}catch(Exception e){cleanup.Add("owned preview: "+e.Message);}
                try{if(!originalScene.IsValid()||!originalScene.isLoaded||!SceneManager.SetActiveScene(originalScene))cleanup.Add("original active scene was not restored");}catch(Exception e){cleanup.Add("active scene: "+e.Message);}
                try{if(scratch.IsValid()&&!EditorSceneManager.CloseScene(scratch,true))cleanup.Add("owned regular scratch scene did not close");}catch(Exception e){cleanup.Add("owned scratch: "+e.Message);}
                var restored=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i)).Select(x=>new{x.handle,x.path,x.isDirty,x.isLoaded}).ToArray();
                bool sceneRestored=originalSetup.SequenceEqual(restored)&&SceneManager.GetActiveScene().handle==originalScene.handle;
                report["original_scene_setup"]=JArray.FromObject(originalSetup);report["restored_scene_setup"]=JArray.FromObject(restored);report["original_scene_setup_restored"]=sceneRestored;report["scene_cleanup_failures"]=new JArray(cleanup);
                if(!sceneRestored||cleanup.Count>0){report["all_pass"]=false;report["persistent_replay_verified"]=false;}
                var after=Directory.GetFiles("Assets/Art/characters/authored","*",SearchOption.AllDirectories).Concat(Directory.GetFiles("Assets/Resources/Characters","*",SearchOption.AllDirectories)).Concat(Directory.GetFiles("Assets/Art/animations","*",SearchOption.AllDirectories)).ToDictionary(p=>p,Hash);
                var drift=before.Keys.Union(after.Keys).Where(k=>!before.TryGetValue(k,out var a)||!after.TryGetValue(k,out var b)||a!=b).ToArray();report["protected_drift"]=new JArray(drift);if(drift.Length>0){report["all_pass"]=false;report["persistent_replay_verified"]=false;}
                File.WriteAllText(Path.Combine(output,"receipt.json"),report.ToString());File.WriteAllText(Path.Combine(parent,"receipt.json"),report.ToString());Need(drift.Length==0,"Replay changed protected assets.");Need(sceneRestored&&cleanup.Count==0,"Replay did not preserve original scenes and close its owned scratch.");
            }
        }
        private static string Hash(string path){using var sha=SHA256.Create();using var f=File.OpenRead(path);return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
        private static void Need(bool pass,string message){if(!pass)throw new InvalidOperationException("Rain persistent replay: "+message);}
    }
}
