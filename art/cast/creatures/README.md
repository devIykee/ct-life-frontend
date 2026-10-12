# Authored creature characters

Shark Person and Froggy come from Polygonal Mind's original 100 Avatars files, pinned to commit `ff07c2ad0017819c4e5366656ee1e5bcc4029bd4`. These are separate authored characters, not a human body recoloured or dressed differently.

- [Shark Person source](https://github.com/PolygonalMind/100Avatars/tree/ff07c2ad0017819c4e5366656ee1e5bcc4029bd4/100Avatars_120): 3,928 triangles, five articulated fingers per hand, original hammerhead silhouette and yellow gloves/boots.
- [Froggy source](https://github.com/PolygonalMind/100Avatars/tree/ff07c2ad0017819c4e5366656ee1e5bcc4029bd4/100Avatars_010): 2,890 triangles, three articulated fingers per hand, original frog face and spotted skin.

Both publisher VRM files declare CC0 and commercial use allowed. The [publisher release](https://github.com/PolygonalMind/100Avatars/releases/tag/v24.02.1) also calls the avatars CC0. The older repository `CCLicense.md` contains CC BY 4.0 wording; the full attribution and modifications notice is retained in CT Life's shipped `Resources/UI/Licenses/Model-Credits.txt`. No accounts or third-party rigging service were used.

`scripts/prepare_polygonal_mind.py` reads the pinned FBX, texture and VRM metadata, preserves the source hero geometry/rig/texture, generates three world LODs, and normalizes reduced skinning to four influences. It filters the source VRM bone map against actual FBX bones: Froggy has three fingers, so absent ring/little finger bones are not invented. No shared human skeleton is substituted.

Run with Blender 5.1.2:

```
Blender --background --disable-autoexec --python scripts/prepare_polygonal_mind.py -- SOURCE_DIRECTORY OUTPUT_DIRECTORY
```

Each export folder contains geometry, albedo, the exact Humanoid map and source/export hashes. Original downloads, Blender source renders, fresh-import validation and licence snapshots live outside the repository at `ct-life-evidence/character-roster-20261011/polygonalmind/`. Unity/device acceptance belongs to the integration receipt, not these Blender exports.
