using CTLife.Core;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Timeline Square v2 (round 3 look-dev), built to the in-game concept (art-direction/out/ingame-timeline-concept-v1.png)
    /// and the title art (title-bg-eggs-v1.png): a sunny Mediterranean town square at golden hour.
    ///  * Painted cobbles, a tiered stone fountain on a round raised deck with flower planters, six park benches round it.
    ///  * West: blue café with a striped awning and a terrace, the Pixel Wall (live surface), a bakery with a red awning,
    ///    a vine-covered corner house. North: two rows with the billboards (live surfaces), a street up to the town hall
    ///    with the Timeline screen (live surface), the clock tower. East: toyshop, parcels shop, market building and stall.
    ///  * Background: a Colosseum-style arched round hall with a burgundy banner, a hill town, cypresses, the sea, and a
    ///    golden-hour sky washed with the Solana gradient (no logo).
    ///  * The 15 Tier A community eggs (docs/research/community-eggs-2026-10.md), as our own drawings, never logos or letters:
    ///    BONK stall, WIF dog, Popcat window, Pudgy igloo cart, Mad Lads poster, Claynosaurz toy, Phantom-style ghost,
    ///    Jupiter astronaut cat, pump.fun capsule machine, Helius orb lamp, Seeker phone, ORE plot, Solana sky, Superteam
    ///    bunting, Colosseum hall.
    /// Spot ids are the contract's; spots.json fe.4 moves center/square_center off the fountain and turns the benches to face it.
    /// </summary>
    public static class VenuesTimelineSquare
    {
        private static readonly Vector3 F = Vector3.zero;
        private const float DeckR = 4.4f, DeckH = 0.16f, BasinR = 2.8f;
        private const float WestX = -20.6f, EastX = 20.6f, NorthZ = 15.6f;

        private static float Ground(Vector3 p) => new Vector2(p.x, p.z).magnitude <= DeckR ? DeckH : 0f;

        private static Vector3 Fwd(SpotDef s) => new Vector3(Mathf.Sin(s.rotY), 0f, Mathf.Cos(s.rotY));

        /// <summary>A facade frame: origin on the facade plane at ground level, facing = the direction the front looks.</summary>
        private struct Fr
        {
            public Vector3 o, r, f;
            public float yaw;
            public Fr(Vector3 origin, float facing)
            {
                o = origin; yaw = facing;
                float a = facing * Mathf.Deg2Rad;
                f = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                r = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
            }
            /// <summary>u along the facade (right), y up, d out of the facade towards the square (negative = inside).</summary>
            public Vector3 P(float u, float y, float d = 0f) => o + r * u + Vector3.up * y + f * d;
        }

        public static void TimelineSquare(VB vb)
        {
            vb.GroundAt = Ground;
            Cobbles(vb);
            Fountain(vb);
            Benches(vb);
            Lamps(vb);
            WestRow(vb);
            NorthRow(vb);
            EastRow(vb);
            SouthEdge(vb);
            Eggs(vb);
            Backdrop(vb);
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 0.6f, 1.4f);
        }

        // ------------------------------------------------------------------ ground

        private static void Cobbles(VB vb)
        {
            // Painted cobble tiles (lit, lightmapped). One seamless tile, unrotated, so joints never show.
            const float t = 2.5f;
            for (int ix = -11; ix < 11; ix++)
                for (int iz = -9; iz < 10; iz++)
                    vb.FloorSign("tl_cobble_a", ix * t + t * .5f, iz * t + t * .5f, t, 0f, t, -0.004f, lit: true);
        }

        // ------------------------------------------------------------------ fountain

        private static void Fountain(VB vb)
        {
            // Round raised deck (walkable) paved in limestone with a kerb, radial joints and a joint ring.
            vb.WalkablePrism("FountainDeck", F, DeckR, DeckH, 56);
            vb.Cylinder(F, DeckR, DeckH, "limestone", 56);
            vb.Arc(F, DeckR - 0.16f, DeckR + 0.02f, 0f, 360f, 0f, DeckH + 0.004f, "stoneshade", 64);
            vb.RingDecal(F + Vector3.up * DeckH, 3.55f, 0.03f, "stoneshade", 48);
            for (int i = 0; i < 24; i++)
            {
                float a = i * 15f * Mathf.Deg2Rad;
                vb.Box(F + new Vector3(Mathf.Sin(a) * 3.55f, DeckH + 0.003f, Mathf.Cos(a) * 3.55f), new Vector3(0.03f, 0.004f, 1.5f), "stoneshade", i * 15f);
            }
            // Basin: a stone ring wall (seat height), a moulded lip, water; navigation blocked by a round solid.
            vb.Arc(F, BasinR - 0.34f, BasinR, 0f, 360f, DeckH, DeckH + 0.52f, "limestone", 64);
            vb.Arc(F, BasinR - 0.4f, BasinR + 0.06f, 0f, 360f, DeckH + 0.52f, DeckH + 0.6f, "stuccocream", 64);
            vb.Arc(F, BasinR - 0.02f, BasinR + 0.04f, 0f, 360f, DeckH, DeckH + 0.12f, "stoneshade", 64);
            vb.Cylinder(F + Vector3.up * DeckH, BasinR - 0.34f, 0.4f, "water2", 48);
            vb.BlockPrism("FountainBasin", F + Vector3.up * DeckH, BasinR + 0.08f, 0.62f, 32);
            // Tiers: pedestal, flared lower bowl, column, upper bowl, finial.
            float y0 = DeckH + 0.4f;
            vb.Cylinder(F + Vector3.up * y0, 0.55f, 0.22f, "limestone", 18, topRadius: 0.32f);
            vb.Cylinder(F + Vector3.up * (y0 + 0.22f), 0.26f, 0.85f, "stuccocream", 14);
            vb.Cylinder(F + Vector3.up * (y0 + 1.07f), 0.3f, 0.36f, "limestone", 28, topRadius: 1.3f, caps: false);
            vb.Arc(F, 1.2f, 1.36f, 0f, 360f, y0 + 1.38f, y0 + 1.48f, "stuccocream", 48);
            vb.Cylinder(F + Vector3.up * (y0 + 1.3f), 1.2f, 0.12f, "water2", 32);
            vb.Cylinder(F + Vector3.up * (y0 + 1.42f), 0.18f, 0.6f, "limestone", 12);
            vb.Cylinder(F + Vector3.up * (y0 + 2.0f), 0.2f, 0.24f, "limestone", 20, topRadius: 0.68f, caps: false);
            vb.Arc(F, 0.6f, 0.72f, 0f, 360f, y0 + 2.22f, y0 + 2.3f, "stuccocream", 32);
            vb.Cylinder(F + Vector3.up * (y0 + 2.15f), 0.62f, 0.08f, "water2", 24);
            vb.Cylinder(F + Vector3.up * (y0 + 2.23f), 0.1f, 0.22f, "limestone", 10);
            vb.Sphere(F + Vector3.up * (y0 + 2.55f), 0.14f, "stuccocream", rings: 6, segments: 10);
            // Water: a spout, streams from both bowls, splash rings on the basin.
            vb.Pipe(F + Vector3.up * (y0 + 2.62f), F + Vector3.up * (y0 + 3.05f), 0.04f, "waterlight", 6);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                vb.Sphere(F + Vector3.up * (y0 + 3.0f) + dir * 0.12f, 0.05f, "waterlight", rings: 3, segments: 5);
            }
            for (int i = 0; i < 12; i++)
            {
                float a = (i + 0.5f) * 30f * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var p0 = F + dir * 1.32f + Vector3.up * (y0 + 1.47f);
                var p1 = F + dir * 1.5f + Vector3.up * (y0 + 1.3f);
                var p2 = F + dir * 1.62f + Vector3.up * (y0 + 0.7f);
                var p3 = F + dir * 1.68f + Vector3.up * (y0 + 0.02f);
                vb.Pipe(p0, p1, 0.05f, "waterlight", 6);
                vb.Pipe(p1, p2, 0.05f, "waterlight", 6);
                vb.Pipe(p2, p3, 0.045f, "waterlight", 6);
                vb.Sphere(p3 + Vector3.up * 0.02f, 0.09f, "petalwhite", rings: 3, segments: 6);
                if (i % 2 == 0)
                {
                    var q0 = F + dir * 0.68f + Vector3.up * (y0 + 2.3f);
                    var q1 = F + dir * 0.8f + Vector3.up * (y0 + 2.15f);
                    var q2 = F + dir * 0.88f + Vector3.up * (y0 + 1.5f);
                    vb.Pipe(q0, q1, 0.035f, "waterlight", 5);
                    vb.Pipe(q1, q2, 0.035f, "waterlight", 5);
                }
            }
            vb.RingDecal(F + Vector3.up * (DeckH + 0.4f), 1.68f, 0.07f, "waterlight", 40);
            vb.RingDecal(F + Vector3.up * (DeckH + 0.4f), 2.05f, 0.03f, "waterlight", 40);
            vb.RingDecal(F + Vector3.up * (y0 + 1.42f), 0.86f, 0.04f, "waterlight", 24);
            // Flower planters on the deck between the bench lines (stone troughs with flowers and leaves).
            for (int i = 0; i < 6; i++)
            {
                float ang = i * 60f;
                float a = ang * Mathf.Deg2Rad;
                var c = F + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 3.78f;
                vb.Box(c + Vector3.up * (DeckH + 0.27f), new Vector3(1.5f, 0.54f, 0.62f), "limestone", ang + 90f);
                vb.Box(c + Vector3.up * (DeckH + 0.55f), new Vector3(1.58f, 0.06f, 0.7f), "stuccocream", ang + 90f);
                vb.RegisterProp("planter", c + Vector3.up * (DeckH + 0.27f), new Vector3(1.5f, 0.54f, 0.62f), ang + 90f);
                Flowers(vb, c + Vector3.up * (DeckH + 0.56f), 1.35f, 0.5f, ang + 90f, i);
            }
        }

        /// <summary>A bed of flowers: leafy mounds, flower clumps and petal dots in mixed colours.</summary>
        private static void Flowers(VB vb, Vector3 top, float length, float depth, float yaw, int seed, bool tall = false)
        {
            var q = Quaternion.Euler(0, yaw, 0);
            seed = Mathf.Abs(seed) % 9973;
            var rnd = new System.Random(seed * 7919 + 17);
            string[] petals = { "petalred", "petalpink", "petalwhite", "petalyellow", "wisteria", "ctorange" };
            int n = Mathf.Max(2, Mathf.RoundToInt(length / 0.45f));
            for (int k = 0; k < n; k++)
            {
                float t = (k + 0.5f) / n - 0.5f;
                var p = top + q * new Vector3(t * length, 0f, (float)(rnd.NextDouble() - 0.5) * depth * 0.4f);
                vb.Blob(p + Vector3.up * 0.1f, new Vector3(0.5f, tall ? 0.42f : 0.3f, 0.42f), k % 2 == 0 ? "ivy" : "leaf", yaw: (float)rnd.NextDouble() * 180f, rings: 3, segments: 6);
                string bloom = petals[(k + seed) % petals.Length];
                for (int j = 0; j < 7; j++)
                {
                    var o = new Vector3((float)(rnd.NextDouble() - 0.5) * 0.44f, 0.16f + (float)rnd.NextDouble() * (tall ? 0.32f : 0.16f), (float)(rnd.NextDouble() - 0.5) * 0.32f);
                    vb.Sphere(p + q * o, 0.065f, j % 3 == 2 ? petals[(k + j + seed) % petals.Length] : bloom, rings: 2, segments: 4);
                }
            }
        }

        // ------------------------------------------------------------------ benches and lamps

        private static void Benches(VB vb)
        {
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("bench_")))
            {
                var p = VB.P(s);
                var fwd = Fwd(s);
                var right = new Vector3(fwd.z, 0, -fwd.x);
                vb.ModelAt("park_bench", p - fwd * 0.12f, VB.Deg(s));
                vb.SeatCollider(s, new Vector3(0, 0.45f, -0.1f), new Vector3(1.6f, 0.5f, 0.6f));
                vb.Approach(s, p + fwd * 0.95f);
                foreach (float side in new[] { -1f, 1f })
                {
                    var pot = p + right * side * 1.25f - fwd * 0.15f;
                    vb.Cylinder(pot, 0.24f, 0.42f, "terracotta", 14, topRadius: 0.3f);
                    vb.RegisterProp("bench_pot", pot + Vector3.up * 0.21f, new Vector3(0.6f, 0.42f, 0.6f));
                    Flowers(vb, pot + Vector3.up * 0.42f, 0.3f, 0.3f, VB.Deg(s), (int)(side + 3) + s.id.Length);
                }
            }
        }

        private static void Lamps(VB vb)
        {
            foreach (float ang in new[] { 60f, 120f, 240f, 300f })
            {
                float a = ang * Mathf.Deg2Rad;
                Lamp(vb, new Vector3(Mathf.Sin(a) * 10.4f, 0f, Mathf.Cos(a) * 10.4f));
            }
            foreach (var p in new[] { new Vector3(-12.6f, 0, -12.2f), new Vector3(12.8f, 0, -12.6f), new Vector3(-6.2f, 0, 14.6f), new Vector3(6.2f, 0, 14.6f),
                                      new Vector3(-18.6f, 0, 11.5f), new Vector3(18.6f, 0, 1.2f) })
                Lamp(vb, p);
        }

        /// <summary>Black cast-iron lantern post with a lit lantern (baked warm light).</summary>
        private static void Lamp(VB vb, Vector3 p)
        {
            vb.Cylinder(p, 0.2f, 0.3f, "iron", 8, topRadius: 0.13f);
            vb.Cylinder(p + Vector3.up * 0.3f, 0.07f, 2.8f, "iron", 8, topRadius: 0.055f);
            vb.Cylinder(p + Vector3.up * 1.0f, 0.1f, 0.12f, "iron", 8);
            vb.Cylinder(p + Vector3.up * 3.08f, 0.17f, 0.08f, "iron", 8);
            vb.Box(p + Vector3.up * 3.38f, new Vector3(0.28f, 0.42f, 0.28f), "glowamber", glow: true);
            foreach (var (dx, dz) in new[] { (-0.14f, -0.14f), (0.14f, -0.14f), (-0.14f, 0.14f), (0.14f, 0.14f) })
                vb.Box(p + new Vector3(dx, 3.38f, dz), new Vector3(0.035f, 0.46f, 0.035f), "iron");
            vb.Cylinder(p + Vector3.up * 3.6f, 0.24f, 0.18f, "iron", 8, topRadius: 0.03f);
            vb.Sphere(p + Vector3.up * 3.82f, 0.05f, "iron", rings: 3, segments: 6);
            vb.RegisterProp("lamp", p + Vector3.up * 1.6f, new Vector3(0.4f, 3.2f, 0.4f));
            vb.PointLight(p + Vector3.up * 3.3f, "glowamber", 0.7f, 6f);
        }

        // ------------------------------------------------------------------ buildings

        /// <summary>
        /// Stucco house: painted facade panel, side-coloured body, stone plinth, quoins and cornice, a gable roof with
        /// painted tiles, and rows of shuttered windows on the upper floors (ground floor left to the caller).
        /// </summary>
        private static void House(VB vb, Fr fr, float width, float height, float depth, string stucco, string side, string shutter,
            int seed, bool roof = true, bool balcony = false, int floors = 2, float firstFloorY = 3.4f, bool far = false)
        {
            if (far)
            {
                // Background house: body, painted facade, a few lit/dark panes and the roof (no quoins, sills or flowers).
                vb.Box(fr.P(0, height * .5f, -depth * .5f), new Vector3(width, height, depth), side, fr.yaw);
                vb.Sign(stucco, fr.P(0, 0, 0.012f), width, fr.yaw, height, lit: true);
                var rf = new System.Random(seed);
                for (int fl = 0; fl < Mathf.Max(1, (int)(height / 2.8f)); fl++)
                    for (int i = 0; i < 2; i++)
                        vb.Sign(rf.NextDouble() < 0.4 ? "tl_pane_lit" : "tl_pane_dark", fr.P(-width * .25f + i * width * .5f, 1.0f + fl * 2.6f, 0.03f), 0.8f, fr.yaw, 1.2f);
                if (roof) Roof(vb, fr, width, height, depth);
                return;
            }
            vb.Box(fr.P(0, height * .5f, -depth * .5f), new Vector3(width, height, depth), side, fr.yaw);
            vb.Sign(stucco, fr.P(0, 0, 0.012f), width, fr.yaw, height, lit: true);
            vb.Box(fr.P(0, 0.32f, 0.04f), new Vector3(width + 0.02f, 0.64f, 0.08f), "stoneshade", fr.yaw);
            vb.Box(fr.P(0, height - 0.16f, 0.1f), new Vector3(width + 0.14f, 0.32f, 0.22f), "limestone", fr.yaw);
            vb.Box(fr.P(0, firstFloorY - 0.12f, 0.06f), new Vector3(width, 0.14f, 0.12f), "limestone", fr.yaw);
            for (float y = 0.7f; y < height - 0.4f; y += 0.5f)
            {
                bool wide = ((int)(y * 2f) % 2) == 0;
                foreach (float s in new[] { -1f, 1f })
                    vb.Box(fr.P(s * (width * .5f - (wide ? 0.2f : 0.15f)), y + 0.22f, 0.03f), new Vector3(wide ? 0.4f : 0.3f, 0.44f, 0.06f), "limestone", fr.yaw);
            }
            if (roof) Roof(vb, fr, width, height, depth);
            var rnd = new System.Random(seed);
            int perFloor = Mathf.Max(1, Mathf.FloorToInt((width - 1.0f) / 2.1f));
            for (int fl = 1; fl < floors; fl++)
            {
                float y = firstFloorY + (fl - 1) * 2.9f + 0.55f;
                for (int i = 0; i < perFloor; i++)
                {
                    float u = -width * .5f + (i + 0.5f) * width / perFloor;
                    bool lit = rnd.NextDouble() < 0.35;
                    bool flowers = (i + fl + seed) % 3 != 0;
                    Window(vb, fr, u, y, lit, shutter, flowers, seed + i * 3 + fl);
                }
                if (balcony && fl == 1) Balcony(vb, fr, 0f, firstFloorY + 0.05f, Mathf.Min(width - 1.2f, 3.2f), seed);
            }
        }

        private static void Roof(VB vb, Fr fr, float width, float height, float depth)
        {
            const float pitch = 27f, over = 0.45f;
            float half = depth * .5f + over;
            float rise = (depth * .5f) * Mathf.Tan(pitch * Mathf.Deg2Rad);
            float slope = half / Mathf.Cos(pitch * Mathf.Deg2Rad);
            var yawQ = Quaternion.Euler(0, fr.yaw, 0);
            foreach (float s in new[] { 1f, -1f })
            {
                // Slab from the ridge (d = -depth/2, y = height + rise) out to the eave on this side.
                var ridge = fr.P(0, height + rise, -depth * .5f);
                var dirDown = fr.f * s * Mathf.Cos(pitch * Mathf.Deg2Rad) - Vector3.up * Mathf.Sin(pitch * Mathf.Deg2Rad);
                var rot = yawQ * Quaternion.Euler(s * pitch, 0f, 0f);
                var center = ridge + dirDown * (slope * .5f) + Vector3.up * 0.06f;
                vb.BoxRot(center, new Vector3(width + 0.5f, 0.12f, slope), rot, "rooftile");
                if (s > 0)
                {
                    // Painted barrel tiles on the slope seen from the square.
                    var n = rot * Vector3.up;
                    var m = Matrix4x4.TRS(ridge + n * 0.125f + Vector3.up * 0.06f, rot * Quaternion.Euler(90f, 0f, 0f), Vector3.one);
                    vb.SignAt("tl_roof", m, width + 0.5f, slope, lit: true);
                    vb.Box(ridge + dirDown * slope + Vector3.up * 0.0f, new Vector3(width + 0.5f, 0.1f, 0.14f), "terracotta", fr.yaw);
                }
            }
            vb.CylinderRot(fr.P(-(width * .5f + 0.25f), height + rise + 0.12f, -depth * .5f), yawQ * Quaternion.Euler(0, 0, -90f), 0.11f, width + 0.5f, "terracotta", 8);
            // Gable end walls (triangles) so the roof reads solid from the sides.
            foreach (float e in new[] { -1f, 1f })
            {
                float u = e * width * .5f;
                vb.Triangle(fr.P(u, height, 0.02f), fr.P(u, height, -depth - 0.02f), fr.P(u, height + rise, -depth * .5f), "stuccocream");
            }
        }

        private static void Window(VB vb, Fr fr, float u, float y, bool lit, string shutter, bool flowers, int seed)
        {
            const float w = 0.82f, h = 1.25f;
            vb.Sign(lit ? "tl_pane_lit" : "tl_pane_dark", fr.P(u, y, 0.03f), w, fr.yaw, h);
            vb.Box(fr.P(u, y + h + 0.08f, 0.06f), new Vector3(w + 0.3f, 0.16f, 0.12f), "limestone", fr.yaw);
            vb.Box(fr.P(u, y - 0.05f, 0.09f), new Vector3(w + 0.24f, 0.1f, 0.18f), "limestone", fr.yaw);
            foreach (float s in new[] { -1f, 1f })
            {
                vb.Box(fr.P(u + s * (w * .5f + 0.04f), y + h * .5f, 0.05f), new Vector3(0.08f, h, 0.06f), "offwhite", fr.yaw);
                if (shutter != null)
                {
                    vb.Box(fr.P(u + s * (w * .5f + 0.3f), y + h * .5f, 0.05f), new Vector3(0.44f, h + 0.05f, 0.05f), shutter, fr.yaw);
                    for (int k = 0; k < 5; k++)
                        vb.Box(fr.P(u + s * (w * .5f + 0.3f), y + 0.15f + k * 0.24f, 0.08f), new Vector3(0.4f, 0.03f, 0.02f), "darkgrey", fr.yaw);
                }
            }
            if (flowers)
            {
                vb.Box(fr.P(u, y - 0.04f, 0.22f), new Vector3(w + 0.1f, 0.2f, 0.24f), "terracotta", fr.yaw);
                Flowers(vb, fr.P(u, y + 0.06f, 0.22f), w, 0.2f, fr.yaw, seed, tall: false);
            }
        }

        private static void Balcony(VB vb, Fr fr, float u, float y, float width, int seed)
        {
            vb.Box(fr.P(u, y, 0.45f), new Vector3(width, 0.12f, 0.9f), "limestone", fr.yaw);
            for (int i = 0; i <= Mathf.RoundToInt(width / 0.18f); i++)
            {
                float uu = u - width * .5f + i * 0.18f;
                vb.Box(fr.P(uu, y + 0.5f, 0.86f), new Vector3(0.025f, 0.9f, 0.025f), "iron", fr.yaw);
            }
            vb.Box(fr.P(u, y + 0.95f, 0.86f), new Vector3(width, 0.05f, 0.05f), "iron", fr.yaw);
            foreach (float s in new[] { -1f, 1f })
                vb.Box(fr.P(u + s * width * .5f, y + 0.5f, 0.45f), new Vector3(0.04f, 0.9f, 0.9f), "iron", fr.yaw);
            vb.Box(fr.P(u, y + 0.25f, 0.86f), new Vector3(width, 0.04f, 0.04f), "iron", fr.yaw);
            Flowers(vb, fr.P(u, y + 0.08f, 0.62f), width - 0.3f, 0.3f, fr.yaw, seed + 101, tall: true);
        }

        /// <summary>Striped canvas awning sloping out from the wall, with a scalloped valance and iron arms.</summary>
        private static void Awning(VB vb, Fr fr, float u, float width, float top, float proj, string a, string b, float drop = 0.55f)
        {
            int n = Mathf.Max(4, Mathf.RoundToInt(width / 0.42f));
            float sw = width / n;
            float pitch = Mathf.Atan2(drop, proj) * Mathf.Rad2Deg;
            float len = Mathf.Sqrt(drop * drop + proj * proj);
            var rot = Quaternion.Euler(0, fr.yaw, 0) * Quaternion.Euler(pitch, 0, 0);
            for (int i = 0; i < n; i++)
            {
                float uu = u - width * .5f + (i + 0.5f) * sw;
                vb.BoxRot(fr.P(uu, top - drop * .5f, proj * .5f), new Vector3(sw + 0.002f, 0.04f, len), rot, i % 2 == 0 ? a : b);
                vb.Box(fr.P(uu, top - drop - 0.16f, proj + 0.01f), new Vector3(sw, 0.32f, 0.03f), i % 2 == 0 ? a : b, fr.yaw);
                vb.Sphere(fr.P(uu, top - drop - 0.33f, proj + 0.01f), sw * 0.5f, i % 2 == 0 ? a : b, rings: 3, segments: 6);
            }
            foreach (float s in new[] { -1f, 1f })
                vb.Pipe(fr.P(u + s * (width * .5f - 0.1f), top - 0.9f, 0.02f), fr.P(u + s * (width * .5f - 0.1f), top - drop, proj), 0.02f, "iron", 5);
        }

        /// <summary>Shop window (painted interior, unlit so it glows) in a dark wood frame with a stone sill.</summary>
        private static void ShopWindow(VB vb, Fr fr, float u, float width, string art, float y = 0.75f, float height = 1.9f)
        {
            vb.Sign(art, fr.P(u, y, 0.02f), width, fr.yaw, height);
            vb.Box(fr.P(u, y - 0.08f, 0.1f), new Vector3(width + 0.2f, 0.12f, 0.24f), "limestone", fr.yaw);
            vb.Box(fr.P(u, y + height + 0.06f, 0.06f), new Vector3(width + 0.2f, 0.12f, 0.12f), "darkwood", fr.yaw);
            foreach (float s in new[] { -1f, 1f })
                vb.Box(fr.P(u + s * (width * .5f + 0.05f), y + height * .5f, 0.06f), new Vector3(0.1f, height + 0.1f, 0.12f), "darkwood", fr.yaw);
        }

        private static void Door(VB vb, Fr fr, float u, string color, float width = 1.1f, float height = 2.3f)
        {
            vb.Box(fr.P(u, height * .5f, 0.02f), new Vector3(width, height, 0.06f), color, fr.yaw);
            for (int k = 0; k < 2; k++)
                vb.Box(fr.P(u, 0.55f + k * 0.95f, 0.06f), new Vector3(width - 0.3f, 0.7f, 0.02f), "darkwood", fr.yaw);
            vb.Box(fr.P(u + width * .3f, 1.1f, 0.09f), new Vector3(0.05f, 0.05f, 0.06f), "brass", fr.yaw);
            vb.Box(fr.P(u, height + 0.12f, 0.07f), new Vector3(width + 0.4f, 0.24f, 0.14f), "limestone", fr.yaw);
            foreach (float s in new[] { -1f, 1f })
                vb.Box(fr.P(u + s * (width * .5f + 0.1f), height * .5f, 0.05f), new Vector3(0.2f, height, 0.1f), "limestone", fr.yaw);
        }

        /// <summary>Climbing vine with leaf clusters and hanging wisteria up a facade edge and along the first floor.</summary>
        private static void Vine(VB vb, Fr fr, float u, float top, float spread, int seed)
        {
            var rnd = new System.Random(seed);
            vb.Pipe(fr.P(u, 0.1f, 0.12f), fr.P(u + 0.1f, top, 0.12f), 0.05f, "walnut", 6);
            vb.Pipe(fr.P(u + 0.1f, top, 0.12f), fr.P(u + spread, top + 0.2f, 0.14f), 0.04f, "walnut", 6);
            for (int i = 0; i < 9; i++)
            {
                float y = 0.8f + i * (top - 0.6f) / 8f;
                vb.Blob(fr.P(u + (float)(rnd.NextDouble() - 0.5) * 0.4f, y, 0.2f), new Vector3(0.62f, 0.5f, 0.32f), i % 2 == 0 ? "ivy" : "leafdark",
                    yaw: fr.yaw, rings: 4, segments: 7);
            }
            int n = Mathf.Max(3, Mathf.RoundToInt(Mathf.Abs(spread) / 0.35f));
            for (int i = 0; i <= n; i++)
            {
                float uu = u + spread * i / n;
                vb.Blob(fr.P(uu, top + 0.15f, 0.2f), new Vector3(0.55f, 0.42f, 0.36f), i % 2 == 0 ? "ivy" : "leafdark", yaw: fr.yaw, rings: 4, segments: 7);
                float len = 0.45f + (float)rnd.NextDouble() * 0.4f;
                vb.Blob(fr.P(uu + 0.1f, top - len * .5f, 0.26f), new Vector3(0.16f, len, 0.16f), i % 3 == 0 ? "wisterialight" : "wisteria", rings: 4, segments: 6);
            }
        }

        // ------------------------------------------------------------------ west row (facing east)

        private static void WestRow(VB vb)
        {
            // W1: the blue café (z -11.5..-2.5).
            var cafe = new Fr(new Vector3(WestX, 0, -7f), 90f);
            House(vb, cafe, 9f, 7.0f, 7f, "tl_stucco_blue", "stuccoblue", "shutterblue", 11, balcony: false);
            ShopWindow(vb, cafe, 2.6f, 3.0f, "tl_win_cafe");
            ShopWindow(vb, cafe, -2.6f, 3.0f, "tl_win_cafe");
            Door(vb, cafe, 0.2f, "awningblue");
            Awning(vb, cafe, 0f, 8.6f, 3.15f, 2.0f, "awningblue", "petalwhite");
            Vine(vb, cafe, 4.3f, 3.25f, -2.4f, 5);

            // W2: Pixel Wall house (z -2.5..3.0): the community pixel canvas (live surface) on the ground floor.
            var pix = new Fr(new Vector3(WestX, 0, 0.25f), 90f);
            House(vb, pix, 5.5f, 6.4f, 7f, "tl_stucco_cream", "stuccocream", "shuttergreen", 21);
            vb.LiveScreen("pixel_wall", "tl_pixel_idle", pix.P(0f, 0.7f, 0.06f), 3.6f, 90f, frame: "darkwood");
            vb.Box(pix.P(0f, 3.92f, 0.14f), new Vector3(4.4f, 0.1f, 0.24f), "iron", 90f);
            for (int i = 0; i < 3; i++)
            {
                vb.Pipe(pix.P(-1.4f + i * 1.4f, 3.92f, 0.24f), pix.P(-1.4f + i * 1.4f, 3.72f, 0.5f), 0.015f, "iron", 4);
                vb.Cylinder(pix.P(-1.4f + i * 1.4f, 3.62f, 0.5f), 0.09f, 0.1f, "glowamber", 8, glow: true, topRadius: 0.05f);
            }

            // W3: bakery (z 3..10) with a red-and-white awning.
            var bakery = new Fr(new Vector3(WestX, 0, 6.5f), 90f);
            House(vb, bakery, 7f, 7.2f, 7f, "tl_stucco_peach", "stuccopeach", "shuttergreen", 31, balcony: true);
            ShopWindow(vb, bakery, -1.3f, 3.6f, "tl_win_bakery");
            Door(vb, bakery, 2.3f, "barnred");
            Awning(vb, bakery, -0.2f, 6.6f, 3.15f, 1.8f, "awningred", "petalwhite");
            var board = bakery.P(2.0f, 0f, 1.6f);
            vb.BoxRot(board + new Vector3(0, 0.5f, 0.12f), new Vector3(0.6f, 1.0f, 0.04f), Quaternion.Euler(0, 90f, 0) * Quaternion.Euler(-12f, 0, 0), "darkwood");
            vb.BoxRot(board + new Vector3(0, 0.5f, -0.12f), new Vector3(0.6f, 1.0f, 0.04f), Quaternion.Euler(0, 90f, 0) * Quaternion.Euler(12f, 0, 0), "darkwood");
            vb.RegisterProp("sandwich_board", board + Vector3.up * 0.5f, new Vector3(0.4f, 1.0f, 0.65f), 90f);
            foreach (var (u, col) in new[] { (-3.1f, "petalpink"), (3.2f, "petalyellow") })
            {
                var pot = bakery.P(u, 0f, 0.5f);
                vb.Cylinder(pot, 0.3f, 0.5f, "terracotta", 14, topRadius: 0.36f);
                vb.RegisterProp("pot", pot + Vector3.up * 0.25f, new Vector3(0.72f, 0.5f, 0.72f));
                Flowers(vb, pot + Vector3.up * 0.5f, 0.45f, 0.4f, 90f, col.Length, tall: true);
            }

            // W4: vine-covered corner house (z 10..15.6), three floors.
            var corner = new Fr(new Vector3(WestX, 0, 12.8f), 90f);
            House(vb, corner, 5.6f, 9.4f, 7f, "tl_stucco_ochre", "stuccoochre", "shutterblue", 41, floors: 3);
            Door(vb, corner, 0.6f, "shuttergreen");
            Vine(vb, corner, -2.5f, 3.3f, 2.2f, 8);
            Vine(vb, corner, 2.4f, 6.2f, -1.8f, 9);
            // Background hill houses behind the west row.
            for (int i = 0; i < 4; i++)
            {
                var back = new Fr(new Vector3(WestX - 8.5f - (i % 2) * 3f, 2.2f + (i % 2), -9f + i * 7f), 90f);
                House(vb, back, 6f, 6f + (i % 3), 6f, i % 2 == 0 ? "tl_stucco_cream" : "tl_stucco_ochre", "stuccocream", null, 200 + i, far: true);
            }
        }

        // ------------------------------------------------------------------ north row (facing south) and the town hall

        private static void NorthRow(VB vb)
        {
            // N1 (x -20.6..-13) with billboard bb_02; N2 (x -13..-5.6) with an arcade.
            var n1 = new Fr(new Vector3(-16.8f, 0, NorthZ), 180f);
            House(vb, n1, 7.6f, 8.6f, 7f, "tl_stucco_cream", "stuccocream", "shuttergreen", 51, floors: 2);
            vb.LiveScreen("bb_02", "tl_bb_2_idle", n1.P(0f, 5.55f, 0.08f), 5.2f, 180f, frame: "iron");
            Door(vb, n1, 2.2f, "walnut");
            ShopWindow(vb, n1, -1.4f, 2.6f, "tl_win_parcels");
            var n2 = new Fr(new Vector3(-9.3f, 0, NorthZ), 180f);
            House(vb, n2, 7.4f, 7.0f, 7f, "tl_stucco_stone", "stuccostone", "shutterblue", 61, balcony: true);
            for (int i = 0; i < 3; i++)
            {
                float u = -2.4f + i * 2.4f;
                vb.Box(n2.P(u, 1.25f, 0.04f), new Vector3(1.6f, 2.5f, 0.05f), "ink", 180f);
                vb.CylinderRot(n2.P(u, 2.5f, 0.04f), Quaternion.Euler(0, 180f, 0) * Quaternion.Euler(90f, 0, 0), 0.8f, 0.02f, "ink", 12);
                vb.Box(n2.P(u - 1.15f, 1.6f, 0.12f), new Vector3(0.3f, 3.2f, 0.24f), "limestone", 180f);
            }
            Vine(vb, n2, 3.4f, 3.3f, -2.6f, 12);

            // Street up to the town hall: side walls of the strip, steps, terrace, the hall and the Timeline screen.
            for (int k = 0; k < 6; k++)
                vb.Box(new Vector3(0, (k + 1) * 0.09f, 21.25f + k * 0.45f), new Vector3(10.4f, (k + 1) * 0.18f, 0.46f), k % 2 == 0 ? "limestone" : "stuccocream");
            vb.Collider(new Vector3(0, 0.6f, 22.4f), new Vector3(10.4f, 1.2f, 2.8f));
            vb.Box(new Vector3(0, 0.55f, 27f), new Vector3(16f, 1.1f, 8.6f), "limestone");
            foreach (float sx in new[] { -1f, 1f })
                for (int k = 0; k < 9; k++)
                {
                    vb.Cylinder(new Vector3(sx * (5.6f + k * 0.5f), 1.1f, 23.0f), 0.09f, 0.72f, "stuccocream", 8, topRadius: 0.07f);
                    if (k == 8) vb.Box(new Vector3(sx * 7.6f, 1.9f, 23.0f), new Vector3(4.4f, 0.12f, 0.3f), "limestone");
                }
            var hall = new Fr(new Vector3(0, 1.1f, 25.4f), 180f);
            House(vb, hall, 12f, 9.2f, 7f, "tl_stucco_stone", "stuccostone", null, 71, roof: false, floors: 1);
            vb.Box(hall.P(0, 9.6f, -0.3f), new Vector3(5.2f, 1.6f, 0.6f), "stuccostone", 180f);
            vb.Triangle(hall.P(-2.8f, 10.4f, 0.02f), hall.P(2.8f, 10.4f, 0.02f), hall.P(0, 11.9f, 0.02f), "limestone");
            vb.Triangle(hall.P(-2.8f, 10.4f, -0.6f), hall.P(2.8f, 10.4f, -0.6f), hall.P(0, 11.9f, -0.6f), "limestone");
            vb.Box(hall.P(0, 9.2f, 0.12f), new Vector3(12.3f, 0.3f, 0.3f), "limestone", 180f);
            vb.Box(hall.P(0, 1.35f, 0.04f), new Vector3(2.0f, 2.7f, 0.06f), "darkwood", 180f);
            vb.CylinderRot(hall.P(0f, 2.7f, 0.04f), Quaternion.Euler(0, 180f, 0) * Quaternion.Euler(90f, 0, 0), 1.0f, 0.02f, "darkwood", 14);
            foreach (float s in new[] { -1f, 1f })
            {
                vb.Box(hall.P(s * 1.25f, 1.5f, 0.12f), new Vector3(0.36f, 3.0f, 0.24f), "limestone", 180f);
                vb.Box(hall.P(s * 4.6f, 4.5f, 0.15f), new Vector3(0.5f, 9.0f, 0.3f), "limestone", 180f);
            }
            vb.LiveScreen("timeline_screen", "tl_timeline_idle", hall.P(0f, 3.6f, 0.12f), 6.6f, 180f, frame: "iron");
            vb.PointLight(hall.P(0, 3.2f, 1.6f), "glowamber", 0.6f, 6f);

            // The street's side walls (N2 east end, N3 west end): painted stucco, windows, a vine, lamps on brackets.
            foreach (float sx in new[] { -1f, 1f })
            {
                var side = new Fr(new Vector3(sx * 5.6f, 0, NorthZ + 3.5f), sx < 0 ? 90f : -90f);
                vb.Sign(sx < 0 ? "tl_stucco_stone" : "tl_stucco_ochre", side.P(0, 0, 0.012f), 7f, side.yaw, sx < 0 ? 7.0f : 8.6f, lit: true);
                vb.Box(side.P(0, 0.32f, 0.04f), new Vector3(7.02f, 0.64f, 0.08f), "stoneshade", side.yaw);
                for (int k = 0; k < 2; k++) Window(vb, side, -1.6f + k * 3.2f, 3.95f, k == 1, sx < 0 ? "shutterblue" : "shuttergreen", true, 500 + k + (int)sx);
                Window(vb, side, 1.6f, 0.9f, false, null, false, 510);
                Vine(vb, side, -3.0f, 3.2f, 2.0f, 520 + (int)sx);
                var br = side.P(0.2f, 2.9f, 0f);
                vb.Pipe(br, br + side.f * 0.6f, 0.03f, "iron", 5);
                vb.Box(br + side.f * 0.6f - Vector3.up * 0.25f, new Vector3(0.24f, 0.36f, 0.24f), "glowamber", glow: true);
                vb.PointLight(br + side.f * 0.6f - Vector3.up * 0.3f, "glowamber", 0.5f, 4f);
            }

            // N3 (x 5.6..13) with billboard bb_01; N4 (x 13..20.6) under the clock tower.
            var n3 = new Fr(new Vector3(9.3f, 0, NorthZ), 180f);
            House(vb, n3, 7.4f, 8.6f, 7f, "tl_stucco_ochre", "stuccoochre", "shutterblue", 81, floors: 2);
            vb.LiveScreen("bb_01", "tl_bb_1_idle", n3.P(0f, 5.55f, 0.08f), 5.2f, 180f, frame: "iron");
            ShopWindow(vb, n3, 1.3f, 2.6f, "tl_win_cafe");
            Door(vb, n3, -2.2f, "shuttergreen");
            var n4 = new Fr(new Vector3(16.8f, 0, NorthZ), 180f);
            House(vb, n4, 7.6f, 7.2f, 7f, "tl_stucco_peach", "stuccopeach", "shuttergreen", 91, balcony: true);
            Door(vb, n4, 0f, "walnut");
            ClockTower(vb, new Vector3(16.6f, 0, 20.4f));
        }

        private static void ClockTower(VB vb, Vector3 b)
        {
            const float w = 4.2f, h = 16f;
            vb.Box(b + Vector3.up * h * .5f, new Vector3(w, h, w), "stuccostone");
            var front = new Fr(b + new Vector3(0, 0, -w * .5f), 180f);
            vb.Sign("tl_stucco_stone", front.P(0, 0, 0.012f), w, 180f, h, lit: true);
            var west = new Fr(b + new Vector3(-w * .5f, 0, 0), -90f);
            vb.Sign("tl_stucco_stone", west.P(0, 0, 0.012f), w, -90f, h, lit: true);
            foreach (var fr in new[] { front, west })
            {
                for (float y = 0.4f; y < h; y += 0.6f)
                    foreach (float s in new[] { -1f, 1f })
                        vb.Box(fr.P(s * (w * .5f - 0.2f), y + 0.25f, 0.03f), new Vector3(((int)(y / 0.6f) % 2 == 0) ? 0.42f : 0.3f, 0.5f, 0.06f), "limestone", fr.yaw);
                vb.Sign("tl_clock", fr.P(0, 10.6f, 0.08f), 2.6f, fr.yaw, 2.6f, lit: true);
                for (int k = 0; k < 2; k++)
                {
                    float u = -0.8f + k * 1.6f;
                    vb.Box(fr.P(u, 14.0f, 0.02f), new Vector3(0.9f, 1.6f, 0.06f), "ink", fr.yaw);
                    vb.CylinderRot(fr.P(u, 14.8f, 0.03f), Quaternion.Euler(0, fr.yaw, 0) * Quaternion.Euler(90f, 0, 0), 0.45f, 0.02f, "ink", 10);
                }
                vb.Box(fr.P(0, 9.0f, 0.1f), new Vector3(w + 0.2f, 0.25f, 0.2f), "limestone", fr.yaw);
                vb.Box(fr.P(0, 13.45f, 0.1f), new Vector3(w + 0.2f, 0.25f, 0.2f), "limestone", fr.yaw);
            }
            vb.Box(b + Vector3.up * (h + 0.15f), new Vector3(w + 0.5f, 0.3f, w + 0.5f), "limestone");
            vb.CylinderRot(b + Vector3.up * (h + 0.3f), Quaternion.Euler(0, 45f, 0), (w + 0.6f) * 0.7071f, 3.4f, "rooftile", 4, topRadius: 0f);
            vb.Sphere(b + Vector3.up * (h + 3.75f), 0.18f, "brass", rings: 4, segments: 8);
        }

        // ------------------------------------------------------------------ east row (facing west)

        private static void EastRow(VB vb)
        {
            // E1: corner house (z 10..15.6) with balconies; bunting anchor.
            var e1 = new Fr(new Vector3(EastX, 0, 12.8f), -90f);
            House(vb, e1, 5.6f, 9.4f, 7f, "tl_stucco_cream", "stuccocream", "shuttergreen", 101, floors: 3, balcony: true);
            Door(vb, e1, -0.8f, "walnut");
            Vine(vb, e1, 2.5f, 3.3f, -2.0f, 15);
            // E2: toyshop (z 3..10), green awning.
            var toys = new Fr(new Vector3(EastX, 0, 6.5f), -90f);
            House(vb, toys, 7f, 7.2f, 7f, "tl_stucco_sage", "stuccosage", "shutterblue", 111);
            ShopWindow(vb, toys, 1.2f, 3.8f, "tl_win_toys");
            Door(vb, toys, -2.4f, "awninggreen");
            Awning(vb, toys, 0.2f, 6.6f, 3.15f, 1.8f, "awninggreen", "petalwhite");
            // E3: parcels shop (z -3.5..3) with the Helius-style orb lamp and the Mad Lads-style poster (Eggs).
            var parcels = new Fr(new Vector3(EastX, 0, -0.25f), -90f);
            House(vb, parcels, 6.5f, 6.8f, 7f, "tl_stucco_ochre", "stuccoochre", "shutterblue", 121);
            ShopWindow(vb, parcels, -1.6f, 2.4f, "tl_win_parcels");
            Door(vb, parcels, 1.4f, "walnut");
            // E4: market building (z -12..-3.5), peach, arcade of doors behind the stall.
            var market = new Fr(new Vector3(EastX, 0, -7.75f), -90f);
            House(vb, market, 8.5f, 6.6f, 7f, "tl_stucco_peach", "stuccopeach", "shuttergreen", 131, balcony: true);
            Door(vb, market, -2.5f, "walnut");
            Door(vb, market, 2.5f, "walnut");
            ShopWindow(vb, market, 0f, 2.2f, "tl_win_bakery");
            for (int i = 0; i < 4; i++)
            {
                var back = new Fr(new Vector3(EastX + 8.5f + (i % 2) * 3f, 2.2f + (i % 2), -9f + i * 7f), -90f);
                House(vb, back, 6f, 6f + (i % 3), 6f, i % 2 == 0 ? "tl_stucco_peach" : "tl_stucco_cream", "stuccopeach", null, 300 + i, far: true);
            }
        }

        // ------------------------------------------------------------------ south edge (camera side)

        private static void SouthEdge(VB vb)
        {
            const float z = -15.5f;
            foreach (var (x0, x1) in new[] { (-21.5f, -3f), (3f, 21.5f) })
            {
                float cx = (x0 + x1) * .5f, len = x1 - x0;
                vb.Box(new Vector3(cx, 0.3f, z), new Vector3(len, 0.6f, 0.5f), "limestone", collider: true);
                vb.Box(new Vector3(cx, 0.63f, z), new Vector3(len + 0.1f, 0.06f, 0.6f), "stuccocream");
                for (float x = x0 + 0.2f; x <= x1 - 0.1f; x += 0.24f)
                    vb.Box(new Vector3(x, 1.08f, z), new Vector3(0.025f, 0.9f, 0.025f), "iron");
                vb.Box(new Vector3(cx, 1.52f, z), new Vector3(len, 0.05f, 0.05f), "iron");
                vb.Box(new Vector3(cx, 0.78f, z), new Vector3(len, 0.04f, 0.04f), "iron");
                for (float x = x0 + 1.5f; x < x1; x += 3f)
                {
                    vb.Cylinder(new Vector3(x, 0.66f, z), 0.06f, 1.0f, "iron", 6);
                    vb.Sphere(new Vector3(x, 1.7f, z), 0.08f, "iron", rings: 3, segments: 6);
                }
                vb.Obstacle(new Vector3(cx, 0.5f, z), new Vector3(len, 1f, 0.6f));
                // Flower troughs outside the railing (camera foreground).
                vb.Box(new Vector3(cx, 0.35f, z - 0.75f), new Vector3(len, 0.7f, 0.7f), "stoneshade");
                Flowers(vb, new Vector3(cx, 0.7f, z - 0.75f), len - 0.3f, 0.5f, 0f, (int)x0 + 50, tall: true);
            }
            foreach (float sx in new[] { -1f, 1f })
            {
                vb.Box(new Vector3(sx * 3.2f, 1.0f, z), new Vector3(0.6f, 2.0f, 0.6f), "limestone", collider: true);
                vb.Sphere(new Vector3(sx * 3.2f, 2.2f, z), 0.25f, "stuccocream", rings: 5, segments: 8);
            }
        }

        // ------------------------------------------------------------------ community eggs (our own drawings)

        private static void Eggs(VB vb)
        {
            CafeTerrace(vb);
            JupiterCat(vb, new Vector3(WestX + 0.2f, 3.95f, -4.0f), 90f);
            PhantomGhost(vb, new Vector3(-12.6f, 0f, -12.2f));
            PopcatWindow(vb);
            OrePlot(vb, new Vector3(-8.2f, 0f, -11.4f));
            BonkStall(vb, new Vector3(13.4f, 0f, -4.4f));
            IglooCart(vb, new Vector3(11.0f, 0f, -8.6f), 225f);
            CapsuleMachine(vb, new Vector3(6.6f, 0f, -13.2f), 200f);
            Dressing(vb);
            HeliusLamp(vb);
            MadLadsPoster(vb);
            ClayDino(vb);
            Bunting(vb, new Vector3(-20.5f, 6.6f, 13.6f), new Vector3(-14.5f, 6.6f, NorthZ - 0.02f), 11, 0);
            Bunting(vb, new Vector3(14.5f, 6.4f, NorthZ - 0.02f), new Vector3(20.5f, 6.6f, 11.2f), 11, 1);
            ColosseumHall(vb, new Vector3(-21f, 2.4f, 36f));
        }

        private static void CafeTerrace(VB vb)
        {
            var tables = new[] { new Vector3(-17.6f, 0, -8.9f), new Vector3(-14.6f, 0, -6.6f), new Vector3(-17.6f, 0, -4.4f) };
            for (int t = 0; t < tables.Length; t++)
            {
                var p = tables[t];
                vb.ModelAt("cafe_table_round", p, 0f, 1f, recolor: "walnut");
                for (int c = 0; c < 3; c++)
                {
                    float a = (c * 120f + t * 40f) * Mathf.Deg2Rad;
                    var cp = p + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 0.82f;
                    vb.ModelAt(c % 2 == 0 ? "cafe_chair_a" : "cafe_chair_b", cp, c * 120f + t * 40f + 180f, 1f, recolor: "awningblue");
                }
                vb.Cylinder(p + new Vector3(0.18f, 0.76f, 0.1f), 0.1f, 0.012f, "petalwhite", 12);
                vb.Cylinder(p + new Vector3(0.18f, 0.772f, 0.1f), 0.045f, 0.08f, "petalwhite", 10);
                if (t != 0) vb.Blob(p + new Vector3(-0.2f, 0.8f, -0.12f), new Vector3(0.16f, 0.07f, 0.09f), "pizza", yaw: 30f);
            }
            // Seeker-style phone (back up, three lens dots, a small engraved shield) beside a coffee on table 1.
            var tp = tables[0] + new Vector3(-0.15f, 0.765f, -0.15f);
            vb.Box(tp, new Vector3(0.09f, 0.012f, 0.18f), "black", 20f);
            var q = Quaternion.Euler(0, 20f, 0);
            for (int i = 0; i < 3; i++)
                vb.Cylinder(tp + q * new Vector3(-0.022f + (i % 2) * 0.024f, 0.006f, 0.05f + (i / 2) * 0.024f), 0.009f, 0.004f, "darkgrey", 8);
            vb.Box(tp + q * new Vector3(0, 0.007f, -0.03f), new Vector3(0.02f, 0.002f, 0.024f), "steel", 20f);
            // Parasol over table 2.
            vb.ModelAt("parasol", tables[1], 0f, 1.05f, recolor: "petalwhite");
            // dogwifhat-style dog under table 1: tan, sitting very still, rose-pink knit beanie.
            DogWithBeanie(vb, tables[0] + new Vector3(0.1f, 0f, 0.05f), 120f);
        }

        private static void DogWithBeanie(VB vb, Vector3 p, float facing)
        {
            var q = Quaternion.Euler(0, facing, 0);
            vb.Blob(p + Vector3.up * 0.2f, new Vector3(0.3f, 0.36f, 0.38f), "tan", yaw: facing, rings: 6, segments: 10);
            vb.Blob(p + q * new Vector3(0, 0.08f, 0.14f) , new Vector3(0.26f, 0.14f, 0.2f), "tan", yaw: facing);
            var head = p + q * new Vector3(0, 0.46f, 0.1f);
            vb.Sphere(head, 0.14f, "tan", rings: 6, segments: 10);
            vb.Blob(head + q * new Vector3(0, -0.04f, 0.12f), new Vector3(0.12f, 0.09f, 0.12f), "cream", yaw: facing);
            vb.Sphere(head + q * new Vector3(0, -0.02f, 0.19f), 0.025f, "black", rings: 3, segments: 6);
            foreach (float s in new[] { -1f, 1f })
            {
                vb.Sphere(head + q * new Vector3(s * 0.055f, 0.03f, 0.125f), 0.018f, "black", rings: 3, segments: 6);
                vb.Blob(head + q * new Vector3(s * 0.13f, -0.02f, 0f), new Vector3(0.05f, 0.14f, 0.08f), "leather", yaw: facing);
            }
            vb.Cylinder(head + Vector3.up * 0.06f, 0.145f, 0.12f, "dustpink", 12, topRadius: 0.11f);
            vb.Cylinder(head + Vector3.up * 0.05f, 0.15f, 0.03f, "petalpink", 12);
            vb.Sphere(head + Vector3.up * 0.21f, 0.045f, "petalpink", rings: 4, segments: 6);
        }

        private static void JupiterCat(VB vb, Vector3 sill, float facing)
        {
            // Small white cat in a white-and-blue astronaut suit, bubble helmet ring, banded planet patch; napping.
            var q = Quaternion.Euler(0, facing, 0);
            vb.Blob(sill + Vector3.up * 0.1f, new Vector3(0.36f, 0.2f, 0.22f), "petalwhite", yaw: facing + 90f, rings: 6, segments: 10);
            vb.Blob(sill + q * new Vector3(0.0f, 0.12f, 0.0f), new Vector3(0.12f, 0.06f, 0.24f), "awningblue", yaw: facing);
            var head = sill + q * new Vector3(0.2f, 0.16f, 0.03f);
            vb.Sphere(head, 0.1f, "petalwhite", rings: 6, segments: 10);
            foreach (float s in new[] { -1f, 1f })
                vb.CylinderRot(head + q * new Vector3(s * 0.05f, 0.07f, 0f), q, 0.03f, 0.06f, "petalwhite", 5, topRadius: 0f);
            for (int i = 0; i < 2; i++) vb.Box(head + q * new Vector3((i - 0.5f) * 0.06f, 0.0f, 0.095f), new Vector3(0.03f, 0.005f, 0.004f), "black", facing);
            for (int i = 0; i < 16; i++)
            {
                float a0 = i * Mathf.PI * 2f / 16f, a1 = (i + 1) * Mathf.PI * 2f / 16f;
                vb.Pipe(head + q * new Vector3(0, Mathf.Sin(a0) * 0.16f, Mathf.Cos(a0) * 0.16f), head + q * new Vector3(0, Mathf.Sin(a1) * 0.16f, Mathf.Cos(a1) * 0.16f), 0.008f, "waterlight", 4);
            }
            var patch = sill + q * new Vector3(-0.05f, 0.16f, 0.1f);
            vb.Cylinder(patch, 0.035f, 0.01f, "ctgreen", 10);
            vb.Box(patch + Vector3.up * 0.011f, new Vector3(0.06f, 0.002f, 0.012f), "cyan", facing + 30f);
            vb.Blob(sill + q * new Vector3(-0.22f, 0.06f, -0.05f), new Vector3(0.22f, 0.05f, 0.05f), "petalwhite", yaw: facing + 20f);
        }

        private static void PhantomGhost(VB vb, Vector3 lamp)
        {
            // Friendly purple ghost of our own design (round cheeks, small scarf) peeking from behind the lamp post.
            var p = lamp + new Vector3(0.32f, 0f, 0.3f);
            vb.Blob(p + Vector3.up * 1.15f, new Vector3(0.56f, 0.7f, 0.5f), "ghostpurple", rings: 8, segments: 12);
            for (int i = 0; i < 4; i++)
                vb.Sphere(p + new Vector3(-0.18f + i * 0.12f, 0.8f, 0f), 0.09f, "ghostpurple", rings: 4, segments: 8);
            foreach (float s in new[] { -1f, 1f })
            {
                vb.Blob(p + new Vector3(s * 0.1f - 0.1f, 1.28f, -0.22f), new Vector3(0.06f, 0.1f, 0.03f), "black");
                vb.Sphere(p + new Vector3(s * 0.17f - 0.1f, 1.16f, -0.2f), 0.045f, "dustpink", rings: 3, segments: 6);
            }
            vb.Blob(p + new Vector3(-0.05f, 0.98f, -0.08f), new Vector3(0.5f, 0.1f, 0.4f), "ctorange", rings: 4, segments: 8);
            vb.Blob(p + new Vector3(0.18f, 0.9f, -0.2f), new Vector3(0.08f, 0.18f, 0.05f), "ctorange");
            vb.Blob(p + new Vector3(-0.3f, 1.1f, -0.05f), new Vector3(0.18f, 0.1f, 0.1f), "ghostpurple", yaw: 30f);
        }

        private static void PopcatWindow(VB vb)
        {
            // Brown-and-white cat in the bakery window, mouth a perfect round O, a ring doughnut on a string before it.
            var fr = new Fr(new Vector3(WestX, 0, 6.5f), 90f);
            var c = fr.P(-0.2f, 0.9f, 0.32f);
            vb.Blob(c + Vector3.up * 0.0f, new Vector3(0.38f, 0.32f, 0.28f), "petalwhite", yaw: 90f, rings: 6, segments: 10);
            var head = c + Vector3.up * 0.32f;
            vb.Sphere(head, 0.19f, "petalwhite", rings: 7, segments: 12);
            vb.Blob(head + fr.r * 0.08f + Vector3.up * 0.07f, new Vector3(0.18f, 0.14f, 0.2f), "leather", yaw: 90f);
            foreach (float s in new[] { -1f, 1f })
            {
                vb.CylinderRot(head + fr.r * (s * 0.11f) + Vector3.up * 0.13f, Quaternion.Euler(0, 90f, 0) * Quaternion.Euler(0, 0, s * -12f), 0.06f, 0.11f, s > 0 ? "leather" : "petalwhite", 5, topRadius: 0f);
                vb.Sphere(head + fr.r * (s * 0.07f) + Vector3.up * 0.04f + fr.f * 0.165f, 0.025f, "black", rings: 3, segments: 6);
            }
            vb.CylinderRot(head + fr.f * 0.17f - Vector3.up * 0.06f, Quaternion.Euler(0, 90f, 0) * Quaternion.Euler(90f, 0, 0), 0.06f, 0.03f, "black", 14);
            var dn = fr.P(-0.2f, 1.24f, 0.62f);
            vb.Pipe(dn + Vector3.up * 0.95f, dn + Vector3.up * 0.09f, 0.004f, "offwhite", 3);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                vb.Sphere(dn + fr.r * (Mathf.Cos(a) * 0.08f) + Vector3.up * (Mathf.Sin(a) * 0.08f), 0.035f, i % 3 == 0 ? "petalpink" : "pizza", rings: 3, segments: 6);
            }
        }

        private static void OrePlot(VB vb, Vector3 c)
        {
            // 5x5 patch of copper-sheen tiles, a pickaxe leaning on the fence, a wheelbarrow of glowing ore; iron fence.
            const float t = 0.78f;
            string[] tones = { "copper", "rosegold", "copper", "pumpkin", "copper" };
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 5; j++)
                {
                    var tc = c + new Vector3((i - 2) * t, 0f, (j - 2) * t);
                    vb.Box(tc + Vector3.up * 0.04f, new Vector3(t - 0.05f, 0.08f, t - 0.05f), "rust");
                    vb.Box(tc + Vector3.up * 0.085f, new Vector3(t - 0.16f, 0.012f, t - 0.16f), tones[(i * 3 + j * 7) % tones.Length]);
                    vb.Box(tc + new Vector3(-0.1f, 0.093f, -0.1f), new Vector3(t - 0.42f, 0.006f, 0.05f), "gold", 0f);
                    if ((i + j * 2) % 7 == 3) vb.Box(tc + Vector3.up * 0.095f, new Vector3(0.12f, 0.004f, 0.12f), "oreglow", 45f, glow: true);
                }
            vb.Box(c + new Vector3(0, 0.01f, 0), new Vector3(t * 5 + 0.2f, 0.02f, t * 5 + 0.2f), "soil");
            float half = t * 2.5f + 0.4f;
            foreach (var (a, b) in new[] { (new Vector3(-half, 0, half), new Vector3(half, 0, half)), (new Vector3(half, 0, half), new Vector3(half, 0, -half)),
                                           (new Vector3(-half, 0, -half), new Vector3(half - 1.2f, 0, -half)) })
            {
                var dir = b - a;
                int n = Mathf.Max(2, Mathf.RoundToInt(dir.magnitude / 0.2f));
                for (int k = 0; k <= n; k++) vb.Box(c + Vector3.Lerp(a, b, k / (float)n) + Vector3.up * 0.45f, new Vector3(0.025f, 0.9f, 0.025f), "iron");
                vb.Pipe(c + a + Vector3.up * 0.88f, c + b + Vector3.up * 0.88f, 0.02f, "iron", 4);
                vb.Pipe(c + a + Vector3.up * 0.2f, c + b + Vector3.up * 0.2f, 0.015f, "iron", 4);
                for (int k = 0; k <= n; k += 4) vb.CylinderRot(c + Vector3.Lerp(a, b, k / (float)n) + Vector3.up * 0.9f, Quaternion.identity, 0.03f, 0.1f, "iron", 4, topRadius: 0f);
            }
            vb.Obstacle(c + Vector3.up * 0.5f, new Vector3(half * 2, 1f, half * 2));
            vb.Collider(c + Vector3.up * 0.5f, new Vector3(half * 2, 1f, half * 2));
            // Pickaxe leaning on the north fence.
            var ph = c + new Vector3(0.6f, 0f, half - 0.15f);
            vb.BoxRot(ph + new Vector3(0, 0.5f, -0.18f), new Vector3(0.05f, 1.05f, 0.05f), Quaternion.Euler(-20f, 0, 0), "oak");
            vb.BoxRot(ph + new Vector3(0, 0.98f, 0.0f), new Vector3(0.55f, 0.07f, 0.06f), Quaternion.Euler(-20f, 0, 8f), "darksteel");
            // Wheelbarrow of ore just outside the plot (east side).
            var wb = c + new Vector3(half + 0.8f, 0f, -1.4f);
            vb.BoxRot(wb + new Vector3(0, 0.45f, 0), new Vector3(0.62f, 0.3f, 0.85f), Quaternion.Euler(-6f, 0, 0), "darksteel");
            vb.CylinderRot(wb + new Vector3(-0.06f, 0.18f, 0.55f), Quaternion.Euler(0, 0, 90f), 0.18f, 0.12f, "iron", 12);
            foreach (float s in new[] { -1f, 1f })
            {
                vb.Pipe(wb + new Vector3(s * 0.26f, 0.45f, -0.3f), wb + new Vector3(s * 0.3f, 0.62f, -0.95f), 0.02f, "oak", 5);
                vb.Pipe(wb + new Vector3(s * 0.22f, 0.3f, -0.25f), wb + new Vector3(s * 0.22f, 0f, -0.3f), 0.02f, "iron", 4);
            }
            for (int i = 0; i < 9; i++)
                vb.Sphere(wb + new Vector3(-0.2f + (i % 3) * 0.2f, 0.62f + (i / 3) * 0.03f, -0.2f + (i / 3) * 0.2f), 0.09f, i % 2 == 0 ? "oreglow" : "gold", rings: 3, segments: 6);
            vb.RegisterProp("wheelbarrow", wb + Vector3.up * 0.4f, new Vector3(0.7f, 0.8f, 1.3f));
            vb.PointLight(wb + Vector3.up * 0.9f, "oreglow", 0.35f, 2.2f);
        }

        private static void BonkStall(VB vb, Vector3 p)
        {
            // Market stall: yellow-and-orange striped awning, a red exclamation-mark pennant, a mouthless sunny dog
            // balloon tied to the post, fruit and veg crates, sunflowers. No disc badge, no letters.
            const float yaw = 150f;
            var q = Quaternion.Euler(0, yaw, 0);
            Vector3 L(float x, float y, float z) => p + q * new Vector3(x, y, z);
            vb.Box(L(0, 0.45f, 0), new Vector3(1.0f, 0.9f, 3.0f), "walnut", yaw);
            vb.Box(L(0, 0.92f, 0), new Vector3(1.15f, 0.06f, 3.15f), "oak", yaw);
            foreach (var (x, z) in new[] { (-0.55f, -1.55f), (-0.55f, 1.55f), (0.55f, -1.55f), (0.55f, 1.55f) })
                vb.Box(L(x, 1.3f, z), new Vector3(0.07f, 2.6f, 0.07f), "walnut", yaw);
            vb.RegisterProp("market_stall", L(0, 0.5f, 0), new Vector3(1.2f, 1.0f, 3.2f), yaw);
            int n = 8;
            for (int i = 0; i < n; i++)
            {
                float z = -1.7f + (i + 0.5f) * 3.4f / n;
                vb.BoxRot(L(0, 2.65f, z), new Vector3(1.6f, 0.04f, 3.4f / n + 0.002f), q * Quaternion.Euler(0, 0, -14f), i % 2 == 0 ? "bonkyellow" : "bonkorange");
                vb.Box(L(0.82f, 2.36f, z), new Vector3(0.03f, 0.3f, 3.4f / n), i % 2 == 0 ? "bonkyellow" : "bonkorange", yaw);
                vb.Sphere(L(0.82f, 2.2f, z), 3.4f / n * 0.5f, i % 2 == 0 ? "bonkyellow" : "bonkorange", rings: 3, segments: 6);
            }
            // Crates of oranges, apples, greens and lemons on the counter and the ground.
            string[] fruit = { "ctorange", "petalred", "grass", "petalyellow", "ctorange" };
            for (int c = 0; c < 5; c++)
            {
                var cp = L(0.05f, 0.95f, -1.2f + c * 0.6f);
                vb.Box(cp + Vector3.up * 0.1f, new Vector3(0.5f, 0.2f, 0.5f), "plywood", yaw + c * 4f);
                for (int k = 0; k < 6; k++)
                    vb.Sphere(cp + new Vector3(-0.13f + (k % 3) * 0.13f, 0.22f, -0.1f + (k / 3) * 0.2f), 0.07f, fruit[c], rings: 4, segments: 7);
            }
            foreach (var (x, z) in new[] { (0.95f, -1.0f), (0.95f, 0.6f) })
            {
                var cp = L(x, 0f, z);
                vb.Box(cp + Vector3.up * 0.18f, new Vector3(0.55f, 0.36f, 0.55f), "plywood");
                for (int k = 0; k < 9; k++)
                    vb.Sphere(cp + new Vector3(-0.15f + (k % 3) * 0.15f, 0.4f, -0.15f + (k / 3) * 0.15f), 0.075f, k % 2 == 0 ? "ctorange" : "petalred", rings: 4, segments: 7);
                vb.RegisterProp("crate", cp + Vector3.up * 0.2f, new Vector3(0.6f, 0.4f, 0.6f));
            }
            // Sunflowers in a bucket.
            var bucket = L(0.9f, 0f, 1.5f);
            vb.Cylinder(bucket, 0.2f, 0.4f, "steel", 12, topRadius: 0.24f);
            for (int k = 0; k < 4; k++)
            {
                var top = bucket + new Vector3(-0.1f + k * 0.07f, 1.0f + (k % 2) * 0.15f, -0.05f + (k % 3) * 0.05f);
                vb.Pipe(bucket + Vector3.up * 0.3f, top, 0.012f, "grass", 4);
                vb.Cylinder(top, 0.13f, 0.02f, "petalyellow", 12);
                vb.Cylinder(top + Vector3.up * 0.015f, 0.06f, 0.015f, "leather", 10);
            }
            vb.RegisterProp("bucket", bucket + Vector3.up * 0.3f, new Vector3(0.5f, 0.6f, 0.5f));
            // Red triangular pennant with a white exclamation mark (a symbol, no letters), hung from the awning post.
            var pole = L(0.55f, 2.6f, 1.55f);
            vb.Triangle(pole + Vector3.up * 0.1f, pole + Vector3.up * 0.1f + q * new Vector3(0, 0, 0.7f) + new Vector3(0, 0, 0), pole - Vector3.up * 0.75f + q * new Vector3(0, 0, 0.35f), "petalred");
            var markBase = pole + q * new Vector3(-0.012f, 0f, 0.35f);
            vb.Box(markBase + Vector3.up * -0.12f, new Vector3(0.012f, 0.26f, 0.06f), "petalwhite", yaw);
            vb.Box(markBase + Vector3.up * -0.38f, new Vector3(0.012f, 0.06f, 0.06f), "petalwhite", yaw);
            vb.Box(markBase + q * new Vector3(0.024f, 0, 0) + Vector3.up * -0.12f, new Vector3(0.012f, 0.26f, 0.06f), "petalwhite", yaw);
            vb.Box(markBase + q * new Vector3(0.024f, 0, 0) + Vector3.up * -0.38f, new Vector3(0.012f, 0.06f, 0.06f), "petalwhite", yaw);
            // Mouthless sunny dog balloon on a string tied to the front post.
            var tie = L(-0.55f, 1.0f, -1.55f);
            var bal = tie + new Vector3(-0.5f, 2.3f, 0.2f);
            vb.Pipe(tie, bal - Vector3.up * 0.4f, 0.005f, "offwhite", 3);
            vb.Sphere(bal, 0.42f, "bonkyellow", rings: 8, segments: 14);
            vb.Blob(bal + new Vector3(-0.02f, -0.12f, -0.33f), new Vector3(0.4f, 0.24f, 0.2f), "petalwhite", rings: 5, segments: 8);
            vb.Sphere(bal + new Vector3(0, -0.06f, -0.42f), 0.05f, "black", rings: 3, segments: 6);
            foreach (float s in new[] { -1f, 1f })
            {
                vb.CylinderRot(bal + new Vector3(s * 0.25f, 0.28f, 0f), Quaternion.Euler(0, 0, s * -18f), 0.13f, 0.28f, "bonkorange", 6, topRadius: 0.02f);
                vb.Sphere(bal + new Vector3(s * 0.14f, 0.1f, -0.36f), 0.045f, "black", rings: 3, segments: 6);
                vb.Sphere(bal + new Vector3(s * 0.25f, -0.08f, -0.3f), 0.06f, "petalpink", rings: 3, segments: 6);
            }
            vb.CylinderRot(bal - Vector3.up * 0.44f, Quaternion.identity, 0.05f, 0.06f, "bonkorange", 6, topRadius: 0.01f);
        }

        private static void IglooCart(VB vb, Vector3 p, float facing)
        {
            // Igloo-shaped shaved-ice cart: our own chubby blue penguin plush on the counter, a tiny wizard-hat penguin,
            // a blue-and-white umbrella, cups of shaved ice in candy colours.
            var q = Quaternion.Euler(0, facing, 0);
            Vector3 L(float x, float y, float z) => p + q * new Vector3(x, y, z);
            vb.Box(L(0, 0.45f, 0), new Vector3(1.2f, 0.5f, 1.6f), "petalwhite", facing);
            vb.Blob(L(0, 0.75f, -0.1f), new Vector3(1.3f, 1.2f, 1.5f), "petalwhite", yaw: facing, rings: 8, segments: 14);
            for (int r = 0; r < 3; r++) vb.Arc(L(0, 0, -0.1f), 0.66f - r * 0.1f, 0.67f - r * 0.1f, 0f, 360f, 0.85f + r * 0.18f, 0.86f + r * 0.18f, "penguinblue", 20);
            vb.Box(L(0, 0.72f, 0.82f), new Vector3(1.1f, 0.06f, 0.36f), "penguinblue", facing);
            vb.Box(L(0, 0.48f, 0.8f), new Vector3(0.7f, 0.42f, 0.04f), "waterlight", facing);
            foreach (float s in new[] { -1f, 1f })
            {
                vb.CylinderRot(L(s * 0.62f, 0.22f, -0.35f), q * Quaternion.Euler(0, 0, 90f), 0.22f, 0.08f, "iron", 12);
                vb.Cylinder(L(s * 0.62f + (s > 0 ? -0.08f : 0f), 0.22f, -0.35f), 0.07f, 0.1f, "steel", 8);
            }
            vb.Pipe(L(0, 0.9f, -0.95f), L(0, 0.9f, -1.35f), 0.03f, "iron", 6);
            vb.RegisterProp("igloo_cart", L(0, 0.6f, 0), new Vector3(1.4f, 1.2f, 1.9f), facing);
            string[] ice = { "petalpink", "waterlight", "petalyellow", "mint" };
            for (int i = 0; i < 4; i++)
            {
                var cup = L(-0.4f + i * 0.27f, 0.75f, 0.85f);
                vb.Cylinder(cup, 0.05f, 0.09f, "petalwhite", 8, topRadius: 0.065f);
                vb.Sphere(cup + Vector3.up * 0.12f, 0.065f, ice[i], rings: 4, segments: 7);
            }
            // Chubby blue penguin plush on the igloo top.
            var pg = L(0.1f, 1.33f, 0f);
            vb.Blob(pg + Vector3.up * 0.25f, new Vector3(0.44f, 0.52f, 0.4f), "penguinblue", yaw: facing, rings: 7, segments: 12);
            vb.Blob(pg + Vector3.up * 0.22f + q * new Vector3(0, 0, 0.12f), new Vector3(0.3f, 0.36f, 0.2f), "petalwhite", yaw: facing);
            vb.CylinderRot(pg + Vector3.up * 0.4f + q * new Vector3(0, 0, 0.2f), q * Quaternion.Euler(90f, 0, 0), 0.045f, 0.08f, "ctorange", 6, topRadius: 0f);
            foreach (float s in new[] { -1f, 1f })
            {
                vb.Sphere(pg + Vector3.up * 0.47f + q * new Vector3(s * 0.08f, 0, 0.17f), 0.03f, "black", rings: 3, segments: 6);
                vb.Blob(pg + Vector3.up * 0.04f + q * new Vector3(s * 0.1f, 0, 0.12f), new Vector3(0.11f, 0.05f, 0.14f), "ctorange", yaw: facing);
                vb.Blob(pg + Vector3.up * 0.26f + q * new Vector3(s * 0.22f, 0, 0), new Vector3(0.07f, 0.24f, 0.15f), "penguinblue", yaw: facing + s * 20f);
                vb.Sphere(pg + Vector3.up * 0.4f + q * new Vector3(s * 0.15f, 0, 0.13f), 0.035f, "petalpink", rings: 3, segments: 6);
            }
            // Tiny wizard-hat penguin beside it.
            var tw = L(-0.35f, 0.75f, 0.62f);
            vb.Blob(tw + Vector3.up * 0.11f, new Vector3(0.18f, 0.22f, 0.17f), "penguinblue", yaw: facing);
            vb.Blob(tw + Vector3.up * 0.1f + q * new Vector3(0, 0, 0.06f), new Vector3(0.11f, 0.14f, 0.08f), "petalwhite", yaw: facing);
            vb.CylinderRot(tw + Vector3.up * 0.21f, Quaternion.identity, 0.11f, 0.24f, "ctblue", 10, topRadius: 0f);
            vb.Cylinder(tw + Vector3.up * 0.2f, 0.13f, 0.02f, "ctblue", 10);
            // Umbrella.
            var up = L(0.5f, 0.9f, -0.6f);
            vb.Pipe(up, up + Vector3.up * 1.4f, 0.02f, "iron", 5);
            for (int i = 0; i < 8; i++)
            {
                float a0 = i * Mathf.PI / 4f, a1 = (i + 1) * Mathf.PI / 4f;
                var top = up + Vector3.up * 1.62f;
                vb.Triangle(top, up + Vector3.up * 1.32f + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * 0.95f,
                    up + Vector3.up * 1.32f + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * 0.95f, i % 2 == 0 ? "penguinblue" : "petalwhite");
            }
        }

        private static void CapsuleMachine(VB vb, Vector3 p, float facing)
        {
            // Curbside capsule vending machine: red base, clear globe full of half-green, half-white capsules.
            vb.Box(p + Vector3.up * 0.5f, new Vector3(0.6f, 1.0f, 0.6f), "petalred", facing);
            vb.Box(p + Vector3.up * 1.02f, new Vector3(0.66f, 0.06f, 0.66f), "petalred", facing);
            var q = Quaternion.Euler(0, facing, 0);
            vb.Cylinder(p + Vector3.up * 0.5f + q * new Vector3(0, 0, 0.3f), 0.09f, 0.02f, "steel", 12);
            vb.Box(p + Vector3.up * 0.3f + q * new Vector3(0, 0, 0.31f), new Vector3(0.16f, 0.12f, 0.02f), "black", facing);
            var g = p + Vector3.up * 1.42f;
            vb.Sphere(g, 0.36f, "waterlight", rings: 8, segments: 14);
            var rnd = new System.Random(7);
            for (int i = 0; i < 26; i++)
            {
                float th = (float)rnd.NextDouble() * Mathf.PI * 2f, ph = Mathf.Acos(1f - 2f * (float)rnd.NextDouble());
                var n = new Vector3(Mathf.Sin(ph) * Mathf.Cos(th), Mathf.Cos(ph), Mathf.Sin(ph) * Mathf.Sin(th));
                if (n.y > 0.75f) continue;
                var c = g + n * 0.355f;
                var along = Vector3.Cross(n, Vector3.up).normalized * 0.04f;
                vb.Sphere(c - along * 0.5f, 0.045f, "ctgreen", rings: 3, segments: 6);
                vb.Sphere(c + along * 0.5f, 0.045f, "petalwhite", rings: 3, segments: 6);
            }
            vb.Cylinder(p + Vector3.up * 1.74f, 0.14f, 0.08f, "petalred", 12);
            vb.Box(p + new Vector3(0.6f, 0.03f, -0.3f), new Vector3(0.09f, 0.04f, 0.04f), "ctgreen", facing + 40f);
            vb.RegisterProp("capsule_machine", p + Vector3.up * 0.85f, new Vector3(0.7f, 1.7f, 0.7f), facing);
        }

        private static void HeliusLamp(VB vb)
        {
            // Glowing orange orb lamp on an iron bracket over the parcels door, a stack of unlabelled parcels below.
            var fr = new Fr(new Vector3(EastX, 0, -0.25f), -90f);
            var bracket = fr.P(1.4f, 3.3f, 0.0f);
            vb.Pipe(bracket, bracket + fr.f * 0.9f, 0.035f, "iron", 6);
            vb.Pipe(bracket - Vector3.up * 0.5f, bracket + fr.f * 0.5f, 0.025f, "iron", 5);
            var orb = bracket + fr.f * 0.85f - Vector3.up * 0.45f;
            vb.Pipe(bracket + fr.f * 0.85f, orb + Vector3.up * 0.3f, 0.012f, "iron", 4);
            vb.Sphere(orb, 0.3f, "heliusorange", glow: true, rings: 8, segments: 12);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var d = (fr.r * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a));
                vb.Pipe(orb + d * 0.36f, orb + d * 0.48f, 0.02f, "heliusorange", 4, glow: true);
            }
            vb.PointLight(orb + fr.f * 0.3f, "ctorange", 0.9f, 4f);
            var base0 = fr.P(-0.2f, 0f, 0.55f);
            float[] sizes = { 0.55f, 0.45f, 0.5f, 0.35f, 0.42f };
            for (int i = 0; i < 5; i++)
            {
                float s = sizes[i];
                var pc = base0 + fr.r * ((i % 3) * 0.5f - 0.3f) + Vector3.up * (s * .5f + (i >= 3 ? 0.5f : 0f));
                vb.Box(pc, new Vector3(s, s * 0.9f, s * 0.8f), "cardboard", -90f + i * 7f);
                vb.Box(pc + Vector3.up * s * 0.45f, new Vector3(s * 0.15f, 0.01f, s * 0.82f), "sand", -90f + i * 7f);
            }
            vb.RegisterProp("parcels", base0 + fr.r * 0.2f + Vector3.up * 0.5f, new Vector3(1.6f, 1.0f, 0.7f), -90f);
        }

        private static void MadLadsPoster(VB vb)
        {
            var fr = new Fr(new Vector3(EastX, 0, -0.25f), -90f);
            vb.Sign("tl_burst", fr.P(2.55f, 1.25f, 0.09f), 0.8f, -90f, 1.0f, frame: "iron", lit: true);
        }

        private static void ClayDino(VB vb)
        {
            // Hand-modelled clay dino toy in the toyshop window: lopsided grin, thumbprint dents, a toy house beside it.
            var fr = new Fr(new Vector3(EastX, 0, 6.5f), -90f);
            var c = fr.P(1.6f, 0.6f, 0.32f);
            vb.Blob(c + Vector3.up * 0.18f, new Vector3(0.42f, 0.3f, 0.26f), "clay", yaw: -90f + 15f, rings: 6, segments: 10);
            var neck = c + Vector3.up * 0.32f + fr.r * 0.14f;
            vb.Blob(neck + Vector3.up * 0.1f, new Vector3(0.12f, 0.3f, 0.12f), "clay", rings: 5, segments: 8);
            var head = neck + Vector3.up * 0.3f + fr.r * 0.05f;
            vb.Blob(head, new Vector3(0.22f, 0.16f, 0.15f), "clay", yaw: -80f, rings: 6, segments: 10);
            vb.Box(head + fr.f * 0.075f - Vector3.up * 0.03f, new Vector3(0.12f, 0.012f, 0.01f), "darkgrass", -90f + 8f);
            foreach (float s in new[] { -1f, 1f }) vb.Sphere(head + fr.f * 0.07f + fr.r * (s * 0.04f + 0.03f) + Vector3.up * 0.03f, 0.016f, "black", rings: 3, segments: 5);
            vb.Blob(c + Vector3.up * 0.1f - fr.r * 0.28f, new Vector3(0.32f, 0.1f, 0.1f), "clay", yaw: -90f, rings: 4, segments: 7);
            foreach (float s in new[] { -1f, 1f })
                vb.Blob(c + fr.r * (s * 0.12f) + fr.f * 0.08f, new Vector3(0.08f, 0.14f, 0.08f), "clay");
            for (int i = 0; i < 3; i++) vb.Sphere(c + Vector3.up * (0.3f) + fr.r * (-0.12f + i * 0.09f) + fr.f * 0.1f, 0.025f, "darkgrass", rings: 3, segments: 5);
            for (int i = 0; i < 4; i++) vb.CylinderRot(c + Vector3.up * 0.33f + fr.r * (-0.15f + i * 0.08f), Quaternion.identity, 0.03f, 0.06f, "grass", 4, topRadius: 0f);
            var house = fr.P(0.6f, 0.75f, 0.32f);
            vb.Box(house + Vector3.up * 0.13f, new Vector3(0.3f, 0.26f, 0.26f), "petalyellow", -90f);
            vb.CylinderRot(house + Vector3.up * 0.26f, Quaternion.Euler(0, 45f, 0), 0.24f, 0.16f, "petalred", 4, topRadius: 0f);
        }

        private static void Bunting(VB vb, Vector3 a, Vector3 b, int count, int seed)
        {
            // Superteam-style bunting: purple and yellow pennants mixed with country flags, one pennant per chapter.
            Vector3 Cord(float t) => Vector3.Lerp(a, b, t) + Vector3.down * (0.9f * (1f - (2f * t - 1f) * (2f * t - 1f)));
            for (int i = 0; i < 20; i++) vb.Pipe(Cord(i / 20f), Cord((i + 1) / 20f), 0.01f, "offwhite", 4);
            var side = Vector3.Cross(Vector3.up, (b - a).normalized);
            float facing = Mathf.Atan2(side.x, side.z) * Mathf.Rad2Deg;
            if (Vector3.Dot(side, new Vector3(0, 0, -1)) < 0) facing += 180f;
            for (int i = 0; i < count; i++)
            {
                var p = Cord((i + 0.5f) / count);
                var along = (Cord((i + 0.8f) / count) - Cord((i + 0.2f) / count)).normalized;
                if (i % 3 == 1)
                {
                    vb.Sign("tl_flag_" + ((i * 5 + seed * 3) % 16), p - Vector3.up * 0.36f, 0.5f, facing, 0.34f, lit: true);
                }
                else
                {
                    string col = (i + seed) % 2 == 0 ? "solpurple" : "ctgold";
                    vb.Triangle(p - along * 0.22f, p + along * 0.22f, p - Vector3.up * 0.5f, col);
                }
            }
        }

        private static void ColosseumHall(VB vb, Vector3 c)
        {
            // Background: an arched round hall (two tiers of arches) with a burgundy banner. No letters.
            vb.Backdrop = true;
            const float r = 6.5f;
            vb.Cylinder(c - Vector3.up * 2.4f, r + 1.5f, 2.4f, "stuccocream", 24);
            vb.Arc(c, r - 0.8f, r, 0f, 360f, 0f, 9.5f, "stuccocream", 40);
            vb.Arc(c, r - 0.05f, r + 0.15f, 0f, 360f, 4.6f, 4.9f, "limestone", 40);
            vb.Arc(c, r - 0.05f, r + 0.15f, 0f, 360f, 9.3f, 9.7f, "limestone", 40);
            for (int i = 0; i < 18; i++)
            {
                float a = i * 20f;
                if (a < 110f || a > 250f) continue;
                float ar = a * Mathf.Deg2Rad;
                var n = new Vector3(Mathf.Sin(ar), 0, Mathf.Cos(ar));
                for (int t = 0; t < 2; t++)
                {
                    var bp = c + n * (r + 0.02f) + Vector3.up * (0.6f + t * 4.6f);
                    vb.Box(bp + Vector3.up * 1.2f, new Vector3(1.1f, 2.4f, 0.05f), "ink", a);
                    vb.CylinderRot(bp + Vector3.up * 2.4f, Quaternion.Euler(0, a, 0) * Quaternion.Euler(90f, 0, 0), 0.55f, 0.02f, "ink", 10);
                }
            }
            var banner = c + new Vector3(Mathf.Sin(200f * Mathf.Deg2Rad), 0, Mathf.Cos(200f * Mathf.Deg2Rad)) * (r + 0.12f);
            vb.Box(banner + Vector3.up * 7.2f, new Vector3(1.5f, 3.2f, 0.06f), "velvetred", 200f);
            vb.Triangle(banner + Vector3.up * 5.6f + Quaternion.Euler(0, 200f, 0) * new Vector3(-0.75f, 0, -0.04f),
                banner + Vector3.up * 5.6f + Quaternion.Euler(0, 200f, 0) * new Vector3(0.75f, 0, -0.04f), banner + Vector3.up * 5.0f, "velvetred");
            vb.Box(banner + Vector3.up * 8.85f, new Vector3(1.7f, 0.08f, 0.1f), "brass", 200f);
            vb.Backdrop = false;
        }

        // ------------------------------------------------------------------ dressing: trees, troughs, pots, crates, bicycle

        private static void Dressing(VB vb)
        {
            foreach (var p in new[] { new Vector3(-12.8f, 0, 3.6f), new Vector3(12.8f, 0, 3.6f), new Vector3(-7.6f, 0, 12.4f), new Vector3(7.6f, 0, 12.4f),
                                      new Vector3(-12.0f, 0, -9.4f), new Vector3(12.6f, 0, -12.2f) })
                CitrusTree(vb, p, (int)(p.x * 3 + p.z));
            // Flower troughs and pots along the facades (away from the doors and the live surfaces).
            foreach (var (pos, yaw, len) in new[]
                     {
                         (new Vector3(-13.7f, 0, 14.8f), 180f, 1.6f), (new Vector3(13.4f, 0, 14.8f), 180f, 2.0f), (new Vector3(19.7f, 0, 9.6f), -90f, 1.6f),
                         (new Vector3(19.7f, 0, -11.6f), -90f, 1.4f),
                     })
            {
                vb.Box(pos + Vector3.up * 0.25f, new Vector3(len, 0.5f, 0.55f), "limestone", yaw);
                vb.RegisterProp("trough", pos + Vector3.up * 0.25f, new Vector3(len, 0.5f, 0.55f), yaw);
                Flowers(vb, pos + Vector3.up * 0.5f, len - 0.2f, 0.4f, yaw, (int)(pos.x + pos.z * 3), tall: true);
            }
            // Barrels and crates by the market building, a bicycle at the toyshop, pots at doors.
            foreach (var (x, z) in new[] { (19.5f, -3.3f), (19.1f, -3.95f) })
            {
                vb.Cylinder(new Vector3(x, 0, z), 0.3f, 0.8f, "walnut", 12);
                vb.Arc(new Vector3(x, 0, z), 0.29f, 0.31f, 0f, 360f, 0.15f, 0.2f, "iron", 16);
                vb.Arc(new Vector3(x, 0, z), 0.29f, 0.31f, 0f, 360f, 0.6f, 0.65f, "iron", 16);
                vb.RegisterProp("barrel", new Vector3(x, 0.4f, z), new Vector3(0.62f, 0.8f, 0.62f));
            }
            foreach (var (x, z, h) in new[] { (19.3f, -8.3f, 0f), (19.3f, -8.3f, 0.42f), (18.7f, -7.9f, 0f) })
            {
                vb.Box(new Vector3(x, h + 0.2f, z), new Vector3(0.6f, 0.4f, 0.45f), "plywood", 8f);
                for (int k = 0; k < 4; k++) vb.Sphere(new Vector3(x - 0.15f + (k % 2) * 0.3f, h + 0.42f, z - 0.1f + (k / 2) * 0.2f), 0.08f, k % 2 == 0 ? "petalred" : "grass", rings: 3, segments: 6);
            }
            vb.RegisterProp("crates", new Vector3(19.0f, 0.42f, -8.1f), new Vector3(1.2f, 0.84f, 0.9f));
            Bicycle(vb, new Vector3(19.85f, 0, 0.6f + 6.25f), 0f);
            foreach (var (x, z) in new[] { (-19.6f, 13.6f), (19.6f, 13.3f), (-15.3f, 14.9f) })
            {
                vb.Cylinder(new Vector3(x, 0, z), 0.26f, 0.5f, "terracotta", 12, topRadius: 0.32f);
                vb.RegisterProp("pot", new Vector3(x, 0.25f, z), new Vector3(0.64f, 0.5f, 0.64f));
                Flowers(vb, new Vector3(x, 0.5f, z), 0.4f, 0.4f, 0f, (int)(x * z), tall: true);
            }
        }

        /// <summary>Small citrus tree in a round stone planter: trunk, layered canopy, a scatter of oranges.</summary>
        private static void CitrusTree(VB vb, Vector3 p, int seed)
        {
            var rnd = new System.Random(seed);
            vb.Cylinder(p, 0.75f, 0.55f, "limestone", 18);
            vb.Arc(p, 0.62f, 0.8f, 0f, 360f, 0.55f, 0.61f, "stuccocream", 24);
            vb.Cylinder(p + Vector3.up * 0.5f, 0.62f, 0.06f, "soil", 18);
            vb.Pipe(p + Vector3.up * 0.55f, p + Vector3.up * 1.9f + new Vector3(0.1f, 0, 0.05f), 0.08f, "walnut", 7);
            var crown = p + Vector3.up * 2.45f;
            for (int i = 0; i < 6; i++)
            {
                var o = new Vector3((float)(rnd.NextDouble() - 0.5) * 1.1f, (float)(rnd.NextDouble() - 0.4) * 0.7f, (float)(rnd.NextDouble() - 0.5) * 1.1f);
                vb.Blob(crown + o, new Vector3(1.2f, 0.95f, 1.15f), i % 2 == 0 ? "ivy" : "leafdark", yaw: i * 40f, rings: 5, segments: 8);
            }
            for (int i = 0; i < 14; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, y = (float)rnd.NextDouble() * 0.9f - 0.35f;
                vb.Sphere(crown + new Vector3(Mathf.Cos(a) * 0.95f, y, Mathf.Sin(a) * 0.95f), 0.07f, "ctorange", rings: 3, segments: 6);
            }
            vb.BlockPrism("Planter_" + seed, p, 0.8f, 0.6f, 12);
            vb.RegisterProp("tree_planter", p + Vector3.up * 0.3f, new Vector3(1.5f, 0.6f, 1.5f));
        }

        private static void Bicycle(VB vb, Vector3 p, float yaw)
        {
            var q = Quaternion.Euler(0, yaw, 0);
            foreach (float z in new[] { -0.52f, 0.52f })
                for (int i = 0; i < 12; i++)
                {
                    float a0 = i * Mathf.PI / 6f, a1 = (i + 1) * Mathf.PI / 6f;
                    vb.Pipe(p + q * new Vector3(0, 0.34f + Mathf.Sin(a0) * 0.32f, z + Mathf.Cos(a0) * 0.32f), p + q * new Vector3(0, 0.34f + Mathf.Sin(a1) * 0.32f, z + Mathf.Cos(a1) * 0.32f), 0.018f, "iron", 4);
                }
            vb.Pipe(p + q * new Vector3(0, 0.34f, -0.52f), p + q * new Vector3(0, 0.75f, -0.05f), 0.025f, "awninggreen", 5);
            vb.Pipe(p + q * new Vector3(0, 0.34f, 0.52f), p + q * new Vector3(0, 0.75f, -0.05f), 0.025f, "awninggreen", 5);
            vb.Pipe(p + q * new Vector3(0, 0.75f, -0.05f), p + q * new Vector3(0, 0.95f, 0.42f), 0.025f, "awninggreen", 5);
            vb.Pipe(p + q * new Vector3(0, 0.34f, 0.52f), p + q * new Vector3(0, 0.95f, 0.42f), 0.022f, "awninggreen", 5);
            vb.Box(p + q * new Vector3(0, 0.9f, -0.15f), new Vector3(0.12f, 0.05f, 0.24f), "leather", yaw);
            vb.Pipe(p + q * new Vector3(-0.24f, 1.02f, 0.45f), p + q * new Vector3(0.24f, 1.02f, 0.45f), 0.02f, "iron", 4);
            vb.Box(p + q * new Vector3(0, 0.95f, 0.66f), new Vector3(0.32f, 0.2f, 0.24f), "plywood", yaw);
            Flowers(vb, p + q * new Vector3(0, 1.05f, 0.66f), 0.25f, 0.2f, yaw, 77, tall: false);
            vb.RegisterProp("bicycle", p + Vector3.up * 0.5f, new Vector3(0.3f, 1.0f, 1.7f), yaw);
        }

        // ------------------------------------------------------------------ background

        private static void Backdrop(VB vb)
        {
            vb.Backdrop = true;
            // Cypresses and round trees on the hill behind the north row.
            var rnd = new System.Random(5);
            for (int i = 0; i < 14; i++)
            {
                float x = -30f + i * 4.6f + (float)rnd.NextDouble() * 1.5f;
                float z = 24f + (float)rnd.NextDouble() * 10f;
                if (Mathf.Abs(x) < 7f && z < 30f) continue;
                float h = 5f + (float)rnd.NextDouble() * 4f;
                vb.Blob(new Vector3(x, 1.5f + h * .5f, z), new Vector3(1.3f, h, 1.3f), "cypress", rings: 6, segments: 8);
                if (i % 3 == 0) vb.Blob(new Vector3(x + 2.2f, 3.2f, z + 1.5f), new Vector3(3.2f, 2.8f, 3.0f), "leafdark", rings: 5, segments: 8);
            }
            foreach (var (x, z) in new[] { (-24f, -4f), (-25f, 6f), (24.5f, 8f), (25f, -6f) })
                vb.Blob(new Vector3(x, 5.5f, z), new Vector3(1.2f, 7f, 1.2f), "cypress", rings: 6, segments: 8);
            // Hill town rooftops north (simple blocks with roofs).
            for (int i = 0; i < 10; i++)
            {
                float x = -34f + i * 7.5f;
                var fr = new Fr(new Vector3(x, 3.5f + (i % 3), 40f + (i % 2) * 6f), 180f);
                House(vb, fr, 6.5f, 5f + (i % 2) * 2f, 6f, i % 3 == 0 ? "tl_stucco_ochre" : i % 3 == 1 ? "tl_stucco_cream" : "tl_stucco_peach", "stuccocream", null, 400 + i, far: true);
            }
            vb.Box(new Vector3(0, 1.5f, 42f), new Vector3(90f, 3f, 22f), "olive");
            vb.Box(new Vector3(0, -0.06f, 8f), new Vector3(120f, 0.1f, 90f), "stuccostone");
            // Painted golden-hour sky (Solana gradient wash), sea, far hills and hill town.
            vb.Sign("tl_sky", new Vector3(0f, -6f, 92f), 260f, 180f, 130f);
            vb.Sign("tl_sky", new Vector3(-92f, -6f, 10f), 260f, 90f, 130f);
            vb.Sign("tl_sky", new Vector3(92f, -6f, 10f), 260f, -90f, 130f);
            vb.Backdrop = false;
        }
    }
}
