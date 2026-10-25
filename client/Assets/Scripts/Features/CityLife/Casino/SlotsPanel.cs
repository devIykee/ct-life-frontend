using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;
using Motion = CTLife.UI.Kit.Motion;

namespace CTLife.CityLife.Casino
{
    /// <summary>
    /// Slots (`POST /casino/slots/spin`): a brass cabinet with a bulb marquee and three reels of our CT symbols. SPIN pulls the
    /// lever: the reels kick back and blur up to speed while the bet is in flight; once the server answers, they land one by
    /// one on `outcome.reels` (the middle row is the payline) with a small overshoot and a clunk. A win lights the payline and
    /// the paying symbols and counts the amount up; three of a kind at 10x or more, or three WILDs, is the big-win show (rays,
    /// coins, confetti). The client decides nothing: the reels only show what the server drew.
    /// </summary>
    public sealed class SlotsPanel : RugPanel
    {
        private const float SpinSpeed = 19f, MinSpin = 0.85f, StopGap = 0.32f, Overshoot = 0.2f;
        private const string LoopKey = "casino_reels";

        private readonly Reel[] _reels = new Reel[3];
        private readonly VisualElement _payline, _marquee, _bulbs, _burstHost;
        private readonly Label _marqueeLabel, _win, _lose;
        private readonly StakeChips _chips;
        private readonly Button _spin;
        private readonly Label _spinSub;
        private readonly CelebrationBurst _burst;
        private readonly List<VisualElement> _bulbEls = new List<VisualElement>();
        private VisualElement _bigWin;
        private bool _busy, _requesting;
        private string _key;
        private CasinoBet _result;
        private CityError _error;
        private float _spinAt, _t;
        private int _stoppedCount;
        private float _winFlash;

        public override bool Busy => _busy;

        public SlotsPanel(RugRoomSheet sheet) : base(sheet)
        {
            name = "rr-slots";
            // ---- the cabinet
            var main = El.Div("rr-main");
            var cab = El.Div("rr-cab");
            _marquee = El.Div("rr-marquee");
            _bulbs = El.Div("rr-bulbs");
            _bulbs.pickingMode = PickingMode.Ignore;
            _marquee.Add(_bulbs);
            _marqueeLabel = El.Text("THE RUG ROOM", "rr-marquee-label");
            _marquee.Add(_marqueeLabel);
            _marquee.RegisterCallback<GeometryChangedEvent>(_ => LayoutBulbs());
            cab.Add(_marquee);
            var frame = El.Div("rr-frame");
            var window = El.Div("rr-window");
            for (int i = 0; i < 3; i++)
            {
                _reels[i] = new Reel(this, i);
                window.Add(_reels[i].Root);
            }
            frame.Add(window);
            _payline = El.Div("rr-payline");
            _payline.pickingMode = PickingMode.Ignore;
            var l = El.Div("rr-payline-tab rr-payline-tab--l"); var r = El.Div("rr-payline-tab rr-payline-tab--r");
            window.Add(_payline);
            frame.Add(l); frame.Add(r);
            cab.Add(frame);
            _win = El.Text("", "rr-win-label");
            _lose = El.Text(CasinoText.T("slots.idle"), "rr-lose-label");
            cab.Add(El.Div("rr-cab-base", _win, _lose));
            main.Add(cab);
            Add(main);

            // ---- the side: paytable, stake, SPIN
            var side = El.Div("rr-side");
            var pay = El.Div("rr-card");
            pay.Add(Cap(CasinoText.T("slots.pays")));
            var grid = El.Div("rr-pay");
            grid.name = "rr-paytable";
            pay.Add(grid);
            pay.Add(El.Text(CasinoText.T("slots.wildNote"), "rr-note"));
            side.Add(pay);
            var bet = El.Div("rr-card");
            bet.style.marginTop = 18;
            _chips = new StakeChips(sheet);
            _chips.Changed += RefreshCta;
            bet.Add(_chips);
            _spin = RugRoomSheet.Btn("", Spin, "rr-cta rr-cta--gold");
            _spin.name = "rr-spin";
            _spin.Add(El.Text(CasinoText.T("slots.spin"), "rr-cta-label"));
            _spinSub = El.Text("", "rr-cta-sub");
            _spin.Add(_spinSub);
            foreach (var e in _spin.Children()) e.pickingMode = PickingMode.Ignore;
            bet.Add(_spin);
            side.Add(bet);
            Add(side);

            _burstHost = El.Div();
            _burstHost.pickingMode = PickingMode.Ignore;
            _burstHost.style.position = Position.Absolute; _burstHost.style.left = 0; _burstHost.style.right = 0; _burstHost.style.top = 0; _burstHost.style.bottom = 0;
            Add(_burstHost);
            _burst = new CelebrationBurst(coins: 36, confetti: 90, puffs: 0);
            _burstHost.Add(_burst);
            BuildPaytable(grid);
            RefreshCta();
        }

