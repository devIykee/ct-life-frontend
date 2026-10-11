using System;

namespace CTLife.Core
{
    /// <summary>Minimal semver compare for `minClientVersion` (contract section 7).</summary>
    public static class Semver
    {
        public const string ClientVersion = "0.1.0";
        public const string ContractVersion = "1.0.0";

        /// <summary>Returns -1, 0 or 1. Pre-release suffixes are ignored.</summary>
        public static int Compare(string a, string b)
        {
            var pa = Parse(a);
            var pb = Parse(b);
            for (int i = 0; i < 3; i++)
            {
                if (pa[i] != pb[i]) return pa[i] < pb[i] ? -1 : 1;
            }
            return 0;
        }

        private static int[] Parse(string v)
        {
            var r = new int[3];
            if (string.IsNullOrEmpty(v)) return r;
            var core = v.Split('-', '+')[0].Split('.');
            for (int i = 0; i < 3 && i < core.Length; i++)
                int.TryParse(core[i], out r[i]);
            return r;
        }

        public static bool IsOutdated(string minClientVersion) =>
            !string.IsNullOrEmpty(minClientVersion) && Compare(ClientVersion, minClientVersion) < 0;
    }
}
