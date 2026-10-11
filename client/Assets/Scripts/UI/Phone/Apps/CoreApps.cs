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
            var s = new ScrollView(ScrollViewMode.Vertical);
            s.AddToClassList("ct-app-page");
            foreach (var c in children) if (c != null) s.Add(c);
            return s;
        }

        public static string Err(Exception e) => e is ApiException api ? Strings.Error(api.Code, api.ServerMessage) : e.Message;
    }

    /// <summary>Travel (feature #4).</summary>
    public sealed class MapApp : PhoneApp
    {
        private readonly Action<string> _travel;
        public MapApp(Action<string> travel) { _travel = travel; }
        public override string Id => "map";
        public override string Title => "Map";
        public override string Color => "#20bf6b";

        public override VisualElement Open()
        {
            var reg = Services.Get<ConfigStore>().Registries;
            var page = AppUi.Page();
            foreach (var d in reg.Districts)
            {
                page.Add(El.Text(d.Value, "ct-h2"));
                foreach (var v in reg.VenueOrder.Where(v => v.districtId == d.Key))
                {
                    bool open = v.phase <= Phone.CurrentPhase;
                    var id = v.id;
                    var b = El.Btn(v.name + (open ? "" : $"  · Phase {v.phase}"), () => { Phone.Close(); _travel(id); }, open ? "ct-row-btn" : "ct-row-btn ct-venue-locked");
                    b.SetEnabled(open);
                    page.Add(b);
                }
            }
            return page;
        }
    }

    /// <summary>Bank: Cash and debt. Top-ups (USDC/SOL/SKR/ORE) join in Phase 2.</summary>
    public sealed class BankApp : PhoneApp
    {
        public override string Id => "bank";
        public override string Title => "Bank";
        public override string Color => "#f7b731";

        public override VisualElement Open()
        {
            var p = Services.Get<PlayerProfile>();
            return AppUi.Page(
                El.Text("Cash", "ct-small ct-muted"),
                El.Text(Money.Cash(p.CashCents), "ct-big-number"),
                El.Text(p.DebtCents > 0 ? $"Debt: {Money.Cash(p.DebtCents)} (repaid $120 a week)" : "No debt", "ct-body"),
                El.Text("Cash is game money. It can never be withdrawn.", "ct-small ct-muted"),
                El.Btn("Top up Cash (Phase 2)", () => Bus.Publish(ToastSignal.Text("Top-ups arrive in Phase 2")), "ct-btn ct-btn-ghost"));
        }
    }

    /// <summary>Net Worth and Weekly PnL boards (feature #11).</summary>
    public sealed class RichListApp : PhoneApp
    {
        public override string Id => "richlist";
        public override string Title => "Rich List";
        public override string Color => "#8854d0";

        public override VisualElement Open()
        {
            var list = El.Div("ct-col");
            var tabs = El.Div("ct-row");
            var page = AppUi.Page(tabs, list);
            void Load(string board)
            {
                list.Clear();
                list.Add(El.Text("Loading…", "ct-small"));
                Fetch(board, list);
            }
            tabs.Add(El.Btn("Net Worth", () => Load("net_worth"), "ct-chip"));
            tabs.Add(El.Btn("Weekly PnL", () => Load("weekly_pnl"), "ct-chip"));
            Load("net_worth");
            return page;
        }

        private static async void Fetch(string board, VisualElement list)
        {
            try
            {
                var lb = await Services.Get<Api>().Leaderboard(board);
                list.Clear();
                foreach (var e in lb.Entries ?? new List<BoardEntry>())
                    list.Add(El.Div("ct-lb-row", El.Text("#" + e.Rank, "ct-lb-rank"), El.Text(e.Alias, "ct-lb-name"), El.Text(Money.Cash(e.Value, true), "ct-lb-value")));
                if (lb.Me != null) list.Add(El.Text($"You: #{lb.Me.Rank} · {Money.Cash(lb.Me.Value, true)}", "ct-body"));
            }
            catch (Exception e)
            {
                list.Clear();
                list.Add(El.Text(AppUi.Err(e), "ct-error"));
            }
        }
    }

    /// <summary>Home and needs (feature #8): eat, sleep, shower, touch grass.</summary>
    public sealed class HomeApp : PhoneApp
    {
        public override string Id => "home";
        public override string Title => "Home";
        public override string Color => "#fa8231";

        public override VisualElement Open()
        {
            var p = Services.Get<PlayerProfile>();
            var cfg = Services.Get<ConfigStore>().Config("needs");
            var page = AppUi.Page(El.Text("Home: " + (p.Me?.HomeId ?? "none"), "ct-h2"));
            var status = El.Text("", "ct-small");
            foreach (var action in new[] { "eat", "sleep", "shower", "touch_grass" })
            {
                var a = action;
                var cost = cfg?["actions"]?[a]?.Long("costCents") ?? 0;
                page.Add(El.Btn(Label(a) + (cost > 0 ? $"  ({Money.Cash(cost)})" : ""), async () =>
                {
                    status.text = "…";
                    try
                    {
                        var r = await Services.Get<Api>().NeedsAction(a);
                        p.SetNeeds(r.Needs);
                        p.SetCash(r.CashCents);
                        status.text = "Done";
                        Bus.Publish(new SfxSignal("sfx_ui_success"));
                    }
                    catch (Exception e)
                    {
                        status.text = AppUi.Err(e);
                    }
                }, "ct-row-btn"));
            }
            page.Add(status);
            return page;
        }

        private static string Label(string a) => a switch
        {
            "eat" => "Eat",
            "sleep" => "Sleep",
            "shower" => "Shower",
            "touch_grass" => "Touch grass",
            _ => a,
        };
    }

    public sealed class ProfileApp : PhoneApp
    {
        public override string Id => "profile";
        public override string Title => "Profile";
        public override string Color => "#2d98da";

        public override VisualElement Open()
        {
            var p = Services.Get<PlayerProfile>();
            var me = p.Me;
            var reg = Services.Get<ConfigStore>().Registries;
            var page = AppUi.Page(
                El.Text(p.Alias + (me?.User?.IsSeeker == true ? " ✓" : ""), "ct-h1"),
                El.Text(reg.Tribe(me?.Character?.TribeId)?.name ?? "", "ct-body"),
                El.Text("Wallet " + Short(me?.User?.Wallet) + (string.IsNullOrEmpty(me?.User?.SkrName) ? "" : " · " + me.User.SkrName), "ct-small ct-muted"),
                El.Text("Miner tier: " + (me?.MinerTier.ToString() ?? "none"), "ct-small"));
            if (me?.Skills != null)
            {
                var s = me.Skills;
                page.Add(El.Text("Skills", "ct-h2"));
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
        public override string Id => "settings";
        public override string Title => "Settings";
        public override string Color => "#4b6584";

        public override VisualElement Open()
        {
            var am = CTLife.Audio.AudioManager.Instance;
            var page = AppUi.Page(El.Text("Settings", "ct-h1"));
            if (am != null)
            {
                page.Add(Slider("Music", am.musicVolume, v => am.musicVolume = v));
                page.Add(Slider("Sound effects", am.sfxVolume, v => am.sfxVolume = v));
            }
            var cfg = Services.Get<ConfigStore>();
            page.Add(El.Text($"Version {Semver.ClientVersion} · contract {Semver.ContractVersion} · config {cfg.Version} ({cfg.Source}) · env {Env.Current.id}", "ct-small ct-muted"));
            page.Add(El.Btn("Sign out", () => { Phone.Close(); _signOut?.Invoke(); }, "ct-btn ct-btn-danger"));
            return page;
        }

        private static VisualElement Slider(string label, float value, Action<float> set)
        {
            var s = new Slider(label, 0f, 1f) { value = value };
            s.AddToClassList("ct-slider");
            s.RegisterValueChangedCallback(e => set(e.newValue));
            return s;
        }
    }
}
