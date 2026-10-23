using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Colyseus;
using Colyseus.Schema;
using CTLife.Net;
using CTLife.World;
using NUnit.Framework;

namespace CTLife.Tests
{
    public class VenueReconnectTests
    {
        [Test]
        public async Task SameZoneLossStartsOneJoinAndBacksOffFailures()
        {
            double now = 0;
            using var retry = new VenueRoomRecovery(() => now);
            retry.SetTarget("timeline_square");
            var pending = new TaskCompletionSource<bool>();
            int joins = 0, failures = 0;
            Task Join(CancellationToken token) { joins++; return pending.Task; }
            var first = retry.TryRun(CancellationToken.None, Join, _ => failures++);
            for (int frame = 0; frame < 100; frame++) await retry.TryRun(CancellationToken.None, Join);
            Assert.AreEqual(1, joins, "a disconnected frame must not start another join");
            pending.SetException(new InvalidOperationException("local fixture failure"));
            await first;
            Assert.AreEqual(1, failures);
            now = 1.99;
            await retry.TryRun(CancellationToken.None, Join);
            Assert.AreEqual(1, joins);
            now = 2;
            await retry.TryRun(CancellationToken.None, _ => { joins++; return Task.CompletedTask; });
            Assert.AreEqual(2, joins);
            Assert.IsFalse(retry.Busy);
        }

        [Test]
        public async Task CrossingZoneCancelsOldJoinAndWaitsForItsLateCleanup()
        {
            using var retry = new VenueRoomRecovery(() => 0);
            retry.SetTarget("timeline_square");
            var rawJoin = new TaskCompletionSource<bool>();
            var capturedChannel = new TestChannel();
            var publishedSuccessor = new TestChannel();
            publishedSuccessor.Accept(new TestRoom());
            CancellationToken captured = default;
            int retired = 0, attached = 0, successorJoins = 0;
            var old = retry.TryRun(CancellationToken.None, async token =>
            {
                captured = token;
                await rawJoin.Task; // Simulate the SDK's non-cancellable raw join.
                capturedChannel.Accept(new TestRoom());
                if (token.IsCancellationRequested)
                {
                    await capturedChannel.Leave();
                    retired++; token.ThrowIfCancellationRequested();
                }
                attached++;
            });
            retry.SetTarget("spaces_studio");
            Assert.IsTrue(captured.IsCancellationRequested);
            await retry.TryRun(CancellationToken.None, _ => { successorJoins++; return Task.CompletedTask; });
            Assert.AreEqual(0, successorJoins, "retire the captured late result before another attempt");
            rawJoin.SetResult(true); await old;
            Assert.AreEqual(1, retired); Assert.AreEqual(0, attached);
            Assert.IsFalse(capturedChannel.IsJoined);
            Assert.IsTrue(publishedSuccessor.IsJoined, "late cleanup must retire only its captured room");
            await retry.TryRun(CancellationToken.None, _ => { successorJoins++; return Task.CompletedTask; });
            Assert.AreEqual(1, successorJoins);
            await publishedSuccessor.Leave();
        }

        [Test]
        public async Task VisitCancellationStopsPendingRecoveryAndFutureRetries()
        {
            using var owner = new CancellationTokenSource();
            using var retry = new VenueRoomRecovery(() => 0);
            retry.SetTarget("timeline_square");
            var ready = new TaskCompletionSource<bool>();
            int joins = 0;
            var task = retry.TryRun(owner.Token, async token =>
            {
                joins++; ready.SetResult(true);
                await Task.Delay(Timeout.Infinite, token);
            });
            await ready.Task; owner.Cancel(); await task;
            await retry.TryRun(owner.Token, _ => { joins++; return Task.CompletedTask; });
            Assert.AreEqual(1, joins); Assert.IsFalse(retry.Busy);
        }

