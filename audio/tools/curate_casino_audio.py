#!/usr/bin/env python3
"""The Rug Room's sounds (R3, 2026-10-24): real recordings only, all CC0.

Freesound HQ previews (freesound.py: originals need a login; previews carry the same licence, re-checked on download) and
Kenney Casino Audio (CC0, already in LICENSES.md). One-shots are cut at their event and peak-normalised to -1 dBFS; loops
take the steadiest stretch with an equal-power crossfade (curate_vehicle_audio's helpers); the floor ambience is two casino
field recordings layered, 30 s, seamless, at the ambience level. Writes audio/processed/{sfx,ambience}/, registers the ids
in sfx-map.json (audio/ and client/Assets/Resources/Audio/, kept identical), copies the WAVs into client Resources and
appends the sources to LICENSES.md.

Usage (repo root): uv run --no-project --with numpy==2.4.2 --with scipy==1.18.1 --with soundfile==0.13.1 \
                     python -I audio/tools/curate_casino_audio.py
"""
import json
import shutil
import sys
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy.signal import resample_poly

TOOLS = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOLS))
import curate_vehicle_audio as V  # noqa: E402  (load, rms, steadiest, onsets; SR = 48 kHz)

AUDIO = TOOLS.parent
REPO = AUDIO.parent
CLIENT = REPO / "client" / "Assets" / "Resources" / "Audio"
KENNEY = AUDIO / "raw" / "kenney" / "casino" / "Audio"
SR = V.SR
TODAY = "2026-10-24"

# id: (source, how, args, desc, volume_db)
#   source: freesound id or 'kenney:<file>'; how: cut (t0, t1) | loop (seconds, search window t0, t1) | whole
CUES = {
    "sfx_slot_lever": (696746, "whole", None, "slot machine handle pulled: a real lever with its two clacks", -4),
    "sfx_slot_spin": (499767, "loop", (1.6, 3.3, 7.6), "slot reels spinning: a real machine's whir in a small casino (loop)", -10),
    "sfx_slot_stop": (204487, "cut", (2.46, 2.74), "a reel stopping: a real slot machine's clack", -4),
    "sfx_slot_win": (709925, "cut", (1.36, 3.6), "a win: a real desk bell ding", -6),
    "sfx_slot_bigwin": (320006, "cut", (45.3, 51.3), "a big win: real coins pouring from a machine's tray", -5),
    "sfx_chips": ("kenney:chips-stack-4.ogg", "whole", None, "casino chips stacked (stake placed)", -4),
    "sfx_coin_flip": (587022, "cut", (0.0, 0.75), "a real coin flicked off the thumb, ringing", -4),
    "sfx_coin_land": (452867, "cut", (8.94, 9.7), "a real coin landing on a table", -4),
    "sfx_rug_pull": (708090, "cut", (0.42, 1.55), "a rug yanked and shaken out: real cloth whumps", -3),
    "sfx_cashout_ok": (614833, "whole", None, "a clean crystal bell (a cash-out-free 'done' chime)", -6),
}
AMBIENCE = {
    "rug_room": ([(277444, 0.0), (438129, -7.0)], 30.0, "Casino floor: a real casino field recording (Phoenix AZ) with a second casino room layered"),
}
AMB_RMS_DB = -27.0     # about -24 LUFS for a broadband crowd bed (the project's ambience target)


