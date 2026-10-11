using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using CTLife.Realtime;

namespace CTLife.Net
{
    /// <summary>
    /// Copies a room's live schema fields, read by name, into the contract's generated state classes.
    ///
    /// Why by name: the Colyseus C# SDK binds a typed client schema only when every field name, index and type
    /// matches the server's reflection exactly (SchemaSerializer.CompareTypes). Any additive server field, a reorder,
    /// or `number` vs `float32` silently disables decoding ("Local schema mismatch"). Backend main @ 9f3e5ef serves
    /// `server/realtime/schemas.ts`, which differs from `contract/realtime/states.ts` in order, types and names
    /// (`archetype` vs `archetypeId`, no `look`), feedback item 47. Decoding with <c>DynamicSchema</c> and mapping by
    /// name keeps the client working across additive contract changes and tolerates both shapes.
    /// </summary>
    public static class StateMapper
    {
        public static void ApplyPlayer(Func<string, object> get, PlayerState p)
        {
            p.userId = Str(get("userId"), p.userId);
            p.name = Str(get("name"), p.name);
            p.skrName = Str(get("skrName"), p.skrName);
            p.tribeId = Str(get("tribeId"), p.tribeId);
            p.isSeeker = Bool(get("isSeeker"), p.isSeeker);
            p.minerTier = Str(get("minerTier"), p.minerTier);
            var look = Strings(get("look"));
            if (look != null && !SameItems(p.look, look)) p.look = new Colyseus.Schema.ArraySchema<string>(look);
            p.x = Num(get("x"), p.x);
            p.z = Num(get("z"), p.z);
            p.rotY = Num(get("rotY"), p.rotY);
            p.anim = Str(get("anim"), p.anim);
            p.emoteId = Str(get("emoteId"), p.emoteId);
            p.emoteAt = Num(get("emoteAt"), p.emoteAt);
            p.spotId = Str(get("spotId"), p.spotId);
            p.talking = Bool(get("talking"), p.talking);
        }

        public static void ApplyNpc(Func<string, object> get, NpcState n)
        {
            n.archetypeId = Str(get("archetypeId") ?? get("archetype"), n.archetypeId);
            n.state = Str(get("state"), n.state);
            n.spotId = Str(get("spotId"), n.spotId);
            n.animId = Str(get("animId"), n.animId);
            n.targetUserId = Str(get("targetUserId"), n.targetUserId);
            n.lineId = Str(get("lineId"), n.lineId);
            var lineParams = Pairs(get("lineParams"));
            if (lineParams != null)
            {
                var map = new Colyseus.Schema.MapSchema<string>();
                foreach (var kv in lineParams) map[kv.Key] = Str(kv.Value, "");
                n.lineParams = map;
            }
            n.emoteId = Str(get("emoteId"), n.emoteId);
            n.startedAt = Num(get("startedAt"), n.startedAt);
        }

        public static void ApplyCoin(Func<string, object> get, CoinState c)
        {
            c.coinId = Str(get("coinId"), c.coinId);
            c.ticker = Str(get("ticker"), c.ticker);
            c.name = Str(get("name"), c.name);
            c.stage = Str(get("stage"), c.stage);
            c.priceMicros = Long(get("priceMicros"), c.priceMicros);
            c.mcapCents = Long(get("mcapCents"), c.mcapCents);
            c.vol5mCents = Long(get("vol5mCents"), c.vol5mCents);
            c.change5mBps = (int)Long(get("change5mBps"), c.change5mBps);
            c.holders = (int)Long(get("holders"), c.holders);
            c.launchedAt = Long(get("launchedAt"), c.launchedAt);
        }

        private static bool SameItems(IReadOnlyList<string> current, List<string> next)
        {
            if (current == null || current.Count != next.Count) return false;
            for (int i = 0; i < next.Count; i++) if (current[i] != next[i]) return false;
            return true;
        }

        public static string Str(object v, string fallback) => v == null ? fallback : v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);

        public static bool Bool(object v, bool fallback)
        {
            switch (v)
            {
                case null: return fallback;
                case bool b: return b;
                case string s: return bool.TryParse(s, out var parsed) ? parsed : fallback;
                default:
                    try { return Convert.ToDouble(v, CultureInfo.InvariantCulture) != 0; }
                    catch (Exception) { return fallback; }
            }
        }

        public static double Num(object v, double fallback)
        {
            if (v == null || v is string) return v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : fallback;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }

        public static long Long(object v, long fallback)
        {
            if (v == null) return fallback;
            try { return Convert.ToInt64(Num(v, fallback)); }
            catch (Exception) { return fallback; }
        }

        /// <summary>Key/value pairs from an IDictionary, a MapSchema (IReadOnlyDictionary) or KeyValuePair items.</summary>
        public static List<KeyValuePair<string, object>> Pairs(object v)
        {
            if (v == null || v is string) return null;
            var list = new List<KeyValuePair<string, object>>();
            if (v is IDictionary dict)
            {
                foreach (DictionaryEntry kv in dict) list.Add(new KeyValuePair<string, object>(Convert.ToString(kv.Key, CultureInfo.InvariantCulture), kv.Value));
                return list;
            }
            if (!(v is IEnumerable items)) return null;
            foreach (var item in items)
            {
                if (item == null) continue;
                var t = item.GetType();
                if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(KeyValuePair<,>)) return null;
                var key = t.GetProperty("Key")?.GetValue(item);
                list.Add(new KeyValuePair<string, object>(Convert.ToString(key, CultureInfo.InvariantCulture), t.GetProperty("Value")?.GetValue(item)));
            }
            return list;
        }

        /// <summary>A string list from an ArraySchema, array or any non-string enumerable; null when absent.</summary>
        public static List<string> Strings(object v)
        {
            if (v == null || v is string) return null;
            if (v is IDictionary dict)
            {
                // ArraySchema exposes items keyed by index on some SDK versions.
                var ordered = new SortedDictionary<int, string>();
                foreach (DictionaryEntry kv in dict)
                    if (int.TryParse(Convert.ToString(kv.Key, CultureInfo.InvariantCulture), out var i)) ordered[i] = Str(kv.Value, "");
                return new List<string>(ordered.Values);
            }
            if (!(v is IEnumerable items)) return null;
            var list = new List<string>();
            foreach (var item in items) list.Add(Str(item, ""));
            return list;
        }
    }
}
