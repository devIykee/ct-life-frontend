using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// CT post formats a Timeline post can be recognised as (display badges and composer starters only; the contract's
    /// `FeedPost` is plain text). From the research notes (docs/research/ct-now-2026-10.md §2b and the 2026-10-04
    /// observations): "JUST IN:"/"BREAKING:" news-bot posts (now also prediction-market odds, "68% chance"), X threads ("a
    /// series of connected posts from one person"), caller posts with a ticker, PnL cards, liquidation clips
    /// ("+$40,000 → liquidated"), wallet-tracker legends ("turned $1K into $15M", "retired off one trade"), trader journals
    /// ("Day 605: ..."), gm, and the giveaway template.
    /// </summary>
    public enum PostKind { Plain, Gm, Thread, Call, Pnl, News, Rekt, Journal, Tracker, Giveaway }

    public static class PostKinds
    {
        private static readonly Regex Ticker = new Regex(@"(?<![A-Za-z0-9])\$[A-Z][A-Z0-9]{1,9}\b", RegexOptions.CultureInvariant);
        private static readonly Regex ThreadMark = new Regex(@"(🧵|\b1/\d{0,2}\b|\(1/\d{0,2}\)|\bthread\b)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex Money = new Regex(@"[+-]\s?\$\s?\d", RegexOptions.CultureInvariant);
        private static readonly Regex Journal = new Regex(@"^\s*day\s+\d{1,4}\s*[:.-]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex Turned = new Regex(@"(turned|flipped)\s+\$?\d[\d,.]*\s*[km]?\s+into\s+\$?\d", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex Odds = new Regex(@"\d{1,3}\s?%\s+chance", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static PostKind Detect(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return PostKind.Plain;
            var t = text.Trim();
            var lower = t.ToLowerInvariant();
            if (ScamSignals.Detect(t) == ScamSignals.Flag.Giveaway) return PostKind.Giveaway;
            if (lower.StartsWith("just in", StringComparison.Ordinal) || lower.StartsWith("breaking", StringComparison.Ordinal) || Odds.IsMatch(t)) return PostKind.News;
            if (Journal.IsMatch(t)) return PostKind.Journal;
            if (ThreadMark.IsMatch(t)) return PostKind.Thread;
            if (lower.Contains("liquidated") || lower.Contains("rekt") || lower.Contains("got rugged")) return PostKind.Rekt;
            if (Turned.IsMatch(t) || lower.Contains("retired off") || lower.Contains("retires off")) return PostKind.Tracker;
            if (lower.Contains("pnl") || (Money.IsMatch(t) && Ticker.IsMatch(t))) return PostKind.Pnl;
            if (Ticker.IsMatch(t) && (lower.Contains("entry") || lower.Contains("target") || lower.Contains("call") || lower.Contains("aping") || lower.Contains("ca:") || lower.Contains("sending"))) return PostKind.Call;
            if (Regex.IsMatch(lower, @"^(gm|gn|gmgm)\b")) return PostKind.Gm;
            return PostKind.Plain;
        }

        public static IReadOnlyList<string> Tickers(string text) =>
            string.IsNullOrEmpty(text) ? Array.Empty<string>() : Ticker.Matches(text).Cast<Match>().Select(m => m.Value).Distinct().Take(5).ToList();

        /// <summary>
        /// Composer starters (SocialStrings keys `feed.starter.*`). Fictional tickers and parody names only; a starter is
        /// editable text, not a structured post type (proposal for the contract: `kind`, `coinId`, `replyToPostId`).
        /// </summary>
        public static readonly PostKind[] Starters = { PostKind.News, PostKind.Thread, PostKind.Call, PostKind.Pnl, PostKind.Rekt, PostKind.Tracker, PostKind.Journal, PostKind.Gm };
    }

    /// <summary>The Timeline list for one scope: newest first, deduplicated, blocked authors hidden. Pure; tested.</summary>
    public sealed class FeedTimeline
    {
        public const int MaxLength = 280;

        private readonly List<FeedPostRow> _posts = new List<FeedPostRow>();
        private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string Scope { get; private set; }
        public bool Loaded { get; private set; }

        public FeedTimeline(string scope) { Scope = scope == "following" ? "following" : "global"; }

        public IReadOnlyList<FeedPostRow> Visible(Func<string, bool> blocked) =>
            _posts.Where(p => !(blocked?.Invoke(p.UserId) ?? false)).ToList();

        /// <summary>Replaces the list with a fresh page (the server is the source of truth); returns new post count.</summary>
        public int Replace(IEnumerable<FeedPostRow> page)
        {
            var before = new HashSet<string>(_ids, StringComparer.OrdinalIgnoreCase);
            _posts.Clear(); _ids.Clear();
            foreach (var p in page ?? Enumerable.Empty<FeedPostRow>()) Add(p);
            Loaded = true;
            return _ids.Count(id => !before.Contains(id));
        }

        public bool Add(FeedPostRow p)
        {
            if (p == null || string.IsNullOrEmpty(p.PostId) || !_ids.Add(p.PostId)) return false;
            int i = 0;
            while (i < _posts.Count && Compare(_posts[i], p) <= 0) i++;
            _posts.Insert(i, p);
            return true;
        }

        private static int Compare(FeedPostRow a, FeedPostRow b)
        {
            int c = b.AtMs.CompareTo(a.AtMs); // newest first
            return c != 0 ? c : string.CompareOrdinal(b.PostId, a.PostId);
        }

        /// <summary>POST /feed/posts text check (≤ 280 UTF-16 units, like the server's JS length). Returns a key or null.</summary>
        public static string Validate(string input, out string text)
        {
            text = (input ?? "").Trim();
            if (text.Length == 0) return "feed.empty";
            if (text.Length > MaxLength) return "feed.tooLong";
            return null;
        }

        public static int Remaining(string input) => MaxLength - (input ?? "").Trim().Length;
    }

    /// <summary>
    /// Follows and blocks known to this device. The contract has no `GET` for either list, so the client remembers what
    /// it did (and what the server confirmed). Serialized as one line per id for PlayerPrefs.
    /// </summary>
    public sealed class IdSet
    {
        private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public event Action Changed;

        public bool Contains(string id) => !string.IsNullOrEmpty(id) && _ids.Contains(id.Trim());
        public int Count => _ids.Count;
        public IEnumerable<string> Items => _ids;

        public bool Set(string id, bool on)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            var changed = on ? _ids.Add(id.Trim()) : _ids.Remove(id.Trim());
            if (changed) Changed?.Invoke();
            return changed;
        }

        public string Serialize() => string.Join("\n", _ids.OrderBy(x => x, StringComparer.Ordinal));

        public void Load(string text)
        {
            _ids.Clear();
            foreach (var line in (text ?? "").Split('\n'))
                if (SocialWire.IsSafeId(line.Trim())) _ids.Add(line.Trim());
        }
    }

    /// <summary>Tip input rules (feature #28). Server values win; these only stop unsendable input.</summary>
    public static class TipRules
    {
        /// <summary>
        /// The contract says the daily Cash tip cap "comes from config", but rc1's economy.json has no such key.
        /// [Proposal] `economy.tipCapCentsPerDay` (mirrors `topUpCapCentsPerDay`); read when present, else the server decides.
        /// </summary>
        public const string CapKey = "tipCapCentsPerDay";

        public static string CashError(string input, long? balanceCents, long? capCents, out long cents)
        {
            if (!CashInput.TryCents(input, out cents)) return "tip.badAmount";
            if (capCents.HasValue && capCents.Value > 0 && cents > capCents.Value) return "tip.overCap";
            if (balanceCents.HasValue && cents > balanceCents.Value) return "tip.overBalance";
            return null;
        }

        /// <summary>A Solana transaction signature: base58 of 64 bytes (86–88 characters).</summary>
        public static bool IsSignature(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.Length < 80 || s.Length > 90) return false;
            var bytes = Base58Lite.Decode(s);
            return bytes != null && bytes.Length == 64;
        }
    }

    /// <summary>Bitcoin-alphabet base58 decode (pure, local so the Logic layer has no UnityEngine dependency).</summary>
    public static class Base58Lite
    {
        private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        public static byte[] Decode(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var value = System.Numerics.BigInteger.Zero;
            foreach (var c in s)
            {
                int d = Alphabet.IndexOf(c);
                if (d < 0) return null;
                value = value * 58 + d;
            }
            var bytes = value.ToByteArray(); // little-endian, may have a sign byte
            int len = bytes.Length;
            if (len > 1 && bytes[len - 1] == 0) len--;
            var body = new byte[value.IsZero ? 0 : len];
            for (int i = 0; i < body.Length; i++) body[i] = bytes[body.Length - 1 - i];
            int zeros = s.TakeWhile(c => c == '1').Count();
            var result = new byte[zeros + body.Length];
            Array.Copy(body, 0, result, zeros, body.Length);
            return result;
        }
    }

    public static class Ago
    {
        public static string Short(long atMs, double nowMs)
        {
            if (atMs <= 0) return "";
            var s = Math.Max(0, (nowMs - atMs) / 1000.0);
            if (s < 60) return "now";
            if (s < 3600) return ((int)(s / 60)).ToString(CultureInfo.InvariantCulture) + "m";
            if (s < 86400) return ((int)(s / 3600)).ToString(CultureInfo.InvariantCulture) + "h";
            return ((int)(s / 86400)).ToString(CultureInfo.InvariantCulture) + "d";
        }

        public static string Clock(double seconds)
        {
            var t = (int)Math.Max(0, seconds);
            return (t / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (t % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
