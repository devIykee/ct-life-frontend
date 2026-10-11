using System;
using UnityEngine;

namespace CTLife.Core
{
    /// <summary>Which EnvConfig is active. Lives at Resources/Env/EnvSelection.</summary>
    [CreateAssetMenu(menuName = "CT Life/Env Selection", fileName = "EnvSelection")]
    public sealed class EnvSelection : ScriptableObject
    {
        public EnvId active = EnvId.Mock;
        public EnvConfig[] configs = Array.Empty<EnvConfig>();

        public EnvConfig Find(EnvId id)
        {
            foreach (var c in configs)
                if (c != null && c.id == id) return c;
            return null;
        }
    }
}
