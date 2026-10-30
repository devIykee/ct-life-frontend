using CTLife.Core;
using System.Collections.Generic;

namespace CTLife.UI
{
    /// <summary>Wallet detail copy, separate from the existing game-Cash Bank app.</summary>
    public static class WalletStrings
    {
        private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
        {
            ["title"] = "Wallet",
            ["holdings"] = "Your tokens",
            ["network"] = "Solana",
            ["mainnet"] = "Solana mainnet",
            ["devnet"] = "Solana devnet",
            ["refresh"] = "Refresh balances",
            ["refreshing"] = "Updating balances…",
            ["observed"] = "On-chain token balances",
            ["disconnected"] = "Sign in with a wallet to see its token balances here.",
            ["unavailable"] = "Balances are unavailable. Try refreshing in a moment.",
            ["partial"] = "Some balances couldn't be updated.",
            ["loading"] = "Loading…",
            ["unknown"] = "Unavailable",
            ["stale"] = "Last known balance · refresh to update",
            ["cashNote"] = "Game Cash lives in Bank. These are your wallet's token balances.",
            ["name.SOL"] = "Solana",
            ["name.USDC"] = "USD Coin",
            ["name.SKR"] = "Seeker",
            ["name.ORE"] = "Ore",
            ["copy"] = "Copy address",
            ["copied"] = "Wallet address copied",
            ["receive"] = "Receive",
            ["topUp"] = "Top up",
            ["trade"] = "Trade",
            ["refreshShort"] = "Refresh",
            ["inGame"] = "In CT Life",
            ["cashTicker"] = "Spend anywhere in the city",
        };
        public static string T(string key) => English.TryGetValue(key, out var value) ? NameTable.Apply(value) : key;
    }
}
