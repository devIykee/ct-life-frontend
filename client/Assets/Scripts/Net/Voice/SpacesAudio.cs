using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Rest.Model;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;
using Space = CTLife.Rest.Model.Space;

namespace CTLife.Net
{
    /// <summary>One phone-owned stage connection. Explicit opt-in mic; no proximity or listener-count inference.</summary>
    public sealed class SpacesAudio : IDisposable
    {
        private Room _room;
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly Dictionary<string, (RemoteTrackPublication pub, string user)> _publications = new Dictionary<string, (RemoteTrackPublication, string)>();
        private readonly Dictionary<string, (AudioStream stream, GameObject go)> _streams = new Dictionary<string, (AudioStream, GameObject)>();
        private readonly HashSet<string> _stage = new HashSet<string>();
        private readonly HashSet<string> _subscribed = new HashSet<string>();
        private readonly HashSet<string> _speaking = new HashSet<string>();
        private GameObject _root, _micObject;
        private SpacesCaptureSource _source;
        private LocalAudioTrack _track;
        private bool _disposed, _micPending, _unpublishing, _speakerAllowed, _paused, _publicationPending;
        private bool _muted;
        private readonly SpacesPublishGate _publishGate = new SpacesPublishGate();
        private double _nextCaptureCheck;
        public bool Connected => !_disposed && _room != null && _room.ConnectionState == ConnectionState.ConnConnected;
        public bool MicOn { get; private set; }
        public bool MicBusy => _micPending || _unpublishing;
        public bool Muted => _muted;
        public event Action Changed;
        public event Action<string> Failed;
        public bool IsSpeaking(Guid user) => Connected && _speaking.Contains(user.ToString());

        public SpacesAudio()
        {
            _ = Runner.Instance;
            Runner.ApplicationPaused += OnPause;
            Runner.Ticked += CheckCapture;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        public async Task Connect(PostRtcToken200Response token, Space state, Guid me, CancellationToken ct)
        {
            if (_disposed || _room != null) throw new InvalidOperationException("Stage connection is single-use.");
            if (token == null || string.IsNullOrWhiteSpace(token.Url) || string.IsNullOrWhiteSpace(token.Token)
                || new DateTimeOffset(token.ExpiresAt.ToUniversalTime()).ToUnixTimeMilliseconds() <= ServerClock.NowMs + 5000)
            { Dispose(); throw new InvalidOperationException("Invalid or expired room token."); }
            if (ct.IsCancellationRequested) { Dispose(); ct.ThrowIfCancellationRequested(); }
            SetRoster(state, me);
            _root = new GameObject("CTLife.SpacesAudio");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _room = new Room();
            _room.Connected += RejectAbandonedConnection;
            _room.TrackPublished += Published;
            _room.TrackUnpublished += Unpublished;
            _room.TrackSubscribed += Subscribed;
            _room.TrackUnsubscribed += Unsubscribed;
            _room.ActiveSpeakersChanged += Speaking;
            _room.LocalTrackUnpublished += LocalUnpublished;
            _room.Disconnected += Disconnected;
            _room.Reconnecting += Reconnecting;
            _room.Reconnected += Reconnected;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _life.Token);
                linked.Token.ThrowIfCancellationRequested();
                var room = _room;
                var instruction = room.Connect(token.Url, token.Token, new LiveKit.RoomOptions { AutoSubscribe = false, AdaptiveStream = true, Dynacast = true });
                await SpacesRequest.Run(_ => CompleteConnection(room, instruction), linked.Token, 20000);
                if (!Connected) throw new InvalidOperationException("Room connection failed.");
                foreach (var participant in _room.RemoteParticipants.Values)
                    foreach (var publication in participant.Tracks.Values) Published(publication, participant);
                Changed?.Invoke();
            }
            catch { Dispose(); throw; }
        }

        private void RejectAbandonedConnection(Room room)
        {
            if (_disposed || !ReferenceEquals(_room, room)) room.Disconnect();
        }
        private async Task<bool> CompleteConnection(Room room, ConnectInstruction instruction)
        {
            try
            {
                await Runner.Await(instruction);
                if (instruction.IsError) throw new SpacesAudioException("audioFailed");
                return true;
            }
            finally
            {
                room.Connected -= RejectAbandonedConnection;
                // Disconnect before Connect completes is explicitly a no-op in SDK 2.1.0.
                if (_disposed || !ReferenceEquals(_room, room)) room.Disconnect();
            }
        }
        public void InvalidateRoster()
        {
            _speakerAllowed = false;
            _publishGate.Invalidate();
            if (_micPending) MuteMicrophone();
        }
        private bool PublishAllowed => _publishGate.CanBegin(Time.realtimeSinceStartupAsDouble);

