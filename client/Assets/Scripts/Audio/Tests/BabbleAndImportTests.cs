using System.Linq;
using NUnit.Framework;

namespace CTLife.Audio.Tests
{
    public class BabbleSchedulerTests
    {
        private static readonly BabbleVoice V = new BabbleVoice
        {
            CharsPerSecond = 10f, PitchJitterSemitones = 0f, NpcPitchMinSemitones = 0f, NpcPitchMaxSemitones = 0f, VolumeDb = -6f,
        };

        [Test]
        public void OneSyllablePerLetterAtTheArchetypeRate()
        {
            var s = BabbleScheduler.Schedule("abc", "npc_1", V);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, s.Select(x => x.Letter).ToArray());
            CollectionAssert.AreEqual(new[] { 0f, 0.1f, 0.2f }, s.Select(x => x.Time).ToArray(), new FloatTolerance());
        }

        [Test]
        public void DigitsAndSymbolsMapToLetters()
        {
            // 0→o 1→w 2→t 3→r 4→f 5→f 6→s 7→s 8→e 9→n; $→s @→a #→h %→p &→n +→p
            var s = BabbleScheduler.Schedule("0123456789$@#%&+", "n", V);
            Assert.AreEqual(16, s.Count);
            Assert.AreEqual("owtrffssensahpnp", new string(s.Select(x => (char)('a' + x.Letter)).ToArray()));
        }

        [Test]
        public void PunctuationIsAPauseOfTheSpecifiedLength()
        {
            float Gap(string text) { var s = BabbleScheduler.Schedule(text, "n", V); return s[1].Time - s[0].Time; }
            Assert.AreEqual(0.1f + 0.1f, Gap("a b"), 1e-4, "space = 1 step");
            Assert.AreEqual(0.1f + 0.2f, Gap("a,b"), 1e-4, "comma = 2 steps");
            Assert.AreEqual(0.1f + 0.3f, Gap("a.b"), 1e-4, "period = 3 steps");
            Assert.AreEqual(0.1f + 0.3f, Gap("a\nb"), 1e-4, "newline = 3 steps");
            Assert.AreEqual(0.1f + 0.1f, Gap("a-b"), 1e-4);
            Assert.AreEqual(0.1f + 0.05f, Gap("a*b"), 1e-4, "other = half a step");
        }

        [Test]
        public void SameNpcSameLineIsIdenticalAcrossClients()
        {
            var voice = BabbleVoice.Default;
            var a = BabbleScheduler.Schedule("JUST IN: $4.2M in longs liquidated", "npc_0042", voice);
            var b = BabbleScheduler.Schedule("JUST IN: $4.2M in longs liquidated", "npc_0042", voice);
            CollectionAssert.AreEqual(a.Select(x => x.Semitones).ToArray(), b.Select(x => x.Semitones).ToArray());
            var c = BabbleScheduler.Schedule("JUST IN: $4.2M in longs liquidated", "npc_0043", voice);
            Assert.AreNotEqual(a[0].Semitones, c[0].Semitones, "two NPCs of one archetype differ");
        }

        [Test]
        public void NpcOffsetStaysInTheBankRange()
        {
            var voice = BabbleVoice.Default;
            for (int i = 0; i < 200; i++)
            {
                float o = BabbleScheduler.NpcOffset("npc_" + i, voice);
                Assert.That(o, Is.InRange(voice.NpcPitchMinSemitones, voice.NpcPitchMaxSemitones));
            }
            Assert.AreEqual(0xCBF43926u, AudioMath.Crc32("123456789"), "CRC-32/IEEE check value");
        }

        [Test]
        public void UppercaseShouts()
        {
            var s = BabbleScheduler.Schedule("aA", "n", V);
            Assert.AreEqual(1.5f, s[1].Semitones - s[0].Semitones, 1e-4);
            Assert.AreEqual(1.12f, s[1].Gain, 1e-4);
        }

        [Test]
        public void QuestionsRiseAtTheEnd()
        {
            var s = BabbleScheduler.Schedule("aaaaaa?", "n", V);
            CollectionAssert.AreEqual(new[] { 0f, 0f, 1f, 2f, 3f, 4f }, s.Select(x => x.Semitones).ToArray(), new FloatTolerance());
        }

