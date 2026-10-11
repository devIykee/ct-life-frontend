using System;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// The chrome every first-time screen shares (spec B8 / C2 "StepFrame"): the title painting blurred behind everything
    /// (full bleed under the cutout), a round back button and the progress bar across the top, the CTA always bottom-right
    /// with a short hint above it saying why it is disabled, and a particle layer on top. Screens add their own content to
    /// <see cref="Content"/>. Styles: Resources/UI/kit.uss + onboarding.uss.
    /// </summary>
    public sealed class OnboardingFrame
    {
        public readonly VisualElement Root, Content, Overlay;
        public readonly Kit.ProgressBar Progress;
        public readonly Kit.LippedButton Back, Cta;
        public readonly Kit.CelebrationBurst Burst;
        private readonly VisualElement _bg, _topBar, _ctaZone, _blocker;
        private readonly Label _hint;

        public OnboardingFrame(string screenClass, Action onBack, Action onCta, string ctaText)
        {
            _bg = new VisualElement { pickingMode = PickingMode.Ignore };
            _bg.AddToClassList("ob-bg");
            var art = OnboardingArt.Backdrop;
            if (art != null) _bg.style.backgroundImage = new StyleBackground(art);

            Back = new Kit.LippedButton("", () => onBack?.Invoke(), Kit.LippedButton.Tone.Cream, Kit.LippedButton.Size.Round).WithIcon("back");
            Back.name = "ob-back";
            Back.tooltip = Strings.T("common.back");
            Progress = new Kit.ProgressBar(CreatorFlow.ProgressSegments);
            _topBar = El.Div("ob-topbar", Back, Progress);
            _topBar.pickingMode = PickingMode.Ignore;

            Content = new VisualElement { pickingMode = PickingMode.Ignore };
            Content.AddToClassList("ob-content");

            Cta = new Kit.LippedButton(ctaText, () => onCta?.Invoke(), Kit.LippedButton.Tone.Green, Kit.LippedButton.Size.Large);
            Cta.name = "ob-cta";
            _hint = El.Text("", "ob-hint");
            _hint.pickingMode = PickingMode.Ignore;
            // A tap on the disabled CTA explains itself: the hint shakes, with an error blip and a reject haptic. Disabled
            // elements get no pointer events, so an enabled, invisible catcher sits over the CTA while it is disabled.
            _blocker = new VisualElement();
            _blocker.AddToClassList("ob-cta-blocker");
            _blocker.style.display = DisplayStyle.None;
            _blocker.RegisterCallback<PointerDownEvent>(_ => NudgeHint());
            _ctaZone = El.Div("ob-cta-zone", _hint, Cta, _blocker);
            _ctaZone.pickingMode = PickingMode.Ignore;

            Overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            Overlay.AddToClassList("ob-overlay");
            Burst = new Kit.CelebrationBurst();

            Root = El.Div("ob-screen " + screenClass, _bg, _topBar, Content, _ctaZone, Overlay, Burst);
            AddSheet(Root, "UI/kit");
            AddSheet(Root, "UI/onboarding");
            SafeAreaBleed.Attach(Root, _bg);
        }

        public static void AddSheet(VisualElement e, string resource)
        {
            var sheet = Resources.Load<StyleSheet>(resource);
            if (sheet != null) e.styleSheets.Add(sheet);
            else Log.Warn("ui", "Missing stylesheet " + resource);
        }

        public VisualElement TopBar => _topBar;
        public VisualElement CtaZone => _ctaZone;
        public string HintText => _hint.text;

        /// <summary>Why the CTA is disabled (or an empty string). Only shown while the CTA is disabled, as in spec A2.</summary>
        public void SetHint(string text)
        {
            _hint.text = text ?? "";
            _hint.EnableInClassList("ob-hint--on", !string.IsNullOrEmpty(text));
            _blocker.style.display = !Cta.enabledSelf && !string.IsNullOrEmpty(text) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void NudgeHint()
        {
            if (string.IsNullOrEmpty(_hint.text)) return;
            Kit.Cues.Nope();
            Kit.Motion.Shake(_hint, 18f);
        }

        public void ShowCta(bool on, bool animate = true)
        {
            _ctaZone.EnableInClassList("ob-cta-zone--hidden", !on);
            if (on && animate) Kit.Motion.Enter(Cta, 0f, 60f, 0.4f);
        }
    }
}
