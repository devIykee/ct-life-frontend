using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Careers.Tests
{
    public class CareersApiContractTests
    {
        [TestCase("/careers")]
        [TestCase("/gigs")]
        public void MalformedServerResponseIsAnErrorNotAnEmptySuccessfulBoard(string route)
        {
            var rest = new CaptureRest { Response = JObject.Parse("{\"unexpected\":true}") };
            var api = new CareersApi(rest);
            Assert.Throws<FormatException>(() => Read(api, route).GetAwaiter().GetResult());
            Assert.AreEqual(route, rest.Path);
        }

        [TestCase("/careers")]
        [TestCase("/gigs")]
        public void ActualEmptyArrayIsAccepted(string route)
        {
            var rest = new CaptureRest { Response = new JArray() };
            Assert.DoesNotThrow(() => Read(new CareersApi(rest), route).GetAwaiter().GetResult());
        }

        [Test]
        public void ApplySendsObjectBodyAndCallerRetainedKey()
        {
            var rest = new CaptureRest { Response = JObject.Parse("{\"careerId\":\"discord_mod\",\"level\":1,\"xp\":0}") };
            var grant = new CareersApi(rest).Apply("discord_mod", "apply-key", CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual("discord_mod", grant.CareerId);
            Assert.AreEqual("/careers/discord_mod/apply", rest.Path);
            Assert.AreEqual("{}", rest.Body);
            Assert.AreEqual("apply-key", rest.Key);
        }

        private static async Task Read(CareersApi api, string route)
        {
            if (route == "/careers") await api.Careers(CancellationToken.None);
            else await api.Gigs(CancellationToken.None);
        }

        private sealed class CaptureRest : IRestClient
        {
            public JToken Response;
            public string Path, Key;
            public object Body;
            public Func<Task<bool>> RefreshHandler { get; set; }
            public Task<T> Get<T>(string path, CancellationToken ct = default)
            {
                Path = path;
                return Task.FromResult(Response.ToObject<T>());
            }
            public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default)
            {
                Path = path; Body = body; Key = idempotencyKey;
                return Task.FromResult(Response.ToObject<T>());
            }
            public Task<T> Put<T>(string p, object body, CancellationToken ct = default) => throw new InvalidOperationException();
            public Task<T> Patch<T>(string p, object body, CancellationToken ct = default) => throw new InvalidOperationException();
            public Task<T> Delete<T>(string p, CancellationToken ct = default) => throw new InvalidOperationException();
            public Task<T> Send<T>(string method, string p, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => throw new InvalidOperationException();
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => throw new InvalidOperationException();
        }
    }
}
