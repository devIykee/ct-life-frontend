using System;

namespace CTLife.Core
{
    public static class Ids
    {
        /// <summary>The only ID the client generates (contract section 1): a UUIDv4 Idempotency-Key.</summary>
        public static string NewIdempotencyKey() => Guid.NewGuid().ToString("D");
    }
}
