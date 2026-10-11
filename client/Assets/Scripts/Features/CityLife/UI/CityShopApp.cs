using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// The Shop app (id `shop`, Phase 4; replaces the "Shop" ComingApp): Cash cosmetics (`GET /shop`, `POST /shop/purchase`),
    /// the wardrobe with try-on and equip (`GET /inventory`, `PATCH /characters/me/look`), and the season pass (status is a
    /// proposal; purchase is the contract's `POST /payments/intents {kind: season_pass}` signed with MWA).
    /// </summary>
    public sealed class CityShopApp : CityApp
    {
        public override string Id => "shop";
        public override string Title => CityLifeStrings.T("app.shop");
        public override string Color => "#fc5c65";
        public override int Phase => 4;

        internal override CityView CreateView(CityLifeHub hub) => new ShopView(hub);

        private sealed class ShopView : CityView
        {
            private readonly VisualElement _tabs, _body = El.Div();
            private string _tab = "shop";
            private List<ShopRow> _shop;
            private InventoryView _inventory;
            private CityError _shopError, _invError;
            private readonly Dictionary<string, RetainedWriteFlow> _buys = new Dictionary<string, RetainedWriteFlow>(StringComparer.Ordinal);
            private readonly HashSet<string> _free;
            private EquipFlow _equip;
            private AvatarPreview _preview;
            private Image _previewImage;
            private string _slot = "top";
            // season pass
            private SeasonPassView _pass;
            private readonly IntentPurchaseFlow _buyPass = new IntentPurchaseFlow();
            private bool _mainnetAck;

            public ShopView(CityLifeHub hub) : base(hub)
            {
                _free = LookDraft.FreeItems(Services.Get<ConfigStore>().Ids("items"));
                _equip = new EquipFlow(new LookDraft(hub.Profile?.LookArray()));
                _equip.Changed += Render;
                _buyPass.Changed += Render;
                hub.PaymentConfirmed += OnPaymentConfirmed;
                Root.Add(CityUi.H1(CityLifeStrings.T("shop.title")));
                _tabs = CityUi.Row();
                Root.Add(_tabs);
                Root.Add(_body);
                Load();
                LoadPass();
                Render();
            }

            private void Load()
            {
                Run(ct => Hub.Api.Shop(ct), rows => { _shop = rows; _shopError = null; Render(); }, e => { _shopError = e; Render(); }, false);
                Run(ct => Hub.Api.Inventory(ct), inv => { _inventory = inv; _invError = null; Render(); }, e => { _invError = e; Render(); }, false);
            }

            private void LoadPass() => Run(ct => Hub.Api.SeasonPass(Hub.NowMs, ct), p => { _pass = p; Render(); }, e => { _pass = SeasonPassView.From(e); Render(); }, false);

            private void Render()
            {
                if (!Alive) return;
                _tabs.Clear();
                foreach (var t in new[] { "shop", "wardrobe", "pass" })
                {
                    var key = t;
                    _tabs.Add(CityUi.Chip(CityLifeStrings.T("shop.tab." + t), () => { _tab = key; Render(); }, _tab == t));
                }
                _body.Clear();
                if (_tab != "wardrobe") HidePreview();
                if (_tab == "shop") RenderShop();
                else if (_tab == "wardrobe") RenderWardrobe();
                else RenderPass();
            }

            // ------------------------------------------------------------ Cash shop

            private void RenderShop()
            {
                _body.Add(CityUi.Small(CityLifeStrings.T("shop.hint")));
                if (_shopError != null) { _body.Add(CityUi.Error(_shopError.Message)); _body.Add(CityUi.Btn(CityLifeStrings.T("common.retry"), Load, "ct-btn")); return; }
                if (_shop == null) { _body.Add(CityUi.Small(CityLifeStrings.T("common.loading"))); return; }
                foreach (var slot in LookSlots.Slots)
                {
                    var rows = _shop.Where(r => r.Slot == slot).ToList();
                    if (rows.Count == 0) continue;
                    var card = CityUi.Card(CityUi.H2(CityLifeStrings.T("shop.slot." + slot)));
                    foreach (var r in rows) card.Add(ShopRowView(r));
                    _body.Add(card);
                }
                if (_shop.Count == 0) _body.Add(CityUi.Small(CityLifeStrings.T("shop.empty")));
            }

            private VisualElement ShopRowView(ShopRow r)
            {
                var row = CityUi.Row(CityUi.Text(string.IsNullOrEmpty(r.Name) ? CityLifeStrings.Readable(r.LookId) : r.Name, "ct-body"), El.Div("ct-grow"));
                bool owned = _inventory != null && _inventory.Owns(r.LookId);
                _buys.TryGetValue(r.PurchaseId, out var flow);
                if (owned) row.Add(CityUi.Text(CityLifeStrings.T("shop.owned"), "ct-badge"));
                else if (r.OnchainOnly) row.Add(CityUi.Small(CityLifeStrings.T("shop.onchain")));
                else if (r.PriceCents >= 0)
                {
                    var item = r;
                    var b = CityUi.Btn(flow?.Stage == WriteStage.Unknown ? CityLifeStrings.T("common.retry") : CityLifeStrings.F("shop.buy", Money.Cash(r.PriceCents)), () => Buy(item), "ct-btn");
                    b.SetEnabled(flow == null || flow.Stage != WriteStage.Sending);
                    row.Add(b);
                }
                var box = El.Div(null, row);
                if (flow?.Error != null) box.Add(CityUi.Error(flow.Error.Message));
                return box;
            }

            private void Buy(ShopRow r)
            {
                if (!_buys.TryGetValue(r.PurchaseId, out var flow)) _buys[r.PurchaseId] = flow = new RetainedWriteFlow();
                if (flow.Stage == WriteStage.Rejected || flow.Stage == WriteStage.Done) flow.Reset();
                var key = flow.Begin("POST", "/shop/purchase", r.PurchaseId);
                if (key == null) return;
                Render();
                Run(ct => Hub.Api.Purchase(r.PurchaseId, key, ct), inv =>
                {
                    flow.Succeeded();
                    if (_inventory == null) _inventory = inv; else _inventory.Merge(inv);
                    Bus.Publish(ToastSignal.Text(CityLifeStrings.F("shop.bought", string.IsNullOrEmpty(r.Name) ? r.LookId : r.Name), ToastKind.Good));
                    Bus.Publish(new SfxSignal("sfx_ui_success"));
                    Hub.RefreshProfile(force: true);
                    Run(ct => Hub.Api.Inventory(ct), full => { _inventory = full; Render(); }, _ => { }, false);
                    Render();
                }, e =>
                {
                    flow.Failed(e);
                    if (e.OutcomeUnknown) Hub.RefreshProfile(force: true);
                    Render();
                }, true);
            }

            // ------------------------------------------------------------ wardrobe + try-on

            private void RenderWardrobe()
            {
                if (_invError != null) _body.Add(CityUi.Error(_invError.Message));
                ShowPreview();
                if (_previewImage != null) _body.Add(_previewImage);
                var slots = CityUi.Row();
                foreach (var s in LookSlots.Slots.Skip(1))
                {
                    var key = s;
                    slots.Add(CityUi.Chip(CityLifeStrings.T("shop.slot." + s), () => { _slot = key; Render(); }, _slot == s));
                }
                _body.Add(slots);
                var draft = _equip.Draft;
                _body.Add(CityUi.Pair(CityLifeStrings.T("wardrobe.wearing"), Name(draft[_slot])));
                var options = new List<string>();
                if (!string.IsNullOrEmpty(draft.Worn[LookSlots.IndexOf(_slot)])) options.Add(draft.Worn[LookSlots.IndexOf(_slot)]);
                if (_inventory != null) options.AddRange(_inventory.InSlot(_slot));
                foreach (var kv in Services.Get<ConfigStore>().Registries.Items)
                    if (kv.Value.slot == _slot && _free.Contains(kv.Key)) options.Add(kv.Key);
                foreach (var id in options.Distinct())
                {
                    var look = id;
                    var b = CityUi.Chip(Name(id) + (_free.Contains(id) ? " · " + CityLifeStrings.T("wardrobe.starter") : ""),
                        () => { if (_equip.Wear(_slot, look, _inventory, _free)) _preview?.SetLook(_equip.Draft.Current); }, draft[_slot] == id);
                    b.SetEnabled(_equip.Stage != SaveStage.Saving && _equip.Draft.CanWear(id, _slot, _inventory, _free));
                    _body.Add(b);
                }
                switch (_equip.Stage)
                {
                    case SaveStage.Saving: _body.Add(CityUi.Small(CityLifeStrings.T("wardrobe.saving"))); break;
                    case SaveStage.Saved: _body.Add(CityUi.Small(CityLifeStrings.T("wardrobe.saved"))); break;
                    case SaveStage.Unknown: _body.Add(CityUi.Error(CityLifeStrings.T("wardrobe.unknown"))); break;
                    case SaveStage.Rejected: _body.Add(CityUi.Error(_equip.Error?.Message ?? "")); break;
                }
                var save = CityUi.Btn(CityLifeStrings.T(_equip.Stage == SaveStage.Unknown ? "common.retry" : "wardrobe.save"), SaveLook);
                save.SetEnabled(_equip.Stage == SaveStage.Dirty || _equip.Stage == SaveStage.Unknown || (_equip.Stage == SaveStage.Rejected && _equip.Draft.IsDirty));
                var revert = CityUi.Btn(CityLifeStrings.T("wardrobe.revert"), () => { _equip.Revert(); _preview?.SetLook(_equip.Draft.Current); }, "ct-btn");
                revert.SetEnabled(_equip.Draft.IsDirty && _equip.Stage != SaveStage.Saving);
                _body.Add(CityUi.Row(save, revert));
            }

            private string Name(string lookId)
            {
                if (string.IsNullOrEmpty(lookId)) return "-";
                if (_inventory != null && _inventory.Names.TryGetValue(lookId, out var n)) return n;
                var row = _shop?.Find(r => r.LookId == lookId);
                if (row != null && !string.IsNullOrEmpty(row.Name)) return row.Name;
                var def = Services.Get<ConfigStore>().Registries.Items.TryGetValue(lookId, out var d) ? d : null;
                return !string.IsNullOrEmpty(def?.name) ? def.name : CityLifeStrings.Readable(lookId);
            }

            private void SaveLook()
            {
                var req = _equip.BeginSave();
                if (req == null) return;
                var (body, key) = req.Value;
                Run(ct => Hub.Api.PatchLook(body, key, ct), echo =>
                {
                    _equip.Saved(echo);
                    Bus.Publish(new SfxSignal("sfx_ui_success"));
                    Hub.RefreshProfile(force: true);
                }, e =>
                {
                    _equip.SaveFailed(e);
                    if (e.OutcomeUnknown) Hub.RefreshProfile(force: true);
                }, true);
            }

            private void ShowPreview()
            {
                if (_preview != null) return;
                try
                {
                    _preview = new AvatarPreview(540, 720);
                    _preview.SetLook(_equip.Draft.Current);
                    _previewImage = new Image { image = _preview.Texture, scaleMode = ScaleMode.ScaleToFit };
                    _previewImage.style.height = 260;
                }
                catch (Exception e)
                {
                    Log.Warn("citylife", "Try-on preview unavailable: " + e.GetType().Name);
                    _preview = null;
                    _previewImage = null;
                }
            }

            private void HidePreview()
            {
                _preview?.Dispose();
                _preview = null;
                _previewImage = null;
            }

            // ------------------------------------------------------------ season pass

            private void RenderPass()
            {
                var card = CityUi.Card(CityUi.H2(CityLifeStrings.T("pass.title")));
                switch (_pass?.State ?? PassState.Unknown)
                {
                    case PassState.Active: card.Add(CityUi.Text(CityLifeStrings.F("pass.active", CityUi.When(_pass.ExpiresAtMs ?? 0)))); break;
                    case PassState.Expired: card.Add(CityUi.Text(CityLifeStrings.F("pass.expired", CityUi.When(_pass.ExpiresAtMs ?? 0)))); break;
                    case PassState.None: card.Add(CityUi.Text(CityLifeStrings.T("pass.none"))); break;
                    case PassState.Unavailable: card.Add(CityUi.Small(CityLifeStrings.T("pass.unavailable"))); break;
                    default: card.Add(CityUi.Small(_pass == null ? CityLifeStrings.T("common.loading") : CityLifeStrings.T("pass.unknown"))); break;
                }
                RenderPassPurchase(card);
                _body.Add(card);
            }

            private string PassSku => CityWire.Str(Services.Get<ConfigStore>().Config("seasonPass")?["sku"] ?? Services.Get<ConfigStore>().Root?["seasonPass"]?["sku"]);

            private void RenderPassPurchase(VisualElement card)
            {
                var sku = PassSku;
                var cluster = Env.Current != null ? Env.Current.cluster : SolanaCluster.None;
                bool payments = Features.IsAvailable(Features.Payments) && cluster != SolanaCluster.None && Services.TryGet<ITransactionSigner>(out _);
                if (!CityWire.IsSafeId(sku) || !payments)
                {
                    card.Add(CityUi.Small(CityLifeStrings.T(payments ? "pass.unavailable" : "pay.unavailable")));
                    return;
                }
                card.Add(CityUi.Small(CityLifeStrings.T("pass.priceFromIntent")));
                var f = _buyPass;
                switch (f.Stage)
                {
                    case IntentStage.Idle:
                    case IntentStage.Failed:
                    case IntentStage.Expired:
                        if (f.Stage == IntentStage.Expired) card.Add(CityUi.Error(CityLifeStrings.T("pay.expired")));
                        if (f.Error != null) card.Add(CityUi.Error(f.Error.Message));
                        card.Add(CityUi.Btn(CityLifeStrings.F("pass.buy", "USDC"), () => CreatePassIntent(sku)));
                        break;
                    case IntentStage.Creating: card.Add(CityUi.Small(CityLifeStrings.T("pay.creating"))); break;
                    case IntentStage.Review:
                        card.Add(CityUi.Text(CityLifeStrings.F("pass.review", FormatUsdc(f.Intent.Amount), f.Intent.Currency ?? "USDC")));
                        if (f.Error != null) card.Add(CityUi.Error(f.Error.Message));
                        if (string.IsNullOrEmpty(f.Intent.Tx))
                        {
                            card.Add(CityUi.Small(CityLifeStrings.T("pay.noTx")));
                            if (!string.IsNullOrEmpty(f.Intent.SolanaPayUrl)) card.Add(CityUi.Btn(CityLifeStrings.T("pay.copyLink"), () => CityUi.Copy(f.Intent.SolanaPayUrl), "ct-btn"));
                            break;
                        }
                        if (cluster == SolanaCluster.Mainnet)
                        {
                            var ack = new Toggle(CityLifeStrings.T("pay.mainnetAck")) { value = _mainnetAck };
                            ack.RegisterValueChangedCallback(e => { _mainnetAck = e.newValue; Render(); });
                            card.Add(ack);
                        }
                        var sign = CityUi.Btn(CityLifeStrings.T("pass.sign"), SignPass);
                        sign.SetEnabled(f.CanSign(Hub.NowMs) && (cluster != SolanaCluster.Mainnet || _mainnetAck));
                        card.Add(sign);
                        card.Add(CityUi.Btn(CityLifeStrings.T("common.cancel"), f.Reset, "ct-btn"));
                        break;
                    case IntentStage.Signing: card.Add(CityUi.Small(CityLifeStrings.T("pay.signing"))); break;
                    case IntentStage.Confirming: card.Add(CityUi.Small(CityLifeStrings.T("pay.confirming"))); break;
                    case IntentStage.Pending: card.Add(CityUi.Text(CityLifeStrings.T("pay.pending"))); break;
                    case IntentStage.Confirmed:
                        card.Add(CityUi.Text(CityLifeStrings.T("pay.confirmed")));
                        card.Add(CityUi.Btn(CityLifeStrings.T("common.close"), () => { f.Reset(); LoadPass(); }, "ct-btn"));
                        break;
                }
            }

            /// <summary>USDC base units (6 decimals, contract) → "5.00". Display only.</summary>
            private static string FormatUsdc(string baseUnits)
            {
                if (!decimal.TryParse(baseUnits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var u)) return baseUnits ?? "?";
                return (u / 1_000_000m).ToString("0.00####", System.Globalization.CultureInfo.InvariantCulture);
            }

            private void CreatePassIntent(string sku)
            {
                var body = CityLifeApi.IntentBody("season_pass", sku, "USDC");
                var key = _buyPass.BeginCreate(body.ToString(Newtonsoft.Json.Formatting.None));
                if (key == null) return;
                _mainnetAck = false;
                Run(ct => Hub.Api.CreateIntent(body, key, ct), i => _buyPass.Created(i), e => _buyPass.CreateFailed(e), true);
            }

            private async void SignPass()
            {
                if (!_buyPass.BeginSign(Hub.NowMs)) return;
                var signer = Services.Get<ITransactionSigner>();
                try
                {
                    var sig = await signer.SignAndSendBase64(_buyPass.Intent.Tx, Life);
                    if (!Alive) return;
                    _buyPass.Signed(sig, Hub.NowMs);
                }
                catch (OperationCanceledException) when (Life.IsCancellationRequested) { return; }
                catch (Exception e)
                {
                    if (!Alive) return;
                    _buyPass.SignFailed(CityErrors.From(e, true), Hub.NowMs);
                }
                await Poll();
            }

            private async Task Poll()
            {
                while (Alive && _buyPass.Stage == IntentStage.Confirming)
                {
                    string status = "pending";
                    try { status = await Hub.Api.IntentStatus(_buyPass.Intent.IntentId, Life); }
                    catch (OperationCanceledException) { return; }
                    catch (Exception e) { Log.Warn("citylife", "Intent poll: " + CityErrors.From(e, false).Code); }
                    if (!Alive || !_buyPass.Polled(status, Hub.NowMs)) break;
                    try { await Awaitable.WaitForSecondsAsync(1f, Life); } catch (OperationCanceledException) { return; }
                }
                if (Alive && _buyPass.Stage == IntentStage.Confirmed) LoadPass();
            }

            private void OnPaymentConfirmed(string intentId)
            {
                if (Alive && _buyPass.ConfirmedByRoom(intentId)) LoadPass();
            }

            public override void Dispose()
            {
                if (Disposed) return;
                _equip.Changed -= Render;
                _buyPass.Changed -= Render;
                Hub.PaymentConfirmed -= OnPaymentConfirmed;
                HidePreview();
                base.Dispose();
            }
        }
    }
}
