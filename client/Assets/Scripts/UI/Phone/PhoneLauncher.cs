using System;
using System.Collections.Generic;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Home-screen pages with a persistent dock. Only a deliberate horizontal drag changes pages.</summary>
    public sealed class PhoneLauncher : VisualElement
    {
        public const int AppsPerPage = 16;
        private static readonly string[] DockIds = { "map", "wallet", "messages", "spaces" };
        private readonly Func<PhoneApp, Button> _tile;
        private readonly VisualElement _viewport, _track, _dots, _dock;
        private readonly VisualElement _searchLayer;
        private readonly TextField _searchField;
        private readonly ScrollView _searchResults;
        private readonly PhoneWidgets _widgets;
        private readonly PhoneGlass _dockGlass, _searchGlass;
        private readonly List<PhoneApp> _catalog = new();
        private readonly List<VisualElement> _pages = new();
        private readonly List<Button> _pageButtons = new();
        private Tween _slide;
        private int _pointer = -1;
        private Vector2 _start, _last;
        private bool _dragged, _horizontal;
        private float _offset;
        public int PageIndex { get; private set; }
        public int PageCount => _pages.Count;
        public bool SuppressClick { get; private set; }
        public bool IsSearching { get; private set; }
        public event Action PageChanged;

        public PhoneLauncher(Func<PhoneApp, Button> tile, Action<string> launch = null)
        {
            _tile = tile ?? throw new ArgumentNullException(nameof(tile));
            AddToClassList("ct-phone-home");
            _track = El.Div("ct-phone-page-track");
            _viewport = El.Div("ct-phone-page-viewport", _track);
            _dots = El.Div("ct-phone-pages");
            _dock = El.Div("ct-phone-dock");
            _dock.AddToClassList("ct-os-glass");
            _dockGlass = new PhoneGlass(_dock);
            _widgets = new PhoneWidgets(launch ?? (_ => { }));
            var search = new Button(OpenSearch) { name = "phone-search", tooltip = Strings.T("phone.search") };
            search.AddToClassList("ct-phone-search-button");
            search.AddToClassList("ct-os-glass");
            search.Add(new PhoneSystemIcon("search"));
            _searchGlass = new PhoneGlass(search);
            _searchField = new TextField { name = "phone-search-input", maxLength = 60 };
            _searchField.AddToClassList("ct-phone-search-field");
            _searchField.textEdition.placeholder = Strings.T("phone.search");
            _searchField.RegisterValueChangedCallback(_ => FilterApps());
            var cancel = new Button(CloseSearch) { text = Strings.T("common.cancel"), name = "phone-search-cancel" };
            cancel.AddToClassList("ct-phone-search-cancel");
            _searchResults = new ScrollView(ScrollViewMode.Vertical) { name = "phone-search-results" };
            _searchResults.AddToClassList("ct-phone-search-results");
            _searchLayer = El.Div("ct-phone-search-layer", El.Div("ct-phone-search-bar", _searchField, cancel), _searchResults);
            _searchLayer.Show(false);
            var controls = El.Div("ct-phone-control-rail", _dock, search);
            Add(_widgets); Add(_widgets.EventsRow); Add(_viewport); Add(_dots); Add(controls); Add(_searchLayer);
            RegisterCallback<PointerDownEvent>(_ => SuppressClick = false, TrickleDown.TrickleDown);
            RegisterCallback<NavigationSubmitEvent>(_ => SuppressClick = false, TrickleDown.TrickleDown);
            RegisterCallback<KeyDownEvent>(OnKey);
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (!IsSearching || e.keyCode != KeyCode.Escape) return;
                CloseSearch();
                e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            _viewport.RegisterCallback<GeometryChangedEvent>(_ => Snap());
            _viewport.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            _viewport.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            _viewport.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            _viewport.RegisterCallback<PointerCancelEvent>(e => CancelPointer(e.pointerId), TrickleDown.TrickleDown);
            _viewport.RegisterCallback<PointerCaptureOutEvent>(e =>
            {
                // A child Button losing capture to this viewport is the start of a swipe, not its cancellation.
                if (ReferenceEquals(e.target, _viewport)) CancelPointer(e.pointerId);
            });
            RegisterCallback<DetachFromPanelEvent>(e => { if (ReferenceEquals(e.target, this)) Suspend(); });
        }

        public void SetApps(IReadOnlyList<PhoneApp> apps, int livePhase)
        {
            Suspend();
            HideSearch(false);
            _catalog.Clear();
            foreach (var app in apps) if (app.ShowOnHome) _catalog.Add(app);
            _widgets.Refresh();
            _track.Clear(); _dots.Clear(); _dock.Clear(); _pages.Clear(); _pageButtons.Clear();
            _dock.Add(_dockGlass);
            var gridApps = new List<PhoneApp>();
            // Live routes precede planned ones, but every registered route remains reachable.
            for (int pass = 0; pass < 2; pass++)
                foreach (var app in apps)
                    if ((app.Phase <= livePhase) == (pass == 0) && Array.IndexOf(DockIds, app.Id) < 0 && app.ShowOnHome)
                        gridApps.Add(app);
            foreach (string id in DockIds)
                foreach (var app in apps)
                    if (app.Id == id) { _dock.Add(_tile(app)); break; }
            _dock.Show(_dock.childCount > 1);
            int count = Math.Max(1, (gridApps.Count + AppsPerPage - 1) / AppsPerPage);
            _track.style.width = Length.Percent(count * 100f);
            for (int i = 0; i < count; i++)
            {
                var page = El.Div("ct-phone-grid ct-phone-page");
                page.name = "phone-page-" + i;
                page.style.width = Length.Percent(100f / count);
                for (int n = i * AppsPerPage; n < Math.Min((i + 1) * AppsPerPage, gridApps.Count); n++)
                    page.Add(GridTile(gridApps[n]));
                _track.Add(page); _pages.Add(page);
                int index = i;
                var button = El.Btn("", () => ShowPage(index), "ct-phone-page-button");
                button.name = "phone-page-dot-" + i;
                button.tooltip = Strings.F("phone.page", i + 1, count);
                button.Add(El.PassThrough(El.Div("ct-phone-page-dot")));
                _dots.Add(button); _pageButtons.Add(button);
            }
            _dots.Show(count > 1);
            ShowPage(PageIndex, false);
        }

        public void RefreshWidgets() => _widgets.Refresh();
        public void RefreshGlass() { _dockGlass.Refresh(); _searchGlass.Refresh(); }

        public void OpenSearch()
        {
            Suspend();
            IsSearching = true;
            AddToClassList("ct-phone-searching");
            _searchLayer.Show(true);
            _searchField.SetValueWithoutNotify("");
            FilterApps();
            _searchField.schedule.Execute(() => { if (IsSearching) _searchField.Focus(); });
        }

        public void CloseSearch() => HideSearch(true);

        private void HideSearch(bool restoreFocus)
        {
            IsSearching = false;
            RemoveFromClassList("ct-phone-searching");
            SoftKeyboard.Hide(_searchField);
            _searchField?.Blur();
            _searchLayer?.Show(false);
            _searchResults?.Clear();
            if (restoreFocus) this.Q<Button>("phone-search")?.Focus();
        }

        private void FilterApps()
        {
            _searchResults.Clear();
            string query = _searchField.value.Trim();
            int matches = 0;
            foreach (var app in _catalog)
            {
                if (query.Length > 0 && app.DisplayTitle.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var button = _tile(app);
                button.AddToClassList("ct-phone-search-result");
                _searchResults.Add(button);
                matches++;
            }
            if (matches == 0) _searchResults.Add(El.Text(Strings.T("phone.noApps"), "ct-phone-search-empty"));
        }

        private Button GridTile(PhoneApp app)
        {
            var button = _tile(app);
            // Unity sends captured pointer events only to the capturing element, skipping ancestors.
            // Observe the native Button's captured moves before Clickable, then take capture only for a drag.
            button.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerCancelEvent>(e => CancelPointer(e.pointerId), TrickleDown.TrickleDown);
            button.RegisterCallback<PointerCaptureOutEvent>(e =>
            {
                if (!_viewport.HasPointerCapture(e.pointerId)) CancelPointer(e.pointerId);
            });
            return button;
        }

        public void ShowPage(int page, bool animate = true)
        {
            PageIndex = Mathf.Clamp(page, 0, Math.Max(0, PageCount - 1));
            _slide?.Cancel();
            var focused = panel?.focusController?.focusedElement as VisualElement;
            bool focusLeavesPage = false;
            for (int i = 0; i < _pages.Count; i++)
            {
                if (i != PageIndex && focused != null && _pages[i].Contains(focused)) focusLeavesPage = true;
                foreach (var button in _pages[i].Query<Button>().ToList()) button.focusable = i == PageIndex;
                _pageButtons[i].EnableInClassList("ct-phone-page-selected", i == PageIndex);
            }
            if (focusLeavesPage) _pageButtons[PageIndex].Focus();
            PageChanged?.Invoke();
            float target = -PageIndex * PageWidth;
            if (!animate || panel == null) { SetOffset(target); return; }
            float from = _offset;
            _slide = Tween.Run(_track, 0.22f, t => SetOffset(Mathf.Lerp(from, target, t)), Ease.OutCubic);
        }

        public void Suspend()
        {
            if (IsSearching) HideSearch(false);
            _slide?.Cancel(); _slide = null;
            int pointer = _pointer; _pointer = -1;
            if (pointer >= 0 && _viewport.HasPointerCapture(pointer)) _viewport.ReleasePointer(pointer);
            _dragged = false; SuppressClick = false;
        }

        private float PageWidth => float.IsNaN(_viewport.layout.width) ? 0 : Math.Max(0, _viewport.layout.width);
        private void SetOffset(float value) { _offset = value; _track.style.translate = new Translate(value, 0); }
        private void Snap() { if (!_dragged) { _slide?.Cancel(); SetOffset(-PageIndex * PageWidth); } }
        private void OnDown(PointerDownEvent e)
        {
            if (_pointer >= 0 || e.button != 0) return;
            _slide?.Finish();
            _pointer = e.pointerId; _start = _last = e.position;
            _dragged = false; _horizontal = false;
            // Empty page/padding has no native Clickable to retain Move/Up after leaving the viewport.
            bool buttonOwnsPress = false;
            for (var target = e.target as VisualElement; target != null && target != _viewport; target = target.parent)
                if (target is Button) { buttonOwnsPress = true; break; }
            if (!buttonOwnsPress) _viewport.CapturePointer(e.pointerId);
        }
        private void OnMove(PointerMoveEvent e)
        {
            if (e.pointerId != _pointer) return;
            _last = e.position;
            Vector2 delta = _last - _start;
            if (!_dragged && delta.magnitude >= 18f)
            {
                _dragged = true; _horizontal = Mathf.Abs(delta.x) > Mathf.Abs(delta.y) * 1.15f;
                SuppressClick = true;
                // Taking capture cancels the child Button press before PointerUp can launch its app.
                _viewport.CapturePointer(e.pointerId);
            }
            if (!_dragged) return;
            if (_horizontal)
            {
                float dx = delta.x;
                if (PageIndex == 0 && dx > 0 || PageIndex == PageCount - 1 && dx < 0) dx *= 0.18f;
                SetOffset(-PageIndex * PageWidth + dx);
            }
            e.StopImmediatePropagation();
        }
        private void OnUp(PointerUpEvent e)
        {
            if (e.pointerId != _pointer) return;
            bool dragged = _dragged;
            int target = PageIndex;
            // Use the last move position: mobile release events need not retain the touch position.
            if (dragged && _horizontal && Mathf.Abs(_last.x - _start.x) >= Mathf.Max(60f, PageWidth * 0.14f))
                target += _last.x < _start.x ? 1 : -1;
            int pointer = _pointer; _pointer = -1; _dragged = false;
            if (_viewport.HasPointerCapture(pointer)) _viewport.ReleasePointer(pointer);
            if (!dragged) return;
            SuppressClick = true;
            ShowPage(target);
            e.StopImmediatePropagation();
        }
        private void CancelPointer(int pointer)
        {
            if (_pointer != pointer) return;
            _pointer = -1; _dragged = false; SuppressClick = true;
            if (_viewport.HasPointerCapture(pointer)) _viewport.ReleasePointer(pointer);
            ShowPage(PageIndex);
        }
        private void OnKey(KeyDownEvent e)
        {
            if (IsSearching)
            {
                if (e.keyCode == KeyCode.Escape) { CloseSearch(); e.StopPropagation(); }
                return;
            }
            if (PageCount <= 1) return;
            int direction = e.keyCode == KeyCode.RightArrow ? 1 : e.keyCode == KeyCode.LeftArrow ? -1 : 0;
            if (direction == 0 && e.keyCode != KeyCode.Home) return;
            SuppressClick = false;
            ShowPage(e.keyCode == KeyCode.Home ? 0 : PageIndex + direction);
            _pageButtons[PageIndex].Focus();
            e.StopPropagation();
        }
    }
}
