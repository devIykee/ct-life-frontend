using System;
using System.Reflection;
using CTLife.Core;
using CTLife.Realtime;
using CTLife.World;

namespace CTLife.WorldLife
{
    /// <summary>
    /// Binds the presenter to the NpcController presentation seam (four public static delegate fields, requested in
    /// docs/progress/requests.md "2026-10-08 world-life -> integration lead", hook 2). Bound by name so this assembly
    /// compiles and its tests run before the seam lands; once it is in, binding succeeds with no change here.
    /// While the seam is missing, NPCs keep their legacy presentation and this logs one warning.
    /// </summary>
    public static class NpcHookBinder
    {
        public const string RewriteField = "PresentRewrite";
        public const string TalkField = "PresentTalk";
        public const string AppliedField = "PresentApplied";
        public const string KeepHiddenField = "PresentKeepHidden";

        public static bool Bound { get; private set; }

        public static bool Bind(NpcPresenter p)
        {
            Delegate rewrite = p == null ? null : new Func<NpcController, NpcState, NpcState>(p.Rewrite);
            Delegate talk = p == null ? null : new Func<NpcController, NpcState, bool>(p.Talk);
            Delegate applied = p == null ? null : new Action<NpcController, NpcState, NpcState>(p.Applied);
            // All-or-nothing: suppressing the legacy talk bubble without the Applied hook would silence every NPC.
            bool ok = Accepts(RewriteField, rewrite) && Accepts(TalkField, talk) && Accepts(AppliedField, applied);
            if (ok)
            {
                Set(RewriteField, rewrite);
                Set(TalkField, talk);
                Set(AppliedField, applied);
                // Optional: without it, offstage NPCs are re-hidden in Applied (re-activated and hidden in the same frame).
                if (Accepts(KeepHiddenField, null)) Set(KeepHiddenField, p == null ? null : new Func<NpcController, NpcState, bool>(p.KeepHidden));
            }
            Bound = ok && p != null;
            if (p != null && !ok)
                Log.Warn("world-life", "NpcController presentation seam not found (requests.md hook 2); NPCs use legacy presentation.");
            return Bound;
        }

        private static bool Accepts(string name, Delegate value)
        {
            var f = typeof(NpcController).GetField(name, BindingFlags.Public | BindingFlags.Static);
            return f != null && (value == null || f.FieldType.IsInstanceOfType(value));
        }

        public static void Unbind() => Bind(null);

        private static bool Set(string name, Delegate value)
        {
            try
            {
                var f = typeof(NpcController).GetField(name, BindingFlags.Public | BindingFlags.Static);
                if (f == null || value != null && !f.FieldType.IsInstanceOfType(value)) return false;
                f.SetValue(null, value);
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("world-life", e);
                return false;
            }
        }
    }
}
