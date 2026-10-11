using CTLife.Core;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Terminal Café v2 (round 5), built to the in-game concept (art-direction/out/ingame-cafe-concept-v1.png): a sunny
    /// corner café that is also the trenches' coworking spot.
    ///  * West: big street windows (the town outside) with warm afternoon sun streaming in, a window seat with a yellow
    ///    cushion, the sage sofa lounge. North-west: the wooden coffee bar with green tiles, pastry case, stools, espresso
    ///    machine and grinders on the back counter, open shelves, a chalkboard menu (doodles only), hanging pothos.
    ///  * Centre: the long communal work table with laptops (terminal_1..4), two round tables (table_1..4).
    ///  * North-east: green booths under the back windows. East: exposed brick with the sunrise-coffee neon, shelves, framed
    ///    prints, and four twin-monitor trading desks with candle charts (terminal_5..8). South-east: armchairs on a round rug.
    ///  * Pendant lights, rugs, plants everywhere.
    ///  * Eggs (docs/research/community-eggs-2026-10.md, our own drawings): the astronaut cat on the window-seat cushion, the
    ///    pink-beanie dog under the communal table, the three-lens phone on the bar, the purple ghost plush on a shelf, the
    ///    O-mouth cat by the till.
    /// Spot ids are the contract's; spots.json fe.6 moves the terminals, tables and the counter onto the new furniture.
    /// The fitted seat presentation (CafeSeatMotionProfile) is bound from the authored café and its exit routes regenerated.
    /// </summary>
    public static class VenuesCafe
    {
        private const float WallH = 4.6f, BarZ = 4.5f, BarX0 = -11.0f, BarX1 = -3.2f, BackCounterZ = 8.45f;
        private const float WinWestX = -12.1f, NorthZ = 9.1f, NorthWinX0 = -0.2f;

        private static Vector3 Fwd(SpotDef s) => new Vector3(Mathf.Sin(s.rotY), 0f, Mathf.Cos(s.rotY));
        private static Vector3 Right(SpotDef s) => new Vector3(Mathf.Cos(s.rotY), 0f, -Mathf.Sin(s.rotY));

        public static void TerminalCafe(VB vb)
        {
            vb.InteriorShell("floor_wood_2m", "cream", wallHeight: WallH, westHeight: 0f, frontHeight: 0.9f, trim: "walnut", back: false);
            var inn = vb.Inner;
            WestWindows(vb);
            NorthWall(vb, inn);
            EastWall(vb, inn);
            Outside(vb);
            Bar(vb);
            Booths(vb);
            CommunalTable(vb);
            RoundTables(vb);
            TradingDesks(vb);
            Lounge(vb);
            Armchairs(vb);
            FrontPlanters(vb, inn);
            Plants(vb);
            Eggs(vb);
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 0.55f, 1.4f);
            // Fill: warm bounce from the sunny floor, a cool sky fill from the back windows.
            vb.PointLight(new Vector3(-4f, 3.2f, -2f), "glowamber", 0.55f, 11f);
            vb.PointLight(new Vector3(6f, 3.4f, 6.5f), "skyblue", 0.35f, 9f);
        }

        // ------------------------------------------------------------------ shell

        private static void WestWindows(VB vb)
        {
            const float x = WinWestX;
            vb.Box(new Vector3(x, 0.28f, 0f), new Vector3(0.3f, 0.56f, 18.6f), "walnut", collider: true);
            vb.Box(new Vector3(x + 0.08f, 0.58f, 0f), new Vector3(0.46f, 0.05f, 18.6f), "oak");
            vb.Box(new Vector3(x, 4.33f, 0f), new Vector3(0.3f, 0.54f, 18.6f), "cream");
            vb.Box(new Vector3(x + 0.12f, 4.05f, 0f), new Vector3(0.2f, 0.1f, 18.6f), "walnut");
            vb.Box(new Vector3(x + 0.05f, 3.15f, 0f), new Vector3(0.08f, 0.06f, 18.6f), "darkwood");
            for (float z = -9f; z <= 9.01f; z += 1.8f)
                vb.Box(new Vector3(x, 2.3f, z), new Vector3(0.16f, 3.5f, 0.16f), "darkwood", collider: true);
            for (float z = -8.1f; z <= 8.2f; z += 1.8f)
                vb.Box(new Vector3(x + 0.03f, 1.85f, z), new Vector3(0.04f, 2.6f, 0.04f), "darkwood");
            // Invisible wall in the openings (the windows are open so the baked sun streams in).
            vb.Collider(new Vector3(x, 2.3f, 0f), new Vector3(0.2f, 3.5f, 18.6f));
            // Hanging pothos trailing from the window head.
            var rnd = new System.Random(7);
            for (float z = -8.5f; z <= 8.5f; z += 2.1f)
                for (int k = 0; k < 3; k++)
                {
                    float len = 0.5f + (float)rnd.NextDouble() * 0.9f;
                    vb.Blob(new Vector3(x + 0.32f, 4.0f - len * .5f, z + k * 0.28f - 0.28f), new Vector3(0.18f, len, 0.2f), k % 2 == 0 ? "ivy" : "leafdark", rings: 3, segments: 5);
                }
        }

        private static void NorthWall(VB vb, Rect inn)
        {
            const float z = NorthZ;
            // Solid part behind the bar, green tiled splashback, shelves and the chalkboard menu.
            float cx = (-12.3f + NorthWinX0) * .5f, len = NorthWinX0 + 12.3f;
            vb.Box(new Vector3(cx, WallH * .5f, z), new Vector3(len, WallH, 0.3f), "cream", collider: true);
            vb.Box(new Vector3(cx, WallH + 0.06f, z), new Vector3(len + 0.1f, 0.12f, 0.36f), "walnut");
            for (float x = -11.6f; x < NorthWinX0 - 0.3f; x += 1.0f)
                vb.Sign("cf_tiles", new Vector3(x + 0.5f, 0.95f, z - 0.16f), 1.0f, 180f, 1.0f, lit: true);
            vb.Box(new Vector3(cx, 1.97f, z - 0.17f), new Vector3(len, 0.06f, 0.05f), "oak");
            // Chalkboard menu (doodles only) in a wood frame.
            vb.Sign("cf_chalk_menu", new Vector3(-4.6f, 2.05f, z - 0.2f), 3.4f, 180f, 1.7f, frame: "walnut", lit: true);
            // Open shelves left of the board: cups, jars, plants (the ghost plush sits on the middle one: Eggs).
            foreach (float y in new[] { 2.2f, 2.8f, 3.4f })
            {
                vb.Box(new Vector3(-9.1f, y, z - 0.32f), new Vector3(3.6f, 0.05f, 0.34f), "oak");
                for (int i = 0; i < 6; i++)
                {
                    float x = -10.6f + i * 0.56f;
                    if (y == 2.8f && i == 2) continue;
                    if (i % 3 == 0) vb.ModelAt("mug", new Vector3(x, y + 0.025f, z - 0.3f), 180f, 1.3f);
                    else if (i % 3 == 1) vb.Cylinder(new Vector3(x, y + 0.025f, z - 0.3f), 0.08f, 0.22f, "glowwhite", 10, topRadius: 0.07f);
                    else vb.ModelAt("plant_small", new Vector3(x, y + 0.025f, z - 0.3f), 0f, 1.3f);
                }
            }
            for (int k = 0; k < 4; k++)
                vb.Blob(new Vector3(-10.9f + k * 1.1f, 3.0f, z - 0.4f), new Vector3(0.2f, 0.7f + (k % 2) * 0.4f, 0.2f), k % 2 == 0 ? "ivy" : "leafdark", rings: 3, segments: 5);
            // Window part over the booths: sill wall, posts, transom, header.
            float wx0 = NorthWinX0, wx1 = inn.xMax + 0.3f, wcx = (wx0 + wx1) * .5f, wl = wx1 - wx0;
            vb.Box(new Vector3(wcx, 0.55f, z), new Vector3(wl, 1.1f, 0.3f), "cream", collider: true);
            vb.Box(new Vector3(wcx, 1.12f, z - 0.08f), new Vector3(wl, 0.05f, 0.42f), "oak");
            vb.Box(new Vector3(wcx, 4.2f, z), new Vector3(wl, 0.8f, 0.3f), "cream");
            vb.Box(new Vector3(wcx, 3.82f, z - 0.12f), new Vector3(wl, 0.08f, 0.16f), "walnut");
            for (float x = wx0; x <= wx1 + 0.01f; x += 2.08f)
                vb.Box(new Vector3(x, 2.45f, z), new Vector3(0.16f, 2.7f, 0.16f), "darkwood", collider: true);
            vb.Collider(new Vector3(wcx, 2.45f, z), new Vector3(wl, 2.7f, 0.2f));
            // Pots on the sill and pothos trailing from the window head.
            foreach (float x in new[] { 1.3f, 5.6f, 10.6f }) vb.ModelAt("home_houseplant_5", new Vector3(x, 1.145f, z - 0.05f), x * 40f, 0.75f);
            var rnd = new System.Random(19);
            for (float x = wx0 + 0.6f; x < wx1 - 0.4f; x += 1.04f)
            {
                float drop = 0.45f + (float)rnd.NextDouble() * 0.8f;
                vb.Blob(new Vector3(x, 3.78f - drop * .5f, z - 0.22f), new Vector3(0.2f, drop, 0.18f), rnd.Next(2) == 0 ? "ivy" : "leafdark", rings: 3, segments: 5);
            }
            // A small market TV on the pillar by the pulse_screen spot.
            vb.Sign("cf_candles_4", new Vector3(-0.15f, 1.55f, z - 0.17f), 0.95f, 180f, 0.6f, frame: "black");
        }

        private static void EastWall(VB vb, Rect inn)
        {
            float x = inn.xMax - 0.01f;
            // Exposed brick over the whole inner face.
            for (float z = -9.0f; z < 9.0f; z += 2.25f)
                for (int row = 0; row < 2; row++)
                    vb.Sign("cf_brick", new Vector3(x, row * 2.3f, z + 1.125f), 2.25f, -90f, 2.3f, lit: true);
            // Sunrise-coffee neon: a cup with rays above it (glow pipes, no letters).
            var c = new Vector3(x - 0.08f, 2.1f, 3.6f); // above the trading desks, as in the concept
            var big = vb.Scaled(c, 1.7f);
            var q = Quaternion.Euler(0, -90f, 0);
            Vector3 L(float u, float v) => c + q * new Vector3(u, v, 0f);
            void Neon(Vector3 a, Vector3 b) => vb.Pipe(a, b, 0.025f, "glowamber", 6, glow: true);
            Neon(L(-0.42f, 0.3f), L(-0.34f, -0.3f)); Neon(L(-0.34f, -0.3f), L(0.34f, -0.3f)); Neon(L(0.34f, -0.3f), L(0.42f, 0.3f));
            Neon(L(-0.42f, 0.3f), L(0.42f, 0.3f));
            for (int i = 0; i < 8; i++)
            {
                float a0 = Mathf.PI * i / 8f, a1 = Mathf.PI * (i + 1) / 8f;
                Neon(L(0.42f + 0.18f * Mathf.Sin(a0), 0.1f - 0.18f * Mathf.Cos(a0) + 0.0f), L(0.42f + 0.18f * Mathf.Sin(a1), 0.1f - 0.18f * Mathf.Cos(a1)));
            }
            for (int i = 0; i < 9; i++)
            {
                float a = Mathf.PI * i / 8f;
                var d = new Vector3(-Mathf.Cos(a), Mathf.Sin(a), 0f);
                Neon(c + q * (new Vector3(0, 0.55f, 0) + d * 0.5f), c + q * (new Vector3(0, 0.55f, 0) + d * (i % 2 == 0 ? 0.85f : 0.72f)));
            }
            vb.PointLight(c + new Vector3(-0.6f, 0.3f, 0f), "glowamber", 0.9f, 4.5f);
            big.Dispose();
            // Shelves with books and plants, framed prints.
            foreach (float y in new[] { 2.1f, 2.7f })
            {
                vb.Box(new Vector3(x - 0.17f, y, 6.7f), new Vector3(0.32f, 0.05f, 1.4f), "oak");
                for (int i = 0; i < 4; i++)
                {
                    var p = new Vector3(x - 0.18f, y + 0.025f, 6.15f + i * 0.36f);
                    if (i % 2 == 0) vb.ModelAt("plant_small", p, -90f, 1.4f);
                    else for (int b = 0; b < 4; b++) vb.Box(p + new Vector3(0, 0.12f, -0.1f + b * 0.06f), new Vector3(0.18f, 0.24f - (b % 2) * 0.04f, 0.05f), new[] { "barnred", "petrol", "mustard", "cream" }[b], -90f);
                }
            }
            vb.Sign("cf_art_1", new Vector3(x - 0.05f, 1.75f, -3.55f), 0.7f, -90f, 0.88f, frame: "walnut", lit: true);
            vb.Sign("cf_art_2", new Vector3(x - 0.05f, 1.75f, -0.85f), 0.7f, -90f, 0.88f, frame: "walnut", lit: true);
        }

        private static void Outside(VB vb)
        {
            // The town outside the windows (backdrop: not lightmapped): a street, trees, lamps and the painted town.
            vb.Backdrop = true;
            vb.Box(new Vector3(-20f, -0.06f, 0f), new Vector3(16f, 0.1f, 40f), "sand");
            vb.Box(new Vector3(0f, -0.06f, 18f), new Vector3(48f, 0.1f, 16f), "sand");
            vb.Box(new Vector3(20f, -0.06f, -2f), new Vector3(16f, 0.1f, 36f), "sand");
            foreach (float z in new[] { -6.5f, 0.5f, 7.5f })
            {
                vb.Model("map_tree_small", -15.5f, z, 0f, scale: 1.2f);
                vb.Cylinder(new Vector3(-15.5f, 0f, z + 3.2f), 0.06f, 3.4f, "iron", 6);
                vb.Box(new Vector3(-15.5f, 3.5f, z + 3.2f), new Vector3(0.26f, 0.36f, 0.26f), "glowamber", glow: true);
            }
            foreach (float x in new[] { 3.5f, 9.5f }) vb.Model("map_tree_small", x, 11.9f, 0f, scale: 1.1f);
            // The painted town stands just across the street so the gameplay camera (looking down ~31°) sees its shopfronts
            // through the windows; backdrop renderers cast no shadows into the bake, so the sun still streams in.
            vb.Sign("cf_town", new Vector3(-17.5f, -0.9f, 0f), 30f, 90f, 15f);
            vb.Sign("cf_town", new Vector3(2f, -0.9f, 13.4f), 40f, 180f, 15f);
            vb.Sign("cf_town", new Vector3(17.5f, -0.9f, -1.5f), 30f, -90f, 15f); // seen past the brick wall from the desks
            foreach (float z in new[] { -7f, 1f }) vb.Model("map_tree_small", 15.2f, z, 0f, scale: 1.1f);
            vb.Backdrop = false;
        }

        // ------------------------------------------------------------------ the coffee bar

        private static void Bar(VB vb)
        {
            float cx = (BarX0 + BarX1) * .5f, len = BarX1 - BarX0;
            // Counter: walnut body, green tiled front, oak top, brass foot rail.
            vb.Box(new Vector3(cx, 0.52f, BarZ), new Vector3(len, 1.04f, 0.7f), "walnut", solid: true);
            vb.Box(new Vector3(cx, 1.07f, BarZ - 0.05f), new Vector3(len + 0.12f, 0.06f, 0.86f), "oak");
            for (float x = BarX0; x < BarX1 - 0.2f; x += 0.98f)
                vb.Sign("cf_tiles", new Vector3(x + 0.49f, 0.16f, BarZ - 0.36f), 0.98f, 180f, 0.82f, lit: true);
            vb.Box(new Vector3(cx, 0.08f, BarZ - 0.37f), new Vector3(len, 0.16f, 0.04f), "darkwood");
            vb.Box(new Vector3(cx, 0.98f, BarZ - 0.37f), new Vector3(len, 0.06f, 0.05f), "oak");
            vb.Pipe(new Vector3(BarX0, 0.25f, BarZ - 0.62f), new Vector3(BarX1, 0.25f, BarZ - 0.62f), 0.025f, "brass", 8);
            // West return to the window sill so the barista area is closed on that side.
            vb.Box(new Vector3(BarX0 - 0.45f, 0.52f, BarZ + 0.6f), new Vector3(0.7f, 1.04f, 1.9f), "walnut", solid: true);
            vb.Box(new Vector3(BarX0 - 0.45f, 1.07f, BarZ + 0.6f), new Vector3(0.82f, 0.06f, 2.0f), "oak");
            float top = 1.1f;
            // Pastry case on the west end.
            var pc = new Vector3(-9.6f, top, BarZ - 0.05f);
            vb.Box(pc + new Vector3(0, 0.02f, 0), new Vector3(1.5f, 0.04f, 0.6f), "darkwood");
            vb.Sign("cf_pastry", pc + new Vector3(0, 0.04f, -0.3f), 1.42f, 180f, 0.5f);
            vb.Box(pc + new Vector3(0, 0.56f, 0), new Vector3(1.5f, 0.04f, 0.6f), "darkwood");
            foreach (float sx in new[] { -0.73f, 0.73f })
                vb.Box(pc + new Vector3(sx, 0.29f, 0), new Vector3(0.04f, 0.5f, 0.6f), "darkwood");
            for (int i = 0; i < 6; i++) vb.ModelAt("croissant", pc + new Vector3(-0.5f + i * 0.2f, 0.05f, 0.1f), i * 30f, 1.4f);
            // Till, cups, a small plant, napkins.
            vb.ModelAt("cash_register", new Vector3(-4.3f, top, BarZ + 0.05f), 180f, 1.0f);
            for (int i = 0; i < 3; i++) vb.ModelAt("cup_coffee", new Vector3(-6.4f + i * 0.22f, top, BarZ - 0.15f), i * 40f, 1.2f);
            vb.ModelAt("plant_small", new Vector3(-7.9f, top, BarZ + 0.1f), 0f, 1.6f);
            vb.Box(new Vector3(-5.3f, top + 0.06f, BarZ + 0.1f), new Vector3(0.14f, 0.12f, 0.1f), "steel");
            // Bar stools (decor, collided).
            foreach (float x in new[] { -9.4f, -7.9f, -6.4f, -4.9f })
            {
                vb.ModelAt("bar_stool", new Vector3(x, 0f, BarZ - 0.95f), 0f, 1.15f);
                vb.Cylinder(new Vector3(x, 0.86f, BarZ - 0.95f), 0.2f, 0.06f, "sage", 14);
            }
            // Back counter: espresso machine (green), grinders, filter machine, cup stacks.
            float bcx = -6.4f, bcl = 10.6f;
            vb.Box(new Vector3(bcx, 0.47f, BackCounterZ), new Vector3(bcl, 0.94f, 0.7f), "walnut", solid: true);
            vb.Box(new Vector3(bcx, 0.97f, BackCounterZ), new Vector3(bcl + 0.06f, 0.06f, 0.76f), "oak");
            var em = new Vector3(-6.6f, 1.0f, BackCounterZ);
            vb.Box(em + new Vector3(0, 0.3f, 0), new Vector3(1.1f, 0.6f, 0.55f), "forest");
            vb.Box(em + new Vector3(0, 0.62f, 0), new Vector3(1.15f, 0.05f, 0.58f), "chrome");
            vb.Box(em + new Vector3(0, 0.12f, -0.29f), new Vector3(0.9f, 0.04f, 0.04f), "chrome");
            foreach (float sx in new[] { -0.3f, 0.3f })
            {
                vb.Cylinder(em + new Vector3(sx, 0.18f, -0.32f), 0.05f, 0.1f, "chrome", 8);
                vb.Pipe(em + new Vector3(sx, 0.2f, -0.3f), em + new Vector3(sx, 0.12f, -0.42f), 0.02f, "black", 5);
            }
            for (int i = 0; i < 6; i++) vb.ModelAt("mug", em + new Vector3(-0.45f + i * 0.18f, 0.65f, 0.08f), 180f, 1.2f);
            foreach (float x in new[] { -5.6f, -5.15f })
            {
                vb.Box(new Vector3(x, 1.2f, BackCounterZ), new Vector3(0.24f, 0.4f, 0.26f), "black");
                vb.Cylinder(new Vector3(x, 1.4f, BackCounterZ), 0.11f, 0.24f, "steel", 10, topRadius: 0.14f);
            }
            vb.ModelAt("coffee_machine", new Vector3(-8.0f, 1.0f, BackCounterZ), 180f, 1.2f);
            for (int s = 0; s < 3; s++)
                for (int k = 0; k < 4; k++)
                    vb.Cylinder(new Vector3(-9.2f + s * 0.25f, 1.0f + k * 0.09f, BackCounterZ - 0.1f), 0.06f, 0.08f, "petalwhite", 8, topRadius: 0.07f);
            for (int i = 0; i < 5; i++) vb.Cylinder(new Vector3(-3.4f + i * 0.3f, 1.0f, BackCounterZ), 0.09f, 0.26f, "glowwhite", 10, topRadius: 0.08f);
            // Pendants over the bar.
            foreach (float x in new[] { -9.4f, -7.0f, -4.6f }) Pendant(vb, new Vector3(x, 2.65f, BarZ), "awninggreen");
        }

        /// <summary>Pendant light: cord rising out of frame, brass cap, enamel shade, warm bulb (baked point light).</summary>
        private static void Pendant(VB vb, Vector3 bulb, string shade)
        {
            vb.Pipe(bulb + Vector3.up * 0.3f, new Vector3(bulb.x, WallH + 0.5f, bulb.z), 0.008f, "black", 4);
            vb.Cylinder(bulb + Vector3.up * 0.22f, 0.04f, 0.1f, "brass", 8);
            vb.Cylinder(bulb, 0.24f, 0.24f, shade, 16, topRadius: 0.05f, caps: false);
            vb.Sphere(bulb + Vector3.up * 0.02f, 0.075f, "glowamber", glow: true, rings: 4, segments: 8);
            vb.PointLight(bulb - Vector3.up * 0.2f, "glowamber", 0.65f, 4.2f);
        }

        // ------------------------------------------------------------------ seating

        private static void Booths(VB vb)
        {
            foreach (float cx in new[] { 3.3f, 7.9f })
            {
                float bz = NorthZ - 0.55f;
                vb.Box(new Vector3(cx, 0.22f, bz), new Vector3(2.4f, 0.44f, 0.6f), "walnut");
                vb.Box(new Vector3(cx, 0.5f, bz), new Vector3(2.36f, 0.12f, 0.56f), "sage");
                vb.Box(new Vector3(cx, 0.8f, bz + 0.25f), new Vector3(2.36f, 0.6f, 0.14f), "sage", 0f);
                vb.Box(new Vector3(cx, 1.12f, bz + 0.27f), new Vector3(2.4f, 0.05f, 0.18f), "walnut");
                vb.RegisterProp("booth_bench", new Vector3(cx, 0.55f, bz), new Vector3(2.4f, 1.1f, 0.62f));
                vb.Box(new Vector3(cx - 0.7f, 0.7f, bz + 0.1f), new Vector3(0.4f, 0.36f, 0.14f), cx < 5f ? "petrol" : "ctorange", 8f);
                vb.Box(new Vector3(cx + 0.7f, 0.7f, bz + 0.1f), new Vector3(0.4f, 0.36f, 0.14f), "cream", -6f);
                var t = new Vector3(cx, 0f, bz - 0.95f);
                vb.Box(t + Vector3.up * 0.37f, new Vector3(0.08f, 0.74f, 0.08f), "black");
                vb.Box(t + Vector3.up * 0.02f, new Vector3(0.6f, 0.04f, 0.5f), "black");
                vb.Box(t + Vector3.up * 0.76f, new Vector3(1.3f, 0.05f, 0.7f), "walnut");
                vb.RegisterProp("booth_table", t + Vector3.up * 0.39f, new Vector3(1.3f, 0.78f, 0.7f));
                vb.ModelAt("cup_coffee", t + new Vector3(-0.3f, 0.785f, 0.05f), 0f, 1.2f);
                vb.ModelAt("plant_small", t + new Vector3(0.25f, 0.785f, 0.1f), 0f, 1.3f);
                foreach (float dx in new[] { -0.55f, 0.55f })
                    vb.ModelAt("cafe_chair_a", t + new Vector3(dx, 0f, -0.75f), 0f, 1f, recolor: "sage");
                Pendant(vb, t + Vector3.up * 2.3f, "brass");
            }
        }

        private static void CommunalTable(VB vb)
        {
            var c = new Vector3(0f, 0f, -2.6f);
            vb.FloorSign("cf_rug_1", c.x, c.z, 7.2f, 0f, 3.6f, 0f, lit: true);
            vb.Box(c + Vector3.up * 0.74f, new Vector3(5.2f, 0.06f, 1.3f), "oak");
            vb.Box(c + Vector3.up * 0.69f, new Vector3(5.0f, 0.06f, 1.1f), "walnut");
            foreach (float sx in new[] { -2.3f, 2.3f })
                foreach (float sz in new[] { -0.5f, 0.5f })
                    vb.Box(c + new Vector3(sx, 0.35f, sz), new Vector3(0.12f, 0.7f, 0.12f), "walnut");
            vb.Box(c + new Vector3(0, 0.2f, 0), new Vector3(4.6f, 0.06f, 0.08f), "walnut");
            vb.RegisterProp("communal_table", c + Vector3.up * 0.39f, new Vector3(5.2f, 0.78f, 1.3f));
            float top = 0.77f;
            // Lamp, plant, books and mugs down the middle.
            vb.Cylinder(c + new Vector3(-0.6f, top, 0.05f), 0.1f, 0.02f, "brass", 12);
            vb.Pipe(c + new Vector3(-0.6f, top, 0.05f), c + new Vector3(-0.55f, top + 0.42f, 0.05f), 0.012f, "brass", 5);
            vb.Cylinder(c + new Vector3(-0.55f, top + 0.36f, 0.05f), 0.11f, 0.12f, "glowgold", 12, glow: true, topRadius: 0.05f);
            vb.PointLight(c + new Vector3(-0.55f, top + 0.6f, 0.05f), "glowamber", 0.4f, 2.5f);
            vb.Cylinder(c + new Vector3(0.35f, top, 0f), 0.13f, 0.2f, "terracotta", 12, topRadius: 0.15f);
            vb.ModelAt("houseplant_bush", c + new Vector3(0.35f, top + 0.18f, 0f), 0f, 0.38f);
            for (int b = 0; b < 3; b++) vb.Box(c + new Vector3(1.0f, top + 0.02f + b * 0.04f, 0.1f), new Vector3(0.26f, 0.04f, 0.2f), new[] { "barnred", "petrol", "mustard" }[b], b * 9f);
            // Terminal seats (laptops) and two end chairs.
            int k = 0;
            foreach (var s in vb.SpotsWhere(s => s.id == "terminal_1" || s.id == "terminal_2" || s.id == "terminal_3" || s.id == "terminal_4"))
            {
                var p = VB.P(s);
                var f = Fwd(s);
                vb.ModelAt("cafe_chair_a", p - f * 0.05f, VB.Deg(s), 1f, recolor: k % 2 == 0 ? "sage" : "denim");
                vb.SeatCollider(s, new Vector3(0, 0.5f, -0.05f), new Vector3(0.55f, 0.5f, 0.55f));
                vb.Approach(s, p + Right(s) * 1.35f - f * 0.1f);
                Laptop(vb, p + f * 0.55f + Vector3.up * top, VB.Deg(s), "cf_candles_" + (1 + k % 3));
                vb.ModelAt("mug", p + f * 0.45f + Right(s) * 0.32f + Vector3.up * top, 0f, 1.3f);
                k++;
            }
            foreach (float sx in new[] { -1f, 1f })
                vb.ModelAt("cafe_chair_a", c + new Vector3(sx * 3.05f, 0f, 0f), sx < 0 ? 90f : -90f, 1f, recolor: "sage");
            Pendant(vb, c + new Vector3(-1.4f, 2.4f, 0f), "awninggreen");
            Pendant(vb, c + new Vector3(1.4f, 2.4f, 0f), "awninggreen");
        }

        /// <summary>Open laptop facing the seat (screen art from the atlas).</summary>
        private static void Laptop(VB vb, Vector3 p, float seatYaw, string screen)
        {
            var q = Quaternion.Euler(0, seatYaw, 0);
            vb.Box(p + Vector3.up * 0.01f, new Vector3(0.36f, 0.02f, 0.25f), "darkgrey", seatYaw);
            var hinge = p + q * new Vector3(0, 0.02f, 0.12f);
            vb.BoxRot(hinge + q * new Vector3(0, 0.12f, 0.03f), new Vector3(0.36f, 0.24f, 0.015f), q * Quaternion.Euler(-12f, 0, 0), "darkgrey");
            var m = Matrix4x4.TRS(hinge + q * new Vector3(0, 0.015f, 0.018f), q * Quaternion.Euler(-12f, 0, 0), Vector3.one);
            vb.SignAt(screen, m, 0.32f, 0.2f, lit: false);
        }

        private static void RoundTables(VB vb)
        {
            foreach (var (c, ids) in new[] { (new Vector3(1.2f, 0f, 3.4f), new[] { "table_1", "table_2" }), (new Vector3(6.2f, 0f, 3.4f), new[] { "table_3", "table_4" }) })
            {
                vb.FloorSign("cf_rug_2", c.x, c.z - 0.1f, 3.1f, 0f, 3.1f, 0f, lit: true);
                vb.ModelAt("cafe_table_round", c, 0f, 1.05f, recolor: "walnut");
                vb.ModelAt("cup_coffee", c + new Vector3(-0.15f, 0.8f, 0.05f), 0f, 1.2f);
                vb.ModelAt("plant_small", c + new Vector3(0.12f, 0.8f, 0.1f), 0f, 1.4f);
                vb.ModelAt("croissant", c + new Vector3(0.05f, 0.8f, -0.18f), 30f, 1.4f);
                foreach (var id in ids)
                {
                    var s = vb.Spot(id);
                    var p = VB.P(s);
                    vb.ModelAt("cafe_chair_b", p - Fwd(s) * 0.05f, VB.Deg(s), 1f, recolor: "sage");
                    vb.SeatCollider(s, new Vector3(0, 0.5f, -0.05f), new Vector3(0.55f, 0.5f, 0.55f));
                    vb.Approach(s, p + Right(s) * 0.95f - Fwd(s) * 0.3f);
                }
                // Two more chairs on the far side (decor).
                foreach (float a in new[] { -45f, 45f })
                {
                    var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                    vb.ModelAt("cafe_chair_b", c + dir * 0.82f, a + 180f, 1f, recolor: "sage");
                }
                Pendant(vb, c + Vector3.up * 2.45f, "brass");
            }
        }

        private static void TradingDesks(VB vb)
        {
            int k = 0;
            foreach (var s in vb.SpotsWhere(s => s.id == "terminal_5" || s.id == "terminal_6" || s.id == "terminal_7" || s.id == "terminal_8"))
            {
                var p = VB.P(s);
                var f = Fwd(s);
                var desk = p + f * 0.85f;
                vb.ModelAt("home_desk", desk, VB.Deg(s) + 180f, 1f, recolor: "walnut");
                vb.RegisterProp("trading_desk", desk + Vector3.up * 0.38f, new Vector3(1.5f, 0.76f, 0.7f), VB.Deg(s));
                vb.ModelAt("office_chair", p - f * 0.05f, VB.Deg(s), 1f, recolor: "charcoal");
                vb.SeatCollider(s, new Vector3(0, 0.75f, -0.05f), new Vector3(0.6f, 0.5f, 0.6f));
                vb.Approach(s, p + Right(s) * 1.35f - f * 0.1f);
                // Twin monitors with candle charts, keyboard, mug, a small plant.
                foreach (float side in new[] { -1f, 1f })
                {
                    var mpos = desk + f * 0.15f + Right(s) * side * 0.36f + Vector3.up * 0.76f;
                    float yaw = VB.Deg(s) - side * 12f;
                    var q = Quaternion.Euler(0, yaw, 0);
                    vb.Box(mpos + Vector3.up * 0.01f, new Vector3(0.22f, 0.02f, 0.16f), "black", yaw);
                    vb.Box(mpos + Vector3.up * 0.14f, new Vector3(0.05f, 0.26f, 0.04f), "black", yaw);
                    vb.Box(mpos + q * new Vector3(0, 0.42f, -0.02f), new Vector3(0.66f, 0.42f, 0.04f), "black", yaw);
                    var m = Matrix4x4.TRS(mpos + q * new Vector3(0, 0.23f, -0.045f), q, Vector3.one);
                    vb.SignAt("cf_candles_" + (1 + (k + (side > 0 ? 1 : 0)) % 4), m, 0.6f, 0.38f, lit: false);
                }
                vb.Box(desk - f * 0.15f + Vector3.up * 0.78f, new Vector3(0.44f, 0.02f, 0.14f), "darkgrey", VB.Deg(s));
                vb.ModelAt("mug", desk - f * 0.1f + Right(s) * 0.45f + Vector3.up * 0.76f, 0f, 1.3f);
                if (k % 2 == 0) vb.ModelAt("plant_small", desk + f * 0.2f - Right(s) * 0.62f + Vector3.up * 0.76f, 0f, 1.5f);
                k++;
            }
        }

        private static void Lounge(VB vb)
        {
            // Sage sofa against the window sill facing the room, round coffee table, pouf, rug.
            vb.FloorSign("cf_rug_3", -9.4f, -5.6f, 4.2f, 90f, 3.2f, 0f, lit: true);
            vb.Model("home_couch_pillows", -11.15f, -5.6f, 90f, scale: 1.1f, recolor: "sage");
            vb.Box(new Vector3(-10.85f, 0.75f, -6.25f), new Vector3(0.14f, 0.4f, 0.42f), "cream", 8f);
            vb.Blob(new Vector3(-10.85f, 0.72f, -4.95f), new Vector3(0.16f, 0.38f, 0.38f), "ctorange");
            var t = new Vector3(-9.4f, 0f, -5.6f);
            vb.Cylinder(t, 0.42f, 0.42f, "walnut", 18);
            vb.Cylinder(t + Vector3.up * 0.42f, 0.5f, 0.05f, "oak", 20);
            vb.RegisterProp("coffee_table", t + Vector3.up * 0.23f, new Vector3(1.0f, 0.47f, 1.0f));
            vb.ModelAt("cup_coffee", t + new Vector3(-0.15f, 0.47f, 0.1f), 0f, 1.2f);
            vb.ModelAt("plant_small", t + new Vector3(0.12f, 0.47f, -0.1f), 0f, 1.4f);
            for (int b = 0; b < 2; b++) vb.Box(t + new Vector3(0.1f, 0.48f + b * 0.04f, 0.2f), new Vector3(0.24f, 0.04f, 0.18f), b == 0 ? "petrol" : "cream", 12f);
            vb.Model("home_lamp_standing", -11.3f, -7.35f, 0f);
            vb.Solid(-11.3f, -7.35f, 0.5f, 0.5f, 1.6f);
            vb.PointLight(new Vector3(-11.1f, 1.7f, -7.2f), "glowamber", 0.45f, 3f);
            var pouf = new Vector3(-8.0f, 0f, -4.2f);
            vb.Cylinder(pouf, 0.32f, 0.42f, "walnut", 14);
            vb.Cylinder(pouf + Vector3.up * 0.42f, 0.3f, 0.04f, "leather", 14);
            vb.RegisterProp("pouf", pouf + Vector3.up * 0.22f, new Vector3(0.66f, 0.46f, 0.66f));
            // Window seat with a yellow cushion (the astronaut cat naps on it: Eggs).
            vb.Box(new Vector3(-11.55f, 0.24f, 0.4f), new Vector3(0.75f, 0.48f, 4.0f), "walnut");
            vb.Box(new Vector3(-11.55f, 0.54f, 0.4f), new Vector3(0.7f, 0.12f, 3.9f), "mustard");
            vb.Box(new Vector3(-11.85f, 0.75f, 0.4f), new Vector3(0.12f, 0.36f, 3.8f), "cream");
            vb.RegisterProp("window_seat", new Vector3(-11.55f, 0.3f, 0.4f), new Vector3(0.75f, 0.6f, 4.0f));
            vb.Box(new Vector3(-11.6f, 0.78f, -1.1f), new Vector3(0.14f, 0.36f, 0.42f), "petrol", 10f);
        }

        private static void Armchairs(VB vb)
        {
            vb.FloorSign("cf_rug_2", 8.7f, -7.2f, 3.0f, 0f, 3.0f, 0f, lit: true);
            vb.Model("home_armchair_pillows", 7.7f, -7.7f, 30f, recolor: "sage");
            vb.Model("home_armchair_pillows", 9.8f, -7.7f, -30f, recolor: "sage");
            vb.Box(new Vector3(7.85f, 0.72f, -7.95f), new Vector3(0.42f, 0.38f, 0.14f), "cream", 30f);
            vb.Box(new Vector3(9.65f, 0.72f, -7.95f), new Vector3(0.42f, 0.38f, 0.14f), "cream", -30f);
            var t = new Vector3(8.75f, 0f, -6.6f);
            vb.Cylinder(t, 0.36f, 0.5f, "walnut", 16);
            vb.Cylinder(t + Vector3.up * 0.5f, 0.42f, 0.05f, "oak", 18);
            vb.RegisterProp("side_table", t + Vector3.up * 0.27f, new Vector3(0.84f, 0.55f, 0.84f));
            vb.ModelAt("mug", t + new Vector3(0.12f, 0.55f, 0.05f), 0f, 1.3f);
            for (int b = 0; b < 2; b++) vb.Box(t + new Vector3(-0.1f, 0.56f + b * 0.04f, -0.05f), new Vector3(0.24f, 0.04f, 0.18f), b == 0 ? "barnred" : "cream", 20f);
            vb.ModelAt("plant_small", t + new Vector3(-0.1f, 0.64f, -0.05f), 0f, 1.3f);
        }

        private static void FrontPlanters(VB vb, Rect inn)
        {
            float z = inn.yMin + 0.4f;
            foreach (float sx in new[] { -1f, 1f })
                foreach (var (x0, x1) in new[] { (2.7f, 6.6f), (7.4f, 11.6f) })
                {
                    float cx = sx * (x0 + x1) * .5f, len = x1 - x0;
                    vb.Box(new Vector3(cx, 0.3f, z), new Vector3(len, 0.6f, 0.55f), "walnut");
                    vb.Box(new Vector3(cx, 0.6f, z), new Vector3(len + 0.06f, 0.04f, 0.6f), "oak");
                    vb.RegisterProp("planter_box", new Vector3(cx, 0.3f, z), new Vector3(len, 0.6f, 0.55f));
                    int n = Mathf.RoundToInt(len / 0.75f);
                    for (int i = 0; i < n; i++)
                    {
                        float x = cx - len * .5f + (i + 0.5f) * len / n;
                        string id = i % 3 == 1 ? "home_houseplant_1" : i % 3 == 2 ? "home_houseplant_5" : "houseplant_bush";
                        vb.Model(id, x, z + (i % 2 == 0 ? 0.05f : -0.05f), 37f * i, y: 0.6f, scale: 0.6f);
                    }
                }
            // A-frame chalk board outside the door (sun over a cup).
            var b = new Vector3(-3.4f, 0f, inn.yMin - 1.1f);
            vb.BoxRot(b + new Vector3(0, 0.55f, -0.14f), new Vector3(0.62f, 1.1f, 0.04f), Quaternion.Euler(14f, 0, 0), "walnut");
            vb.BoxRot(b + new Vector3(0, 0.55f, 0.14f), new Vector3(0.62f, 1.1f, 0.04f), Quaternion.Euler(-14f, 0, 0), "walnut");
            var m = Matrix4x4.TRS(b + new Vector3(0, 0.12f, -0.29f), Quaternion.Euler(14f, 0, 0), Vector3.one);
            vb.SignAt("cf_chalk_sun", m, 0.52f, 0.7f, lit: true);
        }

        private static void Plants(VB vb)
        {
            foreach (var (x, z, id, sc) in new[]
                     {
                         (11.3f, 8.5f, "houseplant_bush", 1.1f), (11.3f, -8.4f, "houseplant_bush", 1.0f), (-11.3f, -8.4f, "home_houseplant_1", 1.0f),
                         (-2.6f, 4.4f, "houseplant_bush", 0.9f), (5.4f, 8.35f, "home_houseplant_1", 0.8f), (11.35f, 5.1f, "home_houseplant_5", 1.2f),
                         (-11.4f, 3.0f, "home_houseplant_5", 0.9f), (-11.4f, -2.2f, "home_houseplant_5", 0.8f), (6.3f, -8.5f, "houseplant_bush", 0.8f),
                         (11.35f, -6.3f, "home_houseplant_1", 0.9f), (-1.2f, 8.5f, "houseplant_bush", 0.8f),
                     })
                vb.Plant(x, z, id, sc);
            // Big leafy plants in pots (the concept's monsteras).
            foreach (var (x, z, sc) in new[] { (-3.4f, -7.5f, 1.1f), (3.4f, -7.5f, 1.0f), (-6.4f, -7.6f, 1.0f), (11.75f, 1.85f, 0.95f), (-11.35f, -3.4f, 0.9f), (9.6f, 8.35f, 0.9f) })
                BigPlant(vb, x, z, sc);
        }

        /// <summary>A leafy floor plant in a terracotta pot, solid.</summary>
        private static void BigPlant(VB vb, float x, float z, float s)
        {
            vb.Cylinder(new Vector3(x, 0f, z), 0.24f * s, 0.42f * s, "terracotta", 14, topRadius: 0.3f * s);
            vb.Cylinder(new Vector3(x, 0.4f * s, z), 0.27f * s, 0.03f, "soil", 12);
            vb.Model("plant_big_leaf", x, z, (x * 37f + z * 11f) % 360f, y: 0.38f * s, scale: 1.55f * s);
            vb.Solid(x, z, 0.62f * s, 0.62f * s, 1.6f * s);
        }

        // ------------------------------------------------------------------ eggs

        private static void Eggs(VB vb)
        {
            // Astronaut cat napping on the window-seat cushion.
            var seat = new Vector3(-11.45f, 0.6f, 1.2f);
            using (vb.Scaled(seat, 2.0f)) VenueEggs.JupiterCat(vb, seat, 90f);
            // Pink-beanie dog under the communal table, peeking out towards the room.
            var dog = new Vector3(-0.05f, 0f, -3.15f);
            using (vb.Scaled(dog, 1.25f)) VenueEggs.DogWithBeanie(vb, dog, 175f);
            // Three-lens phone on the bar.
            VenueEggs.SeekerPhone(vb, new Vector3(-7.25f, 1.112f, BarZ - 0.2f), 160f, 1.4f);
            // Purple ghost plush on the middle shelf behind the bar.
            var shelf = new Vector3(-9.48f, 2.825f, NorthZ - 0.3f);
            using (vb.Scaled(shelf, 0.42f)) VenueEggs.PhantomGhost(vb, shelf - new Vector3(0.32f, 0.8f, 0.3f));
            // O-mouth cat by the till.
            VenueEggs.Popcat(vb, new Vector3(-3.75f, 1.26f, BarZ + 0.05f), 180f, 0.75f, doughnutOnString: false);
        }
    }
}
