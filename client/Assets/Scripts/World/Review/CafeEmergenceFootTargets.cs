#if UNITY_EDITOR
// Editor authoring only. Place in Scripts/World/Review, outside Editor; never attach to production prefab.
using UnityEngine;
namespace CTLife.EditorTools
{
    [ExecuteAlways]
    public sealed class CafeEmergenceFootTargets : MonoBehaviour
    {
        public Animator animator;
        public Transform root;
        public Vector3[] positions;
        public Quaternion[] rotations;
        public float weight;
        public int callbackCount;
        private void OnAnimatorIK(int layerIndex)
        {
            if(layerIndex!=0||animator==null||root==null||positions==null||rotations==null)return;
            callbackCount++;
            if(weight<=0f)return; // No-op control preserves state-authored Foot IK weights.
            for(int side=0;side<2;side++)
            {
                var goal=side==0?AvatarIKGoal.LeftFoot:AvatarIKGoal.RightFoot;
                animator.SetIKPositionWeight(goal,weight);animator.SetIKRotationWeight(goal,weight);
                animator.SetIKPosition(goal,root.TransformPoint(positions[side]));
                animator.SetIKRotation(goal,root.rotation*rotations[side]);
            }
        }
    }
}
#endif
