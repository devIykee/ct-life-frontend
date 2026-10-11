# CT Life audio

Everything here is CC0 or our own work (see `LICENSES.md`). IDs → files: `sfx-map.json`.

```
audio/
  processed/sfx/        one-shots + footsteps (48 kHz 16-bit WAV; UI/phone/trading/footsteps mono, stingers stereo)
  processed/ambience/   venue loops (48 kHz 16-bit stereo WAV, seamless)
  processed/music/      district stems (Ogg Vorbis q4, 48 kHz stereo, seamless, sample-locked)
  processed/babble/     NPC syllable banks + babble-banks.json
  unity/BabblePlayer.cs reference babble player (FE copies into client/)
  tools/                every generator, the fetcher and the QA script
  raw/                  third-party downloads (gitignored; rebuild with tools/fetch_sources.py)
```

## Rebuild from scratch

```sh
cd audio
python3 -m venv .venv && .venv/bin/pip install numpy==2.5.3 scipy==1.18.1 soundfile==0.14.0
cd tools
../.venv/bin/python fetch_sources.py     # Kenney zips + freesound CC0 previews -> raw/, provenance -> sources.json
../.venv/bin/python gen_sfx.py           # processed/sfx/sfx_*.wav
../.venv/bin/python gen_footsteps.py     # processed/sfx/foot_*.wav
../.venv/bin/python gen_music.py         # processed/music/mus_*.ogg   (~10 s per district)
../.venv/bin/python gen_ambience.py      # processed/ambience/amb_*.wav (needs gen_music for the club)
../.venv/bin/python gen_babble.py        # processed/babble/*
../.venv/bin/python check_audio.py       # loudness / true peak / loop seams -> qa_report.json
../.venv/bin/python build_map.py         # sfx-map.json + LICENSES.md
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

Each district `mus_<district>` has five stems with **identical sample length**; start them together on the same
DSP time (FMOD: one event, five synced tracks; Unity: `PlayScheduled` with the same dspTime) and loop them.
Suggested mapping of the spec's rule ("green brings in drums and lead; red low-passes the mix and adds a tension
layer"):

| mood | pad | bass | drums | lead | tension | music bus low-pass |
| --- | --- | --- | --- | --- | --- | --- |
| +1.0 … +0.5 | 0 dB | 0 dB | 0 dB | 0 dB | off | open |
| +0.5 … 0 | 0 dB | 0 dB | fades in from 0 | off → fades in after +0.5 | off | open |
| 0 … −0.5 | 0 dB | 0 dB | off | off | fades in | 20 kHz → 2.5 kHz |
| −0.5 … −1 | 0 dB | 0 dB | off | off | 0 dB | 2.5 kHz → 900 Hz |

Fade over ~2 bars; switch on bar lines (bpm and bars are in `sfx-map.json`). Duck the music bus 6 dB under
LiveKit voice and under every `stinger`. `full_mix_preview` files are a non-adaptive fallback and a reference.
Clubs use `mus_club`; outside a club play `amb_club_doors` (the same groove through a wall).

## Unity import (Android) suggestions

`sfx-map.json` → `unity_import` per ID. Short UI/phone/footsteps/babble: Decompress On Load + ADPCM,
footsteps Force To Mono. Stingers: Compressed In Memory + Vorbis. Ambience and music: Streaming + Vorbis.
If the project uses FMOD Studio (spec), import the same files into FMOD and let it compress per platform.
