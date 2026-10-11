using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
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

        public BeefScreen(CompetitionHub hub) : base(hub, CompStrings.T("app.beef"))
        {
            Page.name = "beef-page";
            Page.AddToClassList("duo-beef");
            var start = Page.Nav.AddRight(DuoGlyph.Kind.Plus, CompStrings.T("beef.challenge"), ShowForm, "beef-start");
            start.AddToClassList("duo-glass-circle");
            Body.Add(_content);
            Hub.Feed.BeefUpdateEvent += OnUpdate;
            Hub.Feed.BeefResultEvent += OnResult;
            Hub.Feed.BeefChallengeEvent += OnChallenge;
            CompetitionLinks.TakePendingOpponent(out _opponentId, out _opponentName);
            ShowList(0);
            // A "call out" from a profile opens the form once the screen is on the phone (sheets need a panel).
            if (_opponentId != null) Root.schedule.Execute(() => { if (Alive) ShowForm(); });
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

        protected override bool Back()
        {
            if (_detailId == null) return false;
            ShowList(_tab);
            return true;
        }

        private VisualElement Clear(bool detail)
        {
            _content.Clear();
            _detailId = null;
            _countdown = null;
            Page.Title?.Show(!detail);
            Page.Nav.SetTitle(CompStrings.T(detail ? "beef.detail" : "app.beef"));
            Page.Nav.SetCollapsed(detail, instant: true);
            Root.scrollOffset = Vector2.zero;
            return _content;
        }

        // ---------------------------------------------------------------- list

        private void ShowList(int tab)
        {
            _tab = tab;
            var page = Clear(false);
            page.Add(CompUi.Tabs(TabKeys.Select(CompStrings.T).ToArray(), tab, ShowList));
            var body = El.Div("duo-comp-body");
            page.Add(body);
            if (tab == 2) { Load(body, LoadElo, RenderElo); return; }
            if (tab == 0) RenderPendingChallenges(body);
            var list = El.Div("duo-comp-body");
            body.Add(list);
            Load(list, ct => Hub.Api.BeefList(tab == 0 ? "mine" : "all", null, null, ct), RenderMatches);
        }

        private void RenderPendingChallenges(VisualElement body)
        {
            var pending = Hub.Feed.BeefChallenges.Where(x => x.ExpiresAtMs == 0 || x.ExpiresAtMs > Hub.NowMs).ToList();
            if (pending.Count == 0) return;
            body.Add(Duo.SectionHeader(CompStrings.T("beef.challenged")));
            var list = El.Div("duo-list");
            foreach (var c in pending)
            {
                var ch = c;
                var row = new DuoRow(() => ShowDetail(ch.MatchId)).Leading(Duo.IconSquare("league", "red"))
                    .Title(BeefRules.TopicLabel(ch.TopicId), "duo-row-title-strong")
                    .Subtitle(Money.Cash(ch.StakeCents) + (ch.ExpiresAtMs > 0 ? " · " + CompStrings.F("beef.expires", CompTime.When(ch.ExpiresAtMs)) : ""), twoLines: true);
                FillAlias(row.SubtitleLabel, ch.FromUserId, "beef.fromLine", row.SubtitleLabel.text);
                row.name = "beef-challenge-" + ch.MatchId;
                row.Trailing(Duo.Pill(CompStrings.T("beef.view"), () => ShowDetail(ch.MatchId), Duo.PillKind.Primary));
                list.Add(row);
            }
            Duo.MarkLast(list);
            body.Add(list);
        }

        private void RenderMatches(VisualElement body, BeefPage page)
        {
            var matches = BeefRules.Order(page.Matches);
            if (matches.Count == 0)
            {
                body.Add(_tab == 0 ? Duo.Empty(CompStrings.T("beef.empty.mine"), null, CompStrings.T("beef.challenge"), ShowForm) : Duo.Empty(CompStrings.T("beef.empty.arena")));
                return;
            }
            var list = El.Div("duo-list");
            foreach (var m in matches) list.Add(MatchRow(m));
            Duo.MarkLast(list);
            body.Add(list);
        }

        private VisualElement MatchRow(BeefMatch m)
        {
            var phase = BeefRules.PhaseOf(m.Status);
            var row = new DuoRow(() => ShowDetail(m.MatchId)).Leading(Duo.IconSquare("league", PhaseTint(phase)))
                .Title(BeefRules.TopicLabel(m.TopicId), "duo-row-title-strong").Subtitle("…", twoLines: true);
            row.name = "beef-match-" + m.MatchId;
            var stake = Money.Cash(m.StakeCents);
            var role = BeefRules.RoleOf(m, Me);
            var outcome = BeefRules.OutcomeFor(m, Me);
            string extra = outcome != "none" ? CompStrings.T("beef.outcome." + outcome) : role != BeefRole.Spectator ? CompStrings.T("beef.role." + role.ToString().ToLowerInvariant()) : null;
            FillVs(row.SubtitleLabel, m, vs => vs + " · " + stake + (extra != null ? "\n" + extra : ""));
            row.Trailing(CompUi.Tag(PhaseText(phase), PhaseTone(phase)));
            row.Chevron();
            return row;
        }

        private static string PhaseText(BeefPhase p) => CompStrings.T("beef.phase." + (p == BeefPhase.Unknown ? "unknown" : p.ToString().ToLowerInvariant()));

        private static string PhaseTone(BeefPhase p) =>
            p == BeefPhase.Active || p == BeefPhase.Voting ? "green" : p == BeefPhase.Open ? "orange" : p == BeefPhase.Closed ? "blue" : "gray";

        private static string PhaseTint(BeefPhase p) =>
            p == BeefPhase.Active || p == BeefPhase.Voting ? "red" : p == BeefPhase.Open ? "orange" : "graphite";

        private void FillAlias(Label target, string userId, string formatKey, string suffix = null)
        {
            string Line(string a) => CompStrings.F(formatKey, a) + (string.IsNullOrEmpty(suffix) ? "" : " · " + suffix);
            var cached = Hub.AliasCached(userId);
            if (cached != null) { target.text = Line(cached); return; }
            Run(ct => Hub.AliasOf(userId, ct), a => target.text = Line(a), null, false);
        }

        private void FillVs(Label target, BeefMatch m, Func<string, string> format = null)
        {
            Run(async ct =>
            {
                var a = m.ChallengerUserId == Me ? CompStrings.T("common.you") : await Hub.AliasOf(m.ChallengerUserId, ct);
                var other = m.AccepterUserId ?? m.InvitedUserId;
                var b = string.IsNullOrEmpty(other) ? CompStrings.T("beef.anyone") : other == Me ? CompStrings.T("common.you") : await Hub.AliasOf(other, ct);
                return a + "  vs  " + b;
            }, s => target.text = format != null ? format(s) : s, null, false);
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
            var hero = Duo.Hero(CompStrings.T("beef.elo.mine"), d.Mine.Elo.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CompStrings.F("beef.elo.wld", d.Mine.Wins, d.Mine.Losses, d.Mine.Draws), null, CompStrings.T("beef.elo.record"));
            hero.name = "beef-elo-hero";
            body.Add(hero);
            body.Add(CompUi.Pair(CompStrings.T("beef.elo.played"), d.Mine.MatchesPlayed.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            body.Add(Duo.SectionHeader(CompStrings.T("beef.elo.board")));
            if (d.BoardError != null) { body.Add(CompUi.ErrorBlock(d.BoardError, () => ShowList(2))); return; }
            if (d.Board.Entries.Count == 0) { body.Add(Duo.Empty(CompStrings.T("beef.elo.empty"))); return; }
            string N(long v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
            body.Add(DuoBoard.Board(d.Board.Entries.Select(e => new DuoRank(e.Rank, e.UserId == Me ? CompStrings.T("common.you") : string.IsNullOrWhiteSpace(e.Alias) ? CompStrings.T("common.unnamed") : e.Alias, e.UserId, N(e.Value), e.UserId == Me)).ToList(), "beef-elo-board"));
            if (d.Board.Me != null && d.Board.Entries.All(e => e.UserId != d.Board.Me.UserId))
                body.Add(DuoBoard.Row(new DuoRank(d.Board.Me.Rank, CompStrings.T("common.you"), Me, N(d.Board.Me.Value), true)));
        }

        // ---------------------------------------------------------------- challenge form (sheet)

        private void ShowForm()
        {
            var sheet = new DuoSheet(CompStrings.T("beef.form.title"), CompStrings.T("beef.form.note"));
            sheet.name = "beef-form-sheet";
            string topic = null, stakeText = "";
            bool invite = _opponentId != null;
            var status = CompUi.Small("");
            var chips = new List<Button>();
            var topicRow = CompUi.ChipRow();
            foreach (var t in CompStrings.BeefTopics)
            {
                var id = t;
                var chip = CompUi.Chip(BeefRules.TopicLabel(id), () =>
                {
                    topic = id;
                    for (int i = 0; i < chips.Count; i++) chips[i].EnableInClassList("duo-chip-on", CompStrings.BeefTopics[i] == id);
                }, false);
                chips.Add(chip);
                topicRow.Add(chip);
            }
            sheet.AddContent(CompUi.Label(CompStrings.T("beef.form.topic")));
            sheet.AddContent(topicRow);
            sheet.AddContent(CompUi.Label(CompStrings.T("beef.form.stake")));
            var stake = CompUi.Field(CompStrings.T("beef.form.stakeField"), "", v => stakeText = v, 12);
            stake.name = "beef-form-stake";
            sheet.AddContent(stake);
            if (_opponentId != null)
            {
                sheet.AddContent(CompUi.Label(CompStrings.T("beef.form.opponent")));
                Button inv = null, open = null;
                inv = CompUi.Chip(CompStrings.F("beef.form.invite", _opponentName), () => { invite = true; inv.EnableInClassList("duo-chip-on", true); open.EnableInClassList("duo-chip-on", false); }, true);
                open = CompUi.Chip(CompStrings.T("beef.form.openToAll"), () => { invite = false; inv.EnableInClassList("duo-chip-on", false); open.EnableInClassList("duo-chip-on", true); }, false);
                sheet.AddContent(CompUi.ChipRow(inv, open));
            }
            sheet.AddContent(status);
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
                    sheet.Close();
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
            submit.name = "beef-form-submit";
            sheet.AddContent(submit);
            sheet.AddSecondary(CompStrings.T("common.cancel"));
            ShowSheet(sheet);
        }

        // ---------------------------------------------------------------- detail

        private void ShowDetail(string matchId)
        {
            var page = Clear(true);
            _detailId = matchId;
            _refetched = false;
            var body = El.Div("duo-comp-body");
            page.Add(body);
            Load(body, ct => Hub.Api.Beef(matchId, ct), RenderDetail);
        }

        private void RenderDetail(VisualElement body, BeefMatch m)
        {
            _detail = m;
            var phase = BeefRules.PhaseOf(m.Status);
            body.Add(Duo.LargeTitle(BeefRules.TopicLabel(m.TopicId)));
            var tags = El.Div("duo-comp-tags", CompUi.Tag(PhaseText(phase), PhaseTone(phase)));
            body.Add(tags);
            var vs = CompUi.Text("…", "duo-beef-vs");
            FillVs(vs, m);
            body.Add(vs);
            // Hero: the pot the vote decides (estimate from the server's stake and fee), else the stake.
            var hero = m.FeeBps > 0
                ? Duo.Hero(CompStrings.T("beef.pot"), Money.Cash(BeefRules.EstimatedPayoutCents(m)), CompStrings.F("beef.feeLine", Money.Bps(m.FeeBps, false)))
                : Duo.Hero(CompStrings.T("beef.stake"), Money.Cash(m.StakeCents));
            hero.name = "beef-hero";
            body.Add(hero);
            body.Add(CompUi.Pair(CompStrings.T("beef.stake"), Money.Cash(m.StakeCents)));
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
                body.Add(Duo.Notice(CompStrings.T("beef.outcome." + outcome), outcome == "won" ? Duo.Tone.Success : outcome == "lost" ? Duo.Tone.Danger : Duo.Tone.Neutral));
                var delta = BeefRules.EloDeltaFor(m, Me);
                if (delta.HasValue) body.Add(CompUi.Pair(CompStrings.T("beef.elo"), BeefRules.SignedElo(delta.Value), delta.Value >= 0 ? "duo-value-positive" : "duo-value-negative"));
            }
            else if (phase == BeefPhase.Closed && string.IsNullOrEmpty(m.WinnerUserId)) body.Add(CompUi.Small(CompStrings.T("beef.outcome.draw")));
            else if (phase == BeefPhase.Closed) body.Add(CompUi.Small(CompStrings.T("beef.closed")));

            var actions = BeefRules.ActionsFor(m, Me);
            var status = CompUi.Small("");
            var row = CompUi.Row();
            if (actions.CanAccept) row.Add(CompUi.Btn(CompStrings.F("beef.accept", Money.Cash(m.StakeCents)), () => Act("accept", m, (id, k, ct) => Hub.Api.AcceptBeef(id, k, ct), status)));
            if (actions.CanDecline) row.Add(CompUi.Btn(CompStrings.T("beef.decline"), () => Act("decline", m, (id, k, ct) => Hub.Api.DeclineBeef(id, k, ct), status), CompUi.Plain));
            if (actions.CanCancel) row.Add(CompUi.Btn(CompStrings.T("beef.cancel"), () => Act("cancel", m, (id, k, ct) => Hub.Api.CancelBeef(id, k, ct), status), CompUi.Danger));
            if (row.childCount > 0) body.Add(row);
            if (actions.CanVote)
            {
                body.Add(Duo.SectionHeader(CompStrings.T("beef.vote.title")));
                body.Add(CompUi.Small(CompStrings.T("beef.vote.note")));
                var accepter = m.AccepterUserId;
                var votes = CompUi.Row();
                var a = CompUi.Btn("…", () => Vote(m, m.ChallengerUserId, status), CompUi.Plain);
                FillBtn(a, m.ChallengerUserId);
                votes.Add(a);
                if (!string.IsNullOrEmpty(accepter))
                {
                    var b = CompUi.Btn("…", () => Vote(m, accepter, status), CompUi.Plain);
                    FillBtn(b, accepter);
                    votes.Add(b);
                }
                body.Add(votes);
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
