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
        public const double PollMs = 30000, BackgroundPollMs = 60000;
        public const int EventKeep = 60;
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

        /// <summary>The city's event cards from realtime events (the fake until the server writes them as posts).</summary>
        private readonly List<FeedPostRow> _events = new List<FeedPostRow>();
        public IReadOnlyList<FeedPostRow> Events => _events;
        /// <summary>The City tab's chip (null = All).</summary>
        public string CityFilter { get; private set; }
        /// <summary>The newest `evt_mood_change` (the City tab's mood strip), or null.</summary>
        public FeedPostRow Mood { get; private set; }
        private readonly TimelineToastPolicy _toasts = new TimelineToastPolicy();
        // backend #32: server tabs (posts in the server's order, cards built by the server)
        private readonly Dictionary<string, List<FeedPostRow>> _pages = new Dictionary<string, List<FeedPostRow>>();
        private readonly Dictionary<string, string> _cursors = new Dictionary<string, string>();
        /// <summary>The server answered with tabs (backend #32). False on an older server: the client mixes instead.</summary>
        public bool ServerTabs { get; private set; }
        /// <summary>For you is the city's last moments because nobody posted (show the "Quiet" line).</summary>
        public bool Quiet { get; private set; }
        public bool LoadingMore { get; private set; }
        private double _refreshSoonAt = double.PositiveInfinity;
        private string PageKey(string tab) => tab == "city" ? "city:" + (CityFilter ?? "all") : tab;
        /// <summary>Opt-in: posts carry a place card for the venue you're at (remembered per player; off by default).</summary>
        public bool WithPlace
        {
            get => _hub.Store.Get("timeline.withPlace") == "1";
            set { _hub.Store.Set("timeline.withPlace", value ? "1" : ""); Notify(); }
        }
        public bool HasMore => _cursors.TryGetValue(PageKey(Tab), out var c) && !string.IsNullOrEmpty(c);
        private string Tab => Scope == "following" ? "following" : Scope == "city" ? "city" : "foryou";
        private double _bgAt = double.NegativeInfinity;
        private bool _firstLoad = true;

        public TimelineController(SocialHub hub)
        {
            _hub = hub;
            _subs.On<CTLife.Realtime.EventMessage>(e => { try { OnEvent(e); } catch (Exception x) { Log.Warn("timeline", "event card failed: " + x.Message); } });
        }

        private readonly Subscriptions _subs = new Subscriptions();

        /// <summary>A realtime city event becomes an event card (and maybe a district toast).</summary>
        public void OnEvent(CTLife.Realtime.EventMessage e)
        {
            if (_disposed) return;
            var card = CityEventCards.FromEvent(e, ServerClock.NowMs);
            if (card == null) return;
            if (card.Kind == EventKinds.Mood) { Mood = card; Notify(); return; }
            if (_events.Exists(x => x.PostId == card.PostId)) return;
            _events.Insert(0, card);
            if (_events.Count > EventKeep) _events.RemoveRange(EventKeep, _events.Count - EventKeep);
            ConsiderToast(card, fromFeed: false);
            if (ServerTabs && _viewers > 0) _refreshSoonAt = SocialHub.NowMs + 2500;   // the server writes the card; fetch it
            Notify();
        }

        public void SetCityFilter(string kind) { CityFilter = kind; if (ServerTabs) Refresh(); Notify(); }

        /// <summary>The rows the current tab shows (For you mixes events in; City is events only).</summary>
        public IReadOnlyList<FeedPostRow> Rows
        {
            get
            {
                if (ServerTabs)
                    return _pages.TryGetValue(PageKey(Tab), out var page) ? page.Where(p => !_hub.Blocks.Contains(p.UserId)).ToList() : new List<FeedPostRow>();
                if (Scope == "city") return TimelineMix.City(GlobalVisible, _events, CityFilter);
                if (Scope == "following") return Visible;
                return TimelineMix.ForYou(Visible, _events);
            }
        }

        private IReadOnlyList<FeedPostRow> GlobalVisible =>
            _feeds.TryGetValue("global", out var g) ? g.Visible(_hub.Blocks.Contains) : (IReadOnlyList<FeedPostRow>)Array.Empty<FeedPostRow>();

        /// <summary>HUD toasts (approved: posts about you, or a big moment in your district, max one per 2 min).</summary>
        private void ConsiderToast(FeedPostRow p, bool fromFeed = true)
        {
            // city moments come from the realtime events (instant); a feed row only toasts when it is about me
            if (fromFeed && p != null && EventKinds.IsCity(p.Kind)) return;
            var me = _hub.Me;
            string alias = Services.TryGet<PlayerProfile>(out var prof) ? prof.Alias : null;
            if (p == null || _hub.Blocks.Contains(p.UserId)) return;   // never surface a blocked author
            var reason = _toasts.Decide(p, me, alias, CurrentDistrict(), DistrictOfVenue, ServerClock.NowMs, _hub.Blocks.Contains);
            if (reason == TimelineToastPolicy.Reason.None) return;
            var text = TimelineText.Toast(p, reason);
            if (text == null) return;
            Log.Info("timeline", "toast " + reason + ": " + p.Kind + " " + p.PostId);
            Bus.Publish(ToastSignal.Text(text, reason == TimelineToastPolicy.Reason.AboutMe ? ToastKind.Event : ToastKind.Info));
        }

        private static string CurrentDistrict()
        {
            var s = CTLife.World.VenueSession.Current;
            if (s == null || s.LocalPlayer == null) return null;
            if (s.IsIsland && s.Island != null) return s.Island.DistrictAt(s.LocalPlayer.transform.position);
            return DistrictOfVenue(s.VenueId);
        }

        private static string DistrictOfVenue(string venueId) =>
            venueId != null && Services.TryGet<ConfigStore>(out var c) && c.Registries != null && c.Registries.Venues.TryGetValue(venueId, out var v) ? v.districtId : null;

        private void Notify() { if (!_disposed) _hub.NotifyChanged(); }

        public FeedTimeline Feed => _feeds.TryGetValue(Scope, out var f) ? f : (_feeds[Scope] = new FeedTimeline(Scope));

        public IReadOnlyList<FeedPostRow> Visible => Feed.Visible(_hub.Blocks.Contains);

        public void Watch(bool on) { _viewers = Math.Max(0, _viewers + (on ? 1 : -1)); if (on) Refresh(); }

        public void SetScope(string scope)
        {
            Scope = scope == "following" ? "following" : scope == "city" ? "city" : "global";
            Error = null;
            Refresh();
            Notify();
        }

        public void Refresh()
        {
            if (ServerTabs || !_loadedAt.ContainsKey("tabs-probed")) { LoadTab(Tab, null); return; }
            Load(Scope == "city" ? "global" : Scope);
        }

        /// <summary>Backend #32 tabs; the first answer tells whether the server has them (else the legacy scopes are used).</summary>
        private void LoadTab(string tab, string before)
        {
            if (_disposed || !Features.IsAvailable(Features.Feed)) return;
            if (before == null ? Loading : LoadingMore) return;
            if (before == null) Loading = true; else LoadingMore = true;
            Notify();
            var key = PageKey(tab);
            var filter = tab == "city" ? CityFilter : null;
            _hub.Run(async ct =>
            {
                try
                {
                    var page = await _hub.Api.Timeline(tab, filter, before, ct);
                    _loadedAt["tabs-probed"] = SocialHub.NowMs;
                    if (page.Quiet == null && before == null)
                    {
                        // an older server: no tabs, no cards
                        ServerTabs = false;
                        var legacy = tab == "following" ? "following" : "global";
                        _feeds.TryGetValue(legacy, out var f);
                        if (f == null) _feeds[legacy] = f = new FeedTimeline(legacy);
                        f.Replace(page.Posts);
                        return;
                    }
                    ServerTabs = true;
                    foreach (var p in page.Posts) _hub.Names.Remember(p.UserId, p.Alias);
                    if (before == null) { _pages[key] = page.Posts; if (tab == "foryou") Quiet = page.Quiet == true; }
                    else if (_pages.TryGetValue(key, out var list)) list.AddRange(page.Posts.Where(p => !list.Exists(x => x.PostId == p.PostId)));
                    _cursors[key] = page.NextBefore;
                    // keep the legacy global feed in step (verdict lookups, the Sleuth toast) from For you
                    if (tab == "foryou" && before == null)
                    {
                        _feeds.TryGetValue("global", out var g);
                        if (g == null) _feeds["global"] = g = new FeedTimeline("global");
                        g.Replace(page.Posts);
                        foreach (var p in page.Posts)
                        {
                            if (_hub.Blocks.Contains(p.UserId)) continue;   // blocked authors never toast (security review)
                            if (_firstLoad) _toasts.Decide(p, null, null, null, null, double.MaxValue); else ConsiderToast(p);
                        }
                        _firstLoad = false;
                    }
                    Error = null;
                }
                finally { Loading = false; LoadingMore = false; _loadedAt[key] = SocialHub.NowMs; Notify(); }
            }, e => { Loading = false; LoadingMore = false; _loadedAt[key] = SocialHub.NowMs; Error = e.Kind == ErrorKind.RouteMissing ? "err.routeMissing" : e.CopyKey; Notify(); });
        }

        public void LoadMore()
        {
            if (!ServerTabs || !_cursors.TryGetValue(PageKey(Tab), out var c) || string.IsNullOrEmpty(c)) return;
            LoadTab(Tab, c);
        }

        private readonly RetainedKey _checkinKey = new RetainedKey();

        /// <summary>Opt-in check-in at a venue (backend #32). Done gets null on success or a copy key.</summary>
        public void CheckIn(string venueId, Action<string> done)
        {
            if (string.IsNullOrEmpty(venueId)) { done?.Invoke("timeline.checkinNowhere"); return; }
            _hub.Run(async ct =>
            {
                var post = await _hub.Api.CheckIn(venueId, _checkinKey, ct);
                _checkinKey.Complete();
                if (_pages.TryGetValue("foryou", out var list) && post != null) list.Insert(0, post);
                Notify();
                done?.Invoke(null);
            }, e =>
            {
                if (!e.OutcomeUnknown) _checkinKey.Complete();
                done?.Invoke(e.Code == "VALIDATION_FAILED" ? "timeline.checkinFar" : e.Code == "RATE_LIMITED" ? "timeline.checkinSoon" : e.CopyKey);
            });
        }

        private void Load(string scope)
        {
            if (Loading || _disposed || !Features.IsAvailable(Features.Feed)) return;
            Loading = true; Notify();
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
                    if (scope == "global") foreach (var p in page)
                        {
                            if (_hub.Blocks.Contains(p.UserId)) continue;   // blocked authors never toast (security review)
                            if (_firstLoad) _toasts.Decide(p, null, null, null, null, double.MaxValue); else ConsiderToast(p);
                        }
                    if (scope == "global") _firstLoad = false;
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
                try { post = await _hub.Api.Post(text, _postKey, ct, WithPlace); }
                finally { Posting = false; }
                _postKey.Complete(); _pendingPost = null;
                if (string.IsNullOrWhiteSpace(post.Alias)) post.Alias = _hub.Names.Name(_hub.Me);
                Feed.Add(post);
                if (Scope != "global" && _feeds.TryGetValue("global", out var g)) g.Add(post);
                if (_pages.TryGetValue("foryou", out var fy) && !fy.Exists(x => x.PostId == post.PostId)) fy.Insert(0, post);
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

        public void OnOnline() { _loadedAt.Clear(); }

        public void Tick(double now)
        {
            if (_disposed || _hub.Offline) return;
            var scope = Scope == "city" ? "global" : Scope;
            if (_viewers > 0)
            {
                var key = ServerTabs ? PageKey(Tab) : scope;
                if (now >= _refreshSoonAt) { _refreshSoonAt = double.PositiveInfinity; Refresh(); return; }
                if (!_loadedAt.TryGetValue(key, out var at) || now - at >= PollMs) Refresh();
                return;
            }
            // closed app: a slow global poll so "the Sleuth posted about you" can reach the HUD
            if (now - _bgAt < BackgroundPollMs) return;
            _bgAt = now;
            if (ServerTabs) LoadTab("foryou", null); else Load("global");
        }

        public void Dispose() { _disposed = true; _subs.Dispose(); }
    }
}
