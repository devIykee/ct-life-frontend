using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// "Take a parked car" (Feel lane F3): World W2 fills street bays with decorative parked cars (<c>CTLife.World.ParkedCar</c>:
    /// model, vehicleClass, segmentId, the parked pose; <c>Hide(bool)</c> removes the decor's visual and collider). When the
    /// player comes near one, the decor hides and a drivable car of its class takes its exact pose, so walking up and
    /// getting in works like any other car. Walk away from an untouched one and the decor comes back; drive it off and the
    /// bay stays empty. The swap happens out of view where it can (up to 22 m away), otherwise close up (9 m).
    /// ParkedCar lives on World's branch until W2 reaches dev, so it is bound by name here; with W2 merged this becomes
    /// direct calls (this file only).
    /// </summary>
    public sealed class ParkedCars
    {
        public const float SwapFar = 22f, SwapNear = 9f, RestoreBeyond = 40f;

        private sealed class Swap { public Component decor; public Vehicle car; public Vector3 pos; public Quaternion rot; }

        private readonly List<Swap> _swaps = new List<Swap>();
        private readonly HashSet<Component> _taken = new HashSet<Component>();   // driven away: the bay stays empty
        private float _scanAt;
        private bool _bound;
        private FieldInfo _all, _class, _model;
        private MethodInfo _hide;
        private readonly Plane[] _planes = new Plane[6];

        public int Active => _swaps.Count;

        private bool Bind()
        {
            if (_bound) return _all != null;
            _bound = true;
            var t = typeof(VehicleSpawn).Assembly.GetType("CTLife.World.ParkedCar");
            if (t == null) return false;
            _all = t.GetField("All", BindingFlags.Public | BindingFlags.Static);
            _class = t.GetField("vehicleClass");
            _model = t.GetField("model");
            _hide = t.GetMethod("Hide", new[] { typeof(bool) });
            if (_all == null || _hide == null) _all = null;
            return _all != null;
        }

        /// <summary>The decor cars streamed in now (World W2), plus any a test registered.</summary>
        private IEnumerable<Component> Decor()
        {
            if (Bind() && _all.GetValue(null) is IList list)
                for (int i = 0; i < list.Count; i++) if (list[i] is Component c && c != null) yield return c;
            foreach (var c in Extra) if (c != null) yield return c;
        }

        /// <summary>Decor stand-ins for captures and tests (anything with a <c>Hide(bool)</c> method and a vehicleClass field).</summary>
        public static readonly List<Component> Extra = new List<Component>();

        private static VehicleClass ClassOf(Component decor, FieldInfo f)
        {
            var fi = f ?? decor.GetType().GetField("vehicleClass");
            if (fi != null && fi.GetValue(decor) is VehicleClass c) return c;
            return VehicleClass.Compact;
        }

        /// <summary>The drivable that stands in for a decor car: its kit model by name when we have it, else by class.</summary>
        public static string SpecFor(VehicleClass c, string model, int pick)
        {
            if (!string.IsNullOrEmpty(model))
                foreach (var s in VehicleCatalog.Specs.Values)
                    if (!VehicleCatalog.Flies(s.kind) && model.IndexOf(s.id, StringComparison.OrdinalIgnoreCase) >= 0) return s.id;
            if ((c & VehicleClass.Roadstar) != 0 && c != VehicleClass.Any) return "roadstar";
            if (c == VehicleClass.Moped) return "scooter";
            var options = new List<string>(3);
            if ((c & VehicleClass.Compact) != 0) options.Add("compact");
            if ((c & VehicleClass.Suv) != 0) options.Add("suv");
            if ((c & VehicleClass.Sports) != 0) options.Add("sports");
            if (options.Count == 0) options.Add((c & VehicleClass.Moped) != 0 ? "scooter" : "compact");
            return options[Mathf.Abs(pick) % options.Count];
        }

        private void Hide(Component decor, bool hidden)
        {
            var m = _hide != null && decor.GetType() == _hide.DeclaringType ? _hide : decor.GetType().GetMethod("Hide", new[] { typeof(bool) });
            m?.Invoke(decor, new object[] { hidden });
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
                if (moved) { _taken.Add(s.decor); _swaps.RemoveAt(i); continue; }
                if ((s.pos - player).sqrMagnitude > RestoreBeyond * RestoreBeyond)
                {
                    sys.Vehicles.Remove(s.car);
                    UnityEngine.Object.Destroy(s.car.gameObject);
                    Hide(s.decor, false);
                    _swaps.RemoveAt(i);
                }
            }
            // to drivable: decor near the player
            int pick = Mathf.FloorToInt(Time.time * 7f);
            foreach (var decor in Decor())
            {
                if (_taken.Contains(decor)) continue;
                var p = decor.transform.position;
                float d2 = (p - player).sqrMagnitude;
                if (d2 > SwapFar * SwapFar) continue;
                bool seen = cam != null && GeometryUtility.TestPlanesAABB(_planes, new Bounds(p + Vector3.up, new Vector3(4.5f, 2f, 4.5f)));
                if (seen && d2 > SwapNear * SwapNear) continue;
                bool done = false;
                foreach (var s in _swaps) if (s.decor == decor) { done = true; break; }
                if (done || !VehicleWorld.IsReady(p)) continue;
                string model = _model != null && _model.DeclaringType == decor.GetType() ? _model.GetValue(decor) as string : decor.GetType().GetField("model")?.GetValue(decor) as string;
                var id = SpecFor(ClassOf(decor, _class != null && _class.DeclaringType == decor.GetType() ? _class : null), model, pick++);
                Hide(decor, true);
                var car = sys.Spawn(id, p + Vector3.up * 0.05f, decor.transform.rotation);
                if (car == null) { Hide(decor, false); continue; }
                _swaps.Add(new Swap { decor = decor, car = car, pos = car.transform.position, rot = car.transform.rotation });
            }
        }

        /// <summary>Leaving the island: every stand-in goes, every decor comes back.</summary>
        public void Clear(VehicleSystem sys)
        {
            foreach (var s in _swaps)
            {
                if (s.car != null) { sys.Vehicles.Remove(s.car); UnityEngine.Object.Destroy(s.car.gameObject); }
                if (s.decor != null) Hide(s.decor, false);
            }
            _swaps.Clear();
            _taken.Clear();
        }
    }
}
