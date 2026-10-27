using System;
using System.Collections.Generic;
using System.Linq;

namespace CTLife.SocialFeatures
{
    /// <summary>One group topic's loaded messages, oldest first, de-duplicated by id.</summary>
    public sealed class GroupThread
    {
        private readonly List<GroupMessage> _items = new List<GroupMessage>();
        private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.Ordinal);
        public IReadOnlyList<GroupMessage> Items => _items;
        public string NextBefore;
        public bool Loaded;

        public bool Has(string id) => id != null && _ids.Contains(id);

        public bool Add(GroupMessage m)
        {
            if (m == null || !_ids.Add(m.MessageId)) return false;
            int i = _items.Count;
            while (i > 0 && _items[i - 1].AtMs > m.AtMs) i--;
            _items.Insert(i, m);
            return true;
        }

        public bool Remove(string messageId)
        {
            if (!_ids.Remove(messageId ?? "")) return false;
            _items.RemoveAll(x => x.MessageId == messageId);
            return true;
        }

        public int RemoveAuthor(string userId)
        {
            var gone = _items.Where(x => SocialWire.SameId(x.AuthorUserId, userId)).ToList();
            foreach (var g in gone) { _items.Remove(g); _ids.Remove(g.MessageId); }
            return gone.Count;
        }
    }

    /// <summary>
    /// The Groups state behind Messages (backend #33), pure so it is tested without a server: the list (newest activity
    /// first), the Invites list, loaded topic pages, unread counts and realtime. Safety rules (security review 2026-10-26):
    /// nothing from a blocked author or inviter is ever stored, counted or shown; anything already stored is purged on block.
    /// </summary>
    public sealed class GroupState
    {
        public enum Applied { Dropped, Duplicate, Added }

        private readonly List<GroupSummary> _groups = new List<GroupSummary>();
        private readonly List<GroupInvitePreview> _invites = new List<GroupInvitePreview>();
        private readonly Dictionary<string, GroupThread> _threads = new Dictionary<string, GroupThread>(StringComparer.Ordinal);
        private readonly Dictionary<string, GroupInfo> _infos = new Dictionary<string, GroupInfo>(StringComparer.Ordinal);
        /// <summary>Who invited me, by invite code (realtime group_invite carries it; the list's `from` too).</summary>
        private readonly Dictionary<string, string> _inviter = new Dictionary<string, string>(StringComparer.Ordinal);

        public IReadOnlyList<GroupSummary> Groups => _groups;
        public string OpenGroup { get; private set; }
        public string OpenTopic { get; private set; }

        private static string Key(string groupId, string topic) => groupId + "/" + (topic ?? GroupTopics.Chat);

        public GroupSummary Summary(string groupId) => _groups.FirstOrDefault(g => g.GroupId == groupId);
        public GroupInfo Info(string groupId) => groupId != null && _infos.TryGetValue(groupId, out var i) ? i : null;
        public void SetInfo(GroupInfo info) { if (info?.GroupId != null) _infos[info.GroupId] = info; }

        public GroupThread Thread(string groupId, string topic)
        {
            var k = Key(groupId, topic);
            if (!_threads.TryGetValue(k, out var t)) _threads[k] = t = new GroupThread();
            return t;
        }

        /// <summary>The topic's messages minus any blocked author (also filtered here, in case a block lands mid-page).</summary>
        public IEnumerable<GroupMessage> Visible(string groupId, string topic, Func<string, bool> blocked) =>
            Thread(groupId, topic).Items.Where(m => blocked == null || !blocked(m.AuthorUserId));

        public IEnumerable<GroupInvitePreview> Invites(Func<string, bool> blocked) =>
            _invites.Where(i => blocked == null || (!blocked(i.From?.UserId ?? "") && !(_inviter.TryGetValue(i.Code, out var f) && blocked(f))));

        /// <summary>Unread across unmuted groups (muted: a dot, no count).</summary>
        public int UnreadTotal => _groups.Where(g => !g.Muted).Sum(g => g.Unread);
        public bool MutedUnread => _groups.Any(g => g.Muted && g.Unread > 0);

        public void SetList(IEnumerable<GroupSummary> groups)
        {
            _groups.Clear();
            _groups.AddRange((groups ?? Enumerable.Empty<GroupSummary>()).Where(g => g != null));
            // the open chat is read
            if (OpenGroup != null && Summary(OpenGroup) is GroupSummary open) open.Unread = 0;
            var alive = new HashSet<string>(_groups.Select(g => g.GroupId));
            foreach (var k in _threads.Keys.Where(k => !alive.Contains(k.Substring(0, k.IndexOf('/')))).ToList()) _threads.Remove(k);
        }

        public void Upsert(GroupSummary g)
        {
            if (g == null) return;
            int i = _groups.FindIndex(x => x.GroupId == g.GroupId);
            if (i >= 0) _groups[i] = g; else _groups.Insert(0, g);
        }

        public void Remove(string groupId)
        {
            _groups.RemoveAll(g => g.GroupId == groupId);
            _infos.Remove(groupId ?? "");
            foreach (var k in _threads.Keys.Where(k => k.StartsWith(groupId + "/", StringComparison.Ordinal)).ToList()) _threads.Remove(k);
            if (OpenGroup == groupId) { OpenGroup = null; OpenTopic = null; }
        }

        public void SetInvites(IEnumerable<GroupInvitePreview> invites, Func<string, bool> blocked)
        {
            _invites.Clear();
            foreach (var i in invites ?? Enumerable.Empty<GroupInvitePreview>())
                if (i != null && (blocked == null || !blocked(i.From?.UserId ?? ""))) _invites.Add(i);
        }

        public void RemoveInvite(string code) { _invites.RemoveAll(i => i.Code == code); _inviter.Remove(code ?? ""); }

        public void Open(string groupId, string topic)
        {
            OpenGroup = groupId; OpenTopic = topic ?? GroupTopics.Chat;
            if (Summary(groupId) is GroupSummary g) g.Unread = 0;
        }

        public void Close() { OpenGroup = null; OpenTopic = null; }

        /// <summary>A page from GET messages (newest first on the wire). Blocked authors are skipped.</summary>
        public int ApplyPage(string groupId, string topic, GroupMessagePage page, Func<string, bool> blocked)
        {
            var t = Thread(groupId, topic);
            t.Loaded = true;
            t.NextBefore = page?.NextBefore;
            int added = 0;
            foreach (var m in page?.Messages ?? new List<GroupMessage>())
                if (m.GroupId == groupId && m.Topic == (topic ?? GroupTopics.Chat) && (blocked == null || !blocked(m.AuthorUserId)) && t.Add(m)) added++;
            return added;
        }

        /// <summary>
        /// Realtime group_msg or my own sent message. A blocked author is dropped before anything is stored or counted. Not
        /// in the open chat (and not mine): the group's unread count rises (capped 99, like the server).
        /// </summary>
        public Applied ApplyIncoming(GroupMessage m, string me, Func<string, bool> blocked)
        {
            if (m == null || (blocked != null && blocked(m.AuthorUserId))) return Applied.Dropped;
            var g = Summary(m.GroupId);
            var t = Thread(m.GroupId, m.Topic);
            if (t.Has(m.MessageId)) return Applied.Duplicate;
            if (t.Loaded) t.Add(m);
            bool mine = SocialWire.SameId(m.AuthorUserId, me);
            if (g != null)
            {
                g.Last = new GroupLastLine { Topic = m.Topic, AuthorAlias = m.AuthorAlias, Preview = SocialWire.Plain(m.Text ?? "", 120), AtMs = m.AtMs };
                bool open = OpenGroup == m.GroupId && OpenTopic == m.Topic;
                if (!mine && !open) g.Unread = Math.Min(99, g.Unread + 1);
                // newest activity first
                _groups.Remove(g); _groups.Insert(0, g);
            }
            return Applied.Added;
        }

        /// <summary>Realtime group_invite. An inviter I blocked never reaches the list.</summary>
        public bool ApplyInvite(GroupInviteEvent e, Func<string, bool> blocked)
        {
            if (e == null || (blocked != null && blocked(e.FromUserId))) return false;
            _inviter[e.Code] = e.FromUserId;
            if (_invites.Any(i => i.Code == e.Code)) return false;
            _invites.Insert(0, new GroupInvitePreview { Code = e.Code, GroupId = e.GroupId, GroupName = e.GroupName, From = new GroupUserRef { UserId = e.FromUserId }, Valid = true });
            return true;
        }

        /// <summary>I blocked someone: their messages and invites go, everywhere.</summary>
        public int OnBlocked(string userId)
        {
            int n = 0;
            foreach (var t in _threads.Values) n += t.RemoveAuthor(userId);
            int before = _invites.Count;
            _invites.RemoveAll(i => SocialWire.SameId(i.From?.UserId, userId) || (_inviter.TryGetValue(i.Code, out var f) && SocialWire.SameId(f, userId)));
            return n + before - _invites.Count;
        }
    }
}
