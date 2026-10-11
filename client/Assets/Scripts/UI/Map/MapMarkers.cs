using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.World;
using UnityEngine;

namespace CTLife.UI.Map
{
    public enum MarkerKind { Door, Desk, Mic, Player, Friend }

    public struct MapMarker
    {
        public MarkerKind Kind;
        public Vector2 Atlas;      // atlas metres
        public string Id, Label;
        /// <summary>Doors and friends get an edge arrow on the minimap when out of view.</summary>
        public bool OfInterest => Kind == MarkerKind.Door || Kind == MarkerKind.Friend;
    }

    /// <summary>
    /// Markers from real data only: the current venue's own anchors (the door you leave by, work desks, the stage mic) and
    /// the other players in the room (friends from the follow graph via <see cref="WorldLabels.IsFriend"/>). For venues you
    /// are not in, only the door from the registry's spots. No objective markers: the game has no objective system yet.
    /// </summary>
    public static class MapMarkers
    {
        public static void Live(VenueSession session, MapArea area, MapPlacement placement, List<MapMarker> into)
        {
            into.Clear();
            var venue = session != null ? session.Venue : null;
            if (venue == null || area == null || placement == null) return;
            foreach (var a in venue.Anchors)
            {
                if (a == null) continue;
                var kind = KindFor(a.kind, venue.LocalSpotId(a.spotId));
                if (kind == null) continue;
                var local = venue.ToLocal(a.transform.position);
                into.Add(new MapMarker { Kind = kind.Value, Atlas = MapMath.AtlasFromLocal(placement, local), Id = a.spotId });
            }
            foreach (var kv in session.Players)
            {
                var p = kv.Value;
                if (p == null) continue;
                string user = p.State?.userId;
                bool friend = !string.IsNullOrEmpty(user) && WorldLabels.IsFriend != null && WorldLabels.IsFriend(user);
                var local = venue.ToLocal(p.transform.position);
                into.Add(new MapMarker { Kind = friend ? MarkerKind.Friend : MarkerKind.Player, Atlas = MapMath.AtlasFromLocal(placement, local),
                    Id = user ?? kv.Key, Label = p.State?.name });
            }
        }

        /// <summary>Spot kind to marker (null = not drawn). Only the exit spot is the door; seats are too many to be useful.</summary>
        public static MarkerKind? KindFor(string spotKind, string localSpotId) => (spotKind ?? "").ToLowerInvariant() switch
        {
            "exit" => localSpotId == "exit" ? MarkerKind.Door : (MarkerKind?)null,
            "terminal" => MarkerKind.Desk,
            "stage" => MarkerKind.Mic,
            _ => null,
        };

        /// <summary>The door of a venue you are not in, from the registry spots (venue-local), placed on the island.</summary>
        public static bool RegistryDoor(string venueId, MapPlacement placement, out Vector2 atlas)
        {
            atlas = default;
            if (placement == null || !Services.TryGet<ConfigStore>(out var cfg)) return false;
            var spots = cfg.Registries.SpotsFor(venueId);
            if (spots == null) return false;
            foreach (var s in spots.spots.Values)
                if (s.kind == "exit" && (s.id == "exit" || s.id.EndsWith(".exit", StringComparison.Ordinal)) && s.hasPosition)
                {
                    atlas = MapMath.AtlasFromLocal(placement, new Vector2(s.x, s.z));
                    return true;
                }
            return false;
        }

        public static string Icon(MarkerKind k) => k switch
        {
            MarkerKind.Door => "door",
            MarkerKind.Desk => "laptop",
            MarkerKind.Mic => "microphone",
            _ => null,
        };
    }
}
