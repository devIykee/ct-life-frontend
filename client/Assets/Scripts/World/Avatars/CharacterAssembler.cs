using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CTLife.World
{
    /// <summary>
    /// Merges modular skinned parts (body, head, hair, top, bottom, shoes, accessory — all skinned to the one shared
    /// skeleton) into ONE SkinnedMeshRenderer with ONE material (Build Spec "Character spec": two skinned meshes
    /// "could roughly double the rendering time"). Bones are remapped by name onto the base skeleton. If the parts
    /// use different textures they are packed into a runtime atlas (textures must be imported Read/Write);
    /// the art pipeline's shared palette atlas makes that step a no-op.
    /// </summary>
    public static class CharacterAssembler
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        public static SkinnedMeshRenderer Merge(Transform skeletonRoot, IList<SkinnedMeshRenderer> parts, Material materialOverride = null, int atlasMaxSize = 1024)
        {
            var boneByName = new Dictionary<string, Transform>();
            foreach (var t in skeletonRoot.GetComponentsInChildren<Transform>(true))
                if (!boneByName.ContainsKey(t.name)) boneByName[t.name] = t;

            var bones = new List<Transform>();
            var boneIndex = new Dictionary<Transform, int>();
            var bindposes = new List<Matrix4x4>();

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uvs = new List<Vector2>();
            var weights = new List<BoneWeight>();
            var tris = new List<int>();
            var partRanges = new List<(int start, int count, Texture2D tex)>();
            var textures = new List<Texture2D>();
            Material firstMaterial = null;

            foreach (var smr in parts)
            {
                if (smr == null || smr.sharedMesh == null) continue;
                var mesh = smr.sharedMesh;
                var partBones = smr.bones;
                var partBind = mesh.bindposes;
                var remap = new int[partBones.Length];
                for (int i = 0; i < partBones.Length; i++)
                {
                    Transform target = null;
                    if (partBones[i] != null) boneByName.TryGetValue(partBones[i].name, out target);
                    if (target == null)
                    {
                        Log.Warn("avatar", $"Part {smr.name}: bone '{partBones[i]?.name}' not on base skeleton; using root");
                        target = skeletonRoot;
                    }
                    if (!boneIndex.TryGetValue(target, out var g))
                    {
                        g = bones.Count;
                        bones.Add(target);
                        boneIndex[target] = g;
                        bindposes.Add(i < partBind.Length ? partBind[i] : target.worldToLocalMatrix * skeletonRoot.localToWorldMatrix);
                    }
                    remap[i] = g;
                }

                int baseVertex = verts.Count;
                verts.AddRange(mesh.vertices);
                var n = mesh.normals;
                normals.AddRange(n.Length == mesh.vertexCount ? n : new Vector3[mesh.vertexCount]);
                var tg = mesh.tangents;
                tangents.AddRange(tg.Length == mesh.vertexCount ? tg : new Vector4[mesh.vertexCount]);
                var uv = mesh.uv;
                uvs.AddRange(uv.Length == mesh.vertexCount ? uv : new Vector2[mesh.vertexCount]);
                foreach (var bw in mesh.boneWeights)
                {
                    weights.Add(new BoneWeight
                    {
                        boneIndex0 = remap[bw.boneIndex0], weight0 = bw.weight0,
                        boneIndex1 = remap[bw.boneIndex1], weight1 = bw.weight1,
                        boneIndex2 = remap[bw.boneIndex2], weight2 = bw.weight2,
                        boneIndex3 = remap[bw.boneIndex3], weight3 = bw.weight3,
                    });
                }
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    foreach (var idx in mesh.GetTriangles(sub)) tris.Add(idx + baseVertex);

                var mat = smr.sharedMaterial;
                if (firstMaterial == null) firstMaterial = mat;
                Texture2D tex = null;
                if (mat != null)
                    tex = (mat.HasProperty(BaseMapId) ? mat.GetTexture(BaseMapId) : mat.mainTexture) as Texture2D;
                partRanges.Add((baseVertex, mesh.vertexCount, tex));
                if (tex != null && !textures.Contains(tex)) textures.Add(tex);
            }

            Material material = materialOverride != null ? materialOverride : firstMaterial;
            if (materialOverride == null && textures.Count > 1)
                material = Atlas(textures, partRanges, uvs, firstMaterial, atlasMaxSize) ?? firstMaterial;

            var merged = new Mesh { name = "avatar_merged" };
            if (verts.Count > 65535) merged.indexFormat = IndexFormat.UInt32;
            merged.SetVertices(verts);
            merged.SetNormals(normals);
            merged.SetTangents(tangents);
            merged.SetUVs(0, uvs);
            merged.boneWeights = weights.ToArray();
            merged.bindposes = bindposes.ToArray();
            merged.SetTriangles(tris, 0);
            CopyBlendShapes(parts, merged, verts.Count);
            merged.RecalculateBounds();

            var bodyGo = new GameObject("body");
            bodyGo.transform.SetParent(skeletonRoot, false);
            var body = bodyGo.AddComponent<SkinnedMeshRenderer>();
            body.sharedMesh = merged;
            body.bones = bones.ToArray();
            body.rootBone = boneByName.TryGetValue("pelvis", out var pelvis) ? pelvis : skeletonRoot;
            body.sharedMaterial = material;
            body.quality = SkinQuality.Bone4;
            body.shadowCastingMode = ShadowCastingMode.Off;
            body.receiveShadows = false;
            body.updateWhenOffscreen = false;
            return body;
        }

        private static Material Atlas(List<Texture2D> textures, List<(int start, int count, Texture2D tex)> ranges, List<Vector2> uvs, Material template, int maxSize)
        {
            foreach (var t in textures)
            {
                if (!t.isReadable)
                {
                    Log.Warn("avatar", $"Texture {t.name} is not Read/Write; cannot atlas. Use the shared palette atlas.");
                    return null;
                }
            }
            var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "avatar_atlas" };
            var rects = atlas.PackTextures(textures.ToArray(), 2, maxSize, false);
            foreach (var r in ranges)
            {
                if (r.tex == null) continue;
                var rect = rects[textures.IndexOf(r.tex)];
                for (int i = r.start; i < r.start + r.count; i++)
                {
                    var u = uvs[i];
                    uvs[i] = new Vector2(rect.x + Mathf.Repeat(u.x, 1f) * rect.width, rect.y + Mathf.Repeat(u.y, 1f) * rect.height);
                }
            }
            atlas.Apply(true, true);
            var m = template != null ? new Material(template) : Mats.Textured(atlas);
            if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, atlas);
            m.mainTexture = atlas;
            return m;
        }

        private static void CopyBlendShapes(IList<SkinnedMeshRenderer> parts, Mesh merged, int totalVerts)
        {
            int offset = 0;
            foreach (var smr in parts)
            {
                if (smr == null || smr.sharedMesh == null) continue;
                var mesh = smr.sharedMesh;
                for (int s = 0; s < mesh.blendShapeCount; s++)
                {
                    var name = mesh.GetBlendShapeName(s);
                    if (merged.GetBlendShapeIndex(name) >= 0) continue;
                    int frames = mesh.GetBlendShapeFrameCount(s);
                    for (int f = 0; f < frames; f++)
                    {
                        var dv = new Vector3[mesh.vertexCount];
                        var dn = new Vector3[mesh.vertexCount];
                        var dt = new Vector3[mesh.vertexCount];
                        mesh.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                        var fv = new Vector3[totalVerts];
                        var fn = new Vector3[totalVerts];
                        var ft = new Vector3[totalVerts];
                        System.Array.Copy(dv, 0, fv, offset, dv.Length);
                        System.Array.Copy(dn, 0, fn, offset, dn.Length);
                        System.Array.Copy(dt, 0, ft, offset, dt.Length);
                        merged.AddBlendShapeFrame(name, mesh.GetBlendShapeFrameWeight(s, f), fv, fn, ft);
                    }
                }
                offset += mesh.vertexCount;
            }
        }
    }
}
