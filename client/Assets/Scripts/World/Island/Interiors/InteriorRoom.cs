using System;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// A walk-in room prefab (Resources/World/Interiors/&lt;id&gt;, built by IslandV2Interiors from
    /// art/processed/world_v3/v3_interiors): one baked-lighting mesh, box colliders (walls, floor and ceiling on the Interior
    /// layer so the camera keeps inside; furniture on the Prop layer so it stops bodies, not the camera) and sit points.
    /// Room space: the origin is the entrance's floor centre, +Z points out of the room (to the street), the room lies at z &lt; 0.
    /// </summary>
    public sealed class InteriorRoom : MonoBehaviour
    {
        [Serializable]
        public struct Seat
        {
            public string id;
            public string kind;        // sofa | seat
            public Vector3 position;   // room space: floor point under the seat's centre
            public float yaw;          // room space: the way a sitter faces (degrees about +Y)
            public Vector3 approach;   // room space: where the sitter stands first (floor)
        }

        public string roomId;
        public Vector3 boundsMin, boundsMax;
        public float doorWidth = 1.1f, doorHeight = 2.2f;
        public Seat[] seats = Array.Empty<Seat>();
        /// <summary>The source model's axes in room space (room = (axisX * x, z, axisZ * y) of the manifest's Blender coordinates).</summary>
        public float axisX = 1f, axisZ = -1f;

        /// <summary>A point given in the manifest's (Blender) room coordinates, in room space.</summary>
        public Vector3 FromSource(float x, float y, float z) => new Vector3(axisX * x, z, axisZ * y);

        /// <summary>Is a room-space point inside the room's footprint (a margin inside its outer walls)?</summary>
        public bool Contains(Vector3 local, float margin = 0.1f) =>
            local.x > boundsMin.x + margin && local.x < boundsMax.x - margin && local.z > boundsMin.z + margin && local.z < -margin &&
            local.y > boundsMin.y - 1f && local.y < boundsMax.y + 0.5f;
    }
}
