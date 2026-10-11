using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>Per env+user device storage for the week snapshot and today's clue answer (ids and numbers only).</summary>
    internal static class CityPrefs
    {
        private static string Key(string user, string k) => $"ctlife.{Env.Current.id}.citylife.{user}.{k}";

        public static WeekSnapshot LoadWeek(string user)
        {
            try { return WeekSnapshot.FromJson(Json.ParseRaw(PlayerPrefs.GetString(Key(user, "week"), "null"))); }
            catch (Exception) { return null; }
        }

        public static void SaveWeek(string user, WeekSnapshot s)
        {
            PlayerPrefs.SetString(Key(user, "week"), s.ToJson().ToString(Newtonsoft.Json.Formatting.None));
            PlayerPrefs.Save();
        }

        public static bool ClueAnswered(string user, string dateUtc) => PlayerPrefs.GetString(Key(user, "clue"), "") == dateUtc;

        public static void MarkClueAnswered(string user, string dateUtc)
        {
            PlayerPrefs.SetString(Key(user, "clue"), dateUtc ?? "");
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// The Week app (id `week`, Phase 4): Sunday Unlock countdown, Weekly Meta (now and next), Market Mood, live server
    /// events, the daily alpha clue, and a "since you last checked" recap with a copyable share card.
    /// </summary>
    public sealed class CityWeekApp : CityApp
    {
        public override string Id => "week";
        public override string Title => CityLifeStrings.T("app.week");
        public override string Color => "#8854d0";
        public override int Phase => 4;

        internal override CityView CreateView(CityLifeHub hub) => new WeekView(hub);

        private sealed class WeekView : CityView
        {
            private readonly Label _unlock, _reset, _mood;
            private readonly VisualElement _metaBox = El.Div(), _eventsBox = El.Div(), _clueBox = El.Div(), _recapBox = El.Div();
            private readonly DailyClueFlow _clue = new DailyClueFlow();
            private readonly WeekClock.Weekly _unlockAt, _resetAt;
            private MetaView _meta;
            private CityError _metaError;
            private string _answer = "";
            private Label _clueExpires, _moodSub;
            private bool _metaLoading, _eventsLoading;
            private bool? _recapMoodKnown;
            private int _recapMoodBps;

            public WeekView(CityLifeHub hub) : base(hub, CityLifeStrings.T("week.title"))
            {
                var economy = Services.Get<ConfigStore>().Config("economy");
                _unlockAt = WeekClock.ParseOr(CityWire.Str(economy?["sundayUnlockCron"]), WeekClock.ContractUnlock);
                _resetAt = WeekClock.ParseOr(CityWire.Str(economy?["mondayResetCron"]), WeekClock.ContractReset);
                Page.name = "week-page";
                Page.AddToClassList("duo-week");
                // Hero: the countdown to Sunday Unlock (from the server's cron), reset and mood as rows below.
                var hero = Duo.Hero(CityLifeStrings.T("week.unlockLabel"), "—");
                hero.name = "week-hero";
                _unlock = hero.Q<Label>(className: "duo-hero-value");
                Body.Add(hero);
                Body.Add(CityUi.Small(CityLifeStrings.F("week.unlockAt", _unlockAt.ToString())));
                var reset = new DuoRow().Leading(Duo.IconSquare("rotate", "blue")).Title(CityLifeStrings.T("week.resetRow"));
                reset.AddToClassList("duo-row-static");
                _reset = El.Text("", "duo-row-value");
                reset.Trailing(_reset);
                var mood = new DuoRow().Leading(Duo.IconSquare("perps", "green")).Title(CityLifeStrings.T("week.moodRow")).Subtitle(CityLifeStrings.T("week.moodSub"), twoLines: true);
                mood.AddToClassList("duo-row-static");
                mood.name = "week-mood";
                _moodSub = mood.SubtitleLabel;
                _mood = El.Text("", "duo-row-value");
                mood.Trailing(_mood);
                var rows = El.Div("duo-list", reset, mood);
                Duo.MarkLast(rows);
                Body.Add(rows);
                Body.Add(_metaBox);
                Body.Add(_clueBox);
                Body.Add(_eventsBox);
                Body.Add(_recapBox);
                _clue.Changed += RenderClue;
                LoadMeta();
                LoadEvents();
                LoadClue();
                Tick();
                RenderClue();
            }

            private DateTimeOffset Now => DateTimeOffset.FromUnixTimeMilliseconds((long)Hub.NowMs);

            protected override void Tick()
            {
                Hub.RefreshMood();
                var now = Now;
                _unlock.text = WeekClock.Countdown(WeekClock.Next(_unlockAt, now), now);
                _reset.text = WeekClock.Countdown(WeekClock.Next(_resetAt, now), now);
                // Known: the mood and SOL's 24h move trail the row. Offline: the reason goes in the subtitle, nothing trails.
                // Known: SOL's 24h move trails the row, the mood line is the subtitle. Offline: the reason is the subtitle.
                _mood.text = Hub.MoodKnown ? Money.Bps(Hub.MoodBps) : "";
                if (_moodSub != null) _moodSub.text = Hub.MoodKnown ? CityLifeStrings.T(MarketMood.Key(Hub.MoodBps)) + " · " + CityLifeStrings.T("week.moodSub") : CityLifeStrings.T("week.moodOffline");
                _mood.EnableInClassList("duo-value-positive", Hub.MoodKnown && Hub.MoodBps > 0);
                _mood.EnableInClassList("duo-value-negative", Hub.MoodKnown && Hub.MoodBps < 0);
                if (_clueExpires != null && _clue.Clue?.ExpiresAtMs != null)
                    _clueExpires.text = CityLifeStrings.F("clue.expires", WeekClock.Countdown(_clue.Clue.ExpiresAtMs.Value - Hub.NowMs));
                if (_recapMoodKnown != Hub.MoodKnown || (Hub.MoodKnown && _recapMoodBps != Hub.MoodBps)) RenderRecap();
            }

            // ------------------------------------------------------------ Weekly Meta

            private void LoadMeta()
            {
                if (_metaLoading) return;
                _metaLoading = true;
                _metaError = null;
                RenderMeta();
                Run(ct => Hub.Api.Meta(ct), m => { _metaLoading = false; _metaError = null; _meta = m; RenderMeta(); RenderRecap(); },
                    e => { _metaLoading = false; _meta = null; _metaError = e; RenderMeta(); RenderRecap(); }, false);
            }

            private void RenderMeta()
            {
                _metaBox.Clear();
                _metaBox.Add(Duo.SectionHeader(CityLifeStrings.T("week.metaSection")));
                var card = CityUi.Card();
                card.name = "week-meta";
                if (_meta != null && _meta.HasBonus)
                {
                    card.Add(CityUi.H2(CityLifeStrings.MetaTitle(_meta.BonusId)));
                    card.Add(CityUi.Text(CityLifeStrings.MetaBody(_meta.BonusId)));
                }
                else if (_metaLoading) card.Add(CityUi.Small(CityLifeStrings.T("common.loading")));
                else if (_metaError != null && !_metaError.Is("NOT_FOUND"))
                {
                    card.Add(Duo.Notice(_metaError.Message, Duo.Tone.Danger));
                    card.Add(CityUi.Row(CityUi.Btn(CityLifeStrings.T("common.retry"), LoadMeta, CityUi.Plain)));
                }
                else card.Add(CityUi.Small(CityLifeStrings.T("week.metaUnknown")));
                if (_meta != null && _meta.PerkCents > 0) card.Add(CityUi.Pair(CityLifeStrings.T("week.metaPerkRow"), Money.Cash(_meta.PerkCents)));
                var next = MetaRotation.Next(_meta, MetaRotation.Read(Services.Get<ConfigStore>().Config("meta")));
                if (next != null) card.Add(CityUi.Pair(CityLifeStrings.T("week.metaNextRow"), CityLifeStrings.MetaTitle(next)));
                _metaBox.Add(card);
            }

            // ------------------------------------------------------------ server events

            private void LoadEvents()
            {
                if (_eventsLoading) return;
                _eventsLoading = true;
                RenderEvents(null);
                Run(ct => Hub.Api.ActiveEvents(ct), events => { _eventsLoading = false; RenderEvents(events); },
                    e => { _eventsLoading = false; RenderEvents(null, e); }, false);
            }

            private void RenderEvents(List<(string id, long? atMs, JObject @params)> events, CityError error = null)
            {
                _eventsBox.Clear();
                _eventsBox.Add(Duo.SectionHeader(CityLifeStrings.T("week.events")));
                if (_eventsLoading) _eventsBox.Add(CityUi.Small(CityLifeStrings.T("common.loading")));
                else if (error != null)
                {
                    _eventsBox.Add(Duo.Notice(error.Message, Duo.Tone.Danger));
                    _eventsBox.Add(CityUi.Row(CityUi.Btn(CityLifeStrings.T("common.retry"), LoadEvents, CityUi.Plain)));
                }
                else if (events == null || events.Count == 0) _eventsBox.Add(CityUi.Small(CityLifeStrings.T("week.noEvents")));
                else
                {
                    var list = El.Div("duo-list");
                    foreach (var e in events)
                    {
                        var row = new DuoRow().Leading(Duo.IconSquare("bell", "red")).Title(CityLifeStrings.EventLabel(e.id), "duo-row-title-strong");
                        if (e.atMs.HasValue) row.Subtitle(CityUi.When(e.atMs.Value));
                        row.AddToClassList("duo-row-static");
                        row.Trailing(CityUi.Tag(CityLifeStrings.T("week.liveTag"), "red"));
                        list.Add(row);
                    }
                    Duo.MarkLast(list);
                    _eventsBox.Add(list);
                }
            }

            // ------------------------------------------------------------ daily clue

            private void LoadClue()
            {
                if (!_clue.BeginLoad()) return;
                Run(ct => Hub.Api.DailyClue(ct), c =>
                {
                    var date = string.IsNullOrEmpty(c.DateUtc) ? Now.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : c.DateUtc;
                    _clue.Loaded(c, Hub.NowMs, CityPrefs.ClueAnswered(Hub.UserId, date));
                }, e => _clue.LoadFailed(e), false);
            }

            private void RenderClue()
            {
                if (!Alive) return;
                _clueBox.Clear();
                _clueExpires = null;
                _clueBox.Add(Duo.SectionHeader(CityLifeStrings.T("clue.title")));
                var card = CityUi.Card(CityUi.Small(CityLifeStrings.T("clue.hint")));
                card.name = "week-clue";
                if (_meta?.BonusId == "alpha_clue_prize_x3") card.Add(CityUi.Small(CityLifeStrings.T("clue.alphaWeek")));
                switch (_clue.Stage)
                {
                    case ClueStage.Loading: card.Add(CityUi.Small(CityLifeStrings.T("common.loading"))); break;
                    case ClueStage.NoClue: card.Add(CityUi.Text(CityLifeStrings.T("clue.none"))); break;
                    case ClueStage.Unavailable: card.Add(CityUi.Small(CityLifeStrings.T("common.notLive"))); break;
                    case ClueStage.Error:
                        card.Add(Duo.Notice(_clue.Error?.Message ?? CityLifeStrings.T("err.generic"), Duo.Tone.Danger));
                        card.Add(CityUi.Row(CityUi.Btn(CityLifeStrings.T("common.retry"), LoadClue, CityUi.Plain)));
                        break;
                    case ClueStage.Expired: card.Add(CityUi.Text(CityLifeStrings.T("clue.expired"))); break;
                    default:
                        if (_clue.Clue != null)
                        {
                            card.Add(CityUi.Text(_clue.Clue.Text, "duo-week-clue"));
                            _clueExpires = CityUi.Small("");
                            card.Add(_clueExpires);
                        }
                        if (_clue.Stage == ClueStage.Ready || _clue.Stage == ClueStage.Unknown)
                        {
                            if (_clue.Stage == ClueStage.Unknown) { card.Add(Duo.Notice(CityLifeStrings.T("clue.unknown"), Duo.Tone.Warning)); _answer = _clue.LastAnswer ?? _answer; }
                            if (_clue.Error != null && _clue.Stage == ClueStage.Ready) card.Add(Duo.Notice(_clue.Error.Message, Duo.Tone.Danger));
                            var field = CityUi.Field(CityLifeStrings.T("clue.answer"), _answer, s => _answer = s, DailyClueFlow.MaxAnswerLength);
                            field.SetEnabled(_clue.Stage == ClueStage.Ready);
                            card.Add(field);
                            var submit = CityUi.Btn(CityLifeStrings.T(_clue.Stage == ClueStage.Unknown ? "common.retry" : "clue.submit"), Submit);
                            submit.name = "week-clue-submit";
                            card.Add(submit);
                        }
                        else if (_clue.Stage == ClueStage.Submitting) card.Add(CityUi.Small(CityLifeStrings.T("clue.checking")));
                        else if (_clue.Stage == ClueStage.Correct) card.Add(Duo.Notice(CityLifeStrings.F("clue.correct", Money.Cash(_clue.Result?.PrizeCents ?? 0)), Duo.Tone.Success));
                        else if (_clue.Stage == ClueStage.Wrong) card.Add(CityUi.Text(CityLifeStrings.T("clue.wrong")));
                        else if (_clue.Stage == ClueStage.AlreadyAnswered) card.Add(CityUi.Text(CityLifeStrings.T("clue.already")));
                        break;
                }
                _clueBox.Add(card);
                Tick();
            }

            private void Submit()
            {
                var key = _clue.BeginSubmit(_answer, Hub.NowMs);
                if (key == null) return;
                var normalized = _clue.LastAnswer;
                var date = string.IsNullOrEmpty(_clue.Clue?.DateUtc) ? Now.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : _clue.Clue.DateUtc;
                Run(ct => Hub.Api.AnswerClue(normalized, key, ct), r =>
                {
                    _clue.Answered(r);
                    CityPrefs.MarkClueAnswered(Hub.UserId, date);
                    Bus.Publish(new SfxSignal(r.Correct ? "sfx_ui_success" : "sfx_ui_error"));
                    if (r.Correct) Hub.RefreshProfile(force: true);
                }, e =>
                {
                    _clue.SubmitFailed(e);
                    if (_clue.Stage == ClueStage.AlreadyAnswered) CityPrefs.MarkClueAnswered(Hub.UserId, date);
                }, true);
            }

            // ------------------------------------------------------------ recap + share card

            private void RenderRecap()
            {
                _recapBox.Clear();
                _recapMoodKnown = Hub.MoodKnown;
                _recapMoodBps = Hub.MoodBps;
                var me = Hub.Profile?.Me;
                if (me == null) return;
                var week = WeekClock.WeekIso(Now);
                var before = CityPrefs.LoadWeek(Hub.UserId);
                var homeName = Hub.Homes.Find(h => h.Id == me.HomeId)?.Name;
                var lines = WeekRecap.Lines(before, me.CashCents, me.DebtCents, me.HomeId, homeName, _meta, Hub.MoodKnown ? (int?)Hub.MoodBps : null, Hub.RentNow());
                if (before == null || before.WeekIso != week)
                    CityPrefs.SaveWeek(Hub.UserId, new WeekSnapshot { WeekIso = week, HomeId = me.HomeId, CashCents = me.CashCents, DebtCents = me.DebtCents, AtMs = Hub.NowMs });
                var share = WeekRecap.Card(Hub.Profile?.Alias, week, lines);
                var shareLink = Duo.SectionHeader(CityLifeStrings.T("week.recap"), CityLifeStrings.T("week.share"), () => CityUi.Copy(share.Text()));
                shareLink.name = "week-recap-header";
                _recapBox.Add(shareLink);
                var list = El.Div("duo-list");
                list.name = "week-recap";
                foreach (var l in lines)
                {
                    var row = new DuoRow().Title(l);
                    row.TitleLabel.style.whiteSpace = WhiteSpace.Normal;
                    row.AddToClassList("duo-row-static");
                    list.Add(row);
                }
                Duo.MarkLast(list);
                _recapBox.Add(list);
            }

            public override void Dispose()
            {
                if (Disposed) return;
                _clue.Changed -= RenderClue;
                base.Dispose();
            }
        }
    }
}
