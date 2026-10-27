using System;
using System.Collections.Generic;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;
using Motion = CTLife.UI.Kit.Motion;

namespace CTLife.CityLife.Casino
{
    /// <summary>
    /// Coinflip (`POST /casino/coinflip/bet`, head-to-head `…/challenges`): our gold coin, BULL (heads) or BEAR (tails).
    /// Against the house a win pays 1.9x; the coin is tossed once the server has flipped it and lands on `outcome.result`.
    /// Head-to-head (bragging rights, no Cash: backend #25): challenge someone from your DMs (120 s to accept, polled), incoming challenges with a
    /// countdown ring and Accept / Decline, your sent ones with Cancel, and the last results. An accepted challenge is tossed
    /// on both phones (the challenger's when the poll sees it settle).
    /// </summary>
    public sealed class CoinflipPanel : RugPanel
    {
        private readonly VisualElement _coin, _shadow;
        private readonly ScrollView _list;
        private readonly Label _banner, _vs;
        private readonly Button _flip, _heads, _tails;
        private readonly StakeChips _chips;
        private readonly CelebrationBurst _burst;
        private readonly Dictionary<string, string> _statusSeen = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _keys = new Dictionary<string, string>();
        private readonly HashSet<string> _answering = new HashSet<string>();
        private readonly List<(Ring ring, CasinoChallenge c)> _rings = new List<(Ring, CasinoChallenge)>();
        private string _side = CasinoGames.Heads, _face = CasinoGames.Heads, _key;
        private bool _requesting, _tossing;
        private float _tossAt = -1, _clock;
        private double _angle0, _angleTotal;
        private string _target;
        private Action _landed;

        public override bool Busy => _requesting || _tossing;

        public CoinflipPanel(RugRoomSheet sheet) : base(sheet)
        {
            name = "rr-coinflip";
            // ---- the stage
            var stage = El.Div("rr-coin-stage");
            _vs = El.Text(CasinoText.T("flip.house"), "rr-coin-vs");
            _banner = El.Text(CasinoText.T("flip.title"), "rr-coin-banner");
            _banner.name = "rr-coin-banner";
            var area = El.Div("rr-coin-area");
            _shadow = El.Div("rr-coin-shadow");
            _coin = El.Div("rr-coin");
            _coin.name = "rr-coin";
            area.Add(_shadow); area.Add(_coin);
            stage.Add(_vs); stage.Add(_banner); stage.Add(area);
            var pick = El.Div("rr-card");
            pick.style.width = 620; pick.style.marginLeft = 28; pick.style.flexShrink = 0; pick.style.alignSelf = Align.Center;
            var sides = El.Div("rr-sides");
            _heads = SideCard(CasinoGames.Heads);
            _tails = SideCard(CasinoGames.Tails);
            sides.Add(_heads); sides.Add(_tails);
            pick.Add(sides);
            _chips = new StakeChips(sheet);
            _chips.Changed += Refresh;
            pick.Add(_chips);
            _flip = RugRoomSheet.Btn("", Flip, "rr-cta rr-cta--gold");
            _flip.name = "rr-flip";
            _flip.Add(El.Text(CasinoText.T("flip.flip"), "rr-cta-label"));
            _flip.Add(El.Text("", "rr-cta-sub"));
            foreach (var e in _flip.Children()) e.pickingMode = PickingMode.Ignore;
            pick.Add(_flip);
            Add(stage);
            Add(pick);

            // ---- head-to-head
            var side = El.Div("rr-side");
            side.style.width = 600;
            var card = El.Div("rr-card");
            card.style.flexGrow = 1;
            card.Add(El.Text(CasinoText.T("flip.h2h"), "rr-h"));
            var note = El.Text("", "rr-note");
            note.name = "rr-h2h-note";
            card.Add(note);
            var challenge = RugRoomSheet.Plain(CasinoText.T("flip.challenge"), OpenPicker, "mint");
            challenge.name = "rr-challenge";
            challenge.style.marginTop = 14; challenge.style.height = 84;
            card.Add(challenge);
            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.AddToClassList("rr-h2h");
            _list.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _list.name = "rr-h2h-list";
            card.Add(_list);
            side.Add(card);
            Add(side);

            _burst = new CelebrationBurst(coins: 26, confetti: 50, puffs: 0);
            Add(_burst);
            SetFace(CasinoGames.Heads);
            Svc.ChallengesChanged += RenderList;
            Refresh();
            RenderList();
        }

