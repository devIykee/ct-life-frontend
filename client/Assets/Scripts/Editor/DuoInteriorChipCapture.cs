using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.EditorTools
{
    /// <summary>Editor-only geometry regression for actual feature chip builders in the shipping phone shell.
    /// No service replacement, account action or network call. Fixture tap callbacks do nothing.</summary>
    internal static class DuoInteriorChipCapture
    {
        private static Button Chip(string assembly, string type, string label, bool selected = false)
        {
            var a = AppDomain.CurrentDomain.GetAssemblies().First(x => x.GetName().Name == assembly);
            var factory = a.GetType(type, true).GetMethod("Chip", BindingFlags.Public | BindingFlags.Static);
            return (Button)factory.Invoke(null, new object[] { label, (Action)(() => { }), selected });
        }
        private static Button Venture(string label, bool selected = false) => Chip("CTLife.Features.Ventures", "CTLife.Ventures.VUi", label, selected);
        private static VisualElement Row(params VisualElement[] children)
        {
            // The shipping VUi/CityUi/CompUi rows explicitly wrap; reproduce that exact parent contract.
            var row = El.Div("ct-row ct-money-row", children);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.flexWrap = Wrap.Wrap;
            return row;
        }

        private sealed class FixtureApp : PhoneApp
        {
            public string Mode = "ads";
            public ScrollView Root;
            public VisualElement ShrinkRow;
            public override string Id => "duo-interior-chip-review";
            public override string Title => "REVIEW · Interior controls";
            public override VisualElement Open()
            {
                Root = El.VerticalScroll("ct-app-page");
                if (Mode == "ads")
                {
                    Root.Add(El.Text("Ad Desk · actual filter builder", "ct-h2"));
                    Root.Add(Row(Venture("All", true), Venture("Billboard"), Venture("Sponsored phone app"), Venture("Pixel Wall tile"), Venture("Conference Week booth")));
                    Root.Add(El.Text("Ad booking · actual term builder", "ct-h2"));
                    Root.Add(Row(Venture("7 days · 20 USDC", true), Venture("30 days · 75 USDC"), Venture("Conference Week · 250 USDC")));
                }
                else if (Mode == "builder")
                {
                    Root.Add(PhonePresentation.Segment(Venture("My projects", true), Venture("Hiring"), Venture("Offers"), Venture("Found one")));
                    Root.Add(El.Text("Builder · actual project types", "ct-h2"));
                    Root.Add(Row(Venture("Memecoin", true), Venture("DEX"), Venture("NFT studio"), Venture("Perps venue"), Venture("L2")));
                    Root.Add(El.Text("Other feature chip builders", "ct-h2"));
                    Root.Add(Row(Chip("CTLife.Features.Money", "CTLife.Economy.MoneyUi", "Unstake ORE"),
                        Chip("CTLife.Features.CityLife", "CTLife.CityLife.CityUi", "Friends only"),
                        Chip("CTLife.Features.Competition", "CTLife.Competition.CompUi", "Previous seasons")));
                }
                else
                {
                    Root.Add(El.Text("Bounded width / wrapped label", "ct-h2"));
                    ShrinkRow = Row(Venture("Conference Week promotional booth · 30 days · 250 USDC", true));
                    Root.Add(ShrinkRow);
                }
                return Root;
            }
        }

        private static void Inspect(VisualElement root, bool allowWrap, Action<bool, string> expect, JArray report)
        {
            foreach (var button in root.Query<Button>(className: "ct-chip").ToList())
            {
                var parent = button.parent;
                var single = button.MeasureTextSize(button.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                var wrapped = button.MeasureTextSize(button.text, button.contentRect.width, VisualElement.MeasureMode.AtMost, 0, VisualElement.MeasureMode.Undefined);
                bool fits = allowWrap ? wrapped.y <= button.contentRect.height + 1f : single.x <= button.contentRect.width + 1f;
                bool contained = button.worldBound.xMin >= parent.worldBound.xMin - 1f && button.worldBound.xMax <= parent.worldBound.xMax + 1f;
                expect(button.panel != null && button.resolvedStyle.display != DisplayStyle.None, "actual interior chip attached: " + button.text);
                expect(fits, "interior chip text fits " + (allowWrap ? "wrapped height: " : "one line: ") + button.text);
                expect(contained, "interior chip stays within its row: " + button.text);
                report.Add(new JObject { ["text"] = button.text, ["contentWidth"] = button.contentRect.width,
                    ["singleLineWidth"] = single.x, ["contentHeight"] = button.contentRect.height, ["wrappedHeight"] = wrapped.y,
                    ["fits"] = fits, ["contained"] = contained });
            }
        }

        public static async Task Run(Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, Action<string> step, JObject receipt)
        {
            var report = new JObject { ["scope"] = "REVIEW COMPONENT FIXTURE: shipping VUi/MoneyUi/CityUi/CompUi chip builders inside shipping PhoneShell; no network or writes.", ["controls"] = new JArray() };
            receipt["duoInteriorChips"] = report;
            using var phone = new PhoneShell(UiRoot.Instance.PhoneLayer);
            var app = new FixtureApp(); phone.Register(app);
            foreach (var mode in new[] { "ads", "builder", "wrap" })
            {
                step("Duo interior chip regression " + mode);
                app.Mode = mode; phone.Open(app.Id); await wait(.4f);
                Inspect(app.Root, mode == "wrap", expect, (JArray)report["controls"]);
                await shot("duo-interior-chips-" + mode, .15f);
                if (mode == "builder")
                {
                    var widths = app.Root.Q(className: "ct-duo-segment").Query<Button>().ToList().Select(x => x.layout.width).ToArray();
                    expect(widths.Max() - widths.Min() < 2f, "four short Builder tabs remain evenly distributed");
                }
                if (mode == "wrap")
                {
                    app.ShrinkRow.style.width = 350;
                    await wait(.3f);
                    Inspect(app.Root, true, expect, (JArray)report["controls"]);
                    await shot("duo-interior-chips-narrow", .15f);
                    expect(app.ShrinkRow.Q<Button>().layout.width <= 350f, "long term chip contracts when its row narrows");
                    app.ShrinkRow.style.width = StyleKeyword.Auto;
                    await wait(.3f);
                    Inspect(app.Root, true, expect, (JArray)report["controls"]);
                }
            }
        }
    }
}
