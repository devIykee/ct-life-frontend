using System;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>Static description of one buildable venue diorama (public venue or private home type).</summary>
    public sealed class VenueSpec
    {
        /// <summary>Asset id and folder name under Assets/World/Venues (e.g. perp_pit, home_hacker_house).</summary>
        public string id;
        /// <summary>VenueSceneRoot.venueId: the contract venue id, or "home" for every private home.</summary>
        public string venueId;
        /// <summary>Key in docs/fe-contract-proposals/ids/spots.json.</summary>
        public string spotsKey;
        /// <summary>Home type id from contract/ids/homes.json (homes only).</summary>
        public string homeId;
        public string title;
        public string district;
        /// <summary>One-line CT identity, recorded in receipts and the progress log.</summary>
        public string identity;
        public Action<VB> build;

        // Gameplay camera (VenueSceneRoot fields). Defaults match the shipped Café/Studio framing.
        public float cameraDistance = 11.5f, cameraPitch = 31f, cameraYaw = -12f, cameraFov = 45f, cameraViewportY = 0.3f;

        // Look (VenueContentLighting); the bake sun uses the same values.
        public Color sunColor = new Color(1f, 0.9f, 0.78f);
        public float sunIntensity = 1.0f;
        public Vector3 sunAngles = new Vector3(45f, -35f, 0f);
        public Color sky = new Color(0.55f, 0.64f, 0.74f), equator = new Color(0.52f, 0.5f, 0.47f), ground = new Color(0.25f, 0.24f, 0.22f);
        public Color haze = new Color(0.7f, 0.74f, 0.76f);

        public bool IsHome => !string.IsNullOrEmpty(homeId);
        public string Folder => VB.VenuesRoot + "/" + id;
        public string PrefabPath => Folder + "/" + id + ".prefab";

        public VenueSpec Look(Color sun, float intensity, Color skyColor, Color equatorColor, Color groundColor)
        {
            sunColor = sun; sunIntensity = intensity; sky = skyColor; equator = equatorColor; ground = groundColor;
            return this;
        }

        public VenueSpec Camera(float distance, float pitch = 31f, float yaw = -12f, float fov = 45f, float viewportY = 0.3f)
        {
            cameraDistance = distance; cameraPitch = pitch; cameraYaw = yaw; cameraFov = fov; cameraViewportY = viewportY;
            return this;
        }
    }
}
