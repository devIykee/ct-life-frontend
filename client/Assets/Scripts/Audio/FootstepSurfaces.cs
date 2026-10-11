using System.Collections.Generic;
using UnityEngine;

namespace CTLife.Audio
{
    /// <summary>
    /// Ground lookup under a foot: one short downward raycast, then <see cref="SurfaceResolver.Resolve"/> on the hit
    /// collider (tag component → physics material → renderer material → object name). Results are cached per collider
    /// and cleared on venue change, so a step costs one raycast and a dictionary lookup.
    /// </summary>
    internal sealed class FootstepSurfaces
    {
        private const float RayUp = 0.5f, RayLength = 1.5f;
        private readonly RaycastHit[] _hits = new RaycastHit[4];
        private readonly Dictionary<int, string> _byCollider = new Dictionary<int, string>();

        public void Clear() => _byCollider.Clear();

        public string Resolve(Vector3 foot, string venueDefault)
        {
            int n;
            try
            {
                n = Physics.RaycastNonAlloc(foot + Vector3.up * RayUp, Vector3.down, _hits, RayLength,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            }
            catch (System.Exception) { return SurfaceResolver.IsKnown(venueDefault) ? venueDefault : SurfaceResolver.Concrete; }
            Collider best = null;
            float bestY = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                // The highest surface at or just below the foot (ignores ceilings above and the floor under a rug).
                if (c == null || _hits[i].point.y > foot.y + 0.3f || _hits[i].point.y <= bestY) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue; // props being pushed
                best = c;
                bestY = _hits[i].point.y;
            }
            if (best == null) return SurfaceResolver.IsKnown(venueDefault) ? venueDefault : SurfaceResolver.Concrete;
            int id = best.GetInstanceID();
            if (_byCollider.TryGetValue(id, out var cached)) return cached;
            var tag = best.GetComponentInParent<FootstepSurface>();
            var renderer = best.GetComponent<Renderer>();
            string surface = SurfaceResolver.Resolve(
                tag != null ? tag.surface : null,
                best.sharedMaterial != null ? best.sharedMaterial.name : null,
                renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : null,
                best is TerrainCollider ? "terrain" : best.gameObject.name,
                venueDefault);
            if (_byCollider.Count > 512) _byCollider.Clear();
            _byCollider[id] = surface;
            return surface;
        }
    }
}
