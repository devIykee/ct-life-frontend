using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    /// <summary>
    /// HUD venue controls found on the Seeker against Railway staging (2026-10-10): the venue mic kept recording in the Lobby.
    /// Only the microphone is a fake; the shipping Hud is constructed as the game builds it.
    /// </summary>
    public class HudMicLobbyTests
    {
        private sealed class FakeMic : IVenueMic
        {
            public bool MicOn { get; set; }
            public readonly List<bool> Requests = new List<bool>();
            public Task<bool> SetMic(bool on, CancellationToken owner, CancellationToken gesture)
            {
                Requests.Add(on);
                MicOn = on;
                return Task.FromResult(true);
            }
        }

        private FakeMic _mic;
        private Hud _hud;

        [SetUp] public void SetUp() { _mic = new FakeMic(); _hud = new Hud(() => _mic); }
        [TearDown] public void TearDown() => _hud?.Dispose();

        [Test]
        public void LeavingTheHudForTheLobbyTurnsALiveMicOff()
        {
            _mic.MicOn = true;
            _hud.Show(false); // FlowController.ShowLobby: the Lobby has no mic control.
            CollectionAssert.AreEqual(new[] { false }, _mic.Requests, "a hidden HUD must not leave the venue mic recording");
            Assert.IsFalse(_mic.MicOn);
        }

        [Test]
        public void HidingWithTheMicAlreadyOffAsksNothing()
        {
            _hud.Show(false);
            CollectionAssert.IsEmpty(_mic.Requests);
        }

        [Test]
        public void ShowingTheHudLeavesTheMicAlone()
        {
            _mic.MicOn = true;
            _hud.Show(true); // Back from the Lobby: the player turns the mic on again with the HUD button.
            CollectionAssert.IsEmpty(_mic.Requests);
            Assert.IsTrue(_mic.MicOn);
        }
    }

