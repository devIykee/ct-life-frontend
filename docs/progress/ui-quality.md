# CT Life UI quality pass

**Current checkpoint:** the user rejected the mineral interface and its buttons. The tactile blue/gold game identity, illustrated signed-in lobby, Lilita One display type, filled/bold licensed icons and movement controls supersede it. K2 onward layout/touch evidence, including the M2 Settings trace below, records technical checks rather than user approval. Earlier sections preserve history, not current appearance or approval.

## 6 October — M2 Settings routing trace

Root's earlier M observation showed city after an intended Settings tap; the original cause remains unidentified. Source inspection found both production and review lobby callbacks call `PhoneShell.Open("settings")` without closing the lobby. No routing behavior was changed to address an unproven cause.

The M2 repeat was inspected directly:

- [Actual Settings touch capture](screens/quality-review-20261006m2/touch/settings-touch.png) shows the Settings phone overlay over the still-visible, dimmed lobby. The 20-W alias is ellipsized and Settings remains within the display.
- [Native trace](screens/quality-review-20261006m2/touch/settings-trace.log) at 09:27:27 identifies the actual pointer target **and** independent panel pick as `ct-lobby-settings`. Panel position `(984.88,176.29)` lies within its bounds `(919.23,110.63,132.22,131.32)`.
- The callback is `settings`; afterward `screen=DashboardScreen`, `screenAttached=True`, `phoneOpen=True`, `screenChildren=1`, `phoneChildren=1`. The observed repeat did not enter the city.
- Added optional `reviewInputTrace=1` diagnostics only in the compile-guarded review flow. Logs include control classes, coordinates/bounds and routing/layer state; they omit labels, account data and entered text. Default input behavior is unchanged.

**Limit:** the prior observation was not reproduced, rather than diagnosed or fixed. This proves the M2 reviewed local fixture path, not a backend-authenticated session or all devices. Initial M stress evidence is retained. Compiler/API and scoped diff checks passed for the instrumentation; root ran the real touch verification.

## 5 October 2026 — implementation checkpoint

Read `docs/AGENTS-BRIEF.md`, original build specification, frontend/backend contract, existing UI implementations and the current PanelSettings before editing. Effective art direction is the updated warm Sims-style city; no character model or world assets changed by this pass.

### Implemented

- Shared ivory, forest, muted plum, amber and coral palette; larger type hierarchy, borders, radii and consistent touch targets in `Resources/UI/ctlife.uss`.
- Locally drawn vector icons for the action dock and in-game phone. No external fonts, image downloads, packages or licence additions.
- Welcome/sign-in composition with an editorial introduction, a clear wallet action, gated demo access and concise age/game-Cash information. Removed MWA/env jargon from the player-facing sign-in instructions; existing wallet/dev handlers and feature gate remain.
- Character creator keeps its live rotating preview and all appearance/identity choices. Sections are easier to scan; the primary action and validation guidance remain visible in a fixed footer. Corrected fallback item display names and validation after a busy request.
- Compact translucent cash/status/self-care HUD and a consistent five-action bottom dock. Need labels are written out and include values. HUD decoration passes world touches through. Opening chat closes emotes and vice versa.
- Scrollable phone home with vector app icons, a greeting, separate available/upcoming app groups and preserved feature gates. Implementation phase numbers are replaced with player-facing opening-soon language.
- City directory with district identity, short descriptions, open-place count, clear travel targets and readable closed states. This remains the existing 2D travel directory, not a claim that the low-detail 3D overview scene has been built.
- Bank, home, settings and rich-list detail styling/copy. Rich-list tabs now show selection and discard stale async responses after another tab/app opens.
- Origin cards use a vertical layout with readable rewards and registry-derived home names. A failed roll now stops the reveal animation before retry rather than leaving its flashing loop running.
- Safe-area bounds are applied to the HUD/screen/phone/modal/toast layers and refreshed on viewport/safe-area changes. This replaces padding on full-screen absolute children, which did not position those children inside the insets.
- New player-facing copy goes through `Strings`; registry-provided display names remain data.

### Boundaries

