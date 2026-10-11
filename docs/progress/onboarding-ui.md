READY TO COMMIT (branch feat/onboarding-ui, worktree ct-life-feel), exactly these paths:
`client/Assets/Scripts/UI/Kit/` (+ `client/Assets/Scripts/UI/Kit.meta`), `client/Assets/Scripts/UI/Screens/Onboarding/`
(+ `client/Assets/Scripts/UI/Screens/Onboarding.meta`), `client/Assets/Scripts/UI/Screens/LotteryScreen.cs`,
`client/Assets/Scripts/UI/Screens/CharacterCreatorScreen.cs`, `client/Assets/Scripts/UI/Screens/AvatarPreview.cs`,
`client/Assets/Scripts/UI/DeviceFeel.cs`, `client/Assets/Scripts/UI/Text/Strings.cs`, `client/Assets/Scripts/App/FlowController.cs`,
`client/Assets/Scripts/Editor/OnboardingArtImporter.cs` (+ .meta), `client/Assets/Scripts/Editor/OnboardingCapture.cs` (+ .meta),
`client/Assets/Tests/EditMode/OnboardingUiTests.cs` (+ .meta), `client/Assets/Resources/UI/kit.uss` (+ .meta),
`client/Assets/Resources/UI/onboarding.uss` (+ .meta), `client/Assets/Resources/UI/Kit/` (+ `client/Assets/Resources/UI/Kit.meta`),
`client/Assets/Resources/UI/Onboarding/` (+ `client/Assets/Resources/UI/Onboarding.meta`), `client/Assets/Resources/UI/ctlife.uss`,
`client/Assets/Resources/UI/landscape.uss`, `art/LICENSES.md`, `docs/progress/onboarding-ui.md`
— first-time flow redesign: origin-story lottery and a one-decision-per-step character creator on the approved painted
identity, a reusable UI kit, draft persistence across pause/relaunch, a keyboard-safe name step, the 18+ toggle card on
Confirm, the celebration, 22 new EditMode tests and the editor capture tool.
Do NOT commit any `client/Assets/Art/Candidates/**/Materials/*.mat` change: Unity's URP AssetVersion upgrade rewrote 81 of
them during my batch runs in this worktree. I reverted them (`git checkout -- client/Assets/Art/Candidates`); if they
reappear after another editor run, revert them again.

# Onboarding UI: origin lottery and character creator (2026-10-08)

Owner: onboarding UI lead. Spec: `docs/research/duolingo-design-teardown.md`, with the correction that there is no shiba guide.
Evidence: `../ct-life-evidence/onboarding-ui/` (32 captures at 2670×1200, two frame strips, `receipt.json`,
`editmode-results.xml`). I did no device run from this lane. The integration lead verifies on the Seeker (checklist below).

## What it looks like now

The pale web-form look is gone. Both screens sit on the title painting, pre-blurred and dimmed at import, so nothing blurs
at runtime. They use the approved squircle button bodies (9-sliced exactly like the title) and Lilita One cream text with a
thick plum outline. There are no translucent plates behind text. Every screen has the same frame: a round back button and a
7-segment progress bar at the top (the lottery is step 1), the character or cards on the left, one question on the right,
and the CTA always at the bottom right. While the CTA is disabled, a short hint above it says why. Tapping the disabled CTA
shakes the hint, with an error blip and a reject haptic.

### Lottery ("Your origin story")
- Two face-down painted cards bob gently. Each shows "20% chance" / "80% chance", read from the served `lottery.json`
  weights. The odds are never hard-coded. They are hidden when any shown card has weight 0 (rc1 seeker = 0), so a Seeker
  holder sees three cards and no odds. The CTA reads **DISCOVER MY BEGINNING**, as the brief says (the spec suggested "DRAW").
