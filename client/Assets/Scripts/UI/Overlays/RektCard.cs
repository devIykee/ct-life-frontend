using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Realtime;
using CTLife.World;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// The REKT card (feature #16): city `liquidated {perpId, coinId, lossCents, at}` to this player. Plays the
    /// liquidation stinger and the avatar's liquidation reaction. The share card export lands with Phase 2 trading.
    /// </summary>
    public sealed class RektCard : IDisposable
    {
        private readonly VisualElement _layer;
        private readonly HashSet<VisualElement> _cards = new HashSet<VisualElement>();
        private bool _disposed;
        private readonly Subscriptions _subs = new Subscriptions();

        public RektCard(VisualElement modalLayer)
        {
            _layer = modalLayer;
            _layer.RegisterCallback<DetachFromPanelEvent>(OnDetached);
            _subs.On<LiquidatedMessage>(Show);
        }

        private bool Owns(VisualElement card) => !_disposed && _cards.Contains(card) && card.parent == _layer;

        private void OnDetached(DetachFromPanelEvent e)
        {
            if (e.target == _layer) Dispose();
        }

        private void OnCardDetached(DetachFromPanelEvent e)
        {
            if (e.target is VisualElement card && _cards.Remove(card))
                card.UnregisterCallback<DetachFromPanelEvent>(OnCardDetached);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subs.Dispose();
            _layer.UnregisterCallback<DetachFromPanelEvent>(OnDetached);
            foreach (var card in _cards)
            {
                card.UnregisterCallback<DetachFromPanelEvent>(OnCardDetached);
                card.RemoveFromHierarchy();
            }
            _cards.Clear();
        }

        public void Show(LiquidatedMessage m)
        {
            if (_disposed) return;
            long loss = (long)m.lossCents;
            Bus.Publish(new SfxSignal("sfx_liquidation"));
            var local = VenueSession.Current?.LocalPlayer;
            if (local != null) local.GetComponent<AvatarView>()?.PlayOneShot("react_liquidated");

            VisualElement card = null;
            card = El.Div("ct-modal-backdrop",
                El.Div("ct-rekt",
                    El.Text(Strings.T("rekt.title"), "ct-rekt-title"),
                    El.Text("-" + Money.Cash(loss).TrimStart('-'), "ct-rekt-loss"),
                    El.Text(Strings.T("rekt.body"), "ct-body"),
                    El.Text(Strings.F("rekt.coin", m.coinId), "ct-small ct-muted"),
                    El.Div("ct-row",
                        El.Btn(Strings.T("rekt.share"), () => { if (Owns(card)) Bus.Publish(ToastSignal.Text(Strings.T("rekt.shareUnavailable"))); }, "ct-btn ct-btn-ghost"),
                        El.Btn(Strings.T("rekt.dismiss"), () => { if (Owns(card)) card.RemoveFromHierarchy(); }, "ct-btn ct-btn-primary"))));
            card.pickingMode = PickingMode.Position;
            _cards.Add(card);
            card.RegisterCallback<DetachFromPanelEvent>(OnCardDetached);
            _layer.Add(card);
        }
    }
}
