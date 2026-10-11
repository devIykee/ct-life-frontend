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
#if !CTLIFE_PROD
            // Development only: a pre-minted LiveKit token (local `livekit-server --dev` keys) for voice tests while
            // the backend has no POST /rtc/token. Copy the asset so the editor never saves the override.
            if (LaunchArgs.TryGet("livekitToken", out var lkToken))
            {
                cfg = UnityEngine.Object.Instantiate(cfg);
                cfg.liveKitDevToken = lkToken;
                if (LaunchArgs.TryGet("livekitUrl", out var lkUrl)) cfg.liveKitUrlOverride = lkUrl;
            }
#endif
            Log.Info("env", $"Environment {cfg.id}: rest={cfg.restBaseUrl} realtime={cfg.realtimeUrl} cluster={cfg.cluster}");
            return cfg;
        }
    }
}
