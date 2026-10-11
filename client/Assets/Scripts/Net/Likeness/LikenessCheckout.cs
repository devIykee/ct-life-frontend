using System;
using System.Globalization;
using System.IO;
using CTLife.Core;
using Newtonsoft.Json.Linq;

namespace CTLife.Net
{
    /// <summary>The reviewed server price/transaction; no client-selected amount, mint or recipient is sent.</summary>
    public sealed class LikenessCheckout
    {
        public string IntentId, ModelId, Amount, Cluster, Transaction, ExpiresAt;
        public static LikenessCheckout Parse(JObject value, string modelId, SolanaCluster cluster, DateTimeOffset now)
        {
            string network = cluster == SolanaCluster.Devnet ? "devnet" : cluster == SolanaCluster.Mainnet ? "mainnet" : null;
            if (network == null || value?.Value<string>("cluster") != network || value.Value<string>("currency") != "USDC" || value.Value<string>("modelId") != modelId)
                throw new InvalidDataException("Payment environment or character mismatch");
            string amount = value.Value<string>("amount"), baseUnits = value.Value<string>("amountBaseUnits");
            if (!decimal.TryParse(amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var units) || units <= 0 ||
                !long.TryParse(baseUnits, NumberStyles.None, CultureInfo.InvariantCulture, out var atomic) || atomic <= 0 || units * 1000000m != atomic)
                throw new InvalidDataException("Payment amount invalid");
            foreach (var field in new[] { "mint", "recipient", "reference" })
                if (!Base58.IsPublicKeyLength(value.Value<string>(field))) throw new InvalidDataException("Payment address invalid");
            if (!DateTimeOffset.TryParse(value.Value<string>("expiresAt"), out var expiry) || expiry <= now.AddSeconds(20)) throw new InvalidDataException("Payment quote expired");
            string transaction = value.Value<string>("transactionBase64");
            if (string.IsNullOrEmpty(transaction) || transaction.Length > 4096) throw new InvalidDataException("Payment transaction invalid");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(transaction); } catch (FormatException) { throw new InvalidDataException("Payment transaction invalid"); }
            if (bytes.Length < 100 || bytes.Length > 1232) throw new InvalidDataException("Payment transaction size invalid");
            return new LikenessCheckout { IntentId = LikenessClient.Id(value.Value<string>("intentId")), ModelId = modelId, Amount = amount,
                Cluster = network, Transaction = transaction, ExpiresAt = value.Value<string>("expiresAt") };
        }
    }
}
