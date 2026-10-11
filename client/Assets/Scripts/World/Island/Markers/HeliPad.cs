using UnityEngine;

namespace CTLife.World
{
    /// <summary>A helicopter landing pad (the transform is the pad centre on its surface; radius in metres).</summary>
    public sealed class HeliPad : MonoBehaviour
    {
        public float radius = 9f;
        public string label;
        private void OnEnable() => IslandMarkers.Register(this);
        private void OnDisable() => IslandMarkers.Unregister(this);
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
#endif
    }
}
