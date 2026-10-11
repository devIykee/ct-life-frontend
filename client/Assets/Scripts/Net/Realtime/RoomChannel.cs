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
        private readonly List<Action> _unsubscribe = new List<Action>();

        public string RoomName { get; }
        public string InstanceId { get; private set; }
        public string SessionId => Room?.SessionId;
        public TState State => Room != null ? Room.State : null;
        public bool IsJoined => Room != null;
        public bool WelcomeReceived { get; private set; }

        public event Action<RejectedMessage> Rejected;
        public event Action<int> Left;
        public event Action Dropped;
        public event Action Reconnected;

        protected RoomChannel(string roomName)
        {
            RoomName = roomName;
        }

        internal async Task Join(Client client, Dictionary<string, object> options)
        {
            Room = await client.JoinOrCreate<TState>(RoomName, options);
            InstanceId = Room.RoomId;
            // Register synchronously right after the join completes so `welcome` is not missed.
            On<WelcomeMessage>("welcome", OnWelcome);
            On<RejectedMessage>("rejected", r =>
            {
                Log.Warn("rt", $"{RoomName} rejected {r.type}: {r.code}");
                Rejected?.Invoke(r);
            });
            Room.OnLeave += code =>
            {
                Log.Info("rt", $"{RoomName} left ({code})");
                Left?.Invoke(code);
            };
            Room.OnDrop += code =>
            {
                Log.Warn("rt", $"{RoomName} dropped ({code}); SDK will try to reconnect");
                Dropped?.Invoke();
            };
            Room.OnReconnect += () =>
            {
                Log.Info("rt", $"{RoomName} reconnected");
                Reconnected?.Invoke();
            };
            Room.OnError += (code, message) => Log.Warn("rt", $"{RoomName} error {code}: {message}");
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

        public Task Send(string type, object payload)
        {
            if (Room == null) return Task.CompletedTask;
            return Room.Send(type, payload);
        }

        public async Task Leave()
        {
            foreach (var u in _unsubscribe)
            {
                try { u(); } catch { /* ignore */ }
            }
            _unsubscribe.Clear();
            var r = Room;
            Room = null;
            if (r != null)
            {
                try
                {
                    await r.Leave();
                }
                catch (Exception e)
                {
                    Log.Warn("rt", $"Leave {RoomName}: {e.Message}");
                }
            }
        }
    }
}
