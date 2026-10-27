using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.UI.Kit;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Motion = CTLife.UI.Kit.Motion;

namespace CTLife.CityLife.Season
{
    /// <summary>
    /// The Season screens (backend #13; UI brief ct-life-evidence/season-r4/ui-refs/BRIEF.md, approved 2026-10-25), full screen
    /// over the game like the casino:
    /// - Season: Brawl-Stars two tracks (pass above, free below, one tier ribbon between) beside a Fortnite-style preview of the
    ///   selected reward, the Apex XP bar with the tier badge and Fall Guys' "Tier n/N"; claimable tiers pulse and Claim pays
    ///   with coins and confetti. The pass is real money in the Shop (payment intent), never Cash; no fake timers, no tier skip.
    /// - Nods Book: per-set album (Another Crab's Treasure), found nods in colour and unfound as "?" (Astro), the set reward
    ///   beside the grid (Apex), a detail card. No positions are shown.
    /// - Hunters: the board of most nods found.
    /// Every reward is off-chain and untradable.
    /// </summary>
    public sealed class SeasonSheet : VisualElement
    {
        public const string TabSeason = "season", TabBook = "book", TabHunters = "hunters";
        private static SeasonSheet _open;
        public static bool IsOpen => _open != null;

        public static void Show(string tab = TabSeason)
        {
            var svc = SeasonService.Current;
            if (svc == null || UiRoot.Instance == null) { Bus.Publish(ToastSignal.Text(SeasonText.T("ui.offline"), ToastKind.Warning)); return; }
            if (_open != null) { _open.Select(tab); return; }
            _open = new SeasonSheet(svc, tab);
            UiRoot.Instance.ModalLayer.Add(_open);
        }

        public static void CloseOpen() => _open?.Close();

