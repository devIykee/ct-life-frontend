using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Careers
{
    /// <summary>
    /// The Jobs app's Career tab (backend PR #16, `GET /careers?view=ladders`): your ladder as a feature card (title, rank
    /// n of 7, the pay bonus, an XP bar to the next rank and what it unlocks), the seven-rank track, and the other ladders
    /// with Join (or the switch cooldown as a countdown). Every number is the server's.
    /// </summary>
    public sealed partial class CareersApp
    {
        private readonly Dictionary<string, string> _joinKeys = new Dictionary<string, string>();

        private async void LoadLadders()
        {
            var ct = BeginView(out int version);
            Message(CareersStrings.T("ladder.loading"));
            try
            {
                var board = await Deadline.Run(c => Api.Ladders(c), ct, 30000);
                if (!Owns(version)) return;
                RenderLadders(board);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception e)
            {
                Log.Warn("careers", "GET /careers?view=ladders failed: " + e.GetType().Name);
                if (Owns(version)) Message(e is TimeoutException ? CareersStrings.T("timeout") : Deadline.Describe(e), true, LoadLadders);
            }
        }

        private void RenderLadders(LadderBoard board)
        {
            _content.Clear();
            var mine = board.Mine;
            _held.text = mine != null ? mine.Title : "";
            if (board.Ladders.Count == 0) { Message(CareersStrings.T("ladder.empty")); return; }
            if (mine != null)
            {
                _content.Add(LadderFeature(mine));
                _content.Add(Duo.SectionHeader(CareersStrings.T("ladder.ranks")));
                var track = El.Div("duo-list duo-ladder-track");
                track.name = "careers-ladder-track";
                foreach (var r in mine.Ranks) track.Add(RankRow(r));
                Duo.MarkLast(track);
                _content.Add(track);
            }
            else _content.Add(Duo.Notice(CareersStrings.T("ladder.pick"), Duo.Tone.Info));

            _content.Add(Duo.SectionHeader(CareersStrings.T(mine != null ? "ladder.others" : "ladder.all")));
            var list = El.Div("duo-list");
            var others = board.Ladders.Where(l => mine == null || l.Id != mine.Id)
                .OrderByDescending(l => l.Id == board.SuggestedLadderId).ThenByDescending(l => l.Xp).ToList();
            foreach (var l in others) list.Add(OtherLadderRow(l, board, mine != null));
            Duo.MarkLast(list);
            _content.Add(list);
            _content.Add(Duo.Footnote(CareersStrings.F("ladder.note", board.JoinCooldownHours)));
        }

        private VisualElement LadderFeature(Ladder l)
        {
            var card = El.Div("duo-feature");
            card.name = "careers-ladder-mine";
            var sheen = El.Div("duo-feature-sheen");
            sheen.style.backgroundImage = Background.FromTexture2D(Duo.Sheen);
            card.Add(El.PassThrough(sheen));
            card.Add(El.PassThrough(El.Div("duo-feature-art", new UiIcon("jobs"))));
            card.Add(El.Text(CareersStrings.F("ladder.kicker", l.Name).ToUpperInvariant(), "duo-feature-kicker"));
            card.Add(El.Text(l.Title, "duo-feature-title"));
            string bonus = l.PayBonusBps > 0 ? " · " + CareersStrings.F("ladder.bonus", LadderBoard.Bonus(l.PayBonusBps)) : "";
            card.Add(El.Text(CareersStrings.F("ladder.rankOf", l.Rank, Math.Max(7, l.Ranks.Count)) + bonus, "duo-feature-meta"));
            var fill = El.Div("duo-meter-fill");
            fill.style.width = Length.Percent(l.Progress * 100f);
            var bar = El.Div("duo-meter duo-meter-green", fill);
            bar.style.marginTop = 10; bar.style.marginBottom = 6;
            card.Add(bar);
            string xpLine = l.NextRank == null || l.XpToNext == null
                ? CareersStrings.F("ladder.xpTop", l.Xp)
                : CareersStrings.F("ladder.xpNext", l.Xp, l.XpToNext.Value, l.NextRank.Title);
            card.Add(El.Text(xpLine, "duo-feature-meta"));
            if (l.NextRank != null) card.Add(El.Text(CareersStrings.F("ladder.unlocks", Perks(l.NextRank)), "duo-feature-meta"));
            return card;
        }

        private static string Perks(LadderRank r)
        {
            var parts = new List<string>();
            if (r.PayBonusBps > 0) parts.Add(CareersStrings.F("ladder.bonus", LadderBoard.Bonus(r.PayBonusBps)));
            foreach (var a in r.VenueAccess) parts.Add(LadderBoard.Area(a));
            if (!string.IsNullOrEmpty(r.CosmeticId)) parts.Add(CareersStrings.T("ladder.cosmetic"));
            return parts.Count == 0 ? CareersStrings.T("ladder.titleOnly") : string.Join(" · ", parts);
        }

        private VisualElement RankRow(LadderRank r)
        {
            var row = new DuoRow().Leading(El.PassThrough(El.Div("duo-app-tile duo-tint-" + (r.Achieved || r.Current ? "green" : r.Next ? "orange" : "gray"),
                    El.Text(r.Rank.ToString(), "duo-ladder-rank-n"))))
                .Title(r.Title, r.Current ? "duo-row-title-strong" : null)
                .Subtitle(CareersStrings.F("ladder.rankXp", r.XpRequired) + " · " + Perks(r), twoLines: true);
            row.name = "careers-rank-" + r.Rank;
            row.EnableInClassList("duo-row-hired", r.Current);
            string pill = r.Current ? CareersStrings.T("ladder.current") : r.Achieved ? "✓" : r.Next ? CareersStrings.T("ladder.next") : "";
            if (!string.IsNullOrEmpty(pill)) row.Trailing(Duo.Pill(pill, null, r.Current ? Duo.PillKind.Primary : Duo.PillKind.Locked));
            return row;
        }

        private VisualElement OtherLadderRow(Ladder l, LadderBoard board, bool switching)
        {
            string sub = l.Xp > 0 ? CareersStrings.F("ladder.kept", l.Title, l.Xp) : CareersStrings.F("ladder.jobs", l.JobIds.Count);
            if (l.Suggested || l.Id == board.SuggestedLadderId) sub = CareersStrings.T("ladder.suggested") + " · " + sub;
            var row = new DuoRow().Leading(El.PassThrough(El.Div("duo-app-tile duo-tint-" + Tint(l.Id), new UiIcon("jobs"))))
                .Title(l.Name, "duo-row-title-strong").Subtitle(sub, twoLines: true);
            row.name = "careers-ladder-" + l.Id;
            bool can = !switching || board.CanSwitchNow(DateTime.UtcNow);
            Button pill;
            if (can)
            {
                pill = Duo.Pill(CareersStrings.T(switching ? "ladder.switch" : "ladder.join"), () => Join(l), Duo.PillKind.Primary);
                pill.name = "careers-join-" + l.Id;
            }
            else
            {
                var left = board.CanSwitchAtUtc.Value - DateTime.UtcNow;
                pill = Duo.Pill(CareersStrings.F("ladder.in", Span(left)), null, Duo.PillKind.Locked);
                pill.SetEnabled(false);
            }
            row.Trailing(pill);
            return row;
        }

        private static string Span(TimeSpan t)
        {
            if (t.TotalSeconds <= 0) return "0m";
            if (t.TotalDays >= 1) return (int)t.TotalDays + "d " + t.Hours + "h";
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            return Math.Max(1, t.Minutes) + "m";
        }

        private async void Join(Ladder l)
        {
            if (!_joinKeys.TryGetValue(l.Id, out var key)) _joinKeys[l.Id] = key = Guid.NewGuid().ToString();
            var ct = BeginView(out int version);
            try
            {
                var r = await Deadline.Run(c => Api.JoinLadder(l.Id, key, c), ct, 30000);
                _joinKeys.Remove(l.Id);
                Log.Info("careers", $"joined ladder {r.LadderId} rank {r.Rank} ({r.Title}) already {r.AlreadyMember}");
                Bus.Publish(ToastSignal.Text(CareersStrings.F("ladder.joined", r.Title), ToastKind.Good));
                Cues.Play("sfx_rank_up", 0.7f);
                if (Owns(version)) LoadLadders();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception e)
            {
                if (e is ApiException a && a.Is("CAREER_SWITCH_COOLDOWN"))
                {
                    _joinKeys.Remove(l.Id);
                    var retry = CareersWire.Time((a.Details as Newtonsoft.Json.Linq.JObject)?["retryAt"]);
                    Bus.Publish(ToastSignal.Text(CareersStrings.F("ladder.cooldown", retry.HasValue ? Span(retry.Value - DateTime.UtcNow) : "72h"), ToastKind.Warning));
                }
                else
                {
                    if (!(e is ApiException ae) || ae.Status < 500) _joinKeys.Remove(l.Id);
                    Bus.Publish(ToastSignal.Text(Deadline.Describe(e), ToastKind.Bad));
                }
                if (Owns(version)) LoadLadders();
            }
        }
    }
}
