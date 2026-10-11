# Audio agent progress log

Owner folder: `audio/` only. Spec: `docs/spec/build-spec.md` "Sound design"; IDs: `docs/spec/contract.md` §2 (`sfx.json` is FE-owned).

## 2026-10-05

### Setup (done)
- Tools verified on this machine: ffmpeg 8.0 (Homebrew, built with libvorbis, libsoxr, librubberband), Python 3.14.7.
- Python venv at `audio/.venv` (gitignored by `audio/.gitignore`). Packages pinned after checking PyPI on 2026-10-05:
  numpy 2.5.3 (2026-09-06), scipy 1.18.1 (2026-08-21), soundfile 0.14.0 (2026-06-06, libsndfile 1.2.2).
- Not installed: sox, SuperCollider, fluidsynth. Not needed; all synthesis is numpy/scipy.

### Plan
1. Download CC0 sources (Kenney packs, freesound CC0 previews, OpenGameArt CC0) into `audio/raw/` with a manifest.
2. Synthesize what is missing (UI, trading, stingers, phone, voice rooms, babble, music) with Python in `audio/tools/`.
3. Process with ffmpeg: trim, fade, loop, loudness. Export to `audio/processed/{sfx,ambience,music,babble}/`.
4. Write `audio/sfx-map.json` and `audio/LICENSES.md`.
