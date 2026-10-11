# Editor startup stability — 2026-10-06

[Inference] Scope: two reproducible Editor messages have a configuration fault and a source-supported startup-race explanation. Neither is evidence for the separate report that the Android phone app closes. Phone crash diagnosis remains with the root agent and needs device logs.

## Addressables: repaired configuration, native verification pending

Observed `/tmp/ct-direction-paired-v4.log:521` reports `Active play mode build script is null.` The stack is the installed Addressables 2.10.3 Editor play-mode hook, called from `FrontendReview.Launch`.

`client/Assets/AddressableAssetsData/AddressableAssetSettings.asset` had `m_DataBuilders: []`. Installed `AddressableAssetSettings.GetDataBuilder` returns null for an empty list; the hook logs this exact error for null `ActivePlayModeDataBuilder`. All three builder assets were already present and each serialized script GUID matched its corresponding installed package source `.meta`.

Root authorized the narrow repair. Only the empty list was replaced:

```diff
-  m_DataBuilders: []
+  m_DataBuilders:
+  - {fileID: 11400000, guid: abdb591fa6b6849d3967bb37ff56c6ea, type: 2}
+  - {fileID: 11400000, guid: 57d867939cc654f779155c0e059e9a1b, type: 2}
+  - {fileID: 11400000, guid: 0ab9f30b5ef6d4dd2806d315e7eecafc, type: 2}
```

Order: existing `BuildScriptFastMode`, `BuildScriptPackedPlayMode`, `BuildScriptPackedMode`. Existing player builder index 2 stays unchanged. `Library/AddressablesConfig.dat` was absent; installed `ProjectConfigData.ConfigSaveData` initializes the editor play index to 0, so no index-file mutation was needed. Existing dirty provider/template fields were preserved exactly.

Byte backup and receipt: `/tmp/ct-editor-stability-addressables-20261006-161148/`. Before SHA256 `cc6ff5930878755b846b6810b035220b32aab5f81904a70074e91010a3ee5411`; after `ed78b2824f4367cc405b819490e4f6a8b8c1a4c33efcc5aec66927911666e713`.

## Search: initial full-index gate failed; corrected initialization gate awaits native verification

Observed sequence: review requests Play mode at log line 530; Unity starts deferred Search indexing at line 903; `SearchDatabase.EnumerateAll` throws `ArgumentOutOfRangeException` at line 904. `UserSettings/Search.index` was absent and `Search.settings` contained `{}`. The capture sequence nevertheless completed and the log reached shutdown, so this log alone does not establish an Editor process crash.

[Inference] The exact-version reference source explains this sequence: `EnumerateAll` creates a default database if needed, then indexes element 0 without an empty-list guard. First-use index creation returns early when `EditorApplication.isPlayingOrWillChangePlaymode` is true. The review previously entered Play immediately during startup, ahead of Search's delayed initialization. See Unity's [6000.3.25f1 SearchDatabase source](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3.25f1/Modules/QuickSearch/Editor/Indexing/SearchDatabase.cs#L435) and [SearchInit source](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3.25f1/Modules/QuickSearch/Editor/SearchInit.cs#L102). Searches of Unity's issue tracker did not locate an exact matching public issue; no specific upstream fix version is claimed.

The first implementation waited on public `SearchService.IsIndexReady(null)`. Native V6 **failed** with `TimeoutException` after 120 seconds and captured nothing. Read-only inspection of `/tmp/ct-landscape-stability-v6.log` shows Search initialized its Assets database at line 521, followed by 8,535 worker import starts before the timeout at line 26102. The generated `UserSettings/Search.index` includes both Assets and Packages. Root also observed modal dialogs. [Inference] Requiring the complete background index made the gate unnecessarily strict; those observations do not isolate modal time from indexing time.

The corrected `FrontendReview.cs` now waits while ordinary compilation/importing is active, then evaluates public `SearchService.EnumerateDatabases().Any()` while still in Edit mode. The [official 6.3 API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Search.SearchService.EnumerateDatabases.html) exposes database enumeration; the [exact-version source](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3.25f1/Modules/QuickSearch/Editor/SearchService.cs#L881) delegates to the same `EnumerateAll` path. This permits first-use construction and verifies a database exists, without waiting for every searchable asset to finish indexing. No Search preference or cache file is manually changed.

The gate has a 120-second deadline, removes its callback on success/failure, and reports a visible failure instead of proceeding if readiness is not reached. Automated review with `reviewExit` exits 1 on a gate failure. Existing dirty scenes are rejected before waiting and again before replacement. No scene or global asset save is introduced. The existing unsaved review scene and Game View setup occur only after readiness.

## Validation and remaining work

- Verified all three builder asset GUIDs and installed script GUIDs before patching.
- Isolated real-Editor-assembly C# compilation passed exit 0 with no diagnostics: `/tmp/ct-editor-stability-compile/compile.log`. This did not invoke Unity or touch the shared compile-check cache. An initial harness path was incorrect; correcting the dotnet/csc paths to `Contents/Resources/Scripting` resolved the invocation error.
- Root reports the complete standalone source check passed in `/tmp/ct-stability-landscape-source-check.log`.
- Assets source frozen again after the V6 timeout correction. Isolated real-Editor-assembly compilation of the corrected enumeration gate passed exit 0 with no diagnostics.
- Required native check: database-initialized log precedes Play; original two messages absent; review captures finish; clean exit; no protected-art drift. Native result not yet claimed.
- No package changes, ProjectSetup invocation, cache deletion, global SaveAssets, backend edit, registry promotion, or art edits in this task.

## Dependency follow-up

See [IAP decision](ct-iap-dependency-20261006.md). After the GUI exited, root authorized removing the unused deprecated IAP direct dependency. Only the manifest entry was removed; package resolution/native verification are root-owned. This later action changes the earlier no-package-change scope and is explicitly recorded.
