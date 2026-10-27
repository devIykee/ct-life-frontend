using System;
using System.Threading;
using System.Threading.Tasks;

namespace CTLife.Core
{
    /// <summary>
    /// Sign-In-With-Solana input, copied verbatim from GET /v1/auth/challenge (contract section 3 step 1).
    /// Every value is the exact string the server sent; never reformat dates.
    /// </summary>
    public sealed class SiwsRequest
    {
        public string domain, statement, uri, version, chainId, nonce, issuedAt, expirationTime;
    }

    /// <summary>MWA `sign_in_result` (contract section 3 step 3). Byte fields are base64.</summary>
    public sealed class SiwsResponse
    {
        public string address;
        public string signedMessageBase64;
        public string signatureBase64;
        public string signatureType = "ed25519";
    }

    public sealed class WalletException : Exception
    {
        public readonly bool UserDeclined;
        /// <summary>A machine-readable reason when it is not the player's choice: <see cref="TxChanged"/> (nothing was
        /// sent because the SDK would have altered the server's transaction), <see cref="TxUnreadable"/>.</summary>
        public readonly string Code;
        public const string TxChanged = "TX_CHANGED", TxUnreadable = "TX_UNREADABLE";

        public WalletException(string message, bool userDeclined = false, Exception inner = null) : base(message, inner)
        {
            UserDeclined = userDeclined;
        }

        public WalletException(string message, string code, Exception inner = null) : base(message, inner)
        {
            Code = code;
        }
    }

    /// <summary>
    /// Implemented by CTLife.Solana on Solana.Unity-SDK (MWA 2.0). The fallback plan in the Build Spec
    /// (our own plugin over mobile-wallet-adapter-clientlib-ktx) would implement the same interface.
    /// </summary>
    public interface IWalletSignIn
    {
        string Name { get; }

        /// <summary>False in the editor and on platforms without MWA.</summary>
        bool IsAvailable { get; }

        Task<SiwsResponse> SignIn(SiwsRequest request, CancellationToken ct);

        /// <summary>MWA `deauthorize` (contract section 3 step 10).</summary>
        Task SignOut();
    }

    /// <summary>Signs and submits a server-built base64 transaction (payments, ORE staking, paid calls).</summary>
    public interface ITransactionSigner
    {
        /// <returns>The transaction signature (base58).</returns>
        Task<string> SignAndSendBase64(string transactionBase64, CancellationToken ct);
    }
}
