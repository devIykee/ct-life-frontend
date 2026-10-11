using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;
using UnityEngine.AI;

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

        [Tooltip("Editor-built navigation from the contract walkable rectangles and the venue's solid props.")]
        public NavMeshData navigation;

        [Tooltip("Authored high three-quarter view. Zero distance keeps the generic camera settings.")]
        public float cameraDistance;
        public float cameraPitch = 50f;
        public float cameraYaw = 25f;
        public float cameraFieldOfView = 42f;
        public Vector3 cameraLookAhead;
        [Range(0f, 0.5f), Tooltip("Zero uses fixed look-ahead. Otherwise frame the followed player's feet at this viewport height.")]
        public float cameraTargetViewportY;
        public float cameraFocusHeight = 1f;

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
                    rotYRad = s.rotY + YawRad;   // world yaw, like anchors (CT Island v2 venues are turned to face their streets)
                    return true;
                }
                Log.UnknownId("spots", (venueId ?? "?") + "." + spotId);
            }
            position = transform.position;
            rotYRad = 0;
            return false;
        }

        /// <summary>Local comparison key. Wire IDs remain registry-owned.</summary>
        public string LocalSpotId(string spotId) => !string.IsNullOrEmpty(venueId) &&
            spotId != null && spotId.StartsWith(venueId + ".", System.StringComparison.Ordinal)
                ? spotId.Substring(venueId.Length + 1) : spotId;

        /// <summary>Authored navigation approach, or the existing seat point for unchanged venues.</summary>
        public bool TryGetSpotApproach(string spotId, out Vector3 position)
        {
            var local = LocalSpotId(spotId);
            if (!string.IsNullOrEmpty(local) &&
                (_anchors.TryGetValue(local, out var anchor) || _anchors.TryGetValue(spotId, out anchor)) &&
                anchor.approach != null)
            {
                position = anchor.approach.position;
                return true;
            }
            return TryGetSpot(spotId, out position, out _);
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

        /// <summary>This root's yaw in radians (0 for venues loaded on their own; CT Island turns venues to their streets).</summary>
        public float YawRad => transform.eulerAngles.y * Mathf.Deg2Rad;

        public Vector3 ToWorld(float x, float z) => transform.TransformPoint(new Vector3(x, 0f, z));

        public Vector3 ToWorld(Vector2 local) => ToWorld(local.x, local.y);

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

        // NPC homes (NpcSpawnPlanner): npcId → spot id, or a ring slot when no spot is free.
        private readonly Dictionary<string, string> _npcHomeSpot = new Dictionary<string, string>();
        private readonly Dictionary<string, int> _npcHomeRing = new Dictionary<string, int>();

        /// <summary>
        /// Where an NPC stands while the server has given it no spot: its own NPC/standing spot, away from the player's
        /// spawn and off the camera's centre line (see <see cref="NpcSpawnPlanner"/>). Stable per NPC until released.
        /// </summary>
        public Vector3 NpcHome(string npcId)
        {
            npcId ??= "";
            if (_npcHomeSpot.TryGetValue(npcId, out var claimed) && TryGetSpot(claimed, out var claimedPos, out _)) return claimedPos;
            TryGetSpot("entrance", out var entranceWorld, out var entranceRot);
            var entrance = ToLocal(entranceWorld);
            var forward = NpcSpawnPlanner.Forward(entranceRot - YawRad);   // the planner works in venue-local space
            if (_npcHomeRing.TryGetValue(npcId, out var ring)) return ToWorld(NpcSpawnPlanner.Ring(ring, entrance, forward));

            // The registry indexes every spot twice (local `crier_2` and wire `timeline_square.crier_2`, same SpotDef), so
            // candidates are keyed by the local id; otherwise two NPCs could claim one spot under its two names (seen on the
            // Seeker 2026-10-08: town_crier and doomposter both at crier_2).
            var candidates = new List<(string id, Vector2 pos)>();
            var seen = new HashSet<string>();
            if (_registrySpots != null)
                foreach (var def in _registrySpots.spots.Values)
                {
                    if (def == null || !NpcSpawnPlanner.IsHomeKind(def.kind) || !seen.Add(def.id)) continue;
                    if (TryGetSpot(def.id, out var world, out _)) candidates.Add((def.id, ToLocal(world)));
                }
            var taken = new HashSet<string>(_npcHomeSpot.Values);
            var pick = NpcSpawnPlanner.Pick(npcId, candidates, entrance, forward, taken);
            if (pick != null && TryGetSpot(pick, out var pos, out _))
            {
                _npcHomeSpot[npcId] = pick;
                return pos;
            }
            int slot = 0;
            var used = new HashSet<int>(_npcHomeRing.Values);
            while (used.Contains(slot)) slot++;
            _npcHomeRing[npcId] = slot;
            return ToWorld(NpcSpawnPlanner.Ring(slot, entrance, forward));
        }

        public void ReleaseNpcHome(string npcId)
        {
            if (npcId == null) return;
            _npcHomeSpot.Remove(npcId);
            _npcHomeRing.Remove(npcId);
        }
    }
}