Only `client/Assets/Scripts/UI`, `client/Assets/Resources/UI/ctlife.uss` and this log were edited. `PanelSettings.asset`, app/net/core/backend/contract, art, audio, world/camera files are not owned or edited by this pass. Existing constructors and external UI APIs are preserved. No commit/push, package install, Unity Editor run, APK build or device install was performed by this agent.

### Verification so far

- Checked Painter2D, Angle, LineCap/LineJoin and MeshGenerationContext APIs against the installed Unity 6000.3.25f1 `UnityEngine.UIElementsModule.xml`.
- Checked current reference resolution: 1080×2400; stylesheet sizes are reference-canvas units, not Android dp.
- Static scan: no undefined constant `Strings.T/F` keys; USS braces balanced.
- Root compile initially found a missing `CTLife.Net` import for `PlayerProfile` in PhoneShell; corrected. Root subsequently reported successful Unity script compilation and venue generation.
- Viewed root's six actual Seeker captures in `docs/progress/screens/quality-review-20261005/quality-review-20261005/`. Sign-in, phone and bank hierarchy read clearly. Creator preview showed render-pass errors/feedback; appearance selectors exposed item IDs; phone-map subtitle overflowed; expanded needs card covered too much of the city.

### Device-review corrections

- Avatar preview now uses a single-sample RenderTexture and explicitly disables MSAA/HDR only on its own camera. The installed URP camera-data source derives target sample count from `camera.allowMSAA` and `targetTexture.antiAliasing`; the previous two-sample texture produced the observed attachment mismatch. The preview has an opaque sage backdrop. World-camera anti-aliasing is unchanged.
- Preview camera, avatar descendants and directional light use reserved layer 31. Existing TagManager and scripts had no assigned use of that layer. Disposal disables the camera, detaches the target, releases and destroys the RenderTexture, then destroys the preview root.
- Shared vertical-scroll helper constrains content to the measured viewport width so wrapped labels cannot size beyond the screen. Touch/wheel scrolling remains available; desktop-style arrow scrollbars are hidden. Applied across creator, welcome, map, phone pages and emotes.
- Known appearance IDs now have centralized player-facing names; registry display names are used when available. Fallback strips the slot prefix instead of exposing an internal ID.
- The self-care panel starts collapsed behind a 124-unit touch target. It still exposes every need/value when tapped, and its collapsed summary calls out the lowest need below 25.
- APIs for scroll viewport/scroller visibility, Camera/RenderTexture and Light culling were checked against local Unity 6000.3.25f1 documentation. Follow-up device capture is pending; no claim yet that the render-pass issue is resolved on the Seeker.

### Next

Root should rebuild and recapture creator, phone map and city HUD; check logcat for the prior MSAA/EndRenderPass errors and toggle self-care once. Root alone runs editor/build/device checks. Constructors and external APIs remain unchanged.

### Second Seeker review

- Viewed `quality-review-20261005b` creator, map and city screenshots. Preview feedback is absent, appearance names are readable, map subtitle stays within the phone and collapsed self-care exposes substantially more of the city. Root reported zero render exceptions in the second device run.
- The creator's head was still clipped by its fixed camera aim/distance. Added per-look mesh-bound measurement across skinned and static avatar parts, excluding the decorative blob shadow. Skinned mesh geometry is baked once for measurement and the temporary mesh is destroyed. Camera target is the measured centre; distance accounts for vertical FOV, portrait RT aspect, turntable radius/depth and 16% margin. This avoids relying on the placeholder's padded/offset culling bounds. Framing still needs the next device capture.
- Confirmed BakeMesh, mesh bounds, TransformPoint and camera aspect APIs against installed Unity 6000.3.25f1 documentation. No change to world cameras, rendering quality or AvatarPreview's public API.
- Saved the requested read-only ORE/SKR endpoint/state-machine/blocker audit in `docs/progress/ore-skr-client-audit.md`. No financial feature code was implemented.

### Remaining placeholder-copy review

