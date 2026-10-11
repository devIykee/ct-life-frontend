#if UNITY_INCLUDE_TESTS && !CTLIFE_PROD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CTLife.App;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using CTLife.UI;
using CTLife.UI.Kit;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    /// <summary>
    /// Real controller/screens/API/profile, controlled transport only. Runs in an empty Editor PlayMode test scene;
    /// stops at city connection before HUD/world construction. No wallet, network, existing-account prefs or production
    /// bootstrap. Reduced motion keeps this a sequencing/error-path test; device QA must verify the normal animation.
    /// </summary>
    public sealed class OnboardingControllerTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private Dictionary<Type, object> _services;
        private GameObject _flowObject;
        private FlowController _flow;
        private UiRoot _ui;
        private RenderTexture _target;
        private Task _enter;
        private ControlledRest _rest;
        private RealtimeBarrier _realtime;
        private PlayerProfile _profile;
        private string _account;
        private bool? _previousReducedMotion;
        private bool _hadRunner;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.IsTrue(Application.isEditor, "This fixture is intentionally Editor-only execution.");
            Assert.IsTrue(Application.isPlaying, "Run this fixture with testPlatform PlayMode in the Editor.");
            Assert.IsNull(UiRoot.Instance, "Run in an empty test scene, not over the user's running game.");
            Assert.IsNull(UnityEngine.Object.FindAnyObjectByType<FlowController>(), "A game flow is already running.");
            Assert.IsFalse(LaunchArgs.Flag("autocreate"), "This fixture drives creator confirmation itself.");
            _services = new Dictionary<Type, object>(ServiceMap());
            _hadRunner = UnityEngine.Object.FindAnyObjectByType<Runner>() != null;
            _previousReducedMotion = (bool?)typeof(DeviceFeel).GetField("_reducedMotionOverride", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            DeviceFeel.OverrideReducedMotion(true);
            _account = Guid.NewGuid().ToString("D");
            var config = new ConfigStore();
            var snapshot = Resources.Load<TextAsset>("Contract/config_snapshot");
            Assert.IsNotNull(snapshot);
            config.Apply(snapshot.text, null, persist: false);
            _rest = new ControlledRest(_account);
            var api = new Api(_rest, config);
            _profile = new PlayerProfile(api);
            _realtime = new RealtimeBarrier();
            Services.Register(config);
            Services.Register(api);
            Services.Register(_profile);
            Services.Register(new Session()); // no SetTokens: do not overwrite the user's saved session
            Services.Register<IRealtimeService>(_realtime);
            _ui = UiRoot.Create();
            _target = new RenderTexture(1335, 600, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _target.Create();
            _ui.GetComponent<UIDocument>().panelSettings.targetTexture = _target;
            _ui.GetComponent<UIDocument>().panelSettings.clearColor = true;
            _ui.GetComponent<UIDocument>().panelSettings.colorClearValue = Color.black;
            _flowObject = new GameObject("OnboardingControllerTest");
            _flow = _flowObject.AddComponent<FlowController>(); // actual Awake creates the owned lifetime
            _flow.enabled = false; // never execute broad startup/overlay/auth work
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_flowObject != null) UnityEngine.Object.Destroy(_flowObject);
            yield return null; // actual OnDestroy cancels the pending realtime boundary and closes the screen
            string flowFailure = null;
            if (_enter != null)
            {
                float deadline = Time.realtimeSinceStartup + 3;
                while (!_enter.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                if (!_enter.IsCompleted) flowFailure = "Controller did not settle after its owner was destroyed.";
                else if (_enter.IsFaulted) flowFailure = _enter.Exception.ToString();
            }
            if (_ui != null) UnityEngine.Object.Destroy(_ui.gameObject);
            var runner = UnityEngine.Object.FindAnyObjectByType<Runner>();
            if (!_hadRunner && runner != null) UnityEngine.Object.Destroy(runner.gameObject);
            yield return null;
            if (_target != null) { _target.Release(); UnityEngine.Object.Destroy(_target); }
            if (!string.IsNullOrEmpty(_account))
            {
                new CreatorDraftStore(_account).Clear();
                new OnboardingTransactionStore(_account).Clear();
            }
            DeviceFeel.OverrideReducedMotion(_previousReducedMotion);
            if (_services != null)
            {
                var map = ServiceMap();
                map.Clear();
                foreach (var pair in _services) map.Add(pair.Key, pair.Value);
            }
            _enter = null;
            Assert.IsNull(flowFailure);
        }

        [UnityTest]
        public IEnumerator CreatorUsesAutomaticFramingAndOpaqueInteractiveWardrobeOnly()
        {
            StartFlow();
            yield return Until(() => Current is CharacterCreatorScreen);
            var creator = (CharacterCreatorScreen)Current;
            var models = (List<BaseModel>)typeof(CharacterCreatorScreen).GetField("_models", Private).GetValue(creator);
            creator.PickForAutomation(models.FindIndex(m => m.id == "head_rain"));
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(.8f);
            CaptureReviewFrame("01-pick-character");
            var renderer = (AvatarPreview)typeof(CharacterCreatorScreen).GetField("_preview", Private).GetValue(creator);
            var surface = creator.Element.Q<VisualElement>(className: "ob-stage-model");
            Assert.IsFalse(renderer.Portrait);
            Assert.IsTrue(renderer.Podium);
            Assert.AreEqual(PickingMode.Position, surface.pickingMode);
            Assert.That(surface.style.opacity.value, Is.EqualTo(1f), "Model selection must not fade the transparent render texture.");
            Assert.IsNull(creator.Element.Q(className: "ob-zoom"));
            Assert.That(creator.Element.Q<Button>("ob-likeness-open").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            creator.PressCta();
            yield return Until(() => creator.Step == CreatorStep.Customize);
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(.8f);
            CaptureReviewFrame("02-make-it-yours");
            Assert.IsTrue(renderer.Podium);
            Assert.IsFalse(renderer.Portrait);
            Assert.That(creator.Element.Q<Button>("ob-likeness-open").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            creator.PressCta();
            yield return Until(() => creator.Step == CreatorStep.Tribe);
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(.8f);
            CaptureReviewFrame("03-find-your-people");
            Assert.IsTrue(renderer.Portrait);
            Assert.IsFalse(renderer.Podium);
            Assert.AreEqual(PickingMode.Ignore, surface.pickingMode);
            Assert.AreEqual(0, _rest.Posts.Count);
        }

        private void CaptureReviewFrame(string name)
        {
            var directory = Environment.GetEnvironmentVariable("CTLIFE_ONBOARDING_CAPTURE_OUT");
            if (string.IsNullOrEmpty(directory)) return;
            System.IO.Directory.CreateDirectory(directory);
            var previous = RenderTexture.active;
            var image = new Texture2D(_target.width, _target.height, TextureFormat.RGBA32, false);
            try
            {
                typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility")?
                    .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "RenderOffscreenPanels" && m.GetParameters().Length == 0)?.Invoke(null, null);
                RenderTexture.active = _target;
                image.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0); image.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(image); }
        }

        [UnityTest]
        public IEnumerator CreatorBackWinsPickingOverTheEnlargedPreviewWhileOverlayStaysAboveIt()
        {
            StartFlow();
            yield return Until(() => Current is CharacterCreatorScreen);
            var creator = (CharacterCreatorScreen)Current;
            var models = (List<BaseModel>)typeof(CharacterCreatorScreen).GetField("_models", Private).GetValue(creator);
            int snow = models.FindIndex(m => m.id == "head_snow");
            Assert.GreaterOrEqual(snow, 0, "Use the real shipped body with authored outfit options.");
            creator.PickForAutomation(snow);
            creator.PressCta();
            yield return Until(() => creator.Step == CreatorStep.Customize);
            var frame = (OnboardingFrame)typeof(CharacterCreatorScreen).GetField("_frame", Private).GetValue(creator);
            var preview = creator.Element.Q<VisualElement>(className: "ob-stage-model");
            Assert.IsNotNull(preview);
            yield return Until(() => frame.Back.panel != null && frame.Back.worldBound.width > 0 && preview.worldBound.width > 0);
            // Let layout and picking caches observe the step's real USS geometry.
            yield return null;
            yield return null;
            var point = frame.Back.worldBound.center;
            Assert.IsTrue(preview.worldBound.Contains(point),
                "Exercise the observed overlap: the enlarged turntable extends behind Back in Customize.");
            Assert.Greater(frame.Root.IndexOf(frame.TopBar), frame.Root.IndexOf(frame.Content));
            Assert.Less(frame.Root.IndexOf(frame.TopBar), frame.Root.IndexOf(frame.Overlay),
                "Moving chrome above the preview must not put it over modal content.");
            var picked = frame.Back.panel.Pick(point);
            Assert.IsTrue(picked == frame.Back || frame.Back.Contains(picked),
                "Back centre must hit the button, not the avatar drag surface. Picked: " + picked?.name);

            var shield = new VisualElement { name = "test-modal-input-shield", pickingMode = PickingMode.Position };
            shield.style.position = UnityEngine.UIElements.Position.Absolute;
            shield.style.left = shield.style.right = shield.style.top = shield.style.bottom = 0;
            frame.Overlay.Add(shield);
            try
            {
                yield return null;
                yield return null;
                Assert.AreSame(shield, frame.Back.panel.Pick(point), "An active overlay must still intercept this touch.");
            }
            finally { shield.RemoveFromHierarchy(); }
            frame.Back.Press();
            Assert.AreEqual(CreatorStep.Look, creator.Step);
            Assert.AreEqual(0, _rest.Posts.Count, "Navigation must not roll or create a character.");
        }

        [UnityTest]
        public IEnumerator FreshPlayerChoosesBeforeAutomaticRollAndCreatesOnlyAfterResultContinue()
        {
            StartFlow();
            yield return Until(() => Current is CharacterCreatorScreen);
            Assert.AreEqual(0, _rest.Posts.Count, "Opening the creator must not roll or create.");
            Assert.IsTrue(((CharacterCreatorScreen)Current).AutoSubmitForAutomation("flowfresh"));
            yield return Result(); // automatic OnShown path; no AutoRoll/roll delegate invocation from this test
            Assert.AreEqual(1, _rest.RollCount);
            Assert.AreEqual(0, _rest.CreateCount, "The player must be able to read their result before creation.");
            AssertReceipt();
            Continue();
            yield return Until(() => _realtime.CityAttempts == 1);
            CollectionAssert.AreEqual(new[] { "/lottery/roll", "/characters" }, _rest.Posts.Select(p => p.Path));
            Assert.AreEqual("flowfresh", _profile.Me.Character.Alias);
            Assert.IsNull(Current);
        }

        [UnityTest]
        public IEnumerator LostCreateResponseReconcilesServerCharacterWithoutSecondRollOrCreate()
        {
            _rest.Failure = CreateFailure.LostCommittedResponse;
            StartFlow();
            yield return Until(() => Current is CharacterCreatorScreen);
            Assert.IsTrue(((CharacterCreatorScreen)Current).AutoSubmitForAutomation("flowlost"));
            yield return Result();
            Continue();
            yield return Until(() => _realtime.CityAttempts == 1);
            Assert.AreEqual(1, _rest.RollCount);
            Assert.AreEqual(1, _rest.CreateCount);
            Assert.That(_rest.Events.Last(), Is.EqualTo("GET /me"), "The lost write must be reconciled from /me.");
            Assert.AreEqual("flowlost", _profile.Me.Character.Alias);
            AssertReceipt();
        }

        [UnityTest]
        public IEnumerator ExistingDifferentCharacterDoesNotReconcileAFailedCreate() =>
            DifferentSavedCharacterRetainsDraft(CreateFailure.DifferentExistingCharacter);

        [UnityTest]
        public IEnumerator SuccessfulResponseWithDifferentSavedCharacterDoesNotCompleteOnboarding() =>
            DifferentSavedCharacterRetainsDraft(CreateFailure.MismatchedSuccess);

        private IEnumerator DifferentSavedCharacterRetainsDraft(CreateFailure failure)
        {
            _rest.Failure = failure;
            StartFlow();
            yield return Until(() => Current is CharacterCreatorScreen);
            Assert.IsTrue(((CharacterCreatorScreen)Current).AutoSubmitForAutomation("intendedcharacter"));
            yield return Result();
            var result = Current;
            Continue();
            yield return null;
            Assert.AreEqual(1, _rest.CreateCount);
            Assert.AreEqual(0, _realtime.CityAttempts, "An unrelated existing character is not evidence that this request committed.");
            Assert.AreSame(result, Current, "Keep the player's result and saved draft available for resolution.");
            Assert.IsTrue(Cta.enabledInHierarchy);
            Assert.AreEqual("intendedcharacter", new CreatorDraftStore(_account).Load()?.alias);
            var message = result.Element.Q<Label>(className: "ob-error");
            Assert.That(message.text, Is.EqualTo(Strings.T("ob.save.characterMismatch")));
            Assert.That(message.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            AssertReceipt();
        }

        [UnityTest]
        public IEnumerator ValidationEditPreservesDraftAndReceiptAndDoesNotRollAgain()
        {
            _rest.Failure = CreateFailure.Validation;
            StartFlow();
            yield return Until(() => Current is CharacterCreatorScreen);
            Assert.IsTrue(((CharacterCreatorScreen)Current).AutoSubmitForAutomation("takenalias"));
            yield return Result();
            var firstResult = Current;
            Continue();
            yield return Until(() => Current is CharacterCreatorScreen);
            var corrected = (CharacterCreatorScreen)Current;
            Assert.AreEqual("takenalias", corrected.Flow.Draft.alias);
            Assert.IsTrue(corrected.Flow.Draft.ageConfirmed, "Consent remains valid during this same live session.");
            Assert.AreEqual(CreatorStep.Confirm, corrected.Step);
            Assert.AreEqual(1, _rest.RollCount);
            Assert.IsTrue(corrected.AutoSubmitForAutomation("freealias"));
            yield return Result();
            Assert.AreNotSame(firstResult, Current);
            Assert.AreEqual(1, _rest.RollCount, "The receipt must reveal directly after correction.");
            AssertReceipt();
            Continue();
            yield return Until(() => _realtime.CityAttempts == 1);
            var creates = _rest.Posts.Where(p => p.Path == "/characters").ToArray();
            Assert.AreEqual(2, creates.Length);
            Assert.AreNotEqual(creates[0].Key, creates[1].Key, "A changed body needs a new request key.");
            Assert.AreEqual("freealias", _profile.Me.Character.Alias);
            Assert.AreEqual(1, _rest.RollCount);
        }

        [UnityTest]
        public IEnumerator UncommittedNetworkFailureRetriesSameCharacterKeyOnTheSameResult()
        {
            _rest.Failure = CreateFailure.NetworkBeforeCommit;
            StartFlow();
            yield return Until(() => Current is CharacterCreatorScreen);
            Assert.IsTrue(((CharacterCreatorScreen)Current).AutoSubmitForAutomation("flowretry"));
            yield return Result();
            var result = Current;
            Continue();
            yield return Until(() => _rest.CreateCount == 1 && Cta.enabledInHierarchy);
            Assert.AreSame(result, Current, "A retryable failure must keep the origin result visible.");
            Assert.AreEqual(0, _realtime.CityAttempts);
            AssertReceipt();
            Continue();
            yield return Until(() => _realtime.CityAttempts == 1);
            var creates = _rest.Posts.Where(p => p.Path == "/characters").ToArray();
            Assert.AreEqual(2, creates.Length);
            Assert.AreEqual(creates[0].Key, creates[1].Key);
            Assert.AreEqual(creates[0].Body, creates[1].Body);
            Assert.AreEqual(1, _rest.RollCount);
        }

        [UnityTest]
        public IEnumerator ReturningCharacterSkipsBothScreensEvenWithStaleLotteryFlag()
        {
            _rest.MakeReturningPlayer();
            StartFlow();
            yield return Until(() => _realtime.CityAttempts == 1);
            Assert.IsNull(Current);
            Assert.AreEqual(0, _rest.Posts.Count);
            Assert.AreEqual(0, _ui.ScreenLayer.childCount);
        }

        private UiScreen Current => (UiScreen)typeof(FlowController).GetField("_flowScreen", Private).GetValue(_flow);
        private LippedButton Cta => Current?.Element?.Q<LippedButton>("ob-cta");
        private void StartFlow() => _enter = (Task)typeof(FlowController).GetMethod("EnterGame", Private).Invoke(_flow, null);
        private IEnumerator Result() => Until(() => Current is LotteryScreen lottery && lottery.Revealed && !lottery.Rolling && Cta.enabledInHierarchy);
        private void Continue()
        {
            Assert.IsInstanceOf<LotteryScreen>(Current);
            Assert.IsNotNull(Cta);
            Assert.IsTrue(Cta.enabledInHierarchy);
            Cta.Press(); // public button path invokes its real registered handler, not a fixture callback
        }
        private IEnumerator Until(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!condition())
            {
                if (_enter?.IsFaulted == true) Assert.Fail(_enter.Exception.ToString());
                Assert.Less(Time.realtimeSinceStartup, deadline, "Controller stopped at " + Current?.GetType().Name + "; " + string.Join(", ", _rest.Events));
                yield return null;
            }
        }
        private void AssertReceipt()
        {
            var receipt = new OnboardingTransactionStore(_account).Receipt;
            Assert.IsNotNull(receipt);
            Assert.AreEqual(PostLotteryRoll200Response.OutcomeEnum.Insider, receipt.Outcome);
            Assert.AreEqual(300000, receipt.CashCents);
            Assert.AreEqual("studio", receipt.HomeId);
        }
        private static Dictionary<Type, object> ServiceMap() => (Dictionary<Type, object>)typeof(Services)
            .GetField("_map", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        private enum CreateFailure { None, LostCommittedResponse, Validation, NetworkBeforeCommit, DifferentExistingCharacter, MismatchedSuccess }
        private sealed class PostRecord { public string Path, Key, Body; }
        private sealed class ControlledRest : IRestClient
        {
            private readonly JObject _me;
            public CreateFailure Failure;
            public readonly List<PostRecord> Posts = new List<PostRecord>();
            public readonly List<string> Events = new List<string>();
            public int RollCount => Posts.Count(p => p.Path == "/lottery/roll");
            public int CreateCount => Posts.Count(p => p.Path == "/characters");
            public Func<Task<bool>> RefreshHandler { get; set; }
            public ControlledRest(string account)
            {
                _me = new JObject
                {
                    ["user"] = new JObject { ["id"] = account, ["wallet"] = "controller-test-only", ["isSeeker"] = false, ["hasCharacter"] = false },
                    ["character"] = null,
                    ["needs"] = new JObject { ["energy"] = 50, ["hunger"] = 50, ["fun"] = 50, ["hygiene"] = 50, ["sanity"] = 50 },
                    ["skills"] = new JObject { ["clout"] = 1, ["coding"] = 1, ["trading"] = 1, ["writing"] = 1, ["design"] = 1, ["security"] = 1 },
                    ["cashCents"] = 0, ["debtCents"] = 0, ["homeId"] = "parents_basement", ["minerTier"] = "none",
                    ["flags"] = new JObject { ["lotteryRolled"] = false },
                };
            }
            public void MakeReturningPlayer()
            {
                var look = CreatorLookCatalog.FallbackLook("controller-returning-test");
                Commit(JObject.Parse(OnboardingRequest.CharacterBody("returning", look, new[] { "fast_talker", "iron_will" }, "make_10k", "trencher", true)));
                _me["flags"]["lotteryRolled"] = false; // migration compatibility case
            }
            public Task<T> Get<T>(string path, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                Events.Add("GET " + path);
                if (path == "/me" && typeof(T) == typeof(JToken)) return Task.FromResult((T)(object)_me.DeepClone());
                return Unexpected<T>("GET " + path);
            }
            public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                Events.Add("POST " + path);
                Posts.Add(new PostRecord { Path = path, Key = idempotencyKey, Body = body as string });
                if (path == "/lottery/roll")
                {
                    _me["flags"]["lotteryRolled"] = true;
                    _me["cashCents"] = 300000;
                    _me["homeId"] = "studio";
                    return Task.FromResult((T)(object)new PostLotteryRoll200Response(PostLotteryRoll200Response.OutcomeEnum.Insider, 300000, 0, "studio"));
                }
                if (path == "/characters")
                {
                    if (CreateCount == 1 && (Failure == CreateFailure.DifferentExistingCharacter || Failure == CreateFailure.MismatchedSuccess))
                    {
                        var previous = JObject.Parse((string)body);
                        previous["alias"] = "earliercharacter";
                        previous.Remove("likenessJobId");
                        Commit(previous);
                        if (Failure == CreateFailure.DifferentExistingCharacter)
                            return Task.FromException<T>(new ApiException(409, "ALREADY_CREATED", "A character already exists."));
                        return Task.FromResult((T)(object)Core.Json.Deserialize<Character>(_me["character"].ToString()));
                    }
                    if (CreateCount == 1 && Failure == CreateFailure.Validation)
                        return Task.FromException<T>(new ApiException(409, "ALIAS_TAKEN", "Choose another alias."));
                    if (CreateCount == 1 && Failure == CreateFailure.NetworkBeforeCommit)
                        return Task.FromException<T>(new ApiException(0, "NETWORK_ERROR", "Controlled disconnect.", isNetwork: true));
                    Commit(JObject.Parse((string)body));
                    if (CreateCount == 1 && Failure == CreateFailure.LostCommittedResponse)
                        return Task.FromException<T>(new ApiException(0, "NETWORK_ERROR", "Controlled lost response.", isNetwork: true));
                    return Task.FromResult((T)(object)Core.Json.Deserialize<Character>(_me["character"].ToString()));
                }
                return Unexpected<T>("POST " + path);
            }
            private void Commit(JObject body)
            {
                _me["character"] = body.DeepClone();
                _me["user"]["hasCharacter"] = true;
                _me["flags"]["hasCharacter"] = true;
            }
            private static Task<T> Unexpected<T>(string operation) => Task.FromException<T>(new InvalidOperationException("Unexpected fixture transport: " + operation));
            public Task<T> Put<T>(string p, object body, CancellationToken ct = default) => Unexpected<T>(p);
            public Task<T> Patch<T>(string p, object body, CancellationToken ct = default) => Unexpected<T>(p);
            public Task<T> Delete<T>(string p, CancellationToken ct = default) => Unexpected<T>(p);
            public Task<T> Send<T>(string method, string p, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Unexpected<T>(p);
            public Task<RawResponse> SendRaw(string method, string p, string body, bool auth, string key, string etag, CancellationToken ct) => Unexpected<RawResponse>(p);
        }

        private sealed class RealtimeBarrier : IRealtimeService
        {
            public int CityAttempts;
            public CityChannel City => null;
            public MarketChannel Market => null;
            public VenueChannel Venue => null;
            public event Action<VenueChannel> VenueJoined { add { } remove { } }
            public event Action VenueLeft { add { } remove { } }
            public Task ConnectCity(CancellationToken ct = default) { CityAttempts++; return Task.Delay(Timeout.Infinite, ct); }
            public Task ConnectMarket(CancellationToken ct = default) => throw new InvalidOperationException("The test must stop at the city boundary.");
            public void KeepPersistentRoomsAlive(CancellationToken owner) => throw new InvalidOperationException("The test must stop before world setup.");
            public Task<VenueChannel> JoinVenue(string venueId, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<VenueChannel> JoinVenueHandoff(string venueId, CancellationToken ct = default) => throw new NotSupportedException();
            public Task LeaveVenue() => Task.CompletedTask;
            public Task DisconnectAll() => Task.CompletedTask;
        }
    }
}
#endif
