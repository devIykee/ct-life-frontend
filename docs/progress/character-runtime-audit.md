# Snow Humanoid/runtime audit — 2026-10-05

Source audit by `ct_contract_exam`; no importer, registry, avatar runtime, source art or world mutations in this pass. Added only the isolated Editor validation helper. Coordinating agent owns Unity import, builds and device review. **Unity compilation/execution of this helper and Snow device quality are pending at this checkpoint.**

## Import requirements

- `client/Assets/Scripts/Editor/ContentImport.cs:179`: character models currently become Humanoid with four influences, imported normals, no imported materials, unit global scale and baked axis conversion. Any non-`/body/` character copies the old CTL body Avatar through `SourceAvatar()` (`:222`). Snow must take a specific `CreateFromThisModel` branch with its own explicit `HumanDescription`; copying the unrelated CTL Avatar is incorrect.
- The Snow branch must execute before the generic environment branch when choosing a path outside `Assets/Art/characters`. That branch otherwise sets animation type to `None`.
- Preserve the final FBX bind pose and custom normals. Build `HumanDescription.skeleton` from the imported rest transforms, including the model/armature wrapper and `Root`; do not copy a skeleton description from CTL or flatten/rotate bones individually after the mesh was bound. Verify valid Humanoid import before optimizing hierarchy.
- `ContentImport.cs:269` caps character textures at 1024 and applies ASTC 6×6. Artist's new creator atlas is planned at 2048: give that atlas an explicit 2048 Android override before evaluating face quality. Color atlas is sRGB; a verified tangent-space normal atlas imports as `NormalMap`, not sRGB color. Preserve mipmaps and UV-island gutters.
- `materialImportMode=None` means the new one-material prefab needs an explicit URP material assignment. Imported FBX material names alone do not bind the atlas.

## Exact mapping

Source is `art/character-candidates/scripts/build_snow_hands.py:32`; artist's machine-readable mapping is `art/character-candidates/unity-snow/unity-humanoid-map.json`. There are **55 skeleton bones and 54 Humanoid mappings**. `Root` stays in the skeleton but is not a HumanBone; `UpperChest` is intentionally absent.

| Unity HumanBodyBones token | Snow transform |
| --- | --- |
| Hips, Spine, Chest, Neck, Head, Jaw | Same name |
| LeftEye / RightEye | Eye.L / Eye.R |
| LeftShoulder / RightShoulder | Shoulder.L / Shoulder.R |
| LeftUpperArm / RightUpperArm | UpperArm.L / UpperArm.R |
| LeftLowerArm / RightLowerArm | Forearm.L / Forearm.R |
| LeftHand / RightHand | Hand.L / Hand.R |
| LeftUpperLeg / RightUpperLeg | Thigh.L / Thigh.R |
| LeftLowerLeg / RightLowerLeg | Calf.L / Calf.R |
| LeftFoot / RightFoot | Foot.L / Foot.R |
| LeftToes / RightToes | Toes.L / Toes.R |
| Left/Right Thumb, Index, Middle, Ring Proximal/Intermediate/Distal | Thumb/Index/Middle/Ring 1/2/3 .L/.R |
| Left/Right Little Proximal/Intermediate/Distal | Pinky1/2/3.L/.R |

For JSON keys use `Enum.Parse<HumanBodyBones>(key)`, then obtain `HumanBone.humanName` from `HumanTrait.BoneName[(int)bone]`; do not assume enum-token spelling equals every canonical human name. `HumanBone.boneName` is the exact Snow transform string. Use default human limits initially. Unity documents `HumanTrait.BoneName` as the source of allowed names: [Unity 6.3 HumanBone.humanName](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/HumanBone-humanName.html).

## Existing clips: what is and is not established

- `client/Assets/Art/animations/clip_map.json` contains **46 action IDs / 44 distinct FBXs**. Keep those FBXs on their own existing CTL source Avatar; the Snow target Animator uses its own new Avatar and the shared controller.
- [Unverified until Unity validation] Imported clips contain usable Humanoid motion for retargeting. Serialized importer settings request Human/CopyFromOther and show no saved error, but this does not substitute for `AnimationClip.humanMotion`, `Avatar.isValid` and actual pose sampling.
- `client/Assets/Art/characters/male/body/ctl_male_body.fbx.meta:88` maps 25 body/eyes/jaw bones. It has **no Humanoid finger mappings**. Existing clip retargeting therefore does not establish articulated Snow fingers; thumbs-up, heart-hands and typing need visible review and later authored finger poses/channels where absent.
- Snow's source remap assigns facial surface weights to `Head` except the eyes. The `Jaw` bone exists but does not currently deform the mouth. No expressive facial system is claimed.
- `ContentImport.BuildAvatarController()` (`:83`) adds a `speed` float and one state per action, but no locomotion blend tree or state speed multiplier. `AvatarView.Speed()` writes this currently unused parameter. Real walking tempo/foot contact must be assessed against world movement, and different proportions can require clip speed/contact adjustment.
- Existing preprocessing locks root rotation/Y/XZ and preserves original offsets. `AvatarFactory` disables root motion. Retarget review must cover foot-floor contact, sit height relative to fixed venue anchors, wrists/fingers, arm raises, shoulders/sleeves and death/fall bounds; a valid Avatar alone does not establish these.

