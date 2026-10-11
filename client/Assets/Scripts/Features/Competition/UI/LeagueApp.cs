using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine.UIElements;

namespace CTLife.Competition
{
    /// <summary>
    /// Phone app "League" (id `league`): my league and season results, all leagues, tribes (pick or view) and district control with
    /// standings. Everything shown is what the server sent; a route the server does not serve yet shows "not available yet".
    /// </summary>
    public sealed class LeagueApp : CompApp
    {
        public override string Id => CompetitionLinks.LeagueApp;
        public override string Title => CompStrings.T("app.league");
        public override string Monogram => "LG";
        public override string Color => "#4b7bec";

        internal override CompScreen CreateView(CompetitionHub hub) => new LeagueScreen(hub);
    }

    internal sealed class LeagueScreen : CompScreen
    {
        private static readonly string[] TabKeys = { "league.tab.mine", "league.tab.leagues", "league.tab.tribes", "league.tab.districts" };
        private int _tab;
        private readonly VisualElement _tabs = El.Div();
        private readonly VisualElement _body = El.Div("duo-comp-body");

        public LeagueScreen(CompetitionHub hub) : base(hub, CompStrings.T("app.league"))
        {
            Page.name = "league-page";
            Page.AddToClassList("duo-league");
            Body.Add(_tabs);
            Body.Add(_body);
            Hub.Feed.LeagueResultEvent += OnLeagueResult;
            Hub.Feed.DistrictFlippedEvent += OnFlip;
            Hub.Feed.SeasonRolloverEvent += OnRollover;
            ShowTab(0);
        }

        protected override void OnDisposed()
        {
            Hub.Feed.LeagueResultEvent -= OnLeagueResult;
            Hub.Feed.DistrictFlippedEvent -= OnFlip;
            Hub.Feed.SeasonRolloverEvent -= OnRollover;
        }

        private void OnLeagueResult(LeagueResultMsg m) { if (Alive && _tab == 0) ShowTab(0); }
        private void OnRollover(SeasonRollover m) { if (Alive && (_tab == 0 || _tab == 1)) ShowTab(_tab); }
        private void OnFlip(DistrictFlipped m) { if (Alive && _tab == 3) ShowTab(3); }

        private void ShowTab(int tab)
        {
            _tab = tab;
            DuoBoard.Unpin(Page);
            _tabs.Clear();
            _tabs.Add(CompUi.Tabs(TabKeys.Select(CompStrings.T).ToArray(), tab, ShowTab));
            switch (tab)
            {
                case 0: Load(_body, LoadMine, RenderMine); break;
                case 1: Load(_body, ct => Hub.LoadLeagues(ct), RenderLeagues); break;
                case 2: Load(_body, LoadTribes, RenderTribes); break;
                default: Load(_body, ct => Hub.LoadDistricts(ct), RenderDistricts); break;
            }
        }

        // ---------------------------------------------------------------- mine

        private sealed class MineData
        {
            public LeagueMembership Membership;
            public List<LeagueInfo> Leagues;
            public BoardPage Board;
            public bool BoardUnavailable;
        }

        private async Task<MineData> LoadMine(System.Threading.CancellationToken ct)
        {
            var d = new MineData { Membership = await Hub.Api.MyLeague(ct), Leagues = await Hub.LoadLeagues(ct) };
            try { d.Board = await Hub.Api.Board("net_worth", "league", ct); }
            catch (Exception e) when (!(e is OperationCanceledException)) { d.BoardUnavailable = true; }
            return d;
        }

