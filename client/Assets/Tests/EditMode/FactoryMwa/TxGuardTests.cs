using System;
using System.IO;
using CTLife.Core;
using CTLife.Solana;
using NUnit.Framework;
using UnityEngine;

namespace CTLife.Tests
{
    /// <summary>MwaWallet's guard (decision 2026-10-26): read legacy or v0 by the header, hand the SDK only byte-exact
    /// transactions. Fixtures: the Factory DBC launch (legacy, mint-signed; backend #2's kit) and a v0 tx built with the
    /// server's own top-up code (server/application/topup.ts on backend main a19582a; fixed keys and blockhash).</summary>
    public sealed class TxGuardTests
    {
        [Serializable] private class Fixture { public string txBase64; }

        private static byte[] Load(string name) => Convert.FromBase64String(JsonUtility.FromJson<Fixture>(
            File.ReadAllText(Path.Combine(Application.dataPath, "Tests/EditMode/FactoryMwa/Fixtures", name))).txBase64);

        [Test]
        public void Legacy_FactoryLaunch_IsReadAsLegacy_AndPassesByteExact()
        {
            var bytes = Load("factory-dbc-partial-signed.json");
            Assert.IsTrue(TxGuard.TryFormat(bytes, out var f, out int at));
            Assert.AreEqual(TxGuard.Format.Legacy, f);
            Assert.AreEqual(1 + 64 * 2, at, "two signature slots (wallet empty, mint signed)");
            var tx = TxGuard.Prepare(bytes, out _);
            CollectionAssert.AreEqual(bytes, tx.Serialize());
        }

        [Test]
        public void V0_Payment_IsReadAsVersioned_AndPassesByteExact()
        {
            var bytes = Load("topup-v0-unsigned.json");
            Assert.IsTrue(TxGuard.TryFormat(bytes, out var f, out int at));
            Assert.AreEqual(TxGuard.Format.Versioned, f);
            Assert.AreEqual(1 + 64, at);
            var tx = TxGuard.Prepare(bytes, out _);
            CollectionAssert.AreEqual(bytes, tx.Serialize());
        }

        [Test]
        public void Garbage_IsRefusedAsUnreadable_NothingSent()
        {
            var e = Assert.Throws<WalletException>(() => TxGuard.Prepare(new byte[] { 3, 1, 2 }, out _));
            Assert.AreEqual(WalletException.TxUnreadable, e.Code);
            Assert.IsFalse(e.UserDeclined);
            e = Assert.Throws<WalletException>(() => TxGuard.Prepare(Array.Empty<byte>(), out _));
            Assert.AreEqual(WalletException.TxUnreadable, e.Code);
        }

        [Test]
        public void FirstDifference_FindsTheByte()
        {
            Assert.AreEqual(-1, TxGuard.FirstDifference(new byte[] { 1, 2 }, new byte[] { 1, 2 }));
            Assert.AreEqual(1, TxGuard.FirstDifference(new byte[] { 1, 2 }, new byte[] { 1, 3 }));
            Assert.AreEqual(2, TxGuard.FirstDifference(new byte[] { 1, 2 }, new byte[] { 1, 2, 0 }));
        }
    }
}
