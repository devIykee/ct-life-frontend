using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.Careers
{
    /// <summary>
    /// The Jobs phone app (id "jobs"): all 14 careers × 5 levels from config `careers` joined with GET /careers,
    /// apply (skill gates from config, enforced by the server), paid shifts, practice for every career game, and side
    /// gigs (GET /gigs, POST /gigs/{id}/run, Phase 4). Every amount shown is a server or config value.
    /// Replaces CTLife.UI.JobsApp through PhoneCatalog's jobsOverride (hook in docs/progress/requests.md).
    /// Duo layout (board #7): segmented Careers / Side gigs, a featured card for the best-paying job you can do now,
    /// App-Store rows with an icon tile and a Work / Apply / Lv pill, details in a sheet.
    /// </summary>
    public sealed class CareersApp : PhoneApp, IDisposable
    {
        public const int GigsPhase = 4;
        private static readonly string[] Tints = { "blue", "orange", "purple", "teal", "pink", "indigo", "green", "red" };

        public override string Id => "jobs";
        public override string Title => CareersStrings.T("app.title");
        public override string Color => "#0fb9b1";
        public override DuoChrome Chrome => DuoChrome.Plain;

        private readonly GigLedger _gigs = new GigLedger();
        private readonly HashSet<string> _gigInFlight = new HashSet<string>();
        private VisualElement _page, _content;
        private DuoSegmented _tabs;
        private Label _held;
        private DuoSheet _details;
        private bool _showGigs;
        private int _version;
        private CancellationTokenSource _viewCts;
        private static bool _shiftRunning;
        private static int _practiceNonce = Environment.TickCount;
        private string _gigsUser;
        private Action _gigTick;
        private readonly List<(string gigId, Button button, Label cooldown, int? cooldownSec)> _gigRows = new List<(string, Button, Label, int?)>();

        private static CareersApi Api => new CareersApi(Services.Get<IRestClient>());

        public override VisualElement Open()
        {
            Closed();
            var page = new DuoPage(Title);
            page.name = "careers-page";
            page.AddToClassList("duo-jobs");
            CUi.Sheet(page);
            page.Nav.AddLeft(DuoGlyph.Kind.Back, Strings.T("common.back"), () => Phone?.Back(), "careers-back");
            _held = El.Text("", "duo-nav-note");
            _held.name = "careers-held";
            page.Nav.Right.Add(_held);
            _tabs = new DuoSegmented(new[] { CareersStrings.T("tab.careers"), CareersStrings.T("tab.gigs") }, _showGigs ? 1 : 0,
                i => { _showGigs = i == 1; Load(); });
            _tabs.Options[0].name = "careers-tab-careers";
            _tabs.Options[1].name = "careers-tab-gigs";
            _content = El.Div("duo-jobs-content");
            page.Body.Add(El.Div("duo-jobs-tabs", _tabs));
            page.Body.Add(_content);
            _page = page;
            page.RegisterCallback<DetachFromPanelEvent>(e => { if (ReferenceEquals(e.target, page) && ReferenceEquals(_page, page)) Closed(); });
            Load();
            return page;
        }

        public override void Closed()
        {
            _version++;
            _viewCts?.Cancel();
            _viewCts = null;
            StopGigTick();
            _details?.Close();
            _details = null;
            _page = null;
        }

        public void Dispose() => Closed();

        // ------------------------------------------------------------------ plumbing

        private CancellationToken BeginView(out int version)
        {
            _viewCts?.Cancel();
            _viewCts = new CancellationTokenSource();
            version = ++_version;
            var cts = _viewCts;
            var session = Services.Get<Session>();
            string user = session.UserId;
            void Changed() { if (!session.IsSignedIn || session.UserId != user) cts.Cancel(); }
            session.Changed += Changed;
            cts.Token.Register(() => session.Changed -= Changed);
            return cts.Token;
        }

        private bool Owns(int version) => version == _version && _page != null;

        /// <summary>
        /// Shifts and practice live in their own overlays above the phone, so closing or re-rendering the phone must
        /// not cancel them. Only a sign-out or account switch does.
        /// </summary>
        private static CancellationTokenSource SessionScope()
        {
            var cts = new CancellationTokenSource();
            var session = Services.Get<Session>();
            string user = session.UserId;
            void Changed() { if (!session.IsSignedIn || session.UserId != user) cts.Cancel(); }
            session.Changed += Changed;
            cts.Token.Register(() => session.Changed -= Changed);
            return cts;
        }

        private static string UserId => Services.TryGet<Session>(out var s) ? s.UserId : null;

        private static string HeldKey(string user) => "ctlife." + (Env.Current != null ? Env.Current.id : "env") + ".careers.held." + user;

        private static string HeldCareer
        {
            get { var u = UserId; return string.IsNullOrEmpty(u) ? null : PlayerPrefs.GetString(HeldKey(u), null); }
            set
            {
                var u = UserId;
                if (string.IsNullOrEmpty(u)) return;
                if (string.IsNullOrEmpty(value)) PlayerPrefs.DeleteKey(HeldKey(u)); else PlayerPrefs.SetString(HeldKey(u), value);
                PlayerPrefs.Save();
            }
        }

        private static int Skill(string name)
        {
            var skills = Services.TryGet<PlayerProfile>(out var p) ? p.Me?.Skills : null;
            if (skills == null) return 1;
            switch (name)
            {
                case "clout": return skills.Clout;
                case "coding": return skills.Coding;
                case "trading": return skills.Trading;
                case "writing": return skills.Writing;
                case "design": return skills.Design;
                case "security": return skills.Security;
                default: return 1;
            }
        }

        private static List<CareerDef> ConfigCareers() =>
            Services.TryGet<ConfigStore>(out var c) ? CareerConfig.Parse(c.Config("careers")) : new List<CareerDef>();

        private void Load()
        {
            if (_page == null) return;
            _tabs.Select(_showGigs ? 1 : 0, false);
            StopGigTick();
            if (_showGigs) LoadGigs(); else LoadCareers();
        }

        private void Message(string text, bool error = false, Action retry = null)
        {
            _content.Clear();
            _content.Add(error ? Duo.Notice(text, Duo.Tone.Danger) : Duo.Footnote(text));
            if (retry != null) _content.Add(El.Div("duo-topup-actions", Duo.Pill(CareersStrings.T("retry"), retry, Duo.PillKind.Primary)));
        }

        private static string Tint(string id)
        {
            uint h = 2166136261;
            foreach (char ch in id ?? "") h = (h ^ ch) * 16777619;
            return Tints[h % (uint)Tints.Length];
        }

        // ------------------------------------------------------------------ careers list

        private async void LoadCareers()
        {
            var ct = BeginView(out int version);
            Message(CareersStrings.T("loading"));
            try
            {
                var rows = await Deadline.Run(c => Api.Careers(c), ct, 30000);
                if (!Owns(version)) return;
                var cards = JobBoard.Build(ConfigCareers(), rows, Skill, Phases.Live, HeldCareer);
                _content.Clear();
                var held = cards.FirstOrDefault(c => c.Status == CareerStatus.Hired);
                _held.text = held != null ? CareersStrings.F("heldLine", held.Level, CareerFlavor.LevelTitle(held.Id, held.Level) ?? held.Name) : "";
                if (cards.Count == 0) { Message(CareersStrings.T("empty")); return; }
                // Featured: the best-paying job you can do right now (hired or hiring), from server/config pay.
                var best = cards.Where(c => (c.Status == CareerStatus.Hired || c.Status == CareerStatus.CanApply) && c.PayCents > 0)
                    .OrderByDescending(c => c.PayCents).FirstOrDefault();
                if (best != null) _content.Add(Featured(best));
                var list = El.Div("duo-list duo-jobs-list");
                foreach (var card in cards) list.Add(CareerRowView(card));
                Duo.MarkLast(list);
                _content.Add(list);
                _content.Add(Duo.Footnote(CareersStrings.T("payNote")));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception e)
            {
                Log.Exception("careers", e);
                if (Owns(version)) Message(e is TimeoutException ? CareersStrings.T("timeout") : Deadline.Describe(e), true, LoadCareers);
            }
        }

        private static string StatusText(CareerCard c)
        {
            switch (c.Status)
            {
                case CareerStatus.Hired: return CareersStrings.T("status.hired");
                case CareerStatus.CanApply: return CareersStrings.T("status.apply");
                case CareerStatus.SkillGated: return CareersStrings.F("status.gated", CareersStrings.SkillName(c.BlockingGate.Skill), c.BlockingGate.Level, c.PlayerSkill);
                case CareerStatus.PhaseLocked: return CareersStrings.F("status.phase", c.Phase);
                default: return CareersStrings.T("status.notOffered");
            }
        }

        private static string PayLine(CareerCard c) =>
            c.PayCents > 0 ? CareersStrings.F("rowPay", CareersWire.Cash(c.PayCents), c.ShiftSec) : CareersStrings.T("payUnknown");

        private VisualElement Featured(CareerCard c)
        {
            var card = El.Div("duo-feature");
            card.name = "careers-featured";
            var sheen = El.Div("duo-feature-sheen");
            sheen.style.backgroundImage = Background.FromTexture2D(Duo.Sheen);
            card.Add(El.PassThrough(sheen));
            card.Add(El.PassThrough(El.Div("duo-feature-art", new UiIcon("jobs"))));
            card.Add(El.Text(CareersStrings.T("best").ToUpperInvariant(), "duo-feature-kicker"));
            card.Add(El.Text(c.Name, "duo-feature-title"));
            card.Add(El.Text(PayLine(c), "duo-feature-meta"));
            var action = PrimaryAction(c);
            if (action != null)
            {
                action.AddToClassList("duo-feature-pill");
                card.Add(El.Div("duo-feature-actions", action));
            }
            return card;
        }

        private VisualElement CareerRowView(CareerCard c)
        {
            string title = CareerFlavor.LevelTitle(c.Id, c.Level) ?? "";
            var row = new DuoRow(() => ShowDetails(c)).Leading(El.PassThrough(El.Div("duo-app-tile duo-tint-" + Tint(c.Id), new UiIcon("jobs"))))
                .Title(c.Name, "duo-row-title-strong")
                .Subtitle(PayLine(c) + " · " + CareersStrings.F("level", c.Level, title), twoLines: true);
            row.name = "careers-details-" + c.Id;
            row.EnableInClassList("duo-row-hired", c.Status == CareerStatus.Hired);
            row.Trailing(TrailingPill(c));
            return row;
        }

        /// <summary>Work (your job), Apply (hiring), or a quiet locked pill (skill level or phase gate).</summary>
        private VisualElement TrailingPill(CareerCard c)
        {
            var primary = PrimaryAction(c);
            if (primary != null) return primary;
            if (c.Status == CareerStatus.SkillGated)
            {
                var gated = Duo.Pill(CareersStrings.F("pill.level", c.BlockingGate.Level), () => ShowDetails(c), Duo.PillKind.Locked);
                gated.tooltip = StatusText(c);
                return gated;
            }
            if (c.Status == CareerStatus.PhaseLocked)
            {
                var soon = Duo.Pill(CareersStrings.T("pill.soon"), () => ShowDetails(c), Duo.PillKind.Locked);
                soon.tooltip = StatusText(c);
                return soon;
            }
            return null;
        }

        private Button PrimaryAction(CareerCard c)
        {
            if (c.Status == CareerStatus.Hired)
            {
                var work = Duo.Pill(CareersStrings.T(_shiftRunning ? "pill.busy" : "pill.work"), () => RunShift(c), Duo.PillKind.Primary);
                work.name = "careers-work-" + c.Id;
                work.tooltip = CareersStrings.T(_shiftRunning ? "busy" : "work");
                work.SetEnabled(!_shiftRunning && CTLife.Core.Features.IsAvailable(CTLife.Core.Features.LedgerFreeze));
                return work;
            }
            if (c.Status == CareerStatus.CanApply)
            {
                var apply = Duo.Pill(CareersStrings.T("apply"), () => Apply(c));
                apply.name = "careers-apply-" + c.Id;
                apply.SetEnabled(!_shiftRunning);
                return apply;
            }
            return null;
        }

        // ------------------------------------------------------------------ details (sheet)

        private void ShowDetails(CareerCard c)
        {
            if (_page == null) return;
            _details?.Close();
            var venue = CareerFlavor.Venue(c.Id);
            var sheet = new DuoSheet(c.Name, venue != null ? CareersStrings.F("venue", venue) : null);
            sheet.name = "careers-details-sheet";
            var body = El.Div("duo-jobs-details");
            var blurb = CareerFlavor.Blurb(c.Id);
            if (blurb != null) body.Add(El.Text(blurb, "duo-sheet-body-text"));
            body.Add(Duo.Notice(StatusText(c), c.Status == CareerStatus.SkillGated ? Duo.Tone.Warning : c.Status == CareerStatus.Hired ? Duo.Tone.Info : Duo.Tone.Neutral));
            var primary = PrimaryAction(c);
            if (primary != null) body.Add(El.Div("duo-topup-actions", primary));

            if (c.Def != null && c.Def.Levels.Count > 0)
            {
                body.Add(Duo.GroupHeader(CareersStrings.T("ladder")));
                var ladder = El.Div("duo-list");
                foreach (var lv in c.Def.Levels)
                {
                    var row = new DuoRow().Title("L" + lv.Level + " · " + (CareerFlavor.LevelTitle(c.Id, lv.Level) ?? ""), lv.Level == c.Level ? "duo-row-title-strong" : null)
                        .Subtitle(CareersStrings.F("ladderPay", CareersWire.Cash(lv.PayCents), lv.ShiftSec, lv.Xp));
                    if (lv.Level == c.Level) row.Value(CareersStrings.T("ladderNow"), "duo-value-positive");
                    ladder.Add(row);
                }
                Duo.MarkLast(ladder);
                body.Add(ladder);
                body.Add(Duo.Footnote((c.IsTopLevel ? CareersStrings.T("topLevel") : c.Next != null ? CareersStrings.F("next", CareersWire.Cash(c.Next.PayCents)) : "")
                    + " " + CareersStrings.T("promotionNote"), "duo-topup-note"));
            }

            body.Add(Duo.GroupHeader(CareersStrings.T("games")));
            var assigned = new HashSet<string>(c.Current?.MiniGameIds ?? new List<string>());
            if (!string.IsNullOrEmpty(c.Row?.MiniGameId)) assigned.Add(c.Row.MiniGameId);
            bool canPractice = PracticeAllowed(c);
            var games = El.Div("duo-list");
            foreach (var spec in GameCatalog.Instance.ForCareer(c.Id))
            {
                var s = spec;
                var practice = Duo.Pill(CareersStrings.T("practice"), () => Practice(c, s));
                practice.name = "careers-practice-" + spec.Id;
                practice.SetEnabled(canPractice && !_shiftRunning && !MiniGameRunner.Busy);
                string note = assigned.Contains(spec.Id) ? CareersStrings.T("gameAssigned") : spec.InContract ? "" : CareersStrings.T("gameProposed");
                games.Add(new DuoRow().Title(spec.Title, "duo-row-title-strong")
                    .Subtitle(spec.Tagline + (note.Length > 0 ? " · " + note : ""), twoLines: true).Trailing(practice));
            }
            Duo.MarkLast(games);
            body.Add(games);
            var scroll = El.VerticalScroll("duo-sheet-scroll");
            scroll.Add(body);
            sheet.AddContent(scroll);
            sheet.AddSecondary(CareersStrings.T("back"));
            _details = sheet;
            sheet.Closed += () => { if (_details == sheet) _details = null; };
            sheet.Show(_page);
        }

        /// <summary>Practice never touches the server. Production builds respect the phase gate; dev builds allow
        /// practising every career so the 42 games can be reviewed in the real flow.</summary>
        private static bool PracticeAllowed(CareerCard c)
        {
#if CTLIFE_PROD
            return c.Status != CareerStatus.PhaseLocked;
#else
            return true;
#endif
        }

        // ------------------------------------------------------------------ actions

        private async void Apply(CareerCard c)
        {
            if (_shiftRunning) return;
            var ct = BeginView(out int version);
            try
            {
                var grant = await Deadline.Run(x => Api.Apply(c.Id, Ids.NewIdempotencyKey(), x), ct, 30000);
                HeldCareer = grant.CareerId;
                CUi.Toast(CareersStrings.F("hired", c.Name), ToastKind.Good);
                CUi.Sfx("sfx_ui_success");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                Log.Exception("careers-apply", e);
                CUi.Toast(CareersStrings.F("applyFailed", Deadline.Describe(e)), ToastKind.Bad);
            }
            _details?.Close();
            if (Owns(version)) LoadCareers();
        }

        private async void RunShift(CareerCard c)
        {
            if (_shiftRunning || MiniGameRunner.Busy) return;
            _shiftRunning = true;
            _details?.Close();
            using var scope = SessionScope();
            var ct = scope.Token;
            ShiftRunner runner = null;
            bool settled = false;
            try
            {
                runner = new ShiftRunner(Api, c);
                settled = await runner.Run(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Log.Exception("careers-shift", e);
                CUi.Toast(Deadline.Describe(e), ToastKind.Bad);
            }
            finally
            {
                runner?.Dispose();
                _shiftRunning = false;
            }
            if (settled && Phone != null && Phone.IsOpen) Phone.Close();
            else if (_page != null) LoadCareers();
        }

        private async void Practice(CareerCard c, MiniGameSpec spec)
        {
            if (_shiftRunning || MiniGameRunner.Busy) return;
            int version = ++_version;
            _details?.Close();
            using var scope = SessionScope();
            var ct = scope.Token;
            var reg = Services.TryGet<ConfigStore>(out var config) && config.Registries.MiniGames.TryGetValue(spec.Id, out var def) ? def : null;
            var game = GameCatalog.Instance.Resolve(spec.Id, reg?.maxScore, reg?.durationSec, reg?.careerId)
                       ?? GameCatalog.Instance.Resolve(spec.Id, null, null, null);
            try
            {
                await MiniGameRunner.Run(new MiniGamePlay
                {
                    Game = game, Seed = MiniGameSeed.ForPractice(spec.Id, Interlocked.Increment(ref _practiceNonce)), Practice = true, Context = c.Name,
                }, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Log.Exception("careers-practice", e);
                CUi.Toast(CareersStrings.T("mg.unavailable"), ToastKind.Bad);
            }
            if (_page != null && version == _version) ShowDetails(c);
        }

        // ------------------------------------------------------------------ gigs

        private async void LoadGigs()
        {
            var ct = BeginView(out int version);
            if (!Phases.IsOpen(GigsPhase)) { Message(CareersStrings.F("gigs.phase", GigsPhase)); return; }
            if (_gigsUser != UserId) { _gigsUser = UserId; }
            Message(CareersStrings.T("gigs.loading"));
            try
            {
                var rows = await Deadline.Run(c => Api.Gigs(c), ct, 30000);
                if (!Owns(version)) return;
                _content.Clear();
                _gigRows.Clear();
                if (rows.Count == 0) { Message(CareersStrings.T("gigs.empty")); return; }
                var list = El.Div("duo-list duo-jobs-list");
                foreach (var g in rows) list.Add(GigRowView(g));
                Duo.MarkLast(list);
                _content.Add(list);
                _content.Add(Duo.Footnote(CareersStrings.T("gigs.note")));
                StartGigTick();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception e)
            {
                Log.Exception("careers-gigs", e);
                if (Owns(version)) Message(Deadline.Describe(e), true, LoadGigs);
            }
        }

        private VisualElement GigRowView(GigRow g)
        {
            var result = El.Text("", "duo-gig-result");
            result.Show(false);
            var cooldown = El.Text("", "duo-gig-cooldown");
            Button run = null;
            run = Duo.Pill(CareersStrings.T("gigs.run"), () => RunGig(g, run, result), Duo.PillKind.Primary);
            run.name = "careers-gig-" + g.Id;
            var meta = new List<string>();
            var pay = g.PayText();
            if (pay != null) meta.Add(CareersStrings.F("gigs.pay", pay));
            if (g.DurationSec.HasValue && g.DurationSec > 0) meta.Add(CareersStrings.F("gigs.duration", g.DurationSec));
            if (g.CooldownSec.HasValue && g.CooldownSec > 0) meta.Add(CareersStrings.F("gigs.every", CareersStrings.Duration(g.CooldownSec.Value)));
            var tagline = CareerFlavor.GigTagline(g.Id) ?? g.Description;
            var row = new DuoRow().Leading(El.PassThrough(El.Div("duo-app-tile duo-tint-" + Tint(g.Id), new UiIcon("projects"))))
                .Title(g.Name, "duo-row-title-strong")
                .Subtitle(string.Join(" · ", meta) + (string.IsNullOrEmpty(tagline) ? "" : "\n" + tagline), twoLines: true)
                .Trailing(El.Div("duo-gig-action", run, cooldown));
            row.name = "careers-gig-row-" + g.Id;
            row.TextColumn.Add(result);
            _gigRows.Add((g.Id, run, cooldown, g.CooldownSec));
            return row;
        }

        private async void RunGig(GigRow g, Button button, Label result)
        {
            double now = CUi.Now;
            if (!_gigs.Ready(g.Id, now) || !_gigInFlight.Add(g.Id)) return;
            using var scope = SessionScope();
            var ct = scope.Token;
            button.SetEnabled(false);
            string key = _gigs.KeyFor(g.Id);
            try
            {
                var receipt = await Deadline.Run(c => Api.RunGig(g.Id, key, c), ct, 30000);
                _gigs.Resolved(g.Id);
                _gigs.StartCooldown(g.Id, CUi.Now, g.CooldownSec ?? 0);
                var outcome = CareerFlavor.GigOutcome(g.Id, receipt.OutcomeId);
                result.text = CareersStrings.F("gigs.paid", CareersWire.Cash(receipt.PayCents)) + (outcome != null ? " · " + outcome : "");
                CUi.Mark(result, "ctc-good", true);
                CUi.Sfx("sfx_ui_success");
                if (Services.TryGet<PlayerProfile>(out var profile))
                    try { await Deadline.Run(async c => { await profile.Refresh(c); return true; }, ct, 15000); }
                    catch (Exception e) when (!(e is OperationCanceledException)) { Log.Exception("careers-gig-profile", e); }
            }
            catch (OperationCanceledException) { return; }
            catch (ApiException e) when (e.Is("RATE_LIMITED") || e.Status == 429)
            {
                _gigs.Resolved(g.Id);
                double wait = GigLedger.RetryAfterSeconds(e.Details) ?? g.CooldownSec ?? 30;
                _gigs.StartCooldown(g.Id, CUi.Now, wait);
                result.text = CareersStrings.F("gigs.rateLimited", CareersStrings.Duration(wait));
                CUi.Mark(result, "ctc-good", false);
            }
            catch (Exception e) when (Deadline.Ambiguous(e))
            {
                // Keep the key: tapping again replays the first answer instead of running the gig twice.
                result.text = CareersStrings.T("gigs.unconfirmed");
                CUi.Mark(result, "ctc-good", false);
            }
            catch (Exception e)
            {
                _gigs.Resolved(g.Id);
                Log.Exception("careers-gig", e);
                result.text = CareersStrings.F("gigs.failed", Deadline.Describe(e));
                CUi.Mark(result, "ctc-good", false);
            }
            finally
            {
                _gigInFlight.Remove(g.Id);
                CUi.Show(result, true);
                button.SetEnabled(_gigs.Ready(g.Id, CUi.Now));
            }
        }

        private void StartGigTick()
        {
            StopGigTick();
            if (UiRoot.Instance == null) return;
            _gigTick = () =>
            {
                double now = CUi.Now;
                foreach (var (gigId, button, cooldown, _) in _gigRows)
                {
                    double left = _gigs.SecondsLeft(gigId, now);
                    string text = left > 0 ? CareersStrings.F("gigs.cooldown", CareersStrings.Duration(left)) : "";
                    if (cooldown.text != text) cooldown.text = text;
                    cooldown.Show(text.Length > 0);
                    bool ready = left <= 0 && !_gigInFlight.Contains(gigId);
                    if (ready != button.enabledSelf) button.SetEnabled(ready);
                }
            };
            UiRoot.Instance.AddUpdater(_gigTick);
        }

        private void StopGigTick()
        {
            if (_gigTick != null && UiRoot.Instance != null) UiRoot.Instance.RemoveUpdater(_gigTick);
            _gigTick = null;
        }
    }
}
