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
    /// Messages (feature #26), phone app id `messages`, in the iMessage layout (Duo board #3/#4): inbox with search,
    /// unread dots, avatars, two-line previews and X-style message requests (people you don't follow and haven't
    /// answered are kept apart, with a scam warning); threads with grouped bubbles (tail on the last of a run), centred
    /// times after 15 min gaps, "Load older" paging, local echoes with retry, realtime `dm` delivery, Block and Report
    /// (sheets), scam hints on messages, a Call shortcut, and a glass composer whose send arrow appears with text.
    /// Text only; links are never clickable (labels never render rich text).
    /// </summary>
    public sealed class MessagesApp : SocialApp
    {
        public const string AppId = "messages";
        public override string Id => AppId;
        public override string Title => SocialStrings.T("dm.title");
        public override string Color => "#26de81";
        public override string Feature => Features.Dms;
        public override DuoChrome Chrome => DuoChrome.Plain;

        /// <summary>Deep link: open this conversation next time the app opens (profile card, Timeline post), which the
        /// player starts, so it is never shown as a request.</summary>
        public static string PendingPeer, PendingAlias;

        /// <summary>
        /// Set by <see cref="OpenFromNotification"/>: the pending peer came from a notification, not from the player.
        /// It opens only a known, accepted conversation; a request opens the inbox (requests apart, scam warning)
        /// and is never accepted by the tap.
        /// </summary>
        public static bool PendingFromNotification;

        /// <summary>Opens a phone app by id; set by <see cref="SocialOverlay"/> so a banner tap can open the Duo.</summary>
        public static Action<string> Opener;

        /// <summary>Banner tap: open the Duo at this conversation, through <see cref="DmBannerPolicy.DeepLink"/>.</summary>
        public static void OpenFromNotification(string peer)
        {
            if (!SocialWire.IsSafeId(peer) || Opener == null) return;
            PendingPeer = peer;
            PendingAlias = null;
            PendingFromNotification = true;
            Opener(AppId);
        }

        internal override SocialView CreateView(SocialHub hub, PhoneShell phone)
        {
            var v = new View(hub, phone, PendingPeer, PendingAlias, PendingFromNotification);
            PendingPeer = null; PendingAlias = null; PendingFromNotification = false;
            return v;
        }

        private sealed class View : SocialView
        {
            /// <summary>Bubbles drawn in the open thread (the newest); grows by a page on Load older.</summary>
            private int _threadShown = ThreadPage;
            private string _threadPeer;
            private const int ThreadPage = 80;
            private readonly MessagesController _m;
            private readonly VisualElement _frame;
            private readonly DuoNav _nav;
            private readonly Label _largeTitle;
            private readonly DuoSearchField _search;
            private readonly TextField _input;
            private readonly Label _inputError;
            private readonly Button _send;
            private readonly VisualElement _threadHead;
            private string _draft = "", _query = "", _lastNewest, _headPeer;
            private bool _threadMode;

            public View(SocialHub hub, PhoneShell phone, string peer, string alias, bool fromNotification = false) : base(hub, phone)
            {
                _m = hub.Messages;
                // The shipping scroll view stays the content owner; a Duo frame adds the nav bar and the composer.
                Root.RemoveFromClassList("ct-app-page");
                Root.RemoveFromClassList("ct-social");
                Root.AddToClassList("duo-scroll");
                Root.AddToClassList("duo-messages-scroll");
                Root.contentContainer.AddToClassList("duo-scroll-content");
                _frame = El.Div("duo-page duo-messages");
                _frame.name = "messages-frame";
                _frame.Add(Root);

                _nav = new DuoNav();
                _nav.SetTitle(SocialStrings.T("dm.title"));
                _nav.AddLeft(DuoGlyph.Kind.Back, SocialStrings.T("common.back"), Back, "messages-back");
                _nav.AddRight(DuoGlyph.Kind.More, SocialStrings.T("dm.more"), MoreSheet, "messages-more");
                var call = Duo.Circle(new UiIcon("duo-phone"), SocialStrings.T("dm.call"), Call, "social-dm-call");
                call.AddToClassList("duo-messages-call");
                _nav.Right.Add(call);
                _threadHead = El.Div("duo-thread-head");
                _nav.Center.Add(_threadHead);
                _frame.Add(_nav);

                _largeTitle = Duo.LargeTitle(SocialStrings.T("dm.title"));
                _largeTitle.name = "messages-large-title";
                _search = new DuoSearchField(SocialStrings.T("dm.search"), v => { _query = (v ?? "").Trim(); MarkDirty(); });
                _search.name = "messages-search";
                Header.Add(_largeTitle);
                Header.Add(_search);
                Root.verticalScroller.valueChanged += OnScroll;

                // Composer: glass "+" (conversation actions) and a glass pill; the send arrow shows with text.
                _input = new TextField { maxLength = DmThread.MaxLength, multiline = true, isDelayed = false };
                _input.name = "messages-input";
                _input.AddToClassList("duo-composer-input");
                _input.textEdition.placeholder = SocialStrings.T("dm.composer");
                _inputError = El.Text("", "duo-composer-error");
                _inputError.name = "messages-input-error";
                _input.RegisterValueChangedCallback(e => { _draft = e.newValue ?? ""; ClearError(); SyncComposer(); });
                _send = new Button(Send) { name = "social-common-send", tooltip = SocialStrings.T("common.send") };
                _send.AddToClassList("duo-composer-send");
                _send.Add(El.PassThrough(El.Div("duo-composer-send-face", new DuoGlyph(DuoGlyph.Kind.Send))));
                var plus = Duo.Circle(DuoGlyph.Kind.Plus, SocialStrings.T("dm.options"), ActionsSheet, "messages-plus");
                plus.AddToClassList("duo-glass-circle");
                // Board #4: the pill is glass (soft shadow under a translucent white face), not an outline.
                var field = El.Div("duo-composer-field");
                Duo.AddShadow(field, "pill");
                field.Add(El.Div("duo-composer-field-face", _input, _send));
                Footer.RemoveFromHierarchy();
                Footer.AddToClassList("duo-composer");
                Footer.style.backgroundImage = Background.FromTexture2D(Duo.Fade);
                Footer.Add(_inputError);
                Footer.Add(El.Div("duo-composer-row", plus, field));
                _frame.Add(Footer);
                SyncComposer();

                if (fromNotification)
                {
                    var entry = SocialWire.IsSafeId(peer) ? _m.Inbox.Get(peer) : null;
                    var target = DmBannerPolicy.DeepLink(peer, entry != null, SocialWire.IsSafeId(peer) && _m.Hidden(peer),
                        entry != null && _m.IsRequest(entry));
                    if (target == DmBannerPolicy.Target.Thread) _m.Open(peer, alias, startedByMe: false);
                    else _m.CloseThread();
                }
                else if (SocialWire.IsSafeId(peer)) _m.Open(peer, alias, startedByMe: true);
                else _m.CloseThread();
                _m.WatchInbox(true);
            }

            public override VisualElement Container => _frame;

            public override void Dispose()
            {
                if (!Disposed)
                {
                    _m.WatchInbox(false);
                    _m.CloseThread();
                    DuoNotificationCenter.Queue.SetForegroundThread(AppId, null);
                }
                base.Dispose();
            }

            private void Back()
            {
                if (_m.Inbox.OpenPeer != null) { _m.CloseThread(); MarkDirty(); }
                else Phone?.Back();
            }

            private void Call()
            {
                var peer = _m.Inbox.OpenPeer;
                if (peer == null) return;
                CallsApp.PendingCallee = peer;
                Phone?.Open("calls");
            }

            private void OnScroll(float offset)
            {
                if (_threadMode) return;
                float threshold = _largeTitle.layout.yMax - _nav.layout.height * .35f;
                _nav.SetCollapsed(!float.IsNaN(threshold) && offset > Mathf.Max(8f, threshold));
            }

            private bool _errorShown;

            private void ClearError() { _errorShown = false; _inputError.text = ""; }

            private void SyncComposer()
            {
                bool hasText = !string.IsNullOrWhiteSpace(_draft);
                _send.Show(hasText);
                _frame.EnableInClassList("duo-composer-has-text", hasText);
                if (!_errorShown)
                {
                    // Inline count near the limit (no "500 max" label).
                    int left = DmThread.MaxLength - _draft.Length;
                    _inputError.text = left <= 50 ? SocialStrings.F("dm.left", Math.Max(0, left)) : "";
                }
                _inputError.EnableInClassList("duo-composer-count", !_errorShown);
                _inputError.Show(!string.IsNullOrEmpty(_inputError.text));
            }

            private void Send()
            {
                var peer = _m.Inbox.OpenPeer;
                if (peer == null) return;
                Bus.Publish(new SfxSignal("sfx_ui_tap"));
                var err = _m.Send(peer, _draft);
                _errorShown = err != null;
                _inputError.text = err == null ? "" : SocialStrings.T(err);
                if (err == null) { _draft = ""; _input.SetValueWithoutNotify(""); ScrollToEnd = true; }
                SyncComposer();
                MarkDirty();
            }

            // ------------------------------------------------------------------ sheets

            private void MoreSheet()
            {
                var sheet = new DuoSheet(SocialStrings.T("dm.title"));
                sheet.AddOption(SocialStrings.T("common.refresh"), _m.RefreshInbox, name: "messages-refresh");
                sheet.AddSecondary(SocialStrings.T("common.cancel"));
                sheet.Show(_frame);
            }

            private void ActionsSheet()
            {
                var peer = _m.Inbox.OpenPeer;
                if (peer == null) return;
                var name = Hub.Names.Name(peer);
                var entry = _m.Inbox.Get(peer);
                var sheet = new DuoSheet(name, SocialStrings.T("dm.options"));
                if (entry != null && _m.IsRequest(entry)) sheet.AddOption(SocialStrings.T("dm.accept"), () => _m.Accept(peer), name: "messages-accept");
                sheet.AddOption(SocialStrings.T("dm.call"), Call, name: "messages-call");
                sheet.AddOption(SocialStrings.T("common.report"), () => ReportSheet(peer, name), name: "messages-report");
                sheet.AddOption(SocialStrings.T("common.block"), () => BlockSheet(peer, name), destructive: true, name: "messages-block");
                sheet.AddSecondary(SocialStrings.T("common.cancel"));
                sheet.Show(_frame);
            }

            private void BlockSheet(string peer, string name)
            {
                var sheet = new DuoSheet(SocialStrings.F("block.confirm", name));
                sheet.AddPrimary(SocialStrings.T("common.block"), () => Hub.Block(peer, null), destructive: true, name: "messages-block-confirm");
                sheet.AddSecondary(SocialStrings.T("common.cancel"));
                sheet.Show(_frame);
            }

            private void ReportSheet(string peer, string name)
            {
                var sheet = new DuoSheet(SocialStrings.F("common.reportTitle", name), SocialStrings.T("common.reportWhy"));
                var reasons = Services.TryGet<ConfigStore>(out var cfg) ? cfg.Registries.ReportReasons : null;
                if (reasons != null)
                    foreach (var r in reasons)
                    {
                        var id = r.id;
                        sheet.AddOption(Strings.ReportReason(r.id, r.name), () => { Hub.Report("user", peer, id, null); MarkDirty(); });
                    }
                sheet.AddSecondary(SocialStrings.T("common.cancel"));
                sheet.Show(_frame);
            }

            // ------------------------------------------------------------------ render

            protected override void Render()
            {
                var peer = _m.Inbox.OpenPeer;
                bool thread = peer != null;
                _threadMode = thread;
                _frame.EnableInClassList("duo-messages-thread", thread);
                Header.Show(!thread);
                Footer.Show(thread);
                _nav.Q("messages-more")?.Show(!thread);
                _nav.Q("social-dm-call")?.Show(thread);
                DuoNotificationCenter.Queue.SetForegroundThread(AppId, peer);
                if (thread) { _nav.SetCollapsed(true, instant: true); RenderThread(peer); }
                else
                {
                    if (_headPeer != null) { _headPeer = null; _threadHead.Clear(); _nav.SetCollapsed(false, instant: true); }
                    OnScroll(Root.scrollOffset.y);
                    RenderInbox();
                }
            }

            private void RenderInbox()
            {
                if (!Features.IsAvailable(Features.Dms)) Body.Add(Duo.Notice(SocialStrings.T("dm.degraded"), Duo.Tone.Danger));
                if (_m.ServerInboxUnavailable) Body.Add(Duo.Footnote(SocialStrings.T("dm.localOnly")));
                if (_m.InboxError != null) Body.Add(Duo.Notice(SocialStrings.T(_m.InboxError), Duo.Tone.Danger));
                if (_m.InboxLoading && !_m.Inbox.All.Any()) Body.Add(Duo.Footnote(SocialStrings.T("common.loading")));

                bool Match(InboxEntry e)
                {
                    if (_query.Length == 0) return true;
                    return Name(e).IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0
                        || (e.LastMessage ?? "").IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                var convos = _m.Conversations.Where(Match).Take(100).ToList();
                var requests = _m.Requests.Where(Match).Take(30).ToList();
                if (convos.Count == 0 && requests.Count == 0)
                {
                    if (_query.Length > 0) Body.Add(Duo.Footnote(SocialStrings.F("dm.noMatch", _query)));
                    else if (!_m.InboxLoading || _m.Inbox.All.Any()) Body.Add(Duo.Empty(SocialStrings.T("dm.none"), SocialStrings.T("dm.noneHint")));
                }
                var list = El.Div("duo-list");
                foreach (var e in convos) list.Add(Entry(e));
                Duo.MarkLast(list);
                Body.Add(list);
                if (requests.Count > 0)
                {
                    Body.Add(Duo.SectionHeader(SocialStrings.F("dm.requests", requests.Count)));
                    Body.Add(El.Text(SocialStrings.T("dm.requestsHint"), "duo-messages-hint"));
                    var rl = El.Div("duo-list");
                    foreach (var e in requests) rl.Add(Entry(e));
                    Duo.MarkLast(rl);
                    Body.Add(rl);
                }
                // The scam warning stays on the inbox, quietly, under the list.
                Body.Add(Duo.Notice(SocialStrings.T("dm.safety"), Duo.Tone.Warning).With("duo-messages-safety"));
            }

            private string Name(InboxEntry e) => string.IsNullOrEmpty(e.Alias) ? Hub.Names.Name(e.UserId) : e.Alias;

            private DuoRow Entry(InboxEntry e)
            {
                var user = e.UserId;
                var alias = e.Alias;
                var name = Name(e);
                var row = new DuoRow(() => { _m.Open(user, alias); ScrollToEnd = true; _lastNewest = null; MarkDirty(); });
                row.name = "messages-row-" + user;
                row.AddToClassList("duo-conv");
                bool unread = e.Unread > 0;
                row.EnableInClassList("duo-conv-unread", unread);
                row.tooltip = unread ? name + " · " + SocialStrings.F("dm.unread", e.Unread) : name;
                var dot = El.Div("duo-conv-dot");
                row.Leading(El.PassThrough(El.Div("duo-conv-lead", dot, Duo.Avatar(name, user, "duo-avatar-m"))));
                var time = e.LastAtMs > 0 ? DuoTime.ListStamp(DuoTime.Local(e.LastAtMs), DateTime.Now) : "";
                var top = El.Div("duo-conv-top", El.Text(name, "duo-row-title duo-row-title-strong duo-conv-name"),
                    El.Text(time, "duo-conv-time"), new DuoGlyph(DuoGlyph.Kind.Chevron).With("duo-row-chevron"));
                row.TextColumn.Add(El.PassThrough(top));
                row.Subtitle(e.LastMessage ?? "", twoLines: true);
                return row;
            }

            private void ThreadHead(string peer)
            {
                var name = Hub.Names.Name(peer);
                if (_headPeer == peer)
                {
                    var label = _threadHead.Q<Label>(className: "duo-thread-name");
                    if (label != null) label.text = name;
                    return;
                }
                _headPeer = peer;
                _threadHead.Clear();
                var who = new Button(ActionsSheet) { name = "messages-thread-name", tooltip = SocialStrings.T("dm.options") };
                who.AddToClassList("duo-thread-who");
                who.Add(Duo.Avatar(name, peer, "duo-avatar-header"));
                var line = El.Div("duo-thread-name-row", El.Text(name, "duo-thread-name"), new DuoGlyph(DuoGlyph.Kind.Chevron).With("duo-thread-name-chevron"));
                who.Add(El.PassThrough(line));
                _threadHead.Add(who);
            }

            private void RenderThread(string peer)
            {
                var t = _m.Thread(peer);
                var entry = _m.Inbox.Get(peer);
                var name = Hub.Names.Name(peer);
                ThreadHead(peer);
                if (entry != null && _m.IsRequest(entry))
                {
                    Body.Add(Duo.Notice(SocialStrings.T("dm.requestBanner") + " " + SocialStrings.T("dm.safety"), Duo.Tone.Warning));
                    var accept = Duo.Pill(SocialStrings.T("dm.accept"), () => _m.Accept(peer), Duo.PillKind.Primary);
                    accept.name = "social-dm-accept";
                    Body.Add(El.Div("duo-request-actions", accept,
                        Duo.Pill(SocialStrings.T("common.block"), () => BlockSheet(peer, name), Duo.PillKind.Destructive),
                        Duo.Pill(SocialStrings.T("common.report"), () => ReportSheet(peer, name))));
                }
                if (_m.Message != null) Body.Add(Duo.Notice(SocialStrings.T(_m.Message), Duo.Tone.Danger));

                if (_threadPeer != peer) { _threadPeer = peer; _threadShown = ThreadPage; }
                if (t.Messages.Count > _threadShown)
                {
                    // Older messages already here: draw them before asking the server (each render rebuilds every bubble).
                    var earlier = El.Btn(SocialStrings.T("dm.loadOlder"), () => { _threadShown += ThreadPage; MarkDirty(); }, "duo-link duo-load-older");
                    earlier.name = "social-dm-showOlder";
                    Body.Add(earlier);
                }
                else if (t.HasOlder && t.Loaded)
                {
                    var older = El.Btn(SocialStrings.T("dm.loadOlder"), () => { _threadShown += ThreadPage; _m.LoadOlder(peer); }, "duo-link duo-load-older");
                    older.name = "social-dm-loadOlder";
                    older.SetEnabled(!_m.Loading(peer));
                    Body.Add(older);
                }
                else if (t.Loaded && t.Messages.Count > 0) Body.Add(Duo.Footnote(SocialStrings.T("dm.noOlder")));
                if (!t.Loaded) Body.Add(Duo.Footnote(SocialStrings.T("common.loading")));

                var list = El.Div("duo-thread");
                var rows = t.Messages.Skip(Math.Max(0, t.Messages.Count - _threadShown)).ToList();
                var now = DateTime.Now;
                for (int i = 0; i < rows.Count; i++)
                {
                    var msg = rows[i];
                    var prev = i > 0 ? rows[i - 1] : null;
                    var next = i + 1 < rows.Count ? rows[i + 1] : null;
                    bool mine = SocialWire.SameId(msg.FromUserId, Hub.Me);
                    bool stamp = DuoTime.NeedsSeparator(prev?.AtMs ?? 0, msg.AtMs, prev == null);
                    if (stamp) list.Add(El.Text(DuoTime.ThreadStamp(DuoTime.Local(msg.AtMs), now), "duo-thread-time"));
                    bool first = stamp || SocialWire.SameId(prev.FromUserId, Hub.Me) != mine;
                    bool last = next == null || SocialWire.SameId(next.FromUserId, Hub.Me) != mine
                        || DuoTime.NeedsSeparator(msg.AtMs, next.AtMs, false) || msg.State != DmSendState.Sent;
                    var bubble = El.Div("duo-bubble " + (mine ? "duo-bubble-out" : "duo-bubble-in"), SocialUi.Text(msg.Text, "duo-bubble-text"));
                    bubble.EnableInClassList("duo-bubble-tail", last);
                    bubble.EnableInClassList("duo-bubble-failed", msg.State == DmSendState.Failed);
                    var row = El.Div("duo-bubble-row " + (mine ? "duo-bubble-row-out" : "duo-bubble-row-in"), bubble);
                    if (first && !stamp) row.AddToClassList("duo-bubble-row-gap");
                    list.Add(row);
                    if (msg.State == DmSendState.Sending) list.Add(El.Text(SocialStrings.T("dm.sending"), "duo-bubble-status duo-bubble-status-out"));
                    else if (msg.State == DmSendState.Failed)
                    {
                        var key = msg.LocalKey;
                        var why = SocialStrings.T("dm.notSent") + (msg.FailureKey != null ? " · " + SocialStrings.T(msg.FailureKey) : "");
                        var retry = El.Btn(SocialStrings.T("dm.retry"), () => _m.Retry(peer, key), "duo-link duo-bubble-action");
                        retry.name = "social-dm-retry";
                        var discard = El.Btn(SocialStrings.T("dm.discard"), () => _m.Discard(peer, key), "duo-link duo-bubble-action duo-destructive");
                        list.Add(El.Div("duo-bubble-failure", El.Text(why, "duo-bubble-status duo-bubble-status-failed"), retry, discard));
                    }
                    if (!mine)
                    {
                        var flag = SocialUi.Flag(msg.Text);
                        if (flag != null) list.Add(El.Text(flag.text, "duo-bubble-flag"));
                    }
                }
                Body.Add(list);
                // Stick to the bottom when the newest message changes (not when older pages load above).
                var newest = rows.Count > 0 ? rows[rows.Count - 1].MessageId : null;
                if (newest != _lastNewest) { ScrollToEnd = true; _lastNewest = newest; }
            }
        }
    }
}
