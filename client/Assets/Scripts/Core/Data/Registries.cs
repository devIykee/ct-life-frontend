using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace CTLife.Core
{
    /// <summary>
    /// Every ID registry from contract section 2, parsed from the `ids` object of GET /v1/config
    /// (or from the bundled snapshot). Lookups never throw: unknown IDs return null and are logged once,
    /// and callers show a fallback (grey capsule, default sound).
    /// </summary>
    public sealed class Registries
    {
        public readonly Dictionary<string, VenueDef> Venues = new Dictionary<string, VenueDef>();
        public readonly List<VenueDef> VenueOrder = new List<VenueDef>();
        public readonly Dictionary<string, string> Districts = new Dictionary<string, string>();
        public readonly Dictionary<string, VenueSpots> Spots = new Dictionary<string, VenueSpots>();
        public readonly Dictionary<string, AnimDef> Anims = new Dictionary<string, AnimDef>();
        public readonly Dictionary<string, SfxDef> Sfx = new Dictionary<string, SfxDef>();
        public readonly Dictionary<string, ItemDef> Items = new Dictionary<string, ItemDef>();
        public readonly List<string> ItemSlots = new List<string>();
        public readonly Dictionary<string, ArchetypeDef> Archetypes = new Dictionary<string, ArchetypeDef>();
        public readonly Dictionary<string, ToastDef> Toasts = new Dictionary<string, ToastDef>();
        public readonly Dictionary<string, EventDef> Events = new Dictionary<string, EventDef>();
        public readonly Dictionary<string, TribeDef> Tribes = new Dictionary<string, TribeDef>();
        public readonly List<TribeDef> TribeOrder = new List<TribeDef>();
        public readonly List<SimpleDef> Traits = new List<SimpleDef>();
        public readonly List<SimpleDef> Dreams = new List<SimpleDef>();
        public readonly List<SimpleDef> Homes = new List<SimpleDef>();
        public readonly List<SimpleDef> Boards = new List<SimpleDef>();
        public readonly List<SimpleDef> ReportReasons = new List<SimpleDef>();
        public readonly Dictionary<string, MiniGameDef> MiniGames = new Dictionary<string, MiniGameDef>();
        public readonly Dictionary<string, ErrorDef> Errors = new Dictionary<string, ErrorDef>();
        public readonly Dictionary<string, NpcLineDef> NpcLines = new Dictionary<string, NpcLineDef>();
        public readonly Dictionary<string, List<string>> LinesByArchetype = new Dictionary<string, List<string>>();
        /// <summary>rc1 toasts map an id to a textId (`toast_drained` → `ui.toast.drained`); reverse lookup.</summary>
        public readonly Dictionary<string, string> ToastTextIds = new Dictionary<string, string>();

        public static Registries Parse(JToken ids, JToken config)
        {
            var r = new Registries();
            try { r.ParseIds(ids); } catch (Exception e) { Log.Exception("registries", e); }
            try { r.ParseNpcLines(config?["npc_lines"] ?? config?["npcLines"]); } catch (Exception e) { Log.Exception("registries", e); }
            return r;
        }

        /// <summary>
        /// Registry entries in either layout: `{items: [...]}` (stand-in) or `{&lt;name&gt;: [...]}` (backend contract
        /// rc1, e.g. `{venues: [...]}`, `{codes: [...]}`). Bare strings become `{id: "..."}`.
        /// </summary>
        private static IEnumerable<JToken> ItemsOf(JToken registry, params string[] keys)
        {
            JArray arr = registry?["items"] as JArray;
            if (arr == null && keys != null)
                foreach (var k in keys)
                    if (registry?[k] is JArray a) { arr = a; break; }
            if (arr == null) yield break;
            foreach (var i in arr)
                yield return i.Type == JTokenType.String ? new JObject { ["id"] = i.ToString() } : i;
        }

        private static List<string> StrList(JToken t)
        {
            var l = new List<string>();
            if (t is JArray a)
                foreach (var x in a) l.Add(x.ToString());
            else if (t != null && t.Type == JTokenType.String) l.Add(t.ToString());
            return l;
        }

        private static string Label(JToken t) => t.Str("name", t.Str("label", t.Str("id")));

        /// <summary>Venue district for the map and music. rc1 venues carry no district; FE fallback table.</summary>
        private static readonly Dictionary<string, string> FallbackDistricts = new Dictionary<string, string>
        {
            ["timeline_square"] = "timeline", ["spaces_studio"] = "timeline", ["discord_heights"] = "timeline",
            ["ratio_arena"] = "timeline", ["ct_jail"] = "timeline",
            ["terminal_cafe"] = "trenches", ["perp_pit"] = "trenches", ["rekt_clinic"] = "trenches",
            ["launchpad_tower"] = "trenches", ["prediction_pit"] = "trenches", ["the_trenches"] = "trenches",
            ["builder_hills"] = "builder_hills", ["audit_row"] = "builder_hills", ["vc_row"] = "builder_hills", ["hackathon_house"] = "builder_hills",
            ["conference_hall"] = "builder_hills",
            ["airdrop_farm"] = "airdrop_farm", ["ore_refinery"] = "airdrop_farm", ["airdrop_fields"] = "airdrop_farm", ["sybil_barn"] = "airdrop_farm",
            ["checker_kiosk"] = "airdrop_farm",
            ["dubai_marina"] = "dubai_marina", ["club_liquidity"] = "dubai_marina", ["cabal_lounge"] = "dubai_marina",
            ["yacht_club"] = "dubai_marina", ["penthouse_row"] = "dubai_marina",
        };

        private static readonly (string id, string name)[] FallbackDistrictNames =
        {
            ("timeline", "Timeline District"), ("trenches", "The Trenches"), ("builder_hills", "Builder Hills"),
            ("airdrop_farm", "Airdrop Farm"), ("dubai_marina", "Dubai Marina"),
        };

        private void ParseIds(JToken ids)
        {
            if (ids == null) return;

            var venues = ids["venues"];
            if (venues?["districts"] is JArray districts)
                foreach (var d in districts) Districts[d.Str("id")] = NameTable.Get("district." + d.Str("id"), d.Str("name"));
            foreach (var v in ItemsOf(venues, "venues"))
            {
                var id = v.Str("id");
                var def = new VenueDef
                {
                    id = id, name = NameTable.Get("venue." + id, Label(v)),   // one table for every name (names.json)
                    districtId = v.Str("districtId") ?? (FallbackDistricts.TryGetValue(id ?? "", out var dd) ? dd : "timeline"),
                    sceneKey = v.Str("sceneKey", v.Str("scene_key")), phase = v.Int("phase", 1),
                    maxPlayers = v.Int("maxPlayers", v.Int("maxClients", 50)),
                    isClub = v.Bool("isClub") || v.Str("kind") == "club",
                };
                Venues[def.id] = def;
                VenueOrder.Add(def);
            }
            if (Districts.Count == 0)
                foreach (var (id, name) in FallbackDistrictNames) Districts[id] = NameTable.Get("district." + id, name);

            if (ids["spots"]?["venues"] is JObject spotVenues)
            {
                foreach (var prop in spotVenues.Properties())
                {
                    var vs = new VenueSpots();
                    if (prop.Value["walkable"] is JArray rects)
                        foreach (var rr in rects)
                            vs.walkable.Add(new WalkRect { x0 = rr.Float("x0"), z0 = rr.Float("z0"), x1 = rr.Float("x1"), z1 = rr.Float("z1") });
                    var spots = prop.Value["spots"];
                    if (spots is JObject so)
                        foreach (var sp in so.Properties()) AddSpot(vs, prop.Name, sp.Name, sp.Value);
                    else if (spots is JArray sa)
                        foreach (var sp in sa) AddSpot(vs, prop.Name, sp.Str("id"), sp);
                    Spots[prop.Name] = vs;
                }
            }

            var animMeta = ids["anims"]?["animMeta"] as JObject;
            foreach (var a in ItemsOf(ids["anims"], "anims"))
            {
                var id = a.Str("id");
                var m = animMeta?[id] ?? a;
                var kind = m.Str("kind") ?? (id.StartsWith("loco_") ? AnimKind.Loco : id.StartsWith("act_") ? AnimKind.Act
                    : id.StartsWith("react_") ? AnimKind.React : AnimKind.Emote);
                Anims[id] = new AnimDef { id = id, kind = kind, loop = m.Bool("loop", kind == AnimKind.Loco || kind == AnimKind.Act), durationMs = m.Int("durationMs", 2000) };
            }

            var sfxMeta = ids["sfx"]?["sfxMeta"] as JObject;
            foreach (var x in ItemsOf(ids["sfx"], "sfx"))
                Sfx[x.Str("id")] = new SfxDef { id = x.Str("id"), category = (sfxMeta?[x.Str("id")] ?? x).Str("category") };

            var items = ids["items"];
            ItemSlots.AddRange(StrList(items?["slots"]));
            foreach (var i in ItemsOf(items, "items"))
            {
                var shop = i["shop"];
                bool shopObj = shop != null && shop.Type == JTokenType.Object;
                Items[i.Str("id")] = new ItemDef
                {
                    id = i.Str("id"), slot = i.Str("slot"), name = Label(i), prefabKey = i.Str("prefabKey", i.Str("prefab_key")),
                    starter = i.Bool("starter", i.Long("cashCents", 1) == 0), phase = i.Int("phase", 1),
                    shopCurrency = shopObj ? shop.Str("currency") : (i.Long("cashCents") > 0 ? "cash" : null),
                    priceCents = shopObj ? shop.Long("priceCents") : i.Long("cashCents"),
                };
                if (!ItemSlots.Contains(i.Str("slot")) && i.Str("slot") != null) ItemSlots.Add(i.Str("slot"));
            }

            foreach (var a in ItemsOf(ids["archetypes"], "archetypes"))
            {
                var homes = StrList(a["homeVenueIds"]);
                if (homes.Count == 0 && a.Str("venueId") != null) homes.Add(a.Str("venueId"));
                Archetypes[a.Str("id")] = new ArchetypeDef
                {
                    id = a.Str("id"), name = NameTable.Get("npc." + a.Str("id"), NameTable.Apply(Label(a))), phase = a.Int("phase", 1), conditional = a.Str("conditional"),
                    maxPerInstance = a.Int("maxPerInstance", 1), homeVenueIds = homes,
                };
            }

            foreach (var t in ItemsOf(ids["toasts"], "toasts"))
            {
                Toasts[t.Str("id")] = new ToastDef { id = t.Str("id"), @params = StrList(t["params"]) };
                if (t.Str("textId") != null) ToastTextIds[t.Str("textId")] = t.Str("id");
            }

            foreach (var e in ItemsOf(ids["events"], "events"))
                Events[e.Str("id")] = new EventDef { id = e.Str("id"), kind = e.Str("kind"), @params = StrList(e["params"]) };

            foreach (var t in ItemsOf(ids["tribes"], "tribes"))
            {
                var perk = t["perk"];
                var def = new TribeDef
                {
                    id = t.Str("id"), name = Label(t), homeVenueId = t.Str("homeVenueId", t.Str("homeTurf")),
                    perkKind = perk != null && perk.Type == JTokenType.Object ? perk.Str("kind") : null,
                    perkValue = perk != null && perk.Type == JTokenType.Object ? perk["value"]?.Value<double>() ?? 0 : 0,
                    perkText = perk != null && perk.Type == JTokenType.String ? perk.ToString() : null,
                    signatureEmoteId = t.Str("signatureEmoteId", t.Str("emoteId")),
                };
                Tribes[def.id] = def;
                TribeOrder.Add(def);
            }

            Simple(ids["traits"], Traits, "traits");
            Simple(ids["dreams"], Dreams, "dreams");
            Simple(ids["homes"], Homes, "homes");
            Simple(ids["boards"], Boards, "boards");
            Simple(ids["report_reasons"], ReportReasons, "reportReasons");

            foreach (var m in ItemsOf(ids["mini_games"], "miniGames"))
                MiniGames[m.Str("id")] = new MiniGameDef
                {
                    id = m.Str("id"), careerId = m.Str("careerId"), name = m.Str("name", m.Str("label", m.Str("id"))),
                    maxScore = m.Int("maxScore", 1000), durationSec = m.Int("durationSec", 45),
                };

            foreach (var e in ItemsOf(ids["errors"], "codes"))
            {
                var code = e.Str("id", e.Str("code"));
                if (code != null) Errors[code] = new ErrorDef { id = code, group = e.Str("group"), message = e.Str("message"), httpStatus = e.Int("httpStatus") };
            }
        }

        /// <summary>Spot ids may be venue-local (`center`) or global (`timeline_square.center`, rc1). Both resolve.</summary>
        private static void AddSpot(VenueSpots vs, string venueId, string id, JToken v)
        {
            if (string.IsNullOrEmpty(id)) return;
            var local = id.StartsWith(venueId + ".") ? id.Substring(venueId.Length + 1) : id;
            var def = new SpotDef
            {
                id = local, wireId = id, kind = v.Str("kind", "stand"), anim = v.Str("anim"),
                x = v.Float("x"), z = v.Float("z"), rotY = v.Float("rotY"), hasPosition = v["x"] != null,
            };
            vs.spots[local] = def;
            if (id != local) vs.spots[id] = def;
        }

        private static void Simple(JToken registry, List<SimpleDef> into, string key)
        {
            foreach (var x in ItemsOf(registry, key))
                into.Add(new SimpleDef { id = x.Str("id"), name = Label(x), description = x.Str("description", x.Str("effect")), tier = x.Int("tier") });
        }

        private void ParseNpcLines(JToken npcLines)
        {
            if (!(npcLines is JObject root)) return;
            // Stand-in: {archetypes: {arch: [{id, text, slots}]}}; rc1: {arch: ["text", ...]} with no line ids.
            var archetypes = root["archetypes"] as JObject ?? root;
            foreach (var prop in archetypes.Properties())
            {
                if (!(prop.Value is JArray lines)) continue;
                var texts = new List<string>();
                int i = 0;
                foreach (var l in lines)
                {
                    var def = l.Type == JTokenType.String
                        ? new NpcLineDef { id = prop.Name + "." + i, archetypeId = prop.Name, text = l.ToString() }
                        : new NpcLineDef { id = l.Str("id"), archetypeId = prop.Name, text = l.Str("text", ""), trigger = l.Str("trigger"), slots = StrList(l["slots"]) };
                    if (!string.IsNullOrEmpty(def.id)) NpcLines[def.id] = def;
                    texts.Add(def.text);
                    i++;
                }
                LinesByArchetype[prop.Name] = texts;
            }
        }

        // ---- lookups with the contract's unknown-ID rule ----

        public VenueDef Venue(string id) => Find(Venues, "venues", id);
        public VenueSpots SpotsFor(string venueId) => Find(Spots, "spots", venueId);
        public AnimDef Anim(string id) => Find(Anims, "anims", id);
        public ItemDef Item(string id) => Find(Items, "items", id);
        public ArchetypeDef Archetype(string id) => Find(Archetypes, "archetypes", id);
        public TribeDef Tribe(string id) => Find(Tribes, "tribes", id);

        private static T Find<T>(Dictionary<string, T> map, string registry, string id) where T : class
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (map.TryGetValue(id, out var v)) return v;
            Log.UnknownId(registry, id);
            return null;
        }

        /// <summary>Fills a line's {slots} with lineParams. Missing slots stay visible as {slot}.</summary>
        public string FormatLine(string lineId, IReadOnlyDictionary<string, string> parameters, string archetypeId = null)
        {
            if (string.IsNullOrEmpty(lineId)) return null;
            if (NpcLines.TryGetValue(lineId, out var line)) return Fill(line.text, parameters);
            // rc1: the NPC brain sends named line ids (crier_long_liquidation…) but npc_lines.json has unnamed lines.
            // Pick a stable line from the archetype's list so the NPC still talks; logged once as a contract gap.
            Log.UnknownId("npc_lines", lineId);
            if (archetypeId != null && LinesByArchetype.TryGetValue(archetypeId, out var list) && list.Count > 0)
            {
                uint h = 2166136261;
                foreach (var ch in lineId) h = (h ^ ch) * 16777619;
                return Fill(list[(int)(h % (uint)list.Count)], parameters);
            }
            return null;
        }

        public static string Fill(string template, IReadOnlyDictionary<string, string> parameters)
        {
            if (string.IsNullOrEmpty(template) || parameters == null) return template;
            var s = template;
            foreach (var kv in parameters)
                s = s.Replace("{" + kv.Key + "}", kv.Value ?? "");
            return s;
        }
    }
}
