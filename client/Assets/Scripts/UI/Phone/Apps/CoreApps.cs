using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    internal static class AppUi
    {
        public static ScrollView Page(params VisualElement[] children)
        {
            var s = El.VerticalScroll();
            s.AddToClassList("ct-app-page");
            foreach (var c in children) if (c != null) s.Add(c);
            return s;
        }

        public static string Err(Exception e)
        {
            // Expected API/network failures are shown in the app; logging them as exceptions popped the red
            // development console over gameplay on the Seeker (device QA bug 8).
            if (e is ApiException expected) Log.Warn("phone", expected.Message);
            else Log.Exception("phone", e);
            return e is ApiException api ? Strings.Error(api.Code, api.ServerMessage) : Strings.T("common.error");
        }
    }

    /// <summary>Travel (feature #4).</summary>
    public sealed class MapApp : PhoneApp
    {
        private readonly Action<string> _travel;
        public MapApp(Action<string> travel) { _travel = travel; }
        public override string Id => "map";
        public override string Title => Strings.T("app.map");
        public override string Color => "#20bf6b";

        public override VisualElement Open()
        {
            var reg = Services.Get<ConfigStore>().Registries;
            return CityMapScreen.BuildGuide(reg, Phone.CurrentPhase, id =>
            {
                Phone.Close();
                _travel?.Invoke(id);
            });
        }
    }

    /// <summary>Bank: Cash and debt. Top-ups (USDC/SOL/SKR/ORE) join in Phase 2.</summary>
    public sealed class BankApp : PhoneApp
    {
        public override string Id => "bank";
        public override string Title => Strings.T("app.bank");
        public override string Color => "#f7b731";

        public override VisualElement Open()
        {
            var p = Services.Get<PlayerProfile>();
            return AppUi.Page(
                El.Text(Strings.T("bank.title"), "ct-h1"),
                El.Div("ct-bank-hero", El.Text(Strings.T("bank.balance"), "ct-eyebrow"),
                    El.Text(Money.Cash(p.CashCents), "ct-big-number"), El.Text(Strings.T("bank.note"), "ct-small ct-muted")),
                El.Div("ct-card", El.Text(p.DebtCents > 0 ? Strings.F("bank.debt", Money.Cash(p.DebtCents)) : Strings.T("bank.noDebt"), "ct-body")),
                El.Btn(Strings.T("bank.topup"), () => Bus.Publish(ToastSignal.Text(Strings.T("bank.topup"))), "ct-btn ct-btn-ghost"));
        }
    }

    /// <summary>Net Worth and Weekly PnL boards (feature #11).</summary>
    public sealed class RichListApp : PhoneApp
    {
        public override string Id => "richlist";
        public override string Title => Strings.T("app.richlist");
        public override string Color => "#8854d0";
        private int _requestVersion;

        public override VisualElement Open()
        {
            var list = El.Div("ct-col");
            var tabs = El.Div("ct-app-tabs");
            var page = AppUi.Page(El.Text(Strings.T("rich.title"), "ct-h1"), tabs, list);
            void Load(string board)
            {
                list.Clear();
                list.Add(El.Text(Strings.T("common.loading"), "ct-small ct-muted"));
                foreach (var tab in tabs.Children()) tab.EnableInClassList("ct-chip-on", tab.name == board);
                Fetch(board, list, ++_requestVersion);
            }
            var net = El.Btn(Strings.T("rich.netWorth"), () => Load("net_worth"), "ct-chip");
            net.name = "net_worth";
            var weekly = El.Btn(Strings.T("rich.weekly"), () => Load("weekly_pnl"), "ct-chip");
            weekly.name = "weekly_pnl";
            tabs.Add(net);
            tabs.Add(weekly);
            Load("net_worth");
            return page;
        }

        public override void Closed() => _requestVersion++;

        private async void Fetch(string board, VisualElement list, int version)
        {
            try
            {
                var lb = await Services.Get<Api>().Leaderboard(board);
                if (version != _requestVersion) return;
                list.Clear();
                foreach (var e in lb.Entries ?? new List<BoardEntry>())
                    list.Add(El.Div("ct-lb-row", El.Text("#" + e.Rank, "ct-lb-rank"), El.Text(e.Alias, "ct-lb-name"), El.Text(Money.Cash(e.Value, true), "ct-lb-value")));
                if (lb.Entries == null || lb.Entries.Count == 0) list.Add(El.Text(Strings.T("rich.empty"), "ct-body ct-muted"));
                if (lb.Me != null) list.Add(El.Text(Strings.F("rich.you", lb.Me.Rank, Money.Cash(lb.Me.Value, true)), "ct-body"));
            }
            catch (Exception e)
            {
                if (version != _requestVersion) return;
                list.Clear();
                list.Add(El.Text(AppUi.Err(e), "ct-error"));
            }
        }
    }

    public sealed class ProfileApp : PhoneApp
    {
        public override string Id => "profile";
        public override string Title => Strings.T("app.profile");
        public override string Color => "#2d98da";

        public override VisualElement Open()
        {
            var p = Services.Get<PlayerProfile>();
            var me = p.Me;
            var reg = Services.Get<ConfigStore>().Registries;
            var page = AppUi.Page(
                El.Text(p.Alias + (me?.User?.IsSeeker == true ? " ✓" : ""), "ct-h1"),
                El.Text(reg.Tribe(me?.Character?.TribeId)?.name ?? "", "ct-body"),
                El.Text(Strings.F("profile.wallet", Short(me?.User?.Wallet)) + (string.IsNullOrEmpty(me?.User?.SkrName) ? "" : " · " + me.User.SkrName), "ct-small ct-muted"),
                El.Text(Strings.F("profile.miner", Strings.ReadableId(me?.MinerTier.ToString())), "ct-small"));
            if (me?.Skills != null)
            {
                var s = me.Skills;
                page.Add(El.Text(Strings.T("profile.skills"), "ct-h2"));
                foreach (var (name, v) in new[] { ("Clout", s.Clout), ("Coding", s.Coding), ("Trading", s.Trading), ("Writing", s.Writing), ("Design", s.Design), ("Security", s.Security) })
                    page.Add(El.Div("ct-lb-row", El.Text(name, "ct-lb-name"), El.Text(v + "/10", "ct-lb-value")));
            }
            return page;
        }

        private static string Short(string w) => string.IsNullOrEmpty(w) || w.Length < 10 ? w : w.Substring(0, 4) + "…" + w.Substring(w.Length - 4);
    }

    public sealed class SettingsApp : PhoneApp
    {
        private readonly Action _signOut;
        public SettingsApp(Action signOut) { _signOut = signOut; }

        /// <summary>An extra settings section from a feature lane (Phase 6 verified X link); null = none.</summary>
        public static Func<VisualElement> ExtraSection;
        public override string Id => "settings";
        public override string Title => Strings.T("app.settings");
        public override string Color => "#4b6584";

        public override VisualElement Open()
        {
            var am = CTLife.Audio.AudioManager.Instance;
            var page = AppUi.Page(El.Text(Strings.T("settings.note"), "ct-body ct-muted"));
            var controls = El.Div("ct-app-tabs");
            var controlHelp = El.Text("", "ct-small ct-muted");
            Button analog = null, arrows = null;
            analog = El.Btn(Strings.T("controls.analog"), () => SelectControls(MovementControlMode.Analog), "ct-chip");
            arrows = El.Btn(Strings.T("controls.arrows"), () => SelectControls(MovementControlMode.Arrows), "ct-chip");
            void SelectControls(MovementControlMode mode)
            {
                ControlSettings.MovementMode = mode;
                analog.EnableInClassList("ct-chip-on", mode == MovementControlMode.Analog);
                arrows.EnableInClassList("ct-chip-on", mode == MovementControlMode.Arrows);
                controlHelp.text = Strings.T(mode == MovementControlMode.Analog ? "controls.analogHelp" : "controls.arrowsHelp");
            }
            controls.Add(analog);
            controls.Add(arrows);
            SelectControls(ControlSettings.MovementMode);
            page.Add(El.Text(Strings.T("controls.title"), "ct-section-title"));
            page.Add(controls);
            page.Add(controlHelp);
            if (am != null)
            {
                page.Add(Slider(Strings.T("settings.master"), am.masterVolume, v => am.masterVolume = v));
                page.Add(Slider(Strings.T("settings.music"), am.musicVolume, v => am.musicVolume = v));
                page.Add(Slider(Strings.T("settings.sound"), am.sfxVolume, v => am.sfxVolume = v));
                page.Add(Slider(Strings.T("settings.voice"), am.voiceVolume, v => am.voiceVolume = v));
                page.Add(Slider(Strings.T("settings.ambience"), am.ambienceVolume, v => am.ambienceVolume = v));
            }
            var extra = ExtraSection?.Invoke(); if (extra != null) page.Add(extra);
            page.Add(AssetCredits.Build());
            page.Add(El.Text(Strings.F("settings.version", Semver.ClientVersion), "ct-small ct-muted"));
            page.Add(El.Btn(Strings.T("settings.signout"), () => { Phone.Close(); _signOut?.Invoke(); }, "ct-btn ct-btn-danger", sfx: null));
            return page;
        }

        /// <summary>
        /// Volume as a −/+ stepper (10% steps). Device QA: a Slider inside the scrolling settings page took vertical
        /// swipes that started near it as value drags (Sound effects jumped 100% → 50%). Buttons only react to taps.
        /// </summary>
        private static VisualElement Slider(string label, float value, Action<float> set)
        {
            float current = UnityEngine.Mathf.Clamp01(value);
            var readout = El.Text("", "ct-stepper-value");
            void Apply(float next)
            {
                current = VolumeStep.Clamp(next);
                readout.text = UnityEngine.Mathf.RoundToInt(current * 100f) + "%";
                set(current);
            }
            var minus = El.Btn("−", () => Apply(VolumeStep.Down(current)), "ct-btn ct-btn-ghost ct-stepper-btn");
            var plus = El.Btn("+", () => Apply(VolumeStep.Up(current)), "ct-btn ct-btn-ghost ct-stepper-btn");
            readout.text = UnityEngine.Mathf.RoundToInt(current * 100f) + "%";
            var row = El.Div("ct-row ct-stepper", El.Text(label, "ct-body ct-grow"), minus, readout, plus);
            row.name = "volume-" + label;
            return row;
        }
    }
}

namespace CTLife.UI
{
    /// <summary>Pure volume stepping for the settings steppers (unit-tested).</summary>
    public static class VolumeStep
    {
        public const float Step = 0.1f;
        public static float Clamp(float v) => v < 0f ? 0f : v > 1f ? 1f : (float)System.Math.Round(v, 2);
        public static float Up(float v) => Clamp((float)System.Math.Round(v / Step) * Step + Step);
        public static float Down(float v) => Clamp((float)System.Math.Round(v / Step) * Step - Step);
    }
}
