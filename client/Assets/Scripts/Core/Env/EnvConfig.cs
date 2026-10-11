using System;
using UnityEngine;

namespace CTLife.Core
{
    /// <summary>The four environments from contract.md section 1.</summary>
    public enum EnvId
    {
        Mock = 0,
        Local = 1,
        Staging = 2,
        Prod = 3,
    }

    public enum SolanaCluster
    {
        None = 0,
        Devnet = 1,
        Mainnet = 2,
    }

    /// <summary>
    /// One environment's endpoints. Assets live in Resources/Env (created by CT Life → Project Setup); the build
    /// selects one with `-ctlifeEnv local|staging|prod` (see Editor/BuildScript.cs).
    /// Phones reach the Mac's localhost through `adb reverse tcp:PORT tcp:PORT`, so mock/local keep localhost.
    /// Staging and prod hosts are not issued yet: their URLs are placeholders ending in `.invalid`, and
    /// <see cref="PlaceholderProblem"/> makes the build refuse them until real values are filled in.
    /// </summary>
    [CreateAssetMenu(menuName = "CT Life/Env Config", fileName = "EnvConfig")]
    public sealed class EnvConfig : ScriptableObject
    {
        public const string PlaceholderHost = ".invalid";

        public EnvId id = EnvId.Mock;

        [Tooltip("REST base including /v1, as in the backend contract's `servers` (e.g. http://localhost:4010/v1).")]
        public string restBaseUrl = "http://localhost:4010/v1";

        [Tooltip("Colyseus endpoint. Example: ws://localhost:2567")]
        public string realtimeUrl = "ws://localhost:2567";

        [Tooltip("Cluster used for payments and SIWS chainId checks. ORE and SGT reads are always mainnet (server side).")]
        public SolanaCluster cluster = SolanaCluster.None;

        [Tooltip("POST /v1/auth/dev is only compiled into mock and local servers (contract section 3).")]
        public bool allowDevAuth = true;

        [Tooltip("Highest build phase (1-6) whose features are open when GET /config does not say. The server value wins.")]
        [Range(1, 6)] public int livePhase = 1;

        [Header("Mobile Wallet Adapter identity (shown by the wallet; should match the SIWS domain)")]
        public string walletIdentityName = "CT Life";
        [Tooltip("Absolute https URI of the dApp identity. Placeholder until the domain is decided (build-spec open question).")]
        public string walletIdentityUri = "https://ctlife.invalid/";
        [Tooltip("Icon path relative to walletIdentityUri.")]
        public string walletIconUri = "/favicon.ico";

        [Tooltip("Optional LiveKit URL override when /rtc/token returns a URL the device cannot reach (ws://localhost:7880).")]
        public string liveKitUrlOverride = "";

        [Tooltip("Optional pre-minted LiveKit dev token (local LiveKit --dev keys only). Never ship a real secret.")]
        [TextArea] public string liveKitDevToken = "";

        /// <summary>Hard-coded defaults so the client boots before any asset is authored.</summary>
        public static EnvConfig CreateDefault(EnvId env)
        {
            var c = CreateInstance<EnvConfig>();
            c.name = "EnvConfig_" + env;
            ApplyDefaults(c, env);
            return c;
        }

        public static void ApplyDefaults(EnvConfig c, EnvId env)
        {
            c.id = env;
            switch (env)
            {
                case EnvId.Mock:
                case EnvId.Local:
                    // devIykee's backend README: local and mock both serve REST on 4010 and realtime on 2567.
                    c.restBaseUrl = "http://localhost:4010/v1";
                    c.realtimeUrl = "ws://localhost:2567";
                    c.cluster = env == EnvId.Local ? SolanaCluster.Devnet : SolanaCluster.None;
                    c.allowDevAuth = true;
                    c.livePhase = 6;
                    c.liveKitUrlOverride = "ws://localhost:7880";
                    break;
                case EnvId.Staging:
                    // REST from the contract's `servers`; realtime not issued yet (Railway URLs are set at deploy).
                    c.restBaseUrl = "https://staging-api.ctlife.dev/v1";
                    c.realtimeUrl = "wss://staging-realtime" + PlaceholderHost;
                    c.cluster = SolanaCluster.Devnet;
                    c.allowDevAuth = false;
                    c.livePhase = 1;
                    break;
                case EnvId.Prod:
                    c.restBaseUrl = "https://api.ctlife.dev/v1";
                    c.realtimeUrl = "wss://realtime" + PlaceholderHost;
                    c.cluster = SolanaCluster.Mainnet;
                    c.allowDevAuth = false;
                    c.livePhase = 1;
                    break;
            }
        }

        /// <summary>Why this config cannot ship yet (null when every endpoint is real). Used by the build.</summary>
        public string PlaceholderProblem()
        {
            if (string.IsNullOrWhiteSpace(restBaseUrl) || restBaseUrl.Contains(PlaceholderHost)) return "restBaseUrl is a placeholder";
            if (string.IsNullOrWhiteSpace(realtimeUrl) || realtimeUrl.Contains(PlaceholderHost)) return "realtimeUrl is a placeholder";
            if (id == EnvId.Prod || id == EnvId.Staging)
            {
                if (string.IsNullOrWhiteSpace(walletIdentityUri) || walletIdentityUri.Contains(PlaceholderHost))
                    return "walletIdentityUri is a placeholder";
                if (!restBaseUrl.StartsWith("https://", StringComparison.Ordinal)) return "restBaseUrl must be https";
                if (!realtimeUrl.StartsWith("wss://", StringComparison.Ordinal)) return "realtimeUrl must be wss";
                if (allowDevAuth) return "dev auth must be off";
            }
            return null;
        }

        public string ApiUrl(string path)
        {
            // The base already carries the version prefix (`servers` in the contract); paths are relative to it.
            var b = restBaseUrl.TrimEnd('/');
            if (!path.StartsWith("/")) path = "/" + path;
            if (b.EndsWith("/v1") && path.StartsWith("/v1/")) path = path.Substring(3);
            return b + path;
        }
    }
}
