using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CTLife.World
{
    /// <summary>
    /// Seagulls over CT Island's water (World lane): a few flocks gliding in loose, wobbling circles, wings beating now and
    /// then. Two tiny meshes (wings up, wings level) built at load, all birds drawn with one instanced call per pose; no
    /// colliders, no GameObjects per bird, culled when the camera is far from a flock.
    /// </summary>
    public sealed class IslandBirds : MonoBehaviour
    {
        [Serializable]
        public struct Flock { public Vector3 center; public float radius; public int count; }

        public Flock[] flocks = Array.Empty<Flock>();
        public Material material;
        public float cullDistance = 650f;
        public float speed = 9f;

        private Mesh _up, _level;
        private Matrix4x4[] _a = new Matrix4x4[0], _b = new Matrix4x4[0];

        private void OnEnable()
        {
            _up = Bird(0.55f);
            _level = Bird(-0.08f);
            int n = 0;
            foreach (var f in flocks) n += f.count;
            _a = new Matrix4x4[n];
            _b = new Matrix4x4[n];
        }

        private void OnDisable()
        {
            if (_up != null) Destroy(_up);
            if (_level != null) Destroy(_level);
        }

        /// <summary>A gull: body and two swept wings (span 1.4 m), wing tips raised by lift (m).</summary>
        private static Mesh Bird(float lift)
        {
            var m = new Mesh { name = "gull" };
            var v = new[]
            {
                new Vector3(0f, 0f, 0.42f), new Vector3(0.09f, 0f, -0.3f), new Vector3(-0.09f, 0f, -0.3f),     // body
                new Vector3(0.06f, 0f, 0.12f), new Vector3(0.7f, lift, -0.05f), new Vector3(0.06f, 0f, -0.12f),  // right wing
                new Vector3(-0.06f, 0f, 0.12f), new Vector3(-0.06f, 0f, -0.12f), new Vector3(-0.7f, lift, -0.05f),
            };
            m.vertices = v;
            m.triangles = new[] { 0, 1, 2, 0, 2, 1, 3, 4, 5, 3, 5, 4, 6, 7, 8, 6, 8, 7 };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.uv = new Vector2[9];
            var white = new Color32[9];
            for (int i = 0; i < 9; i++) white[i] = new Color32(255, 255, 255, 255);    // the Toon shader reads COLOR (tint, AO)
            m.colors32 = white;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return m;
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || material == null || _up == null) return;
            float t = Time.time;
            int na = 0, nb = 0, id = 0;
            var eye = cam.transform.position;
            foreach (var f in flocks)
            {
                bool near = (f.center - eye).sqrMagnitude < cullDistance * cullDistance;
                for (int i = 0; i < f.count; i++, id++)
                {
                    if (!near) continue;
                    float ph = id * 2.399f;                                       // golden-angle spread
                    float r = f.radius * (0.55f + 0.45f * Mathf.Sin(ph * 1.7f));
                    float w = speed / Mathf.Max(r, 5f) * (i % 2 == 0 ? 1f : -1f);   // half the flock circles the other way
                    float a = ph + t * w;
                    float bob = Mathf.Sin(t * 0.6f + ph) * 3f;
                    var p = f.center + new Vector3(Mathf.Cos(a) * r, bob + (i % 3) * 2.5f, Mathf.Sin(a) * r);
                    var fwd = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * Mathf.Sign(w);
                    float bank = Mathf.Sign(w) * 18f;
                    var m = Matrix4x4.TRS(p, Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, bank), Vector3.one * 1.4f);
                    // beat for a moment every few seconds, otherwise glide
                    float beat = Mathf.Repeat(t * 0.35f + ph * 0.13f, 1f);
                    bool up = beat < 0.28f && Mathf.Repeat(t * 3.2f + ph, 1f) < 0.5f;
                    if (up) _a[na++] = m; else _b[nb++] = m;
                }
            }
            var rp = new RenderParams(material) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = new Bounds(eye, Vector3.one * 4000f) };
            if (na > 0) Graphics.RenderMeshInstanced(rp, _up, 0, _a, na);
            if (nb > 0) Graphics.RenderMeshInstanced(rp, _level, 0, _b, nb);
        }
    }
}
