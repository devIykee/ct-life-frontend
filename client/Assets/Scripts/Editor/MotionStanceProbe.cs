using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Motion quality pass (lane B, 2026-10-26): samples a locomotion clip on a cast prefab through Unity's own Humanoid
    /// evaluation (the runtime path, minus our ground IK) and measures stance: lateral foot and knee separation against the
    /// hip joint width, per frame. Compares against the same clip measured in Blender to locate where legs close.
    /// Read-only: instantiates in a scratch scene, saves nothing.
    /// -executeMethod CTLife.EditorTools.MotionStanceProbe.Run -probeOut &lt;json&gt;
    /// </summary>
    public static class MotionStanceProbe
    {
        private const string Snow = "Assets/Resources/Characters/SnowController.overrideController", Rain = "Assets/Resources/Characters/RainController.overrideController";
        private static readonly (string model, string controller, string state)[] Cases =
        {
            ("Assets/Art/characters/cast/looks/player_m_kol/player_m_kol.fbx", Snow, "loco_walk"),
            ("Assets/Art/characters/cast/looks/player_m_kol/player_m_kol.fbx", Snow, "loco_run"),
            ("Assets/Art/characters/cast/looks/player_m_kol/player_m_kol.fbx", Snow, "loco_idle"),
            ("Assets/Art/characters/cast/looks/snow_everyday/snow_everyday.fbx", Snow, "loco_walk"),
            ("Assets/Art/characters/cast/looks/player_f_kol/player_f_kol.fbx", Rain, "loco_walk"),
            ("Assets/Art/characters/cast/looks/player_f_kol/player_f_kol.fbx", Rain, "loco_run"),
        };

        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-probeOut");
            string outPath = i >= 0 && i + 1 < args.Length ? args[i + 1] : "stance-probe.json";
            var sb = new StringBuilder("{\"cases\":[");
            bool first = true;
            foreach (var (model, controller, state) in Cases)
                {
                    var r = Probe(model, controller, state);
                    if (!first) sb.Append(','); first = false;
                    sb.Append(r);
                    Debug.Log("STANCE " + r);
                }
            sb.Append("]}");
            File.WriteAllText(outPath, sb.ToString());
            EditorApplication.Exit(0);
        }

        private static string Probe(string modelPath, string controllerPath, string state)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            if (model == null || controller == null) return $"{{\"model\":\"{modelPath}\",\"error\":\"missing\"}}";
            var go = Object.Instantiate(model);
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var anim = go.GetComponentInChildren<Animator>();
                if (anim == null || anim.avatar == null || !anim.avatar.isHuman) return $"{{\"model\":\"{modelPath}\",\"error\":\"no humanoid animator\"}}";
                AnimatorUtility.DeoptimizeTransformHierarchy(anim.gameObject);   // the cast imports optimise the skeleton away
                anim.runtimeAnimatorController = controller;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                anim.applyRootMotion = false;
                anim.Rebind();
                var lUp = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var rUp = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                var lKnee = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg); var rKnee = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                var lFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot); var rFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                if (lUp == null || lFoot == null) return $"{{\"model\":\"{modelPath}\",\"error\":\"bones not found\"}}";
                const int n = 30;
                var foot = new List<float>(); var knee = new List<float>(); var hipW = new List<float>();
                for (int f = 0; f < n; f++)
                {
                    anim.Play(state, 0, f / (float)n);
                    anim.Update(0f);
                    var side = lUp.position - rUp.position; side.y = 0f;
                    float w = side.magnitude; var s = side / Mathf.Max(w, 1e-5f);
                    hipW.Add(w);
                    foot.Add(Vector3.Dot(lFoot.position - rFoot.position, s) / w);
                    knee.Add(Vector3.Dot(lKnee.position - rKnee.position, s) / w);
                }
                string F(List<float> v) => $"{{\"min\":{v.Min():0.000},\"mean\":{v.Average():0.000},\"max\":{v.Max():0.000}}}";
                return $"{{\"model\":\"{Path.GetFileNameWithoutExtension(modelPath)}\",\"state\":\"{state}\",\"controller\":\"{Path.GetFileNameWithoutExtension(controllerPath)}\"," +
                       $"\"hip_w_m\":{hipW.Average():0.0000},\"foot_ratio\":{F(foot)},\"knee_ratio\":{F(knee)},\"crosses\":{(foot.Any(x => x <= 0f) ? "true" : "false")}}}";
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
