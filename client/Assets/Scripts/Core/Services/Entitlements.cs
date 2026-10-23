using System;
using System.Collections.Generic;

namespace CTLife.Core
{
    /// <summary>
    /// What the signed-in player owns beyond the free game (2026-10-22): bought assets such as the hovercar. Not everyone has
    /// the hovercar, so the HUD's flyer button and the summon read this. The backend has no entitlement API yet (proposal:
    /// docs/fe-contract-proposals/entitlements-20261022.md); until it does, <see cref="LocalEntitlements"/> stands in.
    /// </summary>
    public interface IEntitlements
    {
        bool Owns(string itemId);
        event Action Changed;
    }

    public static class Entitlement
    {
        /// <summary>The player's own flying car (vehicle spec "roadstar", the v3 hovercar): a scarce right-to-drive sold in the
        /// Duo Shop as a Metaplex Core NFT for real money only. The backend grants it after verifying the NFT in the
        /// player's wallet; game Cash never unlocks it.</summary>
        public const string Hovercar = "vehicle.hovercar";
    }

    /// <summary>
    /// Stand-in until GET /v1/me/entitlements exists: owns nothing, except in development builds where the launch extra
    /// <c>grantHovercar=1</c> grants the hovercar (testing the flyer). <see cref="Set"/> lets a future backend client fill it.
    /// </summary>
    public sealed class LocalEntitlements : IEntitlements
    {
        private readonly HashSet<string> _owned = new HashSet<string>(StringComparer.Ordinal);
        public event Action Changed;

        public LocalEntitlements()
        {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            if (LaunchArgs.Get("grantHovercar") == "1") _owned.Add(Entitlement.Hovercar);
#endif
        }

        public bool Owns(string itemId) => itemId != null && _owned.Contains(itemId);

        /// <summary>Replace the owned set (the backend's list from EntitlementsSync). A development grant stays on top.</summary>
        public void Set(IEnumerable<string> owned)
        {
            _owned.Clear();
            if (owned != null) foreach (var id in owned) if (!string.IsNullOrEmpty(id)) _owned.Add(id);
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            if (LaunchArgs.Get("grantHovercar") == "1") _owned.Add(Entitlement.Hovercar);
#endif
            Changed?.Invoke();
        }
    }
}
