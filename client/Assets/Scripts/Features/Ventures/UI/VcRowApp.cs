using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine.UIElements;

namespace CTLife.Ventures
{
    /// <summary>
    /// The VC Row phone app (id `vc`, Phase 5; spec feature #37). Incoming VC term sheets for the projects you founded, with accept
    /// and decline, and the deals board. "Buys player projects cheap, dumps at unlock": the amounts, equity, unlock times and the
    /// dump itself are all the server's. Nothing is fabricated when the routes are missing.
    /// </summary>
    public sealed class VcRowApp : VenturesApp
    {
        public override string Id => "vc";
        public override string Title => VStrings.T("app.vc");
        public override string Color => "#8854d0";
        public override int Phase => 5;

        internal override VView CreateView(VenturesHub hub) => new VcRowView(hub);

        private enum Tab { Offers, Deals }

        private sealed class OfferRow
        {
            public ProjectSummary Project;
            public VcOffer Offer;
        }

        private sealed class VcRowView : VView
        {
            private readonly VisualElement _body = El.Div();
            private readonly Dictionary<string, WriteFlow> _flows = new Dictionary<string, WriteFlow>(StringComparer.Ordinal);
            private Tab _tab = Tab.Offers;
            private List<OfferRow> _offers;
            private int _projectCount = -1;
            private VError _offersError, _dealsError;
            private bool _offersLoading;
            private Page<VcDeal> _deals;
            private string _note = "";

            public VcRowView(VenturesHub hub) : base(hub, VStrings.T("vc.title"))
            {
                Page.name = "vc-page";
                Page.AddToClassList("duo-vc");
                var seg = new DuoSegmented(new[] { VStrings.T("vc.tab.offers"), VStrings.T("vc.tab.deals") }, 0, i =>
                {
                    _tab = i == 0 ? Tab.Offers : Tab.Deals;
                    if (_tab == Tab.Offers) LoadOffers(); else LoadDeals();
                    Render();
                });
                seg.name = "vc-tabs";
                Body.Add(El.Div("duo-comp-tabs", seg));
                Body.Add(VUi.Small(VStrings.T("vc.hint")));
                Body.Add(_body);
                Hub.VcOffered += OnVcOffered;
                Hub.VcDumped += OnVcDumped;
                LoadOffers();
                Render();
            }

            private WriteFlow Flow(string key)
            {
                if (!_flows.TryGetValue(key, out var f)) { _flows[key] = f = new WriteFlow(); f.Changed += Render; }
                return f;
            }

            private void OnVcOffered(VcOfferSignal s) { if (Alive) LoadOffers(); }

            private void OnVcDumped(VcDumpSignal s) { if (Alive) { LoadDeals(); LoadOffers(); } }

            private void Render()
            {
                if (!Alive) return;
                _body.Clear();
                if (!string.IsNullOrEmpty(_note)) _body.Add(Duo.Notice(_note, Duo.Tone.Info));
                if (_tab == Tab.Offers) RenderOffers(); else RenderDeals();
            }

            // ---------------------------------------------------------------- term sheets

            /// <summary>Projects I founded (VC offers are founder-only), then each one's term sheets.</summary>
            private void LoadOffers()
            {
                if (_offersLoading) return;
                _offersLoading = true;
                Run(async ct =>
                {
                    var mine = await Hub.Projects.List("mine", null, ct);
                    var founded = mine.Items.Where(p => string.Equals(p.OwnerUserId, Hub.UserId, StringComparison.OrdinalIgnoreCase) && !ProjectRules.IsDead(p.Status)).Take(5).ToList();
                    var rows = new List<OfferRow>();
                    foreach (var p in founded)
                        foreach (var o in await Hub.Projects.VcOffers(p.ProjectId, ct)) rows.Add(new OfferRow { Project = p, Offer = o });
                    return (founded.Count, rows);
                }, r =>
                {
                    _offersLoading = false;
                    _projectCount = r.Item1;
                    _offers = r.Item2.OrderByDescending(x => x.Offer.IsActionable(Hub.NowMs)).ThenBy(x => x.Offer.ExpiresAtMs).ToList();
                    _offersError = null;
                    Render();
                }, e => { _offersLoading = false; _offersError = e; Render(); }, false);
            }