        private void RenderMine(VisualElement body, MineData d)
        {
            if (_tab != 0) return;
            var m = d.Membership;
            if (!m.HasMembership)
            {
                body.Add(Duo.Empty(CompStrings.T("league.none.title"), CompStrings.T("league.none.body")));
            }
            else
            {
                // Hero: the rank that matters, the league and season as its label, net worth as the caption.
                var hero = Duo.Hero(LeagueRules.LeagueLabel(d.Leagues, m.LeagueId) + " · " + CompStrings.T("league.season") + " " + m.SeasonIso,
                    m.Rank > 0 ? "#" + m.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture) : "—",
                    Money.Cash(m.NetWorthCents), null, CompStrings.T("league.netWorth"));
                hero.name = "league-hero";
                body.Add(hero);
                if (!string.IsNullOrEmpty(m.PromotedFromLeagueId))
                    body.Add(CompUi.Small(CompStrings.F("league.promotedFrom", LeagueRules.LeagueLabel(d.Leagues, m.PromotedFromLeagueId))));
            }
            var last = Hub.Feed.LastLeagueResult;
            if (last != null)
                body.Add(Duo.Notice(CompStrings.F(LeagueRules.OutcomeKey(last.Outcome) + ".line", LeagueRules.LeagueLabel(d.Leagues, last.ToLeagueId), last.Rank),
                    last.Outcome == "relegated" ? Duo.Tone.Warning : Duo.Tone.Info));

            var section = Duo.SectionHeader(CompStrings.T("league.board"), CompStrings.T("league.results.show"), () => ShowResults(d.Leagues));
            section.name = "league-board-header";
            body.Add(section);
            if (d.BoardUnavailable) body.Add(CompUi.Small(CompStrings.T("unavailable.body")));
            else if (d.Board.Entries.Count == 0) body.Add(Duo.Empty(CompStrings.T("league.board.empty")));
            else
            {
                var rows = d.Board.Entries.Select(e => new DuoRank(e.Rank, e.UserId == Me ? CompStrings.T("common.you") : e.Alias, e.UserId, Money.Cash(e.Value), e.UserId == Me)).ToList();
                body.Add(DuoBoard.Board(rows, "league-board"));
                if (d.Board.Me != null)
                    DuoBoard.PinMe(Page, new DuoRank(d.Board.Me.Rank, CompStrings.T("common.you"), d.Board.Me.UserId ?? Me, Money.Cash(d.Board.Me.Value), true), CompStrings.T("common.you"));
            }
        }

        private void ShowResults(List<LeagueInfo> leagues)
        {
            var sheet = new DuoSheet(CompStrings.T("league.results.show"));
            sheet.name = "league-results-sheet";
            var content = El.Div("duo-comp-sheet-body");
            sheet.AddContent(content);
            sheet.AddSecondary(CompStrings.T("common.done"));
            ShowSheet(sheet);
            Load(content, ct => Hub.Api.SeasonResults(null, ct), (b, r) => RenderResults(b, r, leagues));
        }

        private static void RenderResults(VisualElement body, SeasonResult r, List<LeagueInfo> leagues)
        {
            body.Add(CompUi.H2(CompStrings.F("league.results.title", r.SeasonId)));
            body.Add(CompUi.Text(CompStrings.F(LeagueRules.OutcomeKey(r.Outcome) + ".line", LeagueRules.LeagueLabel(leagues, r.ToLeagueId), r.FinalRank)));
            body.Add(CompUi.Pair(CompStrings.T("league.rank"), r.FinalRank > 0 ? "#" + r.FinalRank : "—"));
            if (r.Rewards.Count == 0) body.Add(CompUi.Small(CompStrings.T("league.results.noRewards")));
            foreach (var rw in r.Rewards)
            {
                string line = rw.Kind == "cash" && rw.CashCents.HasValue ? Money.Cash(rw.CashCents.Value)
                    : rw.Kind == "cosmetic" ? CompStrings.F("league.reward.cosmetic", CompStrings.Readable(rw.ItemId))
                    : rw.Kind == "badge" ? CompStrings.F("league.reward.badge", CompStrings.Readable(rw.BadgeId))
                    : CompStrings.Readable(rw.Kind);
                body.Add(CompUi.Pair(CompStrings.T("league.reward"), line));
            }
        }

        // ---------------------------------------------------------------- leagues

