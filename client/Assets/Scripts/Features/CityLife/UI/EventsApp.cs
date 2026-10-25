using System;
using System.Globalization;
using System.Linq;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// The Events app on the Duo (lane B, 2026-10-24, backend PR #11):
    /// - the Breakpoint 2026 poster card: LIVE or the countdown to the start (server clock), the dates and the real venue,
    ///   where to go in the city, and "Watch the official stream" while `streamUrl` is set (opens the browser; never embedded);
    /// - the quest chain (booths 3 of 6, keynote trivia 3 right of 4, nods 5 of 8) with progress, in order;
    /// - the reward (Cash and the lanyard), "Collected" once claimed, and "Wear the lanyard";
    /// - the keynote trivia (multiple choice a–d, each question once) through POST /events/{slug}/progress;
    /// - the calendar: upcoming real-world events with countdowns.
    /// </summary>
    public sealed class EventsApp : CityApp
    {
        public override string Id => EventsService.AppId;
        public override string Title => EventText.T("app");
        public override string HomeLabel => "Events";
        public override string Color => "#aa67fb";
        public override DuoChrome Chrome => DuoChrome.Plain;

        /// <summary>The screen to open next ("trivia" from the kiosk); consumed by the next view.</summary>
        public static string Pending;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        /// <summary>Dev builds: answer a trivia question as a tap would (the capture route).</summary>
        public static Action<string, string> DevAnswer;
        public static Action DevCheckIn, DevToggleVisible, DevSayHi;
        public static Action<string> DevScrollTo;
#else
        public static Action<string, string> DevAnswer => null;
#endif

        internal override CityView CreateView(CityLifeHub hub) => new EventsView(hub);

        private sealed class EventsView : CityView
        {
            private readonly EventsService _svc;
            private bool _trivia;
            private Label _count, _countLabel;
            private readonly System.Collections.Generic.List<(Label label, long at)> _rowClocks = new System.Collections.Generic.List<(Label, long)>();
            private readonly System.Collections.Generic.Dictionary<string, string> _picked = new System.Collections.Generic.Dictionary<string, string>();
            private readonly System.Collections.Generic.Dictionary<string, bool> _right = new System.Collections.Generic.Dictionary<string, bool>();
            private bool _busy;
            private string _focusQ;

            public EventsView(CityLifeHub hub) : base(hub, EventText.T("app"))
            {
                _svc = EventsService.Current;
                Page.AddToClassList("evt-app");
                Page.name = "events-page";
                if (_svc == null) { Body.Add(Duo.Empty(CityLifeStrings.T("common.notLive"))); return; }
                _trivia = Pending == "trivia";
                Pending = null;
                _svc.Changed += Render;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                DevAnswer = (q, c) => { if (!Disposed) { _trivia = true; Answer(q, c); } };
                DevCheckIn = () => { if (!Disposed) CheckIn(); };
                DevScrollTo = cls => { if (!Disposed && Root.Q(className: cls) is VisualElement el) Root.ScrollTo(el); };
                DevSayHi = () => { if (!Disposed) foreach (var a in _svc.IrlBoard) if (!a.Self && a.Handle != null) { SayHi(a.Handle, a.Alias); break; } };
                DevToggleVisible = () => { if (!Disposed) Root.Q<Button>("events-irl-visible")?.SendEvent(new NavigationSubmitEvent { target = Root.Q<Button>("events-irl-visible") }); };
#endif
                if (!_svc.Loaded) _svc.Refresh();
                Render();
            }

            public override void Dispose()
            {
                if (_svc != null) _svc.Changed -= Render;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                DevAnswer = null; DevCheckIn = null; DevToggleVisible = null; DevScrollTo = null; DevSayHi = null;
#endif
                base.Dispose();
            }

            protected override bool Back()
            {
                if (!_trivia) return false;
                _trivia = false;
                Render();
                return true;
            }

            protected override void Tick()
            {
                if (_svc == null) return;
                long now = _svc.NowMs;
                var e = _svc.Breakpoint?.Event;
                if (_count != null && e != null)
                {
                    bool live = e.IsLive(now);
                    _count.text = EventWire.Countdown(live ? e.EndsAtMs - now : e.StartsAtMs - now);
                }
                foreach (var (label, at) in _rowClocks) label.text = EventWire.Short(at - now);
                if (_trivia && _cooldowns.Count > 0) TickCooldowns();
                // the start or the end just passed: read the calendar again (the realtime sweep can lag a minute)
                if (e != null && (now >= e.StartsAtMs && !_wasLive || now >= e.EndsAtMs)) { _wasLive = now >= e.StartsAtMs; _svc.Refresh(); }
            }

            private bool _wasLive;

            private void Render()
            {
                if (Disposed) return;
                Body.Clear();
                _rowClocks.Clear();
                _count = null;
                var title = _trivia ? EventText.T("trivia") : EventText.T("app");
                if (Page.Title != null) Page.Title.text = title;
                if (Page.Nav?.TitleLabel != null) Page.Nav.TitleLabel.text = title;
                if (_svc.LastError != null && !_svc.Loaded) { Body.Add(Duo.Notice(EventText.T("failed"), Duo.Tone.Danger)); return; }
                if (!_svc.Loaded) { Body.Add(CityUi.Small("…")); return; }
                if (_trivia) { RenderTrivia(); return; }
                var d = _svc.Breakpoint;
                if (d != null) { Poster(d); Irl(d); Quest(d); }
                Calendar();
            }

            // ------------------------------------------------------------ the poster card

            private void Poster(EventDetails d)
            {
                var e = d.Event;
                long now = _svc.NowMs;
                bool live = e.IsLive(now);
                _wasLive = live;
                var art = El.Div("evt-poster-art");
                var tex = Resources.Load<Texture2D>("UI/Events/hero");
                if (tex != null) art.style.backgroundImage = new StyleBackground(tex);
                var tag = El.Text(live ? EventText.T("live") : EventText.T("upcoming").ToUpperInvariant(), "evt-live" + (live ? "" : " evt-soon"));
                var body = El.Div("evt-poster-body",
                    El.Div("evt-poster-row", tag, El.Text(EventWire.Dates(e) + " · " + (e.RealVenue ?? e.City), "evt-poster-dates")));
                if (live)
                {
                    var (day, days) = e.Day(now);
                    _countLabel = El.Text((EventText.F("day", day, days) + " · " + EventText.T("endsIn")).ToUpperInvariant(), "evt-count-label");
                }
                else _countLabel = El.Text(EventText.T("startsIn").ToUpperInvariant(), "evt-count-label");
                _count = El.Text(EventWire.Countdown(live ? e.EndsAtMs - now : e.StartsAtMs - now), "evt-count");
                body.Add(_countLabel); body.Add(_count);
                body.Add(El.Text(EventText.T("where"), "evt-where"));
                if (e.StreamUrl != null)
                {
                    var url = e.StreamUrl;
                    var watch = El.Btn(EventText.T("watch").ToUpperInvariant() + "  ↗", () => { Log.Info("events", "open the official stream " + url); Application.OpenURL(url); }, "evt-watch");
                    watch.name = "events-watch";
                    body.Add(watch);
                    body.Add(El.Text(EventText.T("watch.note"), "evt-watch-note"));
                }
                Body.Add(El.Div("evt-poster", art, body));
            }

            // ------------------------------------------------------------ IRL check-in and the attendee board

            private string _irlStatus;
            private bool _irlBusy;

            private void Irl(EventDetails d)
            {
                if (!d.Event.IrlCheckin || !d.Event.IsLive(_svc.NowMs)) return;
                var card = El.Div("evt-irl");
                card.Add(El.Text(EventText.T("irl.kicker"), "evt-irl-kicker"));
                var head = El.Div("evt-irl-head", El.Text(EventText.T("irl.title"), "evt-irl-title"));
                var badge = El.Div("evt-irl-badge");
                var tex = Resources.Load<Texture2D>("UI/Events/irl_badge");
                if (tex != null) badge.style.backgroundImage = new StyleBackground(tex);
                head.Add(badge);
                card.Add(head);
                if (!_svc.IrlCheckedIn)
                {
                    card.Add(El.Text(EventText.T("irl.body"), "evt-irl-body"));
                    var go = El.Btn(_irlBusy ? EventText.T("irl.checking") : EventText.T("irl.button"), CheckIn, "evt-irl-btn");
                    go.name = "events-irl-checkin";
                    go.SetEnabled(!_irlBusy);
                    card.Add(go);
                }
                else
                {
                    card.Add(El.Text("✓ " + EventText.T("irl.done"), "evt-irl-done"));
                    var toggle = El.Btn((_svc.IrlVisible ? "● " : "○ ") + EventText.T("irl.visible"), () =>
                    {
                        if (_irlBusy) return;
                        _irlBusy = true;
                        bool want = !_svc.IrlVisible;
                        Run(ct => _svc.SetIrlVisible(want), () => { _irlBusy = false; Render(); }, e => { _irlBusy = false; _irlStatus = e.Message; Render(); }, true);
                    }, "evt-irl-toggle" + (_svc.IrlVisible ? " evt-on" : ""));
                    toggle.name = "events-irl-visible";
                    card.Add(toggle);
                    card.Add(El.Text(EventText.T("irl.visibleNote"), "evt-irl-note"));
                }
                if (_irlStatus != null) card.Add(El.Text(_irlStatus, "evt-irl-error"));
                card.Add(El.Text(EventText.T("irl.privacy"), "evt-irl-privacy"));
                Body.Add(card);
                // the board
                _ = _svc.LoadIrlBoard();
                Body.Add(El.Text(EventText.T("irl.board"), "evt-section"));
                Body.Add(El.Text(EventText.T("irl.boardNote"), "evt-section-note"));
                if (_svc.IrlBoard.Count == 0) { Body.Add(El.Text(EventText.T("irl.boardEmpty"), "evt-section-note")); return; }
                var list = El.Div("evt-cal");
                foreach (var a in _svc.IrlBoard)
                {
                    var alias = a.Alias;
                    var av = Duo.Avatar(alias, alias, "duo-avatar-m");
                    var main = El.Div("evt-cal-main", El.Text(alias, "evt-cal-title"),
                        El.Text((a.TribeId != null ? Strings.ReadableId(a.TribeId) + " · " : "") + "IRL · " + EventWire.Short(_svc.NowMs - a.CheckedInAtMs).Replace("now", "just now") + " ago", "evt-cal-sub"));
                    VisualElement hi;
                    if (a.Self) hi = El.Text(EventText.T("irl.you"), "evt-irl-you");
                    else if (a.Handle != null)
                    {
                        // PR #18: a hello by handle, delivered as a DM (lands in their requests if you're strangers)
                        var handle = a.Handle;
                        bool sent = _hiSent.Contains(handle), busy = _hiBusy.Contains(handle);
                        var b = El.Btn(sent ? EventText.T("irl.hiSent") : EventText.T("irl.sayHi"), () => SayHi(handle, alias), "evt-irl-hi" + (sent || busy ? " evt-off" : ""));
                        b.name = "events-irl-hi-" + handle;
                        b.SetEnabled(!sent && !busy && _svc.IrlCheckedIn);
                        hi = b;
                    }
                    else
                    {
                        var near = FindNearby(alias);
                        hi = El.Btn(EventText.T("irl.message"), () =>
                        {
                            var v = FindNearby(alias);
                            if (v == null) { Bus.Publish(ToastSignal.Text(EventText.T("irl.notNearby"))); return; }
                            // their profile card: Message (a stranger's DM lands in requests), Block, Report
                            Bus.Publish(new AvatarTappedSignal { userId = v.userId, displayName = v.displayName });
                        }, "evt-irl-hi" + (near == null ? " evt-off" : ""));
                    }
                    list.Add(El.Div("evt-cal-row", El.Div("evt-irl-av", av), main, hi));
                }
                Body.Add(list);
            }

            private readonly System.Collections.Generic.HashSet<string> _hiSent = new System.Collections.Generic.HashSet<string>(), _hiBusy = new System.Collections.Generic.HashSet<string>();

            private void SayHi(string handle, string alias)
            {
                if (_hiSent.Contains(handle) || !_hiBusy.Add(handle)) return;
                Render();
                Run(ct => _svc.SayHi(handle, EventText.T("irl.sayHiText")), () =>
                {
                    _hiBusy.Remove(handle); _hiSent.Add(handle);
                    Bus.Publish(new SfxSignal("sfx_ui_success"));
                    Bus.Publish(ToastSignal.Text(EventText.F("irl.sent", alias)));
                    Render();
                }, e =>
                {
                    _hiBusy.Remove(handle);
                    Bus.Publish(ToastSignal.Text(EventsService.ExplainDm(e), ToastKind.Warning));
                    Render();
                }, true);
            }

            private static CTLife.World.AvatarView FindNearby(string alias)
            {
                foreach (var v in CTLife.World.AvatarView.All)
                    if (v != null && !v.isLocal && !v.isNpc && !string.IsNullOrEmpty(v.userId) && v.displayName == alias) return v;
                return null;
            }

            private async void CheckIn()
            {
                if (_irlBusy) return;
                _irlBusy = true; _irlStatus = null;
                Render();
                string err = null;
                try
                {
                    var (fix, locErr) = await IrlLocation.Once(Life);
                    if (!Alive) return;
                    if (fix == null) err = EventsService.ExplainCheckin(null, locErr);
                    else
                    {
                        var r = await _svc.CheckIn(fix.Value);
                        if (r.FirstCheckin && EventsOverlaysHost != null) _ = EventsOverlaysHost.ShowIrlClaim(r);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e) { err = EventsService.ExplainCheckin(e); Log.Warn("events", "IRL check-in refused: " + (e is CTLife.Net.ApiException a ? a.Code + " " + EventsService.Reason(a) : e.GetType().Name)); }
                if (!Alive) return;
                _irlBusy = false; _irlStatus = err;
                if (err != null) Bus.Publish(new SfxSignal("sfx_ui_error"));
                Render();
            }

            private static EventsOverlays EventsOverlaysHost => BreakpointVenue.Current != null ? BreakpointVenue.Current.Ui : null;

            // ------------------------------------------------------------ the quest chain

            private void Quest(EventDetails d)
            {
                var q = d.Quest;
                bool live = d.Event.IsLive(_svc.NowMs);
                Body.Add(El.Text(EventText.T("quest"), "evt-section"));
                Body.Add(El.Text(live ? EventText.T("quest.note") : EventText.T("quest.notLive"), "evt-section-note"));
                var current = q.Current;
                for (int i = 0; i < q.Steps.Count; i++)
                {
                    var s = q.Steps[i];
                    bool unlocked = q.Unlocked(s.Step);
                    var row = El.Div("evt-step");
                    row.EnableInClassList("evt-step-done", s.Complete);
                    row.EnableInClassList("evt-step-current", live && s == current);
                    row.EnableInClassList("evt-step-locked", !s.Complete && (!unlocked || !live));
                    var num = El.Text(s.Complete ? "✓" : (i + 1).ToString(CultureInfo.InvariantCulture), "evt-step-num");
                    var fill = El.Div("evt-step-fill");
                    fill.style.width = Length.Percent((float)s.Fraction * 100f);
                    var main = El.Div("evt-step-main",
                        El.Text(s.Title, "evt-step-title"),
                        El.Text(!unlocked && !s.Complete ? EventText.T("quest.locked") : EventText.T("step." + s.Step + ".how"), "evt-step-how"),
                        El.Div("evt-step-meter", El.Div("evt-step-track", fill), El.Text(Math.Min(s.Done, s.Required) + " / " + s.Required, "evt-step-count")));
                    if (s.IsTrivia && live && unlocked && !s.Complete)
                    {
                        var b = El.Btn(EventText.T("kiosk"), () => { _trivia = true; Render(); }, "evt-step-btn");
                        b.name = "events-trivia";
                        main.Add(b);
                    }
                    row.Add(num); row.Add(main);
                    Body.Add(row);
                }
                // the reward
                var art = El.Div("evt-reward-art");
                var tex = Resources.Load<Texture2D>("UI/Events/lanyard");
                if (tex != null) art.style.backgroundImage = new StyleBackground(tex);
                var info = El.Div("evt-step-main",
                    El.Text(EventText.T("reward.lanyard"), "evt-reward-title"),
                    El.Text("+" + Money.Cash(q.RewardCents) + " Cash · " + EventText.T("reward.lanyard").ToLowerInvariant(), "evt-reward-sub"));
                if (q.Completed || q.ClaimedAtMs != null)
                {
                    info.Add(El.Text(EventText.T("reward.claimed").ToUpperInvariant() + " ✓", "evt-reward-tag"));
                    var wear = El.Btn(EventText.T("claim.wear"), null, "evt-step-btn");
                    wear.name = "events-wear";
                    wear.clicked += async () =>
                    {
                        wear.SetEnabled(false);
                        bool ok = await EventsOverlays.WearLanyard(q.RewardItemKey ?? EventIds.Lanyard);
                        if (!Disposed) wear.text = ok ? EventText.T("claim.worn") : EventText.T("claim.inWardrobe");
                    };
                    info.Add(wear);
                }
                Body.Add(El.Div("evt-reward", art, info));
            }

            // ------------------------------------------------------------ trivia

            private void RenderTrivia()
            {
                var q = _svc.Breakpoint?.Quest;
                var st = q?.Find(EventIds.StepTrivia);
                if (st == null) { Body.Add(Duo.Empty(EventText.T("notLive"))); return; }
                Body.Add(El.Text(EventText.T("trivia.note"), "evt-section-note"));
                var head = El.Div("evt-step" + (st.Complete ? " evt-step-done" : " evt-step-current"));
                var fill = El.Div("evt-step-fill");
                fill.style.width = Length.Percent((float)st.Fraction * 100f);
                head.Add(El.Text(st.Complete ? "✓" : "?", "evt-step-num"));
                head.Add(El.Div("evt-step-main", El.Text(st.Title, "evt-step-title"),
                    El.Div("evt-step-meter", El.Div("evt-step-track", fill), El.Text(Math.Min(st.Done, st.Required) + " / " + st.Required, "evt-step-count"))));
                Body.Add(head);
                int n = 0;
                long now = _svc.NowMs;
                _cooldowns.Clear();
                foreach (var tq in st.Questions)
                {
                    n++;
                    bool right = tq.Answered;
                    bool cooling = tq.CoolingDown(now);
                    var box = El.Div("evt-q");
                    box.EnableInClassList("evt-q-done", right && !_picked.ContainsKey(tq.Id));
                    box.Add(El.Text("QUESTION " + n + " OF " + st.Questions.Count, "evt-q-num"));
                    box.Add(El.Text(tq.Prompt, "evt-q-prompt"));
                    foreach (var c in tq.Choices)
                    {
                        var qid = tq.Id; var cid = c.Id;
                        var b = new Button(() => Answer(qid, cid)) { text = "" };
                        b.AddToClassList("evt-choice");
                        b.name = "events-q-" + qid + "-" + cid;
                        b.Add(El.Text(cid.ToUpperInvariant(), "evt-choice-letter"));
                        b.Add(El.Text(c.Label, "evt-choice-label"));
                        bool tried = tq.Tried.Contains(cid);
                        if (tried) b.AddToClassList("evt-choice-wrong");
                        else if (right && _picked.TryGetValue(qid, out var p) && p == cid) b.AddToClassList("evt-choice-right");
                        // a tried choice stays shut; the question waits out its cooldown; other questions stay open
                        b.SetEnabled(!right && !tried && !cooling && !_busy && !st.Complete);
                        box.Add(b);
                    }
                    if (right) box.Add(El.Text(_picked.ContainsKey(tq.Id) ? "✓ " + EventText.T("trivia.right") : EventText.T("trivia.answered"), "evt-q-result"));
                    else if (cooling)
                    {
                        var cd = El.Text("", "evt-q-result evt-q-cooldown");
                        _cooldowns.Add((cd, tq.RetryAtMs));
                        box.Add(cd);
                    }
                    else if (tq.Tried.Count > 0) box.Add(El.Text("× " + EventText.T("trivia.wrong") + " · " + EventText.T("trivia.tried") + " " + string.Join(", ", tq.Tried).ToUpperInvariant(), "evt-q-result"));
                    if (tq.Id == _focusQ) { var target = box; Root.schedule.Execute(() => { if (!Disposed && target.panel != null && Root.contentContainer.Contains(target)) Root.ScrollTo(target); }).ExecuteLater(60); }
                    Body.Add(box);
                }
                TickCooldowns();
            }

            private readonly System.Collections.Generic.List<(Label label, long at)> _cooldowns = new System.Collections.Generic.List<(Label, long)>();

            /// <summary>The cooldown lines count down each second; when one runs out its question opens again.</summary>
            private void TickCooldowns()
            {
                long now = _svc.NowMs;
                bool expired = false;
                foreach (var (label, at) in _cooldowns)
                {
                    long left = at - now;
                    if (left <= 0) { expired = true; continue; }
                    label.text = "× " + EventText.T("trivia.wrong") + " · " + EventText.F("trivia.cooldown", EventWire.Clock(left));
                }
                if (expired && _trivia) Render();
            }

            private void Answer(string qid, string choice)
            {
                if (_busy) return;
                // a tried choice or a cooling question never goes to the server (the buttons are disabled too)
                var tq = _svc.Breakpoint?.Quest?.Find(EventIds.StepTrivia)?.Questions?.Find(x => x.Id == qid);
                if (tq != null && (tq.Answered || tq.Tried.Contains(choice) || tq.CoolingDown(_svc.NowMs))) { Bus.Publish(new SfxSignal("sfx_ui_error")); return; }
                _busy = true;
                _focusQ = qid;
                int before = _svc.Breakpoint?.Quest?.Find(EventIds.StepTrivia)?.Done ?? 0;
                Render();
                Run(ct => _svc.Progress(EventIds.StepTrivia, qid, choice), r =>
                {
                    _busy = false;
                    int after = r.Quest.Find(EventIds.StepTrivia)?.Done ?? before;
                    _picked[qid] = choice; _right[qid] = after > before;
                    Bus.Publish(new SfxSignal(after > before ? "sfx_ui_success" : "sfx_ui_error"));
                    Render();
                }, err =>
                {
                    _busy = false;
                    // wrong / already tried / cooling down: the service has marked the question; the screen shows it
                    if (err.Code == "EVENT_STEP_INVALID" || err.Code == "TRIVIA_COOLDOWN") Bus.Publish(new SfxSignal("sfx_ui_error"));
                    else Bus.Publish(ToastSignal.Text(err.Message, ToastKind.Warning));
                    Render();
                }, true);
            }

            // ------------------------------------------------------------ the calendar

            private void Calendar()
            {
                var rows = _svc.Live.Concat(_svc.Upcoming).GroupBy(e => e.Slug).Select(g => g.First()).OrderBy(e => e.StartsAtMs).ToList();
                Body.Add(El.Text(EventText.T("calendar"), "evt-section"));
                if (rows.Count == 0) { Body.Add(El.Text(EventText.T("row.none"), "evt-section-note")); return; }
                var list = El.Div("evt-cal");
                long now = _svc.NowMs;
                foreach (var e in rows)
                {
                    var start = DateTimeOffset.FromUnixTimeMilliseconds(e.StartsAtMs).UtcDateTime;
                    var date = El.Div("evt-cal-date", El.Text(start.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant(), "evt-cal-mon"),
                        El.Text(start.Day.ToString(CultureInfo.InvariantCulture), "evt-cal-day"));
                    var main = El.Div("evt-cal-main", El.Text(e.Title, "evt-cal-title"), El.Text(EventWire.Dates(e) + " · " + (e.City ?? ""), "evt-cal-sub"));
                    bool live = e.IsLive(now);
                    var when = El.Text(live ? EventText.T("live") : EventWire.Short(e.StartsAtMs - now), "evt-cal-in");
                    if (!live) _rowClocks.Add((when, e.StartsAtMs));
                    list.Add(El.Div("evt-cal-row", date, main, when));
                }
                Body.Add(list);
                if (_svc.Breakpoint?.Event?.SourceUrl is string src)
                {
                    var b = El.Btn(EventText.T("source") + " ↗", () => Application.OpenURL(src), "duo-link");
                    Body.Add(El.Div("evt-section-note", b));
                }
            }
        }
    }
}
