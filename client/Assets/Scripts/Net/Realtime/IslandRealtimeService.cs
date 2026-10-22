using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;

namespace CTLife.Net
{
    public sealed partial class RealtimeService
    {
        private readonly RoomJoinSlot<StreetChannel> _street = new RoomJoinSlot<StreetChannel>(c => c.IsJoined, c => c.Leave());
        private readonly RoomJoinSlot<VehicleChannel> _vehicles = new RoomJoinSlot<VehicleChannel>(c => c.IsJoined, c => c.Leave());
        public StreetChannel Street => _street.Current;
        public VehicleChannel Vehicles => _vehicles.Current;

        public Task<StreetChannel> JoinStreet(string district, CancellationToken owner)
        {
            if (RuntimeMode.IsOfflineIsland) throw new InvalidOperationException("Offline island has no street transport.");
            if (!IslandProtocol.IsDistrict(district)) throw new ArgumentException("Unknown street district.", nameof(district));
            string user = _session.UserId;
            return _street.Join(user + ":" + district, owner, () => IdentityCurrent(user), true, () => new StreetChannel(district),
                (candidate, attempt) => JoinWithBusyRetry(async () =>
                {
                    var options = await Options(attempt.Token); options["district"] = district;
                    return candidate.Join(Client, options, attempt);
                }, attempt));
        }
        public Task<VehicleChannel> JoinVehicles(CancellationToken owner)
        {
            if (RuntimeMode.IsOfflineIsland) throw new InvalidOperationException("Offline island has no vehicle transport.");
            string user = _session.UserId;
            return _vehicles.Join(user, owner, () => IdentityCurrent(user), true, () => new VehicleChannel(user),
                (candidate, attempt) => JoinWithBusyRetry(async () => candidate.Join(Client, await Options(attempt.Token), attempt), attempt));
        }
        public async Task LeaveStreet() { var old = _street.Detach(); if (old != null) await old.Leave(); }
        public async Task LeaveVehicles() { var old = _vehicles.Detach(); if (old != null) await old.Leave(); }
    }

    /// <summary>Published 7d2f069 wire names. Premium access remains unavailable until the backend enforces it.</summary>
    public static class IslandProtocol
    {
        public static bool IsDistrict(string id) => id == "timeline" || id == "trenches" || id == "dubai_marina" ||
            id == "boardwalk" || id == "airdrop_farm" || id == "builder_hills" || id == "breakpoint" ||
            id == "residences" || id == "mountain" || id == "islet";
        /// <summary>
        /// The one switch for the v3 realtime vehicle kinds (sedan, taxi, police, pickup, van, bike;
        /// docs/fe-contract-proposals/vehicle-kinds-v3.md). Off until the backend registry with those kinds is deployed: the
        /// v3 cars then travel as "compact" (same lease and speed cap) and a remote client shows a compact. Flip to true then.
        /// </summary>
        public static readonly bool WireV3Kinds = false;
        public static bool IsPublicVehicle(string kind) => kind == "compact" || kind == "suv" || kind == "sports" || kind == "moped" ||
            (WireV3Kinds && (kind == "sedan" || kind == "taxi" || kind == "police" || kind == "pickup" || kind == "van" || kind == "bike"));
        /// <summary>Local spec to wire kind. While the v3 kinds are off, the v3-only cars travel as "compact" and the bike as
        /// "moped" (same lease and speed caps).</summary>
        public static string WireKind(string spec) => spec == "scooter" ? "moped" :
            !WireV3Kinds && spec == "bike" ? "moped" :
            !WireV3Kinds && (spec == "sedan" || spec == "taxi" || spec == "police" || spec == "pickup" || spec == "van") ? "compact" : spec;
        /// <summary>Wire kind to a local spec (every v3 kind now has a client model).</summary>
        public static string SpecKind(string wire) => wire == "moped" ? "scooter" : wire;
        /// <summary>Same immutable parking pose gives every client the same vehicle id. Never includes user identity.</summary>
        public static string ParkedId(string kind, double x, double z) => "park_" + kind + "_" +
            Math.Round(x * 10, MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture) + "_" +
            Math.Round(z * 10, MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