        private void BuildPaytable(VisualElement grid)
        {
            grid.Clear();
            var s = State;
            var order = new[] { "rocket", "wild", "laser_eyes", "diamond_hands", "ape", "rug" };
            foreach (var sym in order)
            {
                double three = s != null && s.PayThree.TryGetValue(sym, out var t3) ? t3 : Default3(sym);
                double two = s != null && s.PayTwo.TryGetValue(sym, out var t2) ? t2 : Default2(sym);
                var art = El.Div("rr-pay-art");
                var tex = RugRoomSheet.Symbol(sym);
                if (tex != null) art.style.backgroundImage = new StyleBackground(tex);
                var row = El.Div("rr-pay-row", art, El.Text(three > 0 ? three.ToString("0.##") + "×" : "0", "rr-pay-x"));
                if (two > 0) row.Add(El.Text("pair " + two.ToString("0.##") + "×", "rr-pay-two"));
                if (sym == "rug") row.Add(El.Text("never pays", "rr-pay-two"));
                grid.Add(row);
            }
        }

        private static double Default3(string s) => s switch { "rocket" => 30, "laser_eyes" => 15, "diamond_hands" => 10, "ape" => 5, "wild" => 25, _ => 0 };
        private static double Default2(string s) => s switch { "rocket" => 1.5, "laser_eyes" => 1.5, "diamond_hands" => 1, "ape" => 0.5, _ => 0 };

        public override void StateChanged()
        {
            _chips.Refresh();
            var grid = this.Q("rr-paytable");
            if (grid != null && State != null && grid.userData == null) { BuildPaytable(grid); grid.userData = true; }
            RefreshCta();
        }

        private void RefreshCta()
        {
            _spinSub.text = CasinoText.Money(Sheet.StakeCents);
            _spin.EnableInClassList("rr-cta--busy", _busy);
        }

        // ------------------------------------------------------------------ the spin

        private void Spin()
        {
            if (_busy) return;
            var s = State;
            if (s == null || !s.AgeConfirmed || s.Excluded(ServerClock.NowMs)) { Cues.Nope(); return; }
            long stake = Sheet.StakeCents;
            if (stake > s.StakeRoomCents) { Sheet.Toast(CasinoText.T(s.StakeRemainingCents < stake ? "err.STAKE_CAP_REACHED" : "err.LOSS_CAP_REACHED")); Cues.Nope(); return; }
            _busy = true; _requesting = true; _result = null; _error = null; _stoppedCount = 0;
            _key ??= Guid.NewGuid().ToString();
            ClearWin();
            _spinAt = _t;
            foreach (var reel in _reels) reel.Start(_t);
            Cues.Play("sfx_slot_lever", 0.9f);
            DeviceFeel.Confirm();
            CTLife.Audio.Sfx.StartLoop(LoopKey, "sfx_slot_spin", 8f);
            RefreshCta();
            Log.Info("casino", "slots spin " + stake);
            Request(stake);
        }

        private async void Request(long stake)
        {
            try
            {
                _result = await Svc.Api.Spin(stake, _key, Svc.Life);
                _key = null;
                Log.Info("casino", $"slots {_result.Round.RoundId} reels {string.Join(",", _result.Round.Reels ?? new string[0])} x{_result.Round.Multiplier} payout {_result.Round.PayoutCents}");
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                _error = CityErrors.From(e, true);
                if (!_error.OutcomeUnknown) _key = null;
            }
            finally { _requesting = false; }
        }

