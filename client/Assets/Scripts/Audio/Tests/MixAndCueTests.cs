using NUnit.Framework;

namespace CTLife.Audio.Tests
{
    public class DuckerTests
    {
        [Test]
        public void VoiceDucksSixDb()
        {
            var d = new Ducker();
            d.Hold(Ducker.VoiceKey, Ducker.VoiceDuckDb);
            Assert.AreEqual(-6f, d.TargetDb(0), 1e-4);
            float db = 0f;
            for (int i = 0; i < 20; i++) db = d.Tick(i * 0.01, 0.01f);
            Assert.AreEqual(-6f, db, 1e-3, "attack reaches −6 dB within 0.2 s");
            d.Clear(Ducker.VoiceKey);
            db = d.Tick(0.3, 0.1f);
            Assert.That(db, Is.GreaterThan(-6f).And.LessThan(-4f), "release is slow (12 dB/s)");
            for (int i = 0; i < 10; i++) db = d.Tick(0.4 + i * 0.1, 0.1f);
            Assert.AreEqual(0f, db, 1e-4);
        }

        [Test]
        public void DeepestHoldWinsWithoutStacking()
        {
            var d = new Ducker();
            d.Hold(Ducker.VoiceKey, -6f);
            d.Hold("spaces", -6f);
            d.HoldUntil(Ducker.StingerKey, Ducker.StingerDuckDb, now: 0, until: 3);
            Assert.AreEqual(-9f, d.TargetDb(1), 1e-4, "voice + Space + stinger = −9, not −21");
            Assert.AreEqual(-6f, d.TargetDb(3.01), 1e-4, "stinger expires at its clip end");
            d.Clear("spaces");
            Assert.AreEqual(-6f, d.TargetDb(4), 1e-4, "proximity voice still holds");
        }

        [Test]
        public void ShorterStingerDoesNotCutALongerOne()
        {
            var d = new Ducker();
            d.HoldUntil(Ducker.StingerKey, -9f, 0, 5);
            d.HoldUntil(Ducker.StingerKey, -9f, 1, 2);
            Assert.AreEqual(-9f, d.TargetDb(3), 1e-4);
        }

        [Test]
        public void InvalidHoldsAreIgnored()
        {
            var d = new Ducker();
            d.Hold("x", float.NaN);
            d.Hold("y", 3f);
            d.Hold(null, -6f);
            d.HoldUntil("z", -6f, 5, 4);
            Assert.AreEqual(0f, d.TargetDb(0));
            d.Hold("call", -12f);
            Assert.IsTrue(d.IsHeld("call"));
            d.Hold("call", 0f);
            Assert.IsFalse(d.IsHeld("call"), "0 dB clears a hold");
        }

        [Test]
        public void SnapJumpsToTarget()
        {
            var d = new Ducker();
            d.Hold(Ducker.VoiceKey, -6f);
            d.Snap(0);
            Assert.AreEqual(-6f, d.CurrentDb);
            Assert.AreEqual(-6f, d.Tick(0, float.NaN), 1e-4);
        }
    }

    public class VolumeSettingsTests
    {
        [Test]
        public void TaperIsSquared()
        {
            Assert.AreEqual(0f, VolumeSettings.Taper(0f));
            Assert.AreEqual(1f, VolumeSettings.Taper(1f));
            Assert.AreEqual(-12.04f, AudioMath.LinearToDb(VolumeSettings.Taper(0.5f)), 0.05f);
            Assert.AreEqual(0f, VolumeSettings.Taper(float.NaN));
            Assert.AreEqual(1f, VolumeSettings.Taper(7f));
        }

