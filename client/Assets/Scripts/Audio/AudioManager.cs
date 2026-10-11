using System;
using System.Collections.Generic;
using System.IO;
using CTLife.Core;
using UnityEngine;

namespace CTLife.Audio
{
    /// <summary>
    /// CT Life's code mixer (Build Spec "Sound design"; runtime contract in audio/RUNTIME_API.md).
    /// Buses: music (MusicDirector), sfx (one-shots, stingers, loops, footsteps, babble), ambience, voice (LiveKit,
    /// applied by the voice owner). Master is AudioListener.volume. Clips come from Resources/Audio/&lt;path&gt; listed in
    /// sfx-map.json. Unknown ids play a default blip and are logged once (contract section 2 rule).
    /// Stingers duck the music 9 dB for their length; nearby voice / Space speakers duck it 6 dB (deepest wins).
    /// [Unverified] The spec names FMOD Studio; its Unity integration is not pinned, so this runs on Unity audio.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        /// <summary>Raised after any volume setting changes (Settings UI refresh, voice owners re-applying gain).</summary>
        public static event Action SettingsChanged;

        public const int SfxVoices = 8, LoopVoices = 2, AmbienceVoices = 2, LocalFootstepVoices = 4;
        public const int OwnedSourceCount = SfxVoices + LoopVoices + AmbienceVoices + LocalFootstepVoices + FootstepBudget.MaxVoices;
        public const float LoopMaxSeconds = 45f;

        private readonly VolumeSettings _settings = new VolumeSettings();
        public VolumeSettings Settings => _settings;
        public float masterVolume { get => _settings.Master; set => SetSetting("master", value); }
        public float musicVolume { get => _settings.Music; set => SetSetting("music", value); }
        public float sfxVolume { get => _settings.Sfx; set => SetSetting("sfx", value); }
        public float voiceVolume { get => _settings.Voice; set => SetSetting("voice", value); }
        public float ambienceVolume { get => _settings.Ambience; set => SetSetting("ambience", value); }

        /// <summary>Linear gain of a bus after the player's slider (master excluded; master is the listener volume).</summary>
        public float BusGain(AudioBus bus) => _settings.BusGain(bus);
        public float SfxGain => _settings.BusGain(AudioBus.Sfx);

        public SfxMap Map { get; private set; } = new SfxMap();
        public MusicDirector Music { get; private set; }
        public Ducker Ducking { get; } = new Ducker();
        public CueGate Cues { get; } = new CueGate();
        public BabbleBudget BabbleBudget { get; } = new BabbleBudget();
        public FootstepBudget OtherFootsteps { get; } = new FootstepBudget();

        /// <summary>The local player's @handle (without @) for mention cues; set by the profile owner.</summary>
        public string LocalHandle { get; set; }

        /// <summary>Venue whose floor default applies to footsteps (null on the city map / lobby).</summary>
        public string VenueId { get; private set; }
        private bool _venueIsClub;
        private string _surfaceOverride;

        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private readonly Dictionary<AudioSource, long> _poolStartOrder = new Dictionary<AudioSource, long>();
        private readonly Dictionary<AudioSource, string> _poolCategories = new Dictionary<AudioSource, string>();
        private readonly Dictionary<AudioSource, string> _poolIds = new Dictionary<AudioSource, string>();
        private readonly Dictionary<AudioSource, double> _poolStartedAt = new Dictionary<AudioSource, double>();
        private long _sfxSequence;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly AudioSource[] _loops = new AudioSource[LoopVoices];
        private readonly string[] _loopKeys = new string[LoopVoices];
        private readonly float[] _loopGains = new float[LoopVoices];
        private readonly double[] _loopEnds = new double[LoopVoices];
        private readonly AudioSource[] _ambience = new AudioSource[AmbienceVoices];
        // Fade state must survive hot reload; source lookup caches below are rebuilt from named children.
        [SerializeField, HideInInspector] private float[] _ambienceWeights = new float[AmbienceVoices], _ambienceGains = new float[AmbienceVoices];
        private int _ambienceTarget = -1;
        private readonly AudioSource[] _footsteps = new AudioSource[LocalFootstepVoices];
        private readonly float[] _footstepGains = new float[LocalFootstepVoices];
        private readonly AudioSource[] _otherSteps = new AudioSource[FootstepBudget.MaxVoices];
        private readonly float[] _otherStepGains = new float[FootstepBudget.MaxVoices];
        private readonly Dictionary<string, int> _lastFootstep = new Dictionary<string, int>();
        private int _nextFootstep;
        private AudioListener _playerListener, _cameraListener;
        private Transform _listenerOwner, _listenerAnchor;
        private bool _prefsDirty;
        private float _savePrefsAt;
        private float _appliedMaster = -1f;
        private AudioClip _blip;
        [NonSerialized] private Subscriptions _subs;
        [NonSerialized] private bool _runtimeReady, _restoringRuntime;
        private readonly FootstepSurfaces _surfaces = new FootstepSurfaces();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private long _reviewFootstepsPlayed, _reviewOtherStepsPlayed;
#endif

