using CTLife.Core;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>Reusable room pieces shared by the venue scripts (shell, stations, seating, lounges).</summary>
    public static class VenueParts
    {
        /// <summary>
        /// Diorama room: floor over the walkable area plus a margin, full-height back and side walls, and a low
        /// cut-away front wall (camera side) with a doorway centred on the exit spot. Returns the wall top height.
        /// </summary>
        public static void InteriorShell(this VB vb, string floorTile, string wallColor, float wallHeight = 4.5f,
            float frontHeight = 0.9f, float doorWidth = 4f, string trim = "charcoal", float sideHeight = -1f, float margin = 1f,
            float westHeight = -1f, float eastHeight = -1f, bool back = true)
        {
            var w = vb.Walk;
            if (sideHeight < 0) sideHeight = wallHeight;
            if (westHeight < 0) westHeight = sideHeight;
            if (eastHeight < 0) eastHeight = sideHeight;
            // Extra apron on the camera (south) side so the entry view never shows the clear colour.
            vb.Floor(floorTile, Rect.MinMaxRect(w.xMin - margin, w.yMin - margin - 4f, w.xMax + margin, w.yMax + margin));
            float t = 0.3f;
            float bx0 = w.xMin - t * .5f - 0.05f, bx1 = w.xMax + t * .5f + 0.05f, bz0 = w.yMin - t * .5f - 0.05f, bz1 = w.yMax + t * .5f + 0.05f;
            vb.Inner = Rect.MinMaxRect(bx0 + t * .5f, bz0 + t * .5f, bx1 - t * .5f, bz1 - t * .5f);
            if (back) vb.Wall(new Vector2(bx0 - t * .5f, bz1), new Vector2(bx1 + t * .5f, bz1), wallHeight, wallColor, t);
            if (westHeight > 0) vb.Wall(new Vector2(bx0, bz0), new Vector2(bx0, bz1), westHeight, wallColor, t);
            if (eastHeight > 0) vb.Wall(new Vector2(bx1, bz0), new Vector2(bx1, bz1), eastHeight, wallColor, t);
            float doorX = vb.HasSpot("exit") ? vb.Spot("exit").x : 0f;
            vb.Wall(new Vector2(bx0 - t * .5f, bz0), new Vector2(doorX - doorWidth * .5f, bz0), frontHeight, wallColor, t);
            vb.Wall(new Vector2(doorX + doorWidth * .5f, bz0), new Vector2(bx1 + t * .5f, bz0), frontHeight, wallColor, t);
            // Skirting and cap trims read the room edge from the high camera.
            if (back) vb.Box(new Vector3((bx0 + bx1) * .5f, wallHeight + 0.06f, bz1), new Vector3(bx1 - bx0 + t * 2, 0.12f, t + 0.06f), trim);
            if (westHeight > 0) vb.Box(new Vector3(bx0, westHeight + 0.06f, (bz0 + bz1) * .5f), new Vector3(t + 0.06f, 0.12f, bz1 - bz0), trim);
            if (eastHeight > 0) vb.Box(new Vector3(bx1, eastHeight + 0.06f, (bz0 + bz1) * .5f), new Vector3(t + 0.06f, 0.12f, bz1 - bz0), trim);
            // Door posts and a header bar (open above: camera side).
            foreach (float s in new[] { -1f, 1f })
                vb.Box(new Vector3(doorX + s * (doorWidth * .5f + 0.12f), 1.2f, bz0), new Vector3(0.24f, 2.4f, t + 0.1f), trim, collider: true);
            vb.Decal(doorX, bz0 + 0.9f, doorWidth - 0.4f, 1.4f, "charcoal");
        }

        /// <summary>Outdoor deck: floor tiles over walkable + margin and a waist-high perimeter rail with an entry gap.</summary>
        public static void OutdoorDeck(this VB vb, string floorTile, string railColor, float margin = 1f, float railHeight = 1.0f,
            bool back = true, bool sides = true, float doorWidth = 4f)
        {
            var w = vb.Walk;
            vb.Floor(floorTile, Rect.MinMaxRect(w.xMin - margin, w.yMin - margin, w.xMax + margin, w.yMax + margin));
            float x0 = w.xMin - 0.25f, x1 = w.xMax + 0.25f, z0 = w.yMin - 0.25f, z1 = w.yMax + 0.25f;
            float doorX = vb.HasSpot("exit") ? vb.Spot("exit").x : 0f;
            void Rail(Vector2 a, Vector2 b)
            {
                var d = b - a;
                int posts = Mathf.Max(2, Mathf.CeilToInt(d.magnitude / 2.5f) + 1);
                for (int i = 0; i < posts; i++)
                {
                    var p = Vector2.Lerp(a, b, i / (float)(posts - 1));
                    vb.Box(new Vector3(p.x, railHeight * .5f, p.y), new Vector3(0.09f, railHeight, 0.09f), railColor);
                }
                vb.Pipe(new Vector3(a.x, railHeight, a.y), new Vector3(b.x, railHeight, b.y), 0.045f, railColor);
                vb.Pipe(new Vector3(a.x, railHeight * .5f, a.y), new Vector3(b.x, railHeight * .5f, b.y), 0.03f, railColor);
                var mid = (a + b) * .5f;
                vb.Collider(new Vector3(mid.x, railHeight * .5f, mid.y), new Vector3(Mathf.Abs(d.x) + 0.1f, railHeight, Mathf.Abs(d.y) + 0.1f));
            }
            if (back) Rail(new Vector2(x0, z1), new Vector2(x1, z1));
            if (sides) { Rail(new Vector2(x0, z0), new Vector2(x0, z1)); Rail(new Vector2(x1, z0), new Vector2(x1, z1)); }
            Rail(new Vector2(x0, z0), new Vector2(doorX - doorWidth * .5f, z0));
            Rail(new Vector2(doorX + doorWidth * .5f, z0), new Vector2(x1, z0));
        }

        /// <summary>Exit marker over the doorway (sign art "exit" from the venue atlas).</summary>
        public static void ExitSign(this VB vb, float height = 2.6f)
        {
            if (!vb.HasSpot("exit")) return;
            var e = vb.Spot("exit");
            vb.Sign("exit", new Vector3(e.x, height, vb.Walk.yMin - 0.25f), 1.4f, 180f, frame: "charcoal");
        }

        /// <summary>
        /// Seated workstation at a terminal spot (avatar faces the spot's rotY): desk ahead, chair on the anchor,
        /// monitors on the desk. The desk is a nav obstacle; the chair is a tap target, never an obstacle.
        /// </summary>
        public static void Terminal(this VB vb, SpotDef s, string screen, string desk = "desk_medium_scifi", string chair = "office_chair_scifi",
            string monitor = "monitor_triple", string chairColor = null, float deskForward = 0.82f, float approachBack = 0.8f)
        {
            float f = VB.Deg(s);
            var deskPos = VB.Rel(s, 0, deskForward);
            vb.ModelAt(desk, deskPos, f + 180f);
            var size = vb.SizeOf(desk);
            vb.Solid(deskPos.x, deskPos.z, size.x * 0.95f, size.z * 0.8f, 0.8f, f);
            if (monitor != null) vb.ModelAt(monitor, VB.Rel(s, 0, deskForward + 0.16f, 0.76f), f + 180f, screen: screen);
            if (chair != null) vb.ModelAt(chair, VB.Rel(s, 0, -0.05f), f, recolor: chairColor);
            vb.SeatCollider(s, new Vector3(0, 0.75f, -0.05f), new Vector3(0.6f, 0.5f, 0.6f));
            vb.Approach(s, VB.Rel(s, 0.0f, -approachBack));
        }

        /// <summary>Chair/seat model on a seat spot, facing the spot direction.</summary>
        public static void Seat(this VB vb, SpotDef s, string model, string recolor = null, float back = 0.0f, float approachBack = 0.75f, float scale = 1f)
        {
            vb.ModelAt(model, VB.Rel(s, 0, -back), VB.Deg(s), scale, recolor: recolor);
            vb.SeatCollider(s, new Vector3(0, 0.7f, -back), new Vector3(0.65f, 0.5f, 0.65f));
            vb.Approach(s, VB.Rel(s, 0, -approachBack));
        }

        /// <summary>Side lounge: sofa facing <paramref name="facing"/>, coffee table in front. Obstacle only (not a spot).</summary>
        public static void Lounge(this VB vb, float x, float z, float facing, string sofaColor = null, string sofa = "lounge_sofa")
        {
            vb.Model(sofa, x, z, facing, recolor: sofaColor);
            var size = vb.SizeOf(sofa);
            vb.Solid(x, z, size.x, size.z, 0.9f, facing);
            var table = new Vector3(x, 0, z) + Quaternion.Euler(0, facing, 0) * new Vector3(0, 0, 1.25f);
            vb.ModelAt("home_table_roundsmall", table, facing, 0.7f);
            vb.Solid(table.x, table.z, 0.9f, 0.9f, 0.6f);
            vb.ModelAt("cup_coffee", table + new Vector3(0.1f, 0.53f, 0), facing);
        }

        /// <summary>Potted plant with a small obstacle.</summary>
        public static void Plant(this VB vb, float x, float z, string id = "houseplant_tall", float scale = 1f)
        {
            vb.Model(id, x, z, 180f, scale: scale);
            var s = vb.SizeOf(id) * scale;
            vb.Solid(x, z, Mathf.Max(0.4f, s.x * 0.7f), Mathf.Max(0.4f, s.z * 0.7f), s.y);
        }
    }
}
