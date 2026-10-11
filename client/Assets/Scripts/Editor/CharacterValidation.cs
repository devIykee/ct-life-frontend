using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Read-only import/retarget check for the Snow 55-bone candidate. This does not create or repair assets.
    /// CLI: -executeMethod CTLife.EditorTools.CharacterValidation.ValidateSnow -ctCharacterAsset Assets/...prefab
    /// It also accepts the imported FBX once its material and Avatar have been configured.
    /// Numeric checks are not a substitute for reviewing the face, hands, sit and walk on the device.
    /// </summary>
    public static class CharacterValidation
    {
        private const string ControllerPath = "Assets/Resources/Characters/AvatarController.controller";

        public static void DiagnoseClipForward()
        {
            const string path = "Assets/Art/animations/loco_idle.fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var previous = importer.userData;
            try
            {
                LogIdleForward("current");
                importer.userData = "ct-life:original-orientation-probe";
                importer.SaveAndReimport();
                LogIdleForward("original-orientation");
            }
            finally { importer.userData = previous; importer.SaveAndReimport(); }
        }

        private static void LogIdleForward(string mode)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(AuthoredCharacterImport.PrefabPath));
            try
            {
                var a = go.GetComponentInChildren<Animator>();
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                a.Rebind(); a.Play("loco_idle", 0, 0.4f); a.Update(0);
                var eyes = (a.GetBoneTransform(HumanBodyBones.LeftEye).position + a.GetBoneTransform(HumanBodyBones.RightEye).position) * 0.5f;
                Debug.Log($"CTLIFE [forward-probe] mode={mode}; face={go.transform.InverseTransformDirection(eyes - a.GetBoneTransform(HumanBodyBones.Head).position):F5}; visualYaw={a.transform.localEulerAngles}");
            }
            finally { Object.DestroyImmediate(go); }
        }

        public static void DiagnoseIdle()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AuthoredCharacterImport.PrefabPath);
            var go = Object.Instantiate(prefab);
            try
            {
                var a = go.GetComponentInChildren<Animator>();
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                a.Rebind();
                a.Play("loco_idle", 0, 0.4f);
                a.Update(0);
                using var handler = new HumanPoseHandler(a.avatar, a.transform);
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                for (int i = 0; i < pose.muscles.Length; i++)
                    Debug.Log($"CTLIFE [pose-source] {HumanTrait.MuscleName[i]}={pose.muscles[i]:F4}");
                Debug.Log($"CTLIFE [pose-source] idle hands {a.GetBoneTransform(HumanBodyBones.LeftHand).position} / {a.GetBoneTransform(HumanBodyBones.RightHand).position}");
                pose.muscles = new float[HumanTrait.MuscleCount];
                handler.SetHumanPose(ref pose);
                Debug.Log($"CTLIFE [pose-source] zero hands {a.GetBoneTransform(HumanBodyBones.LeftHand).position} / {a.GetBoneTransform(HumanBodyBones.RightHand).position}");
            }
            finally { Object.DestroyImmediate(go); }
        }

        public static void ValidateSnow()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-ctCharacterAsset");
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("Supply -ctCharacterAsset Assets/path/to/Snow.prefab (or configured FBX).");
            ValidateSnowAsset(args[index + 1]);
        }

        public static void ValidateSnowAsset(string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null) throw new FileNotFoundException("Character asset not found: " + assetPath);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            Require(controller != null, "Shared AvatarController is missing.");
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = Object.Instantiate(asset);
                SceneManager.MoveGameObjectToScene(root, scene);
                root.SetActive(true);
                // A preview-scene clone is the only object modified by this check.
                var animators = root.GetComponentsInChildren<Animator>(true);
                Require(animators.Length == 1, "Expected one Animator, found " + animators.Length);
                var animator = animators[0];
                animator.enabled = false;
                Require(animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman,
                    "Candidate must have its own valid Humanoid Avatar assigned to its Animator.");
                string avatarPath = AssetDatabase.GetAssetPath(animator.avatar);
                var importer = AssetImporter.GetAtPath(avatarPath) as ModelImporter;
                Require(importer != null && importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel,
                    "Candidate Avatar must use CreateFromThisModel, not the placeholder's CopyFromOther Avatar.");
                Require(avatarPath.IndexOf("ctl_male_body", StringComparison.OrdinalIgnoreCase) < 0 &&
                    avatarPath.IndexOf("ctl_female_body", StringComparison.OrdinalIgnoreCase) < 0,
                    "Candidate Animator still references a placeholder Avatar.");

                Require(root.transform.localPosition.sqrMagnitude < 0.0001f &&
                    Quaternion.Angle(root.transform.localRotation, Quaternion.identity) < 0.1f &&
                    (root.transform.localScale - Vector3.one).sqrMagnitude < 0.0001f,
                    "Prefab root must have zero position, identity rotation and unit scale.");
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    Require(Finite(t.localPosition) && Finite(t.localScale), "Non-finite transform: " + t.name);
                    var scale = t.lossyScale;
                    Require(scale.x > 0 && scale.y > 0 && scale.z > 0, "Negative/zero scale: " + t.name);
                    Require(Mathf.Abs(scale.x - scale.y) < 0.001f && Mathf.Abs(scale.y - scale.z) < 0.001f,
                        "Nonuniform skeleton/mesh scale: " + t.name);
                }
                var expected = SnowBoneMap();
                foreach (var pair in expected)
                {
                    var t = animator.GetBoneTransform(pair.Key);
                    Require(t != null && t.name == pair.Value,
                        $"Humanoid mapping {pair.Key}: expected {pair.Value}, got {(t == null ? "<none>" : t.name)}.");
                }
                Require(animator.GetBoneTransform(HumanBodyBones.UpperChest) == null,
                    "Snow has no UpperChest: do not map Chest into two Humanoid slots.");
                Debug.Log($"CTLIFE [character-check] Avatar valid: {avatarPath}; {expected.Count}/54 Snow mappings; " +
                    $"humanScale={animator.humanScale:F4}; importScale={importer.globalScale:F4}; " +
                    $"fileScale={importer.useFileScale}; bakedAxes={importer.bakeAxisConversion}.");

                var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Require(skins.Length > 0, "No SkinnedMeshRenderer found.");
                long triangles = 0;
                var materials = new HashSet<Material>();
                foreach (var skin in skins)
                {
                    var mesh = skin.sharedMesh;
                    Require(mesh != null && mesh.vertexCount > 0, "Empty skin: " + skin.name);
                    Require(skin.rootBone != null && skin.bones.All(b => b != null), "Missing skin root/bones: " + skin.name);
                    Require(mesh.bindposes.Length == skin.bones.Length, "Bind-pose/bone count mismatch: " + skin.name);
                    for (int s = 0; s < mesh.subMeshCount; s++) triangles += (long)mesh.GetIndexCount(s) / 3;
                    Require(skin.sharedMaterials.Length == mesh.subMeshCount && skin.sharedMaterials.All(m => m != null),
                        "Missing material/submesh assignment: " + skin.name);
                    foreach (var material in skin.sharedMaterials) materials.Add(material);
                    CheckWeights(mesh, skin.bones.Length);
                    Debug.Log($"CTLIFE [character-check] skin={skin.name}; vertices={mesh.vertexCount}; " +
                        $"bones={skin.bones.Length}; shapes={mesh.blendShapeCount}; bounds={skin.localBounds}; " +
                        $"offscreenUpdates={skin.updateWhenOffscreen}.");
                }
                foreach (var material in materials)
                {
                    Require(material.shader != null && material.shader.isSupported, "Unsupported shader: " + material.name);
                    var atlas = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : material.mainTexture;
                    Require(atlas != null, "No color atlas assigned: " + material.name);
                    Debug.Log($"CTLIFE [character-check] material={material.name}; shader={material.shader.name}; " +
                        $"atlas={atlas.name} {atlas.width}x{atlas.height}; keywords={string.Join(",", material.shaderKeywords)}.");
                    if (material.shader.name.Contains("Baked Lit"))
                        Debug.LogWarning("CTLIFE [character-check] Baked Lit does not provide the moving character's realtime key-light shading.");
                    if (material.HasProperty("_BumpMap") && material.GetTexture("_BumpMap") != null)
                    {
                        var normal = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(material.GetTexture("_BumpMap"))) as TextureImporter;
                        Require(normal != null && normal.textureType == TextureImporterType.NormalMap,
                            "Normal atlas must import as NormalMap: " + material.name);
                    }
                }
                var rest = SkinBounds(skins, root.transform);
                Require(rest.size.y > 1.4f && rest.size.y < 2.2f, "Adult rest height is outside 1.4–2.2 metres: " + rest);
                Require(Mathf.Abs(rest.min.y) < 0.08f, "Character soles are not at the root floor plane: " + rest.min.y);
                Debug.Log($"CTLIFE [character-check] rest bounds={rest}; authoredTrianglesAllLODs={triangles}; " +
                    $"skins={skins.Length}; sharedMaterials={materials.Count}; LODGroups={root.GetComponentsInChildren<LODGroup>(true).Length}.");
                if (skins.Length > 1 && root.GetComponentsInChildren<LODGroup>(true).Length == 0)
                    Debug.LogWarning("CTLIFE [character-check] Multiple skins without a LODGroup: confirm these are body parts, not concurrently rendered LODs.");

                var clips = controller.animationClips.Distinct().ToArray();
                Require(clips.Length > 0, "Controller contains no clips.");
                foreach (var clip in clips)
                    Require(clip != null && clip.humanMotion && !clip.legacy && clip.length > 0,
                        "Shared controller clip is not valid Humanoid motion: " + (clip == null ? "<null>" : AssetDatabase.GetAssetPath(clip)));
                Debug.Log($"CTLIFE [character-check] {clips.Length}/{clips.Length} distinct controller clips contain Humanoid motion.");
                animator.runtimeAnimatorController = controller;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                animator.enabled = true;
                animator.Rebind();
                foreach (string state in new[] { "loco_idle", "loco_walk", "loco_run", "act_sit", "act_type", "emote_wave", "emote_clap", "emote_thumbs_up", "react_liquidated" })
                {
                    Require(animator.HasState(0, Animator.StringToHash(state)), "Controller state missing: " + state);
                    foreach (float phase in new[] { 0.05f, 0.4f, 0.8f })
                    {
                        animator.Play(state, 0, phase);
                        animator.Update(0);
                        var bounds = SkinBounds(skins, root.transform);
                        Require(Finite(bounds.center) && Finite(bounds.size) && bounds.size.sqrMagnitude < 36f,
                            $"Exploded/non-finite retarget bounds in {state} at {phase}: {bounds}");
                        var headLocal = root.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Head).position);
                        bool standing = state.StartsWith("loco_") || state.StartsWith("emote_");
                        Require(!standing || (headLocal.y > rest.size.y * 0.55f && bounds.size.y > rest.size.y * 0.65f),
                            $"Collapsed standing retarget in {state} at {phase}: head={headLocal}, bounds={bounds}");
                        Require(!state.StartsWith("act_") || headLocal.y > rest.size.y * 0.3f,
                            $"Collapsed seated retarget in {state} at {phase}: head={headLocal}");
                        if (state == "loco_idle")
                        {
                            foreach (var hand in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                                Require(root.transform.InverseTransformPoint(animator.GetBoneTransform(hand).position).y < headLocal.y - 0.25f,
                                    "Idle hands are raised: check the Humanoid T-pose reference, not just bone mapping.");
                            var eyes = (animator.GetBoneTransform(HumanBodyBones.LeftEye).position + animator.GetBoneTransform(HumanBodyBones.RightEye).position) * 0.5f;
                            var face = root.transform.InverseTransformDirection(eyes - animator.GetBoneTransform(HumanBodyBones.Head).position);
                            Require(face.z > 0.02f, "Idle face points away from navigation forward: " + face);
                        }
                        Require(root.transform.position.sqrMagnitude < 0.0001f,
                            "In-place clip moved the prefab root: " + state);
                        Debug.Log($"CTLIFE [character-check] sample={state}@{phase:F2}; skinBounds={bounds}; " +
                            $"head={animator.GetBoneTransform(HumanBodyBones.Head).position:F3}; " +
                            $"feet={animator.GetBoneTransform(HumanBodyBones.LeftFoot).position:F3}/{animator.GetBoneTransform(HumanBodyBones.RightFoot).position:F3}.");
                    }
                }
                Debug.Log("CTLIFE [character-check] PASS import/mapping/clip/numeric retarget checks. " +
                    "Device face/hand/contact review and real Humanoid crowd profiling remain required; CTL clips do not author Snow finger expressions.");
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        public static Dictionary<HumanBodyBones, string> SnowBoneMap()
        {
            var map = new Dictionary<HumanBodyBones, string>();
            foreach (string bone in new[] { "Hips", "Spine", "Chest", "Neck", "Head", "Jaw" })
                map.Add((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), bone), bone);
            foreach (string side in new[] { "Left", "Right" })
            {
                string suffix = side == "Left" ? ".L" : ".R";
                foreach (var pair in new[] { "Eye:Eye", "Shoulder:Shoulder", "UpperArm:UpperArm", "LowerArm:Forearm", "Hand:Hand", "UpperLeg:Thigh", "LowerLeg:Calf", "Foot:Foot", "Toes:Toes" })
                {
                    var names = pair.Split(':');
                    map.Add((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + names[0]), names[1] + suffix);
                }
                foreach (string digit in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    string sourceDigit = digit == "Little" ? "Pinky" : digit;
                    var joints = new[] { "Proximal", "Intermediate", "Distal" };
                    for (int i = 0; i < joints.Length; i++)
                        map.Add((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + digit + joints[i]), sourceDigit + (i + 1) + suffix);
                }
            }
            return map;
        }

        private static void CheckWeights(Mesh mesh, int boneCount)
        {
            if (!mesh.isReadable)
            {
                Debug.LogWarning("CTLIFE [character-check] CPU weight checks skipped for unreadable mesh: " + mesh.name);
                return;
            }
            // Unity 6.3 returns borrowed Allocator.None views here; the Mesh owns their storage.
            var counts = mesh.GetBonesPerVertex();
            var weights = mesh.GetAllBoneWeights();
            Require(counts.Length == mesh.vertexCount, "Missing skin weights: " + mesh.name);
            int offset = 0;
            foreach (byte count in counts)
            {
                Require(count > 0 && count <= 4, "Unweighted vertex or more than four influences: " + mesh.name);
                float total = 0;
                for (int i = 0; i < count; i++)
                {
                    var weight = weights[offset++];
                    Require(weight.boneIndex >= 0 && weight.boneIndex < boneCount && weight.weight > 0,
                        "Invalid skin weight: " + mesh.name);
                    total += weight.weight;
                }
                Require(Mathf.Abs(total - 1) < 0.005f, "Unnormalized skin weights: " + mesh.name);
            }
        }

        private static Bounds SkinBounds(IEnumerable<SkinnedMeshRenderer> skins, Transform root)
        {
            var mesh = new Mesh();
            bool first = true;
            var bounds = new Bounds();
            try
            {
                foreach (var skin in skins)
                {
                    skin.BakeMesh(mesh, false);
                    foreach (var vertex in mesh.vertices)
                    {
                        var point = root.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                        Require(Finite(point), "Non-finite skinned vertex: " + skin.name);
                        if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                        else bounds.Encapsulate(point);
                    }
                    mesh.Clear();
                }
                Require(!first, "No skinned vertices were baked.");
                return bounds;
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        private static bool Finite(Vector3 value) =>
            !(float.IsNaN(value.x) || float.IsInfinity(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z));

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException("CTLIFE [character-check] " + message);
        }
    }
}
