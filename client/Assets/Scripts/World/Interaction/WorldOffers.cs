using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// A one-tap thing near the player that is not a seat or a vehicle (2026-10-23): a locked flat door ("Knock"), a parcel
    /// on a doorstep ("Pick up"), a parcel locker ("Collect"), "Decorate" inside your own flat. Feature code (City Life)
    /// registers a source; <see cref="InteractionController"/> offers the nearest one through the same HUD prompt as seats
    /// when nothing else is on offer.
    /// </summary>
    public sealed class WorldOffer
    {
        public string id;
        public string verbKey;              // UI Strings key of the button
        public string icon = "door";        // UiIcon name
        public string tagKey;               // the chip above the prompt (UI Strings key), null = none
        public bool tagLocked;
        public bool primary = true;
        public Vector3 position;            // where the prompt floats (world)
        public Transform follow;            // or: an actor it follows (position is then an offset)
        public float range = 2.2f;          // flat distance from the player
        public Action perform;
    }

    public static class WorldOffers
    {
        private static readonly List<Func<Vector3, WorldOffer>> Sources = new List<Func<Vector3, WorldOffer>>();

        public static void Add(Func<Vector3, WorldOffer> source) { if (source != null && !Sources.Contains(source)) Sources.Add(source); }
        public static void Remove(Func<Vector3, WorldOffer> source) => Sources.Remove(source);

        /// <summary>The nearest offer within its range of <paramref name="player"/>, or null.</summary>
        public static WorldOffer Best(Vector3 player)
        {
            WorldOffer best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < Sources.Count; i++)
            {
                WorldOffer o;
                try { o = Sources[i](player); }
                catch (Exception e) { Debug.LogException(e); continue; }
                if (o == null || o.perform == null) continue;
                var at = o.follow != null ? o.follow.position + o.position : o.position;
                float d = InteractionFinder.Flat(player, at);
                if (d <= o.range && d < bestD) { best = o; bestD = d; }
            }
            return best;
        }
    }
}