        private void RenderLeagues(VisualElement body, List<LeagueInfo> leagues)
        {
            if (leagues.Count == 0) { body.Add(Duo.Empty(CompStrings.T("league.leagues.empty"))); return; }
            var list = El.Div("duo-list");
            foreach (var l in leagues)
            {
                var parts = new List<string>();
                if (l.MinNetWorthCents > 0) parts.Add(CompStrings.T("league.minNetWorth") + " " + Money.Cash(l.MinNetWorthCents));
                if (l.MaxMembers > 0) parts.Add(CompStrings.F("league.membersValue", l.MaxMembers));
                if (!string.IsNullOrEmpty(l.PromoteToSlug)) parts.Add(CompStrings.F("league.promoteTo", LeagueRules.LeagueLabel(leagues, l.PromoteToSlug)));
                var row = new DuoRow().Leading(Duo.IconSquare("league", "blue")).Title(l.Label, "duo-row-title-strong");
                if (parts.Count > 0) row.Subtitle(string.Join(" · ", parts), twoLines: true);
                row.AddToClassList("duo-row-static");
                list.Add(row);
            }
            Duo.MarkLast(list);
            body.Add(list);
        }

        // ---------------------------------------------------------------- tribes

        private async Task<List<TribeInfo>> LoadTribes(System.Threading.CancellationToken ct)
        {
            var t = await Hub.LoadTribes(ct);
            try { await Hub.LoadDistricts(ct); } catch (Exception e) when (!(e is OperationCanceledException)) { /* ownership list is optional here */ }
            return t;
        }

        private void RenderTribes(VisualElement body, List<TribeInfo> tribes)
        {
            body.Add(CompUi.Small(CompStrings.T("tribe.note")));
            var mine = Hub.MyTribeId;
            var list = El.Div("duo-list");
            foreach (var t in tribes)
            {
                var tribe = t;
                var owned = tribe.OwnedDistricts.Select(id => Hub.Feed.Districts.Get(id)?.Name ?? CompStrings.Readable(id)).ToList();
                var meta = new List<string>();
                if (tribe.MemberCount.HasValue) meta.Add(CompStrings.F("tribe.membersValue", tribe.MemberCount.Value));
                meta.Add(CompStrings.T("tribe.districts") + ": " + (owned.Count == 0 ? CompStrings.T("tribe.noDistricts") : string.Join(", ", owned)));
                var row = new DuoRow(() => TribeSheet(tribe, owned, meta)).Leading(Duo.Avatar(tribe.Name, tribe.Id, "duo-avatar-m"))
                    .Title(tribe.Name, "duo-row-title-strong").Subtitle((string.IsNullOrEmpty(tribe.Perk) ? "" : tribe.Perk + "\n") + string.Join(" · ", meta), twoLines: true);
                row.name = "league-tribe-" + tribe.Id;
                if (tribe.Id == mine) row.Trailing(CompUi.Tag(CompStrings.T("tribe.yoursTag"), "blue"));
                else
                {
                    var join = Duo.Pill(CompStrings.T("tribe.switch"), () => TribeSheet(tribe, owned, meta));
                    join.tooltip = CompStrings.F("tribe.join", tribe.Name);
                    row.Trailing(join);
                }
                list.Add(row);
            }
            Duo.MarkLast(list);
            body.Add(list);
        }

        private void TribeSheet(TribeInfo tribe, List<string> owned, List<string> meta)
        {
            bool mine = tribe.Id == Hub.MyTribeId;
            var sheet = new DuoSheet(tribe.Name, mine ? CompStrings.T("tribe.yoursLine") : CompStrings.F("tribe.confirm", tribe.Name));
            sheet.name = "league-tribe-sheet";
            if (!string.IsNullOrEmpty(tribe.Perk)) sheet.AddContent(CompUi.Text(tribe.Perk));
            foreach (var line in meta) sheet.AddContent(CompUi.Small(line));
            var status = CompUi.Small("");
            sheet.AddContent(status);
            if (!mine)
            {
                Button go = null;
                go = CompUi.Btn(CompStrings.T("tribe.confirmYes"), () => { go.SetEnabled(false); ChangeTribe(tribe, status, sheet, go); });
                go.name = "league-tribe-confirm";
                sheet.AddContent(go);
            }
            sheet.AddSecondary(CompStrings.T(mine ? "common.done" : "common.cancel"));
            ShowSheet(sheet);
        }

