using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>The Trenches district: Perp Pit, Rekt Clinic, Launchpad Tower, Prediction Pit.</summary>
    public static class VenuesTrenches
    {
        // ------------------------------------------------------------------ Perp Pit
        // Perp-DEX trading floor: two terminal ribbons facing a leverage board and a 24h casualties board (live hooks),
        // a flush liquidation witness circle, CROSS / ISOLATED margin lounges, lever-o-meter and funding art.
        public static void PerpPit(VB vb)
        {
            vb.InteriorShell("floor_concrete_2m", "darkconcrete", wallHeight: 6.6f, sideHeight: 5.2f, frontHeight: 0.9f);
            vb.Floor("floor_metal_2m", Rect.MinMaxRect(-12f, 2.5f, 12f, 10f), 0.002f);

            vb.OnWall('N', 0, 0.75f, 26f, "pp_ticker", height: 0.42f);
            vb.OnWall('N', -2.2f, 1.7f, 7.6f, "pp_leverage_idle", hook: "LeverageBoard");
            vb.OnWall('N', 6.1f, 1.7f, 6.4f, "pp_liq_idle", hook: "LiquidationBoard");
            vb.OnWall('N', -10.1f, 1.8f, 5.0f, "pp_heatmap", frame: "charcoal");
            vb.OnWall('N', 11.6f, 1.9f, 3.0f, "pp_funding", frame: "charcoal");
            vb.OnWall('N', 0, 5.6f, 7.5f, "pp_title");
            vb.Box(new Vector3(0, 0.05f, vb.Inner.yMax - 0.08f), new Vector3(26f, 0.06f, 0.06f), "glowred", glow: true);

            foreach (var s in vb.SpotsWhere(s => s.kind == "terminal"))
                vb.Terminal(s, "pp_terminal", chairColor: "charcoal");
            foreach (float z in new[] { 4f, 8f })
            {
                vb.Box(new Vector3(0, 0.25f, z + 1.3f), new Vector3(21f, 0.5f, 0.14f), "darksteel");
                vb.Box(new Vector3(0, 0.02f, z + 0.38f), new Vector3(21f, 0.03f, 0.05f), z < 6 ? "glowgreen" : "glowred", glow: true);
                vb.Box(new Vector3(0, 3.9f, z + 0.6f), new Vector3(22f, 0.1f, 0.1f), "brass");
            }
            foreach (float x in new[] { -9f, 0f, 9f })
            {
                vb.Model("home_light_chandelier", x, 6f, y: 3.6f, scale: 0.6f);
                vb.PointLight(new Vector3(x, 3.4f, 6f), "glowamber", 1.8f, 9f);
            }
            foreach (float s in new[] { -1f, 1f })
            {
                vb.Box(new Vector3(s * 6.9f, 0.45f, 2.2f), new Vector3(9.8f, 0.9f, 0.2f), "darksteel", solid: true);
                vb.Box(new Vector3(s * 6.9f, 0.92f, 2.2f), new Vector3(9.8f, 0.04f, 0.26f), "glowgreen", glow: true);
            }

            var liq = vb.Spot("liquidation_zone");
            vb.RingDecal(VB.P(liq), 2.4f, 0.12f, "glowred", 48, glow: true);
            vb.RingDecal(VB.P(liq), 1.9f, 0.05f, "ctred", 40);
            vb.FloorSign("pp_floor_liq", liq.x, liq.z + 1.0f, 3.4f);
            vb.SpotLight(new Vector3(liq.x, 5.5f, liq.z), VB.P(liq), "glowred", 2.4f, 9f, 50f);

            vb.Lounge(-12.2f, -5.5f, 90f, "midnight");
            vb.Lounge(12.2f, -5.5f, -90f, "velvetred");
            vb.OnWall('W', -5.5f, 2.3f, 3.2f, "pp_isolated", frame: "charcoal");
            vb.OnWall('E', -5.5f, 2.3f, 3.2f, "pp_cross", frame: "charcoal");
            vb.OnWall('W', 4.0f, 1.4f, 5.0f, "pp_levermeter", frame: "charcoal");
            vb.OnWall('W', 0.2f, 1.3f, 2.2f, "pp_rekt_receipts", frame: "brass");
            vb.HighTable(-6.5f, -6.5f);
            vb.HighTable(6.5f, -6.5f);
            vb.HighTable(-8.5f, -1.0f);
            vb.HighTable(8.5f, -1.0f);
            foreach (float z in new[] { 2.4f, 4.4f, 6.4f, 8.4f })
            {
                vb.Model("server_rack", 13.55f, z, -90f, glow: "glowcyan");
                vb.Solid(13.55f, z, 1.0f, 0.7f, 2f);
            }
            vb.Model("crypto_atm", 3.6f, -9.1f, 0f, screen: "pp_atm", glow: "glowgreen");
            vb.Solid(3.6f, -9.1f, 0.9f, 0.9f, 1.9f);
            vb.Plant(-3.6f, -9.0f, "houseplant_bush");
            vb.Plant(-13.3f, -9.2f);
            vb.Plant(13.3f, -9.2f);
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 1.0f, 1.6f);
            vb.PointLight(new Vector3(-2.2f, 4.2f, 8.6f), "glowgreen", 1.4f, 8f);
            vb.PointLight(new Vector3(6.1f, 4.2f, 8.6f), "glowred", 1.4f, 8f);
            vb.PointLight(new Vector3(0, 3.2f, -6f), "glowwhite", 1.4f, 12f);
            vb.PointLight(new Vector3(-10f, 3.0f, -4f), "glowcyan", 0.9f, 7f);
            vb.PointLight(new Vector3(10f, 3.0f, -4f), "glowpink", 0.9f, 7f);
        }

        // ------------------------------------------------------------------ Rekt Clinic
        // Six recovery beds with vitals monitors and curtains, triage desk, waiting room, and an open east side onto the
        // tombstone "touch grass" garden behind the memorial wall.
        public static void RektClinic(VB vb)
        {
            vb.InteriorShell("floor_tiles_2m", "clinicwhite", wallHeight: 4.4f, westHeight: 4.0f, eastHeight: 0.9f, trim: "hospitalblue");
            var w = vb.Walk;
            vb.OnWall('N', 0, 3.1f, 6.4f, "rc_title");
            vb.OnWall('N', -8.2f, 1.2f, 1.4f, "rc_eye", frame: "white");
            vb.OnWall('N', 8.2f, 1.1f, 1.6f, "rc_chart", frame: "white");
            vb.OnWall('W', -5f, 1.5f, 2.4f, "rc_triage", frame: "white");
            vb.OnWall('W', 2.5f, 1.4f, 3.2f, "rc_wait", frame: "white");
            vb.Box(new Vector3(0, 1.1f, vb.Inner.yMax - 0.03f), new Vector3(19.6f, 0.06f, 0.04f), "mint");

            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("bed_")))
            {
                vb.ModelAt("hospital_bed", VB.Rel(s, 0, -0.62f), VB.Deg(s) + 90f);
                vb.Obstacle(VB.Rel(s, 0, -0.98f, 0.5f), new Vector3(2.05f, 1f, 0.5f), VB.Deg(s));
                vb.Collider(VB.Rel(s, 0, -0.62f, 0.4f), new Vector3(2.05f, 0.8f, 1.0f), VB.Deg(s));
                vb.ModelAt("vitals_monitor", VB.Rel(s, 1.35f, -0.85f), VB.Deg(s), screen: "rc_vitals");
                vb.ModelAt("iv_stand", VB.Rel(s, -1.3f, -0.55f), VB.Deg(s));
                vb.SeatCollider(s, new Vector3(0, 0.6f, -0.6f), new Vector3(1.8f, 0.4f, 0.9f));
                vb.Approach(s, VB.Rel(s, 0.75f, 0.6f));
                vb.PointLight(VB.Rel(s, 0, -0.4f, 3.3f), "glowwhite", 0.9f, 5f);
            }
            foreach (float x in new[] { -3f, 3f })
                foreach (float z in new[] { 1.4f, 5.4f })
                {
                    vb.Model("privacy_curtain", x, z, 90f);
                    vb.Solid(x, z, 0.2f, 1.9f, 2.2f);
                }
            foreach (float x in new[] { -4.5f, 4.5f })
            {
                vb.Model("clinic_cabinet", x, vb.Inner.yMax - 0.25f, 180f);
                vb.Solid(x, vb.Inner.yMax - 0.25f, 1.4f, 0.5f, 0.6f);
            }
            vb.Model("clinic_sink", 0, vb.Inner.yMax - 0.3f, 180f);
            vb.Solid(0, vb.Inner.yMax - 0.3f, 0.9f, 0.5f, 0.9f);

            // Triage desk in front of the receptionist (faces the entering patients).
            var rec = vb.Spot("reception");
            var desk = VB.Rel(rec, 0, 0.95f);
            vb.Counter(desk.x, desk.z, 2.6f, 0.6f, -90f, "white", "hospitalblue", "glowred");
            vb.Model("cash_register", desk.x, desk.z + 0.6f, -90f, y: 1.08f);
            vb.Model("waiting_couch", -8.8f, -1.2f, 90f);
            vb.Solid(-8.8f, -1.2f, 1.2f, 1.7f, 1f);
            for (int i = 0; i < 5; i++)
            {
                vb.Model("waiting_chair", 3f + i * 1.0f, -7.0f, 0f);
                vb.Solid(3f + i * 1.0f, -7.0f, 0.5f, 0.5f, 0.9f);
            }
            vb.Plant(8.9f, -7.0f);
            vb.Plant(-3.0f, -7.0f, "houseplant_bush");

            // Memorial wall (behind tombstone_wall spot) and the open garden beyond the low east wall.
            vb.Box(new Vector3(w.xMax + 0.3f, 1.2f, 0), new Vector3(0.35f, 2.4f, 4.2f), "darkconcrete", collider: true);
            vb.Sign("rc_memorial", new Vector3(w.xMax + 0.1f, 0.9f, 0), 3.6f, -90f);
            var garden = Rect.MinMaxRect(w.xMax + 0.5f, w.yMin - 1f, w.xMax + 8f, w.yMax + 1f);
            vb.Box(new Vector3(garden.center.x, -0.06f, garden.center.y), new Vector3(garden.width, 0.1f, garden.height), "grass");
            int t = 1;
            foreach (float gx in new[] { 12.6f, 15.2f })
                foreach (float gz in new[] { -5f, -1.8f, 3.4f })
                {
                    vb.Model("tombstone", gx, gz, -90f, screen: "rc_tomb" + t++);
                    vb.Model("flower_clump_2", gx - 0.7f, gz + 0.4f, 0f, scale: 1.4f);
                }
            vb.Tree("tree_blossom_2", 16.5f, 6f, 0.8f, obstacle: false);
            vb.Tree("tree_blossom_4", 13.5f, -7.5f, 0.75f, obstacle: false);
            vb.Model("bush_large_flowers", 11.5f, 6.5f, 0f);
            vb.Model("park_bench", 13.9f, 7.4f, 180f);
            vb.FloorSign("rc_floor_grass", 7.2f, -4.6f, 2.6f, 90f);
            vb.FloorSign("exit", vb.Spot("exit").x, w.yMin + 1.0f, 1.6f);
            vb.PointLight(new Vector3(-8f, 3f, -5f), "glowwhite", 1.1f, 7f);
            vb.PointLight(new Vector3(4f, 3.2f, -5f), "glowwhite", 1.0f, 8f);
        }

        // ------------------------------------------------------------------ Launchpad Tower
        // Top-floor launch room: Pulse-ish launch board and next-launch clock (live hooks), the launch desk with its big
        // red button inside the NPC swarm ring, bonding-curve floor graphic, a rocket on its pad and skyline windows.
        public static void LaunchpadTower(VB vb)
        {
            vb.InteriorShell("floor_marble_2m", "midnight", wallHeight: 7.4f, sideHeight: 5.2f, frontHeight: 1.0f, trim: "solpurple");
            vb.OnWall('N', 0, 1.3f, 8.2f, "lt_board_idle", hook: "LaunchBoard");
            vb.OnWall('N', 0, 6.25f, 6.0f, "lt_title");
            vb.OnWall('N', -8.6f, 2.2f, 3.6f, "lt_countdown_idle", hook: "NextLaunchClock");
            vb.OnWall('N', 8.7f, 1.6f, 3.8f, "lt_bonding", frame: "charcoal");
            foreach (char side in new[] { 'W', 'E' })
                foreach (float z in new[] { -5.5f, 0.5f, 6.5f })
                    vb.OnWall(side, z, 1.0f, 5.6f, "lt_window", frame: "darksteel");
            vb.OnWall('W', 3.5f, 4.0f, 2.6f, "lt_rules", frame: "charcoal");

            var deskSpot = vb.Spot("launch_desk");
            vb.Terminal(deskSpot, "lt_terminal", chairColor: "solpurple", approachBack: 0.85f);
            vb.Cylinder(VB.Rel(deskSpot, 0.75f, 0.85f, 0.78f), 0.14f, 0.12f, "glowred", 12, glow: true);
            vb.Sign("lt_desk", VB.Rel(deskSpot, 0, 1.24f, 0.08f), 1.9f, VB.Deg(deskSpot));
            vb.RingDecal(new Vector3(0, 0, 6f), 2.0f, 0.06f, "glowpurple", 40, glow: true);
            vb.RingDecal(new Vector3(0, 0, 6f), 4.6f, 0.05f, "solpurple", 56);
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("swarm_")))
                vb.Decal(s.x, s.z, 0.5f, 0.5f, "glowpurple", 45f, glow: true);

            foreach (var s in vb.SpotsWhere(s => s.kind == "terminal"))
                vb.Terminal(s, "lt_terminal", chairColor: "charcoal");

            // Bonding curve: glowing floor curve rising from the entrance side to the board.
            Vector3 prev = default;
            for (int i = 0; i <= 24; i++)
            {
                float t = i / 24f;
                var p = new Vector3(-9.5f + 19f * t, 0, -8.6f + 7.2f * t * t * t);
                if (i > 0)
                {
                    var mid = (prev + p) * .5f;
                    var d = p - prev;
                    vb.Box(new Vector3(mid.x, 0.004f, mid.z), new Vector3(d.magnitude + 0.04f, 0.004f, 0.12f), "glowgreen",
                        -Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg, glow: true);
                }
                prev = p;
            }
            vb.FloorSign("lt_floor_curve", -6.5f, -9.2f, 3.0f);
            vb.Rocket(9.8f, 7.6f, 4.6f);
            // Creator-fee jar and a stream booth (launch streams are a live CT format).
            vb.Cylinder(new Vector3(5.4f, 0, 9.2f), 0.45f, 0.25f, "darksteel", 12, solid: true);
            vb.Cylinder(new Vector3(5.4f, 0.25f, 9.2f), 0.4f, 1.1f, "skyblue", 12);
            for (int i = 0; i < 6; i++) vb.Cylinder(new Vector3(5.25f + (i % 3) * 0.15f, 0.27f + (i / 3) * 0.05f, 9.15f + (i % 2) * 0.1f), 0.07f, 0.03f, "gold", 8);
            vb.OnWall('N', 5.4f, 2.0f, 2.4f, "lt_feejar");
            // Holder-rewards fountain: small basin with glowing water and coins (decor obstacle).
            vb.Cylinder(new Vector3(-6.5f, 0, 7.4f), 1.1f, 0.5f, "darkmarble", 16, solid: true);
            vb.Cylinder(new Vector3(-6.5f, 0.42f, 7.4f), 0.95f, 0.05f, "glowcyan", 16, glow: true);
            vb.Cylinder(new Vector3(-6.5f, 0.5f, 7.4f), 0.12f, 1.1f, "gold", 10);
            vb.Sphere(new Vector3(-6.5f, 1.7f, 7.4f), 0.22f, "glowgold", glow: true);
            vb.Sign("lt_fountain", new Vector3(-6.5f, 0.1f, 6.27f), 1.8f, 180f);
            vb.Model("home_desk", 10.6f, -6.4f, -90f);
            vb.Solid(10.6f, -6.4f, 0.8f, 1.6f, 0.8f);
            vb.Model("monitor_triple", 10.8f, -6.4f, -90f, y: 0.76f, screen: "lt_terminal");
            vb.Model("office_chair", 9.7f, -6.4f, 90f, recolor: "ctred");
            vb.Model("studio_light", 11.3f, -4.7f, -150f);
            vb.Model("mic_stand", 10.1f, -5.6f, -90f);
            vb.OnWall('E', -6.4f, 2.4f, 3.4f, "lt_stream");
            vb.Lounge(-10.3f, -6.6f, 90f, "velvetpurple");
            vb.Plant(-10.9f, 8.9f);
            vb.Plant(4.9f, 8.9f, "houseplant_bush");
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 1.0f, 1.6f);
            vb.PointLight(new Vector3(0, 4.5f, 6f), "glowpurple", 2.0f, 9f);
            vb.PointLight(new Vector3(0, 3.5f, -4f), "glowwhite", 1.3f, 11f);
            vb.SpotLight(new Vector3(9.8f, 6.5f, 4f), new Vector3(9.8f, 2f, 7.6f), "glowwhite", 2.0f, 9f, 45f);
        }

        // ------------------------------------------------------------------ Prediction Pit
        // Sportsbook-style pit: odds board (live hook), six booths, YES / NO floor halves, category banner and the Oracle.
        public static void PredictionPit(VB vb)
        {
            vb.InteriorShell("floor_concrete_2m", "navy", wallHeight: 7.4f, sideHeight: 5.6f, trim: "ctblue");
            vb.Decal(-6f, -1.5f, 11.6f, 16.6f, "forest");
            vb.Decal(6f, -1.5f, 11.6f, 16.6f, "plum");
            vb.Decal(0, -1.5f, 0.3f, 16.6f, "glowgold", glow: true);
            vb.FloorSign("pd_yes", -6f, -6.3f, 4.2f);
            vb.FloorSign("pd_no", 6f, -6.3f, 4.2f);
            vb.OnWall('N', 0, 1.2f, 9.0f, "pd_board_idle", hook: "OddsBoard");
            vb.OnWall('N', 0, 6.4f, 6.4f, "pd_title");
            vb.OnWall('W', 0, 4.0f, 16f, "pd_tabs");
            vb.OnWall('E', 2.0f, 1.6f, 4.0f, "pd_payout", frame: "charcoal");
            vb.OnWall('W', -5.5f, 1.4f, 4.0f, "pd_rules", frame: "charcoal");
            vb.OnWall('N', -9.3f, 2.0f, 3.6f, "pd_oracle", frame: "charcoal");

            foreach (var s in vb.SpotsWhere(s => s.kind == "terminal"))
                vb.Terminal(s, "pd_booth", desk: "trading_desk", chair: "office_chair", chairColor: "ctblue", deskForward: 0.62f);
            foreach (float x in new[] { -3f, 3f })
                foreach (float z in new[] { 3.6f, 6.6f })
                    vb.Box(new Vector3(x, 0.8f, z), new Vector3(0.08f, 1.6f, 1.6f), "slate", solid: true);

            // The Oracle: crystal ball on a plinth.
            vb.Cylinder(new Vector3(-9.6f, 0, 7.6f), 0.45f, 1.0f, "darkmarble", 12, solid: true);
            vb.Sphere(new Vector3(-9.6f, 1.38f, 7.6f), 0.38f, "glowpurple", glow: true);
            vb.PointLight(new Vector3(-9.6f, 1.6f, 7.0f), "glowpurple", 1.5f, 5f);
            // Bleacher benches either side of the entry aisle.
            foreach (float x in new[] { -8f, 8f })
                foreach (float z in new[] { -4f, -6.2f })
                {
                    vb.Model("bench_city", x, z, 0f, scale: 1.2f);
                    vb.Solid(x, z, 1.9f, 0.6f, 0.5f);
                }
            vb.Plant(-11.3f, -9.2f);
            vb.Plant(11.3f, -9.2f);
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 1.0f, 1.6f);
            vb.PointLight(new Vector3(-6f, 3.6f, 4.5f), "glowgreen", 1.2f, 8f);
            vb.PointLight(new Vector3(6f, 3.6f, 4.5f), "glowred", 1.2f, 8f);
            vb.PointLight(new Vector3(0, 4.0f, -3f), "glowwhite", 1.4f, 12f);
            vb.PointLight(new Vector3(0, 5.0f, 8.5f), "glowblue", 1.4f, 8f);
        }
    }
}
