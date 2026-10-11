using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    public static class EarningsDesk
    {
        public static Task<int> Play(MiniGameDef definition, CancellationToken cancellation = default, bool practice = false)
            => PlayCore(definition, cancellation, practice, Environment.TickCount);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        public static Task<int> PlayPracticeReview(MiniGameDef definition, CancellationToken cancellation, int seed)
            => PlayCore(definition, cancellation, true, seed);
#endif

        /// <summary>Deterministic entry point for the Careers lane (same seed → same round).</summary>

        public static Task<int> PlaySeeded(MiniGameDef definition, int seed, bool practice, CancellationToken cancellation = default) => PlayCore(definition, cancellation, practice, seed);

        private static Task<int> PlayCore(MiniGameDef definition, CancellationToken cancellation, bool practice, int seed)
        {
            cancellation.ThrowIfCancellationRequested();
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var ui = UiRoot.Instance;
            if (ui == null) throw new InvalidOperationException("Earnings desk requires the game UI.");
            var round = new EarningsRound(Time.realtimeSinceStartupAsDouble, definition.durationSec, definition.maxScore, seed);
            var completion = new TaskCompletionSource<int>();
            Label Copy(string key, string classes) => ModDesk.Plain(EarningsStrings.T(key), classes);
            var timer = ModDesk.Plain("", "ct-desk-timer");
            var tally = ModDesk.Plain("", "ct-desk-tally");
            var progress = El.Div("ct-desk-progress-fill");
            var reportNumber = ModDesk.Plain("", "ct-earn-number");
            var company = ModDesk.Plain("", "ct-earn-company");
            var headline = ModDesk.Plain("", "ct-earn-headline");
            var note = ModDesk.Plain("", "ct-earn-note");
            var question = ModDesk.Plain("", "ct-earn-question");
            var figureA = Figure("a");
            var figureB = Figure("b");
            var feedbackTitle = ModDesk.Plain("", "ct-desk-feedback-title");
            var feedbackBody = ModDesk.Plain("", "ct-desk-feedback-body");
            var feedback = El.Div("ct-desk-feedback", feedbackTitle, feedbackBody);
            bool closed = false;
            Button next = null;
            ScrollView evidence = null, decision = null;
            var choices = El.Div("ct-earn-choices");
            for (int i = 0; i < 3; i++)
            {
                int option = i;
                var button = El.Btn("", () => Choose(option), "ct-btn ct-earn-choice", sfx: null);
                button.name = "earnings-answer-" + i;
                choices.Add(button);
            }
            next = El.Btn(EarningsStrings.T("next"), Next, "ct-btn ct-btn-primary ct-desk-next");
            next.name = "earnings-next";
            evidence = El.VerticalScroll("ct-earn-evidence");
            evidence.Add(reportNumber); evidence.Add(company); evidence.Add(headline);
            evidence.Add(figureA); evidence.Add(figureB); evidence.Add(note);
            evidence.Add(Copy("fictional", "ct-earn-fictional"));
            decision = El.VerticalScroll("ct-earn-decision");
            decision.Add(question); decision.Add(choices); decision.Add(feedback); decision.Add(next);
            var body = El.Div("ct-earn-body", evidence, decision);
            var root = El.Div("ct-desk-overlay ct-earn-overlay", El.Div("ct-desk-panel ct-earn-panel",
                El.Div("ct-desk-header", El.Div("ct-desk-heading", Copy("eyebrow", "ct-desk-eyebrow"), Copy("title", "ct-desk-title")), timer),
                El.Div("ct-desk-progress", progress),
                El.Div("ct-desk-meta", Copy(practice ? "practice" : "live", "ct-desk-context"), tally),
                body, Copy("clock", "ct-desk-footnote")));
            root.name = "earnings-desk";
            ModDesk.ApplySheet(root);
            var sheet = Resources.Load<StyleSheet>("UI/earnings-desk");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.RegisterCallback<GeometryChangedEvent>(e => root.EnableInClassList("ct-earn-landscape", e.newRect.width > e.newRect.height));
            Action tick = null;
            void Close(bool cancelled)
            {
                if (closed) return;
                closed = true;
                ui.RemoveUpdater(tick);
                root.RemoveFromHierarchy();
                if (cancelled) completion.TrySetCanceled(); else completion.TrySetResult(round.Score);
            }
            void Render()
            {
                tally.text = EarningsStrings.F("correctCount", round.Correct, EarningsRound.ReportCount);
                choices.Show(!round.IsClear && !round.AwaitingNext);
                feedback.Show(!round.IsClear && round.AwaitingNext);
                next.Show(!round.IsClear && round.AwaitingNext);
                figureA.Show(!round.IsClear); figureB.Show(!round.IsClear); note.Show(!round.IsClear); headline.Show(!round.IsClear);
                if (round.IsClear)
                {
                    reportNumber.text = EarningsStrings.T("clear");
                    company.text = EarningsStrings.F("clearDetail", round.Correct, EarningsRound.ReportCount);
                    question.text = EarningsStrings.T("rules");
                    return;
                }
                var report = round.Current;
                reportNumber.text = EarningsStrings.F("progress", round.Index + 1, EarningsRound.ReportCount);
                company.text = EarningsStrings.T(report.Kind + ".company");
                headline.text = EarningsStrings.Headline(report);
                question.text = EarningsStrings.T(report.Kind + ".question");
                note.text = EarningsStrings.Note(report);
                float maximum = Math.Max(report.A, report.B);
                SetFigure(figureA, EarningsStrings.T(report.Kind + ".a"), report.A, maximum);
                SetFigure(figureB, EarningsStrings.T(report.Kind + ".b"), report.B, maximum);
                for (int i = 0; i < 3; i++) ((Button)choices[i]).text = EarningsStrings.Option(report, report.Options[i]);
                next.text = EarningsStrings.T(round.Index == EarningsRound.ReportCount - 1 ? "finish" : "next");
            }
            void Choose(int option)
            {
                if (closed || cancellation.IsCancellationRequested) return;
                bool? correct = round.Answer(option, Time.realtimeSinceStartupAsDouble);
                if (!correct.HasValue) return;
                feedbackTitle.text = EarningsStrings.T(correct.Value ? "correct" : "incorrect");
                feedbackBody.text = EarningsStrings.Reason(round.Current);
                feedback.EnableInClassList("ct-desk-feedback-correct", correct.Value);
                feedback.EnableInClassList("ct-desk-feedback-incorrect", !correct.Value);
                Bus.Publish(new SfxSignal(correct.Value ? "sfx_ui_success" : "sfx_ui_error"));
                Render();
                decision.scrollOffset = Vector2.zero;
            }
            void Next()
            {
                if (closed || cancellation.IsCancellationRequested) return;
                if (!round.Advance(Time.realtimeSinceStartupAsDouble)) return;
                Render(); evidence.scrollOffset = Vector2.zero; decision.scrollOffset = Vector2.zero;
            }
            tick = () =>
            {
                if (cancellation.IsCancellationRequested) { Close(true); return; }
                double now = Time.realtimeSinceStartupAsDouble;
                timer.text = EarningsStrings.F("timer", (int)Math.Ceiling(round.Remaining(now)));
                progress.style.width = Length.Percent((float)(100 * round.Remaining(now) / definition.durationSec));
                if (round.Expired(now)) Close(false);
            };
            root.RegisterCallback<DetachFromPanelEvent>(e => { if (e.target == root) Close(true); });
            ui.ModalLayer.Add(root); ui.AddUpdater(tick); Render(); tick();
            return completion.Task;
        }

        private static VisualElement Figure(string suffix)
        {
            var root = El.Div("ct-earn-figure",
                El.Div("ct-earn-figure-top", ModDesk.Plain("", "ct-earn-figure-label"), ModDesk.Plain("", "ct-earn-value")),
                El.Div("ct-earn-bar-track", El.Div("ct-earn-bar")));
            root.name = "earnings-figure-" + suffix;
            root.AddToClassList("ct-earn-figure-" + suffix);
            return root;
        }
        private static void SetFigure(VisualElement root, string label, int value, float maximum)
        {
            root.Q<Label>(className: "ct-earn-figure-label").text = label;
            root.Q<Label>(className: "ct-earn-value").text = EarningsStrings.F("kCash", value);
            root.Q<VisualElement>(className: "ct-earn-bar").style.width = Length.Percent(100f * value / maximum);
        }
    }
}
