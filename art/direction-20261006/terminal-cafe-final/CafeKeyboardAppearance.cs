// Candidate-only shared appearance: original licensed material groups/normals, fixed contact surface.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace CTLife.EditorTools
{
    public static class CafeKeyboardAppearance
    {
        public const string TextureSha256 = "3c4e88042999f6e7fb987fec6dbc3f3ce4d7d45040b482adb5c740a91d14cda4";
        public static string ArtRoot => Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/direction-20261006"));
        public static string TexturePath => Path.Combine(ArtRoot,"terminal-cafe-final/keyboard-detail/keyboard-keys.png");
        public static readonly string[] MaterialNames = { "metalDark", "metalMedium" };
        public static string[] SourcePaths => new[] {
            Path.Combine(ArtRoot,"terminal-cafe-b3-fit/keyboard/computerKeyboard.obj"),
            Path.Combine(ArtRoot,"terminal-cafe-b3-fit/keyboard/computerKeyboard.mtl"), TexturePath };

        public static void RequireSources()
        {
            string[] expected = { "e33668db75a2cb9bc6ae64e1f062810d570aebb12f8ac58e13a63074b4cb652e", "e445dc0b0ed0651fae2afb35a0581a9dd259bfff0a1c68f0edfcf11ba80e02ed", TextureSha256 };
            var paths=SourcePaths;
            for(int i=0;i<paths.Length;i++)
            {
                if(!File.Exists(paths[i]))throw new FileNotFoundException("Frozen keyboard appearance source missing.",paths[i]);
                using var sha=SHA256.Create();using var file=File.OpenRead(paths[i]);
                if(BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant()!=expected[i])
                    throw new InvalidDataException("Frozen keyboard appearance source changed: "+paths[i]);
            }
        }

        public static Mesh CreateMesh()
        {
            RequireSources();
            var positions=new List<Vector3>();var sourceNormals=new List<Vector3>();var sourceUv=new List<Vector2>();
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var originalUv=new List<Vector2>();
            var indices=new[]{new List<int>(),new List<int>()};var lookup=new Dictionary<string,int>();int material=-1;
            const float sx=.46f/.2822f,sy=.023f/.027556f,sz=.15f/.118192f;
            float F(string value)=>float.Parse(value,CultureInfo.InvariantCulture);
            foreach(string line in File.ReadLines(SourcePaths[0]))
            {
                var t=line.Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);if(t.Length==0)continue;
                switch(t[0])
                {
                    case "v": positions.Add(new Vector3(F(t[1]),F(t[2]),F(t[3])));break;
                    case "vn": sourceNormals.Add(new Vector3(F(t[1]),F(t[2]),F(t[3])));break;
                    case "vt": sourceUv.Add(new Vector2(F(t[1]),F(t[2])));break;
                    case "usemtl": material=Array.IndexOf(MaterialNames,t[1]);if(material<0)throw new InvalidDataException("Unknown source keyboard material.");break;
                    case "f":
                        if(t.Length!=4||material<0)throw new InvalidDataException("Expected original triangulated keyboard source.");
                        // X reflection reverses winding. Keep original face multiplicity, including source duplicates.
                        foreach(int corner in new[]{1,3,2})
                        {
                            string key=material+":"+t[corner];
                            if(!lookup.TryGetValue(key,out int index))
                            {
                                var parts=t[corner].Split('/');if(parts.Length!=3)throw new InvalidDataException("Keyboard source normal/UV missing.");
                                var p=positions[int.Parse(parts[0],CultureInfo.InvariantCulture)-1];
                                var n=sourceNormals[int.Parse(parts[2],CultureInfo.InvariantCulture)-1];
                                var oldUv=sourceUv[int.Parse(parts[1],CultureInfo.InvariantCulture)-1];
                                p=new Vector3(-p.x*sx-.23f,p.y*sy+.74f,p.z*sz+.205f);
                                index=vertices.Count;lookup.Add(key,index);vertices.Add(p);
                                normals.Add(new Vector3(-n.x/sx,n.y/sy,n.z/sz).normalized);
                                originalUv.Add(oldUv);uv.Add(material==1?new Vector2((p.x+.23f)/.46f,(p.z-.205f)/.15f):oldUv);
                            }
                            indices[material].Add(index);
                        }
                        break;
                }
            }
            if(indices[0].Count!=56*3||indices[1].Count!=8*3)throw new InvalidDataException("Original keyboard material triangle counts changed.");
            var mesh=new Mesh { name="Kenney keyboard — original groups and normals, fixed contact surface" };
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);
            mesh.SetUVs(2,originalUv); // Preserve source UVs separately; UV2 remains available for baked lightmapping.
            mesh.subMeshCount=2;for(int i=0;i<2;i++)mesh.SetTriangles(indices[i],i);
            mesh.RecalculateBounds();return mesh;
        }

        public static Texture2D CreatePreviewTexture()
        {
            RequireSources();var texture=new Texture2D(2,2,TextureFormat.RGBA32,true,false){name="Original keyboard key legends",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear,anisoLevel=4};
            if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(TexturePath)))
            { UnityEngine.Object.DestroyImmediate(texture);throw new InvalidDataException("Keyboard key detail could not be decoded."); }
            return texture;
        }

        public static Material[] CreateMaterials(Texture2D texture)
        {
            if(texture==null)throw new ArgumentNullException(nameof(texture));
            var shader=Shader.Find("Universal Render Pipeline/Simple Lit");if(shader==null)throw new InvalidDataException("Expected project Simple Lit shader.");
            var casing=new Material(shader){name="Keyboard metalDark",enableInstancing=true};
            casing.SetColor("_BaseColor",new Color(.3058824f,.3882353f,.3882353f));casing.SetFloat("_Smoothness",.08f);
            var plate=new Material(shader){name="Keyboard metalMedium key detail",enableInstancing=true};
            plate.SetColor("_BaseColor",Color.white);plate.SetTexture("_BaseMap",texture);plate.SetFloat("_Smoothness",.08f);
            return new[]{casing,plate};
        }
    }
}
