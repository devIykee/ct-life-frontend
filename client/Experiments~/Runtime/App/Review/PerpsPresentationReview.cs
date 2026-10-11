#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.App.Review
{
    /// <summary>Real phone renderer and UI callbacks with explicitly simulated data. No transport instance is created.</summary>
    public static class PerpsPresentationReview
    {
        private sealed class ReviewApp : PhoneApp
        {
            public string Scenario = "active";
            public VisualElement Current;
            public override string Id => "perps-review";
            public override string Title => PerpsStrings.T("title");
            public override VisualElement Open() => Current = PerpsApp.BuildReview(Scenario);
        }
        [Serializable] private sealed class Receipt
        {
            public bool pass;
            public string currentScenario, failure, limits = "Simulated responses; NavigationSubmitEvent plus real TextField/Slider change callbacks. Native pointer hit testing, keyboard/IME, live transport and economics are not certified by this review.";
            public List<string> checks = new List<string>(), captures = new List<string>();
        }
        public static async Task Run(PhoneShell phone, string directory, CancellationToken owner)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(owner);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            var ct = deadline.Token;
            var receipt = new Receipt();
            Directory.CreateDirectory(directory);
            var app = new ReviewApp(); phone.Register(app);
            try
            {
                foreach (var scenario in new[] { "unavailable", "empty", "active", "error", "ambiguous-close", "receipt" })
                {
                    receipt.currentScenario = scenario;
                    app.Scenario = scenario; phone.Open(app.Id);
                    await Settle((ScrollView)app.Current, ct);
                    receipt.checks.Add(PerpsReviewProbe.Inspect(app.Current, scenario));
                    await Capture(directory, "perps-" + scenario, receipt, ct);
                    if (scenario == "active") await InputReview(app.Current, directory, receipt, ct);
                    if (scenario != "unavailable")
                    {
                        var scroll = (ScrollView)app.Current;
                        var target = scenario == "receipt" ? app.Current.Q("perps-receipt") :
                            scenario == "ambiguous-close" ? app.Current.Q("perps-pending") :
                            app.Current.Q(className: "ct-perp-position") ?? app.Current.Q("perps-openTab");
                        Require(target != null, "Scenario detail target missing");
                        scroll.ScrollTo(target); await Settle(scroll, ct);
                        receipt.checks.Add(PerpsReviewProbe.Inspect(app.Current, scenario));
                        await Capture(directory, "perps-" + scenario + "-detail", receipt, ct);
                    }
                }
                receipt.pass = true;
                Log.Info("perps-review", "PASS six watermarked states, native geometry, real side/margin/leverage/review/back callbacks; write controls disabled. Receipt remains open.");
            }
            catch (Exception error)
            {
                receipt.failure = error is OperationCanceledException ? "Cancelled or exceeded 45 seconds" : error.Message;
                try { await Capture(directory, "perps-failure-" + (receipt.currentScenario ?? "startup"), receipt, CancellationToken.None); }
                catch (Exception captureError) { receipt.checks.Add("Failure capture failed: " + captureError.GetType().Name); }
                throw;
            }
            finally
            {
                File.WriteAllText(Path.Combine(directory, "perps-review-receipt.json"), JsonUtility.ToJson(receipt, true));
            }
        }
        private static async Task InputReview(VisualElement current, string directory, Receipt receipt, CancellationToken ct)
        {
            var scroll = (ScrollView)current;
            var input = current.Q<TextField>("perps-margin");
            Require(input != null, "Margin input missing");
            scroll.ScrollTo(input); await Frames(3, ct);
            input.value = "75.50"; // Real value-change callback, not a private field assignment.
            current.Q<SliderInt>("perps-leverage").value = 4;
            await Frames(2, ct);
            await Activate(scroll, current.Q<Button>("perps-short"), ct);
            Require(current.Q<TextField>("perps-margin").value == "75.50", "Side change lost exact margin draft");
            Require(current.Q<SliderInt>("perps-leverage").value == 4, "Side change lost leverage draft");
            scroll.ScrollTo(current.Q<TextField>("perps-margin")); await Frames(3, ct);
            await Capture(directory, "perps-prepare-margin", receipt, ct);
            scroll.ScrollTo(current.Q<Button>("perps-preview")); await Frames(3, ct);
            receipt.checks.Add(PerpsReviewProbe.Inspect(current, "active"));
            await Capture(directory, "perps-prepare-leverage", receipt, ct);
            await Activate(scroll, current.Q<Button>("perps-preview"), ct);
            var confirm = current.Q<Button>("perps-confirm");
            Require(confirm != null && !confirm.enabledInHierarchy, "Review fixture exposed an economic write");
            var expected = PerpsStrings.F("summary", "REVIEW", PerpsStrings.T("short"), 4);
            Require(current.Query<Label>().ToList().Any(x => x.text == expected), "Real review callback lost selected direction/leverage");
            Require(current.Query<Label>().ToList().Any(x => x.text == PerpsStrings.F("marginSummary", PerpsStrings.Cash(7550))), "Real review callback changed margin");
            scroll.ScrollTo(confirm); await Frames(3, ct);
            receipt.checks.Add(PerpsReviewProbe.Inspect(current, "active"));
            await Capture(directory, "perps-confirmation", receipt, ct);
            await Activate(scroll, current.Q<Button>("perps-back"), ct);
            Require(current.Q<TextField>("perps-margin").value == "75.50", "Edit-order callback lost retained input");
            receipt.checks.Add("PASS actual UI callbacks: exact 75.50 margin, 4x, Short, review confirmation, Edit order retains values; no submit event sent.");
        }
        private static async Task Activate(ScrollView scroll, Button button, CancellationToken ct)
        {
            Require(button != null && button.enabledInHierarchy, "Cannot activate missing or disabled control");
            scroll.ScrollTo(button); await Frames(3, ct);
            var viewport = scroll.contentViewport.worldBound; var bounds = button.worldBound;
            Require(bounds.xMin >= viewport.xMin - 2 && bounds.xMax <= viewport.xMax + 2 && bounds.yMin >= viewport.yMin - 2 && bounds.yMax <= viewport.yMax + 2,
                "Control is clipped outside native scroll viewport: " + button.name);
            using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt);
            await Frames(3, ct);
        }
        // Wait for a real painted frame and two matching native layout samples after a remount/scroll.
        // This never accepts undersized controls: Inspect still applies the original 124-unit threshold.
        private static async Task Settle(ScrollView root, CancellationToken ct)
        {
            var limit = Time.realtimeSinceStartupAsDouble + 2;
            string previous = null; int stable = 0;
            do
            {
                await Awaitable.EndOfFrameAsync(ct); await Awaitable.NextFrameAsync(ct);
                var sample = root.contentContainer.worldBound + "|" + string.Join("|", root.Query<Button>().ToList().Select(b => b.worldBound.ToString()));
                stable = sample == previous ? stable + 1 : 0; previous = sample;
                if (stable >= 2) return;
            } while (Time.realtimeSinceStartupAsDouble < limit);
            throw new InvalidOperationException("Perps review layout did not settle within two seconds");
        }
        private static async Task Frames(int count, CancellationToken ct) { for (var i = 0; i < count; i++) await Awaitable.NextFrameAsync(ct); }
        private static async Task Capture(string directory, string name, Receipt receipt, CancellationToken ct)
        {
            await Awaitable.EndOfFrameAsync(ct);
            var texture = ScreenCapture.CaptureScreenshotAsTexture(); Require(texture != null, "Native capture failed");
            try { File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG()); }
            finally { UnityEngine.Object.Destroy(texture); }
            receipt.captures.Add(name + ".png");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Perps review: " + message); }
    }
}
#endif
