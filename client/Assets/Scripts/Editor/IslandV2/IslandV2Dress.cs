using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CTLife.EditorTools.IslandV2
{
    /// <summary>
    /// Island dressing (World lane): reads tools/world/islandgen/out/dress.json (dress.py), builds the kit for the
    /// models it uses, and hands every instance, building box and trunk capsule to its streaming cell. Cells then
    /// write a CTI1 instance file (<see cref="IslandInstances"/>), colliders and markers. Parked decor cars are chosen
    /// here too, in a share of the street bays, so those bays stop being vehicle spawns.
    /// </summary>
    public static class IslandV2Dress
    {
        public static IslandKit Kit;
        public static int[] CarProtos = Array.Empty<int>();
        /// <summary>Share of street bays with a decor car, by district (downtown busier than the hills).</summary>
        public static float ParkedShare(Vector3 p)
        {
            float r = new Vector2(p.x, p.z).magnitude;
            return r < 260f ? 0.4f : 0.22f;
        }

        public sealed class Inst { public int Proto; public float X, Y, Z, Yaw, S, Sy; public uint Tint; }

        public static void Place(CellSet cells, IslandHeightfield h, JObject receipt)
        {
            var path = Path.Combine(IslandV2Paths.Out, "dress.json");
            if (!File.Exists(path)) { Debug.LogWarning("CTLIFE [islandv2] no dress.json: the island stays bare"); Kit = null; return; }
            var doc = JObject.Parse(File.ReadAllText(path));
            var groups = (JArray)doc["instances"];
            var used = new HashSet<string>(groups.Select(g => (string)g["proto"]));
            foreach (var c in CarModels) used.Add(c);
            Kit = IslandV2Kit.Build(used, receipt);
            CarProtos = CarModels.Select(c => Kit.IndexOf(c)).Where(i => i >= 0).ToArray();
            int n = 0, trunks = 0;
            foreach (var g in groups)
            {
                int proto = Kit.IndexOf((string)g["proto"]);
                if (proto < 0) continue;
                var p = Kit.protos[proto];
                var d = ((JArray)g["d"]).Select(v => (float)v).ToArray();
                for (int i = 0; i + 9 < d.Length + 0; i += 10)
                {
                    var inst = new Inst { Proto = proto, X = d[i], Y = d[i + 1], Z = d[i + 2], Yaw = d[i + 3], S = d[i + 4], Sy = d[i + 5], Tint = Pack(d[i + 6], d[i + 7], d[i + 8], d[i + 9]) };
                    var cell = cells.At(new Vector3(inst.X, 0f, inst.Z));
                    cell.Instances.Add(inst);
                    n++;
                    if (p.kind == "tree")
                    {
                        float r = Mathf.Clamp(p.bounds.size.x * inst.S * 0.06f, 0.18f, 0.55f);
                        float ht = Mathf.Min(p.bounds.size.y * inst.S * inst.Sy * 0.45f, 4.5f);
                        cell.Capsules.Add(new CellGeo.CapsuleCol { Center = new Vector3(inst.X, inst.Y + ht / 2f, inst.Z), Radius = r, Height = ht, Layer = Layers.Vegetation });
                        trunks++;
                    }
                    else if (p.kind == "lamp" || p.kind == "pole")
                    {
                        cell.Capsules.Add(new CellGeo.CapsuleCol { Center = new Vector3(inst.X, inst.Y + 2f, inst.Z), Radius = 0.16f, Height = 4f, Layer = Layers.Prop });
                    }
                }
            }
            int boxes = 0;
            foreach (var b in (JArray)doc["buildingBoxes"])
            {
                var a = ((JArray)b).Select(v => (float)v).ToArray();
                var c = new Vector3(a[0], a[1], a[2]);
                cells.At(c).Boxes.Add(new CellGeo.BoxCol
                {
                    Center = c, Rotation = Quaternion.Euler(0f, a[6], 0f), Size = new Vector3(a[3] * 0.96f, a[4], a[5] * 0.96f), Layer = Layers.Building, Name = "building",
                });
                boxes++;
            }
            receipt["dressInstances"] = n; receipt["dressBuildings"] = boxes; receipt["dressTrunks"] = trunks;
            receipt["dressStats"] = doc["stats"];
        }

        /// <summary>Kenney car kit models used as parked decor (Feel's drivable cars are separate).</summary>
        public static readonly string[] CarModels = { "car_sedan", "car_sedan_sports", "car_hatchback_sports", "car_suv", "car_suv_luxury", "car_taxi", "car_van", "car_delivery", "car_police" };

        public static uint Pack(float r, float g, float b, float a) =>
            (uint)Mathf.RoundToInt(Mathf.Clamp01(r) * 255f) | ((uint)Mathf.RoundToInt(Mathf.Clamp01(g) * 255f) << 8) |
            ((uint)Mathf.RoundToInt(Mathf.Clamp01(b) * 255f) << 16) | ((uint)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f) << 24);

        /// <summary>The cell's CTI1 file (see <see cref="IslandInstances"/>).</summary>
        public static byte[] Encode(List<Inst> list)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            var groups = list.GroupBy(i => i.Proto).OrderBy(g => g.Key).ToList();
            w.Write(IslandInstances.Magic);
            w.Write(groups.Count);
            foreach (var g in groups)
            {
                w.Write(g.Key);
                w.Write(g.Count());
                foreach (var i in g) { w.Write(i.X); w.Write(i.Y); w.Write(i.Z); w.Write(i.Yaw); w.Write(i.S); w.Write(i.Sy); w.Write(i.Tint); }
            }
            w.Flush();
            return ms.ToArray();
        }

        /// <summary>Stable 0..1 hash of a bay id (the same bays get cars on every rebuild).</summary>
        public static float Hash01(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s) { h ^= c; h *= 16777619; }
                return (h & 0xffffff) / (float)0x1000000;
            }
        }
    }
}
