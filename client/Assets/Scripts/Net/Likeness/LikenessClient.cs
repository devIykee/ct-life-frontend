using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace CTLife.Net
{
    [Serializable, UnityEngine.Scripting.Preserve]
    public sealed class LikenessAssetManifest
    {
        public string jobId, modelId, sha256, validatorVersion;
        public int bytes, triangles, joints;
        public float heightMeters = 1.75f;
        public System.Collections.Generic.Dictionary<string, string> humanoidBones;
    }

    /// <summary>Owned, authenticated likeness API. Binary requests never follow redirects or replay uploads implicitly.</summary>
    public sealed class LikenessClient
    {
        public const string ConsentVersion = "likeness-v1";
        public const int MaxPhotoBytes = 8 * 1024 * 1024;
        public const int MaxModelBytes = 16 * 1024 * 1024;
        private readonly IRestClient _rest;
        private readonly Session _session;
        private readonly string _owner;
        public LikenessClient(IRestClient rest, Session session)
        { _rest = rest; _session = session; _owner = session.UserId; }
        public Task<JObject> Checkout(string modelId, string key, CancellationToken ct) =>
            _rest.Post<JObject>("/likeness/checkout", new { modelId }, idempotencyKey: key, ct: ct);
        public Task<JObject> CheckoutStatus(string id, CancellationToken ct) => _rest.Get<JObject>("/likeness/checkout/" + Id(id), ct);
        public Task<JObject> CurrentCheckout(CancellationToken ct) => _rest.Get<JObject>("/likeness/checkout/current", ct);
        public Task<JObject> SubmitReceipt(string id, string signature, string key, CancellationToken ct) =>
            _rest.Post<JObject>("/likeness/checkout/" + Id(id) + "/submitted", new { signature }, idempotencyKey: key, ct: ct);
        public Task<JObject> Jobs(CancellationToken ct) => _rest.Get<JObject>("/likeness/jobs", ct);
        public Task<LikenessBytes> Preview(string id, CancellationToken ct) => Binary("GET", JobPath(id) + "/preview", MaxModelBytes, null, null, null, ct);
        public Task<JObject> Availability(CancellationToken ct) => _rest.Get<JObject>("/likeness", ct);
        public Task<JObject> Job(string id, CancellationToken ct) => _rest.Get<JObject>(JobPath(id), ct);
        public Task<LikenessAssetManifest> Manifest(string id, CancellationToken ct) => _rest.Get<LikenessAssetManifest>(JobPath(id) + "/manifest", ct);
        public Task<JObject> Create(string model, string upload, string entitlement, string key, CancellationToken ct) =>
            _rest.Post<JObject>("/likeness/jobs", new { modelId = model, uploadId = upload, entitlementId = entitlement,
                consent = new { version = ConsentVersion, ownPhoto = true, shareWithProvider = true } }, idempotencyKey: key, ct: ct);
        public Task<JObject> Approve(string id, string hash, string key, CancellationToken ct) =>
            _rest.Post<JObject>(JobPath(id) + "/reference/approve", new { referenceSha256 = hash }, idempotencyKey: key, ct: ct);
        public Task<JObject> Accept(string id, string key, CancellationToken ct) =>
            _rest.Post<JObject>(JobPath(id) + "/accept", new { }, idempotencyKey: key, ct: ct);
        public Task<JObject> Cancel(string id, CancellationToken ct) => _rest.Delete<JObject>(JobPath(id), ct);
        public Task<JObject> DeletePhoto(string id, CancellationToken ct) => _rest.Delete<JObject>("/likeness/photos/" + Id(id), ct);
        public async Task<JObject> Upload(byte[] bytes, string mime, string key, CancellationToken ct)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxPhotoBytes || mime != "image/jpeg" && mime != "image/png")
                throw new InvalidDataException("Invalid photo");
            var result = await Binary("POST", "/likeness/photos", 32 * 1024, bytes, mime, key, ct);
            return JObject.Parse(System.Text.Encoding.UTF8.GetString(result.bytes));
        }
        public Task<LikenessBytes> Reference(string id, CancellationToken ct) => Binary("GET", JobPath(id) + "/reference", MaxPhotoBytes, null, null, null, ct);
        public Task<LikenessBytes> Model(string id, CancellationToken ct) => Binary("GET", JobPath(id) + "/result", MaxModelBytes, null, null, null, ct);

        private async Task<LikenessBytes> Binary(string method, string path, int limit, byte[] body, string mime, string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (RuntimeMode.IsOfflineDemo)
                throw new ApiException(0, "OFFLINE_DEMO", "Server actions are unavailable in the offline demo");
            if (_session.AccessTokenStale && _rest.RefreshHandler != null) await _rest.RefreshHandler();
            ct.ThrowIfCancellationRequested();
            if (!_session.IsSignedIn || _session.UserId != _owner) throw new OperationCanceledException("Account changed", ct);
            using var download = new LikenessBoundedDownload(limit);
            using var req = new UnityWebRequest(Env.Current.ApiUrl(path), method) { downloadHandler = download, timeout = 45, redirectLimit = 0 };
            req.SetRequestHeader("Authorization", "Bearer " + _session.AccessToken);
            req.SetRequestHeader("X-Client-Version", Semver.ClientVersion);
            req.SetRequestHeader("X-Contract-Version", Semver.ContractVersion);
            if (body != null)
            {
                req.uploadHandler = new UploadHandlerRaw(body) { contentType = mime };
                req.SetRequestHeader("Content-Type", mime);
                req.SetRequestHeader("Idempotency-Key", key);
                req.SetRequestHeader("X-Likeness-Consent-Version", ConsentVersion);
                req.SetRequestHeader("X-Likeness-Own-Photo", "true");
                req.SetRequestHeader("X-Likeness-Share-With-Provider", "true");
            }
            try { await Awaitable.FromAsyncOperation(req.SendWebRequest(), ct); }
            catch (OperationCanceledException) { req.Abort(); throw; }
            if (_session.UserId != _owner) throw new OperationCanceledException("Account changed", ct);
            if (download.Exceeded) throw new InvalidDataException("Asset exceeds download budget");
            if (req.responseCode < 200 || req.responseCode >= 300 || req.result != UnityWebRequest.Result.Success)
                throw new ApiException((int)req.responseCode, "LIKENESS_REQUEST_FAILED", "The likeness request did not complete", isNetwork: req.responseCode == 0);
            var data = download.Bytes;
            var hash = req.GetResponseHeader("X-Content-Sha256");
            if (!string.IsNullOrEmpty(hash) && !string.Equals(Sha256(data), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Asset checksum mismatch");
            return new LikenessBytes { bytes = data, sha256 = hash, contentType = req.GetResponseHeader("Content-Type") };
        }
        public static string Sha256(byte[] bytes) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        public static string Id(string value) => Guid.TryParse(value, out var id) ? id.ToString() : throw new ArgumentException("Invalid likeness ID");
        private static string JobPath(string id) => "/likeness/jobs/" + Id(id);
    }
    public sealed class LikenessBytes { public byte[] bytes; public string sha256, contentType; }
    internal sealed class LikenessBoundedDownload : DownloadHandlerScript
    {
        private readonly int _limit;
        private readonly MemoryStream _buffer = new MemoryStream();
        public bool Exceeded { get; private set; }
        public byte[] Bytes => _buffer.ToArray();
        public LikenessBoundedDownload(int limit) : base(new byte[32 * 1024]) { _limit = limit; }
        protected override void ReceiveContentLengthHeader(ulong length) { if (length > (ulong)_limit) Exceeded = true; }
        protected override bool ReceiveData(byte[] data, int length)
        {
            if (Exceeded || length < 0 || _buffer.Length + length > _limit) { Exceeded = true; return false; }
            if (data != null && length > 0) _buffer.Write(data, 0, length);
            return true;
        }
    }
}
