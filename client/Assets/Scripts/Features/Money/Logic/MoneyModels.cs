using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.Economy
{
    // Client-side records for the Phase 2 REST payloads. Every amount is the server's value; nothing here is derived.
    // Parsers accept the contract shape (openapi.yaml rc1) and the observed backend variants listed in Wire.

    public sealed class MarketCoin
    {
        public string CoinId, Ticker, Name, Stage;
        public long PriceMicros, McapCents, Vol5mCents;
        public int Change5mBps, Holders;
        public long? LaunchedAtMs;
    }

    public sealed class CoinPage
    {
        public List<MarketCoin> Coins = new List<MarketCoin>();
        public string NextCursor;
        public int Skipped;
    }

    public sealed class CandleRow
    {
        public long OpenMs, Open, High, Low, Close;
        public long? Volume;
    }

    public sealed class HolderRow
    {
        public string Alias, Units;
        /// <summary>Billionths of total supply (contract `pct`); null when the server omits it.</summary>
        public long? PctBillionths;
    }

    public sealed class CoinDetail
    {
        public MarketCoin Coin;
        public List<CandleRow> Candles = new List<CandleRow>();
        public List<HolderRow> Holders = new List<HolderRow>();
        public string RiskHint;
    }

    public sealed class TradeReceipt
    {
        public string TradeId, Units, Side, CoinId;
        public long PriceMicros, CashCents;
        public long? FeeCents, RealizedCents;
    }

    public sealed class PositionRow
    {
        public string PositionId, CoinId, Ticker, Units;
        public long CostBasisCents, UnrealizedCents;
        public long? CurrentPriceMicros, RealizedCents;
    }

    public sealed class PerpRow
    {
        public string PerpId, CoinId, Ticker, Side, Status;
        public int Leverage;
        public long MarginCents, EntryMicros, LiqMicros;
        public long? RealizedCents, ClosedAtMs;
        /// <summary>Open positions (backend eeef49d): the price liquidation reads now, and the PnL if closed at it.</summary>
        public long? MarkMicros, UnrealizedPnlCents;
    }

    /// <summary>POST /bank/repay → what was actually paid (capped at the debt) and the balances after.</summary>
    public sealed class RepayReceipt
    {
        public long RepaidCents, CashCents, DebtCents;
    }

    /// <summary>One Cash ledger row: signed amount (credits +, debits −), the server's reason code and its label.</summary>
    public sealed class LedgerItem
    {
        public string Id, Kind, Label;
        public long AmountCents;
        public long? AtMs;
    }

    /// <summary>GET /ledger page, newest first; <see cref="Next"/> is passed back as `before` (null on the last page).</summary>
    public sealed class LedgerPage
    {
        public Rows<LedgerItem> Rows = new Rows<LedgerItem>();
        public string Next;
    }

    public sealed class PredictionOutcome
    {
        public string Id, Label;
    }

    public sealed class PredictionRow
    {
        public string Id, Question, Status;
        public List<PredictionOutcome> Outcomes = new List<PredictionOutcome>();
        public long? ClosesAtMs;
    }

    public sealed class BetReceipt
    {
        public string BetId, PredictionId, OutcomeId, Status, Horizon;
        public long StakeCents;
        public long? ClosesAtMs, CreatedAtMs;
    }

    public sealed class AlphaCallRow
    {
        public string CallId, UserId, CoinId, Status;
        public long EntryMicros, TargetMicros, StopMicros;
        public long? AtMs;
    }

    public sealed class TrustScoreRow
    {
        public double HitRate;
        public long AvgReturnBps, MaxDrawdownBps, HeldBagPct;
    }

    public sealed class AlphaCallsResult
    {
        public List<AlphaCallRow> Calls = new List<AlphaCallRow>();
        public TrustScoreRow Trust;
        public int Skipped;
    }

    public sealed class SmartMoneyRow
    {
        public string UserId, Alias, CoinId, Ticker, Side;
        public long CashCents;
        public long? AtMs;
    }

    public sealed class TombstoneRow
    {
        public string UserId, Alias, CoinId, Ticker, Reason;
        public long LossCents;
        public long? AtMs;
    }

    public sealed class PaymentIntentRow
    {
        public string IntentId, Currency, Recipient, Mint, Amount, Reference, Memo, Tx, SolanaPayUrl;
        /// <summary>Set for top-ups only (contract). The client shows it; it never computes it.</summary>
        public long? CashCents;
        public int? BonusBps;
        public long? ExpiresAtMs;
        public bool HasTx => !string.IsNullOrEmpty(Tx);
    }

    public sealed class IntentStatusRow
    {
        public string Status, Signature;
    }

    public sealed class OreBoardRow
    {
        public string RoundId, MotherlodeOre, CityStakedOre, PriceUsd;
        public long? EndsAtMs, EndsAtSlot;
        /// <summary>Deployed lamports per square (contract: 25). Kept as base-unit strings.</summary>
        public List<string> Squares = new List<string>();
    }

    public sealed class OreMeRow
    {
        public string Balance, Staked, StOre, MinerTier;
    }

    /// <summary>A list plus the number of rows that could not be read (shown, never silently dropped).</summary>
    public sealed class Rows<T>
    {
        public List<T> Items = new List<T>();
        public int Skipped;
    }

    public static class MoneyParse
    {
        private static readonly HashSet<string> Stages = new HashSet<string> { "new", "final", "graduated", "dead" };

        public static MarketCoin Coin(JToken c)
        {
            if (!(c is JObject)) throw new FormatException("Missing coin");
            var stage = Wire.ReqStr(c["stage"], "stage");
            if (!Stages.Contains(stage)) throw new FormatException("Unknown stage");
            var ticker = Wire.Plain(c["ticker"], 16);
            if (string.IsNullOrWhiteSpace(ticker)) throw new FormatException("Missing ticker");
            return new MarketCoin
            {
                CoinId = Wire.SafeId(c["coinId"], "coinId"),
                Ticker = ticker,
                Name = Wire.Plain(c["name"], 48),
                Stage = stage,
                PriceMicros = Wire.Long(c["priceMicros"]),
                McapCents = Wire.LongOr(c["mcapCents"], 0),
                Vol5mCents = Wire.LongOr(c["vol5mCents"], 0),
                Change5mBps = (int)Wire.LongOr(c["change5mBps"], 0),
                Holders = (int)Wire.LongOr(c["holders"], 0),
                LaunchedAtMs = SafeTime(c["launchedAt"]),
            };
        }

        public static CoinPage Coins(JToken t)
        {
            var page = new CoinPage();
            foreach (var row in Wire.List(t, "coins"))
            {
                try { page.Coins.Add(Coin(row)); }
                catch (Exception e) when (e is FormatException || e is OverflowException) { page.Skipped++; }
            }
            page.NextCursor = t is JObject o ? Wire.Str(o["nextCursor"]) : null;
            return page;
        }

        /// <summary>
        /// Contract: `{coin, candles1m: Candle[], topHolders: Holder[], riskHint: string}`. Backend 9f3e5ef:
        /// `candles1m: [{at, priceMicros}]`, `topHolders: [{alias, units}]`, `riskHint: {level, reason?}`.
        /// A price point becomes a flat candle (open = high = low = close) for the chart.
        /// </summary>
        public static CoinDetail Detail(JToken t)
        {
            var d = new CoinDetail { Coin = Coin(t?["coin"]) };
            if (t["candles1m"] is JArray candles)
                foreach (var k in candles)
                {
                    try
                    {
                        if (!Wire.IsNull(k["close"]))
                            d.Candles.Add(new CandleRow
                            {
                                OpenMs = Wire.TimeMs(k["openTime"]) ?? 0, Open = Wire.Long(k["open"]), High = Wire.Long(k["high"]),
                                Low = Wire.Long(k["low"]), Close = Wire.Long(k["close"]), Volume = Wire.OptLong(k["volume"]),
                            });
                        else
                        {
                            var p = Wire.Long(k["priceMicros"]);
                            d.Candles.Add(new CandleRow { OpenMs = Wire.TimeMs(k["at"]) ?? 0, Open = p, High = p, Low = p, Close = p });
                        }
                    }
                    catch (FormatException) { }
                }
            d.Candles = d.Candles.OrderBy(c => c.OpenMs).ToList();
            if (t["topHolders"] is JArray holders)
                foreach (var h in holders)
                {
                    try
                    {
                        d.Holders.Add(new HolderRow { Alias = Wire.Plain(h["alias"], 32), Units = Wire.Units(h["units"]), PctBillionths = Wire.OptLong(h["pct"]) });
                    }
                    catch (FormatException) { }
                }
            d.RiskHint = RiskText(t["riskHint"]);
            return d;
        }

        private static string RiskText(JToken risk)
        {
            if (!(risk is JObject o)) return Wire.Plain(risk, 200);
            var reason = Wire.Plain(o["reason"], 200);
            return reason.Length > 0 ? reason : Wire.Plain(o["level"], 40);
        }

        /// <summary>
        /// Contract `Trade {tradeId, units, priceMicros, feeCents, cashCents}`. Backend 9f3e5ef returns
        /// `{tradeId, coinId, side, units, priceMicros, cashDeltaCents, realizedCents, newUnits}` (no fee); the Cash moved
        /// is shown as |cashDeltaCents|.
        /// </summary>
        public static TradeReceipt Trade(JToken t)
        {
            if (!(t is JObject)) throw new FormatException("Missing trade receipt");
            long cash = !Wire.IsNull(t["cashCents"]) ? Wire.Long(t["cashCents"]) : Math.Abs(Wire.Long(t["cashDeltaCents"]));
            return new TradeReceipt
            {
                TradeId = Wire.ReqStr(t["tradeId"], "tradeId"),
                Units = Wire.Units(t["units"]),
                PriceMicros = Wire.Long(t["priceMicros"]),
                CashCents = cash,
                FeeCents = Wire.OptLong(t["feeCents"]),
                RealizedCents = Wire.OptLong(t["realizedCents"]),
                Side = Wire.Str(t["side"]),
                CoinId = Wire.Str(t["coinId"]),
            };
        }

        public static Rows<PositionRow> Positions(JToken t) => Many(Wire.List(t, "positions"), p => new PositionRow
        {
            PositionId = Wire.Str(p["positionId"]),
            CoinId = Wire.SafeId(p["coinId"], "coinId"),
            Ticker = Wire.Plain(p["ticker"], 16),
            Units = Wire.Units(p["units"]),
            CostBasisCents = Wire.Long(p["costBasisCents"]),
            UnrealizedCents = Wire.Long(p["unrealizedCents"]),
            CurrentPriceMicros = Wire.OptLong(p["currentPriceMicros"]),
            RealizedCents = Wire.OptLong(p["realizedCents"]),
        });

        public static PerpRow Perp(JToken p)
        {
            if (!(p is JObject)) throw new FormatException("Missing perp");
            var side = Wire.ReqStr(p["side"], "side");
            var status = Wire.ReqStr(p["status"], "status");
            if (side != "long" && side != "short") throw new FormatException("Unknown side");
            if (status != "open" && status != "closed" && status != "liquidated") throw new FormatException("Unknown status");
            return new PerpRow
            {
                PerpId = Wire.SafeId(p["perpId"], "perpId"),
                CoinId = Wire.SafeId(p["coinId"], "coinId"),
                Ticker = Wire.Plain(p["ticker"], 16),
                Side = side,
                Status = status,
                Leverage = Wire.Int(p["leverage"]),
                MarginCents = Wire.Long(p["marginCents"]),
                EntryMicros = Wire.Long(p["entryMicros"]),
                LiqMicros = Wire.Long(p["liqMicros"]),
                RealizedCents = Wire.OptLong(p["realizedCents"]),
                ClosedAtMs = SafeTime(p["closedAt"]),
                MarkMicros = Wire.OptLong(p["markMicros"]),
                UnrealizedPnlCents = Wire.OptLong(p["unrealizedPnlCents"]),
            };
        }

        public static RepayReceipt Repay(JToken t)
        {
            if (!(t is JObject)) throw new FormatException("Missing repay receipt");
            return new RepayReceipt
            {
                RepaidCents = Wire.Long(t["repaidCents"]),
                CashCents = Wire.Long(t["cashCents"]),
                DebtCents = Math.Max(0, Wire.Long(t["debtCents"])),
            };
        }

        public static LedgerItem LedgerRow(JToken t)
        {
            if (!(t is JObject)) throw new FormatException("Missing ledger row");
            var id = Wire.ReqStr(t["id"], "id");
            if (id.Length > 64) throw new FormatException("Bad id");
            return new LedgerItem
            {
                Id = id,
                Kind = Wire.Plain(t["kind"], 32) ?? "",
                Label = Wire.Plain(t["label"], 64),
                AmountCents = Wire.Long(t["amountCents"]),
                AtMs = SafeTime(t["at"]),
            };
        }

        public static LedgerPage Ledger(JToken t)
        {
            var page = new LedgerPage { Rows = Many(Wire.List(t, "items"), LedgerRow) };
            var next = Wire.Str(t?["next"]);
            // a page cursor is a plain number (the contract's `before` pattern); anything else ends paging
            page.Next = !string.IsNullOrEmpty(next) && next.Length <= 24 && ulong.TryParse(next, out _) ? next : null;
            return page;
        }

        public static Rows<PerpRow> Perps(JToken t) => Many(Wire.List(t, "perps"), Perp);

        /// <summary>Contract `PredictionMarket {id, question, outcomes[{id,label}], closesAt, status}`.</summary>
        public static Rows<PredictionRow> Predictions(JToken t) => Many(Wire.List(t, "predictions", "markets"), p =>
        {
            var row = new PredictionRow
            {
                Id = Wire.SafeId(p["id"], "id"),
                Question = Wire.Plain(p["question"], 200),
                Status = Wire.Str(p["status"]) ?? "open",
                ClosesAtMs = Wire.TimeMs(p["closesAt"]),
            };
            if (string.IsNullOrWhiteSpace(row.Question)) throw new FormatException("Missing question");
            if (!(p["outcomes"] is JArray outs) || outs.Count == 0) throw new FormatException("Missing outcomes");
            foreach (var o in outs)
                row.Outcomes.Add(new PredictionOutcome { Id = Wire.ReqStr(o["id"], "outcome.id"), Label = Wire.Plain(o["label"] ?? o["id"], 60) });
            return row;
        });

        public static BetReceipt Bet(JToken t)
        {
            if (!(t is JObject)) throw new FormatException("Missing bet receipt");
            return new BetReceipt
            {
                BetId = Wire.Str(t["betId"]) ?? Wire.ReqStr(t["predictionId"], "betId"),
                PredictionId = Wire.Str(t["predictionId"]),
                OutcomeId = Wire.Str(t["outcomeId"]),
                StakeCents = Wire.Long(t["stakeCents"]),
            };
        }

        /// <summary>Backend GET /predictions: persisted personal bets, including resolved bets.</summary>
        public static Rows<BetReceipt> PredictionHistory(JToken t) => Many(Wire.List(t, "predictions"), p =>
        {
            if (!(p is JObject)) throw new FormatException("Missing prediction");
            var status = Wire.ReqStr(p["status"], "status");
            if (status != "open" && status != "won" && status != "lost" && status != "cancelled")
                throw new FormatException("Unknown prediction status");
            var stake = Wire.Long(p["stakeCents"]);
            if (stake <= 0) throw new FormatException("Invalid prediction stake");
            var id = Wire.SafeId(p["predictionId"], "predictionId");
            return new BetReceipt
            {
                BetId = id, PredictionId = id, OutcomeId = Wire.ReqStr(p["outcomeId"], "outcomeId"),
                StakeCents = stake, Status = status, Horizon = Wire.Plain(p["horizon"], 16),
                ClosesAtMs = Wire.TimeMs(p["closesAt"]), CreatedAtMs = SafeTime(p["createdAt"]),
            };
        });

        public static AlphaCallRow AlphaCall(JToken c)
        {
            if (!(c is JObject)) throw new FormatException("Missing call");
            return new AlphaCallRow
            {
                CallId = Wire.ReqStr(c["callId"], "callId"),
                UserId = Wire.Str(c["userId"]),
                CoinId = Wire.SafeId(c["coinId"], "coinId"),
                EntryMicros = Wire.Long(c["entryMicros"]),
                TargetMicros = Wire.Long(c["targetMicros"]),
                StopMicros = Wire.Long(c["stopMicros"]),
                AtMs = SafeTime(c["at"] ?? c["createdAt"]),
                Status = Wire.Str(c["status"]),
            };
        }

        public static AlphaCallsResult AlphaCalls(JToken t)
        {
            if (!(t is JObject)) throw new FormatException("Missing calls");
            var rows = Many(Wire.List(t["calls"]), AlphaCall);
            var ts = t["trustScore"];
            TrustScoreRow trust = null;
            if (ts is JObject)
                trust = new TrustScoreRow
                {
                    HitRate = ts["hitRate"]?.Type == JTokenType.Float || ts["hitRate"]?.Type == JTokenType.Integer ? (double)ts["hitRate"] : double.NaN,
                    AvgReturnBps = Wire.LongOr(ts["avgReturnBps"], 0),
                    MaxDrawdownBps = Wire.LongOr(ts["maxDrawdownBps"], 0),
                    HeldBagPct = Wire.LongOr(ts["heldBagPct"], 0),
                };
            return new AlphaCallsResult { Calls = rows.Items, Skipped = rows.Skipped, Trust = trust };
        }

        /// <summary>Contract: `SmartMoneyTrade[]`. Backend: `{trades: [...]}` with a signed ledger delta in `cashCents`.</summary>
        public static Rows<SmartMoneyRow> SmartMoney(JToken t) => Many(Wire.List(t, "trades"), s =>
        {
            var side = Wire.ReqStr(s["side"], "side");
            if (side != "buy" && side != "sell") throw new FormatException("Unknown side");
            return new SmartMoneyRow
            {
                UserId = Wire.SafeId(s["userId"], "userId"),
                Alias = Wire.Plain(s["alias"], 32),
                CoinId = Wire.SafeId(s["coinId"], "coinId"),
                Ticker = Wire.Plain(s["ticker"], 16),
                Side = side,
                CashCents = Math.Abs(Wire.Long(s["cashCents"])),
                AtMs = SafeTime(s["at"]),
            };
        });

        /// <summary>Contract: `[{userId, alias, lossCents, coinId, at}]`. Backend: `{tombstones: [{coinId, ticker, alias, lostCents, reason, at}]}`.</summary>
        public static Rows<TombstoneRow> Tombstones(JToken t) => Many(Wire.List(t, "tombstones"), s => new TombstoneRow
        {
            UserId = Wire.Str(s["userId"]),
            Alias = Wire.Plain(s["alias"], 32),
            CoinId = Wire.Str(s["coinId"]),
            Ticker = Wire.Plain(s["ticker"], 16),
            Reason = Wire.Plain(s["reason"], 24),
            LossCents = Math.Abs(Wire.Long(!Wire.IsNull(s["lossCents"]) ? s["lossCents"] : s["lostCents"])),
            AtMs = SafeTime(s["at"]),
        });

        /// <summary>
        /// Contract `PaymentIntent`. HasTx is checked before signing, including when an older/degraded server omits it.
        /// `amount` must be base units; anything else is kept
        /// raw and displayed as such.
        /// </summary>
        public static PaymentIntentRow Intent(JToken t)
        {
            if (!(t is JObject)) throw new FormatException("Missing payment intent");
            return new PaymentIntentRow
            {
                IntentId = Wire.SafeId(t["intentId"], "intentId"),
                Currency = Wire.Str(t["currency"]),
                Recipient = Wire.Str(t["recipient"]),
                Mint = Wire.Str(t["mint"]),
                Amount = Wire.Str(t["amount"]),
                CashCents = Wire.OptLong(t["cashCents"]),
                BonusBps = Wire.OptInt(t["bonusBps"]),
                Reference = Wire.Str(t["reference"]),
                Memo = Wire.Str(t["memo"]),
                Tx = Wire.Str(t["tx"]),
                SolanaPayUrl = Wire.Str(t["solanaPayUrl"]),
                ExpiresAtMs = SafeTime(t["expiresAt"]),
            };
        }

        public static IntentStatusRow IntentStatus(JToken t)
        {
            if (!(t is JObject)) throw new FormatException("Missing status");
            return new IntentStatusRow { Status = Wire.ReqStr(t["status"], "status"), Signature = Wire.Str(t["signature"]) };
        }

        /// <summary>
        /// Contract `OreBoard {roundId:int, endsAt:ISO, endsAtSlot, squares[25]{deployedLamports}, motherlodeOre, cityStakedOre, priceUsd:string}`.
        /// Backend 9f3e5ef: `roundId` string, `endsAt` epoch ms, `squares` 5 plain numbers, `priceUsd` number.
        /// </summary>
        public static OreBoardRow OreBoard(JToken t)
        {
            if (!(t is JObject)) throw new FormatException("Missing board");
            var b = new OreBoardRow
            {
                RoundId = Wire.Str(t["roundId"]) ?? "—",
                EndsAtMs = Wire.TimeMs(t["endsAt"]),
                EndsAtSlot = Wire.OptLong(t["endsAtSlot"]),
                MotherlodeOre = Wire.OptUnits(t["motherlodeOre"]),
                CityStakedOre = Wire.OptUnits(t["cityStakedOre"]),
                PriceUsd = Wire.Str(t["priceUsd"]),
            };
            if (t["squares"] is JArray squares)
                foreach (var s in squares)
                    b.Squares.Add(s is JObject so ? Wire.Units(so["deployedLamports"]) : Wire.Units(s));
            return b;
        }

        public static OreMeRow OreMe(JToken t)
        {
            if (!(t is JObject)) throw new FormatException("Missing ORE position");
            return new OreMeRow
            {
                Balance = Wire.Units(t["balance"]),
                Staked = Wire.Units(t["staked"]),
                StOre = Wire.Units(t["stOre"]),
                MinerTier = Wire.Str(t["minerTier"]) ?? "none",
            };
        }

        /// <summary>Contract `{tx}` (base64 unsigned v0 transaction), with legacy txBase64 compatibility.</summary>
        public static string StakeTx(JToken t)
        {
            var tx = Wire.Str(t?["tx"]) ?? Wire.Str(t?["txBase64"]);
            if (string.IsNullOrEmpty(tx)) throw new FormatException("Missing transaction");
            return tx;
        }

        private static long? SafeTime(JToken t)
        {
            try { return Wire.TimeMs(t); }
            catch (FormatException) { return null; }
        }

        private static Rows<T> Many<T>(JArray list, Func<JToken, T> parse)
        {
            var rows = new Rows<T>();
            foreach (var item in list)
            {
                try { rows.Items.Add(parse(item)); }
                catch (Exception e) when (e is FormatException || e is OverflowException || e is InvalidCastException || e is ArgumentException) { rows.Skipped++; }
            }
            return rows;
        }
    }
}