        private static readonly Dictionary<string, Texture2D> Tex = new Dictionary<string, Texture2D>();
        public static Texture2D Art(string path)
        {
            if (Tex.TryGetValue(path, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>(path);
            Tex[path] = t;
            return t;
        }
        public static Texture2D SetDisc(string setId) => Art("UI/Season/nod_" + setId);

        // ------------------------------------------------------------------ instance

        public readonly SeasonService Svc;
        private readonly VisualElement _body, _toastHost;
        private readonly Label _name, _ends, _xpTier, _xpNum, _badgeN;
        private readonly VisualElement _xpFill;
        private readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>();
        private readonly Label _claimBadge;
        private readonly CelebrationBurst _burst;
        private readonly FullScreenPause _pause = new FullScreenPause();
        private readonly IVisualElementScheduledItem _tick;
        private string _tab;
        private SeasonPanel _season;
        private BookPanel _book;
        private HuntersPanel _hunters;

        private SeasonSheet(SeasonService svc, string tab)
        {
            Svc = svc;
            name = "season-sheet";
            AddToClassList("ss-sheet");
            pickingMode = PickingMode.Position;
            foreach (var s in new[] { "UI/kit", "UI/Season/season" })
            {
                var sheet = Resources.Load<StyleSheet>(s);
                if (sheet != null) styleSheets.Add(sheet);
            }
            Add(El.Div("ss-bg"));

            var top = El.Div("ss-top");
            var close = Btn("ss-close", Close, "season-close");
            close.Add(new UiIcon("close"));
            top.Add(close);
            _name = El.Text("", "ss-name");
            _ends = El.Text("", "ss-ends");
            top.Add(El.Div("ss-title", El.Text(SeasonText.T("ui.kicker"), "ss-kicker"), _name, _ends));
            var tabs = El.Div("ss-tabs");
            foreach (var (id, key, icon) in new[] { (TabSeason, "tab.season", "star"), (TabBook, "tab.book", "magnifying"), (TabHunters, "tab.hunters", "ranking") })
            {
                var b = Btn("ss-tab", () => Select(id), "season-tab-" + id);
                b.Add(new UiIcon(icon == "magnifying" ? "heat" : icon));
                b.Add(El.Text(SeasonText.T(key), "ss-tab-label"));
                if (id == TabSeason)
                {
                    _claimBadge = El.Text("", null);
                    var badge = El.Div("ss-tab-badge", _claimBadge);
                    badge.name = "season-claim-badge";
                    b.Add(badge);
                }
                _tabs[id] = b;
                tabs.Add(b);
            }
            top.Add(tabs);
            _badgeN = El.Text("0", "ss-xp-badge-n");
            _xpTier = El.Text("", "ss-xp-tier");
            _xpNum = El.Text("", "ss-xp-num");
            _xpFill = El.Div("ss-bar-fill");
            top.Add(El.Div("ss-xp", El.Div("ss-xp-badge", _badgeN), El.Div("ss-xp-col", El.Div("ss-xp-row", _xpTier, _xpNum), El.Div("ss-bar", _xpFill))));
            Add(top);

            _body = El.Div("ss-body");
            Add(_body);
            Add(El.Div("ss-foot", El.Text(SeasonText.T("ui.foot"), null)));
            _toastHost = El.Div();
            _toastHost.pickingMode = PickingMode.Ignore;
            _toastHost.style.position = Position.Absolute; _toastHost.style.left = 0; _toastHost.style.right = 0; _toastHost.style.top = 0; _toastHost.style.bottom = 0;
            _toastHost.style.alignItems = Align.Center;
            Add(_toastHost);
            _burst = new CelebrationBurst(coins: 26, confetti: 90, puffs: 0);
            _burst.AddToClassList("ss-burst");
            _burst.pickingMode = PickingMode.Ignore;
            Add(_burst);

            _pause.Begin();
            Svc.SeasonChanged += OnSeason;
            Svc.BookChanged += OnBook;
            RegisterCallback<DetachFromPanelEvent>(_ => Teardown());
            Select(string.IsNullOrEmpty(tab) ? TabSeason : tab);
            OnSeason();
            // furniture rewards show the shop's studio thumbnail: the catalogue names their models
            var shop = HomeShopService.Current;
            if (shop != null && shop.Catalog == null)
                shop.LoadCatalog().ContinueWith(t => { if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) schedule.Execute(() => { if (panel != null) _season?.Render(); }); },
                    System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            _ = Svc.RefreshSeason();
            _ = Svc.RefreshBook();
            Cues.Play("sfx_ui_open");
            style.opacity = 0f;
            Tween.Run(this, 0.25f, t => style.opacity = t, Ease.OutCubic, skipWhenReduced: false);
            _tick = schedule.Execute(Tick).Every(700);
            Log.Info("season", "season screens open at " + _tab);
        }

        public static Button Btn(string cls, Action click, string nm)
        {
            var b = El.Btn("", click, cls);
            b.RemoveFromClassList("unity-button");
            b.name = nm;
            return b;
        }

        public void Select(string id)
        {
            if (id == _tab) return;
            _tab = id;
            _body.Clear();
            VisualElement panel = id switch
            {
                TabBook => _book ??= new BookPanel(this),
                TabHunters => _hunters ??= new HuntersPanel(this),
                _ => _season ??= new SeasonPanel(this),
            };
            _body.Add(panel);
            if (panel is ISeasonPanel p) p.Shown();
            foreach (var kv in _tabs) kv.Value.EnableInClassList("ss-tab--on", kv.Key == id);
            panel.style.opacity = 0f;
            Tween.Run(panel, 0.2f, t => panel.style.opacity = t, Ease.OutCubic, skipWhenReduced: false);
        }

        private void OnSeason()
        {
            var s = Svc.Season;
            if (s == null) { _name.text = SeasonText.T("ui.loading"); return; }
            _name.text = s.Name;
            int tiers = Math.Max(s.Free.Count, s.Paid.Count);
            _badgeN.text = s.Tier.ToString(CultureInfo.InvariantCulture);
            _xpTier.text = SeasonText.F("ui.tierOf", s.Tier, tiers).ToUpperInvariant();
            bool max = s.XpToNext == null || s.Tier >= tiers;
            _xpNum.text = max ? SeasonText.F("ui.xpMax", s.Xp) : SeasonText.F("ui.xpNext", Math.Max(0, s.Xp - s.Tier * s.XpPerTier), s.XpPerTier);
            _xpFill.style.scale = new Scale(new Vector3(max ? 1f : s.TierProgress, 1f, 1f));
            int c = s.ClaimableCount;
            _claimBadge.text = c.ToString(CultureInfo.InvariantCulture);
            _claimBadge.parent.Show(c > 0);
            Tick();
            _season?.Render();
        }

        private void OnBook() { _book?.Render(); }

        private void Tick()
        {
            var s = Svc.Season;
            if (s == null || s.EndsAtMs <= 0) { _ends.text = ""; return; }
            double left = s.EndsAtMs - ServerClock.NowMs;
            _ends.text = left <= 0 ? SeasonText.T("ui.ended") : SeasonText.F("ui.ends", Span(left));
            _season?.Pulse();
        }

        public static string Span(double ms)
        {
            long s = (long)Math.Max(0, ms / 1000);
            if (s >= 86400) return (s / 86400) + "d " + (s % 86400 / 3600) + "h";
            if (s >= 3600) return (s / 3600) + "h " + (s % 3600 / 60) + "m";
            return Math.Max(1, s / 60) + "m";
        }

        public void Celebrate(Vector2 at, bool coins)
        {
            if (coins) _burst.Coins(at, 24);
            _burst.Confetti(80);
            Cues.Play(coins ? "sfx_cash_in" : "sfx_ui_success");
            DeviceFeel.Confirm();
        }

        public void Toast(string text)
        {
            _toastHost.Clear();
            var t = El.Div("ss-toast", El.Text(text, null));
            t.name = "season-toast";
            t.pickingMode = PickingMode.Ignore;
            _toastHost.Add(t);
            Motion.Enter(t, 0f, -30f, 0.25f);
            t.schedule.Execute(() => Tween.Run(t, 0.3f, k => t.style.opacity = 1f - k, Ease.OutCubic, skipWhenReduced: false)).StartingIn(2400);
        }

        public void Close()
        {
            Cues.Play("sfx_ui_close");
            RemoveFromHierarchy();
        }

        private void Teardown()
        {
            _tick?.Pause();
            _pause.End();
            Svc.SeasonChanged -= OnSeason;
            Svc.BookChanged -= OnBook;
            if (ReferenceEquals(_open, this)) _open = null;
            Log.Info("season", "season screens closed");
        }
    }

    internal interface ISeasonPanel { void Shown(); }

    /// <summary>What a reward is, for the tiles and the preview: name, kind tag, glyph and (furniture) the shop's model.</summary>
    public static class SeasonRewards
    {
        public struct Look { public string Name, KindKey, Icon, ModelKey, Amount; }

        public static Look Describe(SeasonReward r)
        {
            if (r == null) return new Look { Name = "?", KindKey = "kind.cash", Icon = "cash" };
            if (r.CosmeticId != null) return new Look { Name = Pretty(r.CosmeticId), KindKey = "kind.cosmetic", Icon = "tshirt" };
            if (r.FurnitureItemId != null)
            {
                var item = HomeShopService.Current?.Catalog?.Items.FirstOrDefault(i => i.ItemId == r.FurnitureItemId);
                return new Look { Name = item?.Name ?? Pretty(r.FurnitureItemId), KindKey = "kind.furniture", Icon = "furniture", ModelKey = item?.ModelKey };
            }
            string money = Money.Cash(r.CashCents);
            return new Look { Name = money + " Cash", KindKey = "kind.cash", Icon = "cash", Amount = money };
        }

        private static readonly HashSet<string> Caps = new HashSet<string> { "gm", "tge", "nft", "defi", "ct" };

        /// <summary>"reward_s1_gm_cap" -> "GM Cap", "plant_zebra" -> "Zebra Plant" (furniture ids read noun-last).</summary>
        public static string Pretty(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var parts = id.Split('_').ToList();
            bool reward = parts.Count > 0 && parts[0] == "reward";
            if (reward) { parts.RemoveAt(0); if (parts.Count > 1 && (parts[0] == "nod" || (parts[0].Length <= 3 && parts[0].StartsWith("s", StringComparison.Ordinal) && char.IsDigit(parts[0][parts[0].Length - 1])))) parts.RemoveAt(0); }
            else if (parts.Count == 2) parts = new List<string> { parts[1], parts[0] };
            return string.Join(" ", parts.Select(p => Caps.Contains(p) ? p.ToUpperInvariant() : p.Length > 0 ? char.ToUpperInvariant(p[0]) + p.Substring(1) : p));
        }
    }

    /// <summary>The Season tab: the reward preview on the left and the two tracks on the right.</summary>
    internal sealed class SeasonPanel : VisualElement, ISeasonPanel
    {
        private readonly SeasonSheet _sheet;
        private readonly VisualElement _heroArt, _heroThumb, _heroTags, _actions;
        private readonly Label _heroName, _heroSub, _heroNote;
        private readonly ScrollView _scroll;
        private readonly VisualElement _rowLabels;
        private readonly List<(VisualElement tile, SeasonTier tier, string track)> _tiles = new List<(VisualElement, SeasonTier, string)>();
        private string _selTrack = "free";
        private int _selTier = -1;
        private bool _pulse, _claiming, _scrolled;

        public SeasonPanel(SeasonSheet sheet)
        {
            _sheet = sheet;
            AddToClassList("ss-season");
            _heroArt = El.Div("ss-hero-art");
            _heroThumb = El.Div("ss-hero-thumb");
            _heroTags = El.Div("ss-hero-tags");
            _heroName = El.Text("", "ss-hero-name");
            _heroSub = El.Text("", "ss-hero-sub");
            _actions = El.Div();
            _heroNote = El.Text(SeasonText.T("ui.earn"), "ss-hero-note");
            var hero = El.Div("ss-hero", _heroArt, _heroTags, _heroName, _heroSub, _actions, _heroNote);
            hero.name = "season-hero";
            Add(hero);
            _rowLabels = El.Div("ss-rowlabels");
            _scroll = new ScrollView(ScrollViewMode.Horizontal);
            _scroll.AddToClassList("ss-scroll");
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.name = "season-tracks";
            Add(El.Div("ss-tracks", _rowLabels, _scroll));
            Render();
        }

        public void Shown() { _scrolled = false; Render(); }

        public void Render()
        {
            var s = _sheet.Svc.Season;
            _scroll.Clear(); _tiles.Clear(); _rowLabels.Clear();
            if (s == null) { _heroName.text = SeasonText.T("ui.loading"); _heroSub.text = ""; _actions.Clear(); return; }
            int n = Math.Max(s.Free.Count, s.Paid.Count);

            // row labels: the pass row's plate (Activate when the player has no pass), the free row
            var paidLabel = El.Div("ss-rowlabel ss-rowlabel--paid", El.Text(SeasonText.T("ui.passRow"), "ss-rowlabel-title"),
                El.Text(SeasonText.T(s.HasPass ? "ui.passOn" : "ui.passOff"), "ss-rowlabel-sub"));
            if (!s.HasPass)
            {
                var act = SeasonSheet.Btn("ss-activate", OpenPass, "season-activate");
                act.Add(new UiIcon("locked"));
                act.Add(El.Text(SeasonText.T("ui.activate"), null));
                paidLabel.Add(act);
            }
            _rowLabels.Add(paidLabel);
            _rowLabels.Add(El.Div("ss-rowgap"));
            _rowLabels.Add(El.Div("ss-rowlabel ss-rowlabel--free", El.Text(SeasonText.T("ui.freeRow"), "ss-rowlabel-title"),
                El.Text(SeasonText.T("ui.freeSub"), "ss-rowlabel-sub")));

            // the columns: pass tile, tier node on the progress line, free tile
            var holder = El.Div();
            holder.style.flexDirection = FlexDirection.Row;
            var line = El.Div("ss-line");
            var lineFill = El.Div("ss-line-fill");
            line.Add(lineFill);
            for (int t = 1; t <= n; t++)
            {
                var paid = s.Paid.FirstOrDefault(x => x.Tier == t);
                var free = s.Free.FirstOrDefault(x => x.Tier == t);
                var col = El.Div("ss-col");
                col.Add(Tile(paid, "paid"));
                var node = El.Div("ss-node" + (t <= s.Tier ? " ss-node--reached" : "") + (t == s.Tier + 1 ? " ss-node--current" : ""));
                node.Add(El.Div("ss-node-n", El.Text(t.ToString(CultureInfo.InvariantCulture), null)));
                col.Add(node);
                col.Add(Tile(free, "free"));
                holder.Add(col);
            }
            // the progress line sits behind the nodes: reached tiers plus the part toward the next
            var lineHost = El.Div();
            lineHost.style.position = Position.Absolute; lineHost.style.left = 98; lineHost.style.width = Math.Max(0, (n - 1) * 196); lineHost.style.top = 204; lineHost.style.height = 88;
            lineHost.pickingMode = PickingMode.Ignore;
            lineHost.Add(line);
            float reached = n <= 1 ? 1f : Mathf.Clamp01((s.Tier - 1 + (s.Tier < n ? s.TierProgress : 0f)) / (n - 1));
            if (s.Tier <= 0) reached = 0f;
            lineFill.style.scale = new Scale(new Vector3(reached, 1f, 1f));
            var content = El.Div();
            content.Add(lineHost);
            content.Add(holder);
            lineHost.SendToBack();
            _scroll.Add(content);

            // the selection: keep it, else the first claimable, else the next tier to reach
            if (_selTier < 0 || !_tiles.Any(x => x.tier?.Tier == _selTier && x.track == _selTrack))
            {
                var first = _tiles.FirstOrDefault(x => x.tier != null && x.tier.Claimable);
                if (first.tier == null) first = _tiles.FirstOrDefault(x => x.tier != null && x.track == "free" && x.tier.Tier == Math.Min(n, s.Tier + 1));
                if (first.tier != null) { _selTier = first.tier.Tier; _selTrack = first.track; }
            }
            Select(_selTier, _selTrack, false);
            if (!_scrolled)
            {
                _scrolled = true;
                int focus = Math.Max(0, _selTier - 3);
                _scroll.schedule.Execute(() => _scroll.scrollOffset = new Vector2(focus * 196, 0)).StartingIn(30);
            }
        }

        private VisualElement Tile(SeasonTier t, string track)
        {
            var tile = SeasonSheet.Btn("ss-tile ss-tile--" + track, () => { if (t != null) Select(t.Tier, track, true); }, "season-tile-" + track + "-" + (t?.Tier ?? 0));
            if (t == null) { tile.style.visibility = Visibility.Hidden; return tile; }
            var look = SeasonRewards.Describe(t.Reward);
            if (look.ModelKey != null)
            {
                var th = El.Div("ss-tile-thumb");
                var rt = FurniturePreview.Instance.Thumbnail(look.ModelKey);
                if (rt != null) th.style.backgroundImage = Background.FromRenderTexture(rt);
                th.pickingMode = PickingMode.Ignore;
                tile.Add(th);
            }
            else tile.Add(new UiIcon(look.Icon));
            var amt = El.Text(look.Amount ?? SeasonText.T(look.KindKey + ".short"), "ss-tile-amt");
            amt.pickingMode = PickingMode.Ignore;
            tile.Add(amt);
            bool passLocked = track == "paid" && (t.PassRequired || !_sheet.Svc.Season.HasPass);
            if (t.Claimed) tile.AddToClassList("ss-tile--claimed");
            else if (t.Claimable && !passLocked) tile.AddToClassList("ss-tile--claimable");
            else tile.AddToClassList("ss-tile--locked");
            if (t.Claimed) tile.Add(Badge("ss-tile-badge--done", "check"));
            else if (passLocked) tile.Add(Badge("ss-tile-badge--pass", "locked"));
            else if (!t.Claimable) tile.Add(Badge("ss-tile-badge--lock", "locked"));
            if (t.Claimable && !passLocked)
            {
                var chip = El.Div("ss-claim-chip", El.Text(SeasonText.T("ui.claimChip"), null));
                chip.pickingMode = PickingMode.Ignore;
                tile.Add(chip);
            }
            foreach (var c in tile.Children()) c.pickingMode = PickingMode.Ignore;
            _tiles.Add((tile, t, track));
            return tile;
        }

        private static VisualElement Badge(string cls, string icon)
        {
            var b = El.Div("ss-tile-badge " + cls, new UiIcon(icon));
            b.pickingMode = PickingMode.Ignore;
            return b;
        }

        /// <summary>Claimable tiles breathe (one class toggled every 700 ms; a USS scale transition).</summary>
        public void Pulse()
        {
            _pulse = !_pulse;
            foreach (var (tile, t, _) in _tiles) if (tile.ClassListContains("ss-tile--claimable")) tile.EnableInClassList("ss-tile--pulse", _pulse);
        }

        private void Select(int tier, string track, bool user)
        {
            var s = _sheet.Svc.Season;
            if (s == null) return;
            _selTier = tier; _selTrack = track;
            foreach (var (tile, t, tr) in _tiles) tile.EnableInClassList("ss-tile--sel", t?.Tier == tier && tr == track);
            var st = (track == "paid" ? s.Paid : s.Free).FirstOrDefault(x => x.Tier == tier);
            _actions.Clear();
            _heroTags.Clear();
            _heroArt.Clear();
            if (st == null) { _heroName.text = ""; _heroSub.text = ""; return; }
            var look = SeasonRewards.Describe(st.Reward);
            _heroArt.EnableInClassList("ss-hero-art--free", track == "free");
            _heroArt.EnableInClassList("ss-hero-art--paid", track == "paid");
            if (look.ModelKey != null)
            {
                var rt = FurniturePreview.Instance.Thumbnail(look.ModelKey);
                if (rt != null) _heroThumb.style.backgroundImage = Background.FromRenderTexture(rt);
                _heroArt.Add(_heroThumb);
            }
            else
            {
                var ic = new UiIcon(look.Icon);
                ic.style.unityBackgroundImageTintColor = track == "paid" ? new Color(1f, 0.84f, 0.47f) : new Color(0.67f, 0.91f, 1f);
                _heroArt.Add(ic);
            }
            _heroTags.Add(El.Div("ss-tag ss-tag--" + track, El.Text(SeasonText.T(track == "paid" ? "ui.tagPass" : "ui.tagFree"), null)));
            _heroTags.Add(El.Div("ss-tag", El.Text(SeasonText.T(look.KindKey), null)));
            _heroTags.Add(El.Div("ss-tag", El.Text(SeasonText.F("ui.tagTier", st.Tier), null)));
            _heroName.text = look.Name;
            bool passLocked = track == "paid" && (st.PassRequired || !s.HasPass);
            if (st.Claimed) _heroSub.text = SeasonText.T("ui.claimed");
            else if (passLocked) _heroSub.text = SeasonText.F(s.Tier >= st.Tier ? "ui.passReached" : "ui.passTier", st.XpRequired);
            else if (st.Claimable) _heroSub.text = SeasonText.T("ui.ready");
            else _heroSub.text = SeasonText.F("ui.reach", st.Tier, Math.Max(0, st.XpRequired - s.Xp));
            if (st.Claimable && !passLocked && !st.Claimed)
            {
                var b = SeasonSheet.Btn("ss-hero-btn ss-hero-btn--claim", () => Claim(st, track), "season-claim");
                b.Add(El.Text(SeasonText.T("ui.claim"), null));
                _actions.Add(b);
            }
            else if (passLocked && !st.Claimed)
            {
                var b = SeasonSheet.Btn("ss-hero-btn ss-hero-btn--pass", OpenPass, "season-pass");
                b.Add(new UiIcon("locked"));
                b.Add(El.Text(SeasonText.T("ui.getPass"), null));
                _actions.Add(b);
            }
        }

        private static void OpenPass()
        {
            // the pass is real money (payment intent, SOL/USDC/SKR) in the Shop; never Cash
            Log.Info("season", "pass: open the Shop");
            SeasonSheet.CloseOpen();
            RetentionService.Opener?.Invoke("shop");
        }

        private async void Claim(SeasonTier t, string track)
        {
            if (_claiming) return;
            _claiming = true;
            var btn = _actions.Q<Button>("season-claim");
            btn?.SetEnabled(false);
            try
            {
                var c = await _sheet.Svc.Claim(t.Tier, track);
                if (panel == null) return;
                var look = SeasonRewards.Describe(c.Reward);
                var at = _heroArt.worldBound.center;
                _sheet.Celebrate(_sheet.WorldToLocal(at), c.Reward.Kind == "cash");
                _sheet.Toast(SeasonText.F(c.AlreadyClaimed ? "ui.alreadyClaimed" : "ui.claimedToast", look.Name));
                if (c.Reward.Kind == "cash" && Services.TryGet<PlayerProfile>(out var p)) _ = p.Refresh().ContinueWith(_ => { });
                _selTier = -1;
            }
            catch (Exception e)
            {
                var err = CityErrors.From(e, true);
                Log.Warn("season", $"claim {track} {t.Tier} failed: {err.Code}");
                _sheet.Toast(err.Is("PASS_REQUIRED") ? SeasonText.T("ui.needPass") : err.Is("TIER_LOCKED") ? SeasonText.T("ui.notYet") : err.Message);
                btn?.SetEnabled(true);
            }
            finally { _claiming = false; }
        }
    }

    /// <summary>The Nods Book tab: the sets, the album of the selected set, the detail card.</summary>
    internal sealed class BookPanel : VisualElement, ISeasonPanel
    {
        private readonly SeasonSheet _sheet;
        private readonly VisualElement _sets, _grid, _detail, _reward;
        private readonly Label _title, _count;
        private string _setId, _nodId;
        private static Dictionary<string, (string nick, string category, string district)> _lore;

        public BookPanel(SeasonSheet sheet)
        {
            _sheet = sheet;
            AddToClassList("ss-book");
            _sets = El.Div("ss-sets");
            _sets.name = "season-sets";
            _title = El.Text("", "ss-album-title");
            _count = El.Text("", "ss-album-count");
            _reward = El.Div("ss-album-reward");
            _grid = El.Div("ss-grid");
            _grid.name = "season-grid";
            var gridScroll = new ScrollView(ScrollViewMode.Vertical);
            gridScroll.AddToClassList("ss-grid-scroll");
            gridScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            gridScroll.Add(_grid);
            var album = El.Div("ss-album", El.Div("ss-album-head", _title, _count), _reward, gridScroll);
            album.name = "season-album";
            _detail = El.Div("ss-detail");
            _detail.name = "season-nod-detail";
            Add(_sets); Add(album); Add(_detail);
            Render();
        }

        public void Shown() => Render();

        private static (string nick, string category, string district) Lore(string id)
        {
            if (_lore == null)
            {
                _lore = new Dictionary<string, (string, string, string)>();
                var ta = Resources.Load<TextAsset>("UI/Season/nod_lore");
                if (ta != null)
                    try
                    {
                        foreach (var kv in (JObject.Parse(ta.text)["nods"] as JObject) ?? new JObject())
                            _lore[kv.Key] = (CityWire.Str(kv.Value["nickname"]), CityWire.Str(kv.Value["category"]), CityWire.Str(kv.Value["district"]));
                    }
                    catch (Exception e) { Log.Warn("season", "nod lore unreadable: " + e.GetType().Name); }
            }
            return _lore.TryGetValue(id ?? "", out var l) ? l : (null, null, null);
        }

        public void Render()
        {
            var b = _sheet.Svc.Book;
            _sets.Clear();
            if (b == null) { _grid.Clear(); _title.text = SeasonText.T("ui.loading"); _count.text = ""; return; }
            if (_setId == null || b.Sets.All(x => x.SetId != _setId)) _setId = b.Sets.FirstOrDefault()?.SetId;
            foreach (var set in b.Sets)
            {
                var id = set.SetId;
                var row = SeasonSheet.Btn("ss-set" + (id == _setId ? " ss-set--on" : "") + (set.Completed ? " ss-set--done" : ""), () => { _setId = id; _nodId = null; Render(); }, "season-set-" + id);
                var disc = El.Div("ss-set-disc");
                var tex = SeasonSheet.SetDisc(id);
                if (tex != null) disc.style.backgroundImage = new StyleBackground(tex);
                var fill = El.Div("ss-set-bar-fill");
                fill.style.scale = new Scale(new Vector3(set.Total > 0 ? set.Collected / (float)set.Total : 0f, 1f, 1f));
                row.Add(disc);
                row.Add(El.Div("ss-set-col", El.Text(set.Name, "ss-set-name"), El.Text(SeasonText.F("book.count", set.Collected, set.Total), "ss-set-count"), El.Div("ss-set-bar", fill)));
                if (set.Completed) row.Add(El.Div("ss-set-check", new UiIcon("check")));
                foreach (var c in row.Query<VisualElement>().ToList()) if (c != row) c.pickingMode = PickingMode.Ignore;
                _sets.Add(row);
            }
            var cur = b.Sets.FirstOrDefault(x => x.SetId == _setId);
            _grid.Clear();
            if (cur == null) return;
            _title.text = cur.Name;
            _count.text = SeasonText.F("book.count", cur.Collected, cur.Total);
            _reward.Clear();
            string rewardName = SeasonRewards.Pretty(cur.RewardCosmeticId);
            _reward.EnableInClassList("ss-album-reward--done", cur.Completed);
            _reward.Add(new UiIcon(cur.Completed ? "check" : "tshirt"));
            _reward.Add(El.Text(cur.Completed ? SeasonText.F("book.rewardGot", rewardName) : SeasonText.F("book.reward", rewardName), null));
            var discTex = SeasonSheet.SetDisc(cur.SetId);
            // found first in the book's order, then the "?" tiles (the shape of the set is visible from day one)
            foreach (var n in cur.Nods.OrderByDescending(x => x.Found))
            {
                var id = n.NodId;
                var tile = SeasonSheet.Btn("ss-nod" + (n.Found ? "" : " ss-nod--missing") + (id == _nodId ? " ss-nod--sel" : ""), () => { _nodId = id; Render(); }, "season-nod-" + id);
                var disc = El.Div("ss-nod-disc");
                if (n.Found && discTex != null) disc.style.backgroundImage = new StyleBackground(discTex);
                else disc.Add(new UiIcon("question"));
                tile.Add(disc);
                tile.Add(El.Text(n.Found ? n.Name : "???", "ss-nod-name"));
                foreach (var c in tile.Query<VisualElement>().ToList()) if (c != tile) c.pickingMode = PickingMode.Ignore;
                _grid.Add(tile);
            }
            if (_nodId == null || cur.Nods.All(x => x.NodId != _nodId)) _nodId = cur.Nods.FirstOrDefault(x => x.Found)?.NodId ?? cur.Nods.FirstOrDefault()?.NodId;
            foreach (var t in _grid.Children()) t.EnableInClassList("ss-nod--sel", t.name == "season-nod-" + _nodId);
            Detail(cur, cur.Nods.FirstOrDefault(x => x.NodId == _nodId), discTex);
        }

        private void Detail(BookSet set, BookNod n, Texture2D discTex)
        {
            _detail.Clear();
            if (n == null) return;
            _detail.EnableInClassList("ss-detail--missing", !n.Found);
            var disc = El.Div("ss-detail-disc");
            if (n.Found && discTex != null) disc.style.backgroundImage = new StyleBackground(discTex);
            else disc.Add(new UiIcon("question"));
            _detail.Add(disc);
            if (!n.Found)
            {
                _detail.Add(El.Text(SeasonText.T("book.unknown"), "ss-detail-name"));
                _detail.Add(El.Text(set.Name.ToUpperInvariant(), "ss-detail-set"));
                _detail.Add(El.Text(SeasonText.T("book.hint"), "ss-detail-quote"));
                return;
            }
            var lore = Lore(n.NodId);
            _detail.Add(El.Text(n.Name, "ss-detail-name"));
            _detail.Add(El.Text(set.Name.ToUpperInvariant(), "ss-detail-set"));
            var lines = El.Div("ss-detail-lines");
            void Line(string k, string v) { if (!string.IsNullOrEmpty(v)) lines.Add(El.Div("ss-detail-line", El.Text(k, "ss-detail-key"), El.Text(v, "ss-detail-val"))); }
            Line(SeasonText.T("book.district"), District(lore.district));
            Line(SeasonText.T("book.kind"), lore.category);
            Line(SeasonText.T("book.rarity"), SeasonText.T("tier." + (n.Tier ?? "C")));
            if (n.CollectedAtMs.HasValue) Line(SeasonText.T("book.found"), DateTimeOffset.FromUnixTimeMilliseconds(n.CollectedAtMs.Value).ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture));
            _detail.Add(lines);
            if (!string.IsNullOrEmpty(lore.nick)) _detail.Add(El.Text(SeasonText.F("book.knownAs", lore.nick), "ss-detail-quote"));
        }

        private static string District(string d)
        {
            if (string.IsNullOrEmpty(d)) return null;
            var w = d.Replace('_', ' ');
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w);
        }
    }

