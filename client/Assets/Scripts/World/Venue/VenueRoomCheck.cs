namespace CTLife.World
{
    /// <summary>Pure venue-room rules (unit-tested).</summary>
    public static class VenueRoomCheck
    {
        /// <summary>True when the joined room is the requested venue. An unknown room venue is not accepted.</summary>
        public static bool Matches(string requested, string actual) =>
            !string.IsNullOrEmpty(requested) && string.Equals(requested, actual, System.StringComparison.Ordinal);

        /// <summary>The schema default (0,0) means the server has not placed the player yet.</summary>
        public static bool IsUnplaced(double x, double z) => System.Math.Abs(x) < 1e-6 && System.Math.Abs(z) < 1e-6;
    }
}
