READY TO COMMIT: client/Assets/Scripts/Audio/ client/Assets/Resources/Audio/ audio/ docs/progress/audio-integration.md docs/progress/requests.md — "feat(audio): runtime API v1, adaptive mood music, surface footsteps, babble budget, CT-native cues, ship only referenced audio (−14 MB)"

Exact paths (git status as of 2026-10-08 01:40):
- `client/Assets/Scripts/Audio/` — modified `AudioManager.cs`, `Babble.cs`, `BabblePlayer.cs`, `MusicDirector.cs`, `SfxMap.cs`;
  new `AudioApi.cs`, `FootstepEmitter.cs`, `FootstepSurface.cs`, `FootstepSurfaces.cs`, `Logic/` (7 files, incl.
  `FootstepCadence.cs` + its original `.meta`, moved from `Audio/`), `Cues/` (asmdef + `RealtimeCues.cs`), `Editor/`
  (asmdef + `AudioImportPostprocessor.cs`), `Tests/` (asmdef + 4 test files), `Tests~/` (runner, scripts);
  **deleted** `Audio/FootstepCadence.cs` and `Audio/FootstepCadence.cs.meta` (moved into `Logic/`, same GUID).
- `client/Assets/Resources/Audio/` — modified `sfx-map.json`, `Music-Credits.txt`, `ambience/amb_club_doors.wav`,
  `sfx/sfx_buy.wav`, `sfx/sfx_sell.wav`, `sfx/sfx_graduation.wav`, `sfx/sfx_sunday_unlock.wav`; new `sfx/sfx_news_flash.wav`,
  `sfx/sfx_gm.wav`, `sfx/sfx_cash_in.wav`, `sfx/sfx_rekt_phonk.wav`, `music/stems/club/mus_club_{pad,bass,drums,lead,tension}.ogg`,
  `music/mood/mus_mood_tension_bed.ogg`; **deleted (intentional, never played: not referenced by sfx-map.json), each with
  its `.meta`**: `music/mus_{timeline_square,trenches,perp_pit,airdrop_farm,builder_hills,dubai_marina,club}_{pad,bass,drums,lead,tension,full}.ogg`
  (42), `music/licensed/mus_clubs_disco_con_tutti.ogg`, `sfx/sfx_ui_{tap,toast,toggle_on,toggle_off,success,error}.wav` (6,
  superseded by `sfx/licensed/*_kenney.wav`), `babble/babble-banks.json` (tool input, never loaded at runtime).
  New clips/folders get their `.meta` from Unity on the next import; commit those with this.
- `audio/` — modified `BABBLE.md`, `LICENSES.md`, `README.md`, `music-curation.json`, `sfx-curation.json`, `sfx-map.json`,
  `processed/Music-Credits.txt`, `processed/ambience/amb_club_doors.wav`, `processed/sfx/sfx_{buy,sell,graduation,sunday_unlock}.wav`,
  `tools/{build_map,check_audio,gen_ambience,gen_music,gen_sfx,instruments}.py`, `tools/{build_music,build_sfx,qa_report}.json`;
  new `RUNTIME_API.md`, `processed/music/stems/club/` (5), `processed/music/mood/` (1), `processed/sfx/sfx_{cash_in,gm,news_flash,rekt_phonk}.wav`,
  `tools/{gen_mood_bed,ship_audio}.py`, `tools/build_mood_bed.json`, `archive/processed/**` (49);
  **deleted** 49 files under `audio/processed/` = `music/mus_*` (42), `music/licensed/mus_clubs_disco_con_tutti.ogg`,
  `sfx/sfx_ui_*.wav` (6). Every one was moved byte-identical to `audio/archive/processed/<same path>` (checked with
  `cmp` against `HEAD`), so git stores no new blobs.
- `docs/progress/requests.md` — five "2026-10-08 audio-integration -> …" sections (already committed by the coordinator;
  one later reorder of Money items 4/5).

# Audio integration lane — progress log