- REKT action now says “Share · soon”; its existing informational toast says “Card sharing isn't available yet.” The action still does not export or open a share sheet. Removed the player-facing “Phase 2” promise. REKT title, body, coin label and dismiss copy are centralized.
- Profile NPC badge is “CITY LOCAL”. Profile close/block/report/loading/giveaway/net-worth text now comes from `Strings`. Report sheet asks “What happened?” and shows readable centralized reasons, including “Sexual or graphic content” for the existing `nsfw` ID. API IDs and callbacks are unchanged.
- Successful API responses produce “Player blocked.” and “Report sent. Thanks for letting us know.” These confirm the completed request without promising unseen moderation work or future message behavior.
- Profile exceptions are logged and shown through mapped player-facing error copy. Unknown error fallback is now the generic central error instead of raw server text or an internal code. Existing known-code messages remain.
- Root reported that the actual Seeker self-care control expanded and collapsed correctly. Static copy-key scan found no missing constant keys; diff check passed. The latest framing/copy additions await root's next compile/capture.

### Third Seeker review and telemetry handoff

- Viewed `quality-review-20261005c/quality-review-20261005c/creator.png`: the complete head and shoes fit inside the preview with margin. Root reported no render errors and passed offline walking plus venue round-trip checks. No further framing change is needed for this current actor.
- The actor remains the existing temporary procedural character: its green shirt/short dark hair do not represent the selected “White hoodie” / “Pink mohawk.” This is an unresolved art/appearance implementation gap, not a finished character-creator claim.
- Root authorized a narrow extension into `FpsMeter`, `PerfManager` and stats additions in `AvatarView`/`AvatarLod`. That pass logs render quality/scale, explicit thermal source/unknown state, registered versus LOD-enabled/distance-culled avatar counts, and supported latest-frame ProfilerRecorder render counters. Thresholds and LOD/adaptation behavior were not changed. See `performance-caveats.md` for interpretation and remaining lifecycle risks.

## Creator correction after rejected model and UX review

The earlier appearance-picker claim is superseded by this correction. Inspection of the actual build-d creator image and `CharacterRegistry.asset` found no player prefab or look mappings; `ItemCatalog` had no base/part assets. The seven appearance rows therefore described registry IDs while rendering the procedural fallback. Renaming those rows did not implement the promised customization.

### Implemented, awaiting imported-asset/device validation

- Replaced the misleading individual hair/clothing selectors with an authored complete-look catalog. `CreatorLookCatalog` loads `Resources/UI/CreatorLookCatalog.asset`; each entry has `labelKey`, the existing seven-string `look`, and `previewYaw`.
- A catalog choice appears only if all seven IDs are present, the central display-label key exists, and `CharacterRegistry.ForLook` resolves the exact full seven-ID key to a prefab. Default and one-item fallbacks do not qualify. Multiple IDs pointing at the same prefab are not presented as distinct choices.
- One actual supplied look is labelled **Everyday**. A single look has no previous/next arrows; the browse controls appear when distinct, authored complete-look prefabs exist. No claim of editable hairstyles, clothes, body shapes or skin tones is made by this screen.
- Separate **Look**, **Personality**, and **Identity** pages replace the long form underneath a small preview. The first page gives the model the remaining screen height, with a **Full body / Face** control and captured drag-to-turn gesture. Face framing uses the Humanoid head anchor when available and includes the measured top of the hair. The camera stops rendering on the other pages. Alias, age confirmation, tribes/perks, two traits and dream remain implemented.
- Submission capability is independent of local preview availability: the constructor accepts optional `Func<string[], bool> serverLookConfirmed`. The selected seven-ID draft is checked again immediately before invoking the existing callback. It also must match starter IDs and slots in the supplied registry. A missing/false capability leaves the final action unavailable with explicit unsaved-state copy. Root owns confirming against the unmerged live server response; a bundled or FE-merged snapshot is insufficient evidence.
- `readOnlyReview: true` permits local preview completion without invoking the character-create callback. The action and completion copy explicitly say no character is saved. Empty catalogs show an unavailable preview, while the personality/identity tabs remain inspectable.
- Removed the automatic wave on every look change so the model can be inspected in its idle pose. Existing `AvatarPreview` constructors and `SetLook`/`Spin` APIs remain; `SetPortrait`, `SetYaw` and `SetVisible` are additive.

