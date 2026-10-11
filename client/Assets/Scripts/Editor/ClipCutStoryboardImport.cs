#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>Imports five original storyboard atlases only. Does not save scenes or unrelated assets.</summary>
    public static class ClipCutStoryboardImport
    {
        private const string Source="art/direction-20261006/minigame-presentation-20261007/art";
        private const string Output="Assets/Resources/UI/ClipCut";
        private static readonly string[] Hashes={
            "a6ffee4d85820e253618e26ef97bf06540aab4926e97d5e2f05824919501503a",
            "f328c320f0200bf4edd50f8e6d82602efa93044c42f6bcf369d0f3420e8e4e36",
            "6a5164446ae06e15fc2a0cf4ad131648780830d455f4cef5554555840431b450",
            "25e1caf12ac7c737ad341a89216f6279d4414a6b55c14d5d56adcb0fe8d7228e",
            "ff066e4431f367e7afab353763f4a45237ae84975138852d51eecd30b7d65128"};
        private static readonly int[] Widths={836,1144,1145,1374,1385},Heights={1881,1375,1374,1145,1136};
        [Serializable] private sealed class Row{public string path,sha256,format;public int width,height;public long runtimeBytes;}
        [Serializable] private sealed class Receipt{public string status="imported_not_visual_or_device_accepted";public Row[] atlases;}
        public static void Import()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Import requires Edit mode.");
            string project=Directory.GetParent(Application.dataPath).FullName,repo=Directory.GetParent(project).FullName;
            var sources=Enumerable.Range(0,5).Select(i=>Path.Combine(repo,Source,"story-"+i+"-atlas-v1.png")).ToArray();
            for(int i=0;i<5;i++)
            {
                Require(Hash(sources[i])==Hashes[i],"Storyboard source changed: "+i);
                string target=Path.Combine(project,Output,"story-"+i+".png");
                Require(!File.Exists(target)||Hash(target)==Hashes[i],"Existing storyboard differs; no overwrite: "+target);
            }
            Directory.CreateDirectory(Path.Combine(project,Output));var receipt=new Receipt{atlases=new Row[5]};
            for(int i=0;i<5;i++)
            {
                string asset=Output+"/story-"+i+".png",target=Path.Combine(project,asset);
                if(!File.Exists(target))File.Copy(sources[i],target,false);
                AssetDatabase.ImportAsset(asset,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(asset);
                importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.alphaSource=TextureImporterAlphaSource.None;
                importer.mipmapEnabled=false;importer.isReadable=false;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;
                importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.Uncompressed;
                // Keep the Editor reference exact; Android uses ASTC with native device visual review still required.
                var android=importer.GetPlatformTextureSettings("Android");android.overridden=true;android.maxTextureSize=2048;android.format=TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(android);importer.SaveAndReimport();
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
                Require(texture!=null&&texture.width==Widths[i]&&texture.height==Heights[i],"Imported dimensions changed: "+asset);
                Require(Hash(target)==Hashes[i],"Importer changed source PNG bytes: "+asset);
                receipt.atlases[i]=new Row{path=asset,sha256=Hashes[i],width=texture.width,height=texture.height,format=texture.format.ToString(),runtimeBytes=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture)};
            }
            File.WriteAllText(Path.Combine(repo,"art/direction-20261006/minigame-presentation-20261007/import-receipt.json"),JsonUtility.ToJson(receipt,true));
            Debug.Log("[clip-storyboard-import] Five atlases imported; no scene/global asset save.");
        }
        private static string Hash(string path){using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
        private static void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
    }
}
#endif
