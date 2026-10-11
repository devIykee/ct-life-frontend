using System;
using System.Collections.Generic;

namespace CTLife.Audio
{
    /// <summary>The code mixer's buses. Every AudioSource CT Life owns belongs to exactly one.</summary>
    public enum AudioBus { Music, Sfx, Ambience, Voice }

    /// <summary>
    /// The player's audio settings (Settings app): master, music, sound effects, voice and ambience, each a 0..1
    /// slider value persisted in PlayerPrefs (`ctlife.audio.&lt;key&gt;`). Sliders are tapered (gain = v²: 50 % ≈ −12 dB,
    /// 10 % ≈ −40 dB) so the steps sound even. Master is applied once (AudioListener.volume) and also covers LiveKit
    /// voices; each bus gain here excludes master.
    /// </summary>
    public sealed class VolumeSettings
    {
        public const string PrefPrefix = "ctlife.audio.";
        public static readonly string[] Keys = { "master", "music", "sfx", "voice", "ambience" };

        // Defaults (2026-10-09): music 0.85 → −2.8 dB, so the −16 LUFS title theme plays near −19 LUFS (the spec's mix target
        // is −18..−16); 0.7 put it at −22 LUFS, which a Seeker at low media volume rendered close to silent. Ambience 0.75
        // → −5 dB. UI cue levels in sfx-map.json are audited against these defaults (audio/tools/mix_audit.py).
        public const float DefaultMusic = 0.85f, DefaultAmbience = 0.75f;

        public float Master = 1f, Music = DefaultMusic, Sfx = 1f, Voice = 1f, Ambience = DefaultAmbience;

        public static float Default(string key) => key switch
        {
            "music" => DefaultMusic,
            "ambience" => DefaultAmbience,
            _ => 1f,
        };

        public float Get(string key) => key switch
        {
            "master" => Master,
            "music" => Music,
            "sfx" => Sfx,
            "voice" => Voice,
            "ambience" => Ambience,
            _ => throw new ArgumentException("Unknown audio setting " + key),
        };

        /// <returns>True when the stored value changed.</returns>
        public bool Set(string key, float value)
        {
            value = AudioMath.Clamp01(value);
            if (Math.Abs(Get(key) - value) < 1e-5f) return false;
            switch (key)
            {
                case "master": Master = value; break;
                case "music": Music = value; break;
                case "sfx": Sfx = value; break;
                case "voice": Voice = value; break;
                case "ambience": Ambience = value; break;
            }
            return true;
        }

        public static float Taper(float slider)
        {
            slider = AudioMath.Clamp01(slider);
            return slider * slider;
        }

        public float BusGain(AudioBus bus) => bus switch
        {
            AudioBus.Music => Taper(Music),
            AudioBus.Sfx => Taper(Sfx),
            AudioBus.Ambience => Taper(Ambience),
            AudioBus.Voice => Taper(Voice),
            _ => 1f,
        };

        public float MasterGain => Taper(Master);
    }

    /// <summary>
    /// Music ducking (Build Spec: "The music bus ducks 6 dB whenever a nearby player or a Space speaker is talking";
    /// "Stingers (each ducks the music)"). Sources are keyed holds; the deepest active hold wins (no stacking, so
    /// voice + stinger is −9 dB, not −15). Attack is fast (≈ 75 dB/s), release slow (≈ 12 dB/s) so speech pauses
    /// between words do not pump the music.
    /// </summary>
    public sealed class Ducker
    {
        public const float VoiceDuckDb = -6f;
        public const float StingerDuckDb = -9f;
        public const float AttackDbPerSecond = 75f;
        public const float ReleaseDbPerSecond = 12f;
        public const string VoiceKey = "voice";
        public const string StingerKey = "stinger";

        private readonly Dictionary<string, float> _holds = new Dictionary<string, float>();
        private readonly Dictionary<string, double> _expires = new Dictionary<string, double>();
        private float _currentDb;

        public float CurrentDb => _currentDb;

        /// <summary>Hold a duck until cleared (db ≤ 0; 0 or positive clears). Keys: "voice", "spaces", "call", …</summary>
        public void Hold(string key, float db)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (float.IsNaN(db) || db >= 0f) { Clear(key); return; }
            _holds[key] = Math.Max(db, AudioMath.SilenceDb);
            _expires.Remove(key);
        }

        /// <summary>Hold a duck until DSP/unscaled time `until` (stingers: clip length).</summary>
        public void HoldUntil(string key, float db, double now, double until)
        {
            if (string.IsNullOrEmpty(key) || until <= now || float.IsNaN(db) || db >= 0f) return;
            if (_expires.TryGetValue(key, out var existing) && existing > until && _holds.TryGetValue(key, out var d) && d <= db) return;
            _holds[key] = db;
            _expires[key] = until;
        }

        public void Clear(string key)
        {
            if (key == null) return;
            _holds.Remove(key);
            _expires.Remove(key);
        }

        public bool IsHeld(string key) => key != null && _holds.ContainsKey(key);

        public void ClearAll()
        {
            _holds.Clear();
            _expires.Clear();
        }

        private readonly List<string> _expired = new List<string>();

        public float TargetDb(double now)
        {
            _expired.Clear();
            foreach (var kv in _expires) if (now >= kv.Value) _expired.Add(kv.Key);
            foreach (var k in _expired) { _holds.Remove(k); _expires.Remove(k); }
            float target = 0f;
            foreach (var v in _holds.Values) if (v < target) target = v;
            return target;
        }

        /// <summary>Advance the smoothed duck; returns the current music gain offset in dB (≤ 0).</summary>
        public float Tick(double now, float deltaSeconds)
        {
            float target = TargetDb(now);
            if (float.IsNaN(deltaSeconds) || deltaSeconds < 0f) deltaSeconds = 0f;
            deltaSeconds = Math.Min(deltaSeconds, 0.5f);
            float rate = target < _currentDb ? AttackDbPerSecond : ReleaseDbPerSecond;
            _currentDb = AudioMath.MoveTowards(_currentDb, target, rate * deltaSeconds);
            return _currentDb;
        }

        /// <summary>Jump straight to the target (used when music starts or after a reload).</summary>
        public void Snap(double now) => _currentDb = TargetDb(now);
    }
}
