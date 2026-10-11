using UnityEngine;

namespace CTLife.World
{
    //Bounded exit-only authoring replay. Ordinary movement uses the existing Animator states.
    //No mesh baking, bone discovery, curve construction or managed array allocation in the callback.
    [DisallowMultipleComponent]
    public sealed class CafeWorkExitGoals : MonoBehaviour
    {
        private Animator _animator;
        private CafeWorkExitMotion _motion;
        private Vector3 _seat;
        private Quaternion _yaw;
        private float _seconds;
        private int _frame = -1;
        public float Seconds => _seconds;
        public int CallbackCount { get; private set; }
        public bool Running => enabled && _motion != null;

        internal bool Begin(Animator animator,CafeWorkExitMotion motion,Vector3 seat,Quaternion yaw)
        {
            if(animator==null||motion==null||!motion.Valid)return false;
            _animator=animator;_motion=motion;_seat=seat;_yaw=yaw;_seconds=0;_frame=-1;CallbackCount=0;enabled=true;return true;
        }
        internal void End(){enabled=false;_motion=null;}
        private void OnDisable(){_motion=null;}
        private void OnAnimatorIK(int layer)
        {
            if(layer!=0||_animator==null||_motion==null||!_motion.Valid)return;
            if(_frame!=Time.frameCount)
            {
                _frame=Time.frameCount;
                //The first native callback initializes the new CrossFade destination at time0.
                //Counting its whole pending frame delta would put goals ahead of the body clip.
                //Start both at0; later callbacks advance once per actual Animator frame.
                if(CallbackCount>0)_seconds=Mathf.Min(CafeWorkExitMotion.Length,_seconds+Time.deltaTime*CafeWorkExitMotion.Rate);
            }
            CallbackCount++;
            float key=_seconds*120f;int a=Mathf.Clamp(Mathf.FloorToInt(key),0,CafeWorkExitMotion.SampleCount-1),b=Mathf.Min(a+1,CafeWorkExitMotion.SampleCount-1);float t=key-a;
            var x=_motion.frames[a];var y=_motion.frames[b];
            _animator.bodyPosition+=_yaw*Vector3.Lerp(x.body,y.body,t);
            Goal(AvatarIKGoal.LeftFoot,Vector3.Lerp(x.leftFoot,y.leftFoot,t),Quaternion.Slerp(x.leftFootRotation,y.leftFootRotation,t));
            Goal(AvatarIKGoal.RightFoot,Vector3.Lerp(x.rightFoot,y.rightFoot,t),Quaternion.Slerp(x.rightFootRotation,y.rightFootRotation,t));
            if(_motion.hands)
            {
                Goal(AvatarIKGoal.LeftHand,Vector3.Lerp(x.leftHand,y.leftHand,t),Quaternion.Slerp(x.leftHandRotation,y.leftHandRotation,t));
                Goal(AvatarIKGoal.RightHand,Vector3.Lerp(x.rightHand,y.rightHand,t),Quaternion.Slerp(x.rightHandRotation,y.rightHandRotation,t));
                _animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow,1);_animator.SetIKHintPosition(AvatarIKHint.LeftElbow,_seat+_yaw*Vector3.Lerp(x.leftElbow,y.leftElbow,t));
                _animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow,1);_animator.SetIKHintPosition(AvatarIKHint.RightElbow,_seat+_yaw*Vector3.Lerp(x.rightElbow,y.rightElbow,t));
            }
        }
        private void Goal(AvatarIKGoal goal,Vector3 position,Quaternion rotation)
        {
            _animator.SetIKPositionWeight(goal,1);_animator.SetIKRotationWeight(goal,1);
            _animator.SetIKPosition(goal,_seat+_yaw*position);_animator.SetIKRotation(goal,_yaw*rotation);
        }
    }
}
