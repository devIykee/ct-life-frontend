using CTLife.UI;
using CTLife.World;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    public sealed class WaypointTests
    {
        [TearDown] public void ClearPin() => Waypoint.Clear();

        [Test]
        public void SetMeasuresFlatDistanceAndClearNotifies()
        {
            int changes = 0;
            void Count() => changes++;
            Waypoint.Changed += Count;
            try
            {
                Waypoint.Set(new Vector2(30f, 40f), "Terminal Café");
                Assert.IsTrue(Waypoint.Active);
                Assert.AreEqual(50f, Waypoint.DistanceFrom(new Vector3(0f, 12f, 0f)), 0.001f);
                Waypoint.Clear();
                Assert.IsFalse(Waypoint.Active);
                Assert.IsTrue(float.IsPositiveInfinity(Waypoint.DistanceFrom(Vector3.zero)));
                Waypoint.Clear(); // already clear: no second notification
                Assert.AreEqual(2, changes);
            }
            finally { Waypoint.Changed -= Count; }
        }

        [Test]
        public void DistanceLabelsRoundToFiveMetresThenKilometres()
        {
            Assert.AreEqual("45 m", WaypointGuide.Format(46f));
            Assert.AreEqual("950 m", WaypointGuide.Format(948f));
            Assert.AreEqual("1.2 km", WaypointGuide.Format(1234f));
        }
    }
}
