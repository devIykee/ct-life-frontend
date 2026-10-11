using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Import-time guard for the Rain-derived (f family) cast meshes. The uncorrected Rain source keeps stale
    /// DEF-COR-Hip_* / DEF-COR-Ankle_* vertex groups that the old conversion sent to Head
    /// (art/character-candidates/rain-weight-correction/README.md). Rain's own creator/LOD FBX carry the reviewed
    /// correction; the cast export inherited the defect: shoe, ankle and hip vertices follow the head by 30–50%, so shoes
    /// smear as soon as the legs move (walking, sitting). Same rule as the reviewed correction: a lower-body vertex (one
    /// that also carries pelvis or leg weight, below the neck) has no Head influence; the remaining anatomical weights are
    /// renormalized. Hair, hats and scarves (Head/Neck weights without any leg or pelvis weight) are untouched. A no-op
    /// once the cast export is corrected at the source.
    /// The looks import with Optimize Game Objects, so the skinned meshes have no bone transforms by OnPostprocessModel:
    /// the rig is recorded in OnPostprocessMeshHierarchy and each bind pose is matched back to its bone.
    /// </summary>
    public sealed class CastWeightRepair : AssetPostprocessor
    {
        public override uint GetVersion() => 7;
        public override int GetPostprocessOrder() => 60;

        private sealed class Bone { public string name, parent; public Matrix4x4 world; public Vector3 Position => world.GetColumn(3); }
        private readonly List<Bone> _rig = new List<Bone>();

        private bool IsCast => assetPath.StartsWith(CastImport.Root + "/", StringComparison.Ordinal);

        private void OnPostprocessMeshHierarchy(GameObject root)
        {
            if (!IsCast) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.GetComponent<MeshFilter>() == null && t.GetComponent<Renderer>() == null)
                    _rig.Add(new Bone { name = t.name, parent = t.parent != null ? t.parent.name : null, world = t.localToWorldMatrix });
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!IsCast || _rig.Count == 0) return;
            int changed = 0;
            float worst = 0f;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var names = MatchBones(smr, out float err, out var toRig);
                worst = Mathf.Max(worst, err);
                if (names == null || err > 0.002f) { Debug.LogWarning($"CTLIFE [cast-weights] {assetPath}#{smr.name}: bind poses do not match the rig ({err:F4} m); not checked"); continue; }
                changed += Repair(smr.sharedMesh, toRig, names, _rig);
            }
            if (changed > 0) Debug.Log($"CTLIFE [cast-weights] {assetPath}: removed stale Head influence from {changed} lower-body vertices (bind match {worst * 1000f:F2} mm)");
        }

        /// <summary>
        /// Bone name per bind pose: the rig bone at that bind pose's position. The bind poses can sit in the source's axis
        /// frame (Blender Z-up) rather than the converted hierarchy's, so the best of the 24 axis-aligned frames is used;
        /// <paramref name="toRig"/> maps mesh space into the rig frame (heights for the lower-body test).
        /// </summary>
        private string[] MatchBones(SkinnedMeshRenderer smr, out float worst, out Matrix4x4 toRig)
        {
            worst = float.MaxValue; toRig = Matrix4x4.identity;
            var mesh = smr.sharedMesh;
            if (mesh == null || mesh.bindposes.Length == 0) return null;
            var meshWorld = smr.transform.localToWorldMatrix;
            var pos = new Vector3[mesh.bindposes.Length];
            for (int i = 0; i < pos.Length; i++) pos[i] = (meshWorld * mesh.bindposes[i].inverse).GetColumn(3);
            string[] best = null;
            foreach (var frame in AxisFrames())
            {
                var names = new string[pos.Length];
                float err = 0f;
                for (int i = 0; i < pos.Length && err < worst; i++)
                {
                    var p = frame.MultiplyVector(pos[i]);
                    float d = float.MaxValue;
                    foreach (var b in _rig) { float e = (b.Position - p).sqrMagnitude; if (e < d) { d = e; names[i] = b.name; } }
                    err = Mathf.Max(err, Mathf.Sqrt(d));
                }
                if (err < worst) { worst = err; best = names; toRig = frame * meshWorld; }
            }
            return best;
        }

        private static IEnumerable<Matrix4x4> AxisFrames()
        {
            var axes = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var x in axes)
                foreach (var y in axes)
                {
                    if (Mathf.Abs(Vector3.Dot(x, y)) > 0.5f) continue;
                    var z = Vector3.Cross(x, y);
                    var m = Matrix4x4.identity;
                    m.SetColumn(0, x); m.SetColumn(1, y); m.SetColumn(2, z);
                    yield return m;
                }
        }

        /// <summary>Returns the number of vertices changed.</summary>
        private static int Repair(Mesh mesh, Matrix4x4 meshWorld, string[] names, List<Bone> rig)
        {
            int head = Array.IndexOf(names, "Head"), hips = Array.IndexOf(names, "Hips");
            if (head < 0 || hips < 0) return 0;
            var byName = new Dictionary<string, Bone>();
            foreach (var b in rig) byName[b.name] = b;
            float headY = byName["Head"].Position.y;
            // Lower body: the pelvis and every bone under a pelvis child other than the spine (the one leading to Head).
            string spineChild = null;
            for (var b = byName["Head"]; b != null && b.parent != null; b = byName.TryGetValue(b.parent, out var p) ? p : null)
                if (b.parent == "Hips") { spineChild = b.name; break; }
            bool Lower(string n)
            {
                if (n == "Hips") return true;
                for (var b = byName.TryGetValue(n, out var x) ? x : null; b != null && b.parent != null; b = byName.TryGetValue(b.parent, out var p) ? p : null)
                    if (b.parent == "Hips") return b.name != spineChild;
                return false;
            }
            var lower = new bool[names.Length];
            for (int i = 0; i < names.Length; i++) lower[i] = names[i] != null && Lower(names[i]);
            var weights = mesh.boneWeights;
            if (weights.Length == 0) return 0;
            var verts = mesh.vertices;
            int changed = 0;
            var idx = new int[4];
            var w = new float[4];
            for (int v = 0; v < weights.Length; v++)
            {
                var bw = weights[v];
                idx[0] = bw.boneIndex0; idx[1] = bw.boneIndex1; idx[2] = bw.boneIndex2; idx[3] = bw.boneIndex3;
                w[0] = bw.weight0; w[1] = bw.weight1; w[2] = bw.weight2; w[3] = bw.weight3;
                float headW = 0f, lowerW = 0f;
                for (int k = 0; k < 4; k++)
                {
                    if (w[k] <= 0f || idx[k] < 0 || idx[k] >= names.Length) continue;
                    if (idx[k] == head) headW += w[k];
                    else if (lower[idx[k]]) lowerW += w[k];
                }
                if (headW <= 0f || lowerW <= 0f) continue;
                // Well below the head (the reviewed check: no Head influence under 1.15 m on Rain's 1.49 m head joint).
                if (meshWorld.MultiplyPoint3x4(verts[v]).y > headY - 0.34f) continue;
                float rest = 0f;
                for (int k = 0; k < 4; k++) { if (idx[k] == head) w[k] = 0f; rest += w[k]; }
                for (int k = 0; k < 4; k++) w[k] = rest > 0f ? w[k] / rest : 0f;
                // Keep the influences in descending order, as Unity stores them.
                for (int a = 0; a < 3; a++)
                    for (int c = a + 1; c < 4; c++)
                        if (w[c] > w[a]) { (w[a], w[c]) = (w[c], w[a]); (idx[a], idx[c]) = (idx[c], idx[a]); }
                for (int k = 0; k < 4; k++) if (w[k] <= 0f) idx[k] = 0;
                weights[v] = new BoneWeight
                {
                    boneIndex0 = idx[0], weight0 = w[0], boneIndex1 = idx[1], weight1 = w[1],
                    boneIndex2 = idx[2], weight2 = w[2], boneIndex3 = idx[3], weight3 = w[3],
                };
                changed++;
            }
            if (changed > 0) mesh.boneWeights = weights;
            return changed;
        }
    }
}
