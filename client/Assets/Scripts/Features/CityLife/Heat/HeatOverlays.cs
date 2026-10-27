using System;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.UI.DS;
using CTLife.World;
using CTLife.World.Vehicles;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife.Heat
{
    /// <summary>
    /// The heat / police / jail layer over the game (lane B, 2026-10-25, backend #17):
    /// - the HeatPlate (stars under Cash) from <see cref="HeatService"/>;
    /// - the Steal prompt: a stopped NPC traffic car offers "Steal" (a `traffic_*` id, `steal: true`); another player's
    ///   parked car answers STEAL_REQUIRED and a sheet asks first; refusals get their own line (taken, hovercar, jail, rate);
    /// - pursuits: the cruisers (<see cref="PolicePursuits"/>), a banner for your own, BUSTED or "you lost them" at the end;
    /// - CT Jail: on `jailed`, out of the car and into the `ct_jail` venue (kept inside it until released), a countdown and
    ///   "Pay bail"; on `released` the panel goes and the door is yours.
    /// Visual references for the plate / prompt / panel: ct-life-evidence/heat-police-20261025/refs (SOURCES.md).
    /// </summary>
    public sealed class HeatOverlays : IDisposable
    {
        private readonly VisualElement _layer;
        private readonly Session _session;
        private HeatService _svc;
        private PolicePursuits _police;
        private VisualElement _jail, _banner, _sheet, _bailCard;
        private Label _jailClock, _bailClock;
        private Button _bail, _bailPay;
        private bool _bailBusy;
        private string _toastedPursuit;
        private HeatPlate _plate;
        private IVisualElementScheduledItem _tick;
        private bool _disposed, _explained;
        private float _confineAt;
        private static StyleSheet _style;
        private readonly System.Random _rng = new System.Random();

        public static HeatOverlays Current { get; private set; }
        public HeatService Service => _svc;

        public HeatOverlays(VisualElement modalLayer)
        {
            _layer = modalLayer;
            _session = Services.Get<Session>();
            _session.Changed += Bind;
            var root = UiRoot.Instance != null ? UiRoot.Instance.Root : null;
            _style ??= Resources.Load<StyleSheet>("UI/heat");
            if (root != null && _style != null && !root.styleSheets.Contains(_style)) root.styleSheets.Add(_style);
            WorldOffers.Add(StealOffer);
            VehicleNetworkBridge.StealRequired = OnStealRequired;
            VehicleNetworkBridge.EnterRefused = OnEnterRefused;
            try { _explained = PlayerPrefs.GetInt("heat.explained", 0) == 1; } catch (Exception) { }
            Current = this;
            Bind();
        }

        private void Bind()
        {
            if (_disposed) return;
            var user = _session.IsSignedIn ? _session.UserId : null;
            if (_svc != null && !_svc.Disposed && _svc.UserId == user) return;
            Unbind();
            if (string.IsNullOrEmpty(user)) return;
            _svc = new HeatService(user);
            _svc.Changed += Render;
            _svc.PursuitStarted += OnPursuit;
            _svc.PursuitEnded += OnPursuitEnded;
            _svc.JailedNow += OnJailed;
            _svc.Released += OnReleased;
            _svc.VehicleSignal += OnVehicleSignal;
            _police = PolicePursuits.Create(_svc);
            _tick = _layer.schedule.Execute(Tick).Every(500);
        }

        private void Unbind()
        {
            _tick?.Pause(); _tick = null;
            if (_police != null) UnityEngine.Object.Destroy(_police.gameObject);
            _police = null;
            if (_svc != null)
            {
                _svc.Changed -= Render; _svc.PursuitStarted -= OnPursuit; _svc.PursuitEnded -= OnPursuitEnded;
                _svc.JailedNow -= OnJailed; _svc.Released -= OnReleased; _svc.VehicleSignal -= OnVehicleSignal;
                _svc.Dispose();
            }
            _svc = null;
            CloseJail();
            _banner?.RemoveFromHierarchy(); _banner = null;
            if (_sheet != null) UiRoot.Instance?.Modals?.Close(_sheet);
            _sheet = null;
            HeatPlate.Current?.Set(0);
        }

        // ------------------------------------------------------------------ the plate

        private void Render()
        {
            if (_svc == null) return;
            var me = _svc.Me;
            int stars = me != null ? me.Stars : 0;
            var level = me != null ? me.Level : HeatLevel.Cool;
            bool chased = _svc.MyPursuit != null;
            if (chased) stars = Math.Max(stars, 4);
            string word = HeatText.T("level." + level.ToString().ToLowerInvariant());
            HeatPlate.Current?.Set(stars, word, level == HeatLevel.Wanted || chased ? 3 : level == HeatLevel.Hot ? 2 : level == HeatLevel.Warm ? 1 : 0, chased);
            if (stars > 0 && !_explained) Explain();
            // a chase already running when the game (re)starts gets its objective too
            var mine = _svc.MyPursuit;
            if (mine != null && mine.PursuitId != null && mine.PursuitId != _toastedPursuit && mine.TargetUserId == _svc.UserId) OnPursuit(mine);
            if (_svc.Jailed && _jail == null) OnJailed(_svc.Jail);
        }

        private void Explain()
        {
            _explained = true;
            try { PlayerPrefs.SetInt("heat.explained", 1); PlayerPrefs.Save(); } catch (Exception) { }
            Objective(HeatText.T("explain.body"), 7000, "info");
        }

        private void Tick()
        {
            if (_svc == null) return;
            // a HUD built after the last server read (venue change, first frame) gets the current stars at once
            if (HeatPlate.Current != _plate) { _plate = HeatPlate.Current; Render(); }
            if (_jail != null) UpdateJail();
        }

        // ------------------------------------------------------------------ steal

        /// <summary>A stopped (or crawling) NPC traffic car beside the player offers "Steal".</summary>
        private WorldOffer StealOffer(Vector3 player)
        {
            var traffic = TrafficSystem.Current;
            var sys = VehicleSystem.Current;
            if (_svc == null || traffic == null || sys == null || sys.Driving != null || sys.Busy) return null;
            if (VenueSession.Current == null || !VenueSession.Current.IsIsland) return null;
            TrafficCar best = null; float bestD = 3.2f;
            foreach (var c in traffic.Cars)
            {
                if (c == null || c.Vehicle == null || c.Vehicle.Kind == VehicleKind.Roadstar || c.Vehicle.Kind == VehicleKind.Helicopter) continue;
                if (!c.Knocked && c.Speed > 2.2f) continue;
                if (c.Knocked && c.Body != null && c.Body.linearVelocity.magnitude > 1.5f) continue;
                float d = Mathf.Min(Vector3.Distance(player, c.Vehicle.EntryPoint), Vector3.Distance(player, c.transform.position) - 1.3f);
                if (d < bestD) { bestD = d; best = c; }
            }
            if (best == null) return null;
            var car = best;
            return new WorldOffer
            {
                id = "steal:" + car.GetInstanceID(), verbKey = "heat.steal", icon = "steal", tagKey = "heat.stealTag",
                follow = car.transform, position = Vector3.up * 1.9f, range = 3.4f, perform = () => StealTraffic(car),
            };
        }

        private void StealTraffic(TrafficCar car)
        {
            if (_svc != null && _svc.Jailed) { Toast(HeatText.T("steal.jailed"), ToastKind.Warning); return; }
            var traffic = TrafficSystem.Current;
            var sys = VehicleSystem.Current;
            if (traffic == null || sys == null || car == null) return;
            var v = traffic.Hijack(car);
            if (v == null) return;
            var id = v.GetComponent<VehicleNetworkIdentity>() ?? v.gameObject.AddComponent<VehicleNetworkIdentity>();
            id.Id = HeatRules.TrafficId(_rng);
            id.Steal = true;
            Log.Info("heat", "steal: traffic " + v.specId);
            Bus.Publish(new SfxSignal("sfx_ui_tap"));
            if (!sys.Enter(v)) Toast(HeatText.T("steal.far"), ToastKind.Warning);
        }

        /// <summary>Another player's parked car: ask, then enter again with `steal: true`.</summary>
        private void OnStealRequired(Vehicle v, string kind)
        {
            if (_disposed || v == null) return;
            var ui = UiRoot.Instance;
            if (ui?.Modals == null) { Log.Warn("heat", "steal confirm: no modal host"); return; }
            if (_sheet != null) ui.Modals.Close(_sheet);
            Log.Info("heat", "steal confirm for a player's " + VehicleName(v));
            var name = VehicleName(v);
            // design system confirm (v2): the police car hero, one line, the heat it costs, Steal it / Leave it
            var yes = new DsAction { Label = HeatText.T("steal.confirm"), Kind = BtnKind.Danger, Name = "heat-steal-yes", OnClick = () =>
            {
                CloseSheet();
                var id = v != null ? v.GetComponent<VehicleNetworkIdentity>() : null;
                if (id == null || VehicleSystem.Current == null) return;
                id.Steal = true;
                Log.Info("heat", "steal: player vehicle confirmed");
                VehicleSystem.Current.Enter(v);
            } };
            var no = new DsAction { Label = HeatText.T("steal.cancel"), Kind = BtnKind.Text, Name = "heat-steal-no", OnClick = CloseSheet };
            _sheet = Ds.Confirm(HeatText.F("steal.title", name), HeatText.T("steal.body"), yes, no, Ds.Hero("police-car", Tone.Danger),
                Ds.Chip(HeatText.F("steal.tag", HeatRules.StealPlayerHeat), Tone.Danger, "star"));
            _sheet.name = "heat-steal-sheet";
            ui.Modals.Show(_sheet, DismissMode.Back, () => _sheet = null);
        }

        private void CloseSheet() { var s = _sheet; _sheet = null; if (s != null) UiRoot.Instance?.Modals?.Close(s); }

        private void OnEnterRefused(string code, Vehicle v)
        {
            string key = code switch
            {
                "VEHICLE_NOT_STEALABLE" => "steal.notStealable",
                "VEHICLE_TAKEN" => "steal.taken",
                "RATE_LIMITED" => "steal.rate",
                "JAILED" => "steal.jailed",
                "VEHICLE_TOO_FAR" => "steal.far",
                _ => null,
            };
            Toast(key != null ? HeatText.T(key) : Strings.T("vehicle.entryUnavailable"), ToastKind.Warning);
            // a hijacked traffic car the server would not hand over: it stays parked where it stopped
            if (code == "JAILED") _svc?.Refresh();
        }

        private void OnVehicleSignal(HeatSignal s)
        {
            var sys = VehicleSystem.Current;
            if (s.Type == "vehicle_stolen") { Toast(HeatText.F("stolen.toast", KindName(s.Kind)), ToastKind.Warning); return; }
            if (s.Type != "vehicle_returned") return;
            var driving = sys != null ? sys.Driving : null;
            var id = driving != null ? driving.GetComponent<VehicleNetworkIdentity>() : null;
            if (id != null && id.Id == s.VehicleId)
            {
                // the thief's time is up: out of the owner's car
                Toast(HeatText.T("returned.thief"), ToastKind.Info);
                sys.Exit();
            }
            else Toast(HeatText.F("returned.toast", KindName(s.Kind)), ToastKind.Good);
        }

        // ------------------------------------------------------------------ pursuits

        private void OnPursuit(PursuitInfo p)
        {
            if (_svc == null) return;
            if (p.TargetUserId == _svc.UserId)
            {
                _toastedPursuit = p.PursuitId;
                Bus.Publish(new SfxSignal("sfx_ui_error"));
                Objective(HeatText.F("pursuit.start", NameTable.Get("npc.sleuth", "The Sleuth"), (int)p.EscapeRadiusM + " m", (int)p.EscapeSeconds + " s"), 5500, "siren");
            }
            else Toast(HeatText.T("pursuit.nearby"), ToastKind.Info);
            Render();
        }

        private void OnPursuitEnded(string id, string outcome)
        {
            if (_svc == null) return;
            if (outcome == "caught") Busted();
            else Passed(HeatText.T("passed.escaped"), HeatText.T("pursuit.escaped"));
            Render();
        }

        /// <summary>GTA's WASTED band: a full-width dark strip across the middle, "BUSTED" in red, who booked you under it.</summary>
        private void Busted()
        {
            var band = El.Div("heat-band", El.Text(HeatText.T("pursuit.caught"), "heat-band-word"), El.Text(HeatText.T("jail.sub"), "heat-band-sub"));
            band.pickingMode = PickingMode.Ignore;
            _layer.Add(band);
            Bus.Publish(new SfxSignal("sfx_ui_error"));
            band.schedule.Execute(() => band.AddToClassList("heat-in")).ExecuteLater(20);
            band.schedule.Execute(() => band.RemoveFromClassList("heat-in")).ExecuteLater(2700);
            band.schedule.Execute(() => band.RemoveFromHierarchy()).ExecuteLater(3100);
        }

        /// <summary>GTA's MISSION PASSED line: a yellow word top centre and one line under it, for about two seconds.</summary>
        private void Passed(string word, string line)
        {
            var p = El.Div("heat-passed", El.Text(word, "heat-passed-word"), El.Text(line, "heat-passed-sub"));
            p.pickingMode = PickingMode.Ignore;
            _layer.Add(p);
            p.schedule.Execute(() => p.AddToClassList("heat-in")).ExecuteLater(20);
            p.schedule.Execute(() => p.RemoveFromClassList("heat-in")).ExecuteLater(2300);
            p.schedule.Execute(() => p.RemoveFromHierarchy()).ExecuteLater(2700);
        }

        /// <summary>A police objective on the one toast channel (v2: the police mark tile, plain text).</summary>
        private static void Objective(string text, int ms, string icon)
        {
            UiRoot.Instance?.Toasts?.Show(new DsToastSpec { Text = text, Brand = BrandTable.Police, Icon = icon, Seconds = ms / 1000f, Key = "heat-objective" });
        }

        // ------------------------------------------------------------------ jail

        private void OnJailed(JailInfo j)
        {
            if (_disposed || j == null) return;
            var sys = VehicleSystem.Current;
            if (sys != null && sys.Driving != null) sys.Exit();
            _ = ToJail();
            if (_jail != null) { UpdateJail(); return; }
            // v2: a jail pill under the minimap (the release clock + Bail, which reopens the card) and the bail card (mockup c)
            _jailClock = Ds.Text("", "ds-t-hud");
            _bail = new Button(ShowBailCard) { text = "", name = "heat-bail" };
            _bail.AddToClassList("ds-btn"); _bail.AddToClassList("ds-btn--primary"); _bail.AddToClassList("ds-btn--sm"); _bail.AddToClassList("heat-bail-btn");
            _bail.Add(Ds.Text(HeatText.T("bail.short.btn"), "ds-btn-label"));
            var badge = new VisualElement { pickingMode = PickingMode.Ignore };
            badge.AddToClassList("ds-badge-ic");
            badge.Add(Ds.Icon("clock", 36, "white"));
            var col = new VisualElement { pickingMode = PickingMode.Ignore };
            col.AddToClassList("ds-hud-pill-col");
            col.Add(_jailClock);
            col.Add(Ds.Text(HeatText.T("jail.title"), "ds-t-hud-caption"));
            _jail = new VisualElement { name = "heat-jail" };
            _jail.AddToClassList("heat-jail"); _jail.AddToClassList("ds-hud-pill"); _jail.AddToClassList(BrandTable.ClassFor(BrandTable.Police));
            _jail.Add(badge); _jail.Add(col); _jail.Add(_bail);
            _layer.Add(_jail);
            In(_jail);
            UpdateJail();
            // after the BUSTED band
            _layer.schedule.Execute(ShowBailCard).ExecuteLater(_svc != null && _svc.MyPursuit == null ? 3200 : 300);
        }

        private void UpdateJail()
        {
            if (_jail == null || _svc == null) return;
            var j = _svc.Jail;
            if (j == null) { CloseJail(); return; }
            long left = j.UntilMs - _svc.NowMs;
            _jailClock.text = HeatRules.Clock(Math.Max(0, left));
            if (_bailClock != null) _bailClock.text = HeatRules.Clock(Math.Max(0, left));
            if (left <= -3000) _svc.Refresh();
            // kept inside the jail until released (the server refuses other venues and cars anyway)
            if (Time.unscaledTime >= _confineAt) { _confineAt = Time.unscaledTime + 1f; Confine(); }
        }

        /// <summary>The bail card (v2 mockup c): Bail / Free in tiles, Pay (police blue), Wait it out.</summary>
        private void ShowBailCard()
        {
            var ui = UiRoot.Instance;
            var j = _svc?.Jail;
            if (_disposed || ui?.Modals == null || j == null || (_bailCard != null && _bailCard.panel != null)) return;
            var bail = Ds.Stat(HeatText.T("bail.label"), Money.Cash(j.BailCents), Ds.Coin, name: "heat-bail-amount");
            var free = Ds.Stat(HeatText.T("bail.free"), HeatRules.Clock(Math.Max(0, j.UntilMs - _svc.NowMs)), "clock", name: "heat-bail-free");
            _bailClock = free.Q<Label>("ds-tile-value");
            VisualElement card = null;
            card = Ds.Dialog(new DialogSpec
            {
                Name = "heat-bail-card",
                Brand = BrandTable.Police,
                Hero = Ds.HeroBrand(BrandTable.Police),
                Title = HeatText.F("bail.title", HeatText.T("jail.title")),
                Subtitle = HeatText.T("bail.sub"),
                Tiles = { bail, free },
                Width = DialogWidth.Confirm,
                Primary = new DsAction { Label = HeatText.F("bail.pay", Money.Cash(j.BailCents)), Name = "heat-bail-pay", OnClick = () => PayBail(card) },
                Secondary = new DsAction { Label = HeatText.T("bail.wait"), Kind = BtnKind.Text, Name = "heat-bail-wait", OnClick = () => ui.Modals.Close(card) },
            });
            _bailPay = card.Q<Button>("heat-bail-pay");
            _bailCard = card;
            ui.Modals.Show(card, DismissMode.Back, () => { _bailCard = null; _bailClock = null; }, "heat-bail-card");
        }

        private async void PayBail(VisualElement card)
        {
            if (_svc == null || _bailBusy) return;
            _bailBusy = true;
            Ds.SetBusy(_bailPay, true);
            Ds.SetError(card, null);
            try
            {
                await _svc.PayBail();
                UiRoot.Instance?.Modals?.Close(card);
            }
            catch (Exception e) { Ds.SetError(card, HeatService.Explain(e)); }
            finally { _bailBusy = false; if (_bailPay != null) Ds.SetBusy(_bailPay, false); }
        }

        private void OnReleased(string reason)
        {
            CloseJail();
            Passed(HeatText.T("passed.free"), HeatText.T("jail.free"));
            Bus.Publish(new SfxSignal("sfx_ui_success"));
        }

        private void CloseJail()
        {
            var j = _jail; _jail = null; _jailClock = null; _bail = null;
            if (_bailCard != null) UiRoot.Instance?.Modals?.Close(_bailCard);
            _bailCard = null; _bailClock = null; _bailPay = null;
            Out(j);
        }

        /// <summary>Walks the player into CT Jail's venue (its zone joins the `ct_jail` room).</summary>
        private async Task ToJail()
        {
            var s = VenueSession.Current;
            if (s == null || !s.IsIsland || s.LocalPlayer == null || s.Island == null) return;
            var venue = s.Island.Find(HeatRules.JailVenue);
            if (venue == null) { Log.Warn("heat", "no ct_jail venue on the island"); return; }
            if (s.Island.ZoneAt(s.LocalPlayer.transform.position) == venue) return;
            var c = venue.walk.center;
            var at = new Vector3(c.x, venue.elevation, c.y);
            try { await s.Island.EnsureCells(at, 120f, _svc != null ? _svc.Life : default); } catch (Exception) { return; }
            if (s.LocalPlayer == null) return;
            float g = IslandStreamer.GroundHeight(at.x, at.z);
            if (CharacterBody.GroundAt(at + Vector3.up * 3f, out float floor)) g = float.IsNaN(g) ? floor : Mathf.Max(g, floor);
            s.LocalPlayer.SnapTo(new Vector3(at.x, (float.IsNaN(g) ? venue.elevation : g) + 0.05f, at.z), venue.yaw * Mathf.Deg2Rad);
            CameraRig.Instance?.SettleBehind();
            Log.Info("heat", "into CT Jail");
        }

        private void Confine()
        {
            var s = VenueSession.Current;
            if (s == null || !s.IsIsland || s.LocalPlayer == null || s.Island == null) return;
            var venue = s.Island.Find(HeatRules.JailVenue);
            if (venue == null) return;
            var p = s.LocalPlayer.transform.position;
            if (IslandStreaming.DistanceToWalk(new Vector2(p.x, p.z), venue) > 1.5f) _ = ToJail();
        }

        // ------------------------------------------------------------------ helpers

        private static void In(VisualElement e) { if (e != null) e.schedule.Execute(() => e.AddToClassList("heat-in")).ExecuteLater(16); }
        private static void Out(VisualElement e)
        {
            if (e == null) return;
            e.RemoveFromClassList("heat-in");
            e.schedule.Execute(() => e.RemoveFromHierarchy()).ExecuteLater(260);
        }

        private static void Toast(string text, ToastKind kind = ToastKind.Info) => Bus.Publish(ToastSignal.Text(text, kind));

        private static string VehicleName(Vehicle v) => v != null && v.Spec != null ? Strings.T(v.Spec.nameKey).ToLowerInvariant() : "car";
        private static string KindName(string kind) => string.IsNullOrEmpty(kind) ? "car" : kind == "moped" || kind == "scooter" ? "scooter" : kind;

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        /// <summary>Dev captures: the help box for another player's car, without needing a second player.</summary>
        public void DevHelpBox(Vehicle v) { Log.Info("heat", "dev: steal confirm " + (v != null ? v.specId : "none")); OnStealRequired(v, v != null ? v.specId : "sedan"); }
        public void DevCloseHelpBox() => CloseSheet();
#endif

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Changed -= Bind;
            WorldOffers.Remove(StealOffer);
            if (VehicleNetworkBridge.StealRequired == (Action<Vehicle, string>)OnStealRequired) VehicleNetworkBridge.StealRequired = null;
            if (VehicleNetworkBridge.EnterRefused == (Action<string, Vehicle>)OnEnterRefused) VehicleNetworkBridge.EnterRefused = null;
            Unbind();
            if (Current == this) Current = null;
        }
    }
}
