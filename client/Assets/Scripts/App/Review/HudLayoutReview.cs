#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.App.Review
{
    /// <summary>Checks actual resolved UI geometry after a rendered frame, not stylesheet constants.</summary>
    public static class HudLayoutReview
    {
        [Serializable] private sealed class Control
        {
            public string classes;
            public Rect bounds;
        }

        [Serializable] private sealed class Receipt
        {
            public string evidence = "Native resolved HUD layout; not a touch or visual-quality acceptance test.";
            public int screenWidth, screenHeight;
            public Rect safeBounds;
            public bool pass;
            public List<Control> controls = new List<Control>();
            public List<string> failures = new List<string>();
        }

        public static void Capture(VisualElement hud, string directory)
        {
            var receipt = new Receipt { screenWidth = Screen.width, screenHeight = Screen.height,
                safeBounds = hud.worldBound };
            var buttons = hud.Query<Button>(className: "ct-dock-action").ToList();
            var needs = hud.Q<Button>(className: "ct-needs-toggle");
            if (needs != null) buttons.Add(needs);
            if (buttons.Count != 7) receipt.failures.Add("Expected six navigation/social actions and the needs toggle.");
            foreach (var button in buttons)
            {
                var b = button.worldBound;
                receipt.controls.Add(new Control { classes = string.Join(" ", button.GetClasses()), bounds = b });
                if (b.width < 123f || b.height < 123f)
                    receipt.failures.Add("Touch target below 124 reference units: " + string.Join(" ", button.GetClasses()));
                if (!Contains(receipt.safeBounds, b)) receipt.failures.Add("Control outside safe HUD bounds.");
            }
            for (int a = 0; a < buttons.Count; a++)
                for (int b = a + 1; b < buttons.Count; b++)
                    if (buttons[a].worldBound.Overlaps(buttons[b].worldBound))
                        receipt.failures.Add("HUD controls overlap: " + a + "/" + b);
            var movement = hud.Q<VisualElement>(className: "ct-move-zone");
            if (movement != null && movement.resolvedStyle.display != DisplayStyle.None)
                foreach (var button in buttons)
                    if (movement.worldBound.Overlaps(button.worldBound)) receipt.failures.Add("Action overlaps the analog movement surface.");
            receipt.pass = receipt.failures.Count == 0;
            File.WriteAllText(Path.Combine(directory, "city-layout.json"), JsonUtility.ToJson(receipt, true));
            if (!receipt.pass) throw new InvalidOperationException("HUD layout review failed: " + string.Join("; ", receipt.failures));
            Debug.Log("CTLIFE [review-layout] Seven HUD targets inside safe bounds, no overlap, all >=124 reference units.");
        }

        private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 1f &&
            inner.yMin >= outer.yMin - 1f && inner.xMax <= outer.xMax + 1f && inner.yMax <= outer.yMax + 1f;

        public static void CaptureIncident(VisualElement hudLayer, string directory)
        {
            var history = hudLayer.Q<Button>(className: "ct-incident-history");
            var receipt = new Receipt { screenWidth = Screen.width, screenHeight = Screen.height, safeBounds = hudLayer.worldBound };
            if (history == null || history.resolvedStyle.display == DisplayStyle.None)
                receipt.failures.Add("Incident receipt control missing after the explicit fixture.");
            else
            {
                var b = history.worldBound;
                receipt.controls.Add(new Control { classes = "ct-incident-history", bounds = b });
                if (b.width < 279f || b.height < 123f || !Contains(receipt.safeBounds, b))
                    receipt.failures.Add("Incident receipt control is undersized or outside safe bounds.");
            }
            receipt.pass = receipt.failures.Count == 0;
            File.WriteAllText(Path.Combine(directory, "incident-layout.json"), JsonUtility.ToJson(receipt, true));
            if (!receipt.pass) throw new InvalidOperationException(string.Join("; ", receipt.failures));
            Debug.Log("CTLIFE [review-layout] Incident receipt target fits safe bounds at >=280x124 reference units.");
        }
    }
}
#endif
