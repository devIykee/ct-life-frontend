using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace CTLife.Net
{
    public sealed class RawResponse
    {
        public long status;
        public string body;
        public string etag;
        public string retryAfter;
    }

    /// <summary>
    /// Thin REST wrapper over UnityWebRequest (contract section 1: the generated `unityWebRequest` library is
    /// experimental, so FE writes the HTTP layer by hand and uses the generated models only).
    /// Every request sends X-Client-Version and X-Contract-Version. Every POST/PUT/PATCH/DELETE sends an
    /// Idempotency-Key (UUIDv4); retries reuse the same key so the server replays the first response.
    /// </summary>
    public sealed class RestClient : IRestClient
    {
        private const int TimeoutSeconds = 15;
        private const int MaxNetworkRetries = 2;

        private readonly Session _session;

        /// <summary>Set by AuthService. Returns true if the tokens were refreshed.</summary>
        public Func<Task<bool>> RefreshHandler { get; set; }

        public RestClient(Session session)
        {
            _session = session;
        }

        public Task<T> Get<T>(string path, CancellationToken ct = default) =>
            Send<T>("GET", path, null, ct: ct);

        public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) =>
            Send<T>("POST", path, body, auth, idempotencyKey ?? Ids.NewIdempotencyKey(), ct);

        public Task<T> Put<T>(string path, object body, CancellationToken ct = default) =>
            Send<T>("PUT", path, body, true, Ids.NewIdempotencyKey(), ct);

        public Task<T> Patch<T>(string path, object body, CancellationToken ct = default) =>
            Send<T>("PATCH", path, body, true, Ids.NewIdempotencyKey(), ct);

        public Task<T> Delete<T>(string path, CancellationToken ct = default) =>
            Send<T>("DELETE", path, null, true, Ids.NewIdempotencyKey(), ct);

        public async Task<T> Send<T>(string method, string path, object body = null, bool auth = true,
            string idempotencyKey = null, CancellationToken ct = default)
        {
            var json = body == null ? null : (body is string s ? s : Json.Serialize(body));
            var res = await SendRaw(method, path, json, auth, idempotencyKey, null, ct);
            if (typeof(T) == typeof(NoContent) || res.status == 204 || string.IsNullOrEmpty(res.body))
                return default;
            if (typeof(T) == typeof(string)) return (T)(object)res.body;
            if (typeof(JToken).IsAssignableFrom(typeof(T))) return (T)(object)Json.ParseRaw(res.body);
            try
            {
                return Json.Deserialize<T>(res.body);
            }
            catch (Exception e)
            {
                throw new ApiException((int)res.status, "DECODE_FAILED", $"{method} {path}: {e.Message}");
            }
        }

        public async Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth,
            string idempotencyKey, string ifNoneMatch, CancellationToken ct)
        {
            bool refreshed = false;
            int networkAttempts = 0;
            bool rateLimitedOnce = false;
            int busyAttempts = 0;

            if (auth && _session.AccessTokenStale && RefreshHandler != null && !string.IsNullOrEmpty(_session.RefreshToken))
                refreshed = await RefreshHandler();

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var res = await SendOnce(method, path, jsonBody, auth, idempotencyKey, ifNoneMatch, ct);

                if (res.status == 0)
                {
                    if (networkAttempts++ < MaxNetworkRetries && (idempotencyKey != null || method == "GET"))
                    {
                        await Awaitable.WaitForSecondsAsync(0.5f * networkAttempts * networkAttempts, ct);
                        continue;
                    }
                    throw new ApiException(0, "NETWORK_ERROR", res.body ?? "No connection", isNetwork: true);
                }

                if (res.status >= 200 && res.status < 300 || res.status == 304)
                {
                    if (busyAttempts > 0) Bus.Publish(new ServerBusySignal { active = false, source = "rest" });
                    return res;
                }

                var err = ParseError(res);

                if (res.status == 401 && auth && !refreshed && RefreshHandler != null &&
                    (err.Is("TOKEN_EXPIRED") || err.Is("UNAUTHORIZED")))
                {
                    refreshed = true;
                    if (await RefreshHandler()) continue;
                }

                if (err.Is("SERVER_BUSY") || res.status == 503 && !string.IsNullOrEmpty(res.retryAfter))
                {
                    busyAttempts++;
                    float wait = RetryAfterSeconds(res.retryAfter, busyAttempts);
                    Log.Warn("rest", $"{method} {path}: SERVER_BUSY, retry {busyAttempts} in {wait:0.#}s");
                    Bus.Publish(new ServerBusySignal { active = true, retryInSeconds = wait, attempt = busyAttempts, source = "rest" });
                    await Awaitable.WaitForSecondsAsync(wait, ct);
                    continue;
                }

                if (res.status == 429 && !rateLimitedOnce)
                {
                    rateLimitedOnce = true;
                    float wait = 1f;
                    if (float.TryParse(res.retryAfter, out var ra)) wait = Mathf.Clamp(ra, 0.2f, 5f);
                    await Awaitable.WaitForSecondsAsync(wait, ct);
                    continue;
                }

                if (res.status == 426 || err.Is("CLIENT_OUTDATED"))
                    Bus.Publish(new UpdateRequiredSignal { minClientVersion = err.Details?.Str("minClientVersion") });

                throw err;
            }
        }

        private async Task<RawResponse> SendOnce(string method, string path, string jsonBody, bool auth,
            string idempotencyKey, string ifNoneMatch, CancellationToken ct)
        {
            var url = Env.Current.ApiUrl(path);
            using var req = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            };
            if (jsonBody != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody)) { contentType = "application/json" };
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.SetRequestHeader("Accept", "application/json");
            req.SetRequestHeader("X-Client-Version", Semver.ClientVersion);
            req.SetRequestHeader("X-Contract-Version", Semver.ContractVersion);
            if (!string.IsNullOrEmpty(idempotencyKey)) req.SetRequestHeader("Idempotency-Key", idempotencyKey);
            if (!string.IsNullOrEmpty(ifNoneMatch)) req.SetRequestHeader("If-None-Match", ifNoneMatch);
            if (auth && _session.IsSignedIn) req.SetRequestHeader("Authorization", "Bearer " + _session.AccessToken);

            var started = Time.realtimeSinceStartup;
            try
            {
                await Awaitable.FromAsyncOperation(req.SendWebRequest(), ct);
            }
            catch (OperationCanceledException)
            {
                req.Abort();
                throw;
            }

            var res = new RawResponse
            {
                status = req.responseCode,
                body = req.downloadHandler?.text,
                etag = req.GetResponseHeader("ETag"),
                retryAfter = req.GetResponseHeader("Retry-After"),
            };
            if (req.result == UnityWebRequest.Result.ConnectionError)
            {
                res.status = 0;
                res.body = req.error;
            }
            Log.Debug("rest", $"{method} {path} → {res.status} in {(Time.realtimeSinceStartup - started) * 1000f:0} ms");
            return res;
        }

        /// <summary>Retry-After is delta-seconds or an HTTP-date (RFC 9110). Missing → capped exponential backoff.</summary>
        public static float RetryAfterSeconds(string header, int attempt)
        {
            if (!string.IsNullOrEmpty(header))
            {
                if (float.TryParse(header, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s))
                    return Mathf.Clamp(s, 1f, 120f);
                if (DateTimeOffset.TryParse(header, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out var when))
                    return Mathf.Clamp((float)(when - DateTimeOffset.UtcNow).TotalSeconds, 1f, 120f);
            }
            return Mathf.Min(30f, 2f * Mathf.Pow(2f, Mathf.Min(attempt - 1, 4)));
        }

        private static ApiException ParseError(RawResponse res)
        {
            try
            {
                var tok = Json.ParseRaw(res.body ?? "");
                var e = tok?["error"];
                if (e != null)
                    return new ApiException((int)res.status, e.Str("code", "HTTP_" + res.status), e.Str("message", ""), e["details"]);
            }
            catch
            {
                // Not JSON: fall through.
            }
            return new ApiException((int)res.status, "HTTP_" + res.status, Truncate(res.body, 200));
        }

        private static string Truncate(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n);
    }
}
