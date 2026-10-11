using System.Collections.Generic;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Finger poses for emotes (Phase A, 2026-10-17): the shipped clips carry no finger animation, so a "Hands" override
    /// layer (fingers-only mask, built by HandPoseAssets) blends a pose in for the emote and back out after it. Left/right
    /// per emote; anything not listed keeps the clip's own (neutral) hands.
    /// </summary>
    public static class HandPoses
    {
        public const string Layer = "Hands", None = "hand_none";
        public const float FadeIn = 0.12f, FadeOut = 0.2f;

        public static readonly Dictionary<string, (string left, string right)> Map = new Dictionary<string, (string, string)>
        {
            ["emote_wave"] = ("relaxed", "open"),
            ["emote_point"] = ("relaxed", "point"),
            ["emote_thumbs_up"] = ("relaxed", "thumbs"),
            ["emote_clap"] = ("open", "open"),
            ["emote_heart_hands"] = ("curve", "curve"),
            ["emote_wagmi_fist_pump"] = ("fist", "fist"),
            ["emote_chest_thump"] = ("relaxed", "fist"),
            ["emote_send_it_slam"] = ("fist", "fist"),
            ["emote_moon_jump"] = ("fist", "fist"),
            ["emote_facepalm"] = ("open", "open"),
            ["emote_salute"] = ("relaxed", "open"),
            ["emote_shrug"] = ("open", "open"),
            ["emote_gm_coffee"] = ("relaxed", "grip"),
            ["emote_coin_flip"] = ("relaxed", "grip"),
            ["emote_watering_can"] = ("grip", "grip"),
            ["emote_hood_up"] = ("grip", "grip"),
            ["emote_ring_light_pose"] = ("open", "point"),
            ["emote_cry"] = ("curve", "curve"),
            ["emote_laugh"] = ("relaxed", "relaxed"),
            ["emote_bow"] = ("relaxed", "relaxed"),
            ["emote_dance_1"] = ("open", "open"),
            ["emote_dance_2"] = ("fist", "fist"),
        };

        public static string StateName(string left, string right) => $"hand_{left}_{right}";

        public static bool TryState(string animId, out string state)
        {
            state = null;
            if (string.IsNullOrEmpty(animId) || !Map.TryGetValue(animId, out var p)) return false;
            state = StateName(p.left, p.right);
            return true;
        }

        /// <summary>Per-avatar weight easing; call every frame from AvatarView.</summary>
        public sealed class Driver
        {
            private int _layer = -2;
            private float _weight, _target;
            private Animator _for;

            public void Begin(Animator a, string animId)
            {
                if (!Resolve(a) || !TryState(animId, out var state) || !a.HasState(_layer, Animator.StringToHash(state))) { _target = 0f; return; }
                a.CrossFadeInFixedTime(state, _weight > 0.01f ? FadeIn : 0f, _layer);
                _target = 1f;
            }

            public void End() => _target = 0f;

            public void Tick(Animator a, float dt)
            {
                if (!Resolve(a)) return;
                if (Mathf.Approximately(_weight, _target)) return;
                _weight = Mathf.MoveTowards(_weight, _target, dt / (_target > _weight ? FadeIn : FadeOut));
                a.SetLayerWeight(_layer, _weight);
            }

            public float Weight => _weight;

            private bool Resolve(Animator a)
            {
                if (a == null || a.runtimeAnimatorController == null) return false;
                if (_for != a || _layer == -2) { _for = a; _layer = a.GetLayerIndex(Layer); _weight = 0f; }
                return _layer >= 0;
            }
        }
    }
}
