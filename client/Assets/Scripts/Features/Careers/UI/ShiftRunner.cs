using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace CTLife.Careers
{
    /// <summary>Request deadline that never retries a write by itself (an expired write may still land).</summary>
    internal static class Deadline
    {
        public static async Task<T> Run<T>(Func<CancellationToken, Task<T>> op, CancellationToken ct, int ms = 45000)
        {
            ct.ThrowIfCancellationRequested();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var request = op(linked.Token);
            var timer = Task.Delay(ms, linked.Token);
            try
            {
                if (await Task.WhenAny(request, timer) != request)
                {
                    linked.Cancel();
                    _ = request.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    ct.ThrowIfCancellationRequested();
                    throw new TimeoutException(CareersStrings.T("shift.requestTimeout"));
                }
                return await request;
            }
            finally { linked.Cancel(); }
        }

        /// <summary>Unknown outcome: the request may have been processed (network loss, timeout, 5xx, busy).</summary>
        public static bool Ambiguous(Exception e) =>
            e is TimeoutException || e is ApiException a && (a.IsNetwork || a.Status == 0 || a.Status >= 500 || a.Is("SERVER_BUSY"));

        public static string Describe(Exception e)
        {
            if (e is ApiException a) return Strings.Error(a.Code, a.ServerMessage);
            if (e is InvalidOperationException && e.Message.Contains(".")) return CareersStrings.T(e.Message);
            if (e is TimeoutException) return CareersStrings.T("shift.requestTimeout");
            return Strings.T("common.error");
        }
    }

    /// <summary>
    /// Runs one paid shift end to end against the contract: POST /shifts → play the assigned games with seeds derived
    /// from (shiftId, miniGameId, index) → wait for endsAt → POST /shifts/{id}/complete → receipt. The ShiftFlow state
    /// machine owns the rules (exact results body, key reuse after an unknown outcome, new key after SHIFT_NOT_FINISHED).
    /// </summary>
    internal sealed class ShiftRunner : IDisposable
    {
        private readonly CareersApi _api;
        private readonly CareerCard _card;
        private readonly ShiftFlow _flow;
        private readonly UiRoot _ui;
        private readonly VisualElement _root;
        private readonly Label _eyebrow, _heading, _copy, _pay, _xp;
        private readonly VisualElement _actions;
        private Action _waitTick;
        private bool _disposed;

        public ShiftRunner(CareersApi api, CareerCard card)
        {
            _api = api;
            _card = card;
            _flow = new ShiftFlow(card.Id);
            _ui = UiRoot.Instance ?? throw new InvalidOperationException("Shifts need the game UI.");
            _eyebrow = CUi.L(CareersStrings.T("shift.eyebrow"), "ctc-eyebrow");
            _heading = CUi.L("", "ctc-h");
            _copy = CUi.L("", "ctc-body-text");
            _pay = CUi.L("", "ctc-big");
            _xp = CUi.L("", "ctc-status");
            _actions = CUi.Div("ctc-actions");
            var scroll = El.VerticalScroll("ctc-body");
            scroll.Add(CUi.Div("ctc-col", _eyebrow, _heading, _copy, _pay, _xp, _actions));
            _root = CUi.Div("ctc-overlay", CUi.Div("ctc-panel ctc-shift-panel", CUi.L(card.Name, "ctc-title"), scroll));
            _root.name = "careers-shift";
            CUi.Sheet(_root);
            _ui.ModalLayer.Add(_root);
        }

        public ShiftFlow Flow => _flow;

        private void Status(string headingKey, string copyKey, params object[] args)
        {
            CUi.Show(_root, true);
            _heading.text = CareersStrings.F(headingKey, args);
            _copy.text = copyKey == null ? "" : CareersStrings.T(copyKey);
            _pay.text = _xp.text = "";
            _actions.Clear();
        }

        /// <returns>True when the shift settled (the caller then closes the phone and returns to the city).</returns>
        public async Task<bool> Run(CancellationToken ct)
        {
            _flow.BeginStart();
            Status("shift.starting", "shift.startingDetail");
            ShiftTicket ticket;
            try { ticket = await Deadline.Run(c => _api.StartShift(_card.Id, _flow.StartKey, c), ct, 30000); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                Log.Exception("careers-shift", e);
                _flow.Fail("shift.failed", Deadline.Describe(e));
                await ShowFailure(Deadline.Describe(e), ct);
                return false;
            }
            double assignedAt = CUi.Now;
            var registry = Services.TryGet<ConfigStore>(out var config) ? config.Registries : null;
            var plan = ShiftPlan.Build(ticket, _card.Id, id =>
            {
                if (registry == null || !registry.MiniGames.TryGetValue(id, out var def)) return null;
                return new RegistryGame { Id = def.id, CareerId = def.careerId, MaxScore = def.maxScore, DurationSec = def.durationSec };
            }, GameCatalog.Instance, out string planError);
            if (plan == null)
            {
                foreach (var id in ticket?.MiniGameIds ?? new System.Collections.Generic.List<string>()) if (GameCatalog.Instance.Get(id) == null) Log.UnknownId("mini_games", id);
                _flow.Fail(planError);
                await ShowFailure(CareersStrings.T(planError), ct);
                return false;
            }
            _flow.Assigned(plan);

            for (int i = 0; i < plan.Games.Count; i++)
            {
                var game = plan.Games[i];
                CUi.Show(_root, false);
                var outcome = await MiniGameRunner.Run(new MiniGamePlay
                {
                    Game = game, Seed = MiniGameSeed.ForShift(plan.ShiftId, game.Id, i), Practice = false,
                    Context = CareersStrings.F("shift.game", i + 1, plan.Games.Count), AbandonLabel = CareersStrings.T("mg.abandonShift"),
                }, ct);
                ct.ThrowIfCancellationRequested();
                if (!outcome.Finished)
                {
                    _flow.Cancel();
                    Status("shift.failed", "shift.abandoned");
                    await Dismiss("shift.done", ct);
                    return false;
                }
                _flow.RecordScore(outcome.Score);
            }

            double minimumEnd = ShiftTiming.MinimumEnd(assignedAt, _card.ShiftSec, plan.TotalDuration);
            await WaitForEnd(plan.EndsAtUtc, minimumEnd, ct);
            return await Settle(ct);
        }

        private async Task<bool> Settle(CancellationToken ct)
        {
            int notYet = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (!_flow.BeginSettle()) return false;
                Status("shift.settling", "shift.settlingDetail");
                try
                {
                    var receipt = await Deadline.Run(c => _api.CompleteShift(_flow.Plan.ShiftId, _flow.CompletionBody(), _flow.CompletionKey, c), ct, 45000);
                    _flow.Settled(receipt);
                    await ShowReceipt(receipt, ct);
                    return true;
                }
                catch (OperationCanceledException) { throw; }
                catch (ApiException e) when (e.Is("SHIFT_NOT_FINISHED") && notYet < 3)
                {
                    // An explicit "not yet" recorded nothing: wait for the server's endsAt, then send again with a new key.
                    notYet++;
                    _flow.NotFinishedYet();
                    var end = CareersWire.Time((e.Details as JObject)?["endsAt"]) ?? _flow.Plan.EndsAtUtc;
                    await WaitForEnd(end, CUi.Now + 2, ct);
                }
                catch (ApiException e) when (e.Status == 404 && (e.Details as JObject)?.Value<string>("kind") == "shift_completed")
                {
                    // The backend answers a second completion (new key) with 404 shift_completed: an earlier attempt
                    // already settled it. Not a failure; the receipt itself is gone, so refresh the balance instead.
                    _flow.Fail("shift.alreadySettled");
                    await RefreshProfile(ct);
                    Status("shift.paid", "shift.alreadySettled");
                    await Dismiss("shift.done", ct);
                    return true;
                }
                catch (Exception e) when (Deadline.Ambiguous(e))
                {
                    Log.Exception("careers-shift-settle", e);
                    _flow.Unconfirmed("shift.unconfirmed", Deadline.Describe(e));
                    if (!await AskCheckAgain(ct)) return false;
                }
                catch (InvalidOperationException e) when (e.Message == "shift.invalidReceipt")
                {
                    // A 2xx the client can't read: the server likely paid. Show it honestly and refresh the balance.
                    _flow.Fail("shift.invalidReceipt");
                    await RefreshProfile(ct);
                    await ShowFailure(CareersStrings.T("shift.invalidReceipt"), ct);
                    return false;
                }
                catch (Exception e)
                {
                    Log.Exception("careers-shift-settle", e);
                    _flow.Fail("shift.failed", Deadline.Describe(e));
                    await ShowFailure(Deadline.Describe(e), ct);
                    return false;
                }
            }
        }

        private Task WaitForEnd(DateTime endsAtUtc, double minimumEnd, CancellationToken ct)
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            double endsMs = new DateTimeOffset(DateTime.SpecifyKind(endsAtUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            double giveUpAt = CUi.Now + 600;
            Status("shift.waiting", "shift.waitingDetail", "…");
            void Stop() { if (_waitTick != null) _ui.RemoveUpdater(_waitTick); _waitTick = null; }
            var reg = ct.Register(() => { Stop(); done.TrySetCanceled(); });
            _waitTick = () =>
            {
                if (_disposed) { Stop(); done.TrySetCanceled(); return; }
                double now = CUi.Now;
                double left = ShiftTiming.SecondsUntilEnd(now, minimumEnd, ServerClock.SecondsUntil(endsMs), ServerClock.IsSynced);
                _heading.text = CareersStrings.F("shift.waiting", Math.Max(0, (int)Math.Ceiling(left)));
                if (left <= 0) { Stop(); done.TrySetResult(true); }
                else if (now >= giveUpAt) { Stop(); done.TrySetException(new TimeoutException(CareersStrings.T("shift.requestTimeout"))); }
            };
            _ui.AddUpdater(_waitTick);
            _waitTick();
            return done.Task.ContinueWith(t => { reg.Dispose(); return t; }, TaskScheduler.Default).Unwrap();
        }

        private async Task ShowReceipt(ShiftReceipt receipt, CancellationToken ct)
        {
            Status("shift.paid", null);
            _pay.text = CareersStrings.F("shift.paidAmount", CareersWire.Cash(receipt.PayCents));
            _xp.text = CareersStrings.F("shift.xp", receipt.Xp) + (receipt.Promoted ? " · " + CareersStrings.T("shift.promoted") : "");
            var scores = _flow.Plan.Games;
            var lines = new System.Collections.Generic.List<string>();
            for (int i = 0; i < scores.Count; i++)
                lines.Add((scores[i].Spec?.Title ?? scores[i].Id) + ": " + _flow.Scores[i] + "/" + scores[i].MaxScore);
            _copy.text = CareersStrings.T("shift.scores") + "\n" + string.Join("\n", lines);
            CUi.Sfx(receipt.Promoted ? "sfx_rank_up" : "sfx_ui_success");
            if (receipt.Promoted) CUi.Toast(CareersStrings.T("shift.promoted"), ToastKind.Good);
            if (!await RefreshProfile(ct)) _xp.text += "\n" + CareersStrings.T("shift.balanceStale");
            await Dismiss("shift.done", ct);
        }

        private static async Task<bool> RefreshProfile(CancellationToken ct)
        {
            try
            {
                if (!Services.TryGet<PlayerProfile>(out var profile)) return false;
                await Deadline.Run(async c => { await profile.Refresh(c); return true; }, ct, 15000);
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { Log.Exception("careers-profile", e); return false; }
        }

        private async Task ShowFailure(string reason, CancellationToken ct)
        {
            Status("shift.failed", "shift.failedDetail");
            _xp.text = reason ?? "";
            await Dismiss("shift.giveUp", ct);
        }

        private async Task<bool> AskCheckAgain(CancellationToken ct)
        {
            Status("shift.unconfirmed", "shift.unconfirmedDetail");
            _xp.text = _flow.ErrorDetail ?? "";
            var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var again = CUi.B(CareersStrings.T("shift.checkAgain"), () => choice.TrySetResult(true), "ctc-btn ctc-btn-primary");
            again.name = "careers-shift-check-again";
            _actions.Add(again);
            _actions.Add(CUi.B(CareersStrings.T("shift.giveUp"), () => choice.TrySetResult(false), "ctc-btn"));
            using (ct.Register(() => choice.TrySetCanceled())) return await choice.Task;
        }

        private async Task Dismiss(string labelKey, CancellationToken ct)
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _actions.Clear();
            var b = CUi.B(CareersStrings.T(labelKey), () => done.TrySetResult(true), "ctc-btn ctc-btn-primary");
            b.name = "careers-shift-dismiss";
            _actions.Add(b);
            using (ct.Register(() => done.TrySetCanceled())) await done.Task;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_waitTick != null) _ui.RemoveUpdater(_waitTick);
            _waitTick = null;
            _root.RemoveFromHierarchy();
        }
    }
}
