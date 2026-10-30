using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Rest.Model;

namespace CTLife.Net
{
    /// <summary>The signed-in player's `Me` (contract section 4) plus local optimistic updates.</summary>
    public sealed class PlayerProfile
    {
        private readonly Api _api;

        public Me Me { get; private set; }
        public string LikenessJobId { get; private set; }

        public PlayerProfile(Api api)
        {
            _api = api;
        }

        public bool HasCharacter => Onboarding.HasCharacter(Me);
        public bool LotteryRolled => Onboarding.LotteryRolled(Me);
        public long CashCents => Me?.CashCents ?? 0;
        public long DebtCents => Me?.DebtCents ?? 0;
        /// <summary>The next debt instalment deadline (epoch ms) and its amount from GET /me; null with no debt or unknown.</summary>
        public long? DebtDueAtMs { get; private set; }
        public long? DebtWeeklyDueCents { get; private set; }
        public string Alias => Me?.Character?.Alias ?? "anon";
        public string UserId => Me?.User?.Id.ToString();

        private Task<Me> _inflight, _queued;

        /// <summary>
        /// GET /me. Many features refresh after their own action, often at the same moment: a call while a request is running
        /// joins one follow-up request (started after the running one, so it still sees that caller's change). A burst
        /// costs at most two requests. Each caller keeps its own cancellation for the wait.
        /// </summary>
        public async Task<Me> Refresh(CancellationToken ct = default)
        {
            Task<Me> task;
            if (_inflight == null || _inflight.IsCompleted) task = _inflight = Fetch();
            else task = _queued ??= After(_inflight);
            if (ct.CanBeCanceled && !task.IsCompleted)
            {
                var cancelled = new TaskCompletionSource<bool>();
                using (ct.Register(() => cancelled.TrySetResult(true))) await Task.WhenAny(task, cancelled.Task);
                ct.ThrowIfCancellationRequested();
            }
            return await task;
        }

        private async Task<Me> After(Task<Me> running)
        {
            try { await running; } catch { /* the follow-up is a fresh attempt */ }
            _queued = null;
            return await (_inflight = Fetch());
        }

        private async Task<Me> Fetch()
        {
            var current = await _api.GetMe(CancellationToken.None);
            Me = current;
            LikenessJobId = _api.LastLikenessJobId;
            (DebtDueAtMs, DebtWeeklyDueCents) = DebtSchedule.From(current);
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            Log.Info("appearance", "Profile refreshed: " + (HasCharacter ? "character present, " : "no character, ") +
                (string.IsNullOrEmpty(LikenessJobId) ? "stock appearance." : "personal appearance."));
#endif
            Bus.Publish(new ProfileChangedSignal());
            return Me;
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        /// <summary>Offline demo mode (islandDemo, development builds only): a read-only stand-in for /me.</summary>
        public void UseOfflineFixture(Me me)
        {
            Me = me;
            LikenessJobId = null;
            (DebtDueAtMs, DebtWeeklyDueCents) = DebtSchedule.From(me);
            Bus.Publish(new ProfileChangedSignal());
        }
#endif

        public void SetCash(long cents)
        {
            if (Me == null) return;
            Me.CashCents = cents;
            Bus.Publish(new ProfileChangedSignal());
        }

        /// <summary>After POST /bank/repay: the server's balances. A paid-off debt has no instalment left.</summary>
        public void ApplyRepay(long cashCents, long debtCents)
        {
            if (Me == null) return;
            Me.CashCents = cashCents;
            Me.DebtCents = Math.Max(0, debtCents);
            if (Me.DebtCents == 0) { DebtDueAtMs = null; DebtWeeklyDueCents = null; }
            else if (DebtWeeklyDueCents.HasValue) DebtWeeklyDueCents = Math.Min(DebtWeeklyDueCents.Value, Me.DebtCents);
            Bus.Publish(new ProfileChangedSignal());
        }

        public void SetNeeds(Needs needs)
        {
            if (Me == null || needs == null) return;
            Me.Needs = needs;
            Bus.Publish(new ProfileChangedSignal());
        }

        public void ApplyLottery(PostLotteryRoll200Response r)
        {
            if (Me == null || r == null) return;
            Me.CashCents = r.CashCents;
            Me.DebtCents = r.DebtCents;
            Me.HomeId = r.HomeId;
            if (Me.Flags != null) Me.Flags["lotteryRolled"] = true;
            Bus.Publish(new ProfileChangedSignal());
        }

        /// <summary>The look as the realtime string[7] order: [skinTone, headId, hairId, topId, bottomId, shoesId, accId].</summary>
        public string[] LookArray()
        {
            var l = Me?.Character?.Look;
            if (l == null) return new string[7];
            return new[] { l.SkinTone, l.HeadId, l.HairId, l.TopId, l.BottomId, l.ShoesId, l.AccId };
        }
    }
}
