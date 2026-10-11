using UnityEngine;

namespace CTLife.Audio
{
    /// <summary>
    /// Tag a floor/ground object (or any parent of its collider) with its footstep surface. Highest priority in
    /// <see cref="SurfaceResolver"/>. Values: concrete, wood, metal_grate, carpet, neon_tile, grass.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FootstepSurface : MonoBehaviour
    {
        public string surface = SurfaceResolver.Concrete;
    }
}
