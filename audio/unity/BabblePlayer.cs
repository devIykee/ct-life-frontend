// CT Life - reference NPC babble player (written by the audio agent; FE owns client/, copy and adapt).
// Rules: audio/BABBLE.md. Python twin used to audition lines: audio/tools/babble_preview.py.
// Unity 6000.3 APIs used: AudioSource.PlayScheduled, AudioSettings.dspTime, AudioSource.pitch.
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
    }

    public class BabblePlayer : MonoBehaviour
    {
        public BabbleBank bank;
        public string npcId = "npc_0001";
        [Tooltip("TALK state lasts 3 s (spec, NPCs and AI)")]
        public float maxSeconds = 3f;
        [Tooltip("How far ahead syllables are handed to the audio thread")]
        public float scheduleAhead = 0.12f;

        const int VoiceCount = 4;                    // round-robin voices: syllables may overlap a little
        readonly AudioSource[] voices = new AudioSource[VoiceCount];
        readonly List<Syllable> queue = new List<Syllable>();
        int nextVoice;
        double lineStartDsp;

        struct Syllable { public float time; public int letter; public float semitones; public float gain; }

        static readonly Dictionary<char, char> Digits = new Dictionary<char, char>
        { {'0','o'},{'1','w'},{'2','t'},{'3','r'},{'4','f'},{'5','f'},{'6','s'},{'7','s'},{'8','e'},{'9','n'} };
        static readonly Dictionary<char, char> Symbols = new Dictionary<char, char>
        { {'$','s'},{'@','a'},{'#','h'},{'%','p'},{'&','n'},{'+','p'} };
        static readonly Dictionary<char, float> Pauses = new Dictionary<char, float>
        { {' ',1f},{',',2f},{';',2f},{':',2f},{'.',3f},{'!',3f},{'?',3f},{'\n',3f},{'-',1f},{'/',1f} };

        void Awake()
        {
            for (int i = 0; i < VoiceCount; i++)
            {
                var go = new GameObject("babble_voice_" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;                  // 3D: heard from the NPC
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.minDistance = 2f;
                s.maxDistance = 15f;
                s.dopplerLevel = 0f;
                voices[i] = s;
            }
        }

        /// <summary>Babble a line. Returns its length in seconds (use it for the speech bubble / mouth flap).</summary>
        public float Say(string text)
        {
            StopTalking();
            if (bank == null || string.IsNullOrEmpty(text)) return 0f;
            float step = 1f / Mathf.Max(1f, bank.charsPerSecond);
            float npcOffset = Mathf.Lerp(bank.npcPitchMinSemitones, bank.npcPitchMaxSemitones, (Crc32(npcId) % 1000) / 999f);
            var rng = new System.Random((int)Crc32(npcId + text));
            string trimmed = text.TrimEnd();
            bool endQuestion = trimmed.EndsWith("?");
            bool endExclaim = trimmed.EndsWith("!");

            var letterIdx = new List<int>();
            for (int i = 0; i < text.Length; i++)
                if (char.IsLetterOrDigit(text[i]) || Symbols.ContainsKey(text[i])) letterIdx.Add(i);

            float t = 0f;
            for (int i = 0; i < text.Length && t <= maxSeconds; i++)
            {
                char ch = text[i];
                char c = char.ToLowerInvariant(ch);
                char letter = '\0';
                if (c >= 'a' && c <= 'z') letter = c;
                else if (Digits.TryGetValue(c, out var d)) letter = d;
                else if (Symbols.TryGetValue(c, out var sy)) letter = sy;

                if (letter == '\0')
                {
                    t += step * (Pauses.TryGetValue(c, out var p) ? p : 0.5f);
                    continue;
                }
                float semis = npcOffset + (float)(rng.NextDouble() * 2.0 - 1.0) * bank.pitchJitterSemitones;
                float gain = 1f;
                if (char.IsUpper(ch)) { semis += 1.5f; gain = 1.12f; }       // CAPS = shouting
                int fromEnd = letterIdx.Count - 1 - letterIdx.IndexOf(i);
                if (endQuestion && fromEnd < 4) semis += (4 - fromEnd);       // questions rise at the end
                if (endExclaim && fromEnd < 3) { semis += 1f; gain *= 1.12f; }
                queue.Add(new Syllable { time = t, letter = letter - 'a', semitones = semis, gain = gain });
                t += step;
            }
            lineStartDsp = AudioSettings.dspTime + scheduleAhead;
            return queue.Count > 0 ? queue[queue.Count - 1].time + 0.2f : 0f;
        }

        public void StopTalking()
        {
            queue.Clear();
            foreach (var v in voices) if (v != null) v.Stop();
        }

        void Update()
        {
            if (queue.Count == 0) return;
            double now = AudioSettings.dspTime;
            float baseVol = Mathf.Pow(10f, bank.volumeDb / 20f);
            // hand over every syllable that starts within the look-ahead window
            while (queue.Count > 0 && lineStartDsp + queue[0].time < now + scheduleAhead)
            {
                var s = queue[0];
                queue.RemoveAt(0);
                var clip = (s.letter >= 0 && s.letter < bank.letters.Length) ? bank.letters[s.letter] : null;
                if (clip == null) continue;                       // missing clip: skip, never crash
                var v = voices[nextVoice];
                nextVoice = (nextVoice + 1) % VoiceCount;
                v.clip = clip;
                v.pitch = Mathf.Pow(2f, s.semitones / 12f);       // resample: pitch and speed together
                v.volume = Mathf.Clamp01(baseVol * s.gain);
                v.PlayScheduled(System.Math.Max(now, lineStartDsp + s.time));
            }
        }

        static uint Crc32(string s)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(s))
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc;
        }
    }
}
