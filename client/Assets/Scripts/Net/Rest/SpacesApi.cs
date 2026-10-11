using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Rest.Model;
using Newtonsoft.Json.Linq;

namespace CTLife.Net
{
    /// <summary>Imported contract 1.0.0-rc1. No capability is inferred from a successful token mint.</summary>
    public sealed class SpacesApi
    {
        private readonly IRestClient _rest;
        public SpacesApi(IRestClient rest) { _rest = rest; }
        public Task<List<Space>> Live(CancellationToken ct) => SpacesRequest.Run(c => _rest.Get<List<Space>>("/spaces?live=true", c), ct);
        public Task<Space> Get(Guid id, CancellationToken ct) => SpacesRequest.Run(c => _rest.Get<Space>(Path(id), c), ct);
        public Task<Space> Create(string title, string key, CancellationToken ct) => SpacesRequest.Run(c =>
            _rest.Post<Space>("/spaces", new JObject { ["title"] = title }, idempotencyKey: key, ct: c), ct);
        public Task<NoContent> Hand(Guid id, bool raised, CancellationToken ct) => SpacesRequest.Run(c => raised
            ? _rest.Post<NoContent>(Path(id) + "/hand", ct: c)
            : _rest.Delete<NoContent>(Path(id) + "/hand", c), ct);
        public Task<NoContent> Promote(Guid id, Guid user, CancellationToken ct) => SpacesRequest.Run(c =>
            _rest.Post<NoContent>(Path(id) + "/speakers", new JObject { ["userId"] = user.ToString() }, ct: c), ct);
        public Task<NoContent> Demote(Guid id, Guid user, CancellationToken ct) => SpacesRequest.Run(c =>
            _rest.Delete<NoContent>(Path(id) + "/speakers/" + user.ToString(), c), ct);
        public Task<NoContent> End(Guid id, CancellationToken ct) => SpacesRequest.Run(c =>
            _rest.Post<NoContent>(Path(id) + "/end", ct: c), ct);
        private static string Path(Guid id)
        {
            if (id == Guid.Empty) throw new ArgumentException("Missing space ID.");
            return "/spaces/" + id.ToString();
        }
    }

    public static class SpacesRequest
    {
        // Also bounds a transport stalled in auth refresh, which does not accept cancellation.
        public static async Task<T> Run<T>(Func<CancellationToken, Task<T>> request, CancellationToken owner, int timeoutMs = 45000)
        {
            owner.ThrowIfCancellationRequested();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner);
            var work = request(linked.Token);
            var deadline = Task.Delay(timeoutMs, linked.Token);
            try
            {
                if (await Task.WhenAny(work, deadline) != work)
                {
                    linked.Cancel();
                    Observe(work);
                    owner.ThrowIfCancellationRequested();
                    throw new TimeoutException("Spaces request confirmation timed out.");
                }
                var result = await work;
                owner.ThrowIfCancellationRequested();
                return result;
            }
            finally { linked.Cancel(); }
        }
        private static async void Observe(Task work) { try { await work; } catch { /* Caller already received cancellation/timeout. */ } }
    }

    public static class SpacesPolicy
    {
        public const int SpeakerLimit = 10; // Build spec: ten speakers plus the host.
        public static bool Valid(Space room) => room != null && room.SpaceId != Guid.Empty && room.HostId != Guid.Empty
            && !string.IsNullOrWhiteSpace(room.Title) && room.SpeakerIds != null && room.HandIds != null
            && room.ListenerCount >= 0 && room.SpeakerIds.All(x => x != Guid.Empty) && room.HandIds.All(x => x != Guid.Empty);
        public static bool IsHost(Space room, Guid user) => Valid(room) && user != Guid.Empty && room.HostId == user;
        public static bool CanSpeak(Space room, Guid user) => Valid(room) && user != Guid.Empty
            && (room.HostId == user || room.SpeakerIds.Contains(user));
        public static List<Guid> Speakers(Space room) => Valid(room)
            ? room.SpeakerIds.Where(x => x != room.HostId).Distinct().ToList() : new List<Guid>();
        public static List<Guid> Hands(Space room)
        {
            if (!Valid(room)) return new List<Guid>();
            var stage = new HashSet<Guid>(room.SpeakerIds) { room.HostId };
            return room.HandIds.Where(x => !stage.Contains(x)).Distinct().ToList();
        }
        public static bool CanPromote(Space room, Guid actor, Guid target) => IsHost(room, actor)
            && target != Guid.Empty && Hands(room).Contains(target) && Speakers(room).Count < SpeakerLimit;
        public static bool RouteUnavailable(Exception e) => e is ApiException a && (a.Status == 404 || a.Status == 501);
    }

    /// <summary>Monotonic role freshness and explicit-gesture ownership across permission/publish awaits.</summary>
    public sealed class SpacesPublishGate
    {
        private bool _allowed;
        private double _until;
        private int _generation;
        public void Refresh(bool allowed, double now)
        {
            if (!allowed) { Invalidate(); return; }
            _allowed = true; _until = now + 8;
        }
        public void Invalidate() { _allowed = false; _until = 0; CancelAttempt(); }
        public void CancelAttempt() { _generation++; }
        public bool CanBegin(double now) => _allowed && now < _until;
        public int Begin(double now)
        {
            if (!CanBegin(now)) throw new InvalidOperationException("A fresh speaker role is required.");
            return ++_generation;
        }
        public bool IsCurrent(int attempt, double now) => attempt == _generation && CanBegin(now);
    }
}
