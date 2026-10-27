#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife.RektBurger
{
    /// <summary>
    /// Dev builds only, launch extra <c>rektDs=1</c> together with <c>islandRoom=rekt_shift</c> (which walks to the counter and
    /// clocks in for real): captures the design-system job screens against mockups 01a/01b on a real shift. Marks "shot
    /// prompt" (the counter prompt), "shot onshift" (the HUD pill), "shot card" (tap on the pill), then after 70 s on shift
    /// presses the card's real Clock out button, "shot summary" (the dialog), presses its real Collect, "done".
    /// Logs "CTLIFE [rekt-ds] ...".
    /// </summary>
    public static class RektDsRoute
    {
        private static void Mark(string m) => Debug.Log("CTLIFE [rekt-ds] " + m);

        public static async Task Run(CancellationToken ct)
        {
            try
            {
                var ui = UiRoot.Instance;
                Mark("start");
                // a shift left running earlier comes back as a "while you were away" summary first: capture and collect it
                if (await Until(() => ShiftBand.IsOpen, 25f, ct))
                {
                    await Awaitable.WaitForSecondsAsync(1.4f, ct);
                    Mark("shot away summary");
                    await Awaitable.WaitForSecondsAsync(3f, ct);
                    Press(ui.Modals.Top?.Q<Button>("rekt-shift-collect"));
                    await Awaitable.WaitForSecondsAsync(2f, ct);
                }
                // the counter's prompt (its where-chip names the restaurant), not a bench on the walk there
                string brand = CTLife.Core.NameTable.Get("venue.rekt_burger", "McDonald's").ToUpperInvariant();
                bool AtCounter()
                {
                    var prompt = ui.HudLayer.Q("scene-interaction");
                    var tag = prompt?.Q<Label>(className: "ct-interact-tag");
                    return Visible(prompt) && tag != null && tag.text.Contains(brand) && prompt.Q<Button>(className: "ct-interact-primary") != null;
                }
                if (!await Until(AtCounter, 420f, ct)) { Mark("FAIL no counter prompt"); return; }
                await Awaitable.WaitForSecondsAsync(0.4f, ct);
                Mark("shot prompt");
                // v2: the counter's press opens the Clock in card (mockup a); its Clock in starts the shift
                if (await Until(() => ClockInCard.IsOpen, 20f, ct))
                {
                    await Awaitable.WaitForSecondsAsync(1.4f, ct);
                    Mark("shot clockin");
                    await Awaitable.WaitForSecondsAsync(4f, ct);
                    Press(ui.Modals.Top?.Q<Button>("rekt-clockin"));
                }
                if (!await Until(() => RektBurgerService.Current?.Shift != null && RektBurgerService.Current.Shift.Active, 60f, ct)) { Mark("FAIL no shift"); return; }
                float shiftStart = Time.realtimeSinceStartup;
                await Awaitable.WaitForSecondsAsync(4f, ct);
                Mark("shot onshift");
                await Awaitable.WaitForSecondsAsync(4f, ct);
                Press(ui.HudLayer.Q<Button>("rekt-shift-chip"));
                await Awaitable.WaitForSecondsAsync(1.2f, ct);
                Mark("shot card " + (ShiftCard.IsOpen ? "open" : "NOT OPEN"));
                await Awaitable.WaitForSecondsAsync(4f, ct);
                Press(ui.Modals.Top?.Q<Button>("rekt-shift-keep"));
                await Until(() => Time.realtimeSinceStartup - shiftStart > 70f, 90f, ct);
                Press(ui.HudLayer.Q<Button>("rekt-shift-chip"));
                await Awaitable.WaitForSecondsAsync(1f, ct);
                Press(ui.Modals.Top?.Q<Button>("rekt-shift-clockout"));
                Mark("pressed clock out");
                if (!await Until(() => ShiftBand.IsOpen, 20f, ct)) { Mark("FAIL no summary"); return; }
                await Awaitable.WaitForSecondsAsync(1.4f, ct);
                Mark("shot summary");
                await Awaitable.WaitForSecondsAsync(5f, ct);
                Press(ui.Modals.Top?.Q<Button>("rekt-shift-collect"));
                Mark("pressed collect");
                await Awaitable.WaitForSecondsAsync(3f, ct);
                // the shared toast channel through the real ToastLayer path (a labelled dev line, not game content)
                CTLife.Core.Bus.Publish(CTLife.Core.ToastSignal.Text("Dev check · toast channel", CTLife.Core.ToastKind.Good, "Second line for long supporting copy"));
                await Awaitable.WaitForSecondsAsync(0.6f, ct);
                Mark("shot toast");
                await Awaitable.WaitForSecondsAsync(3.6f, ct);
                ui.Toasts.Show(new CTLife.UI.DS.DsToastSpec { Text = "Dev check · " + CTLife.Core.NameTable.Get("venue.rekt_burger", "McDonald's") + " toast", Brand = CTLife.UI.DS.BrandTable.McDonalds, Trailing = "v2", Seconds = 8f });
                await Awaitable.WaitForSecondsAsync(0.6f, ct);
                Mark("shot brandtoast");
                await Awaitable.WaitForSecondsAsync(3f, ct);
                Mark("done");
            }
            catch (OperationCanceledException) { Mark("cancelled"); }
            catch (Exception e) { Mark("FAIL " + e.GetType().Name + ": " + e.Message); Debug.LogException(e); }
        }

        private static void Press(Button b)
        {
            if (b == null) { Mark("no button to press"); return; }
            using (var e = NavigationSubmitEvent.GetPooled()) { e.target = b; b.SendEvent(e); }
        }

        private static bool Visible(VisualElement e)
        {
            if (e == null || e.panel == null) return false;
            for (var x = e; x != null; x = x.parent)
                if (x.resolvedStyle.display == DisplayStyle.None || x.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }

        private static async Task<bool> Until(Func<bool> ok, float seconds, CancellationToken ct)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                if (ok()) return true;
                await Awaitable.WaitForSecondsAsync(0.25f, ct);
            }
            return ok();
        }
    }
}
#endif
