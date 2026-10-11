namespace CTLife.Core
{
    /// <summary>
    /// Values the server may list in GET /config `degraded` (backend contract 1.0.0-rc1): voice, llm, feed,
    /// npc_density, venue_cap, board_refresh, smart_money, dms, ore_reader, signup, rate_limit, login, price_feed,
    /// helius, ledger_freeze, login_ip_only. The UI disables the matching surfaces with a "temporarily unavailable"
    /// state. Server-only values (npc_density, venue_cap, rate_limit, login_ip_only) have no client surface.
    /// </summary>
    public static class Features
    {
        public const string Voice = "voice";
        public const string Llm = "llm";
        public const string Feed = "feed";
        public const string BoardRefresh = "board_refresh";
        public const string SmartMoney = "smart_money";
        public const string Dms = "dms";
        public const string OreReader = "ore_reader";
        public const string Signup = "signup";
        public const string Login = "login";
        public const string PriceFeed = "price_feed";
        public const string Helius = "helius";
        public const string LedgerFreeze = "ledger_freeze";

        /// <summary>Client-side composites: payments need Helius + quotes + an unfrozen ledger; markets need the ledger.</summary>
        public const string Payments = "payments";
        public const string Markets = "markets";

        public static bool IsAvailable(string feature)
        {
            if (string.IsNullOrEmpty(feature) || !Services.TryGet<ConfigStore>(out var c)) return true;
            switch (feature)
            {
                case Payments: return !c.IsDegraded(Helius) && !c.IsDegraded(PriceFeed) && !c.IsDegraded(LedgerFreeze) && !c.IsDegraded(Payments);
                case Markets: return !c.IsDegraded(LedgerFreeze) && !c.IsDegraded(Markets);
                default: return !c.IsDegraded(feature);
            }
        }
    }
}
