using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CTLife.App;
using CTLife.Core;
using CTLife.Net;
using CTLife.Realtime;
using CTLife.UI;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace CTLife.Tests.Integration
{
    /// <summary>
    /// Drives the real game flow in the editor against a running backend (tools/integration/run_editor_integration.sh):
    /// dev sign-in → lottery → creator → city/market → Timeline Square → sees the observer bot move/emote/chat and is
    /// seen by it → a shift pays Cash → block/report/unblock → refreshed token still joins → Terminal Café guard.
    /// Writes a JSON receipt and exits the editor with 0 (all pass) or 1.
    ///
    /// Unity -batchmode -projectPath client -executeMethod CTLife.Tests.Integration.IntegrationRun.Launch
    ///       -ctlife-env local -ctlife-autologin dev -ctlife-autocreate 1 -ctlife-autoenter 1
    ///       -ctlife-botStatus /path/bot.json -ctlife-integrationOut /path/receipt.json
    /// </summary>
    [InitializeOnLoad]
    public static class IntegrationRun
    {
        private const string ActiveKey = "ctlife.integration.active";
        private static readonly List<JObject> Checks = new List<JObject>();
        private static readonly List<JObject> Features = new List<JObject>();
        private static string _out;
        private static double _startupDeadline;
        private static bool _started, _driving;

        /// <summary>Entering play mode reloads the domain, which drops static handlers; resume from SessionState.</summary>
        static IntegrationRun()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            _out = LaunchArgs.Get("integrationOut", Path.GetFullPath("../integration-receipt.json"));
            if (EditorApplication.isPlaying) EditorApplication.delayCall += Drive;
            else EditorApplication.playModeStateChanged += OnPlayMode;
        }

        public static void Launch()
        {
            SessionState.SetBool(ActiveKey, true);
            _out = LaunchArgs.Get("integrationOut", Path.GetFullPath("../integration-receipt.json"));
            _startupDeadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update += WaitForEditor;
            Debug.Log("CTLIFE [integration] waiting for imports/compilation");
        }

        private static void WaitForEditor()
        {
            if (EditorApplication.timeSinceStartup > _startupDeadline) { EditorApplication.update -= WaitForEditor; Finish("editor not ready in 180 s"); return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || _started) return;
            _started = true;
            EditorApplication.update -= WaitForEditor;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("IntegrationBootstrap").AddComponent<GameBootstrap>();
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            Drive();
        }

        private static async void Drive()
        {
            if (_driving) return;
            _driving = true;
            Debug.Log("CTLIFE [integration] play mode entered; driving the real flow");
            string fatal = null;
            try { await Steps(); }
            catch (Exception e) { fatal = e.GetType().Name + ": " + e.Message; Debug.LogException(e); }
            Finish(fatal);
        }

        private static async Task<bool> Until(Func<bool> condition, float seconds, float poll = 0.25f)
        {
            var end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                try { if (condition()) return true; } catch { /* not ready */ }
                await Task.Delay((int)(poll * 1000));
            }
            try { return condition(); } catch { return false; }
        }

        private static void Check(string name, bool pass, string detail)
        {
            Checks.Add(new JObject { ["name"] = name, ["pass"] = pass, ["detail"] = detail, ["t"] = Math.Round(Time.realtimeSinceStartup, 1) });
            Debug.Log($"CTLIFE [integration] {(pass ? "PASS" : "FAIL")} {name}: {detail}");
        }

        private static void Blocked(string name, string item, string detail)
        {
            Checks.Add(new JObject { ["name"] = name, ["pass"] = true, ["blocked"] = item, ["detail"] = detail, ["t"] = Math.Round(Time.realtimeSinceStartup, 1) });
            Debug.Log($"CTLIFE [integration] BLOCKED {name} [backend #{item}]: {detail}");
        }

        private static JObject Bot()
        {
            var path = LaunchArgs.Get("botStatus");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try { return JObject.Parse(File.ReadAllText(path)); } catch { return null; }
        }

        private static async Task Steps()
        {
            var session = Services.Get<Session>();
            bool signed = await Until(() => session.IsSignedIn && !string.IsNullOrEmpty(session.UserId), 45);
            if (!signed && UnityEngine.Object.FindAnyObjectByType<FlowController>() == null) Check("flow_started", false, "no FlowController");
            Check("sign_in_dev", signed, $"user={session.UserId} wallet={session.Wallet} env={Env.Current.id} rest={Env.Current.restBaseUrl}");
            if (!signed) return;

            var profile = Services.Get<PlayerProfile>();
            bool onboarded = await Until(() => profile.HasCharacter, 90);
            Check("lottery_then_character", onboarded && profile.LotteryRolled,
                $"alias={profile.Alias} cash={profile.CashCents} debt={profile.DebtCents} rolled={profile.LotteryRolled}");
            if (!onboarded) return;

            var rt = Services.Get<IRealtimeService>();
            bool rooms = await Until(() => rt.City != null && rt.City.IsJoined && rt.Market != null && rt.Market.IsJoined, 30);
            Check("city_and_market_joined", rooms, $"city={rt.City?.InstanceId} market={rt.Market?.InstanceId} online={rt.City?.Online}");

            bool inVenue = await Until(() => VenueSession.Current != null && VenueSession.Current.IsActive && VenueSession.Current.Channel != null, 60);
            var visit = VenueSession.Current;
            Check("venue_joined", inVenue, $"venue={visit?.VenueId} room={visit?.Channel?.InstanceId} roomVenue={visit?.Channel?.VenueId}");
            if (!inVenue) return;
            bool npcs = await Until(() => visit.Npcs.Count > 0, 15);
            Check("server_npcs_visible", npcs, "npcs=" + string.Join(",", visit.Npcs.Values.Select(n => n != null ? n.name : "?")));

            // Second client: the observer bot.
            bool seeBot = await Until(() => visit.Players.Count > 0, 45);
            var botView = visit.Players.Values.FirstOrDefault();
            Vector3 first = botView != null ? botView.transform.position : Vector3.zero;
            await Task.Delay(2500);
            float moved = botView != null ? Vector3.Distance(first, botView.transform.position) : 0f;
            Check("sees_other_player_move", seeBot && moved > 0.05f, $"remotePlayers={visit.Players.Count} movedMetres={moved:0.00}");

            int saidFromOthers = 0;
            var subs = new Subscriptions();
            subs.On<SaidMessage>(m => { if (m.sessionId != visit.Channel?.SessionId) saidFromOthers++; });
            bool heard = await Until(() => saidFromOthers > 0, 15);
            Check("receives_chat", heard, "said messages from others=" + saidFromOthers);
            subs.Dispose();

            // Our actions, observed by the bot.
            var channel = visit.Channel;
            string text = "integration " + DateTime.UtcNow.ToString("HHmmss");
            await Task.Delay(1100);
            bool emoted = channel.Emote("emote_wave");
            visit.LocalPlayer?.GetComponent<AvatarView>()?.PlayOneShot("emote_wave");
            await Task.Delay(2100);
            float wait = channel.Say(text);
            Vector3 start = visit.LocalPlayer.transform.position;
            var target = start + new Vector3(1.0f, 0, 0.5f);
            visit.LocalPlayer.transform.position = target; // the controller publishes the new position next frame
            await Task.Delay(1500);
            var mySession = channel.SessionId;
            bool botSaw = await Until(() =>
            {
                var b = Bot();
                var me = b?["players"]?[mySession];
                return me != null && b["said"].Any(s => (string)s["text"] == text) && me["emotesSeen"].Any(e => (string)e == "emote_wave");
            }, 10);
            var mine = Bot()?["players"]?[mySession];
            Check("seen_by_other_client", botSaw, $"emoteSent={emoted} sayWait={wait} botSeesMe={mine?.ToString(Newtonsoft.Json.Formatting.None)}");

            // Work: a shift pays Cash (REST through the client's own Api/transport/models).
            var api = Services.Get<Api>();
            try
            {
                long before = (await api.GetMe()).CashCents;
                var careers = await api.Careers();
                var career = careers.FirstOrDefault();
                string applied = "", started = "", completed = "";
                try { applied = Json.Serialize(await api.ApplyCareer(career.Id)); } catch (ApiException e) { applied = e.Code; }
                var shift = await api.StartShift(career.Id);
                started = shift.ShiftId + " endsAt=" + shift.EndsAt.ToString("o");
                // The server refuses completion before the shift's duration (SHIFT_NOT_FINISHED).
                var waitMs = (int)Math.Min(90000, Math.Max(500, (shift.EndsAt.ToUniversalTime() - DateTime.UtcNow).TotalMilliseconds + 1500));
                await Task.Delay(waitMs);
                var games = shift.MiniGameIds ?? new List<string>();
                var results = games.Select(g => new Rest.Model.PostShiftsShiftIdCompleteRequestResultsInner(g, 1000)).ToList();
                var done = await api.CompleteShift(shift.ShiftId.ToString(), results);
                completed = Json.Serialize(done);
                long after = (await api.GetMe()).CashCents;
                Check("shift_pays_cash", after > before, $"career={career.Id} apply={applied} shift={started} complete={completed} cash {before}->{after}");
            }
            catch (Exception e) { Check("shift_pays_cash", false, e.GetType().Name + ": " + e.Message); }

            // Safety: block, report and unblock the bot by its real user id.
            var botUser = (string)Bot()?["userId"];
            try
            {
                await api.Block(botUser);
                await api.Report("user", botUser, "spam", "integration test");
                await api.Unblock(botUser);
                Check("block_report_unblock", !string.IsNullOrEmpty(botUser), "bot user " + botUser);
            }
            catch (Exception e) { Check("block_report_unblock", false, e.GetType().Name + ": " + e.Message); }

            // Token refresh, then a fresh room join with the new token.
            var auth = Services.Get<IAuthService>();
            var oldToken = session.AccessToken;
            bool refreshed = await auth.Refresh();
            Check("token_refresh", refreshed && session.AccessToken != oldToken, $"refreshed={refreshed} rotated={session.AccessToken != oldToken}");

            // Venue isolation: Terminal Café gets its own room, not Timeline Square's (server filterBy venueId, item 39).
            try
            {
                var squareRoom = VenueSession.Current?.Channel?.InstanceId;
                var cafe = await VenueSession.Enter("terminal_cafe", joinRoom: true);
                for (int i = 0; i < 40 && cafe.Channel != null && string.IsNullOrEmpty(cafe.Channel.VenueId); i++)
                    await Awaitable.WaitForSecondsAsync(0.05f);
                Check("venue_isolation", cafe.Channel != null && cafe.Channel.VenueId == "terminal_cafe" && cafe.Channel.InstanceId != squareRoom,
                    $"cafe room {cafe.Channel?.InstanceId} venue={cafe.Channel?.VenueId}; square room was {squareRoom}");
            }
            catch (Exception e)
            {
                Check("venue_isolation", false, e.GetType().Name + ": " + e.Message);
                try { await VenueSession.Enter("timeline_square", joinRoom: true); } catch { /* best effort */ }
            }
            var again = VenueSession.Current;
            Check("rejoin_after_refresh", again != null && again.Channel != null && again.Channel.IsJoined,
                $"venue={again?.VenueId} room={again?.Channel?.InstanceId}");

            // Feature pass through the lanes' own APIs (same code as the device run). Blocked = backend gap, not a client failure.
            var rows = await CTLife.App.Dev.FeatureProbe.Run(botUser, default);
            Features.Clear();
            foreach (var r in rows) Features.Add(r.ToJson());

            // Phase 5/6 apps through the real catalog and phone: each must open without an error; a route this backend lacks
            // must say "Not available yet" (n/a), never show invented data.
            Features.AddRange(await OpenApps("ads", "builder", "vc", "league", "beef", "farm", "cabal", "npcchat", "xlink"));
        }

        private static async Task<List<JObject>> OpenApps(params string[] ids)
        {
            var rows = new List<JObject>();
            var layer = UiRoot.Instance != null ? UiRoot.Instance.PhoneLayer : null;
            if (layer == null) { rows.Add(AppRow("apps", "fail", "no phone layer")); return rows; }
            var phone = new PhoneShell(layer);
            try
            {
                PhoneCatalog.Register(phone, _ => { }, () => { });
                foreach (var id in ids)
                {
                    var errors = new List<string>();
                    void OnLog(string msg, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Error) errors.Add(msg); }
                    Application.logMessageReceived += OnLog;
                    try { phone.Open(id); await Task.Delay(4000); }
                    catch (Exception e) { errors.Add(e.GetType().Name + ": " + e.Message); }
                    finally { Application.logMessageReceived -= OnLog; }
                    var text = string.Join(" | ", layer.Query<TextElement>().ToList()
                        .Where(t => t.resolvedStyle.display != DisplayStyle.None && !string.IsNullOrWhiteSpace(t.text)).Select(t => t.text.Trim()));
                    bool notYet = text.IndexOf("Not available yet", StringComparison.OrdinalIgnoreCase) >= 0;
                    string result = errors.Count > 0 ? "fail" : notYet ? "n/a" : "pass";
                    rows.Add(AppRow("app." + id, result, (errors.Count > 0 ? "ERROR " + errors[0] + " || " : "") + text));
                    phone.Close();
                }
            }
            finally { phone.Dispose(); }
            return rows;
        }

        private static JObject AppRow(string feature, string result, string detail)
        {
            if (detail != null && detail.Length > 400) detail = detail.Substring(0, 400);
            Debug.Log($"CTLIFE [integration] {feature}: {result} {detail}");
            return new JObject { ["feature"] = feature, ["result"] = result, ["detail"] = detail, ["item"] = null };
        }

        private static void Finish(string fatal)
        {
            bool pass = fatal == null && Checks.Count > 0 && Checks.All(c => (bool)c["pass"]);
            var receipt = new JObject
            {
                ["at"] = DateTime.UtcNow.ToString("o"), ["pass"] = pass, ["fatal"] = fatal, ["checks"] = new JArray(Checks),
                ["features"] = new JArray(Features),
                ["env"] = Env.Current?.id.ToString(), ["unity"] = Application.unityVersion,
            };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_out) ?? ".");
                File.WriteAllText(_out, receipt.ToString());
            }
            catch (Exception e) { Debug.LogException(e); }
            Debug.Log($"CTLIFE [integration] RESULT {(pass ? "PASS" : "FAIL")} ({Checks.Count(c => (bool)c["pass"])}/{Checks.Count}) receipt {_out}");
            SessionState.SetBool(ActiveKey, false);
            if (LaunchArgs.Flag("integrationKeepOpen")) return;
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
