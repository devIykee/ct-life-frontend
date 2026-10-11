# World quality — Timeline Square

## 2026-10-05 — asset assembly implementation

Scope: frontend venue art loading, Timeline Square assembly and venue camera. No backend, contract,
avatar, audio or UI changes. Existing work retained. Unity execution belongs to the root agent.

Implemented:

- `World/Venue/VenueContentRegistry.cs`: explicit venue-id → prefab mapping. The loader tries
  shipped authored content first, then built-in/Addressables scenes, then the existing greybox.
  An authored prefab lives in its own additive scene and registers its saved NavMesh on entry.
- `Editor/VenueContentBuilder.cs`: repeatable Timeline Square generator using the imported
  `Assets/Art/environment/manifest.json` and existing CC0 FBXs. Textured paving, a fountain,
  shopfronts/balconies, distant marina towers, pink trees, planters, flowers, lamps, benches, Pixel Wall,
  CT billboards, kiosks, café terrace and street assets come from the supplied kits.
- Screen art is copied unchanged from `art/lookdev/tex/`; provenance and original-work record are
  in `art/lookdev/LICENSES.md`. Environment asset provenance is in `art/LICENSES.md`.
- Shared URP Simple Lit materials resolve the imported texture names. Foliage uses cutout alpha;
  screen art uses Unlit. Repeated paving/street modules merge by material in the editor. Source FBX
  Read/Write stays disabled; no model import settings are changed.
- Original LOD0 is retained. Selective screen-size thresholds defer supplied LOD1/LOD2 until window,
  branch and display details become small. No fade overlap, real-time shadows, reflection probes,
  per-prop lights, SSAO or depth of field.
- Static contact-shadow cards merge into one mesh. These are contact shading, **not a lightmap bake**.
  Warm directional + hemisphere lighting and distance haze apply per venue and restore on unload.
- Contract spot coordinates are copied verbatim from the bundled snapshot, including all six bench
  anchors. Ground collider/NavMesh rectangles match the snapshot. Fountain/planter modifier volumes
  keep paths around solid props; center (0,0), entrance and north crier remain open.
- Per-venue high 3/4 camera framing keeps the initial portrait view aimed into the square while
  retaining pan, zoom and avatar follow. Framing must be judged from actual device capture.

Generated outputs (only created by the Unity command):

- `Assets/Resources/World/VenueContentRegistry.asset`
- `Assets/Art/Generated/Venues/TimelineSquare.prefab`
- `Assets/Art/Generated/Venues/Materials/*.mat`
- `Assets/Art/Generated/Venues/Meshes/TimelinePaving_*.asset`, `TimelineContacts.asset`,
  `TimelineNavigation.asset`
- `Assets/Art/Generated/Venues/Textures/*.png`

Run from the repository root (root agent only):

```sh
"/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit -projectPath client \
  -executeMethod CTLife.EditorTools.VenueContentBuilder.BuildTimelineSquare \
  -logFile /tmp/ct-life-venue-content.log
```

The command validates unchanged spot coordinates, non-null URP materials and saved navigation, then
logs worst authored environment triangle count before camera culling. It does not alter Build Settings
or existing scenes. Re-running updates generated assets in place, keeping their GUIDs.

Later checkpoints below record Unity generation, navigation validation, device visual review and the
separate environmental bake. The final 50-player + 12-server-NPC performance gate remains outstanding;
the local procedural-crowd measurement is linked at the end with its limitations.

Transition review: missing-root Addressables/built-in scenes are unloaded before fallback; location
lookup handles are released in `finally`, failed scene-load handles are released rather than unloaded.
The previous active scene is restored on exit. Venue lighting restores before active-scene changes,
with an idempotent destruction fallback that cannot overwrite another scene's render settings. The
main camera persists independently of additive venue scenes and clears its old avatar target on exit.

API verification:

