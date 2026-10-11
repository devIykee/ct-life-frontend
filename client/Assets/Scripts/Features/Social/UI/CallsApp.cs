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
    /// Duo layout (iOS Phone): Favourites (people you follow), Recents with missed calls in red, Contacts, a glass bottom
    /// bar to start the call; the call screen is a big avatar or video tiles over a glass control row (reference 8).
    /// </summary>
    public sealed class CallsApp : SocialApp
    {
        public override string Id => "calls";
        public override string Title => SocialStrings.T("calls.title");
        public override string Color => "#0fb9b1";
        public override string Feature => Features.Voice;
        public override DuoChrome Chrome => DuoChrome.Plain;

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
            private readonly VisualElement _frame;
            private readonly DuoNav _nav;
            private readonly Label _largeTitle;
            private readonly VisualElement _bar;
            private readonly Button _start;
            private string _minutes = "5", _rate, _max;
            private bool? _enabled;

            public View(SocialHub hub, PhoneShell phone, string callee, string paidCreator) : base(hub, phone)
            {
                _c = hub.Calls;
                Root.RemoveFromClassList("ct-app-page");
                Root.RemoveFromClassList("ct-social");
                Root.AddToClassList("duo-scroll");
                Root.contentContainer.AddToClassList("duo-scroll-content");
                _frame = El.Div("duo-page duo-calls");
                _frame.name = "calls-frame";
                _frame.Add(Root);
                _nav = new DuoNav();
                _nav.SetTitle(SocialStrings.T("calls.title"));
                _nav.AddLeft(DuoGlyph.Kind.Back, SocialStrings.T("common.back"), () => Phone?.Back(), "calls-back");
                _frame.Add(_nav);
                _largeTitle = Duo.LargeTitle(SocialStrings.T("calls.title"));
                Header.Add(_largeTitle);
                Root.verticalScroller.valueChanged += v =>
                {
                    float threshold = _largeTitle.layout.yMax - _nav.layout.height * .35f;
                    _nav.SetCollapsed(_frame.ClassListContains("duo-calls-incall") || (!float.IsNaN(threshold) && v > Mathf.Max(8f, threshold)));
                };
                _start = El.Btn(SocialStrings.T("calls.start"), () => _c.StartCall(_selected.ToList()), "duo-calls-start");
                _start.name = "social-calls-start";
                _bar = Duo.BottomBar(_start);
                _frame.Add(_bar);
                if (SocialWire.IsSafeId(callee)) _selected.Add(callee);
                if (SocialWire.IsSafeId(paidCreator)) _c.BeginPaid(paidCreator);
                var saved = _c.Creator;
                _rate = saved != null ? UsdcUnits.Format(saved.RatePerMinUsdc, false) : "";
                _max = saved != null ? saved.MaxMinutes.ToString() : "10";
                _enabled = saved?.Enabled;
            }

            public override VisualElement Container => _frame;

            protected override bool NeedsClockRender =>
                (_c.Active != null && !_c.Active.IsOver) || _c.Incoming.Count > 0 || (_c.Paid != null && !_c.Paid.Terminal);

            protected override void Render()
            {
                var now = Now;
                bool inCall = _c.Active != null && !_c.Active.IsOver;
                _frame.EnableInClassList("duo-calls-incall", inCall || _c.Incoming.Count > 0);
                Header.Show(!inCall && _c.Incoming.Count == 0);
                if (inCall || _c.Incoming.Count > 0) _nav.SetCollapsed(true, instant: true);
                if (!string.IsNullOrEmpty(_c.Message)) RenderMessage(_c.Message);
                foreach (var inc in _c.Incoming.ToList()) RenderIncoming(inc, now);
                if (_c.Active != null) RenderCall(_c.Active, now);
                if (_c.Paid != null && !(_c.Active != null && _c.Active.Paid && !_c.Active.IsOver)) RenderPaid(_c.Paid, now);
                bool people = _c.Active == null || _c.Active.IsOver;
                if (people) RenderPeople();
                // The call bar appears once someone is picked (an empty, disabled bar only covered the list).
                _bar.Show(people && _c.Incoming.Count == 0 && _selected.Count > 0);
                _start.text = _selected.Count > 0 ? SocialStrings.F("calls.startCount", _selected.Count) : SocialStrings.T("calls.start");
                _start.SetEnabled(_selected.Count > 0 && !_c.Busy && !_c.Dialing && Features.IsAvailable(Features.Voice));
            }

            private void RenderMessage(string key)
            {
                Body.Add(Duo.Notice(SocialStrings.T(key), key.StartsWith("perm.") || key.StartsWith("err.") ? Duo.Tone.Warning : Duo.Tone.Info));
                if (key.EndsWith("Blocked"))
                    Body.Add(El.Div("duo-topup-actions", Duo.Pill(SocialStrings.T("perm.openSettings"), MediaPermission.OpenAppSettings)));
            }

            // ---------------------------------------------------------------- incoming

            private void RenderIncoming(IncomingCall inc, double now)
            {
                var from = inc.Call.FromUserId;
                var name = Hub.Names.Name(from);
                var screen = El.Div("duo-call-screen",
                    Duo.Avatar(name, from, "duo-avatar-call"),
                    El.Text(SocialStrings.T(inc.Paid != null ? "calls.incomingPaid" : "calls.incoming"), "duo-call-kicker"),
                    El.Text(SocialStrings.F("calls.from", name), "duo-call-name"),
                    El.Text(SocialStrings.F("calls.answerIn", (int)Math.Ceiling(inc.Call.SecondsToAnswer(now))), "duo-call-status"));
                if (inc.Paid != null) screen.Add(El.Text(SocialStrings.F("paid.creatorCountdown", (int)Math.Ceiling(inc.Paid.AcceptSecondsLeft(now))), "duo-call-status"));
                Body.Add(screen);
                if (!Hub.Follows.Contains(from)) Body.Add(Duo.Notice(SocialStrings.T("calls.unknownCaller"), Duo.Tone.Warning));
                if (inc.Paid?.ErrorKey != null) Body.Add(Duo.Notice(SocialStrings.T(inc.Paid.ErrorKey), Duo.Tone.Danger));
                bool canAnswer = !inc.Working && inc.Call.CanAccept(now);
                var decline = Control("calls.decline", "close", () => _c.Decline(inc.Call.CallId), "duo-control-red");
                var accept = Control("calls.accept", "duo-phone", () => _c.Accept(inc.Call.CallId), "duo-control-green");
                decline.SetEnabled(canAnswer); accept.SetEnabled(canAnswer);
                Body.Add(El.Div("duo-call-controls duo-call-answer", decline, accept));
                var block = El.Btn(SocialStrings.T("common.block"), () => Hub.Block(from, null), "duo-link duo-destructive duo-call-block");
                block.name = "social-common-block";
                Body.Add(block);
            }

            // ---------------------------------------------------------------- in call

            /// <summary>Round control with its label under it (reference 8 toolbars); keeps the `social-…` names.</summary>
            private static Button Control(string key, string icon, Action clicked, string extra = null)
            {
                var b = El.Btn("", clicked, "duo-control" + (extra != null ? " " + extra : ""));
                b.name = "social-" + key.Replace('.', '-');
                b.tooltip = SocialStrings.T(key);
                var face = El.Div("duo-control-face");
                face.Add(icon == "speaker" || icon == "speaker-off" ? (VisualElement)new SocialControlIcon(icon) : new UiIcon(icon));
                b.Add(El.PassThrough(face));
                b.Add(El.PassThrough(El.Text(SocialStrings.T(key), "duo-control-label")));
                return b;
            }

            private void RenderCall(CallSession call, double now)
            {
                var media = _c.Media;
                if (call.IsOver)
                {
                    var done = El.Div("duo-call-screen", El.Text(SocialStrings.T("calls.end." + call.EndReason.ToString().ToLowerInvariant()), "duo-call-name"));
                    if (call.Paid && _c.Paid != null) Settlement(done, _c.Paid);
                    Body.Add(done);
                    var ok = El.Btn(SocialStrings.T("calls.done"), _c.DismissEnded, "duo-sheet-primary duo-call-done");
                    ok.name = "social-calls-done";
                    Body.Add(ok);
                    return;
                }
                var others = new List<string>();
                if (media != null) others.AddRange(media.Peers.Select(p => p.Identity));
                others.AddRange(call.Invitees.Where(i => media == null || !media.Peers.Any(p => SocialWire.SameId(p.Identity, i))));
                string headline = others.Count > 0 ? string.Join(", ", others.Take(3).Select(Hub.Names.Name)) + (others.Count > 3 ? " +" + (others.Count - 3) : "") : SocialStrings.T("calls.you");
                bool anyVideo = (media?.CameraOn == true) || (media != null && media.Peers.Any(p => p.HasVideo && !p.CameraMuted));
                var screen = El.Div("duo-call-screen");
                if (!anyVideo && others.Count > 0) screen.Add(Duo.Avatar(Hub.Names.Name(others[0]), others[0], "duo-avatar-call"));
                screen.Add(El.Text(headline, "duo-call-name"));
                screen.Add(El.Text(StatusText(call, now), "duo-call-status"));
                screen.Add(El.Div("duo-call-tags", El.Text(SocialStrings.T("common.notRecorded"), "duo-call-tag")));
                if (call.Paid && _c.Paid != null) screen.Add(El.Text(SocialStrings.F("paid.with", Hub.Names.Name(_c.Paid.PeerId)), "duo-call-status"));
                Body.Add(screen);

                if (anyVideo || (media != null && media.Peers.Count > 1))
                {
                    var tiles = El.Div("duo-call-tiles");
                    tiles.Add(Tile(SocialStrings.T("calls.you"), media?.CameraOn == true ? media.LocalPreview : null, media?.LocalSpeaking == true,
                        media != null && !media.MicOn, true, media?.LocalRotation ?? 0));
                    if (media != null)
                        foreach (var p in media.Peers)
                            tiles.Add(Tile(Hub.Names.Name(p.Identity), p.HasVideo && !p.CameraMuted ? p.Video : null, p.Speaking, p.MicMuted || !p.HasAudio, false, 0));
                    foreach (var invited in call.Invitees.Where(i => media == null || !media.Peers.Any(p => SocialWire.SameId(p.Identity, i))))
                        tiles.Add(Tile(Hub.Names.Name(invited) + " · " + SocialStrings.T("calls.ringing"), null, false, false, false, 0));
                    Body.Add(tiles);
                }

                var connected = media != null && media.Connected;
                var mic = Control(media?.MicOn == true ? "calls.micOn" : "calls.micOff", "mic", _c.ToggleMic, media?.MicOn != true ? "duo-control-on" : null);
                mic.SetEnabled(connected && !media.MicBusy);
                var cam = Control(media?.CameraOn == true ? "calls.camOn" : "calls.camOff", "calls", _c.ToggleCamera, media?.CameraOn == true ? "duo-control-on" : null);
                cam.SetEnabled(connected && !media.CameraBusy);
                var spk = Control(media?.SpeakerMuted == true ? "calls.speakerUnmute" : "calls.speakerMute", media?.SpeakerMuted == true ? "speaker-off" : "speaker",
                    _c.ToggleSpeaker, media?.SpeakerMuted == true ? "duo-control-on" : null);
                spk.SetEnabled(connected);
                var hang = Control("calls.hangup", "close", _c.Hangup, "duo-control-red");
                var bar = El.Div("duo-call-controls-bar");
                Duo.AddShadow(bar, "bar");
                bar.Add(El.Div("duo-call-controls", mic, cam, spk, hang));
                Body.Add(bar);
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
                var tile = El.Div("duo-call-tile");
                if (speaking) tile.AddToClassList("duo-call-tile-speaking");
                if (video != null)
                {
                    var img = new Image { image = video, scaleMode = ScaleMode.ScaleAndCrop };
                    img.AddToClassList("duo-call-tile-video");
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
                tile.Add(SocialUi.Text(text, "duo-call-tile-label"));
                return tile;
            }

            // ---------------------------------------------------------------- paid (fan)

            private void RenderPaid(PaidCallFlow flow, double now)
            {
                Body.Add(Duo.SectionHeader(SocialStrings.F("paid.with", Hub.Names.Name(flow.PeerId))));
                var stageKey = "paid.stage." + flow.Stage;
                var tone = flow.Stage == PaidStage.Failed ? Duo.Tone.Danger
                    : flow.Stage == PaidStage.Settled || flow.Stage == PaidStage.Refunded ? Duo.Tone.Info
                    : flow.Stage == PaidStage.Draft ? Duo.Tone.Neutral : Duo.Tone.Info;
                var paid = El.Div("duo-paid");
                paid.name = "calls-paid";
                paid.Add(Duo.Notice(flow.Stage == PaidStage.Ringing ? SocialStrings.F(stageKey, (int)Math.Ceiling(flow.AcceptSecondsLeft(now))) : SocialStrings.T(stageKey), tone));
                if (flow.ErrorKey != null) paid.Add(Duo.Notice(SocialStrings.T(flow.ErrorKey), flow.ErrorKey.StartsWith("err.") ? Duo.Tone.Danger : Duo.Tone.Warning));
                if (flow.CallId != null) paid.Add(Duo.Footnote(SocialStrings.F("paid.callId", flow.CallId), "duo-topup-note"));
                var blocker = _c.SignBlocker();
                var actions = El.Div("duo-topup-actions");
                switch (flow.Stage)
                {
                    case PaidStage.Draft:
                    case PaidStage.Creating:
                        paid.Add(Duo.Footnote(SocialStrings.T("paid.intro"), "duo-topup-note"));
                        if (blocker != null) paid.Add(Duo.Notice(SocialStrings.T(blocker), Duo.Tone.Warning));
                        var minutes = new TextField { value = _minutes, maxLength = 2, isDelayed = false, name = "paid-minutes" };
                        minutes.AddToClassList("duo-field");
                        minutes.textEdition.placeholder = SocialStrings.T("paid.minutes");
                        minutes.RegisterValueChangedCallback(e => _minutes = e.newValue);
                        paid.Add(Duo.GroupHeader(SocialStrings.T("paid.minutes")));
                        paid.Add(minutes);
                        var create = Duo.Pill(SocialStrings.T("paid.create"), () => _c.CreatePaid(_minutes), Duo.PillKind.Primary);
                        create.name = "social-paid-create";
                        create.SetEnabled(!_c.PaidWorking && CallController.PaidCallsAllowedHere);
                        actions.Add(create); actions.Add(Duo.Pill(SocialStrings.T("common.cancel"), _c.ClosePaid));
                        break;
                    case PaidStage.ReadyToSign:
                    case PaidStage.Signing:
                        paid.Add(Duo.Footnote(SocialStrings.T("paid.walletShows"), "duo-topup-note"));
                        if (blocker != null) paid.Add(Duo.Notice(SocialStrings.T(blocker), Duo.Tone.Warning));
                        var sign = Duo.Pill(SocialStrings.T("paid.sign"), _c.SignPaid, Duo.PillKind.Primary);
                        sign.name = "social-paid-sign";
                        sign.SetEnabled(!_c.PaidWorking && blocker == null && flow.CanSign(now));
                        actions.Add(sign); actions.Add(Duo.Pill(SocialStrings.T("common.cancel"), _c.ClosePaid));
                        break;
                    case PaidStage.Confirming:
                        actions.Add(Duo.Pill(SocialStrings.T("paid.check"), _c.CheckPaid));
                        if (_c.CanAbandonPaid) actions.Add(Duo.Pill(SocialStrings.T("paid.stopWaiting"), _c.AbandonPaid));
                        break;
                    case PaidStage.Settled:
                    case PaidStage.Refunded:
                    case PaidStage.Failed:
                        Settlement(paid, flow);
                        actions.Add(Duo.Pill(SocialStrings.T("common.close"), _c.ClosePaid));
                        break;
                }
                if (actions.childCount > 0) paid.Add(actions);
                Body.Add(paid);
            }

            /// <summary>Server values only: minutes used, the creator's amount and the refund (USDC base units → display).</summary>
            private static void Settlement(VisualElement card, PaidCallFlow flow)
            {
                var row = flow.Server;
                if (row == null) return;
                if (row.MinutesUsed.HasValue) card.Add(Duo.Footnote(SocialStrings.T("paid.minutesUsed") + ": " + row.MinutesUsed.Value));
                if (row.CreatorAmount != null) card.Add(Duo.Footnote(SocialStrings.T("paid.creatorAmount") + ": " + UsdcUnits.Format(row.CreatorAmount)));
                if (row.RefundAmount != null) card.Add(Duo.Footnote(SocialStrings.T("paid.refundAmount") + ": " + UsdcUnits.Format(row.RefundAmount)));
            }

            // ---------------------------------------------------------------- people

            private IEnumerable<string> People()
            {
                var ids = new List<string>();
                ids.AddRange(Hub.Calls.Recents.Select(r => r.UserId));
                ids.AddRange(Hub.Messages.Inbox.All.Select(e => e.UserId));
                ids.AddRange(Hub.Follows.Items);
                ids.AddRange(_selected);
                return ids.Where(Callable).Distinct(StringComparer.OrdinalIgnoreCase).Take(40);
            }

            private bool Callable(string id) => SocialWire.IsSafeId(id) && !SocialWire.SameId(id, Hub.Me) && !Hub.Blocks.Contains(id);

            private void Toggle(string user)
            {
                if (!_selected.Remove(user) && _selected.Count < CallSession.MaxInvitees) _selected.Add(user);
                MarkDirty();
            }

            private void RenderPeople()
            {
                Body.Add(Duo.Footnote(SocialStrings.T("calls.intro"), "duo-topup-note"));
                var people = People().ToList();
                if (people.Count == 0)
                {
                    Body.Add(Duo.Empty(SocialStrings.T("calls.contactsEmptyTitle"), SocialStrings.T("calls.contactsEmpty")));
                    RenderCreatorRow();
                    return;
                }
                // Favourites: people you follow, as an avatar strip (tap to add to the call).
                var favs = Hub.Follows.Items.Where(Callable).Take(12).ToList();
                if (favs.Count > 0)
                {
                    Body.Add(Duo.SectionHeader(SocialStrings.T("calls.favourites")));
                    var strip = El.Div("duo-favourites");
                    foreach (var id in favs)
                    {
                        var user = id;
                        var name = Hub.Names.Name(user);
                        var fav = new Button(() => Toggle(user)) { name = "calls-fav-" + user, tooltip = name };
                        fav.AddToClassList("duo-favourite");
                        fav.EnableInClassList("duo-favourite-on", _selected.Contains(user));
                        fav.Add(El.PassThrough(El.Div("duo-favourite-ring", Duo.Avatar(name, user, "duo-avatar-fav"))));
                        fav.Add(El.PassThrough(El.Text(name, "duo-favourite-name")));
                        strip.Add(fav);
                    }
                    var scroller = new ScrollView(ScrollViewMode.Horizontal) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Hidden };
                    scroller.AddToClassList("duo-favourites-scroll");
                    scroller.Add(strip);
                    Body.Add(scroller);
                }
                // Recents, missed in red; then everyone else you can call.
                var recents = Hub.Calls.Recents.Where(r => Callable(r.UserId)).Take(12).ToList();
                if (recents.Count > 0)
                {
                    Body.Add(Duo.SectionHeader(SocialStrings.T("calls.recents")));
                    var list = El.Div("duo-list");
                    foreach (var r in recents) list.Add(PersonRow(r.UserId, r));
                    Duo.MarkLast(list);
                    Body.Add(list);
                }
                var rest = people.Where(id => !favs.Contains(id, StringComparer.OrdinalIgnoreCase) && !recents.Any(r => SocialWire.SameId(r.UserId, id))).ToList();
                if (rest.Count > 0)
                {
                    Body.Add(Duo.SectionHeader(SocialStrings.T("calls.contactsSection")));
                    var list = El.Div("duo-list");
                    foreach (var id in rest) list.Add(PersonRow(id, null));
                    Duo.MarkLast(list);
                    Body.Add(list);
                }
                Body.Add(Duo.Footnote(SocialStrings.F("calls.selected", _selected.Count)));
                RenderCreatorRow();
            }

            private DuoRow PersonRow(string user, RecentCall recent)
            {
                var name = Hub.Names.Name(user);
                bool on = _selected.Contains(user);
                var row = new DuoRow(() => Toggle(user)).Leading(Duo.Avatar(name, user, "duo-avatar-s"));
                bool missed = recent?.Kind == "missed";
                row.Title(name, "duo-row-title-strong" + (missed ? " duo-destructive" : ""));
                string sub = recent != null
                    ? SocialStrings.T("calls.kind." + (recent.Kind ?? "outgoing")) + (recent.Paid ? " · " + SocialStrings.T("calls.paidTag") : "")
                    : Hub.Follows.Contains(user) ? SocialStrings.T("feed.following") : "";
                row.Subtitle(sub);
                if (recent != null && recent.AtMs > 0) row.Value(DuoTime.ListStamp(DuoTime.Local(recent.AtMs), DateTime.Now));
                var check = El.Div("duo-select" + (on ? " duo-select-on" : ""), on ? new DuoGlyph(DuoGlyph.Kind.Check) : null);
                row.Trailing(El.PassThrough(check));
                var info = Duo.Circle(DuoGlyph.Kind.More, SocialStrings.T("calls.person"), () => PersonSheet(user, name), "calls-more-" + user);
                info.AddToClassList("duo-row-circle");
                row.Trailing(info);
                row.name = (recent != null ? "calls-recent-" : "calls-person-") + user;
                row.EnableInClassList("duo-row-selected", on);
                return row;
            }

            private void PersonSheet(string user, string name)
            {
                var sheet = new DuoSheet(name);
                sheet.AddOption(SocialStrings.T("calls.callNow"), () => { _c.StartCall(new List<string> { user }); }, name: "calls-call-" + user);
                var paidOk = CallController.PaidCallsAllowedHere && (_c.Paid == null || _c.Paid.Terminal || _c.Paid.Stage == PaidStage.Draft);
                if (paidOk) sheet.AddOption(SocialStrings.T("paid.book"), () => _c.BeginPaid(user), name: "social-paid-book");
                sheet.AddOption(SocialStrings.T("calls.message"), () => { MessagesApp.PendingPeer = user; Phone?.Open("messages"); });
                sheet.AddSecondary(SocialStrings.T("common.cancel"));
                sheet.Show(_frame);
            }

            // ---------------------------------------------------------------- creator settings (PUT /creator)

            private void RenderCreatorRow()
            {
                var row = new DuoRow(CreatorSheet).Leading(Duo.IconSquare("calls", "teal")).Title(SocialStrings.T("creator.title")).Chevron();
                row.name = "social-creator-title";
                var list = El.Div("duo-list duo-creator-row", row);
                Duo.MarkLast(list);
                Body.Add(list);
            }

            private void CreatorSheet()
            {
                var sheet = new DuoSheet(SocialStrings.T("creator.title"), SocialStrings.T("creator.intro"));
                sheet.name = "calls-creator-sheet";
                var panel = El.Div("duo-tip");
                var saved = _c.Creator;
                if (saved != null)
                    panel.Add(Duo.Footnote(SocialStrings.F("creator.lastSaved", SocialStrings.T(saved.Enabled ? "creator.on" : "creator.off"), UsdcUnits.Format(saved.RatePerMinUsdc), saved.MaxMinutes), "duo-topup-note"));
                var toggle = new DuoToggle(_enabled ?? false, v => _enabled = v) { name = "creator-enabled" };
                panel.Add(El.Div("duo-list", toggle.InRow(SocialStrings.T("creator.enabled"))));
                var rate = new TextField { value = _rate, maxLength = 8, isDelayed = false, name = "creator-rate" };
                rate.AddToClassList("duo-field");
                rate.textEdition.placeholder = SocialStrings.T("creator.rate");
                rate.RegisterValueChangedCallback(e => _rate = e.newValue);
                var max = new TextField { value = _max, maxLength = 2, isDelayed = false, name = "creator-max" };
                max.AddToClassList("duo-field");
                max.textEdition.placeholder = SocialStrings.T("creator.max");
                max.RegisterValueChangedCallback(e => _max = e.newValue);
                panel.Add(Duo.GroupHeader(SocialStrings.T("creator.rate")));
                panel.Add(rate);
                panel.Add(Duo.GroupHeader(SocialStrings.T("creator.max")));
                panel.Add(max);
                var message = El.Text("", "duo-compose-msg");
                Button save = null;
                save = El.Btn(SocialStrings.T("creator.save"), () =>
                {
                    save.SetEnabled(false); message.text = "";
                    _c.SaveCreator(_enabled ?? false, _rate, _max, err =>
                    {
                        save.SetEnabled(true);
                        var s = _c.Creator;
                        message.text = err != null ? SocialStrings.T(err) : SocialStrings.F("creator.saved", UsdcUnits.Format(s?.RatePerMinUsdc), s?.MaxMinutes);
                    });
                }, "duo-sheet-primary");
                save.name = "social-creator-save";
                panel.Add(save);
                panel.Add(message);
                var scroll = El.VerticalScroll("duo-sheet-scroll");
                scroll.Add(panel);
                sheet.AddContent(scroll);
                sheet.AddSecondary(SocialStrings.T("common.close"));
                sheet.Show(_frame);
            }
        }
    }
}
