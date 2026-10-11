// Diagnostic imports only. Production Rain files, registry and controller are never changed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CTLife.World;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    public static class RainWeightComparisonProbe
    {
        private const string Original = "Assets/Art/characters/authored/Rain/";
        private const string Candidate = "Assets/Art/characters/authored/RainWeightDiagnostic/";
        public static void Capture()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string source = Path.Combine(repo, "art/character-candidates/rain-weight-correction/exports");
            string output = Path.Combine(repo, "art/character-candidates/locomotion-quality-20261006/native-rain-weights");
            var args = Environment.GetCommandLineArgs();
            int arg = Array.IndexOf(args, "-ctRainWeightSource");
            if (arg >= 0 && arg + 1 < args.Length) source = Path.GetFullPath(args[arg + 1]);
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(Candidate);
            string[] tiers = { "Creator", "LOD1", "LOD2" };
            var registry = AssetDatabase.LoadAssetAtPath<CharacterRegistry>("Assets/Resources/Characters/CharacterRegistry.asset");
            var entry = registry.looks.Single(e => e.key.Split('|').Contains("head_rain"));
            var controller = entry.controllerOverride != null ? entry.controllerOverride : registry.controller;
            string ownPath = Original + "RainAdult_Creator.fbx";
            var ownAvatar = AssetDatabase.LoadAllAssetsAtPath(ownPath).OfType<Avatar>().Single(a => a.isHuman && a.isValid);
            var originalImporter = (ModelImporter)AssetImporter.GetAtPath(ownPath);
            var protectedPaths = tiers.Select(t => Original + "RainAdult_" + t + ".fbx")
                .Concat(new[] { "Assets/Resources/Characters/CharacterRegistry.asset", "Assets/Resources/Characters/AvatarController.controller", AssetDatabase.GetAssetPath(controller), "Assets/Art/animations/loco_walk.fbx", "Assets/Art/animations/loco_run.fbx" })
                .Distinct().ToDictionary(p => p, Hash);
            var comparisons = new List<object>();
            foreach (string tier in tiers)
            {
                string file = "RainAdult_" + tier + ".fbx", dst = Candidate + file;
                if (!File.Exists(Path.Combine(source, file))) throw new FileNotFoundException("Missing frozen candidate " + file);
                File.Copy(Path.Combine(source, file), dst, true);
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var mi = (ModelImporter)AssetImporter.GetAtPath(dst);
                mi.globalScale = 1; mi.useFileScale = true; mi.bakeAxisConversion = true;
                mi.importCameras = false; mi.importLights = false; mi.importAnimation = false; mi.importBlendShapes = true;
                mi.importNormals = ModelImporterNormals.Import; mi.importTangents = ModelImporterTangents.CalculateMikk;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                mi.sourceAvatar = ownAvatar; // Same Rain skeleton/own Avatar. Never the retired legacy Avatar.
                mi.humanDescription = originalImporter.humanDescription;
                mi.optimizeGameObjects = false; mi.skinWeights = ModelImporterSkinWeights.Standard;
                mi.meshCompression = ModelImporterMeshCompression.Off; mi.isReadable = true;
                mi.SaveAndReimport();
                var before = AssetDatabase.LoadAssetAtPath<GameObject>(Original + file);
                var after = AssetDatabase.LoadAssetAtPath<GameObject>(dst);
                if (before == null || after == null) throw new InvalidDataException("Missing imported tier " + tier);
                comparisons.Add(CompareRest(tier, before, after));
                foreach (string state in new[] { "loco_walk", "loco_run" })
                {
                    Sample(output, tier, "before", state, before, ownAvatar, controller);
                    Sample(output, tier, "after", state, after, ownAvatar, controller);
                }
            }
            foreach (var pair in protectedPaths) if (Hash(pair.Key) != pair.Value)
                throw new InvalidOperationException("Diagnostic changed production asset " + pair.Key);
            File.WriteAllText(Path.Combine(output, "comparison.json"), JsonConvert.SerializeObject(new {
                source, candidateAssets = Candidate, ownAvatar = ownPath,
                productionHashesUnchanged = protectedPaths, comparisons,
                method = "Same own Rain Avatar, shared walk/run source and actual speed. Four equal phases on Creator/LOD1/LOD2. No cadence or FootIK change; no registry/prefab/controller writes."
            }, Formatting.Indented));
            Debug.Log("CTLIFE [rain-weight-probe] PASS: three rest/bind/geometry comparisons; 48 native posed meshes; production hashes unchanged.");
        }
        private static object CompareRest(string tier, GameObject before, GameObject after)
        {
            var a = before.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            var b = after.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            Mesh ma = a.sharedMesh, mb = b.sharedMesh;
            if (ma.vertexCount != mb.vertexCount || ma.triangles.Length != mb.triangles.Length)
                throw new InvalidDataException("Weight-only candidate changed imported geometry counts for " + tier);
            // Geometry can be reindexed by FBX/Unity optimization; compare rest vertex/UV multisets.
            string Key(Vector3 v, Vector2 uv) => string.Join(",", new[] { v.x, v.y, v.z, uv.x, uv.y }.Select(x => Mathf.RoundToInt(x * 100000f)));
            string[] Shape(Mesh m) { var uv = m.uv; return m.vertices.Select((v, i) => Key(v, uv[i])).OrderBy(x => x, StringComparer.Ordinal).ToArray(); }
            if (!Shape(ma).SequenceEqual(Shape(mb))) throw new InvalidDataException("Weight-only candidate changed rest positions/UVs for " + tier);
            var binds = a.bones.Select((bone, i) => new { bone.name, matrix = ma.bindposes[i] }).ToDictionary(x => x.name, x => x.matrix);
            float maxBindDelta = 0;
            for (int i = 0; i < b.bones.Length; i++)
            {
                if (!binds.TryGetValue(b.bones[i].name, out var old)) throw new InvalidDataException("New bind bone " + b.bones[i].name);
                for (int j = 0; j < 16; j++) maxBindDelta = Mathf.Max(maxBindDelta, Mathf.Abs(old[j] - mb.bindposes[i][j]));
            }
            if (maxBindDelta > .00002f) throw new InvalidDataException("Weight-only candidate changed bind matrices for " + tier + ": " + maxBindDelta);
            float LowHeadWeight(SkinnedMeshRenderer skin)
            {
                int head = Array.FindIndex(skin.bones, x => x.name == "Head");
                var points = skin.sharedMesh.vertices; var weights = skin.sharedMesh.boneWeights; float maximum = 0;
                for (int i = 0; i < points.Length; i++)
                {
                    if (skin.transform.TransformPoint(points[i]).y > .65f) continue;
                    var w = weights[i];
                    float sum = (w.boneIndex0 == head ? w.weight0 : 0) + (w.boneIndex1 == head ? w.weight1 : 0) +
                        (w.boneIndex2 == head ? w.weight2 : 0) + (w.boneIndex3 == head ? w.weight3 : 0);
                    maximum = Mathf.Max(maximum, sum);
                }
                return maximum;
            }
            float lowBefore = LowHeadWeight(a), lowAfter = LowHeadWeight(b);
            if (lowAfter > .00001f) throw new InvalidDataException("Candidate still has Head weight below knee region: " + lowAfter);
            return new { tier, vertices = ma.vertexCount, triangles = ma.triangles.Length / 3, maxBindDelta,
                maximumHeadWeightBelow65cmBefore = lowBefore, maximumHeadWeightBelow65cmAfter = lowAfter };
        }
        private static void Sample(string output, string tier, string variant, string stateName, GameObject asset,
            Avatar avatar, RuntimeAnimatorController controller)
        {
            var scene = EditorSceneManager.NewPreviewScene(); GameObject root = null; var baked = new Mesh();
            string stem = "rain-" + tier + "-" + variant + "-" + stateName;
            try
            {
                root = Object.Instantiate(asset); SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = root.GetComponent<Animator>();
                if (animator == null) animator = root.AddComponent<Animator>();
                if (animator == null) throw new InvalidDataException("Could not create diagnostic Animator for " + tier);
                animator.avatar = avatar; animator.runtimeAnimatorController = controller;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion = false; animator.enabled = true;
                animator.Rebind(); if (!animator.isHuman) throw new InvalidDataException("Rain own Avatar failed on tier " + tier);
                var skin = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
                var frames = new List<object>();
                var exporter = typeof(LocomotionQualityProbe).GetMethod("WriteObj", BindingFlags.NonPublic | BindingFlags.Static);
                foreach (int phase in new[] { 0, 15, 30, 45 })
                {
                    animator.SetFloat("speed", stateName == "loco_walk" ? 1.75f : 4.5f);
                    animator.Play(stateName, 0, phase / 60f); animator.Update(0f); skin.BakeMesh(baked);
                    Vector3[] vertices = baked.vertices.Select(v => root.transform.InverseTransformPoint(skin.transform.TransformPoint(v))).ToArray();
                    var names = new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes };
                    frames.Add(new { phase = phase / 60f, bodyMinY = vertices.Min(v => v.y), bodyMaxY = vertices.Max(v => v.y),
                        joints = names.ToDictionary(n => n.ToString(), n => { Vector3 p = root.transform.InverseTransformPoint(animator.GetBoneTransform(n).position); return new[] { p.x, p.y, p.z }; }) });
                    exporter.Invoke(null, new object[] { Path.Combine(output, stem + "-phase" + phase.ToString("D2") + ".obj.gz"), vertices,
                        baked.normals.Select(n => root.transform.InverseTransformDirection(skin.transform.TransformDirection(n))).ToArray(), skin.sharedMesh.uv, skin.sharedMesh.triangles });
                }
                File.WriteAllText(Path.Combine(output, stem + ".json"), JsonConvert.SerializeObject(new { tier, variant, stateName, source = AssetDatabase.GetAssetPath(asset), frames }, Formatting.Indented));
            }
            finally { Object.DestroyImmediate(baked); if (root != null) Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        }
        private static string Hash(string path)
        {
            using var hash = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
