using System;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using Solana.Unity.Rpc.Models;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using Solana.Unity.Wallet.Utilities;
using UnityEngine;
using Log = CTLife.Core.Log;

namespace CTLife.Solana
{
    /// <summary>
    /// Mobile Wallet Adapter 2.0 through Solana.Unity-SDK `main` @ bd0d30f (v1.3.0 in package.json).
    /// SIWS: `Web3.Instance.LoginWalletAdapter(SignInPayload)` → `(Account, SignInResult)` with base64
    /// SignedMessage/Signature (verified in the SDK source at that SHA). Signing in on the Seeker needs the user's
    /// fingerprint, so automated runs use dev auth instead.
    /// </summary>
    public sealed class MwaWallet : IWalletSignIn, ITransactionSigner
    {
        private readonly string _identityName;
        private readonly string _identityUri;
        private readonly string _iconUri;
        private Web3 _web3;

        /// <summary>
        /// PlayerPrefs key where Solana.Unity-SDK @ bd0d30f caches the authorized public key
        /// (`SolanaMobileWalletAdapter.PrefKeyPublicKey`, private const; `MwaWalletTests` checks it still matches).
        /// </summary>
        public const string SdkCachedPublicKeyPref = "solana_sdk.mwa.public_key";

        public MwaWallet(string identityName = "CT Life", string identityUri = CTLife.Core.EnvConfig.AppIdentityUri, string iconUri = "/favicon.ico")
        {
            // The identity is EnvConfig.AppIdentityUri (placeholder until the domain is bought); the SIWS domain is its authority.
            _identityName = identityName;
            _identityUri = identityUri;
            _iconUri = iconUri;
        }

        public string Name => "Mobile Wallet Adapter";

        public bool IsAvailable
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        private Web3 EnsureWeb3()
        {
            if (_web3 != null) return _web3;
            if (Web3.Instance != null) return _web3 = Web3.Instance;
            var go = new GameObject("CTLife.Web3");
            go.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(go);
            _web3 = go.AddComponent<Web3>(); // RequireComponent adds MainThreadDispatcher.
            _web3.rpcCluster = Env.Current.cluster == SolanaCluster.Mainnet ? RpcCluster.MainNet : RpcCluster.DevNet;
            _web3.autoConnectOnStartup = false;
            _web3.solanaWalletAdapterOptions ??= new SolanaWalletAdapterOptions();
            _web3.solanaWalletAdapterOptions.solanaMobileWalletAdapterOptions ??= new SolanaMobileWalletAdapterOptions();
            // Web3.CreateWalletAdapter() dereferences the WebGL options on every platform; a component added from code
            // (not deserialized from a scene) leaves them null, so sign-in threw NullReferenceException on the Seeker.
            _web3.solanaWalletAdapterOptions.solanaWalletAdapterWebGLOptions ??= new SolanaWalletAdapterWebGLOptions();
            _web3.solanaWalletAdapterOptions.phantomWalletOptions ??= new PhantomWalletOptions();
            var mwa = _web3.solanaWalletAdapterOptions.solanaMobileWalletAdapterOptions;
            mwa.name = _identityName;
            mwa.identityUri = _identityUri;
            mwa.iconUri = _iconUri;
            mwa.keepConnectionAlive = true;
            go.SetActive(true);
            return _web3;
        }

        public async Task<SiwsResponse> SignIn(SiwsRequest r, CancellationToken ct)
        {
            var web3 = EnsureWeb3();
            var payload = new SignInPayload
            {
                Domain = r.domain,
                Statement = r.statement,
                Uri = r.uri,
                Version = r.version,
                ChainId = r.chainId,
                Nonce = r.nonce,
                IssuedAt = r.issuedAt,
                ExpirationTime = r.expirationTime,
            };
            (Account account, SignInResult result) res;
            // The SDK's own focus hook (SolanaMobileWalletAdapter.HandleApplicationFocus) runs a silent ReconnectWallet
            // whenever the app regains focus with a cached public key + auth token and no Account. During LoginWithSignIn
            // the wallet hands focus back before the sign-in has finished, so that reconnect hits the SDK's single-flight
            // gate and logs OperationInFlightException as an error (seen 2026-10-08 after a real Seed Vault sign-in).
            // The hook returns early when no public key is cached, and the sign-in path never reads it, so the key is
            // hidden for the duration of the sign-in: success writes the new one, failure puts the old one back.
            var hiddenKey = PlayerPrefs.GetString(SdkCachedPublicKeyPref, "");
            if (hiddenKey.Length > 0) { PlayerPrefs.DeleteKey(SdkCachedPublicKeyPref); PlayerPrefs.Save(); }
            try
            {
                res = await web3.LoginWalletAdapter(payload);
            }
            catch (Exception e)
            {
                Log.Warn("wallet", "LoginWalletAdapter failed: " + e);
                // The SDK reports a wallet that closed the session without answering as "Timeout or cancelled";
                // only an explicit decline is the player's choice.
                bool declined = e.Message.IndexOf("declin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                e.Message.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                e.Message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) < 0;
                throw new WalletException("Wallet sign-in failed: " + e.Message, declined, e);
            }
            finally
            {
                if (hiddenKey.Length > 0 && !PlayerPrefs.HasKey(SdkCachedPublicKeyPref))
                {
                    PlayerPrefs.SetString(SdkCachedPublicKeyPref, hiddenKey);
                    PlayerPrefs.Save();
                }
            }
            ct.ThrowIfCancellationRequested();
            if (res.account == null || res.result == null) throw new WalletException("Wallet returned no account.");
            Log.Info("wallet", $"SIWS signed by {res.account.PublicKey}");
            return new SiwsResponse
            {
                address = res.result.Address ?? res.account.PublicKey.Key,
                signedMessageBase64 = res.result.SignedMessage,
                signatureBase64 = res.result.Signature,
                signatureType = string.IsNullOrEmpty(res.result.SignatureType) ? "ed25519" : res.result.SignatureType,
            };
        }

        public async Task SignOut()
        {
            if (Web3.Wallet is SolanaWalletAdapter adapter) await adapter.DeauthorizeWallet();
        }

        /// <summary>
        /// Signs and submits a server-built transaction with MWA `sign_and_send_transactions` (contract section 4,
        /// PaymentIntent.tx; section 6, paid calls). The server sends unsigned v0 transactions.
        /// [Unverified] VersionedTransaction round-trip through the SDK's `Transaction[]` API; verify on the Seeker in
        /// Phase 2, else fall back to the raw-bytes MWA client.
        /// </summary>
        public async Task<string> SignAndSendBase64(string transactionBase64, CancellationToken ct)
        {
            if (!(Web3.Wallet is SolanaWalletAdapter adapter)) throw new WalletException("No MWA session; sign in with the wallet first.");
            var bytes = Convert.FromBase64String(transactionBase64);
            Transaction tx = VersionedTransaction.Deserialize(bytes);
            var result = await adapter.SignAndSendTransactions(new[] { tx });
            ct.ThrowIfCancellationRequested();
            switch (result)
            {
                case SignAndSendTxResult.Success ok when ok.Signatures != null && ok.Signatures.Length > 0:
                    return Encoders.Base58.EncodeData(ok.Signatures[0]);
                case SignAndSendTxResult.UserDeclined _:
                    throw new WalletException("You declined the transaction.", true);
                default:
                    throw new WalletException("The wallet could not submit the transaction (" + result?.GetType().Name + ").");
            }
        }
    }
}
