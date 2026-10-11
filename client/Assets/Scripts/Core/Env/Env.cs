using System;
using UnityEngine;

namespace CTLife.Core
{
    /// <summary>Resolves the active environment: launch override → EnvSelection asset → built-in default.</summary>
    public static class Env
    {
        private static EnvConfig _current;

        public static EnvConfig Current
        {
            get
            {
                if (_current == null) _current = Resolve();
                return _current;
            }
        }

        public static void Override(EnvConfig config) => _current = config;

        private static EnvConfig Resolve()
        {
            var selection = Resources.Load<EnvSelection>("Env/EnvSelection");
            EnvId id = selection != null ? selection.active : EnvId.Mock;

            if (LaunchArgs.TryGet("env", out var envArg) && Enum.TryParse(envArg, true, out EnvId parsed))
            {
#if !CTLIFE_PROD
                id = parsed;
#endif
            }

            var cfg = selection != null ? selection.Find(id) : null;
            if (cfg == null) cfg = EnvConfig.CreateDefault(id);
            Log.Info("env", $"Environment {cfg.id}: rest={cfg.restBaseUrl} realtime={cfg.realtimeUrl} cluster={cfg.cluster}");
            return cfg;
        }
    }
}
