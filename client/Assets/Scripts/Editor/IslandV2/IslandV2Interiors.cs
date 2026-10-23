using System;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>
    /// World v3 walk-in rooms (2026-10-23): the baked room models from art/processed/world_v3/v3_interiors (kind
    /// 'interior_room', world_v3_interior.py) become Resources/World/Interiors/&lt;id&gt;.prefab (InteriorRoom: the baked mesh on
    /// the Interior layer, its collider boxes, the ceiling and floor slabs, the sit points), and dress.json's interiorDoors
    /// become Resources/World/Island/interior_doors.json for IslandInteriors.
    /// Room space: the entrance's floor centre is the origin, +Z points out to the street, the room lies at z &lt; 0. The
    /// manifest's coordinates are Blender's (x, y into the room, z up); the axis mapping is read off the imported mesh.
    /// Batch (rooms and doors only): -executeMethod CTLife.EditorTools.IslandV2.IslandV2Interiors.BuildBatch
    /// </summary>
    public static class IslandV2Interiors
    {
        public const string Dir = "Assets/World/Island/Interiors";
        public const string PrefabDir = "Assets/Resources/World/Interiors";
        public const string DoorsAsset = "Assets/Resources/World/Island/interior_doors.json";
        private static string Manifest => Path.Combine(IslandV2Paths.Repo, "art", "processed", "world_v3", "manifest.json");

        [MenuItem("CT Life/Island v2/Build walk-in interiors")]
        public static void Menu() => Build(new JObject());

        public static void BuildBatch()
        {
            int code = 0;
            var receipt = new JObject();
            try { Build(receipt); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            Debug.Log("CTLIFE [interiors] receipt " + receipt.ToString(Newtonsoft.Json.Formatting.None));
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        public static void Build(JObject receipt)
        {
            Directory.CreateDirectory(Dir); Directory.CreateDirectory(PrefabDir); Directory.CreateDirectory(Path.GetDirectoryName(DoorsAsset));
            var man = JObject.Parse(File.ReadAllText(Manifest));
            var dress = IslandV2Paths.Json("dress.json");
            var doors = dress["interiorDoors"] as JArray ?? new JArray();
            var rooms = doors.Select(d => (string)d["room"]).Where(r => !string.IsNullOrEmpty(r)).Distinct().ToList();
            var built = new JArray();
            foreach (var id in rooms)
            {
                if (!(man[id] is JObject item)) { Debug.LogWarning("CTLIFE [interiors] no manifest entry for room " + id); continue; }
                built.Add(BuildRoom(id, item));
            }
            File.WriteAllText(DoorsAsset, new JObject { ["doors"] = doors }.ToString(Newtonsoft.Json.Formatting.None));
            AssetDatabase.ImportAsset(DoorsAsset, ImportAssetOptions.ForceSynchronousImport);
            receipt["interiorDoors"] = doors.Count;
            receipt["interiorRooms"] = built;
            AssetDatabase.SaveAssets();
        }

        private static JObject BuildRoom(string id, JObject item)
        {
            string src = Path.Combine(IslandV2Paths.Repo, "art", "processed", (string)item["fbx"]);
            string fbx = $"{Dir}/{id}.fbx";
            File.Copy(src, fbx, true);
            string texRel = (string)((JArray)item["textures"])[0];          // the merged manifest: relative to art/processed
            string texSrc = texRel.Contains("/") ? Path.Combine(IslandV2Paths.Repo, "art", "processed", texRel)
                                                 : Path.Combine(Path.GetDirectoryName(src), "textures", texRel);
            string tex = $"{Dir}/{Path.GetFileName(texRel)}";
            File.Copy(texSrc, tex, true);
            AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(tex, ImportAssetOptions.ForceSynchronousImport);
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None; mi.importCameras = false; mi.importLights = false;
            mi.importBlendShapes = false; mi.isReadable = false;
            mi.importNormals = ModelImporterNormals.None;            // unlit, baked: positions and UVs only
            mi.importTangents = ModelImporterTangents.None;
            mi.SaveAndReimport();
            var ti = (TextureImporter)AssetImporter.GetAtPath(tex);
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaIsTransparency = false;
            ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.mipmapEnabled = true; ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 2; ti.wrapMode = TextureWrapMode.Clamp; ti.maxTextureSize = 4096;
            ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.5f;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true; android.maxTextureSize = 4096; android.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(android);
            ti.SaveAndReimport();

            var matPath = $"{Dir}/M_{id}_baked.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            var sh = Shader.Find("CT Life/World/Interior Baked") ?? throw new InvalidOperationException("Interior Baked shader missing");
            if (mat == null) { mat = new Material(sh) { name = $"M_{id}_baked" }; AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = sh;
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex));
            mat.SetFloat("_Exposure", 1.0f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);

            var mesh = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>().FirstOrDefault() ?? throw new InvalidOperationException("no mesh in " + fbx);
            // Blender -> mesh space: y = Blender z; x = +-Blender x and z = +-Blender y, whichever matches the imported bounds
            var jb = (JArray)item["bounds"];
            var bmin = new Vector3((float)jb[0][0], (float)jb[0][1], (float)jb[0][2]);
            var bmax = new Vector3((float)jb[1][0], (float)jb[1][1], (float)jb[1][2]);
            var mb = mesh.bounds;
            float sx = Mathf.Abs(mb.min.x - bmin.x) + Mathf.Abs(mb.max.x - bmax.x) <= Mathf.Abs(mb.min.x + bmax.x) + Mathf.Abs(mb.max.x + bmin.x) ? 1f : -1f;
            float sz = Mathf.Abs(mb.min.z - bmin.y) + Mathf.Abs(mb.max.z - bmax.y) <= Mathf.Abs(mb.min.z + bmax.y) + Mathf.Abs(mb.max.z + bmin.y) ? 1f : -1f;
            // the room must lie at -Z (into the building): turn it round when the import put it at +Z
            var R = sz > 0f ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            Vector3 ToRoom(float x, float y, float z) => R * new Vector3(sx * x, z, sz * y);

            var root = new GameObject(id) { layer = 17 };
            var room = root.AddComponent<InteriorRoom>();
            room.roomId = id;
            room.axisX = sz > 0f ? -sx : sx; room.axisZ = sz > 0f ? -sz : sz;
            var door = item["door"] as JObject;
            if (door != null) { room.doorWidth = (float)door["width"]; room.doorHeight = (float)door["height"]; }
            var model = new GameObject("model") { layer = 17 };
            model.transform.SetParent(root.transform, false);
            model.transform.localRotation = R;
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = model.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mr.allowOcclusionWhenDynamic = false;

            var a = ToRoom(bmin.x, bmin.y, bmin.z); var b = ToRoom(bmax.x, bmax.y, bmax.z);
            room.boundsMin = Vector3.Min(a, b); room.boundsMax = Vector3.Max(a, b);
            var cols = new GameObject("colliders") { layer = 17 };
            cols.transform.SetParent(root.transform, false);
            int walls = 0, furniture = 0;
            void Box(string name, Vector3 c, Vector3 size, int layer)
            {
                var g = new GameObject(name) { layer = layer };
                g.transform.SetParent(cols.transform, false);
                var bc = g.AddComponent<BoxCollider>(); bc.center = c; bc.size = size;
            }
            foreach (JArray box in (JArray)item["colliders"])
            {
                float cx = (float)box[0], cy = (float)box[1], cz = (float)box[2], wx = (float)box[3], wy = (float)box[4], h = (float)box[5];
                bool wall = h > 2.0f;
                // walls run up to the ceiling (they also keep the camera in); furniture stops bodies only (Prop layer)
                Box(wall ? "wall" : "furniture", ToRoom(cx, cy, wall ? 1.45f : cz), new Vector3(wx, wall ? 2.9f : h, wy), wall ? 17 : 13);
                if (wall) walls++; else furniture++;
            }
            var ctr = (room.boundsMin + room.boundsMax) * 0.5f;
            var ext = room.boundsMax - room.boundsMin;
            Box("floor", new Vector3(ctr.x, -0.2f, ctr.z), new Vector3(ext.x, 0.4f, ext.z), 17);
            Box("ceiling", new Vector3(ctr.x, 2.95f, ctr.z), new Vector3(ext.x, 0.3f, ext.z), 17);

            var seats = new System.Collections.Generic.List<InteriorRoom.Seat>();
            foreach (JObject s in (JArray)item["seats"] ?? new JArray())
            {
                var at = (JArray)s["at"];
                float yaw = (float)s["yaw"] * Mathf.Deg2Rad;
                var f = R * new Vector3(sx * Mathf.Sin(yaw), 0f, sz * Mathf.Cos(yaw));
                seats.Add(new InteriorRoom.Seat
                {
                    id = (string)s["id"], kind = (string)s["kind"], position = ToRoom((float)at[0], (float)at[1], 0f),
                    yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg,
                    approach = s["approach"] is JArray ap ? ToRoom((float)ap[0], (float)ap[1], 0f)
                                                          : ToRoom((float)at[0] + 0.55f * Mathf.Sin(yaw), (float)at[1] + 0.55f * Mathf.Cos(yaw), 0f),
                });
            }
            room.seats = seats.ToArray();

            var prefabPath = $"{PrefabDir}/{id}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log($"CTLIFE [interiors] {id}: {mesh.triangles.Length / 3} tris, {walls} wall + {furniture} furniture boxes, {seats.Count} seats, axes x{sx} z{sz}{(sz > 0 ? " turned" : "")}, bounds {room.boundsMin}..{room.boundsMax}");
            return new JObject { ["id"] = id, ["tris"] = mesh.triangles.Length / 3, ["walls"] = walls, ["furniture"] = furniture, ["seats"] = seats.Count };
        }
    }
}
