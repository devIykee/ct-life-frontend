using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace CTLife.WorldLife
{
    public sealed class PropSpec
    {
        public string Id;
        /// <summary>hand_r | hand_l | head | back | chest | floor | above</summary>
        public string Attach = "hand_r";
        /// <summary>Placeholder primitive until a prop asset exists at Resources/WorldLife/Props/&lt;id&gt;: cube | cylinder | sphere | capsule | quad.</summary>
        public string Shape = "cube";
        public float SX = 0.1f, SY = 0.1f, SZ = 0.1f;
        public float OX, OY, OZ;
        public Rgb Color = new Rgb(1, 1, 1);
        public bool Emissive;
    }

    public sealed class ArchetypeLook
    {
        public string Id;
        public string Name;
        public string Where;
        public string LookNote;
        public string SignatureLine;
        /// <summary>CharacterRegistry look keys for the final cast (mirrors NpcCastCatalog; empty = placeholder).</summary>
        public List<string> CastKeys = new List<string>();
        public List<PropSpec> Props = new List<PropSpec>();
        public float BodyScale = 1f;
        /// <summary>humanoid | drone | hidden</summary>
        public string Body = "humanoid";
        public string TalkGesture = "emote_point";
        public string SignatureEmote;
        public List<string> IdleEmotes = new List<string>();
        public HashSet<string> Behaviours = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Backend has this archetype in contract/ids/archetypes.json (false = FE proposal).</summary>
        public bool InContract;
        public bool Has(string behaviour) => Behaviours.Contains(behaviour);
    }

    /// <summary>
    /// Archetype → look/prop/behaviour binding (Resources/WorldLife/archetype_looks.json). Swapping in the final cast is
    /// data-only: `castKeys` here and the NpcCastCatalog asset name CharacterRegistry looks; props and body mode stay.
    /// </summary>
    public sealed class ArchetypeBook
    {
        private readonly Dictionary<string, ArchetypeLook> _looks = new Dictionary<string, ArchetypeLook>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly List<string> SpecArchetypes = new List<string>();
        public IReadOnlyDictionary<string, ArchetypeLook> All => _looks;

        public static ArchetypeBook Build(JToken looks, JToken feData)
        {
            var book = new ArchetypeBook();
            book.SpecArchetypes.AddRange(WireText.Strs(looks?["specArchetypes"]));
            if (feData?["archetypeAliases"] is JObject aa)
                foreach (var p in aa.Properties()) book._aliases[p.Name] = p.Value.ToString();
            if (looks?["archetypes"] is JObject arch)
                foreach (var p in arch.Properties())
                {
                    var v = p.Value;
                    var look = new ArchetypeLook
                    {
                        Id = p.Name,
                        Name = WireText.Str(v, "name", p.Name),
                        Where = WireText.Str(v, "where"),
                        LookNote = WireText.Str(v, "look"),
                        SignatureLine = WireText.Str(v, "signatureLine"),
                        CastKeys = WireText.Strs(v["castKeys"]),
                        BodyScale = (float)WireText.Num(v, "bodyScale", 1),
                        Body = WireText.Str(v, "body", "humanoid"),
                        TalkGesture = WireText.Str(v, "talkGesture", "emote_point"),
                        SignatureEmote = WireText.Str(v, "signatureEmote"),
                        IdleEmotes = WireText.Strs(v["idleEmotes"]),
                        InContract = WireText.Bool(v, "inContract"),
                    };
                    foreach (var b in WireText.Strs(v["behaviours"])) look.Behaviours.Add(b);
                    if (v["props"] is JArray props)
                        foreach (var pr in props)
                        {
                            var size = Floats(pr["size"], 0.1f);
                            var off = Floats(pr["offset"], 0f);
                            look.Props.Add(new PropSpec
                            {
                                Id = WireText.Str(pr, "id"),
                                Attach = WireText.Str(pr, "attach", "hand_r"),
                                Shape = WireText.Str(pr, "shape", "cube"),
                                SX = size[0], SY = size[1], SZ = size[2],
                                OX = off[0], OY = off[1], OZ = off[2],
                                Color = ParseColor(WireText.Str(pr, "color"), new Rgb(1, 1, 1)),
                                Emissive = WireText.Bool(pr, "emissive"),
                            });
                        }
                    book._looks[p.Name] = look;
                }
            return book;
        }

        public string Canonical(string id) => id != null && _aliases.TryGetValue(id, out var a) ? a : id;

        public ArchetypeLook Get(string archetypeId)
        {
            var id = Canonical(archetypeId);
            return id != null && _looks.TryGetValue(id, out var l) ? l : null;
        }

        /// <summary>Spec archetypes with no look entry, no signature line or no props (coverage check for tests/logs).</summary>
        public List<string> MissingCoverage()
        {
            var missing = new List<string>();
            foreach (var id in SpecArchetypes)
            {
                var l = Get(id);
                if (l == null || string.IsNullOrWhiteSpace(l.SignatureLine) || l.Props.Count == 0 && l.Body == "humanoid") missing.Add(id);
            }
            return missing;
        }

        private static float[] Floats(JToken t, float fallback)
        {
            var r = new[] { fallback, fallback, fallback };
            if (t is JArray a)
                for (int i = 0; i < 3 && i < a.Count; i++)
                    if (float.TryParse(a[i].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) r[i] = f;
            return r;
        }

        /// <summary>`#rrggbb` → Rgb; fallback on anything else.</summary>
        public static Rgb ParseColor(string hex, Rgb fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            var h = hex.TrimStart('#');
            if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return fallback;
            return new Rgb(((v >> 16) & 0xff) / 255f, ((v >> 8) & 0xff) / 255f, (v & 0xff) / 255f);
        }
    }
}
