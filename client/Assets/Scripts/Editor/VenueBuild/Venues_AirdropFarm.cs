using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>Airdrop Farm district: ORE Refinery, Points Fields (airdrop_fields), Sybil Barn, Checker Kiosk.</summary>
    public static class VenuesAirdropFarm
    {
        // ------------------------------------------------------------------ ORE Refinery
        // Refinery hall: giant live 5×5 ORE board with round timer and motherlode counter (live hooks), furnace bay
        // whose glow is a tintable hook (heat = city-wide staked ORE), conveyors with glowing ore, four stake/unstake/
        // claim/compound terminals, miner post with hard-hat badges, and the "mine on ore.com, not here" notice.
        public static void OreRefinery(VB vb)
        {
            vb.InteriorShell("floor_concrete_2m", "darksteel", wallHeight: 9.2f, sideHeight: 7.0f, eastHeight: 0f, trim: "ctgold");
            var w = vb.Walk;
            float back = vb.Inner.yMax, east = w.xMax + 0.2f;
            // East wall with an open furnace bay between z = 5 and 11.
            vb.Wall(new Vector2(east, w.yMin - 0.35f), new Vector2(east, 5.0f), 7.0f, "darksteel", 0.3f);
            vb.Wall(new Vector2(east, 11.0f), new Vector2(east, w.yMax + 0.35f), 7.0f, "darksteel", 0.3f);
            vb.Box(new Vector3(east, 6.6f, 8f), new Vector3(0.3f, 0.8f, 6f), "darksteel");
            vb.Floor("floor_metal_2m", Rect.MinMaxRect(-8f, 6f, 8f, 12f), 0.002f);
            for (int i = 0; i < 12; i++)
                vb.Decal(11.6f, 4.6f + i * 0.62f, 0.5f, 0.3f, i % 2 == 0 ? "ctgold" : "charcoal", 45f);

            // The board.
            vb.OreBoard(new Vector3(0, 1.3f, back - 0.12f), 6.0f, 180f);
            vb.Box(new Vector3(0, 1.0f, back - 0.2f), new Vector3(7.0f, 0.08f, 0.1f), "glowgold", glow: true);
            vb.Box(new Vector3(0, 7.85f, back - 0.2f), new Vector3(7.0f, 0.08f, 0.1f), "glowgold", glow: true);
            vb.OnWall('N', 0, 8.05f, 6.0f, "ore_board_label");
            vb.OnWall('N', -6.6f, 2.6f, 3.4f, "ore_timer_idle", hook: "OreRoundTimer");
            vb.OnWall('N', 6.6f, 2.6f, 3.4f, "ore_mother_idle", hook: "OreMotherlode");
            vb.OnWall('W', 0, 4.6f, 8.6f, "ore_title");
            vb.OnWall('W', -5.5f, 1.2f, 2.4f, "ore_tiers", frame: "ctgold");
            vb.OnWall('W', -9.2f, 1.4f, 4.0f, "ore_mine_link", frame: "charcoal");
            vb.PointLight(new Vector3(0, 6.5f, back - 2.5f), "glowgold", 2.2f, 12f);

            // Furnace bay.
            vb.Model("ore_furnace", 15.6f, 8f, -90f, scale: 1.25f, glow: "oreglow");
            vb.Solid(15.6f, 8f, 4.2f, 4.2f, 8f);
            vb.HookGlow("FurnaceGlow", new Vector3(13.42f, 1.7f, 8f), new Vector3(0.12f, 2.2f, 2.4f), "oreglow");
            vb.RingDecal(new Vector3(15.6f, 0, 8f), 2.7f, 0.14f, "glowamber", 40, glow: true);
            vb.Sign("ore_furnace_label", new Vector3(east - 0.18f, 2.0f, 2.6f), 3.6f, -90f, frame: "charcoal");
            vb.PointLight(new Vector3(13.2f, 2.0f, 8f), "oreglow", 3.2f, 11f);
            vb.ModelAt("ore_crystal_pile", new Vector3(13.4f, 0, 5.4f), 200f, glow: "oreglow");
            vb.ModelAt("ore_crystal_pile", new Vector3(13.6f, 0, 10.6f), 120f, glow: "oreglow");
            vb.Solid(13.4f, 5.4f, 1.5f, 1.6f, 0.8f);
            vb.Solid(13.6f, 10.6f, 1.5f, 1.6f, 0.8f);

            // Conveyor line along the back wall feeding the furnace, glowing ore riding on it.
            foreach (float x in new[] { 5.0f, 9.0f })
            {
                vb.Model("factory_conveyor_long", x, back - 1.1f, 180f);
                vb.Solid(x, back - 1.1f, 4.0f, 2.0f, 0.8f);
                vb.ModelAt("ore_crystal_pile", new Vector3(x - 0.8f, 0.8f, back - 1.1f), 30f, 0.45f, glow: "oreglow");
                vb.ModelAt("ore_crystal_pile", new Vector3(x + 1.0f, 0.8f, back - 1.1f), 150f, 0.4f, glow: "oreglow");
            }
            vb.Model("factory_hopper_round", -12.6f, 10.4f, 180f);
            vb.Solid(-12.6f, 10.4f, 2.9f, 2.9f, 2.6f);
            vb.Model("factory_crane", -7.6f, back - 1.4f, 180f);
            vb.Solid(-7.6f, back - 1.4f, 2.7f, 2.2f, 5f);
            vb.Model("factory_machine_window", 12.9f, -8.5f, -90f);
            vb.Solid(12.9f, -8.5f, 2.5f, 2.0f, 2.2f);
            vb.Model("res_gold_nuggets", -13.4f, -9.5f, 0f, scale: 1.4f);
            vb.Model("res_pallet_wood", -13.4f, -7.8f, 0f);
            vb.Solid(-13.4f, -8.6f, 1.4f, 3f, 0.6f);
            foreach (float z in new[] { -10f, -2f, 6f })
                vb.Model("pipes_industrial", vb.Inner.xMin + 0.2f, z, 90f, y: 6.2f, scale: 1.5f);

            // Stake terminals (contract terminal_1..4) with the refinery kiosk beside each desk.
            foreach (var s in vb.SpotsWhere(s => s.kind == "terminal"))
            {
                vb.Terminal(s, "ore_stake_screen", chairColor: "ctgold");
                var k = VB.Rel(s, 1.75f, 0.75f);
                vb.ModelAt("stake_terminal", k, VB.Deg(s) + 180f, screen: "ore_stake_screen");
                vb.Solid(k.x, k.z, 0.9f, 0.7f, 1.7f);
            }
            // Miner post (NPC talks up the board): hard hats rack and badge sign.
            var miner = vb.Spot("miner_post");
            vb.OnWall('W', miner.z, 2.2f, 2.8f, "ore_hardhat");
            for (int i = 0; i < 4; i++)
                vb.Blob(new Vector3(vb.Inner.xMin + 0.25f, 1.45f, miner.z - 0.9f + i * 0.6f), new Vector3(0.36f, 0.22f, 0.36f), "gold");
            vb.Box(new Vector3(vb.Inner.xMin + 0.18f, 1.32f, miner.z), new Vector3(0.1f, 0.05f, 2.6f), "darksteel");
            vb.Pipe(new Vector3(miner.x - 1.1f, 0.05f, miner.z + 1.2f), new Vector3(miner.x - 1.4f, 1.2f, miner.z + 1.5f), 0.04f, "walnut", 6);
            vb.Box(new Vector3(miner.x - 1.4f, 1.25f, miner.z + 1.5f), new Vector3(0.5f, 0.08f, 0.08f), "steel", 30f);

            // Outside: chimney and water tower above the walls.
            vb.Backdrop = true;
            vb.Model("ind_chimney_large", 9f, 19f, 0f, scale: 0.8f);
            vb.Model("ind_water_tower", -11f, 17.5f, 0f);
            vb.Backdrop = false;
            vb.FloorSign("exit", vb.Spot("exit").x, w.yMin + 1.0f, 1.6f);
            vb.PointLight(new Vector3(-6f, 5.5f, -3f), "glowwhite", 1.5f, 14f);
            vb.PointLight(new Vector3(6f, 5.5f, -3f), "glowwhite", 1.5f, 14f);
            vb.PointLight(new Vector3(-12f, 3f, 6f), "glowamber", 1.0f, 6f);
        }

        // ------------------------------------------------------------------ Points Fields
        // Open-air points farm: crop beds of glowing "points sprouts", Season ∞ leaderboard (live hook) on a field
        // billboard, airdrop crates (one still parachuting in), Sybil scarecrow, season mile posts, claim/mindshare
        // signs, fences, trees, water tower and the barn on the horizon.
        public static void PointsFields(VB vb)
        {
            var w = vb.Walk;
            vb.Box(new Vector3(0, -0.06f, 0), new Vector3(w.width + 3f, 0.1f, w.height + 3f), "grass");
            vb.Backdrop = true;
            vb.Box(new Vector3(0, -0.08f, 4f), new Vector3(60f, 0.1f, 50f), "grass");
            vb.Backdrop = false;
            vb.Decal(0, -4f, 2.6f, 12.2f, "soil");
            vb.Decal(0, 1.5f, 18f, 2.0f, "soil");
            vb.Rail(new Vector2(w.xMin - 0.3f, w.yMin - 0.3f), new Vector2(-2.2f, w.yMin - 0.3f), 1.1f, "oak", 2.2f, true);
            vb.Rail(new Vector2(2.2f, w.yMin - 0.3f), new Vector2(w.xMax + 0.3f, w.yMin - 0.3f), 1.1f, "oak", 2.2f, true);
            vb.Rail(new Vector2(w.xMin - 0.3f, w.yMin - 0.3f), new Vector2(w.xMin - 0.3f, w.yMax + 0.3f), 1.1f, "oak", 2.2f, true);
            vb.Rail(new Vector2(w.xMax + 0.3f, w.yMin - 0.3f), new Vector2(w.xMax + 0.3f, w.yMax + 0.3f), 1.1f, "oak", 2.2f, true);
            vb.Rail(new Vector2(w.xMin - 0.3f, w.yMax + 0.3f), new Vector2(w.xMax + 0.3f, w.yMax + 0.3f), 1.1f, "oak", 2.2f, true);
            // Entrance arch with the title.
            foreach (float x in new[] { -2.4f, 2.4f }) vb.Box(new Vector3(x, 1.8f, w.yMin - 0.3f), new Vector3(0.22f, 3.6f, 0.22f), "oak", collider: true);
            vb.Box(new Vector3(0, 3.65f, w.yMin - 0.3f), new Vector3(5.4f, 0.25f, 0.25f), "oak");
            vb.Sign("pf_title", new Vector3(0, 3.8f, w.yMin - 0.45f), 5.0f, 180f);

            // Crop beds with points sprouts (glowing coins on stalks).
            foreach (float x in new[] { -7f, 0f, 7f })
            {
                vb.Box(new Vector3(x, 0.15f, 7.2f), new Vector3(5.6f, 0.3f, 3.4f), "soil", solid: true);
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 6; c++)
                    {
                        float px = x - 2.3f + c * 0.92f, pz = 6.2f + r * 1.0f;
                        vb.Cylinder(new Vector3(px, 0.3f, pz), 0.025f, 0.45f, "leaf", 5);
                        vb.Cylinder(new Vector3(px, 0.72f, pz - 0.03f), 0.12f, 0.04f, (r + c) % 3 == 0 ? "glowgold" : "gold", 10, glow: (r + c) % 3 == 0);
                    }
            }
            // Scarecrow in the middle bed.
            vb.Cylinder(new Vector3(0, 0.3f, 8.2f), 0.06f, 2.1f, "oak", 6);
            vb.Box(new Vector3(0, 1.75f, 8.2f), new Vector3(1.6f, 0.1f, 0.1f), "oak");
            vb.Box(new Vector3(0, 1.45f, 8.2f), new Vector3(0.55f, 0.75f, 0.3f), "denim");
            vb.Sphere(new Vector3(0, 2.15f, 8.2f), 0.22f, "hay");
            vb.Cylinder(new Vector3(0, 2.28f, 8.2f), 0.4f, 0.04f, "hay", 12);
            vb.Cylinder(new Vector3(0, 2.32f, 8.2f), 0.2f, 0.25f, "hay", 10, topRadius: 0.05f);
            vb.Sign("pf_scarecrow", new Vector3(0, 1.2f, 8.04f), 0.9f, 180f);

            // Leaderboard billboard behind the beds.
            foreach (float x in new[] { -4.3f, 4.3f }) vb.Cylinder(new Vector3(x, 0, w.yMax + 1.6f), 0.15f, 2.4f, "oak", 8);
            vb.HookPanel("PointsLeaderboard", "pf_board_idle", new Vector3(0, 2.2f, w.yMax + 1.45f), 8.4f, 180f, frame: "oak");

            // Crates, benches, mile posts and signs.
            vb.AirdropCrate(-9.6f, 1.6f);
            vb.AirdropCrate(9.4f, 2.4f, canopy: "ctteal");
            vb.AirdropCrate(4.2f, -2.6f, 3.4f, "ctgold", solid: false);
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("bench_")))
                vb.Seat(s, "park_bench", back: 0.12f, approachBack: -0.7f);
            string[] seasons = { "pf_season1", "pf_season2", "pf_season3" };
            for (int i = 0; i < 3; i++)
            {
                float z = -7f + i * 4f;
                vb.Cylinder(new Vector3(2.2f, 0, z), 0.07f, 1.3f, "oak", 6);
                vb.Sign(seasons[i], new Vector3(2.2f, 1.3f, z - 0.05f), 1.4f, 180f, frame: "oak");
            }
            foreach (var (x, sign) in new[] { (-6.5f, "pf_farm"), (6.5f, "pf_claim") })
            {
                foreach (float o in new[] { -1.1f, 1.1f }) vb.Cylinder(new Vector3(x + o, 0, w.yMin + 0.6f), 0.07f, 1.0f, "oak", 6);
                vb.Sign(sign, new Vector3(x, 0.9f, w.yMin + 0.55f), 2.6f, 180f, frame: "oak");
                vb.Solid(x, w.yMin + 0.6f, 2.6f, 0.3f, 1.6f);
            }
            vb.Cylinder(new Vector3(w.xMax - 0.6f, 0, -1.5f), 0.07f, 1.4f, "oak", 6);
            vb.Sign("pf_mindshare", new Vector3(w.xMax - 0.65f, 1.3f, -1.5f), 2.0f, -90f, frame: "oak");
            foreach (var p in new[] { new Vector2(-10.6f, -8.6f), new Vector2(10.6f, -8.6f), new Vector2(-10.4f, 4.4f) })
                vb.HayBale(p.x, p.y, 15f);
            vb.Solid(-10.6f, -8.6f, 1.2f, 0.7f, 0.6f);
            vb.Solid(10.6f, -8.6f, 1.2f, 0.7f, 0.6f);
            vb.Solid(-10.4f, 4.4f, 1.2f, 0.7f, 0.6f);

            // Horizon: trees, water tower, the Sybil barn.
            // Low-poly map-kit trees for the horizon ring (backdrop); two hero blossoms near the fence.
            vb.Backdrop = true;
            int t = 0;
            for (float x = -24f; x <= 24f; x += 4f)
                vb.Model(t++ % 2 == 0 ? "map_tree_large" : "map_tree_small", x, w.yMax + 7f + (t % 3) * 2.2f, 0f, scale: 1.3f);
            for (float z = -12f; z <= 10f; z += 4f)
            {
                vb.Model(t++ % 2 == 0 ? "map_tree_large" : "map_tree_small", w.xMin - 5f - (t % 2) * 2f, z, 0f, scale: 1.3f);
                vb.Model(t++ % 2 == 0 ? "map_tree_small" : "map_tree_large", w.xMax + 5f + (t % 2) * 2f, z + 1f, 0f, scale: 1.3f);
            }
            vb.Model("ind_water_tower", -17f, 17f, 0f);
            vb.Box(new Vector3(17f, 3f, 20f), new Vector3(9f, 6f, 7f), "barnred");
            vb.Box(new Vector3(17f, 6.6f, 20f), new Vector3(9.4f, 1.4f, 7.4f), "darkwood");
            vb.Box(new Vector3(17f, 2f, 16.45f), new Vector3(3f, 4f, 0.1f), "white");
            vb.Backdrop = false;
            vb.Tree("tree_blossom_4", w.xMin - 2.6f, -6f, 0.8f, obstacle: false);
            vb.Tree("tree_blossom_4", w.xMax + 2.6f, 5f, 0.8f, obstacle: false);
            foreach (var p in new[] { new Vector2(-12.5f, -1f), new Vector2(12.5f, -6f), new Vector2(-12.5f, 6f) })
                vb.Model("bush_large_flowers", p.x, p.y, 0f);
            vb.FloorSign("exit", vb.Spot("exit").x, w.yMin + 1.0f, 1.6f);
        }

        // ------------------------------------------------------------------ Sybil Barn
        // Red barn: three clone pens of identical wallet-farmers (labelled by wallet range), phone-farm rack,
        // spreadsheet desk, WANTED: SYBIL SQUAD poster, hay-bale seats, lanterns.
        public static void SybilBarn(VB vb)
        {
            vb.InteriorShell("floor_wood_2m", "barnred", wallHeight: 6.0f, sideHeight: 4.6f, trim: "white");
            float back = vb.Inner.yMax;
            // Gable and white X-braces on the back wall.
            for (int i = 0; i < 6; i++)
            {
                float width = 24.6f - i * 4.2f;
                vb.Box(new Vector3(0, 6.3f + i * 0.6f, back + 0.15f), new Vector3(width, 0.6f, 0.3f), "barnred");
            }
            vb.Box(new Vector3(0, 5.0f, back - 0.03f), new Vector3(24f, 0.16f, 0.05f), "white");
            vb.Box(new Vector3(0, 0.5f, back - 0.03f), new Vector3(24f, 0.12f, 0.05f), "white");
            vb.OnWall('N', 0, 3.9f, 6.4f, "sb_title");

            // Clone pens.
            float[][] pens = { new[] { -11f, -4.3f }, new[] { -3.4f, 3.4f }, new[] { 4.3f, 11f } };
            string[] labels = { "sb_pen1", "sb_pen2", "sb_pen3" };
            for (int p = 0; p < 3; p++)
            {
                float x0 = pens[p][0], x1 = pens[p][1];
                vb.Rail(new Vector2(x0, 5.4f), new Vector2(x1, 5.4f), 1.1f, "oak", 1.7f, true, 3);
                vb.Rail(new Vector2(x0, 5.4f), new Vector2(x0, back), 1.1f, "oak", 1.7f, true, 3);
                vb.Rail(new Vector2(x1, 5.4f), new Vector2(x1, back), 1.1f, "oak", 1.7f, true, 3);
                vb.Decal((x0 + x1) * .5f, (5.4f + back) * .5f, x1 - x0, back - 5.4f, "hay");
                vb.Sign(labels[p], new Vector3((x0 + x1) * .5f, 1.18f, 5.32f), 2.8f, 180f, frame: "oak");
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 4; c++)
                        vb.Figure(x0 + 0.9f + c * (x1 - x0 - 1.8f) / 3f, 6.4f + r * 1.15f, "ctgreen", "blush", 0.85f, accent: "screenbg");
            }

            // Phone farm rack on the west wall.
            float wx = vb.Inner.xMin + 0.3f;
            vb.Box(new Vector3(wx, 1.0f, 0f), new Vector3(0.5f, 2.0f, 3.6f), "darkwood", solid: true);
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 9; c++)
                    vb.Box(new Vector3(wx + 0.26f, 0.35f + r * 0.45f, -1.6f + c * 0.4f), new Vector3(0.02f, 0.3f, 0.16f), (r + c) % 4 == 0 ? "glowcyan" : "glowgreen", glow: true);
            vb.OnWall('W', 0f, 2.4f, 3.4f, "sb_phonefarm");
            // Farmer desk with the spreadsheet.
            vb.Model("home_desk", 10.6f, 2.0f, -90f);
            vb.Solid(10.6f, 2.0f, 0.8f, 1.6f, 0.8f);
            vb.Model("monitor_triple", 10.75f, 2.0f, -90f, y: 0.76f, screen: "sb_sheet");
            vb.Model("home_chair_1", 9.8f, 2.0f, 90f);
            vb.OnWall('E', -1.6f, 1.2f, 2.0f, "sb_wanted", frame: "oak");
            vb.OnWall('W', -6.0f, 1.6f, 3.4f, "sb_rule", frame: "oak");

            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("hay_")))
            {
                vb.HayBale(VB.Rel(s, 0, -0.1f).x, VB.Rel(s, 0, -0.1f).z, VB.Deg(s) + 90f);
                vb.SeatCollider(s, new Vector3(0, 0.45f, -0.1f), new Vector3(1.0f, 0.3f, 0.5f));
                vb.Approach(s, VB.Rel(s, 0, 0.75f));
            }
            foreach (var p in new[] { new Vector3(-10.8f, 0, -8.6f), new Vector3(10.8f, 0, -8.6f), new Vector3(-10.8f, 0.6f, -8.6f), new Vector3(10.6f, 0, -6.9f) })
                vb.HayBale(p.x, p.z, 0f, p.y);
            vb.Solid(-10.8f, -8.6f, 1.2f, 0.7f, 1.2f);
            vb.Solid(10.8f, -7.8f, 1.2f, 2.2f, 0.6f);
            // Pitchfork and lanterns.
            vb.Pipe(new Vector3(7.6f, 0.02f, -9.5f), new Vector3(7.2f, 1.9f, -9.75f), 0.035f, "walnut", 6);
            foreach (float x in new[] { -8f, 0f, 8f })
            {
                vb.Pipe(new Vector3(x, 4.6f, 1.5f), new Vector3(x, 3.8f, 1.5f), 0.015f, "black", 4);
                vb.Sphere(new Vector3(x, 3.65f, 1.5f), 0.18f, "glowamber", glow: true);
                vb.PointLight(new Vector3(x, 3.5f, 1.5f), "glowamber", 1.2f, 8f);
            }
            vb.Box(new Vector3(0, 4.7f, 1.5f), new Vector3(20f, 0.2f, 0.2f), "darkwood");
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 1.0f, 1.6f);
            vb.PointLight(new Vector3(0, 3.2f, 7.5f), "glowwhite", 1.0f, 9f);
        }

        // ------------------------------------------------------------------ Checker Kiosk
        // Airdrop eligibility plaza: three checker kiosks under a canopy, big ELIGIBILITY board (live hook) on the roof,
        // queue stanchions ("est. wait: forever"), NOT ELIGIBLE (lol) board, tier poster, cope benches, confetti.
        public static void CheckerKiosk(VB vb)
        {
            var w = vb.Walk;
            vb.Floor("plaza_paving_2m", Rect.MinMaxRect(w.xMin - 4f, w.yMin - 5f, w.xMax + 4f, w.yMax + 4f));
            vb.Backdrop = true;
            vb.Box(new Vector3(0, -0.07f, 0), new Vector3(60f, 0.1f, 50f), "grass");
            vb.Backdrop = false;
            // Canopy structure behind the kiosk spots.
            float cz = w.yMax + 0.6f;
            vb.Box(new Vector3(0, 1.6f, cz), new Vector3(14f, 3.2f, 0.25f), "white", solid: true);
            vb.Box(new Vector3(0, 3.3f, cz - 1.2f), new Vector3(14.6f, 0.2f, 2.8f), "ctgreen");
            vb.Box(new Vector3(0, 3.16f, cz - 2.55f), new Vector3(14.6f, 0.08f, 0.08f), "glowgreen", glow: true);
            foreach (float x in new[] { -7f, 7f }) vb.Box(new Vector3(x, 1.65f, cz - 2.4f), new Vector3(0.18f, 3.3f, 0.18f), "chrome", solid: true);
            vb.Sign("ck_title", new Vector3(0, 2.65f, cz - 2.62f), 5.6f, 180f);
            foreach (float x in new[] { -3.6f, 3.6f }) vb.Box(new Vector3(x, 4.2f, cz - 0.4f), new Vector3(0.15f, 1.8f, 0.15f), "darksteel");
            vb.HookPanel("EligibilityBoard", "ck_main_idle", new Vector3(0, 3.5f, cz - 0.55f), 6.0f, 180f);
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("kiosk_")))
            {
                var k = VB.Rel(s, 0, 1.0f);
                vb.ModelAt("ad_kiosk", k, VB.Deg(s) + 180f, screen: "ck_kiosk");
                vb.Solid(k.x, k.z, 1.5f, 0.6f, 2.7f);
                vb.Decal(s.x, s.z, 1.2f, 0.8f, "ctgreen");
            }
            // Queue: snake of stanchions from the entrance towards the kiosks (decor, non-blocking).
            for (int i = 0; i < 3; i++)
            {
                float z = -3.2f + i * 1.6f;
                vb.Stanchions(new Vector2(-3.2f, z), new Vector2(-0.4f, z));
                vb.Stanchions(new Vector2(0.4f, z), new Vector2(3.2f, z));
            }
            var qs = new Vector3(-4.6f, 0, -4.4f);
            vb.Cylinder(qs, 0.06f, 1.2f, "chrome", 6);
            vb.Sign("ck_queue", qs + new Vector3(0, 1.2f, -0.03f), 1.8f, 180f, frame: "charcoal");
            foreach (var (x, sign, facing) in new[] { (7.4f, "ck_noteligible", -90f), (-7.4f, "ck_tiers", 90f) })
            {
                vb.Box(new Vector3(x, 1.3f, 2.4f), new Vector3(0.12f, 2.6f, 2.6f), "charcoal", solid: true);
                vb.Sign(sign, new Vector3(x + (facing < 0 ? -0.07f : 0.07f), 0.5f, 2.4f), 2.4f, facing);
            }
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("bench_")))
                vb.Seat(s, "park_bench", back: 0.12f, approachBack: -0.7f);
            var cope = vb.Spot("bench_1");
            vb.Sign("ck_cope", VB.Rel(cope, 0, -0.75f, 0.95f), 1.6f, VB.Deg(cope));
            vb.Box(VB.Rel(cope, 0, -0.8f, 0.5f), new Vector3(1.7f, 1.0f, 0.06f), "slate", VB.Deg(cope));
            var rnd = new System.Random(4);
            string[] confetti = { "glowgold", "glowpink", "glowgreen", "glowcyan" };
            for (int i = 0; i < 40; i++)
                vb.Decal((float)(rnd.NextDouble() * 12 - 6), (float)(rnd.NextDouble() * 4 + 0.5), 0.12f, 0.06f, confetti[i % 4], (float)(rnd.NextDouble() * 180), glow: true);
            foreach (var p in new[] { new Vector2(-10f, -4f), new Vector2(10f, -4f), new Vector2(-10f, 4f), new Vector2(10f, 4f) })
            {
                vb.Model("planter_round", p.x, p.y, 0f);
                vb.Tree("tree_blossom_4", p.x, p.y, 0.7f, obstacle: false);
            }
            vb.Model("crypto_atm", -6.6f, -5.0f, 0f, screen: "ck_kiosk", glow: "glowgreen");
            vb.Solid(-6.6f, -5.0f, 0.9f, 0.9f, 1.9f);
            vb.FloorSign("exit", vb.Spot("exit").x, w.yMin + 1.0f, 1.6f);
            vb.PointLight(new Vector3(0, 2.9f, 4.4f), "glowgreen", 1.4f, 8f);
        }
    }
}
