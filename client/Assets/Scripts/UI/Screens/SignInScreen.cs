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
        private Button _walletBtn, _playBtn;
        private Label _another, _error;
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

            // Fixed, trusted copy: rich text only for the underline.
            _another = new Label("<u>" + Strings.T("welcome.anotherWallet") + "</u>") { enableRichText = true, name = "title-another-wallet" };
            _another.AddToClassList("ct-title-link");
            _another.RegisterCallback<ClickEvent>(_ =>
            {
                if (!_another.enabledInHierarchy) return;
                Bus.Publish(new SfxSignal("sfx_ui_tap"));
                _onAnotherWallet?.Invoke();
            });
            _error = El.Text("", "ct-title-error");

            _actions = El.Div("ct-title-actions", _walletBtn, _playBtn, El.Div("ct-title-under", _another, _error));
            // Haptic tick on press (the parent sees the press before the button's own handlers).
            _actions.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target is VisualElement t && (Pressed(_walletBtn, t) || Pressed(_playBtn, t))) DeviceFeel.Tick();
            }, TrickleDown.TrickleDown);

            var legal = El.Text(Strings.T("welcome.age"), "ct-title-legal");
            legal.pickingMode = PickingMode.Ignore;
            _root = El.Div("ct-title", _bg, _actions, legal);
            var sheet = Resources.Load<StyleSheet>("UI/title");
            if (sheet != null) _root.styleSheets.Add(sheet);
            else Log.Warn("ui", "Missing UI/title stylesheet");

            // Full bleed: the screen sits inside the safe area; the art extends under the cutout/edges.
            _root.RegisterCallback<GeometryChangedEvent>(_ => Bleed());
            ApplyButtonArt(_walletBtn, "UI/Title/btn-green-blank");
            ApplyButtonArt(_playBtn, "UI/Title/btn-purple-blank");
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
            var shine = new VisualElement { pickingMode = PickingMode.Ignore };
            shine.AddToClassList("ct-title-shine");
            b.Add(shine);
            return b;
        }

        /// <summary>
        /// The painted blank button body (Resources/UI/Title/btn-*-blank.png, a pill with alpha), 9-sliced so only the middle
        /// stretches sideways: the top and bottom slices together cover the whole height and the side caps are half the height,
        /// and the slice scale follows the button's height, so the rounded ends stay round at any width. USS placeholder until
        /// the file exists.
        /// </summary>
        private static void ApplyButtonArt(Button b, string resource)
        {
            var tex = Resources.Load<Texture2D>(resource);
            if (tex == null) return;
            b.AddToClassList("ct-title-btn-art");
            b.style.backgroundImage = new StyleBackground(tex);
            int half = tex.height / 2 - 1;
            b.style.unitySliceLeft = half;
            b.style.unitySliceRight = half;
            b.style.unitySliceTop = half;
            b.style.unitySliceBottom = half;
            b.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.newRect.height > 0f) b.style.unitySliceScale = e.newRect.height / tex.height;
            });
        }

        private void Bleed()
        {
            var layer = _root?.parent;
            if (layer == null || _bg == null) return;
            var s = layer.resolvedStyle;
            _bg.style.left = -s.left;
            _bg.style.top = -s.top;
            _bg.style.right = -s.right;
            _bg.style.bottom = -s.bottom;
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
