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
            vb.FoliageAtlas = true;
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
            // Pothos trailing from the window head (modelled vines, 1.08 m at scale 1).
            var rnd = new System.Random(7);
            for (float z = -8.5f; z <= 8.5f; z += 2.1f)
            {
                float sc = 0.6f + (float)rnd.NextDouble() * 0.55f;
                vb.ModelAt("pothos_vine", new Vector3(x + 0.3f, 4.0f - 1.09f * sc, z + (float)rnd.NextDouble() * 0.4f - 0.2f), (float)rnd.NextDouble() * 360f, sc);
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
                    if (i % 3 == 0) vb.ModelAt("mug_lite", new Vector3(x, y + 0.025f, z - 0.3f), 180f, 1.3f);
                    else if (i % 3 == 1) vb.ModelAt("cafe_books", new Vector3(x, y + 0.025f, z - 0.3f), 180f + i * 7f, 0.9f);
                    else vb.ModelAt("plant_small", new Vector3(x, y + 0.025f, z - 0.3f), 0f, 1.3f);
                }
            }
            foreach (int k in new[] { 0, 3 })
                vb.ModelAt("pothos_vine", new Vector3(-10.9f + k * 1.1f, 3.42f - 1.09f * (0.6f + (k % 2) * 0.3f), z - 0.4f), k * 90f, 0.6f + (k % 2) * 0.3f);
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
            foreach (float x in new[] { 1.3f, 5.6f, 10.6f }) vb.ModelAt("cafe_plant_fern", new Vector3(x, 1.145f, z - 0.05f), x * 40f, 0.75f);
            var rnd = new System.Random(19);
            for (float x = wx0 + 0.6f; x < wx1 - 0.4f; x += 2.08f)
            {
                float sc = 0.45f + (float)rnd.NextDouble() * 0.5f;
                vb.ModelAt("pothos_vine", new Vector3(x, 3.78f - 1.09f * sc, z - 0.22f), (float)rnd.NextDouble() * 360f, sc);
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
                    else vb.ModelAt("cafe_books", p, -90f + i * 5f, 1.1f);
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
            // Counter: walnut body, green tiled front, oak top with a rounded nosing, brass foot rail on brackets.
            vb.Box(new Vector3(cx, 0.52f, BarZ), new Vector3(len, 1.04f, 0.7f), "walnut", solid: true);
            vb.Box(new Vector3(cx, 1.07f, BarZ - 0.05f), new Vector3(len + 0.12f, 0.06f, 0.86f), "oak");
            vb.CylinderRot(new Vector3(BarX0 - 0.06f, 1.07f, BarZ - 0.48f), Quaternion.Euler(0, 0, -90f), 0.032f, len + 0.12f, "oak", 10);
            for (float x = BarX0; x < BarX1 - 0.2f; x += 0.98f)
                vb.Sign("cf_tiles", new Vector3(x + 0.49f, 0.16f, BarZ - 0.36f), 0.98f, 180f, 0.82f, lit: true);
            vb.Box(new Vector3(cx, 0.08f, BarZ - 0.37f), new Vector3(len, 0.16f, 0.04f), "darkwood");
            vb.Box(new Vector3(cx, 0.98f, BarZ - 0.37f), new Vector3(len, 0.06f, 0.05f), "oak");
            vb.Pipe(new Vector3(BarX0, 0.25f, BarZ - 0.62f), new Vector3(BarX1, 0.25f, BarZ - 0.62f), 0.025f, "brass", 10);
            for (float x = BarX0 + 0.4f; x < BarX1; x += 1.3f)
            {
                vb.Pipe(new Vector3(x, 0.25f, BarZ - 0.37f), new Vector3(x, 0.25f, BarZ - 0.6f), 0.014f, "brass", 6);
                vb.Cylinder(new Vector3(x, 0.24f, BarZ - 0.38f), 0.035f, 0.02f, "brass", 10);
            }
            // West return to the window sill so the barista area is closed on that side.
            vb.Box(new Vector3(BarX0 - 0.45f, 0.52f, BarZ + 0.6f), new Vector3(0.7f, 1.04f, 1.9f), "walnut", solid: true);
            vb.Box(new Vector3(BarX0 - 0.45f, 1.07f, BarZ + 0.6f), new Vector3(0.82f, 0.06f, 2.0f), "oak");
            float top = 1.1f;
            // Pastry case (modelled: walnut base, chrome frame, two shelves) with croissants, doughnuts and a cake.
            var pc = new Vector3(-9.6f, top, BarZ - 0.05f);
            vb.ModelAt("pastry_case", pc, 180f);
            for (int i = 0; i < 6; i++) vb.ModelAt("croissant", pc + new Vector3(-0.55f + i * 0.21f, 0.105f, 0.05f), i * 30f, 1.4f);
            for (int i = 0; i < 4; i++) vb.ModelAt("donut", pc + new Vector3(-0.6f + i * 0.2f, 0.355f, 0.05f), i * 40f, 1.5f);
            vb.ModelAt("cake", pc + new Vector3(0.38f, 0.355f, 0.03f), 20f, 1.1f);
            // Till, cups, a small plant, napkins.
            vb.ModelAt("cash_register", new Vector3(-4.3f, top, BarZ + 0.05f), 180f, 1.0f);
            for (int i = 0; i < 3; i++) vb.ModelAt("cup_coffee", new Vector3(-6.4f + i * 0.22f, top, BarZ - 0.15f), i * 40f, 1.2f);
            vb.ModelAt("plant_small", new Vector3(-7.9f, top, BarZ + 0.1f), 0f, 1.6f);
            vb.Box(new Vector3(-5.3f, top + 0.06f, BarZ + 0.1f), new Vector3(0.14f, 0.12f, 0.1f), "steel");
            // Bentwood bar stools with green cushions (decor, collided).
            foreach (float x in new[] { -9.4f, -7.9f, -6.4f, -4.9f })
                vb.ModelAt("cafe_bar_stool", new Vector3(x, 0f, BarZ - 0.95f), 0f);
            // Back counter (cabinet doors on its front), espresso machine, two grinders, drip brewer, mugs and to-go cups.
            float bcx = -6.4f, bcl = 10.6f;
            vb.Box(new Vector3(bcx, 0.47f, BackCounterZ), new Vector3(bcl, 0.94f, 0.7f), "walnut", solid: true);
            vb.Box(new Vector3(bcx, 0.97f, BackCounterZ), new Vector3(bcl + 0.06f, 0.06f, 0.76f), "oak");
            for (float x = bcx - bcl * .5f + 0.3f; x < bcx + bcl * .5f - 0.2f; x += 0.6f)
            {
                vb.Box(new Vector3(x, 0.47f, BackCounterZ - 0.355f), new Vector3(0.54f, 0.78f, 0.02f), "oak");
                vb.Box(new Vector3(x + 0.2f, 0.62f, BackCounterZ - 0.37f), new Vector3(0.02f, 0.12f, 0.02f), "brass");
            }
            vb.ModelAt("espresso_machine", new Vector3(-6.6f, 1.0f, BackCounterZ + 0.06f), 180f);
            vb.ModelAt("coffee_grinder", new Vector3(-5.55f, 1.0f, BackCounterZ + 0.05f), 180f);
            vb.ModelAt("coffee_grinder", new Vector3(-5.2f, 1.0f, BackCounterZ + 0.05f), 170f);
            vb.ModelAt("coffee_machine", new Vector3(-8.0f, 1.0f, BackCounterZ), 180f, 1.2f);
            for (int i = 0; i < 6; i++) vb.ModelAt("mug_lite", new Vector3(-9.3f + (i % 3) * 0.2f, 1.0f, BackCounterZ - 0.05f + (i / 3) * 0.16f), 180f + i * 20f, 1.3f);
            for (int i = 0; i < 5; i++) vb.ModelAt("cup_coffee", new Vector3(-3.4f + i * 0.28f, 1.0f, BackCounterZ), i * 25f, 1.5f);
            // Pendants over the bar.
            foreach (float x in new[] { -9.4f, -7.0f, -4.6f }) Pendant(vb, new Vector3(x, 2.65f, BarZ), "awninggreen");
        }

        /// <summary>Pendant light: cord rising to the ceiling line, modelled enamel dome (brass for "brass"), warm bulb, baked light.</summary>
        private static void Pendant(VB vb, Vector3 bulb, string shade)
        {
            vb.Pipe(bulb + Vector3.up * 0.3f, new Vector3(bulb.x, WallH + 0.5f, bulb.z), 0.008f, "black", 4);
            vb.ModelAt("pendant_dome", bulb - Vector3.up * 0.04f, 0f, 1f, recolor: shade == "awninggreen" ? null : shade);
            vb.Sphere(bulb, 0.07f, "glowamber", glow: true, rings: 4, segments: 8);
            vb.PointLight(bulb - Vector3.up * 0.2f, "glowamber", 0.65f, 4.2f);
        }

        // ------------------------------------------------------------------ seating

        private static void Booths(VB vb)
        {
            foreach (float cx in new[] { 3.3f, 7.9f })
            {
                float bz = NorthZ - 0.55f;
                // Modelled booth: walnut base and frame, two seat cushions, four channel-tufted back cushions.
                vb.ModelAt("booth_bench", new Vector3(cx, 0f, bz), 180f);
                vb.ModelAt("throw_pillow", new Vector3(cx - 0.75f, 0.5f, bz + 0.1f), 188f, 1f, recolor: cx < 5f ? "petrol" : "ctorange");
                vb.ModelAt("throw_pillow", new Vector3(cx + 0.72f, 0.5f, bz + 0.1f), 172f, 1f, recolor: "cream");
                var t = new Vector3(cx, 0f, bz - 0.95f);
                vb.ModelAt("cafe_table_booth", t, 0f);
                vb.ModelAt("cup_coffee", t + new Vector3(-0.3f, 0.765f, 0.05f), 0f, 1.2f);
                vb.ModelAt("plant_small", t + new Vector3(0.25f, 0.765f, 0.1f), 0f, 1.3f);
                foreach (float dx in new[] { -0.55f, 0.55f })
                    vb.ModelAt("cafe_chair_spindle", t + new Vector3(dx, 0f, -0.75f), 0f);
                Pendant(vb, t + Vector3.up * 2.3f, "brass");
            }
        }

        private static void CommunalTable(VB vb)
        {
            var c = new Vector3(0f, 0f, -2.6f);
            vb.FloorSign("cf_rug_1", c.x, c.z, 7.2f, 0f, 3.6f, 0f, lit: true);
            // Two modelled plank farmhouse tables on trestles pushed together (2 x 2.6 x 1.3 m, top 0.76 m).
            vb.ModelAt("cafe_table_farm", c + new Vector3(-1.3f, 0f, 0f), 0f);
            vb.ModelAt("cafe_table_farm", c + new Vector3(1.3f, 0f, 0f), 180f);
            float top = 0.762f;
            vb.ModelAt("cafe_desk_lamp", c + new Vector3(-0.6f, top, 0.05f), 90f, 1f);
            vb.PointLight(c + new Vector3(-0.55f, top + 0.5f, 0.05f), "glowamber", 0.4f, 2.5f);
            vb.ModelAt("cafe_plant_snake", c + new Vector3(0.35f, top, 0f), 0f, 0.5f);
            vb.ModelAt("cafe_books", c + new Vector3(1.0f, top, 0.1f), 20f, 1f);
            // Terminal seats: spindle chairs with green cushions, laptops kept fully on the table (fe.7 moved the anchors
            // 0.10 m towards it, so a laptop sits 0.63 m ahead of its anchor and its front edge 0.48 m).
            int k = 0;
            foreach (var s in vb.SpotsWhere(s => s.id == "terminal_1" || s.id == "terminal_2" || s.id == "terminal_3" || s.id == "terminal_4"))
            {
                var p = VB.P(s);
                var f = Fwd(s);
                vb.ModelAt("cafe_chair_spindle", p - f * 0.05f, VB.Deg(s));
                vb.SeatCollider(s, new Vector3(0, 0.5f, -0.05f), new Vector3(0.55f, 0.5f, 0.55f));
                vb.Approach(s, p + Right(s) * 1.35f - f * 0.1f);
                Laptop(vb, p + f * 0.63f + Vector3.up * top, VB.Deg(s), "cf_candles_" + (1 + k % 3));
                vb.ModelAt("mug_lite", p + f * 0.5f + Right(s) * 0.34f + Vector3.up * top, 0f, 1.3f);
                k++;
            }
            foreach (float sx in new[] { -1f, 1f })
                vb.ModelAt("cafe_chair_spindle", c + new Vector3(sx * 3.05f, 0f, 0f), sx < 0 ? 90f : -90f);
            Pendant(vb, c + new Vector3(-1.4f, 2.4f, 0f), "awninggreen");
            Pendant(vb, c + new Vector3(1.4f, 2.4f, 0f), "awninggreen");
        }

        /// <summary>Laptop model facing the sitter; a candle chart on its display (atlas sign laid on the leaning screen).</summary>
        private static void Laptop(VB vb, Vector3 p, float seatYaw, string screen)
        {
            var q = Quaternion.Euler(0, seatYaw, 0);                 // +Z points away from the sitter
            vb.ModelAt("cafe_laptop", p, seatYaw + 180f);
            var sq = q * Quaternion.Euler(24f, 0f, 0f);              // the display leans back 24 degrees
            var centre = p + q * new Vector3(0f, 0.113f, 0.096f) + sq * new Vector3(0f, 0f, -0.004f);
            const float w = 0.27f, h = 0.15f;
            vb.SignAt(screen, Matrix4x4.TRS(centre - sq * Vector3.up * (h * .5f), sq, Vector3.one), w, h, lit: false);
        }

        private static void RoundTables(VB vb)
        {
            foreach (var (c, ids) in new[] { (new Vector3(1.2f, 0f, 3.4f), new[] { "table_1", "table_2" }), (new Vector3(6.2f, 0f, 3.4f), new[] { "table_3", "table_4" }) })
            {
                vb.FloorSign("cf_rug_2", c.x, c.z - 0.1f, 3.1f, 0f, 3.1f, 0f, lit: true);
                // Modelled pedestal bistro table and bentwood chairs with green cushions.
                vb.ModelAt("cafe_table_bistro", c, 0f);
                vb.ModelAt("cup_coffee", c + new Vector3(-0.15f, 0.76f, 0.05f), 0f, 1.2f);
                vb.ModelAt("plant_small", c + new Vector3(0.12f, 0.76f, 0.1f), 0f, 1.4f);
                vb.ModelAt("croissant", c + new Vector3(0.05f, 0.76f, -0.18f), 30f, 1.4f);
                foreach (var id in ids)
                {
                    var s = vb.Spot(id);
                    var p = VB.P(s);
                    vb.ModelAt("cafe_chair_bistro", p - Fwd(s) * 0.05f, VB.Deg(s));
                    vb.SeatCollider(s, new Vector3(0, 0.5f, -0.05f), new Vector3(0.55f, 0.5f, 0.55f));
                    vb.Approach(s, p + Right(s) * 0.95f - Fwd(s) * 0.3f);
                }
                // Two more chairs on the far side (decor).
                foreach (float a in new[] { -45f, 45f })
                {
                    var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                    vb.ModelAt("cafe_chair_bistro", c + dir * 0.82f, a + 180f);
                }
                Pendant(vb, c + Vector3.up * 2.45f, "brass");
            }
        }

        private static void TradingDesks(VB vb)
        {
            // fe.7: the anchors moved 0.25 m towards the desks (x 10.75); the desks stay at x 11.35, so the keyboard sits
            // 0.45 m ahead of the anchor and the knees go under the desk.
            const float deskX = 11.35f;
            int k = 0;
            foreach (var s in vb.SpotsWhere(s => s.id == "terminal_5" || s.id == "terminal_6" || s.id == "terminal_7" || s.id == "terminal_8"))
            {
                var p = VB.P(s);
                var f = Fwd(s);
                var desk = new Vector3(deskX, 0f, p.z);
                vb.ModelAt("cafe_desk", desk, VB.Deg(s) + 180f, 1f, recolor: "walnut");
                vb.RegisterProp("trading_desk", desk + Vector3.up * 0.38f, new Vector3(1.5f, 0.76f, 0.7f), VB.Deg(s));
                vb.ModelAt("cafe_office_chair", p - f * 0.05f, VB.Deg(s), 1f, recolor: "charcoal");
                vb.SeatCollider(s, new Vector3(0, 0.75f, -0.05f), new Vector3(0.6f, 0.5f, 0.6f));
                vb.Approach(s, p + Right(s) * 1.35f - f * 0.1f);
                // Twin monitors with candle charts, keyboard, mug, a small plant.
                foreach (float side in new[] { -1f, 1f })
                    Monitor(vb, desk + f * 0.17f + Right(s) * side * 0.33f + Vector3.up * 0.76f, VB.Deg(s) - side * 14f,
                            "cf_candles_" + (1 + (k + (side > 0 ? 1 : 0)) % 4));
                vb.ModelAt("cafe_keyboard", desk - f * 0.15f + Vector3.up * 0.762f, VB.Deg(s) + 180f);
                vb.ModelAt("mug_lite", desk - f * 0.1f + Right(s) * 0.45f + Vector3.up * 0.76f, 0f, 1.3f);
                if (k % 2 == 0) vb.ModelAt("plant_small", desk + f * 0.2f - Right(s) * 0.62f + Vector3.up * 0.76f, 0f, 1.5f);
                k++;
            }
        }

        private static void Lounge(VB vb)
        {
            // Green velvet mid-century sofa against the window sill facing the room, pedestal coffee table, knitted pouf.
            vb.FloorSign("cf_rug_3", -9.4f, -5.6f, 4.2f, 90f, 3.2f, 0f, lit: true);
            vb.Model("cafe_sofa", -11.15f, -5.6f, 90f);
            vb.ModelAt("throw_pillow", new Vector3(-11.3f, 0.42f, -6.3f), 80f, 1f, recolor: "cream");
            vb.ModelAt("throw_pillow", new Vector3(-11.3f, 0.42f, -4.95f), 100f, 1f, recolor: "ctorange");
            vb.Model("home_lamp_standing", -11.3f, -7.35f, 0f);
            vb.Solid(-11.3f, -7.35f, 0.5f, 0.5f, 1.6f);
            vb.PointLight(new Vector3(-11.1f, 1.7f, -7.2f), "glowamber", 0.45f, 3f);
            var t = new Vector3(-9.4f, 0f, -5.6f);
            vb.ModelAt("cafe_coffee_table", t, 0f);
            vb.ModelAt("cup_coffee", t + new Vector3(-0.15f, 0.46f, 0.1f), 0f, 1.2f);
            vb.ModelAt("plant_small", t + new Vector3(0.12f, 0.46f, -0.1f), 0f, 1.4f);
            vb.ModelAt("cafe_books", t + new Vector3(0.12f, 0.46f, 0.22f), 70f, 0.9f);
            vb.ModelAt("pouf_round", new Vector3(-8.0f, 0f, -4.2f), 0f, 1f, recolor: "mustard");
            // Window seat with a yellow cushion (the astronaut cat naps on it: Eggs) and two throw pillows.
            vb.Box(new Vector3(-11.55f, 0.24f, 0.4f), new Vector3(0.75f, 0.48f, 4.0f), "walnut");
            vb.Box(new Vector3(-11.55f, 0.54f, 0.4f), new Vector3(0.7f, 0.12f, 3.9f), "mustard");
            vb.Box(new Vector3(-11.85f, 0.75f, 0.4f), new Vector3(0.12f, 0.36f, 3.8f), "cream");
            vb.RegisterProp("window_seat", new Vector3(-11.55f, 0.3f, 0.4f), new Vector3(0.75f, 0.6f, 4.0f));
            vb.ModelAt("throw_pillow", new Vector3(-11.72f, 0.58f, -1.1f), 100f, 1f, recolor: "petrol");
            vb.ModelAt("throw_pillow", new Vector3(-11.72f, 0.58f, 2.0f), 76f, 1f, recolor: "cream");
        }

        private static void Armchairs(VB vb)
        {
            vb.FloorSign("cf_rug_2", 8.7f, -7.2f, 3.0f, 0f, 3.0f, 0f, lit: true);
            vb.Model("cafe_armchair", 7.7f, -7.7f, 30f);
            vb.Model("cafe_armchair", 9.8f, -7.7f, -30f);
            vb.ModelAt("throw_pillow", new Vector3(7.8f, 0.45f, -7.95f), 30f, 0.9f, recolor: "cream");
            vb.ModelAt("throw_pillow", new Vector3(9.7f, 0.45f, -7.95f), -30f, 0.9f, recolor: "mustard");
            var t = new Vector3(8.75f, 0f, -6.6f);
            vb.ModelAt("cafe_side_table", t, 0f);
            vb.ModelAt("mug_lite", t + new Vector3(0.12f, 0.55f, 0.05f), 0f, 1.3f);
            vb.ModelAt("cafe_books", t + new Vector3(-0.12f, 0.55f, -0.05f), 20f, 0.8f);
            vb.ModelAt("plant_small", t + new Vector3(-0.05f, 0.55f, 0.15f), 0f, 1.3f);
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
                        string id = i % 3 == 1 ? "home_houseplant_1" : i % 3 == 2 ? "houseplant_fern_lite" : "houseplant_bush_lite";
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
                         (11.3f, 8.5f, "cafe_plant_bush", 1.1f), (11.3f, -8.4f, "cafe_plant_bush", 1.0f), (-11.3f, -8.4f, "cafe_plant_monstera", 1.0f),
                         (-2.6f, 4.4f, "cafe_plant_bush", 0.9f), (5.4f, 8.35f, "cafe_plant_monstera", 0.8f), (11.35f, 5.1f, "cafe_plant_fern", 1.3f),
                         (-11.4f, 3.0f, "cafe_plant_fern", 1.0f), (-11.4f, -2.2f, "cafe_plant_snake", 1.0f), (6.3f, -8.5f, "cafe_plant_bush", 0.8f),
                         (11.65f, -6.95f, "cafe_plant_monstera", 0.8f), (-1.2f, 8.5f, "cafe_plant_bush", 0.8f),
                     })
                vb.Plant(x, z, id, sc);
            // Big leafy plants in terracotta pots (the concept's monsteras).
            foreach (var (x, z, sc) in new[] { (-3.4f, -7.5f, 1.1f), (3.4f, -7.5f, 1.0f), (-6.4f, -7.6f, 1.0f), (11.75f, 1.85f, 0.95f), (-11.35f, -3.4f, 0.9f), (9.6f, 8.35f, 0.9f) })
                BigPlant(vb, x, z, sc);
        }

        /// <summary>A big leafy floor plant in a terracotta pot (modelled), solid.</summary>
        private static void BigPlant(VB vb, float x, float z, float s) => vb.Plant(x, z, "cafe_plant_leafy", s);

        /// <summary>Flat-panel monitor model facing the sitter, a candle chart on its panel.</summary>
        private static void Monitor(VB vb, Vector3 p, float yaw, string screen)
        {
            var q = Quaternion.Euler(0, yaw, 0);                     // +Z points away from the sitter
            vb.ModelAt("cafe_monitor", p, yaw + 180f);                // Kenney flat screen: panel centre 0.27 m, leans back 8 degrees
            const float w = 0.55f, h = 0.31f;
            var sq = q * Quaternion.Euler(8f, 0f, 0f);
            var centre = p + q * new Vector3(0f, 0.27f, 0f) + sq * new Vector3(0f, 0f, -0.006f);
            vb.SignAt(screen, Matrix4x4.TRS(centre - sq * Vector3.up * (h * .5f), sq, Vector3.one), w, h, lit: false);
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
