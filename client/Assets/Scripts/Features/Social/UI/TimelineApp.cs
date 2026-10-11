using System;
using System.Linq;
using CTLife.Core;
using CTLife.UI;
using UnityEngine.UIElements;

namespace CTLife.SocialFeatures
{
    /// <summary>
    /// The in-game Timeline (feature #27, never X), phone app id `timeline`: For you / Following, a 280-character composer
    /// with CT post-format starters (gm, thread, call, PnL, rekt, journal; fictional tickers), format badges on posts, scam
    /// hints on "drop your wallet" giveaways, and per-post Follow, Message, Tip (#28), Report and Block.
    /// Posting never earns rewards (X developer policy forbids pay-to-engage; the project keeps that ban in-game too).
    /// </summary>
    public sealed class TimelineApp : SocialApp
    {
        public override string Id => "timeline";
        public override string Title => SocialStrings.T("feed.title");
        public override string Color => "#2d98da";
        public override string Feature => Features.Feed;

        /// <summary>Deep link from a profile card: open the tip sheet for this player.</summary>
        public static string PendingTipUser, PendingTipName;

        internal override SocialView CreateView(SocialHub hub, PhoneShell phone)
        {
            var v = new View(hub, phone, PendingTipUser, PendingTipName);
            PendingTipUser = null; PendingTipName = null;
            return v;
        }

        private sealed class View : SocialView
        {
            private readonly TimelineController _t;
            private readonly TextField _compose;
            private readonly Label _left, _composeMsg;
            private string _draft = "";
            private string _tipFor, _moreFor, _tipAmount = "", _tipSig = "", _tipMsg;
            private bool _tipBusy, _awaitingPost;
            private RetainedKey _tipKey = new RetainedKey(), _sigKey = new RetainedKey();

            private string _tipUser, _tipUserName;

            public View(SocialHub hub, PhoneShell phone, string tipUser, string tipName) : base(hub, phone)
            {
                _t = hub.Timeline;
                if (SocialWire.IsSafeId(tipUser) && !SocialWire.SameId(tipUser, hub.Me)) { _tipUser = tipUser; _tipUserName = tipName ?? hub.Names.Name(tipUser); }
                _compose = SocialUi.Field("feed.compose", _t.PendingPost ?? "", FeedTimeline.MaxLength + 40, v => { _draft = v; UpdateLeft(); }, multiline: true);
                _draft = _compose.value;
                _left = SocialUi.Small("");
                _composeMsg = SocialUi.Small("");
                var starters = SocialUi.Row(SocialUi.Small(SocialStrings.T("feed.starters")));
                foreach (var k in PostKinds.Starters)
                {
                    var kind = k;
                    starters.Add(SocialUi.Chip(SocialStrings.T("feed.kind." + kind), () => { _draft = SocialStrings.T("feed.starter." + kind); _compose.value = _draft; }, false));
                }
                Header.Add(SocialUi.H1(SocialStrings.T("feed.title")));
                Header.Add(_compose);
                Header.Add(SocialUi.Row(SocialUi.Btn("feed.post", Post, true), _left, _composeMsg));
                Header.Add(starters);
                Header.Add(SocialUi.Small(SocialStrings.T("feed.notAdvice")));
                UpdateLeft();
                _t.Watch(true);
            }

            public override void Dispose()
            {
                if (!Disposed) _t.Watch(false);
                base.Dispose();
            }

            private void UpdateLeft()
            {
                var left = FeedTimeline.Remaining(_draft);
                _left.text = SocialStrings.F("feed.left", left);
                _left.EnableInClassList("ct-error", left < 0);
            }

            private void Post()
            {
                var err = _t.Post(_draft);
                _composeMsg.text = err == null ? "" : SocialStrings.T(err);
                _awaitingPost = err == null;
                MarkDirty();
            }

            protected override void Render()
            {
                // Composer state follows the controller (a confirmed post clears it; an unknown outcome keeps it locked).
                if (_awaitingPost && !_t.Posting)
                {
                    _awaitingPost = false;
                    if (_t.PendingPost == null && _t.Message == "feed.posted") { _compose.SetValueWithoutNotify(""); _draft = ""; UpdateLeft(); }
                }
                _compose.SetEnabled(_t.PendingPost == null && !_t.Posting);

                var scope = SocialUi.Row(
                    SocialUi.Chip(SocialStrings.T("feed.global"), () => _t.SetScope("global"), _t.Scope == "global"),
                    SocialUi.Chip(SocialStrings.T("feed.following"), () => _t.SetScope("following"), _t.Scope == "following"),
                    El.Div("ct-grow"), SocialUi.Btn("common.refresh", _t.Refresh));
                Body.Add(scope);
                if (_tipUser != null)
                {
                    Body.Add(TipPanel(_tipUser, _tipUserName));
                    Body.Add(SocialUi.Ghost("common.close", () => { _tipUser = null; _tipMsg = null; MarkDirty(); }));
                }
                if (!Features.IsAvailable(Features.Feed)) Body.Add(SocialUi.Notice(SocialStrings.T("feed.degraded"), "error"));
                if (_t.Message != null)
                {
                    Body.Add(SocialUi.Notice(SocialStrings.T(_t.Message), _t.Message == "feed.postUnknown" ? "warn" : "info"));
                    if (_t.PendingPost != null)
                        Body.Add(SocialUi.Row(SocialUi.Btn("feed.postRetry", Post, true), SocialUi.Ghost("feed.forget", _t.ForgetPendingPost)));
                }
                if (_t.Error != null) Body.Add(SocialUi.Notice(SocialStrings.T(_t.Error), "error"));
                var posts = _t.Visible;
                if (_t.Loading && !_t.Feed.Loaded) { Body.Add(SocialUi.Copy("common.loading")); return; }
                if (posts.Count == 0) { Body.Add(SocialUi.Card(SocialUi.Copy(_t.Scope == "following" ? "feed.noneFollowing" : "feed.none"))); return; }
                foreach (var p in posts.Take(100)) RenderPost(p);
            }

