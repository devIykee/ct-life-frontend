using System;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using CTLife.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Economy
{
    /// <summary>
    /// The 3D half of the launch (brief beats 2-3): the camera eases (0.6 s) to a fixed three-quarter shot of the launch desk
    /// with the desk on the left of the frame (the sheet covers the right), the city HUD hides, and the player's coin (Kenney's
    /// CC0 gold coin with the chosen Twemoji face on both sides) floats and turns over the desk. <see cref="PlayLaunch"/> drops
    /// it onto the desk, stamps it (scale 1 → 0.9 → 1.15, a recorded coin-landing hit) and shoots it up with a trail;
    /// <see cref="PlayRefused"/> drops it back. <see cref="Close"/> gives the camera and HUD back.
    /// </summary>
    public sealed class FactoryDeskStage : MonoBehaviour
    {
        private const string PrefabPath = "World/Factory/FactoryCoin";
        public const float FloatHeight = 1.55f, DeskTop = 0.86f, EaseSeconds = 0.6f;
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        public static FactoryDeskStage Current { get; private set; }

        private Vector3 _desk, _towardRoom;
        private Camera _cam;
        private CameraRig _rig;
        private bool _camTaken;
        private StyleEnum<DisplayStyle> _hudDisplay;
        private Vector3 _fromPos, _shotPos;
        private Quaternion _fromRot, _shotRot;
        private float _ease;
        private Transform _coin;
        private Renderer[] _faces = Array.Empty<Renderer>();
        private TrailRenderer _trail;
        private TextMesh _ticker;
        private bool _animating, _closing;
        private float _spin;

        public static FactoryDeskStage Open(Vector3 desk, Vector3 towardRoom)
        {
            if (Current != null) Current.CloseNow();
            var go = new GameObject("FactoryDeskStage");
            var s = go.AddComponent<FactoryDeskStage>();
            s._desk = desk;
            towardRoom.y = 0f;
            s._towardRoom = towardRoom.sqrMagnitude > 1e-4f ? towardRoom.normalized : Vector3.back;
            Current = s;
            s.TakeCamera();
            s.SpawnCoin();
            return s;
        }

        /// <summary>The camera shot (pure, tested): a three-quarter view from the room side, aimed right of the desk so the desk
        /// sits in the left part of the frame.</summary>
        public static (Vector3 pos, Quaternion rot) Shot(Vector3 desk, Vector3 towardRoom)
        {
            towardRoom.y = 0f; towardRoom.Normalize();
            var side = Vector3.Cross(Vector3.up, towardRoom).normalized;   // the desk's right, seen from the room
            var pos = desk + towardRoom * 3.1f + side * 1.9f + Vector3.up * 2.05f;
            var forward = (desk - pos); forward.y = 0f; forward.Normalize();
            var camRight = Vector3.Cross(Vector3.up, forward).normalized;
            var aim = desk + Vector3.up * 1.15f + camRight * 1.05f;
            return (pos, Quaternion.LookRotation(aim - pos, Vector3.up));
        }

        private void TakeCamera()
        {
            _rig = CameraRig.Instance;
            _cam = _rig != null && _rig.Camera != null ? _rig.Camera : Camera.main;
            if (_cam == null) return;
            if (_rig != null) _rig.enabled = false;
            _camTaken = true;
            _fromPos = _cam.transform.position; _fromRot = _cam.transform.rotation;
            (_shotPos, _shotRot) = Shot(_desk, _towardRoom);
            _ease = 0f;
            var hud = UiRoot.Instance != null ? UiRoot.Instance.HudLayer : null;
            if (hud != null) { _hudDisplay = hud.style.display; hud.style.display = DisplayStyle.None; }
        }

        private void SpawnCoin()
        {
            var prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab == null) { Log.Warn("factory", "no coin prefab at Resources/" + PrefabPath); return; }
            var go = Instantiate(prefab, transform);
            _coin = go.transform;
            _coin.position = _desk + Vector3.up * FloatHeight;
            _coin.rotation = Quaternion.LookRotation(-_towardRoom, Vector3.up);
            var faces = new System.Collections.Generic.List<Renderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>()) if (r.name.StartsWith("Face")) faces.Add(r);
            _faces = faces.ToArray();
            _trail = go.GetComponent<TrailRenderer>();
            var label = new GameObject("Ticker");
            label.transform.SetParent(transform, false);
            _ticker = label.AddComponent<TextMesh>();
            var font = Resources.Load<Font>("UI/Fonts/LilitaOne-Regular");
            if (font != null) { _ticker.font = font; label.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
            _ticker.richText = false;   // the ticker is player text
            _ticker.anchor = TextAnchor.MiddleCenter;
            _ticker.alignment = TextAlignment.Center;
            _ticker.characterSize = 0.022f;
            _ticker.fontSize = 64;
            _ticker.color = new Color(1f, 0.82f, 0.25f);
            label.transform.position = _desk + Vector3.up * (FloatHeight - 0.34f);
        }

        public void SetArt(string templateId)
        {
            var tex = FactoryArt.Face(templateId);
            if (tex == null) return;
            foreach (var r in _faces) if (r != null) r.material.SetTexture(BaseMap, tex);
        }

        public void SetTicker(string ticker)
        {
            if (_ticker != null) _ticker.text = string.IsNullOrEmpty(ticker) ? "" : "$" + ticker;
        }

        private void LateUpdate()
        {
            if (_camTaken && _cam != null && !_closing)
            {
                _ease = Mathf.Min(1f, _ease + Time.unscaledDeltaTime / EaseSeconds);
                float k = Mathf.SmoothStep(0f, 1f, _ease);
                _cam.transform.SetPositionAndRotation(Vector3.Lerp(_fromPos, _shotPos, k), Quaternion.Slerp(_fromRot, _shotRot, k));
            }
            if (_coin != null && !_animating)
            {
                _spin += Time.deltaTime * 55f;
                float bob = Mathf.Sin(Time.time * 1.6f) * 0.03f;
                _coin.position = _desk + Vector3.up * (FloatHeight + bob);
                _coin.rotation = Quaternion.LookRotation(-_towardRoom, Vector3.up) * Quaternion.Euler(0f, Mathf.Sin(_spin * Mathf.Deg2Rad) * 38f, 0f);
                if (_ticker != null && _cam != null) _ticker.transform.rotation = Quaternion.LookRotation(_ticker.transform.position - _cam.transform.position, Vector3.up);
            }
        }

        /// <summary>Drop, stamp, shoot up (about 1.6 s). Plays while the launch request runs.</summary>
        public async Task PlayLaunch()
        {
            if (_coin == null) return;
            _animating = true;
            var face = Quaternion.LookRotation(-_towardRoom, Vector3.up);
            Vector3 top = _desk + Vector3.up * (DeskTop + 0.21f);
            Vector3 start = _coin.position;
            await Tween(0.28f, t => { _coin.position = Vector3.Lerp(start, top, t * t); _coin.rotation = face; });
            Bus.Publish(new SfxSignal("sfx_coin_land"));
            _rig?.Shake(0.25f);
            await Tween(0.10f, t => _coin.localScale = Vector3.one * Mathf.Lerp(1f, 0.9f, t));
            await Tween(0.14f, t => _coin.localScale = Vector3.one * Mathf.Lerp(0.9f, 1.15f, t));
            await Tween(0.10f, t => _coin.localScale = Vector3.one * Mathf.Lerp(1.15f, 1f, t));
            if (_ticker != null) _ticker.gameObject.SetActive(false);
            if (_trail != null) { _trail.Clear(); _trail.emitting = true; }
            Bus.Publish(new SfxSignal("sfx_new_coin"));
            Vector3 up0 = _coin.position;
            await Tween(0.75f, t => { _coin.position = up0 + Vector3.up * (t * t * 14f); _coin.rotation = face * Quaternion.Euler(0f, t * 720f, 0f); });
            if (_coin != null) _coin.gameObject.SetActive(false);
        }

        /// <summary>The server refused: the coin falls back onto the desk, then floats again for the fix.</summary>
        public async Task PlayRefused()
        {
            if (_coin == null) return;
            _animating = true;
            _coin.gameObject.SetActive(true);
            _coin.localScale = Vector3.one;
            if (_trail != null) { _trail.emitting = false; _trail.Clear(); }
            Vector3 high = _desk + Vector3.up * 3.2f, top = _desk + Vector3.up * (DeskTop + 0.21f);
            await Tween(0.35f, t => _coin.position = Vector3.Lerp(high, top, t * t));
            Bus.Publish(new SfxSignal("sfx_coin_land"));
            await Tween(0.35f, t => _coin.position = Vector3.Lerp(top, _desk + Vector3.up * FloatHeight, Mathf.SmoothStep(0f, 1f, t)));
            if (_ticker != null) _ticker.gameObject.SetActive(true);
            _animating = false;
        }

        private async Task Tween(float seconds, Action<float> step)
        {
            float t0 = Time.time;
            while (this != null && _coin != null)
            {
                float t = Mathf.Clamp01((Time.time - t0) / seconds);
                step(t);
                if (t >= 1f) break;
                await Awaitable.NextFrameAsync();
            }
        }

        /// <summary>Gives the camera back (it settles behind the player) and the HUD.</summary>
        public void Close() => CloseNow();

        private void CloseNow()
        {
            if (_closing) return;
            _closing = true;
            if (_camTaken)
            {
                _camTaken = false;
                var hud = UiRoot.Instance != null ? UiRoot.Instance.HudLayer : null;
                if (hud != null) hud.style.display = _hudDisplay;
                if (_rig != null)
                {
                    _rig.enabled = true;
                    var me = VenueSession.Current?.LocalPlayer;
                    if (me != null) _rig.FollowAvatar(me.transform);
                    _rig.SettleBehind();
                }
            }
            if (ReferenceEquals(Current, this)) Current = null;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (!_closing) CloseNow();
        }
    }
}
