using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>
    /// Cached frosted wallpaper under a phone control. This samples the original wallpaper,
    /// never the game framebuffer or other UI. The owning surface supplies tint and clipping.
    /// </summary>
    public sealed class PhoneGlass : VisualElement, IDisposable
    {
        private const int MaximumTextureSide = 512;
        private const float BlurRadius = 12f;
        private readonly VisualElement _surface;
        private readonly List<VisualElement> _geometrySources = new();
        private PhoneWallpaper _wallpaper;
        private Texture2D _source;
        private Material _material;
        private RenderTexture _texture;
        private IVisualElementScheduledItem _refresh;
        private Vector4 _lastUv;
        private Vector2 _lastBlur;
        private Color _lastTint;
        private bool _hasRendered;
        private bool _disposed;

        /// <summary>Inserts the backing before existing children; retain to call Refresh or Dispose.</summary>
        public PhoneGlass(VisualElement surface)
        {
            _surface = surface ?? throw new ArgumentNullException(nameof(surface));
            name = "phone-glass-backing";
            AddToClassList("ct-phone-frost");
            pickingMode = PickingMode.Ignore;
            focusable = false;
            style.position = Position.Absolute;
            style.left = style.top = style.right = style.bottom = 0;
            style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            RegisterCallback<AttachToPanelEvent>(OnAttached);
            RegisterCallback<DetachFromPanelEvent>(OnDetached);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            surface.Insert(0, this);
        }

        private void OnAttached(AttachToPanelEvent evt)
        {
            if (evt.target != this || _disposed) return;
            ObserveGeometry();
            Refresh();
        }

        private void ObserveGeometry()
        {
            StopObservingGeometry();
            for (var ancestor = _surface; ancestor != null; ancestor = ancestor.parent)
            {
                ancestor.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                _geometrySources.Add(ancestor);
                _wallpaper = ancestor.Q<PhoneWallpaper>();
                if (_wallpaper != null) break;
            }
            if (_wallpaper != null)
            {
                _wallpaper.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                _geometrySources.Add(_wallpaper);
            }
        }

        private void StopObservingGeometry()
        {
            foreach (var element in _geometrySources)
                element.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _geometrySources.Clear();
            _wallpaper = null;
        }

        private void OnGeometryChanged(GeometryChangedEvent evt) => Refresh();

        /// <summary>Coalesces layout changes into a single render. Also usable after app resume.</summary>
        public void Refresh()
        {
            if (_disposed || panel == null || _refresh != null) return;
            _refresh = schedule.Execute(() =>
            {
                _refresh = null;
                RenderBacking();
            });
        }

        private void RenderBacking()
        {
            if (_disposed || panel == null) return;
            if (_wallpaper == null) ObserveGeometry();
            if (_wallpaper == null || _wallpaper.resolvedStyle.display == DisplayStyle.None) return;
            var wallpaperRect = _wallpaper.contentRect;
            var patch = _wallpaper.WorldToLocal(worldBound);
            if (!ValidSize(wallpaperRect.size) || !ValidSize(patch.size)) return;

            _source ??= Resources.Load<Texture2D>("UI/PhoneOS/Wallpaper/Dunes");
            if (_source == null) return;
            if (_material == null)
            {
                var shader = Resources.Load<Shader>("UI/PhoneOS/PhoneGlass");
                if (shader == null || !shader.isSupported) return;
                _material = new Material(shader) { name = "CTOS cached glass", hideFlags = HideFlags.HideAndDontSave };
            }

            // Same centered aspect-fill crop as PhoneWallpaper's scale-and-crop style.
            float cover = Mathf.Max(wallpaperRect.width / _source.width, wallpaperRect.height / _source.height);
            float imageWidth = _source.width * cover;
            float imageHeight = _source.height * cover;
            float left = (imageWidth - wallpaperRect.width) * .5f + patch.x - wallpaperRect.x;
            float top = (imageHeight - wallpaperRect.height) * .5f + patch.y - wallpaperRect.y;
            var uv = new Vector4(left / imageWidth, 1f - (top + patch.height) / imageHeight,
                patch.width / imageWidth, patch.height / imageHeight);
            var blur = new Vector2(BlurRadius / (2f * imageWidth), BlurRadius / (2f * imageHeight));
            var tint = _surface.resolvedStyle.backgroundColor;
            float textureScale = Mathf.Min(.5f, MaximumTextureSide / Mathf.Max(patch.width, patch.height));
            int width = Mathf.Clamp(Mathf.CeilToInt(patch.width * textureScale), 1, MaximumTextureSide);
            int height = Mathf.Clamp(Mathf.CeilToInt(patch.height * textureScale), 1, MaximumTextureSide);
            bool resize = _texture == null || _texture.width != width || _texture.height != height;
            if (!resize && _texture.IsCreated() && _hasRendered && uv == _lastUv && blur == _lastBlur && tint == _lastTint) return;
            if (resize)
            {
                ReleaseTexture();
                _texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "CTOS glass backing", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false, autoGenerateMips = false, antiAliasing = 1
                };
            }
            if (!_texture.IsCreated() && !_texture.Create()) return;
            _material.SetVector("_UvRect", uv);
            _material.SetVector("_BlurStep", new Vector4(blur.x, blur.y, 0, 0));
            _material.SetColor("_GlassTint", QualitySettings.activeColorSpace == ColorSpace.Linear ? tint.linear : tint);

            // Unity 6.3 Graphics.Blit changes RenderTexture.active. Preserve caller state.
            // https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Graphics.Blit.html
            var previousTarget = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(_source, _texture, _material, 0);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                GL.sRGBWrite = previousSrgbWrite;
            }
            style.backgroundImage = Background.FromRenderTexture(_texture);
            _lastUv = uv;
            _lastBlur = blur;
            _lastTint = tint;
            _hasRendered = true;
        }

        private static bool ValidSize(Vector2 size) =>
            size.x > 0 && size.y > 0 && !float.IsInfinity(size.x) && !float.IsInfinity(size.y);

        private void OnDetached(DetachFromPanelEvent evt)
        {
            if (evt.target != this) return;
            ReleaseResources();
        }

        private void ReleaseTexture()
        {
            style.backgroundImage = StyleKeyword.None;
            if (_texture != null)
            {
                _texture.Release();
                DestroyResource(_texture);
                _texture = null;
            }
            _hasRendered = false;
        }

        private void ReleaseResources()
        {
            _refresh?.Pause();
            _refresh = null;
            StopObservingGeometry();
            ReleaseTexture();
            if (_material != null) DestroyResource(_material);
            _material = null;
        }

        private static void DestroyResource(UnityEngine.Object resource)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(resource);
            else UnityEngine.Object.DestroyImmediate(resource);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseResources();
            UnregisterCallback<AttachToPanelEvent>(OnAttached);
            UnregisterCallback<DetachFromPanelEvent>(OnDetached);
            UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RemoveFromHierarchy();
        }
    }
}
