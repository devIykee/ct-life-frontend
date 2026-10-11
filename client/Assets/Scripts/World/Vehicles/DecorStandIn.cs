#if UNITY_EDITOR
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// Editor captures only: a stand-in for World W2's decorative <c>ParkedCar</c> (the members the swap reads: vehicleClass,
    /// model, Hide(bool)), so "take a parked car" is exercised before W2 reaches this branch. Not in builds.
    /// </summary>
    public sealed class DecorStandIn : MonoBehaviour
    {
        public VehicleClass vehicleClass = VehicleClass.Compact;
        public string model = "compact";
        public bool hidden;

        public void Hide(bool h)
        {
            hidden = h;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = !h;
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = !h;
        }
    }
}
#endif