#if UNITY_INCLUDE_TESTS // [UnityTest] needs Unity's runner; the offline compile harness has no test runner assembly.
    /// <summary>
    /// The venue chat bar on a real attached panel (Seeker, Railway staging 2026-10-10): no bubble over our own avatar after
    /// sending (the room's `said` skips the sender), and the soft keyboard stayed up after "Say it". Only the room is a fake.
    /// Unity 6000.3 TouchScreenTextEditor.OnFocusOutEvent closes the keyboard only when the field's focus goes nowhere (or to
    /// a text element with different keyboard settings); focus moving to the "Say it" Button (a TextElement with the same
    /// defaults) only "partially dismisses" it, leaving the IME on screen.
    /// </summary>
    public class ChatBarVenueTests
    {
        private sealed class FakeRoom : IChatRoom
        {
            public string SessionId { get; set; } = "me-session";
            public float Wait;
            public readonly List<string> Said = new List<string>();
            public Transform OwnBubbleAnchor { get; set; }
            public float Say(string text) { Said.Add(text); return Wait; }
        }

        private sealed class ChatTestWindow : EditorWindow { }

        private EditorWindow _window;
        private ChatBar _chat;
        private FakeRoom _room;
        private GameObject _head;
        private Action _unsubscribe;
        private readonly List<BubbleSignal> _bubbles = new List<BubbleSignal>();
        private readonly List<VisualElement> _focusOutRelated = new List<VisualElement>();

        [SetUp]
        public void SetUp()
        {
            _bubbles.Clear(); _focusOutRelated.Clear();
            _head = new GameObject("chat-test-head");
            _room = new FakeRoom { OwnBubbleAnchor = _head.transform };
            _chat = new ChatBar(() => _room);
            _unsubscribe = Bus.Subscribe<BubbleSignal>(b => _bubbles.Add(b));
            Field.RegisterCallback<FocusOutEvent>(e => _focusOutRelated.Add(e.relatedTarget as VisualElement), TrickleDown.TrickleDown);
            _window = ScriptableObject.CreateInstance<ChatTestWindow>();
            _window.position = new Rect(40, 40, 700, 420);
            _window.Show();
            _window.rootVisualElement.Add(_chat.Element);
        }

        [TearDown]
        public void TearDown()
        {
            _unsubscribe?.Invoke();
            _chat?.Dispose();
            if (_window != null) _window.Close();
            if (_head != null) UnityEngine.Object.DestroyImmediate(_head);
        }

        private TextField Field => _chat.Element.Q<TextField>();
        private Button SayButton => _chat.Element.Query<Button>().Where(b => b.text == Strings.T("hud.say")).First();
        private VisualElement Focused => _chat.Element.panel?.focusController?.focusedElement as VisualElement;
        private bool FieldFocused => Focused != null && (Focused == Field || Field.Contains(Focused));
        private bool Open => _chat.Element.style.display.value != DisplayStyle.None;
        private string[] Lines => _chat.Element.Query<Label>(className: "ct-chat-line").ToList().Select(l => l.text).ToArray();
        private string FocusName => Focused == null ? "nothing" : Focused.GetType().Name + (Focused is TextElement t ? " \"" + t.text + "\"" : "");

        /// <summary>Opens the bar with the HUD chat button's own path (Toggle) and types a line.</summary>
        private IEnumerator OpenWith(string text)
        {
            var deadline = EditorApplication.timeSinceStartup + 3;
            while (_chat.Element.resolvedStyle.display != DisplayStyle.None && EditorApplication.timeSinceStartup < deadline)
            {
                _window.Repaint();
                yield return null;
            }
            Assert.That(_chat.Element.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "precondition: the bar starts hidden");
            _chat.Toggle();
            Field.value = text;
            Assert.That(FieldFocused, Is.True, "precondition: the HUD chat button focuses the field (focus: " + FocusName + ")");
            _focusOutRelated.Clear();
        }

        private void PointerDownOnSay()
        {
            using (var down = PointerDownEvent.GetPooled()) { down.target = SayButton; SayButton.SendEvent(down); }
        }

        private void ClickSay()
        {
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = SayButton; SayButton.SendEvent(submit); }
        }

        [UnityTest]
        public IEnumerator PressingSayItDoesNotTakeFocusFromTheField()
        {
            yield return OpenWith("gm");
            PointerDownOnSay(); // The device tap's first half; the click comes on pointer up.
            Assert.That(FieldFocused, Is.True, "pressing Say it moved focus to " + FocusName +
                ": Unity then only partially dismisses the soft keyboard and nothing closes it after the send");
            CollectionAssert.IsEmpty(_focusOutRelated, "the field must not lose focus to the Say it button");
        }

        [UnityTest]
        public IEnumerator SendingReleasesTheFieldSoTheKeyboardCloses()
        {
            yield return OpenWith("gm");
            ClickSay();
            CollectionAssert.AreEqual(new[] { "gm" }, _room.Said);
            Assert.That(Open, Is.False, "the bar hides after a sent line");
            Assert.That(FieldFocused, Is.False, "the sent field is still focused (" + FocusName + "): the IME stays over the screen");
            Assert.That(_focusOutRelated.Count, Is.GreaterThan(0), "the field never lost focus");
            Assert.That(_focusOutRelated.Last(), Is.Null, "focus must go nowhere, not to " + _focusOutRelated.Last()?.GetType().Name);
        }

        [UnityTest]
        public IEnumerator RateLimitedSayKeepsTheFieldFocusedForARetry()
        {
            yield return OpenWith("gm again");
            _room.Wait = 1.2f;
            ClickSay();
            Assert.That(Open, Is.True);
            Assert.That(Field.value, Is.EqualTo("gm again"));
            Assert.That(FieldFocused, Is.True, "a throttled line keeps the keyboard so the player can retry (focus: " + FocusName + ")");
            CollectionAssert.IsEmpty(_bubbles, "nothing was said, so no bubble");
            CollectionAssert.IsEmpty(Lines);
        }

        [UnityTest]
        public IEnumerator HidingTheBarReleasesTheField()
        {
            yield return OpenWith("draft");
            _chat.Hide(); // Escape, the emote button, or the HUD hiding for the Lobby.
            Assert.That(FieldFocused, Is.False, "a hidden chat field kept focus (" + FocusName + ")");
            Assert.That(_focusOutRelated.LastOrDefault(), Is.Null);
        }

        [UnityTest]
        public IEnumerator TogglingTheBarClosedReleasesTheField()
        {
            yield return OpenWith("draft");
            _chat.Toggle(); // The HUD chat button again.
            Assert.That(Open, Is.False);
            Assert.That(FieldFocused, Is.False, "a hidden chat field kept focus (" + FocusName + ")");
        }

        [UnityTest]
        public IEnumerator SentLineShowsOurOwnBubbleOverOurHead()
        {
            yield return OpenWith("gm");
            ClickSay();
            Assert.That(_bubbles.Count, Is.EqualTo(1), "the room's said skips the sender, so our bubble must come from the send");
            var bubble = _bubbles[0];
            Assert.That(bubble.anchor, Is.SameAs(_head.transform));
            Assert.That(bubble.text, Is.EqualTo("gm"));
            Assert.That(bubble.isNpc, Is.False);
            Assert.That(bubble.seconds, Is.EqualTo(4.5f));
            CollectionAssert.AreEqual(new[] { "You: gm" }, Lines);
        }

        [UnityTest]
        public IEnumerator AnEchoOfOurOwnLineIsNotShownTwice()
        {
            yield return OpenWith("gm");
            ClickSay();
            Bus.Publish(new SaidMessage { sessionId = _room.SessionId, text = "gm" }); // A server that does echo to the sender.
            CollectionAssert.AreEqual(new[] { "You: gm" }, Lines);
            Bus.Publish(new SaidMessage { sessionId = "other-session", text = "gm back" });
            CollectionAssert.AreEqual(new[] { "You: gm", "Someone: gm back" }, Lines);
        }
    }
#endif
}
