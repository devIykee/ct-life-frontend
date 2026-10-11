using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CTLife.Competition
{
    public enum ChatWho { Me, Npc, System }

    public enum ChatKind { Normal, Pending, Failed, Filtered, Refused, Notice }

    public sealed class ChatLine
    {
        public int Id;
        public ChatWho Who;
        public ChatKind Kind;
        public string Text;
        /// <summary>Server reply id, for "Report reply".</summary>
        public string ReplyId;
        public string ErrorKey;
    }

    public enum ChatBlock { None, Empty, TooLong, Busy, Cooldown, QuotaMinute, QuotaDay, Disabled, Unavailable }

    /// <summary>
    /// Chat with one of the two LLM NPCs (AI Agent, Sleuth). The model, the chat filter and the prompt-injection check all run
    /// server-side; this class only keeps the visible conversation, enforces the input limit and reflects rate-limit and
    /// moderation states the server reports. Replies are always labelled AI-generated.
    /// </summary>
    public sealed class ChatSession
    {
        /// <summary>`LlmChatRequest.message` maxLength in the proposal.</summary>
        public const int MaxChars = 500;
        /// <summary>Lines kept in memory per NPC (UI memory bound, not a game rule).</summary>
        public const int MaxLines = 60;

        public static readonly string[] Archetypes = { "ai_agent", "sleuth" };

        public readonly string Archetype;
        public readonly List<ChatLine> Lines = new List<ChatLine>();
        public LlmQuota Quota { get; private set; }
        public bool InFlight { get; private set; }
        public bool Disabled { get; private set; }
        public bool Unavailable { get; private set; }
        public long CooldownUntilMs { get; private set; }
        /// <summary>The per-minute counter in <see cref="Quota"/> is a one-minute window starting when it was received (`perMinuteLimit`).</summary>
        public const long MinuteWindowMs = 60000;
        public long QuotaAtMs { get; private set; }
        private int _nextId = 1;

        public ChatSession(string archetype)
        {
            if (Array.IndexOf(Archetypes, archetype) < 0) throw new ArgumentException("Not an LLM NPC", nameof(archetype));
            Archetype = archetype;
        }

        public static bool IsLlmNpc(string archetypeId) => Array.IndexOf(Archetypes, archetypeId) >= 0;

        /// <summary>Trim, drop control characters, fold line breaks and runs of spaces to single spaces.</summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder(raw.Length);
            bool space = false;
            foreach (var c in raw)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public ChatBlock Check(string raw, long nowMs)
        {
            if (Unavailable) return ChatBlock.Unavailable;
            if (Disabled) return ChatBlock.Disabled;
            if (InFlight) return ChatBlock.Busy;
            var t = Normalize(raw);
            if (t.Length == 0) return ChatBlock.Empty;
            if (t.Length > MaxChars) return ChatBlock.TooLong;
            if (nowMs < CooldownUntilMs) return ChatBlock.Cooldown;
            if (Quota != null)
            {
                if (Quota.RemainingToday <= 0 && Quota.PerDayLimit > 0) return ChatBlock.QuotaDay;
                if (Quota.RemainingThisMinute <= 0 && Quota.PerMinuteLimit > 0 && nowMs < QuotaAtMs + MinuteWindowMs) return ChatBlock.QuotaMinute;
            }
            return ChatBlock.None;
        }

        public void SetQuota(LlmQuota q, long nowMs) { if (q == null) return; Quota = q; QuotaAtMs = nowMs; }

        /// <summary>Appends the player's line as pending and returns it. Call only when Check(...) is None.</summary>
        public ChatLine Begin(string raw)
        {
            var line = Add(ChatWho.Me, ChatKind.Pending, Normalize(raw));
            InFlight = true;
            return line;
        }

        public void Complete(ChatLine mine, LlmReply reply, long nowMs)
        {
            InFlight = false;
            if (mine != null) mine.Kind = ChatKind.Normal;
            if (reply == null) return;
            SetQuota(reply.Quota, nowMs);
            if (reply.Refused)
            {
                Add(ChatWho.System, ChatKind.Refused, CompStrings.Has("chat.refusal." + reply.RefusalReasonId) ? CompStrings.T("chat.refusal." + reply.RefusalReasonId) : CompStrings.T("chat.refused"));
                return;
            }
            var text = string.IsNullOrWhiteSpace(reply.Text) ? CompStrings.T("chat.empty") : reply.Text;
            var line = Add(ChatWho.Npc, reply.Filtered ? ChatKind.Filtered : ChatKind.Normal, text);
            line.ReplyId = reply.ReplyId;
        }

        public void Fail(ChatLine mine, CompError err, long nowMs)
        {
            InFlight = false;
            if (mine != null) { mine.Kind = ChatKind.Failed; mine.ErrorKey = err?.Key; }
            if (err == null) return;
            if (err.Is("LLM_DISABLED")) Disabled = true;
            else if (err.IsUnavailable) Unavailable = true;
            else if (err.Is("RATE_LIMITED")) CooldownUntilMs = nowMs + (err.RetryAfterMs > 0 ? err.RetryAfterMs : 5000);
            Add(ChatWho.System, ChatKind.Notice, err.Message);
        }

        private ChatLine Add(ChatWho who, ChatKind kind, string text)
        {
            var l = new ChatLine { Id = _nextId++, Who = who, Kind = kind, Text = text };
            Lines.Add(l);
            while (Lines.Count > MaxLines) Lines.RemoveAt(0);
            return l;
        }

        /// <summary>One-line state under the input: why sending is blocked, or the quota left.</summary>
        public string StatusLine(string raw, long nowMs)
        {
            switch (Check(raw, nowMs))
            {
                case ChatBlock.Unavailable: return CompStrings.T("err.unavailable");
                case ChatBlock.Disabled: return CompStrings.T("err.llmDisabled");
                case ChatBlock.Busy: return CompStrings.T("chat.thinking");
                case ChatBlock.TooLong: return CompStrings.F("chat.tooLong", ChatSession.MaxChars);
                case ChatBlock.Cooldown: return CompStrings.F("err.rateLimited", CompTime.Countdown(CooldownUntilMs - nowMs));
                case ChatBlock.QuotaDay: return CompStrings.F("chat.quotaDay", Quota.ResetsAtMs.HasValue ? CompTime.When(Quota.ResetsAtMs.Value) : "-");
                case ChatBlock.QuotaMinute: return CompStrings.F("err.rateLimited", CompTime.Countdown(QuotaAtMs + MinuteWindowMs - nowMs));
                default:
                    return Quota != null && Quota.PerDayLimit > 0 ? CompStrings.F("chat.quotaLeft", Quota.RemainingToday, Quota.PerDayLimit) : "";
            }
        }
    }
}
