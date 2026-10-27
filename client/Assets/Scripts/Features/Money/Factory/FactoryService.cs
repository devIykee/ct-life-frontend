using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using Newtonsoft.Json.Linq;

namespace CTLife.Economy
{
    /// <summary>The outcome of <see cref="FactoryService.Launch"/>: the coin, or why not (a pre-check or the server).</summary>
    public sealed class LaunchResult
    {
        public FactoryCoin Coin;
        /// <summary>Set when the client refused before sending (FactoryText key in <see cref="FactoryCheck.Key"/>).</summary>
        public FactoryCheck Check = FactoryCheck.Pass;
        /// <summary>Set when the server refused or the outcome is unknown.</summary>
        public FactoryError Error;
        public bool Ok => Coin != null;
        public string Message => Coin != null ? FactoryText.F("launched", Coin.Ticker) : !Check.Ok ? FactoryText.F(Check.Key, Check.Args) : Error?.Message;
    }

    /// <summary>A player coin went live somewhere in the city (`evt_coin_launch` with `playerLaunched: "1"`).</summary>
    public sealed class PlayerLaunchSeen
    {
        public string CoinId, Ticker, TemplateId, CreatorAlias;
        public bool Mine;
    }

    /// <summary>A creator dump was flagged (`evt_rug` kind `creator_dump`): the Sleuth posts, the creator gets heat.</summary>
    public sealed class RugSeen
    {
        public string CoinId, Ticker, LauncherAlias;
        public int Heat;
        public bool Mine;
    }

    /// <summary>
    /// The Rugpull Factory for one signed-in player (backend #14): the launch rules (`/factory/config`), my allowance and heat
    /// (`/factory/me`), launching (`POST /factory/coins`, one retained Idempotency-Key per draft until a definitive answer),
    /// the trending / new / mine boards, a coin's detail, buying and selling through `POST /market/trades`, and the city's
    /// `evt_coin_launch` / `evt_rug` events. No UI here: the Factory screens wait for the approved brief; the Launchpad Tower
    /// desk prompt (<see cref="LaunchpadDesk"/>) reads <see cref="Me"/> and opens <see cref="CTLife.World.FactoryHooks.Open"/>.
    /// Owned by <see cref="FactoryOverlays"/>; rebinds on sign-in and sign-out.
    /// </summary>
    public sealed class FactoryService : IDisposable
    {
        public const double MeStaleSeconds = 30, ConfigStaleSeconds = 600;
        public static FactoryService Current { get; private set; }

        public readonly FactoryApi Api;
        private readonly MoneyApi _money;
        private readonly Func<double> _nowMs;
        private readonly Func<double> _clock;   // seconds, for staleness
        public string UserId { get; }
        public FactoryConfig Config { get; private set; } = FactoryConfig.Defaults;
        public FactoryMe Me { get; private set; }
        /// <summary>My coins, newest first (from `view=mine` and my launches).</summary>
        public IReadOnlyList<FactoryCoin> Mine => _mine;
        public bool Disposed { get; private set; }
        /// <summary>The last read failed on the network (the desk prompt says Offline).</summary>
        public bool Offline { get; private set; }
        public bool Launching => _launching;

        public event Action ConfigChanged, MeChanged, MineChanged;
        public event Action<FactoryCoin> Launched;
        public event Action<PlayerLaunchSeen> PlayerLaunch;
        public event Action<RugSeen> Rug;

        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly List<FactoryCoin> _mine = new List<FactoryCoin>();
        private readonly Dictionary<string, FactoryCoin> _known = new Dictionary<string, FactoryCoin>();
        private readonly Dictionary<string, double> _wanted = new Dictionary<string, double>();
        /// <summary>A player coin's Factory facts arrived (dev bag, creator, liquidity) for Pulse's rows and page.</summary>
        public event Action<string> KnownChanged;
        private readonly RetainedWrite _launchWrite = new RetainedWrite();
        private readonly Dictionary<string, RetainedWrite> _tradeWrites = new Dictionary<string, RetainedWrite>();
        private double _meAt = double.NegativeInfinity, _configAt = double.NegativeInfinity;
        private Task<FactoryMe> _meTask;
        private Task<FactoryConfig> _configTask;
        private bool _launching;

        /// <param name="rest">The REST client (tests pass a fake).</param>
        /// <param name="nowMs">Server-time epoch ms (ServerClock in the game).</param>
        /// <param name="clock">Monotonic seconds for staleness (Unity realtime in the game).</param>
        public FactoryService(string userId, IRestClient rest, Func<double> nowMs, Func<double> clock, bool subscribe = true)
        {
            UserId = userId;
            Api = new FactoryApi(rest);
            _money = new MoneyApi(rest);
            _nowMs = nowMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _clock = clock ?? (() => Environment.TickCount / 1000.0);
            if (subscribe)
            {
                Current = this;
                _subs.On<EventMessage>(OnEvent);
            }
        }

