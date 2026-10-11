#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife.Tests
{
    public class CityRouteAvailabilityTests
    {
        // Exact body from public-probes-client-headers.json, GET /v1/businesses, 404.
        private const string NoRoute = "{\"error\":{\"code\":\"NOT_FOUND\",\"message\":\"No route for this path and method.\",\"details\":{\"path\":\"/v1/businesses\",\"method\":\"GET\"}}}";
        private static ApiException CapturedError()
        {
            var e = JObject.Parse(NoRoute)["error"];
            return new ApiException(404, (string)e["code"], (string)e["message"], e["details"]);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExactNoRouteEnvelopeIsUnavailable(bool write)
        {
            var e = CityErrors.From(CapturedError(), write);
            Assert.That(e.Code, Is.EqualTo("UNAVAILABLE"));
            Assert.That(e.Key, Is.EqualTo("err.unavailable"));
            Assert.That(e.OutcomeUnknown, Is.False);
            Assert.That(e.ServerMessage, Is.EqualTo("No route for this path and method."));
        }

        [TestCase("Not found.", "{\"kind\":\"business\",\"id\":\"fixture-business\"}")]
        [TestCase("Not found.", "{\"path\":\"/v1/businesses/fixture-business\",\"method\":\"GET\"}")]
        [TestCase("No route for this path and method.", "{\"kind\":\"business\",\"id\":\"fixture-business\"}")]
        [TestCase("No route for this path and method.", "{\"path\":\"/v1/businesses\"}")]
        public void EntityOrIncompleteEnvelopeStaysNotFound(string message, string details)
        {
            var e = CityErrors.From(new ApiException(404, "NOT_FOUND", message, JObject.Parse(details)), false);
            Assert.That(e.Code, Is.EqualTo("NOT_FOUND"));
            Assert.That(e.Key, Is.EqualTo("err.notFound"));
        }

        [Test]
        public void BusinessMissingRouteShowsReadOnlyNoticeAndDisablesBuying()
        {
            Services.TryGet<Session>(out var previousSession);
            Services.TryGet<ConfigStore>(out var previousConfig);
            object view = null;
            EditorWindow window = null;
            try
            {
                var session = new Session();
                typeof(Session).GetProperty("AccessToken").SetValue(session, "fixture-not-a-token");
                Services.Register(session);
                var config = new ConfigStore();
                config.Apply(Resources.Load<TextAsset>("Contract/config_snapshot").text, null, persist: false);
                Services.Register(config);
                // Isolate the shipping view and real REST business gateway from the hub's unrelated homes/background loop.
                var hub = (CityLifeHub)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(CityLifeHub));
                var rest = new MissingRouteRest();
                typeof(CityLifeHub).GetField("Businesses").SetValue(hub, new RestBusinessGateway(rest));
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var type = typeof(CityBusinessApp).GetNestedType("BizView", BindingFlags.NonPublic);
                view = Activator.CreateInstance(type, flags, null, new object[] { hub }, null);
                var root = (ScrollView)type.BaseType.GetField("Root", flags).GetValue(view);
                window = ScriptableObject.CreateInstance<BusinessReadTestWindow>();
                window.Show();
                window.rootVisualElement.Add(root);
                Assert.That(root.panel, Is.Not.Null);
                Assert.That(rest.Reads, Is.EqualTo(1));
                Assert.That(root.Query<Label>().ToList().Select(l => l.text), Does.Contain(CityLifeStrings.T("biz.notLive")));
                var buys = root.Query<Button>().ToList().Where(b => b.text == CityLifeStrings.T("biz.buy")).ToList();
                Assert.That(buys, Is.Not.Empty);
                Assert.That(buys.All(b => !b.enabledSelf), Is.True);
                Assert.That(rest.Writes, Is.Zero);
            }
            finally
            {
                (view as IDisposable)?.Dispose();
                if (window != null) window.Close();
                if (previousSession == null) Services.Unregister<Session>(); else Services.Register(previousSession);
                if (previousConfig == null) Services.Unregister<ConfigStore>(); else Services.Register(previousConfig);
            }
        }

        private sealed class BusinessReadTestWindow : EditorWindow { }
        private sealed class MissingRouteRest : IRestClient
        {
            public int Reads, Writes;
            public Func<Task<bool>> RefreshHandler { get; set; }
            public Task<T> Get<T>(string path, CancellationToken ct = default)
            {
                Assert.That(path, Is.EqualTo("/businesses"));
                Reads++;
                return Task.FromException<T>(CapturedError());
            }
            private Task<T> Reject<T>() { Writes++; throw new InvalidOperationException("Unexpected fixture write"); }
            public Task<T> Post<T>(string p, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Reject<T>();
            public Task<T> Put<T>(string p, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Patch<T>(string p, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Delete<T>(string p, CancellationToken ct = default) => Reject<T>();
            public Task<T> Send<T>(string method, string p, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Reject<T>();
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => Reject<RawResponse>();
        }
    }
}
#endif
