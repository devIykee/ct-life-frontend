using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Careers
{
    /// <summary>What to play: the resolved game (caps from the registry or FE defaults), its seed and the mode.</summary>
    public sealed class MiniGamePlay
    {
        public ResolvedGame Game;
        public int Seed;
        public bool Practice;
        /// <summary>Shown in the header, e.g. "Game 1 of 2".</summary>
        public string Context;
        /// <summary>Text for the abandon button (a shift abandons the whole shift).</summary>
        public string AbandonLabel;
    }

    public readonly struct MiniGameOutcome
    {
        public readonly bool Finished;
        public readonly int Score, MaxScore;
        public readonly string EndReason;
        public MiniGameOutcome(bool finished, int score, int maxScore, string endReason) { Finished = finished; Score = score; MaxScore = maxScore; EndReason = endReason; }
        public static MiniGameOutcome Abandoned(int maxScore) => new MiniGameOutcome(false, 0, maxScore, "abandoned");
    }

    /// <summary>
    /// Full-screen host for one mini-game: tutorial card → play (with timer, score, pause) → results. Drives a
    /// MiniGameSession from UiRoot's per-frame updater. Losing app focus pauses (and hides the board); a frame gap
    /// over 1.5 s is treated the same way. Cancellation (sign-out, closed UI) tears down without a score.
    /// The nine Phase 1 games in CTLife.UI keep their own hosts and are played through LegacyGames.
    /// </summary>
    public static class MiniGameRunner
    {
        private static VisualElement _active;

        public static bool Busy => _active != null;

        public static Task<MiniGameOutcome> Run(MiniGamePlay play, CancellationToken ct)
        {
            if (play?.Game == null) throw new ArgumentNullException(nameof(play));
            ct.ThrowIfCancellationRequested();
            var spec = play.Game.Spec;
            if (spec == null || spec.Engine == MiniGameEngine.Legacy) return LegacyGames.Play(play, ct);
            var ui = UiRoot.Instance;
            if (ui == null) throw new InvalidOperationException("The mini-game host needs the game UI.");
            if (_active != null) throw new InvalidOperationException("A mini-game is already running.");
            var round = spec.Build(new RoundInit(play.Seed, play.Game.MaxScore, play.Game.Duration));
            return new Host(ui, play, spec, round, ct).Task;
        }

        private sealed class Host
        {
            private readonly TaskCompletionSource<MiniGameOutcome> _done = new TaskCompletionSource<MiniGameOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly UiRoot _ui;
            private readonly MiniGamePlay _play;
            private readonly MiniGameSpec _spec;
            private readonly MiniGameSession _session;
            private readonly CancellationToken _ct;
            private readonly VisualElement _root, _progressFill;
            private readonly ScrollView _body;
            private readonly Label _timer, _score, _context;
            private readonly Button _pause;
            private readonly Action _tick;
            private readonly CancellationTokenRegistration _ctReg;
            private IEngineView _view;
            private MiniGamePhase _shown = (MiniGamePhase)(-1);
            private bool _closed;
            private int _shownSeconds = -1, _shownScore = -1, _shownProgress = -1;
            private bool? _shownUrgent;

            public Task<MiniGameOutcome> Task => _done.Task;

            public Host(UiRoot ui, MiniGamePlay play, MiniGameSpec spec, IMiniGameRound round, CancellationToken ct)
            {
                _ui = ui;
                _play = play;
                _spec = spec;
                _ct = ct;
                _session = new MiniGameSession(round);
                _timer = CUi.L("", "ctc-timer");
                _score = CUi.L("", "ctc-score");
                _context = CUi.L(play.Context ?? "", "ctc-context");
                _pause = CUi.B(CareersStrings.T("mg.pause"), () => _session.Pause(CUi.Now, PauseReason.Player), "ctc-btn ctc-btn-small", null);
                _progressFill = CUi.Div("ctc-progress-fill");
                _body = El.VerticalScroll("ctc-body");
                var header = CUi.Div("ctc-header",
                    CUi.Div("ctc-header-copy", CUi.L(CareersStrings.T(play.Practice ? "mg.practice" : "mg.shift"), "ctc-eyebrow"), CUi.L(spec.Title, "ctc-title"), _context),
                    CUi.Div("ctc-header-stats", _timer, _score, _pause));
                _root = CUi.Div("ctc-overlay", CUi.Div("ctc-panel", header, CUi.Div("ctc-progress", _progressFill), _body));
                _root.name = "careers-minigame";
                _root.pickingMode = PickingMode.Position;
                CUi.Sheet(_root);
                ModDesk.DuoGameChrome(_root);
                _tick = Tick;
                _root.RegisterCallback<DetachFromPanelEvent>(e => { if (ReferenceEquals(e.target, _root)) Close(cancelled: true, detach: false); });
                _ctReg = ct.Register(() => Close(cancelled: true));
                _active = _root;
                try
                {
                    _ui.ModalLayer.Add(_root);
                    _ui.AddUpdater(_tick);
                    Application.focusChanged += OnFocus;
                    Render();
                }
                catch
                {
                    Close(cancelled: true);
                    throw;
                }
            }

            private void OnFocus(bool focused)
            {
                if (!focused) _session.Pause(CUi.Now, PauseReason.Interruption);
            }

            private void Tick()
            {
                if (_closed) return;
                if (_ct.IsCancellationRequested) { Close(cancelled: true); return; }
                try
                {
                    _session.Tick(CUi.Now);
                    Render();
                }
                catch (Exception e)
                {
                    Log.Exception("careers-minigame", e);
                    Close(cancelled: true);
                }
            }

            private void Render()
            {
                if (_closed) return;
                var phase = _session.Phase;
                if (phase != _shown) { Rebuild(phase); CUi.Show(_pause, phase == MiniGamePhase.Playing); }
                // Per-frame: only touch what changed, so a 45 s round doesn't allocate strings every frame.
                var clock = _session.Clock;
                int seconds = (int)Math.Ceiling(clock.Remaining);
                if (seconds != _shownSeconds) { _shownSeconds = seconds; _timer.text = CareersStrings.F("mg.time", seconds); }
                bool urgent = clock.Remaining <= 10 && phase == MiniGamePhase.Playing;
                if (_shownUrgent != urgent) { _shownUrgent = urgent; CUi.Mark(_timer, "ctc-urgent", urgent); }
                int score = phase == MiniGamePhase.Results ? _session.FinalScore : _session.LiveScore;
                if (score != _shownScore) { _shownScore = score; _score.text = CareersStrings.F("mg.score", score); }
                int progress = (int)Math.Round(400 * clock.Remaining / clock.Duration);
                if (progress != _shownProgress) { _shownProgress = progress; _progressFill.style.width = Length.Percent(progress / 4f); }
                if (phase == MiniGamePhase.Playing) _view?.Render();
            }

            private void Rebuild(MiniGamePhase phase)
            {
                _shown = phase;
                _body.Clear();
                switch (phase)
                {
                    case MiniGamePhase.Briefing: _body.Add(Briefing()); break;
                    case MiniGamePhase.Playing:
                        if (_view == null) _view = EngineViews.Create(_spec, _session.Round, () => _session.CanInteract, AfterInput);
                        _body.Add(_view.Root);
                        _view.Render();
                        break;
                    case MiniGamePhase.Paused: _body.Add(Paused()); break;
                    case MiniGamePhase.Results:
                        CUi.Sfx(Scoring.GradeKey(_session.FinalScore, _session.Round.MaxScore) == "grade.rough" ? "sfx_ui_error" : "sfx_ui_success");
                        _body.Add(Results());
                        break;
                    case MiniGamePhase.Cancelled: Close(cancelled: true); break;
                }
            }

            private void AfterInput()
            {
                _session.AfterInput();
                Render();
            }

            private VisualElement Briefing()
            {
                var rules = CUi.Div("ctc-rules");
                foreach (var r in _spec.Rules) rules.Add(CUi.L("• " + r, "ctc-rule"));
                var card = CUi.Div("ctc-card ctc-brief",
                    CUi.L(_spec.Tagline, "ctc-tagline"),
                    CUi.L(CareersStrings.T("mg.howTo"), "ctc-section"), rules,
                    CUi.L(CareersStrings.T("mg.scoring"), "ctc-section"), CUi.L(_spec.ScoringRule, "ctc-rule"),
                    CUi.L(CareersStrings.F("mg.limits", (int)Math.Round(_play.Game.Duration), _play.Game.MaxScore), "ctc-muted"));
                if (!string.IsNullOrEmpty(_spec.RealTask)) card.Add(CUi.L(CareersStrings.F("mg.basedOn", _spec.RealTask), "ctc-muted"));
                var start = CUi.B(CareersStrings.T("mg.start"), () => { if (_session.Begin(CUi.Now)) Render(); }, "ctc-btn ctc-btn-primary");
                start.name = "careers-minigame-start";
                var quit = CUi.B(_play.AbandonLabel ?? CareersStrings.T("mg.quit"), () => Close(cancelled: false, abandoned: true), "ctc-btn");
                return CUi.Div("ctc-scroll-col", card, CUi.Div("ctc-actions", start, quit));
            }

            private VisualElement Paused()
            {
                bool interrupted = _session.LastPauseReason == PauseReason.Interruption;
                double left = Math.Max(0, _session.Clock.MaxPausedSeconds - _session.Clock.PausedSeconds);
                var resume = CUi.B(CareersStrings.T("mg.resume"), () => { if (_session.Resume(CUi.Now)) Render(); }, "ctc-btn ctc-btn-primary");
                resume.name = "careers-minigame-resume";
                var end = CUi.B(CareersStrings.T("mg.endRound"), () => { _session.EndRound(CUi.Now); Render(); }, "ctc-btn");
                var abandon = CUi.B(_play.AbandonLabel ?? CareersStrings.T("mg.abandon"), () => Close(cancelled: false, abandoned: true), "ctc-btn ctc-btn-danger");
                return CUi.Div("ctc-card ctc-paused",
                    CUi.L(CareersStrings.T("mg.paused"), "ctc-h"),
                    CUi.L(CareersStrings.T(interrupted ? "mg.pausedInterrupted" : "mg.pausedPlayer"), "ctc-body-text"),
                    CUi.L(CareersStrings.F("mg.pauseLimit", (int)Math.Floor(left)), "ctc-muted"),
                    CUi.Div("ctc-actions", resume, end, abandon));
            }

            private VisualElement Results()
            {
                var stats = CUi.Div("ctc-stats");
                foreach (var line in _session.Round.Summary)
                    stats.Add(CUi.Div("ctc-stat", CUi.L(CareersStrings.T(line.LabelKey), "ctc-stat-label"), CUi.L(line.Value, "ctc-stat-value")));
                var cont = CUi.B(CareersStrings.T("mg.continue"), () => Close(cancelled: false), "ctc-btn ctc-btn-primary");
                cont.name = "careers-minigame-continue";
                return CUi.Div("ctc-scroll-col",
                    CUi.Div("ctc-card ctc-results",
                        CUi.L(CareersStrings.T("mg.results"), "ctc-eyebrow"),
                        CUi.L(CareersStrings.F("mg.finalScore", _session.FinalScore, _session.Round.MaxScore), "ctc-big"),
                        CUi.L(CareersStrings.T(Scoring.GradeKey(_session.FinalScore, _session.Round.MaxScore)), "ctc-h"),
                        CUi.L(CareersStrings.T("mg.end." + _session.EndReason), "ctc-muted"),
                        stats),
                    CUi.Div("ctc-actions", cont));
            }

            private void Close(bool cancelled, bool detach = true, bool abandoned = false)
            {
                if (_closed) return;
                _closed = true;
                Application.focusChanged -= OnFocus;
                _ui.RemoveUpdater(_tick);
                _ctReg.Dispose();
                if (ReferenceEquals(_active, _root)) _active = null;
                if (detach) _root.RemoveFromHierarchy();
                if (!_session.IsTerminal) _session.Cancel();
                if (cancelled) _done.TrySetCanceled();
                else if (abandoned || _session.Phase != MiniGamePhase.Results) _done.TrySetResult(MiniGameOutcome.Abandoned(_session.Round.MaxScore));
                else _done.TrySetResult(new MiniGameOutcome(true, _session.FinalScore, _session.Round.MaxScore, _session.EndReason));
            }
        }
    }

    /// <summary>
    /// Routes the nine Phase 1 games that live in CTLife.UI. They show their own rules and own overlays. Six of them
    /// (the proposals) always score out of 100; their score is rescaled to the registry cap. They seed themselves from
    /// the tick count (a deterministic entry point is requested in docs/progress/requests.md).
    /// </summary>
    internal static class LegacyGames
    {
        public static async Task<MiniGameOutcome> Play(MiniGamePlay play, CancellationToken ct)
        {
            var g = play.Game;
            var def = new MiniGameDef { id = g.Id, careerId = g.CareerId, name = g.Spec?.Title ?? g.Id, maxScore = g.MaxScore, durationSec = (int)Math.Round(g.Duration) };
            int max = g.MaxScore;
            try
            {
                int score;
                switch (g.Id)
                {
                    case "discord_mod_ban": score = await ModDesk.Play(def, ct, play.Practice); break;
                    case "junior_analyst_earnings": score = await EarningsDesk.Play(def, ct, play.Practice); break;
                    case "kols_engagement": score = await ThreadWorkshop.Play(def, ct, play.Practice); break;
                    case "discord_mod_raid_router": score = Scoring.Scale((await RaidRouter.Play(ct)).Score, RaidRouterRound.Maximum, max); break;
                    case "discord_mod_role_lock": score = Scoring.Scale((await RoleLock.Play(ct)).Score, RoleLockRound.Maximum, max); break;
                    case "junior_analyst_receipt_match": score = Scoring.Scale((await ReceiptMatch.Play(ct)).Score, ReceiptMatchRound.Maximum, max); break;
                    case "junior_analyst_runway_plan": score = Scoring.Scale((await RunwayPlan.Play(ct)).Score, RunwayPlanRound.Maximum, max); break;
                    case "kol_engagement_live_cues": score = Scoring.Scale((await LiveCues.Play(ct)).Score, LiveCuesRound.Maximum, max); break;
                    case "kol_engagement_clip_cut": score = Scoring.Scale((await ClipCut.Play(ct)).Score, ClipCutRound.Maximum, max); break;
                    default:
                        // Registered by the server but unknown to this build: the shared generic host keeps the
                        // contract's fallback rule (play something, never crash).
                        score = await MiniGameHost.Play(def, ct);
                        break;
                }
                return new MiniGameOutcome(true, Scoring.Clamp(score, max), max, "legacy");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The legacy hosts cancel when their overlay is closed: treat as the player quitting.
                return MiniGameOutcome.Abandoned(max);
            }
        }
    }
}
