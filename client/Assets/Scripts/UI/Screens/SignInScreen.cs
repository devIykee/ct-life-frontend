using System;
using CTLife.Core;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Sign In With Solana (contract section 3). Mobile Wallet Adapter is the only real sign-in; the dev button
    /// appears only where the environment allows `POST /v1/auth/dev` (mock, local).
    /// </summary>
    public sealed class SignInScreen : UiScreen
    {
        private readonly Action _onWallet;
        private readonly Action _onDev;
        private readonly bool _walletAvailable;
        private Label _error;
        private Button _walletBtn, _devBtn;

        public SignInScreen(Action onWallet, Action onDev, bool walletAvailable)
        {
            _onWallet = onWallet;
            _onDev = onDev;
            _walletAvailable = walletAvailable;
        }

        protected override VisualElement Build()
        {
            _walletBtn = El.Btn("Sign in with Solana", _onWallet, "ct-btn ct-btn-primary ct-btn-big");
            bool loginUp = Features.IsAvailable(Features.Login);
            _walletBtn.SetEnabled(_walletAvailable && loginUp);
            _devBtn = El.Btn("Dev sign-in (" + Env.Current.id + ")", _onDev, "ct-btn ct-btn-ghost");
            _devBtn.Show(Env.Current.allowDevAuth && _onDev != null);
            _error = El.Text(loginUp ? "" : "Sign-in is temporarily unavailable. Try again in a few minutes.", "ct-error");
            return El.Div("ct-signin",
                El.Text("CT LIFE", "ct-logo"),
                El.Text("Get a job. Trade fake coins. Get rugged. Climb the rich list.", "ct-tagline"),
                El.Div("ct-grow"),
                _walletBtn,
                El.Text(_walletAvailable ? "Uses your Seeker's Seed Vault through Mobile Wallet Adapter." : "Wallet sign-in needs the Android app (Mobile Wallet Adapter).", "ct-small"),
                _devBtn,
                _error,
                El.Text("18+ only. Cash is fictional and can never be withdrawn.", "ct-small ct-muted"));
        }

        public void SetBusy(bool busy)
        {
            _walletBtn?.SetEnabled(!busy && _walletAvailable);
            _devBtn?.SetEnabled(!busy);
            if (busy && _error != null) _error.text = "";
        }

        public void ShowError(string text)
        {
            if (_error != null) _error.text = text;
        }
    }
}
