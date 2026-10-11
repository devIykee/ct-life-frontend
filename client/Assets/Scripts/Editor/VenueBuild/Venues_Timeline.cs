using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>Timeline district: Ratio Arena, Compliance Jail (ct_jail), Discord Heights.</summary>
    public static class VenuesTimeline
    {
        // ------------------------------------------------------------------ Ratio Arena
        // Quote-tweet fight club: flush ring with red/blue corners (OP vs reply guy), ratio meter (live hook),
        // bleacher benches with a static crowd, banner art and arena spotlights.
        public static void RatioArena(VB vb)
        {
            vb.InteriorShell("floor_concrete_2m", "ink", wallHeight: 7.6f, sideHeight: 5.6f, trim: "ctpink");
            // Ring: flush mat + glowing border, corner posts, ropes on the west/east sides only (north/south stay open).
            vb.Decal(0, 0, 7.2f, 7.2f, "slate");
            vb.Decal(0, 0, 6.6f, 6.6f, "midnight", y: 0.005f);
            vb.FloorSign("ra_floor", 0, 0, 4.4f);
            foreach (var c in new[] { new Vector2(-3.5f, -3.5f), new Vector2(-3.5f, 3.5f), new Vector2(3.5f, -3.5f), new Vector2(3.5f, 3.5f) })
            {
                vb.Cylinder(new Vector3(c.x, 0, c.y), 0.12f, 1.45f, c.x < 0 ? "ctred" : "ctblue", 10, solid: true);
                vb.Sphere(new Vector3(c.x, 1.5f, c.y), 0.13f, c.x < 0 ? "glowred" : "glowblue", glow: true);
            }
            foreach (float x in new[] { -3.5f, 3.5f })
            {
                foreach (float y in new[] { 0.5f, 0.95f, 1.35f })
                    vb.Pipe(new Vector3(x, y, -3.5f), new Vector3(x, y, 3.5f), 0.035f, "white", 6);
                vb.Obstacle(new Vector3(x, 0.7f, 0), new Vector3(0.15f, 1.4f, 7f));
            }
            vb.Box(new Vector3(0, 0.008f, -3.5f), new Vector3(7.0f, 0.006f, 0.08f), "glowpink", glow: true);
            vb.Box(new Vector3(0, 0.008f, 3.5f), new Vector3(7.0f, 0.006f, 0.08f), "glowpink", glow: true);

            vb.OnWall('N', 0, 3.0f, 7.0f, "ra_score_idle", hook: "RatioMeter");
            vb.OnWall('N', 0, 6.65f, 6.0f, "ra_title");
            vb.OnWall('W', 0.5f, 2.6f, 5.2f, "ra_banner1", frame: "charcoal");
            vb.OnWall('E', 0.5f, 2.6f, 5.2f, "ra_banner2", frame: "charcoal");
            vb.OnWall('W', -5.5f, 1.4f, 3.2f, "ra_red");
            vb.OnWall('E', -5.5f, 1.4f, 3.2f, "ra_blue");

            // Bleachers: claimable benches at floor level, decorative raised tiers and a static crowd behind.
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("bleacher_")))
                vb.Seat(s, "bench_city", "ctpink", back: 0.12f, approachBack: -0.7f);
            var tierZ = vb.Inner.yMax;
            vb.Box(new Vector3(0, 0.25f, tierZ - 1.05f), new Vector3(18f, 0.5f, 0.7f), "slate", solid: true);
            vb.Box(new Vector3(0, 0.5f, tierZ - 0.4f), new Vector3(18f, 1.0f, 0.7f), "slate", solid: true);
            string[] shirts = { "ctred", "ctblue", "ctgold", "ctgreen", "ctpurple", "white", "ctpink", "ctteal" };
            for (int i = 0; i < 10; i++)
                vb.Figure(-8.1f + i * 1.8f, tierZ - (i % 2 == 0 ? 1.05f : 0.4f), shirts[i % shirts.Length], i % 3 == 0 ? "sand" : "blush", 0.95f);
            foreach (var y in new[] { 0.5f, 1.0f })
                vb.Box(new Vector3(0, y + 0.01f, tierZ - (y < 0.7f ? 1.05f : 0.4f) - 0.33f), new Vector3(18f, 0.03f, 0.04f), "glowpink", glow: true);

            // Arena lights: north truss fixtures are geometry; the south key lights are bake-only (no camera-side truss).
            foreach (var c in new[] { new Vector3(-9f, 6f, 6f), new Vector3(9f, 6f, 6f) })
            {
                vb.SpotLight(c, Vector3.zero, "glowpink", 2.6f, 16f, 34f);
                vb.Cylinder(c + Vector3.down * 0.3f, 0.25f, 0.5f, "charcoal", 10);
            }
            foreach (var c in new[] { new Vector3(-9f, 6.5f, -8f), new Vector3(9f, 6.5f, -8f) })
                vb.SpotLight(c, Vector3.zero, "glowwhite", 2.6f, 18f, 34f);
            vb.Box(new Vector3(0, 5.6f, 6f), new Vector3(20f, 0.2f, 0.2f), "darksteel");
            vb.Plant(-11.3f, -9.2f);
            vb.Plant(11.3f, -9.2f);
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 1.0f, 1.6f);
            vb.PointLight(new Vector3(0, 4.5f, 8.5f), "glowpink", 1.4f, 9f);
        }

        // ------------------------------------------------------------------ Compliance Jail
        // Four barred cells (each labelled with its offence) with bunks, KYC desk ("Know Your Degen"), mugshot wall,
        // guard post and holding bench. Bail is a game-Cash sink.
        public static void ComplianceJail(VB vb)
        {
            vb.InteriorShell("floor_concrete_2m", "concrete", wallHeight: 5.8f, sideHeight: 4.6f, trim: "jailorange");
            float back = vb.Inner.yMax;
            const float barsZ = 6.0f;
            float[] cells = { -9f, -3f, 3f, 9f };
            foreach (float x in new[] { -6f, 0f, 6f })
                vb.Box(new Vector3(x, 1.5f, (barsZ + back) * .5f), new Vector3(0.25f, 3.0f, back - barsZ), "darkconcrete", solid: true);
            var doors = new (float, float)[cells.Length];
            for (int i = 0; i < cells.Length; i++) doors[i] = (cells[i] - vb.Inner.xMin, 1.3f);
            vb.Bars(new Vector2(vb.Inner.xMin, barsZ), new Vector2(vb.Inner.xMax, barsZ), 2.8f, 0.22f, doors);
            for (int i = 0; i < cells.Length; i++)
            {
                float cx = cells[i];
                vb.Sign("cj_cell" + (i + 1), new Vector3(cx, 2.95f, barsZ - 0.06f), 3.6f, 180f, frame: "charcoal");
                vb.Decal(cx, (barsZ + back) * .5f, 5.4f, back - barsZ - 0.2f, "darkconcrete");
                vb.Model("home_bathroom_toilet", cx + 2.0f, back - 0.45f, 180f);
                vb.Solid(cx + 2.0f, back - 0.45f, 0.6f, 0.8f, 0.8f);
                vb.PointLight(new Vector3(cx, 2.6f, 8f), "glowamber", 0.7f, 4f);
            }
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("bunk_")))
            {
                vb.ModelAt("home_bed_single", VB.Rel(s, 0, -0.62f), VB.Deg(s) + 90f, recolor: "jailorange");
                vb.Obstacle(VB.Rel(s, 0, -0.98f, 0.4f), new Vector3(2.1f, 0.8f, 0.45f), VB.Deg(s));
                vb.SeatCollider(s, new Vector3(0, 0.55f, -0.55f), new Vector3(1.8f, 0.3f, 0.9f));
                vb.Approach(s, VB.Rel(s, 0.4f, 0.65f));
            }
            vb.Box(new Vector3(0, 0.004f, barsZ - 0.7f), new Vector3(23.4f, 0.004f, 0.18f), "jailorange");
            vb.OnWall('N', 0, 4.25f, 6.0f, "cj_title");

            var kyc = vb.Spot("kyc_desk");
            var desk = VB.Rel(kyc, 0, 0.95f);
            vb.Counter(desk.x, desk.z, 2.4f, 0.65f, -90f, "concrete", "jailorange", "glowamber");
            vb.Model("laptop", desk.x, desk.z, -90f, y: 1.08f);
            vb.OnWall('W', kyc.z, 0.9f, 1.8f, "cj_kyc", frame: "charcoal");
            var mug = vb.Spot("mugshot");
            vb.Box(VB.Rel(mug, 0, -0.65f, 1.4f), new Vector3(2.4f, 2.8f, 0.12f), "darkgrey", VB.Deg(mug), solid: true);
            vb.Sign("cj_height", VB.Rel(mug, 0, -0.58f, 0.35f), 1.5f, VB.Deg(mug));
            vb.Sign("cj_booking", VB.Rel(mug, 0, -0.58f, 2.6f), 2.3f, VB.Deg(mug));
            vb.ModelAt("light_stand", VB.Rel(mug, 0.6f, 2.4f), VB.Deg(mug) + 180f);
            vb.Solid(VB.Rel(mug, 0.6f, 2.4f).x, VB.Rel(mug, 0.6f, 2.4f).z, 0.5f, 0.5f, 1.9f);
            vb.SpotLight(VB.Rel(mug, 0, 2.6f, 2.4f), VB.Rel(mug, 0, 0, 1.4f), "glowwhite", 1.8f, 7f, 40f);

            // Guard post and holding bench.
            vb.Model("desk_medium_scifi", 10.3f, -1.5f, -90f);
            vb.Solid(10.3f, -1.5f, 0.8f, 2.3f, 0.8f);
            vb.Model("monitor_triple", 10.5f, -1.5f, -90f, y: 0.76f, screen: "cj_booking");
            vb.Model("office_chair", 9.4f, -1.5f, 90f);
            vb.OnWall('E', 3.0f, 1.3f, 2.6f, "cj_rules", frame: "charcoal");
            foreach (float z in new[] { -1.0f, 1.4f })
            {
                vb.Model("bench_city", -11.2f, z, 90f, scale: 1.2f);
                vb.Solid(-11.2f, z, 0.6f, 1.9f, 0.5f);
            }
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 1.0f, 1.6f);
            vb.PointLight(new Vector3(0, 3.6f, 1f), "glowwhite", 1.3f, 12f);
            vb.PointLight(new Vector3(-8f, 3f, -5f), "glowamber", 1.0f, 7f);
        }

        // ------------------------------------------------------------------ Discord Heights
        // Server lounge: channel doors (#general #alpha-maybe #gm #wen-token #support-ticket #memes), announcements
        // board (live hook), verify-to-enter kiosk, mod desk with ban hammer, role-coloured floor runs, sofas.
        public static void DiscordHeights(VB vb)
        {
            vb.InteriorShell("floor_wood_2m", "ink", wallHeight: 7.4f, sideHeight: 5.0f, trim: "discord");
            float back = vb.Inner.yMax;
            string[] channels = { "dh_general", "dh_alpha", "dh_gm", "dh_wen" };
            string[] roles = { "discord", "ctgold", "ctgreen", "ctpink" };
            float[] doorX = { -9f, -3f, 3f, 9f };
            for (int i = 0; i < 4; i++)
            {
                vb.Model("home_door_1", doorX[i], back - 0.08f, 180f, recolor: "slate");
                vb.Box(new Vector3(doorX[i], 1.1f, back - 0.05f), new Vector3(1.2f, 2.3f, 0.06f), roles[i], glow: false);
                vb.Box(new Vector3(doorX[i], 2.28f, back - 0.07f), new Vector3(1.3f, 0.05f, 0.05f), "glow" + (i == 0 ? "blue" : i == 1 ? "gold" : i == 2 ? "green" : "pink"), glow: true);
                vb.Sign(channels[i], new Vector3(doorX[i], 2.45f, back - 0.04f), 2.6f, 180f);
                // Role-coloured run from the lobby to each channel door.
                vb.Decal(doorX[i] * 0.5f, 3.5f, 0.25f, 9f, roles[i], Mathf.Atan2(doorX[i] * 0.5f, 9f) * Mathf.Rad2Deg);
            }
            vb.OnWall('N', 0, 3.4f, 7.0f, "dh_announce_idle", hook: "Announcements");
            vb.OnWall('N', 0, 6.55f, 6.2f, "dh_title");
            vb.Model("home_door_1", vb.Inner.xMin + 0.08f, 2.0f, 90f, recolor: "slate");
            vb.OnWall('W', 2.0f, 2.45f, 2.6f, "dh_support");
            vb.Model("home_door_1", vb.Inner.xMax - 0.08f, 2.0f, -90f, recolor: "slate");
            vb.OnWall('E', 2.0f, 2.45f, 2.6f, "dh_memes");
            vb.OnWall('W', -4.5f, 1.4f, 3.2f, "dh_rules", frame: "charcoal");
            vb.OnWall('E', -4.5f, 1.2f, 2.4f, "dh_wenposter", frame: "discord");
            vb.OnWall('E', 6.5f, 1.2f, 2.0f, "dh_hiring", frame: "discord");

            // Lobby sofas (claimable) facing the announcements board.
            foreach (float x in new[] { -4f, 4f })
            {
                vb.Model("lounge_sofa", x, -3.4f, 0f, recolor: "discord");
                vb.Obstacle(new Vector3(x, 0.45f, -3.75f), new Vector3(2.2f, 0.9f, 0.35f));
                vb.Model("home_table_roundsmall", x, -0.9f, 0f, scale: 0.75f);
                vb.Solid(x, -0.9f, 0.8f, 0.8f, 0.6f);
            }
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("sofa_")))
            {
                vb.SeatCollider(s, new Vector3(0, 0.6f, -0.3f), new Vector3(0.9f, 0.5f, 0.7f));
                vb.Approach(s, VB.Rel(s, 0, 0.8f));
            }
            // Verify kiosk at the door, mod desk with ban hammer.
            vb.Model("ad_kiosk", 4.2f, -7.4f, 180f, screen: "dh_verify");
            vb.Solid(4.2f, -7.4f, 1.5f, 0.7f, 2.7f);
            vb.Box(new Vector3(-9.6f, 0.2f, 1.0f), new Vector3(3.2f, 0.4f, 2.6f), "slate", solid: true);
            vb.Model("desk_medium_scifi", -9.9f, 1.0f, 90f, y: 0.4f);
            vb.Model("monitor_triple", -10.1f, 1.0f, 90f, y: 1.16f, screen: "dh_chat");
            vb.OnWall('W', 1.0f, 2.3f, 2.4f, "dh_mod");
            vb.Cylinder(new Vector3(-8.2f, 0.4f, -0.4f), 0.05f, 1.3f, "walnut", 6);
            vb.Box(new Vector3(-8.2f, 1.75f, -0.4f), new Vector3(0.7f, 0.35f, 0.35f), "chrome");
            // Unread badges and blurple glow trims.
            for (int i = 0; i < 6; i++)
                vb.Sphere(new Vector3(-10f + i * 4f, 5.2f, back - 0.2f), 0.28f, "glowred", glow: true);
            vb.Box(new Vector3(0, 0.05f, back - 0.1f), new Vector3(23.6f, 0.06f, 0.06f), "glowpurple", glow: true);
            vb.Lounge(10.3f, -6.5f, -90f, "plum");
            vb.Plant(-11.2f, -9.2f);
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 1.0f, 1.6f);
            vb.PointLight(new Vector3(0, 4.2f, 2f), "glowpurple", 1.6f, 12f);
            vb.PointLight(new Vector3(0, 3.0f, -6f), "glowwhite", 1.2f, 10f);
            vb.PointLight(new Vector3(-9f, 3f, 1f), "glowgreen", 0.9f, 6f);
        }
    }
}
