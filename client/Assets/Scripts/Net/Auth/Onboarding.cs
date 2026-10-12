using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Rest.Model;

namespace CTLife.Net
{
    /// <summary>
    /// Server-driven onboarding decisions (pure; unit-tested in Tests/EditMode).
    ///
    /// API order: birth lottery, then character creation; the UI can collect draft choices before the roll.
    /// The contract says the roll "is forced before the character is
    /// created", so a character always means rolled, whatever the flag says: backend 7243073 added
    /// `flags.lotteryRolled` from a new column without backfilling existing users, so every player created before that
    /// migration reads `lotteryRolled:false` with a character, and their roll is a 500 (feedback item 92). Without a
    /// character the flag wins; a server without the flag (9f3e5ef) is read from the balance instead: the lottery is
    /// the only path to Cash or debt before a character exists.
    /// </summary>
    public static class Onboarding
    {
        public static bool LotteryRolled(Me me)
        {
            if (me == null) return false;
            if (HasCharacter(me)) return true;
            if (me.Flags != null && me.Flags.TryGetValue("lotteryRolled", out var flag) && flag is bool rolled) return rolled;
            return me.CashCents != 0 || me.DebtCents != 0;
        }

        public static bool HasCharacter(Me me) => me?.Character != null && !string.IsNullOrEmpty(me.Character.Alias);

        public static bool NeedsLottery(Me me) => me != null && !LotteryRolled(me);

        public static bool NeedsCharacter(Me me) => me != null && !HasCharacter(me);

        /// <summary>
        /// A fresh /me showing an actual character reconciles a lost create response. Neither balances nor flags alone
        /// prove creation; the caller must fetch this profile after the failed write and keep the same account lifetime.
        /// </summary>
        public static bool CharacterCreationRecorded(Me refreshed) => HasCharacter(refreshed);

        /// <summary>A refresh confirms this write only when it contains the submitted character, including its
        /// personal asset. A pre-existing stock character is not a receipt for a different creation request.</summary>
        public static bool CharacterCreationMatches(Me refreshed, string alias, string[] look, IList<string> traits,
            string dreamId, string tribeId, string requestedLikenessJobId, string recordedLikenessJobId)
        {
            if (!HasCharacter(refreshed) || look == null || look.Length != 7 || traits == null) return false;
            var character = refreshed.Character;
            var saved = character.Look;
            if (saved == null || character.Alias != alias || character.DreamId != dreamId || character.TribeId != tribeId ||
                !string.Equals(requestedLikenessJobId ?? "", recordedLikenessJobId ?? "", StringComparison.Ordinal)) return false;
            var savedLook = new[] { saved.SkinTone, saved.HeadId, saved.HairId, saved.TopId, saved.BottomId, saved.ShoesId, saved.AccId };
            return savedLook.SequenceEqual(look) && character.TraitIds != null &&
                character.TraitIds.Count == traits.Count && new HashSet<string>(traits, StringComparer.Ordinal).SetEquals(character.TraitIds);
        }

        /// <summary>
        /// After a failed roll and a fresh GET /me: true when the roll must already have happened (409 ALREADY_ROLLED,
        /// or a lost response whose retry now fails, while /me shows the outcome). Then onboarding continues.
        /// </summary>
        public static bool RollFailureMeansDone(Exception error, Me refreshed)
        {
            if (error is ApiException api && api.Is("ALREADY_ROLLED")) return true;
            if (error is ApiException a && a.IsNetwork) return false;
            return LotteryRolled(refreshed);
        }
    }

    /// <summary>
    /// Whether the creator may submit a look. The server's item registry is authoritative when it lists items;
    /// FE-authored complete looks (CreatorLookCatalog) are submitted otherwise and the server decides
    /// (VALIDATION_FAILED is shown to the player). rc1's items.json has no free head item at all, so no look could
    /// ever pass a registry-only gate (feedback item 42; proposal docs/fe-contract-proposals/starter-looks-items-20261007.md).
    /// </summary>
    public static class CreatorLookPolicy
    {
        public static bool CanSubmit(string[] look, bool isAuthoredLook, Func<string[], bool> registryConfirms)
        {
            if (look == null || look.Length != 7) return false;
            foreach (var id in look) if (string.IsNullOrWhiteSpace(id)) return false;
            if (registryConfirms != null && registryConfirms(look)) return true;
            return isAuthoredLook;
        }
    }
}

namespace CTLife.Net
{
    /// <summary>
    /// Makes a /me body decodable by the generated contract model. Backend main's `user` object has no
    /// `hasCharacter` (required by the contract's User schema, feedback item 48), so the strict generated model
    /// throws and the player could never get past sign-in. The value is derived from `character`.
    /// </summary>
    public static class MeCompat
    {
        public static Newtonsoft.Json.Linq.JToken Normalize(Newtonsoft.Json.Linq.JToken body)
        {
            if (!(body is Newtonsoft.Json.Linq.JObject me) || !(me["user"] is Newtonsoft.Json.Linq.JObject user)) return body;
            if (user["hasCharacter"] == null || user["hasCharacter"].Type != Newtonsoft.Json.Linq.JTokenType.Boolean)
            {
                var character = me["character"];
                user["hasCharacter"] = character != null && character.Type == Newtonsoft.Json.Linq.JTokenType.Object;
            }
            if (me["flags"] == null || me["flags"].Type == Newtonsoft.Json.Linq.JTokenType.Null) me["flags"] = new Newtonsoft.Json.Linq.JObject();
            return me;
        }
    }
}
