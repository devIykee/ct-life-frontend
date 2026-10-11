namespace CTLife.SocialFeatures
{
    /// <summary>
    /// What an incoming DM may put on the game screen (Duo notification banner), and where a banner tap may lead.
    /// The banner sits outside Messages' safety surfaces (request separation, scam warning, per-message scam hints,
    /// block), so it must never carry more than the inbox would show:
    /// <list type="bullet">
    /// <item>blocked sender, unsafe id: nothing at all;</item>
    /// <item>message request (someone you don't follow and haven't answered): no banner, the unread badge only;</item>
    /// <item>a message that trips a scam hint: the hint instead of the text;</item>
    /// <item>otherwise the plain text (callers render it literally; links are never tappable).</item>
    /// </list>
    /// </summary>
    public static class DmBannerPolicy
    {
        public enum Target { None, Inbox, Thread }

        /// <summary>A banner to show, or <c>Show == false</c>.</summary>
        public struct Banner
        {
            public bool Show;
            public string Title, Body;
            /// <summary>The scam hint that replaced the text (null when none).</summary>
            public ScamSignals.Flag Flag;
        }

        public static Banner For(string fromUserId, string name, string text, bool blocked, bool request)
        {
            if (blocked || request || !SocialWire.IsSafeId(fromUserId)) return default;
            var plain = SocialWire.Plain(text ?? "", 160);
            var flag = ScamSignals.Detect(plain);
            return new Banner
            {
                Show = true,
                Title = string.IsNullOrWhiteSpace(name) ? SocialStrings.T("dm.title") : name,
                Body = flag == ScamSignals.Flag.None ? plain : SocialStrings.T("flag." + flag),
                Flag = flag,
            };
        }

        /// <summary>
        /// Where a notification deep link opens Messages. Only a known, accepted conversation opens as a thread; a
        /// request (or anything not yet in the inbox) opens the inbox, where requests sit apart under the scam warning.
        /// A notification never counts as the player starting the conversation, so it never accepts a request.
        /// </summary>
        public static Target DeepLink(string peer, bool known, bool blocked, bool request)
        {
            if (!SocialWire.IsSafeId(peer) || blocked) return Target.None;
            return known && !request ? Target.Thread : Target.Inbox;
        }
    }
}
