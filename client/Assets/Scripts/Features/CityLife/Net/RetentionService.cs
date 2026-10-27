using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.CityLife
{
    /// <summary>
    /// The daily habit loop for one signed-in player (2026-10-24, backend PR #9):
    /// - the gm streak and today's chest (`GET /daily/status`, `POST /daily/claim`, `POST /daily/freeze`), refreshed at the
    ///   server's UTC reset;
    /// - today's 3 quests and the weekly chain (`GET /quests`, `POST /quests/{id}/claim`), moved live by `quest_progress` on
    ///   the city room (a toast over the game);
    /// - push: once the OS allows notifications and the build carries Firebase config, the FCM token is registered
    ///   (`POST /push/register`) and the prefs get the device's UTC offset (`PUT /push/prefs`); a notification's
    ///   `deep_link` (cold start or a tap while running) and `ctlife://` links open the right Duo app.
    /// Owned by <see cref="RetentionOverlays"/>; rebinds on sign-in and sign-out.
    /// </summary>
    public sealed class RetentionService : IDisposable
    {
        public const string PushAskedKey = "ctlife.push.asked";
        /// <summary>The FCM token this install last registered, so sign-out can unregister it.</summary>
        private const string TokenKey = "ctlife.push.token";

        public static RetentionService Current { get; private set; }
        /// <summary>Opens a Duo app; false while there is no phone yet (FlowController sets it). The link waits until true.</summary>
        public static Func<string, bool> Opener;
        /// <summary>A quest moved (realtime): the overlay shows the toast.</summary>
        public static event Action<Quest, QuestBoard> QuestMoved;

        public readonly RetentionApi Api;
        public string UserId { get; }
        public DailyStatus Status { get; private set; }
        public QuestBoard Board { get; private set; }
        public bool Disposed { get; private set; }
        public event Action StatusChanged, QuestsChanged;

        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private string _pendingLink;
        private bool _statusLoading, _questsLoading, _pushStarted, _prefsSent;
        private float _statusAt = -1e9f, _questsAt = -1e9f;
        private long _resetSeenMs;
        private int _writes;   // a claim or freeze that lands while a GET is in flight wins over the older read

        public RetentionService(string userId)
        {
            UserId = userId;
            Api = new RetentionApi(Services.Get<IRestClient>());
            Current = this;
            RawRoomMessages.Received += OnRaw;
            Application.focusChanged += OnFocus;
            Application.deepLinkActivated += OnDeepLink;
            if (!string.IsNullOrEmpty(Application.absoluteURL)) _pendingLink = Application.absoluteURL;
            TakeLaunchLink();
            _ = RefreshStatus();
            _ = RefreshQuests();
            StartPush();
            _ = Loop();
        }

        public CancellationToken Life => _life.Token;

        // ------------------------------------------------------------------ streak + chest

        public async Task<DailyStatus> RefreshStatus(bool force = true)
        {
            if (Disposed || RuntimeMode.IsOfflineDemo || _statusLoading) return Status;
            if (!force && Time.realtimeSinceStartup - _statusAt < 5f) return Status;
            _statusLoading = true;
            int writes = _writes;
            try
            {
                var s = await Api.Status(_life.Token);
                if (Disposed) return s;
                if (writes != _writes) return Status;
                Status = s; _statusAt = Time.realtimeSinceStartup;
                Log.Info("retention", $"status day {s.StreakDay} claimed {s.ClaimedToday} chest day {s.ChestDay} ${s.Chest.CashCents / 100.0:0.00} freezes {s.Freezes}");
                StatusChanged?.Invoke();
                return s;
            }
            catch (OperationCanceledException) { return Status; }
            catch (Exception e) { Log.Warn("retention", "GET /daily/status failed: " + (e is ApiException a ? a.Code : e.GetType().Name)); return Status; }
            finally { _statusLoading = false; }
        }

        /// <summary>Claims today's chest (the key is retained by the caller across a retry).</summary>
        public async Task<DailyClaim> Claim(string key)
        {
            var c = await Api.Claim(key, _life.Token);
            if (Disposed) return c;
            _writes++;
            Status = c; _statusAt = Time.realtimeSinceStartup;
            Log.Info("retention", $"claimed day {c.ClaimedDay} ${c.Claimed.CashCents / 100.0:0.00} cosmetic {c.Claimed.CosmeticKey ?? "-"} already {c.AlreadyClaimed} reset {c.Reset} freezesUsed {c.FreezesUsed}");
            Bus.Publish(new GameplayEventSignal { kind = GameplayEvents.ChestOpen, id = "daily" });   // the first session
            StatusChanged?.Invoke();
            return c;
        }

        public async Task<DailyStatus> BuyFreeze(string key)
        {
            var s = await Api.BuyFreeze(key, _life.Token);
            if (Disposed) return s;
            _writes++;
            Status = s;
            Log.Info("retention", "freeze bought: holding " + s.Freezes);
            StatusChanged?.Invoke();
            return s;
        }

        // ------------------------------------------------------------------ quests

        public async Task<QuestBoard> RefreshQuests(bool force = true)
        {
            if (Disposed || RuntimeMode.IsOfflineDemo || _questsLoading) return Board;
            if (!force && Time.realtimeSinceStartup - _questsAt < 5f) return Board;
            _questsLoading = true;
            try
            {
                var b = await Api.Quests(_life.Token);
                if (Disposed) return b;
                Board = b; _questsAt = Time.realtimeSinceStartup;
                QuestsChanged?.Invoke();
                return b;
            }
            catch (OperationCanceledException) { return Board; }
            catch (Exception e) { Log.Warn("retention", "GET /quests failed: " + (e is ApiException a ? a.Code : e.GetType().Name)); return Board; }
            finally { _questsLoading = false; }
        }

        public async Task<(bool already, long cashCents, int xp)> ClaimQuest(Quest q, string key)
        {
            var r = await Api.ClaimQuest(q.Id, key, _life.Token);
            if (Disposed) return r;
            q.Claimed = true;
            Log.Info("retention", $"quest {q.Id} claimed ${r.cashCents / 100.0:0.00} +{r.xp} XP already {r.already}");
            QuestsChanged?.Invoke();
            return r;
        }

        private void OnRaw(string type, object payload)
        {
            if (Disposed || type != "quest_progress") return;
            var p = QuestProgress.From(payload);
            if (p == null) return;
            Log.Info("retention", $"quest_progress {p.QuestId} {p.Progress}/{p.Target} done {p.Completed}");
            var q = Board?.Apply(p);
            if (q == null) { _ = RefreshAndAnnounce(p); return; }
            QuestsChanged?.Invoke();
            try { QuestMoved?.Invoke(q, Board); } catch (Exception e) { Log.Exception("retention", e); }
        }

        private async Task RefreshAndAnnounce(QuestProgress p)
        {
            var b = await RefreshQuests();
            var q = b?.Find(p.QuestId);
            if (q != null) try { QuestMoved?.Invoke(q, b); } catch (Exception e) { Log.Exception("retention", e); }
        }

        // ------------------------------------------------------------------ push

        /// <summary>Should the pre-prompt card be shown now (after a chest claim)? Once per install, only when the OS still asks.</summary>
        public static bool ShouldAskPush => PlayerPrefs.GetInt(PushAskedKey, 0) == 0 && PushBridge.NeedsPermission;

        /// <summary>The pre-prompt's "Turn on": the system dialog, then registration if granted.</summary>
        public void AskPushPermission(Action<bool> done = null)
        {
            PlayerPrefs.SetInt(PushAskedKey, 1); PlayerPrefs.Save();
            PushBridge.RequestPermission(granted =>
            {
                if (granted) { _pushStarted = false; StartPush(); }
                done?.Invoke(granted);
            });
        }

        public static void DeclinePush() { PlayerPrefs.SetInt(PushAskedKey, 1); PlayerPrefs.Save(); Log.Info("push", "pre-prompt: not now"); }

        private void StartPush()
        {
            if (_pushStarted || Disposed || RuntimeMode.IsOfflineDemo) return;
            _pushStarted = true;
            _ = SendPrefs();
            bool dev = PushBridge.DevToken != null;
            if (!PushBridge.Available && !dev) return;   // logs the disabled line once
            if (!PushBridge.PermissionGranted) { Log.Info("push", "waiting for the notification permission"); return; }
            _ = RegisterToken();
        }

        private async Task RegisterToken()
        {
            PushBridge.RequestToken();
            string token = PushBridge.DevToken;
            for (int i = 0; i < 60 && !Disposed && token == null; i++)
            {
                token = PushBridge.Token;
                if (!string.IsNullOrEmpty(token) || PushBridge.TokenError != null) break;
                try { await Awaitable.WaitForSecondsAsync(0.5f, _life.Token); } catch (OperationCanceledException) { return; }
            }
            if (string.IsNullOrEmpty(token)) { Log.Warn("push", "no FCM token: " + (PushBridge.TokenError ?? "timeout")); return; }
            // registered on every sign-in: the server moves a token to the account that registers it last
            try
            {
                await Api.RegisterPush(token, Guid.NewGuid().ToString(), _life.Token);
                PlayerPrefs.SetString(TokenKey, token); PlayerPrefs.Save();
                Log.Info("push", "FCM token registered (" + token.Substring(0, Math.Min(8, token.Length)) + "...)");
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("push", "POST /push/register failed: " + (e is ApiException a ? a.Code : e.GetType().Name)); }
        }

        /// <summary>The server's prefs with the device's current UTC offset (minutes east), so quiet hours are local.</summary>
        private async Task SendPrefs()
        {
            if (_prefsSent) return;
            try
            {
                int offset = (int)Math.Round(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes);
                var current = await Api.Prefs(_life.Token) as JObject ?? new JObject();
                var body = new JObject
                {
                    ["quietEnabled"] = current["quietEnabled"] ?? true,
                    ["quietStartHour"] = current["quietStartHour"] ?? 22,
                    ["quietEndHour"] = current["quietEndHour"] ?? 8,
                    ["tzOffsetMin"] = offset,
                    ["disabledTypes"] = current["disabledTypes"] ?? new JArray(),
                };
                if ((int?)current["tzOffsetMin"] == offset) { _prefsSent = true; Log.Info("push", "prefs: UTC offset " + offset + " min already set"); return; }
                await Api.PutPrefs(body, Guid.NewGuid().ToString(), _life.Token);
                _prefsSent = true;
                Log.Info("push", "prefs: UTC offset " + offset + " min sent");
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("push", "push prefs failed: " + (e is ApiException a ? a.Code : e.GetType().Name)); }
        }

        /// <summary>
        /// Sign-out, while the session is still valid: DELETE /push/register for this install's token (a 404 from an older
        /// server is ignored), then delete the token on the device so the next account registers a fresh one. Safe without
        /// Firebase. FlowController awaits it before revoking the session.
        /// </summary>
        public static async Task BeforeSignOut(CancellationToken ct = default)
        {
            var svc = Current;
            string token = PushBridge.DevToken ?? PushBridge.Token;
            if (string.IsNullOrEmpty(token)) token = PlayerPrefs.GetString(TokenKey, "");
            if (svc != null && !svc.Disposed && !string.IsNullOrEmpty(token))
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(svc._life.Token, ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(4));
                    await svc.Api.UnregisterPush(token, Guid.NewGuid().ToString(), cts.Token);
                    Log.Info("push", "FCM token unregistered for this account");
                }
                catch (ApiException a) when (a.Status == 404) { Log.Info("push", "unregister route not on this server (404): ignored"); }
                catch (Exception e) { Log.Warn("push", "unregister failed: " + (e is ApiException a ? a.Code : e.GetType().Name)); }
            }
            PlayerPrefs.DeleteKey(TokenKey); PlayerPrefs.Save();
            PushBridge.DeleteToken();
            if (svc != null) svc._pushStarted = false;
            Log.Info("push", "sign-out: push token " + (PushBridge.Available ? "deleted on the device" : "none (push disabled)"));
        }

        // ------------------------------------------------------------------ deep links

        private void OnFocus(bool focused) { if (focused && !Disposed) TakeLaunchLink(); }

        private void OnDeepLink(string url) { if (!Disposed) { Log.Info("push", "deep link " + url); _pendingLink = url; } }

        private void TakeLaunchLink()
        {
            var link = PushBridge.TakeLaunchLink();
            if (string.IsNullOrEmpty(link)) return;
            Log.Info("push", "opened from a notification: " + link);
            _pendingLink = link;
        }

        /// <summary>Opens the app a `ctlife://` link names (quests, a quest's deep link, a push tap).</summary>
        public static bool Route(string link)
        {
            if (VenueLinks.TryParse(link, out var venue))   // `venue:<id>` (a Sleuth exposé): travel there
            {
                Log.Info("push", "deep link " + link + " -> travel " + venue);
                return VenueLinks.Open(link);
            }
            if (VenueLinks.TryParseProfile(link, out var profileId))   // `ctlife://profile/<id>`: the profile card
            {
                Bus.Publish(new AvatarTappedSignal { userId = profileId });
                return true;
            }
            if (VenueLinks.TryParseCoin(link, out var coin))   // `ctlife://coin/<id>`: the coin in Pulse
            {
                if (VenueLinks.OpenCoin == null) return false;
                VenueLinks.OpenCoin(coin);
                return true;
            }
            var app = DeepLinks.AppFor(link);
            if (app == null) { Log.Warn("push", "unknown deep link " + link); return true; }
            if (Opener == null || !Opener(app)) return false;
            Log.Info("push", "deep link " + link + " -> " + app);
            return true;
        }

        private void PumpForeground()
        {
            var json = PushBridge.PollForeground();
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var m = JObject.Parse(json);
                string link = CityWire.Str(m["deep_link"]);
                Log.Info("push", "foreground push " + CityWire.Str(m["type"]) + " " + link);
                DuoNotificationCenter.Post(new DuoNotification
                {
                    AppId = DeepLinks.AppFor(link) ?? DeepLinks.QuestsApp,
                    ThreadKey = "push:" + CityWire.Str(m["type"]),
                    Title = CityWire.Plain(m["title"], 60) ?? "CT Life",
                    Body = CityWire.Plain(m["body"], 160) ?? "",
                    Open = () => Route(link),
                });
            }
            catch (Exception e) { Log.Warn("push", "foreground push unreadable: " + e.GetType().Name); }
        }

        // ------------------------------------------------------------------ loop

        private async Task Loop()
        {
            try
            {
                while (!Disposed)
                {
                    await Awaitable.WaitForSecondsAsync(1f, _life.Token);
                    if (_pendingLink != null && Route(_pendingLink)) _pendingLink = null;
                    PumpForeground();
                    // the UTC reset: a new chest and new quests (a second after, so the server has rolled over)
                    var s = Status;
                    if (s != null && s.NextResetAtMs > 0 && s.NextResetAtMs != _resetSeenMs && ServerClock.NowMs > s.NextResetAtMs + 1500)
                    {
                        _resetSeenMs = s.NextResetAtMs;
                        _ = RefreshStatus();
                        _ = RefreshQuests();
                    }
                    if (s == null && Time.realtimeSinceStartup - _statusAt > 30f) { _statusAt = Time.realtimeSinceStartup; _ = RefreshStatus(); }
                }
            }
            catch (OperationCanceledException) { }
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _life.Cancel();
            RawRoomMessages.Received -= OnRaw;
            Application.focusChanged -= OnFocus;
            Application.deepLinkActivated -= OnDeepLink;
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
