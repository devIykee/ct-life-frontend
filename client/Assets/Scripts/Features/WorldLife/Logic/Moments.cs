using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace CTLife.WorldLife
{
    /// <summary>A city `event` message as plain data (contract `EventMessage {eventId, params, at}`).</summary>
    public sealed class CityEvent
    {
        public string EventId;
        public Dictionary<string, string> Params = new Dictionary<string, string>();
        public double At;

        /// <summary>Order-independent identity (same rule as UI IncidentFeed: eventId|at|len-prefixed sorted params).</summary>
        public string Key
        {
            get
            {
                var sb = new StringBuilder(EventId ?? "").Append('|').Append(At.ToString("R", CultureInfo.InvariantCulture));
                if (Params == null) return sb.ToString();
                var keys = new List<string>(Params.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var k in keys)
                {
                    var v = Params[k] ?? "";
                    sb.Append('|').Append(k.Length).Append(':').Append(k).Append(v.Length).Append(':').Append(v);
                }
                return sb.ToString();
            }
        }
    }

    public struct CrowdCue
    {
        public string NpcId;
        public string AnimId;
        public float Delay;
    }

    /// <summary>An NPC that could react (only NPCs: player avatars are never animated by someone else's event).</summary>
    public sealed class CrowdMember
    {
        public string NpcId;
        public string ArchetypeId;
        public string State;
        public bool Hidden;
    }

    /// <summary>The world-side presentation of one city event in the current venue.</summary>
    public sealed class WorldMomentPlan
    {
        public string MomentId;
        public string EventId;
        public string EventKey;
        /// <summary>Global stinger for the audio lane (IncidentPresentation's card also plays one today; see world-life.md).</summary>
        public string StingerSfxId;
        /// <summary>Positional effect in this venue: siren | alarm | carpet_yank | sniper_swarm | furnace_flash | null.</summary>
        public string VenueFx;
        /// <summary>Positional sfx at the effect (played through AudioManager.PlayAt when no audio hook is registered).</summary>
        public string VenueSfxId;
        public string CrierText;
        public string TimelineText;
        /// <summary>-1 red, 0 neutral, +1 green (evt_mood_change only).</summary>
        public int MoodDirection;
        public readonly List<CrowdCue> Crowd = new List<CrowdCue>();
        /// <summary>Fresh enough (≤ 30 s by the server clock) to stage in the world; stale events only feed the Timeline.</summary>
        public bool Live;
    }

    /// <summary>
    /// Event → world-moment mapping. Data-driven by `moments` in npc_presentation.json; pure logic so it is tested
    /// without Unity. Cosmetic choices (which NPC reacts, which anim, delay) hash the event key, so every client in the
    /// venue stages the same moment.
    /// </summary>
    public static class EventMoments
    {
        public const double FreshMs = 30_000;
        public const double FutureSkewMs = 60_000;

        public static readonly string[] ContractEvents =
            { "evt_liquidation", "evt_rug", "evt_coin_launch", "evt_mood_change", "evt_exploit", "evt_ore_motherlode" };

        /// <param name="nowMs">Server time (ServerClock.NowMs).</param>
        /// <param name="synced">Without a server clock nothing is staged live (same rule as the incident card).</param>
        public static WorldMomentPlan Plan(CityEvent e, string venueId, JToken moments, NpcLineBook lines,
            IReadOnlyList<CrowdMember> crowd, double nowMs, bool synced)
        {
            if (e == null || string.IsNullOrEmpty(e.EventId) || double.IsNaN(e.At) || double.IsInfinity(e.At) || e.At < 0) return null;
            var cfg = moments?[e.EventId] as JObject;
            if (cfg == null) return null;
            if (synced && e.At > nowMs + FutureSkewMs) return null;
            var plan = new WorldMomentPlan
            {
                MomentId = WireText.Str(cfg, "moment", e.EventId),
                EventId = e.EventId,
                EventKey = e.Key,
                StingerSfxId = WireText.Str(cfg, "stinger"),
                Live = synced && nowMs - e.At <= FreshMs,
            };
            var p = e.Params ?? new Dictionary<string, string>();

            if (e.EventId == "evt_mood_change") plan.MoodDirection = MoodDirection(p);

            if (cfg["venueFx"] is JObject fx && !string.IsNullOrEmpty(venueId))
            {
                plan.VenueFx = WireText.Str(fx, venueId) ?? WireText.Str(fx, "*");
                if (plan.VenueFx != null && cfg["venueSfx"] is JObject vs) plan.VenueSfxId = WireText.Str(vs, plan.VenueFx);
            }

            var crier = lines?.ResolveEvent(MoodKey(e.EventId, plan.MoodDirection), p, plan.EventKey)
                        ?? lines?.ResolveEvent(e.EventId, p, plan.EventKey);
            plan.CrierText = crier?.Text;
            plan.TimelineText = plan.CrierText ?? WireText.Str(cfg, "label");

            string crowdKey = plan.MoodDirection > 0 ? "crowdUp" : plan.MoodDirection < 0 ? "crowdDown" : "crowd";
            var anims = WireText.Strs(cfg[crowdKey]);
            if (anims.Count == 0) anims = WireText.Strs(cfg["crowd"]);
            int max = (int)WireText.Num(cfg, "crowdMax", 4);
            double share = WireText.Num(cfg, "crowdShare", 0.6);
            if (anims.Count > 0 && crowd != null && max > 0)
            {
                var sorted = new List<CrowdMember>(crowd);
                sorted.Sort((a, b) => string.CompareOrdinal(a.NpcId, b.NpcId));
                var skip = new HashSet<string>(WireText.Strs(cfg["skipArchetypes"]), StringComparer.Ordinal);
                foreach (var m in sorted)
                {
                    if (plan.Crowd.Count >= max) break;
                    if (m == null || m.Hidden || string.IsNullOrEmpty(m.NpcId)) continue;
                    if (skip.Contains(m.ArchetypeId ?? "")) continue;
                    // Never interrupt walking, talking or an authored reaction.
                    if (m.State != "idle" && m.State != "act" && m.State != "follow") continue;
                    var seed = plan.EventKey + "|" + m.NpcId;
                    if (WireText.Hash(seed + "|in") % 1000 >= share * 1000) continue;
                    plan.Crowd.Add(new CrowdCue
                    {
                        NpcId = m.NpcId,
                        AnimId = anims[WireText.Pick(seed + "|anim", anims.Count)],
                        Delay = 0.15f + (WireText.Hash(seed + "|t") % 750) / 1000f,
                    });
                }
            }
            return plan;
        }

        private static string MoodKey(string eventId, int dir) =>
            eventId == "evt_mood_change" ? eventId + (dir > 0 ? ".up" : dir < 0 ? ".down" : "") : eventId;

        public static int MoodDirection(IReadOnlyDictionary<string, string> p)
        {
            if (p == null) return 0;
            if (p.TryGetValue("direction", out var d))
            {
                if (d == "up") return 1;
                if (d == "down") return -1;
            }
            foreach (var k in new[] { "moodBps", "bps", "mood" })
                if (p.TryGetValue(k, out var v) && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bps))
                    return bps > 0 ? 1 : bps < 0 ? -1 : 0;
            return 0;
        }
    }

    /// <summary>
    /// Town Crier "JUST IN" shouts for city events, staged client-side from the real `event` message. Backend 9f3e5ef never
    /// feeds events to the crier brain (`recentEvents: []`, `notify()` unused), so without this the crier never reacts.
    /// When the server starts sending its own crier event lines, a server line of the same category inside the hold
    /// window cancels the pending client shout, so the two never double up.
    /// </summary>
    public sealed class CrierQueue
    {
        public const float HoldSeconds = 1.2f;
        public const float GapSeconds = 4f;
        public const float TtlSeconds = 20f;
        public const int Capacity = 3;

        private sealed class Pending
        {
            public string Key, Category, Text;
            public float At;
        }

        private readonly List<Pending> _pending = new List<Pending>();
        private readonly HashSet<string> _done = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> _doneOrder = new Queue<string>();
        private float _lastShout = float.NegativeInfinity;
        private readonly Dictionary<string, float> _serverSaid = new Dictionary<string, float>(StringComparer.Ordinal);

        public int Count => _pending.Count;

        /// <summary>Server crier line ids → event category (`crier_rug` → evt_rug, …).</summary>
        public static string CategoryOfServerLine(string lineId)
        {
            switch (lineId)
            {
                case "crier_long_liquidation": case "crier_rekt": return "evt_liquidation";
                case "crier_rug": return "evt_rug";
                case "crier_new_launch": return "evt_coin_launch";
                case "crier_exploit": return "evt_exploit";
                case "crier_motherlode": return "evt_ore_motherlode";
                case "crier_mood_down": case "crier_peak_normies": return "evt_mood_change";
                default: return null;
            }
        }

        public bool Enqueue(string key, string category, string text, float now)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(key) || _done.Contains(key)) return false;
            foreach (var p in _pending) if (p.Key == key) return false;
            if (category != null && _serverSaid.TryGetValue(category, out var said) && now - said <= HoldSeconds + GapSeconds) return false;
            _pending.Add(new Pending { Key = key, Category = category, Text = text, At = now });
            while (_pending.Count > Capacity) _pending.RemoveAt(0); // drop the oldest headline
            return true;
        }

        /// <summary>The crier said a server line; cancel a pending client shout of the same category.</summary>
        public void ServerLine(string lineId, float now)
        {
            var cat = CategoryOfServerLine(lineId);
            _lastShout = now;
            if (cat == null) return;
            _serverSaid[cat] = now;
            _pending.RemoveAll(p => p.Category == cat);
        }

        /// <summary>The next shout that is due, or null. Holds each one briefly and spaces shouts out.</summary>
        public string Due(float now, out string key)
        {
            key = null;
            _pending.RemoveAll(p => now - p.At > TtlSeconds);
            if (_pending.Count == 0 || now - _lastShout < GapSeconds) return null;
            for (int i = _pending.Count - 1; i >= 0; i--) // newest first
            {
                var p = _pending[i];
                if (now - p.At < HoldSeconds) continue;
                _pending.RemoveAt(i);
                _lastShout = now;
                key = p.Key;
                _done.Add(p.Key);
                _doneOrder.Enqueue(p.Key);
                while (_doneOrder.Count > 64) _done.Remove(_doneOrder.Dequeue());
                return p.Text;
            }
            return null;
        }

        public void Clear() { _pending.Clear(); _serverSaid.Clear(); _lastShout = float.NegativeInfinity; }
    }

    public enum MoodBand { Neutral, Red, Green }

    /// <summary>Plain RGB 0..1 (no UnityEngine.Color in pure logic).</summary>
    public struct Rgb
    {
        public float R, G, B;
        public Rgb(float r, float g, float b) { R = r; G = g; B = b; }
        public override string ToString() => R.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                                             G.ToString("0.###", CultureInfo.InvariantCulture) + "," + B.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>Market Mood as the world sees it. Thresholds follow the spec: Doomposter at below −2% (−200 bps).</summary>
    public sealed class MoodModel
    {
        public const int RedBelowBps = -200;
        public const int GreenAboveBps = 200;
        public const int HysteresisBps = 50;

        public MoodBand Band { get; private set; } = MoodBand.Neutral;
        public int Bps { get; private set; }
        public bool HasValue { get; private set; }

        /// <summary>−1..+1, same scale as MusicDirector.SetMood (bps / 500, clamped).</summary>
        public float Normalized => Math.Max(-1f, Math.Min(1f, Bps / 500f));

        /// <returns>True when the band changed.</returns>
        public bool Update(int bps)
        {
            Bps = bps;
            HasValue = true;
            var next = Band;
            switch (Band)
            {
                case MoodBand.Red: if (bps > RedBelowBps + HysteresisBps) next = bps >= GreenAboveBps ? MoodBand.Green : MoodBand.Neutral; break;
                case MoodBand.Green: if (bps < GreenAboveBps - HysteresisBps) next = bps < RedBelowBps ? MoodBand.Red : MoodBand.Neutral; break;
                default: next = bps < RedBelowBps ? MoodBand.Red : bps >= GreenAboveBps ? MoodBand.Green : MoodBand.Neutral; break;
            }
            bool changed = next != Band;
            Band = next;
            return changed;
        }

        /// <summary>Tint the venue/lighting side may apply (multiply-style, near white; strength grows with |mood|).</summary>
        public Rgb Tint(out float strength)
        {
            float n = Normalized;
            strength = Math.Min(1f, Math.Abs(n));
            if (Band == MoodBand.Red || n < -0.4f) return new Rgb(1f, 0.78f, 0.78f);
            if (Band == MoodBand.Green || n > 0.4f) return new Rgb(0.86f, 1f, 0.88f);
            return new Rgb(1f, 1f, 1f);
        }
    }
}