- Local pinned URP 17.3.0 `Shaders/SimpleLit.shader`: material property and keyword names.
- Local AI Navigation 2.0.14 `Runtime/NavMeshSurface.cs`: NavMeshBuilder/ModifierBox use.
- Unity 6.3 primary documentation: [Mesh read access in the editor](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh-isReadable.html),
  [CombineMeshes](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh.CombineMeshes.html),
  [LODGroup.SetLODs](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/LODGroup.SetLODs.html),
  [BuildNavMeshData](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AI.NavMeshBuilder.BuildNavMeshData.html).

Next venue route: use the same manifest/material/LOD helpers to author the venue's layout and contract
anchors, save a `VenueSceneRoot` prefab, and append that venue to the catalog. Do not map an unfinished
venue to Timeline Square or disguise a generic block layout as sourced environment content.

## Device review correction and optional light bake

The first device capture failed composition: player cropped at bottom-left, storefronts above the
portrait frustum, and coplanar fountain water. The world-Z camera offset and aim-point clamp caused
the sideways player displacement at the entrance. Camera look-ahead now solves the perspective
projection along camera-forward, placing feet at viewport (0.500, 0.270), including while zooming.
Authored pose is pitch 40°, yaw −8°, distance 52 m, vertical FOV 48°; computed look-ahead is 12.524 m.
The generator logs entrance/head/roof projections. Source fountain stone and water tops were both
0.50 m; only the water vertices are raised by 0.04 m in generated mesh copies. Source FBX is unchanged.
The second device capture (`screens/quality-review-20261005b/quality-review-20261005b/city.png`) and
root's runtime measurement confirm player feet (0.50, 0.27), corrected water and a successful venue
navigation round trip.

### Second device review: source detail and street continuity

The second capture failed asset quality. The initial generator discarded LOD0 and selected decimated
LOD1/2 too early. Android uses quality level 2 with `lodBias: 0.7`, so a 0.12 transition selected the
20% mesh even for many storefronts and trees. The source pipeline `env_process.py::make_lods` applies
generic 50% and 20% collapse decimation across textured geometry. Binary FBX inspection confirmed
identity model transforms and original Y heights: blossom trees 5.8/6.1/6.7 m, column shop 7.476 m.
This was a detail-selection problem, not permission to reduce the art quality to meet an arbitrary count.

Correction: all sourced LOD0 meshes remain available, including the low-poly skyline. Architecture
LOD0→1 transition is 0.035; tree transition is 0.025; CT display props use 0.012. At the Android bias,
architecture/tree transitions correspond to approximately 134/95 pixels on the 2670-pixel display.
LOD2 is deferred further (architecture 0.012, trees 0.009); culling is 0.002. Generator validation now
reads the actual Android quality selection, verifies source mesh heights and LOD0 presence, and logs
per-asset triangles / estimated projected pixel range. It fails if a hero asset starts below LOD0 at
the entrance. These estimates are not a substitute for device render/performance capture.

The sidewalk ends at z=-19; the former road began at z=-20. The supplied road now centers at z=-22
so both edges join. Rear and side floors extend using the existing 4 m concrete kit, with a rear
avenue, sidewalk and seven blossom planters. All added surfaces are decorative: contract collider,
NavMesh bounds and spot coordinates stay unchanged. Run `BuildTimelineSquare` again before the bake.
The following root-run generation and device review supplied the actual results.

Root regeneration and build-c device review are now complete: 966 sourced placements before combining,
30 materials, 124,610 worst authored triangles; all 15 anchors and hero-detail checks passed. The saved
prefab has 218 MeshRenderers including 177 LOD members in 70 groups (about 111 active at one LOD per
group before frustum culling, not 966 renderers). Capture
`screens/quality-review-20261005c/quality-review-20261005c/city.png` shows restored tree trunks,
blossoms, doorframes/windows and continuous front/rear ground. Root reports successful walking and
venue round trip. Environmental lighting remains flat; the approved next step is the actual bake.

Optional lighting command, run separately **after** content generation:

```sh
/usr/bin/time -l "/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit -projectPath client \
  -executeMethod CTLife.EditorTools.VenueLightingBuilder.BakeTimelineSquare \
  -logFile /tmp/ct-life-venue-lighting.log
```

