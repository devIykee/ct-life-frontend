using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Phase-two fictional-Cash terminal. Activation is a reviewed integration decision, not a fabricated server capability.</summary>
    public sealed class PerpsApp : PhoneApp
    {
        public override string Id => "perps";
        public override string Title => PerpsStrings.T("title");
        public override string Color => "#eb3b5a"; // Existing catalog colour.
        public override int Phase => 2;
        public override string Feature => Features.Markets;
        private readonly bool _ordersEnabled;
        private View _view;
        public PerpsApp(bool ordersEnabled = false) { _ordersEnabled = ordersEnabled; }
        public override VisualElement Open() { Closed(); _view = new View(_ordersEnabled, () => Phone.Close()); return _view.Root; }
        public override void Closed() { _view?.Dispose(); _view = null; }

        private sealed class View : IDisposable
        {
            public readonly ScrollView Root;
            private readonly bool _enabled, _reviewOnly;
            private readonly Action _close;
            private readonly PerpsApi _api;
            private readonly Session _session;
            private readonly string _user, _journalKey;
            private readonly CancellationTokenSource _life = new CancellationTokenSource();
            private readonly Subscriptions _subs = new Subscriptions();
            private readonly List<(VisualElement element, Func<bool> allowed)> _controls = new List<(VisualElement, Func<bool>)>();
            private readonly VisualElement _content;
            private readonly Label _message, _age, _working, _stale;
            private List<PerpRecord> _positions;
            private readonly List<PerpCoin> _coins = new List<PerpCoin>();
            private PerpMarket _market;
            private PerpAccount _account;
            private PerpIntent _pending, _preview;
            private PerpRecord _closeReview, _receipt;
            private string _next, _margin = "", _side = "long";
            private int _leverage = 1;
            private bool _busy, _disposed, _fresh, _unavailable, _choose, _history, _editing, _journalBroken;
            private double _received, _nextPoll;
            private bool SameUser => _reviewOnly || (!_disposed && _session.IsSignedIn && _session.UserId == _user);
            private bool Fresh => _fresh && (_reviewOnly || Time.realtimeSinceStartupAsDouble - _received < 20);
            private bool MarketEnabled => _reviewOnly || Features.IsAvailable(Features.Markets);
            private bool WriteReady => !_reviewOnly && _enabled && !_journalBroken && _pending == null && Fresh && MarketEnabled && _api.CanSendOnce;

            public View(bool enabled, Action close, string reviewScenario = null)
            {
                _enabled = enabled; _close = close; _reviewOnly = reviewScenario != null;
                _message = Copy("loading", "ct-perp-message");
                _age = Copy("unknownAge", "ct-perp-note");
                _working = Copy("busy", "ct-perp-note");
                _stale = Copy("stale", "ct-perp-message");
                _content = El.Div("ct-perp-content");
                Root = AppUi.Page(_message, _stale, _working, _content);
                Root.AddToClassList("ct-perps");
                var sheet = Resources.Load<StyleSheet>("UI/perps");
                if (sheet != null) Root.styleSheets.Add(sheet);
                Root.RegisterCallback<DetachFromPanelEvent>(Detached);
                if (_reviewOnly)
                {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                    SeedReview(reviewScenario);
#endif
                    return;
                }
                _api = Services.Get<Api>().Perps;
                _session = Services.Get<Session>();
                _user = _session.UserId;
                if (!_session.IsSignedIn || !Guid.TryParse(_user, out var id) || id == Guid.Empty) { Message("session"); return; }
                _journalKey = $"ctlife.{Env.Current.id}.perpIntent.{id}";
                try { var saved = PlayerPrefs.GetString(_journalKey, ""); if (!string.IsNullOrEmpty(saved)) _pending = PerpIntent.Parse(saved); }
                catch { _journalBroken = true; }
                _session.Changed += SessionChanged;
                _subs.On<DegradedChangedSignal>(_ => { _fresh = false; Message("degraded"); UpdateControls(); _nextPoll = 0; });
                // A liquidation notification only invalidates state. The backend may omit lossCents.
                _subs.On<CTLife.Realtime.LiquidatedMessage>(_ => { _fresh = false; _nextPoll = 0; UpdateControls(); });
                _ = Runner.Instance;
                Runner.Ticked += Tick;
                Run(Refresh);
            }

            private static Label Copy(string key, string cls = "ct-perp-copy") => Label(PerpsStrings.T(key), cls);
            private static Label Label(string text, string cls = "ct-perp-copy")
            {
                var label = El.Text(text, cls); label.enableRichText = false; return label;
            }
            private void Message(string key) { _message.text = key == null ? "" : PerpsStrings.T(key); _message.Show(key != null); }
            private void Detached(DetachFromPanelEvent evt) { if (evt.target == Root) Dispose(); }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true; _life.Cancel(); _subs.Dispose();
                Runner.Ticked -= Tick;
                if (_session != null) _session.Changed -= SessionChanged;
                Root.UnregisterCallback<DetachFromPanelEvent>(Detached);
                // Pending intent is deliberately retained through close/sign-out/cancellation.
            }
            private void SessionChanged() { if (!SameUser) { Dispose(); _close?.Invoke(); } }
            private void Tick()
            {
                if (_disposed) return;
                _age.text = _fresh ? PerpsStrings.F("snapshot", Math.Max(0, (int)(Time.realtimeSinceStartupAsDouble - _received))) : PerpsStrings.T("unknownAge");
                UpdateControls();
                if (_reviewOnly || _busy || _editing || !MarketEnabled || Time.realtimeSinceStartupAsDouble < _nextPoll) return;
                Run(Refresh);
            }
            private async void Run(Func<CancellationToken, Task> work)
            {
                if (_disposed || _busy || _reviewOnly || !SameUser) return;
                _busy = true; UpdateControls();
                try { await work(_life.Token); }
                catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
                catch (Exception error)
                {
                    if (_disposed || !SameUser) return;
                    _fresh = false;
                    _unavailable = error is ApiException a && (a.Status == 404 || a.Status == 501);
                    Message(_pending != null ? (_pending.Kind == "open" ? "unknownOpen" : "unknownClose") :
                        _unavailable ? "unavailable" : error is FormatException || error is OverflowException ? "invalid" :
                        error is TimeoutException || error is ApiException n && n.IsNetwork ? "network" : "failed");
                    Render();
                    Log.Warn("perps", error is ApiException api ? $"Request failed: {api.Code} / {api.Status}" : error.GetType().Name);
                }
                finally { _busy = false; _nextPoll = Time.realtimeSinceStartupAsDouble + 10; if (!_disposed) UpdateControls(); }
            }
            private Task<T> Read<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct) => PerpsRequest.Bounded(call, ct);
            private async Task Refresh(CancellationToken ct)
            {
                var rows = Read(_api.Positions, ct);
                var coins = Read(c => _api.Coins(null, c), ct);
                var rules = Read(_api.Rules, ct);
                var me = Read(_api.Me, ct);
                await Task.WhenAll(rows, coins, rules, me);
                var account = PerpsWire.Account(await me, await rules);
                if (account.User.ToString() != _user) throw new OperationCanceledException("Identity changed");
                ct.ThrowIfCancellationRequested(); if (!SameUser) return;
                var firstPage = await coins;
                var selected = _market?.Coin.Id ?? firstPage.Items.FirstOrDefault()?.Id;
                var market = selected.HasValue ? await Read(c => _api.Market(selected.Value, c), ct) : null;
                ct.ThrowIfCancellationRequested(); if (!SameUser) return;
                if (market != null && market.Coin.Id != selected.Value) throw new FormatException("Unrelated market");
                _account = account; _positions = await rows;
                if (_closeReview != null && !_positions.Any(p => p.Id == _closeReview.Id && p.Status == "open")) _closeReview = null;
                _coins.Clear(); _coins.AddRange(firstPage.Items); _next = firstPage.Next;
                _market = market; _fresh = true; _unavailable = false; _received = Time.realtimeSinceStartupAsDouble;
                _leverage = Math.Max(1, Math.Min(_leverage, account.Cap ?? 1));
                var reconciled = _pending?.Reconcile(_positions);
                if (reconciled != null) { _receipt = reconciled; ClearPending(); }
                // Actual server balance, never margin arithmetic; verify profile identity before publishing.
                if (Services.TryGet<PlayerProfile>(out var profile) && profile.UserId == _user) profile.SetCash(account.Cash);
                Message(null); Render();
            }
            private async Task Select(PerpCoin coin, CancellationToken ct)
            {
                var market = await Read(c => _api.Market(coin.Id, c), ct);
                ct.ThrowIfCancellationRequested(); if (!SameUser) return;
                if (market.Coin.Id != coin.Id) throw new FormatException("Unrelated market");
                _market = market; _choose = false; _preview = null;
                // Market selection does not refresh account/positions, so it cannot extend the write freshness window.
                Message(null); Render();
            }
            private async Task More(CancellationToken ct)
            {
                var cursor = _next;
                var page = await Read(c => _api.Coins(cursor, c), ct);
                ct.ThrowIfCancellationRequested(); if (!SameUser) return;
                foreach (var coin in page.Items) if (!_coins.Any(x => x.Id == coin.Id)) _coins.Add(coin);
                _next = page.Next == cursor ? null : page.Next; Render();
            }
            private void SavePending(PerpIntent intent)
            {
                _pending = intent;
                try { PlayerPrefs.SetString(_journalKey, intent.Serialize()); PlayerPrefs.Save(); }
                catch { _journalBroken = true; throw; } // Never submit when retaining the request fails.
            }
            private void ClearPending()
            {
                PlayerPrefs.DeleteKey(_journalKey); PlayerPrefs.Save(); _pending = null;
            }
            private async Task Submit(PerpIntent intent, CancellationToken ct)
            {
                if (!WriteReady || !intent.Valid || !SameUser) return;
                // Validate against fresh, server-derived state again at the final click.
                if (intent.Kind == "open" && (!DraftValid(out var margin) || intent.Margin != margin || intent.Coin != _market.Coin.Id || intent.Side != _side || intent.Leverage != _leverage)) return;
                if (intent.Kind == "close" && !_positions.Any(p => p.Id == intent.Position && p.Status == "open")) return;
                SavePending(intent); _preview = null; _closeReview = null; Message("sending"); Render();
                try
                {
                    var receipt = await PerpsRequest.Bounded(c => _api.Execute(intent, c), ct);
                    ct.ThrowIfCancellationRequested(); if (!SameUser) return;
                    _receipt = receipt;
                    if (_positions != null)
                    {
                        _positions.RemoveAll(p => p.Id == receipt.Id);
                        _positions.Insert(0, receipt); // Confirmed server row, even if the following balance refresh fails.
                    }
                    ClearPending();
                }
                catch (ApiException error) when ((error.Status == 400 && error.Is("LEVERAGE_NOT_ALLOWED")) ||
                    (error.Status == 402 && error.Is("INSUFFICIENT_CASH")) ||
                    (error.Status == 403 && error.Is("FORBIDDEN") && (string)error.Details?["reason"] == "insufficient_margin"))
                {
                    ClearPending(); _fresh = false; Message("rejected"); Render(); return;
                }
                // Every other uncertain/error/cancelled outcome retains the exact key and disables submissions.
                // There is deliberately no automatic or user-triggered replay control.
                try { await Refresh(ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { _fresh = false; Message("balanceStale"); Render(); }
            }

            private bool DraftValid(out long margin)
            {
                return PerpIntent.TryMargin(_margin, out margin) && _account != null && margin <= _account.Cash &&
                    _account.Cap.HasValue && _leverage >= 1 && _leverage <= _account.Cap.Value &&
                    _market != null && _market.Coin.Price > 0 && _market.Coin.Stage != "dead";
            }
            private Button Button(string key, Action action, Func<bool> allowed = null, string cls = "")
            {
                var button = El.Btn(PerpsStrings.T(key), action, "ct-btn ct-perp-button " + cls);
                button.name = "perps-" + key;
                _controls.Add((button, allowed ?? (() => true))); return button;
            }
            private void UpdateControls()
            {
                _working.Show(_busy);
                _stale.Show(_fresh && !Fresh);
                foreach (var pair in _controls) pair.element.SetEnabled(!_disposed && !_busy && pair.allowed());
            }
            private void Render()
            {
                _controls.Clear(); _content.Clear();
                if (_reviewOnly) _content.Add(Copy("reviewOnly", "ct-perp-review-watermark"));
                var top = El.Div("ct-perp-top", El.Div("ct-grow", Copy("heading", "ct-perp-eyebrow"), Copy("tagline", "ct-perp-title")),
                    El.Div("ct-perp-cash", Copy("cash", "ct-perp-eyebrow"), Label(_account == null ? "—" : PerpsStrings.Cash(_account.Cash), "ct-perp-cash-value")));
                _content.Add(top);
                _content.Add(El.Div("ct-perp-toolbar", El.Div("ct-grow", Copy("fictional", "ct-perp-note"), _age), Button("refresh", () => Run(Refresh), () => !_reviewOnly)));
                if (!_enabled && !_reviewOnly) _content.Add(Copy("gate", "ct-perp-notice"));
                if (_journalBroken) _content.Add(Copy("corruptJournal", "ct-perp-notice"));
                if (_pending != null)
                {
                    var pending = El.Div("ct-perp-notice", Copy(_pending.Kind == "open" ? "unknownOpen" : "unknownClose"),
                        Label(PerpsStrings.F("pendingKey", _pending.Key), "ct-perp-note"), Copy("pendingHold", "ct-perp-note"));
                    pending.name = "perps-pending"; _content.Add(pending);
                }
                if (_receipt != null)
                {
                    var receipt = El.Div("ct-perp-receipt", Copy("confirmed", "ct-perp-eyebrow"), Position(_receipt, false), Button("dismiss", () => { _receipt = null; Render(); }));
                    receipt.name = "perps-receipt"; _content.Add(receipt);
                }
                if (_unavailable) { _content.Add(Copy("unavailableDetail")); UpdateControls(); return; }
                if (_positions == null) { UpdateControls(); return; }
                if (_choose) { RenderMarkets(); UpdateControls(); return; }
                if (_market == null)
                    _content.Add(El.Div("ct-perp-empty", Copy("emptyMarket", "ct-perp-title"), Copy("emptyMarketDetail")));
                else
                {
                    RenderMarket();
                    if (_preview != null) RenderPreview(); else if (_closeReview == null) RenderDraft();
                }
                if (_closeReview != null) RenderClose();
                RenderPositions(); UpdateControls();
            }
            private void RenderMarkets()
            {
                _content.Add(Copy("markets", "ct-perp-title"));
                _content.Add(Button("cancel", () => { _choose = false; Render(); }));
                foreach (var coin in _coins)
                {
                    var selected = coin;
                    var button = El.Btn("", () => Run(c => Select(selected, c)), "ct-perp-market-row ct-pressable");
                    button.name = "perps-market-" + coin.Id;
                    button.Add(El.Div("ct-grow", Label(coin.Ticker, "ct-perp-title"), Label(coin.Name, "ct-perp-note")));
                    button.Add(Label(PerpsStrings.Price(coin.Price), "ct-perp-number"));
                    _controls.Add((button, () => !_reviewOnly)); _content.Add(button);
                }
                if (_next != null) _content.Add(Button("more", () => Run(More), () => !_reviewOnly));
            }
            private void RenderMarket()
            {
                var coin = _market.Coin;
                var header = El.Div("ct-perp-top", El.Div("ct-grow", Copy("market", "ct-perp-eyebrow"),
                    Label(coin.Ticker, "ct-perp-ticker"), Label(coin.Name + " · " + PerpsStrings.T("stage." + coin.Stage), "ct-perp-note")),
                    Button("choose", () => { _choose = true; _preview = null; Render(); }));
                var price = El.Div("ct-perp-top", El.Div("ct-grow ct-perp-price-column", Copy("price", "ct-perp-eyebrow"), Label(PerpsStrings.Price(coin.Price), "ct-perp-price"), Copy("priceUnits", "ct-perp-note")),
                    Label(PerpsStrings.F("change", (coin.ChangeBps / 100m).ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture)), "ct-perp-change " + (coin.ChangeBps < 0 ? "ct-red" : "ct-green")));
                var chart = new PerpChart(_market.Points); chart.name = "perps-chart";
                var panel = El.Div("ct-perp-market-panel", header, price, chart, Copy("history", "ct-perp-note"));
                if (_market.Points.Count < 2) panel.Add(Copy(_market.Points.Count == 0 ? "noHistory" : "onePoint", "ct-perp-note"));
                else panel.Add(Label(PerpsStrings.F("historyRange", DateTimeOffset.FromUnixTimeMilliseconds(_market.Points[0].At).ToString("HH:mm", CultureInfo.InvariantCulture),
                    DateTimeOffset.FromUnixTimeMilliseconds(_market.Points[_market.Points.Count - 1].At).ToString("HH:mm", CultureInfo.InvariantCulture)), "ct-perp-note"));
                _content.Add(panel);
            }
            private void RenderDraft()
            {
                var panel = El.Div("ct-perp-order", Copy("order", "ct-perp-eyebrow"));
                var side = El.Div("ct-perp-split");
                foreach (var value in new[] { "long", "short" })
                {
                    var selected = value;
                    side.Add(Button(value, () => { _side = selected; Render(); }, null, _side == value ? "ct-perp-selected" : ""));
                }
                panel.Add(side); panel.Add(Copy(_side + "Hint", "ct-perp-note"));
                var margin = new TextField(PerpsStrings.T("margin")) { value = _margin, maxLength = 24 };
                margin.name = "perps-margin"; margin.AddToClassList("ct-perp-input");
                margin.RegisterCallback<FocusInEvent>(_ => _editing = true);
                margin.RegisterCallback<FocusOutEvent>(_ => _editing = false);
                margin.RegisterValueChangedCallback(e => { _margin = e.newValue; UpdateControls(); });
                panel.Add(margin); _controls.Add((margin, () => true)); panel.Add(Copy("marginHint", "ct-perp-note"));
                var presets = El.Div("ct-perp-split");
                foreach (var percent in new[] { 10, 25, 50 })
                {
                    var ratio = percent;
                    var button = Button("percent", () => { _margin = (decimal.Floor(_account.Cash * (decimal)ratio / 100m) / 100m).ToString("0.00", CultureInfo.InvariantCulture); Render(); }, () => _account != null && _account.Cash > 0);
                    button.text = PerpsStrings.F("percent", percent); presets.Add(button);
                }
                panel.Add(presets);
                if (_account?.Cap != null)
                {
                    var leverageLabel = Label(PerpsStrings.F("leverage", _leverage), "ct-perp-copy"); panel.Add(leverageLabel);
                    var slider = new SliderInt(1, _account.Cap.Value) { value = _leverage };
                    slider.name = "perps-leverage"; slider.AddToClassList("ct-perp-slider");
                    slider.RegisterValueChangedCallback(e => { _leverage = e.newValue; leverageLabel.text = PerpsStrings.F("leverage", _leverage); UpdateControls(); });
                    slider.RegisterCallback<FocusInEvent>(_ => _editing = true); slider.RegisterCallback<FocusOutEvent>(_ => _editing = false);
                    panel.Add(slider); _controls.Add((slider, () => _account.Cap.Value > 1));
                    panel.Add(Label(PerpsStrings.F("cap", _account.Cap.Value), "ct-perp-note"));
                }
                else panel.Add(Copy("capUnknown", "ct-perp-note"));
                panel.Add(Button("preview", () =>
                {
                    if (!DraftValid(out var amount)) return;
                    _preview = new PerpIntent { Key = Guid.NewGuid().ToString(), Kind = "open", Coin = _market.Coin.Id, Side = _side, Leverage = _leverage, Margin = amount };
                    _editing = false; Render();
                }, () => Fresh && DraftValid(out _) && _pending == null && !_journalBroken, "ct-btn-primary"));
                _content.Add(panel);
            }
            private void RenderPreview()
            {
                var intent = _preview;
                _content.Add(El.Div("ct-perp-order", Copy("review", "ct-perp-title"),
                    Label(PerpsStrings.F("summary", _market.Coin.Ticker, PerpsStrings.T(intent.Side), intent.Leverage), "ct-perp-title"),
                    Label(PerpsStrings.F("marginSummary", PerpsStrings.Cash(intent.Margin))),
                    Label(PerpsStrings.F("notional", PerpsStrings.Cash((decimal)intent.Margin * intent.Leverage))), Copy("notionalNote", "ct-perp-note"),
                    Copy("terms", "ct-perp-note"), _reviewOnly ? Copy("reviewAction", "ct-perp-note") : null,
                    Button("confirm", () => Run(c => Submit(intent, c)), () => WriteReady && DraftValid(out _), "ct-btn-primary"),
                    Button("back", () => { _preview = null; Render(); })));
            }
            private void RenderClose()
            {
                var record = _closeReview;
                _content.Add(El.Div("ct-perp-order", Copy("closeReview", "ct-perp-title"), Position(record, false), Copy("closeTerms", "ct-perp-note"),
                    Button("closeConfirm", () =>
                    {
                        var intent = new PerpIntent { Key = Guid.NewGuid().ToString(), Kind = "close", Position = record.Id };
                        Run(c => Submit(intent, c));
                    }, () => WriteReady && _positions.Any(p => p.Id == record.Id && p.Status == "open"), "ct-btn-primary"),
                    Button("cancel", () => { _closeReview = null; Render(); })));
            }
            private void RenderPositions()
            {
                _content.Add(Copy("positions", "ct-perp-eyebrow"));
                var tabs = El.Div("ct-perp-split");
                var open = Button("openTab", () => { _history = false; Render(); }, null, !_history ? "ct-perp-selected" : "");
                open.text = PerpsStrings.F("openTab", _positions.Count(p => p.Status == "open"));
                var history = Button("historyTab", () => { _history = true; Render(); }, null, _history ? "ct-perp-selected" : "");
                history.text = PerpsStrings.F("historyTab", _positions.Count(p => p.Status != "open"));
                tabs.Add(open); tabs.Add(history); _content.Add(tabs);
                var rows = _positions.Where(p => _history ? p.Status != "open" : p.Status == "open").ToList();
                if (rows.Count == 0) _content.Add(El.Div("ct-perp-empty", Copy(_history ? "emptyReceipts" : "emptyPositions", "ct-perp-title"), !_history ? Copy("emptyPositionsDetail") : null));
                foreach (var record in rows) _content.Add(Position(record, true));
                _content.Add(Copy("limitNote", "ct-perp-note"));
            }
            private VisualElement Position(PerpRecord record, bool action)
            {
                var coin = _coins.FirstOrDefault(c => c.Id == record.CoinId);
                var ticker = !string.IsNullOrEmpty(record.Ticker) ? record.Ticker : coin?.Ticker ?? PerpsStrings.F("coinId", record.CoinId.ToString().Substring(0, 8));
                var row = El.Div("ct-perp-position", El.Div("ct-perp-top", Label(PerpsStrings.F("summary", ticker, PerpsStrings.T(record.Side), record.Leverage), "ct-perp-title ct-grow"),
                    Copy(record.Status, "ct-perp-status" + (record.Status == "liquidated" ? " ct-red" : ""))),
                    El.Div("ct-perp-metrics", Metric("marginLabel", PerpsStrings.Cash(record.Margin)), Metric("entry", PerpsStrings.Price(record.Entry)), Metric("liquidation", PerpsStrings.Price(record.Liquidation))),
                    Label(PerpsStrings.F("positionId", record.Id), "ct-perp-note"));
                row.name = "perps-position-" + record.Id;
                if (record.Status != "open")
                {
                    row.Add(record.Realized.HasValue ? Label(PerpsStrings.F("realized", PerpsStrings.Cash(record.Realized.Value)), "ct-perp-copy " + (record.Realized.Value < 0 ? "ct-red" : "ct-green")) : Copy("realizedMissing", "ct-perp-note"));
                    row.Add(Copy("receiptNote", "ct-perp-note"));
                    if (record.ClosedAt.HasValue) row.Add(Label(PerpsStrings.F("closeTime", record.ClosedAt.Value.UtcDateTime.ToString("MMM d HH:mm", CultureInfo.CurrentCulture)), "ct-perp-note"));
                }
                else if (action) row.Add(Button("close", () => { _preview = null; _closeReview = record; Render(); }, () => _pending == null && !_journalBroken && Fresh));
                return row;
            }
            private static VisualElement Metric(string title, string value) => El.Div("ct-perp-metric", Copy(title, "ct-perp-eyebrow"), Label(value, "ct-perp-number"));

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            private void SeedReview(string scenario)
            {
                var coinId = Guid.Parse("30000000-0000-4000-8000-000000000001");
                var coin = new PerpCoin { Id = coinId, Name = "Review fixture · conviction", Ticker = "REVIEW", Stage = "graduated", Price = 1240000, ChangeBps = 380 };
                _account = new PerpAccount { User = Guid.Parse("30000000-0000-4000-8000-000000000002"), Cash = 125000, Cap = 5 };
                _positions = new List<PerpRecord>(); _coins.Add(coin); _margin = "25.00"; _leverage = 3;
                // Deliberately labelled sample history; never reachable from the normal app constructor.
                _market = new PerpMarket { Coin = coin, Points = new List<PerpPoint> { new PerpPoint(1791288000000, 1100000), new PerpPoint(1791288060000, 1130000), new PerpPoint(1791288120000, 1080000), new PerpPoint(1791288180000, 1220000), new PerpPoint(1791288240000, 1180000), new PerpPoint(1791288300000, 1240000) } };
                var record = new PerpRecord { Id = Guid.Parse("30000000-0000-4000-8000-000000000003"), CoinId = coinId, Ticker = coin.Ticker, Side = "long", Status = "open", Leverage = 3, Margin = 2500, Entry = 1100000, Liquidation = 733333 };
                if (scenario == "active" || scenario == "ambiguous-close" || scenario == "receipt") _positions.Add(record);
                if (scenario == "ambiguous-close") _pending = new PerpIntent { Key = "30000000-0000-4000-8000-000000000004", Kind = "close", Position = record.Id };
                if (scenario == "receipt") { record.Status = "closed"; record.Realized = 954; record.ClosedAt = DateTimeOffset.FromUnixTimeMilliseconds(1791288300000); _history = true; _receipt = record; }
                _unavailable = scenario == "unavailable";
                _fresh = scenario != "error" && !_unavailable; _received = Time.realtimeSinceStartupAsDouble;
                Message(_unavailable ? "unavailable" : scenario == "error" ? "network" : null);
                Render(); Tick();
            }
#endif
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        public static VisualElement BuildReview(string scenario)
        {
            if (!new[] { "unavailable", "empty", "active", "error", "ambiguous-close", "receipt" }.Contains(scenario)) throw new ArgumentException("Unknown review scenario");
            return new View(false, null, scenario).Root;
        }
#endif
    }

    internal sealed class PerpChart : VisualElement
    {
        private readonly List<PerpPoint> _points;
        public PerpChart(List<PerpPoint> points)
        {
            _points = points; AddToClassList("ct-perp-chart"); pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            if (_points == null || _points.Count < 2 || contentRect.width <= 0 || contentRect.height <= 0) return;
            var low = _points.Min(p => (decimal)p.Price); var high = _points.Max(p => (decimal)p.Price);
            var start = _points[0].At; var duration = (decimal)_points[_points.Count - 1].At - start;
            if (duration <= 0) return;
            var spread = high - low; if (spread == 0) spread = Math.Max(1m, high * .02m);
            var painter = context.painter2D; painter.strokeColor = resolvedStyle.color; painter.lineWidth = 4;
            painter.BeginPath();
            for (var i = 0; i < _points.Count; i++)
            {
                var point = _points[i];
                var x = contentRect.xMin + 8 + (float)(((decimal)point.At - start) / duration) * (contentRect.width - 16);
                var y = contentRect.yMin + 16 + (high == low ? .5f : (float)((high - point.Price) / spread)) * (contentRect.height - 32);
                if (i == 0) painter.MoveTo(new Vector2(x, y)); else painter.LineTo(new Vector2(x, y));
            }
            painter.Stroke();
        }
    }
}
