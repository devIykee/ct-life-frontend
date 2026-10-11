using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;

namespace CTLife.Net
{
    /// <summary>One captured proximity-voice connection per venue visit; microphone requires an explicit gesture.</summary>
    public sealed class VoiceService
    {
        public const float HearingRadius = 8f;
        public const int MaxSubscribed = 6;
        public Func<string, AudioSource> SpeakerResolver;
        public Func<string, Vector3?> PositionResolver;
        public Func<Vector3> ListenerPosition;
        private readonly RoomJoinSlot<VoiceConnection> _connections = new RoomJoinSlot<VoiceConnection>(c => c.IsConnected, c => c.Disconnect());
        private long _revision;
#if UNITY_EDITOR && !CTLIFE_PROD
        // Observation only. The native fixture filters its own service and never replaces the SDK operation.
        public static event Action<VoiceService, object> ReviewConnectionCreated;
#endif
        public bool IsConnected => _connections.Current?.IsConnected == true;
        public bool MicOn => _connections.Current?.MicOn == true;
        public bool LocalSpeaking => _connections.Current?.LocalSpeaking == true;
        public string RoomName => _connections.Current?.RoomName;
        public event Action<bool> LocalSpeakingChanged;
        public event Action<string> Error;

        public async Task<bool> Connect(string url, string token, string roomLabel, CancellationToken owner = default)
        {
            owner.ThrowIfCancellationRequested();
            bool wasSpeaking = LocalSpeaking;
            long revision = ++_revision;
            if (wasSpeaking) LocalSpeakingChanged?.Invoke(false);
            Bus.Publish(new VoiceActivitySignal { anyoneTalking = false });
            try
            {
                await _connections.Join(roomLabel, owner, () => revision == _revision, false,
                    () =>
                    {
                        var connection = new VoiceConnection(owner, SpeakerResolver, PositionResolver, ListenerPosition,
                            speaking => { if (revision == _revision) LocalSpeakingChanged?.Invoke(speaking); },
                            active => { if (revision == _revision) Bus.Publish(new VoiceActivitySignal { anyoneTalking = active }); },
                            error => { if (revision == _revision) Error?.Invoke(error); });
#if UNITY_EDITOR && !CTLIFE_PROD
                        ReviewConnectionCreated?.Invoke(this, connection);
#endif
                        return connection;
                    },
                    async (connection, attempt) => await connection.Connect(url, token, roomLabel, attempt.Token));
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                if (revision == _revision) { Log.Warn("voice", "Connect failed: " + error.GetType().Name); Error?.Invoke("connect_failed"); }
                return false;
            }
        }

        public async Task Disconnect()
        {
            bool wasSpeaking = LocalSpeaking;
            _revision++;
            if (wasSpeaking) LocalSpeakingChanged?.Invoke(false);
            Bus.Publish(new VoiceActivitySignal { anyoneTalking = false });
            var connection = _connections.Detach();
            var retired = _connections.RetiredCleanup;
            if (connection != null) await connection.Disconnect();
            await retired;
        }

        public Task<bool> SetMic(bool on, CancellationToken owner = default) => SetMic(on, owner, owner);

        public Task<bool> SetMic(bool on, CancellationToken owner, CancellationToken gesture)
        {
            gesture.ThrowIfCancellationRequested();
            var connection = _connections.Current;
            if (connection == null || (owner.CanBeCanceled && owner != connection.Owner)) return Task.FromResult(false);
            return connection.SetMic(on, gesture);
        }
    }

