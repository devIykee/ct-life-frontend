READY TO COMMIT (branch feat/home-ui at dev 86dcad7, worktree ct-life-feel), exactly these paths:
`client/Assets/Scripts/UI/Screens/DashboardScreen.cs`, `client/Assets/Scripts/UI/Screens/Home/` (+ `client/Assets/Scripts/UI/Screens/Home.meta`),
`client/Assets/Scripts/UI/Kit/IconBadge.cs` (+ .meta), `client/Assets/Scripts/UI/Kit/StatChip.cs` (+ .meta),
`client/Assets/Scripts/UI/Screens/Onboarding/CreatorLookOrder.cs` (+ .meta), `client/Assets/Scripts/UI/Screens/CharacterCreatorScreen.cs`,
`client/Assets/Scripts/UI/Screens/CreatorLookCatalog.cs`, `client/Assets/Resources/UI/CreatorLookCatalog.asset`,
`client/Assets/Scripts/UI/Text/Strings.cs`, `client/Assets/Scripts/App/FlowController.cs`,
`client/Assets/Scripts/Editor/OnboardingCapture.cs`, `client/Assets/Scripts/Editor/OnboardingCaptureHome.cs` (+ .meta),
`client/Assets/Scripts/Editor/CastImport.cs`, `client/Assets/Scripts/Editor/LobbyArtworkImport.cs`, `client/Assets/Scripts/Editor/SizeBudget.cs`,
`client/Assets/Tests/EditMode/HomeUiTests.cs` (+ .meta), `client/Assets/Resources/UI/home.uss` (+ .meta), `client/Assets/Resources/UI/kit.uss`,
`client/Assets/Resources/UI/ctlife.uss`, `client/Assets/Resources/UI/landscape.uss`,
the deletions `client/Assets/Resources/UI/Art/CityDiorama.png`, `client/Assets/Resources/UI/Art/CityDiorama.png.meta` and
`client/Assets/Resources/UI/Art.meta` (already staged with `git rm`), `docs/progress/home-ui.md`, `docs/progress/backend-dev-feedback.md` (item 94),
`docs/progress/requests.md` (the 2026-10-09 cast request at the end)
— the home/lobby redesign, plus the look-diversity fix in the creator (interleaved by skin tone and body family, seeded per
install), and the cast looks now facing the camera.
Do NOT commit `client/Assets/Art/Candidates/**/Materials/*.mat`. That is Unity's URP AssetVersion churn from my batch runs; I
reverted it.

# Home / lobby redesign (2026-10-09)

Owner: onboarding/home UI lead. Spec: `docs/research/duolingo-design-teardown.md` (clarity, one dominant action). Identity:
the title and onboarding screens. Evidence: `../ct-life-evidence/home-ui/` (15 captures at 2670×1200, `receipt.json`,
`editmode-results.xml`). No device run from this lane.

## What changed on screen
The old flat navy dashboard is gone: the "YOUR NEXT CHAPTER / MAKE YOUR NEXT MOVE." heading, the 2D diorama and the pastel
tiles. `DashboardScreen` is rebuilt in place (same class, constructor and `SetBusy`), so every FlowController route
still works.
- **The character.** The player's own 3D character (their `/me` look) stands centre-left on the blurred title painting.
  It has a soft spotlight and a pill shadow, and the painting does a slow Ken Burns drift. Drag to turn; a tap makes it react
  (cycled emote and line). On arrival it reacts to state, most urgent first:
  - low Energy: "I'm cooked. Need sleep.";
  - low Sanity: "Too much timeline. Need some grass.";
  - first arrival: gm wave, "gm! First day. Let's go.";
  - market up or down by 3% or more: "We're so back." / "It's so over. Or is it?";
  - otherwise "gm. Missed me?".
