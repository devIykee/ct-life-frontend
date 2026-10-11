using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Local receipt puzzle. Its authored slips are not account data or payment instructions.</summary>
    public static class ReceiptMatch
    {
        private static VisualElement _active;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        private static Action<bool> _reviewFocus, _reviewPause;
        private static Func<double> _reviewElapsed;
        public static double ReviewElapsed => _reviewElapsed?.Invoke() ?? -1;
        public static Task<ReceiptMatchResult> PlayReview(CancellationToken cancellation, int seed) => PlayCore(cancellation, seed, false, true);
        public static void ReviewFocus(bool focused) => _reviewFocus?.Invoke(focused);
        public static void ReviewPause(bool paused) => _reviewPause?.Invoke(paused);
#endif
        public static Task<ReceiptMatchResult> Play(CancellationToken cancellation = default) => PlayCore(cancellation, Environment.TickCount, true, false);
        private static Task<ReceiptMatchResult> PlayCore(CancellationToken cancellation, int seed, bool observeFocus, bool review)
        {
            cancellation.ThrowIfCancellationRequested(); var ui = UiRoot.Instance;
            if (ui == null) throw new InvalidOperationException("Receipt Match requires the game UI.");
            if (_active != null) throw new InvalidOperationException("A Receipt Match round is already running.");
            var sheet = Resources.Load<StyleSheet>("UI/receipt-match");
            if (sheet == null) throw new InvalidOperationException("Receipt Match stylesheet is missing.");
            var round = new ReceiptMatchRound(Time.realtimeSinceStartupAsDouble, seed);
            var completion = new TaskCompletionSource<ReceiptMatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool closed = false; int renderedBoard = -1; Action tick = null;
            VisualElement root = null, paused = null; Button clear = null, file = null, next = null, pause = null;
            var rows = new Button[4]; var slips = new Button[4]; var rowStates = new Label[4]; var slipStates = new Label[4];
            Label Copy(string key, string classes) => ModDesk.Plain(ReceiptMatchStrings.T(key), classes);
            var timer = ModDesk.Plain("", "ct-desk-timer"); var score = ModDesk.Plain("", "ct-receipt-score");
            var title = ModDesk.Plain("", "ct-receipt-board-title"); var counter = ModDesk.Plain("", "ct-receipt-counter");
            var instruction = Copy("instruction", "ct-receipt-instruction");
            var statement = El.Div("ct-receipt-statement"); statement.name = "receipt-statement";
            var tray = El.Div("ct-receipt-tray"); tray.name = "receipt-tray";
            var progress = El.Div("ct-desk-progress-fill");
            double Now() => Time.realtimeSinceStartupAsDouble;
            void Sound() => Bus.Publish(new SfxSignal("sfx_ui_tap"));
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
            string Amount(ReceiptEntry entry) => ReceiptMatchStrings.F("amount", entry.Amount);
            string Date(ReceiptEntry entry) => ReceiptMatchStrings.F("date", entry.Day);
            string Reference(ReceiptEntry entry) => ReceiptMatchStrings.F("reference", entry.Reference);
            void ReplaceAction(ref Button previous, string name, string key, Action action, string extra)
            {
                var parent = previous.parent; int index = parent.IndexOf(previous);
                var button = El.Btn(ReceiptMatchStrings.T(key), action, "ct-btn ct-receipt-control " + extra, sfx: null);
                button.name = name; previous.RemoveFromHierarchy(); parent.Insert(index, button); previous = button;
            }
            void BuildBoard()
            {
                renderedBoard = round.Index; int boardIndex = renderedBoard; var board = round.Current;
                statement.Clear(); tray.Clear();
                statement.Add(Copy("statement", "ct-receipt-section")); tray.Add(Copy("receipts", "ct-receipt-section"));
                for (int i = 0; i < 4; i++)
                {
                    int row = i; var entry = board.Row(i);
                    var rowButton = El.Btn("", () => {
                        if (closed || cancellation.IsCancellationRequested || !round.Place(boardIndex, row, Now())) return;
                        Sound(); Render();
                    }, "ct-btn ct-receipt-entry", sfx: null); rowButton.name = "receipt-row-" + i;
                    rowStates[i] = Copy("unpaired", "ct-receipt-row-state");
                    var rowBody = El.Div("ct-receipt-entry-copy",
                        El.Div("ct-receipt-line", ModDesk.Plain(Amount(entry), "ct-receipt-amount"), rowStates[i]),
                        El.Div("ct-receipt-line", ModDesk.Plain(Reference(entry), "ct-receipt-reference"), ModDesk.Plain(Date(entry), "ct-receipt-date")));
                    rowButton.Add(El.PassThrough(El.Div("ct-receipt-entry-inner", ModDesk.Plain(ReceiptMatchStrings.F("row", i + 1), "ct-receipt-number"), rowBody)));
                    statement.Add(rowButton); rows[i] = rowButton;

                    int receipt = i; entry = board.Receipt(i);
                    var slip = El.Btn("", () => {
                        if (closed || cancellation.IsCancellationRequested || !round.Select(boardIndex, receipt, Now())) return;
                        Sound(); Render();
                    }, "ct-btn ct-receipt-slip", sfx: null); slip.name = "receipt-slip-" + i;
                    slipStates[i] = Copy("loose", "ct-receipt-slip-state");
                    var slipBody = El.Div("ct-receipt-slip-copy",
                        El.Div("ct-receipt-line", ModDesk.Plain(ReceiptMatchStrings.Board(board, "item." + entry.Item), "ct-receipt-item"), ModDesk.Plain(Amount(entry), "ct-receipt-amount")),
                        El.Div("ct-receipt-line", ModDesk.Plain(Reference(entry), "ct-receipt-reference"), ModDesk.Plain(Date(entry), "ct-receipt-date"), slipStates[i]));
                    slip.Add(El.PassThrough(El.Div("ct-receipt-slip-inner", ModDesk.Plain(ReceiptMatchStrings.Letter(i), "ct-receipt-letter"), slipBody)));
                    slip.Add(El.PassThrough(El.Div("ct-receipt-notch ct-receipt-notch-left")));
                    slip.Add(El.PassThrough(El.Div("ct-receipt-notch ct-receipt-notch-right")));
                    slip.tooltip = ReceiptMatchStrings.F("receiptTip", ReceiptMatchStrings.Letter(i), ReceiptMatchStrings.Board(board, "item." + entry.Item), Amount(entry), Reference(entry), Date(entry));
                    tray.Add(slip); slips[i] = slip;
                }
                void FileCurrent()
                {
                    if (closed || cancellation.IsCancellationRequested || !round.File(boardIndex, Now())) return;
                    Bus.Publish(new SfxSignal(round.BoardCorrect == 4 ? "sfx_ui_tap" : "sfx_ui_error")); Render();
                    if (round.Finished) Close(false);
                }
                void NextCurrent() { if (!closed && !cancellation.IsCancellationRequested && round.Next(boardIndex, Now())) { Sound(); Render(); } }
                void ClearCurrent() { if (!closed && !cancellation.IsCancellationRequested && round.Clear(boardIndex, Now())) { Sound(); Render(); } }
                ReplaceAction(ref file, "receipt-file", "file", FileCurrent, "ct-receipt-primary");
                ReplaceAction(ref next, "receipt-next", "next", NextCurrent, "ct-receipt-primary");
                ReplaceAction(ref clear, "receipt-clear", "clear", ClearCurrent, "");
            }
            clear = El.Btn(ReceiptMatchStrings.T("clear"), null, "ct-btn ct-receipt-control", sfx: null); clear.name = "receipt-clear";
            file = El.Btn(ReceiptMatchStrings.T("file"), null, "ct-btn ct-receipt-control ct-receipt-primary", sfx: null); file.name = "receipt-file";
            next = El.Btn(ReceiptMatchStrings.T("next"), null, "ct-btn ct-receipt-control ct-receipt-primary", sfx: null); next.name = "receipt-next";
            pause = El.Btn(ReceiptMatchStrings.T("pause"), Pause, "ct-btn ct-receipt-control", sfx: null); pause.name = "receipt-pause";
            var end = El.Btn(ReceiptMatchStrings.T("end"), () => Close(true), "ct-btn ct-receipt-control", sfx: null); end.name = "receipt-end";
            var resume = El.Btn(ReceiptMatchStrings.T("resume"), Resume, "ct-btn ct-receipt-control ct-receipt-primary", sfx: null); resume.name = "receipt-resume";
            var pauseEnd = El.Btn(ReceiptMatchStrings.T("end"), () => Close(true), "ct-btn ct-receipt-control", sfx: null); pauseEnd.name = "receipt-paused-end";
            paused = El.Div("ct-receipt-paused", El.Div("ct-receipt-pause-panel", Copy("paused", "ct-desk-title"),
                Copy("pauseDetail", "ct-receipt-pause-copy"), El.Div("ct-receipt-controls", resume, pauseEnd))); paused.name = "receipt-paused";
            root = El.Div("ct-desk-overlay ct-receipt-overlay", El.Div("ct-desk-panel ct-receipt-panel",
                El.Div("ct-desk-header", El.Div("ct-desk-heading", Copy("title", "ct-receipt-title")), score, timer),
                El.Div("ct-desk-progress", progress), El.Div("ct-receipt-meta", title, counter), instruction,
                El.Div("ct-receipt-workspace", statement, tray),
                El.Div("ct-receipt-controls", clear, pause, end, file, next), Copy(review ? "review" : "practice", "ct-receipt-footnote"), paused));
            root.name = "receipt-match-game"; ModDesk.ApplySheet(root); root.styleSheets.Add(sheet);
            void Render()
            {
                if (renderedBoard != round.Index) BuildBoard(); var board = round.Current;
                timer.text = ReceiptMatchStrings.F("timer", (int)Math.Ceiling(round.Remaining)); score.text = ReceiptMatchStrings.F("score", round.Score);
                title.text = ReceiptMatchStrings.Board(board, "title"); counter.text = ReceiptMatchStrings.F("board", round.Index + 1);
                progress.style.width = Length.Percent((float)(round.Remaining * 100 / ReceiptMatchRound.Duration));
                instruction.text = round.Filed ? ReceiptMatchStrings.F("filed", round.BoardCorrect, round.BoardCorrect * 5) + " · " + ReceiptMatchStrings.Board(board, "joke")
                    : round.Selected >= 0 ? ReceiptMatchStrings.F("selected", ReceiptMatchStrings.Letter(round.Selected))
                    : round.PairedCount == 0 ? ReceiptMatchStrings.T("instruction") : ReceiptMatchStrings.F("ready", round.PairedCount);
                instruction.EnableInClassList("ct-receipt-outcome", round.Filed);
                instruction.EnableInClassList("ct-receipt-outcome-error", round.Filed && round.BoardCorrect < 4);
                paused.Show(round.Paused);
                pause.SetEnabled(!round.Paused); clear.SetEnabled(!round.Paused && !round.Filed && (round.PairedCount > 0 || round.Selected >= 0));
                file.Show(!round.Filed); file.SetEnabled(!round.Paused && !round.Finished && round.PairedCount > 0);
                next.Show(round.Filed && !round.AllFiled); next.SetEnabled(!round.Paused && !round.Finished);
                for (int i = 0; i < 4; i++)
                {
                    int pair = round.Pair(i); bool correct = board.Matches(i, pair);
                    rowStates[i].text = round.Filed ? ReceiptMatchStrings.F(correct ? "matched" : "correctReceipt", ReceiptMatchStrings.Letter(correct ? pair : board.CorrectReceipt(i)))
                        : pair >= 0 ? ReceiptMatchStrings.F("paired", ReceiptMatchStrings.Letter(pair)) : ReceiptMatchStrings.T("unpaired");
                    rows[i].EnableInClassList("ct-receipt-row-paired", pair >= 0);
                    rows[i].EnableInClassList("ct-receipt-correct", round.Filed && correct); rows[i].EnableInClassList("ct-receipt-wrong", round.Filed && !correct);
                    rows[i].SetEnabled(!round.Paused && !round.Filed && !round.Finished && (round.Selected >= 0 || pair >= 0));
                    var entry = board.Row(i); rows[i].tooltip = ReceiptMatchStrings.F("rowTip", i + 1, Amount(entry), Reference(entry), Date(entry), rowStates[i].text);
                    int pairedRow = round.RowFor(i); bool selected = round.Selected == i;
                    slipStates[i].text = selected ? ReceiptMatchStrings.T("held") : pairedRow >= 0 ? ReceiptMatchStrings.F("receiptRow", pairedRow + 1) : ReceiptMatchStrings.T("loose");
                    slips[i].EnableInClassList("ct-receipt-selected", selected); slips[i].EnableInClassList("ct-receipt-slip-paired", pairedRow >= 0);
                    slips[i].EnableInClassList("ct-receipt-correct", round.Filed && pairedRow >= 0 && board.Matches(pairedRow, i));
                    slips[i].EnableInClassList("ct-receipt-wrong", round.Filed && pairedRow >= 0 && !board.Matches(pairedRow, i));
                    slips[i].SetEnabled(!round.Paused && !round.Filed && !round.Finished);
                }
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
