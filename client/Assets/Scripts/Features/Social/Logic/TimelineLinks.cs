using System;
using UnityEngine;

namespace CTLife.SocialFeatures
{
    /// <summary>What a coin action card shows (the market's live numbers and the Factory's face for player coins).</summary>
    public sealed class CoinCardInfo
    {
        public string CoinId, Ticker, Name, Stage;
        public long McapCents;
        public int Change5mBps;
        public Texture2D Art;
        public bool Rugged, Player;
    }

    /// <summary>
    /// The Timeline's seams to the market (Money feature, which Social can't reference): set by the app at start-up.
    /// Opening a coin and travelling to a venue go through <see cref="CTLife.Core.VenueLinks"/>.
    /// </summary>
    public static class TimelineLinks
    {
        /// <summary>A live coin id for a ticker (no '$'), or null.</summary>
        public static Func<string, string> CoinIdByTicker;
        /// <summary>The card facts for a coin id, or null when the market hasn't seen it.</summary>
        public static Func<string, CoinCardInfo> Coin;
    }
}