        public override void Tick(float dt)
        {
            _t += dt;
            // land the reels once the answer is in and they have spun long enough
            if (_busy && !_requesting && _t - _spinAt >= MinSpin)
            {
                var reels = _result?.Round?.Reels;
                for (int i = 0; i < 3; i++)
                {
                    var reel = _reels[i];
                    if (reel.Phase != ReelPhase.Spinning) continue;
                    float at = _spinAt + MinSpin + i * StopGap;
                    if (_t < at) break;
                    reel.Stop(reels != null ? reels[i] : null);
                }
            }
            foreach (var reel in _reels) reel.Tick(dt, _t);
            TickBulbs(dt);
        }

        internal void ReelLanded(int index)
        {
            Cues.Play("sfx_slot_stop", 0.9f);
            DeviceFeel.Tick();
            _stoppedCount++;
            if (_stoppedCount < 3) return;
            CTLife.Audio.Sfx.StopLoop(LoopKey);
            if (_error != null)
            {
                _busy = false;
                RefreshCta();
                Sheet.Fail(_error);
                return;
            }
            Reveal(_result);
        }

        private void Reveal(CasinoBet bet)
        {
            _busy = false;
            RefreshCta();
            if (bet == null) return;
            Svc.AfterBet(bet, bet.CashCents);
            var round = bet.Round;
            double m = round.Multiplier;
            long payout = round.PayoutCents ?? 0;
            var reels = round.Reels ?? new string[3];
            if (m <= 0)
            {
                bool rug = Array.IndexOf(reels, "rug") >= 0;
                _lose.text = CasinoText.T(rug ? "slots.rug" : "slots.nowin");
                return;
            }
            if (m <= 1)
            {
                // part of the stake back is still a loss: say so plainly, no win show (no coins, no bells, no green)
                _lose.text = payout >= round.StakeCents ? CasinoText.F("slots.even", CasinoText.Money(payout))
                    : CasinoText.F("slots.partBack", CasinoText.Money(payout), CasinoText.Money(round.StakeCents - payout));
                Cues.Play("sfx_chips", 0.5f);
                return;
            }
            // light the paying symbols: the win symbol and any wild standing in for it
            for (int i = 0; i < 3; i++)
                if (reels[i] == round.WinSymbol || reels[i] == "wild") _reels[i].Glow(true);
            _payline.AddToClassList("rr-payline--win");
            _marquee.AddToClassList("rr-marquee--win");
            bool big = round.Jackpot || m >= 10;
            _marqueeLabel.text = round.Jackpot ? CasinoText.T("slots.jackpot") : CasinoText.F("slots.win", CasinoText.Money(payout));
            CountUp(_win, payout, big ? 1.6f : 0.9f);
            _winFlash = big ? 3f : 1.4f;
            var c = _burst.WorldToLocal(_payline.worldBound.center);
            _burst.Coins(c, big ? 36 : 18);
            if (big) { Cues.Play("sfx_slot_bigwin"); BigWin(round.Jackpot ? CasinoText.T("slots.jackpot") : CasinoText.T("slots.bigwin"), payout); }
            else Cues.Play("sfx_slot_win");
            DeviceFeel.Confirm();
        }

        private void CountUp(Label label, long cents, float seconds)
        {
            label.text = CasinoText.Money(0);
            Tween.Run(label, seconds, t => label.text = CasinoText.Money((long)Math.Round(cents * t)), Ease.OutCubic, skipWhenReduced: false);
            Motion.Pop(label, 1.15f, 0.3f);
        }

