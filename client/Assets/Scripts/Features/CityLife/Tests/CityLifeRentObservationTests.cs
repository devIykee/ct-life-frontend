using System;
using System.Collections.Generic;
using System.Reflection;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.CityLife.Tests
{
    public sealed class CityLifeRentObservationTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private ConfigStore _previousConfig;
        private PlayerProfile _previousProfile, _profile;
        private Subscriptions _subscriptions;
        private List<ToastSignal> _toasts;
        private Character _character;
        private Me _me;

        [SetUp]
        public void SetUp()
        {
            Services.TryGet(out _previousConfig);
            Services.TryGet(out _previousProfile);
            var config = new ConfigStore();
            var snapshot = JObject.Parse(Resources.Load<TextAsset>("Contract/config_snapshot").text);
            snapshot["livePhase"] = 6;
            config.Apply(snapshot.ToString(), null, persist: false);
            Services.Register(config);
            Assert.IsTrue(Phases.IsOpen(4), "Rent notifications must be enabled for this regression test.");

            _character = new Character("rent-observer", new Look("skin", "head", "hair", "top", "bottom", "shoes", "acc"),
                "trencher", new List<string> { "fast_talker", "iron_will" }, "make_10k");
            _me = new Me(new User(Guid.NewGuid(), "fixture-wallet", false, false), _character, new Needs(), new Skills(),
                cashCents: 0, debtCents: 0, homeId: "parents_basement", flags: new Dictionary<string, object>());
            _me.Character = null; // The endpoint permits null; the generated public constructor does not.
            _profile = new PlayerProfile(null); // no transport is used: ApplyLottery is the production signal source
            typeof(PlayerProfile).GetProperty("Me").SetValue(_profile, _me);
            Services.Register(_profile);

            // Exercise the shipping profile callback and rent observer, without starting the unrelated hub's
            // background HTTP loop or room subscriptions. Existing City Life tests use this same isolation pattern.
            var hub = (CityLifeHub)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(CityLifeHub));
            typeof(CityLifeHub).GetField("Needs").SetValue(hub, new NeedsClock());
            typeof(CityLifeHub).GetField("_lastDebt", Private).SetValue(hub, -1L);
            typeof(CityLifeHub).GetProperty("Homes").SetValue(hub, new List<HomeOffer>
            {
                new HomeOffer { Id = "parents_basement", RentCents = 0,
                    NextDueAtMs = DateTimeOffset.UtcNow.AddDays(14).ToUnixTimeMilliseconds() },
            });
            var observe = (Action)Delegate.CreateDelegate(typeof(Action), hub,
                typeof(CityLifeHub).GetMethod("OnProfileChanged", Private));
            _toasts = new List<ToastSignal>();
            _subscriptions = new Subscriptions();
            _subscriptions.On<ProfileChangedSignal>(_ => observe());
            _subscriptions.On<ToastSignal>(t => _toasts.Add(t));
        }

        [TearDown]
        public void TearDown()
        {
            _subscriptions?.Dispose();
            if (_previousConfig == null) Services.Unregister<ConfigStore>(); else Services.Register(_previousConfig);
            if (_previousProfile == null) Services.Unregister<PlayerProfile>(); else Services.Register(_previousProfile);
        }

        [Test]
        public void LotteryDebtDoesNotSpoilRevealAndLaterDebtIncreaseStillNotifies()
        {
            Bus.Publish(new ProfileChangedSignal()); // signed in, no character, no debt
            _profile.ApplyLottery(new PostLotteryRoll200Response(
                PostLotteryRoll200Response.OutcomeEnum.ExitLiquidity, 30000, 72000, "parents_basement"));
            Assert.IsEmpty(_toasts, "The lottery's opening debt must not become a Sunday Unlock penalty toast.");

            _me.Character = _character;
            Bus.Publish(new ProfileChangedSignal());
            Assert.IsEmpty(_toasts, "The first actual character observation establishes its debt baseline.");
            _me.DebtCents += 12000;
            Bus.Publish(new ProfileChangedSignal());
            Assert.AreEqual(1, _toasts.Count, "Suppressing onboarding must not disable subsequent debt notifications.");
            Assert.AreEqual(CityLifeStrings.T("rent.toast.debtUp"), _toasts[0].literal);
        }

        [Test]
        public void CharacterlessProfileResetsPriorObservationBeforeNewCharacterAppears()
        {
            _me.Character = _character;
            Bus.Publish(new ProfileChangedSignal()); // an earlier observed character at zero debt
            _me.Character = null;
            Bus.Publish(new ProfileChangedSignal());
            _profile.ApplyLottery(new PostLotteryRoll200Response(
                PostLotteryRoll200Response.OutcomeEnum.ExitLiquidity, 30000, 72000, "parents_basement"));
            _me.Character = _character;
            Bus.Publish(new ProfileChangedSignal());
            Assert.IsEmpty(_toasts, "An old observed balance must not survive the characterless onboarding state.");
        }
    }
}
