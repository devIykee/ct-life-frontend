using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>Physical surface comparison: UV2 seam splits/index order are not geometry changes.</summary>
    public static class CafeFurnitureGeometry
    {
        public static JObject RequireExactSurface(GameObject item, Vector3 origin,
            (Vector3[] vertices, int[] triangles) target, string label, string diagnosticFolder)
        {
            const float tolerance = .00002f; // Same 20 micrometre coordinate tolerance as the original vertex gate.
            var filters = item.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => !f.name.Contains("_LOD") || f.name.Contains("_LOD0")).ToArray();
            if (filters.Length != 1) throw new InvalidDataException("Expected one actual LOD0 furniture mesh: " + label);
            var filter = filters[0];
            var actual = (vertices: filter.sharedMesh.vertices.Select(v => filter.transform.TransformPoint(v) - origin).ToArray(),
                triangles: filter.sharedMesh.triangles);
            Directory.CreateDirectory(diagnosticFolder);
            string file = string.Concat(label.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
            WriteObj(Path.Combine(diagnosticFolder, file + "-actual.obj.gz"), actual);
            WriteObj(Path.Combine(diagnosticFolder, file + "-target.obj.gz"), target);
            bool finite = actual.vertices.Concat(target.vertices).All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z));
            var unmatched = new List<int>();
            var used = new bool[target.triangles.Length / 3];
            float maxError = 0;
            var buckets = new Dictionary<(long x, long y, long z), List<int>>();
            (long x,long y,long z) Cell(Vector3 p) => ((long)Math.Floor(p.x/tolerance),(long)Math.Floor(p.y/tolerance),(long)Math.Floor(p.z/tolerance));
            Vector3 Centroid((Vector3[] vertices,int[] triangles) mesh,int tri) =>
                (mesh.vertices[mesh.triangles[tri*3]]+mesh.vertices[mesh.triangles[tri*3+1]]+mesh.vertices[mesh.triangles[tri*3+2]])/3f;
            bool validIndices = actual.triangles.All(i=>i>=0&&i<actual.vertices.Length) && target.triangles.All(i=>i>=0&&i<target.vertices.Length);
            if (finite && validIndices && actual.triangles.Length%3==0 && target.triangles.Length%3==0)
            {
                for(int t=0;t<used.Length;t++)
                {
                    var key=Cell(Centroid(target,t));
                    if(!buckets.TryGetValue(key,out var list))buckets[key]=list=new List<int>();
                    list.Add(t);
                }
                for(int a=0;a<actual.triangles.Length/3;a++)
                {
                    var key=Cell(Centroid(actual,a));int best=-1;float bestError=float.PositiveInfinity;
                    for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)for(int dz=-1;dz<=1;dz++)
                    {
                        if(!buckets.TryGetValue((key.x+dx,key.y+dy,key.z+dz),out var list))continue;
                        foreach(int t in list)
                        {
                            if(used[t])continue;
                            // Cyclic rotations preserve triangle winding. Reversals are deliberately rejected.
                            for(int rotation=0;rotation<3;rotation++)
                            {
                                float error=0;
                                for(int corner=0;corner<3;corner++)
                                    error=Mathf.Max(error,Vector3.Distance(actual.vertices[actual.triangles[a*3+corner]],target.vertices[target.triangles[t*3+(corner+rotation)%3]]));
                                if(error<=tolerance&&error<bestError){best=t;bestError=error;}
                            }
                        }
                    }
                    if(best<0)unmatched.Add(a);
                    else{used[best]=true;maxError=Mathf.Max(maxError,bestError);}
                }
            }
            bool passed=finite&&validIndices&&target.triangles.Length>0&&actual.vertices.Length>0&&actual.triangles.Length==target.triangles.Length&&actual.triangles.Length%3==0&&unmatched.Count==0&&used.All(x=>x);
            var result = new JObject {
                ["status"]=passed?"oriented_triangle_surface_matches":"surface_mismatch",
                ["label"]=label,["source_mesh_asset"]=AssetDatabase.GetAssetPath(filter.sharedMesh),["source_mesh_name"]=filter.sharedMesh.name,
                ["actual_vertices"]=actual.vertices.Length,["target_vertices"]=target.vertices.Length,
                ["actual_unique_positions"]=actual.vertices.Distinct().Count(),["target_unique_positions"]=target.vertices.Distinct().Count(),
                ["actual_triangles"]=actual.triangles.Length/3,["target_triangles"]=target.triangles.Length/3,
                ["index_arrays_equal"]=actual.triangles.SequenceEqual(target.triangles),["finite_and_valid_indices"]=finite&&validIndices,
                ["origin_world_m"]=V(origin),["item_world_position_m"]=V(item.transform.position),["item_world_euler_degrees"]=V(item.transform.eulerAngles),["item_world_scale"]=V(item.transform.lossyScale),
                ["actual_bounds"]=Bounds(actual.vertices),["target_bounds"]=Bounds(target.vertices),
                ["matched_triangle_corner_max_error_m"]=maxError,["tolerance_m"]=tolerance,
                ["unmatched_actual_triangles"]=new JArray(unmatched),["unmatched_target_triangles"]=new JArray(Enumerable.Range(0,used.Length).Where(i=>!used[i])),
                ["method"]="One-to-one oriented triangle corner match; cyclic vertex order allowed, reversed winding rejected. Equal triangle multiplicity and every corner within20micrometres are mandatory. UV/normal seams may duplicate or reorder vertices. No bounds-only acceptance." };
            File.WriteAllText(Path.Combine(diagnosticFolder,file+".json"),result.ToString());
            if(!passed)throw new InvalidDataException("Native furniture surface differs from frozen target: "+label+"; actual/target vertices="+actual.vertices.Length+"/"+target.vertices.Length+", triangles="+(actual.triangles.Length/3)+"/"+(target.triangles.Length/3)+", unmatched="+unmatched.Count+"/"+used.Count(x=>!x)+". See "+diagnosticFolder);
            return result;
        }
        private static JObject Bounds(Vector3[] vertices)
        {
            if(vertices.Length==0)return new JObject();var b=new UnityEngine.Bounds(vertices[0],Vector3.zero);foreach(var v in vertices.Skip(1))b.Encapsulate(v);
            return new JObject{["min"]=V(b.min),["max"]=V(b.max)};
        }
        private static JArray V(Vector3 v)=>new JArray(v.x,v.y,v.z);
        private static void WriteObj(string path,(Vector3[] vertices,int[] triangles) mesh)
        {
            using var file=File.Create(path);using var zip=new GZipStream(file,System.IO.Compression.CompressionLevel.Optimal);using var writer=new StreamWriter(zip);
            foreach(var p in mesh.vertices)writer.WriteLine(string.Format(CultureInfo.InvariantCulture,"v {0:R} {1:R} {2:R}",p.x,p.y,p.z));
            for(int i=0;i+2<mesh.triangles.Length;i+=3)writer.WriteLine($"f {mesh.triangles[i]+1} {mesh.triangles[i+1]+1} {mesh.triangles[i+2]+1}");
        }
    }
}