        [Test]
        public async Task SuccessfulRecoveryResetsBackoffForTheNextLoss()
        {
            double now = 0;
            using var retry = new VenueRoomRecovery(() => now);
            retry.SetTarget("timeline_square");
            await retry.TryRun(CancellationToken.None, _ => Task.FromException(new Exception("fixture")));
            now = 2;
            await retry.TryRun(CancellationToken.None, _ => Task.CompletedTask);
            retry.SetTarget(null); // Joined channel is healthy.
            retry.SetTarget("timeline_square"); // Another terminal loss in the same zone.
            int joins = 0;
            await retry.TryRun(CancellationToken.None, _ => { joins++; return Task.CompletedTask; });
            Assert.AreEqual(1, joins);
        }

        private sealed class TestRoom : Room<DynamicSchema>
        {
            public TestRoom() : base("venue")
            {
                RoomId = "local-fixture"; SessionId = "local-fixture-session";
                JoinedAtTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 10000;
                typeof(Room<DynamicSchema>).GetField("Serializer", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(this, new SchemaSerializer<DynamicSchema>());
                SetConnection(new Connection("ws://127.0.0.1:9", null)); // No network connection is opened.
            }
            public void EmitLateReconnect() => ((Delegate)typeof(Room<DynamicSchema>)
                .GetField("OnReconnect", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(this)).DynamicInvoke();
        }
        private sealed class TestChannel : RoomChannel<DynamicSchema>
        {
            public TestChannel() : base("venue") { }
            public void Accept(TestRoom room) => typeof(RoomChannel<DynamicSchema>)
                .GetMethod("BindRoom", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(this, new object[] { room });
        }

        [Test]
        public void ActualSdkTerminalCloseMarksChannelUnjoinedForSameZoneRetry()
        {
            var channel = new TestChannel(); var room = new TestRoom(); channel.Accept(room);
            int lostCode = 0; channel.LostUnexpectedly += code => lostCode = code;
            room.Connection.Dispatch(ConnectionEvent.Closed(4003));
            Assert.IsFalse(channel.IsJoined);
            Assert.AreEqual(4003, lostCode);
        }

        [Test]
        public async Task RetiringADroppedSdkRoomDisablesRetriesAndDiscardsQueuedActions()
        {
            var channel = new TestChannel(); var room = new TestRoom(); channel.Accept(room);
            room.Reconnection.IsReconnecting = true;
            room.Reconnection.EnqueuedMessages.Add(new byte[] { 13, 1 });
            await channel.Leave();
            Assert.IsFalse(channel.IsJoined);
            Assert.IsFalse(room.Reconnection.Enabled);
            Assert.AreEqual(0, room.Reconnection.MaxRetries);
            Assert.IsEmpty(room.Reconnection.EnqueuedMessages);
            await channel.Leave(); // Idempotent retirement must not touch any successor.
        }

        [Test]
        public void OldTerminalCallbackDoesNotClearReplacementChannel()
        {
            var channel = new TestChannel(); var old = new TestRoom(); channel.Accept(old);
            var next = new TestRoom(); channel.Accept(next);
            old.Connection.Dispatch(ConnectionEvent.Closed(4003));
            Assert.IsTrue(channel.IsJoined);
            next.Connection.Dispatch(ConnectionEvent.Closed(4003));
            Assert.IsFalse(channel.IsJoined);
        }

        [Test]
        public async Task AlreadyScheduledReconnectRetiresItsCapturedRoomAndPreservesSuccessor()
        {
            var channel = new TestChannel(); var old = new TestRoom(); channel.Accept(old);
            await channel.Leave(); // Retired during the SDK's pending retry delay.
            var next = new TestRoom(); channel.Accept(next);
            int leaveMessages = 0;
            old.Connection.Transmit = data =>
            {
                CollectionAssert.AreEqual(new byte[] { Protocol.LEAVE_ROOM }, data);
                leaveMessages++;
                old.Connection.Dispatch(ConnectionEvent.Closed(4000));
                return Task.CompletedTask;
            };
            old.Connection.Dispatch(ConnectionEvent.Opened()); // The sleeping retry opened a new transport.
            old.EmitLateReconnect();
            Assert.AreEqual(1, leaveMessages);
            Assert.IsFalse(old.Connection.IsOpen);
            Assert.IsTrue(channel.IsJoined, "a stale reconnect must not retire the current room");
            await channel.Leave();
        }
    }
}
