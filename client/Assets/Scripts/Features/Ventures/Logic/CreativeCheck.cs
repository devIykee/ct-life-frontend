using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.Ventures
{
    public enum CreativeProblem { None, Empty, TooLarge, NotPngOrJpeg, MimeNotAccepted, UnreadableHeader, DecodeTooBig }

    /// <summary>Creative limits from contract config `ads.json` (`creative.maxSizeMB`, `acceptMime`); defaults are the contract's.</summary>
    public sealed class CreativeLimits
    {
        public const long DefaultMaxBytes = 1_048_576;
        /// <summary>Same decoded-size caps as WorldLife's AdImageCache (a small PNG can declare 30000 x 30000).</summary>
        public const int MaxDecodeSide = 4096;
        public const long MaxDecodePixels = 8L * 1024 * 1024;

        public long MaxBytes = DefaultMaxBytes;
        public HashSet<string> AcceptMime = new HashSet<string>(StringComparer.Ordinal) { "image/png", "image/jpeg" };

        public static CreativeLimits FromConfig(JToken ads)
        {
            var l = new CreativeLimits();
            var c = ads?["creative"];
            if (c == null) return l;
            var mb = VWire.DoubleOr(c["maxSizeMB"], 0);
            if (mb > 0 && mb <= 16) l.MaxBytes = (long)(mb * 1024 * 1024);
            if (c["acceptMime"] is JArray a)
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var m in a)
                {
                    var s = VWire.Str(m);
                    // Only the formats the checks below can verify; a wider server list never widens the client.
                    if (s == "image/png" || s == "image/jpeg") set.Add(s);
                }
                if (set.Count > 0) l.AcceptMime = set;
            }
            return l;
        }
    }

    public sealed class CreativeResult
    {
        public CreativeProblem Problem;
        public bool Ok => Problem == CreativeProblem.None;
        public string FileName, Mime;
        public long Bytes;
        public int Width, Height;
        /// <summary>The slot publishes a creative size and this image has a different aspect ratio (a warning; the server reviews).</summary>
        public bool AspectWarning;
        public int SlotWidth, SlotHeight;
    }

    /// <summary>
    /// Client-side check of an ad creative before upload: size, PNG/JPEG magic bytes, declared pixel size from the header (no
    /// decoding), decode caps, and the slot's published aspect ratio. The server re-checks everything (EXIF is stripped there).
    /// </summary>
    public static class CreativeCheck
    {
        public static bool IsPng(byte[] b) => b != null && b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
        public static bool IsJpeg(byte[] b) => b != null && b.Length > 8 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

        public static bool IsDecodeSizeOk(int w, int h) =>
            w > 0 && h > 0 && w <= CreativeLimits.MaxDecodeSide && h <= CreativeLimits.MaxDecodeSide && (long)w * h <= CreativeLimits.MaxDecodePixels;

        public static CreativeResult Check(byte[] data, string fileName, CreativeLimits limits, int slotW = 0, int slotH = 0)
        {
            limits = limits ?? new CreativeLimits();
            var r = new CreativeResult { FileName = VWire.Plain(fileName, 60), Bytes = data?.LongLength ?? 0, SlotWidth = slotW, SlotHeight = slotH };
            if (data == null || data.Length == 0) { r.Problem = CreativeProblem.Empty; return r; }
            if (data.LongLength > limits.MaxBytes) { r.Problem = CreativeProblem.TooLarge; return r; }
            if (IsPng(data)) r.Mime = "image/png";
            else if (IsJpeg(data)) r.Mime = "image/jpeg";
            else { r.Problem = CreativeProblem.NotPngOrJpeg; return r; }
            if (!limits.AcceptMime.Contains(r.Mime)) { r.Problem = CreativeProblem.MimeNotAccepted; return r; }
            if (!TryReadSize(data, out var w, out var h)) { r.Problem = CreativeProblem.UnreadableHeader; return r; }
            r.Width = w;
            r.Height = h;
            if (!IsDecodeSizeOk(w, h)) { r.Problem = CreativeProblem.DecodeTooBig; return r; }
            if (slotW > 0 && slotH > 0)
            {
                double have = (double)w / h, want = (double)slotW / slotH;
                r.AspectWarning = Math.Abs(have - want) / want > 0.02;
            }
            return r;
        }

        /// <summary>Declared width and height from the PNG IHDR chunk or the first JPEG SOFn frame header. False when missing or malformed.</summary>
        public static bool TryReadSize(byte[] b, out int width, out int height)
        {
            width = height = 0;
            if (b == null || b.Length < 24) return false;
            if (IsPng(b))
            {
                if (b[12] != (byte)'I' || b[13] != (byte)'H' || b[14] != (byte)'D' || b[15] != (byte)'R') return false;
                width = b[16] << 24 | b[17] << 16 | b[18] << 8 | b[19];
                height = b[20] << 24 | b[21] << 16 | b[22] << 8 | b[23];
                return width > 0 && height > 0;
            }
            if (!IsJpeg(b)) return false;
            int i = 2;
            while (i + 4 <= b.Length)
            {
                if (b[i] != 0xFF) return false;
                while (i < b.Length && b[i] == 0xFF) i++;
                if (i >= b.Length) return false;
                int m = b[i++];
                if (m == 0x01 || m >= 0xD0 && m <= 0xD9) continue;
                if (i + 2 > b.Length) return false;
                int len = b[i] << 8 | b[i + 1];
                if (len < 2) return false;
                bool sof = m >= 0xC0 && m <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC;
                if (sof)
                {
                    if (i + 7 > b.Length) return false;
                    height = b[i + 3] << 8 | b[i + 4];
                    width = b[i + 5] << 8 | b[i + 6];
                    return width > 0 && height > 0;
                }
                i += len;
            }
            return false;
        }

        /// <summary>`https` only, at most 2048 characters, no spaces or control characters. Empty is allowed (the link is optional). Stored by the server, never fetched.</summary>
        public static bool IsAllowedLink(string link)
        {
            if (string.IsNullOrEmpty(link)) return true;
            if (link.Length > 2048 || !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || link.Length <= 8) return false;
            if (link.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return false;
            return Uri.TryCreate(link, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(u.Host);
        }

        /// <summary>FNV-1a over the bytes: identifies "the same creative" for retained upload keys without keeping a second copy.</summary>
        public static string Fingerprint(byte[] data)
        {
            if (data == null) return "0";
            ulong h = 14695981039346656037UL;
            foreach (var b in data) { h ^= b; h *= 1099511628211UL; }
            return data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + h.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Reads a creative from a local path with a hard size bound, only from roots the caller allows.</summary>
    public static class CreativeFile
    {
        public static bool IsUnderAny(string path, IEnumerable<string> roots)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { return false; }
            foreach (var root in roots ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(root)) continue;
                var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(r, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>Returns the bytes, or null with <paramref name="problem"/> set. Never reads a file larger than the limit.</summary>
        public static byte[] ReadBounded(string path, long maxBytes, out CreativeProblem problem)
        {
            problem = CreativeProblem.None;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0) { problem = CreativeProblem.Empty; return null; }
                if (info.Length > maxBytes) { problem = CreativeProblem.TooLarge; return null; }
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var buf = new byte[info.Length];
                    int read = 0;
                    while (read < buf.Length)
                    {
                        int n = fs.Read(buf, read, buf.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read != buf.Length) { problem = CreativeProblem.Empty; return null; }
                    return buf;
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                problem = CreativeProblem.Empty;
                return null;
            }
        }
    }
}
