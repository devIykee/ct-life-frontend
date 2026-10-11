using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Finite local skill round. Only its capped score is sent through the existing shift contract.</summary>
    public sealed class ModDeskRound
    {
        public const int CaseCount = 10;
        private readonly int[] _order = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        private readonly double _deadline;
        private readonly int _maxScore;
        public int Index { get; private set; }
        public int Correct { get; private set; }
        public bool AwaitingNext { get; private set; }
        public bool IsClear => Index >= CaseCount;
        public int CaseId => IsClear ? -1 : _order[Index];
        public int Score => (int)Math.Round((double)Correct * _maxScore / CaseCount, MidpointRounding.AwayFromZero);

        public ModDeskRound(double startedAt, double duration, int maxScore, int seed)
        {
            if (duration <= 0 || duration > 600 || maxScore <= 0) throw new ArgumentOutOfRangeException(nameof(duration));
            _deadline = startedAt + duration;
            _maxScore = maxScore;
            var random = new System.Random(seed);
            for (int i = _order.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
        }

        public double Remaining(double now) => Math.Max(0, _deadline - now);
        public bool Expired(double now) => now >= _deadline;

        /// <returns>Null means a duplicate, expired or otherwise non-actionable click.</returns>
        public bool? Answer(bool ban, double now)
        {
            if (IsClear || AwaitingNext || Expired(now)) return null;
            bool right = ban == (CaseId % 2 == 0);
            if (right) Correct++;
            AwaitingNext = true;
            return right;
        }

        public bool Advance(double now)
        {
            if (!AwaitingNext || Expired(now)) return false;
            Index++;
            AwaitingNext = false;
            return true;
        }
    }

    /// <summary>One readable message at a time. No live chat or real-person content is consumed.</summary>
    public static class ModDesk
    {
        public static Task<int> Play(MiniGameDef definition, CancellationToken cancellation = default, bool practice = false)
            => PlayCore(definition, cancellation, practice, Environment.TickCount);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        /// <summary>Deterministic local practice for native review; never starts or settles a shift.</summary>
        public static Task<int> PlayPracticeReview(MiniGameDef definition, CancellationToken cancellation, int seed)
            => PlayCore(definition, cancellation, true, seed);
#endif

        private static Task<int> PlayCore(MiniGameDef definition, CancellationToken cancellation, bool practice, int seed)
        {
            cancellation.ThrowIfCancellationRequested();
            var ui = UiRoot.Instance;
            if (ui == null) throw new InvalidOperationException("Mod desk requires the game UI.");
            var completion = new TaskCompletionSource<int>();
            var round = new ModDeskRound(Time.realtimeSinceStartupAsDouble, definition.durationSec, definition.maxScore, seed);
            var timer = Plain("", "ct-desk-timer");
            var tally = Plain("", "ct-desk-tally");
            var number = Plain("", "ct-desk-case-number");
            var author = Plain("", "ct-desk-author");
            var message = Plain("", "ct-desk-message");
            var feedbackTitle = Plain("", "ct-desk-feedback-title");
            var feedbackBody = Plain("", "ct-desk-feedback-body");
            var feedback = El.Div("ct-desk-feedback", feedbackTitle, feedbackBody);
            var progress = El.Div("ct-desk-progress-fill");
            var content = El.VerticalScroll("ct-desk-content");
            content.Add(Plain(ShiftStrings.T("rules"), "ct-desk-rules"));
            content.Add(El.Div("ct-desk-message-card", number, author, message));
            content.Add(feedback);
            Button ban = null, allow = null, next = null;
            VisualElement choices = null;
            ban = El.Btn(ShiftStrings.T("ban"), () => Choose(true), "ct-btn ct-desk-choice", sfx: null);
            allow = El.Btn(ShiftStrings.T("allow"), () => Choose(false), "ct-btn ct-desk-choice", sfx: null);
            next = El.Btn(ShiftStrings.T("next"), Next, "ct-btn ct-btn-primary ct-desk-next");
            choices = El.Div("ct-desk-choices", ban, allow);
            var root = El.Div("ct-desk-overlay", El.Div("ct-desk-panel",
                El.Div("ct-desk-header", El.Div("ct-desk-heading", Plain(ShiftStrings.T("desk"), "ct-desk-eyebrow"),
                    Plain(ShiftStrings.T("title"), "ct-desk-title")), timer),
                El.Div("ct-desk-progress", progress),
                El.Div("ct-desk-meta", Plain(ShiftStrings.T(practice ? "practice" : "live"), "ct-desk-context"), tally),
                content, choices, next, Plain(ShiftStrings.T("clockNote"), "ct-desk-footnote")));
            ApplySheet(root);
            root.RegisterCallback<GeometryChangedEvent>(e => root.EnableInClassList("ct-desk-landscape", e.newRect.width > e.newRect.height));
            bool closed = false;
            Action tick = null;
            void Close(bool cancelled)
            {
                if (closed) return;
                closed = true;
                ui.RemoveUpdater(tick);
                root.RemoveFromHierarchy();
                if (cancelled) completion.TrySetCanceled();
                else completion.TrySetResult(round.Score);
            }
            void Render()
            {
                tally.text = ShiftStrings.F("correctCount", round.Correct);
                choices.Show(!round.IsClear && !round.AwaitingNext);
                next.Show(!round.IsClear && round.AwaitingNext);
                feedback.Show(round.AwaitingNext);
                if (round.IsClear)
                {
                    number.text = ShiftStrings.T("clear");
                    author.text = "";
                    message.text = ShiftStrings.F("clearDetail", round.Correct, ModDeskRound.CaseCount);
                }
                else
                {
                    number.text = ShiftStrings.F("case", round.Index + 1, ModDeskRound.CaseCount);
                    author.text = ShiftStrings.T($"c{round.CaseId}.author");
                    message.text = ShiftStrings.T($"c{round.CaseId}.message");
                    next.text = ShiftStrings.T(round.Index == ModDeskRound.CaseCount - 1 ? "finishReview" : "next");
                }
            }
            void Choose(bool shouldBan)
            {
                var result = round.Answer(shouldBan, Time.realtimeSinceStartupAsDouble);
                if (!result.HasValue) return;
                feedbackTitle.text = ShiftStrings.T(result.Value ? "correct" : "incorrect");
                feedbackBody.text = ShiftStrings.T($"c{round.CaseId}.reason");
                feedback.EnableInClassList("ct-desk-feedback-correct", result.Value);
                feedback.EnableInClassList("ct-desk-feedback-incorrect", !result.Value);
                Bus.Publish(new SfxSignal(result.Value ? "sfx_ui_success" : "sfx_ui_error"));
                Render();
            }
            void Next()
            {
                if (round.Advance(Time.realtimeSinceStartupAsDouble)) Render();
            }
            tick = () =>
            {
                if (cancellation.IsCancellationRequested) { Close(true); return; }
                double now = Time.realtimeSinceStartupAsDouble;
                timer.text = ShiftStrings.F("timer", (int)Math.Ceiling(round.Remaining(now)));
                progress.style.width = Length.Percent((float)(100 * round.Remaining(now) / definition.durationSec));
                if (round.Expired(now)) Close(false);
            };
            root.RegisterCallback<DetachFromPanelEvent>(_ => Close(true));
            ui.ModalLayer.Add(root);
            ui.AddUpdater(tick);
            Render();
            tick();
            return completion.Task;
        }

        internal static Label Plain(string text, string classes)
        {
            var label = El.Text(text, classes);
            label.enableRichText = false;
            return label;
        }

        internal static void ApplySheet(VisualElement root)
        {
            var sheet = Resources.Load<StyleSheet>("UI/mod-desk");
            if (sheet != null) root.styleSheets.Add(sheet);
            else Log.Warn("ui", "Missing UI/mod-desk stylesheet");
        }
    }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
    public static class ModDeskChecks
    {
        public static string Run()
        {
            var correct = new ModDeskRound(100, 45, 10, 7);
            var seen = new System.Collections.Generic.HashSet<int>();
            int ban = 0;
            for (int i = 0; i < ModDeskRound.CaseCount; i++)
            {
                int id = correct.CaseId;
                if (!seen.Add(id)) throw new Exception("Duplicate mod case");
                if (id % 2 == 0) ban++;
                if (correct.Answer(id % 2 == 0, 101 + i) != true) throw new Exception("Correct decision rejected");
                if (correct.Answer(id % 2 == 0, 101 + i).HasValue) throw new Exception("Duplicate scored");
                correct.Advance(101 + i);
            }
            if (correct.Score != 10 || ban != 5 || !correct.IsClear) throw new Exception("Unfair deck or score");
            var wrong = new ModDeskRound(100, 45, 10, 11);
            for (int i = 0; i < ModDeskRound.CaseCount; i++) { wrong.Answer(wrong.CaseId % 2 != 0, 101 + i); wrong.Advance(101 + i); }
            if (wrong.Score != 0) throw new Exception("Wrong decisions scored");
            var expired = new ModDeskRound(100, 45, 10, 3);
            if (expired.Answer(true, 145).HasValue || expired.Score != 0 || expired.Remaining(150) != 0) throw new Exception("Deadline not enforced");
            var partial = new ModDeskRound(100, 45, 100, 7);
            partial.Answer(partial.CaseId % 2 == 0, 101);
            if (partial.Score != 10) throw new Exception("Config score cap not scaled");
            var feedbackDeadline = new ModDeskRound(100, 45, 10, 5);
            feedbackDeadline.Answer(feedbackDeadline.CaseId % 2 == 0, 144.9);
            if (feedbackDeadline.Advance(145) || feedbackDeadline.Score != 1 || !feedbackDeadline.Expired(145))
                throw new Exception("Expiry in feedback lost earned score or advanced another case.");
            return "PASS: finite balanced deck, exact/full/zero/partial scores, duplicate-click guard, deadline including feedback";
        }
    }
#endif
}
