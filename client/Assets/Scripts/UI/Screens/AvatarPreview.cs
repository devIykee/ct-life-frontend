using System;
using CTLife.World;
using UnityEngine;

namespace CTLife.UI
{
    /// <summary>
    /// Renders one avatar on a turntable into a RenderTexture for the character creator and profile cards.
    /// Lives 500 m below the world with a 20 m far plane, so it never shows the venue and the venue camera never
    /// sees it.
    /// </summary>
    public sealed class AvatarPreview : IDisposable
    {
        private static int _slot;

        private readonly GameObject _root;
        private readonly Camera _cam;
        private AvatarView _avatar;
        private float _yaw = 200f;

        public RenderTexture Texture { get; }

        public AvatarPreview(int width = 640, int height = 800)
        {
            var origin = new Vector3(1000f + 50f * _slot++, -500f, 0f);
            _root = new GameObject("CTLife.AvatarPreview");
            _root.transform.position = origin;
            UnityEngine.Object.DontDestroyOnLoad(_root);

            Texture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32) { name = "avatar_preview", antiAliasing = 2 };
            var camGo = new GameObject("preview_cam");
            camGo.transform.SetParent(_root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.15f, 3.4f);
            camGo.transform.localRotation = Quaternion.Euler(6f, 180f, 0f);
            _cam = camGo.AddComponent<Camera>();
            _cam.targetTexture = Texture;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0, 0, 0, 0);
            _cam.fieldOfView = 30f;
            _cam.nearClipPlane = 0.5f;
            _cam.farClipPlane = 20f;

            var lightGo = new GameObject("preview_light");
            lightGo.transform.SetParent(_root.transform, false);
            lightGo.transform.localRotation = Quaternion.Euler(30f, 150f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            l.color = new Color(1f, 0.95f, 0.88f);
        }

        public void SetLook(string[] look)
        {
            if (_avatar != null) UnityEngine.Object.Destroy(_avatar.gameObject);
            _avatar = AvatarFactory.Create("preview", (string[])look.Clone(), false, null, _root.transform);
            _avatar.transform.localPosition = Vector3.zero;
            _avatar.transform.localRotation = Quaternion.Euler(0, _yaw, 0);
            _avatar.PlayOneShot("emote_wave");
            AvatarView.All.Remove(_avatar); // Not part of the world: no name tag, no LOD.
        }

        public void Spin(float pixels)
        {
            _yaw -= pixels * 0.5f;
            if (_avatar != null) _avatar.transform.localRotation = Quaternion.Euler(0, _yaw, 0);
        }

        public void Dispose()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (Texture != null) Texture.Release();
        }
    }
}
