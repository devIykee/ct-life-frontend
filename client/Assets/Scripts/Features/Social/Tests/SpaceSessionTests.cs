using System.Collections.Generic;
using NUnit.Framework;

namespace CTLife.SocialFeatures.Tests
{
    public class SpaceSessionTests
    {
        private const string Me = "0192aaaa-0000-7000-8000-000000000001";
        private const string Host = "0192aaaa-0000-7000-8000-0000000000aa";
        private const string Other = "0192aaaa-0000-7000-8000-0000000000bb";
        private const string SpaceId = "space:0192bbbb-0000-7000-8000-000000000001";

        internal static SpaceInfo Snap(string host = Host, IEnumerable<string> speakers = null, IEnumerable<string> hands = null, bool ended = false) =>
            new SpaceInfo { Id = SpaceId, Title = "gm check-in", HostId = host, SpeakerIds = new List<string>(speakers ?? new string[0]),
                HandIds = new List<string>(hands ?? new string[0]), ListenerCount = 3, Ended = ended };

        private static SpaceSession Live(SpaceInfo snap, double now = 1000)
        {
            var s = new SpaceSession(Me);
            s.Open(SpaceId);
            s.ApplySnapshot(snap, s.BeginFetch(), now);
            s.MediaConnecting();
            s.MediaConnected();
            return s;
        }

        [Test]
        public void RolesComeFromTheServerSnapshot()
        {
            Assert.AreEqual(SpaceRole.Host, SpaceSession.Derive(Snap(host: Me), Me));
            Assert.AreEqual(SpaceRole.Speaker, SpaceSession.Derive(Snap(speakers: new[] { Me }), Me));
            Assert.AreEqual(SpaceRole.Requested, SpaceSession.Derive(Snap(hands: new[] { Me }), Me));
            Assert.AreEqual(SpaceRole.Listener, SpaceSession.Derive(Snap(), Me));
            Assert.AreEqual(SpaceRole.Speaker, SpaceSession.Derive(Snap(speakers: new[] { Me.ToUpperInvariant() }), Me), "ids compare case-insensitively");
        }

        [Test]
        public void OpenThenConnectGoesLive()
        {
            var s = new SpaceSession(Me);
            s.Open(SpaceId);
            Assert.AreEqual(SpacePhase.Opening, s.Phase);
            s.MediaConnecting();
            Assert.AreEqual(SpacePhase.Connecting, s.Phase);
            s.ApplySnapshot(Snap(), s.BeginFetch(), 0);
            s.MediaConnected();
            Assert.AreEqual(SpacePhase.Live, s.Phase);
            Assert.IsTrue(s.InSpace);
            Assert.AreEqual(SpaceRole.Listener, s.Role);
        }

        [Test]
        public void SpaceUpdateMakesTheRosterStaleUntilANewerFetch()
        {
            var s = Live(Snap(), 1000);
            Assert.IsTrue(s.Fresh(1500));
            Assert.IsTrue(s.CanRequest(1500));
            Assert.IsTrue(s.Invalidate(SpaceId));
            Assert.IsFalse(s.Fresh(1500));
            Assert.IsFalse(s.CanRequest(1500), "no role action on a stale roster");
            Assert.IsTrue(s.NeedsFetch(1500));
            s.ApplySnapshot(Snap(), s.BeginFetch(), 1600);
            Assert.IsTrue(s.Fresh(1700));
            Assert.IsFalse(s.NeedsFetch(1700));
        }

        [Test]
        public void UpdatesForOtherSpacesAreIgnored()
        {
            var s = Live(Snap());
            Assert.IsFalse(s.Invalidate("space:another"));
            Assert.IsTrue(s.Fresh(2000));
        }