### Integration and remaining scope

- Root owns Snow Humanoid import, the exact full-key `CharacterRegistry` entry, the UI catalog asset, review caller setup, live-config capability plumbing and FE registry proposal. New proposed Snow look IDs must not be submitted until the backend accepts them. The backend seven-ID payload structure is unchanged; this agent did not edit the shared contract.
- This single imported asset is a quality checkpoint. It does not complete the product's modular customization requirement. Additional real, skin-weighted/atlas-compatible parts and confirmed registry mappings are still required before slot-level controls can return.
- Checked pointer capture/release/cancel, `PointerCaptureOutEvent`, `Animator.avatar`, `Animator.isHuman` and `GetBoneTransform` against installed Unity 6000.3.25f1 XML documentation. Static diff checks pass. Compilation and actual Seeker framing/touch/step/gate validation remain with root; no device success is claimed for this iteration yet.

## Presentation redesign after the user's rejection

The earlier ivory/forest palette, hand-drawn icons and large bottom dock are superseded. Actual Pinterest images and publisher screenshots were viewed first; the five references and observations are in `ui-visual-research.md`. This direction is a design recommendation pending inspection, not a claim of user approval.

### Saved for root's next compile and device review

- Mineral/offwhite, deep ink/aubergine and one lime primary accent replace the old palette. Shared USS uses actual bundled Manrope regular and semibold TTFs; headings and utility labels have less competing emphasis. Financial gain/loss colours remain semantic.
- `UiIcon` now maps every current phone app and utility to a distinct sourced Phosphor regular icon. There are 34 white-on-transparent 192px rasters for 35 semantic keys; bank/wallet share the wallet symbol. Viewed the complete raster contact sheet at `/tmp/ct-ui-source-20261005/icon-sheet.png`. SVG paths are unchanged and runtime packages are not added.
- The HUD has two independent corner clusters (chat/emotes/mic and map/phone), compact cash/location/status, and expandable needs. The middle and empty container areas pass world input through. All existing handlers remain. Main controls retain >=124 reference-canvas-unit targets.
- The creator is now a compact brand/step rail over a large neutral model stage. Face/body controls sit on the stage edge, and look/status plus one primary action sit below. Pointer-down on the focus controls is excluded from stage drag capture. Preview uses the high-quality `forPreview: true` prefab path and a portrait 720×1080 RenderTexture. The preview camera remains isolated and single-sampled; world MSAA is unchanged.
- A real authored portrait appears on sign-in when an exact catalog look exists, replacing the previous text-only hero. It uses the same preview rig and disposes its camera/texture on close. Wallet/demo availability and callbacks remain intact.
- The phone is inset so the city remains visible around it; three-column app cells use the same icon family and quiet surfaces. Available and upcoming apps remain separate, and phase/degraded gates still apply. Generic per-app colours are removed.
- Phone open/close emit the sourced `sfx_ui_open` / `sfx_ui_close` only on actual shell transitions. The back-to-home action has its close cue. HUD phone/map launch, phone back/close, map travel and sign-out no longer stack a generic tap over the transition sound.
- `CanSubmitLook` now relies on the live selected-look callback only. The stale merged registry acceptance method is removed. Creator subscribes to `ConfigStore.Changed` while shown to refresh status/availability, unsubscribes on close, and checks again immediately before submission. Read-only review still never invokes creation.
- Settings has an expandable **Credits & licenses** surface. Snow's source attribution, CC-BY 4.0 terms and modification notice, Manrope OFL and Phosphor MIT notice ship as local TextAssets. Source links open only when the player chooses them. Long license text is split into wrapping paragraphs.

### Source and validation details

