using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// Quests (2026-10-24, backend PR #9 `GET /quests`): a Duo app in the iOS Fitness/App Store language. The gm streak card
    /// on top (opens the chest), today's three quests with progress bars and a Claim / Go capsule, and this week's chain of
    /// five as a stepped track. Progress is the server's (realtime `quest_progress` refreshes the open app); "Go" follows the
    /// quest's `deepLink` to the app that does it; Claim pays with a coin burst, confetti and the paid amount rising.
    /// </summary>
    public sealed class QuestsApp : CityApp
    {
        public override string Id => DeepLinks.QuestsApp;
        public override string Title => RetentionText.T("app.title");
        public override string HomeLabel => "Quests";
        public override string Color => "#ff2d78";

        internal override CityView CreateView(CityLifeHub hub) => new QuestsView(hub);

        /// <summary>The season's card under the streak (Season XP comes from quests); set by SeasonOverlays.</summary>
        public static Func<VisualElement> SeasonCard;

        private static readonly Dictionary<string, (string icon, string tint)> Verbs = new Dictionary<string, (string, string)>
        {
            ["shift_complete"] = ("jobs", "blue"), ["perp_open"] = ("perps", "green"), ["perp_close"] = ("perps", "teal"),
            ["prediction_place"] = ("predict", "purple"), ["alpha_call"] = ("ads", "orange"), ["paid_call"] = ("calls", "indigo"),
            ["tip_send"] = ("cash", "pink"), ["home_visit"] = ("home", "red"),
            ["shift_start"] = ("jobs", "blue"), ["trade_open"] = ("pulse", "green"), ["chest_open"] = ("star", "orange"),
        };

        private sealed class QuestsView : CityView
        {
            private readonly RetentionService _svc;
            private readonly CelebrationBurst _burst;
            private Label _streakSub, _dailySub, _weekSub;
            private readonly HashSet<string> _claiming = new HashSet<string>();
            private readonly Dictionary<string, string> _keys = new Dictionary<string, string>();

            public QuestsView(CityLifeHub hub) : base(hub, RetentionText.T("app.title"))
            {
                _svc = RetentionService.Current;
                Page.AddToClassList("quests");
                Page.name = "quests-page";
                foreach (var name in new[] { "UI/kit", "UI/retention" })   // kit: the coin and confetti particles
                {
                    var sheet = Resources.Load<StyleSheet>(name);
                    if (sheet != null) Page.styleSheets.Add(sheet);
                }
                _burst = new CelebrationBurst(coins: 18, confetti: 70, puffs: 0);
                _burst.AddToClassList("quests-burst");
                Page.Add(_burst);
                if (_svc == null) { Body.Add(Duo.Empty(CityLifeStrings.T("common.notLive"))); return; }
                _svc.QuestsChanged += Render;
                _svc.StatusChanged += Render;
                Render();
                Bus.Publish(new GameplayEventSignal { kind = GameplayEvents.QuestsOpen, id = "quests" });
                if (FirstSessionService.Current != null) FirstSessionService.Current.Changed += Render;
                _ = _svc.RefreshQuests();
                _ = _svc.RefreshStatus(force: false);
            }

            public override void Dispose()
            {
                if (_svc != null) { _svc.QuestsChanged -= Render; _svc.StatusChanged -= Render; }
                if (FirstSessionService.Current != null) FirstSessionService.Current.Changed -= Render;
                base.Dispose();
            }

            protected override void Tick()
            {
                var b = _svc?.Board; var s = _svc?.Status;
                double now = ServerClock.NowMs;
                if (_streakSub != null && s != null) _streakSub.text = StreakLine(s, now);
                if (_dailySub != null && b != null) _dailySub.text = RetentionText.F("app.dailySub", b.DailyDone, DailyStatus.Countdown(Math.Max(0, (b.DailyResetsAtMs - now) / 1000.0)));
                if (_weekSub != null && b != null) _weekSub.text = WeekLine(b, now);
            }

            private static string StreakLine(DailyStatus s, double now) =>
                RetentionText.F("app.streakSub", s.ChestReady ? RetentionText.T("tile.ready") : RetentionText.F("tile.next", DailyStatus.Countdown(s.SecondsToReset(now))));

            private static string WeekLine(QuestBoard b, double now)
            {
                string left = DailyStatus.Countdown(Math.Max(0, (b.WeeklyResetsAtMs - now) / 1000.0));
                if (left.Length > 8 || b.WeeklyResetsAtMs - now > 86400000) left = Math.Ceiling((b.WeeklyResetsAtMs - now) / 86400000.0) + "d";
                return b.WeeklyDone >= b.Weekly.Count ? RetentionText.F("app.weekDone", left)
                    : RetentionText.F("app.weekSub", Math.Min(b.Weekly.Count, b.WeeklyDone + 1), b.Weekly.Count, left);
            }

            private void Render()
            {
                if (!Alive) return;
                Body.Clear();
                double now = ServerClock.NowMs;
                // ---- streak card
                var s = _svc.Status;
                if (s != null)
                {
                    var card = El.Btn("", () => { if (_svc.Status != null) DailyChestSheet.Show(_svc); }, "quests-streak");
                    card.name = "quests-streak";
                    card.EnableInClassList("quests-streak--ready", s.ChestReady);
                    _streakSub = El.Text(StreakLine(s, now), "quests-streak-sub");
                    card.Add(El.Div("quests-streak-flame", LobbyArt.Icon("fire-element", "quests-streak-flame-icon")));
                    card.Add(El.Div("quests-streak-copy", El.Text(RetentionText.F("app.streak", s.ShownDay), "quests-streak-day"), _streakSub));
                    card.Add(LobbyArt.Icon("treasure-chest", "quests-streak-chest"));
                    foreach (var e in card.Query<VisualElement>().ToList()) if (e != card) e.pickingMode = PickingMode.Ignore;
                    Body.Add(card);
                }
                var season = SeasonCard?.Invoke();
                if (season != null) Body.Add(season);
                var b = _svc.Board;
                // ---- the starter card (queue 17, W5): the server's starter quests; before the server has them, a client
                // checklist of the same three steps from the first session (ticks only, no amounts: rewards are the server's)
                if (b != null && b.Starter.Count > 0 && !AllClaimed(b.Starter))
                {
                    int done = 0; foreach (var q in b.Starter) if (q.Completed) done++;
                    Body.Add(Duo.SectionHeader(FirstText.F("starter.title", done, b.Starter.Count)));
                    var starter = El.Div("quests-list quests-starter");
                    starter.name = "quests-starter";
                    foreach (var q in b.Starter) starter.Add(Row(q, false));
                    Body.Add(starter);
                }
                else if ((b == null || b.Starter.Count == 0) && FirstSessionService.Current is FirstSessionService fs && fs.Guiding)
                    Body.Add(LocalStarter(fs));
                if (b == null) { Body.Add(Duo.Empty(RetentionText.T("app.empty"))); return; }

                // ---- today
                Body.Add(Duo.SectionHeader(RetentionText.T("app.today")));
                _dailySub = El.Text("", "quests-sub");
                Body.Add(_dailySub);
                var daily = El.Div("quests-list");
                foreach (var q in b.Daily) daily.Add(Row(q, false));
                Body.Add(daily);

                // ---- this week: the chain
                Body.Add(Duo.SectionHeader(RetentionText.T("app.week")));
                _weekSub = El.Text("", "quests-sub");
                Body.Add(_weekSub);
                var chain = El.Div("quests-list quests-chain");
                for (int i = 0; i < b.Weekly.Count; i++) chain.Add(Row(b.Weekly[i], true, i == b.Weekly.Count - 1));
                Body.Add(chain);
                Body.Add(El.Div("quests-bottom-space"));
                Tick();
            }

            private VisualElement Row(Quest q, bool weekly, bool lastStep = false)
            {
                var row = El.Div("quests-row");
                row.name = "quest-" + q.Id;
                row.EnableInClassList("quests-row--done", q.Completed);
                row.EnableInClassList("quests-row--claimed", q.Claimed);
                VisualElement lead;
                if (weekly)
                {
                    lead = El.Div("quests-step", El.Text(q.Claimed || q.Completed ? "✓" : q.Step.ToString(), "quests-step-n"));
                    if (!lastStep) lead.Add(El.Div("quests-step-line"));
                }
                else
                {
                    var (icon, tint) = Verbs.TryGetValue(q.Verb ?? "", out var v) ? v : ("dream", "gray");
                    lead = El.Div("quests-lead", Duo.IconSquare(icon, tint));
                }
                var title = El.Text(string.IsNullOrEmpty(q.Title) ? q.Description : q.Title, "quests-title");
                title.enableRichText = false;
                var desc = El.Text((q.Description ?? "") + "", "quests-desc");
                desc.enableRichText = false;
                var fill = El.Div("quests-bar-fill");
                fill.style.width = Length.Percent(q.Fraction * 100f);
                var bar = El.Div("quests-bar", fill);
                var meta = El.Div("quests-meta",
                    El.Text(q.Progress + " / " + q.Target, "quests-progress"),
                    El.Div("ct-grow"),
                    El.Text(RetentionText.F("app.reward", RetentionText.Money(q.RewardCashCents), q.RewardXp), "quests-reward"));
                var body = El.Div("quests-body", title, desc, bar, meta);
                Button action;
                if (q.Claimed) action = Duo.Pill(RetentionText.T("app.claimed"), null, Duo.PillKind.Locked);
                else if (q.Completed) { action = Duo.Pill(RetentionText.T("app.claim"), () => Claim(q, row), Duo.PillKind.Primary); action.AddToClassList("quests-claim"); }
                else action = Duo.Pill(RetentionText.T("app.go"), () => Go(q), Duo.PillKind.Plain);
                action.name = "quest-action-" + q.Id;
                if (q.Claimed) action.SetEnabled(false);
                row.Add(lead); row.Add(body); row.Add(action);
                return row;
            }

            private static bool AllClaimed(List<Quest> list) { foreach (var q in list) if (!q.Claimed) return false; return true; }

            /// <summary>The client starter checklist (an older backend without starter quests): the same three steps, ticked.</summary>
            private VisualElement LocalStarter(FirstSessionService fs)
            {
                var (done, total) = fs.State.Progress(FirstSessionCopy.Starter, fs.Available);
                var list = El.Div("quests-list quests-starter");
                list.name = "quests-starter";
                foreach (var beat in FirstSessionCopy.Starter)
                {
                    if (!fs.Available(beat)) continue;
                    bool ok = fs.State.IsDone(beat);
                    string key = beat.ToString().ToLowerInvariant();
                    var row = El.Div("quests-row");
                    row.name = "quest-starter-" + key;
                    row.EnableInClassList("quests-row--done", ok);
                    var title = El.Text(FirstText.T("starter." + key), "quests-title");
                    var desc = El.Text(FirstText.T("starter." + key + ".desc"), "quests-desc");
                    var (icon, tint) = beat == FirstBeat.Shift ? ("jobs", "blue") : beat == FirstBeat.Trade ? ("pulse", "green") : ("star", "orange");
                    row.Add(El.Div("quests-lead", Duo.IconSquare(icon, tint)));
                    row.Add(El.Div("quests-body", title, desc));
                    var link = beat == FirstBeat.Shift ? "ctlife://jobs" : beat == FirstBeat.Trade ? "ctlife://pulse" : "ctlife://chest";
                    var action = ok ? Duo.Pill("✓", null, Duo.PillKind.Locked) : Duo.Pill(RetentionText.T("app.go"), () => Go(new Quest { Id = "starter-" + key, DeepLink = link }), Duo.PillKind.Plain);
                    if (ok) action.SetEnabled(false);
                    row.Add(action);
                    list.Add(row);
                }
                return El.Div(null, Duo.SectionHeader(FirstText.F("starter.title", done, total)), list);
            }

            private void Go(Quest q)
            {
                if (string.IsNullOrEmpty(q.DeepLink)) return;
                if (DeepLinks.AppFor(q.DeepLink) == DeepLinks.QuestsApp && q.DeepLink.IndexOf("chest", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (_svc.Status != null) DailyChestSheet.Show(_svc);   // the GM chest step opens the chest itself
                    return;
                }
                Log.Info("retention", "quest go " + q.Id + " -> " + q.DeepLink);
                RetentionService.Route(q.DeepLink);
            }

            private void Claim(Quest q, VisualElement row)
            {
                if (_claiming.Contains(q.Id)) return;
                _claiming.Add(q.Id);
                if (!_keys.TryGetValue(q.Id, out var key)) _keys[q.Id] = key = Guid.NewGuid().ToString();
                Run(ct => _svc.ClaimQuest(q, key), r =>
                {
                    _claiming.Remove(q.Id); _keys.Remove(q.Id);
                    Celebrate(row, r.cashCents, r.xp, r.already);
                    if (Services.TryGet<PlayerProfile>(out var p)) _ = p.Refresh().ContinueWith(_ => { });
                }, err =>
                {
                    _claiming.Remove(q.Id);
                    if (!err.OutcomeUnknown) _keys.Remove(q.Id);
                    Bus.Publish(ToastSignal.Text(err.Message, ToastKind.Bad));
                }, write: true);
            }

            private void Celebrate(VisualElement row, long cash, int xp, bool already)
            {
                if (already) return;
                Cues.Play("sfx_take_profit");
                DeviceFeel.Confirm();
                var at = _burst.WorldToLocal(row.worldBound.center);
                _burst.Coins(new Vector2(_burst.resolvedStyle.width * 0.78f, at.y), 16);
                _burst.Confetti(60);
                var paid = El.Text(RetentionText.F("app.paid", RetentionText.Money(cash), xp), "quests-paid");
                paid.pickingMode = PickingMode.Ignore;
                Page.Add(paid);
                var start = Page.WorldToLocal(row.worldBound.center);
                Tween.Run(paid, 1.3f, t =>
                {
                    paid.style.translate = new Translate(start.x - 160f, start.y - 40f - 150f * Ease.OutCubic(t));
                    paid.style.opacity = t < 0.15f ? t / 0.15f : Mathf.Clamp01((1f - t) / 0.35f);
                }, Ease.Linear, skipWhenReduced: false);
                paid.schedule.Execute(() => paid.RemoveFromHierarchy()).StartingIn(1400);
            }
        }
    }
}
