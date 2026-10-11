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
            var identity = El.Div("ct-account-identity",
                El.Div("ct-account-avatar", new UiIcon("profile")),
                El.Div("ct-grow", El.Text(p.Alias, "ct-account-name"),
                    El.Text(reg.Tribe(me?.Character?.TribeId)?.name ?? "", "ct-account-subtitle")));
            var badges = El.Div("ct-account-badges",
                PhonePresentation.Status(Strings.F("profile.miner", Strings.ReadableId(me?.MinerTier.ToString())), PhonePresentation.Tone.Accent));
            if (me?.User?.IsSeeker == true) badges.Add(PhonePresentation.Status(Strings.T("profile.seeker"), PhonePresentation.Tone.Success));
            var page = AppUi.Page(El.Div("ct-account-card", identity, badges,
                El.Text(Strings.F("profile.wallet", Short(me?.User?.Wallet)) + (string.IsNullOrEmpty(me?.User?.SkrName) ? "" : " · " + me.User.SkrName), "ct-account-wallet")));
            page.AddToClassList("ct-account-page");
            if (me?.Skills != null)
            {
                var s = me.Skills;
                page.Add(El.Text(Strings.T("profile.skills"), "ct-h2"));
                var skills = El.Div("ct-account-card");
                foreach (var (name, v) in new[] { ("clout", s.Clout), ("coding", s.Coding), ("trading", s.Trading), ("writing", s.Writing), ("design", s.Design), ("security", s.Security) })
                {
                    var fill = El.Div("ct-account-skill-fill");
                    fill.style.width = Length.Percent(UnityEngine.Mathf.Clamp(v, 0, 10) * 10f);
                    skills.Add(El.Div("ct-account-skill", El.Div("ct-account-skill-heading",
                        El.Text(Strings.T("profile.skill." + name), "ct-body ct-grow"), El.Text(v + "/10", "ct-account-skill-value")),
                        El.Div("ct-account-skill-track", fill)));
                }
                page.Add(skills);
            }
            return page;
        }

        private static string Short(string w) => string.IsNullOrEmpty(w) || w.Length < 10 ? w : w.Substring(0, 4) + "…" + w.Substring(w.Length - 4);
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