- Manrope upstream `googlefonts/manrope` commit `468c0dbe38efa331b80bfe9448256abe27be44c3`, official static `manrope-regular.ttf` and `manrope-semibold.ttf`, unmodified. OFL text retained.
- Phosphor upstream `phosphor-icons/core` commit `2b75f3ad12b420c9504ef05df8d2564a28f8500e`; regular SVGs rasterized offline with sharp 0.35.5. Tool version/engine support checked against its live npm registry and official install docs. Tool dependencies stay under `/tmp/ct-ui-source-20261005`; none enter the Unity project.
- Full source URLs, mapping, source SHA-256 hashes, notices and raster recipe: `client/Assets/Resources/UI/THIRD-PARTY-NOTICES.md` and `asset-sources.json`. Root is asked to link these into the central `art/LICENSES.md` inventory.
- Snow credit wording comes from the actual integration asset README; no endorsement is claimed. The Creative Commons canonical legal-code page was verified with the web tool; direct local download returned HTTP 403, so the identical license edition was obtained from Creative Commons' official `legalcode` repository. Download URL and hash are recorded.
- Verified font/image USS asset syntax against Unity 6.3 official manual; verified `Background.FromTexture2D`, `FontDefinition.FromFont`, `unityFontDefinition` and precedence against installed 6000.3.25f1 API XML. No Unity editor or device command run by this agent.
- `git diff --check` passes. Actual Unity import/font rendering, device fit, face/body/drag, needs, credits and final character performance remain to be checked by root. The current one-look catalog is still a quality checkpoint, not completion of modular customization.

### Build F actual Seeker review

Viewed all six actual `quality-review-20261005f` captures. Manrope and Phosphor are present, phone/map text stays inside its panel, and the HUD corner clusters leave the middle free. The model has a clearly wrong raised-arm/crossed-leg pose and darker lighting than the source render; the creator shows too much empty stage. Root owns animation, preview framing and lighting corrections for the next build. No AvatarPreview edits were made during this follow-up.

UI corrections saved from the actual evidence:

- A separate full-bleed screen backing sits above HUD/world labels and below the safe-area screen layer. It appears while a full-screen flow is shown, hides on close, covers the visible world strip at the top and captures input outside the safe content bounds. Control insets remain intact.
- Personality chips now have the same minimum 124-unit target as the other primary controls.
- Sign-in headline is reduced from 88 to 64 units and shortened to one line; supporting copy is concise. Wallet/help/age actions sit at the bottom, while the real portrait fills the flexible scrollable hero area. Portrait cover-cropping and final face readability require the next actual capture after the pose correction.
- Verified `RichListApp.Id` is `richlist`, already mapped to the source `ranking` icon. In the device capture, that podium resembled buildings. Replaced it with official Phosphor `list-numbers` and added `rich_list` as an alias. Source commit, SHA-256 and mapping are updated in the asset manifest; no invented icon drawing.
- `git diff --check` remains clean. No new device success is claimed for these follow-ups yet.

### Build G review

Viewed actual creator, sign-in, city, phone and bank captures. The safe-area backing is continuous; the creator now uses a readable full-body scale after root's animation/framing correction. The displayed back of the character is still wrong for entry/creation, and edges remain undersampled; root owns orientation and preview render-resolution changes. The new sign-in headline/footer layout fits, with the portrait as its main visual. Its final face crop must be judged after the front-view correction.

One additional concrete UI issue was visible: phone panels cut the Self-care HUD card in half and left a partly visible Chat button interactive underneath. `PhoneShell` now hides the HUD layer on a closed→open transition and restores its exact prior display style on close. HUD subscriptions/feature state are not destroyed or reset. This small follow-up awaits the next device run; no AvatarPreview changes were made by this agent.

### Build H Settings follow-up

Viewed actual H creator, sign-in and phone captures plus root's `/tmp/ct-settings-h.png`. The front-facing full character fits the creator stage, sign-in face/actions fit, and the phone no longer shows a partly obscured HUD. These images do not establish live creation/authentication or overall presentation approval.

