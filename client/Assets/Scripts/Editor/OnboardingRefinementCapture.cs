using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using CTLife.UI;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Focused, network-free Seeker-sized captures through the real creator and lottery screens.
    /// Invoke OnboardingCapture.Launch with -ctlife-captureSet refinements and an external -ctlife-captureOut.
    /// Optional -ctlife-refinementOnly portraits|cards|origins selects a faster visual iteration lane.
    /// Actual shipped models, animation controller, UI root and styles are used. Fixture preferences are restored.
    /// </summary>
    internal static class OnboardingRefinementCapture
    {
        private const string SeedKey = "ctlife.creator.lookSeed";
        private const string LongAlias = "DiamondHandsDominion"; // exactly the accepted 20-character limit
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        public static async Task Run(Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, Action<string> step, JObject receipt)
        {
            var draftKeys = new HashSet<string> { "ctlife.creator.draft.local" };
            if (Services.TryGet<PlayerProfile>(out var profile) && !string.IsNullOrEmpty(profile.UserId))
                draftKeys.Add("ctlife.creator.draft." + profile.UserId);
            var drafts = draftKeys.ToDictionary(key => key, key => (present: PlayerPrefs.HasKey(key), value: PlayerPrefs.GetString(key)));
            bool hadSeed = PlayerPrefs.HasKey(SeedKey);
            int seed = PlayerPrefs.GetInt(SeedKey);
            var motionField = typeof(DeviceFeel).GetField("_reducedMotionOverride", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingFieldException("Reduced-motion capture override not found");
            var motionOverride = (bool?)motionField.GetValue(null);
            var owned = new List<UiScreen>();
            var records = new JArray();
            receipt["refinementChecks"] = records;
            receipt["fixtureScope"] = "Actual shipped models/screens; local delegate fixtures; no network, payment or asset writes.";
            receipt["stressAlias"] = LongAlias;
            try
            {
                foreach (var key in draftKeys) PlayerPrefs.DeleteKey(key);
                CreatorLookOrder.SetInstallSeed(0);
                DeviceFeel.OverrideReducedMotion(true);
                var config = Services.Get<ConfigStore>();
                string only = Arg("-ctlife-refinementOnly");
                if (only == null || only == "portraits")
                    await Portraits(config, shot, wait, expect, step, records, owned, draftKeys);
                if (only == null || only == "cards")
                    await PlayerCards(config, shot, wait, expect, step, records, owned, draftKeys);
                if (only == null || only == "origins")
                    await Origins(shot, wait, expect, step, records, owned);
                expect(only == null || new[] { "portraits", "cards", "origins" }.Contains(only), "recognized refinement capture lane");
            }
            finally
            {
                try
                {
                    foreach (var screen in owned.AsEnumerable().Reverse()) screen.Close();
                }
                finally
                {
                    foreach (var entry in drafts)
                    {
                        if (entry.Value.present) PlayerPrefs.SetString(entry.Key, entry.Value.value);
                        else PlayerPrefs.DeleteKey(entry.Key);
                    }
                    if (hadSeed) PlayerPrefs.SetInt(SeedKey, seed);
                    else PlayerPrefs.DeleteKey(SeedKey);
                    DeviceFeel.OverrideReducedMotion(motionOverride);
                    PlayerPrefs.Save();
                    bool restored = drafts.All(entry => PlayerPrefs.HasKey(entry.Key) == entry.Value.present &&
                        (!entry.Value.present || PlayerPrefs.GetString(entry.Key) == entry.Value.value)) &&
                        PlayerPrefs.HasKey(SeedKey) == hadSeed && (!hadSeed || PlayerPrefs.GetInt(SeedKey) == seed) &&
                        Equals(motionField.GetValue(null), motionOverride);
                    receipt["fixturePreferencesRestored"] = restored;
                    expect(restored, "refinement capture restores drafts, seed and motion preference");
                }
            }
        }

        private static async Task Portraits(ConfigStore config, Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, Action<string> step, JArray records, List<UiScreen> owned, HashSet<string> draftKeys)
        {
            step("refinements: Rain side portraits and raised hands");
            var screen = NewCreator(config, owned, draftKeys);
            await SelectLook(screen, "head_rain", wait, expect);
            await ToTribe(screen, wait, expect);
            var preview = Field<AvatarPreview>(screen, "_preview");
            var avatar = preview.Camera.transform.parent.GetComponentInChildren<AvatarView>(true);
            if (avatar == null || avatar.Animator == null) throw new InvalidOperationException("Rain preview has no actual animated avatar");
            avatar.enabled = false;
            var animator = avatar.Animator;
            AnimatorUtility.DeoptimizeTransformHierarchy(animator.gameObject);
            animator.speed = 0f;
            foreach (var pose in new[] { (state: "loco_idle", phase: .30f), (state: "idle_fidget_stretch", phase: .52f), (state: "emote_wave", phase: .52f) })
            {
                await Freeze(preview, animator, pose.state, pose.phase, wait);
                string name = "01-rain-tribe-" + pose.state;
                await shot(name, .4f);
                CheckSide(screen, preview, animator, pose.state, expect, records, name);
            }
            screen.PickForAutomation(0);
            screen.PressCta();
            await wait(.35f);
            expect(screen.Step == CreatorStep.Traits, "Rain reaches the actual two-trait screen");
            foreach (var pose in new[] { (state: "idle_fidget_stretch", phase: .52f), (state: "emote_wave", phase: .52f) })
            {
                await Freeze(preview, animator, pose.state, pose.phase, wait);
                string name = "02-rain-traits-" + pose.state;
                await shot(name, .4f);
                CheckSide(screen, preview, animator, pose.state, expect, records, name);
            }
            PickTraits(screen, config.Registries);
            screen.PressCta();
            await wait(.35f);
            expect(screen.Step == CreatorStep.Dream, "Rain reaches the actual dream screen");
            await Freeze(preview, animator, "loco_idle", .3f, wait);
            await shot("03-rain-dream", .5f);
            screen.Close();
        }

        private static async Task PlayerCards(ConfigStore config, Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, Action<string> step, JArray records, List<UiScreen> owned, HashSet<string> draftKeys)
        {
            step("refinements: actual player cards before the origin draw");
            foreach (var fixture in new[]
            {
                (head: "head_snow", alias: "Snow_Builder", stress: false, suffix: ""),
                (head: "head_rain", alias: "RainOnChain", stress: false, suffix: ""),
                (head: "head_ubc_m", alias: "gm_dex", stress: false, suffix: ""),
                (head: "head_pm_shark", alias: "LiquidityShark", stress: false, suffix: ""),
                (head: "head_ubc_m", alias: LongAlias, stress: true, suffix: "-long-labels"),
                (head: "head_ubc_m", alias: "WWWWWWWWWWWWWWWWWWWW", stress: true, suffix: "-wide-alias")
            })
            {
                // Isolated registry instances allow long-label testing without changing the shared config or assets.
                var reg = Registries.Parse(config.Root["ids"]?.DeepClone(), config.Root["config"]?.DeepClone());
                if (fixture.stress)
                {
                    var dream = reg.Dreams.OrderByDescending(item => item.name?.Length ?? 0).First();
                    dream.name = "Build a project the timeline keeps using";
                }
                var screen = NewCreator(config, owned, draftKeys, reg);
                await SelectLook(screen, fixture.head, wait, expect);
                await ToTribe(screen, wait, expect);
                int tribe = reg.TribeOrder.FindIndex(item => item.id == "builder");
                screen.PickForAutomation(tribe >= 0 ? tribe : 0);
                screen.PressCta(); await wait(.2f);
                PickTraits(screen, reg);
                screen.PressCta(); await wait(.2f);
                var longest = reg.Dreams.OrderByDescending(item => item.name?.Length ?? 0).First();
                screen.PickForAutomation(reg.Dreams.IndexOf(longest));
                screen.PressCta(); await wait(.2f);
                screen.TypeAlias(fixture.alias);
                screen.PressCta();
                await wait(.8f);
                expect(screen.Step == CreatorStep.Confirm, fixture.head + " reaches the real player confirmation");
                string name = "10-card-" + fixture.head + fixture.suffix;
                await shot(name, .8f);
                CheckPlayerCard(screen, expect, records, name, fixture.alias, longest.name);
                if (fixture.stress)
                {
                    screen.PickForAutomation(0);
                    await shot(name + "-age-checked", .25f);
                    expect(screen.Element.Q<UI.Kit.LippedButton>("ob-cta").enabledInHierarchy,
                        "valid long-label card permits continuation after age confirmation");
                }
                screen.Close();
            }
        }

        private static async Task Origins(Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, Action<string> step, JArray records, List<UiScreen> owned)
        {
            step("refinements: all three authoritative origin outcomes");
            var outcomes = new[]
            {
                new PostLotteryRoll200Response(PostLotteryRoll200Response.OutcomeEnum.Insider, 300000, 0, "studio"),
                new PostLotteryRoll200Response(PostLotteryRoll200Response.OutcomeEnum.ExitLiquidity, 30000, 72000, "parents_basement"),
                new PostLotteryRoll200Response(PostLotteryRoll200Response.OutcomeEnum.Seeker, 300000, 0, "studio"),
                new PostLotteryRoll200Response(PostLotteryRoll200Response.OutcomeEnum.ExitLiquidity, 30000, 72000, "parents_basement_above_timeline_square")
            };
            for (int i = 0; i < outcomes.Length; i++)
            {
                var outcome = outcomes[i];
                int rolls = 0;
                var screen = new LotteryScreen(outcome.Outcome == PostLotteryRoll200Response.OutcomeEnum.Seeker,
                    () => { rolls++; return Task.FromResult(outcome); }, _ => { }, existingResult: outcome)
                {
                    PlayerAlias = i == 3 ? LongAlias : "gm_anon",
                    StarterPerk = i == 3
                        ? "Builder perk: ship projects with your people, collect your first paycheck, and put that group chat to work."
                        : "Builder perk: your first project starts here."
                };
                owned.Add(screen);
                screen.Show();
                await Until(() => screen.Revealed, wait, 8f);
                string name = "20-origin-" + LotteryModel.OutcomeId(outcome.Outcome) + (i == 3 ? "-long-labels" : "");
                await shot(name, .8f);
                CheckOrigin(screen, outcome, expect, records, name);
                expect(rolls == 0, name + " uses the existing authoritative receipt without a new roll");
                screen.Close();
            }

            // Capture the real draw and movement as well as reduced-motion results; not a manually assembled card.
            DeviceFeel.OverrideReducedMotion(false);
            int animatedRolls = 0;
            var animated = new LotteryScreen(false, () => { animatedRolls++; return Task.FromResult(outcomes[0]); }, _ => { }, autoStart: false);
            owned.Add(animated);
            animated.Show();
            await shot("24-origin-draw", .8f);
            animated.PressCta();
            await Until(() => animated.Revealed && !animated.Rolling, wait, 16f);
            await shot("25-origin-insider-animated-result", 1f);
            CheckOrigin(animated, outcomes[0], expect, records, "25-origin-insider-animated-result");
            expect(animatedRolls == 1, "animated capture makes exactly one local delegate roll");
            animated.Close();
            DeviceFeel.OverrideReducedMotion(true);
        }

        private static CharacterCreatorScreen NewCreator(ConfigStore config, List<UiScreen> owned,
            HashSet<string> draftKeys, Registries reg = null)
        {
            foreach (string key in draftKeys) PlayerPrefs.DeleteKey(key);
            var screen = new CharacterCreatorScreen(reg ?? config.Registries,
                _ => throw new InvalidOperationException("Capture must never submit a character"), serverLookConfirmed: _ => true);
            owned.Add(screen);
            screen.Show();
            return screen;
        }

        private static async Task SelectLook(CharacterCreatorScreen screen, string head, Func<float, Task> wait, Action<bool, string> expect)
        {
            await wait(.7f);
            var models = Field<List<BaseModel>>(screen, "_models");
            int index = models.FindIndex(model => model.look != null && model.look.Length > 1 && model.look[1] == head);
            if (index < 0) throw new InvalidOperationException("Actual requested character is missing: " + head);
            screen.PickForAutomation(index);
            await wait(.45f);
            expect(screen.Flow.Draft.look[1] == head, "capture selected exact authored character " + head);
        }

        private static async Task ToTribe(CharacterCreatorScreen screen, Func<float, Task> wait, Action<bool, string> expect)
        {
            screen.PressCta(); await wait(.25f);
            if (screen.Step == CreatorStep.Customize) { screen.PressCta(); await wait(.25f); }
            expect(screen.Step == CreatorStep.Tribe, "creator advances through its real optional customization step");
        }

        private static void PickTraits(CharacterCreatorScreen screen, Registries reg)
        {
            foreach (var item in reg.Traits.OrderByDescending(item => OnboardingCopy.TraitName(item).Length).Take(2))
                screen.PickForAutomation(reg.Traits.IndexOf(item));
        }

        private static async Task Freeze(AvatarPreview preview, Animator animator, string state, float phase, Func<float, Task> wait)
        {
            if (!animator.HasState(0, Animator.StringToHash(state))) throw new InvalidOperationException("Missing actual animation state: " + state);
            preview.PlayReaction(state);
            animator.speed = 0;
            animator.Play(state, 0, phase);
            animator.Update(0);
            await wait(.12f); // upload the sampled skinned pose before the next offscreen render
        }

        private static void CheckSide(CharacterCreatorScreen screen, AvatarPreview preview, Animator animator, string state,
            Action<bool, string> expect, JArray records, string name)
        {
            var model = Field<VisualElement>(screen, "_model");
            var camera = preview.Camera;
            var bones = new JObject();
            foreach (var id in new[] { HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
            {
                var bone = animator.GetBoneTransform(id);
                if (bone == null) throw new InvalidOperationException("Missing pose bone " + id);
                var point = camera.WorldToViewportPoint(bone.position);
                bones[id.ToString()] = new JArray(point.x, point.y, point.z);
            }
            expect(!preview.Podium, name + " hides the podium on question pages");
            expect(model.pickingMode == PickingMode.Ignore, name + " disables character interaction on question pages");
            expect(Contains(model.worldBound, screen.Element.worldBound),
                name + " character texture spans the canvas instead of ending at an interior portrait edge");
            if (state == "idle_fidget_stretch")
            {
                float head = animator.GetBoneTransform(HumanBodyBones.Head).position.y;
                expect(animator.GetBoneTransform(HumanBodyBones.LeftHand).position.y > head &&
                    animator.GetBoneTransform(HumanBodyBones.RightHand).position.y > head,
                    name + " is an actual both-hands-above-head pose");
            }
            records.Add(new JObject { ["capture"] = name, ["panelBounds"] = Bounds(screen.Element.worldBound),
                ["modelBounds"] = Bounds(model.worldBound), ["boneViewportCoordinates"] = bones,
                ["note"] = "Viewport coordinates are recorded for crop diagnosis; intentional screen-edge bleed is judged from the actual PNG." });
        }

        private static void CheckPlayerCard(CharacterCreatorScreen screen, Action<bool, string> expect, JArray records,
            string name, string alias, string dream)
        {
            var root = screen.Element;
            var card = Field<VisualElement>(screen, "_idCard");
            var origin = Field<VisualElement>(screen, "_cardOriginRow");
            var nameLabel = Field<Label>(screen, "_cardName");
            var dreamLabel = Field<Label>(screen, "_cardDream");
            var traits = Field<VisualElement>(screen, "_cardTraits");
            var cta = root.Q<UI.Kit.LippedButton>("ob-cta");
            expect(screen.Origin == null && origin.resolvedStyle.display == DisplayStyle.None, name + " does not invent an origin before the draw");
            expect(nameLabel.text == alias && dreamLabel.text == dream, name + " shows the actual selected alias and dream");
            expect(nameLabel.resolvedStyle.fontSize >= 44f, name + " alias fitting preserves a readable 44-unit minimum");
            expect(Contains(root.worldBound, card.worldBound), name + " collectible card fits the screen");
            expect(Contains(root.worldBound, screen.AgeRow.worldBound), name + " 18+ checkbox remains fully on screen");
            expect(!card.Contains(screen.AgeRow), name + " 18+ consent remains outside the collectible");
            expect(!card.worldBound.Overlaps(cta.worldBound) && !screen.AgeRow.worldBound.Overlaps(cta.worldBound),
                name + " card and age confirmation do not cover the primary action");
            expect(!cta.enabledInHierarchy, name + " continuation still requires explicit age confirmation");
            CheckLabel(nameLabel, card, name, expect, records);
            CheckLabel(dreamLabel, card, name, expect, records);
            foreach (var label in traits.Query<Label>().ToList()) CheckLabel(label, card, name, expect, records);
        }

        private static void CheckOrigin(LotteryScreen screen, PostLotteryRoll200Response result,
            Action<bool, string> expect, JArray records, string name)
        {
            var winner = screen.Element.Q("card_" + LotteryModel.OutcomeId(result.Outcome));
            var cash = winner.Q<Label>("origin-cash");
            var home = winner.Q<Label>("origin-home");
            var debt = winner.Q<Label>("origin-debt");
            var cta = screen.Element.Q<UI.Kit.LippedButton>("ob-cta");
            expect(screen.Revealed && winner.resolvedStyle.display != DisplayStyle.None, name + " presents the actual returned outcome");
            expect(cash.text == LotteryModel.CashWhole(result.CashCents), name + " preserves returned starting cash");
            expect(result.DebtCents > 0 ? debt.text == LotteryModel.CashWhole(result.DebtCents) && debt.parent.resolvedStyle.display != DisplayStyle.None
                : debt.parent.resolvedStyle.display == DisplayStyle.None, name + " presents debt only when owed");
            expect(Contains(screen.Element.worldBound, winner.worldBound), name + " origin illustration and details fit the screen");
            expect(!winner.worldBound.Overlaps(cta.worldBound), name + " origin result keeps continuation clear");
            foreach (var label in winner.Query<Label>().ToList().Where(label => label.resolvedStyle.display != DisplayStyle.None && label.worldBound.width > 0))
                if (label.parent.resolvedStyle.display != DisplayStyle.None) CheckLabel(label, winner, name, expect, records);
            records.Add(new JObject { ["capture"] = name, ["origin"] = LotteryModel.OutcomeId(result.Outcome),
                ["cash"] = cash.text, ["home"] = home.text, ["debt"] = debt.text,
                ["resultBounds"] = Bounds(winner.worldBound), ["ctaBounds"] = Bounds(cta.worldBound) });
        }

        private static void CheckLabel(Label label, VisualElement container, string capture, Action<bool, string> expect, JArray records)
        {
            if (string.IsNullOrEmpty(label.text) || label.resolvedStyle.display == DisplayStyle.None) return;
            float longestWord = label.text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                .Select(word => label.MeasureTextSize(word, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x)
                .DefaultIfEmpty(0f).Max();
            bool wordsFit = longestWord <= label.contentRect.width + 1f;
            bool boundsFit = Contains(container.worldBound, label.worldBound);
            expect(wordsFit, capture + " label keeps whole words: " + label.text);
            expect(boundsFit, capture + " label stays in its composition: " + label.text);
            records.Add(new JObject { ["capture"] = capture, ["text"] = label.text, ["labelBounds"] = Bounds(label.worldBound),
                ["containerBounds"] = Bounds(container.worldBound), ["longestWordWidth"] = longestWord,
                ["availableWidth"] = label.contentRect.width, ["wholeWordsFit"] = wordsFit, ["boundsFit"] = boundsFit });
        }

        private static T Field<T>(object instance, string name) where T : class =>
            typeof(CharacterCreatorScreen).GetField(name, PrivateInstance)?.GetValue(instance) as T
            ?? throw new MissingFieldException("Capture could not inspect " + name);

        private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 1f && inner.yMin >= outer.yMin - 1f &&
            inner.xMax <= outer.xMax + 1f && inner.yMax <= outer.yMax + 1f;

        private static JArray Bounds(Rect value) => new JArray(value.xMin, value.yMin, value.xMax, value.yMax);

        private static async Task Until(Func<bool> ready, Func<float, Task> wait, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!ready())
            {
                if (Time.realtimeSinceStartup >= end) throw new TimeoutException("Capture screen did not reach the requested state");
                await wait(.1f);
            }
        }

        private static string Arg(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
