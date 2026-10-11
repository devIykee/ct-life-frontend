using System;
using System.Collections.Generic;

namespace CTLife.Audio
{
    /// <summary>One scheduled babble syllable: start offset (s), letter 0..25 (a..z), pitch (semitones), gain (linear).</summary>
    public struct BabbleSyllable
    {
        public float Time;
        public int Letter;
        public float Semitones;
        public float Gain;
    }

    /// <summary>Per-archetype bank numbers (sfx-map.json `babble.&lt;archetype&gt;`, audio/processed/babble/babble-banks.json).</summary>
    public struct BabbleVoice
    {
        public float CharsPerSecond;
        public float PitchJitterSemitones;
        public float NpcPitchMinSemitones;
        public float NpcPitchMaxSemitones;
        public float VolumeDb;

        public static BabbleVoice Default => new BabbleVoice
        {
            CharsPerSecond = 16f, PitchJitterSemitones = 1.5f, NpcPitchMinSemitones = -2f, NpcPitchMaxSemitones = 2f, VolumeDb = -6f,
        };
    }

    /// <summary>
    /// audio/BABBLE.md as a pure function: walk the line one character at a time (step = 1/cps); a–z, digits and a few
    /// symbols map to syllables; everything else is a pause (space 1 step, ",;:" 2, ".!?" and newline 3, "-" "/" 1,
    /// other 0.5). Pitch = stable per-NPC offset (crc32(npcId)) + seeded jitter (crc32(npcId + line)); UPPERCASE +1.5 st
    /// ×1.12; a trailing "?" raises the last 4 played syllables +1…+4 st (rising to the end); a trailing "!" lifts the
    /// last 3 by +1 st ×1.12. Lines are capped at <see cref="MaxSeconds"/> (the NPC TALK state).
    /// </summary>
    public static class BabbleScheduler
    {
        public const float MaxSeconds = 3f;
        public const int MaxSyllables = 64;

        private static readonly Dictionary<char, char> Digits = new Dictionary<char, char>
        { {'0','o'},{'1','w'},{'2','t'},{'3','r'},{'4','f'},{'5','f'},{'6','s'},{'7','s'},{'8','e'},{'9','n'} };
        private static readonly Dictionary<char, char> Symbols = new Dictionary<char, char>
        { {'$','s'},{'@','a'},{'#','h'},{'%','p'},{'&','n'},{'+','p'} };
        private static readonly Dictionary<char, float> Pauses = new Dictionary<char, float>
        { {' ',1f},{',',2f},{';',2f},{':',2f},{'.',3f},{'!',3f},{'?',3f},{'\n',3f},{'-',1f},{'/',1f} };

        /// <summary>Stable per-NPC pitch offset in semitones.</summary>
        public static float NpcOffset(string npcId, BabbleVoice voice) =>
            AudioMath.Lerp(voice.NpcPitchMinSemitones, voice.NpcPitchMaxSemitones, (AudioMath.Crc32(npcId ?? "") % 1000) / 999f);

        /// <summary>The syllable for a character, or -1 for a pause.</summary>
        public static int LetterOf(char ch)
        {
            char c = char.ToLowerInvariant(ch);
            if (c >= 'a' && c <= 'z') return c - 'a';
            if (Digits.TryGetValue(c, out var d)) return d - 'a';
            if (Symbols.TryGetValue(c, out var s)) return s - 'a';
            return -1;
        }

        public static float PauseSteps(char ch) => Pauses.TryGetValue(ch, out var p) ? p : 0.5f;

