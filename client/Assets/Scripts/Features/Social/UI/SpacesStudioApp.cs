using System;
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
    /// Duo layout (X Spaces): LIVE badge, stage avatar grid with speaking rings, listener count, and a persistent glass
    /// control bar (mic or request, silence, leave).
    /// </summary>
    public sealed class SpacesStudioApp : SocialApp
    {
        public override string Id => "spaces";
        public override string Title => SocialStrings.T("spaces.title");
        public override string Color => "#a55eea";
        public override string Feature => Features.Voice;
        public override DuoChrome Chrome => DuoChrome.Plain;

        internal override SocialView CreateView(SocialHub hub, PhoneShell phone) => new View(hub, phone);

        /// <summary>
        /// A round control that survives re-renders: the bar sits outside the view's press guard, so its Buttons are
        /// updated in place (caption, icon, action, enabled) and never replaced under a finger.
        /// </summary>
        private sealed class Control
        {
            public readonly Button Button;
            private readonly VisualElement _face;
            private readonly Label _label;
            private Action _action;
            private string _icon;

            public Control()
            {
                Button = El.Btn("", () => _action?.Invoke(), "duo-control");
                _face = El.Div("duo-control-face");
                _label = El.Text("", "duo-control-label");
                Button.Add(El.PassThrough(_face));
                Button.Add(El.PassThrough(_label));
            }

            public void Set(string key, string icon, Action action, bool enabled, string tone = null)
            {
                _action = action;
                Button.name = "social-" + key.Replace('.', '-');
                Button.tooltip = SocialStrings.T(key);
                _label.text = SocialStrings.T(key);
                if (_icon != icon)
                {
                    _icon = icon;
                    _face.Clear();
                    _face.Add(icon == "speaker" || icon == "speaker-off" ? (VisualElement)new SocialControlIcon(icon) : new UiIcon(icon));
                }
                foreach (var t in new[] { "duo-control-red", "duo-control-on", "duo-control-purple" }) Button.EnableInClassList(t, t == tone);
                Button.SetEnabled(enabled);
            }
        }

        private sealed class View : SocialView
        {
            private readonly SpacesController _s;
            private readonly VisualElement _frame, _bar;
            private readonly DuoNav _nav;
            private readonly Control _mic = new Control(), _speaker = new Control(), _leave = new Control();
            private TextField _topic;
            private string _draft = "";
            private bool _compose, _showList;

            public View(SocialHub hub, PhoneShell phone) : base(hub, phone)
            {
                _s = hub.Spaces;
                Root.RemoveFromClassList("ct-app-page");
                Root.RemoveFromClassList("ct-social");
                Root.AddToClassList("duo-scroll");
                Root.AddToClassList("ct-social-spaces");
                Root.contentContainer.AddToClassList("duo-scroll-content");
                _frame = El.Div("duo-page duo-spaces");
                _frame.name = "spaces-frame";
                _frame.Add(Root);
                _nav = new DuoNav();
                _nav.SetTitle(SocialStrings.T("spaces.title"));
                _nav.SetCollapsed(true, instant: true);
                _nav.AddLeft(DuoGlyph.Kind.Back, SocialStrings.T("common.back"), Back, "spaces-nav-back");
                _nav.Right.Add(Duo.Circle(new UiIcon("rotate"), SocialStrings.T("common.refresh"), () => _s.RefreshList(), "social-common-refresh"));
                _frame.Add(_nav);
                _bar = El.Div("duo-call-controls-bar duo-spaces-bar");
                Duo.AddShadow(_bar, "bar");
                _bar.Add(El.Div("duo-call-controls", _mic.Button, _speaker.Button, _leave.Button));
                _bar.Show(false);
                _frame.Add(_bar);
                _s.Watch(true);
            }

            public override VisualElement Container => _frame;

            public override void Dispose()
            {
                if (!Disposed) _s.Watch(false);
                base.Dispose();
            }

            /// <summary>"All Spaces" keeps listening (like minimizing a Space); Leave is the explicit exit.</summary>
            private void Back()
            {
                var session = _s.Session;
                bool inRoom = session != null && session.SpaceId != null && session.Phase != SpacePhase.Idle && !(_showList && session.InSpace);
                if (!inRoom) { Phone?.Back(); return; }
                if (session.InSpace) _showList = true; else _s.Close();
                MarkDirty();
            }

            protected override void Render()
            {
                var session = _s.Session;
                bool room = session != null && session.SpaceId != null && session.Phase != SpacePhase.Idle && !(_showList && session.InSpace);
                _frame.EnableInClassList("duo-spaces-room", room);
                _nav.Q("social-common-refresh")?.Show(!room);
                if (room) { _showList = false; RenderSpace(session); }
                else { _bar.Show(false); RenderList(); }
            }

            private void Message()
            {
                if (!string.IsNullOrEmpty(_s.Message)) Body.Add(Duo.Notice(SocialStrings.T(_s.Message), _s.Message.StartsWith("err.") ? Duo.Tone.Danger : Duo.Tone.Info));
            }

            private static Label Badge() => El.Text(SocialStrings.T("spaces.liveBadge"), "duo-live-badge");

            private void RenderList()
            {
                Body.Add(El.Text(SocialStrings.T("spaces.heading"), "duo-large-title ct-h1"));
                Body.Add(Duo.Footnote(SocialStrings.T("spaces.intro"), "duo-topup-note"));
                var current = _s.Session;
                if (current != null && current.InSpace)
                {
                    var back = El.Btn(SocialStrings.T("spaces.return"), () => { _showList = false; MarkDirty(); }, "duo-wide-button duo-wide-button-purple");
                    back.name = "social-spaces-return";
                    Body.Add(El.Div("duo-space-current", El.Div("duo-space-current-head", Badge(), El.Text(current.Space?.Title ?? SocialStrings.T("calls.ongoingSpace"), "duo-space-current-title")), back));
                }
                Message();
                if (_s.Unavailable) { Body.Add(Duo.Notice(SocialStrings.T("err.routeMissing"), Duo.Tone.Danger)); return; }
                if (!Features.IsAvailable(Features.Voice)) Body.Add(Duo.Notice(SocialStrings.T("spaces.voiceDegraded"), Duo.Tone.Warning));

                // Host a Space (inline: typing in Body defers renders until the field blurs)
                if (_compose || _s.PendingTitle != null)
                {
                    if (_topic == null)
                    {
                        _topic = new TextField { value = _draft, maxLength = 80, isDelayed = false };
                        _topic.AddToClassList("duo-field");
                        _topic.textEdition.placeholder = SocialStrings.T("spaces.topic");
                        _topic.tooltip = SocialStrings.T("spaces.topicHint");
                        _topic.RegisterValueChangedCallback(e => _draft = e.newValue);
                    }
                    if (_s.PendingTitle != null) _topic.SetValueWithoutNotify(_s.PendingTitle);
                    _topic.SetEnabled(_s.PendingTitle == null && !_s.Busy);
                    var start = El.Btn(SocialStrings.T(_s.PendingTitle == null ? "spaces.start" : "spaces.startRetry"), () => _s.Create(_draft), "duo-wide-button duo-wide-button-purple");
                    start.name = "social-spaces-start";
                    var other = _s.PendingTitle != null
                        ? El.Btn(SocialStrings.T("spaces.forget"), _s.ForgetPendingCreate, "duo-sheet-secondary")
                        : El.Btn(SocialStrings.T("common.cancel"), () => { _compose = false; MarkDirty(); }, "duo-sheet-secondary");
                    start.SetEnabled(!_s.Busy); other.SetEnabled(!_s.Busy);
                    Body.Add(El.Div("duo-space-compose", Duo.GroupHeader(SocialStrings.T("spaces.host")), _topic,
                        Duo.Footnote(SocialStrings.T("spaces.topicHint"), "duo-topup-note"), Duo.Footnote(SocialStrings.T("spaces.hostTips"), "duo-topup-note"), start, other));
                }
                else
                {
                    var host = El.Btn(SocialStrings.T("spaces.host"), () => { _compose = true; MarkDirty(); }, "duo-wide-button duo-wide-button-purple");
                    host.name = "social-spaces-host";
                    Body.Add(host);
                }

                Body.Add(Duo.SectionHeader(SocialStrings.T("spaces.live")));
                if (_s.ListLoading && _s.List == null) { Body.Add(Duo.Footnote(SocialStrings.T("common.loading"))); return; }
                if (_s.ListError != null) Body.Add(Duo.Notice(SocialStrings.T(_s.ListError), Duo.Tone.Danger));
                var list = _s.List;
                if (list == null || list.Count == 0)
                {
                    if (_s.ListError == null) Body.Add(Duo.Empty(SocialStrings.T("spaces.empty"), SocialStrings.T("spaces.emptyDetail")));
                    return;
                }
                foreach (var sp in list.Take(50))
                {
                    var id = sp.Id; var known = sp;
                    var hostName = Hub.Names.Name(sp.HostId);
                    var faces = El.Div("duo-space-faces", Duo.Avatar(hostName, sp.HostId, "duo-avatar-s"));
                    foreach (var u in sp.SpeakerIds.Take(4)) faces.Add(Duo.Avatar(Hub.Names.Name(u), u, "duo-avatar-s"));
                    var card = El.Div("duo-space-card",
                        El.Div("duo-space-card-head", Badge(), El.Text(SocialStrings.F("spaces.listeners", sp.ListenerCount), "duo-space-card-count")),
                        El.Text(sp.Title, "duo-space-card-title"), faces,
                        El.Text(SocialStrings.F("spaces.hostedBy", hostName), "duo-space-card-host"));
                    card.name = "spaces-card-" + sp.Id;
                    if (Hub.Blocks.Contains(sp.HostId)) card.Add(El.Text(SocialStrings.T("spaces.blockedLabel"), "duo-space-card-host"));
                    var join = Duo.Pill(SocialStrings.T("spaces.join"), () => _s.Open(id, known));
                    join.name = "social-spaces-join";
                    join.AddToClassList("duo-wcard-pill");
                    join.SetEnabled(Features.IsAvailable(Features.Voice));
                    card.Add(El.Div("duo-wcard-actions", join));
                    Body.Add(card);
                }
            }

            private void RenderSpace(SpaceSession session)
            {
                var sp = session.Space;
                var now = Now;
                Body.Add(El.Div("duo-space-head", Badge(), El.Text(SocialStrings.T("common.notRecorded"), "duo-call-tag"),
                    sp != null ? El.Text(SocialStrings.F("spaces.listeners", sp.ListenerCount), "duo-space-count") : null));
                Body.Add(El.Text(sp?.Title ?? SocialStrings.T("common.loading"), "duo-space-title ct-h1"));
                Message();
                if (session.ErrorKey != null && session.ErrorKey != _s.Message) Body.Add(Duo.Notice(SocialStrings.T(session.ErrorKey), Duo.Tone.Danger));

                switch (session.Phase)
                {
                    case SpacePhase.Ended:
                        _bar.Show(false);
                        Body.Add(Duo.Empty(SocialStrings.T("spaces.ended")));
                        Body.Add(El.Btn(SocialStrings.T("spaces.back"), () => { _s.Close(); MarkDirty(); }, "duo-wide-button duo-wide-button-purple"));
                        return;
                    case SpacePhase.Left:
                    case SpacePhase.Failed:
                        _bar.Show(false);
                        Body.Add(Duo.Empty(SocialStrings.T(session.Phase == SpacePhase.Left ? "spaces.leftNote" : "spaces.connectionLost")));
                        var rejoin = El.Btn(SocialStrings.T("spaces.rejoin"), _s.Rejoin, "duo-wide-button duo-wide-button-purple");
                        rejoin.name = "social-spaces-rejoin";
                        Body.Add(rejoin);
                        Body.Add(El.Btn(SocialStrings.T("spaces.back"), () => { _s.Close(); MarkDirty(); }, "duo-sheet-secondary"));
                        return;
                }

                // Audio / mic state line
                var audio = session.Phase == SpacePhase.Live && _s.AudioConnected ? "spaces.connected"
                    : session.Phase == SpacePhase.Reconnecting ? "spaces.reconnecting" : "spaces.connecting";
                var parts = new System.Collections.Generic.List<string> { SocialStrings.T(audio), SocialStrings.T(RoleKey(session.Role)) };
                if (SpaceSession.CanTalk(session.Role)) parts.Add(SocialStrings.T(_s.MicOn ? "spaces.micLive" : "spaces.micOff"));
                if (_s.Muted) parts.Add(SocialStrings.T("spaces.silenced"));
                Body.Add(El.Text(string.Join(" · ", parts), "duo-space-state" + (audio == "spaces.connected" ? " duo-space-state-on" : "")));

                // Persistent glass bar
                _bar.Show(true);
                if (SpaceSession.CanTalk(session.Role))
                    _mic.Set(_s.MicOn ? "spaces.muteMic" : "spaces.unmute", "mic", _s.ToggleMic, _s.MicOn || (!_s.MicBusy && session.CanPublish(now)), _s.MicOn ? "duo-control-purple" : null);
                else if (session.Role == SpaceRole.Requested)
                    _mic.Set("spaces.withdraw", "mic", _s.WithdrawRequest, session.CanWithdraw(now), "duo-control-on");
                else
                    _mic.Set("spaces.request", "mic", _s.RequestToSpeak, session.CanRequest(now), "duo-control-purple");
                _speaker.Set(_s.Muted ? "spaces.speakerOn" : "spaces.speakerOff", _s.Muted ? "speaker-off" : "speaker", _s.ToggleSpeakerMute, true, _s.Muted ? "duo-control-on" : null);
                _leave.Set("spaces.leave", "door", () => { _s.Leave(); MarkDirty(); }, true, "duo-control-red");
                if (session.Role == SpaceRole.Requested) Body.Add(Duo.Footnote(SocialStrings.T("spaces.requestedNote")));
                if (sp == null) return;

                // Stage grid with speaking rings
                var blockedOnStage = sp.SpeakerIds.Count(Hub.Blocks.Contains) + (Hub.Blocks.Contains(sp.HostId) ? 1 : 0);
                if (blockedOnStage > 0) Body.Add(Duo.Notice(SocialStrings.F("spaces.blockedWarning", blockedOnStage), Duo.Tone.Warning));
                Body.Add(Duo.SectionHeader(SocialStrings.T("spaces.stage")));
                bool host = session.Role == SpaceRole.Host;
                var stage = El.Div("duo-stage");
                stage.Add(StageSeat(sp.HostId, "spaces.roleHost", null));
                foreach (var u in sp.SpeakerIds)
                {
                    var user = u;
                    Action demote = null;
                    if (host && session.CanDemote(user, now)) demote = () => _s.Demote(user);
                    stage.Add(StageSeat(user, "spaces.roleSpeaker", demote));
                }
                Body.Add(stage);

                if (host)
                {
                    Body.Add(Duo.SectionHeader(SocialStrings.F("spaces.requests", sp.HandIds.Count)));
                    if (sp.HandIds.Count == 0) Body.Add(Duo.Footnote(SocialStrings.T("spaces.requestsEmpty"), "duo-topup-note"));
                    if (session.StageFull) Body.Add(Duo.Footnote(SocialStrings.T("spaces.stageFull"), "duo-topup-note"));
                    var hands = El.Div("duo-list");
                    foreach (var u in sp.HandIds.Take(30))
                    {
                        var user = u;
                        var name = Hub.Names.Name(user);
                        var add = Duo.Pill(SocialStrings.T("spaces.addSpeaker"), () => _s.Promote(user), Duo.PillKind.Primary);
                        add.name = "social-spaces-addSpeaker";
                        add.SetEnabled(session.CanPromote(user, now));
                        hands.Add(new DuoRow().Leading(Duo.Avatar(name, user, "duo-avatar-s")).Title(name, "duo-row-title-strong")
                            .Subtitle(SocialStrings.T("spaces.roleRequested")).Trailing(add));
                    }
                    Duo.MarkLast(hands);
                    Body.Add(hands);
                    Body.Add(Duo.Footnote(SocialStrings.T("spaces.noCohosts"), "duo-topup-note"));
                    var end = El.Btn(SocialStrings.T("spaces.end"), EndSheet, "duo-wide-button duo-wide-button-red");
                    end.name = "social-spaces-end";
                    end.SetEnabled(session.CanEnd(now));
                    Body.Add(end);
                }
                var report = El.Btn(SocialStrings.T("spaces.reportSpace"), () => ReportSheet(sp), "duo-link duo-destructive duo-space-report");
                report.name = "social-spaces-reportSpace";
                Body.Add(report);
            }

            private void EndSheet()
            {
                var sheet = new DuoSheet(SocialStrings.T("spaces.endAsk"));
                sheet.AddPrimary(SocialStrings.T("spaces.endConfirm"), _s.End, destructive: true, name: "social-spaces-endConfirm");
                sheet.AddSecondary(SocialStrings.T("common.cancel"));
                sheet.Show(_frame);
            }

            private void ReportSheet(SpaceInfo sp)
            {
                var sheet = new DuoSheet(SocialStrings.F("common.reportTitle", sp.Title), SocialStrings.T("common.reportWhy"));
                var reasons = Services.TryGet<ConfigStore>(out var cfg) ? cfg.Registries.ReportReasons : null;
                if (reasons != null)
                    foreach (var r in reasons)
                    {
                        var id = r.id;
                        sheet.AddOption(Strings.ReportReason(r.id, r.name), () => { Hub.Report("space", sp.Id, id, null); MarkDirty(); });
                    }
                sheet.AddSecondary(SocialStrings.T("common.cancel"));
                sheet.Show(_frame);
            }

            private static string RoleKey(SpaceRole r) => r == SpaceRole.Host ? "spaces.roleHost" : r == SpaceRole.Speaker ? "spaces.roleSpeaker"
                : r == SpaceRole.Requested ? "spaces.roleRequested" : "spaces.roleListener";

            /// <summary>One stage seat: avatar with a speaking ring, name, role; the host can move a speaker back.</summary>
            private VisualElement StageSeat(string userId, string roleKey, Action demote)
            {
                var name = Hub.Names.Name(userId);
                if (SocialWire.SameId(userId, Hub.Me)) name = SocialStrings.F("common.you", name);
                bool blocked = Hub.Blocks.Contains(userId);
                bool speaking = !blocked && _s.IsSpeaking(userId);
                var ring = El.Div("duo-stage-ring" + (speaking ? " duo-stage-ring-on" : ""), Duo.Avatar(name, userId, "duo-avatar-stage"));
                var seat = El.Div("duo-stage-seat", ring, El.Text(name, "duo-stage-name"),
                    El.Text(blocked ? SocialStrings.T("spaces.blockedLabel") : speaking ? SocialStrings.T("spaces.speaking") : SocialStrings.T(roleKey),
                        "duo-stage-role" + (speaking ? " duo-stage-role-on" : "")));
                if (demote != null)
                {
                    var move = El.Btn(SocialStrings.T("spaces.toListeners"), demote, "duo-link duo-stage-move");
                    move.name = "social-spaces-toListeners";
                    seat.Add(move);
                }
                return seat;
            }

            // Speaking indicators change without hub events: refresh once a second while in a Space.
            protected override bool NeedsClockRender => _s.Session != null && _s.Session.InSpace;
        }
    }
}
