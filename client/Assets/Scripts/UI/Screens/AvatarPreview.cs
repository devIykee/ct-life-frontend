using System;
using CTLife.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        // Reserved for off-world portraits; no project layer currently uses index 31.
        private const int PreviewLayer = 31;
        private const int PreviewMask = 1 << PreviewLayer;

        private readonly GameObject _root;
        private readonly Camera _cam;
        private AvatarView _avatar;
        private float _yaw = 200f;
        private Vector3 _frameCentre;
        private float _frameDistance;
        private Bounds _bodyBounds, _portraitBounds;
        private bool _portrait;
        private bool _disposed;
        private readonly Light _keyLight;
        private Light _previousSun;
        private bool _lightingOverride;
        private UniversalRenderPipelineAsset _pipeline;
        private float _previousRenderScale;
        private bool _previousAdaptive;
        private readonly bool _transparent;
        private string _idleState;

        public RenderTexture Texture { get; }
        /// <summary>Editor tools (portrait bake) render it on demand.</summary>
        public Camera Camera => _cam;
        public float Yaw => _yaw;
        public bool Portrait => _portrait;

        /// <param name="transparent">Clear to transparent and drop the round blob shadow, so the character stands on whatever
        /// the UI draws behind it (the onboarding draws the painted town, a spotlight and a pill shadow).</param>
        public AvatarPreview(int width = 1080, int height = 1620, bool transparent = false)
        {
            _transparent = transparent;
            var origin = new Vector3(1000f + 50f * _slot++, -500f, 0f);
            _root = new GameObject("CTLife.AvatarPreview");
            _root.transform.position = origin;
            UnityEngine.Object.DontDestroyOnLoad(_root);

            // UI Toolkit samples this texture directly. Keep its attachments and the
            // preview camera single-sampled; world-camera MSAA remains unchanged.
            Texture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32) { name = "avatar_preview", antiAliasing = 1 };
            Texture.Create();
            var camGo = new GameObject("preview_cam");
            camGo.transform.SetParent(_root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.15f, 3.4f);
            camGo.transform.localRotation = Quaternion.Euler(6f, 180f, 0f);
            _cam = camGo.AddComponent<Camera>();
            _cam.allowMSAA = false;
            _cam.allowHDR = false;
            _cam.cullingMask = PreviewMask;
            _cam.targetTexture = Texture;
            _cam.aspect = (float)width / height;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = transparent ? new Color(0f, 0f, 0f, 0f) : (Color)new Color32(232, 230, 225, 255);
            _cam.fieldOfView = 30f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 20f;

            var lightGo = new GameObject("preview_light");
            lightGo.transform.SetParent(_root.transform, false);
            lightGo.transform.localRotation = Quaternion.Euler(30f, 150f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            l.color = new Color(1f, 0.95f, 0.88f);
            l.cullingMask = PreviewMask;
            _keyLight = l;
            // URP selects RenderSettings.sun before choosing other directional lights.
            // The city sun must not replace the portrait's studio key (and vice versa).
            RenderPipelineManager.beginCameraRendering += BeginPreview;
            RenderPipelineManager.endCameraRendering += EndPreview;
        }

        public void SetLook(string[] look)
        {
            if (_avatar != null) UnityEngine.Object.Destroy(_avatar.gameObject);
            _avatar = AvatarFactory.Create("preview", (string[])look.Clone(), false, null, _root.transform, forPreview: true);
            _avatar.transform.localPosition = Vector3.zero;
            _avatar.transform.localRotation = Quaternion.Euler(0, _yaw, 0);
            foreach (var child in _avatar.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = PreviewLayer;
            AvatarView.All.Remove(_avatar); // Not part of the world: no name tag, no LOD.
            if (_transparent)
            {
                var blob = _avatar.transform.Find("blob_shadow");
                if (blob != null) blob.gameObject.SetActive(false);
            }
            if (_avatar.Animator != null)
            {
                _avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _avatar.Animator.Rebind();
                _avatar.Animator.Play("loco_idle", 0, 0);
                _avatar.Animator.Update(0);
            }
            FrameAvatar();
            if (!string.IsNullOrEmpty(_idleState)) _avatar.SetBase(_idleState);
        }

        /// <summary>Strict whole-look replacement for creator transactions; other SetLook callers keep their legacy route.</summary>
        public bool TrySetExactLook(string[] look, out ModularAvatarFailure failure)
        {
            if (_disposed || !CustomizerModel.ResolvesLook(look))
            { failure = new ModularAvatarFailure(ModularAvatarError.InvalidRequest, "No exact whole-look preview is available."); return false; }
            AvatarView candidate = null;
            var childrenBefore = new System.Collections.Generic.HashSet<Transform>();
            foreach (Transform child in _root.transform) childrenBefore.Add(child);
            try { candidate = AvatarFactory.Create("preview", (string[])look.Clone(), false, null, _root.transform, forPreview: true); }
            catch (Exception e)
            {
                // Factory's legacy path is not transactional. Its new children are private to this preview;
                // remove a partially created actor while retaining every pre-existing child.
                foreach (Transform child in _root.transform)
                    if (!childrenBefore.Contains(child)) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
                failure = new ModularAvatarFailure(ModularAvatarError.AssemblyFailed, e.Message); return false;
            }
            return TryCommit(candidate, out failure);
        }

        /// <summary>False keeps the old actor, camera framing and selection available; no fallback renderer is used.</summary>
        public bool TrySetModular(ExactModularCatalog catalog, ModularAvatarRequest request, out ModularAvatarFailure failure)
        {
            if (_disposed) { failure = new ModularAvatarFailure(ModularAvatarError.InvalidRequest, "Preview is disposed."); return false; }
            if (!ExactModularAvatar.TryCreate("preview", catalog, request, _root.transform, true, out var candidate, out failure)) return false;
            return TryCommit(candidate, out failure);
        }

        private bool TryCommit(AvatarView candidate, out ModularAvatarFailure failure)
        {
            var previous = _avatar;
            var body = _bodyBounds; var portrait = _portraitBounds; var centre = _frameCentre; float distance = _frameDistance;
            var cameraPosition = _cam.transform.position; var cameraRotation = _cam.transform.rotation; float far = _cam.farClipPlane;
            try
            {
                if (candidate == null || candidate.Animator == null) throw new InvalidOperationException("Preview candidate has no Animator.");
                candidate.transform.localPosition = Vector3.zero;
                candidate.transform.localRotation = Quaternion.Euler(0, _yaw, 0);
                foreach (var child in candidate.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = PreviewLayer;
                AvatarView.All.Remove(candidate);
                if (_transparent)
                {
                    var blob = candidate.transform.Find("blob_shadow");
                    if (blob != null) blob.gameObject.SetActive(false);
                }
                var animator = candidate.Animator;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                string idle = string.IsNullOrEmpty(_idleState) ? "loco_idle" : _idleState;
                if (!animator.HasState(0, AnimationLibrary.Hash(idle))) throw new InvalidOperationException("Preview idle state is missing.");
                animator.Play(idle, 0, 0); animator.Update(0);
                _avatar = candidate; FrameAvatar();
            }
            catch (Exception e)
            {
                _avatar = previous; _bodyBounds = body; _portraitBounds = portrait; _frameCentre = centre; _frameDistance = distance;
                _cam.transform.SetPositionAndRotation(cameraPosition, cameraRotation); _cam.farClipPlane = far;
                if (candidate != null) { candidate.gameObject.SetActive(false); UnityEngine.Object.Destroy(candidate.gameObject); }
                failure = new ModularAvatarFailure(ModularAvatarError.AssemblyFailed, e.Message); return false;
            }
            if (previous != null) { previous.gameObject.SetActive(false); UnityEngine.Object.Destroy(previous.gameObject); }
            failure = default; return true;
        }

        /// <summary>
        /// The resting pose: "loco_idle" for the full body, "idle_fidget_foldarms" (arms folded) for the face shot, so the
        /// bind-pose-like idle arms never jut into the portrait (device QA 2026-10-08). Emotes return to it when they end.
        /// </summary>
        public void SetIdle(string state)
        {
            _idleState = state;
            if (_avatar != null && !string.IsNullOrEmpty(state) && HasState(state)) _avatar.SetBase(state);
            else if (_avatar != null) _avatar.SetBase("loco_idle");
        }

        /// <summary>
        /// Plays the first of `animIds` the shipped AvatarController has (registry emote ids do not all have states yet),
        /// as a one-shot that returns to the idle. Returns the id played, or null.
        /// </summary>
        public string PlayReaction(params string[] animIds)
        {
            if (_avatar == null || animIds == null) return null;
            foreach (var id in animIds)
            {
                if (string.IsNullOrEmpty(id) || !HasState(id)) continue;
                _avatar.PlayOneShot(id);
                return id;
            }
            return null;
        }

        private bool HasState(string id) =>
            _avatar != null && _avatar.Animator != null && _avatar.Animator.runtimeAnimatorController != null &&
            _avatar.Animator.HasState(0, AnimationLibrary.Hash(id));

        private void BeginPreview(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _cam || _disposed) return;
            if (!Texture.IsCreated()) Texture.Create();
            _previousSun = RenderSettings.sun;
            RenderSettings.sun = _keyLight;
            _pipeline = UniversalRenderPipeline.asset;
            if (_pipeline != null)
            {
                // Verified in installed URP: camera render data reads these after
                // beginCameraRendering. Restore before the next/world camera.
                _previousRenderScale = _pipeline.renderScale;
                _previousAdaptive = _pipeline.useAdaptivePerformance;
                _pipeline.renderScale = 1f;
                _pipeline.useAdaptivePerformance = false;
            }
            _lightingOverride = true;
        }

        private void EndPreview(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _cam || !_lightingOverride) return;
            RenderSettings.sun = _previousSun;
            if (_pipeline != null) { _pipeline.renderScale = _previousRenderScale; _pipeline.useAdaptivePerformance = _previousAdaptive; }
            _lightingOverride = false;
        }

        private void FrameAvatar()
        {
            // Imported skinned-renderer culling bounds can be padded or offset.
            // Measure the current mesh instead, once per outfit change, so hair
            // and shoes are included without pulling the camera far offstage.
            var bounds = new Bounds();
            bool measured = false;
            Mesh baked = null;
            try
            {
            foreach (var renderer in _avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer is SpriteRenderer) continue;
                Bounds part;
                bool localBounds = false;
                if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                {
                    if (baked == null) baked = new Mesh { name = "avatar_preview_measurement" };
                    skinned.BakeMesh(baked, false);
                    if (baked.vertexCount == 0) continue;
                    baked.RecalculateBounds();
                    part = baked.bounds;
                    localBounds = true;
                }
                else if (renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    part = filter.sharedMesh.bounds;
                    localBounds = true;
                }
                else part = renderer.bounds;

                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                        {
                            var corner = part.center + Vector3.Scale(part.extents, new Vector3(x, y, z));
                            var world = localBounds ? renderer.transform.TransformPoint(corner) : corner;
                            var point = _avatar.transform.InverseTransformPoint(world);
                            if (!measured) { bounds = new Bounds(point, Vector3.zero); measured = true; }
                            else bounds.Encapsulate(point);
                        }
            }
            }
            finally { if (baked != null) UnityEngine.Object.Destroy(baked); }
            if (!measured) bounds = new Bounds(new Vector3(0, 0.9f, 0), new Vector3(0.8f, 1.8f, 0.6f));

            _bodyBounds = bounds;
            // Frame the face and shoulders, keeping the measured top of the hair.
            // Humanoid head position anchors the crop; an upper-body crop is the
            // fallback for a non-Humanoid asset, never a claim of editable faces.
            var headCentre = new Vector3(bounds.center.x, bounds.max.y - bounds.size.y * 0.12f, bounds.center.z);
            var animator = _avatar.Animator;
            if (animator != null && animator.avatar != null && animator.isHuman)
            {
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null) headCentre = _avatar.transform.InverseTransformPoint(head.position);
            }
            // Head and shoulders only (chin plus a little chest): the arms stay out of the portrait.
            float lower = Mathf.Clamp(headCentre.y - bounds.size.y * 0.10f,
                bounds.min.y + bounds.size.y * 0.66f, bounds.min.y + bounds.size.y * 0.76f);
            float portraitHeight = Mathf.Max(0.3f, bounds.max.y - lower);
            float portraitWidth = Mathf.Max(portraitHeight * 0.72f, bounds.size.y * 0.18f);
            _portraitBounds = new Bounds(new Vector3(headCentre.x, lower + portraitHeight * 0.5f, headCentre.z),
                new Vector3(portraitWidth, portraitHeight, Mathf.Min(portraitWidth, bounds.size.z)));
            ApplyFraming();
        }

        private void ApplyFraming()
        {
            if (_avatar == null) return;
            var bounds = _portrait ? _portraitBounds : _bodyBounds;

            // A horizontal radius keeps every turntable angle inside the frame.
            // Include the nearest surface's depth plus breathing room above the
            // hair and below the shoes; the camera aims at the measured centre.
            var scale = _avatar.transform.lossyScale;
            var extent = Vector3.Scale(bounds.extents, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            float radius = Mathf.Max(0.1f, new Vector2(extent.x, extent.z).magnitude);
            float tanVertical = Mathf.Tan(_cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
            _frameDistance = Mathf.Max(extent.y / tanVertical, radius / (tanVertical * _cam.aspect)) * 1.06f + radius;
            _frameCentre = bounds.center;
            _cam.farClipPlane = Mathf.Max(20f, _frameDistance + radius + 1f);
            PositionCamera();
        }

        public void SetPortrait(bool portrait)
        {
            _portrait = portrait;
            ApplyFraming();
        }

        public void SetYaw(float degrees)
        {
            _yaw = degrees;
            if (_avatar == null) return;
            _avatar.transform.localRotation = Quaternion.Euler(0, _yaw, 0);
            PositionCamera();
        }

        public void SetVisible(bool visible)
        {
            if (_cam != null) _cam.enabled = visible;
        }

        private void PositionCamera()
        {
            var centre = _avatar.transform.TransformPoint(_frameCentre);
            _cam.transform.position = centre + Vector3.forward * _frameDistance;
            _cam.transform.rotation = Quaternion.LookRotation(centre - _cam.transform.position, Vector3.up);
        }

        public void Spin(float pixels)
        {
            _yaw -= pixels * 0.5f;
            if (_avatar != null)
            {
                _avatar.transform.localRotation = Quaternion.Euler(0, _yaw, 0);
                PositionCamera();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RenderPipelineManager.beginCameraRendering -= BeginPreview;
            RenderPipelineManager.endCameraRendering -= EndPreview;
            if (_lightingOverride) EndPreview(default, _cam);
            if (_cam != null)
            {
                _cam.enabled = false;
                _cam.targetTexture = null;
            }
            if (Texture != null)
            {
                Texture.Release();
                UnityEngine.Object.Destroy(Texture);
            }
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }
    }
}