        [Test]
        public void SetClampsAndReportsChange()
        {
            var s = new VolumeSettings();
            Assert.IsFalse(s.Set("master", 1f));
            Assert.IsTrue(s.Set("voice", 0.25f));
            Assert.AreEqual(0.25f, s.Get("voice"));
            Assert.IsTrue(s.Set("music", 5f));
            Assert.AreEqual(1f, s.Music);
            Assert.AreEqual(0.0625f, s.BusGain(AudioBus.Voice), 1e-6);
            Assert.Throws<System.ArgumentException>(() => s.Get("bogus"));
            foreach (var k in VolumeSettings.Keys) Assert.AreEqual(VolumeSettings.Default(k), new VolumeSettings().Get(k), k);
            // Music default ≈ −3 dB (title theme −16 LUFS → about −19 LUFS; spec mix target −18..−16).
            Assert.AreEqual(-2.8f, AudioMath.LinearToDb(new VolumeSettings().BusGain(AudioBus.Music)), 0.1f);
        }
    }

    public class CueGateTests
    {
        [Test]
        public void SameStingerTwiceIsOnePlay()
        {
            var g = new CueGate();
            Assert.IsTrue(g.TryPass("sfx_liquidation", "stinger", 10.0), "RektCard");
            Assert.IsFalse(g.TryPass("sfx_liquidation", "stinger", 10.05), "IncidentPresentation, same event");
            Assert.IsTrue(g.TryPass("sfx_liquidation", "stinger", 11.5), "a later liquidation still plays");
        }

        [Test]
        public void UiTapsStillRepeat()
        {
            var g = new CueGate();
            Assert.IsTrue(g.TryPass("sfx_ui_tap", "ui", 0));
            Assert.IsFalse(g.TryPass("sfx_ui_tap", "ui", 0.03));
            Assert.IsTrue(g.TryPass("sfx_ui_tap", "ui", 0.1));
            Assert.IsTrue(g.TryPass("sfx_ui_success", "ui", 0.1), "different ids are independent");
        }

        [Test]
        public void WhaleHornOncePerBurst()
        {
            var g = new CueGate();
            Assert.IsTrue(g.TryPass(SfxIds.WhaleHorn, "trading", 0));
            Assert.IsFalse(g.TryPass(SfxIds.WhaleHorn, "trading", 19));
            Assert.IsTrue(g.TryPass(SfxIds.WhaleHorn, "trading", 20.5));
        }

        [Test]
        public void ToastTickIsMaskedByARicherCue()
        {
            var g = new CueGate();
            Assert.IsTrue(g.TryPass(SfxIds.CashIn, "trading", 5.0));
            Assert.IsFalse(g.TryPass(SfxIds.UiToast, "ui", 5.05), "payment toast under the cash-in cue");
            Assert.IsTrue(g.TryPass(SfxIds.UiToast, "ui", 5.5));
            Assert.IsTrue(g.TryPass(SfxIds.NewsFlash, "news", 9.0));
            Assert.IsFalse(g.TryPass(SfxIds.UiToast, "ui", 9.1), "a news card's own toast tick is masked too");
            Assert.IsTrue(g.TryPass(SfxIds.Gm, "social", 20.0));
            Assert.IsTrue(g.TryPass(SfxIds.UiToast, "ui", 20.05), "gm is not a rich cue");
        }

        [Test]
        public void ClockGoingBackwardsNeverBlocksForever()
        {
            var g = new CueGate();
            Assert.IsTrue(g.TryPass("sfx_rug", "stinger", 100));
            Assert.IsTrue(g.TryPass("sfx_rug", "stinger", 1), "time reset after reload");
        }

        [TestCase("sfx_ui_toggle", "sfx_ui_toggle_on")]
        [TestCase("sfx_pnl_flip", "sfx_pnl_card_flip")]
        [TestCase("sfx_sunday_bell", "sfx_sunday_unlock")]
        [TestCase("sfx_room_join", "sfx_vr_join")]
        [TestCase("sfx_room_leave", "sfx_vr_leave")]
        [TestCase("sfx_raise_hand", "sfx_vr_raise_hand")]
        [TestCase("sfx_promoted", "sfx_vr_promoted")]
        [TestCase("sfx_origin_seeker", "sfx_origin_insider")]
        [TestCase("sfx_origin_exit_liquidity", "sfx_origin_exit_liquidity")]
        [TestCase("sfx_buy", "sfx_buy")]
        public void RegistryAliasesResolve(string id, string canonical) => Assert.AreEqual(canonical, CueGate.Canonical(id));

