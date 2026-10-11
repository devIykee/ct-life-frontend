using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace CTLife.CityLife
{
    /// <summary>
    /// `GET /meta/current`. Contract `WeeklyMeta {weekday, bonus}`; the backend checkout answers a key/value row
    /// `{key: "current", value: {...}, updatedAt}` whose value is either the contract shape or its worker's
    /// `{weekIso, winnersCount, losersCount, perkCents, generatedAt}`. All three are read; nothing is guessed.
    /// </summary>
    public sealed class MetaView
    {
        public string BonusId;
        /// <summary>The rotation slot (config `meta.weekly[].weekday`, read `weekday % 7` at every Sunday Unlock). -1 unknown.</summary>
        public int Slot = -1;
        public string WeekIso;
        public long? UpdatedAtMs;
        public long PerkCents = -1;
        public int Winners = -1, Losers = -1;

        public bool HasBonus => !string.IsNullOrEmpty(BonusId);

        public static MetaView Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected meta");
            var v = new MetaView { UpdatedAtMs = CityWire.TimeMsOrNull(o["updatedAt"]) };
            var body = o["value"] is JObject inner ? inner : o;
            v.BonusId = Safe(CityWire.Str(body["bonus"]) ?? CityWire.Str(body["bonusId"]));
            v.Slot = CityWire.IntOr(body["weekday"], -1);
            if (v.Slot < 0 || v.Slot > 6) v.Slot = -1;
            v.WeekIso = CityWire.Plain(body["weekIso"], 12);
            if (string.IsNullOrEmpty(v.WeekIso)) v.WeekIso = null;
            v.PerkCents = CityWire.LongOr(body["perkCents"], -1);
            v.Winners = CityWire.IntOr(body["winnersCount"], -1);
            v.Losers = CityWire.IntOr(body["losersCount"], -1);
            v.UpdatedAtMs = v.UpdatedAtMs ?? CityWire.TimeMsOrNull(body["generatedAt"]);
            return v;
        }

        private static string Safe(string id) => CityWire.IsSafeId(id) ? id : null;
    }

    /// <summary>The rotation in config `meta.weekly` (seven slots).</summary>
    public static class MetaRotation
    {
        public static List<(int slot, string bonus)> Read(JToken metaConfig)
        {
            var list = new List<(int, string)>();
            if (!(metaConfig?["weekly"] is JArray rows)) return list;
            foreach (var r in rows)
            {
                int slot = CityWire.IntOr(r?["weekday"], -1);
                var bonus = CityWire.Str(r?["bonus"]);
                if (slot >= 0 && slot <= 6 && CityWire.IsSafeId(bonus)) list.Add((slot, bonus));
            }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return list;
        }

        /// <summary>The slot of the current meta (its own `weekday`, else found by bonus id), or -1.</summary>
        public static int SlotOf(MetaView current, List<(int slot, string bonus)> rotation)
        {
            if (current == null) return -1;
            if (current.Slot >= 0) return current.Slot;
            if (!current.HasBonus) return -1;
            foreach (var r in rotation) if (r.bonus == current.BonusId) return r.slot;
            return -1;
        }

        /// <summary>What next Sunday Unlock rotates to, or null when the rotation or the current slot is unknown.</summary>
        public static string Next(MetaView current, List<(int slot, string bonus)> rotation)
        {
            int slot = SlotOf(current, rotation);
            if (slot < 0 || rotation.Count == 0) return null;
            int next = (slot + 1) % 7;
            foreach (var r in rotation) if (r.slot == next) return r.bonus;
            return null;
        }
    }

    public enum MoodTier { Capitulation, Red, Crab, Green, Euphoria }

    /// <summary>
    /// Market Mood (city room `mood`, basis points of the real SOL 24h change; contract section 5). Display only: the
    /// mood never is a currency. The −2% line is the spec's Doomposter rule ("spawns only when Market Mood is below
    /// −2%"); the other tier edges are FE presentation.
    /// </summary>
    public static class MarketMood
    {
        public const int DoomposterBelowBps = -200;

        public static MoodTier Tier(int bps)
        {
            if (bps <= -500) return MoodTier.Capitulation;
            if (bps < DoomposterBelowBps) return MoodTier.Red;
            if (bps <= 200) return MoodTier.Crab;
            if (bps < 500) return MoodTier.Green;
            return MoodTier.Euphoria;
        }

        public static bool DoomposterOut(int bps) => bps < DoomposterBelowBps;

        public static string Key(int bps) => "mood." + Tier(bps).ToString().ToLowerInvariant();
    }

    // ------------------------------------------------------------------ daily alpha clue

    public sealed class DailyClue
    {
        public string Text, DateUtc;
        public long? ExpiresAtMs;
        /// <summary>Backend extra `prizeCents`; -1 when not sent (the contract only returns the prize after answering).</summary>
        public long PrizeCents = -1;

        public static DailyClue Parse(JToken t)
        {
            if (!(t is JObject o)) throw new FormatException("Expected a clue");
            var text = CityWire.Plain(o["clueText"] ?? o["text"], 400);
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Missing clueText");
            return new DailyClue
            {
                Text = text,
                ExpiresAtMs = CityWire.TimeMsOrNull(o["expiresAt"]),
                DateUtc = CityWire.Plain(o["date"], 10),
                PrizeCents = CityWire.LongOr(o["prizeCents"], -1),
            };
        }
    }

    public sealed class ClueResult
    {
        public bool Correct;
        public long PrizeCents;

        public static ClueResult Parse(JToken t)
        {
            if (!(t is JObject o) || o["correct"] == null) throw new FormatException("Expected a clue result");
            return new ClueResult { Correct = CityWire.BoolOr(o["correct"], false), PrizeCents = Math.Max(0, CityWire.LongOr(o["prizeCents"], 0)) };
        }
    }

    public enum ClueStage { Idle, Loading, Ready, NoClue, Submitting, Correct, Wrong, AlreadyAnswered, Expired, Unknown, Error, Unavailable }

    /// <summary>
    /// Today's alpha clue (contract `GET /daily-clue`, `POST /daily-clue/answer`). The backend allows one attempt per day
    /// (`FORBIDDEN already_attempted`). The answer body carries the contract field `answer` and the backend's `attempt`
    /// (verified: the backend's zod 4.6.5 `z.object` strips unknown keys, so either server accepts it). Pure; tested.
    /// </summary>
    public sealed class DailyClueFlow
    {
        /// <summary>The backend's limit (`z.string().min(1).max(200)`); the contract sets none.</summary>
        public const int MaxAnswerLength = 200;

        private readonly RetainedKey _key;
        public ClueStage Stage { get; private set; } = ClueStage.Idle;
        public DailyClue Clue { get; private set; }
        public ClueResult Result { get; private set; }
        public CityError Error { get; private set; }
        public string LastAnswer { get; private set; }
        public event Action Changed;

        public DailyClueFlow(Func<string> newKey = null) { _key = new RetainedKey(newKey); }

        public static string Normalize(string answer)
        {
            if (answer == null) return "";
            var sb = new StringBuilder();
            bool space = false;
            foreach (var c in answer.Trim())
            {
                if (char.IsControl(c)) continue;
                if (char.IsWhiteSpace(c)) { if (!space) sb.Append(' '); space = true; continue; }
                space = false;
                sb.Append(c);
            }
            return sb.Length > MaxAnswerLength ? sb.ToString(0, MaxAnswerLength) : sb.ToString();
        }

        public static JObject Body(string normalized) => new JObject { ["answer"] = normalized, ["attempt"] = normalized };

        public bool BeginLoad()
        {
            if (Stage == ClueStage.Loading || Stage == ClueStage.Submitting) return false;
            Stage = ClueStage.Loading;
            Error = null;
            Changed?.Invoke();
            return true;
        }

        public void Loaded(DailyClue clue, double nowMs, bool answeredLocally)
        {
            if (Stage != ClueStage.Loading) return;
            Clue = clue;
            if (clue == null) Stage = ClueStage.NoClue;
            else if (clue.ExpiresAtMs.HasValue && clue.ExpiresAtMs.Value <= nowMs) Stage = ClueStage.Expired;
            else Stage = answeredLocally ? ClueStage.AlreadyAnswered : ClueStage.Ready;
            Changed?.Invoke();
        }

        public void LoadFailed(CityError error)
        {
            if (Stage != ClueStage.Loading) return;
            Error = error;
            Stage = error != null && error.Is("UNAVAILABLE") ? ClueStage.Unavailable
                : error != null && error.Is("NOT_FOUND") ? ClueStage.NoClue : ClueStage.Error;
            Changed?.Invoke();
        }

        public bool CanSubmit(string answer, double nowMs) =>
            (Stage == ClueStage.Ready || Stage == ClueStage.Unknown) && Clue != null && Normalize(answer).Length > 0 &&
            !(Clue.ExpiresAtMs.HasValue && Clue.ExpiresAtMs.Value <= nowMs);

        /// <summary>Returns the Idempotency-Key for the answer, or null. After an unknown outcome only the same answer may be resent.</summary>
        public string BeginSubmit(string answer, double nowMs)
        {
            if (!CanSubmit(answer, nowMs)) return null;
            var n = Normalize(answer);
            if (Stage == ClueStage.Unknown && LastAnswer != null && n != LastAnswer) return null;
            LastAnswer = n;
            Stage = ClueStage.Submitting;
            Error = null;
            Changed?.Invoke();
            return _key.KeyFor("POST", "/daily-clue/answer", Body(n).ToString(Newtonsoft.Json.Formatting.None));
        }

        public void Answered(ClueResult result)
        {
            if (Stage != ClueStage.Submitting) return;
            _key.Complete();
            Result = result;
            Stage = result != null && result.Correct ? ClueStage.Correct : ClueStage.Wrong;
            Changed?.Invoke();
        }

        public void SubmitFailed(CityError error)
        {
            if (Stage != ClueStage.Submitting) return;
            Error = error ?? new CityError { Code = "ERROR", Key = "err.generic" };
            _key.Settle(Error);
            if (Error.OutcomeUnknown) Stage = ClueStage.Unknown;
            else if (Error.Key == "err.alreadyAnswered") Stage = ClueStage.AlreadyAnswered;
            else if (Error.Is("NOT_FOUND")) Stage = ClueStage.Expired;
            else Stage = ClueStage.Ready;
            Changed?.Invoke();
        }

        public bool KeyRetained => _key.Pending;
    }

    // ------------------------------------------------------------------ Sunday Unlock recap and share card

    /// <summary>What the player had when they last looked (stored per env+user on the device). Display only.</summary>
    public sealed class WeekSnapshot
    {
        public string WeekIso, HomeId;
        public long CashCents, DebtCents;
        public double AtMs;

        public JObject ToJson() => new JObject
        {
            ["weekIso"] = WeekIso, ["homeId"] = HomeId, ["cashCents"] = CashCents, ["debtCents"] = DebtCents, ["atMs"] = AtMs,
        };

        public static WeekSnapshot FromJson(JToken t)
        {
            if (!(t is JObject o)) return null;
            try
            {
                var s = new WeekSnapshot
                {
                    WeekIso = CityWire.Str(o["weekIso"]), HomeId = CityWire.Str(o["homeId"]),
                    CashCents = CityWire.Long(o["cashCents"]), DebtCents = CityWire.Long(o["debtCents"]),
                    AtMs = CityWire.DoubleOr(o["atMs"], 0),
                };
                return string.IsNullOrEmpty(s.WeekIso) ? null : s;
            }
            catch (FormatException) { return null; }
        }
    }

    public sealed class ShareCard
    {
        public string Title;
        public readonly List<string> Lines = new List<string>();
        public string Footer;

        public string Text()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Title);
            foreach (var l in Lines) sb.AppendLine("• " + l);
            if (!string.IsNullOrEmpty(Footer)) sb.Append(Footer);
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// "What changed this week" for the Sunday Unlock screen. The contract has no weekly summary endpoint (proposed:
    /// `GET /me/week-recap`), so this compares the server's current numbers with the device's last snapshot and says
    /// "since you last checked" - it never claims a server ledger total. Pure; tested.
    /// </summary>
    public static class WeekRecap
    {
        public static List<string> Lines(WeekSnapshot before, long cashNow, long debtNow, string homeNow, string homeName,
            MetaView meta, int moodBps, RentView rent)
        {
            var lines = new List<string>();
            if (before != null)
            {
                long dc = cashNow - before.CashCents;
                if (dc != 0) lines.Add(CityLifeStrings.F(dc > 0 ? "recap.cashUp" : "recap.cashDown", Money(Math.Abs(dc))));
                long dd = debtNow - before.DebtCents;
                if (dd > 0) lines.Add(CityLifeStrings.F("recap.debtUp", Money(dd)));
                else if (dd < 0) lines.Add(CityLifeStrings.F("recap.debtDown", Money(-dd)));
                if (!string.IsNullOrEmpty(homeNow) && before.HomeId != null && homeNow != before.HomeId)
                    lines.Add(CityLifeStrings.F("recap.moved", string.IsNullOrEmpty(homeName) ? homeNow : homeName));
            }
            else lines.Add(CityLifeStrings.T("recap.first"));
            if (meta != null && meta.HasBonus) lines.Add(CityLifeStrings.F("recap.meta", CityLifeStrings.MetaTitle(meta.BonusId)));
            lines.Add(CityLifeStrings.F("recap.mood", CityLifeStrings.T(MarketMood.Key(moodBps)), Bps(moodBps)));
            if (rent.State != RentState.Unknown) lines.Add(CityLifeStrings.F("recap.rent", CityLifeStrings.T(rent.Key)));
            return lines;
        }

        public static ShareCard Card(string alias, string weekIso, List<string> lines)
        {
            var card = new ShareCard { Title = CityLifeStrings.F("share.weekTitle", string.IsNullOrEmpty(alias) ? "anon" : alias, weekIso ?? "") };
            card.Lines.AddRange(lines);
            card.Footer = CityLifeStrings.T("share.footer");
            return card;
        }

        private static string Money(long cents) => CTLife.Core.Money.Cash(cents);
        private static string Bps(int bps) => CTLife.Core.Money.Bps(bps);
    }
}
