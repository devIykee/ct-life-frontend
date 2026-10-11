using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.Net;
using CTLife.Rest.Model;
using CTLife.UI;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.EditorTools
{
    /// <summary>
    /// Batchmode PlayMode capture of the first-time flow (LotteryScreen, CharacterCreatorScreen and the UI kit) through the
    /// real UiRoot, PanelSettings and stylesheets, rendered into a 2670×1200 target (the Seeker's screen). Every screen and
    /// state is saved as NN-&lt;screen&gt;-&lt;state&gt;.png, plus half-size frame sequences of the card flip and the celebration
    /// (strip-*/NN.png, stitched afterwards), and a receipt with timings, failures and the onboarding textures' Android sizes.
    /// The server is faked at the delegate seams the FlowController uses (roll, submit), so nothing calls the network.
    /// Unity -batchmode -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.OnboardingCapture.Launch
    ///       [-ctlife-captureOut /abs/dir]      (no -nographics: cameras and panels must render)
    /// </summary>
    [InitializeOnLoad]
    public static class OnboardingCapture
    {
        private const string ActiveKey = "ctlife.onboarding.capture";
        private const int W = 2670, H = 1200;

        private static string _out;
        private static bool _started, _driving;
        private static double _deadline;
        private static RenderTexture _rt, _half;
        private static Texture2D _read, _readHalf;
        private static readonly JObject Receipt = new JObject();
        private static readonly JArray Shots = new JArray();
        private static readonly List<string> Failures = new List<string>();
        private static Camera _previewCam;
        private static MethodInfo _renderPanels;

        static OnboardingCapture()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            _out = Arg("-ctlife-captureOut") ?? DefaultOut();
            if (EditorApplication.isPlaying) EditorApplication.delayCall += Drive;
            else
            {
                EditorApplication.playModeStateChanged += OnPlayMode;
                _deadline = EditorApplication.timeSinceStartup + 240;
                EditorApplication.update += ReEnter;
            }
        }

        private static string Set => Arg("-ctlife-captureSet") ?? "onboarding";

        private static string DefaultOut() => Path.GetFullPath(Path.Combine(Application.dataPath,
            Set == "home" ? "../../../ct-life-evidence/home-ui" : Set == "r2" ? "../../../ct-life-evidence/onboarding-r2" : "../../../ct-life-evidence/onboarding-ui"));

        public static void Launch()
        {
            SessionState.SetBool(ActiveKey, true);
            _out = Arg("-ctlife-captureOut") ?? DefaultOut();
            _deadline = EditorApplication.timeSinceStartup + 300;
            EditorApplication.update += WaitForEditor;
        }

        private static void WaitForEditor()
        {
            if (EditorApplication.timeSinceStartup > _deadline) { EditorApplication.update -= WaitForEditor; Finish("editor not ready"); return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || _started) return;
            _started = true;
            EditorApplication.update -= WaitForEditor;
            try
            {
                if (Set == "localart")
                {
                    CreatorLocalArtCapture.Preflight();
                    if (!File.Exists(Path.GetFullPath(OnboardingArtImporter.BackdropPath))) throw new InvalidOperationException("Existing onboarding backdrop is required; local-art proof does not reauthor it.");
                }
                else
                {
                    if (!File.Exists(Path.GetFullPath(OnboardingArtImporter.BackdropPath))) OnboardingArtImporter.BuildBackdrop();
                    AssetDatabase.Refresh();
                }
            }
            catch (Exception e)
            {
                Failures.Add("backdrop/preflight: " + e.Message); Debug.LogException(e);
                if (Set == "localart") { Finish(e.Message); return; }
            }
            // Local-art proof preflight requires an already-empty fresh batch scene; it discards no scene.
            if (Set != "localart") EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.EnterPlaymode();
        }

        private static void ReEnter()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.update -= ReEnter; return; }
            if (EditorApplication.timeSinceStartup > _deadline) { EditorApplication.update -= ReEnter; Finish("play mode never started"); return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= ReEnter;
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            Drive();
        }

        private static void Step(string what) => Debug.Log($"CTLIFE [ob-capture] {what} (t={Time.realtimeSinceStartup:F1})");

        private static async void Drive()
        {
            if (_driving) return;
            _driving = true;
            _deadline = EditorApplication.timeSinceStartup + 900;
            EditorApplication.update += Watchdog;
            string fatal = null;
            try
            {
                Directory.CreateDirectory(_out);
                await Setup();
                if (Set == "localart")
                {
                    await CreatorLocalArtCapture.Run(Shot, Wait, Expect, Step, Receipt);
                }
                else if (Set == "r2")
                {
                    await OnboardingCaptureR2.Run(Shot, Wait, Expect, Step, Receipt);
                }
                else if (Set == "refinements")
                {
                    await OnboardingRefinementCapture.Run(Shot, Wait, Expect, Step, Receipt);
                }
                else if (Set == "lottery")
                {
                    await Lottery(); // the origin cards only (fast iteration on the lottery screens)
                }
                else if (Set == "home")
                {
                    await OnboardingCaptureHome.Run(Shot, Wait, Expect, Step);
                    await LookOrderShots();
                }
                else if (Set == "lobby")
                {
                    await LobbyFlowCapture.Run(Shot, Wait, Expect, Step, Receipt);
                }
                else if (Set == "duointeriors")
                {
                    await ToastCapture.Run(Shot, Wait, Expect, Step, Receipt);
                    await LobbyFlowCapture.Run(Shot, Wait, Expect, Step, Receipt);
                    await DuoSocialCapture.Run(Shot, Wait, Expect, Step, Receipt);
                    await DuoInteriorChipCapture.Run(Shot, Wait, Expect, Step, Receipt);
                }
                else if (Set == "duoios")
                {
                    await DuoIosCapture.Run(Shot, Wait, Expect, Step, Receipt);
                }
                else if (Set == "hud")
                {
                    await CityHudCapture.Run(Shot, Wait, Expect, Step, Receipt);
                }
                else
                {
                    await Lottery();
                    await Creator();
                    await KitSheet();
                }
            }
            catch (Exception e) { fatal = e.GetType().Name + ": " + e.Message; Debug.LogException(e); }
            Finish(fatal);
        }

        private static void Watchdog()
        {
            if (EditorApplication.timeSinceStartup < _deadline) return;
            EditorApplication.update -= Watchdog;
            Finish("watchdog: capture did not finish in 900 s");
        }

        // ------------------------------------------------------------------ rendering

        private static async Task Setup()
        {
            if (!Services.TryGet<ConfigStore>(out var config))
            {
                config = Services.Register(new ConfigStore());
                config.LoadLocal();
            }
            var ui = UiRoot.Create();
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "ob-capture" };
            _rt.Create();
            _half = new RenderTexture(W / 2, H / 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "ob-capture-half" };
            _half.Create();
            _read = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
            _readHalf = new Texture2D(W / 2, H / 2, TextureFormat.RGBA32, false, false);
            var doc = ui.GetComponent<UIDocument>();
            doc.panelSettings.targetTexture = _rt;
            doc.panelSettings.clearColor = true;
            doc.panelSettings.colorClearValue = Color.black;
            // Batchmode has no game view driving rendering: render offscreen panels ourselves before each read.
            var t = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
            _renderPanels = t?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "RenderOffscreenPanels" && m.GetParameters().Length == 0);
            Step("setup: panel renderer " + (_renderPanels != null ? "found" : "not found") + ", Screen " + Screen.width + "x" + Screen.height);
            await Frames(3);
            Receipt["panel"] = ui.Root.layout.width.ToString("0") + "x" + ui.Root.layout.height.ToString("0");
            Step("panel " + Receipt["panel"]);
        }

        private static async Task Frames(int n)
        {
            for (int i = 0; i < n; i++) await Awaitable.NextFrameAsync();
        }

        private static async Task Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) await Awaitable.NextFrameAsync();
        }

        private static void Render()
        {
            if (_previewCam == null)
            {
                var go = GameObject.Find("CTLife.AvatarPreview");
                if (go != null) _previewCam = go.GetComponentInChildren<Camera>();
            }
            if (_previewCam != null && _previewCam.enabled) _previewCam.Render();
            try { _renderPanels?.Invoke(null, null); }
            catch (Exception e) { Debug.LogWarning("CTLIFE [ob-capture] panel render: " + e.Message); _renderPanels = null; }
        }

        private static async Task Shot(string name, float settle = 0.35f)
        {
            await Wait(settle);
            await Frames(2);
            Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
            _read.Apply(false);
            RenderTexture.active = prev;
            var path = Path.Combine(_out, name + ".png");
            File.WriteAllBytes(path, _read.EncodeToPNG());
            var c = _read.GetPixel(W / 2, H / 2);
            if (c.r + c.g + c.b < 0.01f && _read.GetPixel(40, 40).r < 0.01f) Failures.Add(name + ": frame looks empty");
            Shots.Add(name + ".png");
            Step("shot " + name);
        }

        /// <summary>Half-size frames for a strip, with animation time slowed so a fast move is sampled densely.</summary>
        private static async Task Strip(string name, int frames, float realInterval, float timeScale)
        {
            var dir = Path.Combine(_out, "strip-" + name);
            Directory.CreateDirectory(dir);
            UI.Kit.Motion.SetTimeScale(timeScale);
            try
            {
                for (int i = 0; i < frames; i++)
                {
                    await Frames(1);
                    Render();
                    Graphics.Blit(_rt, _half);
                    var prev = RenderTexture.active;
                    RenderTexture.active = _half;
                    _readHalf.ReadPixels(new Rect(0, 0, W / 2, H / 2), 0, 0, false);
                    _readHalf.Apply(false);
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, i.ToString("00") + ".png"), _readHalf.EncodeToPNG());
                    await Wait(realInterval);
                }
            }
            finally { UI.Kit.Motion.SetTimeScale(1f); }
            Step("strip " + name + " (" + frames + " frames)");
        }

        // ------------------------------------------------------------------ lottery

        private static async Task Lottery()
        {
            // 1. Insider: idle, shuffle, spotlight, the flip (strip), the result.
            var insider = new PostLotteryRoll200Response(PostLotteryRoll200Response.OutcomeEnum.Insider, 300000, 0, "studio");
            var screen = new LotteryScreen(false, async () => { await Awaitable.WaitForSecondsAsync(0.8f); return insider; }, _ => { }, autoStart: false);
            string phase = null;
            screen.Phase += p => phase = p;
            screen.Show();
            await Shot("01-lottery-idle", 0.8f);
            screen.PressCta();
            await Shot("02-lottery-shuffle", 0.28f);
            await Wait(0.9f);
            await Shot("03-lottery-drawing", 0.1f);
            LogCards(screen.Element);
            await Until(() => phase == "reveal", 12f, "lottery reveal");
            await Strip("flip-insider", 80, 0.04f, 0.3f); // flip + unfold
            LogCards(screen.Element);
            await Until(() => screen.Revealed, 10f, "insider result");
            await Shot("04-lottery-insider-result", 1.4f);
            screen.Close();

            // 2. Exit Liquidity: the bonk, muted shower and integrated result card.
            var exit = new PostLotteryRoll200Response(PostLotteryRoll200Response.OutcomeEnum.ExitLiquidity, 30000, 72000, "parents_basement");
            phase = null;
            screen = new LotteryScreen(false, async () => { await Awaitable.WaitForSecondsAsync(0.5f); return exit; }, _ => { }, autoStart: false);
            screen.Phase += p => phase = p;
            screen.Show();
            await Wait(0.3f);
            screen.PressCta();
            await Until(() => phase == "landed", 14f, "exit landed");
            await Shot("05-lottery-exit-reveal", 0.25f);
            await Until(() => screen.Revealed, 6f, "exit result");
            await Shot("06-lottery-exit-result", 2.0f);
            screen.Close();

            // 3. A failed roll: the error, the retry CTA, the cards back to idle.
            screen = new LotteryScreen(false, async () =>
            {
                await Awaitable.WaitForSecondsAsync(0.4f);
                throw new ApiException(0, "NETWORK_ERROR", "Network unreachable", isNetwork: true);
            }, _ => { }, autoStart: false);
            screen.Show();
            await Wait(0.3f);
            screen.PressCta();
            await Until(() => !screen.Rolling, 12f, "error path");
            await Shot("07-lottery-error", 2.2f);
            screen.Close();

            // 4. A Seeker Genesis holder: three cards (rc1 seeker weight is 0, so no odds are shown).
            screen = new LotteryScreen(true, async () => { await Awaitable.WaitForSecondsAsync(0.4f); return insider; }, _ => { }, autoStart: false);
            screen.Show();
            await Shot("08-lottery-seeker-idle", 0.6f);
            screen.Close();

            // 5. Reduced motion: no shuffle, hop or particles; a short crossfade to the result.
            DeviceFeel.OverrideReducedMotion(true);
            try
            {
                screen = new LotteryScreen(false, async () => { await Awaitable.WaitForSecondsAsync(0.3f); return exit; }, _ => { }, autoStart: false);
                screen.Show();
                await Wait(0.3f);
                screen.PressCta();
                await Until(() => screen.Revealed, 6f, "reduced-motion result");
                await Shot("09-lottery-reduced-motion-result", 1.6f);
                screen.Close();
            }
            finally { DeviceFeel.OverrideReducedMotion(null); }
        }

        /// <summary>Receipt detail: each lottery card's classes and its glow's resolved opacity/size (the hot ring).</summary>
        private static void LogCards(VisualElement root)
        {
            if (root == null) return;
            foreach (var card in root.Query(className: "ob-card").ToList())
            {
                var glow = card.Q(className: "ob-card-glow");
                Step($"card {card.name} classes=[{string.Join(" ", card.GetClasses())}] scale={card.resolvedStyle.scale.value} " +
                     $"glow opacity={glow?.resolvedStyle.opacity} size={glow?.layout.size} image={(glow?.resolvedStyle.backgroundImage.texture != null)}");
            }
        }

        private static async Task Until(Func<bool> done, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < end) await Awaitable.NextFrameAsync();
            if (!done()) Failures.Add("timed out waiting for " + what);
        }

        // ------------------------------------------------------------------ creator

        private static CharacterCreatorScreen NewCreator(ConfigStore config, Action<CharacterCreatorScreen, CharacterDraft> onSubmit)
        {
            CharacterCreatorScreen screen = null;
            screen = new CharacterCreatorScreen(config.Registries, d => onSubmit(screen, d), serverLookConfirmed: _ => true)
            {
                Origin = OriginSummary.From(300000, 0, "studio", config.Config("lottery")),
            };
            return screen;
        }

        private static async Task Creator()
        {
            var config = Services.Get<ConfigStore>();
            new CreatorDraftStore(null).Clear();
            int submits = 0;
            Action<CharacterCreatorScreen, CharacterDraft> submit = (s, d) => { submits++; s.SetBusy(true); };
            var screen = NewCreator(config, submit);
            screen.Show();
            await Shot("10-creator-look", 1.6f);
            screen.SetFaceForAutomation(true);
            await Shot("11-creator-look-face", 1.0f);
            screen.SetFaceForAutomation(false);
            screen.PickForAutomation(1);
            await Shot("12-creator-look-next", 1.2f);

            screen.PressCta();
            await Shot("13-creator-tribe-empty", 0.8f);
            Expect(screen.Step == CreatorStep.Tribe, "NEXT on Look goes to Tribe");
            screen.PressCta();
            Expect(screen.Step == CreatorStep.Tribe, "NEXT is disabled until a tribe is picked");
            screen.PickForAutomation(0);
            await Shot("14-creator-tribe-selected", 1.1f);

            screen.PressCta();
            await Shot("15-creator-traits-empty", 0.8f);
            screen.PickForAutomation(0);
            await Shot("16-creator-traits-one", 0.7f);
            screen.PickForAutomation(2);
            await Shot("17-creator-traits-two", 0.9f);
            screen.PickForAutomation(4);
            await Shot("18-creator-traits-swap", 0.1f);
            Expect(screen.Flow.Draft.traitIds.Count == 2, "a third trait replaces the oldest");

            screen.PressCta();
            await Shot("19-creator-dream-empty", 0.8f);
            screen.PickForAutomation(1);
            await Shot("20-creator-dream-selected", 0.9f);

            screen.PressCta();
            await Shot("21-creator-name-empty", 0.8f);
            screen.TypeAlias("ab");
            await Shot("22-creator-name-too-short", 0.4f);
            screen.TypeAlias("gm_anon");
            await Shot("23-creator-name-valid", 1.1f);
            screen.ForceTypingLayout(true);
            var keyboard = KeyboardMock();
            UiRoot.Instance.Root.Add(keyboard);
            await Shot("24-creator-name-typing-keyboard", 0.8f);
            keyboard.RemoveFromHierarchy();
            screen.ForceTypingLayout(false);

            screen.PressCta();
            await Shot("25-creator-confirm", 1.0f);
            Expect(screen.Step == CreatorStep.Confirm, "Name → Confirm");
            screen.PressCta();
            Expect(submits == 0, "START MY STORY is disabled until 18+ is confirmed");

            // Pause/resume: the screen is rebuilt from the saved draft (as after a process restart) on the same step.
            screen.Close();
            screen = NewCreator(config, submit);
            screen.Show();
            await Shot("26-creator-resumed-after-relaunch", 1.6f);
            Expect(screen.Step == CreatorStep.Confirm, "the draft resumes on Confirm");
            Expect(screen.Flow.Draft.alias == "gm_anon" && screen.Flow.Draft.traitIds.Count == 2, "the draft keeps its picks");
            Expect(!screen.Flow.Draft.ageConfirmed, "18+ is asked again after a relaunch");

            screen.PickForAutomation(0); // the 18+ card
            await Shot("27-creator-confirm-ready", 0.6f);
            var cta = screen.Element.Q<UI.Kit.LippedButton>("ob-cta");
            cta?.ForcePressed(true);
            await Shot("28-creator-confirm-cta-pressed", 0.2f);
            cta?.ForcePressed(false);

            screen.PressCta();
            await Shot("29-creator-confirm-saving", 0.3f);
            Expect(submits == 1, "submit called once");
            screen.ShowError(Strings.Error("ALIAS_TAKEN", "Alias is taken."));
            screen.SetBusy(false);
            await Shot("30-creator-confirm-error", 0.7f);

            // Success: the celebration (strip), then ENTER THE CITY completes the flow.
            screen.PressCta();
            await Wait(0.2f);
            var celebrate = screen.Celebrate();
            await Strip("celebration", 14, 0.16f, 0.35f);
            await Shot("31-creator-celebration", 1.4f);
            screen.PressEnterCity();
            await Task.WhenAny(celebrate, Task.Delay(3000));
            Expect(celebrate.IsCompleted, "ENTER THE CITY completes the celebration");
            screen.Close();
            Expect(new CreatorDraftStore(null).Load() == null, "the saved draft is cleared after creation");
        }

        /// <summary>A stand-in for the Android keyboard's footprint (device QA: it covers ~65% of the height in landscape).</summary>
        private static VisualElement KeyboardMock()
        {
            var k = new VisualElement { pickingMode = PickingMode.Ignore };
            k.style.position = UnityEngine.UIElements.Position.Absolute;
            k.style.left = 0; k.style.right = 0; k.style.bottom = 0;
            k.style.height = Length.Percent(65f);
            k.style.backgroundColor = new Color(0.12f, 0.13f, 0.14f, 0.92f);
            var label = new Label("Android keyboard area (65% of the height, measured on the Seeker)") { enableRichText = false };
            label.style.color = new Color(0.8f, 0.8f, 0.8f);
            label.style.fontSize = 40;
            label.style.marginTop = 40;
            label.style.unityTextAlign = TextAnchor.UpperCenter;
            k.Add(label);
            return k;
        }

        private static void Expect(bool ok, string what)
        {
            if (!ok) { Failures.Add("check failed: " + what); Debug.LogWarning("CTLIFE [ob-capture] check failed: " + what); }
            else Step("ok: " + what);
        }

        // ------------------------------------------------------------------ kit sheet

        /// <summary>The creator's first look for three install seeds: the first character differs per install (look-order fix).</summary>
        private static async Task LookOrderShots()
        {
            var config = Services.Get<ConfigStore>();
            for (int seed = 0; seed < 3; seed++)
            {
                new CreatorDraftStore(null).Clear();
                CreatorLookOrder.SetInstallSeed(seed);
                var screen = new CharacterCreatorScreen(config.Registries, _ => { }, serverLookConfirmed: _ => true);
                screen.Show();
                await Shot($"{20 + seed:00}-creator-first-look-seed{seed}", 1.4f);
                screen.Close();
            }
            new CreatorDraftStore(null).Clear();
        }

        private static async Task KitSheet()
        {
            var frame = new OnboardingFrame("ob-kit-sheet", null, null, "NEXT");
            frame.Back.style.visibility = Visibility.Visible;
            frame.Progress.Set(3, false);
            frame.SetHint("Pick a tribe");
            frame.Cta.SetEnabled(false);
            var col = new VisualElement();
            col.style.position = UnityEngine.UIElements.Position.Absolute;
            col.style.left = 0; col.style.right = 0; col.style.top = 170; col.style.bottom = 0;
            frame.Content.Add(col);

            VisualElement Row() { var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row; r.style.flexWrap = Wrap.Wrap; r.style.alignItems = Align.FlexEnd; r.style.marginBottom = 40; col.Add(r); return r; }
            VisualElement Gap(VisualElement e) { e.style.marginRight = 26; e.style.marginBottom = 20; return e; }

            var r1 = Row();
            r1.Add(Gap(new UI.Kit.LippedButton("DEFAULT", null)));
            var pressed = new UI.Kit.LippedButton("PRESSED", null); pressed.ForcePressed(true); r1.Add(Gap(pressed));
            var disabled = new UI.Kit.LippedButton("DISABLED", null); disabled.SetEnabled(false); r1.Add(Gap(disabled));
            var busy = new UI.Kit.LippedButton("SAVE", null); busy.SetBusy(true, "SAVING…"); r1.Add(Gap(busy));
            r1.Add(Gap(new UI.Kit.LippedButton("PURPLE", null, UI.Kit.LippedButton.Tone.Purple, UI.Kit.LippedButton.Size.Medium)));
            r1.Add(Gap(new UI.Kit.LippedButton("WHITE", null, UI.Kit.LippedButton.Tone.White, UI.Kit.LippedButton.Size.Medium)));
            r1.Add(Gap(new UI.Kit.LippedButton("", null, UI.Kit.LippedButton.Tone.Green, UI.Kit.LippedButton.Size.Square).WithIcon("next")));

            var r2 = Row();
            UI.Kit.ChoiceCard Card(string title, UI.Kit.ChoiceCard.State state, string icon = "perps")
            {
                var c = new UI.Kit.ChoiceCard(title, title, "One line of perk text", null, UI.Kit.ChoiceCard.Layout.Vertical, null, icon, "green");
                c.style.width = 360; c.style.height = 330;
                c.SetState(state);
                Gap(c);
                return c;
            }
            r2.Add(Card("Default", UI.Kit.ChoiceCard.State.Default));
            r2.Add(Card("Selected", UI.Kit.ChoiceCard.State.Selected));
            r2.Add(Card("Disabled", UI.Kit.ChoiceCard.State.Disabled));
            r2.Add(Card("Error", UI.Kit.ChoiceCard.State.Error));
            var box = new UI.Kit.ChoiceCard("age", "I'm 18 or older", "Required to play", null, UI.Kit.ChoiceCard.Layout.Horizontal, null, UI.Kit.ChoiceCard.Checkbox);
            box.style.width = 640; box.style.height = 160; box.SetState(UI.Kit.ChoiceCard.State.Selected);
            r2.Add(box);

            var bubble = new UI.Kit.SpeechBubble();
            bubble.style.left = 1300; bubble.style.top = 640;
            col.Add(bubble);
            var screen = new SheetScreen(frame.Root);
            screen.Show();
            bubble.Say("Speech bubble: short, first person.");
            await Shot("32-kit-states", 0.8f);
            screen.Close();
        }

        private sealed class SheetScreen : UiScreen
        {
            private readonly VisualElement _root;
            public SheetScreen(VisualElement root) => _root = root;
            protected override VisualElement Build() => _root;
        }

        // ------------------------------------------------------------------ receipt

        /// <summary>Android (ASTC) size of each onboarding texture as imported: blocks × 16 bytes, no mips.</summary>
        private static JArray TextureReport()
        {
            var list = new JArray();
            long total = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/UI/Onboarding" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                var android = ti.GetPlatformTextureSettings("Android");
                ti.GetSourceTextureWidthAndHeight(out int sw, out int sh);
                int max = android.overridden ? android.maxTextureSize : ti.maxTextureSize;
                float scale = Mathf.Min(1f, (float)max / Mathf.Max(sw, sh));
                int w = Mathf.Max(1, Mathf.RoundToInt(sw * scale)), h = Mathf.Max(1, Mathf.RoundToInt(sh * scale));
                int block = android.format.ToString() switch { "ASTC_4x4" => 4, "ASTC_5x5" => 5, "ASTC_6x6" => 6, "ASTC_8x8" => 8, "ASTC_10x10" => 10, "ASTC_12x12" => 12, _ => 0 };
                long bytes = block > 0 ? (long)Mathf.CeilToInt((float)w / block) * Mathf.CeilToInt((float)h / block) * 16 : (long)w * h * 4;
                total += bytes;
                list.Add(new JObject { ["path"] = path, ["source"] = sw + "x" + sh, ["android"] = w + "x" + h, ["format"] = android.format.ToString(), ["bytes"] = bytes });
            }
            list.Add(new JObject { ["totalBytes"] = total });
            Debug.Log("CTLIFE [ob-capture] onboarding textures on Android: " + (total / 1024f / 1024f).ToString("0.00") + " MiB");
            return list;
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        private static void Finish(string fatal)
        {
            EditorApplication.update -= Watchdog;
            SessionState.SetBool(ActiveKey, false);
            Receipt["fatal"] = fatal;
            try { Receipt["textures"] = TextureReport(); }
            catch (Exception e) { Debug.LogException(e); }
            Receipt["shots"] = Shots;
            Receipt["failures"] = new JArray(Failures);
            bool pass = fatal == null && Failures.Count == 0;
            Receipt["pass"] = pass;
            try { Directory.CreateDirectory(_out ?? DefaultOut()); File.WriteAllText(Path.Combine(_out ?? DefaultOut(), "receipt.json"), Receipt.ToString()); }
            catch (Exception e) { Debug.LogException(e); }
            Debug.Log($"CTLIFE [ob-capture] {(pass ? "PASS" : "FAIL")} fatal={fatal} failures={Failures.Count} shots={Shots.Count}");
            if (Application.isBatchMode) EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
