using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.Audio
{
    /// <summary>
    /// Adaptive district music (Build Spec "Adaptive music"): four stems (pad, bass, drums, lead) plus an optional
    /// tension layer. `market_mood` runs −1 (red) to +1 (green): green brings in drums and lead, red low-passes the
    /// mix and adds tension. Stems start sample-aligned with PlayScheduled.
    /// </summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        private static readonly string[] StemNames = { "pad", "bass", "drums", "lead", "tension" };

        private AudioManager _audio;
        private readonly Dictionary<string, AudioSource> _stems = new Dictionary<string, AudioSource>();
        private readonly Dictionary<string, AudioLowPassFilter> _filters = new Dictionary<string, AudioLowPassFilter>();
        private string _district;
        private float _mood;      // −1..1
        private float _smoothedMood;

        public float Mood => _mood;

        public void Init(AudioManager audio)
        {
            _audio = audio;
            foreach (var n in StemNames)
            {
                var go = new GameObject("music_" + n);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.loop = true;
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                _stems[n] = s;
                _filters[n] = go.AddComponent<AudioLowPassFilter>();
                _filters[n].cutoffFrequency = 22000f;
            }
        }

        /// <summary>Market Mood bps (real SOL 24h change) → −1..1. ±5% saturates.</summary>
        public void SetMood(int bps) => _mood = Mathf.Clamp(bps / 500f, -1f, 1f);

        public void SetDistrict(string districtId)
        {
            if (districtId == _district) return;
            _district = districtId;
            foreach (var s in _stems.Values) { s.Stop(); s.clip = null; }
            if (districtId == null || !_audio.Map.Music.TryGetValue(districtId, out var set)) return;

            var start = AudioSettings.dspTime + 0.2;
            foreach (var n in StemNames)
            {
                if (!set.stems.TryGetValue(n, out var path)) continue;
                var clip = _audio.Clip(path);
                if (clip == null) continue;
                var s = _stems[n];
                s.clip = clip;
                s.volume = 0f;
                s.PlayScheduled(start);
            }
            Log.Info("audio", $"Music: {districtId} @ {set.bpm} BPM");
        }

        private void Update()
        {
            _smoothedMood = Mathf.MoveTowards(_smoothedMood, _mood, Time.unscaledDeltaTime * 0.25f);
            float m = _smoothedMood;
            float master = _audio.musicVolume * Mathf.Pow(10f, _audio.MusicDuckDb / 20f);

            SetStem("pad", 1f, master);
            SetStem("bass", 1f, master);
            SetStem("drums", Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.1f, 0.3f, m)), master);
            SetStem("lead", Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, m)), master);
            SetStem("tension", Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, -0.6f, m)), master);

            // Red low-passes the mix: 22 kHz at mood ≥ 0 down to 1.2 kHz at −1.
            float cutoff = m >= 0f ? 22000f : Mathf.Lerp(22000f, 1200f, Mathf.Pow(-m, 0.7f));
            foreach (var f in _filters.Values) f.cutoffFrequency = cutoff;
        }

        private void SetStem(string n, float target, float master)
        {
            var s = _stems[n];
            if (s.clip == null) return;
            s.volume = Mathf.MoveTowards(s.volume, target * master, Time.unscaledDeltaTime * 0.5f);
        }
    }
}
