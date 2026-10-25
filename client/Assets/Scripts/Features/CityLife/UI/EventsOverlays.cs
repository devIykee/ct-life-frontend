using System;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.World;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// Breakpoint 2026 in the world while signed in (lane B, 2026-10-24, backend PR #11): the <see cref="EventsService"/>
    /// (calendar, quest, realtime), the <see cref="BreakpointVenue"/> (the London skin on the Conference Center) and the moments
    /// over the world: the booth stamp, the nod photo (viewfinder, flash, a polaroid of the nod), and the claim moment when
    /// the chain completes (Cash and the lanyard, "Wear the lanyard"). Created by FlowController next to the City Life
    /// overlays; rebinds on sign-in and sign-out.
    /// </summary>
    public sealed class EventsOverlays : IDisposable
    {
        private readonly VisualElement _layer;
        private readonly Session _session;
        private EventsService _svc;
        private BreakpointVenue _venue;
        private VisualElement _card, _photoLayer, _claim;
        private RenderTexture _photoRt;
        private bool _photoBusy, _disposed;
        private static StyleSheet _sheet;

        public EventsOverlays(VisualElement modalLayer)
        {
            _layer = modalLayer;
            _session = Services.Get<Session>();
            _session.Changed += Bind;
            var root = UiRoot.Instance != null ? UiRoot.Instance.Root : null;
            _sheet ??= Resources.Load<StyleSheet>("UI/events");
            if (root != null && _sheet != null && !root.styleSheets.Contains(_sheet)) root.styleSheets.Add(_sheet);
            PhoneWidgets.EventsLine = HomeLine;
            WarmFonts(root);
            WorldLabels.Badge = NameBadge;
            Bind();
        }

        public EventsService Service => _svc;

        /// <summary>The event UI's Geist faces (OFL) are dynamic .ttf fonts like the rest of the UI: lay out every glyph once,
        /// unseen, a few seconds after start, so the first card or app screen never pays for glyph and kerning reads.</summary>
        private static void WarmFonts(VisualElement root)
        {
            if (root == null) return;
            var sb = new System.Text.StringBuilder();
            for (char c = ' '; c <= '~'; c++) sb.Append(c);
            sb.Append("·→…—–’✓↗●○✕");
            string chars = sb.ToString();
            string[] fonts = { "UI/Fonts/Events/Geist-SemiBold", "UI/Fonts/Events/GeistMono-Medium", "UI/Fonts/Events/GeistMono-SemiBold" };
            for (int i = 0; i < fonts.Length; i++)
            {
                string path = fonts[i];
                root.schedule.Execute(() =>
                {
                    var font = Resources.Load<Font>(path);
                    if (font == null) return;
                    var label = new Label(chars) { pickingMode = PickingMode.Ignore, enableRichText = false };
                    label.style.unityFontDefinition = FontDefinition.FromFont(font);
                    label.style.position = Position.Absolute; label.style.left = -20000f; label.style.top = 0f;
                    label.style.visibility = Visibility.Hidden; label.style.whiteSpace = WhiteSpace.NoWrap;
                    root.Add(label);
                    label.schedule.Execute(() => label.RemoveFromHierarchy()).StartingIn(250);
                }).StartingIn(1500 + i * 150);
            }
        }

        /// <summary>Nameplates: "IRL" (gold) for players checked in at the real Breakpoint, "BREAKPOINT" for the lanyard worn.</summary>
        private static string NameBadge(AvatarView a)
        {
            if (a == null) return null;
            // IRL first: players checked in at the real event and listed (the board, `irl_checkin`), during the event
            var s = EventsService.Current;
            if (s != null && s.BreakpointLive && !string.IsNullOrEmpty(a.displayName) && s.IrlAliases.Contains(a.displayName)) return "IRL";
            if (a.accId == EventIds.GoldLanyard) return "IRL";
            return a.accId == EventIds.Lanyard ? "BREAKPOINT" : null;
        }

        /// <summary>The Duo home's events row: the live event, else the next one with its countdown.</summary>
        private static (string title, string when, bool live)? HomeLine()
        {
            var s = EventsService.Current;
            if (s == null || !s.Loaded) return null;
            long now = s.NowMs;
            var live = s.Live.Find(e => e.IsLive(now));
            if (live != null) return (live.Title + (live.City != null ? " · " + live.City : ""), EventText.T("live"), true);
            CalendarEvent next = null;
            foreach (var e in s.Upcoming) if (e.StartsAtMs > now && (next == null || e.StartsAtMs < next.StartsAtMs)) next = e;
            if (next == null) return null;
            return (next.Title + (next.City != null ? " · " + next.City : ""), "in " + EventWire.Short(next.StartsAtMs - now), false);
        }

        private void Bind()
        {
            if (_disposed) return;
            var user = _session.IsSignedIn ? _session.UserId : null;
            if (_svc != null && !_svc.Disposed && _svc.UserId == user) return;
            Unbind();
            if (string.IsNullOrEmpty(user)) return;
            _svc = new EventsService(user);
            _svc.Completed += OnCompleted;
            _venue = BreakpointVenue.Create(_svc, this);
            PhotoCamera();
        }

        private void Unbind()
        {
            if (_venue != null) UnityEngine.Object.Destroy(_venue.gameObject);
            _venue = null;
            if (_svc != null) { _svc.Completed -= OnCompleted; _svc.Dispose(); }
            _svc = null;
            _card?.RemoveFromHierarchy(); _photoLayer?.RemoveFromHierarchy(); _claim?.RemoveFromHierarchy();
            _card = null; _photoLayer = null; _claim = null;
        }

        // ------------------------------------------------------------------ booths

        public async void VisitBooth(int i)
        {
            if (_svc == null) return;
            var id = EventIds.Booth(i);
            var step = _svc.Breakpoint?.Quest?.Find(EventIds.StepBooths);
            if (_svc.HasDone(id))
            {
                Card((i + 1).ToString("00"), Brand(i).name, EventText.T("booth.again"), step);
                return;
            }
            Bus.Publish(new SfxSignal("sfx_ui_tap"));
            try
            {
                var r = await _svc.Progress(EventIds.StepBooths, id);
                var s = r.Quest.Find(EventIds.StepBooths);
                Card((i + 1).ToString("00"), Brand(i).name, EventText.F("booth.visited", i + 1, Math.Min(s?.Done ?? 0, s?.Required ?? 3), s?.Required ?? 3) + " · " + Brand(i).tag, s);
                Bus.Publish(new SfxSignal(s != null && s.Complete ? "sfx_rank_up" : "sfx_ui_success"));
            }
            catch (Exception e)
            {
                Card((i + 1).ToString("00"), Brand(i).name, _svc.Explain(e), step);
                Bus.Publish(new SfxSignal("sfx_ui_error"));
            }
        }

        private static (string name, string tag, string accent) Brand(int i) => i switch
        {
            0 => ("Validator Row", "Stake with the city", "#19c6b5"),
            1 => ("Duo", "The phone is the wallet", "#0a7cff"),
            2 => ("Perp Pit", "Leverage, responsibly", "#2bd96b"),
            3 => ("Buildooor Station", "Ship before the keynote", "#ff8a3d"),
            4 => ("Timeline", "Post it before it is alpha", "#a68bff"),
            _ => ("CT Parcel", "Same-day, door to door", "#0a54db"),
        };

        /// <summary>The stamp card above the HUD: a coloured tile, the title, a line and the step's meter; leaves after 3 s.</summary>
        private void Card(string stamp, string title, string sub, EventStep step)
        {
            _card?.RemoveFromHierarchy();
            var tile = El.Div("evt-stamp", El.Text(stamp, "evt-stamp-text"));
            var main = El.Div("evt-card-main", El.Text("BOOTH " + stamp + " · BREAKPOINT 2026 · EXPO", "evt-kicker"), El.Text(title, "evt-card-title"), El.Text(sub, "evt-card-sub"));
            if (step != null)
            {
                var fill = El.Div("evt-track-fill");
                fill.EnableInClassList("evt-done", step.Complete);
                main.Add(El.Div("evt-track", fill));
                float f = (float)step.Fraction;
                fill.schedule.Execute(() => fill.style.width = Length.Percent(f * 100f)).ExecuteLater(120);
            }
            var card = El.Div("evt-card", tile, main);
            card.pickingMode = PickingMode.Ignore;
            _layer.Add(card);
            _card = card;
            card.schedule.Execute(() => card.AddToClassList("evt-in")).ExecuteLater(16);
            card.schedule.Execute(() => card.RemoveFromClassList("evt-in")).ExecuteLater(3200);
            card.schedule.Execute(() => { card.RemoveFromHierarchy(); if (_card == card) _card = null; }).ExecuteLater(3700);
        }

        // ------------------------------------------------------------------ nods: the photo moment

        public async void SpotNod(int i, Transform disc)
        {
            if (_svc == null || _photoBusy) return;
            var id = EventIds.Nod(i);
            if (_svc.HasDone(id)) { Bus.Publish(ToastSignal.Text(EventText.T("nod.again"))); return; }
            _photoBusy = true;
            try
            {
                var layer = El.Div("evt-layer");
                layer.pickingMode = PickingMode.Ignore;
                var vf = El.Div("evt-viewfinder",
                    El.Div("evt-corner evt-corner-tl"), El.Div("evt-corner evt-corner-tr"), El.Div("evt-corner evt-corner-bl"), El.Div("evt-corner evt-corner-br"),
                    El.Div("evt-reticle"), El.Text("EVENT NOD " + (i + 1) + " / " + EventIds.NodCount, "evt-vf-label"));
                var flash = El.Div("evt-flash");
                layer.Add(vf); layer.Add(flash);
                foreach (var e in new[] { vf, flash }) e.pickingMode = PickingMode.Ignore;
                _photoLayer?.RemoveFromHierarchy();
                _photoLayer = layer;
                _layer.Add(layer);
                // the server call runs while the shutter does its thing
                var call = _svc.Progress(EventIds.StepNods, id);
                await Awaitable.NextFrameAsync();
                vf.AddToClassList("evt-in");
                await Awaitable.WaitForSecondsAsync(0.5f);
                var photo = await Snap(disc);
                flash.AddToClassList("evt-on");
                Bus.Publish(new SfxSignal("sfx_pnl_card_flip"));
                await Awaitable.WaitForSecondsAsync(0.06f);
                flash.RemoveFromClassList("evt-on");
                vf.RemoveFromClassList("evt-in");
                var img = El.Div("evt-photo");
                if (photo != null) img.style.backgroundImage = Background.FromRenderTexture(photo);
                var cap = El.Text("Nod " + (i + 1) + " · spotted", "evt-photo-cap");
                var sub = El.Text("BREAKPOINT PARK · LONDON 2026", "evt-photo-sub");
                var pol = El.Div("evt-polaroid", img, cap, sub);
                pol.pickingMode = PickingMode.Ignore;
                layer.Add(pol);
                await Awaitable.NextFrameAsync();
                pol.AddToClassList("evt-in");
                try
                {
                    var r = await call;
                    var s = r.Quest.Find(EventIds.StepNods);
                    sub.text = EventText.F("nod.spotted", i + 1, Math.Min(s?.Done ?? 0, s?.Required ?? 5), s?.Required ?? 5);
                    Bus.Publish(new SfxSignal(s != null && s.Complete ? "sfx_rank_up" : "sfx_ui_success"));
                }
                catch (Exception e)
                {
                    cap.text = "Nod " + (i + 1);
                    sub.text = _svc.Explain(e);
                    Bus.Publish(new SfxSignal("sfx_ui_error"));
                }
                await Awaitable.WaitForSecondsAsync(3.4f);
                pol.RemoveFromClassList("evt-in");
                await Awaitable.WaitForSecondsAsync(0.5f);
                layer.RemoveFromHierarchy();
                if (_photoLayer == layer) _photoLayer = null;
            }
            catch (Exception e) { Log.Exception("events", e); }
            finally { _photoBusy = false; }
        }

        /// <summary>One frame of the nod through a small square camera at the player's camera (the polaroid's photo). The camera
        /// is enabled for one frame of the normal pipeline (as the furniture thumbnails do): a direct Camera.Render() or a
        /// render request came out black on the Seeker.</summary>
        private Camera _photoCam;

        /// <summary>The photo camera, made once and kept disabled (no component churn on the shutter frame).</summary>
        private Camera PhotoCamera()
        {
            if (_photoCam != null) return _photoCam;
            if (_photoRt == null) { _photoRt = new RenderTexture(320, 320, 24, RenderTextureFormat.RGB565) { name = "bp26_nod_photo", antiAliasing = 1 }; _photoRt.Create(); }     // no alpha: the camera writes alpha 0, which UI Toolkit draws as transparent
            var go = new GameObject("bp26_photo_cam");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _photoCam = go.AddComponent<Camera>();
            _photoCam.enabled = false;
            var data = _photoCam.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderShadows = false; data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            _photoCam.targetTexture = _photoRt;
            return _photoCam;
        }

        /// <summary>One unseen photo frame while the skin loads, so the shutter frame never compiles the photo camera's
        /// shader variants or first-uploads its texture (a 260 ms frame on the Seeker otherwise).</summary>
        public async void PrewarmPhoto(Transform near)
        {
            if (_disposed || near == null) return;
            await Awaitable.WaitForSecondsAsync(1.5f);
            if (_disposed || near == null || _photoBusy) return;
            await Snap(near);
            Log.Info("events", "photo camera prewarmed");
        }

        private async Task<RenderTexture> Snap(Transform target)
        {
            var main = CameraRig.Instance != null ? CameraRig.Instance.Camera : Camera.main;
            if (main == null || target == null) return null;
            try
            {
                var cam = PhotoCamera();
                cam.cullingMask = main.cullingMask;
                cam.clearFlags = main.clearFlags; cam.backgroundColor = main.backgroundColor;
                cam.nearClipPlane = main.nearClipPlane;
                cam.farClipPlane = Mathf.Min(main.farClipPlane, 90f);       // near things only (the flash covers the moment)
                cam.depth = main.depth - 1f;
                var to = target.position - main.transform.position;
                float d = Mathf.Max(1.2f, to.magnitude);
                cam.transform.position = target.position - to.normalized * Mathf.Min(d, 3.2f) + Vector3.up * 0.15f;
                cam.transform.LookAt(target.position);
                cam.fieldOfView = 34f;
                // the medal turns its face to the lens for the shot (its front faces local -z)
                if (target.name == "disc")
                {
                    BreakpointVenue.Frozen = target;
                    target.rotation = Quaternion.LookRotation(target.position - cam.transform.position, Vector3.up);
                }
                cam.targetTexture = _photoRt;
                bool done = false;
                void End(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera c) { if (c == cam) done = true; }
                UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += End;
                cam.enabled = true;      // one frame of the normal pipeline (a direct Render() came out black on the Seeker)
                for (int f = 0; f < 6 && !done; f++) await Awaitable.NextFrameAsync();
                UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= End;
                cam.enabled = false;
                BreakpointVenue.Frozen = null;
                Log.Info("events", "nod photo rendered " + done);
            }
            catch (Exception e) { Log.Warn("events", "photo: " + e.Message); }
            return _photoRt;
        }

        // ------------------------------------------------------------------ trivia, stream

        public void OpenTrivia()
        {
            EventsApp.Pending = "trivia";
            EventsService.Opener?.Invoke(EventsService.AppId);
        }

        public void OpenStream(string url)
        {
            var safe = CalendarEvent.SafeUrl(url);
            if (safe == null) return;
            Log.Info("events", "open the official stream " + safe);
            Application.OpenURL(safe);     // a link-out to the organiser's page; never embedded
        }

        // ------------------------------------------------------------------ the claim moment

        private void OnCompleted(EventDetails d) => _ = ShowClaim(d, 1.2f);

        public async Task ShowClaim(EventDetails d, float delay = 0f)
        {
            if (delay > 0f) await Awaitable.WaitForSecondsAsync(delay);
            if (_disposed || d == null) return;
            _claim?.RemoveFromHierarchy();
            var q = d.Quest ?? new EventQuest();
            var art = El.Div("evt-claim-art");
            var tex = Resources.Load<Texture2D>("UI/Events/lanyard");
            if (tex != null) art.style.backgroundImage = new StyleBackground(tex);
            var cash = El.Text("+" + Money.Cash(0), "evt-claim-row-value");
            var wear = El.Btn(EventText.T("claim.wear"), null, "evt-btn evt-btn-gold");
            var collect = El.Btn(EventText.T("claim.collect"), null, "evt-btn evt-btn-primary");
            var panel = El.Div("evt-claim-panel",
                El.Text("BREAKPOINT 2026 · LONDON", "evt-claim-kicker"),
                El.Text(EventText.T("claim.title"), "evt-claim-title"),
                art,
                El.Div("evt-claim-rows",
                    El.Div("evt-claim-row", El.Text("Cash", "evt-claim-row-label"), cash),
                    El.Div("evt-claim-row", El.Text(EventText.T("reward.lanyard"), "evt-claim-row-label"), El.Text("NEW", "evt-claim-row-value"))),
                El.Text(EventText.T("claim.body"), "evt-claim-note"),
                El.Div("evt-claim-actions", wear, collect));
            var claim = El.Div("evt-claim", panel);
            _claim = claim;
            _layer.Add(claim);
            Bus.Publish(new SfxSignal("sfx_creator_complete"));
            await Awaitable.NextFrameAsync();
            claim.AddToClassList("evt-in");
            art.schedule.Execute(() => art.ToggleInClassList("evt-swing")).Every(1200);
            // count the Cash up
            long target = q.RewardCents;
            float t0 = Time.unscaledTime;
            claim.schedule.Execute(() =>
            {
                float k = Mathf.Clamp01((Time.unscaledTime - t0 - 0.4f) / 1.1f);
                cash.text = "+" + Money.Cash((long)(target * (1f - Mathf.Pow(1f - k, 3f))));
            }).Every(33).Until(() => Time.unscaledTime - t0 > 1.7f);
            claim.schedule.Execute(() => Bus.Publish(new SfxSignal("sfx_cash_in"))).ExecuteLater(500);
            collect.clicked += () => CloseClaim(claim);
            wear.clicked += async () =>
            {
                wear.SetEnabled(false);
                bool ok = await WearLanyard(q.RewardItemKey ?? EventIds.Lanyard);
                wear.text = ok ? EventText.T("claim.worn") : EventText.T("claim.inWardrobe");
                if (ok) Bus.Publish(new SfxSignal("sfx_ui_success"));
            };
        }

        /// <summary>The IRL celebration: the gold lanyard, the IRL badge and the Cash bonus (first check-in only).</summary>
        public async Task ShowIrlClaim(IrlCheckinResult r)
        {
            if (_disposed || r == null) return;
            _claim?.RemoveFromHierarchy();
            var art = El.Div("evt-claim-art");
            var tex = Resources.Load<Texture2D>("UI/Events/lanyard_gold");
            if (tex != null) art.style.backgroundImage = new StyleBackground(tex);
            var badge = El.Div("evt-claim-badge");
            var bt = Resources.Load<Texture2D>("UI/Events/irl_badge");
            if (bt != null) badge.style.backgroundImage = new StyleBackground(bt);
            art.Add(badge);
            var cash = El.Text("+" + Money.Cash(0), "evt-claim-row-value");
            var wear = El.Btn(EventText.T("claim.wear"), null, "evt-btn evt-btn-gold");
            var collect = El.Btn(EventText.T("claim.collect"), null, "evt-btn evt-btn-primary");
            var panel = El.Div("evt-claim-panel evt-claim-gold",
                El.Text("IRL · OLYMPIA LONDON", "evt-claim-kicker"),
                El.Text(EventText.T("irl.claim"), "evt-claim-title"),
                art,
                El.Div("evt-claim-rows",
                    El.Div("evt-claim-row", El.Text("Cash", "evt-claim-row-label"), cash),
                    El.Div("evt-claim-row", El.Text("IRL badge", "evt-claim-row-label"), El.Text("NEW", "evt-claim-row-value")),
                    El.Div("evt-claim-row", El.Text("Gold lanyard", "evt-claim-row-label"), El.Text("NEW", "evt-claim-row-value"))),
                El.Text(EventText.T("irl.claimBody"), "evt-claim-note"),
                El.Div("evt-claim-actions", wear, collect));
            var claim = El.Div("evt-claim", panel);
            _claim = claim;
            _layer.Add(claim);
            Bus.Publish(new SfxSignal("sfx_creator_complete"));
            await Awaitable.NextFrameAsync();
            claim.AddToClassList("evt-in");
            art.schedule.Execute(() => art.ToggleInClassList("evt-swing")).Every(1200);
            long target = r.RewardCents;
            float t0 = Time.unscaledTime;
            claim.schedule.Execute(() =>
            {
                float k = Mathf.Clamp01((Time.unscaledTime - t0 - 0.4f) / 1.1f);
                cash.text = "+" + Money.Cash((long)(target * (1f - Mathf.Pow(1f - k, 3f))));
            }).Every(33).Until(() => Time.unscaledTime - t0 > 1.7f);
            claim.schedule.Execute(() => Bus.Publish(new SfxSignal("sfx_cash_in"))).ExecuteLater(500);
            collect.clicked += () => CloseClaim(claim);
            wear.clicked += async () =>
            {
                wear.SetEnabled(false);
                bool ok = await WearLanyard(EventIds.GoldLanyard);
                wear.text = ok ? EventText.T("claim.worn") : EventText.T("claim.inWardrobe");
            };
        }

        public void CloseAllForDev() { if (_claim != null) CloseClaim(_claim); }

        private void CloseClaim(VisualElement claim)
        {
            claim.RemoveFromClassList("evt-in");
            claim.schedule.Execute(() => { claim.RemoveFromHierarchy(); if (_claim == claim) _claim = null; }).ExecuteLater(320);
        }

        /// <summary>Puts the lanyard in the accessory slot: PATCH /characters/me/look with the worn look and accId swapped.</summary>
        /// <summary>Equips a lanyard: PATCH /characters/me/look {"accId"} (only that slot changes; the server checks ownership).</summary>
        public static async Task<bool> WearLanyard(string itemKey)
        {
            try
            {
                var body = new Newtonsoft.Json.Linq.JObject { ["accId"] = itemKey }.ToString(Formatting.None);
                await Services.Get<IRestClient>().Send<Newtonsoft.Json.Linq.JToken>("PATCH", "/characters/me/look", body, true, Guid.NewGuid().ToString("N"));
                if (Services.TryGet<PlayerProfile>(out var profile)) await profile.Refresh();
                Bus.Publish(new ProfileChangedSignal());
                Log.Info("events", "wearing " + itemKey);
                return true;
            }
            catch (ApiException a) when (a.Code == "ITEM_NOT_OWNED")
            {
                Log.Warn("events", "wear lanyard: not owned yet");
                Bus.Publish(ToastSignal.Text(EventText.T("claim.notOwned"), ToastKind.Warning));
                return false;
            }
            catch (Exception e) { Log.Warn("events", "wear lanyard failed: " + (e is ApiException a ? a.Code : e.Message)); return false; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Changed -= Bind;
            if (PhoneWidgets.EventsLine == (Func<(string, string, bool)?>)HomeLine) PhoneWidgets.EventsLine = null;
            if (WorldLabels.Badge == (Func<AvatarView, string>)NameBadge) WorldLabels.Badge = null;
            Unbind();
            if (_photoCam != null) UnityEngine.Object.Destroy(_photoCam.gameObject);
            if (_photoRt != null) { _photoRt.Release(); UnityEngine.Object.Destroy(_photoRt); }
        }
    }
}
