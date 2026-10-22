using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace CTLife.Ventures
{
    /// <summary>
    /// Raw PNG/JPEG POST for `/uploads` over UnityWebRequest, with the same headers RestClient sends (version headers,
    /// bearer token, Idempotency-Key). The session token is refreshed first when stale and once more after a 401. The body is the
    /// already-validated creative (at most 1 MB); the reply is read as text and parsed with the contract error envelope.
    /// </summary>
    public sealed class UnityMultipartClient : IMultipartClient
    {
        private const int TimeoutSeconds = 30;

        public async Task<JToken> PostFile(string path, string field, string fileName, string mime, byte[] data, string idempotencyKey, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (RuntimeMode.IsOfflineDemo)
                throw new ApiException(0, "OFFLINE_DEMO", "Server actions are unavailable in the offline demo");
            ValidateImage(data, mime);
            if (string.IsNullOrEmpty(idempotencyKey)) throw new ArgumentException("A retained Idempotency-Key is required", nameof(idempotencyKey));
            var session = Services.Get<Session>();
            if (Services.TryGet<IAuthService>(out var auth)) await auth.EnsureFresh(ct);
            for (int attempt = 0; ; attempt++)
            {
                var (status, body) = await SendOnce(path, field, fileName, mime, data, idempotencyKey, session, ct);
                if (status == 401 && attempt == 0 && auth != null && await auth.Refresh()) continue;
                if (status >= 200 && status < 300)
                {
                    try { return Json.ParseRaw(body ?? ""); }
                    catch (Exception) { throw new ApiException((int)status, "DECODE_FAILED", "Unreadable upload reply"); }
                }
                throw ParseError(status, body);
            }
        }

        private static async Task<(long status, string body)> SendOnce(string path, string field, string fileName, string mime, byte[] data,
            string key, Session session, CancellationToken ct)
        {
            using (var req = new UnityWebRequest(Env.Current.ApiUrl(path), "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(data) { contentType = mime };
                req.timeout = TimeoutSeconds;
                req.redirectLimit = 0;
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Accept", "application/json");
                req.SetRequestHeader("Content-Type", mime);
                req.SetRequestHeader("X-Client-Version", Semver.ClientVersion);
                req.SetRequestHeader("X-Contract-Version", Semver.ContractVersion);
                if (!string.IsNullOrEmpty(key)) req.SetRequestHeader("Idempotency-Key", key);
                if (session.IsSignedIn) req.SetRequestHeader("Authorization", "Bearer " + session.AccessToken);
                try { await Awaitable.FromAsyncOperation(req.SendWebRequest(), ct); }
                catch (OperationCanceledException) { req.Abort(); throw; }
                if (req.result == UnityWebRequest.Result.ConnectionError)
                    throw new ApiException(0, "NETWORK_ERROR", req.error ?? "network", null, true);
                return (req.responseCode, req.downloadHandler?.text);
            }
        }

        public static void ValidateImage(byte[] data, string mime)
        {
            if (data == null || data.Length == 0 || data.Length > 1_048_576) throw new ArgumentException("Bad upload size", nameof(data));
            if (mime != "image/png" && mime != "image/jpeg") throw new ArgumentException("Expected a PNG or JPEG", nameof(mime));
        }

        private static ApiException ParseError(long status, string body)
        {
            try
            {
                var e = Json.ParseRaw(body ?? "")?["error"];
                if (e != null)
                    return new ApiException((int)status, VWire.Str(e["code"]) ?? "HTTP_" + status, VWire.Plain(e["message"], 200), e["details"]);
            }
            catch (Exception) { /* fall through to a generic error */ }
            return new ApiException((int)status, "HTTP_" + status, "Upload failed");
        }
    }
}
