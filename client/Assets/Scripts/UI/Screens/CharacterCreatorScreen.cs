using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.UI.Kit;
using UnityEngine;
using UnityEngine.UIElements;
using Motion = CTLife.UI.Kit.Motion;

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
    /// The character creator, one decision per step (spec docs/research/duolingo-design-teardown.md C2, redesign
    /// 2026-10-08): Look, Tribe, Traits (2), Dream, Name, Confirm (with the 18+ confirmation), then a celebration and
    /// "Enter the city". The player's own character is the reactor: it stands on the painted town, turns when dragged and
    /// answers each pick with an emote and a short first-person line. The step machine and validation live in
    /// <see cref="CreatorFlow"/>; the draft survives pause/resume (<see cref="CreatorDraftStore"/>).
    /// Preview availability and permission to submit are separate: local art does not establish that the backend accepts
    /// its proposed item IDs (unchanged).
    /// </summary>
    public sealed class CharacterCreatorScreen : UiScreen
    {
        private const string FullIdle = "loco_idle", FaceIdle = "idle_fidget_foldarms";

        private readonly Registries _reg;
        private readonly Action<CharacterDraft> _onSubmit;
        private readonly CharacterDraft _draft = new CharacterDraft();
        private readonly CreatorFlow _flow;
        private readonly List<CreatorLookCatalog.Entry> _looks;
        private readonly Func<string[], bool> _serverLookConfirmed;
        private readonly bool _readOnlyReview;
        private readonly VisualElement[] _pages = new VisualElement[CreatorFlow.StepCount];
        private readonly List<ChoiceCard> _tribeCards = new List<ChoiceCard>(), _traitCards = new List<ChoiceCard>(), _dreamCards = new List<ChoiceCard>();
        private readonly List<ChoiceCard> _summary = new List<ChoiceCard>();
        private CreatorDraftStore _store;
        private OnboardingFrame _frame;
        private AvatarPreview _preview;
        private ConfigStore _config;
        private SpeechBubble _bubble;
        private VisualElement _content, _stage, _model, _lookControls, _dim, _celebrate, _traitDots, _summaryGrid, _originHost;
        private Label _lookName, _lookCount, _lookTag, _dragHint, _error, _aliasStatus, _confirmTitle, _celebrateTitle;
        private TextField _alias;
        private ChoiceCard _ageCard;
        private LippedButton _bodyFocus, _faceFocus, _done, _enterCity;
        private IVisualElementScheduledItem _sway, _keyboardPoll, _nameLine;
        private TaskCompletionSource<bool> _celebrated;
        private int _lookIndex, _dragPointer = -1, _lookChanges, _keyboardMissing;
        private bool _busy, _portrait, _reviewComplete, _touched, _typing, _returnToConfirm, _built;
        private float _swayStart, _swayBase, _lastReaction = -10f, _focusAt;

        private CreatorLookCatalog.Entry SelectedLook => _looks.Count > 0 ? _looks[_lookIndex] : null;
        private bool CanSubmitLook => SelectedLook != null && _serverLookConfirmed != null && _serverLookConfirmed(_draft.look);
        private string LookKey => SelectedLook == null ? null : string.Join("|", SelectedLook.look);

        /// <summary>The lottery result for the summary card. Set by the caller, or read from the signed-in profile when shown.</summary>
        public OriginSummary Origin { get; set; }

        // Existing callers remain source-compatible. The capability checks the selected look
        // against a live server payload; a bundled/merged registry cannot grant it by itself.
        public CharacterCreatorScreen(Registries reg, Action<CharacterDraft> onSubmit,
            Func<string[], bool> serverLookConfirmed = null, bool readOnlyReview = false)
        {
            _reg = reg;
            _onSubmit = onSubmit;
            _serverLookConfirmed = serverLookConfirmed;
            _readOnlyReview = readOnlyReview;
            _flow = new CreatorFlow(_draft) { ReadOnlyReview = readOnlyReview };
            _looks = CreatorLookCatalog.Available();
            Log.Info("creator", _looks.Count + " looks available");
            if (SelectedLook != null) Array.Copy(SelectedLook.look, _draft.look, _draft.look.Length);
        }

        public CreatorStep Step => _flow.Step;
        public CreatorFlow Flow => _flow;

        protected override VisualElement Build()
        {
            string account = Services.TryGet<PlayerProfile>(out var profile) ? profile.UserId : null;
            _store = new CreatorDraftStore(account);
            var saved = _store.Load();

            _frame = new OnboardingFrame("ob-creator", GoBack, Advance, Strings.T("ob.next"));
            _frame.Cta.EnableShine();
            _content = _frame.Content;

            _dim = El.Div("ob-dim");
            _dim.pickingMode = PickingMode.Ignore;
            _content.Add(_dim);
            BuildStage();
            BuildLookControls();
            var right = El.Div("ob-right");
            right.pickingMode = PickingMode.Ignore;
            _pages[(int)CreatorStep.Look] = BuildLookPage();
            _pages[(int)CreatorStep.Tribe] = BuildTribePage();
            _pages[(int)CreatorStep.Traits] = BuildTraitsPage();
            _pages[(int)CreatorStep.Dream] = BuildDreamPage();
            _pages[(int)CreatorStep.Name] = BuildNamePage();
            _pages[(int)CreatorStep.Confirm] = BuildConfirmPage();
            foreach (var p in _pages) { p.AddToClassList("ob-page"); p.style.display = DisplayStyle.None; right.Add(p); }
            _content.Add(right);

            _bubble = new SpeechBubble(SpeechBubble.Tail.BottomLeft);
            _bubble.AddToClassList("ob-bubble");
            _content.Add(_bubble);
            _error = El.Text("", "ob-error ob-creator-error");
            _error.style.display = DisplayStyle.None;
            _content.Add(_error);
            BuildCelebration();

            Restore(saved);
            RefreshLook(false);
            RefreshSelections();
            ShowStep(_flow.Step, 0, instant: true);
            _built = true;
            _ = Runner.Instance; // the pause event needs the runner
            Runner.ApplicationPaused += OnPaused;
            return _frame.Root;
        }

        // ------------------------------------------------------------------ the character

        private void BuildStage()
        {
            _preview = new AvatarPreview(720, 1080, transparent: true);
            var glow = El.Div("ob-stage-glow");
            glow.style.backgroundImage = new StyleBackground(OnboardingArt.Glow);
            var shadow = El.Div("ob-stage-shadow");
            _model = El.Div("ob-stage-model");
            _model.style.backgroundImage = Background.FromRenderTexture(_preview.Texture);
            _dragHint = El.Text(Strings.T("ob.look.drag"), "ob-drag-hint");
            _dragHint.pickingMode = PickingMode.Ignore;
            _stage = El.Div("ob-stage", glow, shadow, _model, _dragHint);
            _stage.pickingMode = PickingMode.Ignore;
            glow.pickingMode = PickingMode.Ignore;
            shadow.pickingMode = PickingMode.Ignore;

            _model.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_dragPointer >= 0 || e.button != 0 || SelectedLook == null || _busy) return;
                _dragPointer = e.pointerId;
                _model.CapturePointer(e.pointerId);
                FirstTouch();
                e.StopPropagation();
            });
            _model.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_dragPointer != e.pointerId) return;
                _preview.Spin(e.deltaPosition.x);
                e.StopPropagation();
            });
            void Release(int pointer)
            {
                if (_dragPointer != pointer) return;
                _dragPointer = -1;
                _model.ReleasePointer(pointer);
                Save();
            }
            _model.RegisterCallback<PointerUpEvent>(e => Release(e.pointerId));
            _model.RegisterCallback<PointerCancelEvent>(e => Release(e.pointerId));
            _model.RegisterCallback<PointerCaptureOutEvent>(_ => _dragPointer = -1);

            if (SelectedLook == null)
            {
                _dragHint.style.display = DisplayStyle.None;
                _stage.Add(El.PassThrough(El.Div("ob-stage-missing", El.Text(Strings.T("creator.lookMissing"), "ob-line"))));
            }
            _content.Add(_stage);
        }

        private void FirstTouch()
        {
            if (_touched) return;
            _touched = true;
            _sway?.Pause();
            _sway = null;
            Motion.Fade(_dragHint, 1f, 0f, 0.3f);
        }

        /// <summary>A slow ±8° sway on the Look step until the first drag, so the player sees it is 3D (spec C2).</summary>
        private void StartSway()
        {
            if (_touched || _sway != null || DeviceFeel.ReducedMotion || SelectedLook == null) return;
            _swayStart = Motion.Now;
            _swayBase = _preview.Yaw;
            _sway = _stage.schedule.Execute(() =>
            {
                if (_flow.Step != CreatorStep.Look) return;
                _preview.SetYaw(_swayBase + 8f * Mathf.Sin((Motion.Now - _swayStart) * 2f * Mathf.PI / 5f));
            }).Every(33);
        }

        /// <summary>The character's reaction: a line in the bubble and (throttled, so rapid taps do not stack) an emote.</summary>
        private void React(string line, params string[] emotes)
        {
            if (!string.IsNullOrEmpty(line)) _bubble.Say(line);
            if (emotes == null || emotes.Length == 0 || _preview == null) return;
            if (Motion.Now - _lastReaction < 1.1f) return;
            _lastReaction = Motion.Now;
            _preview.PlayReaction(emotes);
        }

        // ------------------------------------------------------------------ step 1: look

        private void BuildLookControls()
        {
            var prev = new LippedButton("", () => BrowseLook(-1), LippedButton.Tone.Green, LippedButton.Size.Square).WithIcon("back");
            prev.tooltip = Strings.T("creator.previousLook");
            prev.AddToClassList("ob-arrow");
            prev.AddToClassList("ob-arrow--prev");
            var next = new LippedButton("", () => BrowseLook(1), LippedButton.Tone.Green, LippedButton.Size.Square).WithIcon("next");
            next.tooltip = Strings.T("creator.nextLook");
            next.AddToClassList("ob-arrow");
            next.AddToClassList("ob-arrow--next");
            // A single authored look is not a carousel. Controls appear as distinct assets are supplied.
            prev.Show(_looks.Count > 1);
            next.Show(_looks.Count > 1);
            _lookName = El.Text("", "ob-look-name");
            _lookCount = El.Text("", "ob-look-count");
            _lookTag = El.Text(Strings.T("ob.look.previewTag"), "ob-tag");
            var label = El.Div("ob-look-label", _lookName, _lookCount, _lookTag);
            label.pickingMode = PickingMode.Ignore;
            _lookControls = El.Div("ob-look-controls", prev, next, label);
            _lookControls.pickingMode = PickingMode.Ignore;
            _content.Add(_lookControls);
        }

        private VisualElement BuildLookPage()
        {
            _bodyFocus = new LippedButton(Strings.T("ob.look.full"), () => FocusPreview(false), LippedButton.Tone.Purple, LippedButton.Size.Medium);
            _faceFocus = new LippedButton(Strings.T("ob.look.face"), () => FocusPreview(true), LippedButton.Tone.Purple, LippedButton.Size.Medium);
            _bodyFocus.AddToClassList("ob-seg");
            _faceFocus.AddToClassList("ob-seg");
            var toggle = El.Div("ob-seg-row", _bodyFocus, _faceFocus);
            toggle.SetEnabled(SelectedLook != null);
            return Page(Strings.T("ob.look.title"), Strings.T("ob.look.sub"), toggle);
        }

        private void BrowseLook(int direction)
        {
            if (_busy || _looks.Count < 2) return;
            _lookIndex = (_lookIndex + direction + _looks.Count) % _looks.Count;
            _reviewComplete = false;
            FirstTouch();
            RefreshLook(true);
            _lookChanges++;
            React(OnboardingCopy.Variant("ob.say.lookPick", _lookChanges, 3), "emote_thumbs_up", "emote_nod");
            Save();
            Validate();
        }

        private void RefreshLook(bool animate)
        {
            var look = SelectedLook;
            _lookName.text = look == null ? Strings.T("creator.look") : look.Display;
            _lookCount.text = _looks.Count > 1 ? Strings.F("ob.look.count", _lookIndex + 1, _looks.Count) : "";
            if (look != null)
            {
                Array.Copy(look.look, _draft.look, _draft.look.Length);
                float yaw = _touched ? _preview.Yaw : look.previewYaw;
                _preview.SetIdle(_portrait ? FaceIdle : FullIdle);
                _preview.SetLook(_draft.look);
                _preview.SetYaw(yaw);
                _preview.SetPortrait(_portrait);
                if (animate) Motion.Fade(_model, 0.35f, 1f, 0.18f);
            }
            RefreshAvailability();
            FocusPreview(_portrait, false);
        }

        private void FocusPreview(bool portrait, bool user = true)
        {
            _portrait = portrait;
            _bodyFocus.EnableInClassList("ob-seg--on", !portrait);
            _faceFocus.EnableInClassList("ob-seg--on", portrait);
            _stage.EnableInClassList("ob-stage--face", portrait && _flow.Step == CreatorStep.Look);
            ApplyFraming();
            if (user) { Cues.Select(); Save(); }
        }

        /// <summary>Face framing on the Look step (when chosen) and while typing the name; full body everywhere else.</summary>
        private void ApplyFraming()
        {
            if (_preview == null) return;
            bool face = _typing || (_portrait && _flow.Step == CreatorStep.Look);
            _preview.SetIdle(face ? FaceIdle : FullIdle);
            _preview.SetPortrait(face);
        }

        protected override void OnShown()
        {
            if (_config == null && Services.TryGet<ConfigStore>(out var config))
            {
                _config = config;
                _config.Changed += RefreshAvailability;
            }
            if (Origin == null && Services.TryGet<PlayerProfile>(out var profile) && profile.Me != null)
                Origin = OriginSummary.From(profile.CashCents, profile.DebtCents, profile.Me.HomeId, _config?.Config("lottery"));
            RefreshOrigin();
            RefreshAvailability();
        }

        private void RefreshAvailability()
        {
            _flow.HasLook = SelectedLook != null;
            _flow.LookSubmittable = CanSubmitLook;
            // Spec C2: a preview-only tag on development builds only; players see a problem only if submit cannot happen.
            if (_lookTag != null) _lookTag.style.display = SelectedLook != null && !CanSubmitLook && Debug.isDebugBuild ? DisplayStyle.Flex : DisplayStyle.None;
            Validate();
        }

        // ------------------------------------------------------------------ step 2: tribe

        private VisualElement BuildTribePage()
        {
            foreach (var t in _reg.TribeOrder)
            {
                var tribe = t;
                var badge = OnboardingArt.TribeBadge(t.id);
                var card = new ChoiceCard(t.id, t.name, PerkText(t), _ => PickTribe(tribe), ChoiceCard.Layout.Vertical,
                    badge, badge == null ? OnboardingCopy.TribeIcon(t.id) : null, "green");
                card.AddToClassList("ob-tribe-card");
                _tribeCards.Add(card);
            }
            var grid = ChoiceGrid.Build(_tribeCards.Cast<VisualElement>().ToList(), 4, 370f, 1.4f, 18f);
            return Page(Strings.T("ob.tribe.title"), Strings.T("ob.tribe.sub"), grid);
        }

        private void PickTribe(TribeDef t)
        {
            if (_busy || _draft.tribeId == t.id) return; // tapping the picked tribe again does nothing (no deselect, spec C2)
            _flow.SelectTribe(t.id);
            Cues.Select();
            RefreshSelections(t.id);
            React(OnboardingCopy.TribeLine(t.id), OnboardingCopy.TribeEmotes(t));
            Save();
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

        // ------------------------------------------------------------------ step 3: traits

        private VisualElement BuildTraitsPage()
        {
            foreach (var t in _reg.Traits)
            {
                var trait = t;
                var card = new ChoiceCard(t.id, t.name, OnboardingCopy.TraitEffect(t), _ => PickTrait(trait), ChoiceCard.Layout.Horizontal,
                    null, OnboardingCopy.TraitIcon(t.id), "purple");
                _traitCards.Add(card);
            }
            _traitDots = El.Div("ob-dots", El.Div("ob-dot"), El.Div("ob-dot"));
            _traitDots.pickingMode = PickingMode.Ignore;
            var grid = ChoiceGrid.Build(_traitCards.Cast<VisualElement>().ToList(), 3, 210f);
            var page = Page(Strings.T("ob.traits.title"), Strings.T("ob.traits.sub"), grid);
            page.Q(className: "ob-title-row").Insert(1, _traitDots);
            return page;
        }

        private void PickTrait(SimpleDef t)
        {
            if (_busy) return;
            bool was = _draft.traitIds.Contains(t.id);
            string removed = _flow.ToggleTrait(t.id);
            if (was) Cues.Deselect(); else Cues.Select();
            RefreshSelections(was ? null : t.id);
            if (removed != null)
            {
                _traitCards.FirstOrDefault(c => c.Id == removed)?.Bump();
                Cues.Deselect();
                React(Strings.T("ob.say.traitSwap"), "emote_shrug");
            }
            else if (!was && _draft.traitIds.Count == 2) { DeviceFeel.Confirm(); React(Strings.T("ob.say.traitTwo"), "emote_thumbs_up", "emote_nod"); }
            else if (!was) React(Strings.T("ob.say.traitOne"), "emote_nod");
            Save();
        }

        // ------------------------------------------------------------------ step 4: dream

        private VisualElement BuildDreamPage()
        {
            foreach (var d in _reg.Dreams)
            {
                var dream = d;
                var card = new ChoiceCard(d.id, d.name, null, _ => PickDream(dream), ChoiceCard.Layout.Horizontal,
                    null, OnboardingCopy.DreamIcon(d.id), "gold");
                card.AddToClassList("ob-dream-card");
                _dreamCards.Add(card);
            }
            var grid = ChoiceGrid.Build(_dreamCards.Cast<VisualElement>().ToList(), 3, 200f, 1.4f, 18f);
            return Page(Strings.T("ob.dream.title"), Strings.T("ob.dream.sub"), grid);
        }

        private void PickDream(SimpleDef d)
        {
            if (_busy || _draft.dreamId == d.id) return;
            _flow.SelectDream(d.id);
            Cues.Select();
            RefreshSelections(d.id);
            React(Strings.T("ob.say.dreamPick"), "emote_point", "idle_fidget_lookaround");
            Save();
        }

        // ------------------------------------------------------------------ step 5: name

        private VisualElement BuildNamePage()
        {
            _alias = new TextField { maxLength = CreatorFlow.MaxAlias };
            _alias.AddToClassList("ob-field");
            _alias.textEdition.placeholder = Strings.T("ob.name.placeholder");
            _alias.textEdition.hidePlaceholderOnFocus = true;
            // Device QA 2026-10-08: the landscape keyboard covers about two thirds of the screen and the OS input line
            // was not visible, so typing was blind. The field now moves into the visible strip above the keyboard while it
            // has focus (typing layout), so the OS line is hidden and the player types into our own field.
            _alias.textEdition.hideMobileInput = true;
            _alias.RegisterValueChangedCallback(e =>
            {
                _flow.SetAlias(e.newValue);
                _reviewComplete = false;
                RefreshAliasStatus(true);
                Save();
                Validate();
            });
            _alias.RegisterCallback<FocusInEvent>(_ => { if (TouchScreenKeyboard.isSupported) SetTyping(true); });
            _alias.RegisterCallback<FocusOutEvent>(_ => _alias.schedule.Execute(() => { if (!IsAliasFocused()) SetTyping(false); }).StartingIn(120));
            _aliasStatus = El.Text("", "ob-alias-status");
            _done = new LippedButton(Strings.T("ob.name.done"), EndTyping, LippedButton.Tone.Green, LippedButton.Size.Medium);
            _done.AddToClassList("ob-done");
            var fieldRow = El.Div("ob-field-row", _alias, _done);
            return Page(Strings.T("ob.name.title"), Strings.T("ob.name.sub"), fieldRow, _aliasStatus);
        }

        /// <summary>
        /// DONE (and anything else that ends typing): close the soft keyboard, then drop focus. Blur alone left the Android
        /// keyboard up on the Seeker (2026-10-08): the layout went back to normal under a keyboard covering 65% of the
        /// screen, hiding the field and NEXT. See <see cref="SoftKeyboard"/>.
        /// </summary>
        private void EndTyping()
        {
            SoftKeyboard.Hide(_alias);
            _alias?.Blur();
            SetTyping(false);
        }

        private bool IsAliasFocused()
        {
            var f = _alias?.focusController?.focusedElement as VisualElement;
            return f != null && (f == _alias || _alias.Contains(f));
        }

        private void RefreshAliasStatus(bool react)
        {
            var state = CreatorFlow.CheckAlias(_draft.alias);
            _aliasStatus.text = Strings.T(state == AliasState.Ok ? "ob.name.ok" : state == AliasState.TooShort ? "ob.name.short" : "ob.name.rule");
            _aliasStatus.EnableInClassList("ob-alias-status--ok", state == AliasState.Ok);
            _aliasStatus.EnableInClassList("ob-alias-status--warn", state == AliasState.TooShort || state == AliasState.TooLong);
            _alias.EnableInClassList("ob-field--ok", state == AliasState.Ok);
            if (!react) return;
            // A reaction once the name settles (600 ms after the last keystroke), not on every key.
            _nameLine?.Pause();
            if (state == AliasState.Ok)
                _nameLine = _alias.schedule.Execute(() => React(Strings.F("ob.say.nameOk", _draft.alias), "emote_nod")).StartingIn(600);
        }

        /// <summary>
        /// The typing layout: while the soft keyboard is up, the field, its status and the character's face move into the
        /// top third of the screen (the keyboard covers the rest in landscape; Unity reports no keyboard area on Android, so
        /// the layout assumes that height). It ends when the field loses focus, the keyboard goes away, or DONE is tapped.
        /// </summary>
        private void SetTyping(bool on)
        {
            if (_typing == on) return;
            _typing = on;
            _frame.Root.EnableInClassList("ob-typing", on);
            _stage.EnableInClassList("ob-stage--typing", on);
            if (on) _bubble.Hide();
            ApplyFraming();
            _keyboardPoll?.Pause();
            _keyboardMissing = 0;
            if (on && TouchScreenKeyboard.isSupported)
            {
                _focusAt = Motion.Now;
                _keyboardPoll = _alias.schedule.Execute(() =>
                {
                    if (Motion.Now - _focusAt < 0.8f) return;
                    _keyboardMissing = TouchScreenKeyboard.visible ? 0 : _keyboardMissing + 1;
                    if (_keyboardMissing >= 2) { _alias.Blur(); SetTyping(false); }
                }).Every(250);
            }
        }

        /// <summary>Editor captures: show the typing layout without a keyboard.</summary>
        public void ForceTypingLayout(bool on) => SetTyping(on);

        // ------------------------------------------------------------------ step 6: confirm

        private VisualElement BuildConfirmPage()
        {
            _originHost = El.Div("ob-origin-host");
            _originHost.pickingMode = PickingMode.Ignore;
            AddSummary(CreatorStep.Look, "ob.sum.look", null, "body");
            AddSummary(CreatorStep.Tribe, "ob.sum.tribe", null, "contacts");
            AddSummary(CreatorStep.Traits, "ob.sum.traits", null, "emote");
            AddSummary(CreatorStep.Dream, "ob.sum.dream", null, "dream");
            AddSummary(CreatorStep.Name, "ob.sum.name", null, "profile");
            _ageCard = new ChoiceCard("age", Strings.T("ob.confirm.age"), Strings.T("ob.confirm.ageSub"), _ => ToggleAge(),
                ChoiceCard.Layout.Horizontal, null, "checkbox");
            _ageCard.AddToClassList("ob-age-card");
            _summaryGrid = El.Div("ob-summary kit-grid");
            _summaryGrid.pickingMode = PickingMode.Ignore;
            var note = El.Text(Strings.T("ob.confirm.note"), "ob-note ob-note--inline");
            var page = Page(Strings.T("ob.confirm.title"), Strings.T("ob.confirm.sub"), _summaryGrid, _ageCard, note);
            _confirmTitle = page.Q<Label>(className: "ob-title");
            return page;
        }

        private void AddSummary(CreatorStep step, string captionKey, Texture2D badge, string icon)
        {
            var card = new ChoiceCard(step.ToString(), "", null, _ => EditFromSummary(step), ChoiceCard.Layout.Horizontal,
                badge, icon, "purple", Strings.T(captionKey));
            card.AddToClassList("ob-summary-card");
            card.userData = step;
            _summary.Add(card);
        }

        private void EditFromSummary(CreatorStep step)
        {
            if (_busy || !_flow.JumpTo(step)) return;
            _returnToConfirm = true;
            ShowStep(step, -1);
        }

        private void ToggleAge()
        {
            if (_busy) return;
            _flow.SetAge(!_draft.ageConfirmed);
            _ageCard.SetSelected(_draft.ageConfirmed, true);
            if (_draft.ageConfirmed) Cues.Select(); else Cues.Deselect();
            _reviewComplete = false;
            Validate();
        }

        /// <summary>Summary values (after any change) and the origin chip, built from the current draft.</summary>
        private void RefreshSummary()
        {
            if (_summaryGrid == null) return;
            foreach (var c in _summary)
            {
                var step = (CreatorStep)c.userData;
                c.Title = step switch
                {
                    CreatorStep.Look => SelectedLook?.Display ?? Strings.T("creator.look"),
                    CreatorStep.Tribe => _reg.TribeOrder.FirstOrDefault(t => t.id == _draft.tribeId)?.name ?? "—",
                    CreatorStep.Traits => string.Join(" · ", _reg.Traits.Where(t => _draft.traitIds.Contains(t.id)).Select(t => t.name)),
                    CreatorStep.Dream => _reg.Dreams.FirstOrDefault(d => d.id == _draft.dreamId)?.name ?? "—",
                    _ => string.IsNullOrEmpty(_draft.alias) ? "—" : _draft.alias,
                };
            }
            if (_confirmTitle != null) _confirmTitle.text = Strings.F("ob.confirm.title", string.IsNullOrEmpty(_draft.alias) ? "you" : _draft.alias);
            // Tribe chip: show the picked tribe's own badge.
            var tribeCard = _summary.FirstOrDefault(c => (CreatorStep)c.userData == CreatorStep.Tribe);
            if (tribeCard != null)
            {
                var badge = OnboardingArt.TribeBadge(_draft.tribeId);
                var art = tribeCard.Face.Q(className: "kit-card__badge");
                if (art == null)
                {
                    art = El.Div("kit-card__badge");
                    art.pickingMode = PickingMode.Ignore;
                    tribeCard.Face.Insert(0, art);
                }
                if (badge != null) art.style.backgroundImage = new StyleBackground(badge);
                art.style.display = badge != null ? DisplayStyle.Flex : DisplayStyle.None;
                var disc = tribeCard.Face.Q(className: "kit-card__disc");
                if (disc != null) disc.style.display = badge != null ? DisplayStyle.None : DisplayStyle.Flex;
            }
            RebuildSummaryGrid();
        }

        private void RebuildSummaryGrid()
        {
            _summaryGrid.Clear();
            var items = new List<VisualElement>();
            if (_originHost.childCount > 0) items.Add(_originHost);
            items.AddRange(_summary);
            var grid = ChoiceGrid.Build(items, 3, 182f, 1.4f, 18f);
            while (grid.childCount > 0) _summaryGrid.Add(grid[0]);
        }

        private void RefreshOrigin()
        {
            if (_originHost == null) return;
            _originHost.Clear();
            if (Origin == null) { RefreshSummary(); return; }
            var name = Strings.T(LotteryModel.NameKey(Origin.outcomeId));
            string sub = Strings.F("ob.lottery.cash", LotteryModel.CashWhole(Origin.cashCents));
            if (Origin.debtCents > 0) sub += " · " + Strings.F("ob.lottery.debt", LotteryModel.CashWhole(Origin.debtCents));
            var card = new ChoiceCard("origin", name, sub, null, ChoiceCard.Layout.Horizontal,
                OnboardingArt.CardFront(Origin.outcomeId), "cash", "gold", Strings.T("ob.sum.origin"));
            card.AddToClassList("ob-summary-card");
            card.AddToClassList("ob-origin-card");
            card.pickingMode = PickingMode.Ignore;
            card.focusable = false;
            _originHost.Add(card);
            RefreshSummary();
        }

        // ------------------------------------------------------------------ steps

        private static VisualElement Page(string title, string sub, params VisualElement[] body)
        {
            // The subtitle sits on the title's row (beside it; under it on the narrower Look column) to keep the grid's height.
            var titleRow = El.Div("ob-title-row", El.Text(title, "ob-title"), El.Text(sub, "ob-sub"));
            titleRow.pickingMode = PickingMode.Ignore;
            var page = El.Div("", titleRow);
            foreach (var b in body) page.Add(b);
            return page;
        }

        private void ShowStep(CreatorStep step, int direction, bool instant = false)
        {
            if (_busy) return;
            if (_typing) EndTyping();
            for (int i = 0; i < _pages.Length; i++) _pages[i].style.display = i == (int)step ? DisplayStyle.Flex : DisplayStyle.None;
            var page = _pages[(int)step];
            if (!instant) Motion.Enter(page, direction >= 0 ? 72f : -72f, 0f);
            else { page.style.opacity = 1f; page.style.translate = new Translate(0f, 0f); }

            _frame.Root.EnableInClassList("ob-mode-look", step == CreatorStep.Look);
            _frame.Root.EnableInClassList("ob-mode-side", step != CreatorStep.Look);
            _frame.Progress.Set(CreatorFlow.ProgressFor(step), !instant);
            _frame.Back.style.visibility = step == CreatorStep.Look ? Visibility.Hidden : Visibility.Visible;
            _lookControls.style.display = step == CreatorStep.Look ? DisplayStyle.Flex : DisplayStyle.None;
            _dragHint.style.display = step == CreatorStep.Look && !_touched && SelectedLook != null ? DisplayStyle.Flex : DisplayStyle.None;
            _stage.EnableInClassList("ob-stage--face", _portrait && step == CreatorStep.Look);
            ApplyFraming();
            if (step == CreatorStep.Look) StartSway();
            if (step == CreatorStep.Confirm) { _returnToConfirm = false; RefreshSummary(); }
            if (step == CreatorStep.Name)
            {
                _alias.SetValueWithoutNotify(_draft.alias);
                // SetValueWithoutNotify does not refresh the placeholder state: a name restored from the draft rendered in
                // the faded placeholder colour on the Seeker (2026-10-08). Match the class to the value.
                _alias.Q(className: TextField.inputUssClassName)?
                    .EnableInClassList("unity-base-text-field__input--placeholder", string.IsNullOrEmpty(_draft.alias));
                RefreshAliasStatus(false);
            }
            _ageCard.SetSelected(_draft.ageConfirmed);
            Validate();
            SayArrival(step);
            Save();
        }

        private void SayArrival(CreatorStep step)
        {
            switch (step)
            {
                case CreatorStep.Look: React(Strings.T("ob.say.look"), "emote_wave"); break;
                case CreatorStep.Tribe:
                    if (_draft.tribeId != null) React(OnboardingCopy.TribeLine(_draft.tribeId));
                    else React(Strings.T("ob.say.tribe"), "emote_shrug");
                    break;
                case CreatorStep.Traits: React(Strings.T(_draft.traitIds.Count == 2 ? "ob.say.traitTwo" : "ob.say.traits"), "emote_nod"); break;
                case CreatorStep.Dream: React(Strings.T(_draft.dreamId != null ? "ob.say.dreamPick" : "ob.say.dream"), "idle_fidget_lookaround"); break;
                case CreatorStep.Name: React(Strings.T("ob.say.name"), "emote_point"); break;
                default: React(Strings.T("ob.say.confirm"), "emote_chest_thump", "emote_thumbs_up"); break;
            }
        }

        private void GoBack()
        {
            if (_busy || !_flow.Back()) return;
            _returnToConfirm = false;
            ShowStep(_flow.Step, -1);
        }

        private void Advance()
        {
            if (_busy) return;
            if (_flow.Step == CreatorStep.Confirm)
            {
                if (_flow.CurrentProblem != null) { _frame.NudgeHint(); return; }
                if (_readOnlyReview)
                {
                    // A local visual review must never call character creation with proposed item IDs.
                    _reviewComplete = true;
                    Validate();
                }
                else if (CanSubmitLook)
                {
                    Cues.Step();
                    ShowError("");
                    _onSubmit?.Invoke(_draft);
                }
                return;
            }
            if (!_flow.CanAdvance) { _frame.NudgeHint(); return; }
            Cues.Step();
            if (_returnToConfirm && _flow.FirstUnanswered() == CreatorStep.Confirm && _flow.JumpTo(CreatorStep.Confirm))
            {
                ShowStep(CreatorStep.Confirm, 1);
                return;
            }
            _returnToConfirm = false;
            _flow.Next();
            ShowStep(_flow.Step, 1);
        }

        private void RefreshSelections(string popId = null)
        {
            foreach (var c in _tribeCards) c.SetSelected(c.Id == _draft.tribeId, c.Id == popId);
            foreach (var c in _traitCards) c.SetSelected(_draft.traitIds.Contains(c.Id), c.Id == popId);
            foreach (var c in _dreamCards) c.SetSelected(c.Id == _draft.dreamId, c.Id == popId);
            if (_traitDots != null)
            {
                int i = 0;
                foreach (var dot in _traitDots.Children()) dot.EnableInClassList("ob-dot--on", i++ < _draft.traitIds.Count);
            }
            _reviewComplete = false;
            Validate();
        }

        private void Validate()
        {
            if (!_built && _frame == null) return;
            _flow.HasLook = SelectedLook != null;
            _flow.LookSubmittable = CanSubmitLook;
            var problem = _flow.CurrentProblem;
            bool confirm = _flow.Step == CreatorStep.Confirm;
            string label = confirm
                ? Strings.T(_readOnlyReview ? (_reviewComplete ? "ob.preview.done" : "ob.preview.finish") : "ob.confirm.cta")
                : Strings.T(_returnToConfirm ? "ob.done" : "ob.next");
            if (!_busy) _frame.Cta.Text = label;
            _frame.Cta.SetBusy(_busy, Strings.T("ob.saving"));
            if (!_busy) _frame.Cta.SetEnabled(problem == null && !_reviewComplete);
            string hint = problem != null ? Strings.T(problem)
                : confirm && _readOnlyReview ? Strings.T(_reviewComplete ? "creator.previewSavedNothing" : "creator.previewNotice") : "";
            _frame.SetHint(hint);
            _frame.Back.SetEnabled(!_busy);
        }

        // ------------------------------------------------------------------ persistence

        private void Restore(CreatorDraftStore.Saved saved)
        {
            if (saved == null) return;
            CreatorDraftStore.Apply(saved, _flow);
            _flow.Sanitize(_reg.TribeOrder.Select(t => t.id).ToList(), _reg.Traits.Select(t => t.id).ToList(), _reg.Dreams.Select(d => d.id).ToList());
            if (!string.IsNullOrEmpty(saved.lookKey))
            {
                int index = _looks.FindIndex(l => string.Join("|", l.look) == saved.lookKey);
                if (index >= 0) _lookIndex = index;
            }
            _portrait = saved.portrait;
            _touched = true; // a returning player has already seen the sway; keep their angle (RefreshLook builds the model)
            if (SelectedLook != null) Array.Copy(SelectedLook.look, _draft.look, _draft.look.Length);
            _preview.SetYaw(saved.yaw);
            _flow.HasLook = SelectedLook != null;
            _flow.Restore(saved.step, saved.furthest);
            Log.Info("creator", "Restored the unfinished character at step " + _flow.Step);
        }

        private void Save()
        {
            if (!_built || _store == null || _celebrated != null) return;
            _store.Save(_flow, LookKey, _preview?.Yaw ?? 180f, _portrait);
        }

        private void OnPaused(bool paused)
        {
            if (!paused || _celebrated != null) return;
            Save();
            CreatorDraftStore.Flush();
        }

        // ------------------------------------------------------------------ celebration

        private void BuildCelebration()
        {
            _celebrateTitle = El.Text("", "ob-celebrate-title");
            var sub = El.Text(Strings.T("ob.celebrate.sub"), "ob-celebrate-sub");
            _enterCity = new LippedButton(Strings.T("ob.celebrate.cta"), () => _celebrated?.TrySetResult(true), LippedButton.Tone.Green, LippedButton.Size.Large);
            _enterCity.AddToClassList("ob-enter-city");
            _celebrate = El.Div("ob-celebrate", _celebrateTitle, sub, _enterCity);
            _celebrate.pickingMode = PickingMode.Ignore;
            _celebrate.style.display = DisplayStyle.None;
            _frame.Overlay.Add(_celebrate);
        }

        /// <summary>
        /// After `POST /v1/characters` succeeds (FlowController awaits this before closing the screen): the bar fills with a
        /// flash, the character jumps, confetti (and coins for a good origin), "Welcome to CT, name!", then ENTER THE CITY
        /// completes the task. Clears the saved draft.
        /// </summary>
        public Task Celebrate()
        {
            _store?.Clear();
            _celebrated = new TaskCompletionSource<bool>();
            _busy = true;
            if (_typing) SetTyping(false);
            _frame.Root.AddToClassList("ob-mode-celebrate");
            _stage.AddToClassList("ob-stage--celebrate");
            _frame.ShowCta(false, false);
            _frame.Back.style.visibility = Visibility.Hidden;
            _error.style.display = DisplayStyle.None;
            _frame.Progress.Set(CreatorFlow.ProgressSegments);
            _frame.Progress.Flash();
            _celebrateTitle.text = Strings.F("ob.celebrate.title", _draft.alias);
            _celebrate.style.display = DisplayStyle.Flex;
            _bubble.SetTail(SpeechBubble.Tail.Left);
            Tween.Run(_dim, 0.4f, t => _dim.style.backgroundColor = new Color(0.12f, 0.04f, 0.16f, 0.45f * t), Ease.OutCubic, skipWhenReduced: false);
            _enterCity.style.display = DisplayStyle.None;
            _portrait = false;
            ApplyFraming();
            _preview.SetYaw(SelectedLook?.previewYaw ?? 180f);
            _lastReaction = -10f;
            React(Strings.T("ob.say.celebrate"), "emote_moon_jump", "react_moon_jump", "emote_wagmi");
            Motion.Enter(_celebrateTitle, 0f, -60f, 0.42f);
            Cues.Play(Cues.SfxCelebrate);
            DeviceFeel.Confirm();
            for (int i = 1; i <= 3; i++) _celebrate.schedule.Execute(DeviceFeel.Tick).StartingIn(260 + i * 110);
            _frame.Burst.Confetti(120);
            bool good = Origin != null && Origin.outcomeId != LotteryModel.Exit;
            if (good) _frame.Burst.Coins(new Vector2(_frame.Root.resolvedStyle.width / 2f, _frame.Root.resolvedStyle.height * 0.62f), 24);
            _celebrate.schedule.Execute(() =>
            {
                _enterCity.style.display = DisplayStyle.Flex;
                Motion.Enter(_enterCity, 0f, 60f, 0.4f);
                _enterCity.EnableShine();
            }).StartingIn(DeviceFeel.ReducedMotion ? 0 : 1000);
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
            if (LaunchArgs.Flag("autocreate")) _celebrate.schedule.Execute(() => _celebrated?.TrySetResult(true)).StartingIn(2500);
#endif
            return _celebrated.Task;
        }

        /// <summary>Captures and automation: ENTER THE CITY.</summary>
        public void PressEnterCity() => _enterCity?.Press();

        /// <summary>Captures and automation: the CTA, as a tap.</summary>
        public void PressCta() => _frame?.Cta.Press();

        /// <summary>Captures and automation: picks in the current step's grid by index.</summary>
        public void PickForAutomation(int index)
        {
            var list = _flow.Step == CreatorStep.Tribe ? _tribeCards : _flow.Step == CreatorStep.Traits ? _traitCards : _flow.Step == CreatorStep.Dream ? _dreamCards : null;
            if (list != null && index >= 0 && index < list.Count) list[index].Press();
            if (_flow.Step == CreatorStep.Confirm && index == 0) _ageCard.Press();
            if (_flow.Step == CreatorStep.Look) BrowseLook(index >= 0 ? 1 : -1);
        }

        /// <summary>Captures: types into the alias field (fires the same change path as the keyboard).</summary>
        public void TypeAlias(string value) => _alias.value = value;

        /// <summary>Captures: the Face/Full toggle.</summary>
        public void SetFaceForAutomation(bool face) => FocusPreview(face);

        /// <summary>Captures and checks: jump to a step already reached.</summary>
        public void JumpForAutomation(CreatorStep step) { if (_flow.JumpTo(step)) ShowStep(step, 1); }

        public ChoiceCard AgeCard => _ageCard;

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
        /// <summary>
        /// Development automation only (launch arg `autocreate=1`): picks the first tribe, two traits and dream,
        /// sets the alias and age confirmation, and submits through the normal path. Returns false if not allowed.
        /// </summary>
        public bool AutoSubmitForAutomation(string alias)
        {
            if (SelectedLook == null || _busy) return false;
            // `look=<cast look id>` (e.g. player_f_kol) picks that creator look for device checks.
            var want = LaunchArgs.Get("look");
            if (!string.IsNullOrEmpty(want))
            {
                int index = -1;
                for (int i = 0; i < _looks.Count; i++)
                    if (_looks[i].look != null && Array.IndexOf(_looks[i].look, "head_" + want) >= 0) { index = i; break; }
                if (index >= 0 && index != _lookIndex) { _lookIndex = index; RefreshLook(false); }
                if (index < 0) Log.Warn("creator", "look " + want + " is not in the creator");
            }
            _draft.tribeId = _reg.TribeOrder.FirstOrDefault()?.id;
            _draft.traitIds.Clear();
            _draft.traitIds.AddRange(_reg.Traits.Take(2).Select(t => t.id));
            _draft.dreamId = _reg.Dreams.FirstOrDefault()?.id;
            _draft.alias = alias;
            _draft.ageConfirmed = true;
            RefreshSelections();
            while (_flow.Next()) { }
            ShowStep(_flow.Step, 1, instant: true);
            if (_flow.Problem(CreatorStep.Confirm) is string problem && !(problem == "creator.lookNotAvailable"))
            { Log.Warn("creator", "Automation draft rejected: " + problem); return false; }
            if (!CanSubmitLook) { Log.Warn("creator", "Automation draft rejected: look not submittable"); return false; }
            _onSubmit?.Invoke(_draft);
            return true;
        }
#endif

        public void ShowError(string text)
        {
            if (_error == null) return;
            _error.text = text ?? "";
            bool on = !string.IsNullOrEmpty(text);
            _error.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            Cues.Nope();
            _lastReaction = -10f;
            React(Strings.T("ob.say.error"), "emote_facepalm", "emote_shrug");
            Motion.Shake(_error);
        }

        public void SetBusy(bool busy) { _busy = busy; Validate(); }

        public override void Close()
        {
            Runner.ApplicationPaused -= OnPaused;
            if (_config != null) _config.Changed -= RefreshAvailability;
            _config = null;
            _sway?.Pause();
            _keyboardPoll?.Pause();
            _celebrated?.TrySetResult(true);
            _frame?.Burst.Stop();
            base.Close();
            _preview?.Dispose();
            _preview = null;
        }
    }
}
