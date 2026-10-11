using System;
using CTLife.Core;
using UnityEngine;

namespace CTLife.Net
{
    /// <summary>
    /// The current voice playback gain for LiveKit AudioSources (proximity voice, Spaces). The audio lane publishes
    /// <see cref="VoiceVolumeSignal"/> from Settings; until then the persisted setting (squared taper) is used.
    /// </summary>
    public static class VoiceVolume
    {
        private static float? _gain;
        private static Subscriptions _subs;
        public static event Action<float> Changed;

        public static float Gain
        {
            get
            {
                Ensure();
                if (_gain == null)
                {
                    float v = Mathf.Clamp01(PlayerPrefs.GetFloat("ctlife.audio.voice", 1f));
                    _gain = v * v;
                }
                return _gain.Value;
            }
        }

        private static void Ensure()
        {
            if (_subs != null) return;
            _subs = new Subscriptions();
            _subs.On<VoiceVolumeSignal>(s =>
            {
                _gain = Mathf.Clamp01(s.gain);
                Changed?.Invoke(_gain.Value);
            });
        }
    }
}
