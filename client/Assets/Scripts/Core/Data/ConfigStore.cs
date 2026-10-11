using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.Core
{
    /// <summary>
    /// GET /v1/config payload `{version, minClientVersion, contractVersion, ids{}, config{}}`, cached by ETag
    /// (contract section 7). Load order: disk cache → bundled snapshot (Resources/Contract/config_snapshot.json),
    /// then the network refresh replaces it. The client never hardcodes a number that exists in config.
    /// </summary>
    public sealed class ConfigStore
    {
        private const string CacheFile = "config_cache.json";
        private const string EtagFile = "config_cache.etag";

        public JToken Root { get; private set; }
        public Registries Registries { get; private set; } = new Registries();
        public string ETag { get; private set; }
        /// <summary>A full config response has been validated in this process; disk caches alone do not grant creator IDs.</summary>
        public bool HasNetworkValidation { get; private set; }
        public string Version => Root.Str("version", "0.0.0");
        public string MinClientVersion => Root.Str("minClientVersion");
        public bool IsLoaded => Root != null;
        public string Source { get; private set; } = "none";

        public event Action Changed;

        private readonly System.Collections.Generic.HashSet<string> _degraded =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Features the server reports as temporarily degraded (additive contract change: `degraded` on GET /config).
        /// Accepts `["voice", "payments"]` or `[{"feature": "voice", ...}]`; unknown names are kept and simply never match.
        /// </summary>
        public System.Collections.Generic.IReadOnlyCollection<string> Degraded => _degraded;

        public bool IsDegraded(string feature) => !string.IsNullOrEmpty(feature) && _degraded.Contains(feature);

        private static string CachePath(string f) => Path.Combine(Application.persistentDataPath, f);

        private JObject _snapshot;
        private readonly System.Collections.Generic.Dictionary<string, string> _confirmedStarterItems = new();
        private static readonly string[] LookSlots = { "skin", "head", "hair", "top", "bottom", "shoes", "acc" };

        /// <summary>Only an explicit item registry received from the server can authorize new creator IDs.
        /// Snapshot and cached/merged presentation data are deliberately insufficient.</summary>
        public bool IsLookConfirmed(string[] look)
        {
            // The contract's skinTone is a plain string, not an item registry entry.
            if (!HasNetworkValidation || look == null || look.Length != LookSlots.Length || string.IsNullOrWhiteSpace(look[0])) return false;
            for (int i = 1; i < LookSlots.Length; i++)
                if (string.IsNullOrWhiteSpace(look[i]) || !_confirmedStarterItems.TryGetValue(look[i], out var slot) || slot != LookSlots[i]) return false;
            return true;
        }

        /// <summary>The bundled stand-in contract (ids + config); the base every server payload is merged over.</summary>
        private JObject Snapshot
        {
            get
            {
                if (_snapshot != null) return _snapshot;
                var ta = Resources.Load<TextAsset>("Contract/config_snapshot");
                _snapshot = ta != null ? Json.ParseRaw(ta.text) as JObject : null;
                return _snapshot ??= new JObject { ["ids"] = new JObject(), ["config"] = new JObject() };
            }
        }

        /// <summary>Loads the disk cache or the bundled snapshot. Never throws.</summary>
        public void LoadLocal()
        {
            try
            {
                var path = CachePath(CacheFile);
                if (File.Exists(path))
                {
                    var etagPath = CachePath(EtagFile);
                    Apply(File.ReadAllText(path), File.Exists(etagPath) ? File.ReadAllText(etagPath) : null, persist: false);
                    Source = "cache";
                    return;
                }
            }
            catch (Exception e)
            {
                Log.Warn("config", "Cache unreadable, using snapshot: " + e.Message);
            }

            var snapshot = Resources.Load<TextAsset>("Contract/config_snapshot");
            if (snapshot != null)
            {
                Apply(snapshot.text, null, persist: false);
                Source = "snapshot";
            }
            else
            {
                Log.Warn("config", "No bundled config snapshot; registries are empty until GET /v1/config succeeds");
            }
        }

        /// <summary>Applies a payload from the network (or cache) and rebuilds the registries.</summary>
        public void Apply(string json, string etag, bool persist = true)
        {
            var raw = Json.ParseRaw(json);
            if (!(raw is JObject)) throw new InvalidDataException("Config response must be an object.");
            var root = Normalize(raw);
            var registries = Registries.Parse(root["ids"], root["config"]);
            _confirmedStarterItems.Clear();
            HasNetworkValidation = persist;
            if (persist) ConfirmRawItems(raw["ids"]?["items"] ?? raw["items"]);
            Root = root;
            ETag = etag;
            Registries = registries;
            ApplyDegraded(root["degraded"] ?? root["config"]?["degraded"]);
            Source = persist ? "network" : Source;
            if (persist)
            {
                try
                {
                    File.WriteAllText(CachePath(CacheFile), json);
                    if (!string.IsNullOrEmpty(etag)) File.WriteAllText(CachePath(EtagFile), etag);
                    else if (File.Exists(CachePath(EtagFile))) File.Delete(CachePath(EtagFile));
                }
                catch (Exception e)
                {
                    Log.Warn("config", "Could not write cache: " + e.Message);
                }
            }
            Log.Info("config", $"Config {Version} ({Source}): {Registries.Venues.Count} venues, {Registries.Anims.Count} anims, {Registries.NpcLines.Count} NPC lines");
            Changed?.Invoke();
        }

        // Read the unmerged network registry only. Snapshot fields must never fill an authorization gap.
        // Mirrors Normalize's accepted registry shapes: {items:[...]} or {itemId:{...}}.
        private void ConfirmRawItems(JToken registry)
        {
            if (!(registry is JObject obj)) return;
            if (obj["items"] is JArray items)
            {
                foreach (var token in items)
                    if (token is JObject item) ConfirmRawItem(item.Str("id"), item);
                return;
            }
            foreach (var property in obj.Properties())
                if (property.Value is JObject item) ConfirmRawItem(item.Str("id", property.Name), item);
        }

        private void ConfirmRawItem(string id, JObject item)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            var starter = item["starter"];
            bool free = starter?.Type == JTokenType.Boolean ? starter.Value<bool>()
                : starter == null && item["cashCents"]?.Type == JTokenType.Integer && item["cashCents"].Value<long>() == 0;
            if (free && !string.IsNullOrWhiteSpace(item.Str("slot"))) _confirmedStarterItems[id] = item.Str("slot");
        }

        /// <summary>
        /// Two payload layouts are accepted:
        /// 1) the local stand-in `{version, minClientVersion, ids{registry…}, config{file…}}`;
        /// 2) the backend contract 1.0.0-rc1 `{version, minClientVersion, degraded[], venues{id: …}, archetypes{…},
        ///    tribes{…}, careers{…}, market{…}, events{…}, npc_lines{…}, ads{…}, ore{…}, meta{…}}`.
        /// Layout 2 is merged over the bundled snapshot so FE-owned registries (spots, anims, sfx) and ones the server
        /// does not serve yet (items, traits, dreams, homes, mini_games, toasts…) keep resolving.
        /// </summary>
        private JToken Normalize(JToken raw)
        {
            if (!(raw is JObject r) || r["ids"] != null) return raw;
            var root = (JObject)Snapshot.DeepClone();
            var ids = root["ids"] as JObject ?? new JObject();
            var cfg = root["config"] as JObject ?? new JObject();
            root["ids"] = ids;
            root["config"] = cfg;
            foreach (var k in new[] { "version", "minClientVersion", "contractVersion", "degraded" })
                if (r[k] != null) root[k] = r[k].DeepClone();

            MergeRegistry(ids, "venues", r["venues"], (item, v) =>
            {
                if (v["maxClients"] != null && item["maxPlayers"] == null) item["maxPlayers"] = v["maxClients"];
            });
            MergeRegistry(ids, "archetypes", r["archetypes"], null);
            MergeRegistry(ids, "tribes", r["tribes"], null);
            MergeRegistry(ids, "items", r["items"], null);

            var lines = r["npc_lines"] ?? r["npcLines"];
            if (lines is JObject lo && lo.Count > 0)
                cfg["npc_lines"] = lo["archetypes"] != null ? lo.DeepClone() : new JObject { ["archetypes"] = lo.DeepClone() };
            foreach (var k in new[] { "careers", "market", "events", "ads", "ore", "meta" })
                if (r[k] is JObject o && o.Count > 0) cfg[k] = o.DeepClone();
            return root;
        }

        /// <summary>Merges an id-keyed map (or a `{items: []}` registry) into `ids.&lt;name&gt;.items`; server fields win.</summary>
        private static void MergeRegistry(JObject ids, string name, JToken incoming, Action<JObject, JToken> fixup)
        {
            if (!(incoming is JObject inc) || inc.Count == 0) return;
            if (inc["items"] is JArray)
            {
                ids[name] = inc.DeepClone();
                return;
            }
            var reg = ids[name] as JObject ?? new JObject { ["registry"] = name };
            var items = reg["items"] as JArray ?? new JArray();
            reg["items"] = items;
            ids[name] = reg;
            foreach (var p in inc.Properties())
            {
                JObject item = null;
                foreach (var existing in items)
                    if (existing is JObject eo && eo.Str("id") == p.Name) { item = eo; break; }
                if (item == null)
                {
                    item = new JObject { ["id"] = p.Name };
                    items.Add(item);
                }
                if (p.Value is JObject v)
                {
                    foreach (var f in v.Properties()) item[f.Name] = f.Value.DeepClone();
                    fixup?.Invoke(item, v);
                }
            }
        }

        private void ApplyDegraded(JToken list)
        {
            var next = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (list is JArray arr)
                foreach (var t in arr)
                {
                    var name = t.Type == JTokenType.String ? t.ToString() : t.Str("feature", t.Str("id"));
                    if (!string.IsNullOrEmpty(name)) next.Add(name);
                }
            if (next.SetEquals(_degraded)) return;
            _degraded.Clear();
            _degraded.UnionWith(next);
            Log.Info("config", "Degraded features: " + (_degraded.Count == 0 ? "none" : string.Join(", ", _degraded)));
            Bus.Publish(new DegradedChangedSignal());
        }

        /// <summary>A config file by name, e.g. "economy", "needs", "careers".</summary>
        public JToken Config(string file) => Root?["config"]?[file];

        public JToken Ids(string registry) => Root?["ids"]?[registry];
    }
}
