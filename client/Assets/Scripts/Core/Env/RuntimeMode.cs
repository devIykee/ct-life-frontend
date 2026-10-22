namespace CTLife.Core
{
    /// <summary>Authenticated development launch modes. Production always uses the live services.</summary>
    public static class RuntimeMode
    {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        public static bool IsOfflineDemo => LaunchArgs.Flag("islandDemo");
        public static bool IsOfflineIsland => IsOfflineDemo || LaunchArgs.Flag("islandOffline");
#else
        public const bool IsOfflineDemo = false;
        public const bool IsOfflineIsland = false;
#endif
    }
}
