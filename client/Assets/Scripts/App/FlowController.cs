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
using UnityEngine.UIElements;

namespace CTLife.App
{
    /// <summary>
    /// splash → sign-in → birth lottery → character creator → lobby → city. Each onboarding step is skipped when the
    /// server's /me shows it is done (see <see cref="Onboarding"/>). Any start-up failure to reach the backend shows
    /// <see cref="ConnectionRetryScreen"/> and resumes from the failed step. Development automation (never in prod):
    /// launch extras `autologin=dev` (optional `wallet`, `seeker=1`), `autocreate=1`, `venue=terminal_cafe`.
    /// </summary>
    public sealed class FlowController : MonoBehaviour
    {
        public const string StartVenue = "timeline_square";
        private string _homeReturnVenueId;

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
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        private bool _freshApplied;
#endif

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
                _overlays.Add(new CTLife.Economy.MoneyRektCard(ui.ModalLayer, id => { if (_phone != null) _phone.Open(id); }));
                _overlays.Add(new CTLife.SocialFeatures.SocialOverlay(ui.ModalLayer, id => { if (_phone != null) _phone.Open(id); }));
                _overlays.Add(new ProfileCard(ui.ModalLayer));
                _overlays.Add(new CTLife.CityLife.CityLifeOverlays(ui.ModalLayer));
                _overlays.Add(new BusyOverlay(ui.ToastLayer));
                var worldLife = new CTLife.WorldLife.WorldLifeHub();
                _overlays.Add(worldLife);
                // Phase 5: a booking went live or ended, so the venue screens reload their ad creatives now.
                _subscriptions.On<CTLife.Ventures.AdsChangedSignal>(_ => worldLife.RefreshAds());
                _overlays.Add(new CTLife.Ventures.VenturesSpotLinks(id => { if (_phone != null) _phone.Open(id); }));
                RawRoomMessages.Received += OnRawRoomMessage;
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
            while (true)
            {
                try
                {
                    await _owner.Wait(StartSession());
                    return;
                }
                catch (OperationCanceledException) when (!Alive) { throw; }
                catch (Exception e)
                {
                    if (_updateRequired) return;
                    Log.Warn("flow", "Start-up failed, offering retry: " + e.Message);
                    await _owner.Wait(WaitForRetry(e));
                }
            }
        }

        /// <summary>One attempt at config → resume or sign-in → profile/onboarding → rooms. Throws on any failure.</summary>
        private async Task StartSession()
        {
            var splash = SplashScreen.TakeBoot() ?? new SplashScreen();
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
                var session = Services.Get<Session>();
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                // Development automation: `fresh=1` ignores a stored session so onboarding runs for a new account.
                if (LaunchArgs.Flag("fresh") && !_freshApplied) { _freshApplied = true; session.Clear(); }
#endif
                bool signedIn = session.IsSignedIn;
                if (!signedIn)
                {
                    splash.SetStatus("Signing in…");
                    signedIn = await _owner.Wait(Services.Get<IAuthService>().TryResume(_owner.Token));
                }
                CloseFlow(splash);
                // The title screen: "Sign in with Solana" for a new player, "PLAY" for a valid saved session.
                if (!signedIn || !SkipTitleForAutomation()) await _owner.Wait(SignIn(returning: signedIn));
                await _owner.Wait(EnterGame());
            }
            finally { CloseFlow(splash); }
        }

        private Task WaitForRetry(Exception error)
        {
            var tcs = new TaskCompletionSource<bool>();
            string detail = error is ApiException api ? (api.IsNetwork ? Strings.T("common.offline") : api.Code) : error.GetType().Name;
            ConnectionRetryScreen screen = null;
            screen = new ConnectionRetryScreen(detail, () =>
            {
                if (!Owns(screen)) return;
                CloseFlow(screen);
                tcs.TrySetResult(true);
            });
            ShowFlow(screen);
            return _owner.Wait(tcs.Task);
        }

        /// <summary>Development automation (launch extras) continues a saved session without waiting for PLAY.</summary>
        private static bool SkipTitleForAutomation()
        {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            return LaunchArgs.Get("autologin") != null || LaunchArgs.Flag("autoenter") || LaunchArgs.Flag("integration")
                || !string.IsNullOrEmpty(LaunchArgs.Get("app"));
#else
            return false;
#endif
        }

        /// <summary>
        /// The title screen. `returning`: a valid session exists, so it shows PLAY (continue) and "Use another wallet" (sign out,
        /// then the sign-in button). Completes once the player is signed in and continues.
        /// </summary>
        private Task SignIn(bool returning = false)
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

            void Play()
            {
                if (pending || !Owns(screen)) return;
                CloseFlow(screen);
                tcs.TrySetResult(true);
            }

            async void AnotherWallet()
            {
                if (pending || !Owns(screen)) return;
                pending = true;
                screen.SetSwitching(true);
                try { await _owner.Wait(auth.SignOut(wallet)); }
                catch (OperationCanceledException) when (!Alive) { return; }
                catch (Exception e) { Log.Warn("flow", "Sign-out for another wallet: " + e.Message); }
                finally { pending = false; }
                if (!Owns(screen)) return;
                screen.SetSwitching(false);
                screen.SetReturning(false);
            }

            screen = new SignInScreen(Wallet, wallet.IsAvailable, returning, Play, AnotherWallet);
            ShowFlow(screen);
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            // Explicit development automation only. No guest/demo route is exposed in the product UI.
            if (!returning && LaunchArgs.Get("autologin") == "dev" && auth.DevAuthAllowed) Dev();
            // Evidence capture: `shot=<name>` saves the app's own frame (no system dialogs on top) to files/<name>.png.
            if (LaunchArgs.Get("shot") is string shot && System.Text.RegularExpressions.Regex.IsMatch(shot, "^[a-z0-9-]{1,40}$"))
                screen.Element?.schedule.Execute(() => { ScreenCapture.CaptureScreenshot(shot + ".png"); Log.Info("flow", "Saved screenshot " + shot); }).StartingIn(5000);
