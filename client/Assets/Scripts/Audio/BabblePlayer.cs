// CT Life NPC babble player (rules: audio/BABBLE.md; schedule: Logic/BabbleScheduler.cs; Python twin used to audition
// lines: audio/tools/babble_preview.py). Unity 6000.3 APIs used: AudioSource.PlayScheduled, AudioSettings.dspTime,
// AudioSource.pitch.
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.Audio
{
    [System.Serializable]
    public class BabbleBank
    {
        public string archetype = "generic";
        [Tooltip("26 clips: babble_<archetype>_a ... _z, in order a..z")]
        public AudioClip[] letters = new AudioClip[26];
        public float charsPerSecond = 16f;          // babble-banks.json: chars_per_second
        public float pitchJitterSemitones = 1.5f;   // pitch_jitter_semitones
        public float npcPitchMinSemitones = -2f;    // npc_pitch_range_semitones[0]
        public float npcPitchMaxSemitones = 2f;     // npc_pitch_range_semitones[1]
        public float volumeDb = -6f;                // volume_db
        [Tooltip("Optional sfx id played (quietly, at most once per SignatureInterval per NPC) as the line starts")]
        public string signatureSfx;                 // babble.<archetype>.signature in sfx-map.json

        public BabbleVoice Voice => new BabbleVoice
        {
            CharsPerSecond = charsPerSecond, PitchJitterSemitones = pitchJitterSemitones,
            NpcPitchMinSemitones = npcPitchMinSemitones, NpcPitchMaxSemitones = npcPitchMaxSemitones, VolumeDb = volumeDb,
        };
    }

    /// <summary>
    /// Babbles NPC lines with the archetype's syllable bank: per-NPC pitch, one syllable per character, 4 round-robin
    /// 3D voices scheduled ~0.12 s ahead on the DSP clock (overlaps allowed, never cut). Rate limits and earshot come
    /// from <see cref="AudioManager.BabbleBudget"/> (≤ 3 talkers near the listener, nearest win; 1 line / NPC / 1.5 s;
    /// silent beyond 15 m with linear distance roll-off). Babble is on the SFX bus. Voices are created on first use.
    /// </summary>
    public class BabblePlayer : MonoBehaviour
    {
        public BabbleBank bank;
        public string npcId = "npc_0001";
        [Tooltip("TALK state lasts 3 s (spec, NPCs and AI)")]
        public float maxSeconds = BabbleScheduler.MaxSeconds;
        [Tooltip("How far ahead syllables are handed to the audio thread")]
        public float scheduleAhead = 0.12f;

        private const int VoiceCount = 4;           // round-robin voices: syllables may overlap a little
        /// <summary>Archetype signature sounds (research dossier `babble_sound`, [Inference]) are a garnish: rare and quiet.</summary>
        public const float SignatureInterval = 30f, SignatureVolume = 0.5f;
        private double _lastSignature = double.NegativeInfinity;
        private const float MinDistance = 2f;
        private readonly AudioSource[] _voices = new AudioSource[VoiceCount];
        private readonly float[] _voiceGains = new float[VoiceCount];
        private List<BabbleSyllable> _queue = new List<BabbleSyllable>();
        private int _next, _nextVoice;
        private double _lineStartDsp;
        private bool _voicesReady;

        private void EnsureVoices()
        {
            if (_voicesReady) return;
            for (int i = 0; i < VoiceCount; i++)
            {
                var go = new GameObject("babble_voice_" + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 1.6f, 0f);   // roughly mouth height
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;                  // 3D: heard from the NPC
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = MinDistance;
                s.maxDistance = BabbleBudget.MaxDistance;
                s.dopplerLevel = 0f;
                _voices[i] = s;
            }
            _voicesReady = true;
        }

        /// <summary>
        /// Babble a line. Returns its length in seconds (use it for the speech bubble / mouth flap) even when the budget
        /// keeps it silent (too far, too many talkers, too soon).
        /// </summary>
        public float Say(string text)
        {
            StopTalking();
            if (bank == null || string.IsNullOrEmpty(text)) return 0f;
            var syllables = BabbleScheduler.Schedule(text, npcId, bank.Voice, maxSeconds);
            float duration = BabbleScheduler.Duration(syllables);
            if (syllables.Count == 0) return 0f;
            var audio = AudioManager.Instance;
            if (audio != null)
            {
                float distance = Vector3.Distance(audio.ListenerPosition, transform.position);
                var decision = audio.BabbleBudget.Request(npcId, distance, duration, Time.unscaledTimeAsDouble);
                if (decision.Verdict == BabbleBudget.Verdict.Deny) return duration;
                if (decision.Verdict == BabbleBudget.Verdict.AllowPreempt) Preempt(decision.Preempt);
                if (audio.SfxGain <= 0f) return duration;
            }
            if (audio != null && !string.IsNullOrEmpty(bank.signatureSfx) && Time.unscaledTimeAsDouble - _lastSignature >= SignatureInterval)
            {
                _lastSignature = Time.unscaledTimeAsDouble;
                audio.PlayAt(bank.signatureSfx, transform.position + Vector3.up * 1.6f, SignatureVolume);
            }
            EnsureVoices();
            _queue = syllables;
            _next = 0;
            _lineStartDsp = AudioSettings.dspTime + scheduleAhead;
            Active[npcId ?? ""] = this;
            return duration;
        }

        private static readonly Dictionary<string, BabblePlayer> Active = new Dictionary<string, BabblePlayer>();

        private static void Preempt(string otherNpc)
        {
            if (otherNpc != null && Active.TryGetValue(otherNpc, out var other) && other != null) other.StopTalking(fromBudget: true);
        }

        public void StopTalking() => StopTalking(false);

        private void StopTalking(bool fromBudget)
        {
            _queue.Clear();
            _next = 0;
            if (_voicesReady) foreach (var v in _voices) if (v != null) v.Stop();
            if (npcId != null && Active.TryGetValue(npcId, out var me) && me == this) Active.Remove(npcId);
            if (!fromBudget) AudioManager.Instance?.BabbleBudget.Stopped(npcId);
        }

        private void OnDisable() => StopTalking();

        private void Update()
        {
            if (!_voicesReady) return;
            float master = AudioManager.Instance != null ? AudioManager.Instance.SfxGain : 1f;
            // Also update the last scheduled syllables after the queue is empty, so mute applies immediately.
            for (int i = 0; i < VoiceCount; i++) _voices[i].volume = _voiceGains[i] * master;
            if (_next >= _queue.Count || bank == null) return;
            double now = AudioSettings.dspTime;
            float baseVol = AudioMath.DbToLinear(bank.volumeDb);
            // Hand over every syllable that starts within the look-ahead window.
            while (_next < _queue.Count && _lineStartDsp + _queue[_next].Time < now + scheduleAhead)
            {
                var s = _queue[_next++];
                var clip = s.Letter >= 0 && s.Letter < bank.letters.Length ? bank.letters[s.Letter] : null;
                if (clip == null) continue;                       // missing clip: skip, never crash
                var v = _voices[_nextVoice];
                _voiceGains[_nextVoice] = Mathf.Clamp01(baseVol * s.Gain);
                v.volume = _voiceGains[_nextVoice] * master;
                _nextVoice = (_nextVoice + 1) % VoiceCount;
                v.clip = clip;
                v.pitch = AudioMath.SemitonesToPitch(s.Semitones); // resample: pitch and speed together
                v.PlayScheduled(System.Math.Max(now, _lineStartDsp + s.Time));
            }
        }
    }
}
