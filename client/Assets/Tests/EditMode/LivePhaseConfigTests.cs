using CTLife.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Tests
{
    public sealed class LivePhaseConfigTests
    {
        [Test]
        public void ServerLivePhaseSurvivesNormalization()
        {
            var config = new ConfigStore();
            config.Apply(new JObject { ["version"] = "phase-test", ["livePhase"] = 6 }.ToString(), null, persist: false);
            Assert.AreEqual(6, Phases.Resolve(config.Root, 1));
        }

        [Test]
        public void MissingLivePhaseFallsBackToBuildDefault()
        {
            var config = new ConfigStore();
            config.Apply(new JObject { ["version"] = "phase-test" }.ToString(), null, persist: false);
            Assert.AreEqual(3, Phases.Resolve(config.Root, 3));
        }
    }
}
