using System.Collections.Generic;
using CTLife.Core;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Maps anims.json ids (FE-owned registry) to Animator states. Convention for the shared avatar controller
    /// (built in the editor from the art pipeline's clips): one state per anim id, named exactly like the id, plus a
    /// float parameter "speed" for the locomotion blend tree. Unknown ids fall back to loco_idle and are logged once.
    /// </summary>
    public static class AnimationLibrary
    {
        public static readonly int SpeedParam = Animator.StringToHash("speed");
        private static readonly Dictionary<string, int> _hashes = new Dictionary<string, int>();

        public static AnimDef Def(string animId)
        {
            if (!Services.TryGet<ConfigStore>(out var cfg)) return null;
            return cfg.Registries.Anims.TryGetValue(animId ?? "", out var d) ? d : null;
        }

        public static int Hash(string animId)
        {
            if (!_hashes.TryGetValue(animId, out var h))
            {
                h = Animator.StringToHash(animId);
                _hashes[animId] = h;
            }
            return h;
        }

        /// <summary>Cross-fades to the state named animId on layer 0 if it exists. Returns false if not.</summary>
        public static bool TryPlay(Animator animator, string animId, float fade)
        {
            if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(animId)) return false;
            var h = Hash(animId);
            if (!animator.HasState(0, h))
            {
                if (Def(animId) == null) Log.UnknownId("anims", animId);
                return false;
            }
            animator.CrossFadeInFixedTime(h, fade, 0);
            return true;
        }
    }
}
