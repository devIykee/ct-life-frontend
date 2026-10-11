using System.Collections.Generic;
using UnityEngine;

namespace CTLife.UI
{
    /// <summary>One avatar's name-tag inputs for <see cref="TagPlanner"/>, in panel coordinates.</summary>
    public struct TagCandidate
    {
        public int Key;
        public bool IsLocal, IsNpc, IsFriend, Talking, Faced, OnScreen;
        /// <summary>Metres from the local player (or the camera when there is no local player).</summary>
        public float Distance;
        /// <summary>Panel position of the head anchor: the tag sits centred above it.</summary>
        public Vector2 Anchor;
        /// <summary>The tag's size in panel units (last measured, or an estimate before the first layout).</summary>
        public Vector2 Size;
    }

    public struct TagPlacement
    {
        public int Key;
        public bool Visible;
        public Vector2 TopLeft;
        public float Alpha;
    }

    /// <summary>
    /// Which name tags to show and where (pure, unit-tested; <see cref="WorldLabels"/> draws them).
    /// - Never your own tag; only avatars on screen within <see cref="MaxDistance"/>.
    /// - Always (priority order): anyone speaking, friends (people you follow), NPCs you are facing. Then the
    ///   <see cref="NearestCount"/> nearest others.
    /// - Screen-space collision: a tag that would overlap one already placed is pushed up one row; if that still
    ///   overlaps, the lower-priority tag is hidden.
    /// - The centre of the screen (where your own character stands) is never covered: a nearest-N tag there is hidden,
    ///   a priority tag is pushed sideways out of it.
    /// - Alpha fades with distance (and slightly when pushed).
    /// </summary>
    public static class TagPlanner
    {
        public const int NearestCount = 6;
        public const float MaxDistance = 16f;
        public const float FullAlphaDistance = 6f;
        public const float MinAlpha = 0.4f;
        public const float NudgedAlphaFactor = 0.9f;
        public const float Gap = 4f;
        public const int MaxNudges = 1;
        /// <summary>The protected centre: ±11% of the width around the middle, 36%–64% of the height.</summary>
        public const float CenterHalfWidth = 0.11f, CenterTop = 0.36f, CenterBottom = 0.64f;
        /// <summary>The tag's bottom sits this far above the head anchor.</summary>
        public const float Lift = 6f;

        public static int Priority(in TagCandidate c) => c.Talking ? 0 : c.IsFriend ? 1 : (c.IsNpc && c.Faced) ? 2 : 3;

        public static Rect CenterZone(Vector2 panel) => Rect.MinMaxRect(
            panel.x * (0.5f - CenterHalfWidth), panel.y * CenterTop, panel.x * (0.5f + CenterHalfWidth), panel.y * CenterBottom);

        public static float AlphaFor(float distance)
        {
            if (distance <= FullAlphaDistance) return 1f;
            float t = Mathf.Clamp01((distance - FullAlphaDistance) / (MaxDistance - FullAlphaDistance));
            return Mathf.Lerp(1f, MinAlpha, t);
        }

        private static readonly List<int> Order = new List<int>();
        private static readonly List<Rect> Placed = new List<Rect>();

        /// <summary>Fills <paramref name="output"/> with one placement per input (same order). Main-thread only.</summary>
        public static void Plan(IReadOnlyList<TagCandidate> input, Vector2 panel, List<TagPlacement> output)
        {
            output.Clear();
            Order.Clear();
            Placed.Clear();
            for (int i = 0; i < input.Count; i++)
            {
                output.Add(new TagPlacement { Key = input[i].Key, Visible = false, Alpha = 0f });
                var c = input[i];
                if (!c.IsLocal && c.OnScreen && c.Distance <= MaxDistance) Order.Add(i);
            }
            Order.Sort((a, b) =>
            {
                int pa = Priority(input[a]), pb = Priority(input[b]);
                if (pa != pb) return pa.CompareTo(pb);
                int d = input[a].Distance.CompareTo(input[b].Distance);
                return d != 0 ? d : input[a].Key.CompareTo(input[b].Key);
            });

            var zone = CenterZone(panel);
            int nearest = 0;
            foreach (int i in Order)
            {
                var c = input[i];
                bool priority = Priority(c) < 3;
                if (!priority && nearest >= NearestCount) continue;
                if (!priority) nearest++;

                var rect = new Rect(c.Anchor.x - c.Size.x * 0.5f, c.Anchor.y - c.Size.y - Lift, c.Size.x, c.Size.y);
                if (rect.Overlaps(zone))
                {
                    if (!priority) continue;
                    // Push sideways to the nearer edge of the protected centre.
                    rect.x = c.Anchor.x < zone.center.x ? zone.xMin - rect.width - Gap : zone.xMax + Gap;
                }
                bool nudged = false;
                int tries = 0;
                while (Collides(rect) && tries < MaxNudges)
                {
                    rect.y -= rect.height + Gap;
                    nudged = true;
                    tries++;
                }
                if (Collides(rect) || rect.Overlaps(zone) || rect.yMin < 0f) continue;
                Placed.Add(rect);
                float alpha = AlphaFor(c.Distance) * (nudged ? NudgedAlphaFactor : 1f);
                output[i] = new TagPlacement { Key = c.Key, Visible = true, TopLeft = rect.position, Alpha = alpha };
            }
        }

        private static bool Collides(Rect r)
        {
            var padded = new Rect(r.x - Gap * 0.5f, r.y - Gap * 0.5f, r.width + Gap, r.height + Gap);
            foreach (var p in Placed) if (padded.Overlaps(p)) return true;
            return false;
        }
    }
}