            private void RenderOffers()
            {
                if (_offersError != null) { _body.Add(ErrorCard(_offersError, "vc.loadFailed", LoadOffers)); return; }
                if (_offers == null) { _body.Add(VUi.Small(VStrings.T("common.loading"))); return; }
                if (_projectCount == 0) { _body.Add(Duo.Empty(VStrings.T("vc.noProjects"))); return; }
                if (_offers.Count == 0) _body.Add(Duo.Empty(VStrings.T("vc.offers.empty")));
                var list = El.Div("duo-list");
                foreach (var row in _offers) list.Add(OfferRowView(row));
                Duo.MarkLast(list);
                _body.Add(list);
            }

            private VisualElement OfferRowView(OfferRow row)
            {
                var o = row.Offer;
                var lines = new List<string> { VStrings.F("vc.offer.project", row.Project.Name) };
                var implied = ProjectRules.ImpliedValuationCents(o.InvestCents, o.EquityBps);
                if (implied.HasValue) lines.Add(VStrings.F("vc.offer.implied", Money.Cash(implied.Value, true)));
                bool actionable = o.IsActionable(Hub.NowMs);
                var r = new DuoRow(() => OfferSheet(row)).Leading(Duo.Avatar(VStrings.VcName(o.VcArchetypeId), o.VcArchetypeId, "duo-avatar-m"))
                    .Title(VStrings.F("vc.offer.short", Money.Cash(o.InvestCents), ProjectRules.EquityText(o.EquityBps)), "duo-row-title-strong")
                    .Subtitle(VStrings.VcName(o.VcArchetypeId) + " · " + string.Join(" · ", lines), twoLines: true);
                r.name = "vc-offer-" + o.OfferId;
                if (actionable) r.TextColumn.Add(El.Text(VStrings.F("vc.offer.expires", VUi.Until(o.ExpiresAtMs, Hub.NowMs)), "duo-row-subtitle duo-comp-status"));
                if (actionable)
                {
                    var review = Duo.Pill(VStrings.T("vc.review"), () => OfferSheet(row), Duo.PillKind.Primary);
                    review.name = "vc-review-" + o.OfferId;
                    r.Trailing(review);
                }
                else if (o.Status == "pending") r.Trailing(VUi.Status(VStrings.T("vc.expiredTag"), PhonePresentation.Tone.Neutral));
                else r.Trailing(VUi.Status(VStrings.Has("vc.status." + o.Status) ? VStrings.T("vc.status." + o.Status) : VWire.Readable(o.Status),
                    o.Status == "accepted" ? PhonePresentation.Tone.Success : PhonePresentation.Tone.Neutral));
                return r;
            }

            /// <summary>The term sheet: amounts, implied valuation, unlock, the risk line, Accept / Decline.</summary>
            private void OfferSheet(OfferRow row)
            {
                var o = row.Offer;
                var sheet = new DuoSheet(VStrings.F("vc.offer.line", VStrings.VcName(o.VcArchetypeId), Money.Cash(o.InvestCents), ProjectRules.EquityText(o.EquityBps)),
                    VStrings.F("vc.offer.project", row.Project.Name));
                sheet.name = "vc-offer-sheet";
                sheet.AddContent(VUi.Pair(VStrings.T("vc.investRow"), Money.Cash(o.InvestCents)));
                sheet.AddContent(VUi.Pair(VStrings.T("vc.equityRow"), ProjectRules.EquityText(o.EquityBps)));
                var implied = ProjectRules.ImpliedValuationCents(o.InvestCents, o.EquityBps);
                if (implied.HasValue) sheet.AddContent(VUi.Pair(VStrings.T("vc.impliedRow"), Money.Cash(implied.Value, true)));
                if (o.UnlockAtMs > 0) sheet.AddContent(VUi.Pair(VStrings.T("vc.unlockRow"), VUi.When(o.UnlockAtMs)));
                bool actionable = o.IsActionable(Hub.NowMs);
                if (actionable)
                {
                    sheet.AddContent(VUi.Small(VStrings.F("vc.offer.expires", VUi.Until(o.ExpiresAtMs, Hub.NowMs))));
                    sheet.AddContent(Duo.Notice(VStrings.T("vc.offer.risk"), Duo.Tone.Warning));
                    var accept = Flow("accept:" + o.OfferId);
                    var decline = Flow("decline:" + o.OfferId);
                    foreach (var f in new[] { accept, decline }) if (f.Error != null) sheet.AddContent(Duo.Notice(f.Error.Message, Duo.Tone.Danger));
                    bool sending = accept.Stage == WriteStage.Sending || decline.Stage == WriteStage.Sending;
                    if (!sending)
                    {
                        sheet.AddPrimary(VStrings.T("vc.accept"), () => Answer(o, true), name: "vc-accept");
                        sheet.AddPrimary(VStrings.T("vc.decline"), () => Answer(o, false), destructive: true, name: "vc-decline");
                    }
                }
                sheet.AddSecondary(VStrings.T("common.close"));
                ShowSheet(sheet);
            }

