using System;
using Newtonsoft.Json.Linq;

namespace CTLife.Net
{
    /// <summary>
    /// A failed REST call. `Code` is the errors.json id from `{"error": {"code", "message", "details"}}`
    /// (contract section 1), or NETWORK_ERROR / HTTP_{status} when the body is not a contract error.
    /// Unknown codes are kept as strings; they never crash the client.
    /// </summary>
    public sealed class ApiException : Exception
    {
        public readonly int Status;
        public readonly string Code;
        public readonly JToken Details;
        public readonly bool IsNetwork;

        public ApiException(int status, string code, string message, JToken details = null, bool isNetwork = false)
            : base($"{code} ({status}): {message}")
        {
            Status = status;
            Code = code;
            Details = details;
            IsNetwork = isNetwork;
        }

        public string UserMessage => string.IsNullOrEmpty(ServerMessage) ? Code : ServerMessage;

        public string ServerMessage
        {
            get
            {
                var m = Message;
                var i = m.IndexOf("): ", StringComparison.Ordinal);
                return i >= 0 ? m.Substring(i + 3) : m;
            }
        }

        public bool Is(string code) => string.Equals(Code, code, StringComparison.Ordinal);
    }

    /// <summary>Marker type for 204 responses.</summary>
    public struct NoContent { }

    /// <summary>Raised when the server answers 426 CLIENT_OUTDATED or config.minClientVersion is newer than us.</summary>
    public struct UpdateRequiredSignal
    {
        public string minClientVersion;
    }
}