    /// <summary>Owns only one SDK room and its streams/capture; late work never reads a successor's fields.</summary>
    internal sealed class VoiceConnection
    {
        public CancellationToken Owner { get; }
        private readonly Func<string, AudioSource> _speaker;
        private readonly Func<string, Vector3?> _position;
        private readonly Func<Vector3> _listener;
        private readonly Action<bool> _speakingChanged, _activity;
        private readonly Action<string> _error;
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private Room _room;
        private bool _closing, _micBusy, _publicationPending;
        private int _micRevision;
        private SpacesCaptureSource _micSource;
        private LocalAudioTrack _micTrack;
        private GameObject _micObject, _fallbackParent;
        private readonly Dictionary<string, GameObject> _fallbackObjects = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, RemoteTrackPublication> _streamPublications = new Dictionary<string, RemoteTrackPublication>();
        private readonly Dictionary<string, AudioStream> _streams = new Dictionary<string, AudioStream>();
        private readonly Dictionary<string, RemoteTrackPublication> _audioPubs = new Dictionary<string, RemoteTrackPublication>();
        private readonly HashSet<string> _subscribed = new HashSet<string>();
        private Coroutine _proximityLoop;
        public bool IsConnected => !_closing && _room != null && _room.ConnectionState == ConnectionState.ConnConnected;
        public bool MicOn { get; private set; }
        public bool LocalSpeaking { get; private set; }
        public string RoomName { get; private set; }

        public VoiceConnection(CancellationToken owner, Func<string, AudioSource> speaker, Func<string, Vector3?> position,
            Func<Vector3> listener, Action<bool> speakingChanged, Action<bool> activity, Action<string> error)
        { Owner = owner; _speaker = speaker; _position = position; _listener = listener; _speakingChanged = speakingChanged; _activity = activity; _error = error; }

        public async Task Connect(string url, string token, string roomLabel, CancellationToken owner)
        {
            owner.ThrowIfCancellationRequested();
            var context = SynchronizationContext.Current;
            if (context == null) throw new InvalidOperationException("Voice requires the Unity main-thread context.");
            int thread = Thread.CurrentThread.ManagedThreadId;
            RoomName = roomLabel;
            var room = new Room(); _room = room;
            room.Connected += RejectLateConnection;
            room.Disconnected += OnDisconnected;
            room.TrackPublished += OnTrackPublished; room.TrackUnpublished += OnTrackUnpublished;
            room.TrackSubscribed += OnTrackSubscribed; room.TrackUnsubscribed += OnTrackUnsubscribed;
            room.ActiveSpeakersChanged += OnActiveSpeakersChanged;
            using var cancellation = owner.Register(() =>
            {
                if (Thread.CurrentThread.ManagedThreadId == thread) _ = Disconnect();
                else context.Post(_ => { _ = Disconnect(); }, null);
            });
            try
            {
                owner.ThrowIfCancellationRequested();
                var instruction = room.Connect(url, token, new LiveKit.RoomOptions { AutoSubscribe = false, AdaptiveStream = true, Dynacast = true });
                await Runner.Await(instruction);
                owner.ThrowIfCancellationRequested();
                if (_closing) throw new OperationCanceledException(owner);
                if (instruction.IsError || !IsConnected) throw new InvalidOperationException("Proximity voice connection failed.");
                foreach (var participant in room.RemoteParticipants.Values)
                    foreach (var publication in participant.Tracks.Values) OnTrackPublished(publication, participant);
                _proximityLoop = Runner.Run(ProximityLoop());
            }
            finally
            {
                room.Connected -= RejectLateConnection;
                // SDK Disconnect before connect completion is a no-op; close this late native handle again.
                if (_closing || owner.IsCancellationRequested) room.Disconnect();
            }
        }
        private void RejectLateConnection(Room room) { if (_closing) room.Disconnect(); }
        private void OnDisconnected(Room room) { _ = Disconnect(); }

        public Task Disconnect()
        {
            if (_closing) return Task.CompletedTask;
            _closing = true; _micRevision++; _life.Cancel();
            var room = _room; _room = null;
            var track = _micTrack; _micTrack = null;
            if (_proximityLoop != null) { Runner.Instance.StopCoroutine(_proximityLoop); _proximityLoop = null; }
            ReleaseCapture();
            foreach (var id in _streams.Keys.ToArray()) Cleanup(() => DropStream(id));
            _audioPubs.Clear(); _subscribed.Clear();
            if (_fallbackParent != null) UnityEngine.Object.Destroy(_fallbackParent);
            _fallbackParent = null; _fallbackObjects.Clear(); _streamPublications.Clear();
            if (room != null)
            {
                room.Disconnected -= OnDisconnected;
                room.TrackPublished -= OnTrackPublished; room.TrackUnpublished -= OnTrackUnpublished;
                room.TrackSubscribed -= OnTrackSubscribed; room.TrackUnsubscribed -= OnTrackUnsubscribed;
                room.ActiveSpeakersChanged -= OnActiveSpeakersChanged;
                Cleanup(room.Disconnect);
            }
            if (track != null && !_publicationPending) Cleanup(track.Handle.Dispose);
            SetLocalSpeaking(false); _activity(false);
            _life.Dispose();
            return Task.CompletedTask;
        }

