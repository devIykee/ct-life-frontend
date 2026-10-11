using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine.UIElements;

namespace CTLife.Competition
{
    /// <summary>
    /// Phone app "Farm" (id `farm`): the Airdrop Farm daily claim loop and the Sybil Barn jail state. The Airdrop Farm and Sybil Barn
    /// venues open it. The proposal defines the loop (a daily claim, an SGT bonus, a tribe bonus, a streak, a daily cap), so this
    /// screen only shows the server's numbers; it invents no rates. Jail and bail are the server's.
    /// </summary>
    public sealed class FarmApp : CompApp
    {
        public override string Id => CompetitionLinks.FarmApp;
        public override string Title => CompStrings.T("app.farm");
        public override string Monogram => "AF";
        public override string Color => "#20bf6b";

        internal override CompScreen CreateView(CompetitionHub hub) => new FarmScreen(hub);
    }

    internal sealed class FarmScreen : CompScreen
    {
        private readonly VisualElement _jail = El.Div(), _farm = El.Div(), _history = El.Div(), _barn = El.Div();
        private SybilStatus _sybil;
        private FarmStatus _status;
        private Label _jailCountdown, _claimCountdown;
        private Button _claimBtn;

        public FarmScreen(CompetitionHub hub) : base(hub, CompStrings.T("app.farm"))
        {
            Page.name = "farm-page";
            Page.AddToClassList("duo-farm");
            Body.Add(_jail);
            Body.Add(_farm);
            Body.Add(CompUi.Small(CompStrings.T("farm.note")));
            Body.Add(Duo.SectionHeader(CompStrings.T("farm.history")));
            Body.Add(_history);
            Body.Add(Duo.SectionHeader(CompStrings.T("sybil.barn"), CompStrings.T("sybil.barn.show"), LoadBarn));
            Body.Add(_barn);
            Hub.Feed.SybilEvent += OnSybil;
            _sybil = FromFeed(Hub.Feed.Sybil);
            LoadAll();
        }

        protected override void OnDisposed() { Hub.Feed.SybilEvent -= OnSybil; }

        private static SybilStatus FromFeed(SybilStatusMsg m) =>
            m == null ? null : new SybilStatus { Jailed = m.Jailed, ReasonId = m.ReasonId, ReleaseAtMs = m.ReleaseAtMs };

        private void OnSybil(SybilStatusMsg m) { if (Alive) LoadAll(); }

        private void LoadAll()
        {
            Load(_jail, ct => Hub.Api.Sybil(ct), RenderJail);
            Load(_farm, ct => Hub.Api.Farm(ct), RenderFarm);
            Load(_history, ct => Hub.Api.Claims(ct), RenderHistory);
        }

        // ---------------------------------------------------------------- jail

        private void RenderJail(VisualElement body, SybilStatus s)
        {
            _sybil = s;
            _jailCountdown = null;
            if (!s.Jailed) return;
            var card = CompUi.Card(Duo.Notice(CompStrings.T("sybil.jailed") + " " + FarmRules.JailReasonLabel(s.ReasonId), Duo.Tone.Danger));
            card.name = "farm-jail";
            if (s.ReleaseAtMs.HasValue)
            {
                _jailCountdown = CompUi.Small("");
                card.Add(_jailCountdown);
                UpdateJail();
            }
            else card.Add(CompUi.Small(CompStrings.T("sybil.noRelease")));
            card.Add(CompUi.Small(CompStrings.T("sybil.rules")));
            if (s.Bailable && s.BailCents.HasValue)
            {
                var bail = CompUi.Btn(CompStrings.F("sybil.bail", Money.Cash(s.BailCents.Value)), () => BailSheet(s.BailCents.Value), CompUi.Plain);
                bail.name = "farm-bail";
                card.Add(CompUi.Row(bail));
            }
            else card.Add(CompUi.Small(CompStrings.T("sybil.notBailable")));
            body.Add(card);
        }

        private void BailSheet(long cents)
        {
            var sheet = new DuoSheet(CompStrings.T("sybil.jailed"), CompStrings.F("sybil.bail.confirm", Money.Cash(cents)));
            sheet.name = "farm-bail-sheet";
            var status = CompUi.Small("");
            sheet.AddContent(status);
            Button go = null;
            go = CompUi.Btn(CompStrings.T("sybil.bail.yes"), () => { go.SetEnabled(false); Bail(status, sheet, go); });
            sheet.AddContent(go);
            sheet.AddSecondary(CompStrings.T("common.cancel"));
            ShowSheet(sheet);
        }

        private void Bail(Label status, DuoSheet sheet, Button go)
        {
            var key = Hub.KeyFor("sybil:bail");
            var k = key.KeyFor("POST", "/me/sybil-status/bail", "");
            status.text = CompStrings.T("common.working");
            Run(ct => Hub.Api.Bail(k, ct), s =>
            {
                key.Complete();
                sheet.Close();
                Hub.Profile?.Refresh();
                Bus.Publish(ToastSignal.Text(CompStrings.T(s.Jailed ? "sybil.bail.partial" : "sybil.bail.done"), s.Jailed ? ToastKind.Info : ToastKind.Good));
                LoadAll();
            }, e =>
            {
                key.Settle(e);
                status.text = e.Message;
                go.SetEnabled(true);
            }, true);
        }

