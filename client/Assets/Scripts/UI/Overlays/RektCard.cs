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
    public sealed class RektCard
    {
        private readonly VisualElement _layer;
        private readonly Subscriptions _subs = new Subscriptions();

        public RektCard(VisualElement modalLayer)
        {
            _layer = modalLayer;
            _subs.On<LiquidatedMessage>(Show);
        }

        public void Show(LiquidatedMessage m)
        {
            long loss = (long)m.lossCents;
            Bus.Publish(new SfxSignal("sfx_liquidation"));
            var local = VenueSession.Current?.LocalPlayer;
            if (local != null) local.GetComponent<AvatarView>()?.PlayOneShot("react_liquidated");

            VisualElement card = null;
            card = El.Div("ct-modal-backdrop",
                El.Div("ct-rekt",
                    El.Text("REKT", "ct-rekt-title"),
                    El.Text("-" + Money.Cash(loss).TrimStart('-'), "ct-rekt-loss"),
                    El.Text("Liquidated. It's so over.", "ct-body"),
                    El.Text("Coin " + m.coinId, "ct-small ct-muted"),
                    El.Div("ct-row",
                        El.Btn("Share", () => Bus.Publish(ToastSignal.Text("Share cards arrive with the PnL card (Phase 2)")), "ct-btn ct-btn-ghost"),
                        El.Btn("We're so back", () => card.RemoveFromHierarchy(), "ct-btn ct-btn-primary"))));
            card.pickingMode = PickingMode.Position;
            _layer.Add(card);
        }
    }
}
