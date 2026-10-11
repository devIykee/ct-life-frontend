using System;
using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.WorldLife
{
    /// <summary>
    /// Published on the Bus when a city event becomes a world moment in the current venue (audio lane: stingers and
    /// positional loops; any lane: reactions). The UI incident card (UI/Overlays/IncidentPresentation) already plays
    /// `StingerSfxId` today through SfxSignal when its card shows; WorldLife does not play the global stinger, so the
    /// two never double up. Positional effect sounds are played by WorldLife unless an audio hook claims them.
    /// </summary>
    public struct WorldMomentSignal
    {
        public string MomentId;
        public string EventId;
        public string VenueId;
        public string StingerSfxId;
        /// <summary>siren | alarm | carpet_yank | sniper_swarm | furnace_flash | null</summary>
        public string VenueFx;
        public Vector3 Position;
        public string CrierText;
        public double At;
    }

    /// <summary>Market Mood band changed (with hysteresis). Normalized −1..+1 matches MusicDirector.SetMood.</summary>
    public struct MarketMoodSignal
    {
        public MoodBand Band;
        public int Bps;
        public float Normalized;
        public Color Tint;
        public float Strength;
    }

    /// <summary>
    /// Implemented by the venue/lighting side to tint a venue by Market Mood (spec: red Mood low-passes the music and
    /// darkens the city). WorldLife calls every registered target on band changes and on venue entry.
    /// </summary>
    public interface IMarketMoodTarget
    {
        void ApplyMood(MoodBand band, float normalized, Color tint, float strength);
    }

    /// <summary>Optional audio-lane hook: return true to take over a positional world sound (siren/alarm loops).</summary>
    public interface IWorldMomentAudio
    {
        bool PlayAt(string momentId, string venueFx, IReadOnlyList<string> sfxCandidates, Vector3 position, float seconds);
    }

    public static class WorldLifeHooks
    {
        private static readonly List<IMarketMoodTarget> _moodTargets = new List<IMarketMoodTarget>();
        public static IWorldMomentAudio Audio { get; set; }
        internal static MarketMoodSignal? LastMood;

        public static IReadOnlyList<IMarketMoodTarget> MoodTargets => _moodTargets;

        /// <summary>Registers a mood target; it is called immediately with the current mood when one is known.</summary>
        public static Action RegisterMoodTarget(IMarketMoodTarget target)
        {
            if (target == null || _moodTargets.Contains(target)) return () => { };
            _moodTargets.Add(target);
            if (LastMood is MarketMoodSignal m) Notify(target, m);
            return () => _moodTargets.Remove(target);
        }

        internal static void PublishMood(MarketMoodSignal m)
        {
            LastMood = m;
            foreach (var t in _moodTargets.ToArray()) Notify(t, m);
            Bus.Publish(m);
        }

        private static void Notify(IMarketMoodTarget t, MarketMoodSignal m)
        {
            try { t.ApplyMood(m.Band, m.Normalized, m.Tint, m.Strength); }
            catch (Exception e) { Log.Exception("world-life", e); }
        }
    }
}
