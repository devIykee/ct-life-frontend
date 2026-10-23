using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// Fewer draw calls per ground vehicle (perf lane 2, 2026-10-23). The v3 cars are built from parts (body, two doors, four
    /// wheels, the steering wheel) at every LOD, each part a renderer with up to three materials: ~14 draws for one car at
    /// LOD1, plus its shadow-map draws. <c>VehicleMergeBake</c> (editor) bakes, per LOD, one mesh of the parts that hold still
    /// relative to the body, one sub-mesh per material, in the rest pose: at LOD0/LOD1 the body and the doors (the wheels and
    /// the steering wheel turn, so they stay separate); at LOD2/LOD3 (beyond ~65 m, where a wheel is a few pixels) the wheels
    /// too. This switches between the merged renderer and the parts: merged while both doors are shut, the parts the moment
    /// a door starts to open (the doors animate), merged again once they are shut. The merged mesh is the parts in their
    /// rest pose, so the switch is invisible. It uses <see cref="Renderer.enabled"/> (the camera rig's own hide uses
    /// forceRenderingOff, so the two never fight).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleMerge : MonoBehaviour
    {
        [Tooltip("Baked merged renderers (any LOD).")]
        public Renderer[] merged = System.Array.Empty<Renderer>();
        [Tooltip("The part renderers the merged ones stand in for.")]
        public Renderer[] parts = System.Array.Empty<Renderer>();

        private Vehicle _v;
        private int _state = -1;   // 1 merged, 0 parts

        /// <summary>Live: the merged renderers stand in (counts and tests).</summary>
        public bool Merged => _state == 1;

        private void Awake() => _v = GetComponent<Vehicle>();

        private void OnEnable() { _state = -1; Apply(Wanted()); }

        private bool Wanted() => _v == null || _v.DoorsShut;

        private void LateUpdate()
        {
            bool want = Wanted();
            if ((want ? 1 : 0) != _state) Apply(want);
        }

        private void Apply(bool on)
        {
            _state = on ? 1 : 0;
            foreach (var r in merged) if (r != null) r.enabled = on;
            foreach (var r in parts) if (r != null) r.enabled = !on;
        }
    }
}
