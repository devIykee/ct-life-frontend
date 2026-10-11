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
    public static class KayKitRawRunCandidateV5
    {
        private const string Output="Assets/Art/characters/authored/MotionReview20261006/KayKitRawRunV5";
        private const string Model=Output+"/Knight.fbx";
        private const string External="art/character-candidates/motion-review-20261006/regular-run-contact-20261007/rain-source-audit-20261007/sources/kaykit-adventurers/Knight.fbx";
        private const string ImportPolicyHash="4dbee7271fdce47c6542f8a65cddb9a4c445ad23920c7b4d9e1f72764c071f3b";
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
            Need(Hash("Assets/Scripts/Editor/ContentImport.cs")==ImportPolicyHash,"Pinned importer policy changed.");
            Need(Hash("Assets/Scripts/Editor/AuthoredCharacterImport.cs")=="619863de6a51a81d508d8668e000c65ef4ee95a3f7a99aaf79e51e8ea4e64921","Pinned target canonicalization implementation changed.");
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
                importer=(ModelImporter)AssetImporter.GetAtPath(Model);
                Need(importer.animationType==ModelImporterAnimationType.Generic&&importer.importAnimation&&importer.isReadable&&importer.avatarSetup==ModelImporterAvatarSetup.CreateFromThisModel&&importer.sourceAvatar==null,"Persisted Generic settings were overridden by import policy; no take lookup attempted.");
                receipt["generic_import_settings"]=JObject.FromObject(new{importer.animationType,importer.importAnimation,importer.isReadable,importer.avatarSetup,importer.animationCompression});
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
                    // Source negative Z is not itself a bad Humanoid mapping: BodyOrientation is relative
                    // to the configured Avatar T-pose. Record it; V4 measured target face/toe−Z; preserve Original and explicitly offset180 for actor+Z.
                    receipt["source_tpose_joints"]=JObject.FromObject(Bones.ToDictionary(p=>p.Key,p=>V(P(p.Value))));
                    receipt["source_reference_axes"]=new JObject{["toe_forward"]=JArray.FromObject(V(forward)),["left_minus_right_upperleg"]=JArray.FromObject(V(P("upperleg.l")-P("upperleg.r"))),["left_minus_right_wrist"]=JArray.FromObject(V(P("wrist.l")-P("wrist.r"))),["hips_to_head"]=JArray.FromObject(V(P("head")-P("hips")))};
                    receipt["source_reference_transforms"]=JArray.FromObject(reference.GetComponentsInChildren<Transform>(true).Select(t=>new{name=t.name,parent=t.parent!=null?t.parent.name:null,localPosition=V(t.localPosition),localRotation=new[]{t.localRotation.x,t.localRotation.y,t.localRotation.z,t.localRotation.w},localScale=V(t.localScale),worldPosition=V(t.position),worldRotation=new[]{t.rotation.x,t.rotation.y,t.rotation.z,t.rotation.w}}));
                    receipt["source_mesh_bounds"]=JArray.FromObject(reference.GetComponentsInChildren<Renderer>(true).Select(r=>new{name=r.name,center=V(r.bounds.center),size=V(r.bounds.size)}));
                    File.WriteAllText(Output+"/SourceTposeDiagnostic.json",receipt.ToString());
                    Need(new Vector2(forward.x,forward.z).magnitude>.001f,"Degenerate source toe-forward axis.");
                    foreach(string side in new[]{"l","r"})
                    {float forearm=Vector3.Distance(P("lowerarm."+side),P("wrist."+side));float palm=Vector3.Distance(P("wrist."+side),P("hand."+side));Need(forearm>palm&&palm>.01f,"Source anatomical wrist distinction changed.");}
                    // Same internal Editor Enforce T-Pose operation used by the current adult import.
                    // It changes only this unsaved source reference; animation bytes and mappings remain original; V5 adds the measured Original+180 heading offset.
                    var setup=typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarSetupTool",true);
                    var wrapper=setup.GetNestedType("BoneWrapper",BindingFlags.Public|BindingFlags.NonPublic);
                    Need(wrapper!=null,"Installed Avatar reference wrapper changed.");
                    var bones=Array.CreateInstance(wrapper,HumanTrait.BoneCount);
                    for(int i=0;i<HumanTrait.BoneCount;i++)
                    {
                        string key=((HumanBodyBones)i).ToString();Transform bone=Bones.TryGetValue(key,out var node)?transforms[node][0]:null;
                        bones.SetValue(Activator.CreateInstance(wrapper,new object[]{HumanTrait.BoneName[i],bone}),i);
                    }
                    const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
                    MethodInfo Method(string name)=>setup.GetMethod(name,flags,null,new[]{bones.GetType()},null)??throw new MissingMethodException("AvatarSetupTool."+name);
                    var enforce=Method("MakePoseValid");var poseError=Method("GetPoseError");var orientation=Method("AvatarComputeOrientation");var validPose=Method("IsPoseValid");
                    JObject ReferenceSnapshot()
                    {
                        var q=(Quaternion)orientation.Invoke(null,new object[]{bones});
                        return new JObject{["pose_error"]=(float)poseError.Invoke(null,new object[]{bones}),["pose_valid"]=(bool)validPose.Invoke(null,new object[]{bones}),["avatar_orientation"]=new JArray(q.x,q.y,q.z,q.w),["joints"]=JObject.FromObject(Bones.ToDictionary(p=>p.Key,p=>V(P(p.Value)))),["transforms"]=JArray.FromObject(reference.GetComponentsInChildren<Transform>(true).Select(t=>new{name=t.name,parent=t.parent!=null?t.parent.name:null,localPosition=V(t.localPosition),localRotation=new[]{t.localRotation.x,t.localRotation.y,t.localRotation.z,t.localRotation.w},localScale=V(t.localScale),worldPosition=V(t.position),worldRotation=new[]{t.rotation.x,t.rotation.y,t.rotation.z,t.rotation.w}}))};
                    }
                    var canonical=new JObject{["operation"]="UnityEditor.AvatarSetupTool.MakePoseValid on isolated Generic authored-T-pose reference",["editor_assembly_path"]=typeof(UnityEditor.Editor).Assembly.Location,["editor_assembly_sha256"]=Hash(typeof(UnityEditor.Editor).Assembly.Location),["before"]=ReferenceSnapshot()};
                    enforce.Invoke(null,new object[]{bones});canonical["after"]=ReferenceSnapshot();
                    receipt["reference_canonicalization"]=canonical;
                    File.WriteAllText(Output+"/ReferenceCanonicalization.json",canonical.ToString());
                    Need((bool)canonical["after"]["pose_valid"],"Official Enforce T-Pose left a noncanonical source reference; evidence saved before failure.");
                    receipt["canonical_reference_joints"]=JObject.FromObject(Bones.ToDictionary(p=>p.Key,p=>V(P(p.Value))));
                    description=new HumanDescription{
                        human=Bones.Select(pair=>new HumanBone{humanName=pair.Key,boneName=pair.Value,limit=new HumanLimit{useDefaultValues=true}}).ToArray(),
                        skeleton=reference.GetComponentsInChildren<Transform>(true).Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray(),
                        upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,armStretch=.05f,legStretch=.05f,feetSpacing=0,hasTranslationDoF=false};
                    receipt["saved_canonical_reference_transforms"]=canonical["after"]["transforms"].DeepClone();
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
                    clip.keepOriginalOrientation=true;clip.rotationOffset=180;
                    clip.keepOriginalPositionY=true;clip.heightFromFeet=false;clip.heightOffset=0;clip.keepOriginalPositionXZ=true;
                }
                importer.clipAnimations=selected;importer.SaveAndReimport();
                importer=(ModelImporter)AssetImporter.GetAtPath(Model);
                Need(importer.animationType==ModelImporterAnimationType.Human&&importer.importAnimation&&importer.isReadable&&importer.avatarSetup==ModelImporterAvatarSetup.CreateFromThisModel&&importer.sourceAvatar==null&&importer.animationCompression==ModelImporterAnimationCompression.Off,"Persisted Human settings were overridden.");
                Need(importer.clipAnimations.Length==2&&importer.clipAnimations.All(c=>c.loopTime&&!c.loopPose&&c.lockRootRotation&&c.lockRootHeightY&&c.lockRootPositionXZ&&c.keepOriginalOrientation&&c.rotationOffset==180&&c.keepOriginalPositionY&&!c.heightFromFeet&&c.heightOffset==0&&c.keepOriginalPositionXZ),"Persisted raw clip/root settings differ.");
                Need(Bones.All(p=>importer.humanDescription.human.SingleOrDefault(b=>b.humanName==p.Key).boneName==p.Value),"Persisted anatomical mapping changed.");
                var avatar=AssetDatabase.LoadAllAssetsAtPath(Model).OfType<Avatar>().Single();Need(avatar.isHuman&&avatar.isValid,"Authored-source Humanoid Avatar is invalid. Do not copy the CT Avatar or silently auto-pose it.");
                var motions=AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__",StringComparison.Ordinal)).ToArray();
                Need(motions.Length==2&&motions.All(c=>c.humanMotion&&c.isLooping),"Expected only two raw Humanoid loops.");
                Need(Mathf.Abs(motions.Single(c=>c.name=="Running_A").length-.8f)<.002f&&Mathf.Abs(motions.Single(c=>c.name=="Running_B").length-32f/30)<.002f,"Imported source duration differs from downloaded source.");
                receipt["clips"]=JArray.FromObject(motions.Select(c=>new{c.name,c.length,c.frameRate,c.humanMotion,c.isLooping,averageSpeed=V(c.averageSpeed)}));
                var canonicalTransforms=(JArray)receipt["saved_canonical_reference_transforms"];
                foreach(var bone in importer.humanDescription.skeleton)
                {
                    var saved=canonicalTransforms.Single(t=>(string)t["name"]==bone.name);var pos=(JArray)saved["localPosition"];var rot=(JArray)saved["localRotation"];var scale=(JArray)saved["localScale"];
                    Need(Vector3.Distance(bone.position,new Vector3((float)pos[0],(float)pos[1],(float)pos[2]))<.000001f&&Quaternion.Angle(bone.rotation,new Quaternion((float)rot[0],(float)rot[1],(float)rot[2],(float)rot[3]))<.001f&&Vector3.Distance(bone.scale,new Vector3((float)scale[0],(float)scale[1],(float)scale[2]))<.000001f,"Persisted canonical reference differs: "+bone.name);
                }
                Need(importer.humanDescription.skeleton.Length==canonicalTransforms.Count,"Persisted reference hierarchy count changed.");
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
            string repo=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));string output=Path.Combine(repo,"art/character-candidates/motion-review-20261006/regular-run-contact-20261007/kaykit-raw-compare-v5/native-raw-six-case");
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
                        result["clip_path"]=AssetDatabase.GetAssetPath(clip);result["clip_asset_sha256"]=Hash(AssetDatabase.GetAssetPath(clip));result["prefab_sha256"]=Hash(AssetDatabase.GetAssetPath(entry.prefab));result["source_name"]=name;var sampled=JObject.Parse(File.ReadAllText(Path.Combine(output,(string)result["file"])));
                        var heading=new JArray();foreach(var frame in (JArray)sampled["frames"])
                        {
                            var joints=(JObject)frame["joints"];Vector3 P(string key){var p=(JArray)joints[key];return new Vector3((float)p[0],(float)p[1],(float)p[2]);}
                            var toe=(P("LeftToes")+P("RightToes")-P("LeftFoot")-P("RightFoot"))*.5f;
                            heading.Add(new JObject{["phase"]=frame["phase"].DeepClone(),["toe_forward"]=JArray.FromObject(V(toe)),["left_minus_right_upperleg"]=JArray.FromObject(V(P("LeftUpperLeg")-P("RightUpperLeg"))),["hips_to_head"]=JArray.FromObject(V(P("Head")-P("Hips")))});
                        }
                        result["target_direction_witnesses"]=heading;cases.Add(result);
                    }
                }
                Need(cases.Count==6,"Six source/look cases required.");receipt["status"]="captured_raw_sources_not_motion_accepted";
            }
            catch(Exception error){receipt["failure"]=error.ToString();throw;}
            finally
            {
                receipt["cases"]=cases;var after=Snapshot();var drift=before.Where(p=>!after.TryGetValue(p.Key,out var value)||value!=p.Value).Select(p=>p.Key).ToArray();var added=after.Keys.Except(before.Keys).ToArray();receipt["protected_drift"]=new JArray(drift);receipt["unexpected_added"]=new JArray(added);
                receipt["limits"]="Native synchronous Animator.Update comparison only, two warm cycles with real normalized-time assertions, exact adult world prefabs/own Avatars/all3 skins. One-state1x raw clips, FootIK OFF/layer IK OFF/root motion OFF, no run correction or root-Y lift. Same official canonical source reference as V4 plus measured Original+180 heading offset; no naturalness, real movement, transitions, contact, source heading, rendered animation or phone acceptance is inferred. Existing actual-source stride speed must be measured against 4.5m/s before tuning.";
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
