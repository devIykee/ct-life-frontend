using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Core;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;

namespace CTLife.Net
{
    /// <summary>
    /// Proximity voice on LiveKit Unity SDK 2.1.0 (Build Spec "Social layer"): one LiveKit room per venue instance,
    /// hear players within 8 m, subscribe only to the 6 nearest speakers, mic off by default. The LiveKit identity
    /// is the userId (contract section 4, RTC tokens). Remote audio plays through a 3D AudioSource on that player's
    /// avatar via <see cref="SpeakerResolver"/>, so Unity's rolloff gives distance attenuation.
    /// </summary>
    public sealed class VoiceService
    {
        public const float HearingRadius = 8f;
        public const int MaxSubscribed = 6;

        /// <summary>userId → the AudioSource to play that user's voice on (usually on the avatar). Null → 2D fallback.</summary>
        public Func<string, AudioSource> SpeakerResolver;

        /// <summary>userId → world position, for nearest-N subscription. Null if not visible.</summary>
        public Func<string, Vector3?> PositionResolver;

        /// <summary>The listener's world position (local avatar or camera).</summary>
        public Func<Vector3> ListenerPosition;

        public bool IsConnected => _room != null && _room.IsConnected;
        public bool MicOn { get; private set; }
        public bool LocalSpeaking { get; private set; }
        public string RoomName { get; private set; }

        public event Action<bool> LocalSpeakingChanged;
        public event Action<string> Error;

        private Room _room;
        private MicrophoneSource _micSource;
        private LocalAudioTrack _micTrack;
        private GameObject _micObject;
        private GameObject _fallbackParent;
        private readonly Dictionary<string, AudioStream> _streams = new Dictionary<string, AudioStream>();
        private readonly Dictionary<string, RemoteTrackPublication> _audioPubs = new Dictionary<string, RemoteTrackPublication>();
        private readonly HashSet<string> _subscribed = new HashSet<string>();
        private Coroutine _proximityLoop;

        /// <summary>Connects with a token minted by `POST /v1/rtc/token` (or the spike's dev token).</summary>
        public async Task<bool> Connect(string url, string token, string roomLabel)
        {
            await Disconnect();
            _room = new Room();
            RoomName = roomLabel;
            _room.TrackPublished += OnTrackPublished;
            _room.TrackUnpublished += OnTrackUnpublished;
            _room.TrackSubscribed += OnTrackSubscribed;
            _room.TrackUnsubscribed += OnTrackUnsubscribed;
            _room.ActiveSpeakersChanged += OnActiveSpeakersChanged;
            _room.Disconnected += r => Log.Info("voice", "Disconnected from " + RoomName);

            // Manual subscription so we can keep only the nearest speakers.
            var options = new LiveKit.RoomOptions { AutoSubscribe = false, AdaptiveStream = true, Dynacast = true };
            var connect = _room.Connect(url, token, options);
            await Runner.Await(connect);
            if (connect.IsError)
            {
                Log.Warn("voice", $"LiveKit connect failed ({url})");
                Error?.Invoke("connect_failed");
                _room = null;
                return false;
            }
            Log.Info("voice", $"LiveKit connected: {RoomName} as {_room.LocalParticipant?.Identity}");

            foreach (var p in _room.RemoteParticipants.Values)
                foreach (var pub in p.Tracks.Values)
                    OnTrackPublished(pub, p);

            _proximityLoop = Runner.Run(ProximityLoop());
            return true;
        }

        public async Task Disconnect()
        {
            if (_proximityLoop != null) { Runner.Instance.StopCoroutine(_proximityLoop); _proximityLoop = null; }
            await SetMic(false);
            foreach (var s in _streams.Values) s.Dispose();
            _streams.Clear();
            _audioPubs.Clear();
            _subscribed.Clear();
            if (_fallbackParent != null) UnityEngine.Object.Destroy(_fallbackParent);
            if (_room != null)
            {
                try { _room.Disconnect(); } catch (Exception e) { Log.Warn("voice", e.Message); }
                _room = null;
            }
            SetLocalSpeaking(false);
        }

        // ---------------------------------------------------------------- microphone

        public async Task<bool> SetMic(bool on)
        {
            if (on == MicOn) return true;
            if (!on)
            {
                if (_micTrack != null && _room?.LocalParticipant != null)
                {
                    var un = _room.LocalParticipant.UnpublishTrack(_micTrack, true);
                    await Runner.Await(un);
                }
                _micSource?.Dispose();
                if (_micObject != null) UnityEngine.Object.Destroy(_micObject);
                _micSource = null;
                _micTrack = null;
                _micObject = null;
                MicOn = false;
                SetLocalSpeaking(false);
                return true;
            }

            if (_room == null || !_room.IsConnected) return false;
            if (!await MicPermission.Request()) { Error?.Invoke("mic_permission"); return false; }
            if (Microphone.devices.Length == 0) { Error?.Invoke("no_mic"); return false; }

            _micObject = new GameObject("CTLife.Mic");
            UnityEngine.Object.DontDestroyOnLoad(_micObject);
            var processing = new AudioProcessingOptions { EchoCancellation = true, NoiseSuppression = true, AutoGainControl = true };
            _micSource = new MicrophoneSource(Microphone.devices[0], _micObject, processing);
            _micTrack = LocalAudioTrack.CreateAudioTrack("mic", _micSource, _room);
            var publish = _room.LocalParticipant.PublishTrack(_micTrack,
                new TrackPublishOptions { Source = TrackSource.SourceMicrophone, AudioEncoding = new AudioEncoding { MaxBitrate = 32000 } });
            await Runner.Await(publish);
            if (publish.IsError)
            {
                _micSource.Dispose();
                UnityEngine.Object.Destroy(_micObject);
                _micSource = null;
                _micTrack = null;
                _micObject = null;
                Error?.Invoke("publish_failed");
                return false;
            }
            _micSource.Start();
            MicOn = true;
            return true;
        }

