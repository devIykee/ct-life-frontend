using System;
using UnityEngine;

namespace CTLife.World
{
    /// <summary>
    /// Job uniforms (2026-10-25/26): the Rekt Burger crew variant of the wearer's own cast look ("uniform:&lt;lookId&gt;_rekt_crew")
    /// is worn while the local player is on shift, and by anyone whose look carries the reward_rekt_burger_uniform cosmetic. Only
    /// the cast families (Snow m / Rain f) have crew variants; other rigs keep their own look.
    /// </summary>
    public static class Uniforms
    {
        public const string RektCosmetic = "reward_rekt_burger_uniform";

        /// <summary>m (Snow family), f (Rain family) or null, from the look's palette base.</summary>
        public static string Family(string[] look)
        {
            if (look == null || look.Length < 2 || string.IsNullOrEmpty(look[1])) return null;
            var b = CharacterPaletteCatalog.Load()?.FindBase(look[1]);
            string person = b == null ? null : string.IsNullOrEmpty(b.personId) ? b.id : b.personId;
            return person == "head_snow" ? "m" : person == "head_rain" ? "f" : null;
        }

        public static bool WearsCosmetic(string[] look) => look != null && Array.IndexOf(look, RektCosmetic) >= 0;

        /// <summary>The cast look a player's look is built on ("player_m_kol", "snow_everyday"), from its palette base; null for other rigs.</summary>
        public static string BaseLookId(string[] look)
        {
            if (look == null || look.Length < 2 || string.IsNullOrEmpty(look[1])) return null;
            var b = CharacterPaletteCatalog.Load()?.FindBase(look[1]);
            if (b == null) return null;
            if (b.id == "head_snow") return "snow_everyday";
            if (b.id == "head_rain") return "rain_breeze";
            return b.id.StartsWith("head_player_", StringComparison.Ordinal) ? b.id.Substring(5) : null;
        }

        /// <summary>
        /// The crew variant of this player's own cast look (their head, face, skin tone, hair colour and beard; only the clothes
        /// change), or null (another rig, or not imported). Review 2026-10-26: identity first.
        /// </summary>
        public static CharacterRegistry.Entry RektCrew(string[] look)
        {
            var id = BaseLookId(look);
            if (id == null) return null;
            var e = CharacterRegistry.Instance?.ForLookKey(CharacterRegistryKeys.Uniform + id + "_rekt_crew");
            return e?.prefab != null ? e : null;
        }

        /// <summary>The wearer's chosen skin / hair swatches onto the worn crew model (the base look's tones are already baked).</summary>
        public static void CarryPalette(GameObject model, CharacterRegistry.Entry crew, string[] look)
        {
            if (model == null || crew == null || !CharacterPaletteResolver.TryResolve(look, out var sel) || !sel.HasChanges) return;
            bool skin = sel.colors.TryGetValue("skin", out var skinColor), hair = sel.colors.TryGetValue("hair", out var hairColor);
            if (!skin && !hair) return;
            var binding = UniformPalettes.Instance?.Find(crew.key.Substring(CharacterRegistryKeys.Uniform.Length));
            if (binding?.maskedMaterial == null || binding.sourceMaterial == null) return;
            var material = new Material(binding.maskedMaterial) { name = binding.lookId + "_wearer_palette" };
            material.SetFloat("_Cull", binding.sourceMaterial.GetFloat("_Cull"));
            if (skin) material.SetVector("_SkinTint", Lin(skinColor));
            if (hair)
            {
                material.SetVector("_HairTint", Lin(hairColor));
                var linear = hairColor.linear;   // the creator's brow rule: darker and less saturated than the hair
                float luminance = Vector3.Dot(new Vector3(linear.r, linear.g, linear.b), new Vector3(.2126f, .7152f, .0722f));
                float scale = Mathf.Min(luminance * .55f, .075f) / Mathf.Max(luminance, .0001f);
                var brow = Color.Lerp(new Color(luminance, luminance, luminance), linear, .6f) * scale;
                material.SetVector("_BrowsTint", new Vector4(brow.r, brow.g, brow.b, 1f));
            }
            model.AddComponent<CharacterPaletteResources>().Own(material);
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.sharedMaterial == binding.sourceMaterial) r.sharedMaterial = material;
        }

        private static Vector4 Lin(Color c) { var l = c.linear; return new Vector4(l.r, l.g, l.b, 1f); }

        /// <summary>At spawn: a look that wears the uniform cosmetic resolves straight to the crew look (local and remote).</summary>
        public static CharacterRegistry.Entry AtSpawn(string[] look) => WearsCosmetic(look) ? RektCrew(look) : null;
    }

    /// <summary>
    /// Puts the crew uniform on the local player while on shift and takes it off after (checked twice a second, so a respawn
    /// in another venue gets it back). Started by City Life with the Rekt Burger service.
    /// </summary>
    public sealed class UniformDirector : MonoBehaviour
    {
        private static UniformDirector _instance;
        public static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("CTLife.UniformDirector");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<UniformDirector>();
        }

        /// <summary>The local player's own look (the profile's), for the family; set by City Life.</summary>
        public static Func<string[]> LocalLook;
        private float _next;

        private void OnEnable() => RektBurgerHooks.ShiftChanged += OnShift;
        private void OnDisable() => RektBurgerHooks.ShiftChanged -= OnShift;
        private void OnShift(bool _) => _next = 0f;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            var me = VenueSession.Current != null ? VenueSession.Current.LocalPlayer : null;
            var view = me != null ? me.GetComponent<AvatarView>() : null;
            if (view == null) return;
            var look = LocalLook?.Invoke();
            bool want = RektBurgerHooks.OnShift && !Uniforms.WearsCosmetic(look);   // the cosmetic already spawned it
            var worn = AvatarOutfit.Worn(view);
            if (want)
            {
                var crew = Uniforms.RektCrew(look);
                if (crew != null && worn != crew.key) AvatarOutfit.Wear(view, crew, model => Uniforms.CarryPalette(model, crew, look));
            }
            else if (worn != null && worn.StartsWith(CharacterRegistryKeys.Uniform, StringComparison.Ordinal)) AvatarOutfit.Restore(view);
        }
    }

    public static class CharacterRegistryKeys { public const string Uniform = "uniform:"; }
}
