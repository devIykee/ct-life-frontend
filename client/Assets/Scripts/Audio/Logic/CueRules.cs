using System;
using System.Collections.Generic;
using System.Text;

namespace CTLife.Audio
{
    /// <summary>
    /// Every sound id the game can fire (contract/ids/sfx.json, FE-owned, plus client-only cues in sfx-map.json).
    /// Lanes that do not reference CTLife.Audio publish <c>Bus.Publish(new SfxSignal(SfxIds.X))</c> with the string.
    /// </summary>
    public static class SfxIds
    {
        // UI
        public const string UiTap = "sfx_ui_tap", UiToggleOn = "sfx_ui_toggle_on", UiToggleOff = "sfx_ui_toggle_off",
            UiError = "sfx_ui_error", UiSuccess = "sfx_ui_success", UiToast = "sfx_ui_toast", UiOpen = "sfx_ui_open", UiClose = "sfx_ui_close";
        // Trading
        public const string Buy = "sfx_buy", Sell = "sfx_sell", TakeProfit = "sfx_take_profit", NewCoin = "sfx_new_coin",
            WhaleHorn = "sfx_whale_horn", PnlCardFlip = "sfx_pnl_card_flip", CashIn = "sfx_cash_in";
        // Stingers (duck the music)
        public const string Liquidation = "sfx_liquidation", Rug = "sfx_rug", Graduation = "sfx_graduation",
            MoonHorn = "sfx_moon_horn", RankUp = "sfx_rank_up", SundayUnlock = "sfx_sunday_unlock",
            RektPhonk = "sfx_rekt_phonk";   // client-only: REKT clip replay / share (phonk-edit culture)
        // News wire (client-only)
        public const string NewsFlash = "sfx_news_flash";
        // Phone
        public const string DmPing = "sfx_dm_ping", Mention = "sfx_mention", CallRing = "sfx_call_ring",
            CallConnect = "sfx_call_connect", CallEnd = "sfx_call_end", SpaceStarted = "sfx_space_started", Gm = "sfx_gm";
        // Voice rooms
        public const string VrJoin = "sfx_vr_join", VrLeave = "sfx_vr_leave", VrRaiseHand = "sfx_vr_raise_hand", VrPromoted = "sfx_vr_promoted";
        // Onboarding (client-only, quote the main theme `mus_title`). "sfx_origin_" + outcome id works for every outcome:
        // insider, exit_liquidity, seeker (alias of insider).
        public const string OriginInsider = "sfx_origin_insider", OriginExitLiquidity = "sfx_origin_exit_liquidity",
            OriginSeeker = "sfx_origin_seeker", CreatorComplete = "sfx_creator_complete",
            UiNext = "sfx_ui_next", CardShuffle = "sfx_card_shuffle";
        // ORE Refinery
        public const string OreFurnace = "sfx_ore_furnace_whoosh", OreClink = "sfx_ore_clink";
    }

    /// <summary>
    /// Pure cue policy: id aliases, per-id/per-category cooldowns (dedupe when the UI and a bus subscription both
    /// react to one event), toast masking under richer cues, and event→cue rules (whale tape, gm, @mentions).
    /// </summary>
    public sealed class CueGate
    {
        /// <summary>Both spellings are registry ids (additions only, never renamed). Resolve to the audio file's id.</summary>
        public static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>
        {
            ["sfx_ui_toggle"] = SfxIds.UiToggleOn, ["sfx_pnl_flip"] = SfxIds.PnlCardFlip,
            ["sfx_sunday_bell"] = SfxIds.SundayUnlock, ["sfx_room_join"] = SfxIds.VrJoin,
            ["sfx_room_leave"] = SfxIds.VrLeave, ["sfx_raise_hand"] = SfxIds.VrRaiseHand,
            ["sfx_promoted"] = SfxIds.VrPromoted,
            [SfxIds.OriginSeeker] = SfxIds.OriginInsider,
        };

        public static string Canonical(string id) =>
            id != null && Aliases.TryGetValue(id, out var target) ? target : id;

        public static float CategoryCooldown(string category) => category switch
        {
            "ui" => 0.06f,
            "stinger" => 1.2f,
            "phone" => 0.25f,
            "voice_room" => 0.15f,
            "trading" => 0.05f,
            "ore" => 0.05f,
            "news" => 0.5f,
            _ => 0.03f,
        };

        /// <summary>Ids with their own minimum spacing (seconds), beyond the category's.</summary>
        public static readonly IReadOnlyDictionary<string, float> IdCooldowns = new Dictionary<string, float>
        {
            [SfxIds.WhaleHorn] = 20f,     // whale prints can arrive in bursts; one horn per burst
            [SfxIds.NewCoin] = 1.5f,      // evt_coin_launch card + market `coins` add are the same launch
            [SfxIds.Gm] = 12f,            // a room-wide gm wave is one chime, not a choir of pings
            [SfxIds.DmPing] = 0.6f,
            [SfxIds.Mention] = 1.0f,
            [SfxIds.CashIn] = 1.0f,
            [SfxIds.VrJoin] = 0.4f,
            [SfxIds.VrLeave] = 0.4f,
            [SfxIds.SpaceStarted] = 5f,
        };

