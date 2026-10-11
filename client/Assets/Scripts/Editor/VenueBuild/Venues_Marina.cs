using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>Dubai Marina district: Cabal Lounge, Yacht Club, Penthouse Row.</summary>
    public static class VenuesMarina
    {
        private static readonly string[] Towers = { "tower_glass_a", "tower_glass_b", "tower_glass_c", "tower_glass_d", "tower_glass_e" };

        /// <summary>Marina tower canyon backdrop (decor only, far from the walkable area).</summary>
        private static void Skyline(VB vb, float near, float far, float spread)
        {
            int i = 0;
            for (float x = -spread; x <= spread; x += 13f)
            {
                float z = (i % 2 == 0) ? near : far;
                vb.Model(Towers[i % Towers.Length], x, z, 180f, scale: 0.8f + (i % 3) * 0.08f);
                i++;
            }
        }

        // ------------------------------------------------------------------ Cabal Lounge
        // Members-only lounge: velvet booths (6 claimable seats) under the CABAL CHAT screen (live hook), gold neon,
        // guest-list rope and podium, back bar with glowing bottle wall and insider menu, "100% NOT BUNDLED" neon.
        public static void CabalLounge(VB vb)
        {
            vb.InteriorShell("floor_marble_2m", "velvetpurple", wallHeight: 6.0f, sideHeight: 4.8f, trim: "gold");
            float back = vb.Inner.yMax;
            vb.Decal(0, -5.5f, 2.2f, 9f, "velvetred");
            vb.RingDecal(new Vector3(0, 0, 3.5f), 5.2f, 0.12f, "gold", 56);
            vb.Decal(0, 3.5f, 9.4f, 3.6f, "darkmarble");
            vb.OnWall('N', 0, 2.5f, 7.0f, "cl_chat_idle", hook: "CabalChat");
            vb.OnWall('N', 0, 5.0f, 6.0f, "cl_title");
            foreach (float cx in new[] { -6.5f, 0f, 6.5f })
            {
                vb.Model("lounge_sofa", cx, 8.6f, 180f, recolor: "velvetred");
                vb.Obstacle(new Vector3(cx, 0.45f, 8.95f), new Vector3(2.2f, 0.9f, 0.35f));
                vb.Model("home_table_roundsmall", cx, 7.1f, 0f, scale: 0.75f, recolor: "gold");
                vb.Solid(cx, 7.1f, 0.7f, 0.7f, 0.6f);
                vb.Cylinder(new Vector3(cx, 0.55f, 7.1f), 0.05f, 0.14f, "glowamber", 6, glow: true);
                vb.PointLight(new Vector3(cx, 1.6f, 7.4f), "glowamber", 0.9f, 4f);
            }
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("booth_")))
            {
                vb.SeatCollider(s, new Vector3(0, 0.6f, -0.35f), new Vector3(0.9f, 0.5f, 0.7f));
                float cx = Mathf.Round(s.x / 6.5f) * 6.5f;
                // Step out sideways from the booth (seat faces -z, so local right is world -x).
                vb.Approach(s, VB.Rel(s, s.x < cx ? 0.65f : -0.65f, 0.45f));
            }
            // Back bar.
            float bx = vb.Inner.xMin + 1.3f;
            vb.Counter(bx, -2.0f, 7.0f, 0.7f, -90f, "darkmarble", "gold", "glowpink");
            for (int r = 0; r < 3; r++)
            {
                vb.Box(new Vector3(vb.Inner.xMin + 0.15f, 1.3f + r * 0.5f, -2.0f), new Vector3(0.25f, 0.04f, 6.5f), "gold");
                for (int c = 0; c < 14; c++)
                    vb.Cylinder(new Vector3(vb.Inner.xMin + 0.18f, 1.32f + r * 0.5f, -5.0f + c * 0.46f), 0.05f, 0.3f,
                        c % 3 == 0 ? "glowamber" : c % 3 == 1 ? "glowpink" : "glowgreen", 6, glow: true);
            }
            for (int i = 0; i < 4; i++) vb.Model("bar_stool", bx + 0.95f, -4.6f + i * 1.6f, 90f);
            vb.OnWall('W', 4.0f, 1.0f, 2.0f, "cl_menu", frame: "gold");
            vb.OnWall('E', 0.5f, 3.0f, 5.4f, "cl_bundle");
            vb.OnWall('E', -5.0f, 1.0f, 2.2f, "cl_art", frame: "gold");
            // Guest-list rope and podium at the door.
            vb.Stanchions(new Vector2(-3.4f, -7.8f), new Vector2(-1.7f, -7.8f), "velvetred");
            vb.Stanchions(new Vector2(1.7f, -7.8f), new Vector2(3.4f, -7.8f), "velvetred");
            vb.Model("podium", -4.4f, -7.6f, 180f, screen: "cl_guest");
            vb.Solid(-4.4f, -7.6f, 0.9f, 0.7f, 1.2f);
            vb.OnWall('E', -8.0f, 1.4f, 3.0f, "cl_guest", frame: "gold");
            foreach (float x in new[] { -6f, 0f, 6f })
                vb.Model("home_light_chandelier", x, 2.5f, y: 3.8f, scale: 0.7f);
            vb.Plant(11.2f, 9.2f);
            vb.Plant(-11.2f, 9.2f);
            vb.Plant(11.2f, -9.2f, "houseplant_bush");
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 1.0f, 1.6f);
            vb.PointLight(new Vector3(0, 3.6f, 2.5f), "glowamber", 1.6f, 11f);
            vb.PointLight(new Vector3(-10f, 2.4f, -2f), "glowpink", 1.0f, 6f);
            vb.PointLight(new Vector3(8f, 3.4f, 0.5f), "glowpink", 0.9f, 6f);
        }

        // ------------------------------------------------------------------ Yacht Club
        // Marina dock: teak deck with sun loungers (6), bar with stools (4) and bartender post, the moored yacht
        // "NUMBER GO UP" and gangway, "EXIT LIQUIDITY" out in the marina, departures board (live hook), palms,
        // parasols, an orange supercar on the quay and the Dubai-Marina tower canyon behind.
        public static void YachtClub(VB vb)
        {
            var w = vb.Walk;
            vb.Backdrop = true;
            vb.Box(new Vector3(0, -0.75f, 10f), new Vector3(160f, 1.0f, 140f), "water");
            vb.Backdrop = false;
            vb.Floor("floor_wood_2m", Rect.MinMaxRect(w.xMin - 1f, w.yMin - 1f, w.xMax + 1f, w.yMax + 1f));
            vb.Box(new Vector3(0, -0.35f, 0), new Vector3(w.width + 2.1f, 0.6f, w.height + 2.1f), "walnut");
            vb.Backdrop = true;
            vb.Floor("floor_concrete_2m", Rect.MinMaxRect(-20f, w.yMin - 9f, 20f, w.yMin - 1f));
            vb.Backdrop = false;
            vb.Rail(new Vector2(w.xMin - 0.3f, w.yMax + 0.3f), new Vector2(-1.1f, w.yMax + 0.3f), 1.0f, "chrome", 2.4f, true);
            vb.Rail(new Vector2(1.1f, w.yMax + 0.3f), new Vector2(w.xMax + 0.3f, w.yMax + 0.3f), 1.0f, "chrome", 2.4f, true);
            vb.Rail(new Vector2(w.xMax + 0.3f, w.yMin - 0.3f), new Vector2(w.xMax + 0.3f, w.yMax + 0.3f), 1.0f, "chrome", 2.4f, true);
            vb.Rail(new Vector2(w.xMin - 0.3f, w.yMin - 0.3f), new Vector2(w.xMin - 0.3f, w.yMax + 0.3f), 1.0f, "chrome", 2.4f, true);
            vb.Rail(new Vector2(w.xMin - 0.3f, w.yMin - 0.3f), new Vector2(-2.2f, w.yMin - 0.3f), 1.0f, "chrome", 2.4f, true);
            vb.Rail(new Vector2(2.2f, w.yMin - 0.3f), new Vector2(w.xMax + 0.3f, w.yMin - 0.3f), 1.0f, "chrome", 2.4f, true);

            vb.Yacht(new Vector3(0, 0, w.yMax + 5.2f), 0f, 17f, "yc_hull1");
            vb.Box(new Vector3(0, 0.35f, w.yMax + 1.6f), new Vector3(1.2f, 0.06f, 3.4f), "oak");
            vb.Backdrop = true;
            vb.Yacht(new Vector3(-16f, 0, w.yMax + 20f), 18f, 22f, "yc_hull2");
            vb.Yacht(new Vector3(20f, 0, w.yMax + 13f), -12f, 14f);
            vb.Backdrop = false;
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("lounger_")))
                vb.Seat(s, "lounge_chair", "white", back: 0.1f, approachBack: 0.8f);
            foreach (float x in new[] { -7.5f, 0f, 7.5f })
            {
                vb.Model("parasol", x, 5.9f, 0f);
                vb.Solid(x, 5.9f, 0.4f, 0.4f, 2.4f);
            }
            // Bar (bar_1..4 stools face the counter; bartender behind it).
            vb.Counter(-10.0f, -1.75f, 5.6f, 0.6f, -90f, "white", "oak", "glowcyan");
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("bar_")))
                vb.Seat(s, "bar_stool", approachBack: 0.7f);
            vb.Box(new Vector3(w.xMin - 0.65f, 1.6f, -1.75f), new Vector3(0.2f, 3.2f, 6.4f), "navy");
            vb.Sign("yc_bar", new Vector3(w.xMin - 0.53f, 1.5f, -1.75f), 3.4f, 90f);
            vb.Sign("yc_title", new Vector3(w.xMin - 0.53f, 2.45f, -1.75f), 4.8f, 90f);
            vb.HookPanel("MarinaDepartures", "yc_board_idle", new Vector3(w.xMax + 0.9f, 1.0f, 1.0f), 4.6f, -90f, frame: "navy");
            foreach (float o in new[] { -2.4f, 2.4f }) vb.Cylinder(new Vector3(w.xMax + 0.95f, -0.3f, 1.0f + o), 0.08f, 1.4f, "chrome", 6);
            vb.Sign("yc_side", new Vector3(5.6f, 0.15f, w.yMin - 0.15f), 2.8f, 180f, frame: "navy");
            vb.Sign("yc_dress", new Vector3(-5.6f, 0.15f, w.yMin - 0.15f), 2.2f, 180f, frame: "navy");
            vb.Model("crypto_atm", 9.6f, -6.8f, 180f, screen: "yc_govfee", glow: "glowcyan");
            vb.Solid(9.6f, -6.8f, 0.9f, 0.9f, 1.9f);
            foreach (var p in new[] { new Vector2(-11f, 7f), new Vector2(11f, 7f), new Vector2(11f, -7f), new Vector2(-11f, -7f) })
            {
                vb.Model("planter_round", p.x, p.y, 0f, scale: 0.7f);
                vb.Tree(p.x > 0 ? "palm_1" : "palm_2", p.x, p.y, 0.8f);
            }
            // String lights along the north rail.
            for (int i = 0; i < 18; i++)
                vb.Sphere(new Vector3(w.xMin + 0.5f + i * 1.35f, 2.2f + Mathf.Sin(i * 0.9f) * 0.12f, w.yMax + 0.3f), 0.07f, "glowwhite", glow: true, rings: 3, segments: 5);
            foreach (float x in new[] { w.xMin - 0.3f, w.xMax + 0.3f }) vb.Cylinder(new Vector3(x, 0, w.yMax + 0.3f), 0.05f, 2.3f, "chrome", 6);
            vb.Pipe(new Vector3(w.xMin - 0.3f, 2.25f, w.yMax + 0.3f), new Vector3(w.xMax + 0.3f, 2.25f, w.yMax + 0.3f), 0.01f, "black", 4);
            vb.Backdrop = true;
            vb.Model("car_sedan", -9f, w.yMin - 4.5f, 90f, recolor: "ctorange");
            vb.Model("car_suv", 9f, w.yMin - 4.5f, -90f, recolor: "white");
            Skyline(vb, 46f, 62f, 52f);
            foreach (float z in new[] { 6f, 24f })
            {
                vb.Model(Towers[(int)z % 5], -42f, z, 90f, scale: 0.85f);
                vb.Model(Towers[((int)z + 2) % 5], 42f, z, -90f, scale: 0.85f);
            }
            vb.Backdrop = false;
            vb.FloorSign("exit", vb.Spot("exit").x, w.yMin + 1.0f, 1.6f);
            vb.PointLight(new Vector3(-9.0f, 2.6f, -1.75f), "glowcyan", 1.2f, 6f);
            vb.PointLight(new Vector3(0, 3f, 4f), "glowamber", 1.2f, 10f);
        }

        // ------------------------------------------------------------------ Penthouse Row
        // Rooftop row of three penthouse fronts (PH-1..3, rent $1,500/week in game Cash), concierge desk, infinity
        // pool with loungers, fire-bowl sofa lounge, glass balustrade and the marina skyline at dusk.
        public static void PenthouseRow(VB vb)
        {
            var w = vb.Walk;
            vb.Floor("floor_marble_2m", Rect.MinMaxRect(w.xMin - 1f, w.yMin - 4f, w.xMax + 1f, w.yMax + 1f));
            vb.Backdrop = true;
            vb.Box(new Vector3(0, -6.12f, 0), new Vector3(w.width + 2.2f, 12f, w.height + 6f), "darkmarble");
            vb.Box(new Vector3(0, -30f, 20f), new Vector3(200f, 1f, 200f), "deepwater");
            vb.Backdrop = false;
            // Penthouse fronts along the back.
            float back = w.yMax + 0.6f;
            string[] numbers = { "pr_ph1", "pr_ph2", "pr_ph3" };
            int i = 0;
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("door_")))
            {
                vb.Box(new Vector3(s.x, 2.4f, back + 2.0f), new Vector3(6.4f, 4.8f, 4.0f), i == 1 ? "white" : "offwhite", collider: true);
                vb.Box(new Vector3(s.x, 1.25f, back - 0.02f), new Vector3(1.6f, 2.5f, 0.06f), "ink");
                vb.Box(new Vector3(s.x, 2.55f, back - 0.04f), new Vector3(1.8f, 0.1f, 0.08f), "gold");
                foreach (float o in new[] { -0.85f, 0.85f }) vb.Box(new Vector3(s.x + o, 1.3f, back - 0.04f), new Vector3(0.08f, 2.6f, 0.08f), "gold");
                foreach (float o in new[] { -2.2f, 2.2f })
                {
                    vb.Box(new Vector3(s.x + o, 2.2f, back - 0.02f), new Vector3(1.4f, 2.6f, 0.05f), "glowamber", glow: true);
                    vb.Box(new Vector3(s.x + o, 2.2f, back - 0.03f), new Vector3(0.06f, 2.6f, 0.06f), "charcoal");
                }
                vb.Sign(numbers[i], new Vector3(s.x, 2.75f, back - 0.05f), 0.9f, 180f);
                vb.Box(new Vector3(s.x, 4.9f, back + 1.9f), new Vector3(6.8f, 0.2f, 4.4f), "gold");
                vb.Decal(s.x, s.z - 0.6f, 1.8f, 1.2f, "charcoal");
                i++;
            }
            vb.Sign("pr_title", new Vector3(0, 5.1f, back - 0.2f), 6.0f, 180f);
            vb.Box(new Vector3(2.6f, 0.75f, -6.45f), new Vector3(2.8f, 1.5f, 0.1f), "charcoal", solid: true);
            vb.Sign("pr_rent", new Vector3(2.6f, 0.25f, -6.52f), 2.6f, 180f);

            // Infinity pool (obstacle) with coping and an east infinity edge.
            var pool = Rect.MinMaxRect(5f, -5.6f, 11.2f, 1.6f);
            vb.Box(new Vector3(pool.center.x, -0.02f, pool.center.y), new Vector3(pool.width, 0.02f, pool.height), "pool");
            foreach (var e in new[] { (new Vector3(pool.center.x, 0.05f, pool.yMin), new Vector3(pool.width + 0.6f, 0.1f, 0.3f)),
                                      (new Vector3(pool.center.x, 0.05f, pool.yMax), new Vector3(pool.width + 0.6f, 0.1f, 0.3f)),
                                      (new Vector3(pool.xMin, 0.05f, pool.center.y), new Vector3(0.3f, 0.1f, pool.height)) })
                vb.Box(e.Item1, e.Item2, "white");
            vb.Box(new Vector3(pool.xMax + 0.1f, 0.0f, pool.center.y), new Vector3(0.1f, 0.03f, pool.height), "glowcyan", glow: true);
            vb.Obstacle(new Vector3(pool.center.x, 0.4f, pool.center.y), new Vector3(pool.width, 0.8f, pool.height));
            vb.Collider(new Vector3(pool.center.x, 0.05f, pool.center.y), new Vector3(pool.width, 0.1f, pool.height));
            vb.Sign("pr_pool", new Vector3(8.1f, 0.12f, pool.yMin - 0.2f), 2.8f, 180f, frame: "white");
            vb.Solid(8.1f, pool.yMin - 0.15f, 2.9f, 0.2f, 1.1f);
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("lounger_")))
                vb.Seat(s, "lounge_chair", "white", back: 0.1f, approachBack: 0.8f);

            // Fire-bowl lounge (sofa_1..2) and concierge desk.
            vb.Model("lounge_sofa", -10.0f, -2.1f, 90f, recolor: "linen");
            vb.Obstacle(new Vector3(-10.35f, 0.45f, -2.1f), new Vector3(0.35f, 0.9f, 2.2f));
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("sofa_")))
            {
                vb.SeatCollider(s, new Vector3(0, 0.6f, -0.3f), new Vector3(0.9f, 0.5f, 0.7f));
                vb.Approach(s, VB.Rel(s, 0, 0.85f));
            }
            vb.Cylinder(new Vector3(-7.2f, 0, -2.1f), 0.55f, 0.45f, "darkmarble", 12, solid: true);
            vb.Cylinder(new Vector3(-7.2f, 0.45f, -2.1f), 0.42f, 0.18f, "glowamber", 10, glow: true, topRadius: 0.1f);
            vb.PointLight(new Vector3(-7.2f, 1.0f, -2.1f), "glowamber", 1.4f, 5f);
            var con = vb.Spot("concierge");
            var d = VB.Rel(con, 0, 0.9f);
            vb.Counter(d.x, d.z, 2.4f, 0.6f, 0f, "white", "gold", "glowgold");
            vb.Sign("pr_concierge", new Vector3(d.x, 0.15f, d.z - 0.32f), 2.2f, 180f);
            // Glass balustrade around the open sides.
            foreach (var (a, b) in new[] { (new Vector2(w.xMin - 0.3f, w.yMin - 0.3f), new Vector2(w.xMin - 0.3f, w.yMax + 0.3f)),
                                            (new Vector2(w.xMax + 0.3f, w.yMin - 0.3f), new Vector2(w.xMax + 0.3f, w.yMax + 0.3f)),
                                            (new Vector2(w.xMin - 0.3f, w.yMin - 0.3f), new Vector2(-2.2f, w.yMin - 0.3f)),
                                            (new Vector2(2.2f, w.yMin - 0.3f), new Vector2(w.xMax + 0.3f, w.yMin - 0.3f)) })
            {
                vb.Rail(a, b, 1.05f, "chrome", 2.5f, true, 1);
                var mid = (a + b) * .5f; var dd = b - a;
                vb.Box(new Vector3(mid.x, 0.55f, mid.y), new Vector3(Mathf.Abs(dd.x) + 0.02f, 0.9f, Mathf.Abs(dd.y) + 0.02f), "skyblue");
            }
            foreach (var p in new[] { new Vector2(-11f, 7.6f), new Vector2(11f, 7.6f), new Vector2(-11f, -8f), new Vector2(11.2f, -7.8f) })
            {
                vb.Model("planter_round", p.x, p.y, 0f, scale: 0.65f);
                vb.Tree("palm_2", p.x, p.y, 0.7f);
            }
            vb.Backdrop = true;
            Skyline(vb, 34f, 48f, 60f);
            foreach (float z in new[] { -6f, 14f })
            {
                vb.Model(Towers[((int)z + 7) % 5], -36f, z, 90f, scale: 0.9f);
                vb.Model(Towers[((int)z + 9) % 5], 36f, z, -90f, scale: 0.9f);
            }
            vb.Backdrop = false;
            vb.FloorSign("exit", vb.Spot("exit").x, w.yMin + 1.0f, 1.6f);
            vb.PointLight(new Vector3(0, 3.2f, 5f), "glowamber", 1.4f, 10f);
            vb.PointLight(new Vector3(8f, 1.2f, -2f), "glowcyan", 1.2f, 6f);
        }
    }
}