        private Button SideCard(string side)
        {
            var b = RugRoomSheet.Btn("", () => { _side = side; Cues.Select(); Refresh(); }, "rr-sidecard");
            b.name = "rr-side-" + side;
            var art = El.Div("rr-sidecard-art");
            var tex = RugRoomSheet.Art(side == CasinoGames.Heads ? "UI/Casino/coin_heads" : "UI/Casino/coin_tails");
            if (tex != null) art.style.backgroundImage = new StyleBackground(tex);
            b.Add(art);
            b.Add(El.Div(null, El.Text(CasinoText.Side(side), "rr-sidecard-name"), El.Text(CasinoText.T(side == CasinoGames.Heads ? "flip.headsSub" : "flip.tailsSub").ToUpperInvariant(), "rr-sidecard-sub")));
            foreach (var e in b.Query<VisualElement>().ToList()) if (e != b) e.pickingMode = PickingMode.Ignore;
            return b;
        }

        private void Refresh()
        {
            _heads.EnableInClassList("rr-sidecard--on", _side == CasinoGames.Heads);
            _tails.EnableInClassList("rr-sidecard--on", _side == CasinoGames.Tails);
            var sub = _flip.Q<Label>(className: "rr-cta-sub");
            if (sub != null) sub.text = CasinoText.Money(Sheet.StakeCents) + "  ·  " + CasinoText.F("flip.pays", (State?.CoinflipPayout ?? 1.9).ToString("0.0#"));
            _flip.EnableInClassList("rr-cta--busy", Busy);
            var note = this.Q<Label>("rr-h2h-note");
            if (note != null) note.text = CasinoText.F("flip.h2hNote", State?.ChallengeTtlSeconds ?? 120);
        }

        public override void StateChanged() { _chips.Refresh(); Refresh(); }

        public override void Shown() { _ = Svc.RefreshChallenges(); }

        // ------------------------------------------------------------------ vs the house

        private async void Flip()
        {
            if (Busy) return;
            var s = State;
            if (s == null || !s.AgeConfirmed || s.Excluded(ServerClock.NowMs)) { Cues.Nope(); return; }
            long stake = Sheet.StakeCents;
            if (stake > s.StakeRoomCents) { Sheet.Toast(CasinoText.T(s.StakeRemainingCents < stake ? "err.STAKE_CAP_REACHED" : "err.LOSS_CAP_REACHED")); Cues.Nope(); return; }
            _requesting = true;
            _key ??= Guid.NewGuid().ToString();
            _vs.text = CasinoText.T("flip.house");
            _banner.text = CasinoText.Side(_side) + "…";
            _banner.RemoveFromClassList("rr-coin-banner--won");
            Refresh();
            Cues.Play("sfx_chips", 0.7f);
            DeviceFeel.Tick();
            Log.Info("casino", $"coinflip {stake} {_side}");
            try
            {
                var bet = await Svc.Api.Flip(stake, _side, _key, Svc.Life);
                _key = null;
                _requesting = false;
                var r = bet.Round;
                Log.Info("casino", $"coinflip {r.RoundId} result {r.Result} won {r.Won} payout {r.PayoutCents}");
                Toss(r.Result, () =>
                {
                    Svc.AfterBet(bet, bet.CashCents);
                    Outcome(r.Won, r.Result, r.PayoutCents ?? 0, null);
                });
            }
            catch (OperationCanceledException) { _requesting = false; }
            catch (Exception e)
            {
                _requesting = false;
                var err = CityErrors.From(e, true);
                if (!err.OutcomeUnknown) _key = null;
                _banner.text = CasinoText.T("flip.title");
                Sheet.Fail(err);
            }
            Refresh();
        }

