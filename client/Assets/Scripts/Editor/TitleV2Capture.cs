using System;
using System.Threading.Tasks;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Title v2 (`-ctlife-captureSet titlev2`): the shipping SignInScreen at loading, error and ready, and the sign-in sheet.
    /// No network or wallet: the states are set through the screen's flow API exactly as FlowController drives them.
    /// </summary>
    internal static class TitleV2Capture
    {
        public static async Task Run(Func<string, float, Task> shot, Func<float, Task> wait, Action<bool, string> expect, Action<string> step, JObject receipt)
        {
            var report = new JObject();
            receipt["titleV2"] = report;
            int wallet = 0, play = 0;
            var title = new SignInScreen(() => wallet++, true, false, () => play++);
            try
            {
                step("title v2 loading");
                title.Show();
                var steps = new TitleBoot();
                steps.Begin(TitleBoot.Step.Config);
                title.SetLoading(steps.Progress, Strings.T(steps.StatusKey));
                steps.Complete(TitleBoot.Step.Config);
                steps.Begin(TitleBoot.Step.Session);
                title.SetLoading(steps.Progress, Strings.T(steps.StatusKey));
                await shot("title-v2-loading", 1.2f);
                expect(title.Element.Q<Label>("title-status")?.text == Strings.T("title.status.signingIn"), "loading: the real step on the status line");

                step("title v2 error");
                title.ShowBootError(Strings.T("common.offline"), () => { });
                await shot("title-v2-error", 0.6f);
                expect(title.Element.Q<Button>("title-retry")?.style.display.value != DisplayStyle.None, "error: retry offered");

                step("title v2 ready");
                title.SetReady(returning: false);
                await shot("title-v2-ready", 1.4f);
                expect(title.Element.Q<Label>("title-status")?.text == Strings.T("title.tap").ToUpperInvariant(), "ready: tap to play");

                step("title v2 sign-in sheet");
                title.OpenSignIn();
                await shot("title-v2-signin-sheet", 0.8f);
                expect(title.Element.Q<Button>("title-signin") != null, "sheet: Sign in with Solana");
                title.ShowError("You cancelled in the wallet.");
                await shot("title-v2-signin-sheet-error", 0.5f);
                report["walletTaps"] = wallet;
                report["plays"] = play;
            }
            finally { title.Close(); }
        }
    }
}
