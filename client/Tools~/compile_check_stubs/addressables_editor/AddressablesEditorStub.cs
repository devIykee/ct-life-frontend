// COMPILE-CHECK STUB ONLY (never imported by Unity). Mirrors com.unity.addressables 2.10.3 editor APIs used by
// CTLife.Editor, copied from Editor/AddressableAssetSettingsDefaultObject.cs, Settings/AddressableAssetSettings.cs
// and Build/AddressablesDataBuilders.cs.
namespace UnityEditor.AddressableAssets.Settings
{
    public class AddressableAssetSettings : UnityEngine.ScriptableObject
    {
        public static void BuildPlayerContent() { }
        public static void BuildPlayerContent(out UnityEditor.AddressableAssets.Build.AddressablesPlayerBuildResult result) { result = null; }
    }
}
namespace UnityEditor.AddressableAssets.Build
{
    public class AddressableAssetBuildResult
    {
        public string Error { get; set; }
        public int LocationCount { get; set; }
    }
    public class AddressablesPlayerBuildResult : AddressableAssetBuildResult { }
}
namespace UnityEditor.AddressableAssets
{
    public class AddressableAssetSettingsDefaultObject
    {
        public static Settings.AddressableAssetSettings GetSettings(bool create) => null;
        public static bool SettingsExists => false;
        public static Settings.AddressableAssetSettings Settings { get; set; }
    }
}
