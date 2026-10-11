using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.Audio
{
    /// <summary>
    /// NPC voice is babble, not TTS (Build Spec "Dialogue"): pitched syllables generated from the text, one syllable
    /// set per archetype, pitched per NPC. Uses the audio agent's syllable clips when sfx-map.json has a `babble`
    /// set for the archetype (or "default"); otherwise synthesizes short formant-ish blips so it works with no assets.
    /// </summary>
    public static class Babble
    {
        private static readonly Dictionary<int, AudioClip> _synth = new Dictionary<int, AudioClip>();

        /// <summary>Plays babble for `text` on `source`. Returns the coroutine so callers can stop it.</summary>
        public static Coroutine Speak(MonoBehaviour host, AudioSource source, string text, string archetypeId, float basePitch)
        {
            if (host == null || source == null || string.IsNullOrEmpty(text)) return null;
            return host.StartCoroutine(SpeakRoutine(source, text, archetypeId, basePitch));
        }

        private static IEnumerator SpeakRoutine(AudioSource source, string text, string archetypeId, float basePitch)
        {
            var am = AudioManager.Instance;
            List<string> files = null;
            if (am != null)
            {
                if (archetypeId != null && am.Map.Babble.TryGetValue(archetypeId, out var e)) files = e.files;
                else if (am.Map.Babble.TryGetValue("default", out var d)) files = d.files;
            }

            int syllables = Mathf.Clamp(CountSyllables(text), 2, 14);
            var rng = new System.Random(text.GetHashCode());
            for (int i = 0; i < syllables; i++)
            {
                AudioClip clip = null;
                if (files != null && files.Count > 0) clip = am.Clip(files[rng.Next(files.Count)]);
                if (clip == null) clip = SynthSyllable(rng.Next(4));
                source.pitch = basePitch * (0.92f + (float)rng.NextDouble() * 0.16f);
                source.PlayOneShot(clip, am != null ? am.sfxVolume * 0.6f : 0.6f);
                yield return new WaitForSeconds(0.075f + (float)rng.NextDouble() * 0.03f);
            }
            source.pitch = 1f;
        }

        private static int CountSyllables(string text)
        {
            int n = 0;
            bool inVowel = false;
            foreach (var ch in text.ToLowerInvariant())
            {
                bool v = "aeiouy".IndexOf(ch) >= 0;
                if (v && !inVowel) n++;
                inVowel = v;
            }
            return n;
        }

        private static AudioClip SynthSyllable(int variant)
        {
            if (_synth.TryGetValue(variant, out var c)) return c;
            float[] formant = { 520f, 660f, 440f, 780f };
            c = MakeBlip(formant[variant % formant.Length], 0.07f, harmonics: true);
            _synth[variant] = c;
            return c;
        }

        /// <summary>Short enveloped tone. Used as the default UI blip and babble fallback.</summary>
        public static AudioClip MakeBlip(float freq, float seconds, bool harmonics = false)
        {
            const int rate = 22050;
            int n = Mathf.CeilToInt(rate * seconds);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Sin(Mathf.PI * i / n);
                float s = Mathf.Sin(2 * Mathf.PI * freq * t);
                if (harmonics) s = 0.6f * s + 0.3f * Mathf.Sin(2 * Mathf.PI * freq * 2.01f * t) + 0.1f * Mathf.Sin(2 * Mathf.PI * freq * 3.02f * t);
                data[i] = s * env * 0.5f;
            }
            var clip = AudioClip.Create($"blip_{freq:0}", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