        private static double Now => Time.unscaledTimeAsDouble;

        public static AudioManager Create()
        {
            if (Instance != null) return Instance;
            var existing = FindFirstObjectByType<AudioManager>();
            if (existing != null) { existing.RestoreRuntime(); return existing; }
            var go = new GameObject("CTLife.Audio");
            DontDestroyOnLoad(go);
            return go.AddComponent<AudioManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            RestoreRuntime();
        }

        private void OnEnable()
        {
            if (Instance != null && Instance != this) return;
            RestoreRuntime();
            BindSignals();
            RefreshListener();
        }

        /// <summary>
        /// Unity 6.3 reloads serializable state, but not readonly arrays/dictionaries or static singletons.
        /// Rebind the existing native sources once, preserving clips and fade state without duplicate children.
        /// Called by either audio component's OnEnable because their callback order is not guaranteed.
        /// </summary>
        internal void RestoreRuntime()
        {
            if (_runtimeReady || _restoringRuntime || !isActiveAndEnabled) return;
            _restoringRuntime = true;
            try
            {
                Instance = this;
                // Settings are plain (non-serialized) state: reload them here so a domain reload keeps the player's levels.
                foreach (var key in VolumeSettings.Keys)
                    _settings.Set(key, PlayerPrefs.GetFloat(VolumeSettings.PrefPrefix + key, VolumeSettings.Default(key)));
                _pool.Clear();
                _poolStartOrder.Clear();
                _poolCategories.Clear();
                _poolIds.Clear();
                _poolStartedAt.Clear();
                if (_ambienceWeights == null || _ambienceWeights.Length != AmbienceVoices) _ambienceWeights = new float[AmbienceVoices];
                if (_ambienceGains == null || _ambienceGains.Length != AmbienceVoices) _ambienceGains = new float[AmbienceVoices];
                for (int i = 0; i < SfxVoices; i++)
                {
                    var source = Spatial(NewSource("sfx" + i), 1.5f, 12f);
                    source.spatialBlend = 0f;
                    _pool.Add(source);
                    _poolStartOrder[source] = 0;
                }
                for (int i = 0; i < LoopVoices; i++)
                {
                    _loops[i] = NewSource("loop" + i);
                    _loops[i].loop = true;
                }
                for (int i = 0; i < AmbienceVoices; i++)
                {
                    _ambience[i] = NewSource("ambience" + i);
                    _ambience[i].loop = true;
                    _ambience[i].UnPause();
                }
                for (int i = 0; i < LocalFootstepVoices; i++) _footsteps[i] = Spatial(NewSource("footstep" + i), 1.5f, 12f);
                for (int i = 0; i < FootstepBudget.MaxVoices; i++)
                    _otherSteps[i] = Spatial(NewSource("other_footstep" + i), 1.5f, FootstepBudget.MaxDistance);
                var listenerChild = transform.Find("player_listener");
                if (listenerChild == null)
                {
                    var listenerGo = new GameObject("player_listener");
                    listenerGo.SetActive(false);
                    listenerGo.transform.SetParent(transform, false);
                    _playerListener = listenerGo.AddComponent<AudioListener>();
                    _playerListener.enabled = false;
                    listenerGo.SetActive(true);
                }
                else
                {
                    _playerListener = listenerChild.GetComponent<AudioListener>();
                    if (_playerListener == null) _playerListener = listenerChild.gameObject.AddComponent<AudioListener>();
                }
                LoadMap();
                if (_blip == null) _blip = CTLife.Audio.Babble.MakeBlip(880f, 0.06f);
                Music = GetComponent<MusicDirector>();
                if (Music == null) Music = gameObject.AddComponent<MusicDirector>();
                Music.Init(this);
                ApplyGains(force: true);
                _runtimeReady = true;
                Log.Info("audio", $"Runtime restored: sfx={_pool.Count} loops={LoopVoices} ambience={AmbienceVoices} " +
                                  $"footsteps={LocalFootstepVoices}+{FootstepBudget.MaxVoices}");
            }
            finally { _restoringRuntime = false; }
        }