        public async Task<bool> SetMic(bool on, CancellationToken owner)
        {
            if (_closing || _micBusy || !IsConnected) return false;
            owner.ThrowIfCancellationRequested();
            if (on == MicOn) return true;
            _micBusy = true; int version = ++_micRevision;
            var room = _room;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner, _life.Token);
            LocalAudioTrack track = null;
            try
            {
                if (!on)
                {
                    track = _micTrack; _micTrack = null;
                    ReleaseCapture(); SetLocalSpeaking(false);
                    if (track != null && room.LocalParticipant != null)
                        await Runner.Await(room.LocalParticipant.UnpublishTrack(track, true));
                    return !_closing;
                }
                if (!await SpacesRequest.Run(_ => MicPermission.Request(), linked.Token, 20000)) { _error("mic_permission"); return false; }
                linked.Token.ThrowIfCancellationRequested();
                if (!CurrentMic(version, room)) return false;
                if (Microphone.devices.Length == 0) { _error("no_mic"); return false; }
                _micObject = new GameObject("CTLife.Mic"); UnityEngine.Object.DontDestroyOnLoad(_micObject);
                _micSource = new SpacesCaptureSource(Microphone.devices[0], _micObject,
                    new AudioProcessingOptions { EchoCancellation = true, NoiseSuppression = true, AutoGainControl = true });
                track = LocalAudioTrack.CreateAudioTrack("mic", _micSource, room); _micTrack = track;
                var publish = room.LocalParticipant.PublishTrack(track, new TrackPublishOptions
                    { Source = TrackSource.SourceMicrophone, AudioEncoding = new AudioEncoding { MaxBitrate = 32000 } });
                _publicationPending = true;
                try { await Runner.Await(publish); }
                finally { _publicationPending = false; }
                linked.Token.ThrowIfCancellationRequested();
                if (!CurrentMic(version, room) || publish.IsError) throw new InvalidOperationException("Microphone publication failed.");
                var source = _micSource;
                await source.StartCapture(linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                if (!CurrentMic(version, room) || !ReferenceEquals(_micSource, source)) throw new OperationCanceledException(linked.Token);
                source.SetMute(false); MicOn = true; track = null; // Accepted local track remains owned by this connection.
                return true;
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested || _closing) { return false; }
            catch (Exception error)
            {
                if (!_closing) { Log.Warn("voice", "Microphone unavailable: " + error.GetType().Name); _error("publish_failed"); }
                return false;
            }
            finally
            {
                if (track != null)
                {
                    if (ReferenceEquals(_micTrack, track)) { _micTrack = null; ReleaseCapture(); }
                    if (on && !_closing) await Disconnect(); // Failed/cancelled publication cannot leave an orphaned published track.
                    Cleanup(track.Handle.Dispose);
                }
                _micBusy = false;
            }
        }
        private bool CurrentMic(int version, Room room) => !_closing && version == _micRevision && ReferenceEquals(_room, room) && IsConnected;
        private void ReleaseCapture()
        {
            MicOn = false;
            var source = _micSource; _micSource = null; var go = _micObject; _micObject = null;
            if (source != null) Cleanup(source.Dispose);
            if (go != null) UnityEngine.Object.Destroy(go);
        }
        private void Cleanup(Action release)
        {
            try { release(); }
            catch (Exception error) { Log.Warn("voice", "Owned audio cleanup failed: " + error.GetType().Name); _error("audio_failed"); }
        }
        // ---------------------------------------------------------------- remote tracks

