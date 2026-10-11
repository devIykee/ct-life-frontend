using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CTLife.Core;
using UnityEditor;
using UnityEditor.Search;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Isolated native audio lifecycle review. Run in a normal Editor with
    /// -executeMethod CTLife.EditorTools.AudioReloadReview.Launch -ctlife-audioReviewExit 1.
    /// No scene/assets/preferences are saved. SessionState carries assertions through two real reloads.
    /// </summary>
    [InitializeOnLoad]
    public static class AudioReloadReview
    {
        private const string Prefix = "CTLife.AudioReloadReview.";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const double TotalTimeoutSeconds = 240;

        [Serializable] private sealed class SceneRecord
        {
            public string path;
            public bool loaded, active;
        }
        [Serializable] private sealed class SceneRecords { public SceneRecord[] items; }

        static AudioReloadReview()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
            if (!Active || Phase != "reload-requested") return;
            SessionState.SetInt(Prefix + "reloads", Reloads + 1);
            SetPhase("restoring-after-reload", 45);
            Debug.Log($"CTLIFE [audio-reload] Real assembly reload observed: {Reloads}/2.");
        }

        private static bool Active => SessionState.GetBool(Prefix + "active", false);
        private static string Phase => SessionState.GetString(Prefix + "phase", "idle");
        private static int Reloads => SessionState.GetInt(Prefix + "reloads", 0);

        [MenuItem("CT Life/Review/Audio domain reload twice")]
        public static void Launch()
        {
            if (Application.isBatchMode)
                throw new InvalidOperationException("Audio reload review needs a normal graphics/audio-enabled Editor.");
            if (Active || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Audio reload review requires idle Edit mode.");
            RequireCleanScenes();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            SessionState.SetString(Prefix + "scenes", JsonUtility.ToJson(new SceneRecords
            {
                items = setup.Select(s => new SceneRecord { path = s.path, loaded = s.isLoaded, active = s.isActive }).ToArray()
            }));
            SessionState.SetBool(Prefix + "exitEditor", LaunchArgs.Flag("audioReviewExit"));
            SessionState.SetBool(Prefix + "sceneReplaced", false);
            SessionState.SetBool(Prefix + "sceneRestored", true);
            SessionState.SetBool(Prefix + "audioCreated", false);
            SessionState.SetBool(Prefix + "runtimeSettingsCaptured", false);
            SessionState.SetInt(Prefix + "reloads", 0);
            SessionState.SetInt(Prefix + "runtimeErrors", 0);
            SessionState.SetInt(Prefix + "emptySourceWarnings", 0);
            SessionState.SetInt(Prefix + "result", 1);
            SessionState.SetString(Prefix + "firstError", "");
            SessionState.SetBool(Prefix + "active", true);
            SetTime("totalDeadline", EditorApplication.timeSinceStartup + TotalTimeoutSeconds);
            SetPhase("waiting-for-editor", 120);
            Debug.Log("CTLIFE [audio-reload] Waiting for imports and Unity Search database initialization; two reloads, then 12 seconds sustained playback.");
        }

        private static void Tick()
        {
            if (!Active) return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (Phase == "stopping")
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode) CompleteCleanup();
                    else if (now >= GetTime("phaseDeadline"))
                    {
                        SessionState.SetInt(Prefix + "result", 1);
                        Debug.LogError("CTLIFE [audio-reload] FAIL: Editor did not leave Play mode within 30 seconds.");
                        FinishSession();
                    }
                    return;
                }
                if (now >= GetTime("totalDeadline") || now >= GetTime("phaseDeadline"))
                    throw new TimeoutException("Audio review timed out in phase " + Phase + ".");
                if (SessionState.GetInt(Prefix + "runtimeErrors", 0) != 0)
                    throw new InvalidOperationException("Runtime log error during review: " + SessionState.GetString(Prefix + "firstError", "unknown"));

                if (Phase == "waiting-for-editor")
                {
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                    if (EditorApplication.isPlayingOrWillChangePlaymode)
                        throw new InvalidOperationException("Unexpected Play mode transition during startup gate.");
                    // Public API verified against installed 6000.3.25f1 SearchService.cs.
                    // Initialize the default database in Edit mode; waiting for its whole index would
                    // import thousands of unrelated package assets and is not required by EnumerateAll.
                    if (!SearchService.EnumerateDatabases().Any()) return;
                    RequireCleanScenes();
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    SessionState.SetBool(Prefix + "sceneReplaced", true);
                    SessionState.SetBool(Prefix + "sceneRestored", false);
                    FocusGameView();
                    SetPhase("entering-play", 45);
                    EditorApplication.EnterPlaymode();
                    return;
                }
                if (Phase == "entering-play")
                {
                    if (!EditorApplication.isPlaying) return;
                    FocusGameView();
                    SetPhase("waiting-for-focus", 20);
                    LogEnvironment("entered-play");
                    return;
                }
                if (Phase == "waiting-for-focus")
                {
                    if (!Application.isFocused || !GameViewFocused())
                    {
                        // The layout can steal the first Focus() during Play-mode entry.
                        // Retry the same native Editor focus request, retaining the bounded gate.
                        if (now >= GetTime("nextFocus"))
                        {
                            FocusGameView();
                            SetTime("nextFocus", now + 1);
                        }
                        return;
                    }
                    Require(!EditorApplication.isPaused, "Editor Play mode is paused; review did not change the pause setting.");
                    Require(!EditorUtility.audioMasterMute, "Editor audio is muted; review did not change the mute setting.");
                    SessionState.SetBool(Prefix + "originalRunInBackground", Application.runInBackground);
                    SessionState.SetBool(Prefix + "runtimeSettingsCaptured", true);
                    Application.runInBackground = true;
                    Debug.Log("CTLIFE [audio-reload] Runtime runInBackground=True for isolated review; original=" +
                        SessionState.GetBool(Prefix + "originalRunInBackground", false) + "; project setting is not saved.");
                    CreateRuntime();
                    SetPhase("warm-initial", 45);
                    SetTime("readyAt", EditorApplication.timeSinceStartup + 4);
                    return;
                }
                if (!EditorApplication.isPlaying)
                    throw new InvalidOperationException("Play mode stopped before audio review completed.");
                if (Phase == "reload-requested") return;
                if (Phase == "restoring-after-reload")
                {
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                    if (SessionState.GetBool(Prefix + "runtimeSettingsCaptured", false)) Application.runInBackground = true;
                    FocusGameView();
                    SetPhase("warm-after-reload", 45);
                    SetTime("readyAt", EditorApplication.timeSinceStartup + 3);
                    LogEnvironment("returned-from-reload");
                    return;
                }
                if (Phase == "warm-initial" || Phase == "warm-after-reload")
                {
                    if (now < GetTime("readyAt")) return;
                    ValidatePlayback(Reloads == 0 ? "before-reload" : "after-reload-" + Reloads, Reloads == 0);
                    CheckTransients();
                    if (Reloads < 2)
                    {
                        SetPhase("reload-requested", 60);
                        Debug.Log($"CTLIFE [audio-reload] Requesting script reload {Reloads + 1}/2; no source mutation.");
                        EditorUtility.RequestScriptReload();
                    }
                    else
                    {
                        SetPhase("sustained-play", 30);
                        SetTime("readyAt", now + 12);
                        SetTime("nextCheck", now + 2);
                    }
                    return;
                }
                if (Phase == "sustained-play")
                {
                    if (now >= GetTime("nextCheck"))
                    {
                        ValidatePlayback("sustained", false);
                        SetTime("nextCheck", now + 2);
                    }
                    if (now < GetTime("readyAt")) return;
                    ValidatePlayback("final", false);
                    CheckTransients();
                    if (Reloads != 2) throw new InvalidOperationException("Expected exactly two observed reloads.");
                    Require(SessionState.GetInt(Prefix + "emptySourceWarnings", 0) == 0, "Empty music source playback warning occurred.");
                    BeginStop(0, "All playback/source identity/listener/bus assertions passed after two real reloads and sustained play.");
                }
            }
            catch (Exception exception)
            {
                var actual = exception is TargetInvocationException invocation && invocation.InnerException != null
                    ? invocation.InnerException : exception;
                BeginStop(1, actual.ToString());
            }
        }

        private static void CreateRuntime()
        {
            if (Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Any(x => x.GetType().FullName == "CTLife.App.GameBootstrap"))
                throw new InvalidOperationException("Audio review must not boot game/network services.");
            var camera = new GameObject("AudioReview.Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 2, -4);
            var listener = camera.gameObject.AddComponent<AudioListener>();
            var player = new GameObject("AudioReview.Player");
            var head = new GameObject("AudioReview.Head");
            head.transform.SetParent(player.transform, false);
            head.transform.localPosition = Vector3.up * 1.7f;
            var type = AudioType();
            var create = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(type.FullName, "Create");
            var audio = (Component)create.Invoke(null, null);
            SessionState.SetBool(Prefix + "audioCreated", true);
            // Runtime fields only: no volume setter/PlayerPrefs mutation. Restore original values on cleanup.
            foreach (var field in new[] { "_sfxVolume", "_musicVolume", "_ambienceVolume" })
                SessionState.SetFloat(Prefix + field, (float)Field(audio, field));
            SetField(audio, "_sfxVolume", 0.75f);
            SetField(audio, "_musicVolume", 0.7f);
            SetField(audio, "_ambienceVolume", 0.6f);
            Invoke(audio, "RegisterCameraListener", listener);
            Invoke(audio, "BindPlayerListener", player.transform, head.transform);
            Invoke(audio, "SetAmbience", "timeline_square");
            Invoke(Music(audio), "SetMusic", "timeline_square", "timeline", false);
            LogPlayback("just-created", audio);
        }

        private static void ValidatePlayback(string label, bool initial)
        {
            var audio = Audio();
            LogPlayback(label + "-preassert", audio);
            Invoke(audio, "ReviewValidateRuntime");
            var music = Music(audio);
            var sources = audio.GetComponentsInChildren<AudioSource>(true).OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
            Require(sources.Length == 26, "Expected exactly 26 native audio sources.");
            Require(sources.Select(s => s.name).Distinct().Count() == 26, "Duplicate audio source names.");
            var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            var active = listeners.Where(l => l.enabled && l.gameObject.activeInHierarchy).ToArray();
            Require(listeners.Length == 2 && active.Length == 1 && active[0].name == "player_listener",
                "Expected one active player listener and one disabled camera listener.");
            string identities = audio.GetInstanceID() + "/" + music.GetInstanceID() + ";" +
                string.Join(";", sources.Select(s => s.name + "=" + s.GetInstanceID())) + ";listeners=" +
                string.Join(",", listeners.OrderBy(l => l.name, StringComparer.Ordinal).Select(l => l.name + "=" + l.GetInstanceID()));
            if (initial) SessionState.SetString(Prefix + "identities", identities);
            else Require(identities == SessionState.GetString(Prefix + "identities", ""), "Native source/component/listener identities changed after reload.");
            Require((string)Field(music, "_district") == "timeline", "Timeline music selection was lost.");
            var musicLoops = sources.Where(s => s.name.StartsWith("music_", StringComparison.Ordinal) && s.clip != null).ToArray();
            var ambienceLoops = sources.Where(s => s.name.StartsWith("ambience", StringComparison.Ordinal) && s.clip != null).ToArray();
            Require(musicLoops.Length > 0 && ambienceLoops.Length == 1, "Expected selected music and one ambience loop.");
            foreach (var source in musicLoops.Concat(ambienceLoops))
                Require(source.isPlaying && source.clip.loadState == AudioDataLoadState.Loaded,
                    "Selected loop is not loaded/playing: " + source.name);
            Require(musicLoops.Any(s => s.volume > 0f) && ambienceLoops[0].volume > 0f,
                "Selected music or ambience mix has no audible source gain.");
            string clips = string.Join(";", musicLoops.Concat(ambienceLoops).OrderBy(s => s.name, StringComparer.Ordinal)
                .Select(s => s.name + "=" + s.clip.GetInstanceID()));
            if (initial) SessionState.SetString(Prefix + "clips", clips);
            else Require(clips == SessionState.GetString(Prefix + "clips", ""), "Selected loop clips changed across reload.");
            Debug.Log("CTLIFE [audio-reload] " + label + " reloads=" + Reloads + " ids=" + identities +
                " playheads=" + string.Join(",", musicLoops.Concat(ambienceLoops).Select(s => s.name + ":" + s.timeSamples)) +
                " " + Invoke(audio, "ReviewSnapshot"));
        }

        private static void CheckTransients()
        {
            var audio = Audio();
            var handlersField = typeof(Bus).GetField("_handlers", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(Bus).FullName, "_handlers");
            var handlers = (IDictionary)handlersField.GetValue(null);
            var handler = handlers[typeof(SfxSignal)] as Delegate;
            Require(handler != null && handler.GetInvocationList().Length == 1, "SFX bus subscription missing or duplicated.");
            long previousSfx = (long)Field(audio, "_sfxSequence");
            Bus.Publish(new SfxSignal("sfx_ui_success"));
            Require((long)Field(audio, "_sfxSequence") == previousSfx + 1, "SFX bus event did not start exactly one voice.");
            Require(audio.GetComponentsInChildren<AudioSource>().Any(s => s.name.StartsWith("sfx", StringComparison.Ordinal) && s.isPlaying),
                "UI success voice did not start playback.");
            long previousSteps = (long)Field(audio, "_reviewFootstepsPlayed");
            Invoke(audio, "PlayFootstep", "concrete", Vector3.zero);
            Require((long)Field(audio, "_reviewFootstepsPlayed") == previousSteps + 1, "Footstep did not play exactly once.");
            Require(audio.GetComponentsInChildren<AudioSource>().Any(s => s.name.StartsWith("footstep", StringComparison.Ordinal) && s.clip != null && s.isPlaying),
                "Concrete footstep voice did not start playback.");
            Debug.Log("CTLIFE [audio-reload] Transients PASS: one SFX bus binding, one success voice, one concrete footstep. " + Invoke(audio, "ReviewSnapshot"));
        }

        private static void FocusGameView()
        {
            // Same Editor type lookup used by FrontendReview; no layout or mute preference is saved.
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
            var window = EditorWindow.GetWindow(type);
            window.Show();
            window.Focus();
        }

        private static bool GameViewFocused() => EditorWindow.focusedWindow != null &&
            EditorWindow.focusedWindow.GetType().FullName == "UnityEditor.GameView";

        private static void LogEnvironment(string label)
        {
            var config = AudioSettings.GetConfiguration();
            Debug.Log($"CTLIFE [audio-reload] environment={label} " +
                $"focused={Application.isFocused} gameViewFocused={GameViewFocused()} background={Application.runInBackground} " +
                $"editorPaused={EditorApplication.isPaused} editorMute={EditorUtility.audioMasterMute} " +
                $"listenerPaused={AudioListener.pause} listenerGain={AudioListener.volume:0.000} " +
                $"dsp={AudioSettings.dspTime:0.000} sampleRate={config.sampleRate} dspBuffer={config.dspBufferSize} " +
                $"realVoices={config.numRealVoices} virtualVoices={config.numVirtualVoices}");
        }

        private static void LogPlayback(string label, Component audio)
        {
            LogEnvironment(label);
            foreach (var source in audio.GetComponentsInChildren<AudioSource>(true).Where(s => s.clip != null))
            {
                var clip = source.clip;
                Debug.Log($"CTLIFE [audio-reload] source={source.name} id={source.GetInstanceID()} " +
                    $"enabled={source.enabled} active={source.gameObject.activeInHierarchy} playing={source.isPlaying} " +
                    $"muted={source.mute} loop={source.loop} gain={source.volume:0.000} samples={source.timeSamples} " +
                    $"clip={clip.name} clipId={clip.GetInstanceID()} clipLoad={clip.loadState} loadType={clip.loadType} " +
                    $"backgroundLoad={clip.loadInBackground} length={clip.length:0.000}");
            }
            Debug.Log("CTLIFE [audio-reload] snapshot=" + label + " " + Invoke(audio, "ReviewSnapshot"));
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!Active || Phase == "stopping" || !EditorApplication.isPlaying) return;
            if (condition.StartsWith("Only custom filters can be played.", StringComparison.Ordinal) &&
                condition.Contains("(music_"))
                SessionState.SetInt(Prefix + "emptySourceWarnings", SessionState.GetInt(Prefix + "emptySourceWarnings", 0) + 1);
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            int count = SessionState.GetInt(Prefix + "runtimeErrors", 0);
            if (count == 0) SessionState.SetString(Prefix + "firstError", condition + "\n" + stackTrace);
            SessionState.SetInt(Prefix + "runtimeErrors", count + 1);
        }

        private static void BeginStop(int result, string detail)
        {
            if (Phase == "stopping") return;
            SessionState.SetInt(Prefix + "result", result);
            SetPhase("stopping", 30);
            if (result == 0) Debug.Log("CTLIFE [audio-reload] Assertions passed; restoring scene. " + detail);
            else Debug.LogError("CTLIFE [audio-reload] FAIL: " + detail);
            try { RestoreRuntimeState(); }
            catch (Exception exception)
            {
                SessionState.SetInt(Prefix + "result", 1);
                Debug.LogError("CTLIFE [audio-reload] FAIL: Runtime setting restoration: " + exception);
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
            else CompleteCleanup();
        }

        private static void RestoreRuntimeState()
        {
            if (SessionState.GetBool(Prefix + "runtimeSettingsCaptured", false) && EditorApplication.isPlaying)
            {
                Application.runInBackground = SessionState.GetBool(Prefix + "originalRunInBackground", false);
                Debug.Log("CTLIFE [audio-reload] Restored runtime runInBackground=" + Application.runInBackground);
            }
            if (!SessionState.GetBool(Prefix + "audioCreated", false) || !EditorApplication.isPlaying) return;
            var found = Object.FindObjectsByType(AudioType(), FindObjectsSortMode.None);
            if (found.Length != 1) return;
            foreach (var field in new[] { "_sfxVolume", "_musicVolume", "_ambienceVolume" })
                SetField(found[0], field, SessionState.GetFloat(Prefix + field, 1f));
        }

        private static void CompleteCleanup()
        {
            try
            {
                if (SessionState.GetBool(Prefix + "sceneReplaced", false))
                {
                    // Only this deliberately empty, unsaved review scene is replaced; saved scenes were never modified.
                    var records = JsonUtility.FromJson<SceneRecords>(SessionState.GetString(Prefix + "scenes", "{}"));
                    var saved = records?.items?.Where(s => !string.IsNullOrEmpty(s.path)).ToArray();
                    if (saved != null && saved.Length > 0)
                    {
                        if (!saved.Any(s => s.active)) saved.First(s => s.loaded).active = true;
                        EditorSceneManager.RestoreSceneManagerSetup(saved.Select(s => new SceneSetup
                        { path = s.path, isLoaded = s.loaded, isActive = s.active }).ToArray());
                    }
                    else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    SessionState.SetBool(Prefix + "sceneRestored", true);
                }
            }
            catch (Exception exception)
            {
                SessionState.SetInt(Prefix + "result", 1);
                Debug.LogError("CTLIFE [audio-reload] FAIL: Scene restoration: " + exception);
            }
            FinishSession();
        }

        private static void FinishSession()
        {
            int result = SessionState.GetInt(Prefix + "result", 1);
            bool exit = SessionState.GetBool(Prefix + "exitEditor", false);
            SessionState.SetBool(Prefix + "active", false);
            SessionState.SetString(Prefix + "phase", "idle");
            Debug.Log("CTLIFE [audio-reload] " + (result == 0 ? "PASS" : "FAILED") + " reloads=" + Reloads +
                " runtimeErrors=" + SessionState.GetInt(Prefix + "runtimeErrors", 0) +
                " emptySourceWarnings=" + SessionState.GetInt(Prefix + "emptySourceWarnings", 0) +
                " sceneRestored=" + SessionState.GetBool(Prefix + "sceneRestored", false));
            if (exit) EditorApplication.Exit(result);
        }

        private static void RequireCleanScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty || (string.IsNullOrEmpty(scene.path) && scene.rootCount > 0))
                    throw new InvalidOperationException("Audio review refused to replace an unsaved scene. Save or close it first.");
            }
        }

        private static Type AudioType() => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("CTLife.Audio.AudioManager", false)).FirstOrDefault(t => t != null)
            ?? throw new TypeLoadException("CTLife.Audio.AudioManager is not loaded.");

        private static Component Audio()
        {
            var found = Object.FindObjectsByType(AudioType(), FindObjectsSortMode.None);
            Require(found.Length == 1, "Expected exactly one AudioManager.");
            return (Component)found[0];
        }

        private static Component Music(Component audio) => (Component)(audio.GetType().GetProperty("Music")
            ?? throw new MissingMemberException(audio.GetType().FullName, "Music")).GetValue(audio);

        private static object Invoke(object target, string name, params object[] args) =>
            (target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new MissingMethodException(target.GetType().FullName, name)).Invoke(target, args);

        private static object Field(object target, string name) =>
            (target.GetType().GetField(name, PrivateInstance) ?? throw new MissingFieldException(target.GetType().FullName, name)).GetValue(target);

        private static void SetField(object target, string name, object value) =>
            (target.GetType().GetField(name, PrivateInstance) ?? throw new MissingFieldException(target.GetType().FullName, name)).SetValue(target, value);

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void SetPhase(string phase, double timeout)
        {
            SessionState.SetString(Prefix + "phase", phase);
            SetTime("phaseDeadline", EditorApplication.timeSinceStartup + timeout);
        }

        private static void SetTime(string key, double value) => SessionState.SetString(Prefix + key, value.ToString("R", CultureInfo.InvariantCulture));
        private static double GetTime(string key) => double.Parse(SessionState.GetString(Prefix + key, "0"), CultureInfo.InvariantCulture);
    }
}