        public void SetRoster(Space state, Guid me)
        {
            _stage.Clear();
            if (SpacesPolicy.Valid(state))
            {
                _stage.Add(state.HostId.ToString());
                foreach (var user in SpacesPolicy.Speakers(state).Take(SpacesPolicy.SpeakerLimit)) _stage.Add(user.ToString());
            }
            _speakerAllowed = SpacesPolicy.CanSpeak(state, me);
            _publishGate.Refresh(_speakerAllowed, Time.realtimeSinceStartupAsDouble);
            if (!_speakerAllowed) MuteMicrophone();
            foreach (var pair in _publications.ToArray()) SetSubscription(pair.Key, pair.Value.pub, pair.Value.user);
        }

        public void SetMuted(bool muted)
        {
            _muted = muted;
            foreach (var item in _streams.Values) if (item.go != null) item.go.GetComponent<AudioSource>().mute = muted;
            PublishActivity();
            Changed?.Invoke();
        }

        public async Task EnableMicrophone(bool serverCanPublish, CancellationToken ct)
        {
            if (MicOn) return;
            if (!Connected || !PublishAllowed || !serverCanPublish || MicBusy) throw new InvalidOperationException("Microphone is not available.");
            int version = _publishGate.Begin(Time.realtimeSinceStartupAsDouble);
            _micPending = true;
            Changed?.Invoke();
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _life.Token);
                bool permission = await SpacesRequest.Run(_ => MicPermission.Request(), linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                await Awaitable.NextFrameAsync(linked.Token);
                if (!_publishGate.IsCurrent(version, Time.realtimeSinceStartupAsDouble)) throw new SpacesAudioException("micInterrupted");
                if (!permission) throw new SpacesAudioException("permission");
                if (!Connected || !PublishAllowed || _paused) throw new SpacesAudioException("roleChanged");
                if (Microphone.devices.Length == 0) throw new SpacesAudioException("noMic");
                _micObject = new GameObject("Spaces microphone");
                _micObject.transform.SetParent(_root.transform, false);
                _source = new SpacesCaptureSource(Microphone.devices[0], _micObject,
                    new AudioProcessingOptions { EchoCancellation = true, NoiseSuppression = true, AutoGainControl = true });
                _track = LocalAudioTrack.CreateAudioTrack("microphone", _source, _room);
                var room = _room;
                var track = _track;
                var instruction = room.LocalParticipant.PublishTrack(track, new TrackPublishOptions
                { Source = TrackSource.SourceMicrophone, AudioEncoding = new AudioEncoding { MaxBitrate = 32000 } });
                _publicationPending = true;
                await SpacesRequest.Run(_ => CompletePublication(room, track, instruction), linked.Token, 20000);
                linked.Token.ThrowIfCancellationRequested();
                if (!_publishGate.IsCurrent(version, Time.realtimeSinceStartupAsDouble) || !Connected || !PublishAllowed || _source == null || _paused) throw new SpacesAudioException("roleChanged");
                await _source.StartCapture(linked.Token);
                if (!_publishGate.IsCurrent(version, Time.realtimeSinceStartupAsDouble) || !Connected || !PublishAllowed || _source == null || _paused)
                    throw new SpacesAudioException("roleChanged");
                _source.SetMute(false);
                MicOn = true;
            }
            catch { if (_track != null || _source != null) Dispose(); throw; } // Publication failure tears down; denying permission keeps listening available.
            finally { _micPending = false; Changed?.Invoke(); }
        }

        private async Task<bool> CompletePublication(Room room, LocalAudioTrack track, PublishTrackInstruction instruction)
        {
            try
            {
                await Runner.Await(instruction);
                if (instruction.IsError) throw new SpacesAudioException("audioFailed");
                return true;
            }
            finally
            {
                _publicationPending = false;
                // A pending SDK publication can add its track after Room teardown. The public
                // track SafeHandle is still ours to release even when the Room is already closed.
                if (_disposed || !ReferenceEquals(_room, room)) track.Handle.Dispose();
            }
        }

        /// <summary>Stops local capture synchronously, before waiting for any server acknowledgement.</summary>
        public void MuteMicrophone()
        {
            _publishGate.CancelAttempt();
            if (_micPending && _track != null && !_disposed) { Dispose(); return; }
            var track = _track;
            _track = null;
            ReleaseCapture();
            if (track != null && Connected) Unpublish(track);
        }

        private async void Unpublish(LocalAudioTrack track)
        {
            _unpublishing = true;
            try { await Wait(_room.LocalParticipant.UnpublishTrack(track, true), _life.Token); }
            catch (OperationCanceledException) { }
            catch { Failed?.Invoke("audioFailed"); Dispose(); }
            finally { track.Handle.Dispose(); _unpublishing = false; Changed?.Invoke(); }
        }

        private void ReleaseCapture()
        {
            MicOn = false;
            if (_source != null) { var source = _source; _source = null; source.Dispose(); }
            if (_micObject != null) UnityEngine.Object.Destroy(_micObject);
            _micObject = null;
            if (_room?.LocalParticipant != null) _speaking.Remove(_room.LocalParticipant.Identity);
            PublishActivity();
            Changed?.Invoke();
        }

        private static async Task Wait(LiveKit.Internal.Threading.YieldInstruction instruction, CancellationToken ct)
        {
            await SpacesRequest.Run(async _ => { await Runner.Await(instruction); return true; }, ct, 20000);
            if (instruction.IsError) throw new SpacesAudioException("audioFailed");
        }

        private void Published(RemoteTrackPublication pub, RemoteParticipant participant)
        {
            if (_disposed || pub.Kind != TrackKind.KindAudio) return;
            _publications[pub.Sid] = (pub, participant.Identity);
            SetSubscription(pub.Sid, pub, participant.Identity);
        }
        private void SetSubscription(string sid, RemoteTrackPublication pub, string identity)
        {
            bool want = !_disposed && _stage.Contains(identity);
            if (want && _subscribed.Add(sid)) pub.SetSubscribed(true);
            else if (!want && _subscribed.Remove(sid)) { pub.SetSubscribed(false); Drop(sid); }
        }
        private void Unpublished(RemoteTrackPublication pub, RemoteParticipant participant)
        { _publications.Remove(pub.Sid); _subscribed.Remove(pub.Sid); Drop(pub.Sid); }
        private void Subscribed(IRemoteTrack track, RemoteTrackPublication pub, RemoteParticipant participant)
        {
            if (_disposed || !_stage.Contains(participant.Identity) || !(track is RemoteAudioTrack audio)) return;
            Drop(pub.Sid);
            var go = new GameObject("Stage audio");
            go.transform.SetParent(_root.transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.mute = _muted;
            source.volume = VoiceVolume.Gain;
            _streams[pub.Sid] = (new AudioStream(audio, source), go);
        }
        private void Unsubscribed(IRemoteTrack track, RemoteTrackPublication pub, RemoteParticipant participant) => Drop(pub.Sid);
        private void Drop(string sid)
        {
            if (!_streams.TryGetValue(sid, out var item)) return;
            _streams.Remove(sid);
            try { item.stream.Dispose(); }
            finally { if (item.go != null) UnityEngine.Object.Destroy(item.go); }
        }
        private void Speaking(List<Participant> participants)
        {
            _speaking.Clear();
            foreach (var participant in participants) if (_stage.Contains(participant.Identity)) _speaking.Add(participant.Identity);
            PublishActivity();
            Changed?.Invoke();
        }
        private void PublishActivity() => Bus.Publish(new VoiceActivitySignal { anyoneTalking = !_muted && Connected && _speaking.Count > 0, source = "spaces" });
        private void LocalUnpublished(TrackPublication publication, LocalParticipant participant)
        {
            if (_track != null && publication.Sid == _track.Sid) { InvalidateRoster(); _track = null; ReleaseCapture(); }
        }
        private void Disconnected(Room room) { Dispose(); }
        private void Reconnecting(Room room) { MuteMicrophone(); Changed?.Invoke(); }
        private void Reconnected(Room room) { Changed?.Invoke(); } // Never resume the microphone automatically.
        private void OnPause(bool paused) { _paused = paused; if (paused) MuteMicrophone(); }
        private void OnAudioConfigurationChanged(bool changed) { if (MicOn || _micPending) MuteMicrophone(); }
        private void CheckCapture()
        {
            if (!MicOn || Time.realtimeSinceStartupAsDouble < _nextCaptureCheck) return;
            _nextCaptureCheck = Time.realtimeSinceStartupAsDouble + 0.5;
            if (_source == null || !_source.IsRecording) { MuteMicrophone(); Failed?.Invoke("audioFailed"); }
        }

        private void Cleanup(Action release)
        {
            try { release(); }
            catch (Exception error)
            {
                // Continue releasing independent resources, but expose a sanitized cleanup failure.
                Log.Warn("spaces", "Audio cleanup failed: " + error.GetType().Name);
                Failed?.Invoke("audioFailed");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _life.Cancel();
            Runner.ApplicationPaused -= OnPause;
            Runner.Ticked -= CheckCapture;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            Cleanup(ReleaseCapture);
            var localTrack = _track;
            _track = null;
            var room = _room;
            _room = null;
            // Streams can create native handles lazily on the audio thread: detach them before the Room releases track handles.
            foreach (var id in _streams.Keys.ToArray()) Cleanup(() => Drop(id));
            if (room != null)
            {
                room.TrackPublished -= Published; room.TrackUnpublished -= Unpublished;
                room.TrackSubscribed -= Subscribed; room.TrackUnsubscribed -= Unsubscribed;
                room.ActiveSpeakersChanged -= Speaking; room.LocalTrackUnpublished -= LocalUnpublished;
                room.Disconnected -= Disconnected; room.Reconnecting -= Reconnecting; room.Reconnected -= Reconnected;
                Cleanup(room.Disconnect);
            }
            if (!_publicationPending && localTrack != null) Cleanup(localTrack.Handle.Dispose);
            _publications.Clear(); _subscribed.Clear(); _speaking.Clear();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            PublishActivity();
            Changed?.Invoke();
        }
    }

    public sealed class SpacesAudioException : Exception
    {
        public string Key { get; }
        public SpacesAudioException(string key) : base(key) { Key = key; }
    }
}
