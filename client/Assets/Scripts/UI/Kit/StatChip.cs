using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI.Kit
{
    /// <summary>
    /// A status chip for the top bar: a badge, a small caption and a big value, optionally a mini bar, on a white body with
    /// the plum outline and a lip. Tones: normal, warn (amber), bad (red-orange), debt (orange). State is never colour
    /// alone: warn/bad add a "!" pip. Tapping it calls <c>onTap</c> (the home screen opens an explainer). Styles: kit.uss.
    /// </summary>
    public sealed class StatChip : Button
    {
        public enum Tone { Normal, Warn, Bad, Debt }

        private readonly Label _caption, _value;
        private readonly VisualElement _bar, _fill, _pip;
        private readonly Action _activate;

        public string Id { get; }

        public StatChip(string id, string iconKind, string badgeTone, string caption, Action<StatChip> onTap, bool withBar = false, string art = null)
        {
            Id = id;
            enableRichText = false;
            text = "";
            AddToClassList("kit-chip");
            _activate = () =>
            {
                Cues.Play(Cues.SfxTap);
                onTap?.Invoke(this);
            };
            clicked += _activate;
            var lip = new VisualElement { pickingMode = PickingMode.Ignore };
            lip.AddToClassList("kit-chip__lip");
            var face = new VisualElement { pickingMode = PickingMode.Ignore };
            face.AddToClassList("kit-chip__face");
            face.Add(new IconBadge(iconKind, badgeTone, KitIcons.Load(art ?? id)));
            var copy = new VisualElement { pickingMode = PickingMode.Ignore };
            copy.AddToClassList("kit-chip__copy");
            _caption = new Label(caption) { enableRichText = false, pickingMode = PickingMode.Ignore };
            _caption.AddToClassList("kit-chip__caption");
            _value = new Label("—") { enableRichText = false, pickingMode = PickingMode.Ignore };
            _value.AddToClassList("kit-chip__value");
            copy.Add(_caption);
            copy.Add(_value);
            if (withBar)
            {
                _bar = new VisualElement { pickingMode = PickingMode.Ignore };
                _bar.AddToClassList("kit-chip__bar");
                _fill = new VisualElement { pickingMode = PickingMode.Ignore };
                _fill.AddToClassList("kit-chip__fill");
                _bar.Add(_fill);
                copy.Add(_bar);
            }
            face.Add(copy);
            _pip = new VisualElement { pickingMode = PickingMode.Ignore };
            _pip.AddToClassList("kit-chip__pip");
            var bang = new Label("!") { enableRichText = false, pickingMode = PickingMode.Ignore };
            bang.AddToClassList("kit-chip__pip-text");
            _pip.Add(bang);
            Add(lip);
            Add(face);
            Add(_pip);
            RegisterCallback<PointerDownEvent>(_ => { if (enabledInHierarchy) DeviceFeel.Tick(); }, TrickleDown.TrickleDown);
        }

        public string Value => _value.text;

        /// <summary>`fill` 0..1 for the mini bar (ignored without one; negative hides it).</summary>
        public void Set(string value, Tone tone = Tone.Normal, float fill = -1f)
        {
            _value.text = value ?? "—";
            EnableInClassList("kit-chip--warn", tone == Tone.Warn);
            EnableInClassList("kit-chip--bad", tone == Tone.Bad);
            EnableInClassList("kit-chip--debt", tone == Tone.Debt);
            _pip.style.display = tone == Tone.Warn || tone == Tone.Bad ? DisplayStyle.Flex : DisplayStyle.None;
            if (_bar == null) return;
            _bar.style.display = fill < 0f ? DisplayStyle.None : DisplayStyle.Flex;
            if (fill >= 0f) _fill.style.width = Length.Percent(Mathf.Clamp01(fill) * 100f);
        }

        public void Press()
        {
            if (enabledInHierarchy) _activate();
        }
    }

    /// <summary>
    /// A small explainer card anchored under the element that opened it: title, a line or two, and an optional action
    /// button. A transparent scrim behind it closes it on any outside tap. One at a time per host. Styles: kit.uss.
    /// </summary>
    public sealed class Popover : VisualElement
    {
        private readonly VisualElement _scrim, _card, _tail;
        private readonly Label _title, _body;
        private readonly LippedButton _action;
        private Action _onAction;

        public Popover()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("kit-pop");
            _scrim = new VisualElement();
            _scrim.AddToClassList("kit-pop__scrim");
            _scrim.RegisterCallback<PointerDownEvent>(_ => Hide());
            _card = new VisualElement { pickingMode = PickingMode.Position };
            _card.AddToClassList("kit-pop__card");
            _tail = new VisualElement { pickingMode = PickingMode.Ignore };
            _tail.AddToClassList("kit-pop__tail");
            _title = new Label { enableRichText = false, pickingMode = PickingMode.Ignore };
            _title.AddToClassList("kit-pop__title");
            _body = new Label { enableRichText = false, pickingMode = PickingMode.Ignore };
            _body.AddToClassList("kit-pop__body");
            _action = new LippedButton("", () => { var a = _onAction; Hide(); a?.Invoke(); }, LippedButton.Tone.Purple, LippedButton.Size.Medium);
            _action.AddToClassList("kit-pop__action");
            _card.Add(_tail);
            _card.Add(_title);
            _card.Add(_body);
            _card.Add(_action);
            Add(_scrim);
            Add(_card);
            style.display = DisplayStyle.None;
        }

        public bool IsOpen => resolvedStyle.display == DisplayStyle.Flex || style.display == DisplayStyle.Flex;
        public string Title => _title.text;

        /// <summary>Opens under <paramref name="anchor"/> (world-space rect of the tapped element), clamped to the host.</summary>
        public void Show(Rect anchorWorld, string title, string body, string actionLabel = null, Action onAction = null)
        {
            _title.text = title ?? "";
            _body.text = body ?? "";
            _onAction = onAction;
            _action.Text = actionLabel ?? "";
            _action.style.display = string.IsNullOrEmpty(actionLabel) || onAction == null ? DisplayStyle.None : DisplayStyle.Flex;
            style.display = DisplayStyle.Flex;
            var local = this.WorldToLocal(anchorWorld);
            float width = 660f;
            float hostW = float.IsNaN(layout.width) || layout.width < 1f ? 2400f : layout.width;
            float left = Mathf.Clamp(local.center.x - width / 2f, 20f, hostW - width - 20f);
            _card.style.left = left;
            _card.style.top = local.yMax + 30f;
            _card.style.width = width;
            _tail.style.left = Mathf.Clamp(local.center.x - left - 22f, 30f, width - 74f);
            Motion.Pop(_card, 1.03f, 0.18f);
        }

        public void Hide()
        {
            style.display = DisplayStyle.None;
            _onAction = null;
        }
    }
}

