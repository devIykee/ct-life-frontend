using UnityEngine.UIElements;

namespace CTLife.UI
{
    public sealed class SplashScreen : UiScreen
    {
        private Label _status;

        protected override VisualElement Build()
        {
            _status = El.Text(Strings.T("splash.gm"), "ct-splash-status");
            return El.Div("ct-splash",
                El.Div("ct-splash-emblem", new UiIcon("city")),
                El.Text(Strings.Brand, "ct-logo"),
                El.Text(Strings.T("splash.tagline"), "ct-tagline"),
                El.Div("ct-loading-track", El.Div("ct-loading-accent")),
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
            El.Brand(),
            El.Text(Strings.T("update.title"), "ct-h1"),
            El.Text(Strings.T("update.body"), "ct-body"),
            El.Text(Strings.F("update.version", CTLife.Core.Semver.ClientVersion, _min), "ct-small ct-muted"));
    }
}
