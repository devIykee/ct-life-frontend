#!/usr/bin/env python3
"""Community nods (R4, 2026-10-25): the find chime, a real recording, CC0 (Freesound HQ preview, licence re-checked on
download by freesound.py): one stroke of a real bell tree from the VSCO 2 Community Edition percussion set, cut round the
stroke and peak-normalised. Registers sfx_nod_collect in sfx-map.json (audio/ and client Resources, kept identical), copies
the WAV into client Resources and appends the source to LICENSES.md. Cutting: curate_rekt_audio.make_cue.

Usage (repo root): uv run --no-project --with numpy==2.4.2 --with scipy==1.18.1 --with soundfile==0.13.1 \
                     python -I audio/tools/curate_nod_audio.py
"""
import json
import shutil
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
import curate_casino_audio as C  # noqa: E402
import curate_rekt_audio as R  # noqa: E402

AUDIO, CLIENT, SR = C.AUDIO, C.CLIENT, C.SR
TODAY = "2026-10-25"

CUES = {
    "sfx_nod_collect": (373394, "auto", 1.8, "a nod found: one stroke of a real bell tree (VSCO 2 CE percussion)", -7),
}


def main():
    smap = json.loads((AUDIO / "sfx-map.json").read_text())
    rows = []
    for cid, spec in CUES.items():
        y, meta, edit, desc, vol, _ = R.make_cue(cid, spec)
        rel = f"sfx/{cid}.wav"
        sf.write(str(AUDIO / "processed" / rel), y.astype(np.float32), SR, subtype="PCM_16")
        shutil.copy(AUDIO / "processed" / rel, CLIENT / rel)
        page = meta.get("url")
        smap["sfx"][cid] = {
            "files": [rel], "volume": round(10 ** (vol / 20), 3), "pitchJitter": 0.04, "category": "world",
            "volume_db": vol, "channels": 1, "loop": False, "duration_s": round(len(y) / SR, 3), "true_peak_dbtp": -1.0,
            "ducks_music": False, "status": "recorded", "desc": desc,
            "sources": [f"{meta.get('title')} — {meta.get('author')} (freesound {spec[0]})"],
            "license": "CC0-1.0", "license_url": "https://creativecommons.org/publicdomain/zero/1.0/", "source_page": page,
            "source_pages": [page], "license_checked": TODAY,
            "processing": f"{json.dumps(edit)}; mono; 48 kHz; peak-normalised to -1 dBFS",
            "unity_import": {"loadType": "DecompressOnLoad", "compressionFormat": "Vorbis", "quality": 0.6, "preloadAudioData": True},
        }
        smap["index"][cid] = f"sfx.{cid}"
        rows.append((cid, meta))
        print(cid, spec[0], meta.get("license"), meta.get("title"), edit, round(len(y) / SR, 2))
    txt = json.dumps(smap, indent=2, ensure_ascii=False) + "\n"
    (AUDIO / "sfx-map.json").write_text(txt)
    (CLIENT / "sfx-map.json").write_text(txt)
    lic = (AUDIO / "LICENSES.md").read_text()
    if "## Community nods (2026-10-25" not in lic:
        out = ["", "## Community nods (2026-10-25): the find chime, a real recording, CC0", "",
               "| id | title | author | source | licence | checked |", "| --- | --- | --- | --- | --- | --- |"]
        for cid, meta in rows:
            out.append(f"| {cid} | {meta.get('title')} | {meta.get('author')} | {meta.get('url')} | CC0 1.0 | {TODAY} |")
        (AUDIO / "LICENSES.md").write_text(lic.rstrip() + "\n" + "\n".join(out) + "\n")
    print("sfx-map + LICENSES updated")


if __name__ == "__main__":
    main()
