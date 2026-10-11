using System;
using System.Collections;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace CTLife.WorldLife
{
    /// <summary>
    /// Downloads ad creatives (PNG/JPEG, contract cap 1 MB) into non-readable GPU textures, at most 1024 px on the long
    /// side (larger images are blitted down and the original freed), inside a fixed byte budget with LRU eviction.
    /// Textures on screen are pinned. Failed URLs are not retried for 10 minutes. One download at a time.
    /// </summary>
    public sealed class AdImageCache : IDisposable
    {
        public const long BudgetBytes = 16L * 1024 * 1024;
        public const int MaxBytes = 1_536_000;
        public const int MaxSide = 1024;
        private const float FailRetrySeconds = 600f;

        private readonly TextureBudget<Texture> _budget = new TextureBudget<Texture>(BudgetBytes);
        private readonly Dictionary<string, float> _failedUntil = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Queue<string> _queue = new Queue<string>();
        private readonly HashSet<string> _queued = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Texture> _evicted = new List<Texture>();
        private bool _running;
        private bool _disposed;

        public event Action<string> Loaded;
        public long UsedBytes => _budget.UsedBytes;

        public bool TryGet(string url, out Texture tex) => _budget.TryGet(url, out tex) && tex != null;

        public void Request(string url)
        {
            if (_disposed || string.IsNullOrEmpty(url) || _queued.Contains(url) || TryGet(url, out _)) return;
            if (_failedUntil.TryGetValue(url, out var until) && Time.unscaledTime < until) return;
            _queue.Enqueue(url);
            _queued.Add(url);
            if (!_running) Runner.Run(Pump());
        }

        /// <summary>URLs currently on a surface; they are never evicted for a new download.</summary>
        public ICollection<string> Pinned { get; set; }

        private IEnumerator Pump()
        {
            _running = true;
            while (!_disposed && _queue.Count > 0)
            {
                var url = _queue.Dequeue();
                yield return Download(url);
                _queued.Remove(url);
            }
            _running = false;
        }

        private IEnumerator Download(string url)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 20;
                req.redirectLimit = 2;
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (_disposed || req.downloadedBytes > (ulong)MaxBytes) { req.Abort(); break; }
                    yield return null;
                }
                if (_disposed) yield break;
                var bytes = req.result == UnityWebRequest.Result.Success ? req.downloadHandler?.data : null;
                if (bytes == null || bytes.Length == 0 || bytes.Length > MaxBytes || !LiveAds.IsPngOrJpeg(bytes))
                {
                    Fail(url, req.result + " " + req.responseCode + (bytes != null ? " bytes=" + bytes.Length : ""));
                    yield break;
                }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "ad_creative", wrapMode = TextureWrapMode.Clamp };
                if (!tex.LoadImage(bytes, true))
                {
                    UnityEngine.Object.Destroy(tex);
                    Fail(url, "decode");
                    yield break;
                }
                Texture shown = tex;
                if (tex.width > MaxSide || tex.height > MaxSide)
                {
                    float k = (float)MaxSide / Mathf.Max(tex.width, tex.height);
                    var rt = new RenderTexture(Mathf.Max(1, (int)(tex.width * k)), Mathf.Max(1, (int)(tex.height * k)), 0,
                        RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "ad_creative_scaled" };
                    rt.Create();
                    Graphics.Blit(tex, rt);
                    UnityEngine.Object.Destroy(tex);
                    shown = rt;
                }
                long size = TextureBudget<Texture>.EstimateBytes(shown.width, shown.height, shown is Texture2D);
                _evicted.Clear();
                if (!_budget.Add(url, size, shown, _evicted, Pinned))
                {
                    DestroyTex(shown);
                    Fail(url, "over budget");
                    yield break;
                }
                foreach (var t in _evicted) if (t != shown) DestroyTex(t);
                try { Loaded?.Invoke(url); }
                catch (Exception e) { Log.Exception("world-life", e); }
            }
        }

        private void Fail(string url, string why)
        {
            _failedUntil[url] = Time.unscaledTime + FailRetrySeconds;
            Log.Warn("world-life", "Ad creative unavailable (" + why + "); house ad shown instead.");
        }

        private static void DestroyTex(Texture t)
        {
            if (t == null) return;
            if (t is RenderTexture rt) rt.Release();
            UnityEngine.Object.Destroy(t);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _evicted.Clear();
            _budget.Clear(_evicted);
            foreach (var t in _evicted) DestroyTex(t);
            _evicted.Clear();
            _queue.Clear();
            _queued.Clear();
        }
    }
}
