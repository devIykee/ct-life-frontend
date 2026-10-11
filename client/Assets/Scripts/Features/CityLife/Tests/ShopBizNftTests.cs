using System;
using System.Collections.Generic;
using System.Threading;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CTLife.CityLife.Tests
{
    public class ShopTests
    {
        [Test]
        public void ShopContractAndBackendShapes()
        {
            var c = ShopRow.ParseList(JArray.Parse("[{\"id\":\"top_suit_black\",\"slot\":\"top\",\"name\":\"Black suit\",\"priceCents\":50000},{\"id\":\"x\",\"slot\":\"wings\",\"name\":\"?\",\"priceCents\":1}]"));
            Assert.AreEqual(1, c.Count, "unknown slots are skipped");
            Assert.AreEqual("top_suit_black", c[0].PurchaseId);
            Assert.AreEqual("top_suit_black", c[0].LookId);
            var b = ShopRow.ParseList(JObject.Parse("{\"items\":[{\"itemId\":\"0192a1c2-0000-7000-8000-000000000001\",\"slot\":\"acc\",\"itemKey\":\"acc_hard_hat\",\"name\":\"Hard hat\",\"priceCents\":\"0\",\"onchainOnly\":true}]}"));
            Assert.AreEqual("0192a1c2-0000-7000-8000-000000000001", b[0].PurchaseId, "purchase by the backend's uuid");
            Assert.AreEqual("acc_hard_hat", b[0].LookId, "wear by the item key");
            Assert.IsTrue(b[0].OnchainOnly);
            Assert.AreEqual(0, b[0].PriceCents);
        }

        [Test]
        public void InventoryShapes()
        {
            var c = InventoryView.Parse(JObject.Parse("{\"items\":[{\"itemId\":\"top_suit_black\",\"slot\":\"top\",\"acquiredAt\":\"2026-10-08T00:00:00Z\"}]}"));
            Assert.IsTrue(c.Owns("top_suit_black"));
            var b = InventoryView.Parse(JObject.Parse("{\"items\":[{\"inventoryId\":\"i\",\"itemId\":\"0192a1c2-0000\",\"slot\":\"shoes\",\"itemKey\":\"shoes_boots_black\",\"name\":\"Boots\",\"source\":\"shop\",\"acquiredAt\":\"2026-10-08T00:00:00Z\"}]}"));
            Assert.IsTrue(b.Owns("shoes_boots_black"));
            Assert.AreEqual("Boots", b.Names["shoes_boots_black"]);
            var single = InventoryView.Parse(JObject.Parse("{\"inventoryId\":\"i\",\"itemId\":\"u\",\"slot\":\"hair\",\"itemKey\":\"hair_villa_blonde\",\"acquiredAt\":\"2026-10-08T00:00:00Z\"}"));
            Assert.IsTrue(single.Owns("hair_villa_blonde"), "backend purchase reply is one row");
            c.Merge(single);
            CollectionAssert.AreEquivalent(new[] { "top_suit_black", "hair_villa_blonde" }, new List<string>(c.SlotByLookId.Keys));
        }

        private static readonly string[] Worn = { "tan", "head_skull_visor", "hair_mohawk_pink", "top_hoodie_black", "bottom_jeans_indigo", "shoes_sneakers_white", "acc_aviators" };

        private static HashSet<string> Free() => LookDraft.FreeItems(JObject.Parse(
            "{\"items\":[{\"id\":\"top_hoodie_white\",\"slot\":\"top\",\"cashCents\":0},{\"id\":\"top_suit_black\",\"slot\":\"top\",\"cashCents\":50000},{\"id\":\"bottom_cargo_olive\",\"slot\":\"bottom\",\"starter\":true,\"cashCents\":900}]}"));

        [Test]
        public void FreeItemsFromRegistry()
        {
            var f = Free();
            Assert.IsTrue(f.Contains("top_hoodie_white"));
            Assert.IsTrue(f.Contains("bottom_cargo_olive"), "explicit starter wins over price");
            Assert.IsFalse(f.Contains("top_suit_black"));
        }

        [Test]
        public void DraftOnlyWearsOwnedFreeOrWorn()
        {
            var inv = InventoryView.Parse(JObject.Parse("{\"items\":[{\"itemId\":\"top_suit_black\",\"slot\":\"top\",\"acquiredAt\":\"x\"}]}"));
            var d = new LookDraft(Worn);
            Assert.IsFalse(d.IsDirty);
            Assert.IsTrue(d.Set("top", "top_suit_black", inv, Free()));
            Assert.IsTrue(d.IsDirty);
            Assert.AreEqual("top_suit_black", d["top"]);
            Assert.IsFalse(d.Set("top", "top_gold_chain", inv, Free()), "not owned");
            Assert.IsFalse(d.Set("bottom", "top_suit_black", inv, Free()), "owned item in the wrong slot");
            Assert.IsFalse(d.Set("skin", "tan", inv, Free()), "skin tone is not an item");
            Assert.IsTrue(d.Set("top", "top_hoodie_white", inv, Free()), "free starter");
            Assert.IsTrue(d.Set("top", "top_hoodie_black", inv, Free()), "back to worn");
            Assert.IsFalse(d.IsDirty);
        }

        [Test]
        public void EquipFlowSaveUnknownRetry()
        {
            var inv = InventoryView.Parse(JObject.Parse("{\"items\":[{\"itemId\":\"top_suit_black\",\"slot\":\"top\",\"acquiredAt\":\"x\"}]}"));
            var f = new EquipFlow(new LookDraft(Worn), Keys.Seq());
            Assert.IsNull(f.BeginSave(), "nothing to save");
            Assert.IsTrue(f.Wear("top", "top_suit_black", inv, Free()));
            Assert.AreEqual(SaveStage.Dirty, f.Stage);
            var req = f.BeginSave().Value;
            Assert.AreEqual("k1", req.key);
            var body = JObject.Parse(req.body);
            Assert.AreEqual("top_suit_black", (string)body["topId"]);
            Assert.AreEqual("tan", (string)body["skinTone"]);
            Assert.IsFalse(f.Wear("top", "top_hoodie_white", inv, Free()), "busy");
            f.SaveFailed(Keys.Network());
            Assert.AreEqual(SaveStage.Unknown, f.Stage);
            Assert.IsFalse(f.Wear("top", "top_hoodie_white", inv, Free()), "settle the unknown save first");
            Assert.AreEqual("k1", f.BeginSave().Value.key);
            f.Saved(null);
            Assert.AreEqual(SaveStage.Saved, f.Stage);
            Assert.IsFalse(f.Draft.IsDirty);
            Assert.AreEqual("top_suit_black", f.Draft.Worn[3]);
        }

        [Test]
        public void EquipRejectedThenRevert()
        {
            var inv = InventoryView.Parse(JObject.Parse("{\"items\":[{\"itemId\":\"top_suit_black\",\"slot\":\"top\",\"acquiredAt\":\"x\"}]}"));
            var f = new EquipFlow(new LookDraft(Worn), Keys.Seq());
            f.Wear("top", "top_suit_black", inv, Free());
            f.BeginSave();
            f.SaveFailed(Keys.Api(400, "VALIDATION_FAILED"));
            Assert.AreEqual(SaveStage.Rejected, f.Stage);
            Assert.IsFalse(f.KeyRetained);
            f.Revert();
            Assert.AreEqual(SaveStage.Clean, f.Stage);
            Assert.AreEqual("top_hoodie_black", f.Draft["top"]);
        }

        [Test]
        public void ServerEchoWins()
        {
            var echo = LookDraft.LookFrom(JObject.Parse("{\"alias\":\"anon\",\"look\":{\"skinTone\":\"tan\",\"headId\":\"h\",\"hairId\":\"r\",\"topId\":\"t\",\"bottomId\":\"b\",\"shoesId\":\"s\",\"accId\":\"a\"}}"));
            CollectionAssert.AreEqual(new[] { "tan", "h", "r", "t", "b", "s", "a" }, echo);
            Assert.IsNull(LookDraft.LookFrom(JObject.Parse("{}")));
            var d = new LookDraft(Worn);
            d.Commit(echo);
            CollectionAssert.AreEqual(echo, d.Worn);
        }

        [Test]
        public void SeasonPassStates()
        {
            Assert.AreEqual(PassState.Active, SeasonPassView.Parse(JObject.Parse("{\"tier\":\"season\",\"expiresAt\":\"2027-01-06T00:00:00Z\"}"), 0).State);
            Assert.AreEqual(PassState.Expired, SeasonPassView.Parse(JObject.Parse("{\"tier\":\"season\",\"expiresAt\":\"1970-01-01T00:00:01Z\"}"), 5000).State);
            Assert.AreEqual(PassState.None, SeasonPassView.Parse(JObject.Parse("{\"tier\":\"free\"}"), 0).State);
            Assert.AreEqual(PassState.None, SeasonPassView.From(Keys.Api(404, "NOT_FOUND", write: false)).State);
            Assert.AreEqual(PassState.Unavailable, SeasonPassView.From(Keys.Api(404, "HTTP_404", write: false)).State);
            Assert.AreEqual(PassState.Unknown, SeasonPassView.From(Keys.Network()).State);
        }
    }

    public class IntentPurchaseFlowTests
    {
        private static PurchaseIntent Intent(string tx = "base64tx", long? expires = 120_000) =>
            new PurchaseIntent { IntentId = "0192-intent", Currency = "USDC", Amount = "5000000", Tx = tx, ExpiresAtMs = expires };

        [Test]
        public void HappyPathWithPolling()
        {
            var f = new IntentPurchaseFlow(Keys.Seq());
            Assert.AreEqual("k1", f.BeginCreate("{}"));
            f.Created(Intent());
            Assert.AreEqual(IntentStage.Review, f.Stage);
            Assert.IsTrue(f.BeginSign(0));
            f.Signed("sig", 1000);
            Assert.AreEqual(IntentStage.Confirming, f.Stage);
            Assert.IsTrue(f.Polled("pending", 2000));
            Assert.IsFalse(f.Polled("confirmed", 3000));
            Assert.AreEqual(IntentStage.Confirmed, f.Stage);
        }

        [Test]
        public void DeclinedReturnsToReviewOtherErrorsNeverResign()
        {
            var f = new IntentPurchaseFlow(Keys.Seq());
            f.BeginCreate("{}"); f.Created(Intent());
            f.BeginSign(0);
            f.SignFailed(CityErrors.From(new WalletException("no", userDeclined: true), true), 0);
            Assert.AreEqual(IntentStage.Review, f.Stage);
            Assert.IsTrue(f.BeginSign(0));
            f.SignFailed(CityErrors.From(new WalletException("socket closed"), true), 0);
            Assert.AreEqual(IntentStage.Confirming, f.Stage, "may have been broadcast: poll");
            Assert.IsFalse(f.CanSign(0));
        }

        [Test]
        public void PollTimeoutBecomesPendingAndRoomConfirms()
        {
            var f = new IntentPurchaseFlow(Keys.Seq());
            f.BeginCreate("{}"); f.Created(Intent());
            f.BeginSign(0); f.Signed("s", 0);
            Assert.IsFalse(f.Polled("pending", IntentPurchaseFlow.PollSeconds * 1000.0));
            Assert.AreEqual(IntentStage.Pending, f.Stage);
            Assert.IsFalse(f.ConfirmedByRoom("other"));
            Assert.IsTrue(f.ConfirmedByRoom("0192-intent"));
            Assert.AreEqual(IntentStage.Confirmed, f.Stage);
        }

        [Test]
        public void GuardsOnTxAndExpiry()
        {
            var f = new IntentPurchaseFlow(Keys.Seq());
            f.BeginCreate("{}"); f.Created(Intent(tx: null));
            Assert.IsFalse(f.CanSign(0), "no tx → Solana Pay fallback only");
            f.Reset();
            f.BeginCreate("{}"); f.Created(Intent(expires: 4000));
            Assert.IsFalse(f.CanSign(0), "under 5 s left");
            Assert.IsTrue(f.CanSign(-2000));
        }

        [Test]
        public void ExpiredAndFailedStatus()
        {
            var f = new IntentPurchaseFlow(Keys.Seq());
            f.BeginCreate("{}"); f.Created(Intent()); f.BeginSign(0); f.Signed("s", 0);
            f.Polled("expired", 1);
            Assert.AreEqual(IntentStage.Expired, f.Stage);
            Assert.AreEqual("k2", f.BeginCreate("{}"), "a new intent after expiry");
            var g = new IntentPurchaseFlow(Keys.Seq());
            g.BeginCreate("{}"); g.Created(Intent()); g.BeginSign(0); g.Signed("s", 0);
            g.Polled("failed", 1);
            Assert.AreEqual(IntentStage.Failed, g.Stage);
        }

        [Test]
        public void IntentParse()
        {
            var i = PurchaseIntent.Parse(JObject.Parse("{\"intentId\":\"0192a1c2-x\",\"currency\":\"USDC\",\"amount\":\"5000000\",\"tx\":\"AA==\",\"expiresAt\":\"2026-10-08T12:10:00Z\"}"));
            Assert.AreEqual("5000000", i.Amount);
            Assert.Throws<FormatException>(() => PurchaseIntent.Parse(JObject.Parse("{\"intentId\":\"../x\"}")));
        }
    }

    public class BusinessTests
    {
        /// <summary>contract/config/businesses.json (1.0.0-rc1), first two rows verbatim plus a proposal field.</summary>
        private const string Config = "{\"version\":\"1.0.0-rc1\",\"businesses\":[" +
            "{\"id\":\"kols_agency\",\"name\":\"KOLs Agency\",\"costCents\":5000000,\"incomeRangeCents\":[200000,600000],\"badDayChanceBps\":200,\"badDayMultiplier\":0.2,\"sellBackBps\":5000,\"badDays\":[{\"id\":\"kol_exposed\"}]}," +
            "{\"id\":\"audit_firm\",\"name\":\"Audit Firm\",\"costCents\":12000000,\"incomeRangeCents\":[400000,1500000],\"badDayChanceBps\":100,\"badDayMultiplier\":0.1}," +
            "{\"id\":\"broken\",\"costCents\":1,\"incomeRangeCents\":[5]}]}";

        [Test]
        public void ParsesConfigAndDerivesNumbers()
        {
            var list = BusinessDef.ParseConfig(JObject.Parse(Config));
            Assert.AreEqual(2, list.Count, "malformed rows are skipped");
            var kol = list[0];
            Assert.AreEqual(40000, kol.BadDayPayCents, "20% of the lower bound");
            // (1-0.02)·400000 + 0.02·40000 = 392000 + 800
            Assert.AreEqual(392800, kol.ExpectedPayCents, 1e-6);
            Assert.AreEqual(5000000 / 392800.0, kol.BreakEvenPeriods, 1e-9);
            Assert.AreEqual(2500000, kol.SellBackCents);
            CollectionAssert.AreEqual(new[] { "kol_exposed" }, kol.BadDayIds);
            Assert.AreEqual(-1, list[1].SellBackCents, "no sell-back price is invented");
        }

        [Test]
        public void OwnedAndCollectProposalShapes()
        {
            var owned = OwnedBusiness.ParseList(JObject.Parse("{\"owned\":[{\"businessId\":\"0192-b\",\"kindId\":\"kols_agency\",\"pendingCents\":\"410000\",\"nextPayoutAt\":\"2026-10-09T00:00:00Z\",\"lastDay\":{\"kind\":\"bad_day\",\"payCents\":40000,\"eventId\":\"kol_exposed\",\"at\":\"2026-10-08T00:00:00Z\"}}]}"));
            Assert.AreEqual(410000, owned[0].PendingCents);
            Assert.AreEqual("bad_day", owned[0].LastDay.Kind);
            Assert.AreEqual(CityLifeStrings.T("biz.bd.kol_exposed"), CityLifeStrings.BadDay(owned[0].LastDay.EventId));
            Assert.AreEqual(CityLifeStrings.T("biz.bd.generic"), CityLifeStrings.BadDay("unknown_event"));
            var r = CollectReceipt.Parse(JObject.Parse("{\"collectedCents\":450000,\"cashCents\":900000,\"days\":[{\"kind\":\"normal\",\"payCents\":410000},{\"kind\":\"bad_day\",\"payCents\":40000}]}"));
            Assert.AreEqual(450000, r.CollectedCents);
            Assert.AreEqual(1, r.BadDays);
            Assert.AreEqual(900000, r.CashCents);
            Assert.Throws<FormatException>(() => OwnedBusiness.Parse(JObject.Parse("{\"businessId\":\"a/b\"}")));
        }

        [Test]
        public void StubIsUnavailableNotFake()
        {
            var stub = new StubBusinessGateway();
            Assert.IsTrue(stub.IsStub);
            var t = stub.Buy("kols_agency", "k", CancellationToken.None);
            Assert.IsTrue(t.IsFaulted);
            var err = CityErrors.From(t.Exception.InnerException, true);
            Assert.AreEqual("UNAVAILABLE", err.Code);
            Assert.IsFalse(err.OutcomeUnknown, "nothing was sent");
        }

        [Test]
        public void RetainedWriteFlow()
        {
            var f = new RetainedWriteFlow(Keys.Seq());
            Assert.AreEqual("k1", f.Begin("POST", "/businesses", "kols_agency"));
            Assert.IsNull(f.Begin("POST", "/businesses", "kols_agency"), "in flight");
            f.Failed(Keys.Network());
            Assert.AreEqual(WriteStage.Unknown, f.Stage);
            Assert.IsNull(f.Begin("POST", "/businesses", "audit_firm"), "a different request cannot abandon an unknown outcome");
            Assert.AreEqual("k1", f.Begin("POST", "/businesses", "kols_agency"));
            f.Succeeded();
            Assert.AreEqual(WriteStage.Done, f.Stage);
            Assert.IsFalse(f.KeyRetained);
            f.Reset();
            Assert.AreEqual("k2", f.Begin("POST", "/businesses", "kols_agency"), "the next identical buy is a new buy");
            f.Failed(Keys.Api(402, "INSUFFICIENT_CASH"));
            Assert.AreEqual(WriteStage.Rejected, f.Stage);
            Assert.IsFalse(f.KeyRetained);
        }
    }

    public class NftTests
    {
        private const string A = "7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU";
        private const string B = "9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM";
        private const string C = "HN7cABqLq46Es1jh92dQQisAq662SmxELLLsHHe4YWrH";

        private static NftItem Item(string id, Moderation m) => new NftItem { AssetId = id, Moderation = m, ImageUrl = "https://img.ctlife.example/" + id };

        [Test]
        public void ParsesAndFailsClosedOnModeration()
        {
            var list = NftItem.ParseList(JObject.Parse("{\"items\":[{\"assetId\":\"" + A + "\",\"name\":\"Lad #1\",\"imageUrl\":\"https://x/1.png\",\"moderation\":\"ok\"},{\"assetId\":\"" + B + "\",\"moderation\":\"weird\"},{\"assetId\":\"not-base58-0OIl\"}]}"));
            Assert.AreEqual(2, list.Count, "bad rows dropped, good rows kept");
            Assert.AreEqual(Moderation.Ok, list[0].Moderation);
            Assert.AreEqual(Moderation.Pending, list[1].Moderation, "unknown verdict → pending");
            Assert.IsTrue(NftItem.IsAssetId(A));
            Assert.IsFalse(NftItem.IsAssetId("0OIl0OIl0OIl0OIl0OIl0OIl0OIl0OIl"));
        }

        [TestCase(Moderation.Ok, true, FrameVisibility.Show)]
        [TestCase(Moderation.Ok, false, FrameVisibility.Show)]
        [TestCase(Moderation.Pending, true, FrameVisibility.Placeholder)]
        [TestCase(Moderation.Pending, false, FrameVisibility.Hidden)]
        [TestCase(Moderation.Nsfw, true, FrameVisibility.Hidden)]
        [TestCase(Moderation.Blocked, false, FrameVisibility.Hidden)]
        public void Visibility(Moderation m, bool owner, FrameVisibility expected) =>
            Assert.AreEqual(expected, NftItem.VisibilityFor(Item(A, m), owner));

        [Test]
        public void LayoutHonorsSlotsDedupesAndFills()
        {
            var items = new List<NftItem> { Item(A, Moderation.Ok), Item(B, Moderation.Ok), Item(C, Moderation.Nsfw) };
            var frames = new List<FramePlacement> { new FramePlacement(2, A), new FramePlacement(2, B), new FramePlacement(1, A), new FramePlacement(3, C), new FramePlacement(9, "missing") };
            var slots = GalleryLayout.Assign(frames, items, 3, viewerIsOwner: true);
            Assert.AreEqual(A, slots[1].AssetId, "A keeps slot 2");
            Assert.AreEqual(B, slots[0].AssetId, "B wanted the taken slot 2 → first free");
            Assert.IsNull(slots[2], "NSFW never hangs; duplicate A dropped");
            var placements = GalleryLayout.ToPlacements(slots);
            Assert.AreEqual(2, placements.Count);
            Assert.AreEqual(1, placements[0].Slot);
        }

        [Test]
        public void LayoutOverflowAndVisitorFilter()
        {
            var items = new List<NftItem> { Item(A, Moderation.Ok), Item(B, Moderation.Pending) };
            var frames = new List<FramePlacement> { new FramePlacement(1, A), new FramePlacement(2, B) };
            Assert.AreEqual(1, Array.FindAll(GalleryLayout.Assign(frames, items, 1, true), s => s != null).Length, "overflow dropped");
            var visitor = GalleryLayout.Assign(frames, items, 2, viewerIsOwner: false);
            Assert.IsNull(visitor[1], "visitors never see pending art");
            Assert.AreEqual(0, GalleryLayout.Assign(frames, items, 0, true).Length);
        }

        [Test]
        public void PlacementJsonRoundTrip()
        {
            var json = FramePlacement.ToJson(new[] { new FramePlacement(1, A) });
            var back = FramePlacement.ParseList(json);
            Assert.AreEqual(1, back.Count);
            Assert.AreEqual(A, back[0].AssetId);
        }

        [Test]
        public void ImagePolicyHostsHttpsAndCaps()
        {
            var p = ImageFetchPolicy.Parse(JObject.Parse("{\"imageHosts\":[\"img.ctlife.example\",\".cdn.example\",\"bad host!\"],\"maxImageBytes\":262144,\"maxImagePx\":512}"));
            Assert.AreEqual(2, p.Hosts.Count);
            Assert.IsTrue(p.IsAllowed("https://img.ctlife.example/a.png"));
            Assert.IsTrue(p.IsAllowed("https://eu.cdn.example/a.png"));
            Assert.IsFalse(p.IsAllowed("https://cdn.example/a.png"), "'.cdn.example' means subdomains only");
            Assert.IsFalse(p.IsAllowed("http://img.ctlife.example/a.png"), "https only");
            Assert.IsFalse(p.IsAllowed("https://user:pw@img.ctlife.example/a.png"));
            Assert.IsFalse(p.IsAllowed("https://img.ctlife.example:8443/a.png"));
            Assert.IsFalse(p.IsAllowed("https://evil.example/img.ctlife.example"));
            Assert.IsFalse(p.IsAllowed("ipfs://cid"));
            Assert.IsTrue(p.AcceptsLength(-1));
            Assert.IsFalse(p.AcceptsLength(262145));
            Assert.IsTrue(p.AcceptsSize(512, 300));
            Assert.IsFalse(p.AcceptsSize(513, 10));
            var none = ImageFetchPolicy.Parse(null);
            Assert.IsFalse(none.IsAllowed("https://img.ctlife.example/a.png"), "no configured hosts → nothing is fetched");
            Assert.AreEqual(ImageFetchPolicy.DefaultMaxBytes, none.MaxBytes);
        }

        [Test]
        public void LruEvictsOldestWithinBudget()
        {
            var cache = new ByteLruCache<string>(100);
            Assert.IsEmpty(cache.Put("a", "A", 40));
            Assert.IsEmpty(cache.Put("b", "B", 40));
            Assert.IsTrue(cache.TryGet("a", out _), "touch a → b is oldest");
            CollectionAssert.AreEqual(new[] { "B" }, cache.Put("c", "C", 40));
            Assert.IsFalse(cache.TryGet("b", out _));
            Assert.AreEqual(80, cache.Bytes);
            CollectionAssert.AreEqual(new[] { "Z" }, cache.Put("z", "Z", 101), "larger than the budget is rejected back to the caller");
            CollectionAssert.AreEqual(new[] { "A" }, cache.Put("a", "A2", 10), "replacing returns the old value");
            Assert.AreEqual(2, cache.Clear().Count);
            Assert.AreEqual(0, cache.Bytes);
        }

        [Test]
        public void StubGatewayUnavailable()
        {
            var g = new StubNftGateway();
            Assert.AreEqual("UNAVAILABLE", CityErrors.From(g.Mine(CancellationToken.None).Exception.InnerException, false).Code);
        }
    }

    public class CityErrorsAndWireTests
    {
        [Test]
        public void ClassifiesTransportFailures()
        {
            var w = Keys.Network();
            Assert.AreEqual("NETWORK_ERROR", w.Code);
            Assert.IsTrue(w.OutcomeUnknown);
            Assert.AreEqual("err.unknownOutcome", w.Key);
            var r = CityErrors.From(new ApiException(0, "NETWORK_ERROR", "x", isNetwork: true), false);
            Assert.IsFalse(r.OutcomeUnknown);
            Assert.AreEqual("err.network", r.Key);
            Assert.IsTrue(CityErrors.From(new ApiException(502, "HTTP_502", "x"), true).OutcomeUnknown);
            Assert.AreEqual("err.server", CityErrors.From(new ApiException(502, "HTTP_502", "x"), false).Key);
            Assert.IsTrue(CityErrors.From(new ApiException(200, "DECODE_FAILED", "x"), true).OutcomeUnknown);
            Assert.IsTrue(CityErrors.From(new FormatException(), true).OutcomeUnknown);
            Assert.AreEqual("UNAVAILABLE", CityErrors.From(new ApiException(404, "HTTP_404", "x"), false).Code);
            Assert.AreEqual("NOT_FOUND", CityErrors.From(new ApiException(404, "NOT_FOUND", "x"), false).Code);
            Assert.AreEqual("UNAVAILABLE", CityErrors.From(new UnavailableException(), true).Code);
        }

        [Test]
        public void BackendVariantsMapToContractMeaning()
        {
            var cash = Keys.Api(400, "VALIDATION_FAILED", JObject.Parse("{\"reason\":\"insufficient cash\",\"have\":\"1000\",\"need\":\"6000\"}"));
            Assert.AreEqual("INSUFFICIENT_CASH", cash.Code);
            Assert.AreEqual("Not enough Cash (short $50.00).", cash.Message);
            var limited = Keys.Api(429, "RATE_LIMITED", JObject.Parse("{\"retryAfterMs\":30000}"));
            Assert.IsTrue(limited.Retryable);
            Assert.AreEqual("Slow down. Try again in 30s.", limited.Message);
            Assert.AreEqual("err.generic", Keys.Api(418, "TEAPOT").Key);
        }

        [Test]
        public void WireToleratesStringsAndWrappers()
        {
            Assert.AreEqual(5000, CityWire.Long(JToken.Parse("\"5000\"")));
            Assert.AreEqual(5000, CityWire.Long(JToken.Parse("5000.0")));
            Assert.Throws<FormatException>(() => CityWire.Long(JToken.Parse("5000.5")));
            Assert.AreEqual(-1, CityWire.LongOr(JToken.Parse("\"x\""), -1));
            Assert.AreEqual(1, CityWire.List(JObject.Parse("{\"items\":[1]}"), "items").Count);
            Assert.Throws<FormatException>(() => CityWire.List(JObject.Parse("{\"x\":[1]}"), "items"));
            Assert.AreEqual(DateTimeOffset.Parse("2026-10-11T20:00:00Z").ToUnixTimeMilliseconds(), CityWire.TimeMs(JToken.Parse("\"2026-10-11T20:00:00.000Z\"")));
            Assert.AreEqual(1700000000000, CityWire.TimeMs(JToken.Parse("1700000000000")));
            Assert.IsNull(CityWire.TimeMsOrNull(JToken.Parse("\"soon\"")));
            Assert.AreEqual("ab", CityWire.Plain(JToken.Parse("\"a\\u0000b\""), 10));
            Assert.IsTrue(CityWire.IsSafeRoomId("home:0192a1c2-xyz"));
            Assert.IsFalse(CityWire.IsSafeId("home:0192"));
            Assert.Throws<ArgumentException>(() => CityWire.Seg("a/b"));
        }

        [Test]
        public void RetainedKeySemantics()
        {
            var k = new RetainedKey(Keys.Seq());
            Assert.AreEqual("k1", k.KeyFor("POST", "/x", "a"));
            Assert.AreEqual("k1", k.KeyFor("POST", "/x", "a"));
            Assert.AreEqual("k2", k.KeyFor("POST", "/x", "b"), "a different body never reuses a key");
            k.Settle(Keys.Network());
            Assert.IsTrue(k.Pending);
            k.Settle(Keys.Api(400, "VALIDATION_FAILED"));
            Assert.IsFalse(k.Pending);
        }

        [Test]
        public void StringsFormatSafely()
        {
            Assert.AreEqual("Not enough Cash (short {0}).", CityLifeStrings.T("err.insufficientCash"));
            Assert.AreEqual("missing.key", CityLifeStrings.T("missing.key"));
            Assert.AreEqual("Touch grass", CityLifeStrings.ActionLabel("touch_grass"));
            Assert.AreEqual("Touch the one plant you own", CityLifeStrings.FurnitureLabel("plant", "touch_grass"));
            Assert.AreEqual("Eat", CityLifeStrings.FurnitureLabel("unknown", "eat"));
        }

        [Test]
        public void ContractCoverageListsOnlyContractRoutes()
        {
            foreach (var op in CityLifeApi.ContractCovered) CollectionAssert.DoesNotContain(CityLifeApi.ProposalRoutes, op);
            Assert.AreEqual(15, CityLifeApi.ContractCovered.Length);
        }
    }
}
