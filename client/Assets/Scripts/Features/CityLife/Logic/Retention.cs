using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife
{
    /// <summary>What a daily chest pays (`ChestRewards`): Cash, and on streak day 7, 14 and 30 a cosmetic (null when owned).</summary>
    public sealed class ChestRewards
    {
        public long CashCents;
        public string CosmeticKey, CosmeticName;
        public bool HasCosmetic => !string.IsNullOrEmpty(CosmeticKey);

        public static ChestRewards Parse(JToken t)
        {
            var r = new ChestRewards { CashCents = CityWire.LongOr(t?["cashCents"], 0) };
            var c = t?["cosmetic"];
            if (!CityWire.IsNull(c)) { r.CosmeticKey = CityWire.Str(c["itemKey"]); r.CosmeticName = CityWire.Plain(c["name"], 48); }
            return r;
        }
    }

    /// <summary>
    /// The gm streak (`GET /v1/daily/status`, backend PR #9). The server owns the clock: days are UTC dates and
    /// <see cref="NextResetAtMs"/> is the next UTC midnight. <see cref="ChestDay"/> is today's chest while it is unclaimed,
    /// tomorrow's after.
    /// </summary>
    public class DailyStatus
    {
        public static readonly int[] Milestones = { 7, 14, 30 };

        public int StreakDay;
        public bool ClaimedToday;
        public long NextResetAtMs;
        public int Freezes, MaxFreezes = 2;
        public long FreezePriceCents = 5000;
        public int ChestDay;
        public ChestRewards Chest = new ChestRewards();

        /// <summary>The day the dashboard shows: today's chest day while it waits, the streak once it is claimed.</summary>
        public int ShownDay => ClaimedToday ? Math.Max(1, StreakDay) : Math.Max(1, ChestDay);
        public bool ChestReady => !ClaimedToday;
        public bool CanBuyFreeze => Freezes < MaxFreezes;

        public static bool IsMilestone(int day) => Array.IndexOf(Milestones, day) >= 0;

        /// <summary>The next milestone at or after <paramref name="day"/> (day 30 and later: every 30).</summary>
        public static int NextMilestone(int day)
        {
            foreach (var m in Milestones) if (day <= m) return m;
            return (day + 29) / 30 * 30;
        }

        public static DailyStatus Parse(JToken t) { var s = new DailyStatus(); s.Fill(t); return s; }

        protected void Fill(JToken t)
        {
            if (t == null || t.Type != JTokenType.Object) throw new FormatException("daily status");
            StreakDay = CityWire.IntOr(t["streakDay"], 0);
            ClaimedToday = CityWire.BoolOr(t["claimedToday"], false);
            NextResetAtMs = CityWire.TimeMs(t["nextResetAt"]) ?? 0;
            Freezes = CityWire.IntOr(t["freezes"], 0);
            MaxFreezes = CityWire.IntOr(t["maxFreezes"], 2);
            FreezePriceCents = CityWire.LongOr(t["freezePriceCents"], 5000);
            var chest = t["chest"];
            ChestDay = CityWire.IntOr(chest?["day"], Math.Max(1, StreakDay + (ClaimedToday ? 1 : 0)));
            Chest = ChestRewards.Parse(chest?["rewards"]);
        }

        /// <summary>Seconds until the UTC reset, never negative.</summary>
        public double SecondsToReset(double nowMs) => NextResetAtMs <= 0 ? 0 : Math.Max(0, (NextResetAtMs - nowMs) / 1000.0);

        public static string Countdown(double seconds)
        {
            var s = (long)Math.Max(0, Math.Floor(seconds));
            return $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";
        }
    }

    /// <summary>`POST /v1/daily/claim`: the status after the claim plus what this claim paid.</summary>
    public sealed class DailyClaim : DailyStatus
    {
        public bool AlreadyClaimed, Reset;
        public int ClaimedDay, FreezesUsed;
        public ChestRewards Claimed = new ChestRewards();

        public new static DailyClaim Parse(JToken t)
        {
            var c = new DailyClaim();
            c.Fill(t);
            c.AlreadyClaimed = CityWire.BoolOr(t["alreadyClaimed"], false);
            c.Reset = CityWire.BoolOr(t["reset"], false);
            c.FreezesUsed = CityWire.IntOr(t["freezesUsed"], 0);
            var claimed = t["claimed"];
            c.ClaimedDay = CityWire.IntOr(claimed?["day"], c.StreakDay);
            c.Claimed = ChestRewards.Parse(claimed?["rewards"]);
            return c;
        }
    }

    /// <summary>One quest (`Quest`): progress comes only from server events; the client never reports it.</summary>
    public sealed class Quest
    {
        public string Id, Title, Description, Verb, DeepLink;
        public int Target = 1, Progress, Step;
        public bool Completed, Claimed;
        public long RewardCashCents; public int RewardXp;
        public bool Weekly => Step > 0;
        public bool Claimable => Completed && !Claimed;
        public float Fraction => Target <= 0 ? 1f : Math.Min(1f, Progress / (float)Target);

        public static Quest Parse(JToken t)
        {
            var q = new Quest
            {
                Id = CityWire.ReqStr(t["id"], "id"),
                Title = CityWire.Plain(t["title"], 60),
                Description = CityWire.Plain(t["description"], 120),
                Verb = CityWire.Str(t["verb"]),
                Target = Math.Max(1, CityWire.IntOr(t["target"], 1)),
                Progress = Math.Max(0, CityWire.IntOr(t["progress"], 0)),
                Completed = CityWire.BoolOr(t["completed"], false),
                Claimed = CityWire.BoolOr(t["claimed"], false),
                DeepLink = CityWire.Str(t["deepLink"]),
                Step = CityWire.IntOr(t["step"], 0),
            };
            q.RewardCashCents = CityWire.LongOr(t["reward"]?["cashCents"], 0);
            q.RewardXp = CityWire.IntOr(t["reward"]?["seasonXp"], 0);
            if (q.Progress >= q.Target) q.Completed = true;
            return q;
        }
    }

    /// <summary>`GET /v1/quests`: today's 3 and this ISO week's chain of 5 (in `step` order).</summary>
    public sealed class QuestBoard
    {
        public string Date, Week;
        public long DailyResetsAtMs, WeeklyResetsAtMs;
        public readonly List<Quest> Daily = new List<Quest>(), Weekly = new List<Quest>();

        public static QuestBoard Parse(JToken t)
        {
            if (t == null || t.Type != JTokenType.Object) throw new FormatException("quests");
            var b = new QuestBoard();
            var d = t["daily"]; var w = t["weekly"];
            b.Date = CityWire.Str(d?["date"]); b.DailyResetsAtMs = CityWire.TimeMs(d?["resetsAt"]) ?? 0;
            b.Week = CityWire.Str(w?["week"]); b.WeeklyResetsAtMs = CityWire.TimeMs(w?["resetsAt"]) ?? 0;
            foreach (var q in CityWire.List(d, "quests")) b.Daily.Add(Quest.Parse(q));
            foreach (var q in CityWire.List(w, "quests")) b.Weekly.Add(Quest.Parse(q));
            b.Weekly.Sort((x, y) => x.Step.CompareTo(y.Step));
            return b;
        }

        public Quest Find(string id)
        {
            foreach (var q in Daily) if (q.Id == id) return q;
            foreach (var q in Weekly) if (q.Id == id) return q;
            return null;
        }

        public int DailyDone { get { int n = 0; foreach (var q in Daily) if (q.Completed) n++; return n; } }
        public int WeeklyDone { get { int n = 0; foreach (var q in Weekly) if (q.Completed) n++; return n; } }
        public int Claimable { get { int n = 0; foreach (var q in Daily) if (q.Claimable) n++; foreach (var q in Weekly) if (q.Claimable) n++; return n; } }

        /// <summary>The weekly chain is walked in order: the first unclaimed step is the one in play.</summary>
        public Quest CurrentWeekly
        {
            get { foreach (var q in Weekly) if (!q.Claimed) return q; return null; }
        }

        /// <summary>Applies a realtime `quest_progress`. Returns the quest it moved (null when unknown, e.g. a new day).</summary>
        public Quest Apply(QuestProgress p)
        {
            var q = Find(p.QuestId);
            if (q == null) return null;
            q.Progress = Math.Max(q.Progress, p.Progress);
            if (p.Target > 0) q.Target = p.Target;
            q.Completed = q.Completed || p.Completed || q.Progress >= q.Target;
            return q;
        }
    }

    /// <summary>Realtime `quest_progress` on the city room: `{questId, pool, progress, target, completed, at}`.</summary>
    public sealed class QuestProgress
    {
        public string QuestId, Pool;
        public int Progress, Target;
        public bool Completed;

        public static QuestProgress From(object payload)
        {
            if (!(payload is IDictionary<string, object> m)) return null;
            string S(string k) => m.TryGetValue(k, out var v) ? v as string : null;
            int I(string k) => m.TryGetValue(k, out var v) && v != null ? Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture) : 0;
            bool B(string k) => m.TryGetValue(k, out var v) && v is bool b && b;
            var id = S("questId");
            if (string.IsNullOrEmpty(id)) return null;
            try { return new QuestProgress { QuestId = id, Pool = S("pool"), Progress = I("progress"), Target = I("target"), Completed = B("completed") }; }
            catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// `ctlife://` links from quests (`deepLink`) and push payloads (`deep_link`), mapped to the Duo app that does the thing.
    /// Unknown links open nothing.
    /// </summary>
    public static class DeepLinks
    {
        public const string Scheme = "ctlife://";
        public const string QuestsApp = "quests";

        private static readonly Dictionary<string, string> Apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["quests"] = QuestsApp, ["daily"] = QuestsApp, ["streak"] = QuestsApp,
            ["jobs"] = "jobs", ["perps"] = "perps", ["predictions"] = "predict", ["alpha"] = "pulse", ["calls"] = "calls",
            ["timeline"] = "timeline", ["neighbours"] = "map", ["neighbors"] = "map", ["home"] = "home", ["daily-clue"] = "week",
            ["events"] = "week", ["shop"] = "shop", ["duoshop"] = "duoshop",
        };

        /// <summary>The app id for a link, or null. Accepts `ctlife://jobs`, `ctlife://jobs/...` and `ctlife://jobs?x=1`.</summary>
        public static string AppFor(string link)
        {
            if (string.IsNullOrWhiteSpace(link)) return null;
            var s = link.Trim();
            if (!s.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return null;
            s = s.Substring(Scheme.Length);
            int cut = s.IndexOfAny(new[] { '/', '?', '#' });
            if (cut >= 0) s = s.Substring(0, cut);
            return Apps.TryGetValue(s, out var app) ? app : null;
        }
    }
}
