#if UNITY_EDITOR
// Editor-only preview component. Place in Scripts/World/Review, outside any Editor folder.
// UNITY_EDITOR excludes the component from player builds. Never attach to a production prefab.
using UnityEngine;

namespace CTLife.EditorTools
{
    [ExecuteAlways]
    public sealed class SeatedTypingHandTargets : MonoBehaviour
    {
        public Animator animator;
        public Transform root;
        public Vector3[][] positions;
        public Quaternion[][] rotations;
        public float weight;
        public int callbackCount;

        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || animator == null || root == null || positions == null || rotations == null) return;
            callbackCount++;
            float phase = Mathf.Repeat(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f);
            float at = phase * (positions.Length - 1);
            int a = Mathf.FloorToInt(at), b = Mathf.Min(a + 1, positions.Length - 1);
            for (int side = 0; side < 2; side++)
            {
                var goal = side == 0 ? AvatarIKGoal.LeftHand : AvatarIKGoal.RightHand;
                animator.SetIKPositionWeight(goal, weight);
                animator.SetIKRotationWeight(goal, weight);
                animator.SetIKPosition(goal, root.TransformPoint(Vector3.Lerp(positions[a][side], positions[b][side], at - a)));
                animator.SetIKRotation(goal, root.rotation * Quaternion.Slerp(rotations[a][side], rotations[b][side], at - a));
            }
        }
    }
}
#endif
