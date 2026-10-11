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
    /// One environment's endpoints. Assets live in Resources/Env (created by CT Life → Project Setup).
    /// Phones reach the Mac's localhost through `adb reverse tcp:PORT tcp:PORT`, so mock/local keep localhost.
    /// </summary>
    [CreateAssetMenu(menuName = "CT Life/Env Config", fileName = "EnvConfig")]
    public sealed class EnvConfig : ScriptableObject
    {
        public EnvId id = EnvId.Mock;

        [Tooltip("REST base including /v1, as in the backend contract's `servers` (e.g. http://localhost:4010/v1).")]
        public string restBaseUrl = "http://localhost:4010/v1";

        [Tooltip("Colyseus endpoint. Example: ws://localhost:2567")]
        public string realtimeUrl = "ws://localhost:2567";

        [Tooltip("Cluster used for payments and SIWS chainId checks. ORE and SGT reads are always mainnet (server side).")]
        public SolanaCluster cluster = SolanaCluster.None;

        [Tooltip("POST /v1/auth/dev is only compiled into mock and local servers (contract section 3).")]
        public bool allowDevAuth = true;

        [Tooltip("Optional LiveKit URL override for the engine spike when no /rtc/token server is up (ws://localhost:7880).")]
        public string liveKitUrlOverride = "";

        [Tooltip("Optional pre-minted LiveKit token for the engine spike only. Never ship a real secret.")]
        [TextArea] public string liveKitDevToken = "";

        /// <summary>Hard-coded defaults so the client boots before any asset is authored.</summary>
        public static EnvConfig CreateDefault(EnvId env)
        {
            var c = CreateInstance<EnvConfig>();
            c.name = "EnvConfig_" + env;
            c.id = env;
            switch (env)
            {
                case EnvId.Mock:
                    c.restBaseUrl = "http://localhost:4010/v1";
                    c.realtimeUrl = "ws://localhost:2567";
                    c.cluster = SolanaCluster.None;
                    c.allowDevAuth = true;
                    c.liveKitUrlOverride = "ws://localhost:7880";
                    break;
                case EnvId.Local:
                    c.restBaseUrl = "http://localhost:3000/v1";
                    c.realtimeUrl = "ws://localhost:2567";
                    c.cluster = SolanaCluster.Devnet;
                    c.allowDevAuth = true;
                    c.liveKitUrlOverride = "ws://localhost:7880";
                    break;
                case EnvId.Staging:
                    // REST from the backend contract's `servers` (1.0.0-rc1). [Unverified] realtime host: not in the REST spec.
                    c.restBaseUrl = "https://staging-api.ctlife.dev/v1";
                    c.realtimeUrl = "wss://staging-realtime.invalid";
                    c.cluster = SolanaCluster.Devnet;
                    c.allowDevAuth = false;
                    break;
                case EnvId.Prod:
                    c.restBaseUrl = "https://api.ctlife.dev/v1";
                    c.realtimeUrl = "wss://realtime.invalid";
                    c.cluster = SolanaCluster.Mainnet;
                    c.allowDevAuth = false;
                    break;
            }
            return c;
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
