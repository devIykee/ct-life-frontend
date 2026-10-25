#!/usr/bin/env python3
"""Rekt Burger's sounds (2026-10-25): real recordings only, all CC0 (Freesound HQ previews, licence re-checked on
download by freesound.py). One-shots are cut round their event (auto: from just before the first onset to where the level
falls 40 dB under the peak, capped) and peak-normalised; the grill loop takes the steadiest stretch of a real sizzle with an
equal-power crossfade; the restaurant ambience layers a real fast-food dining room, a restaurant walla and a quiet kitchen
sizzle, 30 s seamless. Registers the ids in sfx-map.json (audio/ and client Resources, kept identical), copies the WAVs
into client Resources and appends the sources to LICENSES.md. Helpers: curate_casino_audio / curate_vehicle_audio.

Usage (repo root): uv run --no-project --with numpy==2.4.2 --with scipy==1.18.1 --with soundfile==0.13.1 \
                     python -I audio/tools/curate_rekt_audio.py
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
import curate_vehicle_audio as V  # noqa: E402

AUDIO, CLIENT, SR = C.AUDIO, C.CLIENT, C.SR
TODAY = "2026-10-25"

# id: (freesound id, how, args, desc, volume_db); how: auto (max seconds) | loop (seconds, window t0, t1) | whole
CUES = {
    "sfx_grill_sizzle": (873540, "loop", (4.0, 2.0, 28.0), "a flat-top sizzle: real sausages frying (loop)", -12),
    "sfx_fryer_drop": (637625, "auto", 2.5, "a fryer basket dropped into oil: a real kitchen fryer basket clack", -5),
    "sfx_order_bell": (149269, "auto", 2.5, "order up: a real counter bell ding", -6),
    "sfx_ticket_print": (345057, "auto", 2.8, "a new ticket: a real receipt printer", -7),
    "sfx_tray_slide": (590084, "auto", 1.6, "a tray served: a real plate slid across a table", -6),
    "sfx_order_wrong": (146977, "whole", None, "a wrong order: a soft real desk thud", -6),
}
AMBIENCE = {
    "rekt_burger": ([(405148, 0.0), (792481, -6.0), (873539, -15.0)], 30.0,
                    "Rekt Burger: a real fast-food dining room, a restaurant walla and a quiet kitchen sizzle layered"),
}


def auto_cut(x, cap):
    env = np.abs(x)
    k = int(0.01 * SR)
    env = np.convolve(env, np.ones(k) / k, mode="same")
    peak = env.max() + 1e-12
    on = np.argmax(env > peak * 10 ** (-30 / 20))
    s = max(0, on - int(0.01 * SR))
    e = min(len(x), s + int(cap * SR))
    tail = env[s:e]
    above = np.where(tail > peak * 10 ** (-40 / 20))[0]
    if len(above):
        e = min(e, s + above[-1] + int(0.05 * SR))
    return s, e


def make_cue(cid, spec):
    src, how, args, desc, vol = spec
    x, meta = V.load(int(src))
    edit = {}
    if how == "auto":
        s, e = auto_cut(x, args)
        y = C.fades(x[s:e].copy(), b=0.05)
        edit["window_s"] = [round(float(s) / SR, 3), round(float(e) / SR, 3)]
    elif how == "whole":
        y = C.fades(x.copy())
        edit["window_s"] = [0, round(len(x) / SR, 3)]
    else:
        sec, t0, t1 = args
        y, e = V.loop(x[int(t0 * SR):int(t1 * SR)], sec)
        edit.update(e); edit["search_window_s"] = [t0, t1]
    return np.clip(C.peak_norm(y), -1, 1), meta, edit, desc, vol, how == "loop"


def main():
    smap = json.loads((AUDIO / "sfx-map.json").read_text())
    rows = []
    for cid, spec in CUES.items():
        y, meta, edit, desc, vol, is_loop = make_cue(cid, spec)
        rel = f"sfx/{cid}.wav"
        sf.write(str(AUDIO / "processed" / rel), y.astype(np.float32), SR, subtype="PCM_16")
        shutil.copy(AUDIO / "processed" / rel, CLIENT / rel)
        page = meta.get("url")
        smap["sfx"][cid] = {
            "files": [rel], "volume": round(10 ** (vol / 20), 3), "pitchJitter": 0.0 if is_loop else 0.03, "category": "job",
            "volume_db": vol, "channels": 1, "loop": is_loop, "duration_s": round(len(y) / SR, 3), "true_peak_dbtp": -1.0,
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
    for key, spec in AMBIENCE.items():
        y, metas, desc = C.make_ambience(key, spec)
        rel = f"ambience/amb_{key}.wav"
        sf.write(str(AUDIO / "processed" / rel), y.astype(np.float32), SR, subtype="PCM_16")
        shutil.copy(AUDIO / "processed" / rel, CLIENT / rel)
        smap["ambience"][key] = {
            "id": f"amb_{key}", "file": rel, "volume": 1.0, "category": "ambience", "venue_status": "walk-in room (Rekt Burger)", "channels": 2,
            "loop": True, "loop_start_sample": 0, "loop_end_sample": len(y), "duration_s": round(len(y) / SR, 3),
            "loudness_lufs": -24.0, "true_peak_dbtp": round(20 * np.log10(np.abs(y).max() + 1e-9), 1), "status": "recorded", "desc": desc,
            "sources": [f"raw/freesound/{sid}.ogg" for sid, _, _ in metas],
            "processing": "; ".join(f"freesound {sid} from {s} s" for sid, _, s in metas) + f"; {spec[1]} s, 0.5 s equal-power loop crossfade, RMS {C.AMB_RMS_DB} dBFS, right channel +11 ms",
            "unity_import": {"loadType": "Streaming", "compressionFormat": "Vorbis", "quality": 0.35, "preloadAudioData": False, "loadInBackground": True},
        }
        smap["index"][f"amb_{key}"] = f"ambience.{key}"
        for sid, meta, _ in metas:
            rows.append((f"amb_{key}", meta))
        print(f"amb_{key}", [m[0] for m in metas])
    txt = json.dumps(smap, indent=2, ensure_ascii=False) + "\n"
    (AUDIO / "sfx-map.json").write_text(txt)
    (CLIENT / "sfx-map.json").write_text(txt)
    lic = (AUDIO / "LICENSES.md").read_text()
    if "## Rekt Burger (2026-10-25" not in lic:
        out = ["", "## Rekt Burger (2026-10-25): restaurant and kitchen sounds, real recordings, CC0", "",
               "| id | title | author | source | licence | checked |", "| --- | --- | --- | --- | --- | --- |"]
        for cid, meta in rows:
            out.append(f"| {cid} | {meta.get('title')} | {meta.get('author')} | {meta.get('url')} | CC0 1.0 | {TODAY} |")
        (AUDIO / "LICENSES.md").write_text(lic.rstrip() + "\n" + "\n".join(out) + "\n")
    print("sfx-map + LICENSES updated")


if __name__ == "__main__":
    main()