        private static AudioSource Spatial(AudioSource source, float min, float max)
        {
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = min;
            source.maxDistance = max;
            source.dopplerLevel = 0f;
            return source;
        }

        private void BindSignals()
        {
            _subs?.Dispose();
            _subs = new Subscriptions();
            // SfxSignal.volume 0 is the struct default (an initializer that forgot it), not a request for silence.
            _subs.On<SfxSignal>(s => Play(s.sfxId, s.volume > 0f ? s.volume : 1f));
            _subs.On<VoiceActivitySignal>(v => SetVoiceDuck(Ducker.VoiceKey, v.anyoneTalking));
            _subs.On<MoodSignal>(m => Music.SetMood(m.bps));
            _subs.On<VenueChangedSignal>(v =>
            {
                _surfaces.Clear();
                if (v.venueId != null) { NoteVenue(v.venueId, _venueIsClub && v.venueId == VenueId); return; }
                NoteVenue(null, false);
                SetAmbience(null);
                Music.SetDistrict(null);
            });
        }

        private void OnDisable()
        {
            _subs?.Dispose();
            _subs = null;
            // Transients are not replayed after a reload. Loops keep their clips/playheads and resume on enable.
            foreach (var source in _pool) if (source != null) source.Stop();
            foreach (var source in _footsteps) if (source != null) source.Stop();
            foreach (var source in _otherSteps) if (source != null) source.Stop();
            for (int i = 0; i < LoopVoices; i++) StopLoopAt(i);
            foreach (var source in _ambience) if (source != null) source.Pause();
            Ducking.ClearAll();
            _runtimeReady = false;
            SaveVolumes();
            if (_playerListener != null) _playerListener.enabled = false;
            if (_cameraListener != null) _cameraListener.enabled = true;
        }

