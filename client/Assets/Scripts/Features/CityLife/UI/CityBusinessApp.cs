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

            public BizView(CityLifeHub hub) : base(hub, CityLifeStrings.T("biz.title"))
            {
                _catalog = BusinessDef.ParseConfig(Services.Get<ConfigStore>().Config("businesses"));
                Page.name = "business-page";
                Page.AddToClassList("duo-business");
                Body.Add(_body);
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
                if (!Live) _body.Add(Duo.Notice(CityLifeStrings.T("biz.notLive"), Duo.Tone.Warning));
                else if (_ownedError != null)
                {
                    _body.Add(Duo.Notice(_ownedError.Message, Duo.Tone.Danger));
                    _body.Add(CityUi.Row(CityUi.Btn(CityLifeStrings.T("common.retry"), Load, CityUi.Plain)));
                }
                if (_owned != null && _owned.Count > 0)
                {
                    // Hero: what is waiting to be collected across your businesses (server pending amounts).
                    long pending = 0;
                    foreach (var o in _owned) pending += Math.Max(0, o.PendingCents);
                    var hero = Duo.Hero(CityLifeStrings.T("biz.pendingLabel"), Money.Cash(pending), null, null, CityLifeStrings.F("biz.ownedCount", _owned.Count));
                    hero.name = "business-hero";
                    _body.Add(hero);
                    _body.Add(Duo.SectionHeader(CityLifeStrings.T("biz.yours")));
                    var owned = El.Div("duo-list");
                    foreach (var o in _owned) owned.Add(OwnedRow(o));
                    Duo.MarkLast(owned);
                    _body.Add(owned);
                }
                _body.Add(Duo.SectionHeader(CityLifeStrings.T("biz.catalog")));
                _body.Add(CityUi.Small(CityLifeStrings.T("biz.hint")));
                var list = El.Div("duo-list");
                foreach (var d in _catalog) list.Add(CatalogRow(d));
                Duo.MarkLast(list);
                _body.Add(list);
            }

            private string NameOf(string kindId)
            {
                var d = _catalog.Find(c => c.Id == kindId);
                return d != null && !string.IsNullOrEmpty(d.Name) ? d.Name : CityLifeStrings.Readable(kindId);
            }

            private static readonly string[] Tints = { "indigo", "orange", "teal", "pink", "green", "purple" };

            private VisualElement CatalogRow(BusinessDef d)
            {
                int i = Math.Max(0, _catalog.IndexOf(d));
                var row = new DuoRow(() => CatalogSheet(d)).Leading(El.PassThrough(El.Div("duo-app-tile duo-tint-" + Tints[i % Tints.Length], new UiIcon("city"))))
                    .Title(string.IsNullOrEmpty(d.Name) ? CityLifeStrings.Readable(d.Id) : d.Name, "duo-row-title-strong")
                    .Subtitle(CityLifeStrings.F("biz.rowLine", Money.Cash(d.CostCents), Money.Cash(d.IncomeMinCents), Money.Cash(d.IncomeMaxCents)), twoLines: true);
                row.name = "business-kind-" + d.Id;
                var flow = Flow("buy:" + d.Id);
                if (flow.Error != null) row.TextColumn.Add(El.Text(flow.Error.Message, "duo-row-subtitle duo-destructive"));
                if (_notes.TryGetValue("buy:" + d.Id, out var note)) row.TextColumn.Add(El.Text(note, "duo-row-subtitle duo-value-positive"));
                var buy = Duo.Pill(flow.Stage == WriteStage.Unknown ? CityLifeStrings.T("common.retry") : CityLifeStrings.T("biz.buy"), () => Buy(d), Duo.PillKind.Primary);
                buy.name = "business-buy-" + d.Id;
                buy.SetEnabled(Live && _owned != null && flow.Stage != WriteStage.Sending && !Hub.Businesses.IsStub);
                row.Trailing(buy);
                return row;
            }

            private void CatalogSheet(BusinessDef d)
            {
                var name = string.IsNullOrEmpty(d.Name) ? CityLifeStrings.Readable(d.Id) : d.Name;
                var sheet = new DuoSheet(name, CityLifeStrings.Has("biz.flavor." + d.Id) ? CityLifeStrings.T("biz.flavor." + d.Id) : null);
                sheet.name = "business-sheet";
                sheet.AddContent(CityUi.Pair(CityLifeStrings.T("biz.costRow"), Money.Cash(d.CostCents)));
                sheet.AddContent(CityUi.Pair(CityLifeStrings.T("biz.incomeRow"), Money.Cash(d.IncomeMinCents) + "–" + Money.Cash(d.IncomeMaxCents)));
                sheet.AddContent(CityUi.Small(CityLifeStrings.F("biz.badDay", Money.Bps(d.BadDayChanceBps, false), Money.Cash(d.BadDayPayCents))));
                sheet.AddContent(CityUi.Small(CityLifeStrings.F("biz.expected", Money.Cash((long)Math.Round(d.ExpectedPayCents)), CityLifeStrings.T("common.estimate"))));
                if (d.BreakEvenPeriods > 0) sheet.AddContent(CityUi.Small(CityLifeStrings.F("biz.breakEven", Math.Ceiling(d.BreakEvenPeriods), CityLifeStrings.T("common.estimate"))));
                if (d.SellBackCents >= 0) sheet.AddContent(CityUi.Small(CityLifeStrings.F("biz.sellBack", Money.Cash(d.SellBackCents))));
                var me = Hub.Profile?.Me;
                if (me != null && me.CashCents < d.CostCents) sheet.AddContent(Duo.Notice(CityLifeStrings.F("err.insufficientCash", Money.Cash(d.CostCents - me.CashCents)), Duo.Tone.Warning));
                var flow = Flow("buy:" + d.Id);
                if (Live && _owned != null && flow.Stage != WriteStage.Sending && !Hub.Businesses.IsStub)
                    sheet.AddPrimary(CityLifeStrings.F("biz.buyFor", Money.Cash(d.CostCents)), () => Buy(d), name: "business-sheet-buy");
                sheet.AddSecondary(CityLifeStrings.T("common.close"));
                ShowSheet(sheet);
            }

            private VisualElement OwnedRow(OwnedBusiness o)
            {
                var sub = new List<string>();
                if (o.LastDay != null) sub.Add(CityLifeStrings.F("biz.lastDay", CityLifeStrings.T("biz.day." + o.LastDay.Kind)) + " · " + Money.Cash(o.LastDay.PayCents));
                if (o.NextPayoutAtMs.HasValue) sub.Add(CityLifeStrings.F("biz.nextPayout", CityUi.When(o.NextPayoutAtMs.Value)));
                var row = new DuoRow(() => OwnedSheet(o)).Leading(El.PassThrough(El.Div("duo-app-tile duo-tint-green", new UiIcon("city"))))
                    .Title(NameOf(o.KindId), "duo-row-title-strong");
                if (sub.Count > 0) row.Subtitle(string.Join("\n", sub), twoLines: true);
                row.name = "business-owned-" + o.BusinessId;
                var cf = Flow("collect:" + o.BusinessId);
                var sf = Flow("sell:" + o.BusinessId);
                foreach (var f in new[] { cf, sf }) if (f.Error != null) row.TextColumn.Add(El.Text(f.Error.Message, "duo-row-subtitle duo-destructive"));
                if (_notes.TryGetValue(o.BusinessId, out var note)) row.TextColumn.Add(El.Text(note, "duo-row-subtitle duo-value-positive"));
                if (o.LastDay?.Kind == "bad_day") row.TextColumn.Add(El.Text(CityLifeStrings.BadDay(o.LastDay.EventId), "duo-row-subtitle duo-comp-status"));
                var collect = Duo.Pill(cf.Stage == WriteStage.Unknown ? CityLifeStrings.T("common.retry") : Money.Cash(o.PendingCents), () => Collect(o), Duo.PillKind.Primary);
                collect.name = "business-collect-" + o.BusinessId;
                collect.tooltip = CityLifeStrings.F("biz.collect", Money.Cash(o.PendingCents));
                collect.SetEnabled(cf.Stage != WriteStage.Sending && (o.PendingCents > 0 || cf.Stage == WriteStage.Unknown));
                row.Trailing(collect);
                return row;
            }

            private void OwnedSheet(OwnedBusiness o)
            {
                var sheet = new DuoSheet(NameOf(o.KindId), CityLifeStrings.T("biz.owned"));
                sheet.name = "business-owned-sheet";
                sheet.AddContent(CityUi.Pair(CityLifeStrings.T("biz.pendingLabel"), Money.Cash(o.PendingCents)));
                if (o.LastDay != null) sheet.AddContent(CityUi.Pair(CityLifeStrings.F("biz.lastDay", CityLifeStrings.T("biz.day." + o.LastDay.Kind)), Money.Cash(o.LastDay.PayCents)));
                if (o.NextPayoutAtMs.HasValue) sheet.AddContent(CityUi.Pair(CityLifeStrings.T("biz.nextPayoutRow"), CityUi.When(o.NextPayoutAtMs.Value)));
                if (o.LastDay?.Kind == "bad_day") sheet.AddContent(Duo.Notice(CityLifeStrings.BadDay(o.LastDay.EventId), Duo.Tone.Warning));
                var cf = Flow("collect:" + o.BusinessId);
                var sf = Flow("sell:" + o.BusinessId);
                if (cf.Stage != WriteStage.Sending && (o.PendingCents > 0 || cf.Stage == WriteStage.Unknown))
                    sheet.AddPrimary(CityLifeStrings.F("biz.collect", Money.Cash(o.PendingCents)), () => Collect(o), name: "business-sheet-collect");
                if (sf.Stage != WriteStage.Sending)
                    sheet.AddPrimary((sf.Stage == WriteStage.Unknown ? CityLifeStrings.T("common.retry") : CityLifeStrings.T("biz.sell"))
                        + (o.SellBackCents >= 0 ? " (" + Money.Cash(o.SellBackCents) + ")" : ""), () => Sell(o), destructive: true, name: "business-sheet-sell");
                sheet.AddSecondary(CityLifeStrings.T("common.close"));
                ShowSheet(sheet);
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
