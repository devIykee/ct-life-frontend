using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace CTLife.World
{
    /// <summary>
    /// Data-driven interactables for one venue, from its SpotAnchors (authored, with approach points) and the contract
    /// spot registry: claimable spots (`anim` set) become networked seats/terminals/stage; the host stage spot becomes the
    /// mic; `exit` becomes the door; an `npc` counter spot gets a customer-side counter interaction (local only).
    /// </summary>
    public static class InteractableCatalog
    {
        /// <summary>Pure classification of one spot. Null = not something the player uses.</summary>
        public static InteractionKind? Classify(string spotId, string spotKind, string anim)
        {
            var id = (spotId ?? "").ToLowerInvariant();
            var kind = (spotKind ?? "").ToLowerInvariant();
            bool claimable = !string.IsNullOrEmpty(anim);
            switch (kind)
            {
                case "seat":
                    if (!claimable) return null;
                    if (id.Contains("bench")) return InteractionKind.Bench;
                    if (id.Contains("sofa") || id.Contains("couch")) return InteractionKind.Sofa;
                    return InteractionKind.Seat;
                case "terminal":
                    return claimable ? InteractionKind.Terminal : (InteractionKind?)null;
                case "stage":
                    if (!claimable) return null;
                    // Stage chairs carry a seated anim; the host spot (standing anim) is the mic.
                    return anim == "act_sit" || anim == "act_type" ? InteractionKind.StageSeat : InteractionKind.Mic;
                case "exit":
                    return id == "exit" ? InteractionKind.Door : (InteractionKind?)null;
                case "npc":
                    return id.Contains("counter") || id.Contains("bar") ? InteractionKind.Counter : (InteractionKind?)null;
                default:
                    return null;
            }
        }

        /// <summary>Presentation loop while engaged (the server's anim for seats; FE presentation for the rest).</summary>
        public static string EngagedAnim(InteractionKind kind, string spotAnim) => kind switch
        {
            InteractionKind.Mic => "act_host_mic",
            InteractionKind.Counter => "idle_lean_rail",
            InteractionKind.Door => null,
            _ => string.IsNullOrEmpty(spotAnim) ? "act_sit" : spotAnim,
        };

        public static float RangeFor(InteractionKind kind) => kind switch
        {
            InteractionKind.Door => 2.6f,
            InteractionKind.Counter => 2.2f,
            InteractionKind.Mic => 2.4f,
            _ => 1.9f,
        };

        public static List<Interactable> Build(VenueSceneRoot venue)
        {
            var list = new List<Interactable>();
            if (venue == null) return list;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in venue.Anchors)
            {
                if (a == null || !seen.Add(a.spotId)) continue;
                var it = FromAnchor(venue, a.spotId, a.kind, a.anim, a.transform.position, a.transform.eulerAngles.y,
                    a.approach != null ? a.approach.position : (Vector3?)null);
                if (it != null) list.Add(it);
            }
            var reg = venue.RegistrySpots;
            if (reg != null)
                foreach (var s in reg.spots.Values)
                {
                    if (s == null || string.IsNullOrEmpty(s.id) || !s.hasPosition || !seen.Add(s.id)) continue;
                    var it = FromAnchor(venue, s.id, s.kind, s.anim, venue.ToWorld(s.x, s.z), s.rotY * Mathf.Rad2Deg, null);
                    if (it != null) list.Add(it);
                }
            return list;
        }

        private static Interactable FromAnchor(VenueSceneRoot venue, string id, string spotKind, string anim, Vector3 pos, float yaw, Vector3? authoredApproach)
        {
            var k = Classify(id, spotKind, anim);
            if (k == null) return null;
            var kind = k.Value;
            var forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            var it = new Interactable
            {
                id = id, kind = kind, anim = EngagedAnim(kind, anim), position = pos, yaw = yaw, range = RangeFor(kind),
                // Only spots the contract can claim travel on the wire.
                wireId = kind == InteractionKind.Counter || kind == InteractionKind.Door ? null : venue.WireSpotId(id),
            };
            switch (kind)
            {
                case InteractionKind.Counter:
                    // The npc spot is behind the counter facing the room: the customer stands on the clear side, facing back.
                    it.position = ClearPointAlong(pos, forward, 0.6f, 2.6f);
                    it.yaw = yaw + 180f;
                    it.approach = it.position;
                    break;
                case InteractionKind.Door:
                    it.approach = Sample(pos, pos);
                    break;
                default:
                    it.approach = authoredApproach ?? Sample(pos + forward * 0.6f, pos);
                    break;
            }
            return it;
        }

        /// <summary>First point along dir (from min to max metres) where a standing body fits and the NavMesh exists.</summary>
        public static Vector3 ClearPointAlong(Vector3 from, Vector3 dir, float min, float max)
        {
            dir.y = 0; dir.Normalize();
            for (float d = min; d <= max; d += 0.1f)
            {
                var p = from + dir * d;
                if (Physics.CheckCapsule(p + Vector3.up * 0.35f, p + Vector3.up * 1.5f, 0.27f, ~CharacterLayers.CharacterMask, QueryTriggerInteraction.Ignore))
                    continue;
                if (NavMesh.SamplePosition(p, out var hit, 0.2f, NavMesh.AllAreas)) return hit.position;
            }
            return Sample(from + dir * max, from + dir * max);
        }

        private static Vector3 Sample(Vector3 p, Vector3 fallback) =>
            NavMesh.SamplePosition(p, out var hit, 0.75f, NavMesh.AllAreas) ? hit.position : fallback;
    }
}
