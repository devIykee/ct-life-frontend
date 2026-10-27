using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CTLife.Realtime;
using Newtonsoft.Json.Linq;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// Timeline as the world feed (queue 36, approved brief ct-life-evidence/timeline-36/ui-refs/BRIEF.md, 2026-10-26):
    /// the city's auto event cards, action cards, the For you / City mixes and the HUD toast rule. Pure; tested.
    /// The backend is adding server-written event posts (`kind` launch / rug / liquidation / casino_win / exploit / mood with
    /// `meta`); until it serves them, <see cref="CityEventCards"/> builds the same rows from the realtime city events (lost on
    /// reload, which is why the server path was chosen).
    /// </summary>
    public static class EventKinds
    {
        public const string Launch = "launch", Rug = "rug", Liquidation = "liquidation", CasinoWin = "casino_win",
            Exploit = "exploit", Mood = "mood", Motherlode = "motherlode", Caller = "caller", Checkin = "checkin";

        /// <summary>Kinds that render as one-line city event cards (not people's posts).</summary>
        public static readonly HashSet<string> City = new HashSet<string> { Launch, Rug, Liquidation, CasinoWin, Exploit, Mood, Motherlode };

        /// <summary>The City tab's filter chips (All = null).</summary>
        public static readonly string[] Filters = { null, Launch, Rug, Liquidation, CasinoWin, Exploit };

        public static bool IsCity(string kind) => kind != null && City.Contains(kind);

        /// <summary>The place an event happened (for the action card's Go and the district toast rule).</summary>
        public static string VenueFor(string kind) => kind switch
        {
            Launch => "launchpad_tower",
            Rug => "launchpad_tower",
            Liquidation => "perp_pit",
            Motherlode => "ore_refinery",
            _ => null,
        };
    }

    /// <summary>The one action a post can carry (brief 3.1: coin beats place).</summary>
    public sealed class ActionCard
    {
        public enum CardKind { None, Coin, Place, Player }
        public CardKind Kind;
        public string CoinId, Ticker, VenueId, UserId;
        public static readonly ActionCard None = new ActionCard { Kind = CardKind.None };
    }

    public static class ActionCards
    {
        /// <param name="coinByTicker">Finds a live coin id by ticker (Pulse's market); null when unknown.</param>
        public static ActionCard For(FeedPostRow p, Func<string, string> coinByTicker)
        {
            if (p == null) return ActionCard.None;
            if (p.Sleuth != null) return new ActionCard { Kind = ActionCard.CardKind.Coin, CoinId = p.Sleuth.CoinId, Ticker = p.Sleuth.Ticker, VenueId = VenueOf(p.DeepLink) };
            var meta = p.Meta;
            var coinId = SocialWire.Str(meta?["coinId"]);
            var ticker = SocialWire.Plain(SocialWire.Str(meta?["ticker"]), 16);
            if (SocialWire.IsSafeId(coinId)) return new ActionCard { Kind = ActionCard.CardKind.Coin, CoinId = coinId, Ticker = Trim(ticker), VenueId = VenueOf(p.DeepLink) ?? EventKinds.VenueFor(p.Kind) };
            if (p.Kind == FeedKinds.Player || p.Kind == EventKinds.Caller)
            {
                var first = PostKinds.Tickers(p.Text).FirstOrDefault();
                if (first != null)
                {
                    var t = Trim(first);
                    var id = coinByTicker?.Invoke(t);
                    if (id != null) return new ActionCard { Kind = ActionCard.CardKind.Coin, CoinId = id, Ticker = t };
                }
            }
            var venue = VenueOf(p.DeepLink) ?? VenueOf(SocialWire.Str(meta?["venueId"]) is string v ? "venue:" + v : null) ?? EventKinds.VenueFor(p.Kind);
            if (venue != null) return new ActionCard { Kind = ActionCard.CardKind.Place, VenueId = venue };
            return ActionCard.None;
        }

        /// <summary>`venue:&lt;id&gt;` or `ctlife://venue/&lt;id&gt;` → id.</summary>
        public static string VenueOf(string link) => CTLife.Core.VenueLinks.TryParse(link, out var id) ? id : null;

        private static string Trim(string t) => string.IsNullOrEmpty(t) ? t : t.TrimStart('$').ToUpperInvariant();
    }

    /// <summary>The fake event-card source: realtime city events turned into feed rows (until the server writes them).</summary>
    public static class CityEventCards
    {
        public const string AuthorId = "city";

        public static FeedPostRow FromEvent(EventMessage e, double nowMs)
        {
            if (e == null || string.IsNullOrEmpty(e.eventId)) return null;
            string P(string k) => e.@params != null && e.@params.TryGetValue(k, out var v) ? v : null;
            string kind = e.eventId switch
            {
                "evt_coin_launch" => EventKinds.Launch,
                "evt_rug" => EventKinds.Rug,
                "evt_liquidation" => EventKinds.Liquidation,
                "evt_exploit" => EventKinds.Exploit,
                "evt_mood_change" => EventKinds.Mood,
                "evt_ore_motherlode" => EventKinds.Motherlode,
                "evt_casino_win" => EventKinds.CasinoWin,
                _ => null,
            };
            if (kind == null) return null;
            var meta = new JObject();
            foreach (var key in new[] { "coinId", "ticker", "creatorAlias", "launcherAlias", "alias", "lossCents", "moodBps", "direction", "target", "templateId", "playerLaunched", "multiplier", "game" })
                if (P(key) is string v && v.Length > 0 && v.Length <= 64) meta[key] = v;
            long at = double.IsNaN(e.at) || e.at <= 0 ? (long)nowMs : (long)e.at;
            string id = "evt:" + kind + ":" + (P("coinId") ?? "") + ":" + at.ToString(CultureInfo.InvariantCulture);
            return new FeedPostRow { PostId = id, UserId = AuthorId, Alias = "", Text = "", AtMs = at, Kind = kind, Meta = meta };
        }

        /// <summary>Server event rows and the realtime fakes describe the same thing: one per kind+coin within 2 minutes.</summary>
        public static bool Same(FeedPostRow a, FeedPostRow b) =>
            a != null && b != null && a.Kind == b.Kind && Math.Abs(a.AtMs - b.AtMs) < 120_000 &&
            string.Equals(SocialWire.Str(a.Meta?["coinId"]), SocialWire.Str(b.Meta?["coinId"]), StringComparison.Ordinal);
    }

    public static class TimelineMix
    {
        public const int EventEvery = 4;

        /// <summary>For you: people's posts newest first, with at most one city event card in every <see cref="EventEvery"/>
        /// rows (brief 3.1). Events are interleaved by time; extras wait in the City tab.</summary>
        public static List<FeedPostRow> ForYou(IReadOnlyList<FeedPostRow> posts, IReadOnlyList<FeedPostRow> events)
        {
            var people = posts.Where(p => !EventKinds.IsCity(p.Kind)).OrderByDescending(p => p.AtMs).ToList();
            var cards = posts.Where(p => EventKinds.IsCity(p.Kind)).Concat(events ?? Array.Empty<FeedPostRow>())
                .OrderByDescending(p => p.AtMs).ToList();
            var outList = new List<FeedPostRow>();
            int sinceEvent = EventEvery;   // an event may lead if it is the newest
            int pi = 0, ei = 0;
            while (pi < people.Count || ei < cards.Count)
            {
                bool eventTurn = ei < cards.Count && sinceEvent >= EventEvery - 1 &&
                                 (pi >= people.Count || cards[ei].AtMs >= people[pi].AtMs);
                if (eventTurn) { outList.Add(cards[ei++]); sinceEvent = 0; continue; }
                if (pi < people.Count) { outList.Add(people[pi++]); sinceEvent++; continue; }
                if (outList.Count > 0 && sinceEvent < EventEvery - 1) break;   // only events left: they live in City
                outList.Add(cards[ei++]); sinceEvent = 0;
            }
            return outList;
        }

        /// <summary>City: only event cards, newest first, optionally one kind. Duplicates (server + realtime) collapse.</summary>
        public static List<FeedPostRow> City(IReadOnlyList<FeedPostRow> posts, IReadOnlyList<FeedPostRow> events, string filter)
        {
            var server = posts.Where(p => EventKinds.IsCity(p.Kind)).ToList();
            var live = (events ?? Array.Empty<FeedPostRow>()).Where(e => !server.Any(s => CityEventCards.Same(s, e)));
            return server.Concat(live).Where(p => filter == null || p.Kind == filter).OrderByDescending(p => p.AtMs).ToList();
        }

        /// <summary>The coins the Sleuth exposed, so a launch or call post about one gets the attached verdict.</summary>
        public static Dictionary<string, FeedPostRow> Verdicts(IEnumerable<FeedPostRow> posts)
        {
            var d = new Dictionary<string, FeedPostRow>(StringComparer.Ordinal);
            foreach (var p in posts) if (p.Sleuth != null && !d.ContainsKey(p.Sleuth.CoinId)) d[p.Sleuth.CoinId] = p;
            return d;
        }
    }

    /// <summary>
    /// HUD toasts from the feed (approved: only posts about you, or big moments in your district, at most one city moment
    /// every 2 minutes). Pure: the caller passes my id, alias, my district and the clock.
    /// </summary>
    public sealed class TimelineToastPolicy
    {
        public const double CityGapMs = 120_000;
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private double _lastCityAt = double.NegativeInfinity;

        public enum Reason { None, AboutMe, CityMoment }

        /// <param name="blocked">The player's block list: a blocked author never surfaces on the HUD (security review).</param>
        public Reason Decide(FeedPostRow p, string myUserId, string myAlias, string myDistrict, Func<string, string> districtOfVenue, double nowMs,
            Func<string, bool> blocked = null)
        {
            if (p == null || string.IsNullOrEmpty(p.PostId) || !_seen.Add(p.PostId)) return Reason.None;
            if (blocked != null && !string.IsNullOrEmpty(p.UserId) && blocked(p.UserId)) return Reason.None;
            if (nowMs - p.AtMs > 10 * 60_000) return Reason.None;   // old rows on the first load are not news
            if (p.Sleuth != null && (SocialWire.SameId(p.Sleuth.LauncherUserId, myUserId) || !string.IsNullOrEmpty(myAlias) && p.Sleuth.LauncherAlias == myAlias))
                return Reason.AboutMe;
            if (p.Kind == FeedKinds.Player && !string.IsNullOrEmpty(myAlias) && !SocialWire.SameId(p.UserId, myUserId) &&
                (p.Text ?? "").IndexOf("@" + myAlias, StringComparison.OrdinalIgnoreCase) >= 0)
                return Reason.AboutMe;
            bool big = p.Kind == EventKinds.Rug || p.Kind == EventKinds.CasinoWin;
            if (!big || string.IsNullOrEmpty(myDistrict)) return Reason.None;
            var venue = EventKinds.VenueFor(p.Kind) ?? SocialWire.Str(p.Meta?["venueId"]);
            var district = venue != null ? districtOfVenue?.Invoke(venue) : null;
            if (district == null || district != myDistrict) return Reason.None;
            if (nowMs - _lastCityAt < CityGapMs) return Reason.None;
            _lastCityAt = nowMs;
            return Reason.CityMoment;
        }
    }
}
