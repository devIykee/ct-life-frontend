#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using CTLife.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace CTLife.EditorTools
{
    /// <summary>Real existing creator transactions using accepted local art. No DemoCatalog, synthetic resolver or submission.</summary>
    public static class CreatorLocalArtCapture
    {
        static string Arg(string key, string fallback = null) => UbcImportProbe.Argument(key, fallback);
        static void Need(bool value, string message) => UbcImportProbe.Require(value, message);
        static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
        static AvatarSpec Spec(CharacterCreatorScreen s) => Field<AvatarSpec>(s, "_spec");
        static AvatarView Actor(CharacterCreatorScreen s) => Field<AvatarView>(Field<AvatarPreview>(s, "_preview"), "_avatar");
        static bool Submittable(CharacterCreatorScreen s) => (bool)typeof(CharacterCreatorScreen).GetProperty("CanSubmitLook", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(s);
        static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);

        public static void Preflight()
        {
            Need(Application.isBatchMode && !EditorApplication.isPlayingOrWillChangePlaymode, "Use a fresh batch Editor for this screen proof.");
            var scene = SceneManager.GetActiveScene();
            Need(SceneManager.sceneCount == 1 && scene.IsValid() && scene.isLoaded && string.IsNullOrEmpty(scene.path) &&
                !scene.isDirty && scene.rootCount == 0, "Preserve existing scenes: this proof requires the empty fresh batch startup.");
            Need(!Directory.Exists(Arg("-ctlife-captureOut")), "Use a fresh evidence directory.");
            ReadAcceptedPins();
        }

        static JObject ReadAcceptedPins()
        {
            string pinsPath = Arg("-ctCreatorArtPins"), pinsSha = Arg("-ctCreatorArtPinsSha");
            Need(UbcImportProbe.Sha(pinsPath) == pinsSha, "Exact accepted art pin file changed.");
            var pins = JObject.Parse(File.ReadAllText(pinsPath));
            string receiptPath = (string)pins["receipt"], root = (string)pins["assetRoot"];
            Need(UbcImportProbe.Sha(receiptPath) == (string)pins["receiptSHA256"], "Accepted native receipt changed.");
            var receipt = JObject.Parse(File.ReadAllText(receiptPath));
            Need((bool?)receipt["pass"] == true && (string)receipt["assetRoot"] == root &&
                (bool?)receipt["completeDefaultRestSurfaceMatchesMerged"] == true && (bool?)receipt["nativeImportOnly"] == false &&
                receipt["protectedDrift"] is JArray drift && drift.Count == 0 && receipt["cleanupErrors"] is JArray errors && errors.Count == 0,
                "A successful complete UBC import/assembly/motion receipt is required, not a partial catalog.");
            Need((int?)receipt["hairSwap"]?["defaultTriangles"] == 5331 && (int?)receipt["hairSwap"]?["buzzedTriangles"] == 5199 &&
                (bool?)receipt["hairSwap"]?["oldHairAbsent"] == true, "Accepted hair swap evidence is absent.");
            Need(root != null && root.StartsWith("Assets/CTLifeCandidates/UBCImportProbe", StringComparison.Ordinal) && !root.Contains(".."), "Unexpected accepted asset root.");
            var files = (JObject)pins["files"];
            Need(files != null && files.Count > 0 && files[root + "/PreviewOnlyCatalog.asset"] != null, "Catalog asset pins missing.");
            foreach (var p in files.Properties()) Need(File.Exists(p.Name) && UbcImportProbe.Sha(p.Name) == (string)p.Value, "Accepted asset bytes changed: " + p.Name);
            if (pins["tintReceipt"] is JValue tint && tint.Type == JTokenType.String)
            {
                Need(UbcImportProbe.Sha((string)tint) == (string)pins["tintReceiptSHA256"], "Tint receipt changed.");
                var r = JObject.Parse(File.ReadAllText((string)tint));
                Need((bool?)r["pass"] == true && (bool?)r["visibleTintChangesDetected"] == true &&
                    (string)r["sourceCatalog"] == root + "/PreviewOnlyCatalog.asset" && (string)r["catalog"] == (string)pins["catalog"] &&
                    r["protectedDrift"] is JArray td && td.Count == 0 && r["cleanupErrors"] is JArray te && te.Count == 0,
                    "Optional tint requires its own successful matching native receipt.");
            }
            else Need((string)pins["catalog"] == root + "/PreviewOnlyCatalog.asset", "Unproved tint catalog is not allowed.");
            Need(files[(string)pins["catalog"]] != null, "Selected catalog is not byte-pinned.");
            return pins;
        }

        public static async Task Run(Func<string, float, Task> shot, Func<float, Task> wait, Action<bool, string> expect,
            Action<string> step, JObject report)
        {
            var pins = ReadAcceptedPins(); report["acceptedArtPins"] = pins.DeepClone();
            report["scope"] = "Existing Character/Customize in actual Play mode with real local art; no backend/device/human or complete cast approval";
            var files = ((JObject)pins["files"]).Properties().ToDictionary(p => p.Name, p => (string)p.Value);
            var key = "ctlife.creator.draft.local";
            if (Services.TryGet<PlayerProfile>(out var profile) && !string.IsNullOrEmpty(profile.UserId)) key = "ctlife.creator.draft." + profile.UserId;
            bool hadDraft = PlayerPrefs.HasKey(key), hadSeed = PlayerPrefs.HasKey("ctlife.creator.lookSeed");
            string draftBefore = PlayerPrefs.GetString(key); int seedBefore = PlayerPrefs.GetInt("ctlife.creator.lookSeed");
            var screens = new List<CharacterCreatorScreen>(); var owned = new List<Object>();
            var initialViews = AvatarView.All.ToArray(); var checks = new JArray(); report["transactions"] = checks;
            var motionField = typeof(DeviceFeel).GetField("_reducedMotionOverride", BindingFlags.Static | BindingFlags.NonPublic);
            object priorMotion = motionField.GetValue(null);
            int submits = 0;
            try
            {
                PlayerPrefs.DeleteKey(key); CreatorLookOrder.SetInstallSeed(0); DeviceFeel.OverrideReducedMotion(true);
                var source = AssetDatabase.LoadAssetAtPath<ExactModularCatalog>((string)pins["catalog"]);
                Need(source != null, "Accepted catalog cannot be loaded.");
                // Clone only catalogue data for fault injection; never mutate imported prefab/material/mesh objects.
                var art = Object.Instantiate(source); owned.Add(art);
                art.parts = source.parts.Select(p => new ModularPart { id = p.id, baseId = p.baseId, slot = p.slot,
                    renderers = p.renderers.Select(d => new ModularRenderer { prefab = d.prefab, roles = d.roles, tints = d.tints }).ToArray() }).ToArray();
                var ui = MakeOptions(art, pins); owned.Add(ui);
                var defaultSpec = Default(art);
                var binding = new CreatorLocalArtBinding(art, ui, defaultSpec, "Athletic base (M)", 'm', 0);
                CharacterCreatorScreen Open()
                {
                    var s = new CharacterCreatorScreen(Services.Get<ConfigStore>().Registries, _ => submits++, serverLookConfirmed: _ => true);
                    s.ConfigureLocalArt(new[] { binding }); screens.Add(s); s.Show(); return s;
                }
                void Check(bool value, string message)
                { expect(value, message); checks.Add(new JObject { ["check"] = message, ["pass"] = value, ["frame"] = Time.frameCount }); Need(value, message); }
                var screen = Open(); await wait(.4f);
                int shipped = CreatorLookCatalog.Available().Count;
                var models = Field<List<BaseModel>>(screen, "_models"); int local = models.FindIndex(m => m.id == art.baseId);
                Check(models.Count == shipped + 1 && local == shipped, "existing roster retained and exact local base appended");
                screen.PickForAutomation(local); await wait(.35f);
                CheckBody(screen, art, false); Check(Spec(screen).baseId == art.baseId, "actual model pick commits the local base");
                Check(!Submittable(screen) && screen.Flow.Draft.look.All(string.IsNullOrEmpty), "local IDs never gain submission even with permissive callback");
                await shot("30-character-local-default", .25f);
                screen.SetFaceForAutomation(true); await shot("31-character-local-face", .25f); screen.SetFaceForAutomation(false);
                screen.PressCta(); await wait(.2f);
                Check(screen.Step == CreatorStep.Customize, "real hair options make Customize reachable");
                var cats = (List<LookCategory>)typeof(CharacterCreatorScreen).GetMethod("VisibleCategories", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(screen, null);
                Check(cats.SequenceEqual(art.palettes.Length == 0 ? new[] { LookCategory.Hair } : new[] { LookCategory.SkinTone, LookCategory.Hair, LookCategory.HairColor }), "only proven categories are visible");
                screen.SelectCategoryForAutomation(cats.IndexOf(LookCategory.Hair)); screen.PickForAutomation(1); await wait(.35f);
                CheckBody(screen, art, true); Check(Spec(screen).Get(PartSlot.Hair) == "hair-buzzed", "actual option replaces default hair without duplicate geometry");
                await shot("32-customize-real-buzzed", .25f);
                var beforeActor = Actor(screen); string beforeSpec = Spec(screen).Key(), beforeDraft = PlayerPrefs.GetString(key);
                Invoke(screen, "PickOption", new CustomizerOption { kind = OptionKind.Part, key = "Hair", value = "missing-part", category = LookCategory.Hair });
                await wait(.15f);
                Check(Actor(screen) == beforeActor && Spec(screen).Key() == beforeSpec && PlayerPrefs.GetString(key) == beforeDraft,
                    "rejected missing-part option retains actor/spec/draft and reports no accepted change");
                Check(screen.Element.Q<Label>(className: "ob-creator-error").text == Strings.T("common.unavailable"), "failed selection is visible");
                screen.ShowError("");
                // Exercise failure after assembly: the candidate cannot meet the requested preview idle.
                var preview = Field<AvatarPreview>(screen, "_preview"); var cameraPosition = preview.Camera.transform.position;
                preview.SetIdle("missing-preview-idle");
                try
                {
                    Invoke(screen, "PickOption", new CustomizerOption { kind = OptionKind.Part, key = "Hair", value = "hair-default", category = LookCategory.Hair });
                    await wait(.15f);
                    Check(Actor(screen) == beforeActor && Spec(screen).Key() == beforeSpec && PlayerPrefs.GetString(key) == beforeDraft &&
                        preview.Camera.transform.position == cameraPosition, "post-assembly failure rolls back actor, framing, spec and draft");
                    var previewRoot = Field<GameObject>(preview, "_root");
                    Check(previewRoot.GetComponentsInChildren<AvatarView>(true).Length == 1, "failed replacement does not strand a partial preview actor");
                }
                finally { preview.SetIdle("loco_idle"); screen.ShowError(""); }
                if (art.palettes.Length > 0)
                {
                    screen.SelectCategoryForAutomation(cats.IndexOf(LookCategory.SkinTone)); screen.PickForAutomation(9); await wait(.25f);
                    Check(Spec(screen).Get(TintRegion.Skin) == "skin_10", "actual skin selection commits after real material assembly");
                    screen.SelectCategoryForAutomation(cats.IndexOf(LookCategory.HairColor)); screen.PickForAutomation(5); await wait(.25f);
                    Check(Spec(screen).Get(TintRegion.Hair) == "pink", "actual hair-colour selection commits after real material assembly");
                    CheckTint(screen, art, "Skin", "skin_10"); CheckTint(screen, art, "Hair", "pink");
                    await shot("33-customize-real-skin-hair", .25f);
                }
                string savedSpec = Spec(screen).Key(); var savedMeshes = Actor(screen).GetComponentsInChildren<SkinnedMeshRenderer>().Select(r => r.sharedMesh).ToArray();
                screen.Close(); await wait(.15f); screen = Open(); await wait(.35f);
                Check(Spec(screen).Key() == savedSpec && savedMeshes.SequenceEqual(Actor(screen).GetComponentsInChildren<SkinnedMeshRenderer>().Select(r => r.sharedMesh)), "reopen restores the exact local spec and actual meshes");
                Check(screen.Step == CreatorStep.Customize && !Submittable(screen), "restored local Customize remains reachable and unregistered");
                await shot("34-local-draft-restored", .25f);
                // A source disappears after options were built: selecting it must not replace the actor or saved selection.
                screen.JumpForAutomation(CreatorStep.Look); screen.PickForAutomation(0); await wait(.2f);
                Check(!Spec(screen).IsModular && Submittable(screen), "switching back to a real whole look retains its existing server capability check");
                var legacyActor = Actor(screen); var legacySpec = Spec(screen).Key(); var legacyDraft = PlayerPrefs.GetString(key);
                var hair = art.parts.Single(p => p.id == "hair-default").renderers[0]; var prefab = hair.prefab;
                try
                {
                    hair.prefab = null; screen.PickForAutomation(local); await wait(.15f);
                    Check(Actor(screen) == legacyActor && Spec(screen).Key() == legacySpec && PlayerPrefs.GetString(key) == legacyDraft,
                        "failed base pick preserves the prior actor, selection and draft");
                }
                finally { hair.prefab = prefab; }
                screen.PickForAutomation(local); await wait(.2f); CheckBody(screen, art, false);
                screen.Close(); await wait(.15f);
                var corrupted = JObject.Parse(PlayerPrefs.GetString(key)); corrupted["spec"]["parts"]["Hair"] = "missing-part";
                PlayerPrefs.SetString(key, corrupted.ToString()); screen = Open(); await wait(.25f);
                Check(Spec(screen).Key() == defaultSpec.Key(), "invalid saved part restores a complete default, not a claimed missing selection");
                CheckBody(screen, art, false);
                Check(screen.Element.Q<Label>(className: "ob-creator-error").text == Strings.T("common.unavailable"), "invalid saved local choice reports unavailable");
                screen.Close(); await wait(.15f);
                screen = new CharacterCreatorScreen(Services.Get<ConfigStore>().Registries, _ => submits++, serverLookConfirmed: _ => true);
                screens.Add(screen); screen.Show(); await wait(.2f);
                Check(!Spec(screen).IsModular && Spec(screen).baseId != art.baseId && Actor(screen) != null,
                    "missing local binding restores an actual ordinary roster actor, not an unavailable local ID");
                Check(screen.Element.Q<Label>(className: "ob-creator-error").text == Strings.T("common.unavailable"), "missing saved local base is reported");
                Check(submits == 0, "screen proof sent zero character submissions");
                report["broaderCastComplete"] = false; report["humanApproval"] = false;
            }
            finally
            {
                var cleanup = new JArray();
                foreach (var s in screens.AsEnumerable().Reverse()) try { s.Close(); } catch (Exception e) { cleanup.Add("screen close: " + e.Message); }
                foreach (var o in owned) try { if (o != null) Object.Destroy(o); } catch (Exception e) { cleanup.Add("owned metadata: " + e.Message); }
                try { await wait(.1f); } catch (Exception e) { cleanup.Add("deferred cleanup observation: " + e.Message); }
                try
                {
                    if (hadDraft) PlayerPrefs.SetString(key, draftBefore); else PlayerPrefs.DeleteKey(key);
                    if (hadSeed) PlayerPrefs.SetInt("ctlife.creator.lookSeed", seedBefore); else PlayerPrefs.DeleteKey("ctlife.creator.lookSeed");
                    DeviceFeel.OverrideReducedMotion((bool?)priorMotion); PlayerPrefs.Save();
                }
                catch (Exception e) { cleanup.Add("preference restoration: " + e.Message); }
                bool prefs = PlayerPrefs.HasKey(key) == hadDraft && (!hadDraft || PlayerPrefs.GetString(key) == draftBefore) &&
                    PlayerPrefs.HasKey("ctlife.creator.lookSeed") == hadSeed && (!hadSeed || PlayerPrefs.GetInt("ctlife.creator.lookSeed") == seedBefore) && Equals(motionField.GetValue(null), priorMotion);
                report["preferencesRestored"] = prefs; expect(prefs, "local-art proof restores prior draft/seed/motion override");
                var drift = files.Where(p => !File.Exists(p.Key) || UbcImportProbe.Sha(p.Key) != p.Value).Select(p => p.Key).ToArray();
                report["artDrift"] = new JArray(drift); expect(drift.Length == 0, "local-art proof preserves accepted input bytes");
                bool views = initialViews.Length == AvatarView.All.Count && initialViews.All(AvatarView.All.Contains);
                report["avatarRegistryRestored"] = views; expect(views, "preview actors do not enter or remain in the world registry");
                report["localArtCleanupErrors"] = cleanup; expect(cleanup.Count == 0, "local-art owned cleanup completed without errors");
            }
        }

        static AvatarSpec Default(ExactModularCatalog c)
        {
            var spec = new AvatarSpec { baseId = c.baseId };
            foreach (var id in new[] { "head", "hair-default", "top", "bottom", "feet" })
            { var p = c.parts.Single(x => x.id == id); spec.parts[p.slot.ToString()] = id; }
            return spec;
        }
        static CustomizerCatalog MakeOptions(ExactModularCatalog c, JObject pins)
        {
            var ui = ScriptableObject.CreateInstance<CustomizerCatalog>(); var b = new CustomizerCatalog.Base { id = c.baseId }; ui.bases.Add(b);
            foreach (var p in c.parts)
                b.parts.Add(new CustomizerCatalog.Part { id = p.id, slot = (PartSlot)Enum.Parse(typeof(PartSlot), p.slot.ToString()),
                    label = p.id == "hair-default" ? "Simple parted" : p.id == "hair-buzzed" ? "Buzzed" : p.id });
            if (c.palettes.Length > 0)
            {
                var paletteFile = Arg("-ctCreatorPalettePlan"); Need(UbcImportProbe.Sha(paletteFile) == (string)pins["palettePlanSHA256"], "Exact tint labels/palette mapping missing.");
                var plan = JObject.Parse(File.ReadAllText(paletteFile));
                foreach (var p in c.palettes)
                {
                    var palette = new CustomizerCatalog.Palette { id = "local-" + p.region };
                    foreach (var sw in p.swatches)
                    { var original = ((JArray)plan["palettes"][p.region]).Single(x => (string)x["id"] == sw.id); palette.swatches.Add(new CustomizerCatalog.Swatch { id = sw.id, label = (string)original["label"], color = sw.color }); }
                    ui.palettes.Add(palette); b.regions.Add(new CustomizerCatalog.Region { region = (TintRegion)Enum.Parse(typeof(TintRegion), p.region), paletteId = palette.id });
                }
            }
            return ui;
        }
        static void CheckBody(CharacterCreatorScreen s, ExactModularCatalog c, bool buzzed)
        {
            var skins = Actor(s).GetComponentsInChildren<SkinnedMeshRenderer>();
            var hair = c.parts.Single(p => p.id == (buzzed ? "hair-buzzed" : "hair-default")).renderers.Single().prefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
            var other = c.parts.Single(p => p.id == (buzzed ? "hair-default" : "hair-buzzed")).renderers.Single().prefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
            Need(skins.Length == 7 && skins.Sum(r => r.sharedMesh.triangles.Length / 3) == (buzzed ? 5199 : 5331) &&
                skins.Count(r => r.sharedMesh == hair) == 1 && skins.All(r => r.sharedMesh != other), "Actual screen preview does not match requested complete body/hair.");
            foreach (var p in c.parts.Where(p => p.slot != ModularSlot.Hair)) foreach (var d in p.renderers)
                Need(skins.Count(r => r.sharedMesh == d.prefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh) == 1, "Body part was lost during preview selection.");
        }
        static void CheckTint(CharacterCreatorScreen s, ExactModularCatalog c, string region, string swatch)
        {
            Color expected = c.palettes.Single(p => p.region == region).swatches.Single(sw => sw.id == swatch).color.linear;
            foreach (var d in c.parts.SelectMany(p => p.renderers).Where(d => d.tints.Any(t => t.region == region)))
            {
                var mesh = d.prefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
                var skin = Actor(s).GetComponentsInChildren<SkinnedMeshRenderer>().SingleOrDefault(r => r.sharedMesh == mesh); if (skin == null) continue;
                var binding = d.tints.Single(t => t.region == region); Color actual = skin.sharedMaterials[binding.materialIndex].GetColor(binding.colorProperty);
                Need(Mathf.Abs(actual.r-expected.r)<.00002f && Mathf.Abs(actual.g-expected.g)<.00002f && Mathf.Abs(actual.b-expected.b)<.00002f && actual.a==expected.a, "Actual screen material does not carry selected colour.");
            }
        }
    }
}
#endif
