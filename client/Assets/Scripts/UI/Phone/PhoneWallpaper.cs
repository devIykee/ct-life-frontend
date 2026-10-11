using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Original abstract gradient wallpaper (art/source/duo-wallpaper). One static texture; no per-frame rendering.</summary>
    public sealed class PhoneWallpaper : VisualElement
    {
        /// <summary>Resources path shared with <see cref="PhoneGlass"/>, which samples the same texture.</summary>
        public const string ResourcePath = "UI/PhoneOS/Wallpaper/Aurora";

        public PhoneWallpaper()
        {
            AddToClassList("ct-phone-wallpaper");
            pickingMode = PickingMode.Ignore;
            style.backgroundImage = Resources.Load<Texture2D>(ResourcePath);
        }
    }
}
