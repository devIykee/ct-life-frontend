using System.Linq;
using CTLife.Core;
using CTLife.UI;
using UnityEngine.UIElements;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// Spaces Studio (feature #23), phone app id `spaces` (replaces CTLife.UI.SpacesApp). The Space keeps playing while the
    /// phone is closed; the overlay shows an "In a Space" chip to come back. Roles and wording follow X Spaces: host, speakers,
    /// listeners; listeners "Request" to speak; everyone joins muted and unmutes themselves; the host adds speakers from
    /// requests or moves them back to listeners. Blocked speakers are labelled and never heard.
    /// </summary>
    public sealed class SpacesStudioApp : SocialApp
    {
        public override string Id => "spaces";
        public override string Title => SocialStrings.T("spaces.title");
        public override string Color => "#a55eea";
        public override string Feature => Features.Voice;

        internal override SocialView CreateView(SocialHub hub, PhoneShell phone) => new View(hub, phone);

        private sealed class View : SocialView
        {
            private readonly SpacesController _s;
            private TextField _topic;
            private string _draft = "";
            private bool _compose, _confirmEnd, _report, _showList;

            public View(SocialHub hub, PhoneShell phone) : base(hub, phone)
            {
                _s = hub.Spaces;
                Root.AddToClassList("ct-social-spaces");
                _s.Watch(true);
            }

            public override void Dispose()
            {
                if (!Disposed) _s.Watch(false);
                base.Dispose();
            }

            protected override void Render()
            {
                var session = _s.Session;
                if (session != null && session.SpaceId != null && session.Phase != SpacePhase.Idle && !(_showList && session.InSpace)) { _showList = false; RenderSpace(session); }
                else RenderList();
            }

            private void Message()
            {
                if (!string.IsNullOrEmpty(_s.Message)) Body.Add(SocialUi.Notice(SocialStrings.T(_s.Message), _s.Message.StartsWith("err.") ? "error" : "info"));
            }

            private void RenderList()
            {
                Body.Add(SocialUi.Row(SocialUi.H1(SocialStrings.T("spaces.heading")), El.Div("ct-grow"), SocialUi.Action("common.refresh", "rotate", _s.RefreshList)));
                Body.Add(SocialUi.Copy("spaces.intro", "ct-small ct-muted"));
                var current = _s.Session;
                if (current != null && current.InSpace)
                    Body.Add(SocialUi.Card(SocialUi.H2(current.Space?.Title ?? SocialStrings.T("calls.ongoingSpace")),
                        SocialUi.Btn("spaces.return", () => { _showList = false; MarkDirty(); }, true)));
                Message();
                if (_s.Unavailable) { Body.Add(SocialUi.Notice(SocialStrings.T("err.routeMissing"), "error")); return; }
                if (!Features.IsAvailable(Features.Voice)) Body.Add(SocialUi.Notice(SocialStrings.T("spaces.voiceDegraded"), "warn"));

                // Host a Space
                if (_compose || _s.PendingTitle != null)
                {
                    if (_topic == null)
                    {
                        _topic = SocialUi.Field("spaces.topic", _draft, 80, v => _draft = v);
                        _topic.tooltip = SocialStrings.T("spaces.topicHint");
                    }
                    if (_s.PendingTitle != null) _topic.SetValueWithoutNotify(_s.PendingTitle);
                    _topic.SetEnabled(_s.PendingTitle == null && !_s.Busy);
                    var card = SocialUi.Card(SocialUi.H2(SocialStrings.T("spaces.host")), _topic, SocialUi.Small(SocialStrings.T("spaces.topicHint")),
                        SocialUi.Small(SocialStrings.T("spaces.hostTips")));
                    var row = SocialUi.Row(SocialUi.Btn(_s.PendingTitle == null ? "spaces.start" : "spaces.startRetry", () => _s.Create(_draft), true));
                    if (_s.PendingTitle != null) row.Add(SocialUi.Ghost("spaces.forget", _s.ForgetPendingCreate));
                    else row.Add(SocialUi.Ghost("common.cancel", () => { _compose = false; MarkDirty(); }));
                    row.SetEnabled(!_s.Busy);
                    card.Add(row);
                    Body.Add(card);
                }
                else Body.Add(SocialUi.Btn("spaces.host", () => { _compose = true; MarkDirty(); }, true));

                Body.Add(SocialUi.Eyebrow("spaces.live"));
                if (_s.ListLoading && _s.List == null) { Body.Add(SocialUi.Copy("common.loading")); return; }
                if (_s.ListError != null) Body.Add(SocialUi.Notice(SocialStrings.T(_s.ListError), "error"));
                var list = _s.List;
                if (list == null || list.Count == 0)
                {
                    if (_s.ListError == null) Body.Add(SocialUi.Empty("spaces", SocialStrings.T("spaces.empty"), SocialStrings.T("spaces.emptyDetail")));
                    return;
                }
                foreach (var sp in list.Take(50))
                {
                    var id = sp.Id; var known = sp;
                    var hostName = Hub.Names.Name(sp.HostId);
                    var card = SocialUi.Card(SocialUi.Status(SocialStrings.T("spaces.live"), "live", "spaces"), SocialUi.H2(sp.Title),
                        SocialUi.Row(SocialUi.Avatar(hostName), El.Div("ct-grow", SocialUi.Text(hostName),
                            SocialUi.Small(SocialStrings.F("spaces.listeners", sp.ListenerCount)))));
                    card.AddToClassList("ct-social-space-card");
                    if (Hub.Blocks.Contains(sp.HostId)) card.Add(SocialUi.Small(SocialStrings.T("spaces.blockedLabel")));
                    var join = SocialUi.Action("spaces.join", "speaker", () => _s.Open(id, known), true);
                    join.SetEnabled(Features.IsAvailable(Features.Voice));
                    card.Add(join);
                    Body.Add(card);
                }
            }

            private void RenderSpace(SpaceSession session)
            {
                var sp = session.Space;
                var now = Now;
                // "All Spaces" keeps listening (like minimizing a Space); Leave quietly is the explicit exit.
                Body.Add(SocialUi.Row(SocialUi.Ghost("spaces.back", () =>
                {
                    if (session.InSpace) _showList = true; else _s.Close();
                    _report = false; _confirmEnd = false; MarkDirty();
                }).With("ct-social-back")));
                Body.Add(SocialUi.H1(sp?.Title ?? SocialStrings.T("common.loading")));
                var status = SocialUi.Row(SocialUi.Status(SocialStrings.T("common.notRecorded")));
                if (sp != null) status.Add(SocialUi.Small(SocialStrings.F("spaces.listeners", sp.ListenerCount)));
                Body.Add(status);
                Message();
                if (session.ErrorKey != null && session.ErrorKey != _s.Message) Body.Add(SocialUi.Notice(SocialStrings.T(session.ErrorKey), "error"));

                switch (session.Phase)
                {
                    case SpacePhase.Ended:
                        Body.Add(SocialUi.Card(SocialUi.H2(SocialStrings.T("spaces.ended")), SocialUi.Btn("spaces.back", () => { _s.Close(); MarkDirty(); }, true)));
                        return;
                    case SpacePhase.Left:
                    case SpacePhase.Failed:
                        Body.Add(SocialUi.Card(SocialUi.Copy(session.Phase == SpacePhase.Left ? "spaces.leftNote" : "spaces.connectionLost"),
                            SocialUi.Row(SocialUi.Btn("spaces.rejoin", _s.Rejoin, true), SocialUi.Ghost("spaces.back", () => { _s.Close(); MarkDirty(); }))));
                        return;
                }

                // Audio / mic state
                var audio = session.Phase == SpacePhase.Live && _s.AudioConnected ? "spaces.connected"
                    : session.Phase == SpacePhase.Reconnecting ? "spaces.reconnecting" : "spaces.connecting";
                var me = SpaceSession.CanTalk(session.Role) ? (_s.MicOn ? "spaces.micLive" : "spaces.micOff") : null;
                var state = SocialUi.Row(SocialUi.Status(SocialStrings.T(audio), audio == "spaces.connected" ? "success" : "info", "spaces"));
                if (_s.Muted) state.Add(SocialUi.Status(SocialStrings.T("spaces.silenced"), "neutral"));
                if (me != null) state.Add(SocialUi.Status(SocialStrings.T(me), _s.MicOn ? "success" : "neutral", "mic"));
                state.Add(SocialUi.Status(SocialStrings.T(RoleKey(session.Role)), session.Role == SpaceRole.Host ? "live" : "neutral"));
                Body.Add(state);

                var controls = SocialUi.Row();
                controls.AddToClassList("ct-social-media-controls");
                if (SpaceSession.CanTalk(session.Role))
                {
                    var mic = SocialUi.Action(_s.MicOn ? "spaces.muteMic" : "spaces.unmute", "mic", _s.ToggleMic, !_s.MicOn);
                    mic.SetEnabled(_s.MicOn || (!_s.MicBusy && session.CanPublish(now)));
                    controls.Add(mic);
                }
                else if (session.Role == SpaceRole.Requested)
                {
                    var w = SocialUi.Action("spaces.withdraw", "mic", _s.WithdrawRequest);
                    w.SetEnabled(session.CanWithdraw(now));
                    controls.Add(w);
                }
                else
                {
                    var r = SocialUi.Action("spaces.request", "mic", _s.RequestToSpeak, true);
                    r.SetEnabled(session.CanRequest(now));
                    controls.Add(r);
                }
                controls.Add(SocialUi.Action(_s.Muted ? "spaces.speakerOn" : "spaces.speakerOff", _s.Muted ? "speaker-off" : "speaker", _s.ToggleSpeakerMute));
                controls.Add(SocialUi.Action("spaces.leave", "door", () => { _s.Leave(); MarkDirty(); }));
                Body.Add(controls);
                if (session.Role == SpaceRole.Requested) Body.Add(SocialUi.Small(SocialStrings.T("spaces.requestedNote")));
                if (sp == null) return;

                // Stage
                var blockedOnStage = sp.SpeakerIds.Count(Hub.Blocks.Contains) + (Hub.Blocks.Contains(sp.HostId) ? 1 : 0);
                if (blockedOnStage > 0) Body.Add(SocialUi.Notice(SocialStrings.F("spaces.blockedWarning", blockedOnStage), "warn"));
                Body.Add(SocialUi.Eyebrow("spaces.stage"));
                bool host = session.Role == SpaceRole.Host;
                Person(sp.HostId, "spaces.roleHost", null);
                foreach (var u in sp.SpeakerIds)
                {
                    var user = u;
                    Button act = null;
                    if (host) { act = SocialUi.Btn("spaces.toListeners", () => _s.Demote(user)); act.SetEnabled(session.CanDemote(user, now)); }
                    Person(user, "spaces.roleSpeaker", act);
                }

                if (host)
                {
                    Body.Add(SocialUi.Text(SocialStrings.F("spaces.requests", sp.HandIds.Count), "ct-eyebrow"));
                    if (sp.HandIds.Count == 0) Body.Add(SocialUi.Small(SocialStrings.T("spaces.requestsEmpty")));
                    if (session.StageFull) Body.Add(SocialUi.Small(SocialStrings.T("spaces.stageFull")));
                    foreach (var u in sp.HandIds.Take(30))
                    {
                        var user = u;
                        var add = SocialUi.Btn("spaces.addSpeaker", () => _s.Promote(user), true);
                        add.SetEnabled(session.CanPromote(user, now));
                        Person(user, "spaces.roleRequested", add);
                    }
                    Body.Add(SocialUi.Small(SocialStrings.T("spaces.noCohosts")));
                    if (_confirmEnd)
                        Body.Add(SocialUi.Card(SocialUi.H2(SocialStrings.T("spaces.endAsk")),
                            SocialUi.Row(SocialUi.Danger("spaces.endConfirm", () => { _confirmEnd = false; _s.End(); }), SocialUi.Ghost("common.cancel", () => { _confirmEnd = false; MarkDirty(); }))));
                    else
                    {
                        var end = SocialUi.Danger("spaces.end", () => { _confirmEnd = true; MarkDirty(); });
                        end.SetEnabled(session.CanEnd(now));
                        Body.Add(end);
                    }
                }

                if (_report)
                    Body.Add(SocialUi.ReportPanel(sp.Title, reason => { _report = false; Hub.Report("space", sp.Id, reason, null); MarkDirty(); }, () => { _report = false; MarkDirty(); }));
                else Body.Add(SocialUi.Ghost("spaces.reportSpace", () => { _report = true; MarkDirty(); }));
            }

            private static string RoleKey(SpaceRole r) => r == SpaceRole.Host ? "spaces.roleHost" : r == SpaceRole.Speaker ? "spaces.roleSpeaker"
                : r == SpaceRole.Requested ? "spaces.roleRequested" : "spaces.roleListener";

            private void Person(string userId, string roleKey, Button action)
            {
                var name = Hub.Names.Name(userId);
                if (SocialWire.SameId(userId, Hub.Me)) name = SocialStrings.F("common.you", name);
                var info = El.Div("ct-grow", SocialUi.H2(name), SocialUi.Small(SocialStrings.T(roleKey)));
                if (Hub.Blocks.Contains(userId)) info.Add(SocialUi.Small(SocialStrings.T("spaces.blockedLabel")));
                else if (_s.IsSpeaking(userId)) info.Add(SocialUi.Text(SocialStrings.T("spaces.speaking"), "ct-social-speaking"));
                var avatar = SocialUi.Avatar(name);
                avatar.EnableInClassList("ct-social-avatar-speaking", !Hub.Blocks.Contains(userId) && _s.IsSpeaking(userId));
                var row = El.Div("ct-social-person", avatar, info);
                if (action != null) row.Add(action);
                Body.Add(row);
            }

            // Speaking indicators change without hub events: refresh once a second while in a Space.
            protected override bool NeedsClockRender => _s.Session != null && _s.Session.InSpace;
        }
    }
}
