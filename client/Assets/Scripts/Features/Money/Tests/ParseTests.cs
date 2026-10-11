using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.Economy.Tests
{
    /// <summary>
    /// Every parser against (a) the contract shape/example in contract/openapi.yaml rc1 and (b) the shape the backend
    /// checkout (ct-life-backend 9f3e5ef) actually sends, so the client works with both and the differences are pinned.
    /// </summary>
    public sealed class WireTests
    {
        [Test]
        public void Long_AcceptsIntegersAndDigitStrings_Only()
        {
            Assert.AreEqual(100000, Wire.Long(JToken.Parse("100000")));
            Assert.AreEqual(-42, Wire.Long(new JValue("-42")));
            Assert.AreEqual(9007199254740993L, Wire.Long(new JValue("9007199254740993")), "bigint strings stay exact");
            Assert.Throws<FormatException>(() => Wire.Long(JToken.Parse("1.5")));
            Assert.Throws<FormatException>(() => Wire.Long(new JValue("12abc")));
            Assert.Throws<FormatException>(() => Wire.Long(null));
            Assert.Throws<FormatException>(() => Wire.Long(JToken.Parse("99999999999999999999")));
            Assert.IsNull(Wire.OptLong(JValue.CreateNull()));
            Assert.AreEqual(7, Wire.LongOr(new JValue("x"), 7));
        }

        [Test]
        public void TimeMs_AcceptsIsoAndEpochMs()
        {
            Assert.AreEqual(1759665900000L, Wire.TimeMs(new JValue("2025-10-05T12:05:00.000Z")));
            Assert.AreEqual(1759665900000L, Wire.TimeMs(JToken.Parse("1759665900000")));
            Assert.IsNull(Wire.TimeMs(null));
            Assert.AreEqual(1759665900000L, Wire.TimeMs(JToken.Parse("\"2025-10-05T12:05:00.000Z\"")), "Date token from default Newtonsoft parsing");
            Assert.Throws<FormatException>(() => Wire.TimeMs(new JValue("yesterday")));
        }

        [Test]
        public void List_AcceptsBareArraysAndNamedWrappers()
        {
            Assert.AreEqual(2, Wire.List(JToken.Parse("[1,2]"), "positions").Count);
            Assert.AreEqual(1, Wire.List(JToken.Parse("{\"positions\":[1]}"), "positions").Count);
            Assert.Throws<FormatException>(() => Wire.List(JToken.Parse("{\"other\":[1]}"), "positions"));
        }

        [Test]
        public void SafeIds()
        {
            Assert.IsTrue(Wire.IsSafeId("0192a1c2-0000-7000-8000-000000000001"));
            Assert.IsTrue(Wire.IsSafeId("trade_0192"));
            Assert.IsFalse(Wire.IsSafeId("../admin"));
            Assert.IsFalse(Wire.IsSafeId("a b"));
            Assert.IsFalse(Wire.IsSafeId(""));
        }
    }

    public sealed class ParseTests
    {
        private const string CoinId = "0192a1c2-1111-7000-8000-000000000001";

        /// <summary>Parsed like RestClient does (Json.ParseRaw: ISO times stay strings).</summary>
        private static JToken J(string json) => CTLife.Core.Json.ParseRaw(json.Replace('\'', '"'));

        [Test]
        public void Coins_ContractIntegersAndBackendStrings()
        {
            var contract = MoneyParse.Coins(J("{'coins':[{'coinId':'" + CoinId + "','ticker':'FROGE','name':'Froge','stage':'new','priceMicros':15000,'mcapCents':500000,'vol5mCents':1000,'change5mBps':-310,'holders':12,'launchedAt':'2026-10-05T12:00:00Z'}],'nextCursor':null}"));
            Assert.AreEqual(1, contract.Coins.Count);
            Assert.AreEqual(15000, contract.Coins[0].PriceMicros);
            Assert.AreEqual(-310, contract.Coins[0].Change5mBps);
            Assert.IsNull(contract.NextCursor);

            var backend = MoneyParse.Coins(J("{'coins':[{'coinId':'" + CoinId + "','ticker':'FROGE','name':'Froge','stage':'final','priceMicros':'15000','mcapCents':'500000','vol5mCents':'0','change5mBps':0,'holders':3,'launchedAt':'2026-10-05T12:00:00.000Z'},{'coinId':'bad id','ticker':'X','stage':'new','priceMicros':1}],'nextCursor':'FROGE'}"));
            Assert.AreEqual(1, backend.Coins.Count);
            Assert.AreEqual(1, backend.Skipped, "a malformed row is counted, not silently dropped");
            Assert.AreEqual(500000, backend.Coins[0].McapCents);
            Assert.AreEqual("FROGE", backend.NextCursor);
        }

        [Test]
        public void CoinDetail_ContractCandlesAndBackendPricePoints()
        {
            var contract = MoneyParse.Detail(J("{'coin':{'coinId':'" + CoinId + "','ticker':'FROGE','name':'F','stage':'new','priceMicros':15000,'mcapCents':1,'vol5mCents':1,'change5mBps':0,'holders':1,'launchedAt':'2026-10-05T12:00:00Z'},'candles1m':[{'openTime':'2026-10-05T12:01:00Z','open':1,'high':5,'low':1,'close':4,'volume':10},{'openTime':'2026-10-05T12:00:00Z','open':1,'high':2,'low':1,'close':1,'volume':3}],'topHolders':[{'userId':'u1','alias':'anon','units':'1000000','pct':500000000}],'riskHint':'DYOR'}"));
            Assert.AreEqual(2, contract.Candles.Count);
            Assert.AreEqual(1, contract.Candles[0].Close, "sorted by time");
            Assert.AreEqual(500000000, contract.Holders[0].PctBillionths);
            Assert.AreEqual("DYOR", contract.RiskHint);

            var backend = MoneyParse.Detail(J("{'coin':{'coinId':'" + CoinId + "','ticker':'FROGE','name':'F','stage':'new','priceMicros':'15000','mcapCents':'1','vol5mCents':'1','change5mBps':0,'holders':1,'launchedAt':'2026-10-05T12:00:00Z'},'candles1m':[{'at':1759665600000,'priceMicros':'14000'}],'topHolders':[{'alias':'anon','units':'5'}],'riskHint':{'level':'hidden','reason':'Reach security 5 to unlock the risk read-out.'}}"));
            Assert.AreEqual(14000, backend.Candles[0].Close);
            Assert.AreEqual(14000, backend.Candles[0].High);
            Assert.IsNull(backend.Holders[0].PctBillionths);
            Assert.AreEqual("Reach security 5 to unlock the risk read-out.", backend.RiskHint);
        }

        [Test]
        public void Trade_ContractExampleAndBackendReceipt()
        {
            var contract = MoneyParse.Trade(J("{'tradeId':'trade_0192a1c2-...','units':'1000000','priceMicros':15000,'feeCents':100,'cashCents':100000}"));
            Assert.AreEqual(100000, contract.CashCents);
            Assert.AreEqual(100, contract.FeeCents);
            var backend = MoneyParse.Trade(J("{'tradeId':'p1','coinId':'" + CoinId + "','side':'buy','units':'3324','priceMicros':'30066','cashDeltaCents':'-100000','realizedCents':'0','newUnits':'3324'}"));
            Assert.AreEqual(100000, backend.CashCents, "|cashDeltaCents|");
            Assert.IsNull(backend.FeeCents);
            Assert.Throws<FormatException>(() => MoneyParse.Trade(J("{'tradeId':'x','units':'1.5','priceMicros':1,'cashCents':1}")));
        }

        [Test]
        public void Positions_ArrayOrWrapped()
        {
            var contract = MoneyParse.Positions(J("[{'positionId':'p1','coinId':'" + CoinId + "','units':'1000000','costBasisCents':1000,'unrealizedCents':-250}]"));
            Assert.AreEqual(-250, contract.Items[0].UnrealizedCents);
            var backend = MoneyParse.Positions(J("{'positions':[{'coinId':'" + CoinId + "','ticker':'FROGE','units':'1000000','costBasisCents':'1000','realizedCents':'0','currentPriceMicros':'900','unrealizedCents':'-100'}]}"));
            Assert.AreEqual("FROGE", backend.Items[0].Ticker);
            Assert.AreEqual(900, backend.Items[0].CurrentPriceMicros);
        }

        [Test]
        public void Perps_ContractAndBackend()
        {
            var one = MoneyParse.Perp(J("{'perpId':'0192a1c2-2222-7000-8000-000000000001','coinId':'" + CoinId + "','side':'long','leverage':10,'marginCents':5000,'entryMicros':15000,'liqMicros':13500,'status':'open'}"));
            Assert.AreEqual(13500, one.LiqMicros);
            Assert.IsNull(one.RealizedCents);
            var list = MoneyParse.Perps(J("{'perps':[{'perpId':'a1','coinId':'" + CoinId + "','ticker':'FROGE','side':'short','leverage':2,'marginCents':'5000','entryMicros':'15000','liqMicros':'22500','status':'liquidated','realizedCents':'-5000','closedAt':'2026-10-05T12:00:00.000Z','createdAt':''},{'perpId':'a2','side':'sideways'}]}"));
            Assert.AreEqual(1, list.Items.Count);
            Assert.AreEqual(1, list.Skipped);
            Assert.AreEqual("liquidated", list.Items[0].Status);
            Assert.AreEqual(-5000, list.Items[0].RealizedCents);
        }

        [Test]
        public void Predictions_ContractShape()
        {
            var rows = MoneyParse.Predictions(J("[{'id':'0192a1c2-3333-7000-8000-000000000001','question':'Will $FROGE graduate in 15m?','outcomes':[{'id':'yes','label':'Yes'},{'id':'no','label':'No'}],'closesAt':'2026-10-05T12:15:00Z','status':'open'}]"));
            Assert.AreEqual(1, rows.Items.Count);
            Assert.AreEqual(2, rows.Items[0].Outcomes.Count);
            // The backend checkout lists the player's own bets here instead of markets: those rows are counted as unreadable.
            var backend = MoneyParse.Predictions(J("{'predictions':[{'predictionId':'p','outcomeId':'yes','stakeCents':'100','status':'open','horizon':'5m','closesAt':'2026-10-05T12:15:00Z'}]}"));
            Assert.AreEqual(0, backend.Items.Count);
            Assert.AreEqual(1, backend.Skipped);
            var bet = MoneyParse.Bet(J("{'predictionId':'p','outcomeId':'yes','stakeCents':'100','status':'open'}"));
            Assert.AreEqual(100, bet.StakeCents);
        }

        [Test]
        public void AlphaCalls_SmartMoney_Tombstones()
        {
            var calls = MoneyParse.AlphaCalls(J("{'calls':[{'callId':'c1','userId':'u','coinId':'" + CoinId + "','entryMicros':'15000','targetMicros':'30000','stopMicros':'10000','status':'open','createdAt':'2026-10-05T12:00:00Z'}],'trustScore':{'hitRate':0.5,'avgReturnBps':120,'maxDrawdownBps':800,'heldBagPct':10}}"));
            Assert.AreEqual(30000, calls.Calls[0].TargetMicros);
            Assert.AreEqual(0.5, calls.Trust.HitRate, 1e-9);
            Assert.IsNotNull(calls.Calls[0].AtMs);

            var smart = MoneyParse.SmartMoney(J("{'trades':[{'userId':'u1','alias':'whale','coinId':'" + CoinId + "','ticker':'FROGE','side':'buy','cashCents':'-250000','at':'2026-10-05T12:00:00Z'}]}"));
            Assert.AreEqual(250000, smart.Items[0].CashCents, "ledger delta shown as an absolute amount");
            var smartContract = MoneyParse.SmartMoney(J("[{'userId':'u1','alias':'whale','coinId':'" + CoinId + "','side':'sell','cashCents':250000,'at':'2026-10-05T12:00:00Z'}]"));
            Assert.AreEqual("sell", smartContract.Items[0].Side);

            var contract = MoneyParse.Tombstones(J("[{'userId':'u1','alias':'rekt','lossCents':72000,'coinId':'" + CoinId + "','at':'2026-10-05T12:00:00Z'}]"));
            Assert.AreEqual(72000, contract.Items[0].LossCents);
            var backend = MoneyParse.Tombstones(J("{'tombstones':[{'coinId':'" + CoinId + "','ticker':'FROGE','alias':'rekt','lostCents':'72000','reason':'rug','at':'2026-10-05T12:00:00Z'}]}"));
            Assert.AreEqual(72000, backend.Items[0].LossCents);
            Assert.AreEqual("rug", backend.Items[0].Reason);
        }

        [Test]
        public void PaymentIntent_ContractExampleAndBackendGaps()
        {
            var contract = MoneyParse.Intent(J("{'intentId':'0192a1c2-4444-7000-8000-000000000001','currency':'USDC','recipient':'Dgw3c4GbQ','mint':'EPjFWdd5','amount':'10000000','cashCents':700000,'bonusBps':0,'reference':'ref_','memo':'m','tx':'base64','solanaPayUrl':'solana:x','expiresAt':'2026-10-05T12:10:00.000Z'}"));
            Assert.IsTrue(contract.HasTx);
            Assert.AreEqual(700000, contract.CashCents);
            Assert.AreEqual("10 USDC", MoneyFormat.Token(contract.Amount, 6, "USDC"));
            Assert.IsNotNull(contract.ExpiresAtMs);

            var backend = MoneyParse.Intent(J("{'intentId':'0192a1c2-4444-7000-8000-000000000002','recipient':'T','mint':null,'amount':'0.000010','reference':'r','memo':'m','solanaPayUrl':'solana:T?amount=0.000010','expiresAt':'2026-10-05T12:10:00.000Z'}"));
            Assert.IsFalse(backend.HasTx, "no tx: the flow refuses to sign");
            Assert.IsNull(backend.CashCents);
            Assert.IsNull(backend.Mint);

            var status = MoneyParse.IntentStatus(J("{'status':'pending','signature':null}"));
            Assert.AreEqual("pending", status.Status);
            Assert.IsNull(status.Signature);
        }

        [Test]
        public void OreBoard_ContractAndBackend()
        {
            var squares = new JArray();
            for (int i = 0; i < 25; i++) squares.Add(new JObject { ["deployedLamports"] = i == 3 ? "2000000000" : "1000000000" });
            var contract = JObject.Parse("{'roundId':1,'endsAt':'2026-10-05T12:05:00.000Z','endsAtSlot':350000000,'motherlodeOre':'55000000000','cityStakedOre':'120000000000','priceUsd':'135.20'}".Replace('\'', '"'));
            contract["squares"] = squares;
            var board = MoneyParse.OreBoard(contract);
            Assert.AreEqual(25, board.Squares.Count);
            Assert.AreEqual("1", board.RoundId);
            Assert.AreEqual("135.20", board.PriceUsd);
            Assert.AreEqual(3, OreView.Busiest(board.Squares));
            Assert.AreEqual("0.55 ORE", OreView.Ore(board.MotherlodeOre));

            var backend = MoneyParse.OreBoard(J("{'roundId':'dev-fixture','endsAt':1759665900000,'endsAtSlot':0,'squares':[1000000000,1000000000,1000000000,1000000000,1000000000],'motherlodeOre':'0','cityStakedOre':'0','priceUsd':0,'at':1759665840000}"));
            Assert.AreEqual(5, backend.Squares.Count);
            Assert.AreEqual(1759665900000L, backend.EndsAtMs);
            Assert.AreEqual("0", backend.PriceUsd);
            Assert.AreEqual(0, OreView.Busiest(backend.Squares), "ties keep the first square");

            var me = MoneyParse.OreMe(J("{'balance':'0','staked':'100000000000','stOre':'0','minerTier':'miner'}"));
            Assert.AreEqual("1 ORE", OreView.Ore(me.Staked));
            Assert.AreEqual("AQID", MoneyParse.StakeTx(J("{'tx':'AQID'}")));
            Assert.AreEqual("AQID", MoneyParse.StakeTx(J("{'txBase64':'AQID','action':'deposit'}")));
            Assert.Throws<FormatException>(() => MoneyParse.StakeTx(J("{}")));
        }
    }
}
