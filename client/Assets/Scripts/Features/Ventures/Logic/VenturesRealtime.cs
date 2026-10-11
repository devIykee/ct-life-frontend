using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using Newtonsoft.Json.Linq;

namespace CTLife.Ventures
{
    /// <summary>`ad_booking_update` (city, booker only): a booking moved state.</summary>
    public struct AdBookingUpdateSignal
    {
        public string bookingId, statusRaw, reasonId;
        public AdStatus status;
    }

    /// <summary>`ads_changed` (city, everyone): these slots changed; refetch GET /ads/live now instead of waiting for the 5 minute cache.</summary>
    public struct AdsChangedSignal
    {
        public string[] slotIds;
    }

    /// <summary>`vc_offer` (city, founder only): a VC Row NPC sent a term sheet.</summary>
    public struct VcOfferSignal
    {
        public VcOffer offer;
    }

    /// <summary>City event `evt_vc_dump {projectId, name, coinId}`: a VC sold at unlock.</summary>
    public struct VcDumpSignal
    {
        public string projectId, name, coinId;
    }

    /// <summary>`hire_offer` (city, invitee only).</summary>
    public struct HireOfferSignal
    {
        public HireOffer offer;
    }

    /// <summary>`project_event` (city, every project member): a server-rolled risk or lifecycle event.</summary>
    public struct ProjectEventSignal
    {
        public ProjectEvent projectEvent;
    }

    public enum ParseOutcome { Ok, Invalid, Unknown }

    /// <summary>
    /// Tolerant parsing and dispatch of the Phase 5 realtime messages (docs/fe-contract-proposals/phase4-6/realtime-additions.ts).
    /// Payloads arrive as whatever the room decoder produced (dictionary, JObject or JSON text); they are normalised to JSON and
    /// read field by field. A malformed payload is counted and dropped, never thrown: a bad message must not break the room.
    /// </summary>
    public static class VenturesRealtime
    {
        public static readonly string[] Types = { "ad_booking_update", "ads_changed", "vc_offer", "hire_offer", "project_event" };

        /// <summary>City event ids this lane reacts to (EventMessage.eventId).</summary>
        public const string VcDumpEvent = "evt_vc_dump";

        public static int Dropped { get; private set; }

        /// <summary>Log sink for dropped messages (replaced in tests, which run without the Unity player).</summary>
        public static Action<string> Warn = m => Log.Warn("ventures", m);

        public static bool IsVenturesType(string type) => Array.IndexOf(Types, type) >= 0;

        public static ParseOutcome Parse(string type, JToken p, out object signal)
        {
            signal = null;
            if (!(p is JObject)) return IsVenturesType(type) ? ParseOutcome.Invalid : ParseOutcome.Unknown;
            try
            {
                switch (type)
                {
                    case "ad_booking_update":
                    {
                        var id = VWire.Str(p["bookingId"]);
                        var raw = VWire.Str(p["status"]);
                        if (!VWire.IsSafeId(id) || string.IsNullOrEmpty(raw)) return ParseOutcome.Invalid;
                        var reason = VWire.Str(p["reasonId"]);
                        signal = new AdBookingUpdateSignal
                        {
                            bookingId = id, statusRaw = VWire.Plain(raw, 24), status = AdStatuses.Parse(raw), reasonId = VWire.IsSafeId(reason) ? reason : null,
                        };
                        return ParseOutcome.Ok;
                    }
                    case "ads_changed":
                    {
                        if (!(p["slotIds"] is JArray arr)) return ParseOutcome.Invalid;
                        var ids = arr.Select(VWire.Str).Where(VWire.IsSafeId).Distinct().Take(64).ToArray();
                        if (ids.Length == 0) return ParseOutcome.Invalid;
                        signal = new AdsChangedSignal { slotIds = ids };
                        return ParseOutcome.Ok;
                    }
                    case "vc_offer":
                    {
                        var offer = ProjectParse.VcTerm(p);
                        if (offer.InvestCents <= 0 || offer.EquityBps < 1 || offer.EquityBps > 10_000) return ParseOutcome.Invalid;
                        if (string.IsNullOrEmpty(offer.Status)) offer.Status = "pending";
                        signal = new VcOfferSignal { offer = offer };
                        return ParseOutcome.Ok;
                    }
                    case "hire_offer":
                    {
                        var offer = ProjectParse.Offer(p);
                        if (offer.SalaryCentsPerWeek <= 0 || string.IsNullOrEmpty(offer.Role)) return ParseOutcome.Invalid;
                        if (string.IsNullOrEmpty(offer.Status)) offer.Status = "pending";
                        signal = new HireOfferSignal { offer = offer };
                        return ParseOutcome.Ok;
                    }
                    case "project_event":
                    {
                        var e = ProjectParse.Event(p);
                        if (!VWire.IsSafeId(VWire.Str(p["projectId"]))) return ParseOutcome.Invalid;
                        signal = new ProjectEventSignal { projectEvent = e };
                        return ParseOutcome.Ok;
                    }
                    default:
                        return ParseOutcome.Unknown;
                }
            }
            catch (FormatException) { return ParseOutcome.Invalid; }
        }

