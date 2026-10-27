using System.Collections.Generic;
using UnityEngine;

namespace CTLife.UI
{
    /// <summary>
    /// Keeps speech bubbles from covering each other (pure; unit-tested). Each bubble wants to sit above its speaker's head;
    /// the one lowest on screen (usually the nearest speaker) keeps that place, and any bubble that would overlap an
    /// already placed one moves up just above it, with <see cref="Gap"/> between them. Panel coordinates, y down.
    /// Seen 2026-10-08: two NPCs at the same spot spoke at once and one bubble hid the other's text.
    /// </summary>
    public static class BubbleStacker
    {
        public const float Gap = 6f;

        /// <summary>Writes the resolved rect of every desired rect into <paramref name="placed"/> (same order).</summary>
        public static void Resolve(IList<Rect> desired, List<Rect> placed)
        {
            placed.Clear();
            if (desired == null) return;
            for (int i = 0; i < desired.Count; i++) placed.Add(desired[i]);
            var order = new List<int>(desired.Count);
            for (int i = 0; i < desired.Count; i++) order.Add(i);
            // Lowest on screen first (largest bottom edge); ties keep their order.
            order.Sort((a, b) =>
            {
                int c = desired[b].yMax.CompareTo(desired[a].yMax);
                return c != 0 ? c : a.CompareTo(b);
            });

            var done = new List<int>(desired.Count);
            foreach (int i in order)
            {
                var r = desired[i];
                // Moving up can only ever meet bubbles placed earlier; a pass per placed bubble settles it.
                for (int pass = 0; pass <= done.Count; pass++)
                {
                    bool moved = false;
                    foreach (int j in done)
                    {
                        var o = placed[j];
                        if (!Overlaps(r, o)) continue;
                        r.y = o.yMin - r.height - Gap;
                        moved = true;
                    }
                    if (!moved) break;
                }
                placed[i] = r;
                done.Add(i);
            }
        }

        /// <summary>
        /// Keeps bubbles out of the HUD's safe zones (coordinator 2026-10-27: a customer's line sat behind the on-shift pill).
        /// A bubble that would overlap a zone moves down to just under it (the zones are the top HUD rows; a speaker high on
        /// screen gets their line below the pills, never behind them). Pure; unit-tested.
        /// </summary>
        public static void KeepOut(List<Rect> placed, IList<Rect> zones)
        {
            if (placed == null || zones == null || zones.Count == 0) return;
            for (int i = 0; i < placed.Count; i++)
            {
                var r = placed[i];
                for (int pass = 0; pass < zones.Count; pass++)
                {
                    bool moved = false;
                    foreach (var z in zones)
                    {
                        if (z.width <= 0 || z.height <= 0 || !Overlaps(r, z)) continue;
                        r.y = z.yMax + Gap;
                        moved = true;
                    }
                    if (!moved) break;
                }
                placed[i] = r;
            }
        }

        /// <summary>Overlap including the gap, so stacked bubbles never touch.</summary>
        public static bool Overlaps(Rect a, Rect b) =>
            a.xMin < b.xMax + Gap && b.xMin < a.xMax + Gap && a.yMin < b.yMax + Gap && b.yMin < a.yMax + Gap;
    }
}
