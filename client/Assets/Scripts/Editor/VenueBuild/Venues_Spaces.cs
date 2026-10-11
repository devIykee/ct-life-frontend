using CTLife.Core;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Spaces Studio v4 (round 3), built to the in-game concept (ct-life-evidence/art-direction/out/ingame-spaces-concept-v1.png):
    /// a warm, wood-lined evening broadcast studio that opens onto a rooftop terrace.
    ///  * Round two-step stage with amber LED under the nosings, a cream round rug, four tan guest armchairs in a talk-show
    ///    semicircle with boom mic stands, a low round table, and the host desk (sun lamp, mixer, laptop, plant, boom mic).
    ///  * North wall: teal LED waveform wall (live hook), purple/teal fluted acoustic panels, walnut slats, speakers on
    ///    stands, spot cans, and the red on-air lamp (live hook, idle dim). A lighting truss with warm spots over the stage.
    ///  * Speaker pods round the stage: stools with three-lens phones on mic stands.
    ///  * Audience in arcs: big bean bags on the floor, square poufs on two curved wooden risers with amber LED nosing, rugs.
    ///  * West: green-room lounge (teal sofa, record player, headphone shelf, slatted partition) beside the backstage wall
    ///    with the lanyard frame and the rainbow neon arched doorway (Colosseum-style arch, no letters); a lanyard
    ///    poster wall; gig posters; plants.
    ///  * East: open glazing onto the terrace (cushioned benches, lanterns, string lights, planters, palms, one
    ///    multi-country flag bunting) and the evening city and mountains.
    /// No letters anywhere except the shared EXIT floor marker. Spot ids are the contract's; positions follow spots.json fe.4.
    /// </summary>
    public static class VenuesSpaces
    {
        private static readonly Vector3 C = new Vector3(0f, 0f, 6.2f);
        private const float StageR = 4.3f, StepR = 4.7f, StageH = 0.22f, StepH = 0.11f;
        private const float Tier1 = 0.12f, Tier2 = 0.24f, T1In = 11.15f, T1Out = 13.2f, T2Out = 14.9f, TierSpan = 34f;
        private const float BackZ = 1.6f, BackX = -7.4f, BackH = 3.4f, DoorX = -9.0f, DoorW = 1.3f, DoorH = 2.25f;
        private const float PartX = -7.6f, PartZ0 = -3.6f, PartZ1 = 0.2f;

        /// <summary>Polar angle of a point round the stage centre, 0 = due south, positive towards +x (degrees).</summary>
        private static float Phi(Vector3 p) => Mathf.Atan2(p.x - C.x, C.z - p.z) * Mathf.Rad2Deg;

        private static float Ground(Vector3 p)
        {
            float d = new Vector2(p.x - C.x, p.z - C.z).magnitude;
            if (d <= StageR) return StageH;
            if (d <= StepR) return StepH;
            if (Mathf.Abs(Phi(p)) <= TierSpan)
            {
                if (d >= T1Out && d <= T2Out) return Tier2;
                if (d >= T1In && d < T1Out) return Tier1;
            }
            return 0f;
        }

        /// <summary>Point at radius r and polar angle phi (0 = due south of the stage centre).</summary>
        private static Vector3 Polar(float r, float phi, float y = 0f) =>
            new Vector3(C.x + r * Mathf.Sin(phi * Mathf.Deg2Rad), y, C.z - r * Mathf.Cos(phi * Mathf.Deg2Rad));

        private static Vector3 Fwd(SpotDef s) => new Vector3(Mathf.Sin(s.rotY), 0f, Mathf.Cos(s.rotY));
        private static Vector3 Right(SpotDef s) => new Vector3(Mathf.Cos(s.rotY), 0f, -Mathf.Sin(s.rotY));

        public static void SpacesStudio(VB vb)
        {
            vb.GroundAt = Ground;
            vb.InteriorShell("floor_wood_2m", "walnut", wallHeight: 6.4f, westHeight: 5.0f, eastHeight: 0.25f, frontHeight: 0.9f, trim: "darkwood");
            var inn = vb.Inner;
            Glazing(vb, inn);
            Terrace(vb, inn);
            NorthWall(vb, inn);
            Stage(vb);
            Pods(vb);
            Audience(vb);
            Backstage(vb, inn);
            Lounge(vb, inn);
            EastSide(vb, inn);
            FrontPlanters(vb, inn);
            Truss(vb);
            vb.FloorSign("exit", vb.Spot("exit").x, vb.Walk.yMin + 0.55f, 1.4f);
            // House light (baked): warm fill over the audience, cyan wash from the LED wall.
            vb.PointLight(new Vector3(0, 3.8f, -4.0f), "glowamber", 0.75f, 12f);
            // Dusk sky light through the open east side and a violet bounce on the west (the concept's cool shadows).
            vb.PointLight(new Vector3(10.5f, 3.4f, 0f), "lavender", 0.7f, 11f);
            vb.PointLight(new Vector3(-6f, 4.5f, -2f), "glowpurple", 0.35f, 9f);
            vb.PointLight(new Vector3(-5.5f, 3.0f, -5.5f), "glowamber", 0.8f, 7f);
            vb.PointLight(new Vector3(5.5f, 3.0f, -5.5f), "glowamber", 0.8f, 7f);
            vb.PointLight(new Vector3(0, 2.6f, 9.2f), "glowcyan", 1.3f, 7f);
        }

        // ------------------------------------------------------------------ shell: west slats, east glazing

        private static void Glazing(VB vb, Rect inn)
        {
            float x = inn.xMax + 0.15f;
            for (float z = -10f; z <= 10.01f; z += 2.5f)
                vb.Box(new Vector3(x, 2.55f, z), new Vector3(0.16f, 4.6f, 0.16f), "darkwood", collider: true);
            vb.Box(new Vector3(x, 5.6f, 0), new Vector3(0.45f, 1.6f, 20.6f), "walnut");
            vb.Box(new Vector3(x - 0.2f, 4.82f, 0), new Vector3(0.06f, 0.05f, 20.4f), "glowamber", glow: true);
            vb.Box(new Vector3(x, 0.27f, 0), new Vector3(0.4f, 0.06f, 20.6f), "honey");
            // Wood slats on the west wall above the backstage block and lounge.
            for (int i = 0; i < 62; i++)
                vb.Box(new Vector3(inn.xMin + 0.05f, 4.2f, -9.6f + i * 0.31f), new Vector3(0.1f, 1.6f, 0.12f), i % 5 == 0 ? "darkwood" : "walnut");
        }

        // ------------------------------------------------------------------ terrace and city (east, outside the walkable area)

        private static void Terrace(VB vb, Rect inn)
        {
            float x0 = inn.xMax + 0.4f, x1 = x0 + 5.0f;
            vb.Floor("floor_concrete_2m", Rect.MinMaxRect(x0 - 0.1f, -10.6f, x1 + 0.6f, 10.6f), -0.02f);
            vb.Box(new Vector3(x1, 0.5f, 0), new Vector3(0.35f, 1.0f, 21.2f), "concrete");
            vb.Box(new Vector3(x1, 1.03f, 0), new Vector3(0.45f, 0.06f, 21.3f), "offwhite");
            // Cushioned benches along the parapet with throw pillows, lanterns between them.
            string[] seat = { "velvetpurple", "petrol", "velvetpurple", "petrol", "velvetpurple" };
            for (int i = 0; i < 5; i++)
            {
                float z = -8.4f + i * 4.2f;
                vb.Box(new Vector3(x1 - 0.55f, 0.21f, z), new Vector3(0.72f, 0.42f, 3.3f), "honey");
                vb.Box(new Vector3(x1 - 0.55f, 0.47f, z), new Vector3(0.68f, 0.1f, 3.2f), seat[i]);
                vb.Box(new Vector3(x1 - 0.27f, 0.72f, z), new Vector3(0.16f, 0.42f, 3.1f), seat[(i + 1) % 5]);
                vb.Box(new Vector3(x1 - 0.42f, 0.72f, z + 0.9f), new Vector3(0.14f, 0.36f, 0.48f), "terracotta", 12f);
                vb.Box(new Vector3(x1 - 0.42f, 0.7f, z - 1.0f), new Vector3(0.14f, 0.34f, 0.44f), "dustpink", -10f);
                Lantern(vb, new Vector3(x1 - 0.55f, 0f, z + 2.1f));
            }
            // Planters with greenery by the glazing, palms at the ends.
            foreach (float z in new[] { -6.3f, 0.2f, 6.6f })
            {
                vb.Box(new Vector3(x0 + 0.75f, 0.3f, z), new Vector3(0.9f, 0.6f, 1.6f), "darkconcrete");
                vb.Model("houseplant_bush", x0 + 0.75f, z - 0.35f, 0f, y: 0.6f, scale: 0.75f);
                vb.Model("houseplant_bush", x0 + 0.75f, z + 0.4f, 90f, y: 0.6f, scale: 0.65f);
            }
            vb.Model("palm_2", x0 + 3.1f, 9.6f, 0f, scale: 0.5f);
            vb.Model("palm_1", x0 + 3.3f, -9.8f, 0f, scale: 0.45f);
            vb.Model("palm_1", x1 + 1.6f, 3.4f, 40f, scale: 0.55f);
            // Zig-zag string lights from the glazing header to posts on the parapet.
            for (int i = 0; i < 8; i++)
            {
                float zp = -9.8f + i * 2.8f;
                vb.Pipe(new Vector3(x1, 1.06f, zp), new Vector3(x1, 3.3f, zp), 0.03f, "black", 6);
                var a = new Vector3(x0 - 0.15f, 4.75f, zp - 1.4f);
                var b = new Vector3(x1, 3.25f, zp);
                foreach (var (p, q) in new[] { (a, b), (b, a + new Vector3(0, 0, 2.8f)) })
                {
                    vb.Pipe(p, q, 0.007f, "black", 4);
                    for (int k = 1; k < 6; k++)
                    {
                        float t = k / 6f;
                        vb.Sphere(Vector3.Lerp(p, q, t) + Vector3.down * (0.25f * Mathf.Sin(t * Mathf.PI) + 0.05f), 0.05f, "glowwhite", glow: true, rings: 3, segments: 5);
                    }
                }
            }
            // One multi-country flag bunting across the terrace (each flag turned towards the room and the camera).
            Bunting(vb, new Vector3(x0 - 0.1f, 4.6f, -4.5f), new Vector3(x1 + 0.4f, 3.7f, 9.8f), 23);
            vb.PointLight(new Vector3(x0 + 2.5f, 2.4f, -3f), "glowamber", 1.0f, 8f);
            vb.PointLight(new Vector3(x0 + 2.5f, 2.4f, 5f), "glowamber", 1.0f, 8f);

            // Evening city east and north (backdrop: not lightmapped) under the painted sky with the mountain range.
            vb.Backdrop = true;
            string[] towers = { "tower_glass_a", "building_low_a", "tower_glass_b", "building_low_d", "tower_glass_c", "tower_glass_d", "building_low_b", "tower_glass_e" };
            int n = 0;
            for (float z = -34f; z <= 46f; z += 8.5f)
            {
                vb.Model(towers[n % towers.Length], 31f + (n % 3) * 8f, z, -90f, scale: 0.5f + (n % 4) * 0.1f);
                n++;
            }
            for (float xx = -40f; xx <= 40f; xx += 11f) vb.Model(towers[n++ % towers.Length], xx, 50f + (n % 2) * 8f, 180f, scale: 0.75f);
            vb.Sign("ss_sky", new Vector3(95f, -8f, 8f), 240f, -90f, 110f);
            vb.Sign("ss_sky", new Vector3(0f, -8f, 86f), 240f, 180f, 110f);
            vb.Backdrop = false;
        }

        private static void Lantern(VB vb, Vector3 floor)
        {
            vb.Box(floor + new Vector3(0, 0.02f, 0), new Vector3(0.28f, 0.04f, 0.28f), "black");
            vb.Box(floor + new Vector3(0, 0.24f, 0), new Vector3(0.22f, 0.38f, 0.22f), "glowamber", glow: true);
            foreach (var (dx, dz) in new[] { (-0.11f, -0.11f), (0.11f, -0.11f), (-0.11f, 0.11f), (0.11f, 0.11f) })
                vb.Box(floor + new Vector3(dx, 0.24f, dz), new Vector3(0.03f, 0.42f, 0.03f), "black");
            vb.Box(floor + new Vector3(0, 0.46f, 0), new Vector3(0.28f, 0.05f, 0.28f), "black");
            vb.Pipe(floor + new Vector3(0, 0.48f, 0), floor + new Vector3(0, 0.56f, 0), 0.05f, "black", 6);
            vb.PointLight(floor + new Vector3(0, 0.35f, 0), "glowamber", 0.5f, 2.2f);
        }

        private static void Bunting(VB vb, Vector3 a, Vector3 b, int flags)
        {
            Vector3 Cord(float t) => Vector3.Lerp(a, b, t) + Vector3.down * (0.8f * (1f - (2f * t - 1f) * (2f * t - 1f)));
            for (int i = 0; i < 24; i++) vb.Pipe(Cord(i / 24f), Cord((i + 1) / 24f), 0.008f, "offwhite", 4);
            for (int i = 0; i < flags; i++)
            {
                var p = Cord((i + 0.5f) / flags);
                string id = "ss_flag_" + (i % 4 == 0 ? (i / 4) % 2 : 2 + (i * 5) % 14);
                vb.Sign(id, p - new Vector3(0, 0.36f, 0), 0.5f, 215f, 0.34f, lit: true);
            }
        }

        // ------------------------------------------------------------------ north wall

        private static void NorthWall(VB vb, Rect inn)
        {
            float z = inn.yMax;
            vb.OnWall('N', 0f, 1.35f, 8.0f, "ss_waveform_idle", hook: "SpacesWaveform", frame: "black");
            vb.Box(new Vector3(0, 0.9f, z - 0.2f), new Vector3(8.4f, 0.9f, 0.3f), "darkwood");
            vb.Box(new Vector3(0, 5.15f, z - 0.25f), new Vector3(8.6f, 0.4f, 0.5f), "darkwood");
            // Walnut slat field across the wall.
            foreach (float sx in new[] { -1f, 1f })
                for (int i = 0; i < 40; i++)
                    vb.Box(new Vector3(sx * (4.3f + i * 0.2f), 3.1f, z - 0.05f), new Vector3(0.09f, 6.0f, 0.1f), i % 4 == 0 ? "darkwood" : "walnut");
            // Fluted fabric acoustic panels flanking the LED wall (purple west, teal + purple east) and outer pairs.
            foreach (var (px, col, trim) in new[] { (-4.95f, "velvetpurple", "plum"), (-6.2f, "plum", "velvetpurple"), (4.95f, "petrol", "teal2"), (6.2f, "velvetpurple", "plum"),
                                                     (-9.6f, "petrol", "teal2"), (9.6f, "velvetpurple", "plum") })
            {
                vb.Box(new Vector3(px, 2.75f, z - 0.16f), new Vector3(1.12f, 3.7f, 0.1f), trim);
                for (int f = 0; f < 5; f++)
                    vb.Cylinder(new Vector3(px - 0.44f + f * 0.22f, 0.92f, z - 0.24f), 0.11f, 3.66f, col, 8);
                vb.Box(new Vector3(px, 0.86f, z - 0.2f), new Vector3(1.16f, 0.06f, 0.22f), "darkwood");
                vb.Box(new Vector3(px, 4.62f, z - 0.2f), new Vector3(1.16f, 0.06f, 0.22f), "darkwood");
            }
            // Spot cans along the header, aimed down the wall.
            for (int i = 0; i < 8; i++)
            {
                float x = -10.5f + i * 3f;
                vb.Box(new Vector3(x, 5.45f, z - 0.35f), new Vector3(0.08f, 0.25f, 0.08f), "black");
                vb.Cylinder(new Vector3(x, 5.08f, z - 0.42f), 0.1f, 0.26f, "black", 10);
                vb.Cylinder(new Vector3(x, 5.06f, z - 0.42f), 0.075f, 0.02f, "glowwhite", 10, glow: true);
            }
            // On-air lamp: housing + tintable red dome (idle dim; integration lights it only when a Space is live).
            vb.Cylinder(new Vector3(5.0f, 5.3f, z - 0.5f), 0.3f, 0.06f, "black", 18);
            vb.HookGlow("OnAirLamp", new Vector3(5.0f, 5.62f, z - 0.55f), new Vector3(0.44f, 0.44f, 0.3f), "crimson", sphere: true);
            // Speakers on tripod stands flanking the stage.
            foreach (float x in new[] { -4.2f, 4.2f })
            {
                var f = new Vector3(x, 0f, 9.6f);
                for (int l = 0; l < 3; l++)
                {
                    float a = l * 120f * Mathf.Deg2Rad;
                    vb.Pipe(f + new Vector3(Mathf.Cos(a) * 0.38f, 0f, Mathf.Sin(a) * 0.38f), f + Vector3.up * 0.75f, 0.015f, "black", 5);
                }
                vb.Pipe(f + Vector3.up * 0.6f, f + Vector3.up * 1.55f, 0.022f, "black", 6);
                vb.Model("studio_speaker", x, 9.6f, x < 0 ? 165f : -165f, y: 1.55f, scale: 1.15f);
                vb.RegisterProp("speaker_stand", f + Vector3.up * 0.75f, new Vector3(0.6f, 1.5f, 0.6f));
            }
        }

        // ------------------------------------------------------------------ stage

        private static void Stage(VB vb)
        {
            vb.WalkablePrism("StageStep", C, StepR, StepH, 48);
            vb.WalkablePrism("StageTop", C, StageR, StageH, 48);
            vb.Cylinder(C, StepR, StepH, "honey", 48);
            vb.Cylinder(C + Vector3.up * StepH, StageR, StageH - StepH, "walnut", 48);
            // Oak nosing bands (open rings, no caps: nothing coplanar with the step tops).
            vb.Arc(C, StepR - 0.03f, StepR + 0.03f, 0f, 360f, StepH - 0.02f, StepH + 0.003f, "oak", 64);
            vb.Arc(C, StageR - 0.03f, StageR + 0.03f, 0f, 360f, StageH - 0.02f, StageH + 0.003f, "oak", 64);
            // Amber LED strips tucked under both nosings (unlit), with low baked lights for the spill on the floor.
            vb.Arc(C, StepR, StepR + 0.012f, 0f, 360f, StepH - 0.045f, StepH - 0.02f, "glowamber", 64, glow: true);
            vb.Arc(C, StageR, StageR + 0.012f, 0f, 360f, StageH - 0.045f, StageH - 0.02f, "glowamber", 64, glow: true);
            for (int i = 0; i < 7; i++)
            {
                float phi = -90f + i * 30f;
                vb.PointLight(Polar(StepR + 0.45f, phi, 0.32f), "glowamber", 0.28f, 2.1f);
                if (i % 2 == 1) vb.PointLight(Polar(StageR + 0.3f, phi, StepH + 0.3f), "glowamber", 0.2f, 1.6f);
            }
            // Cream woven round rug (geometry: a disc with woven rings and a dot border) and a brass inlay ring.
            var rug = new Vector3(0f, StageH, 5.0f);
            vb.Cylinder(rug, 2.75f, 0.012f, "linen", 48);
            vb.RingDecal(rug + Vector3.up * 0.012f, 2.55f, 0.06f, "tan", 48);
            vb.RingDecal(rug + Vector3.up * 0.012f, 2.38f, 0.025f, "walnut", 48);
            vb.RingDecal(rug + Vector3.up * 0.012f, 1.55f, 0.05f, "tan", 40);
            vb.RingDecal(rug + Vector3.up * 0.012f, 1.42f, 0.02f, "walnut", 40);
            for (int i = 0; i < 20; i++)
            {
                float a = i * Mathf.PI * 2f / 20f;
                vb.Cylinder(rug + new Vector3(Mathf.Cos(a) * 1.98f, 0.012f, Mathf.Sin(a) * 1.98f), 0.05f, 0.004f, "walnut", 8);
            }
            vb.RingDecal(C + Vector3.up * StageH, 4.05f, 0.04f, "brass", 56);

            // Host desk (the host stands behind it at stage_host): sun lamp, mixer, laptop, plant, boom mic.
            var desk = new Vector3(0f, StageH, 7.2f);
            vb.Box(desk + new Vector3(0, 0.41f, 0.02f), new Vector3(2.1f, 0.82f, 0.5f), "walnut");
            vb.Box(desk + new Vector3(0, 0.845f, 0), new Vector3(2.25f, 0.05f, 0.58f), "oak");
            vb.Box(desk + new Vector3(0, 0.05f, -0.24f), new Vector3(2.0f, 0.02f, 0.02f), "glowamber", glow: true);
            for (int i = 0; i < 5; i++) vb.Box(desk + new Vector3(-0.84f + i * 0.42f, 0.42f, -0.235f), new Vector3(0.03f, 0.7f, 0.02f), "darkwood");
            vb.RegisterProp("host_desk", desk + new Vector3(0, 0.44f, 0), new Vector3(2.25f, 0.88f, 0.58f));
            float top = StageH + 0.87f;
            SunLamp(vb, new Vector3(-0.8f, top, 7.25f));
            vb.Box(new Vector3(-0.1f, top + 0.03f, 7.2f), new Vector3(0.55f, 0.06f, 0.3f), "charcoal", 4f);
            for (int i = 0; i < 8; i++)
                vb.Sphere(new Vector3(-0.33f + i * 0.065f, top + 0.07f, 7.12f), 0.013f, i % 3 == 0 ? "glowgreen" : i % 3 == 1 ? "glowamber" : "glowred", glow: true, rings: 3, segments: 5);
            for (int i = 0; i < 4; i++) vb.Box(new Vector3(-0.3f + i * 0.08f, top + 0.075f, 7.27f), new Vector3(0.02f, 0.03f, 0.08f), "lightgrey");
            vb.ModelAt("laptop", new Vector3(0.5f, top, 7.22f), 180f);
            vb.ModelAt("plant_small", new Vector3(0.95f, top, 7.3f), 0f, 1.5f);
            Phone(vb, new Vector3(0.18f, top + 0.006f, 7.05f), flat: true);
            BoomMic(vb, new Vector3(0.15f, top, 7.42f), new Vector3(0f, StageH + 1.5f, 7.72f));

            // Guest armchairs (stage_4..7) in a shallow talk-show arc facing the room. The fitted social exit steps to the
            // seat's left (CafeSeatMotionProfile), so each approach is 0.95 m to the left and the boom mic stands front-right.
            string[] cushion = { "velvetpurple", "petrol", "ctteal", "dustpink" };
            int k = 0;
            foreach (var s in vb.SpotsWhere(s => s.id == "stage_4" || s.id == "stage_5" || s.id == "stage_6" || s.id == "stage_7"))
            {
                var p = VB.P(s);
                var fwd = Fwd(s);
                var right = Right(s);
                vb.ModelAt("home_armchair_pillows", p - fwd * 0.12f + Vector3.up * StageH, VB.Deg(s), 0.85f, recolor: "tan");
                var pillow = p - fwd * 0.28f + right * 0.2f + Vector3.up * (StageH + 0.58f);
                vb.Box(pillow, new Vector3(0.38f, 0.34f, 0.14f), cushion[k++ % 4], VB.Deg(s) - 12f);
                vb.SeatCollider(s, new Vector3(0, 0.5f, -0.1f), new Vector3(0.9f, 0.6f, 0.9f));
                vb.Approach(s, p - right * 0.95f);
                MicStand(vb, p + fwd * 0.6f + right * 0.25f + Vector3.up * StageH, p + fwd * 0.35f + Vector3.up * (StageH + 1.1f), phone: false);
            }
            // Low round table in front of the guests: mugs, a small plant, a book stack.
            var table = new Vector3(0f, StageH, 4.0f);
            vb.Cylinder(table, 0.42f, 0.36f, "walnut", 20);
            vb.Cylinder(table + Vector3.up * 0.36f, 0.5f, 0.045f, "oak", 24);
            vb.RegisterProp("coffee_table", table + Vector3.up * 0.2f, new Vector3(1.0f, 0.42f, 1.0f));
            float tt = StageH + 0.405f;
            vb.Cylinder(new Vector3(-0.22f, tt, 4.08f), 0.045f, 0.09f, "offwhite", 10);
            vb.Cylinder(new Vector3(0.24f, tt, 3.9f), 0.045f, 0.09f, "charcoal", 10);
            vb.ModelAt("plant_small", new Vector3(0.05f, tt, 4.15f), 0f, 1.3f);
            vb.Box(new Vector3(0.12f, tt + 0.02f, 3.78f), new Vector3(0.26f, 0.04f, 0.2f), "barnred", 18f);
            vb.Box(new Vector3(0.12f, tt + 0.055f, 3.78f), new Vector3(0.24f, 0.03f, 0.18f), "cream", 10f);
            // Cable runs (flush), plants at the back of the stage.
            vb.Decal(-1.5f, 6.5f, 2.6f, 0.03f, "black", 18f, StageH + 0.008f);
            vb.Decal(1.5f, 6.5f, 2.6f, 0.03f, "black", -18f, StageH + 0.008f);
            vb.ModelAt("houseplant_bush", new Vector3(-2.9f, StageH, 8.4f), 150f, 1.0f);
            vb.ModelAt("houseplant_bush", new Vector3(2.9f, StageH, 8.4f), 210f, 1.0f);
            vb.ModelAt("houseplant_bush", new Vector3(-2.3f, StageH, 9.15f), 180f, 0.6f);
            vb.ModelAt("houseplant_bush", new Vector3(2.4f, StageH, 9.15f), 160f, 0.6f);
        }

        private static void SunLamp(VB vb, Vector3 basePoint)
        {
            vb.Cylinder(basePoint, 0.1f, 0.03f, "brass", 12);
            vb.Cylinder(basePoint + Vector3.up * 0.03f, 0.015f, 0.2f, "brass", 6);
            var c = basePoint + new Vector3(0, 0.34f, 0);
            vb.Sphere(c, 0.1f, "glowgold", glow: true);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                vb.Pipe(c + dir * 0.12f, c + dir * (i % 2 == 0 ? 0.25f : 0.19f), 0.014f, "glowamber", 4, glow: true);
            }
            vb.PointLight(c + new Vector3(0, 0.1f, -0.3f), "glowamber", 1.0f, 3.5f);
        }

        /// <summary>Chunky phone with a three-lens camera block (generic, no marks).</summary>
        private static void Phone(VB vb, Vector3 center, bool flat, float facing = 180f)
        {
            if (flat)
            {
                vb.Box(center, new Vector3(0.085f, 0.012f, 0.17f), "black");
                for (int i = 0; i < 3; i++) vb.Cylinder(center + new Vector3(-0.022f + (i % 2) * 0.022f, 0.006f, 0.045f + (i / 2) * 0.022f), 0.009f, 0.004f, "darkgrey", 8);
                return;
            }
            var q = Quaternion.Euler(0, facing, 0);
            vb.Box(center, new Vector3(0.085f, 0.17f, 0.012f), "black", facing);
            for (int i = 0; i < 3; i++)
                vb.Sphere(center + q * new Vector3(-0.02f + (i % 2) * 0.022f, 0.045f + (i / 2) * 0.022f, -0.008f), 0.009f, "darkgrey", rings: 3, segments: 6);
        }

        private static void BoomMic(VB vb, Vector3 clamp, Vector3 capsule)
        {
            var elbow = new Vector3(clamp.x, capsule.y + 0.35f, (clamp.z + capsule.z) * .5f + 0.2f);
            vb.Box(clamp, new Vector3(0.06f, 0.08f, 0.06f), "darksteel");
            vb.Pipe(clamp, elbow, 0.014f, "darksteel", 6);
            vb.Pipe(elbow, capsule + Vector3.up * 0.08f, 0.012f, "darksteel", 6);
            vb.Cylinder(capsule - Vector3.up * 0.08f, 0.034f, 0.18f, "charcoal", 10);
            vb.Cylinder(capsule + new Vector3(0, -0.02f, -0.09f), 0.07f, 0.006f, "black", 12);
        }

        /// <summary>Floor mic stand with a boom to the capsule; optional three-lens phone holder.</summary>
        private static void MicStand(VB vb, Vector3 floor, Vector3 capsule, bool phone)
        {
            vb.Cylinder(floor, 0.2f, 0.03f, "darksteel", 12);
            var top = floor + Vector3.up * 1.12f;
            vb.Pipe(floor, top, 0.016f, "darksteel", 6);
            vb.Pipe(top, capsule + Vector3.up * 0.06f, 0.012f, "darksteel", 6);
            vb.Cylinder(capsule - Vector3.up * 0.08f, 0.033f, 0.17f, "charcoal", 10);
            vb.Sphere(capsule + Vector3.up * 0.1f, 0.036f, "darkgrey", rings: 4, segments: 8);
            if (phone)
            {
                var dir = new Vector3(capsule.x - floor.x, 0f, capsule.z - floor.z).normalized;
                var holder = floor + new Vector3(0, 0.98f, 0);
                vb.Pipe(holder, holder + dir * 0.12f, 0.008f, "darksteel", 4);
                Phone(vb, holder + dir * 0.16f + Vector3.up * 0.02f, false, Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg);
            }
            vb.RegisterProp("mic_stand", floor + Vector3.up * 0.6f, new Vector3(0.4f, 1.2f, 0.4f));
        }

        // ------------------------------------------------------------------ speaker pods (stage_1..3, stage_8..10)

        private static void Pods(VB vb)
        {
            string[] cushion = { "rose", "petrol", "velvetpurple" };
            int i = 0;
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("stage_") && s.id != "stage_host" && vb.GroundAt(VB.P(s)) < 0.01f))
            {
                var p = VB.P(s);
                var fwd = Fwd(s);
                var right = Right(s);
                vb.ModelAt("bar_stool", p, VB.Deg(s));
                vb.Cylinder(p + Vector3.up * 0.75f, 0.2f, 0.06f, cushion[i++ % 3], 14);
                vb.SeatCollider(s, new Vector3(0, 0.5f, 0), new Vector3(0.55f, 0.6f, 0.55f));
                vb.Approach(s, p - right * 0.95f); // fitted social exit steps left on stage_*
                MicStand(vb, p + fwd * 0.5f + right * 0.3f, p + fwd * 0.28f + Vector3.up * 1.4f, phone: true);
            }
        }

        // ------------------------------------------------------------------ audience: bean bags, curved risers, poufs

        private static void Audience(VB vb)
        {
            // Rugs under the front rows (flush).
            vb.FloorSign("ss_rug_rect", 0f, -2.5f, 7.2f, 0f, 3.4f, 0f, lit: true);
            vb.FloorSign("ss_rug_grey", -4.9f, -1.3f, 3.6f, 32f, 1.9f, 0f, lit: true);
            vb.FloorSign("ss_rug_grey", 4.9f, -1.3f, 3.6f, -32f, 1.9f, 0f, lit: true);

            // Curved riser decks with amber LED nosing and low spill lights.
            float a0 = 180f - TierSpan, a1 = 180f + TierSpan;
            vb.WalkableArc("Tier1", C, T1In, T1Out, a0, a1, Tier1, 40);
            vb.WalkableArc("Tier2", C, T1Out, T2Out, a0, a1, Tier2, 40);
            vb.Arc(C, T1In, T1Out, a0, a1, 0f, Tier1, "honey", 40);
            vb.Arc(C, T1Out, T2Out, a0, a1, 0f, Tier2, "oak", 40);
            vb.Arc(C, T1In - 0.03f, T1In + 0.02f, a0, a1, Tier1 - 0.02f, Tier1 + 0.003f, "oak", 40);
            vb.Arc(C, T1Out - 0.03f, T1Out + 0.02f, a0, a1, Tier2 - 0.02f, Tier2 + 0.003f, "honey", 40);
            vb.Arc(C, T1In - 0.02f, T1In - 0.005f, a0, a1, Tier1 - 0.05f, Tier1 - 0.025f, "glowamber", 40, glow: true);
            vb.Arc(C, T1Out - 0.02f, T1Out - 0.005f, a0, a1, Tier2 - 0.05f, Tier2 - 0.025f, "glowamber", 40, glow: true);
            for (int i = 0; i < 6; i++)
            {
                float phi = -TierSpan + 4f + i * (2f * TierSpan - 8f) / 5f;
                vb.PointLight(Polar(T1In - 0.5f, phi, 0.32f), "glowamber", 0.28f, 2.1f);
                vb.PointLight(Polar(T1Out - 0.45f, phi, Tier1 + 0.3f), "glowamber", 0.22f, 1.8f);
            }
            // Patterned rugs on the risers between the poufs.
            foreach (float r in new[] { 12.2f, 14.05f })
                foreach (float phi in new[] { -18f, 0f, 18f })
                    vb.FloorSign("ss_rug_grey", Polar(r, phi).x, Polar(r, phi).z, 2.6f, -phi, 1.25f, r < 13f ? Tier1 : Tier2, lit: true);

            string[] bags = { "rose", "petrol", "dustpink", "velvetpurple", "rose" };
            string[] poufs = { "velvetpurple", "petrol", "plum", "ctteal", "velvetpurple" };
            int k = 0;
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("seat_")))
            {
                var p = VB.P(s);
                var fwd = Fwd(s);
                float g = vb.GroundAt(p);
                if (g < 0.01f)
                {
                    string col = bags[k % bags.Length];
                    vb.Blob(p - fwd * 0.08f + Vector3.up * 0.34f, new Vector3(1.3f, 0.7f, 1.2f), col, yaw: VB.Deg(s), rings: 9, segments: 16);
                    vb.Blob(p - fwd * 0.5f + Vector3.up * 0.62f, new Vector3(1.15f, 0.7f, 0.5f), col, yaw: VB.Deg(s), rings: 8, segments: 14);
                    vb.RegisterProp("bean_bag", p - fwd * 0.15f + Vector3.up * 0.34f, new Vector3(1.2f, 0.68f, 1.2f), VB.Deg(s));
                    vb.SeatCollider(s, new Vector3(0, 0.35f, -0.08f), new Vector3(0.9f, 0.5f, 0.9f));
                    vb.Approach(s, p + Right(s) * 0.95f); // fitted social exit steps right on seat_*
                }
                else
                {
                    string col = poufs[k % poufs.Length];
                    vb.Box(p + Vector3.up * (g + 0.19f), new Vector3(0.74f, 0.38f, 0.74f), col, VB.Deg(s));
                    vb.Box(p + Vector3.up * (g + 0.4f), new Vector3(0.7f, 0.06f, 0.7f), poufs[(k + 2) % poufs.Length], VB.Deg(s));
                    vb.RegisterProp("pouf", p + Vector3.up * (g + 0.22f), new Vector3(0.74f, 0.44f, 0.74f), VB.Deg(s));
                    vb.SeatCollider(s, new Vector3(0, 0.3f, 0), new Vector3(0.7f, 0.5f, 0.7f));
                    vb.Approach(s, p + Right(s) * 0.95f);
                }
                k++;
            }
            // Extra floor cushions and drink tables on the risers (decor, collided).
            foreach (var (r, phi, col) in new[] { (12.3f, -17.8f, "petrol"), (12.3f, 22.3f, "velvetpurple"), (14.1f, 7.7f, "rose"), (14.1f, -29f, "plum") })
            {
                var p = Polar(r, phi, r < 13f ? Tier1 : Tier2);
                vb.Box(p + Vector3.up * 0.1f, new Vector3(0.62f, 0.2f, 0.62f), col, -phi + 8f);
                vb.RegisterProp("floor_cushion", p + Vector3.up * 0.1f, new Vector3(0.62f, 0.2f, 0.62f), -phi + 8f);
            }
            foreach (var (r, phi) in new[] { (14.15f, -15.3f), (14.15f, 19.2f), (12.25f, 9.0f) })
                SideTable(vb, Polar(r, phi, r < 13f ? Tier1 : Tier2), "darkwood", true);
            // A big decor bean bag by the east front corner.
            DecorBag(vb, new Vector3(8.6f, 0f, -0.8f), "rose", -65f);
        }

        private static void DecorBag(VB vb, Vector3 p, string col, float facing)
        {
            var fwd = Quaternion.Euler(0, facing, 0) * Vector3.forward;
            vb.Blob(p - fwd * 0.08f + Vector3.up * 0.34f, new Vector3(1.25f, 0.68f, 1.15f), col, yaw: facing, rings: 9, segments: 16);
            vb.Blob(p - fwd * 0.48f + Vector3.up * 0.6f, new Vector3(1.1f, 0.66f, 0.48f), col, yaw: facing, rings: 8, segments: 14);
            vb.RegisterProp("bean_bag", p - fwd * 0.15f + Vector3.up * 0.34f, new Vector3(1.15f, 0.68f, 1.15f), facing);
        }

        private static void SideTable(VB vb, Vector3 floor, string wood, bool drink)
        {
            vb.Cylinder(floor, 0.2f, 0.02f, "black", 12);
            vb.Cylinder(floor, 0.03f, 0.5f, "darksteel", 6);
            vb.Cylinder(floor + Vector3.up * 0.5f, 0.24f, 0.035f, wood, 16);
            if (drink) vb.Cylinder(floor + new Vector3(0.05f, 0.535f, 0.02f), 0.035f, 0.12f, "charcoal", 8);
            vb.RegisterProp("side_table", floor + Vector3.up * 0.27f, new Vector3(0.48f, 0.54f, 0.48f));
        }

        // ------------------------------------------------------------------ backstage block (north-west): lanyards, neon arch door

        private static void Backstage(VB vb, Rect inn)
        {
            float x0 = inn.xMin, d0 = DoorX - DoorW * .5f, d1 = DoorX + DoorW * .5f, zN = inn.yMax;
            vb.Wall(new Vector2(x0 - 0.1f, BackZ), new Vector2(d0, BackZ), BackH, "sand", 0.2f);
            vb.Wall(new Vector2(d1, BackZ), new Vector2(BackX + 0.1f, BackZ), BackH, "sand", 0.2f);
            vb.Box(new Vector3(DoorX, (DoorH + BackH) * .5f, BackZ), new Vector3(DoorW, BackH - DoorH, 0.2f), "sand");
            vb.Wall(new Vector2(BackX, BackZ - 0.1f), new Vector2(BackX, zN), BackH, "sand", 0.2f);
            // Walnut wainscot and a picture rail on the faces the room sees.
            vb.Box(new Vector3((x0 + d0) * .5f, 0.5f, BackZ - 0.115f), new Vector3(d0 - x0, 1.0f, 0.03f), "walnut");
            vb.Box(new Vector3((d1 + BackX) * .5f, 0.5f, BackZ - 0.115f), new Vector3(BackX - d1, 1.0f, 0.03f), "walnut");
            vb.Box(new Vector3(BackX + 0.115f, 0.5f, (BackZ + zN) * .5f), new Vector3(0.03f, 1.0f, zN - BackZ), "walnut");
            vb.Box(new Vector3((x0 + BackX) * .5f, 1.02f, BackZ - 0.13f), new Vector3(BackX - x0, 0.04f, 0.04f), "darkwood");
            vb.Box(new Vector3(BackX + 0.13f, 1.02f, (BackZ + zN) * .5f), new Vector3(0.04f, 0.04f, zN - BackZ), "darkwood");
            vb.Box(new Vector3((x0 + BackX) * .5f, BackH + 0.04f, BackZ), new Vector3(BackX - x0 + 0.2f, 0.08f, 0.26f), "darkwood");
            vb.Box(new Vector3(BackX, BackH + 0.04f, (BackZ + zN) * .5f), new Vector3(0.26f, 0.08f, zN - BackZ), "darkwood");
            vb.Obstacle(new Vector3((x0 + d0) * .5f, 1f, BackZ), new Vector3(d0 - x0, 2f, 0.26f));
            vb.Obstacle(new Vector3((d1 + BackX) * .5f, 1f, BackZ), new Vector3(BackX - d1, 2f, 0.26f));
            vb.Obstacle(new Vector3(BackX, 1f, (BackZ + zN) * .5f), new Vector3(0.26f, 2f, zN - BackZ));
            vb.Box(new Vector3((x0 + BackX) * .5f, 0.06f, BackZ - 0.11f), new Vector3(BackX - x0, 0.12f, 0.02f), "darkwood");
            // Door frame and the rainbow neon arch above it.
            foreach (float x in new[] { d0, d1 })
                vb.Box(new Vector3(x, DoorH * .5f, BackZ - 0.11f), new Vector3(0.1f, DoorH, 0.06f), "darkwood");
            vb.Box(new Vector3(DoorX, DoorH + 0.04f, BackZ - 0.11f), new Vector3(DoorW + 0.2f, 0.08f, 0.06f), "darkwood");
            foreach (var (r, col) in new[] { (0.95f, "glowpurple"), (0.8f, "glowblue"), (0.65f, "glowcyan") })
                NeonArc(vb, new Vector3(DoorX, DoorH + 0.1f, BackZ - 0.13f), r, col);
            vb.PointLight(new Vector3(DoorX, 2.9f, BackZ - 0.6f), "glowpurple", 0.8f, 3f);
            // Lanyard frame (three big conference lanyards) beside the door, as in the concept.
            vb.Sign("ss_lanyard_frame", new Vector3(-11.05f, 0.95f, BackZ - 0.12f), 1.65f, 180f, frame: "walnut", lit: true);
            // Small wall shelf with headphones right of the door.
            vb.Box(new Vector3(-7.9f, 1.45f, BackZ - 0.22f), new Vector3(0.8f, 0.04f, 0.24f), "oak");
            foreach (float x in new[] { -8.15f, -7.9f, -7.65f })
            {
                vb.Pipe(new Vector3(x, 1.6f, BackZ - 0.1f), new Vector3(x, 1.6f, BackZ - 0.2f), 0.01f, "darksteel", 4);
                vb.Blob(new Vector3(x, 1.5f, BackZ - 0.22f), new Vector3(0.18f, 0.2f, 0.07f), "black");
            }
            // Lanyard poster wall on the backstage block's east face (seen from the stage and the pods).
            vb.Sign("ss_lanyards", new Vector3(BackX + 0.12f, 1.0f, 4.6f), 3.6f, 90f, frame: "darkwood", lit: true);
            vb.Box(new Vector3(BackX + 0.16f, 2.62f, 4.6f), new Vector3(0.06f, 0.05f, 3.7f), "glowamber", glow: true);
            // Sponsor screen for the studio's contract ad slot (billboard_03): a LiveSurfaces billboard, stage feed when idle.
            vb.LiveScreen("bb_studio", "ss_greenroom_tv", new Vector3(BackX + 0.16f, 1.2f, 8.4f), 2.2f, 90f);

            // Backstage interior (seen from above through the open top): couch, rail, vanity mirror, fridge, plant.
            vb.Decal(-9.7f, 6.0f, 3.6f, 6.6f, "plum");
            vb.Model("home_couch_pillows", x0 + 0.5f, 6.8f, 90f, recolor: "velvetpurple");
            vb.Box(new Vector3(-9.6f, 0.2f, 6.8f), new Vector3(0.9f, 0.4f, 0.6f), "walnut");
            vb.RegisterProp("bs_table", new Vector3(-9.6f, 0.2f, 6.8f), new Vector3(0.9f, 0.4f, 0.6f));
            vb.Pipe(new Vector3(-11.6f, 1.7f, 3.0f), new Vector3(-11.6f, 1.7f, 5.0f), 0.02f, "chrome", 6);
            foreach (float z in new[] { 3.0f, 5.0f }) vb.Pipe(new Vector3(-11.6f, 0f, z), new Vector3(-11.6f, 1.7f, z), 0.02f, "chrome", 6);
            string[] coats = { "ctteal", "mustard", "plum", "denim", "barnred", "charcoal" };
            for (int i = 0; i < 6; i++) vb.Box(new Vector3(-11.6f, 1.25f, 3.25f + i * 0.3f), new Vector3(0.42f, 0.85f, 0.08f), coats[i], 4f * (i % 3 - 1));
            vb.RegisterProp("coat_rail", new Vector3(-11.6f, 0.85f, 4.0f), new Vector3(0.5f, 1.7f, 2.1f));
            vb.Box(new Vector3(BackX - 0.13f, 1.6f, 8.3f), new Vector3(0.04f, 0.9f, 1.2f), "chrome");
            for (int i = 0; i < 5; i++) vb.Sphere(new Vector3(BackX - 0.16f, 2.1f, 7.8f + i * 0.25f), 0.04f, "glowwhite", glow: true, rings: 3, segments: 6);
            vb.Box(new Vector3(BackX - 0.35f, 0.8f, 8.3f), new Vector3(0.45f, 0.06f, 1.3f), "oak");
            vb.Box(new Vector3(x0 + 0.4f, 0.75f, 9.4f), new Vector3(0.65f, 1.5f, 0.6f), "offwhite");
            vb.RegisterProp("bs_fridge", new Vector3(x0 + 0.4f, 0.75f, 9.4f), new Vector3(0.65f, 1.5f, 0.6f));
            vb.Plant(-8.1f, 9.4f, "houseplant_bush", 0.9f);
            vb.Plant(-8.1f, 2.4f, "houseplant_bush", 0.7f);
            vb.PointLight(new Vector3(-9.7f, 2.6f, 5.6f), "glowamber", 1.2f, 7f);
        }

        private static void NeonArc(VB vb, Vector3 springCenter, float r, string col)
        {
            Vector3 prev = springCenter + new Vector3(-r, 0, 0);
            vb.Pipe(prev + Vector3.down * 0.2f, prev, 0.028f, col, 6, glow: true);
            for (int i = 1; i <= 16; i++)
            {
                float t = Mathf.PI * i / 16f;
                var p = springCenter + new Vector3(-Mathf.Cos(t) * r, Mathf.Sin(t) * r, 0);
                vb.Pipe(prev, p, 0.028f, col, 6, glow: true);
                prev = p;
            }
            vb.Pipe(prev, prev + Vector3.down * 0.2f, 0.028f, col, 6, glow: true);
        }

        // ------------------------------------------------------------------ green-room lounge (west)

        private static void Lounge(VB vb, Rect inn)
        {
            float x0 = inn.xMin;
            vb.FloorSign("ss_rug_green", -10.0f, -1.4f, 4.6f, 90f, 3.4f, 0f, lit: true);
            // Teal sofa against the west wall (greenroom_1/2) with a purple and an orange pillow.
            vb.Model("lounge_sofa", x0 + 0.8f, -1.4f, 90f, recolor: "petrol");
            vb.Obstacle(new Vector3(x0 + 0.25f, 0.45f, -1.4f), new Vector3(0.4f, 0.9f, 2.2f));
            vb.Box(new Vector3(x0 + 0.5f, 0.68f, -2.25f), new Vector3(0.14f, 0.4f, 0.44f), "velvetpurple", 14f);
            vb.Blob(new Vector3(x0 + 0.52f, 0.66f, -0.5f), new Vector3(0.14f, 0.38f, 0.38f), "ctorange");
            foreach (var s in vb.SpotsWhere(s => s.id.StartsWith("greenroom_")))
            {
                vb.SeatCollider(s, new Vector3(0, 0.55f, -0.25f), new Vector3(0.8f, 0.5f, 0.7f));
                vb.Approach(s, new Vector3(-10.3f, 0f, s.z < -1.4f ? -2.8f : -0.05f));
            }
            // Low round table with a record player and headphones.
            var t = new Vector3(-10.0f, 0f, -1.4f);
            vb.Cylinder(t, 0.36f, 0.38f, "walnut", 18);
            vb.Cylinder(t + Vector3.up * 0.38f, 0.42f, 0.04f, "oak", 20);
            vb.RegisterProp("lounge_table", t + Vector3.up * 0.2f, new Vector3(0.84f, 0.42f, 0.84f));
            vb.Box(t + new Vector3(0.02f, 0.47f, 0.05f), new Vector3(0.42f, 0.1f, 0.34f), "charcoal");
            vb.Cylinder(t + new Vector3(0.0f, 0.52f, 0.05f), 0.14f, 0.008f, "black", 18);
            vb.Cylinder(t + new Vector3(0.0f, 0.528f, 0.05f), 0.04f, 0.004f, "ctorange", 10);
            vb.Blob(t + new Vector3(-0.22f, 0.47f, -0.22f), new Vector3(0.22f, 0.08f, 0.2f), "black");
            // Headphone shelf by the partition, a floor lamp, plants.
            var sh = new Vector3(-8.1f, 0f, -3.2f);
            vb.Box(sh + new Vector3(0, 0.55f, 0), new Vector3(0.55f, 1.1f, 0.36f), "oak");
            vb.Box(sh + new Vector3(0, 0.55f, -0.17f), new Vector3(0.47f, 1.0f, 0.02f), "walnut");
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 2; j++)
                    vb.Blob(sh + new Vector3(-0.12f + j * 0.24f, 0.24f + i * 0.33f, -0.16f), new Vector3(0.18f, 0.2f, 0.08f), i == 1 && j == 0 ? "ctteal" : "black");
            vb.RegisterProp("headphone_shelf", sh + Vector3.up * 0.55f, new Vector3(0.55f, 1.1f, 0.36f));
            vb.Model("floor_lamp", x0 + 0.45f, -3.25f, 90f);
            vb.PointLight(new Vector3(x0 + 0.6f, 1.55f, -3.1f), "glowamber", 1.1f, 5f);
            vb.Plant(-11.5f, 0.85f, "houseplant_bush", 0.8f);
            vb.Plant(-8.3f, -4.05f, "houseplant_bush", 1.0f);
            // Slatted wood partition between the lounge and the audience.
            vb.Box(new Vector3(PartX, 0.05f, (PartZ0 + PartZ1) * .5f), new Vector3(0.3f, 0.1f, PartZ1 - PartZ0), "darkwood");
            vb.Box(new Vector3(PartX, 2.0f, (PartZ0 + PartZ1) * .5f), new Vector3(0.24f, 0.06f, PartZ1 - PartZ0), "darkwood");
            for (float z = PartZ0 + 0.06f; z < PartZ1; z += 0.14f)
                vb.Box(new Vector3(PartX, 1.0f, z), new Vector3(0.12f, 1.9f, 0.07f), "walnut");
            vb.Obstacle(new Vector3(PartX, 1f, (PartZ0 + PartZ1) * .5f), new Vector3(0.3f, 2f, PartZ1 - PartZ0));
            vb.Collider(new Vector3(PartX, 1f, (PartZ0 + PartZ1) * .5f), new Vector3(0.3f, 2f, PartZ1 - PartZ0));
            vb.Box(new Vector3(PartX + 0.2f, 0.03f, (PartZ0 + PartZ1) * .5f), new Vector3(0.03f, 0.03f, PartZ1 - PartZ0), "glowamber", glow: true);

            // South-west corner: credenza with a table lamp and books, gig posters, a planter.
            var cr = new Vector3(x0 + 0.3f, 0f, -7.2f);
            vb.Box(cr + new Vector3(0, 0.36f, 0), new Vector3(0.48f, 0.72f, 1.9f), "walnut");
            vb.Box(cr + new Vector3(0.245f, 0.36f, 0), new Vector3(0.02f, 0.6f, 1.8f), "oak");
            vb.RegisterProp("credenza", cr + new Vector3(0, 0.36f, 0), new Vector3(0.48f, 0.72f, 1.9f));
            vb.Cylinder(cr + new Vector3(0, 0.72f, 0.55f), 0.08f, 0.3f, "brass", 10);
            vb.Cylinder(cr + new Vector3(0, 1.02f, 0.55f), 0.18f, 0.22f, "glowamber", 14, glow: true, topRadius: 0.12f);
            vb.PointLight(cr + new Vector3(0.4f, 1.1f, 0.55f), "glowamber", 0.8f, 3.5f);
            for (int i = 0; i < 4; i++) vb.Box(cr + new Vector3(0, 0.76f + i * 0.045f, -0.4f), new Vector3(0.3f, 0.04f, 0.22f), new[] { "barnred", "cream", "petrol", "mustard" }[i], i * 6f);
            vb.ModelAt("plant_small", cr + new Vector3(0, 0.72f, -0.05f), 0f, 1.6f);
            for (int i = 0; i < 3; i++)
                vb.Sign("ss_poster_" + (i + 1), new Vector3(x0 + 0.04f, 1.3f, -8.6f + i * 1.2f), 0.7f, 90f, frame: "black", lit: true);
        }

        // ------------------------------------------------------------------ east side (inside the glazing)

        private static void EastSide(VB vb, Rect inn)
        {
            // Broadcast camera on a tripod aimed at the stage.
            BroadcastCamera(vb, new Vector3(7.2f, 0f, 2.6f), C + Vector3.up * 1.0f);
            // Window nook: rug, two bean bags, a side table, a floor lamp.
            vb.FloorSign("ss_rug_grey", 10.2f, 0.6f, 3.6f, 90f, 2.6f, 0f, lit: true);
            DecorBag(vb, new Vector3(10.3f, 0f, 1.6f), "petrol", -110f);
            DecorBag(vb, new Vector3(10.4f, 0f, -0.5f), "dustpink", -70f);
            SideTable(vb, new Vector3(11.3f, 0f, 0.55f), "oak", true);
            vb.Model("floor_lamp", 11.55f, -1.9f, -90f);
            vb.PointLight(new Vector3(11.3f, 1.55f, -1.8f), "glowamber", 1.0f, 4.5f);
            // Plants along the glazing and in the corners.
            vb.Plant(11.4f, 9.35f, "houseplant_bush", 1.1f);
            vb.Plant(8.9f, 9.5f, "houseplant_bush", 0.8f);
            vb.Plant(11.45f, 4.3f, "houseplant_bush", 0.95f);
            vb.Plant(11.5f, -4.4f, "houseplant_bush", 0.9f);
            // Bar cart with bottles and glasses.
            var cart = new Vector3(10.9f, 0f, 6.6f);
            vb.Box(cart + new Vector3(0, 0.45f, 0), new Vector3(0.5f, 0.03f, 0.9f), "brass");
            vb.Box(cart + new Vector3(0, 0.85f, 0), new Vector3(0.5f, 0.03f, 0.9f), "brass");
            foreach (var (dx, dz) in new[] { (-0.23f, -0.43f), (0.23f, -0.43f), (-0.23f, 0.43f), (0.23f, 0.43f) })
                vb.Box(cart + new Vector3(dx, 0.45f, dz), new Vector3(0.025f, 0.9f, 0.025f), "brass");
            string[] bottle = { "forest", "barnred", "mustard", "denim", "charcoal" };
            for (int i = 0; i < 5; i++) vb.Cylinder(cart + new Vector3(-0.1f + (i % 2) * 0.2f, 0.865f, -0.3f + i * 0.15f), 0.04f, 0.26f, bottle[i], 8, topRadius: 0.025f);
            vb.RegisterProp("bar_cart", cart + Vector3.up * 0.45f, new Vector3(0.5f, 0.9f, 0.9f));
            // South-east corner: big concrete planter, pouf and side table.
            var pl = new Vector3(10.9f, 0f, -8.3f);
            vb.Box(pl + new Vector3(0, 0.45f, 0), new Vector3(1.5f, 0.9f, 1.5f), "concrete");
            vb.Box(pl + new Vector3(0, 0.9f, 0), new Vector3(1.6f, 0.05f, 1.6f), "offwhite");
            vb.Model("houseplant_bush", pl.x, pl.z, 0f, y: 0.92f, scale: 1.3f);
            vb.RegisterProp("planter_big", pl + Vector3.up * 0.45f, new Vector3(1.5f, 0.9f, 1.5f));
            vb.Cylinder(new Vector3(9.3f, 0f, -8.9f), 0.3f, 0.4f, "velvetpurple", 16);
            vb.RegisterProp("pouf_decor", new Vector3(9.3f, 0.2f, -8.9f), new Vector3(0.6f, 0.4f, 0.6f));
            SideTable(vb, new Vector3(9.4f, 0f, -7.6f), "oak", true);
        }

        private static void BroadcastCamera(VB vb, Vector3 floor, Vector3 target)
        {
            for (int l = 0; l < 3; l++)
            {
                float a = (l * 120f + 30f) * Mathf.Deg2Rad;
                vb.Pipe(floor + new Vector3(Mathf.Cos(a) * 0.45f, 0f, Mathf.Sin(a) * 0.45f), floor + Vector3.up * 1.1f, 0.018f, "black", 5);
            }
            var head = floor + Vector3.up * 1.32f;
            var dir = (target - head); dir.y = 0; dir.Normalize();
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            vb.Cylinder(floor + Vector3.up * 1.1f, 0.06f, 0.12f, "darksteel", 8);
            vb.Box(head, new Vector3(0.24f, 0.24f, 0.46f), "charcoal", yaw);
            vb.Pipe(head + dir * 0.2f, head + dir * 0.42f, 0.085f, "black", 12);
            vb.Cylinder(head + dir * 0.43f - Vector3.up * 0.0f, 0.07f, 0.005f, "glowblue", 10, glow: true);
            vb.Box(head - dir * 0.3f + Vector3.up * 0.06f, new Vector3(0.22f, 0.15f, 0.03f), "screenbg", yaw);
            vb.Sphere(head + Vector3.up * 0.15f + dir * 0.1f, 0.02f, "glowred", glow: true, rings: 3, segments: 6);
            vb.Pipe(head - dir * 0.2f, head - dir * 0.6f - Vector3.up * 0.15f, 0.012f, "black", 4);
            vb.RegisterProp("camera_tripod", floor + Vector3.up * 0.7f, new Vector3(0.9f, 1.4f, 0.9f));
        }

        // ------------------------------------------------------------------ front planters (camera foreground)

        private static void FrontPlanters(VB vb, Rect inn)
        {
            // Inside along the front wall, and a lusher row outside it (the camera foreground, as in the concept).
            foreach (var (z, h, outside) in new[] { (inn.yMin + 0.4f, 0.6f, false), (inn.yMin - 0.75f, 0.75f, true) })
                foreach (float sx in new[] { -1f, 1f })
                    foreach (var (x0, x1) in outside ? new[] { (2.6f, 7.2f), (7.6f, 12.6f) } : new[] { (2.7f, 6.5f), (7.4f, 11.6f) })
                    {
                        float cx = sx * (x0 + x1) * .5f, len = x1 - x0;
                        vb.Box(new Vector3(cx, h * .5f, z), new Vector3(len, h, 0.6f), outside ? "darkconcrete" : "walnut");
                        vb.Box(new Vector3(cx, h, z), new Vector3(len + 0.06f, 0.04f, 0.66f), outside ? "concrete" : "honey");
                        if (!outside) vb.RegisterProp("planter_box", new Vector3(cx, h * .5f, z), new Vector3(len, h, 0.6f));
                        int n = Mathf.RoundToInt(len / 0.75f);
                        for (int i = 0; i < n; i++)
                        {
                            float x = cx - len * .5f + (i + 0.5f) * len / n;
                            int pick = (i + (outside ? 2 : 0)) % 4;
                            string id = pick == 1 ? "home_houseplant_1" : pick == 3 ? "home_houseplant_5" : "houseplant_bush";
                            vb.Model(id, x, z + (i % 2 == 0 ? 0.05f : -0.05f), 37f * i, y: h, scale: outside ? 0.72f : 0.62f);
                        }
                    }
        }

        // ------------------------------------------------------------------ truss

        private static void Truss(VB vb)
        {
            var c = C + Vector3.up * 5.0f;
            const float r = 4.9f;
            Vector3 prev = c + new Vector3(r, 0, 0);
            for (int i = 1; i <= 28; i++)
            {
                float a = i * Mathf.PI * 2f / 28f;
                var p = c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                vb.Pipe(prev, p, 0.05f, "black", 6);
                vb.Pipe(prev + Vector3.up * 0.28f, p + Vector3.up * 0.28f, 0.035f, "black", 6);
                prev = p;
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * .5f + Mathf.PI * .25f;
                var p = c + new Vector3(Mathf.Cos(a) * r, 0.28f, Mathf.Sin(a) * r);
                vb.Pipe(p, p + Vector3.up * 2.4f, 0.02f, "black", 4);
            }
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.PI * (1.1f + 0.16f * i);
                var p = c + new Vector3(Mathf.Cos(a) * r, -0.2f, Mathf.Sin(a) * r);
                var target = C + new Vector3((i - 2.5f) * 0.7f, StageH, i % 2 == 0 ? -0.9f : -1.6f);
                var dir = (target - p).normalized;
                vb.Pipe(p + Vector3.up * 0.2f, p, 0.015f, "black", 4);
                vb.Pipe(p - dir * 0.15f, p + dir * 0.2f, 0.12f, "charcoal", 10);
                vb.Sphere(p + dir * 0.21f, 0.09f, "glowwhite", glow: true, rings: 3, segments: 8);
                vb.SpotLight(p + dir * 0.25f, target, i % 2 == 0 ? "glowamber" : "glowwhite", 3.0f, 10f, 46f);
            }
        }
    }
}
