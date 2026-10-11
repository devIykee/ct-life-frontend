using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    public static class CafeFurnitureOrientationReview
    {
        public static void Capture()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
                throw new InvalidOperationException("Read-only orientation diagnostic requires idle Edit mode.");
            var manifest=JObject.Parse(File.ReadAllText("Assets/Art/environment/manifest.json"));
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/direction-20261006/terminal-cafe-b2/seating-amendment/native-orientation.json"));
            var receipt=new JObject { ["status"]="running",["scope"]="Both native imported yaw variants recorded before any orientation decision; no scene or asset save." };
            var rows=new JArray();receipt["cases"]=rows;
            try
            {
                foreach(string id in new[]{"office_chair","home_chair_1"})
                {
                    string source="Assets/Art/"+(string)manifest[id]["fbx"];
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(source);
                    var importer=(ModelImporter)AssetImporter.GetAtPath(source);
                    if(prefab==null)throw new InvalidDataException("Missing native imported chair "+id);
                    foreach(float yaw in new[]{0f,180f})
                    {
                        var scene=EditorSceneManager.NewPreviewScene();GameObject item=null;
                        try
                        {
                            item=Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(item,scene);
                            item.transform.SetPositionAndRotation(Vector3.zero,Quaternion.Euler(0,yaw,0));
                            item.transform.localScale=Vector3.one*(id=="home_chair_1"?1.08f:1f);
                            var filters=item.GetComponentsInChildren<MeshFilter>(true).Where(f=>!f.name.Contains("_LOD")||f.name.Contains("_LOD0")).ToArray();
                            var points=filters.SelectMany(f=>f.sharedMesh.vertices.Select(f.transform.TransformPoint)).ToArray();
                            float low=points.Min(v=>v.y),high=points.Max(v=>v.y);
                            var back=points.Where(v=>v.y>low+(high-low)*.8f).ToArray();
                            var row=new JObject { ["asset"]=id,["source"]=source,["yaw_degrees"]=yaw,
                                ["scale"]=item.transform.localScale.x,["bake_axis_conversion"]=importer.bakeAxisConversion,
                                ["native_high_back_mean_z_m"]=back.Average(v=>v.z),["native_high_back_min_z_m"]=back.Min(v=>v.z),
                                ["native_high_back_max_z_m"]=back.Max(v=>v.z),["native_height_m"]=high-low,
                                ["native_world_bounds"]=new JArray(new JArray(points.Min(v=>v.x),low,points.Min(v=>v.z)),new JArray(points.Max(v=>v.x),high,points.Max(v=>v.z))),
                                ["mesh_transform_rotations"]=new JArray(filters.Select(f=>new JObject { ["mesh"]=f.name,["world_euler"]=new JArray(f.transform.eulerAngles.x,f.transform.eulerAngles.y,f.transform.eulerAngles.z) })) };
                            rows.Add(row);Debug.Log("CTLIFE [cafe-furniture-orientation] "+row.ToString(Newtonsoft.Json.Formatting.None));
                        }
                        finally { if(item!=null)Object.DestroyImmediate(item);EditorSceneManager.ClosePreviewScene(scene); }
                    }
                }
                receipt["status"]="all_four_native_variants_recorded_no_pose_acceptance";
            }
            catch(Exception error) { receipt["status"]="failed";receipt["error"]=error.ToString();throw; }
            finally { File.WriteAllText(output,receipt.ToString()); }
        }
    }
}
