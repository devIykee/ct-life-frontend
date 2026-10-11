using System;
using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    // NEW candidate envelope for actual blended poses, not a relaxation of the steady-run table.
    // Called only by the exact Snow/profile owner after a fresh native Animator evaluation.
    public sealed class SnowTransitionContact
    {
        public const float MaximumLift = .08f;
        public const float MaximumAdditionalFlexion = 30f;
        private const float ContactY = .0005f;
        private sealed class Skin
        {
            internal Transform[] bones;
            internal int[] used;
            internal Matrix4x4[] matrices;
            internal Vertex[][] feet;
            internal SkinnedMeshRenderer renderer;
            internal Mesh mesh;
        }
        private struct Vertex
        {
            internal BoneWeight weight;
            internal Vector3 p0, p1, p2, p3;
        }
        private readonly Transform _root;
        private readonly Skin[] _skins;
        private readonly Transform[] _hips = new Transform[2], _knees = new Transform[2], _feet = new Transform[2];
        public int ShoeVertexCount { get; private set; }
        public int MatrixCount { get; private set; }
        public int SolveCount { get; private set; }
        public float MaximumAnkleLift { get; private set; }
        public float MaximumKneeChange { get; private set; }
        public float MaximumTargetError { get; private set; }
        public float MaximumFootRotationError { get; private set; }

        public SnowTransitionContact(Animator animator, Transform root, SkinnedMeshRenderer[] renderers)
        {
            if (animator == null || !animator.isHuman || root == null || renderers == null || renderers.Length != 3)
                throw new ArgumentException("Exact three-skin Humanoid required.");
            _root = root;
            for (int side = 0; side < 2; side++)
            {
                _hips[side] = animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                _knees[side] = animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                _feet[side] = animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                if (_hips[side] == null || _knees[side] == null || _feet[side] == null) throw new ArgumentException("Complete legs required.");
            }
            var skins = new List<Skin>(3);
            foreach (var renderer in renderers)
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null) throw new ArgumentException("Actual skinned mesh required.");
                if (!mesh.isReadable) throw new ArgumentException("Exact source mesh must remain readable at runtime.");
                var bones = renderer.bones; var weights = mesh.boneWeights; var vertices = mesh.vertices; var bind = mesh.bindposes;
                if (weights.Length != vertices.Length || bind.Length != bones.Length) throw new ArgumentException("Fixed linear skinning required.");
                var skin = new Skin { renderer = renderer, mesh = mesh, bones = bones, matrices = new Matrix4x4[bones.Length], feet = new Vertex[2][] };
                var used = new HashSet<int>();
                for (int side = 0; side < 2; side++)
                {
                    var selected = new List<Vertex>();
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        var w = weights[i];
                        float Weight(int b, float value) => value > 0 && (bones[b] == _feet[side] || bones[b].IsChildOf(_feet[side])) ? value : 0;
                        if (Weight(w.boneIndex0, w.weight0) + Weight(w.boneIndex1, w.weight1) + Weight(w.boneIndex2, w.weight2) + Weight(w.boneIndex3, w.weight3) <= .5f) continue;
                        selected.Add(new Vertex { weight = w, p0 = bind[w.boneIndex0].MultiplyPoint3x4(vertices[i]), p1 = bind[w.boneIndex1].MultiplyPoint3x4(vertices[i]), p2 = bind[w.boneIndex2].MultiplyPoint3x4(vertices[i]), p3 = bind[w.boneIndex3].MultiplyPoint3x4(vertices[i]) });
                        // Zero-weight terms are still evaluated; include those matrices as well.
                        used.Add(w.boneIndex0); used.Add(w.boneIndex1); used.Add(w.boneIndex2); used.Add(w.boneIndex3);
                    }
                    if (selected.Count < 3) throw new ArgumentException("Actual shoe vertices required.");
                    skin.feet[side] = selected.ToArray(); ShoeVertexCount += selected.Count;
                }
                skin.used = new int[used.Count]; used.CopyTo(skin.used); MatrixCount += used.Count; skins.Add(skin);
            }
            _skins = skins.ToArray();
            ValidateBindings();
        }

        private void ValidateBindings()
        {
            if (Vector3.Distance(_root.lossyScale, Vector3.one) > .0001f || Vector3.Dot(_root.up, Vector3.up) < .99999f)
                throw new InvalidOperationException("Only the unchanged unit-scale upright actor is supported.");
            foreach (var skin in _skins)
            {
                if (skin.renderer == null || skin.renderer.sharedMesh != skin.mesh) throw new InvalidOperationException("Source skin was replaced.");
                for (int i = 0; i < skin.mesh.blendShapeCount; i++)
                    if (Mathf.Abs(skin.renderer.GetBlendShapeWeight(i)) > .0001f) throw new InvalidOperationException("Changed blendshape needs separate skin proof.");
            }
        }

        // Exact cached linear-skinning evaluation; no BakeMesh, per-frame allocations or forced Animator work.
        // Independent review compares this against all three native BakeMesh results.
        public float MinimumSole(int side)
        {
            float result = float.PositiveInfinity; var inverse = _root.worldToLocalMatrix;
            foreach (var skin in _skins)
            {
                foreach (int i in skin.used) skin.matrices[i] = inverse * skin.bones[i].localToWorldMatrix;
                foreach (var v in skin.feet[side])
                {
                    var w = v.weight;
                    var p = skin.matrices[w.boneIndex0].MultiplyPoint3x4(v.p0) * w.weight0 + skin.matrices[w.boneIndex1].MultiplyPoint3x4(v.p1) * w.weight1 +
                        skin.matrices[w.boneIndex2].MultiplyPoint3x4(v.p2) * w.weight2 + skin.matrices[w.boneIndex3].MultiplyPoint3x4(v.p3) * w.weight3;
                    result = Mathf.Min(result, p.y);
                }
            }
            return result;
        }

        // Owner snapshots all six locals before calling and rolls the entire frame back on any failure.
        public void Apply()
        {
            ValidateBindings();
            for (int side = 0; side < 2; side++)
            {
                var hip = _hips[side]; var knee = _knees[side]; var foot = _feet[side];
                Vector3 originalHip = hip.position, originalKnee = knee.position, originalFoot = foot.position;
                Quaternion originalRotation = foot.rotation;
                float originalAngle = Vector3.Angle(originalHip - originalKnee, originalFoot - originalKnee);
                float upper = Vector3.Distance(originalHip, originalKnee), lower = Vector3.Distance(originalKnee, originalFoot);
                float total = 0;
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    float minimum = MinimumSole(side);
                    if (!float.IsFinite(minimum)) throw new InvalidOperationException("Nonfinite shoe surface.");
                    if (minimum >= -.00005f) break;
                    total += ContactY - minimum;
                    if (!float.IsFinite(total) || total > MaximumLift) throw new InvalidOperationException("Transition exceeds NEW 80mm ankle-lift envelope.");
                    Vector3 target = originalFoot + Vector3.up * total;
                    float distance = Vector3.Distance(originalHip, target);
                    if (distance <= Mathf.Abs(upper - lower) + .00001f || distance >= upper + lower - .00001f)
                        throw new InvalidOperationException("Transition target is outside measured fixed-length reach.");
                    float angle = Mathf.Acos(Mathf.Clamp((upper * upper + lower * lower - distance * distance) / (2 * upper * lower), -1, 1)) * Mathf.Rad2Deg;
                    float additional = originalAngle - angle;
                    if (additional < -.001f || additional > MaximumAdditionalFlexion)
                        throw new InvalidOperationException("Transition exceeds NEW 30-degree additional-flexion envelope.");
                    Solve(side, target);
                    foot.rotation = originalRotation;
                    float error = Vector3.Distance(foot.position, target), rotationError = Quaternion.Angle(foot.rotation, originalRotation);
                    if (error > .0005f || rotationError > .1f) throw new InvalidOperationException("Exact ankle/foot target was not reproduced.");
                    float lengthError = Mathf.Max(Mathf.Abs(Vector3.Distance(hip.position, knee.position) - upper), Mathf.Abs(Vector3.Distance(knee.position, foot.position) - lower));
                    if (lengthError > .0001f || Vector3.Distance(hip.position, originalHip) > .00001f) throw new InvalidOperationException("Leg length or hip changed.");
                    SolveCount++; MaximumAnkleLift = Mathf.Max(MaximumAnkleLift, total); MaximumKneeChange = Mathf.Max(MaximumKneeChange, additional);
                    MaximumTargetError = Mathf.Max(MaximumTargetError, error); MaximumFootRotationError = Mathf.Max(MaximumFootRotationError, rotationError);
                }
                if (MinimumSole(side) < -.002f) throw new InvalidOperationException("Bounded transition solve left actual shoe penetration.");
            }
        }

        private void Solve(int side, Vector3 target)
        {
            var hip = _hips[side]; var knee = _knees[side]; var foot = _feet[side];
            Vector3 a = hip.position, b = knee.position, c = foot.position;
            float upper = Vector3.Distance(a, b), lower = Vector3.Distance(b, c), distance = Vector3.Distance(a, target);
            Vector3 oldAxis = (c - a).normalized, axis = (target - a) / distance;
            Vector3 bend = b - a - oldAxis * Vector3.Dot(b - a, oldAxis);
            // Preserve the actual evaluated bend plane; refuse an undefined straight-leg direction.
            if (bend.sqrMagnitude < 1e-10f) throw new InvalidOperationException("Evaluated knee plane is undefined; no guessed global pole.");
            bend = PreciseFromTo(oldAxis, axis) * bend.normalized;
            float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
            Vector3 nextKnee = a + axis * along + bend * Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
            hip.rotation = PreciseFromTo(b - a, nextKnee - a) * hip.rotation;
            knee.rotation = PreciseFromTo(foot.position - knee.position, target - knee.position) * knee.rotation;
        }
        private static Quaternion PreciseFromTo(Vector3 a, Vector3 b)
        {
            double x = (double)a.y * b.z - (double)a.z * b.y, y = (double)a.z * b.x - (double)a.x * b.z, z = (double)a.x * b.y - (double)a.y * b.x;
            double w = Math.Sqrt(((double)a.x * a.x + (double)a.y * a.y + (double)a.z * a.z) * ((double)b.x * b.x + (double)b.y * b.y + (double)b.z * b.z)) + (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z;
            double norm = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (norm < 1e-12) throw new InvalidOperationException("Opposite/zero leg rotation is outside this bounded solve.");
            return new Quaternion((float)(x / norm), (float)(y / norm), (float)(z / norm), (float)(w / norm));
        }
    }
}