        [Test]
        public void ALateOlderFetchDoesNotOverwriteANewerOne()
        {
            var s = Live(Snap(), 0);
            var old = s.BeginFetch();
            s.Invalidate(SpaceId);
            var fresh = s.BeginFetch();
            s.ApplySnapshot(Snap(speakers: new[] { Me }), fresh, 100);
            Assert.AreEqual(SpaceRole.Speaker, s.Role);
            s.ApplySnapshot(Snap(), old, 200);
            Assert.AreEqual(SpaceRole.Speaker, s.Role, "the older response arrived late and is ignored");
        }

        [Test]
        public void PollsEveryFiveSecondsWithoutRealtime()
        {
            var s = Live(Snap(), 0);
            Assert.IsFalse(s.NeedsFetch(4999));
            Assert.IsTrue(s.NeedsFetch(SpaceSession.PollMs));
            Assert.IsFalse(s.Fresh(SpaceSession.StaleAfterMs), "after 12 s without a fetch the roster is stale");
        }

        [Test]
        public void DemotionStopsTheMicrophone()
        {
            var s = Live(Snap(speakers: new[] { Me }), 0);
            Assert.AreEqual(SpaceRole.Speaker, s.Role);
            var change = s.ApplySnapshot(Snap(), s.BeginFetch(), 10);
            Assert.IsTrue(change.MicMustStop);
            Assert.AreEqual(SpaceRole.Listener, change.After);
        }

        [Test]
        public void PromotionIsReportedButNeverUnmutes()
        {
            var s = Live(Snap(hands: new[] { Me }), 0);
            Assert.AreEqual(SpaceRole.Requested, s.Role);
            var change = s.ApplySnapshot(Snap(speakers: new[] { Me }), s.BeginFetch(), 10);
            Assert.IsTrue(change.Promoted);
            Assert.IsFalse(change.MicMustStop);
            Assert.IsTrue(s.CanPublish(20), "may unmute; the controller still waits for the player's tap");
        }

        [Test]
        public void EndedSnapshotOrNotFoundEndsTheSession()
        {
            var s = Live(Snap(speakers: new[] { Me }), 0);
            var change = s.ApplySnapshot(Snap(ended: true), s.BeginFetch(), 10);
            Assert.IsTrue(change.Ended);
            Assert.IsTrue(change.MicMustStop);
            Assert.AreEqual(SpacePhase.Ended, s.Phase);
            Assert.IsFalse(s.InSpace);
            var t = Live(Snap(), 0);
            t.ApplyNotFound();
            Assert.AreEqual(SpacePhase.Ended, t.Phase);
        }

        [Test]
        public void MediaDropReconnectsWithBackoffThenGivesUp()
        {
            var s = Live(Snap(), 0);
            s.MediaLost(1000);
            Assert.AreEqual(SpacePhase.Reconnecting, s.Phase);
            Assert.IsFalse(s.ReconnectDue(1500));
            Assert.IsTrue(s.ReconnectDue(2000), "first retry after 1 s");
            s.ReconnectStarted();
            Assert.IsFalse(s.ReconnectDue(2000), "one attempt at a time");
            s.ReconnectFailed(2100);
            Assert.IsFalse(s.ReconnectDue(3000));
            Assert.IsTrue(s.ReconnectDue(4100), "then 2 s");
            for (int i = 0; i < 10 && s.Phase == SpacePhase.Reconnecting; i++) { s.ReconnectStarted(); s.ReconnectFailed(10_000 * (i + 1)); }
            Assert.AreEqual(SpacePhase.Failed, s.Phase);
            Assert.AreEqual("spaces.connectionLost", s.ErrorKey);
            Assert.IsTrue(s.CanRejoin);
        }

        [Test]
        public void NetworkChangeRetriesImmediatelyAndRefetches()
        {
            var s = Live(Snap(), 0);
            s.MediaLost(1000);
            s.ReconnectStarted(); s.ReconnectFailed(2000); // next due at 4000
            Assert.IsFalse(s.ReconnectDue(2500));
            s.NetworkChanged(2500);
            Assert.IsTrue(s.ReconnectDue(2500));
            Assert.IsTrue(s.NeedsFetch(2500));
            s.MediaConnected();
            Assert.AreEqual(SpacePhase.Live, s.Phase);
            Assert.AreEqual(0, s.Reconnect.Attempts);
        }

