using System;

namespace CTLife.World
{
    /// <summary>
    /// The seam between the Rekt Burger storefront (World: the walk-in restaurant in The Trenches, its counter and kitchen)
    /// and the job screens (City Life: `GET /jobs/rekt-burger`, the shift minigame, the careers ladder). Our own parody
    /// brand: original name and art, no real chain's name, logo, colours or likeness.
    /// </summary>
    public static class RektBurgerHooks
    {
        /// <summary>The walk-in room id (interior_doors.json `room`, Resources/World/Interiors/rekt_burger).</summary>
        public const string RoomId = "rekt_burger";

        /// <summary>What the counter can open: clock in / the shift (or the hiring card while locked) and the menu/info card.</summary>
        public const string Shift = "shift", Info = "info";

        /// <summary>Open the job at the counter. Null until City Life signs in.</summary>
        public static Action<string> Open;

        /// <summary>How the counter's Clock in prompt reads right now (City Life: pay rate, today's cap, on shift, offline;
        /// UI brief 2026-10-25). Null = the plain prompt.</summary>
        public static Func<CounterPrompt?> Prompt;

        /// <summary>The prompt's second key: the job card in the Jobs app. Null = no second key.</summary>
        public static Action JobInfo;

        /// <summary>Whether the job is unlocked for the signed-in player (City Life sets it from GET /jobs/rekt-burger), so the
        /// counter's prompt reads "Work a shift" or "Rekt Burger is hiring (locked)".</summary>
        public static bool Unlocked { get; set; }

        /// <summary>The player is clocked in (City Life sets it from the shift state; the shift keeps running server-side when the
        /// player walks away). While true and the player is in the room, the World plays the working loop and the customers.</summary>
        public static bool OnShift { get; private set; }

        public static event Action<bool> ShiftChanged;

        public static void SetOnShift(bool on)
        {
            if (on == OnShift) return;
            OnShift = on;
            ShiftChanged?.Invoke(on);
        }

        /// <summary>The player walked into / out of the restaurant.</summary>
        public static event Action<bool> FloorChanged;

        public static bool OnFloor { get; private set; }

        public static void SetOnFloor(bool on)
        {
            if (on == OnFloor) return;
            OnFloor = on;
            FloorChanged?.Invoke(on);
        }
    }

    /// <summary>The counter prompt's state, filled by City Life (verbs are UI Strings keys; sub is final text).</summary>
    public struct CounterPrompt
    {
        public string verbKey, icon, sub, subTone, style;
        public bool disabled, busy, locked;
    }
}
