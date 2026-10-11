using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// One shipped venue or private-home diorama: `Resources/World/VenueContent/{venue|home}_{id}.asset`.
    /// Loading one entry loads only that venue's prefab, meshes, textures and lightmaps.
    /// Generated from the authoring registry (`Assets/Venues/VenueContentRegistry.asset`) by
    /// `CTLife.EditorTools.VenueContentCatalog.Sync` (menu CT Life → Venues → Sync runtime catalog; also before builds).
    /// </summary>
    public sealed class VenueContentEntry : ScriptableObject
    {
        public string contentId;
        public bool isHome;
        public VenueSceneRoot prefab;
    }

}