- On tap the CTA hides and the odds step out. The cards gather and deal out twice (the shuffle). A gold spotlight then hops
  card to card on the original curve (0.07 → 0.35 s, at least 2.2 s, and it waits for the server; "Still rolling. Hang on."
  after 8 s). The last hops land on the winner.
- The reveal: the loser slides away and fades, and the winner moves to the centre and grows to 1.1. It flips: scaleX to an
  edge, darkening, then swaps to the painted face and opens with an overshoot (strip below).
  - Insider and Seeker: a 1.06 pop, 26 gold coins, 60 confetti, the moon horn, a CONFIRM haptic. "Born on the right side of
    the group chat."
  - Exit Liquidity: a squash "bonk", a soft cloud puff and 12 muted pieces falling slowly. `sfx_rug` plays at half volume
    with a REJECT haptic. "Cooked on day one. Not your fault." Then, 1.2 s later, the supportive second line: "Everyone gets
    a comeback arc. Yours starts now."
- The result, on the left: the outcome name, Cash counting up over 600 ms ("$3,000 Cash"), "Home: Studio", and for the debt
  an orange "$720 to pay back" tag. All values come from the roll response. "Cash is game money. It can't be withdrawn." is
  always at the bottom left. The CTA then reads **MAKE MY CHARACTER**.
- The overlap bug from the old screen ("One beginning…" drawn over the card) is gone: copy and cards live in separate zones.
- I chose no guide character. A Town Crier bust would mean a second 3D preview camera and a second voice before the player's
  own character exists. The cards carry the moment.

### Creator (one decision per step; the player's own character is the reactor)
1. **Look.** The character is large, centre-left, standing on the painting with a soft spotlight and a pill shadow (the
   preview is now transparent). Drag to turn; it sways ±8° until the first touch. Big painted green arrows sit either side,
   the look name and "2 of 24" are under the feet, and a FULL BODY / FACE segmented toggle sits on the right. It waves on
   arrival and nods or thumbs-up on each look change.
2. **Tribe.** 7 badge cards (the painted `tribe-<id>.png`, an icon-disc fallback by id), each with the registry perk. The
   picked card turns green with a check badge and a 1.04 pop. The character plays the tribe's emote: the registry
   `emoteId` if the AvatarController has it, else a stand-in such as `emote_send_it`, `emote_coin_flip` or `emote_hood_up`.
   It also says a tribe line, e.g. "Living in the trenches." or "Points are my love language."
3. **Traits (pick 2).** Six cards, each with its **effect** from the registry (device finding 3). The friendly wording
   ("Sanity drains 20% slower") is used only while the server's text equals the rc1 text it was written from. Otherwise the
   server's own text is shown. Two dots fill as you pick. A third pick replaces the oldest, as before; the dropped card
   shakes and the character says "Only two. Swapped one out."
4. **Dream.** 8 registry dreams in a 3×3 grid (the 9th slot sits under the CTA).
5. **Name.** One big field with live validation: "3–20 characters", then "At least 3 characters" in amber, then "Looks
   good" in green with a green rim. The rules are unchanged: trimmed, at least 3, field capped at 20. While typing, the
   **typing layout** applies (device finding 1): the top bar, title and CTA step away, and the field, its status, a DONE
   button and the character's face move into the top third of the screen, above the landscape keyboard. Capture 24 overlays
   the measured keyboard footprint. `hideMobileInput` is now true, so the player types into our field, not the OS line that
   was invisible on the Seeker.