            private void Answer(VcOffer o, bool accept)
            {
                var f = Flow((accept ? "accept:" : "decline:") + o.OfferId);
                if (f.Stage == WriteStage.Done || f.Stage == WriteStage.Rejected) f.Reset();
                var key = f.Begin("POST", "/vc-offers/" + o.OfferId + (accept ? "/accept" : "/decline"), "");
                if (key == null) return;
                Run(ct => accept ? Hub.Projects.AcceptVc(o.OfferId, key, ct) : Hub.Projects.DeclineVc(o.OfferId, key, ct), r =>
                {
                    f.Succeeded();
                    _note = accept ? VStrings.T("vc.accepted") : VStrings.T("vc.declined");
                    if (accept) Bus.Publish(new SfxSignal("sfx_ui_success"));
                    LoadOffers();
                    LoadDeals();
                }, e => { f.Failed(e); if (e.OutcomeUnknown || e.Is("CONFLICT")) LoadOffers(); }, true);
            }

            // ---------------------------------------------------------------- deals board

            private void LoadDeals()
            {
                _dealsError = null;
                Run(ct => Hub.Projects.VcDeals(null, ct), p => { _deals = p; Render(); }, e => { _dealsError = e; Render(); }, false);
            }

            private void RenderDeals()
            {
                if (_dealsError != null) { _body.Add(ErrorCard(_dealsError, "vc.deals.loadFailed", LoadDeals)); return; }
                if (_deals == null) { LoadDeals(); _body.Add(VUi.Small(VStrings.T("common.loading"))); return; }
                if (_deals.Items.Count == 0) _body.Add(Duo.Empty(VStrings.T("vc.deals.empty")));
                var list = El.Div("duo-list");
                foreach (var d in _deals.Items)
                {
                    var status = VStrings.Has("vc.deal.status." + d.Status) ? VStrings.T("vc.deal.status." + d.Status) : VWire.Readable(d.Status);
                    var r = new DuoRow().Leading(Duo.Avatar(VStrings.VcName(d.VcArchetypeId), d.VcArchetypeId, "duo-avatar-m")).Title(d.ProjectName, "duo-row-title-strong")
                        .Subtitle(VStrings.F("vc.deal.line", VStrings.VcName(d.VcArchetypeId), Money.Cash(d.InvestCents), ProjectRules.EquityText(d.EquityBps))
                            + (d.AtMs > 0 ? "\n" + VUi.When(d.AtMs) : ""), twoLines: true);
                    r.AddToClassList("duo-row-static");
                    r.Trailing(VUi.Status(status, d.Status == "dumped" ? PhonePresentation.Tone.Danger : PhonePresentation.Tone.Neutral));
                    list.Add(r);
                }
                Duo.MarkLast(list);
                _body.Add(list);
                if (_deals.Skipped > 0) _body.Add(VUi.Small(VStrings.F("common.skipped", _deals.Skipped)));
                if (!string.IsNullOrEmpty(_deals.NextCursor))
                    _body.Add(VUi.Btn(VStrings.T("common.more"), () =>
                        Run(ct => Hub.Projects.VcDeals(_deals.NextCursor, ct), more =>
                        {
                            _deals.Items.AddRange(more.Items);
                            _deals.NextCursor = more.NextCursor;
                            _deals.Skipped += more.Skipped;
                            Render();
                        }, e => { _note = e.Message; Render(); }, false), VUi.Plain));
            }

            private VisualElement ErrorCard(VError e, string headKey, Action retry)
            {
                if (e.IsUnavailable) return Duo.Empty(VStrings.T("common.notAvailable"));
                var card = VUi.Card(Duo.Notice(VStrings.T(headKey) + " " + e.Message, Duo.Tone.Danger));
                card.Add(VUi.Row(VUi.Btn(VStrings.T("common.retry"), retry, VUi.Plain)));
                return card;
            }

            public override void Dispose()
            {
                if (Disposed) return;
                Hub.VcOffered -= OnVcOffered;
                Hub.VcDumped -= OnVcDumped;
                foreach (var f in _flows.Values) f.Changed -= Render;
                base.Dispose();
            }
        }
    }
}