- Removed the repeated inner Settings heading; the phone bar remains its title. Audio labels, credits, version and sign-out remain.
- Shared `.ct-slider` treatment puts each label above a full-width 124-reference-unit input/drag area, with a rounded 12-unit track and 64-unit circular handle. Colours use the existing tokens. Focus/hover uses the lime accent.
- Enabled Unity's native `Slider.fill` so active-track width derives from the actual 0–1 value. Existing audio setter callbacks are unchanged; there is no separate progress value or artificial fill.
- Confirmed `fill` and all BaseSlider selectors in installed Unity 6000.3.25f1 XML and the [official 6.3 Slider manual](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-uxml-element-Slider.html). `git diff --check` passes. Root's I build must check visual alignment, endpoint dragging and gain persistence; no editor/device commands were run by this agent. AvatarPreview was untouched.

### Build I device review

- Viewed all eight actual I main captures, five Settings touch captures and five creator touch captures under `screens/quality-review-20261005i/`. The saved build receipt records 179,338,404 bytes (171 MiB), zero build errors and 29 warnings.
- Settings shows one title and the new rounded native sliders. SFX images show the original maximum, minimum, near midpoint, that same near-midpoint position after process relaunch, and restoration to the original maximum. Root clarified the exact procedure: force-stop `com.ctlife.app`, start the review phone afresh, wait 12 seconds, reopen Settings and capture. The near-midpoint setting persists through this tested relaunch; root restored the exact SFX endpoint 1 and left Music 0.7 untouched. Fill terminates at the handle in each capture; labels, credits, version and sign-out fit. This is not a listening test.
- Creator images show back/front rotation, face focus, preserved face mode/rendering after Android Home and resume, then full-body mode restored. `touch/resume.txt` records PID 31634 both before and after. This tests one same-process resume; it does not establish every low-memory/context-loss path.
- No additional blocking layout issue was identified in creator, sign-in, phone, map, bank or the three city captures. The development review strip remains explicitly labelled sample data. These observations do not validate live auth, character creation or transactions.
- The creator's wide-legged, curled-hand idle is still a character-animation limitation. Root/artist are preparing a separate Snow-native idle for J; no UI source edits were made during this review, and J's actual character evidence remains pending.


## Latest user correction: tactile game identity and lobby

The user rejected the mineral interface, including buttons. Its earlier device checks do not constitute design approval. The new implementation and actually viewed reference images are documented in [game-ui-direction-20261005.md](game-ui-direction-20261005.md). Build K visual/touch verification is pending: raised native controls, original city-diorama lobby, actual profile resources, filled/bold glyphs, Lilita One display type, movement control area and full-bleed phone dismissal scrim. No full-customization, final-cast, financial-feature or final sound-composition completion is implied.

K actual layout captures were inspected (all seven screens). Dashboard art/resources/action fit; phone/map/bank copy did not clip in the sampled data. A pale lobby unsafe strip was corrected per screen; phone icon tones and keyboard focus differentiation are queued for K2/L. The K city screenshot cannot validate movement because the development strip was incorrectly in ModalLayer; root corrected the harness. See the tactile direction document for exact images and remaining touch checks.


## K2 verified touch follow-up / L queued correction

Viewed the verified analog run/release, Settings movement selection, arrow mode after process relaunch, outside phone dismissal, held-arrow walk, outward-drag run and neutral pause/resume captures. Settings and HUD controls fit. Valid pressed CTA evidence is `touch/enter-pointer-down.png` (depressed lighter CTA, still lobby) then `enter-pointer-up.png` (city after release). The earlier stationary-swipe `lobby-enter-pressed` image and stale-lobby analog filenames are excluded.

A real overlap was found between Walk/Run and the DOWN arrow. The shared caption now sits above the 420px cluster; all control hit regions are unchanged. L must verify it together with root’s closer camera/contact-shadow changes. Details and exact image links are in the current game UI review. No final presentation approval is claimed.


L primary layouts (seven screens plus direct-walk/run) were viewed: movement caption is clear above the controls; closer camera enlarges the player without moving feet into HUD; Snow remains fully framed, and the new 1-of-2 look browser fits. Lobby/phone/map/bank/sign-in show no new blocking clipping in the sampled data. Breeze selection/world and installed Rain credits require separate evidence/L2. See the current game UI review for exact paths and limits.
