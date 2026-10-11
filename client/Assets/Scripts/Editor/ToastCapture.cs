using System;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.EditorTools
{
    /// <summary>Editor assembly only: real ToastLayer, Bus and dismiss controls with labelled review fixtures.</summary>
    internal static class ToastCapture
    {
        public static async Task Run(Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, Action<string> step, JObject receipt)
        {
            var ui = UiRoot.Instance;
            var background = new VisualElement { pickingMode = PickingMode.Ignore };
            background.style.position = Position.Absolute;
            background.style.left = background.style.right = background.style.top = background.style.bottom = 0;
            background.style.backgroundColor = new Color(.95f, .96f, .97f);
            var watermark = El.Text("REVIEW · LOCAL TOAST FIXTURES");
            watermark.style.position = Position.Absolute;
            watermark.style.bottom = 40;
            watermark.style.left = 40;
            watermark.style.fontSize = 22;
            watermark.style.color = new Color(.3f, .32f, .36f);
            background.Add(watermark);
            ui.ScreenLayer.Add(background);
            var report = new JObject
            {
                ["scope"] = "Actual ToastLayer, Bus, ToastNotice and dismiss controls with local REVIEW fixtures. No network, wallet, payment or account writes.",
                ["kinds"] = new JArray(),
            };
            receipt["toasts"] = report;
            var toastLayer = new ToastLayer(ui.ToastLayer);
            try
            {
                var examples = new[]
                {
                    (ToastKind.Good, "Changes saved", "REVIEW fixture · Your character style is updated."),
                    (ToastKind.Warning, "Connection interrupted", "REVIEW fixture · Check your connection and try again."),
                    (ToastKind.Info, "Invite received", "REVIEW fixture · Open Messages to read it."),
                    (ToastKind.Bad, "Couldn't save changes", "REVIEW fixture · Please try again."),
                    (ToastKind.Event, "A new opportunity", "REVIEW fixture · See the details in Jobs."),
                };
                foreach (var (kind, title, detail) in examples)
                {
                    step("Toast " + kind);
                    Bus.Publish(ToastSignal.Text(title, kind, detail));
                    await shot("toast-" + kind.ToString().ToLowerInvariant(), .3f);
                    var notice = ui.ToastLayer.Q<ToastNotice>();
                    expect(notice != null, kind + " is rendered through the shipping ToastLayer");
                    if (notice == null) continue;
                    Inspect(notice, expect);
                    ((JArray)report["kinds"]).Add(new JObject
                    {
                        ["kind"] = kind.ToString(), ["width"] = notice.worldBound.width,
                        ["height"] = notice.worldBound.height,
                    });
                    Submit(notice.Q<Button>("toast-dismiss"));
                    expect(ui.ToastLayer.Query<ToastNotice>().ToList().Count == 0, kind + " dismiss removes its actual queued item");
                }

                Bus.Publish(ToastSignal.Text("REVIEW fixture · First notice"));
                Bus.Publish(ToastSignal.Text("REVIEW fixture · Second notice", ToastKind.Good));
                Bus.Publish(ToastSignal.Text("REVIEW fixture · Third notice", ToastKind.Warning));
                Bus.Publish(ToastSignal.Text("REVIEW fixture · Fourth notice", ToastKind.Bad));
                await shot("toast-three-stack", .3f);
                var visible = ui.ToastLayer.Query<ToastNotice>().ToList();
                expect(visible.Count == 3, "the real queue caps visible notices at three");
                foreach (var notice in visible) Inspect(notice, expect);
                if (visible.Count > 1) Submit(visible[1].Q<Button>("toast-dismiss"));
                await shot("toast-dismiss-advances-queue", .2f);
                var titles = ui.ToastLayer.Query<Label>(className: "ct-toast-text").ToList().Select(l => l.text).ToArray();
                expect(titles.Length == 3 && titles[0].Contains("First") && titles[1].Contains("Third") && titles[2].Contains("Fourth"),
                    "dismissing the middle notice preserves order and immediately reveals the waiting fourth notice");
                report["stackAfterDismiss"] = new JArray(titles);
                await wait(3.7f);
                expect(ui.ToastLayer.Query<ToastNotice>().ToList().Count == 0, "unscaled expiry clears the remaining stack");
                Bus.Publish(ToastSignal.Text("  "));
                expect(ui.ToastLayer.Query<ToastNotice>().ToList().Count == 0, "empty notices create no card");
                Bus.Publish(ToastSignal.Text("REVIEW fixture · Disposed notice"));
                toastLayer.Dispose();
                Bus.Publish(ToastSignal.Text("REVIEW fixture · Must not appear after disposal"));
                expect(ui.ToastLayer.Query<ToastNotice>().ToList().Count == 0, "disposal clears visible state and removes the bus subscription");
            }
            finally
            {
                toastLayer.Dispose();
                background.RemoveFromHierarchy();
            }
        }

        private static void Inspect(ToastNotice notice, Action<bool, string> expect)
        {
            var copy = notice.Q(className: "ct-toast-copy");
            var close = notice.Q<Button>("toast-dismiss");
            expect(copy.worldBound.xMax <= close.worldBound.xMin + 1, "toast copy leaves the close control readable");
            expect(close.worldBound.xMax <= notice.worldBound.xMax + 1, "toast close control stays inside the card");
            expect(close.worldBound.height >= 55, "toast close target retains its touch size");
        }

        private static void Submit(Button button)
        {
            using var e = NavigationSubmitEvent.GetPooled();
            e.target = button; button.SendEvent(e);
        }
    }
}
