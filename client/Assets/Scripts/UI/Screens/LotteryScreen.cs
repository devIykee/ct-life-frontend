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
    /// After the creator selections, centred painted cards automatically shuffle and reveal the server's result.
    /// The winning face expands into one illustrated landscape card with live origin and starting resources.
    /// The result remains visible while the coordinator commits the character; a failed commit can retry without rolling.
    ///
    /// The face-down cards retain their rarity ribbon and the existing draw/flip. The result keeps its
    /// artwork, name, story and resource values inside the same collectible surface.
    /// </summary>
    public sealed class LotteryScreen : UiScreen
    {
        private const float CardW = 440f, CardH = 605f, CardGap = 96f, SeekerCardW = 360f;
        private const float WideW = 1520f, WideH = 760f, WideTop = -22f;
        private const float ResultArtW = 590f, ResultArtH = 760f;

        private readonly Func<Task<PostLotteryRoll200Response>> _roll;
        private readonly Action<PostLotteryRoll200Response> _onDone;
        private readonly bool _isSeeker;
        private readonly bool _autoStart;
        private readonly bool _alreadyRecorded;
        private readonly PostLotteryRoll200Response _existingResult;
        private readonly List<Card> _cards = new List<Card>();
        private readonly HashSet<Tween> _tweens = new HashSet<Tween>();
        private OnboardingFrame _frame;
        private VisualElement _table;
        private Label _status, _error;
        private PostLotteryRoll200Response _rollResult;
        private IVisualElementScheduledItem _idle, _start, _showCta;
        private bool _rolling, _rollDone, _revealed, _started, _closed, _busy;
        private float _idleStart;

        private sealed class Card
        {
            public string id;
            public VisualElement root, surface, glow, portrait, art, fallback, ribbon, info, stats, debtCell;
            public Label odds, rarity, chance, name, line, alias, perk, cash, home, debt;
            /// <summary>The dream ladder strip: "you start here" (queue 18, W1 of the approved brief).</summary>
            public VisualElement ladder;
            public float homeX;
            public bool wide;
            public bool hasSurface;
        }

        public LotteryScreen(bool isSeeker, Func<Task<PostLotteryRoll200Response>> roll, Action<PostLotteryRoll200Response> onDone,
            bool autoStart = true, PostLotteryRoll200Response existingResult = null, bool alreadyRecorded = false)
        {
            _isSeeker = isSeeker;
            _roll = roll;
            _onDone = onDone;
            _autoStart = autoStart;
            _existingResult = existingResult;
            _alreadyRecorded = alreadyRecorded;
        }

        /// <summary>Presentation phases ("draw", "reveal", "flip", "landed", "result") for editor captures and checks.</summary>
        public event Action<string> Phase;

        /// <summary>True once the result is on screen (captures and automation).</summary>
        public bool Revealed => _revealed;
        public bool Rolling => _rolling;
        public string PlayerAlias { get; set; }
        public string StarterPerk { get; set; }

        protected override VisualElement Build()
        {
            _frame = new OnboardingFrame("ob-lottery", null, OnRoll, Strings.T("ob.lottery.continue"));
            OnboardingFrame.AddSheet(_frame.Root, "UI/origin-reveal");
            _frame.Back.style.visibility = Visibility.Hidden;
            _frame.Progress.Set(CreatorFlow.ProgressSegments - 1, false);
            _frame.ShowCta(false, false);

            _error = El.Text("", "ob-error");
            _error.style.display = DisplayStyle.None;
            _status = El.Text("", "ob-line");
            _status.style.display = DisplayStyle.None;
            var messages = El.Div("ob-lottery-status", _status, _error);
            messages.pickingMode = PickingMode.Ignore;

            _table = El.Div("ob-lottery-table");
            _table.pickingMode = PickingMode.Ignore;
            var ids = LotteryModel.Cards(_isSeeker);
            // Eligibility can change after a recorded draw. Its authoritative outcome must still have a result card.
            if (_existingResult != null)
            {
                string recordedId = LotteryModel.OutcomeId(_existingResult.Outcome);
                if (!ids.Contains(recordedId)) ids.Add(recordedId);
            }
            Dictionary<string, int> odds = null;
            if (Services.TryGet<ConfigStore>(out var config)) odds = LotteryModel.Odds(config.Config("lottery"), ids);
            float w = ids.Count > 2 ? SeekerCardW : CardW;
            float total = ids.Count * w + (ids.Count - 1) * CardGap;
            for (int i = 0; i < ids.Count; i++)
            {
                var card = MakeCard(ids[i], w, odds);
                card.homeX = -total / 2f + i * (w + CardGap);
                card.root.style.left = new Length(50f, LengthUnit.Percent);
                card.root.style.marginLeft = card.homeX;
                _cards.Add(card);
                _table.Add(card.root);
            }

            _frame.Content.Add(_table);
            _frame.Content.Add(messages);
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
            c.portrait = El.Div("ob-origin-portrait", c.art);
            int pct = -1;
            if (odds != null && odds.TryGetValue(id, out var p)) pct = p;
            string rarity = Strings.T(RarityKey(id));
            c.odds = El.Text(pct >= 0 ? Strings.F("ob.lottery.ribbon", rarity, pct) : rarity, "ob-card-odds");
            c.ribbon = El.Div("ob-card-ribbon", c.odds);
            c.ribbon.pickingMode = PickingMode.Ignore;
            c.info = BuildInfo(c, id, rarity, pct);
            c.info.style.display = DisplayStyle.None;
            c.surface = El.Div("ob-origin-surface", El.Text(Strings.T("ob.card.brand"), "ob-origin-brand"));
            var surfaceArt = OnboardingArt.OriginSurface(id);
            c.hasSurface = surfaceArt != null;
            if (c.hasSurface) c.surface.style.backgroundImage = new StyleBackground(surfaceArt);
            c.surface.style.display = DisplayStyle.None;
            c.root = El.Div("ob-card " + RarityClass(id), c.surface, c.glow, c.portrait, c.info, c.ribbon);
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

        private Card ResultCard(string id)
        {
            var card = _cards.FirstOrDefault(c => c.id == id);
            if (card != null) return card;
            // A fresh server response can also disagree with stale local eligibility. Add its actual named card;
            // never reuse another outcome's title/rarity, and never invent odds for an unoffered outcome.
            card = MakeCard(id, CardW, null);
            card.homeX = -CardW / 2f;
            card.root.style.left = new Length(50f, LengthUnit.Percent);
            card.root.style.marginLeft = card.homeX;
            _cards.Add(card);
            _table.Add(card.root);
            return card;
        }

        /// <summary>Live result details occupy the illustrated card's reserved information area.</summary>
        private static VisualElement BuildInfo(Card c, string id, string rarity, int pct)
        {
            c.rarity = El.Text(rarity, "ob-origin-rarity");
            c.chance = El.Text(pct >= 0 ? Strings.F("ob.lottery.odds", pct) : "", "ob-origin-chance");
            var top = El.Div("ob-origin-top", c.rarity, c.chance);
            c.name = El.Text(Strings.T(LotteryModel.NameKey(id)), "ob-origin-name");
            c.line = El.Text("", "ob-origin-line");
            c.alias = El.Text("", "ob-origin-alias");
            c.perk = El.Text("", "ob-origin-perk");
            c.alias.style.display = DisplayStyle.None;
            c.perk.style.display = DisplayStyle.None;
            c.cash = El.Text("", "ob-origin-value ob-origin-value--cash");
            c.cash.name = "origin-cash";
            c.home = El.Text("", "ob-origin-value ob-origin-value--home");
            c.home.name = "origin-home";
            c.debt = El.Text("", "ob-origin-value ob-origin-value--debt");
            c.debt.name = "origin-debt";
            var cashCell = El.Div("ob-origin-stat ob-origin-stat--cash", El.Text(Strings.T("ob.lottery.stat.cash"), "ob-origin-label"), c.cash);
            var homeCell = El.Div("ob-origin-stat ob-origin-stat--home", El.Text(Strings.T("ob.lottery.stat.home"), "ob-origin-label"), c.home);
            c.debtCell = El.Div("ob-origin-stat ob-origin-stat--debt", El.Text(Strings.T("ob.lottery.stat.debt"), "ob-origin-label"), c.debt);
            c.stats = El.Div("ob-origin-stats", cashCell, homeCell, c.debtCell);
            var note = El.Text(Strings.T("ob.lottery.note"), "ob-origin-note");
            c.ladder = LadderStrip();
            var info = El.Div("ob-origin-info", El.Div("ob-origin-head", top, c.alias, c.name, c.line), c.ladder, c.perk,
                El.Div("ob-origin-benefits", c.stats, note));
            info.pickingMode = PickingMode.Ignore;
            return info;
        }

        /// <summary>"YOU START HERE": the four Cash rungs of the dream ladder and the hovercar dream badge, lit at the start rung.</summary>
        private static VisualElement LadderStrip()
        {
            var row = El.Div("ob-ladder-row");
            for (int i = 0; i < DreamLadder.Cash.Length; i++)
            {
                var rung = El.Div("ob-ladder-rung", El.Div("ob-ladder-dot"), El.Text(Strings.T("ladder." + DreamLadder.Cash[i]), "ob-ladder-name"));
                rung.name = "ob-ladder-" + i;
                row.Add(rung);
            }
            row.Add(El.Div("ob-ladder-rung ob-ladder-dream", El.Div("ob-ladder-dot"), El.Text(Strings.T("ladder.Hovercar"), "ob-ladder-name")));
            var strip = El.Div("ob-ladder", El.Text(Strings.T("ladder.startHere"), "ob-ladder-title"), row);
            strip.style.display = DisplayStyle.None;
            strip.pickingMode = PickingMode.Ignore;
            return strip;
        }

        /// <summary>One frame of the result reveal: the illustration moves left as its details enter beside it.</summary>
        private static void SetWide(Card c, float t, float w0, float h0, float top0, float ml0, float s0)
        {
            float w = Mathf.Lerp(w0, WideW, t), h = Mathf.Lerp(h0, WideH, t);
            c.root.style.width = w;
            c.root.style.height = h;
            c.root.style.marginLeft = Mathf.Lerp(ml0, -WideW / 2f, t);
            c.root.style.top = Mathf.Lerp(top0, WideTop, t);
            float s = Mathf.Lerp(s0, 1f, t);
            c.root.style.scale = new Scale(new Vector2(s, s));
            c.portrait.style.width = Mathf.Lerp(w0, ResultArtW, t);
            c.portrait.style.height = Mathf.Lerp(h0, ResultArtH, t);
            c.surface.style.opacity = t;
            c.portrait.style.opacity = c.hasSurface ? 1f - t : 1f;
            float info = Mathf.Clamp01((t - 0.72f) / 0.28f); // after the card is wide enough for the name
            c.info.style.opacity = info;
            c.info.style.translate = new Translate((1f - info) * 48f, 0f);
            c.glow.style.opacity = Mathf.Lerp(0.9f, 0f, Mathf.Clamp01(t * 2f));
        }

        private static void BeginWide(Card c)
        {
            c.wide = true;
            c.root.AddToClassList("ob-card--wide");
            // design system v2 (coordinator 2026-10-27): the painted panel stays hidden; the card is a tactile v2 frame with
            // the outcome's art on the left and the live details rebuilt in v2 type, tiles and ladder on the right
            c.root.AddToClassList("ob-card--v2");
            c.hasSurface = false;
            c.surface.style.display = c.hasSurface ? DisplayStyle.Flex : DisplayStyle.None;
            c.ribbon.style.display = DisplayStyle.None;
            c.portrait.style.flexGrow = 0f;
            c.portrait.style.flexShrink = 0f;
            c.info.style.display = DisplayStyle.Flex;
        }

        /// <summary>The flipped card settles left while its origin details appear beside it.</summary>
        private async Task Unfold(Card c)
        {
            float w0 = c.root.resolvedStyle.width, h0 = c.root.resolvedStyle.height, top0 = c.root.resolvedStyle.top;
            float ml0 = c.root.style.marginLeft.value.value;
            float s0 = c.root.resolvedStyle.scale.value.x;
            BeginWide(c);
            SetWide(c, 0f, w0, h0, top0, ml0, s0);
            Kit.Cues.Play(Kit.Cues.SfxSelect, 0.5f);
            await Animate(_table, 0.5f, t => SetWide(c, t, w0, h0, top0, ml0, s0), Ease.OutCubic);
        }

        /// <summary>The same result arrangement without movement (reduced motion, instant results).</summary>
        private static void MakeWide(Card c)
        {
            if (!c.wide) BeginWide(c);
            SetWide(c, 1f, CardW, CardH, 14f, -CardW / 2f, 1f);
        }

        protected override void OnShown()
        {
            OnboardingAudio.StartTheme();
            if (_closed || _started || (!_autoStart && _existingResult == null && !_alreadyRecorded)) return;
            // The first attached frame can still have unresolved card widths. Start once layout has completed, and
            // pause this item explicitly on close: UI Toolkit otherwise resumes scheduled items when reattached.
            _start?.Pause();
            _start = _table.schedule.Execute(() =>
            {
                if (_closed || _started || _table.panel == null || _cards.Count == 0 ||
                    !(_cards[0].root.resolvedStyle.width > 0f)) return;
                _start?.Pause();
                _start = null;
                _started = true;
                if (_existingResult != null)
                {
                    StopIdle();
                    _rollResult = _existingResult;
                    ShowResultInstant(_rollResult);
                }
                else if (_alreadyRecorded)
                {
                    StopIdle();
                    _rollDone = true;
                    ShowRecordedResult();
                    _onDone?.Invoke(null);
                }
                else OnRoll();
            }).Every(16).StartingIn(120);
        }

        private bool IsActive => !_closed && Element?.panel != null;

        private void RequireActive()
        {
            if (!IsActive) throw new OperationCanceledException("Lottery screen closed");
        }

        private async Task Animate(VisualElement owner, float seconds, Action<float> step, Func<float, float> ease = null)
        {
            RequireActive();
            var tween = Tween.Run(owner, seconds, step, ease);
            _tweens.Add(tween);
            try { await tween.Completion; }
            finally { _tweens.Remove(tween); }
            RequireActive();
        }

        private async void Track(Tween tween)
        {
            _tweens.Add(tween);
            try { await tween.Completion; }
            finally { _tweens.Remove(tween); }
        }

        private async Task Delay(float seconds)
        {
            await Kit.Motion.Delay(seconds);
            RequireActive();
        }

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
            if (!IsActive || _rolling || _busy) return;
            if (_rollDone || _rollResult != null)
            {
                _onDone?.Invoke(_rollResult);
                return;
            }
            _started = true;
            _start?.Pause();
            _start = null;
            _rolling = true;
            _rollDone = false;
            _error.style.display = DisplayStyle.None;
            _status.style.display = DisplayStyle.None;
            _frame.Cta.SetEnabled(false);
            _frame.ShowCta(false);
            StopIdle();
            // The odds were shown before the draw; during the shuffle they would pile up, so they step out.
            foreach (var c in _cards) c.odds.style.opacity = 0f;
            Phase?.Invoke("draw");
            try
            {
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
                    if (!IsActive) return;
                    foreach (var card in _cards) { Hot(card, false); card.odds.style.opacity = 1f; }
                    _status.style.display = DisplayStyle.None;
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
                if (!IsActive) return;
                if (_rollResult == null)
                {
                    // The coordinator reconciled the roll with /me. It owns the verified profile and continuation.
                    ShowRecordedResult();
                    _onDone?.Invoke(null);
                    return;
                }
                try { await Reveal(_rollResult); }
                catch (OperationCanceledException) when (!IsActive) { }
                catch (Exception e)
                {
                    Log.Exception("lottery", e);
                    if (IsActive) ShowResultInstant(_rollResult);
                }
            }
            catch (OperationCanceledException) when (!IsActive) { }
            catch (Exception e)
            {
                Log.Exception("lottery", e);
                if (!IsActive) return;
                if (_rollResult != null) ShowResultInstant(_rollResult);
                else if (_rollDone)
                {
                    ShowRecordedResult();
                    ShowCommitError(AppUi.Err(e));
                }
                else
                {
                    _status.style.display = DisplayStyle.None;
                    _error.text = AppUi.Err(e);
                    _error.style.display = DisplayStyle.Flex;
                    _frame.Cta.Text = Strings.T("ob.lottery.retry");
                    _frame.Cta.SetEnabled(true);
                    _frame.ShowCta(true);
                }
            }
            finally { _rolling = false; }
        }

        private void ShowRecordedResult()
        {
            _revealed = true;
            _frame.Progress.Set(CreatorFlow.ProgressSegments);
            _table.style.display = DisplayStyle.None;
            _status.parent.AddToClassList("ob-lottery-status--recorded");
            _status.AddToClassList("ob-lottery-recorded");
            _status.text = Strings.T("ob.lottery.recorded");
            _status.style.display = DisplayStyle.Flex;
            _frame.Cta.Text = Strings.T("ob.lottery.continue");
            _frame.Cta.SetEnabled(true);
            _frame.ShowCta(true, false);
            Phase?.Invoke("result");
        }

        /// <summary>Disables the result action while the coordinator saves the already selected character.</summary>
        public void SetBusy(bool busy)
        {
            _busy = busy;
            if (_frame == null) return;
            _frame.Cta.SetEnabled(!busy && (!_rolling || _revealed));
            _frame.Cta.Text = Strings.T(busy ? "ob.saving" : _revealed ? "ob.lottery.continue" : "ob.lottery.retry");
            if (busy) _error.style.display = DisplayStyle.None;
        }

        /// <summary>A create-character error leaves the origin result intact; retry continues, never rerolls.</summary>
        public void ShowCommitError(string message)
        {
            if (!IsActive) return;
            SetBusy(false);
            _error.text = message ?? "";
            _error.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
            _frame.ShowCta(true, false);
        }

        /// <summary>The shuffle, then the spotlight hopping card to card until at least 2.2 s and the server's answer.</summary>
        private async Task Draw()
        {
            if (DeviceFeel.ReducedMotion)
            {
                float began = Kit.Motion.Now;
                while (_rolling && !_rollDone)
                {
                    if (Kit.Motion.Now - began > 8f) ShowSlowStatus();
                    await Delay(0.1f);
                }
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
                if (elapsed > 8f)
                    ShowSlowStatus();
                await Delay(Mathf.Lerp(0.07f, 0.35f, Mathf.Clamp01(elapsed / 2.2f)));
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
                await Delay(0.36f + 0.06f * h);
                for (int k = 0; k < _cards.Count; k++) Hot(_cards[k], k == (current + h) % _cards.Count);
                Kit.Cues.Hop(Mathf.Min(6, h));
            }
            await Delay(0.3f);
        }

        private void ShowSlowStatus()
        {
            _status.text = Strings.T("ob.lottery.slow");
            _status.style.display = DisplayStyle.Flex;
        }

        /// <summary>The cards gather to the centre and deal back out in a new order: two quick swaps (spec C1).</summary>
        private async Task Shuffle()
        {
            Kit.Cues.Play(Kit.Cues.SfxShuffle, 0.5f); // two riffles, ~1.1 s, both passes
            var starts = _cards.Select(c => c.homeX).ToArray();
            float w = _cards[0].root.resolvedStyle.width;
            for (int pass = 0; pass < 2; pass++)
            {
                await Animate(_table, 0.26f, t =>
                {
                    for (int k = 0; k < _cards.Count; k++)
                    {
                        float target = (k - (_cards.Count - 1) / 2f) * 18f - w / 2f;
                        _cards[k].root.style.marginLeft = Mathf.Lerp(starts[k], target, t);
                        _cards[k].root.style.rotate = new Rotate(new Angle((k - 1) * 6f * t));
                    }
                }, Ease.InCubic);
                // Deal each card to another card's slot; two passes read as a shuffle.
                var order = Enumerable.Range(0, _cards.Count).Select(k => (k + 1 + pass) % _cards.Count).ToArray();
                var gathered = _cards.Select((c, k) => (k - (_cards.Count - 1) / 2f) * 18f - w / 2f).ToArray();
                await Animate(_table, 0.26f, t =>
                {
                    for (int k = 0; k < _cards.Count; k++)
                    {
                        float target = _cards[order[k]].homeX;
                        _cards[k].root.style.marginLeft = Mathf.LerpUnclamped(gathered[k], target, t);
                        _cards[k].root.style.rotate = new Rotate(new Angle((k - 1) * 6f * (1f - t)));
                    }
                }, Ease.OutBack);
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
            var winner = _cards.FirstOrDefault(c => c.id == id);
            if (winner == null)
            {
                // The server's outcome was absent from the locally offered deck. Present the correct result directly;
                // do not animate a different outcome as the winner or depend on this new card's unresolved layout.
                Phase?.Invoke("reveal");
                ShowResultInstant(r);
                return;
            }
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
                Track(Kit.Motion.Fade(winner.root, 0f, 1f, 0.2f));
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
            await Animate(_table, 0.42f, t =>
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
            }, Ease.OutCubic);
            foreach (var c in losers) c.root.style.display = DisplayStyle.None;

            // The flip: scaleX to 0 (darkening like a card edge), swap to the face, open back up with a little overshoot.
            Phase?.Invoke("flip");
            Kit.Cues.Play(Kit.Cues.SfxFlip);
            await Animate(winner.art, 0.17f, t =>
            {
                winner.art.style.scale = new Scale(new Vector2(Mathf.Max(0.001f, 1f - t), 1f + 0.04f * t));
                winner.art.style.unityBackgroundImageTintColor = Color.Lerp(Color.white, new Color(0.55f, 0.5f, 0.6f), t);
            }, Ease.InCubic);
            SetFront(winner, front, id);
            await Animate(winner.art, 0.28f, t =>
            {
                winner.art.style.scale = new Scale(new Vector2(Mathf.Max(0.001f, t), 1.04f - 0.04f * t));
                winner.art.style.unityBackgroundImageTintColor = Color.Lerp(new Color(0.55f, 0.5f, 0.6f), Color.white, Mathf.Clamp01(t * 1.4f));
            }, Ease.OutBack);

            await Delay(0.12f);
            FillCard(winner, r, id);
            await Unfold(winner);

            var centre = CardCentreInBurst(winner);
            Phase?.Invoke("landed");
            if (good)
            {
                Kit.Cues.Play(Kit.Cues.Origin(id));
                DeviceFeel.Confirm();
                Track(Kit.Motion.Pop(winner.art, 1.06f, 0.24f));
                _frame.Burst.Coins(centre, 26);
                Kit.Cues.Play(Kit.Cues.SfxCoins, 0.7f);
                await Delay(0.18f);
                _frame.Burst.Confetti(60);
            }
            else
            {
                // A comic "bonk": squash, a cloud puff and a slow muted shower; the rug sting mixed down so it is not punishing.
                Kit.Cues.Play(Kit.Cues.SfxDebt); // the comic bonk + hopeful tag, mixed 1 dB under the Insider stinger: full volume
                DeviceFeel.Reject();
                Track(Tween.Run(winner.art, 0.36f, t =>
                {
                    float squash = Mathf.Sin(t * Mathf.PI) * 0.06f;
                    winner.art.style.scale = new Scale(new Vector2(1f + squash, 1f - squash));
                }));
                _frame.Burst.Puff(new Vector2(centre.x, centre.y + winner.root.resolvedStyle.height * 0.42f));
                await Delay(0.2f);
                _frame.Burst.Confetti(12, muted: true, slow: true);
            }
            ShowResult(r, id, true);
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
            _frame.Progress.Set(CreatorFlow.ProgressSegments);
            _status.style.display = DisplayStyle.None;
            var c = ResultCard(id);
            MakeWide(c);
            FillCard(c, r, id);
            // the ladder strip comes in once the coin burst has settled, so no particle sits over it
            if (animate && c.ladder != null)
            {
                c.ladder.style.opacity = 0f;
                var strip = c.ladder;
                strip.schedule.Execute(() => strip.style.opacity = 1f).ExecuteLater(1700);
            }
            CountUp(c.cash, r.CashCents, animate);
            _frame.Cta.Text = Strings.T("ob.lottery.continue");
            _frame.Cta.SetEnabled(true);
            Phase?.Invoke("result");
            _showCta?.Pause();
            _showCta = _frame.Cta.schedule.Execute(() =>
            {
                if (IsActive) _frame.ShowCta(true, animate);
            }).StartingIn(animate ? 400 : 0);
        }

        /// <summary>Writes the outcome onto the card (story line, home, debt); Cash shows $0 until it counts up.</summary>
        private void FillCard(Card c, PostLotteryRoll200Response r, string id)
        {
            c.line.text = Strings.T("ob.lottery." + (id == LotteryModel.Exit ? "exit" : id) + ".line");
            c.alias.text = BoundText(PlayerAlias, CreatorFlow.MaxAlias);
            c.alias.style.display = string.IsNullOrEmpty(c.alias.text) ? DisplayStyle.None : DisplayStyle.Flex;
            c.perk.text = BoundText(StarterPerk, 160);
            c.perk.style.display = string.IsNullOrEmpty(c.perk.text) ? DisplayStyle.None : DisplayStyle.Flex;
            string home = "";
            if (Services.TryGet<ConfigStore>(out var config))
                home = config.Registries.Homes.FirstOrDefault(h => h.id == r.HomeId)?.name;
            if (string.IsNullOrEmpty(home)) home = Strings.ReadableId(r.HomeId);
            c.home.text = home;
            c.debt.text = r.DebtCents > 0 ? LotteryModel.CashWhole(r.DebtCents) : "";
            c.debtCell.style.display = r.DebtCents > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            c.cash.text = LotteryModel.CashWhole(0);
            if (c.ladder != null && !string.IsNullOrEmpty(r.HomeId))
            {
                int start = DreamLadder.StartIndex(r.HomeId);
                for (int i = 0; i < DreamLadder.Cash.Length; i++)
                {
                    var rung = c.ladder.Q("ob-ladder-" + i);
                    rung?.EnableInClassList("ob-ladder-rung--here", i == start);
                    rung?.EnableInClassList("ob-ladder-rung--below", i < start);
                }
                c.ladder.style.display = DisplayStyle.Flex;
                Log.Info("lottery", "result card: start rung " + DreamLadder.Cash[Math.Min(start, DreamLadder.Cash.Length - 1)]);
            }
        }

        private static string BoundText(string value, int maxLength) =>
            string.IsNullOrWhiteSpace(value) ? "" : value.Length <= maxLength ? value.Trim() : value.Substring(0, maxLength).Trim();

        private void ShowResultInstant(PostLotteryRoll200Response r)
        {
            string id = LotteryModel.OutcomeId(r.Outcome);
            var winner = ResultCard(id);
            foreach (var c in _cards) c.root.style.display = c.id == id ? DisplayStyle.Flex : DisplayStyle.None;
            winner.art.style.scale = new Scale(Vector2.one);
            winner.art.style.unityBackgroundImageTintColor = Color.white;
            SetFront(winner, OnboardingArt.CardFront(id), id);
            ShowResult(r, id, false);
        }

        /// <summary>Cash counts up over 600 ms. The labels are built once up front, so the ticks assign, not format.</summary>
        private void CountUp(Label label, long cents, bool animate)
        {
            string final = LotteryModel.CashWhole(cents);
            if (!animate || DeviceFeel.ReducedMotion || cents <= 0) { label.text = final; return; }
            const int steps = 12;
            var labels = new string[steps + 1];
            for (int i = 0; i < steps; i++) labels[i] = LotteryModel.CashWhole(cents * i / steps / 100 * 100);
            labels[steps] = final;
            Track(Tween.Run(label, 0.6f, t => label.text = labels[Mathf.Clamp(Mathf.FloorToInt(t * steps), 0, steps)], Ease.OutCubic));
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        /// <summary>Development automation (`autocreate=1`): rolls, then continues after the reveal.</summary>
        public async void AutoRollForAutomation()
        {
            try
            {
                await Delay(0.5f);
                if (!_started) OnRoll();
                while (_rolling) await Delay(0.25f);
                // `holdLottery=1` (dev captures): hold on the result card so it can be photographed after the burst settles
                if (CTLife.Core.LaunchArgs.Flag("holdLottery")) { await Delay(2.5f); Log.Info("lottery", "shot result"); await Delay(6f); }
                await Delay(1f);
                if (_revealed && IsActive) OnRoll();
            }
            catch (OperationCanceledException) when (!IsActive) { }
            catch (Exception e) { Log.Warn("lottery", "Automation roll failed: " + e.Message); }
        }
#endif

        /// <summary>Captures and automation: presses the CTA the way a tap does.</summary>
        public void PressCta() => _frame?.Cta.Press();

        public override void Close()
        {
            _closed = true;
            _rolling = false;
            _start?.Pause();
            _showCta?.Pause();
            StopIdle();
            // Complete pending animation awaits before detachment pauses UI Toolkit's scheduler.
            foreach (var tween in _tweens.ToArray()) tween.Cancel();
            _tweens.Clear();
            _frame?.Burst.Stop();
            base.Close();
        }
    }
}