        private void UpdateJail()
        {
            if (_jailCountdown == null || _sybil?.ReleaseAtMs == null) return;
            long left = _sybil.ReleaseAtMs.Value - Hub.NowMs;
            _jailCountdown.text = left > 0 ? CompStrings.F("sybil.releaseIn", CompTime.Countdown(left)) : CompStrings.T("sybil.releaseDue");
        }

        // ---------------------------------------------------------------- farm

        private void RenderFarm(VisualElement body, FarmStatus f)
        {
            _status = f;
            _claimCountdown = null;
            if (_sybil == null && f.Sybil != null) _sybil = f.Sybil;
            // Hero: today's drop as the server computed it (base + bonuses, capped).
            var hero = Duo.Hero(CompStrings.T("farm.today") + " · " + f.DayDate, Money.Cash(f.TodayAmountCents),
                CompStrings.F("farm.streakDays", f.StreakDays), f.StreakDays > 0 ? true : (bool?)null, CompStrings.T("farm.streak"));
            hero.name = "farm-hero";
            body.Add(hero);
            _claimCountdown = CompUi.Small("");
            body.Add(_claimCountdown);
            var status = CompUi.Small("");
            _claimBtn = CompUi.Btn(CompStrings.T("farm.claim"), () => Claim(status));
            _claimBtn.name = "farm-claim";
            body.Add(_claimBtn);
            body.Add(status);
            body.Add(CompUi.Pair(CompStrings.T("farm.base"), Money.Cash(f.BaseCents)));
            if (f.SgtBonusBps != 0) body.Add(CompUi.Pair(CompStrings.T("farm.sgt"), Money.Bps(f.SgtBonusBps), "duo-value-positive"));
            if (f.TribeBonusBps != 0) body.Add(CompUi.Pair(CompStrings.T("farm.tribe"), Money.Bps(f.TribeBonusBps), "duo-value-positive"));
            if (f.DailyCapCents > 0) body.Add(CompUi.Pair(CompStrings.T("farm.cap"), Money.Cash(f.DailyCapCents)));
            UpdateClaim();
        }

        private void UpdateClaim()
        {
            if (_status == null || _claimBtn == null) return;
            long now = Hub.NowMs;
            bool can = FarmRules.CanClaim(_status, _sybil, now);
            _claimBtn.SetEnabled(can);
            if (_claimCountdown == null) return;
            if (_sybil?.Jailed == true) _claimCountdown.text = CompStrings.T("farm.jailed");
            else if (can) _claimCountdown.text = CompStrings.T(_status.ClaimedToday ? "farm.ready" : "farm.available");
            else _claimCountdown.text = CompStrings.F("farm.nextIn", CompTime.Countdown(FarmRules.MsUntilNextClaim(_status, now)));
        }

        private void Claim(Label status)
        {
            var key = Hub.KeyFor("airdrop:claim");
            var k = key.KeyFor("POST", "/airdrop/claim", _status?.DayDate ?? "");
            status.text = CompStrings.T("common.working");
            _claimBtn?.SetEnabled(false);
            Run(ct => Hub.Api.Claim(k, ct), c =>
            {
                key.Complete();
                Hub.Profile?.Refresh();
                Bus.Publish(ToastSignal.Text(CompStrings.F("farm.claimed", Money.Cash(c.AmountCents)), ToastKind.Good));
                LoadAll();
            }, e =>
            {
                key.Settle(e);
                status.text = e.Message;
                if (e.Is("SYBIL_JAILED")) LoadAll();
                else UpdateClaim();
            }, true);
        }

        private void RenderHistory(VisualElement body, List<AirdropClaim> claims)
        {
            if (claims.Count == 0) { body.Add(CompUi.Small(CompStrings.T("farm.history.empty"))); return; }
            var list = El.Div("duo-list");
            foreach (var c in claims.OrderByDescending(x => x.DayDate, StringComparer.Ordinal).Take(30))
            {
                var row = new DuoRow().Title(c.DayDate).Value(Money.Cash(c.AmountCents), "duo-value-positive");
                if (c.SgtBonus) row.Subtitle(CompStrings.T("farm.sgtShort"));
                row.AddToClassList("duo-row-static");
                list.Add(row);
            }
            Duo.MarkLast(list);
            body.Add(list);
        }

        // ---------------------------------------------------------------- barn

        private void LoadBarn() => Load(_barn, ct => Hub.Api.Barn(ct), RenderBarn);

        private void RenderBarn(VisualElement body, SybilBarn b)
        {
            body.Add(CompUi.Pair(CompStrings.T("sybil.barn.count"), b.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (b.Inmates.Count == 0) { body.Add(CompUi.Small(CompStrings.T("sybil.barn.empty"))); return; }
            var list = El.Div("duo-list");
            foreach (var i in b.Inmates)
            {
                var row = new DuoRow().Leading(Duo.Avatar(i.Alias, i.Alias, "duo-avatar-s")).Title(i.Alias)
                    .Subtitle(i.ReleaseAtMs.HasValue ? CompStrings.F("sybil.releaseAt", CompTime.When(i.ReleaseAtMs.Value)) : CompStrings.T("sybil.noRelease"));
                row.AddToClassList("duo-row-static");
                list.Add(row);
            }
            Duo.MarkLast(list);
            body.Add(list);
        }

        protected override void Tick()
        {
            UpdateJail();
            UpdateClaim();
        }
    }
}
