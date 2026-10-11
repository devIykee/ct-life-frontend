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
        private readonly VisualElement _body = El.Div();
        private readonly VisualElement _banner = El.Div();
        private string _confirmTribe;

        public LeagueScreen(CompetitionHub hub) : base(hub)
        {
            Root.Add(CompUi.H1(CompStrings.T("app.league")));
            Root.Add(_banner);
            Root.Add(_tabs);
            Root.Add(_body);
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
            _confirmTribe = null;
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
            var m = d.Membership;
            if (!m.HasMembership)
            {
                body.Add(CompUi.Card(CompUi.H2(CompStrings.T("league.none.title")), CompUi.Text(CompStrings.T("league.none.body"))));
            }
            else
            {
                var card = CompUi.Card(CompUi.H2(LeagueRules.LeagueLabel(d.Leagues, m.LeagueId)));
                card.Add(CompUi.Pair(CompStrings.T("league.season"), m.SeasonIso));
                card.Add(CompUi.Pair(CompStrings.T("league.rank"), m.Rank > 0 ? "#" + m.Rank : "—"));
                card.Add(CompUi.Pair(CompStrings.T("league.netWorth"), Money.Cash(m.NetWorthCents)));
                if (!string.IsNullOrEmpty(m.PromotedFromLeagueId))
                    card.Add(CompUi.Small(CompStrings.F("league.promotedFrom", LeagueRules.LeagueLabel(d.Leagues, m.PromotedFromLeagueId))));
                body.Add(card);
            }
            var last = Hub.Feed.LastLeagueResult;
            if (last != null)
                body.Add(CompUi.Card(CompUi.Text(CompStrings.F(LeagueRules.OutcomeKey(last.Outcome) + ".line", LeagueRules.LeagueLabel(d.Leagues, last.ToLeagueId), last.Rank))));

            var results = El.Div();
            body.Add(CompUi.Btn(CompStrings.T("league.results.show"), () => Load(results, ct => Hub.Api.SeasonResults(null, ct), (b, r) => RenderResults(b, r, d.Leagues)), "ct-btn"));
            body.Add(results);

            body.Add(CompUi.H2(CompStrings.T("league.board")));
            if (d.BoardUnavailable) body.Add(CompUi.Small(CompStrings.T("unavailable.body")));
            else if (d.Board.Entries.Count == 0) body.Add(CompUi.Small(CompStrings.T("league.board.empty")));
            else
            {
                foreach (var e in d.Board.Entries)
                    body.Add(CompUi.Pair("#" + e.Rank + "  " + (e.UserId == Me ? CompStrings.T("common.you") : e.Alias), Money.Cash(e.Value)));
                if (d.Board.Me != null && d.Board.Entries.All(e => e.UserId != d.Board.Me.UserId))
                    body.Add(CompUi.Pair("#" + d.Board.Me.Rank + "  " + CompStrings.T("common.you"), Money.Cash(d.Board.Me.Value)));
            }
        }

        private static void RenderResults(VisualElement body, SeasonResult r, List<LeagueInfo> leagues)
        {
            var card = CompUi.Card(CompUi.H2(CompStrings.F("league.results.title", r.SeasonId)));
            card.Add(CompUi.Pair(CompStrings.T("league.rank"), r.FinalRank > 0 ? "#" + r.FinalRank : "—"));
            card.Add(CompUi.Text(CompStrings.F(LeagueRules.OutcomeKey(r.Outcome) + ".line", LeagueRules.LeagueLabel(leagues, r.ToLeagueId), r.FinalRank)));
            if (r.Rewards.Count == 0) card.Add(CompUi.Small(CompStrings.T("league.results.noRewards")));
            foreach (var rw in r.Rewards)
            {
                string line = rw.Kind == "cash" && rw.CashCents.HasValue ? Money.Cash(rw.CashCents.Value)
                    : rw.Kind == "cosmetic" ? CompStrings.F("league.reward.cosmetic", CompStrings.Readable(rw.ItemId))
                    : rw.Kind == "badge" ? CompStrings.F("league.reward.badge", CompStrings.Readable(rw.BadgeId))
                    : CompStrings.Readable(rw.Kind);
                card.Add(CompUi.Text(line));
            }
            body.Add(card);
        }

        // ---------------------------------------------------------------- leagues

        private void RenderLeagues(VisualElement body, List<LeagueInfo> leagues)
        {
            if (leagues.Count == 0) { body.Add(CompUi.Small(CompStrings.T("league.leagues.empty"))); return; }
            foreach (var l in leagues)
            {
                var c = CompUi.Card(CompUi.H2(l.Label));
                if (l.MinNetWorthCents > 0) c.Add(CompUi.Pair(CompStrings.T("league.minNetWorth"), Money.Cash(l.MinNetWorthCents)));
                if (l.MaxMembers > 0) c.Add(CompUi.Pair(CompStrings.T("league.maxMembers"), l.MaxMembers.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                if (!string.IsNullOrEmpty(l.PromoteToSlug)) c.Add(CompUi.Small(CompStrings.F("league.promoteTo", LeagueRules.LeagueLabel(leagues, l.PromoteToSlug))));
                body.Add(c);
            }
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
            foreach (var t in tribes)
            {
                var tribe = t;
                var c = CompUi.Card(CompUi.H2(tribe.Name + (tribe.Id == mine ? "  (" + CompStrings.T("tribe.yours") + ")" : "")));
                c.Add(CompUi.Text(tribe.Perk));
                if (tribe.MemberCount.HasValue) c.Add(CompUi.Pair(CompStrings.T("tribe.members"), tribe.MemberCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                var owned = tribe.OwnedDistricts.Select(id => Hub.Feed.Districts.Get(id)?.Name ?? CompStrings.Readable(id)).ToList();
                c.Add(CompUi.Pair(CompStrings.T("tribe.districts"), owned.Count == 0 ? CompStrings.T("tribe.noDistricts") : string.Join(", ", owned)));
                if (tribe.Id != mine)
                {
                    if (_confirmTribe == tribe.Id)
                    {
                        c.Add(CompUi.Text(CompStrings.F("tribe.confirm", tribe.Name)));
                        var status = CompUi.Small("");
                        c.Add(status);
                        c.Add(CompUi.Row(
                            CompUi.Btn(CompStrings.T("tribe.confirmYes"), () => ChangeTribe(tribe, status)),
                            CompUi.Btn(CompStrings.T("common.cancel"), () => { _confirmTribe = null; ShowTab(2); }, "ct-btn")));
                    }
                    else c.Add(CompUi.Btn(CompStrings.F("tribe.join", tribe.Name), () => { _confirmTribe = tribe.Id; ShowTab2Keep(); }, "ct-btn"));
                }
                body.Add(c);
            }
        }

        private void ShowTab2Keep()
        {
            // Re-render the tribes list with the confirm row open, without refetching.
            if (Hub.Tribes != null) { _body.Clear(); RenderTribes(_body, Hub.Tribes); }
        }

        private void ChangeTribe(TribeInfo tribe, Label status)
        {
            status.text = CompStrings.T("common.working");
            var key = Hub.KeyFor("tribe");
            var k = key.KeyFor("PATCH", "/characters/me/tribe", tribe.Id);
            Run(async ct =>
            {
                await Hub.Api.ChangeTribe(tribe.Id, k, ct);
                if (Hub.Profile != null) await Hub.Profile.Refresh(ct);
                return true;
            }, _ =>
            {
                key.Complete();
                _confirmTribe = null;
                Bus.Publish(ToastSignal.Text(CompStrings.F("tribe.changed", tribe.Name), ToastKind.Good));
                ShowTab(2);
            }, e =>
            {
                key.Settle(e);
                status.text = e.Message;
            }, true);
        }

        // ---------------------------------------------------------------- districts

        private void RenderDistricts(VisualElement body, DistrictBoard board)
        {
            if (board.Flags.Count == 0) { body.Add(CompUi.Small(CompStrings.T("district.empty"))); return; }
            body.Add(CompUi.Small(CompStrings.T("district.note")));
            foreach (var f in board.Flags)
            {
                var flag = f;
                var holder = DistrictBoard.HolderOf(flag);
                var c = CompUi.Card(CompUi.H2(flag.Name));
                c.Add(CompUi.Pair(CompStrings.T("district.holder"), holder == null ? "—" : CompetitionRuntime.TribeName(holder) + (flag.OwnerTribeId == null ? " (" + CompStrings.T("district.default") + ")" : "")));
                if (flag.SinceMs.HasValue) c.Add(CompUi.Pair(CompStrings.T("district.since"), CompTime.Date(flag.SinceMs.Value)));
                var standings = El.Div();
                c.Add(CompUi.Btn(CompStrings.T("district.standings"), () => Load(standings, ct => Hub.Api.Standings(flag.DistrictId, ct), RenderStandings), "ct-btn"));
                c.Add(standings);
                body.Add(c);
            }
        }

        private void RenderStandings(VisualElement body, DistrictStandings s)
        {
            if (s.Standings.Count == 0) body.Add(CompUi.Small(CompStrings.T("district.standings.empty")));
            int rank = 1;
            foreach (var row in s.Standings)
                body.Add(CompUi.Pair("#" + rank++ + "  " + CompetitionRuntime.TribeName(row.TribeId) + (row.TribeId == s.MyTribeId ? "  (" + CompStrings.T("tribe.yours") + ")" : ""),
                    row.Score.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            body.Add(CompUi.Small(s.MyContribution.HasValue
                ? CompStrings.F("district.contribution", s.MyContribution.Value)
                : CompStrings.T("district.contribution.hidden")));
            if (s.RolloverAtMs > 0) body.Add(CompUi.Small(CompStrings.F("district.rollover", CompTime.When(s.RolloverAtMs))));
        }
    }
}