        public CancellationToken Life => _life.Token;
        public double NowMs => _nowMs();
        public bool ConfigFromServer => Config.FromServer;

        /// <summary>The desk can send a launch now (allowance known and open). Unknown allowance = true (the server decides).</summary>
        public bool CanLaunchNow => Me == null || Me.CanLaunch(NowMs);

        // ------------------------------------------------------------------ reads

        /// <summary>GET /factory/me unless fresh (<see cref="MeStaleSeconds"/>); concurrent callers share one request.</summary>
        public Task<FactoryMe> RefreshMe(bool force = false)
        {
            if (Disposed) return Task.FromResult(Me);
            if (!force && Me != null && _clock() - _meAt < MeStaleSeconds) return Task.FromResult(Me);
            if (_meTask != null) return _meTask;
            var t = LoadMe();
            if (!t.IsCompleted) _meTask = t;   // a synchronous completion already ran its finally
            return t;
        }

        private async Task<FactoryMe> LoadMe()
        {
            try
            {
                var me = await Api.Me(_life.Token);
                if (Disposed) return me;
                Me = me;
                _meAt = _clock();
                Offline = false;
                Log.Info("factory", $"me heat {me.Heat} ({me.HeatLevel}) launches {me.LaunchesToday}/{me.DailyLaunchCap} next {(me.NextLaunchAtMs?.ToString(CultureInfo.InvariantCulture) ?? "now")}");
                MeChanged?.Invoke();
                return me;
            }
            catch (OperationCanceledException) { return Me; }
            catch (Exception e)
            {
                var err = FactoryErrors.From(e, false);
                Offline = err.Is("NETWORK_ERROR") || err.Is("TIMEOUT");
                Log.Warn("factory", "GET /factory/me failed: " + err.Code);
                return Me;
            }
            finally { _meTask = null; }
        }

        /// <summary>GET /factory/config unless fresh; the defaults (backend #14's values) stand in until it arrives.</summary>
        public Task<FactoryConfig> RefreshConfig(bool force = false)
        {
            if (Disposed) return Task.FromResult(Config);
            if (!force && Config.FromServer && _clock() - _configAt < ConfigStaleSeconds) return Task.FromResult(Config);
            if (_configTask != null) return _configTask;
            var t = LoadConfig();
            if (!t.IsCompleted) _configTask = t;
            return t;
        }

        private async Task<FactoryConfig> LoadConfig()
        {
            try
            {
                var c = await Api.Config(_life.Token);
                if (Disposed) return c;
                Config = c;
                _configAt = _clock();
                Log.Info("factory", $"config fee {c.LaunchFeeCents} liquidity {c.MinLiquidityCents}-{c.MaxLiquidityCents} dev<= {c.MaxDevAllocationBps} cap {c.DailyLaunchCap} cooldown {c.CooldownSec}s art {c.Templates.Count}");
                ConfigChanged?.Invoke();
                return c;
            }
            catch (OperationCanceledException) { return Config; }
            catch (Exception e) { Log.Warn("factory", "GET /factory/config failed: " + FactoryErrors.From(e, false).Code); return Config; }
            finally { _configTask = null; }
        }

        /// <summary>A board: trending (live coins by 5 m volume), new (newest live) or mine (all of mine). Null on failure.</summary>
        public async Task<List<FactoryCoin>> Board(string view, int limit = 20)
        {
            if (Disposed) return null;
            try
            {
                var list = await Api.Coins(view, limit, _life.Token);
                if (Disposed) return list;
                foreach (var c in list) { MoneyHub.Current?.Market.Remember(c.Coin); Learn(c); }
                if (view == "mine") { _mine.Clear(); _mine.AddRange(list); MineChanged?.Invoke(); }
                return list;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception e) { Log.Warn("factory", $"GET /factory/coins?view={view} failed: " + FactoryErrors.From(e, false).Code); return null; }
        }

        /// <summary>One player coin's card (chart, holders, risk hint). Throws FactoryError-mappable exceptions to the caller.</summary>
        public async Task<FactoryCoinDetail> Detail(string coinId)
        {
            var d = await Api.Coin(coinId, _life.Token);
            if (!Disposed && d?.Coin != null) Learn(d.Coin);
            return d;
        }

        /// <summary>What we know about a player coin (from any board, my launches or a detail), or null.</summary>
        public FactoryCoin Known(string coinId) => coinId != null && _known.TryGetValue(coinId, out var c) ? c : null;

