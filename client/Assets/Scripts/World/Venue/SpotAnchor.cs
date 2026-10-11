using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// A named spot inside a venue scene (Build Spec "NPCs and AI": spots are named anchors that FE authors;
    /// their IDs and positions are exported to contract/ids/spots.json). Venue-local: x right, z forward, metres;
    /// rotY in radians = this transform's yaw.
    /// </summary>
    public sealed class SpotAnchor : MonoBehaviour
    {
        [Tooltip("Venue-local id, e.g. terminal_1, bench_3, stage_host, entrance")]
        public string spotId = "spot";

        [Tooltip("stand | seat | terminal | stage | bed | npc | exit")]
        public string kind = "stand";

        [Tooltip("anims.json id the server sets while a player claims this spot with `sit` (e.g. act_sit, act_type). Empty = not claimable.")]
        public string anim = "";

        [Tooltip("Optional frontend walking/standing point beside furniture. Does not change the contract seat anchor or wire ID.")]
        public Transform approach;

        public float RotY => transform.eulerAngles.y * Mathf.Deg2Rad;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = kind == "npc" ? Color.magenta : kind == "exit" ? Color.red : string.IsNullOrEmpty(anim) ? Color.cyan : Color.yellow;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.1f, 0.35f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.1f, transform.position + Vector3.up * 0.1f + transform.forward * 0.6f);
        }
#endif
    }
}
