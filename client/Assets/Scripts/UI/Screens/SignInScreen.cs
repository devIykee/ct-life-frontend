using System;
using CTLife.Core;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Sign In With Solana (contract section 3). The public entry has one path: wallet authentication.
    /// </summary>
    public sealed class SignInScreen : UiScreen
    {
        private readonly Action _onWallet;
        private readonly bool _walletAvailable;
        private Label _error;
        private Button _walletBtn;
        private AvatarPreview _preview;

        public SignInScreen(Action onWallet, bool walletAvailable)
        {
            _onWallet = onWallet;
            _walletAvailable = walletAvailable;
        }

        protected override VisualElement Build()
        {
            _walletBtn = El.Btn(Strings.T("welcome.wallet"), _onWallet, "ct-btn ct-btn-primary ct-btn-big");
            bool loginUp = Features.IsAvailable(Features.Login);
            _walletBtn.SetEnabled(_walletAvailable && loginUp);
            _error = El.Text(loginUp ? "" : Strings.T("welcome.unavailable"), "ct-error");
            var copy = El.Div("ct-welcome-copy",
                El.Text(Strings.T("welcome.eyebrow"), "ct-eyebrow"),
                El.Text(Strings.T("welcome.title"), "ct-welcome-title"),
                El.Text(Strings.T("welcome.body"), "ct-welcome-body"));
            var hero = El.Div("ct-welcome-hero", copy);
            var looks = CreatorLookCatalog.Available();
            if (looks.Count > 0)
            {
                _preview = new AvatarPreview(1080, 1440);
                _preview.SetLook(looks[0].look);
                _preview.SetYaw(looks[0].previewYaw);
                _preview.SetPortrait(true);
                var portrait = El.Div("ct-welcome-portrait");
                portrait.style.backgroundImage = Background.FromRenderTexture(_preview.Texture);
                portrait.pickingMode = PickingMode.Ignore;
                hero.Add(portrait);
            }
            var actions = El.Div("ct-welcome-actions", _walletBtn,
                El.Text(Strings.T(_walletAvailable ? "welcome.walletNote" : "welcome.walletAndroid"), "ct-small ct-muted ct-center"),
                _error,
                El.Text(Strings.T("welcome.age"), "ct-legal"));
            var scroll = El.VerticalScroll("ct-welcome-scroll");
            scroll.Add(hero);
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.newRect.height > 0) scroll.contentContainer.style.minHeight = e.newRect.height;
            });
            return El.Div("ct-signin", El.Brand(true), El.Div("ct-welcome-layout", scroll, actions));
        }

        public void SetBusy(bool busy)
        {
            _walletBtn?.SetEnabled(!busy && _walletAvailable && Features.IsAvailable(Features.Login));
            if (_walletBtn != null) _walletBtn.text = Strings.T(busy ? "welcome.busy" : "welcome.wallet");
            if (busy && _error != null) _error.text = "";
        }

        public override void Close()
        {
            base.Close();
            _preview?.Dispose();
            _preview = null;
        }

        public void ShowError(string text)
        {
            if (_error != null) _error.text = text;
        }
    }
}
