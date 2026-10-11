using UnityEngine;

namespace CTLife.Audio
{
    // Stable public API of CTLife.Audio (documented in audio/RUNTIME_API.md). Every call is safe before the
    // AudioManager exists, after it is destroyed, and with unknown ids: it does nothing (unknown ids log once).
    // Assemblies that do not reference CTLife.Audio publish CTLife.Core signals instead (SfxSignal, MoodSignal,
    // VoiceActivitySignal, VenueChangedSignal); both paths end in the same AudioManager calls.

    /// <summary>One-shots, 3D one-shots and timed loops by sfx id (<see cref="SfxIds"/>).</summary>
    public static class Sfx
    {
        public static void Play(string id, float volume = 1f) => AudioManager.Instance?.Play(id, volume);

        public static void PlayAt(string id, Vector3 position, float volume = 1f) => AudioManager.Instance?.PlayAt(id, position, volume);

        /// <summary>One-shot transposed by `semitones` (±12): e.g. the draw spotlight tick, +1 semitone per hop (cap +6).</summary>
        public static void PlayPitched(string id, float semitones, float volume = 1f) => AudioManager.Instance?.PlayPitched(id, semitones, volume);

        /// <summary>Loop `id` under `key` until <see cref="StopLoop"/> or `maxSeconds` (call ring: key = SfxIds.CallRing).</summary>
        public static void StartLoop(string key, string id, float maxSeconds = AudioManager.LoopMaxSeconds) =>
            AudioManager.Instance?.StartLoop(key, id, maxSeconds);

        public static void StopLoop(string key) => AudioManager.Instance?.StopLoop(key);

        public static bool Has(string id) => AudioManager.Instance != null && AudioManager.Instance.Has(id);

        /// <summary>The local player's handle (without @), so `@handle` in chat or DMs plays the mention cue.</summary>
        public static void SetLocalHandle(string handle)
        {
            var audio = AudioManager.Instance;
            if (audio != null) audio.LocalHandle = string.IsNullOrEmpty(handle) ? null : handle.TrimStart('@');
        }
    }

    /// <summary>Adaptive music: venue/district selection and market mood.</summary>
    public static class Music
    {
        /// <summary>Music keys for screens outside venues. Title = CT Life main theme (`mus_title`, ignores market mood).</summary>
        public const string Title = "title", Lobby = "lobby";

        /// <summary>Title screen and onboarding (sign-in, origin reveal, creator): the main theme. Idempotent.</summary>
        public static void PlayTitle() => SetDistrict(Title);

        /// <summary>Entering a venue: ambience + music (venue set → clubs → district) + footstep floor default.</summary>
        public static void SetVenue(string venueId, string districtId, bool isClub = false) =>
            AudioManager.Instance?.SetVenue(venueId, districtId, isClub);

        /// <summary>Select a set by sfx-map key ("title", "lobby", a district id, a venue id, "clubs") or its id ("mus_title");
        /// null fades music out. Selecting the playing set again does nothing (no restart).</summary>
        public static void SetDistrict(string key) => AudioManager.Instance?.Music?.SetDistrict(key);

        /// <summary>market_mood −1 (red) … +1 (green). Usually driven by the city room's MoodSignal automatically.</summary>
        public static void SetMood(float mood) => AudioManager.Instance?.Music?.SetMood(mood);

        public static void SetMoodBps(int bps) => AudioManager.Instance?.Music?.SetMood(bps);

        public static void Stop()
        {
            var audio = AudioManager.Instance;
            if (audio == null) return;
            audio.Music?.SetDistrict(null);
            audio.SetAmbience(null);
        }
    }

    /// <summary>Footstep surfaces and other-avatar footsteps.</summary>
    public static class Footsteps
    {
        /// <summary>Force the local player's surface (e.g. a rug zone trigger); null returns to automatic detection.</summary>
        public static void SetSurface(string surface) => AudioManager.Instance?.SetSurfaceOverride(surface);

        /// <summary>Surface under a world position (tag → physics material → material → object name → venue floor).</summary>
        public static string SurfaceAt(Vector3 position) =>
            AudioManager.Instance != null ? AudioManager.Instance.SurfaceAt(position) : SurfaceResolver.Concrete;

        /// <summary>Give a remote player or NPC budgeted footsteps (idempotent). Not for the local player.</summary>
        public static FootstepEmitter Attach(GameObject avatar, float gain = 0.8f)
        {
            if (avatar == null) return null;
            var e = avatar.GetComponent<FootstepEmitter>();
            if (e == null) e = avatar.AddComponent<FootstepEmitter>();
            e.gain = Mathf.Clamp01(gain);
            return e;
        }
    }

    /// <summary>Music ducking holds (voice −6 dB is automatic from VoiceActivitySignal).</summary>
    public static class Ducking
    {
        /// <summary>Duck the music while `key` is held: e.g. ("spaces", −6) while a Space speaker talks, ("call", −12) in a call.</summary>
        public static void Hold(string key, float db) => AudioManager.Instance?.Ducking.Hold(key, db);

        public static void Release(string key) => AudioManager.Instance?.Ducking.Clear(key);

        /// <summary>Speech from one source ("voice" proximity, "spaces" stage): −6 dB while anyone talks.</summary>
        public static void SetVoiceActive(string source, bool talking) => AudioManager.Instance?.SetVoiceDuck(source, talking);
    }
}