def load_any(src):
    if isinstance(src, str) and src.startswith("kenney:"):
        f = KENNEY / src.split(":", 1)[1]
        x, sr = sf.read(str(f), always_2d=True)
        x = x.mean(axis=1)
        if sr != SR:
            g = np.gcd(sr, SR)
            x = resample_poly(x, SR // g, sr // g)
        meta = {"title": "Casino Audio — " + f.name, "author": "Kenney (Kenney Vleugels)", "url": "https://kenney.nl/assets/casino-audio",
                "license": "CC0-1.0"}
        return x.astype(np.float64), meta
    return V.load(int(src))


def peak_norm(seg, db=-1.0):
    seg = seg - seg.mean()
    return seg * (10 ** (db / 20) / (np.abs(seg).max() + 1e-9))


def fades(seg, a=0.004, b=0.08):
    na, nb = int(a * SR), int(b * SR)
    if len(seg) > na + nb:
        seg[:na] *= np.linspace(0, 1, na)
        seg[-nb:] *= np.linspace(1, 0, nb)
    return seg


def make_cue(cid, spec):
    src, how, args, desc, vol = spec
    x, meta = load_any(src)
    edit = {}
    if how == "whole":
        y = fades(x.copy())
        edit["window_s"] = [0, round(len(x) / SR, 3)]
    elif how == "cut":
        t0, t1 = args
        y = fades(x[int(t0 * SR):int(t1 * SR)].copy())
        edit["window_s"] = [t0, t1]
    else:   # loop from the steadiest stretch inside a window
        sec, t0, t1 = args
        sub = x[int(t0 * SR):int(t1 * SR)]
        y, e = V.loop(sub, sec)
        edit.update(e); edit["search_window_s"] = [t0, t1]
    y = np.clip(peak_norm(y), -1, 1)
    return y, meta, edit, desc, vol, how == "loop"


def make_ambience(key, spec):
    layers, sec, desc = spec
    n = int(sec * SR); f = int(0.5 * SR)
    mix = np.zeros(n + f)
    metas = []
    for sid, gain_db in layers:
        x, meta = V.load(sid)
        s = V.steadiest(x, sec + 0.5)
        seg = x[s:s + n + f]
        if len(seg) < n + f:
            seg = np.pad(seg, (0, n + f - len(seg)))
        seg = seg - seg.mean()
        seg = seg / (V.rms(seg) + 1e-9) * 10 ** (gain_db / 20)
        mix += seg
        metas.append((sid, meta, round(s / SR, 2)))
    t = np.linspace(0, 1, f)
    mix[:f] = mix[:f] * np.sqrt(t) + mix[n:n + f] * np.sqrt(1 - t)
    y = mix[:n]
    y = y * (10 ** (AMB_RMS_DB / 20) / V.rms(y))
    # stereo: the left as is, the right 11 ms later (a wider room without phasing a mono source too hard)
    d = int(0.011 * SR)
    st = np.stack([y, np.concatenate([y[-d:], y[:-d]])], axis=1)
    return np.clip(st, -1, 1), metas, desc


def main():
    for d in ("sfx", "ambience"):
        (AUDIO / "processed" / d).mkdir(parents=True, exist_ok=True)
        (CLIENT / d).mkdir(parents=True, exist_ok=True)
    smap = json.loads((AUDIO / "sfx-map.json").read_text())
    lic_rows = []
    for cid, spec in CUES.items():
        y, meta, edit, desc, vol, is_loop = make_cue(cid, spec)
        rel = f"sfx/{cid}.wav"
        sf.write(str(AUDIO / "processed" / rel), y.astype(np.float32), SR, subtype="PCM_16")
        shutil.copy(AUDIO / "processed" / rel, CLIENT / rel)
        src = spec[0]
        page = meta.get("url")
        smap["sfx"][cid] = {
            "files": [rel], "volume": round(10 ** (vol / 20), 3), "pitchJitter": 0.0 if is_loop else 0.03, "category": "casino",
            "volume_db": vol, "channels": 1, "loop": is_loop, "duration_s": round(len(y) / SR, 3), "true_peak_dbtp": -1.0,
            "ducks_music": False, "status": "recorded", "desc": desc,
            "sources": [f"{meta.get('title')} — {meta.get('author')}" + ("" if isinstance(src, str) else f" (freesound {src})")],
            "license": "CC0-1.0", "license_url": "https://creativecommons.org/publicdomain/zero/1.0/", "source_page": page,
            "source_pages": [page], "license_checked": TODAY,
            "processing": f"{json.dumps(edit)}; mono; 48 kHz; peak-normalised to -1 dBFS",
            "unity_import": {"loadType": "DecompressOnLoad", "compressionFormat": "Vorbis", "quality": 0.6, "preloadAudioData": True},
        }
        smap["index"][cid] = f"sfx.{cid}"
        lic_rows.append((cid, meta, src))
        print(cid, src, meta.get("license"), meta.get("title"), edit)
    for key, spec in AMBIENCE.items():
        y, metas, desc = make_ambience(key, spec)
        rel = f"ambience/amb_{key}.wav"
        sf.write(str(AUDIO / "processed" / rel), y.astype(np.float32), SR, subtype="PCM_16")
        shutil.copy(AUDIO / "processed" / rel, CLIENT / rel)
        smap["ambience"][key] = {
            "id": f"amb_{key}", "file": rel, "volume": 1.0, "category": "ambience", "venue_status": "walk-in room (R3)", "channels": 2,
            "loop": True, "loop_start_sample": 0, "loop_end_sample": len(y), "duration_s": round(len(y) / SR, 3),
            "loudness_lufs": -24.0, "true_peak_dbtp": round(20 * np.log10(np.abs(y).max() + 1e-9), 1), "status": "recorded", "desc": desc,
            "sources": [f"raw/freesound/{sid}.ogg" for sid, _, _ in metas],
            "processing": "; ".join(f"freesound {sid} from {s} s" for sid, _, s in metas) + f"; {spec[1]} s, 0.5 s equal-power loop crossfade, RMS {AMB_RMS_DB} dBFS, right channel +11 ms",
            "unity_import": {"loadType": "Streaming", "compressionFormat": "Vorbis", "quality": 0.35, "preloadAudioData": False, "loadInBackground": True},
        }
        smap["index"][f"amb_{key}"] = f"ambience.{key}"
        for sid, meta, _ in metas:
            lic_rows.append((f"amb_{key}", meta, sid))
        print(f"amb_{key}", [m[0] for m in metas])
    txt = json.dumps(smap, indent=2, ensure_ascii=False) + "\n"
    (AUDIO / "sfx-map.json").write_text(txt)
    (CLIENT / "sfx-map.json").write_text(txt)
    lic = (AUDIO / "LICENSES.md").read_text()
    if "## The Rug Room (R3" not in lic:
        rows = ["", "## The Rug Room (R3, 2026-10-24): casino sounds, real recordings, CC0", "",
                "| id | title | author | source | licence | checked |", "| --- | --- | --- | --- | --- | --- |"]
        for cid, meta, src in lic_rows:
            rows.append(f"| {cid} | {meta.get('title')} | {meta.get('author')} | {meta.get('url')} | CC0 1.0 | {TODAY} |")
        (AUDIO / "LICENSES.md").write_text(lic.rstrip() + "\n" + "\n".join(rows) + "\n")
    print("sfx-map + LICENSES updated")


if __name__ == "__main__":
    main()
