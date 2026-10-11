using NUnit.Framework;

namespace CTLife.Audio.Tests
{
    public class SurfaceTests
    {
        [TestCase("Floor_WoodPlanks_01", "wood")]
        [TestCase("pm_metal_grate", "metal_grate")]
        [TestCase("M_Neon Tile (Instance)", "neon_tile")]
        [TestCase("CarpetRed", "carpet")]
        [TestCase("Terrain", "grass")]
        [TestCase("Sidewalk_Concrete", "concrete")]
        [TestCase("BoardwalkDeck", "wood")]
        [TestCase("MarbleLobby", "neon_tile")]
        [TestCase("Material", null)]
        [TestCase("M_Floor_01", null)]
        [TestCase("Yoga_Mat", "carpet")]
        [TestCase("wood", "wood")]
        [TestCase("metal_grate", "metal_grate")]
        [TestCase("", null)]
        [TestCase(null, null)]
        public void NamesMapToSurfaces(string name, string expected) => Assert.AreEqual(expected, SurfaceResolver.FromName(name));

        [Test]
        public void PriorityTagThenPhysicThenMaterialThenObjectThenVenue()
        {
            Assert.AreEqual("carpet", SurfaceResolver.Resolve("carpet", "pm_metal", "M_Wood", "Grass", "concrete"));
            Assert.AreEqual("metal_grate", SurfaceResolver.Resolve(null, "pm_metal", "M_Wood", "Grass", "concrete"));
            Assert.AreEqual("wood", SurfaceResolver.Resolve(null, "Default", "M_Wood", "Grass", "concrete"));
            Assert.AreEqual("grass", SurfaceResolver.Resolve("bogus", null, "Lit", "GrassPatch", "concrete"));
            Assert.AreEqual("neon_tile", SurfaceResolver.Resolve(null, null, "Lit", "Floor", "neon_tile"));
            Assert.AreEqual("concrete", SurfaceResolver.Resolve(null, null, null, null, "lava"));
        }

        [Test]
        public void VenueDefaultsCoverEverySpecSurface()
        {
            Assert.AreEqual("wood", SurfaceResolver.VenueDefault("terminal_cafe"));
            Assert.AreEqual("metal_grate", SurfaceResolver.VenueDefault("perp_pit"));
            Assert.AreEqual("carpet", SurfaceResolver.VenueDefault("spaces_studio"));
            Assert.AreEqual("grass", SurfaceResolver.VenueDefault("airdrop_farm"));
            Assert.AreEqual("neon_tile", SurfaceResolver.VenueDefault("some_new_club", isClub: true));
            Assert.AreEqual("concrete", SurfaceResolver.VenueDefault(null));
            var used = new System.Collections.Generic.HashSet<string>(SurfaceResolver.VenueDefaults.Values);
            foreach (var s in SurfaceResolver.All) Assert.IsTrue(used.Contains(s), s + " is used by some venue");
        }

        [Test]
        public void TokensSplitCamelCaseAndSeparators()
        {
            CollectionAssert.AreEqual(new[] { "floor", "wood", "planks" }, SurfaceResolver.Tokens("Floor_WoodPlanks_01"));
            CollectionAssert.AreEqual(new[] { "material" }, SurfaceResolver.Tokens("Material"));
        }
    }

    public class CadenceTests
    {
        private static int Steps(FootstepCadence c, float speed, float seconds, float dt = 1f / 60f)
        {
            int n = 0;
            for (float t = 0; t < seconds; t += dt) if (c.AdvanceAtSpeed(speed * dt, dt, speed)) n++;
            return n;
        }

        [Test]
        public void CadenceRisesWithSpeed()
        {
            int slow = Steps(new FootstepCadence(), 1.1f, 10f);
            int walk = Steps(new FootstepCadence(), 2.2f, 10f);
            int run = Steps(new FootstepCadence(), 4.5f, 10f);
            Assert.That(slow, Is.LessThan(walk));
            Assert.That(walk, Is.LessThan(run));
            Assert.AreEqual(2.2f * 10f / StrideModel.WalkStride, walk, 1.5f, "walk ≈ 1.5 steps/s");
            Assert.AreEqual(4.5f * 10f / StrideModel.RunStride, run, 1.5f, "run ≈ 3 steps/s");
        }

