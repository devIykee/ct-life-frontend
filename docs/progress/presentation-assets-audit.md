# Presentation assets audit — 2026-10-05

Scope: existing icon drawing, actual audio files/source metadata and runtime wiring. No Unity instance or device audio capture was run by this agent. This audit installed no icon pack, package or account. The authorized narrow audio corrections are now implemented and compile-checked; listening and device acceptance remain open.

## Icon recommendation: Phosphor

Use a deliberately selected subset of **Phosphor**: one coherent family, Regular controls plus Duotone phone tiles if the visual direction approves that distinction. Import source SVGs at a pinned revision, rasterize offline into transparent sprites, and pack a Unity UI atlas. The repository offers Thin/Light/Regular/Bold/Fill/Duotone SVG assets; its manifest currently reports core2.1.1. No runtime SVG package or icon font is needed. [Official repository](https://github.com/phosphor-icons/core), [manifest](https://github.com/phosphor-icons/core/blob/main/package.json).

The official MIT license permits use, modification and redistribution with the copyright and permission notice included. Keep the full notice alongside source assets and in shipped third-party notices. Record each selected source path/revision and generated raster outputs in the art license manifest. [Official MIT license](https://github.com/phosphor-icons/core/blob/main/LICENSE).

Read-only GitHub tree query confirmed revision `2b75f3ad12b420c9504ef05df8d2564a28f8500e` and all proposed filenames below in both `assets/regular/<name>.svg` and `assets/duotone/<name>-duotone.svg`. This is a verified available subset, not a claim that its Unity raster quality has been reviewed.

| Current semantic key | Proposed Phosphor asset |
| --- | --- |
| close / back / next | x / caret-left / caret-right |
| map / phone | map-trifold / device-mobile |
| chat / messages / timeline | chat-circle-dots / chats-circle / newspaper |
| mic / spaces / calls | microphone / users / phone |
| emote / home | smiley / house |
| bank / wallet | bank / wallet |
| jobs / projects | briefcase |
| profile / contacts | user-circle / users |
| settings / ore / city | gear-six / cube / buildings |
| richlist / league | trophy |
| pulse / perps / predict | chart-line-up / chart-line / chart-pie-slice |
| topup / shop / ads | plus-circle / shopping-bag / megaphone |

Author controls for their actual optical size, not just a shared bounding box. A proposed first export is128px transparent monochrome/duotone sprites for controls and256px only where the display requires it; inspect native Seeker screenshots before finalizing density. Preserve alpha, use consistent padding, avoid ASTC block artifacts on thin edges, and retain accessible text labels/adequate hit areas. These dimensions are design proposals, not measured final requirements. Existing `ctlife.uss` uses56px generic symbols,76px app glyphs and a144px splash symbol within the current reference-coordinate UI.

`client/Assets/Scripts/UI/UiIcon.cs` currently draws handcrafted line/circle approximations through Painter2D. Several meanings share one drawing: chat/messages/timeline, microphone/spaces/calls, bank/wallet. The default is a rising graph, so richlist, pulse, perps, predict, topup, shop, ads and league lack distinct symbols. Replacing the asset family does not by itself repair spacing, typography or information hierarchy; the UI design pass owns those choices.

## Icons8 findings

Icons8's public free-use page permits free app use with credit/link in About or Settings and store-description credit. Its documentation limits most free downloads to PNG up to100×100, with exceptions for particular free collections; the current pricing page confirms free format/size restrictions. [Free-use page](https://icons8.com/license), [download limits](https://icons8.github.io/icons8-docs/web-app/licensing/), [current pricing FAQ](https://icons8.com/icons/pricing).

The linked full agreement is more restrictive: free users cannot create derivative copies except through specified Icons8 tools, and prohibited uses include “In gambling, betting, and lottery services, applications, nor events” and NFTs. **Recommendation: do not select Icons8 for CT Life**, whose agreed features include a birth lottery and prediction markets. This recommendation does not classify the product legally; it avoids adopting assets with directly relevant restrictions when MIT alternatives exist. [Official full agreement, dated27November2025](https://intercom.help/icons8-7fb7577e8170/en/articles/5534926-universal-multimedia-license-agreement-for-icons8).

## What is actually in the audio build

Read-only SHA-256 comparison found **923 mapped playback files**, all present under `client/Assets/Resources/Audio` and byte-identical to `audio/processed`. The total library has930 audio files:73 effect/footstep files,42 music files,9 ambience loops and806 babble syllables. The extra7 music files are full-mix previews rather than runtime stem references.

| Material | Verified source/code state |
| --- | --- |
| Music | Seven sets, each five synthesized stems plus a full mix. Entirely authored in Python (`audio/tools/gen_music.py`, `instruments.py`), not recorded musicians or curated commercial compositions. |
| Babble |31 banks ×26 formant-synthesized syllables. Not human voice recordings. |
| SFX |32 entries:22 synthesis-only,10 containing identified Kenney/Freesound recordings. |
| UI | Tap/toggle/error/success/toast are synthesis; open/close use Kenney `maximize_003.ogg` / `minimize_003.ogg`. |
| Footsteps |36 variants across six surfaces; existing Kenney concrete/grass/wood/carpet plus sourced tile/metal cuts. |
| Ambience | Field-recording mixes plus synthetic hum/beeps/club layers, generally30–34s loops. |

Evidence is in `audio/sfx-map.json`, `audio/tools/build_*.json`, `audio/LICENSES.md` and the actual files. `docs/progress/audio.md` explicitly says **nothing was auditioned by a human** in the original delivery. Its measurements (music full mixes about−17LUFS, ambience about−24LUFS, peak/loop checks) are technical checks, not evidence of taste or of the combined runtime mix. This audit also makes no listening-quality claim.

The already-downloaded Kenney **Interface Sounds** and **UI Audio** packs provide a coherent sourced candidate pool for taps, selection, toggles, back, confirmation and error. Only two Interface files currently contribute to the UI build; UI Audio is downloaded but unused. Both official pages mark the assets CC0, and Kenney explicitly permits commercial use without mandatory attribution. Audition a small matched family before replacing the current sounds. [Interface Sounds](https://kenney.nl/assets/interface-sounds), [UI Audio](https://kenney.nl/assets/ui-audio), [Kenney license FAQ](https://kenney.nl/support). Existing footwear Foley comes from the CC0 [Impact Sounds](https://kenney.nl/assets/impact-sounds) pack.

I reopened the actual [square crowd recording](https://freesound.org/people/tferrino/sounds/640625/) and [café recording](https://freesound.org/people/Anya_Media/sounds/437461/); both pages currently display CC0. The square source explicitly contains Portuguese conversation, so intelligibility and fit require listening. The remaining32-source ledger was read, not individually re-audited in this pass. Existing downloads are HQ Ogg previews, per the source ledger, not original recordings.

## Audit findings before the authorized corrections

1. **Footsteps are absent despite shipped assets.** `SfxMap.Footsteps` is parsed but no caller consumes it. `AudioManager.PlayAt()` only resolves the SFX dictionary. Player/NPC movement does not emit footstep events. Proposed narrow correction: local-player distance/cadence trigger using actual horizontal movement, with idle/teleport suppression and non-repeating Kenney variants. Later real clips can supply foot-contact events.
2. **Venue changes cut audio.** `MusicDirector.SetDistrict()` immediately stops every old stem, then schedules the new set at DSP+0.2s. `AudioManager.SetAmbience()` swaps one source immediately. Use two banks/sources for a short coordinated crossfade, retaining scheduled alignment of music stems and the existing voice/stinger ducking.
3. **The sound slider does not mute NPC banks.** `BabblePlayer.Update()` uses only `bank.volumeDb`; it never multiplies by `AudioManager.sfxVolume`. Fallback `Babble.cs` does multiply it, producing inconsistent settings behavior. Existing Settings exposes music and SFX only; ambience has no control and settings currently are not persisted in this code. A fix must keep all named sound categories consistent and not reset user choices on transition.
4. **The spatial listener follows the distant camera.** `CameraRig.Ensure()` attaches an AudioListener to the camera; Timeline Square's authored orbit distance is52m. `BabblePlayer` positions3D sources on NPCs with minDistance2/maxDistance15. [Inference] This makes local conversational attenuation depend on camera framing, not player proximity. Device audibility has not been measured. Proposed correction: one listener follows the local player while preserving camera-facing spatial orientation, with a defined fallback while no player exists.
5. **Sourced UI transitions are not wired.** `PhoneShell.Open/Close()` do not publish `sfx_ui_open/close`; buttons use generic tap. Coordinate a single cue per transition with the UI owner to avoid tap+open/close stacking. Toggle aliases always resolve `sfx_ui_toggle` to the on sound, including deselection.
6. **Pool size is not a voice limit.** `AudioManager.NextSource()` picks a busy source when all eight are busy, then calls PlayOneShot, which can overlap. There are no category priorities/cooldowns. `PlayAt()` allocates through PlayClipAtPoint. Measure real crowd audio rather than claiming an eight-voice budget.
7. **Mix behavior remains unverified.** Music starts at master0.7, ambience0.6, SFX1.0 plus per-ID gain. The full-mix measurements do not cover UI bursts, babble or simultaneous ambience. Ring-loop metadata is not consumed by generic Play. Unknown IDs use an audible synthesized blip. These need an actual phone-speaker/headphone listening pass, not another generated-music batch.

## Implemented runtime corrections

`AudioManager.cs` now uses two ambience sources with a one-second fade. `MusicDirector.cs` uses two five-stem banks with a 1.2-second fade, loading each set before choosing its shared DSP start. The outgoing bank keeps playing until the incoming scheduled start; a return to a still-fading set reverses the fade. A rapid third selection reclaims the quieter bank. Exiting a venue fades both music and ambience out, and zero-weight sources stop and release their clip references. The existing clip cache remains in use; this is not an asset-memory unload change. Music still applies the existing −6 dB voice duck and −9 dB stinger duck. The transient music budget is ten scheduled sources, plus two ambience sources during transition; crowd audio still requires profiling.

`BabblePlayer.cs` now applies the current SFX gain to all four voices every frame, including the last syllables after its queue empties, and stops on disable. Fallback `Babble.cs` re-reads the gain for each syllable. `AudioManager` persists music/SFX/ambience gains through PlayerPrefs, with a debounced save and pause/quit flush; the existing Settings property names continue to work. No new Settings controls were added by this agent.

The final live-mute correction puts the SFX master on each pooled `AudioSource.volume`; `PlayOneShot` receives only entry gain × caller gain. This applies to already-playing effects and unknown-ID fallback sounds without multiplying the master twice. `PlayAt()` now uses the same eight-source pool instead of allocating an untracked source. When all eight sources are busy, the oldest-started source is stopped before reuse. Positional sources reset to2D when subsequently used by `Play()`. Source search found no current external `PlayAt()` callers, but its mute/voice-limit behavior now matches the rest of the API.

`CameraRig.Ensure()` only gained listener registration; camera composition and transforms are untouched. `LocalPlayerController` binds one persistent audio listener at the avatar's existing voice/head anchor and releases it on disable. The camera listener is the fallback before/after the local player exists. Listener orientation follows the camera's horizontal facing, while its position follows the player. Release also stops local footstep tails.

`FootstepCadence.cs` integrates actual horizontal travel, suppressing idle, invalid samples, long gaps and warps. Spawn, server snap, seating and disable reset it. Initial walk/run spacing uses the checked clip manifest durations (1.33/0.67 seconds) and current movement speeds (2.2/4.5 m/s), assuming two plants per cycle; this is an initial calibration, not validated foot-contact timing. `AudioManager.PlayFootstep()` uses existing concrete Foley, avoids the immediately previous variant, and limits local footstep playback to four reusable sources. **Scope limit:** only the local player emits footsteps, and its initial surface is concrete; material-aware floor selection and NPC crowd footsteps are not implemented.

The UI owner wired `sfx_ui_open` / `sfx_ui_close` in `UI/Phone/PhoneShell.cs`; UI files were not edited by this agent. No music, babble or effect recording was replaced, generated, purchased or downloaded in this correction pass.

### Verification performed

- Pinned Unity 6000.3.25f1 Roslyn compilation passed Core (20 files), Net (10), Audio (6) and World (25), against the current project assembly references, including `DEVELOPMENT_BUILD` diagnostics. Final isolated output: `/var/folders/zm/v0w94xb95p11_5df6rxm0js80000gp/T/ctlife-audio-final-compile-ymo9tmay`. No Unity process was launched.
- Final standalone .NET regressions passed **658 assertions**:644 for walk/run at30/60/120Hz, idle jitter, warp/invalid-time suppression, shared DSP start, A→B→A reversal, A→B→C cancellation, active-bank mute, −6dB gain and exit cleanup;14 for existing-SFX mute, single master multiplication, unknown fallback, pooled positional playback, oldest-source stealing and3D→2D reuse. The cadence/music harness compiles their actual source; the SFX harness extracts the actual relevant `AudioManager` methods. Both mock Unity audio sources, so neither verifies audible fades or Unity's audio thread. The SFX harness compilation emitted six unused mock-field warnings, with zero errors. Final scratch sources/executables: `/var/folders/zm/v0w94xb95p11_5df6rxm0js80000gp/T/ctlife-audio-final-tests-e6954ub2/`. Run `CadenceMusicTest.dll` and `SfxTest.dll` there with the pinned editor's `Contents/Resources/Scripting/NetCoreRuntime/dotnet`.
- Changed-source `git diff --check` passed. The repository-wide check reports existing generated `client/client.slnx` CRLF whitespace; that file was not changed by this agent.
- APIs were checked against the installed editor's `UnityEngine.AudioModule.xml` (`PlayScheduled`, `PlayOneShot`, source volume, `AudioListener`, spatial blend and source distance properties) and `UnityEngine.CoreModule.xml` (`FindObjectsByType`). The local `PlayOneShot` documentation explicitly describes its gain as scaling the source volume. Source availability and registry paths were checked locally; no runtime audio package was added.

### Development snapshot for device review

Call `AudioManager.Instance.ReviewSnapshot()` from the review controller, guarded by `UNITY_EDITOR || DEVELOPMENT_BUILD`. This on-demand method logs nothing itself and performs no per-frame object search. It reports:

- `listenerActiveCount`, listener/player/anchor positions and `listenerAnchorError`: expect exactly one enabled listener and an error near0 after LateUpdate while the player is active. Before player creation/after release, the camera listener is the intended fallback and no player anchor is expected.
- `sfxVolume`, `sfxPlaying`, `maxPlayingSfxSourceGain`: while a long effect remains active, setting Sound to0 should keep its source active but report source gain0. Unknown fallback and positional effects use the same master.
- `footstepsPlayed` (cumulative successful local plays) and `footstepsPlaying`: compare snapshots during idle, walking, running, server snap and venue replacement; muted footsteps do not increment the played counter.
- Music target, active bank count, transition count and each bank's key/weight/playing-source/clip counts/start delay; ambience target and both source clip/weight/playing states. During transition, up to two banks can be loaded; after settling expect one requested music bank, and after exit-fade completion expect zero. Loaded banks/playing sources are engine state, not proof of audible quality.

## Required device acceptance

The source corrections and compiler checks are complete. Keep current compositions intact until a listening-driven replacement direction is chosen. No audible-quality claim is made for these changes yet.

Coordinator checks on Seeker: tap/open/close each plays once; sound/music controls mute the intended categories; stand still produces no footsteps; walking/jogging matches motion without double taps; teleport/venue spawn creates no step burst; Timeline→Terminal→Timeline audio crossfades without an old loop lingering; NPC speech remains located near the player while camera zoom/pan changes; and voice activity retains−6dB music ducking. Record a short device audio/video sample and separately review the music, babble, foreground/background balance and repeated UI cues. None of those audible checks has passed merely because files exist or loudness numbers look reasonable.
