using System.Collections.Generic;
using System.Globalization;
using CTLife.Core;
using CTLife.Rest.Model;
using Newtonsoft.Json.Linq;

namespace CTLife.UI
{
    /// <summary>
    /// Pure helpers for the lottery: outcome ids, odds read from the served lottery config (never hard-coded), the origin
    /// a home id implies, and whole-dollar Cash text. EditMode tests: OnboardingUiTests.
    /// </summary>
    public static class LotteryModel
    {
        public const string Insider = "insider", Exit = "exit_liquidity", Seeker = "seeker";

        public static string OutcomeId(PostLotteryRoll200Response.OutcomeEnum outcome) => outcome switch
        {
            PostLotteryRoll200Response.OutcomeEnum.Insider => Insider,
            PostLotteryRoll200Response.OutcomeEnum.Seeker => Seeker,
            _ => Exit,
        };

        /// <summary>The cards on the table: insider and exit liquidity, plus seeker for Seeker Genesis Token holders.</summary>
        public static List<string> Cards(bool isSeeker)
        {
            var cards = new List<string> { Insider, Exit };
            if (isSeeker) cards.Add(Seeker);
            return cards;
        }

        /// <summary>
        /// Whole-percent odds per shown card from lottery.json `weights` (served in /v1/config). Null, so nothing is shown,
        /// when the config is missing or any shown card has no positive weight (e.g. rc1 has seeker = 0): the odds are only
        /// displayed when they are the server's own numbers.
        /// </summary>
        public static Dictionary<string, int> Odds(JToken lotteryConfig, IList<string> shown)
        {
            if (!(lotteryConfig?["weights"] is JObject weights) || shown == null || shown.Count == 0) return null;
            double sum = 0;
            var w = new Dictionary<string, double>();
            foreach (var id in shown)
            {
                var token = weights[id];
                if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) return null;
                double v = token.Value<double>();
                if (v <= 0) return null;
                w[id] = v;
                sum += v;
            }
            var result = new Dictionary<string, int>();
            foreach (var kv in w) result[kv.Key] = (int)System.Math.Round(kv.Value / sum * 100.0, System.MidpointRounding.AwayFromZero);
            return result;
        }

        /// <summary>The outcome whose configured home is `homeId` (lottery.json `outcomes.*.homeId`), or null.</summary>
        public static string OutcomeForHome(JToken lotteryConfig, string homeId)
        {
            if (string.IsNullOrEmpty(homeId) || !(lotteryConfig?["outcomes"] is JObject outcomes)) return null;
            foreach (var kv in outcomes)
                if (kv.Value is JObject o && (string)o["homeId"] == homeId) return kv.Key;
            return null;
        }

        /// <summary>"$3,000" for whole dollars, else the full "$3,000.50" (Money.Cash).</summary>
        public static string CashWhole(long cents)
        {
            var text = Money.Cash(cents);
            return cents % 100 == 0 && text.EndsWith(".00", System.StringComparison.Ordinal) ? text.Substring(0, text.Length - 3) : text;
        }

