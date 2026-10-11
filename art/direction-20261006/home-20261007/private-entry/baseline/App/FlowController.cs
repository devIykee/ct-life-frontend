using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Realtime;
using CTLife.Audio;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using CTLife.UI;
using CTLife.World;
using UnityEngine;

namespace CTLife.App
{
    /// <summary>
    /// splash → sign-in → character creator → birth lottery → lobby → city. Each onboarding step is skipped when the
    /// server says it's already done (`hasCharacter`, `flags.lotteryRolled`). Automated runs: launch extras
    /// `autologin=dev` (and optional `wallet`, `seeker=1`, `venue=perp_pit`).
    /// </summary>
    public sealed class FlowController : MonoBehaviour
    {
        public const string StartVenue = "timeline_square";

        private readonly List<IDisposable> _overlays = new List<IDisposable>();
        private Hud _hud;
        private PhoneShell _phone;
        private DashboardScreen _lobby;
        private bool _travelling;
        private bool _updateRequired;
        private bool _signingOut;
        private FlowLifetime _owner, _gameplay, _travelGameplay;
        private UiScreen _gameplayScreen;
        private readonly Subscriptions _subscriptions = new Subscriptions();
        private CityChannel _kickCity;
        private Action<KickedMessage> _kickHandler;
        private UiScreen _flowScreen, _updateScreen;
        private bool Alive => _owner != null && _owner.IsAlive;

        private void Awake()
        {
            // Cache the Unity6.3 destruction token while this MonoBehaviour is still alive.
            var destruction = destroyCancellationToken;
            _owner = new FlowLifetime(destruction, e => Log.Warn("flow", "Late owned operation failed: " + e.GetType().Name));
        }

        private async void Start()
        {
            try
            {
                _owner.ThrowIfClosed();
                var ui = UiRoot.Instance;
                _overlays.Add(new ToastLayer(ui.ToastLayer));
                _overlays.Add(new WorldLabels(ui.LabelLayer));
                _overlays.Add(new RektCard(ui.ModalLayer));
                _overlays.Add(new ProfileCard(ui.ModalLayer));
                _overlays.Add(new BusyOverlay(ui.ToastLayer));
                RunAction(ConfigRefreshLoop);
                _subscriptions.On<UpdateRequiredSignal>(signal =>
                {
                    if (!Alive || _updateRequired) return;
                    _updateRequired = true;
                    _updateScreen = new UpdateRequiredScreen(signal.minClientVersion);
                    _updateScreen.Show();
                });
                await _owner.Wait(Run());
            }
            catch (OperationCanceledException) when (!Alive) { }
            catch (Exception e) { ReportFailure(e); }
        }

        private async void RunAction(Func<Task> action)
        {
            if (!Alive) return;
            try { await _owner.Wait(action()); }
            catch (OperationCanceledException) when (!Alive) { }
            catch (Exception e) { ReportFailure(e); }
        }

        private void ReportFailure(Exception e)
        {
            if (!Alive) return;
            Log.Exception("flow", e);
            Bus.Publish(ToastSignal.Text(Strings.T("common.error"), ToastKind.Bad));
        }

        private void ShowFlow(UiScreen screen)
        {
            _owner.ThrowIfClosed();
            if (!ReferenceEquals(_flowScreen, screen)) _flowScreen?.Close();
            _flowScreen = screen;
            screen.Show();
        }

        private bool Owns(UiScreen screen) => Alive && ReferenceEquals(_flowScreen, screen);
        private void CloseFlow(UiScreen screen)
        {
            if (ReferenceEquals(_flowScreen, screen)) _flowScreen = null;
            screen?.Close();
        }

        private async Task Run()
        {
            var splash = new SplashScreen();
            ShowFlow(splash);
            try
            {
                splash.SetStatus("Loading config…");
                try { await _owner.Wait(Services.Get<Api>().RefreshConfig(_owner.Token)); }
                catch (OperationCanceledException) when (!Alive) { throw; }
                catch (Exception e)
                {
                    Log.Warn("flow", "Config refresh failed, using " + Services.Get<ConfigStore>().Source + ": " + e.Message);
                }
                _owner.ThrowIfClosed();
                if (_updateRequired) return;
                splash.SetStatus("Signing in…");
                bool signedIn = await _owner.Wait(Services.Get<IAuthService>().TryResume(_owner.Token));
                CloseFlow(splash);
                if (!signedIn) await _owner.Wait(SignIn());
                await _owner.Wait(EnterGame());
            }
            finally { CloseFlow(splash); }
        }

