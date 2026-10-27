using CTLife.Core;
using System.Collections.Generic;
using System.Globalization;

namespace CTLife.SocialFeatures
{
    /// <summary>Words for the world-feed Timeline (queue 36). Event cards are in our own words; player text is shown as typed.</summary>
    public static class TimelineText
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["tab.city"] = "City",
            ["compose.prompt"] = "What's happening on CT?",
            ["filter.all"] = "All", ["filter.launch"] = "Launches", ["filter.rug"] = "Rugs", ["filter.liquidation"] = "Liquidations",
            ["filter.casino_win"] = "Wins", ["filter.exploit"] = "Exploits",
            ["mood"] = "City mood {0}",
            ["quiet"] = "Quiet on the Timeline. Here's what the city's doing.",
            ["cityEmpty"] = "Nothing's happened in the city yet. Launches, rugs and liquidations show up here.",
            ["ev.launch"] = "{0} launched ${1}", ["ev.launchAnon"] = "${0} just launched",
            ["ev.rug"] = "${0} got rugged", ["ev.rugBy"] = "${0} got rugged by its dev, {1}",
            ["ev.liquidation"] = "{0} got liquidated on ${1}", ["ev.liquidationAnon"] = "Longs liquidated on Perp Pit",
            ["ev.exploit"] = "Exploit hit {0}", ["ev.exploitAnon"] = "An exploit hit the city",
            ["ev.motherlode"] = "Motherlode at the ORE Refinery",
            ["ev.casino_win"] = "{0} hit {1}x on {2} in the Rug Room", ["ev.casino_winAnon"] = "Someone hit big in the Rug Room",
            ["ev.city"] = "City",
            ["card.open"] = "Open", ["card.go"] = "Go", ["card.read"] = "Read",
            ["card.rugged"] = "RUGGED", ["card.dead"] = "Dead",
            ["card.mc"] = "MC {0}",
            ["sleuth.mast"] = "THE SLEUTH · exposé",
            ["sleuth.checked"] = "THE SLEUTH checked this coin",
            ["sleuth.sold"] = "SOLD {0}%", ["sleuth.heat"] = "Heat +{0}",
            ["sleuth.go"] = "Go to {0}", ["sleuth.open"] = "Open ${0}",
            ["npc"] = "NPC",
            ["checkin.share"] = "Show where I post from", ["checkin.shareHint"] = "Off by default. When on, a post shows the venue you're at (within 60 m of its door).",
            ["toast.sleuth"] = "The Sleuth posted about you: ${0}",
            ["toast.mention"] = "{0} mentioned you on the Timeline",
            ["toast.rug"] = "Rug nearby: ${0}",
            ["toast.win"] = "Big win nearby: {0}",
            ["route"] = "Route set: {0}",
            ["more"] = "Load more", ["holders"] = "{0} holders", ["boughtIn"] = "{0} bought in", ["card.profile"] = "Profile",
            ["call"] = "Call: entry {0} \u00b7 target {1}x \u00b7 stop -{2}%",
            ["checkin.at"] = "Check in at {0}", ["checkin.none"] = "Check in (walk into a venue first)", ["checkin.done"] = "Checked in at {0}.",
            ["checkinNowhere"] = "Walk into a venue to check in.", ["checkinFar"] = "Get closer to the venue's door to check in.",
            ["checkinSoon"] = "You checked in recently. Try again in a few minutes.",
        };

        public static string T(string key) => En.TryGetValue(key, out var v) ? NameTable.Apply(v) : key;
        public static string F(string key, params object[] a) => string.Format(CultureInfo.InvariantCulture, T(key), a);

        private static string M(FeedPostRow p, string key) => SocialWire.Plain(SocialWire.Str(p.Meta?[key]), 40);

        /// <summary>The one line an event card says.</summary>
        public static string Event(FeedPostRow p)
        {
            var t = M(p, "ticker")?.TrimStart('$');
            switch (p.Kind)
            {
                case EventKinds.Launch:
                    var who = M(p, "creatorAlias");
                    return !string.IsNullOrEmpty(who) && !string.IsNullOrEmpty(t) ? F("ev.launch", who, t) : F("ev.launchAnon", t ?? "?");
                case EventKinds.Rug:
                    var dev = M(p, "launcherAlias");
                    return !string.IsNullOrEmpty(dev) ? F("ev.rugBy", t ?? "?", dev) : F("ev.rug", t ?? "?");
                case EventKinds.Liquidation:
                    var a = M(p, "alias");
                    return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(t) ? F("ev.liquidation", a, t) : T("ev.liquidationAnon");
                case EventKinds.Exploit:
                    var target = M(p, "target");
                    return !string.IsNullOrEmpty(target) ? F("ev.exploit", target) : T("ev.exploitAnon");
                case EventKinds.Motherlode: return T("ev.motherlode");
                case EventKinds.CasinoWin:
                    var w = M(p, "alias"); var x = M(p, "multiplier"); var g = M(p, "game");
                    return !string.IsNullOrEmpty(w) && !string.IsNullOrEmpty(x) ? F("ev.casino_win", w, x, g ?? "a table") : T("ev.casino_winAnon");
                default: return string.IsNullOrEmpty(p.Text) ? T("ev.city") : p.Text;
            }
        }

        public static string Toast(FeedPostRow p, TimelineToastPolicy.Reason why)
        {
            if (why == TimelineToastPolicy.Reason.AboutMe)
                return p.Sleuth != null ? F("toast.sleuth", p.Sleuth.Ticker) : F("toast.mention", string.IsNullOrEmpty(p.Alias) ? "Someone" : p.Alias);
            if (why == TimelineToastPolicy.Reason.CityMoment)
                return p.Kind == EventKinds.Rug ? F("toast.rug", M(p, "ticker") ?? "?") : F("toast.win", Event(p));
            return null;
        }

        /// <summary>The icon for an event card (UiIcon names).</summary>
        public static string Icon(string kind) => kind switch
        {
            EventKinds.Launch => "projects",
            EventKinds.Rug => "heat",
            EventKinds.Liquidation => "perps",
            EventKinds.Exploit => "siren",
            EventKinds.Motherlode => "ore",
            EventKinds.CasinoWin => "dream",
            _ => "city",
        };
    }
}
