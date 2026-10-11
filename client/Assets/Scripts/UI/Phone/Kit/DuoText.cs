using System;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Duo text sizes, smallest to largest. Large is the default (spec 2026-10-16).</summary>
    public enum DuoTextSize { XS = 0, S = 1, M = 2, L = 3, XL = 4, XXL = 5 }

    /// <summary>
    /// The player's Duo text size. Persisted per device and applied as one root class on <c>.ct-phone</c>; the USS
    /// custom properties in duo-kit.uss (<c>--duo-body</c>, <c>--duo-headline</c> …) carry the sizes per class.
    /// </summary>
    public static class DuoText
    {
        public const string PrefKey = "ctlife.duo.textSize";
        public const DuoTextSize Default = DuoTextSize.L;
        public static readonly float[] Scales = { 0.82f, 0.9f, 0.95f, 1f, 1.12f, 1.25f };
        public const int Count = 6;

        public static event Action<DuoTextSize> Changed;

        public static DuoTextSize Current
        {
            get
            {
                int v = DuoPrefs.Store.GetInt(PrefKey, (int)Default);
                return v < 0 || v >= Count ? Default : (DuoTextSize)v;
            }
        }

        public static void Set(DuoTextSize size)
        {
            int v = (int)size;
            if (v < 0 || v >= Count) size = Default;
            if (size == Current) return;
            DuoPrefs.Store.SetInt(PrefKey, (int)size);
            DuoPrefs.Store.Save();
            Changed?.Invoke(size);
        }

        public static float Scale(DuoTextSize size) => Scales[Math.Max(0, Math.Min(Count - 1, (int)size))];

        /// <summary>A Large-size value in Duo panel units at another size (rounded like the USS table).</summary>
        public static int Px(float large, DuoTextSize size) => (int)Math.Round(large * Scale(size), MidpointRounding.AwayFromZero);

        public static string ClassName(DuoTextSize size) => "duo-size-" + size.ToString().ToLowerInvariant();

        /// <summary>Puts exactly one size class on <paramref name="root"/>.</summary>
        public static void Apply(VisualElement root, DuoTextSize size)
        {
            if (root == null) return;
            for (int i = 0; i < Count; i++) root.EnableInClassList(ClassName((DuoTextSize)i), i == (int)size);
        }
    }
}
