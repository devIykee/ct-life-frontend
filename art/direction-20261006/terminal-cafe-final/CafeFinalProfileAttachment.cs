// Candidate-only attachment. Not import-ready until the separately reviewed fitted-exit proof is pinned below.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    public static class CafeFinalProfileAttachment
    {
        private const string PrefabPath="Assets/Art/Candidates/TerminalCafe20261006Final/TerminalCafeFinal.prefab";
        private const string RegistryPath="Assets/Resources/Characters/CharacterRegistry.asset";
        private const string BaselineReceiptSha256="6462f23d8363d68f7ada1c0d73d5f5b5254c45dc6ebd241791870b6d1a7e67c9";
        private const string ProfileTypeName="CTLife.World.CafeSeatMotionProfile";
        private const string ControllerReceiptSha256="d120c156e3073298561b242c0f90c365119220e766c829f44e8c9f6a29c7c65a";

        // Intentionally empty, not a command-line bypass or claimed approval. Direct typing exits failed.
        // Complete these only after motion/root inspect the actual final runtime + visual exit evidence.
        // Pin the exact real receipt bytes and the runtime source identities used by that accepted run.
        private const string ApprovedExitReceiptSha256="";
        private static readonly Dictionary<string,string> ApprovedExitSourceSha256=new Dictionary<string,string>();

        private static string MotionRoot=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/character-candidates/motion-review-20261006"));
        private static string ReceiptPath=>Path.Combine(MotionRoot,"native-cafe-seat-controller-v1/receipt.json");
        private sealed class Row
        {
            public string character;
            public RuntimeAnimatorController baseline,candidate;
            public Avatar avatar;
            public JObject evidence;
        }

        // A usable read-only preflight, independent of the still unresolved exit implementation.
        public static void ValidateBindings()
        {
            RequireIdle();var rows=ReadBindings();var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Need(prefab!=null,"Stage the unregistered final geometry first.");CheckAnchors(prefab);
            Debug.Log("CTLIFE [cafe-profile] exact two-look controller/Avatar bindings verified. Attachment remains gated on final fitted exits.");
        }

        public static void AttachAcceptedProfile()
        {
            RequireIdle();
            string exitReceipt=RequireAcceptedExit(); // No scene, backup or asset mutation before this closed gate.
            var rows=ReadBindings();var original=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Need(original!=null&&!EditorUtility.IsDirty(original),"Final prefab missing or has unsaved changes.");CheckAnchors(original);
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(ProfileTypeName)).FirstOrDefault(t=>t!=null);
            Need(type!=null&&typeof(Component).IsAssignableFrom(type),"Reviewed runtime profile type not installed.");
            string beforeAnchors=AnchorSnapshot(original);byte[] before=File.ReadAllBytes(PrefabPath);
            var protectedPaths=AssetDatabase.GetDependencies(new[]{PrefabPath,RegistryPath}.Concat(rows.Select(r=>AssetDatabase.GetAssetPath(r.candidate))).ToArray(),true)
                .Where(p=>p!=PrefabPath&&File.Exists(p)).Concat(new[]{PrefabPath+".meta",ReceiptPath,exitReceipt})
                .Concat(ApprovedExitSourceSha256.Keys).Distinct().ToArray();
            var frozen=protectedPaths.Concat(protectedPaths.Where(p=>File.Exists(p+".meta")).Select(p=>p+".meta")).Distinct().ToDictionary(p=>p,Hash);
            string backup=Path.Combine(Path.GetTempPath(),"ct-cafe-final-profile-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(backup);
            File.WriteAllBytes(Path.Combine(backup,"before.prefab"),before);
            GameObject contents=null;bool saved=false;
            var receipt=new JObject{["status"]="running",["prefab"]=PrefabPath,["before_sha256"]=Hash(PrefabPath),
                ["controller_receipt_sha256"]=Hash(ReceiptPath),["exit_receipt"]=exitReceipt,["exit_receipt_sha256"]=Hash(exitReceipt),
                ["exit_runtime_sources"]=JObject.FromObject(ApprovedExitSourceSha256),["bindings"]=new JArray(rows.Select(r=>r.evidence.DeepClone())),
                ["scope"]="Exact final-prefab profile attachment only. No registry, controller, clips, furniture, anchors, navigation or bake changes."};
            try
            {
                contents=PrefabUtility.LoadPrefabContents(PrefabPath);CheckAnchors(contents);
                Need(AnchorSnapshot(contents)==beforeAnchors,"Loading candidate changed anchor identities/transforms.");
                var profiles=contents.GetComponents(type);Need(profiles.Length<=1,"Ambiguous profile components.");
                var profile=profiles.Length==1?profiles[0]:contents.AddComponent(type);
                var serialized=new SerializedObject(profile);var bindings=serialized.FindProperty("bindings");Need(bindings!=null&&bindings.isArray,"Runtime binding layout changed.");
                if(profiles.Length==1)Need(Matches(bindings,rows),"Existing profile differs; explicit review required before replacement.");
                else
                {
                    bindings.arraySize=rows.Length;
                    for(int i=0;i<rows.Length;i++)
                    {
                        var element=bindings.GetArrayElementAtIndex(i);
                        element.FindPropertyRelative("baselineController").objectReferenceValue=rows[i].baseline;
                        element.FindPropertyRelative("ownAvatar").objectReferenceValue=rows[i].avatar;
                        element.FindPropertyRelative("cafeController").objectReferenceValue=rows[i].candidate;
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();Need(Matches(serialized.FindProperty("bindings"),rows),"Staged binding references differ.");
                    Need(AnchorSnapshot(contents)==beforeAnchors,"Profile attachment changed anchors.");
                    Need(PrefabUtility.SaveAsPrefabAsset(contents,PrefabPath)!=null,"Could not save only the final candidate prefab.");saved=true;
                }
                PrefabUtility.UnloadPrefabContents(contents);contents=null;
                var reloaded=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);CheckAnchors(reloaded);
                Need(AnchorSnapshot(reloaded)==beforeAnchors,"Saved profile changed anchor geometry.");
                var loadedProfiles=reloaded.GetComponents(type);Need(loadedProfiles.Length==1,"Saved candidate profile missing or ambiguous.");
                Need(Matches(new SerializedObject(loadedProfiles[0]).FindProperty("bindings"),rows),"Saved candidate bindings differ.");
                Need(frozen.All(p=>File.Exists(p.Key)&&Hash(p.Key)==p.Value),"Protected dependencies changed.");
                receipt["status"]=saved?"profile_attached_unbaked_unregistered":"existing_exact_profile_unchanged";
                receipt["after_sha256"]=Hash(PrefabPath);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(PrefabPath),"ProfileAttachmentReceipt.json"),receipt.ToString());
            }
            catch(Exception error)
            {
                receipt["status"]="failed";receipt["error"]=error.ToString();
                if(contents!=null){PrefabUtility.UnloadPrefabContents(contents);contents=null;}
                if(!File.ReadAllBytes(PrefabPath).SequenceEqual(before))
                {
                    File.Copy(PrefabPath,Path.Combine(backup,"after-failure.prefab"));File.WriteAllBytes(PrefabPath,before);
                    receipt["own_prefab_bytes_restored"]=true; // Exit/reopen before using the in-memory candidate again.
                }
                throw;
            }
            finally
            {
                if(contents!=null)PrefabUtility.UnloadPrefabContents(contents);
                receipt["protected_drift"]=new JArray(frozen.Where(p=>!File.Exists(p.Key)||Hash(p.Key)!=p.Value).Select(p=>p.Key));
                File.WriteAllText(Path.Combine(backup,"transaction.json"),receipt.ToString());
                Debug.Log("CTLIFE [cafe-profile] transaction: "+backup);
            }
        }

        private static Row[] ReadBindings()
        {
            Need(File.Exists(ReceiptPath)&&Hash(ReceiptPath)==ControllerReceiptSha256,"Exact controller-preservation receipt changed.");
            var receipt=JObject.Parse(File.ReadAllText(ReceiptPath));
            Need((bool?)receipt["all_pass"]==true&&receipt["protected_drift"] is JArray drift&&drift.Count==0,"Controller fixture did not pass without drift.");
            var cases=(JArray)receipt["cases"];Need(cases.Count==2,"Expected two approved world looks.");
            var registry=AssetDatabase.LoadAssetAtPath<CharacterRegistry>(RegistryPath);Need(registry!=null,"Current registry missing.");
            string baselinePath=Path.Combine(MotionRoot,"cafe-seat-integration/baseline-sha256.json");
            Need(File.Exists(baselinePath)&&Hash(baselinePath)==BaselineReceiptSha256,"Frozen original world-body identity record changed.");
            var baseline=JObject.Parse(File.ReadAllText(baselinePath))["files"] as JObject;
            foreach(var property in baseline.Properties().Where(p=>p.Name=="client/"+RegistryPath||p.Name.StartsWith("client/Assets/Art/characters/authored/Snow/",StringComparison.Ordinal)||p.Name.StartsWith("client/Assets/Art/characters/authored/Rain/",StringComparison.Ordinal)))
            { string path=property.Name.Substring("client/".Length);Need(File.Exists(path)&&Hash(path)==(string)property.Value,"Approved world body/registry bytes changed: "+path); }
            return new[]{"snow","rain"}.Select(character=>
            {
                var c=cases.OfType<JObject>().Single(x=>(string)x["character"]==character);Need((bool?)c["all_pass"]==true,"Character controller fixture failed.");
                foreach(string key in new[]{"controller","baseline_controller","social","typing"})
                { string path=(string)c[key+(key=="social"||key=="typing"?"_clip":"_asset")];Need(File.Exists(path)&&Hash(path)==(string)c[key+"_sha256"],"Approved asset changed: "+key); }
                Need(c["original_state_comparisons"] is JArray original&&original.Count==46&&original.All(x=>(float?)x["all_tier_skin_error_m"]<=.00001f),"46 original-state proof missing.");
                Need(c["private_state_comparisons"] is JArray own&&own.Count==2&&own.All(x=>(int?)x["samples"]==241&&(float?)x["all_tier_skin_error_m"]<=.00001f),"Private native pose proof missing.");
                var entry=registry.looks.Single(x=>x.key==(string)c["look_key"]);var current=entry.controllerOverride??registry.controller;
                Need(AssetDatabase.GetAssetPath(entry.prefab)==(string)c["world_prefab_asset"]&&AssetDatabase.GetAssetPath(current)==(string)c["baseline_controller_asset"],"Current world binding differs from fixture.");
                var avatar=entry.prefab.GetComponentsInChildren<Animator>(true).Single().avatar;
                Need(avatar!=null&&avatar.isHuman&&avatar.name==(string)c["own_avatar_name"]&&AssetDatabase.GetAssetPath(avatar)==(string)c["own_avatar_asset"],"Current own Avatar differs from fixture.");
                var candidate=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>((string)c["controller_asset"]);Need(candidate!=null,"Saved candidate controller missing.");
                return new Row{character=character,baseline=current,avatar=avatar,candidate=candidate,evidence=new JObject{
                    ["character"]=character,["baseline_controller"]=(string)c["baseline_controller_asset"],["baseline_sha256"]=(string)c["baseline_controller_sha256"],
                    ["own_avatar_asset"]=(string)c["own_avatar_asset"],["own_avatar_name"]=avatar.name,["candidate_controller"]=(string)c["controller_asset"],["candidate_sha256"]=(string)c["controller_sha256"]}};
            }).ToArray();
        }

        private static string RequireAcceptedExit()
        {
            Need(ApprovedExitReceiptSha256.Length==64&&ApprovedExitSourceSha256.Count>0,
                "Final fitted exit runtime/visual evidence is not yet accepted. Geometry may stage; attachment must wait. No placeholder approval is available.");
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-ctlife-cafeSeatExitReceipt");
            Need(i>=0&&i+1<args.Length,"Supply the exact separately accepted fitted-exit receipt path.");string path=Path.GetFullPath(args[i+1]);
            Need(File.Exists(path)&&Hash(path)==ApprovedExitReceiptSha256,"Exit receipt differs from the explicitly reviewed final proof.");
            foreach(var source in ApprovedExitSourceSha256)Need(File.Exists(source.Key)&&Hash(source.Key)==source.Value,"Final exit runtime differs from its accepted source: "+source.Key);
            return path;
        }
        private static bool Matches(SerializedProperty bindings,Row[] rows)
        {
            if(bindings==null||bindings.arraySize!=rows.Length)return false;
            for(int i=0;i<rows.Length;i++)
            { var p=bindings.GetArrayElementAtIndex(i);if(p.FindPropertyRelative("baselineController")?.objectReferenceValue!=rows[i].baseline||p.FindPropertyRelative("ownAvatar")?.objectReferenceValue!=rows[i].avatar||p.FindPropertyRelative("cafeController")?.objectReferenceValue!=rows[i].candidate)return false; }
            return true;
        }
        private static void CheckAnchors(GameObject root)
        {
            var venue=root.GetComponent<VenueSceneRoot>();Need(venue!=null&&venue.venueId=="terminal_cafe"&&!venue.IsGreybox,"Expected the authored final Café root.");
            var anchors=root.GetComponentsInChildren<SpotAnchor>(true);Need(anchors.Length==17&&anchors.Select(a=>a.spotId).Distinct().Count()==17,"Final contract anchor count/uniqueness changed.");
            foreach(string id in Enumerable.Range(1,4).Select(i=>"table_"+i).Concat(Enumerable.Range(1,8).Select(i=>"terminal_"+i)))
            { var a=anchors.Single(x=>x.spotId==id);Need(a.anim==(id.StartsWith("table_",StringComparison.Ordinal)?"act_sit":"act_type")&&a.approach!=null,"Missing exact anchor/action/approach: "+id); }
        }
        private static string AnchorSnapshot(GameObject root)=>new JArray(root.GetComponentsInChildren<SpotAnchor>(true).OrderBy(a=>a.spotId).Select(a=>new JObject{
            ["id"]=a.spotId,["kind"]=a.kind,["anim"]=a.anim,["position"]=V(a.transform.position),["rotation"]=Q(a.transform.rotation),
            ["scale"]=V(a.transform.lossyScale),["approach"]=a.approach==null?JValue.CreateNull():(JToken)V(a.approach.position)})).ToString();
        private static JArray V(Vector3 v)=>new JArray(v.x,v.y,v.z);
        private static JArray Q(Quaternion q)=>new JArray(q.x,q.y,q.z,q.w);
        private static void RequireIdle()=>Need(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&!Lightmapping.isRunning,"Use idle Edit mode after imports.");
        private static void Need(bool value,string message){if(!value)throw new InvalidOperationException("Cafe final profile: "+message);}
        private static string Hash(string path){using var sha=SHA256.Create();using var f=File.OpenRead(path);return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    }
}
