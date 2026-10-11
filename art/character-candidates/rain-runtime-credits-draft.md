# Rain runtime credit handoff — docs-only draft

Historical handoff prepared 2026-10-06 while Unity build L was active. Root subsequently installed the Rain notice, localized keys, shared Credits entry and bundled-notice update; verified read-only. Device presentation is still pending. This agent made no runtime credit edits. Keep Snow's existing credit. Add Rain as its own entry so each source remains directly identifiable.

## New `client/Assets/Resources/UI/Licenses/Rain.txt`

```text
Rain Rig (CC) Blender Foundation | studio.blender.org

Source: https://studio.blender.org/characters/rain/v3/
License: Creative Commons Attribution 4.0 International
https://creativecommons.org/licenses/by/4.0/

Modified for CT Life: adult head and leg proportions; ponytail and scarf-clearance adjustments; a 55-bone Humanoid skeleton with thirty digit joints and remapped weights; merged skinned renderer; source-texture atlas and PBR texture bakes; alternate world geometry tiers; runtime materials and animation retargeting. The original source is Rain v3. Modifications were made in October 2026.

This attribution does not imply that Blender Foundation or Blender Studio endorses CT Life. The licensed character is supplied without warranties; the full CC-BY 4.0 notice is available alongside this credit.
```

Add the normal TextAsset `.meta` through the root-owned Unity import. Reuse existing `UI/Licenses/CC-BY-4.0`; no duplicate legalcode or runtime network fetch is needed.

## `client/Assets/Scripts/UI/Text/Strings.cs`

Add alongside the existing Snow keys:

```csharp
["credits.rain"] = "Character art · Rain",
["credits.rainSummary"] = "Rain Rig (CC) Blender Foundation | studio.blender.org. Adapted proportions, rig and texture atlas. CC-BY 4.0.",
```

## `client/Assets/Scripts/UI/AssetCredits.cs`

Actual credits implementation is this shared component, called by Settings, rather than a special Phone-only implementation. Immediately after the Snow entry, add:

```csharp
entries.Add(Credit("credits.rain", "credits.rainSummary", "https://studio.blender.org/characters/rain/v3/",
    "UI/Licenses/Rain", "UI/Licenses/CC-BY-4.0"));
```

## `client/Assets/Resources/UI/THIRD-PARTY-NOTICES.md`

Replace only the Character credit paragraph with:

```text
Licenses/Snow.txt and Licenses/Rain.txt contain source attribution and CT Life modification notices for the two authored characters. Full CC-BY 4.0 text is bundled as Licenses/CC-BY-4.0.txt, sourced from the official Creative Commons legalcode repository. Source files and measured conversion evidence remain under art/character-candidates/unity-snow/ and art/character-candidates/rain-adult-production/. These notices do not claim final character-art or device-performance acceptance.
```

After installation, revise the pending runtime-credit sentence in `art/LICENSES.md`. Verify Phone → Settings → Credits & licenses opens Rain's source/modification notice and the full licence without a connection, with no missing-notice fallback and no text clipping; verify the source button resolves to Rain's own page. Build L does not include this pending addition unless root explicitly rebuilds it.
