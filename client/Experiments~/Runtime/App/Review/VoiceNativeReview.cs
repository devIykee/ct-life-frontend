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
using LiveKit;
using LiveKit.Proto;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.App.Review
{
    /// <summary>Local SDK acceptance of the real proximity VoiceService. No game API or microphone call.</summary>
    public static class VoiceNativeReview
    {
        [Serializable] private sealed class Receipt
        {
            public bool pass, backgroundRestored, loggerRestored, servicesUnchanged, observerRemoved, externalSourceDestroyed;
            public int runtimeErrors, candidateErrors, actualLateConnects, controlledOldCallbacks, createdConnections, cleanupFailures, observerSubscriptionsBefore, observerSubscriptionsAfter;
            public string phase, failureType;
            public List<string> checks = new List<string>();
            public List<Snapshot> snapshots = new List<Snapshot>();
            public string limits = "Local macOS LiveKit SDK and real proximity VoiceService with one synthetic publisher. Injected old owned callbacks are labelled separately from actual late native Connected events. No microphone/permission, Android, game RTC endpoint, multi-peer cap, network-loss reconnect, or venue authority acceptance.";
        }
        [Serializable] private sealed class Snapshot
        {
            public string phase, nativeState;
            public bool serviceConnected, micOn, captureAbsent, nativeConnected, background;
            public int candidateStreams, publications, subscribed, fallbackObjects, nativeParticipants;
            public long toneFrames, meterCallbacks, nonzeroSamples;
        }
        private sealed class Credentials
        {
            public string Url, Room, PublisherToken, ListenerToken, LateToken, SuccessorToken;
            public Guid Publisher, Listener, Late, Successor;
            public IEnumerable<string> Tokens => new[] { PublisherToken, ListenerToken, LateToken, SuccessorToken };
        }
        private sealed class Capture
        {
            public object Connection;
            public Room Room;
            public Task<bool> Operation;
            public Room.ConnectionDelegate ConnectedObserver;
            public int ConnectedEvents;
        }
        private sealed class Tone : RtcAudioSource
        {
            public override event Action<float[], int, int> AudioRead;
            private readonly float[] _pcm = new float[960];
            private bool _running;
            private double _phase;
            public long Frames { get; private set; }
            public Tone() : base(RtcAudioSourceType.AudioSourceCustom, 48000, 1) { }
            public override void Start() { base.Start(); _running = true; }
            public override void Stop() { _running = false; base.Stop(); }
            public void Pump()
            {
                if (!_running) return;
                for (int i = 0; i < _pcm.Length; i++) { _pcm[i] = .05f * (float)Math.Sin(_phase); _phase += 2 * Math.PI * 220 / 48000; }
                AudioRead?.Invoke(_pcm, 1, 48000); Frames++;
            }
            protected override void Dispose(bool disposing) { if (disposing) Stop(); base.Dispose(disposing); }
        }
        // Fixture-only exact-secret redaction, not suppression. Preserve severity and forward all entries.
        // Native stderr outside Unity's logger is not intercepted; never copy raw SDK output into receipts.
        private sealed class RedactingLog : ILogHandler
        {
            private readonly ILogHandler _previous;
            private readonly string[] _tokens;
            public RedactingLog(ILogHandler previous, Credentials credentials) { _previous = previous; _tokens = credentials.Tokens.ToArray(); }
            private string Clean(string value)
            { foreach (var token in _tokens) if (!string.IsNullOrEmpty(token)) value = value.Replace(token, "[review-token-redacted]"); return value; }
            public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args)
            {
                string rendered;
                try { rendered = string.Format(format, args); } catch { rendered = format; }
                _previous.LogFormat(type, context, "{0}", Clean(rendered));
            }
            public void LogException(Exception exception, UnityEngine.Object context)
            {
                string original = exception.ToString(), clean = Clean(original);
                _previous.LogException(original == clean ? exception : new Exception(clean), context);
            }
        }

        public static async Task Run(string credentialPath, string receiptDirectory, CancellationToken owner)
        {
            Directory.CreateDirectory(receiptDirectory);
            var receipt = new Receipt { phase = "validate-local-scope", observerSubscriptionsBefore = ObserverCount() };
            bool background = Application.runInBackground;
            var priorLogger = Debug.unityLogger.logHandler;
            RedactingLog redactor = null;
            Services.TryGet<VoiceService>(out var originalService);
            var service = new VoiceService(); // Never installed into shared Services.
            var captures = new List<Capture>();
            var owners = new List<CancellationTokenSource>();
            Room publisher = null;
            LocalAudioTrack track = null;
            Tone tone = null;
            GameObject externalObject = null;
            AudioSource source = null;
            SpacesReviewMeter meter = null;
            object active = null;
            Room activeRoom = null;
            int speakingEvents = 0, activityEvents = 0;
            Action activitySubscription = null;
            void Created(VoiceService from, object connection)
            { if (ReferenceEquals(from, service)) captures.Add(new Capture { Connection = connection }); }
            void CandidateError(string ignored) { receipt.candidateErrors++; }
            void Speaking(bool ignored) { speakingEvents++; }
            void RuntimeError(string ignored, string trace, LogType kind)
            { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) receipt.runtimeErrors++; }
            void SnapshotNow(string phase)
            {
                receipt.snapshots.Add(new Snapshot {
                    phase = phase, nativeState = activeRoom == null ? "absent" : activeRoom.ConnectionState.ToString(),
                    serviceConnected = service.IsConnected, micOn = service.MicOn,
                    captureAbsent = active == null || (Field<object>(active, "_micSource") == null && Field<object>(active, "_micTrack") == null && Field<GameObject>(active, "_micObject") == null),
                    nativeConnected = activeRoom?.IsConnected == true,
                    candidateStreams = Count(active, "_streams"), publications = Count(active, "_audioPubs"),
                    subscribed = Field<HashSet<string>>(active, "_subscribed")?.Count ?? 0,
                    fallbackObjects = Count(active, "_fallbackObjects"), nativeParticipants = activeRoom?.RemoteParticipants.Count ?? 0,
                    toneFrames = tone?.Frames ?? 0, meterCallbacks = meter == null ? 0 : meter.Callbacks,
                    nonzeroSamples = meter == null ? 0 : meter.NonzeroSamples, background = Application.runInBackground
                });
            }
            CancellationTokenSource NewOwner(CancellationToken deadline)
            { var next = CancellationTokenSource.CreateLinkedTokenSource(deadline); owners.Add(next); return next; }
            Capture Start(string token, string label, CancellationToken cancellation, string url)
            {
                int before = captures.Count;
                var operation = service.Connect(url, token, label, cancellation);
                Require(captures.Count == before + 1, "Factory observer did not capture exactly this service's connection");
                var captured = captures[captures.Count - 1];
                captured.Operation = operation; captured.Room = Field<Room>(captured.Connection, "_room");
                Require(captured.Room != null, "Native Connect did not begin synchronously");
                captured.ConnectedObserver = room => captured.ConnectedEvents++;
                captured.Room.Connected += captured.ConnectedObserver;
                return captured;
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(owner);
            deadline.CancelAfter(TimeSpan.FromSeconds(160));
            var ct = deadline.Token;
            VoiceService.ReviewConnectionCreated += Created;
            service.Error += CandidateError; service.LocalSpeakingChanged += Speaking;
            Application.logMessageReceived += RuntimeError;
            try
            {
                var credentials = ReadCredentials(credentialPath);
                Require(originalService == null || (!originalService.IsConnected && Current(originalService) == null &&
                    Field<object>(Field<object>(originalService, "_connections"), "_pending") == null),
                    "Shared voice has an active or pending owner; local review must not overlap it");
                Require(Math.Abs(ServerClock.OffsetMs) < 60000, "Local review requires actual UTC clock");
                redactor = new RedactingLog(priorLogger, credentials); Debug.unityLogger.logHandler = redactor;
                Application.runInBackground = true;
                activitySubscription = Bus.Subscribe<VoiceActivitySignal>(_ => activityEvents++);
                bool near = true;
                service.PositionResolver = identity => identity == credentials.Publisher.ToString() ? new Vector3(near ? 1 : 100, 0, 0) : (Vector3?)null;
                service.ListenerPosition = () => Vector3.zero;
                service.SpeakerResolver = _ => null; // Exercise real fallback ownership first.
                receipt.phase = "publisher-connect";
                publisher = new Room();
                await Join(publisher, credentials.Url, credentials.PublisherToken, ct);
                Require(publisher.Name == credentials.Room && publisher.LocalParticipant.Identity == credentials.Publisher.ToString(), "Publisher scope mismatch");
                var firstOwner = NewOwner(ct);
                receipt.phase = "proximity-listener-connect";
                var first = Start(credentials.ListenerToken, "review-first", firstOwner.Token, credentials.Url);
                Require(await Bounded(first.Operation, ct, receipt.phase), "Candidate rejected initial listener connect");
                active = first.Connection; activeRoom = first.Room;
                AssertAccepted(service, first, credentials.Room, credentials.Listener, "review-first");
                Require(Count(active, "_streams") == 0, "Unexpected stream before synthetic publication");
                SnapshotNow("listener-connected");
                receipt.checks.Add("PASS actual proximity VoiceService accepted a local room with no microphone source or track.");

                receipt.phase = "publish-synthetic-track";
                tone = new Tone();
                track = LocalAudioTrack.CreateAudioTrack("ctlife-review-synthetic-tone", tone, publisher);
                await Instruction(publisher.LocalParticipant.PublishTrack(track, new TrackPublishOptions {
                    Source = TrackSource.SourceMicrophone, AudioEncoding = new AudioEncoding { MaxBitrate = 32000 }
                }), ct);
                await Until(() => Count(active, "_streams") == 1, 12, ct, null, "near-subscription");
                source = Field<GameObject>(active, "_fallbackParent")?.GetComponentInChildren<AudioSource>();
                Require(source != null && Count(active, "_fallbackObjects") == 1, "Near publisher did not create owned fallback source");
                meter = AttachClearingMeter(source);
                tone.Start();
                receipt.phase = "proximity-synthetic-pcm";
                await Until(() => meter.NonzeroSamples > 4800, 12, ct, tone, receipt.phase);
                SnapshotNow(receipt.phase);
                var oldParticipant = activeRoom.RemoteParticipants.Values.Single(p => p.Identity == credentials.Publisher.ToString());
                var oldPublication = oldParticipant.Tracks.Values.Single(p => p.Kind == TrackKind.KindAudio);
                var oldTrack = oldPublication.Track;
                Require(oldTrack is RemoteAudioTrack, "Synthetic publication has no native received audio track");
                receipt.checks.Add("PASS real proximity AudioStream received nonzero synthetic PCM; clearing meter runs after SDK filter, stores no PCM and suppresses output.");

                receipt.phase = "range-unsubscribe";
                source.volume = 0; tone.Stop(); near = false;
                await Until(() => Count(active, "_streams") == 0 && Count(active, "_fallbackObjects") == 0 && source == null, 8, ct, null, receipt.phase);
                Require(!oldPublication.Subscribed, "Out-of-range publication remains subscribed");
                SnapshotNow(receipt.phase);
                near = true;
                receipt.phase = "range-resubscribe";
                await Until(() => Count(active, "_streams") == 1, 12, ct, null, receipt.phase);
                source = Field<GameObject>(active, "_fallbackParent")?.GetComponentInChildren<AudioSource>();
                Require(source != null, "Near-again publisher did not rebuild source");
                meter = AttachClearingMeter(source); tone.Start();
                await Until(() => meter.NonzeroSamples > 4800, 12, ct, tone, "resubscribed-pcm");
                SnapshotNow(receipt.phase);
                receipt.checks.Add("PASS actual range change 1 m -> 100 m -> 1 m unsubscribed, destroyed the owned source, rebuilt it and delivered fresh nonzero PCM.");

                receipt.phase = "accepted-room-replacement";
                source.volume = 0; tone.Stop();
                var oldFallback = Field<GameObject>(first.Connection, "_fallbackParent");
                externalObject = new GameObject("CTLife.Review.VoiceExternalSource");
                var externalSource = externalObject.AddComponent<AudioSource>(); externalSource.volume = 0;
                service.SpeakerResolver = _ => externalSource;
                var secondOwner = NewOwner(ct);
                var second = Start(credentials.SuccessorToken, "review-successor", secondOwner.Token, credentials.Url);
                Require(await Bounded(second.Operation, ct, receipt.phase), "Candidate rejected successor connect");
                active = second.Connection; activeRoom = second.Room;
                AssertAccepted(service, second, credentials.Room, credentials.Successor, "review-successor");
                await Until(() => !first.Room.IsConnected && first.Room.RemoteParticipants.Count == 0 && oldFallback == null &&
                    !HasParticipant(publisher, credentials.Listener), 8, ct, null, "previous-room-drained");
                await Until(() => Count(active, "_streams") == 1, 12, ct, null, "successor-subscription");
                Require(Count(active, "_fallbackObjects") == 0, "Successor ignored supplied external playback source");
                meter = AttachClearingMeter(externalSource); source = externalSource; tone.Start();
                await Until(() => meter.NonzeroSamples > 4800, 12, ct, tone, "successor-pcm");
                SnapshotNow(receipt.phase);
                source.volume = 0; tone.Stop();
                // These calls use our own callback implementation and real old SDK objects, but are intentionally injected.
                receipt.phase = "controlled-old-owned-callbacks";
                int errorsBefore = receipt.candidateErrors, speakingBefore = speakingEvents, activityBefore = activityEvents;
                InvokeOwned(first.Connection, "OnTrackPublished", oldPublication, oldParticipant);
                InvokeOwned(first.Connection, "OnTrackSubscribed", oldTrack, oldPublication, oldParticipant);
                InvokeOwned(first.Connection, "OnTrackUnsubscribed", oldTrack, oldPublication, oldParticipant);
                InvokeOwned(first.Connection, "OnActiveSpeakersChanged", new List<Participant>());
                InvokeOwned(first.Connection, "OnDisconnected", first.Room);
                receipt.controlledOldCallbacks += 5;
                Require(Count(first.Connection, "_streams") == 0 && Count(first.Connection, "_audioPubs") == 0, "Controlled old callbacks repopulated retired connection");
                AssertAccepted(service, second, credentials.Room, credentials.Successor, "review-successor");
                Require(receipt.candidateErrors == errorsBefore && speakingEvents == speakingBefore && activityEvents == activityBefore,
                    "Controlled old callback published a successor-facing signal");
                receipt.checks.Add("PASS five CONTROLLED calls into old owned callback methods left successor room, streams and signals unchanged; this is not a claim of native late callback delivery.");

                receipt.phase = "worker-owner-cancellation";
                await Task.Run(() => secondOwner.Cancel());
                await Until(() => !service.IsConnected && !second.Room.IsConnected && Count(second.Connection, "_streams") == 0 &&
                    !HasParticipant(publisher, credentials.Successor), 8, ct, null, receipt.phase);
                await Bounded(service.Disconnect(), ct, "owner-drain");
                await Bounded(service.Disconnect(), ct, "repeated-disconnect");
                Require(externalSource != null && externalObject != null, "Owned connection destroyed caller-owned source");
                Require(Current(service) == null, "Cancelled accepted lease remains published");
                SnapshotNow(receipt.phase);
                receipt.checks.Add("PASS cancellation from a worker thread retired the actual accepted SDK room on Unity context; repeated Disconnect settled and caller-owned AudioSource survived.");

                // Stop synthetic publication before pending tests. These cases require actual late native Connected events.
                await Instruction(publisher.LocalParticipant.UnpublishTrack(track, true), ct);
                track.Handle.Dispose(); track = null; tone.Dispose(); tone = null;
                service.SpeakerResolver = _ => null;
                receipt.phase = "actual-pending-disconnect";
                var lateOwner = NewOwner(ct);
                var abandoned = Start(credentials.LateToken, "review-abandoned", lateOwner.Token, credentials.Url);
                Require(!abandoned.Operation.IsCompleted, "Late-connect case did not start while pending");
                lateOwner.Cancel();
                await Bounded(service.Disconnect(), ct, "pending-disconnect-return");
                await ExpectCancelled(abandoned.Operation, ct, receipt.phase);
                await Until(() => abandoned.ConnectedEvents > 0 && !abandoned.Room.IsConnected && abandoned.Room.RemoteParticipants.Count == 0 &&
                    !HasParticipant(publisher, credentials.Late), 18, ct, null, receipt.phase);
                receipt.actualLateConnects += abandoned.ConnectedEvents;
                Require(Current(service) == null, "Abandoned native completion became current");
                receipt.checks.Add("PASS ACTUAL pending native Connected completed after cancellation/disconnect, then drained; caller received cancellation and no current room was published.");

                receipt.phase = "actual-pending-replaced";
                var lateOwner2 = NewOwner(ct);
                var superseded = Start(credentials.LateToken, "review-old-pending", lateOwner2.Token, credentials.Url);
                Require(!superseded.Operation.IsCompleted, "Replacement case did not start while pending");
                var finalOwner = NewOwner(ct);
                var final = Start(credentials.ListenerToken, "review-final", finalOwner.Token, credentials.Url);
                Require(await Bounded(final.Operation, ct, "final-connect"), "Pending replacement rejected successor");
                await ExpectCancelled(superseded.Operation, ct, receipt.phase);
                await Until(() => superseded.ConnectedEvents > 0 && !superseded.Room.IsConnected && superseded.Room.RemoteParticipants.Count == 0 &&
                    !HasParticipant(publisher, credentials.Late), 18, ct, null, receipt.phase);
                receipt.actualLateConnects += superseded.ConnectedEvents;
                active = final.Connection; activeRoom = final.Room;
                AssertAccepted(service, final, credentials.Room, credentials.Listener, "review-final");
                SnapshotNow(receipt.phase);
                receipt.checks.Add("PASS ACTUAL superseded pending native Connected drained its old room while the distinct successor remained accepted and connected.");
                await Bounded(service.Disconnect(), ct, "final-disconnect");
                await Bounded(service.Disconnect(), ct, "final-repeat-disconnect");
                await Until(() => !final.Room.IsConnected && !HasParticipant(publisher, credentials.Listener), 8, ct, null, "final-roster-leave");
                publisher.Disconnect();
                Require(!publisher.IsConnected && publisher.RemoteParticipants.Count == 0, "Publisher did not release local native room");
                Require(receipt.runtimeErrors == 0 && receipt.candidateErrors == 0, "Runtime or candidate error occurred");
                receipt.phase = "complete"; receipt.pass = true;
            }
            catch (Exception error)
            {
                receipt.failureType = error is OperationCanceledException ? "CancelledOrDeadline" : error.GetType().Name;
                try { SnapshotNow("failure:" + receipt.phase); } catch { receipt.checks.Add("Failure snapshot unavailable."); }
                if (error is InvalidOperationException && error.Message.StartsWith("Voice native review:", StringComparison.Ordinal)) receipt.checks.Add(error.Message);
            }
            finally
            {
                void Cleanup(Action action)
                { try { action(); } catch (Exception error) { receipt.cleanupFailures++; receipt.checks.Add("Cleanup failure: " + error.GetType().Name); } }
                Cleanup(() => tone?.Stop());
                foreach (var lifetime in owners) Cleanup(lifetime.Cancel);
                try { await Bounded(service.Disconnect(), CancellationToken.None, "cleanup-disconnect", 12); }
                catch (Exception error) { receipt.cleanupFailures++; receipt.checks.Add("Cleanup disconnect: " + error.GetType().Name); }
                // Retain every connection's own late guard until its operation settles. Do not silently abandon pending SDK work.
                foreach (var captured in captures)
                {
                    if (captured.Operation != null)
                    {
                        try { await Bounded(captured.Operation, CancellationToken.None, "cleanup-pending-drain", 20); }
                        catch (OperationCanceledException) when (captured.Operation.IsCompleted) { }
                        catch (Exception error) { receipt.cleanupFailures++; receipt.checks.Add("Cleanup pending drain: " + error.GetType().Name); Observe(captured.Operation); }
                    }
                    if (captured.Room != null)
                    {
                        captured.Room.Connected -= captured.ConnectedObserver;
                        Cleanup(captured.Room.Disconnect);
                    }
                }
                Cleanup(() => publisher?.Disconnect()); Cleanup(() => track?.Handle.Dispose()); Cleanup(() => tone?.Dispose());
                Cleanup(() => { if (externalObject != null) UnityEngine.Object.Destroy(externalObject); });
                foreach (var lifetime in owners) Cleanup(lifetime.Dispose);
                Cleanup(() => activitySubscription?.Invoke());
                service.Error -= CandidateError; service.LocalSpeakingChanged -= Speaking;
                VoiceService.ReviewConnectionCreated -= Created;
                receipt.observerSubscriptionsAfter = ObserverCount();
                receipt.observerRemoved = receipt.observerSubscriptionsAfter == receipt.observerSubscriptionsBefore;
                await Awaitable.NextFrameAsync();
                receipt.externalSourceDestroyed = externalObject == null;
                receipt.createdConnections = captures.Count;
                Application.runInBackground = background; receipt.backgroundRestored = Application.runInBackground == background;
                Services.TryGet<VoiceService>(out var afterService); receipt.servicesUnchanged = ReferenceEquals(afterService, originalService);
                if (redactor != null && ReferenceEquals(Debug.unityLogger.logHandler, redactor)) Debug.unityLogger.logHandler = priorLogger;
                receipt.loggerRestored = ReferenceEquals(Debug.unityLogger.logHandler, priorLogger);
                Application.logMessageReceived -= RuntimeError;
                receipt.pass &= receipt.cleanupFailures == 0 && receipt.runtimeErrors == 0 && receipt.candidateErrors == 0 && receipt.backgroundRestored && receipt.servicesUnchanged && receipt.loggerRestored && receipt.observerRemoved && receipt.externalSourceDestroyed;
                File.WriteAllText(Path.Combine(receiptDirectory, "voice-native-review.json"), JsonUtility.ToJson(receipt, true));
            }
            Require(receipt.pass, "Failed; inspect sanitized receipt phase and checks");
            Log.Info("voice-native-review", "PASS local proximity PCM/range/replacement/worker cancellation and two actual late connects; controlled callbacks labelled separately; no microphone/game API calls.");
        }

        private static int ObserverCount() => (typeof(VoiceService).GetField("ReviewConnectionCreated", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
        private static T Field<T>(object value, string name) where T : class => value?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(value) as T;
        private static int Count(object value, string name) => Field<IDictionary>(value, name)?.Count ?? 0;
        private static object Current(VoiceService service)
        { var slot = Field<object>(service, "_connections"); return slot?.GetType().GetProperty("Current")?.GetValue(slot); }
        private static bool HasParticipant(Room room, Guid identity) => room.RemoteParticipants.Values.Any(p => p.Identity == identity.ToString());
        private static void InvokeOwned(object connection, string name, params object[] args)
        {
            Require(connection?.GetType().Assembly == typeof(VoiceService).Assembly && connection.GetType().FullName == "CTLife.Net.VoiceConnection", "Callback target is not our owned connection");
            var method = connection.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(method != null, "Expected owned callback does not exist"); method.Invoke(connection, args);
        }
        private static void AssertAccepted(VoiceService service, Capture captured, string room, Guid identity, string label)
        {
            Require(service.IsConnected && ReferenceEquals(Current(service), captured.Connection) && service.RoomName == label, "Wrong accepted connection or label");
            Require(captured.Room.IsConnected && captured.Room.Name == room && captured.Room.LocalParticipant.Identity == identity.ToString(), "Native accepted room/identity mismatch");
            Require(!service.MicOn && Field<object>(captured.Connection, "_micSource") == null && Field<object>(captured.Connection, "_micTrack") == null && Field<GameObject>(captured.Connection, "_micObject") == null,
                "Listener unexpectedly allocated microphone capture");
        }
        private static SpacesReviewMeter AttachClearingMeter(AudioSource source)
        {
            Require(source != null, "Missing synthetic playback source"); source.volume = 0;
            var meter = source.gameObject.AddComponent<SpacesReviewMeter>();
            var components = source.GetComponents<MonoBehaviour>();
            int sdk = Array.FindLastIndex(components, component => component.GetType().FullName == "LiveKit.AudioProbe");
            Require(sdk >= 0 && Array.IndexOf(components, meter) > sdk && meter.isActiveAndEnabled,
                "Clearing meter must follow native receive filter before synthetic gain is enabled");
            source.volume = 1; return meter;
        }
        private static async Task Join(Room room, string url, string token, CancellationToken ct)
        {
            bool complete = false;
            try { await Instruction(room.Connect(url, token, new LiveKit.RoomOptions { AutoSubscribe = false, JoinRetries = 0 }), ct); complete = true; }
            finally
            {
                if (!complete)
                {
                    void Abandoned(Room connected) { connected.Connected -= Abandoned; connected.Disconnect(); }
                    room.Connected += Abandoned; room.Disconnect();
                }
            }
        }
        private static async Task Instruction(LiveKit.Internal.Threading.YieldInstruction instruction, CancellationToken ct)
        { await Bounded(Runner.Await(instruction), ct, "native-instruction"); Require(!instruction.IsError, "Native SDK instruction returned an error"); }
        private static async Task Bounded(Task operation, CancellationToken ct, string phase, double seconds = 22)
        { await Until(() => operation.IsCompleted, seconds, ct, null, phase); await operation; }
        private static async Task<bool> Bounded(Task<bool> operation, CancellationToken ct, string phase, double seconds = 22)
        { await Bounded((Task)operation, ct, phase, seconds); return await operation; }
        private static async Task ExpectCancelled(Task<bool> operation, CancellationToken ct, string phase)
        {
            try { await Bounded(operation, ct, phase); }
            catch (OperationCanceledException) when (operation.IsCanceled) { return; }
            throw new InvalidOperationException("Voice native review: Abandoned connection did not report task cancellation");
        }
        private static async Task Until(Func<bool> condition, double seconds, CancellationToken ct, Tone tone, string phase)
        {
            var end = Time.realtimeSinceStartupAsDouble + seconds; var next = Time.realtimeSinceStartupAsDouble;
            while (!condition())
            {
                ct.ThrowIfCancellationRequested(); Require(Time.realtimeSinceStartupAsDouble < end, "Timed out at " + phase);
                var now = Time.realtimeSinceStartupAsDouble;
                for (int i = 0; i < 4 && now >= next; i++, next += .02) tone?.Pump();
                if (now - next > .2) next = now;
                await Awaitable.NextFrameAsync(ct);
            }
        }
        private static async void Observe(Task operation) { try { await operation; } catch { } }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Voice native review: " + message); }
        private static Credentials ReadCredentials(string path)
        {
            Require(Path.GetFullPath(path).StartsWith("/tmp/", StringComparison.Ordinal) || Path.GetFullPath(path).StartsWith("/private/tmp/", StringComparison.Ordinal), "Tokens must live outside the repository in /tmp");
            var json = JObject.Parse(File.ReadAllText(path));
            var result = new Credentials { Url = (string)json["url"], Room = (string)json["room"], PublisherToken = (string)json["publisherToken"], ListenerToken = (string)json["listenerToken"], LateToken = (string)json["lateToken"], SuccessorToken = (string)json["successorToken"] };
            Require(result.Url == "ws://127.0.0.1:7880", "Only the approved local server is allowed");
            Require(result.Room != null && result.Room.StartsWith("ctlife-review-", StringComparison.Ordinal) && Guid.TryParse(result.Room.Substring("ctlife-review-".Length), out _), "A uniquely named review room is required");
            result.Publisher = ValidateToken(result.PublisherToken, result.Room, true);
            result.Listener = ValidateToken(result.ListenerToken, result.Room, false);
            result.Late = ValidateToken(result.LateToken, result.Room, false);
            result.Successor = ValidateToken(result.SuccessorToken, result.Room, false);
            Require(new[] { result.Publisher, result.Listener, result.Late, result.Successor }.Distinct().Count() == 4, "Review identities must be distinct");
            return result;
        }
        private static Guid ValidateToken(string token, string room, bool publisher)
        {
            var parts = token?.Split('.'); Require(parts != null && parts.Length == 3, "Malformed review token");
            var payload = parts[1].Replace('-', '+').Replace('_', '/'); payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            var claims = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            Require(Guid.TryParse((string)claims["sub"], out var identity) && identity != Guid.Empty, "Review identity must be a fresh UUID");
            Require(((string)claims["name"])?.StartsWith("ctlife-review-", StringComparison.Ordinal) == true, "Display name must identify the review");
            var video = claims["video"];
            Require((string)video?["room"] == room && (bool?)video?["roomJoin"] == true && (bool?)video?["canPublish"] == publisher && (bool?)video?["canSubscribe"] == true && (bool?)video?["canPublishData"] == false,
                "Token is not restricted to required room and role");
            Require((bool?)video?["roomAdmin"] != true && (bool?)video?["roomCreate"] != true && (bool?)video?["roomList"] != true,
                "Review token must not have administrative grants");
            var expires = DateTimeOffset.FromUnixTimeSeconds((long)claims["exp"]).UtcDateTime;
            Require(expires > DateTime.UtcNow.AddMinutes(4) && expires < DateTime.UtcNow.AddHours(1), "Use newly issued short-lived tokens");
            return identity; // SDK/server verifies signature; this only constrains review intent.
        }
    }
}
#endif
