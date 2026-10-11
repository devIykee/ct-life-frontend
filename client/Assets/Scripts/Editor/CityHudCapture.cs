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
    /// <summary>Actual shipped local venue and HUD; isolated data and synthetic prompt-layout states, never a network join.</summary>
    internal static class CityHudCapture
    {
        private const string Owner = "00000000-0000-4000-8000-000000000092";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly string[] Buttons = { "hud-map", "hud-lobby", "hud-needs", "hud-chat", "hud-emotes", "hud-mic", "hud-phone" };

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
            public int Reads, Writes;
            public Func<Task<bool>> RefreshHandler { get; set; }
            public Task<T> Get<T>(string path, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested(); Reads++;
                if (path != "/me") return Task.FromException<T>(new InvalidOperationException("Unexpected HUD fixture read: " + path));
                var value = Profile.DeepClone();
                return Task.FromResult(value is T exact ? exact : value.ToObject<T>(Newtonsoft.Json.JsonSerializer.Create(Json.Settings)));
            }
            private Task<T> Reject<T>() { Writes++; return Task.FromException<T>(new InvalidOperationException("HUD capture attempted a mutation.")); }
            public Task<T> Post<T>(string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => Reject<T>();
            public Task<T> Put<T>(string path, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Patch<T>(string path, object body, CancellationToken ct = default) => Reject<T>();
            public Task<T> Delete<T>(string path, CancellationToken ct = default) => Reject<T>();
            public Task<T> Send<T>(string method, string path, object body = null, bool auth = true, string idempotencyKey = null, CancellationToken ct = default) => method == "GET" ? Get<T>(path, ct) : Reject<T>();
            public Task<RawResponse> SendRaw(string method, string path, string jsonBody, bool auth, string idempotencyKey, string ifNoneMatch, CancellationToken ct) => Reject<RawResponse>();
        }

        /// <summary>Phase A: run only the every-venue/every-home arrival sweep (captureSet "visits").</summary>
        public static bool SweepOnly;
        /// <summary>Phase A: run only the locomotion feel probe (captureSet "feel").</summary>
        public static bool FeelOnly;

        public static async Task Run(Func<string, float, Task> shot, Func<float, Task> wait,
            Action<bool, string> expect, Action<string> step, JObject receipt)
        {
            if (VenueSession.Current != null) throw new InvalidOperationException("HUD capture requires an isolated empty scene, not a live user visit.");
            if (Services.TryGet<WalletBalanceService>(out _)) throw new InvalidOperationException("HUD capture requires no registered live wallet service; its Wallet route must remain unavailable without RPC.");
            if (VenueContent.Find("timeline_square") == null) throw new InvalidOperationException("HUD capture requires the shipped Timeline Square; no remote/greybox fallback.");
            var ui = UiRoot.Instance;
            var config = Services.Get<ConfigStore>();
            var transport = new Transport { Profile = Profile() };
            var api = new Api(transport, config);
            var profile = new PlayerProfile(api);
            var session = new Session();
            // No SetTokens call: fixtures must not persist credentials or change a user's login.
            typeof(Session).GetProperty(nameof(Session.UserId)).SetValue(session, Owner);
            var savedSession = new Saved<Session>(session);
            var savedRest = new Saved<IRestClient>(transport);
            var savedApi = new Saved<Api>(api);
            var savedProfile = new Saved<PlayerProfile>(profile);
            var report = new JObject { ["scope"] = "Actual shipped Timeline Square, stock Rain, local VenueSession, native HUD/CTOS. Isolated in-memory profile; synthetic prompt states test layout only. No network join, voice connection or writes.",
                ["layouts"] = new JArray(), ["prompts"] = new JArray() };
            receipt["cityHud"] = report;
            Hud hud = null;
            PhoneShell phone = null;
            VenueSession visit = null;
            Camera camera = null;
            RenderTexture world = null, previousTarget = null;
            float previousAspect = 0;
            bool projectionMappingInstalled = false;
            var panelSettings = ui.GetComponent<UIDocument>().panelSettings;
            var previousBackground = ui.WorldLayer.style.backgroundImage;
            var watermark = El.Text("REVIEW · LOCAL VENUE / FIXTURE DATA", "ct-small");
            watermark.pickingMode = PickingMode.Ignore;
            watermark.style.position = Position.Absolute; watermark.style.bottom = 3;
            watermark.style.left = Length.Percent(50); watermark.style.marginLeft = -220;
            watermark.style.width = 440; watermark.style.height = 26; watermark.style.fontSize = 16;
            watermark.style.unityTextAlign = TextAnchor.MiddleCenter; watermark.style.color = Color.black;
            watermark.style.backgroundColor = Color.white;
            ui.Root.Add(watermark);
            int errors = 0, layoutMessages = 0;
            void OnLog(string message, string trace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
                if (message.IndexOf("Layout update", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    message.IndexOf("struggl", StringComparison.OrdinalIgnoreCase) >= 0) layoutMessages++;
            }
            Application.logMessageReceived += OnLog;
            void RenderWorld() { if (camera != null && world != null) camera.Render(); }
            try
            {
                await profile.Refresh();
                hud = new Hud(); ui.HudLayer.Add(hud.Element);
                phone = new PhoneShell(ui.PhoneLayer);
                PhoneCatalog.Register(phone, _ => { }, () => { });
                hud.OpenPhone = () => phone.Open();
                visit = await VenueSession.Enter("timeline_square", joinRoom: false, voice: false);
                expect(visit.Channel == null && visit.LocalPlayer != null, "HUD capture uses an actual local player without joining a room");
                var controller = visit.LocalPlayer.GetComponent<InteractionController>();
                controller.enabled = false; // Prompt fixtures remain stable; they have no actionable owner.
                var prompt = (InteractionPrompt)typeof(Hud).GetField("_interact", Private).GetValue(hud);
                camera = CameraRig.Instance.Camera;
                previousTarget = camera.targetTexture;
                previousAspect = camera.aspect;
                var captureTarget = panelSettings.targetTexture;
                if (captureTarget == null) throw new InvalidOperationException("HUD capture requires the existing offscreen panel target.");
                world = new RenderTexture(captureTarget.width, captureTarget.height, 24, RenderTextureFormat.ARGB32) { name = "hud-capture-world" };
                world.Create(); camera.targetTexture = world;
                camera.aspect = (float)world.width / world.height;
                // Batchmode's display can remain 640x480 while the panel renders 2670x1200. Unity's
                // world-to-panel helper flips camera pixels against the display; a texture-backed panel
                // needs that Y origin in its texture space. Calibrate the installed Unity implementation
                // instead of assuming its display height. This mapping is confined to this Editor harness.
                var unit = RuntimePanelUtils.ScreenToPanel(ui.Root.panel, Vector2.one);
                var bottom = RuntimePanelUtils.CameraTransformWorldToPanel(ui.Root.panel,
                    camera.ViewportToWorldPoint(new Vector3(0, 0, 12)), camera);
                float flipHeight = bottom.y / unit.y;
                float textureYOffset = world.height - flipHeight;
                panelSettings.SetScreenToPanelSpaceFunction(p => new Vector2(p.x, p.y + textureYOffset));
                projectionMappingInstalled = true;
                ui.WorldLayer.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(world));
                ui.AddUpdater(RenderWorld);
                report["cameraTarget"] = new JArray(world.width, world.height);
                report["batchDisplay"] = new JArray(Screen.width, Screen.height);
                report["captureProjectionYOffsetPixels"] = textureYOffset;
                report["panelSize"] = new JArray(ui.Root.layout.width, ui.Root.layout.height);
                Bus.Publish(new OnlineCountSignal { online = 27 }); Bus.Publish(new MoodSignal { bps = 420 });
                Bus.Publish(new InteractionPromptSignal());
                hud.Show(true);
                if (FeelOnly) { FeelProbe.OutDir = Environment.GetCommandLineArgs().SkipWhile(x => x != "-ctlife-captureOut").Skip(1).FirstOrDefault(); var (feel, last) = await FeelProbe.Run(visit, camera, shot); receipt["feel"] = feel; visit = last; return; }
                if (SweepOnly) { visit = await VisitSweep(ui, hud, visit, camera, session, transport, profile, shot, wait, expect, receipt); return; }
                await shot("hud-01-city-idle", 1.2f);
                Inspect(hud, "idle", report, expect);

                transport.Profile["cashCents"] = 123456789000L;
                transport.Profile["needs"]["energy"] = 7;
                await profile.Refresh();
                var venueLabel = hud.Element.Q<Label>(className: "ct-venue-name");
                if (venueLabel != null) venueLabel.text = "The Very Long Timeline Community Gathering Square";
                await shot("hud-02-long-location-large-cash-low-needs", .2f);
                Inspect(hud, "long-location-large-cash", report, expect);
                Click(hud.Element.Q<Button>("hud-needs"));
                await shot("hud-03-needs-expanded", .2f);
                Inspect(hud, "needs-expanded", report, expect);

                Click(hud.Element.Q<Button>("hud-chat"));
                var field = hud.Element.Q<TextField>(className: "ct-chat-field");
                if (field != null) field.value = "gm. meeting by the bench?";
                await shot("hud-04-chat-draft", .2f);
                expect(field != null && Visible(field), "actual chat draft field is visible");
                expect(!Visible(hud.Element.Q(className: "ct-needs")), "chat closes expanded needs");
                Click(hud.Element.Q<Button>("hud-emotes"));
                await shot("hud-05-emotes", .2f);
                expect(!Visible(hud.Element.Q(className: "ct-chat")), "emotes close the chat field");
                Click(hud.Element.Q<Button>("hud-emotes"));

                var center = camera.ViewportToWorldPoint(new Vector3(.5f, .52f, 12f));
                async Task Prompt(string name, Vector3 position, List<InteractionAction> actions, bool busy = false, bool followsActor = false)
                {
                    var anchor = followsActor ? new InteractionPromptAnchor(Vector3.up * 1.65f, visit.LocalPlayer.transform, true) : new InteractionPromptAnchor(position);
                    Bus.Publish(new InteractionPromptSignal { targetId = "fixture-" + name, kind = InteractionKind.Seat, busy = busy, actions = actions, anchor = anchor });
                    await wait(.12f); prompt.UpdatePosition();
                    await shot("hud-context-" + name, .15f);
                    InspectPrompt(hud, prompt, anchor, name, report, expect, camera);
                }
                // Use the production offered-seat action, including its actual "seat" icon mapping.
                var sit = new InteractionMachine().Actions(new Interactable { id = "fixture-seat", kind = InteractionKind.Seat });
                await Prompt("sit", center, sit);
                await Prompt("approaching", center, new List<InteractionAction> { new InteractionAction("cancel", "interact.cancel", "close", false) }, true);
                await Prompt("stand-actor-anchor", center, new List<InteractionAction> { new InteractionAction("stand", "interact.stand", "stand", true) }, followsActor: true);
                await Prompt("mic-secondary", center, new List<InteractionAction> { new InteractionAction("stand", "interact.stepAway", "stand", true), new InteractionAction("emote:emote_wave", "interact.wave", "emote", false), new InteractionAction("emote:emote_wagmi", "interact.hype", "emote", false) });
                foreach (var point in new[] { new Vector2(.03f,.04f), new Vector2(.97f,.04f), new Vector2(.03f,.96f), new Vector2(.97f,.96f) })
                    await Prompt("edge-" + point.x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "-" + point.y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), camera.ViewportToWorldPoint(new Vector3(point.x, point.y, 12f)), sit);
                foreach (string name in new[] { "hud-map", "hud-needs", "hud-phone" })
                {
                    var button = hud.Element.Q<Button>(name);
                    if (button == null) continue;
                    var at = button.worldBound.center;
                    var viewport = new Vector3(at.x / ui.Root.layout.width, 1f - at.y / ui.Root.layout.height, 12f);
                    await Prompt("over-" + name, camera.ViewportToWorldPoint(viewport), sit);
                }
#if !CTLIFE_PROD
                await StressSnapshotPicking(ui, visit, prompt, camera, world, shot, report, expect);
#else
                throw new InvalidOperationException("HUD snapshot stress requires an Editor review without CTLIFE_PROD.");
#endif
                Bus.Publish(new InteractionPromptSignal());
                Click(hud.Element.Q<Button>("hud-chat"));
                await wait(.1f);
                Click(hud.Element.Q<Button>("hud-phone"));
                await shot("hud-06-ctos-open", .3f);
                expect(phone.IsOpen && !Visible(hud.Element), "CTOS opens and hides the existing HUD layer");
                expect(hud.Element.Q(className: "ct-chat").resolvedStyle.display == DisplayStyle.None, "opening CTOS closes the chat panel");
                var launcher = ui.PhoneLayer.Q<PhoneLauncher>();
                expect(launcher != null && launcher.PageCount >= 2, "actual phone catalog spans at least two reachable pages");
                Click(ui.PhoneLayer.Q<Button>("phone-page-dot-1"));
                await shot("hud-06b-ctos-page-two", .3f);
                expect(launcher.PageIndex == 1, "native page control reaches CTOS page two");
                expect(Visible(ui.PhoneLayer.Q<Button>("phone-app-wallet")), "Wallet dock remains visible on page two");
                Click(ui.PhoneLayer.Q<Button>("phone-page-dot-0"));
                await shot("hud-06c-ctos-page-one-return", .3f);
                expect(launcher.PageIndex == 0, "native page control returns to CTOS page one");
                Click(ui.PhoneLayer.Q<Button>("phone-app-wallet"));
                await shot("hud-06d-ctos-wallet-unavailable", .2f);
                expect(Visible(ui.PhoneLayer.Q(className: "ct-wallet-page")), "persistent dock opens the actual Wallet app");
                expect(ui.PhoneLayer.Q<Label>(className: "ct-wallet-status")?.text == WalletStrings.T("unavailable"), "Wallet honestly reports unavailable without a service");
                Click(ui.PhoneLayer.Q<Button>("phone-home"));
                await shot("hud-06e-ctos-home-from-wallet", .2f);
                expect(Visible(launcher) && ui.PhoneLayer.Q(className: "ct-wallet-page") == null, "native Home returns from Wallet to CTOS launcher");
                Click(ui.PhoneLayer.Q<Button>("phone-search"));
                await wait(.1f);
                ui.PhoneLayer.Q<TextField>("phone-search-input").value = "wallet";
                await shot("hud-06f-ctos-search-wallet", .2f);
                expect(launcher.IsSearching && ui.PhoneLayer.Q<ScrollView>("phone-search-results").Query<Button>(className: "ct-app").ToList().Count == 1, "search finds the real Wallet route");
                Click(ui.PhoneLayer.Q<Button>("phone-search-cancel"));
                Click(ui.PhoneLayer.Q<Button>("phone-widget-map"));
                await shot("hud-06g-ctos-widget-map", .2f);
                expect(Visible(ui.PhoneLayer.Q(className: "map-full")), "city widget opens the real map (CT Island)");
                Click(ui.PhoneLayer.Q<Button>("phone-home"));
                phone.Close();
                await shot("hud-07-ctos-closed", .2f);
                expect(Visible(hud.Element), "closing CTOS restores the HUD");
                hud.Show(false); await wait(.1f);
                expect(!Visible(hud.Element), "HUD hide is applied");
                hud.Show(true); await shot("hud-08-restored", .2f);
                Inspect(hud, "restored", report, expect);
                await MapShots(ui, hud, phone, visit, shot, wait, expect, report);
                expect(transport.Writes == 0, "HUD capture attempted no REST mutations");
                report["localPlayer"] = visit.LocalPlayer.GetComponent<AvatarView>().name;
                report["scene"] = visit.Venue.name;
            }
            finally
            {
                try
                {
                    phone?.Dispose(); hud?.Dispose(); hud?.Element.RemoveFromHierarchy();
                    ui.RemoveUpdater(RenderWorld);
                    ui.WorldLayer.style.backgroundImage = previousBackground;
                    if (camera != null) { camera.targetTexture = previousTarget; camera.aspect = previousAspect; }
                    if (projectionMappingInstalled) panelSettings.SetScreenToPanelSpaceFunction(p => p);
                    if (world != null) { world.Release(); UnityEngine.Object.Destroy(world); }
                    if (visit != null) await visit.Exit();
                }
                finally
                {
                    watermark.RemoveFromHierarchy();
                    savedProfile.Dispose(); savedApi.Dispose(); savedRest.Dispose(); savedSession.Dispose();
                    report["servicesRestored"] = savedProfile.Restored && savedApi.Restored && savedRest.Restored && savedSession.Restored;
                    report["restReads"] = transport.Reads; report["restWrites"] = transport.Writes; report["runtimeErrors"] = errors;
                    report["layoutStruggleMessages"] = layoutMessages;
                    Application.logMessageReceived -= OnLog;
                    expect((bool)report["servicesRestored"], "HUD capture restored original service instances");
                    expect(errors == 0, "HUD capture has no runtime errors");
                    expect(layoutMessages == 0, "HUD capture has no recursive layout warnings or errors");
                }
            }
            step("city HUD fixtures complete");
        }

        /// <summary>
        /// Map/HUD job: the minimap while actually walking (navmesh MoveTo) north, east, south and west, with an independent
        /// accuracy check (an orthographic camera set up exactly like MapBaker's must put the player at the same uv the minimap
        /// uses); the in-city full map at area, district and island zoom; a venue card; the Duo Map app with search.
        /// </summary>
        private static async Task MapShots(UiRoot ui, Hud hud, PhoneShell phone, VenueSession visit, Func<string, float, Task> shot,
            Func<float, Task> wait, Action<bool, string> expect, JObject report)
        {
            var area = CTLife.UI.Map.MapCatalog.ForVisit(visit.VenueId, visit.HomeId);
            expect(area != null, "baked map exists for the visit");
            if (area == null) return;
            var mini = hud.Minimap;
            var walks = new JArray();
            report["minimapWalks"] = walks;
            var player = visit.LocalPlayer;
            var root = visit.Venue.transform;
            var checkCam = new GameObject("map-accuracy-check").AddComponent<Camera>();
            checkCam.enabled = false;
            checkCam.orthographic = true;
            checkCam.orthographicSize = area.Size.y / 2f;
            checkCam.aspect = area.Size.x / area.Size.y;
            checkCam.transform.SetPositionAndRotation(root.TransformPoint(new Vector3(area.Center.x, 60f, area.Center.y)), root.rotation * Quaternion.Euler(90f, 0f, 0f));
            try
            {
                // Start in the open square (the spawn is the entrance, at the navmesh edge).
                var start = visit.Venue.ToWorld(0f, -7f);
                player.SnapTo(start, 0f);
                await wait(.4f);
                foreach (var (dirName, dir) in new[] { ("north", Vector3.forward), ("east", Vector3.right), ("south", Vector3.back), ("west", Vector3.left) })
                {
                    var target = start + root.TransformDirection(dir) * 4.5f;
                    bool ok = player.MoveTo(target);
                    await wait(1.1f);
                    var local = visit.Venue.ToLocal(player.transform.position);
                    var vp = checkCam.WorldToViewportPoint(player.transform.position);
                    var uv = CTLife.UI.Map.MapMath.AreaUv(area, local);
                    float err = (new Vector2(vp.x, vp.y) - uv).magnitude * Mathf.Max(area.Size.x, area.Size.y);
                    var f = root.InverseTransformDirection(player.transform.forward);
                    float want = CTLife.UI.Map.MapMath.Heading(f);
                    float headingErr = Mathf.Abs(Mathf.DeltaAngle(want * Mathf.Rad2Deg, mini.HeadingRadians * Mathf.Rad2Deg));
                    var placement = CTLife.UI.Map.MapCatalog.PlacementOf(area);
                    float centreErr = (mini.PlayerAtlas - CTLife.UI.Map.MapMath.AtlasFromLocal(placement, local)).magnitude;
                    await shot("map-mini-walk-" + dirName, .05f);
                    expect(ok, "player walks " + dirName + " on the navmesh");
                    expect(err < 0.05f, "map marker matches the baked map within 5 cm walking " + dirName);
                    expect(centreErr < 0.01f, "minimap centre is the player walking " + dirName);
                    expect(headingErr < 12f, "minimap heading follows the player walking " + dirName);
                    walks.Add(new JObject { ["dir"] = dirName, ["local"] = new JArray(local.x, local.y), ["uv"] = new JArray(uv.x, uv.y),
                        ["bakeCameraViewport"] = new JArray(vp.x, vp.y), ["errorMeters"] = err, ["headingErrorDeg"] = headingErr });
                    player.CancelMovement();
                    player.SnapTo(start, 0f);
                    await wait(.4f);
                }
            }
            finally { UnityEngine.Object.Destroy(checkCam.gameObject); }

            // In-city full map: area, district and island zoom (the island view is the atlas).
            hud.OpenMap = () => CTLife.UI.Map.CityMap.Show(ui.HudLayer, _ => { }, () => { }, () => CTLife.UI.Map.CityMap.AvailableVenues(phone.CurrentPhase), null);
            Click(hud.Element.Q<Button>("hud-map"));
            await wait(.3f);
            var view = CTLife.UI.Map.CityMap.Current;
            expect(view != null && Visible(view), "minimap opens the full map");
            if (view != null)
            {
                var me = mini.PlayerAtlas;
                view.SetView(me, 14f); await shot("map-full-1-area", .25f);
                view.SetView(me, 3.2f); await shot("map-full-2-district", .25f);
                view.SetView(CTLite(), view.MinPpm); await shot("map-full-3-island-atlas", .25f);
                view.Select("terminal_cafe", fly: false);
                var cafe = CTLife.UI.Map.MapCatalog.Areas["terminal_cafe"];
                view.SetView(CTLife.UI.Map.MapSurface.AreaAtlasRect(cafe, CTLife.UI.Map.MapCatalog.PlacementOf(cafe)).center, 9f);
                await shot("map-full-4-venue-card", .25f);
                expect(Visible(view.Q("map-card")) && view.Q<Button>("map-go") != null, "venue card offers Go for an open venue");
                report["fullMapPpm"] = new JArray(view.MinPpm, view.MaxPpm);
                CTLife.UI.Map.CityMap.Close();
            }
            // The Duo Map app (same component, Duo look) with search.
            phone.Open("map");
            await wait(.4f);
            await shot("map-duo-app", .3f);
            var duo = ui.PhoneLayer.Q<CTLife.UI.Map.FullMapView>();
            expect(duo != null && Visible(duo), "Duo Map app shows the island map");
            var search = ui.PhoneLayer.Q<TextField>(className: "duo-search-field");
            if (search != null) { search.value = "cafe"; await shot("map-duo-search", .25f); }
            duo?.Select("timeline_square", fly: true);
            await shot("map-duo-card", .6f);
            phone.Close();
            await wait(.2f);
        }

        /// <summary>
        /// Phase A: arrive in EVERY shipped venue and home the way travel does and check the player can be found: on screen
        /// (comfortably inside the frame), grounded (navmesh and floor under the feet), not covered by an interaction prompt,
        /// lit enough to tell apart from the floor, wearing the "you" ring, and on the minimap at the right place.
        /// </summary>
        private static async Task<VenueSession> VisitSweep(UiRoot ui, Hud hud, VenueSession visit, Camera camera, Session session,
            Transport transport, PlayerProfile profile, Func<string, float, Task> shot, Func<float, Task> wait, Action<bool, string> expect, JObject receipt)
        {
            var rows = new JArray();
            receipt["visits"] = rows;
            // Homes need a signed-in owner: an in-memory token only (never stored; the fixture transport has no network).
            typeof(Session).GetProperty(nameof(Session.AccessToken)).SetValue(session, "fixture-in-memory");
            var targets = VenueContent.VenueIds.Select(v => (v, (string)null)).ToList();
            foreach (var h in VenueContent.BuiltHomes) if (VenueContent.HasPlayableHome(h)) targets.Add(("home", h));
            foreach (var (venueId, homeId) in targets)
            {
                string name = homeId != null ? "home_" + homeId : venueId;
                await visit.Exit();
                if (homeId != null)
                {
                    transport.Profile["homeId"] = homeId;
                    await profile.Refresh();
                    visit = await VenueSession.EnterOwnHome();
                }
                else visit = await VenueSession.Enter(venueId, joinRoom: false, voice: false);
                camera = CameraRig.Instance.Camera;
                await wait(1.0f);
                var player = visit.LocalPlayer;
                var pos = player.transform.position;
                var vp = camera.WorldToViewportPoint(pos + Vector3.up * 0.9f);
                bool onScreen = vp.z > 0 && vp.x > 0.12f && vp.x < 0.88f && vp.y > 0.14f && vp.y < 0.86f;
                bool nav = UnityEngine.AI.NavMesh.SamplePosition(pos, out var navHit, 0.3f, UnityEngine.AI.NavMesh.AllAreas);
                bool floor = Physics.Raycast(pos + Vector3.up * 0.6f, Vector3.down, out var floorHit, 1.2f, CTLife.World.CharacterLayers.WorldMask, QueryTriggerInteraction.Ignore);
                float floorGap = floor ? pos.y - floorHit.point.y : 99f;
                // Prompt: none on arrival, or never over the player.
                var prompt = hud.Element.Q(className: "ct-hud-interact");
                bool promptShown = prompt != null && prompt.resolvedStyle.display != DisplayStyle.None && prompt.resolvedStyle.visibility == Visibility.Visible;
                bool promptClear = true;
                if (promptShown && CTLife.UI.InteractionPrompt.LocalPlayerRect(prompt.parent, camera, out var me))
                {
                    var b = prompt.worldBound; var pmin = prompt.parent.WorldToLocal(b.min); var pmax = prompt.parent.WorldToLocal(b.max);
                    promptClear = !Rect.MinMaxRect(pmin.x, pmin.y, pmax.x, pmax.y).Overlaps(me);
                }
                // Minimap: visible, on this area, centred on the player.
                var mini = hud.Minimap;
                var area = CTLife.UI.Map.MapCatalog.ForVisit(visit.VenueId, visit.HomeId);
                var placement = CTLife.UI.Map.MapCatalog.PlacementOf(area);
                bool miniShown = mini.resolvedStyle.display != DisplayStyle.None && mini.Area == area && area != null;
                float miniErr = placement != null ? (mini.PlayerAtlas - CTLife.UI.Map.MapMath.AtlasFromLocal(placement, visit.Venue.ToLocal(pos))).magnitude : 99f;
                bool ring = player.GetComponent<LocalPlayerMarker>() != null && player.transform.Find("you-ring") != null;
                await shot("visit-" + name, .05f);
                expect(onScreen, name + ": the player is comfortably on screen on arrival");
                expect(nav && navHit.distance < 0.3f, name + ": the player stands on the navmesh");
                expect(floor && Mathf.Abs(floorGap) < 0.25f, name + ": the player is grounded (floor gap " + floorGap.ToString("0.00") + " m)");
                expect(promptClear, name + ": no interaction prompt covers the player");
                expect(miniShown && miniErr < 0.01f, name + ": the minimap shows this area with the player at the centre");
                expect(ring, name + ": the 'you' ring marks the player");
                rows.Add(new JObject { ["visit"] = name, ["viewport"] = new JArray(vp.x, vp.y), ["navmesh"] = nav, ["floorGap"] = floorGap,
                    ["promptOnArrival"] = promptShown, ["promptClear"] = promptClear, ["minimap"] = miniShown, ["minimapErrorM"] = miniErr,
                    ["local"] = new JArray(visit.Venue.ToLocal(pos).x, visit.Venue.ToLocal(pos).y) });
            }
            typeof(Session).GetProperty(nameof(Session.AccessToken)).SetValue(session, null);
            return visit;
        }

        private static Vector2 CTLite() => CTLife.UI.Map.MapCatalog.Atlas.Rect.center;

#if !CTLIFE_PROD
        private static async Task StressSnapshotPicking(UiRoot ui, VenueSession visit, InteractionPrompt prompt, Camera camera,
            RenderTexture world, Func<string, float, Task> shot, JObject report, Action<bool, string> expect)
        {
            var actorAnchor = new InteractionPromptAnchor(Vector3.up * 1.65f, visit.LocalPlayer.transform, true);
            var stand = new List<InteractionAction> { new InteractionAction("stand", "interact.stand", "stand", true) };
            var wide = new List<InteractionAction> { new InteractionAction("stand", "interact.stepAway", "stand", true),
                new InteractionAction("emote:emote_wave", "interact.wave", "emote", false),
                new InteractionAction("emote:emote_wagmi", "interact.hype", "emote", false) };
            void Publish(List<InteractionAction> actions) => Bus.Publish(new InteractionPromptSignal
                { targetId = "fixture-snapshot-stress", kind = InteractionKind.Mic, actions = actions, anchor = actorAnchor });
            var frames = new JArray();
            report["snapshotPickingStress"] = new JObject { ["requestedFrames"] = 60, ["frames"] = frames };
            Publish(stand);
            await Awaitable.NextFrameAsync();
            try
            {
                for (int frame = 0; frame < 60; frame++)
                {
                    if (frame == 30) Publish(wide);
                    float phase = frame * Mathf.PI * 2 / 60;
                    CameraRig.Instance.Pan(new Vector2(Mathf.Cos(phase) * 18f, Mathf.Sin(phase) * 12f));
                    await Awaitable.NextFrameAsync(); // CameraRig's preceding LateUpdate has applied the actual pan.
                    prompt.UpdatePosition();
                    // Same read-only traversal used on Seeker, including Panel.Pick for visible nodes.
                    // It deliberately runs immediately after the prompt's presentation update.
                    var snapshot = CTLife.UI.Dev.QaUiSnapshotExporter.Capture(ui.Root, ui.Root.worldBound, world.width, world.height);
                    int actions = snapshot.nodes.Count(n => n.visible && n.name != null && n.name.StartsWith("scene-action-", StringComparison.Ordinal));
                    expect(actions == (frame < 30 ? 1 : 3), "snapshot stress frame " + frame + ": actual scene actions were picked");
                    actorAnchor.TryGetPosition(out var position);
                    var viewport = camera.WorldToViewportPoint(position);
                    frames.Add(new JObject { ["frame"] = Time.frameCount, ["nodes"] = snapshot.nodes.Count,
                        ["actionNodes"] = actions, ["promptBounds"] = Box(prompt.Element.worldBound),
                        ["actorViewport"] = new JArray(viewport.x, viewport.y, viewport.z) });
                }
                expect(frames.Count == 60, "snapshot stress completed all 60 camera-pan frames");
                await shot("hud-context-snapshot-picking-stress", .1f);
            }
            finally { CameraRig.Instance.SnapToTarget(); }
        }
#endif

        private static JObject Profile()
        {
            var look = CreatorLookCatalog.Available().FirstOrDefault(c => c.look != null && c.look[1] == "head_rain")?.look;
            if (look == null) throw new InvalidOperationException("Actual Rain fixture is unavailable.");
            return new JObject
            {
                ["user"] = new JObject { ["id"] = Owner, ["wallet"] = "11111111111111111111111111111111", ["isSeeker"] = true, ["hasCharacter"] = true },
                ["character"] = new JObject { ["alias"] = "city_regular", ["tribeId"] = "builder", ["dreamId"] = "build_a_project", ["traitIds"] = new JArray("fast_talker", "iron_will"),
                    ["look"] = new JObject { ["skinTone"] = look[0], ["headId"] = look[1], ["hairId"] = look[2], ["topId"] = look[3], ["bottomId"] = look[4], ["shoesId"] = look[5], ["accId"] = look[6] } },
                ["needs"] = new JObject { ["energy"] = 82, ["hunger"] = 68, ["fun"] = 71, ["hygiene"] = 61, ["sanity"] = 88 },
                ["skills"] = new JObject { ["clout"] = 1, ["coding"] = 1, ["trading"] = 1, ["writing"] = 1, ["design"] = 1, ["security"] = 1 },
                ["cashCents"] = 300000, ["debtCents"] = 0, ["homeId"] = "studio", ["minerTier"] = "none", ["flags"] = new JObject { ["lotteryRolled"] = true }
            };
        }

        private static void Click(Button button)
        {
            if (button == null) throw new InvalidOperationException("Required named HUD control is missing.");
            using var submit = NavigationSubmitEvent.GetPooled(); submit.target = button; button.SendEvent(submit);
        }
        private static bool Visible(VisualElement element)
        {
            if (element == null || element.panel == null) return false;
            for (var e = element; e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }
        private static void Inspect(Hud hud, string name, JObject report, Action<bool, string> expect)
        {
            var controls = Buttons.Select(id => hud.Element.Q<Button>(id)).Where(Visible).ToArray();
            expect(controls.Length == Buttons.Length, name + ": all seven HUD routes/status actions remain visible");
            foreach (var control in controls)
            {
                expect(Contains(hud.Element.worldBound, control.worldBound), name + ": control fits " + control.name);
                expect(control.worldBound.width >= 100 && control.worldBound.height >= 100, name + ": touch target remains substantial " + control.name);
            }
            for (int i = 0; i < controls.Length; i++) for (int j = i + 1; j < controls.Length; j++)
                expect(!Overlap(controls[i].worldBound, controls[j].worldBound), name + ": controls do not overlap " + controls[i].name + "/" + controls[j].name);
            var centre = hud.Element.worldBound.center;
            var picked = hud.Element.panel.Pick(centre);
            expect(picked == UiRoot.Instance.WorldLayer || UiRoot.Instance.WorldLayer.Contains(picked), name + ": centre remains world input");
            var cash = hud.Element.Q<Label>(className: "ct-cash");
            if (cash != null) expect(TextFits(cash), name + ": Cash fits its text area");
            ((JArray)report["layouts"]).Add(new JObject { ["case"] = name, ["controls"] = new JArray(controls.Select(c => new JObject { ["id"] = c.name, ["bounds"] = Box(c.worldBound) })), ["centrePick"] = picked?.name });
        }
        private static void InspectPrompt(Hud hud, InteractionPrompt prompt, InteractionPromptAnchor anchor, string name,
            JObject report, Action<bool, string> expect, Camera camera)
        {
            bool shown = Visible(prompt.Element);
            expect(shown, name + ": scene prompt visible");
            var bounds = prompt.Element.worldBound;
            var parent = prompt.Element.parent;
            var localBounds = Rect.MinMaxRect(parent.WorldToLocal(bounds.min).x, parent.WorldToLocal(bounds.min).y,
                parent.WorldToLocal(bounds.max).x, parent.WorldToLocal(bounds.max).y);
            var safe = ContextPromptPlacement.Inflate(parent.contentRect, -ContextPromptPlacement.EdgeInset);
            expect(Contains(safe, localBounds), name + ": scene prompt stays inside safe inset");
            anchor.TryGetPosition(out var world);
            var projected = RuntimePanelUtils.CameraTransformWorldToPanel(prompt.Element.panel, world, camera);
            // Independent check: the rendered full-frame camera texture's viewport coordinates must land
            // on the same visible point in WorldLayer. Do not merely compare two uses of the projector.
            var viewport = camera.WorldToViewportPoint(world);
            var scene = UiRoot.Instance.WorldLayer;
            var expectedProjection = scene.LocalToWorld(new Vector2(scene.contentRect.xMin + viewport.x * scene.contentRect.width,
                scene.contentRect.yMin + (1f - viewport.y) * scene.contentRect.height));
            float projectionError = Vector2.Distance(projected, expectedProjection);
            expect(projectionError < 2, name + ": world anchor matches rendered scene viewport");
            expect(!bounds.Contains(projected), name + ": prompt leaves projected target visible");
            var registered = (VisualElement[])typeof(InteractionPrompt).GetField("_obstacles", Private).GetValue(prompt);
            var obstacles = new JArray();
            foreach (var obstacle in registered.Where(Visible).Where(e => e.layout.width > 0 && e.layout.height > 0))
            {
                var min = parent.WorldToLocal(obstacle.worldBound.min);
                var max = parent.WorldToLocal(obstacle.worldBound.max);
                var reserved = ContextPromptPlacement.Inflate(Rect.MinMaxRect(min.x, min.y, max.x, max.y), ContextPromptPlacement.Clearance);
                expect(!Overlap(localBounds, reserved), name + ": prompt clears actual obstacle " + string.Join(" ", obstacle.GetClasses()));
                obstacles.Add(new JObject { ["classes"] = string.Join(" ", obstacle.GetClasses()), ["panelBounds"] = Box(obstacle.worldBound), ["reservedLocalBounds"] = Box(reserved) });
            }
            var pointer = prompt.Element.Q(className: "ct-interact-pointer");
            bool pointerVisible = Visible(pointer);
            if (pointerVisible && !prompt.Element.ClassListContains("ct-interact-clamped"))
                expect(Mathf.Abs(pointer.worldBound.center.x - projected.x) < 2, name + ": tail aligns with projected anchor");
            foreach (var label in prompt.Element.Query<Label>(className: "ct-interact-label").ToList())
                expect(TextFits(label), name + ": scene action label fits");
            ((JArray)report["prompts"]).Add(new JObject { ["case"] = name, ["visible"] = shown, ["bounds"] = Box(bounds),
                ["parentPanelBounds"] = Box(parent.worldBound), ["localBounds"] = Box(localBounds), ["safeLocalBounds"] = Box(safe),
                ["projectedAnchor"] = new JArray(projected.x, projected.y), ["pointerVisible"] = pointerVisible,
                ["renderedSceneAnchor"] = new JArray(expectedProjection.x, expectedProjection.y), ["projectionError"] = projectionError,
                ["pointerPanelBounds"] = pointer == null ? null : Box(pointer.worldBound), ["obstacles"] = obstacles });
        }
        private static bool TextFits(Label label)
        {
            var size = label.MeasureTextSize(label.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            return size.x <= label.contentRect.width + 2 && size.y <= label.contentRect.height + 2 || label.resolvedStyle.textOverflow == TextOverflow.Ellipsis;
        }
        private static bool Contains(Rect a, Rect b) => b.xMin >= a.xMin - 1 && b.yMin >= a.yMin - 1 && b.xMax <= a.xMax + 1 && b.yMax <= a.yMax + 1;
        private static bool Overlap(Rect a, Rect b) => Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 1 && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 1;
        private static JArray Box(Rect rect) => new JArray(rect.x, rect.y, rect.width, rect.height);
    }
}
