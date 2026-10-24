using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// Furniture in the flats (2026-10-23, backend PR #8 placements):
    /// - whenever a flat's room is streamed in and we may know its home (our own, or a guest's when the server says which),
    ///   GET /homes/{homeId}/placements and stand the pieces in the room (owner and visitors see the same);
    /// - Decorate (inside your own flat, from the world prompt or the Home app): pick an owned piece from the tray, a ghost
    ///   with its footprint outline (green fits / red blocked) follows the finger on the floor, two fingers or the Rotate
    ///   button turn it, Place saves the whole set with PUT /homes/{homeId}/placements. Blocked: outside the room's walls,
    ///   the room's own walls and furniture (its colliders), another placed piece, or the entrance.
    /// Room space: origin at the entrance's floor centre, +Z out to the street; placements are metres in it, yaw in radians.
    /// </summary>
    public sealed class PlaceMode : MonoBehaviour
    {
        public const int RenderLayer = 17, BodyLayer = 13;
        public static PlaceMode Current { get; private set; }

        private sealed class Piece
        {
            public Placement p;
            public GameObject go;
            public Footprint f;
        }

        private HomeShopService _svc;
        private VisualElement _layer;
        private InteriorRoom _room; private string _homeId; private int _door = -1;
        private readonly List<Piece> _pieces = new List<Piece>();
        private float _next; private int _loadSeq;
        private Func<Vector3, WorldOffer> _offer;

        // edit session
        public bool Editing { get; private set; }
        private VisualElement _ui, _tray, _bar;
        private Label _hint;
        private Label _status;
        private Piece _sel; private bool _selIsNew; private Vector3 _selStartPos; private float _selStartYaw;
        private LineRenderer _outline; private Material _okMat, _badMat;
        private bool _fits; private bool _saving;
        private bool _camSaved; private Camera _cam; private Vector3 _camDir; private StyleEnum<DisplayStyle> _hudDisplay;
        private readonly Dictionary<int, Vector2> _pointers = new Dictionary<int, Vector2>();
        private Vector3? _lastFloor; private float? _lastTwist; private float _twistAccum;

        public static PlaceMode Create(HomeShopService svc, VisualElement layer)
        {
            var go = new GameObject("Home placements");
            DontDestroyOnLoad(go);
            var pm = go.AddComponent<PlaceMode>();
            pm._svc = svc; pm._layer = layer;
            return pm;
        }

        private void Awake()
        {
            Current = this;
            _offer = Offer;
            WorldOffers.Add(_offer);
        }

        private void OnDestroy()
        {
            WorldOffers.Remove(_offer);
            ExitEdit(save: false);
            ClearPieces();
            if (ReferenceEquals(Current, this)) Current = null;
        }

        // ------------------------------------------------------------------ the room's furniture

        private void Update()
        {
            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + 0.4f;
                Sync();
            }
            if (Editing) EditFrame();
        }

        private void Sync()
        {
            var rooms = IslandInteriors.Current;
            var room = rooms != null && rooms.ActiveRoom != null && rooms.ActiveRoom.gameObject.activeSelf ? rooms.ActiveRoom : null;
            int door = room != null ? rooms.ActiveDoor : -1;
            string unit = door >= 0 ? rooms.UnitOf(door) : null;
            string home = unit != null && _svc != null ? _svc.HomeIdFor(unit) : null;
            if (room == _room && door == _door && home == _homeId) return;
            if (Editing) ExitEdit(save: false);
            ClearPieces();
            _room = room; _door = door; _homeId = home;
            if (room != null && home != null) _ = Load(home);
        }

        public async Task Load(string homeId)
        {
            int seq = ++_loadSeq;
            try
            {
                var list = await _svc.Api.Placements(homeId, _svc.Life);
                if (seq != _loadSeq || _room == null || _homeId != homeId) return;
                Show(list);
                Log.Info("placements", $"{list.Count} piece(s) in {homeId}");
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("placements", "GET placements: " + (e is ApiException a ? a.Code : e.Message)); }
        }

        private void Show(List<Placement> list)
        {
            ClearPieces();
            foreach (var p in list)
            {
                var piece = Spawn(p);
                if (piece != null) _pieces.Add(piece);
            }
        }

        private Piece Spawn(Placement p)
        {
            if (_room == null) return null;
            var go = ShopProps.SpawnFurniture(p.ModelKey, _room.transform);
            if (go == null) return null;
            var f = p.Footprint ?? new Footprint { W = 0.6f, D = 0.6f, H = 0.8f };
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = RenderLayer;
            // the body: a box on the Prop layer stops characters (not the camera); the renderers stay on Interior so they are
            // drawn while the world outside is hidden
            var body = new GameObject("body") { layer = BodyLayer };
            body.transform.SetParent(go.transform, false);
            var box = body.AddComponent<BoxCollider>();
            box.size = new Vector3(f.W, Mathf.Max(0.2f, f.H), f.D);
            box.center = new Vector3(0f, box.size.y * 0.5f, 0f);
            go.transform.localPosition = new Vector3(p.X, 0f, p.Z);
            go.transform.localRotation = Quaternion.Euler(0f, p.RotY * Mathf.Rad2Deg, 0f);
            return new Piece { p = p, go = go, f = f };
        }

        private void ClearPieces()
        {
            foreach (var pc in _pieces) if (pc.go != null) Destroy(pc.go);
            _pieces.Clear();
        }

        // ------------------------------------------------------------------ Decorate

        public bool CanDecorate => _svc != null && _svc.InsideOwnFlat && _room != null && _homeId != null && _homeId == _svc.MyHome?.HomeId;

        private WorldOffer Offer(Vector3 player)
        {
            if (Editing || !CanDecorate) return null;
            var me = VenueSession.Current != null ? VenueSession.Current.LocalPlayer : null;
            if (me == null) return null;
            return new WorldOffer
            {
                id = "decorate", verbKey = "interact.decorate", icon = "edit", tagKey = "interact.tag.home", primary = false,
                position = Vector3.up * 2.15f, follow = me.transform, range = 99f, perform = () => _ = EnterEdit(),
            };
        }

        public async Task EnterEdit()
        {
            if (Editing || !CanDecorate) { if (!CanDecorate) Bus.Publish(ToastSignal.Text(ShopText.T("home.decorate.note"))); return; }
            Editing = true;
            var me = VenueSession.Current?.LocalPlayer;
            if (me != null) { me.StandIfSeated(); me.SetMoveInput(Vector2.zero); }
            BuildUi();
            AimCamera(true);
            try { await _svc.RefreshItems(); }
            catch (Exception e) { Log.Warn("placements", "GET /me/items: " + (e is ApiException a ? a.Code : e.Message)); }
            if (Editing) FillTray();
        }

        public void ExitEdit(bool save)
        {
            if (!Editing) return;
            if (_sel != null) Deselect(revert: true);
            Editing = false;
            _ui?.RemoveFromHierarchy(); _ui = null;
            if (_outline != null) _outline.gameObject.SetActive(false);
            AimCamera(false);
            _pointers.Clear();
        }

        /// <summary>
        /// While decorating, the follow rig steps aside: a camera just under the ceiling, a few metres back from the piece on the
        /// side it was looking from, eases after the piece and looks down at it (walls pull it in, never through). The HUD hides.
        /// </summary>
        private void AimCamera(bool on)
        {
            var rig = CameraRig.Instance;
            var hud = UiRoot.Instance != null ? UiRoot.Instance.HudLayer : null;
            if (on)
            {
                if (rig == null) return;
                _cam = rig.Camera != null ? rig.Camera : Camera.main;
                if (_cam == null) return;
                rig.enabled = false; _camSaved = true;
                var me = VenueSession.Current?.LocalPlayer;
                var focus = me != null ? me.transform.position : _cam.transform.position;
                _camDir = _cam.transform.position - focus; _camDir.y = 0f;
                _camDir = _camDir.sqrMagnitude > 1e-4f ? _camDir.normalized : -(me != null ? me.transform.forward : Vector3.forward);
                if (hud != null) { _hudDisplay = hud.style.display; hud.style.display = DisplayStyle.None; }
            }
            else if (_camSaved)
            {
                _camSaved = false;
                if (hud != null) hud.style.display = _hudDisplay;
                if (rig != null)
                {
                    rig.enabled = true;
                    var me = VenueSession.Current?.LocalPlayer;
                    if (me != null) rig.FollowAvatar(me.transform);
                    rig.SettleBehind();
                    rig.SnapToTarget();
                }
            }
        }

        private void DriveCamera(bool snap)
        {
            if (!_camSaved || _cam == null || _room == null) return;
            var me = VenueSession.Current?.LocalPlayer;
            var focus = _sel != null ? _sel.go.transform.position : me != null ? me.transform.position + me.transform.forward * 1.2f : _room.transform.position;
            float floor = _room.transform.position.y;
            var eye = focus + Vector3.up * 1.0f;
            var want = focus + _camDir * 3.3f; want.y = floor + Mathf.Min(2.35f, _room.boundsMax.y - 0.25f);
            var to = want - eye;
            if (Physics.SphereCast(eye, 0.2f, to.normalized, out var hit, to.magnitude, 1 << RenderLayer, QueryTriggerInteraction.Ignore))
                want = eye + to.normalized * Mathf.Max(0.6f, hit.distance - 0.1f);
            var look = Quaternion.LookRotation((focus + Vector3.up * 0.25f) - want);
            var t = _cam.transform;
            float k = snap ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime * 5f);
            t.SetPositionAndRotation(Vector3.Lerp(t.position, want, k), Quaternion.Slerp(t.rotation, look, k));
        }

        // ------------------------------------------------------------------ the overlay

        private void BuildUi()
        {
            _ui = new VisualElement { name = "place-mode" };
            _ui.AddToClassList("hs-place");
            _ui.pickingMode = PickingMode.Position;
            _ui.RegisterCallback<PointerDownEvent>(OnDown);
            _ui.RegisterCallback<PointerMoveEvent>(OnMove);
            _ui.RegisterCallback<PointerUpEvent>(OnUp);
            _ui.RegisterCallback<PointerCancelEvent>(e => { _pointers.Remove(e.pointerId); _lastFloor = null; _lastTwist = null; });
            var top = El.Div("hs-place-top");
            top.Add(El.Text(ShopText.T("place.title"), "hs-place-title"));
            top.Add(El.Div("ct-grow"));
            var done = El.Btn(CityLifeStrings.T("common.close"), () => ExitEdit(save: false), "hs-glass-btn");
            done.name = "place-close";
            top.Add(done);
            _ui.Add(top);
            _hint = El.Text(ShopText.T("place.pick"), "hs-place-hint");
            _hint.pickingMode = PickingMode.Ignore;
            _ui.Add(_hint);
            _status = El.Text("", "hs-place-status");
            _status.pickingMode = PickingMode.Ignore;
            _ui.Add(_status);
            var bottom = El.Div("hs-place-bottom");
            _bar = El.Div("hs-place-bar");
            _bar.Show(false);
            bottom.Add(_bar);
            _tray = new ScrollView(ScrollViewMode.Horizontal);
            _tray.AddToClassList("hs-place-tray");
            bottom.Add(_tray);
            _ui.Add(bottom);
            _layer.Add(_ui);
        }

        private void FillTray()
        {
            if (_tray == null) return;
            _tray.Clear();
            var placed = new HashSet<string>(_pieces.Select(p => p.p.ItemInstanceId));
            var items = _svc.Items.Where(i => i.Placeable && !string.IsNullOrEmpty(i.ModelKey)).ToList();
            if (items.Count == 0) { _tray.Add(El.Text(ShopText.T("place.none"), "hs-place-empty")); return; }
            foreach (var it in items.OrderBy(i => placed.Contains(i.ItemInstanceId)).ThenBy(i => i.Name))
            {
                bool isPlaced = placed.Contains(it.ItemInstanceId) || (!string.IsNullOrEmpty(it.PlacedHomeId) && it.PlacedHomeId != _homeId);
                var card = El.Btn("", () => Pick(it), "hs-tray-item");
                card.name = "tray-" + it.ItemId;
                var art = El.Div("hs-tray-art");
                var rt = FurniturePreview.Instance.Thumbnail(it.ModelKey);
                if (rt != null) art.style.backgroundImage = Background.FromRenderTexture(rt);
                art.pickingMode = PickingMode.Ignore;
                card.Add(art);
                var name = El.Text(it.Name, "hs-tray-name"); name.pickingMode = PickingMode.Ignore; card.Add(name);
                var tag = El.Text(isPlaced ? ShopText.T("placedHere") : ShopText.T("owned"), "hs-tray-tag" + (isPlaced ? " hs-tray-tag-on" : ""));
                tag.pickingMode = PickingMode.Ignore; card.Add(tag);
                card.SetEnabled(!(!string.IsNullOrEmpty(it.PlacedHomeId) && it.PlacedHomeId != _homeId));
                _tray.Add(card);
            }
        }

        private void ShowBar()
        {
            _bar.Clear();
            bool any = _sel != null;
            _bar.Show(any);
            _tray.Show(!any);
            _hint.text = any ? ShopText.T("place.hint") : ShopText.T("place.pick");
            if (!any) return;
            _bar.Add(Round("rotate", ShopText.T("place.rotate"), () => Rotate(Mathf.PI / 4f)));
            if (!_selIsNew) _bar.Add(Round("down", ShopText.T("place.remove"), PutAway));
            _bar.Add(Round("close", ShopText.T("place.cancel"), () => Deselect(revert: true)));
            var place = El.Btn(ShopText.T("place.done"), () => _ = Confirm(), "hs-place-primary");
            place.name = "place-confirm";
            _bar.Add(place);
        }

        private static Button Round(string icon, string label, Action a)
        {
            var b = El.Btn("", a, "hs-round");
            b.name = "place-" + icon;
            var face = El.Div("hs-round-face", new UiIcon(icon)); face.pickingMode = PickingMode.Ignore;
            b.Add(face);
            var l = El.Text(label, "hs-round-label"); l.pickingMode = PickingMode.Ignore;
            b.Add(l);
            return b;
        }

        // ------------------------------------------------------------------ selection

        private void Pick(OwnedItem it)
        {
            if (_room == null || _saving) return;
            if (_sel != null) Deselect(revert: true);
            var existing = _pieces.Find(p => p.p.ItemInstanceId == it.ItemInstanceId);
            if (existing != null) { Select(existing, isNew: false); return; }
            // a new piece: in front of the player, on the floor
            var me = VenueSession.Current?.LocalPlayer;
            var t = _room.transform;
            Vector3 local = me != null ? t.InverseTransformPoint(me.transform.position + me.transform.forward * 1.6f) : new Vector3(0f, 0f, -3f);
            var p = new Placement { ItemInstanceId = it.ItemInstanceId, ItemId = it.ItemId, Name = it.Name, ModelKey = it.ModelKey, Footprint = it.Footprint, X = local.x, Z = local.z, RotY = 0f };
            var piece = Spawn(p);
            if (piece == null) return;
            _pieces.Add(piece);
            Select(piece, isNew: true);
            // nudge it to the nearest spot that fits
            if (!Check(piece)) FindFit(piece);
            Validate();
        }

        private void Select(Piece pc, bool isNew)
        {
            _sel = pc; _selIsNew = isNew;
            _selStartPos = pc.go.transform.localPosition; _selStartYaw = pc.go.transform.localEulerAngles.y;
            SetBody(pc, false);
            EnsureOutline();
            _outline.gameObject.SetActive(true);
            ShowBar();
            Validate();
        }

        private void Deselect(bool revert)
        {
            var pc = _sel;
            _sel = null;
            if (pc != null)
            {
                if (revert && _selIsNew) { _pieces.Remove(pc); Destroy(pc.go); }
                else if (revert) { pc.go.transform.localPosition = _selStartPos; pc.go.transform.localRotation = Quaternion.Euler(0f, _selStartYaw, 0f); SetBody(pc, true); }
                else SetBody(pc, true);
            }
            if (_outline != null) _outline.gameObject.SetActive(false);
            _status.text = "";
            ShowBar();
            FillTray();
        }

        private static void SetBody(Piece pc, bool on)
        {
            var c = pc.go.GetComponentInChildren<BoxCollider>(true);
            if (c != null) c.enabled = on;
        }

        private void Rotate(float radians)
        {
            if (_sel == null) return;
            _sel.go.transform.localRotation *= Quaternion.Euler(0f, radians * Mathf.Rad2Deg, 0f);
            Validate();
        }

        private async void PutAway()
        {
            if (_sel == null) return;
            var pc = _sel; _sel = null;
            _pieces.Remove(pc);
            Destroy(pc.go);
            if (_outline != null) _outline.gameObject.SetActive(false);
            await Save();
            Deselect(revert: false);
        }

        private async Task Confirm()
        {
            if (_sel == null || _saving) return;
            if (!_fits) { Bus.Publish(ToastSignal.Text(ShopText.T("place.blocked"), ToastKind.Warning)); return; }
            var pc = _sel;
            var t = pc.go.transform;
            pc.p.X = t.localPosition.x; pc.p.Z = t.localPosition.z;
            pc.p.RotY = Mathf.Repeat(t.localEulerAngles.y, 360f) * Mathf.Deg2Rad;
            bool ok = await Save();
            if (ok) { _selIsNew = false; Deselect(revert: false); }
        }

        private async Task<bool> Save()
        {
            if (_homeId == null) return false;
            _saving = true;
            _status.text = ShopText.T("place.saving");
            try
            {
                var list = _pieces.Select(p => p.p).ToList();
                var saved = await _svc.Api.SavePlacements(_homeId, list, Guid.NewGuid().ToString("N"), _svc.Life);
                _status.text = "";
                Bus.Publish(ToastSignal.Text(ShopText.T("place.saved"), ToastKind.Good));
                Log.Info("placements", $"saved {saved.Count} piece(s) in {_homeId}");
                return true;
            }
            catch (Exception e)
            {
                var err = CityErrors.From(e, true);
                _status.text = CityLifeStrings.T(err.Key);
                Log.Warn("placements", "PUT placements: " + err.Code);
                return false;
            }
            finally { _saving = false; }
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        /// <summary>Dev (capture route): pick an owned piece by itemId, as a tap on the tray would.</summary>
        public bool DevPick(string itemId)
        {
            var it = _svc.Items.FirstOrDefault(i => i.ItemId == itemId && i.Placeable) ?? _svc.Items.FirstOrDefault(i => i.Placeable);
            if (it == null) return false;
            Pick(it);
            return _sel != null;
        }

        /// <summary>Dev: slide the selected piece by a room-space offset over <paramref name="seconds"/> (a finger drag).</summary>
        public async Task DevDrag(Vector2 by, float seconds)
        {
            if (_sel == null) return;
            var from = _sel.go.transform.localPosition;
            for (float t = 0f; t < 1f && _sel != null; t += Time.deltaTime / seconds)
            {
                float e = t * t * (3f - 2f * t);
                _sel.go.transform.localPosition = from + new Vector3(by.x, 0f, by.y) * e;
                Validate();
                await Awaitable.NextFrameAsync();
            }
        }

        /// <summary>Dev: drag the selected piece to a free spot 1.2-2.6 m away (a grid search over the floor), as a finger would.</summary>
        public async Task DevDragToFit(float seconds)
        {
            if (_sel == null || _room == null) return;
            var t = _sel.go.transform;
            var start = t.localPosition;
            Vector3? best = null; float bestScore = float.MaxValue;
            for (float x = _room.boundsMin.x; x <= _room.boundsMax.x; x += 0.25f)
                for (float z = _room.boundsMin.z; z <= 0f; z += 0.25f)
                {
                    var p = new Vector3(x, 0f, z);
                    float d = Vector3.Distance(p, start);
                    if (d < 1.2f || d > 2.6f) continue;
                    t.localPosition = p;
                    if (!Check(_sel)) continue;
                    float score = Mathf.Abs(d - 1.9f);
                    if (score < bestScore) { bestScore = score; best = p; }
                }
            t.localPosition = start;
            if (best.HasValue) await DevDrag(new Vector2(best.Value.x - start.x, best.Value.z - start.z), seconds);
        }

        public void DevRotate(float degrees) => Rotate(degrees * Mathf.Deg2Rad);
        public Task DevConfirm() => Confirm();
        public bool DevFits => _fits;
#endif

        // ------------------------------------------------------------------ gestures (on the overlay, never the HUD under it)

        private Vector2 Screen(Vector2 panelPos)
        {
            var size = _ui.panel.visualTree.layout.size;
            var world = _ui.LocalToWorld(panelPos);
            return new Vector2(world.x / size.x * UnityEngine.Screen.width, (1f - world.y / size.y) * UnityEngine.Screen.height);
        }

        private bool Floor(Vector2 screen, out Vector3 local)
        {
            local = default;
            var cam = Camera.main;
            if (cam == null || _room == null) return false;
            var ray = cam.ScreenPointToRay(screen);
            var plane = new Plane(_room.transform.up, _room.transform.position);
            if (!plane.Raycast(ray, out var d)) return false;
            local = _room.transform.InverseTransformPoint(ray.GetPoint(d));
            return true;
        }

        private void OnDown(PointerDownEvent e)
        {
            if (e.target != _ui) return;
            _pointers[e.pointerId] = Screen(e.localPosition);
            _ui.CapturePointer(e.pointerId);
            if (_pointers.Count == 1 && _sel == null) TrySelectAt(_pointers[e.pointerId]);
            _lastFloor = null; _lastTwist = null;
        }

        private void OnMove(PointerMoveEvent e)
        {
            if (!_pointers.ContainsKey(e.pointerId) || _sel == null) return;
            _pointers[e.pointerId] = Screen(e.localPosition);
            if (_pointers.Count >= 2)
            {
                var ps = _pointers.Values.Take(2).ToArray();
                float ang = Mathf.Atan2(ps[1].y - ps[0].y, ps[1].x - ps[0].x) * Mathf.Rad2Deg;
                if (_lastTwist.HasValue) { _sel.go.transform.localRotation *= Quaternion.Euler(0f, -Mathf.DeltaAngle(_lastTwist.Value, ang), 0f); Validate(); }
                _lastTwist = ang;
                _lastFloor = null;
                return;
            }
            if (!Floor(_pointers[e.pointerId], out var at)) return;
            if (_lastFloor.HasValue)
            {
                var d = at - _lastFloor.Value; d.y = 0f;
                var p = _sel.go.transform.localPosition + d;
                p.x = Mathf.Clamp(p.x, _room.boundsMin.x, _room.boundsMax.x);
                p.z = Mathf.Clamp(p.z, _room.boundsMin.z, 0f);
                _sel.go.transform.localPosition = new Vector3(p.x, 0f, p.z);
                Validate();
            }
            _lastFloor = at;
        }

        private void OnUp(PointerUpEvent e)
        {
            _pointers.Remove(e.pointerId);
            if (_ui.HasPointerCapture(e.pointerId)) _ui.ReleasePointer(e.pointerId);
            if (_pointers.Count < 2 && _lastTwist.HasValue && _sel != null)
            {
                // a twist settles on the nearest 15 degrees
                var y = _sel.go.transform.localEulerAngles.y;
                _sel.go.transform.localRotation = Quaternion.Euler(0f, Mathf.Round(y / 15f) * 15f, 0f);
                Validate();
            }
            _lastFloor = null; _lastTwist = null;
        }

        private void TrySelectAt(Vector2 screen)
        {
            if (!Floor(screen, out var at)) return;
            foreach (var pc in _pieces)
            {
                var l = pc.go.transform.InverseTransformPoint(_room.transform.TransformPoint(at));
                if (Mathf.Abs(l.x) <= pc.f.W * 0.5f + 0.1f && Mathf.Abs(l.z) <= pc.f.D * 0.5f + 0.1f) { Select(pc, isNew: false); return; }
            }
        }

        // ------------------------------------------------------------------ fitting

        private void EditFrame()
        {
            if (_room == null || !_svc.InsideOwnFlat) { ExitEdit(save: false); return; }
            if (_sel != null) DrawOutline(_sel);
        }

        private void LateUpdate() { if (Editing) DriveCamera(false); }

        private void Validate()
        {
            if (_sel == null) return;
            _fits = Check(_sel);
            if (_outline != null) _outline.sharedMaterial = _fits ? _okMat : _badMat;
            _status.text = _fits ? "" : ShopText.T("place.blocked");
            var b = _bar?.Q<Button>("place-confirm");
            b?.EnableInClassList("hs-place-primary-off", !_fits);
        }

        /// <summary>Footprint corners in room space.</summary>
        private static Vector2[] Corners(Vector3 pos, float yawDeg, Footprint f)
        {
            var q = Quaternion.Euler(0f, yawDeg, 0f);
            var r = new Vector2[4];
            int i = 0;
            foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                var v = q * new Vector3(sx * f.W * 0.5f, 0f, sz * f.D * 0.5f) + pos;
                r[i++] = new Vector2(v.x, v.z);
            }
            return r;
        }

        private bool Check(Piece pc)
        {
            var t = pc.go.transform;
            var c = Corners(t.localPosition, t.localEulerAngles.y, pc.f);
            return PlacementFit.Fits(c, _room.boundsMin, _room.boundsMax, _pieces.Where(o => o != pc).Select(o => Corners(o.go.transform.localPosition, o.go.transform.localEulerAngles.y, o.f)))
                   && !HitsRoom(pc);
        }

        /// <summary>The room's own walls and furniture (its colliders on the Interior and Prop layers), above the floor.</summary>
        private bool HitsRoom(Piece pc)
        {
            var t = pc.go.transform;
            float h = Mathf.Max(0.2f, pc.f.H);
            var center = t.TransformPoint(new Vector3(0f, 0.08f + (h - 0.08f) * 0.5f, 0f));
            var half = new Vector3(pc.f.W * 0.5f - 0.03f, (h - 0.08f) * 0.5f, pc.f.D * 0.5f - 0.03f);
            var hits = Physics.OverlapBox(center, half, t.rotation, (1 << RenderLayer) | (1 << BodyLayer), QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit == null || !hit.transform.IsChildOf(_room.transform)) continue;   // the building, the player
                bool ours = false;
                foreach (var o in _pieces) if (hit.transform.IsChildOf(o.go.transform)) { ours = true; break; }
                if (!ours) return true;
            }
            return false;
        }

        /// <summary>Spiral out from where it is until it fits (a new piece dropped onto the sofa slides off it).</summary>
        private void FindFit(Piece pc)
        {
            var t = pc.go.transform;
            var start = t.localPosition;
            for (int ring = 1; ring <= 14; ring++)
                for (int k = 0; k < 12; k++)
                {
                    float a = k / 12f * Mathf.PI * 2f, r = ring * 0.3f;
                    t.localPosition = start + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    if (Check(pc)) return;
                }
            t.localPosition = start;
        }

        private void EnsureOutline()
        {
            if (_outline != null) return;
            var go = new GameObject("footprint") { layer = RenderLayer };
            go.transform.SetParent(transform, false);
            _outline = go.AddComponent<LineRenderer>();
            _outline.loop = true; _outline.positionCount = 4; _outline.useWorldSpace = true;
            _outline.widthMultiplier = 0.035f; _outline.numCornerVertices = 2;
            _outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            _okMat = new Material(sh) { name = "footprint_ok" }; _okMat.SetColor("_BaseColor", new Color(0.19f, 0.78f, 0.37f));
            _badMat = new Material(sh) { name = "footprint_bad" }; _badMat.SetColor("_BaseColor", new Color(1f, 0.23f, 0.19f));
            _outline.sharedMaterial = _okMat;
        }

        private void DrawOutline(Piece pc)
        {
            if (_outline == null) return;
            var t = pc.go.transform;
            var c = Corners(t.localPosition, t.localEulerAngles.y, pc.f);
            for (int i = 0; i < 4; i++) _outline.SetPosition(i, _room.transform.TransformPoint(new Vector3(c[i].x, 0.025f, c[i].y)));
        }
    }

    /// <summary>Pure footprint checks for place mode (EditMode-tested).</summary>
    public static class PlacementFit
    {
        public const float WallMargin = 0.06f;

        /// <summary>Inside the room (a margin off its walls), clear of the entrance, and not overlapping another footprint.</summary>
        public static bool Fits(Vector2[] corners, Vector3 roomMin, Vector3 roomMax, IEnumerable<Vector2[]> others)
        {
            foreach (var c in corners)
            {
                if (c.x < roomMin.x + WallMargin || c.x > roomMax.x - WallMargin || c.y < roomMin.z + WallMargin || c.y > -WallMargin) return false;
            }
            // the entrance: 1.6 m wide, 1.5 m deep behind the door stays clear
            var door = new[] { new Vector2(-0.8f, -1.5f), new Vector2(0.8f, -1.5f), new Vector2(0.8f, 0f), new Vector2(-0.8f, 0f) };
            if (Overlap(corners, door)) return false;
            foreach (var o in others) if (Overlap(corners, o)) return false;
            return true;
        }

        /// <summary>Separating-axis test for two convex quads (x/z).</summary>
        public static bool Overlap(Vector2[] a, Vector2[] b)
        {
            foreach (var poly in new[] { a, b })
                for (int i = 0; i < poly.Length; i++)
                {
                    var e = poly[(i + 1) % poly.Length] - poly[i];
                    var axis = new Vector2(-e.y, e.x);
                    float aMin = float.MaxValue, aMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
                    foreach (var p in a) { float d = Vector2.Dot(p, axis); aMin = Mathf.Min(aMin, d); aMax = Mathf.Max(aMax, d); }
                    foreach (var p in b) { float d = Vector2.Dot(p, axis); bMin = Mathf.Min(bMin, d); bMax = Mathf.Max(bMax, d); }
                    if (aMax <= bMin + 1e-4f || bMax <= aMin + 1e-4f) return false;
                }
            return true;
        }
    }
}
