using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// The always-visible dream ladder (queue 18, approved 2026-10-25): Basement $20 → Hacker House $60 → Studio $180 →
    /// Penthouse $1,500/wk as the Cash rungs, the hovercar above as a locked Dream badge (an entitlement, never Cash) that
    /// opens the Duo Shop. Feeds the HUD <see cref="LadderChip"/> from /me (home, Cash), the entitlements and the rent
    /// catalogue (`ids.homes` + `economy.rentCentsPerWeek`), and opens the ladder sheet (W6) when the chip is tapped.
    /// When saved Cash first covers the next rung's goal (4 weeks of its rent): "STUDIO UNLOCKED" and a Duo banner to Home.
    /// </summary>
    public sealed class DreamLadderPresenter : IDisposable
    {
        private readonly VisualElement _layer;
        private readonly string _user;
        private readonly Subscriptions _subs = new Subscriptions();
        private IEntitlements _ents;
        private ConfigStore _config;
        private LadderState _state;
        private List<HomeOffer> _homes = new List<HomeOffer>();
        private VisualElement _sheet;
        private bool _earnOpen, _disposed, _seen, _couldReach;
        private LadderRung? _couldReachRung;

        public DreamLadderPresenter(VisualElement layer, string userId)
        {
            _layer = layer;
            _user = userId;
            _subs.On<ProfileChangedSignal>(_ => Refresh());
            if (Services.TryGet<IEntitlements>(out var e)) { _ents = e; _ents.Changed += Refresh; }
            if (Services.TryGet<ConfigStore>(out var c)) { _config = c; _config.Changed += OnConfig; }
            LadderChip.Tapped += Open;
            OnConfig();
        }

        private void OnConfig()
        {
            if (_config != null && _config.IsLoaded)
                try { _homes = HomeCatalog.FromRegistry(_config.Ids("homes"), _config.Config("economy")); } catch (Exception) { _homes = new List<HomeOffer>(); }
            Refresh();
        }

        private Dictionary<string, long> Rents()
        {
            var d = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var h in _homes) if (h.RentCents > 0) d[h.Id] = h.RentCents;
            return d;
        }

        private Me MyProfile()
        {
            if (!Services.TryGet<PlayerProfile>(out var p) || p.Me == null || p.UserId != _user || !p.HasCharacter) return null;
            return p.Me;
        }

        private void Refresh()
        {
            if (_disposed) return;
            var me = MyProfile();
            if (me == null || _homes.Count == 0) { LadderChip.Hide(); return; }
            bool hover = _ents != null && _ents.Owns(Entitlement.Hovercar);
            // saved means Cash beyond what is owed: the birth lottery's debt is not progress toward the next rent
            _state = DreamLadder.Compute(me.HomeId, false, me.CashCents - Math.Max(0, me.DebtCents), hover, Rents());
            string from = RungName(_state.Current);
            if (!_state.Next.HasValue) LadderChip.Show(from, FirstText.T("chip.dream"), 1f, true);
            else if (_state.NextIsEntitlement) LadderChip.Show(from, RungName(LadderRung.Hovercar), 0f, true);
            else LadderChip.Show(from, RungName(_state.Next.Value), _state.Fraction, false);
            CheckUnlocked();
            if (_sheet != null) Render();
        }

        public static string RungName(LadderRung r) => Strings.T("ladder." + r);

        /// <summary>
        /// The next Cash rung just became affordable while the player is in the city: the MISSION PASSED line and a banner to
        /// Home, once per rung. The first reading is a baseline (a rich start is not an unlock), and the onboarding screens
        /// never celebrate.
        /// </summary>
        private void CheckUnlocked()
        {
            if (_state == null) return;
            bool can = _state.CanReachNext && _state.Next.HasValue;
            var rung = _state.Next;
            bool baseline = !_seen;
            _seen = true;
            if (!can) { _couldReach = false; return; }
            if (baseline) { _couldReach = true; _couldReachRung = rung; return; }
            if (_couldReach && _couldReachRung == rung) return;
            if (FirstSessionOverlays.InWorld == null || !FirstSessionOverlays.InWorld()) return;   // the overlays' tick retries
            _couldReach = true; _couldReachRung = rung;
            var key = "ladder.unlocked." + _user + "." + _state.Next.Value;
            try { if (PlayerPrefs.GetInt(key, 0) == 1) return; PlayerPrefs.SetInt(key, 1); PlayerPrefs.Save(); } catch (Exception) { return; }
            if (!Rentable(_state.Next.Value)) return;
            var name = RungName(_state.Next.Value);
            Log.Info("ladder", "rung affordable: " + _state.Next.Value);
            var p = El.Div("fs-passed", El.Text(FirstText.F("ladder.unlocked", name.ToUpperInvariant()), "fs-passed-word"), El.Text(FirstText.T("ladder.unlockedLine"), "fs-passed-sub"));
            p.pickingMode = PickingMode.Ignore;
            _layer.Add(p);
            p.schedule.Execute(() => p.AddToClassList("fs-in")).ExecuteLater(20);
            p.schedule.Execute(() => p.RemoveFromClassList("fs-in")).ExecuteLater(2600);
            p.schedule.Execute(() => p.RemoveFromHierarchy()).ExecuteLater(3000);
            DuoNotificationCenter.Post(new DuoNotification
            {
                AppId = "home", ThreadKey = "ladder", Title = FirstText.T("ladder.dmTitle"), Body = FirstText.F("ladder.dmBody", name),
                Open = () => FirstSessionOverlays.OpenApp?.Invoke("home"),
            });
        }

        /// <summary>The overlays' tick while the player is in the world: a rung that became affordable off-screen celebrates now.</summary>
        public void Tick() => CheckUnlocked();

        private HomeOffer Home(LadderRung r) => DreamLadder.HomeOf.TryGetValue(r, out var id) ? _homes.Find(h => h.Id == id) : null;
        private bool Rentable(LadderRung r) { var h = Home(r); return h != null && h.RentCents > 0 && Phases.IsOpen(Math.Max(1, h.Phase)); }

        // ------------------------------------------------------------------ the sheet (W6)

        private void Open()
        {
            if (_disposed || _sheet != null) return;
            Refresh();
            if (_state == null) return;
            Log.Info("ladder", "sheet open");
            _earnOpen = false;
            _sheet = El.Div("fs-ladder-scrim");
            _sheet.name = "fs-ladder";
            _sheet.RegisterCallback<PointerDownEvent>(e => { if (e.target == _sheet) Close(); });
            _layer.Add(_sheet);
            Render();
            _sheet.schedule.Execute(() => _sheet?.AddToClassList("fs-in")).ExecuteLater(16);
        }

        private void Close()
        {
            var s = _sheet; _sheet = null;
            if (s == null) return;
            s.RemoveFromClassList("fs-in");
            s.schedule.Execute(() => s.RemoveFromHierarchy()).ExecuteLater(220);
        }

        private void Render()
        {
            if (_sheet == null || _state == null) return;
            _sheet.Clear();
            var me = MyProfile();
            var card = El.Div("fs-ladder-card");
            card.name = "fs-ladder-card";

            // the chosen Dream on top: the long-term why
            var dreamId = me?.Character?.DreamId;
            var dream = _config?.Registries?.Dreams.FirstOrDefault(d => d.id == dreamId)?.name;
            var head = El.Div("fs-ladder-head",
                El.Div("fs-ladder-head-copy", El.Text(FirstText.T("ladder.dream"), "fs-ladder-caption"), El.Text(dream ?? FirstText.T("ladder.noDream"), "fs-ladder-dream")),
                El.Btn("×", Close, "fs-ladder-close"));
            card.Add(head);

            // rungs top to bottom: the hovercar badge, then Penthouse → Basement
            var list = El.Div("fs-ladder-rungs");
            list.Add(HovercarRow());
            for (int i = DreamLadder.Cash.Length - 1; i >= 0; i--) list.Add(RungRow(DreamLadder.Cash[i]));
            card.Add(list);

            // progress to the next rung
            var foot = El.Div("fs-ladder-foot");
            if (_state.Next.HasValue && !_state.NextIsEntitlement && _state.GoalCents > 0)
            {
                var fill = El.Div("fs-ladder-fill");
                fill.style.width = Length.Percent(_state.Fraction * 100f);
                foot.Add(El.Div("fs-ladder-bar", fill));
                foot.Add(El.Text(_state.CanReachNext
                    ? FirstText.F("ladder.ready", Money.Cash(_state.ProgressCents), RungName(_state.Next.Value))
                    : FirstText.F("ladder.progress", Money.Cash(Math.Min(_state.ProgressCents, _state.GoalCents)), Money.Cash(_state.GoalCents), DreamLadder.WeeksOfRent), "fs-ladder-note"));
            }
            else foot.Add(El.Text(FirstText.T(_state.Next.HasValue ? "ladder.hovercarLine" : "ladder.topLine"), "fs-ladder-note"));

            var actions = El.Div("fs-ladder-actions");
            if (_state.CanReachNext && _state.Next.HasValue && Rentable(_state.Next.Value))
            {
                var move = El.Btn(FirstText.T("ladder.moveIn"), () => { Close(); FirstSessionOverlays.OpenApp?.Invoke("home"); }, "fs-ladder-pill fs-ladder-pill--go");
                move.name = "fs-ladder-move";
                actions.Add(move);
            }
            var earn = El.Btn(FirstText.T("ladder.earn"), () => { _earnOpen = !_earnOpen; Render(); }, "fs-ladder-pill");
            earn.name = "fs-ladder-earn";
            actions.Add(earn);
            foot.Add(actions);
            if (_earnOpen)
            {
                var chips = El.Div("fs-ladder-earn-row");
                chips.Add(EarnChip("jobs", "ladder.earn.jobs"));
                if (Phases.IsOpen(2) && Features.IsAvailable(Features.Markets)) chips.Add(EarnChip("pulse", "ladder.earn.pulse"));
                chips.Add(EarnChip(DeepLinks.QuestsApp, "ladder.earn.quests"));
                foot.Add(chips);
            }
            card.Add(foot);
            _sheet.Add(card);
        }

        private Button EarnChip(string app, string key)
        {
            var b = El.Btn("", () => { Close(); FirstSessionOverlays.OpenApp?.Invoke(app); }, "fs-ladder-chip");
            b.name = "fs-ladder-earn-" + app;
            b.Add(El.PassThrough(new UiIcon(app == DeepLinks.QuestsApp ? "star" : app)));
            b.Add(El.PassThrough(El.Text(FirstText.T(key), "fs-ladder-chip-text")));
            return b;
        }

        private VisualElement HovercarRow()
        {
            bool owned = _state.HasHovercar;
            var row = El.Btn("", () => { if (!owned) { Close(); OpenShop(); } }, "fs-ladder-rung fs-ladder-rung--dream");
            row.name = "fs-ladder-hovercar";
            row.EnableInClassList("fs-ladder-rung--owned", owned);
            row.Add(El.PassThrough(El.Div("fs-ladder-icon", new UiIcon("car"))));
            row.Add(El.PassThrough(El.Text(RungName(LadderRung.Hovercar), "fs-ladder-name")));
            row.Add(El.PassThrough(El.Text(FirstText.T("ladder.dreamBadge"), "fs-ladder-badge")));
            row.Add(El.PassThrough(El.Div("ct-grow")));
            row.Add(El.PassThrough(El.Text(owned ? FirstText.T("ladder.owned") : FirstText.T("ladder.shop"), "fs-ladder-right")));
            return row;
        }

        /// <summary>The hovercar's page: the same place the HUD's locked hovercar button opens.</summary>
        private static void OpenShop()
        {
            Log.Info("ladder", "hovercar badge -> shop");
            Bus.Publish(new OpenDuoAppRequestSignal { app = "shop" });
        }

        private VisualElement RungRow(LadderRung r)
        {
            var row = El.Div("fs-ladder-rung");
            row.name = "fs-ladder-" + r;
            bool here = r == _state.Current, below = r < _state.Current, next = _state.Next == r;
            row.EnableInClassList("fs-ladder-rung--here", here);
            row.EnableInClassList("fs-ladder-rung--below", below);
            row.EnableInClassList("fs-ladder-rung--next", next);
            row.Add(El.Div("fs-ladder-icon", new UiIcon("home")));
            row.Add(El.Text(RungName(r), "fs-ladder-name"));
            row.Add(El.Div("ct-grow"));
            var h = Home(r);
            string right;
            if (here) right = FirstText.T("ladder.you");
            else if (below) right = FirstText.T("ladder.done");
            else if (next && _state.GoalCents > 0) right = FirstText.F("ladder.next", Money.Cash(_state.GoalCents));
            else if (h != null && h.RentCents > 0 && !Phases.IsOpen(Math.Max(1, h.Phase))) right = FirstText.F("ladder.phase", Money.Cash(h.RentCents), h.Phase);
            else right = h != null && h.RentCents > 0 ? FirstText.F("ladder.rent", Money.Cash(h.RentCents)) : "";
            row.Add(El.Text(right, "fs-ladder-right"));
            return row;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            LadderChip.Tapped -= Open;
            _subs.Dispose();
            if (_ents != null) _ents.Changed -= Refresh;
            if (_config != null) _config.Changed -= OnConfig;
            _sheet?.RemoveFromHierarchy(); _sheet = null;
            LadderChip.Hide();
        }
    }
}
