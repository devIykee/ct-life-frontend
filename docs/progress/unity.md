# Unity agent progress log

Owner folders: `client/` (Unity project root), repo-root `.mcp.json`, FE-owned registries
`contract/ids/anims.json`, `sfx.json`, `spots.json` (append only).

## Licence status
- **2026-10-05 17:0x: LICENSED.** `~/Library/Unity/licenses/UnityEntitlementLicense.xml` present; batchmode
  `-createProject` probe exits 0 ("Exiting batchmode successfully now!"). Post-licence work started.
- 2026-10-05 15:22 and again 16:5x local: **NOT licensed.** A throwaway `-batchmode -quit -nographics -createProject`
  in the session scratchpad exits 198 with
  `[Licensing::Module] Error: Access token is unavailable; failed to update` →
  `Error: 'com.unity.editor.headless' was not found.` → `No valid Unity Editor license found.`
- Waiting for the user to sign in to Unity Hub (agents must not sign in). Everything that does not need the editor is
  done (below). Re-check: `.../Unity.app/Contents/MacOS/Unity -batchmode -quit -nographics -createProject /tmp/x -logFile -`.

## Post-licence progress (2026-10-05, evening)

### Scope changes from the user (via coordinator)
- The user is **frontend only**; a separate backend developer owns the backend (github.com/devIykee/ct-life-backend,
  `contract-v1.0.0-rc1`). Our local `server/`, `tools/` (incl. mock), `programs/`, compose files and containers were
  deleted. `contract/` stays only as a **local stand-in** (registries for anims/sfx/spots and greybox generation;
  realtime schemas until the user has repo access).
