using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Read-only animation-readiness check for every character prefab the game can spawn (cast looks, registry
    /// archetypes/looks, defaults). A spawnable prefab must have exactly one Animator, on its root, whose Avatar is a
    /// valid Humanoid Avatar: the shared controller only holds Humanoid clips, and an Animator without one leaves the
    /// mesh in its bind pose (the 2026-10-08 T-pose regression). Also reports the cast FBX import settings.
    /// Batch: Unity -batchmode -quit -projectPath client -executeMethod CTLife.EditorTools.CastValidation.ValidateBatch
    ///        [-ctlife-castReport /abs/path/report.json]   exit 0 = no problems.
    /// </summary>
    public static class CastValidation
    {
        private const string RegistryPath = "Assets/Resources/Characters/CharacterRegistry.asset";

        public static readonly string[] PoseStates = { "loco_idle", "loco_walk", "act_sit" };
        /// <summary>A T/bind pose spans ~0.9 of body height across the arms; any real idle/walk/sit pose stays below ~0.5.</summary>
        public const float TPoseSpanRatio = 0.7f;

        /// <summary>
        /// Samples the given controller states on an instance and returns, per state, the lateral (left-right) extent of
        /// the skinned LOD0 surface divided by its height. Uses the shipped controller and the model's own Avatar.
        /// </summary>
        public static Dictionary<string, float> PoseSpans(GameObject prefab, RuntimeAnimatorController controller, IEnumerable<string> states)
        {
            var result = new Dictionary<string, float>();
            var go = UnityEngine.Object.Instantiate(prefab);
            var baked = new Mesh();
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var a = go.GetComponent<Animator>();
                if (a == null || controller == null) return result;
                a.runtimeAnimatorController = controller;
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var body = go.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0).FirstOrDefault();
                if (body == null) return result;
                foreach (var state in states)
                {
                    int hash = Animator.StringToHash(state);
                    a.Rebind();
                    if (!a.HasState(0, hash)) continue;
                    a.Play(hash, 0, 0.3f);
                    a.Update(0f);
                    a.Update(0.05f);
                    body.BakeMesh(baked, true);
                    var m = body.transform.localToWorldMatrix;
                    var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                    foreach (var v in baked.vertices) { var w = m.MultiplyPoint3x4(v); min = Vector3.Min(min, w); max = Vector3.Max(max, w); }
                    // BakeMesh(useScale) already applies the renderer scale; use positions relative to the renderer pivot.
                    var size = max - min;
                    // Lateral (x: the instance faces +z) over height: a T/bind pose spreads the arms sideways, while a seated
                    // pose can be long front-to-back (knees and feet forward on a low seat) without being wide.
                    result[state] = size.y > 0.01f ? size.x / size.y : float.NaN;
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(baked); }
            return result;
        }

        /// <summary>Problems for one prefab; empty = animatable.</summary>
        public static List<string> Check(GameObject prefab, JObject detail = null) => Check(prefab, null, detail);

        public static List<string> Check(GameObject prefab, RuntimeAnimatorController controller, JObject detail)
        {
            var problems = new List<string>();
            if (prefab == null) { problems.Add("missing prefab"); return problems; }
            var animators = prefab.GetComponentsInChildren<Animator>(true);
            detail?.Add("animators", animators.Length);
            if (animators.Length != 1) problems.Add($"{animators.Length} Animators (need exactly 1)");
            var root = prefab.GetComponent<Animator>();
            if (root == null) problems.Add("no Animator on the prefab root");
            foreach (var a in animators)
            {
                var av = a.avatar;
                detail?.Add("avatar_" + a.name, av == null ? "none" : $"{av.name} valid={av.isValid} human={av.isHuman}");
                if (av == null) problems.Add($"Animator on '{a.name}' has no Avatar");
                else if (!av.isValid || !av.isHuman) problems.Add($"Avatar '{av.name}' valid={av.isValid} human={av.isHuman}");
            }
            if (root != null && root.avatar != null && root.avatar.isValid)
            {
                // The source model must own the Avatar (an Avatar added by hand to an optimized model cannot bind).
                var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(prefab);
                var srcAnimator = src != null ? src.GetComponent<Animator>() : null;
                if (srcAnimator == null) problems.Add("the source model has no Animator of its own");
                if (controller != null)
                {
                    var spans = PoseSpans(prefab, controller, PoseStates);
                    var poses = new JObject();
                    foreach (var s in spans) poses[s.Key] = Math.Round(s.Value, 3);
                    detail?.Add("pose_span_ratio", poses);
                    foreach (var s in spans)
                        if (float.IsNaN(s.Value) || s.Value > TPoseSpanRatio) problems.Add($"{s.Key} samples a T/bind pose (span/height {s.Value:F2})");
                    if (spans.Count == 0) problems.Add("no pose could be sampled");
                }
            }
            return problems;
        }

        public static JObject Report(out int problemCount)
        {
            problemCount = 0;
            var report = new JObject();
            var prefabs = new JObject();
            report["prefabs"] = prefabs;
            var seen = new HashSet<string>();
            var reg = AssetDatabase.LoadAssetAtPath<CharacterRegistry>(RegistryPath);
            var shared = reg != null ? reg.controller : null;
            void Add(string label, GameObject prefab, RuntimeAnimatorController ctrl, ref int count)
            {
                if (prefab == null) return;
                var path = AssetDatabase.GetAssetPath(prefab);
                if (!seen.Add(path)) return;
                var detail = new JObject { ["label"] = label, ["controller"] = ctrl != null ? ctrl.name : null };
                var p = Check(prefab, ctrl, detail);
                detail["problems"] = new JArray(p);
                prefabs[path] = detail;
                count += p.Count > 0 ? 1 : 0;
            }
            // Registry entries first (they carry the controller the game will actually use), then any other cast prefab.
            if (reg != null)
            {
                Add("defaultPlayer", reg.defaultPlayer, shared, ref problemCount);
                Add("defaultNpc", reg.defaultNpc, shared, ref problemCount);
                foreach (var e in reg.archetypes.Concat(reg.looks))
                {
                    if (e == null) continue;
                    var ctrl = e.controllerOverride != null ? e.controllerOverride : shared;
                    Add("entry:" + e.key, e.prefab, ctrl, ref problemCount);
                    Add("preview:" + e.key, e.previewPrefab, ctrl, ref problemCount);
                }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { CastImport.Root + "/prefabs" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                var look = prefab != null ? prefab.name.Replace("_Hero", "") : null;
                Add("cast", prefab, (look != null ? CastImport.FamilyController(look) : null) ?? shared, ref problemCount);
            }
            var models = new JObject();
            report["cast_models"] = models;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { CastImport.Root + "/looks" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                models[path] = new JObject
                {
                    ["animationType"] = mi?.animationType.ToString(),
                    ["avatarSetup"] = mi?.avatarSetup.ToString(),
                    ["sourceAvatar"] = mi?.sourceAvatar != null ? AssetDatabase.GetAssetPath(mi.sourceAvatar) : null,
                    ["optimizeGameObjects"] = mi?.optimizeGameObjects,
                    ["avatar"] = avatar == null ? "none" : $"valid={avatar.isValid} human={avatar.isHuman}",
                    ["modelRootAnimator"] = model != null && model.GetComponent<Animator>() != null,
                };
            }
            // Every clip the shared controller (and the registry's overrides) plays must be Humanoid motion: a clip
            // whose source FBX lost its Avatar imports as generic curves on foreign bone names and animates nothing.
            var clips = new JObject();
            report["controller_clips"] = clips;
            int badClips = 0;
            var controllers = new List<RuntimeAnimatorController>();
            if (shared != null) controllers.Add(shared);
            if (reg != null)
                foreach (var e in reg.archetypes.Concat(reg.looks))
                    if (e?.controllerOverride != null && !controllers.Contains(e.controllerOverride)) controllers.Add(e.controllerOverride);
            foreach (var ctrl in controllers)
                foreach (var clip in ctrl.animationClips.Distinct())
                {
                    if (clip == null) continue;
                    var path = AssetDatabase.GetAssetPath(clip);
                    var key = path + "#" + clip.name;
                    if (clips.ContainsKey(key)) continue;
                    var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                    var srcAvatar = mi != null ? AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault() : null;
                    bool ok = clip.humanMotion && clip.length > 0f;
                    if (!ok) badClips++;
                    clips[key] = new JObject
                    {
                        ["humanMotion"] = clip.humanMotion, ["length"] = clip.length,
                        ["avatarSetup"] = mi?.avatarSetup.ToString(),
                        ["sourceAvatar"] = mi?.sourceAvatar != null ? AssetDatabase.GetAssetPath(mi.sourceAvatar) : null,
                        ["fbxAvatar"] = srcAvatar == null ? (mi == null ? "n/a (.anim)" : "none") : $"valid={srcAvatar.isValid} human={srcAvatar.isHuman}",
                    };
                }
            report["bad_clips"] = badClips;
            problemCount += badClips;
            report["problem_prefabs"] = problemCount - badClips;
            return report;
        }

        /// <summary>One line per failing prefab/clip, for exceptions and build logs.</summary>
        public static string Summary(JObject report)
        {
            var lines = new List<string>();
            foreach (var p in (JObject)report["prefabs"])
                if (((JArray)p.Value["problems"]).Count > 0) lines.Add($"{p.Key}: {string.Join("; ", p.Value["problems"])}");
            foreach (var c in (JObject)report["controller_clips"])
                if (!(bool)c.Value["humanMotion"]) lines.Add($"{c.Key}: not Humanoid motion");
            return string.Join("\n", lines);
        }

        /// <summary>Build gate: throws when any spawnable character cannot animate.</summary>
        public static void RequireAnimatable()
        {
            var report = Report(out int problems);
            if (problems > 0) throw new InvalidOperationException($"CTLIFE [cast-validate] {problems} characters cannot animate:\n" + Summary(report));
            Debug.Log("CTLIFE [cast-validate] every spawnable character has a valid Humanoid Avatar and samples real poses");
        }

        public static void ValidateBatch()
        {
            int code = 0;
            try
            {
                var report = Report(out int problems);
                var outPath = LaunchArg("-ctlife-castReport");
                if (!string.IsNullOrEmpty(outPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                    File.WriteAllText(outPath, report.ToString());
                }
                Debug.Log($"CTLIFE [cast-validate] {problems} prefabs with problems; report={outPath ?? "(log only)"}");
                if (problems > 0)
                {
                    foreach (var p in (JObject)report["prefabs"])
                        if (((JArray)p.Value["problems"]).Count > 0)
                            Debug.LogWarning($"CTLIFE [cast-validate] {p.Key}: {string.Join("; ", p.Value["problems"])}");
                    code = 1;
                }
            }
            catch (Exception e) { Debug.LogException(e); code = 2; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        private static string LaunchArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