#endif
            return _owner.Wait(tcs.Task);
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        /// <summary>A stable per-install fake wallet for explicit development automation.</summary>
        private static string DevWallet()
        {
            // A random 32-byte value in base58: the backend validates `wallet` as a Solana public key.
            const string key = "ctlife.devwallet";
            var w = PlayerPrefs.GetString(key, "");
            if (Base58.IsPublicKeyLength(w)) return w;
            var bytes = new byte[32];
            new System.Random().NextBytes(bytes);
            w = Base58.Encode(bytes);
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
            _homeReturnVenueId = null;
            var screen = _gameplayScreen;
            _gameplayScreen = null;
            // Invalidate callbacks before cancelling. Room leases release only their captured channel.
            game?.Dispose();
            if (screen != null) CloseFlow(screen);
            if (ReferenceEquals(_travelGameplay, game)) { _travelGameplay = null; _travelling = false; }
            UnbindKick();
            ProfileCard.PlayerActions = null;
            ProfileCard.VisitHome = null;
            ProfileCard.NpcActions = null;
            WorldLabels.IsFriend = null;
            SettingsApp.ExtraSection = null;
            CTLife.Competition.CompetitionLinks.OpenApp = null;
            // Resets the live Phase 6 state, so a second account never sees the first one's jail, invites or flags.
            CTLife.Competition.CompetitionRuntime.Unbind();
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
                // The roll comes first: the server treats an existing character as "already rolled".
                if (Onboarding.NeedsLottery(profile.Me)) await game.Wait(Lottery(game, session.IsSeeker));
                if (Onboarding.NeedsCharacter(profile.Me)) await game.Wait(CreateCharacter(game));

                var rt = Services.Get<IRealtimeService>();
                try
                {
                    await game.Wait(rt.ConnectCity(game.Token));
                    BindKick(game, rt.City);
                }
                catch (OperationCanceledException) when (!GameAlive(game)) { throw; }
                catch (Exception e) { Log.Warn("flow", "City room unavailable (will retry): " + e.Message); }
                try { await game.Wait(rt.ConnectMarket(game.Token)); }
                catch (OperationCanceledException) when (!GameAlive(game)) { throw; }
                catch (Exception e) { Log.Warn("flow", "Market room unavailable (will retry): " + e.Message); }
                // Rejoins city/market after a failed first join or a drop the SDK could not recover.
                rt.KeepPersistentRoomsAlive(game.Token);
                if (rt is RealtimeService service)
                {
                    service.PersistentRoomRejoined -= OnPersistentRoomRejoined;
                    service.PersistentRoomRejoined += OnPersistentRoomRejoined;
                }

                game.ThrowIfClosed();
                BuildHudAndPhone(game);
                ShowLobby();
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
                if (LaunchArgs.Flag("autoenter")) RunGameAction(game, () => EnterCityFromLobby(game));
                // Device feature pass (dev builds only): `integration=1 [peer=<userId>]`. The editor driver calls it directly.
                if (LaunchArgs.Flag("integration") && !Application.isEditor) RunGameAction(game, async () =>
                {
                    float deadline = Time.realtimeSinceStartup + 90f;
                    while (GameAlive(game) && (VenueSession.Current == null || !VenueSession.Current.IsActive) && Time.realtimeSinceStartup < deadline)
                        await Awaitable.WaitForSecondsAsync(0.5f, game.Token);
                    await Dev.FeatureProbe.Run(LaunchArgs.Get("peer"), game.Token);
                });
                // Device smoke (dev builds only): `app=<phone app id>` opens that app once the player is in a venue.
                if (LaunchArgs.Get("app") is string smokeApp && smokeApp.Length > 0) RunGameAction(game, async () =>
                {
                    float deadline = Time.realtimeSinceStartup + 90f;
                    while (GameAlive(game) && (VenueSession.Current == null || !VenueSession.Current.IsActive) && Time.realtimeSinceStartup < deadline)
                        await Awaitable.WaitForSecondsAsync(0.5f, game.Token);
                    if (GameAlive(game)) { Log.Info("flow", "Opening app " + smokeApp + " (launch extra)"); _phone?.Open(smokeApp); }
                });
#endif
            }
            catch (OperationCanceledException) when (!GameAlive(game)) { }
        }

        private void OnPersistentRoomRejoined(string room)
        {
            var game = _gameplay;
            if (!GameAlive(game)) return;
            var rt = Services.Get<IRealtimeService>();
            if (room == "city" && rt.City != null) BindKick(game, rt.City);
            Log.Info("flow", "Rejoined " + room);
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
            }, serverLookConfirmed: look => CreatorLookPolicy.CanSubmit(look, isAuthoredLook: true, config.IsLookConfirmed));
            ShowFlow(screen);
            _gameplayScreen = screen;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            if (LaunchArgs.Flag("autocreate"))
            {
                var alias = LaunchArgs.Get("alias", "dev" + UnityEngine.Random.Range(10000, 99999));
                if (!screen.AutoSubmitForAutomation(alias)) Log.Warn("flow", "autocreate could not submit the creator");
            }
