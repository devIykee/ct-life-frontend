using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Root component of every venue diorama scene (one scene per venue, loaded additively, Build Spec "Environment").
    /// Venue-local coordinates are this transform's local space. Spots come from child <see cref="SpotAnchor"/>s,
    /// with contract/ids/spots.json as the fallback for spots the scene does not author yet.
    /// </summary>
    public sealed class VenueSceneRoot : MonoBehaviour
    {
        public string venueId;
        public string districtId;

        [Tooltip("Camera focus limits in venue-local metres (x0, z0, x1, z1).")]
        public Rect cameraBounds = new Rect(-20, -15, 40, 30);

        [Tooltip("Ground layer for tap raycasts.")]
        public LayerMask groundMask = ~0;

        public bool IsGreybox { get; set; }

        private readonly Dictionary<string, SpotAnchor> _anchors = new Dictionary<string, SpotAnchor>();
        private VenueSpots _registrySpots;

        public VenueSpots RegistrySpots => _registrySpots;

        public void Init(VenueSpots registrySpots)
        {
            _registrySpots = registrySpots;
            _anchors.Clear();
            foreach (var a in GetComponentsInChildren<SpotAnchor>(true))
                _anchors[a.spotId] = a;
        }

        public bool TryGetSpot(string spotId, out Vector3 position, out float rotYRad)
        {
            if (!string.IsNullOrEmpty(spotId))
            {
                // rc1 uses global ids (`timeline_square.center`); scene anchors are venue-local.
                var local = venueId != null && spotId.StartsWith(venueId + ".") ? spotId.Substring(venueId.Length + 1) : spotId;
                if (_anchors.TryGetValue(local, out var a) || _anchors.TryGetValue(spotId, out a))
                {
                    position = a.transform.position;
                    rotYRad = a.RotY;
                    return true;
                }
                if (_registrySpots != null && (_registrySpots.spots.TryGetValue(spotId, out var s) || _registrySpots.spots.TryGetValue(local, out s)))
                {
                    position = ToWorld(s.x, s.z);
                    rotYRad = s.rotY;
                    return true;
                }
                Log.UnknownId("spots", (venueId ?? "?") + "." + spotId);
            }
            position = transform.position;
            rotYRad = 0;
            return false;
        }

        public SpotDef SpotDef(string spotId) =>
            _registrySpots != null && spotId != null && _registrySpots.spots.TryGetValue(spotId, out var s) ? s : null;

        /// <summary>The id to send in `sit` (registry wire form; rc1 = global `venueId.spotId`).</summary>
        public string WireSpotId(string spotId) => SpotDef(spotId)?.wireId ?? spotId;

        public Vector3 EntrancePosition
        {
            get
            {
                TryGetSpot("entrance", out var p, out _);
                return p;
            }
        }

        public Vector3 ToWorld(float x, float z) => transform.TransformPoint(new Vector3(x, 0f, z));

        public Vector2 ToLocal(Vector3 world)
        {
            var l = transform.InverseTransformPoint(world);
            return new Vector2(l.x, l.z);
        }

        public bool IsWalkable(Vector3 world)
        {
            if (_registrySpots == null) return true;
            var l = ToLocal(world);
            return _registrySpots.IsWalkable(l.x, l.y);
        }

        /// <summary>Spot anchors for the spots.json exporter and NPC fallbacks.</summary>
        public IEnumerable<SpotAnchor> Anchors => _anchors.Values;
    }
}
