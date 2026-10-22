# Camera and Seeker rendering repairs — 19 October 2026

[Unverified] The intermittent neon sky band's root cause remains open. This pass implements two concrete source repairs and a narrowly targeted compatibility workaround; it does not claim full demo acceptance.

The user corrected the working priority: spend more effort repairing known faults and keep validation proportionate. No new UI, art, LOD, geometry budget, backend or entitlement work was added.

## Changes

- **Close-body camera:** obstacle lift could make the final camera-to-pivot distance exceed the avatar-hide threshold even with a collapsed camera boom. Visibility now uses the boom length. Collision, lift, orbit and recovery behavior remain in place. A regression with a real wall collider and renderer covers hiding and restoration.
- **Sky arithmetic:** the cloud projection divided by `h + 0.18`, including zero at `h = -0.18`. The denominator is now bounded at `0.20`. The changed directions have a zero cloud mask (`h <= 0.02`); visible cloud projection, colors and materials are unchanged. No evidence connects this singularity to the recorded upper-sky band.
- **Seeker compatibility:** retain Vulkan first globally, but select the existing GLES3 fallback for the exact combination `Mali-G615 MC2` + brand `solanamobile` + product `seeker`. The committed filter retains Unity 6000.3.25f1's native Google/oriole-raven-bluejay exclusion and appends one Seeker row. Existing assigned filter arrays are preserved; setup is idempotent and validates the filter. Missing assets cause a clear setup error.

The filter asset was generated through the actual Unity menu, **Assets/Create/Vulkan Settings/Device Filter**. Constructor and ObjectFactory creation both omitted native default rows in this Editor; those artifacts were replaced before installation. The first build was intentionally interrupted (exit130); its log and reason are retained. The final asset's native default row was compared byte-for-byte with the menu-generated baseline. The disposable creation helper was removed before the final build.

## Focused validation

- Unity6000.3.25f1 native `CTLife.Tests.OnFootCameraTests`: **12/12 passed**, no failures or skips. This is the current pass's focused run; the earlier2067-test full-suite result belongs to the prior source.
- Host shader arithmetic:100001 sampled directions had finite denominators; all49001 samples above the zero-mask boundary retained the original denominator. Android shader compilation is part of the build below.
- Same prior APK, same fixed development viewpoint, separate50s Vulkan/GLES captures: both settled to six reported windows of30.2fps at a30fps thermal target. Available GPU medians were27.7375ms Vulkan and30.169ms GLES, so no performance improvement is claimed. The Vulkan vertex-data warning recurred; GLES did not emit it. The sparse fixed-view visual samples did **not** reproduce the neon flash under either API, so this comparison does not prove the workaround resolves it. No gameplay input or route acceptance was involved.

- Android development build completed in109s:263.6MiB,0errors,37warnings. `ctlife-render-camera-dev.apk`, SHA256 `1e40a8a3c02201c0065a6b3e7437f67397711497893dc17b9daa556594a39469`. Installed with `adb install -r` at19:19:03UTC; installed APK hash matches. No uninstall/data clear.
- Normal API selection, **without a Unity API override**, booted `OpenGLES3` on the Seeker. A short fixed-view offline launch produced two normal world/sky screenshots; last two reported windows were30.2fps at target30. No vertex-data warning or fatal-signal marker appeared in the captured app-process log. This is a startup/appearance check, not a complete route or an intermittent-flash resolution claim.
- At19:20:06UTC, the app was stopped and the three pre-pass preference/key/profile files were restored byte-for-byte. Recorders stopped, paired capture duplicates removed after hash verification, reverse mappings restored to4010/2567/7880/7881, and owned device lock released. The repaired APK remains installed. No new backend services were started.
- No broad full-suite rerun, full gameplay route, light/dark UI audit or iOS build was performed in this scoped Unity Android pass. No new dependency or UI component was added. No remote push, deployment, store submission, real-wallet payment or external message occurred.

Receipts: `installed-build.json`, `device-restoration.json`, `installed-normal-launch-0.png`, `installed-normal-launch-1.png`. All seven source/asset/settings hashes matched `source-freeze.json` after packaging; rerunning setup during the build did not add a duplicate filter row.

## Sources and evidence

- [Unity6.3 filter-asset creation](https://docs.unity3d.com/6000.3/Documentation/Manual/create-vulkan-device-filtering-asset.html) documents native default exclusions and the creation menu.
- [Unity6.3 filter reference](https://docs.unity3d.com/6000.3/Documentation/Manual/vulkan-device-filter-list-asset-reference.html) describes matching and allow/deny behavior.
- [Unity's ProjectWindowUtil source](https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/6000.3/Editor/Mono/ProjectBrowser/ProjectWindowUtil.cs) shows the native asset finalization step.
- [UUM-79031](https://issuetracker.unity3d.com/issues/android-vulkan-skybox-flickers-when-using-the-vulkan-graphics-api-on-certain-devices) reports Vulkan skybox flicker on other Mali hardware and a GLES workaround. It is not a Seeker-specific diagnosis. See the earlier [rendering investigation](../research/audit-vulkan-rendering-20261019.md) for the direct fault screenshot and limitations.

External evidence: `/Users/mac/Documents/codes/projects/ct-life-evidence/audit-20261019/render-repair/` contains `camera-tests.xml`, `camera-tests.log`, `filter-native-menu.log`, `unity-native-defaults.vulkandevicefilter`, `filter-defaults-verification.json`, `source-freeze.json` and the build logs. Raw device logs, paired recordings and backup preferences remain in the private scratchpad.
