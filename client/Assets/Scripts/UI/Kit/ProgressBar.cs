using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI.Kit
{
    /// <summary>
    /// The onboarding progress bar: a rounded plum track with one notch per step, a green fill that animates (0.4 s,
    /// kit.uss) and a light shine strip on the fill. No numbers (spec B3). Named in the Kit namespace to stay clear of
    /// UnityEngine.UIElements.ProgressBar.
    /// </summary>
    public sealed class ProgressBar : VisualElement
    {
        private readonly VisualElement _fill;
        private readonly int _segments;
        private int _done = -1;

        public ProgressBar(int segments)
        {
            _segments = Mathf.Max(1, segments);
            pickingMode = PickingMode.Ignore;
            AddToClassList("kit-progress");
            _fill = new VisualElement { pickingMode = PickingMode.Ignore };
            _fill.AddToClassList("kit-progress__fill");
            var shine = new VisualElement { pickingMode = PickingMode.Ignore };
            shine.AddToClassList("kit-progress__shine");
            _fill.Add(shine);
            Add(_fill);
            for (int i = 1; i < _segments; i++)
            {
                var notch = new VisualElement { pickingMode = PickingMode.Ignore };
                notch.AddToClassList("kit-progress__notch");
                notch.style.left = Length.Percent(100f * i / _segments);
                Add(notch);
            }
        }

        public int Done => _done;

        /// <summary>Fills `done` of the segments. `animate` false jumps (first show, restore after resume).</summary>
        public void Set(int done, bool animate = true)
        {
            done = Mathf.Clamp(done, 0, _segments);
            if (done == _done) return;
            _done = done;
            EnableInClassList("kit-progress--instant", !animate || DeviceFeel.ReducedMotion);
            _fill.style.width = Length.Percent(100f * done / _segments);
            _fill.style.display = done == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>The 100% flash at the final celebration.</summary>
        public void Flash()
        {
            AddToClassList("kit-progress--flash");
            schedule.Execute(() => RemoveFromClassList("kit-progress--flash")).StartingIn(260);
        }
    }
}
