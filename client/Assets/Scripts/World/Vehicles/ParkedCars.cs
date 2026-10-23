using System;
using System.Collections.Generic;
using CTLife.Net;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// "Take a parked car" (Feel lane F3): World W2 fills street bays with decorative <see cref="ParkedCar"/>s (kit model,
    /// vehicle class, segment, the parked pose; <see cref="ParkedCar.Hide"/> removes the decor's visual and collider). When
    /// the player comes near one, the decor hides and a drivable car of its class takes its exact pose, so walking up and
    /// getting in works like any other car. Walk away from an untouched one and the decor comes back; drive it off and the
    /// bay stays empty. The swap happens out of view where it can (up to 22 m away), otherwise close up (9 m).
    /// </summary>
    public sealed class ParkedCars
    {
        public const float SwapFar = 22f, SwapNear = 9f, RestoreBeyond = 40f;

        private sealed class Swap { public ParkedCar decor; public Vehicle car; public Vector3 pos; public Quaternion rot; }

        private readonly List<Swap> _swaps = new List<Swap>();
        private readonly HashSet<ParkedCar> _taken = new HashSet<ParkedCar>();   // driven away: the bay stays empty
        private readonly List<ParkedCar> _scan = new List<ParkedCar>();
        private float _scanAt;
        private readonly Plane[] _planes = new Plane[6];

        public int Active => _swaps.Count;
        public bool IsSwapped(ParkedCar decor) => _swaps.Exists(s => s.decor == decor);

        /// <summary>The drivable that stands in for a decor car: its kit model by name when we have it, else by class.</summary>
        public static string SpecFor(VehicleClass c, string model, int pick)
        {
            // the police cruiser is parked about town until the wanted system spawns it (one car bay in ten)
            if ((c & VehicleClass.Cars) != 0 && ((pick >> 4) & int.MaxValue) % 10 == 3) return "police";
            if (!string.IsNullOrEmpty(model))
                foreach (var s in VehicleCatalog.Specs.Values)
                    if (!VehicleCatalog.Flies(s.kind) && s.id != "suv" && model.IndexOf(s.id, StringComparison.OrdinalIgnoreCase) >= 0) return s.id;
            if (!string.IsNullOrEmpty(model) && model.IndexOf("suv", StringComparison.OrdinalIgnoreCase) >= 0) return "sedan";
            if ((c & VehicleClass.Roadstar) != 0 && c != VehicleClass.Any) return "roadstar";
            if (c == VehicleClass.Moped) return "scooter";
            var options = new List<string>(3);
            if ((c & VehicleClass.Compact) != 0) { options.Add("compact"); options.Add("sedan"); }
            if ((c & VehicleClass.Suv) != 0) options.Add("sedan");     // the v3 sedan stands in until a v3 SUV lands
            if ((c & VehicleClass.Sports) != 0) options.Add("sports");
            if (options.Count == 0) options.Add((c & VehicleClass.Moped) != 0 ? "scooter" : "compact");
            return options[(pick & int.MaxValue) % options.Count];
        }

        public void Update(VehicleSystem sys, Vector3 player)
        {
            if (Time.time < _scanAt) return;
            _scanAt = Time.time + 0.5f;
            var cam = Camera.main;
            if (cam != null) GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            // back to decor: untouched stand-ins the player walked away from; driven-off ones leave the bay empty
            for (int i = _swaps.Count - 1; i >= 0; i--)
            {
                var s = _swaps[i];
                if (s.decor == null) { _swaps.RemoveAt(i); continue; }
                bool moved = s.car == null || (s.car.transform.position - s.pos).sqrMagnitude > 1.2f * 1.2f || Quaternion.Angle(s.car.transform.rotation, s.rot) > 8f;
                if (s.car != null && (s.car.Occupied || sys.Driving == s.car)) moved = true;
                if (s.car != null && VehicleNetworkBridge.IsRegisteredBay(s.car.GetComponent<VehicleNetworkIdentity>()?.Id)) moved = true;
                if (moved) { _taken.Add(s.decor); _swaps.RemoveAt(i); continue; }
                if ((s.pos - player).sqrMagnitude > RestoreBeyond * RestoreBeyond)
                {
                    sys.Vehicles.Remove(s.car);
                    UnityEngine.Object.Destroy(s.car.gameObject);
                    s.decor.Hide(false);
                    _swaps.RemoveAt(i);
                }
            }
            // to drivable: decor near the player
            _scan.Clear();
            _scan.AddRange(ParkedCar.All);
            foreach (var decor in _scan)
            {
                if (decor == null || _taken.Contains(decor)) continue;
                var p = decor.transform.position;
                float d2 = (p - player).sqrMagnitude;
                if (d2 > SwapFar * SwapFar) continue;
                bool seen = cam != null && GeometryUtility.TestPlanesAABB(_planes, new Bounds(p + Vector3.up, new Vector3(4.5f, 2f, 4.5f)));
                if (seen && d2 > SwapNear * SwapNear) continue;
                if (IsSwapped(decor) || !VehicleWorld.IsReady(p)) continue;
                int pick = unchecked(Mathf.RoundToInt(p.x * 10) * 73856093 ^ Mathf.RoundToInt(p.z * 10) * 19349663);
                var id = SpecFor(decor.vehicleClass, decor.model, pick);
                var wire = IslandProtocol.WireKind(id);
                var networkId = IslandProtocol.ParkedId(wire, p.x, p.z);
                if (VehicleNetworkBridge.IsRegisteredBay(networkId)) { decor.Hide(true); _taken.Add(decor); continue; }
                var spec = VehicleCatalog.Get(id);
                // A hovercar bay parks the hovercar for everyone: a non-owner sees it locked ("Buy in Duo Shop"), never enters it.
                if (spec == null || (!sys.CanUseKind(spec.kind) && spec.kind != VehicleKind.Roadstar) || !VehicleWorld.Dry(p)) continue;
                decor.Hide(true);
                var car = sys.Spawn(id, p + Vector3.up * 0.05f, decor.transform.rotation);
                if (car == null) { decor.Hide(false); continue; }
                // street colours for road cars; the hovercar keeps its own gunmetal
                if (spec.kind == VehicleKind.Car) VehiclePaint.Apply(car, VehiclePaint.Pick(((pick >> 8) & 0xFFFF) / 65535.0));
                var identity = car.gameObject.AddComponent<VehicleNetworkIdentity>();
                identity.Id = networkId; identity.Kind = wire;
                _swaps.Add(new Swap { decor = decor, car = car, pos = car.transform.position, rot = car.transform.rotation });
            }
        }

        /// <summary>Leaving the island: every stand-in goes, every decor comes back.</summary>
        public void Clear(VehicleSystem sys)
        {
            foreach (var s in _swaps)
            {
                if (s.car != null) { sys.Vehicles.Remove(s.car); UnityEngine.Object.Destroy(s.car.gameObject); }
                if (s.decor != null) s.decor.Hide(false);
            }
            _swaps.Clear();
            _taken.Clear();
        }
    }
}
