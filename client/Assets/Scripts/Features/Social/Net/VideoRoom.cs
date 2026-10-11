using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;

namespace CTLife.SocialFeatures
{
    /// <summary>One remote participant in a call, as the phone UI renders it.</summary>
    public sealed class CallPeer
    {
        public string Identity;
        public Texture Video;
        public bool HasVideo, HasAudio, Speaking, CameraMuted, MicMuted;
    }

    /// <summary>
    /// LiveKit media for one video call (room `call:{callId}`), owned by the phone, single-use. It builds on the existing
    /// voice pieces instead of duplicating them: microphone capture is <see cref="SpacesCaptureSource"/> (the same owned,
    /// cancellable capture Spaces uses), coroutines run on <see cref="Runner"/>, and every SDK await is bounded by
    /// <see cref="SpacesRequest.Run{T}"/>. Video is new: camera publish with the SDK's <c>WebCameraSource</c> and remote
    /// <c>VideoStream</c> textures (LiveKit Unity SDK 2.1.0 README "Video" and Samples~/Meet, read from the pinned package).
    /// Mic and camera start off and need a tap; they are never resumed automatically after a pause or reconnect.
    /// Calls are never recorded.
    /// </summary>
    public sealed class VideoRoom : IDisposable
    {
        private Room _room;
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private GameObject _root;
        private bool _disposed, _closingByUs, _micBusy, _camBusy, _publicationPending;

        private SpacesCaptureSource _micSource;
        private LocalAudioTrack _micTrack;
        private GameObject _micObject;

        private WebCamTexture _cam;
        private WebCameraSource _camSource;
        private LocalVideoTrack _camTrack;
        private Coroutine _camLoop;

        private readonly Dictionary<string, CallPeer> _peers = new Dictionary<string, CallPeer>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (VideoStream stream, Coroutine loop, string identity)> _video = new Dictionary<string, (VideoStream, Coroutine, string)>();
        private readonly Dictionary<string, (AudioStream stream, GameObject go, string identity)> _audio = new Dictionary<string, (AudioStream, GameObject, string)>();
        private bool _speakerMuted;

        public bool Connected => !_disposed && _room != null && _room.ConnectionState == ConnectionState.ConnConnected;
        public bool SdkReconnecting { get; private set; }
        public bool MicOn { get; private set; }
        public bool CameraOn { get; private set; }
        public bool MicBusy => _micBusy;
        public bool CameraBusy => _camBusy;
        public bool LocalSpeaking { get; private set; }
        public Texture LocalPreview { get; private set; }
        public int LocalRotation => _cam != null ? _cam.videoRotationAngle : 0;
        public bool SpeakerMuted => _speakerMuted;
        public IReadOnlyCollection<CallPeer> Peers => _peers.Values;

        public event Action Changed;
        public event Action<string> PeerJoined, PeerLeft;
        /// <summary>The room closed without <see cref="Dispose"/> (network, server, room ended). Raised once.</summary>
        public event Action Lost;
        public event Action<string> Failed;

        public VideoRoom()
        {
            _ = Runner.Instance;
            Runner.ApplicationPaused += OnPause;
        }

