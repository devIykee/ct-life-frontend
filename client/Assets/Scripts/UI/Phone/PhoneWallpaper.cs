using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Original photographic wallpaper. One static texture; no per-frame rendering or allocation.</summary>
    public sealed class PhoneWallpaper : VisualElement
    {
        public PhoneWallpaper()
        {
            AddToClassList("ct-phone-wallpaper");
            pickingMode = PickingMode.Ignore;
            style.backgroundImage = Resources.Load<Texture2D>("UI/PhoneOS/Wallpaper/Dunes");
        }
    }
}
