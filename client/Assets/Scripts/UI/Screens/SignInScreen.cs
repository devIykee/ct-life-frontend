using System;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// The title screen (approved art direction, 2026-10-08): the painted town square full-bleed and one big button in the
    /// open cobblestones. A new player gets "Sign in with Solana" (SIWS, contract section 3, the only public entry). A returning
    /// player with a valid saved session gets "PLAY" and a small "Use another wallet" link that signs out and swaps back.
    /// Styles: Resources/UI/title.uss. Art: Resources/UI/Title (art/LICENSES.md, "Title screen").
    /// </summary>
    public sealed class SignInScreen : UiScreen
    {
        private readonly Action _onWallet, _onPlay, _onAnotherWallet;
        private readonly bool _walletAvailable;
        private bool _returning;
        private VisualElement _root, _bg, _actions;
        private Button _walletBtn, _playBtn, _another;
        private Label _error;
        private IVisualElementScheduledItem _idle;

        public SignInScreen(Action onWallet, bool walletAvailable, bool returning = false, Action onPlay = null, Action onAnotherWallet = null)
        {
            _onWallet = onWallet;
            _walletAvailable = walletAvailable;
            _returning = returning && onPlay != null;
            _onPlay = onPlay;
            _onAnotherWallet = onAnotherWallet;
        }

        private static bool LoginUp => Features.IsAvailable(Features.Login);

        protected override VisualElement Build()
        {
            _bg = El.Div("ct-title-bg");
            _bg.pickingMode = PickingMode.Ignore;

            _walletBtn = TitleButton(Strings.T("welcome.wallet"), () => _onWallet?.Invoke(), "green");
            _walletBtn.name = "title-signin";
            _walletBtn.SetEnabled(_walletAvailable && LoginUp);
            _playBtn = TitleButton(Strings.T("welcome.play"), () => _onPlay?.Invoke(), "purple");
            _playBtn.name = "title-play";

            // Secondary text button beside PLAY: outlined game text, underlined, with a 48 dp transparent hit area.
            // Fixed, trusted copy: rich text only for the underline.
            _another = new Button(() =>
            {
                Bus.Publish(new SfxSignal("sfx_ui_tap"));
                _onAnotherWallet?.Invoke();
            }) { text = "<u>" + Strings.T("welcome.anotherWallet") + "</u>", enableRichText = true, name = "title-another-wallet" };
            _another.AddToClassList("ct-title-link");
            _error = El.Text("", "ct-title-error");

            var side = El.Div("ct-title-side", _another);
            side.pickingMode = PickingMode.Ignore;
            var under = El.Div("ct-title-under", _error);
            under.pickingMode = PickingMode.Ignore;
            _actions = El.Div("ct-title-actions", _walletBtn, _playBtn, side, under);
            // Haptic tick on press (the parent sees the press before the button's own handlers).
            _actions.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target is VisualElement t && (Pressed(_walletBtn, t) || Pressed(_playBtn, t))) DeviceFeel.Tick();
            }, TrickleDown.TrickleDown);

            var legalText = El.Text(Strings.T("welcome.age"), "ct-title-legal");
            legalText.pickingMode = PickingMode.Ignore;
            var legal = El.Div("ct-title-legal-row", legalText);
            legal.pickingMode = PickingMode.Ignore;
            // No plate or gradient behind the copy: the outlined text carries itself (title.uss).
            _root = El.Div("ct-title", _bg, _actions, legal);
            var sheet = Resources.Load<StyleSheet>("UI/title");
            if (sheet != null) _root.styleSheets.Add(sheet);
            else Log.Warn("ui", "Missing UI/title stylesheet");

            // Full bleed: the screen sits inside the safe area; the art extends under the cutout/edges.
            SafeAreaBleed.Attach(_root, _bg);
            ApplyButtonArt(_walletBtn, "green");
            ApplyButtonArt(_playBtn, "purple");
            SetReturning(_returning);
            if (!LoginUp) ShowError(Strings.T("welcome.unavailable"));
            else if (!_walletAvailable) ShowError(Strings.T("welcome.walletAndroid"));
            StartMotion();
            return _root;
        }

        private static bool Pressed(Button b, VisualElement target) =>
            b != null && b.resolvedStyle.display != DisplayStyle.None && b.enabledInHierarchy && (target == b || b.Contains(target));

        private static Button TitleButton(string text, Action onClick, string tone)
        {
            var b = new Button(() =>
            {
                Bus.Publish(new SfxSignal("sfx_ui_tap"));
                onClick();
            }) { text = text, enableRichText = false };
            b.AddToClassList("ct-title-btn");
            b.AddToClassList("ct-title-btn-" + tone);
            // The shine sweeps inside a rounded clip that matches the painted body, so it never lights the transparent
            // corners and margins of the art (the button's own rect is square).
            var shine = new VisualElement { pickingMode = PickingMode.Ignore };
            shine.AddToClassList("ct-title-shine");
            var clip = new VisualElement { pickingMode = PickingMode.Ignore };
            clip.AddToClassList("ct-title-shine-clip");
            clip.Add(shine);
            b.Add(clip);
            return b;
        }

        /// <summary>
        /// Side slice of the squircle bodies (btn-*-blank-sq.png, corner radius ~22% of the height) as a fraction of the
        /// texture height: the corner plus its antialiasing and rim shading, so no curved pixel is ever stretched.
        /// `TitleButtonArtTests` measures the painted corner and checks this covers it.
        /// </summary>
        public const float SquircleSideSlice = 0.30f;

        /// <summary>Resource path of a title button body: the squircle body when present, else the older pill.</summary>
        public static string ButtonArtResource(string tone, out bool squircle)
        {
            var sq = "UI/Title/btn-" + tone + "-blank-sq";
            squircle = Resources.Load<Texture2D>(sq) != null;
            return squircle ? sq : "UI/Title/btn-" + tone + "-blank";
        }

        /// <summary>
        /// The painted blank button body (Resources/UI/Title), 9-sliced so only the middle stretches sideways: the top and
        /// bottom slices together cover the whole height, and the slice scale follows the button's height, so there is no
        /// vertical stretch. Side slices: the squircle's corner (SquircleSideSlice of the height), or half the height for the
        /// older pill. Either way the corners keep their painted shape at any width. USS placeholder until a file exists.
        /// </summary>
        private static void ApplyButtonArt(Button b, string tone)
        {
            var tex = Resources.Load<Texture2D>(ButtonArtResource(tone, out bool squircle));
            if (tex == null) return;
            b.AddToClassList("ct-title-btn-art");
            b.style.backgroundImage = new StyleBackground(tex);
            int half = tex.height / 2 - 1;
            int side = squircle ? Mathf.Min(half, Mathf.CeilToInt(tex.height * SquircleSideSlice)) : half;
            b.style.unitySliceLeft = side;
            b.style.unitySliceRight = side;
            b.style.unitySliceTop = half;
            b.style.unitySliceBottom = half;
            b.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.newRect.height > 0f) b.style.unitySliceScale = e.newRect.height / tex.height;
            });
        }

        /// <summary>
        /// Ken Burns drift on the art (USS transition, 1.00 → 1.04 over 25 s, ease in-out, ping-pong), a gentle bob and a shine
        /// sweep on the visible button. All off when the phone's "Remove animations" is on.
        /// </summary>
        private void StartMotion()
        {
            if (DeviceFeel.ReducedMotion) return;
            _bg.RegisterCallback<TransitionEndEvent>(_ => _bg.ToggleInClassList("ct-title-zoom"));
            _bg.schedule.Execute(() => _bg.AddToClassList("ct-title-zoom")).StartingIn(200);
            float start = Time.realtimeSinceStartup;
            _idle = _root.schedule.Execute(() =>
            {
                var b = _returning ? _playBtn : _walletBtn;
                if (b == null) return;
                float t = Time.realtimeSinceStartup - start;
                b.style.translate = new Translate(0, Mathf.Sin(t * 2f * Mathf.PI / 2.6f) * 5f);
                var shine = b.Q(className: "ct-title-shine");
                if (shine == null) return;
                float p = Mathf.Clamp01((t % 4.5f) / 0.9f); // one 0.9 s sweep every 4.5 s
                p = p * p * (3f - 2f * p);
                shine.style.left = Length.Percent(Mathf.Lerp(-35f, 125f, p));
            }).Every(33);
        }

        protected override void OnShown() => OnboardingAudio.StartTheme();

        public void SetReturning(bool returning)
        {
            _returning = returning && _onPlay != null;
            if (_walletBtn == null) return;
            _walletBtn.Show(!_returning);
            _playBtn.Show(_returning);
            _another.Show(_returning && _onAnotherWallet != null);
            _playBtn.SetEnabled(true);
            _another.SetEnabled(true);
            ShowError("");
        }

        public void SetBusy(bool busy)
        {
            _walletBtn?.SetEnabled(!busy && _walletAvailable && LoginUp);
            if (_walletBtn != null) _walletBtn.text = Strings.T(busy ? "welcome.busy" : "welcome.wallet");
            if (busy) ShowError("");
        }

        /// <summary>While "Use another wallet" signs out: PLAY and the link are disabled.</summary>
        public void SetSwitching(bool switching)
        {
            _playBtn?.SetEnabled(!switching);
            _another?.SetEnabled(!switching);
        }

        public void ShowError(string text)
        {
            if (_error == null) return;
            _error.text = text ?? "";
            _error.Show(!string.IsNullOrEmpty(text));
        }

        public override void Close()
        {
            _idle?.Pause();
            _idle = null;
            base.Close();
        }
    }
}
