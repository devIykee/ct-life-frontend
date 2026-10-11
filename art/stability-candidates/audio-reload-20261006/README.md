# Native audio reload review

Status: coordinator installed the harness in `Assets`; this folder mirrors the latest source. Unity 6000.3.25f1 Roslyn compile passed. Native V1–V4 failed at different checkpoints; **V5 passed** with exit 0, `reloads=2 runtimeErrors=0 sceneRestored=True`, including sustained playback while unfocused. Details and limited scope are in `docs/progress/ct-stability-audit-20261006.md`. Root owns the Editor slot. Do not copy these files while a visual/device review is running.

When Unity is idle, copy only `AudioReloadReview.cs` and its `.meta` to `client/Assets/Scripts/Editor/`. No asmdef or existing source edits are needed; the Editor assembly has no direct audio reference, so the harness uses checked reflection for that API.

Run a normal, audio-enabled Editor (no `-batchmode`, `-nographics`, or `-quit`):

```sh
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  -projectPath /Users/mac/Documents/codes/projects/ct-life/client \
  -executeMethod CTLife.EditorTools.AudioReloadReview.Launch \
  -ctlife-audioReviewExit 1 \
  -logFile /tmp/ct-audio-reload-review.log
```

The optional exit flag quits this **Editor** after cleanup. This harness is Editor-only and cannot close an Android game. The menu `CT Life > Review > Audio domain reload twice` runs without an exit flag in an already-open Editor.

The harness:

1. Refuses an active Play session or unsaved scene. Waits at most 120 seconds for imports and `SearchService.EnumerateDatabases().Any()` in Edit mode (database existence, not full index readiness) before replacing a clean scene with an unsaved, empty review scene.
2. Opens/focuses GameView, enters Play and waits up to 20 seconds for application/GameView focus. Paused or muted Editor state is reported as an explicit failure; neither setting is changed. Temporarily enables **runtime** `Application.runInBackground` so working elsewhere on the Mac does not pause this test; SessionState preserves the original value through reloads and cleanup restores it. The project setting is not saved. Creates only a camera/listener, a player anchor and `AudioManager`; no `GameBootstrap`, networking or fixture game state. Temporarily sets private **runtime** volume fields so a muted game preference cannot skip the footstep assertion; it does not call the preference-writing setters.
3. Starts Timeline music and ambience, warms four seconds, then asserts 26 unique named native sources, the manager/director/listener identities, two listener components with only the player listener active, selected district/clips, loaded/playing loops and nonzero mix gains.
4. Checks exactly one SFX bus handler, a real UI-success voice and concrete footstep; records native IDs, clip IDs, playheads and runtime snapshots. Calls `EditorUtility.RequestScriptReload()` twice. The `[InitializeOnLoad]` observer plus `SessionState` count actual assembly reloads, without changing a source file. Post-reload warmup starts on the first post-reload Editor update, then rechecks all identities and playback. Environment/source diagnostics precede assertions, so failures retain focus/mute/DSP/clip-load/playhead evidence.
5. Continues for 12 seconds, validating every two seconds. An error/exception/assert logged during Play fails the review. Restores runtime volumes, leaves Play and restores the original saved scene setup without saving scene/assets/preferences. The entire review is bounded by a 240-second deadline, plus at most 30 seconds for stopping.

Acceptance: final `CTLIFE [audio-reload] PASS reloads=2 runtimeErrors=0 sceneRestored=True`, Editor exit code 0, and no uncaught exceptions in the log. This verifies Editor hot reload behavior; it does **not** establish the cause of an Android process exit or subjective audio quality. A missing asset/audio backend error is a failed test, not an allowed skip.

Official API/lifecycle references, opened 2026-10-06:

- [RequestScriptReload, Unity 6.3](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/EditorUtility.RequestScriptReload.html): asynchronous next-frame reload; no new compilation.
- [SessionState, Unity 6.3](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SessionState.html): survives assembly reloads and clears when Editor exits.
- [EditorApplication.update](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/EditorApplication-update.html).
- [RestoreSceneManagerSetup](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SceneManagement.EditorSceneManager.RestoreSceneManagerSetup.html).

Compilation used the existing Editor response file plus this staged source, with output redirected to `/tmp/CTLife.Editor.AudioReloadReview.dll`; no Unity process or asset import was launched. The only compiler output was the response-file `/noconfig` warning, CS2023. Runtime source changes remain separately frozen in `AudioManager.cs` and `MusicDirector.cs` for native review.
