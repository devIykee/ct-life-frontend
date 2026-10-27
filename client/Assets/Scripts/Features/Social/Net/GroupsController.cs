using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using Newtonsoft.Json.Linq;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// Group chats and alpha groups inside Messages (backend #33, docs/GROUPS.md; board groupchats-22). The service layer:
    /// the list and Invites, topic pages, sending (chat / calls / wins), moderation, invites and links, booths, and the
    /// realtime `group_msg` / `group_invite` on the city room. UI comes later in the design system and reads <see cref="State"/>.
    /// Writes keep their Idempotency-Key while the outcome is unknown (a retry replays, never posts twice). Everything is
    /// plain text; nothing from a blocked player is stored, counted or bannered.
    /// </summary>
    public sealed class GroupsController : IDisposable
    {
        /// <summary>Opens a group chat from a banner (set by the Messages UI).</summary>
        public static Action<string, string> OpenGroup;
        /// <summary>Opens an invite preview from a banner or a ctlife://invite link (set by the Messages UI).</summary>
        public static Action<string> OpenInvite;
        public const string AppId = "messages";
        public const double ListEveryMs = 60_000;

        private readonly SocialHub _hub;
        public readonly GroupState State = new GroupState();
        private readonly Dictionary<string, RetainedKey> _keys = new Dictionary<string, RetainedKey>(StringComparer.Ordinal);
        private readonly HashSet<string> _busy = new HashSet<string>(StringComparer.Ordinal);
        private double _listAt = double.NegativeInfinity, _refreshSoonAt = double.PositiveInfinity;
        private int _viewers;
        private bool _disposed;

        public string Error { get; private set; }
        /// <summary>My alias for mention banners (set by the profile; empty = no mention banners).</summary>
        public string MyAlias { get; set; } = "";

        public GroupsController(SocialHub hub)
        {
            _hub = hub;
            RawRoomMessages.Received += OnRaw;
        }

        private bool Blocked(string userId) => !string.IsNullOrEmpty(userId) && _hub.Blocks.Contains(userId);
        private void Notify() { if (!_disposed) _hub.NotifyChanged(); }
        public bool Busy(string op) => _busy.Contains(op);
        private RetainedKey KeyFor(string op) { if (!_keys.TryGetValue(op, out var k)) _keys[op] = k = new RetainedKey(); return k; }

        /// <summary>Runs one write: one key per op while its outcome is unknown; a definitive answer completes the key.</summary>
        private void Write(string op, Func<RetainedKey, System.Threading.CancellationToken, System.Threading.Tasks.Task> work, Action ok, Action<string> done)
        {
            if (_disposed || !_busy.Add(op)) return;
            var key = KeyFor(op);
            Error = null;
            Notify();
            _hub.Run(async ct =>
            {
                await work(key, ct);
                key.Complete();
                _busy.Remove(op);
                ok?.Invoke();
                Notify();
                done?.Invoke(null);
            }, e =>
            {
                if (!e.OutcomeUnknown) key.Complete();
                _busy.Remove(op);
                Error = GroupText.Error(e);
                Log.Warn("groups", $"{op.Split(':')[0]} failed: {e.Code}/{e.Status} {GroupText.ErrorKey(e)}");
                Notify();
                done?.Invoke(Error);
            });
        }

        // ---------------------------------------------------------------- list, invites, open

        /// <summary>The Messages tab is showing groups (refreshes now and every minute while watched).</summary>
        public void Watch(bool on) { _viewers = Math.Max(0, _viewers + (on ? 1 : -1)); if (on) Refresh(); }

        public void Refresh()
        {
            if (_disposed || _busy.Contains("list")) return;
            _busy.Add("list");
            _listAt = SocialHub.NowMs;
            _refreshSoonAt = double.PositiveInfinity;
            _hub.Run(async ct =>
            {
                var groups = await _hub.Api.Groups(ct);
                var invites = await _hub.Api.GroupInvites(ct);
                _busy.Remove("list");
                State.SetList(groups);
                State.SetInvites(invites, Blocked);
                foreach (var i in invites) if (i.From != null) _hub.Names.Remember(i.From.UserId, i.From.Alias);
                Error = null;
                Notify();
            }, e => { _busy.Remove("list"); Error = GroupText.Error(e); Notify(); });
        }

        /// <summary>Opens a group's topic: info (rules, slow mode, here-with-you), the newest page, and marks it read.</summary>
        public void Open(string groupId, string topic = GroupTopics.Chat)
        {
            if (_disposed || !SocialWire.IsSafeId(groupId)) return;
            State.Open(groupId, topic);
            Notify();
            _hub.Run(async ct =>
            {
                var info = await _hub.Api.Group(groupId, ct);
                State.SetInfo(info);
                if (!State.Thread(groupId, topic).Loaded)
                    State.ApplyPage(groupId, topic, await _hub.Api.GroupMessages(groupId, topic, null, ct), Blocked);
                Notify();
                await _hub.Api.ReadGroup(groupId, new RetainedKey(), ct);
            }, e =>
            {
                Error = GroupText.Error(e);
                // 404 (not a member any more) or hidden: it leaves the list
                if (e.Status == 404 && e.Kind != ErrorKind.RouteMissing || GroupText.ErrorKey(e) == "err.group_hidden") State.Remove(groupId);
                Notify();
            });
        }

        public void Close() { State.Close(); Notify(); }

        public void LoadOlder(string groupId, string topic)
        {
            var t = State.Thread(groupId, topic);
            string op = "older:" + groupId + "/" + topic;
            if (_disposed || string.IsNullOrEmpty(t.NextBefore) || !_busy.Add(op)) return;
            _hub.Run(async ct =>
            {
                State.ApplyPage(groupId, topic, await _hub.Api.GroupMessages(groupId, topic, t.NextBefore, ct), Blocked);
                _busy.Remove(op);
                Notify();
            }, e => { _busy.Remove(op); Error = GroupText.Error(e); Notify(); });
        }

        // ---------------------------------------------------------------- posting

        /// <summary>Chat in any topic that allows it. Returns the pre-check error (copy) or null when sent.</summary>
        public string Send(string groupId, string topic, string input, Action<string> done = null)
        {
            var text = SocialWire.Plain((input ?? "").Trim(), GroupRules.TextMax);
            if (text.Length == 0) return null;
            var g = State.Summary(groupId);
            if (g != null && !GroupRules.CanPost(g.Kind, g.Role, topic)) return GroupText.T("err.calls_owner_only");
            Write("send:" + groupId + "/" + topic + ":" + text, async (key, ct) =>
            {
                var m = await _hub.Api.SendGroupChat(groupId, topic, text, key, ct);
                State.ApplyIncoming(m, _hub.Me, null);
            }, null, done);
            return null;
        }

        /// <summary>An alpha call (owner only); the numbers are pre-checked (target 1.05x..100x, stop 1..95%).</summary>
        public string Call(string groupId, string coinId, double targetX, int stopPct, string text, Action<string> done = null)
        {
            var g = State.Summary(groupId);
            if (g != null && !GroupRules.CanPost(g.Kind, g.Role, GroupTopics.Calls)) return GroupText.T("err.calls_owner_only");
            var bad = GroupRules.CheckCall(targetX, stopPct);
            if (bad != null) return GroupText.T(bad);
            if (!SocialWire.IsSafeId(coinId)) return GroupText.T("err.not_member");
            var note = string.IsNullOrWhiteSpace(text) ? null : SocialWire.Plain(text.Trim(), GroupRules.TextMax);
            Write("call:" + groupId + "/" + coinId, async (key, ct) =>
            {
                var m = await _hub.Api.SendGroupCall(groupId, coinId, targetX, stopPct, note, key, ct);
                State.ApplyIncoming(m, _hub.Me, null);
            }, null, done);
            return null;
        }

        public void Win(string groupId, string coinId, string text, Action<string> done = null)
        {
            if (!SocialWire.IsSafeId(coinId)) return;
            var note = string.IsNullOrWhiteSpace(text) ? null : SocialWire.Plain(text.Trim(), GroupRules.TextMax);
            Write("win:" + groupId + "/" + coinId, async (key, ct) =>
            {
                var m = await _hub.Api.SendGroupWin(groupId, coinId, note, key, ct);
                State.ApplyIncoming(m, _hub.Me, null);
            }, null, done);
        }

        public void Delete(string groupId, string topic, string messageId, Action<string> done = null) =>
            Write("del:" + messageId, (key, ct) => _hub.Api.DeleteGroupMessage(groupId, messageId, key, ct),
                () => State.Thread(groupId, topic).Remove(messageId), done);

        // ---------------------------------------------------------------- groups

        public void Create(string kind, string name, IReadOnlyList<string> inviteUserIds, Action<string> done = null)
        {
            if (!GroupKinds.IsKnown(kind) || kind == GroupKinds.Tribe) return;
            var clean = GroupParse.Name(name, GroupRules.NameMax);
            if (clean.Length == 0) { done?.Invoke(GroupText.T("err.name")); return; }
            var invites = (inviteUserIds ?? Array.Empty<string>()).Where(id => SocialWire.IsSafeId(id) && !Blocked(id)).Distinct().ToList();
            GroupSummary made = null;
            Write("create:" + kind + ":" + clean, async (key, ct) => made = await _hub.Api.CreateGroup(kind, clean, invites, key, ct),
                () => State.Upsert(made), done);
        }

        public void Leave(string groupId, Action<string> done = null)
        {
            var g = State.Summary(groupId);
            if (g != null && !GroupRules.CanLeave(g.Kind)) return;   // tribe chats are muted, not left
            Write("leave:" + groupId, (key, ct) => _hub.Api.LeaveGroup(groupId, key, ct), () => State.Remove(groupId), done);
        }

        public void Mute(string groupId, int hours, Action<string> done = null)
        {
            long? until = null;
            Write("mute:" + groupId + ":" + hours, async (key, ct) => until = await _hub.Api.MuteGroup(groupId, Math.Max(0, hours), key, ct), () =>
            {
                if (State.Summary(groupId) is GroupSummary g) { g.Muted = hours > 0; g.MutedUntilMs = until; }
            }, done);
        }

        public void Report(string groupId, string reasonId, string note, Action<string> done = null) =>
            Write("report:" + groupId, (key, ct) => _hub.Api.ReportGroup(groupId, reasonId, note, key, ct),
                () => Bus.Publish(ToastSignal.Text(SocialStrings.T("report.sent"), ToastKind.Good)), done);

        public void Rename(string groupId, string name, Action<string> done = null)
        {
            var clean = GroupParse.Name(name, GroupRules.NameMax);
            if (clean.Length == 0) return;
            GroupInfo info = null;
            Write("rename:" + groupId, async (key, ct) => info = await _hub.Api.PatchGroup(groupId, clean, null, null, key, ct), () =>
            {
                State.SetInfo(info);
                if (State.Summary(groupId) is GroupSummary g) g.Name = info.Name;
            }, done);
        }

        public void SetRules(string groupId, string rules, Action<string> done = null)
        {
            GroupInfo info = null;
            Write("rules:" + groupId, async (key, ct) => info = await _hub.Api.PatchGroup(groupId, null, SocialWire.Plain(rules ?? "", 1000), null, key, ct),
                () => State.SetInfo(info), done);
        }

        /// <summary>Slow mode for one topic (0..300 s); the whole map is sent (backend main needs every topic).</summary>
        public void SetSlowMode(string groupId, string topic, int seconds, Action<string> done = null)
        {
            var current = State.Info(groupId);
            if (current == null) return;
            GroupInfo info = null;
            Write("slow:" + groupId + "/" + topic + ":" + seconds, async (key, ct) =>
                info = await _hub.Api.PatchGroup(groupId, null, null, GroupRules.SlowModeBody(current, topic, seconds), key, ct), () => State.SetInfo(info), done);
        }

        public void LoadMembers(string groupId, Action<List<GroupMember>> done)
        {
            _hub.Run(async ct =>
            {
                var members = await _hub.Api.GroupMembers(groupId, ct);
                foreach (var m in members) _hub.Names.Remember(m.UserId, m.Alias);
                done?.Invoke(members.Where(m => !Blocked(m.UserId)).ToList());
            }, e => { Error = GroupText.Error(e); Notify(); done?.Invoke(null); });
        }

        public void RemoveMember(string groupId, string userId, Action<string> done = null) =>
            Write("remove:" + groupId + "/" + userId, (key, ct) => _hub.Api.RemoveMember(groupId, userId, key, ct), null, done);

        public void SetRole(string groupId, string userId, string role, Action<string> done = null)
        {
            if (role != GroupRoles.Mod && role != GroupRoles.Member) return;
            Write("role:" + groupId + "/" + userId + ":" + role, (key, ct) => _hub.Api.SetRole(groupId, userId, role, key, ct), null, done);
        }

        // ---------------------------------------------------------------- invites

        /// <summary>An invite link (ctlife://invite/&lt;code&gt;). Sharing it never rewards anyone.</summary>
        public void CreateLink(string groupId, int? maxUses, int? ttlHours, Action<GroupInviteLink, string> done)
        {
            GroupInviteLink link = null;
            Write("link:" + groupId, async (key, ct) => link = await _hub.Api.CreateInviteLink(groupId, maxUses, ttlHours, key, ct), null,
                err => done?.Invoke(err == null ? link : null, err));
        }

        public void InviteUser(string groupId, string userId, Action<string> done = null)
        {
            if (Blocked(userId)) { done?.Invoke(SocialStrings.T("err.blocked")); return; }
            Write("invite:" + groupId + "/" + userId, (key, ct) => _hub.Api.InviteUser(groupId, userId, key, ct), null, done);
        }

        public void Preview(string code, Action<GroupInvitePreview, string> done)
        {
            if (!GroupLinks.IsCode(code)) { done?.Invoke(null, GroupText.T("err.invite_expired")); return; }
            _hub.Run(async ct =>
            {
                var p = await _hub.Api.InvitePreview(code, ct);
                if (p.From != null && Blocked(p.From.UserId)) { done?.Invoke(null, SocialStrings.T("err.blocked")); return; }
                done?.Invoke(p, null);
            }, e => done?.Invoke(null, GroupText.Error(e)));
        }

        public void Accept(string code, Action<string> done = null)
        {
            GroupSummary joined = null;
            Write("accept:" + code, async (key, ct) => joined = await _hub.Api.AcceptInvite(code, key, ct), () =>
            {
                State.RemoveInvite(code);
                State.Upsert(joined);
            }, done);
        }

        public void Decline(string code, Action<string> done = null) =>
            Write("decline:" + code, (key, ct) => _hub.Api.DeclineInvite(code, key, ct), () => State.RemoveInvite(code), done);

        // ---------------------------------------------------------------- booths

        public void LoadBooths(string venueId, Action<List<BoothRow>> done)
        {
            if (!SocialWire.IsSafeId(venueId)) return;
            _hub.Run(async ct => done?.Invoke(await _hub.Api.Booths(venueId, ct)), e => { Error = GroupText.Error(e); Notify(); done?.Invoke(null); });
        }

        public void ClaimBooth(string groupId, string venueId, string boothId, Action<string> done = null) =>
            Write("booth:" + groupId, (key, ct) => _hub.Api.ClaimBooth(groupId, venueId, boothId, key, ct), () =>
            {
                if (State.Summary(groupId) is GroupSummary g) g.Booth = new GroupBooth { VenueId = venueId, BoothId = boothId };
            }, done);

        public void ReleaseBooth(string groupId, Action<string> done = null) =>
            Write("unbooth:" + groupId, (key, ct) => _hub.Api.ReleaseBooth(groupId, key, ct), () =>
            {
                if (State.Summary(groupId) is GroupSummary g) g.Booth = null;
            }, done);

        // ---------------------------------------------------------------- realtime, blocks, lifecycle

        private void OnRaw(string type, object payload)
        {
            if (_disposed || (type != "group_msg" && type != "group_invite")) return;
            JToken t;
            try { t = payload != null ? JObject.FromObject(payload) : null; }
            catch (Exception) { return; }
            if (type == "group_msg") OnMessage(GroupParse.Message(t));
            else OnInvite(GroupParse.InviteEvent(t));
        }

        /// <summary>Realtime group_msg (public for tests and the hub). Blocked authors never get past here.</summary>
        public void OnMessage(GroupMessage m)
        {
            if (_disposed || m == null) return;
            if (Blocked(m.AuthorUserId)) { Log.Info("groups", "group_msg from a blocked author dropped"); return; }
            var g = State.Summary(m.GroupId);
            if (g == null) _refreshSoonAt = Math.Min(_refreshSoonAt, SocialHub.NowMs + 1500);   // a group I just joined
            if (State.ApplyIncoming(m, _hub.Me, Blocked) != GroupState.Applied.Added) return;
            _hub.Names.Remember(m.AuthorUserId, m.AuthorAlias);
            bool open = State.OpenGroup == m.GroupId && State.OpenTopic == m.Topic;
            var banner = GroupBannerPolicy.For(m, _hub.Me, MyAlias, false, m.Muted || (g?.Muted ?? false), open);
            if (banner.Show)
            {
                var gid = m.GroupId; var topic = m.Topic;
                DuoNotificationCenter.Post(new DuoNotification
                {
                    AppId = AppId, ThreadKey = "group:" + gid, Title = banner.Title, Body = banner.Body,
                    Open = () => OpenGroup?.Invoke(gid, topic),
                });
            }
            Notify();
        }

        /// <summary>Realtime group_invite. An inviter I blocked is dropped (no list row, no banner).</summary>
        public void OnInvite(GroupInviteEvent e)
        {
            if (_disposed || e == null) return;
            if (!State.ApplyInvite(e, Blocked)) return;
            var banner = GroupBannerPolicy.Invite(e, _hub.Names.Name(e.FromUserId), Blocked(e.FromUserId));
            if (banner.Show)
            {
                var code = e.Code;
                DuoNotificationCenter.Post(new DuoNotification { AppId = AppId, ThreadKey = "invite:" + code, Title = banner.Title, Body = banner.Body, Open = () => OpenInvite?.Invoke(code) });
            }
            _refreshSoonAt = Math.Min(_refreshSoonAt, SocialHub.NowMs + 1500);   // the full preview (members, owner, trust)
            Notify();
        }

        public void OnBlocked(string userId)
        {
            if (State.OnBlocked(userId) > 0) Notify();
        }

        public void OnOnline() { _listAt = double.NegativeInfinity; }

        public void Tick(double now)
        {
            if (_disposed) return;
            if (now >= _refreshSoonAt || (_viewers > 0 && now - _listAt >= ListEveryMs)) Refresh();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RawRoomMessages.Received -= OnRaw;
        }
    }
}
