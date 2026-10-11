#if UNITY_EDITOR
// Editor authoring only. Place in Scripts/World/Review, outside Editor; never attach to production prefab.
using UnityEngine;
namespace CTLife.EditorTools
{
    [ExecuteAlways]
    public sealed class CafeExitChoreographyTargets : MonoBehaviour
    {
        public Animator animator;
        public Transform root;
        public Vector3[] positions;
        public Quaternion[] rotations;
        public float weight;
        public Vector3 bodyOffset;
        public Vector3[] handPositions, elbowHints;
        public Quaternion[] handRotations;
        public int callbackCount;
        private void OnAnimatorIK(int layerIndex)
        {
            if(layerIndex!=0||animator==null||root==null||positions==null||rotations==null)return;
            callbackCount++;
            if(weight<=0f)return; // No-op control preserves state-authored Foot IK weights.
            animator.bodyPosition += root.TransformVector(bodyOffset); // Authored world-space CoM displacement inside OnAnimatorIK only.
            for(int side=0;side<2;side++)
            {
                var goal=side==0?AvatarIKGoal.LeftFoot:AvatarIKGoal.RightFoot;
                animator.SetIKPositionWeight(goal,weight);animator.SetIKRotationWeight(goal,weight);
                animator.SetIKPosition(goal,root.TransformPoint(positions[side]));
                animator.SetIKRotation(goal,root.rotation*rotations[side]);
                if(handPositions!=null&&handRotations!=null)
                {
                    var hand=side==0?AvatarIKGoal.LeftHand:AvatarIKGoal.RightHand;
                    animator.SetIKPositionWeight(hand,1);animator.SetIKRotationWeight(hand,1);
                    animator.SetIKPosition(hand,root.TransformPoint(handPositions[side]));animator.SetIKRotation(hand,root.rotation*handRotations[side]);
                    var hint=side==0?AvatarIKHint.LeftElbow:AvatarIKHint.RightElbow;
                    animator.SetIKHintPositionWeight(hint,1);animator.SetIKHintPosition(hint,root.TransformPoint(elbowHints[side]));
                }
            }
        }
    }
}
#endif
