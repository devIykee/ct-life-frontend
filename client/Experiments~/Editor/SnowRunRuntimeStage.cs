#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using CTLife.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CTLife.EditorTools
{
    public static class SnowRunRuntimeStage
    {
        public const string Output="Assets/Art/Candidates/SnowRunRuntime20261007";
        public const string ProfilePath=Output+"/SnowRunCorrection.asset";
        private const string ProofFolder="../ct-life-evidence/art/character-candidates/motion-review-20261006/native-snow-sprint-local-cache-v2";
        public static void BuildCandidate()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stage requires Edit mode.");
            if(Directory.Exists(Output))throw new InvalidOperationException("Existing candidate preserved; output already exists.");
            string repo=Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
            string proof=Path.Combine(repo,ProofFolder,"candidate-receipt.json"),table=Path.Combine(repo,ProofFolder,"Snow_SprintLocalCache-table.json");
            var receipt=JObject.Parse(File.ReadAllText(proof));var row=(JObject)receipt["cases"][0];
            Require((bool)receipt["all_pass"]&&(bool)row["all_pass"]&&receipt["protected_drift"].Count()==0,"Native table proof not accepted.");
            Require(Hash(table)==SnowRunCorrectionProfile.AcceptedTableSha256&&(string)row["table_sha256"]==Hash(table),"Exact saved table changed.");
            var guards=(JObject)receipt["protected_hashes"];
            string prefabPath="Assets/Art/characters/authored/Snow/EverydayWorld.prefab",clipPath="Assets/Art/animations/loco_run.fbx";
            foreach(string path in new[]{prefabPath,prefabPath+".meta",clipPath,clipPath+".meta","Assets/Art/characters/authored/Snow/SnowAdult_Creator.fbx","Assets/Art/characters/authored/Snow/SnowAdult_Creator.fbx.meta"})
                Require(guards[path]!=null&&Hash(path)==(string)guards[path],"Accepted dependency differs: "+path);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var clip=AssetDatabase.LoadAllAssetsAtPath(clipPath).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal));
            var baseline=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Resources/Characters/SnowController.overrideController");
            var fitted=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Art/Candidates/PlayableVenues20261007/Controllers/snow.controller");
            Require(Hash(AssetDatabase.GetAssetPath(fitted))=="598f4c82fabd1e96e8d0a4f99c0279d4eafc437e6d73c5cde4d2ef9746cff6e4","Selected fitted controller differs.");
            CheckRun(baseline,clip);CheckRun(fitted,clip);
            var sourcePins=new JObject();
            foreach(string path in AssetDatabase.GetDependencies(new[]{prefabPath,clipPath,AssetDatabase.GetAssetPath(baseline),AssetDatabase.GetAssetPath(fitted)},true).Where(File.Exists).OrderBy(x=>x))sourcePins[path]=Hash(path);
            Directory.CreateDirectory(Output);File.Copy(table,Output+"/SnowRunLocalTable.json",false);
            AssetDatabase.ImportAsset(Output+"/SnowRunLocalTable.json",ImportAssetOptions.ForceSynchronousImport);
            var profile=ScriptableObject.CreateInstance<SnowRunCorrectionProfile>();
            profile.sourcePrefab=prefab;profile.sourceAvatar=prefab.GetComponentInChildren<Animator>(true).avatar;profile.sourceClip=clip;
            profile.allowedControllers=new[]{baseline,fitted};profile.sourceTable=AssetDatabase.LoadAssetAtPath<TextAsset>(Output+"/SnowRunLocalTable.json");
            Require(profile.Read(out var reason),reason??"Profile parse failed.");
            AssetDatabase.CreateAsset(profile,ProfilePath);AssetDatabase.SaveAssetIfDirty(profile);
            File.WriteAllText(Output+"/StageReceipt.json",new JObject{
                ["status"]="staged_unassigned_runtime_transitions_pending",["table_sha256"]=Hash(table),["native_table_receipt_sha256"]=Hash(proof),
                ["profile_path"]=ProfilePath,["profile_sha256"]=Hash(ProfilePath),["source_dependencies"]=sourcePins,
                ["resources_assignment"]=false,["limits"]="Exact saved table and controller source identity only. OnAnimatorMove, real blended poses, interruptions/culling/disable/look replacement and mobile cost remain unaccepted. Rain unchanged."
            }.ToString());
            Debug.Log("[snow-run-stage] Candidate profile created outside Resources; no runtime assignment or global save.");
        }
        private static void CheckRun(RuntimeAnimatorController runtime,AnimationClip expected)
        {
            Require(runtime!=null,"Missing allowed controller.");var over=runtime as AnimatorOverrideController;
            var controller=(over!=null?over.runtimeAnimatorController:runtime)as AnimatorController;
            Require(controller!=null&&controller.layers.Length==1,"Only the inspected single layer is supported.");
            var states=controller.layers[0].stateMachine.states.Where(s=>s.state.name=="loco_run").ToArray();Require(states.Length==1,"Run state identity differs.");
            var state=states[0].state;var source=state.motion as AnimationClip;var effective=over!=null&&source!=null?(over[source]??source):source;
            Require(effective==expected&&!state.iKOnFeet&&state.writeDefaultValues&&!state.mirror&&!state.mirrorParameterActive&&!state.timeParameterActive&&state.cycleOffset==0,
                "Run clip/IK/mirror/time source semantics differ.");
            Require(Mathf.Abs(state.speed-1f/4.5f)<1e-6f&&state.speedParameterActive&&state.speedParameter=="speed","Run rate semantics differ.");
        }
        private static string Hash(string path){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        private static void Require(bool value,string reason){if(!value)throw new InvalidDataException(reason);}
    }
}
#endif
