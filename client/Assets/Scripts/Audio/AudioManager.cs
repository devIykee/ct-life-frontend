using System.Collections.Generic;
using System.IO;
using CTLife.Core;
using UnityEngine;

namespace CTLife.Audio
{
    /// <summary>
    /// SFX, ambience and stingers driven by audio/sfx-map.json (Build Spec "Sound design"). Clips are loaded from
    /// Resources/Audio/&lt;path without extension&gt;. Unknown IDs or missing files play a default blip and are logged
    /// once (contract section 2 rule). Stingers duck the music; nearby voice ducks it 6 dB.
    /// [Unverified] The spec names FMOD Studio; its Unity integration version is still an open question, so this
    /// runs on Unity audio until FMOD is pinned.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Range(0, 1)] public float sfxVolume = 1f;
        [Range(0, 1)] public float musicVolume = 0.7f;
        [Range(0, 1)] public float ambienceVolume = 0.6f;

        public SfxMap Map { get; private set; } = new SfxMap();
        public MusicDirector Music { get; private set; }

        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private AudioSource _ambience;
        private AudioClip _blip;
        private Subscriptions _subs;
        private float _stingerDuckUntil;
        private bool _voiceDuck;

        public static AudioManager Create()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("CTLife.Audio");
            DontDestroyOnLoad(go);
            return go.AddComponent<AudioManager>();
        }

        private void Awake()
        {
            Instance = this;
            for (int i = 0; i < 8; i++) _pool.Add(NewSource("sfx" + i));
            _ambience = NewSource("ambience");
            _ambience.loop = true;
            Music = gameObject.AddComponent<MusicDirector>();
            Music.Init(this);
            _blip = Babble.MakeBlip(880f, 0.06f);
            LoadMap();

            _subs = new Subscriptions();
            _subs.On<SfxSignal>(s => Play(s.sfxId, s.volume <= 0 ? 1f : s.volume));
            _subs.On<VoiceActivitySignal>(v => _voiceDuck = v.anyoneTalking);
            _subs.On<MoodSignal>(m => Music.SetMood(m.bps));
        }

        private void OnDestroy() => _subs?.Dispose();

        private AudioSource NewSource(string n)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        public void LoadMap()
        {
            var ta = Resources.Load<TextAsset>("Audio/sfx-map");
            Map = ta != null ? SfxMap.Parse(ta.text) : new SfxMap();
            Log.Info("audio", $"sfx-map: {Map.Sfx.Count} sfx, {Map.Music.Count} music sets, {Map.Babble.Count} babble sets" +
                              (ta == null ? " (missing: Resources/Audio/sfx-map.json)" : ""));
        }

        public AudioClip Clip(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            if (_clips.TryGetValue(relativePath, out var c)) return c;
            var key = "Audio/" + Path.ChangeExtension(relativePath, null).Replace('\\', '/');
            c = Resources.Load<AudioClip>(key);
            if (c == null) Log.UnknownId("audio_file", relativePath);
            _clips[relativePath] = c;
            return c;
        }

        /// <summary>Plays an sfx.json id as a 2D one-shot.</summary>
        public void Play(string sfxId, float volume = 1f)
        {
            var src = NextSource();
            if (!Map.Sfx.TryGetValue(sfxId ?? "", out var entry))
            {
                Log.UnknownId("sfx", sfxId);
                src.pitch = 1f;
                src.PlayOneShot(_blip, 0.4f * sfxVolume * volume);
                return;
            }
            var clip = Clip(entry.files[Random.Range(0, entry.files.Count)]) ?? _blip;
            src.pitch = 1f + Random.Range(-entry.pitchJitter, entry.pitchJitter);
            src.PlayOneShot(clip, entry.volume * sfxVolume * volume);
            if (entry.category == "stinger") _stingerDuckUntil = Time.unscaledTime + clip.length;
        }

        /// <summary>3D one-shot at a world position (footsteps, NPC reactions).</summary>
        public void PlayAt(string sfxId, Vector3 position, float volume = 1f)
        {
            if (!Map.Sfx.TryGetValue(sfxId ?? "", out var entry)) { Log.UnknownId("sfx", sfxId); return; }
            var clip = Clip(entry.files[Random.Range(0, entry.files.Count)]);
            if (clip != null) AudioSource.PlayClipAtPoint(clip, position, entry.volume * sfxVolume * volume);
        }

        /// <summary>Loops a venue ambience (keys in sfx-map "ambience", usually the venueId).</summary>
        public void SetAmbience(string key)
        {
            if (key != null && Map.Ambience.TryGetValue(key, out var e))
            {
                var clip = Clip(e.files[0]);
                if (clip != null && _ambience.clip != clip)
                {
                    _ambience.clip = clip;
                    _ambience.volume = e.volume * ambienceVolume;
                    _ambience.Play();
                }
                return;
            }
            _ambience.Stop();
            _ambience.clip = null;
        }

        private AudioSource NextSource()
        {
            foreach (var s in _pool)
                if (!s.isPlaying) return s;
            return _pool[Random.Range(0, _pool.Count)];
        }

        /// <summary>Music gain in dB: −6 dB while someone nearby talks, −9 dB during a stinger.</summary>
        public float MusicDuckDb => (Time.unscaledTime < _stingerDuckUntil ? -9f : 0f) + (_voiceDuck ? -6f : 0f);
    }
}
