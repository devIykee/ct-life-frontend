# CT Life audio

Everything here is CC0 or our own work (see `LICENSES.md`). IDs → files: `sfx-map.json`.

```
audio/
  processed/sfx/            one-shots + footsteps (48 kHz 16-bit WAV; UI/phone/trading/footsteps mono, stingers stereo)
  processed/ambience/       venue loops (48 kHz 16-bit stereo WAV, seamless)
  processed/music/title/    CT Life main theme mus_title (own work; tools/gen_title.py, motif in tools/theme.py)
  processed/music/licensed/ complete CC-BY district tracks (Ogg Vorbis, stereo)
  processed/music/stems/    adaptive stem sets, sample-locked (today: club/ — Afropiano, own work)
  processed/music/mood/     red-market tension bed for the full-mix sets (key/tempo-neutral, own work)
  processed/babble/         NPC syllable banks + babble-banks.json (tool input, not shipped)
  archive/processed/        files no longer referenced by sfx-map.json (kept for history / easy revert; never shipped)
  RUNTIME_API.md            the client audio API every lane calls (CTLife.Audio)
  unity/BabblePlayer.cs     original reference player (the shipping one is client/Assets/Scripts/Audio/BabblePlayer.cs)
  tools/                    every generator, the fetcher, QA, map builder and ship_audio.py
  raw/                      third-party downloads (gitignored; rebuild with tools/fetch_sources.py)
```

## Rebuild from scratch

```sh
cd audio
python3 -m venv .venv && .venv/bin/pip install numpy==2.5.3 scipy==1.18.1 soundfile==0.14.0
cd tools
../.venv/bin/python fetch_sources.py     # Kenney zips + freesound CC0 previews -> raw/, provenance -> sources.json
../.venv/bin/python gen_sfx.py           # processed/sfx/sfx_*.wav
../.venv/bin/python gen_footsteps.py     # processed/sfx/foot_*.wav
../.venv/bin/python gen_music.py club    # processed/music/stems/club/mus_club_*.ogg (16 bars, 112 BPM); preview in build/
../.venv/bin/python gen_mood_bed.py      # processed/music/mood/mus_mood_tension_bed.ogg (24 s seamless)
../.venv/bin/python gen_title.py         # processed/music/title/mus_title.ogg (68.6 s seamless main theme)
../.venv/bin/python curate_ui_sounds.py  # Kenney UI cues → processed/sfx/licensed/ (peak −1 dBFS, audited gains)
../.venv/bin/python gen_ambience.py      # processed/ambience/amb_*.wav (needs gen_music for the club; `amb_club_doors` alone re-renders one)
../.venv/bin/python gen_babble.py        # processed/babble/*
../.venv/bin/python check_audio.py       # loudness / true peak / loop seams -> qa_report.json
../.venv/bin/python build_map.py         # sfx-map.json + LICENSES.md
python3 ship_audio.py --archive --sync   # only map-referenced files reach client/Assets/Resources/Audio; prints footprint
../.venv/bin/python mix_audit.py         # onboarding cues vs the title music at default settings (margins in dB)
```
Needs ffmpeg with libvorbis and soxr (checked with Homebrew ffmpeg 8.0). The first run on macOS can stall
for minutes while the OS scans scipy's native libraries; later runs take seconds.

## Levels

| Group | Target | Measured (ffmpeg ebur128) |
| --- | --- | --- |
| Music full mix per district | −17 LUFS integrated (spec −18..−16), ≤ −1 dBTP | see `tools/qa_report.json` |
| Ambience | −24 LUFS, ≤ −3 dBTP (Spaces Studio room tone −36) | |
| One-shot SFX, footsteps | true peak −1 dBTP each (consistent peaks); mix with `volume_db` | |
| Babble | equal RMS per syllable, ≤ −1.5 dBTP | |

## Adaptive music (`market_mood` −1 … +1)

Implemented in `client/Assets/Scripts/Audio/Logic/MoodMix.cs` (unit-tested) and `MusicDirector.cs`; contract in
`RUNTIME_API.md` §4. Two kinds of set:

- **Stem set** (`stems.pad/bass/drums/lead/tension`, identical sample length, started on one DSP tick):

| mood | pad | bass | drums | lead | tension | music low-pass |
| --- | --- | --- | --- | --- | --- | --- |
| +1.0 … +0.5 | on | on | on | on | off | open |
| +0.5 … 0 | on | on | fades out 0.5 → 0 | fades out 0.5 → 0.35 | off | open |
| 0 … −0.5 | on | on | off | off | fades in | 20 kHz → 2.5 kHz |
| −0.5 … −1 | on | on | off | off | on | 2.5 kHz → 900 Hz |

- **Full-mix set** (`full_mix` + `stems.tension`): the licensed arrangement stays intact (green = neutral = the track);
  red applies the same low-pass and fades in the tension bed.

Changes latch on bar lines (bpm in the map) and fade over two bars (2–8 s). Music ducks 6 dB under voice / Space
speakers and 9 dB under every `stinger` (deepest wins). Clubs use `clubs`; outside a club play `amb_club_doors` (the
same groove through a wall).

## Unity import (Android)

Authoritative policy: `client/Assets/Scripts/Audio/Logic/ImportPolicy.cs`, applied by
`CTLife.Audio.Editor/AudioImportPostprocessor`. Music Streaming Vorbis 0.45; stems/beds Compressed In Memory 0.45;
ambience Streaming 0.35; stingers Compressed In Memory 0.6; footsteps Decompress On Load 0.5 at 32 kHz; babble
Decompress On Load 0.5 at 24 kHz; other one-shots Decompress On Load 0.6. No Force To Mono: sources are authored mono,
and Unity's downmix also peak-normalises. `sfx-map.json` `unity_import` fields mirror this for information only.
