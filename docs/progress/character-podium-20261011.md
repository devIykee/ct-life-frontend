READY FOR COORDINATOR VALIDATION: `client/Assets/Scripts/UI/Screens/AvatarPreview.cs`, `AvatarPreviewPodium.cs` + meta, `client/Assets/Tests/PlayMode/DistinctCharacterPreviewTests.cs`.

# Creator podium and framing — 2026-10-11

The creator can opt into a real matte gold-and-purple beveled plinth with `AvatarPreview.SetPodium(true)`. Other callers default to the previous presentation. The plinth shares the character's camera and key light; the initial measured soles meet its top. A low-opacity contact shadow lies on the top surface. Portrait mode hides both plinth and shadow. Disposing the preview destroys its private stage mesh and materials.

The full-body camera reserves the selected Humanoid's arm reach, including fingers. Preview-only skinned renderers update their moving bounds. Before a one-shot plays, its real skinned geometry is measured at thirteen phases, with a small interval margin; the envelope is cached once per look/state. No mesh baking or per-vertex solve runs continuously. The camera fits the projected silhouette at a slight downward angle, retaining a stable turntable frame. This addresses the old idle-only bounds; the creator layout must also give the transparent viewport space beyond the small stage rectangle.

Coordinator integration: construct a square preview (`1080 × 1080`), call `SetPodium(true)` before setting a look, and give its UI image about `1000 × 1000` logical pixels. Allow overflow around the image and its stage, and place the image behind the controls. Remove the former painted oval shadow and heavy spotlight from the creator, since the scene now supplies its stage. Face/ID-photo consumers continue using `SetPortrait(true)`.

Added PlayMode coverage uses the actual Dex, Shark and Froggy meshes and shipped controller. It checks foot contact, real gold/purple rendered pixels, complete posed surfaces through wave/stretch/jump at 18 phases and four turntable angles, hidden podium in portrait mode, opt-in behavior and restoration of the original camera when disabled. The animation test samples phases different from the production envelope. These tests have been written but **not run by this agent**; the coordinator owns Unity and the build clone. `git diff --check` passed.

Current Unity 6000.3 primary sources opened before implementation:

- [BakeMesh](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SkinnedMeshRenderer.BakeMesh.html): compensated transform scale and CPU skinning cost.
- [Animator.Play](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Animator.Play.html) and [Animator.Update](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Animator.Update.html): explicit state/phase evaluation for the private preview.
- [updateWhenOffscreen](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SkinnedMeshRenderer-updateWhenOffscreen.html): animated skinned bounds.
- [WorldToViewportPoint](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Camera.WorldToViewportPoint.html): real vertex/frustum regression checks.
- [Mesh.SetVertices](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh.SetVertices.html): runtime podium mesh construction.

No source models, materials, textures, Home screen, creator layout, backend, credentials or wallet state were edited. No Unity process, build, device install, commit or push was performed by this agent.

## Back input regression

Device QA found the enlarged transparent preview intercepted touches at Back in Customize. The coordinator moved the top bar after creator Content while retaining Overlay above it. `OnboardingControllerTests.CreatorBackWinsPickingOverTheEnlargedPreviewWhileOverlayStaysAboveIt` runs the real creator and USS, confirms the overlap, checks the hierarchy, uses `IPanel.Pick` at Back’s panel-space centre, and verifies an overlay shield still intercepts the same point. Coordinator evidence `character-podium-20261011/back-picking.xml` reports 1/1 passed, zero failures. The test uses the existing controlled network fixture and asserts navigation made no POSTs. Final rebuilt-device verification remains with the coordinator.
