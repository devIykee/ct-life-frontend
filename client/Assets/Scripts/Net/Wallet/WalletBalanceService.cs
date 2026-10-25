using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using Newtonsoft.Json.Linq;

namespace CTLife.Net
{
    /// <summary>
    /// Shared main-thread wallet holdings state. Mainnet display is deliberately separate from the payment cluster.
    /// No signing, prices, transfers or game-Cash fallback. Previous observations remain explicitly stale after failure.
    /// </summary>
    public sealed class WalletBalanceService : IDisposable
    {
        public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
        private static readonly string[] Symbols = { "SOL", "USDC", "SKR", "ORE" };
        private readonly Func<string> _wallet;
        private readonly Func<JToken> _currencies;
        private readonly Func<DateTimeOffset> _now;
        private readonly IWalletBalanceRpc _rpc;
        private readonly SolanaCluster _cluster;
        private readonly Dictionary<string, (WalletMintInfo mint, DateTimeOffset fetched)> _mintCache = new();
        private readonly Session _session;
        private readonly ConfigStore _config;
        private CancellationTokenSource _cancel;
        private Task _active;
        private DateTimeOffset? _lastAttempt;
        private int _revision;
        private bool _disposed;

        public WalletBalanceSnapshot State { get; private set; }
        /// <summary>The cluster the balances are read from (labels say which: "WALLET · DEVNET").</summary>
        public SolanaCluster Cluster => _cluster;
        public string ClusterLabel => ClusterName(_cluster);
        public static string ClusterName(SolanaCluster c) => c == SolanaCluster.Devnet ? "DEVNET" : c == SolanaCluster.Mainnet ? "MAINNET" : "OFFLINE";
        public event Action Changed;

        /// <param name="cluster">The environment's cluster (Env.Current.cluster): staging reads devnet balances, prod mainnet.</param>
        public WalletBalanceService(Session session, ConfigStore config, SolanaCluster cluster = SolanaCluster.Mainnet)
            : this(() => !RuntimeMode.IsOfflineDemo && session.IsSignedIn && session.AuthMethod == "siws" ? session.Wallet : null,
                () => config.Config("economy")?["currencies"], new WalletBalanceRpc(), cluster)
        {
            _session = session; _config = config;
            session.Changed += Invalidate; config.Changed += Invalidate;
            _ = Runner.Instance; // The event requires its persistent MonoBehaviour to receive Android pause/resume.
            Runner.ApplicationPaused += OnApplicationPaused;
        }

