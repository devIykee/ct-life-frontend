#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
using Object=UnityEngine.Object;
namespace CTLife.EditorTools
{
    // Candidate-only source import/comparison. The source Knight is a rig carrier, never a selected look.
    public static class KayKitRawRunCandidate
    {
        private const string Output="Assets/Art/Candidates/KayKitRawRun20261007";
        private const string Model=Output+"/Knight.fbx";
        private const string External="../ct-life-evidence/art/character-candidates/motion-review-20261006/regular-run-contact-20261007/rain-source-audit-20261007/sources/kaykit-adventurers/Knight.fbx";
        private const string SourceHash="b25e35cc7eaaa3c7c103f10a4f41fde1cbd81936fede9aa9ae4efbd29d40ba1b";
        private const string Sampler="Assets/Scripts/Editor/RunRestContactReview.cs";
        private const string Writer="Assets/Scripts/Editor/LocomotionQualityProbe.cs";
        private const string SamplerHash="cbd4ae14e1a727e46f876380f890615d830e1afc3af1668a7bb9708587340ef3";
        private const string WriterHash="c9c9f1d1629ccd7c1f8511a1c3398a27d768aaa2aa5ebb4a77c9f8555302fa3a";
        private static readonly Dictionary<string,string> Bones=new Dictionary<string,string>{
            {"Hips","hips"},{"Spine","spine"},{"Chest","chest"},{"Head","head"},
            {"LeftUpperArm","upperarm.l"},{"LeftLowerArm","lowerarm.l"},{"LeftHand","wrist.l"},
            {"RightUpperArm","upperarm.r"},{"RightLowerArm","lowerarm.r"},{"RightHand","wrist.r"},
            {"LeftUpperLeg","upperleg.l"},{"LeftLowerLeg","lowerleg.l"},{"LeftFoot","foot.l"},{"LeftToes","toes.l"},
            {"RightUpperLeg","upperleg.r"},{"RightLowerLeg","lowerleg.r"},{"RightFoot","foot.r"},{"RightToes","toes.r"}};
        public static void Stage()
        {
            Need(!Directory.Exists(Output),"Candidate output exists; preserve it and use a separately reviewed revision.");
            string repo=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));string source=Path.Combine(repo,External);
            Need(Hash(source)==SourceHash,"Downloaded source differs.");var before=Snapshot();var receipt=new JObject{["status"]="failed_not_accepted",["source_sha256"]=SourceHash};
            Directory.CreateDirectory(Output);
            try
            {
                File.Copy(source,Model,false);AssetDatabase.ImportAsset(Model,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
                var importer=(ModelImporter)AssetImporter.GetAtPath(Model);
                importer.globalScale=1;importer.useFileScale=true;importer.bakeAxisConversion=true;
                importer.importCameras=false;importer.importLights=false;importer.materialImportMode=ModelImporterMaterialImportMode.None;
                importer.isReadable=true;importer.optimizeGameObjects=false;importer.animationCompression=ModelImporterAnimationCompression.Off;
                importer.animationType=ModelImporterAnimationType.Generic;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
                importer.sourceAvatar=null;importer.humanDescription=new HumanDescription();importer.importAnimation=true;importer.clipAnimations=Array.Empty<ModelImporterClipAnimation>();
                importer.SaveAndReimport();
                var takes=importer.defaultClipAnimations;receipt["generic_takes"]=JArray.FromObject(takes.Select(c=>new{c.name,c.takeName,c.firstFrame,c.lastFrame}));
                var tpose=AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Single(c=>c.name=="T-Pose");
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(Model);var preview=EditorSceneManager.NewPreviewScene();GameObject reference=null;HumanDescription description;
                try
                {
                    reference=Object.Instantiate(model);SceneManager.MoveGameObjectToScene(reference,preview);reference.name=model.name;
                    foreach(var animator in reference.GetComponentsInChildren<Animator>(true))animator.enabled=false;
                    tpose.SampleAnimation(reference,0); // Authored Generic T-pose, not an animated-default pose or private editor auto-pose API.
                    var transforms=reference.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.ToArray());
                    foreach(var pair in Bones)Need(transforms.TryGetValue(pair.Value,out var matches)&&matches.Length==1,"Ambiguous/missing mapped joint: "+pair.Value);
                    Vector3 P(string name)=>reference.transform.InverseTransformPoint(transforms[name][0].position);
                    var forward=(P("toes.l")+P("toes.r")-P("foot.l")-P("foot.r"))*.5f;
                    Need(forward.z>0&&Mathf.Abs(forward.x)<.01f,"Imported source heading needs explicit diagnosis, not a guessed 180-degree flip.");
                    foreach(string side in new[]{"l","r"})
                    {float forearm=Vector3.Distance(P("lowerarm."+side),P("wrist."+side));float palm=Vector3.Distance(P("wrist."+side),P("hand."+side));Need(forearm>palm&&palm>.01f,"Source anatomical wrist distinction changed.");}
                    description=new HumanDescription{
                        human=Bones.Select(pair=>new HumanBone{humanName=pair.Key,boneName=pair.Value,limit=new HumanLimit{useDefaultValues=true}}).ToArray(),
                        skeleton=reference.GetComponentsInChildren<Transform>(true).Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray(),
                        upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,armStretch=.05f,legStretch=.05f,feetSpacing=0,hasTranslationDoF=false};
                    receipt["source_tpose_joints"]=JObject.FromObject(Bones.ToDictionary(p=>p.Key,p=>V(P(p.Value))));
                    receipt["mapping"]=JObject.FromObject(Bones);
                    receipt["authored_tpose_clip"]=new JObject{["name"]=tpose.name,["seconds"]=tpose.length,["humanMotion"]=tpose.humanMotion};
                }
                finally{if(reference!=null)Object.DestroyImmediate(reference);EditorSceneManager.ClosePreviewScene(preview);}
                importer.humanDescription=description;importer.animationType=ModelImporterAnimationType.Human;
                importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.sourceAvatar=null;
                var selected=new[]{"Running_A","Running_B"}.Select(name=>takes.Single(c=>c.name==name)).ToArray();
                foreach(var clip in selected)
                {
                    clip.loopTime=true;clip.loopPose=false;clip.cycleOffset=0;
                    clip.lockRootRotation=true;clip.lockRootHeightY=true;clip.lockRootPositionXZ=true;
                    clip.keepOriginalOrientation=true;clip.rotationOffset=0;
                    clip.keepOriginalPositionY=true;clip.heightFromFeet=false;clip.heightOffset=0;clip.keepOriginalPositionXZ=true;
                }
                importer.clipAnimations=selected;importer.SaveAndReimport();
                var avatar=AssetDatabase.LoadAllAssetsAtPath(Model).OfType<Avatar>().Single();Need(avatar.isHuman&&avatar.isValid,"Authored-source Humanoid Avatar is invalid. Do not copy the CT Avatar or silently auto-pose it.");
                var motions=AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal)).ToArray();
                Need(motions.Length==2&&motions.All(c=>c.humanMotion&&c.isLooping),"Expected only two raw Humanoid loops.");
                Need(Mathf.Abs(motions.Single(c=>c.name=="Running_A").length-.8f)<.002f&&Mathf.Abs(motions.Single(c=>c.name=="Running_B").length-32f/30)<.002f,"Imported source duration differs from downloaded source.");
                receipt["clips"]=JArray.FromObject(motions.Select(c=>new{c.name,c.length,c.frameRate,c.humanMotion,c.isLooping,averageSpeed=V(c.averageSpeed)}));
                receipt["model_sha256"]=Hash(Model);receipt["meta_sha256"]=Hash(Model+".meta");
                receipt["settings"]=JObject.FromObject(new{importer.globalScale,importer.useFileScale,importer.bakeAxisConversion,importer.animationCompression,importer.animationType,importer.avatarSetup,importer.optimizeGameObjects,importer.isReadable,clips=importer.clipAnimations.Select(c=>new{c.name,c.takeName,c.firstFrame,c.lastFrame,c.loopTime,c.loopPose,c.cycleOffset,c.lockRootRotation,c.lockRootHeightY,c.lockRootPositionXZ,c.keepOriginalOrientation,c.rotationOffset,c.keepOriginalPositionY,c.heightFromFeet,c.heightOffset,c.keepOriginalPositionXZ})});
                receipt["status"]="staged_raw_sources_not_animation_accepted";
            }
            catch(Exception error){receipt["failure"]=error.ToString();throw;}
            finally
            {
                var after=Snapshot();var drift=before.Where(p=>!after.TryGetValue(p.Key,out var value)||value!=p.Value).Select(p=>p.Key).ToArray();var added=after.Keys.Except(before.Keys).ToArray();
                receipt["protected_drift"]=new JArray(drift);receipt["unexpected_added"]=new JArray(added);receipt["protected_count"]=before.Count;
                if(drift.Length>0||added.Length>0)receipt["status"]="failed_protected_tree_changed";
                File.WriteAllText(Output+"/StageReceipt.json",receipt.ToString());Need(drift.Length==0&&added.Length==0,"Protected tree changed; no automatic restore attempted.");
            }
        }
        public static void CaptureRaw()
        {
            Need(Hash(Sampler)==SamplerHash&&Hash(Writer)==WriterHash,"Pinned native sampler/writer changed.");
            var stage=JObject.Parse(File.ReadAllText(Output+"/StageReceipt.json"));Need((string)stage["status"]=="staged_raw_sources_not_animation_accepted"&&!(stage["protected_drift"] as JArray).Any()&&!(stage["unexpected_added"] as JArray).Any(),"Source stage is not clean.");
            Need(Hash(Model)==(string)stage["model_sha256"]&&Hash(Model+".meta")== (string)stage["meta_sha256"],"Imported source bytes/settings changed.");
            string repo=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));string output=Path.Combine(repo,"../ct-life-evidence/art/character-candidates/motion-review-20261006/regular-run-contact-20261007/kaykit-raw-compare-v1/native-raw-six-case");
            Need(!Directory.Exists(output),"Raw evidence output exists; preserve it.");Directory.CreateDirectory(output);var before=Snapshot();var receipt=new JObject{["status"]="failed_not_accepted",["stage_receipt_sha256"]=Hash(Output+"/StageReceipt.json"),["source_sha256"]=SourceHash,["sampler_sha256"]=SamplerHash};var cases=new JArray();
            try
            {
                var method=typeof(RunRestContactReview).GetMethod("CaptureRig",BindingFlags.Static|BindingFlags.NonPublic);Need(method!=null,"Pinned continuous native sampler missing.");
                var registry=AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
                var source=AssetDatabase.LoadAllAssetsAtPath("Assets/Art/animations/loco_run.fbx").OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal));
                foreach(string look in new[]{"snow","rain"})
                {
                    var entry=registry.looks.Single(e=>e.key.Split('|').Contains("head_"+look));Need(entry.prefab!=null,"Exact adult world prefab absent.");Need(entry.prefab.GetComponentsInChildren<SnowRunCorrection>(true).Length==0,"Raw source review refuses a prefab with a runtime run correction.");
                    var effective=entry.controllerOverride!=null?entry.controllerOverride:registry.controller;AnimationClip current=source;
                    if(effective is AnimatorOverrideController overrides){var pairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();overrides.GetOverrides(pairs);var match=pairs.Single(p=>p.Key==source);if(match.Value!=null)current=match.Value;}
                    foreach(string name in new[]{"current","Running_A","Running_B"})
                    {
                        var clip=name=="current"?current:AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Single(c=>c.name==name);
                        var result=(JObject)method.Invoke(null,new object[]{look,entry.prefab,clip,"kaykit-raw-"+name,false,output});
                        Need((int?)result["frames"]==121&&(int?)result["skins"]==3,"Native sampler did not capture the exact121-frame/all3-skin case.");
                        result["clip_path"]=AssetDatabase.GetAssetPath(clip);result["clip_asset_sha256"]=Hash(AssetDatabase.GetAssetPath(clip));result["prefab_sha256"]=Hash(AssetDatabase.GetAssetPath(entry.prefab));result["source_name"]=name;cases.Add(result);
                    }
                }
                Need(cases.Count==6,"Six source/look cases required.");receipt["status"]="captured_raw_sources_not_motion_accepted";
            }
            catch(Exception error){receipt["failure"]=error.ToString();throw;}
            finally
            {
                receipt["cases"]=cases;var after=Snapshot();var drift=before.Where(p=>!after.TryGetValue(p.Key,out var value)||value!=p.Value).Select(p=>p.Key).ToArray();var added=after.Keys.Except(before.Keys).ToArray();receipt["protected_drift"]=new JArray(drift);receipt["unexpected_added"]=new JArray(added);
                receipt["limits"]="Native synchronous Animator.Update comparison only, two warm cycles with real normalized-time assertions, exact adult world prefabs/own Avatars/all3 skins. One-state1x raw clips, FootIK OFF/layer IK OFF/root motion OFF, no run correction or root-Y lift. No naturalness, real movement, transitions, contact, source heading, rendered animation or phone acceptance is inferred. Existing actual-source stride speed must be measured against 4.5m/s before tuning.";
                if(drift.Length>0||added.Length>0)receipt["status"]="failed_protected_tree_changed";
                File.WriteAllText(Path.Combine(output,"receipt.json"),receipt.ToString());Need(drift.Length==0&&added.Length==0,"Protected tree changed; no automatic restore attempted.");
            }
        }
        private static Dictionary<string,string> Snapshot()=>new[]{"Assets","ProjectSettings","Packages"}.SelectMany(root=>Directory.GetFiles(root,"*",SearchOption.AllDirectories)).Where(p=>!p.Replace('\\','/').StartsWith(Output+"/",StringComparison.Ordinal)&&p.Replace('\\','/')!=Output+".meta").ToDictionary(p=>p,Hash);
        private static float[] V(Vector3 p)=>new[]{p.x,p.y,p.z};
        private static string Hash(string path){using var sha=SHA256.Create();using var f=File.OpenRead(path);return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
        private static void Need(bool condition,string message){if(!condition)throw new InvalidDataException("KayKit raw run: "+message);}
    }
}
#endif
