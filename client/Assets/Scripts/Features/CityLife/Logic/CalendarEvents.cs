using CTLife.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife
{
    /// <summary>
    /// The real-world event calendar (backend PR #11, `feat/events-calendar`, 2026-10-23): Breakpoint 2026 in London and
    /// the like. Shapes as served by GET /v1/events/current|upcoming, GET /v1/events/{slug} and POST
    /// /v1/events/{slug}/progress. Times are epoch ms (the wire sends ISO dates; `serverNow` is epoch ms).
    /// </summary>
    public sealed class CalendarEvent
    {
        public string Slug, Title, Kind, City, RealVenue, VenueId, SkinId, StreamUrl, SourceUrl, Status;
        public long StartsAtMs, EndsAtMs;
        /// <summary>The opt-in IRL check-in is available (the event has a venue geofence).</summary>
        public bool IrlCheckin;

        public bool IsLive(long nowMs) => nowMs >= StartsAtMs && nowMs < EndsAtMs;
        public bool IsUpcoming(long nowMs) => nowMs < StartsAtMs;
        /// <summary>1-based day of the event at <paramref name="nowMs"/> (UTC days from the start) and the number of days.</summary>
        public (int day, int days) Day(long nowMs)
        {
            int days = Math.Max(1, (int)Math.Ceiling((EndsAtMs - StartsAtMs) / 86_400_000.0));
            int day = (int)Math.Floor((nowMs - StartsAtMs) / 86_400_000.0) + 1;
            return (Math.Max(1, Math.Min(days, day)), days);
        }

        /// <summary>An https link-out only (opened in the browser, never embedded); null otherwise.</summary>
        public static string SafeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || url.Length > 2048) return null;
            return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(u.Host) ? u.AbsoluteUri : null;
        }

        public static CalendarEvent Parse(JToken t)
        {
            if (!(t is JObject o)) return null;
            var slug = CityWire.Str(o["slug"]);
            if (!EventIds.SafeSlug(slug)) return null;
            long s = EventWire.Ms(o["startsAt"]), e = EventWire.Ms(o["endsAt"]);
            if (s <= 0 || e <= s) return null;
            return new CalendarEvent
            {
                Slug = slug, Title = CityWire.Str(o["title"]) ?? slug, Kind = CityWire.Str(o["kind"]) ?? "conference",
                City = CityWire.Str(o["city"]), RealVenue = CityWire.Str(o["realVenue"]), VenueId = CityWire.Str(o["ingameVenueId"]),
                SkinId = CityWire.Str(o["skinId"]), StreamUrl = SafeUrl(CityWire.Str(o["streamUrl"])), SourceUrl = SafeUrl(CityWire.Str(o["sourceUrl"])),
                Status = CityWire.Str(o["status"]), StartsAtMs = s, EndsAtMs = e, IrlCheckin = CityWire.BoolOr(o["irlCheckin"], false),
            };
        }

        public static List<CalendarEvent> ParseList(JToken t, out long serverNowMs)
        {
            serverNowMs = EventWire.Ms(t?["serverNow"]);
            var list = new List<CalendarEvent>();
            if (t?["items"] is JArray a)
                foreach (var x in a) { var e = Parse(x); if (e != null) list.Add(e); }
            return list;
        }
    }

    public sealed class TriviaChoice { public string Id, Label; }

    public sealed class TriviaQuestion
    {
        public string Id, Prompt;
        /// <summary>Answered right (PR #18: a wrong answer no longer closes the question).</summary>
        public bool Answered;
        public List<TriviaChoice> Choices = new List<TriviaChoice>();
        /// <summary>Wrong choices already tried on this question (they can not be tried again).</summary>
        public HashSet<string> Tried = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>The question cools down after a wrong choice (60 s, 10 min, 60 min); 0 = open.</summary>
        public long RetryAtMs;
        public bool CoolingDown(long nowMs) => !Answered && RetryAtMs > nowMs;
    }

    public sealed class EventStep
    {
        public string Step, Title, Kind;
        public int Required, Done;
        /// <summary>Booth / nod ids already counted (PR #18 restore state).</summary>
        public HashSet<string> DoneIds = new HashSet<string>(StringComparer.Ordinal);
        public bool Complete;
        public List<TriviaQuestion> Questions = new List<TriviaQuestion>();
        public bool IsTrivia => Kind == "trivia";
        public double Fraction => Required <= 0 ? 0 : Math.Min(1.0, Done / (double)Required);
    }

    public sealed class EventQuest
    {
        public List<EventStep> Steps = new List<EventStep>();
        public bool Completed;
        public long RewardCents;
        public string RewardItemKey;
        public long? ClaimedAtMs;

        public EventStep Find(string step) => Steps.Find(s => s.Step == step);
        /// <summary>The first step not complete yet (steps run in order), null when the chain is done.</summary>
        public EventStep Current => Steps.Find(s => !s.Complete);
        public int StepIndex(string step) => Steps.FindIndex(s => s.Step == step);
        public bool Unlocked(string step) { int i = StepIndex(step); return i >= 0 && (i == 0 || Steps[i - 1].Complete); }

        public static EventQuest Parse(JToken t)
        {
            if (!(t is JObject o)) return null;
            var q = new EventQuest { Completed = CityWire.BoolOr(o["completed"], false) };
            if (o["steps"] is JArray steps)
                foreach (var s in steps)
                {
                    if (!(s is JObject so)) continue;
                    var id = CityWire.Str(so["step"]);
                    if (string.IsNullOrEmpty(id) || id.Length > 32) continue;
                    var st = new EventStep
                    {
                        Step = id, Title = CityWire.Str(so["title"]) ?? id, Kind = CityWire.Str(so["kind"]) ?? "interaction",
                        Required = Math.Max(0, CityWire.IntOr(so["required"], 0)), Done = Math.Max(0, CityWire.IntOr(so["done"], 0)),
                        Complete = CityWire.BoolOr(so["complete"], false),
                    };
                    if (so["doneIds"] is JArray di) foreach (var x in di) { var pid = CityWire.Str(x); if (EventIds.SafeProof(pid)) st.DoneIds.Add(pid); }
                    if (so["questions"] is JArray qs)
                        foreach (var x in qs)
                        {
                            if (!(x is JObject xo)) continue;
                            var qid = CityWire.Str(xo["id"]);
                            if (!EventIds.SafeProof(qid)) continue;
                            var tq = new TriviaQuestion { Id = qid, Prompt = CityWire.Str(xo["prompt"]) ?? "", Answered = CityWire.BoolOr(xo["answered"], false), RetryAtMs = EventWire.Ms(xo["retryAt"]) };
                            if (xo["triedChoices"] is JArray tc) foreach (var c in tc) { var cid = CityWire.Str(c); if (!string.IsNullOrEmpty(cid) && cid.Length <= 4) tq.Tried.Add(cid); }
                            if (xo["choices"] is JArray cs)
                                foreach (var c in cs)
                                {
                                    var cid = CityWire.Str(c?["id"]);
                                    if (string.IsNullOrEmpty(cid) || cid.Length > 4) continue;
                                    tq.Choices.Add(new TriviaChoice { Id = cid, Label = CityWire.Str(c["label"]) ?? cid });
                                }
                            st.Questions.Add(tq);
                        }
                    q.Steps.Add(st);
                }
            if (o["reward"] is JObject r)
            {
                q.RewardCents = Math.Max(0, CityWire.LongOr(r["cashCents"], 0));
                q.RewardItemKey = CityWire.Str(r["itemKey"]);
                long c = EventWire.Ms(r["claimedAt"]);
                q.ClaimedAtMs = c > 0 ? c : (long?)null;
            }
            return q;
        }
    }

    public sealed class EventDetails
    {
        public CalendarEvent Event;
        public EventQuest Quest;
        /// <summary>The caller's IRL state (PR #18); null from an older server.</summary>
        public IrlState Irl;

        public static EventDetails Parse(JToken t)
        {
            var e = CalendarEvent.Parse(t);
            if (e == null) return null;
            return new EventDetails { Event = e, Quest = EventQuest.Parse(t["quest"]) ?? new EventQuest(), Irl = IrlState.Parse(t["irl"]) };
        }
    }

    public sealed class IrlState
    {
        public bool Available, CheckedIn, Visible;
        public long CheckedInAtMs;

        public static IrlState Parse(JToken t)
        {
            if (!(t is JObject o)) return null;
            return new IrlState
            {
                Available = CityWire.BoolOr(o["available"], false), CheckedIn = CityWire.BoolOr(o["checkedIn"], false),
                Visible = CityWire.BoolOr(o["visible"], false), CheckedInAtMs = EventWire.Ms(o["checkedInAt"]),
            };
        }
    }

    /// <summary>The trivia error details (PR #18): `wrong_answer` {retryAfterSeconds, triedChoices}, 429 TRIVIA_COOLDOWN
    /// {retryAt, retryAfterSeconds}.</summary>
    public static class TriviaErrors
    {
        public static long RetryAtMs(JToken details, long nowMs)
        {
            long at = EventWire.Ms(details?["retryAt"]);
            if (at > 0) return at;
            long secs = CityWire.LongOr(details?["retryAfterSeconds"], 0);
            return secs > 0 ? nowMs + secs * 1000 : 0;
        }

        public static IEnumerable<string> Tried(JToken details)
        {
            if (details?["triedChoices"] is JArray a) foreach (var c in a) { var s = CityWire.Str(c); if (!string.IsNullOrEmpty(s) && s.Length <= 4) yield return s; }
        }
    }

    public sealed class EventProgressResult
    {
        public EventQuest Quest;
        public bool Accepted, JustCompleted;

        public static EventProgressResult Parse(JToken t)
        {
            var q = EventQuest.Parse(t);
            if (q == null) return null;
            return new EventProgressResult { Quest = q, Accepted = CityWire.BoolOr(t["accepted"], false), JustCompleted = CityWire.BoolOr(t["justCompleted"], false) };
        }
    }

    /// <summary>Realtime `event_live` / `event_ended` on the city room (docs/REALTIME.md, PR #11).</summary>
    public sealed class EventSignal
    {
        public string Slug, Title, SkinId, VenueId, StreamUrl;
        public bool Live;
        public long EndsAtMs;

        public static EventSignal From(string type, object payload)
        {
            if (type != "event_live" && type != "event_ended") return null;
            if (!(payload is IDictionary<string, object> d)) return null;
            string S(string k) => d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
            var slug = S("slug");
            if (!EventIds.SafeSlug(slug)) return null;
            long ends = 0;
            if (d.TryGetValue("endsAt", out var e) && e != null) try { ends = Convert.ToInt64(e, CultureInfo.InvariantCulture); } catch (Exception) { ends = 0; }
            return new EventSignal { Slug = slug, Title = S("title"), SkinId = S("skinId"), VenueId = S("ingameVenueId"), StreamUrl = CalendarEvent.SafeUrl(S("streamUrl")), Live = type == "event_live", EndsAtMs = ends };
        }
    }

    /// <summary>One player on the IRL board (GET /events/{slug}/irl): no location, no user id.</summary>
    public sealed class IrlAttendee
    {
        public string Alias, TribeId, AvatarId;
        /// <summary>Opaque per-event handle for "Say hi" (PR #18); null from an older server.</summary>
        public string Handle;
        public bool Self;
        public string[] Look;
        public long CheckedInAtMs;

        public static List<IrlAttendee> ParseList(JToken t, out string next)
        {
            next = CityWire.Str(t?["next"]);
            var list = new List<IrlAttendee>();
            if (t?["items"] is JArray a)
                foreach (var x in a)
                {
                    var alias = CityWire.Str(x?["alias"]);
                    if (string.IsNullOrEmpty(alias) || alias.Length > 40) continue;
                    var handle = CityWire.Str(x["handle"]);
                    if (handle != null && (handle.Length > 64 || !System.Text.RegularExpressions.Regex.IsMatch(handle, "^[A-Za-z0-9_-]+$"))) handle = null;
                    var at = new IrlAttendee { Alias = alias, TribeId = CityWire.Str(x["tribeId"]), AvatarId = CityWire.Str(x["avatarId"]), CheckedInAtMs = EventWire.Ms(x["checkedInAt"]),
                        Handle = handle, Self = CityWire.BoolOr(x["self"], false) };
                    if (x["look"] is JObject lo) { at.Look = new string[7]; for (int i = 0; i < 7; i++) at.Look[i] = CityWire.Str(lo[LookSlots.Fields[i]]) ?? ""; }
                    else if (x["look"] is JArray la) at.Look = la.Select(v => CityWire.Str(v) ?? "").ToArray();
                    list.Add(at);
                }
            return list;
        }
    }

    /// <summary>POST /events/{slug}/checkin: the fact of the check-in (the position is never echoed) and the one-time reward.</summary>
    public sealed class IrlCheckinResult
    {
        public bool CheckedIn, Visible, FirstCheckin;
        public long CheckedInAtMs, RewardCents;
        public List<string> RewardItems = new List<string>();

        public static IrlCheckinResult Parse(JToken t)
        {
            if (!(t is JObject o)) return null;
            var r = new IrlCheckinResult
            {
                CheckedIn = CityWire.BoolOr(o["checkedIn"], false), Visible = CityWire.BoolOr(o["visible"], false),
                FirstCheckin = CityWire.BoolOr(o["firstCheckin"], false), CheckedInAtMs = EventWire.Ms(o["checkedInAt"]),
            };
            if (o["reward"] is JObject rw)
            {
                r.RewardCents = Math.Max(0, CityWire.LongOr(rw["cashCents"], 0));
                if (rw["itemKeys"] is JArray ik) foreach (var k in ik) { var s = CityWire.Str(k); if (!string.IsNullOrEmpty(s)) r.RewardItems.Add(s); }
            }
            return r;
        }
    }

    /// <summary>The ids the client owns for Breakpoint 2026 (contract/ids/calendar_events.json, questSet breakpoint_2026).</summary>
    public static class EventIds
    {
        public const string Breakpoint = "breakpoint-2026", LondonSkin = "london_2026", Venue = "conference_hall", Lanyard = "lanyard_breakpoint_2026";
        public const string IrlBadge = "irl_badge_breakpoint_2026", GoldLanyard = "lanyard_breakpoint_2026_gold";
        public const string StepBooths = "booths", StepTrivia = "trivia", StepNods = "nods";
        public const int BoothCount = 6, NodCount = 8;
        public static string Booth(int i) => "bp26.booth_" + (i + 1);
        public static string Nod(int i) => "bp26.nod_" + (i + 1);

        public static bool SafeSlug(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 64) return false;
            foreach (char c in s) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-')) return false;
            return true;
        }

        public static bool SafeProof(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 64) return false;
            foreach (char c in s) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '_')) return false;
            return true;
        }
    }

    public static class EventWire
    {
        /// <summary>Epoch ms from an ISO date (string or a parsed Date token) or a number; 0 when absent or bad.</summary>
        public static long Ms(JToken t)
        {
            if (CityWire.IsNull(t)) return 0;
            switch (t.Type)
            {
                case JTokenType.Integer: return (long)t;
                case JTokenType.Float: return (long)(double)t;
                case JTokenType.Date:
                    var v = ((JValue)t).Value;
                    if (v is DateTimeOffset dto) return dto.ToUnixTimeMilliseconds();
                    if (v is DateTime dt) return new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime()).ToUnixTimeMilliseconds();
                    return 0;
                case JTokenType.String:
                    var s = (string)t;
                    if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
                    return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d.ToUnixTimeMilliseconds() : 0;
                default: return 0;
            }
        }

        /// <summary>"21d 04:12:09", "04:12:09" under a day, "00:00:00" when past.</summary>
        public static string Countdown(long msLeft)
        {
            if (msLeft < 0) msLeft = 0;
            long s = msLeft / 1000;
            long d = s / 86400; s %= 86400;
            long h = s / 3600; s %= 3600;
            long m = s / 60; s %= 60;
            var clock = h.ToString("00", CultureInfo.InvariantCulture) + ":" + m.ToString("00", CultureInfo.InvariantCulture) + ":" + s.ToString("00", CultureInfo.InvariantCulture);
            return d > 0 ? d.ToString(CultureInfo.InvariantCulture) + "d " + clock : clock;
        }

        /// <summary>"0:45", "9:59", "59:59", "1:05:00" for short cooldowns.</summary>
        public static string Clock(long msLeft)
        {
            if (msLeft < 0) msLeft = 0;
            long s = (msLeft + 999) / 1000, h = s / 3600, m = (s % 3600) / 60, sec = s % 60;
            return h > 0 ? h + ":" + m.ToString("00", CultureInfo.InvariantCulture) + ":" + sec.ToString("00", CultureInfo.InvariantCulture)
                         : m + ":" + sec.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>A short "in 21d 4h" / "in 3h 12m" / "in 9m" for rows.</summary>
        public static string Short(long msLeft)
        {
            if (msLeft <= 0) return "now";
            long m = msLeft / 60000, h = m / 60, d = h / 24;
            if (d > 0) return d + "d " + (h % 24) + "h";
            if (h > 0) return h + "h " + (m % 60) + "m";
            return Math.Max(1, m) + "m";
        }

        public static string Dates(CalendarEvent e)
        {
            var s = DateTimeOffset.FromUnixTimeMilliseconds(e.StartsAtMs).UtcDateTime;
            var last = DateTimeOffset.FromUnixTimeMilliseconds(e.EndsAtMs - 1).UtcDateTime;
            var inv = CultureInfo.InvariantCulture;
            if (s.Date == last.Date) return s.ToString("d MMM yyyy", inv);
            if (s.Month == last.Month) return s.Day.ToString(inv) + "–" + last.ToString("d MMM yyyy", inv);
            return s.ToString("d MMM", inv) + " – " + last.ToString("d MMM yyyy", inv);
        }
    }

    /// <summary>Event copy (English; Strings-style keys).</summary>
    public static class EventText
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["app"] = "Events", ["calendar"] = "Calendar", ["upcoming"] = "Upcoming", ["live"] = "LIVE", ["ended"] = "Ended",
            ["startsIn"] = "Starts in", ["endsIn"] = "Ends in", ["day"] = "Day {0} of {1}",
            ["watch"] = "Watch the official stream", ["watch.note"] = "Opens the organiser's stream in your browser.",
            ["source"] = "Official page", ["where"] = "In the city: the Conference Center, Breakpoint Park",
            ["quest"] = "Event quests", ["quest.note"] = "Three steps, in order. Finish them during the event.",
            ["quest.locked"] = "Unlocks after the step above", ["quest.notLive"] = "Opens when the event goes live",
            ["step.booths.how"] = "Walk the expo row under the Conference Center canopy and tap Visit booth.",
            ["step.trivia.how"] = "Answer at the trivia kiosk by the keynote stage, or here.",
            ["step.nods.how"] = "Eight event nods are hidden around Breakpoint Park. Spot five.",
            ["trivia"] = "Keynote trivia", ["trivia.note"] = "Each question counts once, right or wrong. Get three right.",
            ["trivia.right"] = "Correct", ["trivia.wrong"] = "Not quite", ["trivia.answered"] = "Answered",
            ["reward"] = "Reward", ["reward.lanyard"] = "Breakpoint 2026 lanyard",
            ["reward.claimed"] = "Collected", ["claim.title"] = "Breakpoint complete", ["claim.body"] = "The lanyard is in your wardrobe, and on your nameplate.",
            ["claim.collect"] = "Collect", ["claim.wear"] = "Wear the lanyard", ["claim.worn"] = "Lanyard on", ["claim.inWardrobe"] = "In your wardrobe",
            ["booth.visited"] = "Booth {0} visited · {1}/{2}", ["booth.again"] = "Already visited this booth",
            ["nod.spotted"] = "Nod {0} spotted · {1}/{2}", ["nod.again"] = "You already spotted this one",
            ["step.locked"] = "Finish \"{0}\" first", ["notLive"] = "The event isn't live yet", ["rateLimited"] = "Easy — try again in a moment",
            ["failed"] = "Couldn't reach the event server", ["live.toast"] = "{0} is live", ["ended.toast"] = "{0} has ended",
            ["row.title"] = "Events", ["row.none"] = "No events coming up",
            ["irl.title"] = "I'm at Breakpoint", ["irl.kicker"] = "IRL CHECK-IN · OLYMPIA LONDON",
            ["irl.body"] = "At the real event? Check in to get the IRL badge, the gold lanyard and a Cash bonus.",
            ["irl.privacy"] = "We check your location once, when you tap, on this phone. We never store it: we only keep that you were at Breakpoint. Mock locations are refused. You stay off the attendee board unless you switch it on.",
            ["irl.button"] = "Check in at Olympia", ["irl.checking"] = "Checking your location…", ["irl.done"] = "Checked in at Breakpoint",
            ["irl.visible"] = "Show me on the IRL board", ["irl.visibleNote"] = "Others see your alias and look, never where you are.",
            ["irl.board"] = "IRL here", ["irl.boardNote"] = "Players at Olympia who chose to be listed.", ["irl.boardEmpty"] = "No one listed yet.",
            ["irl.wave"] = "Wave", ["irl.message"] = "Message", ["irl.notNearby"] = "Not in your part of the city right now",
            ["irl.waved"] = "You waved at {0}", ["irl.claim"] = "You're at Breakpoint", ["irl.claimBody"] = "The IRL badge and the gold lanyard are in your wardrobe.",
            ["irl.err.mock_location"] = "Mock locations can't check in. Turn off the location spoofer and try again.",
            ["irl.err.poor_accuracy"] = "Your location isn't precise enough (over 150 m). Step outside or near a window and try again.",
            ["irl.err.account_too_new"] = "Accounts made after Breakpoint started can't check in.",
            ["irl.err.outside_geofence"] = "You don't seem to be at Olympia London. The check-in only works at the venue.",
            ["irl.err.not_available"] = "IRL check-in isn't available for this event.",
            ["irl.err.permission"] = "Location permission is needed for the one-time check-in.", ["irl.err.disabled"] = "Turn on Location on your phone, then try again.",
            ["irl.err.timeout"] = "Couldn't get a location fix. Try again near a window.", ["irl.err.unsupported"] = "The IRL check-in needs the phone.",
            ["irl.err.unavailable"] = "Couldn't read your location.", ["irl.err.rate"] = "Too many tries. Try again in an hour.",
            ["trivia.cooldown"] = "Try again in {0}", ["trivia.tried"] = "Tried", ["trivia.coolingDown"] = "Cooling down — try another question",
            ["irl.sayHi"] = "Say hi", ["irl.hiSent"] = "Sent", ["irl.you"] = "YOU", ["irl.sayHiText"] = "Hey! I'm at Breakpoint too. Where are you?", ["irl.sent"] = "Message sent to {0}",
            ["irl.err.dm"] = "Couldn't message them (they may have left the board).", ["irl.err.dmRate"] = "That's a lot of hellos. Try again later.",
            ["irl.err.needCheckin"] = "Check in first to message attendees.", ["claim.notOwned"] = "Not in your inventory yet",
            ["kiosk"] = "Play trivia", ["visit"] = "Visit booth", ["spot"] = "Spot it", ["stream"] = "Watch stream", ["countdown"] = "Events",
        };

        public static string T(string key) => En.TryGetValue(key, out var v) ? NameTable.Apply(v) : key;
        public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
    }
}