        public async Task Connect(RtcGrant grant, CancellationToken ct)
        {
            if (_disposed || _room != null) throw new InvalidOperationException("Call media is single-use.");
            if (grant == null || !grant.Usable(ServerClock.NowMs)) { Dispose(); throw new SpacesAudioException("tokenExpired"); }
            _root = new GameObject("CTLife.CallMedia");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var room = _room = new Room();
            room.Connected += RejectAbandoned;
            room.ParticipantConnected += OnParticipantConnected;
            room.ParticipantDisconnected += OnParticipantDisconnected;
            room.TrackSubscribed += OnTrackSubscribed;
            room.TrackUnsubscribed += OnTrackUnsubscribed;
            room.TrackMuted += OnTrackMuted;
            room.TrackUnmuted += OnTrackUnmuted;
            room.ActiveSpeakersChanged += OnSpeakers;
            room.Disconnected += OnDisconnected;
            room.Reconnecting += OnReconnecting;
            room.Reconnected += OnReconnected;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _life.Token);
                var url = ResolveUrl(grant.Url);
                var instruction = room.Connect(url, grant.Token, new LiveKit.RoomOptions { AutoSubscribe = true, AdaptiveStream = true, Dynacast = true });
                await SpacesRequest.Run(_ => Complete(room, instruction), linked.Token, 20000);
                if (!Connected) throw new SpacesAudioException("audioFailed");
                foreach (var p in room.RemoteParticipants.Values)
                {
                    EnsurePeer(p.Identity);
                    foreach (var pub in p.Tracks.Values)
                        if (pub.Track != null) OnTrackSubscribed(pub.Track, pub, p);
                }
                Changed?.Invoke();
            }
            catch { Dispose(); throw; }
        }

        /// <summary>EnvConfig.liveKitUrlOverride (mock/local: `ws://localhost:7880` through adb reverse), as VenueSession does.</summary>
        private static string ResolveUrl(string url)
        {
            var env = Env.Current;
            return env != null && !string.IsNullOrEmpty(env.liveKitUrlOverride) ? env.liveKitUrlOverride : url;
        }

        private void RejectAbandoned(Room room) { if (_disposed || !ReferenceEquals(_room, room)) room.Disconnect(); }

        private async Task<bool> Complete(Room room, ConnectInstruction instruction)
        {
            try
            {
                await Runner.Await(instruction);
                if (instruction.IsError) throw new SpacesAudioException("audioFailed");
                return true;
            }
            finally
            {
                room.Connected -= RejectAbandoned;
                if (_disposed || !ReferenceEquals(_room, room)) room.Disconnect(); // SDK: Disconnect before connect completes is a no-op
            }
        }

        // ---------------------------------------------------------------- local microphone

        public async Task<bool> SetMic(bool on, CancellationToken ct)
        {
            if (_disposed || _micBusy || !Connected) return false;
            if (on == MicOn) return true;
            if (!on) { StopMic(); Changed?.Invoke(); return true; }
            _micBusy = true; Changed?.Invoke();
            var room = _room;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _life.Token);
                var perm = await MediaPermission.Request(MediaPermission.Kind.Microphone, linked.Token);
                if (perm != PermissionResult.Granted) { Failed?.Invoke(MediaPermission.Key(MediaPermission.Kind.Microphone, perm)); return false; }
                if (!Connected || !ReferenceEquals(room, _room)) return false;
                if (Microphone.devices.Length == 0) { Failed?.Invoke("perm.noMic"); return false; }
                _micObject = new GameObject("Call microphone");
                _micObject.transform.SetParent(_root.transform, false);
                _micSource = new SpacesCaptureSource(Microphone.devices[0], _micObject,
                    new AudioProcessingOptions { EchoCancellation = true, NoiseSuppression = true, AutoGainControl = true });
                var track = _micTrack = LocalAudioTrack.CreateAudioTrack("microphone", _micSource, room);
                var publish = room.LocalParticipant.PublishTrack(track, new TrackPublishOptions
                    { Source = TrackSource.SourceMicrophone, AudioEncoding = new AudioEncoding { MaxBitrate = 32000 } });
                _publicationPending = true;
                try { await SpacesRequest.Run(async _ => { await Runner.Await(publish); return true; }, linked.Token, 20000); }
                finally
                {
                    _publicationPending = false;
                    // Stopped while the publication was pending: StopMic skipped the handle, release it now.
                    if (!ReferenceEquals(track, _micTrack)) Retire(track, track.Handle.Dispose);
                }
                if (publish.IsError || !Connected || !ReferenceEquals(track, _micTrack)) throw new SpacesAudioException("audioFailed");
                await _micSource.StartCapture(linked.Token);
                if (!ReferenceEquals(track, _micTrack) || _micSource == null) return false;
                _micSource.SetMute(false);
                MicOn = true;
                return true;
            }
            catch (OperationCanceledException) { StopMic(); return false; }
            catch (Exception e)
            {
                Log.Warn("calls", "Microphone failed: " + e.GetType().Name);
                StopMic();
                if (!_disposed) Failed?.Invoke("calls.micFailed");
                return false;
            }
            finally { _micBusy = false; if (!_disposed) Changed?.Invoke(); }
        }

        private void StopMic()
        {
            MicOn = false;
            LocalSpeaking = false;
            var track = _micTrack; _micTrack = null;
            var source = _micSource; _micSource = null;
            var go = _micObject; _micObject = null;
            if (source != null) Cleanup(source.Dispose);
            if (go != null) UnityEngine.Object.Destroy(go);
            if (track != null && !_publicationPending) Retire(track, track.Handle.Dispose);
            Bus.Publish(new VoiceActivitySignal { anyoneTalking = AnyoneSpeaking });
        }

        // ---------------------------------------------------------------- local camera

        public async Task<bool> SetCamera(bool on, CancellationToken ct)
        {
            if (_disposed || _camBusy || !Connected) return false;
            if (on == CameraOn) return true;
            if (!on) { StopCamera(); Changed?.Invoke(); return true; }
            _camBusy = true; Changed?.Invoke();
            var room = _room;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _life.Token);
                var perm = await MediaPermission.Request(MediaPermission.Kind.Camera, linked.Token);
                if (perm != PermissionResult.Granted) { Failed?.Invoke(MediaPermission.Key(MediaPermission.Kind.Camera, perm)); return false; }
                // Devices can take a few frames to enumerate after the grant (Samples~/Meet CameraDeviceProvider waits too).
                for (int i = 0; i < 120 && WebCamTexture.devices.Length == 0; i++) await Awaitable.NextFrameAsync(linked.Token);
                if (WebCamTexture.devices.Length == 0) { Failed?.Invoke("perm.noCamera"); return false; }
                if (!Connected || !ReferenceEquals(room, _room)) return false;
                var device = WebCamTexture.devices.FirstOrDefault(d => d.isFrontFacing);
                if (string.IsNullOrEmpty(device.name)) device = WebCamTexture.devices[0];
                // 640x480 @ 24 fps: a phone-panel tile, well inside the Seeker's thermal budget. [Inference] tune on device.
                _cam = new WebCamTexture(device.name, 640, 480, 24) { wrapMode = TextureWrapMode.Clamp };
                _cam.Play();
                for (int i = 0; i < 120 && _cam != null && _cam.width <= 16; i++) await Awaitable.NextFrameAsync(linked.Token);
                if (_cam == null || _cam.width <= 16) throw new SpacesAudioException("cameraFailed");
                var source = _camSource = new WebCameraSource(_cam);
                source.TextureReceived += tex => { if (ReferenceEquals(source, _camSource)) { LocalPreview = tex; Changed?.Invoke(); } };
                var track = _camTrack = LocalVideoTrack.CreateVideoTrack("camera", source, room);
                var publish = room.LocalParticipant.PublishTrack(track, new TrackPublishOptions
                {
                    VideoCodec = VideoCodec.Vp8,
                    VideoEncoding = new VideoEncoding { MaxBitrate = 600_000, MaxFramerate = 24 },
                    Simulcast = false,
                    Source = TrackSource.SourceCamera,
                });
                await SpacesRequest.Run(async _ => { await Runner.Await(publish); return true; }, linked.Token, 20000);
                if (publish.IsError || !Connected || !ReferenceEquals(track, _camTrack)) throw new SpacesAudioException("cameraFailed");
                source.Start();
                _camLoop = Runner.Run(source.Update());
                CameraOn = true;
                return true;
            }
            catch (OperationCanceledException) { StopCamera(); return false; }
            catch (Exception e)
            {
                Log.Warn("calls", "Camera failed: " + e.GetType().Name);
                StopCamera();
                if (!_disposed) Failed?.Invoke("calls.cameraFailed");
                return false;
            }
            finally { _camBusy = false; if (!_disposed) Changed?.Invoke(); }
        }

        private void StopCamera()
        {
            CameraOn = false;
            LocalPreview = null;
            if (_camLoop != null) { Cleanup(() => Runner.Instance.StopCoroutine(_camLoop)); _camLoop = null; }
            var source = _camSource; _camSource = null;
            var track = _camTrack; _camTrack = null;
            var cam = _cam; _cam = null;
            if (source != null) { Cleanup(source.Stop); Cleanup(source.Dispose); }
            if (track != null) Retire(track, track.Handle.Dispose);
            if (cam != null) { Cleanup(cam.Stop); UnityEngine.Object.Destroy(cam); }
        }

        /// <summary>Local speaker mute (the remote audio), not the microphone.</summary>
        public void SetSpeakerMuted(bool muted)
        {
            _speakerMuted = muted;
            foreach (var a in _audio.Values) if (a.go != null) a.go.GetComponent<AudioSource>().mute = muted;
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- remote side

        private CallPeer EnsurePeer(string identity)
        {
            if (string.IsNullOrEmpty(identity)) return null;
            if (!_peers.TryGetValue(identity, out var p))
            {
                _peers[identity] = p = new CallPeer { Identity = identity };
                PeerJoined?.Invoke(identity);
            }
            return p;
        }

        private void OnParticipantConnected(Participant participant)
        {
            if (_disposed) return;
            EnsurePeer(participant.Identity);
            Changed?.Invoke();
        }

        private void OnParticipantDisconnected(Participant participant)
        {
            if (_disposed) return;
            foreach (var sid in _video.Where(v => v.Value.identity == participant.Identity).Select(v => v.Key).ToList()) DropVideo(sid);
            foreach (var sid in _audio.Where(a => a.Value.identity == participant.Identity).Select(a => a.Key).ToList()) DropAudio(sid);
            if (_peers.Remove(participant.Identity)) PeerLeft?.Invoke(participant.Identity);
            Changed?.Invoke();
        }

        private void OnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication pub, RemoteParticipant participant)
        {
            if (_disposed) return;
            var peer = EnsurePeer(participant.Identity);
            if (peer == null) return;
            if (track is RemoteVideoTrack video && pub.Source == TrackSource.SourceCamera)
            {
                DropVideo(pub.Sid);
                var stream = new VideoStream(video);
                var sid = pub.Sid;
                stream.TextureReceived += tex =>
                {
                    if (!_video.ContainsKey(sid) || !_peers.TryGetValue(participant.Identity, out var p)) return;
                    p.Video = tex; Changed?.Invoke();
                };
                stream.Start();
                _video[sid] = (stream, Runner.Run(stream.Update()), participant.Identity);
                peer.HasVideo = true;
                peer.CameraMuted = pub.Muted;
            }
            else if (track is RemoteAudioTrack audio)
            {
                DropAudio(pub.Sid);
                var go = new GameObject("Call audio " + participant.Identity);
                go.transform.SetParent(_root.transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false; source.spatialBlend = 0; source.mute = _speakerMuted;
                _audio[pub.Sid] = (new AudioStream(audio, source), go, participant.Identity);
                peer.HasAudio = true;
                peer.MicMuted = pub.Muted;
            }
            Changed?.Invoke();
        }

        private void OnTrackUnsubscribed(IRemoteTrack track, RemoteTrackPublication pub, RemoteParticipant participant)
        {
            if (_disposed) return;
            DropVideo(pub.Sid);
            DropAudio(pub.Sid);
            if (_peers.TryGetValue(participant.Identity, out var p))
            {
                p.HasVideo = _video.Values.Any(v => v.identity == participant.Identity);
                if (!p.HasVideo) p.Video = null;
                p.HasAudio = _audio.Values.Any(a => a.identity == participant.Identity);
            }
            Changed?.Invoke();
        }

        private void OnTrackMuted(TrackPublication pub, Participant participant) => SetMuted(pub, participant, true);
        private void OnTrackUnmuted(TrackPublication pub, Participant participant) => SetMuted(pub, participant, false);

        private void SetMuted(TrackPublication pub, Participant participant, bool muted)
        {
            if (_disposed || participant == null || !_peers.TryGetValue(participant.Identity, out var p)) return;
            if (pub.Kind == TrackKind.KindVideo) p.CameraMuted = muted;
            else if (pub.Kind == TrackKind.KindAudio) p.MicMuted = muted;
            Changed?.Invoke();
        }

        private void OnSpeakers(List<Participant> speakers)
        {
            if (_disposed) return;
            var ids = new HashSet<string>(speakers.Select(s => s.Identity), StringComparer.OrdinalIgnoreCase);
            foreach (var p in _peers.Values) p.Speaking = ids.Contains(p.Identity);
            LocalSpeaking = MicOn && _room?.LocalParticipant != null && ids.Contains(_room.LocalParticipant.Identity);
            Bus.Publish(new VoiceActivitySignal { anyoneTalking = AnyoneSpeaking });
            Changed?.Invoke();
        }

        private bool AnyoneSpeaking => !_speakerMuted && _peers.Values.Any(p => p.Speaking);

        private void DropVideo(string sid)
        {
            if (!_video.TryGetValue(sid, out var v)) return;
            _video.Remove(sid);
            if (v.loop != null) Cleanup(() => Runner.Instance.StopCoroutine(v.loop));
            Cleanup(v.stream.Stop);
            Cleanup(v.stream.Dispose);
        }

        private void DropAudio(string sid)
        {
            if (!_audio.TryGetValue(sid, out var a)) return;
            _audio.Remove(sid);
            try { a.stream.Dispose(); }
            finally { if (a.go != null) UnityEngine.Object.Destroy(a.go); }
        }

        private void OnReconnecting(Room room)
        {
            SdkReconnecting = true;
            // Privacy: capture stops while the link is down and is not resumed automatically.
            StopMic(); StopCamera();
            Changed?.Invoke();
        }

        private void OnReconnected(Room room) { SdkReconnecting = false; Changed?.Invoke(); }

        private void OnDisconnected(Room room)
        {
            if (_disposed || _closingByUs) return;
            Dispose();
            Lost?.Invoke();
        }

        private void OnPause(bool paused)
        {
            if (!paused || _disposed) return;
            if (CameraOn) StopCamera();
            if (MicOn) StopMic();
            Changed?.Invoke();
        }

        /// <summary>Unpublishes a local track (when still connected), then releases its native handle, never before.</summary>
        private async void Retire(ILocalTrack track, Action release)
        {
            var room = _room;
            try
            {
                if (room != null && room.LocalParticipant != null && room.ConnectionState == ConnectionState.ConnConnected)
                    await SpacesRequest.Run(async _ => { await Runner.Await(room.LocalParticipant.UnpublishTrack(track, true)); return true; }, CancellationToken.None, 10000);
            }
            catch (Exception e) { Log.Warn("calls", "Unpublish failed: " + e.GetType().Name); }
            finally { Cleanup(release); }
        }

        private void Cleanup(Action release)
        {
            try { release(); }
            catch (Exception e) { Log.Warn("calls", "Media cleanup failed: " + e.GetType().Name); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _closingByUs = true;
            _life.Cancel();
            Runner.ApplicationPaused -= OnPause;
            Cleanup(StopCamera);
            Cleanup(StopMic);
            foreach (var sid in _video.Keys.ToList()) Cleanup(() => DropVideo(sid));
            foreach (var sid in _audio.Keys.ToList()) Cleanup(() => DropAudio(sid));
            var room = _room; _room = null;
            if (room != null)
            {
                room.ParticipantConnected -= OnParticipantConnected; room.ParticipantDisconnected -= OnParticipantDisconnected;
                room.TrackSubscribed -= OnTrackSubscribed; room.TrackUnsubscribed -= OnTrackUnsubscribed;
                room.TrackMuted -= OnTrackMuted; room.TrackUnmuted -= OnTrackUnmuted;
                room.ActiveSpeakersChanged -= OnSpeakers; room.Disconnected -= OnDisconnected;
                room.Reconnecting -= OnReconnecting; room.Reconnected -= OnReconnected;
                Cleanup(room.Disconnect);
            }
            _peers.Clear();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            Bus.Publish(new VoiceActivitySignal { anyoneTalking = false });
            Changed?.Invoke();
        }
    }
}