        [Test]
        public void ExclamationsLiftTheLastThree()
        {
            var s = BabbleScheduler.Schedule("aaaaa!", "n", V);
            CollectionAssert.AreEqual(new[] { 0f, 0f, 1f, 1f, 1f }, s.Select(x => x.Semitones).ToArray(), new FloatTolerance());
            Assert.AreEqual(1.12f, s[4].Gain, 1e-4);
            Assert.AreEqual(1f, s[0].Gain, 1e-4);
        }

        [Test]
        public void LinesAreCappedAtTheTalkState()
        {
            var s = BabbleScheduler.Schedule(new string('a', 500), "n", V);
            Assert.That(s.Last().Time, Is.LessThanOrEqualTo(BabbleScheduler.MaxSeconds + 1e-4f));
            Assert.That(s.Count, Is.LessThanOrEqualTo(BabbleScheduler.MaxSyllables));
            Assert.AreEqual(s.Last().Time + 0.2f, BabbleScheduler.Duration(s), 1e-4);
        }

        [Test]
        public void EmptyAndHostileInputNeverThrow()
        {
            Assert.AreEqual(0, BabbleScheduler.Schedule(null, "n", V).Count);
            Assert.AreEqual(0, BabbleScheduler.Schedule("", null, V).Count);
            Assert.AreEqual(0, BabbleScheduler.Schedule("... !!! ???", "n", V).Count);
            Assert.AreEqual(0f, BabbleScheduler.Duration(null));
            var weird = new BabbleVoice { CharsPerSecond = float.NaN, PitchJitterSemitones = float.NaN };
            Assert.That(BabbleScheduler.Schedule("hello 🌕 wagmi", "n", weird).Count, Is.EqualTo(10));
        }
    }

    public class BabbleBudgetTests
    {
        [Test]
        public void AtMostThreeTalkersNearestWin()
        {
            var b = new BabbleBudget();
            Assert.AreEqual(BabbleBudget.Verdict.Allow, b.Request("a", 3f, 2f, 0).Verdict);
            Assert.AreEqual(BabbleBudget.Verdict.Allow, b.Request("b", 6f, 2f, 0).Verdict);
            Assert.AreEqual(BabbleBudget.Verdict.Allow, b.Request("c", 9f, 2f, 0).Verdict);
            Assert.AreEqual(BabbleBudget.Verdict.Deny, b.Request("d", 12f, 2f, 0.1).Verdict, "farther than every talker");
            var near = b.Request("e", 1f, 2f, 0.2);
            Assert.AreEqual(BabbleBudget.Verdict.AllowPreempt, near.Verdict);
            Assert.AreEqual("c", near.Preempt, "the farthest talker is cut");
            Assert.AreEqual(3, b.Active(0.3));
            Assert.AreEqual(0, b.Active(5), "lines end on time");
        }

        [Test]
        public void OneLinePerNpcPerInterval()
        {
            var b = new BabbleBudget();
            Assert.AreEqual(BabbleBudget.Verdict.Allow, b.Request("a", 3f, 0.5f, 0).Verdict);
            Assert.AreEqual(BabbleBudget.Verdict.Deny, b.Request("a", 3f, 0.5f, 1.0).Verdict);
            Assert.AreEqual(BabbleBudget.Verdict.Allow, b.Request("a", 3f, 0.5f, 1.6).Verdict);
        }

        [Test]
        public void OutOfEarshotIsSilent()
        {
            var b = new BabbleBudget();
            Assert.AreEqual(BabbleBudget.Verdict.Deny, b.Request("a", 15.5f, 1f, 0).Verdict);
            Assert.AreEqual(BabbleBudget.Verdict.Deny, b.Request("a", float.NaN, 1f, 0).Verdict);
            Assert.AreEqual(BabbleBudget.Verdict.Deny, b.Request(null, 1f, 1f, 0).Verdict);
            Assert.AreEqual(BabbleBudget.Verdict.Deny, b.Request("a", 1f, 0f, 0).Verdict, "nothing to say");
        }

        [Test]
        public void StoppedTalkerFreesItsSlot()
        {
            var b = new BabbleBudget();
            b.Request("a", 1f, 3f, 0);
            b.Request("b", 1f, 3f, 0);
            b.Request("c", 1f, 3f, 0);
            Assert.AreEqual(BabbleBudget.Verdict.Deny, b.Request("d", 1f, 3f, 0.1).Verdict);
            b.Stopped("a");
            Assert.AreEqual(BabbleBudget.Verdict.Allow, b.Request("d", 1f, 3f, 0.2).Verdict);
        }
    }

    public class ImportPolicyTests
    {
        [TestCase("Assets/Resources/Audio/music/licensed/mus_timeline_funky_chunk.ogg", "music", ClipLoad.Streaming)]
        [TestCase("Assets/Resources/Audio/music/stems/club/mus_club_drums.ogg", "music_layer", ClipLoad.CompressedInMemory)]
        [TestCase("Assets/Resources/Audio/music/mood/mus_mood_tension_bed.ogg", "music_layer", ClipLoad.CompressedInMemory)]
        [TestCase("Assets/Resources/Audio/ambience/amb_terminal_cafe.wav", "ambience", ClipLoad.Streaming)]
        [TestCase("Assets/Resources/Audio/babble/babble_town_crier_j.wav", "babble", ClipLoad.DecompressOnLoad)]
        [TestCase("Assets/Resources/Audio/sfx/foot_wood_03.wav", "footstep", ClipLoad.DecompressOnLoad)]
        [TestCase("Assets/Resources/Audio/sfx/sfx_liquidation.wav", "stinger", ClipLoad.CompressedInMemory)]
        [TestCase("Assets/Resources/Audio/sfx/sfx_rekt_phonk.wav", "stinger", ClipLoad.CompressedInMemory)]
        [TestCase("Assets/Resources/Audio/sfx/sfx_origin_insider.wav", "stinger", ClipLoad.CompressedInMemory)]
        [TestCase("Assets/Resources/Audio/sfx/sfx_origin_exit_liquidity.wav", "stinger", ClipLoad.CompressedInMemory)]
        [TestCase("Assets/Resources/Audio/sfx/sfx_creator_complete.wav", "stinger", ClipLoad.CompressedInMemory)]
        [TestCase("Assets/Resources/Audio/music/title/mus_title.ogg", "music", ClipLoad.Streaming)]
        [TestCase("Assets/Resources/Audio/sfx/sfx_card_shuffle.wav", "sfx", ClipLoad.DecompressOnLoad)]
        [TestCase("Assets/Resources/Audio/sfx/licensed/sfx_ui_tap_kenney.wav", "sfx", ClipLoad.DecompressOnLoad)]
        [TestCase("Assets/Resources/Audio/sfx/sfx_call_ring.wav", "sfx", ClipLoad.DecompressOnLoad)]
        public void GroupsAndLoadTypes(string path, string group, ClipLoad load)
        {
            Assert.IsTrue(ImportPolicy.TryFor(path, out var x));
            Assert.AreEqual(group, x.Group);
            Assert.AreEqual(load, x.Load);
            Assert.That(x.Quality, Is.InRange(0.3f, 0.7f));
        }

        [Test]
        public void ShortClipsAreResampledWhereInaudibleAndNeverDownmixed()
        {
            ImportPolicy.TryFor("Assets/Resources/Audio/babble/babble_vc_a.wav", out var babble);
            Assert.IsFalse(babble.ForceMono, "authored mono; Unity's downmix would peak-normalise and undo equal-RMS syllables");
            Assert.AreEqual(24000, babble.SampleRate);
            ImportPolicy.TryFor("Assets/Resources/Audio/sfx/foot_grass_01.wav", out var foot);
            Assert.AreEqual(32000, foot.SampleRate);
            ImportPolicy.TryFor("Assets/Resources/Audio/sfx/sfx_moon_horn.wav", out var stinger);
            Assert.IsFalse(stinger.ForceMono, "stingers are stereo");
            ImportPolicy.TryFor("Assets/Resources/Audio/music/licensed/x.ogg", out var music);
            Assert.IsFalse(music.Preload);
            Assert.IsTrue(music.LoadInBackground);
        }

        [TestCase("Assets/Art/foo.wav")]
        [TestCase("Assets/Resources/Audio/sfx-map.json")]
        [TestCase("Assets/Resources/Audio/Music-Credits.txt")]
        [TestCase(null)]
        public void IgnoresEverythingElse(string path) => Assert.IsFalse(ImportPolicy.TryFor(path, out _));
    }

    internal sealed class FloatTolerance : System.Collections.IComparer
    {
        public int Compare(object x, object y) => System.Math.Abs((float)x - (float)y) < 1e-4f ? 0 : 1;
    }
}
