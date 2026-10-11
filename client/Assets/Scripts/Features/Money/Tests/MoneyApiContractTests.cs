using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Economy.Tests
{
    public class MoneyApiContractTests
    {
        [Test]
        public void PredictionHistoryRequestsAllPersistedStatusesFromPersonalEndpoint()
        {
            var rest = new CaptureRest();
            var rows = new MoneyApi(rest).PredictionHistory(CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual("/predictions", rest.Path, "an open filter would hide resolved history");
            Assert.AreEqual(0, rows.Items.Count);
            Assert.AreEqual(0, rest.Writes);
        }
        [Test]
        public void SubmittedSignatureUsesExistingOwnerRouteAndAcceptsNoContent()
        {
            var rest = new CaptureRest();
            var api = new MoneyApi(rest);
            var token = new CancellationTokenSource().Token;
            api.SubmitIntent("intent-123", "signature-fixture", "retained-key", token).GetAwaiter().GetResult();
            Assert.AreEqual("/payments/intents/intent-123/submitted", rest.Path);
            Assert.AreEqual("signature-fixture", (string)JObject.Parse((string)rest.Body)["signature"]);
            Assert.AreEqual("retained-key", rest.Key);
            Assert.AreEqual(token, rest.Token);
            Assert.IsTrue(rest.Auth);
            Assert.AreEqual(1, rest.Writes);
        }

        [Test]
        public void SubmittedSignatureRequiresSafeIntentAndRetainedKeyBeforeTransport()
        {
            var rest = new CaptureRest();
            var api = new MoneyApi(rest);
            Assert.Throws<ArgumentException>(() => api.SubmitIntent("../other", "sig", "key", CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => api.SubmitIntent("intent-123", "sig", null, CancellationToken.None).GetAwaiter().GetResult());
            Assert.AreEqual(0, rest.Writes);
        }

        private sealed class CaptureRest : IRestClient
        {
            public string Path, Key;
            public object Body;
            public bool Auth;
            public int Writes;
            public CancellationToken Token;
            public Func<Task<bool>> RefreshHandler { get; set; }
            public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default)
            {
                Path = path; Body = body; Auth = auth; Key = idempotencyKey; Token = ct; Writes++;
                return Task.FromResult(default(T)); // HTTP 204, no JSON body
            }
            public Task<T> Get<T>(string path, CancellationToken ct = default)
            {
                Path = path;
                return Task.FromResult(new JArray().ToObject<T>());
            }
            public Task<T> Put<T>(string p, object body, CancellationToken ct = default) => throw new InvalidOperationException();
            public Task<T> Patch<T>(string p, object body, CancellationToken ct = default) => throw new InvalidOperationException();
            public Task<T> Delete<T>(string p, CancellationToken ct = default) => throw new InvalidOperationException();
            public Task<T> Send<T>(string method, string p, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => throw new InvalidOperationException();
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => throw new InvalidOperationException();
        }
    }
}