        private void Outcome(bool won, string result, long payoutCents, string vs)
        {
            if (vs != null) _vs.text = vs;
            if (won)
            {
                _banner.text = CasinoText.F("flip.won", CasinoText.Side(result), CasinoText.Money(payoutCents));
                _banner.AddToClassList("rr-coin-banner--won");
                _burst.Coins(_burst.WorldToLocal(_coin.worldBound.center), 22);
                Cues.Play("sfx_slot_win", 0.9f);
                DeviceFeel.Confirm();
            }
            else
            {
                _banner.text = CasinoText.F("flip.lost", CasinoText.Side(result));
                _banner.RemoveFromClassList("rr-coin-banner--won");
                DeviceFeel.Reject();
            }
            Motion.Pop(_banner, 1.1f, 0.25f);
        }

        /// <summary>A head-to-head result: bragging rights, no Cash line (nothing was staked).</summary>
        private void H2HOutcome(bool won, string result, string vs)
        {
            if (vs != null) _vs.text = vs;
            _banner.text = CasinoText.F(won ? "flip.h2hWon" : "flip.h2hLost", CasinoText.Side(result));
            _banner.EnableInClassList("rr-coin-banner--won", won);
            if (won) { _burst.Confetti(40); Cues.Play("sfx_slot_win", 0.9f); DeviceFeel.Confirm(); }
            else DeviceFeel.Reject();
            Motion.Pop(_banner, 1.1f, 0.25f);
        }

        // ------------------------------------------------------------------ the toss

        private void SetFace(string face)
        {
            _face = face;
            var tex = RugRoomSheet.Art(face == CasinoGames.Tails ? "UI/Casino/coin_tails" : "UI/Casino/coin_heads");
            if (tex != null) _coin.style.backgroundImage = new StyleBackground(tex);
        }

        /// <summary>Toss the coin so it lands on `result` (half-turns chosen by parity), then run `landed`.</summary>
        private void Toss(string result, Action landed)
        {
            _tossing = true;
            _target = result;
            _landed = landed;
            _tossAt = _clock;
            _angle0 = 0;
            int halfTurns = 11 + (result == _face ? 1 : 0);   // even: the same face up again
            _angleTotal = 180.0 * halfTurns;
            Cues.Play("sfx_coin_flip");
            Refresh();
        }

        public override void Tick(float dt)
        {
            _clock += dt;
            if (_requesting)
            {
                // charging: the coin trembles while the server flips
                float k = Mathf.Sin(_clock * 30f) * 2f;
                _coin.style.rotate = new Rotate(k);
            }
            if (_tossing && _tossAt >= 0)
            {
                const float D = 1.5f;
                float e = _clock - _tossAt;
                float u = Mathf.Clamp01(e / D);
                float h = 150f * 4f * u * (1f - u);
                double ang = _angle0 + _angleTotal * Ease.OutCubic(u);
                double c = Math.Cos(ang * Math.PI / 180.0);
                int half = (int)Math.Floor((ang + 90.0) / 180.0);
                string start = _face;
                string shown = half % 2 == 0 ? start : (start == CasinoGames.Heads ? CasinoGames.Tails : CasinoGames.Heads);
                var tex = RugRoomSheet.Art(shown == CasinoGames.Tails ? "UI/Casino/coin_tails" : "UI/Casino/coin_heads");
                if (tex != null) _coin.style.backgroundImage = new StyleBackground(tex);
                float sy = Mathf.Max(0.06f, (float)Math.Abs(c));
                _coin.style.scale = new Scale(new Vector3(1f, sy, 1f));
                _coin.style.translate = new Translate(0, -h);
                _coin.style.rotate = new Rotate(0);
                float sh = 1f - 0.55f * (h / 150f);
                _shadow.style.scale = new Scale(new Vector3(sh, sh, 1f));
                _shadow.style.opacity = 0.35f + 0.65f * sh;
                if (u >= 1f)
                {
                    _tossing = false; _tossAt = -1;
                    _coin.style.scale = new Scale(Vector3.one);
                    _coin.style.translate = new Translate(0, 0);
                    SetFace(_target);
                    Cues.Play("sfx_coin_land");
                    Motion.Pop(_coin, 1.06f, 0.2f);
                    var cb = _landed; _landed = null;
                    cb?.Invoke();
                    Refresh();
                }
            }
            if (Time.frameCount % 10 == 0) TickRings();
        }

