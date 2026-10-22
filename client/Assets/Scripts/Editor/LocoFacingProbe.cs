using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Regression probe for locomotion facing (2026-10-22, after the mocap v3 clips played backwards in game): each clip
    /// is sampled in place on its family's character facing +Z. It reports the body's facing (hip line x up, from the
    /// Humanoid bones) and the planted foot's travel along that facing: a forward walk has the body facing +Z and the
    /// stance foot sliding backwards (negative), and a start's hips moving forward when root motion is on.
    /// Batch: -executeMethod CTLife.EditorTools.LocoFacingProbe.RunBatch [-ctlifeOut file.json]; fails on any reversed clip.
    /// </summary>
    public static class LocoFacingProbe
    {
        private static readonly (string family, string prefab)[] Bodies =
        {
            ("snow", "Assets/Art/characters/cast/prefabs/player_m_kol.prefab"),
            ("rain", "Assets/Art/characters/cast/prefabs/player_f_builder.prefab"),
            ("hazmat", "Assets/Art/characters/distinct/prefabs/hero_hazmat_Hero.prefab"),
        };

        private static readonly string[] Clips = { "loco_idle", "loco_walk", "loco_jog", "loco_sprint", "loco_walk_start", "loco_run_start", "loco_walk_stop", "loco_jog_stop", "turn_90_l", "turn_90_r", "turn_180_walk" };

        public static JObject Run()
        {
            var report = new JObject();
            var bad = new List<string>();
            foreach (var (family, prefabPath) in Bodies)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) { report[family] = "missing prefab " + prefabPath; continue; }
                var fam = new JObject();
                foreach (var id in Clips)
                {
                    var clip = MocapV3Import.Clip(MocapV3Import.PathOf(family, id));
                    if (clip == null) continue;
                    var r = Measure(prefab, clip);
                    fam[id] = r;
                    bool loop = id == "loco_walk" || id == "loco_jog" || id == "loco_sprint";
                    if (r["error"] != null) { bad.Add($"{family}/{id}: {r["error"]}"); continue; }
                    if ((float)r["facingZ"] < 0.5f || (loop && (float)r["stanceAlongFacing"] > 0f)) bad.Add($"{family}/{id}");
                }
                // the pre-v3 shared walk for comparison
                var old = MocapV3Import.Clip("Assets/Art/animations/loco_walk.fbx");
                if (old != null) fam["shared_loco_walk"] = Measure(prefab, old);
                report[family] = fam;
            }
            report["reversed"] = new JArray(bad);
            return report;
        }

        /// <summary>Samples the clip in place (and once with root motion for the travel) on a copy of the prefab facing +Z.</summary>
        public static JObject Measure(GameObject prefab, AnimationClip clip)
        {
            using var rig = new ClipMeasure.Rig(prefab, clip);
            var anim = rig.animator;
            if (anim == null || anim.avatar == null || !anim.avatar.isHuman) return new JObject { ["error"] = "no Humanoid Animator on " + prefab.name };
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b) ?? throw new InvalidOperationException(prefab.name + " has no " + b);
            var lf = B(HumanBodyBones.LeftFoot); var rf = B(HumanBodyBones.RightFoot);
            var lu = B(HumanBodyBones.LeftUpperLeg); var ru = B(HumanBodyBones.RightUpperLeg);
            var lt = B(HumanBodyBones.LeftToes); var rt = B(HumanBodyBones.RightToes);
            int n = Mathf.Max(8, Mathf.RoundToInt(clip.length * 60f));
            var facing = Vector3.zero; var toeDir = Vector3.zero;
            float stance = 0f;
            Vector3 prevL = default, prevR = default;
            for (int i = 0; i <= n; i++)
            {
                rig.At(clip.length * i / n);
                var right = ru.position - lu.position; right.y = 0f;
                var f = Vector3.Cross(right.normalized, Vector3.up);
                facing += f;
                var td = (lt.position - lf.position) + (rt.position - rf.position); td.y = 0f; toeDir += td;
                if (i > 0)
                {
                    // the lower foot is the planted one: its horizontal travel along the facing
                    bool leftDown = lf.position.y < rf.position.y;
                    var d = leftDown ? lf.position - prevL : rf.position - prevR;
                    d.y = 0f;
                    stance += Vector3.Dot(d, f.normalized);
                }
                prevL = lf.position; prevR = rf.position;
            }
            facing.y = 0f; facing.Normalize(); toeDir.Normalize();
            return new JObject
            {
                ["facingZ"] = Math.Round(facing.z, 3), ["facingX"] = Math.Round(facing.x, 3),
                ["toesZ"] = Math.Round(toeDir.z, 3),
                ["stanceAlongFacing"] = Math.Round(stance, 3),
                ["length"] = Math.Round(clip.length, 3),
            };
        }

        public static void RunBatch()
        {
            int code = 0;
            try
            {
                var r = Run();
                var outPath = Environment.GetCommandLineArgs().SkipWhile(a => a != "-ctlifeOut").Skip(1).FirstOrDefault();
                if (outPath != null) File.WriteAllText(outPath, r.ToString());
                Debug.Log("CTLIFE [loco-facing] " + r.ToString(Newtonsoft.Json.Formatting.None));
                if (((JArray)r["reversed"]).Count > 0) code = 2;
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
