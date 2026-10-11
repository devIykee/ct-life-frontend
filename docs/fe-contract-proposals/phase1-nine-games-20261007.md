# Phase 1: nine distinct shift games

[Proposal] Six additions to the three imported games. These IDs and rules are a versioned frontend proposal, not an imported registry change or available paid assignment. Existing games, career IDs, payouts and all later phases remain unchanged.

## Current blocker

The build spec, Phase 1 item 9, calls for nine mini-games. Imported `contract/ids/mini_games.json` contains three concrete Phase 1 IDs: `discord_mod_ban`, `junior_analyst_earnings`, and `kols_engagement`. The client routes these to Mod Desk, Earnings Desk and Thread Workshop. The generic later-career tapper does not fulfill the remaining six games.

Read-only backend inspection on 7 October: `server/application/shifts.ts` selects `career.levels[0].miniGameId` and returns a one-element assignment. Adding six local games does not make the server select them. The backend owner must agree to the IDs, duration/caps, and assignment selection before paid routing. No endpoint, payout, shift duration or imported config is changed by this proposal. The current completion validation also needs its separately tracked hardening; local scores are not proof of secure server settlement.

## Rules proposed for v1

IDs are immutable once agreed. A materially different scoring/ruleset should receive a new ID rather than silently changing an accepted cap. All fictional situations and data below are authored game content, not real posts, prices, users or project claims.

| Career | Proposed ID | Duration / maximum | Exact local mechanic and scoring |
| --- | --- | --- | --- |
| Discord Mod | `discord_mod_raid_router` | 45 s / 100 | A single shield moves among three chat lanes or a parking bay. Twenty-four visible packets arrive in three escalating waves. Stop RAID packets while allowing CHAT packets through. Twelve packets are raids. Score is `round(max(0, raidsStopped − friendlyChatsBlocked) × 100 / 12)`; idle play scores zero and mistakes cannot be retried. Arrival spacing is at least 1.2 s, with 4 s of visible approach. Combo and last-moment saves are feedback, not hidden multipliers. |
| Discord Mod | `discord_mod_role_lock` | 50 s ceiling / 100 | Five role panels, each with a 3-role × 3-permission toggle grid and an explicit least-privilege brief. Required and forbidden cells are specified by role tasks. Lock a panel once; score 20 only for its exact valid grid, otherwise 0. No guessed social intent. Changes before locking are reversible; remaining panels expire at the deadline. |
| Junior Analyst | `junior_analyst_receipt_match` | 50 s ceiling / 100 | Five reconciliation boards show four bank entries and four receipt cards. Pair each entry to one unused receipt using amount plus reference/date; amounts alone deliberately collide. One submission per board. Each correct pair earns 5 points; no resubmission or negative score. Card assignments can be swapped before filing. Every board has exactly one matching solution. |
| Junior Analyst | `junior_analyst_runway_plan` | 60 s ceiling / 100 | Four budget boards offer discrete cuts to five fictional expense lines. Meet an explicit monthly cash ceiling while preserving two minimum service constraints. Spend a fixed maximum of three edits, with Undo restoring the edit budget before filing. Score 25 per valid plan, 0 otherwise. Multiple valid allocations are accepted; no predicted market returns or real financial advice. |
| KOL Engagement | `kol_engagement_clip_cut` | 50 s ceiling / 100 | Five original storyboard reels contain eight ordered beats. Move in/out handles to retain the labelled setup and payoff while excluding marked dead air or an unrelated interruption. The source strip stays visible, with an in-game preview. One cut per reel; 20 for the exact concise interval, 0 otherwise. No third-party media, recording, external posting or engagement claim. |
| KOL Engagement | `kol_engagement_live_cues` | 45 s / 100 | Run a fictional show with 20 announced cues. Upcoming cue and timing window are visible; choose camera, caption or applause as the marker reaches its window. Each correct action in its declared 800 ms window earns 5 points. Each cue accepts one action; wrong/late actions consume that cue and cannot score twice. No mic, real audience or social engagement metric. |

### Local board-game completion — clarified 7 October

Role Lock and Receipt Match end immediately after their fifth Lock/File, or when the 50-second active-time ceiling expires. Finishing all work does not leave an empty clock to wait through. Pausing local practice excludes that time; cancellation returns no result. This product clarification does not authorize new paid assignments or change a server shift deadline. Score formulas and caps above remain unchanged. Runway Plan likewise completes after its fourth File or a 60-second ceiling, and Clip Cut after its fifth Cut or a 50-second ceiling. Continuous Raid Router and Live Cues retain their 45-active-second shows.

## First implementation: Raid Router

[Proposal] Build the continuous arcade activity first. It introduces movement prediction and timing alongside the existing reading, arithmetic and writing games. The player's action is moving one shared shield; repeatedly tapping every lane is not a valid shortcut because a shield also blocks friendly messages. Chat identity is written and shaped, not communicated by colour alone. All packets and lane names are fictional.

The prototype is practice-only. Pausing or losing application focus freezes practice time and shows an explicit Resume action. It still requires 45 active seconds per completed attempt. This pause behavior is **not** a proposal to extend a paid server shift or bypass `endsAt`; paid activation needs an explicit product/contract decision. A cancelled/detached attempt returns no score. Replay starts a fresh seed, while deterministic review seeds remain compile guarded.

Use the current Manrope, CT palette and desk tokens; no new font/package. Actual native bounds, motion and physical Seeker touch acceptance remain required after source staging. No research establishes that these mechanics will retain players; voluntary retries, completion, mistakes and qualitative feedback should be observed in playtests.

## Sources and implementation inputs

- [Build spec](../spec/build-spec.md), core loop and Phase 1 item 9; [imported mini-game IDs](../../contract/ids/mini_games.json); [career config](../../contract/config/careers.json).
- [Primary CT observations](../research/ct-social-game-direction-20261006.md): everyday problems and confident reversals informed the original fictional tone; no real person's line or likeness is reused.
- [Unity 6.3 pointer events](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-Pointer-Events.html), [event handling](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-Events-Handling.html), and [GeometryChangedEvent](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.GeometryChangedEvent.html), opened 7 October. Button callbacks and layout changes are native UI Toolkit boundaries; source tests alone do not certify mobile touch or layout.
