using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace CTLife.Core
{
    /// <summary>Bitcoin-alphabet Base58 (Solana addresses). Used for development wallets; the SDK does real keys.</summary>
    public static class Base58
    {
        private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        public static string Encode(byte[] data)
        {
            var value = new BigInteger(Reverse(data, appendZero: true));
            var sb = new StringBuilder();
            while (value > 0)
            {
                value = BigInteger.DivRem(value, 58, out var rem);
                sb.Insert(0, Alphabet[(int)rem]);
            }
            for (int i = 0; i < data.Length && data[i] == 0; i++) sb.Insert(0, '1');
            return sb.ToString();
        }

        public static byte[] Decode(string s)
        {
            if (string.IsNullOrEmpty(s)) return Array.Empty<byte>();
            BigInteger value = 0;
            foreach (var c in s)
            {
                int digit = Alphabet.IndexOf(c);
                if (digit < 0) throw new FormatException("Invalid base58 character: " + c);
                value = value * 58 + digit;
            }
            var bytes = new List<byte>(value.ToByteArray());
            bytes.Reverse();
            while (bytes.Count > 0 && bytes[0] == 0) bytes.RemoveAt(0);
            int leading = 0;
            while (leading < s.Length && s[leading] == '1') leading++;
            var result = new byte[leading + bytes.Count];
            bytes.CopyTo(result, leading);
            return result;
        }

        /// <summary>True when the string decodes to exactly 32 bytes (the size of a Solana public key).</summary>
        public static bool IsPublicKeyLength(string s)
        {
            try { return Decode(s).Length == 32; }
            catch (FormatException) { return false; }
        }

        private static byte[] Reverse(byte[] data, bool appendZero)
        {
            var r = new byte[data.Length + (appendZero ? 1 : 0)];
            for (int i = 0; i < data.Length; i++) r[i] = data[data.Length - 1 - i];
            return r;
        }
    }
}
