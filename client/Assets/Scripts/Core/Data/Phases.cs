using Newtonsoft.Json.Linq;

namespace CTLife.Core
{
    /// <summary>
    /// The highest open build phase (1-6). Phone apps, map venues and careers above it show "coming soon".
    /// Order of authority: GET /config (`livePhase`, or `phase`, top level or under `meta`) → the build's
    /// EnvConfig.livePhase → 1. Development builds may override with the `phase` launch argument.
    /// This replaces the two hard-coded `CurrentPhase = 1` constants (QA code-architecture §1.4).
    /// </summary>
    public static class Phases
    {
        public const int Min = 1, Max = 6;

        public static int Live
        {
            get
            {
#if !CTLIFE_PROD
                if (LaunchArgs.TryGet("phase", out var arg) && int.TryParse(arg, out var forced)) return Clamp(forced);
#endif
                var root = Services.TryGet<ConfigStore>(out var config) ? config.Root : null;
                return Resolve(root, Env.Current != null ? Env.Current.livePhase : Min);
            }
        }

        /// <summary>Pure resolution, unit-tested: the server value wins when it is a valid integer.</summary>
        public static int Resolve(JToken configRoot, int envDefault)
        {
            var server = ReadPhase(configRoot);
            return Clamp(server ?? envDefault);
        }

        public static bool IsOpen(int phase) => phase <= Live;

        private static int? ReadPhase(JToken root)
        {
            if (root == null) return null;
            foreach (var token in new[] { root["livePhase"], root["phase"], root["meta"]?["livePhase"], root["config"]?["meta"]?["livePhase"] })
                if (token != null && token.Type == JTokenType.Integer) return token.Value<int>();
            return null;
        }

        private static int Clamp(int p) => p < Min ? Min : p > Max ? Max : p;
    }
}
