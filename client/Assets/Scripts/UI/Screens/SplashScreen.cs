using UnityEngine.UIElements;

namespace CTLife.UI
{
    public sealed class SplashScreen : UiScreen
    {
        private Label _status;

        protected override VisualElement Build()
        {
            _status = El.Text("gm", "ct-splash-status");
            return El.Div("ct-splash",
                El.Text("CT LIFE", "ct-logo"),
                El.Text("a Crypto Twitter life-sim", "ct-tagline"),
                _status);
        }

        public void SetStatus(string text)
        {
            if (_status != null) _status.text = text;
        }
    }

    public sealed class UpdateRequiredScreen : UiScreen
    {
        private readonly string _min;
        public UpdateRequiredScreen(string minVersion) { _min = minVersion; }

        protected override VisualElement Build() => El.Div("ct-splash",
            El.Text("Update required", "ct-h1"),
            El.Text($"This version ({CTLife.Core.Semver.ClientVersion}) is too old. Minimum: {_min}. Get the latest CT Life from the Solana dApp Store.", "ct-body"));
    }
}
