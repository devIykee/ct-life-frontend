using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using UnityEngine;

namespace CTLife.Economy
{
    /// <summary>One `tape` message from the market room: trades of at least config `market.tapeThresholdCents`.</summary>
    public sealed class TapeRow
    {
        public string CoinId, Side, Name;
        public long CashCents;
        public double ReceivedAt;
    }

    /// <summary>
    /// Live coin list for Pulse, Launchpad and the Perp Pit. Source of truth, in order:
    /// 1) the `market` room's `coins` map (contract section 5: 0.5 Hz, about 40 live coins) when joined and non-empty;
    /// 2) otherwise GET /market/coins?stage=… polled while a view asks for it. The backend publishes real coin snapshots
    ///    into the market room; REST also covers startup, disconnection and an empty room.
    /// Also keeps a ticker cache for perps, tombstones, smart money and the REKT card, and the last 30 tape rows.
    /// </summary>
    public sealed class MarketFeed : IDisposable
    {
        public const int TapeKeep = 30;
        private const double RestInterval = 4;

        private readonly MoneyApi _api;
        private readonly Dictionary<string, MarketCoin> _room = new Dictionary<string, MarketCoin>();
        private readonly Dictionary<string, MarketCoin> _rest = new Dictionary<string, MarketCoin>();
        private readonly Dictionary<string, string> _tickers = new Dictionary<string, string>();
        private readonly Dictionary<string, PollGate> _restGates = new Dictionary<string, PollGate>();
        private readonly Dictionary<string, double> _restFetchedAt = new Dictionary<string, double>();
        private readonly LinkedList<TapeRow> _tape = new LinkedList<TapeRow>();
        private readonly Subscriptions _subs = new Subscriptions();
        private MarketChannel _channel;
        private bool _disposed, _changedPending;
        private double _roomUpdatedAt = double.NaN;

        public event Action Changed;
        public event Action<TapeRow> TapeAdded;

        public MarketFeed(MoneyApi api)
        {
            _api = api;
            _subs.On<TapeMessage>(OnTape);
        }

        /// <summary>The market room is joined and has at least one coin.</summary>
        public bool RoomLive => _channel != null && _channel.IsJoined && _room.Count > 0;
        public bool RoomJoined => _channel != null && _channel.IsJoined;
        public IEnumerable<TapeRow> Tape => _tape;
        public string LastError { get; private set; }

        /// <summary>Seconds since the data a view shows was refreshed (room or REST), NaN when never.</summary>
        public double Age(string stage)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (RoomLive) return double.IsNaN(_roomUpdatedAt) ? double.NaN : now - _roomUpdatedAt;
            return _restFetchedAt.TryGetValue(stage ?? "", out var at) ? now - at : double.NaN;
        }

        public string Ticker(string coinId)
        {
            if (string.IsNullOrEmpty(coinId)) return null;
            return _tickers.TryGetValue(coinId, out var t) ? t : null;
        }

        public MarketCoin Find(string coinId)
        {
            if (string.IsNullOrEmpty(coinId)) return null;
            if (RoomLive && _room.TryGetValue(coinId, out var c)) return c;
            return _rest.TryGetValue(coinId, out var r) ? r : (_room.TryGetValue(coinId, out var rc) ? rc : null);
        }

        public void Remember(string coinId, string ticker)
        {
            if (!string.IsNullOrEmpty(coinId) && !string.IsNullOrEmpty(ticker)) _tickers[coinId] = ticker;
        }

        public void Remember(MarketCoin c)
        {
            if (c == null) return;
            _rest[c.CoinId] = c;
            Remember(c.CoinId, c.Ticker);
        }

        /// <summary>Coins in a stage (null = every non-dead stage), newest launch first.</summary>
        public List<MarketCoin> List(string stage)
        {
            IEnumerable<MarketCoin> src = RoomLive ? _room.Values : _rest.Values;
            return src.Where(c => stage == null ? c.Stage != "dead" : c.Stage == stage)
                .OrderByDescending(c => c.LaunchedAtMs ?? 0).ThenBy(c => c.Ticker, StringComparer.Ordinal).ToList();
        }

        /// <summary>Called by visible views every frame-ish: re-binds the room and polls REST when the room has no coins.</summary>
        public void Pump(IEnumerable<string> stages, CancellationToken ct)
        {
            if (_disposed) return;
            BindRoom();
            if (_changedPending)
            {
                _changedPending = false;
                Raise();
            }
            if (RoomLive) return;
            double now = Time.realtimeSinceStartupAsDouble;
            foreach (var stage in stages)
            {
                var key = stage ?? "";
                if (!_restGates.TryGetValue(key, out var gate)) _restGates[key] = gate = new PollGate(RestInterval, 30);
                if (!gate.Due(now)) continue;
                _ = Fetch(stage, gate, ct);
            }
        }