        private void BigWin(string title, long cents)
        {
            _bigWin?.RemoveFromHierarchy();
            var rays = new Sunburst();
            rays.AddToClassList("rr-bigwin-rays");
            var t = El.Text(title, "rr-bigwin-title");
            var a = El.Text(CasinoText.Money(0), "rr-bigwin-amount");
            _bigWin = El.Div("rr-bigwin", rays, t, a);
            _bigWin.name = "rr-bigwin";
            _bigWin.pickingMode = PickingMode.Position;
            var host = _bigWin;
            _bigWin.RegisterCallback<PointerUpEvent>(_ => host.RemoveFromHierarchy());
            Insert(childCount - 1, _bigWin);
            _burst.Confetti(90);
            host.style.opacity = 0f;
            Tween.Run(host, 0.25f, k => host.style.opacity = k, Ease.OutCubic, skipWhenReduced: false);
            Tween.Run(t, 0.5f, k => { float s = Mathf.Lerp(0.4f, 1f, Ease.OutBack(k)); t.style.scale = new Scale(new Vector3(s, s, 1)); }, Ease.Linear, skipWhenReduced: false);
            Tween.Run(a, 1.6f, k => a.text = CasinoText.Money((long)Math.Round(cents * k)), Ease.OutCubic, skipWhenReduced: false);
            host.schedule.Execute(() => rays.Spin(0.6f)).Every(16).Until(() => host.parent == null);
            host.schedule.Execute(() => Tween.Run(host, 0.4f, k => host.style.opacity = 1f - k, Ease.OutCubic, skipWhenReduced: false)).StartingIn(3200);
            host.schedule.Execute(() => host.RemoveFromHierarchy()).StartingIn(3650);
        }

        private void ClearWin()
        {
            foreach (var r in _reels) r.Glow(false);
            _payline.RemoveFromClassList("rr-payline--win");
            _marquee.RemoveFromClassList("rr-marquee--win");
            _marqueeLabel.text = "THE RUG ROOM";
            _win.text = ""; _lose.text = "";
            _bigWin?.RemoveFromHierarchy();
        }

        // ------------------------------------------------------------------ marquee bulbs

        private void LayoutBulbs()
        {
            var r = _marquee.contentRect;
            if (r.width <= 0 || float.IsNaN(r.width) || _bulbEls.Count > 0) return;
            const int across = 22, down = 3;
            void Bulb(float x, float y) { var b = El.Div("rr-bulb"); b.style.left = x - 7; b.style.top = y - 7; _bulbs.Add(b); _bulbEls.Add(b); }
            for (int i = 0; i < across; i++) Bulb(18 + (r.width - 36) * i / (across - 1), 10);
            for (int i = 1; i <= down; i++) Bulb(r.width - 12, 10 + (r.height - 20) * i / (down + 1));
            for (int i = across - 1; i >= 0; i--) Bulb(18 + (r.width - 36) * i / (across - 1), r.height - 10);
            for (int i = down; i >= 1; i--) Bulb(12, 10 + (r.height - 20) * i / (down + 1));
        }

        private float _bulbPhase;
        private void TickBulbs(float dt)
        {
            if (_bulbEls.Count == 0) return;
            _winFlash = Mathf.Max(0, _winFlash - dt);
            float speed = _winFlash > 0 ? 18f : _busy ? 10f : 3f;
            _bulbPhase += dt * speed;
            int ph = (int)_bulbPhase;
            for (int i = 0; i < _bulbEls.Count; i++)
            {
                bool on = _winFlash > 0 ? (ph % 2 == 0) : ((i + ph) % 3 == 0);
                _bulbEls[i].EnableInClassList("rr-bulb--dim", !on);
            }
        }

        public override void Hidden()
        {
            CTLife.Audio.Sfx.StopLoop(LoopKey);
        }

        public override void Disposed() { CTLife.Audio.Sfx.StopLoop(LoopKey); _burst.Stop(); }

        // ------------------------------------------------------------------ one reel

        internal enum ReelPhase { Idle, Spinning, Stopping, Settling }

        private sealed class Reel
        {
            public readonly VisualElement Root;
            public ReelPhase Phase = ReelPhase.Idle;
            private readonly SlotsPanel _panel;
            private readonly int _index;
            private readonly VisualElement[] _cells = new VisualElement[4];
            private readonly VisualElement[] _art = new VisualElement[4];
            private readonly VisualElement[] _glow = new VisualElement[4];
            private readonly long[] _shown = { long.MinValue, long.MinValue, long.MinValue, long.MinValue };
            private readonly Dictionary<long, string> _symbols = new Dictionary<long, string>();
            private readonly System.Random _rng;
            private double _offset, _speed;
            private float _phaseAt;
            private double _from, _target, _dur;
            private float _cell = 160f;
            private bool _glowing;

