using CTLife.Core;

namespace CTLife.World
{
    /// <summary>
    /// Offline demo mode for filming (World lane, contract item 6; development builds only): launch extra `islandDemo=1`
    /// enters CT Island with no backend. The bundled config snapshot stands in for GET /config, a fixed read-only demo
    /// character stands in for /me (zero cash, nothing written), venue rooms are not joined, and each venue the player
    /// walks into gets its registry NPCs locally (idle at their homes). Parked cars and traffic are local already.
    /// No money, progress or social writes happen: every server call is skipped, not faked.
    /// </summary>
    public static class IslandDemo
    {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        public static bool Active => LaunchArgs.Flag("islandDemo");
#else
        public const bool Active = false;
#endif
    }
}
