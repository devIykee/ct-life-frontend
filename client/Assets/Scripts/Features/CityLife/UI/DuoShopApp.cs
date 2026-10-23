using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// The Duo Shop (2026-10-23, backend PR #8): furniture, decor, appliances and food for Cash, delivered to your door.
    /// App Store layout on the Duo kit: large title, category chips, a featured card, item rows with studio thumbnails and a
    /// price pill; a product page with a live 3D turntable; the bag with the delivery window (ASAP / 30 min / 1 hour) and the
    /// destination (your door, or the Residences parcel locker, forced and explained without a flat); checkout in Cash with a
    /// confirmation sheet; order tracking (placed → out for delivery → delivered) with cancel while placed.
    /// </summary>
    public sealed class DuoShopApp : CityApp
    {
        public override string Id => HomeShopService.AppId;
        public override string Title => ShopText.T("app");
        public override string HomeLabel => "Duo Shop";
        public override string Color => "#0a7cff";
        public override DuoChrome Chrome => DuoChrome.Plain;

        internal override CityView CreateView(CityLifeHub hub) => new ShopView(hub);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        // Dev builds: the capture route (HomeShopRoute) drives the open shop: catalogue | product <itemId> | bag | confirm | pay | order <id>
        private static Action<string, string> _devNavigate;
        private static (string screen, string arg)? _devPending;
        public static void Dev(string screen, string arg = null)
        {
            if (_devNavigate != null) _devNavigate(screen, arg); else _devPending = (screen, arg);
        }
#endif

        private enum Screen { Catalogue, Product, Bag, Order, Orders }

        private sealed class ShopView : CityView
        {
            private readonly HomeShopService _svc;
            private readonly Stack<(Screen s, string arg)> _back = new Stack<(Screen, string)>();
            private Screen _screen = Screen.Catalogue;
            private string _arg, _category = "all";
            private string _window = "asap", _dest = "door";
            private VisualElement _bar;
            private Button _bagButton; private Label _bagBadge;
            private CityError _error;
            private bool _busy;
            private Label _etaLabel;
            private VisualElement _preview;
            private Vector2? _dragFrom;

            public ShopView(CityLifeHub hub) : base(hub, ShopText.T("app"))
            {
                _svc = HomeShopService.Current;
                Page.AddToClassList("dshop");
                Page.name = "duoshop-page";
                _bagButton = Duo.Circle(new UiIcon("shop"), ShopText.T("bag"), () => Go(Screen.Bag, null), "duoshop-bag");
                Page.Nav.Right.Add(_bagButton);
                _bagBadge = Duo.Badge(0, "dshop-bag-badge");
                _bagButton.Add(_bagBadge);
                if (_svc == null) { Body.Add(Duo.Empty(CityLifeStrings.T("common.notLive"))); return; }
                _svc.Cart.Changed += CartChanged;
                _svc.OrdersChanged += OrdersChanged;
                _svc.HomeChanged += Render;
                CartChanged();
                Render();
                Load();
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                _devNavigate = DevGo;
                if (_devPending.HasValue) { var p = _devPending.Value; _devPending = null; DevGo(p.screen, p.arg); }
#endif
            }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            private void DevGo(string screen, string arg)
            {
                if (Disposed) return;
                Log.Info("duoshop", "dev " + screen + " " + arg);
                switch (screen)
                {
                    case "catalogue": _back.Clear(); _screen = Screen.Catalogue; _arg = null; Render(); break;
                    case "product": Go(Screen.Product, arg); break;
                    case "bag": Go(Screen.Bag, null); break;
                    case "orders": Go(Screen.Orders, null); break;
                    case "order": Go(Screen.Order, arg ?? _svc.Orders.FirstOrDefault()?.OrderId); break;
                    case "confirm": if (_screen == Screen.Bag && _svc.Catalog != null) Confirm(_svc.Cart.TotalCents(_svc.Catalog, _dest)); break;
                    case "pay": _confirmSheet?.Close(); _confirmSheet = null; Pay(); break;
                }
            }
#endif

            private async void Load()
            {
                try
                {
                    await _svc.LoadCatalog();
                    _error = null;
                }
                catch (Exception e) { _error = CityErrors.From(e, false); Log.Warn("duoshop", "catalogue: " + _error.Code); }
                if (!Alive) return;
                _ = _svc.RefreshOrders();
                if (!_svc.HomeKnown) _svc.RefreshHome(force: true);
                Render();
            }

            public override void Dispose()
            {
                if (_svc != null)
                {
                    _svc.Cart.Changed -= CartChanged;
                    _svc.OrdersChanged -= OrdersChanged;
                    _svc.HomeChanged -= Render;
                }
                FurniturePreview.Instance.HideLive();
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                if (_devNavigate == (Action<string, string>)DevGo) _devNavigate = null;
#endif
                base.Dispose();
            }

            private void CartChanged() { if (!Disposed) Duo.SetBadge(_bagBadge, _svc.Cart.Units); }

            private void OrdersChanged()
            {
                if (Disposed) return;
                if (_screen == Screen.Order || _screen == Screen.Orders || _screen == Screen.Catalogue) Render();
            }

            protected override bool Back()
            {
                if (_back.Count == 0) return false;
                var (s, arg) = _back.Pop();
                _screen = s; _arg = arg;
                Render();
                return true;
            }

            private void Go(Screen s, string arg)
            {
                if (s == _screen && arg == _arg) return;
                _back.Push((_screen, _arg));
                _screen = s; _arg = arg;
                Render();
                Root.scrollOffset = Vector2.zero;
            }

            protected override void Tick()
            {
                if (_screen == Screen.Order && _etaLabel != null)
                {
                    var o = _svc.Orders.Find(x => x.OrderId == _arg);
                    if (o != null && o.Active && o.EtaAtMs.HasValue) _etaLabel.text = ShopText.Until(o.EtaAtMs.Value, Hub.NowMs);
                }
            }

            // ------------------------------------------------------------------ render

            private void Render()
            {
                if (Disposed || _svc == null) return;
                Body.Clear();
                _bar?.RemoveFromHierarchy(); _bar = null;
                _etaLabel = null; _preview = null;
                if (_screen != Screen.Product) FurniturePreview.Instance.HideLive();
                switch (_screen)
                {
                    case Screen.Catalogue: Catalogue(); break;
                    case Screen.Product: Product(); break;
                    case Screen.Bag: Bag(); break;
                    case Screen.Order: Order(); break;
                    case Screen.Orders: OrdersList(); break;
                }
                _bagButton.Show(_screen == Screen.Catalogue || _screen == Screen.Product);
            }

            private void SetTitle(string t, bool large = true)
            {
                Page.Title.text = t;
                Page.Title.Show(large);
                Page.Nav.SetTitle(t);
            }

            private static VisualElement Thumb(string modelKey, string cls)
            {
                var e = El.Div(cls);
                var rt = FurniturePreview.Instance.Thumbnail(modelKey);
                if (rt != null) e.style.backgroundImage = Background.FromRenderTexture(rt);
                e.pickingMode = PickingMode.Ignore;
                return El.Div(cls + "-well", e);
            }

            private static string Dims(Footprint? f) => f.HasValue ? ShopText.F("size.value", f.Value.W.ToString("0.0#"), f.Value.D.ToString("0.0#"), f.Value.H.ToString("0.0#")) : "";

            // ------------------------------------------------------------------ catalogue

            private void Catalogue()
            {
                SetTitle(ShopText.T("app"));
                Body.Add(CityUi.Text(ShopText.T("today"), "dshop-sub"));
                var c = _svc.Catalog;
                if (c == null)
                {
                    if (_error != null) Body.Add(Duo.Empty(CityLifeStrings.T(_error.Key), null, CityLifeStrings.T("common.retry"), Load));
                    else Body.Add(Duo.Footnote(CityLifeStrings.T("common.loading")));
                    return;
                }
                // categories
                var chips = El.Div("dshop-chips");
                foreach (var cat in new[] { "all" }.Concat(c.PresentCategories))
                {
                    var k = cat;
                    var chip = CityUi.Chip(ShopText.Category(cat), () => { _category = k; Render(); }, _category == cat);
                    chip.name = "dshop-cat-" + cat;
                    chips.Add(chip);
                }
                var chipScroll = new ScrollView(ScrollViewMode.Horizontal) { name = "dshop-chip-scroll" };
                chipScroll.AddToClassList("dshop-chip-scroll");
                chipScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                chipScroll.Add(chips);
                Body.Add(chipScroll);
                var items = c.Items.Where(i => _category == "all" || i.Category == _category).ToList();
                // featured: the selection's hero piece (its dearest placeable item), as a real object card
                var featured = items.Where(i => i.Placeable).OrderByDescending(i => i.PriceCents).FirstOrDefault();
                if (featured != null)
                {
                    var card = new Button(() => Go(Screen.Product, featured.ItemId)) { name = "dshop-featured" };
                    card.AddToClassList("dshop-feature");
                    card.Add(El.Text(ShopText.T("featured"), "dshop-feature-kicker"));
                    card.Add(El.Text(featured.Name, "dshop-feature-title"));
                    card.Add(El.Text(ShopText.Category(featured.Category) + " · " + Dims(featured.Footprint), "dshop-feature-sub"));
                    card.Add(Thumb(featured.ModelKey, "dshop-feature-art"));
                    var row = El.Div("dshop-feature-row", El.Text(ShopText.Money(featured.PriceCents), "dshop-feature-price"), El.Div("ct-grow"), AddPill(featured));
                    card.Add(row);
                    foreach (var l in card.Query<Label>().ToList()) l.pickingMode = PickingMode.Ignore;
                    Body.Add(card);
                }
                Body.Add(Duo.SectionHeader(_category == "all" ? ShopText.T("cat.everything") : ShopText.Category(_category)));
                var list = El.Div("dshop-list");
                foreach (var it in items)
                {
                    var item = it;
                    var row = new DuoRow(() => Go(Screen.Product, item.ItemId))
                        .Leading(Thumb(item.ModelKey, "dshop-row-art"))
                        .Title(item.Name, "duo-row-title-strong")
                        .Subtitle(item.Placeable ? ShopText.Category(item.Category) + " · " + Dims(item.Footprint) : ShopText.Category(item.Category))
                        .Trailing(AddPill(item));
                    row.name = "dshop-item-" + item.ItemId;
                    list.Add(row);
                }
                Duo.MarkLast(list);
                Body.Add(list);
                // orders in flight
                var active = _svc.Orders.Where(o => o.Active).ToList();
                if (_svc.Orders.Count > 0)
                {
                    Body.Add(Duo.SectionHeader(ShopText.T("orders"), ShopText.T("seeAll"), () => Go(Screen.Orders, null)));
                    var ol = El.Div("dshop-list");
                    foreach (var o in (active.Count > 0 ? active : _svc.Orders.Take(2)).Take(3)) ol.Add(OrderRow(o));
                    Duo.MarkLast(ol);
                    Body.Add(ol);
                }
                Body.Add(El.Div("dshop-bottom-space"));
            }

            private Button AddPill(ShopItem it)
            {
                int n = _svc.Cart.Count(it.ItemId);
                var pill = Duo.Pill(n > 0 ? ShopText.T("added") + (n > 1 ? " " + n : "") : ShopText.Money(it.PriceCents), () =>
                {
                    if (_svc.Cart.Add(it.ItemId)) { Bus.Publish(new SfxSignal("sfx_ui_tap")); Render(); }
                }, n > 0 ? Duo.PillKind.Primary : Duo.PillKind.Plain);
                pill.name = "dshop-add-" + it.ItemId;
                return pill;
            }

            private DuoRow OrderRow(ShopOrder o)
            {
                var row = new DuoRow(() => Go(Screen.Order, o.OrderId))
                    .Leading(StatusDot(o))
                    .Title(o.Summary, "duo-row-title-strong")
                    .Subtitle(ShopText.Status(o.Status) + (o.Active && o.EtaAtMs.HasValue ? " · " + ShopText.F("eta", ShopText.Clock(o.EtaAtMs.Value)) : ""))
                    .Value(ShopText.Money(o.TotalCents)).Chevron();
                row.name = "dshop-order-" + o.OrderId;
                return row;
            }

            private static VisualElement StatusDot(ShopOrder o)
            {
                var tone = o.Status == OrderStatus.Delivered ? "green" : o.Status == OrderStatus.OutForDelivery ? "blue" : o.Status == OrderStatus.Cancelled ? "gray" : "orange";
                return El.PassThrough(El.Div("dshop-status-tile dshop-tone-" + tone, new UiIcon(o.Status == OrderStatus.Delivered ? "home" : o.Status == OrderStatus.OutForDelivery ? "car" : "shop")));
            }

            // ------------------------------------------------------------------ product

            private void Product()
            {
                var it = _svc.Catalog?.Find(_arg);
                if (it == null) { Back(); return; }
                SetTitle(it.Name, large: false);
                _preview = El.Div("dshop-stage");
                var live = FurniturePreview.Instance.ShowLive(it.ModelKey);
                var art = El.Div("dshop-stage-art");
                art.style.backgroundImage = Background.FromRenderTexture(live);
                art.pickingMode = PickingMode.Ignore;
                _preview.Add(art);
                _preview.Add(El.Text(ShopText.T("drag"), "dshop-stage-hint"));
                _preview.RegisterCallback<PointerDownEvent>(e => { _dragFrom = e.position; FurniturePreview.Instance.Dragging = true; _preview.CapturePointer(e.pointerId); });
                _preview.RegisterCallback<PointerMoveEvent>(e =>
                {
                    if (!_dragFrom.HasValue) return;
                    FurniturePreview.Instance.Turn(-(e.position.x - _dragFrom.Value.x) * 0.35f);
                    _dragFrom = e.position;
                });
                _preview.RegisterCallback<PointerUpEvent>(e => { _dragFrom = null; FurniturePreview.Instance.Dragging = false; if (_preview.HasPointerCapture(e.pointerId)) _preview.ReleasePointer(e.pointerId); });
                Body.Add(_preview);
                Body.Add(El.Text(ShopText.Category(it.Category).ToUpperInvariant(), "dshop-kicker"));
                Body.Add(El.Text(it.Name, "dshop-title"));
                Body.Add(El.Text(ShopText.Money(it.PriceCents), "dshop-price"));
                var facts = El.Div("dshop-facts");
                if (it.Footprint.HasValue) facts.Add(CityUi.Pair(ShopText.T("size"), Dims(it.Footprint)));
                facts.Add(CityUi.Pair(ShopText.T("category"), ShopText.Category(it.Category)));
                facts.Add(CityUi.Pair(ShopText.T("placeable"), it.Placeable ? ShopText.T("placeable.yes") : ShopText.T("placeable.no")));
                facts.Add(CityUi.Pair(ShopText.T("cash"), ShopText.Money(it.PriceCents)));
                Body.Add(facts);
                Body.Add(El.Div("dshop-bottom-space"));
                int n = _svc.Cart.Count(it.ItemId);
                var add = El.Btn(n > 0 ? ShopText.T("added") + " · " + n : ShopText.T("add"), () =>
                {
                    if (_svc.Cart.Add(it.ItemId)) { Bus.Publish(new SfxSignal("sfx_ui_tap")); Render(); }
                }, "dshop-primary");
                add.name = "dshop-add";
                var bag = El.Btn(ShopText.T("bag"), () => Go(Screen.Bag, null), "dshop-secondary");
                bag.name = "dshop-to-bag";
                _bar = n > 0 ? Duo.BottomBar(bag, add) : Duo.BottomBar(add);
                Page.Add(_bar);
            }

            // ------------------------------------------------------------------ bag

            private bool HasDoor => _svc.MyHome?.HasDoor == true;

            private void Bag()
            {
                SetTitle(ShopText.T("bag"));
                var c = _svc.Catalog;
                if (_svc.Cart.IsEmpty || c == null)
                {
                    Body.Add(Duo.Empty(ShopText.T("bag.empty"), ShopText.T("bag.emptyDetail"), ShopText.T("app"), () => Back()));
                    return;
                }
                var list = El.Div("dshop-list");
                foreach (var (itemId, qty) in _svc.Cart.Lines.ToList())
                {
                    var it = c.Find(itemId);
                    if (it == null) continue;
                    var stepper = El.Div("dshop-stepper",
                        El.Btn("−", () => { _svc.Cart.Set(itemId, qty - 1); Render(); }, "dshop-step"),
                        El.Text(qty.ToString(), "dshop-step-n"),
                        El.Btn("+", () => { _svc.Cart.Set(itemId, qty + 1); Render(); }, "dshop-step"));
                    var row = new DuoRow().Leading(Thumb(it.ModelKey, "dshop-row-art")).Title(it.Name, "duo-row-title-strong")
                        .Subtitle(ShopText.Money(it.PriceCents * qty)).Trailing(stepper);
                    row.name = "dshop-line-" + itemId;
                    list.Add(row);
                }
                Duo.MarkLast(list);
                Body.Add(list);

                // delivery window
                Body.Add(Duo.SectionHeader(ShopText.T("window")));
                var windows = c.Windows.Select(w => w.id).ToList();
                int wi = Math.Max(0, windows.IndexOf(_window));
                var seg = new DuoSegmented(windows.Select(ShopText.Window).ToList(), wi, i => { _window = windows[i]; Render(); });
                seg.name = "dshop-window";
                Body.Add(El.Div("dshop-seg", seg));
                int minutes = c.Windows.FirstOrDefault(w => w.id == _window).minutes;
                Body.Add(CityUi.Small(_window == "asap" ? ShopText.F("w.asap.note", minutes) : ShopText.F("w.later.note", minutes)));

                // destination
                Body.Add(Duo.SectionHeader(ShopText.T("dest")));
                if (!HasDoor) _dest = "locker";
                var dests = new[] { "door", "locker" };
                var dseg = new DuoSegmented(new[] { ShopText.T("dest.door"), ShopText.T("dest.locker") }, Array.IndexOf(dests, _dest), i => { _dest = dests[i]; Render(); });
                dseg.name = "dshop-dest";
                dseg.SetEnabled(HasDoor);
                Body.Add(El.Div("dshop-seg", dseg));
                if (!HasDoor) Body.Add(Duo.Notice(ShopText.F("dest.noHome", ShopText.Money(c.LockerFeeCents)), Duo.Tone.Info));
                else Body.Add(CityUi.Small(_dest == "locker" ? ShopText.F("dest.locker.note", ShopText.Money(c.LockerFeeCents))
                    : ShopText.T("dest.door.note")));

                // totals
                Body.Add(Duo.SectionHeader(ShopText.T("total")));
                var totals = El.Div("dshop-facts");
                totals.Add(CityUi.Pair(ShopText.T("items"), ShopText.Money(_svc.Cart.ItemsCents(c))));
                if (_dest == "locker") totals.Add(CityUi.Pair(ShopText.T("fee"), ShopText.Money(c.LockerFeeCents)));
                long total = _svc.Cart.TotalCents(c, _dest);
                totals.Add(CityUi.Pair(ShopText.T("total"), ShopText.Money(total)));
                if (_svc.CashCents >= 0) totals.Add(CityUi.Pair(ShopText.T("balance"), ShopText.Money(_svc.CashCents)));
                Body.Add(totals);
                if (_error != null && _screen == Screen.Bag) Body.Add(Duo.Notice(CityLifeStrings.T(_error.Key), Duo.Tone.Danger));
                Body.Add(El.Div("dshop-bottom-space"));
                var pay = El.Btn(ShopText.T("checkout") + " · " + ShopText.Money(total), () => Confirm(total), "dshop-primary");
                pay.name = "dshop-checkout";
                pay.SetEnabled(!_busy && (_svc.CashCents < 0 || _svc.CashCents >= total));
                _bar = Duo.BottomBar(pay);
                Page.Add(_bar);
            }

            private void Confirm(long total)
            {
                var where = _dest == "locker" ? ShopText.T("dest.locker") : ShopText.T("dest.door");
                var sheet = new DuoSheet(ShopText.T("confirm.title"), ShopText.F("confirm.body", ShopText.Money(total), ShopText.Window(_window), where.ToLowerInvariant()));
                sheet.name = "dshop-confirm";
                sheet.AddContent(El.Div("dshop-confirm-amount", El.Text(ShopText.Money(total), "dshop-confirm-value"), El.Text(ShopText.T("cash"), "dshop-confirm-caption")));
                sheet.AddPrimary(ShopText.F("pay", ShopText.Money(total)), () => { sheet.Close(); Pay(); }, name: "dshop-pay");
                sheet.AddSecondary(CityLifeStrings.T("common.cancel"));
                _confirmSheet = sheet;
                ShowSheet(sheet);
            }

            private DuoSheet _confirmSheet;

            private string _payKey;

            private void Pay()
            {
                if (_busy) return;
                _busy = true; _error = null;
                _payKey ??= Guid.NewGuid().ToString("N");
                var cart = _svc.Cart; string window = _window, dest = _dest, key = _payKey;
                Render();
                Run(ct => _svc.Api.Order(cart, window, dest, key, ct), order =>
                {
                    _busy = false; _payKey = null;
                    cart.Clear();
                    _svc.Upsert(order);
                    _svc.RefreshHome(force: true);
                    Bus.Publish(ToastSignal.Text(ShopText.T("placed"), ToastKind.Good, ShopText.T("placed.body")));
                    _back.Clear();
                    _back.Push((Screen.Catalogue, null));
                    _screen = Screen.Order; _arg = order.OrderId;
                    Render();
                }, err =>
                {
                    _busy = false; _error = err;
                    if (!err.OutcomeUnknown) _payKey = null;   // retry with the same key only when the outcome is unknown
                    Render();
                }, write: true);
            }

            // ------------------------------------------------------------------ tracking

            private void OrdersList()
            {
                SetTitle(ShopText.T("orders"));
                if (_svc.Orders.Count == 0) { Body.Add(Duo.Empty(ShopText.T("orders.empty"))); return; }
                var list = El.Div("dshop-list");
                foreach (var o in _svc.Orders) list.Add(OrderRow(o));
                Duo.MarkLast(list);
                Body.Add(list);
            }

            private void Order()
            {
                var o = _svc.Orders.Find(x => x.OrderId == _arg);
                if (o == null) { SetTitle(ShopText.T("orders")); Body.Add(Duo.Footnote(CityLifeStrings.T("common.loading"))); _ = _svc.RefreshOrders(); return; }
                SetTitle(ShopText.Status(o.Status));
                var hero = El.Div("dshop-track-hero");
                _etaLabel = El.Text(o.Status == OrderStatus.Delivered && o.DeliveredAtMs.HasValue ? ShopText.F("delivered.at", ShopText.Clock(o.DeliveredAtMs.Value))
                    : o.Active && o.EtaAtMs.HasValue ? ShopText.Until(o.EtaAtMs.Value, Hub.NowMs) : ShopText.Status(o.Status), "dshop-track-eta");
                hero.Add(_etaLabel);
                hero.Add(El.Text((o.Destination == "locker" ? ShopText.T("dest.locker") : ShopText.T("dest.door")) + " · " + ShopText.Window(o.Window), "dshop-track-where"));
                Body.Add(hero);
                // the tracker: three steps on a line
                var steps = El.Div("dshop-steps");
                string[] names = { ShopText.T("st.placed"), ShopText.T("st.out"), ShopText.T("st.delivered") };
                for (int i = 0; i < 3; i++)
                {
                    bool done = o.Step >= i, now = o.Step == i;
                    var step = El.Div("dshop-step-col");
                    var dot = El.Div("dshop-step-dot" + (done ? " dshop-step-done" : "") + (now ? " dshop-step-now" : ""));
                    if (done) dot.Add(new DuoGlyph(DuoGlyph.Kind.Check).With("dshop-step-check"));
                    step.Add(dot);
                    step.Add(El.Text(names[i], "dshop-step-name" + (done ? " dshop-step-name-on" : "")));
                    steps.Add(step);
                    if (i < 2) steps.Add(El.Div("dshop-step-line" + (o.Step > i ? " dshop-step-line-on" : "")));
                }
                if (o.Status == OrderStatus.Cancelled) steps.AddToClassList("dshop-steps-cancelled");
                Body.Add(steps);
                Body.Add(Duo.SectionHeader(ShopText.T("items")));
                var list = El.Div("dshop-list");
                foreach (var l in o.Lines)
                {
                    var it = _svc.Catalog?.Find(l.ItemId);
                    var row = new DuoRow().Leading(Thumb(it?.ModelKey, "dshop-row-art")).Title(l.Name, "duo-row-title-strong")
                        .Subtitle("×" + l.Qty).Value(ShopText.Money(l.UnitPriceCents * l.Qty));
                    list.Add(row);
                }
                Duo.MarkLast(list);
                Body.Add(list);
                var totals = El.Div("dshop-facts");
                if (o.FeeCents > 0) totals.Add(CityUi.Pair(ShopText.T("fee"), ShopText.Money(o.FeeCents)));
                totals.Add(CityUi.Pair(ShopText.T("total"), ShopText.Money(o.TotalCents)));
                Body.Add(totals);
                if (_error != null && _screen == Screen.Order) Body.Add(Duo.Notice(CityLifeStrings.T(_error.Key), Duo.Tone.Danger));
                if (o.Cancellable)
                {
                    var cancel = CityUi.Btn(ShopText.T("cancel"), () => CancelSheet(o), CityUi.Danger);
                    cancel.name = "dshop-cancel";
                    Body.Add(El.Div("dshop-cancel-row", cancel));
                }
                Body.Add(El.Div("dshop-bottom-space"));
            }

            private void CancelSheet(ShopOrder o)
            {
                var sheet = new DuoSheet(ShopText.T("cancel.title"), ShopText.F("cancel.body", ShopText.Money(o.TotalCents)));
                sheet.AddPrimary(ShopText.T("cancel"), () =>
                {
                    sheet.Close();
                    var key = Guid.NewGuid().ToString("N");
                    Run(ct => _svc.Api.Cancel(o.OrderId, key, ct), r =>
                    {
                        _svc.Upsert(r);
                        _svc.RefreshHome(force: true);
                        Bus.Publish(ToastSignal.Text(ShopText.T("cancelled"), ToastKind.Good));
                        Render();
                    }, err => { _error = err; Render(); }, write: true);
                }, destructive: true, name: "dshop-cancel-confirm");
                sheet.AddSecondary(ShopText.T("cancel.keep"));
                ShowSheet(sheet);
            }
        }
    }
}
