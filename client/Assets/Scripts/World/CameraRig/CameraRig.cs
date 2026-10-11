using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// The high 3/4 "tilt" camera over the diorama (AGENTS-BRIEF art direction): follows the local avatar, pinch to
    /// zoom, one-finger drag pans a little and eases back, focus clamped to the venue's camera bounds.
    /// Input arrives from the UI layer (UI Toolkit owns touch), so this class has no input-system dependency.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public static CameraRig Instance { get; private set; }

        public float pitch = 50f;
        public float yaw = 45f;
        public float distance = 15f;
        public float minDistance = 7f;
        public float maxDistance = 26f;
        public float followSharpness = 6f;
        public float maxPan = 8f;

        public Transform Target { get; set; }
        public Rect Bounds { get; set; } = new Rect(-30, -30, 60, 60);
        public Camera Camera { get; private set; }

        private Vector3 _focus;
        private Vector3 _pan;
        private float _lastInputAt;

        public static CameraRig Ensure()
        {
            if (Instance != null) return Instance;
            var cam = Camera.main;
            GameObject go;
            if (cam == null)
            {
                go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            else go = cam.gameObject;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 120f;
            cam.fieldOfView = 35f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.78f, 0.92f);
            var rig = go.GetComponent<CameraRig>() ?? go.AddComponent<CameraRig>();
            rig.Camera = cam;
            return rig;
        }

        private void Awake()
        {
            Instance = this;
            Camera = GetComponent<Camera>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SnapToTarget()
        {
            if (Target != null) _focus = Target.position;
            _pan = Vector3.zero;
            LateUpdate();
        }

        /// <summary>Pinch: positive delta zooms in. Units are screen pixels of finger-distance change.</summary>
        public void Zoom(float pixelDelta)
        {
            distance = Mathf.Clamp(distance - pixelDelta * 0.03f, minDistance, maxDistance);
            _lastInputAt = Time.unscaledTime;
        }

        /// <summary>One-finger drag in screen pixels.</summary>
        public void Pan(Vector2 pixelDelta)
        {
            var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            var fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            float k = distance * 0.0016f;
            _pan -= (right * pixelDelta.x + fwd * pixelDelta.y) * k;
            _pan = Vector3.ClampMagnitude(_pan, maxPan);
            _lastInputAt = Time.unscaledTime;
        }

        private void LateUpdate()
        {
            if (Target != null)
                _focus = Vector3.Lerp(_focus, Target.position, 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime));
            if (Time.unscaledTime - _lastInputAt > 2.5f)
                _pan = Vector3.Lerp(_pan, Vector3.zero, 1f - Mathf.Exp(-1.5f * Time.unscaledDeltaTime));

            var f = _focus + _pan;
            f.x = Mathf.Clamp(f.x, Bounds.xMin, Bounds.xMax);
            f.z = Mathf.Clamp(f.z, Bounds.yMin, Bounds.yMax);
            var rot = Quaternion.Euler(pitch, yaw, 0);
            transform.SetPositionAndRotation(f + Vector3.up * 1.0f - rot * Vector3.forward * distance, rot);
        }
    }
}
