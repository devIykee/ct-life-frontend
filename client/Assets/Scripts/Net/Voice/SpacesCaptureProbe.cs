using System;
using UnityEngine;

namespace CTLife.Net
{
    /// <summary>Audio-thread handoff. Detach waits for an in-flight callback before its native source is disposed.</summary>
    public sealed class SpacesCaptureProbe : MonoBehaviour
    {
        private readonly object _gate = new object();
        private Action<float[], int, int> _read;
        private int _sampleRate;
        public void Bind(Action<float[], int, int> read, int sampleRate)
        { lock (_gate) { _read = read; _sampleRate = sampleRate; } }
        public void Detach() { lock (_gate) { _read = null; } }
        private void OnAudioFilterRead(float[] data, int channels)
        {
            lock (_gate)
            {
                try { _read?.Invoke(data, channels, _sampleRate); }
                finally { Array.Clear(data, 0, data.Length); } // Never play the player's own microphone locally.
            }
        }
        private void OnDestroy() => Detach();
    }
}
