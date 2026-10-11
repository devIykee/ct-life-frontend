using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace CTLife.WorldLife.Tests
{
    /// <summary>
    /// Locates the lane's presentation JSON and the contract snapshot without UnityEngine (so the same tests run in the
    /// Unity Test Runner, where the working directory is client/, and in the out-of-editor runner, which sets
    /// CTLIFE_WORLDLIFE_DATA / CTLIFE_CONTRACT_DIR).
    /// </summary>
    public static class TestData
    {
        private const string LaneData = "Assets/Scripts/Features/WorldLife/Resources/WorldLife";

        public static string DataDir => Find("CTLIFE_WORLDLIFE_DATA", LaneData, "client/" + LaneData);
        public static string ContractDir => Find("CTLIFE_CONTRACT_DIR", "contract", "../contract");

        private static string Find(string env, params string[] relative)
        {
            var fromEnv = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrEmpty(fromEnv) && Directory.Exists(fromEnv)) return fromEnv;
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (int up = 0; dir != null && up < 8; up++, dir = dir.Parent)
                foreach (var r in relative)
                {
                    var candidate = Path.Combine(dir.FullName, r);
                    if (Directory.Exists(candidate)) return candidate;
                }
            throw new DirectoryNotFoundException("Set " + env + " (looked for " + string.Join(", ", relative) + ")");
        }

        public static JObject Lane(string name) => (JObject)JToken.Parse(File.ReadAllText(Path.Combine(DataDir, name + ".json")));
        public static JToken Contract(string relative) => JToken.Parse(File.ReadAllText(Path.Combine(ContractDir, relative)));

        public static JObject Presentation => Lane("npc_presentation");
        public static JObject Looks => Lane("archetype_looks");
        public static JObject HouseAds => Lane("house_ads");

        /// <summary>The line book as the game builds it: backend rc1 npc_lines + FE presentation data.</summary>
        public static NpcLineBook Book() => NpcLineBook.Build(Contract("config/npc_lines.json"), Presentation, Looks);
    }
}