- **The top bar.** Status chips for CASH, **TO PAY BACK** (only when `Me.debtCents > 0`), ENERGY and SANITY, the last two
  with mini bars. Low and critical needs go amber or red and get a "!" pip, so state is not shown by colour alone. Thresholds
  are `needs.warnings` from the server if served, else the HUD's 25 / 10. Each chip explains itself in a popover with a
  route:
  - Cash: game money, can't be withdrawn → OPEN BANK.
  - Debt: "$720 left to pay back. $120 is due each week." (weekly from `lottery.json debtRepayment.weeklyDueCents`) → OPEN BANK.
  - Energy: "Sleep to refill it." → OPEN HOME.
  - Sanity: "Touch grass to get it back." → OPEN HOME (the needs actions from `needs.json`).
- **Settings** is a round cream button in the top-right corner.
- **Greeting.** The tribe's painted badge and name, then "gm, <alias>." with one line that reacts to Market Mood (the city
  room's bps, as the HUD shows it): "Market +4.20% today. We're so back." On a first arrival it reads "Welcome to CT,
  <alias>." / "First move: step into the city."
- **Today row**, showing only what is real:
  - a MARKET chip (tap: what Market Mood is), shown once the city room has sent a mood;
  - TODAY'S CLUE from `GET /daily-clue` through the City Life API (tap: opens the `week` app), shown only when the server
    returns a clue;
  - an OFFLINE · Reconnecting… chip when the network is down, the city room is not joined, or `/me` is missing.