        private Task SignIn()
        {
            var tcs = new TaskCompletionSource<bool>();
            var auth = Services.Get<IAuthService>();
            var wallet = Services.Get<IWalletSignIn>();
            SignInScreen screen = null;
            bool pending = false;

            async void Wallet()
            {
                if (pending || !Owns(screen)) return;
                pending = true;
                screen.SetBusy(true);
                try
                {
                    await _owner.Wait(auth.SignInWithWallet(wallet, _owner.Token));
                    if (!Owns(screen)) return;
                    CloseFlow(screen);
                    tcs.TrySetResult(true);
                }
                catch (OperationCanceledException) when (!Alive) { }
                catch (WalletException e)
                {
                    if (Owns(screen)) screen.ShowError(e.UserDeclined ? "You cancelled in the wallet." : e.Message);
                }
                catch (ApiException e)
                {
                    if (Owns(screen)) screen.ShowError(Strings.Error(e.Code, e.ServerMessage));
                }
                catch (Exception e)
                {
                    if (Owns(screen)) screen.ShowError(e.Message);
                }
                finally
                {
                    pending = false;
                    if (Owns(screen)) screen.SetBusy(false);
                }
            }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            async void Dev()
            {
                if (pending || !Owns(screen)) return;
                pending = true;
                screen.SetBusy(true);
                try
                {
                    var w = LaunchArgs.Get("wallet", DevWallet());
                    await _owner.Wait(auth.SignInDev(w, LaunchArgs.Flag("seeker"), _owner.Token));
                    if (!Owns(screen)) return;
                    CloseFlow(screen);
                    tcs.TrySetResult(true);
                }
                catch (OperationCanceledException) when (!Alive) { }
                catch (Exception e)
                {
                    if (Owns(screen)) screen.ShowError(e is ApiException api ? Strings.Error(api.Code, api.ServerMessage) : e.Message);
                }
                finally
                {
                    pending = false;
                    if (Owns(screen)) screen.SetBusy(false);
                }
            }
#endif

            screen = new SignInScreen(Wallet, wallet.IsAvailable);
            ShowFlow(screen);
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            // Explicit development automation only. No guest/demo route is exposed in the product UI.
            if (LaunchArgs.Get("autologin") == "dev" && auth.DevAuthAllowed) Dev();
#endif
            return _owner.Wait(tcs.Task);
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        /// <summary>A stable per-install fake wallet for explicit development automation.</summary>
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
#endif

        private bool GameAlive(FlowLifetime game) => Alive && game != null && ReferenceEquals(_gameplay, game) && game.IsAlive;
        private bool Owns(FlowLifetime game, UiScreen screen) => GameAlive(game) && Owns(screen);

        private FlowLifetime BeginGameplay()
        {
            _owner.ThrowIfClosed();
            EndGameplay();
            return _gameplay = new FlowLifetime(_owner.Token,
                error => Log.Warn("flow", "Late gameplay operation failed: " + error.GetType().Name));
        }

        private void EndGameplay()
        {
            var game = _gameplay;
            _gameplay = null;
            var screen = _gameplayScreen;
            _gameplayScreen = null;
            // Invalidate callbacks before cancelling. Room leases release only their captured channel.
            game?.Dispose();
            if (screen != null) CloseFlow(screen);
            if (ReferenceEquals(_travelGameplay, game)) { _travelGameplay = null; _travelling = false; }
            UnbindKick();
        }

        private void RunGameAction(FlowLifetime game, Func<Task> action)
        {
            if (GameAlive(game)) RunAction(action);
        }

        private async Task EnterGame()
        {
            var game = BeginGameplay();
            try
            {
                var profile = Services.Get<PlayerProfile>();
                var session = Services.Get<Session>();
                await game.Wait(profile.Refresh(game.Token));
                if (!profile.HasCharacter) await game.Wait(CreateCharacter(game));
                if (!profile.LotteryRolled) await game.Wait(Lottery(game, session.IsSeeker));

                var rt = Services.Get<IRealtimeService>();
                try
                {
                    await game.Wait(rt.ConnectCity(game.Token));
                    BindKick(game, rt.City);
                }
                catch (OperationCanceledException) when (!GameAlive(game)) { throw; }
                catch (Exception e) { Log.Warn("flow", "City room unavailable: " + e.Message); }
                try { await game.Wait(rt.ConnectMarket(game.Token)); }
                catch (OperationCanceledException) when (!GameAlive(game)) { throw; }
                catch (Exception e) { Log.Warn("flow", "Market room unavailable: " + e.Message); }

                game.ThrowIfClosed();
                BuildHudAndPhone(game);
                ShowLobby();
            }
            catch (OperationCanceledException) when (!GameAlive(game)) { }
        }

        private Task CreateCharacter(FlowLifetime game)
        {
            var tcs = new TaskCompletionSource<bool>();
            CharacterCreatorScreen screen = null;
            bool pending = false;
            var config = Services.Get<ConfigStore>();
            screen = new CharacterCreatorScreen(config.Registries, async draft =>
            {
                if (pending || !Owns(game, screen)) return;
                pending = true;
                screen.SetBusy(true);
                try
                {
                    await game.Wait(Services.Get<Api>().CreateCharacter(draft.alias, draft.look, draft.traitIds, draft.dreamId,
                        draft.tribeId, draft.ageConfirmed));
                    await game.Wait(Services.Get<PlayerProfile>().Refresh(game.Token));
                    if (!Owns(game, screen)) return;
                    CloseFlow(screen);
                    tcs.TrySetResult(true);
                }
                catch (OperationCanceledException) when (!GameAlive(game)) { }
                catch (Exception e)
                {
                    if (Owns(game, screen)) screen.ShowError(e is ApiException api ? Strings.Error(api.Code, api.ServerMessage) : e.Message);
                    if (Owns(game, screen)) screen.SetBusy(false);
                }
                finally { pending = false; }
            }, serverLookConfirmed: config.IsLookConfirmed);
            ShowFlow(screen);
            _gameplayScreen = screen;
            return game.Wait(tcs.Task);
        }

        private Task Lottery(FlowLifetime game, bool isSeeker)
        {
            var tcs = new TaskCompletionSource<bool>();
            LotteryScreen screen = null;
            var api = Services.Get<Api>();
            screen = new LotteryScreen(isSeeker, () =>
            {
                game.ThrowIfClosed();
                return api.RollLottery(); // A started roll keeps its original economic lifetime.
            }, r =>
            {
                if (!Owns(game, screen)) return;
                Services.Get<PlayerProfile>().ApplyLottery(r);
                CloseFlow(screen);
                tcs.TrySetResult(true);
            });
            ShowFlow(screen);
            _gameplayScreen = screen;
            return game.Wait(tcs.Task);
        }

        private void BuildHudAndPhone(FlowLifetime game)
        {
            game.ThrowIfClosed();
            var ui = UiRoot.Instance;
            _phone?.Dispose();
            _phone = new PhoneShell(ui.PhoneLayer);
            PhoneCatalog.Register(_phone, id => RunGameAction(game, () => Travel(game, id)), () => RunGameAction(game, SignOut));

            _hud?.Element.RemoveFromHierarchy();
            _hud?.Dispose();
            _hud = new Hud { OpenPhone = () => { if (GameAlive(game)) _phone?.Open(); }, OpenMap = () => { if (GameAlive(game)) _phone?.Open("map"); }, OpenHome = () => { if (GameAlive(game)) ShowLobby(); } };
            ui.HudLayer.Add(_hud.Element);
        }

        private void ShowLobby()
        {
            var game = _gameplay;
            if (!GameAlive(game) || _travelling || _lobby != null) return;
            _phone?.Close();
            VenueSession.Current?.LocalPlayer?.CancelMovement();
            _hud?.Show(false);
            _lobby = new DashboardScreen(() => RunGameAction(game, () => EnterCityFromLobby(game)), id => { if (GameAlive(game)) _phone?.Open(id); });
            _lobby.Show();
            AudioManager.Instance?.Music.SetDistrict("lobby");
        }

        private async Task EnterCityFromLobby(FlowLifetime game)
        {
            if (!GameAlive(game) || _travelling) return;
            if (VenueSession.Current == null)
                await Travel(game, LaunchArgs.Get("venue", StartVenue));
            else
            {
                CloseLobby();
                var current = VenueSession.Current;
                var venue = Services.Get<ConfigStore>().Registries.Venue(current.VenueId);
                AudioManager.Instance?.Music.SetMusic(current.VenueId, venue?.districtId, venue != null && venue.isClub);
            }
        }

        private void CloseLobby()
        {
            _lobby?.Close();
            _lobby = null;
            _hud?.Show(true);
        }

        private async Task Travel(FlowLifetime game, string venueId)
        {
            if (!GameAlive(game) || _travelling) return;
            _travelling = true;
            _travelGameplay = game;
            var lobby = _lobby;
            _lobby?.SetBusy(true);
            _lobby?.Element.SetEnabled(false);
            Log.Info("flow", "Travel to " + venueId);
            try
            {
                try { await game.Wait(VenueSession.Enter(venueId, joinRoom: true, owner: game.Token)); }
                catch (OperationCanceledException) when (!GameAlive(game)) { throw; }
                catch (Exception e)
                {
                    Log.Warn("flow", $"Venue room join failed ({e.Message}); entering the venue offline");
                    Bus.Publish(ToastSignal.Text("Realtime is offline. You're alone here for now.", ToastKind.Bad));
                    await game.Wait(VenueSession.Enter(venueId, joinRoom: false, owner: game.Token));
                }
                CloseLobby();
            }
            catch (OperationCanceledException) when (!GameAlive(game)) { }
            catch (Exception e)
            {
                if (!GameAlive(game)) return;
                Log.Exception("flow", e);
                Bus.Publish(ToastSignal.Text(e.Message, ToastKind.Bad));
                lobby?.SetBusy(false);
                lobby?.Element.SetEnabled(true);
                if (lobby != null) AudioManager.Instance?.Music.SetDistrict("lobby");
            }
            finally
            {
                if (ReferenceEquals(_travelGameplay, game)) { _travelGameplay = null; _travelling = false; }
            }
        }

        /// <summary>Re-reads GET /config every 2 minutes (If-None-Match, so usually a 304) to pick up `degraded`.</summary>
        private async Task ConfigRefreshLoop()
        {
            while (Alive)
            {
                await Awaitable.WaitForSecondsAsync(120f, _owner.Token);
                _owner.ThrowIfClosed();
                try { await _owner.Wait(Services.Get<Api>().RefreshConfig(_owner.Token)); }
                catch (OperationCanceledException) when (!Alive) { throw; }
                catch (Exception e) { Log.Debug("flow", "config refresh: " + e.Message); }
            }
        }

        private void BindKick(FlowLifetime game, CityChannel city)
        {
            UnbindKick();
            if (!GameAlive(game) || city == null) return;
            _kickCity = city;
            _kickHandler = message => { if (GameAlive(game)) OnKicked(message.reasonId); };
            city.Kicked += _kickHandler;
        }

        private void UnbindKick()
        {
            if (_kickCity != null && _kickHandler != null) _kickCity.Kicked -= _kickHandler;
            _kickCity = null;
            _kickHandler = null;
        }

        private void OnKicked(string reasonId)
        {
            if (!Alive) return;
            Bus.Publish(ToastSignal.Text("Disconnected: " + reasonId, ToastKind.Bad));
            RunAction(SignOut);
        }

        private void OnDestroy()
        {
            Cleanup(EndGameplay);
            Cleanup(() => _owner?.Dispose());
            _subscriptions.Dispose();
            UnbindKick();
            for (int i = _overlays.Count - 1; i >= 0; i--) Cleanup(_overlays[i].Dispose);
            _overlays.Clear();
            Cleanup(() => _flowScreen?.Close()); _flowScreen = null;
            Cleanup(() => _updateScreen?.Close()); _updateScreen = null;
            Cleanup(() => _lobby?.Close()); _lobby = null;
            Cleanup(() => _phone?.Dispose()); _phone = null;
            Cleanup(() => _hud?.Element.RemoveFromHierarchy());
            Cleanup(() => _hud?.Dispose()); _hud = null;
        }

        private static void Cleanup(Action close)
        {
            try { close(); }
            catch (Exception e) { Log.Exception("flow-cleanup", e); }
        }

        private static async Task FinishSignOut(VenueSession venue, IRealtimeService realtime, IAuthService auth, IWalletSignIn wallet)
        {
            if (venue != null) await venue.Exit();
            await realtime.DisconnectAll();
            await auth.SignOut(wallet);
        }

        private async Task SignOut()
        {
            if (!Alive || _signingOut) return;
            _signingOut = true;
            try
            {
                EndGameplay();
                _lobby?.Close();
                _lobby = null;
                UnbindKick();
                // An explicitly requested logout continues its existing exit/disconnect/revoke sequence
                // even if this UI owner disappears. Only the controller's wait is cancelled.
                await _owner.Wait(FinishSignOut(VenueSession.Current, Services.Get<IRealtimeService>(),
                    Services.Get<IAuthService>(), Services.Get<IWalletSignIn>()));
                _hud?.Element.RemoveFromHierarchy();
                _hud?.Dispose();
                _phone?.Dispose();
                _phone = null;
                await _owner.Wait(SignIn());
                await _owner.Wait(EnterGame());
            }
            finally { _signingOut = false; }
        }
    }
}
