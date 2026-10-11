using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Feel lane F1 ("when it raises its hands it feels very weird"): every emote, celebration and arms-up idle on one look per
    /// cast family, frozen at several phases, rendered as front close-ups (captureSet "arms"). The UBC family uses the clips'
    /// authoring skeleton (pelvis/spine_01/thigh_l), so it is the source-rig reference against the Rain/Snow-derived cast.
    /// Also measures per frame the shoulder-to-hand reach and the upper-arm lift so retarget clamps show as numbers.
    /// </summary>
    internal static class ArmsReviewCapture
    {
        public static readonly string[] Families =
        {
            "skin_tone3|head_ubc_m|hair_ubc_m|top_ubc_m|bottom_ubc_m|shoes_ubc_m|acc_none",
            "skin_dark|head_player_m_kol|hair_player_m_kol|top_player_m_kol|bottom_player_m_kol|shoes_player_m_kol|acc_none",
            "skin_tone3|head_snow|hair_snow_bun|top_vneck_charcoal|bottom_denim_light|shoes_sneakers_blue|acc_none",
            "skin_tone3|head_ubc_f|hair_ubc_f|top_ubc_f|bottom_ubc_f|shoes_ubc_f|acc_none",
            "skin_tan|head_player_f_kol|hair_player_f_kol|top_player_f_kol|bottom_player_f_kol|shoes_player_f_kol|acc_none",
            "skin_tone3|head_rain|hair_rain_ponytail|top_sleeveless_white|bottom_denim_fitted|shoes_canvas_ochre|acc_scarf_sky",
            "skin_tone3|head_hero_gynoid|hair_hero_gynoid|top_hero_gynoid|bottom_hero_gynoid|shoes_hero_gynoid|acc_none",
        };

        public static string[] States = { "loco_idle", "emote_wave", "emote_wagmi", "emote_moon_jump", "react_moon_jump", "emote_heart_hands", "emote_salute",
            "emote_send_it", "emote_chest_thump", "emote_point", "emote_thumbs_up", "emote_clap",
            "emote_hood_up", "emote_ring_light", "emote_shrug", "emote_facepalm", "emote_laugh", "emote_bow", "emote_coin_flip",
            "emote_gm_coffee", "emote_watering_can", "emote_head_shake", "emote_nod", "act_cry", "idle_fidget_stretch", "idle_fidget_lookaround",
            "idle_fidget_foldarms", "act_dance_1", "act_dance_2", "react_rage_stomp", "react_shock", "react_sad" };

        public static readonly float[] Phases = { 0.2f, 0.35f, 0.5f, 0.65f, 0.8f };

        public static async Task Run(Func<string, float, Task> shot, Action<bool, string> expect, JObject receipt, string outDir)
        {
            var o = new JObject(); receipt["arms"] = o;
            var root = new GameObject("arms-review");
            var camGo = new GameObject("arms-cam"); camGo.transform.SetParent(root.transform);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.05f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.82f, 0.85f, 0.9f);
            cam.enabled = false;
            var light = new GameObject("arms-sun").AddComponent<Light>(); light.transform.SetParent(root.transform);
            light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(35f, 150f, 0f); light.intensity = 1.3f;
            RenderSettings.ambientLight = new Color(0.6f, 0.62f, 0.66f);
            var rt = new RenderTexture(300, 400, 24) { antiAliasing = 4 };
            var tex = new Texture2D(300, 400, TextureFormat.RGB24, false);
            Directory.CreateDirectory(outDir);
            var actors = new List<AvatarView>();
            try
            {
                for (int i = 0; i < Families.Length; i++)
                {
                    var look = Families[i].Split('|');
                    var v = AvatarFactory.Create("arms-" + look[1], look, false, null, root.transform);
                    v.transform.SetPositionAndRotation(new Vector3(i * 6f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f));
                    v.enabled = false;  // no idle life, overlays or base requests: the review owns the Animator
                    var ik = v.GetComponentInChildren<CharacterIK>(); if (ik != null) ik.enabled = false;
                    actors.Add(v);
                }
                for (int f = 0; f < 30; f++) await Awaitable.NextFrameAsync();
                foreach (var state in States)
                {
                    var stateO = new JObject(); o[state] = stateO;
                    var sheet = new Texture2D(300 * Phases.Length, 400 * actors.Count, TextureFormat.RGB24, false);
                    for (int a = 0; a < actors.Count; a++)
                    {
                        var v = actors[a]; var an = v.Animator;
                        if (an == null || !an.HasState(0, Animator.StringToHash(state))) { stateO[Families[a].Split('|')[1]] = "no state"; continue; }
                        var metrics = new JArray();
                        for (int p = 0; p < Phases.Length; p++)
                        {
                            an.cullingMode = AnimatorCullingMode.AlwaysAnimate; an.enabled = true;   // the review camera renders manually
                            an.speed = 0f;
                            an.Play(state, 0, Phases[p]);
                            // The finger pose the game layers on (HandPoses "Hands" layer), at full weight as mid-emote.
                            int hands = an.GetLayerIndex(HandPoses.Layer);
                            if (hands >= 0)
                            {
                                bool posed = HandPoses.TryState(state, out var hs) && an.HasState(hands, Animator.StringToHash(hs));
                                if (posed) an.Play(hs, hands, 0f);
                                an.SetLayerWeight(hands, posed ? 1f : 0f);
                            }
                            await Awaitable.NextFrameAsync();   // the engine's own update applies the frozen pose to the skin
                            await Awaitable.NextFrameAsync();
                            var target = v.transform.position + Vector3.up * 1.15f;
                            camGo.transform.position = target + v.transform.forward * 3.6f + v.transform.right * 0.6f + Vector3.up * 0.15f;
                            camGo.transform.LookAt(target);
                            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                            var prev = RenderTexture.active; RenderTexture.active = rt;
                            tex.ReadPixels(new Rect(0, 0, 300, 400), 0, 0); tex.Apply(); RenderTexture.active = prev;
                            sheet.SetPixels(p * 300, (actors.Count - 1 - a) * 400, 300, 400, tex.GetPixels());
                            metrics.Add(ArmMetrics(an));
                        }
                        stateO[Families[a].Split('|')[1]] = metrics;
                    }
                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, "arms-" + state + ".png"), sheet.EncodeToPNG());
                    Object.DestroyImmediate(sheet);
                }
                expect(true, "arms review rendered " + States.Length + " states on " + actors.Count + " families");
            }
            finally
            {
                Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
                Object.Destroy(root);
            }
            await Task.CompletedTask;
        }

        /// <summary>Humanoid muscle readout at the current pose (rig-independent): shoulder up/down, arm up/down, twist.</summary>
        private static JObject ArmMetrics(Animator an)
        {
            var handler = new HumanPoseHandler(an.avatar, an.transform);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            var o = new JObject();
            foreach (var m in new[] { "Left Shoulder Down-Up", "Right Shoulder Down-Up", "Left Arm Down-Up", "Right Arm Down-Up", "Left Arm Twist In-Out", "Right Arm Twist In-Out",
                "Left Forearm Stretch", "Right Forearm Stretch", "Left Hand Down-Up", "Right Hand Down-Up", "Left Hand In-Out", "Right Hand In-Out",
                "Left Upper Leg In-Out", "Right Upper Leg In-Out", "Left Upper Leg Front-Back", "Right Upper Leg Front-Back", "Left Lower Leg Stretch", "Right Lower Leg Stretch" })
            {
                int i = Array.IndexOf(HumanTrait.MuscleName, m);
                if (i >= 0) o[m.Replace(" ", "")] = Math.Round(pose.muscles[i], 2);
            }
            handler.Dispose();
            return o;
        }
    }
}