        [Test]
        public void StrideInterpolatesBetweenGaits()
        {
            Assert.AreEqual(StrideModel.WalkStride, StrideModel.StrideFor(1f));
            Assert.AreEqual(StrideModel.RunStride, StrideModel.StrideFor(9f));
            float mid = StrideModel.StrideFor(3.35f);
            Assert.That(mid, Is.GreaterThan(StrideModel.WalkStride).And.LessThan(StrideModel.RunStride));
            Assert.AreEqual(0f, StrideModel.CadenceHz(0.1f));
            Assert.AreEqual(0f, StrideModel.StrideFor(float.NaN) - StrideModel.WalkStride);
        }

        [Test]
        public void FirstStepComesAfterHalfAStride()
        {
            var c = new FootstepCadence();
            Assert.IsFalse(c.AdvanceAtSpeed(0.7f, 0.3f, 2.2f));
            Assert.IsTrue(c.AdvanceAtSpeed(0.1f, 0.05f, 2.2f), "half stride 0.73 + 0.7 + 0.1 > 1.463");
        }

        [Test]
        public void WarpsPausesAndBadSamplesResetWithoutAStep()
        {
            var c = new FootstepCadence();
            Assert.IsFalse(c.AdvanceAtSpeed(30f, 0.016f, 2.2f), "teleport");
            Assert.IsFalse(c.AdvanceAtSpeed(0.03f, 0.016f, 0.05f), "standing");
            Assert.IsFalse(c.AdvanceAtSpeed(float.NaN, 0.016f, 2.2f));
            Assert.IsFalse(c.AdvanceAtSpeed(0.03f, 0.9f, 2.2f), "hitch longer than 0.5 s");
            Assert.IsFalse(c.Advance(0.03f, 0.016f, moving: false, running: false));
        }

        [Test]
        public void LegacyWalkRunFlagStillWorks()
        {
            var c = new FootstepCadence();
            int n = 0;
            for (int i = 0; i < 600; i++) if (c.Advance(2.2f / 60f, 1f / 60f, true, false)) n++;
            Assert.AreEqual(15, n, 1);
        }
    }

    public class FootstepBudgetTests
    {
        [Test]
        public void FarStepsAreSilent()
        {
            var b = new FootstepBudget();
            Assert.IsFalse(b.Request(1, 12.5f, 0).Play);
            Assert.IsFalse(b.Request(1, float.NaN, 0).Play);
            Assert.IsTrue(b.Request(1, 3f, 0).Play);
        }

        [Test]
        public void MidDistanceThinsToEverySecondStep()
        {
            var b = new FootstepBudget();
            int played = 0;
            for (int i = 0; i < 10; i++) if (b.Request(7, 8f, i * 1.0).Play) played++;
            Assert.AreEqual(5, played);
        }

        [Test]
        public void VoiceCapStealsTheFarthest()
        {
            var b = new FootstepBudget();
            for (int i = 0; i < FootstepBudget.MaxVoices; i++)
                Assert.IsTrue(b.Request(100 + i, 2f + i * 0.5f, 0.0).Play);
            Assert.AreEqual(FootstepBudget.MaxVoices, b.Sounding(0.01));
            var farther = b.Request(200, 5.9f, 0.01);
            Assert.IsFalse(farther.Play, "not nearer than anything sounding");
            var nearer = b.Request(201, 1f, 0.02);
            Assert.IsTrue(nearer.Play);
            Assert.AreEqual(FootstepBudget.MaxVoices - 1, nearer.Voice, "stole the farthest voice (4.5 m)");
        }

        [Test]
        public void StartsPerWindowAreCapped()
        {
            var b = new FootstepBudget();
            int played = 0;
            // 40 NPCs planting a foot within one frame, each nearer than the last, so voice stealing never blocks:
            // only the per-window start cap limits them.
            for (int i = 0; i < 40; i++)
            {
                var d = b.Request(i, 5f - i * 0.1f, 0.0 + i * 0.0001);
                if (d.Play) played++;
            }
            Assert.AreEqual(FootstepBudget.MaxStepsPerWindow, played);
            Assert.IsTrue(b.Request(999, 1f, 0.5).Play, "next window opens");
        }
    }
}
