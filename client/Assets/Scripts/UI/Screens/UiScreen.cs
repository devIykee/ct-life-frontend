using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>A full-screen flow screen on the Screens layer. Build once, show/close.</summary>
    public abstract class UiScreen
    {
        public VisualElement Element { get; private set; }

        protected abstract VisualElement Build();

        public void Show()
        {
            if (Element == null)
            {
                Element = Build();
                Element.AddToClassList("ct-screen");
                Element.pickingMode = PickingMode.Position;
            }
            if (Element.parent == null) UiRoot.Instance.ScreenLayer.Add(Element);
            OnShown();
        }

        protected virtual void OnShown() { }

        public virtual void Close()
        {
            Element?.RemoveFromHierarchy();
        }
    }
}
