using CTLife.EditorTools;
using NUnit.Framework;

namespace CTLife.Tests
{
    /// <summary>
    /// Regression for the 2026-10-08 T-pose: every character the game can spawn (registry archetypes/looks, defaults,
    /// all cast prefabs) owns exactly one root Animator with a valid Humanoid Avatar, every controller clip is Humanoid
    /// motion, and sampling idle/walk/sit with the shipped controller gives a real pose, not the bind (T) pose.
    /// </summary>
    public class CastAnimatableTests
    {
        [Test]
        public void EverySpawnableCharacterAnimates()
        {
            var report = CastValidation.Report(out int problems);
            Assert.AreEqual(0, problems, CastValidation.Summary(report));
            Assert.Greater(((Newtonsoft.Json.Linq.JObject)report["prefabs"]).Count, 0, "no character prefabs were checked");
        }
    }
}
