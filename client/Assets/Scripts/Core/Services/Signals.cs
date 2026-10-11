using System.Collections.Generic;

namespace CTLife.Core
{
    // Bus messages that do not depend on generated contract types. Realtime payloads are published by
    // CTLife.Net as the generated CTLife.Realtime.* message classes directly.

    /// <summary>Show a toast. Either a toasts.json id with params, or literal text.</summary>
    public struct ToastSignal
    {
        public string textId;
        public Dictionary<string, string> @params;
        public string literal;
        /// <summary>Optional supporting copy supplied by the caller; never inferred from a receipt or transaction.</summary>
        public string detail;
        public ToastKind kind;

        public static ToastSignal Text(string text, ToastKind kind = ToastKind.Info, string detail = null) => new ToastSignal { literal = text, kind = kind, detail = detail };
        public static ToastSignal Id(string id, Dictionary<string, string> p, ToastKind kind = ToastKind.Info) => new ToastSignal { textId = id, @params = p, kind = kind };
    }

    public enum ToastKind { Info, Good, Bad, Event, Warning }

    /// <summary>Play a sound by sfx.json id (FE-owned registry).</summary>
    public struct SfxSignal
    {
        public string sfxId;
        public float volume;

        public SfxSignal(string id, float volume = 1f)
        {
            sfxId = id;
            this.volume = volume;
        }
    }

    /// <summary>Cash, needs or profile changed (after a REST call or a realtime confirmation).</summary>
    public struct ProfileChangedSignal { }

    /// <summary>Market Mood in basis points of the real SOL 24h change (city room).</summary>
    public struct MoodSignal
    {
        public int bps;
    }

    public struct OnlineCountSignal
    {
        public int online;
    }

    /// <summary>Voice activity near the listener; the music bus ducks 6 dB while true.</summary>
    public struct VoiceActivitySignal
    {
        public bool anyoneTalking;
        /// <summary>"voice" (proximity, default when null), "spaces", "call". Audio ducks per source; deepest wins.</summary>
        public string source;
    }

    /// <summary>Linear voice playback gain (Settings → Voices, after taper). LiveKit playback owners apply it to their AudioSources.</summary>
    public struct VoiceVolumeSignal
    {
        public float gain;
    }

    /// <summary>The thermal state changed (Adaptive Performance). Level: 0 none, 1 imminent, 2 throttling.</summary>
    public struct ThermalSignal
    {
        public int level;
    }

    /// <summary>
    /// The backend answered SERVER_BUSY (with Retry-After). While active, the UI shows "City is busy, retrying in Ns";
    /// the caller retries automatically and publishes active=false once a request goes through.
    /// </summary>
    public struct ServerBusySignal
    {
        public bool active;
        public float retryInSeconds;
        public int attempt;
        public string source;
    }

    /// <summary>GET /config `degraded` changed; UI re-evaluates which features to disable.</summary>
    public struct DegradedChangedSignal { }

    /// <summary>A world object asks the UI to show a speech bubble.</summary>
    public struct BubbleSignal
    {
        public UnityEngine.Transform anchor;
        public string text;
        public bool isNpc;
        public float seconds;
    }

    /// <summary>The user tapped an avatar; UI opens the profile card.</summary>
    public struct AvatarTappedSignal
    {
        public string userId;
        public string npcId;
        public string archetypeId;
        public string displayName;
    }

    /// <summary>The current venue changed (null when on the city map).</summary>
    public struct VenueChangedSignal
    {
        public string venueId;
        public string instanceId;
    }

    /// <summary>A player was blocked (POST /blocks/{userId} succeeded).</summary>
    public struct UserBlockedSignal { public string userId; }
}
