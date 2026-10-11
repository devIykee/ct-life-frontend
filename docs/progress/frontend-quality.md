# Frontend quality checkpoint — 2026-10-05

**Current presentation work:** [game lobby, controls and two-character review](seeker-game-quality-20261006.md). That review supersedes this historical build-e snapshot. Snow is now imported as an exact authored look with separate creator/world prefabs; the old temporary-avatar captures and build-d crowd result below do not describe the current build. Full customization, cast, animation refinement, sound content and backend integration remain open.

## Historical build-e snapshot

**Historical build:** `client/Builds/ctlife-quality-dev.apk`, build e, 129.3 MiB. Android IL2CPP build succeeded with **0 errors and 29 warnings**, installed and visually checked on Seeker. This is a frontend checkpoint; final characters, full gameplay and backend integration are incomplete.

## Inspect the results

- [Latest actual Seeker city capture](screens/quality-review-20261005e/quality-review-20261005e/city.png): restored model detail, baked shadows and corrected haze.
- [Actual creator capture](screens/quality-review-20261005c/quality-review-20261005c/creator.png): full head/feet framing; still the temporary avatar.
- [Adult Snow proportions](../../art/character-candidates/snow-adult-proof/before-after.jpg) and [revised phone-grip/wave proof](../../art/character-candidates/snow-hand-quality/gesture-proof.jpg).
- [Character conversion report](../../art/character-candidates/snow-hand-quality/README.md), [world report](world-quality.md), [UI report](ui-quality.md).
- [Measured crowd check](seeker-crowd-check-20261005.md), [telemetry definitions](performance-caveats.md).
- [ORE/SKR integration audit](ore-skr-client-audit.md), [backend questions](backend-dev-feedback.md).

## Delivered and verified

| Area | Result |
| --- | --- |
| City geometry | 966 sourced placements, combined where appropriate; 30 materials; 124,610 worst authored environment triangles before culling. All 15 anchors retained, with complete navigation paths. |
| Model detail | Source LOD0 retained with corrected transitions for Android bias 0.7. Device images show complete trunks/blossoms and storefront details. |
| Lighting | Five non-directional maps, 3,473,408 color texels, 217 bound static renderers across every LOD. GPU bake method 53.6 seconds; total editor process 70.4 seconds. Source FBXs untouched. |
| Runtime lighting | Shader-retention guard passed. Device assertions passed for map indices/scale-offsets, unload into an unbaked venue and return with exactly five maps. Final haze 70–165 m. |
| Camera/movement | Player fits at entrance; offline walk reaches center within 0.09 m. Venue return retains camera target/navigation. Fountain surfaces no longer overlap. |
| Interface | Revised sign-in, creator, HUD, phone, map and core views. Tested needs toggle, map overflow, safe areas and creator rendering/framing on Seeker. |
| Character study | 55 bones, 30 weighted digit joints, retained detailed hands. City candidate 11,451 triangles; creator 18,365. Both FBXs pass all 13 Blender round-trip checks including posed geometry. No Unity/device claim for these candidates. |
| Local crowd | Build d: 60.30 Unity update frames/s across 155.187 sampled seconds after warm-up; 62 registered procedural actors, 28–42 LOD-enabled, no voice/network. Hot device at 44°C, thermal status 3, render scale 0.6. Sparse draw samples 111–124. |
| Presentation spot check | Separate build-e SurfaceView buffer: 127 actual-present timestamps over 2.105 seconds, 59.85 presented frames/s. Short snapshot only; not the sustained multiplayer gate. |

Build-e image inspected after its fog-only correction. The longer crowd result remains tied to build d; evidence folders retain APK hashes. No further batching was added: current observations did not justify its memory cost.

## Implementation

- Shipped-venue catalog, additive loading, saved navigation and scoped camera/lighting state; unload releases handles and restores lighting.
- Development-only review mode uses actual Unity views with labelled sample data. No REST/realtime connection or account/session writes; write actions are rejected. Excluded from release and `CTLIFE_PROD` builds.
- Shared phone catalog aligns review and normal flow. Offline walking animates; server seat ownership is not fabricated.
- Preview uses a compatible single-sample Vulkan render target, isolated layer, measured bounds and explicit disposal.
- Build script avoids duplicate Addressables builds, restores the prior setting and reports actual APK size.
- Telemetry separates registered/culled actors, procedural/Animator assets, observed/applied thermal state, render scale and latest-frame counters. The measurement helper validates mandatory timing fields and stops the test app afterward.

## Remaining work

- Character atlas/material consolidation, canonical Unity Humanoid mapping, facial expressions, garment/customization morphs, sleeve/shoe cleanup and final animation/device tests. Rain's adult derivative is not built yet.
- Creator selections still do not produce their named clothing/hair on the temporary avatar. Snow remains outside Unity.
- Imported Animator characters need the intended mesh/animation LOD behavior; placeholder results do not prove that budget.
- Full stress gate: 50 connected players + 12 server NPCs, voice on, all emoting, ten minutes on Seeker. Also cold-device quality, sustained presented-frame pacing, peak memory and repeated-travel memory.
- Gameplay phases, ORE/SKR payments, markets, calls and backend integration remain incomplete. The audit records cluster, confirmation, denomination and quote-schema questions; no real transaction occurred.

## Reproduce

Development APK only:

```sh
adb shell am start -W -n com.ctlife.app/com.unity3d.player.UnityPlayerGameActivity -e review 1 -e reviewScreen city
python3 client/Tools~/seeker_review.py --seconds 180 --crowd 62 --output /tmp/ct-life-crowd-review
```

- `reviewCapture`: writable app-owned output folder.
- `reviewSequence`: views, default `city,signin,creator,phone,map,bank`.
- `reviewChecks=1`: walk, venue return, camera/navigation and baked-lightmap assertions.
- `reviewCrowd=1..62`: total synthetic local actors; no server NPC/obstacle-avoidance validation.
- `reviewExit=1`: exit after captures/check failure.
- Helper installs only with explicit `--install`; otherwise measures the installed APK. Initial warm-up excluded; final screenshot outside sampling; thermal/memory evidence saved. Editor review helper remains untested.

## Failure history and boundaries

- First capture failed camera framing and Vulkan preview rendering; the next exposed damaged environment LODs; build d exposed excessive foreground haze. Each was corrected and recaptured; earlier images remain diagnostic.
- First APK build logged an Addressables automatic-hook exception; subsequent builds did not after the duplicate-build correction.
- Offline compiler initially reported `CS1061` for `UniversalRenderPipelineAsset.useAdaptivePerformance`: the compile-only stub lacked the real pinned URP 17.3 property. Stub corrected; offline compiler and real builds passed.
- Original Claude Build Spec/contract exports remain baseline, with later user art decisions overriding old chibi guidance. Backend/program ownership remains with `devIykee`; imported contract and backend source were not changed.
- No commit, push, account creation, purchase, external message, deployment or real transaction. Source assets retained with attribution in `art/LICENSES.md`.
