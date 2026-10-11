using System;
using System.Collections.Generic;
using System.Linq;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// One DM conversation (feature #26): ascending by time, deduplicated by `messageId`, paged backwards with `before`,
    /// with realtime `dm` pushes and locally echoed outgoing messages merged in. Pure; tested.
    ///
    /// Paging: the contract says `before` is "a cursor returned by a previous call" but the response is a bare
    /// `DmMessage[]`, so no cursor is ever returned. [Inference] The client sends the oldest loaded message's `at` (ISO-8601),
    /// which is exactly what the backend checkout compares (`created_at < before`). A page that adds nothing new ends paging.
    /// </summary>
    public sealed class DmThread
    {
        public const int MaxLength = 500;

        public readonly string Me, PeerId;
        private readonly List<DmRow> _rows = new List<DmRow>();
        private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool HasOlder { get; private set; } = true;
        public bool Loaded { get; private set; }

        public DmThread(string me, string peerId)
        {
            Me = me; PeerId = peerId;
        }

        public IReadOnlyList<DmRow> Messages => _rows;

        /// <summary>`before` for the next older page: the oldest confirmed message's time.</summary>
        public string OlderCursor
        {
            get
            {
                var oldest = _rows.FirstOrDefault(r => !r.IsLocal);
                return oldest == null ? null : SocialWire.IsoFromMs(oldest.AtMs);
            }
        }

        private bool Belongs(DmRow r) =>
            (SocialWire.SameId(r.FromUserId, PeerId) && (string.IsNullOrEmpty(r.ToUserId) || SocialWire.SameId(r.ToUserId, Me))) ||
            (SocialWire.SameId(r.FromUserId, Me) && (string.IsNullOrEmpty(r.ToUserId) || SocialWire.SameId(r.ToUserId, PeerId)));

        private bool Insert(DmRow r)
        {
            if (r == null || string.IsNullOrEmpty(r.MessageId) || !Belongs(r) || !_ids.Add(r.MessageId)) return false;
            // A server copy of one of our local echoes (same text, from me) replaces the oldest matching echo.
            if (SocialWire.SameId(r.FromUserId, Me))
            {
                var echo = _rows.FirstOrDefault(x => x.IsLocal && x.State != DmSendState.Failed && x.Text == r.Text && r.AtMs >= x.AtMs - 120_000);
                if (echo != null) { _rows.Remove(echo); _ids.Remove(echo.MessageId); }
            }
            int i = _rows.Count;
            while (i > 0 && Compare(_rows[i - 1], r) > 0) i--;
            _rows.Insert(i, r);
            return true;
        }

        private static int Compare(DmRow a, DmRow b)
        {
            if (a.IsLocal != b.IsLocal) return a.IsLocal ? 1 : -1; // pending echoes stay at the bottom
            int c = a.AtMs.CompareTo(b.AtMs);
            return c != 0 ? c : string.CompareOrdinal(a.MessageId, b.MessageId);
        }

        /// <summary>Applies a page (either order). Returns how many new messages it added.</summary>
        public int ApplyPage(IEnumerable<DmRow> page, bool older)
        {
            int added = 0;
            foreach (var r in page ?? Enumerable.Empty<DmRow>()) if (Insert(r)) added++;
            Loaded = true;
            if (older && added == 0) HasOlder = false;
            if (!older && added == 0 && _rows.Count == 0) HasOlder = false;
            return added;
        }

        /// <summary>City `dm {messageId, fromUserId, text, at}` (epoch ms). Returns true when it was new for this thread.</summary>
        public bool ApplyRealtime(string messageId, string fromUserId, string text, double atMs) =>
            Insert(new DmRow { MessageId = messageId, FromUserId = fromUserId, ToUserId = Me, Text = SocialWire.Plain(text, MaxLength), AtMs = (long)atMs });

        /// <summary>Validates text for POST /dms/{userId}. Returns a SocialStrings key or null with the trimmed text.</summary>
        public static string Validate(string input, out string text)
        {
            text = (input ?? "").Trim();
            if (text.Length == 0) return "dm.empty";
            if (text.Length > MaxLength) return "dm.tooLong";
            return null;
        }

        public DmRow AddLocal(string text, string key, double nowMs)
        {
            var row = new DmRow { MessageId = "local:" + key, LocalKey = key, FromUserId = Me, ToUserId = PeerId, Text = text, AtMs = (long)nowMs, State = DmSendState.Sending };
            _ids.Add(row.MessageId);
            _rows.Add(row);
            return row;
        }

        public void Confirm(string key, DmRow server)
        {
            var local = _rows.FirstOrDefault(r => r.LocalKey == key);
            if (local != null) { _rows.Remove(local); _ids.Remove(local.MessageId); }
            if (server != null)
            {
                if (string.IsNullOrEmpty(server.ToUserId)) server.ToUserId = PeerId;
                Insert(server);
            }
        }

        public void Fail(string key, string failureKey, bool retryable)
        {
            var local = _rows.FirstOrDefault(r => r.LocalKey == key);
            if (local == null) return;
            local.State = DmSendState.Failed;
            local.FailureKey = failureKey;
            local.RetrySameKey = retryable; // unknown outcome: the retry reuses the key; definitive: a retry is a new message
        }

        public DmRow Local(string key) => _rows.FirstOrDefault(r => r.LocalKey == key);

        public void Remove(string key)
        {
            var local = _rows.FirstOrDefault(r => r.LocalKey == key);
            if (local != null) { _rows.Remove(local); _ids.Remove(local.MessageId); }
        }

        /// <summary>The peer has written and I never have: an X-style message request (shown with the scam warning).</summary>
        public bool IsRequestFrom(string peer) =>
            _rows.Any(r => SocialWire.SameId(r.FromUserId, peer)) && !_rows.Any(r => SocialWire.SameId(r.FromUserId, Me) && !r.IsLocal);
    }

    public sealed class InboxEntry
    {
        public string UserId, Alias, LastMessage;
        public long LastAtMs;
        public int Unread;
        /// <summary>I have never replied in this conversation (local knowledge: the contract has no request state).</summary>
        public bool Replied;
        public bool Accepted;
    }

    /// <summary>
    /// The thread list: `GET /dms` merged with realtime `dm` pushes and conversations started on this device, newest first.
    /// Blocked users are hidden. Threads from people the player neither follows nor has replied to are "Requests", kept
    /// apart from the inbox the way X keeps message requests apart until accepted (see the research notes).
    /// </summary>
    public sealed class DmInbox
    {
        private readonly Dictionary<string, InboxEntry> _byUser = new Dictionary<string, InboxEntry>(StringComparer.OrdinalIgnoreCase);
        public string OpenPeer;

        public IEnumerable<InboxEntry> All => _byUser.Values.OrderByDescending(e => e.LastAtMs).ThenBy(e => e.UserId, StringComparer.Ordinal);

        public InboxEntry Get(string userId) => userId != null && _byUser.TryGetValue(userId, out var e) ? e : null;

        private InboxEntry Ensure(string userId)
        {
            if (!_byUser.TryGetValue(userId, out var e)) _byUser[userId] = e = new InboxEntry { UserId = userId };
            return e;
        }

        public void ApplyThreads(IEnumerable<DmThreadRow> rows)
        {
            foreach (var r in rows ?? Enumerable.Empty<DmThreadRow>())
            {
                if (string.IsNullOrEmpty(r.UserId)) continue;
                var e = Ensure(r.UserId);
                if (!string.IsNullOrEmpty(r.Alias)) e.Alias = r.Alias;
                if (r.LastAtMs >= e.LastAtMs) { e.LastAtMs = r.LastAtMs; e.LastMessage = r.LastMessage; }
            }
        }

        /// <summary>Records a conversation known locally (opened from a profile, restored from this device).</summary>
        public InboxEntry Touch(string userId, string alias, bool replied)
        {
            var e = Ensure(userId);
            if (!string.IsNullOrEmpty(alias)) e.Alias = alias;
            if (replied) e.Replied = true;
            return e;
        }

        /// <summary>A realtime `dm`. Returns the entry (unread is counted unless that thread is open).</summary>
        public InboxEntry ApplyIncoming(string fromUserId, string text, long atMs)
        {
            if (string.IsNullOrEmpty(fromUserId)) return null;
            var e = Ensure(fromUserId);
            if (atMs >= e.LastAtMs) { e.LastAtMs = atMs; e.LastMessage = SocialWire.Plain(text, 120); }
            if (!SocialWire.SameId(OpenPeer, fromUserId)) e.Unread++;
            return e;
        }

        public void ApplyOutgoing(string toUserId, string text, long atMs)
        {
            var e = Ensure(toUserId);
            e.Replied = true;
            e.Accepted = true;
            if (atMs >= e.LastAtMs) { e.LastAtMs = atMs; e.LastMessage = SocialWire.Plain(text, 120); }
        }

        public void MarkRead(string userId) { var e = Get(userId); if (e != null) e.Unread = 0; }

        public void Remove(string userId) => _byUser.Remove(userId ?? "");

        public bool IsRequest(InboxEntry e, Func<string, bool> following) =>
            e != null && !e.Replied && !e.Accepted && !(following?.Invoke(e.UserId) ?? false);

        public int UnreadTotal(Func<string, bool> hidden) => _byUser.Values.Where(e => !(hidden?.Invoke(e.UserId) ?? false)).Sum(e => e.Unread);
    }

    /// <summary>
    /// Scam red flags in DMs, posts and call invites. Display-only hints; the server's moderation is authoritative.
    /// Grounded in the research notes: MetaMask support "will never DM you" and "if anyone asks for your Secret Recovery
    /// Phrase, they are trying to scam you"; the FTC's "free money promises are always fake", "only scammers will guarantee
    /// profits"; the CT giveaway template "Drop your wallets" (observed 2026-10-04) and, from docs/research/ct-now-2026-10.md,
    /// "like and reply hi, surprise in DMs", "quote this post with your username" airdrop tests and the Sep 2026 "build an
    /// AI crypto bot" tutorials that make viewers drain their own wallets.
    /// </summary>
    public static class ScamSignals
    {
        public enum Flag { None, Secret, Drainer, Impersonation, Giveaway, Guarantee, SendFunds, Link }

        private static readonly (Flag flag, string[] needles)[] Rules =
        {
            (Flag.Secret, new[] { "seed phrase", "recovery phrase", "secret phrase", "private key", "12 words", "24 words", "mnemonic" }),
            (Flag.Drainer, new[] { "paste this", "run this script", "run this code", "import your wallet", "import your seed", "sign this to claim",
                "approve this", "build an ai bot", "ai trading bot", "connect your wallet to claim", "claim site" }),
            (Flag.Impersonation, new[] { "support team", "admin here", "official support", "verify your wallet", "validate your wallet",
                "wallet verification", "sync your wallet", "rectify", "we noticed an issue with your wallet" }),
            (Flag.Giveaway, new[] { "drop your wallet", "drop wallet", "drop your address", "comment your solana address", "reply with your wallet", "giveaway",
                "airdrop claim", "claim your airdrop", "free sol", "free usdc", "double your", "surprise in dm", "like and reply",
                "quote this post with your", "join my tg", "join my telegram", "join my telegrum" }),
            (Flag.Guarantee, new[] { "guaranteed", "100x", "1000x", "risk free", "risk-free", "can't lose", "cant lose", "no risk", "99% winrate", "99.99%" }),
            (Flag.SendFunds, new[] { "send sol", "send usdc", "send me", "pay first", "deposit first", "send to this address" }),
            (Flag.Link, new[] { "http://", "https://", "www.", ".com", ".io", ".xyz", ".app", ".fun", "t.me/", "discord.gg" }),
        };

        public static Flag Detect(string text)
        {
            if (string.IsNullOrEmpty(text)) return Flag.None;
            var t = text.ToLowerInvariant();
            foreach (var (flag, needles) in Rules)
                if (needles.Any(n => t.Contains(n))) return flag;
            return Flag.None;
        }
    }
}
