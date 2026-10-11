// Isolated surface evidence. Coordinator copies into Editor; no asset mutation.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
namespace CTLife.EditorTools
{
    public static class VincentIdleProbe
    {
        public static void CaptureShared()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/character-candidates/vincent-idle/native-shared"));
            Directory.CreateDirectory(output);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Resources/Characters/AvatarController.controller");
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-ctVincentIdleController");
            if (at >= 0)
            {
                controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(args[at + 1]);
                output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/character-candidates/vincent-idle/native-candidate"));
                Directory.CreateDirectory(output);
            }
            if (controller == null) throw new InvalidDataException("Missing explicit/shared idle controller.");
            foreach (var tuple in new[] { ("creator", VincentCharacterImport.PrefabPath), ("world", VincentCharacterImport.WorldPrefabPath) })
                Capture(output, tuple.Item1, tuple.Item2, controller);
            Debug.Log("CTLIFE [vincent-idle-probe] Actual same-phase surfaces captured; no intersection/pose approval inferred from lateral proxy.");
        }
        private static void Capture(string output, string label, string path, RuntimeAnimatorController controller)
        {
            var scene = EditorSceneManager.NewPreviewScene(); GameObject root = null; var mesh = new Mesh();
            try
            {
                root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path)); SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = root.GetComponentsInChildren<Animator>(true).Single();
                if (animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid) throw new InvalidDataException("Vincent own Avatar invalid.");
                animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion = false; animator.enabled = true; animator.Rebind();
                var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var records = new List<object>();
                foreach (var skin in skins)
                {
                    for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++) skin.SetBlendShapeWeight(i, 0);
                    var weights = skin.sharedMesh.boneWeights; var names = skin.bones.Select(b => b.name).ToArray();
                    int[] Select(Func<string,bool> wanted) => Enumerable.Range(0, weights.Length).Where(i =>
                    {
                        var w = weights[i]; float W(int n,float a) => a > 0 && wanted(names[n]) ? a : 0;
                        return W(w.boneIndex0,w.weight0)+W(w.boneIndex1,w.weight1)+W(w.boneIndex2,w.weight2)+W(w.boneIndex3,w.weight3) > .5f;
                    }).ToArray();
                    bool Hand(string n,string side) => n == "Hand."+side || n.EndsWith("."+side,StringComparison.Ordinal) && new[]{"Thumb","Index","Middle","Ring","Pinky"}.Any(d=>n.StartsWith(d,StringComparison.Ordinal));
                    var left = Select(n=>Hand(n,"L")); var right = Select(n=>Hand(n,"R")); var thigh = Select(n=>n=="Hips" || n.StartsWith("Thigh.",StringComparison.Ordinal));
                    File.WriteAllText(Path.Combine(output,label+"-"+skin.name+"-regions.json"),JsonConvert.SerializeObject(new {leftHand=left,rightHand=right,thigh,mesh=skin.sharedMesh.name,vertices=skin.sharedMesh.vertexCount}));
                }
                Vector3 Bone(HumanBodyBones b) => root.transform.InverseTransformPoint(animator.GetBoneTransform(b).position);
                for (int f=0; f<=12; f++)
                {
                    float phase=f/12f; animator.Play("loco_idle",0,phase); animator.Update(0f);
                    var bones = Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>().Where(b=>b!=HumanBodyBones.LastBone && animator.GetBoneTransform(b)!=null).ToDictionary(b=>b.ToString(),b=>V(Bone(b)));
                    foreach (var skin in skins)
                    {
                        skin.BakeMesh(mesh); var matrix=root.transform.worldToLocalMatrix*skin.transform.localToWorldMatrix;
                        var vertices=mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                        records.Add(new {phase,skin=skin.name,minimumY=vertices.Min(v=>v.y),maximumY=vertices.Max(v=>v.y),bones});
                        WriteObj(Path.Combine(output,label+"-"+skin.name+"-phase"+f.ToString("D2")+".obj.gz"),vertices,mesh.normals.Select(n=>matrix.MultiplyVector(n).normalized).ToArray(),skin.sharedMesh.uv,skin.sharedMesh.triangles);
                    }
                }
                File.WriteAllText(Path.Combine(output,label+"-poses.json"),JsonConvert.SerializeObject(new {prefab=path,controller=AssetDatabase.GetAssetPath(controller),avatar=AssetDatabase.GetAssetPath(animator.avatar),frames=records},Formatting.Indented));
            }
            finally { Object.DestroyImmediate(mesh); if(root!=null)Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        }
        private static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
        private static void WriteObj(string path,Vector3[] vertices,Vector3[] normals,Vector2[] uv,int[] triangles)
        {
            using var file=File.Create(path); using var zip=new GZipStream(file,System.IO.Compression.CompressionLevel.Fastest); using var writer=new StreamWriter(zip);
            string N(float n)=>n.ToString("R",CultureInfo.InvariantCulture);
            foreach(var v in vertices)writer.WriteLine($"v {N(v.x)} {N(v.y)} {N(v.z)}");
            foreach(var t in uv)writer.WriteLine($"vt {N(t.x)} {N(t.y)}");
            foreach(var n in normals)writer.WriteLine($"vn {N(n.x)} {N(n.y)} {N(n.z)}");
            for(int i=0;i<triangles.Length;i+=3){int a=triangles[i]+1,b=triangles[i+1]+1,c=triangles[i+2]+1;writer.WriteLine($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}");}
        }
    }
}
