using System;
using System.Collections.Generic;

namespace CTLife.Core
{
    /// <summary>
    /// Typed in-process message bus so modules talk without referencing each other
    /// (Net publishes realtime messages, UI/World/Audio subscribe). Main thread only.
    /// </summary>
    public static class Bus
    {
        private static readonly Dictionary<Type, Delegate> _handlers = new Dictionary<Type, Delegate>();

        /// <returns>An action that unsubscribes.</returns>
        public static Action Subscribe<T>(Action<T> handler)
        {
            var t = typeof(T);
            _handlers.TryGetValue(t, out var existing);
            _handlers[t] = Delegate.Combine(existing, handler);
            return () => Unsubscribe(handler);
        }

        public static void Unsubscribe<T>(Action<T> handler)
        {
            var t = typeof(T);
            if (_handlers.TryGetValue(t, out var existing))
            {
                var next = Delegate.Remove(existing, handler);
                if (next == null) _handlers.Remove(t);
                else _handlers[t] = next;
            }
        }

        public static void Publish<T>(T message)
        {
            if (!_handlers.TryGetValue(typeof(T), out var d)) return;
            var invocation = ((Action<T>)d).GetInvocationList();
            foreach (var h in invocation)
            {
                try
                {
                    ((Action<T>)h)(message);
                }
                catch (Exception e)
                {
                    Log.Exception("bus", e);
                }
            }
        }

        public static void Clear() => _handlers.Clear();
    }

    /// <summary>Collects unsubscribe actions; dispose in OnDestroy.</summary>
    public sealed class Subscriptions : IDisposable
    {
        private readonly List<Action> _undo = new List<Action>();

        public void Add(Action unsubscribe)
        {
            if (unsubscribe != null) _undo.Add(unsubscribe);
        }

        public void On<T>(Action<T> handler) => Add(Bus.Subscribe(handler));

        public void Dispose()
        {
            foreach (var u in _undo)
            {
                try { u(); } catch (Exception e) { Log.Exception("bus", e); }
            }
            _undo.Clear();
        }
    }
}
