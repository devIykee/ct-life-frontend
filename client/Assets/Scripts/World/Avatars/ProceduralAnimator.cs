using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Bone-level procedural animation for <see cref="PlaceholderHumanoid"/>. A locomotion base (idle/walk/run/sit)
    /// plus an overlay clip (act/emote/react id from anims.json) with fade in/out. Real Animator clips replace this
    /// once the art pipeline delivers them; <see cref="AvatarView"/> picks whichever exists.
    /// </summary>
    public sealed class ProceduralAnimator : MonoBehaviour
    {
        private readonly Dictionary<string, Transform> _b = new Dictionary<string, Transform>();
        private readonly Dictionary<Transform, Quaternion> _rest = new Dictionary<Transform, Quaternion>();
        private Vector3 _pelvisRest;
        private Transform _pelvis;

        private string _base = "loco_idle";
        private string _overlay;
        private float _overlayStart, _overlayDuration;
        private bool _overlayLoop;
        private float _overlayWeight;
        private float _speed;
        private float _phase;
        private float _seed;

        /// <summary>1 = every frame; 2 = every other frame (LOD1); 4 = LOD2. Set by AvatarLod.</summary>
        public int UpdateInterval { get; set; } = 1;
        private int _frameOffset;
        private float _accum;

        public void Bind(Transform[] bones)
        {
            foreach (var t in bones)
            {
                _b[t.name] = t;
                _rest[t] = t.localRotation;
            }
            _pelvis = _b["pelvis"];
            _pelvisRest = _pelvis.localPosition;
            _seed = Random.value * 10f;
            _frameOffset = Random.Range(0, 4);
        }

        public void SetBase(string animId) => _base = string.IsNullOrEmpty(animId) ? "loco_idle" : animId;

        public void SetSpeed(float metresPerSecond) => _speed = metresPerSecond;

        /// <summary>Plays an act/emote/react overlay. duration ≤ 0 with loop=true runs until cleared.</summary>
        public void PlayOverlay(string animId, float duration, bool loop)
        {
            _overlay = animId;
            _overlayStart = Time.time;
            _overlayDuration = duration;
            _overlayLoop = loop;
        }

        public void ClearOverlay() => _overlay = null;

        public bool HasOverlay => _overlay != null;

        private void LateUpdate()
        {
            _accum += Time.deltaTime;
            if (UpdateInterval > 1 && (Time.frameCount + _frameOffset) % UpdateInterval != 0) return;
            float dt = _accum;
            _accum = 0f;

            if (_overlay != null && !_overlayLoop && Time.time - _overlayStart > _overlayDuration) _overlay = null;
            _overlayWeight = Mathf.MoveTowards(_overlayWeight, _overlay != null ? 1f : 0f, dt * 6f);

            float gait = _base == "loco_run" ? 2.6f : _base == "loco_walk" ? 1.7f : 0f;
            if (_speed > 0.05f) gait = Mathf.Max(gait, Mathf.Lerp(1.4f, 2.8f, Mathf.InverseLerp(0.5f, 4.5f, _speed)));
            _phase += dt * (gait > 0 ? gait * Mathf.PI * 2f * 0.9f : 1f);

            ResetPose();
            if (_base == "act_sit" || _base == "act_type" || _base == "act_frantic_type" || _base == "act_lean_back" || _base == "act_head_in_hands")
                Sit(_base);
            else if (gait > 0f) Gait(gait);
            else Idle();

            if (_overlay != null || _overlayWeight > 0.01f) Overlay(_overlay, Time.time - _overlayStart, _overlayWeight);
        }

        private void ResetPose()
        {
            foreach (var kv in _rest) kv.Key.localRotation = kv.Value;
            _pelvis.localPosition = _pelvisRest;
        }

        private void Rot(string bone, float x, float y, float z, float w = 1f)
        {
            if (!_b.TryGetValue(bone, out var t)) return;
            t.localRotation = t.localRotation * Quaternion.Euler(x * w, y * w, z * w);
        }

        private void Lift(float y) => _pelvis.localPosition += new Vector3(0, y, 0);

        private void Idle()
        {
            float t = Time.time + _seed;
            float breathe = Mathf.Sin(t * 1.6f);
            Rot("spine_02", breathe * 1.2f, 0, 0);
            Rot("Head", Mathf.Sin(t * 0.5f) * 3f, Mathf.Sin(t * 0.31f) * 8f, 0);
            Rot("upperarm_l", 0, 0, 2f + breathe);
            Rot("upperarm_r", 0, 0, -2f - breathe);
        }

        private void Gait(float gait)
        {
            float s = Mathf.Sin(_phase), c = Mathf.Cos(_phase);
            float amp = gait > 2.2f ? 1.6f : 1f;
            Rot("thigh_l", 28f * s * amp, 0, 0);
            Rot("thigh_r", -28f * s * amp, 0, 0);
            Rot("calf_l", Mathf.Max(0, -c) * 35f * amp, 0, 0);
            Rot("calf_r", Mathf.Max(0, c) * 35f * amp, 0, 0);
            Rot("upperarm_l", -22f * s * amp, 0, 0);
            Rot("upperarm_r", 22f * s * amp, 0, 0);
            Rot("lowerarm_l", -15f * amp, 0, 0);
            Rot("lowerarm_r", -15f * amp, 0, 0);
            Rot("spine_01", (gait > 2.2f ? 8f : 2f), 6f * s, 0);
            Lift(Mathf.Abs(c) * 0.03f * amp - 0.015f);
        }

        private void Sit(string mode)
        {
            Lift(-0.43f);
            Rot("thigh_l", -88f, 0, 0);
            Rot("thigh_r", -88f, 0, 0);
            Rot("calf_l", 88f, 0, 0);
            Rot("calf_r", 88f, 0, 0);
            float t = Time.time + _seed;
            switch (mode)
            {
                case "act_type":
                case "act_frantic_type":
                    float speed = mode == "act_frantic_type" ? 22f : 9f;
                    Rot("upperarm_l", -45f, 0, 8f);
                    Rot("upperarm_r", -45f, 0, -8f);
                    Rot("lowerarm_l", -55f + Mathf.Sin(t * speed) * 6f, 0, 0);
                    Rot("lowerarm_r", -55f + Mathf.Sin(t * speed + 1.7f) * 6f, 0, 0);
                    Rot("Head", 12f, 0, 0);
                    break;
                case "act_lean_back":
                    Rot("spine_01", -18f, 0, 0);
                    Rot("upperarm_l", 0, 0, 60f);
                    Rot("upperarm_r", 0, 0, -60f);
                    Rot("lowerarm_l", -110f, 0, 0);
                    Rot("lowerarm_r", -110f, 0, 0);
                    break;
                case "act_head_in_hands":
                    Rot("spine_01", 25f, 0, 0);
                    Rot("Head", 25f, 0, 0);
                    Rot("upperarm_l", -60f, 0, 15f);
                    Rot("upperarm_r", -60f, 0, -15f);
                    Rot("lowerarm_l", -120f, 0, 0);
                    Rot("lowerarm_r", -120f, 0, 0);
                    break;
                default:
                    Rot("upperarm_l", -12f, 0, 4f);
                    Rot("upperarm_r", -12f, 0, -4f);
                    Rot("spine_02", Mathf.Sin(t * 1.4f) * 1f, 0, 0);
                    break;
            }
        }

        /// <summary>Parametric gestures for act/emote/react ids. Unknown ids fall back to a wave.</summary>
        private void Overlay(string id, float t, float w)
        {
            if (id == null) return;
            float k = Mathf.Sin(t * 8f);
            switch (id)
            {
                case "emote_wave":
                case "emote_heart_hands":
                case "emote_salute":
                    Rot("upperarm_r", 0, 0, -150f, w);
                    Rot("lowerarm_r", id == "emote_salute" ? -100f : 0, id == "emote_wave" ? k * 25f : 0, 0, w);
                    if (id == "emote_heart_hands") { Rot("upperarm_l", 0, 0, 150f, w); Rot("lowerarm_l", -60f, 0, 0, w); Rot("lowerarm_r", -60f, 0, 0, w); }
                    break;
                case "emote_point":
                case "emote_thumbs_up":
                case "emote_ring_light":
                    Rot("upperarm_r", -80f, 0, -10f, w);
                    Rot("lowerarm_r", id == "emote_thumbs_up" ? -60f : 0, 0, 0, w);
                    break;
                case "emote_clap":
                case "emote_laugh":
                    Rot("upperarm_l", -50f, 0, -20f, w);
                    Rot("upperarm_r", -50f, 0, 20f, w);
                    Rot("lowerarm_l", -60f + k * 12f, 0, 0, w);
                    Rot("lowerarm_r", -60f + k * 12f, 0, 0, w);
                    if (id == "emote_laugh") Rot("spine_02", -10f + k * 4f, 0, 0, w);
                    break;
                case "emote_facepalm":
                case "act_cry":
                case "react_sad":
                    Rot("Head", 22f, 0, 0, w);
                    Rot("upperarm_r", -70f, 0, 20f, w);
                    Rot("lowerarm_r", -130f, 0, 0, w);
                    if (id == "act_cry") { Rot("upperarm_l", -70f, 0, -20f, w); Rot("lowerarm_l", -130f, 0, 0, w); Rot("spine_02", 12f + k * 2f, 0, 0, w); }
                    break;
                case "emote_shrug":
                case "react_shock":
                    Rot("upperarm_l", 0, 0, 40f, w);
                    Rot("upperarm_r", 0, 0, -40f, w);
                    Rot("lowerarm_l", -70f, 0, 0, w);
                    Rot("lowerarm_r", -70f, 0, 0, w);
                    Rot("Head", -8f, 0, id == "emote_shrug" ? 10f : 0f, w);
                    break;
                case "emote_nod":
                case "act_listen_nod":
                    Rot("Head", Mathf.Max(0, k) * 15f, 0, 0, w);
                    break;
                case "emote_head_shake":
                    Rot("Head", 0, k * 25f, 0, w);
                    break;
                case "emote_bow":
                    Rot("spine_01", 40f * Mathf.Sin(Mathf.Clamp01(t / 2f) * Mathf.PI), 0, 0, w);
                    break;
                case "emote_wagmi":
                case "emote_chest_thump":
                case "emote_send_it":
                    Rot("upperarm_r", 0, 0, -160f + k * 10f, w);
                    Rot("lowerarm_r", -40f, 0, 0, w);
                    if (id == "emote_send_it") Lift(-0.15f * Mathf.Abs(k) * w);
                    break;
                case "emote_moon_jump":
                case "react_moon_jump":
                    Lift(Mathf.Max(0, Mathf.Sin(t * Mathf.PI)) * 0.45f * w);
                    Rot("upperarm_l", 0, 0, 160f, w);
                    Rot("upperarm_r", 0, 0, -160f, w);
                    break;
                case "act_dance_1":
                case "act_dance_2":
                    Lift(Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * 0.08f * w);
                    Rot("upperarm_l", 0, 0, 90f + k * 40f, w);
                    Rot("upperarm_r", 0, 0, -90f - k * 40f, w);
                    Rot("spine_01", 0, k * 15f, 0, w);
                    break;
                case "act_host_mic":
                case "emote_gm_coffee":
                case "act_phone_scroll":
                    Rot("upperarm_r", -50f, 0, 15f, w);
                    Rot("lowerarm_r", -110f, 0, 0, w);
                    Rot("Head", id == "act_phone_scroll" ? 20f : 0f, 0, 0, w);
                    break;
                case "act_farm":
                case "emote_watering_can":
                case "emote_pickaxe":
                    Rot("spine_01", 25f + k * 10f, 0, 0, w);
                    Rot("upperarm_l", -60f + k * 20f, 0, 0, w);
                    Rot("upperarm_r", -60f + k * 20f, 0, 0, w);
                    break;
                case "emote_coin_flip":
                    Rot("upperarm_r", -40f, 0, 0, w);
                    Rot("lowerarm_r", -90f + Mathf.Max(0, k) * 40f, 0, 0, w);
                    Rot("Head", -20f, 0, 0, w);
                    break;
                case "emote_hood_up":
                    Rot("upperarm_l", 0, 0, 150f, w);
                    Rot("upperarm_r", 0, 0, -150f, w);
                    Rot("lowerarm_l", -120f, 0, 0, w);
                    Rot("lowerarm_r", -120f, 0, 0, w);
                    break;
                case "emote_frantic_type":
                    Rot("upperarm_l", -45f, 0, 8f, w);
                    Rot("upperarm_r", -45f, 0, -8f, w);
                    Rot("lowerarm_l", -55f + Mathf.Sin(t * 22f) * 8f, 0, 0, w);
                    Rot("lowerarm_r", -55f + Mathf.Sin(t * 22f + 1.7f) * 8f, 0, 0, w);
                    break;
                case "react_liquidated":
                case "react_rugged":
                    // Stagger → collapse → faceplant.
                    float fall = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1.6f, t));
                    float dir = id == "react_rugged" ? -1f : 1f;
                    Rot("spine_01", (8f * Mathf.Sin(t * 10f) * (1f - fall) + 80f * fall * dir), 0, 0, w);
                    Rot("thigh_l", -30f * fall, 0, 0, w);
                    Rot("thigh_r", -30f * fall, 0, 0, w);
                    Lift(-0.7f * fall * w);
                    Rot("upperarm_l", 0, 0, 70f * fall, w);
                    Rot("upperarm_r", 0, 0, -70f * fall, w);
                    break;
                case "react_rage_stomp":
                    Rot("thigh_r", -50f * Mathf.Max(0, k), 0, 0, w);
                    Rot("upperarm_l", 0, 0, 30f, w);
                    Rot("upperarm_r", 0, 0, -30f, w);
                    Rot("lowerarm_l", -100f, 0, 0, w);
                    Rot("lowerarm_r", -100f, 0, 0, w);
                    break;
                case "react_run_away":
                    Gait(2.8f);
                    break;
                case "react_ghost_respawn":
                    float pop = Mathf.Clamp01(t / 0.6f);
                    transform.localScale = Vector3.one * Mathf.Lerp(0.2f, 1f, Mathf.SmoothStep(0, 1, pop));
                    break;
                default:
                    Rot("upperarm_r", 0, 0, -140f, w);
                    Rot("lowerarm_r", 0, k * 20f, 0, w);
                    break;
            }
        }
    }
}
