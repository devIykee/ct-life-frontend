using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife
{
    /// <summary>A placeable item's bounding box in metres (w along the model's x, d along its z, h up).</summary>
    public struct Footprint
    {
        public float W, D, H;
        public bool Valid => W > 0f && D > 0f;

        public static Footprint? Parse(JToken t)
        {
            if (!(t is JObject o)) return null;
            var f = new Footprint { W = (float)CityWire.DoubleOr(o["w"], 0), D = (float)CityWire.DoubleOr(o["d"], 0), H = (float)CityWire.DoubleOr(o["h"], 0) };
            return f.Valid ? f : (Footprint?)null;
        }
    }

    /// <summary>One Duo Shop catalogue row (`GET /shop/catalog`, FE-owned contract/ids/shop_items.json).</summary>
    public sealed class ShopItem
    {
        public string ItemId, Category, Name, ModelKey;
        public long PriceCents;
        public bool Placeable;
        public Footprint? Footprint;

        public static ShopItem Parse(JToken t)
        {
            if (!(t is JObject o)) return null;
            var id = CityWire.Str(o["itemId"]);
            if (!CityWire.IsSafeId(id)) return null;
            var price = CityWire.LongOr(o["priceCents"], -1);
            if (price <= 0) return null;
            return new ShopItem
            {
                ItemId = id, Category = CityWire.Str(o["category"]) ?? "furniture", Name = CityWire.Plain(o["name"], 60) ?? id,
                PriceCents = price, Placeable = o["placeable"]?.Type == JTokenType.Boolean && (bool)o["placeable"],
                Footprint = global::CTLife.CityLife.Footprint.Parse(o["footprint"]), ModelKey = SafeKey(CityWire.Str(o["modelKey"])),
            };
        }

        /// <summary>A modelKey usable as a Resources path segment (letters, digits, underscore), else null.</summary>
        public static string SafeKey(string key) =>
            !string.IsNullOrEmpty(key) && key.Length <= 96 && key.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_') ? key : null;
    }

    public sealed class ShopCatalog
    {
        public readonly List<ShopItem> Items = new List<ShopItem>();
        public long LockerFeeCents = 300;
        public readonly List<(string id, int minutes)> Windows = new List<(string, int)>();

        public static readonly string[] Categories = { "furniture", "decor", "appliance", "food", "clothing" };

        public static ShopCatalog Parse(JToken t)
        {
            var c = new ShopCatalog();
            foreach (var row in CityWire.List(t, "items")) { var it = ShopItem.Parse(row); if (it != null) c.Items.Add(it); }
            c.LockerFeeCents = CityWire.LongOr(t?["lockerFeeCents"], 300);
            if (t?["windows"] is JArray ws)
                foreach (var w in ws)
                {
                    var id = CityWire.Str(w?["id"]);
                    if (id == "asap" || id == "30m" || id == "1h") c.Windows.Add((id, CityWire.IntOr(w["minutes"], 0)));
                }
            if (c.Windows.Count == 0) { c.Windows.Add(("asap", 10)); c.Windows.Add(("30m", 30)); c.Windows.Add(("1h", 60)); }
            return c;
        }

        public ShopItem Find(string itemId) => Items.FirstOrDefault(i => i.ItemId == itemId);
        public IEnumerable<string> PresentCategories => Categories.Where(cat => Items.Any(i => i.Category == cat));
    }

    public enum OrderStatus { Placed, OutForDelivery, Delivered, Cancelled, Unknown }

    public sealed class ShopOrderLine { public string ItemId, Name; public int Qty; public long UnitPriceCents; }

    /// <summary>`ShopOrder` (POST/GET /shop/orders).</summary>
    public sealed class ShopOrder
    {
        public string OrderId, Window, Destination, UnitId;
        public OrderStatus Status;
        public readonly List<ShopOrderLine> Lines = new List<ShopOrderLine>();
        public long ItemsCents, FeeCents, TotalCents;
        public long? PlacedAtMs, EtaAtMs, DeliveredAtMs, CancelledAtMs;

        public static OrderStatus ParseStatus(string s) => s switch
        {
            "placed" => OrderStatus.Placed, "out_for_delivery" => OrderStatus.OutForDelivery, "delivered" => OrderStatus.Delivered,
            "cancelled" => OrderStatus.Cancelled, _ => OrderStatus.Unknown,
        };

        public static ShopOrder Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected an order");
            var id = CityWire.Str(o["orderId"]);
            if (string.IsNullOrEmpty(id) || id.Length > 64) throw new FormatException("Missing orderId");
            var r = new ShopOrder
            {
                OrderId = id, Status = ParseStatus(CityWire.Str(o["status"])), Window = CityWire.Str(o["window"]),
                Destination = CityWire.Str(o["destination"]), UnitId = CityWire.Str(o["unitId"]),
                ItemsCents = CityWire.LongOr(o["itemsCents"], 0), FeeCents = CityWire.LongOr(o["feeCents"], 0), TotalCents = CityWire.LongOr(o["totalCents"], 0),
                PlacedAtMs = CityWire.TimeMsOrNull(o["placedAt"]), EtaAtMs = CityWire.TimeMsOrNull(o["etaAt"]),
                DeliveredAtMs = CityWire.TimeMsOrNull(o["deliveredAt"]), CancelledAtMs = CityWire.TimeMsOrNull(o["cancelledAt"]),
            };
            if (o["items"] is JArray lines)
                foreach (var l in lines)
                    r.Lines.Add(new ShopOrderLine
                    {
                        ItemId = CityWire.Str(l?["itemId"]), Name = CityWire.Plain(l?["name"], 60), Qty = CityWire.IntOr(l?["qty"], 1),
                        UnitPriceCents = CityWire.LongOr(l?["unitPriceCents"], 0),
                    });
            return r;
        }

        public static List<ShopOrder> ParseList(JToken t)
        {
            var list = new List<ShopOrder>();
            foreach (var row in CityWire.List(t, "items", "orders")) { try { list.Add(Parse(row)); } catch (FormatException) { } }
            return list;
        }

        public bool Cancellable => Status == OrderStatus.Placed;
        public bool Active => Status == OrderStatus.Placed || Status == OrderStatus.OutForDelivery;
        public int Units => Lines.Sum(l => Math.Max(1, l.Qty));
        public string Summary => Lines.Count == 0 ? "" : Lines.Count == 1 ? Lines[0].Name + (Lines[0].Qty > 1 ? " ×" + Lines[0].Qty : "")
            : Lines[0].Name + " + " + (Lines.Count - 1) + " more";

        /// <summary>The tracking step 0 placed, 1 out for delivery, 2 delivered (-1 cancelled).</summary>
        public int Step => Status == OrderStatus.Cancelled ? -1 : Status == OrderStatus.Delivered ? 2 : Status == OrderStatus.OutForDelivery ? 1 : 0;
    }

    /// <summary>`GET /me/items` row: one owned unit (off-chain, never tradable).</summary>
    public sealed class OwnedItem
    {
        public string ItemInstanceId, ItemId, Name, Category, ModelKey, OrderId, PlacedHomeId;
        public bool Placeable;
        public Footprint? Footprint;
        public long? AcquiredAtMs;

        public static List<OwnedItem> ParseList(JToken t)
        {
            var list = new List<OwnedItem>();
            foreach (var r in CityWire.List(t, "items"))
            {
                var id = CityWire.Str(r?["itemInstanceId"]);
                if (string.IsNullOrEmpty(id) || id.Length > 64) continue;
                list.Add(new OwnedItem
                {
                    ItemInstanceId = id, ItemId = CityWire.Str(r["itemId"]), Name = CityWire.Plain(r["name"], 60), Category = CityWire.Str(r["category"]),
                    ModelKey = ShopItem.SafeKey(CityWire.Str(r["modelKey"])), Placeable = r["placeable"]?.Type == JTokenType.Boolean && (bool)r["placeable"],
                    Footprint = global::CTLife.CityLife.Footprint.Parse(r["footprint"]), OrderId = CityWire.Str(r["orderId"]), PlacedHomeId = CityWire.Str(r["placedHomeId"]),
                    AcquiredAtMs = CityWire.TimeMsOrNull(r["acquiredAt"]),
                });
            }
            return list;
        }
    }

    /// <summary>One placed item in a home (`GET|PUT /homes/{homeId}/placements`): metres in room space, yaw in radians.</summary>
    public sealed class Placement
    {
        public string ItemInstanceId, ItemId, Name, ModelKey;
        public float X, Z, RotY;
        public Footprint? Footprint;

        public static List<Placement> ParseList(JToken t)
        {
            var list = new List<Placement>();
            foreach (var r in CityWire.List(t, "placements"))
            {
                var id = CityWire.Str(r?["itemInstanceId"]);
                if (string.IsNullOrEmpty(id)) continue;
                list.Add(new Placement
                {
                    ItemInstanceId = id, ItemId = CityWire.Str(r["itemId"]), Name = CityWire.Plain(r["name"], 60), ModelKey = ShopItem.SafeKey(CityWire.Str(r["modelKey"])),
                    X = (float)CityWire.DoubleOr(r["x"], 0), Z = (float)CityWire.DoubleOr(r["z"], 0), RotY = (float)CityWire.DoubleOr(r["rotY"], 0),
                    Footprint = global::CTLife.CityLife.Footprint.Parse(r["footprint"]),
                });
            }
            return list;
        }

        public static JObject Body(IEnumerable<Placement> placements) => new JObject
        {
            ["placements"] = new JArray(placements.Select(p => new JObject
            {
                ["itemInstanceId"] = p.ItemInstanceId,
                ["x"] = Math.Round(p.X, 3), ["z"] = Math.Round(p.Z, 3), ["rotY"] = Math.Round(p.RotY, 4),
            })),
        };
    }

    /// <summary>`GET /me` → `home {homeId, unitId, tier, roomType, status, missedWeeks}` (null on the street).</summary>
    public sealed class MyHome
    {
        public string HomeId, UnitId, Tier, RoomType, Status;
        public int MissedWeeks;

        public static MyHome Parse(JToken me)
        {
            if (!(me?["home"] is JObject h)) return null;
            var id = CityWire.Str(h["homeId"]);
            if (string.IsNullOrEmpty(id)) return null;
            return new MyHome
            {
                HomeId = id, UnitId = CityWire.Str(h["unitId"]), Tier = CityWire.Str(h["tier"]), RoomType = CityWire.Str(h["roomType"]),
                Status = CityWire.Str(h["status"]) ?? "active", MissedWeeks = CityWire.IntOr(h["missedWeeks"], 0),
            };
        }

        public bool HasDoor => !string.IsNullOrEmpty(UnitId) && Status != "evicted" && Status != "moved_out";
        public bool Overdue => Status == "overdue";
    }

    /// <summary>`GET /units/{unitId}/access` → `{canEnter, reason}`; `homeId` is read when the server adds it (proposal).</summary>
    public sealed class UnitAccess
    {
        public bool CanEnter;
        public string Reason, HomeId;

        public static UnitAccess Parse(JToken t) => new UnitAccess
        {
            CanEnter = t?["canEnter"]?.Type == JTokenType.Boolean && (bool)t["canEnter"],
            Reason = CityWire.Str(t?["reason"]) ?? "locked",
            HomeId = CityWire.Str(t?["homeId"]),
        };
    }

    /// <summary>The bag in the Duo Shop: lines, totals and the server's limits (10 lines, 1..10 of each, 20 units).</summary>
    public sealed class ShopCart
    {
        public const int MaxLines = 10, MaxEach = 10, MaxUnits = 20;
        private readonly List<(string itemId, int qty)> _lines = new List<(string, int)>();
        public IReadOnlyList<(string itemId, int qty)> Lines => _lines;
        public event Action Changed;

        public int Units => _lines.Sum(l => l.qty);
        public int Count(string itemId) => _lines.FirstOrDefault(l => l.itemId == itemId).qty;
        public bool IsEmpty => _lines.Count == 0;

        /// <summary>Adds one; false (no change) at a limit.</summary>
        public bool Add(string itemId)
        {
            if (!CityWire.IsSafeId(itemId) || Units >= MaxUnits) return false;
            int i = _lines.FindIndex(l => l.itemId == itemId);
            if (i < 0) { if (_lines.Count >= MaxLines) return false; _lines.Add((itemId, 1)); }
            else { if (_lines[i].qty >= MaxEach) return false; _lines[i] = (itemId, _lines[i].qty + 1); }
            Changed?.Invoke();
            return true;
        }

        public void Set(string itemId, int qty)
        {
            int i = _lines.FindIndex(l => l.itemId == itemId);
            if (i < 0) return;
            qty = Math.Max(0, Math.Min(MaxEach, qty));
            int others = Units - _lines[i].qty;
            qty = Math.Min(qty, MaxUnits - others);
            if (qty <= 0) _lines.RemoveAt(i); else _lines[i] = (itemId, qty);
            Changed?.Invoke();
        }

        public void Clear() { _lines.Clear(); Changed?.Invoke(); }

        public long ItemsCents(ShopCatalog c) => _lines.Sum(l => (c?.Find(l.itemId)?.PriceCents ?? 0) * l.qty);
        public long TotalCents(ShopCatalog c, string destination) => ItemsCents(c) + (destination == "locker" ? (c?.LockerFeeCents ?? 300) : 0);

        public JObject Body(string window, string destination) => new JObject
        {
            ["items"] = new JArray(_lines.Select(l => new JObject { ["itemId"] = l.itemId, ["qty"] = l.qty })),
            ["window"] = window, ["destination"] = destination,
        };
    }

    /// <summary>Duo Shop and home copy (English, original).</summary>
    public static class ShopText
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["app"] = "Duo Shop",
            ["today"] = "Delivered to your door",
            ["cat.all"] = "All", ["cat.furniture"] = "Furniture", ["cat.decor"] = "Decor", ["cat.appliance"] = "Appliances", ["cat.food"] = "Food", ["cat.clothing"] = "Clothing",
            ["featured"] = "FEATURED", ["cat.everything"] = "Everything", ["seeAll"] = "See All",
            ["bag"] = "Bag", ["bag.empty"] = "Your bag is empty", ["bag.emptyDetail"] = "Add something for the flat.",
            ["add"] = "Add to Bag", ["added"] = "In Bag", ["get"] = "Add",
            ["window"] = "Delivery window", ["w.asap"] = "ASAP", ["w.30m"] = "30 min", ["w.1h"] = "1 hour",
            ["w.asap.note"] = "About {0} minutes.", ["w.later.note"] = "Arrives in {0} minutes.",
            ["dest"] = "Deliver to", ["dest.door"] = "My door", ["dest.locker"] = "Parcel locker",
            ["dest.door.note"] = "Left on the doorstep of your flat in the Residences.",
            ["dest.locker.note"] = "Collect it from the Residences parcel locker. Locker fee {0}.",
            ["dest.noHome"] = "You don't have a flat with a door yet, so orders go to the Residences parcel locker (fee {0}).",
            ["items"] = "Items", ["fee"] = "Locker fee", ["total"] = "Total", ["cash"] = "Cash", ["balance"] = "Cash balance",
            ["checkout"] = "Check Out", ["pay"] = "Pay {0}", ["paying"] = "Paying…",
            ["confirm.title"] = "Confirm order", ["confirm.body"] = "{0} will be taken from your Cash. Delivery {1} to {2}.",
            ["placed"] = "Order placed", ["placed.body"] = "We'll let you know when the courier leaves.",
            ["orders"] = "Orders", ["orders.empty"] = "No orders yet",
            ["st.placed"] = "Placed", ["st.out"] = "Out for delivery", ["st.delivered"] = "Delivered", ["st.cancelled"] = "Cancelled",
            ["eta"] = "Arrives {0}", ["eta.in"] = "Arrives in {0}", ["eta.now"] = "Arriving now", ["delivered.at"] = "Delivered {0}",
            ["cancel"] = "Cancel Order", ["cancel.title"] = "Cancel this order?", ["cancel.body"] = "You get {0} back in Cash.", ["cancel.keep"] = "Keep Order",
            ["cancelled"] = "Order cancelled. Cash refunded.",
            ["size"] = "Size", ["size.value"] = "{0} × {1} × {2} m", ["category"] = "Category", ["placeable"] = "Fits in your flat",
            ["placeable.yes"] = "Place it in your flat", ["placeable.no"] = "Not placeable",
            ["drag"] = "Drag to turn",
            ["notify.out"] = "Your courier is on the way", ["notify.out.body"] = "{0} · arriving {1}",
            ["notify.arrived.door"] = "Delivered to your door", ["notify.arrived.locker"] = "Ready in the parcel locker",
            ["notify.arrived.body"] = "{0}. Pick it up when you're home.",
            ["pickup.done"] = "Picked up. It's in your flat's storage.", ["pickup.none"] = "Nothing new to pick up.",
            ["knock"] = "Knocking isn't available yet.",
            ["shownOut"] = "You were shown out",
            ["home.unit"] = "Your flat", ["home.door"] = "Door", ["home.status"] = "Status",
            ["home.active"] = "Rent paid", ["home.overdue"] = "Rent overdue", ["home.evicted"] = "Evicted", ["home.movedOut"] = "Moved out",
            ["home.missed"] = "{0} missed week(s). Four in a row means eviction.",
            ["home.noDoor"] = "No flat with a door yet. Leasing a Hacker House or Studio gives you one of the Residences flats.",
            ["home.decorate"] = "Decorate", ["home.decorate.note"] = "Walk into your flat, then tap Decorate.",
            ["place.title"] = "Decorate", ["place.pick"] = "Choose something to place", ["place.none"] = "Nothing to place yet. Order furniture in the Duo Shop.",
            ["place.rotate"] = "Rotate", ["place.done"] = "Place", ["place.cancel"] = "Cancel", ["place.remove"] = "Put away",
            ["place.saved"] = "Saved", ["place.blocked"] = "Doesn't fit there", ["place.saving"] = "Saving…", ["place.hint"] = "Drag to move · two fingers to turn",
            ["owned"] = "In storage", ["placedHere"] = "Placed",
        };

        public static string T(string key) => En.TryGetValue(key, out var v) ? v : CityLifeStrings.T(key);
        public static string F(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, T(key), args);
        public static string Category(string cat) => En.TryGetValue("cat." + cat, out var v) ? v : CityLifeStrings.Readable(cat);
        public static string Window(string w) => En.TryGetValue("w." + w, out var v) ? v : w;

        public static string Status(OrderStatus s) => s switch
        {
            OrderStatus.Placed => T("st.placed"), OrderStatus.OutForDelivery => T("st.out"), OrderStatus.Delivered => T("st.delivered"),
            OrderStatus.Cancelled => T("st.cancelled"), _ => "",
        };

        /// <summary>"$249" / "$1,249.50": Cash in dollars, cents only when there are any.</summary>
        public static string Money(long cents)
        {
            var d = cents / 100m;
            return "$" + (cents % 100 == 0 ? d.ToString("#,0", CultureInfo.InvariantCulture) : d.ToString("#,0.00", CultureInfo.InvariantCulture));
        }

        /// <summary>"4 min" / "45 s" until <paramref name="atMs"/>.</summary>
        public static string Until(long atMs, double nowMs)
        {
            double s = (atMs - nowMs) / 1000.0;
            if (s <= 15) return T("eta.now");
            if (s < 90) return F("eta.in", Math.Ceiling(s / 5) * 5 + " s");
            return F("eta.in", Math.Ceiling(s / 60) + " min");
        }

        public static string Clock(long atMs) => DateTimeOffset.FromUnixTimeMilliseconds(atMs).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
    }
}