        [Test]
        public void OriginStingersShareTheStingerCooldown()
        {
            var g = new CueGate();
            Assert.IsTrue(g.TryPass(CueGate.Canonical(SfxIds.OriginSeeker), "stinger", 0));
            Assert.IsFalse(g.TryPass(SfxIds.OriginInsider, "stinger", 0.5), "same reveal, one fanfare");
            Assert.IsTrue(g.TryPass(SfxIds.CreatorComplete, "stinger", 0.6), "different stinger");
            Assert.IsTrue(g.TryPass(SfxIds.CardShuffle, "card", 0));
            Assert.IsTrue(g.TryPass(SfxIds.UiNext, "ui", 0));
        }

        [Test]
        public void EmptyIdNeverPasses()
        {
            var g = new CueGate();
            Assert.IsFalse(g.TryPass(null, "ui", 0));
            Assert.IsFalse(g.TryPass("", "ui", 0));
        }
    }

    public class CueRuleTests
    {
        [TestCase(999_999L, null)]
        [TestCase(1_000_000L, "sfx_whale_horn")]
        [TestCase(-2_500_000L, "sfx_whale_horn")]
        [TestCase(100_000L, null)]
        public void WhaleHornAtTenTimesTheTapeThreshold(long cents, string expected) =>
            Assert.AreEqual(expected, CueRules.ForTape("buy", cents));

        [Test]
        public void WhaleThresholdFollowsConfig()
        {
            Assert.AreEqual(5_000_000L, CueRules.WhaleThresholdCents(500_000));
            Assert.AreEqual(1_000_000L, CueRules.WhaleThresholdCents(0), "never below $10k");
        }

        [TestCase("new", "graduated", "sfx_graduation")]
        [TestCase("final", "graduated", "sfx_graduation")]
        [TestCase("graduated", "graduated", null)]
        [TestCase("new", "dead", null)]
        public void GraduationOnStageChange(string from, string to, string expected) =>
            Assert.AreEqual(expected, CueRules.ForCoinStage(from, to));

        [TestCase("gm", true)]
        [TestCase("GM", true)]
        [TestCase("gm!", true)]
        [TestCase("gm gm", true)]
        [TestCase("gmgm", true)]
        [TestCase("gmmm ☀️", true)]
        [TestCase("GM CT", true)]
        [TestCase("gm frens", true)]
        [TestCase("gm fam wagmi", true)]
        [TestCase("good morning", true)]
        [TestCase("Good morning CT", true)]
        [TestCase("gn", false)]
        [TestCase("game on", false)]
        [TestCase("gm I just got liquidated on my 50x long", false)]
        [TestCase("gmx is pumping", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void GmRitual(string text, bool expected) => Assert.AreEqual(expected, CueRules.IsGm(text));

        [TestCase("yo @Dominion check this", "dominion", true)]
        [TestCase("@dominion", "@Dominion", true)]
        [TestCase("ask @dominion_2 instead", "dominion", false)]
        [TestCase("email me dominion@site", "site", true)]
        [TestCase("no mention here", "dominion", false)]
        [TestCase("@dom", "dominion", false)]
        [TestCase("hey @", "dominion", false)]
        [TestCase("@dominion.", "dominion", true)]
        public void Mentions(string text, string handle, bool expected) =>
            Assert.AreEqual(expected, CueRules.MentionsHandle(text, handle));

        [Test]
        public void MentionNeedsAHandle()
        {
            Assert.IsFalse(CueRules.MentionsHandle("@x", null));
            Assert.IsFalse(CueRules.MentionsHandle("@x", "@"));
            Assert.IsFalse(CueRules.MentionsHandle(null, "x"));
        }
    }
}
