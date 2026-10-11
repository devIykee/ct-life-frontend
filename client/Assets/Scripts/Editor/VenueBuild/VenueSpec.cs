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

        /// <summary>
        /// Repack mode (VenueRepack): instead of a layout script, take this existing authored prefab, keep its anchors,
        /// approaches, colliders, NavMesh, lights and components, and re-emit its geometry merged per material into
        /// LOD-grouped cells with texture atlases. Live Screen_* surfaces stay separate renderers.
        /// </summary>
        public string repackSource;
        public float cellSize = 10f;
        /// <summary>Screen-relative height below which a repacked cell switches to its source LOD1 meshes.</summary>
        public float lod0Threshold = 0.22f;
        /// <summary>
        /// Repack: models whose bounds centre is farther than this from the walkable area (and that have a source LOD1)
        /// use their LOD1 mesh at every distance. The gameplay camera follows a player inside the walkable area, so
        /// these are always background. 0 = off.
        /// </summary>
        public float farLod1Distance;
        /// <summary>Repack: tiled-texture materials (facades, floors) as one venue-wide renderer each instead of per cell.</summary>
        public bool tiledVenueWide;
        /// <summary>Repack: cells split at the walkable centre (floor) instead of centred on it (round).</summary>
        public bool cellSplit;
        /// <summary>Repack: cell depth along z (0 = same as cellSize). Shallow rows let the far side of a room drop to LOD1.</summary>
        public float cellSizeZ;

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
