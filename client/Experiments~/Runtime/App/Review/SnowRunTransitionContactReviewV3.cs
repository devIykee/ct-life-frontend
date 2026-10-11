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
using UnityEngine.Rendering;

namespace CTLife.App.Review
{
    /// <summary>Real player-loop candidate/control comparison. No Animator.Update or synthetic evaluation callback.</summary>
    public static class SnowRunTransitionContactReviewV3
    {
        private const string ProfilePath="Assets/Art/Candidates/SnowRunRuntime20261007/SnowRunCorrection.asset";
        private const string RoomPath="Assets/Art/Candidates/PlayableVenues20261007/terminal_cafe.prefab";
        private static readonly HumanBodyBones[] Upper={HumanBodyBones.Hips,HumanBodyBones.Chest,HumanBodyBones.Head,HumanBodyBones.LeftHand,HumanBodyBones.RightHand};
        private static readonly HumanBodyBones[] Legs={HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot};
        private static readonly HumanBodyBones[] Feet={HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot};
        private static readonly HumanBodyBones[] WitnessBones={HumanBodyBones.Hips,HumanBodyBones.Chest,HumanBodyBones.Head,
            HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot,HumanBodyBones.RightToes};
        private sealed class Case
        {
            public string name;public int frames,corrected,evaluations;public float minimumCandidate=999,minimumControl=999,maxUpperDelta,maxNoCorrectionSkinDelta,maxRootY;
            public float minWeight=2,maxWeight=-1,maxWeightSumError,maxSteadyAnkleXZ,maxSteadyFootRotation;public bool transition;public string failure;
            public float maxEvaluatorError,maxTransitionAnkleXZ,maxTransitionRotation,maxTransitionLengthError,maxTransitionAdditionalFlexion,maxTransitionLift,maxCorrectionStepDegrees;
            public int requiredContactSamples,ungatedPureEmoteSamples;public float minimumRequiredContact=999,minimumPureEmote=999;
            public double maxContactMs;public List<object> poseCaptures=new List<object>();
            public List<string> captures=new List<string>();
            public List<string> nativePoseExports=new List<string>();
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
            JObject stageProof=null,startBarrier=null;
            int previousRate=Application.targetFrameRate,previousSync=QualitySettings.vSyncCount;
            bool pass=false;string failure=null;var meshes=new List<Mesh>();
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(owner);var ct=timeout.Token;
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
                if(CTLife.Core.LaunchArgs.TryGet("reviewStartFile",out var startFile))
                {
                    startFile=Path.GetFullPath(startFile);Need(!File.Exists(startFile),"Start signal already exists; use a fresh path.");
                    using var barrierOwner=CancellationTokenSource.CreateLinkedTokenSource(owner);barrierOwner.CancelAfter(TimeSpan.FromSeconds(180));
                    startBarrier=new JObject{["status"]="waiting",["file"]=startFile,["ready_frame"]=Time.frameCount,
                        ["ready_utc"]=DateTime.UtcNow.ToString("O"),["ready_focused"]=Application.isFocused,["maximum_wait_seconds"]=180};
                    File.WriteAllText(Path.Combine(directory,"ready-for-start.json"),startBarrier.ToString());
                    while(!File.Exists(startFile))await Awaitable.NextFrameAsync(barrierOwner.Token);
                    startBarrier["status"]="released";startBarrier["released_frame"]=Time.frameCount;
                    startBarrier["released_utc"]=DateTime.UtcNow.ToString("O");startBarrier["released_focused"]=Application.isFocused;
                }
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
                var footVertices=FootVertices(control.Animator,cs);
                for(int i=0;i<3;i++){Need(cs[i].sharedMesh==ss[i].sharedMesh,"Control and candidate skin identity differs.");bakedA[i]=new Mesh();bakedB[i]=new Mesh();meshes.Add(bakedA[i]);meshes.Add(bakedB[i]);}
                void Set(string state,float speed){control.SetSpeed(speed);candidate.SetSpeed(speed);control.SetBase(state);candidate.SetBase(state);}
                async Task Observe(string name,double duration,bool floorGate,bool noCorrection=false)
                {
                    var result=new Case{name=name};cases.Add(result);Quaternion[] previousDeltas=null;int capturedTransitions=0;int priorApply=correction.ApplyCount,priorEval=correction.EvaluationCount;
                    double start=Time.realtimeSinceStartupAsDouble,deadline=start+duration+8;
                    do
                    {
                        await Awaitable.EndOfFrameAsync(ct);Need(Time.realtimeSinceStartupAsDouble<deadline,"Observation frame deadline: "+name);
                        long measurementStart=System.Diagnostics.Stopwatch.GetTimestamp();
                        var a=control.Animator.GetCurrentAnimatorStateInfo(0);var b=candidate.Animator.GetCurrentAnimatorStateInfo(0);
                        bool blend=candidate.Animator.IsInTransition(0);
                        Need(a.fullPathHash==b.fullPathHash&&control.Animator.IsInTransition(0)==blend&&Mathf.Abs(a.normalizedTime-b.normalizedTime)<1e-4f,"Paired native clocks differ: "+name);
                        float upper=0,minimumA=999,minimumB=999,allSkin=0;
                        var footA=new[]{999f,999f};var footB=new[]{999f,999f};
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
                                for(int side=0;side<2;side++)if(footVertices[tier][side].Contains(v))
                                {footA[side]=Mathf.Min(footA[side],x.y);footB[side]=Mathf.Min(footB[side],y.y);}
                            }
                        }
                        // Native all-LOD BakeMesh is independent of the cached runtime linear evaluator.
                        for(int side=0;side<2;side++)result.maxEvaluatorError=Mathf.Max(result.maxEvaluatorError,Mathf.Abs(correction.EvaluatedSole(side)-footB[side]));
                        if(correction.TransitionAppliedThisFrame)
                        {
                            var deltas=new Quaternion[6];
                            for(int side=0;side<2;side++)
                            {
                                var hipBone=side==0?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg;
                                var kneeBone=side==0?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg;
                                var ca=control.Animator.GetBoneTransform(hipBone);var cb=candidate.Animator.GetBoneTransform(hipBone);
                                var ka=control.Animator.GetBoneTransform(kneeBone);var kb=candidate.Animator.GetBoneTransform(kneeBone);
                                var fa=control.Animator.GetBoneTransform(Feet[side]);var fb=candidate.Animator.GetBoneTransform(Feet[side]);
                                Vector3 delta=candidate.transform.InverseTransformPoint(fb.position)-control.transform.InverseTransformPoint(fa.position);
                                result.maxTransitionAnkleXZ=Mathf.Max(result.maxTransitionAnkleXZ,new Vector2(delta.x,delta.z).magnitude);
                                result.maxTransitionRotation=Mathf.Max(result.maxTransitionRotation,Quaternion.Angle(fa.rotation,fb.rotation));
                                result.maxTransitionLengthError=Mathf.Max(result.maxTransitionLengthError,
                                    Mathf.Abs(Vector3.Distance(ca.position,ka.position)-Vector3.Distance(cb.position,kb.position)),
                                    Mathf.Abs(Vector3.Distance(ka.position,fa.position)-Vector3.Distance(kb.position,fb.position)));
                                float extra=Vector3.Angle(ca.position-ka.position,fa.position-ka.position)-Vector3.Angle(cb.position-kb.position,fb.position-kb.position);
                                result.maxTransitionAdditionalFlexion=Mathf.Max(result.maxTransitionAdditionalFlexion,extra);
                                result.maxTransitionLift=Mathf.Max(result.maxTransitionLift,delta.y);
                            }
                            for(int bone=0;bone<6;bone++)
                            {
                                deltas[bone]=candidate.Animator.GetBoneTransform(Legs[bone]).localRotation*Quaternion.Inverse(control.Animator.GetBoneTransform(Legs[bone]).localRotation);
                                if(previousDeltas!=null)result.maxCorrectionStepDegrees=Mathf.Max(result.maxCorrectionStepDegrees,Quaternion.Angle(previousDeltas[bone],deltas[bone]));
                            }
                            previousDeltas=deltas;
                        }
                        int runHash=Animator.StringToHash("loco_run"),waveHash=Animator.StringToHash("emote_wave");
                        var nextState=blend?candidate.Animator.GetNextAnimatorStateInfo(0):default;
                        bool involvesRun=b.shortNameHash==runHash||(blend&&nextState.shortNameHash==runHash);
                        bool pureEmote=!involvesRun&&(b.shortNameHash==waveHash||(blend&&nextState.shortNameHash==waveHash));
                        bool contactRequired=floorGate&&!pureEmote;
                        if(contactRequired){result.requiredContactSamples++;result.minimumRequiredContact=Mathf.Min(result.minimumRequiredContact,minimumB);}
                        if(pureEmote){result.ungatedPureEmoteSamples++;result.minimumPureEmote=Mathf.Min(result.minimumPureEmote,minimumB);}
                        result.maxContactMs=Math.Max(result.maxContactMs,correction.LastContactMilliseconds);
                        // Bound graphics cost: the first actual blend per case, plus two separated steady phases.
                        bool selectedPose=(blend&&involvesRun&&capturedTransitions==0)||
                            (name=="steady-run"&&(result.frames==0||(result.frames>=4&&capturedTransitions==1)));
                        if(selectedPose)
                        {
                            capturedTransitions++;
                            string prefix=name+"-frame-"+Time.frameCount;
                            result.poseCaptures.Add(CapturePosePair(camera,control,candidate,cs,ss,bakedA,bakedB,directory,prefix));
                        }
                        if(blend&&minimumB<-.002f&&result.nativePoseExports.Count==0)
                        {
                            for(int tier=0;tier<3;tier++)
                            {
                                string controlPath=name+"-control-frame-"+Time.frameCount+"-tier"+tier+".obj";
                                string candidatePath=name+"-candidate-frame-"+Time.frameCount+"-tier"+tier+".obj";
                                ExportObj(Path.Combine(directory,controlPath),bakedA[tier],cs[tier].transform,control.transform);
                                ExportObj(Path.Combine(directory,candidatePath),bakedB[tier],ss[tier].transform,candidate.transform);
                                result.nativePoseExports.Add(controlPath);result.nativePoseExports.Add(candidatePath);
                            }
                            var failureImage=ScreenCapture.CaptureScreenshotAsTexture();Need(failureImage!=null,"Actual failing-frame capture failed.");
                            string failureImagePath=name+"-first-floor-failure-frame-"+Time.frameCount+".png";
                            try{File.WriteAllBytes(Path.Combine(directory,failureImagePath),failureImage.EncodeToPNG());result.captures.Add(failureImagePath);}
                            finally{UnityEngine.Object.Destroy(failureImage);}
                        }
                        result.frames++;result.transition|=blend;result.minimumCandidate=Mathf.Min(result.minimumCandidate,minimumB);result.minimumControl=Mathf.Min(result.minimumControl,minimumA);
                        result.maxUpperDelta=Mathf.Max(result.maxUpperDelta,upper);result.maxNoCorrectionSkinDelta=Mathf.Max(result.maxNoCorrectionSkinDelta,allSkin);
                        result.maxRootY=Mathf.Max(result.maxRootY,Mathf.Abs(control.transform.position.y),Mathf.Abs(candidate.transform.position.y));
                        float weight=correction.CurrentWeight+correction.NextWeight;result.minWeight=Mathf.Min(result.minWeight,weight);result.maxWeight=Mathf.Max(result.maxWeight,weight);
                        if(weight>0)result.maxWeightSumError=Mathf.Max(result.maxWeightSumError,Mathf.Abs(correction.TotalReportedWeight-1));
                        result.samples.Add(new{frame=Time.frameCount,phase=b.normalizedTime,state=b.shortNameHash,next=blend?candidate.Animator.GetNextAnimatorStateInfo(0).shortNameHash:0,nextPhase=blend?candidate.Animator.GetNextAnimatorStateInfo(0).normalizedTime:0,blend,
                            weightCurrent=correction.CurrentWeight,weightNext=correction.NextWeight,reportedWeight=correction.TotalReportedWeight,evaluations=correction.EvaluationCount,applies=correction.ApplyCount,upper,minimumA,minimumB,allSkin,focused=Application.isFocused,editorFocusedWindow=EditorWindow.focusedWindow!=null?EditorWindow.focusedWindow.GetType().FullName:null,
                            cameraEnabled=camera.enabled,animatorEnabled=candidate.Animator.enabled,cullingMode=candidate.Animator.cullingMode.ToString(),footA,footB,
                            controlPose=PoseWitness(control),candidatePose=PoseWitness(candidate),deltaTime=Time.deltaTime,unscaledDeltaTime=Time.unscaledDeltaTime,
                            contactRequired,pureEmote,transitionApplied=correction.TransitionAppliedThisFrame,contactMs=correction.LastContactMilliseconds,
                            evaluatedSoles=new[]{correction.EvaluatedSole(0),correction.EvaluatedSole(1)},
                            measurementWorkMs=(System.Diagnostics.Stopwatch.GetTimestamp()-measurementStart)*1000d/System.Diagnostics.Stopwatch.Frequency});
                        // Preserve the complete diagnostic interval; evaluate its gates below rather than aborting at its first frame.
                        camera.transform.position=candidate.PresentationPosition+new Vector3(3.4f,1.5f,-4);camera.transform.LookAt(candidate.PresentationPosition+Vector3.up*.85f);
                    }while(Time.realtimeSinceStartupAsDouble-start<duration||result.frames<3);
                    result.corrected=correction.ApplyCount-priorApply;result.evaluations=correction.EvaluationCount-priorEval;
                    if(correction.Failure!=null)result.failure=correction.Failure;
                    else if(result.maxSteadyAnkleXZ>.002f||result.maxSteadyFootRotation>1f)result.failure="Steady run changed accepted ankle XZ/foot orientation bounds.";
                    else if(result.maxEvaluatorError>.0001f)result.failure="Cached evaluator differs from independent native all-LOD foot surface by over 0.1mm.";
                    else if(result.maxTransitionAnkleXZ>.0005f||result.maxTransitionRotation>.1f||result.maxTransitionLengthError>.0001f||result.maxTransitionAdditionalFlexion>30.001f||result.maxTransitionLift>.08001f)result.failure="NEW transition-only anatomical/contact envelope exceeded.";
                    else if(result.maxUpperDelta>.0002f)result.failure="Upper body changed.";
                    else if(result.maxRootY>.00001f)result.failure="Actor root Y changed.";
                    else if(noCorrection&&result.maxNoCorrectionSkinDelta>.0005f)result.failure="Disabled or non-run poses differ.";
                    else if(result.requiredContactSamples>0&&result.minimumRequiredContact<-.002f)result.failure="Actual all-LOD surface crossed floor by over 2mm.";
                    if(name=="idle-to-run"||name=="steady-run"||name=="run-to-idle"||name=="interrupt-return-run"||name=="correction-reenabled"||name=="fitted-controller-run")
                    {
                        var image=ScreenCapture.CaptureScreenshotAsTexture();Need(image!=null,"Actual frame capture failed.");
                        string path=name+"-frame-"+Time.frameCount+".png";
                        try{File.WriteAllBytes(Path.Combine(directory,path),image.EncodeToPNG());result.captures.Add(path);}finally{UnityEngine.Object.Destroy(image);}
                    }
                    File.WriteAllText(Path.Combine(directory,"partial-receipt.json"),JObject.FromObject(new{status="in_progress_not_accepted",cases,checks}).ToString());
                }
                timeout.CancelAfter(TimeSpan.FromSeconds(90));
                Set("loco_idle",0);await Observe("initial-idle",.3,true,true);
                Set("loco_walk",2);await Observe("idle-to-walk",.3,true,true);
                Set("loco_idle",0);await Observe("walk-to-idle",.3,true,true);
                Set("loco_run",4.5f);await Observe("idle-to-run",.3,true);await Observe("steady-run",1.4,true);
                Need(cases.Last().corrected>3&&cases.Last().evaluations>=cases.Last().corrected,"No actual evaluated-frame correction.");
                Set("loco_walk",2);await Observe("run-to-walk",.35,true);await Observe("steady-walk",.3,false,true);
                Set("loco_run",4.5f);await Observe("walk-to-run",.35,true);
                Set("loco_idle",0);await Observe("run-to-idle",.35,true);await Observe("settled-idle",.3,false,true);
                Set("loco_run",4.5f);await Observe("interrupt-enter-run",.08,true);
                Set("loco_walk",2);await Observe("interrupt-return-walk",.06,true);
                Set("loco_run",4.5f);await Observe("interrupt-return-run",.45,true);
                control.PlayOneShot("emote_wave");candidate.PlayOneShot("emote_wave");await Observe("run-to-emote",.35,true);
                await Observe("emote-return-to-run",2.2,true);
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
                rain=AvatarFactory.Create("Rain negative control",rainEntry.key.Split('|'),false,parent:stage.transform);
                Need(!rain.BindRunCorrection(profile,rainEntry.prefab)&&rain.Animator.GetComponent<SnowRunCorrection>()==null,"Rain accepted Snow correction.");
                checks.Add("Rain exact-prefab rejection observed before seat-owner test.");
                var roomOwner=new GameObject("Owned profile fixture container");roomOwner.transform.SetParent(stage.transform);roomOwner.SetActive(false);
                var room=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RoomPath),roomOwner.transform);
                room.name="Exact saved Cafe — owned active profile fixture";room.SetActive(true);
                // Keep the inactive clone's lighting callbacks from touching the current venue table.
                foreach(var maps in room.GetComponentsInChildren<VenueLightmaps>(true))maps.enabled=false;
                foreach(var lightSource in room.GetComponentsInChildren<Light>(true))lightSource.enabled=false;
                // Profile authority depends on the real active instance and initialized immutable anchors.
                // Rendering/physics of this owned clone is excluded; this is an owner test, not furniture traversal.
                foreach(var renderer in room.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
                foreach(var collider in room.GetComponentsInChildren<Collider>(true))collider.enabled=false;
                var venue=room.GetComponent<VenueSceneRoot>();Need(venue!=null&&!venue.IsGreybox,"Exact saved room root absent.");venue.Init(null);
                roomOwner.SetActive(true);
                var seatProfile=room.GetComponent<CafeSeatMotionProfile>();Need(seatProfile!=null&&seatProfile.isActiveAndEnabled,"Owned profile is not active.");
                control.BindSeatMotionProfile(seatProfile);candidate.BindSeatMotionProfile(seatProfile);
                var fittedController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Art/Candidates/PlayableVenues20261007/Controllers/snow.controller");
                Need(control.Animator.runtimeAnimatorController==fittedController&&candidate.Animator.runtimeAnimatorController==fittedController,
                    "Fitted controller was not actually bound; no fitted claim is valid.");
                checks.Add("Both exact Snow actors use the saved fitted controller from an active initialized room instance.");
                Set("loco_run",4.5f);await Observe("fitted-controller-run",.8,true);
                control.SetBase("act_type","terminal_1");candidate.SetBase("act_type","terminal_1");await Observe("fitted-seat-entry",.8,false);
                Need(!candidate.Animator.IsInTransition(0)&&candidate.Animator.GetCurrentAnimatorStateInfo(0).IsName(CafeSeatMotionProfile.TypingState)&&
                    !control.Animator.IsInTransition(0)&&control.Animator.GetCurrentAnimatorStateInfo(0).IsName(CafeSeatMotionProfile.TypingState),
                    "Fitted typing state did not actually evaluate.");
                stopped=correction.ApplyCount;
                Vector3 approach=default;
                Need(venue.TryGetSpot("terminal_1",out var seat,out var seatYaw)&&venue.TryGetSpotApproach("terminal_1",out approach),"Exact room anchors missing.");
                control.transform.position=candidate.transform.position=approach;
                var seatedRotation=Quaternion.Euler(0,seatYaw*Mathf.Rad2Deg,0);
                Need(control.BeginSeatExitPresentation("terminal_1",seat,seatedRotation)&&candidate.BeginSeatExitPresentation("terminal_1",seat,seatedRotation),"Actual seat-exit owner did not begin.");
                Need(control.SeatExitActive&&candidate.SeatExitActive,"Seat-exit owner did not become active.");
                Set("loco_idle",0);double exitLimit=Time.realtimeSinceStartupAsDouble+12;
                do{await Observe("seat-exit-owned",.2,false);Need(correction.ApplyCount==stopped,"Ordinary run correction touched seat exit.");Need(Time.realtimeSinceStartupAsDouble<exitLimit,"Seat exit did not complete.");}while(candidate.SeatExitActive||control.SeatExitActive);
                checks.Add("Actual persistent fitted controller and SeatExitActive owner exclude ordinary correction.");
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
                File.WriteAllText(Path.Combine(directory,"receipt.json"),JObject.FromObject(new{pass=pass&&errors.Count==0,motionAccepted=false,visualAcceptance=false,movingPlayerAcceptance=false,failure,cases,checks,cleanupErrors=errors,stageProof,startBarrier,
                    helperSha256=Hash("Assets/Scripts/App/Review/SnowRunTransitionContactReviewV3.cs"),
                    revision="snow-transition-contact-v3-new-30deg-80mm-envelope",
                    pureEmoteRemainingNegative=cases.Any(c=>c.ungatedPureEmoteSamples>0&&c.minimumPureEmote<-.002f),
                    runtimeHashes=new[]{"Assets/Scripts/World/Avatars/AvatarView.cs","Assets/Scripts/World/Avatars/AvatarFactory.cs","Assets/Scripts/World/Avatars/SnowRunCorrection.cs","Assets/Scripts/World/Avatars/SnowRunCorrectionProfile.cs","Assets/Scripts/World/Avatars/SnowTransitionContact.cs"}.ToDictionary(path=>path,Hash),
                    scope="Actual AvatarFactory/AvatarView player-loop candidate/control; no Animator.Update or spoofed callbacks. Pose/transition/explicit culling ownership candidate only. Same-frame native CPU pose front/side pairs require independent visual review. NEW run-boundary envelope is 30 degrees additional knee flexion/80mm ankle lift; original steady table is unchanged. Pure emote poses are separately recorded as remaining ungated negatives, not accepted motion. Cached skin evaluation is checked against independent native BakeMesh; continuity and moving stride are not certified. Existing AvatarLod managers are disabled and restored. Not live look replacement, automatic LOD scheduling, application pause/focus lifecycle, Local/Remote transport, furniture clearance, ground-slope alignment, cadence/foley, rendered moving stride feel or Android performance acceptance. Rain unchanged."}).ToString());
            }
        }
        private static object CapturePosePair(Camera camera,AvatarView control,AvatarView candidate,
            SkinnedMeshRenderer[] controlSkins,SkinnedMeshRenderer[] candidateSkins,Mesh[] controlMeshes,Mesh[] candidateMeshes,string directory,string prefix)
        {
            var rows=new List<object>();var oldPosition=camera.transform.position;var oldRotation=camera.transform.rotation;int oldMask=camera.cullingMask;
            try
            {
                for(int which=0;which<2;which++)
                {
                    var actor=which==0?control:candidate;var skins=which==0?controlSkins:candidateSkins;var baked=which==0?controlMeshes:candidateMeshes;
                    var groups=actor.GetComponentsInChildren<LODGroup>(true);
                    var lod0=groups.SelectMany(g=>g.GetLODs().Take(1)).SelectMany(l=>l.renderers).OfType<SkinnedMeshRenderer>().Distinct().ToArray();
                    Need(lod0.Length==1,"Exact LOD0 skin required for current-pose snapshot.");int tier=Array.IndexOf(skins,lod0[0]);Need(tier>=0,"LOD0 absent from measured skins.");
                    var enabled=skins.Select(s=>s.enabled).ToArray();var groupEnabled=groups.Select(g=>g.enabled).ToArray();GameObject snapshot=null;Mesh snapshotMesh=null;
                    try
                    {
                        snapshot=new GameObject("Same native evaluated pose snapshot");snapshot.transform.SetParent(skins[tier].transform,false);snapshot.layer=30;
                        snapshotMesh=UnityEngine.Object.Instantiate(baked[tier]);snapshot.AddComponent<MeshFilter>().sharedMesh=snapshotMesh;var renderer=snapshot.AddComponent<MeshRenderer>();renderer.sharedMaterials=skins[tier].sharedMaterials;
                        renderer.shadowCastingMode=skins[tier].shadowCastingMode;renderer.receiveShadows=skins[tier].receiveShadows;renderer.renderingLayerMask=skins[tier].renderingLayerMask;
                        var block=new MaterialPropertyBlock();skins[tier].GetPropertyBlock(block);renderer.SetPropertyBlock(block);
                        for(int i=0;i<renderer.sharedMaterials.Length;i++){block.Clear();skins[tier].GetPropertyBlock(block,i);if(!block.isEmpty)renderer.SetPropertyBlock(block,i);}
                        foreach(var group in groups)group.enabled=false;foreach(var skin in skins)skin.enabled=false;
                        // Hide the other actor too; its native pose stays unchanged.
                        var other=which==0?candidateSkins:controlSkins;var otherEnabled=other.Select(s=>s.enabled).ToArray();foreach(var skin in other)skin.enabled=false;
                        try
                        {
                            var vertices=baked[tier].vertices;var shownVertices=snapshotMesh.vertices;Need(vertices.Length==shownVertices.Length,"Snapshot topology differs.");float error=0;using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))
                            for(int vi=0;vi<vertices.Length;vi++)
                            {var v=vertices[vi];var measured=actor.transform.InverseTransformPoint(skins[tier].transform.TransformPoint(v));var shown=actor.transform.InverseTransformPoint(snapshot.transform.TransformPoint(shownVertices[vi]));error=Mathf.Max(error,Vector3.Distance(measured,shown));writer.Write(shown.x);writer.Write(shown.y);writer.Write(shown.z);}
                            Need(error<=.00001f,"CPU rendered snapshot differs from measured current pose.");using var sha=SHA256.Create();string vertexHash=BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-","").ToLowerInvariant();
                            foreach(string view in new[]{"front","side"})
                            {
                                var target=actor.PresentationPosition+Vector3.up*.9f;camera.transform.position=target+(view=="front"?new Vector3(0,.1f,4):new Vector3(4,.1f,0));camera.transform.LookAt(target);camera.cullingMask=1<<30;
                                string path=prefix+"-"+(which==0?"control":"candidate")+"-"+view+".png";RenderPoseCamera(camera,Path.Combine(directory,path));
                                rows.Add(new{path,sha256=Hash(Path.Combine(directory,path)),vertexHash,tier,error,view,actor=which==0?"control":"candidate",frame=Time.frameCount,phase=actor.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime,source="same-frame native BakeMesh, original materials, unchanged transform chain"});
                            }
                        }
                        finally{for(int i=0;i<other.Length;i++)other[i].enabled=otherEnabled[i];}
                    }
                    finally{if(snapshot!=null)UnityEngine.Object.DestroyImmediate(snapshot);if(snapshotMesh!=null)UnityEngine.Object.DestroyImmediate(snapshotMesh);for(int i=0;i<groups.Length;i++)groups[i].enabled=groupEnabled[i];for(int i=0;i<skins.Length;i++)skins[i].enabled=enabled[i];}
                }
                return rows;
            }
            finally{camera.transform.SetPositionAndRotation(oldPosition,oldRotation);camera.cullingMask=oldMask;}
        }
        private static void RenderPoseCamera(Camera camera,string path)
        {
            RenderTexture rt=null;Texture2D image=null;var prior=RenderTexture.active;
            try
            {
                rt=new RenderTexture(768,768,24,RenderTextureFormat.ARGB32);Need(rt.Create(),"Render texture creation failed.");image=new Texture2D(768,768,TextureFormat.RGB24,false);
                var request=new RenderPipeline.StandardRequest{destination=rt};Need(RenderPipeline.SupportsRenderRequest(camera,request),"Base-camera StandardRequest is unavailable.");
                RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,768,768),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally{RenderTexture.active=prior;if(image!=null)UnityEngine.Object.DestroyImmediate(image);if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}}
        }
        private static HashSet<int>[][] FootVertices(Animator animator,SkinnedMeshRenderer[] skins)
        {
            var result=new HashSet<int>[skins.Length][];
            for(int tier=0;tier<skins.Length;tier++)
            {
                result[tier]=new HashSet<int>[2];var bones=skins[tier].bones;var weights=skins[tier].sharedMesh.boneWeights;
                for(int side=0;side<2;side++)
                {
                    var foot=animator.GetBoneTransform(Feet[side]);var selected=new HashSet<int>();
                    for(int v=0;v<weights.Length;v++)
                    {
                        var w=weights[v];float Weight(int bone,float amount)=>amount>0&&(bones[bone]==foot||bones[bone].IsChildOf(foot))?amount:0;
                        if(Weight(w.boneIndex0,w.weight0)+Weight(w.boneIndex1,w.weight1)+Weight(w.boneIndex2,w.weight2)+Weight(w.boneIndex3,w.weight3)>.5f)selected.Add(v);
                    }
                    Need(selected.Count>=3,"Missing actual foot-weighted surface patch.");result[tier][side]=selected;
                }
            }
            return result;
        }
        private static object PoseWitness(AvatarView avatar)
        {
            float[] V(Vector3 value)=>new[]{value.x,value.y,value.z};
            float[] Q(Quaternion value)=>new[]{value.x,value.y,value.z,value.w};
            return WitnessBones.Select(bone=>{var t=avatar.Animator.GetBoneTransform(bone);return new{
                name=bone.ToString(),position=V(avatar.transform.InverseTransformPoint(t.position)),
                rotation=Q(Quaternion.Inverse(avatar.transform.rotation)*t.rotation),localRotation=Q(t.localRotation),
                parentRotation=Q(Quaternion.Inverse(avatar.transform.rotation)*t.parent.rotation)};}).ToArray();
        }
        private static void ExportObj(string path,Mesh mesh,Transform skin,Transform root)
        {
            using var writer=new StreamWriter(path);writer.WriteLine("# Actual native BakeMesh pose, actor-root coordinates; no pose correction/export reprojection.");
            foreach(var vertex in mesh.vertices)
            {var p=root.InverseTransformPoint(skin.TransformPoint(vertex));writer.WriteLine(FormattableString.Invariant($"v {p.x:R} {p.y:R} {p.z:R}"));}
            var triangles=mesh.triangles;
            for(int i=0;i<triangles.Length;i+=3)writer.WriteLine($"f {triangles[i]+1} {triangles[i+1]+1} {triangles[i+2]+1}");
        }
        private static JObject ReadStageProof()
        {
            Need(Hash(RoomPath)=="7415226b1301b2c636d34482b486535fb5a728df2fd8a33e31c2304fefb03e52","Exact saved Cafe profile/anchor prefab changed.");
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
            return new JObject{["path"]=path,["sha256"]=Hash(path),["profile_sha256"]=Hash(ProfilePath),["room_prefab_sha256"]=Hash(RoomPath),["source_dependencies"]=dependencies.DeepClone()};
        }
        private static string Hash(string path)
        {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        private static void Need(bool value,string reason){if(!value)throw new InvalidOperationException("Snow run review: "+reason);}
    }
}
#endif
