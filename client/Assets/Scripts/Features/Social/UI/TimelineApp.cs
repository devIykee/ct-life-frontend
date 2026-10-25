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
    /// Duo layout (spec Timeline): sticky tabs with a blue underline, posts as rows with hairlines, ticker chips, an
    /// action row, and a floating compose button that opens the composer in a sheet.
    /// </summary>
    public sealed class TimelineApp : SocialApp
    {
        public override string Id => "timeline";
        public override string Title => SocialStrings.T("feed.title");
        public override string Color => "#2d98da";
        public override string Feature => Features.Feed;
        public override DuoChrome Chrome => DuoChrome.Plain;

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
            private readonly VisualElement _frame;
            private readonly Button _tabGlobal, _tabFollowing;
            private readonly TextField _compose;
            private readonly Label _left, _composeMsg;
            private readonly VisualElement _composer;
            private Button _postButton;
            private DuoSheet _composeSheet;
            private string _draft = "";
            private bool _awaitingPost;

            public View(SocialHub hub, PhoneShell phone, string tipUser, string tipName) : base(hub, phone)
            {
                _t = hub.Timeline;
                Root.RemoveFromClassList("ct-app-page");
                Root.RemoveFromClassList("ct-social");
                Root.AddToClassList("duo-scroll");
                Root.contentContainer.AddToClassList("duo-scroll-content");
                _frame = El.Div("duo-page duo-timeline");
                _frame.name = "timeline-frame";
                _frame.Add(Root);

                // Sticky top: nav + For you / Following with the blue underline.
                var nav = new DuoNav();
                nav.SetTitle(SocialStrings.T("feed.title"));
                nav.SetCollapsed(true, instant: true);
                nav.AddLeft(DuoGlyph.Kind.Back, SocialStrings.T("common.back"), () => Phone?.Back(), "timeline-back");
                nav.Right.Add(Duo.Circle(new UiIcon("rotate"), SocialStrings.T("common.refresh"), () => _t.Refresh(), "social-common-refresh"));
                _tabGlobal = El.Btn(SocialStrings.T("feed.global"), () => _t.SetScope("global"), "duo-tab");
                _tabGlobal.name = "timeline-tab-global";
                _tabFollowing = El.Btn(SocialStrings.T("feed.following"), () => _t.SetScope("following"), "duo-tab");
                _tabFollowing.name = "timeline-tab-following";
                foreach (var tab in new[] { _tabGlobal, _tabFollowing }) tab.Add(El.PassThrough(El.Div("duo-tab-underline")));
                var top = El.Div("duo-timeline-top", nav, El.Div("duo-tabs", _tabGlobal, _tabFollowing));
                top.pickingMode = PickingMode.Ignore;
                _frame.Add(top);

                // The composer (persistent; shown in a sheet from the floating button).
                _compose = new TextField { maxLength = FeedTimeline.MaxLength + 40, multiline = true, isDelayed = false, value = _t.PendingPost ?? "" };
                _compose.name = "timeline-compose";
                _compose.AddToClassList("duo-field");
                _compose.AddToClassList("duo-compose-field");
                _compose.textEdition.placeholder = SocialStrings.T("feed.placeholder");
                _compose.RegisterValueChangedCallback(e => { _draft = e.newValue ?? ""; UpdateLeft(); });
                _draft = _compose.value;
                _left = El.Text("", "duo-compose-left");
                _composeMsg = El.Text("", "duo-compose-msg");
                var starters = El.Div("duo-compose-starters");
                foreach (var k in PostKinds.Starters)
                {
                    var kind = k;
                    var pill = Duo.Pill(SocialStrings.T("feed.kind." + kind), () => { _draft = SocialStrings.T("feed.starter." + kind); _compose.value = _draft; });
                    pill.name = "timeline-starter-" + kind;
                    starters.Add(pill);
                }
                _postButton = El.Btn(SocialStrings.T("feed.post"), Post, "duo-sheet-primary");
                _postButton.name = "social-feed-post";
                _composer = El.Div("duo-composer-sheet", _compose, El.Div("duo-compose-meta", _left, El.Div("ct-grow")), _composeMsg,
                    Duo.GroupHeader(SocialStrings.T("feed.starters")), starters, _postButton,
                    Duo.Footnote(SocialStrings.T("feed.notAdvice")));
                UpdateLeft();

                var fab = new Button(OpenComposer) { name = "timeline-compose-button", tooltip = SocialStrings.T("feed.newPost") };
                fab.AddToClassList("duo-fab");
                Duo.AddShadow(fab, "fab");
                fab.Add(El.PassThrough(El.Div("duo-fab-face", new DuoGlyph(DuoGlyph.Kind.Plus))));
                _frame.Add(fab);

                if (SocialWire.IsSafeId(tipUser) && !SocialWire.SameId(tipUser, hub.Me))
                {
                    var user = tipUser; var name = tipName ?? hub.Names.Name(tipUser);
                    _frame.RegisterCallback<AttachToPanelEvent>(OnceTip);
                    void OnceTip(AttachToPanelEvent e)
                    {
                        _frame.UnregisterCallback<AttachToPanelEvent>(OnceTip);
                        _frame.schedule.Execute(() => TipSheet(user, name));
                    }
                }
                _t.Watch(true);
            }

            public override VisualElement Container => _frame;

            public override void Dispose()
            {
                if (!Disposed) { _t.Watch(false); _composeSheet?.Close(); }
                base.Dispose();
            }

            private void UpdateLeft()
            {
                var left = FeedTimeline.Remaining(_draft);
                _left.text = SocialStrings.F("feed.left", left);
                _left.EnableInClassList("duo-compose-over", left < 0);
            }

            private void OpenComposer()
            {
                if (Disposed) return;
                _composeSheet?.Close();
                _composer.RemoveFromHierarchy();
                _composeSheet = new DuoSheet(SocialStrings.T("feed.newPost"));
                _composeSheet.name = "timeline-compose-sheet";
                _composeSheet.AddContent(_composer);
                var sheet = _composeSheet;
                _composeSheet.Closed += () => { if (_composeSheet == sheet) _composeSheet = null; };
                _composeSheet.Show(_frame);
            }

            private void Post()
            {
                var err = _t.Post(_draft);
                _composeMsg.text = err == null ? "" : SocialStrings.T(err);
                _composeMsg.EnableInClassList("duo-compose-error", err != null);
                _awaitingPost = err == null;
                MarkDirty();
            }

            protected override void Render()
            {
                // Composer state follows the controller (a confirmed post clears it; an unknown outcome keeps it locked).
                if (_awaitingPost && !_t.Posting)
                {
                    _awaitingPost = false;
                    if (_t.PendingPost == null && _t.Message == "feed.posted")
                    {
                        _compose.SetValueWithoutNotify(""); _draft = ""; UpdateLeft();
                        _composeSheet?.Close();
                    }
                }
                bool editable = _t.PendingPost == null && !_t.Posting;
                _compose.SetEnabled(editable);
                _postButton.SetEnabled(editable);
                _postButton.text = SocialStrings.T(_t.Posting ? "feed.posting" : "feed.post");
                _tabGlobal.EnableInClassList("duo-tab-on", _t.Scope == "global");
                _tabFollowing.EnableInClassList("duo-tab-on", _t.Scope == "following");

                if (!Features.IsAvailable(Features.Feed)) Body.Add(Duo.Notice(SocialStrings.T("feed.degraded"), Duo.Tone.Danger));
                if (_t.Message != null)
                {
                    Body.Add(Duo.Notice(SocialStrings.T(_t.Message), _t.Message == "feed.postUnknown" ? Duo.Tone.Warning : Duo.Tone.Info));
                    if (_t.PendingPost != null)
                    {
                        var retry = Duo.Pill(SocialStrings.T("feed.postRetry"), Post, Duo.PillKind.Primary);
                        retry.name = "social-feed-postRetry";
                        Body.Add(El.Div("duo-topup-actions", retry, Duo.Pill(SocialStrings.T("feed.forget"), _t.ForgetPendingPost)));
                    }
                }
                if (_t.Error != null) Body.Add(Duo.Notice(SocialStrings.T(_t.Error), Duo.Tone.Danger));
                var posts = _t.Visible;
                if (_t.Loading && !_t.Feed.Loaded) { Body.Add(Duo.Footnote(SocialStrings.T("common.loading"))); return; }
                if (posts.Count == 0) { Body.Add(Duo.Empty(SocialStrings.T(_t.Scope == "following" ? "feed.noneFollowing" : "feed.none"))); return; }
                var list = El.Div("duo-feed");
                foreach (var p in posts.Take(100)) list.Add(RenderPost(p));
                Body.Add(list);
            }

            private VisualElement RenderPost(FeedPostRow p)
            {
                var author = p.UserId;
                var mine = SocialWire.SameId(author, Hub.Me);
                var name = string.IsNullOrWhiteSpace(p.Alias) ? Hub.Names.Name(author) : p.Alias;
                var head = El.Div("duo-post-head",
                    El.Text(mine ? SocialStrings.F("common.you", name) : name, "duo-post-name"),
                    El.Text("· " + Ago.Short(p.AtMs, ServerClock.NowMs), "duo-post-time"));
                var kind = PostKinds.Detect(p.Text);
                if (kind != PostKind.Plain) head.Add(El.Text(SocialStrings.T("feed.kind." + kind), "duo-post-kind" + (kind == PostKind.Giveaway ? " duo-post-kind-warn" : "")));
                var body = El.Div("duo-post-body", head, SocialUi.Text(p.Text, "duo-post-text"));
                var tickers = PostKinds.Tickers(p.Text);
                if (tickers.Count > 0)
                {
                    var chips = El.Div("duo-post-chips");
                    foreach (var t in tickers.Take(6)) chips.Add(El.Div("duo-coin-chip", El.Div("duo-coin-dot"), El.Text(t, "duo-coin-chip-text")));
                    body.Add(chips);
                }
                var flag = SocialUi.Flag(p.Text);
                if (flag != null && !mine) body.Add(El.Text(flag.text, "duo-post-flag"));
                if (!mine)
                {
                    bool following = _t.Following(author);
                    var follow = PostAction(following ? "feed.unfollow" : "feed.follow", "contacts", () => _t.ToggleFollow(author));
                    follow.SetEnabled(!_t.FollowBusy(author));
                    follow.EnableInClassList("duo-post-action-on", following);
                    var actions = El.Div("duo-post-actions",
                        PostAction("feed.message", "chat", () => { MessagesApp.PendingPeer = author; MessagesApp.PendingAlias = p.Alias; Phone?.Open("messages"); }),
                        PostAction("feed.tip", "cash", () => TipSheet(author, name)),
                        follow,
                        PostAction("feed.more", "menu", () => MoreSheet(p, author, name)));
                    body.Add(actions);
                }
                var row = El.Div("duo-post", Duo.Avatar(name, author, "duo-avatar-post"), body);
                row.name = "timeline-post-" + p.PostId;
                return row;
            }

            private static Button PostAction(string key, string icon, Action clicked)
            {
                var b = El.Btn("", clicked, "duo-post-action");
                b.name = "social-" + key.Replace('.', '-');
                b.tooltip = SocialStrings.T(key);
                b.Add(El.PassThrough(new UiIcon(icon)));
                b.Add(El.PassThrough(El.Text(SocialStrings.T(key), "duo-post-action-label")));
                return b;
            }

            private void MoreSheet(FeedPostRow p, string author, string name)
            {
                var sheet = new DuoSheet(SocialStrings.F("common.reportTitle", name), SocialStrings.T("common.reportWhy"));
                var reasons = Services.TryGet<ConfigStore>(out var cfg) ? cfg.Registries.ReportReasons : null;
                if (reasons != null)
                    foreach (var r in reasons)
                    {
                        var id = r.id;
                        sheet.AddOption(Strings.ReportReason(r.id, r.name), () => { Hub.Report("post", p.PostId, id, null); MarkDirty(); });
                    }
                sheet.AddOption(SocialStrings.T("common.block"), () => Hub.Block(author, null), destructive: true, name: "timeline-block");
                sheet.AddSecondary(SocialStrings.T("common.cancel"));
                sheet.Show(_frame);
            }

            /// <summary>Tip sheet (#28): Cash (game money, server-capped daily) or a record of a wallet-to-wallet tip.</summary>
            private void TipSheet(string userId, string name)
            {
                if (Disposed) return;
                var sheet = new DuoSheet(SocialStrings.F("tip.title", name));
                sheet.name = "timeline-tip-sheet";
                var panel = El.Div("duo-tip");
                string amount = "", signature = "";
                bool busy = false;
                var message = El.Text("", "duo-compose-msg");
                panel.Add(Duo.GroupHeader(SocialStrings.T("tip.cash")));
                panel.Add(Duo.Footnote(SocialStrings.T("tip.cashHint"), "duo-topup-note"));
                var bal = _t.BalanceCents;
                if (bal.HasValue) panel.Add(Duo.Footnote(SocialStrings.F("tip.balance", CashInput.Format(bal.Value)), "duo-topup-note"));
                var cap = _t.TipCapCents;
                panel.Add(Duo.Footnote(cap.HasValue ? SocialStrings.F("tip.capKnown", CashInput.Format(cap.Value)) : SocialStrings.T("tip.capServer"), "duo-topup-note"));
                var amountField = new TextField { maxLength = 12, isDelayed = false, name = "tip-amount" };
                amountField.AddToClassList("duo-field");
                amountField.textEdition.placeholder = SocialStrings.T("tip.amount");
                // backend #20: 3% of a Cash tip is burned; say what they get before sending
                var feeLine = Duo.Footnote(SocialStrings.F("tip.feeHint", TipRules.FeeBps / 100), "duo-topup-note");
                feeLine.name = "tip-fee";
                void UpdateFee()
                {
                    feeLine.text = CashInput.TryCents(amount, out var c) && c >= TipRules.MinCents
                        ? SocialStrings.F("tip.fee", TipRules.FeeBps / 100, CashInput.Format(TipRules.ReceivedCents(c)))
                        : SocialStrings.F("tip.feeHint", TipRules.FeeBps / 100);
                }
                amountField.RegisterValueChangedCallback(e => { amount = e.newValue; UpdateFee(); });
                panel.Add(amountField);
                panel.Add(feeLine);
                var tipKey = new RetainedKey();
                var sigKey = new RetainedKey();
                Button send = null, record = null;
                void Busy(bool on) { busy = on; send.SetEnabled(!on); record.SetEnabled(!on); }
                send = El.Btn(SocialStrings.T("tip.send"), () =>
                {
                    if (busy) return;
                    Busy(true); message.text = "";
                    _t.TipCash(userId, amount, tipKey, (err, tip) =>
                    {
                        Busy(false);
                        message.text = err != null ? SocialStrings.T(err)
                            : tip != null ? SocialStrings.F("tip.sentNet", name, CashInput.Format(tip.ReceivedCents), CashInput.Format(tip.FeeCents))
                            : SocialStrings.F("tip.sent", name);
                        if (err == null) { amount = ""; amountField.SetValueWithoutNotify(""); tipKey = new RetainedKey(); UpdateFee(); }
                    });
                }, "duo-sheet-primary");
                send.name = "social-tip-send";
                panel.Add(send);
                panel.Add(message);   // the result right under the button (it was below the wallet section, out of view)
                panel.Add(Duo.GroupHeader(SocialStrings.T("tip.onchain")));
                panel.Add(Duo.Footnote(SocialStrings.T("tip.onchainHint"), "duo-topup-note"));
                var sigField = new TextField { maxLength = 100, isDelayed = false, name = "tip-signature" };
                sigField.AddToClassList("duo-field");
                sigField.textEdition.placeholder = SocialStrings.T("tip.signature");
                sigField.RegisterValueChangedCallback(e => signature = e.newValue);
                panel.Add(sigField);
                record = El.Btn(SocialStrings.T("tip.record"), () =>
                {
                    if (busy) return;
                    Busy(true); message.text = "";
                    _t.RecordOnchainTip(userId, signature, sigKey, err =>
                    {
                        Busy(false);
                        message.text = SocialStrings.T(err ?? "tip.recorded");
                        if (err == null) { signature = ""; sigField.SetValueWithoutNotify(""); sigKey = new RetainedKey(); }
                    });
                }, "duo-sheet-secondary");
                record.name = "social-tip-record";
                panel.Add(record);
                var scroll = El.VerticalScroll("duo-sheet-scroll");
                scroll.Add(panel);
                sheet.AddContent(scroll);
                sheet.AddSecondary(SocialStrings.T("common.close"));
                sheet.Show(_frame);
            }
        }
    }
}
