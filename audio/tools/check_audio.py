#!/usr/bin/env python3
"""QA pass over audio/processed: loudness (ffmpeg ebur128), true peak, format, loop seams.

Writes tools/qa_report.json and prints a summary table.
Usage: check_audio.py [subfolder ...]   (default: sfx ambience music babble)
"""
from __future__ import annotations

import json
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import numpy as np

import dsp
from master import OUT, ebur128, probe, read_audio

REPORT = Path(__file__).with_name("qa_report.json")


def seam_db(path: Path) -> float:
    """Jump at the loop point relative to the typical sample-to-sample step (dB). ~0 dB = seamless."""
    x = read_audio(path, mono=True)
    d = np.abs(np.diff(x))
    typical = np.percentile(d, 99) + 1e-9
    jump = abs(x[0] - x[-1])
    return 20 * np.log10(jump / typical + 1e-9)


def seam_rank(path: Path) -> float:
    """Percentile rank of the wrap-around step among all sample steps (100 = biggest jump in the file)."""
    x = read_audio(path, mono=True)
    d = np.abs(np.diff(x))
    return float(np.mean(d < abs(x[0] - x[-1])) * 100)


def check(path: Path, loop: bool) -> dict:
    p = probe(path)
    m = ebur128(path)
    r = {"file": str(path.relative_to(OUT.parent)), "codec": p["codec_name"], "sr": int(p["sample_rate"]),
         "ch": p["channels"], "dur": round(float(p.get("duration", 0)), 3), "I": m["I"], "TP": m["TP"], "LRA": m["LRA"],
         "bytes": path.stat().st_size}
    if loop:
        x = read_audio(path, mono=True)
        r["samples"] = len(x)
        r["seam_db"] = round(seam_db(path), 1)
        r["seam_pct"] = round(seam_rank(path), 3)
    return r


def main(folders: list[str]) -> None:
    folders = folders or ["sfx", "ambience", "music", "babble"]
    old = json.loads(REPORT.read_text()) if REPORT.exists() else {}
    for fo in folders:
        files = sorted((OUT / fo).glob("*.wav")) + sorted((OUT / fo).glob("*.ogg"))
        loop = fo in ("ambience", "music")
        with ThreadPoolExecutor(8) as ex:
            res = list(ex.map(lambda f: check(f, loop or "call_ring" in f.name), files))
        old[fo] = res
        print(f"== {fo}: {len(res)} files, {sum(r['bytes'] for r in res) / 1e6:.1f} MB")
        for r in res:
            extra = f" samples={r['samples']} seam={r['seam_db']}dB rank={r['seam_pct']}%" if "samples" in r else ""
            print(f"  {Path(r['file']).name:42} {r['codec']:9} {r['sr']} ch{r['ch']} {r['dur']:7.2f}s  I={r['I']!s:>6} LUFS  TP={r['TP']!s:>6}{extra}")
    REPORT.write_text(json.dumps(old, indent=2))


if __name__ == "__main__":
    main(sys.argv[1:])
