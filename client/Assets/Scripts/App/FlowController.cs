using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using CTLife.UI;
using CTLife.World;
using UnityEngine;

namespace CTLife.App
{
    /// <summary>
    /// splash → sign-in → character creator → birth lottery → city (Phase 1 flow). Each step is skipped when the
    /// server says it's already done (`hasCharacter`, `flags.lotteryRolled`). Automated runs: launch extras
    /// `autologin=dev` (and optional `wallet`, `seeker=1`, `venue=perp_pit`).
    /// </summary>
    public sealed class FlowController : MonoBehaviour
    {
        public const string StartVenue = "timeline_square";

        private Hud _hud;
        private PhoneShell _phone;
        private bool _updateRequired;

        private async void Start()
        {
            var ui = UiRoot.Instance;
            new ToastLayer(ui.ToastLayer);
            new WorldLabels(ui.LabelLayer);
            new RektCard(ui.ModalLayer);
            new ProfileCard(ui.ModalLayer);
            new BusyOverlay(ui.ToastLayer);
            _ = ConfigRefreshLoop();
            Bus.Subscribe<UpdateRequiredSignal>(s =>
            {
                if (_updateRequired) return;
                _updateRequired = true;
                new UpdateRequiredScreen(s.minClientVersion).Show();
            });
            try
            {
                await Run();
            }
            catch (Exception e)
            {
                Log.Exception("flow", e);
                Bus.Publish(ToastSignal.Text("Something broke: " + e.Message, ToastKind.Bad));
            }
        }

        private async Task Run()
        {
            var splash = new SplashScreen();
            splash.Show();
            splash.SetStatus("Loading config…");
            try
            {
                await Services.Get<Api>().RefreshConfig();
            }
            catch (Exception e)
            {
                Log.Warn("flow", "Config refresh failed, using " + Services.Get<ConfigStore>().Source + ": " + e.Message);
            }
            if (_updateRequired) return;

            splash.SetStatus("Signing in…");
            var auth = Services.Get<IAuthService>();
            bool signedIn = await auth.TryResume();
            splash.Close();
            if (!signedIn) await SignIn();

            await EnterGame();
        }

        private Task SignIn()
        {
            var tcs = new TaskCompletionSource<bool>();
            var auth = Services.Get<IAuthService>();
            var wallet = Services.Get<IWalletSignIn>();
            SignInScreen screen = null;

            async void Wallet()
            {
                screen.SetBusy(true);
                try
                {
                    await auth.SignInWithWallet(wallet);
                    screen.Close();
                    tcs.TrySetResult(true);
                }
                catch (WalletException e)
                {
                    screen.ShowError(e.UserDeclined ? "You cancelled in the wallet." : e.Message);
                }
                catch (ApiException e)
                {
                    screen.ShowError(Strings.Error(e.Code, e.ServerMessage));
                }
                catch (Exception e)
                {
                    screen.ShowError(e.Message);
                }
                finally
                {
                    screen.SetBusy(false);
                }
            }

            async void Dev()
            {
                screen.SetBusy(true);
                try
                {
                    var w = LaunchArgs.Get("wallet", DevWallet());
                    await auth.SignInDev(w, LaunchArgs.Flag("seeker"));
                    screen.Close();
                    tcs.TrySetResult(true);
                }
                catch (Exception e)
                {
                    screen.ShowError(e is ApiException api ? Strings.Error(api.Code, api.ServerMessage) : e.Message);
                }
                finally
                {
                    screen.SetBusy(false);
                }
            }

            screen = new SignInScreen(Wallet, auth.DevAuthAllowed ? (Action)Dev : null, wallet.IsAvailable);
            screen.Show();
            if (LaunchArgs.Get("autologin") == "dev" && auth.DevAuthAllowed) Dev();
            return tcs.Task;
        }

        /// <summary>A stable per-install fake wallet for dev auth (base58 alphabet, 44 chars).</summary>
        private static string DevWallet()
        {
            const string key = "ctlife.devwallet";
            var w = PlayerPrefs.GetString(key, "");
            if (!string.IsNullOrEmpty(w)) return w;
            const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
            var rng = new System.Random();
            var chars = new char[44];
            for (int i = 0; i < chars.Length; i++) chars[i] = alphabet[rng.Next(alphabet.Length)];
            w = new string(chars);
            PlayerPrefs.SetString(key, w);
            PlayerPrefs.Save();
            return w;
        }

        private async Task EnterGame()
        {
            var profile = Services.Get<PlayerProfile>();
            var session = Services.Get<Session>();
            await profile.Refresh();

            if (!profile.HasCharacter) await CreateCharacter();
            if (!profile.LotteryRolled) await Lottery(session.IsSeeker);

            var rt = Services.Get<IRealtimeService>();
            try
            {
                await rt.ConnectCity();
                rt.City.Kicked += k => OnKicked(k.reasonId);
            }
            catch (Exception e) { Log.Warn("flow", "City room unavailable: " + e.Message); }
            try { await rt.ConnectMarket(); }
            catch (Exception e) { Log.Warn("flow", "Market room unavailable: " + e.Message); }

            BuildHudAndPhone();
            await Travel(LaunchArgs.Get("venue", StartVenue));
        }

