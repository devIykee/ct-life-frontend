using System.Linq;
using CTLife.Core;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>
    /// KING OF THE TOWER on the Launchpad Tower's big launch board (brief beat 4, decision D5): while the player is within
    /// <see cref="ActiveDistance"/> of the tower, a small UI Toolkit panel renders into a RenderTexture that replaces the
    /// board's idle art (the venue's `Hook_LaunchBoard`): the trending player coin (`/factory/coins?view=trending&amp;limit=1`,
    /// every 30 s) with its face, ticker, name and market cap, and a flashing NEW LAUNCH slot for 20 s after a player coin
    /// goes live anywhere in the city. Far away it stops and the idle art comes back.
    /// </summary>
    public sealed class LaunchBoardScreen : MonoBehaviour
    {
        public const float ActiveDistance = 60f, RefreshSeconds = 30f, NewSeconds = 20f;
        private const string HookName = "Hook_LaunchBoard";
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        private FactoryService _svc;
        private Renderer _board;
        private Material _boardMat, _idleMat;
        private RenderTexture _rt;
        private UIDocument _doc;
        private VisualElement _root, _king, _newSlot;
        private Label _ticker, _name, _mc, _newText, _empty;
        private VisualElement _art, _newArt;
        private float _nextFind, _nextRefresh, _newUntil, _nextFlash;
        private bool _active, _flashOn;
        private PlayerLaunchSeen _lastNew;

        public static LaunchBoardScreen Create(FactoryService svc)
        {
            var go = new GameObject("LaunchBoardScreen");
            DontDestroyOnLoad(go);
            var s = go.AddComponent<LaunchBoardScreen>();
            s._svc = svc;
            svc.PlayerLaunch += s.OnLaunch;
            return s;
        }

        private void OnLaunch(PlayerLaunchSeen p)
        {
            _lastNew = p;
            _newUntil = Time.unscaledTime + NewSeconds;
            _nextRefresh = 0f;
        }

        private void Update()
        {
            if (_svc == null || _svc.Disposed) return;
            var s = VenueSession.Current;
            var world = s != null && s.IsIsland ? s.Island : null;
            var me = s?.LocalPlayer;
            var desk = world != null ? LaunchpadDesk.DeskPosition(world) : null;
            bool near = desk != null && me != null && InteractionFinder.Flat(me.transform.position, desk.Value) < ActiveDistance;
            if (!near) { if (_active) SetActive(false); return; }
            if (_board == null)
            {
                if (Time.unscaledTime < _nextFind) return;
                _nextFind = Time.unscaledTime + 2f;
                _board = FindBoard(world, desk.Value);
                if (_board == null) return;
                Log.Info("factory", "launch board found: " + _board.name + " size " + _board.bounds.size);
            }
            if (!_active) SetActive(true);
            float t = Time.unscaledTime;
            if (t >= _nextRefresh) { _nextRefresh = t + RefreshSeconds; _ = Refresh(); }
            bool fresh = _lastNew != null && t < _newUntil;
            if (_newSlot != null)
            {
                _newSlot.Show(fresh);
                if (fresh && t >= _nextFlash) { _nextFlash = t + 0.45f; _flashOn = !_flashOn; _newSlot.EnableInClassList("rf-board-new--flash", _flashOn); }
            }
        }

        private static Renderer FindBoard(IslandWorld world, Vector3 near)
        {
            Renderer best = null; float bestD = 40f;
            foreach (var r in world.GetComponentsInChildren<Renderer>(false))
            {
                if (r.name != HookName) continue;
                float d = Vector3.Distance(r.bounds.center, near);
                if (d < bestD) { bestD = d; best = r; }
            }
            return best;
        }

        private void SetActive(bool on)
        {
            _active = on;
            if (on)
            {
                Build();
                if (_board != null && _rt != null)
                {
                    _idleMat = _board.sharedMaterial;
                    _boardMat = new Material(_idleMat);
                    _boardMat.SetTexture(BaseMap, _rt);
                    _boardMat.SetTextureScale(BaseMap, Vector2.one);
                    _boardMat.SetTextureOffset(BaseMap, Vector2.zero);
                    _board.sharedMaterial = _boardMat;
                }
                if (_doc != null) _doc.enabled = true;
                _nextRefresh = 0f;
            }
            else
            {
                if (_board != null && _idleMat != null) _board.sharedMaterial = _idleMat;
                if (_doc != null) _doc.enabled = false;
                if (_boardMat != null) Destroy(_boardMat);
                _boardMat = null;
                _board = null;   // the cell may stream out; find it again next time
            }
        }

        private void Build()
        {
            if (_doc != null) { _doc.enabled = true; Bind(); return; }
            var size = _board != null ? _board.bounds.size : new Vector3(8f, 4f, 0f);
            float w = Mathf.Max(size.x, size.z), h = Mathf.Max(0.5f, size.y);
            int pxW = 1024, pxH = Mathf.Clamp(Mathf.RoundToInt(pxW * h / Mathf.Max(0.5f, w)), 256, 1024);
            _rt = new RenderTexture(pxW, pxH, 0, RenderTextureFormat.ARGB32) { name = "launch_board_rt", useMipMap = false };
            _rt.Create();
            var template = CTLife.UI.UiRoot.Instance != null ? CTLife.UI.UiRoot.Instance.GetComponent<UIDocument>()?.panelSettings : null;
            var ps = template != null ? Instantiate(template) : ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "LaunchBoardPanel";
            ps.targetTexture = _rt;
            ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            ps.scale = 1f;
            ps.clearColor = true;
            ps.colorClearValue = new Color(0.055f, 0.024f, 0.118f, 1f);
            ps.sortingOrder = -100;
            ps.SetScreenToPanelSpaceFunction(_ => new Vector2(float.NaN, float.NaN));   // a wall screen: never takes touches
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = ps;
            Bind();
        }

        private void Bind()
        {
            var root = _doc.rootVisualElement;
            if (root == null) return;
            if (_root != null && _root.parent == root) return;
            root.Clear();
            var style = Resources.Load<StyleSheet>("UI/Factory/factory");
            if (style != null && !root.styleSheets.Contains(style)) root.styleSheets.Add(style);
            _root = CTLife.UI.El.Div("rf-board");
            _root.style.width = _rt.width; _root.style.height = _rt.height;
            _root.Add(Text("rf-board-kicker", FactoryText.T("board.king")));
            _art = new VisualElement(); _art.AddToClassList("rf-board-art");
            _ticker = Text("rf-board-ticker", "");
            _name = Text("rf-board-name", "");
            _mc = Text("rf-board-mc", "");
            _king = CTLife.UI.El.Div("rf-board-king", _art, CTLife.UI.El.Div(null, _ticker, _name, _mc));
            _root.Add(_king);
            _empty = Text("rf-board-empty", FactoryText.T("board.empty"));
            _root.Add(_empty);
            _newArt = new VisualElement(); _newArt.AddToClassList("rf-board-new-art");
            _newText = Text("rf-board-new-text", "");
            _newSlot = CTLife.UI.El.Div("rf-board-new", _newArt, _newText);
            _newSlot.Show(false);
            _root.Add(_newSlot);
            root.Add(_root);
            _king.Show(false);
        }

        private async System.Threading.Tasks.Task Refresh()
        {
            var list = await _svc.Board("trending", 1);
            if (this == null || !_active || _root == null) return;
            var king = list?.FirstOrDefault();
            _king.Show(king != null);
            _empty.Show(king == null);
            if (king != null)
            {
                var tex = FactoryArt.Face(king.TemplateId);
                if (tex != null) _art.style.backgroundImage = Background.FromTexture2D(tex);
                _ticker.text = "$" + king.Ticker;
                _name.text = king.Coin.Name + " · " + FactoryText.F("pulse.by", king.CreatorAlias);
                _mc.text = FactoryText.F("board.mc", MoneyFormat.Cash(king.Coin.McapCents, true));
            }
            if (_lastNew != null)
            {
                var tex = FactoryArt.Face(_lastNew.TemplateId);
                if (tex != null) _newArt.style.backgroundImage = Background.FromTexture2D(tex);
                _newText.text = FactoryText.F("board.new", _lastNew.Ticker, _lastNew.CreatorAlias ?? "anon");
            }
            Log.Info("factory", "launch board: king " + (king?.Ticker ?? "none") + (_lastNew != null && Time.unscaledTime < _newUntil ? " new " + _lastNew.Ticker : ""));
        }

        private static Label Text(string cls, string text)
        {
            var l = El.Text(text);
            l.AddToClassList(cls);
            return l;
        }

        private void OnDestroy()
        {
            if (_svc != null) _svc.PlayerLaunch -= OnLaunch;
            if (_active) SetActive(false);
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