            public Reel(SlotsPanel panel, int index)
            {
                _panel = panel; _index = index;
                _rng = new System.Random(Environment.TickCount + index * 7919);
                Root = El.Div("rr-reel");
                Root.name = "rr-reel-" + index;
                for (int i = 0; i < 4; i++)
                {
                    _glow[i] = El.Div("rr-cell-glow");
                    _art[i] = El.Div("rr-cell-art");
                    _cells[i] = El.Div("rr-cell", _glow[i], _art[i]);
                    _cells[i].pickingMode = PickingMode.Ignore;
                    Root.Add(_cells[i]);
                }
                Root.Add(new Shade(true)); Root.Add(new Shade(false));
                Root.RegisterCallback<GeometryChangedEvent>(_ => { var h = Root.contentRect.height; if (h > 10) { _cell = h / 3f; foreach (var c in _cells) c.style.height = _cell; Place(); } });
                _offset = 4 + _rng.Next(0, 4);
                Place();
            }

            private string SymbolFor(long n)
            {
                if (_symbols.TryGetValue(n, out var s)) return s;
                var st = _panel.State;
                var weights = st?.ReelWeights;
                double u = _rng.NextDouble();
                s = weights != null && weights.Count > 0 ? CasinoMath.PickSymbol(u, weights) : CasinoGames.Symbols[(int)(u * 6) % 6];
                _symbols[n] = s;
                if (_symbols.Count > 64) { var old = new List<long>(); foreach (var k in _symbols.Keys) if (k < n - 8) old.Add(k); foreach (var k in old) _symbols.Remove(k); }
                return s;
            }

            public void Start(float now)
            {
                Phase = ReelPhase.Spinning; _phaseAt = now; _speed = 0;
            }

            /// <summary>Land on `symbol` (null: a failed spin lands anywhere).</summary>
            public void Stop(string symbol)
            {
                long cur = (long)Math.Floor(_offset);
                long target = cur + 4 + 1;
                if (symbol != null) _symbols[target - 2] = symbol;
                _from = _offset; _target = target;
                double dist = target + Overshoot - _offset;
                _dur = Math.Max(0.35, Math.Min(1.1, 3.0 * dist / Math.Max(4.0, _speed)));
                Phase = ReelPhase.Stopping; _phaseAt = _panel._t;
            }

            private bool _placedIdle;

            public void Tick(float dt, float now)
            {
                // at rest nothing moves: no per-frame style writes (each one re-lays the panel out; the Infinix felt it)
                if (Phase == ReelPhase.Idle && !_glowing) { if (!_placedIdle) { Place(); _placedIdle = true; } return; }
                _placedIdle = false;
                switch (Phase)
                {
                    case ReelPhase.Spinning:
                    {
                        float e = now - _phaseAt;
                        // anticipation: a short kick back, then up to speed
                        _speed = e < 0.12f ? -3.0 : Math.Min(SpinSpeed, (e - 0.12f) * 70.0);
                        _offset += _speed * dt;
                        break;
                    }
                    case ReelPhase.Stopping:
                    {
                        float e = now - _phaseAt;
                        double k = Math.Min(1.0, e / _dur);
                        double eased = 1 - Math.Pow(1 - k, 3);
                        _offset = _from + (_target + Overshoot - _from) * eased;
                        _speed = (1 - k) * (1 - k) * 3 * (_target + Overshoot - _from) / _dur;
                        if (k >= 1.0) { Phase = ReelPhase.Settling; _phaseAt = now; }
                        break;
                    }
                    case ReelPhase.Settling:
                    {
                        float e = now - _phaseAt;
                        double k = Math.Min(1.0, e / 0.16);
                        _offset = _target + Overshoot * (1 - Ease.InOutSine((float)k));
                        _speed = 0;
                        if (k >= 1.0) { _offset = _target; Phase = ReelPhase.Idle; _panel.ReelLanded(_index); }
                        break;
                    }
                }
                Place();
                if (_glowing)
                {
                    float pulse = 0.65f + 0.35f * Mathf.Sin(now * 7f);
                    for (int i = 0; i < 4; i++) if (_glow[i].style.opacity.value > 0.01f) _glow[i].style.opacity = pulse;
                }
            }

