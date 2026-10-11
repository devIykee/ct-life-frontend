#if UNITY_EDITOR && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.App.Review
{
    /// <summary>Real player-loop candidate/control comparison. No Animator.Update or synthetic evaluation callback.</summary>
    public static class SnowRunTransitionReview
    {
        private const string ProfilePath="Assets/Art/Candidates/SnowRunRuntime20261007/SnowRunCorrection.asset";
        private const string RoomPath="Assets/Art/Candidates/PlayableVenues20261007/terminal_cafe.prefab";
        private static readonly HumanBodyBones[] Upper={HumanBodyBones.Hips,HumanBodyBones.Chest,HumanBodyBones.Head,HumanBodyBones.LeftHand,HumanBodyBones.RightHand};
        private static readonly HumanBodyBones[] Feet={HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot};
        private sealed class Case
        {
            public string name;public int frames,corrected,evaluations;public float minimumCandidate=999,minimumControl=999,maxUpperDelta,maxNoCorrectionSkinDelta,maxRootY;
            public float minWeight=2,maxWeight=-1,maxWeightSumError,maxSteadyAnkleXZ,maxSteadyFootRotation;public bool transition;public string failure;
            public List<string> captures=new List<string>();
            public List<object> samples=new List<object>();
        }
        public static async Task Run(string directory,CancellationToken owner)
        {
            directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
            var cases=new List<Case>();var checks=new List<string>();var errors=new List<string>();
            AvatarView control=null,candidate=null,rain=null;Camera camera=null;GameObject stage=null;
            var cameras=Camera.allCameras.Select(c=>(camera:c,enabled:c.enabled)).ToArray();
            var lodManagers=UnityEngine.Object.FindObjectsByType<AvatarLod>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .Select(manager=>(manager,enabled:manager.enabled)).ToArray();
            JObject stageProof=null;
            int previousRate=Application.targetFrameRate,previousSync=QualitySettings.vSyncCount;
            bool pass=false;string failure=null;var meshes=new List<Mesh>();
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(owner);timeout.CancelAfter(TimeSpan.FromSeconds(90));var ct=timeout.Token;
            try
            {
                Need(Resources.Load<SnowRunCorrectionProfile>(SnowRunCorrectionProfile.ResourcePath)==null,"Review requires unassigned candidate; a global profile already exists.");
                stageProof=ReadStageProof();
                var profile=AssetDatabase.LoadAssetAtPath<SnowRunCorrectionProfile>(ProfilePath);Need(profile!=null&&profile.Read(out _),"Exact staged profile missing.");
                foreach(var row in lodManagers)row.manager.enabled=false;
                checks.Add("Existing AvatarLod managers temporarily disabled for independent explicit culling controls; automatic manager integration is not certified.");
                var registry=CharacterRegistry.Instance;var snowEntry=registry.looks.Single(e=>e.prefab==profile.sourcePrefab);
                var rainEntry=registry.looks.Single(e=>e.key.Contains("head_rain"));
                Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
                double focusLimit=Time.realtimeSinceStartupAsDouble+30;
                while(!Application.isFocused){Need(Time.realtimeSinceStartupAsDouble<focusLimit,"Foreground focus was not observed.");await Awaitable.NextFrameAsync(ct);}
                foreach(var row in cameras)row.camera.enabled=false;
                stage=new GameObject("Snow run comparison — local synthetic review");
                camera=new GameObject("Snow run review camera").AddComponent<Camera>();camera.transform.SetParent(stage.transform);
                camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.72f,.77f,.82f);camera.nearClipPlane=.05f;camera.farClipPlane=60;camera.fieldOfView=35;
                var light=new GameObject("review light").AddComponent<Light>();light.transform.SetParent(stage.transform);light.type=LightType.Directional;light.intensity=1.1f;light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(35,-35,0);
                var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.name="review floor top Y0";ground.transform.SetParent(stage.transform);ground.transform.position=new Vector3(0,-.025f,0);ground.transform.localScale=new Vector3(40,.05f,40);ground.layer=30;
                control=AvatarFactory.Create("Snow uncorrected control",snowEntry.key.Split('|'),false,parent:stage.transform);
                candidate=AvatarFactory.Create("Snow six-leg candidate",snowEntry.key.Split('|'),false,parent:stage.transform);
                foreach(var t in control.GetComponentsInChildren<Transform>(true))t.gameObject.layer=29;
                foreach(var t in candidate.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
                control.Animator.cullingMode=candidate.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                Need(candidate.BindRunCorrection(profile,snowEntry.prefab),"Exact Snow binding refused.");
                var correction=candidate.Animator.GetComponent<SnowRunCorrection>();Need(correction!=null,"Correction absent.");
                var cs=control.GetComponentsInChildren<SkinnedMeshRenderer>(true);var ss=candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Need(cs.Length==3&&ss.Length==3,"Actual three-skin source changed.");
                var bakedA=new Mesh[3];var bakedB=new Mesh[3];
                for(int i=0;i<3;i++){Need(cs[i].sharedMesh==ss[i].sharedMesh,"Control and candidate skin identity differs.");bakedA[i]=new Mesh();bakedB[i]=new Mesh();meshes.Add(bakedA[i]);meshes.Add(bakedB[i]);}
                void Set(string state,float speed){control.SetSpeed(speed);candidate.SetSpeed(speed);control.SetBase(state);candidate.SetBase(state);}
                async Task Observe(string name,double duration,bool floorGate,bool noCorrection=false)
                {
                    var result=new Case{name=name};cases.Add(result);int priorApply=correction.ApplyCount,priorEval=correction.EvaluationCount;
                    double start=Time.realtimeSinceStartupAsDouble,deadline=start+duration+8;
                    do
                    {
                        await Awaitable.EndOfFrameAsync(ct);Need(Time.realtimeSinceStartupAsDouble<deadline,"Observation frame deadline: "+name);
                        var a=control.Animator.GetCurrentAnimatorStateInfo(0);var b=candidate.Animator.GetCurrentAnimatorStateInfo(0);
                        bool blend=candidate.Animator.IsInTransition(0);
                        Need(a.fullPathHash==b.fullPathHash&&control.Animator.IsInTransition(0)==blend&&Mathf.Abs(a.normalizedTime-b.normalizedTime)<1e-4f,"Paired native clocks differ: "+name);
                        float upper=0,minimumA=999,minimumB=999,allSkin=0;
                        if(!blend&&b.IsName("loco_run")&&correction.enabled&&correction.CurrentWeight>.999f)
                        {
                            foreach(var bone in Feet)
                            {
                                var ca=control.Animator.GetBoneTransform(bone);var cb=candidate.Animator.GetBoneTransform(bone);
                                Vector3 delta=candidate.transform.InverseTransformPoint(cb.position)-control.transform.InverseTransformPoint(ca.position);
                                result.maxSteadyAnkleXZ=Mathf.Max(result.maxSteadyAnkleXZ,new Vector2(delta.x,delta.z).magnitude);
                                result.maxSteadyFootRotation=Mathf.Max(result.maxSteadyFootRotation,Quaternion.Angle(ca.rotation,cb.rotation));
                            }
                        }
                        foreach(var bone in Upper)upper=Mathf.Max(upper,Vector3.Distance(control.transform.InverseTransformPoint(control.Animator.GetBoneTransform(bone).position),candidate.transform.InverseTransformPoint(candidate.Animator.GetBoneTransform(bone).position)));
                        for(int tier=0;tier<3;tier++)
                        {
                            cs[tier].BakeMesh(bakedA[tier]);ss[tier].BakeMesh(bakedB[tier]);var va=bakedA[tier].vertices;var vb=bakedB[tier].vertices;
                            Need(va.Length==vb.Length,"Native skin topology differs.");
                            for(int v=0;v<va.Length;v++)
                            {
                                Vector3 x=control.transform.InverseTransformPoint(cs[tier].transform.TransformPoint(va[v])),y=candidate.transform.InverseTransformPoint(ss[tier].transform.TransformPoint(vb[v]));
                                minimumA=Mathf.Min(minimumA,x.y);minimumB=Mathf.Min(minimumB,y.y);if(noCorrection)allSkin=Mathf.Max(allSkin,Vector3.Distance(x,y));
                            }
                        }
                        result.frames++;result.transition|=blend;result.minimumCandidate=Mathf.Min(result.minimumCandidate,minimumB);result.minimumControl=Mathf.Min(result.minimumControl,minimumA);
                        result.maxUpperDelta=Mathf.Max(result.maxUpperDelta,upper);result.maxNoCorrectionSkinDelta=Mathf.Max(result.maxNoCorrectionSkinDelta,allSkin);
                        result.maxRootY=Mathf.Max(result.maxRootY,Mathf.Abs(control.transform.position.y),Mathf.Abs(candidate.transform.position.y));
                        float weight=correction.CurrentWeight+correction.NextWeight;result.minWeight=Mathf.Min(result.minWeight,weight);result.maxWeight=Mathf.Max(result.maxWeight,weight);
                        if(weight>0)result.maxWeightSumError=Mathf.Max(result.maxWeightSumError,Mathf.Abs(correction.TotalReportedWeight-1));
                        result.samples.Add(new{frame=Time.frameCount,phase=b.normalizedTime,state=b.shortNameHash,next=blend?candidate.Animator.GetNextAnimatorStateInfo(0).shortNameHash:0,blend,
                            weightCurrent=correction.CurrentWeight,weightNext=correction.NextWeight,reportedWeight=correction.TotalReportedWeight,evaluations=correction.EvaluationCount,applies=correction.ApplyCount,upper,minimumA,minimumB,allSkin,focused=Application.isFocused});
                        // Preserve the complete diagnostic interval; evaluate its gates below rather than aborting at its first frame.
                        camera.transform.position=candidate.PresentationPosition+new Vector3(3.4f,1.5f,-4);camera.transform.LookAt(candidate.PresentationPosition+Vector3.up*.85f);
                    }while(Time.realtimeSinceStartupAsDouble-start<duration||result.frames<3);
                    result.corrected=correction.ApplyCount-priorApply;result.evaluations=correction.EvaluationCount-priorEval;
                    if(correction.Failure!=null)result.failure=correction.Failure;
                    else if(result.maxSteadyAnkleXZ>.002f||result.maxSteadyFootRotation>1f)result.failure="Steady run changed accepted ankle XZ/foot orientation bounds.";
                    else if(result.maxUpperDelta>.0002f)result.failure="Upper body changed.";
                    else if(result.maxRootY>.00001f)result.failure="Actor root Y changed.";
                    else if(noCorrection&&result.maxNoCorrectionSkinDelta>.0005f)result.failure="Disabled or non-run poses differ.";
                    else if(floorGate&&result.minimumCandidate<-.002f)result.failure="Actual all-LOD surface crossed floor by over 2mm.";
                    if(name=="idle-to-run"||name=="steady-run"||name=="run-to-idle"||name=="interrupt-return-run"||name=="correction-reenabled"||name=="fitted-controller-run")
                    {
                        var image=ScreenCapture.CaptureScreenshotAsTexture();Need(image!=null,"Actual frame capture failed.");
                        string path=name+"-frame-"+Time.frameCount+".png";
                        try{File.WriteAllBytes(Path.Combine(directory,path),image.EncodeToPNG());result.captures.Add(path);}finally{UnityEngine.Object.Destroy(image);}
                    }
                    File.WriteAllText(Path.Combine(directory,"partial-receipt.json"),JObject.FromObject(new{status="in_progress_not_accepted",cases,checks}).ToString());
                }
                Set("loco_idle",0);await Observe("initial-idle",.3,false,true);
                Set("loco_run",4.5f);await Observe("idle-to-run",.3,true);await Observe("steady-run",1.4,true);
                Need(cases.Last().corrected>3&&cases.Last().evaluations>=cases.Last().corrected,"No actual evaluated-frame correction.");
                Set("loco_walk",2);await Observe("run-to-walk",.35,true);await Observe("steady-walk",.3,false,true);
                Set("loco_run",4.5f);await Observe("walk-to-run",.35,true);
                Set("loco_idle",0);await Observe("run-to-idle",.35,true);await Observe("settled-idle",.3,false,true);
                Set("loco_run",4.5f);await Observe("interrupt-enter-run",.08,true);
                Set("loco_walk",2);await Observe("interrupt-return-walk",.06,true);
                Set("loco_run",4.5f);await Observe("interrupt-return-run",.45,true);
                control.PlayOneShot("emote_wave");candidate.PlayOneShot("emote_wave");await Observe("run-to-emote",.35,false);
                await Observe("emote-return-to-run",2.2,false);
                Set("loco_run",4.5f);await Observe("pre-disable-run",.35,true);
                correction.enabled=false;await Observe("correction-disabled",.35,false,true);
                correction.enabled=true;await Observe("correction-reenabled",.7,true);
                control.Animator.enabled=candidate.Animator.enabled=false;int stopped=correction.ApplyCount;
                await Observe("animator-disabled",.2,false,true);Need(correction.ApplyCount==stopped,"Unevaluated Animator accumulated correction.");
                control.Animator.enabled=candidate.Animator.enabled=true;await Observe("animator-reenabled",.7,true);
                control.ApplyLod(0,true,1);candidate.ApplyLod(0,true,1);stopped=correction.ApplyCount;
                await Observe("explicit-cull",.2,false,true);Need(correction.ApplyCount==stopped,"Culled animation accumulated correction.");
                control.ApplyLod(0,false,1);candidate.ApplyLod(0,false,1);
                control.Animator.cullingMode=candidate.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;await Observe("explicit-cull-resume",.7,true);
                // Production CullCompletely with actual renderer invisibility, independent of AvatarView.Culled.
                control.Animator.cullingMode=candidate.Animator.cullingMode=AnimatorCullingMode.CullCompletely;
                foreach(var skin in cs)skin.enabled=false;foreach(var skin in ss)skin.enabled=false;
                await Observe("renderer-cull",.2,false,true);stopped=correction.ApplyCount;await Observe("renderer-cull-held",.2,false,true);
                Need(correction.ApplyCount==stopped,"Renderer-culled pose accumulated correction.");
                foreach(var skin in cs)skin.enabled=true;foreach(var skin in ss)skin.enabled=true;
                control.Animator.cullingMode=candidate.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;await Observe("renderer-cull-resume",.7,true);
                Set("loco_idle",0);await Observe("final-idle",.35,true);await Observe("final-idle-held",.2,false,true);
                // Exercise the exact persistent fitted controller and actual exit owner without claiming furniture traversal here.
                var seatProfile=AssetDatabase.LoadAssetAtPath<GameObject>(RoomPath).GetComponent<CafeSeatMotionProfile>();
                control.BindSeatMotionProfile(seatProfile);candidate.BindSeatMotionProfile(seatProfile);
                Set("loco_run",4.5f);await Observe("fitted-controller-run",.8,true);
                control.SetBase("act_type","terminal_1");candidate.SetBase("act_type","terminal_1");await Observe("fitted-seat-entry",.8,false);
                stopped=correction.ApplyCount;
                control.transform.position=candidate.transform.position=new Vector3(-6.15f,0,2.9f);
                Need(control.BeginSeatExitPresentation("terminal_1",new Vector3(-7.5f,0,3),Quaternion.identity)&&candidate.BeginSeatExitPresentation("terminal_1",new Vector3(-7.5f,0,3),Quaternion.identity),"Actual seat-exit owner did not begin.");
                Set("loco_idle",0);double exitLimit=Time.realtimeSinceStartupAsDouble+12;
                do{await Observe("seat-exit-owned",.2,false);Need(correction.ApplyCount==stopped,"Ordinary run correction touched seat exit.");Need(Time.realtimeSinceStartupAsDouble<exitLimit,"Seat exit did not complete.");}while(candidate.SeatExitActive||control.SeatExitActive);
                checks.Add("Actual persistent fitted controller and SeatExitActive owner exclude ordinary correction.");
                rain=AvatarFactory.Create("Rain negative control",rainEntry.key.Split('|'),false,parent:stage.transform);
                Need(!rain.BindRunCorrection(profile,rainEntry.prefab)&&rain.Animator.GetComponent<SnowRunCorrection>()==null,"Rain accepted Snow correction.");
                UnityEngine.Object.Destroy(candidate.gameObject);candidate=null;await Awaitable.NextFrameAsync(ct);await Awaitable.NextFrameAsync(ct);
                Need(correction==null,"Destroyed look retained correction component.");
                checks.Add("Correction disable/re-enable, Animator disable/re-enable, explicit/invisible renderer culling, Snow destruction and Rain binding rejection controls completed. Actual live look replacement is not exercised.");
                Need(cases.All(c=>c.failure==null),"One or more actual transition/contact cases failed; read complete per-case diagnostics.");
                pass=true;
            }
            catch(Exception error){failure=error.ToString();throw;}
            finally
            {
                try{if(stage!=null)UnityEngine.Object.Destroy(stage);foreach(var mesh in meshes)if(mesh!=null)UnityEngine.Object.Destroy(mesh);}catch(Exception error){errors.Add(error.ToString());}
                foreach(var row in cameras)if(row.camera!=null)row.camera.enabled=row.enabled;
                foreach(var row in lodManagers)if(row.manager!=null)row.manager.enabled=row.enabled;
                Application.targetFrameRate=previousRate;QualitySettings.vSyncCount=previousSync;
                File.WriteAllText(Path.Combine(directory,"receipt.json"),JObject.FromObject(new{pass=pass&&errors.Count==0,failure,cases,checks,cleanupErrors=errors,stageProof,
                    helperSha256=Hash("Assets/Scripts/App/Review/SnowRunTransitionReview.cs"),
                    runtimeHashes=new[]{"Assets/Scripts/World/Avatars/AvatarView.cs","Assets/Scripts/World/Avatars/AvatarFactory.cs","Assets/Scripts/World/Avatars/SnowRunCorrection.cs","Assets/Scripts/World/Avatars/SnowRunCorrectionProfile.cs"}.ToDictionary(path=>path,Hash),
                    scope="Actual AvatarFactory/AvatarView player-loop candidate/control; no Animator.Update or spoofed callbacks. Pose/transition/explicit culling ownership candidate only. Six selected actual rendered frames require independent visual review. Existing AvatarLod managers are disabled and restored. Not live look replacement, automatic LOD scheduling, application pause/focus lifecycle, Local/Remote transport, furniture clearance, ground-slope alignment, cadence/foley, rendered moving stride feel or Android performance acceptance. Rain unchanged."}).ToString());
            }
        }
        private static JObject ReadStageProof()
        {
            string path=Path.GetDirectoryName(ProfilePath)+"/StageReceipt.json";
            Need(File.Exists(path),"Candidate stage receipt is missing.");
            var receipt=JObject.Parse(File.ReadAllText(path));
            Need((string)receipt["status"]=="staged_unassigned_runtime_transitions_pending"&&
                (bool?)receipt["resources_assignment"]==false&&
                (string)receipt["profile_path"]==ProfilePath&&
                (string)receipt["profile_sha256"]==Hash(ProfilePath)&&
                (string)receipt["table_sha256"]==SnowRunCorrectionProfile.AcceptedTableSha256,"Candidate stage identity differs.");
            var dependencies=(JObject)receipt["source_dependencies"];
            Need(dependencies!=null&&dependencies.Count>0,"Stage source dependencies absent.");
            foreach(var dependency in dependencies.Properties())
                Need(File.Exists(dependency.Name)&&Hash(dependency.Name)==(string)dependency.Value,"Stage source dependency differs: "+dependency.Name);
            return new JObject{["path"]=path,["sha256"]=Hash(path),["profile_sha256"]=Hash(ProfilePath),["source_dependencies"]=dependencies.DeepClone()};
        }
        private static string Hash(string path)
        {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        private static void Need(bool value,string reason){if(!value)throw new InvalidOperationException("Snow run review: "+reason);}
    }
}
#endif