        [Test]
        public void NetworkChangeRevivesAFailedConnection()
        {
            var s = Live(Snap(), 0);
            s.MediaLost(0);
            for (int i = 0; i < 10 && s.Phase == SpacePhase.Reconnecting; i++) { s.ReconnectStarted(); s.ReconnectFailed(100_000 * (i + 1)); }
            Assert.AreEqual(SpacePhase.Failed, s.Phase);
            s.NetworkChanged(2_000_000);
            Assert.AreEqual(SpacePhase.Reconnecting, s.Phase);
            Assert.IsTrue(s.ReconnectDue(2_000_000));
        }

        [Test]
        public void RequestAndWithdrawFollowTheRole()
        {
            var s = Live(Snap(), 0);
            Assert.IsTrue(s.CanRequest(10));
            Assert.IsFalse(s.CanWithdraw(10));
            Assert.IsTrue(s.Begin(SpaceOp.Request));
            Assert.IsFalse(s.CanRequest(10), "one op at a time");
            Assert.IsFalse(s.Begin(SpaceOp.Withdraw));
            s.Finish();
            Assert.IsFalse(s.Fresh(10), "finishing an op forces a refetch");
            s.ApplySnapshot(Snap(hands: new[] { Me }), s.BeginFetch(), 20);
            Assert.IsTrue(s.CanWithdraw(30));
            Assert.IsFalse(s.CanRequest(30));
        }

        [Test]
        public void HostActionsNeedHostRoleAndARequest()
        {
            var s = Live(Snap(host: Me, speakers: new[] { Other }, hands: new[] { Host }), 0);
            Assert.IsTrue(s.CanPromote(Host, 10));
            Assert.IsFalse(s.CanPromote(Other, 10), "already on stage");
            Assert.IsTrue(s.CanDemote(Other, 10));
            Assert.IsFalse(s.CanDemote(Host, 10));
            Assert.IsTrue(s.CanEnd(10));
            var listener = Live(Snap(speakers: new[] { Other }, hands: new[] { Me }), 0);
            Assert.IsFalse(listener.CanPromote(Me, 10));
            Assert.IsFalse(listener.CanDemote(Other, 10));
            Assert.IsFalse(listener.CanEnd(10));
        }

        [Test]
        public void StageCapStopsPromotion()
        {
            var speakers = new List<string>();
            for (int i = 0; i < SpaceSession.SpeakerLimit; i++) speakers.Add("0192aaaa-0000-7000-8000-0000000001" + i.ToString("00"));
            var s = Live(Snap(host: Me, speakers: speakers, hands: new[] { Other }), 0);
            Assert.IsTrue(s.StageFull);
            Assert.IsFalse(s.CanPromote(Other, 10));
        }

        [Test]
        public void LeaveAndRejoin()
        {
            var s = Live(Snap(speakers: new[] { Me }), 0);
            s.Leave();
            Assert.AreEqual(SpacePhase.Left, s.Phase);
            Assert.IsFalse(s.InSpace);
            Assert.IsTrue(s.CanRejoin);
            s.MediaLost(10);
            Assert.AreEqual(SpacePhase.Left, s.Phase, "leaving is not a drop: no reconnect");
            s.Open(SpaceId);
            Assert.AreEqual(SpacePhase.Opening, s.Phase);
            Assert.IsNotNull(s.Space, "the same Space keeps its last roster while reopening");
        }

        [Test]
        public void MicNeedsLiveStageAndFreshness()
        {
            var s = Live(Snap(speakers: new[] { Me }), 0);
            Assert.IsTrue(s.CanPublish(100));
            s.MediaLost(200);
            Assert.IsFalse(s.CanPublish(200), "not while reconnecting");
            var t = Live(Snap(), 0);
            Assert.IsFalse(t.CanPublish(100), "listeners never publish");
        }
    }
}
