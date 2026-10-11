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

        private float _sfxVolume = 1f, _musicVolume = 0.7f, _ambienceVolume = 0.6f;
        public float sfxVolume
        {
            get => _sfxVolume;
            set { SetVolume("sfx", ref _sfxVolume, value); ApplySfxVolumes(); }
        }
        public float musicVolume { get => _musicVolume; set => SetVolume("music", ref _musicVolume, value); }
        public float ambienceVolume { get => _ambienceVolume; set => SetVolume("ambience", ref _ambienceVolume, value); }

        public SfxMap Map { get; private set; } = new SfxMap();
        public MusicDirector Music { get; private set; }

        private readonly List<AudioSource> _pool = new List<AudioSource>();
        private readonly Dictionary<AudioSource, long> _poolStartOrder = new Dictionary<AudioSource, long>();
        private readonly Dictionary<AudioSource, string> _poolCategories = new Dictionary<AudioSource, string>();
        private readonly Dictionary<string, float> _lastUiPlayedAt = new Dictionary<string, float>();
        private long _sfxSequence;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly AudioSource[] _ambience = new AudioSource[2];
        // Fade state must survive hot reload; source lookup caches below are rebuilt from named children.
        [SerializeField, HideInInspector] private float[] _ambienceWeights = new float[2], _ambienceGains = new float[2];
        private int _ambienceTarget = -1;
        private readonly AudioSource[] _footsteps = new AudioSource[4];
        private readonly float[] _footstepGains = new float[4];
        private readonly Dictionary<string, int> _lastFootstep = new Dictionary<string, int>();
        private int _nextFootstep;
        private AudioListener _playerListener, _cameraListener;
        private Transform _listenerOwner, _listenerAnchor;
        private bool _prefsDirty;
        private float _savePrefsAt;
        private AudioClip _blip;
        [System.NonSerialized] private Subscriptions _subs;
        [System.NonSerialized] private bool _runtimeReady, _restoringRuntime;
        private float _stingerDuckUntil;
        private bool _voiceDuck;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private long _reviewFootstepsPlayed;
