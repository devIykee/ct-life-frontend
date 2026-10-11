using CTLife.Core;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Private home types (contract/ids/homes.json) beyond the shipped Parent's Basement. All share the "home" spot set
    /// (bed, desk, sofa, entrance, exit, centre; 12 × 10 m walkable) and VenueSceneRoot.venueId = "home"; each home
    /// type differs in shell, furniture and view.
    /// </summary>
    public static class VenuesHomes
    {
        private static void Bed(VB vb, string model, string recolor = null, float depth = 1.6f)
        {
            var s = vb.Spot("bed");
            float back = depth * .5f + 0.05f;
            vb.ModelAt(model, VB.Rel(s, 0, -back), VB.Deg(s) + 90f, recolor: recolor);
            vb.Obstacle(VB.Rel(s, 0, -back - depth * .25f, 0.4f), new Vector3(2.2f, 0.8f, depth * .5f), VB.Deg(s));
            vb.Collider(VB.Rel(s, 0, -back, 0.3f), new Vector3(2.2f, 0.6f, depth), VB.Deg(s));
            vb.SeatCollider(s, new Vector3(0, 0.55f, -0.4f), new Vector3(1.8f, 0.3f, 0.7f));
            vb.Approach(s, VB.Rel(s, 0.9f, 0.6f));
        }

        private static void Sofa(VB vb, string model, string recolor, float depth = 0.9f)
        {
            var s = vb.Spot("sofa");
            vb.ModelAt(model, VB.Rel(s, 0, -depth * .5f + 0.12f), VB.Deg(s), recolor: recolor);
            vb.Obstacle(VB.Rel(s, 0, -depth + 0.05f, 0.45f), new Vector3(2.0f, 0.9f, 0.4f), VB.Deg(s));
            vb.SeatCollider(s, new Vector3(0, 0.6f, -0.3f), new Vector3(1.2f, 0.5f, 0.7f));
            vb.Approach(s, VB.Rel(s, 0, 0.75f));
        }

        private static void Tv(VB vb, string screen)
        {
            vb.Box(new Vector3(0, 0.25f, vb.Inner.yMax - 0.3f), new Vector3(2.2f, 0.5f, 0.45f), "walnut", solid: true);
            vb.Sign(screen, new Vector3(0, 0.95f, vb.Inner.yMax - 0.08f), 1.9f, 180f, frame: "black");
        }

        private static void Desk(VB vb, string desk, string chair, string monitorScreen, string chairColor)
        {
            var s = vb.Spot("desk");
            vb.Terminal(s, monitorScreen, desk: desk, chair: chair, monitor: "monitor_triple", chairColor: chairColor, deskForward: 0.75f, approachBack: 0.75f);
        }

        // ------------------------------------------------------------------ Hacker House ($60/week)
        public static void HackerHouse(VB vb)
        {
            vb.InteriorShell("floor_concrete_2m", "terracotta", wallHeight: 3.6f, frontHeight: 0.6f, doorWidth: 2.4f, trim: "charcoal");
            Bed(vb, "home_bed_bunk", depth: 1.3f);
            Desk(vb, "desk_medium_scifi", "office_chair_scifi", "hm_monitor", "ctgreen");
            Sofa(vb, "lounge_sofa", "charcoal");
            Tv(vb, "hm_tv");
            // Extra workstations along the west wall, server rack, whiteboard, pizza and energy drinks.
            vb.Model("trading_desk", -5.45f, -1.6f, -90f);
            vb.Solid(-5.45f, -1.6f, 0.7f, 1.8f, 0.8f);
            vb.Model("monitor_triple", -5.6f, -1.6f, -90f, y: 0.76f, screen: "hm_monitor");
            vb.Model("office_chair", -4.6f, -1.6f, 90f);
            vb.Model("server_rack", 5.6f, -2.6f, -90f, glow: "glowgreen");
            vb.Solid(5.6f, -2.6f, 0.7f, 1.0f, 2f);
            vb.OnWall('W', 1.6f, 1.2f, 1.8f, "hm_shipit", frame: "chrome");
            vb.OnWall('N', -2.6f, 2.0f, 1.6f, "hm_gm");
            vb.OnWall('N', 2.6f, 2.0f, 2.2f, "hm_wagmi");
            vb.OnWall('E', 1.6f, 1.6f, 2.0f, "hm_rent_hacker", frame: "charcoal");
            vb.Blob(new Vector3(-2.6f, 0.3f, -2.6f), new Vector3(0.95f, 0.6f, 0.95f), "ctpurple");
            vb.Solid(-2.6f, -2.6f, 0.8f, 0.8f, 0.6f);
            for (int i = 0; i < 4; i++)
                vb.Box(new Vector3(3.2f, 0.05f + i * 0.06f, -3.6f), new Vector3(0.45f, 0.05f, 0.45f), i % 2 == 0 ? "cardboard" : "pizza", i * 11f);
            vb.Solid(3.2f, -3.6f, 0.6f, 0.6f, 0.3f);
            vb.Model("cafe_fridge", 5.5f, -0.6f, -90f);
            vb.Solid(5.5f, -0.6f, 0.8f, 0.8f, 1.9f);
            vb.Blob(new Vector3(-2.2f, 0.12f, 3.95f), new Vector3(1.8f, 0.22f, 0.7f), "forest");
            vb.Box(new Vector3(0, 3.1f, vb.Inner.yMax - 0.05f), new Vector3(11.8f, 0.05f, 0.05f), "glowpurple", glow: true);
            vb.FloorSign("exit", 0, vb.Walk.yMin + 0.6f, 1.2f);
            vb.PointLight(new Vector3(0, 2.8f, 0.5f), "glowamber", 1.3f, 8f);
            vb.PointLight(new Vector3(4.5f, 2.2f, 3.5f), "glowgreen", 0.8f, 4f);
            vb.PointLight(new Vector3(-4f, 2.2f, 3f), "glowpurple", 0.7f, 4f);
        }

        // ------------------------------------------------------------------ Studio ($180/week)
        public static void Studio(VB vb)
        {
            vb.InteriorShell("floor_wood_2m", "cream", wallHeight: 3.4f, frontHeight: 0.6f, doorWidth: 2.4f, trim: "oak");
            Bed(vb, "home_bed_single", "linen", depth: 1.1f);
            Desk(vb, "home_desk", "home_chair_1", "hm_monitor", null);
            Sofa(vb, "home_couch_small2", "petrol");
            Tv(vb, "hm_tv");
            vb.OnWall('W', 0.5f, 0.9f, 3.0f, "hm_window_day", frame: "white");
            vb.OnWall('N', -2.0f, 1.0f, 1.6f, "hm_window_day", frame: "white");
            vb.OnWall('E', -3.6f, 1.6f, 1.8f, "hm_rent_studio", frame: "oak");
            // Kitchenette along the east wall.
            float ex = vb.Inner.xMax - 0.35f;
            string[] kit = { "home_kitchen_fridge", "home_kitchen_cabinet1", "home_kitchen_sink", "home_kitchen_oven", "home_kitchen_2drawers" };
            for (int i = 0; i < kit.Length; i++) vb.Model(kit[i], ex, -2.0f + i * 0.65f, -90f);
            vb.Solid(ex, -0.7f, 0.7f, 3.4f, 1.0f);
            vb.Decal(0, 0.6f, 3.2f, 2.2f, "mustard");
            vb.Plant(-5.4f, -3.9f);
            vb.Model("home_houseplant_5", 2.3f, 4.4f, 180f);
            vb.Model("home_light_floor1", -1.6f, 1.6f, 180f);
            vb.FloorSign("exit", 0, vb.Walk.yMin + 0.6f, 1.2f);
            vb.PointLight(new Vector3(0, 2.8f, 0.5f), "glowamber", 1.3f, 8f);
            vb.PointLight(new Vector3(-5f, 1.8f, 0.5f), "glowwhite", 0.9f, 5f);
        }

        // ------------------------------------------------------------------ Penthouse ($1,500/week)
        public static void Penthouse(VB vb)
        {
            vb.InteriorShell("floor_marble_2m", "white", wallHeight: 3.8f, frontHeight: 0.6f, doorWidth: 2.4f, trim: "gold");
            Bed(vb, "home_bed_king", "white", depth: 2.1f);
            Desk(vb, "home_desk", "office_chair", "hm_monitor", "leather");
            Sofa(vb, "home_couch_l", "charcoal", depth: 1.0f);
            Tv(vb, "hm_tv");
            foreach (float z in new[] { -2.5f, 1.5f }) vb.OnWall('W', z, 0.5f, 3.6f, "hm_window_night", frame: "gold");
            foreach (float z in new[] { -2.5f, 1.5f }) vb.OnWall('E', z, 0.5f, 3.6f, "hm_window_night", frame: "gold");
            vb.OnWall('N', -2.4f, 1.5f, 1.5f, "hm_bought_top", frame: "gold");
            vb.OnWall('N', 2.4f, 1.6f, 1.8f, "hm_rent_penthouse", frame: "gold");
            vb.Model("home_light_chandelier", 0, 0.5f, y: 2.8f, scale: 0.8f);
            vb.Decal(0, 0.4f, 4.0f, 2.8f, "cream");
            vb.Box(new Vector3(4.6f, 0.45f, -3.4f), new Vector3(1.0f, 0.9f, 0.5f), "darkmarble", solid: true);
            for (int i = 0; i < 5; i++)
                vb.Cylinder(new Vector3(4.25f + i * 0.17f, 0.9f, -3.4f), 0.04f, 0.28f, i % 2 == 0 ? "glowamber" : "glowgreen", 6, glow: true);
            vb.Plant(-5.4f, -3.9f);
            vb.Plant(5.4f, 4.2f, "houseplant_bush");
            vb.FloorSign("exit", 0, vb.Walk.yMin + 0.6f, 1.2f);
            vb.PointLight(new Vector3(0, 2.6f, 0.5f), "glowamber", 1.5f, 9f);
            vb.PointLight(new Vector3(-4f, 2.0f, 2.5f), "glowpurple", 0.6f, 4f);
        }

        // ------------------------------------------------------------------ Marina Villa ($5,000/week)
        public static void MarinaVilla(VB vb)
        {
            vb.InteriorShell("floor_wood_2m", "white", wallHeight: 1.0f, sideHeight: 3.6f, frontHeight: 0.6f, doorWidth: 2.4f, trim: "oak");
            var w = vb.Walk;
            Bed(vb, "home_bed_king", "linen", depth: 2.1f);
            Desk(vb, "home_desk", "office_chair", "hm_monitor", "linen");
            Sofa(vb, "home_couch_l", "linen", depth: 1.0f);
            // Low back wall with a glass rail opens onto the pool terrace, the marina and the towers.
            vb.Box(new Vector3(0, 1.45f, vb.Inner.yMax + 0.15f), new Vector3(12.4f, 0.9f, 0.04f), "skyblue");
            vb.Box(new Vector3(0, 1.92f, vb.Inner.yMax + 0.15f), new Vector3(12.4f, 0.05f, 0.08f), "chrome");
            vb.Box(new Vector3(0, -0.06f, w.yMax + 5f), new Vector3(16f, 0.1f, 9f), "beach");
            vb.Box(new Vector3(0, -0.04f, w.yMax + 3.6f), new Vector3(8f, 0.06f, 3.2f), "pool");
            foreach (float x in new[] { -6.5f, 6.5f }) vb.Tree("palm_1", x, w.yMax + 3.5f, 0.75f, obstacle: false);
            vb.Backdrop = true;
            vb.Box(new Vector3(0, -0.6f, w.yMax + 40f), new Vector3(120f, 0.4f, 60f), "water");
            vb.Yacht(new Vector3(-6f, -0.4f, w.yMax + 18f), 10f, 14f);
            foreach (var (x, z, i) in new[] { (-30f, 52f, 0), (-12f, 60f, 1), (8f, 64f, 2), (26f, 56f, 3), (40f, 48f, 4) })
                vb.Model(new[] { "tower_glass_a", "tower_glass_b", "tower_glass_c", "tower_glass_d", "tower_glass_e" }[i], x, z, 180f, scale: 0.85f);
            vb.Backdrop = false;
            foreach (float z in new[] { -2.5f, 1.5f }) vb.OnWall('W', z, 0.5f, 3.6f, "hm_window_marina", frame: "oak");
            vb.OnWall('E', 1.5f, 0.5f, 3.6f, "hm_window_marina", frame: "oak");
            vb.OnWall('E', -3.0f, 1.7f, 1.8f, "hm_rent_villa", frame: "oak");
            vb.Model("home_fireplace", vb.Inner.xMin + 0.35f, -2.8f, 90f);
            vb.Solid(vb.Inner.xMin + 0.35f, -2.8f, 0.6f, 1.6f, 1.3f);
            vb.Model("home_armchair_pillows", -3.0f, -2.9f, 60f, recolor: "linen");
            vb.Solid(-3.0f, -2.9f, 1.1f, 1.1f, 0.9f);
            vb.Decal(0, 0.4f, 4.2f, 2.8f, "sand");
            vb.Plant(5.4f, -3.9f);
            vb.Plant(-5.4f, 4.3f, "houseplant_bush");
            vb.FloorSign("exit", 0, vb.Walk.yMin + 0.6f, 1.2f);
            vb.PointLight(new Vector3(0, 2.6f, 0.5f), "glowamber", 1.3f, 9f);
            vb.PointLight(new Vector3(0, 1.0f, w.yMax + 3.6f), "glowcyan", 1.0f, 6f);
        }
    }
}