        private void OnTrackPublished(RemoteTrackPublication pub, RemoteParticipant participant)
        {
            if (_closing) return;
            if (pub.Kind != TrackKind.KindAudio) return;
            if (_audioPubs.TryGetValue(participant.Identity, out var previous) && !ReferenceEquals(previous, pub))
            {
                previous.SetSubscribed(false); _subscribed.Remove(participant.Identity); DropStream(participant.Identity);
            }
            _audioPubs[participant.Identity] = pub;
        }

        private void OnTrackUnpublished(RemoteTrackPublication pub, RemoteParticipant participant)
        {
            if (_closing) return;
            if (pub.Kind != TrackKind.KindAudio) return;
            if (!_audioPubs.TryGetValue(participant.Identity, out var current) || !ReferenceEquals(current, pub)) return;
            _audioPubs.Remove(participant.Identity);
            _subscribed.Remove(participant.Identity);
            DropStream(participant.Identity);
        }

        private void OnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication pub, RemoteParticipant participant)
        {
            if (_closing) return;
            if (!(track is RemoteAudioTrack audio) || !_audioPubs.TryGetValue(participant.Identity, out var current) || !ReferenceEquals(current, pub)) return;
            DropStream(participant.Identity);
            var source = _speaker?.Invoke(participant.Identity);
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
                _fallbackObjects[participant.Identity] = go;
            }
            _streams[participant.Identity] = new AudioStream(audio, source);
            _streamPublications[participant.Identity] = pub;
        }

        private void OnTrackUnsubscribed(IRemoteTrack track, RemoteTrackPublication pub, RemoteParticipant participant)
        {
            if (_closing) return;
            if (track is RemoteAudioTrack && _streamPublications.TryGetValue(participant.Identity, out var current) && ReferenceEquals(current, pub)) DropStream(participant.Identity);
        }

        private void DropStream(string identity)
        {
            _streamPublications.Remove(identity);
            if (_streams.TryGetValue(identity, out var s))
            {
                _streams.Remove(identity);
                Cleanup(s.Dispose);
            }
            if (_fallbackObjects.TryGetValue(identity, out var owned))
            {
                _fallbackObjects.Remove(identity);
                if (owned != null) UnityEngine.Object.Destroy(owned);
            }
        }

        private void OnActiveSpeakersChanged(List<Participant> speakers)
        {
            if (_closing) return;
            bool local = false;
            bool anyNear = false;
            var me = _listener?.Invoke() ?? Vector3.zero;
            foreach (var p in speakers)
            {
                if (_room != null && p == _room.LocalParticipant) { local = true; continue; }
                var pos = _position?.Invoke(p.Identity);
                if (pos.HasValue && (pos.Value - me).sqrMagnitude <= VoiceService.HearingRadius * VoiceService.HearingRadius) anyNear = true;
            }
            SetLocalSpeaking(local);
            _activity(anyNear || local);
        }

        private void SetLocalSpeaking(bool on)
        {
            if (LocalSpeaking == on) return;
            LocalSpeaking = on;
            _speakingChanged(on);
        }

        /// <summary>Every 0.5 s: subscribe to the 6 nearest publishers within 10 m (hysteresis over the 8 m radius).</summary>
        private IEnumerator ProximityLoop()
        {
            var wait = new WaitForSecondsRealtime(0.5f);
            while (!_closing && _room != null)
            {
                if (!IsConnected) { yield return wait; continue; }
                var me = _listener?.Invoke() ?? Vector3.zero;
                var ranked = _audioPubs.Keys
                    .Select(id => (id, pos: _position?.Invoke(id)))
                    .Where(x => x.pos.HasValue && (x.pos.Value - me).sqrMagnitude <= 100f)
                    .OrderBy(x => (x.pos.Value - me).sqrMagnitude)
                    .Take(VoiceService.MaxSubscribed)
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
            // A dismissed dialog raises neither callback; without this the request waited for its caller's timeout.
            cb.PermissionRequestDismissed += _ => tcs.TrySetResult(false);
            UnityEngine.Android.Permission.RequestUserPermission(perm, cb);
            return await tcs.Task;
#else
            await Task.Yield();
            return true;
#endif
        }
    }
}
