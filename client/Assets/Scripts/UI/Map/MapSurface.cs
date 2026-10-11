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
            public Rect AtlasRect;       // where uv (0,0)-(1,1) of the texture lands before RotationDeg, atlas metres (min = bottom-left)
            public float RotationDeg;    // degrees counter-clockwise about AtlasRect.center (MapMath's convention; a venue yaw is the negative)
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
                        var atlas = AtlasFromLayer(l, new Vector2(u, 1f - v));
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

        /// <summary>Atlas metres of a point of the layer's drawn rect (q in 0..1, y north), turned with it: the vertex math of Draw.</summary>
        public static Vector2 AtlasFromLayer(in Layer l, Vector2 q)
        {
            var p = l.AtlasRect.min + Vector2.Scale(q, l.AtlasRect.size);
            return l.RotationDeg != 0f ? l.AtlasRect.center + MapMath.Rotate(p - l.AtlasRect.center, l.RotationDeg * Mathf.Deg2Rad) : p;
        }

        /// <summary>Inverse of <see cref="AtlasFromLayer"/>: where an atlas point falls in the layer's rect (0..1 inside).</summary>
        public static Vector2 LayerFromAtlas(in Layer l, Vector2 atlas)
        {
            var p = l.RotationDeg != 0f ? l.AtlasRect.center + MapMath.Rotate(atlas - l.AtlasRect.center, -l.RotationDeg * Mathf.Deg2Rad) : atlas;
            return new Vector2((p.x - l.AtlasRect.xMin) / l.AtlasRect.width, (p.y - l.AtlasRect.yMin) / l.AtlasRect.height);
        }

        /// <summary>An area's baked texture as a layer on the island, turned with its venue.</summary>
        public static Layer AreaLayer(MapArea a, MapPlacement p, Texture2D texture, int grid) => new Layer
        {
            Texture = texture, AtlasRect = AreaAtlasRect(a, p), RotationDeg = AreaAtlasRotation(a, p), Alpha = 1f, Grid = grid,
        };

        /// <summary>
        /// The atlas rectangle an area texture covers before it turns: its baked size, centred where the area's centre lands on
        /// the island. For an unrotated venue this is the whole frame (min = AtlasFromLocal(Min)).
        /// </summary>
        public static Rect AreaAtlasRect(MapArea a, MapPlacement p)
        {
            var size = a.Size * p.Scale;
            return new Rect(MapMath.AtlasFromLocal(p, a.Center) - size / 2f, size);
        }

        /// <summary>
        /// How far the area texture turns on the island, degrees counter-clockwise about <see cref="AreaAtlasRect"/>'s centre: the
        /// placement (atlas.json v2 rotationDeg is the venue yaw, clockwise, so this is its negative) plus any bake rotation of
        /// the area itself. The same turn <see cref="MapMath.AtlasFromLocal"/> gives the markers, so they stay on their pixels.
        /// </summary>
        public static float AreaAtlasRotation(MapArea a, MapPlacement p) => p.RotationDeg + a.RotationDeg;

        /// <summary>The four atlas corners of the turned area quad (bottom-left, bottom-right, top-right, top-left of the texture).</summary>
        public static Vector2[] AreaCorners(MapArea a, MapPlacement p)
        {
            var l = AreaLayer(a, p, null, 1);
            return new[] { AtlasFromLayer(l, Vector2.zero), AtlasFromLayer(l, Vector2.right), AtlasFromLayer(l, Vector2.one), AtlasFromLayer(l, Vector2.up) };
        }

        /// <summary>Axis-aligned bounds of the turned area quad, for framing and coarse tests.</summary>
        public static Rect AreaAtlasBounds(MapArea a, MapPlacement p)
        {
            var c = AreaCorners(a, p);
            Vector2 min = c[0], max = c[0];
            for (int i = 1; i < 4; i++) { min = Vector2.Min(min, c[i]); max = Vector2.Max(max, c[i]); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>Whether an atlas point lies on the area's texture (the turned quad, not its bounds): taps and hit tests.</summary>
        public static bool AreaContains(MapArea a, MapPlacement p, Vector2 atlas)
        {
            var uv = MapMath.AreaUv(a, MapMath.LocalFromAtlas(p, atlas));
            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }

        /// <summary>The point of the turned area quad's edge straight north of its centre (where a venue's name pill rides).</summary>
        public static Vector2 AreaNorthEdge(MapArea a, MapPlacement p)
        {
            var r = AreaAtlasRect(a, p);
            var d = MapMath.Rotate(Vector2.up, -AreaAtlasRotation(a, p) * Mathf.Deg2Rad);   // north in the quad's own frame
            float tx = Mathf.Abs(d.x) > 1e-5f ? r.width / 2f / Mathf.Abs(d.x) : float.MaxValue;
            float ty = Mathf.Abs(d.y) > 1e-5f ? r.height / 2f / Mathf.Abs(d.y) : float.MaxValue;
            return r.center + new Vector2(0f, Mathf.Min(tx, ty));
        }
    }
}