        /// <summary>City event `evt_vc_dump`. Returns false for other events or when the params lack a project id.</summary>
        public static bool TryParseVcDump(string eventId, IDictionary<string, string> p, out VcDumpSignal dump)
        {
            dump = default;
            if (eventId != VcDumpEvent || p == null) return false;
            p.TryGetValue("projectId", out var pid);
            if (!VWire.IsSafeId(pid)) return false;
            p.TryGetValue("name", out var name);
            p.TryGetValue("coinId", out var coin);
            dump = new VcDumpSignal { projectId = pid, name = VWire.Plain(name, 40), coinId = VWire.IsSafeId(coin) ? coin : null };
            return true;
        }

        /// <summary>
        /// Entry point for the room layer (hook in docs/progress/requests.md): normalise <paramref name="payload"/>, parse, publish
        /// the typed signal on the Bus. Returns true when the message was one of ours and valid.
        /// </summary>
        public static bool Dispatch(string type, object payload)
        {
            if (!IsVenturesType(type)) return false;
            var outcome = Parse(type, VWire.ToToken(payload), out var signal);
            if (outcome != ParseOutcome.Ok)
            {
                Dropped++;
                Warn?.Invoke("Dropped malformed " + type + " message.");
                return false;
            }
            switch (signal)
            {
                case AdBookingUpdateSignal s: Bus.Publish(s); break;
                case AdsChangedSignal s: Bus.Publish(s); break;
                case VcOfferSignal s: Bus.Publish(s); break;
                case HireOfferSignal s: Bus.Publish(s); break;
                case ProjectEventSignal s: Bus.Publish(s); break;
            }
            return true;
        }
    }

    /// <summary>Which phone app a tapped venue spot opens (hook: docs/progress/requests.md, "phase5-ventures").</summary>
    public static class VenturesSpots
    {
        public const string AdsApp = "ads", BuilderApp = "builder", VcApp = "vc";

        /// <summary>
        /// Registered spot ids only (contract/ids/spots.json): the VC Row pitch room, the Timeline Square Pixel Wall and the
        /// Conference Hall expo. Builder Hills spots do not exist in the registry yet; the proposal asks for
        /// `builder_hills.project_board` and `builder_hills.hq_door`, which map here once added.
        /// </summary>
        public static string AppFor(string venueId, string spotId)
        {
            if (string.IsNullOrEmpty(spotId)) return null;
            // Anchors may carry the venue-local id ("center") or the registry id ("vc_row.center").
            if (spotId.IndexOf('.') < 0) { if (string.IsNullOrEmpty(venueId)) return null; spotId = venueId + "." + spotId; }
            switch (spotId)
            {
                case "vc_row.center": return VcApp;
                case "timeline_square.pixel_wall": return AdsApp;
                case "conference_hall.center": return AdsApp;
                case "builder_hills.project_board":
                case "builder_hills.hq_door": return BuilderApp;
                default: return null;
            }
        }
    }
}
