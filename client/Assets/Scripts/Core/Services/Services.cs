using System;
using System.Collections.Generic;

namespace CTLife.Core
{
    /// <summary>
    /// Tiny service locator. Services are registered once by the bootstrap and looked up by interface or type.
    /// Kept deliberately simple: no scopes, no reflection, main thread only.
    /// </summary>
    public static class Services
    {
        private static readonly Dictionary<Type, object> _map = new Dictionary<Type, object>();

        public static T Register<T>(T service) where T : class
        {
            _map[typeof(T)] = service ?? throw new ArgumentNullException(nameof(service));
            return service;
        }

        public static T Get<T>() where T : class
        {
            if (_map.TryGetValue(typeof(T), out var s)) return (T)s;
            throw new InvalidOperationException($"Service {typeof(T).Name} is not registered");
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (_map.TryGetValue(typeof(T), out var s))
            {
                service = (T)s;
                return true;
            }
            service = null;
            return false;
        }

        public static void Unregister<T>() where T : class => _map.Remove(typeof(T));

        public static void Clear() => _map.Clear();
    }
}
