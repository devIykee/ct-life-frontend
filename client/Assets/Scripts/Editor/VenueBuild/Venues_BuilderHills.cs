using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>Builder Hills district: Conference Center (conference_hall), Hackathon House, Audit Row, VC Row.</summary>
    public static class VenuesBuilderHills
    {
        // ------------------------------------------------------------------ Conference Center
        // "TOKENFEST 2026": main stage with LED wall (live hook) and podium, audience rows, expo booths
        // (Startup Village, IV drip bar), registration/lanyard desk, side-event and Wi-Fi gags.
        public static void ConferenceCenter(VB vb)
        {
            vb.InteriorShell("floor_tiles_2m", "slate", wallHeight: 7.8f, sideHeight: 5.6f, trim: "cyan");
            float back = vb.Inner.yMax;
            vb.Decal(0, 8.0f, 16f, 4.2f, "ink");
            vb.Box(new Vector3(0, 0.006f, 5.9f), new Vector3(16f, 0.006f, 0.08f), "glowcyan", glow: true);
            vb.Decal(0, -3.5f, 2.0f, 11f, "ctblue");
            vb.Decal(0, 4.0f, 9f, 1.6f, "midnight");
            vb.OnWall('N', 0, 1.3f, 9.4f, "cc_stage_idle", hook: "MainStage");
            vb.OnWall('N', 0, 6.4f, 7.0f, "cc_title");
            foreach (float x in new[] { -6.6f, 6.6f })
            {
                vb.Box(new Vector3(x, 2.6f, back - 0.5f), new Vector3(1.0f, 5.2f, 0.6f), "ink", solid: true);
                vb.Box(new Vector3(x, 2.6f, back - 0.81f), new Vector3(0.12f, 5.0f, 0.02f), "glowcyan", glow: true);
                vb.Model("studio_speaker", x * 1.12f, 7.0f, 180f);
                vb.Solid(x * 1.12f, 7.0f, 0.6f, 0.6f, 0.8f);
            }
            var speaker = vb.Spot("stage_speaker");
            vb.ModelAt("podium", VB.Rel(speaker, 0, 0.9f), VB.Deg(speaker), screen: "cc_reg");
            vb.Obstacle(VB.Rel(speaker, 0, 0.92f, 0.6f), new Vector3(0.8f, 1.2f, 0.4f), VB.Deg(speaker));
            vb.ModelAt("mic_stand", VB.Rel(speaker, 0.55f, 0.5f), VB.Deg(speaker));
            vb.Box(new Vector3(0, 5.6f, 5.6f), new Vector3(17f, 0.22f, 0.22f), "darksteel");
            foreach (float x in new[] { -6f, -2f, 2f, 6f })
            {
                vb.Cylinder(new Vector3(x, 5.2f, 5.6f), 0.18f, 0.35f, "charcoal", 8);
                vb.SpotLight(new Vector3(x, 5.2f, 5.4f), new Vector3(x * 0.3f, 0, 8f), "glowwhite", 1.6f, 10f, 38f);
            }
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("seat_")))
                vb.Seat(s, "waiting_chair", "ctblue", back: 0.05f, approachBack: -0.6f);

            // Expo booths (booth_1 west, booth_2 east) with counters in front of the booth staff.
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("booth_")))
            {
                var c = VB.Rel(s, 0, 0.95f);
                vb.Counter(c.x, c.z, 2.6f, 0.6f, VB.Deg(s) + 180f, "white", s.x < 0 ? "ctgreen" : "ctteal", s.x < 0 ? "glowgreen" : "glowcyan");
                vb.ModelAt("trading_terminal", VB.Rel(s, 0.9f, 0.95f, 1.08f), VB.Deg(s), screen: "cc_booth");
                vb.OnWall(s.x < 0 ? 'W' : 'E', s.z, 1.4f, 3.4f, s.x < 0 ? "cc_village" : "cc_ivdrip", frame: "charcoal");
            }
            vb.OnWall('W', 1.5f, 2.0f, 3.6f, "cc_side", frame: "charcoal");
            vb.OnWall('E', 1.5f, 2.0f, 2.6f, "cc_plunge", frame: "charcoal");
            vb.OnWall('E', -8.4f, 1.6f, 1.8f, "cc_wifi", frame: "charcoal");
            vb.OnWall('W', 6.5f, 2.2f, 2.4f, "cc_timer", hook: "PitchTimer");
            vb.OnWall('N', 9.6f, 3.4f, 3.6f, "cc_after");
            vb.OnWall('N', -9.6f, 2.6f, 3.6f, "cc_next", frame: "charcoal");
            // Booth mascot (photo op) beside the east booth: a big soft parody character.
            vb.Blob(new Vector3(9.6f, 0.9f, -1.6f), new Vector3(1.3f, 1.8f, 1.1f), "ctteal");
            vb.Sphere(new Vector3(9.35f, 1.35f, -2.08f), 0.12f, "white");
            vb.Sphere(new Vector3(9.85f, 1.35f, -2.08f), 0.12f, "white");
            vb.Sphere(new Vector3(9.35f, 1.35f, -2.17f), 0.05f, "black");
            vb.Sphere(new Vector3(9.85f, 1.35f, -2.17f), 0.05f, "black");
            vb.Solid(9.6f, -1.6f, 1.3f, 1.1f, 1.8f);
            vb.Sign("cc_mascot", new Vector3(9.6f, 0.05f, -2.25f), 1.6f, 180f);

            // Registration desk with lanyard rack near the door.
            vb.Counter(-5.6f, -8.3f, 3.0f, 0.7f, 0f, "white", "gold", "glowgold");
            vb.Sign("cc_reg", new Vector3(-5.6f, 0.12f, -8.67f), 2.4f, 180f);
            for (int i = 0; i < 8; i++)
                vb.Box(new Vector3(-6.7f + i * 0.3f, 1.35f, -8.15f), new Vector3(0.04f, 0.5f, 0.02f), i % 2 == 0 ? "ctblue" : "cyan");
            vb.Box(new Vector3(-5.6f, 1.62f, -8.15f), new Vector3(2.6f, 0.04f, 0.04f), "chrome");
            vb.Plant(-11.3f, -9.2f);
            vb.Plant(11.3f, -9.2f);
            vb.Plant(11.3f, 9.2f, "houseplant_bush");
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 1.0f, 1.6f);
            vb.PointLight(new Vector3(0, 4.2f, -2f), "glowwhite", 1.6f, 14f);
            vb.PointLight(new Vector3(-10f, 3f, -5f), "glowgreen", 0.8f, 6f);
            vb.PointLight(new Vector3(10f, 3f, -5f), "glowcyan", 0.8f, 6f);
        }

        // ------------------------------------------------------------------ Hackathon House
        // Buildooor Station: long hacking table (8 claimable desks), submission countdown (live hook), whiteboard plan,
        // couch corner with beanbags and pizza, bunk/sleeping-bag corner, energy-drink fridge, demo podium.
        public static void HackathonHouse(VB vb)
        {
            vb.InteriorShell("floor_wood_2m", "cream", wallHeight: 6.0f, sideHeight: 4.6f, trim: "plywood");
            float back = vb.Inner.yMax;
            // Long table: plywood top on steel trestles.
            vb.Box(new Vector3(0, 0.74f, -1.5f), new Vector3(16f, 0.06f, 1.2f), "plywood");
            foreach (float x in new[] { -7.6f, -2.5f, 2.5f, 7.6f })
                vb.Box(new Vector3(x, 0.36f, -1.5f), new Vector3(0.08f, 0.72f, 1.0f), "darksteel");
            vb.Obstacle(new Vector3(0, 0.4f, -1.5f), new Vector3(16f, 0.8f, 1.1f));
            vb.Collider(new Vector3(0, 0.4f, -1.5f), new Vector3(16f, 0.8f, 1.2f));
            int n = 0;
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("desk_")))
            {
                vb.ModelAt("office_chair", VB.Rel(s, 0, -0.05f), VB.Deg(s), recolor: n++ % 2 == 0 ? "ctgreen" : "charcoal");
                vb.ModelAt("laptop", VB.Rel(s, 0, 0.62f, 0.77f), VB.Deg(s) + 180f);
                vb.ModelAt("cup_coffee", VB.Rel(s, 0.45f, 0.55f, 0.77f), VB.Deg(s));
                vb.SeatCollider(s, new Vector3(0, 0.75f, -0.05f), new Vector3(0.6f, 0.5f, 0.6f));
                vb.Approach(s, VB.Rel(s, 0, -0.85f));
            }
            for (int i = 0; i < 5; i++)
                vb.Box(new Vector3(-7f + i * 3.5f, 0.8f + (i % 2) * 0.05f, -1.5f), new Vector3(0.42f, 0.05f + (i % 2) * 0.05f, 0.42f), "cardboard", i * 7f);
            vb.OnWall('N', 0, 1.8f, 7.0f, "hh_countdown_idle", hook: "SubmissionCountdown");
            vb.OnWall('N', 0, 5.0f, 6.2f, "hh_title");
            vb.OnWall('W', 2.5f, 1.1f, 5.0f, "hh_whiteboard", frame: "chrome");
            vb.OnWall('E', 5.5f, 3.3f, 5.6f, "hh_sponsor");
            vb.OnWall('E', -1.5f, 1.2f, 2.0f, "hh_rules", frame: "charcoal");

            // Couch corner (claimable couch seats), beanbags, pizza.
            vb.Model("home_couch_medium1", -8.75f, 5.75f, 0f, recolor: "ctteal");
            vb.Obstacle(new Vector3(-8.75f, 0.45f, 5.45f), new Vector3(2.2f, 0.9f, 0.45f));
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("couch_")))
            {
                vb.SeatCollider(s, new Vector3(0, 0.6f, -0.3f), new Vector3(0.8f, 0.5f, 0.7f));
                vb.Approach(s, VB.Rel(s, 0, 0.8f));
            }
            vb.Model("home_table_medium", -8.75f, 8.6f, 0f, scale: 0.8f);
            vb.Solid(-8.75f, 8.6f, 1.2f, 1.2f, 0.7f);
            vb.Box(new Vector3(-8.75f, 0.66f, 8.6f), new Vector3(0.45f, 0.05f, 0.45f), "pizza");
            vb.Blob(new Vector3(-11.2f, 0.32f, 7.8f), new Vector3(1.0f, 0.65f, 1.0f), "ctorange");
            vb.Blob(new Vector3(-6.5f, 0.32f, 8.6f), new Vector3(1.0f, 0.65f, 1.0f), "ctpurple");
            vb.Solid(-11.2f, 7.8f, 0.9f, 0.9f, 0.6f);
            vb.Solid(-6.5f, 8.6f, 0.9f, 0.9f, 0.6f);
            // Sleeping corner and energy drinks.
            vb.Model("home_bed_bunk", 10.6f, 8.5f, 180f);
            vb.Solid(10.6f, 8.5f, 1.4f, 2.4f, 1.8f);
            foreach (float x in new[] { 7.2f, 8.5f })
            {
                vb.Blob(new Vector3(x, 0.14f, 9.1f), new Vector3(0.75f, 0.25f, 1.8f), x < 8 ? "ctblue" : "forest");
                vb.Solid(x, 9.1f, 0.8f, 1.8f, 0.3f);
            }
            vb.Model("cafe_fridge", 11.4f, 3.6f, -90f);
            vb.Solid(11.4f, 3.6f, 0.8f, 0.8f, 1.9f);
            vb.Model("server_rack", 11.5f, 1.8f, -90f, glow: "glowgreen");
            vb.Solid(11.5f, 1.8f, 0.7f, 1.0f, 2f);
            for (int i = 0; i < 6; i++)
                vb.Box(new Vector3(9.2f + (i % 3) * 0.5f, 0.2f + (i / 3) * 0.42f, -8.9f), new Vector3(0.45f, 0.4f, 0.45f), "cardboard", i * 9f);
            vb.Solid(9.7f, -8.9f, 1.6f, 0.6f, 0.9f);
            vb.Model("podium", 7.4f, -6.2f, 180f, screen: "hh_demo");
            vb.Solid(7.4f, -6.2f, 0.9f, 0.7f, 1.2f);
            vb.Decal(7.4f, -6.9f, 3.0f, 2.0f, "ctgreen");
            vb.SpotLight(new Vector3(7.4f, 4.2f, -8.5f), new Vector3(7.4f, 1f, -6.5f), "glowwhite", 1.6f, 7f, 40f);
            vb.Plant(-11.3f, -9.2f);
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 1.0f, 1.6f);
            foreach (float x in new[] { -6f, 0f, 6f })
            {
                vb.Model("home_light_chandelier", x, -1.5f, y: 3.4f, scale: 0.5f);
                vb.PointLight(new Vector3(x, 3.2f, -1.5f), "glowamber", 1.3f, 7f);
            }
            vb.PointLight(new Vector3(-8.5f, 2.8f, 7.5f), "glowpink", 0.9f, 6f);
            vb.PointLight(new Vector3(9.5f, 2.8f, 7.5f), "glowblue", 0.8f, 6f);
        }

        // ------------------------------------------------------------------ Audit Row
        // Four audit booths (claimable desks) behind glass partitions, the EXPLOIT ALARM board and beacon (live hooks),
        // REKT-O-BOARD hall of losses, bug-bounty corkboard, "funds are probably fine" banner, giant magnifier.
        public static void AuditRow(VB vb)
        {
            vb.InteriorShell("floor_wood_2m", "slate", wallHeight: 7.2f, sideHeight: 5.0f, trim: "ctteal");
            float back = vb.Inner.yMax;
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("audit_desk_")))
            {
                vb.Terminal(s, "ar_screen", chair: "office_chair", chairColor: "petrol");
                vb.ModelAt("locker", VB.Rel(s, 1.6f, 1.95f), VB.Deg(s) + 180f);
                vb.Solid(VB.Rel(s, 1.6f, 1.95f).x, VB.Rel(s, 1.6f, 1.95f).z, 0.75f, 0.5f, 1.9f);
                vb.Box(VB.Rel(s, -0.7f, 0.75f, 0.82f), new Vector3(0.35f, 0.1f, 0.25f), "paper");
                vb.Box(VB.Rel(s, -0.7f, 0.75f, 0.92f), new Vector3(0.33f, 0.08f, 0.24f), "paper", 8f);
            }
            foreach (float x in new[] { -5f, 0f, 5f })
            {
                vb.Box(new Vector3(x, 1.15f, 7.9f), new Vector3(0.06f, 2.3f, 3.6f), "skyblue", solid: true);
                vb.Box(new Vector3(x, 2.32f, 7.9f), new Vector3(0.1f, 0.06f, 3.6f), "chrome");
            }
            vb.OnWall('N', 0, 2.6f, 6.2f, "ar_alarm_idle", hook: "ExploitAlarm");
            vb.Beacon("ExploitBeacon", new Vector3(0, 5.6f, back - 0.4f), 0.9f);
            vb.Box(new Vector3(0, 5.55f, back - 0.25f), new Vector3(1.4f, 0.1f, 0.5f), "darksteel");
            vb.PointLight(new Vector3(0, 5.0f, back - 1.2f), "glowred", 1.6f, 9f);
            vb.OnWall('N', -8.3f, 3.0f, 5.2f, "ar_title");
            vb.OnWall('N', 8.3f, 2.6f, 3.4f, "ar_rektboard", frame: "charcoal");
            vb.OnWall('W', 0.5f, 1.4f, 3.8f, "ar_bounty", frame: "brass");
            vb.OnWall('E', 0.5f, 2.8f, 6.0f, "ar_safu");
            vb.OnWall('E', -3.2f, 1.2f, 1.8f, "ar_stamp");
            vb.OnWall('E', 3.6f, 1.2f, 1.8f, "ar_unaudited");
            vb.OnWall('W', -5.5f, 2.2f, 3.4f, "ar_queue", frame: "charcoal");
            vb.OnWall('W', 6.5f, 1.2f, 2.0f, "ar_hermit", frame: "oak");

            // Client waiting chairs and the giant magnifying glass sculpture.
            for (int i = 0; i < 4; i++)
            {
                vb.Model("waiting_chair", -11.3f, -7.6f + i * 0.9f, 90f);
                vb.Solid(-11.3f, -7.6f + i * 0.9f, 0.5f, 0.5f, 0.9f);
            }
            var lens = new Vector3(8.6f, 1.9f, -5.6f);
            for (int i = 0; i < 20; i++)
            {
                float a0 = i * Mathf.PI * 2 / 20, a1 = (i + 1) * Mathf.PI * 2 / 20;
                vb.Pipe(lens + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0) * 0.9f, lens + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0) * 0.9f, 0.08f, "brass", 6);
            }
            vb.Pipe(lens + new Vector3(0.6f, -0.65f, 0), new Vector3(9.9f, 0.05f, -5.6f), 0.11f, "walnut", 8);
            vb.Box(lens, new Vector3(1.6f, 1.6f, 0.02f), "skyblue");
            vb.Solid(9.0f, -5.6f, 2.4f, 0.6f, 2.8f);
            vb.Plant(-11.3f, 9.0f);
            vb.Plant(11.3f, -9.2f, "houseplant_bush");
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 1.0f, 1.6f);
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("audit_desk_")))
                vb.PointLight(VB.Rel(s, 0, 0.3f, 2.6f), "glowwhite", 0.9f, 5f);
            vb.PointLight(new Vector3(0, 3.4f, -4f), "glowwhite", 1.3f, 12f);
        }

        // ------------------------------------------------------------------ VC Row
        // Partner floor: glass pitch room with a long table (6 claimable seats) and pitch-deck screen (live hook),
        // WE ARE SO EARLY neon, term-sheet wall, thesis/portfolio art, partner desk, library lounge.
        public static void VcRow(VB vb)
        {
            vb.InteriorShell("floor_wood_2m", "cream", wallHeight: 6.4f, sideHeight: 4.8f, trim: "walnut");
            float back = vb.Inner.yMax;
            vb.Decal(0, 7.6f, 10f, 4.6f, "walnut");
            vb.Decal(0, -3f, 9f, 5f, "velvetred");
            // Glass pitch room: mullions + header, door gap in the middle of the front.
            const float front = 5.2f;
            for (float x = -5f; x <= 5.01f; x += 1.25f)
                if (Mathf.Abs(x) > 1.1f) vb.Box(new Vector3(x, 1.4f, front), new Vector3(0.07f, 2.8f, 0.07f), "darksteel");
            foreach (float s in new[] { -1f, 1f })
            {
                vb.Box(new Vector3(s * 3.05f, 1.4f, front), new Vector3(3.9f, 2.8f, 0.03f), "skyblue");
                vb.Obstacle(new Vector3(s * 3.05f, 1.4f, front), new Vector3(3.9f, 2.8f, 0.12f));
                vb.Collider(new Vector3(s * 3.05f, 1.4f, front), new Vector3(3.9f, 2.8f, 0.12f));
                for (float z = front; z <= back + 0.01f; z += 1.2f) vb.Box(new Vector3(s * 5f, 1.4f, z), new Vector3(0.07f, 2.8f, 0.07f), "darksteel");
                vb.Box(new Vector3(s * 5f, 1.4f, (front + back) * .5f), new Vector3(0.03f, 2.8f, back - front), "skyblue");
                vb.Obstacle(new Vector3(s * 5f, 1.4f, (front + back) * .5f), new Vector3(0.12f, 2.8f, back - front));
            }
            vb.Box(new Vector3(0, 2.85f, front), new Vector3(10.1f, 0.1f, 0.1f), "darksteel");
            vb.Sign("vc_door", new Vector3(0, 2.92f, front - 0.06f), 1.8f, 180f);
            // Long table and claimable pitch seats.
            vb.Box(new Vector3(0, 0.74f, 7.6f), new Vector3(6.4f, 0.07f, 1.0f), "walnut");
            vb.Box(new Vector3(0, 0.36f, 7.6f), new Vector3(5.6f, 0.72f, 0.4f), "darkwood");
            vb.Obstacle(new Vector3(0, 0.4f, 7.6f), new Vector3(6.4f, 0.8f, 0.9f));
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("pitch_")))
            {
                vb.ModelAt("office_chair", VB.Rel(s, 0, -0.05f), VB.Deg(s), recolor: "leather");
                vb.SeatCollider(s, new Vector3(0, 0.75f, -0.05f), new Vector3(0.6f, 0.5f, 0.6f));
                vb.Approach(s, VB.Rel(s, 0.7f, -0.3f));
            }
            vb.Box(new Vector3(0.3f, 0.79f, 7.5f), new Vector3(0.09f, 0.015f, 0.17f), "gold");
            vb.Box(new Vector3(0.3f, 0.8f, 7.5f), new Vector3(0.075f, 0.01f, 0.15f), "glowgold", glow: true);
            vb.OnWall('N', 0, 1.2f, 5.0f, "vc_deck_idle", hook: "PitchDeck");
            vb.OnWall('N', -8.5f, 3.4f, 5.0f, "vc_title");
            vb.OnWall('N', 8.5f, 1.0f, 2.2f, "vc_termsheet", frame: "brass");
            vb.OnWall('W', 2.0f, 2.4f, 6.4f, "vc_early");
            vb.OnWall('W', -5.5f, 1.3f, 2.4f, "vc_art", frame: "gold");
            vb.OnWall('E', 1.5f, 2.2f, 3.6f, "vc_thesis", frame: "charcoal");
            vb.OnWall('E', -3.5f, 2.2f, 3.6f, "vc_portfolio", frame: "charcoal");

            vb.Terminal(vb.Spot("partner_desk"), "vc_deck_idle", desk: "home_desk", chair: "office_chair", chairColor: "leather", deskForward: 0.75f);
            // Library lounge.
            foreach (float z in new[] { -6.5f, -4.4f })
            {
                vb.Model("home_armchair_pillows", 10.6f, z, -90f, recolor: "leather");
                vb.Solid(10.6f, z, 1.2f, 1.3f, 0.9f);
            }
            vb.Model("home_table_roundsmall", 9.2f, -5.45f, 0f, scale: 0.7f);
            vb.Solid(9.2f, -5.45f, 0.9f, 0.9f, 0.6f);
            foreach (float z in new[] { 6.6f, 8.0f, 9.4f })
            {
                vb.Model("home_bookshelf", vb.Inner.xMax - 0.2f, z, -90f);
                vb.Solid(vb.Inner.xMax - 0.2f, z, 0.4f, 1.0f, 1.9f);
            }
            vb.Plant(-11.3f, -9.2f);
            vb.Plant(-6.2f, 9.3f);
            vb.Plant(6.2f, 9.3f);
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 1.0f, 1.6f);
            vb.Model("home_light_chandelier", 0, 7.6f, y: 3.3f, scale: 0.6f);
            vb.PointLight(new Vector3(0, 3.0f, 7.6f), "glowamber", 1.5f, 7f);
            vb.PointLight(new Vector3(0, 3.4f, -3f), "glowamber", 1.3f, 12f);
            vb.PointLight(new Vector3(-10f, 3f, 2f), "glowpink", 0.8f, 6f);
        }
    }
}
