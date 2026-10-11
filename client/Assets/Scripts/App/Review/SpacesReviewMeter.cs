#if UNITY_EDITOR && !CTLIFE_PROD
using System;
using System.Threading;
using UnityEngine;

namespace CTLife.App.Review
{
    /// <summary>Counts only synthetic received PCM and clears its output. Never stores audio.</summary>
    public sealed class SpacesReviewMeter : MonoBehaviour
    {
        private long _callbacks, _nonzero;
        public long Callbacks => Interlocked.Read(ref _callbacks);
        public long NonzeroSamples => Interlocked.Read(ref _nonzero);
        private void OnAudioFilterRead(float[] data, int channels)
        {
            long count = 0;
            for (var i = 0; i < data.Length; i++) if (Math.Abs(data[i]) > .0001f) count++;
            Interlocked.Increment(ref _callbacks); Interlocked.Add(ref _nonzero, count);
            Array.Clear(data, 0, data.Length);
        }
    }
}
#endif
