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

        public PlayerProfile(Api api)
        {
            _api = api;
        }

        public bool HasCharacter => Me?.Character != null && !string.IsNullOrEmpty(Me.Character.Alias);
        public bool LotteryRolled
        {
            get
            {
                if (Me == null) return false;
                if (Me.Flags != null && Me.Flags.TryGetValue("lotteryRolled", out var r) && r is bool b) return b;
                return !string.IsNullOrEmpty(Me.HomeId);
            }
        }
        public long CashCents => Me?.CashCents ?? 0;
        public long DebtCents => Me?.DebtCents ?? 0;
        public string Alias => Me?.Character?.Alias ?? "anon";
        public string UserId => Me?.User?.Id.ToString();

        public async Task<Me> Refresh(CancellationToken ct = default)
        {
            Me = await _api.GetMe(ct);
            Bus.Publish(new ProfileChangedSignal());
            return Me;
        }

        public void SetCash(long cents)
        {
            if (Me == null) return;
            Me.CashCents = cents;
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
