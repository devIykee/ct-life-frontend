using System;
using CTLife.Core;
using Solana.Unity.Rpc.Models;
using Log = CTLife.Core.Log;

namespace CTLife.Solana
{
    /// <summary>
    /// Reads a server-built transaction with the matching Solana.Unity reader and proves the SDK will hand the wallet the
    /// server's exact bytes (decision 2026-10-26 after the Factory byte-preservation test: the DBC launch tx is legacy,
    /// payments are v0). The SDK re-serializes every Transaction before MWA sees it, so a reader that changes one byte
    /// would break a partial signature (the Factory mint) or sign something the server did not build: that is refused.
    /// </summary>
    public static class TxGuard
    {
        public enum Format { Legacy, Versioned }

        /// <summary>Legacy or versioned from the wire header: after the compact-u16 signature count and its 64-byte
        /// signatures, a versioned message's first byte has the high bit set (0x80 | version).</summary>
        public static bool TryFormat(byte[] tx, out Format format, out int messageOffset)
        {
            format = Format.Legacy; messageOffset = -1;
            if (tx == null || tx.Length == 0) return false;
            int count = 0, shift = 0, i = 0;
            while (true)
            {
                if (i >= tx.Length || i > 2) return false;
                int b = tx[i++];
                count |= (b & 0x7f) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            long offset = i + 64L * count;
            if (offset >= tx.Length) return false;
            messageOffset = (int)offset;
            format = (tx[messageOffset] & 0x80) != 0 ? Format.Versioned : Format.Legacy;
            return true;
        }

        public static Transaction Read(byte[] tx, Format format) =>
            format == Format.Versioned ? VersionedTransaction.Deserialize(tx) : Transaction.Deserialize(tx);

        /// <summary>The first index where two byte arrays differ, or -1 when they are equal.</summary>
        public static int FirstDifference(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return i;
            return a.Length == b.Length ? -1 : n;
        }

        /// <summary>The transaction to hand the SDK, only when it re-serializes to <paramref name="original"/> byte for
        /// byte. Throws <see cref="WalletException"/> with <see cref="WalletException.TxUnreadable"/> or
        /// <see cref="WalletException.TxChanged"/>; nothing reaches the wallet in either case.</summary>
        public static Transaction Prepare(byte[] original, out Format format)
        {
            if (!TryFormat(original, out format, out _))
                throw new WalletException("This transaction from the server can't be read. Nothing was sent.", WalletException.TxUnreadable);
            Transaction tx;
            try { tx = Read(original, format); }
            catch (Exception e)
            {
                Log.Warn("wallet", $"tx guard: {format} reader refused {original.Length} bytes: {e.GetType().Name} {e.Message}");
                throw new WalletException("This transaction from the server can't be read. Nothing was sent.", WalletException.TxUnreadable, e);
            }
            byte[] again;
            try { again = tx.Serialize(); }
            catch (Exception e)
            {
                Log.Warn("wallet", $"tx guard: {format} re-serialize failed: {e.GetType().Name} {e.Message}");
                throw new WalletException("This transaction can't be sent safely from here. Nothing was sent.", WalletException.TxChanged, e);
            }
            int diff = FirstDifference(original, again);
            if (diff >= 0)
            {
                Log.Warn("wallet", $"tx guard: {format} tx changed by the SDK at byte {diff} ({original.Length} -> {again.Length} bytes); not sent");
                throw new WalletException("This transaction can't be sent safely from here (it would change on the way to your wallet). Nothing was sent.", WalletException.TxChanged);
            }
            return tx;
        }
    }
}
