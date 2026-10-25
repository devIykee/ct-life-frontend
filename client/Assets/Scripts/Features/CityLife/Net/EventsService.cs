using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.CityLife
{
    /// <summary>
    /// The real-world event calendar for the signed-in player (lane B, 2026-10-24, backend PR #11):
    /// - GET /events/current and /events/upcoming on start, every few minutes, at the next start/end boundary and on the city
    ///   room's `event_live` / `event_ended`; the clock offset comes from their `serverNow` (countdowns never trust the device);
    /// - GET /events/{slug} for Breakpoint 2026 (skin, stream link, the quest chain and the caller's progress);
    /// - POST /events/{slug}/progress for booths, nods and trivia, with a retained Idempotency-Key per proof;
    /// - the proof ids this player already sent (the server only returns counts), kept per user and event.
    /// One per signed-in user, owned by <see cref="EventsOverlays"/>.
    /// </summary>
    public sealed class EventsService : IDisposable
    {
        public const string AppId = "events";
        public const double RefreshSeconds = 180;

        public static EventsService Current { get; private set; }
        /// <summary>Opens a Duo app (FlowController sets it to the phone).</summary>
        public static Action<string> Opener;
        /// <summary>Closes the phone (FlowController).</summary>
        public static Action Closer;

        public string UserId { get; }
        public List<CalendarEvent> Live { get; private set; } = new List<CalendarEvent>();
        public List<CalendarEvent> Upcoming { get; private set; } = new List<CalendarEvent>();
        /// <summary>Breakpoint 2026 with the caller's quest (null until read, or when the server does not list it).</summary>
        public EventDetails Breakpoint { get; private set; }
        public bool Loaded { get; private set; }
        public string LastError { get; private set; }
        public bool Disposed { get; private set; }

        /// <summary>Server time from the last calendar read's `serverNow` (falls back to <see cref="ServerClock"/>).</summary>
        public long NowMs => _offsetKnown ? (long)(ServerClock.LocalNowMs + _offsetMs) : (long)ServerClock.NowMs;

        public event Action Changed;
        /// <summary>A proof was counted (step, proof id, accepted, result).</summary>
        public event Action<string, string, EventProgressResult> Progressed;
        /// <summary>The call that completed the chain and paid (the claim moment).</summary>
        public event Action<EventDetails> Completed;

        private readonly IRestClient _rest;
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly HashSet<string> _done = new HashSet<string>();
        private readonly Dictionary<string, string> _keys = new Dictionary<string, string>();
        private double _offsetMs;
        private bool _offsetKnown, _loading, _again;
        private float _loadedAt = -1e9f;

        public EventsService(string userId)
        {
            UserId = userId;
            _rest = Services.Get<IRestClient>();
            Current = this;
            LoadDone();
            LoadIrl();
            RawRoomMessages.Received += OnRaw;
            Refresh();
            _ = Loop();
        }

        public CancellationToken Life => _life.Token;

        /// <summary>The event whose skin dresses the Conference Center now: live Breakpoint, else the upcoming one.</summary>
        public CalendarEvent SkinEvent
        {
            get
            {
                long now = NowMs;
                var bp = Breakpoint?.Event ?? Live.Concat(Upcoming).FirstOrDefault(e => e.Slug == EventIds.Breakpoint);
                if (bp != null && bp.SkinId == EventIds.LondonSkin && bp.EndsAtMs > now) return bp;
                return null;
            }
        }

        public bool BreakpointLive => SkinEvent is CalendarEvent e && e.IsLive(NowMs);

        // ------------------------------------------------------------------ reads

        public async void Refresh()
        {
            if (Disposed || RuntimeMode.IsOfflineDemo) return;
            if (_loading) { _again = true; return; }
            _loading = true;
            try
            {
                do
                {
                    _again = false;
                    var cur = await _rest.Get<JToken>("/events/current", _life.Token);
                    var up = await _rest.Get<JToken>("/events/upcoming?limit=10", _life.Token);
                    if (Disposed) return;
                    var live = CalendarEvent.ParseList(cur, out long n1);
                    var upcoming = CalendarEvent.ParseList(up, out long n2);
                    long serverNow = Math.Max(n1, n2);
                    if (serverNow > 0) { _offsetMs = serverNow - ServerClock.LocalNowMs; _offsetKnown = true; }
                    Live = live; Upcoming = upcoming.OrderBy(e => e.StartsAtMs).ToList();
                    var bp = live.Concat(upcoming).FirstOrDefault(e => e.Slug == EventIds.Breakpoint);
                    if (bp != null)
                    {
                        var d = EventDetails.Parse(await _rest.Get<JToken>("/events/" + EventIds.Breakpoint, _life.Token));
                        if (Disposed) return;
                        if (d != null) { Breakpoint = d; SyncFromServer(d); }
                    }
                    else Breakpoint = null;
                    Loaded = true; LastError = null;
                    _loadedAt = Time.realtimeSinceStartup;
                    Log.Info("events", $"calendar: {Live.Count} live, {Upcoming.Count} upcoming; breakpoint {(Breakpoint == null ? "none" : Breakpoint.Event.IsLive(NowMs) ? "live" : "upcoming")}, offset {_offsetMs:F0} ms");
                    Raise();
                } while (_again && !Disposed);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                LastError = e is ApiException a ? a.Code : e.GetType().Name;
                Log.Warn("events", "calendar read failed: " + LastError);
                _loadedAt = Time.realtimeSinceStartup - (float)RefreshSeconds + 30f;     // try again in 30 s, not every tick
                Raise();
            }
            finally { _loading = false; }
        }

        private async Task Loop()
        {
            try
            {
                while (!Disposed)
                {
                    await Awaitable.WaitForSecondsAsync(5f, _life.Token);
                    bool stale = Time.realtimeSinceStartup - _loadedAt > RefreshSeconds;
                    // a boundary passed since the last read (the realtime sweep can be a minute late; we re-read ourselves)
                    long now = NowMs;
                    bool crossed = Live.Any(e => now >= e.EndsAtMs) || Upcoming.Any(e => now >= e.StartsAtMs);
                    if ((stale || crossed) && !_loading && Time.realtimeSinceStartup - _loadedAt > 8f) Refresh();
                }
            }
            catch (OperationCanceledException) { }
        }

        private void OnRaw(string type, object payload)
        {
            if (Disposed) return;
            if (type == "irl_checkin")
            {
                if (payload is IDictionary<string, object> d && d.TryGetValue("alias", out var al) && al is string alias && alias.Length > 0 && alias.Length <= 40)
                {
                    IrlAliases.Add(alias);
                    Log.Info("events", "irl_checkin " + alias);
                    _ = LoadIrlBoard(force: true);
                }
                return;
            }
            var s = EventSignal.From(type, payload);
            if (s == null) return;
            Log.Info("events", $"{type} {s.Slug} skin {s.SkinId}");
            string title = string.IsNullOrEmpty(s.Title) ? s.Slug : s.Title;
            DuoNotificationCenter.Post(new DuoNotification
            {
                AppId = AppId, ThreadKey = "event:" + s.Slug,
                Title = EventText.F(s.Live ? "live.toast" : "ended.toast", title),
                Body = s.Live ? EventText.T("where") : null,
                Open = () => Opener?.Invoke(AppId),
            });
            Refresh();
        }

        // ------------------------------------------------------------------ IRL check-in (PR #11, 7d67a39)

        /// <summary>This player's check-in, as this phone saw it (the server has no read for it yet).</summary>
        public bool IrlCheckedIn { get; private set; }
        public bool IrlVisible { get; private set; }
        public List<IrlAttendee> IrlBoard { get; private set; } = new List<IrlAttendee>();
        /// <summary>Aliases known to be checked in and listed (the board plus `irl_checkin`), for the nameplate tag.</summary>
        public readonly HashSet<string> IrlAliases = new HashSet<string>(StringComparer.Ordinal);
        public event Action<IrlCheckinResult> CheckedIn;
        private float _boardAt = -1e9f;
        private bool _boardLoading;

        private string IrlKey => "events.irl." + EventIds.Breakpoint + "." + UserId;

        private void LoadIrl()
        {
            try { var s = PlayerPrefs.GetString(IrlKey, ""); IrlCheckedIn = s.StartsWith("1", StringComparison.Ordinal); IrlVisible = s == "1v"; }
            catch (Exception e) { Log.Debug("events", "prefs: " + e.Message); }
        }

        private void SaveIrl()
        {
            try { PlayerPrefs.SetString(IrlKey, IrlCheckedIn ? (IrlVisible ? "1v" : "1") : ""); PlayerPrefs.Save(); }
            catch (Exception e) { Log.Debug("events", "prefs: " + e.Message); }
        }

        /// <summary>POST /events/{slug}/checkin with one fix; the coordinates leave the phone in this call only.</summary>
        public async Task<IrlCheckinResult> CheckIn(IrlFix fix)
        {
            var body = new JObject { ["lat"] = Math.Round(fix.Lat, 6), ["lng"] = Math.Round(fix.Lng, 6), ["accuracyM"] = Math.Round(fix.AccuracyM, 1), ["isMock"] = fix.IsMock };
            var reply = await _rest.Send<JToken>("POST", "/events/" + EventIds.Breakpoint + "/checkin", body.ToString(Formatting.None), true, Guid.NewGuid().ToString("N"), _life.Token);
            var r = IrlCheckinResult.Parse(reply) ?? throw new ApiException(0, "BAD_REPLY", "Unexpected check-in reply");
            IrlCheckedIn = r.CheckedIn; IrlVisible = r.Visible;
            SaveIrl();
            Log.Info("events", $"IRL checked in: first {r.FirstCheckin}, visible {r.Visible}, reward {r.RewardCents} + {string.Join(",", r.RewardItems)}");
            try { CheckedIn?.Invoke(r); } catch (Exception e) { Log.Exception("events", e); }
            Raise();
            return r;
        }

        public async Task SetIrlVisible(bool on)
        {
            var reply = await _rest.Send<JToken>("PUT", "/events/" + EventIds.Breakpoint + "/checkin/visibility", new JObject { ["visible"] = on }.ToString(Formatting.None), true, Guid.NewGuid().ToString("N"), _life.Token);
            IrlVisible = CityWire.BoolOr(reply?["visible"], on);
            SaveIrl();
            Log.Info("events", "IRL board visibility " + IrlVisible);
            _ = LoadIrlBoard(force: true);
            Raise();
        }

        /// <summary>GET /events/{slug}/irl (first page, 30), at most every 20 s unless forced.</summary>
        public async Task LoadIrlBoard(bool force = false)
        {
            if (Disposed || _boardLoading || (!force && Time.realtimeSinceStartup - _boardAt < 20f)) return;
            _boardLoading = true;
            try
            {
                var r = await _rest.Get<JToken>("/events/" + EventIds.Breakpoint + "/irl?limit=30", _life.Token);
                if (Disposed) return;
                IrlBoard = IrlAttendee.ParseList(r, out _);
                foreach (var a in IrlBoard) IrlAliases.Add(a.Alias);
                _boardAt = Time.realtimeSinceStartup;
                Raise();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { _boardAt = Time.realtimeSinceStartup; Log.Warn("events", "GET irl board failed: " + (e is ApiException a ? a.Code : e.GetType().Name)); }
            finally { _boardLoading = false; }
        }

        /// <summary>Player-facing line for a failed check-in (CHECKIN_REJECTED reasons, rate limit, location errors).</summary>
        public static string ExplainCheckin(Exception e, string locationError = null)
        {
            if (locationError != null) return EventText.T("irl.err." + locationError);
            if (e is ApiException a)
            {
                if (a.Code == "CHECKIN_REJECTED") return EventText.T("irl.err." + (Reason(a) ?? "not_available"));
                if (a.Code == "RATE_LIMITED" || a.Status == 429) return EventText.T("irl.err.rate");
                if (a.Code == "EVENT_NOT_LIVE") return EventText.T("notLive");
                return a.UserMessage;
            }
            return EventText.T("failed");
        }

        // ------------------------------------------------------------------ progress

        public bool HasDone(string proofId) => _done.Contains(proofId);
        public int DoneCount(string prefix) => _done.Count(x => x.StartsWith(prefix, StringComparison.Ordinal));

        /// <summary>POST /events/{slug}/progress. Throws ApiException (EVENT_STEP_INVALID, EVENT_NOT_LIVE, RATE_LIMITED).</summary>
        public async Task<EventProgressResult> Progress(string step, string proofId, string answer = null)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(EventsService));
            if (!EventIds.SafeProof(proofId)) throw new ArgumentException("bad proof id");
            var proof = new JObject { ["id"] = proofId };
            if (!string.IsNullOrEmpty(answer)) proof["answer"] = answer;
            var body = new JObject { ["step"] = step, ["proof"] = proof };
            // one key per proof (and answer): a retry after a lost reply replays, it never double-counts
            var k = step + "|" + proofId + "|" + answer;
            if (!_keys.TryGetValue(k, out var key)) _keys[k] = key = Guid.NewGuid().ToString("N");
            JToken reply;
            try
            {
                reply = await _rest.Send<JToken>("POST", "/events/" + EventIds.Breakpoint + "/progress", body.ToString(Formatting.None), true, key, _life.Token);
            }
            catch (ApiException e) when (!e.IsNetwork && e.Status >= 400 && e.Status < 500 && DropKey(k))
            {
                throw;   // never reached: DropKey returns false so the trivia handler below still runs
            }
            catch (ApiException e) when (step == EventIds.StepTrivia && (e.Code == "EVENT_STEP_INVALID" || e.Code == "TRIVIA_COOLDOWN"))
            {
                var q = Breakpoint?.Quest?.Find(step)?.Questions.Find(x => x.Id == proofId);
                var reason = Reason(e);
                if (reason == "already_answered")
                {
                    // right before (or an older server that closed the question on any answer)
                    MarkDone(proofId);
                    if (q != null) q.Answered = true;
                }
                else if (q != null)
                {
                    // PR #18: a wrong choice is remembered and the question cools down (60 s, 10 min, 60 min)
                    if (reason == "wrong_answer" || reason == "already_tried") { if (!string.IsNullOrEmpty(answer)) q.Tried.Add(answer); foreach (var c in TriviaErrors.Tried(e.Details)) q.Tried.Add(c); }
                    long at = TriviaErrors.RetryAtMs(e.Details, NowMs);
                    if (at > 0) q.RetryAtMs = at;
                }
                Raise();
                throw;
            }
            var r = EventProgressResult.Parse(reply) ?? throw new ApiException(0, "BAD_REPLY", "Unexpected progress reply");
            MarkDone(proofId);
            if (Breakpoint != null) Breakpoint.Quest = r.Quest;
            Log.Info("events", $"progress {step} {proofId}: accepted {r.Accepted}, {string.Join(" ", r.Quest.Steps.Select(s => s.Step + " " + s.Done + "/" + s.Required))}{(r.JustCompleted ? " COMPLETED" : "")}");
            try { Progressed?.Invoke(step, proofId, r); } catch (Exception ex) { Log.Exception("events", ex); }
            if (r.JustCompleted && Breakpoint != null) try { Completed?.Invoke(Breakpoint); } catch (Exception ex) { Log.Exception("events", ex); }
            Raise();
            return r;
        }

        /// <summary>A refused request (4xx) was answered for good: the next try of the same proof is a new request with a
        /// new key (replaying the old key got 409 IN_FLIGHT). Returns false so it can sit in an exception filter.</summary>
        private bool DropKey(string k) { _keys.Remove(k); return false; }

        public static string Reason(ApiException e) => CityWire.Str(e?.Details?["reason"]);

        /// <summary>PR #18 restore state: the booths and nods done, the trivia answered right and the IRL check-in come from
        /// the server (another phone, a reinstall); the local list is only a fallback for an older server.</summary>
        private void SyncFromServer(EventDetails d)
        {
            if (d?.Irl == null) return;
            var server = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in d.Quest.Steps)
            {
                foreach (var id in s.DoneIds) server.Add(id);
                foreach (var q in s.Questions) if (q.Answered) server.Add(q.Id);
            }
            if (!server.SetEquals(_done))
            {
                _done.Clear();
                foreach (var id in server) _done.Add(id);
                try { PlayerPrefs.SetString(DoneKey, string.Join(",", _done)); PlayerPrefs.Save(); } catch (Exception e) { Log.Debug("events", "prefs: " + e.Message); }
            }
            IrlCheckedIn = d.Irl.CheckedIn; IrlVisible = d.Irl.Visible;
            SaveIrl();
        }

        /// <summary>POST /events/{slug}/irl/{handle}/dm (PR #18): an ordinary DM to a listed attendee (403 without a check-in,
        /// 404 for an unknown, hidden, own or blocked handle, 10 an hour).</summary>
        public async Task SayHi(string handle, string text)
        {
            if (string.IsNullOrEmpty(handle)) throw new ArgumentException("no handle");
            var body = new JObject { ["text"] = text }.ToString(Formatting.None);
            await _rest.Send<JToken>("POST", "/events/" + EventIds.Breakpoint + "/irl/" + Uri.EscapeDataString(handle) + "/dm", body, true, Guid.NewGuid().ToString("N"), _life.Token);
            Log.Info("events", "IRL hello sent");
        }

        public static string ExplainDm(Exception e)
        {
            if (e is ApiException a)
            {
                if (a.Status == 429 || a.Code == "RATE_LIMITED") return EventText.T("irl.err.dmRate");
                if (a.Status == 403) return EventText.T("irl.err.needCheckin");
                return EventText.T("irl.err.dm");
            }
            return EventText.T("failed");
        }

        public static string ExplainDm(CityError e)
        {
            if (e == null) return EventText.T("failed");
            if (e.Is("RATE_LIMITED")) return EventText.T("irl.err.dmRate");
            if (e.Is("FORBIDDEN")) return EventText.T("irl.err.needCheckin");
            return EventText.T("irl.err.dm");
        }

        /// <summary>Player-facing line for a failed progress call.</summary>
        public string Explain(Exception e)
        {
            if (e is ApiException a)
            {
                if (a.Code == "EVENT_NOT_LIVE") return EventText.T("notLive");
                if (a.Code == "TRIVIA_COOLDOWN") return EventText.F("trivia.cooldown", EventWire.Clock(TriviaErrors.RetryAtMs(a.Details, NowMs) - NowMs));
                if (a.Code == "RATE_LIMITED" || a.Status == 429) return EventText.T("rateLimited");
                if (a.Code == "EVENT_STEP_INVALID")
                {
                    var reason = Reason(a);
                    if (reason == "step_locked")
                    {
                        var q = Breakpoint?.Quest;
                        var prev = q?.Current;
                        return EventText.F("step.locked", prev?.Title ?? "the step before");
                    }
                    if (reason == "already_answered") return EventText.T("trivia.answered");
                    if (reason == "wrong_answer" || reason == "already_tried") return EventText.T("trivia.wrong");
                }
                return a.UserMessage;
            }
            return EventText.T("failed");
        }

        private void MarkDone(string id)
        {
            if (!_done.Add(id)) return;
            try { PlayerPrefs.SetString(DoneKey, string.Join(",", _done)); PlayerPrefs.Save(); }
            catch (Exception e) { Log.Debug("events", "prefs: " + e.Message); }
        }

        private string DoneKey => "events.done." + EventIds.Breakpoint + "." + UserId;

        private void LoadDone()
        {
            try
            {
                var s = PlayerPrefs.GetString(DoneKey, "");
                foreach (var id in s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) if (EventIds.SafeProof(id)) _done.Add(id);
            }
            catch (Exception e) { Log.Debug("events", "prefs: " + e.Message); }
        }

        private void Raise()
        {
            try { Changed?.Invoke(); } catch (Exception e) { Log.Exception("events", e); }
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _life.Cancel();
            RawRoomMessages.Received -= OnRaw;
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