    /// <summary>The Hunters tab: most nods found (GET /nods/leaderboard), the player's row pinned under the top.</summary>
    internal sealed class HuntersPanel : VisualElement, ISeasonPanel
    {
        private readonly SeasonSheet _sheet;
        private readonly VisualElement _list;
        private readonly Label _total;
        private bool _loading;

        public HuntersPanel(SeasonSheet sheet)
        {
            _sheet = sheet;
            AddToClassList("ss-hunters");
            _total = El.Text("", "ss-board-total");
            _list = El.Div();
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.flexGrow = 1;
            scroll.Add(_list);
            var board = El.Div("ss-board", El.Div("ss-board-head", new UiIcon("trophy"), El.Text(SeasonText.T("hunt.title"), "ss-board-title"), _total), scroll);
            board.name = "season-hunters";
            Add(board);
        }

        public async void Shown()
        {
            if (_loading) return;
            _loading = true;
            if (_list.childCount == 0) _list.Add(El.Text(SeasonText.T("ui.loading"), "ss-empty"));
            try
            {
                var b = await _sheet.Svc.Api.Leaderboard(_sheet.Svc.Life);
                if (panel == null) return;
                _list.Clear();
                _total.text = SeasonText.F("hunt.total", b.Total);
                if (b.Top.Count == 0) _list.Add(El.Text(SeasonText.T("hunt.empty"), "ss-empty"));
                foreach (var r in b.Top) _list.Add(Row(r, r.UserId != null && r.UserId == _sheet.Svc.UserId));
                if (b.Me != null && b.Top.All(r => r.UserId != b.Me.UserId)) { _list.Add(El.Text("···", "ss-empty")); _list.Add(Row(b.Me, true)); }
                Log.Info("season", $"hunters top {b.Top.Count} me {(b.Me != null ? b.Me.Rank.ToString() : "-")}");
            }
            catch (Exception e)
            {
                _list.Clear();
                _list.Add(El.Text(CityErrors.From(e, false).Message, "ss-empty"));
            }
            finally { _loading = false; }
        }

        private static VisualElement Row(HunterRow r, bool me)
        {
            var row = El.Div("ss-row" + (me ? " ss-row--me" : "") + (r.Rank >= 1 && r.Rank <= 3 ? " ss-row--top" + r.Rank : ""));
            row.Add(El.Text("#" + r.Rank, "ss-row-rank"));
            row.Add(El.Text(string.IsNullOrEmpty(r.Alias) ? "anon" : r.Alias, "ss-row-name"));
            row.Add(El.Text(r.Count.ToString(CultureInfo.InvariantCulture), "ss-row-count"));
            row.Add(El.Text(SeasonText.T("hunt.unit"), "ss-row-unit"));
            return row;
        }
    }
}
