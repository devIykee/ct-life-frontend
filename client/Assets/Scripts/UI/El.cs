using System;
using CTLife.Core;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Terse builders for UI Toolkit elements with the ct-* USS classes.</summary>
    public static class El
    {
        public static VisualElement Div(string cls = null, params VisualElement[] children)
        {
            var e = new VisualElement();
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) e.AddToClassList(c);
            foreach (var ch in children) if (ch != null) e.Add(ch);
            return e;
        }

        public static Label Text(string text, string cls = null)
        {
            var l = new Label(text);
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) l.AddToClassList(c);
            return l;
        }

        public static Button Btn(string text, Action onClick, string cls = "ct-btn", string sfx = "sfx_ui_tap")
        {
            var b = new Button(() =>
            {
                if (!string.IsNullOrEmpty(sfx)) Bus.Publish(new SfxSignal(sfx));
                onClick?.Invoke();
            }) { text = text };
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) b.AddToClassList(c);
            return b;
        }

        public static T With<T>(this T e, string cls) where T : VisualElement
        {
            foreach (var c in cls.Split(' ')) if (c.Length > 0) e.AddToClassList(c);
            return e;
        }

        public static void Show(this VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
