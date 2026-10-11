# Audio agent progress log

Owner folder: `audio/` only. Spec: `docs/spec/build-spec.md` "Sound design"; IDs: `docs/spec/contract.md` §2
(`sfx.json` is FE-owned). Unity asked for the `sfx-map.json` shape in `docs/progress/requests.md`; done.

## Status 2026-10-05: library complete (v1)

| Group | IDs | Files | Format | Level (ffmpeg ebur128) |
| --- | --- | --- | --- | --- |
| UI | 8 | 8 | WAV 48k/16 mono | −1.0…−1.7 dBTP |
| Trading | 6 | 6 | mono | same |
| Stingers | 6 | 6 | stereo | same |
| Phone | 6 | 6 | mono (`sfx_call_ring` = seamless 3 s loop) | same |
| Voice rooms | 4 | 4 | mono | same |
| ORE refinery | 2 | 7 (2 furnace + 5 clink) | mono | same |
| Footsteps | 6 surfaces | 36 (5/5/5/5/8/8) | mono | −1.0 dBTP |
| Ambience | 9 venues | 9 loops, 30–34 s | WAV stereo | −24.0 LUFS (studio −36.2), TP ≤ −3.2 |
| Music | 7 sets | 42 (5 stems + full mix each) | Ogg Vorbis q4 stereo | full mixes −17.0/−17.1 LUFS, TP −2.0…−5.0 |
| Babble | 31 banks | 806 (26 letters each) | mono | ≤ −1.5 dBTP, equal RMS |

Totals: 85 IDs, 930 files, 87 MB on disk (ambience WAV is 50 MB of it; Unity re-encodes on import).
Loops: every music stem has the exact intended sample count (stems of a set are sample-identical in length); the
wrap-around step at every music/ambience loop point is an ordinary step for that file (highest percentile rank 99.4
among its own sample steps, `seam_pct` in `audio/tools/qa_report.json`), so no click is expected.

Deliverables:
- `audio/sfx-map.json` — ids → files (Unity shape + `index` of prefixed ids + metadata).
- `audio/LICENSES.md` — 8 Kenney packs + 32 freesound sounds, all CC0, with page URL, author, date, use.
- `audio/BABBLE.md` + `audio/unity/BabblePlayer.cs` — babble rules and reference player.
- `audio/README.md` — layout, rebuild commands, levels, market_mood stem rules, import hints.
- `audio/tools/` — `fetch_sources.py`, `gen_sfx.py`, `gen_footsteps.py`, `gen_music.py`, `gen_ambience.py`,
  `gen_babble.py`, `babble_preview.py`, `check_audio.py`, `build_map.py`, shared `dsp.py`/`instruments.py`/`master.py`.

## How to rebuild / verify (≈2.5 min)
```sh
cd audio && python3 -m venv .venv && .venv/bin/pip install numpy==2.5.3 scipy==1.18.1 soundfile==0.14.0
cd tools && P=../.venv/bin/python
$P fetch_sources.py && $P gen_sfx.py && $P gen_footsteps.py && $P gen_music.py && $P gen_ambience.py \
  && $P gen_babble.py && $P check_audio.py && $P build_map.py
$P babble_preview.py town_crier 'JUST IN: $4.2M in longs liquidated'   # -> audio/build/babble_preview/
```

## Verified
- ffmpeg 8.0 (Homebrew, libvorbis + soxr); Python 3.14.7; numpy 2.5.3 / scipy 1.18.1 / soundfile 0.14.0 checked on
  PyPI 2026-10-05. sox / SuperCollider not installed and not needed.
- Licences: Kenney zips' License.txt says CC0; each freesound page was re-checked for the CC0 deed at download.
- `BabblePlayer.cs` compiles with Unity 6000.3.25f1's bundled Roslyn against UnityEngine.CoreModule/AudioModule.
  `AudioSource.PlayScheduled` checked on the 6000.3 scripting reference (not deprecated).

## Unverified / limits
- [Unverified] **Nothing was auditioned by a human.** Checks were objective only: loudness, true peak, loop seams,
  spectrograms. Musical quality of the synthesised music, the babble character and the synthesised SFX need a
  listening pass.
- Freesound files are the public HQ previews (192 kbps Ogg), not the originals (originals need a login).
- [Unverified] Layered field recordings were chosen by title/description plus automatic "steadiest stretch"
  selection; e.g. `450126` (refinery) mentions a distant warning alarm and the crowd recordings may contain
  intelligible speech.
- [Unverified] Behaviour of Vorbis loops inside Unity/FMOD on device (gapless) was not tested on the Seeker.
- Ambience level (−24 LUFS) and the per-ID `volume_db` are my design choices; the spec only sets the music target.

## Gaps that need custom recording or a better source
- Real human vocal babble (current banks are formant synthesis; a recorded CC0 voice actor would sound warmer).
- Metal grate / neon tile footsteps are sliced from walking takes (8 each) — ok, but a dedicated Foley session would
  match the Kenney sets better. No sneaker/heel variants per avatar.
- Real music performance (banjo, piano, flute are synthesised) and a proper master; Afrobeats/Amapiano groove needs
  a producer's ear.
- Club interior music is the `clubs` set; no crowd-cheer / DJ drops; no Spaces Studio stage stingers beyond
  `sfx_space_started`; no per-career mini-game SFX (Phase 1 has 9 mini-games) — not in the spec's list yet.

## Requests posted
- audio → unity: sfx-map shape answered; please add 27 new sfx ids to `contract/ids/sfx.json`; music key notes.
- audio → backend: `laser_eyes_maxi` / `chain_tribalist` not in archetypes.json; `spaces_host` listed twice.

## Log
- 2026-10-05 15:00 setup, tool checks, Kenney + freesound research (freesound search rate-limits parallel scrapers:
  429 → sequential with backoff).
- 2026-10-05 first scipy import in the new venv stalled ~50 min on macOS `syspolicyd` scanning native libraries;
  later runs are fast.
- 2026-10-05 SFX, footsteps, music (7 sets), ambience (9), babble (31 banks) rendered; QA; map + licences; clean rebuild ok.
