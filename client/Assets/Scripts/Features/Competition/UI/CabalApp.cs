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
    /// Phone app "Cabal" (id `cabal`): the Cabal Lounge door (membership, invites in and out, accepting an invite) and the net-worth gate
    /// messaging for Dubai Marina and the Cabal Lounge. The server decides every gate and every invite; this screen only asks, and shows
    /// the answer. It never shows or accepts a bearer invite code: invites are named for one guest and accepted by id.
    /// </summary>
    public sealed class CabalApp : CompApp
    {
        public override string Id => CompetitionLinks.CabalApp;
        public override string Title => CompStrings.T("app.cabal");
        public override string Monogram => "CB";
        public override string Color => "#8854d0";

        internal override CompScreen CreateView(CompetitionHub hub) => new CabalScreen(hub);
    }

    internal sealed class CabalScreen : CompScreen
    {
        private readonly VisualElement _door = El.Div(), _gates = El.Div(), _mine = El.Div(), _sent = El.Div(), _host = El.Div();
        private CabalMembership _membership;
        private string _guestId, _guestName;

        public CabalScreen(CompetitionHub hub) : base(hub)
        {
            CompetitionLinks.TakePendingCabalGuest(out _guestId, out _guestName);
            Root.Add(CompUi.H1(CompStrings.T("app.cabal")));
            Root.Add(CompUi.Small(CompStrings.T("cabal.note")));
            Root.Add(_door);
            Root.Add(_host);
            Root.Add(CompUi.H2(CompStrings.T("cabal.gates")));
            Root.Add(_gates);
            Root.Add(CompUi.H2(CompStrings.T("cabal.invitesForMe")));
            Root.Add(_mine);
            Root.Add(CompUi.H2(CompStrings.T("cabal.invitesSent")));
            Root.Add(_sent);
            Hub.Feed.CabalInviteEvent += OnInvite;
            LoadAll();
        }

        protected override void OnDisposed() { Hub.Feed.CabalInviteEvent -= OnInvite; }

        private void OnInvite(CabalInviteMsg m) { if (Alive) Load(_mine, ct => Hub.Api.CabalInvites("guest", Hub.UserId, ct), RenderMine); }

        private void LoadAll()
        {
            Load(_door, ct => Hub.Api.MyCabal(ct), RenderDoor);
            LoadGates();
            Load(_mine, ct => Hub.Api.CabalInvites("guest", Hub.UserId, ct), RenderMine);
            Load(_sent, ct => Hub.Api.CabalInvites("host", Hub.UserId, ct), RenderSent);
        }

        // ---------------------------------------------------------------- door + host

        private void RenderDoor(VisualElement body, CabalMembership m)
        {
            _membership = m;
            var card = CompUi.Card(CompUi.H2(CompStrings.T(m.Member ? "cabal.member" : "cabal.outsider")));
            if (m.Member)
            {
                if (m.SinceMs.HasValue) card.Add(CompUi.Pair(CompStrings.T("cabal.since"), CompTime.Date(m.SinceMs.Value)));
                if (m.ExpiresAtMs.HasValue) card.Add(CompUi.Pair(CompStrings.T("cabal.expires"), CompTime.Date(m.ExpiresAtMs.Value)));
                card.Add(CompUi.Pair(CompStrings.T("cabal.invitesLeft"), m.InvitesRemaining.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            else card.Add(CompUi.Text(CompStrings.T("cabal.howToJoin")));
            if (m.HostGateCents > 0) card.Add(CompUi.Pair(CompStrings.T("cabal.hostGate"), Money.Cash(m.HostGateCents)));
            body.Add(card);
            RenderHost();
        }

        private void RenderHost()
        {
            _host.Clear();
            if (_guestId == null) return;
            var card = CompUi.Card(CompUi.H2(CompStrings.F("cabal.inviteTitle", _guestName)));
            var block = GateRules.HostBlock(_membership);
            var status = CompUi.Small("");
            if (block != null) card.Add(CompUi.Text(CompStrings.T(block)));
            var send = CompUi.Btn(CompStrings.F("cabal.invite", _guestName), () => SendInvite(status));
            // The server is the judge (member, quota, host net-worth gate); the button is only hidden when we already know the answer is no.
            send.SetEnabled(block == null);
            card.Add(send);
            card.Add(status);
            _host.Add(card);
        }

        private void SendInvite(Label status)
        {
            var guest = _guestId;
            var key = Hub.KeyFor("cabal:invite");
            var k = key.KeyFor("POST", "/cabal/invites", guest);
            status.text = CompStrings.T("common.working");
            Run(ct => Hub.Api.Invite(guest, k, ct), i =>
            {
                key.Complete();
                _guestId = _guestName = null;
                Bus.Publish(ToastSignal.Text(CompStrings.T("cabal.invite.sent"), ToastKind.Good));
                LoadAll();
                RenderHost();
            }, e =>
            {
                key.Settle(e);
                status.text = e.Message;
            }, true);
        }

        // ---------------------------------------------------------------- gates

        private void LoadGates()
        {
            _gates.Clear();
            foreach (var id in new[] { "dubai_marina", "cabal_lounge" })
            {
                var venue = id;
                var line = CompUi.Card(CompUi.H2(CompStrings.T("venue." + venue)));
                var status = CompUi.Small(CompStrings.T("common.loading"));
                line.Add(status);
                _gates.Add(line);
                Run(ct => Hub.Api.Access(venue, ct), a =>
                {
                    if (a.Allowed) status.text = CompStrings.T("gate.open");
                    else
                    {
                        status.text = GateRules.Message(a);
                        var short_ = GateRules.Shortfall(a);
                        if (short_.HasValue) line.Add(CompUi.Small(CompStrings.F("gate.shortfall", Money.Cash(short_.Value))));
                    }
                    if (a.NetWorthCents > 0) line.Add(CompUi.Pair(CompStrings.T("gate.yourNetWorth"), Money.Cash(a.NetWorthCents)));
                }, e =>
                {
                    status.text = e.IsUnavailable ? CompStrings.T("gate.unavailable") : e.Message;
                }, false);
            }
        }

        // ---------------------------------------------------------------- invites

        private void RenderMine(VisualElement body, List<CabalInvite> invites)
        {
            var now = Hub.NowMs;
            var pending = invites.Where(i => GateRules.InviteIsPendingFor(i, Me, now)).ToList();
            // A push can arrive before the list reflects it; show it from its id alone, the server confirms on accept.
            foreach (var pushed in Hub.Feed.CabalInvites)
                if (pending.All(p => p.InviteId != pushed.InviteId) && invites.All(i => i.InviteId != pushed.InviteId) && (pushed.ExpiresAtMs == 0 || pushed.ExpiresAtMs > now))
                    pending.Add(new CabalInvite { InviteId = pushed.InviteId, HostUserId = pushed.FromUserId, GuestUserId = Me, Status = "pending", ExpiresAtMs = pushed.ExpiresAtMs == 0 ? (long?)null : pushed.ExpiresAtMs });
            if (pending.Count == 0) { body.Add(CompUi.Small(CompStrings.T("cabal.none"))); return; }
            foreach (var inv in pending)
            {
                var invite = inv;
                var card = CompUi.Card();
                var who = CompUi.Text("…");
                Run(ct => Hub.AliasOf(invite.HostUserId, ct), a => who.text = CompStrings.F("cabal.from", a), null, false);
                card.Add(who);
                if (invite.ExpiresAtMs.HasValue) card.Add(CompUi.Small(CompStrings.F("cabal.expiresAt", CompTime.When(invite.ExpiresAtMs.Value))));
                var status = CompUi.Small("");
                card.Add(CompUi.Btn(CompStrings.T("cabal.accept"), () => Accept(invite, status)));
                card.Add(status);
                body.Add(card);
            }
        }

        private void Accept(CabalInvite invite, Label status)
        {
            var key = Hub.KeyFor("cabal:accept:" + invite.InviteId);
            var k = key.KeyFor("POST", "/cabal/invites/" + invite.InviteId + "/accept", "");
            status.text = CompStrings.T("common.working");
            Run(ct => Hub.Api.AcceptInvite(invite.InviteId, k, ct), i =>
            {
                key.Complete();
                Hub.Feed.CabalInvites.RemoveAll(x => x.InviteId == invite.InviteId);
                Bus.Publish(ToastSignal.Text(CompStrings.T("cabal.accepted"), ToastKind.Good));
                LoadAll();
            }, e =>
            {
                key.Settle(e);
                status.text = e.Message;
                if (e.Is("INVITE_INVALID")) Hub.Feed.CabalInvites.RemoveAll(x => x.InviteId == invite.InviteId);
            }, true);
        }

        private void RenderSent(VisualElement body, List<CabalInvite> invites)
        {
            if (invites.Count == 0) { body.Add(CompUi.Small(CompStrings.T("cabal.sent.none"))); return; }
            foreach (var inv in invites.OrderByDescending(i => i.CreatedAtMs ?? 0).Take(20))
            {
                var invite = inv;
                var card = CompUi.Card();
                var who = CompUi.Text("…");
                Run(ct => Hub.AliasOf(invite.GuestUserId, ct), a => who.text = CompStrings.F("cabal.to", a), null, false);
                card.Add(who);
                card.Add(CompUi.Small(CompStrings.T("cabal.status." + (CompStrings.Has("cabal.status." + invite.Status) ? invite.Status : "unknown"))));
                if (invite.Status == "pending")
                {
                    var status = CompUi.Small("");
                    card.Add(CompUi.Btn(CompStrings.T("cabal.revoke"), () => Revoke(invite, status), "ct-btn"));
                    card.Add(status);
                }
                body.Add(card);
            }
        }

        private void Revoke(CabalInvite invite, Label status)
        {
            var key = Hub.KeyFor("cabal:revoke:" + invite.InviteId);
            var k = key.KeyFor("POST", "/cabal/invites/" + invite.InviteId + "/revoke", "");
            status.text = CompStrings.T("common.working");
            Run(ct => Hub.Api.RevokeInvite(invite.InviteId, k, ct), _ => { key.Complete(); LoadAll(); }, e => { key.Settle(e); status.text = e.Message; }, true);
        }
    }
}