        /// <summary>Schedules `text`. Never throws; an empty/null line yields no syllables.</summary>
        public static List<BabbleSyllable> Schedule(string text, string npcId, BabbleVoice voice, float maxSeconds = MaxSeconds)
        {
            var list = new List<BabbleSyllable>();
            if (string.IsNullOrEmpty(text)) return list;
            float cps = float.IsNaN(voice.CharsPerSecond) ? 16f : AudioMath.Clamp(voice.CharsPerSecond, 4f, 40f);
            float step = 1f / cps;
            float npcOffset = NpcOffset(npcId, voice);
            var rng = new Random(unchecked((int)AudioMath.Crc32((npcId ?? "") + text)));
            float jitter = Math.Max(0f, float.IsNaN(voice.PitchJitterSemitones) ? 0f : voice.PitchJitterSemitones);
            float t = 0f;
            foreach (char ch in text)
            {
                if (t > maxSeconds || list.Count >= MaxSyllables) break;
                int letter = LetterOf(ch);
                if (letter < 0) { t += step * PauseSteps(ch); continue; }
                float semis = npcOffset + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                float gain = 1f;
                if (char.IsUpper(ch)) { semis += 1.5f; gain = 1.12f; }   // CAPS = shouting ("JUST IN")
                list.Add(new BabbleSyllable { Time = t, Letter = letter, Semitones = semis, Gain = gain });
                t += step;
            }
            string trimmed = text.TrimEnd();
            int n = list.Count;
            if (trimmed.EndsWith("?"))
                for (int k = 0; k < Math.Min(4, n); k++)
                {
                    var s = list[n - 1 - k];
                    s.Semitones += 4 - k;                                     // last +4, then +3, +2, +1
                    list[n - 1 - k] = s;
                }
            else if (trimmed.EndsWith("!"))
                for (int k = 0; k < Math.Min(3, n); k++)
                {
                    var s = list[n - 1 - k];
                    s.Semitones += 1f;
                    s.Gain *= 1.12f;
                    list[n - 1 - k] = s;
                }
            return list;
        }

        /// <summary>Speech length for bubbles / mouth flaps: last onset + 0.2 s tail (0 when silent).</summary>
        public static float Duration(List<BabbleSyllable> syllables) =>
            syllables == null || syllables.Count == 0 ? 0f : syllables[syllables.Count - 1].Time + 0.2f;
    }

    /// <summary>
    /// Babble rate limits: at most <see cref="MaxTalkers"/> NPCs babbling at once around the listener (nearest win; a
    /// nearer new line preempts the farthest talker), one line per NPC per <see cref="MinLineInterval"/> seconds, and
    /// none beyond <see cref="MaxDistance"/> metres (the speech bubble still shows; only the sound is skipped).
    /// </summary>
    public sealed class BabbleBudget
    {
        public const int MaxTalkers = 3;
        public const float MinLineInterval = 1.5f;
        public const float MaxDistance = 15f;

        public enum Verdict { Deny, Allow, AllowPreempt }

        public struct Decision
        {
            public Verdict Verdict;
            /// <summary>For AllowPreempt: the talker to stop.</summary>
            public string Preempt;
        }

        private sealed class Talker { public double Until; public float Distance; }

        private readonly Dictionary<string, Talker> _active = new Dictionary<string, Talker>();
        private readonly Dictionary<string, double> _lastLine = new Dictionary<string, double>();
        private readonly List<string> _ended = new List<string>();

        public int Active(double now)
        {
            Expire(now);
            return _active.Count;
        }

        public Decision Request(string npcId, float distance, float lineSeconds, double now)
        {
            var deny = new Decision { Verdict = Verdict.Deny };
            if (string.IsNullOrEmpty(npcId) || float.IsNaN(distance) || distance > MaxDistance || lineSeconds <= 0f) return deny;
            Expire(now);
            if (_lastLine.TryGetValue(npcId, out var last) && now - last < MinLineInterval && now >= last) return deny;
            string preempt = null;
            if (!_active.ContainsKey(npcId) && _active.Count >= MaxTalkers)
            {
                float farthest = -1f;
                foreach (var kv in _active)
                    if (kv.Value.Distance > farthest) { farthest = kv.Value.Distance; preempt = kv.Key; }
                if (preempt == null || farthest <= distance) return deny;
                _active.Remove(preempt);
            }
            _active[npcId] = new Talker { Until = now + lineSeconds, Distance = distance };
            _lastLine[npcId] = now;
            return new Decision { Verdict = preempt != null ? Verdict.AllowPreempt : Verdict.Allow, Preempt = preempt };
        }

        public void Stopped(string npcId)
        {
            if (npcId != null) _active.Remove(npcId);
        }

        private void Expire(double now)
        {
            _ended.Clear();
            foreach (var kv in _active) if (kv.Value.Until <= now) _ended.Add(kv.Key);
            foreach (var k in _ended) _active.Remove(k);
        }
    }
}