        private void OnDestroy()
        {
            _subs?.Dispose();
            SaveVolumes();
            if (_playerListener != null) _playerListener.enabled = false;
            if (_cameraListener != null) _cameraListener.enabled = true;
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ settings

        public float GetSetting(string key) => _settings.Get(key);

        public void SetSetting(string key, float value)
        {
            if (!_settings.Set(key, value)) return;
            PlayerPrefs.SetFloat(VolumeSettings.PrefPrefix + key, _settings.Get(key));
            _prefsDirty = true;
            _savePrefsAt = Time.unscaledTime + 0.5f;
            ApplyGains(force: true);
            try { SettingsChanged?.Invoke(); }
            catch (Exception e) { Log.Exception("audio", e); }
        }

        private void SaveVolumes() { if (_prefsDirty) { PlayerPrefs.Save(); _prefsDirty = false; } }
        private void OnApplicationPause(bool paused) { if (paused) SaveVolumes(); }
        private void OnApplicationQuit() => SaveVolumes();

        // ------------------------------------------------------------------ listener

        public void RegisterCameraListener(AudioListener listener)
        {
            if (_cameraListener != null && _cameraListener != listener) _cameraListener.enabled = false;
            _cameraListener = listener;
            RefreshListener();
        }

        public void BindPlayerListener(Transform owner, Transform head)
        {
            _listenerOwner = owner;
            _listenerAnchor = head != null ? head : owner;
            RefreshListener();
            LateUpdate();
        }

        public void ReleasePlayerListener(Transform owner)
        {
            if (_listenerOwner != owner) return;
            _listenerOwner = _listenerAnchor = null;
            foreach (var source in _footsteps) source.Stop();
            RefreshListener();
        }

        /// <summary>World position the player hears from (player head, else camera, else origin).</summary>
        public Vector3 ListenerPosition
        {
            get
            {
                if (_playerListener != null && _playerListener.enabled) return _playerListener.transform.position;
                if (_cameraListener != null && _cameraListener.isActiveAndEnabled) return _cameraListener.transform.position;
                return Vector3.zero;
            }
        }

        private void RefreshListener()
        {
            bool local = _listenerAnchor != null && _listenerAnchor.gameObject.activeInHierarchy;
            // Disable the old listener first so a transition never leaves two enabled listeners.
            if (local)
            {
                if (_cameraListener != null) _cameraListener.enabled = false;
                _playerListener.enabled = true;
            }
            else
            {
                _playerListener.enabled = false;
                if (_cameraListener != null) _cameraListener.enabled = true;
            }
        }

        private void LateUpdate()
        {
            if (_listenerAnchor == null || !_listenerAnchor.gameObject.activeInHierarchy)
            {
                if (_playerListener.enabled) RefreshListener();
                return;
            }
            var forward = _cameraListener != null ? _cameraListener.transform.forward : _listenerOwner.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            _playerListener.transform.SetPositionAndRotation(_listenerAnchor.position, Quaternion.LookRotation(forward, Vector3.up));
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            double now = Now;
            float dt = Time.unscaledDeltaTime;
            Ducking.Tick(now, dt);
            float ambienceBus = BusGain(AudioBus.Ambience);
            for (int i = 0; i < AmbienceVoices; i++)
            {
                _ambienceWeights[i] = Mathf.MoveTowards(_ambienceWeights[i], i == _ambienceTarget ? 1f : 0f, dt);
                _ambience[i].volume = _ambienceWeights[i] * _ambienceGains[i] * ambienceBus;
                if (i != _ambienceTarget && _ambienceWeights[i] <= 0f && _ambience[i].clip != null)
                { _ambience[i].Stop(); _ambience[i].clip = null; }
            }
            for (int i = 0; i < LoopVoices; i++)
                if (_loopKeys[i] != null && (now >= _loopEnds[i] || !_loops[i].isPlaying)) StopLoopAt(i);
            ApplyGains(force: false);
            if (_prefsDirty && Time.unscaledTime >= _savePrefsAt) SaveVolumes();
        }

        private void ApplyGains(bool force)
        {
            float sfx = SfxGain;
            // Per-event gain belongs to PlayOneShot's volumeScale; the source volume is the live bus gain.
            foreach (var source in _pool) if (source != null) source.volume = sfx;
            for (int i = 0; i < LocalFootstepVoices; i++) if (_footsteps[i] != null) _footsteps[i].volume = _footstepGains[i] * sfx;
            for (int i = 0; i < FootstepBudget.MaxVoices; i++) if (_otherSteps[i] != null) _otherSteps[i].volume = _otherStepGains[i] * sfx;
            for (int i = 0; i < LoopVoices; i++) if (_loops[i] != null) _loops[i].volume = _loopGains[i] * sfx;
            float master = _settings.MasterGain;
            if (force || !Mathf.Approximately(master, _appliedMaster))
            {
                AudioListener.volume = master;
                _appliedMaster = master;
            }
        }

        private AudioSource NewSource(string n)
        {
            var child = transform.Find(n);
            var go = child != null ? child.gameObject : new GameObject(n);
            if (child == null) go.transform.SetParent(transform, false);
            var s = go.GetComponent<AudioSource>();
            if (s == null) s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        // ------------------------------------------------------------------ map and clips

        public void LoadMap()
        {
            var ta = Resources.Load<TextAsset>("Audio/sfx-map");
            Map = ta != null ? SfxMap.Parse(ta.text) : new SfxMap();
            SurfaceResolver.Override(Map.VenueSurfaces);
            _banks.Clear();
            Log.Info("audio", $"sfx-map: {Map.Sfx.Count} sfx, {Map.Music.Count} music sets, {Map.Babble.Count} babble sets, " +
                              $"{Map.Footsteps.Count} footstep surfaces" + (ta == null ? " (missing: Resources/Audio/sfx-map.json)" : ""));
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

        /// <summary>True when the id (or its registry alias) has a mapped clip.</summary>
        public bool Has(string sfxId) => TryEntry(sfxId, out _);

        private bool TryEntry(string sfxId, out SfxEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(sfxId)) return false;
            if (Map.Sfx.TryGetValue(sfxId, out entry)) return true;
            var canonical = CueGate.Canonical(sfxId);
            return canonical != sfxId && Map.Sfx.TryGetValue(canonical, out entry);
        }

        private readonly Dictionary<string, BabbleBank> _banks = new Dictionary<string, BabbleBank>();

        /// <summary>
        /// Babble bank for an archetype from sfx-map.json `babble` (26 syllables a..z; audio/BABBLE.md), falling back to
        /// `default`. Null when no bank exists (Babble then synthesizes blips).
        /// </summary>
        public BabbleBank BabbleBankFor(string archetypeId)
        {
            var key = archetypeId != null && Map.Babble.ContainsKey(archetypeId) ? archetypeId : "default";
            if (_banks.TryGetValue(key, out var cached)) return cached;
            BabbleBank bank = null;
            if (Map.Babble.TryGetValue(key, out var e) && e.files.Count >= 26)
            {
                bank = new BabbleBank { archetype = key };
                for (int i = 0; i < 26; i++) bank.letters[i] = Clip(e.files[i]);
                var r = e.raw;
                bank.charsPerSecond = r.Float("chars_per_second", 16f);
                bank.pitchJitterSemitones = r.Float("pitch_jitter_semitones", 1.5f);
                if (r?["npc_pitch_range_semitones"] is Newtonsoft.Json.Linq.JArray range && range.Count == 2)
                {
                    bank.npcPitchMinSemitones = (float)range[0];
                    bank.npcPitchMaxSemitones = (float)range[1];
                }
                bank.volumeDb = r.Float("volume_db", -6f);
                bank.signatureSfx = r.Str("signature");
            }
            _banks[key] = bank;
            return bank;
        }

        // ------------------------------------------------------------------ one-shots

        /// <summary>Plays an sfx id as a 2D one-shot (loops such as `sfx_call_ring` start a timed loop instead).</summary>
        public void Play(string sfxId, float volume = 1f) => PlayInternal(sfxId, volume, null);

        /// <summary>
        /// 2D one-shot transposed by `semitones` (clamped ±12; pitch and speed together). Onboarding spec: the draw
        /// spotlight's tick rises one semitone per hop.
        /// </summary>
        public void PlayPitched(string sfxId, float semitones, float volume = 1f) =>
            PlayInternal(sfxId, volume, null, float.IsNaN(semitones) ? 0f : Mathf.Clamp(semitones, -12f, 12f));

        /// <summary>3D one-shot in the shared bounded SFX pool. Local footsteps have a dedicated pool.</summary>
        public void PlayAt(string sfxId, Vector3 position, float volume = 1f) => PlayInternal(sfxId, volume, position);

        private void PlayInternal(string sfxId, float volume, Vector3? position, float semitones = 0f)
        {
            if (!_runtimeReady) return;
            if (float.IsNaN(volume) || volume < 0f) volume = 0f;
            double now = Now;
            if (!TryEntry(sfxId, out var entry))
            {
                Log.UnknownId("sfx", sfxId);
                if (position.HasValue || !Cues.TryPass("unknown:" + sfxId, "ui", now)) return;
                var fallback = NextSource("ui");
                fallback.pitch = 1f;
                fallback.PlayOneShot(_blip, 0.4f * volume);
                return;
            }
            if (entry.loop && !position.HasValue)
            {
                // A loop id fired as a signal is a keep-alive: it rings for one more cycle after the last signal, so an
                // owner that re-publishes every cycle (CallController: every 3 s) controls its length, and a lost
                // "stop" can never leave it ringing.
                var loopClip = Clip(entry.files[0]);
                StartLoop(entry.id, entry.id, (loopClip != null ? loopClip.length : 3f) + 0.5f, volume);
                return;
            }
            if (!Cues.TryPass(entry.id, entry.category, now)) return;
            if (entry.id == SfxIds.CallConnect || entry.id == SfxIds.CallEnd) StopLoop(SfxIds.CallRing);
            if (CueGate.IsRich(entry.category)) MaskRecentToasts(now);
            var clip = Clip(entry.files[UnityEngine.Random.Range(0, entry.files.Count)]);
            if (clip == null) clip = _blip;
            var src = NextSource(entry.category);
            _poolIds[src] = entry.id;
            _poolStartedAt[src] = now;
            if (position.HasValue)
            {
                src.spatialBlend = 1f;
                src.transform.position = position.Value;
            }
            src.pitch = AudioMath.SemitonesToPitch(semitones) * (1f + UnityEngine.Random.Range(-entry.pitchJitter, entry.pitchJitter));
            src.PlayOneShot(clip, entry.volume * volume);
            // A muted or zero-gain stinger never ducks the music.
            if (entry.ducksMusic && entry.volume * volume > 0f && SfxGain > 0f)
                Ducking.HoldUntil(Ducker.StingerKey, Ducker.StingerDuckDb, now, now + clip.length);
        }

        /// <summary>A richer cue started: drop a toast tick that began in the same instant (one event, one sound).</summary>
        private void MaskRecentToasts(double now)
        {
            foreach (var s in _pool)
                if (s.isPlaying && _poolIds.TryGetValue(s, out var id) && id == SfxIds.UiToast &&
                    _poolStartedAt.TryGetValue(s, out var at) && now - at < CueGate.ToastMaskSeconds)
                    s.Stop();
        }

        private AudioSource NextSource(string category)
        {
            AudioSource source = null;
            // Two voices per class (UI, stingers): a confirmation beside a click, but rapid menus cannot fill all
            // eight voices or repeatedly cut off a longer world/stinger sound.
            if (category == "ui" || category == "stinger")
            {
                int playing = 0;
                foreach (var s in _pool)
                    if (s.isPlaying && _poolCategories.TryGetValue(s, out var playingCategory) && playingCategory == category)
                    {
                        playing++;
                        if (source == null || _poolStartOrder[s] < _poolStartOrder[source]) source = s;
                    }
                if (playing < 2) source = null;
                else source.Stop();
            }
            if (source == null)
                foreach (var s in _pool)
                    if (!s.isPlaying) { source = s; break; }
            if (source == null)
            {
                source = _pool[0];
                foreach (var s in _pool)
                    if (_poolStartOrder[s] < _poolStartOrder[source]) source = s;
                source.Stop();
            }
            _poolStartOrder[source] = ++_sfxSequence;
            _poolCategories[source] = category;
            source.spatialBlend = 0f;
            source.volume = SfxGain;
            return source;
        }

        // ------------------------------------------------------------------ loops

        /// <summary>
        /// Loop an sfx id under `key` (e.g. the call ring) until <see cref="StopLoop"/> or `maxSeconds`. Restarting the
        /// same key keeps it playing. At most two loops; a third replaces the oldest.
        /// </summary>
        public void StartLoop(string key, string sfxId, float maxSeconds = LoopMaxSeconds, float volume = 1f)
        {
            if (!_runtimeReady || string.IsNullOrEmpty(key)) return;
            if (!TryEntry(sfxId, out var entry)) { Log.UnknownId("sfx", sfxId); return; }
            double now = Now;
            float seconds = float.IsNaN(maxSeconds) || maxSeconds <= 0f ? LoopMaxSeconds : Mathf.Min(maxSeconds, 300f);
            for (int i = 0; i < LoopVoices; i++)
                if (_loopKeys[i] == key)
                {
                    _loopEnds[i] = Math.Max(_loopEnds[i], now + seconds);
                    _loopGains[i] = entry.volume * Mathf.Max(0f, volume);
                    return;
                }
            var clip = Clip(entry.files[0]);
            if (clip == null) return;
            int slot = 0;
            for (int i = 0; i < LoopVoices; i++)
            {
                if (_loopKeys[i] == null) { slot = i; break; }
                if (_loopEnds[i] < _loopEnds[slot]) slot = i;
            }
            StopLoopAt(slot);
            _loopKeys[slot] = key;
            _loopEnds[slot] = now + seconds;
            _loopGains[slot] = entry.volume * Mathf.Max(0f, volume);
            var s = _loops[slot];
            s.clip = clip;
            s.loop = true;
            s.pitch = 1f;
            s.volume = _loopGains[slot] * SfxGain;
            s.Play();
        }

        public void StopLoop(string key)
        {
            if (key == null) return;
            for (int i = 0; i < LoopVoices; i++) if (_loopKeys[i] == key) StopLoopAt(i);
        }

        public bool IsLooping(string key)
        {
            for (int i = 0; i < LoopVoices; i++) if (key != null && _loopKeys[i] == key) return true;
            return false;
        }

        private void StopLoopAt(int i)
        {
            _loopKeys[i] = null;
            _loopGains[i] = 0f;
            if (_loops[i] == null) return;
            _loops[i].Stop();
            _loops[i].clip = null;
        }

        // ------------------------------------------------------------------ ducking

        /// <summary>Duck the music 6 dB while `source` ("voice", "spaces", …) has someone talking.</summary>
        public void SetVoiceDuck(string source, bool talking)
        {
            if (talking) Ducking.Hold(source ?? Ducker.VoiceKey, Ducker.VoiceDuckDb);
            else Ducking.Clear(source ?? Ducker.VoiceKey);
        }

        /// <summary>Music gain offset in dB from every duck source (voice −6, stinger −9; deepest wins).</summary>
        public float MusicDuckDb => Ducking.CurrentDb;

        // ------------------------------------------------------------------ footsteps

        internal void NoteVenue(string venueId, bool isClub)
        {
            if (venueId != VenueId) _surfaces.Clear();
            VenueId = venueId;
            _venueIsClub = isClub;
        }

        /// <summary>Force the local player's surface (e.g. a rug zone); null returns to automatic detection.</summary>
        public void SetSurfaceOverride(string surface) => _surfaceOverride = SurfaceResolver.IsKnown(surface) ? surface : null;

        /// <summary>Surface under a world position: tagged/physics/renderer material/object name, then the venue floor.</summary>
        public string SurfaceAt(Vector3 position) =>
            _surfaces.Resolve(position, SurfaceResolver.VenueDefault(VenueId, _venueIsClub));

        /// <summary>
        /// Local player footstep. `surface` null = automatic (override, else ground under `position`). Sourced Foley,
        /// bounded voice pool, never the same variant twice in a row, ±2 dB per step.
        /// </summary>
        public void PlayFootstep(string surface, Vector3 position)
        {
            if (!_runtimeReady || SfxGain <= 0f) return;
            if (string.IsNullOrEmpty(surface)) surface = _surfaceOverride ?? SurfaceAt(position);
            var clip = FootstepClip(surface, out var entry);
            if (clip == null) return;
            int voice = _nextFootstep++ % LocalFootstepVoices;
            var source = _footsteps[voice];
            source.Stop();
            source.transform.position = position;
            source.clip = clip;
            source.pitch = 1f + UnityEngine.Random.Range(-entry.pitchJitter, entry.pitchJitter);
            _footstepGains[voice] = entry.volume * Mathf.Pow(10f, UnityEngine.Random.Range(-2f, 2f) / 20f);
            source.volume = _footstepGains[voice] * SfxGain;
            source.Play();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _reviewFootstepsPlayed++;
#endif
        }

        /// <summary>Footstep of another avatar (remote player / NPC), through the shared footstep budget.</summary>
        public void PlayOtherFootstep(int emitterId, Vector3 position, float gain = 0.8f)
        {
            if (!_runtimeReady || SfxGain <= 0f) return;
            var decision = OtherFootsteps.Request(emitterId, Vector3.Distance(ListenerPosition, position), Now);
            if (!decision.Play) return;
            var clip = FootstepClip(SurfaceAt(position), out var entry);
            if (clip == null) return;
            var source = _otherSteps[decision.Voice];
            source.Stop();
            source.transform.position = position;
            source.clip = clip;
            source.pitch = 1f + UnityEngine.Random.Range(-entry.pitchJitter, entry.pitchJitter);
            _otherStepGains[decision.Voice] = entry.volume * Mathf.Clamp01(gain) * Mathf.Pow(10f, UnityEngine.Random.Range(-2f, 2f) / 20f);
            source.volume = _otherStepGains[decision.Voice] * SfxGain;
            source.Play();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _reviewOtherStepsPlayed++;
#endif
        }

        private AudioClip FootstepClip(string surface, out SfxEntry entry)
        {
            if (!Map.Footsteps.TryGetValue(surface ?? "", out entry) && !Map.Footsteps.TryGetValue(SurfaceResolver.Concrete, out entry))
                return null;
            int count = entry.files.Count;
            int index = 0;
            if (count > 1)
            {
                bool previous = _lastFootstep.TryGetValue(entry.id, out int last);
                index = UnityEngine.Random.Range(0, previous ? count - 1 : count);
                if (previous && index >= last) index++;
            }
            _lastFootstep[entry.id] = index;
            return Clip(entry.files[index]);
        }

        // ------------------------------------------------------------------ ambience

        /// <summary>Loops a venue ambience (keys in sfx-map "ambience", usually the venueId). Null fades it out.</summary>
        public void SetAmbience(string key)
        {
            _ambienceTarget = -1;
            if (key != null && key != "club_doors") NoteVenue(key, _venueIsClub && key == VenueId);
            if (key != null && Map.Ambience.TryGetValue(key, out var entry))
            {
                var clip = Clip(entry.files[0]);
                if (clip == null) return;
                for (int i = 0; i < AmbienceVoices; i++)
                    if (_ambience[i].clip == clip) { _ambienceTarget = i; break; }
                if (_ambienceTarget < 0)
                {
                    _ambienceTarget = _ambienceWeights[0] <= _ambienceWeights[1] ? 0 : 1;
                    var source = _ambience[_ambienceTarget];
                    source.Stop();
                    source.clip = clip;
                    source.volume = 0f;
                    _ambienceWeights[_ambienceTarget] = 0f;
                    source.Play();
                }
                _ambienceGains[_ambienceTarget] = entry.volume;
            }
        }

        /// <summary>Enter a venue: ambience, music (venue → club → district) and the footstep floor in one call.</summary>
        public void SetVenue(string venueId, string districtId, bool isClub)
        {
            NoteVenue(venueId, isClub);
            SetAmbience(venueId);
            if (Music != null) Music.SetMusic(venueId, districtId, isClub);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Native review assertion; call after enable/reload, before capturing ReviewSnapshot.</summary>
        public void ReviewValidateRuntime()
        {
            if (!_runtimeReady || Instance != this || _subs == null || Map.Sfx.Count == 0)
                throw new InvalidOperationException("Audio runtime or signal bindings were not restored.");
            if (_pool.Count != SfxVoices || _playerListener == null || Music == null)
                throw new InvalidOperationException("Audio pool, listener or director was not restored.");
            var unique = new HashSet<AudioSource>(_pool);
            foreach (var source in _loops) unique.Add(source);
            foreach (var source in _ambience) unique.Add(source);
            foreach (var source in _footsteps) unique.Add(source);
            foreach (var source in _otherSteps) unique.Add(source);
            if (unique.Count != OwnedSourceCount || unique.Contains(null) || GetComponents<MusicDirector>().Length != 1)
                throw new InvalidOperationException("Audio source binding is missing or duplicated.");
            int expected = OwnedSourceCount + MusicDirector.OwnedSourceCount;
            if (GetComponentsInChildren<AudioSource>(true).Length != expected)
                throw new InvalidOperationException($"Audio source count changed across reload (expected {expected}).");
            Music.ReviewValidateRuntime();
        }

        /// <summary>On-demand device review only. No per-frame object search or log output.</summary>
        public string ReviewSnapshot()
        {
            int listeners = 0, sfxPlaying = 0, footstepsPlaying = 0, uiPlaying = 0;
            AudioListener activeListener = null;
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (listener.enabled && listener.gameObject.activeInHierarchy) { listeners++; activeListener = listener; }
            foreach (var source in _pool)
                if (source.isPlaying)
                {
                    sfxPlaying++;
                    if (_poolCategories.TryGetValue(source, out var category) && category == "ui") uiPlaying++;
                }
            foreach (var source in _footsteps) if (source.isPlaying) footstepsPlaying++;
            var r = new System.Text.StringBuilder();
            r.Append($"audioReady={_runtimeReady} audioSources={GetComponentsInChildren<AudioSource>(true).Length} ");
            r.Append($"listenerActiveCount={listeners} listener={(activeListener != null ? activeListener.name : "none")} venue={VenueId ?? "none"} ");
            r.Append(FormattableString.Invariant($"master={masterVolume:0.00} music={musicVolume:0.00} sfx={sfxVolume:0.00} voice={voiceVolume:0.00} ambience={ambienceVolume:0.00} "));
            r.Append($"sfxPlaying={sfxPlaying}/{SfxVoices} uiPlaying={uiPlaying}/2 footstepsPlayed={_reviewFootstepsPlayed} ");
            r.Append($"footstepsPlaying={footstepsPlaying}/{LocalFootstepVoices} otherSteps={_reviewOtherStepsPlayed} ");
            r.Append(FormattableString.Invariant($"duckDb={MusicDuckDb:0.0} ambienceTarget={_ambienceTarget}"));
            for (int i = 0; i < LoopVoices; i++) r.Append($" loop{i}={_loopKeys[i] ?? "none"}");
            for (int i = 0; i < AmbienceVoices; i++)
                r.Append(FormattableString.Invariant(
                    $" ambience{i}=[clip={(_ambience[i].clip != null ? _ambience[i].clip.name : "none")},weight={_ambienceWeights[i]:0.000},playing={_ambience[i].isPlaying}]"));
            r.Append(' ').Append(Music != null ? Music.ReviewSnapshot() : "music=none");
            return r.ToString();
        }
#endif
    }
}
