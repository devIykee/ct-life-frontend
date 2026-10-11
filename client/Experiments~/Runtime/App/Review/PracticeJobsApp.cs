#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CTLife.Core;
using CTLife.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace CTLife.App.Review
{
    /// <summary>Interactive local practice. No career, shift, score submission or payout API is called.</summary>
    public sealed class PracticeJobsApp : PhoneApp, IDisposable
    {
        private readonly CancellationToken _ownerCancellation;
        private CancellationTokenSource _attempt;
        private VisualElement _page, _game, _result;
        private bool _running, _disposed;
        private int _roundsStarted;
        public override string Id => "jobs";
        public override string Title => PracticeJobsStrings.T("title");
        internal bool IsRunning => _running;
        internal int RoundsStarted => _roundsStarted;
        internal VisualElement ActiveGame => _game;
        internal VisualElement Result => _result;
        internal string LastFailure { get; private set; }
        private static readonly string[] Keys = { "mod", "earnings", "thread", "router", "role", "receipt", "cues", "runway", "clip" };
        private static readonly string[] Ids = { "discord_mod_ban", "junior_analyst_earnings", "kols_engagement", RaidRouterRound.ProposedId, RoleLockRound.ProposedId, ReceiptMatchRound.ProposedId, LiveCuesRound.ProposedId, RunwayPlanRound.ProposedId, ClipCutRound.ProposedId };

        public PracticeJobsApp(CancellationToken ownerCancellation) => _ownerCancellation = ownerCancellation;

        public override VisualElement Open()
        {
            var page = El.VerticalScroll("ct-app-page");
            page.name = "practice-jobs";
            page.Add(Copy("practice", "ct-eyebrow"));
            page.Add(Copy("heading", "ct-h1"));
            page.Add(Copy("intro", "ct-body ct-muted"));
            for (int index = 0; index < Keys.Length; index++)
            {
                int choice = index;
                var card = El.Div("ct-card", Copy(Keys[index] + ".title", "ct-h2"), Copy(Keys[index] + ".body", "ct-body"));
                bool ready = TryDefinition(index, out var definition);
                card.Add(Plain(ready ? PracticeJobsStrings.F("details", definition.durationSec, definition.maxScore)
                    : PracticeJobsStrings.T("unavailable"), "ct-small ct-muted"));
                var play = El.Btn(PracticeJobsStrings.T(_running ? "busy" : "play"), () => { if (ReferenceEquals(_page, page)) Begin(choice); }, "ct-btn ct-btn-primary");
                play.name = "practice-play-" + Keys[index];
                play.SetEnabled(ready && !_running && !_disposed && !_ownerCancellation.IsCancellationRequested);
                card.Add(play); page.Add(card);
            }
            _page = page;
            return page;
        }

        // Phone.Close happens before a desk mounts. Closing the phone must not cancel that desk.
        public override void Closed() => _page = null;

        private async void Begin(int choice)
        {
            if (_running || _disposed || _ownerCancellation.IsCancellationRequested || _result != null) return;
            _running = true; // shared gate across all practice games, before any UI detach or await
            if (_page != null) foreach (var button in _page.Query<Button>().ToList()) button.SetEnabled(false);
            var attempt = CancellationTokenSource.CreateLinkedTokenSource(_ownerCancellation);
            var token = attempt.Token;
            _attempt = attempt;
            UiRoot ui = UiRoot.Instance;
            var existing = ui != null ? ui.ModalLayer.Children().ToHashSet() : new HashSet<VisualElement>();
            int score = 0, maximum = 0;
            string details = null;
            async Task<int> PlayRouter()
            {
                var result = await RaidRouter.Play(token);
                details = RaidRouterStrings.Summary(result);
                return result.Score;
            }
            async Task<int> PlayRoleLock()
            {
                var result = await RoleLock.Play(token);
                details = RoleLockStrings.Summary(result);
                return result.Score;
            }
            async Task<int> PlayReceiptMatch()
            {
                var result = await ReceiptMatch.Play(token);
                details = ReceiptMatchStrings.Summary(result);
                return result.Score;
            }
            async Task<int> PlayLiveCues()
            {
                var result = await LiveCues.Play(token);
                details = LiveCuesStrings.Summary(result);
                return result.Score;
            }
            async Task<int> PlayRunwayPlan()
            {
                var result = await RunwayPlan.Play(token);
                details = RunwayPlanStrings.Summary(result);
                return result.Score;
            }
            async Task<int> PlayClipCut()
            {
                var result = await ClipCut.Play(token);
                details = ClipCutStrings.Summary(result);
                return result.Score;
            }
            bool completed = false, cancelled = false, capturedGame = false;
            try
            {
                if (ui == null || !TryDefinition(choice, out var definition)) throw new InvalidOperationException("Practice definition unavailable.");
                if (ui.ModalLayer.Children().Any(c => c.ClassListContains("ct-desk-overlay")))
                    throw new InvalidOperationException("Another desk is already active.");
                maximum = definition.maxScore;
                Phone.Close();
                Task<int> play = choice == 0 ? ModDesk.Play(definition, token, practice: true)
                    : choice == 1 ? EarningsDesk.Play(definition, token, practice: true)
                    : choice == 2 ? ThreadWorkshop.Play(definition, token, practice: true)
                    : choice == 3 ? PlayRouter() : choice == 4 ? PlayRoleLock() : choice == 5 ? PlayReceiptMatch() : choice == 6 ? PlayLiveCues() : choice == 7 ? PlayRunwayPlan() : PlayClipCut();
                _game = FindOwnedGame(ui, existing);
                if (_game == null) throw new InvalidOperationException("Practice view did not open.");
                capturedGame = true;
                var header = _game.Q<VisualElement>(className: "ct-desk-header");
                if (header == null) throw new InvalidOperationException("Practice controls unavailable.");
                if (choice < 3) // The newer local games own their visible End practice controls.
                {
                    // Add once; never detach/reparent a child while ModDesk is alive (its detach callback bubbles).
                    var end = El.IconButton("close", PracticeJobsStrings.T("end"), () => {
                        if (ReferenceEquals(_attempt, attempt)) CancelRound();
                    }, sfx: null);
                    end.name = "practice-end";
                    header.Add(end);
                }
                _roundsStarted++;
                score = await play;
                token.ThrowIfCancellationRequested();
                completed = true;
            }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception error)
            {
                // Offline/availability failures are recoverable here. Do not send them to AppUi.Err/Log.Exception.
                ReportFailure(error, "play");
                completed = false;
            }
            finally
            {
                TryCancel(attempt);
                // An external detach can settle the task inside panel dispatch. Defer both remaining
                // hierarchy cleanup and the next view, while keeping the single-flight gate held.
                if (!_disposed && !_ownerCancellation.IsCancellationRequested)
                {
                    try { await Awaitable.NextFrameAsync(_ownerCancellation); }
                    catch (OperationCanceledException) { }
                }
                // Play can fail synchronously after mounting; claim only roots added by this attempt.
                if (!capturedGame && _game == null && ui != null) _game = FindOwnedGame(ui, existing);
                RemoveGame();
                attempt.Dispose();
                if (ReferenceEquals(_attempt, attempt)) _attempt = null;
            }
            _running = false;
            if (_disposed || _ownerCancellation.IsCancellationRequested || UiRoot.Instance == null) return;
            try
            {
                if (cancelled) Phone.Open(Id);
                else ShowResult(choice, completed ? score : (int?)null, maximum, details);
            }
            catch (Exception error)
            {
                RemoveResult();
                ReportFailure(error, "present-result");
                Bus.Publish(ToastSignal.Text(PracticeJobsStrings.T("recovery")));
            }
        }

        private static VisualElement FindOwnedGame(UiRoot ui, HashSet<VisualElement> existing)
            => ui.ModalLayer.Children().FirstOrDefault(c => !existing.Contains(c) && c.ClassListContains("ct-desk-overlay"));

        private static bool TryDefinition(int choice, out MiniGameDef definition)
        {
            definition = null;
            // This authored local candidate is not an imported/paid server assignment.
            if (choice == 3)
            {
                definition = new MiniGameDef { id = RaidRouterRound.ProposedId, careerId = "discord_mod",
                    durationSec = RaidRouterRound.Duration, maxScore = RaidRouterRound.Maximum };
                return true;
            }
            if (choice == 4)
            {
                definition = new MiniGameDef { id = RoleLockRound.ProposedId, careerId = "discord_mod",
                    durationSec = RoleLockRound.Duration, maxScore = RoleLockRound.Maximum };
                return true;
            }
            if (choice == 5)
            {
                definition = new MiniGameDef { id = ReceiptMatchRound.ProposedId, careerId = "junior_analyst",
                    durationSec = ReceiptMatchRound.Duration, maxScore = ReceiptMatchRound.Maximum };
                return true;
            }
            if (choice == 6)
            {
                definition = new MiniGameDef { id = LiveCuesRound.ProposedId, careerId = "kol_engagement",
                    durationSec = LiveCuesRound.Duration, maxScore = LiveCuesRound.Maximum };
                return true;
            }
            if (choice == 7)
            {
                definition = new MiniGameDef { id = RunwayPlanRound.ProposedId, careerId = "junior_analyst",
                    durationSec = RunwayPlanRound.Duration, maxScore = RunwayPlanRound.Maximum };
                return true;
            }
            if (choice == 8)
            {
                definition = new MiniGameDef { id = ClipCutRound.ProposedId, careerId = "kol_engagement",
                    durationSec = ClipCutRound.Duration, maxScore = ClipCutRound.Maximum };
                return true;
            }
            return choice >= 0 && choice < Ids.Length && Services.TryGet<ConfigStore>(out var config)
                && config.Registries != null && config.Registries.MiniGames.TryGetValue(Ids[choice], out definition)
                && definition.durationSec > 0 && definition.durationSec <= 600 && definition.maxScore > 0;
        }

        private void CancelRound()
        {
            if (!_running || _attempt == null) return;
            TryCancel(_attempt);
            // Also settle synchronously if UiRoot updates stop during owner teardown.
            RemoveGame();
        }

        private void RemoveGame()
        {
            var game = _game; _game = null;
            game?.RemoveFromHierarchy();
        }

        private void ShowResult(int choice, int? score, int maximum, string details = null)
        {
            if (_disposed || _ownerCancellation.IsCancellationRequested || UiRoot.Instance == null) return;
            var result = El.Div("ct-desk-overlay", El.Div("ct-desk-panel ct-shift-panel",
                Copy("practice", "ct-desk-eyebrow"), Copy(Keys[choice] + ".title", "ct-desk-title"),
                Copy(score.HasValue ? "done" : "interrupted", "ct-shift-heading"),
                Plain(score.HasValue ? PracticeJobsStrings.F("score", score.Value, maximum) : PracticeJobsStrings.T("recovery"), "ct-shift-copy"),
                Copy("noRewards", "ct-small ct-muted")));
            result.name = "practice-result";
            var panel = result[0];
            if (score.HasValue && !string.IsNullOrEmpty(details)) panel.Add(Plain(details, "ct-small ct-muted"));
            var again = El.Btn(PracticeJobsStrings.T("again"), () => {
                if (!ReferenceEquals(_result, result)) return;
                RemoveResult(); Begin(choice);
            }, "ct-btn ct-btn-primary ct-desk-next");
            again.name = "practice-again";
            var back = El.Btn(PracticeJobsStrings.T("return"), () => {
                if (!ReferenceEquals(_result, result)) return;
                RemoveResult(); if (!_disposed && !_ownerCancellation.IsCancellationRequested) Phone.Open(Id);
            }, "ct-btn ct-btn-ghost ct-desk-next");
            back.name = "practice-return";
            panel.Add(again); panel.Add(back);
            var sheet = Resources.Load<StyleSheet>("UI/mod-desk");
            if (sheet != null) result.styleSheets.Add(sheet);
            result.RegisterCallback<GeometryChangedEvent>(e => result.EnableInClassList("ct-desk-landscape", e.newRect.width > e.newRect.height));
            result.RegisterCallback<DetachFromPanelEvent>(e => { if (ReferenceEquals(e.target, result) && ReferenceEquals(_result, result)) _result = null; });
            _result = result;
            UiRoot.Instance.ModalLayer.Add(result);
        }

        private void RemoveResult() { var result = _result; _result = null; result?.RemoveFromHierarchy(); }
        private static Label Copy(string key, string classes) => Plain(PracticeJobsStrings.T(key), classes);
        private static Label Plain(string text, string classes) { var label = El.Text(text, classes); label.enableRichText = false; return label; }
        private static void TryCancel(CancellationTokenSource source) { try { source?.Cancel(); } catch (ObjectDisposedException) { } }
        private void ReportFailure(Exception error, string phase)
        {
            LastFailure = phase + ": " + error.GetType().Name;
            // Preserve a bounded diagnostic without turning an unavailable practice view into a red crash overlay.
            Log.Info("practice-jobs", LastFailure);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            TryCancel(_attempt);
            RemoveGame(); RemoveResult();
            _attempt?.Dispose(); // finally also disposes; CTS disposal is idempotent
            _attempt = null;
            _page = null;
        }
    }
}
#endif
