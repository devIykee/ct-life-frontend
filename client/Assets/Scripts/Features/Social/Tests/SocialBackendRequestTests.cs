using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.SocialFeatures.Tests
{
    public class SocialBackendRequestTests
    {
        [TestCase(false, "video")]
        [TestCase(true, "paid_call")]
        public void CallTokenTargetsTheServersParticipantRoom(bool paid, string expectedKind)
        {
            var rest = new CaptureRest();
            rest.Response = JObject.Parse("{\"url\":\"wss://rtc.example\",\"token\":\"fixture\",\"canPublish\":true}");
            var grant = new SocialApi(rest).CallToken("fixture-call", paid, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(rest.Method, Is.EqualTo("POST"));
            Assert.That(rest.Path, Is.EqualTo("/rtc/token"));
            Assert.That((string)rest.Body["kind"], Is.EqualTo(expectedKind));
            Assert.That((string)rest.Body["id"], Is.EqualTo("fixture-call"));
            Assert.That(grant.CanPublish, Is.True);
        }

        [Test]
        public void VideoEndRetainsItsKeyAcrossUncertainRetries()
        {
            var rest = new CaptureRest { Error = new ApiException(0, "NETWORK_ERROR", "offline") };
            var api = new SocialApi(rest);
            var key = new RetainedKey(() => "end-fixture-key");
            Assert.Throws<ApiException>(() => api.EndVideoCall("call:one", key, CancellationToken.None).GetAwaiter().GetResult());
            Assert.That(rest.Path, Is.EqualTo("/video-calls/call%3Aone/end"));
            Assert.That(rest.Key, Is.EqualTo("end-fixture-key"));
            rest.Error = null;
            api.EndVideoCall("call:one", key, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(rest.Key, Is.EqualTo("end-fixture-key"));
            Assert.That(rest.Writes, Is.EqualTo(2));
        }

        [Test]
        public void AlreadyEndedIsTerminalButOtherValidationErrorsAreNotSuccess()
        {
            var rest = new CaptureRest { Error = new ApiException(400, "VALIDATION_FAILED", "Invalid", JObject.Parse("{\"reason\":\"call already ended\"}")) };
            var api = new SocialApi(rest);
            api.EndVideoCall("fixture", new RetainedKey(), CancellationToken.None).GetAwaiter().GetResult();
            rest.Error = new ApiException(403, "FORBIDDEN", "Forbidden", JObject.Parse("{\"reason\":\"only host can end call\"}"));
            Assert.Throws<ApiException>(() => api.EndVideoCall("fixture", new RetainedKey(), CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        public void BackendBlockedMessageIsReportedAsBlockedRatherThanBadInput()
        {
            var error = SocialError.From(new ApiException(400, "VALIDATION_FAILED", "Invalid", JObject.Parse("{\"reason\":\"blocked\"}")));
            Assert.That(error.Code, Is.EqualTo("BLOCKED"));
            Assert.That(error.CopyKey, Is.EqualTo("err.blocked"));
            Assert.That(error.OutcomeUnknown, Is.False);
        }

        private sealed class CaptureRest : IRestClient
        {
            public string Method, Path, Key;
            public JObject Body;
            public int Writes;
            public Exception Error;
            public JToken Response;
            public Func<Task<bool>> RefreshHandler { get; set; }
            public Task<T> Send<T>(string method, string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default)
            {
                Method = method; Path = path; Key = idempotencyKey; Writes++;
                Body = body is string json ? JObject.Parse(json) : null;
                return Error != null ? Task.FromException<T>(Error) : Task.FromResult((T)(object)Response);
            }
            public Task<T> Get<T>(string path, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Post<T>(string p, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Put<T>(string p, object body, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Patch<T>(string p, object body, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Delete<T>(string p, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => throw new NotSupportedException();
        }
    }
}
