using System.Linq;
using CTLife.Core;
using CTLife.UI;
using UnityEngine.UIElements;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// Messages (feature #26), phone app id `messages`: inbox with X-style message requests (people you don't follow and
    /// haven't answered are kept apart, with a scam warning), threads with "Load older" paging, local echoes with retry,
    /// realtime `dm` delivery, Block and Report on every conversation, scam hints on messages, and a Call shortcut.
    /// Text only; links are never clickable (labels never render rich text).
    /// </summary>
    public sealed class MessagesApp : SocialApp
    {
        public override string Id => "messages";
        public override string Title => SocialStrings.T("dm.title");
        public override string Color => "#26de81";
        public override string Feature => Features.Dms;

        /// <summary>Deep link: open this conversation next time the app opens (profile card, Timeline post).</summary>
        public static string PendingPeer, PendingAlias;

        internal override SocialView CreateView(SocialHub hub, PhoneShell phone)
        {
            var v = new View(hub, phone, PendingPeer, PendingAlias);
            PendingPeer = null; PendingAlias = null;
            return v;
        }

        private sealed class View : SocialView
        {
            private readonly MessagesController _m;
            private readonly TextField _input;
            private readonly Label _inputError;
            private string _draft = "";
            private bool _report, _confirmBlock;
            private string _lastNewest;

            public View(SocialHub hub, PhoneShell phone, string peer, string alias) : base(hub, phone)
            {
                _m = hub.Messages;
                _input = SocialUi.Field("dm.placeholder", "", DmThread.MaxLength, v => _draft = v, multiline: true);
                _inputError = SocialUi.Small("");
                Footer.Add(_input);
                Footer.Add(SocialUi.Row(SocialUi.Btn("common.send", Send, true), _inputError));
                if (SocialWire.IsSafeId(peer)) _m.Open(peer, alias, startedByMe: true);
                else _m.CloseThread();
                _m.WatchInbox(true);
            }

            public override void Dispose()
            {
                if (!Disposed) { _m.WatchInbox(false); _m.CloseThread(); }
                base.Dispose();
            }

            private void Send()
            {
                var peer = _m.Inbox.OpenPeer;
                if (peer == null) return;
                var err = _m.Send(peer, _draft);
                _inputError.text = err == null ? "" : SocialStrings.T(err);
                if (err == null) { _draft = ""; _input.SetValueWithoutNotify(""); ScrollToEnd = true; }
                MarkDirty();
            }

            protected override void Render()
            {
                var peer = _m.Inbox.OpenPeer;
                Footer.Show(peer != null);
                if (peer == null) RenderInbox(); else RenderThread(peer);
            }

            private void RenderInbox()
            {
                Body.Add(SocialUi.Row(SocialUi.H1(SocialStrings.T("dm.inbox")), El.Div("ct-grow"), SocialUi.Btn("common.refresh", _m.RefreshInbox)));
                Body.Add(SocialUi.Notice(SocialStrings.T("dm.safety"), "warn"));
                if (!Features.IsAvailable(Features.Dms)) Body.Add(SocialUi.Notice(SocialStrings.T("dm.degraded"), "error"));
                if (_m.ServerInboxUnavailable) Body.Add(SocialUi.Small(SocialStrings.T("dm.localOnly")));
                if (_m.InboxError != null) Body.Add(SocialUi.Notice(SocialStrings.T(_m.InboxError), "error"));
                if (_m.InboxLoading && !_m.Inbox.All.Any()) Body.Add(SocialUi.Copy("common.loading"));

                var requests = _m.Requests.ToList();
                if (requests.Count > 0)
                {
                    Body.Add(SocialUi.Text(SocialStrings.F("dm.requests", requests.Count), "ct-eyebrow"));
                    Body.Add(SocialUi.Small(SocialStrings.T("dm.requestsHint")));
                    foreach (var e in requests.Take(30)) Entry(e);
                }
                var convos = _m.Conversations.ToList();
                if (convos.Count == 0 && requests.Count == 0) Body.Add(SocialUi.Card(SocialUi.Copy("dm.none")));
                foreach (var e in convos.Take(100)) Entry(e);
            }

            private void Entry(InboxEntry e)
            {
                var user = e.UserId;
                var name = string.IsNullOrEmpty(e.Alias) ? Hub.Names.Name(user) : e.Alias;
                var info = El.Div("ct-grow", SocialUi.H2(name), SocialUi.Small((e.LastMessage ?? "") + (e.LastAtMs > 0 ? " · " + Ago.Short(e.LastAtMs, ServerClock.NowMs) : "")));
                var row = El.Div("ct-social-person", info);
                if (e.Unread > 0) row.Add(SocialUi.Badge(SocialStrings.F("dm.unread", e.Unread)));
                row.Add(SocialUi.Btn("dm.open", () => { _m.Open(user, e.Alias); ScrollToEnd = true; _lastNewest = null; MarkDirty(); }));
                Body.Add(row);
            }

            private void RenderThread(string peer)
            {
                var t = _m.Thread(peer);
                var entry = _m.Inbox.Get(peer);
                var name = Hub.Names.Name(peer);
                Body.Add(SocialUi.Row(SocialUi.Ghost("common.back", () => { _m.CloseThread(); _report = false; _confirmBlock = false; MarkDirty(); }),
                    SocialUi.H2(name), El.Div("ct-grow"),
                    SocialUi.Btn("dm.call", () => { CallsApp.PendingCallee = peer; Phone?.Open("calls"); })));
                var request = entry != null && _m.IsRequest(entry);
                if (request)
                {
                    Body.Add(SocialUi.Notice(SocialStrings.T("dm.requestBanner") + " " + SocialStrings.T("dm.safety"), "warn"));
                    Body.Add(SocialUi.Row(SocialUi.Btn("dm.accept", () => _m.Accept(peer), true), SocialUi.Danger("common.block", () => { _confirmBlock = true; MarkDirty(); }),
                        SocialUi.Ghost("common.report", () => { _report = true; MarkDirty(); })));
                }
                else Body.Add(SocialUi.Row(SocialUi.Ghost("common.block", () => { _confirmBlock = true; MarkDirty(); }), SocialUi.Ghost("common.report", () => { _report = true; MarkDirty(); })));
                if (_confirmBlock)
                    Body.Add(SocialUi.Card(SocialUi.Text(SocialStrings.F("block.confirm", name)),
                        SocialUi.Row(SocialUi.Danger("common.block", () => { _confirmBlock = false; Hub.Block(peer, null); }), SocialUi.Ghost("common.cancel", () => { _confirmBlock = false; MarkDirty(); }))));
                if (_report)
                    Body.Add(SocialUi.ReportPanel(name, reason => { _report = false; Hub.Report("user", peer, reason, null); MarkDirty(); }, () => { _report = false; MarkDirty(); }));
                if (_m.Message != null) Body.Add(SocialUi.Notice(SocialStrings.T(_m.Message), "error"));

                if (t.HasOlder && t.Loaded) { var older = SocialUi.Btn("dm.loadOlder", () => _m.LoadOlder(peer)); older.SetEnabled(!_m.Loading(peer)); Body.Add(older); }
                else if (t.Loaded && t.Messages.Count > 0) Body.Add(SocialUi.Small(SocialStrings.T("dm.noOlder")));
                if (!t.Loaded) Body.Add(SocialUi.Copy("common.loading"));

                var list = El.Div("ct-social-thread");
                foreach (var msg in t.Messages.Skip(System.Math.Max(0, t.Messages.Count - 300)))
                {
                    var mine = SocialWire.SameId(msg.FromUserId, Hub.Me);
                    var bubble = El.Div("ct-social-msg " + (mine ? "ct-social-msg-me" : "ct-social-msg-them"), SocialUi.Text(msg.Text));
                    var meta = msg.State == DmSendState.Sending ? SocialStrings.T("dm.sending")
                        : msg.State == DmSendState.Failed ? SocialStrings.T("dm.failed") + (msg.FailureKey != null ? " · " + SocialStrings.T(msg.FailureKey) : "")
                        : Ago.Short(msg.AtMs, ServerClock.NowMs);
                    bubble.Add(SocialUi.Small(meta));
                    if (!mine) { var flag = SocialUi.Flag(msg.Text); if (flag != null) bubble.Add(flag); }
                    if (msg.State == DmSendState.Failed)
                    {
                        var key = msg.LocalKey;
                        bubble.Add(SocialUi.Row(SocialUi.Btn("dm.retry", () => _m.Retry(peer, key)), SocialUi.Ghost("dm.discard", () => _m.Discard(peer, key))));
                    }
                    list.Add(bubble);
                }
                Body.Add(list);
                // Stick to the bottom when the newest message changes (not when older pages load above).
                var newest = t.Messages.Count > 0 ? t.Messages[t.Messages.Count - 1].MessageId : null;
                if (newest != _lastNewest) { ScrollToEnd = true; _lastNewest = newest; }
            }
        }
    }
}
