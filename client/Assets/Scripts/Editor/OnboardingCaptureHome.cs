using System;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using CTLife.UI;
using Newtonsoft.Json.Linq;

namespace CTLife.EditorTools
{
    /// <summary>
    /// The home / lobby states for <see cref="OnboardingCapture"/> (`-ctlife-captureSet home`, output ct-life-evidence/home-ui/):
    /// first arrival, returning (Market Mood up, daily clue), with debt (+ its explainer), no debt, low energy (+ explainer),
    /// network error (offline + a failed ENTER THE CITY), a character tap, and the new kit pieces. The profile is a
    /// PlayerProfile with a /me built like backend main's; nothing calls the network.
    /// </summary>
    internal static class OnboardingCaptureHome
    {
        public static async Task Run(Func<string, float, Task> shot, Func<float, Task> wait, Action<bool, string> expect, Action<string> step)
        {
            var config = Services.Get<ConfigStore>();
            var profile = Services.TryGet<PlayerProfile>(out var existing) ? existing : Services.Register(new PlayerProfile(null));

            // 1. First arrival (straight out of onboarding): Insider, no debt, the gm wave.
            SetMe(profile, Me("gm_anon", "player_f_kol", "builder", "build_a_project", "studio", 300000, 0, 50, 50));
            var home = new DashboardScreen(() => { }, _ => { }, firstArrival: true);
            home.Show();
            await shot("01-home-first-arrival", 1.6f);
            expect(home.State.GreetingKey == "home.greet.first", "first arrival greets as first");
            expect(home.State.Stats.All(s => s.id != "debt"), "no debt chip without debt");
            home.Close();

            // 2. Returning, market up, today's clue, no debt.
            SetMe(profile, Me("degen_dad", "player_m_tradfi", "perp_degen", "make_10k", "studio", 412000, 0, 72, 64));
            home = new DashboardScreen(() => { }, _ => { }) { DailyClue = async ct => { await Task.Yield(); return "Which chain's tribalists argue in the Ratio Arena?"; } };
            home.Show();
            Bus.Publish(new MoodSignal { bps = 420 });
            await shot("02-home-returning-market-up", 1.6f);
            expect(home.State.SubKey == "home.sub.up", "returning + mood up uses the up line");
            home.TapCharacter();
            await shot("03-home-character-tap", 0.9f);
            home.Close();

            // 3. With debt (Exit Liquidity), market down; then the debt explainer.
            SetMe(profile, Me("cooked_chad", "player_f_lagos_ankara", "trencher", "rug_survivor", "parents_basement", 30000, 72000, 55, 48));
            home = new DashboardScreen(() => { }, _ => { });
            home.Show();
            Bus.Publish(new MoodSignal { bps = -520 });
            await shot("04-home-with-debt", 1.6f);
            expect(home.State.Stats.Any(s => s.id == "debt" && s.value == "$720"), "debt chip shows $720");
            home.TapChip("debt");
            await shot("05-home-debt-explainer", 0.6f);
            home.Close();

            // 4. No debt, flat market, the cash explainer.
            SetMe(profile, Me("anon_shark", "player_m_shark", "prediction_shark", "top_rich_list", "studio", 150000, 0, 64, 70));
            home = new DashboardScreen(() => { }, _ => { });
            home.Show();
            Bus.Publish(new MoodSignal { bps = 80 });
            await shot("06-home-no-debt", 1.6f);
            home.TapChip("cash");
            await shot("07-home-cash-explainer", 0.6f);
            home.Close();

            // 5. Low energy (critical) and low-ish sanity: chips warn, the character is cooked; then the energy explainer.
            SetMe(profile, Me("tired_builder", "player_m_builder", "builder", "build_a_project", "parents_basement", 9000, 64000, 7, 22));
            home = new DashboardScreen(() => { }, _ => { });
            home.Show();
            Bus.Publish(new MoodSignal { bps = 30 });
            await shot("08-home-low-energy", 1.6f);
            expect(home.State.LowEnergy && home.State.ReactionKey == "home.say.tired", "low energy reaction");
            home.TapChip("energy");
            await shot("09-home-energy-explainer", 0.6f);
            home.Close();

            // 6. Network error: offline, and ENTER THE CITY fails (FlowController shows the inline error + a toast).
            SetMe(profile, Me("gm_anon", "player_f_coat", "anon", "host_a_space", "studio", 300000, 0, 60, 60));
            DashboardScreen failing = null;
            failing = new DashboardScreen(() =>
            {
                failing.SetBusy(true);
                failing.Element.schedule.Execute(() => { failing.SetBusy(false); failing.ShowError(Strings.T("home.error.travel")); }).StartingIn(400);
            }, _ => { }) { OfflineOverride = true };
            failing.Show();
            await wait(0.6f);
            failing.PressEnter();
            await shot("10-home-entering-busy", 0.15f);
            await shot("11-home-network-error", 1.2f);
            failing.Close();

            // 7. No profile yet (/me failed): placeholders, offline chip, ENTER disabled.
            SetMe(profile, null);
            home = new DashboardScreen(() => { }, _ => { });
            home.Show();
            await shot("12-home-no-profile", 1.0f);
            home.Close();
            step("home set done");
        }

        private static void SetMe(PlayerProfile profile, Me me)
        {
            typeof(PlayerProfile).GetProperty("Me").SetValue(profile, me);
            Bus.Publish(new ProfileChangedSignal());
        }

        private static Me Me(string alias, string castLook, string tribe, string dream, string home, long cash, long debt, int energy, int sanity)
        {
            var entry = CreatorLookCatalog.Available().FirstOrDefault(e => e.look != null && e.look[1] == "head_" + castLook)
                        ?? CreatorLookCatalog.Available().FirstOrDefault();
            var l = entry?.look ?? new[] { "skin_tan", "head_x", "h", "t", "b", "s", "acc_none" };
            var body = new JObject
            {
                ["user"] = new JObject { ["id"] = "01a11860-9c1f-77a4-a496-17038bb21f01", ["wallet"] = "HAoo9696iSShm1io2SsnU6R7XhAn8y1YH7sEHgFajESf",
                    ["isSeeker"] = false, ["skrName"] = null },
                ["character"] = new JObject
                {
                    ["alias"] = alias, ["tribeId"] = tribe, ["traitIds"] = new JArray("fast_talker", "iron_will"), ["dreamId"] = dream,
                    ["look"] = new JObject { ["skinTone"] = l[0], ["headId"] = l[1], ["hairId"] = l[2], ["topId"] = l[3],
                        ["bottomId"] = l[4], ["shoesId"] = l[5], ["accId"] = l[6] },
                },
                ["needs"] = new JObject { ["energy"] = energy, ["hunger"] = 50, ["fun"] = 50, ["hygiene"] = 50, ["sanity"] = sanity },
                ["skills"] = new JObject { ["clout"] = 1, ["coding"] = 1, ["trading"] = 1, ["writing"] = 1, ["design"] = 1, ["security"] = 1 },
                ["cashCents"] = cash, ["debtCents"] = debt, ["homeId"] = home, ["minerTier"] = "none", ["flags"] = new JObject(),
            };
            return Json.Deserialize<Me>(MeCompat.Normalize(body).ToString());
        }
    }
}
