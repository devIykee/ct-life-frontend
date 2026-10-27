#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI.DS
{
    /// <summary>
    /// Dev builds only, launch extra <c>dsKitchen=1</c>: every design-system component over the live world, then the
    /// standard dialog and a confirm through the modal host, then the device probes for SYSTEM.md's [Unverified] items:
    /// Inter digit widths (tabular or not) and the cost of text-shadow (a 60-label stress panel on/off, 10 s each, with
    /// FpsMeter segments ds_shadow_off / ds_shadow_on). Logs "CTLIFE [ds-kitchen] ..." with "shot ..." marks.
    /// </summary>
    public static class DsKitchenSink
    {
        private static void Mark(string m) => Debug.Log("CTLIFE [ds-kitchen] " + m);

        public static async Task Run(CancellationToken ct)
        {
            try
            {
                var ui = UiRoot.Instance;
                float deadline = Time.realtimeSinceStartup + 240f;
                while (!ct.IsCancellationRequested && (VenueSession.Current == null || !VenueSession.Current.IsActive) && Time.realtimeSinceStartup < deadline)
                    await Awaitable.WaitForSecondsAsync(0.5f, ct);
                await Awaitable.WaitForSecondsAsync(4f, ct);
                if (ui.HudLayer != null) ui.HudLayer.style.display = DisplayStyle.None;

                // 1. HUD pieces, prompt, toast, chips, seg (over the world)
                var probe = Resources.Load<StyleSheet>("UI/DS/ds-probe");
                if (probe != null && !ui.Root.styleSheets.Contains(probe)) ui.Root.styleSheets.Add(probe);
                var board = new VisualElement { name = "ds-kitchen", pickingMode = PickingMode.Ignore };
                board.style.position = Position.Absolute; board.style.left = 0; board.style.top = 0; board.style.right = 0; board.style.bottom = 0;
                ui.ModalLayer.Add(board);
                var pill = HudPill("UI/Casino/coin_heads", "$309.95", null);
                Place(pill, left: 1000, top: 32);
                board.Add(pill);
                var plate = HudPill(null, "dev44464", "Studio · Lv 3");
                Place(plate, left: 40, top: 32);
                board.Add(plate);
                var phone = Ds.HudRound("device-mobile", null);
                Ds.SetBadge(phone, 2);
                Place(phone, right: 330, top: 32);
                board.Add(phone);
                var wanted = new VisualElement();
                wanted.AddToClassList("ds-hud-pill");
                wanted.AddToClassList("ds-hud-pill--danger");
                wanted.Add(Ds.Stars(2));
                Place(wanted, left: 1340, top: 32);
                board.Add(wanted);
                var prompt = Prompt();
                Place(prompt, left: 1000, top: 360);
                board.Add(prompt);
                var seg = Ds.Seg(new[] { "Slots", "Crash", "Coinflip" }, 1, null);
                Place(seg, left: 40, top: 200);
                board.Add(seg);
                var chips = Ds.Chips(Ds.Chip("Free", Tone.Accent), Ds.Chip("+1 wanted star", Tone.Danger, "star"), Ds.Chip("Tier 3"), Ds.Chip("$5", Tone.Ink), Ds.Chip("Times Square", Tone.Glass, "map-trifold"));
                Place(chips, left: 40, top: 320);
                board.Add(chips);
                var jump = Ds.HudRound("arrow-fat-up", null, big: true);
                Place(jump, right: 40, bottom: 40);
                board.Add(jump);
                ui.Toasts.Show(new DsToastSpec { Icon = "briefcase", Text = "First shift · Clock in at the counter", Trailing = "0/3", Tone = Tone.Accent, Sticky = true, Key = "kitchen" });
                await Awaitable.WaitForSecondsAsync(1.5f, ct);
                // digits: Inter's default figures, tabular or proportional? (fixed min-width is the fallback either way)
                var ones = Ds.Text("1111111111", "ds-t-stat"); var eights = Ds.Text("8888888888", "ds-t-stat");
                Place(ones, left: 40, top: 440); Place(eights, left: 40, top: 500);
                board.Add(ones); board.Add(eights);
                await Awaitable.WaitForSecondsAsync(0.5f, ct);
                Mark($"digits width 1x10 {ones.resolvedStyle.width:0.0} vs 8x10 {eights.resolvedStyle.width:0.0} -> {(Mathf.Abs(ones.resolvedStyle.width - eights.resolvedStyle.width) < 1f ? "TABULAR" : "PROPORTIONAL")}");
                Mark("shot hud");
                await Awaitable.WaitForSecondsAsync(4f, ct);
                ui.Toasts.Clear("kitchen");

                // 2. standard dialog (01b) and confirm (06) through the one modal host
                var dialog = Ds.Dialog(new DialogSpec
                {
                    Hero = Ds.Hero("briefcase", Tone.Accent),
                    Title = "Shift done",
                    Subtitle = NameTable.Get("venue.rekt_burger", "McDonald's") + " · " + NameTable.Get("venue.timeline_square", "Times Square"),
                    Tiles = { Ds.Stat("Time worked", "1 min", "clock"), Ds.Stat("Earned", "+$0.63", Ds.Coin, Tone.Accent) },
                    Progress = Ds.Progress("chart-line-up", "Today's cap", "resets in 2h 6m", 0.04f, "$0.63", "$35"),
                    Primary = Ds.Action("Collect $0.63", () => ui.Modals.Close()),
                    Secondary = Ds.TextAction("Clock in again", () => ui.Modals.Close()),
                });
                ui.Modals.Show(dialog);
                await Awaitable.WaitForSecondsAsync(1.2f, ct);
                Mark("shot dialog");
                await Awaitable.WaitForSecondsAsync(4f, ct);
                ui.Modals.Close();
                await Awaitable.WaitForSecondsAsync(0.6f, ct);
                var confirm = Ds.Confirm("Steal this car?", "It belongs to another player. The police will notice.",
                    Ds.Action("Steal it", () => ui.Modals.Close(), BtnKind.Danger), Ds.TextAction("Leave it", () => ui.Modals.Close()),
                    Ds.Hero("police-car", Tone.Danger), Ds.Chip("+1 wanted star", Tone.Danger, "star"), Ds.Chip("+8 heat", Tone.Warn));
                ui.Modals.Show(confirm);
                await Awaitable.WaitForSecondsAsync(1.2f, ct);
                Mark("shot confirm");
                await Awaitable.WaitForSecondsAsync(4f, ct);
                ui.Modals.Close();
                await Awaitable.WaitForSecondsAsync(0.6f, ct);

                // 3. text-shadow cost: 60 world captions without, then with, 10 s each (FpsMeter segments)
                var stress = new VisualElement { pickingMode = PickingMode.Ignore };
                stress.style.position = Position.Absolute; stress.style.left = 300; stress.style.top = 600; stress.style.flexDirection = FlexDirection.Row; stress.style.flexWrap = Wrap.Wrap; stress.style.width = 1800;
                for (int i = 0; i < 60; i++) { var l = Ds.Text("Times Square " + i, "ds-t-hud-caption"); l.style.marginRight = 12; stress.Add(l); }
                board.Add(stress);
                PerfSegments.Ensure();
                PerfSegments.Label = "ds_shadow_off";
                Mark("shadow off");
                await Awaitable.WaitForSecondsAsync(10f, ct);
                stress.AddToClassList("ds-probe-shadow");
                foreach (var l in stress.Children()) l.AddToClassList("ds-probe-shadow");
                PerfSegments.Label = "ds_shadow_on";
                Mark("shadow on");
                await Awaitable.WaitForSecondsAsync(10f, ct);
                PerfSegments.Label = null;
                board.RemoveFromHierarchy();
                if (ui.HudLayer != null) ui.HudLayer.style.display = DisplayStyle.Flex;
                Mark("done");
            }
            catch (OperationCanceledException) { Mark("cancelled"); }
            catch (Exception e) { Mark("FAIL " + e.GetType().Name + ": " + e.Message); Debug.LogException(e); }
        }

        private static VisualElement HudPill(string art, string value, string caption)
        {
            var p = new VisualElement();
            p.AddToClassList("ds-hud-pill");
            if (art != null)
            {
                var a = new VisualElement();
                a.AddToClassList("ds-hud-pill-art");
                var t = Ds.IconTexture(art);
                if (t != null) a.style.backgroundImage = Background.FromTexture2D(t);
                p.Add(a);
            }
            var col = new VisualElement();
            col.AddToClassList("ds-hud-pill-col");
            col.Add(Ds.Text(value, "ds-t-hud"));
            if (caption != null) col.Add(Ds.Text(caption, "ds-t-hud-caption"));
            p.Add(col);
            return p;
        }

        private static VisualElement Prompt()
        {
            var p = new VisualElement();
            p.AddToClassList("ds-prompt");
            p.Add(Ds.Pill(NameTable.Get("venue.rekt_burger", "McDonald's").ToUpperInvariant() + " · COUNTER", "ds-prompt-where"));
            var acts = new VisualElement();
            acts.AddToClassList("ds-prompt-acts");
            var main = new VisualElement(); main.AddToClassList("ds-prompt-act");
            main.Add(Ds.Orb("clock", null));
            main.Add(Ds.Pill("Clock in", "ds-prompt-label"));
            main.Add(Ds.Pill("$30/hr · $35.05 left today", "ds-prompt-meta"));
            var alt = new VisualElement(); alt.AddToClassList("ds-prompt-act");
            alt.Add(Ds.Orb("info", null, main: false));
            alt.Add(Ds.Pill("Job info", "ds-prompt-label"));
            acts.Add(main); acts.Add(alt);
            p.Add(acts);
            return p;
        }

        private static void Place(VisualElement e, float? left = null, float? top = null, float? right = null, float? bottom = null)
        {
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = left.Value;
            if (top.HasValue) e.style.top = top.Value;
            if (right.HasValue) e.style.right = right.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
        }
    }
}
#endif
