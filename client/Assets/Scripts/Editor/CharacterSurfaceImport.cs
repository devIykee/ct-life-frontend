using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CTLife.EditorTools
{
    /// <summary>Apply the reviewed material-side change without regenerating prefabs, meshes, maps or saved looks.</summary>
    public static class CharacterSurfaceImport
    {
        [MenuItem("CT Life/Content/Repair Rain wardrobe surface sides")]
        public static void Run()
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { CastImport.Root + "/materials" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string id = Path.GetFileNameWithoutExtension(path);
                if ((string)CastImport.Look(id)?["family"] != "f") continue;
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                material.SetFloat("_Cull", (float)CullMode.Off); EditorUtility.SetDirty(material);
                string palettePath = "Assets/Art/characters/palettes/humans/" + id + "_Masked.mat";
                var palette = AssetDatabase.LoadAssetAtPath<Material>(palettePath);
                if (palette != null) { palette.SetFloat("_Cull", (float)CullMode.Off); EditorUtility.SetDirty(palette); }
                count++;
            }
            if (count == 0) throw new InvalidDataException("No Rain family materials were found.");
            AssetDatabase.SaveAssets();
            Debug.Log($"CTLIFE [surface-sides] {count} Rain-family materials keep opaque shell backs; existing palettes retained.");
        }

        public static void RunBatch()
        {
            int code = 0;
            try { RaisedHandAnimationImport.Run(); Run(); }
            catch (Exception exception) { Debug.LogException(exception); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
