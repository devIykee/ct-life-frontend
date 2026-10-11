#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Call after two layout frames on PerpsApp.BuildReview only. Does not dispatch network or clicks.</summary>
    public static class PerpsReviewProbe
    {
        public static string Inspect(VisualElement root, string scenario)
        {
            if (root == null || root.panel == null || root.worldBound.width <= 0 || root.worldBound.height <= 0)
                throw new InvalidOperationException("Perps review must be mounted with resolved native geometry");
            if (root.Q<Label>(className: "ct-perp-review-watermark") == null)
                throw new InvalidOperationException("Simulated responses require the review watermark");
            foreach (var button in root.Query<Button>().ToList())
            {
                if (button.resolvedStyle.display == DisplayStyle.None) continue;
                if (float.IsNaN(button.worldBound.width) || button.worldBound.width < 124 || button.worldBound.height < 124)
                    throw new InvalidOperationException($"Undersized native touch target: scenario={scenario}; name={button.name}; text={button.text}; layout={button.layout}; world={button.worldBound}; min={button.resolvedStyle.minWidth}×{button.resolvedStyle.minHeight}; parent={button.parent?.worldBound}; allButtons=" + string.Join(" | ", root.Query<Button>().ToList().Select(b => $"{b.name}:{b.worldBound}")));
                if (!string.IsNullOrEmpty(button.text))
                {
                    var measured = button.MeasureTextSize(button.text, button.contentRect.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                    if (measured.y > button.contentRect.height + 2)
                        throw new InvalidOperationException($"Button label exceeds native text area: scenario={scenario}; name={button.name}; text={button.text}; content={button.contentRect}; measured={measured}; world={button.worldBound}");
                }
            }
            foreach (var label in root.Query<Label>().ToList())
            {
                if (!Visible(label) || string.IsNullOrEmpty(label.text)) continue;
                var size = label.MeasureTextSize(label.text, label.contentRect.width, VisualElement.MeasureMode.AtMost, 0, VisualElement.MeasureMode.Undefined);
                if (size.x > label.contentRect.width + 2 || size.y > label.contentRect.height + 2)
                    throw new InvalidOperationException($"Native label content is clipped: scenario={scenario}; text={label.text}; content={label.contentRect}; measured={size}; world={label.worldBound}; parent={label.parent?.worldBound}; classes=" + string.Join(" ", label.GetClasses()));
            }
            if (scenario != "unavailable" && (root.Q("perps-chart") == null || root.Q("perps-chart").worldBound.height < 200))
                throw new InvalidOperationException("Price history chart has no native layout");
            if (scenario == "ambiguous-close" && root.Q("perps-pending") == null)
                throw new InvalidOperationException("Unconfirmed close lost its retained-request explanation");
            if (scenario == "receipt" && root.Q("perps-receipt") == null)
                throw new InvalidOperationException("Confirmed fixture receipt not visible in hierarchy");
            foreach (var name in new[] { "perps-confirm", "perps-closeConfirm" })
                if (root.Q<Button>(name)?.enabledInHierarchy == true) throw new InvalidOperationException("A review fixture enabled a write control");
            return $"PASS Perps review geometry {scenario}: {root.Query<Button>().ToList().Count} buttons; labelled simulated state; no order dispatch.";
        }
        private static bool Visible(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                if (current.resolvedStyle.display == DisplayStyle.None || current.resolvedStyle.visibility == Visibility.Hidden) return false;
            return element.panel != null;
        }
    }
}
#endif
