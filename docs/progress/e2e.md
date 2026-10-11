# End-to-end tests (e2e)

One command checks that the whole app works against the real backend, on the editor and on the Seeker:

```bash
tools/backend-local/up.sh                 # if the local backend is not running yet (the runner also starts it)
tools/integration/run_e2e.sh [--build]    # --build: build the dev APK from the warm clone and install it first
```

Output: `../ct-life-evidence/e2e/<stamp>/SUMMARY.md` (every check and feature row) plus the receipts, logs and
screenshots beside it. Exit 0 only when every check passes. Close the Unity editor on this worktree first (the editor
run needs the project), and keep the Seeker on USB with the dev APK installed.

## What it checks

**1. Editor integration** (`run_editor_integration.sh`, `client/Assets/Tests/Integration/IntegrationRun.cs`): the
real client in play mode, env=local, dev auth, a fresh wallet, with `observer-bot.mjs` as a second player.

| check | means |
|---|---|
| sign_in_dev | dev sign-in returns a session for the wallet |
| lottery_then_character | the lottery rolls and a character is created (`autocreate`) |
| city_and_market_joined | both always-on Colyseus rooms join |
| venue_joined, server_npcs_visible | Timeline Square joins and the server's NPCs arrive |
| sees_other_player_move, receives_chat | the client sees the bot walk and hears its chat |
| seen_by_other_client | the bot sees our player, our emote and our chat line |
| shift_pays_cash | apply for a career, work a shift, Cash goes up |
| block_report_unblock | safety calls against the bot's real user id |
| token_refresh, rejoin_after_refresh | the token rotates and rooms still join with it |
| venue_isolation | Terminal Café gets its own room, not Timeline Square's |

Then `FeatureProbe` calls every lane's API through the client's own code (money, city life, careers, social, world,
ventures, competition, venues), and each phone app is opened once.

**2. Device** (`run_device_e2e.sh`): the installed dev build on the Seeker.

| check | means |
|---|---|
| sign_in | a fresh dev player signs in |
| onboarding_saved_on_server | real taps through lottery → character → tribe → traits → dream → alias → player card; then `GET /v1/me` for the same wallet must hold the typed alias, a tribe, two traits, a dream, a look and the rolled lottery |
| seen_by_other_client | after ENTER THE CITY (twice: celebration, then home), the observer bot in Timeline Square sees the player by alias |
| device_joined_venue | the device logs the travel and the room joins |
| no_errors_during_play | no Unity errors or exceptions in logcat during the walk |
| device_feature_probe | `integration=1` runs FeatureProbe on the phone; no feature may fail |
| app_alive, no_exceptions_during_probe | the app is still running and threw nothing |

## Reading results

- **pass**: works end to end.
- **blocked #N**: waits on backend item N in `backend-dev-feedback.md` (sent to devIykee as
  https://github.com/devIykee/ct-life-backend/issues/2). A blocked row does not fail the run.
- **n/a**: this backend has no route for it yet (Phase 5/6 proposals); the app shows "Not available yet".
- **FAIL**: a real failure. Fix it or report it before merging.

## Not covered (needs a person or a real wallet)

- SIWS with a real wallet (Seed Vault approval on the phone); dev auth is used instead.
- USDC top-up signing (also blocked by backend #87), Seeker Genesis Token holders' third lottery card.
- Voice: the Space token is minted (`social.space_audio`), but audio itself is not asserted.
- The onboarding tap coordinates are for the 2670×1200 Seeker and the 2026-10-10 layout; update
  `run_device_e2e.sh` when a screen's buttons move.

## Latest results

2026-10-10, `../ct-life-evidence/e2e/20261010-145702/SUMMARY.md`, backend devIykee `9a4d6ff` + local patches 0001,
0002, 0004 (0003 dropped: fixed upstream in 4616dc6). **Overall PASS.**

- Editor: 13/13 checks (with a second, idle player in the room). Features: 28 pass, 5 blocked, 15 n/a, 0 fail.
- Device: 9/9 checks. Features: 25 pass, 5 blocked, 9 n/a, 0 fail.
- Blocked on the backend (issue #2 on devIykee/ct-life-backend): homes rent and visit (#75), USDC top-up intent 500
  (#87), call ringing (#54), city events (#64).
- n/a: Phase 5/6 routes this backend does not serve yet (projects list, hiring, VC deals, beef list, farm, sybil
  status, venue access, cabal, LLM quota).
