using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace CTLife.UI
{
    /// <summary>Reads the contract's needs.actions array without inventing missing IDs or prices.</summary>
    internal static class HomeActions
    {
        internal readonly struct ActionDefinition
        {
            public readonly string Id;
            public readonly long CostCents;
            public ActionDefinition(string id, long costCents) { Id = id; CostCents = costCents; }
        }

        private static readonly string[] Required = { "eat", "sleep", "shower", "touch_grass" };

        internal static bool TryRead(JToken config, out ActionDefinition[] result, out string reason)
        {
            result = null;
            reason = "actions_array_missing";
            if (!(config is JObject obj) || !(obj["actions"] is JArray rows)) return false;
            var byId = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                reason = "action_invalid";
                if (!(row is JObject action) || action["id"]?.Type != JTokenType.String) return false;
                string id = action["id"].Value<string>();
                if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || byId.ContainsKey(id)) return false;
                reason = "action_cost_invalid";
                if (action["costCents"]?.Type != JTokenType.Integer) return false;
                long cost;
                try { cost = action["costCents"].Value<long>(); }
                catch (Exception e) when (e is OverflowException || e is InvalidCastException || e is FormatException) { return false; }
                if (cost < 0) return false;
                byId.Add(id, cost);
            }
            var definitions = new ActionDefinition[Required.Length];
            for (int i = 0; i < Required.Length; i++)
            {
                reason = "required_action_missing";
                if (!byId.TryGetValue(Required[i], out var cost)) return false;
                definitions[i] = new ActionDefinition(Required[i], cost);
            }
            result = definitions;
            reason = null;
            return true;
        }
    }
}