        /// <summary>Categories that make a near-simultaneous `sfx_ui_toast` redundant (the toast tick is masked).</summary>
        public static bool IsRich(string category) =>
            category == "trading" || category == "stinger" || category == "phone" || category == "ore" || category == "voice_room" ||
            category == "news";

        public const float ToastMaskSeconds = 0.2f;

        private readonly Dictionary<string, double> _last = new Dictionary<string, double>();
        private double _lastRichAt = double.NegativeInfinity;

        public double LastRichAt => _lastRichAt;

        public float CooldownFor(string id, string category)
        {
            float c = CategoryCooldown(category);
            if (id != null && IdCooldowns.TryGetValue(id, out var own) && own > c) c = own;
            return c;
        }

        /// <summary>Decide whether a cue may start now. Records it when it passes.</summary>
        public bool TryPass(string id, string category, double now)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (id == SfxIds.UiToast && now - _lastRichAt < ToastMaskSeconds) return false;
            if (_last.TryGetValue(id, out var at) && now - at < CooldownFor(id, category) && now >= at) return false;
            _last[id] = now;
            if (IsRich(category)) _lastRichAt = now;
            return true;
        }

        public void Reset()
        {
            _last.Clear();
            _lastRichAt = double.NegativeInfinity;
        }
    }

    /// <summary>Event → cue rules for realtime messages that already reach the Bus.</summary>
    public static class CueRules
    {
        /// <summary>contract/config/market.json `tapeThresholdCents` (prints below it never reach the tape).</summary>
        public const long DefaultTapeThresholdCents = 100_000;

        /// <summary>
        /// [Inference] A "whale" print is ten times the tape threshold ($10,000 of Cash at the rc1 threshold). The
        /// contract has no whale threshold; this is the client's presentation rule, not an economy value.
        /// </summary>
        public static long WhaleThresholdCents(long tapeThresholdCents) =>
            Math.Max(1_000_000, 10 * Math.Max(0, tapeThresholdCents));

        public static string ForTape(string side, long cashCents, long tapeThresholdCents = DefaultTapeThresholdCents) =>
            Math.Abs(cashCents) >= WhaleThresholdCents(tapeThresholdCents) ? SfxIds.WhaleHorn : null;

        /// <summary>Market stage change from MarketState.coins (new | final | graduated | dead).</summary>
        public static string ForCoinStage(string previousStage, string stage) =>
            stage == "graduated" && previousStage != "graduated" ? SfxIds.Graduation : null;

        /// <summary>
        /// The CT "gm" ritual: a short chat line that is a morning greeting ("gm", "gm gm", "gmgm", "GM CT", "gm frens ☀️",
        /// "good morning"). Longer sentences that merely start with gm do not count.
        /// </summary>
        public static bool IsGm(string text)
        {
            var words = Words(text);
            if (words.Count == 0 || words.Count > 4) return false;
            if (words.Count >= 2 && words[0] == "good" && words[1] == "morning") return words.Count <= 3;
            var w = words[0];
            if (w.Length < 2 || w[0] != 'g') return false;
            // gm, gmm, gmmm, gmgm, gmgmgm
            bool isGm = true;
            for (int i = 1; i < w.Length; i++)
                if (!(w[i] == 'm' || (w[i] == 'g' && i + 1 < w.Length && w[i + 1] == 'm'))) { isGm = false; break; }
            if (!isGm || w[1] != 'm') return false;
            for (int i = 1; i < words.Count; i++)
                if (!GmTail.Contains(words[i]) && !(words[i].Length >= 2 && words[i][0] == 'g' && words[i][1] == 'm')) return false;
            return true;
        }

        private static readonly HashSet<string> GmTail = new HashSet<string>
        {
            "ct", "fam", "frens", "fren", "friends", "all", "everyone", "everybody", "chat", "gang", "anon", "anons", "degens",
            "legends", "sers", "ser", "wagmi", "to", "you", "y'all", "yall", "guys", "people", "family", "naija", "lagos",
        };

        private static List<string> Words(string text)
        {
            var words = new List<string>();
            if (string.IsNullOrEmpty(text) || text.Length > 80) return words;
            var sb = new StringBuilder();
            foreach (char raw in text)
            {
                char c = char.ToLowerInvariant(raw);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '\'') sb.Append(c);
                else if (sb.Length > 0) { words.Add(sb.ToString()); sb.Clear(); }
            }
            if (sb.Length > 0) words.Add(sb.ToString());
            return words;
        }

        /// <summary>True when `text` mentions `@handle` (case-insensitive, whole handle).</summary>
        public static bool MentionsHandle(string text, string handle)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(handle)) return false;
            handle = handle.TrimStart('@');
            if (handle.Length == 0) return false;
            int from = 0;
            while (from < text.Length)
            {
                int at = text.IndexOf('@', from);
                if (at < 0 || at + 1 + handle.Length > text.Length) return false;
                if (string.Compare(text, at + 1, handle, 0, handle.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    int end = at + 1 + handle.Length;
                    bool boundary = end == text.Length || !(char.IsLetterOrDigit(text[end]) || text[end] == '_');
                    if (boundary) return true;
                }
                from = at + 1;
            }
            return false;
        }
    }
}