        private void ChangeTribe(TribeInfo tribe, Label status, DuoSheet sheet, Button go)
        {
            status.text = CompStrings.T("common.working");
            var key = Hub.KeyFor("tribe");
            var k = key.KeyFor("POST", "/me/tribe", tribe.Id);
            Run(async ct =>
            {
                await Hub.Api.ChangeTribe(tribe.Id, k, ct);
                if (Hub.Profile != null) await Hub.Profile.Refresh(ct);
                return true;
            }, _ =>
            {
                key.Complete();
                sheet.Close();
                Bus.Publish(ToastSignal.Text(CompStrings.F("tribe.changed", tribe.Name), ToastKind.Good));
                ShowTab(2);
            }, e =>
            {
                key.Settle(e);
                status.text = e.Message;
                go.SetEnabled(true);
            }, true);
        }

        // ---------------------------------------------------------------- districts

        private void RenderDistricts(VisualElement body, DistrictBoard board)
        {
            if (board.Flags.Count == 0) { body.Add(Duo.Empty(CompStrings.T("district.empty"))); return; }
            body.Add(CompUi.Small(CompStrings.T("district.note")));
            var list = El.Div("duo-list");
            foreach (var f in board.Flags)
            {
                var flag = f;
                var holder = DistrictBoard.HolderOf(flag);
                string holderText = holder == null ? "—" : CompetitionRuntime.TribeName(holder) + (flag.OwnerTribeId == null ? " (" + CompStrings.T("district.default") + ")" : "");
                string sub = CompStrings.T("district.holder") + " " + holderText + (flag.SinceMs.HasValue ? " · " + CompStrings.T("district.since") + " " + CompTime.Date(flag.SinceMs.Value) : "");
                var row = new DuoRow(() => Standings(flag, holderText)).Leading(Duo.IconSquare("map", "green")).Title(flag.Name, "duo-row-title-strong").Subtitle(sub, twoLines: true).Chevron();
                row.name = "league-district-" + flag.DistrictId;
                if (holder != null && holder == Hub.MyTribeId) row.Trailing(CompUi.Tag(CompStrings.T("tribe.yoursTag"), "blue"));
                list.Add(row);
            }
            Duo.MarkLast(list);
            body.Add(list);
        }

        private void Standings(DistrictFlag flag, string holderText)
        {
            var sheet = new DuoSheet(flag.Name, CompStrings.T("district.holder") + " " + holderText);
            sheet.name = "league-standings-sheet";
            var content = El.Div("duo-comp-sheet-body");
            sheet.AddContent(content);
            sheet.AddSecondary(CompStrings.T("common.done"));
            ShowSheet(sheet);
            Load(content, ct => Hub.Api.Standings(flag.DistrictId, ct), RenderStandings);
        }

        private void RenderStandings(VisualElement body, DistrictStandings s)
        {
            if (s.Standings.Count == 0) body.Add(CompUi.Small(CompStrings.T("district.standings.empty")));
            int rank = 1;
            var list = El.Div("duo-list");
            foreach (var row in s.Standings)
                list.Add(DuoBoard.Row(new DuoRank(rank++, CompetitionRuntime.TribeName(row.TribeId), row.TribeId,
                    row.Score.ToString(System.Globalization.CultureInfo.InvariantCulture), row.TribeId == s.MyTribeId)));
            Duo.MarkLast(list);
            body.Add(list);
            body.Add(CompUi.Small(s.MyContribution.HasValue
                ? CompStrings.F("district.contribution", s.MyContribution.Value)
                : CompStrings.T("district.contribution.hidden")));
            if (s.RolloverAtMs > 0) body.Add(CompUi.Small(CompStrings.F("district.rollover", CompTime.When(s.RolloverAtMs))));
        }
    }
}
