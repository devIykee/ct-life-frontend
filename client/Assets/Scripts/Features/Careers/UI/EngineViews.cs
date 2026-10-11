using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace CTLife.Careers
{
    internal interface IEngineView
    {
        VisualElement Root { get; }
        /// <summary>Called every frame while playing and after each input. Must be cheap: update, don't rebuild.</summary>
        void Render();
    }

    /// <summary>One view per engine. Views only call round methods; the session decides whether input is open.</summary>
    internal static class EngineViews
    {
        public static IEngineView Create(MiniGameSpec spec, IMiniGameRound round, Func<bool> canInteract, Action afterInput)
        {
            var labels = (spec.ActionLabel ?? "").Split('|');
            switch (round)
            {
                case SortRound r: return new SortView(r, canInteract, afterInput);
                case FlagRound r: return new FlagView(r, canInteract, afterInput);
                case SequenceRound r: return new SequenceView(r, canInteract, afterInput);
                case MatchRound r: return new MatchView(r, canInteract, afterInput);
                case PickRound r: return new PickView(r, canInteract, afterInput);
                case ComboRound r: return new ComboView(r, canInteract, afterInput);
                case TimingRound r: return new TimingView(r, canInteract, afterInput, labels[0]);
                case BalanceRound r: return new BalanceView(r, canInteract, afterInput);
                default: throw new InvalidOperationException("No view for " + round.GetType().Name);
            }
        }

        internal static string Str(string key) => CareersStrings.T(key);
        internal static string Fmt(string key, params object[] a) => CareersStrings.F(key, a);

        /// <summary>A feedback box: verdict line plus an explanation, marked good/bad without relying on colour alone.</summary>
        internal sealed class Feedback
        {
            public readonly VisualElement Root;
            private readonly Label _head, _why;
            public Feedback()
            {
                _head = CUi.L("", "ctc-feedback-head");
                _why = CUi.L("", "ctc-feedback-why");
                Root = CUi.Div("ctc-feedback", _head, _why);
                CUi.Show(Root, false);
            }

            public void Set(bool? good, string head, string why)
            {
                CUi.Show(Root, true);
                _head.text = head ?? "";
                _why.text = why ?? "";
                CUi.Show(_why, !string.IsNullOrEmpty(why));
                CUi.Mark(Root, "ctc-good", good == true);
                CUi.Mark(Root, "ctc-bad", good == false);
            }

            public void Hide() => CUi.Show(Root, false);
        }

        // ------------------------------------------------------------ Sort

        private sealed class SortView : IEngineView
        {
            private readonly SortRound _r;
            private readonly Func<bool> _can;
            private readonly Action _after;
            private readonly Label _progress, _status, _title, _body;
            private readonly VisualElement _chips, _swatch;
            private readonly Label _swatchText;
            private readonly Feedback _fb = new Feedback();
            private int _shownIndex = -1;
            public VisualElement Root { get; }

            public SortView(SortRound r, Func<bool> can, Action after)
            {
                _r = r; _can = can; _after = after;
                _progress = CUi.L("", "ctc-muted");
                _status = CUi.L("", "ctc-status");
                _title = CUi.L("", "ctc-card-title");
                _body = CUi.L("", "ctc-body-text");
                _chips = CUi.Div("ctc-chips");
                _swatchText = CUi.L("", "ctc-swatch-text");
                _swatch = CUi.Div("ctc-swatch", _swatchText);
                var bins = CUi.Div(r.Bins.Count > 2 ? "ctc-choices ctc-choices-grid" : "ctc-choices");
                for (int i = 0; i < r.Bins.Count; i++)
                {
                    int bin = i;
                    var b = CUi.B(r.Bins[i], () => Answer(bin), "ctc-btn ctc-choice", null);
                    b.name = "careers-bin-" + i;
                    bins.Add(b);
                }
                Root = CUi.Div("ctc-engine", _progress, _status, CUi.Div("ctc-card ctc-sort-card", _title, _swatch, _body, _chips), bins, _fb.Root);
            }

            private void Answer(int bin)
            {
                if (!_can()) return;
                var a = _r.Answer(bin);
                if (!a.HasValue) return;
                var x = a.Value;
                CUi.Sfx(x.Correct ? "sfx_ui_success" : "sfx_ui_error");
                _fb.Set(x.Correct, x.Correct ? Str("mg.correct") : Str("mg.wrong") + " · " + Fmt("mg.answer", _r.Bins[x.CorrectBin]), x.Why);
                _after();
            }

            public void Render()
            {
                if (_shownIndex == _r.Index) return;
                _shownIndex = _r.Index;
                _progress.text = Fmt("mg.boardOf", Math.Min(_r.Index + 1, _r.Cards.Count), _r.Cards.Count);
                _status.text = _r.Status ?? "";
                CUi.Show(_status, !string.IsNullOrEmpty(_r.Status));
                var c = _r.Current;
                if (c == null) return;
                _title.text = c.Title ?? "";
                _body.text = c.Body ?? "";
                CUi.Show(_body, !string.IsNullOrEmpty(c.Body));
                _chips.Clear();
                foreach (var chip in c.Chips ?? Array.Empty<string>()) _chips.Add(CUi.Chip(chip));
                CUi.Show(_swatch, c.Swatch != null);
                if (c.Swatch != null)
                {
                    _swatch.style.backgroundColor = CUi.Rgb(c.Swatch.Background);
                    _swatchText.style.color = CUi.Rgb(c.Swatch.Foreground);
                    _swatchText.text = c.Swatch.Sample;
                }
            }
        }

        // ------------------------------------------------------------ Flag

        private sealed class FlagView : IEngineView
        {
            private readonly FlagRound _r;
            private readonly Func<bool> _can;
            private readonly Action _after;
            private readonly Label _progress, _title, _brief, _count;
            private readonly VisualElement _lines;
            private readonly Button _file;
            private readonly Feedback _fb = new Feedback();
            private readonly List<Button> _buttons = new List<Button>();
            private int _shownBoard = -1;
            private bool _dirty = true;
            public VisualElement Root { get; }

            public FlagView(FlagRound r, Func<bool> can, Action after)
            {
                _r = r; _can = can; _after = after;
                _progress = CUi.L("", "ctc-muted");
                _title = CUi.L("", "ctc-card-title");
                _brief = CUi.L("", "ctc-body-text");
                _count = CUi.L("", "ctc-status");
                _lines = CUi.Div("ctc-lines");
                _file = CUi.B(Str("mg.file"), File, "ctc-btn ctc-btn-primary", null);
                _file.name = "careers-flag-file";
                Root = CUi.Div("ctc-engine", _progress, _title, _brief, _count, _lines, CUi.Div("ctc-actions", _file), _fb.Root);
            }

            private void File()
            {
                if (!_can()) return;
                var f = _r.File();
                if (!f.HasValue) return;
                var x = f.Value;
                bool perfect = x.Misses == 0 && x.FalseFlags == 0;
                CUi.Sfx(perfect ? "sfx_ui_success" : "sfx_ui_error");
                var why = new List<string>();
                for (int i = 0; i < x.Board.Lines.Length; i++)
                {
                    var line = x.Board.Lines[i];
                    if (line.Flaw && !x.Flagged[i]) why.Add("Missed: " + line.Text + " — " + line.Why);
                    else if (!line.Flaw && x.Flagged[i]) why.Add("Clean: " + line.Text);
                }
                if (why.Count == 0) why.AddRange(x.Board.Lines.Where(l => l.Flaw).Select(l => l.Why));
                _fb.Set(perfect, Fmt("stat.found") + " " + x.Hits + "/" + x.Board.FlawCount + " · " + Str("stat.falseFlags") + " " + x.FalseFlags, string.Join("\n", why));
                _after();
            }

            public void Render()
            {
                if (!_dirty && _shownBoard == _r.BoardIndex) return;
                _dirty = false;
                _progress.text = Fmt("mg.boardOf", Math.Min(_r.BoardIndex + 1, _r.Boards.Count), _r.Boards.Count);
                if (_shownBoard != _r.BoardIndex)
                {
                    _shownBoard = _r.BoardIndex;
                    _lines.Clear();
                    _buttons.Clear();
                    var b = _r.Board;
                    if (b == null) return;
                    _title.text = b.Title;
                    _brief.text = b.Brief;
                    for (int i = 0; i < b.Lines.Length; i++)
                    {
                        int line = i;
                        var btn = CUi.B("", () => { if (_can() && _r.Toggle(line)) { _dirty = true; CUi.Sfx("sfx_ui_tap"); _after(); } }, "ctc-btn ctc-line" + (b.Code ? " ctc-code" : ""), null);
                        btn.name = "careers-flag-line-" + i;
                        _buttons.Add(btn);
                        _lines.Add(btn);
                    }
                }
                var board = _r.Board;
                if (board == null) return;
                for (int i = 0; i < _buttons.Count; i++)
                {
                    bool on = _r.IsFlagged(i);
                    _buttons[i].text = (on ? "[x] " : "[ ] ") + board.Lines[i].Text;
                    CUi.Mark(_buttons[i], "ctc-selected", on);
                }
                _count.text = Fmt("mg.flags", _r.FlagCount);
            }
        }

        // ------------------------------------------------------------ Sequence

        private sealed class SequenceView : IEngineView
        {
            private readonly SequenceRound _r;
            private readonly Func<bool> _can;
            private readonly Action _after;
            private readonly Label _progress, _title, _brief;
            private readonly VisualElement _placed, _pool;
            private readonly Button _file, _clear;
            private readonly Feedback _fb = new Feedback();
            private int _shownBoard = -1;
            private bool _dirty = true;
            public VisualElement Root { get; }

            public SequenceView(SequenceRound r, Func<bool> can, Action after)
            {
                _r = r; _can = can; _after = after;
                _progress = CUi.L("", "ctc-muted");
                _title = CUi.L("", "ctc-card-title");
                _brief = CUi.L("", "ctc-body-text");
                _placed = CUi.Div("ctc-lines ctc-placed");
                _pool = CUi.Div("ctc-lines");
                _file = CUi.B(Str("mg.file"), File, "ctc-btn ctc-btn-primary", null);
                _file.name = "careers-seq-file";
                _clear = CUi.B(Str("mg.clear"), () => { if (_can()) { _r.Clear(); _dirty = true; _after(); } }, "ctc-btn", "sfx_ui_tap");
                Root = CUi.Div("ctc-engine", _progress, _title, _brief, CUi.L(Str("mg.yourOrder"), "ctc-section"), _placed,
                    CUi.L(Str("mg.tapToAdd"), "ctc-section"), _pool, CUi.Div("ctc-actions", _file, _clear), _fb.Root);
            }

            private void File()
            {
                if (!_can()) return;
                var f = _r.File();
                if (!f.HasValue) return;
                var x = f.Value;
                CUi.Sfx(x.Perfect ? "sfx_ui_success" : "sfx_ui_error");
                var order = x.Board.Items.Select((t, i) => (i + 1) + ". " + t + (x.Board.Why != null && i < x.Board.Why.Length ? " — " + x.Board.Why[i] : ""));
                _fb.Set(x.Perfect, Str("stat.inPlace") + " " + x.InPlace + "/" + x.Board.Items.Length, string.Join("\n", order));
                _after();
            }

            public void Render()
            {
                if (!_dirty && _shownBoard == _r.BoardIndex) return;
                _dirty = false;
                var b = _r.Board;
                _progress.text = Fmt("mg.boardOf", Math.Min(_r.BoardIndex + 1, _r.Boards.Count), _r.Boards.Count);
                if (b == null) return;
                if (_shownBoard != _r.BoardIndex) { _shownBoard = _r.BoardIndex; _title.text = b.Title; _brief.text = b.Brief; }
                _placed.Clear();
                for (int p = 0; p < _r.Placed.Count; p++)
                {
                    int pos = p;
                    _placed.Add(CUi.B((p + 1) + ". " + b.Items[_r.Placed[p]], () => { if (_can() && _r.Unplace(pos)) { _dirty = true; _after(); } }, "ctc-btn ctc-line ctc-selected", "sfx_ui_tap"));
                }
                _pool.Clear();
                foreach (int item in _r.Shown)
                {
                    if (_r.IsPlaced(item)) continue;
                    int it = item;
                    var btn = CUi.B(b.Items[item], () => { if (_can() && _r.Place(it)) { _dirty = true; _after(); } }, "ctc-btn ctc-line", "sfx_ui_tap");
                    btn.name = "careers-seq-item-" + item;
                    _pool.Add(btn);
                }
                _file.SetEnabled(_r.Ready);
            }
        }

        // ------------------------------------------------------------ Match

        private sealed class MatchView : IEngineView
        {
            private readonly MatchRound _r;
            private readonly Func<bool> _can;
            private readonly Action _after;
            private readonly Label _progress, _title, _brief;
            private readonly VisualElement _left, _right;
            private readonly Button _file;
            private readonly Feedback _fb = new Feedback();
            private int _shownBoard = -1;
            private bool _dirty = true;
            public VisualElement Root { get; }

            public MatchView(MatchRound r, Func<bool> can, Action after)
            {
                _r = r; _can = can; _after = after;
                _progress = CUi.L("", "ctc-muted");
                _title = CUi.L("", "ctc-card-title");
                _brief = CUi.L(Str("mg.selectLeft"), "ctc-body-text");
                _left = CUi.Div("ctc-lines");
                _right = CUi.Div("ctc-lines");
                _file = CUi.B(Str("mg.file"), File, "ctc-btn ctc-btn-primary", null);
                _file.name = "careers-match-file";
                Root = CUi.Div("ctc-engine", _progress, _title, _brief, _left, CUi.Div("ctc-divider"), _right, CUi.Div("ctc-actions", _file), _fb.Root);
            }

            private void File()
            {
                if (!_can()) return;
                var f = _r.File();
                if (!f.HasValue) return;
                var x = f.Value;
                bool perfect = x.Correct == x.Board.Left.Length;
                CUi.Sfx(perfect ? "sfx_ui_success" : "sfx_ui_error");
                var lines = new List<string>();
                for (int i = 0; i < x.Board.Left.Length; i++)
                    if (x.Pairs[i] != x.Board.Solution[i])
                        lines.Add(x.Board.Left[i] + " → " + x.Board.Right[x.Board.Solution[i]] + (x.Board.Why != null ? " (" + x.Board.Why[i] + ")" : ""));
                _fb.Set(perfect, Str("stat.pairs") + " " + x.Correct + "/" + x.Board.Left.Length, string.Join("\n", lines));
                _after();
            }

            public void Render()
            {
                if (!_dirty && _shownBoard == _r.BoardIndex) return;
                _dirty = false;
                _shownBoard = _r.BoardIndex;
                var b = _r.Board;
                _progress.text = Fmt("mg.boardOf", Math.Min(_r.BoardIndex + 1, _r.Boards.Count), _r.Boards.Count);
                if (b == null) return;
                _title.text = b.Title + (string.IsNullOrEmpty(b.Brief) ? "" : " · " + b.Brief);
                _left.Clear();
                for (int i = 0; i < b.Left.Length; i++)
                {
                    int left = i;
                    int pair = _r.PairOf(i);
                    string text = (i == _r.SelectedLeft ? "> " : "") + b.Left[i] + "\n" + (pair >= 0 ? Fmt("mg.pairedWith", b.Right[pair]) : "…");
                    var btn = CUi.B(text, () =>
                    {
                        if (!_can()) return;
                        if (_r.PairOf(left) >= 0 && _r.SelectedLeft == left) _r.Unpair(left); else _r.SelectLeft(left);
                        _dirty = true;
                        _after();
                    }, "ctc-btn ctc-line" + (i == _r.SelectedLeft ? " ctc-selected" : "") + (pair >= 0 ? " ctc-paired" : ""), "sfx_ui_tap");
                    btn.name = "careers-match-left-" + i;
                    _left.Add(btn);
                }
                _right.Clear();
                foreach (int right in _r.RightShown)
                {
                    int rr = right;
                    int owner = _r.LeftFor(right);
                    var btn = CUi.B(b.Right[right] + (owner >= 0 ? "\n(" + (owner + 1) + ")" : ""), () => { if (_can() && _r.PairWith(rr)) { _dirty = true; _after(); } },
                        "ctc-btn ctc-line" + (owner >= 0 ? " ctc-paired" : ""), "sfx_ui_tap");
                    btn.name = "careers-match-right-" + right;
                    btn.SetEnabled(_r.SelectedLeft >= 0);
                    _right.Add(btn);
                }
                _file.SetEnabled(_r.Ready);
            }
        }

        // ------------------------------------------------------------ Pick

        private sealed class PickView : IEngineView
        {
            private readonly PickRound _r;
            private readonly Func<bool> _can;
            private readonly Action _after;
            private readonly Label _progress, _title, _brief, _context;
            private readonly VisualElement _options;
            private readonly Feedback _fb = new Feedback();
            private int _shownBoard = -1;
            public VisualElement Root { get; }

            public PickView(PickRound r, Func<bool> can, Action after)
            {
                _r = r; _can = can; _after = after;
                _progress = CUi.L("", "ctc-muted");
                _title = CUi.L("", "ctc-card-title");
                _brief = CUi.L("", "ctc-body-text");
                _context = CUi.L("", "ctc-status");
                _options = CUi.Div("ctc-lines");
                Root = CUi.Div("ctc-engine", _progress, _title, _brief, _context, _options, _fb.Root);
            }

            private void Pick(int option)
            {
                if (!_can()) return;
                var p = _r.Pick(option);
                if (!p.HasValue) return;
                var x = p.Value;
                CUi.Sfx(x.Correct ? "sfx_ui_success" : "sfx_ui_error");
                var reveal = x.Board.Options.Select((o, i) => (i == x.Board.Correct ? "Best: " : "") + o.Label + (string.IsNullOrEmpty(o.Reveal) ? "" : " — " + o.Reveal));
                _fb.Set(x.Correct, x.Correct ? Str("mg.correct") : Str("mg.wrong"), x.Board.Why + "\n" + string.Join("\n", reveal));
                _after();
            }

            public void Render()
            {
                if (_shownBoard == _r.BoardIndex) return;
                _shownBoard = _r.BoardIndex;
                _progress.text = Fmt("mg.boardOf", Math.Min(_r.BoardIndex + 1, _r.Boards.Count), _r.Boards.Count);
                var b = _r.Board;
                _options.Clear();
                if (b == null) return;
                _title.text = b.Title;
                _brief.text = b.Brief;
                _context.text = b.Context ?? "";
                CUi.Show(_context, !string.IsNullOrEmpty(b.Context));
                for (int i = 0; i < b.Options.Length; i++)
                {
                    int o = i;
                    var opt = b.Options[i];
                    var btn = CUi.B(opt.Label + (opt.Facts.Length > 0 ? "\n" + string.Join(" · ", opt.Facts) : ""), () => Pick(o), "ctc-btn ctc-line ctc-option", null);
                    btn.name = "careers-pick-" + i;
                    _options.Add(btn);
                }
            }
        }

        // ------------------------------------------------------------ Combo

        private sealed class ComboView : IEngineView
        {
            private readonly ComboRound _r;
            private readonly Func<bool> _can;
            private readonly Action _after;
            private readonly Label _progress, _title, _brief;
            private readonly VisualElement _slots;
            private readonly Button _file;
            private readonly Feedback _fb = new Feedback();
            private readonly List<List<Button>> _optionButtons = new List<List<Button>>();
            private int _shownBoard = -1;
            private bool _dirty = true;
            public VisualElement Root { get; }

            public ComboView(ComboRound r, Func<bool> can, Action after)
            {
                _r = r; _can = can; _after = after;
                _progress = CUi.L("", "ctc-muted");
                _title = CUi.L("", "ctc-card-title");
                _brief = CUi.L("", "ctc-status");
                _slots = CUi.Div("ctc-slots");
                _file = CUi.B(Str("mg.file"), File, "ctc-btn ctc-btn-primary", null);
                _file.name = "careers-combo-file";
                Root = CUi.Div("ctc-engine", _progress, _title, _brief, _slots, CUi.Div("ctc-actions", _file), _fb.Root);
            }

            private void File()
            {
                if (!_can()) return;
                var f = _r.File();
                if (!f.HasValue) return;
                var x = f.Value;
                CUi.Sfx(x.Passed ? "sfx_ui_success" : "sfx_ui_error");
                string why = x.Passed ? null : (x.FailWhy ?? Fmt("mg.band", x.Board.Min, x.Board.Max, x.Board.Unit));
                _fb.Set(x.Passed, (x.Passed ? Str("mg.pass") : Str("mg.fail")) + " · " + Fmt("mg.total", x.Total, x.Board.Unit), why);
                _after();
            }

            public void Render()
            {
                if (!_dirty && _shownBoard == _r.BoardIndex) return;
                _dirty = false;
                _progress.text = Fmt("mg.boardOf", Math.Min(_r.BoardIndex + 1, _r.Boards.Count), _r.Boards.Count);
                var b = _r.Board;
                if (b == null) return;
                if (_shownBoard != _r.BoardIndex)
                {
                    _shownBoard = _r.BoardIndex;
                    _title.text = b.Title;
                    _brief.text = b.Brief;
                    _slots.Clear();
                    _optionButtons.Clear();
                    for (int s = 0; s < b.Slots.Length; s++)
                    {
                        int slot = s;
                        var row = CUi.Div("ctc-slot-options");
                        var buttons = new List<Button>();
                        for (int o = 0; o < b.Slots[s].Options.Length; o++)
                        {
                            int opt = o;
                            var btn = CUi.B(b.Slots[s].Options[o].Label, () => { if (_can() && _r.Choose(slot, opt)) { _dirty = true; _after(); } }, "ctc-btn ctc-slot-option", "sfx_ui_tap");
                            btn.name = "careers-combo-" + s + "-" + o;
                            buttons.Add(btn);
                            row.Add(btn);
                        }
                        _optionButtons.Add(buttons);
                        _slots.Add(CUi.Div("ctc-slot", CUi.L(b.Slots[s].Name, "ctc-slot-name"), row));
                    }
                }
                for (int s = 0; s < _optionButtons.Count; s++)
                    for (int o = 0; o < _optionButtons[s].Count; o++)
                        CUi.Mark(_optionButtons[s][o], "ctc-selected", _r.ChoiceOf(s) == o);
                _file.SetEnabled(_r.Ready);
            }
        }

        // ------------------------------------------------------------ Timing

        private sealed class TimingView : IEngineView
        {
            private const int Bars = 40;
            private readonly TimingRound _r;
            private readonly Func<bool> _can;
            private readonly Action _after;
            private readonly Label _progress, _title, _brief, _readout, _tell, _note;
            private readonly VisualElement _chart;
            private readonly VisualElement[] _bars = new VisualElement[Bars];
            private readonly Button _act;
            private readonly Feedback _fb = new Feedback();
            private int _shownWindow = -1, _shownSample = -1, _shownResults = -1;
            private bool _shownActed;
            public VisualElement Root { get; }

            public TimingView(TimingRound r, Func<bool> can, Action after, string actLabel)
            {
                _r = r; _can = can; _after = after;
                _progress = CUi.L("", "ctc-muted");
                _title = CUi.L("", "ctc-card-title");
                _brief = CUi.L("", "ctc-body-text");
                _readout = CUi.L("", "ctc-readout");
                _tell = CUi.L("", "ctc-tell");
                _note = CUi.L("", "ctc-status");
                _chart = CUi.Div("ctc-chart");
                for (int i = 0; i < Bars; i++)
                {
                    _bars[i] = CUi.Div("ctc-bar");
                    _bars[i].pickingMode = PickingMode.Ignore;
                    _chart.Add(_bars[i]);
                }
                _act = CUi.B(string.IsNullOrEmpty(actLabel) ? "Act" : actLabel, Act, "ctc-btn ctc-btn-primary ctc-act", null);
                _act.name = "careers-timing-act";
                Root = CUi.Div("ctc-engine", _progress, _title, _brief, _readout, _tell, _note, _chart, CUi.Div("ctc-actions", _act), _fb.Root);
            }

            private void Act()
            {
                if (!_can()) return;
                var res = _r.Act();
                if (!res.HasValue) return;
                CUi.Sfx(res.Value.Points >= 0.8 ? "sfx_ui_success" : res.Value.Points > 0 ? "sfx_ui_tap" : "sfx_ui_error");
                _after();
            }

            public void Render()
            {
                var w = _r.Window;
                bool actedNow = _r.Acted;
                if (_shownWindow == _r.WindowIndex && _shownSample == _r.Sample && _shownResults == _r.Results.Count && _shownActed == actedNow) return;
                _shownActed = actedNow;
                _progress.text = Fmt("mg.boardOf", Math.Min(_r.WindowIndex + 1, _r.Windows.Count), _r.Windows.Count);
                if (_shownResults != _r.Results.Count)
                {
                    _shownResults = _r.Results.Count;
                    var last = _r.Last;
                    if (last.HasValue)
                    {
                        var x = last.Value;
                        if (!x.Acted) _fb.Set(x.Best <= 0, x.Best <= 0 ? Str("mg.passedRight") : Str("mg.missedWindow"), null);
                        else if (x.Payoff <= 0) _fb.Set(false, Str("mg.loss"), null);
                        else _fb.Set(x.Points >= 0.8, Fmt("mg.captured", (int)Math.Round(100 * x.Points)), null);
                    }
                }
                if (w == null) return;
                if (_shownWindow != _r.WindowIndex) { _shownWindow = _r.WindowIndex; _title.text = w.Title; _brief.text = w.Brief ?? ""; _shownSample = -1; }
                int s = _r.Sample;
                _act.SetEnabled(!actedNow);
                if (s == _shownSample) return;
                _shownSample = s;
                _readout.text = _r.Readout ?? _r.Signal.ToString("0.00", ContentKit.Inv);
                _tell.text = _r.Tell ? (w.TellText ?? "!") : "";
                CUi.Show(_tell, _r.Tell);
                _note.text = _r.Note ?? "";
                CUi.Show(_note, !string.IsNullOrEmpty(_r.Note));
                // Normalise by what has been seen so far: never leak the window's future range.
                int from = Math.Max(0, s - Bars + 1);
                double lo = double.MaxValue, hi = double.MinValue;
                for (int i = 0; i <= s; i++) { lo = Math.Min(lo, w.Signal[i]); hi = Math.Max(hi, w.Signal[i]); }
                double span = Math.Max(1e-6, hi - lo);
                for (int b = 0; b < Bars; b++)
                {
                    int i = from + b;
                    bool on = i <= s;
                    CUi.Show(_bars[b], on);
                    if (on) _bars[b].style.height = Length.Percent((float)(8 + 92 * (w.Signal[i] - lo) / span));
                }
            }
        }

        // ------------------------------------------------------------ Balance

        private sealed class BalanceView : IEngineView
        {
            private readonly BalanceRound _r;
            private readonly Func<bool> _can;
            private readonly Action _after;
            private readonly CUi.Meter _meter;
            private readonly Label _value, _band, _note, _cue;
            private readonly Button[] _actions;
            private readonly int[] _shownCooldown;
            private int _shownValue = int.MinValue;
            private bool? _shownInBand;
            private BalanceCue _shownCue;
            private int _shownCueLeft = -1;
            private string _shownNote;
            public VisualElement Root { get; }

            public BalanceView(BalanceRound r, Func<bool> can, Action after)
            {
                _r = r; _can = can; _after = after;
                _shownCooldown = Enumerable.Repeat(-1, r.Spec.Actions.Length).ToArray();
                var s = r.Spec;
                _meter = new CUi.Meter(s.Min, s.Max, s.BandLo, s.BandHi, fill: s.Min >= 0);
                _value = CUi.L("", "ctc-readout");
                _band = CUi.L("", "ctc-status");
                _note = CUi.L("", "ctc-status");
                _cue = CUi.L("", "ctc-tell");
                _actions = new Button[s.Actions.Length];
                var row = CUi.Div(s.Actions.Length > 2 ? "ctc-choices ctc-choices-grid" : "ctc-choices");
                for (int i = 0; i < s.Actions.Length; i++)
                {
                    int a = i;
                    _actions[i] = CUi.B(s.Actions[i].Label, () => Apply(a), "ctc-btn ctc-choice", null);
                    _actions[i].name = "careers-balance-" + i;
                    row.Add(_actions[i]);
                }
                Root = CUi.Div("ctc-engine", CUi.L(s.MeterLabel, "ctc-card-title"),
                    CUi.Div("ctc-meter-labels", CUi.L(s.LowLabel, "ctc-muted"), CUi.Div("ctc-grow"), CUi.L(s.HighLabel, "ctc-muted")),
                    _meter.Root, _value, _band, _cue, _note, row);
            }

            private void Apply(int action)
            {
                if (!_can()) return;
                var outcome = _r.Apply(action);
                if (!outcome.HasValue) return;
                CUi.Sfx(outcome.Value == BalanceOutcome.Misfire ? "sfx_ui_error" : outcome.Value == BalanceOutcome.Answered ? "sfx_ui_success" : "sfx_ui_tap");
                _after();
            }

            // Called every frame: only touch labels when what they show actually changes (no per-frame strings).
            public void Render()
            {
                _meter.Set(_r.Value);
                int value = (int)Math.Round(_r.Value);
                if (value != _shownValue) { _shownValue = value; _value.text = _r.Spec.MeterLabel + " " + value.ToString(ContentKit.Inv); }
                bool inBand = _r.InBand;
                if (_shownInBand != inBand)
                {
                    _shownInBand = inBand;
                    _band.text = Str(inBand ? "mg.inBand" : "mg.outBand");
                    CUi.Mark(_band, "ctc-good", inBand);
                    CUi.Mark(_band, "ctc-bad", !inBand);
                }
                var cue = _r.ActiveCue;
                int cueLeft = cue == null ? -1 : (int)Math.Ceiling(_r.CueTimeLeft);
                if (!ReferenceEquals(cue, _shownCue) || cueLeft != _shownCueLeft)
                {
                    _shownCue = cue;
                    _shownCueLeft = cueLeft;
                    _cue.text = cue == null ? "" : Fmt("mg.cue", cue.Text, cueLeft);
                    CUi.Show(_cue, cue != null);
                }
                var note = _r.Note;
                if (!ReferenceEquals(note, _shownNote))
                {
                    _shownNote = note;
                    _note.text = note ?? "";
                    CUi.Show(_note, !string.IsNullOrEmpty(note));
                }
                for (int i = 0; i < _actions.Length; i++)
                {
                    double cd = _r.CooldownLeft(i);
                    int shown = cd > 0.3 ? (int)Math.Ceiling(cd) : 0;
                    if (shown == _shownCooldown[i]) continue;
                    _shownCooldown[i] = shown;
                    _actions[i].text = shown > 0 ? Fmt("mg.cooldown", _r.Spec.Actions[i].Label, shown) : _r.Spec.Actions[i].Label;
                }
                for (int i = 0; i < _actions.Length; i++)
                {
                    bool ready = _r.CooldownLeft(i) <= 1e-9;
                    if (_actions[i].enabledSelf != ready) _actions[i].SetEnabled(ready);
                }
            }
        }
    }
}
