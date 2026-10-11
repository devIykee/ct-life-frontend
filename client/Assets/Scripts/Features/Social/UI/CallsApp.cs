using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// Calls (features #24 free video calls, #25 paid calls), phone app id `calls`: the in-call screen (video tiles over the
    /// world, mic/camera/silence/hang up), incoming invites, people to call (from DMs, follows and recent calls; the contract
    /// has no contacts list), the fan's paid-call flow and the creator's settings.
    /// </summary>
    public sealed class CallsApp : SocialApp
    {
        public override string Id => "calls";
        public override string Title => SocialStrings.T("calls.title");
        public override string Color => "#0fb9b1";
        public override string Feature => Features.Voice;

        /// <summary>Deep links from a profile card: preselect someone to call, or open a paid-call booking.</summary>
        public static string PendingCallee, PendingPaidCreator;

        internal override SocialView CreateView(SocialHub hub, PhoneShell phone)
        {
            var v = new View(hub, phone, PendingCallee, PendingPaidCreator);
            PendingCallee = null; PendingPaidCreator = null;
            return v;
        }

        private sealed class View : SocialView
        {
            private readonly CallController _c;
            private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private string _minutes = "5", _rate, _max;
            private bool? _enabled;
            private string _creatorMsg;
            private bool _creatorBusy, _showCreator;

            public View(SocialHub hub, PhoneShell phone, string callee, string paidCreator) : base(hub, phone)
            {
                _c = hub.Calls;
                Root.AddToClassList("ct-social-calls");
                if (SocialWire.IsSafeId(callee)) _selected.Add(callee);
                if (SocialWire.IsSafeId(paidCreator)) _c.BeginPaid(paidCreator);
                var saved = _c.Creator;
                _rate = saved != null ? UsdcUnits.Format(saved.RatePerMinUsdc, false) : "";
                _max = saved != null ? saved.MaxMinutes.ToString() : "10";
                _enabled = saved?.Enabled;
            }

            protected override bool NeedsClockRender =>
                (_c.Active != null && !_c.Active.IsOver) || _c.Incoming.Count > 0 || (_c.Paid != null && !_c.Paid.Terminal);

            protected override void Render()
            {
                var now = Now;
                if (!string.IsNullOrEmpty(_c.Message)) RenderMessage(_c.Message);
                foreach (var inc in _c.Incoming.ToList()) RenderIncoming(inc, now);
                if (_c.Active != null) RenderCall(_c.Active, now);
                if (_c.Paid != null && !(_c.Active != null && _c.Active.Paid && !_c.Active.IsOver)) RenderPaid(_c.Paid, now);
                if (_c.Active == null || _c.Active.IsOver) RenderPeople();
                RenderCreator();
            }

            private void RenderMessage(string key)
            {
                Body.Add(SocialUi.Notice(SocialStrings.T(key), key.StartsWith("perm.") || key.StartsWith("err.") ? "warn" : "info"));
                if (key.EndsWith("Blocked")) Body.Add(SocialUi.Btn("perm.openSettings", MediaPermission.OpenAppSettings));
            }

            // ---------------------------------------------------------------- incoming

            private void RenderIncoming(IncomingCall inc, double now)
            {
                var from = inc.Call.FromUserId;
                var card = SocialUi.Card(SocialUi.Eyebrow(inc.Paid != null ? "calls.incomingPaid" : "calls.incoming"),
                    SocialUi.H2(SocialStrings.F("calls.from", Hub.Names.Name(from))),
                    SocialUi.Small(SocialStrings.F("calls.answerIn", (int)Math.Ceiling(inc.Call.SecondsToAnswer(now)))));
                if (inc.Paid != null) card.Add(SocialUi.Small(SocialStrings.F("paid.creatorCountdown", (int)Math.Ceiling(inc.Paid.AcceptSecondsLeft(now)))));
                if (!Hub.Follows.Contains(from)) card.Add(SocialUi.Notice(SocialStrings.T("calls.unknownCaller"), "warn"));
                if (inc.Paid?.ErrorKey != null) card.Add(SocialUi.Notice(SocialStrings.T(inc.Paid.ErrorKey), "error"));
                var row = SocialUi.Row(SocialUi.Btn("calls.accept", () => _c.Accept(inc.Call.CallId), true), SocialUi.Danger("calls.decline", () => _c.Decline(inc.Call.CallId)));
                row.SetEnabled(!inc.Working && inc.Call.CanAccept(now));
                card.Add(row);
                card.Add(SocialUi.Ghost("common.block", () => Hub.Block(from, null)));
                Body.Add(card);
            }

            // ---------------------------------------------------------------- in call

            private void RenderCall(CallSession call, double now)
            {
                var media = _c.Media;
                if (call.IsOver)
                {
                    var done = SocialUi.Card(SocialUi.H2(SocialStrings.T("calls.end." + call.EndReason.ToString().ToLowerInvariant())));
                    if (call.Paid && _c.Paid != null) Settlement(done, _c.Paid);
                    done.Add(SocialUi.Btn("calls.done", _c.DismissEnded, true));
                    Body.Add(done);
                    return;
                }
                var head = SocialUi.Row(SocialUi.H2(StatusText(call, now)), El.Div("ct-grow"), SocialUi.Status(SocialStrings.T("common.notRecorded")));
                Body.Add(head);
                if (call.Paid && _c.Paid != null) Body.Add(SocialUi.Small(SocialStrings.F("paid.with", Hub.Names.Name(_c.Paid.PeerId))));

                var tiles = El.Div("ct-social-tiles");
                tiles.Add(Tile(SocialStrings.T("calls.you"), media?.CameraOn == true ? media.LocalPreview : null, media?.LocalSpeaking == true,
                    media != null && !media.MicOn, true, media?.LocalRotation ?? 0));
                if (media != null)
                    foreach (var p in media.Peers)
                        tiles.Add(Tile(Hub.Names.Name(p.Identity), p.HasVideo && !p.CameraMuted ? p.Video : null, p.Speaking, p.MicMuted || !p.HasAudio, false, 0));
                foreach (var invited in call.Invitees.Where(i => media == null || !media.Peers.Any(p => SocialWire.SameId(p.Identity, i))))
                    tiles.Add(Tile(Hub.Names.Name(invited) + " · " + SocialStrings.T("calls.ringing"), null, false, false, false, 0));
                Body.Add(tiles);

                var controls = SocialUi.Row();
                controls.AddToClassList("ct-social-media-controls");
                var connected = media != null && media.Connected;
                var mic = SocialUi.Action(media?.MicOn == true ? "calls.micOn" : "calls.micOff", "mic", _c.ToggleMic, media?.MicOn != true);
                mic.SetEnabled(connected && !media.MicBusy);
                var cam = SocialUi.Action(media?.CameraOn == true ? "calls.camOn" : "calls.camOff", "calls", _c.ToggleCamera);
                cam.SetEnabled(connected && !media.CameraBusy);
                var spk = SocialUi.Action(media?.SpeakerMuted == true ? "calls.speakerUnmute" : "calls.speakerMute", media?.SpeakerMuted == true ? "speaker-off" : "speaker", _c.ToggleSpeaker);
                spk.SetEnabled(connected);
                controls.Add(mic); controls.Add(cam); controls.Add(spk);
                controls.Add(SocialUi.Action("calls.hangup", "close", _c.Hangup, danger: true));
                Body.Add(controls);
            }

            private string StatusText(CallSession call, double now)
            {
                switch (call.Phase)
                {
                    case CallPhase.Dialing: return SocialStrings.T("calls.dialing");
                    case CallPhase.Accepting:
                    case CallPhase.Connecting: return SocialStrings.T("calls.connecting");
                    case CallPhase.Waiting: return SocialStrings.T("calls.waiting");
                    case CallPhase.Reconnecting: return SocialStrings.T("calls.reconnecting");
                    case CallPhase.Live: return SocialStrings.F("calls.live", Ago.Clock(call.LiveSeconds(now)));
                    default: return SocialStrings.T("calls.connecting");
                }
            }

            private static VisualElement Tile(string label, Texture video, bool speaking, bool muted, bool local, int rotation)
            {
                var tile = El.Div("ct-social-tile");
                if (speaking) tile.AddToClassList("ct-social-tile-speaking");
                if (video != null)
                {
                    var img = new Image { image = video, scaleMode = ScaleMode.ScaleAndCrop };
                    img.AddToClassList("ct-social-tile-video");
                    img.pickingMode = PickingMode.Ignore;
                    // [Unverified on the Seeker] front camera orientation: rotate by WebCamTexture.videoRotationAngle, mirror the selfie view.
                    if (local)
                    {
                        if (rotation != 0) img.style.rotate = new Rotate(new Angle(rotation));
                        img.style.scale = new Scale(new Vector2(-1, 1));
                    }
                    // Video textures change on the GPU; repaint the element while it is shown.
                    img.schedule.Execute(img.MarkDirtyRepaint).Every(33);
                    tile.Add(img);
                }
                var text = label + (muted ? " · " + SocialStrings.T("calls.mutedPeer") : "") + (video == null ? " · " + SocialStrings.T("calls.noVideo") : "");
                tile.Add(SocialUi.Text(text, "ct-social-tile-label"));
                return tile;
            }

            // ---------------------------------------------------------------- paid (fan)

            private void RenderPaid(PaidCallFlow flow, double now)
            {
                var card = SocialUi.Card(SocialUi.Eyebrow("paid.title"), SocialUi.H2(SocialStrings.F("paid.with", Hub.Names.Name(flow.PeerId))));
                var stageKey = "paid.stage." + flow.Stage;
                var tone = flow.Stage == PaidStage.Failed ? "error"
                    : flow.Stage == PaidStage.Settled || flow.Stage == PaidStage.Refunded ? "success"
                    : flow.Stage == PaidStage.Draft ? "neutral" : "info";
                card.Add(SocialUi.Status(flow.Stage == PaidStage.Ringing ? SocialStrings.F(stageKey, (int)Math.Ceiling(flow.AcceptSecondsLeft(now))) : SocialStrings.T(stageKey), tone, "calls"));
                if (flow.ErrorKey != null) card.Add(SocialUi.Notice(SocialStrings.T(flow.ErrorKey), flow.ErrorKey.StartsWith("err.") ? "error" : "warn"));
                if (flow.CallId != null) card.Add(SocialUi.Small(SocialStrings.F("paid.callId", flow.CallId)));
                var blocker = _c.SignBlocker();
                switch (flow.Stage)
                {
                    case PaidStage.Draft:
                    case PaidStage.Creating:
                        card.Add(SocialUi.Small(SocialStrings.T("paid.intro")));
                        if (blocker != null) card.Add(SocialUi.Notice(SocialStrings.T(blocker), "warn"));
                        card.Add(SocialUi.Field("paid.minutes", _minutes, 2, v => _minutes = v));
                        var create = SocialUi.Btn("paid.create", () => _c.CreatePaid(_minutes), true);
                        create.SetEnabled(!_c.PaidWorking && CallController.PaidCallsAllowedHere);
                        card.Add(SocialUi.Row(create, SocialUi.Ghost("common.cancel", _c.ClosePaid)));
                        break;
                    case PaidStage.ReadyToSign:
                    case PaidStage.Signing:
                        card.Add(SocialUi.Small(SocialStrings.T("paid.walletShows")));
                        if (blocker != null) card.Add(SocialUi.Notice(SocialStrings.T(blocker), "warn"));
                        var sign = SocialUi.Btn("paid.sign", _c.SignPaid, true);
                        sign.SetEnabled(!_c.PaidWorking && blocker == null && flow.CanSign(now));
                        card.Add(SocialUi.Row(sign, SocialUi.Ghost("common.cancel", _c.ClosePaid)));
                        break;
                    case PaidStage.Confirming:
                        var row = SocialUi.Row(SocialUi.Btn("paid.check", _c.CheckPaid));
                        if (_c.CanAbandonPaid) row.Add(SocialUi.Ghost("paid.stopWaiting", _c.AbandonPaid));
                        card.Add(row);
                        break;
                    case PaidStage.Settled:
                    case PaidStage.Refunded:
                    case PaidStage.Failed:
                        Settlement(card, flow);
                        card.Add(SocialUi.Btn("common.close", _c.ClosePaid));
                        break;
                }
                Body.Add(card);
            }

            /// <summary>Server values only: minutes used, the creator's amount and the refund (USDC base units → display).</summary>
            private static void Settlement(VisualElement card, PaidCallFlow flow)
            {
                var row = flow.Server;
                if (row == null) return;
                if (row.MinutesUsed.HasValue) card.Add(SocialUi.Small(SocialStrings.T("paid.minutesUsed") + ": " + row.MinutesUsed.Value));
                if (row.CreatorAmount != null) card.Add(SocialUi.Small(SocialStrings.T("paid.creatorAmount") + ": " + UsdcUnits.Format(row.CreatorAmount)));
                if (row.RefundAmount != null) card.Add(SocialUi.Small(SocialStrings.T("paid.refundAmount") + ": " + UsdcUnits.Format(row.RefundAmount)));
            }

            // ---------------------------------------------------------------- people

            private IEnumerable<string> People()
            {
                var ids = new List<string>();
                ids.AddRange(Hub.Calls.Recents.Select(r => r.UserId));
                ids.AddRange(Hub.Messages.Inbox.All.Select(e => e.UserId));
                ids.AddRange(Hub.Follows.Items);
                ids.AddRange(_selected);
                return ids.Where(id => SocialWire.IsSafeId(id) && !SocialWire.SameId(id, Hub.Me) && !Hub.Blocks.Contains(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(40);
            }

            private void RenderPeople()
            {
                Body.Add(SocialUi.Small(SocialStrings.T("calls.intro")));
                var people = People().ToList();
                if (people.Count == 0) { Body.Add(SocialUi.Empty("calls", SocialStrings.T("calls.contactsEmptyTitle"), SocialStrings.T("calls.contactsEmpty"))); return; }
                Body.Add(SocialUi.Eyebrow("calls.contacts"));
                foreach (var id in people)
                {
                    var user = id;
                    var on = _selected.Contains(user);
                    var row = El.Div("ct-social-person", SocialUi.Avatar(Hub.Names.Name(user)), El.Div("ct-grow", SocialUi.H2(Hub.Names.Name(user)), SocialUi.Small(Hub.Follows.Contains(user) ? SocialStrings.T("feed.following") : "")));
                    row.Add(SocialUi.Chip(on ? "✓" : "+", () => { if (!_selected.Remove(user) && _selected.Count < CallSession.MaxInvitees) _selected.Add(user); MarkDirty(); }, on));
                    var paid = SocialUi.Action("paid.book", "calls", () => _c.BeginPaid(user));
                    paid.SetEnabled(CallController.PaidCallsAllowedHere && (_c.Paid == null || _c.Paid.Terminal || _c.Paid.Stage == PaidStage.Draft));
                    row.Add(paid);
                    Body.Add(row);
                }
                Body.Add(SocialUi.Small(SocialStrings.F("calls.selected", _selected.Count)));
                var start = SocialUi.Action("calls.start", "calls", () => { _c.StartCall(_selected.ToList()); }, true);
                start.SetEnabled(_selected.Count > 0 && !_c.Busy && !_c.Dialing && Features.IsAvailable(Features.Voice));
                Body.Add(start);
                var recents = Hub.Calls.Recents.Take(8).ToList();
                if (recents.Count > 0)
                {
                    Body.Add(SocialUi.Eyebrow("calls.recents"));
                    foreach (var r in recents)
                        Body.Add(SocialUi.Small(Hub.Names.Name(r.UserId) + " · " + r.Kind + (r.Paid ? " · paid" : "") + " · " + Ago.Short(r.AtMs, ServerClock.NowMs)));
                }
            }

            // ---------------------------------------------------------------- creator settings (PUT /creator)

            private void RenderCreator()
            {
                if (!_showCreator) { Body.Add(SocialUi.Ghost("creator.title", () => { _showCreator = true; MarkDirty(); })); return; }
                var card = SocialUi.Card(SocialUi.H2(SocialStrings.T("creator.title")), SocialUi.Small(SocialStrings.T("creator.intro")));
                var saved = _c.Creator;
                if (saved != null)
                    card.Add(SocialUi.Small(SocialStrings.F("creator.lastSaved", SocialStrings.T(saved.Enabled ? "creator.on" : "creator.off"), UsdcUnits.Format(saved.RatePerMinUsdc), saved.MaxMinutes)));
                var toggle = new Toggle(SocialStrings.T("creator.enabled")) { value = _enabled ?? false };
                toggle.AddToClassList("ct-toggle");
                toggle.RegisterValueChangedCallback(e => _enabled = e.newValue);
                card.Add(toggle);
                card.Add(SocialUi.Field("creator.rate", _rate, 8, v => _rate = v));
                card.Add(SocialUi.Field("creator.max", _max, 2, v => _max = v));
                if (_creatorMsg != null) card.Add(SocialUi.Notice(_creatorMsg, "info"));
                var save = SocialUi.Btn("creator.save", () =>
                {
                    _creatorBusy = true; _creatorMsg = null; MarkDirty();
                    _c.SaveCreator(_enabled ?? false, _rate, _max, err =>
                    {
                        _creatorBusy = false;
                        var s = _c.Creator;
                        _creatorMsg = err != null ? SocialStrings.T(err) : SocialStrings.F("creator.saved", UsdcUnits.Format(s?.RatePerMinUsdc), s?.MaxMinutes);
                        MarkDirty();
                    });
                }, true);
                save.SetEnabled(!_creatorBusy);
                card.Add(SocialUi.Row(save, SocialUi.Ghost("common.close", () => { _showCreator = false; MarkDirty(); })));
                Body.Add(card);
            }
        }
    }
}
