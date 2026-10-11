#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
// Development diagnostic only. Never run during performance captures.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CTLife.World;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CTLife.App.Review
{
    /// <summary>Observe the real continuously evaluated Animator after EndOfFrame; never evaluate or pose it.</summary>
    internal sealed class DeviceGaitSampler : IDisposable
    {
        [Serializable] public struct Ground
        {
            public bool found;
            public Vector3 position, normal;
        }
        [Serializable] public struct Foot
        {
            public Vector3 ankleWorld, toeWorld, ankleRoot, toeRoot;
            public Quaternion ankleRotationWorld, toeRotationWorld;
            public float soleMinWorldY, soleMinRootY, soleGapToGround;
            public int lowestVertex;
            public Vector3 lowestPointWorld, fixedFootSoleWorld, fixedToeSoleWorld;
            public Ground ground;
        }
        [Serializable] public struct Sample
        {
            public int frame, stateHash, nextStateHash, lodTier;
            public double realtime, elapsed, sampleCostMilliseconds;
            public float unityDeltaTime, normalizedTime, nextNormalizedTime, transitionNormalizedTime;
            public float stateSpeed, stateSpeedMultiplier, animatorSpeed, speedParameter;
            public float actualSpeed, observedRootSpeed, clipLength, clipWeight, agentBaseOffset;
            public bool inTransition, bodyEnabled, bodyVisible, animatorEnabled, geometryMeasured;
            public int selectedRendererIndex;
            public string geometryStatus;
            public RendererObservation[] rendererObservations;
            public float headHipLateralDegrees, headHipForwardDegrees;
            public Vector3 headRoot, hipsRoot;
            public string state, clip;
            public Vector3 rootWorld, rootForward, actualVelocity, agentVelocity, navPosition;
            public Quaternion rootRotation;
            public Ground rootGround;
            public Foot left, right;
        }
        [Serializable] public sealed class Capture
        {
            public string label, avatar, controller, renderer, mesh, unityVersion, coordinates, limitations;
            public int schemaVersion = 2;
            public int requestedHz, maximumSamples, samplesWritten, vertexCount, groundMask;
            public int measuredSamples, missingVisibleSamples, ambiguousVisibleSamples, invalidVisibleSamples;
            public RendererInfo[] renderers;
            public int leftVertexCount, rightVertexCount, leftFixedFootIndex, leftFixedToeIndex;
            public int rightFixedFootIndex, rightFixedToeIndex;
            public bool reachedCapacity;
            public double startedRealtime;
            public Vector3 rootScale, rendererScale;
            public Sample[] samples;
        }

        [Serializable] public sealed class RendererInfo
        {
            public int index, instanceId, vertexCount;
            public string path, mesh, unavailableReason;
            public string[] lodMembership;
            public int leftVertexCount, rightVertexCount, leftFixedFootIndex, leftFixedToeIndex, rightFixedFootIndex, rightFixedToeIndex;
        }
        [Serializable] public struct RendererObservation
        {
            public int rendererIndex;
            public bool enabled, active, visible, meshMatchesCache, eligible;
        }
        private sealed class SkinCache
        {
            public SkinnedMeshRenderer skin;
            public Mesh source;
            public Selection left, right;
            public RendererInfo info;
        }
        private sealed class Selection
        {
            public int[] vertices;
            public int fixedFootIndex = -1, fixedToeIndex = -1;
            public Transform foot, toe;
        }
        private readonly LocalPlayerController _player;
        private readonly AvatarView _view;
        private readonly Animator _animator;
        private readonly NavMeshAgent _agent;
        private readonly SkinCache[] _skins;
        private readonly Capture _capture;
        private readonly List<Vector3> _vertices;
        private readonly List<AnimatorClipInfo> _clips = new List<AnimatorClipInfo>(4);
        private readonly Sample[] _samples;
        private readonly double _interval;
        private Mesh _baked;
        private int _count;
        private double _nextAt, _lastAt;
        private Vector3 _lastRoot;
        private static readonly int SpeedParameter = Animator.StringToHash("speed");
        public int Count => _count;

        public DeviceGaitSampler(LocalPlayerController player, string label, int groundMask = ~0,
            int sampleRateHz = 20, int maximumSamples = 120)
        {
            _player = player != null ? player : throw new ArgumentNullException(nameof(player));
            _view = player.GetComponent<AvatarView>();
            _animator = _view != null ? _view.Animator : null;
            if (_animator == null || !_animator.isHuman || _animator.avatar == null || !_animator.avatar.isValid)
                throw new InvalidOperationException("Gait capture requires the player's own valid Humanoid.");
            _agent = player.GetComponent<NavMeshAgent>();
            _skins = CacheSkins();
            if (_skins.Length == 0) throw new InvalidOperationException("Gait capture found no authored skinned renderers.");
            sampleRateHz = Mathf.Clamp(sampleRateHz, 12, 24);
            maximumSamples = Mathf.Clamp(maximumSamples, 1, 120);
            _samples = new Sample[maximumSamples];
            _vertices = new List<Vector3>();
            _interval = 1d / sampleRateHz;
            _nextAt = _lastAt = Time.realtimeSinceStartupAsDouble;
            _lastRoot = player.transform.position;
            _capture = new Capture
            {
                label = label, avatar = _animator.avatar.name,
                controller = _animator.runtimeAnimatorController != null ? _animator.runtimeAnimatorController.name : "",
                renderer = "per-sample observed renderer (see renderers and selectedRendererIndex)", mesh = "per-renderer", vertexCount = 0,
                renderers = Array.ConvertAll(_skins, cache => cache.info),
                unityVersion = Application.unityVersion, requestedHz = sampleRateHz, maximumSamples = maximumSamples,
                groundMask = groundMask, startedRealtime = _nextAt,
                rootScale = player.transform.lossyScale,
                leftFixedFootIndex = -1, leftFixedToeIndex = -1, rightFixedFootIndex = -1, rightFixedToeIndex = -1,
                coordinates = "Unity world metres and player-root metres; Y up. Ground rays use the supplied venue mask, ignore triggers, and require an upward surface normal > 0.7.",
                limitations = "Diagnostic CPU skinning/raycast cost changes frame timing; not performance evidence. Samples only when exactly one eligible footwear renderer reports enabled, active and Renderer.isVisible after EndOfFrame. Visibility can include other cameras/shadows and is not proof of GPU draw submission. All renderer observations and declared LOD memberships are recorded; AvatarView.LodTier is a separate crowd budget tier. Missing/ambiguous/invalid visible samples have geometryMeasured=false and no accepted contact measurement. Sole set comes from rest Foot+Toes weight > 0.5. Fixed material anchors are independent rest-low vertices with Foot or Toes weight >= 0.75; index -1 means unavailable. Lowest vertex may change between samples. No animation, controller, root, LOD or collider setting is changed."
            };
            _baked = new Mesh { name = "Temporary device gait snapshot" };
        }

        private SkinCache[] CacheSkins()
        {
            var result = new List<SkinCache>();
            var groups = _player.GetComponentsInChildren<LODGroup>(true);
            foreach (var skin in _player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var source = skin.sharedMesh;
                var memberships = new List<string>();
                foreach (var group in groups)
                {
                    var lods = group.GetLODs();
                    for (int i = 0; i < lods.Length; i++)
                        if (Array.IndexOf(lods[i].renderers, skin) >= 0) memberships.Add(PathOf(group.transform) + ":LOD" + i);
                }
                var cache = new SkinCache { skin = skin, source = source, info = new RendererInfo {
                    index = result.Count, instanceId = skin.GetInstanceID(), path = PathOf(skin.transform),
                    mesh = source != null ? source.name : "", vertexCount = source != null ? source.vertexCount : 0,
                    lodMembership = memberships.ToArray(), leftFixedFootIndex = -1, leftFixedToeIndex = -1,
                    rightFixedFootIndex = -1, rightFixedToeIndex = -1
                } };
                try
                {
                    if (source == null || !source.isReadable) throw new InvalidOperationException("Mesh missing or unreadable.");
                    var rest = source.vertices; var weights = source.boneWeights; var bones = skin.bones;
                    cache.left = Select(weights, rest, bones, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
                    cache.right = Select(weights, rest, bones, HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
                    cache.info.leftVertexCount = cache.left.vertices.Length; cache.info.rightVertexCount = cache.right.vertices.Length;
                    cache.info.leftFixedFootIndex = cache.left.fixedFootIndex; cache.info.leftFixedToeIndex = cache.left.fixedToeIndex;
                    cache.info.rightFixedFootIndex = cache.right.fixedFootIndex; cache.info.rightFixedToeIndex = cache.right.fixedToeIndex;
                }
                catch (InvalidOperationException error) { cache.info.unavailableReason = error.Message; }
                result.Add(cache);
            }
            return result.ToArray();
        }
        private string PathOf(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null && transform != _player.transform)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }
        private RendererObservation[] ObserveRenderers(out SkinCache selected, out string status)
        {
            var observed = new RendererObservation[_skins.Length];
            selected = null;
            int visible = 0, eligible = 0;
            foreach (var cache in _skins)
            {
                var skin = cache.skin;
                bool exists = skin != null;
                var observation = new RendererObservation {
                    rendererIndex = cache.info.index,
                    enabled = exists && skin.enabled, active = exists && skin.gameObject.activeInHierarchy,
                    visible = exists && skin.isVisible, meshMatchesCache = exists && skin.sharedMesh == cache.source,
                    eligible = exists && skin.sharedMesh == cache.source && string.IsNullOrEmpty(cache.info.unavailableReason)
                };
                observed[cache.info.index] = observation;
                if (!observation.enabled || !observation.active || !observation.visible) continue;
                visible++;
                if (observation.eligible) { selected = cache; eligible++; }
            }
            // Record ambiguity rather than force a LOD, assume crowd-tier equals Unity LOD, or silently sample the hidden high tier.
            if (visible == 0) { selected = null; status = "missing-visible-renderer"; _capture.missingVisibleSamples++; }
            else if (visible > 1) { selected = null; status = "ambiguous-visible-renderers"; _capture.ambiguousVisibleSamples++; }
            else if (eligible != 1) { selected = null; status = "invalid-visible-renderer"; _capture.invalidVisibleSamples++; }
            else { status = "measured"; _capture.measuredSamples++; }
            return observed;
        }

        private Selection Select(BoneWeight[] weights, Vector3[] rest, Transform[] bones,
            HumanBodyBones footBone, HumanBodyBones toeBone)
        {
            var selection = new Selection { foot = _animator.GetBoneTransform(footBone), toe = _animator.GetBoneTransform(toeBone) };
            if (selection.foot == null || selection.toe == null) throw new InvalidOperationException("Missing mapped foot/toe bones.");
            int footIndex = Array.IndexOf(bones, selection.foot), toeIndex = Array.IndexOf(bones, selection.toe);
            if (footIndex < 0 || toeIndex < 0 || weights.Length != rest.Length)
                throw new InvalidOperationException("Body mesh weights do not contain the Animator's mapped feet.");
            var selected = new List<int>();
            float footY = float.PositiveInfinity, toeY = float.PositiveInfinity;
            for (int i = 0; i < weights.Length; i++)
            {
                float footWeight = Weight(weights[i], footIndex), toeWeight = Weight(weights[i], toeIndex);
                if (footWeight + toeWeight <= .5f) continue;
                selected.Add(i);
                if (footWeight >= .75f && rest[i].y < footY) { footY = rest[i].y; selection.fixedFootIndex = i; }
                if (toeWeight >= .75f && rest[i].y < toeY) { toeY = rest[i].y; selection.fixedToeIndex = i; }
            }
            if (selected.Count == 0) throw new InvalidOperationException("No rest-weighted footwear vertices.");
            selection.vertices = selected.ToArray();
            return selection;
        }
        private static float Weight(BoneWeight w, int bone) =>
            (w.boneIndex0 == bone ? w.weight0 : 0f) + (w.boneIndex1 == bone ? w.weight1 : 0f) +
            (w.boneIndex2 == bone ? w.weight2 : 0f) + (w.boneIndex3 == bone ? w.weight3 : 0f);

        /// <summary>Caller awaits EndOfFrame first. Rate limiting skips work; no catch-up samples or Animator.Update.</summary>
        public bool SampleAfterEndOfFrame()
        {
            if (_baked == null) throw new ObjectDisposedException(nameof(DeviceGaitSampler));
            if (_count >= _samples.Length) { _capture.reachedCapacity = true; return false; }
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < _nextAt) return false;
            _nextAt = now + _interval;
            long before = System.Diagnostics.Stopwatch.GetTimestamp();
            var observations = ObserveRenderers(out var selected, out string geometryStatus);
            if (selected != null)
            {
                selected.skin.BakeMesh(_baked, false);
                _baked.GetVertices(_vertices);
                if (_vertices.Count != selected.source.vertexCount) throw new InvalidOperationException("Baked vertex count changed.");
            }
            var root = _player.transform;
            var state = _animator.GetCurrentAnimatorStateInfo(0);
            bool transitioning = _animator.IsInTransition(0);
            var next = transitioning ? _animator.GetNextAnimatorStateInfo(0) : default;
            _clips.Clear();
            _animator.GetCurrentAnimatorClipInfo(0, _clips);
            AnimatorClipInfo main = default;
            foreach (var clip in _clips) if (clip.weight > main.weight) main = clip;
            var sample = new Sample
            {
                frame = Time.frameCount, realtime = now, elapsed = now - _capture.startedRealtime,
                unityDeltaTime = Time.deltaTime, rootWorld = root.position, rootRotation = root.rotation, rootForward = root.forward,
                actualVelocity = _player.ActualVelocity, actualSpeed = _player.ActualVelocity.magnitude,
                observedRootSpeed = _count > 0 && now > _lastAt ? Vector3.Distance(root.position, _lastRoot) / (float)(now - _lastAt) : 0f,
                stateHash = state.fullPathHash, state = StateName(state), normalizedTime = state.normalizedTime,
                stateSpeed = state.speed, stateSpeedMultiplier = state.speedMultiplier, animatorSpeed = _animator.speed,
                speedParameter = _animator.GetFloat(SpeedParameter), inTransition = transitioning,
                nextStateHash = next.fullPathHash, nextNormalizedTime = next.normalizedTime,
                transitionNormalizedTime = transitioning ? _animator.GetAnimatorTransitionInfo(0).normalizedTime : 0f,
                clip = main.clip != null ? main.clip.name : "", clipLength = main.clip != null ? main.clip.length : 0f, clipWeight = main.weight,
                agentVelocity = _agent != null ? _agent.velocity : Vector3.zero,
                navPosition = _agent != null && _agent.isOnNavMesh ? _agent.nextPosition : root.position,
                agentBaseOffset = _agent != null ? _agent.baseOffset : 0f,
                rootGround = ProbeGround(root.position),
                left = selected != null ? Measure(selected, selected.left) : new Foot { lowestVertex = -1 },
                right = selected != null ? Measure(selected, selected.right) : new Foot { lowestVertex = -1 },
                lodTier = _view.LodTier, bodyEnabled = selected != null && selected.skin.enabled,
                bodyVisible = selected != null && selected.skin.isVisible, animatorEnabled = _animator.enabled,
                selectedRendererIndex = selected != null ? selected.info.index : -1,
                geometryMeasured = selected != null, geometryStatus = geometryStatus, rendererObservations = observations
            };
            var head = _animator.GetBoneTransform(HumanBodyBones.Head);
            var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            sample.headRoot = root.InverseTransformPoint(head.position);
            sample.hipsRoot = root.InverseTransformPoint(hips.position);
            var torso = sample.headRoot - sample.hipsRoot;
            sample.headHipLateralDegrees = Mathf.Atan2(torso.x, torso.y) * Mathf.Rad2Deg;
            sample.headHipForwardDegrees = Mathf.Atan2(torso.z, torso.y) * Mathf.Rad2Deg;
            sample.sampleCostMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - before) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            _samples[_count++] = sample;
            _capture.reachedCapacity = _count >= _samples.Length;
            _lastAt = now; _lastRoot = root.position;
            return true;
        }

        private Foot Measure(SkinCache cache, Selection selection)
        {
            var root = _player.transform;
            Matrix4x4 matrix = cache.skin.localToWorldMatrix;
            Matrix4x4 toRoot = root.worldToLocalMatrix * matrix;
            var foot = new Foot
            {
                ankleWorld = selection.foot.position, toeWorld = selection.toe.position,
                ankleRoot = root.InverseTransformPoint(selection.foot.position), toeRoot = root.InverseTransformPoint(selection.toe.position),
                ankleRotationWorld = selection.foot.rotation, toeRotationWorld = selection.toe.rotation,
                soleMinWorldY = float.PositiveInfinity, soleMinRootY = float.PositiveInfinity,
                fixedFootSoleWorld = selection.fixedFootIndex >= 0 ? matrix.MultiplyPoint3x4(_vertices[selection.fixedFootIndex]) : Vector3.zero,
                fixedToeSoleWorld = selection.fixedToeIndex >= 0 ? matrix.MultiplyPoint3x4(_vertices[selection.fixedToeIndex]) : Vector3.zero
            };
            foreach (int i in selection.vertices)
            {
                Vector3 world = matrix.MultiplyPoint3x4(_vertices[i]);
                if (float.IsNaN(world.y) || float.IsInfinity(world.y)) throw new InvalidOperationException("Nonfinite baked footwear.");
                foot.soleMinRootY = Mathf.Min(foot.soleMinRootY, toRoot.MultiplyPoint3x4(_vertices[i]).y);
                if (world.y < foot.soleMinWorldY) { foot.soleMinWorldY = world.y; foot.lowestPointWorld = world; foot.lowestVertex = i; }
            }
            foot.ground = ProbeGround(foot.lowestPointWorld);
            foot.soleGapToGround = foot.ground.found ? foot.soleMinWorldY - foot.ground.position.y : 0f;
            return foot;
        }
        private Ground ProbeGround(Vector3 point)
        {
            // Same vertical vicinity as local grounding; probe each sole's XZ independently on slopes.
            point.y = _player.transform.position.y + .35f;
            bool found = Physics.Raycast(point, Vector3.down, out var hit, .85f, _capture.groundMask, QueryTriggerInteraction.Ignore)
                && hit.normal.y > .7f;
            return new Ground { found = found, position = found ? hit.point : Vector3.zero, normal = found ? hit.normal : Vector3.zero };
        }
        private static string StateName(AnimatorStateInfo state) => state.IsName("loco_walk") ? "loco_walk" :
            state.IsName("loco_run") ? "loco_run" : state.IsName("loco_idle") ? "loco_idle" : "other";

        /// <summary>Call in finally to preserve partial observations and release the temporary native mesh.</summary>
        public void WriteAndDispose(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                _capture.samplesWritten = _count;
                _capture.samples = new Sample[_count];
                Array.Copy(_samples, _capture.samples, _count);
                string stem = "gait-" + SafeName(_capture.label);
                File.WriteAllText(Path.Combine(directory, stem + ".json"), JsonUtility.ToJson(_capture, true));
                var csv = new StringBuilder("frame,elapsed,realtime,state,phase,in_transition,next_phase,speed,observed_root_speed,speed_parameter,state_speed,multiplier,root_x,root_y,root_z,root_ground_found,root_ground_y,left_ankle_x,left_ankle_y,left_ankle_z,left_toe_x,left_toe_y,left_toe_z,right_ankle_x,right_ankle_y,right_ankle_z,right_toe_x,right_toe_y,right_toe_z,left_sole_world_y,right_sole_world_y,left_sole_root_y,right_sole_root_y,left_ground_found,right_ground_found,left_gap,right_gap,left_lowest_vertex,right_lowest_vertex,sample_cost_ms,geometry_measured,geometry_status,renderer_index,head_hip_lateral_degrees,head_hip_forward_degrees\n");
                foreach (var sample in _capture.samples)
                {
                    csv.Append(sample.frame).Append(',').Append(N(sample.elapsed)).Append(',').Append(N(sample.realtime)).Append(',')
                        .Append(sample.state).Append(',').Append(N(sample.normalizedTime)).Append(',').Append(sample.inTransition ? 1 : 0).Append(',')
                        .Append(N(sample.nextNormalizedTime)).Append(',').Append(N(sample.actualSpeed)).Append(',').Append(N(sample.observedRootSpeed)).Append(',')
                        .Append(N(sample.speedParameter)).Append(',').Append(N(sample.stateSpeed)).Append(',').Append(N(sample.stateSpeedMultiplier)).Append(',');
                    CsvVector(csv, sample.rootWorld); csv.Append(sample.rootGround.found ? 1 : 0).Append(',').Append(N(sample.rootGround.position.y)).Append(',');
                    CsvVector(csv, sample.left.ankleWorld); CsvVector(csv, sample.left.toeWorld);
                    CsvVector(csv, sample.right.ankleWorld); CsvVector(csv, sample.right.toeWorld);
                    csv.Append(N(sample.left.soleMinWorldY)).Append(',').Append(N(sample.right.soleMinWorldY)).Append(',')
                        .Append(N(sample.left.soleMinRootY)).Append(',').Append(N(sample.right.soleMinRootY)).Append(',')
                        .Append(sample.left.ground.found ? 1 : 0).Append(',').Append(sample.right.ground.found ? 1 : 0).Append(',')
                        .Append(N(sample.left.soleGapToGround)).Append(',').Append(N(sample.right.soleGapToGround)).Append(',')
                        .Append(sample.left.lowestVertex).Append(',').Append(sample.right.lowestVertex).Append(',').Append(N(sample.sampleCostMilliseconds)).Append(',')
                        .Append(sample.geometryMeasured ? 1 : 0).Append(',').Append(sample.geometryStatus).Append(',')
                        .Append(sample.selectedRendererIndex).Append(',').Append(N(sample.headHipLateralDegrees)).Append(',')
                        .Append(N(sample.headHipForwardDegrees)).Append('\n');
                }
                File.WriteAllText(Path.Combine(directory, stem + ".csv"), csv.ToString());
            }
            finally { Dispose(); }
        }
        private static void CsvVector(StringBuilder builder, Vector3 v) => builder.Append(N(v.x)).Append(',').Append(N(v.y)).Append(',').Append(N(v.z)).Append(',');
        private static string N(double number) => number.ToString("R", CultureInfo.InvariantCulture);
        private static string SafeName(string value)
        {
            var result = new StringBuilder();
            foreach (char c in value ?? "capture") result.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return result.ToString();
        }
        public void Dispose() { if (_baked != null) { Object.Destroy(_baked); _baked = null; } }
    }
}
#endif
