using System;

namespace CTLife.World
{
    /// <summary>
    /// The seam between the community nods on CT Island (World: the 80 painted props of contract/ids/nods.json, placed by
    /// tools/world/islandgen/nods.py, drawn and offered by <see cref="NodField"/>) and the season collection (City Life:
    /// the nod net calls and the collection UI). World asks; City Life answers and reports.
    /// </summary>
    public static class NodHooks
    {
        /// <summary>Has the signed-in player found this nod already? (found nods are not drawn or offered). Null until City
        /// Life signs in: everything shows.</summary>
        public static Func<string, bool> IsFound;

        /// <summary>The player pressed "Collect" at this nod (POST /nods/{id}/collect). Null until City Life signs in.</summary>
        public static Action<string> TryCollect;

        /// <summary>The server confirmed a find: World plays the pickup (burst, chime) and hides the prop.</summary>
        public static event Action<string> Collected;

        /// <summary>City Life's found list changed wholesale (sign-in, a refresh): World re-reads <see cref="IsFound"/>.</summary>
        public static event Action FoundRefreshed;

        /// <summary>The player came within reach of a nod (true) or left it (false): nodId, inRange.</summary>
        public static event Action<string, bool> NearNod;

        /// <summary>City Life: the server confirmed <paramref name="nodId"/> found.</summary>
        public static void NotifyCollected(string nodId) => Collected?.Invoke(nodId);

        /// <summary>City Life: the found list was (re)loaded.</summary>
        public static void NotifyFoundRefreshed() => FoundRefreshed?.Invoke();

        /// <summary>World: the player's reach to a nod changed.</summary>
        public static void NotifyNear(string nodId, bool inRange) => NearNod?.Invoke(nodId, inRange);
    }
}
