using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiveKit;
using UnityEngine;

namespace CTLife.Net
{
    /// <summary>Owned, cancellable Unity capture using LiveKit's public processing source; no SDK startup/resume coroutine.</summary>
    public sealed class SpacesCaptureSource : RtcAudioSource
    {
        public override event Action<float[], int, int> AudioRead;
        private readonly string _device;
        private readonly GameObject _owner;
        private AudioSource _playback;
        private SpacesCaptureProbe _probe;
        private AudioClip _clip;
        private int _attempt;
        private bool _disposed, _recording;
        public bool IsRecording => !_disposed && _recording && Microphone.IsRecording(_device);

        public SpacesCaptureSource(string device, GameObject owner, AudioProcessingOptions processing)
            : base(RtcAudioSourceType.AudioSourceMicrophone, processing) { _device = device; _owner = owner; }

        public async Task StartCapture(CancellationToken ct)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SpacesCaptureSource));
            ct.ThrowIfCancellationRequested();
            int attempt = ++_attempt;
            SetMute(true); // No outgoing samples until readiness and current role are checked by the caller.
            base.Start();
            try
            {
                if (!Application.HasUserAuthorization(UserAuthorization.Microphone)) throw new SpacesAudioException("permission");
                int rate = AudioSettings.outputSampleRate;
                if (rate <= 0) throw new SpacesAudioException("audioFailed");
                _recording = true;
                _clip = Microphone.Start(_device, true, 1, rate);
                if (_clip == null) throw new SpacesAudioException("audioFailed");
                _playback = _owner.AddComponent<AudioSource>();
                _playback.playOnAwake = false; _playback.spatialBlend = 0; _playback.loop = true; _playback.clip = _clip;
                _probe = _owner.AddComponent<SpacesCaptureProbe>();
                _probe.Bind((samples, channels, sampleRate) => AudioRead?.Invoke(samples, channels, sampleRate), rate);
                double deadline = Time.realtimeSinceStartupAsDouble + 2.5;
                while (Microphone.GetPosition(_device) <= 0)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_disposed || attempt != _attempt || !Microphone.IsRecording(_device) || Time.realtimeSinceStartupAsDouble >= deadline)
                        throw new SpacesAudioException("audioFailed");
                    await Awaitable.NextFrameAsync(ct);
                }
                ct.ThrowIfCancellationRequested();
                if (_disposed || attempt != _attempt) throw new OperationCanceledException();
                _playback.Play();
            }
            catch { Stop(); throw; }
        }

        public override void Stop()
        {
            _attempt++;
            SetMute(true);
            var probe = _probe; _probe = null;
            var playback = _playback; _playback = null;
            var clip = _clip; _clip = null;
            bool recording = _recording; _recording = false;
            var errors = new List<Exception>();
            void Release(Action operation) { try { operation(); } catch (Exception error) { errors.Add(error); } }
            if (probe != null) Release(probe.Detach);
            if (recording) Release(() => Microphone.End(_device));
            if (playback != null) Release(() => { playback.Stop(); playback.clip = null; UnityEngine.Object.Destroy(playback); });
            if (probe != null) Release(() => UnityEngine.Object.Destroy(probe));
            if (clip != null) Release(() => UnityEngine.Object.Destroy(clip));
            Release(() => base.Stop());
            if (errors.Count > 0) throw new AggregateException("Microphone cleanup failed.", errors);
        }
        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            try { if (disposing) Stop(); }
            finally { base.Dispose(disposing); }
        }
    }
}
