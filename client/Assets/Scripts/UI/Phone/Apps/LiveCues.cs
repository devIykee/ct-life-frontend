using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Fictional practice console. Its controls operate only this local timing puzzle.</summary>
    public static class LiveCues
    {
        private static VisualElement _active;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        private static Action<bool> _reviewFocus, _reviewPause;
        private static Func<double> _reviewElapsed;
        public static double ReviewElapsed => _reviewElapsed?.Invoke() ?? -1;
        public static Task<LiveCuesResult> PlayReview(CancellationToken cancellation, int seed) => PlayCore(cancellation, seed, false, true);
        public static void ReviewFocus(bool focused) => _reviewFocus?.Invoke(focused);
        public static void ReviewPause(bool paused) => _reviewPause?.Invoke(paused);
#endif
        public static Task<LiveCuesResult> Play(CancellationToken cancellation = default) => PlayCore(cancellation, Environment.TickCount, true, false);
        /// <summary>Deterministic entry point for the Careers lane (same seed → same round).</summary>
        public static Task<LiveCuesResult> PlaySeeded(int seed, CancellationToken cancellation = default) => PlayCore(cancellation, seed, true, false);
        private static Task<LiveCuesResult> PlayCore(CancellationToken cancellation, int seed, bool observeFocus, bool review)
        {
            cancellation.ThrowIfCancellationRequested(); var ui = UiRoot.Instance;
            if (ui == null) throw new InvalidOperationException("Live Cues requires the game UI.");
            if (_active != null) throw new InvalidOperationException("A Live Cues show is already running.");
            var sheet = Resources.Load<StyleSheet>("UI/live-cues");
            if (sheet == null) throw new InvalidOperationException("Live Cues stylesheet is missing.");
            var round = new LiveCuesRound(Time.realtimeSinceStartupAsDouble, seed);
            var completion = new TaskCompletionSource<LiveCuesResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool closed = false; int renderedCue = -1, lastHandled = 0; double feedbackUntil = 0, pressedUntil = 0; int pressedAction = -1;
            VisualElement root = null, paused = null; Action tick = null; Button pause = null;
            var pads = new Button[3]; var lamps = new Label[3];
            Label Copy(string key, string classes) => ModDesk.Plain(LiveCuesStrings.T(key), classes);
            var timer = ModDesk.Plain("", "ct-desk-timer"); var score = ModDesk.Plain("", "ct-cues-score");
            var cueCount = ModDesk.Plain("", "ct-cues-cue-count"); var cueAction = ModDesk.Plain("", "ct-cues-action");
            var cueCopy = ModDesk.Plain("", "ct-cues-script"); var standby = Copy("standby", "ct-cues-status");
            var nextAction = ModDesk.Plain("", "ct-cues-next-action"); var nextCopy = ModDesk.Plain("", "ct-cues-next-copy");
            var countdown = ModDesk.Plain("", "ct-cues-countdown"); var feedback = Copy("ready", "ct-cues-feedback");
            var tally = ModDesk.Plain("", "ct-cues-tally"); var combo = ModDesk.Plain("", "ct-cues-tally");
            var onAir = Copy("onAir", "ct-cues-air-copy"); onAir.name = "cues-air-copy";
            var airLamp = El.Div("ct-cues-air-lamp"); airLamp.name = "cues-air-lamp";
            var air = El.Div("ct-cues-air", airLamp, onAir); air.name = "cues-air";
            var progress = El.Div("ct-desk-progress-fill");
            var console = El.Div("ct-cues-pads"); console.name = "cues-console";
            var needle = El.Div("ct-cues-needle", El.Div("ct-cues-needle-head")); needle.name = "cues-needle";
            var window = El.Div("ct-cues-window"); window.name = "cues-window";
            var track = El.Div("ct-cues-track", El.Div("ct-cues-track-line"), window, needle); track.name = "cues-track";
            track.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < 11; i++) { var mark = El.Div("ct-cues-tick"); mark.style.left = Length.Percent(i * 10); track.Insert(0, mark); }
            var stage = El.Div("ct-cues-stage",
                El.Div("ct-cues-current", El.Div("ct-cues-stage-meta", cueCount, standby), cueAction, cueCopy),
                El.Div("ct-cues-next", nextAction, nextCopy));
            stage.name = "cues-stage";
            double Now() => Time.realtimeSinceStartupAsDouble;
            void Close(bool cancel, bool detach = true)
            {
                if (closed) return; closed = true;
                if (observeFocus) { Application.focusChanged -= Focus; Runner.ApplicationPaused -= ApplicationPause; }
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                if (ReferenceEquals(_active, root)) { _reviewFocus = null; _reviewPause = null; _reviewElapsed = null; }
#endif
                ui.RemoveUpdater(tick); if (ReferenceEquals(_active, root)) _active = null;
                if (cancel) round.Cancel(); if (detach) root.RemoveFromHierarchy();
                if (cancel) completion.TrySetCanceled(); else completion.TrySetResult(round.Result);
            }
            void Pause() { if (!closed && !cancellation.IsCancellationRequested && round.Pause(Now())) Render(); }
            void Resume() { if (!closed && !cancellation.IsCancellationRequested && round.Resume(Now())) Render(); }
            void Focus(bool focused) { if (!focused) Pause(); }
            void ApplicationPause(bool value) { if (value) Pause(); }
            void BuildPads()
            {
                renderedCue = round.Index; int expected = renderedCue; console.Clear();
                for (int i = 0; i < 3; i++)
                {
                    var action = (LiveCueAction)i;
                    var pad = El.Btn("", () => {
                        if (closed || cancellation.IsCancellationRequested || !round.Press(expected, action, Now())) return;
                        pressedAction = (int)action; pressedUntil = round.Elapsed + .16;
                        Render();
                    }, "ct-btn ct-cues-pad", sfx: null);
                    pad.AddToClassList("ct-cues-pad-" + action.ToString().ToLowerInvariant());
                    if (i == 2) pad.AddToClassList("ct-cues-pad-last");
                    pad.name = "cues-pad-" + i; pad.tooltip = LiveCuesStrings.Action(action);
                    lamps[i] = Copy("padReady", "ct-cues-pad-status");
                    var light = El.Div("ct-cues-pad-light");
                    // Two existing MIT Phosphor symbols; applause uses a static sound-burst glyph, not an audio meter.
                    VisualElement glyph = i < 2 ? new UiIcon(i == 0 ? "calls" : "chat").With("ct-cues-pad-symbol")
                        : El.Div("ct-cues-pad-symbol ct-cues-sound-burst", El.Div("ct-cues-sound-bar ct-cues-sound-short"),
                            El.Div("ct-cues-sound-bar"), El.Div("ct-cues-sound-bar ct-cues-sound-short"));
                    pad.Add(El.PassThrough(El.Div("ct-cues-pad-face",
                        El.Div("ct-cues-pad-title-row", glyph, ModDesk.Plain(LiveCuesStrings.Action(action), "ct-cues-pad-title"), light),
                        El.Div("ct-cues-pad-lower", ModDesk.Plain((i + 1).ToString("00"), "ct-cues-pad-number"), lamps[i]))));
                    pads[i] = pad; console.Add(pad);
                }
            }
            pause = El.Btn(LiveCuesStrings.T("pause"), Pause, "ct-btn ct-cues-control", sfx: null); pause.name = "cues-pause";
            var end = El.Btn(LiveCuesStrings.T("end"), () => Close(true), "ct-btn ct-cues-control ct-cues-end", sfx: null); end.name = "cues-end";
            var resume = El.Btn(LiveCuesStrings.T("resume"), Resume, "ct-btn ct-cues-control ct-cues-resume", sfx: null); resume.name = "cues-resume";
            var pauseEnd = El.Btn(LiveCuesStrings.T("end"), () => Close(true), "ct-btn ct-cues-control ct-cues-end", sfx: null); pauseEnd.name = "cues-paused-end";
            paused = El.Div("ct-cues-paused", El.Div("ct-cues-pause-panel", Copy("paused", "ct-desk-title"), Copy("pauseDetail", "ct-cues-pause-copy"), El.Div("ct-cues-controls", resume, pauseEnd))); paused.name = "cues-paused";
            root = El.Div("ct-desk-overlay ct-cues-overlay", El.Div("ct-desk-panel ct-cues-panel",
                El.Div("ct-desk-header", El.Div("ct-cues-brand", Copy("title", "ct-cues-title"), air), Copy("brief", "ct-cues-brief"), score, timer),
                El.Div("ct-desk-progress", progress), stage,
                El.Div("ct-cues-timing", El.Div("ct-cues-rail-labels", Copy("railStart", "ct-cues-rail-copy"), Copy("windowLabel", "ct-cues-window-copy"), countdown), track), console,
                El.Div("ct-cues-feedback-row", feedback, tally, combo),
                El.Div("ct-cues-controls", Copy(review ? "review" : "practice", "ct-cues-footnote"), pause, end), paused));
            root.name = "live-cues-game"; ModDesk.ApplySheet(root); root.styleSheets.Add(sheet);
            void Render()
            {
                if (renderedCue != round.Index) BuildPads();
                var cue = round.Current; bool go = round.InWindow;
                timer.text = LiveCuesStrings.F("timer", (int)Math.Ceiling(round.Remaining)); score.text = LiveCuesStrings.F("score", round.Score);
                tally.text = LiveCuesStrings.F("tally", round.Hits); combo.text = LiveCuesStrings.F("combo", round.Combo);
                progress.style.width = Length.Percent((float)(round.Remaining * 100 / LiveCuesRound.Duration));
                cueCount.text = LiveCuesStrings.F("cue", Math.Min(20, round.Index + 1));
                cueAction.text = cue == null ? LiveCuesStrings.T("rolling") : LiveCuesStrings.Action(cue.Action);
                cueCopy.text = cue == null ? LiveCuesStrings.T("wrapped") : LiveCuesStrings.Cue(cue);
                standby.text = LiveCuesStrings.T(cue == null ? "rolling" : go ? "go" : "standby");
                onAir.text = LiveCuesStrings.T(round.Paused ? "onHold" : "onAir");
                air.EnableInClassList("ct-cues-air-held", round.Paused);
                stage.EnableInClassList("ct-cues-go", go); window.EnableInClassList("ct-cues-go", go);
                if (cue != null && cue.Index + 1 < LiveCuesRound.CueCount)
                { var next = round.Cues[cue.Index + 1]; nextAction.text = LiveCuesStrings.F("nextAction", LiveCuesStrings.Action(next.Action)); nextCopy.text = LiveCuesStrings.Cue(next); }
                else { nextAction.text = LiveCuesStrings.T("rolling"); nextCopy.text = LiveCuesStrings.T("wrapped"); }
                countdown.text = cue == null ? "" : LiveCuesStrings.F(go ? "window" : "in", Math.Max(0, (go ? cue.Closes : cue.Opens) - round.Elapsed));
                needle.Show(cue != null && round.CanPress);
                if (cue != null)
                {
                    double fraction = Math.Max(0, Math.Min(1, (round.Elapsed - (cue.Target - LiveCuesRound.ApproachSeconds)) / (LiveCuesRound.ApproachSeconds * 2)));
                    needle.style.translate = new Translate((float)(track.contentRect.width * fraction), 0, 0);
                }
                paused.Show(round.Paused); pause.SetEnabled(!round.Paused);
                for (int i = 0; i < 3; i++)
                {
                    pads[i].SetEnabled(round.CanPress);
                    pads[i].EnableInClassList("ct-cues-pad-go", go);
                    pads[i].EnableInClassList("ct-cues-pad-pressed", i == pressedAction && round.Elapsed < pressedUntil);
                    lamps[i].text = LiveCuesStrings.T(go ? "padGo" : round.CanPress ? "padReady" : "padWait");
                }
                if (lastHandled != round.Index)
                {
                    lastHandled = round.Index; var last = round.Last; string outcome = last.Outcome.ToString().ToLowerInvariant();
                    feedback.text = last.Outcome == LiveCueOutcome.Wrong || last.Outcome == LiveCueOutcome.Missed
                        ? LiveCuesStrings.F(outcome, LiveCuesStrings.Action(last.Action)) : LiveCuesStrings.T(outcome);
                    feedback.EnableInClassList("ct-cues-mistake", last.Outcome != LiveCueOutcome.Hit);
                    feedbackUntil = round.Elapsed + 1.2;
                    Bus.Publish(new SfxSignal(last.Outcome == LiveCueOutcome.Hit ? "sfx_ui_success" : "sfx_ui_error"));
                }
                else if (round.Elapsed >= feedbackUntil)
                { feedback.text = LiveCuesStrings.T(cue == null ? "wrapped" : "ready"); feedback.EnableInClassList("ct-cues-mistake", false); }
            }
            tick = () => {
                if (closed) return; if (cancellation.IsCancellationRequested) { Close(true); return; }
                round.Step(Now()); Render(); if (round.Finished) Close(false);
            };
            root.RegisterCallback<DetachFromPanelEvent>(e => { if (ReferenceEquals(e.target, root)) Close(true, false); });
            _active = root;
            try
            {
                ui.ModalLayer.Add(root); ui.AddUpdater(tick);
                if (observeFocus) { _ = Runner.Instance; Application.focusChanged += Focus; Runner.ApplicationPaused += ApplicationPause; if (!Application.isFocused) Pause(); }
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                if (review) { _reviewFocus = Focus; _reviewPause = ApplicationPause; _reviewElapsed = () => round.Elapsed; }
#endif
                Render(); tick();
            }
            catch { Close(true); throw; }
            return completion.Task;
        }
    }
}
