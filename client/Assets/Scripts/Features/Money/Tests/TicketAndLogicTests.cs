using System;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Economy.Tests
{
    public sealed class RetainedWriteTests
    {
        [Test]
        public void SameRequestReusesTheKeyUntilCompleted()
        {
            int n = 0;
            var w = new RetainedWrite(() => "k" + (++n));
            Assert.AreEqual("k1", w.KeyFor("/market/trades", "{\"a\":1}"));
            Assert.AreEqual("k1", w.KeyFor("/market/trades", "{\"a\":1}"), "unknown outcome: retry replays");
            Assert.AreEqual("k2", w.KeyFor("/market/trades", "{\"a\":2}"), "different body never reuses a key");
            w.Complete();
            Assert.IsFalse(w.Pending);
            Assert.AreEqual("k3", w.KeyFor("/market/trades", "{\"a\":2}"), "after a definitive answer, the same order is a new order");
        }

        [Test]
        public void DefaultKeysAreUuidV4()
        {
            var key = new RetainedWrite().KeyFor("/x", "{}");
            Assert.IsTrue(Guid.TryParse(key, out _));
            Assert.AreEqual('4', key[14]);
        }
    }

    public sealed class AmountInputTests
    {
        [TestCase("12.5", 1250)]
        [TestCase("12.50", 1250)]
        [TestCase("0.01", 1)]
        [TestCase("100", 10000)]
        [TestCase(" 7 ", 700)]
        public void Cash_Accepts(string input, long cents)
        {
            Assert.IsTrue(AmountInput.TryCash(input, out var c));
            Assert.AreEqual(cents, c);
        }

        [TestCase("0")]
        [TestCase("-5")]
        [TestCase("1.234")]
        [TestCase("1,000")]
        [TestCase("1e3")]
        [TestCase("")]
        [TestCase("abc")]
        [TestCase("99999999999999999")]
        public void Cash_Rejects(string input) => Assert.IsFalse(AmountInput.TryCash(input, out _));

        [Test]
        public void Micros_And_Slippage()
        {
            Assert.IsTrue(AmountInput.TryMicros("0.0125", out var m));
            Assert.AreEqual(12500, m);
            Assert.IsFalse(AmountInput.TryMicros("0.0000001", out _));
            Assert.IsTrue(AmountInput.TrySlippagePercent("1.5", out var bps));
            Assert.AreEqual(150, bps);
            Assert.IsFalse(AmountInput.TrySlippagePercent("0", out _));
            Assert.IsFalse(AmountInput.TrySlippagePercent("51", out _));
        }
    }

    public sealed class TicketTests
    {
        private const string Coin = "0192a1c2-1111-7000-8000-000000000001";

        [Test]
        public void Buy_BuildsContractBody()
        {
            var t = Tickets.Buy(Coin, "25", 100000, 100, 200);
            Assert.IsTrue(t.Ok);
            Assert.AreEqual("buy", (string)t.Body["side"]);
            Assert.AreEqual(2500, (long)t.Body["cashCents"]);
            Assert.AreEqual(200, (int)t.Body["maxSlippageBps"]);
            Assert.IsNull(t.Body["units"]);
        }

        [Test]
        public void Buy_Rejections()
        {
            Assert.AreEqual("ticket.pickCoin", Tickets.Buy(null, "25", 100000, 100, 200).ErrorKey);
            Assert.AreEqual("ticket.badCash", Tickets.Buy(Coin, "2.555", 100000, 100, 200).ErrorKey);
            Assert.AreEqual("ticket.minTrade", Tickets.Buy(Coin, "0.50", 100000, 100, 200).ErrorKey);
            Assert.AreEqual("ticket.overBalance", Tickets.Buy(Coin, "2000", 100000, 100, 200).ErrorKey);
            Assert.AreEqual("ticket.badSlippage", Tickets.Buy(Coin, "25", 100000, 100, 0).ErrorKey);
        }

        [Test]
        public void Sell_SendsUnitsAsBaseUnitString()
        {
            var units = TokenUnits.Fraction("3324000", 50);
            var t = Tickets.Sell(Coin, units, "3324000", 100);
            Assert.IsTrue(t.Ok);
            Assert.AreEqual(JTokenType.String, t.Body["units"].Type, "contract: units is a string");
            Assert.AreEqual("1662000", (string)t.Body["units"]);
            Assert.AreEqual("ticket.overHeld", Tickets.Sell(Coin, "3324001", "3324000", 100).ErrorKey);
            Assert.AreEqual("ticket.badUnits", Tickets.Sell(Coin, null, "3324000", 100).ErrorKey);
            Assert.AreEqual("ticket.badUnits", Tickets.Sell(Coin, "0", "3324000", 100).ErrorKey);
        }

        [Test]
        public void OpenPerp_RespectsCapAndContractMax()
        {
            var ok = Tickets.OpenPerp(Coin, "long", 10, 10, "50", 100000);
            Assert.IsTrue(ok.Ok);
            Assert.AreEqual(5000, (long)ok.Body["marginCents"]);
            Assert.AreEqual(10, (int)ok.Body["leverage"]);
            Assert.AreEqual("ticket.leverageCap", Tickets.OpenPerp(Coin, "long", 11, 10, "50", 100000).ErrorKey);
            Assert.AreEqual("ticket.leverageCap", Tickets.OpenPerp(Coin, "short", 21, null, "50", 100000).ErrorKey, "unknown cap still bounded by the contract's 20");
            Assert.IsTrue(Tickets.OpenPerp(Coin, "short", 20, null, "50", 100000).Ok);
            Assert.AreEqual("ticket.pickSide", Tickets.OpenPerp(Coin, "up", 1, 10, "50", 100000).ErrorKey);
            Assert.AreEqual("ticket.overBalance", Tickets.OpenPerp(Coin, "long", 1, 10, "5000", 100000).ErrorKey);
        }

        [Test]
        public void Bet_AlphaCall_Stake()
        {
            var bet = Tickets.Bet("0192a1c2-3333-7000-8000-000000000001", "yes", "10", 100000);
            Assert.AreEqual(1000, (long)bet.Body["stakeCents"]);
            Assert.AreEqual("ticket.pickOutcome", Tickets.Bet("0192a1c2-3333-7000-8000-000000000001", null, "10", 100000).ErrorKey);

            var call = Tickets.AlphaCall(Coin, 15000, "0.03", "0.01");
            Assert.IsTrue(call.Ok);
            Assert.AreEqual(JTokenType.String, call.Body["entryMicros"].Type, "backend Zod bigint price fields require digit strings");
            Assert.AreEqual(JTokenType.String, call.Body["targetMicros"].Type);
            Assert.AreEqual(JTokenType.String, call.Body["stopMicros"].Type);
            Assert.AreEqual(30000, (long)call.Body["targetMicros"]);
            Assert.IsTrue(Tickets.AlphaCall(Coin, 15000, "0.01", "0.03").Ok, "bearish call");
            Assert.AreEqual("ticket.callSides", Tickets.AlphaCall(Coin, 15000, "0.03", "0.02").ErrorKey);
            Assert.AreEqual("ticket.badPrice", Tickets.AlphaCall(Coin, 15000, "x", "0.01").ErrorKey);

            var deposit = Tickets.Stake("deposit", "0.5", "100000000000", "0");
            Assert.AreEqual("50000000000", (string)deposit.Body["amount"]);
            Assert.AreEqual("ticket.overOre", Tickets.Stake("deposit", "2", "100000000000", "0").ErrorKey);
            Assert.AreEqual("ticket.overStaked", Tickets.Stake("withdraw", "0.1", "0", "0").ErrorKey);
            Assert.IsNull(Tickets.Stake("claim", null, null, null).Body["amount"]);
            Assert.AreEqual("ticket.badAction", Tickets.Stake("deploy", "1", null, null).ErrorKey, "mining (deploy) is never built");
        }
    }

    public sealed class LeverageCapTests
    {
        private static readonly JToken Skills = JToken.Parse(@"{""leverageByTradingLevel"":[{""level"":1,""leverage"":1},{""level"":4,""leverage"":1},{""level"":5,""leverage"":2},{""level"":10,""leverage"":10}],""tribeBonusLeverage"":{""perp_degen"":5,""whale"":15}}");

        [Test]
        public void FromConfig()
        {
            Assert.AreEqual(1, LeverageCap.Compute(Skills, 1, "trencher"));
            Assert.AreEqual(2, LeverageCap.Compute(Skills, 5, null));
            Assert.AreEqual(10, LeverageCap.Compute(Skills, 10, "anon"));
            Assert.AreEqual(15, LeverageCap.Compute(Skills, 10, "perp_degen"));
            Assert.AreEqual(20, LeverageCap.Compute(Skills, 10, "whale"), "clamped to the contract's maximum");
            Assert.IsNull(LeverageCap.Compute(Skills, 7, null), "level missing from the table: defer to the server");
            Assert.IsNull(LeverageCap.Compute(null, 5, null));
            Assert.IsNull(LeverageCap.Compute(Skills, null, null));
        }
    }

    public sealed class MoneyErrorsTests
    {
        [Test]
        public void ContractCodes()
        {
            var cap = MoneyErrors.From(new ApiException(429, "TOPUP_CAP_REACHED", "cap"), true);
            Assert.AreEqual("err.topupCap", cap.Key);
            Assert.IsFalse(cap.OutcomeUnknown);

            var cash = MoneyErrors.From(new ApiException(402, "INSUFFICIENT_CASH", "no", JToken.Parse("{\"shortfallCents\":50000}")), true);
            Assert.AreEqual("err.insufficientCash", cash.Key);
            Assert.AreEqual("$500.00", cash.Args[0]);

            var lev = MoneyErrors.From(new ApiException(400, "LEVERAGE_NOT_ALLOWED", "no", JToken.Parse("{\"requested\":5,\"allowed\":2}")), true);
            Assert.AreEqual("2x", lev.Args[0], "backend sends `allowed`, the contract example `max`");

            var slip = MoneyErrors.From(new ApiException(400, "SLIPPAGE_EXCEEDED", "no", JToken.Parse("{\"executedBps\":350}")), true);
            Assert.AreEqual("3.50%", slip.Args[0]);
            Assert.IsTrue(slip.Retryable);

            Assert.AreEqual("err.paymentExpired", MoneyErrors.From(new ApiException(410, "PAYMENT_EXPIRED", "x"), false).Key);
            Assert.AreEqual("err.paymentUnderpaid", MoneyErrors.From(new ApiException(402, "PAYMENT_UNDERPAID", "x"), false).Key);
            Assert.AreEqual("err.generic", MoneyErrors.From(new ApiException(400, "SOMETHING_NEW", "x"), false).Key, "unknown codes never crash");
        }

        [Test]
        public void TransportFailures_AreUnknownOutcomesForWrites()
        {
            var net = MoneyErrors.From(new ApiException(0, "NETWORK_ERROR", "offline", isNetwork: true), true);
            Assert.IsTrue(net.OutcomeUnknown);
            Assert.AreEqual("err.unknownOutcome", net.Key);
            Assert.IsFalse(MoneyErrors.From(new ApiException(0, "NETWORK_ERROR", "offline", isNetwork: true), false).OutcomeUnknown);
            Assert.IsTrue(MoneyErrors.From(new ApiException(200, "DECODE_FAILED", "x"), true).OutcomeUnknown);
            Assert.IsTrue(MoneyErrors.From(new TimeoutException(), true).OutcomeUnknown);
            Assert.IsTrue(MoneyErrors.From(new FormatException(), true).OutcomeUnknown);
            Assert.IsTrue(MoneyErrors.From(new ApiException(502, "HTTP_502", "bad gateway"), true).OutcomeUnknown);
            Assert.AreEqual("UNAVAILABLE", MoneyErrors.From(new ApiException(404, "HTTP_404", "Not Found"), false).Code, "missing route");
            Assert.AreEqual("err.notFound", MoneyErrors.From(new ApiException(404, "NOT_FOUND", "gone"), false).Key);
        }

        [Test]
        public void Wallet()
        {
            var declined = MoneyErrors.From(new WalletException("no", userDeclined: true), true);
            Assert.AreEqual("WALLET_DECLINED", declined.Code);
            Assert.IsFalse(declined.OutcomeUnknown);
            var failed = MoneyErrors.From(new WalletException("boom"), true);
            Assert.IsTrue(failed.OutcomeUnknown, "the wallet may have broadcast");
        }

        [Test]
        public void EveryKeyHasCopy()
        {
            foreach (var code in new[] { "TOPUP_CAP_REACHED", "PAYMENT_EXPIRED", "PAYMENT_UNDERPAID", "INSUFFICIENT_CASH", "SLIPPAGE_EXCEEDED",
                         "LEVERAGE_NOT_ALLOWED", "VALIDATION_FAILED", "NOT_FOUND", "FORBIDDEN", "RATE_LIMITED", "SERVER_BUSY", "TOKEN_EXPIRED" })
                Assert.IsTrue(MoneyStrings.Has(MoneyErrors.From(new ApiException(400, code, "x"), false).Key), code);
            foreach (var key in new[] { "err.walletDeclined", "err.walletFailed", "err.unknownOutcome", "err.network", "err.invalidResponse", "err.unavailable", "err.server", "topup.noTx", "topup.failed" })
                Assert.IsTrue(MoneyStrings.Has(key), key);
            Assert.AreEqual("Not enough Cash (short $1.00).", MoneyStrings.Err(new MoneyError { Key = "err.insufficientCash", Args = new object[] { "$1.00" } }));
        }
    }

    public sealed class CatalogTests
    {
        private static readonly JToken Economy = JToken.Parse(@"{
            ""topUpCapCentsPerDay"": 1000000,
            ""topUpPacks"": [
                { ""id"": ""ramen"", ""label"": ""Ramen"", ""priceUsdc"": 1, ""cashCents"": 50000 },
                { ""id"": ""whale"", ""label"": ""Whale"", ""priceUsdc"": 10, ""cashCents"": 700000, ""bestValue"": true },
                { ""id"": ""../bad"", ""label"": ""Bad"", ""priceUsdc"": 1, ""cashCents"": 1 }
            ],
            ""currencyBonusBps"": { ""USDC"": 0, ""SOL"": 0, ""SKR"": 1000, ""ORE"": 1500 },
            ""currencies"": {
                ""USDC"": { ""decimals"": 6, ""mint"": ""EPj"", ""devnetMint"": ""4zM"" },
                ""SOL"": { ""decimals"": 9, ""mint"": null, ""devnetMint"": null },
                ""SKR"": { ""decimals"": 5, ""mint"": ""SKR"", ""devnetMint"": null },
                ""ORE"": { ""decimals"": 11, ""mint"": ""ore"", ""devnetMint"": null, ""mainnetOnly"": true }
            }
        }");

        [Test]
        public void ParsesPacksAndCurrencies()
        {
            var c = TopUpCatalog.Parse(Economy);
            Assert.AreEqual(2, c.Packs.Count, "unsafe sku ids are dropped");
            Assert.IsTrue(c.Packs[1].BestValue);
            Assert.AreEqual(700000, c.Packs[1].CashCents);
            Assert.AreEqual(1500, c.Currencies["ORE"].BonusBps);
            Assert.AreEqual(5, c.Decimals("SKR"));
            Assert.IsFalse(TopUpCatalog.Parse(null).Loaded);
        }

        [Test]
        public void AvailabilityByCluster()
        {
            var c = TopUpCatalog.Parse(Economy);
            Assert.AreEqual(CurrencyAccess.Devnet, c.Availability("USDC", SolanaCluster.Devnet).Access);
            Assert.AreEqual(CurrencyAccess.Devnet, c.Availability("SOL", SolanaCluster.Devnet).Access);
            Assert.AreEqual(CurrencyAccess.Unavailable, c.Availability("SKR", SolanaCluster.Devnet).Access, "no verified devnet mint");
            Assert.AreEqual(CurrencyAccess.Mainnet, c.Availability("ORE", SolanaCluster.Devnet).Access, "ORE is mainnet even on staging");
            Assert.IsTrue(c.Availability("ORE", SolanaCluster.Devnet).NeedsMainnetConfirm);
            Assert.AreEqual(CurrencyAccess.Mainnet, c.Availability("USDC", SolanaCluster.Mainnet).Access);
            Assert.AreEqual(CurrencyAccess.Mainnet, c.Availability("SKR", SolanaCluster.Mainnet).Access);
            Assert.AreEqual(CurrencyAccess.Unavailable, c.Availability("USDC", SolanaCluster.None).Access, "mock env: wallet flows go to staging");
            Assert.AreEqual(CurrencyAccess.Unavailable, c.Availability("BTC", SolanaCluster.Devnet).Access);
        }
    }

    public sealed class DisplayLogicTests
    {
        [Test]
        public void PnlCard_UsesServerValuesOnly()
        {
            var p = new PositionRow { CoinId = "c", Ticker = "FROGE", Units = "2500000", CostBasisCents = 10000, UnrealizedCents = -2500 };
            var d = PnlCardData.From(p, null);
            Assert.AreEqual("FROGE", d.Ticker);
            Assert.AreEqual(-2500, d.UnrealizedCents);
            Assert.AreEqual(-2500, d.ReturnBps, "-25.00% of the server's cost basis");
            Assert.IsTrue(d.Down);
            Assert.AreEqual("2.5", d.UnitsText);
            StringAssert.Contains("-$25.00", d.ShareText());
            StringAssert.Contains("-25.00%", d.ShareText());
            Assert.IsNull(PnlCardData.From(new PositionRow { Units = "1", CostBasisCents = 0, UnrealizedCents = 5 }, "X").ReturnBps);
            Assert.AreEqual(long.MaxValue / 10000 * 10000 / 1, PnlCardData.From(new PositionRow { Units = "1", CostBasisCents = 1, UnrealizedCents = long.MaxValue / 10000 }, "X").ReturnBps, "no overflow");
        }

        [Test]
        public void RektCard()
        {
            var m = RektCardModel.From("p1", "c1", 72000, id => id == "c1" ? "FROGE" : null);
            Assert.AreEqual("FROGE", m.Ticker);
            Assert.AreEqual("-$720.00", m.LossText);
            Assert.AreEqual("-$720.00", RektCardModel.From("p1", "c1", -72000, null).LossText, "sign-agnostic");
            Assert.IsNull(RektCardModel.From("p1", "c2", 1, _ => throw new InvalidOperationException()).Ticker);
        }

        [Test]
        public void OreView_TimerTierHardHat()
        {
            Assert.AreEqual(30.0, OreView.SecondsLeft(1_000_030_000, 1_000_000_000).Value, 1e-9);
            Assert.AreEqual(0.0, OreView.SecondsLeft(1_000_000_000, 1_000_030_000).Value, 1e-9);
            Assert.IsNull(OreView.SecondsLeft(null, 0));
            var ore = JToken.Parse(@"{""minerTiers"":[{""id"":""none"",""label"":""None"",""hardhat"":false},{""id"":""miner"",""label"":""Miner"",""hardhat"":true}]}");
            Assert.AreEqual("Miner", OreView.TierLabel(ore, "miner"));
            Assert.IsTrue(OreView.HardHat(ore, "miner"));
            Assert.IsFalse(OreView.HardHat(ore, "none"));
            Assert.AreEqual("Refiner", OreView.TierLabel(ore, "refiner"));
            Assert.AreEqual(-1, OreView.Busiest(new[] { "0", "0" }));
            Assert.AreEqual("1.5", OreView.Sol("1500000000"));
        }

        [Test]
        public void PollGate_IntervalAndBackoff()
        {
            var g = new PollGate(2, 30);
            Assert.IsTrue(g.Due(0));
            g.Start();
            Assert.IsFalse(g.Due(100), "never two in flight");
            g.Succeeded(10);
            Assert.IsFalse(g.Due(11.9));
            Assert.IsTrue(g.Due(12));
            g.Start();
            g.Failed(12);
            Assert.AreEqual(16, g.NextAt, 1e-9, "2 s × 2^1");
            g.Start();
            g.Failed(16);
            Assert.AreEqual(24, g.NextAt, 1e-9, "2 s × 2^2");
            for (int i = 0; i < 10; i++) { g.Start(); g.Failed(100); }
            Assert.AreEqual(130, g.NextAt, 1e-9, "capped at 30 s");
            g.Kick();
            Assert.IsTrue(g.Due(100));
            Assert.AreEqual(90, g.Age(100), 1e-9);
        }
    }
}
