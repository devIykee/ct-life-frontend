using System;
using CTLife.Core;
using CTLife.UI;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;
using Motion = CTLife.UI.Kit.Motion;

namespace CTLife.CityLife
{
    /// <summary>
    /// The dashboard's gm streak tile (2026-10-24): the lobby's angled event-tile language, taller. A flame with the day
    /// count, seven pips to the next milestone (gold on milestone days), freezes held, and either a pulsing "Open chest"
    /// pill or a live countdown to the UTC reset. Tapping it opens <see cref="DailyChestSheet"/>. Hidden until the server
    /// answers (the lobby shows nothing it cannot back). Starts the chest's async load and off-screen prewarm.
    /// </summary>
    public sealed class GmStreakTile : Button
    {
        private static readonly Color Flame = new Color32(0xff, 0x8a, 0x1f, 0xff), Gold = new Color32(0xff, 0xc8, 0x3c, 0xff);
        private RetentionService _svc;
        private readonly SkewBox _box;
        private readonly Label _caption, _day, _status, _freezes, _milestone;
        private readonly VisualElement _pips, _flameGlow, _statusPill, _freezeChip;
        private readonly IVisualElementScheduledItem _tick;
        private float _pulse;
        private int _shownDay = -1;
        private DailyStatus _seen;   // the service replaces Status on every change: a new reference means re-render

        public GmStreakTile()
        {
            _svc = RetentionService.Current;
            name = "home-gm-streak";
            var sheet = Resources.Load<StyleSheet>("UI/retention");
            if (sheet != null) styleSheets.Add(sheet);
            AddToClassList("home-today-chip");
            AddToClassList("gm-tile");
            clicked += Open;
            _box = new SkewBox("home-today-box gm-tile-box") { Fill = LobbyArt.Dark, Edge = Flame }.Shape(0f, 18f);
            _box.pickingMode = PickingMode.Ignore;

            var flame = El.Div("gm-tile-flame");
            flame.pickingMode = PickingMode.Ignore;
            _flameGlow = El.Div("gm-tile-flame-glow");
            _flameGlow.pickingMode = PickingMode.Ignore;
            flame.Add(_flameGlow);
            flame.Add(LobbyArt.Icon("fire-element", "gm-tile-flame-icon"));

            _caption = El.Text(RetentionText.T("tile.caption"), "home-today-caption gm-tile-caption");
            _caption.style.color = Flame;
            _milestone = El.Text(RetentionText.T("tile.milestone"), "gm-tile-milestone");
            var capRow = El.Div("gm-tile-caprow", _caption, _milestone);
            _day = El.Text("", "gm-tile-day");
            _freezes = El.Text("", "gm-tile-freeze-count");
            _freezeChip = El.Div("gm-tile-freeze", LobbyArt.Icon("snowflake", "gm-tile-freeze-icon"), _freezes);
            var dayRow = El.Div("gm-tile-dayrow", _day, El.Div("ct-grow"), _freezeChip);
            _pips = El.Div("gm-tile-pips");
            _status = El.Text("", "gm-tile-status");
            _statusPill = El.Div("gm-tile-pill", _status);
            var copy = El.Div("home-today-copy gm-tile-copy", capRow, dayRow, _pips, _statusPill);
            foreach (var e in new VisualElement[] { capRow, _caption, _milestone, dayRow, _day, _freezeChip, _freezes, _pips, _status, _statusPill, copy })
                e.pickingMode = PickingMode.Ignore;
            _box.Add(flame);
            _box.Add(copy);
            Add(_box);
            style.display = DisplayStyle.None;

            Attach();
            _tick = schedule.Execute(Tick).Every(50);
            Refresh();
            _ = ChestStage.Prewarm();
            DailyChestSheet.PrewarmAudio();
        }

        /// <summary>Binds to the signed-in player's service (it can arrive a moment after the lobby is built).</summary>
        private void Attach()
        {
            var svc = RetentionService.Current;
            if (svc == null || ReferenceEquals(svc, _svc)) return;
            _svc = svc;
            _ = _svc.RefreshStatus(force: false);
            Refresh();
        }

        private void Open()
        {
            if (_svc?.Status == null) return;
            DeviceFeel.Tick();
            Cues.Play(Cues.SfxTap);
            DailyChestSheet.Show(_svc);
        }

        private void Refresh()
        {
            var s = _svc?.Status;
            _seen = s;
            style.display = s == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (s == null) return;
            int day = s.ShownDay;
            bool milestone = s.ChestReady && DailyStatus.IsMilestone(s.ChestDay);
            _day.text = RetentionText.F("tile.day", day);
            _milestone.Show(milestone);
            _box.Edge = milestone ? Gold : Flame;
            _box.Repaint();
            _caption.style.color = milestone ? Gold : Flame;
            _freezeChip.Show(s.Freezes > 0);
            _freezes.text = s.Freezes.ToString();
            EnableInClassList("gm-tile--ready", s.ChestReady);
            EnableInClassList("gm-tile--milestone", milestone);
            int pipKey = day * 2 + (s.ClaimedToday ? 1 : 0);
            if (_shownDay != pipKey) { _shownDay = pipKey; BuildPips(s); }
            UpdateStatus(s);
            tooltip = RetentionText.F("tile.access", day, _status.text);
        }

        /// <summary>Seven pips: the current run of 7 (8-14, 15-21, ...), filled up to the day, the milestone day in gold.</summary>
        private void BuildPips(DailyStatus s)
        {
            _pips.Clear();
            int day = s.ShownDay, start = (day - 1) / 7 * 7 + 1;
            int filled = s.ClaimedToday ? day : day - 1;
            for (int d = start; d < start + 7; d++)
            {
                var p = El.Div("gm-pip");
                p.pickingMode = PickingMode.Ignore;
                p.EnableInClassList("gm-pip--done", d <= filled);
                p.EnableInClassList("gm-pip--today", d == day && s.ChestReady);
                p.EnableInClassList("gm-pip--milestone", DailyStatus.IsMilestone(d) || d == start + 6);
                _pips.Add(p);
            }
        }

        private void UpdateStatus(DailyStatus s)
        {
            _status.text = s.ChestReady ? RetentionText.T("tile.ready")
                : RetentionText.F("tile.next", DailyStatus.Countdown(s.SecondsToReset(ServerClock.NowMs)));
        }

        private void Tick()
        {
            if (_svc == null || _svc.Disposed) { if (Time.frameCount % 20 == 0) Attach(); return; }
            var s = _svc.Status;
            if (!ReferenceEquals(s, _seen)) Refresh();
            if (s == null || panel == null) return;
            if (!s.ChestReady) { if (Time.frameCount % 10 == 0) UpdateStatus(s); _flameGlow.style.opacity = 0.35f; return; }
            // the ready state breathes: the flame's glow and the pill
            _pulse += 0.05f;
            float k = 0.5f + 0.5f * Mathf.Sin(_pulse * 3.2f);
            _flameGlow.style.opacity = 0.45f + 0.55f * k;
            float sc = 1f + 0.045f * k;
            _statusPill.style.scale = new Scale(new Vector3(sc, sc, 1f));
        }
    }
}