#endif
            return game.Wait(tcs.Task);
        }

        private Task Lottery(FlowLifetime game, bool isSeeker)
        {
            var tcs = new TaskCompletionSource<bool>();
            LotteryScreen screen = null;
            var api = Services.Get<Api>();
            var profile = Services.Get<PlayerProfile>();
            screen = new LotteryScreen(isSeeker, async () =>
            {
                game.ThrowIfClosed();
                try
                {
                    return await api.RollLottery(); // A started roll keeps its original economic lifetime.
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    // A lost response, a retry after a committed roll, or 409: reconcile with the server's /me.
                    Rest.Model.Me refreshed = null;
                    try { refreshed = await profile.Refresh(game.Token); }
                    catch (Exception refresh) when (!(refresh is OperationCanceledException)) { Log.Warn("lottery", "Profile refresh failed: " + refresh.Message); }
                    if (Onboarding.RollFailureMeansDone(e, refreshed))
                    {
                        Log.Info("lottery", "Roll already recorded server-side (" + (e is ApiException a ? a.Code : e.GetType().Name) + "); continuing");
                        return null;
                    }
                    throw;
                }
            }, r =>
            {
                if (!Owns(game, screen)) return;
                if (r != null) profile.ApplyLottery(r);
                CloseFlow(screen);
                tcs.TrySetResult(true);
            });
            ShowFlow(screen);
            _gameplayScreen = screen;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            if (LaunchArgs.Flag("autocreate")) screen.AutoRollForAutomation();
#endif
            return game.Wait(tcs.Task);
        }

        private void BuildHudAndPhone(FlowLifetime game)
        {
            game.ThrowIfClosed();
            var ui = UiRoot.Instance;
            _phone?.Dispose();
            _phone = new PhoneShell(ui.PhoneLayer);
            PhoneCatalog.Register(_phone, id => RunGameAction(game, () => Travel(game, id)), () => RunGameAction(game, SignOut),
                homeOverride: new CTLife.CityLife.CityHomeApp(() => RunGameAction(game, () => EnterOwnHome(game)),
                    () => RunGameAction(game, () => ReturnFromHome(game)),
                    () => GameAlive(game) && !_travelling && VenueSession.CanEnterOwnHome(),
                    () => GameAlive(game) && VenueSession.Current != null && VenueSession.Current.IsOwnHome));

            VenueSession.RemoteLookFallback = CreatorLookCatalog.FallbackLook;
            ProfileCard.VisitHome = (id, name) => { CTLife.CityLife.CityLifeLinks.VisitHome(id, name); _phone?.Open("home"); };
            ProfileCard.PlayerActions = (id, name, close) => El.Div("ct-col",
                CTLife.SocialFeatures.SocialProfileActions.Build(id, name, close, app => _phone?.Open(app)),
                CTLife.Competition.CompetitionProfileActions.Build(id, name, close),
                HireAction(id, close));
            ProfileCard.NpcActions = CTLife.Competition.CompetitionProfileActions.BuildNpc;
            WorldLabels.IsFriend = id => CTLife.SocialFeatures.SocialHub.Current?.Follows.Contains(id) == true;
            SettingsApp.ExtraSection = CTLife.Competition.XLinkPanel.Build;
            CTLife.Competition.CompetitionRuntime.Bind();
            CTLife.Competition.CompetitionLinks.OpenApp = app => _phone?.Open(app);

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
            if (VenueSession.Current != null && VenueSession.Current.IsOwnHome)
                await ReturnFromHome(game);
            else if (VenueSession.Current == null)
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

        private async Task EnterOwnHome(FlowLifetime game)
        {
            if (!GameAlive(game) || _travelling || !VenueSession.CanEnterOwnHome()) return;
            if (VenueSession.Current != null && VenueSession.Current.IsOwnHome) { _phone?.Close(); CloseLobby(); return; }
            _travelling = true; _travelGameplay = game;
            _phone?.Close();
            var current = VenueSession.Current;
            var reg = Services.Get<ConfigStore>().Registries;
            var returnVenue = current != null && reg.Venue(current.VenueId) != null ? current.VenueId : StartVenue;
            var lobby = _lobby; lobby?.SetBusy(true); lobby?.Element.SetEnabled(false);
            try
            {
                var home = await game.Wait(VenueSession.EnterOwnHome(game.Token));
                game.ThrowIfClosed();
                if (!ReferenceEquals(VenueSession.Current, home) || !home.IsActive || !home.IsOwnHome) return;
                _homeReturnVenueId = returnVenue;
                CloseLobby();
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (GameAlive(game))
                {
                    Log.Warn("home", "Private Home entry failed: " + error.GetType().Name);
                    Bus.Publish(ToastSignal.Text(HomeStrings.T("entryFailed"), ToastKind.Bad));
                }
            }
            finally
            {
                if (ReferenceEquals(_travelGameplay, game)) { _travelGameplay = null; _travelling = false; }
                if (GameAlive(game))
                {
                    lobby?.SetBusy(false); lobby?.Element.SetEnabled(true);
                    // An entry failure may occur after the previous visit has been fully released.
                    // Reopen the existing dashboard owner; do not revive an obsolete scene/channel.
                    if (VenueSession.Current == null) ShowLobby();
                }
            }
        }

        private Task ReturnFromHome(FlowLifetime game)
        {
            if (!GameAlive(game) || _travelling) return Task.CompletedTask;
            var id = _homeReturnVenueId;
            if (string.IsNullOrEmpty(id) || Services.Get<ConfigStore>().Registries.Venue(id) == null) id = StartVenue;
            return Travel(game, id);
        }

        private async Task Travel(FlowLifetime game, string venueId)
        {
            if (!GameAlive(game) || _travelling) return;
            if (venueId == "home") return; // Private Home has a separate Me.homeId-selected entry.
            bool leavingOwnHome = VenueSession.Current != null && VenueSession.Current.IsOwnHome;
            _travelling = true;
            _travelGameplay = game;
            _phone?.Close();
            var lobby = _lobby;
            _lobby?.SetBusy(true);
            _lobby?.Element.SetEnabled(false);
            Log.Info("flow", "Travel to " + venueId);
            bool rejected = false;
            void ReopenLobby()
            {
                lobby?.SetBusy(false);
                lobby?.Element.SetEnabled(true);
            }
            try
            {
                // Phase 6 gated venues (dubai_marina net worth, cabal_lounge membership, sybil_barn jailed-only). Null = go ahead,
                // including when the server cannot answer yet: the room's own check then decides.
                var blocked = await game.Wait(CTLife.Competition.CompetitionLinks.BlockMessageBeforeJoin(venueId, game.Token));
                if (blocked != null) { Bus.Publish(ToastSignal.Text(blocked, ToastKind.Bad)); ReopenLobby(); return; }
                try { await game.Wait(VenueSession.Enter(venueId, joinRoom: true, owner: game.Token)); }
                catch (OperationCanceledException) when (!GameAlive(game)) { throw; }
                catch (Exception e) when (GatedJoinRejection(e) is string rejection)
                {
                    Log.Warn("flow", $"Venue room refused the join ({e.Message})");
                    Bus.Publish(ToastSignal.Text(rejection, ToastKind.Bad));
                    rejected = true;
                    ReopenLobby();
                    if (lobby != null) AudioManager.Instance?.Music.SetDistrict("lobby");
                    return;
                }
                catch (Exception e)
                {
                    Log.Warn("flow", $"Venue room join failed ({e.Message}); entering the venue offline");
                    Bus.Publish(ToastSignal.Text("Realtime is offline. You're alone here for now.", ToastKind.Bad));
                    await game.Wait(VenueSession.Enter(venueId, joinRoom: false, owner: game.Token));
                }
                _homeReturnVenueId = null;
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
                if (GameAlive(game) && (leavingOwnHome || rejected) && VenueSession.Current == null) ShowLobby();
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
            _kickHandler = message => { if (GameAlive(game)) OnKicked(game, message.reasonId); };
            city.Kicked += _kickHandler;
        }

        private void UnbindKick()
        {
            if (_kickCity != null && _kickHandler != null) _kickCity.Kicked -= _kickHandler;
            _kickCity = null;
            _kickHandler = null;
        }

        private void OnKicked(FlowLifetime game, string reasonId)
        {
            if (!Alive) return;
            if (reasonId == "sybil_jailed" && GameAlive(game))
            {
                // Phase 6: a jailed player stays signed in; the server only lets them into the Sybil Barn, where the farm app
                // shows the jail and bail. [Unverified] the 9f3e5ef backend never sends this kick yet.
                Bus.Publish(ToastSignal.Text(CTLife.Competition.CompErrors.JoinRejection("SYBIL_JAILED"), ToastKind.Bad));
                RunGameAction(game, () => Travel(game, "sybil_barn"));
                return;
            }
            Bus.Publish(ToastSignal.Text("Disconnected: " + reasonId, ToastKind.Bad));
            RunAction(SignOut);
        }

        /// <summary>
        /// Phase 6 room messages without generated types (RawRoomMessages): Ventures takes its own types, Competition the rest.
        /// Each lane ignores types that are not its own.
        /// </summary>
        private static void OnRawRoomMessage(string type, object payload)
        {
            bool ventures = false;
            try { ventures = CTLife.Ventures.VenturesRealtime.Dispatch(type, payload); }
            catch (Exception e) { Log.Exception("ventures", e); }
            if (!ventures) CTLife.Competition.CompetitionRuntime.OnRaw(type, payload);
        }

        /// <summary>A gated venue room's join error (Phase 6 codes) as the player-facing message; null for any other failure.</summary>
        private static string GatedJoinRejection(Exception e)
        {
            for (var x = e; x != null; x = x.InnerException)
                foreach (var code in new[] { "NET_WORTH_TOO_LOW", "INVITE_REQUIRED", "SYBIL_JAILED" })
                    if (x.Message != null && x.Message.Contains(code)) return CTLife.Competition.CompErrors.JoinRejection(code);
            return null;
        }

        /// <summary>
        /// Phase 5 "Hire" on another player's card. The Builder app's hire form takes a player id, so this copies the id and opens
        /// Builder (the Ventures lane has no prefill hook; requests.md phase5-ventures item 8).
        /// </summary>
        private VisualElement HireAction(string userId, Action close)
        {
            if (string.IsNullOrEmpty(userId) || !Phases.IsOpen(5)) return null;
            return El.Btn(Strings.T("profile.hire"), () =>
            {
                GUIUtility.systemCopyBuffer = userId;
                close?.Invoke();
                Bus.Publish(ToastSignal.Text(Strings.T("profile.hireCopied"), ToastKind.Info));
                _phone?.Open("builder");
            }, "ct-btn");
        }

        private void OnDestroy()
        {
            Cleanup(EndGameplay);
            Cleanup(() => _owner?.Dispose());
            _subscriptions.Dispose();
            RawRoomMessages.Received -= OnRawRoomMessage;
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
