using CTLife.Core;
using CTLife.World;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// The context CTA: a big primary button ("Sit", "Use terminal", "Take the mic", "Lean on the counter", "Leave";
    /// then "Stand up" / "Step away") with optional secondary actions, above the right-hand dock where the thumb
    /// already is. Driven by InteractionController's InteractionPromptSignal; nothing happens until it is pressed.
    /// </summary>
    public sealed class InteractionPrompt
    {
        private readonly Subscriptions _subs = new Subscriptions();
        public VisualElement Element { get; }

        public InteractionPrompt()
        {
            Element = El.Div("ct-hud-interact");
            Element.pickingMode = PickingMode.Ignore;
            Element.Show(false);
            _subs.On<InteractionPromptSignal>(Render);
            _subs.On<InteractionToastSignal>(s => Bus.Publish(ToastSignal.Text(Strings.T(s.key), ToastKind.Bad)));
        }

        private void Render(InteractionPromptSignal s)
        {
            Element.Clear();
            bool any = s.actions != null && s.actions.Count > 0;
            Element.Show(any);
            if (!any) return;
            foreach (var a in s.actions)
            {
                var id = a.id;
                var label = Strings.T(a.labelKey);
                var button = El.Btn("", () => InteractionController.Current?.Perform(id),
                    a.primary ? "ct-interact ct-interact-primary" : "ct-interact ct-interact-secondary");
                button.tooltip = label;
                button.Add(new UiIcon(a.icon));
                button.Add(El.Text(label, "ct-interact-label"));
                Element.Add(button);
            }
            Element.EnableInClassList("ct-interact-busy", s.busy);
        }

        public void Dispose() => _subs.Dispose();
    }
}
