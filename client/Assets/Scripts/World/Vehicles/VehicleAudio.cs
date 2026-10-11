using System.Collections.Generic;
using CTLife.Audio;
using UnityEngine;

namespace CTLife.World.Vehicles
{
    /// <summary>
    /// Vehicle sound (Feel lane F2), all real CC0 recordings (Resources/Vehicles/Audio, provenance in sources.json): the
    /// engine loop pitched by rpm and opened by throttle, tyre squeal by slip, rotor/drone loops by rotor speed, the
    /// Roadstar's hover hum and thrust, impacts by impulse, doors and ignition. 3D sources at the vehicle; levels follow
    /// the player's SFX slider (AudioManager.SfxGain).
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public sealed class VehicleAudio : MonoBehaviour
    {
        private Vehicle _v;
        private CarPhysics _car;
        private FlightPhysics _flight;
        private AudioSource _engine, _skid, _hum, _thrust, _oneShot;
        private float _engineVol;
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();

        public static AudioClip Clip(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!Clips.TryGetValue(name, out var c)) { c = Resources.Load<AudioClip>("Vehicles/Audio/" + name); Clips[name] = c; }
            return c;
        }

        private AudioSource Source(string clip, bool loop, float minDistance, float maxDistance)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = Clip(clip); s.loop = loop; s.playOnAwake = false; s.spatialBlend = 1f; s.dopplerLevel = 0.3f;
            s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = minDistance; s.maxDistance = maxDistance; s.volume = 0f;
            return s;
        }

        private void Awake()
        {
            _v = GetComponent<Vehicle>();
            _car = GetComponent<CarPhysics>();
            _flight = GetComponent<FlightPhysics>();
            var s = _v.Spec ?? VehicleCatalog.Get(_v.specId) ?? new VehicleSpec();
            bool big = s.kind == VehicleKind.Helicopter;
            _engine = Source(s.engineSound, true, big ? 12f : 3f, big ? 260f : 70f);
            if (_car != null) _skid = Source("skid", true, 4f, 70f);
            if (s.kind == VehicleKind.Roadstar) { _hum = Source("hover_hum", true, 3f, 80f); _thrust = Source("jet_thrust", true, 4f, 120f); }
            _oneShot = Source(null, false, 3f, 90f);
        }

        private float Gain => AudioManager.Instance != null ? AudioManager.Instance.SfxGain : 1f;

        public void StartEngine()
        {
            var s = _v.Spec;
            if (s != null && !string.IsNullOrEmpty(s.startSound)) Play(s.startSound, 0.9f);
        }

        public void StopEngine() { }

        public void Door(bool open) => Play(open ? "door_open" : "door_close", 0.8f);

        public void Play(string clip, float volume)
        {
            var c = Clip(clip);
            if (c != null && _oneShot != null) _oneShot.PlayOneShot(c, volume * Gain);
        }

        private void OnEnable()
        {
            if (_car == null) _car = GetComponent<CarPhysics>();
            if (_car != null) _car.Impact += OnImpact;
        }

        private void OnDisable()
        {
            if (_car != null) _car.Impact -= OnImpact;
        }

        private void OnImpact(float impulse, Vector3 point)
        {
            float k = Mathf.Clamp01(impulse / (_v.Spec != null ? _v.Spec.mass * 9f : 9000f));
            if (_oneShot == null) return;
            _oneShot.pitch = Random.Range(0.9f, 1.08f);
            _oneShot.PlayOneShot(Clip("impact"), Mathf.Lerp(0.25f, 1f, k) * Gain);
            if (_v.Driver != null && _v.Driver.isLocal) CameraRig.Instance?.Shake(Mathf.Lerp(0.15f, 0.7f, k));
        }

        private void Update()
        {
            var s = _v.Spec;
            if (s == null) return;
            float gain = Gain;
            bool on = _v.EngineOn;
            float rpm, open;
            if (_flight != null && (s.kind == VehicleKind.Helicopter || s.kind == VehicleKind.Drone))
            {
                rpm = _flight.Rotor01; open = _flight.Rotor01 * (0.6f + 0.4f * _flight.Thrust01);
            }
            else
            {
                rpm = _car != null ? _car.Rpm01 : 0f;
                open = on ? 0.35f + 0.65f * Mathf.Max(_v.Input.throttle, _v.Input.brake * 0.4f) : 0f;
            }
            _engineVol = Mathf.MoveTowards(_engineVol, on || rpm > 0.05f ? open : 0f, Time.deltaTime * 1.8f);
            Loop(_engine, _engineVol * 0.9f * gain, Mathf.Lerp(s.enginePitchIdle, s.enginePitchMax, rpm));
            if (_skid != null) Loop(_skid, (_car != null && !_v.Flying ? _car.SkidAmount : 0f) * 0.85f * gain, 0.95f + 0.1f * (_car != null ? _car.SkidAmount : 0f));
            if (_hum != null && _flight != null)
            {
                float f = _flight.Fold01;
                Loop(_hum, f * 0.6f * gain, 0.9f + 0.25f * _flight.Thrust01);
                Loop(_thrust, f * Mathf.Clamp01(_v.Input.throttle + 0.15f) * 0.75f * gain, 0.85f + 0.35f * Mathf.Clamp01(_v.Speed / 60f));
                if (_engine != null && f > 0.01f) _engine.volume *= 1f - f;   // the road motor gives way to the pads
            }
        }

        private static void Loop(AudioSource src, float volume, float pitch)
        {
            if (src == null || src.clip == null) return;
            src.volume = volume;
            src.pitch = pitch;
            if (volume > 0.005f) { if (!src.isPlaying) src.Play(); }
            else if (src.isPlaying) src.Stop();
        }
    }
}
