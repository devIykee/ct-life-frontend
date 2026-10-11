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

            public VcRowView(VenturesHub hub) : base(hub)
            {
                Root.Add(VUi.H1(VStrings.T("vc.title")));
                Root.Add(VUi.Small(VStrings.T("vc.hint")));
                Root.Add(_body);
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
                _body.Add(VUi.Row(VUi.Chip(VStrings.T("vc.tab.offers"), () => { _tab = Tab.Offers; LoadOffers(); Render(); }, _tab == Tab.Offers),
                    VUi.Chip(VStrings.T("vc.tab.deals"), () => { _tab = Tab.Deals; LoadDeals(); Render(); }, _tab == Tab.Deals)));
                if (!string.IsNullOrEmpty(_note)) _body.Add(VUi.Text(_note));
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
                if (_projectCount == 0) { _body.Add(VUi.Text(VStrings.T("vc.noProjects"))); return; }
                if (_offers.Count == 0) _body.Add(VUi.Text(VStrings.T("vc.offers.empty")));
                foreach (var row in _offers) _body.Add(OfferCard(row));
            }

            private VisualElement OfferCard(OfferRow row)
            {
                var o = row.Offer;
                var card = VUi.Card(VUi.H2(VStrings.F("vc.offer.line", VStrings.VcName(o.VcArchetypeId), Money.Cash(o.InvestCents), ProjectRules.EquityText(o.EquityBps))));
                card.Add(VUi.Small(VStrings.F("vc.offer.project", row.Project.Name)));
                var implied = ProjectRules.ImpliedValuationCents(o.InvestCents, o.EquityBps);
                if (implied.HasValue) card.Add(VUi.Small(VStrings.F("vc.offer.implied", Money.Cash(implied.Value, true))));
                if (o.UnlockAtMs > 0) card.Add(VUi.Small(VStrings.F("vc.offer.unlock", VUi.When(o.UnlockAtMs))));
                bool actionable = o.IsActionable(Hub.NowMs);
                if (o.Status == "pending" && !actionable) card.Add(VUi.Small(VStrings.T("vc.expired")));
                else if (o.Status != "pending") card.Add(VUi.Text(VStrings.F("vc.status", VStrings.Has("vc.status." + o.Status) ? VStrings.T("vc.status." + o.Status) : VWire.Readable(o.Status)), "ct-badge"));
                if (actionable)
                {
                    card.Add(VUi.Small(VStrings.F("vc.offer.expires", VUi.Until(o.ExpiresAtMs, Hub.NowMs))));
                    card.Add(VUi.Small(VStrings.T("vc.offer.risk")));
                    var accept = Flow("accept:" + o.OfferId);
                    var decline = Flow("decline:" + o.OfferId);
                    foreach (var f in new[] { accept, decline }) if (f.Error != null) card.Add(VUi.Error(f.Error.Message));
                    bool sending = accept.Stage == WriteStage.Sending || decline.Stage == WriteStage.Sending;
                    var a = VUi.Btn(VStrings.T("vc.accept"), () => Answer(o, true));
                    var d = VUi.Btn(VStrings.T("vc.decline"), () => Answer(o, false), "ct-btn");
                    a.SetEnabled(!sending);
                    d.SetEnabled(!sending);
                    card.Add(VUi.Row(a, d));
                }
                return card;
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
                if (_deals.Items.Count == 0) _body.Add(VUi.Text(VStrings.T("vc.deals.empty")));
                foreach (var d in _deals.Items)
                {
                    var status = VStrings.Has("vc.deal.status." + d.Status) ? VStrings.T("vc.deal.status." + d.Status) : VWire.Readable(d.Status);
                    var card = VUi.Card(VUi.H2(d.ProjectName),
                        VUi.Text(VStrings.F("vc.deal.line", VStrings.VcName(d.VcArchetypeId), Money.Cash(d.InvestCents), ProjectRules.EquityText(d.EquityBps))),
                        d.Status == "dumped" ? VUi.Error(status) : VUi.Small(status));
                    if (d.AtMs > 0) card.Add(VUi.Small(VUi.When(d.AtMs)));
                    _body.Add(card);
                }
                if (_deals.Skipped > 0) _body.Add(VUi.Small(VStrings.F("common.skipped", _deals.Skipped)));
                if (!string.IsNullOrEmpty(_deals.NextCursor))
                    _body.Add(VUi.Btn(VStrings.T("common.more"), () =>
                        Run(ct => Hub.Projects.VcDeals(_deals.NextCursor, ct), more =>
                        {
                            _deals.Items.AddRange(more.Items);
                            _deals.NextCursor = more.NextCursor;
                            _deals.Skipped += more.Skipped;
                            Render();
                        }, e => { _note = e.Message; Render(); }, false), "ct-btn"));
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
                Hub.VcOffered -= OnVcOffered;
                Hub.VcDumped -= OnVcDumped;
                foreach (var f in _flows.Values) f.Changed -= Render;
                base.Dispose();
            }
        }
    }
}
