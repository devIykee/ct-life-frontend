using System;
using CTLife.Core;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// What the title screen is waiting on. Real steps only: each one completes when the flow says so (config fetched or
    /// fallen back, stored session resumed or not), and the bar shows the share of steps done. Pure: unit-tested.
    /// </summary>
    public sealed class TitleBoot
    {
        public enum Step { Config = 0, Session = 1 }
        private const int Steps = 2;
        private readonly bool[] _done = new bool[Steps];

        public Step Current { get; private set; } = Step.Config;
        public string Error { get; private set; }

        /// <summary>0..1, the completed share of the boot steps. Never goes backwards (a retry keeps finished steps).</summary>
        public float Progress
        {
            get { int n = 0; foreach (bool d in _done) if (d) n++; return n / (float)Steps; }
        }

        public bool Ready => Error == null && Progress >= 1f;

        public void Begin(Step step) { Current = step; Error = null; }
        public void Complete(Step step) { _done[(int)step] = true; Error = null; }
        public void Fail(string error) => Error = string.IsNullOrEmpty(error) ? "?" : error;

        /// <summary>The status line for the step in progress (Strings keys).</summary>
        public string StatusKey => Error != null ? null : Ready ? "title.tap" : Current == Step.Config ? "title.status.connecting" : "title.status.signingIn";
    }

    /// <summary>
    /// The title screen (approved 2026-10-16, mock `title-final-B-v3`): the Blender key art full-bleed with a slow Ken Burns
    /// drift, the CT LIFE lockup centred, and at the bottom a thin bar with the real boot progress, the status line (or the real
    /// error with a retry), then "Tap to play" pulsing once ready. A tap anywhere continues: a saved session goes straight on;
    /// without one a white bottom sheet offers "Sign in with Solana" (the existing MWA / Seed Vault sign-in and its error copy).
    /// Wallet switching belongs in the in-game Settings sign-out flow. Styles: Resources/UI/title.uss. Art: Resources/UI/Title
    /// (art/LICENSES.md, "Title screen").
    /// </summary>
    public sealed class SignInScreen : UiScreen
    {
        private readonly Action _onWallet, _onPlay;
        private readonly bool _walletAvailable;
        private bool _returning, _ready = true, _busy, _shownOnce;
        private Action _retry;
        private VisualElement _root, _bg, _logo, _bar, _fill, _sheetLayer, _sheet;
        private Label _status, _error, _sheetError, _walletLabel;
        private Button _retryBtn, _walletBtn;
        private IVisualElementScheduledItem _pulse;
        private Tween _sheetMotion;
        private float _shownProgress;
        private static Texture2D _shade;

        /// <param name="onWallet">Runs the wallet sign-in (from the sheet's button).</param>
        /// <param name="returning">A valid session exists: a tap continues with <paramref name="onPlay"/>.</param>
        public SignInScreen(Action onWallet, bool walletAvailable, bool returning = false, Action onPlay = null)
        {
            _onWallet = onWallet;
            _walletAvailable = walletAvailable;
            _returning = returning && onPlay != null;
            _onPlay = onPlay;
        }

        private static bool LoginUp => Features.IsAvailable(Features.Login);
        public bool SheetOpen => _sheetLayer != null && _sheetLayer.parent != null;
        public bool Ready => _ready;

        protected override VisualElement Build()
        {
            _bg = El.Div("ct-title-bg");
            _bg.pickingMode = PickingMode.Ignore;
            // Bottom shade (mock .shade-b: 50% tall, rgba(10,6,24,.55) at the edge to clear): USS has no gradients.
            var shade = El.Div("ct-title-shade");
            shade.pickingMode = PickingMode.Ignore;
            shade.style.backgroundImage = Background.FromTexture2D(Shade);

            _logo = El.Div("ct-title-logo");
            _logo.name = "title-logo";
            _logo.pickingMode = PickingMode.Ignore;

            _status = El.Text("", "ct-title-status");
            _status.name = "title-status";
            _status.pickingMode = PickingMode.Ignore;
            _error = El.Text("", "ct-title-error");
            _error.name = "title-error";
            _error.pickingMode = PickingMode.Ignore;
            _retryBtn = new Button(() => { DeviceFeel.Tick(); Bus.Publish(new SfxSignal("sfx_ui_tap")); var r = _retry; _retry = null; r?.Invoke(); })
                { text = Strings.T("title.retry"), enableRichText = false, name = "title-retry" };
            _retryBtn.AddToClassList("ct-title-retry");
            _retryBtn.Show(false);
            _fill = El.Div("ct-title-bar-fill");
            _fill.pickingMode = PickingMode.Ignore;
            _bar = El.Div("ct-title-bar", _fill);
            _bar.name = "title-bar";
            _bar.pickingMode = PickingMode.Ignore;
            var legal = El.Text(Strings.T("title.legal"), "ct-title-legal");
            legal.pickingMode = PickingMode.Ignore;
            var bottom = El.Div("ct-title-bottom", _error, _retryBtn, _status, _bar, legal);
            bottom.pickingMode = PickingMode.Ignore;

            _root = El.Div("ct-title", _bg, shade, _logo, bottom);
            _root.name = "title-root";
            var sheet = Resources.Load<StyleSheet>("UI/title");
            if (sheet != null) _root.styleSheets.Add(sheet);
            else Log.Warn("ui", "Missing UI/title stylesheet");
            // Tap anywhere (not on the retry button or the sheet) continues once ready.
            _root.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.target is VisualElement t && (t == _retryBtn || _retryBtn.Contains(t) || t.panel == null)) return; // retry, or the sheet's scrim just closed it
                if (SheetOpen) return;
                Continue();
            });
            // Full bleed: the screen sits inside the safe area; the art (and its shade) extends under the cutout/edges.
            SafeAreaBleed.Attach(_root, _bg);
            SafeAreaBleed.Attach(_root, shade);
            SetFill(_ready ? 1f : 0f, true);
            Refresh();
            StartMotion();
            return _root;
        }

        /// <summary>Linear vertical ramp, transparent at the top to the mock's plum-black 55% at the bottom (1×64, built once).</summary>
        private static Texture2D Shade
        {
            get
            {
                if (_shade != null) return _shade;
                _shade = new Texture2D(1, 64, TextureFormat.RGBA32, false) { name = "Title shade", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                for (int y = 0; y < 64; y++) _shade.SetPixel(0, y, new Color(10f / 255f, 6f / 255f, 24f / 255f, 0.55f * (1f - y / 63f)));
                _shade.Apply(false, true);
                return _shade;
            }
        }

        // ------------------------------------------------------------------ flow API

        /// <summary>Boot in progress: the bar shows the real share of steps done, the line says what it is waiting on.</summary>
        public void SetLoading(float progress, string status)
        {
            _ready = false;
            _retry = null;
            _error.text = "";
            _status.text = status ?? "";
            SetFill(progress, false);
            Refresh();
        }

        /// <summary>Boot finished: full bar, "Tap to play". <paramref name="returning"/>: a saved session continues straight on.</summary>
        public void SetReady(bool returning)
        {
            _returning = returning && _onPlay != null;
            _ready = true;
            _retry = null;
            _error.text = "";
            SetFill(1f, false);
            Refresh();
            if (!LoginUp && !_returning) ShowError(Strings.T("welcome.unavailable"));
        }

        /// <summary>Boot failed: the real error and a retry (the bar keeps the steps already done).</summary>
        public void ShowBootError(string error, Action retry)
        {
            _ready = false;
            _retry = retry;
            _error.text = error ?? "";
            Refresh();
        }

        /// <summary>Kept for callers: a returning session continues on tap; otherwise the tap opens the sign-in sheet.</summary>
        public void SetReturning(bool returning) => SetReady(returning);

        public void SetBusy(bool busy)
        {
            _busy = busy;
            if (_walletBtn != null)
            {
                _walletBtn.SetEnabled(!busy && _walletAvailable && LoginUp);
                if (_walletLabel != null) _walletLabel.text = Strings.T(busy ? "welcome.busy" : "welcome.wallet");
            }
            if (busy) ShowError("");
        }

        /// <summary>Sign-in errors (wallet declined, server code). In the sheet when it is open, else above the bar.</summary>
        public void ShowError(string text)
        {
            text = text ?? "";
            if (SheetOpen && _sheetError != null)
            {
                _sheetError.text = text;
                _sheetError.Show(text.Length > 0);
                return;
            }
            if (_error == null) return;
            if (_retry == null) _error.text = text;
            Refresh();
        }

        private void Refresh()
        {
            if (_status == null) return;
            bool failed = _retry != null;
            _retryBtn.Show(failed);
            _error.Show(!string.IsNullOrEmpty(_error.text));
            _status.Show(!failed);
            if (_ready) _status.text = Strings.T("title.tap").ToUpperInvariant();
            _status.EnableInClassList("ct-title-tap", _ready);
            _bar.EnableInClassList("ct-title-bar-failed", failed);
            if (!_ready) _status.style.opacity = 1f;
        }

        private void SetFill(float progress, bool instant)
        {
            float target = Mathf.Clamp01(progress);
            if (_fill == null) { _shownProgress = target; return; }
            float from = _shownProgress;
            _shownProgress = Mathf.Max(_shownProgress, target); // real progress only moves forward
            if (instant || DeviceFeel.ReducedMotion || _fill.panel == null) { _fill.style.width = Length.Percent(_shownProgress * 100f); return; }
            float to = _shownProgress;
            Tween.Run(_fill, 0.35f, t => _fill.style.width = Length.Percent(Mathf.Lerp(from, to, t) * 100f), Ease.OutCubic);
        }

        // ------------------------------------------------------------------ tap → continue / sign-in sheet

        private void Continue()
        {
            if (!_ready || _busy) return;
            DeviceFeel.Tick();
            Bus.Publish(new SfxSignal("sfx_ui_tap"));
            if (_returning) { _onPlay?.Invoke(); return; }
            OpenSignIn();
        }

        /// <summary>The sign-in sheet: white, iOS-like (Duo kit language), one primary "Sign in with Solana" squircle.</summary>
        public void OpenSignIn()
        {
            if (_root == null || SheetOpen) return;
            var scrim = new Button(CloseSignIn) { tooltip = Strings.T("common.cancel") };
            scrim.AddToClassList("ct-title-sheet-scrim");
            _sheet = El.Div("ct-title-sheet");
            _sheet.name = "title-signin-sheet";
            _sheet.Add(El.Div("ct-title-sheet-grabber"));
            _sheet.Add(El.Text(Strings.T("title.sheet.title"), "ct-title-sheet-title"));
            _sheet.Add(El.Text(Strings.T("welcome.walletNote"), "ct-title-sheet-body"));
            _walletBtn = new Button(() => { if (_busy) return; DeviceFeel.Tick(); Bus.Publish(new SfxSignal("sfx_ui_tap")); _onWallet?.Invoke(); })
                { name = "title-signin", tooltip = Strings.T("welcome.wallet") };
            _walletBtn.AddToClassList("ct-title-sheet-primary");
            // Icon + label as children (a Button's own text would centre under the icon).
            var mark = Resources.Load<Texture2D>("UI/Brand/solana-logomark");
            if (mark != null)
            {
                var icon = El.Div("ct-title-sheet-primary-icon");
                icon.style.backgroundImage = Background.FromTexture2D(mark);
                icon.pickingMode = PickingMode.Ignore;
                _walletBtn.Add(icon);
            }
            _walletLabel = El.Text(Strings.T("welcome.wallet"), "ct-title-sheet-primary-label");
            _walletLabel.name = "title-signin-label";
            _walletLabel.pickingMode = PickingMode.Ignore;
            _walletBtn.Add(_walletLabel);
            _sheet.Add(_walletBtn);
            _sheetError = El.Text("", "ct-title-sheet-error");
            _sheetError.name = "title-signin-error";
            _sheet.Add(_sheetError);
            _sheet.Add(El.Text(Strings.T("title.legal"), "ct-title-sheet-legal"));
            _sheetLayer = El.Div("ct-title-sheet-layer", scrim, _sheet);
            _sheetLayer.name = "title-signin-layer";
            _root.Add(_sheetLayer);
            // The dim covers the whole screen like the key art: bleed the scrim past the safe area (cutout) on every edge.
            scrim.RegisterCallback<GeometryChangedEvent>(_ => SafeAreaBleed.Apply(_root, scrim));
            scrim.RegisterCallback<AttachToPanelEvent>(_ => SafeAreaBleed.Apply(_root, scrim));
            SafeAreaBleed.Apply(_root, scrim);
            SetBusy(_busy);
            string note = !LoginUp ? Strings.T("welcome.unavailable") : !_walletAvailable ? Strings.T("welcome.walletAndroid") : "";
            _sheetError.text = note;
            _sheetError.Show(note.Length > 0);
            _sheetMotion?.Cancel();
            _sheetLayer.style.opacity = 0f;
            _sheetMotion = Tween.Run(_sheetLayer, 0.24f, t =>
            {
                _sheetLayer.style.opacity = Mathf.Clamp01(t * 1.6f);
                _sheet.style.translate = new Translate(0, (1f - t) * 420f);
            }, Ease.OutCubic);
            if (DeviceFeel.ReducedMotion) { _sheetLayer.style.opacity = 1f; _sheet.style.translate = new Translate(0, 0); }
        }

        public void CloseSignIn()
        {
            if (!SheetOpen || _busy) return;
            var layer = _sheetLayer;
            _sheetMotion?.Cancel();
            void Gone() { layer.RemoveFromHierarchy(); if (_sheetLayer == layer) { _sheetLayer = null; _sheet = null; _walletBtn = null; _walletLabel = null; _sheetError = null; } }
            if (DeviceFeel.ReducedMotion) { Gone(); return; }
            _sheetMotion = Tween.Run(layer, 0.18f, t =>
            {
                layer.style.opacity = 1f - t;
                if (_sheet != null) _sheet.style.translate = new Translate(0, t * 420f);
                if (t >= 1f) Gone();
            }, Ease.OutCubic, skipWhenReduced: false);
        }

        // ------------------------------------------------------------------ motion

        /// <summary>
        /// A slow 1.5% Ken Burns drift on the art (USS transition, 20 s, ping-pong), the lockup scaling in once, and "Tap to
        /// play" pulsing gently once ready. All off when the phone's "Remove animations" is on.
        /// </summary>
        private void StartMotion()
        {
            if (DeviceFeel.ReducedMotion) return;
            _bg.RegisterCallback<TransitionEndEvent>(_ => _bg.ToggleInClassList("ct-title-zoom"));
            _bg.schedule.Execute(() => _bg.AddToClassList("ct-title-zoom")).StartingIn(200);
            float start = Time.realtimeSinceStartup;
            _pulse = _root.schedule.Execute(() =>
            {
                if (!_ready || _status == null) return;
                float t = Time.realtimeSinceStartup - start;
                _status.style.opacity = 0.62f + 0.38f * (0.5f + 0.5f * Mathf.Cos(t * 2f * Mathf.PI / 1.8f));
            }).Every(33);
        }

        protected override void OnShown()
        {
            OnboardingAudio.StartTheme();
            if (_shownOnce || _logo == null) return;
            _shownOnce = true;
            if (DeviceFeel.ReducedMotion) return;
            _logo.style.opacity = 0f;
            Tween.Run(_logo, 0.6f, t =>
            {
                _logo.style.opacity = Mathf.Clamp01(t * 1.8f);
                float s = Mathf.Lerp(0.9f, 1f, Ease.OutBack(t));
                _logo.style.scale = new Scale(new Vector3(s, s, 1f));
            });
        }

        public override void Close()
        {
            _pulse?.Pause();
            _pulse = null;
            _sheetMotion?.Cancel();
            base.Close();
        }

        // ------------------------------------------------------------------ shared button art (LippedButton)

        /// <summary>
        /// Side slice of the squircle bodies (btn-*-blank-sq.png, corner radius ~22% of the height) as a fraction of the
        /// texture height: the corner plus its antialiasing and rim shading, so no curved pixel is ever stretched.
        /// `TitleButtonArtTests` measures the painted corner and checks this covers it. Used by the UI kit's LippedButton.
        /// </summary>
        public const float SquircleSideSlice = 0.30f;

        /// <summary>Resource path of a painted button body: the squircle body when present, else the older pill.</summary>
        public static string ButtonArtResource(string tone, out bool squircle)
        {
            var sq = "UI/Title/btn-" + tone + "-blank-sq";
            squircle = Resources.Load<Texture2D>(sq) != null;
            return squircle ? sq : "UI/Title/btn-" + tone + "-blank";
        }
    }
}
