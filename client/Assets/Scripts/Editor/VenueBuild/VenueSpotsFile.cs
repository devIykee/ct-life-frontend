using System.IO;
using CTLife.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.EditorTools.VenueBuild
{
    /// <summary>
    /// Reads FE-owned spot sets from docs/fe-contract-proposals/ids/spots.json (the proposal that contract/ids/spots.json
    /// mirrors). New spot sets/ids are appended there by tools/venues/propose_spots.py, never renamed.
    /// </summary>
    public static class VenueSpotsFile
    {
        public static string ProposalPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/fe-contract-proposals/ids/spots.json"));

        private static JObject _doc;

        public static void Reset() => _doc = null;

        public static VenueSpots Load(string venueKey)
        {
            _doc ??= JObject.Parse(File.ReadAllText(ProposalPath));
            var venue = _doc["venues"]?[venueKey] ?? throw new InvalidDataException("spots.json proposal has no venue " + venueKey);
            var vs = new VenueSpots();
            foreach (var r in (JArray)venue["walkable"])
                vs.walkable.Add(new WalkRect { x0 = (float)r["x0"], z0 = (float)r["z0"], x1 = (float)r["x1"], z1 = (float)r["z1"] });
            foreach (var s in (JArray)venue["spots"])
            {
                string id = (string)s["id"];
                string local = id.StartsWith(venueKey + ".") ? id.Substring(venueKey.Length + 1) : id;
                var def = new SpotDef
                {
                    id = local, wireId = id, kind = (string)s["kind"] ?? "stand", anim = (string)s["anim"],
                    x = (float)s["x"], z = (float)s["z"], rotY = s["rotY"] != null ? (float)s["rotY"] : 0f, hasPosition = s["x"] != null,
                };
                vs.spots[local] = def;
                if (id != local) vs.spots[id] = def;
            }
            return vs;
        }
    }
}
