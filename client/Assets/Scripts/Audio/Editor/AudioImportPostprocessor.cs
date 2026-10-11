using UnityEditor;
using UnityEngine;

namespace CTLife.Audio.EditorTools
{
    /// <summary>
    /// Applies <see cref="ImportPolicy"/> (CTLife.Audio/Logic/ImportPolicy.cs, unit-tested) to every clip under
    /// Assets/Resources/Audio/: default and Android settings per group (music, music layers, ambience, stingers,
    /// footsteps, babble, other one-shots). Runs after the general content postprocessor (order 100), so this audio
    /// policy is authoritative for audio even while ContentImport still has its older OnPreprocessAudio block.
    /// </summary>
    public sealed class AudioImportPostprocessor : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 100;

        private void OnPreprocessAudio()
        {
            if (!ImportPolicy.TryFor(assetPath, out var policy)) return;
            var importer = (AudioImporter)assetImporter;
            var s = importer.defaultSampleSettings;
            s.loadType = policy.Load switch
            {
                ClipLoad.Streaming => AudioClipLoadType.Streaming,
                ClipLoad.CompressedInMemory => AudioClipLoadType.CompressedInMemory,
                _ => AudioClipLoadType.DecompressOnLoad,
            };
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = policy.Quality;
            s.preloadAudioData = policy.Preload;
            if (policy.SampleRate > 0)
            {
                s.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                s.sampleRateOverride = (uint)policy.SampleRate;
            }
            else s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = s;
            importer.SetOverrideSampleSettings("Android", s);
            importer.forceToMono = policy.ForceMono;
            importer.loadInBackground = policy.LoadInBackground;
            importer.ambisonic = false;
        }

        /// <summary>Re-import every clip under Resources/Audio with the current policy (menu, or batch -executeMethod).</summary>
        [MenuItem("CT Life/Audio/Reimport Audio With Policy")]
        public static void ReimportAll()
        {
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources/Audio" });
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var g in guids) AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            Debug.Log($"CTLIFE [audio] reimported {guids.Length} clips with ImportPolicy");
        }
    }
}
