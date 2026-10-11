using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Furniture close-ups for evidence (round 6): <c>VenueBuildPipeline ... -ctCloseups</c> renders each shot to
    /// <c>captures/&lt;venue&gt;-close-&lt;name&gt;.png</c> after the gameplay views. A shot is a free camera (eye, target, FOV),
    /// optionally relative to a spot so it follows spots.json.
    /// </summary>
    internal static class VenueCloseups
    {
        internal struct Shot
        {
            public string name, spot;
            public Vector3 eye, target;
            public float fov;
        }

        private static Shot S(string name, Vector3 eye, Vector3 target, float fov = 42f) => new Shot { name = name, eye = eye, target = target, fov = fov };

        private static Shot At(string name, string spot, Vector3 eye, Vector3 target, float fov = 42f) =>
            new Shot { name = name, spot = spot, eye = eye, target = target, fov = fov };

        internal static readonly Dictionary<string, Shot[]> Shots = new Dictionary<string, Shot[]>
        {
            ["terminal_cafe"] = new[]
            {
                S("bar", new Vector3(-4.2f, 2.0f, 1.4f), new Vector3(-7.4f, 0.9f, 5.2f)),
                S("espresso", new Vector3(-6.0f, 1.75f, 6.4f), new Vector3(-6.6f, 1.15f, 8.45f), 38f),
                S("communal", new Vector3(2.6f, 2.0f, -6.1f), new Vector3(0f, 0.7f, -2.6f)),
                S("booths", new Vector3(5.6f, 1.8f, 4.6f), new Vector3(7.9f, 0.6f, 7.9f)),
                S("round_tables", new Vector3(3.9f, 1.9f, 0.3f), new Vector3(1.2f, 0.6f, 3.4f)),
                S("lounge", new Vector3(-6.9f, 1.8f, -3.6f), new Vector3(-10.4f, 0.45f, -5.8f)),
                S("armchairs", new Vector3(8.75f, 1.7f, -4.0f), new Vector3(8.75f, 0.45f, -7.4f)),
                S("trading", new Vector3(8.3f, 1.8f, -0.4f), new Vector3(11.3f, 1.0f, -2.2f)),
            },
            ["spaces_studio"] = new[]
            {
                S("stage_chairs", new Vector3(0f, 2.1f, -0.6f), new Vector3(0f, 0.8f, 4.6f)),
                At("bean_bags", "seat_1", new Vector3(-1.9f, 1.5f, -2.2f), new Vector3(0f, 0.35f, 0f)),
                At("poufs", "seat_13", new Vector3(-1.6f, 1.6f, -2.0f), new Vector3(0f, 0.4f, 0f)),
                At("pod_stool", "stage_1", new Vector3(-1.4f, 1.6f, -1.8f), new Vector3(0f, 0.6f, 0f)),
                S("terrace", new Vector3(8.4f, 1.9f, -1.2f), new Vector3(11.6f, 0.5f, -3.2f)),
                S("lounge", new Vector3(-8.6f, 1.6f, 0.9f), new Vector3(-11.0f, 0.5f, -1.5f)),
            },
            ["timeline_square"] = new[]
            {
                S("fountain", new Vector3(0f, 2.6f, -7.6f), new Vector3(0f, 1.3f, 0f), 45f),
                S("terrace", new Vector3(-5.6f, 2.0f, -8.8f), new Vector3(-9.3f, 0.6f, -5.9f)),
                S("stall", new Vector3(5.6f, 2.1f, -5.6f), new Vector3(9.2f, 1.0f, -8.4f)),
                S("lamp", new Vector3(4.4f, 2.4f, -0.6f), new Vector3(6.9f, 2.2f, -2.6f)),
                At("bench_4", "bench_4", new Vector3(1.6f, 1.5f, 1.9f), new Vector3(0f, 0.5f, 0f)),
                S("planter", new Vector3(-3.4f, 1.6f, 1.6f), new Vector3(-5.5f, 0.4f, 3.2f)),
                S("market", new Vector3(8.0f, 1.8f, -3.0f), new Vector3(10.6f, 0.5f, -5.6f)),
            },
        };

        internal static IEnumerable<(string name, Vector3 eye, Vector3 target, float fov)> For(string venueId, VenueSpots spots)
        {
            if (!Shots.TryGetValue(venueId, out var shots)) yield break;
            foreach (var s in shots)
            {
                var o = Vector3.zero;
                if (s.spot != null)
                {
                    if (!spots.spots.TryGetValue(s.spot, out var sp)) continue;
                    o = new Vector3(sp.x, 0f, sp.z);
                }
                yield return (s.name, o + s.eye, o + s.target, s.fov);
            }
        }
    }
}
