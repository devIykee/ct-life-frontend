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
    /// </summary>
    public sealed class CareersApp : PhoneApp, IDisposable
    {
        public const int GigsPhase = 4;

        public override string Id => "jobs";
        public override string Title => CareersStrings.T("app.title");
        public override string Color => "#0fb9b1";

        private readonly GigLedger _gigs = new GigLedger();
        private readonly HashSet<string> _gigInFlight = new HashSet<string>();
        private VisualElement _page, _content;
        private Button _tabCareers, _tabGigs;
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
            _tabCareers = CUi.B(CareersStrings.T("tab.careers"), () => { _showGigs = false; Load(); }, "ctc-btn ctc-tab");
            _tabGigs = CUi.B(CareersStrings.T("tab.gigs"), () => { _showGigs = true; Load(); }, "ctc-btn ctc-tab");
            _tabCareers.name = "careers-tab-careers";
            _tabGigs.name = "careers-tab-gigs";
            _content = CUi.Div("ctc-col");
            var scroll = El.VerticalScroll("ct-app-page");
            scroll.Add(CUi.Div("ctc-tabs", _tabCareers, _tabGigs));
            scroll.Add(_content);
            CUi.Sheet(scroll);
            _page = scroll;
            var page = scroll;
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
            CUi.Mark(_tabCareers, "ctc-selected", !_showGigs);
            CUi.Mark(_tabGigs, "ctc-selected", _showGigs);
            StopGigTick();
            if (_showGigs) LoadGigs(); else LoadCareers();
        }

        private void Message(string text, string cls, Action retry = null)
        {
            _content.Clear();
            _content.Add(CUi.L(text, cls));
            if (retry != null) _content.Add(CUi.B(CareersStrings.T("retry"), retry, "ctc-btn ctc-btn-primary"));
        }

        // ------------------------------------------------------------------ careers list

        private async void LoadCareers()
        {
            var ct = BeginView(out int version);
            Message(CareersStrings.T("loading"), "ctc-muted");
            try
            {
                var rows = await Deadline.Run(c => Api.Careers(c), ct, 30000);
                if (!Owns(version)) return;
                var cards = JobBoard.Build(ConfigCareers(), rows, Skill, Phases.Live, HeldCareer);
                _content.Clear();
                if (cards.Count == 0) { Message(CareersStrings.T("empty"), "ctc-muted"); return; }
                _content.Add(CUi.L(CareersStrings.T("payNote"), "ctc-muted"));
                foreach (var card in cards) _content.Add(CareerRowView(card));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception e)
            {
                Log.Exception("careers", e);
                if (Owns(version)) Message(e is TimeoutException ? CareersStrings.T("timeout") : Deadline.Describe(e), "ctc-error", LoadCareers);
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
            c.PayCents > 0 ? CareersStrings.F("pay", CareersWire.Cash(c.PayCents), c.ShiftSec) : CareersStrings.T("payUnknown");

        private VisualElement CareerRowView(CareerCard c)
        {
            string title = CareerFlavor.LevelTitle(c.Id, c.Level) ?? "";
            var row = CUi.Div("ctc-card ctc-career " + (c.Status == CareerStatus.Hired ? "ctc-hired" : ""),
                PhonePresentation.Status(StatusText(c), CareerTone(c.Status)),
                CUi.L(c.Name, "ctc-card-title"),
                CUi.L(CareersStrings.F("level", c.Level, title) + " · " + CareersStrings.F("xp", c.Xp), "ctc-muted"),
                CUi.L(PayLine(c), "ct-duo-pay-line"));
            var blurb = CareerFlavor.Blurb(c.Id);
            if (blurb != null) row.Add(CUi.L(blurb, "ctc-muted"));
            var actions = CUi.Div("ctc-actions");
            var primary = PrimaryAction(c);
            if (primary != null) actions.Add(primary);
            var details = CUi.B(CareersStrings.T("details"), () => ShowDetails(c), "ctc-btn");
            details.name = "careers-details-" + c.Id;
            actions.Add(details);
            row.Add(actions);
            return row;
        }

        private static PhonePresentation.Tone CareerTone(CareerStatus status) => status switch
        {
            CareerStatus.Hired => PhonePresentation.Tone.Success,
            CareerStatus.CanApply => PhonePresentation.Tone.Info,
            CareerStatus.SkillGated => PhonePresentation.Tone.Warning,
            _ => PhonePresentation.Tone.Neutral,
        };

        private Button PrimaryAction(CareerCard c)
        {
            if (c.Status == CareerStatus.Hired)
            {
                var work = CUi.B(CareersStrings.T(_shiftRunning ? "busy" : "work"), () => RunShift(c), "ctc-btn ctc-btn-primary");
                work.name = "careers-work-" + c.Id;
                work.SetEnabled(!_shiftRunning && CTLife.Core.Features.IsAvailable(CTLife.Core.Features.LedgerFreeze));
                return work;
            }
            if (c.Status == CareerStatus.CanApply)
            {
                var apply = CUi.B(CareersStrings.T("apply"), () => Apply(c), "ctc-btn ctc-btn-primary");
                apply.name = "careers-apply-" + c.Id;
                apply.SetEnabled(!_shiftRunning);
                return apply;
            }
            return null;
        }

        // ------------------------------------------------------------------ details

        private void ShowDetails(CareerCard c)
        {
            if (_page == null) return;
            _version++;
            _content.Clear();
            _content.Add(CUi.B(CareersStrings.T("back"), LoadCareers, "ctc-btn ctc-btn-small"));
            _content.Add(CUi.L(c.Name, "ctc-title"));
            var venue = CareerFlavor.Venue(c.Id);
            if (venue != null) _content.Add(CUi.L(CareersStrings.F("venue", venue), "ctc-muted"));
            var blurb = CareerFlavor.Blurb(c.Id);
            if (blurb != null) _content.Add(CUi.L(blurb, "ctc-body-text"));
            _content.Add(PhonePresentation.Status(StatusText(c), CareerTone(c.Status)));
            var primary = PrimaryAction(c);
            if (primary != null) _content.Add(CUi.Div("ctc-actions", primary));

            if (c.Def != null && c.Def.Levels.Count > 0)
            {
                _content.Add(CUi.L(CareersStrings.T("ladder"), "ctc-section"));
                foreach (var lv in c.Def.Levels)
                {
                    string text = CareersStrings.F("ladderRow", lv.Level, CareerFlavor.LevelTitle(c.Id, lv.Level) ?? "", CareersWire.Cash(lv.PayCents), lv.ShiftSec, lv.Xp);
                    var line = CUi.Div("ctc-ladder" + (lv.Level == c.Level ? " ctc-selected" : ""), CUi.L(text + (lv.Level == c.Level ? " · " + CareersStrings.T("ladderNow") : ""), "ctc-body-text"));
                    _content.Add(line);
                }
                _content.Add(CUi.L(c.IsTopLevel ? CareersStrings.T("topLevel") : c.Next != null ? CareersStrings.F("next", CareersWire.Cash(c.Next.PayCents)) : "", "ctc-muted"));
                _content.Add(CUi.L(CareersStrings.T("promotionNote"), "ctc-muted"));
            }

            _content.Add(CUi.L(CareersStrings.T("games"), "ctc-section"));
            var assigned = new HashSet<string>(c.Current?.MiniGameIds ?? new List<string>());
            if (!string.IsNullOrEmpty(c.Row?.MiniGameId)) assigned.Add(c.Row.MiniGameId);
            bool canPractice = PracticeAllowed(c);
            foreach (var spec in GameCatalog.Instance.ForCareer(c.Id))
            {
                var card = CUi.Div("ctc-card ctc-game",
                    CUi.L(spec.Title, "ctc-card-title"), CUi.L(spec.Tagline, "ctc-body-text"),
                    CUi.L(assigned.Contains(spec.Id) ? CareersStrings.T("gameAssigned") : spec.InContract ? "" : CareersStrings.T("gameProposed"), "ctc-muted"));
                var s = spec;
                var practice = CUi.B(CareersStrings.T("practice"), () => Practice(c, s), "ctc-btn");
                practice.name = "careers-practice-" + spec.Id;
                practice.SetEnabled(canPractice && !_shiftRunning && !MiniGameRunner.Busy);
                card.Add(CUi.Div("ctc-actions", practice));
                _content.Add(card);
            }
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
            if (Owns(version)) LoadCareers();
        }

        private async void RunShift(CareerCard c)
        {
            if (_shiftRunning || MiniGameRunner.Busy) return;
            _shiftRunning = true;
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
            if (!Phases.IsOpen(GigsPhase)) { Message(CareersStrings.F("gigs.phase", GigsPhase), "ctc-muted"); return; }
            if (_gigsUser != UserId) { _gigsUser = UserId; }
            Message(CareersStrings.T("gigs.loading"), "ctc-muted");
            try
            {
                var rows = await Deadline.Run(c => Api.Gigs(c), ct, 30000);
                if (!Owns(version)) return;
                _content.Clear();
                _gigRows.Clear();
                if (rows.Count == 0) { Message(CareersStrings.T("gigs.empty"), "ctc-muted"); return; }
                _content.Add(CUi.L(CareersStrings.T("gigs.note"), "ctc-muted"));
                foreach (var g in rows) _content.Add(GigRowView(g));
                StartGigTick();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception e)
            {
                Log.Exception("careers-gigs", e);
                if (Owns(version)) Message(Deadline.Describe(e), "ctc-error", LoadGigs);
            }
        }

        private VisualElement GigRowView(GigRow g)
        {
            var result = CUi.L("", "ctc-status");
            CUi.Show(result, false);
            var cooldown = CUi.L("", "ctc-muted");
            Button run = null;
            run = CUi.B(CareersStrings.T("gigs.run"), () => RunGig(g, run, result), "ctc-btn ctc-btn-primary");
            run.name = "careers-gig-" + g.Id;
            var meta = new List<string>();
            var pay = g.PayText();
            if (pay != null) meta.Add(CareersStrings.F("gigs.pay", pay));
            if (g.DurationSec.HasValue && g.DurationSec > 0) meta.Add(CareersStrings.F("gigs.duration", g.DurationSec));
            if (g.CooldownSec.HasValue && g.CooldownSec > 0) meta.Add(CareersStrings.F("gigs.every", CareersStrings.Duration(g.CooldownSec.Value)));
            var card = CUi.Div("ctc-card ctc-gig", CUi.L(g.Name, "ctc-card-title"));
            var tagline = CareerFlavor.GigTagline(g.Id);
            if (tagline != null) card.Add(CUi.L(tagline, "ctc-body-text"));
            if (!string.IsNullOrEmpty(g.Description)) card.Add(CUi.L(g.Description, "ctc-muted"));
            card.Add(CUi.L(string.Join(" · ", meta), "ctc-muted"));
            card.Add(CUi.Div("ctc-actions", run, cooldown));
            card.Add(result);
            _gigRows.Add((g.Id, run, cooldown, g.CooldownSec));
            return card;
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
