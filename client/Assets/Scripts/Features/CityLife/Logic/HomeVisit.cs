using System;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife
{
    /// <summary>
    /// Who may walk into my home. Contract rc1 has no endpoint for this; it is proposed in
    /// docs/fe-contract-proposals/city-life-homes-needs.md (`GET/PUT /homes/me/settings`). The server enforces it by
    /// refusing `POST /homes/{ownerId}/visit` with `FORBIDDEN {reason: visits_closed | followers_only}`.
    /// </summary>
    public enum VisitorPolicy { Open, Followers, Closed }

    public static class VisitorPolicies
    {
        public static string Wire(VisitorPolicy p) => p == VisitorPolicy.Open ? "open" : p == VisitorPolicy.Followers ? "followers" : "closed";

        public static bool TryParse(string s, out VisitorPolicy p)
        {
            switch (s)
            {
                case "open": p = VisitorPolicy.Open; return true;
                case "followers": p = VisitorPolicy.Followers; return true;
                case "closed": p = VisitorPolicy.Closed; return true;
                default: p = VisitorPolicy.Closed; return false;
            }
        }

        /// <summary>`{visitors: "open"|...}` (proposal). Unknown or missing values fail closed.</summary>
        public static VisitorPolicy Parse(JToken t) => TryParse(CityWire.Str(t?["visitors"]), out var p) ? p : VisitorPolicy.Closed;
    }

    /// <summary>A granted visit: the room to `joinById` (contract `POST /homes/{ownerId}/visit` → `{roomId}`).</summary>
    public sealed class VisitTicket
    {
        public string OwnerId, RoomId, HomeId;

        /// <summary>
        /// Contract: `{roomId}`. The backend checkout instead answers `{visitId, homeId, visitorUserId, visitedAt}` (a visit
        /// log row, no room), which is reported as UNAVAILABLE rather than guessed into a room id.
        /// </summary>
        public static VisitTicket Parse(string ownerId, JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a visit");
            var room = CityWire.Str(o["roomId"]);
            if (string.IsNullOrEmpty(room)) return null;
            if (!CityWire.IsSafeRoomId(room)) throw new FormatException("Invalid roomId");
            return new VisitTicket { OwnerId = ownerId, RoomId = room, HomeId = CityWire.Str(o["homeId"]) };
        }
    }

    public enum VisitStage { Idle, Requesting, Joining, Inside, Leaving, Denied, Failed, Unavailable }

    /// <summary>
    /// Home visit: ask the server for the owner's room, join it, stay, leave. Pure state machine; the UI and the room
    /// client drive it. One retained Idempotency-Key per owner while the request outcome is unknown. Tested.
    /// </summary>
    public sealed class HomeVisitFlow
    {
        private readonly RetainedKey _key;
        public VisitStage Stage { get; private set; } = VisitStage.Idle;
        public string OwnerId { get; private set; }
        public string OwnerName { get; private set; }
        public VisitTicket Ticket { get; private set; }
        public CityError Error { get; private set; }
        public int Visitors { get; private set; }
        public event Action Changed;

        public HomeVisitFlow(Func<string> newKey = null) { _key = new RetainedKey(newKey); }

        public bool Busy => Stage == VisitStage.Requesting || Stage == VisitStage.Joining || Stage == VisitStage.Leaving;
        public bool IsInside => Stage == VisitStage.Inside;

        /// <summary>Start a visit. Self-visits are allowed (the owner joins their own home room). Returns the Idempotency-Key, or null.</summary>
        public string Request(string ownerId, string ownerName, string myUserId)
        {
            if (!CityWire.IsSafeId(ownerId) || Busy || Stage == VisitStage.Inside) return null;
            if (OwnerId != ownerId) _key.Complete();
            OwnerId = ownerId;
            OwnerName = ownerName;
            Ticket = null;
            Error = null;
            Stage = VisitStage.Requesting;
            Changed?.Invoke();
            return _key.KeyFor("POST", "/homes/" + ownerId + "/visit", "");
        }

        public bool IsOwnHome(string myUserId) => !string.IsNullOrEmpty(OwnerId) && OwnerId == myUserId;

        public void Granted(VisitTicket ticket)
        {
            if (Stage != VisitStage.Requesting) return;
            _key.Complete();
            if (ticket == null)
            {
                Error = CityError.Unavailable("visit.noRoom");
                Stage = VisitStage.Unavailable;
            }
            else
            {
                Ticket = ticket;
                Stage = VisitStage.Joining;
            }
            Changed?.Invoke();
        }

        public void RequestFailed(CityError error)
        {
            if (Stage != VisitStage.Requesting) return;
            Error = error ?? new CityError { Code = "ERROR", Key = "err.generic" };
            _key.Settle(Error);
            Stage = Error.Is("UNAVAILABLE") ? VisitStage.Unavailable
                : Error.Is("FORBIDDEN") || Error.Is("BLOCKED") || Error.Is("NOT_FOUND") ? VisitStage.Denied
                : VisitStage.Failed;
            Changed?.Invoke();
        }

        public void Joined(int visitors)
        {
            if (Stage != VisitStage.Joining) return;
            Visitors = Math.Max(0, visitors);
            Stage = VisitStage.Inside;
            Changed?.Invoke();
        }

        public void JoinFailed(CityError error)
        {
            if (Stage != VisitStage.Joining) return;
            Error = error ?? new CityError { Code = "ERROR", Key = "visit.joinFailed" };
            // A room that rejects or no longer exists: the next try asks the server for a fresh ticket.
            Ticket = null;
            Stage = Error.Is("VENUE_FULL") ? VisitStage.Denied : VisitStage.Failed;
            Changed?.Invoke();
        }

        public void PresenceChanged(int visitors)
        {
            if (Stage != VisitStage.Inside) return;
            Visitors = Math.Max(0, visitors);
            Changed?.Invoke();
        }

        /// <summary>Player asked to leave. Returns false when there is nothing to leave.</summary>
        public bool BeginLeave()
        {
            if (Stage != VisitStage.Inside && Stage != VisitStage.Joining) return false;
            Stage = VisitStage.Leaving;
            Changed?.Invoke();
            return true;
        }

        /// <summary>The room is gone: our own leave finished, or the server closed it (owner closed the door, kick, drop).</summary>
        public void Left(bool unexpected)
        {
            if (Stage != VisitStage.Leaving && Stage != VisitStage.Inside && Stage != VisitStage.Joining) return;
            bool wasLeaving = Stage == VisitStage.Leaving;
            Ticket = null;
            Visitors = 0;
            if (unexpected && !wasLeaving)
            {
                Error = new CityError { Code = "ROOM_CLOSED", Key = "visit.closed" };
                Stage = VisitStage.Failed;
            }
            else
            {
                Error = null;
                Stage = VisitStage.Idle;
            }
            Changed?.Invoke();
        }

        /// <summary>Dismiss a terminal state (Denied/Failed/Unavailable) back to Idle.</summary>
        public void Dismiss()
        {
            if (Stage != VisitStage.Denied && Stage != VisitStage.Failed && Stage != VisitStage.Unavailable) return;
            if (Error == null || !Error.OutcomeUnknown) _key.Complete();
            Stage = VisitStage.Idle;
            Changed?.Invoke();
        }

        public bool KeyRetained => _key.Pending;
    }
}