        /// <summary>Is this coin mine (launched by me)?</summary>
        public bool IsMine(string coinId) => coinId != null && (_mine.Exists(c => c.CoinId == coinId) || Known(coinId)?.IsMine == true);

        /// <summary>Ask for a player coin's facts if we don't have them (at most once per 30 s per coin); KnownChanged fires.</summary>
        public void Want(string coinId)
        {
            if (Disposed || !Wire.IsSafeId(coinId) || _known.ContainsKey(coinId)) return;
            double now = _clock();
            if (_wanted.TryGetValue(coinId, out var at) && now - at < 30) return;
            _wanted[coinId] = now;
            _ = WantAsync(coinId);
        }

        private async Task WantAsync(string coinId)
        {
            try { await Detail(coinId); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("factory", "coin facts for " + coinId + " failed: " + FactoryErrors.From(e, false).Code); }
        }

        private void Learn(FactoryCoin c)
        {
            if (c?.CoinId == null) return;
            _known[c.CoinId] = c;
            KnownChanged?.Invoke(c.CoinId);
        }

        // ------------------------------------------------------------------ launch

        /// <summary>
        /// Launch a coin. Pre-checks shape, art, ranges, Cash, cooldown and cap; then POSTs with the draft's retained key (the
        /// same key while the same draft is retried after an unknown outcome, so a retry can never launch twice).
        /// </summary>
        /// <param name="cashCents">The Cash balance for the pre-check, or null to let the server decide.</param>
        public async Task<LaunchResult> Launch(LaunchDraft draft, long? cashCents)
        {
            if (Disposed) return new LaunchResult { Error = new FactoryError { Money = new MoneyError { Code = "ERROR", Key = "err.generic" } } };
            if (_launching) return new LaunchResult { Error = new FactoryError { Money = new MoneyError { Code = "BUSY", Key = "err.generic" } } };
            var check = FactoryRules.Check(draft, Config, cashCents, Me, NowMs);
            if (!check.Ok) return new LaunchResult { Check = check };
            var body = draft.Body();
            string key = _launchWrite.KeyFor("/factory/coins", RetainedWrite.Body(body));
            _launching = true;
            try
            {
                var coin = await Api.Launch(body, key, _life.Token);
                _launchWrite.Complete();
                Log.Info("factory", $"launched {coin.Ticker} {coin.CoinId} art {coin.TemplateId} liquidity {coin.LiquidityCents} dev {coin.DevAllocationBps}bps");
                if (Disposed) return new LaunchResult { Coin = coin };
                _mine.RemoveAll(c => c.CoinId == coin.CoinId);
                _mine.Insert(0, coin);
                Learn(coin);
                MoneyHub.Current?.Market.Remember(coin.Coin);
                MoneyHub.Current?.RefreshProfile(force: true);
                // until /factory/me answers, count the launch locally so an immediate second try is caught by the pre-check
                if (Me != null)
                {
                    long cool = (long)NowMs + Config.CooldownSec * 1000L;
                    Me = new FactoryMe { Heat = Me.Heat, HeatLevel = Me.HeatLevel, LaunchesToday = Me.LaunchesToday + 1, DailyLaunchCap = Me.DailyLaunchCap,
                        NextLaunchAtMs = Math.Max(Me.NextLaunchAtMs ?? 0, cool) };
                    MeChanged?.Invoke();
                }
                MineChanged?.Invoke();
                Launched?.Invoke(coin);
                _ = RefreshMe(force: true);
                return new LaunchResult { Coin = coin };
            }
            catch (OperationCanceledException) { return new LaunchResult { Error = FactoryErrors.From(new TimeoutException(), true) }; }
            catch (Exception e)
            {
                var err = FactoryErrors.From(e, true);
                if (!err.OutcomeUnknown) _launchWrite.Complete();
                Log.Warn("factory", $"launch refused: {err.Code}" + (err.RetryAfterMs != null ? $" retryAfterMs {err.RetryAfterMs}" : ""));
                if (err.Is("LAUNCH_COOLDOWN") || err.Is("LAUNCH_LIMIT_REACHED") || err.OutcomeUnknown) _ = RefreshMe(force: true);
                if (err.Is("INSUFFICIENT_CASH")) MoneyHub.Current?.RefreshProfile(force: true);
                return new LaunchResult { Error = err };
            }
            finally { _launching = false; }
        }

        /// <summary>Drop a draft whose outcome is unknown on purpose (the player edits the form instead of retrying).</summary>
        public void AbandonPendingLaunch() => _launchWrite.Complete();
        public bool LaunchPending => _launchWrite.Pending;

