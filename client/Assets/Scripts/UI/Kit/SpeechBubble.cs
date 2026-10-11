using UnityEngine.UIElements;

namespace CTLife.UI.Kit
{
    /// <summary>
    /// A short line from the character: white bubble, plum outline, a tail pointing at the speaker. Pops in (220 ms
    /// ease-out-back, scale 0.8 to 1, kit.uss) and never blocks input (picking ignored). Rich text is off: the line can
    /// contain the player's alias.
    /// </summary>
    public sealed class SpeechBubble : VisualElement
    {
        public enum Tail { BottomLeft, Left, BottomRight }

        private readonly Label _text;
        private readonly VisualElement _tail;
        private IVisualElementScheduledItem _in, _hide;

        public SpeechBubble(Tail tail = Tail.BottomLeft)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("kit-bubble");
            _text = new Label { enableRichText = false, pickingMode = PickingMode.Ignore };
            _text.AddToClassList("kit-bubble__text");
            _tail = new VisualElement { pickingMode = PickingMode.Ignore };
            _tail.AddToClassList("kit-bubble__tail");
            Add(_tail);
            Add(_text);
            SetTail(tail);
        }

        public string Text => _text.text;

        public void SetTail(Tail tail)
        {
            _tail.EnableInClassList("kit-bubble__tail--bottom-left", tail == Tail.BottomLeft);
            _tail.EnableInClassList("kit-bubble__tail--left", tail == Tail.Left);
            _tail.EnableInClassList("kit-bubble__tail--bottom-right", tail == Tail.BottomRight);
        }

        /// <summary>Shows `line` with a pop. `holdSeconds` &gt; 0 hides it again after that long.</summary>
        public void Say(string line, float holdSeconds = 0f)
        {
            _hide?.Pause();
            _in?.Pause();
            _text.text = line ?? "";
            if (string.IsNullOrEmpty(line)) { Hide(); return; }
            // Restart the pop from the small state; the class is added on the next tick so the transition runs.
            RemoveFromClassList("kit-bubble--in");
            AddToClassList("kit-bubble--shown");
            if (DeviceFeel.ReducedMotion) AddToClassList("kit-bubble--in");
            else _in = schedule.Execute(() => AddToClassList("kit-bubble--in")).StartingIn(16);
            if (holdSeconds > 0f) _hide = schedule.Execute(Hide).StartingIn((long)(holdSeconds * 1000f));
        }

        public void Hide()
        {
            _in?.Pause();
            RemoveFromClassList("kit-bubble--in");
            RemoveFromClassList("kit-bubble--shown");
        }
    }
}
