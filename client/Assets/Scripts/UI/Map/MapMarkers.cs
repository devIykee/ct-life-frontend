using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.World;
using UnityEngine;

namespace CTLife.UI.Map
{
    public enum MarkerKind { Door, Desk, Mic, Player, Friend, Pin }

    public struct MapMarker
    {
        public MarkerKind Kind;
        public Vector2 Atlas;      // atlas metres
        public string Id, Label;
        /// <summary>Doors, friends and the player's map pin get an edge arrow on the minimap when out of view.</summary>
        public bool OfInterest => Kind == MarkerKind.Door || Kind == MarkerKind.Friend || Kind == MarkerKind.Pin;
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
                // only an exit needs its local spot id (building it allocates; this runs every frame)
                var kind = KindFor(a.kind, string.Equals(a.kind, "exit", StringComparison.OrdinalIgnoreCase) ? venue.LocalSpotId(a.spotId) : null);
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

        /// <summary>The player's map pin (<see cref="Waypoint"/>), when one is set.</summary>
        public static void AddPin(List<MapMarker> into)
        {
            if (Waypoint.Active) into.Add(new MapMarker { Kind = MarkerKind.Pin, Atlas = Waypoint.Atlas, Id = "pin", Label = Waypoint.Label });
        }

        /// <summary>Spot kind to marker (null = not drawn). Only the exit spot is the door; seats are too many to be useful.</summary>
        public static MarkerKind? KindFor(string spotKind, string localSpotId)
        {
            // case-insensitive without a lowered copy (called for every anchor every frame by the minimap)
            if (string.Equals(spotKind, "exit", StringComparison.OrdinalIgnoreCase)) return localSpotId == "exit" ? MarkerKind.Door : (MarkerKind?)null;
            if (string.Equals(spotKind, "terminal", StringComparison.OrdinalIgnoreCase)) return MarkerKind.Desk;
            if (string.Equals(spotKind, "stage", StringComparison.OrdinalIgnoreCase)) return MarkerKind.Mic;
            return null;
        }

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

        /// <summary>The landmark icon of a venue on the island map (Icons8 fluent; a generic chat bubble, never a brand logo).</summary>
        public static string Landmark(string areaId) => areaId switch
        {
            "timeline_square" => "fountain", "spaces_studio" => "podcast", "discord_heights" => "chat", "ratio_arena" => "boxing",
            "ct_jail" => "prison", "conference_hall" => "conference-call", "hackathon_house" => "source-code", "audit_row" => "inspection",
            "vc_row" => "money-bag", "terminal_cafe" => "coffee", "perp_pit" => "combo-chart", "rekt_clinic" => "hospital",
            "launchpad_tower" => "rocket", "prediction_pit" => "dice", "airdrop_fields" => "gift", "ore_refinery" => "factory",
            "sybil_barn" => "farm-2", "checker_kiosk" => "approval", "yacht_club" => "yacht", "cabal_lounge" => "cocktail",
            "penthouse_row" => "skyscrapers",
            _ => areaId != null && areaId.StartsWith("home_", StringComparison.Ordinal) ? "home-page" : "place-marker",
        };

        public static string Icon(MarkerKind k) => k switch
        {
            MarkerKind.Door => "door",
            MarkerKind.Desk => "laptop",
            MarkerKind.Mic => "microphone",
            MarkerKind.Pin => "place-marker",
            _ => null,
        };
    }
}
