using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// What the open island needs from whichever camera renders it (World lane; the camera itself is Feel's): a far plane
    /// that reaches across the island, the sky instead of a solid colour, and per-layer cull distances so small props and
    /// plants stop drawing at sensible ranges. Applied every frame to Camera.main while the island is loaded and only
    /// ever raises the far plane, so Feel's camera code keeps control of everything else.
    /// </summary>
    public sealed class IslandRenderSettings : MonoBehaviour
    {
        public float farClip = 5200f;
        [Tooltip("Cull distance per World layer (0 = the far plane).")]
        public float groundCull, roadCull, buildingCull, propCull = 160f, vegetationCull = 420f, waterCull, interiorCull = 220f;
        public bool skybox = true;

        public const int Ground = 10, Road = 11, Building = 12, Prop = 13, Vegetation = 14, VehicleBlocker = 15, CoastWall = 16, Interior = 17;

        private Camera _applied;
        private readonly float[] _dist = new float[32];
        private float _prevFar;
        private CameraClearFlags _prevClear;

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (cam != _applied) { Restore(); _applied = cam; _prevFar = cam.farClipPlane; _prevClear = cam.clearFlags; }
            if (cam.farClipPlane < farClip) cam.farClipPlane = farClip;
            if (skybox && RenderSettings.skybox != null && cam.clearFlags != CameraClearFlags.Skybox) cam.clearFlags = CameraClearFlags.Skybox;
            _dist[Ground] = groundCull; _dist[Road] = roadCull; _dist[Building] = buildingCull; _dist[Prop] = propCull;
            _dist[Vegetation] = vegetationCull; _dist[4] = waterCull; _dist[Interior] = interiorCull;
            cam.layerCullDistances = _dist;
            cam.layerCullSpherical = false;
        }

        private void Restore()
        {
            if (_applied == null) return;
            _applied.farClipPlane = _prevFar;
            _applied.clearFlags = _prevClear;
            _applied.layerCullDistances = new float[32];
            _applied = null;
        }

        private void OnDisable() => Restore();
    }
}
