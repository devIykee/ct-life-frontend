using System;
using System.Linq;
using CTLife.Core;
using CTLife.UI;
using UnityEngine.UIElements;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// Over-the-world social layer (ModalLayer), alive for the whole session: rings incoming calls from city `call_invite`
    /// (accept/decline/countdown, unknown-caller warning) whether or not the phone is open, and shows a small "Ongoing call" /
    /// "In a Space" chip that reopens the phone app while a call or Space continues behind a closed phone.
    /// Owns a hub reference so invites, DMs and Space updates are handled before any social app is opened.
    /// </summary>
    public sealed class SocialOverlay : IDisposable
    {
        private readonly VisualElement _layer;
        private readonly Action<string> _openApp;
        private readonly VisualElement _chipBar;
        private VisualElement _ring;
        private SocialHub _hub;
        private Session _session;
        private bool _disposed;
        private IVisualElementScheduledItem _tick;

        /// <param name="modalLayer">UiRoot.Instance.ModalLayer.</param>
        /// <param name="openApp">Opens a phone app by id (e.g. <c>id => phone.Open(id)</c>).</param>
        public SocialOverlay(VisualElement modalLayer, Action<string> openApp)
        {
            _layer = modalLayer ?? throw new ArgumentNullException(nameof(modalLayer));
            _openApp = openApp;
            MessagesApp.Opener = openApp; // notification banners open the Duo at a conversation
            _chipBar = El.Div("ct-social-chip-bar");
            _chipBar.pickingMode = PickingMode.Ignore;
            SocialUi.Style(_chipBar);
            _layer.Add(_chipBar);
            if (Services.TryGet<Session>(out _session)) _session.Changed += Bind;
            Bind();
            _tick = _layer.schedule.Execute(Render).Every(500);
        }

        private void Bind()
        {
            if (_disposed) return;
            var signedIn = _session != null && _session.IsSignedIn;
            if (_hub != null && (_hub.Disposed || !signedIn || _hub.Me != _session.UserId))
            {
                _hub.Changed -= Render;
                if (!_hub.Disposed) _hub.Release();
                _hub = null;
            }
            if (_hub == null && signedIn)
            {
                _hub = SocialHub.Acquire();
                if (_hub != null) _hub.Changed += Render;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                if (_hub != null) ReplayDevDm();
#endif
            }
            Render();
        }

        private void Render()
        {
            if (_disposed) return;
            if (_hub != null && _hub.Disposed) { Bind(); return; }
            // Messages badge = the inbox's unread count (blocked players excluded); opening a chat clears it.
            DuoNotificationCenter.SetBadge(MessagesApp.AppId, _hub != null ? _hub.Messages.Unread : 0);
            RenderChip();
            RenderRing();
        }

        private string _chipState;

        private void RenderChip()
        {
            string state = null;
            if (_hub != null && _openApp != null)
            {
                var calls = _hub.Calls;
                if (calls.Active != null && !calls.Active.IsOver) state = "calls";
                else if (_hub.Spaces.InSpace) state = "spaces";
            }
            if (state == _chipState) return; // rebuild only on change: never replace a button under a finger
            _chipState = state;
            _chipBar.Clear();
            if (state == "calls") _chipBar.Add(El.Btn(SocialStrings.T("calls.ongoing"), () => _openApp("calls"), "ct-btn ct-btn-primary"));
            else if (state == "spaces") _chipBar.Add(El.Btn(SocialStrings.T("calls.ongoingSpace"), () => _openApp("spaces"), "ct-btn"));
        }

        private string _ringCallId;
        private Label _ringCountdown, _ringPaidCountdown;
        private VisualElement _ringRow;

        /// <summary>Builds the ring sheet once per call and only updates the countdown, so a tap is never lost to a rebuild.</summary>
        private void RenderRing()
        {
            var inc = _hub?.Calls.Incoming.FirstOrDefault();
            if (inc == null) { _ring?.RemoveFromHierarchy(); _ring = null; _ringCallId = null; return; }
            var now = SocialHub.NowMs;
            if (_ring == null || _ringCallId != inc.Call.CallId)
            {
                _ring?.RemoveFromHierarchy();
                _ringCallId = inc.Call.CallId;
                var sheet = El.VerticalScroll("ct-sheet ct-social-ring");
                SocialUi.Style(sheet);
                sheet.Add(El.Div("ct-sheet-handle"));
                var from = inc.Call.FromUserId;
                var callId = inc.Call.CallId;
                sheet.Add(SocialUi.Eyebrow(inc.Paid != null ? "calls.incomingPaid" : "calls.incoming"));
                sheet.Add(SocialUi.H1(SocialStrings.F("calls.from", _hub.Names.Name(from))));
                _ringCountdown = SocialUi.Small("");
                sheet.Add(_ringCountdown);
                _ringPaidCountdown = SocialUi.Small("");
                if (inc.Paid != null) sheet.Add(_ringPaidCountdown);
                if (!_hub.Follows.Contains(from)) sheet.Add(SocialUi.Notice(SocialStrings.T("calls.unknownCaller"), "warn"));
                sheet.Add(SocialUi.Badge(SocialStrings.T("common.notRecorded")));
                _ringRow = SocialUi.Row(
                    SocialUi.Btn("calls.accept", () => { _hub?.Calls.Accept(callId); _openApp?.Invoke("calls"); }, true),
                    SocialUi.Danger("calls.decline", () => _hub?.Calls.Decline(callId)));
                sheet.Add(_ringRow);
                var backdrop = El.Div("ct-modal-backdrop ct-modal-bottom", sheet);
                backdrop.pickingMode = PickingMode.Position;
                _ring = backdrop;
                _layer.Add(backdrop);
            }
            _ringCountdown.text = SocialStrings.F("calls.answerIn", (int)Math.Ceiling(inc.Call.SecondsToAnswer(now)));
            if (inc.Paid != null) _ringPaidCountdown.text = SocialStrings.F("paid.creatorCountdown", (int)Math.Ceiling(inc.Paid.AcceptSecondsLeft(now)));
            _ringRow.SetEnabled(!inc.Working && inc.Call.CanAccept(now));
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        private bool _devDmReplayed;

        /// <summary>
        /// Dev builds only (launch extras need the automation key), a QA tool: since backend da95d7f the server pushes the
        /// realtime `dm` itself after the message commits (DmMessage, `at` in epoch ms; nothing for a blocked pair), so real
        /// banners need no help. This replays one DM onto the Bus as if pushed (extras devDmFrom, devDmId, devDmText
        /// URL-encoded), 8 s after sign-in, for checking the banner without a second account. It takes the shipping path:
        /// MessagesController.OnRealtime → DmBannerPolicy → banner/badge.
        /// </summary>
        private void ReplayDevDm()
        {
            if (_devDmReplayed || !LaunchArgs.TryGet("devDmFrom", out var from) || !SocialWire.IsSafeId(from)) return;
            _devDmReplayed = true;
            string id = LaunchArgs.Get("devDmId", Guid.NewGuid().ToString("N"));
            string text = Uri.UnescapeDataString(LaunchArgs.Get("devDmText", "gm"));
            Log.Info("social", "Dev: replaying DM " + id + " from " + from + " as a realtime push (QA tool)");
            _layer.schedule.Execute(() =>
            {
                if (_disposed) return;
                Bus.Publish(new CTLife.Realtime.DmMessage { messageId = id, fromUserId = from, text = text, at = ServerClock.NowMs });
            }).ExecuteLater(8000);
        }
#endif

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _tick?.Pause();
            if (_session != null) _session.Changed -= Bind;
            if (_hub != null) { _hub.Changed -= Render; if (!_hub.Disposed) _hub.Release(); _hub = null; }
            _ring?.RemoveFromHierarchy();
            _chipBar.RemoveFromHierarchy();
            if (MessagesApp.Opener == _openApp) MessagesApp.Opener = null;
            DuoNotificationCenter.Reset(); // banners and counts belong to this session's identity
        }
    }

    /// <summary>
    /// Social actions for the player profile card (ProfileCard hook, see docs/progress/requests.md): Message, Follow, Video
    /// call and Paid call (devnet builds). No Tip: Cash never moves between players (backend #25). Returns a row to append under Block/Report.
    /// </summary>
    public static class SocialProfileActions
    {
        public static VisualElement Build(string userId, string name, Action close, Action<string> openApp)
        {
            var hub = SocialHub.Current;
            if (hub == null || !SocialWire.IsSafeId(userId) || SocialWire.SameId(userId, hub.Me) || openApp == null) return null;
            hub.Names.Remember(userId, name);
            var row = SocialUi.Row();
            SocialUi.Style(row);
            void Go(string app) { close?.Invoke(); openApp(app); }
            if (Features.IsAvailable(Features.Dms))
                row.Add(SocialUi.Btn("profile.message", () => { MessagesApp.PendingPeer = userId; MessagesApp.PendingAlias = name; Go("messages"); }));
            var following = hub.Follows.Contains(userId);
            row.Add(SocialUi.Btn(following ? "profile.unfollow" : "profile.follow", () => { hub.Timeline.ToggleFollow(userId); close?.Invoke(); }));
            if (Features.IsAvailable(Features.Voice) && !hub.Blocks.Contains(userId))
            {
                row.Add(SocialUi.Btn("profile.call", () => { CallsApp.PendingCallee = userId; Go("calls"); }));
                if (CallController.PaidCallsAllowedHere) row.Add(SocialUi.Btn("profile.paidCall", () => { CallsApp.PendingPaidCreator = userId; Go("calls"); }));
            }
            return row;
        }
    }

    /// <summary>The four Phase 3 phone apps, for PhoneCatalog (see the integration request).</summary>
    public static class SocialApps
    {
        public static PhoneApp[] Create() => new PhoneApp[] { new TimelineApp(), new MessagesApp(), new SpacesStudioApp(), new CallsApp() };
    }
}
