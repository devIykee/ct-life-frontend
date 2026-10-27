// EditMode test for lane C: does Solana.Unity keep a server-prepared, partially signed
// transaction byte-for-byte? If not, MwaWallet must hand MWA the raw bytes.
// Lives in Assets/Tests/EditMode/FactoryMwa/ (own assembly: Solana.Unity.Rpc/Wallet DLLs) with its fixture.
// Fixture: a Meteora DBC create-pool tx (legacy), signed by the mint, wallet slot empty
// (ct-life-evidence/factory-r5/mwa-spike/fixture-partially-signed-tx.json).
using System;
using System.IO;
using NUnit.Framework;
using Solana.Unity.Rpc.Models;
using UnityEngine;

public class FactoryTxBytePreservationTests
{
    [Serializable]
    private class Fixture
    {
        public string txBase64;
        public string mint;
        public string wallet;
    }

    private static byte[] Original()
    {
        var path = Path.Combine(Application.dataPath, "Tests/EditMode/FactoryMwa/Fixtures/factory-dbc-partial-signed.json");
        var f = JsonUtility.FromJson<Fixture>(File.ReadAllText(path));
        return Convert.FromBase64String(f.txBase64);
    }

    // What MwaWallet.SignAndSendBase64 does today: VersionedTransaction.Deserialize, then the SDK's
    // SignAndSendTransactions calls Transaction.Serialize(). Run 2026-10-26 (lane C, Solana.Unity-SDK @ bd0d30f):
    // the DBC create-pool tx is a LEGACY message and VersionedTransaction.Deserialize throws
    // NotSupportedException("Expected versioned message but received legacy message"), so today's MwaWallet path
    // cannot send a Factory launch at all. This test pins that finding; the fix is the kit's raw-bytes path
    // (or a legacy branch, whose round trip the next test shows is byte-exact).
    [Test]
    public void VersionedTransaction_rejects_the_legacy_DBC_tx_so_MwaWallet_needs_another_path()
    {
        var original = Original();
        Assert.Throws<NotSupportedException>(() => VersionedTransaction.Deserialize(original));
    }

    [Test]
    public void Legacy_Transaction_round_trip_keeps_the_exact_bytes()
    {
        var original = Original();
        var tx = Transaction.Deserialize(original);
        Assert.AreEqual(Convert.ToBase64String(original), Convert.ToBase64String(tx.Serialize()));
    }
}
