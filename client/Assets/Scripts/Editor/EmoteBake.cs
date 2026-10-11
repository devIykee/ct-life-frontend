using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Feel lane F1 (2026-10-17), "when it raises its hands it feels very weird". The standing emotes were hand-keyed on
    /// the UAL idle, whose wide A-stance is not the game's idle (the relaxed Snow / Rain idles), so every emote spread the
    /// legs; the wave, point and shrug arms were stiff. This bakes each emote as a Humanoid muscle clip: the upper body
    /// from the gesture (CMU mocap where there is one, else the hand-keyed clip), eased in and out of the idle's own upper
    /// body, standing on the idle's legs and root. One clip per idle: the Snow idle goes on the base controller, the Rain
    /// idle into RainController's overrides. Whole-body moves (jumps, stomps, dances) keep their own clips.
    /// Batch: Unity -batchmode -executeMethod CTLife.EditorTools.EmoteBake.BuildBatch
    /// </summary>
    public static class EmoteBake
    {
        public const string Folder = "Assets/Art/animations/emotes";
        private const string Anim = "Assets/Art/animations/";
        private const string Mocap = MocapImport.Root + "/";
        private const string ControllerPath = CharacterFeelController.ControllerPath;
        private const string RainOverridePath = "Assets/Resources/Characters/RainController.overrideController";
        public const string RainIdle = "Assets/Art/characters/authored/RainIdle/RainAdult_RelaxedIdle.fbx";
        private const float Fps = 30f;

        public sealed class Spec
        {
            public string State, Source;
            public float From;                // source window start, seconds
            public float RampIn = 0.3f, RampOut = 0.35f;
            public Spec(string state, string source) { State = state; Source = source; }
        }

        /// <summary>Emote state → gesture source. Loops, jumps, stomps and dances keep their clips (legs are the move).</summary>
        public static readonly Spec[] Specs =
        {
            new Spec("emote_wave", Mocap + "wave_hello.fbx"),
            new Spec("emote_shrug", Mocap + "shrug.fbx"),
            new Spec("emote_point", Anim + "emote_point.fbx"),
            new Spec("emote_bow", Anim + "emote_bow.fbx"),
            new Spec("emote_salute", Anim + "emote_salute.fbx"),
            new Spec("emote_thumbs_up", Anim + "emote_thumbs_up.fbx"),
            new Spec("emote_clap", Anim + "emote_clap.fbx"),
            new Spec("emote_heart_hands", Anim + "emote_heart_hands.fbx"),
            new Spec("emote_wagmi", Anim + "emote_wagmi_fist_pump.fbx"),
            new Spec("emote_chest_thump", Anim + "emote_chest_thump.fbx"),
            new Spec("emote_laugh", Anim + "emote_laugh.fbx"),
            new Spec("emote_facepalm", Anim + "emote_facepalm.fbx"),
            new Spec("emote_hood_up", Anim + "emote_hood_up.fbx"),
            new Spec("emote_ring_light", Anim + "emote_ring_light_pose.fbx"),
            new Spec("emote_coin_flip", Anim + "emote_coin_flip.fbx"),
            new Spec("emote_gm_coffee", Anim + "emote_gm_coffee.fbx"),
            new Spec("emote_watering_can", Anim + "emote_watering_can.fbx"),
            new Spec("emote_head_shake", Anim + "emote_headshake.fbx"),
            new Spec("emote_nod", Anim + "emote_nod.fbx"),
            new Spec("react_shock", Anim + "react_shock_gasp.fbx"),
            new Spec("react_sad", Anim + "react_sad.fbx"),
        };

        // ------------------------------------------------------------------ channel groups

        private static readonly Regex LegMuscle = new Regex(@"^(Left|Right) (Upper Leg|Lower Leg|Foot|Toes) ", RegexOptions.Compiled);

        public enum Group { Root, Legs, Fingers, Upper }

        /// <summary>Which body part a Humanoid clip channel drives (Animator muscle/goal property names).</summary>
        public static Group GroupOf(string property)
        {
            if (property.StartsWith("RootT.", StringComparison.Ordinal) || property.StartsWith("RootQ.", StringComparison.Ordinal)) return Group.Root;
            if (property.StartsWith("LeftFootT.", StringComparison.Ordinal) || property.StartsWith("LeftFootQ.", StringComparison.Ordinal)
                || property.StartsWith("RightFootT.", StringComparison.Ordinal) || property.StartsWith("RightFootQ.", StringComparison.Ordinal)) return Group.Legs;
            if (property.StartsWith("LeftHand.", StringComparison.Ordinal) || property.StartsWith("RightHand.", StringComparison.Ordinal)) return Group.Fingers;
            return LegMuscle.IsMatch(property) ? Group.Legs : Group.Upper;
        }

        /// <summary>Gesture weight at t of a clip lasting <paramref name="length"/> (smoothstep in and out).</summary>
        public static float Weight(float t, float length, float rampIn, float rampOut)
        {
            float a = rampIn > 0f ? Mathf.Clamp01(t / rampIn) : 1f, b = rampOut > 0f ? Mathf.Clamp01((length - t) / rampOut) : 1f;
            float w = Mathf.Min(a, b);
            return w * w * (3f - 2f * w);
        }

        // ------------------------------------------------------------------ build

        [MenuItem("CT Life/Characters/Bake emotes onto the idles")]
        public static void Build()
        {
            MocapImport.Run();
            Directory.CreateDirectory(Folder);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) ?? throw new FileNotFoundException(ControllerPath);
            var rain = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(RainOverridePath) ?? throw new FileNotFoundException(RainOverridePath);
            var snowIdle = ClipAt(CharacterFeelController.SnowIdle);
            var rainIdle = ClipAt(RainIdle);
            var durations = RegistryDurations();
            var states = controller.layers[0].stateMachine.states.ToDictionary(s => s.state.name, s => s.state);
            var rainPairs = new List<(AnimationClip snow, AnimationClip rain)>();
            foreach (var spec in Specs)
            {
                if (!states.TryGetValue(spec.State, out var state)) throw new InvalidDataException("No controller state " + spec.State);
                var source = ClipAt(spec.Source);
                float length = durations.TryGetValue(spec.State, out var d) ? d : source.length - spec.From;
                var snow = Bake(spec, source, snowIdle, length, Folder + "/" + spec.State + ".anim");
                state.motion = snow;
                rainPairs.Add((snow, Bake(spec, source, rainIdle, length, Folder + "/" + spec.State + "_rain.anim")));
                Debug.Log($"CTLIFE [emotes] {spec.State}: {Path.GetFileName(spec.Source)}, {length:F2} s");
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(rain.overridesCount);
            rain.GetOverrides(overrides);
            for (int i = 0; i < overrides.Count; i++)
            {
                var hit = rainPairs.FirstOrDefault(p => p.snow == overrides[i].Key);
                if (hit.snow != null) overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(hit.snow, hit.rain);
            }
            rain.ApplyOverrides(overrides);
            EditorUtility.SetDirty(rain);
            AssetDatabase.SaveAssets();
            int wired = overrides.Count(o => rainPairs.Any(p => p.snow == o.Key && o.Value == p.rain));
            if (wired != rainPairs.Count) throw new InvalidDataException($"RainController overrides wired {wired}/{rainPairs.Count}");
            Debug.Log($"CTLIFE [emotes] baked {Specs.Length} emotes (and their Rain variants) into {Folder}");
        }

        public static void BuildBatch()
        {
            int code = 0;
            try { Build(); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        private static AnimationClip ClipAt(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
            ?? throw new FileNotFoundException("No clip in " + path);

        /// <summary>Server registry durations (anims.animMeta[id].durationMs): PlayOneShot ends the overlay there.</summary>
        private static Dictionary<string, float> RegistryDurations()
        {
            var d = new Dictionary<string, float>(StringComparer.Ordinal);
            var text = Resources.Load<TextAsset>("Contract/config_snapshot");
            var meta = text != null ? JObject.Parse(text.text)["ids"]?["anims"]?["animMeta"] as JObject : null;
            if (meta == null) return d;
            foreach (var p in meta.Properties())
                if (p.Value["durationMs"] != null) d[p.Name] = (float)p.Value["durationMs"] / 1000f;
            return d;
        }

        private static AnimationClip Bake(Spec spec, AnimationClip source, AnimationClip idle, float length, string path)
        {
            var src = Curves(source);
            var idl = Curves(idle);
            if (!src.Keys.Any(k => GroupOf(k) == Group.Upper)) throw new InvalidDataException(source.name + " has no Humanoid muscle curves");
            var names = new SortedSet<string>(src.Keys, StringComparer.Ordinal);
            names.UnionWith(idl.Keys);
            int frames = Mathf.Max(2, Mathf.RoundToInt(length * Fps) + 1);
            float idleLength = Mathf.Max(idle.length, 1f / Fps);
            // A gesture shorter than the registry duration eases out at its own end and the idle carries the rest.
            float gesture = Mathf.Min(length, source.length - spec.From);
            var keys = names.ToDictionary(n => n, _ => new Keyframe[frames], StringComparer.Ordinal);
            for (int f = 0; f < frames; f++)
            {
                float t = Mathf.Min(f / Fps, length);
                float ts = Mathf.Min(spec.From + t, source.length);
                float ti = t % idleLength;
                float w = t <= gesture ? Weight(t, gesture, spec.RampIn, spec.RampOut) : 0f;
                foreach (var n in names)
                {
                    var g = GroupOf(n);
                    float vi = idl.TryGetValue(n, out var ci) ? ci.Evaluate(ti) : src[n].Evaluate(spec.From);
                    float vs = src.TryGetValue(n, out var cs) ? cs.Evaluate(ts) : vi;
                    // Root, legs, feet goals and fingers stay the idle's (the Hands layer poses the fingers).
                    float v = g == Group.Upper ? Mathf.Lerp(vi, vs, w) : vi;
                    keys[n][f] = new Keyframe(t, v);
                }
            }
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves();
            clip.frameRate = Fps;
            NormalizeQuaternions(keys, "RootQ.");
            foreach (var kv in keys)
            {
                var curve = new AnimationCurve(kv.Value);
                for (int i = 0; i < curve.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key), curve);
            }
            // Root handling as the clip the legs and root came from: the idle's bake-into-pose settings.
            var settings = AnimationUtility.GetAnimationClipSettings(idle);
            settings.loopTime = false;
            settings.startTime = 0f;
            settings.stopTime = length;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static Dictionary<string, AnimationCurve> Curves(AnimationClip clip)
        {
            var d = new Dictionary<string, AnimationCurve>(StringComparer.Ordinal);
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
                if (b.type == typeof(Animator) && string.IsNullOrEmpty(b.path))
                {
                    var c = AnimationUtility.GetEditorCurve(clip, b);
                    if (c != null && c.length > 0) d[b.propertyName] = c;
                }
            return d;
        }

        private static void NormalizeQuaternions(Dictionary<string, Keyframe[]> keys, string prefix)
        {
            if (!keys.TryGetValue(prefix + "x", out var x) || !keys.TryGetValue(prefix + "y", out var y)
                || !keys.TryGetValue(prefix + "z", out var z) || !keys.TryGetValue(prefix + "w", out var w)) return;
            for (int i = 0; i < x.Length; i++)
            {
                var q = new Quaternion(x[i].value, y[i].value, z[i].value, w[i].value);
                float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                if (m < 1e-6f) continue;
                x[i].value /= m; y[i].value /= m; z[i].value /= m; w[i].value /= m;
            }
        }
    }
}
