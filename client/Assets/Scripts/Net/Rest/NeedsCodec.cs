using System;
using CTLife.Rest.Model;
using Newtonsoft.Json.Linq;

namespace CTLife.Net
{
    /// <summary>
    /// The live server decays needs fractionally. The rc1 generated UI model uses whole
    /// percentages; round down for display only, without changing the authoritative state.
    /// </summary>
    public static class NeedsCodec
    {
        private static readonly string[] Fields = { "energy", "hunger", "fun", "hygiene", "sanity" };

        public static JObject Normalize(JToken token)
        {
            if (!(token is JObject)) throw new FormatException("Missing needs");
            var result = new JObject();
            foreach (var field in Fields)
            {
                var value = token[field];
                if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float))
                    throw new FormatException("Invalid need: " + field);
                double number = value.Value<double>();
                if (double.IsNaN(number) || double.IsInfinity(number) || number < 0 || number > 100)
                    throw new FormatException("Need out of range: " + field);
                result[field] = (int)Math.Floor(number);
            }
            return result;
        }

        public static Needs Decode(JToken token)
        {
            var n = Normalize(token);
            return new Needs((int)n["energy"], (int)n["hunger"], (int)n["fun"], (int)n["hygiene"], (int)n["sanity"]);
        }
    }
}
