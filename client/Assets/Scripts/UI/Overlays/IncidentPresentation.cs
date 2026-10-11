using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CTLife.Core;
using CTLife.Realtime;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// City-channel incidents only. No polling, invented activity, balance updates or financial actions.
    /// One brief card leaves the world readable; receipts retain missed incidents during this sign-in session.
    /// </summary>
    public sealed class IncidentPresentation : IDisposable
    {
        private const float CardSeconds = 8f;
        private readonly UiRoot _ui;
        private readonly VisualElement _root;
        private readonly VisualElement _cardSlot;
        private readonly VisualElement _drawer;
        private readonly ScrollView _receipts;
        private readonly Button _history;
        private readonly IncidentFeed _feed = new IncidentFeed();
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly Session _session;
        private string _userId;
        private IncidentReceipt _current;
        private float _until;
        private bool _drawerOpen;
        private bool _disposed;

        public IncidentPresentation(UiRoot ui)
        {
            _ui = ui;
            _root = El.Div("ct-incidents");
            _root.name = "city-incidents";
            _root.pickingMode = PickingMode.Ignore;
            var sheet = Resources.Load<StyleSheet>("UI/incidents");
            if (sheet != null) _root.styleSheets.Add(sheet);
            else Log.Warn("ui", "Missing UI/incidents stylesheet");
            _cardSlot = El.Div("ct-incident-slot");
            _cardSlot.pickingMode = PickingMode.Ignore;
            _history = El.Btn("", ToggleHistory, "ct-incident-history");
            _history.Show(false);
            _receipts = El.VerticalScroll("ct-incident-receipts");
            _drawer = El.Div("ct-incident-drawer",
                El.Div("ct-incident-drawer-heading",
                    Plain(IncidentStrings.T("recent"), "ct-incident-drawer-title"),
                    El.IconButton("close", IncidentStrings.T("close"), CloseHistory, "ct-incident-close")),
                _receipts);
            _drawer.Show(false);
            _root.Add(_cardSlot);
            _root.Add(_history);
            _root.Add(_drawer);
            // Below full-screen menus, phone and modals; empty screen area remains non-pickable.
            _ui.HudLayer.Add(_root);
            _root.RegisterCallback<DetachFromPanelEvent>(OnDetached);
            _subs.On<EventMessage>(Receive);
            if (Services.TryGet<Session>(out var session))
            {
                _session = session;
                _userId = session.UserId;
                session.Changed += OnSessionChanged;
            }
            _ui.AddUpdater(Tick);
        }

        private void Receive(EventMessage message)
        {
            if (_disposed) return;
            if (_feed.Accept(message, Time.unscaledTime, ServerClock.NowMs, ServerClock.IsSynced))
            {
                RefreshHistory();
                if (_drawerOpen) RenderReceipts();
            }
        }

        private void Tick()
        {
            if (_disposed) return;
            float now = Time.unscaledTime;
            // Timers continue under menus so returning from a call does not replay a backlog as breaking news.
            if (_current != null && now >= _until) Dismiss();
            bool obscured = HasVisibleChildren(_ui.ScreenLayer) || HasVisibleChildren(_ui.PhoneLayer) ||
                HasVisibleChildren(_ui.ModalLayer);
            _root.Show(!obscured && (_feed.History.Count > 0 || _current != null));
            if (obscured || _drawerOpen || _current != null) return;
            var next = _feed.TakeNext(now);
            if (next != null) Show(next, now);
        }

        private static bool HasVisibleChildren(VisualElement layer)
        {
            if (layer == null) return false;
            foreach (var child in layer.Children())
                if (child.resolvedStyle.display != DisplayStyle.None) return true;
            return false;
        }

        private void Show(IncidentReceipt receipt, float now)
        {
            _current = receipt;
            _until = now + CardSeconds;
            _cardSlot.Clear();
            var copy = El.Div("ct-incident-copy",
                Plain(IncidentStrings.T("wire"), "ct-incident-eyebrow"),
                Plain(receipt.Title, "ct-incident-title"),
                Plain(receipt.Body, "ct-incident-body"));
            if (!string.IsNullOrEmpty(receipt.Detail)) copy.Add(Plain(receipt.Detail, "ct-incident-detail"));
            copy.Add(Plain(receipt.Context, "ct-incident-context"));
            var card = El.Div("ct-incident-card ct-incident-" + receipt.Tone,
                El.Div("ct-incident-symbol", new UiIcon(receipt.Icon)), copy,
                El.IconButton("close", IncidentStrings.T("dismiss"), Dismiss, "ct-incident-close"));
            // Only buttons take input. Dragging over the headline can still orbit the camera.
            card.pickingMode = PickingMode.Ignore;
            El.PassThrough(copy);
            El.PassThrough(card[0]);
            _cardSlot.Add(card);
            Bus.Publish(new SfxSignal(receipt.Sfx));
        }

        private void Dismiss()
        {
            if (_disposed) return;
            _current = null;
            _cardSlot.Clear();
        }

        private void ToggleHistory()
        {
            if (_disposed) return;
            _drawerOpen = !_drawerOpen;
            _drawer.Show(_drawerOpen);
            _cardSlot.Show(!_drawerOpen);
            if (_drawerOpen)
            {
                // Explicitly opening receipts consumes the banner queue; all incidents remain in the list.
                Dismiss();
                _feed.ClearPending();
                RenderReceipts();
            }
        }

        private void CloseHistory()
        {
            if (_disposed) return;
            _drawerOpen = false;
            _drawer.Show(false);
            _cardSlot.Show(true);
        }

        private void RefreshHistory()
        {
            _history.text = IncidentStrings.F("history", _feed.History.Count);
            _history.Show(_feed.History.Count > 0);
        }

        private void RenderReceipts()
        {
            _receipts.Clear();
            foreach (var receipt in _feed.History)
            {
                var row = El.Div("ct-incident-receipt ct-incident-" + receipt.Tone,
                    Plain(receipt.Title, "ct-incident-receipt-title"),
                    Plain(receipt.Body, "ct-incident-body"));
                if (!string.IsNullOrEmpty(receipt.Detail)) row.Add(Plain(receipt.Detail, "ct-incident-detail"));
                string when = !receipt.FreshAtReceipt ? IncidentStrings.T("earlier") :
                    Time.unscaledTime - receipt.ReceivedAt < 60f ? IncidentStrings.T("now") :
                    IncidentStrings.F("minute", (int)((Time.unscaledTime - receipt.ReceivedAt) / 60f));
                row.Add(Plain(when + " · " + receipt.Context, "ct-incident-context"));
                _receipts.Add(row);
            }
        }

        internal static Label Plain(string text, string cls)
        {
            var label = El.Text(text, cls);
            label.enableRichText = false;
            return label;
        }

        private void OnSessionChanged()
        {
            if (_disposed) return;
            if (_session.IsSignedIn && _session.UserId == _userId) return;
            _userId = _session.UserId;
            _feed.Clear();
            Dismiss();
            CloseHistory();
            _receipts.Clear();
            RefreshHistory();
            _root.Show(false);
        }

        private void OnDetached(DetachFromPanelEvent e)
        {
            if (e.target == _root) Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subs.Dispose();
            if (_session != null) _session.Changed -= OnSessionChanged;
            if (_ui != null) _ui.RemoveUpdater(Tick);
            _feed.Clear();
            _current = null;
            _drawerOpen = false;
            _root.UnregisterCallback<DetachFromPanelEvent>(OnDetached);
            _root.RemoveFromHierarchy();
            _cardSlot.Clear();
            _receipts.Clear();
        }
    }

    /// <summary>Bounded, deterministic presentation state. Arrival time is monotonic; server epoch is display/order only.</summary>
    internal sealed class IncidentFeed
    {
        internal const int HistoryLimit = 12;
        internal const int PendingLimit = 4;
        internal const float PendingSeconds = 30f;
        private readonly List<IncidentReceipt> _history = new List<IncidentReceipt>();
        private readonly List<IncidentReceipt> _pending = new List<IncidentReceipt>();
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> _seenOrder = new Queue<string>();
        internal IReadOnlyList<IncidentReceipt> History => _history;

        internal bool Accept(EventMessage message, float receivedAt, double serverNow, bool synced)
        {
            if (message == null || double.IsNaN(message.at) || double.IsInfinity(message.at) || message.at < 0 ||
                message.at != Math.Floor(message.at)) return false;
            if (synced && (message.at > serverNow + 60000 || message.at < serverNow - 600000)) return false;
            var receipt = IncidentReceipt.From(message);
            if (receipt == null) return false;
            var key = Key(message);
            if (!_seen.Add(key)) return false;
            _seenOrder.Enqueue(key);
            while (_seenOrder.Count > 64) _seen.Remove(_seenOrder.Dequeue());
            receipt.At = message.at;
            receipt.ReceivedAt = receivedAt;
            // Without a server clock, do not label historical messages as current breaking news.
            receipt.FreshAtReceipt = synced && serverNow - message.at <= 30000;
            _history.Add(receipt);
            _history.Sort((a, b) => b.At.CompareTo(a.At));
            if (_history.Count > HistoryLimit) _history.RemoveAt(_history.Count - 1);
            if (receipt.FreshAtReceipt)
            {
                _pending.Add(receipt);
                _pending.Sort((a, b) => b.At.CompareTo(a.At));
                if (_pending.Count > PendingLimit) _pending.RemoveAt(_pending.Count - 1);
            }
            return true;
        }

        internal IncidentReceipt TakeNext(float now)
        {
            while (_pending.Count > 0)
            {
                var next = _pending[0];
                _pending.RemoveAt(0);
                if (now - next.ReceivedAt <= PendingSeconds) return next;
            }
            return null;
        }

        internal void ClearPending() => _pending.Clear();
        internal void Clear() { _history.Clear(); _pending.Clear(); _seen.Clear(); _seenOrder.Clear(); }

        private static string Key(EventMessage message)
        {
            var key = new StringBuilder(message.eventId).Append('|').Append(message.at.ToString("R", CultureInfo.InvariantCulture));
            if (message.@params == null) return key.ToString();
            var keys = new List<string>(message.@params.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var name in keys)
            {
                // Length prefixes keep separators in user aliases from colliding with another payload.
                var value = message.@params[name] ?? "";
                key.Append('|').Append(name.Length).Append(':').Append(name).Append(value.Length).Append(':').Append(value);
            }
            return key.ToString();
        }
    }

    internal sealed class IncidentReceipt
    {
        internal string Title, Body, Detail, Context, Tone, Icon, Sfx;
        internal double At;
        internal float ReceivedAt;
        internal bool FreshAtReceipt;

        internal static IncidentReceipt From(EventMessage e)
        {
            string P(string key) => e.@params != null && e.@params.TryGetValue(key, out var value) ? Clean(value) : null;
            string Cash(string key) => Money.TryParseCents(P(key), out var cents) && cents >= 0 ? Money.Cash(cents, true) : null;
            string ticker = P("ticker");
            if (!string.IsNullOrEmpty(ticker)) ticker = "$" + ticker.TrimStart('$');
            var r = new IncidentReceipt { Tone = "news", Icon = "timeline", Sfx = "sfx_news_flash", Context = IncidentStrings.T("fictional") };
            switch (e.eventId)
            {
                case "evt_liquidation":
                    r.Title = IncidentStrings.T("liquidation.title");
                    r.Body = P("alias") != null && ticker != null ? IncidentStrings.F("liquidation.actor", P("alias"), ticker) : IncidentStrings.T("liquidation.body");
                    var loss = Cash("lossCents");
                    r.Detail = loss != null ? IncidentStrings.F("liquidation.loss", loss) : null;
                    r.Tone = "loss"; r.Icon = "perps"; r.Sfx = "sfx_liquidation"; break;
                case "evt_rug":
                    r.Title = IncidentStrings.T("rug.title");
                    r.Body = ticker != null ? IncidentStrings.F("rug.coin", ticker) : IncidentStrings.T("rug.body");
                    var peak = Cash("peakMcapCents");
                    r.Detail = peak != null ? IncidentStrings.F("rug.peak", peak) : null;
                    r.Tone = "loss"; r.Icon = "pulse"; r.Sfx = "sfx_rug"; break;
                case "evt_coin_launch":
                    r.Title = IncidentStrings.T("launch.title");
                    r.Body = ticker != null ? IncidentStrings.F("launch.coin", ticker) : IncidentStrings.T("launch.body");
                    r.Icon = "projects"; r.Sfx = "sfx_new_coin"; break;
                case "evt_mood_change":
                    r.Title = IncidentStrings.T("mood.title");
                    string direction = P("direction");
                    r.Body = IncidentStrings.T(direction == "up" ? "mood.up" : direction == "down" ? "mood.down" : "mood.body");
                    r.Context = IncidentStrings.T("market");
                    if (long.TryParse(P("moodBps"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mood))
                        r.Detail = IncidentStrings.F("mood.value", Money.Bps(mood));
                    r.Tone = direction == "up" ? "good" : direction == "down" ? "loss" : "news";
                    r.Icon = "pulse"; break;
                case "evt_exploit":
                    r.Title = IncidentStrings.T("exploit.title");
                    r.Body = P("target") != null ? IncidentStrings.F("exploit.target", P("target")) : IncidentStrings.T("exploit.body");
                    r.Tone = "loss"; r.Icon = "energy"; r.Sfx = "sfx_rug"; break;
                case "evt_ore_motherlode":
                    r.Title = IncidentStrings.T("ore.title");
                    r.Body = IncidentStrings.T("ore.body");
                    // The contract does not define the incident amount key/denomination. Do not label
                    // an arbitrary numeric string as whole ORE (token amounts elsewhere are base units).
                    r.Context = IncidentStrings.T("ore");
                    r.Tone = "ore"; r.Icon = "ore"; r.Sfx = "sfx_moon_horn"; break;
                default:
                    Log.UnknownId("events", e.eventId);
                    return null;
            }
            return r;
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = new StringBuilder();
            foreach (char c in value)
            {
                if (char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format) continue;
                text.Append(c);
                if (text.Length >= 80) break;
            }
            var result = text.ToString().Trim();
            return result.Length == 0 ? null : result;
        }
    }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
    /// <summary>Explicit data checks only; does not publish fixture events or connect to a server.</summary>
    public static class IncidentPresentationChecks
    {
        public static string Run()
        {
            const double epoch = 1801828800000;
            var feed = new IncidentFeed();
            EventMessage Make(int offset, string ticker = "TEST") => new EventMessage
            {
                eventId = "evt_coin_launch", at = epoch + offset,
                @params = new Dictionary<string, string> { ["ticker"] = ticker, ["name"] = "Fixture only" }
            };
            void Require(bool ok, string reason) { if (!ok) throw new InvalidOperationException("Incident check: " + reason); }
            Require(feed.Accept(Make(0), 0, epoch, true), "initial event rejected");
            var duplicate = Make(0);
            duplicate.@params = new Dictionary<string, string> { ["name"] = "Fixture only", ["ticker"] = "TEST" };
            Require(!feed.Accept(duplicate, 1, epoch, true), "parameter order defeats duplicate suppression");
            Require(feed.Accept(Make(2000), 2, epoch + 2000, true), "new event rejected");
            Require(feed.TakeNext(3)?.At == epoch + 2000, "queue must prefer the newest server timestamp");
            Require(feed.TakeNext(31) == null, "queued event outlived 30-second TTL");
            Require(feed.History.Count == 2, "expired headline lost its receipt");
            feed.Clear();
            for (int i = 0; i < 30; i++) feed.Accept(Make(i), i, epoch + i, true);
            Require(feed.History.Count == IncidentFeed.HistoryLimit, "receipt history unbounded");
            int count = 0; while (feed.TakeNext(30) != null) count++;
            Require(count == IncidentFeed.PendingLimit, "burst queue unbounded");
            feed.Clear();
            Require(feed.Accept(Make(0), 1, epoch + 40000, true) && feed.TakeNext(1) == null, "stale event shown as fresh");
            feed.Clear();
            Require(feed.Accept(Make(0), 1, epoch, false) && feed.TakeNext(1) == null, "unsynced clock claims fresh news");
            Require(!feed.Accept(Make(100000), 1, epoch, true), "implausible future timestamp accepted");
            Require(!feed.Accept(new EventMessage { eventId = "evt_rug", at = double.NaN }, 1, epoch, true), "NaN timestamp accepted");
            Require(!feed.Accept(new EventMessage { eventId = "uncontracted", at = epoch }, 1, epoch, true), "unknown contract event presented");
            var missing = IncidentReceipt.From(new EventMessage { eventId = "evt_liquidation", at = epoch });
            Require(missing != null && missing.Detail == null && !missing.Body.Contains("?"), "missing payload fabricated detail");
            var ore = IncidentReceipt.From(new EventMessage { eventId = "evt_ore_motherlode", at = epoch,
                @params = new Dictionary<string, string> { ["amountOre"] = "5500000000000" } });
            Require(ore.Detail == null, "unconfirmed ORE units were presented as whole tokens");
            var malformed = Make(1, "<size=999>TEST</size>\n");
            var display = IncidentReceipt.From(malformed);
            Require(!display.Body.Contains("\n"), "control character reached copy");
            var label = IncidentPresentation.Plain(display.Body, "ct-incident-body");
            Require(!label.enableRichText, "untrusted payload permits rich text");
            feed.Clear();
            Require(feed.History.Count == 0 && feed.TakeNext(1) == null && feed.Accept(Make(0), 1, epoch, true), "session reset retained another user's state");
            return "PASS: dedupe, chronological queue, TTL, bounded history/burst, stale/unsynced/future timestamps, contract IDs, missing fields, text safety, session reset";
        }
    }
#endif
}