namespace CTLife.UI.Kit
{
    /// <summary>
    /// A plain checkbox with its text beside it (the 18+ line: user review 2026-10-09 asked for a normal checkbox, not a
    /// toggle card). The whole row is the tap target (≥ 130 units = 48 dp tall). Checked: Solana green box, plum tick.
    /// </summary>
    public sealed class CheckRow : Button
    {
        private readonly UnityEngine.UIElements.VisualElement _box;
        private readonly System.Action _toggle;

        public bool Checked { get; private set; }

        public CheckRow(string text, System.Action onToggle)
        {
            enableRichText = false;
            this.text = "";
            AddToClassList("kit-check");
            _toggle = onToggle;
            clicked += () => _toggle?.Invoke();
            _box = new UnityEngine.UIElements.VisualElement { pickingMode = UnityEngine.UIElements.PickingMode.Ignore };
            _box.AddToClassList("kit-check__box");
            var tick = new UnityEngine.UIElements.VisualElement { pickingMode = UnityEngine.UIElements.PickingMode.Ignore };
            tick.AddToClassList("kit-check__tick");
            _box.Add(tick);
            var label = new UnityEngine.UIElements.Label(text) { enableRichText = false, pickingMode = UnityEngine.UIElements.PickingMode.Ignore };
            label.AddToClassList("kit-check__label");
            Add(_box);
            Add(label);
            RegisterCallback<UnityEngine.UIElements.PointerDownEvent>(_ => { if (enabledInHierarchy) DeviceFeel.Tick(); }, UnityEngine.UIElements.TrickleDown.TrickleDown);
        }

        public void SetChecked(bool on)
        {
            bool changed = on != Checked;
            Checked = on;
            EnableInClassList("kit-check--on", on);
            if (changed && on) Motion.Pop(_box, 1.12f, 0.18f);
        }

        public void Press()
        {
            if (enabledInHierarchy) _toggle?.Invoke();
        }
    }
}