            private void Place()
            {
                long fo = (long)Math.Floor(_offset);
                float blur = (float)Math.Min(1.0, Math.Abs(_speed) / SpinSpeed);
                for (int i = 0; i < 4; i++)
                {
                    long n = fo - Mod(fo + i, 4);
                    double y = (_offset - n) - 1.0;
                    _cells[i].style.top = (float)(y * _cell);
                    if (_shown[i] != n)
                    {
                        _shown[i] = n;
                        var tex = RugRoomSheet.Symbol(SymbolFor(n));
                        _art[i].style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
                        _glow[i].style.opacity = 0f;
                    }
                    // speed blur: stretched along the motion, a touch transparent
                    _art[i].style.scale = new Scale(new Vector3(1f - 0.06f * blur, 1f + 0.32f * blur, 1f));
                    _art[i].style.opacity = 1f - 0.35f * blur;
                }
            }

            private static long Mod(long a, long m) { long r = a % m; return r < 0 ? r + m : r; }

            /// <summary>The middle row's symbol glows (a win).</summary>
            public void Glow(bool on)
            {
                _glowing = on;
                long fo = (long)Math.Floor(_offset);
                for (int i = 0; i < 4; i++)
                {
                    long n = fo - Mod(fo + i, 4);
                    bool middle = n == (long)Math.Round(_offset) - 2;
                    _glow[i].style.opacity = on && middle ? 1f : 0f;
                    if (on && middle) Motion.Pop(_art[i], 1.18f, 0.35f);
                }
            }

            /// <summary>Glass shading at the top and bottom of the reel: the drum curving away.</summary>
            private sealed class Shade : VisualElement
            {
                private readonly bool _top;
                public Shade(bool top)
                {
                    _top = top;
                    AddToClassList("rr-reel-shade");
                    AddToClassList(top ? "rr-reel-shade--top" : "rr-reel-shade--bottom");
                    pickingMode = PickingMode.Ignore;
                    generateVisualContent += Draw;
                }

                private void Draw(MeshGenerationContext ctx)
                {
                    var r = contentRect;
                    if (r.width <= 0 || float.IsNaN(r.width)) return;
                    var p = ctx.painter2D;
                    const int bands = 12;
                    for (int i = 0; i < bands; i++)
                    {
                        float a = Mathf.Pow(1f - i / (float)bands, 2f) * 0.16f;
                        float y0 = r.height * i / bands, y1 = r.height * (i + 1) / bands;
                        if (!_top) { y0 = r.height - y0; y1 = r.height - y1; }
                        p.fillColor = new Color(0.08f, 0.02f, 0.06f, a);
                        p.BeginPath();
                        p.MoveTo(new Vector2(0, y0)); p.LineTo(new Vector2(r.width, y0)); p.LineTo(new Vector2(r.width, y1)); p.LineTo(new Vector2(0, y1));
                        p.ClosePath(); p.Fill();
                    }
                }
            }
        }
    }

    /// <summary>Golden light rays (Painter2D wedges), turning: the big-win show.</summary>
    public sealed class Sunburst : VisualElement
    {
        private float _angle;
        public Sunburst()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void Spin(float degrees) { _angle = (_angle + degrees) % 360f; style.rotate = new Rotate(_angle); }

        private void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width <= 0 || float.IsNaN(r.width)) return;
            var c = r.center; float R = Mathf.Min(r.width, r.height) * 0.5f;
            var p = ctx.painter2D;
            const int N = 16;
            for (int pass = 0; pass < 3; pass++)
            {
                float len = R * (0.5f + 0.25f * pass);
                for (int i = 0; i < N; i++)
                {
                    float a0 = (i / (float)N) * Mathf.PI * 2f, a1 = a0 + Mathf.PI / N * (0.6f - 0.1f * pass);
                    p.fillColor = new Color(1f, 0.82f, 0.3f, 0.12f);
                    p.BeginPath();
                    p.MoveTo(c);
                    p.LineTo(c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * len);
                    p.LineTo(c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * len);
                    p.ClosePath();
                    p.Fill();
                }
            }
        }
    }
}
