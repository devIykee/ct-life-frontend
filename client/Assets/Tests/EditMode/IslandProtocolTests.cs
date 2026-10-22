using System.Collections.Generic;
using System.Globalization;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.World.Vehicles;
using Newtonsoft.Json;
using NUnit.Framework;

namespace CTLife.Tests
{
    public class IslandProtocolTests
    {
        [Test] public void VehicleStateMapsFilteredSchemaWithAdditiveFields()
        {
            var fields = new Dictionary<string, object> { ["id"] = "park_compact_12_33", ["kind"] = "compact", ["driver"] = "u2",
                ["x"] = 125.5f, ["y"] = 27.0, ["z"] = -21f, ["yaw"] = 1.5, ["pitch"] = 0.02f, ["roll"] = -0.1,
                ["speed"] = 12.5, ["flags"] = 3, ["future"] = "ignored" };
            var state = new VehicleState();
            StateMapper.ApplyVehicle(k => fields.TryGetValue(k, out var v) ? v : null, state);
            Assert.AreEqual("u2", state.driver); Assert.AreEqual(125.5, state.x); Assert.AreEqual(-21, state.z);
            Assert.AreEqual(27, state.y); Assert.AreEqual(1.5, state.yaw); Assert.AreEqual(12.5, state.speed); Assert.AreEqual(3, state.flags);
            StateMapper.ApplyVehicle(k => k == "driver" ? "" : null, state);
            Assert.AreEqual("", state.driver); Assert.AreEqual(125.5, state.x);
        }
        [Test] public void PlayerKeepsIslandAndVenueCoordinatesSeparate()
        {
            var fields = new Dictionary<string, object> { ["x"] = 2, ["z"] = -3, ["rotY"] = 0.5, ["ix"] = 102, ["iz"] = 303, ["irotY"] = 1.7 };
            var p = new PlayerState(); StateMapper.ApplyPlayer(k => fields.TryGetValue(k, out var v) ? v : null, p);
            Assert.AreEqual(2, p.x); Assert.AreEqual(-3, p.z); Assert.AreEqual(102, p.ix); Assert.AreEqual(303, p.iz); Assert.AreEqual(1.7, p.irotY);
        }
        [Test] public void ParkingIdentityDoesNotDependOnDeviceCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); Assert.AreEqual("park_compact_102_-213", IslandProtocol.ParkedId("compact", 10.2, -21.3)); }
            finally { CultureInfo.CurrentCulture = previous; }
            Assert.AreEqual("park_compact_102_-213", IslandProtocol.ParkedId("compact", 10.2, -21.3));
        }
        [Test] public void PublicWireKindsExcludeUnimplementedEntitlements()
        {
            Assert.IsTrue(IslandProtocol.IsPublicVehicle(IslandProtocol.WireKind("scooter")));
            foreach (var kind in new[] { "heli", "drone", "roadstar", "unknown" }) Assert.IsFalse(IslandProtocol.IsPublicVehicle(kind));
            Assert.AreEqual("scooter", IslandProtocol.SpecKind("moped"));
            Assert.IsFalse(IslandProtocol.IsDistrict("countryside")); Assert.IsTrue(IslandProtocol.IsDistrict("mountain"));
        }
        [Test] public void ServerCorrectionAndClaimRejectionDecodeThePublishedPayload()
        {
            var correction = JsonConvert.DeserializeObject<CorrectedMessage>("{\"x\":12,\"z\":-4,\"island\":true}");
            Assert.IsTrue(correction.island); Assert.AreEqual(-4, correction.z);
            var rejection = JsonConvert.DeserializeObject<RejectedMessage>("{\"type\":\"vehicle_enter\",\"code\":\"VEHICLE_TAKEN\",\"id\":\"park_compact_12_33\"}");
            Assert.AreEqual("park_compact_12_33", rejection.id);
        }
    }
}