## Runtime and visible quality

- `CharacterRegistry.ForLook()` (`:71`) prefers exact look/item entries before `defaultPlayer`; `ForArchetype()` (`:63`) prefers existing per-NPC entries before `defaultNpc`. Replacing defaults does not override old mappings. `FillCharacterRegistry()` also keeps non-null entries. Make the intended preview/player mapping explicit; this audit does not authorize replacing the whole cast.
- `AvatarFactory.cs:26` accepts a prefab with an existing Animator; if absent, it adds an Animator without an Avatar. Ship a prefab containing the valid candidate Animator. Keep prefab root position/rotation identity, unit scale, floor origin, adult height in metres and Unity-forward orientation. A uniformly scaled child may be valid, but scale must match the skinned mesh/bind pose and should not be a hidden 100× FBX correction.
- Factory collider is fixed at 1.8 m / centre 0.9 m, and its blob shadow sits at root Y+0.02. Registry `height` is used by head/voice anchors; despite its comment saying eye height, fallback measures the renderer top. Set/measure it consistently with actual head placement, then inspect labels/voice anchors.
- Registry models currently get `lods=null` in the factory; `AvatarView.ApplyLod()` only swaps meshes for procedural `PlaceholderLods`. A multi-LOD imported prefab requires a real `LODGroup` or explicit runtime mesh integration. Do not put multiple LOD skins in the prefab and assume the current factory selects one.
- `AvatarLod.cs` assigns local/nearest 12 tier0, next24 tier1, remaining tier2, with distance culling beyond25m. `AvatarView.cs:157` applies half/quarter update intervals only to `ProceduralAnimator`; a visible real Animator remains enabled at speed1 every frame. The previous procedural crowd benchmark cannot certify Snow crowd performance. Lowering Animator playback speed is not frame-rate throttling.
- Preserve the detailed creator/near-character mesh and face atlas until actual visual inspection passes. Use independent creator and city LOD choices; the creator should not lose face/hand detail because a crowd budget is applied. The artist is preparing one retained-surface, single-skin atlas candidate; this audit did not measure the final export.
- Recommended first material is opaque URP Simple Lit with the atlas, restrained specular and a normal map only if its bake is validated. The pinned URP17.3 `SimpleLitForwardPass.hlsl` uses main-light shading and SH/GI; `BakedLitForwardPass.hlsl` is for baked/ambient lighting and is inappropriate for the desired moving face key-light response. Keep realtime shadow casting off and retain the existing blob until real measurements justify a change. Avoid transparent hair layers for this opaque mesh candidate.

## Validation helper and handoff

Added `client/Assets/Scripts/Editor/CharacterValidation.cs`. It opens an isolated preview scene, instantiates a disposable clone and never writes importer settings, prefabs or registry assets. It throws on invalid imports rather than publishing a partial result.

Coordinator's CLI suffix (replace the asset argument with the final Unity asset path):

```text
-executeMethod CTLife.EditorTools.CharacterValidation.ValidateSnow
-ctCharacterAsset Assets/path/to/Snow.prefab
```

The method validates own CreateFromThisModel Avatar; exact 54 mappings; transform, floor and adult-scale bounds; skin bones/bind poses/max-four normalized weights when readable; material/atlas assignment and normal-map import; every distinct shared-controller clip's `humanMotion`; and 27 numeric skin/pose samples across idle, walk, run, sit, typing, wave, clap, thumbs-up and death. It logs all-LOD authored triangles separately from visible render cost. Unreadable CPU weight data is explicitly reported as skipped.

APIs were checked against installed Unity6000.3.25f1 managed XML docs. The mesh weight arrays are borrowed views and are not disposed, following [GetAllBoneWeights](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh.GetAllBoneWeights.html) and [GetBonesPerVertex](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh.GetBonesPerVertex.html). Static source/whitespace checks passed; **no Unity compilation, numeric sample results or device render pass is claimed by this agent**.

Next: coordinator imports the artist's final FBX/atlas, creates a specific candidate prefab, compiles/runs this check, and captures the actual creator + walk/sit/hand poses. Any valid-import result remains separate from visual acceptance and real Humanoid crowd profiling.
