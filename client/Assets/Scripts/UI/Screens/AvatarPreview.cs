using System;
using System.Collections.Generic;
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
        private bool _portrait, _identityPortrait;
        private readonly int _textureWidth, _textureHeight;
        private Vector2 _portraitCanvas;
        private Rect _portraitWindow;
        private bool _disposed;
        private readonly Light _keyLight;
        private Light _previousSun;
        private bool _lightingOverride;
        private UniversalRenderPipelineAsset _pipeline;
        private float _previousRenderScale;
        private bool _previousAdaptive;
        private readonly bool _transparent;
        private string _idleState;
        private AvatarPreviewPodium _podium;
        private bool _podiumEnabled;
        private bool _fullBodyReach;
        private bool ReachFraming => _podiumEnabled || _fullBodyReach;
        // Lobby reactions and autonomous idle fidgets. Measure once at selection so the camera stays
        // fixed when a greeting, tap or needs reaction begins; no per-frame mesh baking is performed.
        private static readonly string[] FullBodyStates = { "idle_fidget_stretch", "idle_fidget_foldarms",
            "emote_wave", "emote_thumbs_up", "emote_laugh", "emote_clap", "emote_point",
            "emote_facepalm", "emote_shrug", "react_sad", "emote_head_shake", "emote_moon_jump" };
        private Bounds _creatorBounds;
        private float _creatorRadius;
        private readonly HashSet<string> _measuredStates = new HashSet<string>(StringComparer.Ordinal);

        public RenderTexture Texture { get; }
        /// <summary>Editor tools (portrait bake) render it on demand.</summary>
        public Camera Camera => _cam;
        public float Yaw => _yaw;
        public bool Portrait => _portrait;
        public bool Podium => _podiumEnabled;

        /// <param name="transparent">Clear to transparent and drop the round blob shadow, so the character stands on whatever
        /// the UI draws behind it (the onboarding draws the painted town, a spotlight and a pill shadow).</param>
        public AvatarPreview(int width = 1080, int height = 1620, bool transparent = false)
        {
            _transparent = transparent;
            _textureWidth = width; _textureHeight = height;
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
            if (_avatar != null) { _avatar.gameObject.SetActive(false); UnityEngine.Object.Destroy(_avatar.gameObject); }
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
            if (_disposed || (!CustomizerModel.ResolvesLook(look) && !CharacterPaletteResolver.TryResolve(look, out _)))
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

        /// <summary>Displays the accepted generated body while retaining the prior preview on any validation failure.</summary>
        public bool TrySetLikeness(string jobId, out ModularAvatarFailure failure)
        {
            if (_disposed) { failure = new ModularAvatarFailure(ModularAvatarError.InvalidRequest, "Preview is disposed."); return false; }
            if (!AvatarFactory.TryCreateLikeness("personal_preview", jobId, _root.transform, true, out var candidate, out var error))
            { failure = new ModularAvatarFailure(ModularAvatarError.AssemblyFailed, error); return false; }
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
            var body = _bodyBounds; var portrait = _portraitBounds; var creator = _creatorBounds; float creatorRadius = _creatorRadius;
            var centre = _frameCentre; float distance = _frameDistance;
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
                _avatar = previous; _bodyBounds = body; _portraitBounds = portrait; _creatorBounds = creator;
                _creatorRadius = creatorRadius;
                _frameCentre = centre; _frameDistance = distance;
                _measuredStates.Clear();
                PlacePodium();
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
                // Fit the actual selected rig's reaction before its first rendered frame. Cached per look/state;
                // nothing is baked every frame, and other preview consumers keep their existing camera behavior.
                if (ReachFraming && !_portrait) IncludeAnimationEnvelope(id);
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
            var surface = new System.Collections.Generic.List<Vector3>();
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
                    // Unity 6000.3 useScale=true compensates the renderer Transform scale.
                    // The TransformPoint below applies it once (publisher FBXs may use a 100x mesh transform).
                    skinned.BakeMesh(baked, true);
                    if (baked.vertexCount == 0) continue;
                    baked.RecalculateBounds();
                    part = baked.bounds;
                    localBounds = true;
                    foreach (var vertex in baked.vertices)
                        surface.Add(_avatar.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertex)));
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
            _creatorBounds = bounds;
            _measuredStates.Clear();
            if (ReachFraming) PrepareCreatorEnvelope();
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
            float portraitDepth = Mathf.Min(portraitWidth, bounds.size.z);
            // Measure the authored head silhouette: a human-width crop cuts off a hammerhead's eyes.
            // Lower-body/arm width must not make every human face unnecessarily small.
            bool upperMeasured = false;
            var upper = new Bounds();
            foreach (var point in surface)
            {
                if (point.y < lower) continue;
                if (!upperMeasured) { upper = new Bounds(point, Vector3.zero); upperMeasured = true; }
                else upper.Encapsulate(point);
            }
            if (upperMeasured)
            {
                portraitWidth = Mathf.Max(portraitWidth, upper.size.x);
                portraitDepth = Mathf.Max(portraitDepth, upper.size.z);
                headCentre.x = upper.center.x;
                headCentre.z = upper.center.z;
            }
            _portraitBounds = new Bounds(new Vector3(headCentre.x, lower + portraitHeight * 0.5f, headCentre.z),
                new Vector3(portraitWidth, portraitHeight, portraitDepth));
            PlacePodium();
            ApplyFraming();
        }

        /// <summary>
        /// Creator-only stage. Call before selecting a look. Profiles, Home and portrait bakers remain unchanged.
        /// The UI must give this transparent texture room to extend behind its overlays (a square viewport works well).
        /// </summary>
        public void SetPodium(bool enabled)
        {
            if (_disposed || _podiumEnabled == enabled) return;
            _podiumEnabled = enabled;
            if (enabled && _podium == null) _podium = new AvatarPreviewPodium(_root.transform, PreviewLayer);
            if (_avatar != null)
            {
                _creatorBounds = _bodyBounds;
                _measuredStates.Clear();
                if (ReachFraming) PrepareCreatorEnvelope();
                PlacePodium();
                ApplyFraming();
            }
            else _podium?.SetVisible(enabled && !_portrait);
        }

        /// <summary>
        /// Opt-in full-body reaction framing without adding a podium or changing the front-facing camera.
        /// Set before selecting the lobby's look. Other previews retain their existing framing.
        /// </summary>
        public void SetFullBodyReach(bool enabled)
        {
            if (_disposed || _fullBodyReach == enabled) return;
            _fullBodyReach = enabled;
            if (_avatar == null) return;
            _creatorBounds = _bodyBounds;
            _measuredStates.Clear();
            if (ReachFraming) PrepareCreatorEnvelope();
            ApplyFraming();
        }

        private void PrepareCreatorEnvelope()
        {
            _creatorRadius = new Vector2(_bodyBounds.extents.x, _bodyBounds.extents.z).magnitude;
            // Imported bind-pose culling bounds cannot describe a raised hand. Keep these private renderers
            // updating, then reserve an anatomical reach envelope before the first wave. It also covers idle
            // fidgets triggered internally by AvatarView, which do not pass through PlayReaction.
            foreach (var renderer in _avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                renderer.updateWhenOffscreen = true;
            var animator = _avatar.Animator;
            if (animator == null || !animator.isHuman) return;
            IncludeArmReach(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleDistal);
            IncludeArmReach(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand, HumanBodyBones.RightMiddleDistal);
            if (_fullBodyReach)
                foreach (var state in FullBodyStates)
                    if (HasState(state)) IncludeAnimationEnvelope(state, applyFraming: false);
        }

        private void IncludeArmReach(Animator animator, HumanBodyBones upperId, HumanBodyBones lowerId,
            HumanBodyBones handId, HumanBodyBones fingerId)
        {
            var upper = animator.GetBoneTransform(upperId); var lower = animator.GetBoneTransform(lowerId);
            var hand = animator.GetBoneTransform(handId); var finger = animator.GetBoneTransform(fingerId);
            if (upper == null || lower == null || hand == null) return;
            var origin = _avatar.transform.InverseTransformPoint(upper.position);
            var elbow = _avatar.transform.InverseTransformPoint(lower.position);
            var wrist = _avatar.transform.InverseTransformPoint(hand.position);
            float reach = Vector3.Distance(origin, elbow) + Vector3.Distance(elbow, wrist) + _bodyBounds.size.y * .045f;
            if (finger != null) reach += Vector3.Distance(wrist, _avatar.transform.InverseTransformPoint(finger.position));
            _creatorBounds.Encapsulate(origin + Vector3.up * reach);
            _creatorBounds.Encapsulate(new Vector3(origin.x - reach, origin.y, origin.z));
            _creatorBounds.Encapsulate(new Vector3(origin.x + reach, origin.y, origin.z));
            // The arm's reach and the plinth are separate volumes: combining their AABB corners fabricates
            // a wide diagonal which no surface reaches, unnecessarily making the hero smaller.
            _creatorRadius = Mathf.Max(_creatorRadius,
                new Vector2(origin.x - _bodyBounds.center.x, origin.z - _bodyBounds.center.z).magnitude + reach);
        }

        private void IncludeAnimationEnvelope(string state, bool applyFraming = true)
        {
            if (!_measuredStates.Add(state)) return;
            var animator = _avatar.Animator;
            var current = animator.GetCurrentAnimatorStateInfo(0);
            var renderers = _avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            var baked = new Mesh { name = "creator_reaction_measurement" };
            var vertices = new List<Vector3>();
            var envelope = _bodyBounds;
            try
            {
                // A dozen poses cover the authored onboarding one-shots, with extra padding for the intervals.
                // Actual mesh measurements retain unusual hands, heads, tails and costumes on the selected body.
                for (int sample = 0; sample <= 12; sample++)
                {
                    animator.Play(AnimationLibrary.Hash(state), 0, sample / 12f);
                    animator.Update(0f);
                    foreach (var renderer in renderers)
                    {
                        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sharedMesh == null) continue;
                        renderer.BakeMesh(baked, true);
                        baked.GetVertices(vertices);
                        var toAvatar = _avatar.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                        foreach (var vertex in vertices)
                        {
                            var point = toAvatar.MultiplyPoint3x4(vertex);
                            envelope.Encapsulate(point);
                            _creatorRadius = Mathf.Max(_creatorRadius,
                                new Vector2(point.x - _bodyBounds.center.x, point.z - _bodyBounds.center.z).magnitude + _bodyBounds.size.y * .0175f);
                        }
                    }
                }
                envelope.Expand(_bodyBounds.size.y * .035f);
                // Never zoom back in during an emote; subsequent reactions use the established roomy stage.
                _creatorBounds.Encapsulate(envelope);
            }
            finally
            {
                animator.Play(current.fullPathHash, 0, current.normalizedTime);
                animator.Update(0f);
                UnityEngine.Object.Destroy(baked);
            }
            if (applyFraming) ApplyFraming();
        }

        private void PlacePodium()
        {
            if (_avatar == null) return;
            var blob = _avatar.transform.Find("blob_shadow");
            if (blob != null)
            {
                blob.gameObject.SetActive(_podiumEnabled ? !_portrait : !_transparent);
                if (_podiumEnabled)
                {
                    blob.localPosition = new Vector3(_bodyBounds.center.x, _bodyBounds.min.y + .006f, _bodyBounds.center.z);
                    if (blob.TryGetComponent<SpriteRenderer>(out var shadow)) shadow.color = new Color(1f, 1f, 1f, .6f);
                }
            }
            _avatar.transform.localPosition = _podiumEnabled
                ? Vector3.up * (AvatarPreviewPodium.Height - _bodyBounds.min.y * _avatar.transform.localScale.y)
                : Vector3.zero;
            if (_podium == null) return;
            float height = _bodyBounds.size.y * Mathf.Abs(_avatar.transform.lossyScale.y);
            var centre = _root.transform.InverseTransformPoint(_avatar.transform.TransformPoint(
                new Vector3(_bodyBounds.center.x, _bodyBounds.min.y, _bodyBounds.center.z)));
            centre.y = 0f;
            _podium.Place(centre, Mathf.Clamp(height * .34f, .48f, .8f));
            _podium.SetVisible(_podiumEnabled && !_portrait);
        }

        private void ApplyFraming()
        {
            if (_avatar == null) return;
            var bounds = _portrait ? _portraitBounds : ReachFraming ? _creatorBounds : _bodyBounds;

            if (_portrait && _identityPortrait)
            {
                // A collectible shows the chosen outfit as well as the face. Keep the native portrait's
                // head centre, but include the body down to the thighs instead of using an ID-photo crop.
                float lower = _bodyBounds.min.y + _bodyBounds.size.y * .27f;
                float width = Mathf.Max(_portraitBounds.size.x, _bodyBounds.size.y * .42f);
                bounds = new Bounds(new Vector3(_portraitBounds.center.x, (lower + _bodyBounds.max.y) * .5f, _portraitBounds.center.z),
                    new Vector3(width, _bodyBounds.max.y - lower, _portraitBounds.size.z));
            }
            if (_podiumEnabled && !_portrait)
            {
                // Include the plinth, with the actor's soles placed at its top. Keep a small camera elevation so
                // this reads as a physical stage, not another flat UI ellipse.
                var bottom = _avatar.transform.InverseTransformPoint(_podium.Transform.position);
                float podiumRadius = _podium.Radius / Mathf.Abs(_avatar.transform.lossyScale.x);
                bounds.Encapsulate(bottom + new Vector3(podiumRadius, 0f, podiumRadius));
                bounds.Encapsulate(bottom - new Vector3(podiumRadius, 0f, podiumRadius));
            }
            if (ReachFraming && !_portrait)
            {
                var centre = bounds.center;
                centre.x = _bodyBounds.center.x; centre.z = _bodyBounds.center.z;
                bounds.center = centre;
            }

            // A horizontal radius keeps every turntable angle inside the frame.
            // Include the nearest surface's depth plus breathing room above the
            // hair and below the shoes; the camera aims at the measured centre.
            var scale = _avatar.transform.lossyScale;
            var extent = Vector3.Scale(bounds.extents, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            float radius = Mathf.Max(0.1f, new Vector2(extent.x, extent.z).magnitude);
            if (ReachFraming && !_portrait)
                radius = Mathf.Max(_podiumEnabled ? _podium.Radius : 0f, _creatorRadius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
            float tanVertical = Mathf.Tan(_cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
            float framingAspect = (float)_textureWidth / _textureHeight;
            if (ReachFraming && !_portrait)
            {
                // Fit the cylinder's actual projected silhouette, not a depth-padded box. This keeps the actor
                // substantially larger while retaining all turntable angles and the elevated stage camera.
                var backward = _podiumEnabled ? CreatorCameraBackward : Vector3.forward;
                var up = new Vector3(0f, backward.z, -backward.y);
                float required = 0f;
                for (int i = 0; i < 32; i++)
                {
                    float angle = i * Mathf.PI * 2f / 32f;
                    for (int y = -1; y <= 1; y += 2)
                    {
                        var point = new Vector3(Mathf.Cos(angle) * radius, extent.y * y, Mathf.Sin(angle) * radius);
                        float depth = Vector3.Dot(point, backward);
                        required = Mathf.Max(required, Mathf.Abs(Vector3.Dot(point, up)) / tanVertical + depth,
                            Mathf.Abs(point.x) / (tanVertical * framingAspect) + depth);
                    }
                }
                _frameDistance = required * 1.035f;
            }
            else _frameDistance = Mathf.Max(extent.y / tanVertical, radius / (tanVertical * framingAspect)) * 1.06f + radius;
            _frameCentre = bounds.center;
            _cam.farClipPlane = Mathf.Max(20f, _frameDistance + radius + 1f);
            PositionCamera();
        }

        /// <summary>
        /// Composes a portrait into a larger transparent canvas without changing its apparent scale.
        /// window is the original portrait rectangle in canvas coordinates (top-left origin). Arms can
        /// extend outside that rectangle; only the actual screen clips them. Zero size restores normal framing.
        /// The same RenderTexture is resized, so UI consumers keep a valid reference and no second camera is needed.
        /// </summary>
        public void SetPortraitCanvas(Vector2 size, Rect window)
        {
            bool enabled = size.x > 0f && size.y > 0f;
            _portraitCanvas = enabled ? size : Vector2.zero;
            _portraitWindow = window;
            int width = enabled ? 1536 : _textureWidth;
            int height = enabled ? Mathf.Clamp(Mathf.RoundToInt(width * size.y / size.x), 256, 1536) : _textureHeight;
            if (Texture.width != width || Texture.height != height)
            {
                Texture.Release(); Texture.width = width; Texture.height = height; Texture.Create();
            }
            ApplyFraming();
        }

        public void SetIdentityPortrait(bool enabled)
        {
            if (_identityPortrait == enabled) return;
            _identityPortrait = enabled;
            ApplyFraming();
        }

        public void SetPortrait(bool portrait)
        {
            _portrait = portrait;
            PlacePodium();
            ApplyFraming();
        }

        public void SetYaw(float degrees)
        {
            _yaw = degrees;
            if (_avatar == null) return;
            _avatar.transform.localRotation = Quaternion.Euler(0, _yaw, 0);
            PlacePodium();
            PositionCamera();
        }

        public void SetVisible(bool visible)
        {
            if (_cam != null) _cam.enabled = visible;
        }

        private void PositionCamera()
        {
            var centre = _avatar.transform.TransformPoint(_frameCentre);
            _cam.transform.position = centre + (_podiumEnabled && !_portrait ? CreatorCameraBackward : Vector3.forward) * _frameDistance;
            _cam.transform.rotation = Quaternion.LookRotation(centre - _cam.transform.position, Vector3.up);
            _cam.ResetProjectionMatrix();
            _cam.aspect = (float)_textureWidth / _textureHeight;
            if (_portrait && _portraitCanvas.x > 0f && _portraitCanvas.y > 0f)
            {
                var projection = Matrix4x4.Perspective(_cam.fieldOfView, _cam.aspect, _cam.nearClipPlane, _cam.farClipPlane);
                projection.m00 *= _portraitWindow.width / _portraitCanvas.x;
                projection.m11 *= _portraitWindow.height / _portraitCanvas.y;
                projection.m02 = 1f - 2f * _portraitWindow.center.x / _portraitCanvas.x;
                projection.m12 = 2f * _portraitWindow.center.y / _portraitCanvas.y - 1f;
                _cam.projectionMatrix = projection;
            }
        }

        public void Spin(float pixels)
        {
            _yaw -= pixels * 0.5f;
            if (_avatar != null)
            {
                _avatar.transform.localRotation = Quaternion.Euler(0, _yaw, 0);
                PlacePodium();
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
            _podium?.Dispose();
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }

        private static readonly Vector3 CreatorCameraBackward = new Vector3(0f, .1391731f, .9902681f); // 8 degrees down
    }
}
