using System;
using System.Collections.Generic;

namespace CTLife.UI
{
    /// <summary>
    /// The creator's steps, one decision each (spec C2). Look is the Character (base model) pick; Customize edits it with the
    /// category rail and is skipped when the model offers nothing to customize. The lottery is step 1 of 7 on the progress
    /// bar; Look and Customize share one segment.
    /// </summary>
    public enum CreatorStep { Look = 0, Customize = 1, Tribe = 2, Traits = 3, Dream = 4, Name = 5, Confirm = 6 }

    public enum AliasState { Empty, TooShort, TooLong, Ok }

    /// <summary>
    /// The creator's step state machine and validation, with no UI in it (EditMode tests: OnboardingUiTests).
    /// Validation is the creator's existing rules, unchanged: a complete look, one tribe, exactly two traits (a third pick
    /// replaces the oldest), one dream, an alias of at least 3 characters after trimming (the field caps it at 20), the 18+
    /// confirmation, and a look the server can accept (or a read-only review, which never submits).
    /// Problems are Strings keys, so the hint under the CTA says why it is disabled.
    /// </summary>
    public sealed class CreatorFlow
    {
        public const int MinAlias = 3, MaxAlias = 20;
        public const int StepCount = 7;
        /// <summary>Segments on the progress bar: the lottery plus the six creator steps.</summary>
        public const int ProgressSegments = 7;

        public readonly CharacterDraft Draft;
        public CreatorStep Step { get; private set; }
        /// <summary>The furthest step reached: summary chips can jump back to any step up to here.</summary>
        public CreatorStep Furthest { get; private set; }
        public bool HasLook;
        public bool LookSubmittable;
        /// <summary>True when the current base model has at least one customizer category (else Customize is skipped).</summary>
        public bool CustomizeAvailable;
        public bool ReadOnlyReview;

        public CreatorFlow(CharacterDraft draft) => Draft = draft ?? new CharacterDraft();

        public static AliasState CheckAlias(string raw)
        {
            var a = (raw ?? "").Trim();
            if (a.Length == 0) return AliasState.Empty;
            if (a.Length < MinAlias) return AliasState.TooShort;
            return a.Length > MaxAlias ? AliasState.TooLong : AliasState.Ok;
        }

        /// <summary>Why `step` cannot be left forward, as a Strings key; null when it can.</summary>
        public string Problem(CreatorStep step)
        {
            switch (step)
            {
                case CreatorStep.Look: return HasLook ? null : "creator.needLook";
                case CreatorStep.Customize: return null;
                case CreatorStep.Tribe: return string.IsNullOrEmpty(Draft.tribeId) ? "ob.hint.tribe" : null;
                case CreatorStep.Traits:
                    return Draft.traitIds.Count == 2 ? null : Draft.traitIds.Count == 1 ? "ob.hint.traits1" : "ob.hint.traits2";
                case CreatorStep.Dream: return string.IsNullOrEmpty(Draft.dreamId) ? "ob.hint.dream" : null;
                case CreatorStep.Name: return CheckAlias(Draft.alias) == AliasState.Ok ? null : "ob.hint.name";
                default:
                    for (var s = CreatorStep.Look; s < CreatorStep.Confirm; s++)
                        if (Problem(s) is string earlier) return earlier;
                    if (!Draft.ageConfirmed) return "ob.hint.age";
                    return !ReadOnlyReview && !LookSubmittable ? "creator.lookNotAvailable" : null;
            }
        }

        public string CurrentProblem => Problem(Step);
        public bool CanAdvance => CurrentProblem == null;

        /// <summary>The first step with a problem (where the summary sends the player), or Confirm when all are answered.</summary>
        public CreatorStep FirstUnanswered()
        {
            for (var s = CreatorStep.Look; s < CreatorStep.Confirm; s++)
                if (Problem(s) != null) return s;
            return CreatorStep.Confirm;
        }

        /// <summary>Moves forward one step when the current one is answered. Never past Confirm (Confirm submits).</summary>
        public bool Next()
        {
            if (Step == CreatorStep.Confirm || !CanAdvance) return false;
            Step++;
            if (Step == CreatorStep.Customize && !CustomizeAvailable) Step++;
            if (Step > Furthest) Furthest = Step;
            return true;
        }

        public bool Back()
        {
            if (Step == CreatorStep.Look) return false;
            Step--;
            if (Step == CreatorStep.Customize && !CustomizeAvailable) Step--;
            return true;
        }

        /// <summary>Jump to a step already reached (summary chips). Selections are kept.</summary>
        public bool JumpTo(CreatorStep step)
        {
            if (step > Furthest || step < CreatorStep.Look) return false;
            if (step == CreatorStep.Customize && !CustomizeAvailable) return false;
            Step = step;
            return true;
        }

        /// <summary>Restore after a pause/relaunch: clamps to the first unanswered step so a restored draft is never ahead of itself.</summary>
        public void Restore(CreatorStep step, CreatorStep furthest)
        {
            var limit = FirstUnanswered();
            Furthest = furthest > limit ? limit : furthest;
            if (step > Furthest) step = Furthest;
            Step = step < CreatorStep.Look ? CreatorStep.Look : step;
            if (Step == CreatorStep.Customize && !CustomizeAvailable) Step = CreatorStep.Look;
            if (Step > Furthest) Furthest = Step;
        }

        public void SelectTribe(string id) => Draft.tribeId = id;
        public void SelectDream(string id) => Draft.dreamId = id;
        public void SetAlias(string raw) => Draft.alias = (raw ?? "").Trim();
        public void SetAge(bool confirmed) => Draft.ageConfirmed = confirmed;

        /// <summary>
        /// Tap on a trait: deselects it if picked; otherwise picks it, and when two are already picked the oldest is
        /// removed (existing behaviour). Returns the id pushed out, or null.
        /// </summary>
        public string ToggleTrait(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Draft.traitIds.Remove(id)) return null;
            string removed = null;
            if (Draft.traitIds.Count >= 2)
            {
                removed = Draft.traitIds[0];
                Draft.traitIds.RemoveAt(0);
            }
            Draft.traitIds.Add(id);
            return removed;
        }

        /// <summary>Completed selections before `step`; Look and Customize share one, and the lottery comes last.</summary>
        public static int ProgressFor(CreatorStep step) => step <= CreatorStep.Customize ? 0 : (int)step - 1;

        /// <summary>Drops selections that are not in the current registries (a restored draft from an older config).</summary>
        public void Sanitize(ICollection<string> tribes, ICollection<string> traits, ICollection<string> dreams)
        {
            if (Draft.tribeId != null && tribes != null && !tribes.Contains(Draft.tribeId)) Draft.tribeId = null;
            if (traits != null) Draft.traitIds.RemoveAll(t => !traits.Contains(t));
            while (Draft.traitIds.Count > 2) Draft.traitIds.RemoveAt(0);
            if (Draft.dreamId != null && dreams != null && !dreams.Contains(Draft.dreamId)) Draft.dreamId = null;
            if (Draft.alias != null && Draft.alias.Length > MaxAlias) Draft.alias = Draft.alias.Substring(0, MaxAlias);
        }
    }
}
