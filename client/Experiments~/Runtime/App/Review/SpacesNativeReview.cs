#if UNITY_EDITOR && !CTLIFE_PROD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using LiveKit;
using LiveKit.Proto;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Space = CTLife.Rest.Model.Space;

namespace CTLife.App.Review
{
    /// <summary>Isolated local-server native acceptance. Only unique review-room tokens; no microphone method is called.</summary>
    public static class SpacesNativeReview
    {
        [Serializable] private sealed class Receipt
        {
            public bool pass, backgroundRestored;
            public int runtimeErrors, lateConnects;
            public string failureType, phase;
            public List<Snapshot> snapshots = new List<Snapshot>();
            public List<string> checks = new List<string>();
            public string limits = "Local macOS SDK connection/subscription/teardown and synthetic PCM only. Does not certify Android, microphone capture/permission, game Spaces routes, server role changes or reconnect after real network loss.";
        }
        [Serializable] private sealed class Snapshot
        {
            public string phase, publisherState, listenerState;
            public int localPublications, remoteParticipants, remotePublications, requestedSubscriptions, receivedAudioTracks,
                candidatePublications, candidateRequested, candidateStreams, stageMembers, activeAudioListeners,
                publishedEvents, subscribedEvents, unsubscribedEvents, candidateFailures, outputRate;
            public long toneFrames, meterCallbacks, receivedNonzero;
            public bool rosterIncludesPublisher, playbackPresent, playbackEnabled, playbackPlaying, playbackVirtual,
                playbackMuted, playbackClip, listenerPaused, appFocused, background;
            public float playbackVolume;
            public double dspTime;
        }
        private sealed class Credentials
        {
            public string Url, Room, PublisherToken, ListenerToken, LateToken;
            public Guid Publisher, Listener, Late;
            public DateTime ListenerExpiry, LateExpiry;
        }
        // Generates PCM only through RtcAudioSource's documented public event. All pumping/lifetime is on main thread.
        private sealed class ReviewTone : RtcAudioSource
        {
            public override event Action<float[], int, int> AudioRead;
            private readonly float[] _pcm = new float[960];
            private bool _running;
            private double _phase;
            public long Frames { get; private set; }
            public ReviewTone() : base(RtcAudioSourceType.AudioSourceCustom, 48000, 1) { }
            public override void Start() { base.Start(); _running = true; }
            public override void Stop() { _running = false; base.Stop(); }
            public void Pump()
            {
                if (!_running) return;
                for (var i = 0; i < _pcm.Length; i++) { _pcm[i] = .05f * (float)Math.Sin(_phase); _phase += 2 * Math.PI * 220 / 48000; }
                AudioRead?.Invoke(_pcm, 1, 48000);
                Frames++;
            }
            protected override void Dispose(bool disposing) { if (disposing) Stop(); base.Dispose(disposing); }
        }

