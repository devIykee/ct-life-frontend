using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife.Heat
{
    /// <summary>Sleuth heat levels (backend #17, docs/HEAT.md): cool 0..9, warm 10..34, hot 35..69, wanted 70+.</summary>
    public enum HeatLevel { Cool, Warm, Hot, Wanted }

    /// <summary>GET /v1/heat/me: heat, level, when it drops, the active pursuit and the CT Jail stay.</summary>
    public sealed class HeatMe
    {
        public double Heat;
        public HeatLevel Level;
        public long DecayAtMs, WantedUntilMs;
        public int WarmAt = 10, HotAt = 35, WantedAt = 70;
        public PursuitInfo Pursuit;
        public JailInfo Jail;

        public int Stars => HeatRules.Stars(Heat);

        public static HeatMe Parse(JToken t)
        {
            if (!(t is JObject o)) return null;
            var me = new HeatMe
            {
                Heat = Math.Max(0, Math.Min(100, CityWire.DoubleOr(o["heat"], 0))),
                Level = HeatRules.ParseLevel(CityWire.Str(o["level"])),
                DecayAtMs = EventWire.Ms(o["decayAt"]),
                WantedUntilMs = EventWire.Ms(o["wantedUntil"]),
            };
            if (o["levels"] is JObject lv)
            {
                me.WarmAt = CityWire.IntOr(lv["warm"], 10); me.HotAt = CityWire.IntOr(lv["hot"], 35); me.WantedAt = CityWire.IntOr(lv["wanted"], 70);
            }
            if (o["pursuit"] is JObject p) me.Pursuit = PursuitInfo.Parse(p, null);
            if (o["jail"] is JObject j)
            {
                me.Jail = new JailInfo
                {
                    VenueId = HeatRules.SafeVenue(CityWire.Str(j["venueId"])),
                    JailedAtMs = EventWire.Ms(j["jailedAt"]),
                    UntilMs = EventWire.Ms(j["until"]),
                    BailCents = Math.Max(0, CityWire.LongOr(j["bailCents"], 0)),
                };
                if (me.Jail.UntilMs <= 0) me.Jail = null;
            }
            return me;
        }
    }

    /// <summary>One police pursuit: from /heat/me, or the `police_pursuit` message (city room to the target, street room to bystanders).</summary>
    public sealed class PursuitInfo
    {
        public string PursuitId, TargetUserId;
        public int Units = 1;
        public double Heat;
        public long StartedAtMs, ExpiresAtMs;
        public float EscapeRadiusM = 200f, EscapeSeconds = 60f;

        public static PursuitInfo Parse(JToken t, string targetFallback)
        {
            if (!(t is JObject o)) return null;
            var id = CityWire.Str(o["pursuitId"]);
            if (!HeatRules.SafeId(id)) return null;
            return new PursuitInfo
            {
                PursuitId = id,
                TargetUserId = CityWire.Str(o["targetUserId"]) ?? targetFallback,
                Units = Math.Max(1, Math.Min(4, CityWire.IntOr(o["units"], 1))),
                Heat = CityWire.DoubleOr(o["heat"], 0),
                StartedAtMs = EventWire.Ms(o["startedAt"]),
                ExpiresAtMs = EventWire.Ms(o["expiresAt"]),
                EscapeRadiusM = (float)Math.Max(10, CityWire.DoubleOr(o["escapeRadiusM"], 200)),
                EscapeSeconds = (float)Math.Max(1, CityWire.DoubleOr(o["escapeSeconds"], 60)),
            };
        }
    }

    public sealed class JailInfo
    {
        public string VenueId = HeatRules.JailVenue;
        public long JailedAtMs, UntilMs, BailCents;
    }

    /// <summary>A realtime heat message, decoded (police_pursuit, police_pursuit_end, jailed, released, vehicle_stolen, vehicle_returned).</summary>
    public sealed class HeatSignal
    {
        public string Type;
        public PursuitInfo Pursuit;          // police_pursuit
        public string PursuitId, Outcome;    // police_pursuit_end: caught | escaped
        public double Heat;
        public JailInfo Jail;                // jailed
        public string Reason;                // released (timer | bail), pursuit end reason, vehicle_returned reason
        public string VehicleId, Kind, By;   // vehicle_stolen / vehicle_returned

        public static readonly string[] Types = { "police_pursuit", "police_pursuit_end", "jailed", "released", "vehicle_stolen", "vehicle_returned" };

        public static HeatSignal From(string type, object payload)
        {
            if (Array.IndexOf(Types, type) < 0) return null;
            JToken t;
            try { t = payload is JToken jt ? jt : JToken.FromObject(payload ?? new Dictionary<string, object>()); }
            catch (Exception) { return null; }
            if (!(t is JObject o)) return null;
            var s = new HeatSignal { Type = type, Heat = CityWire.DoubleOr(o["heat"], 0), Reason = CityWire.Str(o["reason"]) };
            switch (type)
            {
                case "police_pursuit":
                    s.Pursuit = PursuitInfo.Parse(o, null);
                    if (s.Pursuit == null) return null;
                    s.PursuitId = s.Pursuit.PursuitId;
                    break;
                case "police_pursuit_end":
                    s.PursuitId = CityWire.Str(o["pursuitId"]);
                    if (!HeatRules.SafeId(s.PursuitId)) return null;
                    s.Outcome = CityWire.Str(o["outcome"]) == "caught" ? "caught" : "escaped";
                    break;
                case "jailed":
                    s.Jail = new JailInfo
                    {
                        VenueId = HeatRules.SafeVenue(CityWire.Str(o["venueId"])),
                        JailedAtMs = EventWire.Ms(o["at"]),
                        UntilMs = EventWire.Ms(o["until"]),
                        BailCents = Math.Max(0, CityWire.LongOr(o["bailCents"], 0)),
                    };
                    if (s.Jail.UntilMs <= 0) return null;
                    break;
                case "vehicle_stolen":
                case "vehicle_returned":
                    s.VehicleId = CityWire.Str(o["vehicleId"]);
                    s.Kind = CityWire.Str(o["kind"]);
                    s.By = CityWire.Str(o["by"]);
                    break;
            }
            return s;
        }
    }

    public static class HeatRules
    {
        public const string JailVenue = "ct_jail";
        public const string TrafficPrefix = "traffic_";
        /// <summary>Steal heat (docs/HEAT.md, [Inference] on the server): an NPC car, a player's parked vehicle.</summary>
        public const int StealNpcHeat = 8, StealPlayerHeat = 15;
        /// <summary>A cruiser this close to the target for <see cref="ContactSeconds"/> reports the catch.</summary>
        public const float ContactMetres = 4.5f, ContactSeconds = 2f;
        /// <summary>A cruiser stopped this close (blocked by traffic or a post) makes the arrest on foot.</summary>
        public const float FootArrestMetres = 12f;
        /// <summary>The server believes a catch only from 10 s into the pursuit.</summary>
        public const float CatchMinAgeSeconds = 10.5f;

        /// <summary>The HeatPlate's stars: none while cool, then one per 20 heat (warm 1-2, hot 2-4, wanted 4-5).</summary>
        public static int Stars(double heat)
        {
            if (heat < 10) return 0;
            return Math.Max(1, Math.Min(5, (int)Math.Ceiling(heat / 20.0)));
        }

        public static HeatLevel ParseLevel(string s) =>
            s == "warm" ? HeatLevel.Warm : s == "hot" ? HeatLevel.Hot : s == "wanted" ? HeatLevel.Wanted : HeatLevel.Cool;

        public static HeatLevel LevelOf(double heat) => heat >= 70 ? HeatLevel.Wanted : heat >= 35 ? HeatLevel.Hot : heat >= 10 ? HeatLevel.Warm : HeatLevel.Cool;

        public static bool SafeId(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 64) return false;
            foreach (char c in s) if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_')) return false;
            return true;
        }

        public static string SafeVenue(string s) => SafeId(s) ? s : JailVenue;

        /// <summary>A client-chosen id for a stolen NPC traffic car (the server only sees it on entry).</summary>
        public static string TrafficId(System.Random rng)
        {
            const string hex = "0123456789abcdef";
            var c = new char[12];
            for (int i = 0; i < c.Length; i++) c[i] = hex[rng.Next(16)];
            return TrafficPrefix + new string(c);
        }

        /// <summary>The countdown on the jail panel: "4:05", "0:09".</summary>
        public static string Clock(long msLeft) => EventWire.Clock(msLeft);

        /// <summary>403 JAILED details: {until, bailCents}.</summary>
        public static JailInfo FromJailedError(JToken details)
        {
            if (!(details is JObject o)) return null;
            long until = EventWire.Ms(o["until"]);
            if (until <= 0) return null;
            return new JailInfo { UntilMs = until, BailCents = Math.Max(0, CityWire.LongOr(o["bailCents"], 0)) };
        }
    }
}
