using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Community easter eggs shared by several venues (docs/research/community-eggs-2026-10.md, Tier A): our own drawings,
    /// never logos or letters. Wrap a call in <c>vb.Scaled(pivot, s)</c> to resize one.
    /// </summary>
    public static class VenueEggs
    {
        /// <summary>Seeker-style phone lying back up: three lens dots and a small engraved shield (a generic phone).</summary>
        public static void SeekerPhone(VB vb, Vector3 p, float yaw, float scale = 1f)
        {
            using var big = vb.Scaled(p, scale);
            vb.Box(p, new Vector3(0.09f, 0.012f, 0.18f), "black", yaw);
            var q = Quaternion.Euler(0, yaw, 0);
            for (int i = 0; i < 3; i++)
                vb.Cylinder(p + q * new Vector3(-0.022f + (i % 2) * 0.024f, 0.006f, 0.05f + (i / 2) * 0.024f), 0.009f, 0.004f, "darkgrey", 8);
            vb.Box(p + q * new Vector3(0, 0.007f, -0.03f), new Vector3(0.02f, 0.002f, 0.024f), "steel", yaw);
        }

        public static void DogWithBeanie(VB vb, Vector3 p, float facing)
        {
            var q = Quaternion.Euler(0, facing, 0);
            vb.Blob(p + Vector3.up * 0.2f, new Vector3(0.3f, 0.36f, 0.38f), "tan", yaw: facing, rings: 6, segments: 10);
            vb.Blob(p + q * new Vector3(0, 0.08f, 0.14f) , new Vector3(0.26f, 0.14f, 0.2f), "tan", yaw: facing);
            var head = p + q * new Vector3(0, 0.46f, 0.1f);
            vb.Sphere(head, 0.14f, "tan", rings: 6, segments: 10);
            vb.Blob(head + q * new Vector3(0, -0.04f, 0.12f), new Vector3(0.12f, 0.09f, 0.12f), "cream", yaw: facing);
            vb.Sphere(head + q * new Vector3(0, -0.02f, 0.19f), 0.025f, "black", rings: 3, segments: 6);
            foreach (float s in new[] { -1f, 1f })
            {
                vb.Sphere(head + q * new Vector3(s * 0.055f, 0.03f, 0.125f), 0.018f, "black", rings: 3, segments: 6);
                vb.Blob(head + q * new Vector3(s * 0.13f, -0.02f, 0f), new Vector3(0.05f, 0.14f, 0.08f), "leather", yaw: facing);
            }
            vb.Cylinder(head + Vector3.up * 0.06f, 0.145f, 0.12f, "dustpink", 12, topRadius: 0.11f);
            vb.Cylinder(head + Vector3.up * 0.05f, 0.15f, 0.03f, "petalpink", 12);
            vb.Sphere(head + Vector3.up * 0.21f, 0.045f, "petalpink", rings: 4, segments: 6);
        }
        public static void JupiterCat(VB vb, Vector3 sill, float facing)
        {
            // Small white cat in a white-and-blue astronaut suit, bubble helmet ring, banded planet patch; napping.
            var q = Quaternion.Euler(0, facing, 0);
            vb.Blob(sill + Vector3.up * 0.1f, new Vector3(0.36f, 0.2f, 0.22f), "petalwhite", yaw: facing + 90f, rings: 6, segments: 10);
            vb.Blob(sill + q * new Vector3(0.0f, 0.12f, 0.0f), new Vector3(0.12f, 0.06f, 0.24f), "awningblue", yaw: facing);
            var head = sill + q * new Vector3(0.2f, 0.16f, 0.03f);
            vb.Sphere(head, 0.1f, "petalwhite", rings: 6, segments: 10);
            foreach (float s in new[] { -1f, 1f })
                vb.CylinderRot(head + q * new Vector3(s * 0.05f, 0.07f, 0f), q, 0.03f, 0.06f, "petalwhite", 5, topRadius: 0f);
            for (int i = 0; i < 2; i++) vb.Box(head + q * new Vector3((i - 0.5f) * 0.06f, 0.0f, 0.095f), new Vector3(0.03f, 0.005f, 0.004f), "black", facing);
            for (int i = 0; i < 16; i++)
            {
                float a0 = i * Mathf.PI * 2f / 16f, a1 = (i + 1) * Mathf.PI * 2f / 16f;
                vb.Pipe(head + q * new Vector3(0, Mathf.Sin(a0) * 0.16f, Mathf.Cos(a0) * 0.16f), head + q * new Vector3(0, Mathf.Sin(a1) * 0.16f, Mathf.Cos(a1) * 0.16f), 0.008f, "waterlight", 4);
            }
            var patch = sill + q * new Vector3(-0.05f, 0.16f, 0.1f);
            vb.Cylinder(patch, 0.035f, 0.01f, "ctgreen", 10);
            vb.Box(patch + Vector3.up * 0.011f, new Vector3(0.06f, 0.002f, 0.012f), "cyan", facing + 30f);
            vb.Blob(sill + q * new Vector3(-0.22f, 0.06f, -0.05f), new Vector3(0.22f, 0.05f, 0.05f), "petalwhite", yaw: facing + 20f);
        }
        public static void PhantomGhost(VB vb, Vector3 lamp)
        {
            // Friendly purple ghost of our own design (round cheeks, small scarf) peeking from behind the lamp post.
            var p = lamp + new Vector3(0.32f, 0f, 0.3f);
            vb.Blob(p + Vector3.up * 1.15f, new Vector3(0.56f, 0.7f, 0.5f), "ghostpurple", rings: 8, segments: 12);
            for (int i = 0; i < 4; i++)
                vb.Sphere(p + new Vector3(-0.18f + i * 0.12f, 0.8f, 0f), 0.09f, "ghostpurple", rings: 4, segments: 8);
            foreach (float s in new[] { -1f, 1f })
            {
                vb.Blob(p + new Vector3(s * 0.1f - 0.1f, 1.28f, -0.22f), new Vector3(0.06f, 0.1f, 0.03f), "black");
                vb.Sphere(p + new Vector3(s * 0.17f - 0.1f, 1.16f, -0.2f), 0.045f, "dustpink", rings: 3, segments: 6);
            }
            vb.Blob(p + new Vector3(-0.05f, 0.98f, -0.08f), new Vector3(0.5f, 0.1f, 0.4f), "ctorange", rings: 4, segments: 8);
            vb.Blob(p + new Vector3(0.18f, 0.9f, -0.2f), new Vector3(0.08f, 0.18f, 0.05f), "ctorange");
            vb.Blob(p + new Vector3(-0.3f, 1.1f, -0.05f), new Vector3(0.18f, 0.1f, 0.1f), "ghostpurple", yaw: 30f);
        }
        /// <summary>
        /// Popcat-style brown-and-white cat with its mouth a perfect round O, facing <paramref name="facing"/>; a ring doughnut
        /// hangs on a string in front of it (or sits on a little plate).
        /// </summary>
        public static void Popcat(VB vb, Vector3 c, float facing, float scale, bool doughnutOnString)
        {
            float a = facing * Mathf.Deg2Rad;
            var f = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            var r = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
            var yawQ = Quaternion.Euler(0, facing, 0);
            using var big = vb.Scaled(c - Vector3.up * 0.15f, scale);
            vb.Blob(c, new Vector3(0.38f, 0.32f, 0.28f), "petalwhite", yaw: facing, rings: 6, segments: 10);
            var head = c + Vector3.up * 0.32f;
            vb.Sphere(head, 0.19f, "petalwhite", rings: 7, segments: 12);
            vb.Blob(head + r * 0.08f + Vector3.up * 0.07f, new Vector3(0.18f, 0.14f, 0.2f), "leather", yaw: facing);
            foreach (float s in new[] { -1f, 1f })
            {
                vb.CylinderRot(head + r * (s * 0.11f) + Vector3.up * 0.13f, yawQ * Quaternion.Euler(0, 0, s * -12f), 0.06f, 0.11f, s > 0 ? "leather" : "petalwhite", 5, topRadius: 0f);
                vb.Sphere(head + r * (s * 0.07f) + Vector3.up * 0.04f + f * 0.165f, 0.025f, "black", rings: 3, segments: 6);
            }
            vb.CylinderRot(head + f * 0.17f - Vector3.up * 0.06f, yawQ * Quaternion.Euler(90f, 0, 0), 0.06f, 0.03f, "black", 14);
            if (doughnutOnString)
            {
                var dn = c + f * 0.3f + Vector3.up * 0.34f;
                vb.Pipe(dn + Vector3.up * 0.95f, dn + Vector3.up * 0.09f, 0.004f, "offwhite", 3);
                for (int i = 0; i < 12; i++)
                {
                    float t = i * Mathf.PI * 2f / 12f;
                    vb.Sphere(dn + r * (Mathf.Cos(t) * 0.08f) + Vector3.up * (Mathf.Sin(t) * 0.08f), 0.035f, i % 3 == 0 ? "petalpink" : "pizza", rings: 3, segments: 6);
                }
            }
            else
            {
                var plate = c + f * 0.3f - Vector3.up * 0.15f;
                vb.Cylinder(plate, 0.11f, 0.012f, "petalwhite", 12);
                for (int i = 0; i < 12; i++)
                {
                    float t = i * Mathf.PI * 2f / 12f;
                    vb.Sphere(plate + new Vector3(Mathf.Cos(t) * 0.065f, 0.035f, Mathf.Sin(t) * 0.065f), 0.03f, i % 3 == 0 ? "petalpink" : "pizza", rings: 3, segments: 6);
                }
            }
        }
    }
}
