using System.IO;
using System.Text.RegularExpressions;
using CTLife.UI.DS;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>One brand table: real brands come from contract/ids/brands.json (Resources/Contract/brands.json); the USS
    /// brand classes must carry exactly its ui_tokens (USS cannot read JSON, so this test is the link).</summary>
    public sealed class BrandTableTests
    {
        private static string Assets => Application.dataPath;

        [Test]
        public void UssBrandColours_MatchBrandsJson()
        {
            var brands = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(Assets, "Resources/Contract/brands.json")))["brands"];
            var uss = File.ReadAllText(Path.Combine(Assets, "Resources/UI/DS/ds-tokens.uss"));
            int checkedBrands = 0;
            foreach (var p in brands.Properties())
            {
                var b = (JObject)p.Value;
                var id = (string)b["ui_id"];
                if (string.IsNullOrEmpty(id)) continue;
                var block = Regex.Match(uss, @"\.brand-" + Regex.Escape(id) + @"\s*\{(.*?)\}", RegexOptions.Singleline);
                Assert.IsTrue(block.Success, "ds-tokens.uss has no .brand-" + id);
                string Var(string name) => Regex.Match(block.Groups[1].Value, "--" + name + @":\s*(#[0-9A-Fa-f]{6})").Groups[1].Value.ToUpperInvariant();
                var t = (JObject)b["ui_tokens"];
                Assert.AreEqual(((string)t["title"]).ToUpperInvariant(), Var("b-title"), id + " title");
                Assert.AreEqual(((string)t["primary"]).ToUpperInvariant(), Var("b-primary"), id + " primary");
                Assert.AreEqual(((string)t["primary_top"]).ToUpperInvariant(), Var("b-primary-top"), id + " primary top");
                Assert.AreEqual(((string)t["primary_base"]).ToUpperInvariant(), Var("b-primary-base"), id + " primary base");
                Assert.AreEqual(((string)t["label"]).ToUpperInvariant(), Var("b-on-primary"), id + " label");
                Assert.IsTrue(BrandTable.IsKnown(id));
                Assert.IsNotNull(BrandTable.Mark(id).Texture, id + ": the mark_png loads from Resources");
                checkedBrands++;
            }
            Assert.Greater(checkedBrands, 0);
        }

        [Test]
        public void ResourcePath_FromAssetPath()
        {
            Assert.AreEqual("UI/Brands/mcdonalds_mark", BrandTable.ResourcePath("client/Assets/Resources/UI/Brands/mcdonalds_mark.png"));
            Assert.IsNull(BrandTable.ResourcePath("art/source/brands/mcdonalds_mark.svg"));
            Assert.AreEqual(BrandTable.Police, BrandTable.ForVenue("ct_jail"));
            Assert.IsNull(BrandTable.Mark("police").Art, "police is a system theme with an icon mark");
        }
    }
}
