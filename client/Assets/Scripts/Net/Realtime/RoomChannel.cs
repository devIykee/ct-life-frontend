using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Colyseus;
using CTLife.Core;
using CTLife.Realtime;

namespace CTLife.Net
{
    /// <summary>
    /// One joined Colyseus room (Colyseus Unity SDK 0.18.8). Handles `welcome` (clock offset + instanceId) and
    /// `rejected {type, code}` for every room (contract section 5), and keeps typed handler registration in one place.
    /// </summary>
    public abstract class RoomChannel<TState> where TState : Colyseus.Schema.Schema
    {
        protected Room<TState> Room { get; private set; }
        private long _joinRevision;
        private readonly List<Action> _unsubscribe = new List<Action>();

        public string RoomName { get; }
        public string InstanceId { get; private set; }
        public string SessionId => Room?.SessionId;
        public TState State => Room != null ? Room.State : null;
        public bool IsJoined => Room != null;
        public bool WelcomeReceived { get; private set; }

        public event Action<RejectedMessage> Rejected;
        public event Action<int> Left;
        /// <summary>The room ended without our own Leave(): server shutdown, kick, or a reconnect that failed.</summary>
        public event Action<int> LostUnexpectedly;
        private bool _leaving;
        public event Action Dropped;
        public event Action Reconnected;

        protected RoomChannel(string roomName)
        {
            RoomName = roomName;
        }

        internal async Task Join(Client client, Dictionary<string, object> options, RoomJoinAttempt owner)
        {
            owner.ThrowIfObsolete();
            long revision = ++_joinRevision;
            _leaving = false;
            var joined = await client.JoinOrCreate<TState>(RoomName, options);
            // The installed SDK has no CancellationToken join overload. Drain the exact raw result before
            // assigning Room or registering welcome/state/message handlers when its owner is obsolete.
            try
            {
                owner.ThrowIfObsolete();
                if (revision != _joinRevision) throw new OperationCanceledException("Channel join was superseded.", owner.Token);
            }
            catch
            {
                try { await joined.Leave(); }
                catch (Exception e) { Log.Warn("rt", $"Discarded {RoomName} join leave: {e.Message}"); }
                throw;
            }
            BindRoom(joined);
        }

        private void BindRoom(Room<TState> joined)
        {
            Room = joined;
            InstanceId = Room.RoomId;
            // Register synchronously right after the join completes so `welcome` is not missed.
            On<WelcomeMessage>("welcome", OnWelcome);
            On<RejectedMessage>("rejected", r =>
            {
                Log.Warn("rt", $"{RoomName} rejected {r.type}: {r.code}");
                Rejected?.Invoke(r);
            });
            var room = Room;
            Room.OnLeave += code =>
            {
                bool expected = _leaving || !ReferenceEquals(Room, room);
                Log.Info("rt", $"{RoomName} left ({code}){(expected ? "" : " unexpectedly")}");
                if (!expected)
                {
                    // The SDK gave up (or the server closed the room). Mark this channel unjoined so the owner
                    // can join a fresh room; the dead handle's listeners are released now.
                    _joinRevision++;
                    ReleaseListeners();
                    Room = null;
                }
                Left?.Invoke(code);
                if (!expected) LostUnexpectedly?.Invoke(code);
            };
            Room.OnDrop += code =>
            {
                if (_leaving || !ReferenceEquals(Room, room)) return;
                Log.Warn("rt", $"{RoomName} dropped ({code}); SDK will try to reconnect");
                Dropped?.Invoke();
            };
            Room.OnReconnect += () =>
            {
                if (_leaving || !ReferenceEquals(Room, room))
                {
                    // SDK retries already sleeping at Leave can still connect. Close that captured
                    // room if it returns late, without selecting or touching the successor handle.
                    _ = RetireRoom(room);
                    return;
                }
                Log.Info("rt", $"{RoomName} reconnected");
                Reconnected?.Invoke();
            };
            Room.OnError += (code, message) =>
            {
                if (!_leaving && ReferenceEquals(Room, room)) Log.Warn("rt", $"{RoomName} error {code}: {message}");
            };
            OnJoined();
            Log.Info("rt", $"Joined {RoomName} room {Room.RoomId} as {Room.SessionId}");
        }

        private void OnWelcome(WelcomeMessage w)
        {
            WelcomeReceived = true;
            ServerClock.OnWelcome(w.serverNow);
            if (!string.IsNullOrEmpty(w.instanceId)) InstanceId = w.instanceId;
            OnWelcomeReceived(w);
        }

        protected virtual void OnJoined() { }
        protected virtual void OnWelcomeReceived(WelcomeMessage w) { }

        protected void On<T>(string type, Action<T> handler)
        {
            _unsubscribe.Add(Room.OnMessage<T>(type, m =>
            {
                try
                {
                    handler(m);
                }
                catch (Exception e)
                {
                    Log.Exception("rt", e);
                }
            }));
        }

        protected void Track(Action unsubscribe)
        {
            if (unsubscribe != null) _unsubscribe.Add(unsubscribe);
        }

        protected Action TrackAndReturn(Action unsubscribe)
        {
            Track(unsubscribe);
            return unsubscribe;
        }

        public Task Send(string type, object payload)
        {
            if (Room == null) return Task.CompletedTask;
            return Room.Send(type, payload);
        }

        private void ReleaseListeners()
        {
            foreach (var u in _unsubscribe)
            {
                try { u(); } catch { /* ignore */ }
            }
            _unsubscribe.Clear();
        }

        public async Task Leave()
        {
            _leaving = true;
            _joinRevision++;
            ReleaseListeners();
            var r = Room;
            Room = null;
            if (r != null) await RetireRoom(r);
        }

        private async Task RetireRoom(Room<TState> r)
        {
            // Leaving a dropped 0.18.8 room otherwise returns without changing its retry policy.
            // Delays already scheduled inside the SDK can still run; they must not start another retry cycle.
            r.Reconnection.Enabled = false;
            r.Reconnection.MaxRetries = 0;
            r.Reconnection.EnqueuedMessages.Clear();
            try
            {
                if (!r.Connection.IsOpen) r.Connection.CancelConnection();
                await r.Leave();
            }
            catch (Exception e)
            {
                Log.Warn("rt", $"Leave {RoomName}: {e.Message}");
            }
        }
    }
}
