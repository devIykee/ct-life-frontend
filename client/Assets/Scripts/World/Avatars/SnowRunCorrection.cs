using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>Candidate six-leg-bone correction after native animation. No root/hips/upper-body writes.</summary>
    [DisallowMultipleComponent]
    public sealed class SnowRunCorrection : MonoBehaviour
    {
        private AvatarView _owner; private Animator _animator; private SnowRunCorrectionProfile _profile;
        private readonly Transform[] _bones=new Transform[6];
        private readonly Quaternion[] _before=new Quaternion[6],_written=new Quaternion[6];
        private readonly List<AnimatorClipInfo> _current=new List<AnimatorClipInfo>(4),_next=new List<AnimatorClipInfo>(4);
        private bool _owned,_paused;
        private SnowTransitionContact _contact;
        public bool TransitionAppliedThisFrame { get; private set; }
        public int TransitionApplyCount { get; private set; }
        public int ContactVertexCount => _contact != null ? _contact.ShoeVertexCount : 0;
        public int ContactMatrixCount => _contact != null ? _contact.MatrixCount : 0;
        public float MaximumTransitionLift => _contact != null ? _contact.MaximumAnkleLift : 0;
        public float MaximumTransitionKneeChange => _contact != null ? _contact.MaximumKneeChange : 0;
        public double LastContactMilliseconds { get; private set; }
        public float EvaluatedSole(int side) => _contact != null ? _contact.MinimumSole(side) : float.NaN;
        private int _evaluatedFrame=-1,_consumedEvaluation=-1;
        public int EvaluationCount{get;private set;}
        public int ApplyCount{get;private set;}
        public float CurrentWeight{get;private set;}
        public float NextWeight{get;private set;}
        public float TotalReportedWeight{get;private set;}
        public string Failure{get;private set;}
        private static readonly int RunHash=Animator.StringToHash("loco_run");
        private static readonly int WalkHash=Animator.StringToHash("loco_walk"), IdleHash=Animator.StringToHash("loco_idle"), WaveHash=Animator.StringToHash("emote_wave");
        public bool Bind(AvatarView owner,SnowRunCorrectionProfile profile,GameObject sourcePrefab)
        {
            if(_owner!=null||owner==null||owner.Animator==null||owner.Animator.gameObject!=gameObject||profile==null||!profile.Matches(owner.Animator,sourcePrefab))return false;
            for(int b=0;b<6;b++)
            { _bones[b]=owner.Animator.GetBoneTransform(SnowRunCorrectionProfile.Bones[b]);if(_bones[b]==null)return false; }
            try { _contact=new SnowTransitionContact(owner.Animator,owner.transform,owner.GetComponentsInChildren<SkinnedMeshRenderer>(true)); }
            catch(System.Exception ex) { Failure="Source skin binding rejected: "+ex.GetType().Name+": "+ex.Message;return false; }
            _owner=owner;_animator=owner.Animator;_profile=profile;return true;
        }
        // Unity 6.3 documents this callback after native animation evaluation, before IK.
        // applyRootMotion=false is a binding/runtime precondition; this observer never writes root motion.
        private void OnAnimatorMove(){if(_owner!=null){EvaluationCount++;_evaluatedFrame=Time.frameCount;}}
        private void OnDisable(){RestoreOwned();}
        private void OnApplicationPause(bool paused){_paused=paused;if(paused)RestoreOwned();}
        // Losing focus restores owned locals. If native animation continues, a NEW actual evaluation may
        // still receive contact correction; this does not enable/update a paused or culled Animator.
        private void OnApplicationFocus(bool focused){if(!focused)RestoreOwned();}
        internal void BeforeAnimation()=>RestoreOwned();
        internal void AfterAnimation()
        {
            CurrentWeight=NextWeight=TotalReportedWeight=0;TransitionAppliedThisFrame=false;LastContactMilliseconds=0;
            if(!enabled||_owner==null||_animator==null||_profile==null||Failure!=null||_paused||
                _owner.SeatExitActive||_owner.Culled||!_animator.enabled||!_animator.gameObject.activeInHierarchy||
                _animator.avatar!=_profile.sourceAvatar||_animator.layerCount!=1||_animator.applyRootMotion||!_profile.Allows(_animator.runtimeAnimatorController)||
                _evaluatedFrame!=Time.frameCount||_consumedEvaluation==EvaluationCount)return;
            _consumedEvaluation=EvaluationCount;
            var current=_animator.GetCurrentAnimatorStateInfo(0);bool transition=_animator.IsInTransition(0);
            var next=transition?_animator.GetNextAnimatorStateInfo(0):default;
            _current.Clear();_next.Clear();_animator.GetCurrentAnimatorClipInfo(0,_current);
            if(transition)_animator.GetNextAnimatorClipInfo(0,_next);
            TotalReportedWeight=Sum(_current)+Sum(_next);
            bool hasRun=current.shortNameHash==RunHash||(transition&&next.shortNameHash==RunHash);
            if(!hasRun)return;
            // Never invent a transition weight from a wall clock or silently normalize an incomplete/interrupted blend.
            if(!Finite(TotalReportedWeight)||Mathf.Abs(TotalReportedWeight-1)>0.001f)
            { Failure="Animator current/next clip weights do not form a complete blend.";return; }
            CurrentWeight=RunWeight(current,_current);NextWeight=transition?RunWeight(next,_next):0;
            if(Failure!=null||CurrentWeight+NextWeight<=0)return;
            if(!Finite(current.normalizedTime)||(transition&&!Finite(next.normalizedTime)))
            {Failure="Nonfinite Animator phase.";return;}
            if(transition)
            {
                // This new envelope is confined to an evaluated run boundary. Pure emotes/other
                // actions keep their original poses and are NOT certified by this contact candidate.
                bool Supported(int hash)=>hash==RunHash||hash==WalkHash||hash==IdleHash||hash==WaveHash;
                if(!Supported(current.shortNameHash)||!Supported(next.shortNameHash))return;
                for(int b=0;b<6;b++)_before[b]=_bones[b].localRotation;
                long started=System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    _contact.Apply();
                    for(int b=0;b<6;b++)_written[b]=_bones[b].localRotation;
                    _owned=true;ApplyCount++;TransitionApplyCount++;TransitionAppliedThisFrame=true;
                }
                catch(System.Exception ex)
                {
                    // Synchronous transaction: no other pose writer can intervene inside this call.
                    for(int b=0;b<6;b++)if(_bones[b]!=null)_bones[b].localRotation=_before[b];
                    _owned=false;Failure="Transition contact rejected: "+ex.GetType().Name+": "+ex.Message;
                }
                finally { LastContactMilliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000d/System.Diagnostics.Stopwatch.Frequency; }
                return;
            }
            // Accepted steady-run six-local-rotation table is unchanged.
            for(int b=0;b<6;b++)
            {
                Quaternion a=CurrentWeight>0?_profile.Sample(current.normalizedTime,b):Quaternion.identity;
                Quaternion z=NextWeight>0?_profile.Sample(next.normalizedTime,b):Quaternion.identity;
                if(a.w<0)a=new Quaternion(-a.x,-a.y,-a.z,-a.w);
                if(z.w<0)z=new Quaternion(-z.x,-z.y,-z.z,-z.w);
                float rest=Mathf.Max(0,1-CurrentWeight-NextWeight);
                Quaternion delta=Quaternion.Normalize(new Quaternion(a.x*CurrentWeight+z.x*NextWeight,a.y*CurrentWeight+z.y*NextWeight,
                    a.z*CurrentWeight+z.z*NextWeight,rest+a.w*CurrentWeight+z.w*NextWeight));
                _before[b]=_bones[b].localRotation;_written[b]=delta*_before[b];
            }
            for(int b=0;b<6;b++)_bones[b].localRotation=_written[b];
            _owned=true;ApplyCount++;
        }
        private float RunWeight(AnimatorStateInfo state,List<AnimatorClipInfo> clips)
        {
            if(state.shortNameHash!=RunHash)return 0;
            if(clips.Count!=1||clips[0].clip!=_profile.sourceClip||!state.loop)
            {Failure="Run state no longer uses the accepted single looping source clip.";return 0;}
            return clips[0].weight;
        }
        private static float Sum(List<AnimatorClipInfo> clips)
        {
            float result=0;foreach(var clip in clips)
            {if(!Finite(clip.weight)||clip.weight<0)return float.NaN;result+=clip.weight;}
            return result;
        }
        private static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        private void RestoreOwned()
        {
            if(!_owned)return;_owned=false;
            // A different pose writer owns changed transforms; never overwrite it with a stale snapshot.
            for(int b=0;b<6;b++)if(_bones[b]==null||Mathf.Abs(Quaternion.Dot(_bones[b].localRotation,_written[b]))<.9999999f)return;
            for(int b=0;b<6;b++)_bones[b].localRotation=_before[b];
        }
    }
}
