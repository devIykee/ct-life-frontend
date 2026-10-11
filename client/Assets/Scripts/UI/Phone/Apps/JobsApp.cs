using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Careers and shifts (feature #9): apply, start a shift (`POST /v1/shifts`), play its mini-games, wait for
    /// `endsAt`, then `POST /v1/shifts/{id}/complete` with scores (the server caps them at maxScore).
    /// </summary>
    public sealed class JobsApp : PhoneApp
    {
        public override string Id => "jobs";
        public override string Title => ShiftStrings.T("jobs");
        public override string Color => "#0fb9b1";

        private VisualElement _list;
        private int _loadVersion;
        private CancellationTokenSource _catalogCancellation;
        private bool _busy;

        public override VisualElement Open()
        {
            Closed();
            var list = _list = El.Div("ct-col");
            var page = AppUi.Page(El.Text(ShiftStrings.T("careers"), "ct-h1"), list);
            page.RegisterCallback<DetachFromPanelEvent>(e =>
            {
                if (ReferenceEquals(e.target, page) && ReferenceEquals(_list, list)) Closed();
            });
            Load();
            return page;
        }

        public override void Closed()
        {
            _loadVersion++;
            _list = null;
            _catalogCancellation?.Cancel();
        }

        private async void Load()
        {
            var list = _list;
            if (list == null) return;
            int version = ++_loadVersion;
            _catalogCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _catalogCancellation = cancellation;
            Session session = null;
            Action sessionChanged = null;
            string userId = null;
            bool OwnsView() => version == _loadVersion && ReferenceEquals(list, _list)
                && !cancellation.IsCancellationRequested && (session == null || session.UserId == userId);
            list.Clear();
            list.Add(El.Text(ShiftStrings.T("loading"), "ct-small"));
            try
            {
                session = Services.Get<Session>();
                userId = session.UserId;
                sessionChanged = () => { if (!session.IsSignedIn || session.UserId != userId) cancellation.Cancel(); };
                session.Changed += sessionChanged;
                // Read-only catalog deadline; existing paid-shift settlement behavior is unchanged.
                var careers = await ShiftReliability.Request(ct => Services.Get<Api>().Careers(ct), cancellation.Token, 30000);
                if (!OwnsView()) return;
                list.Clear();
                foreach (var c in careers)
                {
                    var info = CareerInfo.From(c.Id);
                    if (info.phase > Phone.CurrentPhase) continue;
                    var lvl = info.Level(Math.Max(1, c.Level));
                    var row = El.Div("ct-card",
                        ModDesk.Plain(c.Name ?? info.name ?? c.Id, "ct-h2"),
                        ModDesk.Plain(lvl != null ? ShiftStrings.F("offer", Money.Cash(lvl.Long("payCents")), lvl.Int("shiftSec")) : ShiftStrings.F("level", c.Level), "ct-small"));
                    var career = c;
                    var action = El.Btn(ShiftStrings.T(_busy ? "busy" : c.Active ? "work" : "apply"),
                        () => { if (career.Active) RunShift(career.Id, career.Level); else Apply(career.Id); }, "ct-btn ct-btn-primary");
                    action.SetEnabled(!_busy);
                    row.Add(action);
                    list.Add(row);
                }
                if (list.childCount == 0) list.Add(El.Text(ShiftStrings.T("empty"), "ct-body"));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception e)
            {
                if (!OwnsView()) return;
                list.Clear();
                list.Add(El.Text(e is TimeoutException ? JobsCatalogStrings.T("timeout") : AppUi.Err(e), "ct-error"));
                var retry = El.Btn(Strings.T("common.retry"), () =>
                {
                    if (version == _loadVersion && ReferenceEquals(list, _list)) Load();
                }, "ct-btn ct-btn-primary");
                retry.name = "jobs-catalog-retry";
                retry.SetEnabled(!_busy);
                list.Add(retry);
            }
            finally
            {
                if (session != null && sessionChanged != null) session.Changed -= sessionChanged;
                if (ReferenceEquals(_catalogCancellation, cancellation)) _catalogCancellation = null;
                cancellation.Dispose();
            }
        }

        private void Busy(bool value)
        {
            _busy = value;
            if (_list != null)
                foreach (var button in _list.Query<Button>().ToList()) button.SetEnabled(!value);
        }

        /// <summary>Phase and level table for a career from config `careers` (GET /careers only has id/name/level/xp/active).</summary>
        private sealed class CareerInfo
        {
            public string name;
            public int phase = 1;
            public Newtonsoft.Json.Linq.JArray levels;

            public static CareerInfo From(string careerId)
            {
                var info = new CareerInfo();
                var cfg = Services.Get<ConfigStore>().Config("careers");
                var list = cfg?["careers"] as Newtonsoft.Json.Linq.JArray;
                Newtonsoft.Json.Linq.JToken c = null;
                if (list != null) c = list.FirstOrDefault(x => x.Str("id") == careerId);
                else if (cfg?[careerId] != null) c = cfg[careerId];
                if (c == null) return info;
                info.name = c.Str("name");
                info.phase = c.Int("phase", 1);
                info.levels = c["levels"] as Newtonsoft.Json.Linq.JArray;
                return info;
            }

            public Newtonsoft.Json.Linq.JToken Level(int level) => levels?.FirstOrDefault(l => l.Int("level") == level);
        }

        private async void Apply(string careerId)
        {
            if (_busy) return;
            Busy(true);
            var session = Services.Get<Session>();
            string userId = session.UserId;
            using var cancellation = new CancellationTokenSource();
            void SessionChanged() { if (!session.IsSignedIn || session.UserId != userId) cancellation.Cancel(); }
            session.Changed += SessionChanged;
            try
            {
                var job = await ShiftReliability.Request(ct => Services.Get<Api>().ApplyCareer(careerId, ct), cancellation.Token);
                if (Services.Get<Session>().UserId != userId) return;
                if (job == null || string.IsNullOrEmpty(job.CareerId))
                    Bus.Publish(ToastSignal.Text(ShiftStrings.T("invalidJob"), ToastKind.Bad));
                else
                {
                    var title = CareerInfo.From(job.CareerId).Level(Math.Max(1, job.Level))?.Str("title") ?? job.CareerId;
                    Bus.Publish(ToastSignal.Text(ShiftStrings.F("hired", title), ToastKind.Good));
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception e)
            {
                if (Services.Get<Session>().UserId == userId)
                    Bus.Publish(ToastSignal.Text(e is TimeoutException ? e.Message : AppUi.Err(e), ToastKind.Bad));
            }
            finally { session.Changed -= SessionChanged; Busy(false); Load(); }
        }

        private async void RunShift(string careerId, int careerLevel)
        {
            if (_busy) return;
            Busy(true);
            var api = Services.Get<Api>();
            var session = Services.Get<Session>();
            string userId = session.UserId;
            using var cancellation = new CancellationTokenSource();
            void SessionChanged() { if (!session.IsSignedIn || session.UserId != userId) cancellation.Cancel(); }
            session.Changed += SessionChanged;
            ShiftRunView view = null;
            bool returnToCity = false;
            try
            {
                view = new ShiftRunView(cancellation.Cancel);
                var shift = await ShiftReliability.Request(ct => api.StartShift(careerId, ct), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                double assignmentReceivedAt = Time.realtimeSinceStartupAsDouble;
                var registry = Services.Get<ConfigStore>().Registries;
                var definitions = new List<MiniGameDef>();
                if (shift == null || string.IsNullOrEmpty(shift.ShiftId) || shift.MiniGameIds == null || shift.MiniGameIds.Count == 0 ||
                    shift.EndsAt == default || shift.MiniGameIds.Distinct(StringComparer.Ordinal).Count() != shift.MiniGameIds.Count)
                    throw new InvalidOperationException(ShiftStrings.T("invalidAssignment"));
                ShiftReadyNotice.Schedule(shift.EndsAt.Kind == DateTimeKind.Local ? shift.EndsAt.ToUniversalTime() : shift.EndsAt);   // "Shift done" at endsAt
                foreach (string id in shift.MiniGameIds)
                {
                    if (string.IsNullOrEmpty(id) || !registry.MiniGames.TryGetValue(id, out var definition) || definition.maxScore <= 0 ||
                        definition.careerId != careerId ||
                        definition.durationSec <= 0 || definition.durationSec > 600)
                        throw new InvalidOperationException(ShiftStrings.T("invalidAssignment"));
                    definitions.Add(definition);
                }
                int configuredSeconds = CareerInfo.From(careerId).Level(Math.Max(1, careerLevel))?.Int("shiftSec") ?? 0;
                double minimumEnd = assignmentReceivedAt + Math.Max(configuredSeconds, definitions.Sum(d => d.durationSec)) + 0.5;
                var results = new List<PostShiftsShiftIdCompleteRequestResultsInner>();
                view.Show(false);
                foreach (var definition in definitions)
                {
                    int score = await MiniGameHost.Play(definition, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    results.Add(new PostShiftsShiftIdCompleteRequestResultsInner(definition.id, score));
                }
                await view.WaitForEnd(shift.EndsAt, minimumEnd, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                view.Status("settling", "settlingDetail");
                // Api/RestClient generates one idempotency key and reuses it across network retries.
                PostShiftsShiftIdComplete200Response result;
                try
                {
                    result = await ShiftReliability.Request(ct => api.CompleteShift(shift.ShiftId, results, ct), cancellation.Token);
                }
                catch (ApiException e) when (e.Is("SHIFT_NOT_FINISHED"))
                {
                    // An explicit early rejection confirms no settlement. This one new request is safe;
                    // ambiguous network/timeouts are never re-submitted with a new idempotency key here.
                    view.Status("waitingForServer", "waitingDetail");
                    await view.WaitForEnd(RetryEnd(e, shift.EndsAt), Time.realtimeSinceStartupAsDouble + 2, cancellation.Token);
                    view.Status("settling", "settlingDetail");
                    result = await ShiftReliability.Request(ct => api.CompleteShift(shift.ShiftId, results, ct), cancellation.Token);
                }
                cancellation.Token.ThrowIfCancellationRequested();
                ShiftReadyNotice.Cancel();   // collected
                view.Receipt(result);
                if (result.Promoted) Bus.Publish(new SfxSignal("sfx_rank_up"));
                else Bus.Publish(new SfxSignal("sfx_ui_success"));
                try
                {
                    await ShiftReliability.Request(async ct => { await Services.Get<PlayerProfile>().Refresh(ct); return true; }, cancellation.Token, 15000);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { Log.Exception("shift-profile", e); view.BalanceRefreshFailed(); }
                await view.Dismiss(cancellation.Token);
                returnToCity = true;
            }
            catch (OperationCanceledException) { /* Sign-out or detached UI: do not submit/replay results for another user. */ }
            catch (Exception e)
            {
                Log.Exception("shift", e);
                if (!cancellation.IsCancellationRequested && view != null)
                {
                    view.Failure(e is InvalidOperationException || e is TimeoutException ? e.Message : AppUi.Err(e));
                    try { await view.Dismiss(cancellation.Token); returnToCity = true; }
                    catch (OperationCanceledException) { }
                }
            }
            finally
            {
                session.Changed -= SessionChanged;
                view?.Dispose();
                Busy(false);
                if (returnToCity) Phone.Close(); else Load();
            }
        }

        private static DateTime RetryEnd(ApiException error, DateTime fallback)
        {
            var value = error.Details?["endsAt"];
            if (value == null) return fallback;
            if (value.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
            {
                // Current backend emits epoch milliseconds; imported contract example uses an ISO string.
                try { return DateTimeOffset.FromUnixTimeMilliseconds((long)value).UtcDateTime; }
                catch (ArgumentOutOfRangeException) { return fallback; }
            }
            return DateTimeOffset.TryParse(value.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.UtcDateTime : fallback;
        }
    }

    /// <summary>
    /// Full-screen mini-game runner. The base mechanic is "tap the right ones": targets pop up, good taps score,
    /// wrong taps cost. Discord moderation uses its bespoke finite desk; other registered games retain this host.
    /// </summary>
    public static class MiniGameHost
    {
        private sealed class Theme
        {
            public string good, bad, prompt;
        }

        private static Theme ThemeFor(MiniGameDef d)
        {
            var id = d.id ?? "";
            if (id.Contains("drainer") || id.Contains("mod")) return new Theme { good = "DM: \"support here, send seed\"", bad = "DM: \"gm ser\"", prompt = "Ban the drainers. Spare the real ones." };
            if (id.Contains("verify") || id.Contains("human")) return new Theme { good = "human", bad = "bot", prompt = "Verify humans only." };
            if (id.Contains("thread") || id.Contains("write")) return new Theme { good = "1/ alpha", bad = "ratio'd take", prompt = "Pick the bangers." };
            if (id.Contains("audit") || id.Contains("bug")) return new Theme { good = "reentrancy", bad = "false alarm", prompt = "Flag real bugs." };
            return new Theme { good = "✓", bad = "✗", prompt = d.name };
        }

        public static Task<int> Play(MiniGameDef def, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            if (def.id == "discord_mod_ban") return ModDesk.Play(def, cancellation);
            if (def.id == "junior_analyst_earnings") return EarningsDesk.Play(def, cancellation);
            if (def.id == "kols_engagement") return ThreadWorkshop.Play(def, cancellation);
            var tcs = new TaskCompletionSource<int>();
            var theme = ThemeFor(def);
            var arena = El.Div("ct-mg-arena");
            var scoreLabel = El.Text("0", "ct-mg-score");
            var timeLabel = El.Text(def.durationSec + "s", "ct-mg-time");
            var root = El.Div("ct-modal-backdrop ct-mg",
                El.Div("ct-row", El.Text(def.name, "ct-h2"), El.Div("ct-grow"), scoreLabel, timeLabel),
                El.Text(theme.prompt, "ct-body"),
                arena);
            root.pickingMode = PickingMode.Position;
            UiRoot.Instance.ModalLayer.Add(root);

            int good = 0, bad = 0, goodSpawned = 0;
            bool closed = false;
            float start = Time.unscaledTime, nextSpawn = start + 0.4f;
            var rng = new System.Random();
            var live = new List<(VisualElement el, float until)>();
            Action tick = null;
            void Close(bool cancelled)
            {
                if (closed) return;
                closed = true;
                UiRoot.Instance.RemoveUpdater(tick);
                root.RemoveFromHierarchy();
                if (cancelled) tcs.TrySetCanceled(); else tcs.TrySetResult(Score(def, good, bad, goodSpawned));
            }
            root.RegisterCallback<DetachFromPanelEvent>(_ => Close(true));
            tick = () =>
            {
                if (cancellation.IsCancellationRequested) { Close(true); return; }
                float t = Time.unscaledTime - start;
                timeLabel.text = Mathf.CeilToInt(Mathf.Max(0, def.durationSec - t)) + "s";
                if (Time.unscaledTime >= nextSpawn && t < def.durationSec)
                {
                    nextSpawn = Time.unscaledTime + Mathf.Lerp(0.9f, 0.45f, t / def.durationSec);
                    bool isGood = rng.NextDouble() < 0.6;
                    if (isGood) goodSpawned++;
                    VisualElement el = null;
                    bool answered = false;
                    el = El.Btn(isGood ? theme.good : theme.bad, () =>
                    {
                        if (answered || closed || Time.unscaledTime - start >= def.durationSec) return;
                        answered = true;
                        if (isGood) good++; else bad++;
                        scoreLabel.text = Score(def, good, bad, goodSpawned).ToString();
                        el.RemoveFromHierarchy();
                    }, isGood ? "ct-mg-target ct-mg-good" : "ct-mg-target ct-mg-bad", isGood ? "sfx_ui_success" : "sfx_ui_error");
                    el.style.left = Length.Percent((float)rng.NextDouble() * 70f);
                    el.style.top = Length.Percent((float)rng.NextDouble() * 80f);
                    arena.Add(el);
                    live.Add((el, Time.unscaledTime + 1.6f));
                }
                for (int i = live.Count - 1; i >= 0; i--)
                    if (Time.unscaledTime > live[i].until) { live[i].el.RemoveFromHierarchy(); live.RemoveAt(i); }
                if (t >= def.durationSec)
                {
                    Close(false);
                }
            };
            UiRoot.Instance.AddUpdater(tick);
            return tcs.Task;
        }

        private static int Score(MiniGameDef d, int good, int bad, int goodSpawned)
        {
            int availableGood = Mathf.Max(1, goodSpawned);
            float r = Mathf.Clamp01((good - bad * 0.5f) / availableGood);
            return Mathf.RoundToInt(r * d.maxScore);
        }
    }
}
