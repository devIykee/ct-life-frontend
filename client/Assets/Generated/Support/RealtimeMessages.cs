// Realtime message payloads for contract-v1.0.0-rc1 (contract/realtime/messages.ts).
// HAND-MIRRORED: the backend contract defines messages as Zod schemas, which `schema-codegen` does not emit
// (it only generates the @colyseus/schema state classes in ../Realtime). Field names and types follow messages.ts
// one to one; z.number().int() → long (cents) or double (epoch ms, same as the generated states).
// Reported in docs/progress/backend-dev-feedback.md. Replace with generated code if the contract adds it.
using System.Collections.Generic;

namespace CTLife.Realtime
{
    // ---- client → server (venue room) ----
    public class MoveMessage { public double x; public double z; public double rotY; public bool island; }
    public class CorrectedMessage { public double x; public double z; public bool island; }
    public class VehicleEnterMessage { public string id; public string kind; public double x, y, z, yaw; }
    public class VehicleMoveMessage { public string id; public double x, y, z, yaw, pitch, roll, speed; public ushort flags; }
    public class VehicleExitMessage { public string id; public double x, z, yaw; }
    public class VehicleCorrectedMessage { public string id; public double x, y, z; }
    public class WireVector3 { public double x, y, z; }
    public class VehicleHitRelayMessage { public string victim, by, vehicleId; public WireVector3 velocity; public double at; }
    public class SitMessage { public string spotId; }
    public class StandMessage { }
    public class EmoteMessage { public string emoteId; }
    public class SayMessage { public string text; }
    public class TalkingMessage { public bool on; }
    public class NpcTapMessage { public string npcId; }

    // ---- server → client (venue room) ----
    public class WelcomeMessage { public double serverNow; public string instanceId; }
    public class SaidMessage { public string messageId; public string userId; public string sessionId; public string text; public double at; }
    /// <summary>`params` is optional in rc1 (may be null).</summary>
    public class ToastMessage { public string textId; public Dictionary<string, string> @params; }
    /// <summary>`code` is the RejectedMessage enum in messages.ts; kept as a string so unknown codes never crash.</summary>
    public class RejectedMessage { public string type; public string code; public string id; }

    // ---- server → client (city room) ----
    public class EventMessage { public string eventId; public Dictionary<string, string> @params; public double at; }
    public class LiquidatedMessage { public string perpId; public string coinId; public long lossCents; public double at; }
    public class PaymentConfirmedMessage { public string intentId; public long cashCents; }
    public class SpaceUpdateMessage { public string spaceId; }
    public class DmMessage { public string messageId; public string fromUserId; public string text; public double at; }
    public class CallInviteMessage { public string callId; public string fromUserId; public bool paid; }
    public class KickedMessage { public string reasonId; }

    // ---- server → client (market room) ----
    public class TapeMessage { public string coinId; public string side; public long cashCents; public string name; }

    /// <summary>RATE_LIMITS in messages.ts.</summary>
    public static class RealtimeLimits
    {
        public const int MovePerSecond = 10;
        public const int SayEveryMs = 2000;
        public const int EmotePerSecond = 1;
    }
}