        private Task CreateCharacter()
        {
            var tcs = new TaskCompletionSource<bool>();
            CharacterCreatorScreen screen = null;
            screen = new CharacterCreatorScreen(Services.Get<ConfigStore>().Registries, async draft =>
            {
                screen.SetBusy(true);
                try
                {
                    await Services.Get<Api>().CreateCharacter(draft.alias, draft.look, draft.traitIds, draft.dreamId,
                        draft.tribeId, draft.ageConfirmed);
                    await Services.Get<PlayerProfile>().Refresh();
                    screen.Close();
                    tcs.TrySetResult(true);
                }
                catch (Exception e)
                {
                    screen.ShowError(e is ApiException api ? Strings.Error(api.Code, api.ServerMessage) : e.Message);
                    screen.SetBusy(false);
                }
            });
            screen.Show();
            return tcs.Task;
        }

        private Task Lottery(bool isSeeker)
        {
            var tcs = new TaskCompletionSource<bool>();
            LotteryScreen screen = null;
            screen = new LotteryScreen(isSeeker, () => Services.Get<Api>().RollLottery(), r =>
            {
                Services.Get<PlayerProfile>().ApplyLottery(r);
                screen.Close();
                tcs.TrySetResult(true);
            });
            screen.Show();
            return tcs.Task;
        }

        private void BuildHudAndPhone()
        {
            var ui = UiRoot.Instance;
            _phone = new PhoneShell(ui.PhoneLayer);
            _phone.Register(new MapApp(id => _ = Travel(id)));
            _phone.Register(new JobsApp());
            _phone.Register(new BankApp());
            _phone.Register(new RichListApp());
            _phone.Register(new HomeApp());
            _phone.Register(new ProfileApp());
            _phone.Register(new SettingsApp(() => _ = SignOut()));
            _phone.Register(new ComingApp("pulse", "Pulse", 2, "#20bf6b", Features.Markets));
            _phone.Register(new ComingApp("perps", "Perps", 2, "#eb3b5a", Features.Markets));
            _phone.Register(new ComingApp("predict", "Predict", 2, "#3867d6", Features.Markets));
            _phone.Register(new ComingApp("ore", "ORE", 2, "#fd9644", Features.OreReader));
            _phone.Register(new ComingApp("topup", "Top up", 2, "#f7b731", Features.Payments));
            _phone.Register(new ComingApp("timeline", "Timeline", 3, "#2d98da", Features.Feed));
            _phone.Register(new ComingApp("messages", "Messages", 3, "#26de81", Features.Dms));
            _phone.Register(new ComingApp("spaces", "Spaces", 3, "#a55eea", Features.Voice));
            _phone.Register(new ComingApp("calls", "Calls", 3, "#0fb9b1", Features.Voice));
            _phone.Register(new ComingApp("shop", "Shop", 4, "#fc5c65"));
            _phone.Register(new ComingApp("ads", "Ads", 5, "#778ca3"));
            _phone.Register(new ComingApp("league", "League", 6, "#4b7bec"));

            _hud = new Hud { OpenPhone = () => _phone.Open(), OpenMap = () => _phone.Open("map") };
            ui.HudLayer.Add(_hud.Element);
        }

        private async Task Travel(string venueId)
        {
            Log.Info("flow", "Travel to " + venueId);
            try
            {
                await VenueSession.Enter(venueId, joinRoom: true);
            }
            catch (Exception e)
            {
                Log.Warn("flow", $"Venue room join failed ({e.Message}); entering offline greybox");
                Bus.Publish(ToastSignal.Text("Realtime is offline. You're alone here for now.", ToastKind.Bad));
                await VenueSession.Enter(venueId, joinRoom: false);
            }
        }

        /// <summary>Re-reads GET /config every 2 minutes (If-None-Match, so usually a 304) to pick up `degraded`.</summary>
        private async Task ConfigRefreshLoop()
        {
            while (this != null)
            {
                await Awaitable.WaitForSecondsAsync(120f);
                try { await Services.Get<Api>().RefreshConfig(); }
                catch (Exception e) { Log.Debug("flow", "config refresh: " + e.Message); }
            }
        }

        private void OnKicked(string reasonId)
        {
            Bus.Publish(ToastSignal.Text("Disconnected: " + reasonId, ToastKind.Bad));
            _ = SignOut();
        }

        private async Task SignOut()
        {
            if (VenueSession.Current != null) await VenueSession.Current.Exit();
            await Services.Get<IRealtimeService>().DisconnectAll();
            await Services.Get<IAuthService>().SignOut(Services.Get<IWalletSignIn>());
            _hud?.Element.RemoveFromHierarchy();
            _hud?.Dispose();
            _phone?.Close();
            await SignIn();
            await EnterGame();
        }
    }
}
