using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>A local access-matrix puzzle. No real admin API, economic write or server assignment.</summary>
    public static class RoleLock
    {
        private static VisualElement _active;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        private static Action<bool> _reviewFocus, _reviewPause;
        private static Func<double> _reviewElapsed;
        public static double ReviewElapsed => _reviewElapsed?.Invoke() ?? -1;
        public static Task<RoleLockResult> PlayReview(CancellationToken cancellation, int seed) => PlayCore(cancellation, seed, false, true);
        public static void ReviewFocus(bool focused) => _reviewFocus?.Invoke(focused);
        public static void ReviewPause(bool paused) => _reviewPause?.Invoke(paused);
#endif
        public static Task<RoleLockResult> Play(CancellationToken cancellation = default) => PlayCore(cancellation, Environment.TickCount, true, false);

        private static Task<RoleLockResult> PlayCore(CancellationToken cancellation, int seed, bool observeFocus, bool review)
        {
            cancellation.ThrowIfCancellationRequested();
            var ui = UiRoot.Instance;
            if (ui == null) throw new InvalidOperationException("Role Lock requires the game UI.");
            if (_active != null) throw new InvalidOperationException("A Role Lock round is already running.");
            var sheet = Resources.Load<StyleSheet>("UI/role-lock");
            if (sheet == null) throw new InvalidOperationException("Role Lock stylesheet is missing.");
            var round = new RoleLockRound(Time.realtimeSinceStartupAsDouble, seed);
            var completion = new TaskCompletionSource<RoleLockResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool closed = false; int renderedBoard = -1;
            VisualElement root = null, paused = null; Action tick = null;
            Button clear = null, lockAccess = null, next = null, pause = null;
            var cells = new Button[3, 3]; var states = new Label[3, 3];
            Label Copy(string key, string classes) => ModDesk.Plain(RoleLockStrings.T(key), classes);
            var timer = ModDesk.Plain("", "ct-desk-timer");
            var score = ModDesk.Plain("", "ct-role-score");
            var title = ModDesk.Plain("", "ct-role-board-title");
            var counter = ModDesk.Plain("", "ct-role-counter");
            var feedback = ModDesk.Plain("", "ct-role-feedback");
            var explanation = ModDesk.Plain("", "ct-role-explanation");
            var grid = El.Div("ct-role-grid"); grid.name = "role-grid";
            var brief = Copy("instruction", "ct-role-instruction");
            var shackle = El.Div("ct-role-shackle"); shackle.name = "role-seal-shackle";
            var seal = El.PassThrough(El.Div("ct-role-seal", shackle, El.Div("ct-role-lock-body", El.Div("ct-role-keyhole"))));
            var feedbackArea = El.Div("ct-role-feedback-area", seal, El.Div("ct-role-feedback-copy", feedback, explanation));
            var progress = El.Div("ct-desk-progress-fill");
            double Now() => Time.realtimeSinceStartupAsDouble;
            void Close(bool cancel, bool detach = true)
            {
                if (closed) return; closed = true;
                if (observeFocus) { Application.focusChanged -= Focus; Runner.ApplicationPaused -= ApplicationPause; }
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                if (ReferenceEquals(_active, root)) { _reviewFocus = null; _reviewPause = null; _reviewElapsed = null; }
#endif
                ui.RemoveUpdater(tick); if (ReferenceEquals(_active, root)) _active = null;
                if (cancel) round.Cancel();
                if (detach) root.RemoveFromHierarchy();
                if (cancel) completion.TrySetCanceled(); else completion.TrySetResult(round.Result);
            }
            void Pause()
            { if (!closed && !cancellation.IsCancellationRequested && round.Pause(Now())) Render(); }
            void Focus(bool focused) { if (!focused) Pause(); }
            void ApplicationPause(bool value) { if (value) Pause(); }
            void Resume()
            { if (!closed && !cancellation.IsCancellationRequested && round.Resume(Now())) Render(); }
            void ClickSound() => Bus.Publish(new SfxSignal("sfx_ui_tap"));
            void BuildBoard()
            {
                renderedBoard = round.Index; int boardIndex = renderedBoard;
                var board = round.Current;
                grid.Clear();
                grid.Add(El.PassThrough(El.Div("ct-role-case-edge")));
                foreach (var corner in new[] { "top-left", "top-right", "bottom-left", "bottom-right" })
                    grid.Add(El.PassThrough(El.Div("ct-role-rivet ct-role-rivet-" + corner, El.Div("ct-role-rivet-slot"))));
                var headers = El.Div("ct-role-grid-header", Copy("role", "ct-role-row-heading"));
                for (int column = 0; column < 3; column++)
                    headers.Add(ModDesk.Plain(RoleLockStrings.Board(board, "permission." + board.Permission(column)), "ct-role-permission"));
                grid.Add(headers);
                for (int row = 0; row < 3; row++)
                {
                    int r = row;
                    var role = RoleLockStrings.Board(board, "role." + board.Role(row));
                    var task = RoleLockStrings.Board(board, "task." + board.Role(row));
                    var line = El.Div("ct-role-row", El.Div("ct-role-task", ModDesk.Plain(role, "ct-role-name"), ModDesk.Plain(task, "ct-role-task-copy")));
                    for (int column = 0; column < 3; column++)
                    {
                        int c = column;
                        var cell = El.Btn("", () => {
                            if (closed || cancellation.IsCancellationRequested || !round.Toggle(boardIndex, r, c, Now())) return;
                            ClickSound(); Render();
                        }, "ct-btn ct-role-cell", sfx: null);
                        cell.name = "role-cell-" + r + "-" + c;
                        var state = Copy("off", "ct-role-cell-state"); state.pickingMode = PickingMode.Ignore;
                        var grip = El.Div("ct-role-thumb-grip", El.Div("ct-role-grip-ridge"), El.Div("ct-role-grip-ridge"), El.Div("ct-role-grip-ridge"));
                        var thumb = El.Div("ct-role-switch-thumb", grip);
                        var rail = El.Div("ct-role-switch", El.Div("ct-role-rail-guide"), thumb);
                        var light = El.Div("ct-role-indicator", El.Div("ct-role-indicator-glint"));
                        cell.Add(El.PassThrough(El.Div("ct-role-switch-assembly", rail, light))); cell.Add(state); cells[r, c] = cell; states[r, c] = state; line.Add(cell);
                    }
                    grid.Add(line);
                }
                // Bind to this board generation. A late detached button cannot toggle/lock its successor.
                void LockCurrent()
                {
                    if (closed || cancellation.IsCancellationRequested || !round.Lock(boardIndex, Now())) return;
                    Bus.Publish(new SfxSignal(round.Correct ? "sfx_ui_tap" : "sfx_ui_error")); Render();
                    if (round.Finished) Close(false);
                }
                void NextCurrent()
                { if (!closed && !cancellation.IsCancellationRequested && round.Next(boardIndex, Now())) { ClickSound(); Render(); } }
                void ClearCurrent()
                { if (!closed && !cancellation.IsCancellationRequested && round.Clear(boardIndex, Now())) { ClickSound(); Render(); } }
                // Replaced controls below own their generation-specific delegate; no handler accumulates across boards.
                ReplaceAction(ref lockAccess, "role-lock", "lock", LockCurrent, "ct-role-primary");
                ReplaceAction(ref next, "role-next", "next", NextCurrent, "ct-role-primary");
                ReplaceAction(ref clear, "role-clear", "clear", ClearCurrent, "");
            }
            void ReplaceAction(ref Button previous, string name, string copy, Action action, string extra)
            {
                var parent = previous.parent; int index = parent.IndexOf(previous);
                var replacement = El.Btn(RoleLockStrings.T(copy), action, "ct-btn ct-role-control " + extra, sfx: null);
                if (copy == "lock")
                    replacement.Add(El.PassThrough(El.Div("ct-role-action-lock", El.Div("ct-role-shackle"), El.Div("ct-role-lock-body", El.Div("ct-role-keyhole")))));
                replacement.name = name; previous.RemoveFromHierarchy(); parent.Insert(index, replacement); previous = replacement;
            }
            clear = El.Btn(RoleLockStrings.T("clear"), null, "ct-btn ct-role-control", sfx: null); clear.name = "role-clear";
            lockAccess = El.Btn(RoleLockStrings.T("lock"), null, "ct-btn ct-role-control ct-role-primary", sfx: null); lockAccess.name = "role-lock";
            next = El.Btn(RoleLockStrings.T("next"), null, "ct-btn ct-role-control ct-role-primary", sfx: null); next.name = "role-next";
            pause = El.Btn(RoleLockStrings.T("pause"), Pause, "ct-btn ct-role-control", sfx: null); pause.name = "role-pause";
            var end = El.Btn(RoleLockStrings.T("end"), () => Close(true), "ct-btn ct-role-control ct-role-end", sfx: null); end.name = "role-end";
            var resume = El.Btn(RoleLockStrings.T("resume"), Resume, "ct-btn ct-role-control ct-role-primary", sfx: null); resume.name = "role-resume";
            var pauseEnd = El.Btn(RoleLockStrings.T("end"), () => Close(true), "ct-btn ct-role-control", sfx: null); pauseEnd.name = "role-paused-end";
            paused = El.Div("ct-role-paused", El.Div("ct-role-pause-panel", Copy("paused", "ct-desk-title"),
                Copy("pauseDetail", "ct-role-pause-copy"), El.Div("ct-role-controls", resume, pauseEnd))); paused.name = "role-paused";
            root = El.Div("ct-desk-overlay ct-role-overlay", El.Div("ct-desk-panel ct-role-panel",
                El.Div("ct-desk-header", El.Div("ct-desk-heading", Copy("title", "ct-role-title"), Copy("desk", "ct-role-kicker")), score, timer),
                El.Div("ct-desk-progress", progress), El.Div("ct-role-meta", title, counter), brief, grid,
                feedbackArea, El.Div("ct-role-controls", clear, pause, end, El.Div("ct-role-control-spacer"), lockAccess, next),
                Copy(review ? "review" : "practice", "ct-role-footnote"), paused));
            root.name = "role-lock-game"; ModDesk.ApplySheet(root); root.styleSheets.Add(sheet);
            void Render()
            {
                if (renderedBoard != round.Index) BuildBoard();
                var board = round.Current;
                timer.text = RoleLockStrings.F("timer", (int)Math.Ceiling(round.Remaining));
                score.text = RoleLockStrings.F("score", round.Score);
                brief.text = RoleLockStrings.T("instruction");
                title.text = RoleLockStrings.Board(board, "title"); counter.text = RoleLockStrings.F("board", round.Index + 1);
                progress.style.width = Length.Percent((float)(round.Remaining * 100 / RoleLockRound.Duration));
                paused.Show(round.Paused); pause.SetEnabled(!round.Paused);
                clear.SetEnabled(!round.Paused && !round.Locked && round.Selection != 0);
                lockAccess.Show(!round.Locked); lockAccess.SetEnabled(!round.Paused && !round.Finished);
                next.Show(round.Locked && !round.AllLocked); next.SetEnabled(!round.Paused && !round.Finished);
                for (int row = 0; row < 3; row++) for (int column = 0; column < 3; column++)
                {
                    var cell = cells[row, column]; bool selected = round.Selected(row, column), needed = board.Requires(row, column);
                    bool error = round.Locked && selected != needed;
                    var state = RoleLockStrings.T(error ? (selected ? "extra" : "missing") : selected ? "on" : "off");
                    states[row, column].text = state;
                    cell.tooltip = RoleLockStrings.F("cell", RoleLockStrings.Board(board, "role." + board.Role(row)),
                        RoleLockStrings.Board(board, "permission." + board.Permission(column)), state);
                    cell.EnableInClassList("ct-role-selected", selected); cell.EnableInClassList("ct-role-error", error);
                    cell.SetEnabled(!round.Paused && !round.Locked && !round.Finished);
                }
                if (!round.Locked)
                {
                    int granted = RoleLockRound.Bits(round.Selection);
                    feedback.text = granted == 1 ? RoleLockStrings.T("readyOne") : RoleLockStrings.F("ready", granted);
                    explanation.text = RoleLockStrings.Board(board, "joke");
                }
                else
                {
                    feedback.text = RoleLockStrings.T(round.Correct ? "correct" : "incorrect");
                    explanation.text = round.Correct ? RoleLockStrings.Board(board, "success") : RoleLockStrings.F("mistakes",
                        RoleLockRound.Bits(round.Selection & ~board.RequiredMask), RoleLockRound.Bits(board.RequiredMask & ~round.Selection));
                    if (round.AllLocked) counter.text = RoleLockStrings.T("allLocked");
                }
                feedback.EnableInClassList("ct-role-feedback-error", round.Locked && !round.Correct);
                feedbackArea.EnableInClassList("ct-role-sealed", round.Locked);
                feedbackArea.EnableInClassList("ct-role-sealed-error", round.Locked && !round.Correct);
            }
            tick = () => {
                if (closed) return;
                if (cancellation.IsCancellationRequested) { Close(true); return; }
                round.Step(Now()); Render(); if (round.Finished) Close(false);
            };
            root.RegisterCallback<DetachFromPanelEvent>(e => { if (ReferenceEquals(e.target, root)) Close(true, false); });
            _active = root;
            try
            {
                ui.ModalLayer.Add(root); ui.AddUpdater(tick);
                if (observeFocus)
                {
                    _ = Runner.Instance; Application.focusChanged += Focus; Runner.ApplicationPaused += ApplicationPause;
                    if (!Application.isFocused) Pause();
                }
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
