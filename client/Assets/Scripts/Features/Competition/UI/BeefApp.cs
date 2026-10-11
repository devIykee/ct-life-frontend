using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine.UIElements;

namespace CTLife.Competition
{
    /// <summary>
    /// Phone app "Beef" (id `beef`): challenge a player (or anyone), accept / decline / cancel, the match list, a detail page with
    /// server-driven voting, and the Elo board. The Ratio Arena venue opens it. Settlement, votes, eligibility and Elo are the
    /// server's; this screen sends intents and shows what comes back.
    /// </summary>
    public sealed class BeefApp : CompApp
    {
        public override string Id => CompetitionLinks.BeefApp;
        public override string Title => CompStrings.T("app.beef");
        public override string Monogram => "BF";
        public override string Color => "#eb3b5a";

        internal override CompScreen CreateView(CompetitionHub hub) => new BeefScreen(hub);
    }

    internal sealed class BeefScreen : CompScreen
    {
        private static readonly string[] TabKeys = { "beef.tab.mine", "beef.tab.arena", "beef.tab.elo" };
        private readonly VisualElement _content = El.Div();
        private int _tab;
        private string _detailId;
        private Label _countdown;
        private BeefMatch _detail;
        private bool _refetched;
        private string _opponentId, _opponentName;

        public BeefScreen(CompetitionHub hub) : base(hub)
        {
            Root.Add(CompUi.H1(CompStrings.T("app.beef")));
            Root.Add(_content);
            Hub.Feed.BeefUpdateEvent += OnUpdate;
            Hub.Feed.BeefResultEvent += OnResult;
            Hub.Feed.BeefChallengeEvent += OnChallenge;
            CompetitionLinks.TakePendingOpponent(out _opponentId, out _opponentName);
            if (_opponentId != null) ShowForm(); else ShowList(0);
        }

        protected override void OnDisposed()
        {
            Hub.Feed.BeefUpdateEvent -= OnUpdate;
            Hub.Feed.BeefResultEvent -= OnResult;
            Hub.Feed.BeefChallengeEvent -= OnChallenge;
        }

        private void OnUpdate(BeefUpdateMsg m) { if (Alive) Refresh(m.MatchId); }
        private void OnResult(BeefResultMsg m) { if (Alive) Refresh(m.MatchId); }
        private void OnChallenge(BeefChallengeMsg m) { if (Alive && _detailId == null && _tab == 0) ShowList(0); }

        private void Refresh(string matchId)
        {
            if (_detailId == matchId) ShowDetail(matchId);
            else if (_detailId == null && _tab < 2) ShowList(_tab);
        }

        private VisualElement Page(bool back)
        {
            _content.Clear();
            _detailId = null;
            _countdown = null;
            if (back) _content.Add(CompUi.Btn(CompStrings.T("common.back"), () => ShowList(_tab), "ct-btn"));
            return _content;
        }

        // ---------------------------------------------------------------- list

        private void ShowList(int tab)
        {
            _tab = tab;
            var page = Page(false);
            page.Add(CompUi.Btn(CompStrings.T("beef.challenge"), ShowForm));
            page.Add(CompUi.Tabs(TabKeys.Select(CompStrings.T).ToArray(), tab, ShowList));
            var body = El.Div();
            page.Add(body);
            if (tab == 2) { Load(body, LoadElo, RenderElo); return; }
            if (tab == 0) RenderPendingChallenges(body);
            var list = El.Div();
            body.Add(list);
            Load(list, ct => Hub.Api.BeefList(tab == 0 ? "mine" : "all", null, null, ct), RenderMatches);
        }

        private void RenderPendingChallenges(VisualElement body)
        {
            foreach (var c in Hub.Feed.BeefChallenges.Where(x => x.ExpiresAtMs == 0 || x.ExpiresAtMs > Hub.NowMs).ToList())
            {
                var ch = c;
                var card = CompUi.Card(CompUi.H2(CompStrings.T("beef.challenged")));
                var who = CompUi.Text("…");
                FillAlias(who, ch.FromUserId, "beef.from");
                card.Add(who);
                card.Add(CompUi.Pair(CompStrings.T("beef.topic"), BeefRules.TopicLabel(ch.TopicId)));
                card.Add(CompUi.Pair(CompStrings.T("beef.stake"), Money.Cash(ch.StakeCents)));
                if (ch.ExpiresAtMs > 0) card.Add(CompUi.Small(CompStrings.F("beef.expires", CompTime.When(ch.ExpiresAtMs))));
                card.Add(CompUi.Btn(CompStrings.T("beef.view"), () => ShowDetail(ch.MatchId), "ct-btn"));
                body.Add(card);
            }
        }

