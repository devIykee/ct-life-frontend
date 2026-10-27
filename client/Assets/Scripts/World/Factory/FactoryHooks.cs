using System;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// The seam between the Launchpad Tower (World: the tower on CT Island, its launch desk and, later, its interior) and the
    /// Rugpull Factory (Money: `/factory/*`, backend #14). The desk prompt and any future World interactable raise
    /// <see cref="Open"/>; the Money feature sets it after sign-in. Spot ids come from contract/ids/spots.json.
    /// </summary>
    public static class FactoryHooks
    {
        public const string VenueId = "launchpad_tower";
        /// <summary>The launch desk (venue-local x 0, z 7, facing the room) and the launch board behind it.</summary>
        public const string DeskSpot = "launchpad_tower.launch_desk", BoardSpot = "launchpad_tower.launch_board";
        /// <summary>Fallback venue-local desk position when the spots registry is not loaded.</summary>
        public static readonly Vector2 DeskLocalFallback = new Vector2(0f, 7f);

        /// <summary>What the tower can open: the launch form, the board of player coins, my coins.</summary>
        public const string Launch = "launch", Board = "board", Mine = "mine", Rules = "rules";

        /// <summary>Open the Factory at a view (one of the ids above). Null until the Money feature signs in.</summary>
        public static Action<string> Open;

        /// <summary>The player walked into / out of the tower (the venue zone).</summary>
        public static event Action<bool> InTowerChanged;
        public static bool InTower { get; private set; }

        public static void SetInTower(bool on)
        {
            if (on == InTower) return;
            InTower = on;
            InTowerChanged?.Invoke(on);
        }
    }
}
