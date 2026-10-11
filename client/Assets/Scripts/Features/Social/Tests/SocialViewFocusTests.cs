#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CTLife.SocialFeatures.Tests
{
    // Exercise the shipping Spaces view and its scheduled render on a real attached panel.
    // Only REST is a fixture; no signed-in session, network or wallet is used.
    public class SocialViewFocusTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private EditorWindow _window;
        private SocialHub _hub;
        private object _view;
        private Type _viewType;
        private ScrollView _root;
        private IRestClient _previousRest;
        private Runner _previousRunner;
        private GameObject _testRunner;
        private static readonly FieldInfo RunnerInstance = typeof(Runner).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);

        [SetUp]
        public void SetUp()
        {
            Services.TryGet(out _previousRest);
            _previousRunner = (Runner)RunnerInstance.GetValue(null);
            // Install an inert editor runner: Runner.Instance's normal creation calls play-only DontDestroyOnLoad.
            _testRunner = new GameObject("social-focus-test-runner");
            RunnerInstance.SetValue(null, _testRunner.AddComponent<Runner>());
            Services.Register<IRestClient>(new SpacesListRest());
            _hub = (SocialHub)Activator.CreateInstance(typeof(SocialHub), Instance, null,
                new object[] { "social-focus-test-" + Guid.NewGuid().ToString("N") }, null);
            _viewType = typeof(SocialHub).Assembly.GetType("CTLife.SocialFeatures.SpacesStudioApp+View", true);
            _view = Activator.CreateInstance(_viewType, Instance, null, new object[] { _hub, null }, null);
            _root = (ScrollView)_viewType.BaseType.GetField("Root", Instance).GetValue(_view);
            _window = ScriptableObject.CreateInstance<FocusTestWindow>();
            _window.position = new Rect(40, 40, 600, 700);
            _window.Show();
            _window.rootVisualElement.Add(_root);
        }

        [TearDown]
        public void TearDown()
        {
            (_view as IDisposable)?.Dispose();
            _hub?.Dispose();
            if (_window != null) _window.Close();
            if (_previousRest != null) Services.Register(_previousRest);
            else Services.Unregister<IRestClient>();
            RunnerInstance.SetValue(null, _previousRunner);
            if (_testRunner != null) UnityEngine.Object.DestroyImmediate(_testRunner);
        }

        [UnityTest]
        public IEnumerator FocusedHostButtonDoesNotStarveComposeRender()
        {
            yield return WaitFor(() => Button("Host a Space") != null);
            var host = Button("Host a Space");
            host.Focus();
            yield return null;
            Assert.That(_root.panel, Is.Not.Null);
            Assert.That(_root.panel.focusController.focusedElement, Is.SameAs(host),
                "The regression requires a genuinely focused attached Button.");
            Submit(host);
            Assert.That((bool)_viewType.GetField("_compose", Instance).GetValue(_view), Is.True,
                "The shipping Host a Space handler must have requested compose before testing rendering.");
            Assert.That(_root.panel.focusController.focusedElement, Is.SameAs(host));
            // Drive the shipping scheduled callback while focus is still asserted; an editor window can
            // otherwise steal focus during a multi-frame wait and accidentally mask the device defect.
            Tick();
            Assert.That(_root.Q<TextField>(), Is.Not.Null,
                "Host a Space must render Topic without a second tap on a nonfocusable heading.");
            Assert.That(Button("Go live"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator BodyTextEditingPreservesDraftSelectionAndPendingRenderUntilBlur()
        {
            yield return OpenComposer();
            var topic = _root.Q<TextField>();
            topic.value = "unfinished topic";
            var input = topic.Q<VisualElement>("unity-text-input");
            Assert.That(input, Is.Not.Null);
            input.Focus();
            yield return null;
            var focused = _root.panel.focusController.focusedElement as VisualElement;
            Assert.That(focused == topic || topic.Contains(focused), Is.True,
                "Focus must be on the real text field or its editing descendant.");
            TestContext.WriteLine("Editing focus type: " + focused.GetType().FullName);
            topic.SelectRange(2, 8);
            var cursor = topic.cursorIndex;
            var selection = topic.selectIndex;
            var originalHeading = _root.Q<Label>(className: "ct-h1");
            _hub.Spaces.Message = "common.retry";
            _hub.NotifyChanged();
            yield return WaitForTicks();
            Assert.That(_root.Q<Label>(className: "ct-h1"), Is.SameAs(originalHeading),
                "Do not rebuild Body while its text input is being edited.");
            Assert.That(_root.Q<TextField>(), Is.SameAs(topic));
            Assert.That(topic.value, Is.EqualTo("unfinished topic"));
            Assert.That(topic.cursorIndex, Is.EqualTo(cursor));
            Assert.That(topic.selectIndex, Is.EqualTo(selection));
            Assert.That(Notice(), Is.Null);
            focused.Blur();
            yield return WaitFor(() => Notice() != null);
            Assert.That(Notice(), Is.Not.Null, "The queued render must survive editing and flush after blur.");
            Assert.That(topic.value, Is.EqualTo("unfinished topic"));
        }

        [UnityTest]
        public IEnumerator PersistentFooterTextFocusDoesNotBlockBodyRender()
        {
            yield return WaitFor(() => Button("Host a Space") != null);
            var footer = (VisualElement)_viewType.BaseType.GetField("Footer", Instance).GetValue(_view);
            var input = new TextField { value = "persistent composer" };
            footer.Add(input);
            input.Focus();
            yield return null;
            var focused = _root.panel.focusController.focusedElement as VisualElement;
            Assert.That(focused == input || input.Contains(focused), Is.True);
            _hub.Spaces.Message = "common.retry";
            _hub.NotifyChanged();
            yield return WaitFor(() => Notice() != null);
            Assert.That(Notice(), Is.Not.Null, "Only editing inside the replaced Body should defer rendering.");
            Assert.That(input.value, Is.EqualTo("persistent composer"));
            Assert.That(_root.panel.focusController.focusedElement, Is.SameAs(focused));
        }

        [UnityTest] public IEnumerator PointerUpReleasesDeferredRender() => PointerProtection("up");
        [UnityTest] public IEnumerator PointerCancelReleasesDeferredRender() => PointerProtection("cancel");
        [UnityTest] public IEnumerator PointerLeaveReleasesDeferredRender() => PointerProtection("leave");

        private IEnumerator PointerProtection(string release)
        {
            yield return WaitFor(() => Button("Host a Space") != null);
            var heading = _root.Q<Label>(className: "ct-h1");
            using (var down = PointerDownEvent.GetPooled())
            {
                down.target = heading;
                heading.SendEvent(down);
            }
            Assert.That((bool)_viewType.BaseType.GetField("_pressing", Instance).GetValue(_view), Is.True);
            _hub.Spaces.Message = "common.retry";
            _hub.NotifyChanged();
            // Tick synchronously so editor mouse-leave synthesis cannot end our injected gesture between frames.
            Tick();
            Assert.That(Notice(), Is.Null, "Do not replace controls beneath an active pointer.");
            Assert.That(_root.Q<Label>(className: "ct-h1"), Is.SameAs(heading));
            if (release == "up")
            {
                using (var evt = PointerUpEvent.GetPooled()) { evt.target = heading; heading.SendEvent(evt); }
            }
            else if (release == "cancel")
            {
                using (var evt = PointerCancelEvent.GetPooled()) { evt.target = heading; heading.SendEvent(evt); }
            }
            else
            {
                using (var evt = PointerLeaveEvent.GetPooled()) { evt.target = _root; _root.SendEvent(evt); }
            }
            Tick();
            Assert.That(Notice(), Is.Not.Null, "Pending render must resume after pointer " + release + ".");
        }

        private IEnumerator OpenComposer()
        {
            yield return WaitFor(() => Button("Host a Space") != null);
            var host = Button("Host a Space");
            host.Focus();
            yield return null;
            Submit(host);
            (_root.panel.focusController.focusedElement as VisualElement)?.Blur(); // Test editing independently of button focus.
            yield return WaitFor(() => _root.Q<TextField>() != null);
            Assert.That(_root.Q<TextField>(), Is.Not.Null);
        }

        private IEnumerator WaitForTicks()
        {
            var deadline = EditorApplication.timeSinceStartup + .8;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                _window.Repaint();
                yield return null;
            }
        }

        private Label Notice() => _root.Query<Label>().Where(l => l.text == "Try again").First();
        private void Tick() => _viewType.BaseType.GetMethod("OnTick", Instance).Invoke(_view, null);
        private Button Button(string text) => _root.Query<Button>().Where(b => b.text == text).First();

        private static void Submit(Button button)
        {
            using (var evt = NavigationSubmitEvent.GetPooled()) button.SendEvent(evt);
        }

        private IEnumerator WaitFor(Func<bool> ready)
        {
            var deadline = EditorApplication.timeSinceStartup + 3;
            while (!ready() && EditorApplication.timeSinceStartup < deadline)
            {
                _window.Repaint();
                yield return null;
            }
        }

        private sealed class FocusTestWindow : EditorWindow { }

        private sealed class SpacesListRest : IRestClient
        {
            public Func<Task<bool>> RefreshHandler { get; set; }
            public Task<T> Get<T>(string path, CancellationToken ct = default)
            {
                Assert.That(path, Is.EqualTo("/spaces?live=true"));
                return Task.FromResult((T)(object)JToken.Parse("{\"spaces\":[]}"));
            }
            public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Put<T>(string path, object body, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Patch<T>(string path, object body, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Delete<T>(string path, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Send<T>(string method, string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => throw new NotSupportedException();
        }
    }
}
#endif
