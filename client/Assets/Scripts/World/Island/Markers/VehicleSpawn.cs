using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// A parking bay where a drivable vehicle may be spawned (World places them; Feel spawns). The transform is the bay
    /// centre on the ground, forward = the way a parked car faces. Registered while enabled (cells stream in and out).
    /// </summary>
    public sealed class VehicleSpawn : MonoBehaviour
    {
        public VehicleClass classes = VehicleClass.Cars;
        [Tooltip("Bay size in metres (width across, length along forward).")]
        public Vector2 size = new Vector2(2.5f, 5.5f);
        [Tooltip("street | lot | garage | marina | helipad-side")]
        public string kind = "street";
        [Tooltip("Road segment this bay belongs to (street parking), for leaving into traffic.")]
        public string segmentId;
        [Tooltip("Placement priority 0..1: demo-visible bays (plaza, beach, lodge) score higher.")]
        [Range(0f, 1f)] public float showcase;

        public bool Accepts(VehicleClass c) => (classes & c) != 0;
        private void OnEnable() => IslandMarkers.Register(this);
        private void OnDisable() => IslandMarkers.Unregister(this);
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * 0.05f, new Vector3(size.x, 0.1f, size.y));
        }
#endif
    }
}