        /// <summary>Explicit dependency seam for offline verification; the app constructor uses its authenticated SIWS session.</summary>
        public WalletBalanceService(Func<string> wallet, Func<JToken> currencies, IWalletBalanceRpc rpc,
            SolanaCluster cluster = SolanaCluster.Mainnet, Func<DateTimeOffset> now = null)
        {
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _currencies = currencies ?? throw new ArgumentNullException(nameof(currencies));
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc)); _cluster = cluster;
            _now = now ?? (() => DateTimeOffset.UtcNow);
            State = Empty(ConnectedWallet());
        }

        /// <summary>Coalesces callers. Automatic refreshes wait 30s; manual refresh still has a 5s minimum cooldown.</summary>
        public Task Refresh(bool force = false)
        {
            if (_disposed) return Task.CompletedTask;
            string wallet = ConnectedWallet();
            if (wallet != State.Wallet) Invalidate();
            if (wallet == null || _cluster != SolanaCluster.Mainnet && _cluster != SolanaCluster.Devnet)
                return Task.CompletedTask;
            if (_active != null) return _active;
            if (_lastAttempt.HasValue && _now() - _lastAttempt.Value < (force ? TimeSpan.FromSeconds(5) : RefreshInterval))
                return Task.CompletedTask;
            _lastAttempt = _now();
            var completed = new TaskCompletionSource<bool>();
            _active = completed.Task;
            _cancel = new CancellationTokenSource();
            _ = RefreshCore(wallet, _revision, _cancel.Token, completed);
            return completed.Task;
        }

        /// <summary>Account/config changes cancel old work and remove every value from the previous identity.</summary>
        public void Invalidate()
        {
            if (_disposed) return;
            _revision++; _cancel?.Cancel(); _cancel?.Dispose(); _cancel = null;
            _active = null; _lastAttempt = null; _mintCache.Clear();
            State = Empty(ConnectedWallet()); Changed?.Invoke();
        }

        private async Task RefreshCore(string wallet, int revision, CancellationToken ct, TaskCompletionSource<bool> completed)
        {
            var current = State.Tokens.ToArray();
            try
            {
                var previous = State.Tokens.ToArray();
                var specs = Specs();
                current = specs.Select(spec => Carry(spec.symbol, spec.mint, previous, WalletTokenState.Loading, null)).ToArray();
                Publish(wallet, current, true);
                await Task.WhenAll(specs.Select((spec, i) => ReadToken(spec.symbol, spec.mint, wallet, revision, ct, i, current)));
                if (Current(wallet, revision)) Publish(wallet, current, false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception)
            {
                if (Current(wallet, revision))
                    Publish(wallet, current.Select(token => Carry(token.Symbol, token.Mint, current,
                        WalletTokenState.Unavailable, "responseInvalid")).ToArray(), false);
            }
            finally
            {
                if (revision == _revision) { _active = null; _cancel?.Dispose(); _cancel = null; }
                completed.TrySetResult(true);
            }
        }

        private async Task ReadToken(string symbol, string mint, string wallet, int revision, CancellationToken ct,
            int index, WalletTokenBalance[] current)
        {
            WalletTokenBalance token;
            try
            {
                if (symbol != "SOL" && !WalletBalanceParser.IsAddress(mint)) throw new WalletBalanceReadException("tokenNotConfigured");
                WalletBalanceValue value;
                if (symbol == "SOL")
                {
                    var result = await _rpc.Read(_cluster, "getBalance", new JArray(wallet, new JObject { ["commitment"] = "confirmed" }), ct);
                    value = WalletBalanceParser.Native(result);
                }
                else
                {
                    string key = _cluster + ":" + mint;
                    if (!_mintCache.TryGetValue(key, out var metadata) || _now() - metadata.fetched > TimeSpan.FromMinutes(10))
                    {
                        var result = await _rpc.Read(_cluster, "getAccountInfo", new JArray(mint, Options()), ct);
                        metadata = (WalletBalanceParser.Mint(result), _now());
                        ct.ThrowIfCancellationRequested();
                        if (!Current(wallet, revision)) return;
                        _mintCache[key] = metadata;
                    }
                    var resultAccounts = await _rpc.Read(_cluster, "getTokenAccountsByOwner",
                        new JArray(wallet, new JObject { ["mint"] = mint }, Options()), ct);
                    value = WalletBalanceParser.Tokens(resultAccounts, wallet, mint, metadata.mint);
                }
                ct.ThrowIfCancellationRequested();
                token = new WalletTokenBalance(symbol, mint, WalletTokenState.Ready, value, _now(), false, null, _now);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (OperationCanceledException)
            {
                token = Carry(symbol, mint, current, WalletTokenState.Unavailable, "networkUnavailable");
            }
            catch (Exception e)
            {
                token = Carry(symbol, mint, current, WalletTokenState.Unavailable,
                    e is WalletBalanceReadException read ? read.Code : "responseInvalid");
            }
            if (!Current(wallet, revision)) return;
            current[index] = token;
            Publish(wallet, current, true);
        }

        private void Publish(string wallet, WalletTokenBalance[] tokens, bool refreshing)
        {
            int ready = tokens.Count(token => token.State == WalletTokenState.Ready);
            var status = refreshing ? WalletBalanceStatus.Loading : ready == tokens.Length ? WalletBalanceStatus.Ready :
                ready > 0 ? WalletBalanceStatus.Partial : WalletBalanceStatus.Unavailable;
            State = new WalletBalanceSnapshot(wallet, _cluster, status, refreshing, tokens.ToArray());
            Changed?.Invoke();
        }

        private WalletTokenBalance Carry(string symbol, string mint, IEnumerable<WalletTokenBalance> prior, WalletTokenState state, string error)
        {
            var old = prior.FirstOrDefault(token => token.Symbol == symbol && token.Mint == mint && token.Amount.HasValue);
            var value = old == null ? null : new WalletBalanceValue(decimal.Parse(old.RawAmount, System.Globalization.CultureInfo.InvariantCulture), old.Decimals);
            return new WalletTokenBalance(symbol, mint, state, value, old?.UpdatedAtUtc, old != null, error, _now);
        }

        private WalletBalanceSnapshot Empty(string wallet)
        {
            string error = wallet == null ? "walletNotConnected" : _cluster == SolanaCluster.None ? "unsupportedCluster" : null;
            return new WalletBalanceSnapshot(wallet, _cluster, wallet == null ? WalletBalanceStatus.Disconnected : WalletBalanceStatus.Unavailable, false,
                Specs().Select(spec => new WalletTokenBalance(spec.symbol, spec.mint, WalletTokenState.Unavailable, null, null, false, error, _now)).ToArray());
        }

        private (string symbol, string mint)[] Specs()
        {
            var currencies = _currencies() as JObject;
            return Symbols.Select(symbol =>
            {
                var definition = currencies?[symbol] as JObject;
                var mint = definition?[_cluster == SolanaCluster.Mainnet ? "mint" : "devnetMint"];
                return (symbol, symbol == "SOL" || mint?.Type != JTokenType.String ? null : (string)mint);
            }).ToArray();
        }
        private string ConnectedWallet() { var value = _wallet(); return WalletBalanceParser.IsAddress(value) ? value : null; }
        private bool Current(string wallet, int revision) => !_disposed && _revision == revision && wallet == ConnectedWallet();
        private static JObject Options() => new JObject { ["commitment"] = "confirmed", ["encoding"] = "jsonParsed" };
        private void OnApplicationPaused(bool paused) { if (!paused) _ = Refresh(force: true); }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _revision++; _cancel?.Cancel(); _cancel?.Dispose(); _cancel = null; _active = null;
            if (_session != null) _session.Changed -= Invalidate;
            if (_config != null) _config.Changed -= Invalidate;
            Runner.ApplicationPaused -= OnApplicationPaused;
            Changed = null;
        }
    }
}
