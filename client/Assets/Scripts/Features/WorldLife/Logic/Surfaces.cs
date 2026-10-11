using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace CTLife.WorldLife
{
    // ------------------------------------------------------------------ Timeline screen feed

    public sealed class TimelineEntry
    {
        public string Key;
        /// <summary>event | post | npc</summary>
        public string Kind;
        public string Author;
        public string Text;
        public double At;
    }

    /// <summary>
    /// What the Timeline screen in Timeline Square shows: live city events (the same `event` messages the incident card
    /// uses), lines NPCs post (AI Agent, Sleuth, Thread Boy…) and `GET /feed?scope=global` posts. Bounded, de-duplicated,
    /// newest first; <see cref="Version"/> changes only when the visible list changes so the screen repaints rarely.
    /// </summary>
    public sealed class TimelineFeed
    {
        public const int Limit = 24;
        public const int TextMax = 140;
        private readonly List<TimelineEntry> _entries = new List<TimelineEntry>();
        private readonly HashSet<string> _keys = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<TimelineEntry> Entries => _entries;
        public int Version { get; private set; }

        public bool Add(TimelineEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.Key) || _keys.Contains(e.Key)) return false;
            if (double.IsNaN(e.At) || double.IsInfinity(e.At)) return false;
            var text = WireText.Clean(e.Text, TextMax);
            if (text == null) return false;
            var entry = new TimelineEntry { Key = e.Key, Kind = e.Kind ?? "post", Author = WireText.Clean(e.Author, 24), Text = text, At = e.At };
            // Older than everything in a full list: not shown, not remembered.
            if (_entries.Count >= Limit && entry.At <= _entries[_entries.Count - 1].At) return false;
            _entries.Add(entry);
            _keys.Add(entry.Key);
            _entries.Sort((a, b) => b.At != a.At ? b.At.CompareTo(a.At) : string.CompareOrdinal(a.Key, b.Key));
            while (_entries.Count > Limit)
            {
                _keys.Remove(_entries[_entries.Count - 1].Key);
                _entries.RemoveAt(_entries.Count - 1);
            }
            Version++;
            return true;
        }

        /// <summary>`GET /feed` body: contract `FeedPost[]` or backend `{posts: FeedPost[]}`. Returns the number added.</summary>
        public int MergePosts(JToken body)
        {
            var arr = body as JArray ?? body?["posts"] as JArray;
            if (arr == null) return 0;
            int added = 0;
            foreach (var p in arr)
            {
                if (!(p is JObject)) continue;
                var id = WireText.Str(p, "postId");
                var at = ParseTime(p["at"] ?? p["createdAt"]);
                if (string.IsNullOrEmpty(id) || at <= 0) continue;
                if (Add(new TimelineEntry { Key = "post:" + id, Kind = "post", Author = WireText.Str(p, "alias"), Text = WireText.Str(p, "text"), At = at }))
                    added++;
            }
            return added;
        }

        /// <summary>Rendered rows: "@alias: text" for posts, plain text for events and NPC posts.</summary>
        public List<string> Lines(int max, int maxChars)
        {
            var lines = new List<string>();
            foreach (var e in _entries)
            {
                if (lines.Count >= max) break;
                var row = e.Kind == "post" || e.Kind == "npc" ? "@" + (e.Author ?? "anon") + ": " + e.Text : e.Text;
                if (row.Length > maxChars) row = row.Substring(0, Math.Max(1, maxChars - 1)) + "…";
                lines.Add(row);
            }
            return lines;
        }

        public void Clear() { _entries.Clear(); _keys.Clear(); Version++; }

        /// <summary>ISO-8601 string or epoch milliseconds → epoch ms; 0 when unusable.</summary>
        public static double ParseTime(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return 0;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) return t.Value<double>();
            if (t.Type == JTokenType.Date) return new DateTimeOffset(t.Value<DateTime>().ToUniversalTime()).ToUnixTimeMilliseconds();
            var s = t.ToString();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)) return ms;
            return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
                ? dto.ToUnixTimeMilliseconds() : 0;
        }
    }

    // ------------------------------------------------------------------ live ads

    public sealed class LiveCreative
    {
        public string SlotId;
        public string BookingId;
        public string ImageUrl;
        public string Link;
        /// <summary>Always true for /ads/live creatives: the spec requires a "Sponsored" label on every paid placement.</summary>
        public bool Sponsored = true;
    }

    public static class LiveAds
    {
        public const int UrlMax = 2048;

        /// <summary>
        /// `GET /ads/live`. Accepts the contract shape (`{slotId: {slotId, imageUrl, link, sponsored}}`), the backend
        /// 9f3e5ef shape (`{ads: [{slotId, bookingId, link, imageKey, mimeType}]}`, an S3 key, not a URL) and a bare array.
        /// Creatives without a usable https image URL are rejected (counted) and the slot falls back to house ads.
        /// </summary>
        public static Dictionary<string, List<LiveCreative>> Parse(JToken body, Func<string, string> imageKeyToUrl, bool allowHttp, out int rejected)
        {
            rejected = 0;
            var map = new Dictionary<string, List<LiveCreative>>(StringComparer.Ordinal);
            var rows = new List<(string slotHint, JToken row)>();
            if (body is JArray arr) foreach (var r in arr) rows.Add((null, r));
            else if (body?["ads"] is JArray ads) foreach (var r in ads) rows.Add((null, r));
            else if (body is JObject o)
                foreach (var p in o.Properties())
                {
                    if (p.Value is JArray list) foreach (var r in list) rows.Add((p.Name, r));
                    else rows.Add((p.Name, p.Value));
                }
            foreach (var (hint, row) in rows)
            {
                if (!(row is JObject)) { rejected++; continue; }
                var slot = WireText.Str(row, "slotId", hint);
                var url = WireText.Str(row, "imageUrl") ?? WireText.Str(row, "url");
                if (url == null)
                {
                    var key = WireText.Str(row, "imageKey");
                    if (key != null && imageKeyToUrl != null) url = imageKeyToUrl(key);
                }
                if (string.IsNullOrEmpty(slot) || !IsSafeId(slot) || !IsImageUrl(url, allowHttp)) { rejected++; continue; }
                var c = new LiveCreative
                {
                    SlotId = slot, BookingId = WireText.Str(row, "bookingId"), ImageUrl = url,
                    Link = IsImageUrl(WireText.Str(row, "link"), false) ? WireText.Str(row, "link") : null,
                };
                if (!map.TryGetValue(slot, out var l)) map[slot] = l = new List<LiveCreative>();
                l.Add(c);
            }
            foreach (var l in map.Values) l.Sort((a, b) => string.CompareOrdinal(a.BookingId ?? a.ImageUrl, b.BookingId ?? b.ImageUrl));
            return map;
        }

        /// <summary>PNG or JPEG magic bytes (the contract accepts only image/png and image/jpeg creatives).</summary>
        public static bool IsPngOrJpeg(byte[] b) =>
            b != null && b.Length > 8 && (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 || b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF);

        public static bool IsSafeId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            foreach (char c in id) if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') return false;
            return true;
        }

        public static bool IsImageUrl(string url, bool allowHttp)
        {
            if (string.IsNullOrEmpty(url) || url.Length > UrlMax) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || string.IsNullOrEmpty(u.Host)) return false;
            if (!string.IsNullOrEmpty(u.UserInfo)) return false;
            return u.Scheme == Uri.UriSchemeHttps || allowHttp && u.Scheme == Uri.UriSchemeHttp;
        }
    }

    /// <summary>`ids.ad_slots` entries for one venue, by kind, in id order.</summary>
    public static class AdSlots
    {
        public static List<string> ForVenue(JToken adSlots, string venueId, string kind)
        {
            var list = new List<string>();
            var arr = adSlots?["adSlots"] as JArray ?? adSlots?["items"] as JArray ?? adSlots as JArray;
            if (arr == null || string.IsNullOrEmpty(venueId)) return list;
            foreach (var s in arr)
                if (WireText.Str(s, "venueId") == venueId && WireText.Str(s, "kind") == kind && LiveAds.IsSafeId(WireText.Str(s, "id")))
                    list.Add(WireText.Str(s, "id"));
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }

    /// <summary>
    /// What a surface shows right now. Live creatives for its slot rotate first; otherwise house rotation where index 0
    /// is the surface's baked house art and 1..N are text house ads. Driven by server time so every client agrees.
    /// </summary>
    public static class AdRotation
    {
        public struct Pick
        {
            public bool Live;
            public int Index;
        }

        public static Pick Current(int liveCount, int houseCount, double serverNowMs, float periodSeconds, int surfaceIndex)
        {
            long tick = periodSeconds > 0 ? (long)Math.Floor(serverNowMs / 1000.0 / periodSeconds) : 0;
            if (tick < 0) tick = 0;
            if (liveCount > 0) return new Pick { Live = true, Index = (int)((tick + surfaceIndex) % liveCount) };
            if (houseCount <= 0) return new Pick { Live = false, Index = 0 };
            return new Pick { Live = false, Index = (int)((tick + surfaceIndex) % houseCount) };
        }
    }

    public static class Poll
    {
        /// <summary>Polling backoff: doubles from <paramref name="baseSeconds"/> per failure, capped at <paramref name="maxSeconds"/>.</summary>
        public static float Backoff(int failures, float baseSeconds, float maxSeconds) =>
            failures <= 0 ? baseSeconds : Math.Min(maxSeconds, baseSeconds * (float)Math.Pow(2, Math.Min(10, failures - 1)));
    }

    // ------------------------------------------------------------------ texture memory budget

    /// <summary>
    /// LRU accounting for downloaded creatives. The caller destroys whatever <see cref="Add"/> evicts. An item larger
    /// than the whole budget is refused. Pure bookkeeping (byte sizes are computed by the caller from the real texture).
    /// </summary>
    public sealed class TextureBudget<T>
    {
        private readonly long _capBytes;
        private readonly LinkedList<(string key, long bytes, T item)> _lru = new LinkedList<(string, long, T)>();
        private readonly Dictionary<string, LinkedListNode<(string key, long bytes, T item)>> _index =
            new Dictionary<string, LinkedListNode<(string, long, T)>>(StringComparer.Ordinal);

        public long UsedBytes { get; private set; }
        public long CapBytes => _capBytes;
        public int Count => _index.Count;

        public TextureBudget(long capBytes) { _capBytes = Math.Max(0, capBytes); }

        public static long EstimateBytes(int width, int height, bool mips) =>
            (long)Math.Max(0, width) * Math.Max(0, height) * 4L * (mips ? 4L : 3L) / 3L;

        public bool TryGet(string key, out T item)
        {
            item = default;
            if (key == null || !_index.TryGetValue(key, out var node)) return false;
            _lru.Remove(node);
            _lru.AddFirst(node);
            item = node.Value.item;
            return true;
        }

        /// <returns>False when the item alone exceeds the cap (caller must destroy it). Evicted items go to <paramref name="evicted"/>.</returns>
        public bool Add(string key, long bytes, T item, List<T> evicted, ICollection<string> pinned = null)
        {
            if (key == null || bytes > _capBytes) return false;
            if (_index.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                _index.Remove(key);
                UsedBytes -= existing.Value.bytes;
                evicted?.Add(existing.Value.item);
            }
            var node = _lru.AddFirst((key, bytes, item));
            _index[key] = node;
            UsedBytes += bytes;
            var cursor = _lru.Last;
            while (UsedBytes > _capBytes && cursor != null)
            {
                var prev = cursor.Previous;
                if (cursor != node && (pinned == null || !pinned.Contains(cursor.Value.key)))
                {
                    _lru.Remove(cursor);
                    _index.Remove(cursor.Value.key);
                    UsedBytes -= cursor.Value.bytes;
                    evicted?.Add(cursor.Value.item);
                }
                cursor = prev;
            }
            return true;
        }

        public void Clear(List<T> evicted)
        {
            foreach (var n in _lru) evicted?.Add(n.item);
            _lru.Clear();
            _index.Clear();
            UsedBytes = 0;
        }
    }
}
