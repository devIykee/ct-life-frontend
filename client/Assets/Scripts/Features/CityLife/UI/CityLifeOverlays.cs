using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.CityLife
{
    /// <summary>
    /// Entry points other assemblies can call without referencing City Life types (wired by the integration lead; see
    /// docs/progress/requests.md "2026-10-08 city-life -> integration lead").
    /// </summary>
    public static class CityLifeLinks
    {
        /// <summary>Knock on a player's door (ProfileCard "Visit home"). No-op before the hub exists.</summary>
        public static void VisitHome(string userId, string displayName)
        {
            var hub = CityLifeHub.Current;
            if (hub == null || !CityWire.IsSafeId(userId)) return;
            _ = hub.RequestVisit(userId, displayName);
        }
    }

    /// <summary>
    /// Always-on City Life presentation while signed in: needs warnings and rent toasts (via the hub loop), the furniture
    /// sheet for home scene taps, and the NFT frames on the home's walls. Created by FlowController next to the other
    /// overlays (hook request item 3); disposing it releases the hub.
    /// </summary>
    public sealed class CityLifeOverlays : IDisposable
    {
        private readonly VisualElement _layer;
        private readonly Session _session;
        private readonly Subscriptions _subs = new Subscriptions();
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private CityLifeHub _hub;
        private NeedsActionRunner _runner;
        private NftWallPresenter _wall;
        private VisualElement _open;
        private bool _disposed;

        /// <summary>Safe to create before sign-in (with the other FlowController overlays): it binds to the signed-in user and rebinds on sign-out/sign-in.</summary>
        public CityLifeOverlays(VisualElement modalLayer)
        {
            _layer = modalLayer;
            _session = Services.Get<Session>();
            _session.Changed += Bind;
            WorldInput.SpotTapped += OnSpotTapped;
            _subs.On<VenueChangedSignal>(_ => { Close(); _wall?.Sync(); });
            _subs.On<GalleryChangedSignal>(s => _wall?.Reload(s.ownerId));
            _subs.On<ProfileChangedSignal>(_ => Bind());
            Bind();
        }

        private void Bind()
        {
            if (_disposed) return;
            var user = _session.IsSignedIn ? _session.UserId : null;
            if (_hub != null && !_hub.Disposed && _hub.UserId == user) return;
            Unbind();
            if (string.IsNullOrEmpty(user)) return;
            _hub = CityLifeHub.Acquire();
            _runner = new NeedsActionRunner(_hub);
            _wall = new NftWallPresenter(_hub);
            _wall.Sync();
        }

        private void Unbind()
        {
            Close();
            _wall?.Dispose();
            _wall = null;
            _runner = null;
            _hub?.Release();
            _hub = null;
        }

        private static bool InOwnHome => VenueSession.Current != null && VenueSession.Current.IsActive && VenueSession.Current.IsOwnHome;
        private static bool VisitingHome => VenueSession.Current != null && VenueSession.Current.IsActive && VenueSession.Current.VenueId == "home" && !VenueSession.Current.IsOwnHome;

        private void OnSpotTapped(SpotAnchor spot)
        {
            if (_disposed || spot == null || _hub == null) return;
            if (!InOwnHome && !VisitingHome) return;
            var kind = HomeFurniture.Kind(spot.spotId);
            var actions = HomeFurniture.ActionsFor(spot.spotId);
            if (actions.Length == 0) return;
            ShowSheet(kind, actions, ownerOnly: !InOwnHome);
        }

        private void ShowSheet(string kind, string[] actions, bool ownerOnly)
        {
            Close();
            var hub = _hub;
            var runner = _runner;
            var sheet = El.VerticalScroll("ct-sheet");
            sheet.Insert(0, El.Div("ct-sheet-handle"));
            sheet.Add(CityUi.Legacy.H1(CityLifeStrings.Has("furniture." + kind) ? CityLifeStrings.T("furniture." + kind) : CityLifeStrings.Readable(kind)));
            var status = CityUi.Legacy.Small("");
            if (ownerOnly) sheet.Add(CityUi.Legacy.Text(CityLifeStrings.T("furniture.visitorOnly")));
            else
            {
                var cfg = hub.Needs.Config;
                var projected = hub.Needs.Projected(hub.NowMs);
                foreach (var a in actions)
                {
                    var action = a;
                    var def = cfg?.Action(a);
                    if (def == null) continue;
                    int idx = Array.IndexOf(NeedsConfig.Ids, def.Raises);
                    if (idx >= 0 && projected != null)
                        sheet.Add(CityUi.Legacy.Small(CityLifeStrings.NeedName(def.Raises) + ": " + Math.Floor(projected[idx]).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    var gate = runner.GateText(action);
                    var b = CityUi.Legacy.Btn(CityLifeStrings.FurnitureLabel(kind, action), async () =>
                    {
                        if (_disposed || runner.Busy || !ReferenceEquals(runner, _runner)) return;
                        status.text = CityLifeStrings.T("needs.working");
                        var msg = await runner.Run(action, _life.Token);
                        if (!_disposed && msg != null) status.text = msg;
                    });
                    b.SetEnabled(gate == null);
                    sheet.Add(b);
                    if (gate != null) sheet.Add(CityUi.Legacy.Small(gate));
                }
            }
            sheet.Add(status);
            sheet.Add(CityUi.Legacy.Btn(CityLifeStrings.T("common.close"), Close, "ct-btn"));
            var backdrop = El.Div("ct-modal-backdrop ct-modal-bottom", sheet);
            backdrop.pickingMode = PickingMode.Position;
            backdrop.RegisterCallback<PointerDownEvent>(e => { if (e.target == backdrop) Close(); });
            _open = backdrop;
            _layer.Add(backdrop);
        }

        private void Close()
        {
            var o = _open;
            _open = null;
            o?.RemoveFromHierarchy();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Changed -= Bind;
            WorldInput.SpotTapped -= OnSpotTapped;
            _life.Cancel();
            _subs.Dispose();
            Unbind();
        }
    }

    /// <summary>
    /// Hangs NFT frames on the wall spots of the home being shown (own home, or a visited home once the World visit
    /// presenter exists). Wall spots are scene anchors or registry spots named `wall_N` / `frame_N` (proposed in
    /// docs/fe-contract-proposals/ids/home-spots-city-life.md). Frames sit 1.6 m up the wall (FE presentation constant).
    /// </summary>
    internal sealed class NftWallPresenter : IDisposable
    {
        public const float FrameHeight = 1.6f, FrameWidth = 0.8f, WallOffset = 0.05f;
        private readonly CityLifeHub _hub;
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<Material> _materials = new List<Material>();
        private CancellationTokenSource _load;
        private VenueSession _shownIn;
        private string _ownerId;

        public NftWallPresenter(CityLifeHub hub) { _hub = hub; }

        public void Sync()
        {
            var v = VenueSession.Current;
            string owner = null;
            if (v != null && v.Venue != null && v.VenueId == "home")
                owner = v.IsOwnHome ? _hub.UserId : (_hub.Visit.IsInside ? _hub.Visit.OwnerId : null);
            if (ReferenceEquals(v, _shownIn) && owner == _ownerId && owner != null) return;
            Clear();
            _shownIn = v;
            _ownerId = owner;
            if (owner != null && Phases.IsOpen(4)) _ = Load(v, owner);
        }

        public void Reload(string ownerId)
        {
            if (ownerId == null || ownerId != _ownerId) return;
            var v = _shownIn;
            Clear();
            _shownIn = v;
            _ownerId = ownerId;
            _ = Load(v, ownerId);
        }

        private List<(int index, Vector3 pos, float rotY)> WallSpots(VenueSceneRoot root)
        {
            var list = new List<(int, Vector3, float)>();
            var seen = new HashSet<int>();
            foreach (var a in root.Anchors)
            {
                int i = HomeFurniture.FrameIndex(a.spotId);
                if (i > 0 && seen.Add(i)) list.Add((i, a.transform.position, a.RotY));
            }
            if (root.RegistrySpots != null)
                foreach (var kv in root.RegistrySpots.spots)
                {
                    int i = HomeFurniture.FrameIndex(kv.Key);
                    if (i > 0 && seen.Add(i) && root.TryGetSpot(kv.Key, out var p, out var r)) list.Add((i, p, r));
                }
            list.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            return list;
        }

        private async Task Load(VenueSession venue, string ownerId)
        {
            _load?.Cancel();
            var cts = _load = CancellationTokenSource.CreateLinkedTokenSource(_hub.Life);
            try
            {
                var root = venue.Venue;
                var spots = WallSpots(root);
                if (spots.Count == 0) return;
                var (frames, items) = await _hub.Nfts.Gallery(ownerId, cts.Token);
                if (cts.IsCancellationRequested || !ReferenceEquals(VenueSession.Current, venue) || venue.Venue == null) return;
                var me = _hub.Profile?.Me;
                var home = ownerId == _hub.UserId ? _hub.Homes.Find(h => h.Id == me?.HomeId) : null;
                int slots = HomeCatalog.FrameSlots(home, spots.Count, home?.FrameSlots ?? -1);
                if (home == null) slots = spots.Count;
                bool owner = ownerId == _hub.UserId;
                var assigned = GalleryLayout.Assign(frames, items, slots, owner);
                for (int i = 0; i < assigned.Length && i < spots.Count; i++)
                {
                    var item = assigned[i];
                    if (item == null) continue;
                    var go = Frame(spots[i].pos, spots[i].rotY, root.transform);
                    if (NftItem.VisibilityFor(item, owner) != FrameVisibility.Show) continue;
                    var tex = await _hub.Images.Get(string.IsNullOrEmpty(item.ImageUrl) ? item.ThumbUrl : item.ImageUrl, cts.Token);
                    if (cts.IsCancellationRequested || go == null) return;
                    if (tex == null) continue;
                    var art = go.transform.Find("art");
                    var mat = Mats.Textured(tex);
                    _materials.Add(mat);
                    art.GetComponent<Renderer>().sharedMaterial = mat;
                    float aspect = tex.height > 0 ? (float)tex.width / tex.height : 1f;
                    art.localScale = aspect >= 1 ? new Vector3(FrameWidth, FrameWidth / aspect, 0.005f) : new Vector3(FrameWidth * aspect, FrameWidth, 0.005f);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                var err = CityErrors.From(e, false);
                if (!err.Is("UNAVAILABLE")) Log.Warn("nft", "Gallery wall unavailable: " + err.Code);
            }
        }

        private GameObject Frame(Vector3 spot, float rotY, Transform parent)
        {
            var go = new GameObject("nft_frame");
            go.transform.SetParent(parent, true);
            var rot = Quaternion.Euler(0, rotY * Mathf.Rad2Deg, 0);
            go.transform.SetPositionAndRotation(spot + Vector3.up * FrameHeight + rot * Vector3.forward * WallOffset, rot);
            var border = GameObject.CreatePrimitive(PrimitiveType.Cube);
            border.name = "border";
            UnityEngine.Object.Destroy(border.GetComponent<Collider>());
            border.transform.SetParent(go.transform, false);
            border.transform.localScale = new Vector3(FrameWidth + 0.08f, FrameWidth + 0.08f, 0.03f);
            border.GetComponent<Renderer>().sharedMaterial = Mats.Color(new Color(0.12f, 0.1f, 0.08f));
            // A thin cube rather than a quad: every cube face carries the full texture upright, so the art reads from
            // the room whichever way the authored spot faces (spot rotY is defined as "facing into the room").
            var art = GameObject.CreatePrimitive(PrimitiveType.Cube);
            art.name = "art";
            UnityEngine.Object.Destroy(art.GetComponent<Collider>());
            art.transform.SetParent(go.transform, false);
            art.transform.localPosition = new Vector3(0, 0, 0.02f);
            art.transform.localScale = new Vector3(FrameWidth, FrameWidth, 0.005f);
            art.GetComponent<Renderer>().sharedMaterial = Mats.Color(new Color(0.75f, 0.75f, 0.78f));
            _objects.Add(go);
            return go;
        }

        private void Clear()
        {
            _load?.Cancel();
            foreach (var go in _objects) if (go != null) UnityEngine.Object.Destroy(go);
            _objects.Clear();
            foreach (var m in _materials) if (m != null) UnityEngine.Object.Destroy(m);
            _materials.Clear();
            _shownIn = null;
            _ownerId = null;
        }

        public void Dispose() => Clear();
    }
}
