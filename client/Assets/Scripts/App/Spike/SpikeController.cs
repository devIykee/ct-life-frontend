using System;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.App
{
    /// <summary>
    /// Engine spike (Build Spec "Tech stack → Engine spike"): on a Seeker, an IL2CPP build must sign in with Solana,
    /// join a LiveKit room and a Colyseus room, and hold 60 fps with 62 placeholder avatars.
    /// Automated run: `adb shell am start ... -e spike 1 -e autologin dev [-e livekitUrl ws://localhost:7880 -e livekitToken JWT]`.
    /// Results go to logcat as `CTLIFE [spike] ...` and `CTLIFE [fps] ...` lines.
    /// </summary>
    public sealed class SpikeController : MonoBehaviour
    {
        public const int TargetAvatars = 62;

        private Label _fps, _colyseus, _livekit, _siws, _avatars;

        private async void Start()
        {
            BuildOverlay();
            new WorldLabels(UiRoot.Instance.LabelLayer);
            new ToastLayer(UiRoot.Instance.ToastLayer);
            new BusyOverlay(UiRoot.Instance.ToastLayer);
            await Run();
        }

        private void BuildOverlay()
        {
            _fps = El.Text("fps —", "ct-body");
            _avatars = El.Text("avatars 0", "ct-small");
            _colyseus = El.Text("Colyseus: waiting", "ct-small");
            _livekit = El.Text("LiveKit: waiting", "ct-small");
            _siws = El.Text("SIWS: not tried (needs fingerprint)", "ct-small");
            var panel = El.Div("ct-spike", El.Text("CT Life engine spike", "ct-body"), _fps, _avatars, _colyseus, _livekit, _siws,
                El.Btn("Sign in with Solana", () => _ = TrySiws(), "ct-btn ct-btn-primary"));
            panel.pickingMode = PickingMode.Position;
            UiRoot.Instance.HudLayer.Add(panel);
            UiRoot.Instance.AddUpdater(() =>
            {
                _fps.text = $"fps {FpsMeter.Average:0.0} (min {FpsMeter.Min:0.0}, 1% {FpsMeter.OnePercentLow:0.0})";
                _avatars.text = $"avatars {AvatarView.All.Count} · thermal {(PerfManager.Instance != null ? PerfManager.Instance.ThermalLevel : -1)}";
            });
        }

        private async Task Run()
        {
            // 1) Dev auth against the mock so the Colyseus join has a token.
            var auth = Services.Get<IAuthService>();
            bool authed = false;
            try
            {
                try { await Services.Get<Api>().RefreshConfig(); } catch (Exception e) { Log.Warn("spike", "config: " + e.Message); }
                if (auth.DevAuthAllowed)
                {
                    var authSession = await auth.SignInDev(LaunchArgs.Get("wallet", "SpikeWa11et1111111111111111111111111111111"), false);
                    authed = true;
                    // The real server requires a character for venue joins (409 CHARACTER_REQUIRED).
                    if (authSession.User != null && !authSession.User.HasCharacter) await EnsureCharacter();
                    await Services.Get<PlayerProfile>().Refresh();
                }
            }
            catch (Exception e)
            {
                Log.Warn("spike", "dev auth failed: " + e.Message);
            }

            // 2) Colyseus venue room (mock: 20 fake players + 9 NPCs), else the greybox alone.
            VenueSession session;
            try
            {
                if (!authed) throw new Exception("no token");
                session = await VenueSession.Enter("timeline_square", joinRoom: true);
                Report(_colyseus, $"Colyseus: joined venue {session.Channel.InstanceId} as {session.Channel.SessionId}, welcome={session.Channel.WelcomeReceived}");
            }
            catch (Exception e)
            {
                Report(_colyseus, "Colyseus: FAILED " + e.Message);
                session = await VenueSession.Enter("timeline_square", joinRoom: false);
            }

            // 3) Fill to 62 animated humanoids.
            await Awaitable.WaitForSecondsAsync(2f);
            int needed = TargetAvatars - AvatarView.All.Count;
            var spots = Services.Get<ConfigStore>().Registries.SpotsFor("timeline_square");
            var rng = new System.Random(62);
            string[] emotes = { "emote_wave", "emote_clap", "emote_laugh", "emote_wagmi", "act_dance_1", "emote_shrug" };
            for (int i = 0; i < needed; i++)
            {
                var look = new[] { "skin_0" + (1 + i % 8), "head_01", "hair_" + i, "top_" + i * 7, "bottom_" + i * 3, "shoes_" + i, "acc_none" };
                var view = AvatarFactory.Create("crowd_" + i, look, false, null, session.transform);
                view.userId = "crowd_" + i;
                var walker = view.gameObject.AddComponent<CrowdWalker>();
                walker.Init(session.Venue, spots, rng.Next(), emotes);
            }
            Report(_avatars, $"avatars: {AvatarView.All.Count} (crowd {Math.Max(0, needed)})");

            // 4) LiveKit room join.
            var url = LaunchArgs.Get("livekitUrl", Env.Current.liveKitUrlOverride);
            var token = LaunchArgs.Get("livekitToken", Env.Current.liveKitDevToken);
            try
            {
                if (string.IsNullOrEmpty(token) && session.Channel != null)
                {
                    var t = await Services.Get<Api>().RtcToken("venue", session.Channel.InstanceId);
                    token = t.Token;
                    if (string.IsNullOrEmpty(url)) url = t.Url;
                }
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(url)) throw new Exception("no LiveKit url/token");
                bool ok = await Services.Get<VoiceService>().Connect(url, token, "spike");
                Report(_livekit, ok ? $"LiveKit: connected to {url}" : $"LiveKit: FAILED to connect to {url}");
            }
            catch (Exception e)
            {
                Report(_livekit, "LiveKit: FAILED " + e.Message);
            }
        }

        /// <summary>Creates a starter-item character and rolls the birth lottery for a fresh dev wallet.</summary>
        private static async Task EnsureCharacter()
        {
            var reg = Services.Get<ConfigStore>().Registries;
            string First(string slot) => reg.Items.Values.Where(i => i.slot == slot && i.starter).Select(i => i.id).OrderBy(i => i).FirstOrDefault();
            var look = new[] { First("skin"), First("head"), First("hair"), First("top"), First("bottom"), First("shoes"), "acc_none" };
            var traits = reg.Traits.Take(2).Select(t => t.id).ToList();
            var api = Services.Get<Api>();
            try
            {
                await api.CreateCharacter("spike" + UnityEngine.Random.Range(1000, 9999), look, traits, reg.Dreams.FirstOrDefault()?.id,
                    reg.TribeOrder.FirstOrDefault()?.id ?? "trencher", true);
                Log.Info("spike", "character created");
            }
            catch (ApiException e) when (e.Is("CHARACTER_EXISTS")) { }
            try { await api.RollLottery(); }
            catch (ApiException e) when (e.Is("ALREADY_ROLLED")) { }
        }

        private async Task TrySiws()
        {
            Report(_siws, "SIWS: waiting for the wallet…");
            try
            {
                var s = await Services.Get<IAuthService>().SignInWithWallet(Services.Get<IWalletSignIn>());
                Report(_siws, $"SIWS: OK {s.User?.Wallet} seeker={s.User?.IsSeeker}");
            }
            catch (Exception e)
            {
                Report(_siws, "SIWS: FAILED " + e.Message);
            }
        }

        private static void Report(Label label, string text)
        {
            if (label != null) label.text = text;
            Log.Info("spike", text);
        }
    }
}
