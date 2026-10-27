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
    /// hints on "drop your wallet" giveaways, and per-post Follow, Message, Report and Block. (Cash tips were removed with backend #25: Cash never moves between players.)
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

        internal override SocialView CreateView(SocialHub hub, PhoneShell phone) => new View(hub, phone);

        private sealed class View : SocialView
        {
            private readonly TimelineController _t;
            private readonly VisualElement _frame;
            private readonly Button _tabGlobal, _tabFollowing, _tabCity;
            private readonly TextField _compose;
            private readonly Label _left, _composeMsg;
            private readonly VisualElement _composer;
            private Button _postButton, _checkin;
            private Toggle _placeToggle;
            private DuoSheet _composeSheet;
            private string _draft = "";
            private bool _awaitingPost;

            public View(SocialHub hub, PhoneShell phone) : base(hub, phone)
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
                _tabCity = El.Btn(TimelineText.T("tab.city"), () => _t.SetScope("city"), "duo-tab");
                _tabCity.name = "timeline-tab-city";
                foreach (var tab in new[] { _tabGlobal, _tabFollowing, _tabCity }) tab.Add(El.PassThrough(El.Div("duo-tab-underline")));
                var top = El.Div("duo-timeline-top", nav, El.Div("duo-tabs", _tabGlobal, _tabFollowing, _tabCity));
                _style ??= UnityEngine.Resources.Load<StyleSheet>("UI/timeline-world");
                var uiRoot = UiRoot.Instance != null ? UiRoot.Instance.Root : null;
                if (uiRoot != null && _style != null && !uiRoot.styleSheets.Contains(_style)) uiRoot.styleSheets.Add(_style);
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
                // backend #32: opt-in place on posts, and an explicit check-in (nothing posts by itself)
                _placeToggle = new Toggle(TimelineText.T("checkin.share")) { name = "timeline-with-place", value = _t.WithPlace };
                _placeToggle.AddToClassList("tl-toggle");
                _placeToggle.RegisterValueChangedCallback(e => _t.WithPlace = e.newValue);
                _checkin = El.Btn("", CheckIn, "duo-sheet-secondary");
                _checkin.name = "timeline-checkin";
                _composer = El.Div("duo-composer-sheet", _compose, El.Div("duo-compose-meta", _left, El.Div("ct-grow")), _composeMsg,
                    _placeToggle, Duo.Footnote(TimelineText.T("checkin.shareHint")), _checkin,
                    Duo.GroupHeader(SocialStrings.T("feed.starters")), starters, _postButton,
                    Duo.Footnote(SocialStrings.T("feed.notAdvice")));
                UpdateLeft();

                var fab = new Button(OpenComposer) { name = "timeline-compose-button", tooltip = SocialStrings.T("feed.newPost") };
                fab.AddToClassList("duo-fab");
                Duo.AddShadow(fab, "fab");
                fab.Add(El.PassThrough(El.Div("duo-fab-face", new DuoGlyph(DuoGlyph.Kind.Plus))));
                _frame.Add(fab);

                _t.Watch(true);
            }

            private static StyleSheet _style;

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

            /// <summary>The venue zone the player stands in on the island (a check-in target), or null.</summary>
            private static string HereVenue()
            {
                var s = CTLife.World.VenueSession.Current;
                var id = s?.VenueId;
                if (string.IsNullOrEmpty(id) || id == "home" || !Services.TryGet<ConfigStore>(out var c) || c.Registries == null || !c.Registries.Venues.ContainsKey(id)) return null;
                return id;
            }

            private void CheckIn()
            {
                var here = HereVenue();
                _composeMsg.text = "";
                _t.CheckIn(here, err =>
                {
                    _composeMsg.text = err == null ? TimelineText.F("checkin.done", VenueName(here)) : Copy(err);
                    _composeMsg.EnableInClassList("duo-compose-error", err != null);
                    MarkDirty();
                });
            }

            private static string Copy(string key) => key != null && key.StartsWith("timeline.") ? TimelineText.T(key.Substring(9)) : SocialStrings.T(key);

            private void OpenComposer()
            {
                if (Disposed) return;
                var here = HereVenue();
                _checkin.text = here != null ? TimelineText.F("checkin.at", VenueName(here)) : TimelineText.T("checkin.none");
                _checkin.SetEnabled(here != null);
                _placeToggle.SetValueWithoutNotify(_t.WithPlace);
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
                _tabCity.EnableInClassList("duo-tab-on", _t.Scope == "city");

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
                if (_t.Scope == "city") { RenderCity(); return; }
                Body.Add(ComposeRow());
                var rows = _t.Rows;
                if (_t.Loading && !_t.Feed.Loaded && rows.Count == 0) { Body.Add(Duo.Footnote(SocialStrings.T("common.loading"))); return; }
                if (_t.ServerTabs && _t.Quiet && _t.Scope != "following") Body.Add(SocialUi.Text(TimelineText.T("quiet"), "tl-quiet"));
                if (rows.Count == 0)
                {
                    if (_t.Scope == "following") { Body.Add(Duo.Empty(SocialStrings.T("feed.noneFollowing"))); return; }
                    // never empty: the city's last five moments (brief 6)
                    var recent = _t.Events.Take(5).ToList();
                    if (recent.Count == 0) { Body.Add(Duo.Empty(SocialStrings.T("feed.none"))); return; }
                    Body.Add(SocialUi.Text(TimelineText.T("quiet"), "tl-quiet"));
                    var quiet = El.Div("duo-feed");
                    foreach (var e in recent) quiet.Add(RenderEvent(e));
                    Body.Add(quiet);
                    return;
                }
                var verdicts = TimelineMix.Verdicts(_t.Visible);
                var list = El.Div("duo-feed");
                foreach (var p in rows.Take(200)) list.Add(RenderRow(p, verdicts));
                Body.Add(list);
                AddLoadMore();
            }

            private void AddLoadMore()
            {
                if (!_t.HasMore) return;
                var more = Duo.Pill(SocialStrings.T(_t.LoadingMore ? "common.loading" : "timeline.more"), _t.LoadMore);
                more.text = _t.LoadingMore ? SocialStrings.T("common.loading") : TimelineText.T("more");
                more.name = "timeline-load-more";
                Body.Add(El.Div("tl-more", more));
            }

            private VisualElement ComposeRow()
            {
                var me = Hub.Me;
                var row = new Button(OpenComposer) { name = "timeline-compose-row" };
                row.AddToClassList("tl-compose");
                row.Add(El.PassThrough(Duo.Avatar(Hub.Names.Name(me), me, "duo-avatar-post")));
                row.Add(El.PassThrough(SocialUi.Text(TimelineText.T("compose.prompt"), "tl-compose-prompt")));
                return row;
            }

            private void RenderCity()
            {
                var chips = El.Div("tl-filters");
                foreach (var f in EventKinds.Filters)
                {
                    var kind = f;
                    if (_t.ServerTabs && kind == EventKinds.Exploit) continue;   // backend #32 has no exploit source yet
                    var chip = El.Btn(TimelineText.T("filter." + (kind ?? "all")), () => _t.SetCityFilter(kind), "tl-chip");
                    chip.name = "timeline-filter-" + (kind ?? "all");
                    chip.EnableInClassList("tl-chip--on", _t.CityFilter == kind);
                    chips.Add(chip);
                }
                Body.Add(chips);
                var mood = _t.Mood;
                if (mood != null && long.TryParse(SocialWire.Str(mood.Meta?["moodBps"]), out var bps))
                {
                    var strip = El.Div("tl-mood", SocialUi.Text(TimelineText.F("mood", (bps >= 0 ? "+" : "") + (bps / 100.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%"), null));
                    strip.EnableInClassList("tl-mood--up", bps > 0);
                    strip.EnableInClassList("tl-mood--down", bps < 0);
                    Body.Add(strip);
                }
                var rows = _t.Rows;
                if (rows.Count == 0) { Body.Add(Duo.Empty(TimelineText.T("cityEmpty"))); return; }
                var list = El.Div("duo-feed");
                foreach (var p in rows.Take(200)) list.Add(RenderEvent(p));
                Body.Add(list);
                AddLoadMore();
            }

            private VisualElement RenderRow(FeedPostRow p, System.Collections.Generic.Dictionary<string, FeedPostRow> verdicts)
            {
                if (EventKinds.IsCity(p.Kind)) return RenderEvent(p);
                if (p.IsSleuth) return RenderSleuth(p);
                return RenderPost(p, verdicts);
            }

            // ------------------------------------------------------------ city event card (one line)

            private VisualElement RenderEvent(FeedPostRow p)
            {
                var row = El.Div("tl-event tl-event--" + p.Kind);
                row.name = "timeline-event-" + p.PostId;
                row.Add(El.Div("tl-event-icon", new UiIcon(TimelineText.Icon(p.Kind))));
                // city cards are written by the server (not players): old names in them read with the approved ones
                var line = string.IsNullOrWhiteSpace(p.Text) ? TimelineText.Event(p) : CTLife.Core.NameTable.Apply(p.Text);
                var col = El.Div("tl-event-col", SocialUi.Text(line, "tl-event-text"),
                    SocialUi.Text(Ago.Short(p.AtMs, ServerClock.NowMs) + SocialLine(p), "tl-event-time"));
                row.Add(col);
                var card = p.Card != null ? FromServer(p.Card) : ActionCards.For(p, TimelineLinks.CoinIdByTicker);
                var act = CardButton(card, true);
                if (act != null) row.Add(act);
                if (p.SecondaryCard != null && p.SecondaryCard.Type == "venue")
                {
                    var go = CardButton(FromServer(p.SecondaryCard), true);
                    if (go != null) row.Add(go);
                }
                return row;
            }

            /// <summary>Launch cards: "· 12 holders · dana, kofi bought in" (only people you follow are named).</summary>
            private static string SocialLine(FeedPostRow p)
            {
                if (p.Social == null) return "";
                var s = p.Social.Holders > 0 ? " \u00b7 " + TimelineText.F("holders", p.Social.Holders) : "";
                if (p.Social.Followed.Count > 0) s += " \u00b7 " + TimelineText.F("boughtIn", string.Join(", ", p.Social.Followed.Select(f => f.alias)));
                return s;
            }

            private static ActionCard FromServer(ServerCard c) => c.Type switch
            {
                "coin" => new ActionCard { Kind = ActionCard.CardKind.Coin, CoinId = c.CoinId, Ticker = c.Ticker },
                "venue" => new ActionCard { Kind = ActionCard.CardKind.Place, VenueId = c.VenueId },
                "profile" => new ActionCard { Kind = ActionCard.CardKind.Player, UserId = c.UserId },
                _ => ActionCard.None,
            };

            private Button CardButton(ActionCard card, bool small)
            {
                if (card.Kind == ActionCard.CardKind.Coin && card.CoinId != null)
                {
                    var b = El.Btn(TimelineText.T("card.open"), () => OpenCoin(card.CoinId), "tl-pill" + (small ? " tl-pill--ghost" : ""));
                    b.name = "timeline-open-coin";
                    return b;
                }
                if (card.Kind == ActionCard.CardKind.Place && card.VenueId != null)
                {
                    var b = El.Btn(TimelineText.T("card.go"), () => GoTo(card.VenueId), "tl-pill tl-pill--ghost");
                    b.name = "timeline-go-place";
                    return b;
                }
                if (card.Kind == ActionCard.CardKind.Player && card.UserId != null)
                {
                    var b = El.Btn(TimelineText.T("card.profile"), () => Bus.Publish(new AvatarTappedSignal { userId = card.UserId }), "tl-pill tl-pill--ghost");
                    b.name = "timeline-open-profile";
                    return b;
                }
                return null;
            }

            private void OpenCoin(string coinId)
            {
                Log.Info("timeline", "card: open coin " + coinId);
                if (CTLife.Core.VenueLinks.OpenCoin != null) CTLife.Core.VenueLinks.OpenCoin(coinId);
            }

            private void GoTo(string venueId)
            {
                Log.Info("timeline", "card: go to " + venueId);
                Phone?.Close();
                if (CTLife.Core.VenueLinks.Open("venue:" + venueId))
                    Bus.Publish(ToastSignal.Text(TimelineText.F("route", VenueName(venueId)), ToastKind.Info));
            }

            private static string VenueName(string venueId) =>
                Services.TryGet<ConfigStore>(out var c) && c.Registries != null && c.Registries.Venues.TryGetValue(venueId, out var v) && !string.IsNullOrEmpty(v.name)
                    ? v.name : Strings.ReadableId(venueId);

            // ------------------------------------------------------------ action card under a post

            private static string CallLine(Newtonsoft.Json.Linq.JObject call)
            {
                var entry = SocialWire.OptLong(call["entryMicros"]);
                var target = SocialWire.Str(call["targetX"]);
                var stop = SocialWire.Str(call["stopPct"]);
                return TimelineText.F("call", entry.HasValue ? "$" + (entry.Value / 1_000_000.0).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) : "?", target ?? "?", stop ?? "?");
            }

            private VisualElement ActionCardView(ActionCard card, ServerCard server = null)
            {
                if (card.Kind == ActionCard.CardKind.Coin && card.CoinId != null)
                {
                    var info = TimelineLinks.Coin?.Invoke(card.CoinId);
                    if (server != null && server.Type == "coin")
                    {
                        // the server's live numbers win; the market supplies the player coin face
                        info = new CoinCardInfo
                        {
                            CoinId = server.CoinId, Ticker = server.Ticker, Name = server.Name, Stage = server.Stage, McapCents = server.McapCents,
                            Change5mBps = server.Change5mBps, Rugged = server.Rugged, Art = info?.Art, Player = info?.Player == true,
                        };
                        if (server.Dead) info.Stage = "dead";
                    }
                    var box = El.Div("tl-card");
                    box.name = "timeline-coin-card";
                    var art = El.Div("tl-card-art");
                    if (info?.Art != null) { art.style.backgroundImage = Background.FromTexture2D(info.Art); box.Add(art); }
                    else box.Add(Duo.Avatar(card.Ticker ?? "?", card.CoinId, "duo-avatar-s"));
                    var col = El.Div("tl-card-col");
                    var ticker = info?.Ticker ?? card.Ticker ?? "?";
                    col.Add(SocialUi.Text("$" + ticker + (string.IsNullOrEmpty(info?.Name) ? "" : "  " + info.Name), "tl-card-title"));
                    var sub = El.Div("tl-card-sub");
                    bool rugged = info?.Rugged == true, dead = info?.Stage == "dead";
                    if (rugged || dead)
                    {
                        var tag = El.Div("tl-card-tag", SocialUi.Text(TimelineText.T(rugged ? "card.rugged" : "card.dead"), null));
                        tag.EnableInClassList("tl-card-tag--dead", !rugged);
                        sub.Add(tag);
                    }
                    if (info != null)
                    {
                        sub.Add(SocialUi.Text(TimelineText.F("card.mc", CashInput.Format(info.McapCents)), null));
                        var pct = (info.Change5mBps >= 0 ? "+" : "") + (info.Change5mBps / 100.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";
                        sub.Add(SocialUi.Text(pct, info.Change5mBps >= 0 ? "tl-up" : "tl-down"));
                    }
                    col.Add(sub);
                    box.Add(col);
                    box.EnableInClassList("tl-card--rugged", rugged);
                    box.Add(CardButton(card, false));
                    return box;
                }
                if (card.Kind == ActionCard.CardKind.Player && card.UserId != null)
                {
                    var box = El.Div("tl-card tl-card-place");
                    box.Add(new UiIcon("profile"));
                    box.Add(El.Div("tl-card-col", SocialUi.Text(server?.Alias ?? Hub.Names.Name(card.UserId), "tl-card-title")));
                    box.Add(CardButton(card, true));
                    return box;
                }
                if (card.Kind == ActionCard.CardKind.Place && card.VenueId != null)
                {
                    var box = El.Div("tl-card tl-card-place");
                    box.Add(new UiIcon("map"));
                    box.Add(El.Div("tl-card-col", SocialUi.Text(VenueName(card.VenueId), "tl-card-title")));
                    box.Add(CardButton(card, true));
                    return box;
                }
                return null;
            }

            // ------------------------------------------------------------ the Sleuth's exposé (paper card)

            private VisualElement RenderSleuth(FeedPostRow p)
            {
                var m = p.Sleuth;
                var card = El.Div("tl-sleuth");
                card.name = "timeline-sleuth-" + p.PostId;
                card.Add(El.Div("tl-sleuth-mast", SocialUi.Text(TimelineText.T("sleuth.mast").ToUpperInvariant(), null),
                    SocialUi.Text(Ago.Short(p.AtMs, ServerClock.NowMs), null)));
                var body = El.Div("tl-sleuth-body");
                if (m != null) body.Add(SocialUi.Text(SleuthHeadline(m), "tl-sleuth-headline"));
                body.Add(SocialUi.Text(CTLife.Core.NameTable.Apply(p.Text), "tl-sleuth-text"));   // a server template, not player text
                if (m != null)
                {
                    body.Add(El.Div("tl-sleuth-facts",
                        El.Div("tl-sleuth-box", SocialUi.Text(TimelineText.F("sleuth.sold", m.SellSharePct), null)),
                        SocialUi.Text(TimelineText.F("sleuth.heat", m.Heat), "tl-sleuth-heat")));
                    var actions = El.Div("tl-sleuth-actions");
                    var venue = p.SecondaryCard?.VenueId ?? ActionCards.VenueOf(p.DeepLink);
                    if (venue != null)
                    {
                        var go = El.Btn(TimelineText.F("sleuth.go", VenueName(venue)), () => GoTo(venue), "tl-pill");
                        go.name = "timeline-sleuth-go";
                        actions.Add(go);
                    }
                    var open = El.Btn(TimelineText.F("sleuth.open", m.Ticker), () => OpenCoin(m.CoinId), "tl-pill tl-pill--ghost");
                    open.name = "timeline-sleuth-open";
                    actions.Add(open);
                    body.Add(actions);
                }
                card.Add(body);
                return card;
            }

            private static string SleuthHeadline(SleuthMeta m) =>
                ("Dev dumps $" + m.Ticker + " on " + (string.IsNullOrEmpty(m.LauncherAlias) ? "their own holders" : m.LauncherAlias + "'s holders")).ToUpperInvariant();

            private VisualElement RenderPost(FeedPostRow p, System.Collections.Generic.Dictionary<string, FeedPostRow> verdicts)
            {
                var author = p.UserId;
                var mine = SocialWire.SameId(author, Hub.Me);
                var name = string.IsNullOrWhiteSpace(p.Alias) ? Hub.Names.Name(author) : p.Alias;
                if (p.IsNpc) name = CTLife.Core.NameTable.Apply(name);   // server NPCs (callers, the city) carry the approved names
                var head = El.Div("duo-post-head",
                    El.Text(mine ? SocialStrings.F("common.you", name) : name, "duo-post-name"),
                    El.Text("· " + Ago.Short(p.AtMs, ServerClock.NowMs), "duo-post-time"));
                if (p.Kind == EventKinds.Caller || p.IsNpc) head.Insert(1, SocialUi.Text(TimelineText.T("npc"), "tl-npc"));
                var kind = PostKinds.Detect(p.Text);
                if (kind != PostKind.Plain) head.Add(El.Text(SocialStrings.T("feed.kind." + kind), "duo-post-kind" + (kind == PostKind.Giveaway ? " duo-post-kind-warn" : "")));
                var body = El.Div("duo-post-body", head, SocialUi.Text(p.Text, "duo-post-text"));
                var tickers = PostKinds.Tickers(p.Text);
                var action = p.Card != null ? FromServer(p.Card) : ActionCards.For(p, TimelineLinks.CoinIdByTicker);
                var cardView = ActionCardView(action, p.Card);
                var call = p.Meta?["call"] as Newtonsoft.Json.Linq.JObject;
                if (call != null) body.Add(SocialUi.Text(CallLine(call), "tl-call"));
                if (cardView != null)
                {
                    body.Add(cardView);
                    // the Sleuth's verdict attaches under a post about a coin it exposed (brief 3.1)
                    if (p.Verdict != null)
                        body.Add(El.Div("tl-verdict", SocialUi.Text(TimelineText.T("sleuth.checked"), "tl-verdict-head"), SocialUi.Text(p.Verdict.Text, "tl-verdict-text")));
                    else if (p.Card == null && action.CoinId != null && verdicts != null && verdicts.TryGetValue(action.CoinId, out var verdict) && verdict.Sleuth != null)
                    {
                        body.Add(El.Div("tl-verdict", SocialUi.Text(TimelineText.T("sleuth.checked"), "tl-verdict-head"),
                            SocialUi.Text(verdict.Text, "tl-verdict-text")));
                    }
                }
                else if (tickers.Count > 0)
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
        }
    }
}
