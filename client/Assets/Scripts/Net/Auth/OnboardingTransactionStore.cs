using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using CTLife.Core;
using CTLife.Rest.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.Net
{
    /// <summary>
    /// Per-account receipts and retained request keys for interrupted onboarding. Saves are flushed before a key is
    /// returned or a receipt is acknowledged. Stores no auth credentials, draft contents, or reusable age consent.
    /// The receipt is the exact birth result, not an inference from /me's current balances or pre-character home.
    /// </summary>
    public sealed class OnboardingTransactionStore
    {
        private const int Version = 1;
        private readonly string _key;
        private string _lotteryKey, _characterKey, _characterSignature;
        private bool _lotteryRecorded;
        private PostLotteryRoll200Response _receipt;

        public OnboardingTransactionStore(string accountId, string environment = null)
        {
            if (string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("An account is required", nameof(accountId));
            _key = "ctlife.onboarding.transaction." + (environment ?? Env.Current.id.ToString()) + "." + accountId;
            Load(PlayerPrefs.GetString(_key, ""));
        }

        /// <summary>True even if the server confirmed an old roll whose exact receipt is unavailable.</summary>
        public bool LotteryRecorded => _lotteryRecorded;
        public string PendingLotteryKey => _lotteryKey;

        /// <summary>A defensive copy; null means the exact birth outcome is unknown, not Exit Liquidity.</summary>
        public PostLotteryRoll200Response Receipt => Copy(_receipt);

        /// <summary>Persist before starting POST /lottery/roll; retry and relaunch reuse this key.</summary>
        public string LotteryKey()
        {
            if (string.IsNullOrEmpty(_lotteryKey))
                _lotteryKey = Ids.NewIdempotencyKey();
            Save();
            return _lotteryKey;
        }

        public void RecordLottery(PostLotteryRoll200Response receipt)
        {
            if (!ValidReceipt(receipt)) throw new ArgumentException("A complete lottery receipt is required", nameof(receipt));
            _receipt = Copy(receipt);
            _lotteryRecorded = true;
            Save();
        }

        /// <summary>A confirmed ALREADY_ROLLED or fresh /me can establish completion without inventing a birth card.</summary>
        public void RecordRolledWithoutReceipt()
        {
            _lotteryRecorded = true;
            Save();
        }

        /// <summary>
        /// The exact request body selects its retained key. Age must be confirmed anew by the caller; only the request
        /// hash is stored. A changed alias, trait order, or other selection produces a new key rather than replaying an
        /// earlier character body. Do not call this until unresolved writes have been reconciled through /me.
        /// </summary>
        public string CharacterKey(string alias, string[] look, IList<string> traitIds, string dreamId, string tribeId)
        {
            var body = OnboardingRequest.CharacterBody(alias, look, traitIds, dreamId, tribeId, true);
            string signature;
            using (var sha = SHA256.Create())
                signature = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(body))).Replace("-", "").ToLowerInvariant();
            if (_characterSignature != signature || string.IsNullOrEmpty(_characterKey))
            {
                _characterSignature = signature;
                _characterKey = Ids.NewIdempotencyKey();
            }
            Save();
            return _characterKey;
        }

        /// <summary>Call only after the character response or a fresh /me confirms creation. Retains the birth receipt.</summary>
        public void ClearCharacterRequest()
        {
            _characterKey = _characterSignature = null;
            Save();
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(_key);
            PlayerPrefs.Save();
            _lotteryKey = _characterKey = _characterSignature = null;
            _receipt = null;
            _lotteryRecorded = false;
        }

        private void Save()
        {
            var state = new JObject
            {
                ["v"] = Version,
                ["lotteryKey"] = _lotteryKey,
                ["lotteryRecorded"] = _lotteryRecorded,
                ["receipt"] = _receipt == null ? null : JObject.Parse(Core.Json.Serialize(_receipt)),
                ["characterKey"] = _characterKey,
                ["characterSignature"] = _characterSignature,
            };
            // A failed write must reach the caller: submitting without a persisted key loses restart reconciliation.
            PlayerPrefs.SetString(_key, state.ToString(Formatting.None));
            PlayerPrefs.Save();
        }

        private void Load(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var state = JObject.Parse(json);
                if ((int?)state["v"] != Version) return;
                _lotteryKey = RequestKey((string)state["lotteryKey"]);
                _characterKey = RequestKey((string)state["characterKey"]);
                _characterSignature = (string)state["characterSignature"];
                _lotteryRecorded = state["lotteryRecorded"]?.Type == JTokenType.Boolean && (bool)state["lotteryRecorded"];
                if (state["receipt"] is JObject raw)
                {
                    var receipt = Core.Json.Deserialize<PostLotteryRoll200Response>(raw.ToString());
                    if (ValidReceipt(receipt)) { _receipt = receipt; _lotteryRecorded = true; }
                }
            }
            catch (Exception)
            {
                // Do not log raw stored data. Server state remains authoritative if a local receipt is damaged.
                _receipt = null;
                Log.Warn("onboarding", "Stored onboarding receipt is unreadable; reconcile with the server.");
            }
        }

        private static string RequestKey(string key) => Guid.TryParseExact(key, "D", out _) ? key : null;
        private static bool ValidReceipt(PostLotteryRoll200Response r) => r != null &&
            Enum.IsDefined(typeof(PostLotteryRoll200Response.OutcomeEnum), r.Outcome) &&
            r.CashCents >= 0 && r.DebtCents >= 0 && !string.IsNullOrWhiteSpace(r.HomeId);
        private static PostLotteryRoll200Response Copy(PostLotteryRoll200Response r) => r == null ? null :
            new PostLotteryRoll200Response(r.Outcome, r.CashCents, r.DebtCents, r.HomeId);
    }

    /// <summary>One body serializer shared by API submission and its persisted request signature.</summary>
    public static class OnboardingRequest
    {
        public static string CharacterBody(string alias, string[] look, IList<string> traitIds, string dreamId, string tribeId,
            bool ageConfirmed)
        {
            if (look == null || look.Length != 7) throw new ArgumentException("A look has seven IDs", nameof(look));
            if (traitIds == null) throw new ArgumentNullException(nameof(traitIds));
            return new JObject
            {
                ["alias"] = alias,
                ["look"] = new JObject
                {
                    ["skinTone"] = look[0], ["headId"] = look[1], ["hairId"] = look[2], ["topId"] = look[3],
                    ["bottomId"] = look[4], ["shoesId"] = look[5], ["accId"] = look[6],
                },
                ["traitIds"] = new JArray(traitIds), ["dreamId"] = dreamId, ["tribeId"] = tribeId,
                ["ageConfirmed"] = ageConfirmed,
            }.ToString(Formatting.None);
        }
    }
}
