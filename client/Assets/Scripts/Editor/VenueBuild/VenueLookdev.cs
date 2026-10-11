using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTLife.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Look-dev pieces for graded venues: the global Volume profile and VenueLook component (build time), and the
    /// light-probe grid plus reflection probe that VenueBake bakes alongside the lightmaps.
    /// </summary>
    public static class VenueLookdev
    {
        /// <summary>Global Volume with the venue grade + VenueLook (camera clear colour, probes slot).</summary>
        public static void AddLook(VenueSpec spec, GameObject root)
        {
            string path = spec.Folder + "/" + spec.id + "_Look.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
            foreach (var c in profile.components.ToList())
            {
                profile.Remove(c.GetType());
                Object.DestroyImmediate(c, true);
            }
            var tone = Add<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.Neutral);
            var adj = Add<ColorAdjustments>(profile);
            adj.postExposure.Override(spec.postExposure);
            adj.contrast.Override(spec.contrast);
            adj.saturation.Override(spec.saturation);
            adj.colorFilter.Override(spec.colorFilter);
            var wb = Add<WhiteBalance>(profile);
            wb.temperature.Override(spec.temperature);
            wb.tint.Override(spec.tint);
            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(spec.bloomThreshold);
            bloom.intensity.Override(spec.bloomIntensity);
            bloom.scatter.Override(0.6f);
            bloom.highQualityFiltering.Override(false); // Build Spec: HQ filtering off on mobile
            var vig = Add<Vignette>(profile);
            vig.intensity.Override(spec.vignette);
            vig.smoothness.Override(0.45f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);

            var look = VB.Child("Look", root.transform).gameObject;
            var volume = look.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
            var vl = root.AddComponent<VenueLook>();
            vl.skyColor = spec.skyColor;
        }

        private static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        /// <summary>Probe grid over the NavMesh (knee and head height, 2.5 m spacing) and one baked reflection probe.</summary>
        public static (LightProbeGroup group, ReflectionProbe reflection) AddBakeProbes(GameObject root)
        {
            var venue = root.GetComponent<VenueSceneRoot>();
            var b = venue.cameraBounds;
            var positions = new List<Vector3>();
            NavMeshDataInstance nav = default;
            if (venue.navigation != null) nav = NavMesh.AddNavMeshData(venue.navigation);
            try
            {
                for (float x = b.xMin; x <= b.xMax + 0.01f; x += 2.5f)
                    for (float z = b.yMin; z <= b.yMax + 0.01f; z += 2.5f)
                    {
                        var p = new Vector3(x, 0, z);
                        float y = NavMesh.SamplePosition(p, out var hit, 1.2f, NavMesh.AllAreas) ? hit.position.y : 0f;
                        positions.Add(new Vector3(x, y + 0.5f, z));
                        positions.Add(new Vector3(x, y + 1.7f, z));
                    }
            }
            finally { if (nav.valid) NavMesh.RemoveNavMeshData(nav); }
            var go = new GameObject("Bake light probes");
            var group = go.AddComponent<LightProbeGroup>();
            group.probePositions = positions.ToArray();
            var rgo = new GameObject("Bake reflection probe");
            rgo.transform.position = new Vector3(b.center.x, 2f, b.center.y);
            var probe = rgo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(b.width + 8f, 8f, b.height + 8f);
            probe.resolution = 128;
            probe.hdr = false;
            probe.boxProjection = false;
            return (group, probe);
        }

        /// <summary>After the bake: save the probes as an asset on VenueLook and copy the reflection cubemap into a Custom probe.</summary>
        public static void BindBakedProbes(GameObject root, ReflectionProbe baked, string output, string id)
        {
            var look = root.GetComponent<VenueLook>();
            if (look == null) return;
            var source = LightmapSettings.lightProbes;
            if (source == null || source.count == 0) throw new InvalidDataException(id + ": light probes did not bake.");
            string probesPath = output + "/" + id + "_probes.asset";
            AssetDatabase.DeleteAsset(probesPath);
            var copy = Object.Instantiate(source);
            copy.name = id + "_probes";
            AssetDatabase.CreateAsset(copy, probesPath);
            look.lightProbes = AssetDatabase.LoadAssetAtPath<LightProbes>(probesPath);
            if (look.lightProbes == null) throw new InvalidDataException(id + ": light probes asset could not be saved.");
            var cube = baked != null ? baked.bakedTexture : null;
            if (cube != null)
            {
                var old = root.transform.Find("Reflections");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var r = VB.Child("Reflections", root.transform);
                r.position = baked.transform.position;
                var probe = r.gameObject.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Custom;
                probe.customBakedTexture = cube;
                probe.size = baked.size;
                probe.importance = 1;
                probe.boxProjection = false;
            }
            Debug.Log($"CTLIFE [venue-bake] {id}: {look.lightProbes.count} light probes saved; reflection cubemap {(cube != null ? AssetDatabase.GetAssetPath(cube) : "none")}.");
        }
    }
}
