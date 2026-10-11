using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.Rest.Model;
using Newtonsoft.Json.Linq;

namespace CTLife.UI
{
    public enum StatLevel { Ok, Low, Critical }

    /// <summary>One top-bar status: what it shows, how worried to look, what the explainer says and where it leads.</summary>
    public sealed class HomeStat
    {
        public string id, icon, badgeTone, captionKey, value;
        public float fill = -1f;
        public StatLevel level;
        /// <summary>Explainer copy (Strings key + already formatted body) and the existing lobby route it offers.</summary>
        public string explainTitleKey, explainBody, actionKey, actionApp;
    }

    /// <summary>Server-provided progress towards the dream. Null when the server sends none (the contract has no field yet).</summary>
    public sealed class DreamProgress
    {
        public double current, target;
        /// <summary>"cents" (money dreams) or anything else for a plain count.</summary>
        public string unit;
        public float Fraction => target > 0 ? (float)Math.Max(0, Math.Min(1, current / target)) : 0f;

        public string Text() => unit == "cents"
            ? Strings.F("home.dream.of", LotteryModel.CashWhole((long)current), LotteryModel.CashWhole((long)target))
            : Strings.F("home.dream.of", ((long)current).ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                ((long)target).ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Everything the home screen shows, derived from /me, the registries and config, plus the city room's mood.</summary>
    public sealed class HomeState
    {
        public bool HasProfile;
        public string Alias, TribeId, TribeName, DreamId, DreamName, HomeName;
        public DreamProgress Dream;
        public readonly List<HomeStat> Stats = new List<HomeStat>();
        public long DebtCents, WeeklyDueCents = -1;
        public long? DebtDueAtMs;
        public int? MoodBps;
        public string GreetingKey, SubKey;
        public string ReactionKey;
        public string[] ReactionEmotes = Array.Empty<string>();
        public bool LowEnergy, LowSanity;
    }

    /// <summary>
    /// Pure rules for the home screen (EditMode tests: HomeUiTests). Values come only from the server: /me (Cash, debt,
    /// needs, character, home), the registries (tribe, dream and home names), config (`needs.warnings`, `lottery.debtRepayment`)
    /// and the city room's Market Mood (bps of the real SOL 24h change). Nothing is invented: the dream bar only appears when
    /// the server sends progress (`flags.dreamProgress {current, target}`, proposed in backend feedback #94).
    /// </summary>
    public static class HomeModel
    {
        /// <summary>Market Mood beyond ±3% reads as a big day (the HUD shows the same number).</summary>
        public const int MoodSwingBps = 300;

        /// <param name="weeklyDueCents">GET /me debtWeeklyDueCents (server truth; overrides the lottery config's figure).</param>
        /// <param name="dueAtMs">GET /me debtDueAt (the next Sunday Unlock).</param>
        public static HomeState Build(Me me, Registries reg, JToken needsConfig, JToken lotteryConfig, int? moodBps, bool firstArrival,
            long? weeklyDueCents = null, long? dueAtMs = null)
        {
            var s = new HomeState { MoodBps = moodBps };
            s.HasProfile = me != null;
            var (low, critical) = NeedThresholds(needsConfig);
            if (me != null)
            {
                s.Alias = me.Character?.Alias;
                s.TribeId = me.Character?.TribeId;
                s.TribeName = reg?.TribeOrder.FirstOrDefault(t => t.id == s.TribeId)?.name;
                s.DreamId = me.Character?.DreamId;
                s.DreamName = reg?.Dreams.FirstOrDefault(d => d.id == s.DreamId)?.name;
                s.HomeName = reg?.Homes.FirstOrDefault(h => h.id == me.HomeId)?.name;
                s.Dream = ReadDreamProgress(me);
                s.DebtCents = Math.Max(0, me.DebtCents);
                s.WeeklyDueCents = weeklyDueCents ?? WeeklyDue(lotteryConfig);
                s.DebtDueAtMs = dueAtMs;

                s.Stats.Add(new HomeStat
                {
                    id = "cash", icon = "cash", badgeTone = "gold", captionKey = "home.cash", value = LotteryModel.CashWhole(me.CashCents),
                    explainTitleKey = "home.explain.cash", explainBody = Strings.T("home.explain.cashBody"),
                    actionKey = "home.action.bank", actionApp = "bank",
                });
                if (s.DebtCents > 0)
                {
                    var body = Strings.F("home.explain.debtBody", LotteryModel.CashWhole(s.DebtCents));
                    if (s.WeeklyDueCents > 0 && dueAtMs.HasValue)
                        body += " " + Strings.F("home.explain.debtDue", LotteryModel.CashWhole(s.WeeklyDueCents), DebtDue.When(dueAtMs.Value, ServerClock.NowMs));
                    else if (s.WeeklyDueCents > 0) body += " " + Strings.F("home.explain.debtWeekly", LotteryModel.CashWhole(s.WeeklyDueCents));
                    s.Stats.Add(new HomeStat
                    {
                        id = "debt", icon = "bank", badgeTone = "orange", captionKey = "home.debt", value = LotteryModel.CashWhole(s.DebtCents),
                        explainTitleKey = "home.explain.debt", explainBody = body, actionKey = "home.action.repay", actionApp = "repay",
                    });
                }
                if (me.Needs != null)
                {
                    int energy = Clamp(me.Needs.Energy), sanity = Clamp(me.Needs.Sanity);
                    var e = Level(energy, low, critical);
                    var z = Level(sanity, low, critical);
                    s.LowEnergy = e != StatLevel.Ok;
                    s.LowSanity = z != StatLevel.Ok;
                    s.Stats.Add(new HomeStat
                    {
                        id = "energy", icon = "energy", badgeTone = "green", captionKey = "home.energy", value = energy.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        fill = energy / 100f, level = e, explainTitleKey = "home.explain.energy",
                        explainBody = Strings.F("home.explain.energyBody", energy), actionKey = "home.action.home", actionApp = "home",
                    });
                    s.Stats.Add(new HomeStat
                    {
                        id = "sanity", icon = "pulse", badgeTone = "purple", captionKey = "home.sanity", value = sanity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        fill = sanity / 100f, level = z, explainTitleKey = "home.explain.sanity",
                        explainBody = Strings.F("home.explain.sanityBody", sanity), actionKey = "home.action.home", actionApp = "home",
                    });
                }
            }

            s.GreetingKey = firstArrival ? "home.greet.first" : "home.greet";
            s.SubKey = firstArrival ? "home.sub.first" : MoodLineKey(moodBps);
            (s.ReactionKey, s.ReactionEmotes) = Reaction(s, firstArrival);
            return s;
        }

        /// <summary>The line under the greeting for a returning player, from Market Mood; a neutral line before the city answers.</summary>
        public static string MoodLineKey(int? bps)
        {
            if (!bps.HasValue) return "home.sub.noMood";
            if (bps.Value >= MoodSwingBps) return "home.sub.up";
            if (bps.Value <= -MoodSwingBps) return "home.sub.down";
            return "home.sub.flat";
        }

        /// <summary>What the character says and does on arrival, most urgent first (needs, then first day, then the market).</summary>
        public static (string key, string[] emotes) Reaction(HomeState s, bool firstArrival)
        {
            if (s.LowEnergy) return ("home.say.tired", new[] { "react_sad", "emote_facepalm" });
            if (s.LowSanity) return ("home.say.stressed", new[] { "emote_head_shake", "emote_facepalm" });
            if (firstArrival) return ("home.say.first", new[] { "emote_wave" });
            if (s.MoodBps >= MoodSwingBps) return ("home.say.up", new[] { "emote_moon_jump", "emote_wave" });
            if (s.MoodBps <= -MoodSwingBps) return ("home.say.down", new[] { "emote_facepalm", "emote_wave" });
            return ("home.say.back", new[] { "emote_wave" });
        }

        public static StatLevel Level(int value, int low, int critical) =>
            value <= critical ? StatLevel.Critical : value <= low ? StatLevel.Low : StatLevel.Ok;

        /// <summary>`needs.warnings {low, critical}` when the server sends it (FE proposal), else the HUD's 25 / 10.</summary>
        public static (int low, int critical) NeedThresholds(JToken needsConfig)
        {
            int low = 25, critical = 10;
            if (needsConfig?["warnings"] is JObject w)
            {
                if (w["low"] != null && w["low"].Type == JTokenType.Integer) low = w["low"].Value<int>();
                if (w["critical"] != null && w["critical"].Type == JTokenType.Integer) critical = w["critical"].Value<int>();
            }
            if (critical > low) critical = low;
            return (low, critical);
        }

        /// <summary>`lottery.debtRepayment.weeklyDueCents` (or the exit outcome's own), -1 when the config has none.</summary>
        public static long WeeklyDue(JToken lotteryConfig)
        {
            var v = lotteryConfig?["debtRepayment"]?["weeklyDueCents"] ?? lotteryConfig?["outcomes"]?["exit_liquidity"]?["weeklyDueCents"];
            return v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) && v.Value<long>() > 0 ? v.Value<long>() : -1;
        }

        /// <summary>`flags.dreamProgress {current, target, unit?}` (numbers, target &gt; 0; unit "cents" for money), else null.</summary>
        public static DreamProgress ReadDreamProgress(Me me)
        {
            if (me?.Flags == null || !me.Flags.TryGetValue("dreamProgress", out var raw) || raw == null) return null;
            var o = raw as JObject ?? (raw is string text ? TryParse(text) : null);
            if (o == null) return null;
            if (!IsNumber(o["current"]) || !IsNumber(o["target"])) return null;
            double target = o["target"].Value<double>(), current = o["current"].Value<double>();
            return target > 0 && current >= 0 ? new DreamProgress { current = current, target = target, unit = (string)o["unit"] } : null;
        }

        private static bool IsNumber(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float);

        private static JObject TryParse(string text)
        {
            try { return JObject.Parse(text); }
            catch (Newtonsoft.Json.JsonException) { return null; }
        }

        private static int Clamp(int v) => v < 0 ? 0 : v > 100 ? 100 : v;
    }
}