        // ------------------------------------------------------------------ trades (the ordinary market endpoint)

        /// <summary>Buy a player coin with Cash (`POST /market/trades` side buy). Returns the receipt, or throws.</summary>
        public Task<TradeReceipt> Buy(string coinId, long cashCents, int slippageBps = Tickets.DefaultSlippageBps)
        {
            if (!Wire.IsSafeId(coinId)) throw new ArgumentException("Unsafe coin id");
            if (cashCents <= 0) throw new ArgumentOutOfRangeException(nameof(cashCents));
            return Trade(coinId, new JObject { ["coinId"] = coinId, ["side"] = "buy", ["cashCents"] = cashCents, ["maxSlippageBps"] = slippageBps });
        }

        /// <summary>Sell units of a player coin (base-unit string, as `/market/positions` returns them).</summary>
        public Task<TradeReceipt> Sell(string coinId, string units, int slippageBps = Tickets.DefaultSlippageBps)
        {
            if (!Wire.IsSafeId(coinId)) throw new ArgumentException("Unsafe coin id");
            if (!TokenUnits.IsPositive(units)) throw new ArgumentOutOfRangeException(nameof(units));
            return Trade(coinId, new JObject { ["coinId"] = coinId, ["side"] = "sell", ["units"] = units, ["maxSlippageBps"] = slippageBps });
        }

        private async Task<TradeReceipt> Trade(string coinId, JObject body)
        {
            if (!_tradeWrites.TryGetValue(coinId, out var write)) _tradeWrites[coinId] = write = new RetainedWrite();
            string key = write.KeyFor("/market/trades", RetainedWrite.Body(body));
            try
            {
                var r = await _money.Trade(body, key, _life.Token);
                write.Complete();
                Log.Info("factory", $"trade {body["side"]} {coinId} units {r.Units} cash {r.CashCents} fee {r.FeeCents}");
                MoneyHub.Current?.RefreshProfile(force: true);
                return r;
            }
            catch (Exception e)
            {
                if (!MoneyErrors.From(e, true).OutcomeUnknown) write.Complete();
                throw;
            }
        }

        // ------------------------------------------------------------------ city events

        private string MyAlias => Services.TryGet<PlayerProfile>(out var p) ? p.Alias : null;

        /// <summary>Handles one city `event` (public for tests).</summary>
        public void OnEvent(EventMessage e)
        {
            if (Disposed || e == null || e.@params == null) return;
            string P(string k) => e.@params.TryGetValue(k, out var v) ? v : null;
            if (e.eventId == "evt_coin_launch" && P("playerLaunched") == "1")
            {
                var seen = new PlayerLaunchSeen
                {
                    CoinId = Wire.IsSafeId(P("coinId")) ? P("coinId") : null,
                    Ticker = Clean(P("ticker"), 16),
                    TemplateId = MoneyParse.TemplateIdOrNull(P("templateId")),
                    CreatorAlias = Clean(P("creatorAlias"), 32),
                };
                if (seen.CoinId == null || seen.Ticker == null) return;
                seen.Mine = _mine.Any(c => c.CoinId == seen.CoinId) || seen.CreatorAlias != null && seen.CreatorAlias == MyAlias;
                MoneyHub.Current?.Market.Remember(seen.CoinId, seen.Ticker);
                PlayerLaunch?.Invoke(seen);
            }
            else if (e.eventId == "evt_rug" && P("kind") == "creator_dump")
            {
                int heat = FactoryParse.HeatOf(P("heat") != null ? new JValue(P("heat")) : null);
                var rug = new RugSeen
                {
                    CoinId = Wire.IsSafeId(P("coinId")) ? P("coinId") : null,
                    Ticker = Clean(P("ticker"), 16),
                    LauncherAlias = Clean(P("launcherAlias"), 32),
                    Heat = Math.Max(0, Math.Min(100, heat)),
                };
                var mine = rug.CoinId != null ? _mine.FirstOrDefault(c => c.CoinId == rug.CoinId) : null;
                rug.Mine = mine != null || rug.LauncherAlias != null && rug.LauncherAlias == MyAlias;
                if (mine != null) { mine.RugFlagged = true; MineChanged?.Invoke(); }
                if (rug.Mine)
                {
                    Log.Info("factory", $"my {rug.Ticker} dump was flagged, heat {rug.Heat}");
                    _ = RefreshMe(force: true);
                }
                Rug?.Invoke(rug);
            }
        }

        private static string Clean(string s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            foreach (var ch in s) if (char.IsControl(ch)) return null;
            s = FactoryParse.NoMarkup(s);
            return s.Length > max ? s.Substring(0, max) : s;
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _life.Cancel();
            _subs.Dispose();
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
