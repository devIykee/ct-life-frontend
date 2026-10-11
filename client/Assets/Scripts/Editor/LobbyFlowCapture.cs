using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CTLife.App;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Isolated actual lobby + phone capture: OnboardingCapture.Launch -ctlife-captureSet lobby.
    /// No account, HTTP, wallet signing, writes or scene loading. Uses real stock prefabs and PhoneCatalog apps.
    /// Run in the throwaway capture Editor, never over a running user game. Restores registered service references.
    /// </summary>
    internal static class LobbyFlowCapture
    {
        private const string Owner = "00000000-0000-4000-8000-000000000091";
        private const string Wallet = "11111111111111111111111111111111";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private sealed class Saved<T> : IDisposable where T : class
        {
            private readonly T old;
            private readonly bool existed;
            public Saved(T replacement) { existed = Services.TryGet(out old); Services.Register(replacement); }
            public void Dispose() { if (existed) Services.Register(old); else Services.Unregister<T>(); }
            public bool Restored => Services.TryGet<T>(out var current) ? existed && ReferenceEquals(old, current) : !existed;
        }

        private sealed class Transport : IRestClient
        {
            public JObject Profile;
            public readonly List<string> Reads = new List<string>();
            public int Writes;
            public Func<Task<bool>> RefreshHandler { get; set; }
            public Task<T> Get<T>(string path, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested(); Reads.Add(path);
                JToken value;
                switch (path)
                {
                    case "/me": value = Profile.DeepClone(); break;
                    case "/careers": case "/gigs": case "/homes": case "/nfts/me": value = new JArray(); break;
                    case "/homes/me/settings": value = new JObject { ["visitors"] = "open" }; break;
                    default: return Task.FromException<T>(new ApiException(404, "NOT_FOUND", "No offline fixture for " + path));
                }
                return Task.FromResult(value is T exact ? exact : value.ToObject<T>(Newtonsoft.Json.JsonSerializer.Create(Json.Settings)));
            }
            private Task<T> Reject<T>() { Writes++; return Task.FromException<T>(new InvalidOperationException("Capture attempted a mutation.")); }
            public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Reject<T>();
            public Task<T> Put<T>(string path, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Patch<T>(string path, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Delete<T>(string path, CancellationToken ct = default) => Reject<T>();
            public Task<T> Send<T>(string method, string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => method == "GET" ? Get<T>(path, ct) : Reject<T>();
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => Reject<RawResponse>();
        }

        private sealed class WalletRpc : IWalletBalanceRpc
        {
            public bool Fail, Large;
            public int Reads;
            public Task<JObject> Read(SolanaCluster cluster, string method, JArray parameters, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); Reads++;
                if (Fail) return Task.FromException<JObject>(new WalletBalanceReadException("networkUnavailable"));
                JToken value;
                if (method == "getBalance") value = Large ? 12345678901234567890UL : 12345678900UL;
                else if (method == "getAccountInfo") value = new JObject
                {
                    ["owner"] = WalletBalanceParser.TokenProgram,
                    ["data"] = new JObject { ["parsed"] = new JObject { ["type"] = "mint",
                        ["info"] = new JObject { ["isInitialized"] = true, ["decimals"] = 6 } } }
                };
                else if (method == "getTokenAccountsByOwner") value = new JArray(new JObject
                {
                    ["pubkey"] = Wallet,
                    ["account"] = new JObject { ["owner"] = WalletBalanceParser.TokenProgram,
                        ["data"] = new JObject { ["parsed"] = new JObject { ["type"] = "account", ["info"] = new JObject
                        {
                            ["owner"] = (string)parameters[0], ["mint"] = (string)parameters[1]["mint"],
                            ["tokenAmount"] = new JObject { ["amount"] = Large ? "12345678901234567890" : "928500000", ["decimals"] = 6 }
                        } } } }
                });
                else throw new InvalidOperationException("Unexpected wallet fixture read.");
                return Task.FromResult(new JObject { ["context"] = new JObject { ["slot"] = 1234 }, ["value"] = value });
            }
        }

        public static async Task Run(Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, Action<string> step, JObject receipt)
        {
            var config = Services.Get<ConfigStore>();
            var transport = new Transport();
            var api = new Api(transport, config);
            var profile = new PlayerProfile(api);
            var session = new Session();
            // The public SetTokens API persists credentials. This isolated session has no tokens and is never saved.
            typeof(Session).GetProperty(nameof(Session.UserId)).SetValue(session, Owner);
            var rpc = new WalletRpc();
            var now = DateTimeOffset.UtcNow;
            string connectedWallet = Wallet;
            var currencies = new JObject();
            foreach (var symbol in new[] { "USDC", "SKR", "ORE" }) currencies[symbol] = new JObject { ["mint"] = Wallet };
            using var wallet = new WalletBalanceService(() => connectedWallet, () => currencies, rpc, now: () => now);
            var savedSession = new Saved<Session>(session);
            var savedRest = new Saved<IRestClient>(transport);
            var savedApi = new Saved<Api>(api);
            var savedProfile = new Saved<PlayerProfile>(profile);
            var savedWallet = new Saved<WalletBalanceService>(wallet);
            var report = new JObject
            {
                ["scope"] = "Actual DashboardScreen, AvatarPreview, PhoneShell and PhoneCatalog apps; isolated profile and read-only in-memory REST/RPC fixtures. No backend, signing or account changes.",
                ["layouts"] = new JArray(), ["apps"] = new JArray()
            };
            receipt["lobbyFlow"] = report;
            DashboardScreen home = null;
            PhoneShell phone = null;
            var watermark = new Label("REVIEW · LOCAL FIXTURES") { pickingMode = PickingMode.Ignore };
            watermark.style.position = Position.Absolute; watermark.style.bottom = 3f;
            watermark.style.left = Length.Percent(50); watermark.style.marginLeft = -160f;
            watermark.style.width = 320f; watermark.style.height = 24f; watermark.style.fontSize = 16f;
            watermark.style.unityTextAlign = TextAnchor.MiddleCenter; watermark.style.color = new Color(.23f, .08f, .25f);
            watermark.style.backgroundColor = Color.white;
            UiRoot.Instance.Root.Add(watermark);
            int runtimeErrors = 0;
            void OnLog(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeErrors++; }
            Application.logMessageReceived += OnLog;
            try
            {
                await wallet.Refresh();
                expect(wallet.State.Status == WalletBalanceStatus.Ready, "wallet fixture is parsed through the production service");
                phone = new PhoneShell(UiRoot.Instance.PhoneLayer);
                PhoneCatalog.Register(phone, _ => { }, () => { });
                int i = 0;
                foreach (var head in new[] { "head_snow", "head_rain", "head_ubc_m", "head_hero_gynoid" })
                {
                    home?.Close();
                    transport.Profile = Profile(head, i == 0 ? "Amara" : head == "head_hero_gynoid" ? "joel" : "city_regular");
                    await profile.Refresh();
                    home = new DashboardScreen(() => { }, id => phone.Open(id)) { OfflineOverride = false };
                    home.Show();
                    Bus.Publish(new MoodSignal { bps = 420 });
                    await shot("lobby-0" + (++i) + "-" + head, 1.6f);
                    Inspect(home, head, report, expect);
                    var preview = (AvatarPreview)typeof(DashboardScreen).GetField("_preview", Private).GetValue(home);
                    var actor = (AvatarView)typeof(AvatarPreview).GetField("_avatar", Private).GetValue(preview);
                    var animator = actor.Animator;
                    if (animator != null)
                    {
                        actor.enabled = false; animator.speed = 0f;
                        var cameraBefore = preview.Camera.transform.position;
                        foreach (var pose in new[] { "emote_wave", "idle_fidget_stretch" })
                        {
                            if (!animator.HasState(0, Animator.StringToHash(pose))) continue;
                            preview.PlayReaction(pose);
                            expect(Vector3.Distance(cameraBefore, preview.Camera.transform.position) < .0001f, head + ": reaction retains precomputed camera framing " + pose);
                            animator.speed = 0f;
                            animator.Play(pose, 0, .52f); animator.Update(0f);
                            await shot("lobby-0" + i + "-" + head + "-" + pose, .1f);
                            Inspect(home, head + "-" + pose, report, expect);
                        }
                    }
                }
                home.Close();
                transport.Profile = Profile("head_rain", new string('W', 20), 123456789000L, 72000, 7, 22);
                await profile.Refresh();
                rpc.Large = true; now += TimeSpan.FromSeconds(31); await wallet.Refresh();
                home = new DashboardScreen(() => { }, id => phone.Open(id)) { OfflineOverride = false };
                home.Show();
                await shot("lobby-05-long-identity-debt-large-wallet", 1.6f);
                Inspect(home, "long-identity-debt-large-wallet", report, expect);
                home.TapChip("energy");
                await shot("lobby-05b-energy-explainer", .4f);
                var popover = home.Element.Q<CTLife.UI.Kit.Popover>();
                var popoverCard = popover?.Q(className: "kit-pop__card");
                expect(popover != null && popover.IsOpen && popoverCard != null && Contains(home.Element.worldBound, popoverCard.worldBound, 1f),
                    "bottom energy explainer remains fully inside the lobby");
                if (popoverCard != null) report["energyExplainerBounds"] = Box(popoverCard.worldBound);
                popover?.Hide();
                rpc.Fail = true; now += TimeSpan.FromSeconds(31); await wallet.Refresh();
                await shot("lobby-06-wallet-stale", .3f);
                expect(wallet.State.Tokens.All(t => t.IsStale), "failed refresh preserves explicitly stale observed wallet values");
                Inspect(home, "wallet-stale", report, expect);
                connectedWallet = null; wallet.Invalidate();
                await shot("lobby-07-wallet-disconnected", .3f);
                expect(wallet.State.Tokens.All(t => !t.Amount.HasValue), "disconnected wallet exposes no previous account holdings");
                Inspect(home, "wallet-disconnected", report, expect);

                foreach (var walletCase in new[] { "ready", "large", "unavailable", "disconnected" })
                {
                    connectedWallet = walletCase == "disconnected" ? null : Wallet;
                    rpc.Fail = walletCase == "unavailable"; rpc.Large = walletCase == "large";
                    now += TimeSpan.FromSeconds(31); wallet.Invalidate(); await wallet.Refresh();
                    phone.Open("wallet");
                    await shot("lobby-wallet-" + walletCase, .4f);
                    var page = UiRoot.Instance.PhoneLayer.Q(className: "ct-wallet-page");
                    expect(page != null, walletCase + ": actual registered WalletApp opened");
                    if (page != null)
                    {
                        var amounts = page.Query<Label>(className: "ct-wallet-amount").ToList();
                        expect(amounts.Count == (walletCase == "disconnected" ? 0 : 4), walletCase + ": expected wallet token rows");
                        foreach (var amount in amounts)
                        {
                            var measured = amount.MeasureTextSize(amount.text, amount.contentRect.width, VisualElement.MeasureMode.AtMost, 0, VisualElement.MeasureMode.Undefined);
                            expect(measured.x <= amount.contentRect.width + 2f && measured.y <= amount.contentRect.height + 2f,
                                walletCase + ": full-precision wallet amount fits its actual text area");
                        }
                        ((JArray)report["apps"]).Add(new JObject { ["app"] = "wallet-" + walletCase, ["amounts"] = new JArray(amounts.Select(a => a.text)) });
                    }
                    phone.Close();
                }

                // The production shell keeps the lobby underneath, with its real outside-dismiss scrim.
                foreach (var app in new[] { "phone", "map", "jobs", "bank", "profile", "home", "settings" })
                {
                    phone.Open(app == "phone" ? null : app);
                    await shot("lobby-app-" + app, .8f);
                    var frame = UiRoot.Instance.PhoneLayer.Q(className: "ct-phone");
                    expect(phone.IsOpen && frame != null && frame.worldBound.width > 0, app + ": real phone frame rendered");
                    if (frame != null)
                    {
                        var labels = frame.Query<Label>().ToList().Where(l => !string.IsNullOrEmpty(l.text)).Select(l => l.text).ToArray();
                        expect(labels.Length > 2, app + ": actual page populated");
                        expect(!labels.Contains(Strings.T("phone.error")), app + ": app did not fall into PhoneShell exception page");
                        ((JArray)report["apps"]).Add(new JObject { ["app"] = app, ["frame"] = Box(frame.worldBound), ["labels"] = new JArray(labels.Take(8)) });
                    }
                    phone.Close();
                }
                await shot("lobby-08-phone-closed", .3f);
                expect(!phone.IsOpen, "phone closes back to lobby");
                Inspect(home, "phone-closed", report, expect);
                expect(transport.Writes == 0, "zero fixture REST mutations attempted");
                expect(runtimeErrors == 0, "zero runtime errors during actual lobby/phone render");
                step("lobby + actual phone apps captured");
            }
            finally
            {
                phone?.Dispose(); home?.Close();
                watermark.RemoveFromHierarchy();
                savedWallet.Dispose(); savedProfile.Dispose(); savedApi.Dispose(); savedRest.Dispose(); savedSession.Dispose();
                bool restored = savedWallet.Restored && savedProfile.Restored && savedApi.Restored && savedRest.Restored && savedSession.Restored;
                report["servicesRestored"] = restored;
                report["restReads"] = new JArray(transport.Reads); report["restWrites"] = transport.Writes;
                report["rpcReads"] = rpc.Reads; report["runtimeErrors"] = runtimeErrors;
                Application.logMessageReceived -= OnLog;
                expect(restored, "capture restored original service references");
            }
        }

        private static JObject Profile(string head, string alias, long cash = 300000, long debt = 0, int energy = 82, int sanity = 88)
        {
            var look = CreatorLookCatalog.Available().FirstOrDefault(c => c.look != null && c.look[1] == head)?.look;
            if (look == null) throw new InvalidOperationException("Missing actual character fixture " + head);
            if (head == "head_rain" && CharacterPaletteResolver.TryChange(look, "top", "red", out var colored)) look = colored;
            return new JObject
            {
                ["user"] = new JObject { ["id"] = Owner, ["wallet"] = Wallet, ["isSeeker"] = true, ["hasCharacter"] = true },
                ["character"] = new JObject { ["alias"] = alias, ["tribeId"] = "builder", ["dreamId"] = "build_a_project",
                    ["traitIds"] = new JArray("fast_talker", "iron_will"),
                    ["look"] = new JObject { ["skinTone"] = look[0], ["headId"] = look[1], ["hairId"] = look[2], ["topId"] = look[3], ["bottomId"] = look[4], ["shoesId"] = look[5], ["accId"] = look[6] } },
                ["needs"] = new JObject { ["energy"] = energy, ["hunger"] = 50, ["fun"] = 50, ["hygiene"] = 50, ["sanity"] = sanity },
                ["skills"] = new JObject { ["clout"] = 1, ["coding"] = 1, ["trading"] = 1, ["writing"] = 1, ["design"] = 1, ["security"] = 1 },
                ["cashCents"] = cash, ["debtCents"] = debt, ["homeId"] = "studio", ["minerTier"] = "none", ["flags"] = new JObject { ["lotteryRolled"] = true }
            };
        }

        private static void Inspect(DashboardScreen home, string name, JObject report, Action<bool, string> expect)
        {
            // Thousands of passing pairwise checks need one log line per frame, not thousands of Unity stack traces.
            var emit = expect;
            int checks = 0;
            expect = (pass, message) => { checks++; if (!pass) emit(false, message); };
            var root = home.Element;
            var controls = root.Query<VisualElement>().ToList().Where(e =>
                (e.name == "home-identity" || e.name == "home-wallet" || e.name == "home-enter" ||
                 e.name.StartsWith("home-tile-", StringComparison.Ordinal) || e.name.StartsWith("home-chip-", StringComparison.Ordinal) ||
                 e.ClassListContains("home-settings") || e.ClassListContains("home-card") || e.ClassListContains("home-today-chip")) && e.resolvedStyle.display != DisplayStyle.None).ToList();
            expect(controls.Any(e => e.name == "home-wallet") && controls.Any(e => e.name == "home-identity"), name + ": top identity and wallet present");
            var rectangles = new JArray();
            foreach (var element in controls)
            {
                var b = element.worldBound;
                expect(b.width > 60 && b.height > 60, name + ": control has real touch area " + element.name);
                expect(Contains(root.worldBound, b, 1f), name + ": control inside lobby " + element.name);
                rectangles.Add(new JObject { ["name"] = element.name, ["bounds"] = Box(b) });
                var value = element.Q<Label>(className: "kit-chip__value");
                if (value != null)
                {
                    var measured = value.MeasureTextSize(value.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                    expect(Contains(b, value.worldBound, 1f), name + ": chip value stays inside its control " + element.name);
                    expect(value.resolvedStyle.textOverflow == TextOverflow.Ellipsis || measured.x <= value.contentRect.width + 2f,
                        name + ": chip value text fits or explicitly ellipsizes " + element.name);
                }
            }
            for (int i = 0; i < controls.Count; i++)
                for (int j = i + 1; j < controls.Count; j++)
                    expect(!Overlap(controls[i].worldBound, controls[j].worldBound), name + ": controls do not overlap " + controls[i].name + "/" + controls[j].name);
            var wallet = root.Q("home-wallet");
            if (wallet != null)
            {
                var amounts = wallet.Query<Label>(className: "home-wallet-amount").ToList();
                for (int i = 0; i < amounts.Count; i++)
                {
                    expect(Contains(wallet.worldBound, amounts[i].worldBound, 1f), name + ": wallet amount is inside its card");
                    var textSize = amounts[i].MeasureTextSize(amounts[i].text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                    expect(amounts[i].resolvedStyle.textOverflow == TextOverflow.Ellipsis || textSize.x <= amounts[i].contentRect.width + 2f,
                        name + ": wallet amount text fits or explicitly ellipsizes");
                    for (int j = i + 1; j < amounts.Count; j++) expect(!Overlap(amounts[i].worldBound, amounts[j].worldBound), name + ": wallet amounts do not overlap");
                }
            }

            var preview = (AvatarPreview)typeof(DashboardScreen).GetField("_preview", Private).GetValue(home);
            var actor = (AvatarView)typeof(AvatarPreview).GetField("_avatar", Private).GetValue(preview);
            var elementBounds = root.Q(className: "home-stage-model").worldBound;
            float scale = Mathf.Min(elementBounds.width / preview.Texture.width, elementBounds.height / preview.Texture.height);
            var fit = new Rect(elementBounds.center.x - preview.Texture.width * scale / 2, elementBounds.center.y - preview.Texture.height * scale / 2,
                preview.Texture.width * scale, preview.Texture.height * scale);
            int vertices = 0, cropped = 0, blocked = 0;
            var points = new List<Vector2>();
            foreach (var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                var baked = new Mesh();
                try
                {
                    skin.BakeMesh(baked, true);
                    foreach (var local in baked.vertices)
                    {
                        var p = preview.Camera.WorldToViewportPoint(skin.transform.TransformPoint(local));
                        if (p.z <= 0) { cropped++; continue; }
                        vertices++;
                        if (p.x < -.001f || p.x > 1.001f || p.y < -.001f || p.y > 1.001f) cropped++;
                        var panel = new Vector2(fit.x + p.x * fit.width, fit.y + (1 - p.y) * fit.height);
                        points.Add(panel);
                        if (controls.Any(c => c.worldBound.Contains(panel))) blocked++;
                    }
                }
                finally { UnityEngine.Object.Destroy(baked); }
            }
            expect(vertices > 1000, name + ": actual selected skinned hero geometry sampled");
            expect(cropped == 0, name + ": actual animated hero fits render canvas");
            expect(blocked == 0, name + ": actual animated hero unobscured by navigation/wallet/play controls");
            ((JArray)report["layouts"]).Add(new JObject { ["case"] = name, ["controls"] = rectangles,
                ["heroVertices"] = vertices, ["croppedVertices"] = cropped, ["controlOverlappedVertices"] = blocked,
                ["heroBounds"] = points.Count == 0 ? null : Box(Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y))) });
            emit(true, name + ": evaluated " + checks + " actual layout/geometry constraints (failures reported separately)");
        }

        private static bool Contains(Rect outer, Rect inner, float tolerance) => inner.xMin >= outer.xMin - tolerance && inner.yMin >= outer.yMin - tolerance && inner.xMax <= outer.xMax + tolerance && inner.yMax <= outer.yMax + tolerance;
        private static bool Overlap(Rect a, Rect b) => Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 1 && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 1;
        private static JArray Box(Rect rect) => new JArray(rect.x, rect.y, rect.width, rect.height);
    }
}
