using CTLife.Audio;
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

        private Transform _target;
        private AvatarView _targetAvatar;
        public Transform Target
        {
            get => _target;
            set { _target = value; _targetAvatar = value != null ? value.GetComponent<AvatarView>() : null; }
        }
        private Vector3 TargetPosition => _targetAvatar != null ? _targetAvatar.PresentationPosition : Target.position;
        public Rect Bounds { get; set; } = new Rect(-30, -30, 60, 60);
        public Camera Camera { get; private set; }

        private Vector3 _focus;
        private Vector3 _pan;
        private float _lastInputAt;
        private Vector3 _lookAhead;
        private float _targetViewportY;
        private float _focusHeight = 1f;

        public void ConfigureVenue(VenueSceneRoot venue)
        {
            Bounds = venue.cameraBounds;
            _lookAhead = venue.cameraLookAhead;
            _targetViewportY = venue.cameraTargetViewportY;
            _focusHeight = venue.cameraFocusHeight;
            distance = venue.cameraDistance > 0f ? venue.cameraDistance : 15f;
            pitch = venue.cameraDistance > 0f ? venue.cameraPitch : 50f;
            yaw = venue.cameraDistance > 0f ? venue.cameraYaw : 45f;
            maxDistance = Mathf.Max(26f, distance + 8f);
            minDistance = 9f;
            if (Camera != null)
            {
                Camera.fieldOfView = venue.cameraDistance > 0f ? venue.cameraFieldOfView : 35f;
                Camera.backgroundColor = new Color(0.81f, 0.83f, 0.80f);
            }
            _pan = Vector3.zero;
        }

        public static CameraRig Ensure()
        {
            if (Instance != null) { Instance.RegisterAudioListener(); return Instance; }
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
            var rig = go.GetComponent<CameraRig>();
            if (rig == null) rig = go.AddComponent<CameraRig>();
            rig.Camera = cam;
            rig.RegisterAudioListener();
            return rig;
        }

        private void RegisterAudioListener()
        {
            var listener = GetComponent<AudioListener>();
            if (listener == null) listener = gameObject.AddComponent<AudioListener>();
            AudioManager.Instance?.RegisterCameraListener(listener);
        }

        private void Awake()
        {
            Instance = this;
            Camera = GetComponent<Camera>();
            // The first camera can be created while an additive venue is active. Own it independently
            // so leaving that venue does not remove the game camera or its AudioListener.
            if (Application.isPlaying)
            {
                transform.SetParent(null, true);
                DontDestroyOnLoad(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SnapToTarget()
        {
            if (Target != null) _focus = TargetPosition;
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
                _focus = Vector3.Lerp(_focus, TargetPosition, 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime));
            if (Time.unscaledTime - _lastInputAt > 2.5f)
                _pan = Vector3.Lerp(_pan, Vector3.zero, 1f - Mathf.Exp(-1.5f * Time.unscaledDeltaTime));

            // Clamp the tracked position, not the camera's aim point. Clamping a world-Z look-ahead
            // moved the entrance avatar outside the narrow portrait frustum when the camera had yaw.
            var f = _focus + _pan;
            f.x = Mathf.Clamp(f.x, Bounds.xMin, Bounds.xMax);
            f.z = Mathf.Clamp(f.z, Bounds.yMin, Bounds.yMax);
            if (_targetViewportY > 0f)
            {
                float ahead = LookAheadForViewport(distance, pitch, Camera.fieldOfView, _targetViewportY, _focusHeight);
                f += Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * ahead;
            }
            else f += _lookAhead;
            var rot = Quaternion.Euler(pitch, yaw, 0);
            transform.SetPositionAndRotation(f + Vector3.up * _focusHeight - rot * Vector3.forward * distance, rot);
        }

        /// <summary>
        /// Solve the perspective projection for a ground-plane follow target at the requested screen height.
        /// Recomputed on zoom, so the avatar remains centered horizontally and above the bottom HUD.
        /// </summary>
        public static float LookAheadForViewport(float distance, float pitchDegrees, float fovDegrees, float viewportY, float focusHeight)
        {
            float angle = pitchDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
            float q = (2f * viewportY - 1f) * Mathf.Tan(fovDegrees * Mathf.Deg2Rad * 0.5f);
            return -(q * distance + focusHeight * (cos + q * sin)) / (sin - q * cos);
        }
    }
}
