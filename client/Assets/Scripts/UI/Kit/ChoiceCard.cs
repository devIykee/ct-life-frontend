using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI.Kit
{
    /// <summary>
    /// A big tappable choice: cream face with the plum outline, standing on a lip, with an icon (a painted badge texture or
    /// an icon disc), a Lilita title and an optional one-line perk/effect. The whole card is the tap target (a real Button).
    /// States: default, selected (green tint, green lip, check badge, 1.04 pop), disabled, error (red outline + shake).
    /// State is always shown by more than colour: the check badge, the lip and the pop. Styles: Resources/UI/kit.uss.
    /// </summary>
    public sealed class ChoiceCard : Button
    {
        public enum Layout { Vertical, Horizontal }
        public enum State { Default, Selected, Disabled, Error }

        private readonly VisualElement _face, _check;
        private readonly Label _title, _sub, _caption;
        private readonly Action _activate;
        private State _state;

        public string Id { get; }

        /// <summary>Pass as `iconKind` for a toggle card with a checkbox square (the 18+ confirmation).</summary>
        public const string Checkbox = "checkbox";

        /// <param name="badge">A painted badge (e.g. Resources/UI/Onboarding/tribe-*.png); when null, `iconKind` draws an icon disc.</param>
        public ChoiceCard(string id, string title, string sub, Action<ChoiceCard> onPick, Layout layout = Layout.Vertical,
            Texture2D badge = null, string iconKind = null, string discTone = "purple", string caption = null)
        {
            Id = id;
            enableRichText = false;
            text = "";
            AddToClassList("kit-card");
            AddToClassList(layout == Layout.Vertical ? "kit-card--v" : "kit-card--h");
            _activate = () => onPick?.Invoke(this);
            clicked += _activate;

            var lip = new VisualElement { pickingMode = PickingMode.Ignore };
            lip.AddToClassList("kit-card__lip");
            _face = new VisualElement { pickingMode = PickingMode.Ignore };
            _face.AddToClassList("kit-card__face");
            Add(lip);
            Add(_face);

            if (badge != null)
            {
                var art = new VisualElement { pickingMode = PickingMode.Ignore };
                art.AddToClassList("kit-card__badge");
                art.style.backgroundImage = new StyleBackground(badge);
                _face.Add(art);
            }
            else if (iconKind == Checkbox)
            {
                // A real checkbox square with the label attached (device QA 2026-10-08: the old 18+ box sat far from its label).
                var box = new VisualElement { pickingMode = PickingMode.Ignore };
                box.AddToClassList("kit-card__box");
                var boxTick = new VisualElement { pickingMode = PickingMode.Ignore };
                boxTick.AddToClassList("kit-card__box-tick");
                box.Add(boxTick);
                _face.Add(box);
                AddToClassList("kit-card--checkbox");
            }
            else if (!string.IsNullOrEmpty(iconKind))
            {
                var disc = new VisualElement { pickingMode = PickingMode.Ignore };
                disc.AddToClassList("kit-card__disc");
                disc.AddToClassList("kit-card__disc--" + discTone);
                var icon = new UiIcon(iconKind);
                icon.AddToClassList("kit-card__icon");
                disc.Add(icon);
                _face.Add(disc);
            }

            var copy = new VisualElement { pickingMode = PickingMode.Ignore };
            copy.AddToClassList("kit-card__copy");
            if (!string.IsNullOrEmpty(caption))
            {
                _caption = new Label(caption) { enableRichText = false, pickingMode = PickingMode.Ignore };
                _caption.AddToClassList("kit-card__caption");
                copy.Add(_caption);
            }
            _title = new Label(title) { enableRichText = false, pickingMode = PickingMode.Ignore };
            _title.AddToClassList("kit-card__title");
            copy.Add(_title);
            if (!string.IsNullOrEmpty(sub))
            {
                _sub = new Label(sub) { enableRichText = false, pickingMode = PickingMode.Ignore };
                _sub.AddToClassList("kit-card__sub");
                copy.Add(_sub);
            }
            _face.Add(copy);

            _check = new VisualElement { pickingMode = PickingMode.Ignore };
            _check.AddToClassList("kit-card__check");
            var tick = new VisualElement { pickingMode = PickingMode.Ignore };
            tick.AddToClassList("kit-card__tick");
            _check.Add(tick);
            Add(_check);

            RegisterCallback<PointerDownEvent>(_ => { if (enabledInHierarchy) DeviceFeel.Tick(); }, TrickleDown.TrickleDown);
        }

        public VisualElement Face => _face;

        public string Title
        {
            get => _title.text;
            set => _title.text = value;
        }

        public State Current => _state;
        public bool Selected => _state == State.Selected;

        /// <summary>Sets the visual state. `pop` plays the 1.04 select pop (180 ms, spec B2) when becoming selected.</summary>
        public void SetState(State state, bool pop = false)
        {
            bool wasSelected = _state == State.Selected;
            _state = state;
            EnableInClassList("kit-card--selected", state == State.Selected);
            EnableInClassList("kit-card--error", state == State.Error);
            SetEnabled(state != State.Disabled);
            if (pop && state == State.Selected && !wasSelected) Motion.Pop(_face, 1.04f, 0.18f);
            if (state == State.Error) Motion.Shake(this);
        }

        public void SetSelected(bool selected, bool pop = false) =>
            SetState(selected ? State.Selected : (_state == State.Disabled ? State.Disabled : State.Default), pop);

        /// <summary>The "pushed out" feedback (third trait replaces the oldest): un-pop with a short shake.</summary>
        public void Bump() => Motion.Shake(this, 16f, 0.24f);

        /// <summary>Automation and captures: the same path as a tap.</summary>
        public void Press()
        {
            if (enabledInHierarchy) _activate();
        }

        /// <summary>Editor captures: show the pressed state without a pointer.</summary>
        public void ForcePressed(bool on) => EnableInClassList("kit-card--pressed", on);
    }

    /// <summary>Lays cards out in a fixed-column grid with percentage widths, so no screen scrolls (spec B8).</summary>
    public static class ChoiceGrid
    {
        /// <param name="gapPercent">Horizontal gap as a percentage of the grid width.</param>
        public static VisualElement Build(IReadOnlyList<VisualElement> items, int columns, float rowHeight, float gapPercent = 1.4f, float rowGap = 22f)
        {
            var grid = new VisualElement { pickingMode = PickingMode.Ignore };
            grid.AddToClassList("kit-grid");
            float width = (100f - gapPercent * (columns - 1)) / columns;
            for (int i = 0; i < items.Count; i++)
            {
                var e = items[i];
                e.style.width = Length.Percent(width);
                e.style.height = rowHeight;
                e.style.marginRight = (i % columns) == columns - 1 ? new Length(0f) : Length.Percent(gapPercent);
                e.style.marginBottom = rowGap;
                grid.Add(e);
            }
            return grid;
        }
    }
}
