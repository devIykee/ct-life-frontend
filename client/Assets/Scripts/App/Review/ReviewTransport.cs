#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using CTLife.UI;

namespace CTLife.App.Review
{
    /// <summary>
    /// Read-only visual fixture. Selected explicitly with review=1 in development builds only.
    /// No network, account, session tokens, simulated transactions or persistence.
    /// </summary>
    internal sealed class ReviewTransport : IRestClient
    {
        private readonly Me _profile;
        public Func<Task<bool>> RefreshHandler { get; set; }

        public ReviewTransport(Registries reg)
        {
            var choices = CreatorLookCatalog.Available();
            var requested = LaunchArgs.Get("reviewLook", "");
            var look = (string.IsNullOrEmpty(requested) ? choices.FirstOrDefault() :
                choices.FirstOrDefault(c => c.labelKey == requested))?.look;
            if (look == null) throw new InvalidOperationException("Visual review needs an imported authored character look.");
            Log.Info("review", "Authored profile look: " + string.Join("|", look));
            bool textStress = LaunchArgs.Flag("reviewTextStress");
            _profile = new Me(
                new User(Guid.Parse("00000000-0000-4000-8000-000000000001"), "visual-review-only", true, true),
                new Character(textStress ? new string('W', 20) : "Amara", new Look(look[0], look[1], look[2], look[3], look[4], look[5], look[6]), reg.TribeOrder.FirstOrDefault()?.id,
                    reg.Traits.Take(2).Select(t => t.id).ToList(), reg.Dreams.FirstOrDefault()?.id),
                new Needs(82, 67, 91, 74, 88), new Skills(8, 4, 9, 7, 3, 2),
                textStress ? 123456789000L : 248750L, 0, reg.Homes.FirstOrDefault()?.id, flags: new Dictionary<string, object> { ["lotteryRolled"] = true });
        }

        public Task<T> Get<T>(string path, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (path == "/me" && typeof(T) == typeof(Me)) return Task.FromResult((T)(object)_profile);
            return Unavailable<T>();
        }

        private static Task<T> Unavailable<T>() => Task.FromException<T>(
            new InvalidOperationException("Visual review uses sample data. This action needs a connected game session."));

        public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Unavailable<T>();
        public Task<T> Put<T>(string path, object body, CancellationToken ct = default) => Unavailable<T>();
        public Task<T> Patch<T>(string path, object body, CancellationToken ct = default) => Unavailable<T>();
        public Task<T> Delete<T>(string path, CancellationToken ct = default) => Unavailable<T>();
        public Task<T> Send<T>(string method, string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) =>
            method == "GET" ? Get<T>(path, ct) : Unavailable<T>();
        public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => Unavailable<RawResponse>();
    }
}
#endif
