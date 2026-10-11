using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.Economy;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Ventures
{
    /// <summary>
    /// The Ad Desk phone app (id `ads`, Phase 5; spec feature #36). Slot browser with availability, booking with a validated creative
    /// upload, payment through the Money lane's intent flow (MWA, devnet only), "my bookings" with live status, and cancel. Prices,
    /// windows, sizes and statuses come from the server; nothing is computed or invented here.
    /// </summary>
    public sealed class AdDeskApp : VenturesApp
    {
        public override string Id => "ads";
        public override string Title => VStrings.T("app.ads");
        public override string Color => "#f7b731";
        public override int Phase => 5;

        internal override VView CreateView(VenturesHub hub) => new AdDeskView(hub);

        private enum Tab { Slots, Mine }

        private sealed class AdDeskView : VView
        {
            private readonly VisualElement _body = El.Div();
            private readonly AdBookingFlow _flow;
            private readonly Dictionary<string, WriteFlow> _cancels = new Dictionary<string, WriteFlow>(StringComparer.Ordinal);
            private readonly Func<string, int> _decimals;
            private Tab _tab = Tab.Slots;
            private AdSlotList _slots;
            private VError _slotsError;
            private bool _slotsLoading, _availLoading, _availUnavailable, _dirty;
            private string _kindFilter = "";
            private string _path = "";
            private string _creativeError = "";
            private Texture2D _preview;
            private bool _previewSkipped;
            private bool _paying;
            private string _note = "";

            public AdDeskView(VenturesHub hub) : base(hub)
            {
                _flow = new AdBookingFlow(hub.Ads);
                TopUpCatalog catalog = null;
                try { if (Services.TryGet<ConfigStore>(out var cfg)) catalog = TopUpCatalog.Parse(cfg.Config("economy")); }
                catch (Exception e) { Log.Warn("ventures", "economy config unreadable: " + e.Message); }
                _decimals = code => catalog != null ? catalog.Decimals(code, -1) : (code == "USDC" ? TokenUnits.UsdcDecimals : -1);
                Root.Add(VUi.H1(VStrings.T("ads.title")));
                Root.Add(VUi.Small(VStrings.T("ads.hint")));
                Root.Add(_body);
                _flow.Changed += RequestRender;
                Hub.Changed += RequestRender;
                Hub.Payment.Changed += RequestRender;
                Hub.AdsChanged += OnAdsChanged;
                LoadSlots();
                _ = Hub.RefreshBoard();
                Render();
            }

            // ---------------------------------------------------------------- loading

            private void LoadSlots()
            {
                if (_slotsLoading) return;
                _slotsLoading = true;
                Run(ct => Hub.Ads.Slots(ct), s => { _slots = s; _slotsError = null; _slotsLoading = false; RequestRender(); },
                    e => { _slotsError = e; _slotsLoading = false; RequestRender(); }, false);
            }

            private void OnAdsChanged(AdsChangedSignal s)
            {
                if (!Alive) return;
                // Someone booked or an ad went live: windows and "free from" dates moved.
                if (_flow.SlotId == null) LoadSlots();
                else if (s.slotIds.Contains(_flow.SlotId)) LoadAvailability(_flow.SlotId);
            }

            private void LoadAvailability(string slotId)
            {
                _availLoading = true;
                Run(ct => Hub.Ads.Availability(slotId, ct), a =>
                {
                    _availLoading = false;
                    _availUnavailable = false;
                    _flow.RefreshAvailability(a);
                    if (_flow.CreativeBytes != null) _flow.SetCreative(_flow.CreativeBytes, _flow.Creative?.FileName, Hub.Limits);
                    RequestRender();
                }, e =>
                {
                    _availLoading = false;
                    _availUnavailable = e.IsUnavailable;
                    RequestRender();
                }, false);
            }

            // ---------------------------------------------------------------- render plumbing

            private bool TextFocused()
            {
                var f = Root.panel?.focusController?.focusedElement as VisualElement;
                return f != null && Root.Contains(f) && f.GetFirstAncestorOfType<TextField>() != null || f is TextField;
            }

            private void RequestRender()
            {
                if (!Alive) return;
                if (TextFocused()) { _dirty = true; return; }
                Render();
            }

            protected override void Tick()
            {
                if (_dirty && !TextFocused()) Render();
                else if (Hub.Payment.Stage == TopUpStage.Review && !TextFocused()) Render();
            }

            private void Render()
            {
                if (!Alive) return;
                _dirty = false;
                _body.Clear();
                if (_flow.SlotId != null) { RenderBooking(); return; }
                _body.Add(PhonePresentation.Segment(VUi.Chip(VStrings.T("ads.tab.slots"), () => { _tab = Tab.Slots; Render(); }, _tab == Tab.Slots),
                    VUi.Chip(VStrings.T("ads.tab.mine"), () => { _tab = Tab.Mine; _ = Hub.RefreshBoard(); Render(); }, _tab == Tab.Mine)));
                if (!string.IsNullOrEmpty(_note)) _body.Add(VUi.Text(_note));
                if (_tab == Tab.Slots) RenderSlots(); else RenderMine();
            }

            // ---------------------------------------------------------------- slots

            private void RenderSlots()
            {
                _body.Add(VUi.Small(VStrings.T("ads.rules")));
                if (_slots == null)
                {
                    if (_slotsError != null) _body.Add(ErrorCard(_slotsError, "ads.slots.loadFailed", LoadSlots));
                    else _body.Add(VUi.Small(VStrings.T("common.loading")));
                    return;
                }
                var kinds = _slots.Items.Select(s => s.Kind).Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();
                if (kinds.Count > 1)
                {
                    var chips = VUi.Row(VUi.Chip(VStrings.T("ads.filter.all"), () => { _kindFilter = ""; Render(); }, _kindFilter == ""));
                    foreach (var k in kinds) { var kk = k; chips.Add(VUi.Chip(VStrings.AdKind(k), () => { _kindFilter = kk; Render(); }, _kindFilter == k)); }
                    _body.Add(chips);
                }
                var shown = _slots.Items.Where(s => _kindFilter == "" || s.Kind == _kindFilter).ToList();
                if (shown.Count == 0) _body.Add(VUi.Text(VStrings.T("ads.slots.empty")));
                foreach (var s in shown) _body.Add(SlotCard(s));
                if (_slots.Skipped > 0) _body.Add(VUi.Small(VStrings.F("common.skipped", _slots.Skipped)));
            }

            private VisualElement SlotCard(AdSlot s)
            {
                var card = VUi.Card(VUi.Text(VStrings.AdKind(s.Kind), "ct-duo-card-title"), VUi.Text(s.SlotId, "ct-duo-card-kicker"));
                if (s.Terms.Count == 0) card.Add(VUi.Small(VStrings.T("ads.slot.noTerms")));
                foreach (var t in s.Terms) card.Add(VUi.Text(TermLine(t)));
                if (s.NextFreeAtMs.HasValue)
                    card.Add(PhonePresentation.Status(s.NextFreeAtMs.Value > Hub.NowMs ? VStrings.F("ads.slot.nextFree", VUi.When(s.NextFreeAtMs.Value)) : VStrings.T("ads.slot.free"),
                        s.NextFreeAtMs.Value > Hub.NowMs ? PhonePresentation.Tone.Neutral : PhonePresentation.Tone.Success));
                var book = VUi.Btn(VStrings.T("ads.slot.book"), () => BeginBook(s));
                book.SetEnabled(s.Terms.Count > 0);
                card.Add(PhonePresentation.Footer(book));
                return card;
            }

            private string TermLine(AdTerm t)
            {
                var price = t.PriceUsdcUnits != null ? AdMoney.Usdc(t.PriceUsdcUnits) : null;
                if (t.Days > 0 && price != null) return VStrings.F("ads.slot.term", t.Days, price);
                if (price != null) return VStrings.F("ads.slot.termNoDays", VWire.Readable(t.TermId), price);
                return VStrings.F("ads.slot.termNoPrice", VWire.Readable(t.TermId));
            }

            private void BeginBook(AdSlot s)
            {
                _note = "";
                ResetCreativeUi();
                _flow.Reset();
                _availUnavailable = false;
                _flow.SelectSlot(AdAvailability.FromSlot(s));
                LoadAvailability(s.SlotId);
            }

            // ---------------------------------------------------------------- booking sheet

            private void RenderBooking()
            {
                var a = _flow.Availability;
                bool busy = _flow.Busy;
                var back = VUi.Btn(VStrings.T("common.back"), LeaveBooking, "ct-btn");
                back.SetEnabled(!busy && Hub.Payment.Stage != TopUpStage.Signing);
                _body.Add(back);
                _body.Add(VUi.H2(VStrings.F("book.title", VStrings.AdKind(a?.Kind))));
                _body.Add(VUi.Small(_flow.SlotId));
                if (_flow.Stage == BookStage.Booked) { RenderPaymentStep(); return; }
                if (_availLoading) _body.Add(VUi.Small(VStrings.T("book.availability.loading")));
                else if (_availUnavailable) _body.Add(VUi.Small(VStrings.T("book.availability.unavailable")));
                if (a != null && a.CreativeWidth > 0 && a.CreativeHeight > 0) _body.Add(VUi.Text(VStrings.F("book.creativeSize", a.CreativeWidth, a.CreativeHeight)));
                if (a != null && a.Booked.Count > 0)
                {
                    _body.Add(VUi.Small(VStrings.T("book.booked")));
                    foreach (var w in a.Booked) _body.Add(VUi.Small(VStrings.F("book.booked.row", VUi.Day(w.StartsAtMs), VUi.Day(w.EndsAtMs))));
                }
                _body.Add(VUi.Small(VStrings.T("book.terms")));
                var terms = VUi.Row();
                if (a != null)
                    foreach (var t in a.Terms)
                    {
                        var tt = t;
                        terms.Add(VUi.Chip(TermLine(t), () => { if (!busy) { _flow.TermId = tt.TermId; Render(); } }, _flow.TermId == t.TermId));
                    }
                _body.Add(terms);
                var term = _flow.Term();
                if (term != null && term.Days > 0)
                {
                    _body.Add(VUi.Small(VStrings.T("book.start")));
                    var f = VUi.Field("", _flow.StartText, v => _flow.StartText = v, 10);
                    f.SetEnabled(!busy);
                    _body.Add(f);
                }
                else if (term != null) _body.Add(VUi.Small(VStrings.T("book.startNotForTerm")));
                _body.Add(VUi.Small(VStrings.T("book.link")));
                var link = VUi.Field("", _flow.Link, v => _flow.Link = v, 2048);
                link.SetEnabled(!busy);
                _body.Add(link);
                RenderCreativePicker(busy);
                RenderProblemsAndSubmit(busy);
            }

            private void RenderCreativePicker(bool busy)
            {
                _body.Add(VUi.Small(VStrings.F("book.creative", VStrings.Bytes(Hub.Limits.MaxBytes))));
                if (_flow.Creative != null && _flow.Creative.Ok)
                {
                    var c = _flow.Creative;
                    _body.Add(VUi.Text(VStrings.F("book.creative.ok", c.FileName, c.Width, c.Height, VStrings.Bytes(c.Bytes))));
                    if (c.AspectWarning) _body.Add(VUi.Small(VStrings.F("book.creative.aspect", c.Width, c.Height, c.SlotWidth, c.SlotHeight)));
                    if (_preview != null) _body.Add(PreviewElement(c));
                    else if (_previewSkipped) _body.Add(VUi.Small(VStrings.T("book.creative.previewSkipped")));
                    var rm = VUi.Btn(VStrings.T("book.creative.clear"), () => { ResetCreativeUi(); _flow.ClearCreative(); }, "ct-btn");
                    rm.SetEnabled(!busy);
                    _body.Add(rm);
                    return;
                }
                if (!string.IsNullOrEmpty(_creativeError)) _body.Add(VUi.Error(_creativeError));
                if (Services.TryGet<ICreativePicker>(out var picker))
                {
                    var pick = VUi.Btn(VStrings.T("book.creative.pick"), () => PickFromGallery(picker));
                    pick.SetEnabled(!busy);
                    _body.Add(pick);
                }
                _body.Add(VUi.Small(VStrings.T("book.creative.path")));
                var path = VUi.Field("", _path, v => _path = v, 300);
                path.SetEnabled(!busy);
                _body.Add(path);
                var load = VUi.Btn(VStrings.T("book.creative.load"), LoadFromPath, "ct-btn");
                load.SetEnabled(!busy);
                _body.Add(load);
            }

            private VisualElement PreviewElement(CreativeResult c)
            {
                var img = El.Div("ct-card");
                float w = Mathf.Min(220f, c.Width);
                img.style.width = w;
                img.style.height = w * c.Height / Mathf.Max(1, c.Width);
                img.style.backgroundImage = new StyleBackground(_preview);
                return img;
            }

            private void ResetCreativeUi()
            {
                _creativeError = "";
                _previewSkipped = false;
                if (_preview != null) { UnityEngine.Object.Destroy(_preview); _preview = null; }
            }

            private void LoadFromPath()
            {
                _creativeError = "";
                var roots = new List<string> { Application.persistentDataPath, Application.temporaryCachePath };
                if (!Application.isEditor && !CreativeFile.IsUnderAny(_path, roots)) { _creativeError = VStrings.T("book.creative.outsideRoot"); Render(); return; }
                var bytes = CreativeFile.ReadBounded(_path, Hub.Limits.MaxBytes, out var problem);
                if (bytes == null) { _creativeError = VStrings.CreativeProblemText(problem, Hub.Limits.MaxBytes); Render(); return; }
                AcceptCreative(bytes, System.IO.Path.GetFileName(_path));
            }

            private async void PickFromGallery(ICreativePicker picker)
            {
                try
                {
                    var picked = await picker.Pick(Life);
                    if (!Alive || picked?.Bytes == null) return;
                    AcceptCreative(picked.Bytes, picked.Name);
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    Log.Warn("ventures", "Picker failed: " + e.GetType().Name);
                    if (Alive) { _creativeError = VStrings.T("err.generic"); Render(); }
                }
            }

            private void AcceptCreative(byte[] bytes, string name)
            {
                ResetCreativeUi();
                var r = _flow.SetCreative(bytes, name, Hub.Limits);
                if (!r.Ok) { _creativeError = VStrings.CreativeProblemText(r.Problem, Hub.Limits.MaxBytes); Render(); return; }
                // A small image gets a preview. Large ones skip it: LoadImage would allocate the full decoded size.
                if ((long)r.Width * r.Height <= 1_048_576)
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "ad_creative_preview" };
                    if (tex.LoadImage(bytes, true) && tex.width == r.Width && tex.height == r.Height) _preview = tex;
                    else UnityEngine.Object.Destroy(tex);
                }
                else _previewSkipped = true;
                Render();
            }

            private void RenderProblemsAndSubmit(bool busy)
            {
                var problems = _flow.Problems(Hub.NowMs);
                foreach (var p in problems) _body.Add(VUi.Small(VStrings.T("book.problem." + p)));
                if (_flow.Stage == BookStage.Uploading) _body.Add(VUi.Text(VStrings.T("book.uploading")));
                if (_flow.Stage == BookStage.Booking) _body.Add(VUi.Text(VStrings.T("book.booking")));
                if (_flow.Error != null)
                {
                    _body.Add(VUi.Error(_flow.Error.Message));
                }
                var label = _flow.Stage == BookStage.Failed && (_flow.Error?.OutcomeUnknown ?? false) ? VStrings.T("common.retry") : VStrings.T("book.submit");
                var go = VUi.Btn(label, Submit);
                // Never create a booking that cannot be paid right now (another payment still open in the shared flow).
                bool payFree = Hub.Payment.CanStartNew;
                go.SetEnabled(!busy && problems.Count == 0 && PayBlock() == null && payFree);
                _body.Add(go);
                if (PayBlock() != null) _body.Add(VUi.Error(VStrings.T(PayBlock())));
                else if (!payFree) _body.Add(VUi.Error(VStrings.T("pay.otherInFlight")));
                else _body.Add(VUi.Small(VStrings.T("book.next")));
            }

            private static string PayBlock() => PaymentPolicy.BlockedReason(Env.Current != null ? Env.Current.cluster : SolanaCluster.None,
                Services.TryGet<ITransactionSigner>(out _), Features.IsAvailable(Features.Payments));

            private void LeaveBooking()
            {
                if (_flow.Busy) return;
                ResetCreativeUi();
                _flow.Reset();
                _tab = Tab.Mine;
                _ = Hub.RefreshBoard();
                Render();
            }

            private void Submit()
            {
                if (_paying) return;
                Run(ct => _flow.Submit(Hub.NowMs, ct), () =>
                {
                    if (_flow.NeedsAvailabilityRefresh && _flow.SlotId != null)
                    {
                        _flow.AckAvailabilityRefresh();
                        LoadAvailability(_flow.SlotId);
                    }
                    if (_flow.Stage != BookStage.Booked) return;
                    _ = Hub.RefreshBoard();
                    StartPayment();
                }, e => RequestRender(), true);
            }

            // ---------------------------------------------------------------- payment (Money lane's TopUpFlow)

            private void StartPayment()
            {
                if (_flow.Result == null) return;
                if (_flow.Result.Intent == null) return;
                _paying = true;
                Run(ct => AdPayments.Begin(Hub.Payment, Hub.PaymentIntents, _flow.Result, ct), ok =>
                {
                    _paying = false;
                    if (!ok) _note = VStrings.T("pay.otherInFlight");
                    RequestRender();
                }, e => { _paying = false; _note = e.Message; RequestRender(); }, true);
            }

            private void RenderPaymentStep()
            {
                var id = _flow.Result.BookingId;
                _body.Add(VUi.Small(VStrings.T("mine.waitingPay")));
                var panel = PaymentPanel(id);
                if (_flow.Result.Intent == null) _body.Add(VUi.Error(VStrings.T("pay.noIntent")));
                else if (panel != null) _body.Add(panel);
                else if (_paying) _body.Add(VUi.Text(VStrings.T("pay.preparing")));
                else if (!string.IsNullOrEmpty(_note)) _body.Add(VUi.Error(_note));
                _body.Add(CancelRow(id, _flow.Result.Intent?.IntentId));
            }

            /// <summary>The payment card for the booking the shared flow is paying, or null when that flow belongs to another booking.</summary>
            private VisualElement PaymentPanel(string bookingId)
            {
                var p = Hub.Payment;
                if (p.Sku != bookingId || p.Stage == TopUpStage.Choose && p.Intent == null) return null;
                var card = VUi.Card();
                var intent = p.Intent;
                switch (p.Stage)
                {
                    case TopUpStage.Creating:
                        card.Add(VUi.Text(VStrings.T("pay.preparing")));
                        break;
                    case TopUpStage.Review:
                    {
                        card.Add(VUi.Text(VStrings.F("pay.review", AdMoney.Format(intent.Amount, intent.Currency, _decimals))));
                        if (intent.ExpiresAtMs.HasValue) card.Add(VUi.Small(VStrings.F("pay.expiry", VUi.Until(intent.ExpiresAtMs.Value, Hub.NowMs))));
                        if (!string.IsNullOrEmpty(intent.Recipient)) card.Add(VUi.Small(VStrings.T("pay.to") + ": " + VUi.Short(intent.Recipient)));
                        if (p.Error != null) card.Add(VUi.Error(MoneyStrings.Err(p.Error)));
                        var block = PayBlock();
                        if (!intent.HasTx) card.Add(VUi.Error(VStrings.T("pay.noTx")));
                        else if (block != null) card.Add(VUi.Error(VStrings.T(block)));
                        var pay = VUi.Btn(VStrings.T("pay.sign"), Pay);
                        pay.SetEnabled(intent.HasTx && block == null);
                        card.Add(pay);
                        break;
                    }
                    case TopUpStage.Signing: card.Add(VUi.Text(VStrings.T("pay.signing"))); break;
                    case TopUpStage.Confirming:
                        card.Add(VUi.Text(VStrings.T("pay.confirming")));
                        if (p.SignUncertain) card.Add(VUi.Small(VStrings.T("pay.uncertain")));
                        break;
                    case TopUpStage.Confirmed:
                        card.Add(VUi.Text(VStrings.T("pay.confirmed")));
                        card.Add(VUi.Btn(VStrings.T("common.done"), DonePaying, "ct-btn"));
                        break;
                    case TopUpStage.Pending:
                        card.Add(VUi.Text(VStrings.T("pay.pending")));
                        if (p.StatusUnavailable) card.Add(VUi.Small(VStrings.T("pay.statusUnavailable")));
                        card.Add(VUi.Btn(VStrings.T("pay.check"), CheckAgain, "ct-btn"));
                        break;
                    case TopUpStage.Expired:
                        card.Add(VUi.Error(VStrings.T("pay.expired")));
                        card.Add(VUi.Btn(VStrings.T("common.done"), DonePaying, "ct-btn"));
                        break;
                    case TopUpStage.Failed:
                    case TopUpStage.Error:
                        card.Add(VUi.Error(p.Error != null ? MoneyStrings.Err(p.Error) : VStrings.T("pay.failed")));
                        card.Add(VUi.Btn(VStrings.T("common.done"), DonePaying, "ct-btn"));
                        break;
                    default:
                        card.Add(VUi.Text(VStrings.T("pay.preparing")));
                        break;
                }
                return card;
            }

            private async void Pay()
            {
                try { await Hub.Payment.Pay(Life); }
                catch (OperationCanceledException) { }
                catch (Exception e) { Log.Exception("ventures", e); }
                if (Alive) { _ = Hub.RefreshBoard(); RequestRender(); }
            }

            private async void CheckAgain()
            {
                try { await Hub.Payment.CheckAgain(Life); }
                catch (OperationCanceledException) { }
                catch (Exception e) { Log.Exception("ventures", e); }
                if (Alive) { _ = Hub.RefreshBoard(); RequestRender(); }
            }

            private void DonePaying()
            {
                if (Hub.Payment.Stage == TopUpStage.Signing) return;
                Hub.Payment.Reset();
                if (_flow.Stage == BookStage.Booked) { ResetCreativeUi(); _flow.Reset(); }
                _tab = Tab.Mine;
                _ = Hub.RefreshBoard();
                Render();
            }

            // ---------------------------------------------------------------- cancel

            private WriteFlow CancelFlow(string id)
            {
                if (!_cancels.TryGetValue(id, out var f)) { _cancels[id] = f = new WriteFlow(); f.Changed += RequestRender; }
                return f;
            }

            private bool PaymentLocksCancel(string bookingId, string intentId)
            {
                var p = Hub.Payment;
                bool mine = p.Sku == bookingId || intentId != null && p.Intent?.IntentId == intentId;
                return mine && (p.Busy || p.Signature != null || p.SignUncertain || p.Stage == TopUpStage.Pending || p.Stage == TopUpStage.Confirmed);
            }

            private VisualElement CancelRow(string bookingId, string intentId)
            {
                var f = CancelFlow(bookingId);
                var row = VUi.Card();
                if (f.Error != null) row.Add(VUi.Error(f.Error.Message));
                var cancel = VUi.Btn(f.Stage == WriteStage.Unknown ? VStrings.T("common.retry") : VStrings.T("mine.cancel"), () => Cancel(bookingId), "ct-btn ct-btn-danger");
                cancel.SetEnabled(f.Stage != WriteStage.Sending && !PaymentLocksCancel(bookingId, intentId));
                row.Add(cancel);
                return row;
            }

            private void Cancel(string bookingId)
            {
                var f = CancelFlow(bookingId);
                if (f.Stage == WriteStage.Done || f.Stage == WriteStage.Rejected) f.Reset();
                var key = f.Begin("POST", "/ads/bookings/" + bookingId + "/cancel", "");
                if (key == null) return;
                Run(ct => Hub.Ads.Cancel(bookingId, key, ct), b =>
                {
                    f.Succeeded();
                    Hub.Upsert(b);
                    if (Hub.Payment.Sku == bookingId && Hub.Payment.Stage != TopUpStage.Signing) Hub.Payment.Reset();
                    if (_flow.Result?.BookingId == bookingId) { ResetCreativeUi(); _flow.Reset(); _tab = Tab.Mine; }
                    _note = VStrings.T("mine.cancelled");
                    RequestRender();
                }, e =>
                {
                    f.Failed(e);
                    if (e.Is("CONFLICT")) _ = Hub.RefreshBoard(); // it moved on (paid or expired): show the real status
                }, true);
            }

            // ---------------------------------------------------------------- my bookings

            private void RenderMine()
            {
                _body.Add(VUi.H2(VStrings.T("mine.title")));
                var refresh = VUi.Btn(VStrings.T("common.refresh"), () => { _ = Hub.RefreshBoard(); }, "ct-btn");
                _body.Add(refresh);
                if (Hub.BoardError != null)
                {
                    if (Hub.BoardError.IsUnavailable) _body.Add(VUi.Card(VUi.Text(VStrings.T("common.notAvailable"))));
                    else _body.Add(ErrorCard(Hub.BoardError, "mine.loadFailed", () => { _ = Hub.RefreshBoard(); }));
                    if (Hub.Board.Items.Count == 0) return;
                }
                else if (!Hub.BoardLoaded) { _body.Add(VUi.Small(VStrings.T("common.loading"))); return; }
                if (Hub.Board.Items.Count == 0) { _body.Add(VUi.Text(VStrings.T("mine.empty"))); return; }
                var items = Hub.Board.Items.OrderBy(b => AdBoard.Rank(b.Status)).ThenByDescending(b => b.CreatedAtMs).ToList();
                foreach (var b in items) _body.Add(BookingCard(b));
                if (Hub.Board.Skipped > 0) _body.Add(VUi.Small(VStrings.F("common.skipped", Hub.Board.Skipped)));
                if (!string.IsNullOrEmpty(Hub.Board.NextCursor)) _body.Add(VUi.Btn(VStrings.T("common.more"), () => { _ = Hub.LoadMoreBoard(); }, "ct-btn"));
            }

            private VisualElement BookingCard(AdBooking b)
            {
                var card = VUi.Card(PhonePresentation.Status(
                    b.Status == AdStatus.Unknown && !string.IsNullOrEmpty(b.StatusRaw) ? b.StatusRaw : VStrings.AdStatusText(b.Status), BookingTone(b.Status)),
                    VUi.Text(SlotTitle(b), "ct-duo-card-title"));
                if (b.StartsAtMs.HasValue && b.EndsAtMs.HasValue) card.Add(VUi.Text(VStrings.F("mine.runs", VUi.Day(b.StartsAtMs.Value), VUi.Day(b.EndsAtMs.Value))));
                else if (b.Status == AdStatus.PendingReview || b.Status == AdStatus.AwaitingPayment) card.Add(VUi.Small(VStrings.T("mine.datesLater")));
                if (b.Amount != null) card.Add(VUi.Small(VStrings.F("mine.amount", AdMoney.Format(b.Amount, b.Currency, _decimals))));
                if (b.Creative != null && b.Creative.Width > 0) card.Add(VUi.Small(VStrings.F("mine.creative", b.Creative.Width, b.Creative.Height, VStrings.Bytes(b.Creative.SizeBytes))));
                if (!string.IsNullOrEmpty(b.Link)) card.Add(VUi.Small(VStrings.F("mine.link", b.Link)));
                if (b.Status == AdStatus.Rejected || !string.IsNullOrEmpty(b.RejectionReasonId))
                {
                    if (!string.IsNullOrEmpty(b.RejectionReasonId)) card.Add(VUi.Error(VStrings.F("mine.reason", VenturesHub.ReasonText(b.RejectionReasonId))));
                    if (b.Refund == null && b.Status == AdStatus.Rejected) card.Add(VUi.Small(VStrings.T("mine.rejectedNote")));
                }
                if (b.Refund != null)
                {
                    var st = VStrings.Has("mine.refund." + b.Refund.Status) ? VStrings.T("mine.refund." + b.Refund.Status) : VWire.Readable(b.Refund.Status);
                    card.Add(VUi.Text(VStrings.F("mine.refund", st, AdMoney.Format(b.Refund.Amount, b.Refund.Currency, _decimals))));
                }
                if (b.Status == AdStatus.AwaitingPayment)
                {
                    var panel = PaymentPanel(b.BookingId);
                    if (panel == null && b.IntentId != null && Hub.Payment.Intent?.IntentId == b.IntentId)
                    {
                        // Restored after the app was closed: the flow knows the intent but not the booking id.
                        panel = VUi.Card(VUi.Text(VStrings.T(Hub.Payment.Stage == TopUpStage.Pending ? "pay.pending" : "pay.confirming")));
                        if (Hub.Payment.Stage == TopUpStage.Pending) panel.Add(VUi.Btn(VStrings.T("pay.check"), CheckAgain, "ct-btn"));
                    }
                    if (panel != null) card.Add(panel); else card.Add(VUi.Small(VStrings.T("mine.waitingPay")));
                    if (AdStatuses.CanCancel(b.Status)) card.Add(CancelRow(b.BookingId, b.IntentId));
                }
                return card;
            }

            private static PhonePresentation.Tone BookingTone(AdStatus status) => status switch
            {
                AdStatus.Live => PhonePresentation.Tone.Success,
                AdStatus.Refunded => PhonePresentation.Tone.Success,
                AdStatus.PendingReview => PhonePresentation.Tone.Accent,
                AdStatus.Scheduled => PhonePresentation.Tone.Info,
                AdStatus.AwaitingPayment => PhonePresentation.Tone.Warning,
                AdStatus.Rejected => PhonePresentation.Tone.Danger,
                _ => PhonePresentation.Tone.Neutral,
            };

            private string SlotTitle(AdBooking b)
            {
                var slot = _slots?.Items.Find(s => s.SlotId == b.SlotId);
                return (slot != null ? VStrings.AdKind(slot.Kind) + " · " : "") + b.SlotId;
            }

            private VisualElement ErrorCard(VError e, string headKey, Action retry)
            {
                var card = VUi.Card();
                if (e.IsUnavailable) { card.Add(VUi.Text(VStrings.T("common.notAvailable"))); return card; }
                card.Add(VUi.Error(VStrings.T(headKey)));
                card.Add(VUi.Small(e.Message));
                card.Add(VUi.Btn(VStrings.T("common.retry"), retry, "ct-btn"));
                return card;
            }

            public override void Dispose()
            {
                if (Disposed) return;
                _flow.Changed -= RequestRender;
                Hub.Changed -= RequestRender;
                Hub.Payment.Changed -= RequestRender;
                Hub.AdsChanged -= OnAdsChanged;
                foreach (var f in _cancels.Values) f.Changed -= RequestRender;
                ResetCreativeUi();
                base.Dispose();
            }
        }
    }
}