#endif

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
            _sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("ctlife.audio.sfx", 1f));
            _musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("ctlife.audio.music", 0.7f));
            _ambienceVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("ctlife.audio.ambience", 0.6f));
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
                _pool.Clear();
                _poolStartOrder.Clear();
                _poolCategories.Clear();
                if (_ambienceWeights == null || _ambienceWeights.Length != 2) _ambienceWeights = new float[2];
                if (_ambienceGains == null || _ambienceGains.Length != 2) _ambienceGains = new float[2];
                for (int i = 0; i < 8; i++)
                {
                    var source = NewSource("sfx" + i);
                    source.rolloffMode = AudioRolloffMode.Linear;
                    source.minDistance = 1.5f;
                    source.maxDistance = 12f;
                    source.dopplerLevel = 0f;
                    _pool.Add(source);
                    _poolStartOrder[source] = 0;
                }
                for (int i = 0; i < _ambience.Length; i++)
                {
                    _ambience[i] = NewSource("ambience" + i);
                    _ambience[i].loop = true;
                    _ambience[i].UnPause();
                }
                for (int i = 0; i < _footsteps.Length; i++)
                {
                    var source = _footsteps[i] = NewSource("footstep" + i);
                    source.spatialBlend = 1f;
                    source.rolloffMode = AudioRolloffMode.Linear;
                    source.minDistance = 1.5f;
                    source.maxDistance = 12f;
                    source.dopplerLevel = 0f;
                }
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
                if (_blip == null) _blip = Babble.MakeBlip(880f, 0.06f);
                Music = GetComponent<MusicDirector>();
                if (Music == null) Music = gameObject.AddComponent<MusicDirector>();
                Music.Init(this);
                ApplySfxVolumes();
                _runtimeReady = true;
                Log.Info("audio", $"Runtime restored: sfx={_pool.Count} ambience={_ambience.Length} footsteps={_footsteps.Length}");
            }
            finally { _restoringRuntime = false; }
        }

        private void BindSignals()
        {
            _subs?.Dispose();
            _subs = new Subscriptions();
            _subs.On<SfxSignal>(s => Play(s.sfxId, Mathf.Max(0f, s.volume)));
            _subs.On<VoiceActivitySignal>(v => _voiceDuck = v.anyoneTalking);
            _subs.On<MoodSignal>(m => Music.SetMood(m.bps));
            _subs.On<VenueChangedSignal>(v =>
            {
                if (v.venueId != null) return;
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
            foreach (var source in _ambience) if (source != null) source.Pause();
            _voiceDuck = false;
            _stingerDuckUntil = 0f;
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

        private void SetVolume(string key, ref float field, float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(field, value)) return;
            field = value;
            PlayerPrefs.SetFloat("ctlife.audio." + key, value);
            _prefsDirty = true;
            _savePrefsAt = Time.unscaledTime + 0.5f;
        }

        private void SaveVolumes() { if (_prefsDirty) { PlayerPrefs.Save(); _prefsDirty = false; } }
        private void OnApplicationPause(bool paused) { if (paused) SaveVolumes(); }
        private void OnApplicationQuit() => SaveVolumes();

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

        private void Update()
        {
            for (int i = 0; i < _ambience.Length; i++)
            {
                _ambienceWeights[i] = Mathf.MoveTowards(_ambienceWeights[i], i == _ambienceTarget ? 1f : 0f, Time.unscaledDeltaTime);
                _ambience[i].volume = _ambienceWeights[i] * _ambienceGains[i] * ambienceVolume;
                if (i != _ambienceTarget && _ambienceWeights[i] <= 0f && _ambience[i].clip != null)
                { _ambience[i].Stop(); _ambience[i].clip = null; }
            }
            ApplySfxVolumes();
            if (_prefsDirty && Time.unscaledTime >= _savePrefsAt) SaveVolumes();
        }

        private void ApplySfxVolumes()
        {
            // Per-event gain belongs to PlayOneShot; this master remains live for existing voices.
            foreach (var source in _pool) source.volume = sfxVolume;
            for (int i = 0; i < _footsteps.Length; i++)
                if (_footsteps[i] != null) _footsteps[i].volume = _footstepGains[i] * sfxVolume;
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

        /// <summary>
        /// Both spellings are registry ids (additions only, never renamed): the FE proposal's first names and the
        /// audio deliverable's names. Resolve to whichever has a clip.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, string> Aliases =
            new System.Collections.Generic.Dictionary<string, string>
            {
                ["sfx_ui_toggle"] = "sfx_ui_toggle_on", ["sfx_pnl_flip"] = "sfx_pnl_card_flip",
                ["sfx_sunday_bell"] = "sfx_sunday_unlock", ["sfx_room_join"] = "sfx_vr_join",
                ["sfx_room_leave"] = "sfx_vr_leave", ["sfx_raise_hand"] = "sfx_vr_raise_hand",
                ["sfx_promoted"] = "sfx_vr_promoted",
            };

        private bool TryEntry(string sfxId, out SfxEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(sfxId)) return false;
            if (Map.Sfx.TryGetValue(sfxId, out entry)) return true;
            return Aliases.TryGetValue(sfxId, out var alias) && Map.Sfx.TryGetValue(alias, out entry);
        }

        private readonly System.Collections.Generic.Dictionary<string, BabbleBank> _banks =
            new System.Collections.Generic.Dictionary<string, BabbleBank>();

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
            }
            _banks[key] = bank;
            return bank;
        }

        /// <summary>Plays an sfx.json id as a 2D one-shot.</summary>
        public void Play(string sfxId, float volume = 1f)
        {
            if (!TryEntry(sfxId, out var entry))
            {
                var fallback = NextSource();
                Log.UnknownId("sfx", sfxId);
                fallback.pitch = 1f;
                fallback.PlayOneShot(_blip, 0.4f * volume);
                return;
            }
            // Duplicate same-frame UI events should not pile up identical transients.
            // Distinct success/error/open cues remain distinct; ordinary repeated taps still work.
            if (entry.category == "ui")
            {
                if (_lastUiPlayedAt.TryGetValue(entry.id, out var playedAt) && Time.unscaledTime - playedAt < 0.06f) return;
                _lastUiPlayedAt[entry.id] = Time.unscaledTime;
            }
            var src = NextSource(entry.category);
            var clip = Clip(entry.files[Random.Range(0, entry.files.Count)]);
            if (clip == null) clip = _blip;
            src.pitch = 1f + Random.Range(-entry.pitchJitter, entry.pitchJitter);
            src.PlayOneShot(clip, entry.volume * volume);
            if (entry.category == "stinger" && entry.volume * volume > 0f && sfxVolume > 0f)
                _stingerDuckUntil = Mathf.Max(_stingerDuckUntil, Time.unscaledTime + clip.length);
        }

        /// <summary>3D one-shot in the shared bounded SFX pool. Local footsteps have a dedicated pool.</summary>
        public void PlayAt(string sfxId, Vector3 position, float volume = 1f)
        {
            if (!TryEntry(sfxId, out var entry)) { Log.UnknownId("sfx", sfxId); return; }
            var clip = Clip(entry.files[Random.Range(0, entry.files.Count)]);
            if (clip == null) return;
            var source = NextSource(entry.category);
            source.spatialBlend = 1f;
            source.transform.position = position;
            source.pitch = 1f + Random.Range(-entry.pitchJitter, entry.pitchJitter);
            source.PlayOneShot(clip, entry.volume * volume);
        }

        /// <summary>Existing sourced Foley, with a bounded voice pool and no immediate variant repetition.</summary>
        public void PlayFootstep(string surface, Vector3 position)
        {
            if (!Map.Footsteps.TryGetValue(surface, out var entry) && !Map.Footsteps.TryGetValue("concrete", out entry)) return;
            int count = entry.files.Count;
            int index = 0;
            if (count > 1)
            {
                bool previous = _lastFootstep.TryGetValue(entry.id, out int last);
                index = Random.Range(0, previous ? count - 1 : count);
                if (previous && index >= last) index++;
            }
            _lastFootstep[entry.id] = index;
            var clip = Clip(entry.files[index]);
            if (clip == null || sfxVolume <= 0f) return;
            int voice = _nextFootstep++ % _footsteps.Length;
            var source = _footsteps[voice];
            source.Stop();
            source.transform.position = position;
            source.clip = clip;
            source.pitch = 1f + Random.Range(-entry.pitchJitter, entry.pitchJitter);
            _footstepGains[voice] = entry.volume * Mathf.Pow(10f, Random.Range(-2f, 2f) / 20f);
            source.volume = _footstepGains[voice] * sfxVolume;
            source.Play();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _reviewFootstepsPlayed++;
#endif
        }

        /// <summary>Loops a venue ambience (keys in sfx-map "ambience", usually the venueId).</summary>
        public void SetAmbience(string key)
        {
            _ambienceTarget = -1;
            if (key != null && Map.Ambience.TryGetValue(key, out var entry))
            {
                var clip = Clip(entry.files[0]);
                if (clip == null) return;
                for (int i = 0; i < _ambience.Length; i++)
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

        private AudioSource NextSource(string category = null)
        {
            AudioSource source = null;
            // Two UI voices allow a confirmation beside a click, but rapid menus cannot fill all eight
            // voices or repeatedly cut off a longer world/stinger sound.
            if (category == "ui")
            {
                int playingUi = 0;
                foreach (var s in _pool)
                    if (s.isPlaying && _poolCategories.TryGetValue(s, out var playingCategory) && playingCategory == "ui")
                    {
                        playingUi++;
                        if (source == null || _poolStartOrder[s] < _poolStartOrder[source]) source = s;
                    }
                if (playingUi < 2) source = null;
                else source.Stop();
            }
            foreach (var s in _pool)
                if (source == null && !s.isPlaying) { source = s; break; }
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
            source.volume = sfxVolume;
            return source;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Native review assertion; call after enable/reload, before capturing ReviewSnapshot.</summary>
        public void ReviewValidateRuntime()
        {
            if (!_runtimeReady || Instance != this || _subs == null || Map.Sfx.Count == 0)
                throw new System.InvalidOperationException("Audio runtime or signal bindings were not restored.");
            if (_pool.Count != 8 || _playerListener == null || Music == null)
                throw new System.InvalidOperationException("Audio pool, listener or director was not restored.");
            var unique = new HashSet<AudioSource>(_pool);
            foreach (var source in _ambience) unique.Add(source);
            foreach (var source in _footsteps) unique.Add(source);
            if (unique.Count != 14 || unique.Contains(null) || GetComponents<MusicDirector>().Length != 1)
                throw new System.InvalidOperationException("Audio source binding is missing or duplicated.");
            if (GetComponentsInChildren<AudioSource>(true).Length != 26)
                throw new System.InvalidOperationException("Audio source count changed across reload (expected 26).");
            Music.ReviewValidateRuntime();
        }

        /// <summary>On-demand device review only. No per-frame object search or log output.</summary>
        public string ReviewSnapshot()
        {
            int listeners = 0, sfxPlaying = 0, footstepsPlaying = 0, uiPlaying = 0;
            AudioListener activeListener = null;
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (listener.enabled && listener.gameObject.activeInHierarchy) { listeners++; activeListener = listener; }
            float maxSfxSourceGain = 0f;
            foreach (var source in _pool)
                if (source.isPlaying)
                {
                    sfxPlaying++; maxSfxSourceGain = Mathf.Max(maxSfxSourceGain, source.volume);
                    if (_poolCategories.TryGetValue(source, out var category) && category == "ui") uiPlaying++;
                }
            foreach (var source in _footsteps) if (source.isPlaying) footstepsPlaying++;
            float anchorError = activeListener != null && _listenerAnchor != null
                ? Vector3.Distance(activeListener.transform.position, _listenerAnchor.position) : -1f;
            var result = new System.Text.StringBuilder();
            result.Append($"audioReady={_runtimeReady} audioSources={GetComponentsInChildren<AudioSource>(true).Length} sfxPoolCount={_pool.Count} ");
            result.Append($"listenerActiveCount={listeners} listener={(activeListener != null ? activeListener.name : "none")}");
            result.Append($" listenerPosition={ReviewPosition(activeListener != null ? activeListener.transform : null)}");
            result.Append($" playerPosition={ReviewPosition(_listenerOwner)} anchorPosition={ReviewPosition(_listenerAnchor)}");
            result.Append(System.FormattableString.Invariant($" listenerAnchorError={anchorError:0.000} sfxVolume={sfxVolume:0.000}"));
            result.Append(System.FormattableString.Invariant($" sfxPlaying={sfxPlaying}/8 maxPlayingSfxSourceGain={maxSfxSourceGain:0.000}"));
            result.Append($" uiPlaying={uiPlaying}/2");
            result.Append($" footstepsPlayed={_reviewFootstepsPlayed} footstepsPlaying={footstepsPlaying}/4");
            result.Append(System.FormattableString.Invariant($" musicVolume={musicVolume:0.000} duckDb={MusicDuckDb:0.0} ambienceTarget={_ambienceTarget}"));
            for (int i = 0; i < _ambience.Length; i++)
                result.Append(System.FormattableString.Invariant(
                    $" ambience{i}=[clip={(_ambience[i].clip != null ? _ambience[i].clip.name : "none")},weight={_ambienceWeights[i]:0.000},playing={_ambience[i].isPlaying}]"));
            result.Append(" ").Append(Music != null ? Music.ReviewSnapshot() : "music=none");
            return result.ToString();
        }

        private static string ReviewPosition(Transform value) => value == null ? "none" :
            System.FormattableString.Invariant($"({value.position.x:0.00},{value.position.y:0.00},{value.position.z:0.00})");
#endif

        /// <summary>Music gain in dB: −6 dB while someone nearby talks, −9 dB during a stinger.</summary>
        public float MusicDuckDb => (sfxVolume > 0f && Time.unscaledTime < _stingerDuckUntil ? -9f : 0f) + (_voiceDuck ? -6f : 0f);
    }
}
