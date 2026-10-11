using CTLife.Core;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Terminal Café v3 (round 7), built to the in-game concept (art-direction/out/ingame-cafe-concept-v1.png): a sunny,
    /// cosy corner café that is also the trenches' coworking spot. Round 7 shrinks the room to 17 x 12 m (spots.json fe.8)
    /// so the bar, communal table, round tables, booths, lounge and trading wall sit close together and the gameplay frame
    /// is mostly furniture, people-space and decor (round 6 left about 60% of the frame bare floor).
    ///  * West: big street windows (the town outside, the sun streaming in), window seat with the astronaut cat, the green
    ///    velvet sofa lounge. North-west: the coffee bar with green tiles, a full pastry case, stools, espresso machine and
    ///    grinders on the back counter, cluttered shelves, the chalkboard menu (doodles only).
    ///  * Centre: the long communal laptop table (terminal_1..4); two round tables (table_1..4).
    ///  * North-east: green booths under the back windows. East: exposed brick, the sunrise-coffee neon, framed prints and
    ///    four twin-monitor trading desks (terminal_5..8). South-east: armchairs on a round rug.
    ///  * A rug under every zone, pendants, hanging planters from the ceiling line, pothos, plants everywhere, small
    ///    things on every table.
    /// All furniture is modelled (round 6: Quaternius / Kenney / KayKit CC0 and our own env_custom_r6 pieces).
    /// </summary>
    public static class VenuesCafe
    {
        private const float WallH = 4.6f, BarZ = 3.3f, BarX0 = -7.9f, BarX1 = -3.0f, BackCounterZ = 5.7f;
        private const float WinWestX = -8.7f, NorthZ = 6.2f, NorthWinX0 = -0.6f, DeskX = 8.0f;
        private static readonly Vector3 Communal = new Vector3(-1.4f, 0f, -1.6f);
        private static readonly Vector3 Round1 = new Vector3(1.7f, 0f, 1.0f), Round2 = new Vector3(4.9f, 0f, 1.0f);
        private static readonly float[] BoothX = { 1.6f, 5.4f };

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
            Sidewalk(vb, inn);
            Plants(vb);
            HangingPlanters(vb);
            Eggs(vb);
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 0.5f, 1.3f);
            // Fill: a little warm bounce, a cool sky fill from the back windows (kept low so the sun patches read).
            vb.PointLight(new Vector3(-3f, 3.2f, -1.5f), "glowamber", 0.3f, 9f);
            vb.PointLight(new Vector3(4f, 3.4f, 4.5f), "skyblue", 0.28f, 8f);
        }

        // ------------------------------------------------------------------ shell

        private static void WestWindows(VB vb)
        {
            const float x = WinWestX, len = 12.4f;
            vb.Box(new Vector3(x, 0.28f, 0f), new Vector3(0.3f, 0.56f, len), "walnut", collider: true);
            vb.Box(new Vector3(x + 0.08f, 0.58f, 0f), new Vector3(0.46f, 0.05f, len), "oak");
            vb.Box(new Vector3(x, 4.33f, 0f), new Vector3(0.3f, 0.54f, len), "cream");
            vb.Box(new Vector3(x + 0.12f, 4.05f, 0f), new Vector3(0.2f, 0.1f, len), "walnut");
            vb.Box(new Vector3(x + 0.05f, 3.15f, 0f), new Vector3(0.08f, 0.06f, len), "darkwood");
            for (float z = -6.1f; z <= 6.11f; z += 1.525f)
                vb.Box(new Vector3(x, 2.3f, z), new Vector3(0.16f, 3.5f, 0.16f), "darkwood", collider: true);
            for (float z = -5.34f; z <= 5.4f; z += 1.525f)
                vb.Box(new Vector3(x + 0.03f, 1.85f, z), new Vector3(0.04f, 2.6f, 0.04f), "darkwood");
            // Invisible wall in the openings (the windows are open so the baked sun streams in).
            vb.Collider(new Vector3(x, 2.3f, 0f), new Vector3(0.2f, 3.5f, len));
            // Pothos trailing from the window head (modelled vines, 1.09 m at scale 1).
            var rnd = new System.Random(7);
            for (float z = -5.6f; z <= 5.7f; z += 1.6f)
            {
                float sc = 0.6f + (float)rnd.NextDouble() * 0.55f;
                vb.ModelAt("pothos_vine", new Vector3(x + 0.3f, 4.0f - 1.09f * sc, z + (float)rnd.NextDouble() * 0.4f - 0.2f), (float)rnd.NextDouble() * 360f, sc);
            }
        }

        private static void NorthWall(VB vb, Rect inn)
        {
            const float z = NorthZ;
            // Solid part behind the bar: green tiled splashback, cluttered shelves, the chalkboard menu.
            float x0 = WinWestX - 0.15f, cx = (x0 + NorthWinX0) * .5f, len = NorthWinX0 - x0;
            vb.Box(new Vector3(cx, WallH * .5f, z), new Vector3(len, WallH, 0.3f), "cream", collider: true);
            vb.Box(new Vector3(cx, WallH + 0.06f, z), new Vector3(len + 0.1f, 0.12f, 0.36f), "walnut");
            for (float x = x0 + 0.3f; x < NorthWinX0 - 0.3f; x += 1.0f)
                vb.Sign("cf_tiles", new Vector3(x + 0.5f, 0.95f, z - 0.16f), 1.0f, 180f, 1.0f, lit: true);
            vb.Box(new Vector3(cx, 1.97f, z - 0.17f), new Vector3(len, 0.06f, 0.05f), "oak");
            vb.Sign("cf_chalk_menu", new Vector3(-3.0f, 2.1f, z - 0.2f), 2.9f, 180f, 1.45f, frame: "walnut", lit: true);
            // Open shelves: jars of beans, coffee bags, cup stacks, books, mugs, plants (the ghost plush sits on the middle one).
            string[] clutter = { "jar_beans", "cafe_books", "mug_lite", "coffee_bag", "plant_small", "cup_stack" };
            int n = 0;
            foreach (float y in new[] { 2.2f, 2.8f, 3.4f })
            {
                vb.Box(new Vector3(-6.6f, y, z - 0.32f), new Vector3(3.3f, 0.05f, 0.34f), "oak");
                for (int i = 0; i < 6; i++)
                {
                    float x = -8.0f + i * 0.55f;
                    if (y == 2.8f && i == 2) continue;
                    string id = clutter[(i + n) % clutter.Length];
                    float sc = id == "plant_small" ? 1.3f : id == "mug_lite" ? 1.3f : id == "cafe_books" ? 0.9f : 1f;
                    vb.ModelAt(id, new Vector3(x + ((i + n) % 2) * 0.06f, y + 0.025f, z - 0.3f), 180f + i * 9f, sc);
                    if (id == "mug_lite") vb.ModelAt("mug_lite", new Vector3(x + 0.14f, y + 0.025f, z - 0.32f), 160f, 1.3f);
                }
                n += 2;
            }
            foreach (var (x, sc) in new[] { (-8.0f, 0.75f), (-5.3f, 0.6f) })
                vb.ModelAt("pothos_vine", new Vector3(x, 3.42f - 1.09f * sc, z - 0.4f), x * 30f, sc);
            // Window part over the booths: sill wall, posts, transom, header.
            float wx0 = NorthWinX0, wx1 = inn.xMax + 0.3f, wcx = (wx0 + wx1) * .5f, wl = wx1 - wx0;
            vb.Box(new Vector3(wcx, 0.55f, z), new Vector3(wl, 1.1f, 0.3f), "cream", collider: true);
            vb.Box(new Vector3(wcx, 1.12f, z - 0.08f), new Vector3(wl, 0.05f, 0.42f), "oak");
            vb.Box(new Vector3(wcx, 4.2f, z), new Vector3(wl, 0.8f, 0.3f), "cream");
            vb.Box(new Vector3(wcx, 3.82f, z - 0.12f), new Vector3(wl, 0.08f, 0.16f), "walnut");
            for (float x = wx0; x <= wx1 + 0.01f; x += 1.9f)
                vb.Box(new Vector3(x, 2.45f, z), new Vector3(0.16f, 2.7f, 0.16f), "darkwood", collider: true);
            vb.Collider(new Vector3(wcx, 2.45f, z), new Vector3(wl, 2.7f, 0.2f));
            foreach (float x in new[] { 0.6f, 3.5f, 7.6f }) vb.ModelAt("cafe_plant_fern", new Vector3(x, 1.145f, z - 0.05f), x * 40f, 0.7f);
            var rnd = new System.Random(19);
            for (float x = wx0 + 0.7f; x < wx1 - 0.4f; x += 1.9f)
            {
                float sc = 0.45f + (float)rnd.NextDouble() * 0.5f;
                vb.ModelAt("pothos_vine", new Vector3(x, 3.78f - 1.09f * sc, z - 0.22f), (float)rnd.NextDouble() * 360f, sc);
            }
            // A small market TV on the pillar by the pulse_screen spot.
            vb.Sign("cf_candles_4", new Vector3(-0.45f, 1.55f, z - 0.17f), 0.85f, 180f, 0.54f, frame: "black");
        }

        private static void EastWall(VB vb, Rect inn)
        {
            float x = inn.xMax - 0.01f;
            // Exposed brick over the whole inner face.
            for (float z = inn.yMin; z < inn.yMax - 0.01f; z += 2.25f)
                for (int row = 0; row < 2; row++)
                    vb.Sign("cf_brick", new Vector3(x, row * 2.3f, Mathf.Min(z + 1.125f, inn.yMax - 1.125f)), 2.25f, -90f, 2.3f, lit: true);
            // Sunrise-coffee neon above the last two desks (a cup with rays, glow pipes, no letters).
            var c = new Vector3(x - 0.08f, 2.12f, 2.9f);
            var big = vb.Scaled(c, 1.6f);
            var q = Quaternion.Euler(0, -90f, 0);
            Vector3 L(float u, float v) => c + q * new Vector3(u, v, 0f);
            void Neon(Vector3 a, Vector3 b) => vb.Pipe(a, b, 0.025f, "glowamber", 6, glow: true);
            Neon(L(-0.42f, 0.3f), L(-0.34f, -0.3f)); Neon(L(-0.34f, -0.3f), L(0.34f, -0.3f)); Neon(L(0.34f, -0.3f), L(0.42f, 0.3f));
            Neon(L(-0.42f, 0.3f), L(0.42f, 0.3f));
            for (int i = 0; i < 8; i++)
            {
                float a0 = Mathf.PI * i / 8f, a1 = Mathf.PI * (i + 1) / 8f;
                Neon(L(0.42f + 0.18f * Mathf.Sin(a0), 0.1f - 0.18f * Mathf.Cos(a0)), L(0.42f + 0.18f * Mathf.Sin(a1), 0.1f - 0.18f * Mathf.Cos(a1)));
            }
            for (int i = 0; i < 9; i++)
            {
                float a = Mathf.PI * i / 8f;
                var d = new Vector3(-Mathf.Cos(a), Mathf.Sin(a), 0f);
                Neon(c + q * (new Vector3(0, 0.55f, 0) + d * 0.5f), c + q * (new Vector3(0, 0.55f, 0) + d * (i % 2 == 0 ? 0.85f : 0.72f)));
            }
            vb.PointLight(c + new Vector3(-0.6f, 0.3f, 0f), "glowamber", 0.9f, 4.5f);
            big.Dispose();
            // Shelves with books, jars and plants above the north-east corner.
            foreach (float y in new[] { 2.1f, 2.7f })
            {
                vb.Box(new Vector3(x - 0.17f, y, 5.0f), new Vector3(0.32f, 0.05f, 1.6f), "oak");
                for (int i = 0; i < 4; i++)
                {
                    var p = new Vector3(x - 0.18f, y + 0.025f, 4.4f + i * 0.4f);
                    string id = (i + (y > 2.5f ? 1 : 0)) % 3 == 0 ? "plant_small" : (i % 3 == 1 ? "cafe_books" : "jar_beans");
                    vb.ModelAt(id, p, -90f + i * 5f, id == "plant_small" ? 1.4f : id == "cafe_books" ? 1.1f : 1f);
                }
            }
            vb.ModelAt("pothos_vine", new Vector3(x - 0.2f, 2.66f - 1.09f * 0.7f, 5.6f), 40f, 0.7f);
            // Framed prints between and above the desks.
            foreach (var (art, zz, yy) in new[] { ("cf_art_1", -4.5f, 1.8f), ("cf_art_3", -2.55f, 1.95f), ("cf_art_4", -0.45f, 1.8f), ("cf_art_6", 4.95f, 1.2f) })
                vb.Sign(art, new Vector3(x - 0.05f, yy - 0.44f, zz), 0.7f, -90f, 0.88f, frame: "walnut", lit: true);
        }

        private static void Outside(VB vb)
        {
            // The town outside the windows (backdrop: not lightmapped): a street, trees, lamps and the painted town.
            vb.Backdrop = true;
            vb.Box(new Vector3(-15f, -0.06f, 0f), new Vector3(13f, 0.1f, 30f), "sand");
            vb.Box(new Vector3(0f, -0.06f, 13f), new Vector3(40f, 0.1f, 14f), "sand");
            vb.Box(new Vector3(15f, -0.06f, -2f), new Vector3(13f, 0.1f, 28f), "sand");
            foreach (float z in new[] { -4.6f, 1.4f })
            {
                vb.Model("map_tree_small", -11.6f, z, 0f, scale: 1.2f);
                vb.Cylinder(new Vector3(-11.6f, 0f, z + 3.0f), 0.06f, 3.4f, "iron", 6);
                vb.Box(new Vector3(-11.6f, 3.5f, z + 3.0f), new Vector3(0.26f, 0.36f, 0.26f), "glowamber", glow: true);
            }
            foreach (float x in new[] { 2.4f, 7.4f }) vb.Model("map_tree_small", x, 9.0f, 0f, scale: 1.1f);
            // The painted town just across the street so the gameplay camera sees its shopfronts through the windows.
            vb.Sign("cf_town", new Vector3(-13.6f, -0.9f, 0f), 30f, 90f, 15f);
            vb.Sign("cf_town", new Vector3(0f, -0.9f, 10.4f), 36f, 180f, 15f);
            vb.Sign("cf_town", new Vector3(13.6f, -0.9f, -1.5f), 30f, -90f, 15f);
            foreach (float z in new[] { -4.5f, 1.5f }) vb.Model("map_tree_small", 11.4f, z, 0f, scale: 1.1f);
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
                vb.Sign("cf_tiles", new Vector3(Mathf.Min(x + 0.49f, BarX1 - 0.49f), 0.16f, BarZ - 0.36f), 0.98f, 180f, 0.82f, lit: true);
            vb.Box(new Vector3(cx, 0.08f, BarZ - 0.37f), new Vector3(len, 0.16f, 0.04f), "darkwood");
            vb.Box(new Vector3(cx, 0.98f, BarZ - 0.37f), new Vector3(len, 0.06f, 0.05f), "oak");
            vb.Pipe(new Vector3(BarX0, 0.25f, BarZ - 0.62f), new Vector3(BarX1, 0.25f, BarZ - 0.62f), 0.025f, "brass", 10);
            for (float x = BarX0 + 0.4f; x < BarX1; x += 1.2f)
            {
                vb.Pipe(new Vector3(x, 0.25f, BarZ - 0.37f), new Vector3(x, 0.25f, BarZ - 0.6f), 0.014f, "brass", 6);
                vb.Cylinder(new Vector3(x, 0.24f, BarZ - 0.38f), 0.035f, 0.02f, "brass", 10);
            }
            // West return to the window sill so the barista area is closed on that side.
            vb.Box(new Vector3(BarX0 - 0.33f, 0.52f, BarZ + 0.6f), new Vector3(0.5f, 1.04f, 1.9f), "walnut", solid: true);
            vb.Box(new Vector3(BarX0 - 0.33f, 1.07f, BarZ + 0.6f), new Vector3(0.62f, 0.06f, 2.0f), "oak");
            float top = 1.1f;
            // A full pastry case: croissants and muffins below, doughnuts, cupcakes and a cake above.
            var pc = new Vector3(-6.95f, top, BarZ - 0.05f);
            vb.ModelAt("pastry_case", pc, 180f);
            for (int i = 0; i < 4; i++) vb.ModelAt("croissant", pc + new Vector3(-0.6f + i * 0.2f, 0.105f, 0.06f), 90f + i * 12f, 1.2f);
            for (int i = 0; i < 3; i++) vb.ModelAt("muffin", pc + new Vector3(0.22f + i * 0.15f, 0.105f, 0.04f), i * 50f, 1.0f);
            for (int i = 0; i < 3; i++) vb.ModelAt("donut", pc + new Vector3(-0.6f + i * 0.17f, 0.355f, 0.04f), i * 40f, 1.4f);
            for (int i = 0; i < 3; i++) vb.ModelAt("cupcake", pc + new Vector3(-0.04f + i * 0.14f, 0.355f, 0.05f), i * 60f, 1f);
            vb.ModelAt("cake", pc + new Vector3(0.48f, 0.355f, 0.03f), 20f, 1.0f);
            vb.ModelAt("plate", pc + new Vector3(0.0f, 0.62f, -0.05f), 0f, 1f);
            for (int i = 0; i < 4; i++) vb.ModelAt("cookie", pc + new Vector3(-0.06f + (i % 2) * 0.1f, 0.645f + (i / 2) * 0.02f, -0.08f + (i % 2) * 0.05f), i * 70f, 1f);
            // Till, cups, a jar of beans, napkins, a small plant.
            vb.ModelAt("cash_register", new Vector3(-3.6f, top, BarZ + 0.05f), 180f, 1.0f);
            for (int i = 0; i < 3; i++) vb.ModelAt("cup_coffee", new Vector3(-4.7f + i * 0.22f, top, BarZ - 0.15f), i * 40f, 1.2f);
            vb.ModelAt("jar_beans", new Vector3(-5.95f, top, BarZ + 0.1f), 0f, 1f);
            vb.ModelAt("plant_small", new Vector3(-5.6f, top, BarZ + 0.15f), 0f, 1.5f);
            vb.Box(new Vector3(-4.1f, top + 0.06f, BarZ + 0.1f), new Vector3(0.14f, 0.12f, 0.1f), "steel");
            // Bentwood bar stools with green cushions on a runner (decor, collided).
            vb.FloorSign("cf_rug_3", cx, BarZ - 0.95f, len - 0.2f, 0f, 1.1f, 0f, lit: true);
            foreach (float x in new[] { -7.2f, -6.0f, -4.8f, -3.6f })
                vb.ModelAt("cafe_bar_stool", new Vector3(x, 0f, BarZ - 0.95f), 0f);
            // Back counter (cabinet doors on its front), espresso machine, two grinders, drip brewer, jars, cups.
            float bx0 = WinWestX + 0.25f, bx1 = -1.3f, bcx = (bx0 + bx1) * .5f, bcl = bx1 - bx0;
            vb.Box(new Vector3(bcx, 0.47f, BackCounterZ), new Vector3(bcl, 0.94f, 0.7f), "walnut", solid: true);
            vb.Box(new Vector3(bcx, 0.97f, BackCounterZ), new Vector3(bcl + 0.06f, 0.06f, 0.76f), "oak");
            for (float x = bx0 + 0.3f; x < bx1 - 0.2f; x += 0.6f)
            {
                vb.Box(new Vector3(x, 0.47f, BackCounterZ - 0.355f), new Vector3(0.54f, 0.78f, 0.02f), "oak");
                vb.Box(new Vector3(x + 0.2f, 0.62f, BackCounterZ - 0.37f), new Vector3(0.02f, 0.12f, 0.02f), "brass");
            }
            float bt = 1.0f;
            vb.ModelAt("espresso_machine", new Vector3(-4.9f, bt, BackCounterZ + 0.06f), 180f);
            vb.ModelAt("coffee_grinder", new Vector3(-3.85f, bt, BackCounterZ + 0.05f), 180f);
            vb.ModelAt("coffee_grinder", new Vector3(-3.5f, bt, BackCounterZ + 0.05f), 170f);
            vb.ModelAt("coffee_machine", new Vector3(-6.3f, bt, BackCounterZ), 180f, 1.2f);
            for (int i = 0; i < 6; i++) vb.ModelAt("mug_lite", new Vector3(-7.7f + (i % 3) * 0.2f, bt, BackCounterZ - 0.05f + (i / 3) * 0.16f), 180f + i * 20f, 1.3f);
            foreach (float x in new[] { -5.75f, -2.2f }) vb.ModelAt("cup_stack", new Vector3(x, bt, BackCounterZ - 0.1f), 0f, 1f);
            for (int i = 0; i < 3; i++) vb.ModelAt("jar_beans", new Vector3(-2.9f + i * 0.19f, bt, BackCounterZ + 0.08f), i * 30f, 1f);
            for (int i = 0; i < 4; i++) vb.ModelAt("cup_coffee", new Vector3(-1.95f + (i % 2) * 0.17f, bt, BackCounterZ - 0.12f + (i / 2) * 0.17f), i * 25f, 1.4f);
            vb.ModelAt("coffee_bag", new Vector3(-1.55f, bt, BackCounterZ + 0.05f), 190f, 1f);
            // Pendants over the bar.
            foreach (float x in new[] { -7.0f, -5.25f, -3.6f }) Pendant(vb, new Vector3(x, 2.65f, BarZ), "awninggreen");
        }

        /// <summary>Pendant light: cord rising to the ceiling line, modelled enamel dome (brass for "brass"), warm bulb, baked light.</summary>
        private static void Pendant(VB vb, Vector3 bulb, string shade)
        {
            vb.Pipe(bulb + Vector3.up * 0.3f, new Vector3(bulb.x, WallH + 0.5f, bulb.z), 0.008f, "black", 4);
            vb.ModelAt("pendant_dome", bulb - Vector3.up * 0.04f, 0f, 1f, recolor: shade == "awninggreen" ? null : shade);
            vb.Sphere(bulb, 0.07f, "glowamber", glow: true, rings: 4, segments: 8);
            vb.PointLight(bulb - Vector3.up * 0.2f, "glowamber", 0.6f, 4.0f);
        }

        // ------------------------------------------------------------------ seating

        private static void Booths(VB vb)
        {
            foreach (float cx in BoothX)
            {
                float bz = NorthZ - 0.55f;
                var t = new Vector3(cx, 0f, bz - 0.95f);
                vb.FloorSign("cf_rug_1", cx, bz - 0.85f, 2.8f, 0f, 1.7f, 0f, lit: true);
                // Modelled booth: walnut base and frame, two seat cushions, four channel-tufted back cushions.
                vb.ModelAt("booth_bench", new Vector3(cx, 0f, bz), 180f);
                vb.ModelAt("throw_pillow", new Vector3(cx - 0.75f, 0.5f, bz + 0.1f), 188f, 1f, recolor: cx < 3f ? "petrol" : "ctorange");
                vb.ModelAt("throw_pillow", new Vector3(cx + 0.72f, 0.5f, bz + 0.1f), 172f, 1f, recolor: "cream");
                vb.ModelAt("cafe_table_booth", t, 0f);
                float tt = 0.765f;
                vb.ModelAt("cup_coffee", t + new Vector3(-0.35f, tt, 0.08f), 0f, 1.2f);
                vb.ModelAt("mug_lite", t + new Vector3(0.38f, tt, -0.1f), 120f, 1.3f);
                vb.ModelAt("plate", t + new Vector3(-0.05f, tt, -0.08f), 0f, 1f);
                vb.ModelAt("croissant", t + new Vector3(-0.05f, tt + 0.02f, -0.08f), 40f, 1.3f);
                vb.ModelAt("plant_small", t + new Vector3(0.25f, tt, 0.16f), 0f, 1.3f);
                foreach (float dx in new[] { -0.55f, 0.55f })
                    vb.ModelAt("cafe_chair_spindle", t + new Vector3(dx, 0f, -0.75f), 0f);
                Pendant(vb, t + Vector3.up * 2.3f, "brass");
            }
        }

        private static void CommunalTable(VB vb)
        {
            var c = Communal;
            vb.FloorSign("cf_rug_1", c.x, c.z, 7.0f, 0f, 3.6f, 0f, lit: true);
            // Two modelled plank farmhouse tables on trestles pushed together (2 x 2.6 x 1.3 m, top 0.76 m).
            vb.ModelAt("cafe_table_farm", c + new Vector3(-1.3f, 0f, 0f), 0f);
            vb.ModelAt("cafe_table_farm", c + new Vector3(1.3f, 0f, 0f), 180f);
            float top = 0.762f;
            vb.ModelAt("cafe_desk_lamp", c + new Vector3(-0.6f, top, 0.05f), 90f, 1f);
            vb.PointLight(c + new Vector3(-0.55f, top + 0.5f, 0.05f), "glowamber", 0.4f, 2.5f);
            vb.ModelAt("cafe_plant_snake", c + new Vector3(0.35f, top, 0f), 0f, 0.5f);
            vb.ModelAt("cafe_books", c + new Vector3(1.0f, top, 0.1f), 20f, 1f);
            vb.ModelAt("cafe_books", c + new Vector3(-2.2f, top, -0.05f), 110f, 0.9f);
            vb.ModelAt("plate", c + new Vector3(2.1f, top, 0.05f), 0f, 1f);
            vb.ModelAt("croissant", c + new Vector3(2.1f, top + 0.02f, 0.05f), 15f, 1.3f);
            vb.ModelAt("muffin", c + new Vector3(2.18f, top + 0.02f, -0.06f), 0f, 1f);
            // Terminal seats: spindle chairs with green cushions, laptops fully on the table.
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

        private static void RoundTables(VB vb)
        {
            int n = 0;
            foreach (var (c, ids) in new[] { (Round1, new[] { "table_1", "table_2" }), (Round2, new[] { "table_3", "table_4" }) })
            {
                vb.FloorSign("cf_rug_2", c.x, c.z - 0.05f, 2.9f, 0f, 2.9f, 0f, lit: true);
                // Modelled pedestal bistro table and bentwood chairs with green cushions; a coffee and a pastry on a plate.
                vb.ModelAt("cafe_table_bistro", c, 0f);
                vb.ModelAt("cup_coffee", c + new Vector3(-0.18f, 0.76f, 0.05f), 0f, 1.2f);
                vb.ModelAt("mug_lite", c + new Vector3(0.2f, 0.76f, -0.12f), 70f, 1.3f);
                vb.ModelAt("plant_small", c + new Vector3(0.12f, 0.76f, 0.16f), 0f, 1.3f);
                vb.ModelAt("plate", c + new Vector3(-0.02f, 0.76f, -0.18f), 0f, 0.9f);
                vb.ModelAt(n++ == 0 ? "croissant" : "cupcake", c + new Vector3(-0.02f, 0.78f, -0.18f), 30f, 1.2f);
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
            // The desks stand 0.6 m in front of their anchors (fe.7 reach: keyboard 0.45 m ahead, knees under the desk), on a runner.
            vb.FloorSign("cf_rug_1", 7.55f, -0.45f, 7.4f, 90f, 1.7f, 0f, lit: true);
            int k = 0;
            foreach (var s in vb.SpotsWhere(s => s.id == "terminal_5" || s.id == "terminal_6" || s.id == "terminal_7" || s.id == "terminal_8"))
            {
                var p = VB.P(s);
                var f = Fwd(s);
                var desk = new Vector3(DeskX, 0f, p.z);
                vb.ModelAt("cafe_desk", desk, VB.Deg(s) + 180f, 1f, recolor: "walnut");
                vb.RegisterProp("trading_desk", desk + Vector3.up * 0.38f, new Vector3(1.5f, 0.76f, 0.7f), VB.Deg(s));
                // The seat carve keeps 0.6 m round the anchor, which leaves the desk's near corners uncarved: block them.
                foreach (float e in new[] { -1f, 1f })
                    vb.Obstacle(new Vector3(DeskX - 0.175f, 0.4f, p.z + e * 0.78f), new Vector3(0.35f, 0.8f, 0.36f));
                vb.ModelAt("cafe_office_chair", p - f * 0.05f, VB.Deg(s), 1f, recolor: "charcoal");
                vb.SeatCollider(s, new Vector3(0, 0.75f, -0.05f), new Vector3(0.6f, 0.5f, 0.6f));
                vb.Approach(s, p + Right(s) * 1.35f - f * 0.1f);
                // Twin monitors with candle charts, keyboard, mug, a plant or books.
                foreach (float side in new[] { -1f, 1f })
                    Monitor(vb, desk + f * 0.17f + Right(s) * side * 0.33f + Vector3.up * 0.76f, VB.Deg(s) - side * 14f,
                            "cf_candles_" + (1 + (k + (side > 0 ? 1 : 0)) % 4));
                vb.ModelAt("cafe_keyboard", desk - f * 0.15f + Vector3.up * 0.762f, VB.Deg(s) + 180f);
                vb.ModelAt("mug_lite", desk - f * 0.1f + Right(s) * 0.45f + Vector3.up * 0.76f, 0f, 1.3f);
                if (k % 2 == 0) vb.ModelAt("plant_small", desk + f * 0.2f - Right(s) * 0.62f + Vector3.up * 0.76f, 0f, 1.5f);
                else vb.ModelAt("cafe_books", desk + f * 0.15f - Right(s) * 0.6f + Vector3.up * 0.76f, VB.Deg(s), 0.9f);
                k++;
            }
        }

        private static void Lounge(VB vb)
        {
            // Green velvet mid-century sofa against the window sill facing the room, pedestal coffee table, knitted pouf.
            vb.FloorSign("cf_rug_3", -6.8f, -3.6f, 3.6f, 90f, 3.1f, 0f, lit: true);
            vb.Model("cafe_sofa", -8.05f, -3.6f, 90f);
            vb.ModelAt("throw_pillow", new Vector3(-8.2f, 0.42f, -4.3f), 80f, 1f, recolor: "cream");
            vb.ModelAt("throw_pillow", new Vector3(-8.2f, 0.42f, -2.95f), 100f, 1f, recolor: "ctorange");
            vb.Model("home_lamp_standing", -8.1f, -5.3f, 0f);
            vb.Solid(-8.1f, -5.3f, 0.5f, 0.5f, 1.6f);
            vb.PointLight(new Vector3(-7.9f, 1.7f, -5.15f), "glowamber", 0.45f, 3f);
            var t = new Vector3(-6.7f, 0f, -3.6f);
            vb.ModelAt("cafe_coffee_table", t, 0f);
            vb.ModelAt("cup_coffee", t + new Vector3(-0.15f, 0.46f, 0.1f), 0f, 1.2f);
            vb.ModelAt("plant_small", t + new Vector3(0.12f, 0.46f, -0.12f), 0f, 1.4f);
            vb.ModelAt("cafe_books", t + new Vector3(0.18f, 0.46f, 0.22f), 70f, 0.9f);
            vb.ModelAt("plate", t + new Vector3(-0.2f, 0.46f, -0.18f), 0f, 0.85f);
            for (int i = 0; i < 3; i++) vb.ModelAt("cookie", t + new Vector3(-0.24f + i * 0.05f, 0.475f, -0.2f + (i % 2) * 0.05f), i * 60f, 1f);
            vb.ModelAt("pouf_round", new Vector3(-5.55f, 0f, -2.35f), 0f, 1f, recolor: "mustard");
            // Window seat with a yellow cushion (the astronaut cat naps on it: Eggs) and two throw pillows.
            vb.Box(new Vector3(-8.1f, 0.24f, 0.3f), new Vector3(0.75f, 0.48f, 3.4f), "walnut");
            vb.Box(new Vector3(-8.1f, 0.54f, 0.3f), new Vector3(0.7f, 0.12f, 3.3f), "mustard");
            vb.Box(new Vector3(-8.4f, 0.75f, 0.3f), new Vector3(0.12f, 0.36f, 3.2f), "cream");
            vb.RegisterProp("window_seat", new Vector3(-8.1f, 0.3f, 0.3f), new Vector3(0.75f, 0.6f, 3.4f));
            vb.ModelAt("throw_pillow", new Vector3(-8.27f, 0.58f, -1.0f), 100f, 1f, recolor: "petrol");
            vb.ModelAt("throw_pillow", new Vector3(-8.27f, 0.58f, 1.75f), 76f, 1f, recolor: "cream");
            vb.ModelAt("cafe_books", new Vector3(-7.95f, 0.6f, -0.45f), 80f, 0.9f);
            vb.ModelAt("mug_lite", new Vector3(-7.9f, 0.6f, -0.15f), 10f, 1.3f);
        }

        private static void Armchairs(VB vb)
        {
            vb.FloorSign("cf_rug_2", 4.85f, -4.35f, 2.8f, 0f, 2.8f, 0f, lit: true);
            vb.Model("cafe_armchair", 4.0f, -4.7f, 30f);
            vb.Model("cafe_armchair", 5.7f, -4.7f, -30f);
            vb.ModelAt("throw_pillow", new Vector3(4.1f, 0.45f, -4.95f), 30f, 0.9f, recolor: "cream");
            vb.ModelAt("throw_pillow", new Vector3(5.6f, 0.45f, -4.95f), -30f, 0.9f, recolor: "mustard");
            var t = new Vector3(4.85f, 0f, -3.75f);
            vb.ModelAt("cafe_side_table", t, 0f);
            vb.ModelAt("mug_lite", t + new Vector3(0.12f, 0.55f, 0.05f), 0f, 1.3f);
            vb.ModelAt("cafe_books", t + new Vector3(-0.12f, 0.55f, -0.05f), 20f, 0.8f);
            vb.ModelAt("plant_small", t + new Vector3(-0.05f, 0.55f, 0.15f), 0f, 1.3f);
        }

        private static void FrontPlanters(VB vb, Rect inn)
        {
            float z = inn.yMin + 0.4f;
            foreach (var (x0, x1) in new[] { (-7.4f, -3.75f), (2.6f, 6.3f) })
            {
                float cx = (x0 + x1) * .5f, len = x1 - x0;
                vb.Box(new Vector3(cx, 0.3f, z), new Vector3(len, 0.6f, 0.55f), "walnut");
                vb.Box(new Vector3(cx, 0.6f, z), new Vector3(len + 0.06f, 0.04f, 0.6f), "oak");
                vb.RegisterProp("planter_box", new Vector3(cx, 0.3f, z), new Vector3(len, 0.6f, 0.55f));
                int n = Mathf.RoundToInt(len / 0.7f);
                for (int i = 0; i < n; i++)
                {
                    float x = cx - len * .5f + (i + 0.5f) * len / n;
                    string id = i % 3 == 1 ? "home_houseplant_1" : i % 3 == 2 ? "houseplant_fern_lite" : "houseplant_bush_lite";
                    vb.Model(id, x, z + (i % 2 == 0 ? 0.05f : -0.05f), 37f * i, y: 0.6f, scale: 0.6f);
                }
            }
            // A tall open bookcase by the door: books, plants, jars.
            var bc = new Vector3(-3.15f, 0f, inn.yMin + 0.32f);
            vb.ModelAt("cafe_bookcase", bc, 0f, 1f);
            foreach (var (y, id) in new[] { (1.9f, "plant_small"), (1.9f, "cafe_books") })
                vb.ModelAt(id, bc + new Vector3(id == "plant_small" ? -0.22f : 0.2f, y, 0f), 0f, id == "plant_small" ? 1.5f : 1f);
            // A-frame chalk board outside the door (sun over a cup).
            var b = new Vector3(-3.0f, 0f, inn.yMin - 1.1f);
            vb.BoxRot(b + new Vector3(0, 0.55f, -0.14f), new Vector3(0.62f, 1.1f, 0.04f), Quaternion.Euler(14f, 0, 0), "walnut");
            vb.BoxRot(b + new Vector3(0, 0.55f, 0.14f), new Vector3(0.62f, 1.1f, 0.04f), Quaternion.Euler(-14f, 0, 0), "walnut");
            var m = Matrix4x4.TRS(b + new Vector3(0, 0.12f, -0.29f), Quaternion.Euler(14f, 0, 0), Vector3.one);
            vb.SignAt("cf_chalk_sun", m, 0.52f, 0.7f, lit: true);
        }

        /// <summary>
        /// The pavement in front of the café (the bottom of the gameplay frame): stone paving, lush planter boxes against the
        /// low front wall, two sidewalk bistro sets, lamp posts. Outside the walkable rectangle (decor only).
        /// </summary>
        private static void Sidewalk(VB vb, Rect inn)
        {
            float wallZ = inn.yMin - 0.3f;                       // outer face of the low front wall
            vb.Floor("plaza_hexpaving_2m", Rect.MinMaxRect(inn.xMin - 1.2f, wallZ - 6.0f, inn.xMax + 1.2f, wallZ), 0.012f);
            vb.Box(new Vector3(0f, 0.07f, wallZ - 4.65f), new Vector3(inn.width + 2.4f, 0.14f, 0.22f), "limestone");
            vb.Box(new Vector3(0f, 0.02f, wallZ - 5.4f), new Vector3(inn.width + 2.4f, 0.04f, 1.3f), "asphalt");
            foreach (var (x0, x1) in new[] { (-8.7f, -3.6f), (3.6f, 8.7f) })
            {
                float cx = (x0 + x1) * .5f, len = x1 - x0, z = wallZ - 0.4f;
                vb.Box(new Vector3(cx, 0.32f, z), new Vector3(len, 0.64f, 0.6f), "darkwood");
                vb.Box(new Vector3(cx, 0.66f, z), new Vector3(len + 0.06f, 0.05f, 0.66f), "oak");
                int n = Mathf.RoundToInt(len / 0.62f);
                for (int i = 0; i < n; i++)
                {
                    float x = cx - len * .5f + (i + 0.5f) * len / n;
                    string id = i % 4 == 0 ? "cafe_plant_leafy" : i % 4 == 1 ? "houseplant_fern_lite" : i % 4 == 2 ? "houseplant_bush_lite" : "home_houseplant_1";
                    float sc = id == "cafe_plant_leafy" ? 0.55f : 0.62f;
                    vb.Model(id, x, z + (i % 2 == 0 ? 0.06f : -0.06f), 47f * i, y: 0.66f, scale: sc);
                }
            }
            // Two sidewalk bistro sets and a pair of lamp posts.
            foreach (float sx in new[] { -1f, 1f })
            {
                var t = new Vector3(sx * 6.0f, 0.012f, wallZ - 2.3f);
                vb.ModelAt("cafe_table_bistro", t, 0f, 0.82f);
                vb.ModelAt("cafe_chair_bistro", t + new Vector3(-0.62f, 0f, 0.05f), 90f);
                vb.ModelAt("cafe_chair_bistro", t + new Vector3(0.62f, 0f, -0.05f), -90f);
                vb.ModelAt("cup_coffee", t + new Vector3(-0.1f, 0.625f, 0.05f), 0f, 1.2f);
                vb.ModelAt("plate", t + new Vector3(0.12f, 0.625f, -0.04f), 0f, 0.8f);
                vb.ModelAt(sx < 0 ? "croissant" : "muffin", t + new Vector3(0.12f, 0.64f, -0.04f), 30f, 1.2f);
                vb.ModelAt("tl_lamp_post", new Vector3(sx * 9.4f, 0.012f, wallZ - 3.9f), 0f);
            }
        }

        private static void Plants(VB vb)
        {
            foreach (var (x, z, id, sc) in new[]
                     {
                         (7.95f, 5.55f, "cafe_plant_leafy", 0.9f), (3.5f, 5.6f, "cafe_plant_monstera", 0.75f), (8.15f, -5.45f, "cafe_plant_snake", 0.9f),
                         (8.2f, -2.55f, "cafe_plant_snake", 0.8f), (8.2f, -0.45f, "cafe_plant_snake", 0.75f), (8.2f, 1.65f, "cafe_plant_snake", 0.8f),
                         (-8.15f, -1.95f, "cafe_plant_snake", 0.85f), (-8.15f, 2.5f, "cafe_plant_fern", 0.9f), (-2.55f, 2.5f, "cafe_plant_bush", 0.7f),
                         (2.35f, -5.15f, "cafe_plant_leafy", 0.85f),
                     })
                vb.Plant(x, z, id, sc);
        }

        /// <summary>Macramé planters hanging from the ceiling line, pothos trailing below (the concept's hanging greens).</summary>
        private static void HangingPlanters(VB vb)
        {
            var rnd = new System.Random(31);
            foreach (var (x, z, y) in new[] { (-6.3f, -1.4f, 2.95f), (-4.0f, -4.2f, 3.05f), (1.6f, -3.4f, 3.0f), (3.3f, 2.9f, 3.1f), (6.4f, -2.4f, 3.0f), (-1.0f, 3.4f, 3.15f) })
            {
                var p = new Vector3(x, y, z);
                vb.Pipe(p + Vector3.up * 0.62f, new Vector3(x, WallH + 0.5f, z), 0.006f, "darkwood", 3);
                vb.ModelAt("hanging_planter", p, (float)rnd.NextDouble() * 360f, 1f);
                float sc = 0.55f + (float)rnd.NextDouble() * 0.3f;
                vb.ModelAt("pothos_vine", p + new Vector3(0.08f, 0.15f - 1.09f * sc, 0.04f), (float)rnd.NextDouble() * 360f, sc);
            }
        }

        // ------------------------------------------------------------------ eggs

        private static void Eggs(VB vb)
        {
            // Astronaut cat napping on the window-seat cushion.
            var seat = new Vector3(-8.0f, 0.6f, 0.75f);
            using (vb.Scaled(seat, 2.0f)) VenueEggs.JupiterCat(vb, seat, 90f);
            // Pink-beanie dog under the communal table, peeking out towards the room.
            var dog = Communal + new Vector3(-0.05f, 0f, -0.55f);
            using (vb.Scaled(dog, 1.25f)) VenueEggs.DogWithBeanie(vb, dog, 175f);
            // Three-lens phone on the bar.
            VenueEggs.SeekerPhone(vb, new Vector3(-5.25f, 1.112f, BarZ - 0.2f), 160f, 1.4f);
            // Purple ghost plush on the middle shelf behind the bar.
            var shelf = new Vector3(-6.9f, 2.825f, NorthZ - 0.3f);
            using (vb.Scaled(shelf, 0.42f)) VenueEggs.PhantomGhost(vb, shelf - new Vector3(0.32f, 0.8f, 0.3f));
            // O-mouth cat by the till.
            VenueEggs.Popcat(vb, new Vector3(-3.15f, 1.26f, BarZ + 0.05f), 180f, 0.75f, doughnutOnString: false);
        }
    }
}
