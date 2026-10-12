using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    public sealed class OnboardingTransactionTests
    {
        private const string Scope = "transaction-tests";
        private string _account;
        private readonly List<OnboardingTransactionStore> _stores = new List<OnboardingTransactionStore>();
        private static readonly string[] LookIds = { "skin_dark", "head_snow", "hair_snow", "top_tee", "bottom_denim", "shoes_canvas", "acc_none" };
        private static readonly string[] Traits = { "fast_talker", "iron_will" };

        [SetUp] public void SetUp() => _account = Guid.NewGuid().ToString("D");
        [TearDown] public void TearDown() { foreach (var store in _stores) store.Clear(); _stores.Clear(); }

        private OnboardingTransactionStore Store(string account = null, string scope = Scope)
        {
            var store = new OnboardingTransactionStore(account ?? _account, scope);
            _stores.Add(store);
            return store;
        }

        private static string CreateKey(OnboardingTransactionStore store, string alias = "anon", string[] look = null,
            string[] traits = null, string dream = "make_10k", string tribe = "trencher") =>
            store.CharacterKey(alias, look ?? LookIds, traits ?? Traits, dream, tribe);

        private static PostLotteryRoll200Response Insider() => new PostLotteryRoll200Response(
            PostLotteryRoll200Response.OutcomeEnum.Insider, 300000, 0, "studio");

        [Test]
        public void LotteryKeyExistsInStorageBeforeCallerCanSendAndSurvivesRestart()
        {
            var first = Store();
            var key = first.LotteryKey();
            Assert.IsTrue(Guid.TryParseExact(key, "D", out _));
            var restarted = Store();
            Assert.AreEqual(key, restarted.PendingLotteryKey);
            Assert.AreEqual(key, restarted.LotteryKey());
            Assert.IsFalse(restarted.LotteryRecorded, "preparing a request is not a committed roll");
        }

        [Test]
        public void AccountsAndEnvironmentsDoNotShareKeysOrReceipts()
        {
            var first = Store();
            first.RecordLottery(Insider());
            var otherAccount = Store("other-" + _account);
            var otherEnvironment = Store(scope: Scope + "-other");
            Assert.IsNull(otherAccount.Receipt);
            Assert.IsNull(otherEnvironment.Receipt);
            Assert.AreNotEqual(first.LotteryKey(), otherAccount.LotteryKey());
            Assert.AreNotEqual(first.LotteryKey(), otherEnvironment.LotteryKey());
        }

        [Test]
        public void ReceiptKeepsExactBirthValuesAcrossRestartAndDefendsAgainstMutation()
        {
            var first = Store();
            var result = Insider();
            first.RecordLottery(result);
            result.HomeId = "parents_basement";
            result.CashCents = 1;
            var restarted = Store();
            Assert.IsTrue(restarted.LotteryRecorded);
            var receipt = restarted.Receipt;
            Assert.AreEqual(PostLotteryRoll200Response.OutcomeEnum.Insider, receipt.Outcome);
            Assert.AreEqual(300000, receipt.CashCents);
            Assert.AreEqual(0, receipt.DebtCents);
            Assert.AreEqual("studio", receipt.HomeId);
            receipt.HomeId = "changed-by-caller";
            Assert.AreEqual("studio", restarted.Receipt.HomeId);
        }

        [Test]
        public void AlreadyRolledWithoutReceiptRemainsUnknownInsteadOfInventingAnOrigin()
        {
            var first = Store();
            first.RecordRolledWithoutReceipt();
            var restarted = Store();
            Assert.IsTrue(restarted.LotteryRecorded);
            Assert.IsNull(restarted.Receipt);
            restarted.RecordLottery(Insider());
            restarted.RecordRolledWithoutReceipt();
            Assert.AreEqual(PostLotteryRoll200Response.OutcomeEnum.Insider, restarted.Receipt.Outcome,
                "a later reconciliation must not erase a known exact receipt");
        }

        [Test]
        public void IncompleteOrInvalidReceiptsCannotBecomeRecordedBirthResults()
        {
            var store = Store();
            Assert.Throws<ArgumentException>(() => store.RecordLottery(null));
            Assert.Throws<ArgumentException>(() => store.RecordLottery(new PostLotteryRoll200Response(
                (PostLotteryRoll200Response.OutcomeEnum)99, 30000, 0, "home")));
            Assert.Throws<ArgumentException>(() => store.RecordLottery(new PostLotteryRoll200Response(
                PostLotteryRoll200Response.OutcomeEnum.ExitLiquidity, 30000, -1, "home")));
            Assert.IsFalse(store.LotteryRecorded);
        }

        [Test]
        public void SameCreateBodyKeepsKeyAcrossRelaunchAndAfterRoll()
        {
            var store = Store();
            var key = CreateKey(store);
            store.RecordLottery(Insider());
            Assert.AreEqual(key, CreateKey(Store()));
            var raw = PlayerPrefs.GetString("ctlife.onboarding.transaction." + Scope + "." + _account);
            Assert.IsNull(JObject.Parse(raw)["ageConfirmed"], "confirmation is not restored from the transaction journal");
            StringAssert.DoesNotContain("fast_talker", raw, "the journal keeps a signature, not the draft");
            StringAssert.DoesNotContain("anon", raw);
        }

        [TestCase("alias")]
        [TestCase("look")]
        [TestCase("traits")]
        [TestCase("trait-order")]
        [TestCase("dream")]
        [TestCase("tribe")]
        public void EditingAnyCreateBodyFieldRotatesItsKeyWithoutRerolling(string field)
        {
            var store = Store();
            var lotteryKey = store.LotteryKey();
            store.RecordLottery(Insider());
            var original = CreateKey(store);
            var look = (string[])LookIds.Clone();
            var traits = (string[])Traits.Clone();
            if (field == "look") look[3] = "top_new";
            if (field == "traits") traits[1] = "early_bird";
            if (field == "trait-order") Array.Reverse(traits);
            var changed = CreateKey(store, field == "alias" ? "new-alias" : "anon", look, traits,
                field == "dream" ? "own_a_business" : "make_10k", field == "tribe" ? "builder" : "trencher");
            Assert.AreNotEqual(original, changed);
            Assert.AreEqual(lotteryKey, store.LotteryKey());
            Assert.AreEqual("studio", store.Receipt.HomeId);
        }

        [Test]
        public void CompletingCharacterClearsOnlyCreateRequest()
        {
            var store = Store();
            var lotteryKey = store.LotteryKey();
            var createKey = CreateKey(store);
            store.RecordLottery(Insider());
            store.ClearCharacterRequest();
            var restarted = Store();
            Assert.AreEqual(lotteryKey, restarted.LotteryKey());
            Assert.IsTrue(restarted.LotteryRecorded);
            Assert.AreEqual("studio", restarted.Receipt.HomeId);
            Assert.AreNotEqual(createKey, CreateKey(restarted));
        }

        [Test]
        public void MissingOrEmptyAccountCannotWriteASharedAnonymousJournal()
        {
            Assert.Throws<ArgumentException>(() => new OnboardingTransactionStore(null, Scope));
            Assert.Throws<ArgumentException>(() => new OnboardingTransactionStore(" ", Scope));
        }

        [Test]
        public void LostCreateResponseRequiresActualRefreshedCharacterNotFlagsOrMoney()
        {
            var character = new Character("anon", new Look("skin", "head", "hair", "top", "bottom", "shoes", "acc"),
                "trencher", new List<string>(Traits), "make_10k");
            var me = new Me(new User(Guid.NewGuid(), "test-wallet", false, true), character, needs: new Needs(), skills: new Skills(),
                cashCents: 300000, homeId: "parents_basement", flags: new Dictionary<string, object>
                { ["lotteryRolled"] = true, ["hasCharacter"] = true });
            me.Character = null; // generated constructor rejects null even though /me legitimately returns it
            Assert.IsFalse(Onboarding.CharacterCreationRecorded(null));
            Assert.IsFalse(Onboarding.CharacterCreationRecorded(me));
            me.Character = character;
            Assert.IsTrue(Onboarding.CharacterCreationRecorded(me));
            me.Character.Alias = "";
            Assert.IsFalse(Onboarding.CharacterCreationRecorded(me));
        }

        [Test]
        public async Task ApiTransmitsRetainedKeysAndTheSameSignedCreateBody()
        {
            var store = Store();
            var rest = new RecordingRest();
            var api = new Api(rest, null);
            var lotteryKey = store.LotteryKey();
            await api.RollLottery(idempotencyKey: lotteryKey);
            Assert.AreEqual("/lottery/roll", rest.Path);
            Assert.AreEqual(lotteryKey, rest.Key);
            var createKey = CreateKey(store);
            using var cancellation = new CancellationTokenSource();
            await api.CreateCharacter("anon", LookIds, Traits, "make_10k", "trencher", true,
                cancellation.Token, idempotencyKey: createKey);
            Assert.AreEqual("/characters", rest.Path);
            Assert.AreEqual(createKey, rest.Key);
            Assert.AreEqual(cancellation.Token, rest.Token);
            Assert.AreEqual(OnboardingRequest.CharacterBody("anon", LookIds, Traits, "make_10k", "trencher", true), rest.Body);
            Assert.AreEqual(JTokenType.Boolean, JObject.Parse(rest.Body)["ageConfirmed"].Type);
        }

        private sealed class RecordingRest : IRestClient
        {
            public string Path, Key, Body;
            public CancellationToken Token;
            public Func<Task<bool>> RefreshHandler { get; set; }
            public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default)
            {
                Path = path; Key = idempotencyKey; Body = body as string; Token = ct;
                return Task.FromResult(default(T));
            }
            public Task<T> Get<T>(string p, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Put<T>(string p, object body, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Patch<T>(string p, object body, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Delete<T>(string p, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<T> Send<T>(string method, string p, object body = null, bool auth = true, string idempotencyKey = null,
                CancellationToken ct = default) => throw new NotSupportedException();
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey,
                string ifNoneMatch, CancellationToken ct) => throw new NotSupportedException();
        }
    }
}
