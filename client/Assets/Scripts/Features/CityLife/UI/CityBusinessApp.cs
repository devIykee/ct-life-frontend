using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.UI;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// The Business app (id `biz`, Phase 4; spec feature #30 "Businesses and bad days"). The catalog comes from contract
    /// config `businesses.json`; buy / collect / sell-back go through <see cref="IBusinessGateway"/>, which targets the
    /// proposal routes and falls back to read-only when the server answers UNAVAILABLE. No money moves client-side.
    /// </summary>
    public sealed class CityBusinessApp : CityApp
    {
        public override string Id => "biz";
        public override string Title => CityLifeStrings.T("app.biz");
        public override string Color => "#0fb9b1";
        public override int Phase => 4;

        internal override CityView CreateView(CityLifeHub hub) => new BizView(hub);

        private sealed class BizView : CityView
        {
            private readonly List<BusinessDef> _catalog;
            private List<OwnedBusiness> _owned;
            private CityError _ownedError;
            private readonly Dictionary<string, RetainedWriteFlow> _flows = new Dictionary<string, RetainedWriteFlow>(StringComparer.Ordinal);
            private readonly Dictionary<string, string> _notes = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly VisualElement _body = El.Div();

            public BizView(CityLifeHub hub) : base(hub)
            {
                _catalog = BusinessDef.ParseConfig(Services.Get<ConfigStore>().Config("businesses"));
                Root.Add(CityUi.H1(CityLifeStrings.T("biz.title")));
                Root.Add(CityUi.Small(CityLifeStrings.T("biz.hint")));
                Root.Add(_body);
                Load();
                Render();
            }

            private bool Live => _ownedError == null || !_ownedError.Is("UNAVAILABLE");

            private void Load() => Run(ct => Hub.Businesses.Owned(ct), o => { _owned = o; _ownedError = null; Render(); }, e => { _ownedError = e; Render(); }, false);

            private RetainedWriteFlow Flow(string k)
            {
                if (!_flows.TryGetValue(k, out var f)) _flows[k] = f = new RetainedWriteFlow();
                return f;
            }

            private void Render()
            {
                if (!Alive) return;
                _body.Clear();
                if (!Live) _body.Add(CityUi.Card(CityUi.Text(CityLifeStrings.T("biz.notLive"))));
                else if (_ownedError != null) { _body.Add(CityUi.Error(_ownedError.Message)); _body.Add(CityUi.Btn(CityLifeStrings.T("common.retry"), Load, "ct-btn")); }
                if (_owned != null)
                    foreach (var o in _owned) _body.Add(OwnedCard(o));
                foreach (var d in _catalog) _body.Add(CatalogCard(d));
            }

            private string NameOf(string kindId)
            {
                var d = _catalog.Find(c => c.Id == kindId);
                return d != null && !string.IsNullOrEmpty(d.Name) ? d.Name : CityLifeStrings.Readable(kindId);
            }

            private VisualElement CatalogCard(BusinessDef d)
            {
                var card = CityUi.Card(CityUi.H2(string.IsNullOrEmpty(d.Name) ? CityLifeStrings.Readable(d.Id) : d.Name));
                if (CityLifeStrings.Has("biz.flavor." + d.Id)) card.Add(CityUi.Small(CityLifeStrings.T("biz.flavor." + d.Id)));
                card.Add(CityUi.Text(CityLifeStrings.F("biz.cost", Money.Cash(d.CostCents))));
                card.Add(CityUi.Small(CityLifeStrings.F("biz.income", Money.Cash(d.IncomeMinCents), Money.Cash(d.IncomeMaxCents))));
                card.Add(CityUi.Small(CityLifeStrings.F("biz.badDay", Money.Bps(d.BadDayChanceBps, false), Money.Cash(d.BadDayPayCents))));
                card.Add(CityUi.Small(CityLifeStrings.F("biz.expected", Money.Cash((long)Math.Round(d.ExpectedPayCents)), CityLifeStrings.T("common.estimate"))));
                if (d.BreakEvenPeriods > 0) card.Add(CityUi.Small(CityLifeStrings.F("biz.breakEven", Math.Ceiling(d.BreakEvenPeriods), CityLifeStrings.T("common.estimate"))));
                if (d.SellBackCents >= 0) card.Add(CityUi.Small(CityLifeStrings.F("biz.sellBack", Money.Cash(d.SellBackCents))));
                var flow = Flow("buy:" + d.Id);
                if (flow.Error != null) card.Add(CityUi.Error(flow.Error.Message));
                if (_notes.TryGetValue("buy:" + d.Id, out var note)) card.Add(CityUi.Small(note));
                var buy = CityUi.Btn(flow.Stage == WriteStage.Unknown ? CityLifeStrings.T("common.retry") : CityLifeStrings.T("biz.buy"), () => Buy(d));
                var me = Hub.Profile?.Me;
                buy.SetEnabled(Live && _owned != null && flow.Stage != WriteStage.Sending && !Hub.Businesses.IsStub);
                if (me != null && me.CashCents < d.CostCents) card.Add(CityUi.Small(CityLifeStrings.F("err.insufficientCash", Money.Cash(d.CostCents - me.CashCents))));
                card.Add(buy);
                return card;
            }

            private VisualElement OwnedCard(OwnedBusiness o)
            {
                var card = CityUi.Card(PhonePresentation.Status(CityLifeStrings.T("biz.owned"), PhonePresentation.Tone.Success), CityUi.Text(NameOf(o.KindId), "ct-duo-card-title"));
                if (o.LastDay != null)
                {
                    card.Add(CityUi.Small(CityLifeStrings.F("biz.lastDay", CityLifeStrings.T("biz.day." + o.LastDay.Kind)) + " · " + Money.Cash(o.LastDay.PayCents)));
                    if (o.LastDay.Kind == "bad_day") card.Add(CityUi.Text(CityLifeStrings.BadDay(o.LastDay.EventId)));
                }
                if (o.NextPayoutAtMs.HasValue) card.Add(CityUi.Small(CityUi.When(o.NextPayoutAtMs.Value)));
                var cf = Flow("collect:" + o.BusinessId);
                var sf = Flow("sell:" + o.BusinessId);
                foreach (var f in new[] { cf, sf }) if (f.Error != null) card.Add(CityUi.Error(f.Error.Message));
                if (_notes.TryGetValue(o.BusinessId, out var note)) card.Add(CityUi.Text(note));
                var collect = CityUi.Btn(cf.Stage == WriteStage.Unknown ? CityLifeStrings.T("common.retry") : CityLifeStrings.F("biz.collect", Money.Cash(o.PendingCents)), () => Collect(o));
                collect.SetEnabled(cf.Stage != WriteStage.Sending && (o.PendingCents > 0 || cf.Stage == WriteStage.Unknown));
                var sell = CityUi.Btn(sf.Stage == WriteStage.Unknown ? CityLifeStrings.T("common.retry") : CityLifeStrings.T("biz.sell")
                    + (o.SellBackCents >= 0 ? " (" + Money.Cash(o.SellBackCents) + ")" : ""), () => Sell(o), "ct-btn ct-btn-danger");
                sell.SetEnabled(sf.Stage != WriteStage.Sending);
                card.Add(PhonePresentation.Footer(collect, sell));
                return card;
            }

            private void Buy(BusinessDef d)
            {
                var flow = Flow("buy:" + d.Id);
                if (flow.Stage == WriteStage.Done || flow.Stage == WriteStage.Rejected) flow.Reset();
                var key = flow.Begin("POST", "/businesses", d.Id);
                if (key == null) return;
                Render();
                Run(ct => Hub.Businesses.Buy(d.Id, key, ct), b =>
                {
                    flow.Succeeded();
                    _notes["buy:" + d.Id] = CityLifeStrings.F("biz.bought", NameOf(d.Id));
                    Bus.Publish(new SfxSignal("sfx_ui_success"));
                    Hub.RefreshProfile(force: true);
                    Load();
                }, e => { flow.Failed(e); if (e.OutcomeUnknown) Hub.RefreshProfile(force: true); Render(); }, true);
            }

            private void Collect(OwnedBusiness o)
            {
                var flow = Flow("collect:" + o.BusinessId);
                if (flow.Stage == WriteStage.Done || flow.Stage == WriteStage.Rejected) flow.Reset();
                var key = flow.Begin("POST", "/businesses/" + o.BusinessId + "/collect", "");
                if (key == null) return;
                Render();
                Run(ct => Hub.Businesses.Collect(o.BusinessId, key, ct), r =>
                {
                    flow.Succeeded();
                    int bad = r.BadDays;
                    _notes[o.BusinessId] = bad > 0 ? CityLifeStrings.F("biz.collectedBad", Money.Cash(r.CollectedCents), bad) : CityLifeStrings.F("biz.collected", Money.Cash(r.CollectedCents));
                    var badDay = r.Days.Find(x => x.Kind == "bad_day");
                    if (badDay != null) Bus.Publish(ToastSignal.Text(CityLifeStrings.BadDay(badDay.EventId), ToastKind.Bad));
                    if (r.CashCents.HasValue) Hub.Profile?.SetCash(r.CashCents.Value);
                    Hub.RefreshProfile(force: true);
                    Load();
                }, e => { flow.Failed(e); if (e.OutcomeUnknown) Hub.RefreshProfile(force: true); Render(); }, true);
            }

            private void Sell(OwnedBusiness o)
            {
                var flow = Flow("sell:" + o.BusinessId);
                if (flow.Stage == WriteStage.Done || flow.Stage == WriteStage.Rejected) flow.Reset();
                var key = flow.Begin("POST", "/businesses/" + o.BusinessId + "/sell", "");
                if (key == null) return;
                Render();
                Run(ct => Hub.Businesses.SellBack(o.BusinessId, key, ct), refund =>
                {
                    flow.Succeeded();
                    if (refund.HasValue) Bus.Publish(ToastSignal.Text(CityLifeStrings.F("biz.sold", Money.Cash(refund.Value)), ToastKind.Good));
                    Hub.RefreshProfile(force: true);
                    Load();
                }, e => { flow.Failed(e); if (e.OutcomeUnknown) Hub.RefreshProfile(force: true); Render(); }, true);
            }
        }
    }
}