- **REST source of truth:** `contract/backend-dev/openapi.json` (his 1.0.0-rc1, 90 ops, 46 schemas). Regenerated with
  `client/Tools~/codegen_rest.sh` (openapi-generator-cli 7.25.0 from Maven Central, sha1 verified; validatable=false;
  `patch_rest_codegen.py` widens `*Cents` to long and fixes `const: true`). All call sites fixed; **all 9 CT Life
  assemblies compile against it.** Codegen/schema problems written up for him in
  `docs/progress/backend-dev-feedback.md` (2 blocking: `const: true` boolean → invalid C# enum; money fields int32).
- REST base URLs now include `/v1` (his `servers`): mock `http://localhost:4010/v1`, staging
  `https://staging-api.ctlife.dev/v1`, prod `https://api.ctlife.dev/v1`.
- Network layer behind interfaces: `IRestClient`, `IAuthService`, `IRealtimeService` (`client/Assets/Scripts/Net/Interfaces.cs`).
- Additive contract features built: **SERVER_BUSY + Retry-After** (REST auto-retry honouring Retry-After seconds or
  HTTP-date; realtime join backoff; `BusyOverlay` "City is busy, retrying in Ns") and **`degraded` on GET /config**
  (`Features` helper keyed to his values; HUD mic, phone apps, sign-in disable with "temporarily unavailable"; config
  re-polled every 2 min with If-None-Match). His flat `/config` layout is merged over the bundled snapshot.
- Our earlier requests to the (now stopped) backend agent in `requests.md` are moot.

### Editor bring-up
- Project opened in batchmode; real-editor compile: all runtime assemblies compiled first time; one editor-only error
  (`ShadowQuality` ambiguous with URP's) fixed. Unity auto-added template packages on first open (IAP 4 — deprecated and
  would add Play Billing — analytics, collab, 2D, XR…); pruned from the manifest.
- `ProjectSetup.Run` applied: CT Life / com.ctlife.app, IL2CPP, ARM64, Vulkan→GLES3, min SDK 29, target SDK 36,
  Optimized Frame Pacing (swappy) on, GameActivity, GPU (batched) skinning, ASTC, URP asset (render scale 0.7, no
  shadows/HDR, MSAA 2x) assigned, PanelSettings, Env assets, Bootstrap + Spike scenes in Build Settings, Addressables
  settings. Workarounds needed in batchmode: `UniversalRenderPipelineAsset.Create` throws "AssetDatabase cannot load now"
  (asset created via CreateInstance + serialized renderer list instead); `Shader.Find`/`LoadAssetAtPath` return null for
  URP shaders during these runs (runtime materials now ship as `Resources/Materials/*.mat` wired to URP Simple Lit by GUID).

## Pinned versions (verified 2026-10-05)

Sources: the editor's own package manifest
(`Unity.app/Contents/Resources/PackageManager/Editor/manifest.json` and `BuiltInPackages/*/package.json`
inside 6000.3.25f1), `packages.unity.com`, `package.openupm.com`, GitHub API (tags/branches/commits), PyPI.

| Package | Pin (in `client/Packages/manifest.json`) | Evidence / note |
| --- | --- | --- |
| URP `com.unity.render-pipelines.universal` | `17.3.0` | Core package bundled in 6000.3.25f1 (`BuiltInPackages`, `unity: 6000.3`). The public registry stops at 10.10.1 because 17.x ships with the editor. |
| Input System `com.unity.inputsystem` | `1.20.0` | Editor-recommended/bundled for 6000.3.25f1. 1.20.1 was published to the registry today (2026-10-05); not adopted until the editor recommends it. |
| Addressables `com.unity.addressables` | `2.10.3` | Editor-recommended for 6000.3.25f1 (`mustBeBundled`). Registry has 4.1.0 (2026-09-28, new major, needs SBP 4.0.0); not adopted. |
| Adaptive Performance `com.unity.adaptiveperformance` | `6.0.0` | Bundled in 6000.3.25f1 (registry latest is 5.1.6; 6.0 ships with the editor; runtime lives in `UnityEngine.AdaptivePerformanceModule`). |
| AP Android provider `com.unity.adaptiveperformance.google.android` | `6.0.0` | Bundled in 6000.3.25f1. |
| Cinemachine `com.unity.cinemachine` | `3.1.7` | Registry latest, published 2026-06-09, `unity: 2022.3`, deps `com.unity.splines 2.0.0`. (The editor recommends 2.10.7; the brief asks for 3.x.) |
| Newtonsoft `com.unity.nuget.newtonsoft-json` | `3.2.2` | Editor-recommended and registry latest (2025-10-27). |
| AI Navigation `com.unity.ai.navigation` | `2.0.14` | Editor-recommended (registry has 2.0.15 from 2026-09-24). |
| uGUI + TextMeshPro `com.unity.ugui` | `2.0.0` | Core package; TMP is merged into ugui in Unity 6. |
| Android Logcat | `1.4.7` | Editor-recommended. |
| Mali system metrics `com.unity.profiling.systemmetrics.mali` | `1.1.4` | Editor-recommended; Mali GPU counters for Seeker profiling. |
| Test Framework | `1.6.0` | Editor-recommended. |
| **Colyseus Unity SDK** `io.colyseus.sdk` | **embedded** at `client/Packages/io.colyseus.sdk` = `upm` branch `7306c8ee4be7e78984baf0837df5ea8125154820` | Release `0.18.8` (2026-09-23, tag = `05fc089`); the installable UPM layout is the `upm` branch, commit `7306c8e` "Update upm to output generated at 05fc089", `package.json` 0.18.8. Git URL `https://github.com/colyseus/colyseus-unity-sdk.git#7306c8e…` was the plan; it is **vendored instead** because of the CS0433 clash below (only change: two asmdefs set `overrideReferences: true`; see `VENDORED.txt`). |
| **LiveKit Unity SDK** `io.livekit.livekit-sdk` | `2.1.0` via OpenUPM scoped registry (`https://package.openupm.com`, scope `io.livekit`) | GitHub release `v2.1.0` = `356bf5d`, 2026-09-23. The git URL `…client-sdk-unity.git#v2.1.0` needs **Git LFS** for `liblivekit_ffi` and `git lfs` is not installed here. The OpenUPM tarball (sha1 `ba8aacec…`, 128 MB) contains the real `ffi-android-arm64/liblivekit_ffi.so` (19.6 MB) and `libwebrtc.jar`. README: tested on Unity 6000.3.10; Android supported. |
| **Solana.Unity-SDK** `com.solana.unity_sdk` | `https://github.com/magicblock-labs/Solana.Unity-SDK.git#bd0d30f648ed2368c0775e8c2eecd578eb6e4999` | `main` HEAD on 2026-10-05 = `bd0d30f` (2026-06-17, "Merge pull request #289 feat/mwa-v2"), `package.json` 1.3.0. `v1.2.9` (#257, still OPEN: UnityLinker can't resolve BouncyCastle on Android IL2CPP) not used. SIWS API verified in source: `Web3.Instance.LoginWalletAdapter(SignInPayload)` → `Task<(Account, SignInResult)>`, base64 `SignedMessage`/`Signature`. |
| **MCP for Unity** `com.coplaydev.unity-mcp` | `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v10.3.0` | Release `v10.3.0` = `aa5fc63`, 2026-10-04; package.json 10.3.0. Server `mcpforunityserver==10.3.0` on PyPI (2026-10-04); `uvx --from mcpforunityserver==10.3.0 mcp-for-unity --help` runs (uv 0.11.21). |

Android toolchain installed with the editor (`/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer`):
SDK platforms android-34/35/**36**/37.0, build-tools 36.0.0, NDK r27c (27.2.12479018), OpenJDK 17.0.18.
`PlayerSettings` APIs for min SDK 29 / target SDK 36 (`AndroidSdkVersions.AndroidApiLevel36`), Optimized Frame
Pacing, ASTC, Vulkan+GLES3 exist in this editor (checked in its UnityEditor XML docs).

## MCP for Unity (Claude Code)
- Repo-root `.mcp.json` registers `UnityMCP` as `{"type": "http", "url": "http://localhost:8080/mcp"}`, the format
  `claude mcp add --scope project --transport http` writes (checked with Claude Code 2.1.289 in a scratch dir).
- HTTP is the package default; auto-start defaults to off. Once the editor is open: **Window → MCP for Unity → Start
  Server**, or `uvx --from mcpforunityserver==10.3.0 mcp-for-unity --transport http --http-url http://127.0.0.1:8080`.
- Docs used: `website/docs/getting-started/install.md`, `architecture/transports.md`,
  `Editor/Clients/McpClientConfiguratorBase.cs` at tag v10.3.0.

## Pre-licence work (DONE)

### Compile-ready, verified without a licence
`client/Tools~/compile_check.py` compiles the whole client with the editor's own Roslyn (`DotNetSdkRoslyn/csc.dll`
on its bundled dotnet) against its UnityEngine/UnityEditor reference assemblies and the .NET Standard 2.1 profile,
with Android IL2CPP player defines. It fetches/builds the pinned third-party sources too (ugui+TMP, UniTask, Solana
SDK @bd0d30f, Colyseus (vendored), LiveKit 2.1.0, Addressables 2.10.3 + ResourceManager + Profiling.Core) and emulates
Unity's auto-referencing of precompiled DLLs from their `.meta` files. Stubs (under `Tools~`, never imported) mirror the
URP 17.3.0 and Addressables 2.10.3 editor signatures we touch, copied from the real sources.
**Result (latest run): all 9 CT Life assemblies compile (8 player + CTLife.Editor), 0 warnings at level 4.**
```
python3 client/Tools~/compile_check.py            # player pass
python3 client/Tools~/compile_check.py --editor   # + CTLife.Editor
```
[Inference] It approximates Unity's compile; the real editor compile (ILPP, asset import) is still step 4.

### Problems the harness found (would have broken the first real build)
1. **CS0433 NativeWebSocket clash (confirmed, fixed):** Solana SDK's `Packages/NativeWebSocket.dll` is auto-referenced on
   every platform (Any Platform, no Exclude flags) and defines `NativeWebSocket.WebSocket`, which Colyseus compiles from
   source → `ColyseusSDK` fails. Fix: Colyseus vendored as an embedded package with `overrideReferences: true`.
   Reproduce upstream failure: `CTLIFE_CC_COLYSEUS_UPSTREAM=1 python3 client/Tools~/compile_check.py`.
2. **REST models don't compile under .NET Standard 2.1:** `System.ComponentModel.DataAnnotations` (IValidatableObject) is
   not in Unity's netstandard.dll. Regenerated with `validatable=false` (request to backend to add it to the script).
3. **`const: true` booleans** become `enum { True = true }` (invalid C#) in 3 models; `client/Tools~/patch_rest_codegen.py`
   fixes them and a dangling comma on `HeliusWebhookRequest`. Request to backend for the schema fix.
4. **URP 17.3 `supportsMainLightShadows` / `additionalLightsRenderingMode` have `internal set`**: ProjectSetup writes the
   serialized fields instead.
5. `RoomOptions` ambiguity (LiveKit vs LiveKit.Proto) and Base58 encoder namespace; fixed.

### Codegen (run into FE folder)
- `pnpm contract:codegen:cs` → `client/Assets/Generated/Realtime` (24 files; needs `mkdir -p client/Assets/Generated` first).
- REST: `cd contract && pnpm exec openapi-generator-cli generate -i openapi.yaml -g csharp -o ../client/Assets/Generated/Rest
  --global-property models,modelDocs=false,modelTests=false,apiTests=false,apiDocs=false,supportingFiles=false
  --additional-properties=packageName=CTLife.Rest,targetFramework=netstandard2.1,nullableReferenceTypes=false,library=httpclient,validatable=false`
  then `python3 client/Tools~/patch_rest_codegen.py` (123 models). Support types in `client/Assets/Generated/Support/`.
- `python3 client/Tools~/sync_contract.py` bundles ids+config into `Assets/Resources/Contract/config_snapshot.json`
  (GET /v1/config shape) so registries resolve offline / before the network config arrives.

### Client architecture (`client/Assets/Scripts`, one asmdef each)
- **CTLife.Core**: `EnvConfig`/`EnvSelection`/`Env` (mock/local/staging/prod; launch override), `LaunchArgs` (Android
  intent extras: `env`, `autologin`, `wallet`, `venue`, `spike`, `livekitUrl`, `livekitToken`), `Services` locator, typed
  `Bus`, `ServerClock` (welcome offset), `ConfigStore` (ETag cache → snapshot) + `Registries` (all ID registries, unknown-ID
  fallback rule, NPC line formatting), `Session`, wallet interfaces (`IWalletSignIn`, `ITransactionSigner`), `Runner`.
- **CTLife.Net**: `RestClient` (UnityWebRequest + Awaitable, Idempotency-Key reused on retries, X-Client/Contract-Version,
  401 refresh, 429 Retry-After, 426 → update screen, contract error codes kept as strings), `Api` (Phase 1 + spike
  endpoints), `AuthService` (SIWS with raw challenge strings, dev bypass, refresh, resume, logout + deauthorize),
  `PlayerProfile`, `RealtimeService` + `CityChannel`/`MarketChannel`/`VenueChannel` (welcome/rejected, every contract
  message, state callbacks, client-side rate limits), `VoiceService` (LiveKit: nearest-6 subscription within 10 m, 3D voice
  on avatars, mic with AEC/NS/AGC, speaking → `talking`).
- **CTLife.World**: `VenueLoader` (Addressables scene by `sceneKey` → Build Settings scene → greybox built from spots.json
  with runtime NavMesh), `VenueSceneRoot`/`SpotAnchor`, `VenueSession` (binds the venue room to avatars, bubbles,
  rejections, voice), `LocalPlayerController` (tap-to-move NavMesh, `move` ≤10 Hz, sit/stand), `RemotePlayer` (120 ms
  snapshot interpolation, emotes at `emoteAt`), `NpcController` (state → spot/anim/line/babble, late-join snap),
  `AvatarFactory` + `CharacterAssembler` (merge parts into one SMR + one material, atlas fallback, blend shapes) +
  `PlaceholderHumanoid` (procedural 30-bone humanoid, 3 LODs, palette material) + `ProceduralAnimator` (every anims.json id)
  + `AvatarLod` (12/24/rest, 25 m cull, thermal halving), `CameraRig`, `WorldInput`, `PerfManager` (60 fps, render scale
  0.7 → 0.6 on throttling via Adaptive Performance or PowerManager thermal status), `FpsMeter` (logcat `CTLIFE [fps]` every 5 s).
- **CTLife.Audio**: `AudioManager` (sfx-map.json driven, unknown-id blip, stinger + voice ducking), `MusicDirector`
  (4 stems + tension, market_mood −1..1, low-pass on red), `Babble` (clip or synthesized syllables).
- **CTLife.Solana**: `MwaWallet` (Solana.Unity-SDK `LoginWalletAdapter(SignInPayload)`, deauthorize,
  `SignAndSendTransactions` for server-built txs [Unverified: v0 round-trip, Phase 2]).
- **CTLife.UI** (UI Toolkit, built in code, `Resources/UI/ctlife.uss`): `UiRoot` layers + safe area, world-input layer
  (tap/drag/pinch), splash, sign-in, character creator (3D preview, tribes with perks, traits, dream, 18+), birth lottery,
  city map, update-required, HUD (Cash, 5 needs, online, Mood, chat, emotes, mic), toasts + Town Crier events, REKT card,
  name tags (NPC tag, Seeker ✓, ORE tiers, speaking) and speech bubbles, profile/NPC card with Block/Report (+ Giveaway
  Guy "Drop wallet"), report sheet, phone shell with Map, Jobs (shift flow + mini-game host), Bank, Rich List, Home/needs,
  Profile, Settings, and later-phase tiles.
- **CTLife.App**: `GameBootstrap` (composition root), `FlowController` (splash → sign-in → creator → lottery → city),
  `SpikeController` + `CrowdWalker` (engine spike).
- **CTLife.Editor**: `ProjectSetup.Run` (Android settings, URP mobile asset, always-included shaders, PanelSettings, Env
  assets, Bootstrap/Spike scenes, Addressables settings), `BuildScript.BuildAndroid` (Addressables content + APK).
- `Assets/link.xml` keeps reflection-used assemblies from IL2CPP stripping.

## Next (post-licence), exact commands
```
U=/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity
# 4. open/import + setup (creates scenes, URP, Android settings)
$U -batchmode -quit -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.ProjectSetup.Run -logFile client/Logs/setup.log
# 6. spike APK + Seeker run
$U -batchmode -quit -projectPath client -buildTarget Android -executeMethod CTLife.EditorTools.BuildScript.BuildAndroid -ctlifeOut Builds/ctlife-dev.apk -ctlifeDev -logFile client/Logs/build.log
ADB=~/Library/Android/sdk/platform-tools/adb; for p in 4010 2567 3000 7880; do $ADB -s SM02G40619141343 reverse tcp:$p tcp:$p; done
$ADB -s SM02G40619141343 install -r client/Builds/ctlife-dev.apk
$ADB -s SM02G40619141343 shell am start -n com.ctlife.app/com.unity3d.player.UnityPlayerGameActivity -e spike 1 -e autologin dev
$ADB -s SM02G40619141343 logcat -s Unity | grep CTLIFE
```
Then: import `art/processed/characters` (Base prefab + parts by prefabKey, anim alias map, AvatarController with states
named by anims.json ids), venue scenes from art, `audio/processed` → `Resources/Audio`, the 9 Phase-1 mini-games, and
Phase 2 screens. [Unverified] Adaptive Performance Android provider still has to be enabled in Project Settings
(the code falls back to PowerManager thermal polling).

## Requests posted (docs/progress/requests.md)
- unity → audio: `audio/sfx-map.json` schema.
- unity → backend: REST codegen fixes (`validatable=false`, `const: true` booleans, Helius dangling comma, codegen mkdir);
  FE approval of contract v1.0.0 with notes.
- unity → assets: clip-name alias map (no renames needed) and 4 missing clips (`act_host_mic`, `act_listen_nod`,
  `emote_moon_jump`, `react_sad`).

## Log
- 2026-10-05: read AGENTS-BRIEF, build-spec, contract. Licence check failed. Pinned packages, wrote manifest and `.mcp.json`.
- 2026-10-05: ran realtime + REST codegen; wrote the full client architecture (8 runtime asmdefs + editor); built the
  pre-licence compile harness; fixed every error it found; vendored Colyseus; all CT Life assemblies compile.
- 2026-10-05: licence re-checked after pre-licence work: still unlicensed (exit 198). BLOCKED until Unity Hub sign-in.
