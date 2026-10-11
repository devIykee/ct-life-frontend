#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CTLife.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Search;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>Play-mode lifetime probe. No asset save, production startup, GC or unused-asset unload.</summary>
    [InitializeOnLoad]
    public static class GeneratedAvatarOwnershipReview
    {
        private const string Folder = "art/character-candidates/motion-review-20261006/assembler-ownership";
        private const string AssemblerPath = "client/Assets/Scripts/World/Avatars/CharacterAssembler.cs";
        private const string OwnerPath = "client/Assets/Scripts/World/Avatars/GeneratedAvatarResources.cs";
        private const string BaselineHash = "98e1cbe1245b268b55ffa35b80d154c8e2c17beea133d6f1c281d46bb95c94fc";
        private const string Prefix = "CTLife.GeneratedAvatarOwnershipReview.v2.";
        private static Receipt _receipt;
        private static FixtureCase _case;
        private static int _caseIndex, _step, _nextFrame;
        private static bool _unexpectedReload;
        private static bool Active => SessionState.GetBool(Prefix + "active", false);
        private static string Phase => SessionState.GetString(Prefix + "phase", "idle");

        [Serializable] private sealed class Receipt
        {
            public string state, unityVersion, sourceSha256, ownerSha256, exception;
            public string mode = "play-mode-deferred-destroy";
            public bool expectedCleanup, passed, enteredPlayMode, sceneRestored;
            public int startFrame, lastFrame, runtimeErrors;
            public List<CaseResult> cases = new List<CaseResult>();
        }
        [Serializable] private sealed class SceneRecord { public string path; public bool loaded, active; }
        [Serializable] private sealed class SceneRecords { public SceneRecord[] items; }
        [Serializable] private sealed class CaseResult
        {
            public string name;
            public bool hasGeneratedAtlas, generatedMeshDestroyed, generatedMaterialDestroyed, generatedAtlasDestroyed;
            public bool allSharedInputsAlive, hiddenBodyKeepsAssets, destroyedEntireAvatar, targetDestroyed, observedInPlayMode;
            public int vertices, triangles, blendShapes, createdFrame, destroyRequestFrame, observedFrame;
        }
        private sealed class FixtureCase
        {
            public readonly List<Object> inputs = new List<Object>();
            public GameObject root, body;
            public Mesh mesh;
            public Material material;
            public Texture2D atlas;
            public CaseResult result;

            public void Release()
            {
                // Run only after observations or failure. Destroy is deferred here, as in the player.
                if (root != null) Object.Destroy(root);
                if (mesh != null) Object.Destroy(mesh);
                if (material != null) Object.Destroy(material);
                if (atlas != null) Object.Destroy(atlas);
                foreach (var item in inputs) if (item != null) Object.Destroy(item);
            }
        }

        static GeneratedAvatarOwnershipReview()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
            _unexpectedReload = Active && Phase == "running";
        }

        [MenuItem("CT Life/Review/Generated Avatar Ownership Baseline (Play mode)")]
        public static void CaptureBaseline() => Begin(false);
        [MenuItem("CT Life/Review/Generated Avatar Ownership Candidate (Play mode)")]
        public static void CaptureCandidate() => Begin(true);

        private static void Begin(bool expectedCleanup)
        {
            Require(!Environment.GetCommandLineArgs().Any(a => a.Equals("-quit", StringComparison.OrdinalIgnoreCase)),
                "Omit -quit: the asynchronous Play-mode probe exits batch mode itself after its receipt and scene restoration.");
            Require(!Active && !EditorApplication.isPlayingOrWillChangePlaymode, "Probe requires idle Edit mode.");
            Require(!EditorApplication.isPaused, "Probe does not change the Editor pause setting; unpause first.");
            RequireCleanScenes();
            string repo = Directory.GetParent(Application.dataPath).Parent.FullName;
            string hash = Hash(Path.Combine(repo, AssemblerPath));
            if (!expectedCleanup) Require(hash == BaselineHash, "Baseline assembler source has changed.");
            else
            {
                Require(hash == Hash(Path.Combine(repo, Folder, AssemblerPath)), "Candidate assembler differs from staged source.");
                Require(Hash(Path.Combine(repo, OwnerPath)) == Hash(Path.Combine(repo, Folder, OwnerPath)),
                    "Candidate owner differs from staged source.");
            }
            _receipt = new Receipt { state = "starting", expectedCleanup = expectedCleanup, unityVersion = Application.unityVersion,
                sourceSha256 = hash, ownerSha256 = expectedCleanup ? Hash(Path.Combine(repo, OwnerPath)) : "absent" };
            string output = Path.Combine(repo, Folder, expectedCleanup ? "native-playmode-candidate.json" : "native-playmode-baseline.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            SessionState.SetString(Prefix + "output", output);
            SessionState.SetString(Prefix + "scenes", JsonUtility.ToJson(new SceneRecords
            {
                items = EditorSceneManager.GetSceneManagerSetup().Select(s => new SceneRecord
                    { path = s.path, loaded = s.isLoaded, active = s.isActive }).ToArray()
            }));
            SessionState.SetBool(Prefix + "sceneReplaced", false);
            SessionState.SetBool(Prefix + "runtimeSettingsCaptured", false);
            SessionState.SetBool(Prefix + "exitEditor", Application.isBatchMode);
            SessionState.SetInt(Prefix + "runtimeErrors", 0);
            SessionState.SetString(Prefix + "firstError", "");
            SessionState.SetBool(Prefix + "active", true);
            _unexpectedReload = false;
            StoreReceipt();
            SetPhase("startup", 120);
            Debug.Log("[avatar-ownership] Awaiting imports, then actual Play mode and 25 deferred-destruction cases.");
        }

        private static void Tick()
        {
            if (!Active) return;
            try
            {
                if (_receipt == null) _receipt = JsonUtility.FromJson<Receipt>(SessionState.GetString(Prefix + "receipt", "{}"));
                if (Phase == "stopping")
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode) RestoreAndFinish();
                    else if (EditorApplication.timeSinceStartup > Deadline())
                    {
                        _receipt.passed = false;
                        _receipt.exception += "\nTimed out leaving Play mode; scene restoration not completed.";
                        Finish();
                    }
                    return;
                }
                Require(EditorApplication.timeSinceStartup <= Deadline(), "Probe timeout in phase " + Phase + ".");
                Require(!_unexpectedReload, "Unexpected assembly reload during runtime observations; result invalidated.");
                Require(SessionState.GetInt(Prefix + "runtimeErrors", 0) == 0,
                    "Runtime error during probe: " + SessionState.GetString(Prefix + "firstError", ""));
                if (Phase == "startup")
                {
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                    Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Unexpected Play mode transition during startup.");
                    // Same installed Unity 6.3 Search initialization gate as FrontendReview.
                    if (!SearchService.EnumerateDatabases().Any()) return;
                    RequireCleanScenes();
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    SessionState.SetBool(Prefix + "sceneReplaced", true);
                    SetPhase("entering-play", 90);
                    EditorApplication.EnterPlaymode();
                    return;
                }
                if (Phase == "entering-play")
                {
                    if (!Application.isPlaying) return;
                    Require(!EditorApplication.isPaused, "Play mode is paused.");
                    SessionState.SetBool(Prefix + "originalRunInBackground", Application.runInBackground);
                    SessionState.SetBool(Prefix + "runtimeSettingsCaptured", true);
                    Application.runInBackground = true;
                    _receipt.enteredPlayMode = true;
                    _receipt.startFrame = Time.frameCount;
                    _receipt.state = "runtime-running";
                    _caseIndex = 0;
                    _step = 0;
                    _nextFrame = Time.frameCount + 2;
                    StoreReceipt();
                    SetPhase("running", 120);
                    return;
                }
                Require(Phase == "running" && Application.isPlaying, "Play mode ended before observations completed.");
                if (Time.frameCount < _nextFrame) return;
                _receipt.lastFrame = Time.frameCount;
                if (_step == 0)
                {
                    _case = new FixtureCase();
                    PrepareCase(_caseIndex, _case);
                    _step = 1;
                    _nextFrame = Time.frameCount + 2;
                }
                else if (_step == 1)
                {
                    var r = _case.result;
                    r.hiddenBodyKeepsAssets = _case.mesh != null && (!r.hasGeneratedAtlas || (_case.material != null && _case.atlas != null));
                    Require(r.hiddenBodyKeepsAssets, r.name + ": hiding the body released resources.");
                    r.destroyRequestFrame = Time.frameCount;
                    if (r.destroyedEntireAvatar) Object.Destroy(_case.root);
                    else Object.Destroy(_case.body);
                    _step = 2;
                    _nextFrame = Time.frameCount + 3;
                }
                else if (_step == 2)
                {
                    ObserveCase(_case);
                    StoreReceipt();
                    _case.Release();
                    _case = null;
                    _step = 3;
                    _nextFrame = Time.frameCount + 2;
                }
                else
                {
                    _caseIndex++;
                    if (_caseIndex == 25) BeginStop(true, null);
                    else { _step = 0; _nextFrame = Time.frameCount + 1; }
                }
            }
            catch (Exception e) { BeginStop(false, e.ToString()); }
        }

        private static void PrepareCase(int index, FixtureCase c)
        {
            bool twoTextures = index != 0, useOverride = index == 2, unreadable = index == 3;
            bool inactiveParent = index == 4 || (index >= 5 && index % 2 == 0);
            string name = index == 0 ? "one-shared-texture" : index == 1 ? "generated-atlas" :
                index == 2 ? "caller-material-override" : index == 3 ? "unreadable-shared-texture" :
                index == 4 ? "initially-inactive-parent" : "repeat-" + (index - 5);
            c.root = new GameObject("ownership-review-" + name);
            var pelvis = new GameObject("pelvis");
            pelvis.transform.SetParent(c.root.transform, false);
            if (inactiveParent) c.root.SetActive(false);
            var textureA = MakeTexture("source-a", Color.white, false, c.inputs);
            var textureB = twoTextures ? MakeTexture("source-b", Color.green, unreadable, c.inputs) : textureA;
            var materialA = MakeMaterial("source-a", textureA, c.inputs);
            var materialB = twoTextures ? MakeMaterial("source-b", textureB, c.inputs) : materialA;
            var materialOverride = useOverride ? MakeMaterial("caller-override", textureA, c.inputs) : null;
            var parts = new List<SkinnedMeshRenderer>
            {
                MakePart(c.root.transform, pelvis.transform, "part-a", materialA, c.inputs),
                MakePart(c.root.transform, pelvis.transform, "part-b", materialB, c.inputs)
            };
            var body = CharacterAssembler.Merge(c.root.transform, parts, materialOverride, 64);
            c.body = body.gameObject;
            c.mesh = body.sharedMesh;
            bool hasAtlas = twoTextures && !useOverride && !unreadable;
            if (hasAtlas)
            {
                c.material = body.sharedMaterial;
                c.atlas = (c.material.HasProperty("_BaseMap") ? c.material.GetTexture("_BaseMap") : c.material.mainTexture) as Texture2D;
                Require(c.material != materialA && c.material != materialB, name + ": atlas material must be new.");
                Require(c.atlas != null && c.atlas != textureA && c.atlas != textureB, name + ": atlas texture must be new.");
            }
            else Require(body.sharedMaterial == (useOverride ? materialOverride : materialA), name + ": shared material changed.");
            Require(c.mesh.vertexCount == 6 && c.mesh.triangles.Length == 6, name + ": geometry shape changed.");
            Require(c.mesh.blendShapeCount == 2 && c.mesh.bindposes.Length == 1, name + ": blend shapes or skeleton mapping changed.");
            c.result = new CaseResult { name = name, hasGeneratedAtlas = hasAtlas, destroyedEntireAvatar = inactiveParent,
                vertices = c.mesh.vertexCount, triangles = c.mesh.triangles.Length / 3,
                blendShapes = c.mesh.blendShapeCount, createdFrame = Time.frameCount };
            _receipt.cases.Add(c.result);
            c.body.SetActive(false);
        }

        private static void ObserveCase(FixtureCase c)
        {
            var r = c.result;
            r.observedFrame = Time.frameCount;
            r.observedInPlayMode = Application.isPlaying;
            r.targetDestroyed = r.destroyedEntireAvatar ? c.root == null : c.body == null;
            r.generatedMeshDestroyed = c.mesh == null;
            r.generatedMaterialDestroyed = !r.hasGeneratedAtlas || c.material == null;
            r.generatedAtlasDestroyed = !r.hasGeneratedAtlas || c.atlas == null;
            r.allSharedInputsAlive = c.inputs.TrueForAll(item => item != null);
            Require(r.observedInPlayMode && r.observedFrame - r.destroyRequestFrame >= 3, r.name + ": not observed across runtime frames.");
            Require(r.targetDestroyed, r.name + ": deferred avatar destruction has not completed.");
            Require(r.allSharedInputsAlive, r.name + ": shared source/override asset was destroyed.");
            Require(r.generatedMeshDestroyed == _receipt.expectedCleanup, r.name + ": mesh lifetime differs from expectation.");
            if (r.hasGeneratedAtlas)
            {
                Require(r.generatedMaterialDestroyed == _receipt.expectedCleanup, r.name + ": material lifetime differs from expectation.");
                Require(r.generatedAtlasDestroyed == _receipt.expectedCleanup, r.name + ": atlas lifetime differs from expectation.");
            }
        }

        private static void BeginStop(bool passed, string exception)
        {
            if (Phase == "stopping") return;
            _receipt.passed = passed;
            _receipt.exception = exception;
            _receipt.runtimeErrors = SessionState.GetInt(Prefix + "runtimeErrors", 0);
            _receipt.state = passed ? "runtime-passed-awaiting-restoration" : "runtime-failed";
            StoreReceipt();
            SetPhase("stopping", 45);
            if (_case != null && Application.isPlaying) { _case.Release(); _case = null; }
            if (SessionState.GetBool(Prefix + "runtimeSettingsCaptured", false) && Application.isPlaying)
                Application.runInBackground = SessionState.GetBool(Prefix + "originalRunInBackground", false);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
            else RestoreAndFinish();
        }

        private static void RestoreAndFinish()
        {
            try
            {
                if (SessionState.GetBool(Prefix + "sceneReplaced", false))
                {
                    var saved = JsonUtility.FromJson<SceneRecords>(SessionState.GetString(Prefix + "scenes", "{}"))
                        .items.Where(s => !string.IsNullOrEmpty(s.path)).ToArray();
                    if (saved.Length > 0)
                    {
                        if (!saved.Any(s => s.active)) saved.First(s => s.loaded).active = true;
                        EditorSceneManager.RestoreSceneManagerSetup(saved.Select(s => new SceneSetup
                            { path = s.path, isLoaded = s.loaded, isActive = s.active }).ToArray());
                    }
                    else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
                _receipt.sceneRestored = true;
            }
            catch (Exception e) { _receipt.passed = false; _receipt.exception += "\nScene restoration: " + e; }
            Finish();
        }

        private static void Finish()
        {
            _receipt.state = _receipt.passed && _receipt.sceneRestored ? "native-playmode-passed" : "native-playmode-failed";
            _receipt.runtimeErrors = SessionState.GetInt(Prefix + "runtimeErrors", 0);
            StoreReceipt();
            bool exit = SessionState.GetBool(Prefix + "exitEditor", false);
            SessionState.SetBool(Prefix + "active", false);
            SessionState.SetString(Prefix + "phase", "idle");
            Debug.Log("[avatar-ownership] " + _receipt.state + " " + SessionState.GetString(Prefix + "output", ""));
            if (!_receipt.passed) Debug.LogError(_receipt.exception);
            if (exit) EditorApplication.Exit(_receipt.passed && _receipt.sceneRestored ? 0 : 1);
        }

        private static void StoreReceipt()
        {
            string json = JsonUtility.ToJson(_receipt, true);
            SessionState.SetString(Prefix + "receipt", json);
            File.WriteAllText(SessionState.GetString(Prefix + "output", ""), json);
        }
        private static void SetPhase(string phase, double timeout)
        {
            SessionState.SetString(Prefix + "phase", phase);
            SessionState.SetString(Prefix + "deadline", (EditorApplication.timeSinceStartup + timeout).ToString("R", CultureInfo.InvariantCulture));
        }
        private static double Deadline() => double.Parse(SessionState.GetString(Prefix + "deadline", "0"), CultureInfo.InvariantCulture);
        private static void OnLog(string message, string stack, LogType type)
        {
            if (!Active || Phase == "stopping" || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            int count = SessionState.GetInt(Prefix + "runtimeErrors", 0);
            if (count == 0) SessionState.SetString(Prefix + "firstError", message + "\n" + stack);
            SessionState.SetInt(Prefix + "runtimeErrors", count + 1);
        }
        private static void RequireCleanScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                Require(!scene.isDirty && (!string.IsNullOrEmpty(scene.path) || scene.rootCount == 0),
                    "Probe refuses to replace an unsaved scene.");
            }
        }

        private static Texture2D MakeTexture(string name, Color color, bool unreadable, List<Object> inputs)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "ownership-" + name };
            inputs.Add(texture);
            var colors = new Color[16];
            for (int i = 0; i < colors.Length; i++) colors[i] = color;
            texture.SetPixels(colors);
            texture.Apply(false, unreadable);
            return texture;
        }

        private static Material MakeMaterial(string name, Texture2D texture, List<Object> inputs)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Require(shader != null, "URP Lit shader unavailable.");
            var material = new Material(shader) { name = "ownership-" + name };
            inputs.Add(material);
            material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
            return material;
        }

        private static SkinnedMeshRenderer MakePart(Transform root, Transform pelvis, string name,
            Material material, List<Object> inputs)
        {
            var mesh = new Mesh { name = "ownership-" + name };
            inputs.Add(mesh);
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.bindposes = new[] { Matrix4x4.identity };
            var weight = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
            mesh.boneWeights = new[] { weight, weight, weight };
            mesh.AddBlendShapeFrame(name, 100, new[] { Vector3.zero, Vector3.up * .01f, Vector3.zero },
                new Vector3[3], new Vector3[3]);
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.bones = new[] { pelvis };
            renderer.rootBone = pelvis;
            return renderer;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
