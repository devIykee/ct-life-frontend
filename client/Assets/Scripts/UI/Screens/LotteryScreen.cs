using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Rest.Model;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// The birth lottery (feature #3), forced once per account: Insider Baby, Exit Liquidity Baby, or (Seeker
    /// Genesis Token holders) Seeker Baby. The server rolls (`POST /v1/lottery/roll`); the client only animates.
    /// The roll delegate returns null when the server shows the roll already happened (409 or reconciled /me).
    ///
    /// Presentation (spec docs/research/duolingo-design-teardown.md C1, redesign 2026-10-08): face-down painted cards on the
    /// blurred town, one action ("DISCOVER MY BEGINNING"), a shuffle and a spotlight that hops card to card (the original
    /// 0.07 → 0.35 s curve, minimum 2.2 s, waits for the server), the winner flips; Insider/Seeker get a coin burst and the moon
    /// horn, Exit Liquidity gets a soft "bonk", a muted shower and a second, supportive line. Then the result (Cash, home,
    /// debt) and the CTA into the creator. No guide character: the cards carry the moment.
    ///
    /// Round 3 (user review 2026-10-10, trading-card references): the face-down cards carry a rarity ribbon (RARE · 20%,
    /// COMMON · 80%); the winner flips, comes to the centre and unfolds into a landscape origin card: the painted art in a
    /// window on the left, rarity, name, the story line and a stat band (starting Cash, home, debt) on the card itself.
    /// </summary>
    public sealed class LotteryScreen : UiScreen
    {
        private const float CardW = 440f, CardH = 605f, CardGap = 96f, SeekerCardW = 360f;
        // The table spans the content; face-down cards sit right of the intro copy, the unfolded card is centred.
        private const float DrawShiftX = 440f, WideW = 1880f, WideH = 660f, WideTop = -8f, WidePad = 22f, WideBorder = 10f;
        private static readonly Color WideBody = new Color32(42, 15, 50, 255);

        private readonly Func<Task<PostLotteryRoll200Response>> _roll;
        private readonly Action<PostLotteryRoll200Response> _onDone;
        private readonly bool _isSeeker;
        private readonly List<Card> _cards = new List<Card>();
        private OnboardingFrame _frame;
        private VisualElement _table, _intro;
        private Label _eyebrow, _title, _line, _error, _note;
        private PostLotteryRoll200Response _rollResult;
        private Card _shown;
        private float _drawShift = DrawShiftX;
        private IVisualElementScheduledItem _idle;
        private bool _rolling, _rollDone, _revealed;
        private float _idleStart;

        private sealed class Card
        {
            public string id;
            public VisualElement root, glow, art, fallback, ribbon, info, stats, debtCell;
            public Label odds, rarity, chance, name, line, cash, home, debt;
            public float homeX;
            public bool wide;
        }

        public LotteryScreen(bool isSeeker, Func<Task<PostLotteryRoll200Response>> roll, Action<PostLotteryRoll200Response> onDone)
        {
            _isSeeker = isSeeker;
            _roll = roll;
            _onDone = onDone;
        }

        /// <summary>Presentation phases ("draw", "reveal", "flip", "landed", "result") for editor captures and checks.</summary>
        public event Action<string> Phase;

        /// <summary>True once the result is on screen (captures and automation).</summary>
        public bool Revealed => _revealed;
        public bool Rolling => _rolling;

        protected override VisualElement Build()
        {
            _frame = new OnboardingFrame("ob-lottery", null, OnRoll, Strings.T("ob.lottery.cta"));
            _frame.Back.style.visibility = Visibility.Hidden; // nothing to go back to; the slot keeps the bar where the creator has it
            _frame.Progress.Set(0, false);
            _frame.Cta.EnableShine();

            _eyebrow = El.Text(Strings.T("lottery.eyebrow"), "ob-eyebrow");
            _title = El.Text(Strings.T("lottery.title"), "ob-title");
            _line = El.Text(Strings.T("lottery.body"), "ob-line");
            _intro = El.Div("ob-lottery-intro", _eyebrow, _title, _line);

            _error = El.Text("", "ob-error");
            _error.style.display = DisplayStyle.None;
            _note = El.Text(Strings.T("ob.lottery.note"), "ob-note");
            var left = El.Div("ob-lottery-left", _intro, _error);
            left.pickingMode = PickingMode.Ignore;

            _table = El.Div("ob-lottery-table");
            _table.pickingMode = PickingMode.Ignore;
            var ids = LotteryModel.Cards(_isSeeker);
            Dictionary<string, int> odds = null;
            if (Services.TryGet<ConfigStore>(out var config)) odds = LotteryModel.Odds(config.Config("lottery"), ids);
            float w = ids.Count > 2 ? SeekerCardW : CardW;
            _drawShift = ids.Count > 2 ? DrawShiftX - 70f : DrawShiftX; // three cards: keep the right card off the screen edge
            float total = ids.Count * w + (ids.Count - 1) * CardGap;
            for (int i = 0; i < ids.Count; i++)
            {
                var card = MakeCard(ids[i], w, odds);
                card.homeX = _drawShift - total / 2f + i * (w + CardGap);
                card.root.style.left = new Length(50f, LengthUnit.Percent);
                card.root.style.marginLeft = card.homeX;
                _cards.Add(card);
                _table.Add(card.root);
            }

            _frame.Content.Add(left);
            _frame.Content.Add(_table);
            _frame.Content.Add(_note);
            _frame.SetHint("");
            StartIdle();
            return _frame.Root;
        }

        private Card MakeCard(string id, float width, Dictionary<string, int> odds)
        {
            var c = new Card { id = id };
            c.glow = El.Div("ob-card-glow");
            c.glow.style.backgroundImage = new StyleBackground(OnboardingArt.Glow);
            c.art = El.Div("ob-card-art");
            var back = OnboardingArt.CardBack;
            if (back != null) c.art.style.backgroundImage = new StyleBackground(back);
            else c.art.AddToClassList("ob-card-art--plain");
            // Shown only when the face art for this outcome is missing (e.g. no Seeker card painted yet).
            c.fallback = El.Text(Strings.T(LotteryModel.NameKey(id)), "ob-card-fallback");
            c.fallback.style.display = DisplayStyle.None;
            c.art.Add(c.fallback);
            int pct = -1;
            if (odds != null && odds.TryGetValue(id, out var p)) pct = p;
            string rarity = Strings.T(RarityKey(id));
            c.odds = El.Text(pct >= 0 ? Strings.F("ob.lottery.ribbon", rarity, pct) : rarity, "ob-card-odds");
            c.ribbon = El.Div("ob-card-ribbon", c.odds);
            c.ribbon.pickingMode = PickingMode.Ignore;
            c.info = BuildInfo(c, id, rarity, pct);
            c.info.style.display = DisplayStyle.None;
            c.root = El.Div("ob-card " + RarityClass(id), c.glow, c.art, c.info, c.ribbon);
            c.root.name = "card_" + id;
            c.root.style.width = width;
            c.root.style.height = width * CardH / CardW;
            El.PassThrough(c.root);
            return c;
        }

        private static string RarityKey(string id) =>
            id == LotteryModel.Insider ? "ob.lottery.rarity.insider" : id == LotteryModel.Seeker ? "ob.lottery.rarity.seeker" : "ob.lottery.rarity.exit";

        private static string RarityClass(string id) =>
            id == LotteryModel.Insider ? "ob-card--rare" : id == LotteryModel.Seeker ? "ob-card--seeker" : "ob-card--common";

        /// <summary>The right half of the unfolded origin card: rarity and odds, the name, the story line, the stat band.</summary>
        private static VisualElement BuildInfo(Card c, string id, string rarity, int pct)
        {
            c.rarity = El.Text(rarity, "ob-origin-rarity");
            c.chance = El.Text(pct >= 0 ? Strings.F("ob.lottery.odds", pct) : "", "ob-origin-chance");
            var top = El.Div("ob-origin-top", c.rarity, c.chance);
            c.name = El.Text(Strings.T(LotteryModel.NameKey(id)), "ob-origin-name");
            c.line = El.Text("", "ob-origin-line");
            c.cash = El.Text("", "ob-origin-value ob-origin-value--cash");
            c.home = El.Text("", "ob-origin-value ob-origin-value--home");
            c.debt = El.Text("", "ob-origin-value ob-origin-value--debt");
            var cashCell = El.Div("ob-origin-stat", El.Text(Strings.T("ob.lottery.stat.cash"), "ob-origin-label"), c.cash);
            var homeCell = El.Div("ob-origin-stat ob-origin-stat--sep", El.Text(Strings.T("ob.lottery.stat.home"), "ob-origin-label"), c.home);
            c.debtCell = El.Div("ob-origin-stat ob-origin-stat--sep", El.Text(Strings.T("ob.lottery.stat.debt"), "ob-origin-label"), c.debt);
            c.stats = El.Div("ob-origin-stats", cashCell, homeCell, c.debtCell);
            var info = El.Div("ob-origin-info", El.Div("ob-origin-head", top, c.name, c.line), c.stats);
            info.pickingMode = PickingMode.Ignore;
            return info;
        }

        private static float WideArtWidth() => (WideH - 2f * (WidePad + WideBorder)) * CardW / CardH;

        /// <summary>One frame of the unfold, t 0 → 1: the face-up card grows into the landscape origin card.</summary>
        private static void SetWide(Card c, float t, float w0, float h0, float top0, float ml0, float s0)
        {
            float w = Mathf.Lerp(w0, WideW, t), h = Mathf.Lerp(h0, WideH, t);
            c.root.style.width = w;
            c.root.style.height = h;
            c.root.style.marginLeft = Mathf.Lerp(ml0, -WideW / 2f, t);
            c.root.style.top = Mathf.Lerp(top0, WideTop, t);
            float s = Mathf.Lerp(s0, 1f, t);
            c.root.style.scale = new Scale(new Vector2(s, s));
            float pad = WidePad * t, border = WideBorder * t;
            c.root.style.paddingLeft = pad; c.root.style.paddingRight = pad; c.root.style.paddingTop = pad; c.root.style.paddingBottom = pad;
            c.root.style.borderLeftWidth = border; c.root.style.borderRightWidth = border;
            c.root.style.borderTopWidth = border; c.root.style.borderBottomWidth = border;
            var body = WideBody; body.a = Mathf.Clamp01(t * 4f);
            c.root.style.backgroundColor = body;
            c.art.style.width = Mathf.Lerp(w0, WideArtWidth(), t);
            float info = Mathf.Clamp01((t - 0.72f) / 0.28f); // after the card is wide enough for the name
            c.info.style.opacity = info;
            c.info.style.translate = new Translate((1f - info) * 48f, 0f);
            c.glow.style.opacity = Mathf.Lerp(0.9f, 0f, Mathf.Clamp01(t * 2f));
        }

        private static void BeginWide(Card c)
        {
            c.wide = true;
            c.root.AddToClassList("ob-card--wide");
            c.ribbon.style.display = DisplayStyle.None;
            c.art.style.flexGrow = 0f;
            c.art.style.flexShrink = 0f;
            c.info.style.display = DisplayStyle.Flex;
        }

        /// <summary>The flipped card comes to the centre and unfolds sideways into the landscape origin card.</summary>
        private async Task Unfold(Card c)
        {
            float w0 = c.root.resolvedStyle.width, h0 = c.root.resolvedStyle.height, top0 = c.root.resolvedStyle.top;
            float ml0 = c.root.style.marginLeft.value.value;
            float s0 = c.root.resolvedStyle.scale.value.x;
            BeginWide(c);
            SetWide(c, 0f, w0, h0, top0, ml0, s0);
            Kit.Cues.Play(Kit.Cues.SfxSelect, 0.5f);
            await Tween.Run(_table, 0.5f, t => SetWide(c, t, w0, h0, top0, ml0, s0), Ease.OutCubic).Completion;
        }

        /// <summary>The unfolded card without animation (reduced motion, instant results).</summary>
        private static void MakeWide(Card c)
        {
            if (c.wide) return;
            BeginWide(c);
            SetWide(c, 1f, CardW, CardH, 14f, -CardW / 2f, 1f);
        }

        protected override void OnShown() => OnboardingAudio.StartTheme();

        /// <summary>Face-down cards breathe gently (offset phases) until the draw. Off under reduced motion.</summary>
        private void StartIdle()
        {
            if (DeviceFeel.ReducedMotion) return;
            _idleStart = Kit.Motion.Now;
            _idle = _table.schedule.Execute(IdleTick).Every(33);
        }

        private void IdleTick()
        {
            float t = Kit.Motion.Now - _idleStart;
            for (int i = 0; i < _cards.Count; i++)
                _cards[i].root.style.translate = new Translate(0f, Mathf.Sin(t * 2f * Mathf.PI / 2.8f + i * 1.7f) * 8f);
        }

        private void StopIdle()
        {
            _idle?.Pause();
            _idle = null;
            foreach (var c in _cards) c.root.style.translate = new Translate(0f, 0f);
        }

        private async void OnRoll()
        {
            if (_rolling) return;
            if (_rollResult != null)
            {
                _onDone?.Invoke(_rollResult);
                return;
            }
            _rolling = true;
            _rollDone = false;
            _error.style.display = DisplayStyle.None;
            _frame.Cta.SetEnabled(false);
            _frame.ShowCta(false);
            _line.text = Strings.T("ob.lottery.drawing");
            StopIdle();
            // The odds were shown before the draw; during the shuffle they would pile up, so they step out.
            foreach (var c in _cards) Kit.Motion.Fade(c.odds, 1f, 0f, 0.15f);
            Phase?.Invoke("draw");
            var draw = Draw();
            try
            {
                _rollResult = await _roll();
                _rollDone = true;
            }
            catch (Exception e)
            {
                _rolling = false;
                await draw;
                if (Element?.panel == null) return;
                foreach (var card in _cards) { Hot(card, false); card.odds.style.opacity = 1f; }
                _line.text = Strings.T("lottery.body");
                _error.text = AppUi.Err(e);
                _error.style.display = DisplayStyle.Flex;
                Kit.Cues.Nope();
                _frame.Cta.Text = Strings.T("ob.lottery.retry");
                _frame.Cta.SetEnabled(true);
                _frame.ShowCta(true);
                StartIdle();
                return;
            }
            await draw;
            if (Element?.panel == null) { _rolling = false; return; }
            if (_rollResult == null)
            {
                // The roll had already happened (reconciled from /me). Nothing to reveal; continue onboarding.
                _rolling = false;
                _onDone?.Invoke(null);
                return;
            }
            try { await Reveal(_rollResult); }
            catch (Exception e) { Log.Exception("lottery", e); ShowResultInstant(_rollResult); }
            _rolling = false;
        }

        /// <summary>The shuffle, then the spotlight hopping card to card until at least 2.2 s and the server's answer.</summary>
        private async Task Draw()
        {
            if (DeviceFeel.ReducedMotion)
            {
                while (_rolling && !_rollDone) await Awaitable.WaitForSecondsAsync(0.1f);
                return;
            }
            await Shuffle();
            float t0 = Kit.Motion.Now;
            int i = 0;
            while (_rolling && (Kit.Motion.Now - t0 < 2.2f || !_rollDone))
            {
                for (int k = 0; k < _cards.Count; k++) Hot(_cards[k], k == i % _cards.Count);
                i++;
                Kit.Cues.Hop(i - 1);
                float elapsed = Kit.Motion.Now - t0;
                if (elapsed > 8f) _line.text = Strings.T("ob.lottery.slow");
                await Kit.Motion.Delay(Mathf.Lerp(0.07f, 0.35f, Mathf.Clamp01(elapsed / 2.2f)));
                if (Element?.panel == null) return;
            }
            if (!_rolling || _rollResult == null) return;
            // Land on the winner: a few last, slowing hops ending on it.
            int winner = _cards.FindIndex(c => c.id == LotteryModel.OutcomeId(_rollResult.Outcome));
            if (winner < 0) return;
            int current = (i - 1 + _cards.Count) % _cards.Count;
            int hops = (winner - current + _cards.Count) % _cards.Count;
            if (hops == 0) hops = _cards.Count;
            for (int h = 1; h <= hops; h++)
            {
                await Kit.Motion.Delay(0.36f + 0.06f * h);
                for (int k = 0; k < _cards.Count; k++) Hot(_cards[k], k == (current + h) % _cards.Count);
                Kit.Cues.Hop(Mathf.Min(6, h));
            }
            await Kit.Motion.Delay(0.3f);
        }

        /// <summary>The cards gather to the centre and deal back out in a new order: two quick swaps (spec C1).</summary>
        private async Task Shuffle()
        {
            Kit.Cues.Play(Kit.Cues.SfxShuffle, 0.5f); // two riffles, ~1.1 s, both passes
            var starts = _cards.Select(c => c.homeX).ToArray();
            float w = _cards[0].root.resolvedStyle.width;
            for (int pass = 0; pass < 2; pass++)
            {
                await Tween.Run(_table, 0.26f, t =>
                {
                    for (int k = 0; k < _cards.Count; k++)
                    {
                        float target = _drawShift + (k - (_cards.Count - 1) / 2f) * 18f - w / 2f;
                        _cards[k].root.style.marginLeft = Mathf.Lerp(starts[k], target, t);
                        _cards[k].root.style.rotate = new Rotate(new Angle((k - 1) * 6f * t));
                    }
                }, Ease.InCubic).Completion;
                // Deal each card to another card's slot; two passes read as a shuffle.
                var order = Enumerable.Range(0, _cards.Count).Select(k => (k + 1 + pass) % _cards.Count).ToArray();
                var gathered = _cards.Select((c, k) => _drawShift + (k - (_cards.Count - 1) / 2f) * 18f - w / 2f).ToArray();
                await Tween.Run(_table, 0.26f, t =>
                {
                    for (int k = 0; k < _cards.Count; k++)
                    {
                        float target = _cards[order[k]].homeX;
                        _cards[k].root.style.marginLeft = Mathf.LerpUnclamped(gathered[k], target, t);
                        _cards[k].root.style.rotate = new Rotate(new Angle((k - 1) * 6f * (1f - t)));
                    }
                }, Ease.OutBack).Completion;
                for (int k = 0; k < _cards.Count; k++) starts[k] = _cards[order[k]].homeX;
                Kit.Cues.Play(Kit.Cues.SfxSelect, 0.4f);
            }
            // Re-sort so each card's home is where it now sits.
            for (int k = 0; k < _cards.Count; k++) _cards[k].homeX = starts[k];
        }

        private static void Hot(Card c, bool on) => c.root.EnableInClassList("ob-card--hot", on);

        private async Task Reveal(PostLotteryRoll200Response r)
        {
            string id = LotteryModel.OutcomeId(r.Outcome);
            var winner = _cards.FirstOrDefault(c => c.id == id) ?? _cards[0];
            var losers = _cards.Where(c => c != winner).ToList();
            foreach (var c in _cards) Hot(c, c == winner);
            bool good = id != LotteryModel.Exit;
            var front = OnboardingArt.CardFront(id);
            Phase?.Invoke("reveal");

            if (DeviceFeel.ReducedMotion)
            {
                foreach (var c in losers) c.root.style.display = DisplayStyle.None;
                winner.odds.style.display = DisplayStyle.None;
                SetFront(winner, front, id);
                MakeWide(winner);
                Kit.Motion.Fade(winner.root, 0f, 1f, 0.2f);
                Kit.Cues.Play(Kit.Cues.Origin(id));
                if (good) DeviceFeel.Confirm(); else DeviceFeel.Reject();
                ShowResult(r, id, false);
                return;
            }

            // Losers step aside and fade; the winner comes to the centre and grows a little.
            float startX = winner.root.style.marginLeft.value.value;
            float width = winner.root.resolvedStyle.width;
            var loserStart = losers.Select(c => c.root.style.marginLeft.value.value).ToArray();
            foreach (var c in losers) c.odds.style.display = DisplayStyle.None;
            winner.odds.style.display = DisplayStyle.None;
            await Tween.Run(_table, 0.42f, t =>
            {
                winner.root.style.marginLeft = Mathf.Lerp(startX, -width / 2f, t);
                float s = Mathf.Lerp(1f, 1.1f, t);
                winner.root.style.scale = new Scale(new Vector2(s, s));
                for (int k = 0; k < losers.Count; k++)
                {
                    float dir = loserStart[k] < startX ? -1f : 1f;
                    losers[k].root.style.marginLeft = loserStart[k] + dir * 260f * t;
                    losers[k].root.style.opacity = 1f - t;
                }
            }, Ease.OutCubic).Completion;
            foreach (var c in losers) c.root.style.display = DisplayStyle.None;

            // The flip: scaleX to 0 (darkening like a card edge), swap to the face, open back up with a little overshoot.
            Phase?.Invoke("flip");
            Kit.Cues.Play(Kit.Cues.SfxFlip);
            await Tween.Run(winner.art, 0.17f, t =>
            {
                winner.art.style.scale = new Scale(new Vector2(Mathf.Max(0.001f, 1f - t), 1f + 0.04f * t));
                winner.art.style.unityBackgroundImageTintColor = Color.Lerp(Color.white, new Color(0.55f, 0.5f, 0.6f), t);
            }, Ease.InCubic).Completion;
            SetFront(winner, front, id);
            await Tween.Run(winner.art, 0.28f, t =>
            {
                winner.art.style.scale = new Scale(new Vector2(Mathf.Max(0.001f, t), 1.04f - 0.04f * t));
                winner.art.style.unityBackgroundImageTintColor = Color.Lerp(new Color(0.55f, 0.5f, 0.6f), Color.white, Mathf.Clamp01(t * 1.4f));
            }, Ease.OutBack).Completion;

            await Kit.Motion.Delay(0.12f);
            FillCard(winner, r, id);
            Kit.Motion.Fade(_intro, 1f, 0f, 0.18f); // the card unfolds over the copy column
            await Unfold(winner);
            if (Element?.panel == null) return;

            var centre = CardCentreInBurst(winner);
            Phase?.Invoke("landed");
            if (good)
            {
                Kit.Cues.Play(Kit.Cues.Origin(id));
                DeviceFeel.Confirm();
                Kit.Motion.Pop(winner.art, 1.06f, 0.24f);
                _frame.Burst.Coins(centre, 26);
                Kit.Cues.Play(Kit.Cues.SfxCoins, 0.7f);
                await Kit.Motion.Delay(0.18f);
                _frame.Burst.Confetti(60);
            }
            else
            {
                // A comic "bonk": squash, a cloud puff and a slow muted shower; the rug sting mixed down so it is not punishing.
                Kit.Cues.Play(Kit.Cues.SfxDebt); // the comic bonk + hopeful tag, mixed 1 dB under the Insider stinger: full volume
                DeviceFeel.Reject();
                _ = Tween.Run(winner.art, 0.36f, t =>
                {
                    float squash = Mathf.Sin(t * Mathf.PI) * 0.06f;
                    winner.art.style.scale = new Scale(new Vector2(1f + squash, 1f - squash));
                }).Completion;
                _frame.Burst.Puff(new Vector2(centre.x, centre.y + winner.root.resolvedStyle.height * 0.42f));
                await Kit.Motion.Delay(0.2f);
                _frame.Burst.Confetti(12, muted: true, slow: true);
            }
            ShowResult(r, id, true);
            await Kit.Motion.Delay(0.4f);
        }

        private Vector2 CardCentreInBurst(Card c)
        {
            var b = c.art.worldBound;
            var local = _frame.Burst.WorldToLocal(b.center);
            return local;
        }

        private void SetFront(Card c, Texture2D front, string id)
        {
            if (front != null)
            {
                c.art.style.backgroundImage = new StyleBackground(front);
                c.fallback.style.display = DisplayStyle.None;
            }
            else
            {
                // No painted face yet: the plain card with the outcome's name, never a broken image.
                c.art.style.backgroundImage = StyleKeyword.None;
                c.art.AddToClassList("ob-card-art--plain");
                c.fallback.style.display = DisplayStyle.Flex;
            }
            c.root.AddToClassList("ob-card--face");
        }

        private void ShowResult(PostLotteryRoll200Response r, string id, bool animate)
        {
            _revealed = true;
            _frame.Progress.Set(1);
            _intro.style.display = DisplayStyle.None;
            var c = _cards.FirstOrDefault(x => x.id == id) ?? _cards[0];
            MakeWide(c);
            _shown = c;
            FillCard(c, r, id);
            CountUp(c.cash, r.CashCents, animate);
            if (id == LotteryModel.Exit)
            {
                // Spec C1: the debt outcome must not feel like a punishment. A second, supportive line 1.2 s later.
                _ = SecondLine();
            }
            _frame.Cta.Text = Strings.T("ob.lottery.next");
            _frame.Cta.SetEnabled(true);
            Phase?.Invoke("result");
            _frame.Cta.schedule.Execute(() => _frame.ShowCta(true, animate)).StartingIn(animate ? 400 : 0);
        }

        /// <summary>Writes the outcome onto the card (story line, home, debt); Cash shows $0 until it counts up.</summary>
        private static void FillCard(Card c, PostLotteryRoll200Response r, string id)
        {
            c.line.text = Strings.T("ob.lottery." + (id == LotteryModel.Exit ? "exit" : id) + ".line");
            string home = "";
            if (Services.TryGet<ConfigStore>(out var config))
                home = config.Registries.Homes.FirstOrDefault(h => h.id == r.HomeId)?.name;
            if (string.IsNullOrEmpty(home)) home = Strings.ReadableId(r.HomeId);
            c.home.text = home;
            c.debt.text = r.DebtCents > 0 ? LotteryModel.CashWhole(r.DebtCents) : "";
            c.debtCell.style.display = r.DebtCents > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            c.cash.text = LotteryModel.CashWhole(0);
        }

        private async Task SecondLine()
        {
            await Kit.Motion.Delay(DeviceFeel.ReducedMotion ? 0f : 1.2f);
            if (Element?.panel == null) return;
            if (_shown == null) return;
            _shown.line.text = Strings.T("ob.lottery.exit.line") + "\n" + Strings.T("ob.lottery.exit.line2");
            Kit.Motion.Pop(_shown.line, 1.03f, 0.2f);
            DeviceFeel.Tick();
        }

        private void ShowResultInstant(PostLotteryRoll200Response r)
        {
            string id = LotteryModel.OutcomeId(r.Outcome);
            foreach (var c in _cards) c.root.style.display = c.id == id ? DisplayStyle.Flex : DisplayStyle.None;
            var winner = _cards.FirstOrDefault(c => c.id == id);
            if (winner != null) SetFront(winner, OnboardingArt.CardFront(id), id);
            ShowResult(r, id, false);
        }

        /// <summary>Cash counts up over 600 ms. The labels are built once up front, so the ticks assign, not format.</summary>
        private static void CountUp(Label label, long cents, bool animate)
        {
            string final = LotteryModel.CashWhole(cents);
            if (!animate || DeviceFeel.ReducedMotion || cents <= 0) { label.text = final; return; }
            const int steps = 12;
            var labels = new string[steps + 1];
            for (int i = 0; i < steps; i++) labels[i] = LotteryModel.CashWhole(cents * i / steps / 100 * 100);
            labels[steps] = final;
            Tween.Run(label, 0.6f, t => label.text = labels[Mathf.Clamp(Mathf.FloorToInt(t * steps), 0, steps)], Ease.OutCubic);
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        /// <summary>Development automation (`autocreate=1`): rolls, then continues after the reveal.</summary>
        public async void AutoRollForAutomation()
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(0.5f);
                OnRoll();
                while (_rolling) await Awaitable.WaitForSecondsAsync(0.25f);
                await Awaitable.WaitForSecondsAsync(1f);
                if (_rollResult != null && Element?.parent != null) OnRoll();
            }
            catch (Exception e) { Log.Warn("lottery", "Automation roll failed: " + e.Message); }
        }
#endif

        /// <summary>Captures and automation: presses the CTA the way a tap does.</summary>
        public void PressCta() => _frame?.Cta.Press();

        public override void Close()
        {
            _rolling = false;
            StopIdle();
            _frame?.Burst.Stop();
            base.Close();
        }
    }
}
