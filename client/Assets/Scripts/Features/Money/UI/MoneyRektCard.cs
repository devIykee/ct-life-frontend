using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Realtime;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>
    /// REKT card for the city `liquidated {perpId, coinId, lossCents, at}` message (feature #16): server loss, the coin's
    /// ticker when known, the liquidation stinger and avatar reaction, and a link to the Rekt Clinic. A drop-in replacement
    /// for CTLife.UI.RektCard (do not run both, or two cards show).
    /// </summary>
    public sealed class MoneyRektCard : IDisposable
    {
        private readonly VisualElement _layer;
        private readonly Action<string> _openApp;
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly HashSet<string> _shown = new HashSet<string>();
        private readonly List<VisualElement> _cards = new List<VisualElement>();
        private bool _disposed;

        /// <param name="modalLayer">UiRoot.Instance.ModalLayer.</param>
        /// <param name="openApp">Opens a phone app by id (e.g. <c>id => phone.Open(id)</c>); null hides the Rekt Clinic button.</param>
        public MoneyRektCard(VisualElement modalLayer, Action<string> openApp = null)
        {
            _layer = modalLayer ?? throw new ArgumentNullException(nameof(modalLayer));
            _openApp = openApp;
            _subs.On<LiquidatedMessage>(Show);
        }

        public void Show(LiquidatedMessage m)
        {
            if (_disposed || m == null) return;
            // Reconnects can replay a message; one card per perp.
            if (!string.IsNullOrEmpty(m.perpId) && !_shown.Add(m.perpId)) return;
            var model = RektCardModel.From(m.perpId, m.coinId, m.lossCents, id => MoneyHub.Current?.Market.Ticker(id));
            Bus.Publish(new SfxSignal("sfx_liquidation"));
            try
            {
                var local = VenueSession.Current != null ? VenueSession.Current.LocalPlayer : null;
                if (local != null && local.TryGetComponent<AvatarView>(out var avatar)) avatar.PlayOneShot("react_liquidated");
            }
            catch (Exception e) { Log.Warn("money", "REKT reaction failed: " + e.Message); }

            VisualElement card = null;
            void Close()
            {
                if (card == null) return;
                card.RemoveFromHierarchy();
                _cards.Remove(card);
            }
            var buttons = El.Div("ct-row");
            if (_openApp != null)
                buttons.Add(El.Btn(MoneyStrings.T("rekt.clinic"), () =>
                {
                    Close();
                    PerpPitApp.RequestRektClinic();
                    _openApp("perps");
                }, "ct-btn ct-btn-ghost"));
            buttons.Add(El.Btn(MoneyStrings.T("rekt.dismiss"), Close, "ct-btn ct-btn-primary"));
            card = El.Div("ct-modal-backdrop",
                El.Div("ct-rekt",
                    MoneyUi.Text(MoneyStrings.T("rekt.title"), "ct-rekt-title"),
                    MoneyUi.Text(model.LossText, "ct-rekt-loss"),
                    MoneyUi.Text(MoneyStrings.T("rekt.body"), "ct-body"),
                    model.Ticker != null ? MoneyUi.Text(MoneyStrings.F("rekt.coin", model.Ticker), "ct-small ct-muted") : null,
                    buttons));
            card.pickingMode = PickingMode.Position;
            _cards.Add(card);
            _layer.Add(card);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subs.Dispose();
            foreach (var c in _cards) c.RemoveFromHierarchy();
            _cards.Clear();
        }
    }

    /// <summary>One-call registration of the Phase 2 money apps, in phone-grid order.</summary>
    public static class MoneyApps
    {
        /// <summary>
        /// Registers Bank (id "bank", Phase 1; replaces CTLife.UI.BankApp), Pulse ("pulse"), Perp Pit ("perps"; replaces
        /// CTLife.UI.PerpsApp), Prediction Pit ("predict"), ORE ("ore") and Top up ("topup"). Each replaces the
        /// ComingApp with the same id in App/PhoneCatalog.cs.
        /// </summary>
        public static IReadOnlyList<PhoneApp> Create() => new PhoneApp[]
        {
            new MoneyBankApp(), new PulseApp(), new PerpPitApp(), new PredictionPitApp(), new OreRefineryApp(), new MoneyBankApp(topUpTile: true),
        };
    }
}