Owner: audio integration lane. Scope: `client/Assets/Scripts/Audio/` (assemblies CTLife.Audio, .Cues, .Editor, .Tests),
`client/Assets/Resources/Audio/`, `audio/`. No Unity editor, no adb, no listening device: **nothing here has been heard
by anyone**. Every claim below is from code, unit tests, compile passes or objective audio measurement.

## Status (2026-10-08)

| Task | State | Evidence |
| --- | --- | --- |
| 1 Every sfx id has a trigger path | 27 of 36 ids have at least one trigger path in code (QA counted 12 of 32 firing); the other 9 have exact diffs requested from their owners: graduation, ORE furnace, PnL flip, Space started, Sunday Unlock, take-profit (today only as Giveaway Guy's signature), toggle-off, raise hand, REKT phonk | `audio/RUNTIME_API.md` §3 table; requests.md |
| 2 Adaptive music | Mood layering per spec, bar-latched, 2-bar fades, synced stem start + drift guard, venue crossfades, voice/Spaces/call duck 6 dB, stinger duck 9 dB | `MoodMix`/`MoodController`/`Ducker` + tests |
| 3 Footsteps | Surface resolver (tag → physics material → material → object name → venue floor), speed-driven cadence, other-avatar budget | `SurfaceResolver`/`StrideModel`/`FootstepBudget` + tests; World wiring requested |
| 4 Babble | BABBLE.md as a pure scheduler; ≤ 3 talkers, 1 line/NPC/1.5 s, 15 m earshot, linear roll-off, lazy voices | `BabbleScheduler`/`BabbleBudget` + tests |
| 5 Mixer, settings, import, size | Code mixer (master/music/sfx/voice/ambience, v² taper), import policy, ship-only-referenced tool; audio payload 36.7 MB measured → 21.5–22.6 MB estimated | `VolumeSettings`, `ImportPolicy` (+ tests), `ship_audio.py` |
| 6 Tests outside the editor | **132 passing** NUnit cases | `Tests~/run_audio_tests.sh` |

Compile: `Tests~/compile_audio.py` dev player, release player and editor passes: CTLife.Audio, .Cues, .Editor, .Tests,
World, UI all ok, **0 warnings in audio assemblies**. (Unrelated, other lanes mid-edit at the time: CTLife.App editor
pass needs `CTLife.Features.WorldLife` in compile_check's App refs; earlier Social had a missing `SocialStrings`.)
The World/UI diffs I requested were applied to a scratch copy and compile clean.

## What changed

**Runtime API v1** (`audio/RUNTIME_API.md`, additive-only contract). Facades `Sfx`, `Music`, `Footsteps`, `Ducking`,
constants `SfxIds`; every call no-ops safely without a runtime. Lanes without a CTLife.Audio reference keep using
`Bus.Publish(new SfxSignal(id))`. `SfxSignal.volume == 0` (struct default) now means 1 (it silently muted before).

**Cue policy** (`Logic/CueRules.cs`): registry aliases; per-category and per-id cooldowns so the UI card and a bus
subscription reacting to the same event play once (e.g. `sfx_liquidation` from RektCard + MoneyRektCard + incident card);
a rich cue masks a simultaneous toast tick; loop ids sent as signals are keep-alive (Social's CallController re-sends
the ring every 3 s; connect/end stops it at once; a lost stop can never ring for more than one cycle).

**Realtime subscriptions** (`Cues/RealtimeCues.cs`, separate assembly so the shared compile check is not broken; installs
itself with `RuntimeInitializeOnLoadMethod`, kept through stripping with `AlwaysLinkAssembly`): `dm` → DM ping,
`call_invite` → ring, market `tape` ≥ 10× `tapeThresholdCents` → whale horn ([Inference] threshold: the contract has
none), venue `said` "gm" → `sfx_gm`, someone else's `@alias` → mention, `payment_confirmed` → `sfx_cash_in`.

**Music** (`MusicDirector`): stem sets and full-mix sets. Mood rules exactly per `audio/README.md` (drums in 0 → +0.5,
lead in +0.35 → +0.5, tension in 0 → −0.5, low-pass 20 kHz → 2.5 kHz → 900 Hz). Full mixes stay intact and get the
low-pass plus a key/tempo-neutral tension bed. Changes latch on bar lines and fade over two bars. Stems wait for all
clips to load, then `PlayScheduled` on one DSP tick (3 s timeout); a 1 s drift guard re-locks a stem > 1024 samples off.
Low-pass filters are disabled while open (no DSP cost when green/neutral). Music sources get priority 0.

**Mixer/settings** (`MixModel.cs`, `AudioManager`): master (= `AudioListener.volume`, covers LiveKit), music, sfx, voice,
ambience; PlayerPrefs `ctlife.audio.<key>`; v² taper (50 % ≈ −12 dB); reloaded on every runtime restore ([Inference] the
old code read them only in Awake, so an editor domain reload reset them to the field defaults). Ducking: keyed holds, deepest wins (voice + stinger = −9, not −15),
75 dB/s attack, 12 dB/s release. Voice volume must be applied by the LiveKit owners (requested, with a Core signal).

**Footsteps**: `PlayFootstep(null, pos)` = automatic surface; `FootstepSurface` component for scenes; venue floor
defaults in `sfx-map.json` `surfaces.venues` ([Inference] from venue themes); `FootstepEmitter` + `Footsteps.Attach`
for remote players/NPCs through `FootstepBudget`. Local wiring is a 2-line World diff (requested).

**Babble**: `BabbleScheduler` (pure; fixes the old O(n²) `IndexOf` per character and applies ?/! contours to the
syllables actually played after the 3 s cap), `BabbleBudget`, linear roll-off to silence at 15 m (the old logarithmic
roll-off never reached zero), voices created on first line. Optional per-archetype signature cue (data:
`babble.<id>.signature`; Town Crier → `sfx_news_flash`, Giveaway Guy → `sfx_take_profit`, Engagement Farmer →
`sfx_new_coin`), quiet and at most once per 30 s per NPC. [Inference] mapping from the dossier's `babble_sound` fields.

**Import policy** (`Logic/ImportPolicy.cs` + `Editor/AudioImportPostprocessor.cs`, order 100): music Streaming Vorbis
0.45; stems/bed Compressed In Memory 0.45; ambience Streaming 0.35 (was 0.45); stingers CIM 0.6 (were Decompress On
Load: 0.4–0.8 MB of PCM each in RAM); footsteps DOL 0.5 at 32 kHz; babble DOL 0.5 at 24 kHz; other one-shots DOL 0.6.
**Force To Mono is off**: the old importer forced mono on every short clip, and Unity's downmix also peak-normalises
("Normalize" is on in every `.meta`), which [Inference] lifted each babble syllable and Kenney UI tick to full scale and
undid the equal-RMS / quiet-tick design. Sources are authored mono; `ship_audio.py` warns on any stereo file in a mono group.

## Sound culture research (CT-native palette)

WebSearch was unavailable (the session's 200-search budget was already used by other lanes); I fetched primary pages
directly with WebFetch on 2026-10-08 and read the dossiers. Chrome was not used.

| Source (fetched 2026-10-08 unless noted) | Finding used |
| --- | --- |
| docs/research/ct-now-2026-10.md + .json (research lane, 2026-10-07/08) | Liquidation / "JUST IN" news-bot posts are the timeline's heartbeat (§1b, §2b, observed); "BREAKING" + siren emoji; `gm` now mostly brand/farmer accounts; §4d sound culture (mostly [Inference]): phonk edits of liquidation clips, donation alerts on launchpad streams, Spaces dead air, raise-hand pings; per-archetype `babble_sound` |
| docs/research/ct-x-observations-2026-10-04.md | News bots "JUST IN: $X liquidated…"; Nigerian engagement prompts |
| [Wikipedia: Pump.fun](https://en.wikipedia.org/wiki/Pump.fun) | Launched 19 Jan 2024; coins "graduate" to a DEX at a market-cap threshold; livestreams suspended Nov 2024, back 11 Apr 2025 |
| [Know Your Meme: GM](https://knowyourmeme.com/memes/gm-good-morning) | gm / gn / wagmi greeting ritual on NFT/crypto Twitter since 2018 |
| [Know Your Meme: Money Printer Go Brrr](https://knowyourmeme.com/memes/money-printer-go-brrr) | 9 Mar 2020; onomatopoeic "brrr" (not used yet) |
| [Know Your Meme: BitConnect](https://knowyourmeme.com/memes/bitconnect) | Famous crypto audio meme — copyrighted recording, deliberately **not** used or imitated |
| [Wikipedia: Amapiano](https://en.wikipedia.org/wiki/Amapiano) | Log drum = "a wide percussive bassline"; shakers, piano, jazz chords; Afropiano = Nigerian afrobeats × amapiano fusion (early 2020s) |
| [Native Instruments blog: Amapiano](https://blog.native-instruments.com/amapiano/) (published 4 Sep 2025) | "110s BPM" (tutorial at 114), kick on every beat, shakers |
| [Wikipedia: Afrobeats](https://en.wikipedia.org/wiki/Afrobeats) | 3–2 / 2–3 clave; the beat is "a major character" |
| [Wikipedia: Talking drum](https://en.wikipedia.org/wiki/Talking_drum) | Yoruba dùndún: pitch glides within a beat, "like a human humming"; Fuji and Jùjú |
| [Wikipedia: Owambe](https://en.wikipedia.org/wiki/Owambe) | Yoruba celebration parties; money "spraying" |
| [TradingView: alerts](https://www.tradingview.com/support/solutions/43000595315-how-to-set-up-alerts/) | "Play Sound" is a standard alert action |
| [Axiom docs](https://docs.axiom.trade/llms.txt) (wallet tracker, tweet monitor), [GMGN docs](https://docs.gmgn.ai/) | No documented sound alerts. [Unverified] whether these terminals ship audio at all; our pings are original and short |
| [Whale Alert](https://whale-alert.io/) | Whale alerts are push/X/Telegram posts since 2018; no sound documented |
| [Wikipedia: Twitter Spaces](https://en.wikipedia.org/wiki/Twitter_Spaces) | Launched 2021; up to 13 on stage |

Resulting changes (all own synthesis, CC0; no sampled meme audio):
- `sfx_buy` / `sfx_sell`: short terminal pings (fifth up / fourth down, < 0.45 s) instead of casino chime arpeggios.
- `sfx_graduation`: the bonding curve fills (accelerating rising ticks + riser) → brass hit → a whoosh that travels
  left → right (migration to the DEX).
- `sfx_sunday_unlock`: trading-floor opening bell (electric clapper) + padlock clack + deep strike (CT "unlock" =
  team/VC tokens becoming sellable; the dossier logs a monthly team unlock).
- New client-only cues: `sfx_news_flash` ("JUST IN"/"BREAKING" two-tone siren chirp + teletype), `sfx_gm` (soft
  sunrise chime, at most every 12 s), `sfx_cash_in` (owambe money-spray flutter + register + dùndún glide),
  `sfx_rekt_phonk` (2-bar phonk tag for REKT replays; [Inference] from the dossier).
- Clubs (spec: Amapiano / Afrobeats, 112 BPM): own-work Afropiano stem set — log-drum bass, shakers, kick every beat,
  3–2 clave woodblock, dùndún talking-drum fills, highlife-style interlocking guitar, whistle answer, one quiet synth
  DJ air horn per loop in the lead stem (heard only when the market is green). Replaces the licensed "Disco con Tutti"
  (archived, licence row kept; revert = restore `clubs` in `music-curation.json`). `amb_club_doors` re-rendered from it.
- Measurements: new one-shots −1.0…−1.7 dBTP; club preview −17.0 LUFS integrated (spec −18…−16), stems sample-identical
  (1,645,714 samples), loop seams ordinary (rank ≤ 98.3 %); tension bed −27 LUFS, seam step below the 99th percentile.
- Not done from the research ([Inference] there too): Spaces "dead air" ambience (mic bumps, echo), a launchpad
  "dono" alert (use `sfx_cash_in` for now), more archetype signature sounds (most need new assets).

## Size

`audio/tools/ship_audio.py` maps every `.resource` in `client/Builds/ctlife-local-dev.apk` back to its clip via the
`.meta` GUIDs. Measured audio payload: **36.71 MB** (944 clips: generated stems 13.0 MB that never played, licensed music
16.5 MB, ambience 5.7 MB, babble 1.0 MB, sfx + footsteps 0.45 MB). Shipping set now: 905 clips, **21.5–22.6 MB estimated**
(unchanged clips at their measured size; new clips at their group's measured bytes/s; ambience/babble/footstep setting
changes as [Inference] ratio ranges). [Inference] APK 162.4 MB → ≈ 148–149 MB with nothing else changed. Needs a real build
to confirm (requests.md item 8).

## Verify

```sh
client/Assets/Scripts/Audio/Tests~/run_audio_tests.sh                    # → PASSED 132  FAILED 0
python3 client/Assets/Scripts/Audio/Tests~/compile_audio.py --editor     # dev player + editor: RESULT ok
python3 client/Assets/Scripts/Audio/Tests~/compile_audio.py --release    # CTLIFE_PROD player: RESULT ok
python3 audio/tools/ship_audio.py --check                                # Resources/Audio == map; prints footprint
```
In Unity: Test Runner → EditMode → CTLife.Audio.Tests; CT Life → Audio → Reimport Audio With Policy.
Device/listening checklist (nobody has heard any of this): club stems in sync for 5+ minutes; mood red → muffled + tension,
green → drums/lead; voice duck; ring stops on answer; footsteps change on café wood / Perp Pit grate; babble ≤ 3 voices;
UI ticks now quieter than before (normalisation removed) — rebalance `volume_db` by ear if needed.

## Requests made (docs/progress/requests.md, 2026-10-08)

Integration lead: compile_check units; World footsteps/attach/`Music.SetVenue`; Core `VoiceActivitySignal.source` +
`VoiceVolumeSignal`; Net voice source/volume; Settings master/voice/ambience; news cue + toggle-off; remove the old audio
importer and sync audio through `ship_audio.py`; measure the next APK. Money: take-profit, PnL flip, market new coin +
graduation, ORE furnace, REKT phonk. Social: raise hand, Space started, `sfx_call_end` on decline/miss, voice source,
voice volume. CityLife: Sunday Unlock bell. WorldLife: id candidates for moment effects, NPC footsteps.

## Unverified / limits

- [Unverified] FMOD (spec) is not used; this runs on Unity audio, as before. FMOD integration version still unpinned.
- [Unverified] On-device behaviour of `PlayScheduled` stem sync, CIM Vorbis looping, AudioLowPassFilter cost on Mali;
  `RuntimeInitializeOnLoadMethod` in an otherwise unreferenced assembly under IL2CPP stripping (mitigated with
  `AlwaysLinkAssembly` + `[Preserve]`).
- [Inference] whale threshold, venue floor defaults, stride-vs-speed below walk speed, size ratios, archetype signatures.
- Unity glue (AudioManager/MusicDirector/BabblePlayer components) is compile-checked, not executed; only the pure
  logic is under test. Next: PlayMode tests once the editor is available.

## Next

1. After the lead's next import: confirm `.meta` generation, reimport with policy, run the EditMode tests in Unity.
2. Apply my side of the Core voice-source change as soon as the fields exist.
3. Spaces "dead air" ambience, siren loop for WorldLife moments, more archetype signatures — proposals, need assets.