        // ---------------------------------------------------------------- remote tracks

        private void OnTrackPublished(RemoteTrackPublication pub, RemoteParticipant participant)
        {
            if (pub.Kind != TrackKind.KindAudio) return;
            _audioPubs[participant.Identity] = pub;
        }

        private void OnTrackUnpublished(RemoteTrackPublication pub, RemoteParticipant participant)
        {
            if (pub.Kind != TrackKind.KindAudio) return;
            _audioPubs.Remove(participant.Identity);
            _subscribed.Remove(participant.Identity);
            DropStream(participant.Identity);
        }

        private void OnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication pub, RemoteParticipant participant)
        {
            if (!(track is RemoteAudioTrack audio)) return;
            DropStream(participant.Identity);
            var source = SpeakerResolver?.Invoke(participant.Identity);
            if (source == null)
            {
                if (_fallbackParent == null)
                {
                    _fallbackParent = new GameObject("CTLife.VoiceFallback");
                    UnityEngine.Object.DontDestroyOnLoad(_fallbackParent);
                }
                var go = new GameObject("Voice " + participant.Identity);
                go.transform.SetParent(_fallbackParent.transform, false);
                source = go.AddComponent<AudioSource>();
                source.spatialBlend = 0f;
            }
            _streams[participant.Identity] = new AudioStream(audio, source);
        }

        private void OnTrackUnsubscribed(IRemoteTrack track, RemoteTrackPublication pub, RemoteParticipant participant)
        {
            if (track is RemoteAudioTrack) DropStream(participant.Identity);
        }

        private void DropStream(string identity)
        {
            if (_streams.TryGetValue(identity, out var s))
            {
                s.Dispose();
                _streams.Remove(identity);
            }
        }

        private void OnActiveSpeakersChanged(List<Participant> speakers)
        {
            bool local = false;
            bool anyNear = false;
            var me = ListenerPosition?.Invoke() ?? Vector3.zero;
            foreach (var p in speakers)
            {
                if (_room != null && p == _room.LocalParticipant) { local = true; continue; }
                var pos = PositionResolver?.Invoke(p.Identity);
                if (pos.HasValue && (pos.Value - me).sqrMagnitude <= HearingRadius * HearingRadius) anyNear = true;
            }
            SetLocalSpeaking(local);
            Bus.Publish(new VoiceActivitySignal { anyoneTalking = anyNear || local });
        }

        private void SetLocalSpeaking(bool on)
        {
            if (LocalSpeaking == on) return;
            LocalSpeaking = on;
            LocalSpeakingChanged?.Invoke(on);
        }

        /// <summary>Every 0.5 s: subscribe to the 6 nearest publishers within 10 m (hysteresis over the 8 m radius).</summary>
        private IEnumerator ProximityLoop()
        {
            var wait = new WaitForSecondsRealtime(0.5f);
            while (_room != null)
            {
                var me = ListenerPosition?.Invoke() ?? Vector3.zero;
                var ranked = _audioPubs.Keys
                    .Select(id => (id, pos: PositionResolver?.Invoke(id)))
                    .Where(x => x.pos.HasValue && (x.pos.Value - me).sqrMagnitude <= 100f)
                    .OrderBy(x => (x.pos.Value - me).sqrMagnitude)
                    .Take(MaxSubscribed)
                    .Select(x => x.id)
                    .ToList();
                var want = new HashSet<string>(ranked);
                foreach (var kv in _audioPubs)
                {
                    bool should = want.Contains(kv.Key);
                    if (should && !_subscribed.Contains(kv.Key)) { kv.Value.SetSubscribed(true); _subscribed.Add(kv.Key); }
                    else if (!should && _subscribed.Contains(kv.Key)) { kv.Value.SetSubscribed(false); _subscribed.Remove(kv.Key); }
                }
                yield return wait;
            }
        }
    }

    /// <summary>Android RECORD_AUDIO runtime permission.</summary>
    public static class MicPermission
    {
        public static async Task<bool> Request()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string perm = UnityEngine.Android.Permission.Microphone;
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(perm)) return true;
            var tcs = new TaskCompletionSource<bool>();
            var cb = new UnityEngine.Android.PermissionCallbacks();
            cb.PermissionGranted += _ => tcs.TrySetResult(true);
            cb.PermissionDenied += _ => tcs.TrySetResult(false);
            UnityEngine.Android.Permission.RequestUserPermission(perm, cb);
            return await tcs.Task;
#else
            await Task.Yield();
            return true;
#endif
        }
    }
}
