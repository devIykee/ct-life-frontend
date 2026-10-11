using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Where AI traffic may spawn and despawn around the player: a box (transform = centre and heading, size in metres).
    /// density = target cars per 100 m of lane; Feel's traffic AI reads it. Registered while enabled.
    /// </summary>
    public sealed class TrafficZone : MonoBehaviour
    {
        public Vector3 size = new Vector3(200f, 60f, 200f);
        [Range(0f, 4f)] public float density = 1f;
        public string district;

        public bool Contains(Vector3 world)
        {
            var l = transform.InverseTransformPoint(world);
            return Mathf.Abs(l.x) <= size.x * 0.5f && Mathf.Abs(l.y) <= size.y * 0.5f && Mathf.Abs(l.z) <= size.z * 0.5f;
        }

        private void OnEnable() => IslandMarkers.Register(this);
        private void OnDisable() => IslandMarkers.Unregister(this);
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
#endif
    }
}
