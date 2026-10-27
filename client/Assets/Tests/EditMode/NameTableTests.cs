using System.IO;
using CTLife.Core;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>One table for every name players see (QUALITY-BAR.md): contract/ids/names.json, shipped in Resources.</summary>
    public sealed class NameTableTests
    {
        [TearDown] public void TearDown() => NameTable.Reset();

        [Test]
        public void ShippedCopy_MatchesTheContract()
        {
            var contract = Path.Combine(Application.dataPath, "../../contract/ids/names.json");
            var shipped = Resources.Load<TextAsset>("Contract/names");
            Assert.IsNotNull(shipped, "Resources/Contract/names.json");
            Assert.AreEqual(File.ReadAllText(contract).Replace("\r\n", "\n"), shipped.text.Replace("\r\n", "\n"), "copy contract/ids/names.json to Resources/Contract");
            var root = JObject.Parse(shipped.text);
            foreach (var kv in (JObject)root["names"])
            {
                StringAssert.IsMatch(@"^[a-z]+(\.[a-z0-9_]+)+$", kv.Key);
                Assert.IsFalse(string.IsNullOrWhiteSpace((string)kv.Value), kv.Key);
            }
        }

        [Test]
        public void ApprovedNames_AreApplied()
        {
            // the user approved ct-life-evidence/names/NAMES.md as listed (2026-10-26)
            Assert.AreEqual("Hyperliquid", NameTable.Get("venue.perp_pit"));
            Assert.AreEqual("Axiom", NameTable.Get("app.pulse"));
            Assert.AreEqual("Police", NameTable.Get("npc.sleuth"));
            Assert.AreEqual("McDonald's", Strings.T("rekt.tag"));
        }

        [Test]
        public void Apply_SwapsOldNamesInCopy()
        {
            Assert.AreEqual("McDonald's is hiring", NameTable.Apply("Rekt Burger is hiring"));
            Assert.AreEqual("Play CRASH in the Casino", NameTable.Apply("Play MOON OR RUG in the Rug Room"));
            Assert.AreEqual("Police sends cruisers", NameTable.Apply("The Sleuth sends cruisers"));
            Assert.AreEqual("Fraud Investigator", NameTable.Apply("Rug Sleuth"), "the longest match wins");
            Assert.AreEqual("$PULSE pumps", NameTable.Apply("$PULSE pumps"), "tickers are never touched");
            Assert.AreEqual("Pulsed", NameTable.Apply("Pulsed"), "whole words only");
            Assert.AreEqual("Timeline Downtown", NameTable.Apply("Timeline Downtown"), "a protected name stays");
            Assert.AreEqual("Post it on X", NameTable.Apply("Post it on the Timeline"));
        }

        [Test]
        public void OneValue_RenamesEverywhereItIsRead()
        {
            NameTable.LoadFrom(@"{""names"":{""venue.perp_pit"":""Futures Floor"",""app.pulse"":""Trade""},""strings"":{""rekt.tag"":""Burger Bar""}}");
            Assert.AreEqual("Futures Floor", NameTable.Get("venue.perp_pit", "Perp Pit"));
            Assert.AreEqual("Burger Bar", Strings.T("rekt.tag"));
            Assert.AreEqual("Clock in", Strings.T("rekt.shift"), "keys without a name entry are untouched");
            Assert.AreEqual("fallback", NameTable.Get("venue.unknown", "fallback"));
        }
    }
}
