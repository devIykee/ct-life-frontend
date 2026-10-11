using System;
using System.Collections.Generic;
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
        public override DuoChrome Chrome => DuoChrome.Grouped;

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

            public HomeView(CityHomeApp app, CityLifeHub hub) : base(hub, CityLifeStrings.T("app.home"))
            {
                _app = app;
                // Health-style grouped page: white groups on system grey.
                Page.AddToClassList("duo-page-grouped");
                Page.AddToClassList("duo-home");
                Page.name = "home-page";
                var me = hub.Profile?.Me;
                var homeId = me?.HomeId;
                var reg = Services.Get<ConfigStore>().Registries;
                var name = reg.Homes.FirstOrDefault(h => h.id == homeId)?.name ?? CityLifeStrings.Readable(homeId);
                Body.Add(CityUi.Text(CityLifeStrings.F("home.current", name), "duo-home-where"));
                if (CityLifeStrings.Has("home.flavor." + homeId)) Body.Add(CityUi.Small(CityLifeStrings.T("home.flavor." + homeId)));
                _needs = new NeedsPanel(hub, () => Alive, Life);
                Body.Add(_needs.Root);
                AddEntry();
                bool p4 = Phases.IsOpen(4);
                if (!p4) { Body.Add(Duo.GroupFooter(CityLifeStrings.T("common.phase4"))); return; }
                Body.Add(_rentBox);
                Body.Add(_visitBox);
                Body.Add(_homesBox);
                Body.Add(_galleryBox);
                hub.HomesChanged += Render;
                hub.Visit.Changed += RenderVisit;
                _move.Changed += RenderMove;
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
                var b = Duo.Pill(CityLifeStrings.T(returning ? "home.return" : "home.enter"), () =>
                {
                    if (!Alive) return;
                    if (returning ? _app._atOwnHome?.Invoke() == true : _app._canEnterHome?.Invoke() == true)
                        (returning ? _app._returnToCity : _app._enterHome)?.Invoke();
                }, Duo.PillKind.Primary);
                b.name = returning ? "home-return-city" : "home-enter-own";
                b.SetEnabled(can);
                var row = new DuoRow().Leading(Duo.IconSquare("home", "orange")).Title(CityLifeStrings.T(returning ? "home.return" : "home.enter"), "duo-row-title-strong")
                    .Subtitle(CityLifeStrings.T(can ? "home.explore" : "home.sceneMissing"), twoLines: true).Trailing(b);
                row.AddToClassList("duo-row-static");
                Body.Add(Duo.Group(row));
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

            private static string RentTone(RentState s) => s == RentState.Paid ? "green" : s == RentState.Unknown ? "gray" : s >= RentState.Overdue ? "red" : "orange";

            private void RenderRent()
            {
                _rentBox.Clear();
                var me = Hub.Profile?.Me;
                if (me == null) return;
                var r = Hub.RentNow();
                _rentBox.Add(Duo.GroupHeader(CityLifeStrings.T("rent.title")));
                var rows = new List<VisualElement>();
                var rent = new DuoRow().Leading(Duo.IconSquare("cash", "green")).Title(CityLifeStrings.T("rent.title"), "duo-row-title-strong")
                    .Subtitle(r.RentCents >= 0 ? CityLifeStrings.F("home.rentPerWeek", Money.Cash(r.RentCents)) : CityLifeStrings.T("home.noPrice"));
                rent.name = "home-rent";
                rent.AddToClassList("duo-row-static");
                _rentDue = El.Text("", "duo-row-subtitle");
                rent.TextColumn.Add(_rentDue);
                rent.Trailing(CityUi.Tag(CityLifeStrings.T(r.Key), RentTone(r.State)));
                var current = Hub.Homes.Find(h => h.Id == me.HomeId);
                if (current != null && current.RentCents >= 0 && r.State >= RentState.DueSoon)
                {
                    var pay = Duo.Pill(CityLifeStrings.T("home.renewShort"), () => { _move.Select(current, me.HomeId); }, Duo.PillKind.Primary);
                    pay.name = "home-renew";
                    pay.tooltip = CityLifeStrings.T("home.renew");
                    rent.Trailing(pay);
                }
                rows.Add(rent);
                if (me.DebtCents > 0)
                {
                    var debt = new DuoRow().Leading(Duo.IconSquare("bank", "red")).Title(CityLifeStrings.T("rent.debtRow")).Value(Money.Cash(me.DebtCents), "duo-value-negative");
                    debt.AddToClassList("duo-row-static");
                    rows.Add(debt);
                }
                _rentBox.Add(Duo.Group(rows.ToArray()));
                var notes = new List<string>();
                if (r.ShortfallCents > 0) _rentBox.Add(Duo.Notice(CityLifeStrings.F("rent.shortfall", Money.Cash(r.ShortfallCents)), Duo.Tone.Warning));
                if (r.WeeksBeforeEviction >= 0 && r.State >= RentState.Overdue) _rentBox.Add(Duo.Notice(CityLifeStrings.F("rent.weeksLeft", r.WeeksBeforeEviction), Duo.Tone.Danger));
                if (me.DebtCents > 0)
                {
                    var policy = RentPolicy.Parse(Services.Get<ConfigStore>().Config("economy"));
                    if (policy.LotteryWeeklyDueCents > 0 && policy.LotteryMissedPenaltyBps >= 0)
                        notes.Add(CityLifeStrings.F("rent.lotteryDebt", Money.Cash(me.DebtCents), Money.Cash(policy.LotteryWeeklyDueCents), Money.Bps(policy.LotteryMissedPenaltyBps, false)));
                }
                if (Hub.HomesError != null && !Hub.HomesError.Is("UNAVAILABLE")) notes.Add(Hub.HomesError.Message);
                if (notes.Count > 0) _rentBox.Add(Duo.GroupFooter(string.Join("\n", notes)));
                Tick();
            }

            // ------------------------------------------------------------ moving homes

            private void RenderHomes()
            {
                if (!Alive) return;
                _homesBox.Clear();
                var me = Hub.Profile?.Me;
                _homesBox.Add(Duo.GroupHeader(CityLifeStrings.T("home.movesTitle")));
                var group = El.Div("duo-group");
                group.name = "home-homes";
                long currentRent = Hub.Homes.Find(h => h.Id == me?.HomeId)?.RentCents ?? -1;
                foreach (var h in Hub.Homes)
                {
                    var name = string.IsNullOrEmpty(h.Name) ? CityLifeStrings.Readable(h.Id) : h.Name;
                    var parts = new List<string> { h.RentCents >= 0 ? CityLifeStrings.F("home.rentPerWeek", Money.Cash(h.RentCents)) : CityLifeStrings.T("home.noPrice") };
                    if (h.Rooms > 0) parts.Add(CityLifeStrings.F("home.rooms", h.Rooms));
                    var row = new DuoRow().Leading(Duo.IconSquare("home", me != null && h.Id == me.HomeId ? "orange" : "gray")).Title(name, "duo-row-title-strong")
                        .Subtitle(string.Join(" · ", parts) + (CityLifeStrings.Has("home.flavor." + h.Id) ? "\n" + CityLifeStrings.T("home.flavor." + h.Id) : ""), twoLines: true);
                    row.AddToClassList("duo-row-static");
                    row.name = "home-option-" + h.Id;
                    if (me != null && h.Id == me.HomeId) row.Trailing(CityUi.Tag(CityLifeStrings.T("home.youLiveHereTag"), "blue"));
                    else if (h.RentCents >= 0)
                    {
                        var target = h;
                        bool down = currentRent >= 0 && h.RentCents < currentRent;
                        var b = Duo.Pill(CityLifeStrings.T(down ? "home.downsizeShort" : "home.moveShort"), () => { if (Alive) _move.Select(target, me?.HomeId); });
                        // This backend's /rent endpoint only renews existing leases; it cannot move the character.
                        b.SetEnabled(false);
                        b.tooltip = CityLifeStrings.T("home.moveUnavailable");
                        row.Trailing(b);
                    }
                    group.Add(row);
                }
                Duo.MarkLast(group);
                if (group.childCount > 0) _homesBox.Add(group);
                if (Hub.HomesError != null)
                {
                    _homesBox.Add(Duo.Notice(Hub.HomesError.Message, Duo.Tone.Danger));
                    _homesBox.Add(CityUi.Row(CityUi.Btn(CityLifeStrings.T("common.retry"), () => { _ = Hub.RefreshHomes(force: true); }, CityUi.Plain)));
                }
                _homesBox.Add(Duo.GroupFooter(CityLifeStrings.T("home.movesHint") + " " + CityLifeStrings.T("home.moveUnavailable")));
            }

            /// <summary>The lease confirm / signing / done states in a sheet (was an inline box).</summary>
            private void RenderMove()
            {
                if (!Alive) return;
                RenderHomes();
                var t = _move.Target;
                if (_move.Stage == MoveStage.Idle || t == null) { _moveSheet?.Close(); _moveSheet = null; return; }
                var name = string.IsNullOrEmpty(t.Name) ? CityLifeStrings.Readable(t.Id) : t.Name;
                if (_moveSheet == null || _moveSheet.panel == null)
                {
                    _moveSheet = new DuoSheet(CityLifeStrings.T(_move.IsRenewal ? "home.renew" : "home.move"));
                    _moveSheet.name = "home-move-sheet";
                    var sheet = _moveSheet;
                    sheet.Closed += () =>
                    {
                        if (_moveSheet != sheet) return;
                        _moveSheet = null;
                        if (_move.Stage == MoveStage.Unknown) Hub.RefreshProfile(true);
                        if (_move.Stage != MoveStage.Signing && _move.Stage != MoveStage.Idle) _move.Cancel();
                    };
                    ShowSheet(sheet);
                }
                var content = _moveSheet.Content;
                content.Clear();
                switch (_move.Stage)
                {
                    case MoveStage.Confirming:
                    case MoveStage.Rejected:
                    case MoveStage.Unknown:
                        content.Add(CityUi.Text(_move.IsRenewal ? CityLifeStrings.F("home.confirmRenew", Money.Cash(t.RentCents), name)
                            : CityLifeStrings.F("home.confirmMove", name, Money.Cash(t.RentCents))));
                        if (_move.Error != null) content.Add(Duo.Notice(_move.Error.Message, Duo.Tone.Danger));
                        var sign = CityUi.Btn(_move.Stage == MoveStage.Unknown ? CityLifeStrings.T("common.retry") : CityLifeStrings.T(_move.IsRenewal ? "home.renew" : "home.move"), Sign);
                        sign.name = "home-move-sign";
                        content.Add(sign);
                        var cancel = El.Btn(CityLifeStrings.T("common.cancel"), () => _moveSheet?.Close(), "duo-sheet-secondary");
                        content.Add(cancel);
                        break;
                    case MoveStage.Signing:
                        content.Add(CityUi.Text(CityLifeStrings.T("home.signing")));
                        break;
                    case MoveStage.Done:
                        content.Add(CityUi.Text(CityLifeStrings.F(_move.IsRenewal ? "home.renewed" : "home.moved", name)));
                        content.Add(El.Btn(CityLifeStrings.T("common.close"), () => _moveSheet?.Close(), "duo-sheet-secondary"));
                        break;
                }
            }

            private DuoSheet _moveSheet;

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
                _visitBox.Add(Duo.GroupHeader(CityLifeStrings.T("visit.title")));
                var group = El.Div("duo-group");
                group.name = "home-visits";
                switch (v.Stage)
                {
                    case VisitStage.Requesting: group.Add(StateRow(CityLifeStrings.T("visit.requesting"))); break;
                    case VisitStage.Joining: group.Add(StateRow(CityLifeStrings.T("visit.joining"))); break;
                    case VisitStage.Leaving: group.Add(StateRow(CityLifeStrings.T("visit.leaving"))); break;
                    case VisitStage.Inside:
                        bool own = v.IsOwnHome(Hub.UserId);
                        var inside = new DuoRow().Leading(Duo.IconSquare("contacts", "teal")).Title(own ? CityLifeStrings.F("visit.insideOwn", v.Visitors)
                            : CityLifeStrings.F("visit.inside", string.IsNullOrEmpty(v.OwnerName) ? "?" : v.OwnerName, v.Visitors), "duo-row-title-strong");
                        var names = Hub.Presenter.Names;
                        var sub = new List<string>();
                        if (names.Count > 0) sub.Add(CityLifeStrings.F("visit.visitorsHere", string.Join(", ", names)));
                        if (!Hub.Presenter.ShowsInterior) sub.Add(CityLifeStrings.T("visit.presenceOnly"));
                        if (sub.Count > 0) inside.Subtitle(string.Join("\n", sub), twoLines: true);
                        inside.AddToClassList("duo-row-static");
                        var leave = Duo.Pill(CityLifeStrings.T("visit.leave"), () => _ = Hub.LeaveVisit(), Duo.PillKind.Destructive);
                        leave.name = "home-visit-leave";
                        inside.Trailing(leave);
                        group.Add(inside);
                        break;
                    default:
                        if (v.Error != null && (v.Stage == VisitStage.Denied || v.Stage == VisitStage.Failed || v.Stage == VisitStage.Unavailable))
                        {
                            var err = new DuoRow().Title(v.Error.Message, "duo-destructive");
                            err.AddToClassList("duo-row-static");
                            err.Trailing(Duo.Pill(CityLifeStrings.T("common.close"), v.Dismiss));
                            group.Add(err);
                        }
                        var knock = new DuoRow(KnockSheet).Leading(Duo.IconSquare("door", "blue")).Title(CityLifeStrings.T("visit.knockRow"), "duo-row-title-strong").Chevron();
                        knock.name = "home-visit-knock";
                        group.Add(knock);
                        var open = new DuoRow().Leading(Duo.IconSquare("home", "orange")).Title(CityLifeStrings.T("visit.myRoom"));
                        open.AddToClassList("duo-row-static");
                        var openPill = Duo.Pill(CityLifeStrings.T("visit.openShort"), () => _ = Hub.RequestVisit(Hub.UserId, Hub.Profile?.Alias));
                        openPill.name = "home-visit-open";
                        open.Trailing(openPill);
                        group.Add(open);
                        break;
                }
                Duo.MarkLast(group);
                _visitBox.Add(group);
                _visitBox.Add(Duo.GroupHeader(CityLifeStrings.T("visit.policy")));
                if (_policy == null)
                    _visitBox.Add(Duo.GroupFooter(_policyError != null && _policyError.Is("UNAVAILABLE") ? CityLifeStrings.T("common.notLive") : _policyError?.Message ?? CityLifeStrings.T("common.loading")));
                else
                {
                    var policies = (VisitorPolicy[])Enum.GetValues(typeof(VisitorPolicy));
                    var seg = new DuoSegmented(policies.Select(p => CityLifeStrings.T("visit.policy." + VisitorPolicies.Wire(p))).ToList(), Array.IndexOf(policies, _policy.Value), i => SavePolicy(policies[i]));
                    seg.name = "home-visit-policy";
                    seg.SetEnabled(_policySave.Stage != WriteStage.Sending);
                    _visitBox.Add(El.Div("duo-home-policy", seg));
                    string note = _policySave.Error != null ? _policySave.Error.Message : _policySave.Stage == WriteStage.Done ? CityLifeStrings.T("visit.policySaved") : CityLifeStrings.T("visit.hint");
                    _visitBox.Add(Duo.GroupFooter(note));
                }
            }

            private static DuoRow StateRow(string text)
            {
                var r = new DuoRow().Leading(Duo.IconSquare("door", "gray")).Title(text);
                r.AddToClassList("duo-row-static");
                return r;
            }

            private void KnockSheet()
            {
                var sheet = new DuoSheet(CityLifeStrings.T("visit.knockRow"), CityLifeStrings.T("visit.hint"));
                sheet.name = "home-visit-sheet";
                var field = CityUi.Field(CityLifeStrings.T("visit.idLabel"), _visitId, s => _visitId = (s ?? "").Trim(), 80);
                field.name = "home-visit-id";
                sheet.AddContent(field);
                sheet.AddPrimary(CityLifeStrings.T("visit.go"), () => { if (CityWire.IsSafeId(_visitId)) _ = Hub.RequestVisit(_visitId, null); }, name: "home-visit-go");
                sheet.AddSecondary(CityLifeStrings.T("common.cancel"));
                ShowSheet(sheet);
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
                _needs?.Close();
                base.Dispose();
            }
        }
    }
}
