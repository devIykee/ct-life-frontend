using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// A venue's street-side building on CT Island (World lane): walls with a door, a roof and a name sign around the room
    /// diorama. While the local player is inside, the roof dissolves (dither) and so does every wall between the camera
    /// and the room, so the room plays as before; outside, the building stands whole.
    /// </summary>
    public sealed class VenueShell : MonoBehaviour
    {
        public string venueId;
        [Tooltip("Venue-local footprint (x, z) of the shell.")]
        public Rect footprint;
        public float height = 9f;
        public Vector3 origin;
        public float yaw;
        public Renderer roof;
        public Renderer[] walls = new Renderer[0];
        [Tooltip("World fix-ups 2: the venue architecture merged into one renderer (one draw per material), drawn while the " +
                 "player is outside; the per-side renderers (walls / roof) take over only while something is fading.")]
        public Renderer whole;
        [Tooltip("Outward normal of each wall (world), and its centre.")]
        public Vector3[] normals = new Vector3[0];
        public Vector3[] centres = new Vector3[0];
        public float fadeSeconds = 0.3f;

        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private MaterialPropertyBlock _mpb;
        private float _roof;
        private float[] _wall = new float[0];

        private void OnEnable()
        {
            _mpb = new MaterialPropertyBlock();
            _wall = new float[walls.Length];
            _roof = 0f;
            Apply(roof, 0f);
            for (int i = 0; i < walls.Length; i++) Apply(walls[i], 0f);
        }

        /// <summary>Venue-local (x, z) of a world point.</summary>
        public Vector2 ToLocal(Vector3 p)
        {
            float a = yaw * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            float dx = p.x - origin.x, dz = p.z - origin.z;
            return new Vector2(dx * c - dz * s, dx * s + dz * c);
        }

        public bool Contains(Vector3 p, float margin = 0.6f)
        {
            var l = ToLocal(p);
            return l.x > footprint.xMin - margin && l.x < footprint.xMax + margin && l.y > footprint.yMin - margin && l.y < footprint.yMax + margin
                   && p.y < origin.y + height + 1f && p.y > origin.y - 3f;
        }

        private void LateUpdate()
        {
            var player = VenueSession.Current != null && VenueSession.Current.LocalPlayer != null ? VenueSession.Current.LocalPlayer.transform : null;
            var cam = Camera.main;
            bool inside = player != null && Contains(player.position);
            float step = fadeSeconds > 0f ? Time.deltaTime / fadeSeconds : 1f;
            _roof = Mathf.MoveTowards(_roof, inside ? 1f : 0f, step);
            bool split = inside || _roof > 0f;
            for (int i = 0; i < walls.Length; i++)
            {
                bool hide = inside && cam != null && i < normals.Length && Vector3.Dot(normals[i], cam.transform.position - centres[i]) > 0f;
                _wall[i] = Mathf.MoveTowards(_wall[i], hide ? 0.92f : 0f, step);
                split |= _wall[i] > 0f;
            }
            if (whole != null)
            {
                // outside: the merged building only (the sign, not part of it, stays as it is)
                if (whole.enabled == split) whole.enabled = !split;
                if (!split)
                {
                    if (roof != null && roof.enabled) roof.enabled = false;
                    for (int i = 0; i < walls.Length; i++)
                        if (walls[i] != null && walls[i] != sign && walls[i].enabled) walls[i].enabled = false;
                    if (sign != null) Apply(sign, 0f);
                    return;
                }
            }
            Apply(roof, _roof);
            for (int i = 0; i < walls.Length; i++) Apply(walls[i], _wall[i]);
        }

        [Tooltip("The name sign (one of the walls): never part of the merged renderer.")]
        public Renderer sign;

        private void Apply(Renderer r, float fade)
        {
            if (r == null) return;
            bool show = fade < 0.999f;
            if (r.enabled != show) r.enabled = show;
            if (!show) return;
            // no property block at rest: a renderer with one drops out of the SRP batcher
            if (fade <= 0f)
            {
                if (r.HasPropertyBlock()) r.SetPropertyBlock(null);
                return;
            }
            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(FadeId, fade);
            r.SetPropertyBlock(_mpb);
        }
    }
}
