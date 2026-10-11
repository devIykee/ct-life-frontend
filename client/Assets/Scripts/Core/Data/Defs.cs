using System.Collections.Generic;

namespace CTLife.Core
{
    // Plain registry records parsed from GET /v1/config (`ids.*`, `config.*`). Hand-written instead of the generated
    // REST models because the registries are free-form JSON objects in the OpenAPI schema.

    public sealed class VenueDef
    {
        public string id, name, districtId, sceneKey;
        public int phase, maxPlayers;
        public bool isClub;
    }

    public struct WalkRect
    {
        public float x0, z0, x1, z1;

        public bool Contains(float x, float z) =>
            x >= System.Math.Min(x0, x1) && x <= System.Math.Max(x0, x1) &&
            z >= System.Math.Min(z0, z1) && z <= System.Math.Max(z0, z1);
    }

    public sealed class SpotDef
    {
        /// <summary>Venue-local id (`center`).</summary>
        public string id;
        /// <summary>The id exactly as the registry/server uses it (rc1: global `timeline_square.center`); send this in `sit`.</summary>
        public string wireId;
        public string kind, anim;
        public float x, z, rotY;
        public bool hasPosition;
        public bool Claimable => !string.IsNullOrEmpty(anim) || kind == "seat" || kind == "terminal";
    }

    public sealed class VenueSpots
    {
        public readonly List<WalkRect> walkable = new List<WalkRect>();
        public readonly Dictionary<string, SpotDef> spots = new Dictionary<string, SpotDef>();

        public bool IsWalkable(float x, float z)
        {
            if (walkable.Count == 0) return true;
            foreach (var r in walkable)
                if (r.Contains(x, z)) return true;
            return false;
        }

        public SpotDef Entrance => spots.TryGetValue("entrance", out var s) ? s : null;
        public SpotDef Exit => spots.TryGetValue("exit", out var s) ? s : Entrance;
    }

    public static class AnimKind
    {
        public const string Loco = "loco", Act = "act", Emote = "emote", React = "react";
    }

    public sealed class AnimDef
    {
        public string id, kind;
        public bool loop;
        public int durationMs;
    }

    public sealed class SfxDef
    {
        public string id, category;
    }

    public sealed class ItemDef
    {
        public string id, slot, name, prefabKey, shopCurrency;
        public bool starter;
        public int phase;
        public long priceCents;
    }

    public sealed class ArchetypeDef
    {
        public string id, name, conditional;
        public int phase, maxPerInstance;
        public List<string> homeVenueIds = new List<string>();
    }

    public sealed class ToastDef
    {
        public string id;
        public List<string> @params = new List<string>();
    }

    public sealed class EventDef
    {
        public string id, kind;
        public List<string> @params = new List<string>();
    }

    public sealed class TribeDef
    {
        public string id, name, homeVenueId, perkKind, signatureEmoteId, perkText;
        public double perkValue;
    }

    /// <summary>For small registries that only need an id and a display name (traits, dreams, homes, boards, reasons).</summary>
    public sealed class SimpleDef
    {
        public string id, name, description;
        public int tier;
    }

    public sealed class MiniGameDef
    {
        public string id, careerId, name;
        public int maxScore, durationSec;
    }

    public sealed class NpcLineDef
    {
        public string id, archetypeId, text, trigger;
        public List<string> slots = new List<string>();
    }

    public sealed class ErrorDef
    {
        public string id, group, message;
        public int httpStatus;
    }
}
