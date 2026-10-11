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
    /// Composition root. Lives in the Bootstrap scene (the only scene in Build Settings). Registers every service,
    /// creates the persistent systems (perf, audio, UI), then hands over to <see cref="FlowController"/>.
    /// The old review fixtures and engine spike live in client/Experiments~ and are not compiled.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public static bool Booted { get; private set; }

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
            // One line per Info/Warning. With the default script stack on every log line, a caught 404 warning printed an
            // async stack (Task.TrySetException / AsyncTaskMethodBuilder.SetException) that read like an exception on device
            // (2026-10-08). Errors, asserts and exceptions keep their stacks.
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
            // Development builds pop Unity's on-screen developer console over the game on any logged error (it covered
            // half the lobby on 2026-10-08). Errors still reach logcat; only the overlay is off.
            Debug.developerConsoleEnabled = false;
            Debug.developerConsoleVisible = false;
#endif
            Log.Info("boot", $"CT Life {Semver.ClientVersion} (contract {Semver.ContractVersion}) on {SystemInfo.deviceModel}, {SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsDeviceName}");
            var session = Services.Register(new Session());
            var config = Services.Register(new ConfigStore());
            config.LoadLocal();
            IRestClient rest = Services.Register<IRestClient>(new RestClient(session));
            var api = Services.Register(new Api(rest, config));
            var auth = new AuthService(rest, session);
            Services.Register<IAuthService>(auth);
            Services.Register(new PlayerProfile(api));
            var realtime = new RealtimeService(session)
            {
                // Rooms verify the JWT at join time; never join with a token that is about to expire.
                EnsureFreshToken = auth.EnsureFresh,
            };
            Services.Register<IRealtimeService>(realtime);
            Services.Register(new VoiceService());
            var env = Env.Current;
            var wallet = new MwaWallet(env.walletIdentityName, env.IdentityUri, env.walletIconUri);
            Services.Register<IWalletSignIn>(wallet);
            Services.Register<ITransactionSigner>(wallet);

            // Brand first: the first rendered frame is the full-screen branded loading screen, never the
            // scene's empty sky (launch showed 3-4 s of sky before the flow's first screen).
            UiRoot.Create();
            SplashScreen.ShowBoot();
            PerfManager.Ensure();
            AudioManager.Create();
            AvatarLod.Ensure();
        }

        private void Start() => gameObject.AddComponent<FlowController>();
    }
}