        private void RenderMatches(VisualElement body, BeefPage page)
        {
            var matches = BeefRules.Order(page.Matches);
            if (matches.Count == 0) { body.Add(CompUi.Small(CompStrings.T(_tab == 0 ? "beef.empty.mine" : "beef.empty.arena"))); return; }
            foreach (var m in matches) body.Add(MatchCard(m));
        }

        private VisualElement MatchCard(BeefMatch m)
        {
            var phase = BeefRules.PhaseOf(m.Status);
            var card = CompUi.Card(CompUi.H2(BeefRules.TopicLabel(m.TopicId)));
            var vs = CompUi.Text("…");
            FillVs(vs, m);
            card.Add(vs);
            card.Add(CompUi.Pair(CompStrings.T("beef.stake"), Money.Cash(m.StakeCents)));
            card.Add(CompUi.Pair(CompStrings.T("beef.status"), PhaseText(phase)));
            var role = BeefRules.RoleOf(m, Me);
            if (role != BeefRole.Spectator) card.Add(CompUi.Small(CompStrings.T("beef.role." + role.ToString().ToLowerInvariant())));
            var outcome = BeefRules.OutcomeFor(m, Me);
            if (outcome != "none") card.Add(CompUi.Small(CompStrings.T("beef.outcome." + outcome)));
            card.Add(CompUi.Btn(CompStrings.T("beef.view"), () => ShowDetail(m.MatchId), "ct-btn"));
            return card;
        }

        private static string PhaseText(BeefPhase p) => CompStrings.T("beef.phase." + (p == BeefPhase.Unknown ? "unknown" : p.ToString().ToLowerInvariant()));

        private void FillAlias(Label target, string userId, string formatKey)
        {
            var cached = Hub.AliasCached(userId);
            if (cached != null) { target.text = CompStrings.F(formatKey, cached); return; }
            Run(ct => Hub.AliasOf(userId, ct), a => target.text = CompStrings.F(formatKey, a), null, false);
        }

        private void FillVs(Label target, BeefMatch m)
        {
            Run(async ct =>
            {
                var a = await Hub.AliasOf(m.ChallengerUserId, ct);
                var other = m.AccepterUserId ?? m.InvitedUserId;
                var b = string.IsNullOrEmpty(other) ? CompStrings.T("beef.anyone") : await Hub.AliasOf(other, ct);
                return a + "  vs  " + b;
            }, s => target.text = s, null, false);
        }

        // ---------------------------------------------------------------- elo

        private sealed class EloData { public BeefElo Mine; public BoardPage Board; public CompError BoardError; }

        private async Task<EloData> LoadElo(CancellationToken ct)
        {
            var d = new EloData { Mine = await Hub.Api.Elo(null, ct) };
            try { d.Board = await Hub.Api.Board("beef_elo", null, ct); }
            catch (Exception e) when (!(e is OperationCanceledException)) { d.BoardError = CompErrors.From(e, false); }
            return d;
        }

