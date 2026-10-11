using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace CTLife.CityLife
{
    /// <summary>
    /// Fetches NFT art for frames: allow-listed HTTPS hosts only (<see cref="ImageFetchPolicy"/>), a byte cap enforced
    /// while the body streams (declared Content-Length and actual bytes), a pixel cap after decode, two downloads at a
    /// time, and an LRU texture cache with a memory budget. Failures return null; the frame shows a placeholder.
    /// </summary>
    public sealed class NftImageCache
    {
        /// <summary>FE memory budget for decoded frame textures (RGBA32 estimate). Perf choice for the Seeker, not a game rule.</summary>
        public const long BudgetBytes = 48L * 1024 * 1024; // 12 frames (Marina Villa) at the 1024 px cap
        public const int TimeoutSeconds = 15;

        private readonly ByteLruCache<Texture2D> _cache = new ByteLruCache<Texture2D>(BudgetBytes);
        private readonly Dictionary<string, Task<Texture2D>> _inflight = new Dictionary<string, Task<Texture2D>>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _slots = new SemaphoreSlim(2, 2);
        private readonly HashSet<string> _failed = new HashSet<string>(StringComparer.Ordinal);

        public ImageFetchPolicy Policy
        {
            get
            {
                var cfg = Services.TryGet<ConfigStore>(out var c) ? c.Config("nft") ?? c.Root?["nft"] : null;
                return ImageFetchPolicy.Parse(cfg);
            }
        }

        public Task<Texture2D> Get(string url, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(url)) return Task.FromResult<Texture2D>(null);
            if (_cache.TryGet(url, out var hit) && hit != null) return Task.FromResult(hit);
            if (_failed.Contains(url)) return Task.FromResult<Texture2D>(null);
            if (_inflight.TryGetValue(url, out var running)) return running;
            var policy = Policy;
            if (!policy.IsAllowed(url))
            {
                Log.Warn("nft", "Image host not allowed: " + SafeHost(url));
                _failed.Add(url);
                return Task.FromResult<Texture2D>(null);
            }
            var task = Fetch(url, policy, ct);
            _inflight[url] = task;
            return task;
        }

        private async Task<Texture2D> Fetch(string url, ImageFetchPolicy policy, CancellationToken ct)
        {
            bool acquired = false;
            try
            {
                await _slots.WaitAsync(ct);
                acquired = true;
                using (var req = UnityWebRequest.Get(url))
                {
                    req.timeout = TimeoutSeconds;
                    req.redirectLimit = 0; // a redirect could leave the allow-listed host
                    var op = req.SendWebRequest();
                    while (!op.isDone)
                    {
                        if (ct.IsCancellationRequested) { req.Abort(); ct.ThrowIfCancellationRequested(); }
                        var declared = req.GetResponseHeader("Content-Length");
                        if (declared != null && long.TryParse(declared, out var len) && !policy.AcceptsLength(len)) { req.Abort(); return Fail(url, "too large (declared)"); }
                        if ((long)req.downloadedBytes > policy.MaxBytes) { req.Abort(); return Fail(url, "too large"); }
                        await Awaitable.NextFrameAsync(ct);
                    }
                    if (req.result != UnityWebRequest.Result.Success) return Fail(url, req.result + " " + req.responseCode);
                    var type = req.GetResponseHeader("Content-Type") ?? "";
                    if (!(type.StartsWith("image/png", StringComparison.OrdinalIgnoreCase) || type.StartsWith("image/jpeg", StringComparison.OrdinalIgnoreCase)))
                        return Fail(url, "unsupported type " + type);
                    var bytes = req.downloadHandler.data;
                    if (bytes == null || bytes.Length == 0 || bytes.Length > policy.MaxBytes) return Fail(url, "empty or too large");
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "nft_frame", wrapMode = TextureWrapMode.Clamp };
                    if (!tex.LoadImage(bytes, true) || !policy.AcceptsSize(tex.width, tex.height))
                    {
                        UnityEngine.Object.Destroy(tex);
                        return Fail(url, "decode or size");
                    }
                    foreach (var old in _cache.Put(url, tex, (long)tex.width * tex.height * 4))
                        if (old != null && !ReferenceEquals(old, tex)) UnityEngine.Object.Destroy(old);
                    return _cache.TryGet(url, out var kept) ? kept : null;
                }
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception e) { return Fail(url, e.GetType().Name); }
            finally
            {
                if (acquired) _slots.Release();
                _inflight.Remove(url);
            }
        }

        private Texture2D Fail(string url, string why)
        {
            _failed.Add(url);
            Log.Warn("nft", "Image skipped (" + why + ") from " + SafeHost(url));
            return null;
        }

        private static string SafeHost(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "invalid url";

        public void Clear()
        {
            foreach (var t in _cache.Clear()) if (t != null) UnityEngine.Object.Destroy(t);
            _failed.Clear();
        }
    }
}
