using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI.Kit
{
    /// <summary>
    /// A round badge built like the painted tribe medallions: a thick plum rim, a coloured field with a soft top-left bevel
    /// highlight and a darker bottom inset, and a white glyph with a plum drop. Drawn in USS (no texture), so any UiIcon kind
    /// becomes a badge at no APK cost. Tones: green, purple, gold, coral, cyan, orange, plum. Styles: Resources/UI/kit.uss.
    /// </summary>
    public sealed class IconBadge : VisualElement
    {
        public IconBadge(string iconKind, string tone = "purple", Texture2D painted = null)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("kit-badge");
            AddToClassList("kit-badge--" + tone);
            if (painted != null)
            {
                // A painted badge (e.g. a tribe medallion) replaces the drawn one entirely.
                AddToClassList("kit-badge--painted");
                style.backgroundImage = new StyleBackground(painted);
                return;
            }
            var inset = new VisualElement { pickingMode = PickingMode.Ignore };
            inset.AddToClassList("kit-badge__inset");
            var bevel = new VisualElement { pickingMode = PickingMode.Ignore };
            bevel.AddToClassList("kit-badge__bevel");
            var drop = new UiIcon(iconKind);
            drop.AddToClassList("kit-badge__glyph");
            drop.AddToClassList("kit-badge__glyph--drop");
            var glyph = new UiIcon(iconKind);
            glyph.AddToClassList("kit-badge__glyph");
            Add(inset);
            Add(bevel);
            Add(drop);
            Add(glyph);
        }
    }

    /// <summary>
    /// A chunky icon tile for secondary navigation: an <see cref="IconBadge"/> over a Lilita label, the whole tile a Button
    /// (≥ 130 units = 48 dp tall) that sinks on press. Styles: Resources/UI/kit.uss.
    /// </summary>
    public sealed class IconTile : Button
    {
        private readonly Action _activate;

        /// <param name="art">Glossy icon name (<see cref="KitIcons"/>); the drawn badge is the fallback while it is missing.</param>
        public IconTile(string iconKind, string label, string tone, Action onClick, string sfx = Cues.SfxTap, string art = null)
        {
            enableRichText = false;
            text = "";
            AddToClassList("kit-tile");
            tooltip = label;
            _activate = () =>
            {
                Cues.Play(sfx);
                onClick?.Invoke();
            };
            clicked += _activate;
            var face = new VisualElement { pickingMode = PickingMode.Ignore };
            face.AddToClassList("kit-tile__face");
            face.Add(new IconBadge(iconKind, tone, KitIcons.Load(art)));
            var l = new Label(label) { enableRichText = false, pickingMode = PickingMode.Ignore };
            l.AddToClassList("kit-tile__label");
            face.Add(l);
            Add(face);
            RegisterCallback<PointerDownEvent>(_ => { if (enabledInHierarchy) DeviceFeel.Tick(); }, TrickleDown.TrickleDown);
        }

        public void Press()
        {
            if (enabledInHierarchy) _activate();
        }
    }
}
