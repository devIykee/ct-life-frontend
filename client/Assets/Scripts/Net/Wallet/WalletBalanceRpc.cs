using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace CTLife.Net
{
    public interface IWalletBalanceRpc
    {
        /// <summary>Returns only the JSON-RPC result object. This transport never signs or submits transactions.</summary>
        Task<JObject> Read(SolanaCluster cluster, string method, JArray parameters, CancellationToken ct);
    }

    public sealed class WalletBalanceReadException : Exception
    {
        public string Code { get; }
        public WalletBalanceReadException(string code) : base(code) { Code = code; }
    }

    /// <summary>Credential-free, bounded public RPC reads. Public endpoints are rate-limited, not a production SLA.</summary>
    public sealed class WalletBalanceRpc : IWalletBalanceRpc
    {
        private int _requestId;
        public async Task<JObject> Read(SolanaCluster cluster, string method, JArray parameters, CancellationToken ct)
        {
            string url = cluster == SolanaCluster.Mainnet ? "https://api.mainnet.solana.com" :
                cluster == SolanaCluster.Devnet ? "https://api.devnet.solana.com" : null;
            if (url == null) throw new WalletBalanceReadException("unsupportedCluster");
            if (method != "getBalance" && method != "getAccountInfo" && method != "getTokenAccountsByOwner")
                throw new ArgumentException("Wallet balances permit read-only RPC methods", nameof(method));
            ct.ThrowIfCancellationRequested();
            int id = Interlocked.Increment(ref _requestId);
            var body = new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };
            using var download = new BalanceDownload();
            using var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None))),
                downloadHandler = download, timeout = 12, redirectLimit = 0
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "application/json");
            try { await Awaitable.FromAsyncOperation(req.SendWebRequest(), ct); }
            catch (OperationCanceledException) { req.Abort(); throw; }
            if (download.TooLarge) throw new WalletBalanceReadException("responseInvalid");
            if (req.responseCode == 429) throw new WalletBalanceReadException("rateLimited");
            if (req.result != UnityWebRequest.Result.Success || req.responseCode != 200)
                throw new WalletBalanceReadException("networkUnavailable");
            try
            {
                using var reader = new JsonTextReader(new StringReader(download.Text)) { MaxDepth = 32 };
                var response = JObject.Load(reader);
                if (reader.Read() || (string)response["jsonrpc"] != "2.0" || (int?)response["id"] != id)
                    throw new FormatException();
                if (response["error"] != null) throw new WalletBalanceReadException("rpcRejected");
                return response["result"] as JObject ?? throw new FormatException();
            }
            catch (WalletBalanceReadException) { throw; }
            catch (Exception) { throw new WalletBalanceReadException("responseInvalid"); }
        }

        private sealed class BalanceDownload : DownloadHandlerScript
        {
            private const int MaxBytes = 512 * 1024;
            private readonly MemoryStream _bytes = new MemoryStream();
            public bool TooLarge { get; private set; }
            public string Text => Encoding.UTF8.GetString(_bytes.ToArray());
            public BalanceDownload() : base(new byte[8192]) { }
            protected override bool ReceiveData(byte[] data, int length)
            {
                if (data == null || length <= 0) return true;
                if (_bytes.Length + length > MaxBytes) { TooLarge = true; return false; }
                _bytes.Write(data, 0, length); return true;
            }
        }
    }
}
