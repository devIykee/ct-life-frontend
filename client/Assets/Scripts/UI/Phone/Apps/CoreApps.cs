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

    /// <summary>
    /// Travel (feature #4): the CT Island map (map/HUD job, 2026-10-17). The baked painterly island with every venue in its place,
    /// Apple-Maps-like glass controls, search, and a venue card whose Go travels with the existing travel. Replaces the old
    /// district text list; the same <see cref="CTLife.UI.Map.FullMapView"/> is the in-city full-screen map.
    /// </summary>
    public sealed class MapApp : PhoneApp
    {
        private readonly Action<string> _travel;
        public MapApp(Action<string> travel) { _travel = travel; }
        public override string Id => "map";
        public override string Title => Strings.T("app.map");
        public override string Color => "#20bf6b";
        public override DuoChrome Chrome => DuoChrome.Plain;

        public override VisualElement Open()
        {
            var reg = Services.Get<ConfigStore>().Registries;
            var root = El.Div("duo-map-app");
            root.name = "map-page";
            var map = new CTLife.UI.Map.FullMapView(CTLife.UI.Map.FullMapView.Look.Duo)
            {
                Travel = id => { Phone.Close(); _travel?.Invoke(id); },
                OpenHome = () => Phone?.Open("home"),
                Available = () => CityMapScreen.AvailableVenues(reg, Phone.CurrentPhase),
            };
            root.Add(map);
            var back = Duo.Circle(DuoGlyph.Kind.Back, Strings.T("common.back"), () => Phone?.Back(), "map-back");
            back.AddToClassList("duo-map-back");
            root.Add(back);
            OnboardingFrame.AddSheet(root, "UI/map");
            return root;
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

    /// <summary>
    /// Net Worth and Weekly PnL boards (feature #11). Duo layout (spec "Rich List / League"): segmented boards, a podium for
    /// the top three, rank rows with the value trailing, your own line pinned at the bottom in glass. Server ranks only.
    /// </summary>
    public sealed class RichListApp : PhoneApp
    {
        public override string Id => "richlist";
        public override string Title => Strings.T("app.richlist");
        public override string Color => "#8854d0";
        public override DuoChrome Chrome => DuoChrome.Plain;
        private int _requestVersion;
        private DuoPage _page;
        private VisualElement _list;

        public override VisualElement Open()
        {
            _page = new DuoPage(Title);
            _page.name = "richlist-page";
            _page.AddToClassList("duo-richlist");
            _page.Nav.AddLeft(DuoGlyph.Kind.Back, Strings.T("common.back"), () => Phone?.Back(), "richlist-back");
            string[] boards = { "net_worth", "weekly_pnl" };
            var tabs = new DuoSegmented(new[] { Strings.T("rich.netWorth"), Strings.T("rich.weekly") }, 0, i => Load(boards[i]));
            tabs.Options[0].name = "net_worth";
            tabs.Options[1].name = "weekly_pnl";
            _list = El.Div("duo-richlist-body");
            _page.Body.Add(El.Div("duo-comp-tabs", tabs));
            _page.Body.Add(El.Text(Strings.T("rich.title"), "duo-comp-note"));
            _page.Body.Add(_list);
            Load("net_worth");
            return _page;
        }

        private void Load(string board)
        {
            _list.Clear();
            DuoBoard.Unpin(_page);
            _list.Add(El.Text(Strings.T("common.loading"), "duo-comp-note"));
            Fetch(board, _list, _page, ++_requestVersion);
        }

        public override void Closed() => _requestVersion++;

        private async void Fetch(string board, VisualElement list, DuoPage page, int version)
        {
            try
            {
                var lb = await Services.Get<Api>().Leaderboard(board);
                if (version != _requestVersion) return;
                list.Clear();
                bool signed = board == "weekly_pnl";
                Guid? me = lb.Me?.UserId;
                var entries = (lb.Entries ?? new List<BoardEntry>())
                    .Select(e => new DuoRank(e.Rank, string.IsNullOrWhiteSpace(e.Alias) ? Strings.T("rich.noAlias") : e.Alias, e.UserId.ToString(), Money.Cash(e.Value, signed), me.HasValue && e.UserId == me.Value)).ToList();
                if (entries.Count == 0) list.Add(Duo.Empty(Strings.T("rich.empty")));
                else list.Add(DuoBoard.Board(entries, "richlist-board"));
                if (lb.Me != null && lb.Me.Rank > 0)
                    DuoBoard.PinMe(page, new DuoRank(lb.Me.Rank, lb.Me.Alias, lb.Me.UserId.ToString(), Money.Cash(lb.Me.Value, signed), true), Strings.T("rich.youRow"));
            }
            catch (Exception e)
            {
                if (version != _requestVersion) return;
                list.Clear();
                list.Add(Duo.Notice(AppUi.Err(e), Duo.Tone.Danger));
            }
        }
    }

    /// <summary>Who you are in the city: avatar header, real tags (miner tier, Seeker), wallet, and skills as small bars.</summary>
    public sealed class ProfileApp : PhoneApp
    {
        public override string Id => "profile";
        public override string Title => Strings.T("app.profile");
        public override string Color => "#2d98da";
        public override DuoChrome Chrome => DuoChrome.Plain;

        public override VisualElement Open()
        {
            var p = Services.Get<PlayerProfile>();
            var me = p.Me;
            var reg = Services.Get<ConfigStore>().Registries;
            var page = new DuoPage(Title, largeTitle: false);
            page.name = "profile-page";
            page.AddToClassList("duo-profile");
            page.Nav.AddLeft(DuoGlyph.Kind.Back, Strings.T("common.back"), () => Phone?.Back(), "profile-back");

            var head = El.Div("duo-profile-head", Duo.Avatar(p.Alias, me?.User != null ? me.User.Id.ToString() : p.Alias, "duo-avatar-profile"),
                El.Text(p.Alias, "duo-profile-name"));
            var tribe = reg.Tribe(me?.Character?.TribeId)?.name;
            if (!string.IsNullOrEmpty(tribe)) head.Add(El.Text(tribe, "duo-profile-subtitle"));
            var tags = El.Div("duo-profile-tags", El.Text(Strings.F("profile.miner", Strings.ReadableId(me?.MinerTier.ToString())), "duo-tag duo-tag-blue"));
            if (me?.User?.IsSeeker == true) tags.Add(El.Text(Strings.T("profile.seeker"), "duo-tag duo-tag-green"));
            head.Add(tags);
            page.Body.Add(head);

            var wallet = new DuoRow().Leading(Duo.IconSquare("wallet", "graphite")).Title(Strings.T("profile.walletRow"))
                .Value(string.IsNullOrEmpty(me?.User?.Wallet) ? Strings.T("profile.noWallet") : Short(me.User.Wallet));
            wallet.AddToClassList("duo-row-static");
            wallet.name = "profile-wallet";
            var rows = El.Div("duo-list", wallet);
            if (!string.IsNullOrEmpty(me?.User?.SkrName))
            {
                var skr = new DuoRow().Leading(Duo.IconSquare("profile", "purple")).Title(Strings.T("profile.skrName")).Value(me.User.SkrName);
                skr.AddToClassList("duo-row-static");
                rows.Add(skr);
            }
            Duo.MarkLast(rows);
            page.Body.Add(rows);

            if (me?.Skills != null)
            {
                var s = me.Skills;
                page.Body.Add(Duo.SectionHeader(Strings.T("profile.skills")));
                var skills = El.Div("duo-list duo-profile-skills");
                skills.name = "profile-skills";
                foreach (var (name, v) in new[] { ("clout", s.Clout), ("coding", s.Coding), ("trading", s.Trading), ("writing", s.Writing), ("design", s.Design), ("security", s.Security) })
                {
                    var fill = El.Div("duo-meter-fill");
                    fill.style.width = Length.Percent(UnityEngine.Mathf.Clamp(v, 0, 10) * 10f);
                    var row = new DuoRow().Title(Strings.T("profile.skill." + name)).Value(v + "/10");
                    row.TextColumn.Add(El.Div("duo-meter", fill));
                    row.AddToClassList("duo-row-static");
                    skills.Add(row);
                }
                Duo.MarkLast(skills);
                page.Body.Add(skills);
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
