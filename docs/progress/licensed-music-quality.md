# Licensed music and UI sound pass — 2026-10-06

**Current source checkpoint: ready for root's native audio import; not listening-approved.** This pass replaces the six remaining synthesized district selections, adds a quieter-textured café selection, and replaces six frequent synthetic UI cues with sourced Kenney effects. The previous K handoff is retained below as history. No Unity/editor/adb run or client Resources copy was performed by this agent.

## Complete music selections

All nine music-map keys now use full arrangements: eight distinct compositions, with lobby/Timeline sharing Funky Chunk and retaining its continuous playback phase. The original native two-bank transition, user gain and voice −6 dB duck remain. Spaces Studio still selects no music; no call-hold bed or fabricated adaptive stem was added.

| Location | Composition / official source | Composer-listed tempo | Decoded length | Measured LUFS |
| --- | --- | ---: | ---: | ---: |
| Lobby / Timeline | [Funky Chunk](https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1500054) | 115 BPM | 239.020 s | −18.0 (K measurement) |
| Trenches | [Got Funk](https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1200005) | 98 BPM | 281.078 s | −18.00 |
| Perp Pit | [Getting it Done](https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1800004) | 135 BPM | 209.842 s | −18.04 |
| Builder Hills | [Pamgaea](https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1300036) | 94 BPM | 168.568 s | −17.94 |
| Dubai Marina | [Paradise Found](https://incompetech.com/music/royalty-free/index.html?isrc=USUAN2300000) | 105 BPM | 187.481 s | −18.03 |
| Clubs | [Disco con Tutti](https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1200091) | 115 BPM | 236.352 s | −18.05 |
| Airdrop Farm | [Easy Lemon](https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1200076) | 82 BPM | 126.380 s | −18.25 |
| Terminal Café | [Airport Lounge](https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1100806) | 129 BPM | 307.670 s | −18.40 |

Selection intent follows the composer's official metadata: bass/horns for the Trenches; light dance electronics for Perp Pit; relaxed synth contrast for builders; tropical house at the waterfront; bright disco for clubs; guitar/mallet texture on the farm; electric-piano/vibraphone in the café. This is an art-direction rationale, **not an audition or proof that players find the tracks enjoyable/non-fatiguing**. This agent has no audio-listening capability. The original exact genre/stem targets are not claimed: these are not phonk, banjo lo-fi or Amapiano replacements in name only. The user's brighter social direction takes precedence for this selection pass; the [FE-only proposal](../../audio/proposals/presentation-audio-20261006.md) records the distinction.

The official [Incompetech licence page](https://incompetech.com/music/royalty-free/licenses/) was reopened and its current HTML retained. It offers no-charge credited use; the credit generator specifies [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/legalcode.en). The checked CC0 music source [FreePD remains closed](https://freepd.com/); this is not a claim that all CC0 compositions are unavailable. All seven new MP3s came directly from the composer's public download endpoint, without purchase or account. Each has a licence ledger row, download/source hash and processing record. The unused Funkorama candidate's historical ledger row is preserved.

Seven new files add **32.961 MiB** before Unity's import compression. They retain complete arrangements, original intros/endings and tempo; only constant gain and format/rate conversion are applied. Their repetitions are **not advertised as seamless musical loops**. Direct decoded PCM measured zero full-scale samples. Sample peaks range from −7.39 to −2.51 dBFS; the independent loudness scan's true-peak estimates range from −7.36 to −2.45 dBTP. The small extra attenuation on Easy Lemon/Airport Lounge preserves peak margin. Each MP3 produced only the demuxer's bitrate-based duration-estimation notice, with no overread/corrupt-frame warning. Reported durations use decoded samples; processed Ogg decoding was clean. These figures do not measure the summed game mix or Android-recompressed playback.

[Exact metrics and hashes](../../audio/sources/incompetech/district-music-metrics-20261006.json), [composer metadata](../../audio/sources/incompetech/catalog-selection-20261006.json), [licence ledger](../../audio/LICENSES.md).

## Sourced everyday feedback and bounded playback

[Kenney Interface Sounds](https://kenney.nl/assets/interface-sounds) is officially CC0 and the already downloaded pack contains its licence. The six replacements retain existing FE SFX IDs:

- Tap: `click_003`, 7.1 ms, maximum post-entry peak approximately −19 dBFS.
- Toast: `tick_002`, 20.3 ms, approximately −21 dBFS.
- Toggle on/off: `toggle_001` / `toggle_002`, 136.4 ms, approximately −20 dBFS.
- Success: `confirmation_001`, 289.9 ms, approximately −17 dBFS.
- Error: `error_001`, 161.7 ms, approximately −20 dBFS.

These use distinct source cues for success and error, fixed pitch, restrained entry gain and unchanged source duration. Their perceived distinction must still be checked by listening. Output uses 48 kHz mono PCM16; root's existing importer applies short-SFX Vorbis quality 0.6, DecompressOnLoad and force-to-mono. Float attenuation occurs before PCM16 encoding; some lossy source decodes exceed unity, so their measured positive peaks are not copied into clipped output. All six outputs have zero full-scale samples. [Cue metrics](../../audio/sources/kenney-ui-metrics-20261006.json) and [curation](../../audio/sfx-curation.json) retain the exact source files and gains. Phone open/close and concrete footsteps remain their prior sourced cues.

`AudioManager` now ignores duplicate instances of the same UI ID within 60 ms and limits UI playback to two voices within the existing eight-source pool. Different error/success/open events still have different IDs; rapid UI events cannot accumulate eight overlapping UI transients. The regression exercises an active stinger beside rapid menus. This does not suppress different tap/open IDs fired by the same UI action: the UI owner identified Dashboard resource/player/settings/story cards as that remaining double-cue case, and root owns that narrow integration change. No movement-drag audio spam or speculative new event routing was added.

A concrete mute defect was also corrected: a muted stinger previously continued to reduce music by 9 dB. SFX mute now releases that stinger reduction immediately; voice duck remains −6 dB. Silent/zero-gain stinger calls do not start a new duck interval, and overlapping stingers retain the later end time. Existing active-source SFX gain updates and persisted settings remain.

## Native import handoff and verification

**Final payload receipt:** [presentation-handoff-20261006.json](../../audio/sources/presentation-handoff-20261006.json). Final source-map SHA256: `990e47c89c83308d408d2fb6a141b3ffb3f12a981646621a95acb84f33272496`.

Root copies the listed fifteen processed paths (eight music files including the existing Funky Chunk, six UI WAVs, revised Music-Credits.txt), preserving paths under `Assets/Resources/Audio/`, and copies `audio/sfx-map.json` there. **Do not run ContentImport.SyncAll:** it recreates the shared avatar controller. Existing music import stays Streaming/Vorbis quality **0.45**, preload false, background load true. Informational curation metadata now matches that actual policy. Root also owns the small `credits.musicSummary` text change to “Music by Kevin MacLeod (incompetech.com), CC-BY 4.0. Full track credits and adaptation details below.” The detailed offline credits already name all tracks/sources/modifications.

Old generated stems/UI assets remain on disk for history; the new map selects none of the generated music stems. This pass does not silently delete their Unity Resources assets, so it makes no APK-size saving claim. `mus_terminal_cafe` is explicitly recorded as a proposed FE-only music selection, not a new backend sound/event ID. No contract snapshot was edited.

Verification: Audio assembly compiled against pinned Unity 6000.3.25f1 references with zero errors (known response-file noconfig warning). Twenty-four mocked-audio assertions exercised actual extracted SFX methods: live gains, spatial pool reuse, same-ID duplicate suppression, two-UI-voice bound beside a stinger, ordinary later taps, silent stingers, live mute and independent voice duck. The existing 687 MusicDirector/cadence regression assertions also passed; MusicDirector source itself is unchanged. JSON checks confirmed all nine music keys are full-mix, all files exist, all six UI paths are distinct, and Spaces has no music entry. These are source/runtime-state checks, not a hearing claim.

Root/device follow-up: verify imported streaming/load policy and final map hash; sample lobby→Timeline phase continuity, café/district transitions and Spaces silence; test rapid UI, active-stinger mute and speech duck; listen to the mix through phone speaker/headphones including one complete track ending/restart. No subjective audio approval is asserted.

---

# Historical K handoff — 2026-10-05

The user explicitly rejected the existing music as depressing. The lobby and Timeline now
select Kevin MacLeod's **Funky Chunk**, a complete 239.020-second composition, instead of the
38.4-second synthesized Timeline stems. This is a source-content replacement as well as a
playback change. The other district entries still use their previous stem assets.

## Source selection and rights

The current [Incompetech catalogue](https://incompetech.com/music/royalty-free/pieces.json)
lists Funky Chunk at 115 BPM, with bass, electric piano, drums, guitar, synth and horns, and
tags it Bright/Grooving/Uplifting. The official track identifier is USUAN1500054:
[track page](https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1500054),
[direct original MP3](https://incompetech.com/music/royalty-free/mp3-royaltyfree/Funky%20Chunk.mp3).
The dynamic track pages did not expose their data through the web tool; the exact live
catalogue JSON and HTML licence declaration were fetched directly from the same official
site and retained in `audio/sources/incompetech/`.

The [official licence page](https://incompetech.com/music/royalty-free/licenses/) offers
Creative Commons use without charge when credited. Its credit generator names CC BY 4.0.
The [CC BY 4.0 legal code](https://creativecommons.org/licenses/by/4.0/legalcode.en) permits
commercial sharing/adaptation with attribution, licence reference and modification notice.
The shipped `Resources/Audio/Music-Credits.txt` names the track/composer, source, licence and
level/format changes; Settings → Credits & licenses exposes it alongside the existing
offline CC BY 4.0 text. No purchase, signup or account was used.

CC0 was checked first: [FreePD's official homepage](https://freepd.com/) says the service is
permanently closed. The documented CC-BY route was used for this replacement. Lobby Time
was also checked; its creator-described retro/relaxed arrangement was a weaker match for
the requested direction. Funkorama was downloaded as a candidate, but its official MP3
produced 13 decoder overread-warning lines. It remains under `audio/candidates/`, outside
processed assets and Unity Resources. Those warnings are not proof of an audible defect;
they were sufficient reason to prefer the clean-decoding Funky Chunk source for this pass.

**No listening claim:** this agent has no audio-listening tool. Selection uses the creator's
description and source/technical checks. The actual composition, phone-speaker balance,
loop transition and taste acceptance still require listening by the user/root's review.

## Actual asset

- Original source: `audio/raw/incompetech/Funky Chunk.mp3` (7,650,907 bytes; raw folder ignored).
- Original SHA-256: `e6203c8c8fddbae0d9790da391a6ea73ef765fc3eec08bd87441e63037dc3c9c`.
- Runtime source: `audio/processed/music/licensed/mus_timeline_funky_chunk.ogg`, copied byte-for-byte
  to `client/Assets/Resources/Audio/music/licensed/`.
- 48 kHz, stereo, 239.020417 seconds; Vorbis quality 6; 6,508,915 bytes (6.21 MiB).
- Processed SHA-256: `36a1578eda958a918faabfe7148ba88faced4ba3ab2f8fcf44ec975df8fecd5c`.
- Constant −4.57 dB gain only. No tempo change, musical cuts, artificial stem extraction,
  dynamic loudness processing, or fabricated composition. Original intro and ending stay.
- Decoded processed-file FFmpeg ebur128 measurement: −18.0 LUFS integrated, 2.3 LU loudness
  range, reported true peak −4.2 dBFS. This measures the processed source, not Unity's
  subsequently compressed Android import and not perceptual quality.

The whole composition repeats with its original ending and intro. It is **not presented as
a bar-aligned seamless loop**. Original-download/catalogue evidence, conversion logs, hashes
and durations are in `audio/sources/incompetech/`. `audio/LICENSES.md` records both downloads.

## Runtime behavior

`MusicEntry.fullMix` parses the explicit `full_mix` path. A full-mix bank plays one dedicated
AudioSource; its five stem sources remain empty. The arrangement stays intact across market
mood changes instead of filtering it or hiding its lead/drums. Existing adaptive stems are
still supported for the other mapped districts.

`music.lobby` and `music.timeline` reference the same file. Before allocating an incoming
bank, MusicDirector compares the incoming full-mix path with an already loaded bank. When
the path matches it reuses that bank, changes the logical key, and keeps its clip and DSP
start time. Consequently entering Timeline from the lobby does not restart the composition.
This alias transition is covered by the regression harness. Different music still uses the
existing two-bank 1.2-second crossfade; no new per-transition AudioSources are allocated.

User music gain, entry gain, voice −6 dB duck and other existing MusicDuckDb behavior still
apply to the dedicated source. Spaces Studio remains voice-only even when its Timeline
district has a full-mix entry. `ReviewSnapshot` now logs `mode=full_mix`, one active clip,
bank weight and full-mix gain for root's actual-device review.

The existing `CTLifeImportPostprocessor.OnPreprocessAudio` imports `/music/` as **Streaming**,
Vorbis quality **0.45**, `preloadAudioData=false`, `loadInBackground=true`, preserving stereo.
Root explicitly retained that existing import policy for K. Resources.Load references the
streaming clip; this source code does not request full PCM decompression. Root must verify
the imported asset/meta in Unity. The informational source-map import-quality field is not
an Editor override; the actual postprocessor remains authoritative.

`audio/music-curation.json` records the two aliases. `audio/tools/build_map.py` applies these
entries after old synthesized build reports and emits the required licence section, so
regenerating the source map does not silently restore the rejected Timeline music.

## Verification and handoff

- Audio assembly: all 6 source files compile with Unity 6000.3.25f1's Roslyn and pinned references.
- 687 actual MusicDirector/FootstepCadence assertions passed with lightweight Unity/audio
  stubs: old cadence/crossfade cases, full-mix isolation, entry gain, mood-independent full
  arrangement, alias DSP continuity, live mute, voice duck, Spaces voice-only, mixed-mode
  transitions and bounded cleanup. These do not verify actual sound or Unity scheduling.
- Source and Resources maps agree; both full-mix entries contain no stems; copied audio bytes
  match. The map-generator syntax and changed-file whitespace checks passed.
- No Unity, build, adb or device operation was performed by this agent. Root owns lobby
  FlowController wiring, K import/build and listening/device acceptance.

Compiler scratch: `/var/folders/zm/v0w94xb95p11_5df6rxm0js80000gp/T/ct-fullmix-compile-n6ti54sv`.
Regression scratch: `/var/folders/zm/v0w94xb95p11_5df6rxm0js80000gp/T/ct-fullmix-tests-ehk8npcz`.

Import-policy correction (2026-10-06): the earlier handoff incorrectly called short SFX ADPCM. Root verified ContentImport.OnPreprocessAudio: actual short-SFX policy is Vorbis quality 0.6, DecompressOnLoad, force mono. Curation/map metadata now matches it; no Unity policy, audio bytes or runtime behavior changed.