        // ------------------------------------------------------------------ head-to-head

        private void RenderList()
        {
            _list.Clear();
            _rings.Clear();
            var all = Svc.Challenges;
            var incoming = all.FindAll(c => c.Incoming && c.Open);
            var sent = all.FindAll(c => c.Mine && c.Open);
            var recent = all.FindAll(c => !c.Open);
            if (incoming.Count > 0) { _list.Add(Cap(CasinoText.T("flip.incoming"))); foreach (var c in incoming) _list.Add(Row(c)); }
            if (sent.Count > 0) { _list.Add(Cap(CasinoText.T("flip.outgoing"))); foreach (var c in sent) _list.Add(Row(c)); }
            if (recent.Count > 0) { _list.Add(Cap(CasinoText.T("flip.recent"))); for (int i = 0; i < Math.Min(6, recent.Count); i++) _list.Add(Row(recent[i])); }
            if (all.Count == 0) _list.Add(El.Text(CasinoText.T("flip.none"), "rr-note"));
            // a challenge I sent got accepted: toss it here too
            foreach (var c in all)
            {
                bool had = _statusSeen.TryGetValue(c.ChallengeId, out var before);
                _statusSeen[c.ChallengeId] = c.Status;
                if (had && before == "open" && c.Status == "settled" && c.Mine && parent != null && !Busy)
                {
                    Log.Info("casino", $"challenge {c.ChallengeId} accepted by {c.OpponentAlias}: {c.Result}");
                    bool won = c.IWon(Svc.UserId);
                    var vs = CasinoText.F("flip.vs", "@" + Alias(c.ChallengerAlias), "@" + Alias(c.OpponentAlias));
                    _vs.text = vs;
                    Toss(c.Result, () => H2HOutcome(won, c.Result, vs));
                }
            }
            TickRings();
        }

        private static string Alias(string a) => string.IsNullOrEmpty(a) ? CasinoText.T("flip.anon") : a;

        private VisualElement Row(CasinoChallenge c)
        {
            var row = El.Div("rr-ch");
            row.name = "rr-ch-" + c.ChallengeId;
            if (c.Open)
            {
                var ring = new Ring();
                _rings.Add((ring, c));
                row.Add(ring);
            }
            else
            {
                var art = El.Div("rr-sidecard-art");
                art.style.width = 56; art.style.height = 56; art.style.marginRight = 14;
                var tex = RugRoomSheet.Art(c.Result == CasinoGames.Tails ? "UI/Casino/coin_tails" : "UI/Casino/coin_heads");
                if (tex != null && c.Result != null) art.style.backgroundImage = new StyleBackground(tex);
                row.Add(art);
            }
            var title = El.Text("@" + Alias(c.PeerAlias), "rr-ch-title");
            title.enableRichText = false;
            var sub = El.Text(CasinoText.F("flip.youAre", CasinoText.Side(c.MySide)), "rr-ch-sub");
            var copy = El.Div("rr-ch-copy", title, sub);
            row.Add(copy);
            if (c.Open)
            {
                var actions = El.Div("rr-ch-actions");
                if (c.Incoming)
                {
                    var acc = RugRoomSheet.Plain(CasinoText.T("flip.accept"), () => Answer(c, "accept"), "mint");
                    acc.name = "rr-accept-" + c.ChallengeId;
                    actions.Add(RugRoomSheet.Plain(CasinoText.T("flip.decline"), () => Answer(c, "decline"), ""));
                    actions.Add(acc);
                }
                else
                {
                    var cancel = RugRoomSheet.Plain(CasinoText.T("flip.cancel"), () => Answer(c, "cancel"), "danger");
                    cancel.name = "rr-cancel-" + c.ChallengeId;
                    actions.Add(cancel);
                }
                row.Add(actions);
            }
            else
            {
                string key = c.Status == "settled" ? (c.IWon(Svc.UserId) ? "flip.status.settled.won" : "flip.status.settled.lost") : "flip.status." + c.Status;
                var st = El.Text(CasinoText.Has(key) ? CasinoText.T(key) : c.Status, "rr-ch-status");
                st.EnableInClassList("rr-ch-status--won", c.Status == "settled" && c.IWon(Svc.UserId));
                row.Add(st);
            }
            return row;
        }

