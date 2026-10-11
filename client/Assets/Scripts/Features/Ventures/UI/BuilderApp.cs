using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace CTLife.Ventures
{
    /// <summary>
    /// The Builder Hills phone app (id `builder`, Phase 5; spec feature #37). Found a project (type, name, ticker), see who is hiring,
    /// answer role offers, and run a project from its HQ screen: fund it with game Cash, send offers, remove people, quote and launch
    /// the token, enter the HQ room. Costs, odds and outcomes are the server's; routes the server lacks show "not available yet".
    /// </summary>
    public sealed class BuilderApp : VenturesApp
    {
        public override string Id => "builder";
        public override string Title => VStrings.T("app.builder");
        public override string Color => "#20bf6b";
        public override int Phase => 5;

        internal override VView CreateView(VenturesHub hub) => new BuilderView(hub);

        private enum Tab { Mine, Hiring, Offers, Found }

        private sealed class BuilderView : VView
        {
            private readonly VisualElement _body = El.Div();
            private readonly Dictionary<string, Page<ProjectSummary>> _lists = new Dictionary<string, Page<ProjectSummary>>();
            private readonly Dictionary<string, VError> _listErrors = new Dictionary<string, VError>();
            private readonly Dictionary<string, WriteFlow> _flows = new Dictionary<string, WriteFlow>(StringComparer.Ordinal);
            private readonly LaunchFlow _launch = new LaunchFlow();
            private Tab _tab = Tab.Mine;
            private string _hiringScope = "hiring";
            private Page<HireOffer> _offers;
            private VError _offersError;
            // found form
            private string _kind = ProjectKinds.All[0], _name = "", _ticker = "";
            // HQ
            private string _hqId;
            private ProjectDetail _detail;
            private VError _detailError;
            private bool _detailLoading, _quoteLoading, _dirty;
            private string _fund = "", _hirePlayer = "", _hireRole = ProjectKinds.HireRoles[0], _hireSalary = "", _launchTicker = "";
            private string _note = "";

            public BuilderView(VenturesHub hub) : base(hub, VStrings.T("builder.title"))
            {
                Page.name = "builder-page";
                Page.AddToClassList("duo-builder");
                Body.Add(VUi.Small(VStrings.T("builder.hint")));
                Body.Add(_body);
                Hub.HireOffered += OnHireOffer;
                Hub.ProjectEvented += OnProjectEvent;
                _launch.Changed += RequestRender;
                LoadList("mine");
                Render();
            }

            private long MyCash => Services.TryGet<PlayerProfile>(out var p) ? p.CashCents : 0;

            // ---------------------------------------------------------------- plumbing

            private WriteFlow Flow(string key)
            {
                if (!_flows.TryGetValue(key, out var f)) { _flows[key] = f = new WriteFlow(); f.Changed += RequestRender; }
                return f;
            }

            private bool TextFocused()
            {
                var f = Root.panel?.focusController?.focusedElement as VisualElement;
                return f is TextField || f != null && Root.Contains(f) && f.GetFirstAncestorOfType<TextField>() != null;
            }

            private void RequestRender()
            {
                if (!Alive) return;
                if (TextFocused()) { _dirty = true; return; }
                Render();
            }

            protected override void Tick() { if (_dirty && !TextFocused()) Render(); }

            private void OnHireOffer(HireOfferSignal s) { if (Alive) LoadOffers(); }

            private void OnProjectEvent(ProjectEventSignal s)
            {
                if (Alive && _hqId != null && s.projectEvent.ProjectId == _hqId) LoadDetail(_hqId);
            }

            private void Render()
            {
                if (!Alive) return;
                _dirty = false;
                _body.Clear();
                Page.Title?.Show(_hqId == null);
                if (_hqId == null) Page.Nav.SetTitle(VStrings.T("builder.title"));
                else Page.Nav.SetCollapsed(true, instant: true);
                if (_hqId != null) { RenderHq(); return; }
                var tabs = VUi.ChipRow();
                void Add(Tab t, string key) => tabs.Add(VUi.Chip(VStrings.T(key), () => { _tab = t; _note = ""; OnTab(t); Render(); }, _tab == t));
                Add(Tab.Mine, "builder.tab.mine");
                Add(Tab.Hiring, "builder.tab.hiring");
                Add(Tab.Offers, "builder.tab.offers");
                Add(Tab.Found, "builder.tab.found");
                _body.Add(tabs);
                if (!string.IsNullOrEmpty(_note)) _body.Add(VUi.Text(_note));
                switch (_tab)
                {
                    case Tab.Mine: RenderList("mine", "builder.mine.empty"); break;
                    case Tab.Hiring: RenderHiring(); break;
                    case Tab.Offers: RenderOffers(); break;
                    default: RenderFound(); break;
                }
            }

            private void OnTab(Tab t)
            {
                if (t == Tab.Mine) LoadList("mine");
                else if (t == Tab.Hiring) LoadList(_hiringScope);
                else if (t == Tab.Offers) LoadOffers();
            }

            // ---------------------------------------------------------------- project lists

            private void LoadList(string scope)
            {
                _listErrors.Remove(scope);
                Run(ct => Hub.Projects.List(scope, null, ct), p => { _lists[scope] = p; RequestRender(); },
                    e => { _listErrors[scope] = e; RequestRender(); }, false);
            }

            private void RenderList(string scope, string emptyKey)
            {
                if (_listErrors.TryGetValue(scope, out var err)) { _body.Add(ErrorCard(err, "builder.loadFailed", () => LoadList(scope))); return; }
                if (!_lists.TryGetValue(scope, out var page)) { _body.Add(VUi.Small(VStrings.T("common.loading"))); return; }
                if (page.Items.Count == 0) _body.Add(Duo.Empty(VStrings.T(emptyKey)));
                var list = El.Div("duo-list");
                foreach (var p in page.Items) list.Add(ProjectRow(p));
                Duo.MarkLast(list);
                _body.Add(list);
                if (page.Skipped > 0) _body.Add(VUi.Small(VStrings.F("common.skipped", page.Skipped)));
                if (!string.IsNullOrEmpty(page.NextCursor))
                    _body.Add(VUi.Btn(VStrings.T("common.more"), () =>
                        Run(ct => Hub.Projects.List(scope, page.NextCursor, ct), more =>
                        {
                            page.Items.AddRange(more.Items.Where(m => page.Items.All(x => x.ProjectId != m.ProjectId)));
                            page.NextCursor = more.NextCursor;
                            page.Skipped += more.Skipped;
                            RequestRender();
                        }, e => { _note = e.Message; RequestRender(); }, false), VUi.Plain));
            }

            private VisualElement ProjectRow(ProjectSummary p)
            {
                var lines = new List<string> { VStrings.Kind(p.Kind) + " · " + Money.Cash(p.TreasuryCents) + " · " + VStrings.F("project.card.people", p.MemberCount) };
                if (p.OpenRoles.Count > 0) lines.Add(VStrings.F("project.card.open", string.Join(", ", p.OpenRoles.Select(VStrings.Role))));
                else if (!string.IsNullOrEmpty(p.OwnerAlias)) lines.Add(VStrings.F("project.by", p.OwnerAlias));
                var row = new DuoRow(() => OpenHq(p.ProjectId)).Leading(El.PassThrough(El.Div("duo-app-tile duo-tint-" + KindTint(p.Kind), new UiIcon("projects"))))
                    .Title(p.Name, "duo-row-title-strong").Subtitle(string.Join("\n", lines), twoLines: true);
                row.name = "builder-project-" + p.ProjectId;
                row.Trailing(VUi.Status(VStrings.ProjectStatus(p.Status), ProjectTone(p.Status)));
                row.Chevron();
                return row;
            }

            private static string KindTint(string kind)
            {
                uint h = 2166136261;
                foreach (char ch in kind ?? "") h = (h ^ ch) * 16777619;
                string[] tints = { "indigo", "teal", "orange", "pink", "purple", "blue" };
                return tints[h % (uint)tints.Length];
            }

            private static PhonePresentation.Tone ProjectTone(string status) => status switch
            {
                "live" => PhonePresentation.Tone.Success,
                "launching" => PhonePresentation.Tone.Info,
                "paused" => PhonePresentation.Tone.Warning,
                "rugged" => PhonePresentation.Tone.Danger,
                "dead" => PhonePresentation.Tone.Neutral,
                "founded" => PhonePresentation.Tone.Accent,
                _ => PhonePresentation.Tone.Neutral,
            };

            private void RenderHiring()
            {
                _body.Add(VUi.ChipRow(VUi.Chip(VStrings.T("builder.scope.hiring"), () => { _hiringScope = "hiring"; LoadList("hiring"); Render(); }, _hiringScope == "hiring"),
                    VUi.Chip(VStrings.T("builder.scope.all"), () => { _hiringScope = "all"; LoadList("all"); Render(); }, _hiringScope == "all")));
                _body.Add(VUi.Small(VStrings.T("builder.hiring.note")));
                RenderList(_hiringScope, "builder.hiring.empty");
            }

            // ---------------------------------------------------------------- offers inbox

            private void LoadOffers()
            {
                _offersError = null;
                Run(ct => Hub.Projects.MyOffers(null, ct), p => { _offers = p; RequestRender(); }, e => { _offersError = e; RequestRender(); }, false);
            }

            private void RenderOffers()
            {
                _body.Add(Duo.SectionHeader(VStrings.T("offers.title")));
                if (_offersError != null) { _body.Add(ErrorCard(_offersError, "builder.loadFailed", LoadOffers)); return; }
                if (_offers == null) { LoadOffers(); _body.Add(VUi.Small(VStrings.T("common.loading"))); return; }
                var shown = _offers.Items.Where(o => o.Status == "pending" || o.Status == null || o.Status == "").ToList();
                if (shown.Count == 0) _body.Add(Duo.Empty(VStrings.T("offers.empty")));
                foreach (var o in shown) _body.Add(OfferCard(o));
                var old = _offers.Items.Except(shown).Take(5).ToList();
                foreach (var o in old)
                    _body.Add(VUi.Small(VStrings.F("offers.line", o.ProjectName, VStrings.Role(o.Role), Money.Cash(o.SalaryCentsPerWeek)) + " · " +
                        VStrings.F("offers.status", VStrings.Has("offers.status." + o.Status) ? VStrings.T("offers.status." + o.Status) : VWire.Readable(o.Status))));
            }

            private VisualElement OfferCard(HireOffer o)
            {
                var card = VUi.Card(VUi.H2(o.ProjectName), VUi.Text(VStrings.F("offers.line", o.ProjectName, VStrings.Role(o.Role), Money.Cash(o.SalaryCentsPerWeek))));
                if (o.ExpiresAtMs > 0) card.Add(VUi.Small(VStrings.F("time.expiresIn", VUi.Until(o.ExpiresAtMs, Hub.NowMs))));
                var accept = Flow("accept:" + o.OfferId);
                var decline = Flow("decline:" + o.OfferId);
                foreach (var f in new[] { accept, decline }) if (f.Error != null) card.Add(VUi.Error(f.Error.Message));
                bool expired = o.ExpiresAtMs > 0 && o.ExpiresAtMs <= Hub.NowMs;
                var a = VUi.Btn(VStrings.T("offers.accept"), () => Answer(o, true));
                var d = VUi.Btn(VStrings.T("offers.decline"), () => Answer(o, false), VUi.Plain);
                bool sending = accept.Stage == WriteStage.Sending || decline.Stage == WriteStage.Sending;
                a.SetEnabled(!sending && !expired);
                d.SetEnabled(!sending);
                card.Add(VUi.Row(a, d));
                if (expired) card.Add(VUi.Small(VStrings.T("time.expired")));
                return card;
            }

            private void Answer(HireOffer o, bool accept)
            {
                var f = Flow((accept ? "accept:" : "decline:") + o.OfferId);
                if (f.Stage == WriteStage.Done || f.Stage == WriteStage.Rejected) f.Reset();
                var path = "/project-offers/" + o.OfferId + (accept ? "/accept" : "/decline");
                var key = f.Begin("POST", path, "");
                if (key == null) return;
                Run(ct => accept ? Hub.Projects.AcceptOffer(o.OfferId, key, ct) : Hub.Projects.DeclineOffer(o.OfferId, key, ct), r =>
                {
                    f.Succeeded();
                    _note = accept ? VStrings.F("offers.accepted", o.ProjectName) : VStrings.T("offers.declined");
                    LoadOffers();
                    LoadList("mine");
                }, e => { f.Failed(e); if (e.OutcomeUnknown || e.Is("CONFLICT")) LoadOffers(); }, true);
            }

            // ---------------------------------------------------------------- found

            private void RenderFound()
            {
                var f = Flow("create");
                _body.Add(VUi.H2(VStrings.T("found.title")));
                _body.Add(VUi.Small(VStrings.T("found.kind")));
                var kinds = VUi.Row();
                foreach (var k in ProjectKinds.All) { var kk = k; kinds.Add(VUi.Chip(VStrings.Kind(k), () => { _kind = kk; Render(); }, _kind == k)); }
                _body.Add(kinds);
                _body.Add(VUi.Small(VStrings.Has("kindhint." + _kind) ? VStrings.T("kindhint." + _kind) : ""));
                _body.Add(VUi.Small(VStrings.T("found.name")));
                var name = VUi.Field("", _name, v => _name = v, 40);
                _body.Add(name);
                _body.Add(VUi.Small(VStrings.T("found.ticker")));
                _body.Add(VUi.Field("", _ticker, v => _ticker = v, 10));
                _body.Add(VUi.Small(VStrings.T("found.cost")));
                var nameProblem = ProjectRules.CheckName(_name);
                bool tickerBad = !string.IsNullOrWhiteSpace(_ticker) && !ProjectRules.IsTicker(_ticker);
                if (_name.Length > 0 && nameProblem != NameProblem.None) _body.Add(VUi.Error(VStrings.T("found.problem." + nameProblem)));
                if (tickerBad) _body.Add(VUi.Error(VStrings.T("found.problem.ticker")));
                if (f.Error != null) _body.Add(VUi.Error(f.Error.Message));
                var go = VUi.Btn(f.Stage == WriteStage.Unknown ? VStrings.T("common.retry") : VStrings.T("found.submit"), Found);
                go.SetEnabled(f.Stage != WriteStage.Sending);
                _body.Add(go);
            }

            private void Found()
            {
                var f = Flow("create");
                if (ProjectRules.CheckName(_name) != NameProblem.None || !string.IsNullOrWhiteSpace(_ticker) && !ProjectRules.IsTicker(_ticker)) { Render(); return; }
                if (f.Stage == WriteStage.Done || f.Stage == WriteStage.Rejected) f.Reset();
                var body = ProjectsApi.CreateBody(_kind, _name, _ticker);
                var key = f.Begin("POST", "/projects", body.ToString(Formatting.None));
                if (key == null) return;
                Run(ct => Hub.Projects.Create(body, key, ct), p =>
                {
                    f.Succeeded();
                    f.Reset();
                    Bus.Publish(new SfxSignal("sfx_ui_success"));
                    _name = "";
                    _ticker = "";
                    _note = VStrings.F("found.done", p.Name);
                    Hub.RefreshProfile(true);
                    LoadList("mine");
                    OpenHq(p.ProjectId);
                }, e => { f.Failed(e); if (e.OutcomeUnknown) { LoadList("mine"); Hub.RefreshProfile(true); } }, true);
            }

            // ---------------------------------------------------------------- HQ

            private void OpenHq(string id)
            {
                _hqId = id;
                _detail = null;
                _detailError = null;
                _note = "";
                _launch.SetQuote(null);
                _launchTicker = "";
                foreach (var k in _flows.Keys.Where(k => k.StartsWith("hq:", StringComparison.Ordinal)).ToList()) _flows[k].Reset();
                LoadDetail(id);
                Render();
            }

            private void LoadDetail(string id)
            {
                if (_detailLoading) return;
                _detailLoading = true;
                Run(ct => Hub.Projects.Detail(id, ct), d =>
                {
                    _detailLoading = false;
                    if (_hqId != id) return;
                    _detail = d;
                    _detailError = null;
                    RequestRender();
                }, e => { _detailLoading = false; _detailError = e; RequestRender(); }, false);
            }

            private void CloseHq()
            {
                _hqId = null;
                _detail = null;
                LoadList("mine");
                Render();
            }

            protected override bool Back()
            {
                if (_hqId == null) return false;
                CloseHq();
                return true;
            }

            private void RenderHq()
            {
                if (_detailError != null) { _body.Add(ErrorCard(_detailError, "hq.loadFailed", () => LoadDetail(_hqId))); return; }
                if (_detail == null) { _body.Add(VUi.Small(VStrings.T("common.loading"))); return; }
                var p = _detail.Project;
                var standing = ProjectRules.StandingOf(_detail, Hub.UserId);
                Page.Nav.SetTitle(p.Name);
                var card = VUi.Card(Duo.LargeTitle(p.Name), VUi.Status(VStrings.Kind(p.Kind) + " · " + VStrings.ProjectStatus(p.Status), ProjectTone(p.Status)));
                card.name = "builder-hq";
                // Hero: the project's treasury (server value).
                card.Add(Duo.Hero(VStrings.T("hq.treasury"), Money.Cash(p.TreasuryCents), null, null, VStrings.F("project.card.people", p.MemberCount)));
                if (!string.IsNullOrEmpty(p.OwnerAlias)) card.Add(VUi.Small(VStrings.F("project.by", p.OwnerAlias)));
                if (_detail.PayrollCentsPerWeek > 0) card.Add(VUi.Small(VStrings.F("hq.payroll", Money.Cash(_detail.PayrollCentsPerWeek))));
                if (_detail.NextPayrollAtMs.HasValue) card.Add(VUi.Small(VStrings.F("hq.nextPayroll", VUi.When(_detail.NextPayrollAtMs.Value))));
                if (standing == ProjectRules.Standing.Visitor) card.Add(VUi.Small(VStrings.T("hq.visitor")));
                _body.Add(card);
                if (!string.IsNullOrEmpty(_note)) _body.Add(VUi.Text(_note));
                RenderMembers(standing);
                RenderStakes();
                RenderEvents();
                bool dead = ProjectRules.IsDead(p.Status);
                if (!dead && ProjectRules.CanFund(standing)) RenderFund();
                if (!dead && ProjectRules.CanManage(standing)) RenderHire();
                if (ProjectRules.CanManage(standing) && p.Status == "founded") RenderLaunch();
                RenderEnter();
            }

            private void RenderMembers(ProjectRules.Standing standing)
            {
                var card = VUi.Card(VUi.H2(VStrings.T("hq.members")));
                var team = _detail.Members;
                if (team.Count == 0) card.Add(VUi.Small(VStrings.T("hq.noMembers")));
                foreach (var m in team)
                {
                    var line = m.Role == "founder" ? VStrings.F("hq.founderLine", m.Alias, VStrings.Role(m.Role))
                        : VStrings.F("hq.member.line", m.Alias, VStrings.Role(m.Role), Money.Cash(m.SalaryCentsPerWeek));
                    card.Add(VUi.Text(line));
                    if (ProjectRules.CanManage(standing) && m.Role != "founder")
                    {
                        var member = m;
                        var f = Flow("hq:fire:" + m.UserId);
                        if (f.Error != null) card.Add(VUi.Error(f.Error.Message));
                        var fire = VUi.Btn(VStrings.T("hq.fire"), () => Fire(member), VUi.Danger);
                        fire.SetEnabled(f.Stage != WriteStage.Sending);
                        card.Add(fire);
                    }
                }
                _body.Add(card);
            }

            private void Fire(ProjectMember m)
            {
                var f = Flow("hq:fire:" + m.UserId);
                if (f.Stage == WriteStage.Done || f.Stage == WriteStage.Rejected) f.Reset();
                var key = f.Begin("DELETE", "/projects/" + _hqId + "/members/" + m.UserId, "");
                if (key == null) return;
                var id = _hqId;
                Run(ct => Hub.Projects.Fire(id, m.UserId, key, ct), () => { f.Succeeded(); _note = VStrings.T("hq.fired"); LoadDetail(id); },
                    e => { f.Failed(e); if (e.OutcomeUnknown) LoadDetail(id); }, true);
            }

            private void RenderStakes()
            {
                var card = VUi.Card(VUi.H2(VStrings.T("hq.vc")));
                if (_detail.VcStakes.Count == 0) card.Add(VUi.Small(VStrings.T("hq.vc.none")));
                foreach (var s in _detail.VcStakes)
                    card.Add(VUi.Text(VStrings.F("hq.vc.line", VStrings.VcName(s.VcArchetypeId), Money.Cash(s.InvestedCents), ProjectRules.EquityText(s.EquityBps), VUi.Day(s.UnlockAtMs))));
                _body.Add(card);
            }

            private void RenderEvents()
            {
                var card = VUi.Card(VUi.H2(VStrings.T("hq.events")));
                if (_detail.Events.Count == 0) card.Add(VUi.Small(VStrings.T("hq.events.none")));
                var registry = Services.TryGet<ConfigStore>(out var cfg) ? cfg.Ids("project_events") : null;
                foreach (var e in _detail.Events.Take(12))
                {
                    var line = VStrings.ProjectEventLine(e) + " · " + VUi.When(e.AtMs);
                    card.Add(VenturesRegistry.IsRiskEvent(registry, e.EventId) ? VUi.Error(line) : VUi.Small(line));
                }
                _body.Add(card);
            }

            private void RenderFund()
            {
                var f = Flow("hq:fund");
                var card = VUi.Card(VUi.H2(VStrings.T("fund.title")), VUi.Small(VStrings.T("fund.hint")));
                card.Add(VUi.Small(VStrings.T("fund.amount")));
                card.Add(VUi.Field("", _fund, v => _fund = v, 14));
                var problem = _fund.Length == 0 ? ProjectRules.FundProblem.None : ProjectRules.CheckFund(_fund, MyCash, out _, out var shortfall);
                if (_fund.Length > 0 && problem == ProjectRules.FundProblem.Invalid) card.Add(VUi.Error(VStrings.T("fund.problem.Invalid")));
                if (_fund.Length > 0 && problem == ProjectRules.FundProblem.NotEnoughCash) card.Add(VUi.Error(VStrings.F("fund.problem.NotEnoughCash", Money.Cash(MyCash))));
                if (f.Error != null) card.Add(VUi.Error(f.Error.Message));
                var go = VUi.Btn(f.Stage == WriteStage.Unknown ? VStrings.T("common.retry") : VStrings.T("fund.submit"), Fund);
                go.SetEnabled(f.Stage != WriteStage.Sending);
                card.Add(go);
                _body.Add(card);
            }

            private void Fund()
            {
                var f = Flow("hq:fund");
                if (ProjectRules.CheckFund(_fund, MyCash, out var cents, out _) != ProjectRules.FundProblem.None) { Render(); return; }
                if (f.Stage == WriteStage.Done || f.Stage == WriteStage.Rejected) f.Reset();
                var body = ProjectsApi.FundBody(cents);
                var id = _hqId;
                var key = f.Begin("POST", "/projects/" + id + "/fund", body.ToString(Formatting.None));
                if (key == null) return;
                Run(ct => Hub.Projects.Fund(id, body, key, ct), p =>
                {
                    f.Succeeded();
                    f.Reset();
                    _fund = "";
                    _note = VStrings.T("fund.done");
                    Hub.RefreshProfile(true);
                    LoadDetail(id);
                }, e => { f.Failed(e); if (e.OutcomeUnknown) { Hub.RefreshProfile(true); LoadDetail(id); } }, true);
            }

            private void RenderHire()
            {
                var f = Flow("hq:hire");
                var card = VUi.Card(VUi.H2(VStrings.T("hire.title")), VUi.Small(VStrings.T("hire.hint")), VUi.Small(VStrings.T("hire.npcNote")));
                card.Add(VUi.Small(VStrings.T("hire.player")));
                card.Add(VUi.Field("", _hirePlayer, v => _hirePlayer = v, 40));
                card.Add(VUi.Small(VStrings.T("hire.role")));
                var roles = VUi.Row();
                foreach (var r in ProjectKinds.HireRoles) { var rr = r; roles.Add(VUi.Chip(VStrings.Role(r), () => { _hireRole = rr; Render(); }, _hireRole == r)); }
                card.Add(roles);
                card.Add(VUi.Small(VStrings.T("hire.salary")));
                card.Add(VUi.Field("", _hireSalary, v => _hireSalary = v, 14));
                var problem = ProjectRules.CheckHire(_hirePlayer, _hireRole, _hireSalary, Hub.UserId, out _);
                if (_hirePlayer.Length + _hireSalary.Length > 0 && problem != ProjectRules.HireProblem.None) card.Add(VUi.Error(VStrings.T("hire.problem." + problem)));
                if (f.Error != null) card.Add(VUi.Error(f.Error.Message));
                var go = VUi.Btn(f.Stage == WriteStage.Unknown ? VStrings.T("common.retry") : VStrings.T("hire.submit"), Hire);
                go.SetEnabled(f.Stage != WriteStage.Sending);
                card.Add(go);
                _body.Add(card);
            }

            private void Hire()
            {
                var f = Flow("hq:hire");
                if (ProjectRules.CheckHire(_hirePlayer, _hireRole, _hireSalary, Hub.UserId, out var salary) != ProjectRules.HireProblem.None) { Render(); return; }
                if (f.Stage == WriteStage.Done || f.Stage == WriteStage.Rejected) f.Reset();
                var body = ProjectsApi.OfferBody(_hirePlayer.Trim().ToLowerInvariant(), _hireRole, salary);
                var id = _hqId;
                var key = f.Begin("POST", "/projects/" + id + "/offers", body.ToString(Formatting.None));
                if (key == null) return;
                Run(ct => Hub.Projects.Offer(id, body, key, ct), o =>
                {
                    f.Succeeded();
                    f.Reset();
                    _hirePlayer = "";
                    _hireSalary = "";
                    _note = VStrings.T("hire.done");
                    LoadDetail(id);
                }, e => f.Failed(e), true);
            }

            private void RenderLaunch()
            {
                var card = VUi.Card(VUi.H2(VStrings.T("launch.title")), VUi.Small(VStrings.T("launch.hint")));
                card.Add(VUi.Small(VStrings.T("launch.ticker")));
                card.Add(VUi.Field("", _launchTicker, v => _launchTicker = v, 10));
                var w = Flow("hq:launch");
                var q = _launch.Quote;
                if (q != null)
                {
                    card.Add(VUi.Text(VStrings.F("launch.cost", Money.Cash(q.LaunchCostCents))));
                    if (q.BuilderDiscountBps > 0) card.Add(VUi.Small(VStrings.F("launch.discount", Money.Bps(q.BuilderDiscountBps, false))));
                    card.Add(VUi.Small(VStrings.F("launch.treasury", Money.Cash(q.TreasuryCents))));
                    foreach (var b in q.Blockers) card.Add(VUi.Error(VStrings.Has("launch.blocker." + b) ? VStrings.T("launch.blocker." + b) : VWire.Readable(b)));
                }
                if (_launch.Error != null) card.Add(VUi.Error(_launch.Error.Message));
                else if (w.Error != null) card.Add(VUi.Error(w.Error.Message));
                switch (_launch.Stage)
                {
                    case LaunchStage.Confirm:
                        card.Add(VUi.Text(VStrings.F("launch.confirming", ProjectRules.CleanTicker(_launchTicker), Money.Cash(_launch.ConfirmedCostCents))));
                        card.Add(VUi.Row(VUi.Btn(VStrings.F("launch.confirm", Money.Cash(_launch.ConfirmedCostCents)), Launch), VUi.Btn(VStrings.T("common.cancel"), _launch.Cancel, VUi.Plain)));
                        break;
                    case LaunchStage.Sending: card.Add(VUi.Text(VStrings.T("launch.sending"))); break;
                    case LaunchStage.Done: card.Add(VUi.Text(VStrings.T("launch.done"))); break;
                    default:
                    {
                        var quote = VUi.Btn(VStrings.T("launch.quote"), LoadQuote, VUi.Plain);
                        quote.SetEnabled(!_quoteLoading);
                        card.Add(quote);
                        if (q != null)
                        {
                            var gate = _launch.Check(_launchTicker);
                            if (gate == LaunchFlow.Gate.BadTicker || gate == LaunchFlow.Gate.NotEnoughTreasury || gate == LaunchFlow.Gate.Blocked)
                                card.Add(VUi.Small(VStrings.T("launch.gate." + gate)));
                            var go = VUi.Btn(VStrings.F("launch.confirm", Money.Cash(q.LaunchCostCents)), () => { _launch.AskConfirm(_launchTicker); });
                            go.SetEnabled(gate == LaunchFlow.Gate.Ok);
                            card.Add(go);
                        }
                        break;
                    }
                }
                _body.Add(card);
            }

            private void LoadQuote()
            {
                _quoteLoading = true;
                var id = _hqId;
                Run(ct => Hub.Projects.Quote(id, ct), q => { _quoteLoading = false; _launch.SetQuote(q); RequestRender(); },
                    e => { _quoteLoading = false; _note = e.Message; RequestRender(); }, false);
            }

            private void Launch()
            {
                var req = _launch.Request(_launchTicker);
                if (req == null || !_launch.BeginSend()) return;
                var w = Flow("hq:launch");
                if (w.Stage == WriteStage.Done || w.Stage == WriteStage.Rejected) w.Reset();
                var id = _hqId;
                var body = ProjectsApi.LaunchBody(req.Value.ticker, req.Value.quotedCostCents);
                var key = w.Begin("POST", "/projects/" + id + "/launch", body.ToString(Formatting.None));
                if (key == null) { _launch.Failed(null); return; }
                Run(ct => Hub.Projects.Launch(id, body, key, ct), p =>
                {
                    w.Succeeded();
                    _launch.Succeeded();
                    Bus.Publish(new SfxSignal("sfx_ui_success"));
                    LoadDetail(id);
                }, e =>
                {
                    w.Failed(e);
                    _launch.Failed(e);
                    if (e.OutcomeUnknown) LoadDetail(id);
                }, true);
            }

            private void RenderEnter()
            {
                var f = Flow("hq:enter");
                var card = VUi.Card();
                if (f.Error != null) card.Add(VUi.Error(f.Error.Message));
                if (Hub.Hq == null) card.Add(VUi.Small(VStrings.T("hq.noPresenter")));
                var go = VUi.Btn(VStrings.T("hq.enter"), EnterHq);
                go.SetEnabled(f.Stage != WriteStage.Sending);
                card.Add(go);
                _body.Add(card);
            }

            private void EnterHq()
            {
                var f = Flow("hq:enter");
                if (f.Stage == WriteStage.Done || f.Stage == WriteStage.Rejected) f.Reset();
                var id = _hqId;
                var key = f.Begin("POST", "/projects/" + id + "/hq/visit", "");
                if (key == null) return;
                var name = _detail?.Project?.Name;
                Run(async ct =>
                {
                    var ticket = await Hub.Projects.HqVisit(id, key, ct);
                    if (Hub.Hq != null) await Hub.Hq.Enter(ticket, id, name, ct);
                    return ticket;
                }, t =>
                {
                    f.Succeeded();
                    _note = Hub.Hq != null ? VStrings.T("hq.entered") : VStrings.T("hq.noPresenter");
                    RequestRender();
                }, e => f.Failed(e), true);
            }

            private VisualElement ErrorCard(VError e, string headKey, Action retry)
            {
                var card = VUi.Card();
                if (e.IsUnavailable) { card.Add(VUi.Text(VStrings.T("common.notAvailable"))); return card; }
                card.Add(VUi.Error(VStrings.T(headKey)));
                card.Add(VUi.Small(e.Message));
                card.Add(VUi.Btn(VStrings.T("common.retry"), retry, VUi.Plain));
                return card;
            }

            public override void Dispose()
            {
                if (Disposed) return;
                Hub.HireOffered -= OnHireOffer;
                Hub.ProjectEvented -= OnProjectEvent;
                _launch.Changed -= RequestRender;
                foreach (var f in _flows.Values) f.Changed -= RequestRender;
                base.Dispose();
            }
        }
    }
}
