using Newtonsoft.Json.Linq;

namespace CTLife.Ventures
{
    /// <summary>Reads the proposed id registries (`ad_rejection_reasons`, `project_events`) when the server's config carries them.</summary>
    public static class VenturesRegistry
    {
        /// <summary>A registry as the config store holds it: a bare array, `{items: []}` (the store's layout), or the proposal file's own key.</summary>
        private static JArray Rows(JToken registry, string fileKey) =>
            registry is JArray a ? a : registry?["items"] as JArray ?? registry?[fileKey] as JArray;

        /// <summary>`{adRejectionReasons: [{id, label}]}` (or a bare array) → the label for <paramref name="id"/>, or null.</summary>
        public static string ReasonLabel(JToken registry, string id)
        {
            var rows = Rows(registry, "adRejectionReasons");
            if (rows == null) return null;
            foreach (var r in rows)
                if (VWire.Str(r["id"]) == id)
                {
                    var label = VWire.Plain(r["label"], 80);
                    return label.Length == 0 ? null : label;
                }
            return null;
        }

        /// <summary>`{projectEvents: [{id, risk}]}` → whether the event is a server-rolled risk. Unknown ids are not risks.</summary>
        public static bool IsRiskEvent(JToken registry, string id)
        {
            var rows = Rows(registry, "projectEvents");
            if (rows == null) return false;
            foreach (var r in rows)
                if (VWire.Str(r["id"]) == id) return VWire.BoolOr(r["risk"], false);
            return false;
        }
    }
}
