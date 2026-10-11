using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// A decorative parked car (World lane): a kit model drawn by the island's instanced renderer in a street bay that is
    /// therefore not a <see cref="VehicleSpawn"/>. Feel may hide the decor (<see cref="Hide"/>) and put a drivable car of
    /// the same class at this pose. Forward = the parked heading; the box collider on this object stops other cars.
    /// </summary>
    public sealed class ParkedCar : MonoBehaviour
    {
        public static readonly List<ParkedCar> All = new List<ParkedCar>();

        public string model;
        public VehicleClass vehicleClass = VehicleClass.Compact;
        public string segmentId;
        [Tooltip("The cell's instance set that draws this car.")]
        public IslandInstances set;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        /// <summary>Removes the decor car (visual and collider) while a drivable one takes its place.</summary>
        public void Hide(bool hidden)
        {
            if (set != null) set.SetHidden(model, transform.position, hidden);
            foreach (var c in GetComponents<Collider>()) c.enabled = !hidden;
        }
    }
}
