using System;
using System.Collections.Generic;
using System.Globalization;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>
    /// The launch sheet (brief beats 2-3): one plum-glass sheet on the right, the world on the left. Steps ART (4 x 3 coin
    /// faces), NAME (name + $TICKER at the top, inline errors), BAG (liquidity slider + chips, dev-bag slider with the live
    /// split bar, the rug meter with the heat range and stars, the total) and REVIEW (HOLD TO LAUNCH). Every choice updates
    /// the 3D coin over the desk. Cash only; the coin's outcome is hidden server-side and nothing here suggests otherwise.
    /// </summary>
    public sealed class FactoryLaunchSheet : VisualElement
    {
        public enum Step { Art, Name, Bag, Review }
        public static readonly long[] LiquidityChips = { 1_000, 5_000, 25_000, 100_000 };

        private readonly FactoryService _svc;
        private readonly FactoryDeskStage _stage;
        private readonly Action _closed;
        private readonly Action<LaunchDraft> _launch;
        public readonly LaunchDraft Draft;
        public Step Current { get; private set; }

        private readonly VisualElement _body, _rail;
        private readonly Label _cash, _footNote;
        private readonly Button _next;
        private TextField _name, _ticker;
        private Label _nameErr, _tickerErr, _nameCount, _tickerCount;
        private string _nameError, _tickerError, _bagError, _reviewError;
        private HoldToLaunch _hold;
        public HoldToLaunch Hold => _hold;

        public FactoryLaunchSheet(FactoryService svc, FactoryDeskStage stage, Action<LaunchDraft> launch, Action closed)
        {
            _svc = svc; _stage = stage; _launch = launch; _closed = closed;
            var cfg = svc.Config;
            Draft = new LaunchDraft { TemplateId = cfg.Templates.Count > 0 ? cfg.Templates[0] : "rf_frog", LiquidityCents = 25_000, DevAllocationBps = 0 };
            Draft.LiquidityCents = Math.Max(cfg.MinLiquidityCents, Math.Min(cfg.MaxLiquidityCents, Draft.LiquidityCents));
            AddToClassList("rf-sheet");
            name = "factory-sheet";
            var back = new Button(Back) { name = "factory-back" };
            back.AddToClassList("rf-back");
            back.Add(new UiIcon("back"));
            _cash = El.Text("").Named("factory-cash");
            _cash.AddToClassList("rf-cash");
            var title = El.Text(FactoryText.T("ui.title"));
            title.AddToClassList("rf-title");
            var head = El.Div("rf-head", back, title, _cash);
            _rail = El.Div("rf-rail");
            _body = El.Div("rf-body");
            _footNote = El.Text("");
            _footNote.AddToClassList("rf-foot-note");
            _next = new Button(Next) { name = "factory-next" };
            _next.AddToClassList("rf-next");
            Add(head); Add(_rail); Add(_body); Add(El.Div("rf-foot", _footNote, _next));
            schedule.Execute(RenderCash).Every(1000);
            RenderCash();
            _stage?.SetArt(Draft.TemplateId);
            Go(Step.Art);
            schedule.Execute(() => AddToClassList("rf-in")).ExecuteLater(30);
        }

        private static long? Cash => Services.TryGet<PlayerProfile>(out var p) && p.Me != null ? p.CashCents : (long?)null;
        private void RenderCash() => _cash.text = Cash is long c ? MoneyFormat.Cash(c) : "";

        public static string Dollars(long cents) =>
            cents % 100 == 0 ? "$" + (cents / 100).ToString("N0", CultureInfo.InvariantCulture) : MoneyFormat.Cash(cents);

        // ------------------------------------------------------------------ navigation

        public void Go(Step step, string error = null)
        {
            if (_name != null) SoftKeyboard.Hide(_name);
            if (_ticker != null) SoftKeyboard.Hide(_ticker);
            Current = step;
            if (step == Step.Review) _reviewError = error;
            RenderRail();
            _body.Clear();
            _footNote.text = "";
            switch (step)
            {
                case Step.Art: RenderArt(); break;
                case Step.Name: RenderName(); break;
                case Step.Bag: RenderBag(); break;
                case Step.Review: RenderReview(); break;
            }
            _next.Show(step != Step.Review);
            _next.text = FactoryText.T(step == Step.Bag ? "ui.review" : "ui.next");
            Validate();
        }

        /// <summary>The server refused: back to the step that fixes it, with the message.</summary>
        public void ShowRefusal(FactoryError err, FactoryCheck check)
        {
            string msg = !check.Ok ? FactoryText.F(check.Key, check.Args) : err?.Message;
            string key = !check.Ok ? check.Key : err?.Key;
            RemoveFromClassList("rf-out");
            AddToClassList("rf-in");
            if (key == "err.name" || key != null && key.StartsWith("check.name")) { _nameError = msg; Go(Step.Name); }
            else if (key == "err.taken" || key != null && key.StartsWith("check.ticker")) { _tickerError = msg; Go(Step.Name); }
            else if (key == "err.cash" || key == "check.liquidity" || key == "check.dev" || key == "check.art") { _bagError = msg; Go(Step.Bag); }
            else Go(Step.Review, msg);
        }

        public void SlideOut() { RemoveFromClassList("rf-in"); }

        private void Back()
        {
            switch (Current)
            {
                case Step.Art: _closed?.Invoke(); break;
                case Step.Name: Go(Step.Art); break;
                case Step.Bag: Go(Step.Name); break;
                case Step.Review: Go(Step.Bag); break;
            }
        }

        private void Next()
        {
            if (!Validate()) return;
            Bus.Publish(new SfxSignal("sfx_ui_tap"));
            Go(Current == Step.Art ? Step.Name : Current == Step.Name ? Step.Bag : Step.Review);
        }

        private bool Validate()
        {
            var cfg = _svc.Config;
            bool ok = Current switch
            {
                Step.Art => cfg.HasTemplate(Draft.TemplateId),
                Step.Name => FactoryRules.CheckName(Draft.Name, cfg).Ok && FactoryRules.CheckTicker(Draft.Ticker, cfg).Ok,
                Step.Bag => true,
                _ => true,
            };
            _next.SetEnabled(ok);
            return ok;
        }

        private void RenderRail()
        {
            _rail.Clear();
            var steps = new[] { (Step.Art, "ui.step.art"), (Step.Name, "ui.step.name"), (Step.Bag, "ui.step.bag") };
            for (int i = 0; i < steps.Length; i++)
            {
                var (s, key) = steps[i];
                bool done = Current > s, on = Current == s;
                var n = El.Div("rf-step-n", El.Text(done ? "✓" : (i + 1).ToString(CultureInfo.InvariantCulture)));
                var label = El.Text(FactoryText.T(key));
                label.AddToClassList("rf-step-label");
                var step = El.Div("rf-step", n, label);
                step.EnableInClassList("rf-step--on", on);
                step.EnableInClassList("rf-step--done", done);
                _rail.Add(step);
                if (i < steps.Length - 1)
                {
                    var line = El.Div("rf-step-line");
                    line.EnableInClassList("rf-step-line--done", done);
                    _rail.Add(line);
                }
            }
        }

        // ------------------------------------------------------------------ ART

        private void RenderArt()
        {
            var grid = El.Div("rf-grid");
            grid.name = "factory-art-grid";
            foreach (var id in _svc.Config.Templates)
            {
                var art = id;
                var tile = new Button(() =>
                {
                    Draft.TemplateId = art;
                    _stage?.SetArt(art);
                    Bus.Publish(new SfxSignal("sfx_ui_tap"));
                    Go(Step.Art);
                }) { name = "factory-art-" + art };
                tile.AddToClassList("rf-tile");
                tile.EnableInClassList("rf-tile--on", Draft.TemplateId == art);
                tile.Add(FactoryArt.Image(art, "rf-tile-art"));
                var n = El.Text(FactoryArt.Name(art).ToUpperInvariant());
                n.AddToClassList("rf-tile-name");
                tile.Add(n);
                grid.Add(tile);
            }
            _body.Add(grid);
        }

        // ------------------------------------------------------------------ NAME

        private void RenderName()
        {
            var cfg = _svc.Config;
            _nameCount = Small("rf-count");
            _name = Field("factory-name", Draft.Name, cfg.NameMax, v =>
            {
                Draft.Name = v; _nameError = null; RefreshName();
            });
            _nameErr = Small("rf-error");
            _body.Add(El.Div("rf-field-head", Small("rf-label", FactoryText.T("ui.name")), _nameCount));
            _body.Add(Row(null, _name, _nameError != null));
            _body.Add(_nameErr);

            _tickerCount = Small("rf-count");
            _ticker = Field("factory-ticker", Draft.Ticker, cfg.TickerMax, v =>
            {
                var up = FactoryRules.NormaliseTicker(v);
                if (up != v) _ticker.SetValueWithoutNotify(up);
                Draft.Ticker = up; _tickerError = null; _stage?.SetTicker(up); RefreshName();
            });
            _tickerErr = Small("rf-error");
            _body.Add(El.Div("rf-field-head", Small("rf-label", FactoryText.T("ui.ticker")), _tickerCount));
            var dollar = El.Text("$");
            dollar.AddToClassList("rf-dollar");
            _body.Add(Row(dollar, _ticker, _tickerError != null));
            _body.Add(_tickerErr);
            _footNote.text = FactoryText.T("ui.nameNote");
            RefreshName();
        }

        private void RefreshName()
        {
            var cfg = _svc.Config;
            var n = FactoryRules.NormaliseName(Draft.Name);
            var t = FactoryRules.NormaliseTicker(Draft.Ticker);
            if (_nameCount != null) _nameCount.text = n.Length + "/" + cfg.NameMax;
            if (_tickerCount != null) _tickerCount.text = t.Length + "/" + cfg.TickerMax;
            // shape hints only once something is typed; the server's refusals win
            var nc = n.Length > 0 ? FactoryRules.CheckName(Draft.Name, cfg) : FactoryCheck.Pass;
            var tc = t.Length > 0 ? FactoryRules.CheckTicker(Draft.Ticker, cfg) : FactoryCheck.Pass;
            Set(_nameErr, _nameError ?? (nc.Ok ? null : FactoryText.F(nc.Key, nc.Args)));
            Set(_tickerErr, _tickerError ?? (tc.Ok ? null : FactoryText.F(tc.Key, tc.Args)));
            _name?.parent?.EnableInClassList("rf-input-row--bad", _nameErr != null && _nameErr.text.Length > 0);
            _ticker?.parent?.EnableInClassList("rf-input-row--bad", _tickerErr != null && _tickerErr.text.Length > 0);
            Validate();
        }

        private static void Set(Label l, string text)
        {
            if (l == null) return;
            l.text = text ?? "";
            l.Show(!string.IsNullOrEmpty(text));
        }

        private static TextField Field(string name, string value, int max, Action<string> changed)
        {
            var f = new TextField { name = name, maxLength = max, isDelayed = false, value = value ?? "" };
            f.AddToClassList("rf-input");
            f.textEdition.hideMobileInput = false;
            f.RegisterValueChangedCallback(e => changed(e.newValue ?? ""));
            return f;
        }

        private static VisualElement Row(VisualElement lead, TextField field, bool bad)
        {
            var row = El.Div("rf-input-row");
            if (lead != null) row.Add(lead);
            row.Add(field);
            row.EnableInClassList("rf-input-row--bad", bad);
            return row;
        }

        // ------------------------------------------------------------------ BAG

        private void RenderBag()
        {
            var cfg = _svc.Config;
            var liqValue = El.Text(Dollars(Draft.LiquidityCents)).Named("factory-liquidity");
            liqValue.AddToClassList("rf-value"); liqValue.AddToClassList("rf-value--green");
            _body.Add(El.Div("rf-row", Small("rf-label", FactoryText.T("ui.liquidity")), liqValue));
            var liq = new Slider(0f, 1f) { name = "factory-liquidity-slider", value = LiquidityToT(Draft.LiquidityCents, cfg) };
            liq.AddToClassList("rf-slider"); liq.AddToClassList("rf-slider--green");
            _body.Add(liq);
            Action refresh = null;
            var chips = El.Div("rf-chips");
            var chipButtons = new List<(Button b, long c)>();
            foreach (var c in LiquidityChips)
            {
                if (c < cfg.MinLiquidityCents || c > cfg.MaxLiquidityCents) continue;
                var cents = c;
                var b = new Button(() => { Draft.LiquidityCents = cents; liq.SetValueWithoutNotify(LiquidityToT(cents, cfg)); refresh?.Invoke(); }) { text = Dollars(c) };
                b.AddToClassList("rf-chip");
                chips.Add(b);
                chipButtons.Add((b, c));
            }
            _body.Add(chips);

            var bagValue = El.Text("").Named("factory-bag");
            bagValue.AddToClassList("rf-value"); bagValue.AddToClassList("rf-value--yellow");
            _body.Add(El.Div("rf-row", Small("rf-label", FactoryText.T("ui.bag")), bagValue));
            int maxPct = cfg.MaxDevAllocationBps / 100;
            var bag = new SliderInt(0, Math.Max(0, maxPct)) { name = "factory-bag-slider", value = Draft.DevAllocationBps / 100 };
            bag.AddToClassList("rf-slider"); bag.AddToClassList("rf-slider--yellow");
            _body.Add(bag);
            var youLabel = El.Text(""); var floatLabel = El.Text("");
            var you = El.Div("rf-split-you", youLabel);
            var split = El.Div("rf-split", you, El.Div("rf-split-float", floatLabel));
            _body.Add(split);

            var rugWord = Small("rf-rug-word", FactoryText.T("ui.rug"));
            _body.Add(El.Div("rf-row", Small("rf-label", FactoryText.T("ui.ifSell")), rugWord));
            var meter = El.Div("rf-meter");
            var segs = new VisualElement[4];
            for (int i = 0; i < 4; i++) { segs[i] = El.Div("rf-seg"); meter.Add(segs[i]); }
            var heatText = Small("rf-meter-heat");
            var stars = El.Div("rf-stars");
            _body.Add(El.Div("rf-meter-row", meter, heatText, stars));
            var note = Small("rf-note");
            _body.Add(note);
            var err = Small("rf-error");
            _body.Add(err);
            var total = Small("rf-total");
            var totalDetail = Small("rf-total-detail");
            _footNote.text = "";
            _footNote.Clear();
            _footNote.Add(El.Div("rf-row", total, totalDetail));

            void Refresh()
            {
                liqValue.text = Dollars(Draft.LiquidityCents);
                foreach (var (b, c) in chipButtons) b.EnableInClassList("rf-chip--on", c == Draft.LiquidityCents);
                int pct = Draft.DevAllocationBps / 100;
                bagValue.text = pct + " %";
                you.style.width = new Length(pct, LengthUnit.Percent);
                you.Show(pct > 0);
                bool roomy = pct >= 14;
                youLabel.text = roomy ? FactoryText.F("ui.you", pct) : "";
                floatLabel.text = roomy ? FactoryText.F("ui.float", 100 - pct) : FactoryText.F("ui.you", pct) + " · " + FactoryText.F("ui.float", 100 - pct);
                int heatNow = _svc.Me?.Heat ?? 0;
                var (lo, hi, sLo, sHi) = RugRange(heatNow);
                int on = pct == 0 ? 0 : LevelIndex(heatNow + hi) + 1;
                for (int i = 0; i < 4; i++) segs[i].EnableInClassList("rf-seg--on", i < on);
                rugWord.Show(pct > 0);
                heatText.text = pct == 0 ? "" : FactoryText.F("ui.heatRange", lo, hi);
                stars.Clear();
                if (pct > 0)
                    for (int i = 0; i < 5; i++)
                    {
                        var s = new UiIcon(i < sHi ? "star" : "star-outline");
                        s.AddToClassList("rf-star");
                        if (i >= sHi) s.AddToClassList("rf-star--off");
                        stars.Add(s);
                    }
                note.text = pct == 0 ? FactoryText.T("ui.noBag") : FactoryText.F("ui.bagPublic", pct);
                Set(err, _bagError);
                long cost = FactoryRules.TotalCostCents(Draft, cfg);
                total.text = FactoryText.F("ui.total", Dollars(cost));
                totalDetail.text = FactoryText.F("ui.totalDetail", Dollars(Draft.LiquidityCents), Dollars(cfg.LaunchFeeCents));
            }

            refresh = Refresh;
            liq.RegisterValueChangedCallback(e => { Draft.LiquidityCents = TToLiquidity(e.newValue, cfg); _bagError = null; Refresh(); });
            bag.RegisterValueChangedCallback(e => { Draft.DevAllocationBps = Math.Max(0, Math.Min(cfg.MaxDevAllocationBps, e.newValue * 100)); Refresh(); });
            Refresh();
        }

        /// <summary>Heat after a flagged dump (backend: +35 to +50 on top of the current heat, capped at 100) and the stars at
        /// both ends (HeatRules: none below 10, then one per 20). Pure; tested.</summary>
        public static (int lo, int hi, int starsLo, int starsHi) RugRange(int currentHeat)
        {
            int lo = Math.Min(100, Math.Max(0, currentHeat) + 35), hi = Math.Min(100, Math.Max(0, currentHeat) + 50);
            return (lo - currentHeat, hi - currentHeat, Stars(lo), Stars(hi));
        }

        public static int Stars(double heat) => heat < 10 ? 0 : Math.Max(1, Math.Min(5, (int)Math.Ceiling(heat / 20.0)));

        /// <summary>The level a heat value reads as: cool &lt;10, warm &lt;35, hot &lt;70, wanted (backend /heat/me levels). Pure.</summary>
        public static int LevelIndex(int heat) => heat >= 70 ? 3 : heat >= 35 ? 2 : heat >= 10 ? 1 : 0;

        /// <summary>The liquidity slider is logarithmic ($10 to $1,000 by default), snapped to $1 / $5 / $10 steps. Pure; tested.</summary>
        public static long TToLiquidity(float t, FactoryConfig cfg)
        {
            double min = cfg.MinLiquidityCents, max = cfg.MaxLiquidityCents;
            double v = min * Math.Pow(max / min, Mathf.Clamp01(t));
            long step = v < 10_000 ? 100 : v < 50_000 ? 500 : 1_000;
            long c = (long)Math.Round(v / step) * step;
            return Math.Max(cfg.MinLiquidityCents, Math.Min(cfg.MaxLiquidityCents, c));
        }

        public static float LiquidityToT(long cents, FactoryConfig cfg)
        {
            double min = cfg.MinLiquidityCents, max = cfg.MaxLiquidityCents;
            if (max <= min) return 0f;
            return Mathf.Clamp01((float)(Math.Log(Math.Max(min, cents) / min) / Math.Log(max / min)));
        }

        // ------------------------------------------------------------------ REVIEW

        private void RenderReview()
        {
            var cfg = _svc.Config;
            var t = FactoryRules.NormaliseTicker(Draft.Ticker);
            var title = El.Text("$" + t + " · " + FactoryRules.NormaliseName(Draft.Name)).Named("factory-review-title");
            title.AddToClassList("rf-review-title");
            _body.Add(title);
            _body.Add(Small("rf-review-line", FactoryText.F("ui.reviewArt", FactoryArt.Name(Draft.TemplateId), Dollars(Draft.LiquidityCents), Draft.DevAllocationBps / 100)));
            var me = _svc.Me;
            int nth = (me?.LaunchesToday ?? 0) + 1, cap = me?.DailyLaunchCap ?? cfg.DailyLaunchCap;
            _body.Add(Small("rf-review-line", FactoryText.F("ui.reviewTotal", Dollars(FactoryRules.TotalCostCents(Draft, cfg)), nth, cap)));
            var check = FactoryRules.Check(Draft, cfg, Cash, me, _svc.NowMs);
            var err = Small("rf-error");
            Set(err, _reviewError ?? (check.Ok ? null : FactoryText.F(check.Key, check.Args)));
            _body.Add(err);
            _hold = new HoldToLaunch(FactoryText.T("ui.hold"), () => _launch?.Invoke(Draft));
            _hold.SetEnabled(check.Ok && !_svc.Launching);
            var hint = Small("rf-hold-hint", FactoryText.T("ui.holdHint"));
            _body.Add(El.Div("rf-hold-wrap", _hold, hint));
        }

        private static Label Small(string cls, string text = "")
        {
            var l = El.Text(text);
            l.AddToClassList(cls);
            return l;
        }
    }
}
