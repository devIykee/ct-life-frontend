using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>Debug: dumps a prefab's named objects (position, yaw, bounds) to the evidence receipts folder.
    /// -executeMethod CTLife.EditorTools.VenueBuild.VenueDump.Run -ctDumpPrefab Assets/...prefab</summary>
    public static class VenueDump
    {
        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            string path = args[System.Array.IndexOf(args, "-ctDumpPrefab") + 1];
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var sb = new StringBuilder();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.EndsWith("_LOD0") || t.name.EndsWith("_LOD1") || t.name.EndsWith("_LOD2")) continue;
                var rs = t.GetComponentsInChildren<Renderer>(true);
                var b = rs.Length > 0 ? rs.Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; }) : new Bounds(t.position, Vector3.zero);
                int depth = 0; for (var p = t.parent; p != null; p = p.parent) depth++;
                if (depth > 3) continue;
                sb.AppendLine($"{new string(' ', depth * 2)}{t.name} pos={t.position:F2} yaw={t.eulerAngles.y:F0} scale={t.lossyScale.x:F2} bounds c={b.center:F2} s={b.size:F2}");
            }
            File.WriteAllText(Path.Combine(VenueBuildPipeline.EvidenceDir, "receipts", Path.GetFileNameWithoutExtension(path) + "-dump.txt"), sb.ToString());
        }
    }
}
