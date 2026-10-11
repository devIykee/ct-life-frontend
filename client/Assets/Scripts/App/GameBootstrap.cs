using CTLife.Audio;
using CTLife.Core;
using CTLife.Net;
using CTLife.Solana;
using CTLife.UI;
using CTLife.World;
using UnityEngine;

namespace CTLife.App
{
    /// <summary>
    /// Composition root. Lives in the Bootstrap scene (first in Build Settings). Registers every service, creates
    /// the persistent systems (perf, audio, UI), then hands over to <see cref="FlowController"/>.
    /// Launch extra `spike=1` runs the engine spike instead of the game flow.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public static bool Booted { get; private set; }

        [Tooltip("Run the engine spike (62 avatars + Colyseus + LiveKit + SIWS) instead of the game.")]
        public bool runSpike;

        private void Awake()
        {
            if (Booted)
            {
                Destroy(gameObject);
                return;
            }
            Booted = true;
            DontDestroyOnLoad(gameObject);
            Boot();
        }

        public static void Boot()
        {
            Log.Info("boot", $"CT Life {Semver.ClientVersion} (contract {Semver.ContractVersion}) on {SystemInfo.deviceModel}, {SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceName}");
            var session = Services.Register(new Session());
            var config = Services.Register(new ConfigStore());
            config.LoadLocal();
            IRestClient transport = new RestClient(session);
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            if (LaunchArgs.Flag("review")) transport = new Review.ReviewTransport(config.Registries);
#endif
            IRestClient rest = Services.Register<IRestClient>(transport);
            var api = Services.Register(new Api(rest, config));
            Services.Register<IAuthService>(new AuthService(rest, session));
            Services.Register(new PlayerProfile(api));
            Services.Register<IRealtimeService>(new RealtimeService(session));
            Services.Register(new VoiceService());
            var wallet = new MwaWallet();
            Services.Register<IWalletSignIn>(wallet);
            Services.Register<ITransactionSigner>(wallet);

            PerfManager.Ensure();
            AudioManager.Create();
            UiRoot.Create();
            AvatarLod.Ensure();
        }

        private void Start()
        {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            if (LaunchArgs.Flag("review"))
            {
                gameObject.AddComponent<Review.FrontendReviewController>();
                return;
            }
#endif
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            if (runSpike || LaunchArgs.Flag("spike"))
            {
                gameObject.AddComponent<SpikeController>();
                return;
            }
#endif
            gameObject.AddComponent<FlowController>();
        }
    }
}
