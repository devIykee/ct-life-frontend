using System;
using System.Collections.Generic;
using CTLife.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace CTLife.UI
{
    /// <summary>A small, matte, beveled plinth sharing the creator's real camera and studio light.</summary>
    internal sealed class AvatarPreviewPodium : IDisposable
    {
        internal const float Height = 0.24f;
        private readonly GameObject _root;
        private readonly Mesh _mesh;
        private readonly Material[] _materials;
        internal float Radius { get; private set; }
        internal Transform Transform => _root.transform;

        internal AvatarPreviewPodium(Transform parent, int layer)
        {
            _root = new GameObject("creator_podium") { layer = layer };
            _root.transform.SetParent(parent, false);
            _mesh = BuildMesh();
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            // Same established creator yellow and purple; no emission, metallic paint or specular rim.
            _materials = new[] { Material("gold", new Color32(255, 211, 92, 255)),
                Material("band", new Color32(146, 88, 220, 255)) };
            var renderer = _root.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = _materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static Material Material(string name, Color color)
        {
            var material = new Material(Mats.LitShader) { name = "creator_podium_" + name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else material.color = color;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.08f);
            if (material.HasProperty("_SpecColor")) material.SetColor("_SpecColor", Color.black);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            material.DisableKeyword("_EMISSION");
            return material;
        }

        internal void Place(Vector3 centre, float radius)
        {
            Radius = radius;
            _root.transform.localPosition = centre;
            _root.transform.localScale = new Vector3(radius, 1f, radius);
        }

        internal void SetVisible(bool visible) => _root.SetActive(visible);

        private static Mesh BuildMesh()
        {
            // Radius / height profile. The upper bevel catches the same light as the character.
            var profile = new[] { new Vector2(0f, 0f), new Vector2(.95f, 0f),
                new Vector2(1f, .025f), new Vector2(1f, .055f), new Vector2(.975f, .075f),
                new Vector2(.975f, .145f), new Vector2(1f, .165f), new Vector2(1f, .205f),
                new Vector2(.96f, Height), new Vector2(0f, Height) };
            const int segments = 64;
            var vertices = new List<Vector3>(profile.Length * (segments + 1));
            var normals = new List<Vector3>(vertices.Capacity);
            var gold = new List<int>(); var band = new List<int>();
            // Separate profile strips retain a flat top and deliberate bevel edges; angular normals remain smooth.
            for (int ring = 0; ring < profile.Length - 1; ring++)
            {
                var a = profile[ring]; var b = profile[ring + 1];
                Vector2 slope = (b - a).normalized;
                int start = vertices.Count;
                for (int segment = 0; segment <= segments; segment++)
                {
                    float angle = segment * Mathf.PI * 2f / segments;
                    var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    var normal = radial * slope.y - Vector3.up * slope.x;
                    vertices.Add(radial * a.x + Vector3.up * a.y);
                    vertices.Add(radial * b.x + Vector3.up * b.y);
                    normals.Add(normal); normals.Add(normal);
                }
                var triangles = ring >= 3 && ring <= 5 ? band : gold;
                for (int segment = 0; segment < segments; segment++)
                {
                    int at = start + segment * 2;
                    triangles.Add(at); triangles.Add(at + 1); triangles.Add(at + 2);
                    triangles.Add(at + 2); triangles.Add(at + 1); triangles.Add(at + 3);
                }
            }
            var mesh = new Mesh { name = "creator_podium", subMeshCount = 2 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals);
            mesh.SetTriangles(gold, 0); mesh.SetTriangles(band, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(_root);
            UnityEngine.Object.Destroy(_mesh);
            foreach (var material in _materials) UnityEngine.Object.Destroy(material);
        }
    }
}
