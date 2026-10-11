using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    public sealed class SplashScreen : UiScreen
    {
        private Label _status;
        private static SplashScreen _boot;

        /// <summary>Shown by GameBootstrap on the first frame, so launch never shows an empty scene; the flow adopts it.</summary>
        public static void ShowBoot() { if (_boot == null) { _boot = new SplashScreen(); _boot.Show(); } }
        public static SplashScreen TakeBoot() { var s = _boot; _boot = null; return s; }

        protected override VisualElement Build()
        {
            _status = El.Text(Strings.T("splash.gm"), "ct-splash-status");
            var accent = El.Div("ct-loading-accent");
            var root = El.Div("ct-splash ct-brand-screen",
                El.LogoMark("ct-splash-mark"),
                El.Text(Strings.Brand, "ct-logo"),
                El.Text(Strings.T("splash.tagline"), "ct-tagline"),
                El.Div("ct-loading-track", accent),
                _status);
            // Indeterminate progress: the accent sweeps across the track.
            float t = 0f;
            root.schedule.Execute(() =>
            {
                t = (t + 0.035f) % 1.35f;
                accent.style.left = Length.Percent(Mathf.Lerp(-40f, 100f, Mathf.Clamp01(t)));
            }).Every(33);
            return root;
        }

        public void SetStatus(string text)
        {
            if (_status != null) _status.text = text;
        }
    }

    /// <summary>
    /// Shown when start-up cannot reach the backend (config, session resume, profile). Keeps the player on a clear
    /// "try again" path instead of an empty screen (QA code-architecture §3 item 2).
    /// </summary>
    public sealed class ConnectionRetryScreen : UiScreen
    {
        private readonly string _detail;
        private readonly System.Action _retry;
        private Button _button;

        public ConnectionRetryScreen(string detail, System.Action retry)
        {
            _detail = detail;
            _retry = retry;
        }

        protected override VisualElement Build()
        {
            _button = El.Btn(Strings.T("common.retry"), () =>
            {
                _button.SetEnabled(false);
                _retry?.Invoke();
            }, "ct-btn ct-btn-primary ct-btn-big");
            _button.name = "connection-retry";
            return El.Div("ct-splash",
                El.Brand(),
                El.Text(Strings.T("connection.title"), "ct-h1"),
                El.Text(Strings.T("connection.body"), "ct-body"),
                El.Text(_detail ?? "", "ct-small ct-muted"),
                _button);
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
