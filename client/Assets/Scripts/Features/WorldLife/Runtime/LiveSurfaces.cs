using System;
using System.Collections.Generic;
using System.Threading;
using CTLife.Core;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.WorldLife
{
    /// <summary>
    /// Billboards, the Pixel Wall, kiosks and the Timeline screen as live surfaces. Screens are found by material name
    /// (`Screen_&lt;screenId&gt;`, written by VenueContentBuilder) and classified by house_ads.json rules; shop signs are left
    /// alone. Each live surface renders a small UI Toolkit panel into its own RenderTexture (≤ 512 px), applied with a
    /// MaterialPropertyBlock so the shared venue material is never modified. Panels render only when their content
    /// changes and are switched off a few frames later. Content: `GET /ads/live` creatives with a "Sponsored" label when
    /// present, otherwise rotating CT parody house ads with the baked house art kept in the rotation; the Timeline
    /// screen shows live city events, NPC posts and `GET /feed?scope=global`. Memory: ≤ 6 surfaces × ≤ 1 MB RT plus the
    /// AdImageCache budget (16 MB).
    /// </summary>
    public sealed class LiveSurfaces : IDisposable
    {
        private const int RenderFrames = 3;
        private const float AdsPollSeconds = 300f;
        private const float FeedPollSeconds = 30f;
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");

        private sealed class Target
        {
            public Renderer Renderer;
            public int Index;
            public Texture Original;
        }

        private sealed class Surface
        {
            public string Key, Kind, ScreenId;
            public Vector3 Position;
            public readonly List<Target> Targets = new List<Target>();
            public int KindIndex;
            public string SlotId;
            public List<string> TileSlots = new List<string>();
            public string ShownKey;
            public GameObject PanelGo;
            public UIDocument Doc;
            public PanelSettings Settings;
            public RenderTexture Rt;
            public int DisableAtFrame = -1;
            public Texture Original => Targets.Count > 0 ? Targets[0].Original : null;
        }

        private readonly WorldLifeData _data;
        private readonly TimelineFeed _feed;
        private readonly WorldLifeApi _api;
        private readonly AdImageCache _images = new AdImageCache();
        private readonly List<Surface> _surfaces = new List<Surface>();
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private readonly HashSet<string> _pinned = new HashSet<string>(StringComparer.Ordinal);
        private Dictionary<string, List<LiveCreative>> _live = new Dictionary<string, List<LiveCreative>>();
        private CancellationTokenSource _cts;
        private Transform _host;
        private string _venueId;
        private float _nextAds, _nextFeed;
        private int _adsFailures, _feedFailures;

        /// <summary>Reload ad creatives on the next tick (Phase 5 `ads_changed`, glued in FlowController).</summary>
        public void RefreshAdsNow() { _nextAds = 0f; _adsFailures = 0; }
        private bool _adsUnsupported, _feedUnsupported, _adsBusy, _feedBusy;
        private ThemeStyleSheet _theme;
        private Font _display, _body;
        private bool _disposed;

        public int SurfaceCount => _surfaces.Count;

        public LiveSurfaces(WorldLifeData data, TimelineFeed feed, WorldLifeApi api, Transform host)
        {
            _data = data;
            _feed = feed;
            _api = api;
            _host = host;
            _images.Pinned = _pinned;
            _images.Loaded += _ => { foreach (var s in _surfaces) s.ShownKey = null; };
        }

        public void Enter(string venueId, VenueSceneRoot root)
        {
            Exit();
            if (_disposed || root == null) return;
            _venueId = venueId;
            _cts = new CancellationTokenSource();
            Discover(root);
            _nextAds = _nextFeed = 0f;
            if (_surfaces.Count > 0) Log.Info("world-life", $"{venueId}: {_surfaces.Count} live surfaces");
        }

        public void Exit()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            foreach (var s in _surfaces)
            {
                foreach (var t in s.Targets) Apply(t, null);
                DestroyPanel(s);
            }
            _surfaces.Clear();
            _pinned.Clear();
            _venueId = null;
        }

        // ------------------------------------------------------------------ discovery

        private void Discover(VenueSceneRoot root)
        {
            var rules = _data.HouseAds["surfaces"]?["rules"] as JArray;
            int max = (int)WireText.Num(_data.HouseAds["surfaces"], "maxPerVenue", 6);
            if (rules == null) return;
            var byKey = new Dictionary<string, Surface>(StringComparer.Ordinal);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    var name = m.name.Replace(" (Instance)", "");
                    if (!name.StartsWith("Screen_", StringComparison.Ordinal)) continue;
                    string kind = null;
                    foreach (var rule in rules)
                        if (name.StartsWith(WireText.Str(rule, "prefix") ?? "\u0000", StringComparison.Ordinal)) { kind = WireText.Str(rule, "kind"); break; }
                    if (kind == null) continue;
                    var owner = r.transform;
                    while (owner.parent != null && owner.parent != root.transform && owner.name.Contains("_LOD")) owner = owner.parent;
                    var key = kind + "#" + owner.GetInstanceID();
                    if (!byKey.TryGetValue(key, out var s))
                        byKey[key] = s = new Surface { Key = key, Kind = kind, ScreenId = name.Substring(7), Position = owner.position };
                    var tex = m.HasProperty(BaseMap) ? m.GetTexture(BaseMap) : m.mainTexture;
                    s.Targets.Add(new Target { Renderer = r, Index = i, Original = tex });
                }
            }
            var list = new List<Surface>(byKey.Values);
            list.Sort((a, b) =>
            {
                int k = Priority(a.Kind).CompareTo(Priority(b.Kind));
                if (k != 0) return k;
                k = string.CompareOrdinal(a.ScreenId, b.ScreenId);
                if (k != 0) return k;
                k = a.Position.x.CompareTo(b.Position.x);
                return k != 0 ? k : a.Position.z.CompareTo(b.Position.z);
            });
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            // CT Island v2 (World lane): the street billboards nearest this venue join its rotation (they stream with the
            // island's cells; their panels are Screen_bb_island_* materials, so the same rules classify them).
            int islandMax = (int)WireText.Num(_data.HouseAds["surfaces"], "islandBillboards", 4);
            foreach (var screen in IslandScreen.Near(root.transform.position, 360f, islandMax))
            {
                var mats = screen.screen.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.name.StartsWith("Screen_bb_", StringComparison.Ordinal)) continue;
                    var tex = m.HasProperty(BaseMap) ? m.GetTexture(BaseMap) : m.mainTexture;
                    var s = new Surface { Key = "island#" + screen.GetInstanceID(), Kind = "billboard", ScreenId = m.name.Substring(7), Position = screen.transform.position };
                    s.Targets.Add(new Target { Renderer = screen.screen, Index = i, Original = tex });
                    list.Add(s);
                    break;
                }
            }
            var billboardSlots = AdSlots.ForVenue(_data.AdSlots, _venueId, "billboard");
            var tileSlots = AdSlots.ForVenue(_data.AdSlots, _venueId, "pixel_tile");
            var perKind = new Dictionary<string, int>();
            foreach (var s in list)
            {
                perKind.TryGetValue(s.Kind, out var n);
                s.KindIndex = n;
                perKind[s.Kind] = n + 1;
                if (s.Kind == "billboard" && n < billboardSlots.Count) s.SlotId = billboardSlots[n];
                if (s.Kind == "pixel_wall" && n == 0) s.TileSlots = tileSlots;
                _surfaces.Add(s);
            }
        }

        private static int Priority(string kind) => kind == "timeline" ? 0 : kind == "pixel_wall" ? 1 : kind == "billboard" ? 2 : 3;

        // ------------------------------------------------------------------ live data

        public void Tick(float now)
        {
            if (_disposed || _surfaces.Count == 0) return;
            PollServer(now);
            double serverNow = ServerClock.IsSynced ? ServerClock.NowMs : ServerClock.LocalNowMs;
            float period = (float)WireText.Num(_data.HouseAds, "rotationSeconds", 12);
            _pinned.Clear();
            foreach (var s in _surfaces)
            {
                try { Update(s, serverNow, period); }
                catch (Exception e) { Log.Exception("world-life", e); }
            }
            int frame = Time.frameCount;
            foreach (var s in _surfaces)
                if (s.DisableAtFrame >= 0 && frame >= s.DisableAtFrame && s.PanelGo != null)
                {
                    s.PanelGo.SetActive(false);
                    s.DisableAtFrame = -1;
                }
        }

        private void PollServer(float now)
        {
            if (_api == null || _cts == null || RuntimeMode.IsOfflineDemo) return;
            bool wantsAds = false, wantsFeed = false;
            foreach (var s in _surfaces)
            {
                if (s.Kind == "billboard" && s.SlotId != null || s.Kind == "pixel_wall" && s.TileSlots.Count > 0) wantsAds = true;
                if (s.Kind == "timeline") wantsFeed = true;
            }
            if (wantsAds && !_adsUnsupported && !_adsBusy && now >= _nextAds) PollAds(now, _cts.Token);
            if (wantsFeed && !_feedUnsupported && !_feedBusy && now >= _nextFeed) PollFeed(now, _cts.Token);
        }

        private async void PollAds(float now, CancellationToken ct)
        {
            _adsBusy = true;
            try
            {
                var body = await _api.LiveAds(ct);
                if (ct.IsCancellationRequested) return;
                bool allowHttp = Env.Current != null && Env.Current.ApiUrl("/").StartsWith("http://", StringComparison.Ordinal);
                _live = LiveAds.Parse(body, null, allowHttp, out var rejected);
                if (rejected > 0) Log.Warn("world-life", $"/ads/live: {rejected} creatives without a usable https imageUrl (contract gap; house ads shown).");
                _adsFailures = 0;
                _nextAds = Time.unscaledTime + AdsPollSeconds;
                foreach (var s in _surfaces) s.ShownKey = null;
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (WorldLifeApi.IsUnsupported(e)) { _adsUnsupported = true; Log.Info("world-life", "/ads/live not served; house ads only."); }
                else
                {
                    _adsFailures++;
                    _nextAds = Time.unscaledTime + Poll.Backoff(_adsFailures, 60f, 600f);
                    Log.Warn("world-life", "/ads/live failed: " + e.Message);
                }
            }
            finally { _adsBusy = false; }
        }

        private async void PollFeed(float now, CancellationToken ct)
        {
            _feedBusy = true;
            try
            {
                var body = await _api.GlobalFeed(ct);
                if (ct.IsCancellationRequested) return;
                _feed.MergePosts(body);
                _feedFailures = 0;
                _nextFeed = Time.unscaledTime + FeedPollSeconds;
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (WorldLifeApi.IsUnsupported(e)) { _feedUnsupported = true; Log.Info("world-life", "/feed not served; Timeline shows city events only."); }
                else
                {
                    _feedFailures++;
                    _nextFeed = Time.unscaledTime + Poll.Backoff(_feedFailures, FeedPollSeconds, 240f);
                }
            }
            finally { _feedBusy = false; }
        }

        // ------------------------------------------------------------------ content

        private List<LiveCreative> LoadedCreatives(string slot)
        {
            var ready = new List<LiveCreative>();
            if (slot == null || !_live.TryGetValue(slot, out var list)) return ready;
            foreach (var c in list)
            {
                if (_images.TryGet(c.ImageUrl, out _)) ready.Add(c);
                else _images.Request(c.ImageUrl);
            }
            return ready;
        }

        private void Update(Surface s, double serverNow, float period)
        {
            switch (s.Kind)
            {
                case "timeline":
                {
                    var key = "timeline:" + _feed.Version;
                    if (key != s.ShownKey) { s.ShownKey = key; Render(s, root => BuildTimeline(root, s.Rt.height)); }
                    break;
                }
                case "billboard":
                case "kiosk":
                {
                    var live = LoadedCreatives(s.SlotId);
                    var house = _data.HouseAds[s.Kind] as JArray ?? new JArray();
                    var pick = AdRotation.Current(live.Count, house.Count + 1, serverNow, period, s.KindIndex);
                    if (pick.Live)
                    {
                        var c = live[pick.Index];
                        _pinned.Add(c.ImageUrl);
                        var key = "live:" + c.ImageUrl;
                        if (key != s.ShownKey && _images.TryGet(c.ImageUrl, out var tex))
                        {
                            s.ShownKey = key;
                            Render(s, root => BuildCreative(root, s, tex));
                        }
                    }
                    else if (pick.Index == 0 || house.Count == 0)
                    {
                        if (s.ShownKey != "baked") { s.ShownKey = "baked"; foreach (var t in s.Targets) Apply(t, null); }
                    }
                    else
                    {
                        var key = "house:" + pick.Index;
                        if (key != s.ShownKey) { s.ShownKey = key; var ad = house[pick.Index - 1]; Render(s, root => BuildHouse(root, s, ad)); }
                    }
                    break;
                }
                case "pixel_wall":
                {
                    long tick = (long)Math.Floor(serverNow / 1000.0 / Math.Max(1f, period));
                    var tiles = new List<(string text, Texture tex)>();
                    var keyParts = new List<string> { "pixel", tick.ToString() };
                    foreach (var slot in s.TileSlots)
                    {
                        var ready = LoadedCreatives(slot);
                        if (ready.Count == 0) continue;
                        var c = ready[(int)(tick % ready.Count)];
                        if (_images.TryGet(c.ImageUrl, out var tex)) { tiles.Add((null, tex)); _pinned.Add(c.ImageUrl); keyParts.Add(c.ImageUrl); }
                    }
                    var key = string.Join("|", keyParts);
                    if (key != s.ShownKey)
                    {
                        s.ShownKey = key;
                        var parody = WireText.Strs(_data.HouseAds["pixelTiles"]);
                        Render(s, root => BuildPixelWall(root, tiles, parody, tick));
                    }
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ panels

        private void Render(Surface s, Action<VisualElement> build)
        {
            if (!EnsurePanel(s)) return;
            s.PanelGo.SetActive(true);
            var root = s.Doc.rootVisualElement;
            if (root == null) return;
            root.Clear();
            root.pickingMode = PickingMode.Ignore;
            root.style.width = s.Rt.width;
            root.style.height = s.Rt.height;
            root.style.backgroundColor = new Color(0.04f, 0.06f, 0.08f, 1f);
            root.style.overflow = Overflow.Hidden;
            build(root);
            foreach (var t in s.Targets) Apply(t, s.Rt);
            s.DisableAtFrame = Time.frameCount + RenderFrames;
        }

        private bool EnsurePanel(Surface s)
        {
            if (s.PanelGo != null) return true;
            if (_theme == null) _theme = Resources.Load<ThemeStyleSheet>("UI/CTLifeTheme");
            if (_display == null) _display = Resources.Load<Font>("UI/Fonts/LilitaOne-Regular");
            if (_body == null) _body = Resources.Load<Font>("UI/Fonts/Manrope-Semibold");
            int w = 512, h = 256;
            var o = s.Original;
            if (s.Kind == "pixel_wall") h = 512;
            if (o != null && o.width > 0 && o.height > 0)
            {
                float aspect = (float)o.width / o.height;
                if (aspect >= 1f) { w = 512; h = Mathf.Clamp(Mathf.RoundToInt(512 / aspect), 128, 512); }
                else { h = 512; w = Mathf.Clamp(Mathf.RoundToInt(512 * aspect), 128, 512); }
            }
            s.Rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "wl_" + s.Kind + "_" + s.KindIndex, useMipMap = false };
            s.Rt.Create();
            s.Settings = ScriptableObject.CreateInstance<PanelSettings>();
            s.Settings.name = "wl_panel_" + s.Kind + "_" + s.KindIndex;
            s.Settings.themeStyleSheet = _theme;
            s.Settings.targetTexture = s.Rt;
            s.Settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            s.Settings.scale = 1f;
            s.Settings.clearColor = false; // every layout paints an opaque full-size background
            s.Settings.sortingOrder = -100;
            // Off-screen panel: never take pointer input from the real screen.
            s.Settings.SetScreenToPanelSpaceFunction(_ => new Vector2(float.NaN, float.NaN));
            s.PanelGo = new GameObject(s.Settings.name);
            if (_host != null) s.PanelGo.transform.SetParent(_host, false);
            s.PanelGo.SetActive(false);
            s.Doc = s.PanelGo.AddComponent<UIDocument>();
            s.Doc.panelSettings = s.Settings;
            return true;
        }

        private void DestroyPanel(Surface s)
        {
            if (s.PanelGo != null) UnityEngine.Object.Destroy(s.PanelGo);
            if (s.Settings != null) UnityEngine.Object.Destroy(s.Settings);
            if (s.Rt != null) { s.Rt.Release(); UnityEngine.Object.Destroy(s.Rt); }
            s.PanelGo = null; s.Doc = null; s.Settings = null; s.Rt = null;
            s.ShownKey = null;
        }

        /// <summary>Overrides the screen texture for this renderer's material slot; null clears the override.</summary>
        private void Apply(Target t, Texture tex)
        {
            if (t.Renderer == null) return;
            _mpb.Clear();
            if (tex != null)
            {
                t.Renderer.GetPropertyBlock(_mpb, t.Index);
                _mpb.SetTexture(BaseMap, tex);
                _mpb.SetTexture(MainTex, tex);
            }
            t.Renderer.SetPropertyBlock(_mpb, t.Index);
        }

        private Label Text(string text, float size, bool display, Color color)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = false };
            l.style.fontSize = size;
            l.style.color = color;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            var f = display ? _display : _body;
            if (f != null) l.style.unityFontDefinition = FontDefinition.FromFont(f);
            return l;
        }

        private VisualElement Chip(string text, float h, Color bg)
        {
            var chip = Text(text, h * 0.075f, false, Color.white);
            chip.style.position = Position.Absolute;
            chip.style.right = h * 0.04f;
            chip.style.bottom = h * 0.04f;
            chip.style.backgroundColor = bg;
            chip.style.paddingLeft = chip.style.paddingRight = h * 0.03f;
            chip.style.paddingTop = chip.style.paddingBottom = h * 0.01f;
            return chip;
        }

        private void BuildHouse(VisualElement root, Surface s, JToken ad)
        {
            float h = s.Rt.height;
            root.style.justifyContent = Justify.Center;
            root.style.alignItems = Align.Center;
            root.style.paddingLeft = root.style.paddingRight = h * 0.08f;
            root.style.backgroundColor = new Color(0.07f, 0.09f, 0.16f, 1f);
            root.Add(Text(WireText.Clean(Headline(ad), 40) ?? NameTable.Get("place.city", "City").ToUpperInvariant(), h * 0.22f, true, new Color(0.99f, 0.8f, 0.2f)));
            root.Add(Text(WireText.Clean(NameTable.Apply(WireText.Str(ad, "sub")), 90) ?? "", h * 0.1f, false, Color.white));
            var tag = WireText.Clean(WireText.Str(ad, "tag"), 24);
            if (tag != null) root.Add(Chip(tag, h, new Color(1f, 1f, 1f, 0.18f)));
        }

        /// <summary>A house ad's headline: "nameId" names a place or app in contract/ids/names.json (shown in caps), so a
        /// rename never needs new ad copy; a plain "headline" still passes through the names table's replace map.</summary>
        public static string Headline(JToken ad)
        {
            var id = WireText.Str(ad, "nameId");
            var named = string.IsNullOrEmpty(id) ? null : NameTable.Get(id);
            return named != null ? named.ToUpperInvariant() : NameTable.Apply(WireText.Str(ad, "headline"));
        }

        private void BuildCreative(VisualElement root, Surface s, Texture tex)
        {
            float h = s.Rt.height;
            var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.style.flexGrow = 1;
            root.Add(img);
            // Spec: every paid placement shows a "Sponsored" label.
            root.Add(Chip("Sponsored", h, new Color(0f, 0f, 0f, 0.65f)));
        }

        private void BuildPixelWall(VisualElement root, List<(string text, Texture tex)> live, List<string> parody, long tick)
        {
            const int cols = 5, rows = 4;
            root.style.flexDirection = FlexDirection.Row;
            root.style.flexWrap = Wrap.Wrap;
            root.style.backgroundColor = new Color(0.1f, 0.1f, 0.12f, 1f);
            int n = cols * rows;
            for (int i = 0; i < n; i++)
            {
                var tile = new VisualElement { pickingMode = PickingMode.Ignore };
                tile.style.width = Length.Percent(100f / cols);
                tile.style.height = Length.Percent(100f / rows);
                tile.style.borderLeftWidth = tile.style.borderTopWidth = 1;
                tile.style.borderLeftColor = tile.style.borderTopColor = new Color(0, 0, 0, 0.6f);
                tile.style.justifyContent = Justify.Center;
                if (i < live.Count)
                {
                    var img = new Image { image = live[i].tex, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
                    img.style.flexGrow = 1;
                    tile.Add(img);
                    var star = Text("AD", 14, false, Color.white);
                    star.style.position = Position.Absolute;
                    star.style.right = 2; star.style.top = 2;
                    star.style.backgroundColor = new Color(0, 0, 0, 0.7f);
                    tile.Add(star);
                }
                else if (parody.Count > 0)
                {
                    int idx = (int)((tick + i * 7) % parody.Count);
                    tile.style.backgroundColor = Color.HSVToRGB(((i * 37 + tick * 11) % 100) / 100f, 0.55f, 0.85f);
                    tile.Add(Text(parody[idx], 20, true, Color.white));
                }
                root.Add(tile);
            }
            if (live.Count > 0)
            {
                var foot = Chip("AD = Sponsored tile", 512, new Color(0, 0, 0, 0.7f));
                root.Add(foot);
            }
        }

        private void BuildTimeline(VisualElement root, float h)
        {
            root.style.flexDirection = FlexDirection.Column;
            root.style.paddingLeft = root.style.paddingRight = h * 0.04f;
            root.style.paddingTop = h * 0.03f;
            root.style.backgroundColor = new Color(0.03f, 0.05f, 0.09f, 1f);
            // This screen also contains local NPC flavour and retained city events, so it must not claim a live connection.
            var title = Text(Headline(_data.HouseAds["timeline"]) ?? NameTable.Get("app.timeline", "X").ToUpperInvariant(), h * 0.11f, true, new Color(0.11f, 0.85f, 0.95f));
            title.style.unityTextAlign = TextAnchor.MiddleLeft;
            root.Add(title);
            var lines = _feed.Lines(6, 72);
            if (lines.Count == 0) lines.Add(NameTable.Apply(WireText.Str(_data.HouseAds["timeline"], "empty")) ?? "");
            foreach (var line in lines)
            {
                var row = Text(line, h * 0.068f, false, Color.white);
                row.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.style.whiteSpace = WhiteSpace.NoWrap;
                row.style.overflow = Overflow.Hidden;
                row.style.marginTop = h * 0.012f;
                root.Add(row);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Exit();
            _disposed = true;
            _images.Dispose();
        }
    }
}
