using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI.Map
{
    /// <summary>
    /// Draws map textures as UI Toolkit meshes through a projection (atlas metres to element pixels): a flat rotated quad for
    /// the minimap (texture plus a UV transform, no camera), a subdivided grid for the full map's perspective tilt. Nothing is
    /// rendered by a second camera; the parent clips (rounded square / viewport).
    /// </summary>
    public sealed class MapSurface : VisualElement
    {
        public struct Layer
        {
            public Texture2D Texture;
            public Rect AtlasRect;       // where uv (0,0)-(1,1) of the texture lands, atlas metres (min = bottom-left)
            public float Alpha;
            public int Grid;             // subdivisions per side (1 for affine views)
            public Rect Uv;              // texture sub-rect to draw (default 0..1)
        }

        private readonly List<Layer> _layers = new List<Layer>();
        public Func<Vector2, Vector2> Project;

        public MapSurface()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("map-surface");
            generateVisualContent += Draw;
        }

        public void SetLayers(IEnumerable<Layer> layers)
        {
            _layers.Clear();
            _layers.AddRange(layers);
            MarkDirtyRepaint();
        }

        public void Refresh() => MarkDirtyRepaint();

        private void Draw(MeshGenerationContext ctx)
        {
            if (Project == null) return;
            foreach (var l in _layers)
            {
                if (l.Texture == null || l.Alpha <= 0.001f) continue;
                int n = Mathf.Clamp(l.Grid, 1, 48);
                int vc = (n + 1) * (n + 1), ic = n * n * 6;
                var mesh = ctx.Allocate(vc, ic, l.Texture);
                var uv = l.Uv.width > 0 ? l.Uv : new Rect(0, 0, 1, 1);
                var tint = new Color(1, 1, 1, l.Alpha);
                for (int j = 0; j <= n; j++)
                    for (int i = 0; i <= n; i++)
                    {
                        float u = i / (float)n, v = j / (float)n;           // v = 0 at the top edge of the drawn rect (north)
                        var atlas = new Vector2(l.AtlasRect.xMin + u * l.AtlasRect.width, l.AtlasRect.yMax - v * l.AtlasRect.height);
                        var p = Project(atlas);
                        var tu = uv.xMin + u * uv.width;
                        var tv = uv.yMax - v * uv.height;                    // texture v grows up (north)
                        mesh.SetNextVertex(new Vertex
                        {
                            position = new Vector3(p.x, p.y, Vertex.nearZ),
                            uv = new Vector2(tu, tv), // the renderer remaps atlas regions itself (Unity 6)
                            tint = tint,
                        });
                    }
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        ushort a = (ushort)(j * (n + 1) + i), b = (ushort)(a + 1), d = (ushort)(a + n + 1), c = (ushort)(d + 1);
                        // Clockwise on screen (y down): top-left, top-right, bottom-right / top-left, bottom-right, bottom-left.
                        mesh.SetNextIndex(a); mesh.SetNextIndex(b); mesh.SetNextIndex(c);
                        mesh.SetNextIndex(a); mesh.SetNextIndex(c); mesh.SetNextIndex(d);
                    }
            }
        }

        /// <summary>The atlas rectangle an area texture covers (its baked frame placed on the island).</summary>
        public static Rect AreaAtlasRect(MapArea a, MapPlacement p)
        {
            // Rotation 0 placements (all current ones): an axis-aligned rect. Rotated placements would need a rotated quad.
            var min = MapMath.AtlasFromLocal(p, a.Min);
            return new Rect(min, a.Size * p.Scale);
        }
    }
}