        private void TickRings()
        {
            double now = ServerClock.NowMs;
            int ttl = State?.ChallengeTtlSeconds ?? 120;
            foreach (var (ring, c) in _rings)
            {
                double left = c.SecondsLeft(now);
                ring.Set((float)(left / ttl), CasinoText.F("flip.expiresIn", Math.Ceiling(left)));
            }
        }

        private async void Answer(CasinoChallenge c, string verb)
        {
            if (_answering.Contains(c.ChallengeId) || (verb == "accept" && Busy)) return;
            if (verb == "accept")
            {
                _requesting = true;
                _vs.text = CasinoText.F("flip.vs", "@" + Alias(c.ChallengerAlias), "@" + Alias(c.OpponentAlias));
                _banner.text = CasinoText.Side(c.MySide) + "…";
                Refresh();
            }
            _answering.Add(c.ChallengeId);
            string k = c.ChallengeId + ":" + verb;
            if (!_keys.TryGetValue(k, out var key)) _keys[k] = key = Guid.NewGuid().ToString();
            Log.Info("casino", $"challenge {verb} {c.ChallengeId}");
            try
            {
                var reply = await Svc.Api.Answer(c.ChallengeId, verb, key, Svc.Life);
                _keys.Remove(k);
                Svc.Apply(reply);
                var ch = reply.Challenge;
                _statusSeen[ch.ChallengeId] = ch.Status;
                Svc.Upsert(ch);
                if (verb == "accept")
                {
                    _requesting = false;
                    bool won = ch.IWon(Svc.UserId);
                    var vs = CasinoText.F("flip.vs", "@" + Alias(ch.ChallengerAlias), "@" + Alias(ch.OpponentAlias));
                    Log.Info("casino", $"challenge accepted {ch.ChallengeId}: {ch.Result} won {won}");
                    Toss(ch.Result, () => { Svc.Apply(reply); H2HOutcome(won, ch.Result, vs); });
                }
                else
                {
                    Sheet.Toast(CasinoText.T(verb == "cancel" ? "flip.status.cancelled" : "flip.status.declined"), good: true);
                }
            }
            catch (OperationCanceledException) { _requesting = false; }
            catch (Exception e)
            {
                _requesting = false;
                var err = CityErrors.From(e, true);
                if (!err.OutcomeUnknown) _keys.Remove(k);
                if (verb == "accept") _banner.text = CasinoText.T("flip.title");
                if (err.Is("CHALLENGE_EXPIRED")) { Sheet.Toast(CasinoText.T("flip.expired")); _ = Svc.RefreshChallenges(); }
                else if (err.Is("CHALLENGE_NOT_OPEN")) { Sheet.Toast(CasinoText.T("err.CHALLENGE_NOT_OPEN")); _ = Svc.RefreshChallenges(); }
                else Sheet.Fail(err);
            }
            finally { _answering.Remove(c.ChallengeId); }
            Refresh();
        }

        // ------------------------------------------------------------------ the challenge picker

