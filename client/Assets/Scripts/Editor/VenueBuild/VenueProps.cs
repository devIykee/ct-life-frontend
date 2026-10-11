using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>Procedural CT props (palette blocks) and wall-placement helpers shared by venue scripts.</summary>
    public static class VenueProps
    {
        /// <summary>
        /// Place sign art on an inner wall face: 'N' back wall (faces the room/camera), 'W' west wall (faces +x),
        /// 'E' east wall (faces -x). <paramref name="along"/> is x for N, z for W/E.
        /// </summary>
        public static void OnWall(this VB vb, char wall, float along, float y, float width, string sign, string frame = null, string hook = null, float height = -1f)
        {
            Vector3 p; float facing;
            switch (wall)
            {
                case 'W': p = new Vector3(vb.Inner.xMin + 0.03f, y, along); facing = 90f; break;
                case 'E': p = new Vector3(vb.Inner.xMax - 0.03f, y, along); facing = -90f; break;
                default: p = new Vector3(along, y, vb.Inner.yMax - 0.03f); facing = 180f; break;
            }
            if (hook != null)
            {
                var off = Quaternion.Euler(0, facing, 0) * Vector3.forward * 0.04f;
                vb.HookPanel(hook, sign, p + off, width, facing, height, frame: frame ?? "charcoal");
            }
            else vb.Sign(sign, p, width, facing, height, frame);
        }

        /// <summary>Post-and-rail run (fence, balustrade, pen). Optional collider; never a nav obstacle unless solid.</summary>
        public static void Rail(this VB vb, Vector2 a, Vector2 b, float height, string color, float spacing = 2f, bool solid = false, int rails = 2,
            string railColor = null, bool glowRail = false)
        {
            var d = b - a;
            int posts = Mathf.Max(2, Mathf.CeilToInt(d.magnitude / spacing) + 1);
            for (int i = 0; i < posts; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)(posts - 1));
                vb.Box(new Vector3(p.x, height * .5f, p.y), new Vector3(0.1f, height, 0.1f), color);
            }
            for (int r = 1; r <= rails; r++)
            {
                float y = height * r / rails - 0.04f;
                vb.Pipe(new Vector3(a.x, y, a.y), new Vector3(b.x, y, b.y), 0.04f, railColor ?? color, 6, glowRail);
            }
            if (solid)
            {
                var mid = (a + b) * .5f;
                float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg - 90f;
                vb.Obstacle(new Vector3(mid.x, height * .5f, mid.y), new Vector3(d.magnitude, height, 0.2f), yaw);
                vb.Collider(new Vector3(mid.x, height * .5f, mid.y), new Vector3(d.magnitude, height, 0.2f), yaw);
            }
        }

        /// <summary>Vertical bars between a and b (jail fronts), leaving door gaps (centre ± half width along the run).</summary>
        public static void Bars(this VB vb, Vector2 a, Vector2 b, float height, float spacing, (float center, float width)[] doors, string color = "barsteel")
        {
            var d = b - a;
            float len = d.magnitude;
            var dir = d / len;
            for (float t = 0; t <= len + 0.001f; t += spacing)
            {
                var p = a + dir * t;
                bool inDoor = false;
                foreach (var door in doors)
                    if (Mathf.Abs(Vector2.Dot(p - a, dir) - door.center) < door.width * .5f) inDoor = true;
                if (inDoor) continue;
                vb.Cylinder(new Vector3(p.x, 0, p.y), 0.035f, height, color, 6);
            }
            vb.Pipe(new Vector3(a.x, height, a.y), new Vector3(b.x, height, b.y), 0.06f, color, 6);
            vb.Pipe(new Vector3(a.x, 0.06f, a.y), new Vector3(b.x, 0.06f, b.y), 0.05f, color, 6);
            // Solid segments between doors.
            float start = 0;
            foreach (var door in doors)
            {
                float end = door.center - door.width * .5f;
                if (end - start > 0.05f) Seg(start, end);
                start = door.center + door.width * .5f;
            }
            if (len - start > 0.05f) Seg(start, len);

            void Seg(float s0, float s1)
            {
                var p0 = a + dir * s0; var p1 = a + dir * s1; var mid = (p0 + p1) * .5f;
                float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg - 90f;
                vb.Obstacle(new Vector3(mid.x, height * .5f, mid.y), new Vector3(s1 - s0, height, 0.15f), yaw);
                vb.Collider(new Vector3(mid.x, height * .5f, mid.y), new Vector3(s1 - s0, height, 0.15f), yaw);
            }
        }

        /// <summary>Stylised standing figure (clone, mannequin, crowd prop). Static decor, not an NPC.</summary>
        public static void Figure(this VB vb, float x, float z, string body, string head = "blush", float scale = 1f, string accent = null)
        {
            vb.Cylinder(new Vector3(x, 0, z), 0.2f * scale, 1.15f * scale, body, 8, topRadius: 0.16f * scale);
            vb.Sphere(new Vector3(x, 1.38f * scale, z), 0.17f * scale, head, rings: 5, segments: 8);
            if (accent != null) vb.Box(new Vector3(x, 1.0f * scale, z - 0.17f * scale), new Vector3(0.22f, 0.15f, 0.02f) * scale, accent);
        }

        public static void Tree(this VB vb, string id, float x, float z, float scale = 1f, float yaw = 0f, bool obstacle = true)
        {
            vb.Model(id, x, z, yaw, scale: scale);
            if (obstacle) vb.Solid(x, z, 1.0f * scale, 1.0f * scale, 3f);
        }

        /// <summary>Stylised motor yacht (bow towards +x before yaw). Decor beyond the walkable deck.</summary>
        public static void Yacht(this VB vb, Vector3 p, float yaw, float length, string hullSign = null)
        {
            var q = Quaternion.Euler(0, yaw, 0);
            Vector3 W(float x, float y, float z) => p + q * new Vector3(x, y, z);
            float w = length * 0.24f;
            vb.Box(W(0, 0.6f, 0), new Vector3(length * .78f, 1.4f, w), "white", yaw);
            vb.Box(W(length * .43f, 0.75f, 0), new Vector3(length * .18f, 1.1f, w * .62f), "white", yaw + 0);
            foreach (float s in new[] { -1f, 1f })
                vb.Box(W(length * .41f, 0.75f, s * w * .3f), new Vector3(length * .22f, 1.1f, w * .3f), "white", yaw + s * 18f);
            vb.Box(W(0, -0.05f, 0), new Vector3(length * .8f, 0.12f, w * 1.02f), "navy", yaw);
            vb.Box(W(-length * .05f, 1.75f, 0), new Vector3(length * .5f, 0.9f, w * .8f), "offwhite", yaw);
            vb.Box(W(-length * .05f, 1.8f, 0), new Vector3(length * .5f + 0.02f, 0.35f, w * .8f + 0.02f), "ink", yaw);
            vb.Box(W(-length * .1f, 2.6f, 0), new Vector3(length * .28f, 0.7f, w * .6f), "offwhite", yaw);
            vb.Box(W(-length * .1f, 2.62f, 0), new Vector3(length * .28f + 0.02f, 0.25f, w * .6f + 0.02f), "ink", yaw);
            vb.Cylinder(W(-length * .12f, 2.95f, 0), 0.05f, 1.4f, "chrome", 6);
            if (hullSign != null)
                foreach (float s in new[] { -1f, 1f })
                    vb.Sign(hullSign, W(-length * .1f, 0.55f, s * (w * .5f + 0.02f)), length * .35f, yaw + (s < 0 ? 180f : 0f));
        }

        /// <summary>Stylised rocket on a pad (Launchpad Tower identity).</summary>
        public static void Rocket(this VB vb, float x, float z, float height, string body = "white", string accent = "solpurple")
        {
            float r = height * 0.09f;
            vb.Cylinder(new Vector3(x, 0, z), r * 2.2f, 0.3f, "darksteel", 12, solid: true);
            vb.Cylinder(new Vector3(x, 0.3f, z), r, height * .62f, body, 12);
            vb.Cylinder(new Vector3(x, 0.3f + height * .2f, z), r * 1.02f, height * .06f, accent, 12);
            vb.Cylinder(new Vector3(x, 0.3f + height * .62f, z), r, height * .3f, accent, 12, topRadius: 0f);
            for (int i = 0; i < 4; i++)
                vb.Box(new Vector3(x, 0.3f + height * .08f, z) + Quaternion.Euler(0, i * 90f, 0) * new Vector3(r * 1.3f, 0, 0),
                    new Vector3(r * 1.1f, height * .16f, 0.05f), accent, i * 90f);
            vb.Cylinder(new Vector3(x, 0.08f, z), r * .8f, 0.25f, "glowamber", 10, glow: true, topRadius: r * .3f);
        }

        /// <summary>Airdrop crate under a parachute canopy (optionally hanging at a height).</summary>
        public static void AirdropCrate(this VB vb, float x, float z, float y = 0f, string canopy = "ctpink", bool solid = true)
        {
            vb.Box(new Vector3(x, y + 0.4f, z), new Vector3(0.8f, 0.8f, 0.8f), "cardboard", 12f);
            vb.Box(new Vector3(x, y + 0.4f, z), new Vector3(0.82f, 0.12f, 0.82f), "gold", 12f);
            vb.Blob(new Vector3(x, y + 2.6f, z), new Vector3(2.0f, 1.0f, 2.0f), canopy);
            foreach (var c in new[] { new Vector2(-.35f, -.35f), new Vector2(.35f, -.35f), new Vector2(-.35f, .35f), new Vector2(.35f, .35f) })
                vb.Pipe(new Vector3(x + c.x, y + 0.8f, z + c.y), new Vector3(x + c.x * 2.4f, y + 2.45f, z + c.y * 2.4f), 0.012f, "offwhite", 4);
            if (solid && y < 0.1f) vb.Solid(x, z, 0.9f, 0.9f, 0.8f);
        }

        public static void HayBale(this VB vb, float x, float z, float yaw = 0f, float y = 0f) =>
            vb.Box(new Vector3(x, y + 0.3f, z), new Vector3(1.1f, 0.6f, 0.55f), "hay", yaw);

        /// <summary>Queue stanchion pair with a glowing rope (non-blocking decor).</summary>
        public static void Stanchions(this VB vb, Vector2 a, Vector2 b, string rope = "glowgold")
        {
            foreach (var p in new[] { a, b })
            {
                vb.Cylinder(new Vector3(p.x, 0, p.y), 0.16f, 0.04f, "chrome", 10);
                vb.Cylinder(new Vector3(p.x, 0, p.y), 0.035f, 0.95f, "chrome", 8);
                vb.Sphere(new Vector3(p.x, 0.97f, p.y), 0.05f, "chrome", rings: 4, segments: 6);
            }
            vb.Pipe(new Vector3(a.x, 0.88f, a.y), new Vector3(b.x, 0.88f, b.y), 0.025f, rope, 6, glow: rope.StartsWith("glow"));
        }

        /// <summary>Counter/bar block with a contrasting top and a glow kick strip; solid.</summary>
        public static void Counter(this VB vb, float x, float z, float width, float depth, float yaw, string body, string top, string kick = null, float height = 1.05f)
        {
            vb.Box(new Vector3(x, height * .5f, z), new Vector3(width, height, depth), body, yaw, solid: true);
            vb.Box(new Vector3(x, height + 0.03f, z), new Vector3(width + 0.1f, 0.06f, depth + 0.1f), top, yaw);
            if (kick != null)
            {
                var front = Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0, -depth * .5f - 0.01f);
                vb.Box(new Vector3(x, 0.08f, z) + front, new Vector3(width, 0.04f, 0.02f), kick, yaw, glow: true);
            }
        }

        /// <summary>High-top cocktail table with two stools (decor obstacle).</summary>
        public static void HighTable(this VB vb, float x, float z, string top = "charcoal")
        {
            vb.Cylinder(new Vector3(x, 0, z), 0.25f, 0.03f, "darksteel", 10);
            vb.Cylinder(new Vector3(x, 0, z), 0.05f, 1.05f, "darksteel", 8);
            vb.Cylinder(new Vector3(x, 1.05f, z), 0.38f, 0.04f, top, 12);
            vb.Model("bar_stool", x - 0.65f, z, 90f);
            vb.Model("bar_stool", x + 0.65f, z, -90f);
            vb.Solid(x, z, 1.9f, 0.9f, 1.1f);
        }

        /// <summary>Rotating-beacon style alarm light: tintable hook dome on a base.</summary>
        public static void Beacon(this VB vb, string hookName, Vector3 basePoint, float size = 0.6f)
        {
            vb.Cylinder(basePoint, size * .55f, size * .25f, "darksteel", 12);
            vb.HookGlow(hookName, basePoint + Vector3.up * (size * .25f + size * .35f), new Vector3(size * .9f, size * .7f, size * .9f), "glowred", sphere: true);
        }
    }
}
