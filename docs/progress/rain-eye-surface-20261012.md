# Rain's missing pupil surface — 2026-10-12

READY TO COMMIT: Rain-family cast FBXs/manifests in `art/cast/export` and `client/Assets/Art/characters/cast`; the four affected cast pipeline scripts; `RainEyeRenderTests.cs` and its UV reference fixture; this progress note and `art/LICENSES.md` — Restore Rain's missing right pupil and retain it on cast rebuilds.

Status: source diagnosis, all-family FBX preservation, and coordinator's before/after Unity tests passed. Corrected assets are copied into main. The coordinator subsequently built and installed the Android APK and visually verified both solid pupils on the Seeker's people, traits, dream and name portraits. Final combined acceptance is in `illustrated-card-and-eyes-review-20261012.md`. No Unity, ADB, build or publication was run by the rig subagent.

## Cause, independently reproduced

`art/cast/scripts/cast_extract.py` classified an eye face as hidden solely by `normal.y > 0.3`. Rain's source right pupil has a reverse-wound **front-positioned** opaque quad (source face 7043, centre `(-0.03730581, -0.09828892, 1.55426955)` metres). That quad passed the deletion predicate. The left pupil cap survived. The cast hero export retained 455 eye triangles and a real opening inside the right iris.

This differs from the clothing backface issue. The authored eye material has Alpha 1 and Transmission Weight 0. The current runtime palette is opaque and already renders both sides; a shader cannot draw the deleted pupil. Whole-character alpha also misses this because the inside of the opaque head can appear behind the opening.

The coordinator's before capture independently reproduces this in Unity: `before/loco_idle-0.30-0-actual.png` shows head/skin through the right pupil. The companion isolated eye-geometry image has a circular opening where that pupil should be. Across 24 close poses, the eye-only flood fill measured up to 1,405 enclosed missing pixels (1,351 in front idle), with zero partly transparent pixels.

Evidence: `/Users/mac/Documents/codes/projects/ct-life-evidence/eye-card-revision-20261012/`. The source audit is `source-eye-removal.json`; baseline render measurements are **`before/eye-measurements.json`**.

## Correction

- Extraction now requires a culled eye face to be physically behind the eye joint as well as rear-facing. The original front pupil can no longer be removed by winding alone.
- `cast_looks.py` also calls the shared `cast_eye_surface.py` restoration when assembling older cached LOD inputs, so a wardrobe rebuild from pre-correction intermediates retains the pupil. Original cached source files are preserved.
- `art/cast/scripts/repair_rain_pupil.py` produces explicit review candidates from the saved native cast source. It mirrors the surviving authored cap's four vertices, two triangles and atlas UVs, assigns the right-eye weight, and preserves all pre-existing geometry and shading data. This restores the original surface without rerunning wardrobe generation or decimation.
- Candidate scope is every affected Rain-derived appearance: 29 looks, 99 meshes across existing hero/world LODs, 41 FBX files. Existing materials and the accepted clothing backface correction remain unchanged.
- Native-source preservation assertions pass with **zero** changes to existing positions, UVs, weights, faces and decoded custom normals. Copying the packed custom-normal data and edge flags directly avoids both normal re-quantization and changes to smoothing boundaries.
- A separate FBX round-trip comparison passed **99/99 meshes across all 41 files**, including exact equality of existing positions, faces, UVs, weights, normals, skeleton and object transforms. The source `art/cast/export` FBXs and current Unity FBXs were identical before this correction. `fbx-preservation-validation.json` holds the complete result. Accepted files are now present in both locations, and both manifests contain updated counts, hashes and a surface-correction provenance entry.

## Verified Unity outcome

The coordinator's `eyes-after.xml` reports **2/2 passed**: the always-on solid-pupil regression and the actual evidence capture. `after/eye-measurements.json` records 457 eye triangles, **zero enclosed missing pixels and zero partly transparent pixels in every one of the 24 poses**. The corresponding front actual portrait visibly has two solid black pupils; the eye-only render has no opening. Before and after captures use the same cameras and phases. No color, clothing or whole-character material change was needed for this eye correction.

## Actual surface regression

`RainEyeRenderTests` uses `SkinnedMeshRenderer.BakeMesh` for every sampled pose. An atlas reference captured from actual eye-weighted source vertices identifies the eye triangles without changing the runtime mesh's readability flag. The test asserts the selected geometry count and visible surface count, renders it separately, and floods the exterior transparency; any remaining enclosed hole is an actual opening in an eye, even if the full head behind it is opaque.

- Always-on test: `CTLife.Tests.RainEyeRenderTests.RainPupilsRemainSolidAcrossClosePortraitPoses`.
- Opt-in evidence: `CTLife.Tests.RainEyeRenderTests.CaptureActualEyesAcrossPortraitPoses` with `CTLIFE_EYE_REVIEW=<external directory>`.
- Each runs idle, folded arms, raised stretch and wave at two phases and three viewing angles. The latter writes actual shaded faces, isolated geometry renders and `eye-measurements.json`.

Current primary APIs checked: [Unity 6.3 BakeMesh](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html), [Mesh.SetTriangles](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh.SetTriangles.html), and [Humanoid LeftEye](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/HumanBodyBones.LeftEye.html).
