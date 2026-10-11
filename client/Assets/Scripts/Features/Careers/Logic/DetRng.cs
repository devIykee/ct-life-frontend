using System;
using System.Collections.Generic;
using System.Text;

namespace CTLife.Careers
{
    /// <summary>
    /// Deterministic 32-bit PRNG (Mulberry32). Chosen because it is tiny, has no platform-dependent behaviour (unlike
    /// System.Random's implementation history) and ports to TypeScript in four lines with Math.imul, so the server can
    /// regenerate the same round content from the same seed (FE proposal in docs/fe-contract-proposals/careers-minigames-20261008.md).
    /// Not cryptographic. Never used for anything with real-money value.
    /// </summary>
    public sealed class DetRng
    {
        private uint _state;

        public DetRng(int seed) { _state = unchecked((uint)seed); }

        public uint NextUInt()
        {
            unchecked
            {
                _state += 0x6D2B79F5u;
                uint t = _state;
                t = (t ^ (t >> 15)) * (t | 1u);
                t = (t + ((t ^ (t >> 7)) * (t | 61u))) ^ t;
                return t ^ (t >> 14);
            }
        }

        /// <summary>[0, maxExclusive). Multiply-shift mapping; the bias is below 1e-9 for the small ranges used here.</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(((ulong)NextUInt() * (ulong)maxExclusive) >> 32);
        }

        /// <summary>[minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return minInclusive + Next(maxExclusive - minInclusive);
        }

        /// <summary>[0, 1).</summary>
        public double NextDouble() => NextUInt() / 4294967296.0;

        public double Between(double min, double max) => min + (max - min) * NextDouble();

        public bool Chance(double probability) => NextDouble() < probability;

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public T Pick<T>(IReadOnlyList<T> list)
        {
            if (list == null || list.Count == 0) throw new ArgumentException("Empty list", nameof(list));
            return list[Next(list.Count)];
        }

        /// <summary>A shuffled copy of up to `count` distinct elements.</summary>
        public List<T> Sample<T>(IReadOnlyList<T> list, int count)
        {
            var copy = new List<T>(list);
            Shuffle(copy);
            if (count < copy.Count) copy.RemoveRange(count, copy.Count - count);
            return copy;
        }

        /// <summary>A permutation of 0..n-1 that is never the identity when n &gt; 1 (so a "shuffled" board is never pre-solved).</summary>
        public int[] Derangement(int n)
        {
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            if (n <= 1) return order;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                for (int i = n - 1; i > 0; i--)
                {
                    int j = Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }
                bool identity = true;
                for (int i = 0; i < n && identity; i++) identity = order[i] == i;
                if (!identity) return order;
            }
            // Deterministic fallback: rotate by one.
            for (int i = 0; i < n; i++) order[i] = (i + 1) % n;
            return order;
        }
    }

    /// <summary>
    /// Seeds derived from the server's shift so a round is reproducible from (shiftId, miniGameId, index) alone.
    /// FNV-1a 32 over UTF-8 of `ctlife.mg.v1|{shiftId}|{miniGameId}|{index}`.
    /// </summary>
    public static class MiniGameSeed
    {
        public const string Version = "ctlife.mg.v1";

        public static uint Fnv1a(string text)
        {
            unchecked
            {
                uint h = 0x811C9DC5u;
                foreach (var b in Encoding.UTF8.GetBytes(text ?? ""))
                {
                    h ^= b;
                    h *= 0x01000193u;
                }
                return h;
            }
        }

        public static int ForShift(string shiftId, string miniGameId, int index) =>
            unchecked((int)Fnv1a(Version + "|" + (shiftId ?? "") + "|" + (miniGameId ?? "") + "|" + index));

        /// <summary>Practice rounds: a fresh seed per attempt (the nonce is the caller's counter or tick).</summary>
        public static int ForPractice(string miniGameId, long nonce) =>
            unchecked((int)Fnv1a(Version + "|practice|" + (miniGameId ?? "") + "|" + nonce.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}
