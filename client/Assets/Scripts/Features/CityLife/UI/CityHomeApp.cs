using System;
using System.Linq;
using CTLife.Core;
using CTLife.UI;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// The Home phone app (id `home`, replaces CTLife.UI.HomeApp with the same constructor): private home entry, needs
    /// with lazy decay, rent / debt / eviction status, moving between the five homes, visits and visitor permissions, and
    /// the NFT gallery wall. Phase 4 sections stay closed until `Phases.IsOpen(4)`.
    /// </summary>
    public sealed class CityHomeApp : CityApp
    {
        private readonly Action _enterHome, _returnToCity;
        private readonly Func<bool> _canEnterHome, _atOwnHome;

        public CityHomeApp(Action enterHome = null, Action returnToCity = null, Func<bool> canEnterHome = null, Func<bool> atOwnHome = null)
        {
            _enterHome = enterHome; _returnToCity = returnToCity; _canEnterHome = canEnterHome; _atOwnHome = atOwnHome;
        }

        public override string Id => "home";
        public override string Title => CityLifeStrings.T("app.home");
        public override string Color => "#fa8231";

        internal override CityView CreateView(CityLifeHub hub) => new HomeView(this, hub);

        private sealed class HomeView : CityView
        {
            private readonly CityHomeApp _app;
            private readonly NeedsPanel _needs;
            private readonly VisualElement _rentBox = El.Div(), _homesBox = El.Div(), _visitBox = El.Div(), _galleryBox = El.Div();
            private readonly MoveHomeFlow _move = new MoveHomeFlow();
            private readonly RetainedWriteFlow _policySave = new RetainedWriteFlow();
            private VisitorPolicy? _policy;
            private CityError _policyError;
            private Label _rentDue;
            private string _visitId = "";
            private NftGalleryEditor _gallery;

            public HomeView(CityHomeApp app, CityLifeHub hub) : base(hub)
            {
                _app = app;
                var me = hub.Profile?.Me;
                var homeId = me?.HomeId;
                var reg = Services.Get<ConfigStore>().Registries;
                var name = reg.Homes.FirstOrDefault(h => h.id == homeId)?.name ?? CityLifeStrings.Readable(homeId);
                Root.Add(CityUi.H1(CityLifeStrings.F("home.current", name)));
                if (CityLifeStrings.Has("home.flavor." + homeId)) Root.Add(CityUi.Small(CityLifeStrings.T("home.flavor." + homeId)));
                AddEntry();
                _needs = new NeedsPanel(hub, () => Alive, Life);
                Root.Add(_needs.Root);
                bool p4 = Phases.IsOpen(4);
                if (!p4) { Root.Add(CityUi.Small(CityLifeStrings.T("common.phase4"))); return; }
                Root.Add(_rentBox);
                Root.Add(_visitBox);
                Root.Add(_homesBox);
                Root.Add(_galleryBox);
                hub.HomesChanged += Render;
                hub.Visit.Changed += RenderVisit;
                _move.Changed += RenderHomes;
                _policySave.Changed += RenderVisit;
                Subs.On<ProfileChangedSignal>(_ => { if (Alive) { RenderRent(); RenderHomes(); } });
                _ = hub.RefreshHomes();
                Run(ct => hub.Api.HomeSettings(ct), p => { _policy = p; RenderVisit(); }, e => { _policyError = e; RenderVisit(); }, false);
                Render();
            }

            private void AddEntry()
            {
                if (_app._enterHome == null || _app._returnToCity == null) return;
                bool returning = _app._atOwnHome?.Invoke() == true;
                bool can = returning || _app._canEnterHome?.Invoke() == true;
                var b = CityUi.Btn(CityLifeStrings.T(returning ? "home.return" : "home.enter"), () =>
                {
                    if (!Alive) return;
                    if (returning ? _app._atOwnHome?.Invoke() == true : _app._canEnterHome?.Invoke() == true)
                        (returning ? _app._returnToCity : _app._enterHome)?.Invoke();
                });
                b.name = returning ? "home-return-city" : "home-enter-own";
                b.SetEnabled(can);
                Root.Add(CityUi.Card(b, CityUi.Small(CityLifeStrings.T(can ? "home.explore" : "home.sceneMissing"))));
            }

            private void Render()
            {
                if (!Alive) return;
                RenderRent();
                RenderVisit();
                RenderHomes();
                RenderGallery();
            }

            protected override void Tick()
            {
                _needs.Refresh();
                if (_rentDue != null)
                {
                    var r = Hub.RentNow();
                    _rentDue.text = r.DueAtMs > 0 ? CityLifeStrings.F("rent.dueIn", WeekClock.Countdown(r.DueAtMs - Hub.NowMs), CityUi.When(r.DueAtMs)) : "";
                }
            }

            // ------------------------------------------------------------ rent and debt

            private void RenderRent()
            {
                _rentBox.Clear();
                var me = Hub.Profile?.Me;
                if (me == null) return;
                var r = Hub.RentNow();
                var card = CityUi.Card(CityUi.H2(CityLifeStrings.T("rent.title")),
                    PhonePresentation.Status(CityLifeStrings.T(r.Key), r.State == RentState.Paid ? PhonePresentation.Tone.Success
                        : r.State == RentState.Unknown ? PhonePresentation.Tone.Neutral
                        : r.State >= RentState.Overdue ? PhonePresentation.Tone.Danger : PhonePresentation.Tone.Warning));
                if (r.RentCents >= 0) card.Add(CityUi.Pair(CityLifeStrings.T("rent.title"), CityLifeStrings.F("home.rentPerWeek", Money.Cash(r.RentCents))));
                _rentDue = CityUi.Small("");
                card.Add(_rentDue);
                if (r.ShortfallCents > 0) card.Add(CityUi.Error(CityLifeStrings.F("rent.shortfall", Money.Cash(r.ShortfallCents))));
                if (r.WeeksBeforeEviction >= 0 && r.State >= RentState.Overdue) card.Add(CityUi.Error(CityLifeStrings.F("rent.weeksLeft", r.WeeksBeforeEviction)));
                if (me.DebtCents > 0)
                {
                    card.Add(CityUi.Pair(CityLifeStrings.F("rent.debt", ""), Money.Cash(me.DebtCents)));
                    var policy = RentPolicy.Parse(Services.Get<ConfigStore>().Config("economy"));
                    if (policy.LotteryWeeklyDueCents > 0 && policy.LotteryMissedPenaltyBps >= 0)
                        card.Add(CityUi.Small(CityLifeStrings.F("rent.lotteryDebt", Money.Cash(me.DebtCents), Money.Cash(policy.LotteryWeeklyDueCents), Money.Bps(policy.LotteryMissedPenaltyBps, false))));
                }
                var current = Hub.Homes.Find(h => h.Id == me.HomeId);
                if (current != null && current.RentCents >= 0 && r.State >= RentState.DueSoon)
                    card.Add(CityUi.Btn(CityLifeStrings.T("home.renew"), () => { _move.Select(current, me.HomeId); }, "ct-btn"));
                if (Hub.HomesError != null && !Hub.HomesError.Is("UNAVAILABLE")) card.Add(CityUi.Small(Hub.HomesError.Message));
                _rentBox.Add(card);
                Tick();
            }

            // ------------------------------------------------------------ moving homes

            private void RenderHomes()
            {
                if (!Alive) return;
                _homesBox.Clear();
                var me = Hub.Profile?.Me;
                var card = CityUi.Card(CityUi.H2(CityLifeStrings.T("home.movesTitle")), CityUi.Small(CityLifeStrings.T("home.movesHint")));
                card.Add(CityUi.Small(CityLifeStrings.T("home.moveUnavailable")));
                if (Hub.HomesError != null)
                {
                    card.Add(CityUi.Error(Hub.HomesError.Message));
                    card.Add(CityUi.Btn(CityLifeStrings.T("common.retry"), () => { _ = Hub.RefreshHomes(force: true); }, "ct-btn"));
                }
                if (_move.Stage != MoveStage.Idle && _move.Target != null) card.Add(MoveBox(me));
                long currentRent = Hub.Homes.Find(h => h.Id == me?.HomeId)?.RentCents ?? -1;
                foreach (var h in Hub.Homes)
                {
                    var name = string.IsNullOrEmpty(h.Name) ? CityLifeStrings.Readable(h.Id) : h.Name;
                    var row = CityUi.Card(CityUi.Text(name, "ct-body"));
                    row.Add(CityUi.Small(h.RentCents >= 0 ? CityLifeStrings.F("home.rentPerWeek", Money.Cash(h.RentCents)) : CityLifeStrings.T("home.noPrice")));
                    if (h.Rooms > 0) row.Add(CityUi.Small(CityLifeStrings.F("home.rooms", h.Rooms)));
                    if (CityLifeStrings.Has("home.flavor." + h.Id)) row.Add(CityUi.Small(CityLifeStrings.T("home.flavor." + h.Id)));
                    if (me != null && h.Id == me.HomeId) row.Add(CityUi.Text(CityLifeStrings.T("home.youLiveHere"), "ct-badge"));
                    else if (h.RentCents >= 0)
                    {
                        var target = h;
                        bool down = currentRent >= 0 && h.RentCents < currentRent;
                        var b = CityUi.Btn(CityLifeStrings.T(down ? "home.downsize" : "home.move"), () => { if (Alive) _move.Select(target, me?.HomeId); }, "ct-btn");
                        // This backend's /rent endpoint only renews existing leases; it cannot move the character.
                        b.SetEnabled(false);
                        row.Add(b);
                        if (me != null && me.CashCents < h.RentCents) row.Add(CityUi.Small(CityLifeStrings.F("home.cantAffordHint", Money.Cash(me.CashCents))));
                    }
                    card.Add(row);
                }
                _homesBox.Add(card);
            }

            private VisualElement MoveBox(CTLife.Rest.Model.Me me)
            {
                var t = _move.Target;
                var name = string.IsNullOrEmpty(t.Name) ? CityLifeStrings.Readable(t.Id) : t.Name;
                var box = CityUi.Card();
                switch (_move.Stage)
                {
                    case MoveStage.Confirming:
                    case MoveStage.Rejected:
                    case MoveStage.Unknown:
                        box.Add(CityUi.Text(_move.IsRenewal ? CityLifeStrings.F("home.confirmRenew", Money.Cash(t.RentCents), name)
                            : CityLifeStrings.F("home.confirmMove", name, Money.Cash(t.RentCents))));
                        if (_move.Error != null) box.Add(CityUi.Error(_move.Error.Message));
                        box.Add(CityUi.Row(
                            CityUi.Btn(_move.Stage == MoveStage.Unknown ? CityLifeStrings.T("common.retry") : CityLifeStrings.T(_move.IsRenewal ? "home.renew" : "home.move"), Sign),
                            CityUi.Btn(CityLifeStrings.T("common.cancel"), () => { if (_move.Stage == MoveStage.Unknown) Hub.RefreshProfile(true); _move.Cancel(); }, "ct-btn")));
                        break;
                    case MoveStage.Signing:
                        box.Add(CityUi.Text(CityLifeStrings.T("home.signing")));
                        break;
                    case MoveStage.Done:
                        box.Add(CityUi.Text(CityLifeStrings.F(_move.IsRenewal ? "home.renewed" : "home.moved", name)));
                        box.Add(CityUi.Btn(CityLifeStrings.T("common.close"), _move.Cancel, "ct-btn"));
                        break;
                }
                return box;
            }

            private void Sign()
            {
                if (!HomeLeaseActions.CanRenew(Hub.Profile?.Me?.HomeId, _move.Target?.Id))
                {
                    Bus.Publish(ToastSignal.Text(CityLifeStrings.T("home.moveUnavailable"), ToastKind.Bad));
                    return;
                }
                var key = _move.BeginSign();
                if (key == null) return;
                var target = _move.Target;
                Run(async ct =>
                {
                    var receipt = await Hub.Api.Rent(target.Id, key, ct);
                    // A lease receipt alone does not prove that the authoritative character moved.
                    // Current server rent renews an owned home without changing characters.home_id.
                    var profile = Hub.Profile ?? throw new InvalidOperationException("Missing player profile");
                    await profile.Refresh(ct);
                    if (profile.Me == null || profile.Me.HomeId != target.Id)
                        throw new FormatException("Home change was not confirmed by the player profile");
                    return receipt;
                }, receipt =>
                {
                    _move.Succeeded(receipt);
                    Bus.Publish(new SfxSignal("sfx_ui_success"));
                    Hub.RefreshProfile(force: true);
                    _ = Hub.RefreshHomes(force: true);
                }, err =>
                {
                    _move.Failed(err);
                    if (err.OutcomeUnknown) Hub.RefreshProfile(force: true);
                }, true);
            }

            // ------------------------------------------------------------ visits and visitor permissions

            private void RenderVisit()
            {
                if (!Alive) return;
                _visitBox.Clear();
                var v = Hub.Visit;
                var card = CityUi.Card(CityUi.H2(CityLifeStrings.T("visit.title")));
                switch (v.Stage)
                {
                    case VisitStage.Requesting: card.Add(CityUi.Text(CityLifeStrings.T("visit.requesting"))); break;
                    case VisitStage.Joining: card.Add(CityUi.Text(CityLifeStrings.T("visit.joining"))); break;
                    case VisitStage.Leaving: card.Add(CityUi.Text(CityLifeStrings.T("visit.leaving"))); break;
                    case VisitStage.Inside:
                        bool own = v.IsOwnHome(Hub.UserId);
                        card.Add(CityUi.Text(own ? CityLifeStrings.F("visit.insideOwn", v.Visitors)
                            : CityLifeStrings.F("visit.inside", string.IsNullOrEmpty(v.OwnerName) ? "?" : v.OwnerName, v.Visitors)));
                        var names = Hub.Presenter.Names;
                        if (names.Count > 0) card.Add(CityUi.Small(CityLifeStrings.F("visit.visitorsHere", string.Join(", ", names))));
                        if (!Hub.Presenter.ShowsInterior) card.Add(CityUi.Small(CityLifeStrings.T("visit.presenceOnly")));
                        card.Add(CityUi.Btn(CityLifeStrings.T("visit.leave"), () => _ = Hub.LeaveVisit(), "ct-btn"));
                        break;
                    default:
                        if (v.Error != null && (v.Stage == VisitStage.Denied || v.Stage == VisitStage.Failed || v.Stage == VisitStage.Unavailable))
                        {
                            card.Add(CityUi.Error(v.Error.Message));
                            card.Add(CityUi.Btn(CityLifeStrings.T("common.close"), v.Dismiss, "ct-btn"));
                        }
                        card.Add(CityUi.Small(CityLifeStrings.T("visit.hint")));
                        card.Add(CityUi.Field(CityLifeStrings.T("visit.idLabel"), _visitId, s => _visitId = (s ?? "").Trim(), 80));
                        card.Add(CityUi.Row(
                            CityUi.Btn(CityLifeStrings.T("visit.go"), () => { if (CityWire.IsSafeId(_visitId)) _ = Hub.RequestVisit(_visitId, null); }, "ct-btn"),
                            CityUi.Btn(CityLifeStrings.T("visit.myRoom"), () => _ = Hub.RequestVisit(Hub.UserId, Hub.Profile?.Alias), "ct-btn")));
                        break;
                }
                card.Add(CityUi.Text(CityLifeStrings.T("visit.policy"), "ct-small"));
                if (_policy == null)
                    card.Add(CityUi.Small(_policyError != null && _policyError.Is("UNAVAILABLE") ? CityLifeStrings.T("common.notLive") : _policyError?.Message ?? CityLifeStrings.T("common.loading")));
                else
                {
                    var row = CityUi.Row();
                    foreach (VisitorPolicy p in Enum.GetValues(typeof(VisitorPolicy)))
                    {
                        var pick = p;
                        var chip = CityUi.Chip(CityLifeStrings.T("visit.policy." + VisitorPolicies.Wire(p)), () => SavePolicy(pick), _policy == p);
                        chip.SetEnabled(_policySave.Stage != WriteStage.Sending);
                        row.Add(chip);
                    }
                    card.Add(row);
                    if (_policySave.Stage == WriteStage.Done) card.Add(CityUi.Small(CityLifeStrings.T("visit.policySaved")));
                    if (_policySave.Error != null) card.Add(CityUi.Error(_policySave.Error.Message));
                }
                _visitBox.Add(card);
            }

            private void SavePolicy(VisitorPolicy p)
            {
                if (_policy == p && _policySave.Stage != WriteStage.Unknown) return;
                if (_policySave.Stage == WriteStage.Done || _policySave.Stage == WriteStage.Rejected) _policySave.Reset();
                var key = _policySave.Begin("PUT", "/homes/me/settings", VisitorPolicies.Wire(p));
                if (key == null) return;
                Run(ct => Hub.Api.SaveHomeSettings(p, key, ct), _ => { _policy = p; _policySave.Succeeded(); }, e => _policySave.Failed(e), true);
            }

            // ------------------------------------------------------------ gallery wall

            private void RenderGallery()
            {
                if (_gallery == null)
                {
                    _gallery = new NftGalleryEditor(Hub, () => Alive, Life);
                    _galleryBox.Add(_gallery.Root);
                }
                else _gallery.Refresh();
            }

            public override void Dispose()
            {
                if (Disposed) return;
                Hub.HomesChanged -= Render;
                Hub.Visit.Changed -= RenderVisit;
                _gallery?.Dispose();
                base.Dispose();
            }
        }
    }
}
