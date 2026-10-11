using System;

namespace CTLife.Audio
{
    /// <summary>
    /// Pure helpers shared by the audio logic. No UnityEngine dependency, so every rule in Logic/ runs in the
    /// out-of-editor test runner (Tests~/run_audio_tests.sh) exactly as in the player.
    /// </summary>
    public static class AudioMath
    {
        public const float SilenceDb = -80f;

        public static float Clamp01(float v) => float.IsNaN(v) ? 0f : v < 0f ? 0f : v > 1f ? 1f : v;
        public static float Clamp(float v, float lo, float hi) => float.IsNaN(v) ? lo : v < lo ? lo : v > hi ? hi : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        /// <summary>0 at a, 1 at b; works for a &gt; b (descending ranges). Clamped.</summary>
        public static float InverseLerp(float a, float b, float v)
        {
            if (Math.Abs(b - a) < 1e-9f) return v >= b ? 1f : 0f;
            return Clamp01((v - a) / (b - a));
        }

        /// <summary>Hermite ease of an already 0..1 value.</summary>
        public static float SmoothStep01(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (maxDelta <= 0f) return current;
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Math.Sign(target - current) * maxDelta;
        }

        public static float DbToLinear(float db) => db <= SilenceDb ? 0f : (float)Math.Pow(10.0, db / 20.0);

        public static float LinearToDb(float linear) => linear <= 1e-4f ? SilenceDb : (float)(20.0 * Math.Log10(linear));

        /// <summary>Logarithmic interpolation for frequencies (equal musical steps).</summary>
        public static float LogLerp(float a, float b, float t)
        {
            t = Clamp01(t);
            return (float)Math.Exp(Math.Log(a) + (Math.Log(b) - Math.Log(a)) * t);
        }

        public static float SemitonesToPitch(float semitones) => (float)Math.Pow(2.0, semitones / 12.0);

        /// <summary>CRC-32 (IEEE, reflected) of the UTF-8 bytes: the stable per-NPC seed audio/BABBLE.md specifies.</summary>
        public static uint Crc32(string s)
        {
            uint crc = 0xFFFFFFFFu;
            if (s != null)
                foreach (byte b in System.Text.Encoding.UTF8.GetBytes(s))
                {
                    crc ^= b;
                    for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            return ~crc;
        }
    }
}
