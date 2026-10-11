using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CTLife.UI
{
    /// <summary>The customizer's categories, in rail order. Character is the base model (its own step); the rest edit it.</summary>
    public enum LookCategory { Character, SkinTone, Hair, HairColor, Top, Bottom, Shoes, Accessories, Colors, Outfit }

    /// <summary>Swappable modular parts (one per slot) on a shared rig.</summary>
    public enum PartSlot { Head, Hair, Top, Bottom, Shoes }

    /// <summary>Tint regions of a modular model (each takes one swatch from a palette).</summary>
    public enum TintRegion { Skin, Hair, Top, Bottom, Shoes, Accent }

    /// <summary>Accessory sockets on the shared rig (each holds at most one accessory).</summary>
    public enum AccessorySocket { Head, Face, Neck, Back, Shoulder, Hands }

    public enum OptionKind { Part, Tint, Accessory, Outfit, Palette, HairStyle }
    public enum CharacterBrowse { All, Men, Women, Others }

    /// <summary>
    /// What the player built: a base model plus per-slot parts, per-region tints and per-socket accessories. Today's cast
    /// models are whole authored characters, carried as <see cref="legacyLook"/> (seven contract ids) with no parts. The
    /// multi-model cast (Quaternius bases, modular parts, masked CT heroes, one shared rig, tint regions) fills the maps.
    /// Maps are keyed by enum name so the JSON is readable and survives new values.
    /// </summary>
    [Serializable]
    public sealed class AvatarSpec
    {
        public string baseId;
        public string[] legacyLook;
        public Dictionary<string, string> parts = new Dictionary<string, string>();
        public Dictionary<string, string> tints = new Dictionary<string, string>();
        public Dictionary<string, string> accessories = new Dictionary<string, string>();

        public bool IsModular => parts.Count > 0 || tints.Count > 0 || accessories.Count > 0;

        public AvatarSpec Clone() => new AvatarSpec
        {
            baseId = baseId, legacyLook = (string[])legacyLook?.Clone(),
            parts = new Dictionary<string, string>(parts), tints = new Dictionary<string, string>(tints),
            accessories = new Dictionary<string, string>(accessories),
        };

        public string Get(PartSlot s) => parts.TryGetValue(s.ToString(), out var v) ? v : null;
        public string Get(TintRegion r) => tints.TryGetValue(r.ToString(), out var v) ? v : null;
        public string Get(AccessorySocket s) => accessories.TryGetValue(s.ToString(), out var v) ? v : null;

        /// <summary>A stable string for equality (sorted maps).</summary>
        public string Key()
        {
            string Map(Dictionary<string, string> m) => string.Join(",", m.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
            return baseId + "|" + string.Join("|", legacyLook ?? Array.Empty<string>()) + "|p:" + Map(parts) + "|t:" + Map(tints) + "|a:" + Map(accessories);
        }

        public JObject ToJson() => new JObject
        {
            ["base"] = baseId, ["legacy"] = legacyLook == null ? null : new JArray(legacyLook.Select(x => (object)x ?? "").ToArray()),
            ["parts"] = JObject.FromObject(parts), ["tints"] = JObject.FromObject(tints), ["acc"] = JObject.FromObject(accessories),
        };

        public static AvatarSpec FromJson(JToken t)
        {
            if (!(t is JObject o)) return null;
            var s = new AvatarSpec { baseId = (string)o["base"] };
            if (o["legacy"] is JArray l && l.Count == 7) s.legacyLook = l.ToObject<string[]>();
            if (o["parts"] is JObject p) s.parts = p.ToObject<Dictionary<string, string>>();
            if (o["tints"] is JObject ti) s.tints = ti.ToObject<Dictionary<string, string>>();
            if (o["acc"] is JObject a) s.accessories = a.ToObject<Dictionary<string, string>>();
            return s;
        }
    }

    /// <summary>
    /// The customizer data (Resources/UI/CustomizerCatalog, optional; the cast lane's manifest v2 import will write it):
    /// palettes (the default skin palette has 10 steps), and per base model its swappable parts per slot, the tint regions
    /// it supports (each with a palette) and its accessories per socket. Today's whole-look cast needs no entries.
    /// </summary>
    [CreateAssetMenu(menuName = "CT Life/Customizer Catalog", fileName = "CustomizerCatalog")]
    public sealed class CustomizerCatalog : ScriptableObject
    {
        [Serializable] public sealed class Swatch { public string id, label; public Color color = Color.white; }
        [Serializable] public sealed class Palette { public string id; public List<Swatch> swatches = new List<Swatch>(); }
        [Serializable] public sealed class Part { public PartSlot slot; public string id, label; public Texture2D thumb; }
        [Serializable] public sealed class Region { public TintRegion region; public string paletteId, defaultSwatch; }
        [Serializable] public sealed class Accessory { public AccessorySocket socket; public string id, label; public Texture2D thumb; }

        [Serializable]
        public sealed class Base
        {
            [Tooltip("Base model id (CustomizerModel.BaseId of its default look: the head id for authored characters).")]
            public string id;
            [Tooltip("Default parts (one per slot) and default tints for a modular base; empty for a whole authored look.")]
            public List<Part> parts = new List<Part>();
            public List<Region> regions = new List<Region>();
            public List<Accessory> accessories = new List<Accessory>();
        }

        public List<Palette> palettes = new List<Palette>();
        public List<Base> bases = new List<Base>();

        public static CustomizerCatalog Load() => Resources.Load<CustomizerCatalog>("UI/CustomizerCatalog");

        public Palette FindPalette(string id) => palettes.FirstOrDefault(p => p.id == id) ?? (id == DefaultSkinPaletteId ? DefaultSkinPalette() : null);

        public const string DefaultSkinPaletteId = "skin10";

        /// <summary>
        /// The 10-step skin palette, light to deep, with even luminance steps (the re-spread proposed to the cast lane,
        /// requests.md 2026-10-09). Used when the catalog defines no "skin10" palette of its own.
        /// </summary>
        public static Palette DefaultSkinPalette()
        {
            var hex = new[] { "#f6dcc6", "#eecaa9", "#e2b48e", "#d29e74", "#bf885d", "#a8714a", "#8e5b3a", "#73462d", "#583322", "#3b2418" };
            var names = new[] { "Porcelain", "Ivory", "Sand", "Honey", "Caramel", "Bronze", "Chestnut", "Mocha", "Espresso", "Ebony" };
            var p = new Palette { id = DefaultSkinPaletteId };
            for (int i = 0; i < hex.Length; i++)
            {
                ColorUtility.TryParseHtmlString(hex[i], out var c);
                p.swatches.Add(new Swatch { id = "skin_" + (i + 1).ToString("00"), label = names[i], color = c });
            }
            return p;
        }
    }

    /// <summary>One choice on the rail's option grid: a part, a tint swatch or an accessory (or "none" for a socket).</summary>
    public sealed class CustomizerOption
    {
        public LookCategory category;
        public OptionKind kind;
        public string key, value, label;
        public bool hasSwatch;
        public Color swatch = Color.white;
        public Texture2D thumb;
        public string[] authoredLook;

        public string Id => kind + ":" + key + ":" + (value ?? "none");

        public AvatarSpec ApplyTo(AvatarSpec spec)
        {
            var next = spec.Clone();
            if (kind == OptionKind.HairStyle)
            {
                if (CharacterPaletteResolver.TryChangeHairStyle(next.legacyLook, value, out var changed)) next.legacyLook = changed;
                return next;
            }
            if (kind == OptionKind.Palette)
            {
                if (CharacterPaletteResolver.TryChange(next.legacyLook, key, value, out var changed)) next.legacyLook = changed;
                return next;
            }
            if (kind == OptionKind.Outfit)
            {
                next.legacyLook = CharacterPaletteResolver.TryChangeOutfit(next.legacyLook, authoredLook, out var changedOutfit)
                    ? changedOutfit : (string[])authoredLook?.Clone();
                next.parts.Clear(); next.tints.Clear(); next.accessories.Clear();
                return next;
            }
            var map = kind == OptionKind.Part ? next.parts : kind == OptionKind.Tint ? next.tints : next.accessories;
            if (string.IsNullOrEmpty(value)) map.Remove(key);
            else map[key] = value;
            return next;
        }

        public bool IsCurrent(AvatarSpec spec)
        {
            if (kind == OptionKind.HairStyle)
                return spec?.legacyLook != null && CharacterPaletteResolver.TryChangeHairStyle(spec.legacyLook, value, out var changedHair) &&
                    changedHair.SequenceEqual(spec.legacyLook);
            if (kind == OptionKind.Palette)
            {
                return spec?.legacyLook != null && CharacterPaletteResolver.TryChange(spec.legacyLook, key, value, out var changed) &&
                    changed.SequenceEqual(spec.legacyLook);
            }
            if (kind == OptionKind.Outfit) return authoredLook != null && spec?.legacyLook != null &&
                (authoredLook.SequenceEqual(spec.legacyLook) || CharacterPaletteResolver.TryResolve(spec.legacyLook, out var selection) &&
                    authoredLook.SequenceEqual(selection.IdentityLook));
            var map = kind == OptionKind.Part ? spec.parts : kind == OptionKind.Tint ? spec.tints : spec.accessories;
            map.TryGetValue(key, out var v);
            return string.IsNullOrEmpty(value) ? string.IsNullOrEmpty(v) : v == value;
        }
    }

    /// <summary>One base model in the Character step.</summary>
    public sealed class BaseModel
    {
        public string id, label, species;
        public char family;
        public string[] look;
        public float previewYaw;
        public Texture2D portrait;
        public CustomizerCatalog.Base data;
        public readonly List<CreatorLookCatalog.Entry> outfits = new List<CreatorLookCatalog.Entry>();
        public bool OwnsLook(string[] candidate) => candidate != null && (outfits.Any(e => e.look.SequenceEqual(candidate)) ||
            candidate.Length == 7 && CharacterPaletteResolver.TryResolve(candidate, out var palette) &&
            outfits.Any(e => e.look.SequenceEqual(palette.IdentityLook)));
    }

    /// <summary>
    /// Pure customizer rules (EditMode tests: OnboardingR2Tests): base models, the options per category, which categories a
    /// model shows (a category hides itself unless it offers at least two distinct specs that resolve), the default spec of
    /// a model, and the mapping to the contract's seven Look ids (plus the extras proposed in
    /// docs/fe-contract-proposals/look-customizer-20261009.md).
    /// </summary>
    public static class CustomizerModel
    {
        public static readonly LookCategory[] RailOrder =
        {
            LookCategory.Outfit, LookCategory.SkinTone, LookCategory.Hair, LookCategory.HairColor, LookCategory.Top, LookCategory.Bottom,
            LookCategory.Shoes, LookCategory.Accessories, LookCategory.Colors,
        };

        /// <summary>Contract accId takes one accessory: the first filled socket in this order. The rest are extras.</summary>
        public static readonly AccessorySocket[] AccessoryPriority =
            { AccessorySocket.Face, AccessorySocket.Head, AccessorySocket.Neck, AccessorySocket.Back, AccessorySocket.Shoulder, AccessorySocket.Hands };

        /// <summary>A stable id for a base model: its head id (unique per authored character), else the joined look.</summary>
        public static string BaseId(string[] look) =>
            look == null ? null : look.Length > 1 && !string.IsNullOrEmpty(look[1]) ? look[1] : string.Join("|", look);

        public static List<BaseModel> Build(IReadOnlyList<CreatorLookCatalog.Entry> looks, CustomizerCatalog catalog, Func<string, Texture2D> portraits = null)
        {
            var result = new List<BaseModel>();
            if (looks == null) return result;
            foreach (var e in looks)
            {
                if (e?.look == null) continue;
                var identity = e.IdentityId;
                var existing = result.FirstOrDefault(m => m.id == identity);
                if (existing != null)
                {
                    if (!existing.OwnsLook(e.look)) existing.outfits.Add(e);
                    // The source character is the default, independently of outfit ordering.
                    if (BaseId(e.look) == identity) { existing.look = (string[])e.look.Clone(); existing.previewYaw = e.previewYaw; }
                    continue;
                }
                var m = new BaseModel
                {
                    id = identity, label = e.IdentityDisplay, species = e.species, look = (string[])e.look.Clone(), previewYaw = e.previewYaw,
                    family = CreatorLookOrder.FamilyOf(e.look.Length > 1 ? e.look[1] : null),
                };
                m.outfits.Add(e);
                m.portrait = portraits?.Invoke(m.id);
                m.data = catalog?.bases?.FirstOrDefault(b => b != null && b.id == m.id);
                result.Add(m);
            }
            return result;
        }

        /// <summary>The model's starting spec: its authored look plus its default parts and tints (the first entry per slot/region).</summary>
        public static AvatarSpec DefaultSpec(BaseModel m, CustomizerCatalog catalog = null)
        {
            var s = new AvatarSpec { baseId = m?.id, legacyLook = (string[])m?.look?.Clone() };
            if (m?.data == null) return s;
            foreach (var slot in m.data.parts.Select(p => p.slot).Distinct())
                s.parts[slot.ToString()] = m.data.parts.First(p => p.slot == slot).id;
            foreach (var r in m.data.regions)
            {
                var sw = r.defaultSwatch ?? catalog?.FindPalette(r.paletteId)?.swatches.FirstOrDefault()?.id;
                if (!string.IsNullOrEmpty(sw)) s.tints[r.region.ToString()] = sw;
            }
            return s;
        }

        /// <summary>Migrates an outfit-as-person draft without changing any authored look ID or granting foreign art.</summary>
        public static AvatarSpec RestoreSpec(BaseModel model, AvatarSpec saved, string[] legacyLook, CustomizerCatalog catalog = null)
        {
            if (model == null) return null;
            if (saved != null && saved.IsModular) return saved.baseId == model.id ? saved.Clone() : null;
            var look = saved?.legacyLook ?? legacyLook;
            if (look == null || !model.OwnsLook(look)) return null;
            return new AvatarSpec { baseId = model.id, legacyLook = (string[])look.Clone() };
        }

        /// <summary>The model's options in one category (before the art check).</summary>
        public static List<CustomizerOption> AllOptions(BaseModel m, LookCategory category, CustomizerCatalog catalog)
        {
            var list = new List<CustomizerOption>();
            var palettes = CharacterPaletteCatalog.Load();
            if (m != null && palettes?.FindBase(m.id) != null)
            {
                if (category == LookCategory.Hair)
                {
                    foreach (var style in palettes.FindBase(m.id).hairStyles)
                        list.Add(new CustomizerOption { category = category, kind = OptionKind.HairStyle,
                            key = m.id, value = style.id, label = style.labelKey, thumb = style.thumbnail });
                    return list;
                }
                string region = category switch { LookCategory.SkinTone => "skin", LookCategory.HairColor => "hair",
                    LookCategory.Top => "top", LookCategory.Bottom => "bottom", LookCategory.Shoes => "shoes", _ => null };
                if (region != null)
                {
                    list.Add(new CustomizerOption { category = category, kind = OptionKind.Palette, key = region,
                        value = null, label = "ob.color.original" });
                    foreach (var sw in palettes.PaletteFor(region).swatches)
                        list.Add(new CustomizerOption { category = category, kind = OptionKind.Palette, key = region,
                            value = sw.id, label = "ob.color." + sw.id, hasSwatch = true, swatch = sw.color });
                    return list;
                }
            }
            if (category == LookCategory.Outfit)
            {
                if (m == null || m.outfits.Count < 2) return list;
                foreach (var outfit in m.outfits)
                    list.Add(new CustomizerOption { category = category, kind = OptionKind.Outfit, key = m.id,
                        value = string.Join("|", outfit.look), label = outfit.Display,
                        thumb = Portrait(BaseId(outfit.look)), authoredLook = (string[])outfit.look.Clone() });
                return list;
            }
            var d = m?.data;
            if (d == null) return list;
            void Parts(PartSlot slot)
            {
                foreach (var p in d.parts.Where(p => p.slot == slot))
                    list.Add(new CustomizerOption { category = category, kind = OptionKind.Part, key = slot.ToString(), value = p.id, label = p.label, thumb = p.thumb });
            }
            void Tints(TintRegion region, bool prefix)
            {
                var r = d.regions.FirstOrDefault(x => x.region == region);
                var pal = r == null ? null : catalog?.FindPalette(r.paletteId) ?? (r.paletteId == CustomizerCatalog.DefaultSkinPaletteId ? CustomizerCatalog.DefaultSkinPalette() : null);
                if (pal == null) return;
                foreach (var sw in pal.swatches)
                    list.Add(new CustomizerOption
                    {
                        category = category, kind = OptionKind.Tint, key = region.ToString(), value = sw.id,
                        label = prefix ? region + " · " + sw.label : sw.label, hasSwatch = true, swatch = sw.color,
                    });
            }
            switch (category)
            {
                case LookCategory.SkinTone: Tints(TintRegion.Skin, false); break;
                case LookCategory.Hair: Parts(PartSlot.Hair); break;
                case LookCategory.HairColor: Tints(TintRegion.Hair, false); break;
                case LookCategory.Top: Parts(PartSlot.Top); break;
                case LookCategory.Bottom: Parts(PartSlot.Bottom); break;
                case LookCategory.Shoes: Parts(PartSlot.Shoes); break;
                case LookCategory.Colors:
                    Tints(TintRegion.Top, true); Tints(TintRegion.Bottom, true); Tints(TintRegion.Shoes, true); Tints(TintRegion.Accent, true);
                    break;
                case LookCategory.Accessories:
                    foreach (var socket in d.accessories.Select(a => a.socket).Distinct())
                    {
                        list.Add(new CustomizerOption { category = category, kind = OptionKind.Accessory, key = socket.ToString(), value = null, label = socket + " · none" });
                        foreach (var a in d.accessories.Where(a => a.socket == socket))
                            list.Add(new CustomizerOption { category = category, kind = OptionKind.Accessory, key = socket.ToString(), value = a.id, label = a.label, thumb = a.thumb });
                    }
                    break;
            }
            return list;
        }

        /// <summary>Options whose result resolves to real art.</summary>
        public static List<CustomizerOption> Options(BaseModel m, LookCategory category, AvatarSpec current, CustomizerCatalog catalog, Func<AvatarSpec, bool> resolves) =>
            AllOptions(m, category, catalog).Where(o => resolves == null || resolves(o.ApplyTo(current))).ToList();

        /// <summary>Categories with at least two distinct resolvable results, in rail order. Empty means: skip Customize.</summary>
        public static List<LookCategory> Visible(BaseModel m, AvatarSpec current, CustomizerCatalog catalog, Func<AvatarSpec, bool> resolves)
        {
            var visible = new List<LookCategory>();
            if (m == null || current == null) return visible;
            foreach (var c in RailOrder)
            {
                var distinct = new HashSet<string> { current.Key() };
                foreach (var o in Options(m, c, current, catalog, resolves)) distinct.Add(o.ApplyTo(current).Key());
                if (distinct.Count >= 2) visible.Add(c);
            }
            return visible;
        }

        /// <summary>
        /// The contract Look [skinTone, headId, hairId, topId, bottomId, shoesId, accId]. A whole authored look maps to itself.
        /// A modular spec maps: skinTone ← Skin tint; head/hair/top/bottom/shoes ← parts, with a region tint as a variant id
        /// (`top_hoodie__purple`); accId ← the first filled socket (face, head, neck, back, shoulder, hands) or "acc_none".
        /// </summary>
        public static string[] ToContract(AvatarSpec s)
        {
            var legacy = s?.legacyLook != null && s.legacyLook.Length == 7 ? s.legacyLook : new string[7];
            if (s == null || !s.IsModular) return (string[])legacy.Clone();
            string Variant(PartSlot slot, TintRegion? region, int legacyIndex)
            {
                var part = s.Get(slot) ?? legacy[legacyIndex];
                var tint = region.HasValue ? s.Get(region.Value) : null;
                return string.IsNullOrEmpty(part) || string.IsNullOrEmpty(tint) ? part : part + "__" + tint;
            }
            var acc = AccessoryPriority.Select(s.Get).FirstOrDefault(a => !string.IsNullOrEmpty(a)) ?? (legacy[6] ?? "acc_none");
            return new[]
            {
                s.Get(TintRegion.Skin) ?? legacy[0], s.Get(PartSlot.Head) ?? legacy[1] ?? s.baseId,
                Variant(PartSlot.Hair, TintRegion.Hair, 2), Variant(PartSlot.Top, TintRegion.Top, 3), Variant(PartSlot.Bottom, TintRegion.Bottom, 4),
                Variant(PartSlot.Shoes, TintRegion.Shoes, 5), acc,
            };
        }

        /// <summary>What the 7 ids cannot carry (base id, accent tint, all sockets): the proposed optional `Look.extras`. Not sent today.</summary>
        public static JObject Extras(AvatarSpec s)
        {
            if (s == null || !s.IsModular) return null;
            var o = new JObject { ["baseId"] = s.baseId };
            if (s.Get(TintRegion.Accent) is string accent) o["accentTint"] = accent;
            if (s.accessories.Count > 0) o["accessories"] = JObject.FromObject(s.accessories);
            return o;
        }

        /// <summary>Whole authored looks require their exact registry entry. Local modular art uses an explicit binding.</summary>
        public static bool Resolves(AvatarSpec s, BaseModel m) => s != null && m != null && s.baseId == m.id && !s.IsModular &&
            (m.outfits.Count == 0 || m.OwnsLook(ToContract(s))) && ResolvesLook(ToContract(s));

        public static bool ResolvesLook(string[] look)
        {
            if (look == null || look.Length != 7) return false;
            var entry = CharacterRegistry.Instance?.ForLook(look);
            return entry?.prefab != null && entry.key == string.Join("|", look) || CharacterPaletteResolver.TryResolve(look, out _);
        }

        // These are authored character categories, not gender guessed from a photo. Unspecified masked characters,
        // the robot and creatures remain available under All/Others without forcing a male/female assignment.
        public static bool MatchesBrowse(BaseModel model, CharacterBrowse browse)
        {
            if (model == null) return false;
            if (browse == CharacterBrowse.All) return true;
            var group = model.id switch
            {
                "head_snow" or "head_ubc_m" => CharacterBrowse.Men,
                "head_rain" or "head_ubc_f" or "head_hero_assassin" => CharacterBrowse.Women,
                _ => CharacterBrowse.Others,
            };
            return group == browse;
        }

        public static Texture2D Illustration(string baseId) => string.IsNullOrEmpty(baseId) ? null :
            Resources.Load<Texture2D>("UI/Customizer/Illustrations/" + Safe(baseId));

        /// <summary>Baked base-model portraits (Resources/UI/Customizer/Portraits/&lt;baseId&gt;.png), null while missing.</summary>
        public static Texture2D Portrait(string baseId) =>
            string.IsNullOrEmpty(baseId) ? null : Resources.Load<Texture2D>("UI/Customizer/Portraits/" + Safe(baseId));

        public static string Safe(string id)
        {
            var chars = (id ?? "").ToCharArray();
            for (int i = 0; i < chars.Length; i++) if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-') chars[i] = '_';
            return new string(chars);
        }

        public static string CategoryKey(LookCategory c) => "ob.cat." + c.ToString().ToLowerInvariant();

        public static string CategoryIcon(LookCategory c) => c switch
        {
            LookCategory.SkinTone => "face", LookCategory.Hair => "emote", LookCategory.HairColor => "dream",
            LookCategory.Outfit => "shop", LookCategory.Top => "shop", LookCategory.Bottom => "shop", LookCategory.Shoes => "stand",
            LookCategory.Accessories => "npc", LookCategory.Colors => "dream", _ => "body",
        };
    }
}