            private void RenderPost(FeedPostRow p)
            {
                var author = p.UserId;
                var mine = SocialWire.SameId(author, Hub.Me);
                var name = string.IsNullOrWhiteSpace(p.Alias) ? Hub.Names.Name(author) : p.Alias;
                var head = SocialUi.Row(SocialUi.H2(mine ? SocialStrings.F("common.you", name) : name), SocialUi.Small(" · " + Ago.Short(p.AtMs, ServerClock.NowMs)));
                var kind = PostKinds.Detect(p.Text);
                if (kind != PostKind.Plain) head.Add(SocialUi.Badge(SocialStrings.T("feed.kind." + kind)));
                var card = SocialUi.Card(head, SocialUi.Text(p.Text));
                var tickers = PostKinds.Tickers(p.Text);
                if (tickers.Count > 0) card.Add(SocialUi.Small(string.Join("  ", tickers)));
                var flag = SocialUi.Flag(p.Text);
                if (flag != null && !mine) card.Add(flag);
                if (!mine)
                {
                    var follow = SocialUi.Btn(_t.Following(author) ? "feed.unfollow" : "feed.follow", () => _t.ToggleFollow(author), !_t.Following(author));
                    follow.SetEnabled(!_t.FollowBusy(author));
                    card.Add(SocialUi.Row(follow,
                        SocialUi.Btn("feed.message", () => { MessagesApp.PendingPeer = author; MessagesApp.PendingAlias = p.Alias; Phone?.Open("messages"); }),
                        SocialUi.Btn("feed.tip", () => { _tipFor = _tipFor == p.PostId ? null : p.PostId; _tipMsg = null; _tipKey = new RetainedKey(); _sigKey = new RetainedKey(); MarkDirty(); }),
                        SocialUi.Ghost("feed.more", () => { _moreFor = _moreFor == p.PostId ? null : p.PostId; MarkDirty(); })));
                    if (_tipFor == p.PostId) card.Add(TipPanel(author, name));
                    if (_moreFor == p.PostId)
                        card.Add(SocialUi.ReportPanel(name, reason => { _moreFor = null; Hub.Report("post", p.PostId, reason, null); MarkDirty(); }, () => { _moreFor = null; MarkDirty(); }));
                    if (_moreFor == p.PostId) card.Add(SocialUi.Danger("common.block", () => { _moreFor = null; Hub.Block(author, null); }));
                }
                Body.Add(card);
            }

            /// <summary>Tip sheet (#28): Cash (game money, server-capped daily) or a record of a wallet-to-wallet tip.</summary>
            private VisualElement TipPanel(string userId, string name)
            {
                var panel = SocialUi.Card(SocialUi.H2(SocialStrings.F("tip.title", name)));
                panel.Add(SocialUi.Eyebrow("tip.cash"));
                panel.Add(SocialUi.Small(SocialStrings.T("tip.cashHint")));
                var bal = _t.BalanceCents;
                if (bal.HasValue) panel.Add(SocialUi.Small(SocialStrings.F("tip.balance", CashInput.Format(bal.Value))));
                var cap = _t.TipCapCents;
                panel.Add(SocialUi.Small(cap.HasValue ? SocialStrings.F("tip.capKnown", CashInput.Format(cap.Value)) : SocialStrings.T("tip.capServer")));
                panel.Add(SocialUi.Field("tip.amount", _tipAmount, 12, v => _tipAmount = v));
                var send = SocialUi.Btn("tip.send", () =>
                {
                    _tipBusy = true; _tipMsg = null; MarkDirty();
                    var amount = _tipAmount;
                    _t.TipCash(userId, amount, _tipKey, err =>
                    {
                        _tipBusy = false;
                        _tipMsg = err == null ? SocialStrings.F("tip.sent", name) : SocialStrings.T(err);
                        if (err == null) { _tipAmount = ""; _tipKey = new RetainedKey(); }
                        MarkDirty();
                    });
                }, true);
                send.SetEnabled(!_tipBusy);
                panel.Add(send);
                panel.Add(SocialUi.Eyebrow("tip.onchain"));
                panel.Add(SocialUi.Small(SocialStrings.T("tip.onchainHint")));
                panel.Add(SocialUi.Field("tip.signature", _tipSig, 100, v => _tipSig = v));
                var record = SocialUi.Btn("tip.record", () =>
                {
                    _tipBusy = true; _tipMsg = null; MarkDirty();
                    _t.RecordOnchainTip(userId, _tipSig, _sigKey, err =>
                    {
                        _tipBusy = false;
                        _tipMsg = SocialStrings.T(err ?? "tip.recorded");
                        if (err == null) { _tipSig = ""; _sigKey = new RetainedKey(); }
                        MarkDirty();
                    });
                });
                record.SetEnabled(!_tipBusy);
                panel.Add(record);
                if (_tipMsg != null) panel.Add(SocialUi.Notice(_tipMsg));
                return panel;
            }
        }
    }
}
