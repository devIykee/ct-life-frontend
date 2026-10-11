using System.Collections.Generic;
using CTLife.Core;

namespace CTLife.UI
{
    /// <summary>
    /// Player-facing words. toasts.json is "BE (FE writes the words)", so every toast id's text lives here, with
    /// {param} slots filled from the toast params. Money params ending in Cents are formatted as Cash.
    /// </summary>
    public static class Strings
    {
        private static readonly Dictionary<string, string> Toasts = new Dictionary<string, string>
        {
            ["toast_drained"] = "You got drained. -{amountCents}. Never drop your wallet.",
            ["toast_payment_confirmed"] = "Payment confirmed: +{cashCents} Cash",
            ["toast_promoted"] = "Promoted! {careerId} level {level}",
            ["toast_shift_paid"] = "Shift paid: +{payCents} · +{xp} XP",
            ["toast_rent_paid"] = "Rent paid: {rentCents}",
            ["toast_rent_missed"] = "Rent missed: {rentCents}. Landlord is not amused.",
            ["toast_debt_payment"] = "Debt payment {amountCents}. {remainingCents} to go.",
            ["toast_debt_missed"] = "Debt payment missed. Penalty {penaltyCents}.",
            ["toast_tax_paid"] = "Taxes paid: {taxCents}",
            ["toast_tip_received"] = "{fromAlias} tipped you {cashCents}",
            ["toast_needs_low"] = "Your {needId} is low ({value})",
            ["toast_spot_taken"] = "Someone is already there",
            ["toast_space_promoted"] = "You're a speaker now",
            ["toast_ad_approved"] = "Your ad on {slotId} is live",
            ["toast_ad_rejected"] = "Your ad on {slotId} was rejected",
        };

        private static readonly Dictionary<string, string> Errors = new Dictionary<string, string>
        {
            ["NETWORK_ERROR"] = "No connection. Check your network and try again.",
            ["INSUFFICIENT_CASH"] = "Not enough Cash.",
            ["ALIAS_TAKEN"] = "That alias is taken.",
            ["ALREADY_ROLLED"] = "You were already born. No re-rolls.",
            ["NONCE_INVALID"] = "Sign-in expired. Try again.",
            ["SIGNATURE_INVALID"] = "The wallet signature didn't check out. Try again.",
            ["TOKEN_EXPIRED"] = "Session expired. Sign in again.",
            ["SGT_ALREADY_LINKED"] = "This Seeker is already linked to another account.",
            ["CLIENT_OUTDATED"] = "Update required.",
            ["RATE_LIMITED"] = "Slow down a little.",
            ["VENUE_FULL"] = "This venue is full. Try again in a moment.",
            ["SPOT_TAKEN"] = "Someone is already there.",
            ["BLOCKED"] = "You can't interact with this player.",
            ["SHIFT_NOT_FINISHED"] = "Shift isn't over yet.",
            ["LEVERAGE_NOT_ALLOWED"] = "Your Trading skill doesn't allow that leverage yet.",
            ["SLIPPAGE_EXCEEDED"] = "Price moved too much. Try again.",
            ["TOPUP_CAP_REACHED"] = "Daily top-up cap reached.",
        };

        public static string Toast(string id, IReadOnlyDictionary<string, string> p)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (!Toasts.TryGetValue(id, out var t) && CTLife.Core.Services.TryGet<ConfigStore>(out var cfg) &&
                cfg.Registries.ToastTextIds.TryGetValue(id, out var mapped))
                Toasts.TryGetValue(mapped, out t);
            if (t == null)
            {
                Log.UnknownId("toasts", id);
                return id;
            }
            if (p == null) return t;
            foreach (var kv in p)
            {
                var v = kv.Value ?? "";
                if (kv.Key.EndsWith("Cents") && Money.TryParseCents(v, out var cents)) v = Money.Cash(cents);
                t = t.Replace("{" + kv.Key + "}", v);
            }
            return t;
        }

        public static string Error(string code, string serverMessage = null)
        {
            if (code != null && Errors.TryGetValue(code, out var m)) return m;
            return string.IsNullOrEmpty(serverMessage) ? "Something went wrong (" + code + ")" : serverMessage;
        }

        public static string NeedName(string needId) => needId switch
        {
            "energy" => "Energy",
            "hunger" => "Hunger",
            "fun" => "Fun",
            "hygiene" => "Hygiene",
            "sanity" => "Sanity",
            _ => needId,
        };
    }
}
