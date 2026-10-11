// COMPILE-CHECK STUB ONLY (never imported by Unity). Mirrors com.unity.addressables 2.10.3 editor APIs used by
// CTLife.Editor, copied from Editor/AddressableAssetSettingsDefaultObject.cs, Settings/AddressableAssetSettings.cs
// and Build/AddressablesDataBuilders.cs. Group/entry members (2026-10-08) from Settings/AddressableAssetSettings.cs,
// AddressableAssetGroup.cs, AddressableAssetEntry.cs; ModificationEvent lists only the values used here.
namespace UnityEditor.AddressableAssets.Settings
{
    public class AddressableAssetEntry
    {
        public string address { get; set; }
    }
    public class AddressableAssetGroupSchema : UnityEngine.ScriptableObject { }
    public class AddressableAssetGroup : UnityEngine.ScriptableObject
    {
        public virtual System.Collections.Generic.ICollection<AddressableAssetEntry> entries => null;
        public System.Collections.Generic.List<AddressableAssetGroupSchema> Schemas => null;
    }
    public class AddressableAssetSettings : UnityEngine.ScriptableObject
    {
        public enum PlayerBuildOption { PreferencesValue, BuildWithPlayer, DoNotBuildWithPlayer }
        public PlayerBuildOption BuildAddressablesWithPlayerBuild { get; set; }
        public System.Collections.Generic.List<AddressableAssetGroup> groups => null;
        public enum ModificationEvent { GroupAdded, GroupRemoved, EntryAdded, EntryMoved, EntryRemoved, EntryModified }
        public AddressableAssetGroup DefaultGroup { get; set; }
        public AddressableAssetGroup FindGroup(string groupName) => null;
        public AddressableAssetGroup CreateGroup(string groupName, bool setAsDefaultGroup, bool readOnly, bool postEvent,
            System.Collections.Generic.List<AddressableAssetGroupSchema> schemasToCopy, params System.Type[] types) => null;
        public AddressableAssetEntry CreateOrMoveEntry(string guid, AddressableAssetGroup targetParent, bool readOnly = false, bool postEvent = true) => null;
        public void SetDirty(ModificationEvent modificationEvent, object eventData, bool postEvent, bool settingsModified = false) { }
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
