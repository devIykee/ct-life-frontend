using System;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// One batchmode entry for content steps after a lane merge (saves separate editor launches):
    /// Unity -batchmode -quit -projectPath client -executeMethod CTLife.EditorTools.IntegrationBatch.PrepareContent
    /// 1) registers the built venue/home prefabs and syncs the runtime venue catalog, 2) reimports audio with the
    /// audio lane's single import policy.
    /// </summary>
    public static class IntegrationBatch
    {
        public static void PrepareContent()
        {
            int code = 0;
            try
            {
                VenueBuild.VenueIntegration.RegisterBuiltVenues();
                var reimport = Type.GetType("CTLife.Audio.EditorTools.AudioImportPostprocessor, CTLife.Audio.Editor")?.GetMethod("ReimportAll");
                if (reimport == null) Debug.LogWarning("CTLIFE [batch] audio reimport method not found");
                else reimport.Invoke(null, null);
                AssetDatabase.SaveAssets();
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
