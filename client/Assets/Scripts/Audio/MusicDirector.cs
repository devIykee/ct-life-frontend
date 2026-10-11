using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.Audio
{
    /// <summary>
    /// Adaptive venue/district music (Build Spec "Adaptive music"). A set is either five sample-locked stems (pad,
    /// bass, drums, lead, tension) or a complete licensed composition plus an optional key/tempo-neutral tension bed.
    /// `market_mood` (−1 red … +1 green) drives <see cref="MoodController"/>: green brings in drums and lead, red
    /// low-passes the mix and adds tension; changes latch on bar lines and fade over two bars. Stems start together
    /// with PlayScheduled on one DSP tick once every clip has loaded, and a drift guard re-locks them if a device ever
    /// slips. Venue changes crossfade between two banks (1.2 s). Spaces Studio is voice only (no music).
    /// The music bus follows the Music setting and the AudioManager duck (voice −6 dB, stingers −9 dB).
    /// </summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        public static readonly string[] StemNames = { "pad", "bass", "drums", "lead", "tension" };
        public const int OwnedSourceCount = 2 * (1 + 5);
        private const float CrossfadeSeconds = 1.2f;
        private const double ScheduleLead = 0.15;
        private const double LoadTimeout = 3.0;
        private const int DriftToleranceSamples = 1024;

        [System.Serializable]
        private sealed class Bank
        {
            public readonly Dictionary<string, AudioSource> stems = new Dictionary<string, AudioSource>();
            public readonly Dictionary<string, AudioLowPassFilter> filters = new Dictionary<string, AudioLowPassFilter>();
            public AudioLowPassFilter fullMixFilter;
            public string key;
            public AudioSource fullMix;
            public string fullMixPath;
            public float gain = 1f;
            public float weight;
            public float bpm = 100f;
            public bool fullMixMode;
            public double startDsp;
            public bool pendingStart;
            public double pendingSince;
            public bool resumeAfterEnable, resumeScheduled;
        }

        private AudioManager _audio;
        [SerializeField, HideInInspector] private Bank[] _banks = { new Bank(), new Bank() };
        private readonly MoodController _mood = new MoodController();
        [System.NonSerialized] private bool _runtimeReady;
        private int _targetBank = -1;
        private string _district;
        private double _nextDriftCheck;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private int _reviewTransitions, _reviewResyncs;
#endif

        /// <summary>The requested market mood (−1..1); the audible mix follows it on the next bar line.</summary>
        public float Mood => _mood.TargetMood;
        public MoodLayers Layers => _mood.Current;
        public string CurrentKey => _district;

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
            for (int i = 0; i < _banks.Length; i++)
            {
                var bank = _banks[i] ?? (_banks[i] = new Bank());
                bank.stems.Clear();
                bank.filters.Clear();
                bank.fullMix = Source("music_" + i + "_full_mix");
                bank.fullMixFilter = Filter(bank.fullMix);
                foreach (var n in StemNames)
                {
                    var source = Source("music_" + i + "_" + n);
                    bank.stems[n] = source;
                    bank.filters[n] = Filter(source);
                }
                if (bank.resumeAfterEnable)
                {
                    // Pause/UnPause retains the native playhead. A still-scheduled bank is scheduled together again.
                    if (bank.resumeScheduled || bank.pendingStart) bank.pendingStart = true;
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
            source.priority = 0; // music is never virtualised away by one-shots
            if (restoreActive) go.SetActive(true);
            return source;
        }

        private static AudioLowPassFilter Filter(AudioSource source)
        {
            var filter = source.GetComponent<AudioLowPassFilter>();
            if (filter == null) filter = source.gameObject.AddComponent<AudioLowPassFilter>();
            filter.lowpassResonanceQ = 1f;
            return filter;
        }

        /// <summary>Market Mood bps (real SOL 24h change) → −1..1. ±5% saturates.</summary>
        public void SetMood(int bps) => _mood.SetMood(MoodMix.FromBps(bps));

        /// <summary>Market mood −1 (red) … +1 (green).</summary>
        public void SetMood(float mood) => _mood.SetMood(mood);

        /// <summary>Venue-specific set first (Perp Pit has its own), else clubs for club venues, else the district's.</summary>
        public void SetMusic(string venueId, string districtId, bool isClub = false)
        {
            if (_audio == null) return;
            _audio.NoteVenue(venueId, isClub);
            // Build Spec "Sound design", district tempo table: Spaces Studio = None / Voice only.
            // Its location in the Timeline district must not select that district's music fallback.
            if (venueId == "spaces_studio") { SetDistrict(null); return; }
            string key = null;
            if (venueId != null && _audio.Map.Music.ContainsKey(venueId)) key = venueId;
            else if (isClub && _audio.Map.Music.ContainsKey("clubs")) key = "clubs";
            else if (districtId != null && _audio.Map.Music.ContainsKey(districtId)) key = districtId;
            SetDistrict(key);
        }

        /// <summary>Select a music set by its sfx-map key (district id, venue id, "clubs", "lobby"); null stops.</summary>
        public void SetDistrict(string districtId)
        {
            if (_audio == null || districtId == _district) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _reviewTransitions++;
#endif
            _district = districtId;
            _targetBank = -1;
            if (districtId == null || !_audio.Map.Music.TryGetValue(districtId, out var set)) return;

            // Returning to a still-fading set reverses the fade without restarting it. Lobby and Timeline share one
            // full composition; entering the city keeps its musical phase.
            for (int i = 0; i < _banks.Length; i++)
                if (_banks[i].key == districtId || (set.FullMixMode && _banks[i].fullMixPath == set.fullMix && _banks[i].fullMix.clip != null))
                {
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
            bank.bpm = set.bpm > 1f ? set.bpm : 100f;
            bank.fullMixMode = set.FullMixMode;
            if (set.FullMixMode)
            {
                bank.fullMixPath = set.fullMix;
                bank.fullMix.clip = _audio.Clip(set.fullMix);
                bank.fullMix.volume = 0f;
            }
            foreach (var n in StemNames)
            {
                // Full-mix sets only take the red-market tension bed from `stems`.
                if (set.FullMixMode && n != "tension") continue;
                if (!set.stems.TryGetValue(n, out var path)) continue;
                var clip = _audio.Clip(path);
                if (clip == null) continue;
                var s = bank.stems[n];
                s.clip = clip;
                s.volume = 0f;
            }
            // Start only when every clip is decoded/ready, all on one DSP tick: disk or decode latency on one stem
            // cannot desynchronise the set.
            bank.pendingStart = true;
            bank.pendingSince = AudioSettings.dspTime;
            foreach (var s in Sources(bank)) if (s.clip != null && s.clip.loadState == AudioDataLoadState.Unloaded) s.clip.LoadAudioData();
            _mood.Restart(false);
            Log.Info("audio", $"Music: {districtId} @ {set.bpm} BPM ({(set.FullMixMode ? "full mix: " + set.fullMix : "adaptive stems")})");
        }

        private static IEnumerable<AudioSource> Sources(Bank bank)
        {
            yield return bank.fullMix;
            foreach (var s in bank.stems.Values) yield return s;
        }

        private void TryStart(Bank bank, double now)
        {
            bool ready = true;
            foreach (var s in Sources(bank))
                if (s.clip != null && s.clip.loadState == AudioDataLoadState.Loading) ready = false;
            if (!ready && now - bank.pendingSince < LoadTimeout) return;
            bank.pendingStart = false;
            bank.startDsp = now + ScheduleLead;
            foreach (var s in Sources(bank))
                if (s.clip != null && s.clip.loadState != AudioDataLoadState.Failed) s.PlayScheduled(bank.startDsp);
        }

        private void Update()
        {
            if (_audio == null) return;
            double now = AudioSettings.dspTime;
            float dt = Time.unscaledDeltaTime;
            foreach (var bank in _banks) if (bank.key != null && bank.pendingStart) TryStart(bank, now);

            var target = _targetBank >= 0 ? _banks[_targetBank] : null;
            var layers = target != null && !target.pendingStart
                ? _mood.Tick(now, target.startDsp, target.bpm, dt)
                : _mood.Tick(now, now, 120, dt);
            float bus = _audio.BusGain(AudioBus.Music) * AudioMath.DbToLinear(_audio.MusicDuckDb);
            bool targetReady = target == null || (!target.pendingStart && now >= target.startDsp);
            bool filter = layers.FilterActive;
            float cutoff = Mathf.Clamp(layers.CutoffHz, 10f, 22000f);
            for (int i = 0; i < _banks.Length; i++)
            {
                var bank = _banks[i];
                if (bank.key == null) continue;
                if (targetReady) bank.weight = Mathf.MoveTowards(bank.weight, i == _targetBank ? 1f : 0f, dt / CrossfadeSeconds);
                float g = bus * bank.gain * bank.weight;
                bank.fullMix.volume = bank.fullMixMode ? g : 0f;
                SetFilter(bank.fullMixFilter, filter, cutoff);
                foreach (var n in StemNames)
                {
                    float layer = n == "tension" ? layers.Tension : bank.fullMixMode ? 0f : layers.Gain(n);
                    bank.stems[n].volume = layer * g;
                    SetFilter(bank.filters[n], filter, cutoff);
                }
                if (i != _targetBank && bank.weight <= 0f) Clear(bank);
            }
            if (target != null && !target.fullMixMode && now >= _nextDriftCheck) { _nextDriftCheck = now + 1.0; LockStems(target, now); }
        }

        private static void SetFilter(AudioLowPassFilter f, bool active, float cutoff)
        {
            if (f == null) return;
            if (f.enabled != active) f.enabled = active;
            if (active) f.cutoffFrequency = cutoff;
        }

        /// <summary>Re-lock any stem that drifted more than ~21 ms from the first playing stem (seamless-loop guard).</summary>
        private void LockStems(Bank bank, double now)
        {
            if (now < bank.startDsp + 0.5) return;
            AudioSource reference = null;
            foreach (var n in StemNames)
            {
                var s = bank.stems[n];
                if (s.clip == null || !s.isPlaying) continue;
                if (reference == null) { reference = s; continue; }
                int length = s.clip.samples;
                if (length <= 0 || length != reference.clip.samples) continue;
                int diff = Mathf.Abs(s.timeSamples - reference.timeSamples);
                diff = Mathf.Min(diff, length - diff);
                if (diff <= DriftToleranceSamples) continue;
                s.timeSamples = reference.timeSamples;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _reviewResyncs++;
#endif
                Log.Warn("audio", $"Music stem {n} drifted {diff} samples in {bank.key}; re-locked.");
            }
        }

        private static void Clear(Bank bank)
        {
            if (bank.fullMix != null) { bank.fullMix.Stop(); bank.fullMix.clip = null; }
            bank.fullMixPath = null;
            foreach (var source in bank.stems.Values) { source.Stop(); source.clip = null; }
            bank.key = null;
            bank.weight = 0f;
            bank.pendingStart = false;
            bank.fullMixMode = false;
        }

        private void OnDisable()
        {
            foreach (var bank in _banks)
            {
                bank.resumeAfterEnable = bank.key != null;
                bank.resumeScheduled = bank.resumeAfterEnable && (bank.pendingStart || bank.startDsp > AudioSettings.dspTime);
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
            if (!_runtimeReady || _audio == null || _banks.Length != 2)
                throw new System.InvalidOperationException("Music runtime was not restored.");
            foreach (var bank in _banks)
            {
                if (bank.fullMix == null || bank.fullMixFilter == null || bank.stems.Count != StemNames.Length || bank.filters.Count != StemNames.Length)
                    throw new System.InvalidOperationException("Music bank bindings were not restored.");
                foreach (var name in StemNames)
                    if (bank.stems[name] == null || bank.filters[name] == null)
                        throw new System.InvalidOperationException("Music stem source/filter was not restored.");
            }
            if (_targetBank >= 0 && !_banks[_targetBank].pendingStart && AudioSettings.dspTime > _banks[_targetBank].startDsp + 0.5)
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
            var l = _mood.Current;
            var result = new System.Text.StringBuilder();
            result.Append($"musicActiveBanks={activeBanks} musicTarget={_district ?? "none"} musicTargetBank={_targetBank} musicTransitions={_reviewTransitions} resyncs={_reviewResyncs}");
            result.Append(System.FormattableString.Invariant(
                $" mood={_mood.TargetMood:0.00}/{_mood.AppliedMood:0.00} layers=[pad={l.Pad:0.00},bass={l.Bass:0.00},drums={l.Drums:0.00},lead={l.Lead:0.00},tension={l.Tension:0.00},lp={l.CutoffHz:0}]"));
            for (int i = 0; i < _banks.Length; i++)
            {
                var bank = _banks[i];
                int playing = 0, clips = 0;
                foreach (var source in Sources(bank)) { if (source.isPlaying) playing++; if (source.clip != null) clips++; }
                result.Append(System.FormattableString.Invariant(
                    $" music{i}=[key={bank.key ?? "none"},mode={(bank.fullMixMode ? "full_mix" : "stems")},weight={bank.weight:0.000},playing={playing},clips={clips},pending={bank.pendingStart},startIn={bank.startDsp - AudioSettings.dspTime:0.000}]"));
            }
            return result.ToString();
        }
#endif
    }
}
