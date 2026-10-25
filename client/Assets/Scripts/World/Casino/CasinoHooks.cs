using System;

namespace CTLife.World
{
    /// <summary>
    /// The seam between the Rug Room venue (World: the walk-in casino on the Boardwalk pier, its machines and tables) and the
    /// casino screens (City Life: `GET /casino/state`, slots, crash, coinflip, safety). The World side raises
    /// <see cref="Open"/> from the floor's interactables; City Life sets it after sign-in.
    /// </summary>
    public static class CasinoHooks
    {
        /// <summary>The walk-in room id of the casino floor (interior_doors.json `room`, Resources/World/Interiors/rug_room).</summary>
        public const string RoomId = "rug_room";

        /// <summary>Game ids the floor can open.</summary>
        public const string Lobby = "lobby", Slots = "slots", Crash = "crash", Coinflip = "coinflip";

        /// <summary>Open the casino at a game (one of the ids above). Null until City Life signs in.</summary>
        public static Action<string> Open;

        /// <summary>The player walked onto / off the casino floor (City Life starts / stops the ambience and the challenge poll).</summary>
        public static event Action<bool> FloorChanged;

        public static bool OnFloor { get; private set; }

        public static void SetOnFloor(bool on)
        {
            if (on == OnFloor) return;
            OnFloor = on;
            FloorChanged?.Invoke(on);
        }
    }
}
