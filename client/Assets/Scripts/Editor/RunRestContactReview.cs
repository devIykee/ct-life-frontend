// Staged outside Assets. Candidate-only import and native comparison; no production promotion.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    public static class RunRestContactReview
    {
        private const string Stage = "Assets/Art/characters/authored/MotionReview20261006/RestContactMatrix/";
        private const string Rest = "Assets/Art/characters/authored/MotionReview20261006/SourceLibraryRest/UAL_SourceRest.fbx";
        private const string SourceJogHash = "4cbfd571a7ecef633491cf98fb6514e83433fcfde5b0fbd7cfaa61cc8c45896a";
        private const string RestHash = "c7982fc0f414181a6ae65c28676ee6c62802bd635ea85b743d634273526d1de7";
        private const int Phases = 120;
        private sealed class Skin
        {
            public SkinnedMeshRenderer renderer;
            public Mesh baked;
            public int[] left, right;
            public int[][] patches;
            public readonly List<Vector3> vertices = new List<Vector3>();
        }
        private static readonly string[] PatchNames = { "Foot.L", "Toes.L", "Foot.R", "Toes.R" };

        public static void Capture()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string outside = Path.Combine(repo, "art/character-candidates/motion-review-20261006");
            string output = Path.Combine(outside, "native-rest-contact-matrix");
            Directory.CreateDirectory(output);
            var roots = new[] { "Assets/Resources/Characters", "Assets/Art/animations", "Assets/Art/characters/authored" };
            var frozen = roots.SelectMany(r => Directory.GetFiles(r, "*", SearchOption.AllDirectories))
                .Where(p => !p.Replace('\\', '/').StartsWith(Stage, StringComparison.Ordinal) && p != Stage.TrimEnd('/') + ".meta")
                .ToDictionary(p => p, Hash);
            var receipt = new JObject { ["state"] = "sampling", ["scope"] = "Rest-source Jog only; Original/Feet Y projection x state Foot IK. No global translation, no runtime promotion.",
                ["limitations"] = "Native Animator.Update in preview scenes, continuous 1x cycle after 2 warm-up cycles. No NavMesh, physical ground collision, transitions, sound or phone acceptance." };
            try
            {
                Require(Hash(Rest) == RestHash, "Rest reference source changed.");
                string sourceFile = Path.Combine(outside, "UAL_SourceJog.fbx");
                Require(Hash(sourceFile) == SourceJogHash, "Source Jog changed.");
                var restImporter = (ModelImporter)AssetImporter.GetAtPath(Rest);
                var sourceAvatar = AssetDatabase.LoadAllAssetsAtPath(Rest).OfType<Avatar>().Single();
                Require(sourceAvatar.isHuman && sourceAvatar.isValid, "Rest source Avatar invalid.");
                receipt["source_avatar"] = Rest;
                receipt["source_human_mapping"] = Mapping(restImporter);
                var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
                var results = new JArray(); receipt["cases"] = results;
                var imports = new JArray(); receipt["import_variants"] = imports;
                foreach (bool feet in new[] { false, true })
                {
                    string projection = feet ? "feet-y" : "original-y";
                    string path = Stage + "UAL_Jog_" + projection + ".fbx";
                    Directory.CreateDirectory(Stage);
                    if (File.Exists(path)) Require(Hash(path) == SourceJogHash, "Different candidate source exists: " + path);
                    else File.Copy(sourceFile, path);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                    mi.globalScale = 1; mi.useFileScale = true; mi.bakeAxisConversion = true;
                    mi.importCameras = false; mi.importLights = false; mi.materialImportMode = ModelImporterMaterialImportMode.None;
                    mi.animationType = ModelImporterAnimationType.Generic; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    mi.sourceAvatar = null; mi.humanDescription = new HumanDescription(); mi.importAnimation = false;
                    mi.optimizeGameObjects = false; mi.SaveAndReimport();
                    mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    mi.sourceAvatar = sourceAvatar; mi.humanDescription = restImporter.humanDescription;
                    mi.animationCompression = ModelImporterAnimationCompression.Off; mi.importAnimation = true; mi.SaveAndReimport();
                    var clips = mi.defaultClipAnimations;
                    foreach (var c in clips)
                    {
                        c.loopTime = true; c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true;
                        c.keepOriginalOrientation = true; c.rotationOffset = 0; c.keepOriginalPositionXZ = true;
                        c.keepOriginalPositionY = !feet; c.heightFromFeet = feet; c.heightOffset = 0;
                    }
                    mi.clipAnimations = clips; mi.SaveAndReimport();
                    var actual = mi.clipAnimations.Single();
                    Require(actual.lockRootHeightY && actual.keepOriginalPositionY == !feet && actual.heightFromFeet == feet &&
                        actual.heightOffset == 0 && actual.keepOriginalOrientation && actual.rotationOffset == 0,
                        "Candidate import settings were overridden.");
                    imports.Add(new JObject { ["projection"] = projection, ["path"] = path,
                        ["source_sha256"] = Hash(path), ["source_avatar"] = AssetDatabase.GetAssetPath(mi.sourceAvatar),
                        ["bake_y_into_pose"] = actual.lockRootHeightY, ["keep_original_y"] = actual.keepOriginalPositionY,
                        ["height_from_feet"] = actual.heightFromFeet, ["height_offset"] = actual.heightOffset,
                        ["original_orientation"] = actual.keepOriginalOrientation, ["rotation_offset"] = actual.rotationOffset });
                    var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
                    Require(clip.humanMotion && clip.isLooping && Mathf.Abs(clip.length - 28f / 30f) < .0001f, "Unexpected candidate motion.");
                    foreach (bool ik in new[] { false, true })
                    {
                        string variant = projection + (ik ? "-footik" : "-noik");
                        // Source skeleton control exposes whether target-avatar differences originate in retargeting.
                        results.Add(CaptureRig("source", AssetDatabase.LoadAssetAtPath<GameObject>(Rest), clip, variant, ik, output));
                        foreach (string character in new[] { "snow", "rain" })
                        {
                            var entry = registry.looks.Single(e => e.key.Split('|').Contains("head_" + character));
                            results.Add(CaptureRig(character, entry.prefab, clip, variant, ik, output));
                        }
                    }
                }
                receipt["state"] = "captured-not-accepted";
            }
            catch (Exception e) { receipt["state"] = "failed"; receipt["failure"] = e.ToString(); throw; }
            finally
            {
                var drift = frozen.Where(p => !File.Exists(p.Key) || Hash(p.Key) != p.Value).Select(p => p.Key).ToArray();
                receipt["protected_drift"] = new JArray(drift); receipt["protected_sha256"] = JObject.FromObject(frozen);
                File.WriteAllText(Path.Combine(output, "receipt.json"), receipt.ToString());
                Require(drift.Length == 0, "Protected production/reference file drift: " + string.Join(",", drift));
            }
        }

        private static JObject CaptureRig(string character, GameObject prefab, AnimationClip clip, string variant, bool ik, string output)
        {
            var scene = EditorSceneManager.NewPreviewScene(); GameObject root = null;
            var skins = new List<Skin>(); AnimatorController controller = null; AnimatorStateMachine machine = null; AnimatorState state = null;
            try
            {
                root = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = root.GetComponentsInChildren<Animator>(true).Single(); animator.enabled = false;
                Require(animator.avatar != null && animator.avatar.isHuman && animator.avatar.isValid, "Invalid own Avatar.");
                string avatarPath = AssetDatabase.GetAssetPath(animator.avatar);
                var map = AssetImporter.GetAtPath(avatarPath) as ModelImporter;
                var joints = Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>()
                    .Where(b => b != HumanBodyBones.LastBone && animator.GetBoneTransform(b) != null).ToArray();
                Vector3 Point(HumanBodyBones b) => root.transform.InverseTransformPoint(animator.GetBoneTransform(b).position);
                var restJoints = JObject.FromObject(joints.ToDictionary(b => b.ToString(), b => V(Point(b))));
                var restSurfaces = new JArray();
                if (character != "source") foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var weights = renderer.sharedMesh.boneWeights;
                    var names = renderer.bones.Select(b => b.name).ToArray();
                    int[] Select(string[] wanted, float minimum) => Enumerable.Range(0, weights.Length).Where(i => {
                        var w = weights[i]; float W(int b, float v) => v > 0 && wanted.Contains(names[b]) ? v : 0;
                        return W(w.boneIndex0, w.weight0) + W(w.boneIndex1, w.weight1) + W(w.boneIndex2, w.weight2) + W(w.boneIndex3, w.weight3) >= minimum;
                    }).ToArray();
                    var skin = new Skin { renderer = renderer, baked = new Mesh(), left = Select(new[] { "Foot.L", "Toes.L" }, .5f),
                        right = Select(new[] { "Foot.R", "Toes.R" }, .5f), patches = new int[4][] };
                    skins.Add(skin);
                    Require(skin.left.Length > 0 && skin.right.Length > 0, "Missing target footwear weights.");
                    renderer.BakeMesh(skin.baked); skin.baked.GetVertices(skin.vertices);
                    var matrix = root.transform.worldToLocalMatrix * renderer.localToWorldMatrix;
                    var vertices = skin.vertices.Select(v => matrix.MultiplyPoint3x4(v)).ToArray();
                    for (int p = 0; p < PatchNames.Length; p++)
                    {
                        var ids = Select(new[] { PatchNames[p] }, .75f); Require(ids.Length > 0, "Missing fixed patch " + PatchNames[p]);
                        float low = ids.Min(i => vertices[i].y);
                        skin.patches[p] = ids.Where(i => vertices[i].y <= low + .002f).ToArray();
                    }
                    restSurfaces.Add(new JObject { ["skin"] = renderer.name, ["vertices"] = renderer.sharedMesh.vertexCount,
                        ["left_sole_m"] = skin.left.Min(i => vertices[i].y), ["right_sole_m"] = skin.right.Min(i => vertices[i].y),
                        ["left_ankle_to_visual_sole_m"] = Point(HumanBodyBones.LeftFoot).y - skin.left.Min(i => vertices[i].y),
                        ["right_ankle_to_visual_sole_m"] = Point(HumanBodyBones.RightFoot).y - skin.right.Min(i => vertices[i].y),
                        ["patches"] = new JArray(PatchNames.Select((p, i) => new JObject { ["name"] = p, ["fixed_vertex_indices"] = new JArray(skin.patches[i]) })) });
                }
                controller = new AnimatorController { name = "UNSAVED_REST_CONTACT_" + variant };
                machine = new AnimatorStateMachine(); state = machine.AddState("loco_run");
                state.motion = clip; state.speed = 1; state.iKOnFeet = ik; machine.defaultState = state;
                controller.layers = new[] { new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1, stateMachine = machine } };
                animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false; animator.speed = 1; animator.enabled = true; animator.Rebind();
                animator.Play("loco_run", 0, 0); animator.Update(0);
                float dt = clip.length / Phases;
                for (int i = 0; i < 2 * Phases; i++) animator.Update(dt);
                var frames = new JArray();
                float referenceStart = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                Require(Mathf.Abs(referenceStart - 2) < .001f, "Unexpected warm-up advancement.");
                for (int i = 0; i <= Phases; i++)
                {
                    Require(root.transform.position.sqrMagnitude < 1e-10f, "Root moved despite disabled root motion.");
                    var info = animator.GetCurrentAnimatorStateInfo(0);
                    Require(Mathf.Abs(info.normalizedTime - referenceStart - i / (float)Phases) < .001f, "Unexpected native phase advancement.");
                    var surfaces = new JArray();
                    foreach (var skin in skins)
                    {
                        skin.renderer.BakeMesh(skin.baked); skin.baked.GetVertices(skin.vertices);
                        var matrix = root.transform.worldToLocalMatrix * skin.renderer.localToWorldMatrix;
                        var v = skin.vertices.Select(p => matrix.MultiplyPoint3x4(p)).ToArray();
                        Require(v.All(p => !float.IsNaN(p.sqrMagnitude) && !float.IsInfinity(p.sqrMagnitude)), "Nonfinite skin geometry.");
                        var patches = new JObject();
                        for (int p = 0; p < PatchNames.Length; p++) patches[PatchNames[p]] = new JObject {
                            ["mean"] = V(skin.patches[p].Aggregate(Vector3.zero, (a, index) => a + v[index]) / skin.patches[p].Length),
                            ["min_y"] = skin.patches[p].Min(index => v[index].y) };
                        surfaces.Add(new JObject { ["skin"] = skin.renderer.name, ["left_min_y"] = skin.left.Min(index => v[index].y),
                            ["right_min_y"] = skin.right.Min(index => v[index].y), ["patches"] = patches });
                        if (i < Phases && i % 30 == 0)
                        {
                            var writeObj = typeof(LocomotionQualityProbe).GetMethod("WriteObj", BindingFlags.Static | BindingFlags.NonPublic);
                            Require(writeObj != null, "Native OBJ writer missing.");
                            writeObj.Invoke(null, new object[] { Path.Combine(output, character + "-" + variant + "-" + skin.renderer.name + "-phase" + i.ToString("000") + ".obj.gz"), v,
                                skin.baked.normals.Select(n => root.transform.InverseTransformDirection(skin.renderer.transform.TransformDirection(n))).ToArray(),
                                skin.renderer.sharedMesh.uv, skin.renderer.sharedMesh.triangles });
                        }
                    }
                    frames.Add(new JObject { ["phase"] = i / (float)Phases, ["native_normalized_time"] = info.normalizedTime,
                        ["joints"] = JObject.FromObject(joints.ToDictionary(b => b.ToString(), b => V(Point(b)))),
                        ["surfaces"] = surfaces, ["native_delta_position"] = V(animator.deltaPosition) });
                    if (i < Phases) animator.Update(dt);
                }
                string filename = character + "-" + variant + ".json";
                var result = new JObject { ["character"] = character, ["variant"] = variant, ["source"] = AssetDatabase.GetAssetPath(clip),
                    ["clip_seconds"] = clip.length, ["rate"] = 1, ["foot_ik"] = ik, ["root_motion"] = false,
                    ["prefab"] = AssetDatabase.GetAssetPath(prefab), ["own_avatar"] = avatarPath,
                    ["human_scale"] = animator.humanScale, ["left_feet_bottom_height"] = animator.leftFeetBottomHeight,
                    ["right_feet_bottom_height"] = animator.rightFeetBottomHeight, ["target_human_mapping"] = Mapping(map),
                    ["rest_joints"] = restJoints, ["rest_surfaces"] = restSurfaces, ["frames"] = frames,
                    ["scope"] = "Native continuous Animator.Update at 1x, after two warm-up cycles. All target world skins, source control has joint-only geometry. No root/Y offset." };
                File.WriteAllText(Path.Combine(output, filename), result.ToString());
                return new JObject { ["character"] = character, ["variant"] = variant, ["file"] = filename, ["frames"] = frames.Count, ["skins"] = skins.Count };
            }
            finally
            {
                foreach (var skin in skins) if (skin.baked != null) Object.DestroyImmediate(skin.baked);
                if (root != null) Object.DestroyImmediate(root);
                if (controller != null) Object.DestroyImmediate(controller);
                if (state != null) Object.DestroyImmediate(state);
                if (machine != null) Object.DestroyImmediate(machine);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        private static JArray Mapping(ModelImporter importer) => importer == null ? new JArray() :
            new JArray(importer.humanDescription.human.Select(b => new JObject { ["human"] = b.humanName, ["bone"] = b.boneName }));
        private static JArray V(Vector3 value) => new JArray(value.x, value.y, value.z);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static string Hash(string path)
        {
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