        public static string NameKey(string outcomeId) => outcomeId == Insider ? "lottery.insider" : outcomeId == Seeker ? "lottery.seeker" : "lottery.exit";
    }

    /// <summary>Who the player turned out to be, for the creator's summary card.</summary>
    public sealed class OriginSummary
    {
        public string outcomeId;
        public long cashCents, debtCents;

        public static OriginSummary From(long cashCents, long debtCents, string homeId, JToken lotteryConfig)
        {
            var id = LotteryModel.OutcomeForHome(lotteryConfig, homeId);
            if (id == null && debtCents > 0) id = LotteryModel.Exit;
            return id == null ? null : new OriginSummary { outcomeId = id, cashCents = cashCents, debtCents = debtCents };
        }
    }

    /// <summary>
    /// Presentation copy and cues keyed by registry id. Names, perks, effects and lists always come from the registry;
    /// this only adds a voice line, an icon and an emote per known id, and falls back cleanly for an unknown one.
    /// </summary>
    public static class OnboardingCopy
    {
        /// <summary>
        /// Player-friendly wording for a trait's effect, used only while the registry text is exactly the one it was written
        /// from (ids/traits.json rc1). If the server's effect changes, its own text is shown instead, so the card never
        /// contradicts the server.
        /// </summary>
        private static readonly Dictionary<string, (string registry, string friendly)> TraitEffects = new Dictionary<string, (string, string)>
        {
            ["fast_talker"] = ("say cooldown 2s -> 1.5s", "Chat cooldown 1.5s, not 2s"),
            ["deep_pockets"] = ("rent penalty 0.5x", "Rent penalty halved"),
            ["iron_will"] = ("Sanity decay 0.8x", "Sanity drains 20% slower"),
            ["early_bird"] = ("shift pay +10% before noon", "+10% shift pay before noon"),
            ["sleuth"] = ("Sleuth NPC pays you for tips", "The Sleuth pays you for tips"),
            ["rug_veteran"] = ("REKT card visual variant", "A special REKT card look"),
        };

        public static string TraitEffect(SimpleDef trait)
        {
            if (trait == null) return null;
            var raw = trait.description;
            if (TraitEffects.TryGetValue(trait.id ?? "", out var copy) && string.Equals(raw, copy.registry, System.StringComparison.Ordinal))
                return copy.friendly;
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }

        // UiIcon kinds (UiIcon.cs maps them to the bundled Phosphor images).
        public static string TraitIcon(string id) => id switch
        {
            "fast_talker" => "chat", "deep_pockets" => "wallet", "iron_will" => "pulse", "early_bird" => "counter",
            "sleuth" => "face", "rug_veteran" => "league", _ => "emote",
        };

        public static string DreamIcon(string id) => id switch
        {
            "make_10k" => "cash", "top_rich_list" => "richlist", "rug_survivor" => "needs", "build_a_project" => "projects",
            "get_rugged_by_creator" => "rotate", "host_a_space" => "mic_stage", "stake_ore" => "ore", "call_a_top" => "perps",
            _ => "dream",
        };

        /// <summary>Icon disc when a painted tribe badge is not there yet.</summary>
        public static string TribeIcon(string id) => id switch
        {
            "trencher" => "perps", "perp_degen" => "projects", "prediction_shark" => "predict", "airdrop_farmer" => "cash",
            "caller" => "ads", "builder" => "terminal", "anon" => "face", _ => "contacts",
        };

        /// <summary>The character's line when this tribe is picked (first person, live CT terms: spec C2).</summary>
        public static string TribeLine(string id)
        {
            var key = "ob.tribe.say." + id;
            var line = Strings.T(key);
            return line == key ? Strings.T("ob.say.tribePick") : line;
        }

        /// <summary>
        /// Emotes to try for a tribe pick, best first: the registry's signature emote, then a stand-in that exists in the
        /// shipped AvatarController (some registry ids, e.g. emote_send_it_slam, have no state yet).
        /// </summary>
        public static string[] TribeEmotes(TribeDef tribe)
        {
            string fallback = tribe?.id switch
            {
                "trencher" => "emote_send_it", "perp_degen" => "emote_chest_thump", "prediction_shark" => "emote_coin_flip",
                "airdrop_farmer" => "emote_watering_can", "caller" => "emote_ring_light", "builder" => "emote_frantic_type",
                "anon" => "emote_hood_up", _ => "emote_thumbs_up",
            };
            return new[] { tribe?.signatureEmoteId, fallback, "emote_thumbs_up" };
        }

        /// <summary>A short random-free variant for repeated reactions (look changes), so captures are repeatable.</summary>
        public static string Variant(string keyPrefix, int index, int count) =>
            Strings.T(keyPrefix + "." + (((index % count) + count) % count).ToString(CultureInfo.InvariantCulture));
    }
}