        private void OpenPicker()
        {
            var s = State;
            if (s == null || !s.AgeConfirmed || s.Excluded(ServerClock.NowMs)) { Cues.Nope(); return; }
            var card = El.Div("rr-modal-card");
            card.name = "rr-picker";
            card.Add(El.Text(CasinoText.T("flip.pick"), "rr-modal-title"));
            card.Add(El.Text(CasinoText.F("flip.pickSub", CasinoText.Side(_side)), "rr-modal-body"));
            var peers = new ScrollView(ScrollViewMode.Vertical);
            peers.AddToClassList("rr-peers");
            peers.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            card.Add(peers);
            string chosen = null, chosenAlias = null;
            Button send = null;
            VisualElement modal = null;
            string key = Guid.NewGuid().ToString();
            send = RugRoomSheet.Plain(CasinoText.T("flip.send"), async () =>
            {
                if (chosen == null) { Cues.Nope(); return; }
                send.SetEnabled(false);
                try
                {
                    var reply = await Svc.Api.Challenge(chosen, _side, key, Svc.Life);
                    Svc.Apply(reply);
                    _statusSeen[reply.Challenge.ChallengeId] = reply.Challenge.Status;
                    Svc.Upsert(reply.Challenge);
                    Log.Info("casino", $"challenge sent {reply.Challenge.ChallengeId} to {chosenAlias} {reply.Challenge.ChallengerSide}");
                    Sheet.CloseModal(modal);
                    Sheet.Toast(CasinoText.F("flip.sent", Alias(chosenAlias)), good: true);
                    Cues.Play("sfx_chips");
                }
                catch (Exception e)
                {
                    var err = CityErrors.From(e, true);
                    if (!err.OutcomeUnknown) key = Guid.NewGuid().ToString();
                    send.SetEnabled(true);
                    Sheet.Fail(err);
                }
            }, "mint");
            send.name = "rr-send-challenge";
            send.SetEnabled(false);
            var cancel = RugRoomSheet.Plain(CasinoText.T("lim.keep"), () => Sheet.CloseModal(modal), "");
            card.Add(El.Div("rr-modal-actions", cancel, send));
            modal = Sheet.ShowModal(card);

            void Fill(List<CasinoPeer> list)
            {
                peers.Clear();
                // recent opponents first, then DM peers, once each, never yourself
                var seen = new HashSet<string> { Svc.UserId };
                var rows = new List<CasinoPeer>();
                foreach (var c in Svc.Challenges) if (seen.Add(c.PeerId)) rows.Add(new CasinoPeer { UserId = c.PeerId, Alias = c.PeerAlias });
                foreach (var p in list) if (seen.Add(p.UserId)) rows.Add(p);
                if (rows.Count == 0) { peers.Add(El.Text(CasinoText.T("flip.pickNone"), "rr-note")); return; }
                var buttons = new List<Button>();
                foreach (var p in rows)
                {
                    var peer = p;
                    var initial = string.IsNullOrEmpty(p.Alias) ? "?" : p.Alias.Substring(0, 1).ToUpperInvariant();
                    var b = RugRoomSheet.Btn("", null, "rr-peer");
                    b.name = "rr-peer-" + p.UserId;
                    b.Add(El.Div("rr-peer-dot", El.Text(initial, "rr-peer-initial")));
                    var nm = El.Text("@" + Alias(p.Alias), "rr-peer-name");
                    nm.enableRichText = false;
                    b.Add(nm);
                    foreach (var e in b.Query<VisualElement>().ToList()) if (e != b) e.pickingMode = PickingMode.Ignore;
                    b.clicked += () =>
                    {
                        chosen = peer.UserId; chosenAlias = peer.Alias;
                        foreach (var x in buttons) x.EnableInClassList("rr-peer--on", x == b);
                        send.SetEnabled(true);
                        Cues.Select();
                    };
                    buttons.Add(b);
                    peers.Add(b);
                }
            }
            Fill(new List<CasinoPeer>());
            _ = LoadPeers(Fill);
        }

        private async System.Threading.Tasks.Task LoadPeers(Action<List<CasinoPeer>> fill)
        {
            try { fill(await Svc.Api.Peers(Svc.Life)); }
            catch (Exception e) { Log.Warn("casino", "peers failed: " + CityErrors.From(e, false).Code); }
        }

        public override void Disposed()
        {
            Svc.ChallengesChanged -= RenderList;
            _burst.Stop();
        }
    }
}
