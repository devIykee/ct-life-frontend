// Compile-check stub for com.unity.cloud.gltfast 6.20.0 (Packages/manifest.json). Only the API surface CT Life uses
// (World/Likeness/LikenessAvatarLoader.cs). The real package also needs Burst/Collections/Mathematics, which this
// out-of-editor harness does not build. Signatures follow the package source in Library/PackageCache.
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GLTFast.Logging
{
    public interface ICodeLogger { }
    public sealed class LogItem { public LogType Type; }
    public sealed class CollectingLogger : ICodeLogger { public IEnumerable<LogItem> Items => null; }
}

namespace GLTFast.Schema
{
    public sealed class TextureInfo { public int index; }
    public sealed class PbrMetallicRoughness { public TextureInfo BaseColorTexture => null; public Color BaseColor => Color.white; }
    public abstract class MaterialBase { public bool doubleSided; public PbrMetallicRoughness PbrMetallicRoughness => null; }
}

namespace GLTFast.Materials
{
    public interface IMaterialGenerator
    {
        Material GetDefaultMaterial(bool pointsSupport = false);
        Material GenerateMaterial(GLTFast.Schema.MaterialBase gltfMaterial, GLTFast.IGltfReadable gltf, bool pointsSupport = false);
        void SetLogger(GLTFast.Logging.ICodeLogger logger);
    }
}

namespace GLTFast
{
    public enum NameImportMethod { OriginalUnique }
    public enum AnimationMethod { None }
    public sealed class ImportSettings { public NameImportMethod NodeNameMethod; public AnimationMethod AnimationMethod; }
    public interface IGltfReadable { Texture2D GetTexture(int index); }
    public class GltfImport : IGltfReadable, System.IDisposable
    {
        public GltfImport(object downloadProvider = null, object deferAgent = null, GLTFast.Materials.IMaterialGenerator materialGenerator = null, GLTFast.Logging.ICodeLogger logger = null) { }
        public Task<bool> Load(byte[] data, System.Uri uri = null, ImportSettings importSettings = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> InstantiateMainSceneAsync(Transform parent, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Texture2D GetTexture(int index) => null;
        public void Dispose() { }
    }
}