        private void RenderElo(VisualElement body, EloData d)
        {
            var c = CompUi.Card(CompUi.H2(CompStrings.T("beef.elo.mine")));
            c.Add(CompUi.Pair(CompStrings.T("beef.elo"), d.Mine.Elo.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            c.Add(CompUi.Pair(CompStrings.T("beef.elo.record"), CompStrings.F("beef.elo.wld", d.Mine.Wins, d.Mine.Losses, d.Mine.Draws)));
            c.Add(CompUi.Pair(CompStrings.T("beef.elo.played"), d.Mine.MatchesPlayed.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            body.Add(c);
            body.Add(CompUi.H2(CompStrings.T("beef.elo.board")));
            if (d.BoardError != null) { body.Add(CompUi.ErrorBlock(d.BoardError, () => ShowList(2))); return; }
            if (d.Board.Entries.Count == 0) { body.Add(CompUi.Small(CompStrings.T("beef.elo.empty"))); return; }
            foreach (var e in d.Board.Entries)
                body.Add(CompUi.Pair("#" + e.Rank + "  " + (e.UserId == Me ? CompStrings.T("common.you") : e.Alias), e.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (d.Board.Me != null && d.Board.Entries.All(e => e.UserId != d.Board.Me.UserId))
                body.Add(CompUi.Pair("#" + d.Board.Me.Rank + "  " + CompStrings.T("common.you"), d.Board.Me.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        // ---------------------------------------------------------------- challenge form

        private void ShowForm()
        {
            var page = Page(true);
            page.Add(CompUi.H2(CompStrings.T("beef.form.title")));
            page.Add(CompUi.Small(CompStrings.T("beef.form.note")));
            string topic = null, stakeText = "";
            bool invite = _opponentId != null;
            var status = CompUi.Small("");
            var topicRow = El.Div("ct-app-tabs");
            var chips = new List<Button>();
            foreach (var t in CompStrings.BeefTopics)
            {
                var id = t;
                Button chip = null;
                chip = CompUi.Chip(BeefRules.TopicLabel(id), () =>
                {
                    topic = id;
                    for (int i = 0; i < chips.Count; i++) chips[i].EnableInClassList("ct-chip-on", CompStrings.BeefTopics[i] == id);
                }, false);
                chips.Add(chip);
                topicRow.Add(chip);
            }
            page.Add(CompUi.Text(CompStrings.T("beef.form.topic"), "ct-section-title"));
            page.Add(topicRow);
            page.Add(CompUi.Text(CompStrings.T("beef.form.stake"), "ct-section-title"));
            page.Add(CompUi.Field(CompStrings.T("beef.form.stakeField"), "", v => stakeText = v, 12));
            if (_opponentId != null)
            {
                page.Add(CompUi.Text(CompStrings.T("beef.form.opponent"), "ct-section-title"));
                var row = El.Div("ct-app-tabs");
                Button inv = null, open = null;
                inv = CompUi.Chip(CompStrings.F("beef.form.invite", _opponentName), () => { invite = true; inv.EnableInClassList("ct-chip-on", true); open.EnableInClassList("ct-chip-on", false); }, true);
                open = CompUi.Chip(CompStrings.T("beef.form.openToAll"), () => { invite = false; inv.EnableInClassList("ct-chip-on", false); open.EnableInClassList("ct-chip-on", true); }, false);
                row.Add(inv);
                row.Add(open);
                page.Add(row);
            }
            Button submit = null;
            submit = CompUi.Btn(CompStrings.T("beef.form.submit"), () =>
            {
                if (topic == null) { status.text = CompStrings.T("beef.form.pickTopic"); return; }
                if (!BeefRules.TryParseStakeCents(stakeText, out var cents) || BeefRules.StakeError(cents) != null) { status.text = CompStrings.T("beef.form.badStake"); return; }
                var body = CompetitionApi.BeefOpenBody(cents, topic, invite ? _opponentId : null);
                var key = Hub.KeyFor("beef:open");
                var k = key.KeyFor("POST", "/beef", body.ToString(Newtonsoft.Json.Formatting.None));
                status.text = CompStrings.T("common.working");
                submit.SetEnabled(false);
                Run(ct => Hub.Api.OpenBeef(body, k, ct), m =>
                {
                    key.Complete();
                    _opponentId = _opponentName = null;
                    Bus.Publish(ToastSignal.Text(CompStrings.T("beef.form.sent"), ToastKind.Good));
                    Hub.Profile?.Refresh();
                    ShowDetail(m.MatchId);
                }, e =>
                {
                    key.Settle(e);
                    submit.SetEnabled(true);
                    status.text = e.Message;
                }, true);
            });
            page.Add(submit);
            page.Add(status);
        }

        // ---------------------------------------------------------------- detail

        private void ShowDetail(string matchId)
        {
            var page = Page(true);
            _detailId = matchId;
            _refetched = false;
            var body = El.Div();
            page.Add(body);
            Load(body, ct => Hub.Api.Beef(matchId, ct), RenderDetail);
        }

        private void RenderDetail(VisualElement body, BeefMatch m)
        {
            _detail = m;
            var phase = BeefRules.PhaseOf(m.Status);
            body.Add(CompUi.H2(BeefRules.TopicLabel(m.TopicId)));
            var vs = CompUi.Text("…");
            FillVs(vs, m);
            body.Add(vs);
            body.Add(CompUi.Pair(CompStrings.T("beef.status"), PhaseText(phase)));
            body.Add(CompUi.Pair(CompStrings.T("beef.stake"), Money.Cash(m.StakeCents)));
            if (m.FeeBps > 0)
                body.Add(CompUi.Pair(CompStrings.T("beef.pot"), CompStrings.F("beef.potValue", Money.Cash(BeefRules.EstimatedPayoutCents(m)), Money.Bps(m.FeeBps, false))));
            if (!BeefRules.IsFinished(phase) && m.PhaseEndsAtMs.HasValue)
            {
                _countdown = CompUi.Small("");
                body.Add(_countdown);
                UpdateCountdown();
            }

            // Votes: shown only when the server sends tallies. The proposal hides them until the match closes.
            if (m.Votes != null)
                body.Add(CompUi.Pair(CompStrings.T("beef.votes"), CompStrings.F("beef.votesValue", m.Votes.Challenger, m.Votes.Accepter)));
            else if (phase == BeefPhase.Voting)
                body.Add(CompUi.Small(CompStrings.T("beef.votes.hidden")));
            if (!string.IsNullOrEmpty(m.MyVote))
            {
                var mv = CompUi.Small("…");
                FillAlias(mv, m.MyVote, "beef.youVoted");
                body.Add(mv);
            }

            var outcome = BeefRules.OutcomeFor(m, Me);
            if (outcome != "none")
            {
                body.Add(CompUi.Text(CompStrings.T("beef.outcome." + outcome)));
                var delta = BeefRules.EloDeltaFor(m, Me);
                if (delta.HasValue) body.Add(CompUi.Pair(CompStrings.T("beef.elo"), BeefRules.SignedElo(delta.Value)));
            }
            else if (phase == BeefPhase.Closed && string.IsNullOrEmpty(m.WinnerUserId)) body.Add(CompUi.Small(CompStrings.T("beef.outcome.draw")));
            else if (phase == BeefPhase.Closed) body.Add(CompUi.Small(CompStrings.T("beef.closed")));

            var actions = BeefRules.ActionsFor(m, Me);
            var status = CompUi.Small("");
            if (actions.CanAccept) body.Add(CompUi.Btn(CompStrings.F("beef.accept", Money.Cash(m.StakeCents)), () => Act("accept", m, (id, k, ct) => Hub.Api.AcceptBeef(id, k, ct), status)));
            if (actions.CanDecline) body.Add(CompUi.Btn(CompStrings.T("beef.decline"), () => Act("decline", m, (id, k, ct) => Hub.Api.DeclineBeef(id, k, ct), status), "ct-btn"));
            if (actions.CanCancel) body.Add(CompUi.Btn(CompStrings.T("beef.cancel"), () => Act("cancel", m, (id, k, ct) => Hub.Api.CancelBeef(id, k, ct), status), "ct-btn"));
            if (actions.CanVote)
            {
                body.Add(CompUi.Text(CompStrings.T("beef.vote.title"), "ct-section-title"));
                body.Add(CompUi.Small(CompStrings.T("beef.vote.note")));
                var accepter = m.AccepterUserId;
                var a = CompUi.Btn("…", () => Vote(m, m.ChallengerUserId, status), "ct-btn");
                FillBtn(a, m.ChallengerUserId);
                body.Add(a);
                if (!string.IsNullOrEmpty(accepter))
                {
                    var b = CompUi.Btn("…", () => Vote(m, accepter, status), "ct-btn");
                    FillBtn(b, accepter);
                    body.Add(b);
                }
            }
            body.Add(status);
        }

        private void FillBtn(Button b, string userId) =>
            Run(ct => Hub.AliasOf(userId, ct), a => b.text = CompStrings.F("beef.vote.for", a), null, false);

        private void Act(string kind, BeefMatch m, Func<string, string, CancellationToken, Task<BeefMatch>> call, Label status)
        {
            var key = Hub.KeyFor("beef:" + kind + ":" + m.MatchId);
            var k = key.KeyFor("POST", "/beef/" + m.MatchId + "/" + kind, "");
            status.text = CompStrings.T("common.working");
            Run(ct => call(m.MatchId, k, ct), r =>
            {
                key.Complete();
                Hub.Profile?.Refresh();
                ShowDetail(r.MatchId);
            }, e =>
            {
                key.Settle(e);
                status.text = e.Message;
            }, true);
        }

        private void Vote(BeefMatch m, string votedUserId, Label status)
        {
            var key = Hub.KeyFor("beef:vote:" + m.MatchId);
            var k = key.KeyFor("POST", "/beef/" + m.MatchId + "/votes", votedUserId);
            status.text = CompStrings.T("common.working");
            Run(ct => Hub.Api.Vote(m.MatchId, votedUserId, k, ct), _ =>
            {
                key.Complete();
                Bus.Publish(ToastSignal.Text(CompStrings.T("beef.vote.cast"), ToastKind.Good));
                ShowDetail(m.MatchId);
            }, e =>
            {
                key.Settle(e);
                // A repeat vote or an ineligible voter is a definitive answer: show the server's reason and reload the match.
                status.text = e.Message;
                if (e.Is("ALREADY_VOTED")) ShowDetail(m.MatchId);
            }, true);
        }

        protected override void Tick()
        {
            if (_countdown == null || _detail == null) return;
            UpdateCountdown();
        }

        private void UpdateCountdown()
        {
            if (_countdown == null || _detail?.PhaseEndsAtMs == null) return;
            long left = _detail.PhaseEndsAtMs.Value - Hub.NowMs;
            if (left > 0) { _countdown.text = CompStrings.F("beef.endsIn", CompTime.Countdown(left)); return; }
            _countdown.text = CompStrings.T("beef.phaseEnded");
            // The server moves the phase on; ask once for the new state instead of polling.
            if (!_refetched && _detailId != null) { _refetched = true; var id = _detailId; ShowDetail(id); _refetched = true; }
        }
    }
}
