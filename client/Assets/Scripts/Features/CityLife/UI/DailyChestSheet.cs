using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;
using Motion = CTLife.UI.Kit.Motion;

namespace CTLife.CityLife
{
    /// <summary>
    /// The daily chest (2026-10-24, backend PR #9 `POST /daily/claim`). Over the dashboard: the real 3D chest
    /// (<see cref="ChestStage"/>) drops in on a plum scrim under the day ("DAY 3"; milestone days 7, 14 and 30 in gold with
    /// stronger rays and confetti) and this run of seven days. Tap: the chest rocks while the claim is in flight, the lid
    /// swings open on its hinge with a golden flash, light rays and a burst of coins, and the reward cards rise (Cash; on
    /// milestones the cosmetic, which is in the wardrobe). COLLECT: the scrim lifts and the coins fly into the Cash pill, which
    /// counts up. A streak reset or a used freeze is said plainly; freezes can be bought here. Sounds are the project's
    /// recorded cues (sfx-map.json). A claimed day opens the same sheet showing the countdown to the next chest.
    /// </summary>
    public sealed class DailyChestSheet : VisualElement
    {
        private static readonly string[] Sounds = { "sfx_sunday_unlock", "sfx_cash_in", "sfx_creator_complete", "sfx_rank_up", "sfx_ui_open", "sfx_ui_tap" };
        private static DailyChestSheet _open;

        private enum Phase { Intro, Idle, Opening, Open, Collecting, Claimed, Done }

        private readonly RetentionService _svc;
        private readonly DailyStatus _status;
        private readonly bool _milestone;
        private readonly VisualElement _scrim, _stageWrap, _chest, _glow, _mouth, _flash, _header, _rewards, _footer, _week;
        private readonly Rays _rays;
        private readonly Label _hint, _notice, _freezeLabel;
        private readonly Button _collect, _close, _buyFreeze;
        private readonly CelebrationBurst _burst;
        private readonly IVisualElementScheduledItem _tick;
        private Phase _phase = Phase.Intro;
        private float _t0, _phaseAt, _openAt;
        private DailyClaim _claim;
        private string _claimKey, _freezeKey;
        private bool _requesting, _responded, _failed, _closing;
        private readonly List<(VisualElement el, Vector2 from, Vector2 ctrl, Vector2 to, float start)> _flying = new List<(VisualElement, Vector2, Vector2, Vector2, float)>();
        private VisualElement _pill; private Label _pillLabel;
        private long _cashFrom, _cashTo; private float _countAt = -1f; private int _landed;

