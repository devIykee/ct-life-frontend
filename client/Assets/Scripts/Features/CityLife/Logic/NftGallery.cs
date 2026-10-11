using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife
{
    public enum Moderation { Ok, Pending, Nsfw, Blocked }

    public enum FrameVisibility { Show, Placeholder, Hidden }

    /// <summary>
    /// One NFT the server verified the player holds (proposal `GET /nfts/me`, docs/fe-contract-proposals/city-life-nft-gallery.md).
    /// The server reads the wallet with Helius DAS `getAssetsByOwner` (verified fields: `content.links.image`,
    /// `content.files[].cdn_uri|mime`, `grouping`, `burnt`, `interface`; DAS has no moderation flag, so `moderation` is
    /// our server's own verdict) and returns image URLs on our image proxy.
    /// </summary>
    public sealed class NftItem
    {
        public string AssetId, Name, Collection, ImageUrl, ThumbUrl, Mime;
        public int Width, Height;
        public Moderation Moderation;

        /// <summary>Unknown or missing moderation fails closed (treated as pending, never shown as an image).</summary>
        public static Moderation ParseModeration(string s)
        {
            switch (s)
            {
                case "ok": return Moderation.Ok;
                case "nsfw": return Moderation.Nsfw;
                case "blocked": return Moderation.Blocked;
                default: return Moderation.Pending;
            }
        }

        public static NftItem Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected an NFT");
            var id = CityWire.ReqStr(o["assetId"], "assetId");
            if (!IsAssetId(id)) throw new FormatException("Invalid assetId");
            return new NftItem
            {
                AssetId = id,
                Name = CityWire.Plain(o["name"], 48),
                Collection = CityWire.Plain(o["collection"] ?? o["collectionName"], 48),
                ImageUrl = CityWire.Str(o["imageUrl"]),
                ThumbUrl = CityWire.Str(o["thumbUrl"]),
                Mime = CityWire.Str(o["mime"]),
                Width = CityWire.IntOr(o["width"], 0),
                Height = CityWire.IntOr(o["height"], 0),
                Moderation = ParseModeration(CityWire.Str(o["moderation"])),
            };
        }

        public static List<NftItem> ParseList(JToken t)
        {
            var list = new List<NftItem>();
            foreach (var r in CityWire.List(t, "items", "nfts"))
            {
                try { list.Add(Parse(r)); }
                catch (FormatException) { /* one bad row never hides the others */ }
            }
            return list;
        }

        /// <summary>Solana asset ids are base58 public keys (32–44 chars, no 0/O/I/l).</summary>
        public static bool IsAssetId(string id) =>
            !string.IsNullOrEmpty(id) && id.Length >= 32 && id.Length <= 44 &&
            id.All(c => (c >= '1' && c <= '9') || (c >= 'A' && c <= 'Z' && c != 'I' && c != 'O') || (c >= 'a' && c <= 'z' && c != 'l'));

        /// <summary>
        /// Visitors see only server-approved art. The owner also sees pending items as placeholders; NSFW/blocked art is
        /// never fetched for anyone (the owner sees a "flagged" tile in the picker instead).
        /// </summary>
        public static FrameVisibility VisibilityFor(NftItem item, bool viewerIsOwner)
        {
            if (item == null) return FrameVisibility.Hidden;
            switch (item.Moderation)
            {
                case Moderation.Ok: return FrameVisibility.Show;
                case Moderation.Pending: return viewerIsOwner ? FrameVisibility.Placeholder : FrameVisibility.Hidden;
                default: return FrameVisibility.Hidden;
            }
        }
    }

    /// <summary>A frame placed on a wall slot (proposal `PUT /homes/me/gallery {frames: [{slot, assetId}]}`).</summary>
    public readonly struct FramePlacement
    {
        public readonly int Slot;
        public readonly string AssetId;
        public FramePlacement(int slot, string assetId) { Slot = slot; AssetId = assetId; }

        public static List<FramePlacement> ParseList(JToken t)
        {
            var list = new List<FramePlacement>();
            JArray rows;
            try { rows = CityWire.List(t, "frames", "items"); } catch (FormatException) { return list; }
            foreach (var r in rows)
            {
                var id = CityWire.Str(r?["assetId"]);
                if (NftItem.IsAssetId(id)) list.Add(new FramePlacement(CityWire.IntOr(r["slot"], 0), id));
            }
            return list;
        }

        public static JObject ToJson(IEnumerable<FramePlacement> frames) =>
            new JObject { ["frames"] = new JArray(frames.Select(f => new JObject { ["slot"] = f.Slot, ["assetId"] = f.AssetId })) };
    }

    /// <summary>Which NFT hangs on which wall slot. Pure; tested.</summary>
    public static class GalleryLayout
    {
        /// <summary>
        /// Slots are 1-based. A placement keeps its slot when that slot exists and is free; otherwise it takes the first
        /// free slot. Duplicate assets, unknown assets and anything the viewer may not see are dropped; overflow is dropped.
        /// </summary>
        public static NftItem[] Assign(IList<FramePlacement> placements, IList<NftItem> items, int slotCount, bool viewerIsOwner)
        {
            var result = new NftItem[Math.Max(0, slotCount)];
            if (slotCount <= 0 || placements == null || items == null) return result;
            var byId = new Dictionary<string, NftItem>(StringComparer.Ordinal);
            foreach (var i in items) if (i != null && !byId.ContainsKey(i.AssetId)) byId[i.AssetId] = i;
            var used = new HashSet<string>(StringComparer.Ordinal);
            var deferred = new List<NftItem>();
            foreach (var p in placements)
            {
                if (!byId.TryGetValue(p.AssetId ?? "", out var item) || !used.Add(p.AssetId)) continue;
                if (NftItem.VisibilityFor(item, viewerIsOwner) == FrameVisibility.Hidden) continue;
                if (p.Slot >= 1 && p.Slot <= slotCount && result[p.Slot - 1] == null) result[p.Slot - 1] = item;
                else deferred.Add(item);
            }
            int next = 0;
            foreach (var item in deferred)
            {
                while (next < slotCount && result[next] != null) next++;
                if (next >= slotCount) break;
                result[next] = item;
            }
            return result;
        }

        /// <summary>Placements for the save call from a slot array (1-based slots, empty slots skipped).</summary>
        public static List<FramePlacement> ToPlacements(NftItem[] slots)
        {
            var list = new List<FramePlacement>();
            if (slots == null) return list;
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) list.Add(new FramePlacement(i + 1, slots[i].AssetId));
            return list;
        }
    }

    /// <summary>
    /// Client-side safety for remote images: HTTPS only, hosts from config `nft.imageHosts` (proposal; empty → nothing is
    /// fetched), a byte cap enforced while downloading, and a pixel cap after decode. The defaults are FE safety limits,
    /// not game rules.
    /// </summary>
    public sealed class ImageFetchPolicy
    {
        public const long DefaultMaxBytes = 512 * 1024;
        public const int DefaultMaxPixels = 1024;

        public readonly List<string> Hosts = new List<string>();
        public long MaxBytes = DefaultMaxBytes;
        public int MaxPixels = DefaultMaxPixels;

        public static ImageFetchPolicy Parse(JToken nftConfig)
        {
            var p = new ImageFetchPolicy();
            if (nftConfig?["imageHosts"] is JArray hosts)
                foreach (var h in hosts)
                {
                    var s = CityWire.Str(h)?.Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(s) && s.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-') && s.Trim('.').Length > 0) p.Hosts.Add(s);
                }
            long bytes = CityWire.LongOr(nftConfig?["maxImageBytes"], DefaultMaxBytes);
            if (bytes > 0 && bytes <= 4 * 1024 * 1024) p.MaxBytes = bytes;
            int px = CityWire.IntOr(nftConfig?["maxImagePx"], DefaultMaxPixels);
            if (px >= 64 && px <= 2048) p.MaxPixels = px;
            return p;
        }

        /// <summary>A host entry "img.example" matches exactly; ".example" matches any subdomain of example.</summary>
        public bool IsAllowed(string url)
        {
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort) return false;
            var host = uri.Host.ToLowerInvariant();
            foreach (var h in Hosts)
            {
                if (h.StartsWith(".", StringComparison.Ordinal) ? host.EndsWith(h, StringComparison.Ordinal) && host.Length > h.Length : host == h)
                    return true;
            }
            return false;
        }

        /// <summary>Declared Content-Length check before the body arrives; -1 (unknown) is allowed and capped while streaming.</summary>
        public bool AcceptsLength(long declared) => declared < 0 || declared <= MaxBytes;

        public bool AcceptsSize(int width, int height) => width > 0 && height > 0 && width <= MaxPixels && height <= MaxPixels;
    }

    /// <summary>
    /// Least-recently-used cache with a byte budget. Pure; the Unity layer destroys the evicted textures it gets back.
    /// </summary>
    public sealed class ByteLruCache<T> where T : class
    {
        private sealed class Entry { public string Key; public T Value; public long Bytes; }

        private readonly long _budget;
        private readonly LinkedList<Entry> _order = new LinkedList<Entry>();
        private readonly Dictionary<string, LinkedListNode<Entry>> _map = new Dictionary<string, LinkedListNode<Entry>>(StringComparer.Ordinal);
        public long Bytes { get; private set; }
        public int Count => _map.Count;

        public ByteLruCache(long budgetBytes) { _budget = Math.Max(1, budgetBytes); }

        public bool TryGet(string key, out T value)
        {
            value = null;
            if (key == null || !_map.TryGetValue(key, out var node)) return false;
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        /// <summary>Adds or replaces; returns the values evicted (including a replaced value and an entry larger than the budget).</summary>
        public List<T> Put(string key, T value, long bytes)
        {
            var evicted = new List<T>();
            if (key == null || value == null) return evicted;
            if (_map.TryGetValue(key, out var old))
            {
                _order.Remove(old);
                _map.Remove(key);
                Bytes -= old.Value.Bytes;
                if (!ReferenceEquals(old.Value.Value, value)) evicted.Add(old.Value.Value);
            }
            if (bytes > _budget) { evicted.Add(value); return evicted; }
            var node = _order.AddFirst(new Entry { Key = key, Value = value, Bytes = Math.Max(0, bytes) });
            _map[key] = node;
            Bytes += node.Value.Bytes;
            while (Bytes > _budget && _order.Last != null && !ReferenceEquals(_order.Last, node))
            {
                var last = _order.Last;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
                Bytes -= last.Value.Bytes;
                evicted.Add(last.Value.Value);
            }
            return evicted;
        }

        public List<T> Clear()
        {
            var all = _order.Select(e => e.Value).ToList();
            _order.Clear();
            _map.Clear();
            Bytes = 0;
            return all;
        }
    }

    /// <summary>The NFT gallery surface. Contract rc1 has none; REST targets the proposal, the stub reports UNAVAILABLE.</summary>
    public interface INftGateway
    {
        bool IsStub { get; }
        /// <summary>My verified NFTs (one page; the proposal caps a page at 100).</summary>
        Task<List<NftItem>> Mine(CancellationToken ct);
        /// <summary>The frames on a player's walls with the items they show (visitors receive only `ok` art).</summary>
        Task<(List<FramePlacement> frames, List<NftItem> items)> Gallery(string ownerId, CancellationToken ct);
        Task SaveGallery(List<FramePlacement> frames, string idempotencyKey, CancellationToken ct);
    }

    public sealed class StubNftGateway : INftGateway
    {
        public bool IsStub => true;
        public Task<List<NftItem>> Mine(CancellationToken ct) => Task.FromException<List<NftItem>>(new UnavailableException());
        public Task<(List<FramePlacement> frames, List<NftItem> items)> Gallery(string ownerId, CancellationToken ct) =>
            Task.FromException<(List<FramePlacement>, List<NftItem>)>(new UnavailableException());
        public Task SaveGallery(List<FramePlacement> frames, string key, CancellationToken ct) => Task.FromException(new UnavailableException());
    }
}
