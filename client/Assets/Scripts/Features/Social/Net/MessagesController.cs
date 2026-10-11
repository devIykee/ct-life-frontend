using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CTLife.Core;
using CTLife.UI;
using CTLife.Net;
using CTLife.Realtime;
using Newtonsoft.Json.Linq;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// DMs (feature #26): the inbox (GET /dms + realtime `dm` + conversations remembered on this device), open threads with
    /// backwards paging and deduplication (<see cref="DmThread"/>), sending with retained Idempotency-Keys and local echoes,
    /// blocked users hidden, X-style message requests for people the player neither follows nor answered.
    /// The backend's city room never pushes `dm`, so an open thread is also polled.
    /// </summary>
    public sealed class MessagesController : IDisposable
    {
        public const double ThreadPollMs = 8000, InboxPollMs = 30000;

        private readonly SocialHub _hub;
        public readonly DmInbox Inbox = new DmInbox();
        private readonly Dictionary<string, DmThread> _threads = new Dictionary<string, DmThread>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool InboxLoading { get; private set; }
        public string InboxError { get; private set; }
        /// <summary>The server's thread list is unavailable (route missing or refused); the list shows device-known threads.</summary>
        public bool ServerInboxUnavailable { get; private set; }
        public string Message { get; set; }
        private double _inboxAt = double.NegativeInfinity, _threadAt = double.NegativeInfinity;
        private int _inboxViewers;
        private bool _disposed;

        public MessagesController(SocialHub hub)
        {
            _hub = hub;
            LoadDevice();
        }

        private void Notify() { if (!_disposed) _hub.NotifyChanged(); }

        private void LoadDevice()
        {
            var known = _hub.Store.GetJson("threads");
            if (known == null) return;
            foreach (var p in known.Properties())
            {
                if (!SocialWire.IsSafeId(p.Name) || _hub.Blocks.Contains(p.Name)) continue;
                var e = Inbox.Touch(p.Name, SocialWire.Str(p.Value["alias"]), SocialWire.BoolOr(p.Value["replied"], false));
                e.Accepted = SocialWire.BoolOr(p.Value["accepted"], false);
                e.LastAtMs = SocialWire.OptLong(p.Value["at"]) ?? 0;
                e.LastMessage = SocialWire.Plain(SocialWire.Str(p.Value["last"]), 120);
                if (!string.IsNullOrEmpty(e.Alias)) _hub.Names.Remember(e.UserId, e.Alias);
            }
        }

        private void SaveDevice()
        {
            var o = new JObject();
            foreach (var e in Inbox.All.Take(100))
                o[e.UserId] = new JObject { ["alias"] = e.Alias, ["replied"] = e.Replied, ["accepted"] = e.Accepted, ["at"] = e.LastAtMs, ["last"] = e.LastMessage };
            _hub.Store.SetJson("threads", o);
        }

        public bool Hidden(string userId) => _hub.Blocks.Contains(userId);
        public bool IsRequest(InboxEntry e) => Inbox.IsRequest(e, _hub.Follows.Contains);
        public int Unread => Inbox.UnreadTotal(Hidden);

        public IEnumerable<InboxEntry> Conversations => Inbox.All.Where(e => !Hidden(e.UserId) && !IsRequest(e));
        public IEnumerable<InboxEntry> Requests => Inbox.All.Where(e => !Hidden(e.UserId) && IsRequest(e));

        public DmThread Thread(string peer)
        {
            if (string.IsNullOrEmpty(peer)) return null;
            if (!_threads.TryGetValue(peer, out var t)) _threads[peer] = t = new DmThread(_hub.Me, peer);
            return t;
        }

        public bool Loading(string peer) => peer != null && _loading.Contains(peer);

        // ---------------------------------------------------------------- inbox

        public void WatchInbox(bool on)
        {
            _inboxViewers = Math.Max(0, _inboxViewers + (on ? 1 : -1));
            if (on) RefreshInbox();
        }

        public void RefreshInbox()
        {
            if (InboxLoading || _disposed || !Features.IsAvailable(Features.Dms)) return;
            InboxLoading = true; Notify();
            _hub.Run(async ct =>
            {
                try
                {
                    var rows = await _hub.Api.Threads(ct);
                    Inbox.ApplyThreads(rows.Where(r => !Hidden(r.UserId)));
                    foreach (var r in rows) _hub.Names.Remember(r.UserId, r.Alias);
                    ServerInboxUnavailable = false;
                    InboxError = null;
                    SaveDevice();
                }
                finally { InboxLoading = false; _inboxAt = SocialHub.NowMs; Notify(); }
            }, e =>
            {
                InboxLoading = false; _inboxAt = SocialHub.NowMs;
                // Backend checkout: GET /dms without `peerUserId` answers VALIDATION_FAILED (it lists one conversation only).
                ServerInboxUnavailable = e.Kind == ErrorKind.RouteMissing || e.Is("VALIDATION_FAILED");
                InboxError = ServerInboxUnavailable ? null : e.CopyKey;
                Notify();
            });
        }

        /// <summary>
        /// Opens a conversation. <paramref name="startedByMe"/>: the player started it (profile card, Timeline post), so it is
        /// never shown as a message request; opening an existing request from the inbox does not accept it.
        /// </summary>
        public void Open(string peer, string alias = null, bool startedByMe = false)
        {
            if (!SocialWire.IsSafeId(peer) || SocialWire.SameId(peer, _hub.Me)) return;
            var entry = Inbox.Touch(peer, alias, false);
            if (startedByMe) entry.Accepted = true;
            _hub.Names.Remember(peer, alias);
            Inbox.OpenPeer = peer;
            Inbox.MarkRead(peer);
            var t = Thread(peer);
            if (!t.Loaded) LoadPage(peer, false);
            SaveDevice();
            Notify();
        }

        public void CloseThread() { Inbox.OpenPeer = null; Notify(); }

        public void Accept(string peer)
        {
            var e = Inbox.Get(peer);
            if (e == null) return;
            e.Accepted = true;
            SaveDevice();
            Notify();
        }

        public void LoadOlder(string peer)
        {
            var t = Thread(peer);
            if (t == null || !t.HasOlder || Loading(peer)) return;
            LoadPage(peer, true);
        }

        private void LoadPage(string peer, bool older)
        {
            if (!_loading.Add(peer)) return;
            var t = Thread(peer);
            var cursor = older ? t.OlderCursor : null;
            Notify();
            _hub.Run(async ct =>
            {
                try
                {
                    var rows = await _hub.Api.Messages(peer, cursor, ct);
                    t.ApplyPage(rows.Where(r => !Hidden(r.FromUserId)), older);
                    var newest = t.Messages.LastOrDefault(r => !r.IsLocal);
                    if (newest != null)
                    {
                        var e = Inbox.Touch(peer, null, t.Messages.Any(r => SocialWire.SameId(r.FromUserId, _hub.Me) && !r.IsLocal));
                        if (newest.AtMs >= e.LastAtMs) { e.LastAtMs = newest.AtMs; e.LastMessage = SocialWire.Plain(newest.Text, 120); }
                    }
                    Message = null;
                }
                finally { _loading.Remove(peer); _threadAt = SocialHub.NowMs; Notify(); }
            }, e => { _loading.Remove(peer); _threadAt = SocialHub.NowMs; Message = e.CopyKey; Notify(); });
        }

        // ---------------------------------------------------------------- send

        public string Send(string peer, string input)
        {
            if (!Features.IsAvailable(Features.Dms)) return "dm.degraded";
            if (Hidden(peer)) return "dm.blockedThem";
            var err = DmThread.Validate(input, out var text);
            if (err != null) return err;
            var key = new RetainedKey();
            var k = Guid.NewGuid().ToString("N"); // local echo id; the Idempotency-Key is bound to the exact request by `key`
            var t = Thread(peer);
            t.AddLocal(text, k, ServerClock.NowMs);
            Inbox.ApplyOutgoing(peer, text, (long)ServerClock.NowMs);
            SaveDevice();
            Notify();
            Deliver(peer, text, k, key);
            return null;
        }

        private void Deliver(string peer, string text, string localKey, RetainedKey key)
        {
            var t = Thread(peer);
            var row = t.Local(localKey);
            if (row != null) { row.State = DmSendState.Sending; row.FailureKey = null; }
            _pendingKeys[localKey] = key;
            _hub.Run(async ct =>
            {
                var server = await _hub.Api.SendDm(peer, text, key, ct);
                key.Complete();
                _pendingKeys.Remove(localKey);
                t.Confirm(localKey, server);
                Notify();
            }, e =>
            {
                if (!e.OutcomeUnknown) key.Complete();
                t.Fail(localKey, e.Is("BLOCKED") ? "dm.blockedByThem" : e.CopyKey, e.OutcomeUnknown);
                Notify();
            });
        }

        private readonly Dictionary<string, RetainedKey> _pendingKeys = new Dictionary<string, RetainedKey>();

        /// <summary>Retry a failed message: same key after an unknown outcome (the server replays), else a new message.</summary>
        public void Retry(string peer, string localKey)
        {
            var t = Thread(peer);
            var row = t?.Local(localKey);
            if (row == null || row.State != DmSendState.Failed) return;
            if (row.RetrySameKey && _pendingKeys.TryGetValue(localKey, out var key)) { Deliver(peer, row.Text, localKey, key); return; }
            t.Remove(localKey);
            _pendingKeys.Remove(localKey);
            Send(peer, row.Text);
        }

        public void Discard(string peer, string localKey)
        {
            Thread(peer)?.Remove(localKey);
            _pendingKeys.Remove(localKey);
            Notify();
        }

        // ---------------------------------------------------------------- realtime and lifecycle

        public void OnRealtime(DmMessage m)
        {
            if (_disposed || m == null || !SocialWire.IsSafeId(m.fromUserId) || string.IsNullOrEmpty(m.messageId)) return;
            if (Hidden(m.fromUserId)) return; // the server should not deliver these; never show them if it does
            var atMs = m.at > 0 ? (long)m.at : (long)ServerClock.NowMs;
            var isNew = true;
            if (_threads.TryGetValue(m.fromUserId, out var t)) isNew = t.ApplyRealtime(m.messageId, m.fromUserId, m.text, atMs);
            if (!isNew) return;
            var entry = Inbox.ApplyIncoming(m.fromUserId, m.text, atMs);
            SaveDevice();
            if (!SocialWire.SameId(Inbox.OpenPeer, m.fromUserId))
            {
                Bus.Publish(new SfxSignal("sfx_dm_ping"));
                var from = m.fromUserId;
                // iOS-style banner over the game (DuoNotificationCenter), gated by DmBannerPolicy: never for a blocked
                // sender or a message request (badge only: requests are read in the inbox under the scam warning);
                // a scam-flagged text shows its hint instead. The center also suppresses it while the Duo shows this
                // chat or the inbox, and under Do Not Disturb.
                var name = !string.IsNullOrWhiteSpace(entry?.Alias) ? entry.Alias : _hub.Names.Name(from);
                var banner = DmBannerPolicy.For(from, name, m.text, Hidden(from), IsRequest(entry));
                if (!banner.Show) Log.Info("notify", "messages banner withheld (request, blocked or unsafe id); badge only");
                if (banner.Show)
                {
                    var n = new DuoNotification
                    {
                        AppId = MessagesApp.AppId, ThreadKey = from, Title = banner.Title, Body = banner.Body,
                        Open = () => MessagesApp.OpenFromNotification(from),
                    };
                    var posted = DuoNotificationCenter.Post(n);
                    // The sender's alias may still be loading ("Player 01a142"): retitle the live banner when it lands.
                    if ((posted == DuoPostResult.Shown || posted == DuoPostResult.Replaced) && name == NameCache.Short(from))
                    {
                        Action retitle = null;
                        retitle = () =>
                        {
                            var queue = DuoNotificationCenter.Queue;
                            var resolved = _hub.Names.Name(from);
                            if (!queue.Contains(n.Id) || resolved != NameCache.Short(from)) _hub.Names.Changed -= retitle;
                            if (queue.Contains(n.Id) && resolved != NameCache.Short(from)) queue.Retitle(n.Id, resolved);
                        };
                        _hub.Names.Changed += retitle;
                    }
                }
            }
            Notify();
        }

        public void OnBlocked(string userId)
        {
            Inbox.Remove(userId);
            _threads.Remove(userId);
            if (SocialWire.SameId(Inbox.OpenPeer, userId)) Inbox.OpenPeer = null;
            SaveDevice();
            Notify();
        }

        public void OnOnline() { _inboxAt = double.NegativeInfinity; _threadAt = double.NegativeInfinity; }

        public void Tick(double now)
        {
            if (_disposed || _hub.Offline || !Features.IsAvailable(Features.Dms)) return;
            var open = Inbox.OpenPeer;
            if (open != null && now - _threadAt >= ThreadPollMs && !Loading(open)) LoadPage(open, false);
            if (_inboxViewers > 0 && open == null && now - _inboxAt >= InboxPollMs && !ServerInboxUnavailable) RefreshInbox();
        }

        public void Dispose() { _disposed = true; }
    }

    /// <summary>
    /// The in-game Timeline (feature #27): feed per scope (global | following), posting (≤ 280) with a retained key, follows
    /// (POST/DELETE /follows/{userId}, remembered on this device because the contract cannot read them back), blocked
    /// authors hidden. Tips (feature #28) live here too because they start from a post or a profile.
    /// </summary>
    public sealed class TimelineController : IDisposable
    {
        public const double PollMs = 30000;
        private readonly SocialHub _hub;
        private readonly Dictionary<string, FeedTimeline> _feeds = new Dictionary<string, FeedTimeline>();
        private readonly Dictionary<string, double> _loadedAt = new Dictionary<string, double>();
        public string Scope { get; private set; } = "global";
        public bool Loading { get; private set; }
        public bool Posting { get; private set; }
        public string Error { get; private set; }
        public string Message { get; set; }
        private readonly RetainedKey _postKey = new RetainedKey();
        private string _pendingPost;
        public string PendingPost => _pendingPost;
        private readonly HashSet<string> _followBusy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _viewers;
        private bool _disposed;

        public TimelineController(SocialHub hub) { _hub = hub; }

        private void Notify() { if (!_disposed) _hub.NotifyChanged(); }

        public FeedTimeline Feed => _feeds.TryGetValue(Scope, out var f) ? f : (_feeds[Scope] = new FeedTimeline(Scope));

        public IReadOnlyList<FeedPostRow> Visible => Feed.Visible(_hub.Blocks.Contains);

        public void Watch(bool on) { _viewers = Math.Max(0, _viewers + (on ? 1 : -1)); if (on) Refresh(); }

        public void SetScope(string scope)
        {
            Scope = scope == "following" ? "following" : "global";
            Error = null;
            Refresh();
            Notify();
        }

        public void Refresh()
        {
            if (Loading || _disposed || !Features.IsAvailable(Features.Feed)) return;
            Loading = true; Notify();
            var scope = Scope;
            _hub.Run(async ct =>
            {
                try
                {
                    var page = await _hub.Api.Feed(scope, ct);
                    foreach (var p in page) _hub.Names.Remember(p.UserId, p.Alias);
                    _feeds.TryGetValue(scope, out var f);
                    if (f == null) _feeds[scope] = f = new FeedTimeline(scope);
                    f.Replace(page);
                    Error = null;
                }
                finally { Loading = false; _loadedAt[scope] = SocialHub.NowMs; Notify(); }
            }, e => { Loading = false; _loadedAt[scope] = SocialHub.NowMs; Error = e.Kind == ErrorKind.RouteMissing ? "err.routeMissing" : e.CopyKey; Notify(); });
        }

        /// <summary>POST /feed/posts. Unknown outcome keeps the text and key so a retry is replayed, not posted twice.</summary>
        public string Post(string input)
        {
            if (Posting) return "feed.posting";
            if (!Features.IsAvailable(Features.Feed)) return "feed.degraded";
            var err = FeedTimeline.Validate(_pendingPost ?? input, out var text);
            if (err != null) return err;
            _pendingPost = text;
            Posting = true; Message = null; Notify();
            _hub.Run(async ct =>
            {
                FeedPostRow post;
                try { post = await _hub.Api.Post(text, _postKey, ct); }
                finally { Posting = false; }
                _postKey.Complete(); _pendingPost = null;
                if (string.IsNullOrWhiteSpace(post.Alias)) post.Alias = _hub.Names.Name(_hub.Me);
                Feed.Add(post);
                if (Scope != "global" && _feeds.TryGetValue("global", out var g)) g.Add(post);
                Message = "feed.posted";
                Bus.Publish(new SfxSignal("sfx_ui_success"));
                Notify();
            }, e =>
            {
                Posting = false;
                if (!e.OutcomeUnknown) { _postKey.Complete(); _pendingPost = null; }
                Message = e.OutcomeUnknown ? "feed.postUnknown" : e.CopyKey;
                Notify();
            });
            return null;
        }

        public void ForgetPendingPost() { _postKey.Complete(); _pendingPost = null; Message = null; Notify(); }

        public bool Following(string userId) => _hub.Follows.Contains(userId);
        public bool FollowBusy(string userId) => _followBusy.Contains(userId ?? "");

        public void ToggleFollow(string userId)
        {
            if (!SocialWire.IsSafeId(userId) || SocialWire.SameId(userId, _hub.Me) || !_followBusy.Add(userId)) return;
            var follow = !Following(userId);
            var key = new RetainedKey();
            Notify();
            _hub.Run(async ct =>
            {
                try { await _hub.Api.Follow(userId, follow, key, ct); }
                finally { _followBusy.Remove(userId); }
                _hub.Follows.Set(userId, follow);
                Message = follow ? "feed.followed" : "feed.unfollowed";
                if (Scope == "following") Refresh();
                Notify();
            }, e => { _followBusy.Remove(userId); Message = e.CopyKey; Notify(); });
        }

        // ---------------------------------------------------------------- tips (#28)

        public long? TipCapCents
        {
            get
            {
                var economy = Services.TryGet<ConfigStore>(out var c) ? c.Config("economy") : null;
                return SocialWire.OptLong(economy?[TipRules.CapKey]);
            }
        }

        public long? BalanceCents => Services.TryGet<PlayerProfile>(out var p) && p.Me != null ? p.CashCents : (long?)null;

        /// <summary>POST /tips. The new balance comes from the server (`cashCents`) or a GET /me; never computed here.</summary>
        public void TipCash(string toUserId, string amountInput, RetainedKey key, Action<string> done)
        {
            if (!SocialWire.IsSafeId(toUserId) || SocialWire.SameId(toUserId, _hub.Me)) { done?.Invoke("tip.badUser"); return; }
            if (Features.IsAvailable(Features.LedgerFreeze) == false) { done?.Invoke("tip.frozen"); return; }
            var err = TipRules.CashError(amountInput, BalanceCents, TipCapCents, out var cents);
            if (err != null) { done?.Invoke(err); return; }
            _hub.Run(async ct =>
            {
                var balance = await _hub.Api.TipCash(toUserId, cents, key, ct);
                key.Complete();
                if (Services.TryGet<PlayerProfile>(out var profile))
                {
                    if (balance.HasValue) profile.SetCash(balance.Value);
                    try { await profile.Refresh(ct); } catch (Exception e) when (!(e is OperationCanceledException)) { }
                }
                Bus.Publish(new SfxSignal("sfx_ui_success"));
                done?.Invoke(null);
            }, e =>
            {
                if (!e.OutcomeUnknown) key.Complete();
                done?.Invoke(e.OutcomeUnknown ? "tip.unknown" : e.CopyKey);
            });
        }

        /// <summary>POST /tips/onchain {toUserId, signature}: records a wallet-to-wallet tip the player already sent.</summary>
        public void RecordOnchainTip(string toUserId, string signatureInput, RetainedKey key, Action<string> done)
        {
            var sig = (signatureInput ?? "").Trim();
            if (!SocialWire.IsSafeId(toUserId) || SocialWire.SameId(toUserId, _hub.Me)) { done?.Invoke("tip.badUser"); return; }
            if (!TipRules.IsSignature(sig)) { done?.Invoke("tip.badSignature"); return; }
            _hub.Run(async ct =>
            {
                await _hub.Api.TipOnchain(toUserId, sig, key, ct);
                key.Complete();
                done?.Invoke(null);
            }, e =>
            {
                if (!e.OutcomeUnknown) key.Complete();
                done?.Invoke(e.OutcomeUnknown ? "tip.unknown" : e.CopyKey);
            });
        }

        public void OnOnline() { _loadedAt.Clear(); }

        public void Tick(double now)
        {
            if (_disposed || _viewers <= 0 || _hub.Offline) return;
            if (!_loadedAt.TryGetValue(Scope, out var at) || now - at >= PollMs) Refresh();
        }

        public void Dispose() { _disposed = true; }
    }
}
