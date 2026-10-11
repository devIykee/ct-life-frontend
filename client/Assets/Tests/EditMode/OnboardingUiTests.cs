using System.Collections.Generic;
using CTLife.Core;
using CTLife.Rest.Model;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace CTLife.Tests
{
    /// <summary>
    /// The first-time flow redesign (docs/progress/onboarding-ui.md): the creator's step machine and validation, the saved
    /// draft, the lottery helpers (odds only from the served config), copy fallbacks, art fallbacks and the UI kit's states.
    /// </summary>
    public class OnboardingUiTests
    {
        private static CreatorFlow Flow(bool hasLook = true, bool submittable = true)
        {
            var f = new CreatorFlow(new CharacterDraft()) { HasLook = hasLook, LookSubmittable = submittable };
            return f;
        }

        private static CreatorFlow Answered()
        {
            var f = Flow();
            Assert.IsTrue(f.Next()); // look
            f.SelectTribe("trencher");
            Assert.IsTrue(f.Next());
            f.ToggleTrait("fast_talker");
            f.ToggleTrait("iron_will");
            Assert.IsTrue(f.Next());
            f.SelectDream("make_10k");
            Assert.IsTrue(f.Next());
            f.SetAlias("  degen  ");
            Assert.IsTrue(f.Next());
            Assert.AreEqual(CreatorStep.Confirm, f.Step);
            return f;
        }

        // ---------------------------------------------------------------- step machine

        [Test]
        public void StartsOnLook_AndNeedsALookToLeave()
        {
            var f = Flow(hasLook: false);
            Assert.AreEqual(CreatorStep.Look, f.Step);
            Assert.AreEqual("creator.needLook", f.CurrentProblem);
            Assert.IsFalse(f.Next());
            f.HasLook = true;
            Assert.IsTrue(f.Next());
            Assert.AreEqual(CreatorStep.Tribe, f.Step);
        }

        [Test]
        public void EachStepIsGatedUntilAnswered()
        {
            var f = Flow();
            f.Next();
            Assert.AreEqual("ob.hint.tribe", f.CurrentProblem);
            Assert.IsFalse(f.Next());
            f.SelectTribe("anon");
            Assert.IsNull(f.CurrentProblem);
            f.Next();
            Assert.AreEqual("ob.hint.traits2", f.CurrentProblem);
            f.ToggleTrait("sleuth");
            Assert.AreEqual("ob.hint.traits1", f.CurrentProblem);
            Assert.IsFalse(f.Next());
            f.ToggleTrait("early_bird");
            Assert.IsTrue(f.Next());
            Assert.AreEqual("ob.hint.dream", f.CurrentProblem);
            f.SelectDream("host_a_space");
            Assert.IsTrue(f.Next());
            Assert.AreEqual("ob.hint.name", f.CurrentProblem);
            f.SetAlias("ab");
            Assert.IsFalse(f.Next());
            f.SetAlias("abc");
            Assert.IsTrue(f.Next());
            Assert.AreEqual(CreatorStep.Confirm, f.Step);
        }

        [Test]
        public void ThirdTraitReplacesTheOldest_AndTappingAPickDeselects()
        {
            var f = Flow();
            Assert.IsNull(f.ToggleTrait("a"));
            Assert.IsNull(f.ToggleTrait("b"));
            Assert.AreEqual("a", f.ToggleTrait("c"));
            CollectionAssert.AreEqual(new[] { "b", "c" }, f.Draft.traitIds);
            Assert.IsNull(f.ToggleTrait("b"));
            CollectionAssert.AreEqual(new[] { "c" }, f.Draft.traitIds);
            Assert.IsNull(f.ToggleTrait(null));
        }

        [Test]
        public void Confirm_NeedsAgeAndASubmittableLook()
        {
            var f = Answered();
            Assert.AreEqual("ob.hint.age", f.CurrentProblem);
            Assert.IsFalse(f.Next(), "Confirm submits; it never steps further");
            f.SetAge(true);
            Assert.IsNull(f.CurrentProblem);
            f.LookSubmittable = false;
            Assert.AreEqual("creator.lookNotAvailable", f.CurrentProblem);
            f.ReadOnlyReview = true;
            Assert.IsNull(f.CurrentProblem, "a read-only review never submits, so the look need not be accepted");
        }

        [Test]
        public void Confirm_ReportsAnEarlierStepsProblemFirst()
        {
            var f = Answered();
            f.SetAge(true);
            f.Draft.traitIds.Clear();
            Assert.AreEqual("ob.hint.traits2", f.CurrentProblem);
            Assert.AreEqual(CreatorStep.Traits, f.FirstUnanswered());
        }

        [Test]
        public void BackKeepsSelections_AndJumpOnlyToReachedSteps()
        {
            var f = Answered();
            Assert.IsTrue(f.Back());
            Assert.AreEqual(CreatorStep.Name, f.Step);
            Assert.AreEqual("degen", f.Draft.alias);
            Assert.IsTrue(f.JumpTo(CreatorStep.Tribe));
            Assert.AreEqual("trencher", f.Draft.tribeId);
            Assert.IsTrue(f.JumpTo(CreatorStep.Confirm));
            var fresh = Flow();
            Assert.IsFalse(fresh.JumpTo(CreatorStep.Dream));
            Assert.IsFalse(fresh.Back());
        }

        [Test]
        public void AliasRules_TrimAndLength()
        {
            Assert.AreEqual(AliasState.Empty, CreatorFlow.CheckAlias("   "));
            Assert.AreEqual(AliasState.Empty, CreatorFlow.CheckAlias(null));
            Assert.AreEqual(AliasState.TooShort, CreatorFlow.CheckAlias(" ab "));
            Assert.AreEqual(AliasState.Ok, CreatorFlow.CheckAlias(" abc "));
            Assert.AreEqual(AliasState.Ok, CreatorFlow.CheckAlias(new string('x', 20)));
            Assert.AreEqual(AliasState.TooLong, CreatorFlow.CheckAlias(new string('x', 21)));
            var f = Flow();
            f.SetAlias("  name  ");
            Assert.AreEqual("name", f.Draft.alias, "the submitted alias is trimmed, as before");
        }

        [Test]
        public void Progress_CountsTheLotteryAsStepOne()
        {
            Assert.AreEqual(7, CreatorFlow.ProgressSegments);
            Assert.AreEqual(1, CreatorFlow.ProgressFor(CreatorStep.Look));
            Assert.AreEqual(6, CreatorFlow.ProgressFor(CreatorStep.Confirm));
        }

        [Test]
        public void Restore_NeverLandsAheadOfTheFirstUnansweredStep()
        {
            var f = Flow();
            f.SelectTribe("trencher");
            f.Restore(CreatorStep.Name, CreatorStep.Name);
            Assert.AreEqual(CreatorStep.Traits, f.Step);
            Assert.AreEqual(CreatorStep.Traits, f.Furthest);
            var g = Answered();
            var h = new CreatorFlow(g.Draft) { HasLook = true, LookSubmittable = true };
            h.Restore(CreatorStep.Dream, CreatorStep.Confirm);
            Assert.AreEqual(CreatorStep.Dream, h.Step);
            Assert.IsTrue(h.JumpTo(CreatorStep.Confirm));
        }

        [Test]
        public void Sanitize_DropsIdsNotInTheRegistry()
        {
            var f = Flow();
            f.SelectTribe("gone");
            f.Draft.traitIds.AddRange(new[] { "sleuth", "gone", "iron_will" });
            f.SelectDream("gone");
            f.Sanitize(new[] { "trencher" }, new[] { "sleuth", "iron_will" }, new[] { "make_10k" });
            Assert.IsNull(f.Draft.tribeId);
            CollectionAssert.AreEqual(new[] { "sleuth", "iron_will" }, f.Draft.traitIds);
            Assert.IsNull(f.Draft.dreamId);
        }

        // ---------------------------------------------------------------- saved draft

        [Test]
        public void Draft_RoundTripsWithoutTheAgeConfirmation()
        {
            var f = Answered();
            f.SetAge(true);
            f.JumpTo(CreatorStep.Dream);
            var json = CreatorDraftStore.Serialize(f, "skin|head|hair|top|bottom|shoes|acc", 215f, true);
            var saved = CreatorDraftStore.Parse(json);
            Assert.IsNotNull(saved);
            Assert.AreEqual(CreatorStep.Dream, saved.step);
            Assert.AreEqual(CreatorStep.Confirm, saved.furthest);
            Assert.AreEqual("skin|head|hair|top|bottom|shoes|acc", saved.lookKey);
            Assert.AreEqual(215f, saved.yaw, 0.001f);
            Assert.IsTrue(saved.portrait);
            var g = new CreatorFlow(new CharacterDraft());
            CreatorDraftStore.Apply(saved, g);
            Assert.AreEqual("trencher", g.Draft.tribeId);
            CollectionAssert.AreEqual(new[] { "fast_talker", "iron_will" }, g.Draft.traitIds);
            Assert.AreEqual("make_10k", g.Draft.dreamId);
            Assert.AreEqual("degen", g.Draft.alias);
            Assert.IsFalse(g.Draft.ageConfirmed, "18+ is asked again after a relaunch");
            StringAssert.DoesNotContain("age", json);
        }

        [Test]
        public void Draft_BadOrOldDataStartsFresh()
        {
            Assert.IsNull(CreatorDraftStore.Parse(null));
            Assert.IsNull(CreatorDraftStore.Parse("{not json"));
            Assert.IsNull(CreatorDraftStore.Parse("{\"v\":0,\"step\":3}"));
            Assert.IsNull(CreatorDraftStore.Parse("{\"v\":1,\"step\":3}"), "a v1 draft (before Customize) starts fresh");
            var s = CreatorDraftStore.Parse("{\"v\":2,\"step\":99,\"furthest\":-4}");
            Assert.AreEqual(CreatorStep.Confirm, s.step);
            Assert.AreEqual(CreatorStep.Look, s.furthest);
        }

        // ---------------------------------------------------------------- lottery

        private static readonly JObject Lottery = JObject.Parse(@"{
            ""weights"": { ""seeker"": 0, ""insider"": 20, ""exit_liquidity"": 80 },
            ""outcomes"": { ""seeker"": { ""homeId"": ""hacker_house"" }, ""insider"": { ""homeId"": ""studio"" },
                          ""exit_liquidity"": { ""homeId"": ""parents_basement"", ""debtCents"": 72000 } } }");

        [Test]
        public void Odds_ComeOnlyFromTheServedWeights()
        {
            var odds = LotteryModel.Odds(Lottery, LotteryModel.Cards(false));
            Assert.AreEqual(20, odds[LotteryModel.Insider]);
            Assert.AreEqual(80, odds[LotteryModel.Exit]);
            Assert.IsNull(LotteryModel.Odds(Lottery, LotteryModel.Cards(true)), "a zero weight hides the odds instead of showing 0%");
            Assert.IsNull(LotteryModel.Odds(null, LotteryModel.Cards(false)));
            Assert.IsNull(LotteryModel.Odds(JObject.Parse("{\"weights\":{\"insider\":\"x\"}}"), LotteryModel.Cards(false)));
        }

        [Test]
        public void Outcomes_MapIdsHomesAndCash()
        {
            Assert.AreEqual("insider", LotteryModel.OutcomeId(PostLotteryRoll200Response.OutcomeEnum.Insider));
            Assert.AreEqual("exit_liquidity", LotteryModel.OutcomeId(PostLotteryRoll200Response.OutcomeEnum.ExitLiquidity));
            Assert.AreEqual("seeker", LotteryModel.OutcomeId(PostLotteryRoll200Response.OutcomeEnum.Seeker));
            Assert.AreEqual("insider", LotteryModel.OutcomeForHome(Lottery, "studio"));
            Assert.IsNull(LotteryModel.OutcomeForHome(Lottery, "mansion"));
            Assert.AreEqual("$3,000", LotteryModel.CashWhole(300000));
            Assert.AreEqual("$720", LotteryModel.CashWhole(72000));
            Assert.AreEqual("$12.50", LotteryModel.CashWhole(1250));
            var origin = OriginSummary.From(30000, 72000, "parents_basement", Lottery);
            Assert.AreEqual("exit_liquidity", origin.outcomeId);
            Assert.AreEqual("exit_liquidity", OriginSummary.From(30000, 72000, null, null).outcomeId, "debt means the exit outcome even without config");
            Assert.IsNull(OriginSummary.From(0, 0, null, null));
            CollectionAssert.AreEqual(new[] { "insider", "exit_liquidity", "seeker" }, LotteryModel.Cards(true));
        }

        // ---------------------------------------------------------------- copy and art

        [Test]
        public void TraitEffect_FriendlyOnlyWhileTheRegistryTextMatches()
        {
            var t = new SimpleDef { id = "iron_will", name = "Iron Will", description = "Sanity decay 0.8x" };
            Assert.AreEqual("Sanity drains 20% slower", OnboardingCopy.TraitEffect(t));
            t.description = "Sanity decay 0.7x";
            Assert.AreEqual("Sanity decay 0.7x", OnboardingCopy.TraitEffect(t), "a changed server effect is shown as the server wrote it");
            Assert.IsNull(OnboardingCopy.TraitEffect(new SimpleDef { id = "new_trait", description = " " }));
            Assert.IsNull(OnboardingCopy.TraitEffect(null));
        }

        [Test]
        public void TribeCopy_FallsBackForUnknownTribes()
        {
            Assert.AreEqual("Living in the trenches.", OnboardingCopy.TribeLine("trencher"));
            Assert.AreEqual(Strings.T("ob.say.tribePick"), OnboardingCopy.TribeLine("brand_new_tribe"));
            var emotes = OnboardingCopy.TribeEmotes(new TribeDef { id = "trencher", signatureEmoteId = "emote_send_it_slam" });
            Assert.AreEqual("emote_send_it_slam", emotes[0]);
            Assert.AreEqual("emote_send_it", emotes[1]);
            Assert.AreEqual("Fresh fit.", OnboardingCopy.Variant("ob.say.lookPick", 3, 3));
            Assert.AreEqual("Clean.", OnboardingCopy.Variant("ob.say.lookPick", -2, 3));
        }

        [Test]
        public void EveryOnboardingKeyHasCopy()
        {
            var keys = new List<string>
            {
                "ob.next", "ob.done", "ob.saving", "ob.lottery.cta", "ob.lottery.next", "ob.lottery.note", "ob.lottery.insider.line",
                "ob.lottery.exit.line", "ob.lottery.exit.line2", "ob.lottery.seeker.line", "ob.look.title", "ob.tribe.title",
                "ob.traits.title", "ob.dream.title", "ob.name.title", "ob.confirm.title", "ob.confirm.age", "ob.confirm.cta",
                "ob.hint.tribe", "ob.hint.traits1", "ob.hint.traits2", "ob.hint.dream", "ob.hint.name", "ob.hint.age",
                "ob.celebrate.title", "ob.celebrate.cta", "ob.say.lookPick.0", "ob.say.lookPick.1", "ob.say.lookPick.2",
                "creator.needLook", "creator.lookNotAvailable",
            };
            foreach (var id in new[] { "trencher", "perp_degen", "prediction_shark", "airdrop_farmer", "caller", "builder", "anon" })
                keys.Add("ob.tribe.say." + id);
            foreach (var k in keys) Assert.AreNotEqual(k, Strings.T(k), "missing copy for " + k);
            StringAssert.Contains("can't be withdrawn", Strings.T("ob.lottery.note"));
        }

        [Test]
        public void Art_LoadsByIdAndIsNullWhenMissing()
        {
            foreach (var id in new[] { "trencher", "perp_degen", "prediction_shark", "airdrop_farmer", "caller", "builder", "anon" })
                Assert.IsNotNull(OnboardingArt.TribeBadge(id), "tribe badge " + id);
            Assert.IsNull(OnboardingArt.TribeBadge("not_a_tribe"));
            Assert.IsNull(OnboardingArt.TribeBadge(null));
            Assert.IsNotNull(OnboardingArt.CardBack);
            Assert.IsNotNull(OnboardingArt.CardFront("insider"));
            Assert.IsNotNull(OnboardingArt.CardFront("exit_liquidity"));
            Assert.IsNull(OnboardingArt.CardFront("seeker"), "no Seeker face yet: the lottery falls back to a plain named card");
            Assert.IsNotNull(OnboardingArt.Backdrop);
            Assert.IsNotNull(OnboardingArt.Glow);
        }

        // ---------------------------------------------------------------- kit states

        [Test]
        public void ChoiceCard_StatesAreClassesNotJustColour()
        {
            int picks = 0;
            var card = new UI.Kit.ChoiceCard("x", "Title", "Perk", _ => picks++);
            card.SetState(UI.Kit.ChoiceCard.State.Selected);
            Assert.IsTrue(card.ClassListContains("kit-card--selected"));
            Assert.IsNotNull(card.Q(className: "kit-card__check"), "selected state has a check badge");
            card.SetState(UI.Kit.ChoiceCard.State.Error);
            Assert.IsTrue(card.ClassListContains("kit-card--error"));
            Assert.IsFalse(card.ClassListContains("kit-card--selected"));
            card.Press();
            Assert.AreEqual(1, picks);
            card.SetState(UI.Kit.ChoiceCard.State.Disabled);
            card.Press();
            Assert.AreEqual(1, picks, "a disabled card ignores presses");
            var box = new UI.Kit.ChoiceCard("age", "I'm 18 or older", null, null, UI.Kit.ChoiceCard.Layout.Horizontal, null, UI.Kit.ChoiceCard.Checkbox);
            Assert.IsNotNull(box.Q(className: "kit-card__box"), "the 18+ toggle has its checkbox with the label");
        }

        [Test]
        public void LippedButton_PressRespectsDisabled_AndBusyRestoresTheLabel()
        {
            int clicks = 0;
            var b = new UI.Kit.LippedButton("NEXT", () => clicks++, sfx: null);
            b.Press();
            Assert.AreEqual(1, clicks);
            b.SetBusy(true, "SAVING…");
            Assert.AreEqual("SAVING…", b.Text);
            b.Press();
            Assert.AreEqual(1, clicks);
            b.SetBusy(false, "SAVING…");
            Assert.AreEqual("NEXT", b.Text);
            Assert.IsTrue(b.enabledSelf);
        }

        [Test]
        public void ProgressBar_ClampsAndHasANotchPerStep()
        {
            var bar = new UI.Kit.ProgressBar(7);
            bar.Set(9, false);
            Assert.AreEqual(7, bar.Done);
            bar.Set(-1, false);
            Assert.AreEqual(0, bar.Done);
            Assert.AreEqual(6, bar.Query(className: "kit-progress__notch").ToList().Count);
        }

        [Test]
        public void ReducedMotion_CanBeForcedForCaptures()
        {
            DeviceFeel.OverrideReducedMotion(true);
            try { Assert.IsTrue(DeviceFeel.ReducedMotion); }
            finally { DeviceFeel.OverrideReducedMotion(null); }
            Assert.IsFalse(DeviceFeel.ReducedMotion, "the editor has no Android setting: motion is on");
        }
    }
}
