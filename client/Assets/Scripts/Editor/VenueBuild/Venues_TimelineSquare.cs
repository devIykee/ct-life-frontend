using CTLife.Core;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Timeline Square v3 (round 4), composed to the in-game concept (art-direction/out/ingame-timeline-concept-v1.png) from
    /// the default gameplay camera at the centre spot and at the entrance: a compact, dense Mediterranean square at golden hour.
    ///  * The plaza is 23 × 22 m (v2 was 41 × 31): building fronts close it on three sides and fill the upper frame.
    ///  * Centre: a three-tier fountain on a round deck with flower troughs and pots, a ring of planters, six benches facing it.
    ///  * West (left in frame): the blue café with a striped awning and an umbrella terrace, the bakery (red awning), the
    ///    vine-covered corner house with the Pixel Wall (live surface).
    ///  * North: two rows carrying the billboards (live surfaces), a street with steps up to the town hall and its Timeline
    ///    screen (live surface), the clock tower behind. East (right): toyshop (green awning), parcels shop, market building
    ///    with the stall and fruit crates. South (camera side): a stone wall with iron railings and a gate.
    ///  * String lights and two Superteam-style buntings across the square, wall lanterns, lamp posts, citrus trees.
    ///  * Background: the Colosseum-style hall, a hill town, cypresses, a golden-hour sky (Solana gradient wash, no logo).
    ///  * The 15 Tier A community eggs (docs/research/community-eggs-2026-10.md), our own drawings without logos or letters,
    ///    about 1.5–2× their v2 size so they read at gameplay distance.
    /// Spot ids are the contract's; spots.json fe.5 moves the criers, the Pixel Wall and billboard viewpoints, the entrance and
    /// the exit into the compact square and shrinks the walkable rectangles to it (bench positions and facing unchanged).
    /// </summary>
    public static class VenuesTimelineSquare
    {
        private static readonly Vector3 F = Vector3.zero;
        private const float DeckR = 4.4f, DeckH = 0.16f, BasinR = 2.8f;
        private const float WestX = -11.5f, EastX = 11.5f, NorthZ = 9.8f, SouthZ = -12.4f, StreetX = 3.5f;

        // Facade frames (origin on the facade plane at ground level, centre of the front).
        private static readonly Fr CafeFr = new Fr(new Vector3(WestX, 0, -8.2f), 90f);      // z -12.4..-4.0
        private static readonly Fr BakeryFr = new Fr(new Vector3(WestX, 0, -0.7f), 90f);    // z -4.0..2.6
        private static readonly Fr CornerFr = new Fr(new Vector3(WestX, 0, 6.2f), 90f);     // z 2.6..9.8
        private static readonly Fr N1Fr = new Fr(new Vector3(-7.5f, 0, NorthZ), 180f);      // x -11.5..-3.5
        private static readonly Fr N2Fr = new Fr(new Vector3(7.5f, 0, NorthZ), 180f);       // x 3.5..11.5
        private static readonly Fr ToyFr = new Fr(new Vector3(EastX, 0, 6.5f), -90f);       // z 3.2..9.8
        private static readonly Fr ParcelFr = new Fr(new Vector3(EastX, 0, 0.1f), -90f);    // z -3.0..3.2
        private static readonly Fr MarketFr = new Fr(new Vector3(EastX, 0, -7.7f), -90f);   // z -12.4..-3.0

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
            Paving(vb);
            Fountain(vb);
            RingPlanters(vb);
            Benches(vb);
            Lamps(vb);
            WestRow(vb);
            NorthRow(vb);
            EastRow(vb);
            SouthEdge(vb);
            Eggs(vb);
            Dressing(vb);
            Lushness(vb);
            StringLights(vb);
            Backdrop(vb);
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 0.6f, 1.4f);
        }

        // ------------------------------------------------------------------ ground

        private static void Cobbles(VB vb)
        {
            // Painted cobble tiles (lit, lightmapped). One seamless tile, unrotated, so joints never show.
            const float t = 2.5f;
            for (int ix = -6; ix < 6; ix++)
                for (int iz = -7; iz < 6; iz++)
                    vb.FloorSign("tl_cobble_a", ix * t + t * .5f, iz * t + t * .5f, t, 0f, t, -0.004f, lit: true);
        }

        /// <summary>
        /// Lighter stone paving set into the cobbles (flush): rings and radial joints round the fountain and a bordered path
        /// from the gate, so the open ground reads as a designed square; a few pigeons near the path.
        /// </summary>
        private static void Paving(VB vb)
        {
            vb.RingDecal(F, 4.75f, 0.14f, "stoneshade", 64);
            vb.RingDecal(F, 6.05f, 0.34f, "limestone", 72);
            vb.RingDecal(F, 7.7f, 0.14f, "stoneshade", 80);
            for (int i = 0; i < 32; i++)
            {
                float a = (i + 0.5f) * 360f / 32f;
                float ar = a * Mathf.Deg2Rad;
                var mid = new Vector3(Mathf.Sin(ar) * 6.2f, 0f, Mathf.Cos(ar) * 6.2f);
                vb.Decal(mid.x, mid.z, 0.08f, 2.9f, "limestone", a, 0.004f);
            }
            foreach (float sx in new[] { -1f, 1f })
                vb.Decal(sx * 1.7f, -10.25f, 0.16f, 4.3f, "stoneshade", 0f, 0.004f);
            for (float z = -12.2f; z < -8.0f; z += 1.1f)
                vb.Decal(0f, z, 3.24f, 0.1f, "stoneshade", 0f, 0.004f);
            // Pigeons pecking by the path.
            var rnd = new System.Random(17);
            foreach (var (x, z) in new[] { (1.9f, -4.9f), (2.4f, -5.3f), (2.0f, -5.7f), (2.8f, -4.7f), (-2.3f, -9.0f), (-2.7f, -9.5f) })
            {
                float yaw = (float)rnd.NextDouble() * 360f;
                var q = Quaternion.Euler(0, yaw, 0);
                var p = new Vector3(x, 0f, z);
                vb.Blob(p + Vector3.up * 0.1f, new Vector3(0.13f, 0.12f, 0.24f), "grey", yaw: yaw, rings: 4, segments: 7);
                vb.Sphere(p + q * new Vector3(0, 0.19f, 0.1f), 0.055f, "darkgrey", rings: 3, segments: 6);
                vb.Blob(p + q * new Vector3(0, 0.08f, -0.14f), new Vector3(0.08f, 0.03f, 0.1f), "darkgrey", yaw: yaw);
                vb.Box(p + q * new Vector3(0, 0.18f, 0.16f), new Vector3(0.02f, 0.02f, 0.05f), "ctorange", yaw);
            }
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
                // Round 5 lushness: denser blooms (10 per clump) with a few trailing over the rim.
                for (int j = 0; j < 10; j++)
                {
                    var o = new Vector3((float)(rnd.NextDouble() - 0.5) * 0.5f, 0.14f + (float)rnd.NextDouble() * (tall ? 0.34f : 0.18f), (float)(rnd.NextDouble() - 0.5) * 0.36f);
                    if (j == 9) o = new Vector3(o.x, -0.06f, (rnd.NextDouble() < 0.5 ? -1f : 1f) * depth * 0.55f);
                    vb.Sphere(p + q * o, 0.07f, j % 3 == 2 ? petals[(k + j + seed) % petals.Length] : bloom, rings: 2, segments: 4);
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

        /// <summary>Round stone planters with flowers on the ring between the benches (south kept open for the entrance view).</summary>
        private static void RingPlanters(VB vb)
        {
            foreach (float ang in new[] { 0f, 60f, 120f, 240f, 300f })
            {
                float a = ang * Mathf.Deg2Rad;
                float r = ang == 120f || ang == 240f ? 6.2f : 6.4f;
                var p = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                vb.Cylinder(p, 0.62f, 0.5f, "limestone", 18);
                vb.Arc(p, 0.52f, 0.68f, 0f, 360f, 0.5f, 0.56f, "stuccocream", 24);
                vb.RegisterProp("ring_planter", p + Vector3.up * 0.25f, new Vector3(1.3f, 0.5f, 1.3f));
                Flowers(vb, p + Vector3.up * 0.5f, 0.9f, 0.7f, ang, (int)ang + 3, tall: true);
                Flowers(vb, p + Vector3.up * 0.5f, 0.7f, 0.5f, ang + 90f, (int)ang + 9, tall: true);
            }
            // Pots on the deck rim between the troughs.
            for (int i = 0; i < 6; i++)
            {
                float a = (i * 60f + 30f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(a) * 4.0f, DeckH, Mathf.Cos(a) * 4.0f);
                vb.Cylinder(p, 0.2f, 0.36f, "terracotta", 12, topRadius: 0.25f);
                Flowers(vb, p + Vector3.up * 0.36f, 0.3f, 0.3f, i * 60f, 40 + i, tall: true);
            }
        }

        private static void Lamps(VB vb)
        {
            foreach (var p in new[] { new Vector3(6.9f, 0, -2.6f), new Vector3(-3.0f, 0, 9.0f), new Vector3(3.0f, 0, 9.0f) })
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
            // Two hanging flower baskets on short arms (round 5).
            foreach (float sx in new[] { -1f, 1f })
            {
                var arm = p + Vector3.up * 2.55f;
                vb.Pipe(arm, arm + new Vector3(sx * 0.42f, 0.06f, 0f), 0.018f, "iron", 4);
                HangingBasket(vb, arm + new Vector3(sx * 0.42f, 0.06f, 0f), 0.6f, (int)(p.x * 7 + p.z * 3 + sx * 5));
            }
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

        // ------------------------------------------------------------------ west row (facing east, left in frame)

        private static void WestRow(VB vb)
        {
            // W1: the blue café (terrace and eggs in CafeTerrace / Eggs).
            var cafe = CafeFr;
            House(vb, cafe, 8.4f, 7.2f, 7f, "tl_stucco_blue", "stuccoblue", "shutterblue", 13);
            ShopWindow(vb, cafe, 2.4f, 2.6f, "tl_win_cafe");
            ShopWindow(vb, cafe, -2.4f, 2.6f, "tl_win_cafe");
            Door(vb, cafe, 0f, "awningblue");
            Awning(vb, cafe, 0f, 8.0f, 3.15f, 2.0f, "awningblue", "petalwhite");
            Vine(vb, cafe, -4.0f, 3.25f, 2.6f, 5);
            WallLantern(vb, cafe, 3.9f, 2.9f);
            // Stone ledge under the middle first-floor window (the astronaut cat naps on it).
            vb.Box(cafe.P(0f, 3.86f, 0.28f), new Vector3(1.7f, 0.1f, 0.56f), "limestone", cafe.yaw);
            SideFace(vb, new Fr(new Vector3(WestX - 3.5f, 0, SouthZ), 180f), 7f, 7.2f, "tl_stucco_blue", "shutterblue", 14);

            // W2: bakery, red awning (the O-mouth cat is in its window: Eggs).
            var bakery = BakeryFr;
            House(vb, bakery, 6.6f, 7.6f, 7f, "tl_stucco_peach", "stuccopeach", "shuttergreen", 31, balcony: true);
            ShopWindow(vb, bakery, 0.9f, 3.4f, "tl_win_bakery");
            Door(vb, bakery, -2.2f, "barnred");
            Awning(vb, bakery, -0.3f, 6.0f, 3.15f, 1.5f, "awningred", "petalwhite");
            WallLantern(vb, bakery, -3.05f, 2.9f);
            var board = bakery.P(-2.2f, 0f, 1.25f);
            vb.BoxRot(board + new Vector3(-0.12f, 0.5f, 0f), new Vector3(0.04f, 1.0f, 0.6f), Quaternion.Euler(0, 0, -12f), "darkwood");
            vb.BoxRot(board + new Vector3(0.12f, 0.5f, 0f), new Vector3(0.04f, 1.0f, 0.6f), Quaternion.Euler(0, 0, 12f), "darkwood");
            vb.RegisterProp("sandwich_board", board + Vector3.up * 0.5f, new Vector3(0.4f, 1.0f, 0.65f));
            foreach (var (u, seed) in new[] { (-3.0f, 3), (2.95f, 7) })
                Pot(vb, bakery.P(u, 0f, 0.45f), 0.3f, seed);

            // W3: vine-covered corner house with the Pixel Wall (community pixel canvas, live surface) on its ground floor.
            var corner = CornerFr;
            House(vb, corner, 7.2f, 9.6f, 7f, "tl_stucco_ochre", "stuccoochre", "shutterblue", 41, floors: 3);
            vb.LiveScreen("pixel_wall", "tl_pixel_idle", corner.P(0f, 0.55f, 0.06f), 3.8f, 90f, frame: "darkwood");
            vb.Box(corner.P(0f, 3.5f, 0.16f), new Vector3(4.4f, 0.08f, 0.26f), "iron", corner.yaw);
            for (int i = 0; i < 3; i++)
            {
                vb.Pipe(corner.P(-1.4f + i * 1.4f, 3.5f, 0.26f), corner.P(-1.4f + i * 1.4f, 3.3f, 0.5f), 0.015f, "iron", 4);
                vb.Cylinder(corner.P(-1.4f + i * 1.4f, 3.2f, 0.5f), 0.09f, 0.1f, "glowamber", 8, glow: true, topRadius: 0.05f);
            }
            vb.PointLight(corner.P(0f, 3.0f, 1.0f), "glowamber", 0.5f, 4f);
            Vine(vb, corner, 3.4f, 6.2f, -2.4f, 8);
            Vine(vb, corner, -3.3f, 3.3f, 1.2f, 9);
            // Hill houses behind the west row (roofs above it on the left of the frame).
            for (int i = 0; i < 3; i++)
            {
                var back = new Fr(new Vector3(-21.5f - (i % 2) * 2.5f, 2.6f + (i % 2) * 1.2f, -6f + i * 7.5f), 90f);
                House(vb, back, 6.5f, 6.5f + (i % 2), 6f, i % 2 == 0 ? "tl_stucco_cream" : "tl_stucco_peach", "stuccocream", null, 200 + i, far: true);
            }
        }

        // ------------------------------------------------------------------ north row (facing south) and the town hall

        private static void NorthRow(VB vb)
        {
            // The default gameplay camera sees the north fronts up to about 3 m (frame top), so the live surfaces sit on the
            // ground floors: N1 carries the two billboards (ad slots billboard_01/02), N2 the Timeline screen.
            // N1 (x -11.5..-3.5): two framed billboards with a lantern between them (its door is on the street side).
            var n1 = N1Fr;
            House(vb, n1, 8f, 8.8f, 5.5f, "tl_stucco_cream", "stuccocream", "shuttergreen", 51);
            vb.LiveScreen("bb_02", "tl_bb_2_idle", n1.P(1.95f, 0.95f, 0.08f), 2.7f, 180f, frame: "iron");
            vb.LiveScreen("bb_01", "tl_bb_1_idle", n1.P(-1.75f, 0.95f, 0.08f), 2.7f, 180f, frame: "iron");
            foreach (float u in new[] { 1.95f, -1.75f })
                vb.Box(n1.P(u, 2.47f, 0.2f), new Vector3(2.9f, 0.06f, 0.28f), "iron", 180f);
            WallLantern(vb, n1, 0.1f, 2.6f);
            Vine(vb, n1, 3.8f, 3.3f, -1.6f, 52);
            // N2 (x 3.5..11.5): the Timeline screen (live surface) in a stone surround on the ground floor.
            var n2 = N2Fr;
            House(vb, n2, 8f, 8.8f, 5.5f, "tl_stucco_ochre", "stuccoochre", "shutterblue", 81);
            vb.LiveScreen("timeline_screen", "tl_timeline_idle", n2.P(-0.45f, 0.55f, 0.1f), 4.6f, 180f, frame: "iron");
            vb.Box(n2.P(-0.45f, 3.27f, 0.16f), new Vector3(5.1f, 0.22f, 0.32f), "limestone", 180f);
            vb.Box(n2.P(-0.45f, 0.42f, 0.18f), new Vector3(5.0f, 0.12f, 0.36f), "limestone", 180f);
            vb.PointLight(n2.P(-0.45f, 2.4f, 1.6f), "glowamber", 0.5f, 5f);
            Vine(vb, n2, -3.8f, 3.3f, 1.4f, 82);

            // The street's side walls (N1 east end, N2 west end): painted stucco, windows, a vine, lanterns on brackets.
            foreach (float sx in new[] { -1f, 1f })
            {
                var side = new Fr(new Vector3(sx * StreetX, 0, NorthZ + 2.75f), sx < 0 ? 90f : -90f);
                vb.Sign(sx < 0 ? "tl_stucco_cream" : "tl_stucco_ochre", side.P(0, 0, 0.012f), 5.5f, side.yaw, 8.8f, lit: true);
                vb.Box(side.P(0, 0.32f, 0.04f), new Vector3(5.52f, 0.64f, 0.08f), "stoneshade", side.yaw);
                Window(vb, side, 0.6f, 3.95f, sx > 0, sx < 0 ? "shuttergreen" : "shutterblue", true, 500 + (int)sx);
                Window(vb, side, 0.6f, 6.85f, sx < 0, sx < 0 ? "shuttergreen" : "shutterblue", true, 510 + (int)sx);
                Vine(vb, side, -2.4f, 3.2f, 1.8f, 520 + (int)sx);
                WallLantern(vb, side, 2.25f, 2.9f);
                Door(vb, side, 1.15f, sx < 0 ? "walnut" : "shuttergreen");
            }

            // Steps up to the town hall terrace, the hall, its pediment and the Timeline screen (live surface).
            for (int k = 0; k < 5; k++)
                vb.Box(new Vector3(0, (k + 1) * 0.1f, 12.18f + k * 0.36f), new Vector3(StreetX * 2f, (k + 1) * 0.2f, 0.36f), k % 2 == 0 ? "limestone" : "stuccocream");
            vb.Box(new Vector3(0, 0.5f, 14.6f), new Vector3(StreetX * 2f, 1.0f, 1.6f), "limestone");
            foreach (float sx in new[] { -1f, 1f })
            {
                Pot(vb, new Vector3(sx * 2.9f, 1.0f, 14.2f), 0.32f, 60 + (int)sx);
                Pot(vb, new Vector3(sx * 3.0f, 0f, 11.6f), 0.3f, 64 + (int)sx);
            }
            var hall = new Fr(new Vector3(0, 1.0f, 15.4f), 180f);
            House(vb, hall, 9f, 9.2f, 6f, "tl_stucco_stone", "stuccostone", null, 71, roof: false, floors: 1);
            vb.Box(hall.P(0, 9.6f, -0.3f), new Vector3(5.2f, 1.6f, 0.6f), "stuccostone", 180f);
            vb.Triangle(hall.P(-2.8f, 10.4f, 0.02f), hall.P(2.8f, 10.4f, 0.02f), hall.P(0, 11.9f, 0.02f), "limestone");
            vb.Triangle(hall.P(-2.8f, 10.4f, -0.6f), hall.P(2.8f, 10.4f, -0.6f), hall.P(0, 11.9f, -0.6f), "limestone");
            vb.Box(hall.P(0, 9.2f, 0.12f), new Vector3(9.3f, 0.3f, 0.3f), "limestone", 180f);
            vb.Box(hall.P(0, 1.35f, 0.04f), new Vector3(1.8f, 2.7f, 0.06f), "darkwood", 180f);
            vb.CylinderRot(hall.P(0f, 2.7f, 0.04f), Quaternion.Euler(0, 180f, 0) * Quaternion.Euler(90f, 0, 0), 0.9f, 0.02f, "darkwood", 14);
            foreach (float sx in new[] { -1f, 1f })
            {
                vb.Box(hall.P(sx * 1.15f, 1.5f, 0.12f), new Vector3(0.36f, 3.0f, 0.24f), "limestone", 180f);
                vb.Box(hall.P(sx * 3.9f, 4.5f, 0.15f), new Vector3(0.5f, 9.0f, 0.3f), "limestone", 180f);
            }
            foreach (float sx in new[] { -1f, 1f })
            {
                vb.Box(hall.P(sx * 2.6f, 5.4f, 0.12f), new Vector3(1.1f, 3.0f, 0.05f), "velvetred", 180f);
                vb.Box(hall.P(sx * 2.6f, 6.95f, 0.16f), new Vector3(1.3f, 0.07f, 0.08f), "brass", 180f);
                vb.Triangle(hall.P(sx * 2.6f - 0.55f, 3.9f, 0.12f), hall.P(sx * 2.6f + 0.55f, 3.9f, 0.12f), hall.P(sx * 2.6f, 3.45f, 0.12f), "velvetred");
            }
            vb.PointLight(hall.P(0, 3.2f, 1.6f), "glowamber", 0.6f, 6f);
            ClockTower(vb, new Vector3(8.4f, 0, 17.6f));
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

        // ------------------------------------------------------------------ east row (facing west, right in frame)

        private static void EastRow(VB vb)
        {
            // E1: toyshop, green awning (the clay dino is in its window: Eggs).
            var toys = ToyFr;
            House(vb, toys, 6.6f, 7.6f, 7f, "tl_stucco_sage", "stuccosage", "shutterblue", 111, balcony: true);
            ShopWindow(vb, toys, -0.9f, 3.4f, "tl_win_toys");
            Door(vb, toys, 2.4f, "awninggreen");
            Awning(vb, toys, 0.3f, 6.0f, 3.15f, 1.6f, "awninggreen", "petalwhite");
            WallLantern(vb, toys, -3.05f, 2.9f);
            Vine(vb, toys, 3.1f, 3.3f, -1.8f, 15);
            // E2: parcels shop (orb lamp, parcels and the red burst poster: Eggs).
            var parcels = ParcelFr;
            House(vb, parcels, 6.2f, 7.0f, 7f, "tl_stucco_ochre", "stuccoochre", "shuttergreen", 121);
            ShopWindow(vb, parcels, 1.0f, 2.2f, "tl_win_parcels");
            Door(vb, parcels, -1.6f, "walnut");
            // E3: market building (the stall and crates stand in front of it).
            var market = MarketFr;
            House(vb, market, 9.4f, 7.2f, 7f, "tl_stucco_peach", "stuccopeach", "shuttergreen", 131, balcony: true);
            Door(vb, market, -3.4f, "walnut");
            Door(vb, market, 3.6f, "walnut");
            ShopWindow(vb, market, 0.1f, 3.0f, "tl_win_bakery");
            WallLantern(vb, market, -1.9f, 2.9f);
            WallLantern(vb, market, 2.1f, 2.9f);
            Vine(vb, market, -4.5f, 3.3f, 1.8f, 132);
            SideFace(vb, new Fr(new Vector3(EastX + 3.5f, 0, SouthZ), 180f), 7f, 7.2f, "tl_stucco_peach", "shuttergreen", 133);
            for (int i = 0; i < 3; i++)
            {
                var back = new Fr(new Vector3(21.5f + (i % 2) * 2.5f, 2.6f + (i % 2) * 1.2f, -6f + i * 7.5f), -90f);
                House(vb, back, 6.5f, 6.5f + (i % 2), 6f, i % 2 == 0 ? "tl_stucco_peach" : "tl_stucco_cream", "stuccopeach", null, 300 + i, far: true);
            }
        }

        /// <summary>Painted end wall of a row block (the camera-facing south ends), with two windows and a plinth.</summary>
        private static void SideFace(VB vb, Fr fr, float width, float height, string stucco, string shutter, int seed)
        {
            vb.Sign(stucco, fr.P(0, 0, 0.012f), width, fr.yaw, height, lit: true);
            vb.Box(fr.P(0, 0.32f, 0.04f), new Vector3(width + 0.02f, 0.64f, 0.08f), "stoneshade", fr.yaw);
            Window(vb, fr, -1.5f, 3.95f, false, shutter, true, seed);
            Window(vb, fr, 1.5f, 3.95f, true, shutter, false, seed + 1);
            Window(vb, fr, 0f, 0.95f, false, null, false, seed + 2);
        }

        /// <summary>Iron wall bracket with a lit lantern (baked warm light).</summary>
        private static void WallLantern(VB vb, Fr fr, float u, float y)
        {
            var b = fr.P(u, y, 0f);
            vb.Box(b, new Vector3(0.12f, 0.24f, 0.04f), "iron", fr.yaw);
            vb.Pipe(b, b + fr.f * 0.45f, 0.02f, "iron", 5);
            var l = b + fr.f * 0.45f - Vector3.up * 0.32f;
            vb.Pipe(b + fr.f * 0.45f, l + Vector3.up * 0.22f, 0.01f, "iron", 4);
            vb.Box(l, new Vector3(0.2f, 0.3f, 0.2f), "glowamber", fr.yaw, glow: true);
            vb.Box(l + Vector3.up * 0.18f, new Vector3(0.26f, 0.06f, 0.26f), "iron", fr.yaw);
            vb.Box(l - Vector3.up * 0.17f, new Vector3(0.22f, 0.04f, 0.22f), "iron", fr.yaw);
            vb.PointLight(l + fr.f * 0.2f, "glowamber", 0.55f, 4f);
        }

        /// <summary>Terracotta pot with flowers (a floor prop).</summary>
        private static void Pot(VB vb, Vector3 p, float r, int seed)
        {
            vb.Cylinder(p, r * 0.85f, r * 1.6f, "terracotta", 12, topRadius: r);
            vb.Arc(p, r * 0.92f, r * 1.08f, 0f, 360f, r * 1.5f, r * 1.7f, "terracotta", 14);
            vb.RegisterProp("pot", p + Vector3.up * r * 0.8f, new Vector3(r * 2.1f, r * 1.6f, r * 2.1f));
            Flowers(vb, p + Vector3.up * r * 1.6f, r * 1.4f, r * 1.4f, seed * 37f, seed, tall: true);
        }

        // ------------------------------------------------------------------ south edge (camera side)

        private static void SouthEdge(VB vb)
        {
            const float z = SouthZ;
            foreach (var (x0, x1) in new[] { (WestX, -2.7f), (2.7f, EastX) })
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
            // Gate pillars with lanterns.
            foreach (float sx in new[] { -1f, 1f })
            {
                vb.Box(new Vector3(sx * 2.7f, 1.0f, z), new Vector3(0.6f, 2.0f, 0.6f), "limestone", collider: true);
                vb.Box(new Vector3(sx * 2.7f, 2.05f, z), new Vector3(0.72f, 0.1f, 0.72f), "stuccocream");
                vb.Box(new Vector3(sx * 2.7f, 2.3f, z), new Vector3(0.26f, 0.38f, 0.26f), "glowamber", glow: true);
                vb.Box(new Vector3(sx * 2.7f, 2.53f, z), new Vector3(0.34f, 0.08f, 0.34f), "iron");
                vb.PointLight(new Vector3(sx * 2.7f, 2.4f, z + 0.4f), "glowamber", 0.6f, 4.5f);
            }
        }

        // ------------------------------------------------------------------ community eggs (our own drawings)

        private static void Eggs(VB vb)
        {
            CafeTerrace(vb);
            var sill = CafeFr.P(0f, 3.91f, 0.3f);
            using (vb.Scaled(sill, 2.0f)) VenueEggs.JupiterCat(vb, sill, 90f);
            var ghostLamp = new Vector3(-7.0f, 0f, -3.2f);
            Lamp(vb, ghostLamp);
            using (vb.Scaled(ghostLamp, 1.6f)) VenueEggs.PhantomGhost(vb, ghostLamp);
            PopcatWindow(vb, BakeryFr, 0.9f, 1.7f);
            OrePlot(vb, new Vector3(-7.0f, 0f, -10.2f));
            BonkStall(vb, new Vector3(9.2f, 0f, -8.4f), 155f);
            var igloo = new Vector3(6.4f, 0f, -5.4f);
            using (vb.Scaled(igloo, 1.35f)) IglooCart(vb, igloo, 220f);
            var capsule = new Vector3(3.8f, 0f, -10.8f);
            using (vb.Scaled(capsule, 1.5f)) CapsuleMachine(vb, capsule, 200f);
            HeliusLamp(vb, ParcelFr, -1.6f, 1.6f);
            MadLadsPoster(vb, ParcelFr, 0f);
            ClayDino(vb, ToyFr, -0.9f, 1.8f);
            // Superteam-style bunting: two strings over the square's back corners (top centre stays open, as in the title art).
            Bunting(vb, new Vector3(WestX + 0.1f, 6.7f, 0.6f), new Vector3(-4.2f, 6.6f, NorthZ - 0.05f), 15, 0);
            Bunting(vb, new Vector3(4.2f, 6.6f, NorthZ - 0.05f), new Vector3(EastX - 0.1f, 6.7f, 0.6f), 15, 1);
            ColosseumHall(vb, new Vector3(-12.5f, 3.6f, 27f));
        }

        private static void CafeTerrace(VB vb)
        {
            var tables = new[] { new Vector3(-9.9f, 0, -7.4f), new Vector3(-7.7f, 0, -5.6f), new Vector3(-9.9f, 0, -4.6f) };
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
                if (t != 1) vb.Blob(p + new Vector3(-0.2f, 0.8f, -0.12f), new Vector3(0.16f, 0.07f, 0.09f), "pizza", yaw: 30f);
                vb.ModelAt("plant_small", p + new Vector3(-0.15f, 0.76f, 0.22f), 0f, 1.2f);
            }
            // Seeker-style phone (back up, three lens dots, a small engraved shield) beside a coffee on the table nearest the
            // plaza, 1.6x so it reads from the gameplay camera.
            var tp = tables[1] + new Vector3(-0.12f, 0.765f, -0.2f);
            VenueEggs.SeekerPhone(vb, tp, 20f, 1.6f);
            // Big white umbrellas over the two tables by the café wall.
            vb.ModelAt("parasol", tables[0], 0f, 1.15f, recolor: "petalwhite");
            // dogwifhat-style dog beside the plaza table: tan, sitting very still, rose-pink knit beanie.
            var dog = tables[1] + new Vector3(1.05f, 0f, -0.75f);
            using (vb.Scaled(dog, 1.6f)) VenueEggs.DogWithBeanie(vb, dog, 150f);
            vb.RegisterProp("dog", dog + Vector3.up * 0.4f, new Vector3(0.6f, 0.8f, 0.7f), 150f);
            // Flower pots along the terrace edge.
            Pot(vb, new Vector3(-10.9f, 0f, -9.0f), 0.3f, 21);
            Pot(vb, new Vector3(-10.9f, 0f, -3.1f), 0.28f, 22);
        }

        private static void PopcatWindow(VB vb, Fr fr, float u, float scale) =>
            VenueEggs.Popcat(vb, fr.P(u, 0.9f, 0.32f), fr.yaw, scale, doughnutOnString: true);

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

        private static void BonkStall(VB vb, Vector3 p, float yaw)
        {
            // Market stall: yellow-and-orange striped awning, a red exclamation-mark pennant, a mouthless sunny dog
            // balloon tied to the post, fruit and veg crates, sunflowers. No disc badge, no letters.
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
            using var pennant = vb.Scaled(pole + Vector3.up * 0.1f, 1.6f);
            vb.Triangle(pole + Vector3.up * 0.1f, pole + Vector3.up * 0.1f + q * new Vector3(0, 0, 0.7f) + new Vector3(0, 0, 0), pole - Vector3.up * 0.75f + q * new Vector3(0, 0, 0.35f), "petalred");
            var markBase = pole + q * new Vector3(-0.012f, 0f, 0.35f);
            vb.Box(markBase + Vector3.up * -0.12f, new Vector3(0.012f, 0.26f, 0.06f), "petalwhite", yaw);
            vb.Box(markBase + Vector3.up * -0.38f, new Vector3(0.012f, 0.06f, 0.06f), "petalwhite", yaw);
            vb.Box(markBase + q * new Vector3(0.024f, 0, 0) + Vector3.up * -0.12f, new Vector3(0.012f, 0.26f, 0.06f), "petalwhite", yaw);
            vb.Box(markBase + q * new Vector3(0.024f, 0, 0) + Vector3.up * -0.38f, new Vector3(0.012f, 0.06f, 0.06f), "petalwhite", yaw);
            pennant.Dispose();
            // Mouthless sunny dog balloon on a string tied to the front post (1.7x so it reads across the square).
            var tie = L(-0.55f, 1.0f, -1.55f);
            var bal = tie + new Vector3(-0.6f, 2.9f, 0.2f);
            vb.Pipe(tie, bal - Vector3.up * 0.7f, 0.006f, "offwhite", 3);
            using var big = vb.Scaled(bal, 1.7f);
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
            for (int r = 0; r < 3; r++) vb.Cylinder(L(0, 0.85f + r * 0.18f, -0.1f), 0.665f - r * 0.12f, 0.015f, "penguinblue", 20, caps: false);
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

        private static void HeliusLamp(VB vb, Fr fr, float u, float scale)
        {
            // Glowing orange orb lamp on an iron bracket over the parcels door, a stack of unlabelled parcels below.
            var bracket = fr.P(u, 3.3f, 0.0f);
            var big = vb.Scaled(bracket, scale);
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
            big.Dispose();
            var base0 = fr.P(u - 1.0f, 0f, 0.55f);
            float[] sizes = { 0.55f, 0.45f, 0.5f, 0.35f, 0.42f };
            for (int i = 0; i < 5; i++)
            {
                float s = sizes[i];
                var pc = base0 - fr.r * ((i % 3) * 0.5f - 0.3f) + Vector3.up * (s * .5f + (i >= 3 ? 0.5f : 0f));
                vb.Box(pc, new Vector3(s, s * 0.9f, s * 0.8f), "cardboard", -90f + i * 7f);
                vb.Box(pc + Vector3.up * s * 0.45f, new Vector3(s * 0.15f, 0.01f, s * 0.82f), "sand", -90f + i * 7f);
            }
            vb.RegisterProp("parcels", base0 - fr.r * 0.2f + Vector3.up * 0.5f, new Vector3(1.6f, 1.0f, 0.7f), -90f);
        }

        private static void MadLadsPoster(VB vb, Fr fr, float u)
        {
            // Hot-red poster with a black jagged lightning frame (no letters, no character), 1.6x the v2 size.
            vb.Sign("tl_burst", fr.P(u, 3.72f, 0.09f), 1.25f, fr.yaw, 1.56f, frame: "iron", lit: true);
        }

        private static void ClayDino(VB vb, Fr fr, float u, float scale)
        {
            // Hand-modelled clay dino toy in the toyshop window: lopsided grin, thumbprint dents, a toy house beside it.
            var c = fr.P(u + 0.6f, 0.62f, 0.32f);
            using var big = vb.Scaled(fr.P(u + 0.6f, 0.62f, 0.32f), scale);
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
            var house = fr.P(u - 0.55f, 0.75f, 0.32f);
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
                    vb.Sign("tl_flag_" + ((i * 5 + seed * 3) % 16), p - Vector3.up * 0.5f, 0.72f, facing, 0.48f, lit: true);
                }
                else
                {
                    string col = (i + seed) % 2 == 0 ? "solpurple" : "ctgold";
                    vb.Triangle(p - along * 0.3f, p + along * 0.3f, p - Vector3.up * 0.7f, col);
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
            foreach (var p in new[] { new Vector3(-10.3f, 0, 3.0f), new Vector3(10.3f, 0, 3.2f), new Vector3(-10.6f, 0, -11.3f), new Vector3(10.6f, 0, -11.4f) })
                CitrusTree(vb, p, (int)(p.x * 3 + p.z));
            // Flower troughs along the north fronts and by the toyshop.
            foreach (var (pos, yaw, len) in new[]
                     {
                         (new Vector3(-10.4f, 0, 9.35f), 180f, 1.6f), (new Vector3(10.2f, 0, 9.35f), 180f, 1.8f), (new Vector3(-5.2f, 0, 9.35f), 180f, 1.2f),
                         (new Vector3(5.4f, 0, 9.35f), 180f, 1.2f),
                     })
            {
                vb.Box(pos + Vector3.up * 0.25f, new Vector3(len, 0.5f, 0.55f), "limestone", yaw);
                vb.RegisterProp("trough", pos + Vector3.up * 0.25f, new Vector3(len, 0.5f, 0.55f), yaw);
                Flowers(vb, pos + Vector3.up * 0.5f, len - 0.2f, 0.4f, yaw, (int)(pos.x + pos.z * 3), tall: true);
            }
            // Market: barrels, stacked fruit crates, sacks, a bicycle at the toyshop, pots at doors.
            foreach (var (x, z) in new[] { (10.9f, -3.55f), (10.45f, -4.05f) })
            {
                vb.Cylinder(new Vector3(x, 0, z), 0.3f, 0.8f, "walnut", 12);
                vb.Arc(new Vector3(x, 0, z), 0.29f, 0.31f, 0f, 360f, 0.15f, 0.2f, "iron", 16);
                vb.Arc(new Vector3(x, 0, z), 0.29f, 0.31f, 0f, 360f, 0.6f, 0.65f, "iron", 16);
                vb.Sphere(new Vector3(x, 0.86f, z), 0.12f, "petalred", rings: 3, segments: 6);
                vb.RegisterProp("barrel", new Vector3(x, 0.4f, z), new Vector3(0.62f, 0.8f, 0.62f));
            }
            string[] fruit = { "ctorange", "petalred", "grass", "petalyellow", "ctorange", "petalred" };
            int f = 0;
            foreach (var (x, z, h) in new[] { (10.9f, -5.3f, 0f), (10.9f, -5.3f, 0.42f), (10.9f, -5.95f, 0f), (10.25f, -5.6f, 0f), (10.9f, -10.6f, 0f), (10.9f, -10.6f, 0.42f) })
            {
                vb.Box(new Vector3(x, h + 0.2f, z), new Vector3(0.62f, 0.4f, 0.5f), "plywood", 8f);
                for (int k = 0; k < 6; k++)
                    vb.Sphere(new Vector3(x - 0.18f + (k % 3) * 0.18f, h + 0.44f, z - 0.1f + (k / 3) * 0.2f), 0.085f, fruit[f % fruit.Length], rings: 3, segments: 6);
                f++;
            }
            vb.RegisterProp("crates", new Vector3(10.6f, 0.42f, -5.6f), new Vector3(1.3f, 0.84f, 1.3f));
            vb.RegisterProp("crates", new Vector3(10.9f, 0.42f, -10.6f), new Vector3(0.7f, 0.84f, 0.6f));
            Bicycle(vb, new Vector3(10.9f, 0, 8.4f), 0f);
            Pot(vb, new Vector3(10.9f, 0f, 3.9f), 0.3f, 31);
            Pot(vb, new Vector3(-10.9f, 0f, 9.2f), 0.3f, 32);
            Pot(vb, MarketFr.P(-2.6f, 0f, 0.45f), 0.28f, 33);
            Pot(vb, ParcelFr.P(2.6f, 0f, 0.45f), 0.28f, 34);
        }

        // ------------------------------------------------------------------ round 5: lushness (vines, wisteria, baskets, market)

        /// <summary>Hanging basket: chains, a moss-lined bowl, a spill of flowers and trailing ivy.</summary>
        private static void HangingBasket(VB vb, Vector3 hook, float drop, int seed)
        {
            var rnd = new System.Random(Mathf.Abs(seed) % 9973 + 3);
            var bowl = hook - Vector3.up * drop;
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                vb.Pipe(hook, bowl + new Vector3(Mathf.Cos(a) * 0.2f, 0.12f, Mathf.Sin(a) * 0.2f), 0.006f, "iron", 3);
            }
            vb.Cylinder(bowl - Vector3.up * 0.12f, 0.1f, 0.24f, "walnut", 10, topRadius: 0.24f);
            vb.Blob(bowl + Vector3.up * 0.12f, new Vector3(0.52f, 0.26f, 0.52f), "ivy", rings: 3, segments: 7);
            string[] petals = { "petalpink", "petalred", "petalwhite", "wisteria", "petalyellow", "ctorange" };
            string bloom = petals[Mathf.Abs(seed) % petals.Length];
            for (int j = 0; j < 9; j++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = 0.12f + (float)rnd.NextDouble() * 0.14f;
                vb.Sphere(bowl + new Vector3(Mathf.Cos(a) * r, 0.16f + (float)rnd.NextDouble() * 0.12f, Mathf.Sin(a) * r), 0.06f, j % 3 == 2 ? petals[(j + seed & 0xff) % petals.Length] : bloom, rings: 2, segments: 4);
            }
            for (int t = 0; t < 4; t++)
            {
                float a = t * Mathf.PI * 0.5f + 0.4f;
                var top = bowl + new Vector3(Mathf.Cos(a) * 0.22f, 0.02f, Mathf.Sin(a) * 0.22f);
                float len = 0.35f + (float)rnd.NextDouble() * 0.35f;
                vb.Blob(top - Vector3.up * len * 0.5f, new Vector3(0.12f, len, 0.12f), t % 2 == 0 ? "leafdark" : "ivy", rings: 3, segments: 5);
            }
        }

        /// <summary>Leafy garland with hanging wisteria racemes along a facade (under a cornice or band).</summary>
        private static void WisteriaGarland(VB vb, Fr fr, float u0, float u1, float y, int seed, float maxDrop = 0.7f)
        {
            var rnd = new System.Random(Mathf.Abs(seed) % 9973 + 11);
            int n = Mathf.Max(2, Mathf.RoundToInt(Mathf.Abs(u1 - u0) / 0.36f));
            for (int i = 0; i <= n; i++)
            {
                float u = Mathf.Lerp(u0, u1, i / (float)n);
                vb.Blob(fr.P(u, y + (float)rnd.NextDouble() * 0.1f, 0.2f), new Vector3(0.5f, 0.36f, 0.34f), i % 2 == 0 ? "ivy" : "leafdark", yaw: fr.yaw, rings: 3, segments: 6);
                float len = 0.3f + (float)rnd.NextDouble() * (maxDrop - 0.3f);
                vb.Blob(fr.P(u + 0.08f, y - len * .5f, 0.27f), new Vector3(0.15f, len, 0.15f), i % 3 == 0 ? "wisterialight" : "wisteria", rings: 3, segments: 5);
            }
        }

        private static void Lushness(VB vb)
        {
            // More climbers on the facades that had none, and wisteria garlands under the first-floor bands.
            Vine(vb, BakeryFr, 3.1f, 3.25f, -1.4f, 61);
            Vine(vb, ParcelFr, -2.9f, 3.25f, 1.0f, 62);
            Vine(vb, MarketFr, 4.5f, 3.25f, -1.6f, 63);
            Vine(vb, ToyFr, -3.1f, 6.2f, 1.6f, 64);
            WisteriaGarland(vb, N1Fr, -3.6f, 3.6f, 3.3f, 65, 0.6f);
            WisteriaGarland(vb, CornerFr, -3.3f, 3.3f, 6.4f, 66);
            WisteriaGarland(vb, ParcelFr, -2.6f, 2.6f, 6.3f, 67);
            WisteriaGarland(vb, N2Fr, 2.2f, 3.6f, 3.3f, 68, 0.6f);
            // Hanging baskets: from the awning edges (café, bakery, toyshop) and on brackets beside shopfronts.
            foreach (float u in new[] { -3.0f, -1.0f, 1.0f, 3.0f })
                HangingBasket(vb, CafeFr.P(u, 2.3f, 1.95f), 0.42f, 70 + (int)(u * 3));
            foreach (float u in new[] { -2.2f, 1.6f })
                HangingBasket(vb, BakeryFr.P(u, 2.3f, 1.45f), 0.42f, 80 + (int)(u * 3));
            foreach (float u in new[] { -1.8f, 2.0f })
                HangingBasket(vb, ToyFr.P(u, 2.3f, 1.55f), 0.42f, 90 + (int)(u * 3));
            foreach (var (fr, u) in new[] { (CornerFr, -2.95f), (CornerFr, 2.95f), (N2Fr, 3.15f), (ParcelFr, 2.6f), (MarketFr, -1.0f), (MarketFr, 1.2f) })
            {
                var b = fr.P(u, 3.05f, 0f);
                vb.Pipe(b, b + fr.f * 0.5f, 0.018f, "iron", 4);
                vb.Box(b, new Vector3(0.08f, 0.16f, 0.03f), "iron", fr.yaw);
                HangingBasket(vb, b + fr.f * 0.5f, 0.45f, (int)(b.x * 5 + b.z * 7));
            }
            // A few more market props: a produce table with baskets and sacks by the stall.
            var t = new Vector3(8.2f, 0f, -11.4f);
            vb.Box(t + Vector3.up * 0.42f, new Vector3(1.6f, 0.06f, 0.7f), "oak", -20f);
            foreach (var (dx, dz) in new[] { (-0.7f, -0.28f), (0.7f, -0.28f), (-0.7f, 0.28f), (0.7f, 0.28f) })
                vb.Box(t + Quaternion.Euler(0, -20f, 0) * new Vector3(dx, 0.21f, dz), new Vector3(0.05f, 0.42f, 0.05f), "walnut", -20f);
            string[] produce = { "ctorange", "petalred", "grass", "petalyellow" };
            for (int i = 0; i < 4; i++)
            {
                var bp = t + Quaternion.Euler(0, -20f, 0) * new Vector3(-0.55f + i * 0.37f, 0.45f, 0f);
                vb.Cylinder(bp, 0.15f, 0.12f, "plywood", 10, topRadius: 0.17f);
                for (int k = 0; k < 5; k++) vb.Sphere(bp + new Vector3(-0.07f + (k % 3) * 0.07f, 0.15f, -0.04f + (k / 3) * 0.08f), 0.055f, produce[i], rings: 2, segments: 5);
            }
            vb.RegisterProp("produce_table", t + Vector3.up * 0.25f, new Vector3(1.7f, 0.5f, 0.8f), -20f);
            foreach (var (dx, dz) in new[] { (1.1f, 0.5f), (1.3f, -0.2f) })
            {
                vb.Blob(t + new Vector3(dx, 0.28f, dz), new Vector3(0.45f, 0.56f, 0.4f), "sand", yaw: dx * 40f, rings: 4, segments: 7);
                vb.RegisterProp("sack", t + new Vector3(dx, 0.28f, dz), new Vector3(0.45f, 0.56f, 0.4f));
            }
        }

        /// <summary>String lights across the square, facade to facade, with warm bulbs (unlit glow, no lights).</summary>
        private static void StringLights(VB vb)
        {
            foreach (var (a, b) in new[]
                     {
                         (new Vector3(WestX + 0.1f, 5.9f, -9.5f), new Vector3(EastX - 0.1f, 5.9f, -5.0f)),
                         (new Vector3(WestX + 0.1f, 5.9f, -3.2f), new Vector3(EastX - 0.1f, 5.9f, 1.4f)),
                         (new Vector3(WestX + 0.1f, 5.9f, 3.4f), new Vector3(EastX - 0.1f, 5.9f, 7.6f)),
                     })
            {
                Vector3 Cord(float t) => Vector3.Lerp(a, b, t) + Vector3.down * (1.1f * (1f - (2f * t - 1f) * (2f * t - 1f)));
                const int segs = 24;
                for (int i = 0; i < segs; i++) vb.Pipe(Cord(i / (float)segs), Cord((i + 1) / (float)segs), 0.008f, "iron", 4);
                for (int i = 1; i < segs; i++)
                {
                    var p = Cord(i / (float)segs);
                    vb.Pipe(p, p - Vector3.up * 0.1f, 0.005f, "iron", 3);
                    vb.Sphere(p - Vector3.up * 0.16f, 0.07f, "glowwhite", glow: true, rings: 3, segments: 6);
                }
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
            // Hill behind the town hall: a terraced slope with a hill town, cypresses and round trees.
            vb.Box(new Vector3(0, 1.6f, 36f), new Vector3(90f, 3.2f, 26f), "olive");
            vb.Box(new Vector3(0, 0.8f, 23.5f), new Vector3(90f, 1.6f, 3f), "olive");
            var rnd = new System.Random(5);
            for (int i = 0; i < 14; i++)
            {
                float x = -30f + i * 4.6f + (float)rnd.NextDouble() * 1.5f;
                float z = 24f + (float)rnd.NextDouble() * 9f;
                if (Mathf.Abs(x) < 6f) continue;
                float h = 5f + (float)rnd.NextDouble() * 4f;
                float y0 = z > 25f ? 3.2f : 1.6f;
                vb.Blob(new Vector3(x, y0 + h * .5f, z), new Vector3(1.3f, h, 1.3f), "cypress", rings: 6, segments: 8);
                if (i % 3 == 0) vb.Blob(new Vector3(x + 2.2f, y0 + 1.6f, z + 1.5f), new Vector3(3.2f, 2.8f, 3.0f), "leafdark", rings: 5, segments: 8);
            }
            for (int i = 0; i < 10; i++)
            {
                float x = -34f + i * 7.5f;
                if (x > -21f && x < -4f) continue; // the Colosseum-style hall stands there
                var fr = new Fr(new Vector3(x, 3.2f + (i % 3) * 1.4f, 30f + (i % 2) * 6f), 180f);
                House(vb, fr, 6.5f, 5f + (i % 2) * 2f, 6f, i % 3 == 0 ? "tl_stucco_ochre" : i % 3 == 1 ? "tl_stucco_cream" : "tl_stucco_peach", "stuccocream", null, 400 + i, far: true);
            }
            vb.Box(new Vector3(0, -0.06f, 8f), new Vector3(120f, 0.1f, 90f), "stuccostone");
            // Painted golden-hour sky (Solana gradient wash), sea, far hills and hill town.
            vb.Sign("tl_sky", new Vector3(0f, -6f, 92f), 260f, 180f, 130f);
            vb.Sign("tl_sky", new Vector3(-92f, -6f, 10f), 260f, 90f, 130f);
            vb.Sign("tl_sky", new Vector3(92f, -6f, 10f), 260f, -90f, 130f);
            vb.Backdrop = false;
        }
    }
}