Do not add `-nographics`. This arm64 Mac uses ProgressiveGPU. Unity 6.3 explicitly does not support CPU
lightmapping on Apple silicon ([platform requirements](https://docs.unity3d.com/6000.3/Documentation/Manual/system-requirements.html#limitations)).
CPU is supported by this command on other supported hosts; `--ct-lightmapper=cpu` is rejected here.
Root coordinates Unity as the only rendering/baking job.

The method uses synchronous `Lightmapping.Bake()`; the installed Unity 6.3 XML API documentation and
[public API reference](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Lightmapping.Bake.html)
identify it as synchronous. `-quit` cannot run between starting the bake and binding its result.
It saves bindings only after successful completion and validates every prepared static renderer.

Settings: 10 texels/m, non-directional 1024 atlases, 32 direct / 128 indirect / 64 environment samples,
two bounces, local baked AO, Gaussian filtering, one baked golden-hour sun. Distant skyline uses a lower
lightmap scale. All static lightmapped meshes receive unique UV2 on generated copies; source FBX and
base generated meshes stay unchanged. Display submeshes split into unlit renderers without casts/GI;
their LOD membership follows the frame. Contact cards are disabled once replaced by baked shadows.

Outputs: `Assets/Art/Generated/Venues/Lighting/TimelineSquare.unity`, generated lighting data/lightmaps,
`Meshes/`, `Materials/`, `TimelineLightingSettings.lighting`, and `LastBake.txt`. The existing venue prefab
is updated in place, preserving its GUID. `VenueLightmaps` registers texture refs and renderer indices /
scale-offsets on entry and restores the prior table on normal unload. Static materials use URP Baked Lit
to avoid adding runtime sun lighting twice; screen artwork stays Unlit. The runtime sun continues to
light avatars with no realtime shadows.

Build integration: because this is prefab-bound lighting (the bake scene is not in Build Settings), the
root agent set Lightmap Modes to Custom, retaining Baked Non-Directional and the existing other flags.
Unity's [Graphics Settings reference](https://docs.unity3d.com/6000.3/Documentation/Manual/class-GraphicsSettings.html)
explains the build-scene stripping behavior. Primary `UnityCsReference` GraphicsSettingsInspector
defines `Automatic = 0`, `Custom = 1`. The bake and baked-venue builds inspect those serialized settings
read-only and fail if plain lightmaps are not retained. No settings are changed by the bake method.

Pinned URP 17.3 `BakedLit.shader` declares `LIGHTMAP_ON`; its `ShaderScriptableStripper` does not reject
valid non-directional lightmapped variants. `VenueLightmapShaderGuard` runs after the pinned SRP
preprocessor (callback 0), logs retained `BakedLit` forward-pass variants, and fails a fresh compilation
if the plain or alpha-cutout lightmapped fragment variants are absent. Cached compilation may not invoke
the callback, so actual device rendering remains the final check.

LOD semantics: keep LODGroups intact during ProgressiveGPU baking so lower LODs do not become
overlapping independent occluders. Unity's Progressive Lightmapper supports lower-LOD indirect light
without probes ([lightmaps and LOD](https://docs.unity3d.com/2022.3/Documentation/Manual/LODForBakedGI.html));
the current [LOD Group reference](https://docs.unity3d.com/6000.3/Documentation/Manual/class-LODGroup.html)
also exposes lightmap scale for every renderer in a group. Every prepared renderer, including lower
LODs, must have a valid index and positive atlas scale before prefab saving. The command logs counts
per LOD and fails visibly if any mesh is unbound. The actual Unity 6.3 bake passed these checks.
VenueLoader explicitly restores the previous lightmap table before switching active scenes on unload.

### Actual ProgressiveGPU bake result

Root executed the method with graphics enabled on the Apple silicon Mac. Unity exited 0. Generated
`Lighting/LastBake.txt` records 53.6 seconds inside the method, 5 lightmaps, 3,473,408 color texels and
217 bound static renderers. Lower LOD validation passed: LOD0 70, LOD1 70, LOD2 37. All static maps
and positive scale-offsets were validated before saving the existing prefab; its GUID was retained.
The remaining 40 bound renderers are outside LOD groups. Displays remain unlit and contact cards are
disabled after the real bake.

The full Unity process took 70.4 seconds including startup/import/compile. Root's process monitor saw
about 1.9 GiB RSS; `LastBake.txt` recorded peak editor working set 2,103,885,824 bytes (about 2.0 GiB).
These are editor/process measures, not device memory or a measurement of separate GPU allocations.

Root also changed Fog Modes from Automatic to Custom, retaining the existing Linear flag, because
the venue's haze is enabled at runtime and is absent from bootstrap scenes. No broad always-included
shader setting was added. Plain baked-lightmap and linear-fog retention are the only targeted settings
changes made by root for this venue.

The subsequent build-d device checkpoint validates the shader variants, rendered lightmaps and table
restoration. Performance evidence remains scoped to the specific local-crowd and presentation runs below.

### Build-d device validation and haze correction

Root's build-d shader guard passed; build completed with 0 errors, 29 warnings and a 129.3 MiB APK.
The device validated all 217 bindings against five maps, cleared the venue table in Terminal Café and
restored the same table on return. Capture `screens/quality-review-20261005d/quality-review-20261005d/city.png`
shows baked shadows, but the now-retained fog variant washed out storefronts and blossom colors.

Runtime fog alone now starts at 70 m and ends at 165 m. Camera and source-bound calculations put front
shop bounds at view Z 57.3–74.9 m, garden crowns 42.3–63.5 m and skyline bounds 61.7–102.0 m. Under
the former 48–110 m linear fog, shop bounds received approximately 15–43% haze; the new range is 0–5%.
Garden crowns receive no haze at the entry camera, while the far skyline base reaches about 34%.
These estimates use the pinned URP linear-fog view-Z formula (near-plane adjustment below 0.3 m).
Sun, materials, haze color, camera and baked textures are unchanged. No generator rerun or rebake is
needed. Build e below confirms the resulting appearance on the device.

Runtime static batching remains unimplemented: the measured draw counts below do not justify its
added mesh-buffer memory and loading cost. No shared source import settings were changed for batching.

### Final build-e visual evidence and measured performance scope

Build e completed with **0 errors, 29 warnings**, a **129.3 MiB APK**, 67 seconds of build time and
82 seconds for the full Unity process. The inspected [final device city capture](screens/quality-review-20261005e/quality-review-20261005e/city.png)
shows restored foreground/shop/blossom colors, intact source geometry and baked contact/cast shadows.
The saved capture log contained no exceptions. The haze adjustment required no content regeneration
or lightmap rebake.

The [build-d Seeker crowd report](seeker-crowd-check-20261005.md) records the full local test and raw
artifacts: **60.30 Unity update frames/s over 155.187 sampled seconds** after warm-up, with sparse
last-frame samples of **111–124 draw calls/batches** and **124,958–144,904 triangles**. The run registered
62 procedural avatars; only **28–42 were LOD-enabled**, and 20–34 were distance-culled. LOD-enabled
does not establish camera-frustum visibility. The hot device stayed at thermal status 3 with render
scale 0.6 and procedural animation scale 0.5. This was offline placeholder motion, without production
Humanoid animation, room networking, server NPC decisions or voice. It does not satisfy the final
10-minute multiplayer/voice performance gate.

A separate [build-e presentation snapshot](screens/quality-presentation-20261005e/presentation-summary.json)
recorded 127 valid Android actual-present timestamps spanning 2.105091154 seconds: **59.85 presented
frames/s** at that moment. This short snapshot is distinct from build d's longer update-loop result;
neither is presented as a long-running build-e display-pacing measurement. The crowd report links
the raw latency buffer, display state and measurement definitions.

Run `BuildTimelineSquare` again only when intentionally rebuilding content; it restores the unbaked
prefab, so follow it with this separate bake before shipping baked lighting.