6. **Confirm.** "Meet <alias>", and six summary chips: ORIGIN (from the profile's home → `lottery.json` outcomes), LOOK,
   TRIBE (its badge), TRAITS, DREAM, NAME. Tapping a chip jumps to that step; its CTA then reads DONE and returns straight to
   Confirm. The **18+ confirmation is a proper toggle card with its checkbox and label together** (device finding 4). START
   MY STORY stays disabled ("Confirm you're 18+") until it is ticked. Game-Cash line. Busy: "SAVING…". On an error the
   message shows, the character facepalms ("That didn't land. Try again?"), and the CTA re-enables.
7. **Celebration.** The bar fills to 100% with a flash, the town dims, and the character does a moon jump in a spotlight.
   "Welcome to CT, <alias>!", "gm. The timeline is waiting.", 120 confetti, coins for a good origin, a fanfare
   (`sfx_graduation`) with a CONFIRM haptic plus 3 ticks. Then **ENTER THE CITY**.

### Device findings from the integration lead
1. Blind alias typing: fixed by the typing layout and `hideMobileInput = true` (see Name).
2. Lottery line over the card: fixed by the new layout (captures 04 and 06).
3. Traits had no descriptions: each now shows its registry effect.
4. The 18+ box was far from its label: it is now a toggle card on Confirm.
5. Stiff arms in the face view: the preview now has a resting pose. The face shot plays `idle_fidget_foldarms` (arms
   folded) and is framed tighter: head and shoulders, with the lower bound at 66–76% of the body height instead of 60–72%
   (capture 11).
6. Lost progress on background: the draft is saved per account in PlayerPrefs on every change and flushed on pause. It
   holds the step, furthest step, look, yaw, Face/Full, tribe, traits, dream and alias. The 18+ confirmation is
   deliberately not saved. The draft is restored when the creator is rebuilt, clamped to the first unanswered step, with
   ids sanitised against the current registries. It is cleared when the character is created. Capture 26 shows a rebuilt
   screen resuming on Confirm. I could not reproduce the device root cause without the phone; this fix covers both a
   rebuilt screen and a killed process.
7. The lobby shows $300 Cash but not the $720 to pay back. **Next-screen item, not built** (lobby / Bank owner).

### Server contract: unchanged
- The roll and done callbacks, and the 409 / reconciled-/me path (`roll` returns null → continue), are as before.
- The character payload is unchanged: alias (trimmed), look[7], tribeId, 2 traitIds, dreamId, ageConfirmed. Validation and
  error display (`Strings.Error` via FlowController) are unchanged.
- `CreatorLookPolicy` / `serverLookConfirmed`, the read-only review mode, and the `autocreate` automation are kept.
  Automation now also auto-continues the celebration after 2.5 s.
- The only FlowController change: after a successful `POST /v1/characters` and the profile refresh, it awaits
  `screen.Celebrate()` (ENTER THE CITY) before closing the screen.

## Code
- **UI kit** (`Scripts/UI/Kit/`, namespace `CTLife.UI.Kit`, styles `Resources/UI/kit.uss`):
  - `LippedButton`: the painted squircle (green or purple) or a cream face, on a plum lip, with press travel equal to the
    lip (60 ms in, 140 ms ease-out-back out). Disabled is flat cream, pressed-in, with a muted label. Also: busy label, icon
    with a plum drop, shine sweep, `Press()` for automation.
  - `ChoiceCard`: vertical or horizontal; badge or icon disc or checkbox; states default, selected (check badge, green lip,
    pop), disabled, error (red rim and shake); `Bump()`. Also `ChoiceGrid`.
  - `ProgressBar`: notches, 0.4 s fill, shine, flash.
  - `SpeechBubble`: pop-in, three tail positions.
  - `CelebrationBurst`: a pooled, allocation-free particle system for coins, confetti and puffs.
  - `Motion` / `Tween` / `Ease`: scheduler-driven tweens; nothing allocates per tick; `TimeScale` for captures.
  - `Cues`: the single SFX and haptic map.
- **Onboarding logic** (`Scripts/UI/Screens/Onboarding/`):
  - `CreatorFlow`: the pure step machine and validation.
  - `CreatorDraftStore`: persistence.
  - `LotteryModel` / `OriginSummary` / `OnboardingCopy`: odds, origin, cash text, trait wording, icons, emotes, tribe lines.
  - `OnboardingArt`: loaders that return null when a file is missing.
  - `OnboardingFrame`: the shared chrome.
- `AvatarPreview`: optional transparent clear (and drops the round blob shadow), `SetIdle`, `PlayReaction` (only states the
  controller has), tighter face framing, exposes `Yaw`. CityShopApp's use is unchanged (opaque by default).
- `DeviceFeel`: `Confirm()`, `Reject()` and `ClockTick()` haptics, plus a reduced-motion override for captures and tests.
  The constants come from AOSP `HapticFeedbackConstants.java`, read 2026-10-08: VIRTUAL_KEY 1, CLOCK_TICK 4, CONFIRM 16,
  REJECT 17.
- **Copy**: the `ob.*` block in `Strings.cs`. Short, second person, live terms only (trenches, cooked, CA, max long, points,
  gm, "we're so back"), parody-safe, with no brands or handles. I removed 51 dead `creator.*` / `lottery.*` keys and the
  dead `ct-creator*` / `ct-lottery*` USS rules.
- **Accessibility**: every target is at least 130 units (48 dp on the Seeker: back 130, CTA 150, arrows 150, cards 182–370
  tall). No text is under 38 units (14 dp). Contrast comes from the plum outline, not plates. Cards show state with a check
  badge, a lip and a pop, not colour alone. Under reduced motion there is no shuffle, hop, sway, particles or shine, and
  transitions become 120–200 ms fades (capture 09).
- **Performance**: the tickers (idle bob, hops, tweens, particles, shine, sway) assign struct style values only. Cash
  count-up labels are built once. The preview RT is now 720×1080 (was 1080×1620), but it renders on every creator step
  because the character is the reactor.

## Art and APK
The art is copied from `../ct-life-evidence/art-direction/out/ui/` (sha1-identical to the sources at copy time). Import
settings live in `Editor/OnboardingArtImporter.cs`, which runs on every import. Android ASTC sizes as imported, from
`receipt.json`:
| Asset | Android | Format | Bytes |
|---|---|---|---|
| origin-card-back / insider / exit_liquidity (640×880 each) | 640×880 | ASTC 6x6 | 3 × 251,664 |
| tribe-<id> × 7 (512² source) | 256×256 | ASTC 5x5 | 7 × 43,264 |
| onboarding-bg (blurred title painting) | 1280×576 | ASTC 8x8 | 184,320 |
| Kit/soft-glow (128², white radial alpha) | 128×128 | ASTC 4x4 | 16,384 [computed, not in the receipt] |
| **Total** | | | **≈ 1.26 MB (1.20 MiB)** |
**APK estimate [Estimate, not built]:** about +1.3 MiB (textures plus USS and code), so roughly 146 → 147.3 MiB of the
150 MiB budget. The old screens shipped no art of their own, so nothing is freed.
- onboarding-bg is generated by `OnboardingArtImporter.BuildBackdrop`: 3× area downsample, three-pass box blur (σ ≈ 7 px),
  16% darker, 8% desaturated. Menu: CT Life → Onboarding → Build blurred backdrop.
- soft-glow.png was written by a 15-line Python zlib script (white RGBA, alpha = smoothstep(1 − r)). A glow texture made in
  code turned out not to be the problem; it was only too faint. The baked file stays, so puffs can use it from USS.
- There is no Seeker card face yet: the lottery shows a plain plum card with "Seeker Baby" (wired by id:
  `origin-seeker_baby.png`).

## Verification
- `compile_check.py`, all three passes (dev player, `--editor`, `--release`): **0 errors**. The one editor warning is
  pre-existing (`VenueVerify.cs` CS0219).
- EditMode, `CTLife.Tests.EditMode`: **124/124 passed**, including the 22 new `OnboardingUiTests`. They cover:
  - step gating, the trait cap and swap, alias rules, the Confirm 18+ / look gate, back and jump, progress;
  - restore clamping and sanitise; the draft round-trip without 18+, and bad or old drafts;
  - odds only from served weights; outcome and home mapping; whole-dollar Cash;
  - trait wording fallback, tribe copy fallback, every onboarding key present, art by id with nulls when missing;
  - card and button states, the progress bar, the reduced-motion override.
  Results: `../ct-life-evidence/onboarding-ui/editmode-results.xml`.
- Editor PlayMode captures:
  `Unity -batchmode -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.OnboardingCapture.Launch`.
  It runs the real UiRoot, PanelSettings and stylesheets, renders into a 2670×1200 target, and fakes the roll and submit
  delegates. It also checks flow facts in-run (gating, swap, resume, 18+ re-ask, draft cleared after creation). The
  receipt says **PASS**, 0 failures, 32 shots.

Captures (`../ct-life-evidence/onboarding-ui/`):
- **Lottery** (01–09):
  - 01 idle (odds); 02 shuffle; 03 drawing (gold spotlight on the hot card); 04 insider result.
  - 05 exit reveal (puff + muted shower); 06 exit result (debt tag + second line).
  - 07 network error (message + TRY AGAIN); 08 Seeker holder (3 cards, no odds); 09 reduced-motion result.
- **Creator** (10–31):
  - 10 look; 11 look-face; 12 look-next.
  - 13 tribe-empty (disabled CTA + hint); 14 tribe-selected.
  - 15 traits-empty; 16 traits-one; 17 traits-two; 18 traits-swap.
  - 19 dream-empty; 20 dream-selected.
  - 21 name-empty; 22 name-too-short; 23 name-valid; 24 name-typing-keyboard (keyboard footprint overlaid).
  - 25 confirm (18+ unticked); 26 resumed-after-relaunch; 27 confirm-ready; 28 confirm-cta-pressed.
  - 29 confirm-saving; 30 confirm-error; 31 celebration.
- **Kit** (32): button default, pressed, disabled, busy, purple, cream, square; card default, selected, disabled, error,
  checkbox; bubble; progress; disabled CTA with hint.
- **Strips**: `strip-flip-insider.png` (40 frames at 0.3× speed: losers leave, winner centres, flip through the edge,
  face opens, coins start) and `strip-celebration.png` (14 frames: bar flash, moon jump, coins, confetti, ENTER THE CITY
  slides in). The raw frames are in `strip-*/`.

## Open items
**For the Seeker** (integration lead):
- (a) The name typing layout with the real keyboard. Unity reports no keyboard area on Android (docs: `TouchScreenKeyboard.area`
  is a zero Rect), so the layout assumes the measured 65% footprint. `TouchScreenKeyboard.visible` polling ends the layout
  when the keyboard closes [Unverified on device]. `hideMobileInput = true` [Unverified on device].
- (b) The haptic feel of CONFIRM, REJECT and CLOCK_TICK.
- (c) Thermal and FPS with the preview camera live on all creator steps.
- (d) Background then resume on each step.
- (e) The cutout side inset with the full-bleed background.
- (f) The audio mix of `sfx_rug` at 0.5 and `sfx_graduation`.

**Other open items:**
- Lobby debt display (device finding 7) is not built. Next-screen item.
- [Unverified] that the server rolls with the same `lottery.json` weights it serves. The odds shown are the config's.
- No Seeker card face art; the fallback is in place.
- Dreams beyond 8 would collide with the CTA slot (3×3 grid); the registry has 8 today.
- No "Roll a name" suggestions: they need a vetted word list with no real handles. No hardware Back key on the creator steps.
- The celebration waits for ENTER THE CITY; it does not auto-advance. Automation continues it.
- `sfx_rug` cannot be cut at 800 ms through `SfxSignal` (the spec asked for that); it plays at half volume instead.
- The Look step ships with a small "PREVIEW ONLY" tag on development builds when the server has not confirmed a look; it is
  never shown in release.
