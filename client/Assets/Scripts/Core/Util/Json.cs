using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTLife.Core
{
    /// <summary>Shared Newtonsoft settings. Generated REST models carry their own [DataMember] names.</summary>
    public static class Json
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            DateParseHandling = DateParseHandling.DateTime,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        };

        public static string Serialize(object o) => JsonConvert.SerializeObject(o, Settings);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        /// <summary>
        /// Parses without turning ISO strings into DateTime. Required for the SIWS challenge: the wallet
        /// must sign the exact strings the server issued (contract section 3 step 5 checks field for field).
        /// </summary>
        public static JToken ParseRaw(string json)
        {
            using var sr = new StringReader(json);
            using var reader = new JsonTextReader(sr)
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
            };
            return JToken.ReadFrom(reader);
        }

        public static string Str(this JToken t, string key, string fallback = null)
        {
            var v = t?[key];
            return v == null || v.Type == JTokenType.Null ? fallback : v.ToString();
        }

        public static int Int(this JToken t, string key, int fallback = 0)
        {
            var v = t?[key];
            return v == null || v.Type == JTokenType.Null ? fallback : v.Value<int>();
        }

        public static long Long(this JToken t, string key, long fallback = 0)
        {
            var v = t?[key];
            return v == null || v.Type == JTokenType.Null ? fallback : v.Value<long>();
        }

        public static bool Bool(this JToken t, string key, bool fallback = false)
        {
            var v = t?[key];
            return v == null || v.Type == JTokenType.Null ? fallback : v.Value<bool>();
        }

        public static float Float(this JToken t, string key, float fallback = 0f)
        {
            var v = t?[key];
            return v == null || v.Type == JTokenType.Null ? fallback : v.Value<float>();
        }
    }
}
