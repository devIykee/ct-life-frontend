using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CTLife.Careers
{
    public sealed class CareerLevel
    {
        public int Level, ShiftSec, Xp;
        public long PayCents;
        public string MiniGameId;
        /// <summary>Level 1-5 of a career may list several games (FE proposal `miniGameIds`); rc1 has one `miniGameId`.</summary>
        public List<string> MiniGameIds = new List<string>();
    }

    public sealed class SkillGate
    {
        public string Skill;
        public int Level;
    }

    /// <summary>One career from config `careers` (contract config/careers.json: 14 careers × 5 levels).</summary>
    public sealed class CareerDef
    {
        public string Id, Name;
        public bool Active;
        public int Phase = 1;
        public List<SkillGate> Gates = new List<SkillGate>();
        public List<CareerLevel> Levels = new List<CareerLevel>();
        public CareerLevel Level(int level) => Levels.FirstOrDefault(l => l.Level == level);
        public int TopLevel => Levels.Count == 0 ? 1 : Levels.Max(l => l.Level);
    }

    public static class CareerConfig
    {
        /// <summary>
        /// Accepts `{careers: [...]}` (contract), a bare array, or an id-keyed object. Skill gates accept the rc1 shape
        /// `{"skill": "coding", "level": 1}`, a map `{"coding": 1}`, or an array of either. Malformed careers are skipped.
        /// </summary>
        public static List<CareerDef> Parse(JToken config)
        {
            var result = new List<CareerDef>();
            IEnumerable<(string key, JToken value)> entries;
            if (CareersWire.List(config, "careers") is JArray arr) entries = arr.Select(x => ((string)null, x));
            else if (config is JObject o) entries = o.Properties().Where(p => p.Value is JObject && p.Name != "version").Select(p => (p.Name, p.Value));
            else return result;
            foreach (var (key, c) in entries)
            {
                if (!(c is JObject)) continue;
                var id = CareersWire.Str(c, "id") ?? key;
                if (string.IsNullOrEmpty(id)) continue;
                var def = new CareerDef
                {
                    Id = id, Name = CareersWire.Str(c, "name") ?? id, Active = CareersWire.Bool(c, "active") ?? true,
                    Phase = Math.Max(1, Math.Min(6, CareersWire.Int(c, "phase") ?? 1)),
                };
                def.Gates = ParseGates(c["skillGates"]);
                if (c["levels"] is JArray levels)
                    foreach (var l in levels)
                    {
                        int? level = CareersWire.Int(l, "level");
                        if (!level.HasValue || level < 1 || level > 5) continue;
                        var lv = new CareerLevel
                        {
                            Level = level.Value, PayCents = Math.Max(0, CareersWire.Long(l, "payCents") ?? 0),
                            ShiftSec = Math.Max(0, CareersWire.Int(l, "shiftSec") ?? 0), Xp = Math.Max(0, CareersWire.Int(l, "xp") ?? 0),
                            MiniGameId = CareersWire.Str(l, "miniGameId"),
                        };
                        lv.MiniGameIds = CareersWire.Strings(l["miniGameIds"]);
                        if (lv.MiniGameIds.Count == 0 && !string.IsNullOrEmpty(lv.MiniGameId)) lv.MiniGameIds.Add(lv.MiniGameId);
                        if (string.IsNullOrEmpty(lv.MiniGameId) && lv.MiniGameIds.Count > 0) lv.MiniGameId = lv.MiniGameIds[0];
                        def.Levels.RemoveAll(x => x.Level == lv.Level);
                        def.Levels.Add(lv);
                    }
                def.Levels.Sort((a, b) => a.Level.CompareTo(b.Level));
                result.Add(def);
            }
            return result;
        }

        public static List<SkillGate> ParseGates(JToken t)
        {
            var gates = new List<SkillGate>();
            void One(JToken g)
            {
                if (!(g is JObject o)) return;
                var skill = CareersWire.Str(o, "skill");
                var level = CareersWire.Int(o, "level", "min", "minLevel");
                if (!string.IsNullOrEmpty(skill) && level.HasValue) { Add(skill, level.Value); return; }
                foreach (var p in o.Properties())
                    if (p.Value.Type == JTokenType.Integer) Add(p.Name, p.Value.Value<int>());
            }
            void Add(string skill, int level)
            {
                if (level <= 1) return; // every skill starts at 1 (config skills.startValue): a level-1 gate never blocks
                level = Math.Min(10, level);
                var existing = gates.FirstOrDefault(x => x.Skill == skill);
                if (existing != null) existing.Level = Math.Max(existing.Level, level);
                else gates.Add(new SkillGate { Skill = skill, Level = level });
            }
            if (t is JArray a) foreach (var g in a) One(g);
            else One(t);
            return gates;
        }
    }

    /// <summary>One row of GET /careers. Contract: `{id, name, level, xp, active}`; the backend adds `payCents`, `shiftSec`,
    /// `miniGameId`, `phase` for the current level.</summary>
    public sealed class CareerRow
    {
        public string Id, Name, MiniGameId;
        public int Level, Xp;
        public bool Active;
        public long? PayCents;
        public int? ShiftSec, Phase;

        public static List<CareerRow> ParseList(JToken body, out int malformed)
        {
            malformed = 0;
            var list = new List<CareerRow>();
            var arr = CareersWire.List(body, "careers");
            if (arr == null) { malformed = 1; return list; }
            foreach (var r in arr)
            {
                var id = CareersWire.Str(r, "id", "careerId");
                if (!CareersWire.IsSafeId(id) || list.Any(x => x.Id == id)) { malformed++; continue; }
                list.Add(new CareerRow
                {
                    Id = id, Name = CareersWire.Str(r, "name"), Level = Math.Max(0, Math.Min(5, CareersWire.Int(r, "level") ?? 0)),
                    Xp = Math.Max(0, CareersWire.Int(r, "xp") ?? 0), Active = CareersWire.Bool(r, "active") ?? false,
                    PayCents = CareersWire.Long(r, "payCents"), ShiftSec = CareersWire.Int(r, "shiftSec"),
                    MiniGameId = CareersWire.Str(r, "miniGameId"), Phase = CareersWire.Int(r, "phase"),
                });
            }
            return list;
        }
    }

    /// <summary>POST /careers/{id}/apply → contract `Job {careerId, level, xp}`; backend `{id, level}`.</summary>
    public sealed class JobGrant
    {
        public string CareerId;
        public int Level, Xp;

        public static JobGrant Parse(JToken body, string requested)
        {
            if (!(body is JObject)) return null;
            var id = CareersWire.Str(body, "careerId", "id");
            if (string.IsNullOrEmpty(id) || id != requested) return null;
            return new JobGrant { CareerId = id, Level = Math.Max(1, Math.Min(5, CareersWire.Int(body, "level") ?? 1)), Xp = Math.Max(0, CareersWire.Int(body, "xp") ?? 0) };
        }
    }

    public enum CareerStatus { Hired, CanApply, SkillGated, PhaseLocked, NotOffered }

    /// <summary>What the jobs board shows for one career. Pay and shift length are server values (row first, then config).</summary>
    public sealed class CareerCard
    {
        public string Id, Name;
        public CareerDef Def;
        public CareerRow Row;
        public CareerStatus Status;
        public int Level, Xp, Phase;
        public CareerLevel Current, Next;
        public long PayCents;
        public int ShiftSec;
        public SkillGate BlockingGate;
        public int PlayerSkill;
        public bool IsTopLevel => Def != null && Level >= Def.TopLevel;
    }

    /// <summary>
    /// Builds the jobs board. The contract's GET /careers `active` flag is ambiguous: "my current job" or "career is
    /// offered" (the backend returns every offered career with active=true). When more than one row is active the
    /// flag is read as "offered" and the held job comes from the last confirmed apply (`heldCareerId`); otherwise
    /// it is read as the held job. Logged as a contract question in docs/progress/requests.md.
    /// </summary>
    public static class JobBoard
    {
        public static bool ActiveMeansHeld(IReadOnlyList<CareerRow> rows) => rows != null && rows.Count > 1 && rows.Count(r => r.Active) <= 1;

        public static List<CareerCard> Build(IReadOnlyList<CareerDef> config, IReadOnlyList<CareerRow> rows,
            Func<string, int> playerSkill, int livePhase, string heldCareerId)
        {
            config = config ?? Array.Empty<CareerDef>();
            rows = rows ?? Array.Empty<CareerRow>();
            bool activeHeld = ActiveMeansHeld(rows);
            var cards = new List<CareerCard>();
            var ids = new List<string>();
            foreach (var d in config) if (!ids.Contains(d.Id)) ids.Add(d.Id);
            foreach (var r in rows) if (!ids.Contains(r.Id)) ids.Add(r.Id);
            foreach (var id in ids)
            {
                var def = config.FirstOrDefault(d => d.Id == id);
                var row = rows.FirstOrDefault(r => r.Id == id);
                int phase = row?.Phase ?? def?.Phase ?? 1;
                int level = Math.Max(1, row?.Level ?? 1);
                var card = new CareerCard
                {
                    Id = id, Name = !string.IsNullOrEmpty(row?.Name) ? row.Name : def?.Name ?? id, Def = def, Row = row,
                    Level = level, Xp = row?.Xp ?? 0, Phase = phase,
                    Current = def?.Level(level), Next = def?.Level(level + 1),
                };
                card.PayCents = row?.PayCents ?? card.Current?.PayCents ?? 0;
                card.ShiftSec = row?.ShiftSec ?? card.Current?.ShiftSec ?? 0;
                bool held = activeHeld ? row != null && row.Active : heldCareerId == id && row != null;
                if (phase > livePhase) card.Status = CareerStatus.PhaseLocked;
                else if (row == null) card.Status = CareerStatus.NotOffered;
                else if (held) card.Status = CareerStatus.Hired;
                else
                {
                    card.Status = CareerStatus.CanApply;
                    foreach (var g in def?.Gates ?? new List<SkillGate>())
                    {
                        int have = playerSkill?.Invoke(g.Skill) ?? 1;
                        if (have < g.Level) { card.Status = CareerStatus.SkillGated; card.BlockingGate = g; card.PlayerSkill = have; break; }
                    }
                }
                cards.Add(card);
            }
            // Hired first, then open, then gated, then later phases; stable by config order within a group.
            return cards.Select((c, i) => (c, i)).OrderBy(x => Rank(x.c.Status)).ThenBy(x => x.c.Phase).ThenBy(x => x.i).Select(x => x.c).ToList();
        }

        private static int Rank(CareerStatus s) => s switch
        {
            CareerStatus.Hired => 0, CareerStatus.CanApply => 1, CareerStatus.SkillGated => 2, CareerStatus.NotOffered => 3, _ => 4,
        };
    }
}
