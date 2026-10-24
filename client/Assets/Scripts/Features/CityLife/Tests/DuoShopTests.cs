using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.CityLife.Tests
{
    /// <summary>Duo Shop, delivery and placement logic (backend PRs #7/#8 shapes as served on 2026-10-23).</summary>
    public sealed class DuoShopTests
    {
        private const string Catalog = @"{""items"":[
            {""itemId"":""sofa_linen"",""category"":""furniture"",""name"":""Linen Loveseat"",""priceCents"":29900,""deliverable"":true,""placeable"":true,""footprint"":{""w"":1.57,""d"":0.66,""h"":0.8},""modelKey"":""fu_sofa_linen""},
            {""itemId"":""pantry_restock"",""category"":""food"",""name"":""Pantry Restock"",""priceCents"":1800,""deliverable"":true,""placeable"":false,""modelKey"":""fu_pantry""},
            {""itemId"":""bad id"",""category"":""furniture"",""name"":""x"",""priceCents"":100,""placeable"":false,""modelKey"":""k""},
            {""itemId"":""free"",""category"":""decor"",""name"":""x"",""priceCents"":0,""placeable"":false,""modelKey"":""k""}],
            ""lockerFeeCents"":300,""windows"":[{""id"":""asap"",""minutes"":10},{""id"":""30m"",""minutes"":30},{""id"":""1h"",""minutes"":60}]}";

        [Test]
        public void CatalogueParsesValidRowsOnly()
        {
            var c = ShopCatalog.Parse(JToken.Parse(Catalog));
            Assert.AreEqual(2, c.Items.Count);
            Assert.AreEqual(300, c.LockerFeeCents);
            Assert.AreEqual(3, c.Windows.Count);
            var sofa = c.Find("sofa_linen");
            Assert.IsTrue(sofa.Placeable);
            Assert.AreEqual(1.57f, sofa.Footprint.Value.W, 1e-4);
            Assert.IsNull(c.Find("pantry_restock").Footprint);
            CollectionAssert.AreEqual(new[] { "furniture", "food" }, new List<string>(c.PresentCategories));
        }

        [Test]
        public void ModelKeysAreResourcePathSafe()
        {
            Assert.AreEqual("fu_sofa_linen", ShopItem.SafeKey("fu_sofa_linen"));
            Assert.IsNull(ShopItem.SafeKey("../secret"));
            Assert.IsNull(ShopItem.SafeKey("a/b"));
        }

        [Test]
        public void CartKeepsServerLimitsAndTotals()
        {
            var c = ShopCatalog.Parse(JToken.Parse(Catalog));
            var cart = new ShopCart();
            for (int i = 0; i < 12; i++) cart.Add("sofa_linen");
            Assert.AreEqual(ShopCart.MaxEach, cart.Count("sofa_linen"));
            for (int i = 0; i < 12; i++) cart.Add("pantry_restock");
            Assert.AreEqual(ShopCart.MaxUnits, cart.Units);
            cart.Set("sofa_linen", 1);
            Assert.AreEqual(29900 + 10 * 1800, cart.ItemsCents(c));
            Assert.AreEqual(29900 + 10 * 1800 + 300, cart.TotalCents(c, "locker"));
            Assert.AreEqual(29900 + 10 * 1800, cart.TotalCents(c, "door"));
            var body = cart.Body("30m", "door");
            Assert.AreEqual("30m", (string)body["window"]);
            Assert.AreEqual(2, ((JArray)body["items"]).Count);
            cart.Set("sofa_linen", 0);
            Assert.AreEqual(0, cart.Count("sofa_linen"));
        }

        [Test]
        public void OrderParsesTheServerShape()
        {
            var o = ShopOrder.Parse(JToken.Parse(@"{""orderId"":""01a1682d-c7a5"",""status"":""out_for_delivery"",""window"":""asap"",""destination"":""door"",""unitId"":""flat:lgmf_n400_p168:0"",
                ""items"":[{""itemId"":""lamp_pipe"",""name"":""Pipe Table Lamp"",""qty"":2,""unitPriceCents"":4900}],""itemsCents"":9800,""feeCents"":0,""totalCents"":9800,
                ""placedAt"":""2026-10-23T09:53:13.125Z"",""etaAt"":""2026-10-23T10:03:13.125Z"",""deliveredAt"":null,""cancelledAt"":null}"));
            Assert.AreEqual(OrderStatus.OutForDelivery, o.Status);
            Assert.AreEqual(1, o.Step);
            Assert.IsFalse(o.Cancellable);
            Assert.IsTrue(o.Active);
            Assert.AreEqual(2, o.Units);
            Assert.AreEqual("Pipe Table Lamp ×2", o.Summary);
            Assert.AreEqual(600000, o.EtaAtMs - o.PlacedAtMs);
        }

        [Test]
        public void HomeAndAccessParse()
        {
            var h = MyHome.Parse(JToken.Parse(@"{""home"":{""homeId"":""home:abc"",""unitId"":""flat:lgmf_n400_p168:0"",""tier"":""flat"",""roomType"":""apartment_modern"",""status"":""overdue"",""missedWeeks"":2}}"));
            Assert.IsTrue(h.HasDoor);
            Assert.IsTrue(h.Overdue);
            Assert.IsNull(MyHome.Parse(JToken.Parse(@"{""home"":null}")));
            Assert.IsFalse(MyHome.Parse(JToken.Parse(@"{""home"":{""homeId"":""h"",""unitId"":""u"",""status"":""evicted""}}")).HasDoor);
            var a = UnitAccess.Parse(JToken.Parse(@"{""canEnter"":false,""reason"":""locked""}"));
            Assert.IsFalse(a.CanEnter);
            Assert.AreEqual("locked", a.Reason);
            Assert.IsNull(a.HomeId);
        }

        [Test]
        public void DeliveryEventsFromRoomPayloads()
        {
            var e = DeliveryEvent.From("delivery_out", new Dictionary<string, object> { ["orderId"] = "o1", ["destination"] = "locker", ["unitId"] = null, ["etaAt"] = 1792749000000.0 });
            Assert.IsFalse(e.Arrived);
            Assert.IsTrue(e.ToLocker);
            Assert.AreEqual(1792749000000L, e.EtaAtMs);
            var d = DeliveryEvent.From("delivery_arrived", new Dictionary<string, object> { ["orderId"] = "o2", ["destination"] = "door", ["unitId"] = "flat:x:0" });
            Assert.IsTrue(d.Arrived);
            Assert.IsFalse(d.ToLocker);
            Assert.IsNull(DeliveryEvent.From("delivery_out", new Dictionary<string, object> { ["destination"] = "door" }));
        }

        [Test]
        public void FootprintsFitTheRoomAndEachOther()
        {
            Vector2[] Box(float x, float z, float w, float d) => new[] { new Vector2(x - w / 2, z - d / 2), new Vector2(x + w / 2, z - d / 2), new Vector2(x + w / 2, z + d / 2), new Vector2(x - w / 2, z + d / 2) };
            var min = new Vector3(-5f, 0f, -10f); var max = new Vector3(5f, 3f, 0f);
            Assert.IsTrue(PlacementFit.Fits(Box(2f, -5f, 1.6f, 0.7f), min, max, new List<Vector2[]>()));
            Assert.IsFalse(PlacementFit.Fits(Box(4.5f, -5f, 1.6f, 0.7f), min, max, new List<Vector2[]>()), "through the side wall");
            Assert.IsFalse(PlacementFit.Fits(Box(0f, -0.8f, 1f, 1f), min, max, new List<Vector2[]>()), "blocks the entrance");
            Assert.IsFalse(PlacementFit.Fits(Box(2f, -5f, 1.6f, 0.7f), min, max, new List<Vector2[]> { Box(2.5f, -5.2f, 1f, 1f) }), "overlaps a piece");
            Assert.IsTrue(PlacementFit.Fits(Box(2f, -5f, 1.6f, 0.7f), min, max, new List<Vector2[]> { Box(2f, -7f, 1f, 1f) }));
        }

        [Test]
        public void MoneyAndPlacementBody()
        {
            Assert.AreEqual("$299", ShopText.Money(29900));
            Assert.AreEqual("$1,249.50", ShopText.Money(124950));
            var b = Placement.Body(new[] { new Placement { ItemInstanceId = "i1", X = 1.23456f, Z = -2f, RotY = 1.5708f } });
            Assert.AreEqual(1.235, (double)b["placements"][0]["x"], 1e-9);
            Assert.AreEqual("i1", (string)b["placements"][0]["itemInstanceId"]);
        }
    }
}
