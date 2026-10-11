using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CTLife.SocialFeatures.Tests
{
    public class DmThreadTests
    {
        private const string Me = "0192aaaa-0000-7000-8000-000000000001";
        private const string Peer = "0192aaaa-0000-7000-8000-0000000000pp";
        private const string Stranger = "0192aaaa-0000-7000-8000-0000000000ss";
        private const long T0 = 1_800_000_000_000;

        private static DmRow M(string id, string from, long at, string text = "gm") =>
            new DmRow { MessageId = id, FromUserId = from, ToUserId = from == Me ? Peer : Me, Text = text, AtMs = at };

        [Test]
        public void PagesMergeInTimeOrderWithoutDuplicates()
        {
            var t = new DmThread(Me, Peer);
            // Backend order: newest first.
            Assert.AreEqual(3, t.ApplyPage(new[] { M("m5", Peer, T0 + 5), M("m4", Me, T0 + 4), M("m3", Peer, T0 + 3) }, false));
            Assert.AreEqual(T0 + 3, SocialWire.TimeMs(Newtonsoft.Json.Linq.JToken.FromObject(t.OlderCursor)));
            // Contract order (newest last), overlapping by one.
            Assert.AreEqual(2, t.ApplyPage(new[] { M("m1", Peer, T0 + 1), M("m2", Me, T0 + 2), M("m3", Peer, T0 + 3) }, true));
            CollectionAssert.AreEqual(new[] { "m1", "m2", "m3", "m4", "m5" }, t.Messages.Select(m => m.MessageId).ToArray());
            Assert.IsTrue(t.HasOlder);
            Assert.AreEqual(0, t.ApplyPage(new[] { M("m1", Peer, T0 + 1) }, true), "an older page with nothing new ends paging");
            Assert.IsFalse(t.HasOlder);
        }

        [Test]
        public void EmptyFirstPageMeansNoHistory()
        {
            var t = new DmThread(Me, Peer);
            t.ApplyPage(new DmRow[0], false);
            Assert.IsTrue(t.Loaded);
            Assert.IsFalse(t.HasOlder);
            Assert.IsNull(t.OlderCursor);
        }

        [Test]
        public void CursorIsTheOldestMessageTimeInIso()
        {
            var t = new DmThread(Me, Peer);
            t.ApplyPage(new[] { M("m2", Peer, 1_791_000_000_123) }, false);
            Assert.AreEqual("2026-10-03T04:00:00.123Z", t.OlderCursor);
        }

        [Test]
        public void RealtimeDeliveryIsDeduplicated()
        {
            var t = new DmThread(Me, Peer);
            t.ApplyPage(new[] { M("m1", Peer, T0) }, false);
            Assert.IsTrue(t.ApplyRealtime("m2", Peer, "you up?", T0 + 10));
            Assert.IsFalse(t.ApplyRealtime("m2", Peer, "you up?", T0 + 10), "reconnect replay");
            Assert.IsFalse(t.ApplyRealtime("m3", Stranger, "wrong thread", T0 + 20));
            Assert.AreEqual(2, t.Messages.Count);
        }

        [Test]
        public void MessagesFromOtherConversationsAreRejected()
        {
            var t = new DmThread(Me, Peer);
            t.ApplyPage(new[] { new DmRow { MessageId = "x", FromUserId = Stranger, ToUserId = Me, Text = "hi", AtMs = T0 } }, false);
            Assert.AreEqual(0, t.Messages.Count);
        }

        [Test]
        public void LocalEchoIsReplacedByTheServerCopy()
        {
            var t = new DmThread(Me, Peer);
            t.ApplyPage(new[] { M("m1", Peer, T0) }, false);
            var local = t.AddLocal("wen call", "k1", T0 + 100);
            Assert.AreEqual(DmSendState.Sending, local.State);
            Assert.AreEqual("local:k1", t.Messages.Last().MessageId);
            t.Confirm("k1", new DmRow { MessageId = "m2", FromUserId = Me, Text = "wen call", AtMs = T0 + 120 });
            CollectionAssert.AreEqual(new[] { "m1", "m2" }, t.Messages.Select(m => m.MessageId).ToArray());
            Assert.AreEqual(Peer, t.Messages.Last().ToUserId, "the server copy is filed under this peer");
        }

        [Test]
        public void ServerCopyArrivingFirstAbsorbsTheEcho()
        {
            var t = new DmThread(Me, Peer);
            t.AddLocal("gm", "k1", T0);
            t.ApplyPage(new[] { M("m9", Me, T0 + 50, "gm") }, false);
            Assert.AreEqual(1, t.Messages.Count);
            t.Confirm("k1", M("m9", Me, T0 + 50, "gm"));
            Assert.AreEqual(1, t.Messages.Count, "no duplicate after the late confirm");
        }

        [Test]
        public void PendingEchoesStayAtTheBottom()
        {
            var t = new DmThread(Me, Peer);
            t.AddLocal("first", "k1", T0 + 1000);
            t.ApplyRealtime("m1", Peer, "later", T0 + 5000);
            Assert.AreEqual("local:k1", t.Messages.Last().MessageId);
        }

        [Test]
        public void FailedSendKeepsTheRetryMode()
        {
            var t = new DmThread(Me, Peer);
            t.AddLocal("gm", "k1", T0);
            t.Fail("k1", "err.network", true);
            Assert.AreEqual(DmSendState.Failed, t.Local("k1").State);
            Assert.IsTrue(t.Local("k1").RetrySameKey);
            t.AddLocal("gm2", "k2", T0);
            t.Fail("k2", "dm.blockedByThem", false);
            Assert.IsFalse(t.Local("k2").RetrySameKey);
            t.Remove("k2");
            Assert.IsNull(t.Local("k2"));
        }

        [TestCase("", "dm.empty")]
        [TestCase("   ", "dm.empty")]
        [TestCase("gm", null)]
        public void TextValidation(string input, string error) => Assert.AreEqual(error, DmThread.Validate(input, out _));

        [Test]
        public void FiveHundredCharactersMax()
        {
            Assert.IsNull(DmThread.Validate(new string('a', 500), out _));
            Assert.AreEqual("dm.tooLong", DmThread.Validate(new string('a', 501), out _));
        }

        [Test]
        public void RequestUntilIReply()
        {
            var t = new DmThread(Me, Peer);
            t.ApplyPage(new[] { M("m1", Peer, T0) }, false);
            Assert.IsTrue(t.IsRequestFrom(Peer));
            t.ApplyPage(new[] { M("m2", Me, T0 + 1) }, false);
            Assert.IsFalse(t.IsRequestFrom(Peer));
        }
    }

    public class DmInboxTests
    {
        [Test]
        public void IncomingCountsUnreadUnlessThatThreadIsOpen()
        {
            var inbox = new DmInbox();
            inbox.ApplyIncoming("a", "gm", 10);
            inbox.ApplyIncoming("a", "you up", 20);
            Assert.AreEqual(2, inbox.Get("a").Unread);
            Assert.AreEqual("you up", inbox.Get("a").LastMessage);
            inbox.OpenPeer = "a";
            inbox.ApplyIncoming("a", "hello?", 30);
            Assert.AreEqual(2, inbox.Get("a").Unread);
            inbox.MarkRead("a");
            Assert.AreEqual(0, inbox.UnreadTotal(null));
        }

        [Test]
        public void OlderThreadRowsDoNotOverwriteNewerPreview()
        {
            var inbox = new DmInbox();
            inbox.ApplyIncoming("a", "new", 50);
            inbox.ApplyThreads(new[] { new DmThreadRow { UserId = "a", Alias = "Froggy", LastMessage = "old", LastAtMs = 10 } });
            Assert.AreEqual("new", inbox.Get("a").LastMessage);
            Assert.AreEqual("Froggy", inbox.Get("a").Alias);
        }

        [Test]
        public void SortedNewestFirst()
        {
            var inbox = new DmInbox();
            inbox.ApplyIncoming("a", "1", 10);
            inbox.ApplyIncoming("b", "2", 30);
            inbox.ApplyOutgoing("c", "3", 20);
            CollectionAssert.AreEqual(new[] { "b", "c", "a" }, inbox.All.Select(e => e.UserId).ToArray());
        }

        [Test]
        public void RequestsAreStrangersIHaveNotAnswered()
        {
            var inbox = new DmInbox();
            var e = inbox.ApplyIncoming("stranger", "drop your wallet", 10);
            Assert.IsTrue(inbox.IsRequest(e, _ => false));
            Assert.IsFalse(inbox.IsRequest(e, id => id == "stranger"), "people I follow are never requests");
            e.Accepted = true;
            Assert.IsFalse(inbox.IsRequest(e, _ => false));
            var f = inbox.ApplyIncoming("other", "hi", 11);
            inbox.ApplyOutgoing("other", "hey", 12);
            Assert.IsFalse(inbox.IsRequest(f, _ => false), "replying accepts");
        }

        [Test]
        public void BlockedUsersAreHiddenFromUnread()
        {
            var inbox = new DmInbox();
            inbox.ApplyIncoming("a", "x", 1);
            inbox.ApplyIncoming("b", "y", 2);
            Assert.AreEqual(1, inbox.UnreadTotal(id => id == "b"));
            inbox.Remove("a");
            Assert.IsNull(inbox.Get("a"));
        }
    }

    public class FeedTests
    {
        private static FeedPostRow P(string id, string user, long at, string text = "gm") => new FeedPostRow { PostId = id, UserId = user, AtMs = at, Text = text };

        [Test]
        public void NewestFirstAndDeduplicated()
        {
            var f = new FeedTimeline("global");
            f.Replace(new[] { P("p1", "u1", 10), P("p3", "u2", 30), P("p2", "u1", 20), P("p3", "u2", 30) });
            CollectionAssert.AreEqual(new[] { "p3", "p2", "p1" }, f.Visible(null).Select(p => p.PostId).ToArray());
            Assert.IsFalse(f.Add(P("p2", "u1", 20)));
            Assert.IsTrue(f.Add(P("p4", "me", 40)));
            Assert.AreEqual("p4", f.Visible(null)[0].PostId);
        }

        [Test]
        public void BlockedAuthorsAreHidden()
        {
            var f = new FeedTimeline("following");
            Assert.AreEqual("following", f.Scope);
            f.Replace(new[] { P("p1", "good", 10), P("p2", "blocked", 20) });
            CollectionAssert.AreEqual(new[] { "p1" }, f.Visible(id => id == "blocked").Select(p => p.PostId).ToArray());
            Assert.AreEqual("global", new FeedTimeline("weird").Scope);
        }

        [Test]
        public void ReplaceCountsOnlyNewPosts()
        {
            var f = new FeedTimeline("global");
            Assert.AreEqual(2, f.Replace(new[] { P("p1", "u", 1), P("p2", "u", 2) }));
            Assert.AreEqual(1, f.Replace(new[] { P("p1", "u", 1), P("p2", "u", 2), P("p3", "u", 3) }));
        }

        [Test]
        public void TwoHundredEightyCharacters()
        {
            Assert.AreEqual("feed.empty", FeedTimeline.Validate("  ", out _));
            Assert.IsNull(FeedTimeline.Validate(new string('x', 280), out _));
            Assert.AreEqual("feed.tooLong", FeedTimeline.Validate(new string('x', 281), out _));
            Assert.AreEqual(278, FeedTimeline.Remaining("  gm  "));
        }

        [TestCase("JUST IN: $120M of longs liquidated in the Perp Pit in the past 10 minutes", PostKind.News)]
        [TestCase("BREAKING: Vitalek says the launchpad turned retail into gamblers", PostKind.News)]
        [TestCase("68% chance the Clarity Act passes this year", PostKind.News)]
        [TestCase("🧵 1/ why $FROGE's chart is the only honest thing in this city", PostKind.Thread)]
        [TestCase("$FROGE is sending. entry: here. target: moon. NFA", PostKind.Call)]
        [TestCase("PnL: +$420 on $FROGE", PostKind.Pnl)]
        [TestCase("+$40,000 → liquidated. the entire life of a trader", PostKind.Rekt)]
        [TestCase("this guy turned $1K into $15M this year", PostKind.Tracker)]
        [TestCase("18 year old retires off a single memecoin trade", PostKind.Tracker)]
        [TestCase("Day 605: 95% of FOMO traders lose money", PostKind.Journal)]
        [TestCase("gm frens", PostKind.Gm)]
        [TestCase("Giving away 10 SOL. 1. Like RT 2. Drop your wallets 3. Join my TG", PostKind.Giveaway)]
        [TestCase("Like and reply hi, surprise in DMs", PostKind.Giveaway)]
        [TestCase("building a game in the city today", PostKind.Plain)]
        public void PostKindsMatchCtFormats(string text, PostKind kind) => Assert.AreEqual(kind, PostKinds.Detect(text));

        [Test]
        public void EveryStarterIsRecognisedAsItsOwnFormat()
        {
            var starters = new Dictionary<PostKind, string>
            {
                [PostKind.News] = "JUST IN: $120M of longs liquidated in the Perp Pit in the past 10 minutes",
                [PostKind.Thread] = "🧵 1/ why $FROGE's chart is the only honest thing in this city",
                [PostKind.Call] = "$FROGE is sending. entry: here. target: moon. stop: my dignity. NFA",
                [PostKind.Pnl] = "PnL: +$420 on $FROGE. screenshot or it didn't happen (it's on my phone)",
                [PostKind.Rekt] = "+$4,200 → liquidated. the entire life of a trader in one Perp Pit session",
                [PostKind.Tracker] = "this guy turned $1K into $15K on $FROGE this week. if you're not following him…",
                [PostKind.Journal] = "Day 1: bought high, sold low, learned nothing. cooked.",
                [PostKind.Gm] = "gm. coffee, charts, no leverage today (lie)",
            };
            foreach (var k in PostKinds.Starters) Assert.AreEqual(k, PostKinds.Detect(starters[k]), k.ToString());
        }

        [Test]
        public void TickersAreExtracted()
        {
            CollectionAssert.AreEqual(new[] { "$FROGE", "$WIFHAT" }, PostKinds.Tickers("$FROGE vs $WIFHAT vs $FROGE, not $5 or US$").ToArray());
        }

        [TestCase("send me your seed phrase to verify", ScamSignals.Flag.Secret)]
        [TestCase("Admin here, please verify your wallet", ScamSignals.Flag.Impersonation)]
        [TestCase("paste this into your console to claim", ScamSignals.Flag.Drainer)]
        [TestCase("Drop your wallet below for the airdrop", ScamSignals.Flag.Giveaway)]
        [TestCase("GUARANTEED 100X, huge cabal launch", ScamSignals.Flag.Guarantee)]
        [TestCase("send USDC first and I'll send the alpha", ScamSignals.Flag.SendFunds)]
        [TestCase("check https://example.xyz", ScamSignals.Flag.Link)]
        [TestCase("gm, see you at the Perp Pit", ScamSignals.Flag.None)]
        public void ScamSignalsFlagKnownPatterns(string text, ScamSignals.Flag flag) => Assert.AreEqual(flag, ScamSignals.Detect(text));
    }
}
