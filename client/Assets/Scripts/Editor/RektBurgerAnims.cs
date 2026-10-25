using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using CTLife.World;
using UnityEngine;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Rekt Burger (2026-10-25): the clock-in stations' CMU mocap clips (MocapImport.Clips act_rb_*) imported with their own
    /// Avatars and upserted as states of the shared AvatarController, nothing else touched (a full CharacterFeelController
    /// Apply re-points states other tools fitted). Batch: -executeMethod CTLife.EditorTools.RektBurgerAnims.RunBatch
    /// </summary>
    public static class RektBurgerAnims
    {
        [MenuItem("CT Life/Characters/Rekt Burger station clips")]
        public static void Run()
        {
            MocapImport.Run();
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterFeelController.ControllerPath);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var (file, state) in MocapImport.Clips.Where(c => c.state.StartsWith("act_rb_", StringComparison.Ordinal)))
            {
                var clip = MocapImport.Clip(MocapImport.PathOf(file)) ?? throw new InvalidOperationException("clip missing: " + file);
                var st = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == state) ?? sm.AddState(state);
                st.motion = clip;
                Debug.Log($"CTLIFE [rekt-anims] {state} <- {file} ({clip.length:F2} s)");
            }
            EditorUtility.SetDirty(ctrl);
            WriteLengths();
            AssetDatabase.SaveAssets();
            // the way the body faces in each clip on the shipped male look (shoulder line x up; 0 = the actor's own forward)
            var look = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/characters/cast/prefabs/player_m_kol.prefab");
            foreach (var (label, path) in new[] { ("loco_idle", "Assets/Art/animations/loco_idle.fbx"), ("act_interact", "Assets/Art/animations/act_interact.fbx") }
                         .Concat(MocapImport.Clips.Where(c => c.state.StartsWith("act_rb_", StringComparison.Ordinal)).Select(c => (c.state, MocapImport.PathOf(c.file)))))
            {
                var clip = CharacterFeelController.Clip(path);
                if (clip == null || look == null) continue;
                using var rig = new ClipMeasure.Rig(look, clip);
                var l = rig.animator.GetBoneTransform(HumanBodyBones.LeftUpperArm); var r = rig.animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                var yaws = new[] { 0f, 0.5f, 1f }.Select(k =>
                {
                    rig.At(clip.length * k);
                    var f = Vector3.Cross(r.position - l.position, Vector3.up);
                    return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                }).ToArray();
                Debug.Log($"CTLIFE [rekt-anims] facing {label}: {string.Join(" / ", yaws.Select(y => y.ToString("0")))} deg (start / middle / end)");
            }
        }

        /// <summary>The act_rb_* clips' lengths in LocomotionTuning (code reads them for one-shot timing; SeatFitTests checks every
        /// mocap state). Batch: -executeMethod CTLife.EditorTools.RektBurgerAnims.LengthsBatch</summary>
        public static void WriteLengths()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<LocomotionTuning>(CharacterFeelController.TuningPath);
            if (tuning == null) throw new InvalidOperationException("tuning missing");
            foreach (var (file, state) in MocapImport.Clips.Where(c => c.state.StartsWith("act_rb_", StringComparison.Ordinal)))
            {
                var clip = MocapImport.Clip(MocapImport.PathOf(file));
                if (clip == null) throw new InvalidOperationException("clip missing: " + file);
                tuning.lengths.RemoveAll(l => l != null && l.state == state);
                tuning.lengths.Add(new LocomotionTuning.StateLength { state = state, seconds = clip.length });
                Debug.Log($"CTLIFE [rekt-anims] tuning length {state} = {clip.length:F2} s");
            }
            EditorUtility.SetDirty(tuning);
            AssetDatabase.SaveAssets();
        }

        public static void LengthsBatch()
        {
            int code = 0;
            try { WriteLengths(); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        public static void RunBatch()
        {
            int code = 0;
            try { Run(); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
