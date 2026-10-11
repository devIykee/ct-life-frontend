using UnityEngine;

namespace CTLife.Audio
{
    /// <summary>
    /// Footsteps for another avatar (remote player or NPC). Measures its own horizontal travel each frame, steps with
    /// the speed-driven cadence, and asks <see cref="AudioManager.PlayOtherFootstep"/>, which applies the shared
    /// budget (distance, voice cap, thinning). Add once per avatar root: <c>Footsteps.Attach(gameObject)</c>.
    /// Never add it to the local player (LocalPlayerController plays its own steps through PlayFootstep).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FootstepEmitter : MonoBehaviour
    {
        [Tooltip("Relative loudness of this avatar's steps (NPC crowds a little quieter).")]
        [Range(0f, 1f)] public float gain = 0.8f;

        private readonly FootstepCadence _cadence = new FootstepCadence();
        private Vector3 _last;
        private bool _hasLast;
        private int _id;

        private void OnEnable()
        {
            _id = GetInstanceID();
            _hasLast = false;
            _cadence.Reset();
        }

        private void OnDisable() => AudioManager.Instance?.OtherFootsteps.Forget(_id);

        private void LateUpdate()
        {
            var pos = transform.position;
            if (!_hasLast) { _last = pos; _hasLast = true; return; }
            var travelled = pos - _last;
            travelled.y = 0f;
            _last = pos;
            float dt = Time.deltaTime;
            float metres = travelled.magnitude;
            float speed = dt > 1e-5f ? metres / dt : 0f;
            if (!_cadence.AdvanceAtSpeed(metres, dt, speed)) return;
            var audio = AudioManager.Instance;
            if (audio != null) audio.PlayOtherFootstep(_id, pos, gain);
        }
    }
}