        /// <summary>Forces the next Pump to refetch (pull to refresh, after a trade).</summary>
        public void Kick()
        {
            foreach (var g in _restGates.Values) g.Kick();
        }

        private async Task Fetch(string stage, PollGate gate, CancellationToken ct)
        {
            gate.Start();
            try
            {
                var seen = new HashSet<string>();
                string cursor = null;
                // The backend pages 50 rows by ticker; three pages cover the ~40 live coins the contract describes.
                for (int page = 0; page < 3; page++)
                {
                    var res = await _api.Coins(stage, cursor, ct);
                    if (_disposed) return;
                    foreach (var c in res.Coins)
                    {
                        seen.Add(c.CoinId);
                        Remember(c);
                    }
                    cursor = res.NextCursor;
                    if (string.IsNullOrEmpty(cursor)) break;
                }
                // Drop coins that left this stage (graduated or died) from the REST cache.
                foreach (var id in _rest.Where(kv => (stage == null || kv.Value.Stage == stage) && !seen.Contains(kv.Key)).Select(kv => kv.Key).ToList())
                    _rest.Remove(id);
                LastError = null;
                _restFetchedAt[stage ?? ""] = Time.realtimeSinceStartupAsDouble;
                gate.Succeeded(Time.realtimeSinceStartupAsDouble);
                Raise();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception e)
            {
                if (_disposed) return;
                gate.Failed(Time.realtimeSinceStartupAsDouble);
                LastError = MoneyErrors.From(e, write: false).Key;
                Log.Warn("money", "Coin list failed: " + (e is ApiException a ? a.Code : e.GetType().Name));
                Raise();
            }
        }

        private void BindRoom()
        {
            IRealtimeService rt = null;
            Services.TryGet(out rt);
            var current = rt?.Market;
            if (ReferenceEquals(current, _channel)) return;
            Unbind();
            _channel = current;
            if (_channel == null) return;
            _channel.CoinAdded += OnCoin;
            _channel.CoinChanged += OnCoin;
            _channel.CoinRemoved += OnCoinRemoved;
            // OnAdd callbacks for coins that existed before this subscription already fired; take a snapshot.
            try
            {
                var coins = _channel.State?.coins;
                if (coins != null)
                    foreach (var kv in coins) OnCoin(kv.Key, kv.Value);
            }
            catch (Exception e) { Log.Warn("money", "Market snapshot failed: " + e.Message); }
        }

        private void Unbind()
        {
            if (_channel != null)
            {
                _channel.CoinAdded -= OnCoin;
                _channel.CoinChanged -= OnCoin;
                _channel.CoinRemoved -= OnCoinRemoved;
            }
            _channel = null;
            _room.Clear();
        }

        private void OnCoin(string key, CoinState s)
        {
            if (_disposed || s == null || !Wire.IsSafeId(s.coinId) || string.IsNullOrEmpty(s.ticker)) return;
            var c = new MarketCoin
            {
                CoinId = s.coinId,
                Ticker = s.ticker.Length > 16 ? s.ticker.Substring(0, 16) : s.ticker,
                Name = s.name ?? "",
                Stage = s.stage ?? "new",
                PriceMicros = s.priceMicros,
                McapCents = s.mcapCents,
                Vol5mCents = s.vol5mCents,
                Change5mBps = s.change5mBps,
                Holders = s.holders,
                LaunchedAtMs = s.launchedAt > 0 ? s.launchedAt : (long?)null,
            };
            _room[c.CoinId] = c;
            Remember(c.CoinId, c.Ticker);
            _roomUpdatedAt = Time.realtimeSinceStartupAsDouble;
            _changedPending = true; // Coalesced: one Changed per Pump, not one per field patch.
        }

        private void OnCoinRemoved(string key)
        {
            if (_disposed) return;
            var hit = _room.FirstOrDefault(kv => kv.Key == key || kv.Value.CoinId == key);
            if (hit.Value != null) _room.Remove(hit.Key);
            _changedPending = true;
        }

        private void OnTape(TapeMessage m)
        {
            if (_disposed || m == null) return;
            var row = new TapeRow
            {
                CoinId = m.coinId,
                Side = m.side == "sell" ? "sell" : "buy",
                Name = string.IsNullOrEmpty(m.name) ? "anon" : (m.name.Length > 32 ? m.name.Substring(0, 32) : m.name),
                CashCents = Math.Abs(m.cashCents),
                ReceivedAt = Time.realtimeSinceStartupAsDouble,
            };
            _tape.AddFirst(row);
            while (_tape.Count > TapeKeep) _tape.RemoveLast();
            try { TapeAdded?.Invoke(row); }
            catch (Exception e) { Log.Exception("money", e); }
        }

        private void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Log.Exception("money", e); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subs.Dispose();
            Unbind();
        }
    }
}
