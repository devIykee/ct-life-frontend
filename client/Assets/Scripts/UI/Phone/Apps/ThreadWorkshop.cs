using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    public static class ThreadWorkshop
    {
        private static VisualElement _active;
        public static Task<int> Play(MiniGameDef definition, CancellationToken cancellation = default, bool practice = false)
            => PlayCore(definition, cancellation, practice, Environment.TickCount);
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        public static Task<int> PlayPracticeReview(MiniGameDef definition, CancellationToken cancellation, int seed)
            => PlayCore(definition, cancellation, true, seed, true);
        public static Task<int> PlayPoolReview(MiniGameDef definition, CancellationToken cancellation, int seed)
            => PlayCore(definition, cancellation, true, seed);
#endif
        /// <summary>Deterministic entry point for the Careers lane (same seed → same round).</summary>
        public static Task<int> PlaySeeded(MiniGameDef definition, int seed, bool practice, CancellationToken cancellation = default) => PlayCore(definition, cancellation, practice, seed);
        private static Task<int> PlayCore(MiniGameDef definition, CancellationToken cancellation, bool practice, int seed, bool reviewDeck = false)
        {
            cancellation.ThrowIfCancellationRequested();
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var ui = UiRoot.Instance;
            if (ui == null) throw new InvalidOperationException("Thread Workshop requires the game UI.");
            if (_active != null) throw new InvalidOperationException("A Thread Workshop is already running.");
            var sheet = Resources.Load<StyleSheet>("UI/thread-workshop");
            if (sheet == null) throw new InvalidOperationException("Thread Workshop stylesheet is missing.");
            ThreadRound round;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            if (reviewDeck) round = ThreadRound.Review(Time.realtimeSinceStartupAsDouble, definition.durationSec, definition.maxScore, seed);
            else
#endif
                round = new ThreadRound(Time.realtimeSinceStartupAsDouble, definition.durationSec, definition.maxScore, seed);
            var completion = new TaskCompletionSource<int>();
            Label Copy(string key, string classes) => ModDesk.Plain(ThreadStrings.T(key), classes);
            var timer = ModDesk.Plain("", "ct-desk-timer");
            var tally = ModDesk.Plain("", "ct-desk-tally");
            var number = ModDesk.Plain("", "ct-thread-number");
            var story = ModDesk.Plain("", "ct-thread-story");
            var progress = El.Div("ct-desk-progress-fill");
            var tray = El.VerticalScroll("ct-thread-tray");
            var composer = El.VerticalScroll("ct-thread-compose");
            var fragments = El.Div("ct-thread-fragments");
            var slots = El.Div("ct-thread-slots");
            var feedbackTitle = ModDesk.Plain("", "ct-thread-feedback-title");
            var feedbackBody = ModDesk.Plain("", "ct-thread-feedback-body");
            var feedback = El.Div("ct-thread-feedback", feedbackTitle, feedbackBody);
            tray.Add(Copy("tray", "ct-thread-eyebrow")); tray.Add(fragments);
            composer.Add(Copy("compose", "ct-thread-eyebrow")); composer.Add(slots); composer.Add(feedback);
            int selected = -1, displayedStory = 0;
            bool closed = false;
            Action tick = null;
            IVisualElementScheduledItem feedbackScroll = null;
            int feedbackScrollVersion = -1;
            Button undo = null, up = null, down = null, remove = null, publish = null, next = null;
            VisualElement root = null;
            Button Control(string key, Action action)
            {
                var b = El.Btn(ThreadStrings.T(key), action, "ct-btn ct-thread-control", sfx: null);
                b.name = "thread-" + key; return b;
            }
            undo = Control("undo", () => Edit(() => { bool ok = round.Undo(Now()); if (ok) selected = -1; return ok; }));
            up = Control("up", () => Edit(() => { bool ok = round.Move(selected, -1, Now()); if (ok) selected--; return ok; }));
            down = Control("down", () => Edit(() => { bool ok = round.Move(selected, 1, Now()); if (ok) selected++; return ok; }));
            remove = Control("remove", () => Edit(() => { bool ok = round.Remove(selected, Now()); if (ok) selected = -1; return ok; }));
            publish = Control("publish", Publish);
            publish.AddToClassList("ct-thread-publish");
            next = Control("next", Next); next.AddToClassList("ct-thread-publish");
            root = El.Div("ct-desk-overlay ct-thread-overlay", El.Div("ct-desk-panel ct-thread-panel",
                El.Div("ct-desk-header", El.Div("ct-desk-heading", Copy("title", "ct-desk-eyebrow"), story), timer),
                El.Div("ct-desk-progress", progress),
                El.Div("ct-desk-meta", Copy(practice ? "practice" : "live", "ct-desk-context"), number, tally),
                Copy("brief", "ct-thread-brief"),
                El.Div("ct-thread-body", tray, composer),
                El.Div("ct-thread-controls", undo, up, down, remove, publish, next),
                Copy("local", "ct-thread-local")));
            root.name = "thread-workshop";
            ModDesk.ApplySheet(root); root.styleSheets.Add(sheet);
            root.RegisterCallback<GeometryChangedEvent>(e => root.EnableInClassList("ct-thread-landscape", e.newRect.width > e.newRect.height));
            double Now() => Time.realtimeSinceStartupAsDouble;
            void Close(bool cancelled, bool detach = true)
            {
                if (closed) return;
                closed = true;
                CancelFeedbackScroll();
                ui.RemoveUpdater(tick);
                if (ReferenceEquals(_active, root)) _active = null;
                if (detach) root.RemoveFromHierarchy();
                if (cancelled) completion.TrySetCanceled(); else completion.TrySetResult(round.Score);
            }
            void Edit(Func<bool> action)
            {
                if (closed || cancellation.IsCancellationRequested || !action()) return;
                Bus.Publish(new SfxSignal("sfx_ui_tap")); Render();
            }
            void Render()
            {
                tally.text = ThreadStrings.F("tally", round.Correct, ThreadRound.ThreadCount);
                if (!round.Clear) displayedStory = round.Current.Story;
                number.text = round.Clear ? ThreadStrings.T("clear") : ThreadStrings.F("progress", round.Index + 1, ThreadRound.ThreadCount);
                story.text = ThreadStrings.T(displayedStory + ".title");
                fragments.Clear(); slots.Clear();
                bool editable = !round.Clear && !round.Filed;
                int version = round.Index;
                if (!round.Clear)
                    foreach (int fragment in round.Current.Pieces)
                    {
                        int piece = fragment;
                        var button = El.Btn(ThreadStrings.Fragment(displayedStory, piece), () => Edit(() => {
                            if (round.Index != version) return false;
                            bool ok = round.Add(piece, Now()); if (ok) selected = round.Compose.Count - 1; return ok;
                        }), "ct-btn ct-thread-fragment", sfx: null);
                        button.name = "thread-piece-" + piece;
                        bool used = false; foreach (int placed in round.Compose) if (placed == piece) used = true;
                        button.SetEnabled(editable && !used); fragments.Add(button);
                    }
                else fragments.Add(Copy("clock", "ct-thread-instruction"));
                for (int i = 0; i < ThreadRound.PieceCount; i++)
                {
                    int slot = i;
                    if (i >= round.Compose.Count)
                    {
                        slots.Add(ModDesk.Plain(ThreadStrings.F("empty", i + 1), "ct-thread-empty")); continue;
                    }
                    var button = El.Btn("", () => {
                        if (closed || cancellation.IsCancellationRequested || round.Index != version || !editable || round.Expired(Now())) return;
                        selected = slot; Render();
                    }, "ct-btn ct-thread-slot", sfx: null);
                    button.name = "thread-slot-" + slot;
                    button.Add(ModDesk.Plain(ThreadStrings.F("slot", i + 1), "ct-thread-slot-number"));
                    button.Add(ModDesk.Plain(ThreadStrings.Fragment(displayedStory, round.Compose[i]), "ct-thread-slot-copy"));
                    button.EnableInClassList("ct-thread-selected", editable && selected == i);
                    button.SetEnabled(editable); slots.Add(button);
                }
                feedback.Show(round.Filed || round.Clear);
                if (round.Clear)
                {
                    feedbackTitle.text = ThreadStrings.T("clear");
                    feedbackBody.text = ThreadStrings.F("clearDetail", round.Correct, ThreadRound.ThreadCount);
                }
                else if (round.Filed)
                {
                    feedbackTitle.text = ThreadStrings.T(round.LastCorrect ? "correct" : "incorrect");
                    feedbackBody.text = ThreadStrings.T(displayedStory + ".reason");
                }
                undo.SetEnabled(editable && round.CanUndo);
                up.SetEnabled(editable && selected > 0);
                down.SetEnabled(editable && selected >= 0 && selected < round.Compose.Count - 1);
                remove.SetEnabled(editable && selected >= 0 && selected < round.Compose.Count);
                publish.Show(!round.Filed && !round.Clear); publish.SetEnabled(editable && round.Compose.Count == ThreadRound.PieceCount);
                next.Show(round.Filed && !round.Clear);
                next.text = ThreadStrings.T(round.Index == ThreadRound.ThreadCount - 1 ? "finish" : "next");
            }
            void CancelFeedbackScroll()
            {
                feedbackScrollVersion = -1;
                feedbackScroll?.Pause(); feedbackScroll = null;
            }
            void FeedbackLayoutChanged(GeometryChangedEvent e)
            {
                if (feedbackScrollVersion < 0 || closed || cancellation.IsCancellationRequested ||
                    !round.Filed || round.Index != feedbackScrollVersion || feedback.layout.height <= 0) return;
                // GeometryChanged runs after layout; wait one frame for the ScrollView's own
                // content/viewport geometry handlers to finish updating its scroll range.
                int version = feedbackScrollVersion;
                feedbackScroll?.Pause();
                feedbackScroll = root.schedule.Execute(() =>
                {
                    feedbackScroll = null;
                    if (closed || cancellation.IsCancellationRequested || root.panel == null ||
                        version != feedbackScrollVersion || version != round.Index || !round.Filed || round.Expired(Now())) return;
                    feedbackScrollVersion = -1;
                    composer.ScrollTo(feedback);
                });
            }
            feedback.RegisterCallback<GeometryChangedEvent>(FeedbackLayoutChanged);
            composer.contentContainer.RegisterCallback<GeometryChangedEvent>(FeedbackLayoutChanged);
            void Publish()
            {
                if (closed || cancellation.IsCancellationRequested) return;
                bool? right = round.Publish(Now());
                if (!right.HasValue) return;
                CancelFeedbackScroll(); feedbackScrollVersion = round.Index;
                selected = -1; Render();
                Bus.Publish(new SfxSignal("sfx_ui_tap"));
            }
            void Next()
            {
                if (closed || cancellation.IsCancellationRequested || !round.Next(Now())) return;
                CancelFeedbackScroll();
                selected = -1; Render(); tray.scrollOffset = Vector2.zero; composer.scrollOffset = Vector2.zero;
            }
            tick = () =>
            {
                if (cancellation.IsCancellationRequested) { Close(true); return; }
                double now = Now(); timer.text = ThreadStrings.F("timer", (int)Math.Ceiling(round.Remaining(now)));
                progress.style.width = Length.Percent((float)(100 * round.Remaining(now) / definition.durationSec));
                if (round.Expired(now)) Close(false);
            };
            root.RegisterCallback<DetachFromPanelEvent>(e => { if (ReferenceEquals(e.target, root)) Close(true, false); });
            _active = root;
            try { ui.ModalLayer.Add(root); ui.AddUpdater(tick); Render(); tick(); }
            catch { Close(true); throw; }
            return completion.Task;
        }
    }
}