- **The Dream card** (tap: Me) shows the registry name ("Make $10K"). It shows a progress bar **only when the server sends
  progress**. The contract has none, so I proposed `flags.dreamProgress {current, target, unit}` (backend feedback #94) and
  the client reads it when present; no bar today. **Home card** (tap: the Home app).
- **ENTER THE CITY**: the big green squircle (176 units tall, the only green on the screen), "OPENING THE CITY…" while
  travelling. A failed entry shows an inline line to its left ("Couldn't reach the city. Check your signal and try
  again."), the character facepalms "Signal's cooked. Try again?", and the toast still fires.
- **Tiles**: Explore, Jobs, Bank, Me, Phone. Chunky round medallions built like the painted tribe badges (plum rim, coloured
  field, bevel highlight, cream glyph with a plum drop), drawn in USS so they cost no texture.
- Routes kept: enter city, map, jobs, bank, profile, phone (null = phone home), settings, home (Home card + the Energy and
  Sanity explainers), bank (Cash + debt explainers). Profile is also the Dream card's tap, as before. Busy state disables
  everything as before.

## Look diversity (coordinator item, user feedback)
- **Creator order.** `CreatorLookOrder` interleaves the 24 creator looks by skin-tone band (light: fair/light; medium:
  olive/tan/warm; deep: brown/dark/deep/tone3) and body family (m/f). It starts from a band, family and look chosen by a
  per-install seed (PlayerPrefs), so look #1 is no longer always Snow/Rain (both tone3).
  - Seeds 0/1/2 open on Blazer (light), Trencher f (warm), Breeze (deep): captures 20–22.
  - Presentation only: the authored catalog order is unchanged, because `FallbackLook` must give a remote player the same
    look on every device.
- **Lagos looks.** Lagos is no longer named in what players see: "Kaftan & Fila", "Ankara & Gele", "Ankara Kaftan" (they
  stay available among the others). Every cast look got a short player-facing name instead of an authoring note ("Trencher
  (hoodie, joggers, headset)" → "Trencher"; "Blazer + Checkpoint lanyard" → "Blazer"). The onboarding/home copy had no
  Lagos-first content.
- **Bug found and fixed: every cast look showed its back.** `CastImport` wrote `previewYaw = 180` for the 22 cast looks;
  Humanoid faces +Z with the camera in front, so 0 is the face (as Snow/Rain/Vincent already use). Fixed in the asset and the
  importer.
- **NPCs.** Tones are balanced by label: 51 looks, fair 6, light 7, olive 6, tan 8, warm 7, brown 7, dark 5, deep 4. Body
  families are not (34 m / 17 f; the first venue's five NPCs are 4 m / 1 f). Request filed in `requests.md` (cast /
  world-life lane).
- **The root cause, also filed: the cast skin palette has no light skin.** In `cast_tex.py`, "fair" is #c08b69,
  luminance 0.31; for comparison a typical light-skin swatch is about 0.63 [Inference: a reference swatch, not a standard].
  So every character renders medium to deep, whatever the order. Reordering cannot fix that; the palette needs re-spreading
  in the next cast export (after the "cool CT hero" direction is approved). No characters were built.

## Code
- `Screens/Home/HomeModel.cs`: the pure rules (chips, levels, debt + weekly due, mood line, reaction priority, dream
  progress parse).
- `DashboardScreen.cs`: the screen.
- Kit additions: `IconBadge`, `IconTile`, `StatChip`, `Popover`.
- Styles: `home.uss`, plus a kit.uss block.
- FlowController:
  - `firstArrival` is passed after onboarding ran this session;
  - the daily clue provider (`CityLifeHub.Acquire()` → `Api.DailyClue`, released after);
  - `lobby.ShowError(...)` beside the existing toasts for blocked or rejected or failed travel and a failed Private Home
    entry.
- Dead code removed: the old lobby USS (about 80 rule lines across ctlife.uss and landscape.uss) and the 11 unused `lobby.*` strings.
- **CityDiorama removed from Resources.** A byte-identical copy stays in `website/assets/city-diorama.png`.
  `LobbyArtworkImport` and `SizeBudget` tolerate its absence.

## APK
**No new textures.** The home reuses `onboarding-bg`, the tribe badges and `soft-glow`, and the badges and tiles are USS.
Removing CityDiorama (Android 1024² ASTC 4x4 = 1,048,576 bytes, per its .meta) frees about **1.0 MiB**. Additions are a USS
file and code, a few tens of KB. **Estimate [not built]: release ≈ 147.3 → ~146.3 MiB** of the 150 MiB budget.

## Verification
- `compile_check.py`, all three passes (dev, `--editor`, `--release`): **0 errors**. The one editor warning is pre-existing
  (`VenueVerify.cs` CS0219).
- EditMode `CTLife.Tests.EditMode`: **146/146 passed**, including 13 new `HomeUiTests`. They cover:
  - the debt chip only when owed, with the weekly line from config;
  - routes kept, need levels and thresholds, no-profile;
  - mood line keys, reaction priority, registry names;
  - dream progress only from server flags (cents and count);
  - look order: a permutation, no shared band among neighbours while all bands have looks, mostly alternating families,
    no Lagos run at the start, the first look's band and family varying with the seed;
  - StatChip state beyond colour, IconTile and Popover, all home copy present.
  Results: `../ct-life-evidence/home-ui/editmode-results.xml`.
- Editor captures:
  `Unity -batchmode -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.OnboardingCapture.Launch -ctlife-captureSet home`.
  The receipt says **PASS** with in-run checks (first-arrival greeting, no debt chip without debt, mood line, $720 chip,
  low-energy reaction).
  - 01 first arrival;
  - 02 returning, market up + today's clue; 03 character tap;
  - 04 with debt, market down; 05 debt explainer;
  - 06 no debt; 07 cash explainer;
  - 08 low energy (critical) + low sanity; 09 energy explainer;
  - 10 entering (busy); 11 network error (offline + failed ENTER THE CITY);
  - 12 no profile (/me missing);
  - 20–22 the creator's first look for install seeds 0/1/2.

## Open items
**For the Seeker:**
- Thermal and FPS with the preview camera live on the lobby; it also renders when the lobby opens over a venue from the HUD.
- The popover's pointer tail did not show in the captures (the card itself does). Cosmetic, not chased.
- `GET /daily-clue` live status on the backend [Unverified]; the chip simply stays hidden when the route fails.

**Other open items:**
- Backend #94: dream progress.
- Cast lane: re-spread the skin palette and balance NPC body families (see `requests.md`).
- The onboarding captures in `../ct-life-evidence/onboarding-ui/` still show the old look order and back-facing cast
  looks. Re-run the onboarding set (`-ctlife-captureSet onboarding`) when convenient.
- "Today" does not show the week (Sunday Unlock countdown) yet: `WeekClock` lives in the City Life lane, and it was left out
  to keep the row to two items.
