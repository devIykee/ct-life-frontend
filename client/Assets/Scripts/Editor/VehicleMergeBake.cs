using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using CTLife.World.Vehicles;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Bakes <see cref="VehicleMerge"/> into the ground vehicles' prefabs (perf lane 2, 2026-10-23): per LOD, one mesh of the
    /// parts that hold still relative to the body (LOD0/LOD1: body and doors; LOD2/LOD3: the wheels too), one sub-mesh per
    /// material, in the prefab's rest pose, under Model as <c>Merged_LOD&lt;n&gt;</c>, added to that LOD's renderers.
    /// Mirrored parts keep their winding. Flying kinds (hovercar, heli, drone) are left alone. Idempotent; runs at the end
    /// of the vehicle import and on its own:
    /// Unity -batchmode -quit -projectPath client -executeMethod CTLife.EditorTools.VehicleMergeBake.Run
    /// </summary>
    public static class VehicleMergeBake
    {
        public const string MeshDir = VehicleImport.Root + "/merged";

        public static void Run()
        {
            bool ok = true;
            try { BakeAll(); }
            catch (Exception e) { Debug.LogException(e); ok = false; }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static void BakeAll()
        {
            Directory.CreateDirectory(MeshDir);
            foreach (var path in Directory.GetFiles(VehicleImport.PrefabRoot, "*.prefab").Select(p => p.Replace('\\', '/')).OrderBy(p => p))
                Bake(path);
            AssetDatabase.SaveAssets();
        }

        private static string Strip(string n) => n.Length > 5 && n.StartsWith("LOD", StringComparison.Ordinal) && n[4] == '_' ? n.Substring(5) : n;
        private static int LodOf(string n) => n.StartsWith("LOD3_", StringComparison.Ordinal) ? 3 : n.StartsWith("LOD2_", StringComparison.Ordinal) ? 2 : n.StartsWith("LOD1_", StringComparison.Ordinal) ? 1 : 0;

        /// <summary>Parts that move relative to the body: the wheels (at LOD0/1), the steering wheel, rotors and thrusters.</summary>
        private static bool Moves(Transform t, Transform model, bool far)
        {
            for (var c = t; c != null && c != model; c = c.parent)
            {
                string n = Strip(c.name);
                if (n.StartsWith("Steering", StringComparison.Ordinal) || n.StartsWith("Rotor_", StringComparison.Ordinal) || n == "TailRotor"
                    || n.StartsWith("Thruster_", StringComparison.Ordinal) || n.StartsWith("Glow_", StringComparison.Ordinal)) return true;
                if (!far && n.StartsWith("Wheel_", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static void Bake(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var veh = root.GetComponent<Vehicle>();
                var lg = root.GetComponent<LODGroup>();
                var spec = veh != null ? VehicleCatalog.Get(veh.specId) : null;
                var model = root.transform.Find("Model");
                if (veh == null || lg == null || spec == null || model == null || VehicleCatalog.Flies(spec.kind)) return;
                string id = Path.GetFileNameWithoutExtension(path);
                // idempotent: drop an earlier bake
                foreach (Transform c in model.Cast<Transform>().ToList())
                    if (c.name.StartsWith("Merged_LOD", StringComparison.Ordinal)) Object.DestroyImmediate(c.gameObject);
                var lods = lg.GetLODs();
                for (int i = 0; i < lods.Length; i++) lods[i].renderers = lods[i].renderers.Where(r => r != null).ToArray();
                var merged = new List<Renderer>();
                var parts = new List<Renderer>();
                var report = new List<string>();
                for (int li = 0; li < lods.Length; li++)
                {
                    bool far = li >= 2;
                    var take = lods[li].renderers.OfType<MeshRenderer>()
                        .Where(r => r.GetComponent<MeshFilter>()?.sharedMesh != null && !Moves(r.transform, model, far)).ToList();
                    int before = take.Sum(r => Mathf.Min(r.sharedMaterials.Length, r.GetComponent<MeshFilter>().sharedMesh.subMeshCount));
                    if (take.Count < 2) continue;
                    int lodN = LodOf(take[0].name);
                    var mesh = Combine(take, model, out var mats);
                    mesh.name = $"{id}_merged_LOD{lodN}";
                    string meshPath = $"{MeshDir}/{id}_LOD{lodN}.asset";
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (existing != null) { EditorUtility.CopySerialized(mesh, existing); mesh = existing; EditorUtility.SetDirty(existing); }
                    else AssetDatabase.CreateAsset(mesh, meshPath);
                    // GPU-only at runtime, like the FBX meshes (no CPU copy kept in the player)
                    var so = new SerializedObject(mesh);
                    so.FindProperty("m_IsReadable").boolValue = false;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    var go = new GameObject($"Merged_LOD{lodN}");
                    go.layer = take[0].gameObject.layer;
                    go.transform.SetParent(model, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = mats;
                    mr.shadowCastingMode = take.Any(r => r.shadowCastingMode != ShadowCastingMode.Off) ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    mr.receiveShadows = take[0].receiveShadows;
                    mr.lightProbeUsage = take[0].lightProbeUsage;
                    mr.reflectionProbeUsage = take[0].reflectionProbeUsage;
                    mr.enabled = false;   // VehicleMerge turns it on at runtime
                    var list = lods[li].renderers.ToList(); list.Add(mr); lods[li].renderers = list.ToArray();
                    merged.Add(mr); parts.AddRange(take);
                    report.Add($"LOD{lodN} {take.Count} parts / {before} draws -> {mats.Length}");
                }
                lg.SetLODs(lods);
                var vm = root.GetComponent<VehicleMerge>();
                if (merged.Count == 0) { if (vm != null) Object.DestroyImmediate(vm); }
                else
                {
                    if (vm == null) vm = root.AddComponent<VehicleMerge>();
                    vm.merged = merged.ToArray();
                    vm.parts = parts.ToArray();
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"CTLIFE [vehicle-merge] {id}: {string.Join("; ", report)}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>The renderers' sub-meshes in <paramref name="space"/>, grouped by material (one sub-mesh each, in first-seen
        /// order); mirrored transforms flip their triangles so the faces keep pointing out.</summary>
        private static Mesh Combine(List<MeshRenderer> rs, Transform space, out Material[] mats)
        {
            var order = new List<Material>();
            var groups = new Dictionary<Material, List<int>>();
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var tan = new List<Vector4>(); var uv = new List<Vector2>();
            bool hasTan = true;
            var tmpV = new List<Vector3>(); var tmpN = new List<Vector3>(); var tmpT = new List<Vector4>(); var tmpU = new List<Vector2>();
            foreach (var r in rs)
            {
                var m = r.GetComponent<MeshFilter>().sharedMesh;
                if (m.tangents.Length != m.vertexCount) hasTan = false;
            }
            foreach (var r in rs)
            {
                var m = r.GetComponent<MeshFilter>().sharedMesh;
                var mat = space.worldToLocalMatrix * r.transform.localToWorldMatrix;
                bool mirror = mat.determinant < 0f;
                var nm = mat.inverse.transpose;
                int baseV = pos.Count;
                m.GetVertices(tmpV); m.GetNormals(tmpN); m.GetUVs(0, tmpU);
                if (hasTan) m.GetTangents(tmpT);
                for (int i = 0; i < tmpV.Count; i++)
                {
                    pos.Add(mat.MultiplyPoint3x4(tmpV[i]));
                    nrm.Add(i < tmpN.Count ? nm.MultiplyVector(tmpN[i]).normalized : Vector3.up);
                    uv.Add(i < tmpU.Count ? tmpU[i] : Vector2.zero);
                    if (hasTan)
                    {
                        var t = tmpT[i];
                        var d = mat.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                        tan.Add(new Vector4(d.x, d.y, d.z, mirror ? -t.w : t.w));
                    }
                }
                var sm = r.sharedMaterials;
                for (int s = 0; s < m.subMeshCount && s < sm.Length; s++)
                {
                    var key = sm[s];
                    if (key == null) continue;
                    if (!groups.TryGetValue(key, out var idx)) { groups[key] = idx = new List<int>(); order.Add(key); }
                    var tri = m.GetTriangles(s);
                    for (int k = 0; k + 2 < tri.Length; k += 3)
                    {
                        idx.Add(baseV + tri[k]);
                        if (mirror) { idx.Add(baseV + tri[k + 2]); idx.Add(baseV + tri[k + 1]); }
                        else { idx.Add(baseV + tri[k + 1]); idx.Add(baseV + tri[k + 2]); }
                    }
                }
            }
            var mesh = new Mesh { indexFormat = pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(pos); mesh.SetNormals(nrm); mesh.SetUVs(0, uv);
            if (hasTan) mesh.SetTangents(tan);
            mesh.subMeshCount = order.Count;
            for (int s = 0; s < order.Count; s++) mesh.SetTriangles(groups[order[s]], s, true);
            mesh.RecalculateBounds();
            mats = order.ToArray();
            return mesh;
        }
    }
}
