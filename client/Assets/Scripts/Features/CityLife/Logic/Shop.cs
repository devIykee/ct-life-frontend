using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife
{
    /// <summary>The seven look slots in the contract's realtime order: [skinTone, headId, hairId, topId, bottomId, shoesId, accId].</summary>
    public static class LookSlots
    {
        public static readonly string[] Slots = { "skin", "head", "hair", "top", "bottom", "shoes", "acc" };
        public static readonly string[] Fields = { "skinTone", "headId", "hairId", "topId", "bottomId", "shoesId", "accId" };

        public static int IndexOf(string slot) => Array.IndexOf(Slots, slot);

        public static JObject ToJson(string[] look)
        {
            var o = new JObject();
            for (int i = 0; i < Fields.Length; i++) o[Fields[i]] = look != null && i < look.Length ? look[i] ?? "" : "";
            return o;
        }
    }

    /// <summary>
    /// A Cash shop row. Contract `ShopItem {id, slot, name, priceCents}` (bare array). Tolerated backend shape
    /// `{items: [{itemId (uuid), slot, itemKey, name, priceCents, onchainOnly}]}`: the purchase uses `itemId`, the look
    /// uses `itemKey`.
    /// </summary>
    public sealed class ShopRow
    {
        public string PurchaseId, LookId, Slot, Name;
        public long PriceCents;
        public bool OnchainOnly;

        public static List<ShopRow> ParseList(JToken t)
        {
            var list = new List<ShopRow>();
            foreach (var r in CityWire.List(t, "items", "shop"))
            {
                var slot = CityWire.Str(r?["slot"]);
                if (LookSlots.IndexOf(slot) < 0) continue;
                var key = CityWire.Str(r["itemKey"]);
                var id = CityWire.Str(r["id"]) ?? CityWire.Str(r["itemId"]);
                if (!CityWire.IsSafeId(id)) continue;
                var look = CityWire.IsSafeId(key) ? key : id;
                list.Add(new ShopRow
                {
                    PurchaseId = id, LookId = look, Slot = slot,
                    Name = CityWire.Plain(r["name"], 40), PriceCents = CityWire.LongOr(r["priceCents"], -1),
                    OnchainOnly = CityWire.BoolOr(r["onchainOnly"], false),
                });
            }
            return list;
        }
    }

    /// <summary>Owned cosmetics. Contract `Inventory {items: [{itemId, slot, acquiredAt}]}`; backend rows add `itemKey`, `name`, `source`.</summary>
    public sealed class InventoryView
    {
        public readonly Dictionary<string, string> SlotByLookId = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);

        public bool Owns(string lookId) => !string.IsNullOrEmpty(lookId) && SlotByLookId.ContainsKey(lookId);

        public IEnumerable<string> InSlot(string slot) => SlotByLookId.Where(kv => kv.Value == slot).Select(kv => kv.Key);

        public static InventoryView Parse(JToken t)
        {
            var v = new InventoryView();
            JArray rows;
            if (t is JObject o && o["items"] == null && (o["itemId"] != null || o["itemKey"] != null)) rows = new JArray(o); // one purchased row (backend)
            else rows = CityWire.List(t, "items", "inventory");
            foreach (var r in rows)
            {
                var slot = CityWire.Str(r?["slot"]);
                var key = CityWire.Str(r?["itemKey"]);
                var id = CityWire.IsSafeId(key) ? key : CityWire.Str(r?["itemId"]);
                if (!CityWire.IsSafeId(id) || LookSlots.IndexOf(slot) < 0) continue;
                v.SlotByLookId[id] = slot;
                var name = CityWire.Plain(r["name"], 40);
                if (!string.IsNullOrEmpty(name)) v.Names[id] = name;
            }
            return v;
        }

        public void Merge(InventoryView other)
        {
            if (other == null) return;
            foreach (var kv in other.SlotByLookId) SlotByLookId[kv.Key] = kv.Value;
            foreach (var kv in other.Names) Names[kv.Key] = kv.Value;
        }
    }

    /// <summary>
    /// A look being edited in the wardrobe. A slot may only change to an item the server lists as owned, a free starter
    /// item from the item registry (`cashCents: 0` / `starter: true`), or back to what is worn now. The server validates
    /// `PATCH /characters/me/look` regardless. Pure; tested.
    /// </summary>
    public sealed class LookDraft
    {
        private readonly string[] _worn;
        private readonly string[] _draft;

        public LookDraft(string[] worn)
        {
            _worn = new string[7];
            for (int i = 0; i < 7; i++) _worn[i] = worn != null && i < worn.Length ? worn[i] ?? "" : "";
            _draft = (string[])_worn.Clone();
        }

        public string[] Worn => (string[])_worn.Clone();
        public string[] Current => (string[])_draft.Clone();
        public string this[string slot] => LookSlots.IndexOf(slot) is int i && i >= 0 ? _draft[i] : null;

        public bool IsDirty => !_worn.SequenceEqual(_draft);

        public bool CanWear(string lookId, string slot, InventoryView inventory, ISet<string> freeItems)
        {
            int i = LookSlots.IndexOf(slot);
            if (i <= 0 || !CityWire.IsSafeId(lookId)) return false; // skinTone is not an item
            if (lookId == _worn[i]) return true;
            if (inventory != null && inventory.SlotByLookId.TryGetValue(lookId, out var s) && s == slot) return true;
            return freeItems != null && freeItems.Contains(lookId);
        }

        public bool Set(string slot, string lookId, InventoryView inventory, ISet<string> freeItems)
        {
            if (!CanWear(lookId, slot, inventory, freeItems)) return false;
            _draft[LookSlots.IndexOf(slot)] = lookId;
            return true;
        }

        public void Revert() { Array.Copy(_worn, _draft, 7); }

        /// <summary>The server accepted the look: what is worn is now the draft (or the server's echo when given).</summary>
        public void Commit(string[] serverLook)
        {
            var src = serverLook != null && serverLook.Length == 7 && serverLook.All(s => s != null) ? serverLook : _draft;
            Array.Copy(src, _worn, 7);
            Array.Copy(src, _draft, 7);
        }

        /// <summary>Free starter items from the item registry rows (`{id, slot, cashCents | starter}`).</summary>
        public static HashSet<string> FreeItems(JToken idsItems)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            JArray rows;
            try { rows = CityWire.List(idsItems, "items"); } catch (FormatException) { return set; }
            foreach (var r in rows)
            {
                var id = CityWire.Str(r?["id"]);
                if (!CityWire.IsSafeId(id)) continue;
                bool free = r["starter"] != null ? CityWire.BoolOr(r["starter"], false) : CityWire.LongOr(r["cashCents"], -1) == 0;
                if (free) set.Add(id);
            }
            return set;
        }

        /// <summary>A `Character` reply → its look array, or null when absent.</summary>
        public static string[] LookFrom(JToken character)
        {
            var look = character?["look"] as JObject;
            if (look == null) return null;
            var arr = new string[7];
            for (int i = 0; i < 7; i++) arr[i] = CityWire.Str(look[LookSlots.Fields[i]]) ?? "";
            return arr;
        }
    }

    public enum SaveStage { Clean, Dirty, Saving, Saved, Rejected, Unknown }

    /// <summary>Equip state machine around `PATCH /characters/me/look` with a retained Idempotency-Key. Pure; tested.</summary>
    public sealed class EquipFlow
    {
        private readonly RetainedKey _key;
        public SaveStage Stage { get; private set; } = SaveStage.Clean;
        public CityError Error { get; private set; }
        public readonly LookDraft Draft;
        private string _pendingBody;
        public event Action Changed;

        public EquipFlow(LookDraft draft, Func<string> newKey = null) { Draft = draft; _key = new RetainedKey(newKey); }

        public bool Wear(string slot, string lookId, InventoryView inv, ISet<string> free)
        {
            if (Stage == SaveStage.Saving) return false;
            if (Stage == SaveStage.Unknown && _pendingBody != null) return false; // settle the unknown save first
            if (!Draft.Set(slot, lookId, inv, free)) return false;
            Stage = Draft.IsDirty ? SaveStage.Dirty : SaveStage.Clean;
            Error = null;
            Changed?.Invoke();
            return true;
        }

        public void Revert()
        {
            if (Stage == SaveStage.Saving) return;
            Draft.Revert();
            _key.Complete();
            _pendingBody = null;
            Stage = SaveStage.Clean;
            Error = null;
            Changed?.Invoke();
        }

        /// <summary>Returns (body, key) to send, or null when there is nothing to save.</summary>
        public (string body, string key)? BeginSave()
        {
            if (Stage != SaveStage.Dirty && Stage != SaveStage.Unknown && Stage != SaveStage.Rejected) return null;
            if (!Draft.IsDirty && Stage != SaveStage.Unknown) return null;
            var body = LookSlots.ToJson(Draft.Current).ToString(Newtonsoft.Json.Formatting.None);
            _pendingBody = body;
            Stage = SaveStage.Saving;
            Error = null;
            Changed?.Invoke();
            return (body, _key.KeyFor("PATCH", "/characters/me/look", body));
        }

        public void Saved(string[] serverLook)
        {
            if (Stage != SaveStage.Saving) return;
            _key.Complete();
            _pendingBody = null;
            Draft.Commit(serverLook);
            Stage = SaveStage.Saved;
            Changed?.Invoke();
        }

        public void SaveFailed(CityError error)
        {
            if (Stage != SaveStage.Saving) return;
            Error = error ?? new CityError { Code = "ERROR", Key = "err.generic" };
            _key.Settle(Error);
            if (!Error.OutcomeUnknown) _pendingBody = null;
            Stage = Error.OutcomeUnknown ? SaveStage.Unknown : SaveStage.Rejected;
            Changed?.Invoke();
        }

        public bool KeyRetained => _key.Pending;
    }

    // ------------------------------------------------------------------ season pass

    public enum PassState { Unknown, None, Active, Expired, Unavailable }

    /// <summary>
    /// Season pass status. Contract rc1 only has the purchase (`POST /payments/intents {kind: season_pass}`); the status
    /// read is proposed (`GET /season-pass/me`, docs/fe-contract-proposals/city-life-shop-season-pass.md). The backend
    /// checkout already serves that path as `{passId, tier: free|season, startedAt, expiresAt, pricePaidCents}` or a
    /// contract-shaped 404 NOT_FOUND when the player has no pass.
    /// </summary>
    public sealed class SeasonPassView
    {
        public PassState State;
        public string Tier;
        public long? StartedAtMs, ExpiresAtMs;

        public static SeasonPassView Parse(JToken t, double nowMs)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a season pass");
            var v = new SeasonPassView
            {
                Tier = CityWire.Str(o["tier"]) ?? "season",
                StartedAtMs = CityWire.TimeMsOrNull(o["startedAt"]),
                ExpiresAtMs = CityWire.TimeMsOrNull(o["expiresAt"]),
            };
            if (v.Tier == "free") v.State = PassState.None;
            else if (v.ExpiresAtMs.HasValue && v.ExpiresAtMs.Value <= nowMs) v.State = PassState.Expired;
            else v.State = PassState.Active;
            return v;
        }

        public static SeasonPassView From(CityError error)
        {
            if (error != null && error.Is("NOT_FOUND")) return new SeasonPassView { State = PassState.None };
            if (error != null && error.Is("UNAVAILABLE")) return new SeasonPassView { State = PassState.Unavailable };
            return new SeasonPassView { State = PassState.Unknown };
        }
    }

    /// <summary>A payment intent for a non-top-up purchase (contract `PaymentIntent`).</summary>
    public sealed class PurchaseIntent
    {
        public string IntentId, Currency, Amount, Tx, SolanaPayUrl;
        public long? ExpiresAtMs;

        public static PurchaseIntent Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected an intent");
            var id = CityWire.ReqStr(o["intentId"], "intentId");
            if (!CityWire.IsSafeId(id)) throw new FormatException("Invalid intentId");
            return new PurchaseIntent
            {
                IntentId = id, Currency = CityWire.Str(o["currency"]), Amount = CityWire.Str(o["amount"]),
                Tx = CityWire.Str(o["tx"]), SolanaPayUrl = CityWire.Str(o["solanaPayUrl"]),
                ExpiresAtMs = CityWire.TimeMsOrNull(o["expiresAt"]),
            };
        }
    }

    public enum IntentStage { Idle, Creating, Review, Signing, Confirming, Confirmed, Pending, Expired, Failed, Declined }

    /// <summary>
    /// Real-money purchase of a non-Cash product (season pass, on-chain cosmetic): create intent → sign in wallet (MWA)
    /// → poll `GET /payments/intents/{id}` every second for up to 30 s (contract section 4) → confirmed or "pending"
    /// (the city room's `payment_confirmed` closes it later). A wallet error after a possible broadcast never re-signs
    /// that intent. Pure; tested.
    /// </summary>
    public sealed class IntentPurchaseFlow
    {
        public const int PollSeconds = 30;
        public const int MinSecondsLeftToSign = 5;
        private readonly RetainedKey _key;
        public IntentStage Stage { get; private set; } = IntentStage.Idle;
        public PurchaseIntent Intent { get; private set; }
        public CityError Error { get; private set; }
        public string Signature { get; private set; }
        public double ConfirmStartedMs { get; private set; }
        private bool _maybeBroadcast;
        public event Action Changed;

        public IntentPurchaseFlow(Func<string> newKey = null) { _key = new RetainedKey(newKey); }

        public string BeginCreate(string body)
        {
            if (Stage != IntentStage.Idle && Stage != IntentStage.Failed && Stage != IntentStage.Expired) return null;
            Stage = IntentStage.Creating;
            Error = null;
            Intent = null;
            _maybeBroadcast = false;
            Changed?.Invoke();
            return _key.KeyFor("POST", "/payments/intents", body);
        }

        public void Created(PurchaseIntent intent)
        {
            if (Stage != IntentStage.Creating) return;
            _key.Complete();
            Intent = intent;
            Stage = IntentStage.Review;
            Changed?.Invoke();
        }

        public void CreateFailed(CityError e)
        {
            if (Stage != IntentStage.Creating) return;
            Error = e;
            _key.Settle(e);
            Stage = IntentStage.Failed;
            Changed?.Invoke();
        }

        public bool CanSign(double nowMs) =>
            Stage == IntentStage.Review && Intent != null && !string.IsNullOrEmpty(Intent.Tx) && !_maybeBroadcast &&
            (!Intent.ExpiresAtMs.HasValue || Intent.ExpiresAtMs.Value - nowMs > MinSecondsLeftToSign * 1000.0);

        public bool BeginSign(double nowMs)
        {
            if (!CanSign(nowMs)) return false;
            Stage = IntentStage.Signing;
            Changed?.Invoke();
            return true;
        }

        public void Signed(string signature, double nowMs)
        {
            if (Stage != IntentStage.Signing) return;
            Signature = signature;
            _maybeBroadcast = true;
            ConfirmStartedMs = nowMs;
            Stage = IntentStage.Confirming;
            Changed?.Invoke();
        }

        /// <summary>The wallet declined (nothing was sent) → back to review; any other wallet error → keep polling, never re-sign.</summary>
        public void SignFailed(CityError e, double nowMs)
        {
            if (Stage != IntentStage.Signing) return;
            Error = e;
            if (e != null && e.Is("WALLET_DECLINED")) Stage = IntentStage.Review;
            else
            {
                _maybeBroadcast = true;
                ConfirmStartedMs = nowMs;
                Stage = IntentStage.Confirming;
            }
            Changed?.Invoke();
        }

        /// <summary>One poll result: pending | confirmed | expired | failed. Returns true while polling should continue.</summary>
        public bool Polled(string status, double nowMs)
        {
            if (Stage != IntentStage.Confirming) return false;
            switch (status)
            {
                case "confirmed": Stage = IntentStage.Confirmed; break;
                case "expired": Stage = IntentStage.Expired; break;
                case "failed": Stage = IntentStage.Failed; Error = new CityError { Code = "PAYMENT_FAILED", Key = "pay.failed" }; break;
                default:
                    if (nowMs - ConfirmStartedMs >= PollSeconds * 1000.0) Stage = IntentStage.Pending;
                    else return true;
                    break;
            }
            Changed?.Invoke();
            return false;
        }

        /// <summary>City room `payment_confirmed {intentId}` closes the purchase from any stage.</summary>
        public bool ConfirmedByRoom(string intentId)
        {
            if (Intent == null || intentId != Intent.IntentId || Stage == IntentStage.Confirmed) return false;
            Stage = IntentStage.Confirmed;
            Changed?.Invoke();
            return true;
        }

        public void Reset()
        {
            if (Stage == IntentStage.Creating || Stage == IntentStage.Signing || Stage == IntentStage.Confirming) return;
            _key.Complete();
            Stage = IntentStage.Idle;
            Intent = null;
            Error = null;
            Signature = null;
            _maybeBroadcast = false;
            Changed?.Invoke();
        }
    }
}
