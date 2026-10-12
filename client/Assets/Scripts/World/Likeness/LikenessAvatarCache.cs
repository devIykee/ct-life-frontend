using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.World.Likeness
{
    /// <summary>Accepted, owner-scoped runtime templates. Clones retain import resources until their actual destruction.</summary>
    public static class LikenessAvatarCache
    {
        internal sealed class Entry { internal LikenessLoadedAvatar asset; internal string owner; internal int leases; internal bool retired; }
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        private static Session _session;
        public static bool IsPrepared(string jobId) => !string.IsNullOrEmpty(jobId) && Entries.TryGetValue(jobId, out var entry) && !entry.retired && entry.owner == Owner() && entry.asset.CanEquip;
        private static string Owner() => Services.TryGet<Session>(out var session) ? session.UserId : null;
        public static async Task PrepareOwned(string jobId, CancellationToken ct = default)
        {
            LikenessClient.Id(jobId);
            if (IsPrepared(jobId)) return;
            var session = Services.Get<Session>(); var owner = session.UserId;
            var client = new LikenessClient(Services.Get<Api>().Rest, session);
            var jobs = await client.Jobs(ct);
            bool equipped = Services.TryGet<PlayerProfile>(out var profile) && profile.LikenessJobId == jobId;
            if (!equipped && jobs.Value<string>("acceptedJobId") != jobId) throw new InvalidDataException("Character has not been accepted by this account");
            var manifest = await client.Manifest(jobId, ct); var bytes = await client.Model(jobId, ct);
            if (session.UserId != owner) throw new OperationCanceledException("Account changed", ct);
            await PrepareAccepted(manifest, bytes.bytes, ct);
        }
        /// <summary>Caller has received and checked the server acceptance receipt, or authoritative equipped profile.</summary>
        public static async Task PrepareAccepted(LikenessAssetManifest manifest, byte[] bytes, CancellationToken ct = default)
        {
            var owner = Owner(); if (string.IsNullOrEmpty(owner)) throw new InvalidOperationException("Sign in before equipping a character");
            if (IsPrepared(manifest?.jobId)) return;
            var asset = await LikenessAvatarLoader.Load(bytes, manifest, requireHumanoid: true, ct: ct);
            if (Owner() != owner) { asset.Dispose(); throw new OperationCanceledException("Account changed", ct); }
            asset.Instance.SetActive(false); Object.DontDestroyOnLoad(asset.Instance);
            if (Entries.TryGetValue(manifest.jobId, out var prior)) Retire(prior);
            // Keep one prepared appearance; existing live actor leases retain a retired template until destruction.
            foreach (var pair in new List<KeyValuePair<string, Entry>>(Entries))
                if (pair.Key != manifest.jobId) { Retire(pair.Value); Entries.Remove(pair.Key); }
            Entries[manifest.jobId] = new Entry { asset = asset, owner = owner };
            var session = Services.Get<Session>();
            if (!ReferenceEquals(_session, session))
            { if (_session != null) _session.Changed -= AccountChanged; _session = session; _session.Changed += AccountChanged; }
        }
        public static bool TryInstantiate(string jobId, Transform parent, out LikenessAvatarLease lease)
        {
            lease = null;
            if (!IsPrepared(jobId)) return false;
            var entry = Entries[jobId];
            var instance = Object.Instantiate(entry.asset.Instance, parent, false);
            instance.name = "Personal character";
            entry.leases++;
            lease = new LikenessAvatarLease(instance, entry);
            instance.AddComponent<LikenessLeaseLifetime>().Bind(lease);
            instance.SetActive(true);
            return true;
        }
        internal static void Release(Entry entry)
        {
            entry.leases = Math.Max(0, entry.leases - 1);
            if (entry.retired && entry.leases == 0) entry.asset.Dispose();
        }
        private static void Retire(Entry entry) { entry.retired = true; if (entry.leases == 0) entry.asset.Dispose(); }
        private static void AccountChanged()
        {
            string owner = Owner(); var remove = new List<string>();
            foreach (var pair in Entries) if (pair.Value.owner != owner) { Retire(pair.Value); remove.Add(pair.Key); }
            foreach (var key in remove) Entries.Remove(key);
        }
    }
    public sealed class LikenessAvatarLease : IDisposable
    {
        private LikenessAvatarCache.Entry _entry;
        public GameObject Instance { get; private set; }
        public Animator Animator => Instance == null ? null : Instance.GetComponentInChildren<Animator>();
        public LikenessAssetManifest Manifest => _entry?.asset.Manifest;
        internal LikenessAvatarLease(GameObject instance, LikenessAvatarCache.Entry entry) { Instance = instance; _entry = entry; }
        public void Dispose() { if (Instance != null) { Instance.SetActive(false); Object.Destroy(Instance); } }
        internal void Destroyed() { Instance = null; if (_entry != null) { var entry = _entry; _entry = null; LikenessAvatarCache.Release(entry); } }
    }
    internal sealed class LikenessLeaseLifetime : MonoBehaviour
    {
        private LikenessAvatarLease _lease;
        internal void Bind(LikenessAvatarLease lease) { _lease = lease; }
        private void OnDestroy() { _lease?.Destroyed(); _lease = null; }
    }
}
