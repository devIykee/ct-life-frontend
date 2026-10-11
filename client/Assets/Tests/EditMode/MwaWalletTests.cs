using System.Reflection;
using NUnit.Framework;

namespace CTLife.Tests
{
    /// <summary>
    /// MwaWallet hides the SDK's cached public key during a sign-in so the SDK's focus hook does not start a second MWA
    /// operation (OperationInFlightException). That relies on a private constant of the vendored Solana.Unity-SDK; this
    /// fails if an SDK update renames it, before the guard silently stops working.
    /// </summary>
    public class MwaWalletTests
    {
        [Test]
        public void SdkPublicKeyPrefKeyStillMatches()
        {
            var type = System.Type.GetType("Solana.Unity.SDK.SolanaMobileWalletAdapter, com.solana.unity_sdk");
            Assert.IsNotNull(type, "SolanaMobileWalletAdapter not found in com.solana.unity_sdk");
            var field = type.GetField("PrefKeyPublicKey", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "SolanaMobileWalletAdapter.PrefKeyPublicKey is gone: review MwaWallet.SignIn's focus guard");
            Assert.AreEqual(field.GetRawConstantValue(), CTLife.Solana.MwaWallet.SdkCachedPublicKeyPref);
        }
    }
}
