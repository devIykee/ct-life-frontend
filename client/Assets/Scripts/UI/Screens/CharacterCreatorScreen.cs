using CTLife.World;
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
        private List<BaseModel> _models;
        private CustomizerCatalog _customizer;
        private Func<AvatarSpec, bool> _resolves;
        private readonly Dictionary<string, CreatorLocalArtBinding> _localArt = new Dictionary<string, CreatorLocalArtBinding>(StringComparer.Ordinal);
        private string _presentedSpecKey;
        private bool PreviewReady => _spec != null && _presentedSpecKey == _spec.Key();
        /// <summary>What the player built (base + parts + tints + accessories); _draft.look is its contract form.</summary>
        private AvatarSpec _spec;
        private readonly Func<string[], bool> _serverLookConfirmed;
        private readonly bool _readOnlyReview;
        private readonly VisualElement[] _pages = new VisualElement[CreatorFlow.StepCount];
        private readonly List<ChoiceCard> _tribeCards = new List<ChoiceCard>(), _traitCards = new List<ChoiceCard>(), _dreamCards = new List<ChoiceCard>();
        private readonly List<ChoiceCard> _modelCards = new List<ChoiceCard>(), _optionCards = new List<ChoiceCard>();
        private readonly List<Button> _railButtons = new List<Button>();
        private CreatorDraftStore _store;
        private OnboardingFrame _frame;
        private AvatarPreview _preview;
        private ConfigStore _config;
        private SpeechBubble _bubble;
        private VisualElement _content, _stage, _model, _lookControls, _dim, _celebrate, _traitDots, _modelGrid, _rail, _optionGrid;
        private ScrollView _railScroll;
        private VisualElement _idCard, _cardPortrait, _cardTribeBadge, _cardOriginArt, _cardTraits, _cardDreamIcon;
        private Label _lookName, _lookCount, _lookTag, _dragHint, _error, _aliasStatus, _confirmTitle, _celebrateTitle;
        private Label _cardName, _cardTribe, _cardOrigin, _cardDream, _optionsEmpty;
        private TextField _alias;
        private CheckRow _ageRow;
        private LippedButton _bodyFocus, _faceFocus, _done, _enterCity;
        private LookCategory _category = LookCategory.SkinTone;
        private IVisualElementScheduledItem _sway, _keyboardPoll, _nameLine;
        private TaskCompletionSource<bool> _celebrated;
        private int _lookIndex, _dragPointer = -1, _lookChanges, _keyboardMissing;
        private bool _busy, _portrait, _reviewComplete, _touched, _typing, _returnToConfirm, _built;
        private float _swayStart, _swayBase, _lastReaction = -10f, _focusAt;

        /// <summary>The current base model (Character step). Its look may have been customized since: see _draft.look.</summary>
        private BaseModel SelectedLook => _models.Count > 0 ? _models[_lookIndex] : null;
        private bool CanSubmitLook => SelectedLook != null && PreviewReady && !(_spec?.IsModular ?? false) &&
            !_localArt.ContainsKey(SelectedLook.id) && _serverLookConfirmed != null && _serverLookConfirmed(_draft.look);
        private string LookKey => SelectedLook?.id;

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
            _customizer = CustomizerCatalog.Load();
            _resolves = ResolvesSpec;
            BuildModels();
        }

        /// <summary>
        /// Base models in creator order (interleaved by skin-tone band and body family, seeded per install: CreatorLookOrder),
        /// each with the customizer options its art supports (CustomizerCatalog, optional) and its baked portrait.
        /// </summary>
        private void BuildModels()
        {
            var ordered = CreatorLookOrder.ForCreator(CreatorLookCatalog.Available(), CreatorLookOrder.InstallSeed());
            _models = CustomizerModel.Build(ordered, _customizer, CustomizerModel.Portrait);
            foreach (var binding in _localArt.Values)
            {
                if (_models.Any(m => m.id == binding.Model.id)) throw new InvalidOperationException("Local art collides with a shipped creator model.");
                _models.Add(binding.Model);
            }
            Log.Info("creator", _models.Count + " base models available");
            _lookIndex = 0;
            SetSpec(SelectedLook == null ? null : DefaultSpec(SelectedLook));
        }

        /// <summary>Sets the spec and its contract look (the server payload: seven Look ids).</summary>
        private void SetSpec(AvatarSpec spec)
        {
            _spec = spec;
            // Unregistered local selections are stored in the full local spec only, never as invented wire IDs.
            if (spec == null || spec.IsModular || _localArt.ContainsKey(spec.baseId)) { Array.Clear(_draft.look, 0, _draft.look.Length); return; }
            var look = CustomizerModel.ToContract(spec);
            Array.Copy(look, _draft.look, _draft.look.Length);
        }

        /// <summary>
        /// Captures and tests (before Show): a customizer catalog and resolver other than the shipped ones, e.g. a demo
        /// catalog for a model with options. Production uses Resources/UI/CustomizerCatalog and exact art resolution.
        /// </summary>
        public void ConfigureCustomizer(CustomizerCatalog catalog, Func<AvatarSpec, bool> resolves)
        {
            _customizer = catalog;
            _resolves = resolves ?? ResolvesSpec;
            BuildModels();
        }

        /// <summary>Explicit pre-Show local art. No registry/resource mutation, backend aliases or submission capability.</summary>
        public void ConfigureLocalArt(IEnumerable<CreatorLocalArtBinding> bindings)
        {
            if (_preview != null || _built) throw new InvalidOperationException("Configure local art before showing the creator.");
            var next = (bindings ?? throw new ArgumentNullException(nameof(bindings))).ToArray();
            if (next.Any(b => b == null) || next.Select(b => b.Model.id).Distinct(StringComparer.Ordinal).Count() != next.Length)
                throw new ArgumentException("Local art bindings must have unique complete bases.");
            foreach (var b in next)
                if (!b.Resolves(b.DefaultSpec()) || _models.Any(m => !_localArt.ContainsKey(m.id) && m.id == b.Model.id))
                    throw new ArgumentException("Local art no longer resolves or collides with a shipped model.");
            _localArt.Clear(); foreach (var b in next) _localArt.Add(b.Model.id, b);
            _resolves = ResolvesSpec; BuildModels();
        }

        private CustomizerCatalog OptionsFor(BaseModel model) => model != null && _localArt.TryGetValue(model.id, out var b) ? b.Options : _customizer;
        private AvatarSpec DefaultSpec(BaseModel model) => model != null && _localArt.TryGetValue(model.id, out var b) ?
            b.DefaultSpec() : CustomizerModel.DefaultSpec(model, _customizer);
        private bool ResolvesSpec(AvatarSpec spec) => spec != null && (_localArt.TryGetValue(spec.baseId, out var b) ?
            b.Resolves(spec) : CustomizerModel.Resolves(spec, _models?.FirstOrDefault(m => m.id == spec.baseId)));

        private bool TryPresent(BaseModel model, AvatarSpec spec)
        {
            var failure = new ModularAvatarFailure(ModularAvatarError.InvalidRequest, "No exact preview selection.");
            bool shown = false;
            if (model == null || spec == null || spec.baseId != model.id) return false;
            if (_localArt.TryGetValue(model.id, out var binding))
                shown = spec.legacyLook == null && ExactCustomizerSelection.TryApply(_preview, binding.Art, spec, out _, out failure);
            else shown = !spec.IsModular && _preview.TrySetExactLook(CustomizerModel.ToContract(spec), out failure);
            if (!shown)
            {
                Log.Warn("creator", "Preview selection rejected for " + model.id + ": " + failure);
                ShowError(Strings.T("common.unavailable")); return false;
            }
            _presentedSpecKey = spec.Key(); return true;
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
            _pages[(int)CreatorStep.Customize] = BuildCustomizePage();
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
            _lookName = El.Text("", "ob-look-name");
            _lookCount = El.Text("", "ob-look-count");
            _lookTag = El.Text(Strings.T("ob.look.previewTag"), "ob-tag");
            var label = El.Div("ob-look-label", _lookName, _lookCount, _lookTag);
            label.pickingMode = PickingMode.Ignore;
            // Zoom: full body / face, as two round buttons on the preview.
            _bodyFocus = new LippedButton("", () => FocusPreview(false), LippedButton.Tone.White, LippedButton.Size.Round).WithIcon("body");
            _bodyFocus.tooltip = Strings.T("ob.look.full");
            _faceFocus = new LippedButton("", () => FocusPreview(true), LippedButton.Tone.White, LippedButton.Size.Round).WithIcon("face");
            _faceFocus.tooltip = Strings.T("ob.look.face");
            _bodyFocus.AddToClassList("ob-zoom");
            _faceFocus.AddToClassList("ob-zoom");
            var zoom = El.Div("ob-zoom-col", _bodyFocus, _faceFocus);
            zoom.pickingMode = PickingMode.Ignore;
            zoom.SetEnabled(SelectedLook != null);
            _lookControls = El.Div("ob-look-controls", label, zoom);
            _lookControls.pickingMode = PickingMode.Ignore;
            _content.Add(_lookControls);
        }

        /// <summary>
        /// Step 1, Character: the base models as a portrait grid (baked portraits; a named disc while a portrait is missing).
        /// Tapping one puts it on the big preview. The grid scrolls when there are more models than fit.
        /// </summary>
        private VisualElement BuildLookPage()
        {
            for (int i = 0; i < _models.Count; i++)
            {
                int index = i;
                var m = _models[i];
                var card = new ChoiceCard(m.id, m.label, null, _ => PickModel(index), ChoiceCard.Layout.Vertical,
                    m.portrait, m.portrait == null ? (m.family == 'f' ? "profile" : "body") : null, m.family == 'f' ? "purple" : "green");
                card.AddToClassList("ob-model-card");
                _modelCards.Add(card);
            }
            var scroll = El.VerticalScroll("ob-model-scroll");
            // Portrait tiles, not cards (user review 2026-10-10): six across, two rows visible, and the scroll ends above
            // the CTA so the roster never touches NEXT.
            _modelGrid = ChoiceGrid.Build(_modelCards.Cast<VisualElement>().ToList(), 6, 236f, 2f, 16f);
            scroll.Add(_modelGrid);
            return Page(Strings.T("ob.look.title"), Strings.T("ob.look.sub"), scroll);
        }

        private void PickModel(int index)
        {
            if (_busy || index < 0 || index >= _models.Count || index == _lookIndex) return;
            var model = _models[index]; var next = DefaultSpec(model);
            if (!TryPresent(model, next)) return;
            _lookIndex = index; SetSpec(next);
            _reviewComplete = false;
            Cues.Select(); FirstTouch();
            RefreshLook(true); RefreshModelCards(SelectedLook.id);
            _lookChanges++;
            React(OnboardingCopy.Variant("ob.say.lookPick", _lookChanges, 3), "emote_thumbs_up", "emote_nod");
            Save(); Validate();
        }

        private void RefreshModelCards(string popId = null)
        {
            var current = SelectedLook?.id;
            foreach (var c in _modelCards) c.SetSelected(c.Id == current, c.Id == popId);
        }

        // ------------------------------------------------------------------ step 2: customize

        /// <summary>
        /// Step 2, Customize: a category rail (only the categories this model has art for) and that category's options
        /// (swatches, thumbnails or names). Every option patches the seven contract Look ids. Skipped when a model has none.
        /// </summary>
        private VisualElement BuildCustomizePage()
        {
            _rail = El.Div("ob-rail");
            _rail.pickingMode = PickingMode.Ignore;
            _railScroll = El.VerticalScroll("ob-rail-scroll");
            _railScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _railScroll.Add(_rail);
            _optionGrid = El.Div("ob-options");
            _optionGrid.pickingMode = PickingMode.Ignore;
            _optionsEmpty = El.Text(Strings.T("ob.custom.empty"), "ob-line");
            var scroll = El.VerticalScroll("ob-option-scroll");
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.Add(_optionGrid);
            var body = El.Div("ob-custom-body", _railScroll, El.Div("ob-custom-options", scroll, _optionsEmpty));
            body.pickingMode = PickingMode.Ignore;
            return Page(Strings.T("ob.custom.title"), Strings.T("ob.custom.sub"), body);
        }

        private List<LookCategory> VisibleCategories() =>
            CustomizerModel.Visible(SelectedLook, _spec, OptionsFor(SelectedLook), _resolves);

        private void RefreshCustomizeAvailability() => _flow.CustomizeAvailable = VisibleCategories().Count > 0;

        private void BuildRail()
        {
            _rail.Clear();
            _railButtons.Clear();
            var visible = VisibleCategories();
            if (!visible.Contains(_category) && visible.Count > 0) _category = visible[0];
            foreach (var c in visible)
            {
                var cat = c;
                var b = new Button(() => { if (_busy) return; Cues.Select(); _category = cat; BuildRail(); }) { text = "", enableRichText = false };
                b.AddToClassList("ob-rail-item");
                b.EnableInClassList("ob-rail-item--on", c == _category);
                b.Add(new IconBadge(CustomizerModel.CategoryIcon(c), c == _category ? "purple" : "plum"));
                var l = El.Text(Strings.T(CustomizerModel.CategoryKey(c)), "ob-rail-label");
                l.pickingMode = PickingMode.Ignore;
                b.Add(l);
                b.userData = c;
                // Rebuilding the rail must keep its selected category visible, including a restored or keyboard choice.
                // Wait for its real layout; the last categories can sit below this landscape viewport.
                b.RegisterCallback<GeometryChangedEvent>(e =>
                {
                    if (_category == cat && e.newRect.height > 0f && b.panel != null) _railScroll.ScrollTo(b);
                });
                _railButtons.Add(b);
                _rail.Add(b);
            }
            BuildOptions();
        }

        private void BuildOptions()
        {
            _optionGrid.Clear();
            _optionCards.Clear();
            var options = CustomizerModel.Options(SelectedLook, _category, _spec, OptionsFor(SelectedLook), _resolves);
            _optionsEmpty.style.display = options.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var o in options)
            {
                var opt = o;
                var label = Strings.T(o.label ?? "") is string t && t != o.label ? t : o.label;
                var card = new ChoiceCard(o.Id, label, null, _ => PickOption(opt), ChoiceCard.Layout.Vertical, o.thumb, null);
                card.AddToClassList("ob-option-card");
                if (o.thumb == null && o.hasSwatch)
                {
                    var sw = El.Div("ob-swatch");
                    sw.style.backgroundColor = o.swatch;
                    sw.pickingMode = PickingMode.Ignore;
                    card.Face.Insert(0, sw);
                }
                card.SetSelected(o.IsCurrent(_spec));
                _optionCards.Add(card);
            }
            var grid = ChoiceGrid.Build(_optionCards.Cast<VisualElement>().ToList(), 5, 236f, 1.4f, 22f);
            while (grid.childCount > 0) _optionGrid.Add(grid[0]);
            _optionGrid.AddToClassList("kit-grid");
        }

        private void PickOption(CustomizerOption option)
        {
            if (_busy || option == null || _spec == null) return;
            var next = option.ApplyTo(_spec);
            if (next.Key() == _spec.Key()) return;
            if (!TryPresent(SelectedLook, next)) return;
            SetSpec(next); Cues.Select(); _reviewComplete = false;
            ApplyFraming();
            foreach (var c in _optionCards) c.SetSelected(c.Id == option.Id, c.Id == option.Id);
            React(OnboardingCopy.Variant("ob.say.lookPick", ++_lookChanges, 3), "emote_thumbs_up", "emote_nod");
            RefreshAvailability();
            Save();
        }

        private void BrowseLook(int direction)
        {
            if (_models.Count < 2) return;
            PickModel((_lookIndex + direction + _models.Count) % _models.Count);
        }

        private void RefreshLook(bool animate)
        {
            var look = SelectedLook;
            _lookName.text = look == null ? Strings.T("creator.look") : look.label;
            _lookCount.text = _models.Count > 1 ? Strings.F("ob.look.count", _lookIndex + 1, _models.Count) : "";
            if (look != null)
            {
                var next = _spec != null && _spec.baseId == look.id ? _spec : DefaultSpec(look);
                float yaw = _touched ? _preview.Yaw : look.previewYaw;
                if (_presentedSpecKey != next.Key())
                {
                    if (!TryPresent(look, next)) { RefreshAvailability(); return; }
                    SetSpec(next);
                }
                _preview.SetIdle(_portrait ? FaceIdle : FullIdle);
                _preview.SetYaw(yaw); _preview.SetPortrait(_portrait);
                if (animate) Motion.Fade(_model, 0.35f, 1f, 0.18f);
            }
            RefreshAvailability();
            RefreshCustomizeAvailability();
            FocusPreview(_portrait, false);
        }



        private void FocusPreview(bool portrait, bool user = true)
        {
            _portrait = portrait;
            _bodyFocus.EnableInClassList("ob-seg--on", !portrait);
            _faceFocus.EnableInClassList("ob-seg--on", portrait);
            _stage.EnableInClassList("ob-stage--face", portrait && OnLookSteps);
            ApplyFraming();
            if (user) { Cues.Select(); Save(); }
        }

        private bool OnLookSteps => _flow.Step == CreatorStep.Look || _flow.Step == CreatorStep.Customize;

        /// <summary>Face framing on the Look step (when chosen) and while typing the name; full body everywhere else.</summary>
        private void ApplyFraming()
        {
            if (_preview == null) return;
            // The player card (Confirm) shows an ID-photo crop: head and shoulders; the celebration goes back to full body.
            bool face = _typing || (_portrait && OnLookSteps) || (_flow.Step == CreatorStep.Confirm && _celebrated == null);
            _preview.SetIdle(face ? FaceIdle : FullIdle);
            _preview.SetPortrait(face);
        }

        protected override void OnShown()
        {
            OnboardingAudio.StartTheme();
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
            _flow.HasLook = SelectedLook != null && PreviewReady;
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
                var card = new ChoiceCard(t.id, OnboardingCopy.TraitName(t), OnboardingCopy.TraitEffect(t), _ => PickTrait(trait), ChoiceCard.Layout.Horizontal,
                    KitIcons.Load(OnboardingCopy.TraitArt(t.id)), OnboardingCopy.TraitIcon(t.id), "purple");
                card.AddToClassList("ob-trait-card");
                _traitCards.Add(card);
            }
            _traitDots = El.Div("ob-dots", El.Div("ob-dot"), El.Div("ob-dot"));
            _traitDots.pickingMode = PickingMode.Ignore;
            var grid = ChoiceGrid.Build(_traitCards.Cast<VisualElement>().ToList(), 3, 236f);
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

        /// <summary>
        /// Step 7, Confirm: ONE player card, "a look of yourself" (user review 2026-10-09: the stacked containers read as a
        /// form). Trading/ID-card style: the live character on the left, the name, tribe badge, origin, dream and the two
        /// traits as icons on the right; every part taps back to its step. Under it: the 18+ checkbox with its text beside
        /// it, and the Game Cash line.
        /// </summary>
        private VisualElement BuildConfirmPage()
        {
            _cardPortrait = El.Div("ob-card-portrait");
            _cardPortrait.style.backgroundImage = Background.FromRenderTexture(_preview.Texture);
            var portraitHit = CardHit("ob-card-portrait-hit", CreatorStep.Look, _cardPortrait);

            _cardName = El.Text("", "ob-card-name");
            var nameHit = CardHit("ob-card-row--name", CreatorStep.Name, _cardName);
            _cardTribeBadge = El.Div("ob-card-tribe-badge");
            _cardTribe = El.Text("", "ob-card-tribe");
            var tribeHit = CardHit("ob-card-row--tribe", CreatorStep.Tribe, _cardTribeBadge, _cardTribe);
            var header = El.Div("ob-card-header", nameHit, tribeHit);

            _cardOriginArt = El.Div("ob-card-origin-art");
            _cardOrigin = El.Text("", "ob-card-value");
            var originRow = El.Div("ob-card-row ob-card-row--static", Caption("ob.sum.origin"), El.Div("ob-card-line", _cardOriginArt, _cardOrigin));
            _cardDreamIcon = new IconBadge("dream", "gold");
            _cardDream = El.Text("", "ob-card-value");
            var dreamHit = CardHit("ob-card-row", CreatorStep.Dream, Caption("ob.sum.dream"), El.Div("ob-card-line", _cardDreamIcon, _cardDream));
            _cardTraits = El.Div("ob-card-line ob-card-traits");
            var traitsHit = CardHit("ob-card-row", CreatorStep.Traits, Caption("ob.sum.traits"), _cardTraits);
            var info = El.Div("ob-card-info", header, originRow, dreamHit, traitsHit);
            info.pickingMode = PickingMode.Ignore;
            var band = El.Div("ob-card-band", El.Text(Strings.T("ob.card.band"), "ob-card-band-text"), El.Text(Strings.T("ob.confirm.sub"), "ob-card-band-hint"));
            var shine = El.Div("ob-card-holo");
            _idCard = El.Div("ob-idcard", band, El.Div("ob-card-body", portraitHit, info), shine);
            El.PassThrough(band);
            El.PassThrough(shine);

            _ageRow = new CheckRow(Strings.T("ob.confirm.age"), ToggleAge);
            _ageRow.AddToClassList("ob-age-row");
            var note = El.Text(Strings.T("ob.confirm.note"), "ob-note ob-note--card");
            var page = Page(Strings.T("ob.confirm.title"), Strings.T("ob.confirm.sub"), _idCard, El.Div("ob-card-foot", _ageRow, note));
            _confirmTitle = page.Q<Label>(className: "ob-title");
            return page;
        }

        private static Label Caption(string key)
        {
            var l = El.Text(Strings.T(key), "ob-card-caption");
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        /// <summary>A tappable part of the card that jumps back to `step` (contents never steal the tap).</summary>
        private Button CardHit(string classes, CreatorStep step, params VisualElement[] children)
        {
            var b = new Button(() => { Cues.Play(Cues.SfxTap); EditFromSummary(step); }) { text = "", enableRichText = false };
            foreach (var c in classes.Split(' ')) b.AddToClassList(c);
            b.AddToClassList("ob-card-hit");
            foreach (var c in children) { El.PassThrough(c); b.Add(c); }
            return b;
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
            _ageRow.SetChecked(_draft.ageConfirmed);
            if (_draft.ageConfirmed) Cues.Select(); else Cues.Deselect();
            _reviewComplete = false;
            Validate();
        }

        /// <summary>The card's values from the current draft (after any change).</summary>
        private void RefreshSummary()
        {
            if (_idCard == null) return;
            _cardName.text = string.IsNullOrEmpty(_draft.alias) ? "—" : _draft.alias;
            var tribe = _reg.TribeOrder.FirstOrDefault(t => t.id == _draft.tribeId);
            _cardTribe.text = tribe?.name?.ToUpperInvariant() ?? "—";
            var badge = OnboardingArt.TribeBadge(_draft.tribeId);
            _cardTribeBadge.style.backgroundImage = badge != null ? new StyleBackground(badge) : new StyleBackground(StyleKeyword.None);
            _cardTribeBadge.style.display = badge != null ? DisplayStyle.Flex : DisplayStyle.None;
            _cardDream.text = _reg.Dreams.FirstOrDefault(d => d.id == _draft.dreamId)?.name ?? "—";
            _cardTraits.Clear();
            foreach (var t in _reg.Traits.Where(t => _draft.traitIds.Contains(t.id)))
            {
                var chip = El.Div("ob-card-trait", new IconBadge(OnboardingCopy.TraitIcon(t.id), "purple", KitIcons.Load(OnboardingCopy.TraitArt(t.id))),
                    El.Text(OnboardingCopy.TraitName(t), "ob-card-value ob-card-value--trait"));
                El.PassThrough(chip);
                _cardTraits.Add(chip);
            }
            if (_confirmTitle != null) _confirmTitle.text = Strings.F("ob.confirm.title", string.IsNullOrEmpty(_draft.alias) ? "you" : _draft.alias);
            RefreshOrigin(false);
        }

        private void RefreshOrigin(bool refreshCard = true)
        {
            if (_cardOrigin == null) return;
            if (Origin == null)
            {
                _cardOrigin.text = "—";
                _cardOriginArt.style.display = DisplayStyle.None;
            }
            else
            {
                var name = Strings.T(LotteryModel.NameKey(Origin.outcomeId));
                string text = name + " · " + Strings.F("ob.lottery.cash", LotteryModel.CashWhole(Origin.cashCents));
                if (Origin.debtCents > 0) text += " · " + Strings.F("ob.lottery.debt", LotteryModel.CashWhole(Origin.debtCents));
                _cardOrigin.text = text;
                var art = OnboardingArt.CardFront(Origin.outcomeId);
                _cardOriginArt.style.backgroundImage = art != null ? new StyleBackground(art) : new StyleBackground(StyleKeyword.None);
                _cardOriginArt.style.display = art != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (refreshCard) RefreshSummary();
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

            bool look = step == CreatorStep.Look || step == CreatorStep.Customize;
            _frame.Root.EnableInClassList("ob-mode-look", look);
            _frame.Root.EnableInClassList("ob-mode-custom", step == CreatorStep.Customize);
            _frame.Root.EnableInClassList("ob-mode-confirm", step == CreatorStep.Confirm);
            _frame.Root.EnableInClassList("ob-mode-side", !look && step != CreatorStep.Confirm);
            _frame.Progress.Set(CreatorFlow.ProgressFor(step), !instant);
            _frame.Back.style.visibility = step == CreatorStep.Look ? Visibility.Hidden : Visibility.Visible;
            _lookControls.style.display = look ? DisplayStyle.Flex : DisplayStyle.None;
            _dragHint.style.display = look && !_touched && SelectedLook != null ? DisplayStyle.Flex : DisplayStyle.None;
            _stage.EnableInClassList("ob-stage--face", _portrait && look);
            ApplyFraming();
            if (step == CreatorStep.Look) { StartSway(); RefreshModelCards(); }
            if (step == CreatorStep.Customize) BuildRail();
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
            _ageRow.SetChecked(_draft.ageConfirmed);
            Validate();
            SayArrival(step);
            Save();
        }

        private void SayArrival(CreatorStep step)
        {
            switch (step)
            {
                case CreatorStep.Look: React(Strings.T("ob.say.look"), "emote_wave"); break;
                case CreatorStep.Customize: React(Strings.T("ob.say.custom"), "emote_point"); break;
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
            _flow.HasLook = SelectedLook != null && PreviewReady;
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
            int targetIndex = string.IsNullOrEmpty(saved.lookKey) ? _lookIndex : _models.FindIndex(m => m.id == saved.lookKey);
            _portrait = saved.portrait;
            _touched = true;
            if (targetIndex >= 0)
            {
                var model = _models[targetIndex];
                bool custom = saved.spec != null && saved.spec.baseId == model.id && ResolvesSpec(saved.spec);
                var next = custom ? saved.spec : DefaultSpec(model);
                if (TryPresent(model, next)) { _lookIndex = targetIndex; SetSpec(next); }
                if (saved.spec != null && !custom) ShowError(Strings.T("common.unavailable"));
            }
            else ShowError(Strings.T("common.unavailable"));
            _preview.SetYaw(saved.yaw);
            _flow.HasLook = SelectedLook != null && PreviewReady;
            RefreshCustomizeAvailability();
            _flow.Restore(saved.step, saved.furthest);
            Log.Info("creator", "Restored the unfinished character at step " + _flow.Step);
        }

        private void Save()
        {
            if (!_built || _store == null || _celebrated != null || !PreviewReady) return;
            _store.Save(_flow, LookKey, _preview?.Yaw ?? 180f, _portrait, _spec);
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
            _frame.Root.RemoveFromClassList("ob-mode-confirm");
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
            _preview.SetYaw(SelectedLook?.previewYaw ?? 0f);
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
            if (_flow.Step == CreatorStep.Confirm && index == 0) _ageRow.Press();
            if (_flow.Step == CreatorStep.Look) PickModel(index);
            if (_flow.Step == CreatorStep.Customize && index >= 0 && index < _optionCards.Count) _optionCards[index].Press();
        }

        /// <summary>Captures: types into the alias field (fires the same change path as the keyboard).</summary>
        public void TypeAlias(string value) => _alias.value = value;

        /// <summary>Captures: the Face/Full toggle.</summary>
        public void SetFaceForAutomation(bool face) => FocusPreview(face);

        /// <summary>Captures and checks: jump to a step already reached.</summary>
        public void JumpForAutomation(CreatorStep step) { if (_flow.JumpTo(step)) ShowStep(step, 1); }

        public CheckRow AgeRow => _ageRow;

        /// <summary>Captures: selects a customizer category by rail position.</summary>
        public void SelectCategoryForAutomation(int railIndex)
        {
            if (railIndex < 0 || railIndex >= _railButtons.Count) return;
            _category = (LookCategory)_railButtons[railIndex].userData;
            BuildRail();
        }

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
                int index = _models.FindIndex(m => m.look != null && Array.IndexOf(m.look, "head_" + want) >= 0);
                if (index >= 0 && index != _lookIndex) PickModel(index);
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
