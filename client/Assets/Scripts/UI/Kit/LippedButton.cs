using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI.Kit
{
    /// <summary>
    /// The game button: the approved painted squircle body (Resources/UI/Title/btn-*-blank-sq.png, 9-sliced exactly like the
    /// title screen) standing on a solid plum lip. Pressing moves the face down by the lip's depth so the lip disappears
    /// (60 ms in, 140 ms ease-out-back out: kit.uss). Disabled: a flat cream face, pressed in, with a muted label.
    /// It is a real Button, so focus, disabled state and click semantics are UI Toolkit's own. Styles: Resources/UI/kit.uss.
    /// </summary>
    public sealed class LippedButton : Button
    {
        public enum Tone { Green, Purple, Cream }
        public enum Size { Large, Medium, Square, Round }

        private readonly VisualElement _face, _shine;
        private readonly Label _label;
        private readonly Tone _tone;
        private readonly Action _activate;
        private IVisualElementScheduledItem _shineTick;
        private string _idleText;
        private float _shineStart;

        public LippedButton(string text, Action onClick, Tone tone = Tone.Green, Size size = Size.Large, string sfx = Cues.SfxTap)
        {
            _tone = tone;
            enableRichText = false;
            this.text = "";
            AddToClassList("kit-btn");
            AddToClassList("kit-btn--" + tone.ToString().ToLowerInvariant());
            AddToClassList("kit-btn--" + size.ToString().ToLowerInvariant());
            _activate = () =>
            {
                Cues.Play(sfx);
                onClick?.Invoke();
            };
            clicked += _activate;

            var lip = new VisualElement { pickingMode = PickingMode.Ignore };
            lip.AddToClassList("kit-btn__lip");
            _face = new VisualElement { pickingMode = PickingMode.Ignore };
            _face.AddToClassList("kit-btn__face");
            _label = new Label(text) { enableRichText = false, pickingMode = PickingMode.Ignore };
            _label.AddToClassList("kit-btn__label");
            _face.Add(_label);
            Add(lip);
            Add(_face);
            _idleText = text;

            if (tone != Tone.Cream)
            {
                var clip = new VisualElement { pickingMode = PickingMode.Ignore };
                clip.AddToClassList("kit-btn__shine-clip");
                _shine = new VisualElement { pickingMode = PickingMode.Ignore };
                _shine.AddToClassList("kit-btn__shine");
                clip.Add(_shine);
                _face.Insert(0, clip);
                SliceArt(_face, tone == Tone.Green ? "green" : "purple");
            }
            if (string.IsNullOrEmpty(text)) _label.style.display = DisplayStyle.None;
            // Haptic on touch-down (before the click), like the title screen's buttons.
            RegisterCallback<PointerDownEvent>(_ => { if (enabledInHierarchy) DeviceFeel.Tick(); }, TrickleDown.TrickleDown);
        }

        public VisualElement Face => _face;

        public string Text
        {
            get => _label.text;
            set
            {
                _idleText = value;
                _label.text = value;
                _label.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        /// <summary>Adds an icon (UiIcon kind) to the face, with a plum drop under it so it reads on the painted body.</summary>
        public LippedButton WithIcon(string kind)
        {
            var holder = new VisualElement { pickingMode = PickingMode.Ignore };
            holder.AddToClassList("kit-btn__icon-holder");
            var drop = new UiIcon(kind);
            drop.AddToClassList("kit-btn__icon");
            drop.AddToClassList("kit-btn__icon--drop");
            var icon = new UiIcon(kind);
            icon.AddToClassList("kit-btn__icon");
            holder.Add(drop);
            holder.Add(icon);
            _face.Add(holder);
            return this;
        }

        /// <summary>Busy: disabled, with a short label ("SAVING…"); false restores the idle label.</summary>
        public void SetBusy(bool busy, string busyText)
        {
            SetEnabled(!busy);
            _label.text = busy ? busyText : _idleText;
        }

        /// <summary>Editor captures: show the pressed state without a pointer.</summary>
        public void ForcePressed(bool on) => EnableInClassList("kit-btn--pressed", on);

        /// <summary>Automation and captures: the same path as a tap (sound, then the click handler), only when enabled.</summary>
        public void Press()
        {
            if (enabledInHierarchy) _activate();
        }

        /// <summary>
        /// A light sweep across the face every 4.5 s (as on the title screen's button), for the one primary CTA on a screen.
        /// Off under reduced motion and while disabled. Ticks allocate nothing.
        /// </summary>
        public void EnableShine()
        {
            if (_shine == null || DeviceFeel.ReducedMotion || _shineTick != null) return;
            _shineStart = Motion.Now;
            _shineTick = schedule.Execute(ShineTick).Every(33);
        }

        private void ShineTick()
        {
            float t = Motion.Now - _shineStart;
            float p = Mathf.Clamp01((t % 4.5f) / 0.9f);
            p = p * p * (3f - 2f * p);
            _shine.style.left = Length.Percent(Mathf.Lerp(-35f, 125f, p));
        }

        /// <summary>
        /// 9-slice the painted squircle like SignInScreen: the corner slices are SquircleSideSlice of the texture height and the
        /// slice scale follows the face height, so no curved pixel stretches. The image itself comes from kit.uss (so the
        /// disabled rule can swap it for a flat face); this only sets the slices.
        /// </summary>
        private static void SliceArt(VisualElement face, string tone)
        {
            var tex = Resources.Load<Texture2D>(SignInScreen.ButtonArtResource(tone, out bool squircle));
            if (tex == null) return;
            int half = tex.height / 2 - 1;
            int side = squircle ? Mathf.Min(half, Mathf.CeilToInt(tex.height * SignInScreen.SquircleSideSlice)) : half;
            face.style.unitySliceLeft = side;
            face.style.unitySliceRight = side;
            face.style.unitySliceTop = half;
            face.style.unitySliceBottom = half;
            face.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.newRect.height > 0f) face.style.unitySliceScale = e.newRect.height / tex.height;
            });
        }
    }
}
