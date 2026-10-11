using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.Audio
{
    /// <summary>
    /// Adaptive district music (Build Spec "Adaptive music"): four stems (pad, bass, drums, lead) plus an optional
    /// tension layer. `market_mood` runs −1 (red) to +1 (green): green brings in drums and lead, red low-passes the
    /// mix and adds tension. Stems start sample-aligned with PlayScheduled. Licensed full compositions use a
    /// separate single-source mode: no pretend stems and no mood low-pass removing their bright arrangement.
    /// </summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        private static readonly string[] StemNames = { "pad", "bass", "drums", "lead", "tension" };

        private AudioManager _audio;
        private const float CrossfadeSeconds = 1.2f;
        [System.Serializable]
        private sealed class Bank
        {
            public readonly Dictionary<string, AudioSource> stems = new Dictionary<string, AudioSource>();
            public readonly Dictionary<string, AudioLowPassFilter> filters = new Dictionary<string, AudioLowPassFilter>();
            public string key;
            public AudioSource fullMix;
            public string fullMixPath;
            public float gain = 1f;
            public float weight;
            public double startDsp;
            public bool resumeAfterEnable, resumeScheduled;
        }
        [SerializeField, HideInInspector] private Bank[] _banks = { new Bank(), new Bank() };
        private readonly Dictionary<string, float> _stemGains = new Dictionary<string, float>();
        [System.NonSerialized] private bool _runtimeReady;
        private int _targetBank = -1;
        private string _district;
        private float _mood;      // −1..1
        private float _smoothedMood;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private int _reviewTransitions;
#endif

        public float Mood => _mood;

        private void OnEnable()
        {
            var audio = GetComponent<AudioManager>();
            if (audio == null || !audio.isActiveAndEnabled) return;
            // This may run before AudioManager.OnEnable; the owner restores its map and both components once.
            audio.RestoreRuntime();
            Init(audio);
        }

        public void Init(AudioManager audio)
        {
            if (_runtimeReady) return;
            _audio = audio;
            if (_banks == null || _banks.Length != 2) _banks = new[] { new Bank(), new Bank() };
            foreach (var n in StemNames) _stemGains[n] = n == "pad" || n == "bass" ? 1f : 0f;
            for (int i = 0; i < _banks.Length; i++)
            {
                var bank = _banks[i] ?? (_banks[i] = new Bank());
                bank.stems.Clear();
                bank.filters.Clear();
                bank.fullMix = Source("music_" + i + "_full_mix");
                foreach (var n in StemNames)
                {
                    var source = Source("music_" + i + "_" + n);
                    bank.stems[n] = source;
                    var filter = source.GetComponent<AudioLowPassFilter>();
                    if (filter == null) filter = source.gameObject.AddComponent<AudioLowPassFilter>();
                    bank.filters[n] = filter;
                }
                if (bank.resumeAfterEnable)
                {
                    // Pause/UnPause retains the native playhead. A still-scheduled bank is scheduled together again.
                    if (bank.resumeScheduled)
                    {
                        bank.startDsp = AudioSettings.dspTime + 0.2;
                        if (bank.fullMix.clip != null) bank.fullMix.PlayScheduled(bank.startDsp);
                        foreach (var source in bank.stems.Values) if (source.clip != null) source.PlayScheduled(bank.startDsp);
                    }
                    else
                    {
                        if (bank.fullMix.clip != null) bank.fullMix.UnPause();
                        foreach (var source in bank.stems.Values) if (source.clip != null) source.UnPause();
                    }
                    bank.resumeAfterEnable = false;
                    bank.resumeScheduled = false;
                }
            }
            _runtimeReady = true;
        }

        private AudioSource Source(string name)
        {
            var child = transform.Find(name);
            var go = child != null ? child.gameObject : new GameObject(name);
            if (child == null) go.transform.SetParent(transform, false);
            var source = go.GetComponent<AudioSource>();
            // Configure a new source before its first activation. AudioSource defaults to
            // playOnAwake=true; adding it to an active object can queue empty playback.
            // Never toggle an existing source: its native playhead survives domain reload.
            bool restoreActive = source == null && go.activeSelf;
            if (restoreActive) go.SetActive(false);
            if (source == null) source = go.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            if (restoreActive) go.SetActive(true);
            return source;
        }

        /// <summary>Market Mood bps (real SOL 24h change) → −1..1. ±5% saturates.</summary>
        public void SetMood(int bps) => _mood = Mathf.Clamp(bps / 500f, -1f, 1f);

        /// <summary>Venue-specific set first (audio: Perp Pit has its own), else the district's, else clubs for club venues.</summary>
        public void SetMusic(string venueId, string districtId, bool isClub = false)
        {
            // Build Spec "Sound design", district tempo table: Spaces Studio = None / Voice only.
            // Its location in the Timeline district must not select that district's music fallback.
            if (venueId == "spaces_studio") { SetDistrict(null); return; }
            string key = null;
            if (venueId != null && _audio.Map.Music.ContainsKey(venueId)) key = venueId;
            else if (isClub && _audio.Map.Music.ContainsKey("clubs")) key = "clubs";
            else if (districtId != null && _audio.Map.Music.ContainsKey(districtId)) key = districtId;
            SetDistrict(key);
        }

        public void SetDistrict(string districtId)
        {
            if (districtId == _district) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _reviewTransitions++;
#endif
            _district = districtId;
            _targetBank = -1;
            if (districtId == null || !_audio.Map.Music.TryGetValue(districtId, out var set)) return;

            // Returning to a still-fading set reverses the fade without restarting its stems.
            for (int i = 0; i < _banks.Length; i++)
                if (_banks[i].key == districtId || (!string.IsNullOrEmpty(set.fullMix) &&
                    _banks[i].fullMixPath == set.fullMix && _banks[i].fullMix.clip != null))
                {
                    // Lobby and Timeline share one full composition; entering the city keeps its musical phase.
                    _banks[i].key = districtId;
                    _banks[i].gain = set.volume;
                    _targetBank = i;
                    return;
                }
            _targetBank = _banks[0].weight <= _banks[1].weight ? 0 : 1;
            var bank = _banks[_targetBank];
            Clear(bank);
            bank.key = districtId;
            bank.gain = set.volume;
            if (!string.IsNullOrEmpty(set.fullMix))
            {
                bank.fullMixPath = set.fullMix;
                bank.fullMix.clip = _audio.Clip(set.fullMix);
                bank.fullMix.volume = 0f;
            }
            else
            {
                foreach (var n in StemNames)
                {
                    if (!set.stems.TryGetValue(n, out var path)) continue;
                    var clip = _audio.Clip(path);
                    if (clip == null) continue;
                    var s = bank.stems[n];
                    s.clip = clip;
                    s.volume = 0f;
                }
            }
            // Load every clip before picking the shared DSP start; disk loads cannot desynchronise stems.
            bank.startDsp = AudioSettings.dspTime + 0.2;
            if (bank.fullMix.clip != null) bank.fullMix.PlayScheduled(bank.startDsp);
            foreach (var s in bank.stems.Values) if (s.clip != null) s.PlayScheduled(bank.startDsp);
            Log.Info("audio", $"Music: {districtId} @ {set.bpm} BPM ({(string.IsNullOrEmpty(set.fullMix) ? "adaptive stems" : "full mix: " + set.fullMix)})");
        }

        private void Update()
        {
            _smoothedMood = Mathf.MoveTowards(_smoothedMood, _mood, Time.unscaledDeltaTime * 0.25f);
            float m = _smoothedMood;
            float master = _audio.musicVolume * Mathf.Pow(10f, _audio.MusicDuckDb / 20f);

            SetStemGain("pad", 1f);
            SetStemGain("bass", 1f);
            SetStemGain("drums", Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.1f, 0.3f, m)));
            SetStemGain("lead", Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, m)));
            SetStemGain("tension", Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, -0.6f, m)));

            // Red low-passes the mix: 22 kHz at mood ≥ 0 down to 1.2 kHz at −1.
            float cutoff = m >= 0f ? 22000f : Mathf.Lerp(22000f, 1200f, Mathf.Pow(-m, 0.7f));
            bool targetReady = _targetBank < 0 || AudioSettings.dspTime >= _banks[_targetBank].startDsp;
            for (int i = 0; i < _banks.Length; i++)
            {
                var bank = _banks[i];
                if (bank.key == null) continue;
                if (targetReady) bank.weight = Mathf.MoveTowards(bank.weight, i == _targetBank ? 1f : 0f, Time.unscaledDeltaTime / CrossfadeSeconds);
                bank.fullMix.volume = master * bank.gain * bank.weight;
                foreach (var n in StemNames)
                {
                    bank.stems[n].volume = _stemGains[n] * master * bank.gain * bank.weight;
                    bank.filters[n].cutoffFrequency = cutoff;
                }
                if (i != _targetBank && bank.weight <= 0f) Clear(bank);
            }
        }

        private void SetStemGain(string n, float target) =>
            _stemGains[n] = Mathf.MoveTowards(_stemGains[n], target, Time.unscaledDeltaTime * 0.5f);

        private static void Clear(Bank bank)
        {
            if (bank.fullMix != null) { bank.fullMix.Stop(); bank.fullMix.clip = null; }
            bank.fullMixPath = null;
            foreach (var source in bank.stems.Values) { source.Stop(); source.clip = null; }
            bank.key = null;
            bank.weight = 0f;
        }

        private void OnDisable()
        {
            foreach (var bank in _banks)
            {
                bank.resumeAfterEnable = bank.key != null;
                bank.resumeScheduled = bank.resumeAfterEnable && bank.startDsp > AudioSettings.dspTime;
                if (bank.fullMix != null) bank.fullMix.Pause();
                foreach (var source in bank.stems.Values) if (source != null) source.Pause();
            }
            // Keep district, clip references, bank weights and playheads for OnEnable; explicit SetDistrict(null)
            // remains the path for leaving a venue or stopping music.
            _runtimeReady = false;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void ReviewValidateRuntime()
        {
            if (!_runtimeReady || _audio == null || _banks.Length != 2 || _stemGains.Count != StemNames.Length)
                throw new System.InvalidOperationException("Music runtime was not restored.");
            foreach (var bank in _banks)
            {
                if (bank.fullMix == null || bank.stems.Count != StemNames.Length || bank.filters.Count != StemNames.Length)
                    throw new System.InvalidOperationException("Music bank bindings were not restored.");
                foreach (var name in StemNames)
                    if (bank.stems[name] == null || bank.filters[name] == null)
                        throw new System.InvalidOperationException("Music stem source/filter was not restored.");
            }
            if (_targetBank >= 0 && AudioSettings.dspTime > _banks[_targetBank].startDsp + 0.5)
            {
                var target = _banks[_targetBank];
                if (target.fullMix.clip != null && !target.fullMix.isPlaying)
                    throw new System.InvalidOperationException("Assigned full composition did not resume.");
                foreach (var source in target.stems.Values)
                    if (source.clip != null && !source.isPlaying)
                        throw new System.InvalidOperationException("Assigned music stem did not resume.");
            }
        }

        public string ReviewSnapshot()
        {
            int activeBanks = 0;
            foreach (var bank in _banks) if (bank.key != null) activeBanks++;
            var result = new System.Text.StringBuilder();
            result.Append($"musicReady={_runtimeReady} musicActiveBanks={activeBanks} musicTarget={_district ?? "none"} musicTargetBank={_targetBank} musicTransitions={_reviewTransitions}");
            for (int i = 0; i < _banks.Length; i++)
            {
                var bank = _banks[i];
                int playing = 0, clips = 0;
                if (bank.fullMix.isPlaying) playing++;
                if (bank.fullMix.clip != null) clips++;
                foreach (var source in bank.stems.Values)
                { if (source.isPlaying) playing++; if (source.clip != null) clips++; }
                result.Append(System.FormattableString.Invariant(
                    $" music{i}=[key={bank.key ?? "none"},mode={(bank.fullMix.clip != null ? "full_mix" : "stems")},weight={bank.weight:0.000},playing={playing},clips={clips},fullMixGain={bank.fullMix.volume:0.000},startIn={bank.startDsp - AudioSettings.dspTime:0.000}]"));
            }
            return result.ToString();
        }
#endif
    }
}