        public static void PrewarmAudio()
        {
            foreach (var id in Sounds) Resources.LoadAsync<AudioClip>("Audio/sfx/" + id);
            // the sheet's icons and style sheets too, so opening it never pays for a synchronous load
            foreach (var icon in new[] { "treasure-chest", "coins", "crown", "snowflake", "bell", "goal" }) Resources.LoadAsync<Texture2D>("UI/Lobby/Icons/" + icon);
            foreach (var sheet in new[] { "UI/kit", "UI/home", "UI/retention" }) Resources.LoadAsync<StyleSheet>(sheet);
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        /// <summary>Dev captures (`gmChest=1`): open the sheet, tap the chest, collect.</summary>
        public static async System.Threading.Tasks.Task DevRoute(System.Threading.CancellationToken ct)
        {
            void Mark(string m) => Debug.Log("CTLIFE [gm-chest] " + m);
            float end = Time.realtimeSinceStartup + 120f;
            while ((RetentionService.Current?.Status == null || CTLife.World.VenueSession.Current == null || !CTLife.World.VenueSession.Current.IsActive) && Time.realtimeSinceStartup < end)
                await Awaitable.WaitForSecondsAsync(0.5f, ct);
            await Awaitable.WaitForSecondsAsync(4f, ct);
            Show(RetentionService.Current);
            if (_open == null) { Mark("FAIL no sheet"); return; }
            await Awaitable.WaitForSecondsAsync(2.5f, ct);
            Mark("shot chest " + (_open._status.ClaimedToday ? "claimed" : "unclaimed"));
            await Awaitable.WaitForSecondsAsync(3f, ct);
            if (!_open._status.ClaimedToday)
            {
                _open.TapChest();
                await Awaitable.WaitForSecondsAsync(5f, ct);
                Mark("shot reward");
                await Awaitable.WaitForSecondsAsync(3f, ct);
                if (_open != null && _open._collect != null) _open.Collect();
                await Awaitable.WaitForSecondsAsync(2f, ct);
            }
            Mark("done");
        }
#endif

        public static void Show(RetentionService svc)
        {
            if (_open != null || svc?.Status == null || UiRoot.Instance == null) return;
            var sheet = new DailyChestSheet(svc);
            UiRoot.Instance.ModalLayer.Add(sheet);
            _open = sheet;
        }

        private DailyChestSheet(RetentionService svc)
        {
            _svc = svc;
            _status = svc.Status;
            bool claimed = _status.ClaimedToday;
            int day = claimed ? Math.Max(1, _status.StreakDay) : _status.ChestDay;
            _milestone = !claimed && DailyStatus.IsMilestone(day);
            name = "gm-chest-sheet";
            AddToClassList("gm-sheet");
            if (_milestone) AddToClassList("gm-sheet--milestone");
            OnboardingFrameSheets(this);
            pickingMode = PickingMode.Position;

            _scrim = El.Div("gm-scrim");
            _scrim.style.backgroundColor = new Color(14f / 255f, 3f / 255f, 30f / 255f, 0.97f);   // linear-space blend: reads as ~80%
            Add(_scrim);

            // ---- header: kicker, DAY n, this run of seven
            var kicker = El.Text(RetentionText.T("chest.kicker"), "gm-kicker");
            var title = LobbyArt.Outlined(_milestone ? RetentionText.F("chest.milestone", day) : RetentionText.F("chest.day", day), "gm-title");
            _week = BuildWeek(day, claimed);
            _header = El.Div("gm-header", kicker, title, _week);
            Add(_header);

            // ---- the stage: rays, glow, the chest, a flash
            _rays = new Rays(_milestone);
            _glow = El.Div("gm-glow");
            _chest = new VisualElement { name = "gm-chest" };
            _chest.AddToClassList("gm-chest");
            _flash = El.Div("gm-flash");
            _mouth = El.Div("gm-mouth");   // light spilling out of the open chest, over the model
            _mouth.style.opacity = 0f;
            _stageWrap = El.Div("gm-stage", _rays, _glow, _chest, _mouth, _flash);
            _stageWrap.RegisterCallback<PointerUpEvent>(_ => TapChest());
            Add(_stageWrap);
            var stage = ChestStage.Instance;
            if (stage.Show()) _chest.style.backgroundImage = Background.FromRenderTexture(stage.Texture);
            foreach (var e in new[] { _rays, _glow, _chest, _mouth, _flash }) e.pickingMode = PickingMode.Ignore;

            _hint = El.Text(claimed ? RetentionText.T("chest.claimed") : RetentionText.T("chest.tap"), "gm-hint");
            _hint.pickingMode = PickingMode.Ignore;
            Add(_hint);

            _rewards = El.Div("gm-rewards");
            _rewards.pickingMode = PickingMode.Ignore;
            Add(_rewards);
            _notice = El.Text("", "gm-notice");
            _notice.Show(false);

            // ---- footer: freezes, collect
            _freezeLabel = El.Text("", "gm-freeze-label");
            _buyFreeze = El.Btn("", BuyFreeze, "gm-freeze-buy");
            _buyFreeze.name = "gm-buy-freeze";
            var freezeRow = El.Div("gm-freeze-row", LobbyArt.Icon("snowflake", "gm-freeze-icon"), _freezeLabel, _buyFreeze);
            // design system v2: COLLECT is the 3D primary (press-down), the dismiss is the back square bottom-left (no X)
            _collect = new Button(Collect) { name = "gm-collect", text = "" };
            _collect.AddToClassList("gm-collect");
            _collect.AddToClassList("ds-btn");
            _collect.AddToClassList("ds-btn--primary");
            _collect.Add(CTLife.UI.DS.Ds.Text(RetentionText.T("chest.collect"), "ds-btn-label"));
            _collect.Show(false);
            _footer = El.Div("gm-footer", _notice, freezeRow, _collect);
            Add(_footer);
            _close = CTLife.UI.DS.Ds.Back(Close);
            _close.name = "gm-close";
            _close.AddToClassList("gm-close-v2");
            Add(_close);

            _burst = new CelebrationBurst(coins: 28, confetti: _milestone ? 110 : 40, puffs: 0);
            Add(_burst);

            RefreshFreezes(_status);
            if (claimed) EnterClaimed(day);
            _t0 = Time.unscaledTime; _phaseAt = _t0;
            Cues.Play("sfx_ui_open");
            _tick = schedule.Execute(Tick).Every(0);
            RegisterCallback<DetachFromPanelEvent>(_ => Teardown());
        }

        private static void OnboardingFrameSheets(VisualElement root)
        {
            foreach (var s in new[] { "UI/kit", "UI/home", "UI/retention" })
            {
                var sheet = Resources.Load<StyleSheet>(s);
                if (sheet != null) root.styleSheets.Add(sheet);
            }
        }

        private VisualElement BuildWeek(int day, bool claimed)
        {
            var row = El.Div("gm-week");
            int start = (day - 1) / 7 * 7 + 1;
            for (int d = start; d < start + 7; d++)
            {
                bool done = d < day || (claimed && d == day), today = d == day && !claimed;
                bool mile = DailyStatus.IsMilestone(d) || d == start + 6;
                var cell = El.Div("gm-day");
                cell.EnableInClassList("gm-day--done", done);
                cell.EnableInClassList("gm-day--today", today);
                cell.EnableInClassList("gm-day--milestone", mile);
                cell.Add(El.Text(d.ToString(), "gm-day-n"));
                var mark = done ? El.Text("✓", "gm-day-mark")
                    : today ? El.Text(RetentionText.Money(_status.Chest.CashCents), "gm-day-cash")
                    : mile ? LobbyArt.Icon("treasure-chest", "gm-day-chest") : El.Text("•", "gm-day-dot");
                cell.Add(mark);
                row.Add(cell);
            }
            foreach (var e in row.Query<VisualElement>().ToList()) e.pickingMode = PickingMode.Ignore;
            return row;
        }

        private void RefreshFreezes(DailyStatus s)
        {
            _freezeLabel.text = RetentionText.F("chest.freezes", s.Freezes, s.MaxFreezes);
            _buyFreeze.text = s.CanBuyFreeze ? RetentionText.F("chest.buyFreeze", RetentionText.Money(s.FreezePriceCents)) : RetentionText.T("chest.freezeFull");
            _buyFreeze.SetEnabled(s.CanBuyFreeze);
            _buyFreeze.tooltip = RetentionText.T("chest.freezeHint");
        }

        private void EnterClaimed(int day)
        {
            _phase = Phase.Claimed;
            _rays.style.opacity = 0.25f;
            _hint.text = RetentionText.T("chest.claimed") + "\n" + RetentionText.F("chest.resetIn", DailyStatus.Countdown(_status.SecondsToReset(ServerClock.NowMs)));
            _hint.AddToClassList("gm-hint--claimed");
            _notice.text = RetentionText.F("chest.claimedBody", day + 1);
            _notice.Show(true);
        }

        // ------------------------------------------------------------------ the claim

        private void TapChest()
        {
            if (_phase != Phase.Idle && _phase != Phase.Intro) return;
            if (_status.ClaimedToday) return;
            DeviceFeel.Tick();
            _phase = Phase.Opening; _phaseAt = Time.unscaledTime;
            _hint.text = RetentionText.T("chest.opening");
            _close.Show(false);
            Claim();
        }

        private async void Claim()
        {
            if (_requesting) return;
            _requesting = true;
            _claimKey ??= Guid.NewGuid().ToString();
            try
            {
                _claim = await _svc.Claim(_claimKey);
                _responded = true;
            }
            catch (Exception e)
            {
                var err = CityErrors.From(e, true);
                Log.Warn("retention", "claim failed: " + err.Code);
                _failed = true;
                _hint.text = RetentionText.F("chest.error", err.Message);
                _close.Show(true);
                Cues.Nope();
            }
            finally { _requesting = false; }
        }

        private void BeginOpen()
        {
            _phase = Phase.Open; _openAt = Time.unscaledTime;
            var stage = ChestStage.Instance;
            stage.Wobble = 0f; stage.Hop = 0f;
            _hint.text = "";
            Cues.Play("sfx_sunday_unlock");
            DeviceFeel.Confirm();
            var c = _claim;
            if (c.Reset) ShowNotice(RetentionText.T("chest.reset"));
            else if (c.FreezesUsed > 0)
                ShowNotice(RetentionText.F("chest.saved", c.FreezesUsed == 1 ? RetentionText.T("chest.saved1") : RetentionText.F("chest.savedN", c.FreezesUsed)));
            else
            {
                int next = DailyStatus.NextMilestone(c.ClaimedDay + 1), left = next - c.ClaimedDay;
                ShowNotice(left == 1 ? RetentionText.F("chest.nextOne", next) : RetentionText.F("chest.next", left, next));
            }
            RefreshFreezes(c);
        }

        private void ShowNotice(string text) { _notice.text = text; _notice.Show(true); }

        private void RevealRewards()
        {
            var c = _claim;
            _rewards.Clear();
            var cash = El.Div("gm-reward gm-reward--cash", LobbyArt.Icon("coins", "gm-reward-icon"),
                El.Div("gm-reward-copy", El.Text("+" + RetentionText.Money(c.Claimed.CashCents), "gm-reward-value"), El.Text(RetentionText.T("chest.cash"), "gm-reward-caption")));
            _rewards.Add(cash);
            if (c.Claimed.HasCosmetic)
                _rewards.Add(El.Div("gm-reward gm-reward--cosmetic", LobbyArt.Icon("crown", "gm-reward-icon"),
                    El.Div("gm-reward-copy", El.Text(c.Claimed.CosmeticName ?? c.Claimed.CosmeticKey, "gm-reward-value"), El.Text(RetentionText.T("chest.wardrobe").ToUpperInvariant(), "gm-reward-caption"))));
            int i = 0;
            foreach (var card in _rewards.Children())
            {
                card.pickingMode = PickingMode.Ignore;
                var el = card; float delay = 0.09f * i++;
                el.style.opacity = 0f;
                el.schedule.Execute(() => Motion.Enter(el, 0f, 46f, 0.32f)).StartingIn((long)(delay * 1000));
            }
            Cues.Play(_milestone || c.Claimed.HasCosmetic ? "sfx_rank_up" : "sfx_creator_complete", 0.8f);
            _collect.Show(true);
            Motion.Enter(_collect, 0f, 30f, 0.3f);
        }

        // ------------------------------------------------------------------ per frame

        private void Tick()
        {
            if (_phase == Phase.Done || panel == null) return;
            float now = Time.unscaledTime, t = now - _t0;
            var stage = ChestStage.Instance;
            // the prefab may still be loading on a very early tap: attach the texture once it is in
            if (_chest.resolvedStyle.backgroundImage.renderTexture == null && stage.Show()) _chest.style.backgroundImage = Background.FromRenderTexture(stage.Texture);
            // intro: scrim, header and the chest dropping in with a bounce
            float intro = Mathf.Clamp01(t / 0.5f);
            if (!_closing) _scrim.style.opacity = Ease.OutCubic(intro);
            _header.style.opacity = Ease.OutCubic(Mathf.Clamp01((t - 0.08f) / 0.35f));
            _header.style.translate = new Translate(0, (1f - Ease.OutCubic(Mathf.Clamp01((t - 0.08f) / 0.35f))) * -40f);
            float drop = Mathf.Clamp01((t - 0.05f) / 0.55f);
            float dy = (1f - Ease.OutBack(drop)) * -260f;
            if (!_closing) _stageWrap.style.translate = new Translate(0, dy);
            _stageWrap.style.opacity = Mathf.Clamp01(drop * 3f) * (_closing ? 0f : 1f);
            if (_phase == Phase.Intro && t > 0.6f) { _phase = Phase.Idle; _phaseAt = now; }

            switch (_phase)
            {
                case Phase.Idle:
                {
                    float k = Mathf.Sin((now - _phaseAt) * 2.4f);
                    stage.Hop = 0.012f * (0.5f + 0.5f * k);
                    stage.Yaw = 162f + 6f * Mathf.Sin((now - _phaseAt) * 0.7f);
                    _hint.style.opacity = 0.65f + 0.35f * (0.5f + 0.5f * Mathf.Sin((now - _phaseAt) * 3.4f));
                    _glow.style.opacity = 0.35f + 0.1f * k;
                    _rays.style.opacity = _milestone ? 0.35f : 0.18f;
                    break;
                }
                case Phase.Opening:
                {
                    // anticipation: three growing rocks, then hold the rattle until the server answers
                    float e = now - _phaseAt;
                    float amp = Mathf.Lerp(3f, 9f, Mathf.Clamp01(e / 0.9f));
                    stage.Wobble = Mathf.Sin(e * 26f) * amp;
                    stage.Hop = Mathf.Abs(Mathf.Sin(e * 13f)) * 0.03f;
                    float sq = 1f + Mathf.Sin(e * 26f) * 0.015f;
                    _chest.style.scale = new Scale(new Vector3(1f / sq, sq, 1f));
                    _glow.style.opacity = 0.35f + 0.25f * Mathf.Clamp01(e / 0.9f);
                    if (Mathf.FloorToInt(e * 26f / Mathf.PI) != Mathf.FloorToInt((e - Time.unscaledDeltaTime) * 26f / Mathf.PI) && e < 0.95f) Cues.Play(Cues.SfxTap, 0.35f);
                    if (_failed) { stage.Wobble = 0f; _chest.style.scale = new Scale(Vector3.one); _phase = Phase.Idle; _phaseAt = now; _failed = false; }
                    else if (_responded && e > 0.9f) { _chest.style.scale = new Scale(Vector3.one); BeginOpen(); }
                    break;
                }
                case Phase.Open:
                {
                    float e = now - _openAt;
                    stage.Lid = Ease.OutBack(Mathf.Clamp01(e / 0.42f));
                    stage.KeyBoost = Mathf.Clamp01(1f - e / 0.8f) * 0.9f;
                    stage.Yaw = Mathf.Lerp(stage.Yaw, 162f, Time.unscaledDeltaTime * 4f);
                    float pop = e < 0.12f ? e / 0.12f : Mathf.Clamp01(1f - (e - 0.12f) / 0.5f);
                    _flash.style.opacity = pop * 0.85f;
                    _flash.style.scale = new Scale(Vector3.one * (0.6f + 0.9f * Mathf.Clamp01(e / 0.4f)));
                    float glowIn = Ease.OutCubic(Mathf.Clamp01(e / 0.35f));
                    _glow.style.opacity = 0.35f + 0.65f * glowIn;
                    float lidUp = Mathf.Clamp01((e - 0.1f) / 0.35f);
                    _mouth.style.opacity = lidUp * (0.85f + 0.15f * Mathf.Sin(e * 5f));
                    float gs = 1f + 0.35f * glowIn + 0.05f * Mathf.Sin(e * 3f);
                    _glow.style.scale = new Scale(new Vector3(gs, gs, 1f));
                    _rays.style.opacity = Mathf.Lerp(_milestone ? 0.35f : 0.18f, _milestone ? 1f : 0.8f, glowIn);
                    float rs = 0.7f + 0.3f * glowIn;
                    _rays.style.scale = new Scale(new Vector3(rs, rs, 1f));
                    if (e >= 0.16f && e - Time.unscaledDeltaTime < 0.16f)
                    {
                        var mouth = _burst.WorldToLocal(_chest.LocalToWorld(new Vector2(_chest.resolvedStyle.width * 0.5f, _chest.resolvedStyle.height * 0.42f)));
                        _burst.Coins(mouth, _milestone ? 28 : 18);
                        if (_milestone) _burst.Confetti(110);
                        Cues.Play("sfx_cash_in", 0.7f);
                    }
                    if (e >= 0.5f && e - Time.unscaledDeltaTime < 0.5f) RevealRewards();
                    break;
                }
                case Phase.Claimed:
                    stage.Yaw = 162f + 6f * Mathf.Sin(t * 0.7f);
                    if (Time.frameCount % 15 == 0)
                        _hint.text = RetentionText.T("chest.claimed") + "\n" + RetentionText.F("chest.resetIn", DailyStatus.Countdown(_status.SecondsToReset(ServerClock.NowMs)));
                    break;
                case Phase.Collecting:
                    TickCollect(now);
                    break;
            }
            _rays.Spin(Time.unscaledDeltaTime * (_phase == Phase.Open || _phase == Phase.Collecting ? 22f : 8f));
        }

        // ------------------------------------------------------------------ collect: the coins fly to the Cash pill

        private void Collect()
        {
            if (_phase != Phase.Open || _claim == null) return;
            DeviceFeel.Tick();
            _phase = Phase.Collecting; _phaseAt = Time.unscaledTime;
            _closing = true;
            _collect.SetEnabled(false);
            var root = panel?.visualTree;
            _pill = root?.Q("home-chip-cash") ?? root?.Q("hud-cash");
            _pillLabel = _pill?.Q<Label>(className: "home-pill-value") ?? _pill?.Q<Label>(className: "ct-cash");
            var profile = Services.TryGet<PlayerProfile>(out var p) ? p : null;
            _cashFrom = profile?.CashCents ?? 0;
            _cashTo = _cashFrom + (_claim.AlreadyClaimed ? 0 : _claim.Claimed.CashCents);
            Vector2 to = _pill != null ? this.WorldToLocal(_pill.worldBound.center) : new Vector2(resolvedStyle.width * 0.5f, 60f);
            if (_pill != null) to.x = this.WorldToLocal(new Vector2(_pill.worldBound.xMin + 46f, 0)).x;
            var c = this.WorldToLocal(_chest.worldBound.center);
            int n = _claim.AlreadyClaimed ? 0 : 12;
            for (int i = 0; i < n; i++)
            {
                var coin = El.Div("kit-burst__coin gm-fly-coin", El.Div("kit-burst__coin-hi"));
                coin.pickingMode = PickingMode.Ignore;
                Add(coin);
                var from = c + new Vector2(UnityEngine.Random.Range(-110f, 110f), UnityEngine.Random.Range(-90f, 20f));
                var ctrl = new Vector2(Mathf.Lerp(from.x, to.x, 0.3f) + UnityEngine.Random.Range(-220f, 220f), Mathf.Min(from.y, to.y) - UnityEngine.Random.Range(120f, 320f));
                _flying.Add((coin, from, ctrl, to, i * 0.045f));
                coin.style.translate = new Translate(from.x, from.y);
            }
        }

        private void TickCollect(float now)
        {
            float e = now - _phaseAt;
            float fade = Mathf.Clamp01(1f - e / 0.28f);
            _scrim.style.opacity = fade; _header.style.opacity = fade; _rewards.style.opacity = fade; _footer.style.opacity = fade;
            _hint.style.opacity = fade; _stageWrap.style.opacity = fade;
            for (int i = 0; i < _flying.Count; i++)
            {
                var (el, a, ctrl, b, start) = _flying[i];
                float u = Mathf.Clamp01((e - 0.12f - start) / 0.62f);
                if (u <= 0f) continue;
                float k = Ease.InCubic(u) * 0.55f + u * 0.45f;
                var pos = (1 - k) * (1 - k) * a + 2 * (1 - k) * k * ctrl + k * k * b;
                el.style.translate = new Translate(pos.x, pos.y);
                float s = Mathf.Lerp(1f, 0.55f, u);
                el.style.scale = new Scale(new Vector3(s, s, 1f));
                if (u >= 1f && el.style.display != DisplayStyle.None)
                {
                    el.style.display = DisplayStyle.None;
                    _landed++;
                    if (_landed == 1) { Cues.Play("sfx_cash_in"); _countAt = now; }
                    if (_landed % 3 == 1) DeviceFeel.Tick();
                    PunchPill(now);
                }
            }
            if (_countAt > 0f && _pillLabel != null)
            {
                float u = Mathf.Clamp01((now - _countAt) / 0.75f);
                long v = _cashFrom + (long)Math.Round((_cashTo - _cashFrom) * Ease.OutCubic(u));
                _pillLabel.text = _pill.name == "hud-cash" ? Money.Cash(v) : LobbyAmounts.Cash(v);
            }
            if (_pill != null)
            {
                float since = now - _pillPunchAt;
                float ps = 1f + 0.12f * Mathf.Clamp01(1f - since / 0.18f);
                _pill.style.scale = new Scale(new Vector3(ps, ps, 1f));
            }
            bool coinsDone = _landed >= _flying.Count;
            if (coinsDone && (_countAt < 0f || now - _countAt > 0.8f) && e > 0.35f) Finish();
        }

        private float _pillPunchAt = -10f;
        private void PunchPill(float now) => _pillPunchAt = now;

        private void Finish()
        {
            if (_phase == Phase.Done) return;
            _phase = Phase.Done;
            if (_pill != null) _pill.style.scale = new Scale(Vector3.one);
            if (Services.TryGet<PlayerProfile>(out var profile))
            {
                if (_claim != null && !_claim.AlreadyClaimed) profile.SetCash(_cashTo);
                _ = profile.Refresh().ContinueWith(_ => { });
            }
            var claim = _claim;
            RemoveFromHierarchy();
            if (claim != null && !claim.AlreadyClaimed && RetentionService.ShouldAskPush) PushPrePrompt.Show(_svc);
        }

        private void Close()
        {
            if (_phase == Phase.Opening && !_failed) return;
            Cues.Play("sfx_ui_close");
            RemoveFromHierarchy();
        }

        private async void BuyFreeze()
        {
            var s = _svc.Status;
            if (s == null || !s.CanBuyFreeze) return;
            _buyFreeze.SetEnabled(false);
            _freezeKey ??= Guid.NewGuid().ToString();
            try
            {
                var after = await _svc.BuyFreeze(_freezeKey);
                _freezeKey = null;
                RefreshFreezes(after);
                Cues.Play("sfx_ui_success");
                if (Services.TryGet<PlayerProfile>(out var profile)) _ = profile.Refresh().ContinueWith(_ => { });
            }
            catch (Exception e)
            {
                var err = CityErrors.From(e, true);
                if (!err.OutcomeUnknown) _freezeKey = null;
                ShowNotice(err.Message);
                Cues.Nope();
                RefreshFreezes(_svc.Status ?? s);
            }
        }

        private void Teardown()
        {
            _tick?.Pause();
            _burst.Stop();
            var stage = ChestStage.Instance;
            stage.Hide();
            stage.Lid = 0f; stage.Wobble = 0f; stage.Hop = 0f;
            if (ReferenceEquals(_open, this)) _open = null;
        }

        /// <summary>Soft light rays behind the chest (Painter2D wedges), turning slowly; gold on milestones.</summary>
        private sealed class Rays : VisualElement
        {
            private readonly bool _gold;
            private float _angle;
            public Rays(bool gold)
            {
                _gold = gold;
                AddToClassList("gm-rays");
                generateVisualContent += Draw;
            }

            public void Spin(float degrees)
            {
                _angle = (_angle + degrees) % 360f;
                style.rotate = new Rotate(_angle);
            }

            private void Draw(MeshGenerationContext ctx)
            {
                var r = contentRect;
                if (r.width <= 0 || float.IsNaN(r.width)) return;
                var c = r.center; float R = Mathf.Min(r.width, r.height) * 0.5f;
                var p = ctx.painter2D;
                // four nested passes per ray fake a falloff: bright at the chest, fading out to the tip
                var col = _gold ? new Color(1f, 0.80f, 0.28f, 0.13f) : new Color(1f, 0.90f, 0.62f, 0.09f);
                const int N = 14;
                for (int pass = 0; pass < 4; pass++)
                {
                    float len = R * (0.4f + 0.2f * pass);
                    for (int i = 0; i < N; i++)
                    {
                        float a0 = (i / (float)N) * Mathf.PI * 2f, a1 = a0 + Mathf.PI / N * (0.62f - 0.08f * pass);
                        p.fillColor = col;
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
}