        public static async Task Run(string credentialPath, string receiptDirectory, CancellationToken owner)
        {
            Credentials credentials;
            try { credentials = ReadCredentials(credentialPath); }
            catch { throw new InvalidOperationException("Spaces native review: external credential file is missing, invalid, expired or outside the approved local scope"); }
            Directory.CreateDirectory(receiptDirectory);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(owner);
            deadline.CancelAfter(TimeSpan.FromSeconds(90));
            var ct = deadline.Token;
            var receipt = new Receipt();
            bool background = Application.runInBackground;
            Application.runInBackground = true; // Harness only, restored; user focus must not suspend DSP.
            Room publisher = null;
            LocalAudioTrack track = null;
            ReviewTone tone = null;
            SpacesAudio listener = null;
            Room listenerRoom = null;
            SpacesReviewMeter meter = null;
            AudioSource source = null;
            int publishedEvents = 0, subscribedEvents = 0, unsubscribedEvents = 0, candidateFailures = 0;
            void Published(RemoteTrackPublication publication, RemoteParticipant participant) { publishedEvents++; }
            void Subscribed(IRemoteTrack subscribed, RemoteTrackPublication publication, RemoteParticipant participant) { subscribedEvents++; }
            void Unsubscribed(IRemoteTrack subscribed, RemoteTrackPublication publication, RemoteParticipant participant) { unsubscribedEvents++; }
            void CandidateFailed(string ignored) { candidateFailures++; }
            void SnapshotNow(string phase)
            {
                // All fields are counts, booleans, enum state or owned-source telemetry. No identifiers, tokens or SDK error bodies.
                var publications = listenerRoom?.RemoteParticipants.Values.SelectMany(p => p.Tracks.Values).ToList();
                receipt.snapshots.Add(new Snapshot {
                    phase = phase, publisherState = publisher == null ? "absent" : publisher.ConnectionState.ToString(),
                    listenerState = listenerRoom == null ? "absent" : listenerRoom.ConnectionState.ToString(),
                    localPublications = publisher?.LocalParticipant?.Tracks.Count ?? 0,
                    remoteParticipants = listenerRoom?.RemoteParticipants.Count ?? 0,
                    remotePublications = publications?.Count ?? 0,
                    requestedSubscriptions = publications?.Count(p => p.Subscribed) ?? 0,
                    receivedAudioTracks = publications?.Count(p => p.Track is RemoteAudioTrack) ?? 0,
                    candidatePublications = listener == null ? 0 : OwnField<IDictionary>(listener, "_publications")?.Count ?? 0,
                    candidateRequested = listener == null ? 0 : OwnField<HashSet<string>>(listener, "_subscribed")?.Count ?? 0,
                    candidateStreams = listener == null ? 0 : StreamCount(listener),
                    stageMembers = listener == null ? 0 : OwnField<HashSet<string>>(listener, "_stage")?.Count ?? 0,
                    rosterIncludesPublisher = listener != null && OwnField<HashSet<string>>(listener, "_stage")?.Contains(credentials.Publisher.ToString()) == true,
                    publishedEvents = publishedEvents, subscribedEvents = subscribedEvents, unsubscribedEvents = unsubscribedEvents,
                    candidateFailures = candidateFailures, toneFrames = tone?.Frames ?? 0,
                    meterCallbacks = meter == null ? 0 : meter.Callbacks, receivedNonzero = meter == null ? 0 : meter.NonzeroSamples,
                    playbackPresent = source != null, playbackEnabled = source != null && source.enabled,
                    playbackPlaying = source != null && source.isPlaying, playbackVirtual = source != null && source.isVirtual,
                    playbackMuted = source != null && source.mute, playbackVolume = source == null ? -1 : source.volume,
                    playbackClip = source != null && source.clip != null,
                    activeAudioListeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a => a.isActiveAndEnabled),
                    listenerPaused = AudioListener.pause, outputRate = AudioSettings.outputSampleRate, dspTime = AudioSettings.dspTime,
                    appFocused = Application.isFocused, background = Application.runInBackground
                });
            }
            var leftovers = new List<(Room room, Task operation, Room.ConnectionDelegate observer)>();
            void Error(string condition, string trace, LogType kind)
            { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) receipt.runtimeErrors++; }
            Application.logMessageReceived += Error;
            try
            {
                Require(Math.Abs(ServerClock.OffsetMs) < 60000, "Harness requires actual UTC clock for its short-lived local tokens");
                receipt.phase = "publisher-connect";
                publisher = new Room();
                await Join(publisher, credentials.Url, credentials.PublisherToken, ct);
                Require(publisher.Name == credentials.Room && publisher.LocalParticipant.Identity == credentials.Publisher.ToString(), "Publisher joined an unexpected room or identity");
                var roster = new Space(Guid.NewGuid(), "ctlife-review synthetic stage", credentials.Publisher, new List<Guid>(), new List<Guid>(), 0);
                receipt.phase = "candidate-listen-connect";
                listener = new SpacesAudio();
                listener.Failed += CandidateFailed;
                await listener.Connect(new PostRtcToken200Response(credentials.Url, credentials.ListenerToken, credentials.ListenerExpiry, false), roster, credentials.Listener, ct);
                Require(listener.Connected && !listener.MicOn && !listener.MicBusy, "Listener must connect with microphone inactive");
                Require(OwnField<object>(listener, "_source") == null && OwnField<object>(listener, "_track") == null, "Listener constructed local capture");
                listenerRoom = OwnField<Room>(listener, "_room");
                Require(listenerRoom != null && listenerRoom.Name == credentials.Room, "Candidate connected outside isolated room");
                listenerRoom.TrackPublished += Published; listenerRoom.TrackSubscribed += Subscribed; listenerRoom.TrackUnsubscribed += Unsubscribed;
                SnapshotNow("listener-connected");
                receipt.checks.Add("PASS native candidate listen connect; token identity and unique review room verified; no capture source/track exists.");

                receipt.phase = "publish-synthetic-track";
                tone = new ReviewTone();
                track = LocalAudioTrack.CreateAudioTrack("ctlife-review-synthetic-tone", tone, publisher);
                await Instruction(publisher.LocalParticipant.PublishTrack(track, new TrackPublishOptions {
                    Source = TrackSource.SourceMicrophone, AudioEncoding = new AudioEncoding { MaxBitrate = 32000 }
                }), ct);
                tone.Start();
                SnapshotNow("synthetic-track-published");
                receipt.phase = "candidate-stream-created";
                await Until(() => StreamCount(listener) == 1, 12, ct, tone, receipt.phase);
                SnapshotNow("candidate-stream-created");
                source = OwnField<GameObject>(listener, "_root").GetComponentInChildren<AudioSource>();
                Require(source != null, "Candidate subscription produced no playback source");
                source.volume = 0; // Synthetic test source only; keep the user's music/listener settings untouched.
                meter = source.gameObject.AddComponent<SpacesReviewMeter>();
                // The meter must run after LiveKit's public receive component and clear the generated PCM.
                // Only then enable unity-source gain, isolating the previous zero-gain measurement.
                var filters = source.GetComponents<MonoBehaviour>();
                int sdkFilter = Array.FindIndex(filters, f => f.GetType().FullName == "LiveKit.AudioProbe");
                int meterFilter = Array.IndexOf(filters, meter);
                Require(sdkFilter >= 0 && meterFilter > sdkFilter && meter.isActiveAndEnabled,
                    "Synthetic clearing meter must follow the SDK receive filter before gain is enabled");
                receipt.checks.Add("PASS owned clearing meter follows SDK receive filter; gain control changes only synthetic source.");
                SnapshotNow("meter-attached-zero-gain");
                source.volume = 1;
                receipt.phase = "candidate-received-nonzero-pcm";
                SnapshotNow("before-received-pcm");
                await Until(() => meter.NonzeroSamples > 4800, 12, ct, tone, receipt.phase);
                SnapshotNow("candidate-received-nonzero-pcm");
                receipt.checks.Add($"PASS synthetic PCM reached candidate AudioStream: callbacks={meter.Callbacks}, nonzeroSamples={meter.NonzeroSamples}; no PCM stored; output suppressed.");
                listener.SetMuted(true); Require(source.mute && listener.Muted, "Mute did not affect owned receive source");
                listener.SetMuted(false); Require(!source.mute && !listener.Muted, "Unmute did not affect owned receive source");

                // A fresh roster removes this remote speaker. No server moderation endpoint is invented.
                listener.SetRoster(new Space(roster.SpaceId, roster.Title, credentials.Listener, new List<Guid>(), new List<Guid>(), 0), credentials.Listener);
                receipt.phase = "roster-removal";
                await Until(() => StreamCount(listener) == 0 && source == null, 5, ct, tone, receipt.phase);
                Require(!listener.MicOn, "Roster update enabled microphone without an explicit gesture");
                receipt.checks.Add("PASS roster removal unsubscribed and destroyed owned stream; no automatic microphone activation.");
                listener.SetRoster(roster, credentials.Listener);
                receipt.phase = "roster-restoration";
                await Until(() => StreamCount(listener) == 1, 12, ct, tone, receipt.phase);
                var restoredSource = OwnField<GameObject>(listener, "_root").GetComponentInChildren<AudioSource>();
                Require(restoredSource != null, "Restored roster did not rebuild receive stream");
                restoredSource.volume = 0;
                restoredSource.gameObject.AddComponent<SpacesReviewMeter>();
                var ownedRoot = OwnField<GameObject>(listener, "_root");
                listener.Failed -= CandidateFailed;
                listener.Dispose(); listener.Dispose(); listener = null;
                receipt.phase = "candidate-dispose";
                await Until(() => ownedRoot == null && !listenerRoom.IsConnected && listenerRoom.RemoteParticipants.Count == 0 &&
                    !publisher.RemoteParticipants.Values.Any(p => p.Identity == credentials.Listener.ToString()), 8, ct, tone, receipt.phase);
                receipt.checks.Add("PASS repeated dispose: native Room disconnected, participants released, owned Unity playback root destroyed, publisher observed leave.");

                // Native SDK pending Connect ignores Disconnect. Exercise candidate's late-completion cleanup twice.
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    receipt.phase = "pending-connect-abandonment-" + (attempt + 1);
                    var abandoned = new SpacesAudio();
                    var operation = abandoned.Connect(new PostRtcToken200Response(credentials.Url, credentials.LateToken, credentials.LateExpiry, false), roster, credentials.Late, ct);
                    var native = OwnField<Room>(abandoned, "_room");
                    Require(native != null, "Connect did not begin before abandonment");
                    int observedConnect = 0;
                    void Connected(Room room) { observedConnect++; }
                    native.Connected += Connected;
                    leftovers.Add((native, operation, Connected));
                    abandoned.Dispose(); abandoned.Dispose();
                    try { await operation; throw new InvalidOperationException("Disposed candidate reported connection success"); }
                    catch (OperationCanceledException) { }
                    await Until(() => observedConnect > 0 && !native.IsConnected && native.RemoteParticipants.Count == 0 &&
                        !publisher.RemoteParticipants.Values.Any(p => p.Identity == credentials.Late.ToString()), 18, ct, tone, receipt.phase);
                    native.Connected -= Connected;
                    receipt.lateConnects += observedConnect;
                    receipt.checks.Add($"PASS actual pending-connect abandonment {attempt + 1}: native completion observed={observedConnect}; eventual disconnected/absent; no success returned to disposed caller.");
                }
                receipt.phase = "publisher-cleanup";
                tone.Stop();
                await Instruction(publisher.LocalParticipant.UnpublishTrack(track, true), ct);
                track.Handle.Dispose();
                Require(track.Handle.IsClosed, "Synthetic local track handle remains open");
                publisher.Disconnect();
                Require(!publisher.IsConnected && publisher.RemoteParticipants.Count == 0, "Publisher native room remains connected");
                Require(receipt.runtimeErrors == 0, "Runtime errors occurred during native lifecycle review");
                receipt.phase = "complete";
                receipt.pass = true;
            }
            catch (Exception error)
            {
                try { SnapshotNow("failure:" + receipt.phase); }
                catch (Exception snapshotError) { receipt.checks.Add("Sanitized snapshot failed: " + snapshotError.GetType().Name); }
                receipt.failureType = error is OperationCanceledException ? "CancelledOrDeadline" : error.GetType().Name;
                // Sanitized harness assertions are fixed source strings; SDK/token-bearing error bodies are not persisted.
                if (error is InvalidOperationException && error.Message.StartsWith("Spaces native review:", StringComparison.Ordinal)) receipt.checks.Add(error.Message);
                throw new InvalidOperationException("Spaces native review failed; see sanitized receipt for stage and failure type");
            }
            finally
            {
                void Cleanup(Action release)
                {
                    try { release(); }
                    catch (Exception error) { receipt.pass = false; receipt.checks.Add("Cleanup failure: " + error.GetType().Name); }
                }
                if (listenerRoom != null) { listenerRoom.TrackPublished -= Published; listenerRoom.TrackSubscribed -= Subscribed; listenerRoom.TrackUnsubscribed -= Unsubscribed; }
                if (listener != null) listener.Failed -= CandidateFailed;
                Cleanup(() => listener?.Dispose());
                Cleanup(() => tone?.Stop());
                foreach (var pair in leftovers) { pair.room.Connected -= pair.observer; Cleanup(pair.room.Disconnect); Observe(pair.operation); }
                Cleanup(() => publisher?.Disconnect());
                Cleanup(() => track?.Handle.Dispose());
                Cleanup(() => tone?.Dispose());
                Application.logMessageReceived -= Error;
                Application.runInBackground = background;
                receipt.backgroundRestored = Application.runInBackground == background;
                File.WriteAllText(Path.Combine(receiptDirectory, "spaces-native-review-receipt.json"), JsonUtility.ToJson(receipt, true));
            }
            Require(receipt.pass && receipt.backgroundRestored && receipt.runtimeErrors == 0, "Native review cleanup did not pass");
            Log.Info("spaces-native-review", $"PASS synthetic PCM + roster removal/resubscribe + disposal + {receipt.lateConnects} actual late completions; runtimeErrors=0; background restored; no microphone methods invoked.");
        }

        private static T OwnField<T>(SpacesAudio value, string name) where T : class =>
            typeof(SpacesAudio).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(value) as T;
        private static int StreamCount(SpacesAudio value) => OwnField<IDictionary>(value, "_streams")?.Count ?? 0;
        private static async Task Join(Room room, string url, string token, CancellationToken ct)
        {
            bool complete = false;
            try { await Instruction(room.Connect(url, token, new LiveKit.RoomOptions { AutoSubscribe = false, JoinRetries = 0 }), ct); complete = true; }
            finally
            {
                if (!complete)
                {
                    // Public native SDK's Connect cannot be cancelled; retain a late-completion disconnect guard.
                    void Abandoned(Room connected) { connected.Connected -= Abandoned; connected.Disconnect(); }
                    room.Connected += Abandoned; room.Disconnect();
                }
            }
        }
        private static async Task Instruction(LiveKit.Internal.Threading.YieldInstruction instruction, CancellationToken ct)
        {
            await SpacesRequest.Run(async _ => { await Runner.Await(instruction); return true; }, ct, 18000);
            Require(!instruction.IsError, "Native SDK instruction returned an error");
        }
        private static async Task Until(Func<bool> condition, double seconds, CancellationToken ct, ReviewTone tone, string phase)
        {
            var end = Time.realtimeSinceStartupAsDouble + seconds;
            var next = Time.realtimeSinceStartupAsDouble;
            while (!condition())
            {
                ct.ThrowIfCancellationRequested();
                Require(Time.realtimeSinceStartupAsDouble < end, "Timed out at " + phase);
                var now = Time.realtimeSinceStartupAsDouble;
                for (var i = 0; i < 4 && now >= next; i++, next += .02) tone?.Pump();
                if (now - next > .2) next = now;
                await Awaitable.NextFrameAsync(ct);
            }
        }
        private static async void Observe(Task operation) { try { await operation; } catch { } }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Spaces native review: " + message); }
        private static Credentials ReadCredentials(string path)
        {
            Require(Path.GetFullPath(path).StartsWith("/tmp/", StringComparison.Ordinal) || Path.GetFullPath(path).StartsWith("/private/tmp/", StringComparison.Ordinal), "Tokens must live outside the repository in /tmp");
            var json = JObject.Parse(File.ReadAllText(path));
            var result = new Credentials { Url = (string)json["url"], Room = (string)json["room"], PublisherToken = (string)json["publisherToken"], ListenerToken = (string)json["listenerToken"], LateToken = (string)json["lateToken"] };
            Require(result.Url == "ws://127.0.0.1:7880", "Only the approved local server is allowed");
            Require(result.Room != null && result.Room.StartsWith("ctlife-review-", StringComparison.Ordinal) && Guid.TryParse(result.Room.Substring("ctlife-review-".Length), out _), "A uniquely named review room is required");
            result.Publisher = ValidateToken(result.PublisherToken, result.Room, true, out _);
            result.Listener = ValidateToken(result.ListenerToken, result.Room, false, out result.ListenerExpiry);
            result.Late = ValidateToken(result.LateToken, result.Room, false, out result.LateExpiry);
            Require(result.Publisher != result.Listener && result.Listener != result.Late && result.Publisher != result.Late, "Review identities must be distinct");
            return result;
        }
        private static Guid ValidateToken(string token, string room, bool publisher, out DateTime expires)
        {
            var parts = token?.Split('.'); Require(parts != null && parts.Length == 3, "Malformed review token");
            var payload = parts[1].Replace('-', '+').Replace('_', '/'); payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            var claims = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            Require(Guid.TryParse((string)claims["sub"], out var identity) && identity != Guid.Empty, "Review identity must be a fresh UUID");
            Require(((string)claims["name"])?.StartsWith("ctlife-review-", StringComparison.Ordinal) == true, "Participant display name must identify the review");
            var video = claims["video"];
            Require((string)video?["room"] == room && (bool?)video?["roomJoin"] == true && (bool?)video?["canPublish"] == publisher,
                "Token is not restricted to the required room and role");
            Require((bool?)video?["roomAdmin"] != true && (bool?)video?["roomCreate"] != true && (bool?)video?["roomList"] != true,
                "Review token must not have administrative grants");
            expires = DateTimeOffset.FromUnixTimeSeconds((long)claims["exp"]).UtcDateTime;
            Require(expires > DateTime.UtcNow.AddMinutes(2) && expires < DateTime.UtcNow.AddHours(1), "Use newly issued short-lived tokens");
            return identity; // Native server still verifies signature; this only constrains local test intent.
        }
    }
}
#endif
