using System;
using System.Collections.Generic;
using System.Linq;
using CTLife.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.UI
{
    /// <summary>Everything `POST /v1/characters` needs (contract section 4).</summary>
    public sealed class CharacterDraft
    {
        public string alias = "";
        public readonly string[] look = new string[7]; // [skinTone, headId, hairId, topId, bottomId, shoesId, accId]
        public string tribeId;
        public readonly List<string> traitIds = new List<string>();
        public string dreamId;
        public bool ageConfirmed;
    }

    /// <summary>
    /// An authored look, personality, then identity. Preview availability and permission to submit
    /// are separate: local art does not establish that the backend accepts its proposed item IDs.
    /// </summary>
    public sealed class CharacterCreatorScreen : UiScreen
    {
        private readonly Registries _reg;
        private readonly Action<CharacterDraft> _onSubmit;
        private readonly CharacterDraft _draft = new CharacterDraft();
        private readonly List<CreatorLookCatalog.Entry> _looks;
        private readonly Func<string[], bool> _serverLookConfirmed;
        private readonly bool _readOnlyReview;
        private readonly VisualElement[] _pages = new VisualElement[3];
        private readonly Button[] _tabs = new Button[3];
        private AvatarPreview _preview;
        private ConfigStore _config;
        private VisualElement _content, _tribeRow, _traitRow, _dreamRow;
        private Label _error, _guidance, _traitCount, _lookName, _lookCount, _lookStatus, _identitySummary;
        private Button _next, _back, _bodyFocus, _faceFocus;
        private int _step, _lookIndex, _dragPointer = -1;
        private bool _busy, _portrait, _reviewComplete;

        private CreatorLookCatalog.Entry SelectedLook => _looks.Count > 0 ? _looks[_lookIndex] : null;
        private bool CanSubmitLook => SelectedLook != null && _serverLookConfirmed != null
            && _serverLookConfirmed(_draft.look);

        // Existing callers remain source-compatible. The capability checks the selected look
        // against a live server payload; a bundled/merged registry cannot grant it by itself.
        public CharacterCreatorScreen(Registries reg, Action<CharacterDraft> onSubmit,
            Func<string[], bool> serverLookConfirmed = null, bool readOnlyReview = false)
        {
            _reg = reg;
            _onSubmit = onSubmit;
            _serverLookConfirmed = serverLookConfirmed;
            _readOnlyReview = readOnlyReview;
            _looks = CreatorLookCatalog.Available();
            Log.Info("creator", _looks.Count + " looks available");
            if (SelectedLook != null) Array.Copy(SelectedLook.look, _draft.look, _draft.look.Length);
        }

        protected override VisualElement Build()
        {
            _pages[0] = BuildLookPage();
            _pages[1] = BuildPersonalityPage();
            _pages[2] = BuildIdentityPage();
            _content = El.Div("ct-creator-content", _pages);

            var steps = El.Div("ct-creator-steps");
            for (int i = 0; i < _tabs.Length; i++)
            {
                int index = i;
                _tabs[i] = El.Btn(Strings.F("creator.step", i + 1, Strings.T("creator.step." + i)),
                    () => ShowStep(index), "ct-creator-tab");
                steps.Add(_tabs[i]);
            }
            _error = El.Text("", "ct-error");
            _guidance = El.Text("", "ct-small ct-muted ct-center");
            _back = El.Btn(Strings.T("common.back"), () => ShowStep(_step - 1), "ct-btn ct-btn-ghost ct-creator-back");
            _next = El.Btn("", Advance, "ct-btn ct-btn-primary ct-btn-big ct-grow");
            var footer = El.Div("ct-creator-footer", _error, _guidance, El.Div("ct-row ct-creator-actions", _back, _next));
            var header = El.Div("ct-creator-header", El.Div("ct-row", El.Brand(true), El.Div("ct-grow"),
                El.Text(Strings.T("creator.title"), "ct-creator-title")), steps);
            var root = El.Div("ct-creator", header, _content, footer);
            RefreshLook();
            Refresh();
            ShowStep(0);
            return root;
        }

        private VisualElement BuildLookPage()
        {
            _preview = new AvatarPreview();
            var surface = El.Div("ct-creator-model");
            surface.style.backgroundImage = Background.FromRenderTexture(_preview.Texture);
            surface.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target != surface || _dragPointer >= 0 || e.button != 0 || SelectedLook == null) return;
                _dragPointer = e.pointerId;
                surface.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            surface.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_dragPointer != e.pointerId) return;
                _preview.Spin(e.deltaPosition.x);
                e.StopPropagation();
            });
            void Release(int pointer)
            {
                if (_dragPointer != pointer) return;
                _dragPointer = -1;
                surface.ReleasePointer(pointer);
            }
            surface.RegisterCallback<PointerUpEvent>(e => Release(e.pointerId));
            surface.RegisterCallback<PointerCancelEvent>(e => Release(e.pointerId));
            surface.RegisterCallback<PointerCaptureOutEvent>(_ => _dragPointer = -1);

            var hint = El.PassThrough(El.Text(Strings.T("creator.preview"), "ct-preview-hint"));
            surface.Add(hint);
            if (SelectedLook == null)
            {
                hint.Show(false);
                surface.Add(El.PassThrough(El.Div("ct-creator-missing", El.Text(Strings.T("creator.lookMissing"), "ct-h2 ct-center"),
                    El.Text(Strings.T("creator.lookMissingDetail"), "ct-small ct-muted ct-center"))));
            }
            _bodyFocus = El.DockButton("body", Strings.T("creator.fullBody"), () => FocusPreview(false), "ct-creator-focus");
            _faceFocus = El.DockButton("face", Strings.T("creator.face"), () => FocusPreview(true), "ct-creator-focus");
            var focus = El.Div("ct-creator-focus-row", _bodyFocus, _faceFocus);
            focus.SetEnabled(SelectedLook != null);
            _lookName = El.Text("", "ct-creator-look-name");
            _lookCount = El.Text("", "ct-small ct-muted");
            _lookStatus = El.Text("", "ct-small ct-muted ct-center ct-creator-look-status");
            var previous = El.IconButton("back", Strings.T("creator.previousLook"), () => BrowseLook(-1));
            var next = El.IconButton("next", Strings.T("creator.nextLook"), () => BrowseLook(1));
            // A single authored look is not a carousel. Controls appear as distinct assets are supplied.
            previous.Show(_looks.Count > 1);
            next.Show(_looks.Count > 1);
            var selector = El.Div("ct-creator-look-selector", previous,
                El.Div("ct-grow ct-creator-look-copy", _lookName, _lookCount), next);
            surface.Add(focus);
            return El.Div("ct-creator-look-page", surface, El.Div("ct-creator-look-details", selector, _lookStatus));
        }

        private VisualElement BuildPersonalityPage()
        {
            _tribeRow = El.Div("ct-chips");
            foreach (var t in _reg.TribeOrder)
                _tribeRow.Add(Chip(t.name, () => { _draft.tribeId = t.id; Refresh(); }, t.id, "tribe", PerkText(t)));
            _traitRow = El.Div("ct-chips");
            foreach (var t in _reg.Traits) _traitRow.Add(Chip(t.name, () => ToggleTrait(t.id), t.id, "trait"));
            _dreamRow = El.Div("ct-chips");
            foreach (var d in _reg.Dreams)
                _dreamRow.Add(Chip(d.name, () => { _draft.dreamId = d.id; Refresh(); }, d.id, "dream"));
            _traitCount = El.Text("", "ct-selection-count");
            var scroll = El.VerticalScroll("ct-scroll ct-creator-scroll");
            scroll.Add(El.Div("ct-creator-section", El.Section("01", Strings.T("creator.tribe"), Strings.T("creator.tribeDetail")), _tribeRow));
            scroll.Add(El.Div("ct-creator-section", El.Section("02", Strings.T("creator.traits"), Strings.T("creator.traitsDetail")), _traitCount, _traitRow));
            scroll.Add(El.Div("ct-creator-section", El.Section("03", Strings.T("creator.dream"), Strings.T("creator.dreamDetail")), _dreamRow));
            return scroll;
        }

        private VisualElement BuildIdentityPage()
        {
            var alias = new TextField(Strings.T("creator.alias")) { maxLength = 20 };
            alias.AddToClassList("ct-field");
            // Device QA: the soft keyboard covered the field. Keep the OS input line above the keyboard visible.
            alias.textEdition.hideMobileInput = false;
            alias.RegisterValueChangedCallback(e => { _draft.alias = e.newValue.Trim(); _reviewComplete = false; Validate(); });
            var age = new Toggle(Strings.T("creator.age")).With("ct-toggle");
            age.RegisterValueChangedCallback(e => { _draft.ageConfirmed = e.newValue; _reviewComplete = false; Validate(); });
            _identitySummary = El.Text("", "ct-body");
            var scroll = El.VerticalScroll("ct-scroll ct-creator-scroll");
            scroll.Add(El.Div("ct-creator-section", El.Text(Strings.T("creator.identity"), "ct-h2"),
                alias, El.Text(Strings.T("creator.aliasHelp"), "ct-small ct-muted"), age));
            scroll.Add(El.Div("ct-card", El.Text(Strings.T("creator.yourStory"), "ct-eyebrow"), _identitySummary,
                El.Text(Strings.T("creator.editHint"), "ct-small ct-muted")));
            return scroll;
        }

        private void BrowseLook(int direction)
        {
            if (_busy || _looks.Count < 2) return;
            _lookIndex = (_lookIndex + direction + _looks.Count) % _looks.Count;
            _reviewComplete = false;
            RefreshLook();
            Refresh();
        }

        private void RefreshLook()
        {
            var look = SelectedLook;
            _lookName.text = look == null ? Strings.T("creator.look") : look.Display;
            _lookCount.text = _looks.Count > 1 ? Strings.F("creator.lookCount", _lookIndex + 1, _looks.Count) : Strings.T("creator.wholeLook");
            _lookCount.Show(look != null);
            if (look != null)
            {
                Array.Copy(look.look, _draft.look, _draft.look.Length);
                _preview.SetLook(_draft.look);
                _preview.SetYaw(look.previewYaw);
                _preview.SetPortrait(_portrait);
            }
            RefreshAvailability();
            FocusPreview(_portrait);
        }

        protected override void OnShown()
        {
            if (_config == null && Services.TryGet<ConfigStore>(out var config))
            {
                _config = config;
                _config.Changed += RefreshAvailability;
            }
            RefreshAvailability();
        }

        private void RefreshAvailability()
        {
            if (_lookStatus != null)
                _lookStatus.text = SelectedLook == null ? "" : Strings.T(CanSubmitLook ? "creator.lookReady" : "creator.lookPreviewOnly");
            Validate();
        }

        private void FocusPreview(bool portrait)
        {
            _portrait = portrait;
            _bodyFocus.EnableInClassList("ct-creator-focus-on", !portrait);
            _faceFocus.EnableInClassList("ct-creator-focus-on", portrait);
            _preview.SetPortrait(portrait);
        }

        private void ShowStep(int step)
        {
            if (_busy) return;
            _step = Mathf.Clamp(step, 0, _pages.Length - 1);
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Show(i == _step);
                _tabs[i].EnableInClassList("ct-creator-tab-on", i == _step);
            }
            _preview.SetVisible(_step == 0);
            Validate();
        }

        private void Advance()
        {
            if (_busy) return;
            if (_step < 2) { ShowStep(_step + 1); return; }
            if (DraftProblem() != null) return;
            if (_readOnlyReview)
            {
                // A local visual review must never call character creation with proposed item IDs.
                _reviewComplete = true;
                Validate();
            }
            else if (CanSubmitLook) _onSubmit?.Invoke(_draft);
        }

        private static string PerkText(TribeDef t) => t.perkText ?? t.perkKind switch
        {
            "launch_preview_ms" => Strings.F("creator.perk.launch", t.perkValue / 1000),
            "leverage_bonus_x" => Strings.F("creator.perk.leverage", t.perkValue),
            "prediction_fee_discount_bps" => Strings.F("creator.perk.prediction", t.perkValue / 100),
            "farm_yield_bonus_bps" => Strings.F("creator.perk.farm", t.perkValue / 100),
            "call_reach_multiplier" => Strings.F("creator.perk.reach", t.perkValue),
            "project_cost_discount_bps" => Strings.F("creator.perk.launchCost", t.perkValue / 100),
            "hidden_from_rich_list" => Strings.T("creator.perk.hidden"),
            _ => null,
        };

        private VisualElement Chip(string text, Action onClick, string id, string group, string sub = null)
        {
            var button = El.Btn("", onClick, "ct-chip", "sfx_ui_toggle_on");
            button.Add(El.Text(text, "ct-chip-title"));
            button.userData = group + ":" + id;
            if (!string.IsNullOrEmpty(sub)) button.Add(El.Text(sub, "ct-chip-sub"));
            return button;
        }

        private void ToggleTrait(string id)
        {
            if (_draft.traitIds.Contains(id)) _draft.traitIds.Remove(id);
            else
            {
                if (_draft.traitIds.Count >= 2) _draft.traitIds.RemoveAt(0);
                _draft.traitIds.Add(id);
            }
            Refresh();
        }

        private void Refresh()
        {
            _reviewComplete = false;
            Mark(_tribeRow, "tribe", id => id == _draft.tribeId);
            Mark(_traitRow, "trait", id => _draft.traitIds.Contains(id));
            Mark(_dreamRow, "dream", id => id == _draft.dreamId);
            if (_traitCount != null) _traitCount.text = Strings.F("creator.traitsCount", _draft.traitIds.Count);
            if (_identitySummary != null)
            {
                var names = new List<string>();
                if (SelectedLook != null) names.Add(SelectedLook.Display);
                var tribe = _reg.TribeOrder.FirstOrDefault(t => t.id == _draft.tribeId);
                if (tribe != null) names.Add(tribe.name);
                names.AddRange(_reg.Traits.Where(t => _draft.traitIds.Contains(t.id)).Select(t => t.name));
                var dream = _reg.Dreams.FirstOrDefault(d => d.id == _draft.dreamId);
                if (dream != null) names.Add(dream.name);
                _identitySummary.text = names.Count > 0 ? string.Join(" · ", names) : Strings.T("creator.storyEmpty");
            }
            Validate();
        }

        private static void Mark(VisualElement row, string group, Func<string, bool> selected)
        {
            if (row == null) return;
            foreach (var child in row.Children())
            {
                var tag = child.userData as string;
                if (tag != null && tag.StartsWith(group + ":")) child.EnableInClassList("ct-chip-on", selected(tag.Substring(group.Length + 1)));
            }
        }

        private string PersonalityProblem()
        {
            if (_draft.tribeId == null) return Strings.T("creator.needTribe");
            if (_draft.traitIds.Count != 2) return Strings.T("creator.needTraits");
            return _draft.dreamId == null ? Strings.T("creator.needDream") : null;
        }

        private string DraftProblem()
        {
            if (SelectedLook == null) return Strings.T("creator.needLook");
            if (PersonalityProblem() is string personality) return personality;
            if (_draft.alias.Length < 3) return Strings.T("creator.needAlias");
            return !_draft.ageConfirmed ? Strings.T("creator.needAge") : null;
        }

        private void Validate()
        {
            if (_next == null) return;
            _back.Show(_step > 0);
            _back.SetEnabled(!_busy);
            foreach (var tab in _tabs) tab?.SetEnabled(!_busy);
            _content?.SetEnabled(!_busy);
            string problem = _step == 0 ? (SelectedLook == null ? Strings.T("creator.needLook") : null)
                : _step == 1 ? PersonalityProblem() : DraftProblem();
            if (_step == 2 && !_readOnlyReview && !CanSubmitLook) problem = Strings.T("creator.lookNotAvailable");
            _next.SetEnabled(!_busy && problem == null && !_reviewComplete);
            _next.text = Strings.T(_busy ? "creator.saving" : _step == 0 ? "creator.toPersonality" : _step == 1 ? "creator.toIdentity"
                : _readOnlyReview ? (_reviewComplete ? "creator.previewComplete" : "creator.finishPreview") : "creator.submit");
            _guidance.text = problem ?? Strings.T(_step == 0 ? "creator.lookGuidance" : _step == 1 ? "creator.personalityReady"
                : _readOnlyReview ? (_reviewComplete ? "creator.previewSavedNothing" : "creator.previewNotice") : "creator.ready");
            _guidance.Show(problem != null || _step == 2);
            _error.Show(!string.IsNullOrEmpty(_error.text));
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        /// <summary>
        /// Development automation only (launch arg `autocreate=1`): picks the first tribe, two traits and dream,
        /// sets the alias and age confirmation, and submits through the normal path. Returns false if not allowed.
        /// </summary>
        public bool AutoSubmitForAutomation(string alias)
        {
            if (SelectedLook == null || _busy) return false;
            _draft.tribeId = _reg.TribeOrder.FirstOrDefault()?.id;
            _draft.traitIds.Clear();
            _draft.traitIds.AddRange(_reg.Traits.Take(2).Select(t => t.id));
            _draft.dreamId = _reg.Dreams.FirstOrDefault()?.id;
            _draft.alias = alias;
            _draft.ageConfirmed = true;
            Refresh();
            if (DraftProblem() != null || !CanSubmitLook) { Log.Warn("creator", "Automation draft rejected: " + (DraftProblem() ?? "look not submittable")); return false; }
            _onSubmit?.Invoke(_draft);
            return true;
        }
#endif

        public void ShowError(string text) { if (_error != null) { _error.text = text; _error.Show(!string.IsNullOrEmpty(text)); } }
        public void SetBusy(bool busy) { _busy = busy; Validate(); }

        public override void Close()
        {
            if (_config != null) _config.Changed -= RefreshAvailability;
            _config = null;
            base.Close();
            _preview?.Dispose();
            _preview = null;
        }
    }
}
