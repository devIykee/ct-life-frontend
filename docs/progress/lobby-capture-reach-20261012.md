READY TO COMMIT: `client/Assets/Scripts/UI/Screens/AvatarPreview.cs`, `client/Assets/Scripts/Editor/LobbyFlowCapture.cs` (+ `.meta`), `client/Assets/Scripts/Editor/OnboardingCapture.cs`, and this log — verify lobby reach, phone apps and balance layouts with isolated fixtures.

# Lobby reach and capture verification — 2026-10-12

## Implementation

- `AvatarPreview.SetFullBodyReach(true)` is an opt-in for the lobby. It reuses the creator's anatomical reach envelope and samples the selected avatar's shipped reaction/fidget clips before display, without adding a podium. The camera remains fixed when a later reaction plays. Other callers retain their existing framing.
- `LobbyFlowCapture` runs the actual `DashboardScreen`, `AvatarPreview`, `PhoneShell` and `PhoneCatalog` apps with isolated in-memory profile, REST and balance-RPC fixtures. It restores the original service instances afterward, rejects/counts REST writes, and does not persist tokens or profile changes. The capture is Editor-only, with a visible `REVIEW · LOCAL FIXTURES` watermark.
- Coverage includes Snow, Rain with a red palette top, Dex and the saved Gynoid; wave and both-hands-raised poses; long identity, debt and large amounts; stale/disconnected balances; the actual Wallet page in ready/large/unavailable/disconnected states; and Phone, Map, Jobs, Bank, Profile, Home and Settings.
- Geometry assertions bake each actual selected skinned mesh, project its vertices through the preview camera into the rendered UI texture, and check clipping and control overlap. Additional assertions check real control bounds, text fit/ellipsis, full-precision Wallet rows, the Energy popup, and camera stability across reactions.

## Accepted final desktop capture

Coordinator evidence: `/Users/mac/Documents/codes/projects/ct-life-evidence/lobby-duo-20261012/lobby-final/receipt.json`.

- **28 captures; pass true; no fatal error or failed assertion.**
- All **16** layout cases reported **zero cropped vertices and zero vertices overlapping lobby controls**. Actual vertex counts were Snow 7,140, Rain 8,419, Dex 6,221 and Gynoid 8,060.
- Services restored: true. REST writes: **0**. Runtime errors: **0**. All 27 RPC calls were to the in-memory fixture.
- The Energy popup bounds were `(19.79, 496.49, 660.19, 404.75)` in panel coordinates, fully inside the roughly 2,400 × 1,080 panel. The previous offscreen popup failure was resolved in the coordinator's placement code.
- Independently inspected Snow, Rain and Dex raised-hand images: both hands and feet remain visible, with space above the head and no square-render-target cut. The selected Rain red top is retained. Inspected Gynoid's normal render; its authored body remains selected.
- Independently inspected the long-identity/Energy-popup and large-Wallet renders. Lobby numbers remain inside their cards; large Cash and wallet summaries use compact units. The actual Wallet app retains full-precision amounts in its scrollable detail cards. Ready, unavailable and disconnected Wallet cases are separately captured, rather than treating an unavailable response as a zero balance.

Earlier capture failures were useful: they exposed large summary text overflow and the Energy popup's initial zero-size layout measurement. The coordinator corrected those surfaces; the final receipt above is the accepted run. This lane made no further runtime changes after the final capture.

## Saved Gynoid finding

The coordinator's backend read found Joel's saved look uses `head_hero_gynoid` and its matching body slots, with `skin_tone3` and `acc_none`. The preserved source portrait `client/Assets/Resources/UI/Customizer/Portraits/head_hero_gynoid.png` has the same smooth faceless head and white mechanical body visible in the earlier lobby screenshot. Its provenance is Gynoid 2000 Series by Loves_Art, CC-BY 4.0, recorded in `client/Assets/Art/characters/distinct/manifest.json` ([source](https://sketchfab.com/3d-models/gynoid-2000-series-56993f528d7041f9b734c0221f5dffe0)). This is the saved robot selection, not evidence that Rain lost its face. No avatar substitution or saved-look mutation was made.

## Reproduction and remaining verification

Run only in the coordinator's isolated Unity clone, with graphics available:

```sh
Unity -batchmode -projectPath <clone-client> -buildTarget Android \
  -executeMethod CTLife.EditorTools.OnboardingCapture.Launch \
  -ctlife-captureSet lobby \
  -ctlife-captureOut <external-evidence-directory>
```

Do not add `-nographics`: these checks render the real camera and UI panels. The older experimental `FrontendReviewController` is outside compiled assemblies and is not the accepted capture route.

The captures establish the fixture-driven desktop layouts and geometry listed above. They do not establish live backend balances or Android performance. Final test execution, APK installation and real Seeker verification remain with the coordinator; this lane did not run Unity, ADB, builds, commits or pushes during the lobby work.
